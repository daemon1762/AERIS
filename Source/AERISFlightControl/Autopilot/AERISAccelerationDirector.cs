using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Autopilot
{
    // ACC is AERIS' lower speed director. It converts a requested surface-speed
    // acceleration into a bounded throttle demand. AA remains the sole final
    // FlightCtrlState writer; ACC only publishes the requested throttle before AA.
    // VEL now plans target acceleration and feeds this lower director.
    internal sealed class AERISAccelerationDirector
    {
        internal bool Armed { get; private set; }
        internal bool ControlActive { get; private set; }
        internal bool AccelerationErrorValid { get; private set; }
        internal string ControlState { get; private set; } = "Inactive";

        // Manual ACC target is retained while VEL is active.  The public target used by
        // ACC's law is switched only by VEL's explicit planner handoff.
        float manualTargetAccelerationMps2;
        internal float TargetAccelerationMps2 { get { return VelocityPlannerTargetActive ? VelocityPlannerTargetAccelerationMps2 : manualTargetAccelerationMps2; } }
        internal float ManualTargetAccelerationMps2 { get { return manualTargetAccelerationMps2; } }
        internal bool VelocityPlannerTargetActive { get; private set; }
        internal float VelocityPlannerTargetAccelerationMps2 { get; private set; }
        internal string TargetAccelerationText = "0";
        internal float CurrentSurfaceSpeedMps { get; private set; }
        internal float MeasuredAccelerationMps2 { get; private set; }
        internal float FilteredAccelerationMps2 { get; private set; }
        internal float AccelerationErrorMps2 { get; private set; }
        internal float EffectiveAccelerationErrorMps2 { get; private set; }
        // VEL commands small acceleration values near its speed target. Its precision
        // path needs a much narrower deadband than manual ACC, otherwise an intended
        // +/-0.02 m/s² trim is silently discarded by the lower director.
        internal float EffectiveAccelerationDeadbandMps2 { get; private set; }

        internal float BaseThrottle { get; private set; }
        internal float BaseThrottleAdaptation { get; private set; }
        // VEL gets a dedicated, fast reversible integral bias. The manual ACC
        // BaseThrottle remains an equilibrium learner, so large VEL acceleration
        // requests do not pollute the zero-acceleration trim and then unwind slowly.
        internal float VelocityPlannerThrottleBias { get; private set; }
        internal float VelocityPlannerThrottleBiasAdaptation { get; private set; }
        internal bool VelocityPlannerPrecisionActive { get; private set; }
        internal float ThrottleCorrection { get; private set; }
        internal float RawThrottleDemand { get; private set; }
        internal float ThrottleDemand { get; private set; }
        internal float AppliedThrottleSlewPerSec { get; private set; }
        internal float DynamicPressureKpa { get; private set; }
        internal float DynamicPressureCorrectionScale { get; private set; }
        internal float AirbrakeDemand { get; private set; }
        internal float AirbrakeDecelerationShortfallMps2 { get; private set; }

        // ACC zero-hold trim intentionally works only inside the normal acceleration
        // deadband. It slowly transfers a small persistent residual acceleration into
        // BaseThrottle without reintroducing a high-frequency P correction.
        internal bool ZeroAccelerationFineTrimActive { get; private set; }
        internal float ZeroAccelerationFineTrimAdaptation { get; private set; }
        internal float ZeroAccelerationFineTrimErrorMps2 { get; private set; }

        // Saturation is an observation/diagnostic state. It never changes the throttle
        // request or changes any control authority. VEL/AIRBRAKE AUTO will later use
        // these explicit states instead of guessing from a one-frame throttle value.
        internal bool ThrustSaturated { get; private set; }
        internal bool CoastLimited { get; private set; }
        internal string AccelerationLimitState { get; private set; } = "NONE";
        internal float AccelerationLimitElapsedSeconds { get; private set; }
        internal float ThrustSaturatedElapsedSeconds { get; private set; }
        internal float CoastLimitedElapsedSeconds { get; private set; }

        internal bool AaNativeThrottleOverrideActive { get; private set; }
        internal float AaNativeThrottleDemand { get; private set; }

        // Conservative ACC foundation tuning. ACC does not try to learn an entire
        // propulsion model yet: BaseThrottle slowly finds the zero-acceleration point,
        // while P correction captures the requested acceleration without abrupt throttle steps.
        internal float AccelerationGainThrottlePerMps2 = 0.060f;
        internal float BaseThrottleAdaptGainPerMps2PerSec = 0.022f;
        internal float AccelerationDeadbandMps2 = 0.030f;
        internal float AccelerationFilterPerSec = 5.0f;

        // v0.7.3 SPEED precision rebase. VEL is an outer trajectory director and
        // therefore needs small, continuous acceleration corrections at the target.
        // These settings apply only while VEL's explicit planner handoff is active.
        internal float VelocityPlannerAccelerationGainThrottlePerMps2 = 0.140f;
        internal float VelocityPlannerBiasGainPerMps2PerSec = 0.120f;
        internal float VelocityPlannerAccelerationDeadbandMps2 = 0.004f;
        // VEL must retain authority to reach true zero throttle for a natural-drag
        // deceleration.  The earlier ±0.60 cap could leave a residual positive thrust
        // with BaseThrottle seeded at 1.0, so ACC reported a strong negative acceleration
        // request while the airframe remained at a faster equilibrium speed.
        internal float VelocityPlannerThrottleBiasLimit = 1.00f;
        internal bool VelocityPlannerCoastAuthorityActive { get; private set; }
        internal bool VelocityPlannerBiasAtLimit { get; private set; }
        internal float LowQThrottleSlewPerSec = 0.72f;
        internal float HighQThrottleSlewPerSec = 0.34f;
        internal float MaximumTargetAccelerationMps2 = 30.0f;

        // v0.7.1 ACC hold trim / saturation awareness.
        internal float ZeroAccelerationFineTrimGainPerMps2PerSec = 0.012f;
        internal float ZeroAccelerationFineTrimNoiseFloorMps2 = 0.006f;
        internal float SaturationErrorThresholdMps2 = 0.20f;
        internal float SaturationDwellSeconds = 0.75f;
        internal float FullThrottleSaturationThreshold = 0.995f;
        internal float IdleThrottleSaturationThreshold = 0.005f;
        internal float AirbrakeShortfallEntryMps2 = 0.25f;
        internal float AirbrakeShortfallFullMps2 = 4.0f;

        float lastSurfaceSpeedMps;
        float lastSampleFixedTime;
        bool haveSpeedSample;
        float lastThrottleDemand;
        string lastLoggedAccelerationLimitState = "NONE";

        // VEL is the sole caller of this handoff. It never changes the prepared manual ACC
        // target, so selecting ACC again immediately restores the pilot's ACC setting.
        internal void SetVelocityPlannerTargetAcceleration(float targetAccelerationMps2)
        {
            if (float.IsNaN(targetAccelerationMps2) || float.IsInfinity(targetAccelerationMps2))
                targetAccelerationMps2 = 0f;
            // Keep the VEL integral state through continuous target updates. It is
            // explicitly handed back to BaseThrottle only when VEL releases ownership.
            VelocityPlannerTargetActive = true;
            VelocityPlannerTargetAccelerationMps2 = Mathf.Clamp(targetAccelerationMps2,
                -MaximumTargetAccelerationMps2, MaximumTargetAccelerationMps2);
        }

        internal void ClearVelocityPlannerTarget()
        {
            // Preserve the commanded physical throttle when VEL hands control back to
            // manual ACC. Without this transfer, a large capture bias disappears in
            // one tick and causes a visible throttle/acceleration step at mode release.
            if (VelocityPlannerTargetActive && Mathf.Abs(VelocityPlannerThrottleBias) > 0.000001f)
                BaseThrottle = Mathf.Clamp01(BaseThrottle + VelocityPlannerThrottleBias);
            VelocityPlannerTargetActive = false;
            VelocityPlannerTargetAccelerationMps2 = 0f;
            VelocityPlannerThrottleBias = 0f;
            VelocityPlannerThrottleBiasAdaptation = 0f;
            VelocityPlannerPrecisionActive = false;
            VelocityPlannerCoastAuthorityActive = false;
            VelocityPlannerBiasAtLimit = false;
        }

        internal void SetArmed(bool armed, Vessel vessel, VirtualAttitudeInstrument attitude)
        {
            if (Armed == armed) return;
            Armed = armed;
            ResetDynamicState();
            if (armed)
            {
                // Capture the current physical output as a non-disruptive starting point.
                // This is only a seed; ACC adapts BaseThrottle from measured acceleration.
                float finalThrottleSample = StandardFlyByWire.LastFinalThrottle;
                float seed = IsFinite(finalThrottleSample) ? Mathf.Clamp01(finalThrottleSample) : 0f;
                if (seed <= 0.0001f)
                {
                    try
                    {
                        if (FlightInputHandler.state != null &&
                            IsFinite(FlightInputHandler.state.mainThrottle))
                            seed = Mathf.Clamp01(FlightInputHandler.state.mainThrottle);
                    }
                    catch { }
                }
                BaseThrottle = seed;
                lastThrottleDemand = seed;
                ThrottleDemand = seed;
                CurrentSurfaceSpeedMps = attitude != null && attitude.SharedSurfaceSpeedValid &&
                    IsFinite(attitude.SurfaceSpeedMps) ? attitude.SurfaceSpeedMps : 0f;
                ControlState = "Armed";
                AERISLogger.Info("[ACC] armed: target=" + TargetAccelerationMps2.ToString("+0.0;-0.0;0.0") +
                    " m/s²; base throttle seed=" + BaseThrottle.ToString("0.000") + ".");
            }
            else
            {
                ClearAaNativeThrottleOverride();
                ControlState = "Inactive";
                AERISLogger.Info("[ACC] disarmed.");
            }
        }

        // Ground-armed ACC/VEL must not reuse the throttle seen when the aircraft was
        // parked.  Seed the equilibrium and slew states from AA's last physical output
        // exactly once when reliable liftoff releases normal AP execution.  This keeps
        // manual takeoff and Auto Takeoff handoff free of a one-frame throttle drop.
        internal void PreparePostTakeoffActivation(float physicalThrottle, string source)
        {
            if (!Armed) return;
            float seed = IsFinite(physicalThrottle) ? Mathf.Clamp01(physicalThrottle) : 0f;
            if (seed <= 0.0001f)
            {
                try
                {
                    if (FlightInputHandler.state != null &&
                        IsFinite(FlightInputHandler.state.mainThrottle))
                        seed = Mathf.Clamp01(FlightInputHandler.state.mainThrottle);
                }
                catch { }
            }
            ResetDynamicState();
            BaseThrottle = seed;
            lastThrottleDemand = seed;
            RawThrottleDemand = seed;
            ThrottleDemand = seed;
            ControlState = "PostTakeoffSeed";
            AERISLogger.Info("[ACC] post-takeoff throttle seed=" + seed.ToString("0.000") +
                "; source=" + source + ".");
        }

        internal void Disable(string reason)
        {
            if (!Armed && !AaNativeThrottleOverrideActive) return;
            Armed = false;
            ControlActive = false;
            AccelerationErrorValid = false;
            ResetDynamicState();
            ClearAaNativeThrottleOverride();
            ControlState = "Inactive";
            AERISLogger.Info("[ACC] disabled: " + reason);
        }

        internal bool TrySetTarget(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric acceleration target.";
                return false;
            }
            if (value < -MaximumTargetAccelerationMps2 || value > MaximumTargetAccelerationMps2)
            {
                error = "Acceleration target must be between -" + MaximumTargetAccelerationMps2.ToString("0") +
                    " and +" + MaximumTargetAccelerationMps2.ToString("0") + " m/s².";
                return false;
            }
            manualTargetAccelerationMps2 = value;
            TargetAccelerationText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            AERISLogger.Info("[ACC] target=" + TargetAccelerationText + " m/s²");
            return true;
        }

        internal void SetZeroTarget()
        {
            manualTargetAccelerationMps2 = 0f;
            TargetAccelerationText = "0.0";
            AERISLogger.Info("[ACC] target reset to 0.0 m/s².");
        }

        internal void Update(Vessel vessel, VirtualAttitudeInstrument attitude, bool aerisMaster, bool standardFbwActive)
        {
            bool sensorValid = attitude != null && attitude.InstrumentValid &&
                attitude.SharedSurfaceSpeedValid && attitude.SharedDynamicPressureValid &&
                IsFinite(attitude.SurfaceSpeedMps) && IsFinite(attitude.DynamicPressureKpa);
            bool executable = Armed && aerisMaster && standardFbwActive && vessel != null &&
                !vessel.packed && !vessel.LandedOrSplashed && vessel.situation != Vessel.Situations.PRELAUNCH &&
                sensorValid;
            if (!executable)
            {
                ControlActive = false;
                AccelerationErrorValid = false;
                BaseThrottleAdaptation = 0f;
                VelocityPlannerThrottleBiasAdaptation = 0f;
                VelocityPlannerPrecisionActive = false;
                VelocityPlannerCoastAuthorityActive = false;
                VelocityPlannerBiasAtLimit = false;
                ZeroAccelerationFineTrimActive = false;
                ZeroAccelerationFineTrimAdaptation = 0f;
                ZeroAccelerationFineTrimErrorMps2 = 0f;
                ThrottleCorrection = 0f;
                RawThrottleDemand = BaseThrottle;
                DynamicPressureKpa = attitude != null && attitude.SharedDynamicPressureValid &&
                    IsFinite(attitude.DynamicPressureKpa) ? Mathf.Max(0f, attitude.DynamicPressureKpa) : 0f;
                DynamicPressureCorrectionScale = 1f;
                AirbrakeDemand = 0f;
                AirbrakeDecelerationShortfallMps2 = 0f;
                ResetSaturationState();
                if (!sensorValid)
                {
                    haveSpeedSample = false;
                    MeasuredAccelerationMps2 = 0f;
                    FilteredAccelerationMps2 = 0f;
                    lastSurfaceSpeedMps = 0f;
                    lastSampleFixedTime = 0f;
                }
                // Safety release must not depend on a later AA callback: rails, landing,
                // invalid state, or MASTER release clear ACC ownership immediately.
                ClearAaNativeThrottleOverride();
                if (Armed) ControlState = "Standby";
                else ControlState = "Inactive";
                return;
            }

            float now = Time.fixedTime;
            float speed = Mathf.Max(0f, attitude.SurfaceSpeedMps);
            float dt = haveSpeedSample ? Mathf.Clamp(now - lastSampleFixedTime, 0.001f, 0.25f) : Time.fixedDeltaTime;
            float rawAcceleration = haveSpeedSample ? (speed - lastSurfaceSpeedMps) / Mathf.Max(0.001f, dt) : 0f;
            float filterBlend = Mathf.Clamp01(dt * AccelerationFilterPerSec);
            FilteredAccelerationMps2 = haveSpeedSample
                ? Mathf.Lerp(FilteredAccelerationMps2, rawAcceleration, filterBlend)
                : rawAcceleration;
            MeasuredAccelerationMps2 = rawAcceleration;
            CurrentSurfaceSpeedMps = speed;
            lastSurfaceSpeedMps = speed;
            lastSampleFixedTime = now;
            haveSpeedSample = true;

            DynamicPressureKpa = Mathf.Max(0f, attitude.DynamicPressureKpa);
            float highQBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(35f, 105f, DynamicPressureKpa));
            DynamicPressureCorrectionScale = Mathf.Lerp(1.0f, 0.65f, highQBlend);
            AppliedThrottleSlewPerSec = Mathf.Lerp(LowQThrottleSlewPerSec, HighQThrottleSlewPerSec, highQBlend);

            AccelerationErrorMps2 = TargetAccelerationMps2 - FilteredAccelerationMps2;
            VelocityPlannerPrecisionActive = VelocityPlannerTargetActive;
            EffectiveAccelerationDeadbandMps2 = VelocityPlannerPrecisionActive
                ? VelocityPlannerAccelerationDeadbandMps2
                : AccelerationDeadbandMps2;
            EffectiveAccelerationErrorMps2 = Mathf.Abs(AccelerationErrorMps2) <= EffectiveAccelerationDeadbandMps2
                ? 0f : AccelerationErrorMps2;

            // Manual ACC retains the validated BaseThrottle equilibrium learner. VEL
            // uses a separate fast reversible bias, because its continuously changing
            // acceleration trajectory must not contaminate BaseThrottle and then take
            // tens of seconds to unwind after a capture or vertical-energy transition.
            float normalBaseThrottleAdaptation = 0f;
            VelocityPlannerThrottleBiasAdaptation = 0f;
            if (VelocityPlannerPrecisionActive)
            {
                VelocityPlannerThrottleBiasAdaptation = EffectiveAccelerationErrorMps2 *
                    VelocityPlannerBiasGainPerMps2PerSec * dt * DynamicPressureCorrectionScale;
                VelocityPlannerThrottleBias = Mathf.Clamp(
                    VelocityPlannerThrottleBias + VelocityPlannerThrottleBiasAdaptation,
                    -VelocityPlannerThrottleBiasLimit, VelocityPlannerThrottleBiasLimit);
            }
            else
            {
                normalBaseThrottleAdaptation = EffectiveAccelerationErrorMps2 *
                    BaseThrottleAdaptGainPerMps2PerSec * dt * DynamicPressureCorrectionScale;
            }

            VelocityPlannerCoastAuthorityActive = VelocityPlannerPrecisionActive &&
                TargetAccelerationMps2 < -0.0001f && VelocityPlannerThrottleBias < -0.6001f;
            VelocityPlannerBiasAtLimit = VelocityPlannerPrecisionActive &&
                Mathf.Abs(VelocityPlannerThrottleBias) >= VelocityPlannerThrottleBiasLimit - 0.0001f;

            ZeroAccelerationFineTrimActive = false;
            ZeroAccelerationFineTrimAdaptation = 0f;
            ZeroAccelerationFineTrimErrorMps2 = 0f;
            bool zeroAccelerationTarget = Mathf.Abs(TargetAccelerationMps2) <= 0.0005f;
            float absoluteFilteredAcceleration = Mathf.Abs(FilteredAccelerationMps2);
            bool insideNormalDeadband = absoluteFilteredAcceleration <= AccelerationDeadbandMps2;
            bool aboveFineTrimNoiseFloor = absoluteFilteredAcceleration >= ZeroAccelerationFineTrimNoiseFloorMps2;
            if (!VelocityPlannerPrecisionActive && zeroAccelerationTarget && insideNormalDeadband && aboveFineTrimNoiseFloor &&
                !ThrustSaturated && !CoastLimited)
            {
                ZeroAccelerationFineTrimActive = true;
                ZeroAccelerationFineTrimErrorMps2 = -FilteredAccelerationMps2;
                ZeroAccelerationFineTrimAdaptation = ZeroAccelerationFineTrimErrorMps2 *
                    ZeroAccelerationFineTrimGainPerMps2PerSec * dt * DynamicPressureCorrectionScale;
            }

            BaseThrottleAdaptation = normalBaseThrottleAdaptation + ZeroAccelerationFineTrimAdaptation;
            BaseThrottle = Mathf.Clamp01(BaseThrottle + BaseThrottleAdaptation);
            float accelerationGain = VelocityPlannerPrecisionActive
                ? VelocityPlannerAccelerationGainThrottlePerMps2
                : AccelerationGainThrottlePerMps2;
            ThrottleCorrection = Mathf.Clamp(EffectiveAccelerationErrorMps2 * accelerationGain * DynamicPressureCorrectionScale,
                -0.48f, 0.48f);
            RawThrottleDemand = Mathf.Clamp01(BaseThrottle + VelocityPlannerThrottleBias + ThrottleCorrection);
            ThrottleDemand = Mathf.MoveTowards(lastThrottleDemand, RawThrottleDemand, AppliedThrottleSlewPerSec * dt);
            lastThrottleDemand = ThrottleDemand;
            UpdateSaturationState(dt);

            // Airbrake demand is only produced after zero-throttle coast authority has
            // been exhausted for the saturation dwell. A separate controller applies it
            // only when the user has enabled SPEED automatic airbrakes.
            AirbrakeDecelerationShortfallMps2 = Mathf.Max(0f,
                FilteredAccelerationMps2 - TargetAccelerationMps2);
            bool airbrakeEligible = CoastLimited && TargetAccelerationMps2 < -0.10f &&
                ThrottleDemand <= IdleThrottleSaturationThreshold;
            AirbrakeDemand = airbrakeEligible
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AirbrakeShortfallEntryMps2,
                    AirbrakeShortfallFullMps2, AirbrakeDecelerationShortfallMps2))
                : 0f;

            ControlActive = true;
            AccelerationErrorValid = true;
            float absError = Mathf.Abs(AccelerationErrorMps2);
            if (ThrustSaturated) ControlState = "ThrustSaturated";
            else if (CoastLimited) ControlState = "CoastLimited";
            else if (absError <= 0.08f) ControlState = "AccelerationHold";
            else if (RawThrottleDemand <= 0.001f && TargetAccelerationMps2 < 0f) ControlState = "NaturalDeceleration";
            else if (absError > 1.2f) ControlState = "AccelerationCapture";
            else ControlState = "AccelerationTrack";
        }

        void UpdateSaturationState(float dt)
        {
            bool positiveShortfall = AccelerationErrorMps2 >= SaturationErrorThresholdMps2;
            bool negativeShortfall = AccelerationErrorMps2 <= -SaturationErrorThresholdMps2;
            bool throttleAtFull = RawThrottleDemand >= 0.999f && ThrottleDemand >= FullThrottleSaturationThreshold;
            bool throttleAtIdle = RawThrottleDemand <= 0.001f && ThrottleDemand <= IdleThrottleSaturationThreshold;

            if (throttleAtFull && positiveShortfall)
                ThrustSaturatedElapsedSeconds += dt;
            else
                ThrustSaturatedElapsedSeconds = 0f;

            if (throttleAtIdle && negativeShortfall)
                CoastLimitedElapsedSeconds += dt;
            else
                CoastLimitedElapsedSeconds = 0f;

            ThrustSaturated = ThrustSaturatedElapsedSeconds >= SaturationDwellSeconds;
            CoastLimited = !ThrustSaturated && CoastLimitedElapsedSeconds >= SaturationDwellSeconds;

            string newState = ThrustSaturated ? "THRUST_SATURATED" : (CoastLimited ? "COAST_LIMITED" : "NONE");
            AccelerationLimitState = newState;
            AccelerationLimitElapsedSeconds = ThrustSaturated ? ThrustSaturatedElapsedSeconds :
                (CoastLimited ? CoastLimitedElapsedSeconds : 0f);

            if (newState != lastLoggedAccelerationLimitState)
            {
                lastLoggedAccelerationLimitState = newState;
                if (newState == "NONE")
                    AERISLogger.Info("[ACC] acceleration limit cleared.");
                else
                    AERISLogger.Info("[ACC] acceleration limit=" + newState +
                        "; target=" + TargetAccelerationMps2.ToString("+0.00;-0.00;0.00") +
                        " m/s²; measured=" + FilteredAccelerationMps2.ToString("+0.00;-0.00;0.00") +
                        " m/s²; throttle=" + ThrottleDemand.ToString("0.000") + ".");
            }
        }

        void ResetSaturationState()
        {
            bool hadLimit = AccelerationLimitState != "NONE" || lastLoggedAccelerationLimitState != "NONE";
            ThrustSaturated = false;
            CoastLimited = false;
            AccelerationLimitState = "NONE";
            AccelerationLimitElapsedSeconds = 0f;
            ThrustSaturatedElapsedSeconds = 0f;
            CoastLimitedElapsedSeconds = 0f;
            if (hadLimit) lastLoggedAccelerationLimitState = "NONE";
        }

        // Called before AA StandardFlyByWire. ACC publishes a requested throttle into
        // AA's ownership path; AA remains the only writer of FlightCtrlState.mainThrottle.
        internal void ApplyAaNativeThrottleDemand(FlightCtrlState state, Vessel vessel, VirtualAttitudeInstrument attitude,
            bool aerisMaster, bool standardFbwActive)
        {
            bool executable = state != null && Armed && ControlActive && aerisMaster && standardFbwActive &&
                vessel != null && !vessel.packed && !vessel.LandedOrSplashed &&
                vessel.situation != Vessel.Situations.PRELAUNCH && attitude != null && attitude.InstrumentValid &&
                attitude.SharedSurfaceSpeedValid && attitude.SharedDynamicPressureValid &&
                IsFinite(ThrottleDemand);
            if (!executable)
            {
                ClearAaNativeThrottleOverride();
                if (Armed) ControlState = "Standby";
                return;
            }
            AaNativeThrottleDemand = Mathf.Clamp01(ThrottleDemand);
            AaNativeThrottleOverrideActive = true;
            StandardFlyByWire.ExternalThrottleOverride = true;
            StandardFlyByWire.ExternalThrottleDemand = AaNativeThrottleDemand;
        }

        void ClearAaNativeThrottleOverride()
        {
            AaNativeThrottleOverrideActive = false;
            AaNativeThrottleDemand = 0f;
            StandardFlyByWire.ExternalThrottleOverride = false;
            StandardFlyByWire.ExternalThrottleDemand = 0f;
        }

        void ResetDynamicState()
        {
            ControlActive = false;
            AccelerationErrorValid = false;
            CurrentSurfaceSpeedMps = 0f;
            MeasuredAccelerationMps2 = 0f;
            FilteredAccelerationMps2 = 0f;
            AccelerationErrorMps2 = 0f;
            EffectiveAccelerationErrorMps2 = 0f;
            EffectiveAccelerationDeadbandMps2 = AccelerationDeadbandMps2;
            BaseThrottleAdaptation = 0f;
            VelocityPlannerThrottleBias = 0f;
            VelocityPlannerThrottleBiasAdaptation = 0f;
            VelocityPlannerPrecisionActive = false;
            VelocityPlannerCoastAuthorityActive = false;
            VelocityPlannerBiasAtLimit = false;
            ZeroAccelerationFineTrimActive = false;
            ZeroAccelerationFineTrimAdaptation = 0f;
            ZeroAccelerationFineTrimErrorMps2 = 0f;
            ThrottleCorrection = 0f;
            RawThrottleDemand = 0f;
            ThrottleDemand = 0f;
            AppliedThrottleSlewPerSec = 0f;
            DynamicPressureKpa = 0f;
            DynamicPressureCorrectionScale = 1f;
            AirbrakeDemand = 0f;
            AirbrakeDecelerationShortfallMps2 = 0f;
            AaNativeThrottleOverrideActive = false;
            AaNativeThrottleDemand = 0f;
            haveSpeedSample = false;
            lastSurfaceSpeedMps = 0f;
            lastSampleFixedTime = 0f;
            lastThrottleDemand = 0f;
            lastLoggedAccelerationLimitState = "NONE";
            ClearVelocityPlannerTarget();
            ResetSaturationState();
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
