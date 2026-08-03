using System;
using System.Collections.Generic;
using UnityEngine;

namespace AERISFlightControl.API
{
    // Semantic, vehicle-agnostic requests. Providers describe intent; AERIS arbitration owns final authority.
    public enum AERISControlChannel
    {
        Pitch, Roll, Yaw, Throttle, Brakes, Gear,
        Collective, VerticalThrust, HoverHold, TranslationForward, TranslationRight,
        RotorRpm, RotorTilt, AntiTorque,
        PropellerDemand, ElectricMotorDemand, JetThrottleDemand, GimbalDemand, ReverseThrustDemand
    }

    public enum AERISControlPriority
    {
        PilotAssist = 10,
        VehicleController = 20,
        Autopilot = 30,
        EnvelopeProtection = 40,
        EmergencyProtection = 50
    }

    public struct AERISControlRequest
    {
        public string ProviderId;
        public AERISControlChannel Channel;
        public float Value;
        public AERISControlPriority Priority;
        public string Reason;
        public bool CanOverridePilot;
        public float PublishedRealtime;
    }

    public interface IAERISControlProvider
    {
        string ProviderId { get; }
        void CollectControlRequests(Vessel vessel, List<AERISControlRequest> requests);
    }

    // Registration only in v0.2.68. Arbitration is intentionally not wired into FlightCtrlState yet.
    // This avoids changing existing fixed-wing/AP/APP behaviour while external contracts stabilize.
    public static class AERISControlProviderRegistry
    {
        const int MaxProviders = 256;
        const int MaxRequestsPerProvider = 128;
        static readonly List<IAERISControlProvider> providers = new List<IAERISControlProvider>();
        static readonly object sync = new object();
        public static void Register(IAERISControlProvider provider)
        {
            if (provider == null) return;
            string id;
            try { id = provider.ProviderId; } catch { return; }
            if (string.IsNullOrEmpty(id) || id.Length > 128) return;
            lock (sync)
                if (!providers.Contains(provider) && providers.Count < MaxProviders)
                    providers.Add(provider);
        }
        public static void Unregister(IAERISControlProvider provider)
        {
            if (provider != null) lock (sync) providers.Remove(provider);
        }
        public static void Collect(Vessel vessel, List<AERISControlRequest> output)
        {
            if (output == null) return;
            IAERISControlProvider[] snapshot;
            lock (sync) snapshot = providers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                var local = new List<AERISControlRequest>(MaxRequestsPerProvider);
                try
                {
                    snapshot[i].CollectControlRequests(vessel, local);
                    int count = Math.Min(local.Count, MaxRequestsPerProvider);
                    for (int j = 0; j < count; j++)
                    {
                        AERISControlRequest request = local[j];
                        if (string.IsNullOrEmpty(request.ProviderId) ||
                            request.ProviderId.Length > 128 || float.IsNaN(request.Value) ||
                            float.IsInfinity(request.Value) ||
                            (int)request.Channel < (int)AERISControlChannel.Pitch ||
                            (int)request.Channel > (int)AERISControlChannel.ReverseThrustDemand ||
                            (int)request.Priority < (int)AERISControlPriority.PilotAssist ||
                            (int)request.Priority > (int)AERISControlPriority.EmergencyProtection)
                            continue;
                        if (request.Reason != null && request.Reason.Length > 512)
                            request.Reason = request.Reason.Substring(0, 512);
                        if (float.IsNaN(request.PublishedRealtime) ||
                            float.IsInfinity(request.PublishedRealtime))
                            request.PublishedRealtime = Time.realtimeSinceStartup;
                        output.Add(request);
                    }
                }
                catch { }
            }
        }
    }

    // Shared AERIS Flight Deviation Instrument extension surface. Providers publish
    // display-only annunciations; they never gain control authority through this API.
    public enum AERISFlightInstrumentPriority
    {
        Information = 10,
        Advisory = 20,
        Intervention = 30,
        Protection = 40,
        Emergency = 50
    }

    public struct AERISFlightInstrumentMessage
    {
        public string ProviderId;
        public string MessageId;
        public string Label;
        public string Value;
        public string Detail;
        public AERISFlightInstrumentPriority Priority;
        public bool Active;
        public float PublishedRealtime;
        public float HoldSeconds;
    }

    public interface IAERISFlightInstrumentProvider
    {
        string ProviderId { get; }
        void CollectFlightInstrumentMessages(Vessel vessel, List<AERISFlightInstrumentMessage> messages);
    }

    public static class AERISFlightInstrumentProviderRegistry
    {
        const int MaxProviders = 256;
        const int MaxMessagesPerProvider = 128;
        static readonly List<IAERISFlightInstrumentProvider> providers = new List<IAERISFlightInstrumentProvider>();
        static readonly object sync = new object();

        public static void Register(IAERISFlightInstrumentProvider provider)
        {
            if (provider == null) return;
            string id;
            try { id = provider.ProviderId; } catch { return; }
            if (string.IsNullOrEmpty(id) || id.Length > 128) return;
            lock (sync)
                if (!providers.Contains(provider) && providers.Count < MaxProviders)
                    providers.Add(provider);
        }

        public static void Unregister(IAERISFlightInstrumentProvider provider)
        {
            if (provider != null) lock (sync) providers.Remove(provider);
        }

        public static void Collect(Vessel vessel, List<AERISFlightInstrumentMessage> output)
        {
            if (output == null) return;
            IAERISFlightInstrumentProvider[] snapshot;
            lock (sync) snapshot = providers.ToArray();
            for (int i = 0; i < snapshot.Length; i++)
            {
                IAERISFlightInstrumentProvider provider = snapshot[i];
                if (provider == null) continue;
                var local = new List<AERISFlightInstrumentMessage>(MaxMessagesPerProvider);
                try
                {
                    provider.CollectFlightInstrumentMessages(vessel, local);
                    int count = Math.Min(local.Count, MaxMessagesPerProvider);
                    for (int j = 0; j < count; j++)
                    {
                        AERISFlightInstrumentMessage message = local[j];
                        if (string.IsNullOrEmpty(message.ProviderId) ||
                            message.ProviderId.Length > 128 ||
                            string.IsNullOrEmpty(message.MessageId) ||
                            message.MessageId.Length > 128 ||
                            (int)message.Priority < (int)AERISFlightInstrumentPriority.Information ||
                            (int)message.Priority > (int)AERISFlightInstrumentPriority.Emergency)
                            continue;
                        message.Label = Bound(message.Label, 128);
                        message.Value = Bound(message.Value, 256);
                        message.Detail = Bound(message.Detail, 512);
                        if (float.IsNaN(message.PublishedRealtime) ||
                            float.IsInfinity(message.PublishedRealtime))
                            message.PublishedRealtime = Time.realtimeSinceStartup;
                        message.HoldSeconds = float.IsNaN(message.HoldSeconds) ||
                            float.IsInfinity(message.HoldSeconds)
                            ? 0f : Mathf.Clamp(message.HoldSeconds, 0f, 10f);
                        output.Add(message);
                    }
                }
                catch { }
            }
        }

        static string Bound(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }
    }

    public enum AERISRecorderSeverity { Verbose, Info, Warning, Error, Critical }

    public struct AERISRecorderTelemetrySchema
    {
        public string ProviderId;
        public string ChannelId;
        public string[] Fields;
        public float RequestedHz;
    }

    public struct AERISRecorderTelemetryFrame
    {
        public Vessel Vessel;
        public string ProviderId;
        public string ChannelId;
        public string[] Values;
    }

    // Public extension point. External mods never write AERIS CSV files directly.
    // AERIS owns serialization, flight folders, rate limiting and disposal.
    public static class AERISFlightRecorderApi
    {
        internal static Action<string, string, string, AERISRecorderSeverity, string> EventSink;
        internal static Func<AERISRecorderTelemetrySchema, bool> SchemaSink;
        internal static Action<AERISRecorderTelemetryFrame> TelemetrySink;

        public static void RecordEvent(string providerId, string category, string action, AERISRecorderSeverity severity, string message)
        {
            var sink = EventSink;
            if (sink == null || string.IsNullOrEmpty(providerId) || providerId.Length > 128 ||
                (int)severity < (int)AERISRecorderSeverity.Verbose ||
                (int)severity > (int)AERISRecorderSeverity.Critical) return;
            providerId = providerId.Trim();
            if (providerId.Length == 0) return;
            category = string.IsNullOrEmpty(category) ? "GENERAL" : Bound(category.Trim(), 128);
            action = string.IsNullOrEmpty(action) ? "EVENT" : Bound(action.Trim(), 128);
            message = Bound(message ?? string.Empty, 4096);
            try { sink(providerId, category, action, severity, message); }
            catch { }
        }

        public static bool RegisterTelemetryChannel(string providerId, string channelId, string[] fields, float requestedHz)
        {
            var sink = SchemaSink;
            if (sink == null || string.IsNullOrEmpty(providerId) || providerId.Length > 128 ||
                string.IsNullOrEmpty(channelId) || channelId.Length > 128 || fields == null ||
                fields.Length == 0 || fields.Length > 128 || float.IsNaN(requestedHz) ||
                float.IsInfinity(requestedHz)) return false;
            providerId = providerId.Trim();
            channelId = channelId.Trim();
            if (providerId.Length == 0 || channelId.Length == 0) return false;
            string[] schemaFields = new string[fields.Length];
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.IsNullOrEmpty(fields[i]) || fields[i].Length > 128) return false;
                schemaFields[i] = fields[i];
            }
            try
            {
                return sink(new AERISRecorderTelemetrySchema { ProviderId = providerId,
                    ChannelId = channelId, Fields = schemaFields,
                    RequestedHz = Mathf.Clamp(requestedHz, 0.2f, 50f) });
            }
            catch { return false; }
        }

        static string Bound(string value, int maximumLength)
        {
            if (value == null) return string.Empty;
            return value.Length <= maximumLength ? value : value.Substring(0, maximumLength);
        }

        public static void PublishTelemetry(Vessel vessel, string providerId, string channelId, string[] values)
        {
            var sink = TelemetrySink;
            if (sink == null || vessel == null || string.IsNullOrEmpty(providerId) ||
                providerId.Length > 128 || string.IsNullOrEmpty(channelId) ||
                channelId.Length > 128 || values == null || values.Length > 128) return;
            providerId = providerId.Trim();
            channelId = channelId.Trim();
            if (providerId.Length == 0 || channelId.Length == 0) return;
            string[] frameValues = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i] ?? string.Empty;
                if (value.Length > 4096) return;
                frameValues[i] = value;
            }
            try { sink(new AERISRecorderTelemetryFrame { Vessel = vessel,
                ProviderId = providerId, ChannelId = channelId, Values = frameValues }); }
            catch { }
        }
    }
}
