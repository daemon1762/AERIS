using System;
using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.Logging;
using AERISFlightControl.API;
using AERISFlightControl.Integrations;

namespace AERISFlightControl.Protect
{
    // AA remains the only aerodynamic-surface controller. AERIS Protect detects energy/stall risk,
    // commands only a throttle floor, and optionally manages landing gear by radar altitude.
    internal enum ProtectRiskLevel { Unavailable, Safe, Caution, StallRisk, StallDetected }

    internal sealed class ProtectTelemetry
    {
        // v0.2.73 diagnostic only.
        float lastFlightCtrlTraceTime = -999f;
        bool lastFlightCtrlTraceProtect;

        internal bool AntiStallEnabled = true;
        internal bool ThrustAssistEnabled = true;
        internal bool AutoGearEnabled = true;
        // Auto Takeoff deliberately leaves gear/flaps to the pilot.  This transient
        // inhibit does not alter the persisted Auto Gear preference and is released
        // automatically when the takeoff sequence ends.
        internal bool AutoGearExternalInhibit;
        internal string RiskText
        {
            get
            {
                switch (Risk)
                {
                    case ProtectRiskLevel.Safe: return "Safe";
                    case ProtectRiskLevel.Caution: return "Caution";
                    case ProtectRiskLevel.StallRisk: return "StallRisk";
                    case ProtectRiskLevel.StallDetected: return "StallDetected";
                    default: return "Unavailable";
                }
            }
        }
        internal float AoADegrees { get; private set; }
        internal float BaseLimitDegrees { get; private set; }
        internal float EstimatedLimitDegrees { get; private set; }
        internal float StallMarginDegrees { get; private set; }
        internal float StallMarginNormalized { get; private set; }
        internal float SideslipDegrees { get; private set; }
        internal float PitchRateDegPerSec { get; private set; }
        internal float RollRateDegPerSec { get; private set; }
        internal float YawRateDegPerSec { get; private set; }
        internal float SurfaceSpeed { get; private set; }
        internal float SpeedDecayPerSecond { get; private set; }
        internal float VerticalSpeed { get; private set; }
        internal float DynamicPressureKpa { get; private set; }
        internal bool LowAltitudeHighAoAEnvelopeActive { get; private set; }
        internal float LowAltitudeHighAoAEnvelopeBlend { get; private set; }
        internal float HighAoAAllowanceDegrees { get; private set; }
        internal bool EnergyCollapseDetected { get; private set; }
        internal bool SpeedDirectorDecelerationActive { get; private set; }
        internal bool HighEnergyDecelerationActive { get; private set; }
        internal bool IntentionalDecelerationActive { get; private set; }
        internal bool DecelerationThrustInhibitActive { get; private set; }
        internal bool ThrustAssistInhibitedByDeceleration { get; private set; }

        // PilotThrottle is read from KSP input, never from AERIS's previous FlightCtrlState write.
        internal float UserThrottle { get; private set; }
        internal float LastAppliedThrottle { get; private set; }
        internal bool ThrottleAssistOwnershipActive { get; private set; }
        // Desired is the raw physics target. Requested is the rate-limited throttle floor actually sent to FlightCtrlState.
        internal float DesiredAssistThrottle { get; private set; }
        internal float RequestedAssistThrottle { get; private set; }
        internal float RequestedAssistContribution { get; private set; }
        internal bool ThrustAssistRecoveryTaper { get; private set; }
        internal float AvailableThrust { get; private set; }
        internal float RequiredThrust { get; private set; }
        internal float TargetRecoveryAcceleration { get; private set; }
        // Derived from available forward thrust / vessel mass. 1.0 is neutral; <1 trims high-TWR commands.
        internal float ThrustResponseFactor { get; private set; }
        internal float AvailableForwardAcceleration { get; private set; }
        internal bool ThrustAssistActive { get; private set; }
        internal bool ThrustAssistSaturated { get; private set; }
        internal bool InsufficientThrust { get; private set; }
        internal bool PropulsionProviderConnected { get; private set; }
        internal bool ExternalPropulsionIntegrationEnabled { get; set; } = true;
        internal bool PropulsionReady { get; private set; }
        internal bool PropulsionUnavailable { get; private set; }
        internal float PropulsionMotorResponse { get; private set; }
        // Last values returned by the propulsion provider; retained for logging outside the provider call scope.
        internal float ActualAvailableForwardThrustN { get; private set; }
        internal float EstimatedAvailableForwardThrustN { get; private set; }
        internal float RequiredForwardThrustkN { get { return RequiredThrust / 1000f; } }
        internal float ActualAvailableForwardThrustkN { get { return ActualAvailableForwardThrustN / 1000f; } }
        internal float EstimatedAvailableForwardThrustkN { get { return EstimatedAvailableForwardThrustN / 1000f; } }
        internal string PropulsionResponseStatus { get; private set; }
        internal string PropulsionProviderId { get; private set; }
        internal string PropulsionStatusDetail { get; private set; }
        float nextProviderTelemetryTime;
        internal PropulsionAvailabilityReason PropulsionReason { get; private set; }

        internal float RadarAltitude { get; private set; }
        internal bool AutoGearCommandKnown { get; private set; }
        internal bool AutoGearCommandUp { get; private set; }
        internal bool AutoGearActive { get; private set; }
        internal bool AutoGearTransitionActive { get; private set; }
        internal bool AutoGearPilotOverride { get; private set; }
        internal string AutoGearStatus { get; private set; }

        internal ProtectRiskLevel Risk { get; private set; }
        internal bool ProtectActive { get { return AntiStallEnabled && (Risk == ProtectRiskLevel.StallRisk || Risk == ProtectRiskLevel.StallDetected); } }
        internal bool StallWarning { get { return Risk == ProtectRiskLevel.Caution || Risk == ProtectRiskLevel.StallRisk || Risk == ProtectRiskLevel.StallDetected; } }
        internal bool StallDetected { get { return Risk == ProtectRiskLevel.StallDetected; } }
        internal string Status { get; private set; }
        internal string StallReason { get; private set; }

        const float Rad2Deg = 57.2957795f;
        const float GearDeployBelowMeters = 95f;
        const float GearRetractAboveMeters = 105f;
        const float GearCommandIntervalSeconds = 0.75f;
        const float GearSameDirectionLatchSeconds = 6.0f;
        const float GearTransitionAnnunciationSeconds = 2.5f;

        // v0.9.4 gear-independent high-AoA takeoff/landing allowance.  It is deliberately
        // kinematic: landing-gear action groups are not an input.  Stable low-altitude
        // high-AoA flight gets extra telemetry/thrust-floor margin, but the allowance
        // continuously collapses as pitch rate, sideslip, sink or energy loss deteriorates.
        const float HighAoAEnvelopeFullBelowMeters = 80f;
        const float HighAoAEnvelopeFadeAboveMeters = 300f;
        const float HighAoAEnvelopeMaximumAllowanceDeg = 14f;
        const float HighAoAEnvelopeMinimumSpeedMps = 15f;
        const float HighAoAEnvelopeFullSpeedMps = 25f;
        const float HighAoAEnvelopePitchRateSoftDegPerSec = 5f;
        const float HighAoAEnvelopePitchRateHardDegPerSec = 14f;
        const float HighAoAEnvelopeSideslipSoftDeg = 8f;
        const float HighAoAEnvelopeSideslipHardDeg = 18f;
        const float HighAoAEnvelopeSpeedDecaySoftMps2 = 4.5f;
        const float HighAoAEnvelopeSpeedDecayHardMps2 = 8.0f;
        const float HighAoAEnvelopeSinkSoftMps = 8f;
        const float HighAoAEnvelopeSinkHardMps = 18f;
        const float HighAoAEnvelopeThrottleReleaseRate = 0.90f;

        // Thrust-assist envelope: enter from STALL RISK, reserve full authority for STALL DETECTED.
        // Rates are throttle fraction per second. The decay path is intentionally slower than entry.
        const float StallRiskRecoveryAcceleration = 0.4375f; // 1/4 of detected authority (1.75 m/s²)
        const float StallDetectedRecoveryAcceleration = 1.75f;
        const float StallRiskRiseRate = 0.25f;
        const float StallDetectedRiseRate = 0.65f;
        const float CautionDecayRate = 0.11f;
        const float SafeDecayRate = 0.18f;
        const float UnavailableDecayRate = 0.35f;

        // Adaptive thrust-response shaping. A high-TWR craft needs only a short, modest pulse;
        // a low-output craft keeps the original stronger/longer energy assist.
        const float LowResponseAcceleration = 2.0f;   // m/s² available at full thrust
        const float HighResponseAcceleration = 18.0f; // m/s² available at full thrust
        const float HighResponseTargetScale = 0.35f;  // never reduce recovery authority below 35%
        const float HighResponseDecayScale = 1.85f;   // recover/relax faster once energy returns
        // Deep-stall protection must remain authoritative even on high-TWR craft.
        const float StallDetectedMinimumResponseFactor = 0.70f;
        const float StallDetectedMinimumThrottleFloor = 0.35f;

        float lastLogTime;
        float lastSampleTime = -1f;
        float controlDeltaTime = 0.02f;
        float filteredSpeedDecay;
        float lastSurfaceSpeed;
        float lastGearCommandTime = -99f;
        bool lastGearTargetKnown;
        bool lastGearTargetDeployed;
        float lastGearTargetTime = -99f;
        float lastGearCommandConfirmAfter = -99f;
        bool previousAutoGearEnabled = true;
        float lastGearTransitionCommandTime = -999f;
        string lastSummary = string.Empty;
        float lastPilotThrottle;
        // While a safety floor owns throttle, preserve the pilot setting that existed before intervention.
        // AERIS may protect against decreases during recovery, but always hands control back to this saved value.
        float protectedPilotThrottle;
        bool protectedPilotThrottleValid;
        bool restorePilotThrottlePending;
        // Last value mirrored by AERIS into FlightInputHandler. Never treat this echo as pilot input.
        float lastMirroredInputThrottle;
        bool hasMirroredInputThrottle;
        bool speedDecelerationContextRequested;
        float speedDecelerationTargetMps2;
        float speedDecelerationThrottleDemand;
        float speedDecelerationAirbrakeDemand;
        float speedDecelerationContextTime = -99f;

        internal ProtectTelemetry()
        {
            Status = "Awaiting atmospheric flight";
            StallReason = "NoValidSample";
            AutoGearStatus = "Awaiting flight";
            Risk = ProtectRiskLevel.Unavailable;
        }

        // SPEED publishes intent only; Protect retains sole authority over whether its
        // thrust floor is safe to inhibit. A short freshness window prevents a stale
        // director state from surviving an AP release or scene transition.
        internal void SetSpeedDecelerationContext(bool requested, float targetAccelerationMps2,
            float throttleDemand, float airbrakeDemand)
        {
            bool finite = IsFinite(targetAccelerationMps2) && IsFinite(throttleDemand) &&
                IsFinite(airbrakeDemand);
            speedDecelerationContextRequested = requested && finite;
            speedDecelerationTargetMps2 = finite ? targetAccelerationMps2 : 0f;
            speedDecelerationThrottleDemand = finite ? Mathf.Clamp01(throttleDemand) : 0f;
            speedDecelerationAirbrakeDemand = finite ? Mathf.Clamp01(airbrakeDemand) : 0f;
            speedDecelerationContextTime = Time.realtimeSinceStartup;
        }

        internal void Tick(TopModuleManager manager, bool master)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            UpdateAutoGear(v, master);

            if (!master || v == null || manager == null || manager.FlightModel == null || v.LandedOrSplashed || v.packed || v.staticPressurekPa < 0.1073d)
            {
                SetUnavailable("Standby / no valid atmospheric FBW sample");
                return;
            }

            var fm = manager.FlightModel;
            float aoa = fm.AoA(AutopilotModule.PITCH) * Rad2Deg;
            float sideslip = fm.AoA(AutopilotModule.YAW) * Rad2Deg;
            float pitchRate = fm.AngularVel(AutopilotModule.PITCH) * Rad2Deg;
            float rollRate = fm.AngularVel(AutopilotModule.ROLL) * Rad2Deg;
            float yawRate = fm.AngularVel(AutopilotModule.YAW) * Rad2Deg;
            float surfaceSpeed = (float)v.srfSpeed;
            float verticalSpeed = (float)v.verticalSpeed;
            float density = (float)v.atmDensity;
            if (!IsFinite(aoa) || !IsFinite(sideslip) || !IsFinite(pitchRate) ||
                !IsFinite(rollRate) || !IsFinite(yawRate) || !IsFinite(surfaceSpeed) ||
                !IsFinite(verticalSpeed) || !IsFinite(density))
            {
                SetUnavailable("Standby / non-finite atmospheric FBW sample");
                return;
            }
            AoADegrees = aoa;
            SideslipDegrees = sideslip;
            PitchRateDegPerSec = pitchRate;
            RollRateDegPerSec = rollRate;
            YawRateDegPerSec = yawRate;
            SurfaceSpeed = Mathf.Max(0f, surfaceSpeed);
            VerticalSpeed = verticalSpeed;
            DynamicPressureKpa = 0.5f * Mathf.Max(0f, density) *
                SurfaceSpeed * SurfaceSpeed / 1000f;
            if (!IsFinite(DynamicPressureKpa))
            {
                SetUnavailable("Standby / invalid dynamic pressure sample");
                return;
            }
            UserThrottle = ReadPilotThrottle(v.ctrlState);

            float now = Time.realtimeSinceStartup;
            float dt = lastSampleTime < 0f ? 0f : Mathf.Clamp(now - lastSampleTime, 0.001f, 1.0f);
            if (dt > 0f)
            {
                float rawDecay = (lastSurfaceSpeed - SurfaceSpeed) / dt;
                filteredSpeedDecay = Mathf.Lerp(filteredSpeedDecay, rawDecay, Mathf.Clamp01(dt * 2.5f));
            }
            lastSurfaceSpeed = SurfaceSpeed;
            controlDeltaTime = dt > 0f ? dt : 0.02f;
            lastSampleTime = now;
            SpeedDecayPerSecond = filteredSpeedDecay;

            UpdateDecelerationCoordination();

            float limit = manager.PitchController != null ? manager.PitchController.max_aoa : 15.0f;
            if (!IsFinite(limit)) limit = 15f;
            BaseLimitDegrees = Mathf.Max(3.0f, limit);
            UpdateLowAltitudeHighAoAEnvelope();
            EstimatedLimitDegrees = BaseLimitDegrees + HighAoAAllowanceDegrees;
            StallMarginDegrees = EstimatedLimitDegrees - Mathf.Abs(AoADegrees);
            StallMarginNormalized = Mathf.Clamp01(StallMarginDegrees / EstimatedLimitDegrees);

            if (!AntiStallEnabled)
            {
                Risk = ProtectRiskLevel.Safe;
                Status = "ANTI-STALL OFF — telemetry only";
                StallReason = "AntiStallDisabled";
                ComputeThrustAssist(v);
                LogIfChanged();
                return;
            }

            float cautionMargin = Mathf.Max(2.0f, EstimatedLimitDegrees * 0.25f);
            float riskMargin = Mathf.Max(0.75f, EstimatedLimitDegrees * 0.10f);
            float rateMagnitude = Mathf.Abs(PitchRateDegPerSec);
            bool nearBoundary = StallMarginDegrees <= riskMargin;
            bool beyondBoundary = StallMarginDegrees < -0.35f;
            bool fastApproach = StallMarginDegrees <= cautionMargin && AoADegrees > 0.0f && PitchRateDegPerSec > 8.0f;
            bool largeSideslip = Mathf.Abs(SideslipDegrees) > 12.0f;
            // A strong, stable high-q deceleration is not an energy-collapse cue. AA's
            // AoA/G moderation remains fully active; only the additional thrust floor is
            // coordinated away so SPEED or pilot-commanded energy bleed can work.
            bool meaningfulSpeedLoss = SurfaceSpeed > 20f && SpeedDecayPerSecond > 4.0f &&
                !IntentionalDecelerationActive;
            bool severeSpeedLoss = SurfaceSpeed > 20f && SpeedDecayPerSecond >
                HighAoAEnvelopeSpeedDecayHardMps2 && !IntentionalDecelerationActive;
            bool demandStillUp = PitchRateDegPerSec > 3.0f;

            if (beyondBoundary && (meaningfulSpeedLoss || demandStillUp || largeSideslip || EnergyCollapseDetected))
            {
                Risk = ProtectRiskLevel.StallDetected;
                Status = "STALL DETECTED — AA owns aerodynamic recovery";
                StallReason = severeSpeedLoss ? "AoABoundaryExceeded+SevereSpeedDecay" : "AoABoundaryExceeded+LossOfEnergy";
            }
            else if (nearBoundary || (fastApproach && largeSideslip) || (meaningfulSpeedLoss && StallMarginDegrees <= cautionMargin))
            {
                Risk = ProtectRiskLevel.StallRisk;
                Status = "STALL RISK — AA aerodynamic protection active";
                StallReason = nearBoundary ? "LowAoAMargin" : (largeSideslip ? "RapidPitchUp+HighSideslip" : "SpeedDecay+LowAoAMargin");
            }
            else if (StallMarginDegrees <= cautionMargin || (AoADegrees > 0.0f && rateMagnitude > 12.0f))
            {
                Risk = ProtectRiskLevel.Caution;
                Status = "CAUTION — stall margin reduced";
                StallReason = StallMarginDegrees <= cautionMargin ? "ReducedAoAMargin" : "HighPitchRate";
            }
            else
            {
                Risk = ProtectRiskLevel.Safe;
                Status = LowAltitudeHighAoAEnvelopeActive
                    ? "CONTROLLED HIGH-AOA TAKEOFF/LANDING ENVELOPE — monitoring"
                    : "SAFE — stall margin available";
                StallReason = LowAltitudeHighAoAEnvelopeActive
                    ? "GearIndependentStableHighAoA"
                    : "None";
            }

            ComputeThrustAssist(v);
            LogIfChanged();
        }

        void UpdateLowAltitudeHighAoAEnvelope()
        {
            float positiveAoA = Mathf.Max(0f, AoADegrees);
            float positiveSpeedDecay = Mathf.Max(0f, SpeedDecayPerSecond);
            float sinkRate = Mathf.Max(0f, -VerticalSpeed);
            float pitchRate = Mathf.Abs(PitchRateDegPerSec);
            float sideslip = Mathf.Abs(SideslipDegrees);

            bool sinkCollapseRelevant = RadarAltitude <= HighAoAEnvelopeFadeAboveMeters ||
                DynamicPressureKpa < 3f;
            EnergyCollapseDetected = (!IntentionalDecelerationActive &&
                positiveSpeedDecay >= HighAoAEnvelopeSpeedDecayHardMps2) ||
                (sinkCollapseRelevant && sinkRate >= HighAoAEnvelopeSinkHardMps) ||
                pitchRate >= HighAoAEnvelopePitchRateHardDegPerSec ||
                sideslip >= HighAoAEnvelopeSideslipHardDeg;

            float altitudeBlend = 1f - Mathf.InverseLerp(HighAoAEnvelopeFullBelowMeters,
                HighAoAEnvelopeFadeAboveMeters, RadarAltitude);
            altitudeBlend = Mathf.SmoothStep(0f, 1f, altitudeBlend);
            float speedBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                HighAoAEnvelopeMinimumSpeedMps, HighAoAEnvelopeFullSpeedMps, SurfaceSpeed));
            float aoaBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                BaseLimitDegrees - 2f, BaseLimitDegrees + 2f, positiveAoA));
            float pitchStability = 1f - Mathf.InverseLerp(HighAoAEnvelopePitchRateSoftDegPerSec,
                HighAoAEnvelopePitchRateHardDegPerSec, pitchRate);
            float slipStability = 1f - Mathf.InverseLerp(HighAoAEnvelopeSideslipSoftDeg,
                HighAoAEnvelopeSideslipHardDeg, sideslip);
            float decayStability = 1f - Mathf.InverseLerp(HighAoAEnvelopeSpeedDecaySoftMps2,
                HighAoAEnvelopeSpeedDecayHardMps2, positiveSpeedDecay);
            float sinkStability = 1f - Mathf.InverseLerp(HighAoAEnvelopeSinkSoftMps,
                HighAoAEnvelopeSinkHardMps, sinkRate);
            float stability = Mathf.Min(pitchStability, Mathf.Min(slipStability,
                Mathf.Min(decayStability, sinkStability)));
            if (EnergyCollapseDetected) stability = 0f;

            LowAltitudeHighAoAEnvelopeBlend = Mathf.Clamp01(altitudeBlend * speedBlend *
                aoaBlend * Mathf.Clamp01(stability));
            HighAoAAllowanceDegrees = HighAoAEnvelopeMaximumAllowanceDeg *
                LowAltitudeHighAoAEnvelopeBlend;
            LowAltitudeHighAoAEnvelopeActive = LowAltitudeHighAoAEnvelopeBlend > 0.05f &&
                positiveAoA >= BaseLimitDegrees - 2f;
        }

        void UpdateDecelerationCoordination()
        {
            float contextAge = Time.realtimeSinceStartup - speedDecelerationContextTime;
            bool freshSpeedContext = contextAge >= 0f && contextAge <= 0.30f;
            SpeedDirectorDecelerationActive = freshSpeedContext && speedDecelerationContextRequested &&
                speedDecelerationTargetMps2 < -0.10f && speedDecelerationThrottleDemand <= 0.02f;

            float pilotIntent = protectedPilotThrottleValid ? protectedPilotThrottle : UserThrottle;
            bool stableDirection = Mathf.Abs(SideslipDegrees) < 12f &&
                Mathf.Abs(PitchRateDegPerSec) < HighAoAEnvelopePitchRateHardDegPerSec;
            HighEnergyDecelerationActive = DynamicPressureKpa >= 8f && SurfaceSpeed >= 80f &&
                SpeedDecayPerSecond > 4f && stableDirection;
            bool manualIdleDeceleration = pilotIntent <= 0.15f;
            IntentionalDecelerationActive = HighEnergyDecelerationActive &&
                (SpeedDirectorDecelerationActive || manualIdleDeceleration ||
                 (freshSpeedContext && speedDecelerationAirbrakeDemand > 0.01f));
            DecelerationThrustInhibitActive = IntentionalDecelerationActive && stableDirection;
        }

        // AA invokes this after producing native auto-throttle output.
        // Protect only raises the demand to its safety floor; it never reduces AA demand.
        internal float ApplyThrottleFloor(float aaThrottleDemand, Vessel v, bool master)
        {
            float aa = Clamp01Finite(aaThrottleDemand);
            if (v == null || !master || !ThrustAssistEnabled || Risk == ProtectRiskLevel.Unavailable)
            {
                ThrustAssistActive = false;
                ThrottleAssistOwnershipActive = false;
                LastAppliedThrottle = aa;
                return aa;
            }
            float requested = Clamp01Finite(RequestedAssistThrottle);
            float final = Mathf.Max(aa, requested);
            ThrustAssistActive = final > aa + 0.01f;
            ThrottleAssistOwnershipActive = ThrustAssistActive;
            LastAppliedThrottle = final;
            return final;
        }

        // Called at KSP's flight-control callback. AERIS never decreases pilot throttle.
        // Important: read pilot intent from FlightInputHandler, not from a FlightCtrlState value AERIS
        // may have written on the previous callback. This prevents the assist floor self-latching.
        internal void ApplyFlightControl(FlightCtrlState state, Vessel v, bool master, bool aaSpeedControlActive)
        {
            if (state == null || v == null || !master || !ThrustAssistEnabled || Risk == ProtectRiskLevel.Unavailable)
            {
                ThrustAssistActive = false;
                ThrottleAssistOwnershipActive = false;
                LastAppliedThrottle = 0f;
                hasMirroredInputThrottle = false;
                return;
            }

            // AA's ProgradeThrustController has already written its automatic demand into
            // state.mainThrottle before AERIS runs.  Preserve that demand when AA Speed Control
            // is active; AERIS Protect is a minimum-throttle safety floor, not a replacement
            // for AA auto-throttle.
            float aaThrottleDemand = Clamp01Finite(state.mainThrottle);
            float rawPilot = ReadPilotThrottle(state);
            float requested = Clamp01Finite(RequestedAssistThrottle);
            TraceFlightCtrlState("PRE", state, v, aaSpeedControlActive, aaThrottleDemand, rawPilot, requested);

            // AERIS is a failsafe: while its floor is active, a decrease command (X / throttle-down)
            // must not pull actual throttle below the safety floor. A pilot increase is always accepted.
            // Keep a stable pilot baseline during ownership so that our own prior write is never mistaken
            // for a new pilot command, and do not let a lower raw input collapse the floor mid-recovery.
            if (requested > 0.001f)
            {
                if (!protectedPilotThrottleValid)
                {
                    // Capture the actual pilot setting once, before AERIS writes its protective floor.
                    protectedPilotThrottle = rawPilot;
                    protectedPilotThrottleValid = true;
                    restorePilotThrottlePending = true;
                }
                // A FlightInputHandler value may be our own previous mirror. Only an input that exceeds
                // that exact mirror by a meaningful margin is allowed to redefine the later restore target.
                // This prevents the protection floor from contaminating RestoreThrottle frame-to-frame.
                else if (!hasMirroredInputThrottle || rawPilot > lastMirroredInputThrottle + 0.02f)
                {
                    protectedPilotThrottle = rawPilot;
                }
            }
            else if (restorePilotThrottlePending && protectedPilotThrottleValid)
            {
                // The safety floor has completely released: explicitly restore the pre-intervention setting.
                rawPilot = protectedPilotThrottle;
                restorePilotThrottlePending = false;
                protectedPilotThrottleValid = false;
            }
            else
            {
                protectedPilotThrottle = rawPilot;
                protectedPilotThrottleValid = false;
            }

            float pilot = protectedPilotThrottleValid ? protectedPilotThrottle : rawPilot;
            lastPilotThrottle = pilot;
            UserThrottle = pilot;
            float baseThrottle = aaSpeedControlActive ? aaThrottleDemand : pilot;
            float final = Mathf.Max(baseThrottle, requested);
            ThrustAssistActive = final > baseThrottle + 0.01f;
            ThrottleAssistOwnershipActive = ThrustAssistActive;
            LastAppliedThrottle = final;
            state.mainThrottle = final;
            TraceFlightCtrlState("POST", state, v, aaSpeedControlActive, aaThrottleDemand, rawPilot, requested);
            // Some stock electric-prop paths consume the input-handler throttle after vessel callbacks.
            // Mirror only the protected minimum there so X cannot undercut an active safety floor.
            try
            {
                if (FlightInputHandler.state != null)
                {
                    // During intervention protect the floor; once released, restore the stored pilot setting.
                    if (!aaSpeedControlActive && requested > 0.001f && final > FlightInputHandler.state.mainThrottle)
                    {
                        FlightInputHandler.state.mainThrottle = final;
                        lastMirroredInputThrottle = final;
                        hasMirroredInputThrottle = true;
                    }
                    else if (!aaSpeedControlActive && requested <= 0.001f && !protectedPilotThrottleValid)
                    {
                        // Write the originally captured control position back to the raw input channel.
                        FlightInputHandler.state.mainThrottle = final;
                        lastMirroredInputThrottle = final;
                        hasMirroredInputThrottle = true;
                    }
                }
            }
            catch { }
        }

        void TraceFlightCtrlState(string phase, FlightCtrlState state, Vessel vessel, bool aaSpeedControlActive, float aaThrottleDemand, float rawPilot, float requested)
        {
            bool protectNow = ProtectActive || requested > 0.001f || ThrustAssistActive;
            float now = Time.realtimeSinceStartup;
            if (!protectNow && now - lastFlightCtrlTraceTime < 0.75f) return;
            if (protectNow == lastFlightCtrlTraceProtect && now - lastFlightCtrlTraceTime < 0.20f) return;
            lastFlightCtrlTraceTime = now;
            lastFlightCtrlTraceProtect = protectNow;
            float inputThrottle = FlightInputHandler.state == null ? -1f : FlightInputHandler.state.mainThrottle;
            AERISLogger.Info(string.Format("[FCTRL_TRACE] phase={0} protect={1} risk={2} aaSpeed={3} aaDemand={4:F3} pilot={5:F3} requested={6:F3} stateThrottle={7:F3} inputThrottle={8:F3} pitch={9:F3} roll={10:F3} yaw={11:F3} wheelThrottle={12:F3} wheelSteer={13:F3} killRot={14}", phase, protectNow, Risk, aaSpeedControlActive, aaThrottleDemand, rawPilot, requested, state.mainThrottle, inputThrottle, state.pitch, state.roll, state.yaw, state.wheelThrottle, state.wheelSteer, state.killRot));
        }

        float ReadPilotThrottle(FlightCtrlState fallback)
        {
            try
            {
                // This is KSP's raw pilot control state; it is not the value AERIS writes in OnFlyByWire.
                if (FlightInputHandler.state != null)
                    return Clamp01Finite(FlightInputHandler.state.mainThrottle);
            }
            catch
            {
                // Keep a safe fallback for unusual scene transitions / input-handler availability.
            }
            return fallback != null ? Clamp01Finite(fallback.mainThrottle) : Clamp01Finite(lastPilotThrottle);
        }

        void ComputeThrustAssist(Vessel v)
        {
            RequestedAssistContribution = 0f;
            AvailableThrust = 0f;
            RequiredThrust = 0f;
            TargetRecoveryAcceleration = 0f;
            ThrustResponseFactor = 1f;
            AvailableForwardAcceleration = 0f;
            ThrustAssistSaturated = false;
            InsufficientThrust = false;
            ThrustAssistActive = false;
            ThrustAssistInhibitedByDeceleration = false;
            if (!ThrustAssistEnabled || Risk == ProtectRiskLevel.Unavailable || v == null) return;

            bool deepAoA = StallMarginDegrees <= -5f;
            bool hardDirectionalInstability = Mathf.Abs(PitchRateDegPerSec) >=
                HighAoAEnvelopePitchRateHardDegPerSec || Mathf.Abs(SideslipDegrees) >=
                HighAoAEnvelopeSideslipHardDeg;
            if (DecelerationThrustInhibitActive && !deepAoA && !hardDirectionalInstability &&
                (Risk == ProtectRiskLevel.StallRisk || Risk == ProtectRiskLevel.StallDetected))
            {
                DesiredAssistThrottle = 0f;
                ThrustAssistInhibitedByDeceleration = true;
                ThrustAssistRecoveryTaper = false;
                SmoothAssistThrottle(0f);
                RequestedAssistContribution = Mathf.Max(0f, RequestedAssistThrottle - UserThrottle);
                Status = "CONTROLLED DECELERATION — AA envelope active, thrust assist inhibited";
                StallReason += "+DecelerationCoordination";
                UpdatePropulsionProvider(v);
                return;
            }

            // The requested acceleration is deliberately modest: AA owns AoA/surface recovery.
            // CAUTION never starts a new intervention. It only tapers a prior assist out gently.
            // STALL RISK begins assistance at one-quarter of STALL DETECTED authority.
            if (Risk == ProtectRiskLevel.StallRisk) TargetRecoveryAcceleration = StallRiskRecoveryAcceleration;
            else if (Risk == ProtectRiskLevel.StallDetected) TargetRecoveryAcceleration = StallDetectedRecoveryAcceleration;
            else
            {
                DesiredAssistThrottle = 0f;
                ThrustAssistRecoveryTaper = RequestedAssistThrottle > 0.01f && Risk == ProtectRiskLevel.Caution;
                SmoothAssistThrottle(0f);
                RequestedAssistContribution = Mathf.Max(0f, RequestedAssistThrottle - UserThrottle);
                // Refresh the provider with this frame's zero demand so recovery UI/logs cannot
                // retain a previous Limited/Meeting demand result after protection has released.
                UpdatePropulsionProvider(v);
                return;
            }

            float massKg = Mathf.Max(1f, (float)v.totalMass * 1000f);
            AvailableThrust = EstimateAvailableThrust(v, UserThrottle);

            // Calculate the recovery demand before querying the propulsion provider.
            // The provider's status/UI must be based on this frame's actual demand, not the previous frame's zero/reset value.
            AvailableForwardAcceleration = AvailableThrust > 1f ? AvailableThrust / massKg : 0f;
            float response01 = Mathf.InverseLerp(LowResponseAcceleration, HighResponseAcceleration, AvailableForwardAcceleration);
            ThrustResponseFactor = Mathf.Lerp(1f, HighResponseTargetScale, response01);
            // High-power craft recover energy very quickly, so give them a smaller command. For a confirmed
            // deep stall/spin, however, do not let adaptive trimming collapse recovery authority.
            if (Risk == ProtectRiskLevel.StallDetected)
                ThrustResponseFactor = Mathf.Max(ThrustResponseFactor, StallDetectedMinimumResponseFactor);
            TargetRecoveryAcceleration *= ThrustResponseFactor;
            float neededAcceleration = TargetRecoveryAcceleration + Mathf.Max(0f, SpeedDecayPerSecond);
            RequiredThrust = massKg * neededAcceleration;

            if (AvailableThrust <= 1f || PropulsionUnavailable)
            {
                InsufficientThrust = true;
                DesiredAssistThrottle = 1f;
            }
            else
            {
                DesiredAssistThrottle = Mathf.Clamp01(RequiredThrust / AvailableThrust);
                // Confirmed stall receives a non-trivial recovery floor even if a high-TWR estimate says
                // only a few percent throttle would be sufficient. This prevents 0.04-style under-command.
                if (Risk == ProtectRiskLevel.StallDetected)
                    DesiredAssistThrottle = Mathf.Max(DesiredAssistThrottle, StallDetectedMinimumThrottleFloor);
                InsufficientThrust = RequiredThrust > AvailableThrust * 1.02f;
            }

            ThrustAssistRecoveryTaper = false;
            SmoothAssistThrottle(DesiredAssistThrottle);
            RequestedAssistContribution = Mathf.Max(0f, RequestedAssistThrottle - UserThrottle);
            ThrustAssistSaturated = RequestedAssistThrottle >= 0.995f;

            // Query APP only after RequiredThrust and RequestedAssistThrottle are finalized for this frame.
            // This prevents an active recovery demand from being displayed as "Standby".
            UpdatePropulsionProvider(v);
            if (PropulsionProviderConnected && PropulsionReady)
            {
                float responseN = ActualAvailableForwardThrustN > 1f
                    ? ActualAvailableForwardThrustN
                    : EstimatedAvailableForwardThrustN;
                if (responseN > 1f)
                    AvailableThrust = Mathf.Max(AvailableThrust, responseN);
                InsufficientThrust = PropulsionUnavailable || RequiredThrust > Mathf.Max(1f, responseN) * 1.02f;
            }
        }

        void SmoothAssistThrottle(float target)
        {
            float dt = Mathf.Clamp(controlDeltaTime, 0.001f, 1.0f);
            float rate;
            if (target > RequestedAssistThrottle)
            {
                rate = Risk == ProtectRiskLevel.StallDetected ? StallDetectedRiseRate : StallRiskRiseRate;
            }
            else
            {
                rate = Risk == ProtectRiskLevel.Caution ? CautionDecayRate
                    : (Risk == ProtectRiskLevel.Safe ? SafeDecayRate : UnavailableDecayRate);
                if (LowAltitudeHighAoAEnvelopeActive && !EnergyCollapseDetected)
                    rate = Mathf.Max(rate, HighAoAEnvelopeThrottleReleaseRate);
                if (DecelerationThrustInhibitActive)
                    rate = Mathf.Max(rate, 2.50f);
                // High-response craft do not need a long residual throttle floor after recovery.
                rate *= Mathf.Lerp(1f, HighResponseDecayScale, Mathf.Clamp01(1f - ThrustResponseFactor));
            }

            RequestedAssistThrottle = Mathf.MoveTowards(RequestedAssistThrottle, Mathf.Clamp01(target), rate * dt);
            if (RequestedAssistThrottle < 0.001f) RequestedAssistThrottle = 0f;
        }

        float EstimateAvailableThrust(Vessel v, float currentThrottle)
        {
            float total = 0f;
            foreach (Part part in v.parts)
            {
                if (part == null) continue;
                foreach (PartModule module in part.Modules)
                {
                    ModuleEngines engine = module as ModuleEngines;
                    if (engine == null || !engine.isOperational) continue;
                    float maxThrust = engine.maxThrust;
                    float thrustPercentage = engine.thrustPercentage;
                    float finalThrust = engine.finalThrust;
                    if (!IsFinite(maxThrust) || !IsFinite(thrustPercentage) ||
                        !IsFinite(finalThrust)) continue;
                    float nominal = Mathf.Max(0f, maxThrust * thrustPercentage * 0.01f);
                    // When an engine is already producing thrust, infer its actual atmospheric/air-intake-limited ceiling.
                    if (currentThrottle > 0.05f && finalThrust > 0f)
                        nominal = Mathf.Max(nominal * 0.25f, finalThrust / currentThrottle);
                    if (!IsFinite(nominal)) continue;
                    total += nominal;
                    if (!IsFinite(total)) return 0f;
                }
            }
            // ModuleEngines.maxThrust/finalThrust are expressed in kN.
            // RequiredThrust is calculated in N (kg × m/s²), so convert once here.
            return total * 1000f;
        }


        void ClearPropulsionProvider()
        {
            PropulsionProviderConnected = false;
            PropulsionReady = false;
            PropulsionUnavailable = false;
            PropulsionMotorResponse = 0f;
            PropulsionProviderId = string.Empty;
            PropulsionStatusDetail = string.Empty;
            PropulsionReason = PropulsionAvailabilityReason.None;
            ActualAvailableForwardThrustN = 0f;
            EstimatedAvailableForwardThrustN = 0f;
            InsufficientThrust = false;
        }

        void UpdatePropulsionProvider(Vessel v)
        {
            if (!ExternalPropulsionIntegrationEnabled) { ClearPropulsionProvider(); PropulsionResponseStatus = "Integration disabled"; return; }
            if (!AddonIntegration.AppInstalled || !AddonIntegration.CurrentVesselSupportsApp) { ClearPropulsionProvider(); PropulsionResponseStatus = AddonIntegration.AppInstalled ? "No compatible propulsion on current vessel" : "No provider"; return; }
            if (Time.realtimeSinceStartup < nextProviderTelemetryTime && PropulsionProviderConnected) return;
            nextProviderTelemetryTime = Time.realtimeSinceStartup + 0.05f;
            PropulsionProviderConnected = false;
            PropulsionReady = false;
            PropulsionUnavailable = false;
            PropulsionMotorResponse = 0f;
            PropulsionProviderId = string.Empty;
            PropulsionStatusDetail = string.Empty;
            PropulsionReason = PropulsionAvailabilityReason.None;
            PropulsionResponseStatus = "No provider";
            ActualAvailableForwardThrustN = 0f;
            EstimatedAvailableForwardThrustN = 0f;

            if (v == null) return;
            var request = new AERISPropulsionRequest
            {
                RequiredThrottle = RequestedAssistThrottle,
                RequiredForwardThrustkN = RequiredThrust / 1000f,
                RequiredForwardThrustN = RequiredThrust,
                StallWarning = StallWarning,
                StallRisk = Risk == ProtectRiskLevel.StallRisk,
                StallDetected = StallDetected
            };
            // Publish safety demand; the companion propulsion extension executes it in its own FixedUpdate.
            AERISPropulsionStatus status;
            string selectedProviderId;
            if (!AERISPropulsionBridge.TrySelectProvider(v, request, out selectedProviderId, out status))
            {
                PropulsionReason = PropulsionAvailabilityReason.ProviderFault;
                PropulsionStatusDetail = "No compatible provider returned status";
                return;
            }
            PropulsionProviderConnected = true;
            PropulsionProviderId = selectedProviderId;
            AERISPropulsionDemandBus.Publish(v, request, selectedProviderId);
            PropulsionReady = status.PropulsionReady;
            PropulsionUnavailable = status.PropulsionUnavailable;
            PropulsionMotorResponse = Clamp01Finite(status.MotorResponse01);
            PropulsionReason = status.Reason;
            PropulsionStatusDetail = status.Detail ?? string.Empty;
            float actualkN = IsFinite(status.ActualAvailableForwardThrustkN)
                ? Mathf.Max(0f, status.ActualAvailableForwardThrustkN) : 0f;
            float estimatedkN = IsFinite(status.EstimatedAvailableForwardThrustkN)
                ? Mathf.Max(0f, status.EstimatedAvailableForwardThrustkN) : 0f;
            // Compatibility with old providers that only populate N fields.
            if (actualkN <= 0.0001f && IsFinite(status.ActualAvailableForwardThrustN) &&
                status.ActualAvailableForwardThrustN > 0f)
                actualkN = status.ActualAvailableForwardThrustN / 1000f;
            if (estimatedkN <= 0.0001f && IsFinite(status.EstimatedAvailableForwardThrustN) &&
                status.EstimatedAvailableForwardThrustN > 0f)
                estimatedkN = status.EstimatedAvailableForwardThrustN / 1000f;
            ActualAvailableForwardThrustN = actualkN * 1000f;
            EstimatedAvailableForwardThrustN = estimatedkN * 1000f;
            float requestedkN = RequiredThrust / 1000f;
            float responsekN = actualkN > 0.0001f ? actualkN : estimatedkN;
            if (PropulsionUnavailable) PropulsionResponseStatus = "Unavailable: " + PropulsionReason;
            else if (!PropulsionReady) PropulsionResponseStatus = "Waiting for propulsion";
            else if (requestedkN <= 0.05f) PropulsionResponseStatus = "Standby (no recovery demand)";
            else if (responsekN <= 0.05f) PropulsionResponseStatus = "No measured forward thrust";
            else if (responsekN + 0.05f < requestedkN) PropulsionResponseStatus = "Limited (" + responsekN.ToString("F1") + " / " + requestedkN.ToString("F1") + " kN)";
            else PropulsionResponseStatus = "Meeting demand";
            if (actualkN > 0.001f)
                AvailableThrust = Mathf.Max(AvailableThrust, actualkN * 1000f);
            else if (estimatedkN > 0.001f)
                AvailableThrust = Mathf.Max(AvailableThrust, estimatedkN * 1000f);
        }

        void UpdateAutoGear(Vessel v, bool master)
        {
            float gearNow = Time.realtimeSinceStartup;
            AutoGearActive = false;
            AutoGearTransitionActive = gearNow - lastGearTransitionCommandTime < GearTransitionAnnunciationSeconds;
            AutoGearCommandKnown = false;
            float rawRadarAltitude = v == null ? 0f : (float)v.radarAltitude;
            RadarAltitude = IsFinite(rawRadarAltitude) ? Mathf.Max(0f, rawRadarAltitude) : 0f;

            // Re-arm requires an explicit Auto Gear OFF -> ON cycle or a MASTER cycle.
            if (!AutoGearEnabled)
            {
                previousAutoGearEnabled = false;
                AutoGearPilotOverride = false;
                AutoGearStatus = "AUTO GEAR OFF";
                return;
            }
            if (AutoGearExternalInhibit)
            {
                AutoGearStatus = "MANUAL — AUTO TAKEOFF INHIBIT";
                return;
            }
            if (!previousAutoGearEnabled)
            {
                previousAutoGearEnabled = true;
                AutoGearPilotOverride = false;
                lastGearTargetKnown = false;
                AERISLogger.Info("[PROTECT][GEAR] Auto Gear re-armed by OFF -> ON.");
            }
            if (!master || v == null)
            {
                AutoGearPilotOverride = false;
                lastGearTargetKnown = false;
                AutoGearStatus = "Standby / MASTER OFF";
                return;
            }
            if (v.packed) { AutoGearStatus = "On rails"; return; }
            if (v.LandedOrSplashed || v.situation == Vessel.Situations.PRELAUNCH) { AutoGearStatus = "Ground / water — no command"; return; }

            bool targetKnown = false;
            // KSPActionGroup.Gear == true means the gear action group is ON / deployed.
            // The previous v0.2.8 implementation treated this as "gear up", reversing commands.
            bool targetGearDeployed = false;
            if (RadarAltitude < GearDeployBelowMeters) { targetKnown = true; targetGearDeployed = true; }
            else if (RadarAltitude > GearRetractAboveMeters) { targetKnown = true; targetGearDeployed = false; }

            if (!targetKnown)
            {
                AutoGearStatus = "Hold — 95–105 m hysteresis";
                return;
            }

            AutoGearCommandKnown = true;
            AutoGearCommandUp = !targetGearDeployed;

            bool currentGearDeployed = v.ActionGroups[KSPActionGroup.Gear];

            // A change opposite to AERIS's last confirmed command, after the command had time
            // to propagate, is treated as an intentional pilot gear action. Do not fight it.
            if (!AutoGearPilotOverride && lastGearTargetKnown
                && gearNow >= lastGearCommandConfirmAfter
                && currentGearDeployed != lastGearTargetDeployed)
            {
                AutoGearPilotOverride = true;
                AutoGearStatus = "PILOT OVERRIDE — Auto Gear suspended";
                AERISLogger.Info("[PROTECT][GEAR] PILOT OVERRIDE detected: actionGroup="
                    + (currentGearDeployed ? "DEPLOYED" : "RETRACTED")
                    + "; automatic gear commands suspended until re-armed.");
                return;
            }
            if (AutoGearPilotOverride)
            {
                AutoGearStatus = "PILOT OVERRIDE — Auto Gear suspended";
                return;
            }

            AutoGearActive = true;
            AutoGearStatus = AutoGearTransitionActive
                ? (lastGearTargetDeployed ? "GEAR DEPLOYING" : "GEAR RETRACTING")
                : (targetGearDeployed ? "AUTO EXTEND (<95 m radar)" : "AUTO RETRACT (>105 m radar)");
            if (lastGearTargetKnown && lastGearTargetDeployed == targetGearDeployed
                && gearNow - lastGearTargetTime < GearSameDirectionLatchSeconds)
            {
                if (!AutoGearTransitionActive) AutoGearStatus += " — command latched";
                return;
            }
            if (gearNow - lastGearCommandTime < GearCommandIntervalSeconds) return;
            if (currentGearDeployed != targetGearDeployed)
            {
                v.ActionGroups.SetGroup(KSPActionGroup.Gear, targetGearDeployed);
                lastGearCommandTime = gearNow;
                lastGearTransitionCommandTime = gearNow;
                AutoGearTransitionActive = true;
                AutoGearStatus = targetGearDeployed ? "GEAR DEPLOYING" : "GEAR RETRACTING";
                lastGearTargetKnown = true;
                lastGearTargetDeployed = targetGearDeployed;
                lastGearTargetTime = gearNow;
                lastGearCommandConfirmAfter = gearNow + 1.0f;
                AERISLogger.Info("[PROTECT][GEAR] " + (targetGearDeployed ? "EXTEND" : "RETRACT")
                    + " radarAlt=" + RadarAltitude.ToString("F1") + "m threshold="
                    + (targetGearDeployed ? "<95" : ">105") + "m actionGroupBefore="
                    + (currentGearDeployed ? "DEPLOYED" : "RETRACTED") + " actionGroupTarget="
                    + (targetGearDeployed ? "DEPLOYED" : "RETRACTED")
                    + " latch=" + GearSameDirectionLatchSeconds.ToString("F1") + "s");
            }
            else
            {
                lastGearTargetKnown = true;
                lastGearTargetDeployed = targetGearDeployed;
                lastGearTargetTime = gearNow;
            }
        }

        void LogIfChanged()
        {
            // Normal flight telemetry changes every frame.  Log only meaningful protection
            // state transitions; detailed numeric tracing belongs to an explicit DEBUG mode.
            string key = Risk + "|" + StallReason + "|" + ThrustAssistActive + "|" +
                         ThrottleAssistOwnershipActive + "|" + ThrustAssistSaturated + "|" +
                         InsufficientThrust + "|" + AutoGearStatus + "|" +
                         LowAltitudeHighAoAEnvelopeActive + "|" + EnergyCollapseDetected + "|" +
                         DecelerationThrustInhibitActive + "|" + ThrustAssistInhibitedByDeceleration + "|" +
                         (PropulsionProviderConnected ? PropulsionProviderId : "none") + "|" +
                         PropulsionReady + "|" + PropulsionUnavailable + "|" + PropulsionResponseStatus;
            if (key == lastSummary) return;
            lastSummary = key;
            lastLogTime = Time.realtimeSinceStartup;
            string summary = Risk + " reason=" + StallReason +
                " assist=" + ThrustAssistActive +
                " ownership=" + ThrottleAssistOwnershipActive +
                " floor=" + RequestedAssistThrottle.ToString("F2") +
                " prop=" + (PropulsionProviderConnected ? PropulsionProviderId : "none") +
                " response=" + PropulsionResponseStatus +
                " highAoA=" + LowAltitudeHighAoAEnvelopeActive +
                " allowance=" + HighAoAAllowanceDegrees.ToString("F1") +
                " energyCollapse=" + EnergyCollapseDetected +
                " decelCoord=" + DecelerationThrustInhibitActive +
                " thrustInhibit=" + ThrustAssistInhibitedByDeceleration +
                " gear=" + AutoGearStatus;
            AERISLogger.Info("[PROTECT] " + summary);
        }

        void SetUnavailable(string status)
        {
            AoADegrees = 0f; BaseLimitDegrees = 0f; EstimatedLimitDegrees = 0f; StallMarginDegrees = 0f; StallMarginNormalized = 0f;
            SideslipDegrees = 0f; PitchRateDegPerSec = 0f; RollRateDegPerSec = 0f; YawRateDegPerSec = 0f;
            SurfaceSpeed = 0f; SpeedDecayPerSecond = 0f; VerticalSpeed = 0f; DynamicPressureKpa = 0f; LowAltitudeHighAoAEnvelopeActive = false; LowAltitudeHighAoAEnvelopeBlend = 0f; HighAoAAllowanceDegrees = 0f; EnergyCollapseDetected = false; SpeedDirectorDecelerationActive = false; HighEnergyDecelerationActive = false; IntentionalDecelerationActive = false; DecelerationThrustInhibitActive = false; ThrustAssistInhibitedByDeceleration = false; UserThrottle = 0f; LastAppliedThrottle = 0f; ThrottleAssistOwnershipActive = false; lastPilotThrottle = 0f; protectedPilotThrottle = 0f; protectedPilotThrottleValid = false; restorePilotThrottlePending = false; lastMirroredInputThrottle = 0f; hasMirroredInputThrottle = false; DesiredAssistThrottle = 0f; RequestedAssistThrottle = 0f; RequestedAssistContribution = 0f; ThrustAssistRecoveryTaper = false;
            AvailableThrust = 0f; RequiredThrust = 0f; TargetRecoveryAcceleration = 0f; ThrustResponseFactor = 1f; AvailableForwardAcceleration = 0f; ThrustAssistActive = false; ThrustAssistSaturated = false; InsufficientThrust = false; PropulsionProviderConnected = false; PropulsionReady = false; PropulsionUnavailable = false; PropulsionMotorResponse = 0f; ActualAvailableForwardThrustN = 0f; EstimatedAvailableForwardThrustN = 0f; PropulsionProviderId = string.Empty; PropulsionStatusDetail = string.Empty; PropulsionReason = PropulsionAvailabilityReason.None; PropulsionResponseStatus = "No provider";
            filteredSpeedDecay = 0f; lastSampleTime = -1f; controlDeltaTime = 0.02f; lastSurfaceSpeed = 0f;
            Risk = ProtectRiskLevel.Unavailable;
            StallReason = "NoValidSample";
            Status = status;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float Clamp01Finite(float value)
        {
            return IsFinite(value) ? Mathf.Clamp01(value) : 0f;
        }

        internal void ResetForSceneTransition(string reason)
        {
            SetUnavailable("Standby / scene reset: " + reason);
            AutoGearExternalInhibit = false;
            AutoGearActive = false;
            AutoGearTransitionActive = false;
            lastGearTransitionCommandTime = -999f;
            AutoGearCommandKnown = false;
            AutoGearPilotOverride = false;
            AutoGearStatus = "Standby / scene reset";
            RadarAltitude = 0f;
            previousAutoGearEnabled = AutoGearEnabled;
            lastGearTargetKnown = false;
            lastGearTargetDeployed = false;
            lastGearTargetTime = -99f;
            lastGearCommandTime = -99f;
            lastGearCommandConfirmAfter = -99f;
            lastSummary = string.Empty;
            speedDecelerationContextRequested = false;
            speedDecelerationTargetMps2 = 0f;
            speedDecelerationThrottleDemand = 0f;
            speedDecelerationAirbrakeDemand = 0f;
            speedDecelerationContextTime = -99f;
        }

    }
}
