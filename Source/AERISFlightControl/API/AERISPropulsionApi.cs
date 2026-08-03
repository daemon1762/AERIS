using System;
using System.Collections.Generic;
using UnityEngine;

namespace AERISFlightControl.API
{
    // Public, intentionally small contract for propulsion-specialist mods such as AutoPropPitch.
    // AERIS remains the authority for safety demand; providers report whether that demand can become real thrust.
    public enum PropulsionAvailabilityReason
    {
        None,
        NoControllablePropulsion,
        MotorInactive,
        PropellerNotDeployed,
        ElectricChargeDepleted,
        IntakeOrResourceStarved,
        EngineFlameout,
        ProviderFault,
        Unknown
    }

    public struct AERISPropulsionRequest
    {
        public float RequiredThrottle;
        // Canonical unit: kN. Kept alongside the legacy N field during migration.
        public float RequiredForwardThrustkN;
        [Obsolete("Use RequiredForwardThrustkN. This field is N.")]
        public float RequiredForwardThrustN;
        public bool StallWarning;
        public bool StallRisk;
        public bool StallDetected;
        // True while AERIS speed hold owns propulsion demand, including zero-throttle deceleration.
        public bool AutopilotSpeedHold;
        // Desired surface speed for propulsion-specialist speed controllers. 0 means no speed target.
        public float TargetSpeedMps;
    }

    public struct AERISPropulsionStatus
    {
        public bool HasControllablePropulsion;
        public bool PropulsionReady;
        public bool PropulsionUnavailable;
        // Canonical unit: kN. Legacy N fields remain for one compatibility cycle.
        public float ActualAvailableForwardThrustkN;
        public float EstimatedAvailableForwardThrustkN;
        [Obsolete("Use ActualAvailableForwardThrustkN. This field is N.")]
        public float ActualAvailableForwardThrustN;
        [Obsolete("Use EstimatedAvailableForwardThrustkN. This field is N.")]
        public float EstimatedAvailableForwardThrustN;
        public float MotorResponse01;
        public PropulsionAvailabilityReason Reason;
        public string Detail;
    }

    // AERIS publishes demand only. Propulsion extensions consume it from their own update loop.
    public struct AERISPropulsionDemand
    {
        public uint VesselPersistentId;
        public AERISPropulsionRequest Request;
        public bool Active;
        public float PublishedRealtime;
        // Monotonic publication sequence for diagnostics / consumer stale-demand detection.
        public uint Sequence;
        // Empty means broadcast/backward-compatible. Non-empty addresses one provider.
        public string TargetProviderId;
    }

    public static class AERISPropulsionDemandBus
    {
        static AERISPropulsionDemand latest;
        static uint sequence;
        static readonly object sync = new object();
        public static float LatestDemandPercent
        {
            get
            {
                lock (sync)
                    return latest.Active ? Mathf.Clamp01(latest.Request.RequiredThrottle) * 100f : 0f;
            }
        }
        public static void Publish(Vessel vessel, AERISPropulsionRequest request)
        {
            Publish(vessel, request, null);
        }
        public static void Publish(Vessel vessel, AERISPropulsionRequest request, string targetProviderId)
        {
            request = SanitizeRequest(request);
            string target = (targetProviderId ?? string.Empty).Trim();
            if (target.Length > 128) target = string.Empty;
            lock (sync)
            {
                latest = new AERISPropulsionDemand
                {
                    VesselPersistentId = vessel == null ? 0u : vessel.persistentId,
                    Request = request,
                    Active = vessel != null,
                    PublishedRealtime = Time.realtimeSinceStartup,
                    Sequence = ++sequence,
                    TargetProviderId = target
                };
            }
        }
        public static void Clear(Vessel vessel)
        {
            lock (sync)
                if (vessel == null || latest.VesselPersistentId == vessel.persistentId)
                    latest = new AERISPropulsionDemand();
        }
        public static bool TryGet(Vessel vessel, out AERISPropulsionDemand demand)
        {
            lock (sync) demand = latest;
            float age = Time.realtimeSinceStartup - demand.PublishedRealtime;
            return vessel != null && demand.Active &&
                demand.VesselPersistentId == vessel.persistentId &&
                age >= 0f && age < 0.75f;
        }

        internal static AERISPropulsionRequest SanitizeRequest(AERISPropulsionRequest request)
        {
            request.RequiredThrottle = Finite(request.RequiredThrottle)
                ? Mathf.Clamp01(request.RequiredThrottle) : 0f;
            float canonicalKn = Finite(request.RequiredForwardThrustkN) &&
                request.RequiredForwardThrustkN >= 0f
                ? request.RequiredForwardThrustkN : 0f;
#pragma warning disable 618
            if (canonicalKn <= 0f && Finite(request.RequiredForwardThrustN) &&
                request.RequiredForwardThrustN > 0f)
                canonicalKn = request.RequiredForwardThrustN / 1000f;
            request.RequiredForwardThrustkN = canonicalKn;
            request.RequiredForwardThrustN = canonicalKn * 1000f;
#pragma warning restore 618
            request.TargetSpeedMps = Finite(request.TargetSpeedMps)
                ? Mathf.Max(0f, request.TargetSpeedMps) : 0f;
            return request;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public interface IAERISPropulsionProvider
    {
        string ProviderId { get; }
        bool TryGetStatus(Vessel vessel, AERISPropulsionRequest request, out AERISPropulsionStatus status);
    }

    // Optional metadata for deterministic selection when several propulsion mods are installed.
    // Higher priority wins; ties are resolved by ProviderId ordinal order.
    public interface IAERISPropulsionProviderMetadata
    {
        int SelectionPriority { get; }
        string DisplayName { get; }
    }

    public static class AERISPropulsionBridge
    {
        sealed class ProviderRecord
        {
            internal IAERISPropulsionProvider Provider;
            internal string Id;
            internal int Priority;
        }

        static readonly List<ProviderRecord> providers = new List<ProviderRecord>();
        static readonly object sync = new object();

        // Backward-compatible view. New code should select by vessel/request.
        public static IAERISPropulsionProvider Provider
        {
            get
            {
                lock (sync)
                {
                    SortProviders();
                    return providers.Count == 0 ? null : providers[0].Provider;
                }
            }
        }
        public static int ProviderCount { get { lock (sync) return providers.Count; } }

        public static void Register(IAERISPropulsionProvider value)
        {
            if (value == null) return;
            string id = SafeProviderId(value);
            if (string.IsNullOrEmpty(id)) return;
            int priority = SafePriority(value);
            lock (sync)
            {
                for (int i = 0; i < providers.Count; i++)
                    if (ReferenceEquals(providers[i].Provider, value) ||
                        string.Equals(providers[i].Id, id, StringComparison.Ordinal))
                        return;
                providers.Add(new ProviderRecord { Provider = value, Id = id,
                    Priority = priority });
                SortProviders();
            }
        }
        public static void Unregister(IAERISPropulsionProvider value)
        {
            if (value == null) return;
            lock (sync)
                for (int i = providers.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(providers[i].Provider, value))
                        providers.RemoveAt(i);
        }

        public static string[] GetRegisteredProviderIds()
        {
            lock (sync)
            {
                SortProviders();
                var result = new string[providers.Count];
                for (int i = 0; i < providers.Count; i++)
                    result[i] = providers[i].Id;
                return result;
            }
        }

        public static bool TrySelectProvider(Vessel vessel, AERISPropulsionRequest request,
            out string providerId, out AERISPropulsionStatus status)
        {
            providerId = string.Empty;
            status = new AERISPropulsionStatus();
            if (vessel == null) return false;
            request = AERISPropulsionDemandBus.SanitizeRequest(request);
            ProviderRecord[] snapshot;
            lock (sync)
            {
                SortProviders();
                snapshot = providers.ToArray();
            }
            for (int i = 0; i < snapshot.Length; i++)
            {
                var candidate = snapshot[i];
                AERISPropulsionStatus candidateStatus;
                try
                {
                    if (!candidate.Provider.TryGetStatus(vessel, request, out candidateStatus)) continue;
                }
                catch { continue; }
                SanitizeStatus(ref candidateStatus);
                if (!candidateStatus.HasControllablePropulsion) continue;
                providerId = candidate.Id;
                status = candidateStatus;
                return true;
            }
            return false;
        }

        public static bool TryGetStatus(string providerId, Vessel vessel,
            AERISPropulsionRequest request, out AERISPropulsionStatus status)
        {
            status = new AERISPropulsionStatus();
            if (vessel == null || string.IsNullOrEmpty(providerId)) return false;
            request = AERISPropulsionDemandBus.SanitizeRequest(request);
            ProviderRecord candidate = null;
            lock (sync)
            {
                for (int i = 0; i < providers.Count; i++)
                    if (string.Equals(providers[i].Id, providerId, StringComparison.Ordinal))
                    { candidate = providers[i]; break; }
            }
            if (candidate == null) return false;
            try
            {
                if (!candidate.Provider.TryGetStatus(vessel, request, out status)) return false;
                SanitizeStatus(ref status);
                return true;
            }
            catch { return false; }
        }

        public static bool TryGetStatus(Vessel vessel, AERISPropulsionRequest request, out AERISPropulsionStatus status)
        {
            string ignored;
            return TrySelectProvider(vessel, request, out ignored, out status);
        }

        static string SafeProviderId(IAERISPropulsionProvider provider)
        {
            if (provider == null) return string.Empty;
            string id = null;
            try { id = provider.ProviderId; } catch { }
            if (string.IsNullOrEmpty(id)) id = provider.GetType().FullName;
            id = (id ?? string.Empty).Trim();
            return id.Length <= 128 ? id : string.Empty;
        }
        static int SafePriority(IAERISPropulsionProvider provider)
        {
            var metadata = provider as IAERISPropulsionProviderMetadata;
            if (metadata == null) return 0;
            try { return metadata.SelectionPriority; } catch { return 0; }
        }

        static void SanitizeStatus(ref AERISPropulsionStatus status)
        {
            float actualKn = FinitePositive(status.ActualAvailableForwardThrustkN);
            float estimatedKn = FinitePositive(status.EstimatedAvailableForwardThrustkN);
#pragma warning disable 618
            if (actualKn <= 0f)
                actualKn = FinitePositive(status.ActualAvailableForwardThrustN) / 1000f;
            if (estimatedKn <= 0f)
                estimatedKn = FinitePositive(status.EstimatedAvailableForwardThrustN) / 1000f;
            status.ActualAvailableForwardThrustkN = actualKn;
            status.EstimatedAvailableForwardThrustkN = estimatedKn;
            status.ActualAvailableForwardThrustN = actualKn * 1000f;
            status.EstimatedAvailableForwardThrustN = estimatedKn * 1000f;
#pragma warning restore 618
            status.MotorResponse01 = Finite(status.MotorResponse01)
                ? Mathf.Clamp01(status.MotorResponse01) : 0f;
            if (!status.HasControllablePropulsion)
                status.PropulsionReady = false;
            if (status.PropulsionUnavailable)
                status.PropulsionReady = false;
            if (status.PropulsionReady)
            {
                status.HasControllablePropulsion = true;
                status.PropulsionUnavailable = false;
                status.Reason = PropulsionAvailabilityReason.None;
            }
            if (status.Detail != null && status.Detail.Length > 512)
                status.Detail = status.Detail.Substring(0, 512);
        }

        static float FinitePositive(float value)
        {
            return Finite(value) && value > 0f ? value : 0f;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static void SortProviders()
        {
            providers.Sort(delegate(ProviderRecord a, ProviderRecord b)
            {
                int priority = b.Priority.CompareTo(a.Priority);
                return priority != 0 ? priority : string.CompareOrdinal(a.Id, b.Id);
            });
        }
    }
}
