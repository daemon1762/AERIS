using UnityEngine;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Autopilot
{
    // VEL is AERIS' upper SPEED director. It converts a requested surface speed into
    // a jerk-limited acceleration trajectory, then hands only that acceleration target
    // to ACC. ACC remains the sole AERIS throttle-demand generator and AA remains the
    // sole final FlightCtrlState.mainThrottle writer.
    internal sealed class AERISVelocityDirector
    {
        internal bool Armed { get; private set; }
        internal bool ControlActive { get; private set; }
        internal bool TargetConfirmed { get; private set; }
        internal bool VelocityErrorValid { get; private set; }
        internal bool VelocityHoldActive { get; private set; }
        internal string ControlState { get; private set; } = "Inactive";

        internal float TargetSurfaceSpeedMps { get; private set; }
        internal string TargetSurfaceSpeedText = "0";
        internal float CurrentSurfaceSpeedMps { get; private set; }
        internal float VelocityErrorMps { get; private set; }
        internal float PredictedVelocityErrorMps { get; private set; }
        internal float MeasuredAccelerationMps2 { get; private set; }
        internal float ProjectedStoppingSpeedLeadMps { get; private set; }
        // Extra speed lead for the measured acceleration that has not yet followed
        // the planned acceleration. This captures throttle/drag response lag without
        // adding a second throttle path to VEL.
        internal float AccelerationTrackingLeadMps { get; private set; }
        internal float DesiredAccelerationMps2 { get; private set; }
        internal float PlannedAccelerationMps2 { get; private set; }
        internal float PublishedAccelerationMps2 { get; private set; }
        internal float DynamicPressureKpa { get; private set; }
        internal float DynamicPressurePlannerScale { get; private set; }
        internal float EffectiveMaxAccelerationMps2 { get; private set; }
        internal float EffectiveMaxDecelerationMps2 { get; private set; }
        internal float EffectiveJerkLimitMps3 { get; private set; }

        // VEL uses one symmetric acceleration-magnitude limit. The 4.0 m/s² default
        // is the AERIS high-performance baseline; it remains explicit and adjustable in
        // the UI / persisted settings. Dynamic-pressure scheduling still reduces the
        // published envelope before ACC receives it.
        internal const float DefaultAccelerationLimitMps2 = 4.0f;
        internal const float MinimumAccelerationLimitMps2 = 0.10f;
        internal const float MaximumAccelerationLimitMps2 = 30.0f;
        // Hypersonic-capable test aircraft require a target range above the prior
        // 2,000 m/s UI gate. This is only a target-validation range; propulsion,
        // thermal, drag and acceleration limits are still observed and logged normally.
        internal float MaximumTargetSurfaceSpeedMps = 5000f;
        internal float ConfiguredAccelerationLimitMps2 { get; private set; } = DefaultAccelerationLimitMps2;
        internal string AccelerationLimitText = "4.0";
        internal float MaximumAccelerationMps2 = DefaultAccelerationLimitMps2;
        internal float MaximumDecelerationMps2 = DefaultAccelerationLimitMps2;
        internal float VelocityErrorGainPerSec = 0.32f;
        internal float AccelerationDamping = 0.72f;
        // v0.7.3: tight VEL hold bands and a deliberate low-amplitude speed trim.
        // ACC's VEL precision path can now execute this small command instead of
        // discarding it in its manual-ACC deadband.
        internal float HoldVelocityErrorGainPerSec = 0.42f;
        internal float HoldAccelerationDamping = 0.85f;
        internal float HoldAccelerationLimitMps2 = 0.16f;
        internal float AccelerationJerkLimitMps3 = 1.80f;
        internal float HoldEnterSpeedBandMps = 0.06f;
        internal float HoldExitSpeedBandMps = 0.18f;
        internal float HoldEnterAccelerationBandMps2 = 0.04f;
        internal float HoldExitAccelerationBandMps2 = 0.12f;
        internal float AccelerationTrackingLeadSeconds = 0.55f;

        float lastSampleFixedTime;
        bool haveSample;

        internal void SetArmed(bool armed, Vessel vessel, VirtualAttitudeInstrument attitude,
            AERISAccelerationDirector acceleration)
        {
            if (Armed == armed) return;
            Armed = armed;
            ResetDynamicState();
            if (armed)
            {
                CurrentSurfaceSpeedMps = attitude != null && attitude.SharedSurfaceSpeedValid &&
                    IsFinite(attitude.SurfaceSpeedMps) ? Mathf.Max(0f, attitude.SurfaceSpeedMps) : 0f;
                PlannedAccelerationMps2 = acceleration != null &&
                    IsFinite(acceleration.FilteredAccelerationMps2)
                    ? acceleration.FilteredAccelerationMps2 : 0f;
                PublishedAccelerationMps2 = PlannedAccelerationMps2;
                ControlState = TargetConfirmed ? "Armed" : "TargetRequired";
                AERISLogger.Info("[VEL] armed: target=" + TargetSurfaceSpeedMps.ToString("0.0") +
                    " m/s; target confirmed=" + TargetConfirmed + "; ACC seed=" +
                    PlannedAccelerationMps2.ToString("+0.00;-0.00;0.00") + " m/s².");
            }
            else
            {
                if (acceleration != null) acceleration.ClearVelocityPlannerTarget();
                ControlState = "Inactive";
                AERISLogger.Info("[VEL] disarmed.");
            }
        }

        internal void Disable(string reason, AERISAccelerationDirector acceleration)
        {
            if (!Armed && !ControlActive) return;
            Armed = false;
            ResetDynamicState();
            if (acceleration != null) acceleration.ClearVelocityPlannerTarget();
            ControlState = "Inactive";
            AERISLogger.Info("[VEL] disabled: " + reason);
        }

        internal bool TrySetTarget(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric surface-speed target.";
                return false;
            }
            if (value < 0f || value > MaximumTargetSurfaceSpeedMps)
            {
                error = "Surface-speed target must be between 0 and " +
                    MaximumTargetSurfaceSpeedMps.ToString("0") + " m/s.";
                return false;
            }
            TargetSurfaceSpeedMps = value;
            TargetSurfaceSpeedText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            TargetConfirmed = true;
            if (Armed && !ControlActive) ControlState = "Armed";
            AERISLogger.Info("[VEL] target=" + TargetSurfaceSpeedText + " m/s.");
            return true;
        }

        internal bool TrySetAccelerationLimit(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric VEL acceleration limit.";
                return false;
            }
            if (value < MinimumAccelerationLimitMps2 || value > MaximumAccelerationLimitMps2)
            {
                error = "VEL acceleration limit must be between " +
                    MinimumAccelerationLimitMps2.ToString("0.0") + " and " +
                    MaximumAccelerationLimitMps2.ToString("0.0") + " m/s².";
                return false;
            }
            SetAccelerationLimit(value);
            AERISLogger.Info("[VEL] symmetric acceleration limit=±" +
                ConfiguredAccelerationLimitMps2.ToString("0.0") + " m/s².");
            return true;
        }

        internal void SetAccelerationLimit(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) value = 4f;
            float clamped = Mathf.Clamp(Mathf.Abs(value), MinimumAccelerationLimitMps2,
                MaximumAccelerationLimitMps2);
            ConfiguredAccelerationLimitMps2 = clamped;
            MaximumAccelerationMps2 = clamped;
            MaximumDecelerationMps2 = clamped;
            AccelerationLimitText = clamped.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal void SetCurrent(VirtualAttitudeInstrument attitude)
        {
            if (attitude == null || !attitude.SharedSurfaceSpeedValid ||
                !IsFinite(attitude.SurfaceSpeedMps)) return;
            float value = Mathf.Max(0f, attitude.SurfaceSpeedMps);
            TargetSurfaceSpeedMps = value;
            TargetSurfaceSpeedText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            TargetConfirmed = true;
            if (Armed && !ControlActive) ControlState = "Armed";
            AERISLogger.Info("[VEL] target captured from current surface speed=" +
                TargetSurfaceSpeedText + " m/s.");
        }

        internal void Update(Vessel vessel, VirtualAttitudeInstrument attitude,
            AERISAccelerationDirector acceleration, bool aerisMaster, bool standardFbwActive)
        {
            bool sensorValid = attitude != null && attitude.InstrumentValid &&
                attitude.SharedSurfaceSpeedValid && attitude.SharedDynamicPressureValid &&
                IsFinite(attitude.SurfaceSpeedMps) && IsFinite(attitude.DynamicPressureKpa) &&
                acceleration != null && IsFinite(acceleration.FilteredAccelerationMps2);
            bool executable = Armed && TargetConfirmed && aerisMaster && standardFbwActive &&
                vessel != null && !vessel.packed && !vessel.LandedOrSplashed &&
                vessel.situation != Vessel.Situations.PRELAUNCH && sensorValid && acceleration.Armed;
            if (!executable)
            {
                ControlActive = false;
                VelocityErrorValid = false;
                VelocityHoldActive = false;
                DesiredAccelerationMps2 = 0f;
                PublishedAccelerationMps2 = 0f;
                ProjectedStoppingSpeedLeadMps = 0f;
                AccelerationTrackingLeadMps = 0f;
                DynamicPressureKpa = attitude != null && attitude.SharedDynamicPressureValid &&
                    IsFinite(attitude.DynamicPressureKpa) ? Mathf.Max(0f, attitude.DynamicPressureKpa) : 0f;
                DynamicPressurePlannerScale = 1f;
                EffectiveMaxAccelerationMps2 = MaximumAccelerationMps2;
                EffectiveMaxDecelerationMps2 = MaximumDecelerationMps2;
                EffectiveJerkLimitMps3 = AccelerationJerkLimitMps3;
                if (acceleration != null) acceleration.ClearVelocityPlannerTarget();
                if (!sensorValid)
                {
                    haveSample = false;
                    MeasuredAccelerationMps2 = 0f;
                    PlannedAccelerationMps2 = 0f;
                    lastSampleFixedTime = 0f;
                }
                if (Armed) ControlState = TargetConfirmed ? "Standby" : "TargetRequired";
                else ControlState = "Inactive";
                return;
            }

            float now = Time.fixedTime;
            float dt = haveSample ? Mathf.Clamp(now - lastSampleFixedTime, 0.001f, 0.25f) : Time.fixedDeltaTime;
            lastSampleFixedTime = now;
            haveSample = true;

            CurrentSurfaceSpeedMps = Mathf.Max(0f, attitude.SurfaceSpeedMps);
            VelocityErrorMps = TargetSurfaceSpeedMps - CurrentSurfaceSpeedMps;
            MeasuredAccelerationMps2 = acceleration.FilteredAccelerationMps2;
            DynamicPressureKpa = Mathf.Max(0f, attitude.DynamicPressureKpa);

            // Higher q makes the propulsion/drag response sharper. VEL reduces both the
            // acceleration envelope and its jerk so the same planner does not become abrupt.
            float highQBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(35f, 105f, DynamicPressureKpa));
            DynamicPressurePlannerScale = Mathf.Lerp(1.0f, 0.70f, highQBlend);
            EffectiveMaxAccelerationMps2 = MaximumAccelerationMps2 * DynamicPressurePlannerScale;
            EffectiveMaxDecelerationMps2 = MaximumDecelerationMps2 * DynamicPressurePlannerScale;
            EffectiveJerkLimitMps3 = Mathf.Max(0.20f, AccelerationJerkLimitMps3 * DynamicPressurePlannerScale);

            float absoluteError = Mathf.Abs(VelocityErrorMps);
            float absoluteAcceleration = Mathf.Abs(MeasuredAccelerationMps2);
            if (VelocityHoldActive)
            {
                if (absoluteError > HoldExitSpeedBandMps || absoluteAcceleration > HoldExitAccelerationBandMps2)
                    VelocityHoldActive = false;
            }
            else if (absoluteError <= HoldEnterSpeedBandMps && absoluteAcceleration <= HoldEnterAccelerationBandMps2)
            {
                VelocityHoldActive = true;
            }

            // While current acceleration is already carrying the aircraft toward the speed
            // target, this predicts the remaining speed travel needed to jerk acceleration
            // back to zero. v0.7.3 also includes the observed gap between measured and
            // planned acceleration, which is the part the original jerk-only lead could
            // not see during a pitch/drag or throttle-response transition.
            float jerkStoppingLead = Mathf.Sign(MeasuredAccelerationMps2) *
                (MeasuredAccelerationMps2 * MeasuredAccelerationMps2) /
                Mathf.Max(0.01f, 2f * EffectiveJerkLimitMps3);
            bool sameAccelerationDirection = Mathf.Abs(MeasuredAccelerationMps2) > 0.0001f &&
                Mathf.Abs(PlannedAccelerationMps2) > 0.0001f &&
                Mathf.Sign(MeasuredAccelerationMps2) == Mathf.Sign(PlannedAccelerationMps2);
            float scheduledSameDirectionMagnitude = sameAccelerationDirection
                ? Mathf.Abs(PlannedAccelerationMps2) : 0f;
            float untrackedAccelerationMagnitude = Mathf.Max(0f,
                Mathf.Abs(MeasuredAccelerationMps2) - scheduledSameDirectionMagnitude);
            AccelerationTrackingLeadMps = Mathf.Sign(MeasuredAccelerationMps2) *
                untrackedAccelerationMagnitude * AccelerationTrackingLeadSeconds;
            ProjectedStoppingSpeedLeadMps = jerkStoppingLead + AccelerationTrackingLeadMps;
            PredictedVelocityErrorMps = VelocityErrorMps - ProjectedStoppingSpeedLeadMps;

            if (VelocityHoldActive)
            {
                DesiredAccelerationMps2 = Mathf.Clamp(
                    HoldVelocityErrorGainPerSec * PredictedVelocityErrorMps -
                    HoldAccelerationDamping * MeasuredAccelerationMps2,
                    -HoldAccelerationLimitMps2, HoldAccelerationLimitMps2);
                ControlState = "VelocityHold";
            }
            else
            {
                DesiredAccelerationMps2 = Mathf.Clamp(
                    VelocityErrorGainPerSec * PredictedVelocityErrorMps -
                    AccelerationDamping * MeasuredAccelerationMps2,
                    -EffectiveMaxDecelerationMps2, EffectiveMaxAccelerationMps2);
                ControlState = absoluteError > 3.0f ? "VelocityCapture" : "VelocityTrack";
            }

            PlannedAccelerationMps2 = Mathf.MoveTowards(PlannedAccelerationMps2, DesiredAccelerationMps2,
                EffectiveJerkLimitMps3 * dt);
            PublishedAccelerationMps2 = PlannedAccelerationMps2;
            acceleration.SetVelocityPlannerTargetAcceleration(PublishedAccelerationMps2);

            ControlActive = true;
            VelocityErrorValid = true;
        }

        void ResetDynamicState()
        {
            ControlActive = false;
            VelocityErrorValid = false;
            VelocityHoldActive = false;
            CurrentSurfaceSpeedMps = 0f;
            VelocityErrorMps = 0f;
            PredictedVelocityErrorMps = 0f;
            MeasuredAccelerationMps2 = 0f;
            ProjectedStoppingSpeedLeadMps = 0f;
            AccelerationTrackingLeadMps = 0f;
            DesiredAccelerationMps2 = 0f;
            PlannedAccelerationMps2 = 0f;
            PublishedAccelerationMps2 = 0f;
            DynamicPressureKpa = 0f;
            DynamicPressurePlannerScale = 1f;
            EffectiveMaxAccelerationMps2 = MaximumAccelerationMps2;
            EffectiveMaxDecelerationMps2 = MaximumDecelerationMps2;
            EffectiveJerkLimitMps3 = AccelerationJerkLimitMps3;
            haveSample = false;
            lastSampleFixedTime = 0f;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
