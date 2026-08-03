using System.Collections.Generic;
using UnityEngine;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;
using AERISFlightControl.Protect;

namespace AERISFlightControl.Autopilot
{
    // SPEED owns only the deployment state of control surfaces explicitly assigned to
    // KSP's Brakes action group. Wheel brakes and FlightCtrlState are never written here.
    // The original per-surface deploy state and angle are restored on every release path.
    internal sealed class AERISSpeedAirbrakeController
    {
        sealed class SurfaceSnapshot
        {
            internal ModuleControlSurface Surface;
            internal bool Deploy;
            internal float DeployAngle;
        }

        readonly List<SurfaceSnapshot> owned = new List<SurfaceSnapshot>();
        Vessel ownedVessel;
        float lastUpdateFixedTime;
        float appliedDemand;
        bool surfaceScanResolved;
        float nextSurfaceRetryRealtime;
        int lastLoggedSurfaceCount = -1;

        internal bool Enabled { get; set; }
        internal bool Active { get; private set; }
        internal bool GroundAssistActive { get; private set; }
        internal float RequestedDemand { get; private set; }
        internal float AppliedDemand { get { return appliedDemand; } }
        internal float DynamicPressureLimitScale { get; private set; }
        internal float AppliedAngleDegrees { get; private set; }
        internal int EligibleSurfaceCount { get; private set; }
        internal string Status { get; private set; } = "OFF";

        internal float DemandRisePerSecond = 0.75f;
        internal float DemandReleasePerSecond = 1.80f;
        internal float HighQDerateStartKpa = 60f;
        internal float HighQDerateFullKpa = 140f;
        internal float HighQMinimumDemandScale = 0.25f;
        internal float HardQReleaseKpa = 180f;

        internal void Update(Vessel vessel, AERISAccelerationDirector acceleration,
            VirtualAttitudeInstrument attitude, ProtectTelemetry protect,
            bool master, bool standardFbwActive)
        {
            // Ground Assist publishes later in the same AA control pass. If it owned the
            // surfaces on the previous pass, do not restore/rescan them in the airborne SPEED path.
            if (GroundAssistActive) return;
            float now = Time.fixedTime;
            float dt = lastUpdateFixedTime > 0f
                ? Mathf.Clamp(now - lastUpdateFixedTime, 0.001f, 0.25f)
                : Time.fixedDeltaTime;
            if (!IsFinite(dt)) dt = 0.02f;
            lastUpdateFixedTime = now;

            bool validFlight = Enabled && master && standardFbwActive && vessel != null &&
                !vessel.packed && !vessel.LandedOrSplashed &&
                vessel.situation != Vessel.Situations.PRELAUNCH &&
                attitude != null && attitude.InstrumentValid &&
                acceleration != null && acceleration.Armed && acceleration.ControlActive;

            bool genuineStallRecovery = protect != null && protect.ProtectActive &&
                !protect.DecelerationThrustInhibitActive;
            if (!validFlight || genuineStallRecovery)
            {
                Release(genuineStallRecovery
                    ? "PROTECT recovery priority" : (Enabled ? "standby" : "disabled"));
                return;
            }

            float qKpa = IsFinite(attitude.DynamicPressureKpa)
                ? Mathf.Max(0f, attitude.DynamicPressureKpa) : 0f;
            float qDerate = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(HighQDerateStartKpa, HighQDerateFullKpa, qKpa));
            DynamicPressureLimitScale = qKpa >= HardQReleaseKpa
                ? 0f
                : Mathf.Lerp(1f, HighQMinimumDemandScale, qDerate);
            RequestedDemand = Clamp01Finite(acceleration.AirbrakeDemand) * DynamicPressureLimitScale;

            if (RequestedDemand <= 0.001f)
            {
                appliedDemand = Mathf.MoveTowards(appliedDemand, 0f, DemandReleasePerSecond * dt);
                if (appliedDemand <= 0.001f) Release(acceleration.CoastLimited
                    ? "deceleration shortfall cleared" : "coast authority not exhausted");
                else ApplyOwnedSurfaces(appliedDemand);
                return;
            }

            if (ownedVessel != vessel)
            {
                Release("vessel/surface rescan");
                surfaceScanResolved = false;
            }
            if (!surfaceScanResolved || (owned.Count == 0 && Time.realtimeSinceStartup >= nextSurfaceRetryRealtime))
                CaptureEligibleSurfaces(vessel);
            if (owned.Count == 0)
            {
                appliedDemand = 0f;
                Status = "NO BRAKES-GROUP AIRBRAKE SURFACES";
                return;
            }

            appliedDemand = Mathf.MoveTowards(appliedDemand, RequestedDemand,
                (RequestedDemand > appliedDemand ? DemandRisePerSecond : DemandReleasePerSecond) * dt);
            ApplyOwnedSurfaces(appliedDemand);
            Active = appliedDemand > 0.01f;
            Status = Active
                ? "AUTO MODULATING — " + (appliedDemand * 100f).ToString("F0") + "%"
                : "ARMED / STANDBY";
        }


        internal void UpdateGroundAssist(Vessel vessel, float requestedDemand, float configuredLimit, bool enabled)
        {
            float now = Time.fixedTime;
            float dt = lastUpdateFixedTime > 0f
                ? Mathf.Clamp(now - lastUpdateFixedTime, 0.001f, 0.25f)
                : Time.fixedDeltaTime;
            if (!IsFinite(dt)) dt = 0.02f;
            lastUpdateFixedTime = now;
            float target = enabled && vessel != null && !vessel.packed
                ? Clamp01Finite(requestedDemand) * Clamp01Finite(configuredLimit) : 0f;
            RequestedDemand = target;
            DynamicPressureLimitScale = 1f;
            if (target <= 0.001f)
            {
                appliedDemand = Mathf.MoveTowards(appliedDemand, 0f, DemandReleasePerSecond * dt);
                if (appliedDemand <= 0.001f)
                {
                    if (GroundAssistActive || owned.Count > 0) Release("Ground Assist demand cleared");
                    GroundAssistActive = false;
                }
                else ApplyOwnedSurfaces(appliedDemand);
                return;
            }
            if (ownedVessel != vessel)
            {
                Release("Ground Assist vessel/surface rescan");
                surfaceScanResolved = false;
            }
            if (!surfaceScanResolved || (owned.Count == 0 && Time.realtimeSinceStartup >= nextSurfaceRetryRealtime))
                CaptureEligibleSurfaces(vessel);
            if (owned.Count == 0)
            {
                appliedDemand = 0f; GroundAssistActive = false;
                Status = "GROUND ASSIST — NO BRAKES-GROUP AIRBRAKE SURFACES";
                return;
            }
            appliedDemand = Mathf.MoveTowards(appliedDemand, target,
                (target > appliedDemand ? DemandRisePerSecond : DemandReleasePerSecond) * dt);
            ApplyOwnedSurfaces(appliedDemand);
            Active = appliedDemand > 0.01f;
            GroundAssistActive = Active;
            Status = Active ? "GROUND ASSIST LINK — " + (appliedDemand * 100f).ToString("F0") + "%"
                : "GROUND ASSIST ARMED";
        }

        void CaptureEligibleSurfaces(Vessel vessel)
        {
            owned.Clear();
            ownedVessel = vessel;
            EligibleSurfaceCount = 0;
            surfaceScanResolved = true;
            nextSurfaceRetryRealtime = Time.realtimeSinceStartup + 2.0f;
            if (vessel == null || vessel.parts == null) return;
            foreach (Part part in vessel.parts)
            {
                if (part == null || part.Modules == null) continue;
                foreach (PartModule module in part.Modules)
                {
                    ModuleControlSurface surface = module as ModuleControlSurface;
                    if (surface == null || !IsFinite(surface.deployAngle) ||
                        !HasBrakesAction(surface)) continue;
                    owned.Add(new SurfaceSnapshot
                    {
                        Surface = surface,
                        Deploy = surface.deploy,
                        DeployAngle = surface.deployAngle
                    });
                }
            }
            EligibleSurfaceCount = owned.Count;
            if (EligibleSurfaceCount != lastLoggedSurfaceCount)
            {
                lastLoggedSurfaceCount = EligibleSurfaceCount;
                AERISLogger.Info("[SPEED][AIRBRAKE] captured " + EligibleSurfaceCount +
                    " Brakes-group control surfaces; wheel brakes remain untouched.");
            }
        }

        static bool HasBrakesAction(ModuleControlSurface surface)
        {
            try
            {
                foreach (BaseAction action in surface.Actions)
                {
                    if (action != null &&
                        (action.actionGroup & KSPActionGroup.Brakes) == KSPActionGroup.Brakes)
                        return true;
                }
            }
            catch { }
            return false;
        }

        void ApplyOwnedSurfaces(float demand)
        {
            float angleSum = 0f;
            int valid = 0;
            for (int i = 0; i < owned.Count; i++)
            {
                SurfaceSnapshot snapshot = owned[i];
                ModuleControlSurface surface = snapshot.Surface;
                if (surface == null || surface.part == null || surface.vessel != ownedVessel) continue;
                float angle = Mathf.Max(0.1f, Mathf.Abs(snapshot.DeployAngle) * Clamp01Finite(demand));
                surface.deployAngle = angle;
                surface.deploy = demand > 0.01f;
                angleSum += angle;
                valid++;
            }
            AppliedAngleDegrees = valid > 0 ? angleSum / valid : 0f;
            EligibleSurfaceCount = valid;
            if (valid == 0 && owned.Count > 0)
            {
                owned.Clear();
                surfaceScanResolved = false;
                nextSurfaceRetryRealtime = Time.realtimeSinceStartup;
            }
        }

        internal void Release(string reason)
        {
            bool wasOwned = owned.Count > 0 || Active;
            for (int i = 0; i < owned.Count; i++)
            {
                SurfaceSnapshot snapshot = owned[i];
                ModuleControlSurface surface = snapshot.Surface;
                if (surface == null || surface.part == null) continue;
                surface.deployAngle = snapshot.DeployAngle;
                surface.deploy = snapshot.Deploy;
            }
            owned.Clear();
            ownedVessel = null;
            surfaceScanResolved = false;
            nextSurfaceRetryRealtime = 0f;
            appliedDemand = 0f;
            RequestedDemand = 0f;
            DynamicPressureLimitScale = 1f;
            AppliedAngleDegrees = 0f;
            EligibleSurfaceCount = 0;
            Active = false;
            GroundAssistActive = false;
            Status = Enabled ? "ARMED / STANDBY" : "OFF";
            if (wasOwned) AERISLogger.Info("[SPEED][AIRBRAKE] released and restored surfaces: " + reason + ".");
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float Clamp01Finite(float value)
        {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        }
    }
}
