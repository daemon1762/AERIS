using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;
using AERISFlightControl.Protect;

namespace AERISFlightControl.Autopilot
{
    // HDG is an upper-level lateral director. Roll execution is always delegated
    // to BANK. Terminal yaw is deliberately a small finishing assist only; it is
    // never used as the primary means of turning toward a heading.
    internal sealed class AERISHdgDirector
    {
        internal bool Armed { get; private set; }
        internal float TargetHeading { get; private set; }
        internal string TargetHeadingText = "0";
        internal float CurrentHeading { get; private set; }
        internal float HeadingError { get; private set; }
        internal float CommandedBankTarget { get; private set; }
        internal float RawBankTarget { get; private set; }
        internal float AutoMaxBankLimitDeg { get; private set; }
        internal float EffectiveMaxBankLimitDeg { get; private set; }
        // The legacy q-only AUTO schedule is deliberately retained as the baseline.
        // When measured speed, q, radar altitude and PROTECT stall margin all agree
        // that additional load factor is safe, this envelope restores enough BANK
        // authority for HDG to complete a low-speed turn.  BANK remains the sole
        // roll executor and AA/PROTECT remain the final limiting layers.
        internal bool SafeLowSpeedBankAuthorityActive { get; private set; }
        internal float SafeLowSpeedBankCapabilityLimitDeg { get; private set; }
        internal float SafeLowSpeedBankAuthorityLimitDeg { get; private set; }
        internal float SafeLowSpeedBankAuthorityBlend { get; private set; }
        internal float SafeLowSpeedBankSpeedBlend { get; private set; }
        internal float SafeLowSpeedBankQBlend { get; private set; }
        internal float SafeLowSpeedBankStallBlend { get; private set; }
        internal float SafeLowSpeedBankAltitudeBlend { get; private set; }
        internal string SafeLowSpeedBankAuthorityReason { get; private set; } = "INACTIVE";
        internal bool UseAutoMaxBankLimit { get; private set; } = true;
        internal float ManualMaxBankLimitDeg { get; private set; } = 30f;
        internal string ManualMaxBankLimitText = "30";
        internal string ControlState { get; private set; } = "Inactive";
        internal bool ThinAirTurnAssistEnabled = true;
        internal bool ThinAirTurnAssistActive { get; private set; }
        internal float ThinAirBlend { get; private set; }
        internal float ThinAirTurnAssistBlend { get; private set; }
        internal float ThinAirTurnResponseRatio { get; private set; } = 1f;
        internal float ThinAirTurnBankTargetDeg { get; private set; }
        internal float ThinAirTurnPitchAssistRateDegPerSec { get; private set; }
        internal float ThinAirTurnWeakResponseElapsedSeconds { get; private set; }
        internal bool ThinAirTurnAltitudeQualified { get; private set; }
        internal bool ThinAirTurnSpeedQualified { get; private set; }
        internal bool ThinAirTurnStallMarginQualified { get; private set; }
        internal bool ThinAirTurnHeadingErrorQualified { get; private set; }
        internal string ThinAirTurnQualificationStatus { get; private set; } = "OFF";
        internal float ThinAirTurnObservedAltitudeMeters { get; private set; }
        internal float ThinAirTurnObservedSurfaceSpeedMps { get; private set; }
        internal float ThinAirTurnObservedStallMarginDeg { get; private set; }
        internal float ThinAirTurnObservedHeadingRateDegPerSec { get; private set; }

        // Terminal coordination capture: active only after BANK has almost rolled
        // out. The command is injected before AA, so AA remains the final FBW layer.
        internal bool TerminalYawActive { get; private set; }
        // Kept as an ownership/status compatibility flag. v0.4.90 transports yaw as
        // an AA-native angular-velocity request rather than virtual pilot input.
        internal bool YawOwned { get { return Armed; } }
        // Terminal residual-capture yaw. Kept separate from normal turn coordination
        // so telemetry and UI can distinguish the two roles.
        internal float TerminalYawCommand { get; private set; }
        internal float TerminalYawRawCommand { get; private set; }
        internal float CoordinatedYawCommand { get; private set; }
        internal float CoordinatedYawFeedForward { get; private set; }
        internal float CoordinatedYawRateCorrection { get; private set; }
        // Legacy virtual-pilot yaw shadow retained strictly for comparison telemetry.
        // v0.4.90 no longer gives this value to AA as a rudder/pilot input.
        internal float VirtualYawCommand { get; private set; }

        // Native AA yaw-rate transport. AERIS remains the outer HDG director; it gives
        // desired yaw angular velocity to AA's existing YawAngularVelocityController.
        internal bool NativeYawTransportEligible { get; private set; }
        internal bool AaNativeYawRateOverrideActive { get; private set; }
        internal float AaNativeYawRateDemandDegPerSec { get; private set; }
        internal float AaNativeYawRateDemandRadPerSec { get; private set; }
        internal float YawRateRequestDegPerSec { get; private set; }
        internal float YawRateActualDegPerSec { get; private set; }
        internal float YawInputAfterNeutralization { get; private set; }
        internal float TerminalYawRateRawDegPerSec { get; private set; }
        internal float TerminalYawRateCommandDegPerSec { get; private set; }
        internal float TerminalYawRateProportionalTermDegPerSec { get; private set; }
        internal float TerminalYawRateDampingTermDegPerSec { get; private set; }
        internal float CoordinatedYawRateTargetDegPerSec { get; private set; }
        internal float CoordinatedYawRateCommandDegPerSec { get; private set; }
        internal string TerminalYawCaptureBand { get; private set; } = "OFF";
        internal float TerminalYawProportionalTerm { get; private set; }
        internal float TerminalYawRateDampingTerm { get; private set; }
        internal float RolloutStartErrorDeg { get; private set; }
        internal bool RolloutHoldActive { get; private set; }
        // A small terminal BANK floor is only retained when rudder response is demonstrably weak.
        // This is still a BANK target; AERIS never writes final AA roll output.
        internal float TerminalRollAssistDeg { get; private set; }
        internal bool TerminalRollAssistActive { get; private set; }
        // Terminal roll-assist conditioner: absorbs tiny HDG terminal changes before
        // they reach BANK, and requires a brief persistence before reversing assist sign.
        internal bool TerminalRollAssistHoldActive { get; private set; }
        internal bool TerminalRollAssistReversePending { get; private set; }
        internal float TerminalRollAssistFilteredDeg { get; private set; }
        internal float TerminalRollAssistRawDeg { get; private set; }
        // v0.4.58: visible status for the HDG→BANK terminal quiet handoff.
        internal bool TerminalBankQuietZoneActive { get; private set; }

        internal float MaxBankLowQ = 15f;
        internal float MaxBankMidQ = 30f;
        // High-q aircraft remain dynamically stable and may need decisive turns.
        // Allow a larger HDG bank envelope while preserving the BANK director
        // slew/reversal protections.
        internal float MaxBankHighQ = 45f;
        internal float HeadingToBankGainLowQ = 0.35f;
        internal float HeadingToBankGainMidQ = 0.75f;
        internal float HeadingBankSlewLowQDegPerSec = 5f;
        internal float HeadingBankSlewMidQDegPerSec = 14f;

        // v0.11.6 baseline safe low-speed BANK authority recovery.  v0.11.7 may
        // extend the 30-degree baseline toward 45 degrees only when AA's measured
        // sustainable-G capability and the continuous PROTECT gates both allow it.
        internal float SafeLowSpeedBankMaximumDeg = 30f;
        internal float SafeLowSpeedBankAdaptiveMaximumDeg = 45f;
        internal float SafeLowSpeedBankMeasuredMaximumDeg { get; private set; } = 30f;
        internal bool SafeLowSpeedBankCapabilitySampleActive { get; private set; }
        internal float SafeLowSpeedBankObservedG { get; private set; } = 1f;
        float safeLowSpeedLearnedBankCapDeg = 30f;
        // Retained for diagnostics/source compatibility only.  v0.11.7 no longer
        // treats an absolute m/s value as aircraft capability: 75 m/s can be healthy
        // for one airframe and unrecoverable for another.  The speed gate below is
        // now a dimensionless PROTECT stall-margin/energy gate.
        internal float SafeLowSpeedBankSpeedStartMps = 90f;
        internal float SafeLowSpeedBankSpeedFullMps = 140f;
        internal float SafeLowSpeedBankStallMarginStartDeg = 5.5f;
        internal float SafeLowSpeedBankStallMarginFullDeg = 10f;
        internal float SafeLowSpeedBankStallMarginStartNormalized = 0.20f;
        internal float SafeLowSpeedBankStallMarginFullNormalized = 0.45f;
        internal float SafeLowSpeedBankRadarAltitudeStartM = 150f;
        internal float SafeLowSpeedBankRadarAltitudeFullM = 600f;
        internal float SafeLowSpeedBankHeadingErrorStartDeg = 5f;
        internal float SafeLowSpeedBankHeadingErrorFullDeg = 20f;

        // The max-bank hold lasts until this fraction of the original saturation
        // corridor remains. This avoids early roll-out and excessive turn radius.
        internal float RolloutHoldFraction = 0.55f; // retained only for the low-q baseline
        // HDG max-bank retention schedule. Low-q keeps the existing conservative lead;
        // mid/high-q hold the efficient turn until roughly 6.5 / 5 degrees remain.
        internal float RolloutHoldMidQErrorDeg = 6.5f;
        internal float RolloutHoldHighQErrorDeg = 5.0f;
        internal float RolloutPredictedTurnLeadSeconds = 0.35f;
        internal float TerminalYawEntryErrorDeg = 3f;
        internal float TerminalYawMaxBankDeg = 14f;
        internal float TerminalYawMaxRollRateDegPerSec = 8f;
        internal float TerminalYawHeadingGain = 0.145f;
        internal float TerminalYawRateDamping = 0.018f;
        internal float TerminalYawMaxCommand = 0.72f;
        internal float TerminalYawSlewPerSec = 2.20f;
        internal float TerminalYawWeakResponseCommand = 0.16f;
        internal float TerminalYawWeakResponseRateDegPerSec = 0.75f;
        internal float TerminalRollAssistEntryDeg = 4.5f;
        internal float TerminalRollAssistPrecisionDeg = 1.75f;
        internal float TerminalRollAssistMinimumErrorDeg = 0.18f;
        internal float TerminalRollAssistDeadbandDeg = 0.22f;
        internal float TerminalRollAssistReverseDwellSeconds = 0.22f;
        internal float TerminalRollAssistFadeWhenYawEffective = 0.58f;
        internal float TerminalYawStrongBandDeg = 1.0f;
        internal float TerminalYawPrecisionBandDeg = 0.5f;

        // v0.4.90 native yaw-rate outer-loop parameters. Values are degrees/sec until
        // the final AA boundary, where they are converted once to radians/sec.
        internal float TerminalYawHeadingRateGainPerSec = 0.85f;
        internal float TerminalYawRateDampingGain = 0.55f;
        internal float TerminalYawNativeMaxRateDegPerSec = 3.20f;
        internal float TerminalYawNativeSlewDegPerSec2 = 8.00f;
        internal float TerminalYawWeakResponseDemandDegPerSec = 1.00f;
        internal float CoordinatedYawNativeSlewDegPerSec2 = 9.00f;
        internal float CoordinatedYawNativeMaxRateDegPerSec = 5.00f;
        internal float NativeYawCombinedMaxRateDegPerSec = 5.00f;

        // Legacy virtual-yaw shadow parameters retained only for comparison telemetry.
        // v0.4.90 actual yaw control uses the native yaw-rate parameters above, never
        // a post-AA output write or an AA PID modification.
        internal float CoordinatedYawBankGain = 0.0105f;
        internal float CoordinatedYawMaxCommand = 0.34f;
        internal float CoordinatedYawRatePerBankDeg = 0.10f;
        internal float CoordinatedYawRateGain = 0.012f;
        internal float CoordinatedYawMinBankDeg = 3.0f;
        internal float CoordinatedYawSlewPerSec = 1.25f;

        // v0.9.12 roll-first adaptive high-energy turn supervisor with continuous predictive margin authority,
        // measured-G capability adaptation, AA LIMIT HOLD and recoverable STALL RECOVERY. Entry is based on the requested
        // operational envelope (ALT >= 3000 m and surface speed >= 600 m/s), not on a
        // weak-response dwell. Once entered, the manoeuvre is latched and follows a
        // BUILD -> SUSTAIN -> ROLLOUT trajectory. Measured heading response remains
        // diagnostic/efficiency evidence only and can never cancel a successful assist.
        internal float ThinAirDensityEntryKgM3 = 0.080f;
        internal float ThinAirDensityFullKgM3 = 0.015f;
        internal float ThinAirMinimumAltitudeMeters = 3000f;
        internal float ThinAirReleaseAltitudeMeters = 2700f;
        internal float ThinAirMinimumSurfaceSpeedMps = 600f;
        internal float ThinAirReleaseSurfaceSpeedMps = 540f;
        internal float ThinAirMinimumStallMarginDeg = 5f;
        internal float ThinAirMinimumStallMarginNormalized = 0.25f;
        internal float ThinAirSustainStallMarginDeg = 2.5f;
        internal float ThinAirSustainStallMarginNormalized = 0.10f;
        internal float ThinAirCriticalStallMarginDeg = 1.5f;
        internal float ThinAirMinimumHeadingErrorDeg = 20f;
        internal float ThinAirHeadingRolloutStartDeg = 18f;
        internal float ThinAirHeadingErrorReleaseDeg = 8f;
        internal float ThinAirEntryDwellSeconds = 0.20f;
        internal float ThinAirReleaseDwellSeconds = 0.75f;
        internal float ThinAirMinimumLatchedSeconds = 2.50f;
        internal float ThinAirRearmCooldownSeconds = 2.00f;
        internal float ThinAirWeakResponseRatio = 0.60f; // diagnostic only
        internal float ThinAirAssistRiseRatePerSec = 0.55f;
        internal float ThinAirAssistFallRatePerSec = 1.40f;
        internal float ThinAirMinimumBankDeg = 45f;
        internal float ThinAirMaximumBankDeg = 80f;
        internal float ThinAirRollInTargetBankDeg = 40f;
        internal float ThinAirPitchEnableBankDeg = 32f;
        internal float ThinAirPitchFullBankDeg = 45f;
        internal float ThinAirRollInBankToleranceDeg = 4f;
        internal float ThinAirRollInMaxRollRateDegPerSec = 8f;
        internal float ThinAirRollInStableDwellSeconds = 0.35f;
        internal float ThinAirFullBankSurfaceSpeedMps = 1800f;
        internal float ThinAirMinimumTargetG = 1f;
        internal float ThinAirMaximumTargetG = 9f;
        internal float ThinAirGCommandRisePerSec = 0.65f;
        internal float ThinAirGCommandFallPerSec = 4.00f;
        internal float ThinAirMinimumPitchAssistDegPerSec = 1.75f;
        internal float ThinAirMaximumPitchAssistDegPerSec = 12.0f;
        internal float ThinAirFullPitchSurfaceSpeedMps = 1800f;
        internal float ThinAirMaximumPitchFloorDegPerSec = 6.50f;
        internal float ThinAirPitchTargetGLeadFraction = 0.65f;
        internal float ThinAirPitchGFeedbackGain = 0.60f;
        internal float ThinAirHighBankRolloutLeadSeconds = 4.50f;
        internal float ThinAirMaximumYawRateDegPerSec = 5.50f;
        internal float ThinAirAaLimitExitDwellSeconds = 0.75f;
        internal float ThinAirProtectUnavailableReleaseDwellSeconds = 1.25f;
        internal float ThinAirCriticalRecoveryReleaseDwellSeconds = 3.00f;
        internal float ThinAirAaLimitMaximumG = 3.00f;
        internal float ThinAirAaLimitEnergyMaximumG = 2.00f;
        internal float ThinAirAaLimitAssistBlend = 0.55f;
        internal float ThinAirAaLimitDefaultPitchCapDegPerSec = 1.25f;
        internal float ThinAirMarginPredictionSeconds = 1.25f;
        internal float ThinAirMarginGovernorSoftDeg = 6.50f;
        internal float ThinAirMarginGovernorHardDeg = 3.00f;
        internal float ThinAirMarginGovernorSoftNormalized = 0.25f;
        internal float ThinAirMarginGovernorHardNormalized = 0.08f;
        internal float ThinAirMarginGovernorRecoveryDeg = 7.50f;
        internal float ThinAirMarginGovernorRecoveryNormalized = 0.30f;
        internal float ThinAirMarginGovernorRecoveryDwellSeconds = 2.00f;
        internal float ThinAirMarginGovernorFallRatePerSec = 1.60f;
        internal float ThinAirMarginGovernorRiseRatePerSec = 0.16f;
        internal float ThinAirHighAltitudeEnvelopeStartMeters = 15000f;
        internal float ThinAirHighAltitudeEnvelopeFullMeters = 19000f;
        internal float ThinAirLowQEnvelopeFullKpa = 31.0f;
        internal float ThinAirLowQEnvelopeClearKpa = 38.0f;
        internal float ThinAirLowQMaximumPitchDegPerSec = 3.00f;
        internal float ThinAirCapabilitySeedSustainableG = 1.30f;
        internal float ThinAirCapabilityHeadroomG = 0.25f;
        internal float ThinAirCapabilityMinimumG = 1.05f;
        internal float ThinAirCapabilityTrackingToleranceG = 0.20f;
        internal float ThinAirCapabilityRisePerSec = 0.08f;
        internal float ThinAirCapabilityFallPerSec = 0.35f;
        internal float ThinAirCapabilityRelaxPerSec = 0.025f;
        internal float ThinAirCapabilityMinimumBankDeg = 35f;
        internal float ThinAirCapabilityBankRiseDegPerSec = 1.25f;
        internal float ThinAirCapabilityBankFallDegPerSec = 3.00f;
        internal float ThinAirLowQBankTargetSlewDegPerSec = 1.75f;
        internal float ThinAirLowQRollInSlewDegPerSec = 4.00f;
        internal float ThinAirTurnYawFullBelowBankDeg = 30f;
        internal float ThinAirTurnYawZeroAboveBankDeg = 75f;
        internal float ThinAirLowQTurnYawFullBelowBankDeg = 25f;
        internal float ThinAirLowQTurnYawZeroAboveBankDeg = 60f;
        internal float ThinAirStabilityYawStartBankDeg = 25f;
        internal float ThinAirStabilityYawFullBankDeg = 60f;
        internal float ThinAirStabilityYawSideslipGain = 0.12f;
        internal float ThinAirStabilityYawRateDampingGain = 0.18f;
        internal float ThinAirStabilityYawAccelerationDampingGain = 0.010f;
        internal float ThinAirStabilityYawMaximumRateDegPerSec = 2.50f;
        internal float ThinAirStabilityYawSlewDegPerSec2 = 7.00f;
        internal float ThinAirGravityMps2 = 9.80665f;

        internal bool ThinAirTurnLatched { get; private set; }
        internal string ThinAirTurnPhase { get; private set; } = "STANDBY";
        internal float ThinAirTurnTargetG { get; private set; } = 1f;
        internal float ThinAirTurnCommandedG { get; private set; } = 1f;
        internal float ThinAirTurnMeasuredG { get; private set; } = 1f;
        internal float ThinAirTurnStabilityScore { get; private set; }
        internal float ThinAirTurnStallAuthority { get; private set; }
        internal float ThinAirTurnTrackingAuthority { get; private set; } = 1f;
        internal float ThinAirTurnBankSpeedBlend { get; private set; }
        internal float ThinAirTurnPitchKinematicRateDegPerSec { get; private set; }
        internal float ThinAirTurnPitchFloorRateDegPerSec { get; private set; }
        internal float ThinAirTurnPitchFeedbackRateDegPerSec { get; private set; }
        internal float ThinAirTurnRolloutLeadDeg { get; private set; }
        internal float ThinAirTurnEntryElapsedSeconds { get; private set; }
        internal float ThinAirTurnLatchedElapsedSeconds { get; private set; }
        internal float ThinAirTurnReleaseElapsedSeconds { get; private set; }
        internal string ThinAirTurnReleaseReason { get; private set; } = "NONE";
        internal bool ThinAirAaLimitHoldActive { get; private set; }
        internal string ThinAirAaLimitHoldReason { get; private set; } = "NONE";
        internal float ThinAirAaLimitHoldElapsedSeconds { get; private set; }
        internal float ThinAirAaLimitRecoveryElapsedSeconds { get; private set; }
        internal float ThinAirCriticalConditionElapsedSeconds { get; private set; }
        internal float ThinAirAaPitchRequestedDegPerSec { get; private set; }
        internal float ThinAirAaPitchAppliedDegPerSec { get; private set; }
        internal float ThinAirAaPitchModerationDeltaDegPerSec { get; private set; }
        internal float ThinAirAaPitchAuthority { get; private set; } = 1f;
        internal float ThinAirAaLimitGCap { get; private set; } = 9f;
        internal float ThinAirAaLimitPitchCapDegPerSec { get; private set; } = 12f;
        internal bool ThinAirMarginGovernorActive { get; private set; }
        internal string ThinAirMarginGovernorReason { get; private set; } = "NONE";
        internal float ThinAirStallMarginRateDegPerSec { get; private set; }
        internal float ThinAirPredictedStallMarginDeg { get; private set; }
        internal float ThinAirMarginGovernorAuthority { get; private set; } = 1f;
        internal float ThinAirMarginRecoveryElapsedSeconds { get; private set; }
        internal float ThinAirLowQEnvelopeBlend { get; private set; }
        internal float ThinAirLowQBankCapDeg { get; private set; } = 80f;
        internal float ThinAirLowQGCap { get; private set; } = 9f;
        internal float ThinAirLowQPitchCapDegPerSec { get; private set; } = 12f;
        internal float ThinAirEstimatedSustainableG { get; private set; } = 1.30f;
        internal float ThinAirCapabilityGCap { get; private set; } = 1.55f;
        internal float ThinAirCapabilityTrackingErrorG { get; private set; }
        internal bool ThinAirCapabilityLimited { get; private set; }
        internal float ThinAirCapabilityBankCapDeg { get; private set; } = 50f;
        internal float ThinAirBankTargetSlewLimitDegPerSec { get; private set; }
        internal bool ThinAirStallRecoveryActive { get; private set; }
        internal float ThinAirTurnYawBankFade { get; private set; } = 1f;
        internal float ThinAirTurnYawRateTargetDegPerSec { get; private set; }
        internal float AttitudeStabilityYawRateTargetDegPerSec { get; private set; }
        internal float AttitudeStabilityYawRateCommandDegPerSec { get; private set; }
        internal float AttitudeStabilityYawSideslipTermDegPerSec { get; private set; }
        internal float AttitudeStabilityYawRateDampingTermDegPerSec { get; private set; }
        internal float AttitudeStabilityYawAccelerationDampingTermDegPerSec { get; private set; }
        internal string YawAssistMode { get; private set; } = "OFF";

        float lastUpdateTime;
        float terminalRollAssistReverseSince;
        float thinAirEntrySince;
        float thinAirReleaseSince;
        float thinAirLatchedSince;
        float thinAirLastReleaseTime = -100f;
        float thinAirRollInStableSince;
        float thinAirAaLimitHoldSince;
        float thinAirAaLimitClearSince;
        float thinAirCriticalConditionSince;
        float thinAirMarginGovernorClearSince;
        bool thinAirReleaseRequested;
        float filteredThinAirTurnResponseRatio = 1f;
        float filteredThinAirStallMarginRateDegPerSec;
        float filteredThinAirMarginGovernorAuthority = 1f;
        float filteredThinAirSustainableG = 1.30f;
        float filteredThinAirCapabilityBankCapDeg = 50f;
        float previousThinAirStallMarginDeg;
        bool hasPreviousThinAirStallMargin;
        float filteredThinAirAaPitchAuthority = 1f;
        float filteredThinAirGTrackingAuthority = 1f;
        float filteredThinAirGRate;
        float previousThinAirMeasuredG = 1f;
        float lastThinAirHeadingDeg;
        bool hasLastThinAirHeading;
        bool lastLoggedThinAirTurnAssistActive;
        bool lastLoggedSafeLowSpeedBankAuthorityActive;
        bool lastLoggedThinAirAaLimitHoldActive;
        string lastLoggedThinAirAaLimitHoldReason = "NONE";
        bool lastLoggedThinAirMarginGovernorActive;
        string lastLoggedThinAirMarginGovernorReason = "NONE";

        internal void SetAutoMaxBankLimit()
        {
            UseAutoMaxBankLimit = true;
            AERISLogger.Info("[HDG] bank limit=AUTO");
        }

        internal void SetManualMaxBankLimit(float degrees)
        {
            if (float.IsNaN(degrees) || float.IsInfinity(degrees)) degrees = 30f;
            ManualMaxBankLimitDeg = Mathf.Clamp(degrees, 0f, 90f);
            UseAutoMaxBankLimit = false;
            AERISLogger.Info("[HDG] bank limit=MANUAL " + ManualMaxBankLimitDeg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }

        internal bool TrySetManualMaxBankLimit(string text, out string error)
        {
            error = null;
            float value;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value < 0f || value > 90f)
            {
                error = "Enter a bank limit from 0 to 90 degrees.";
                return false;
            }
            SetManualMaxBankLimit(value);
            ManualMaxBankLimitText = ManualMaxBankLimitDeg.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        internal void SetArmed(bool armed, Vessel vessel, AERISBankDirector bank, VirtualAttitudeInstrument attitude)
        {
            if (!armed)
            {
                if (bank != null) bank.SetHdgTerminalQuietMode(false);
                Armed = false;
                ControlState = "Inactive";
                CommandedBankTarget = 0f;
                TerminalYawActive = false;
                TerminalYawCommand = 0f;
                TerminalYawRawCommand = 0f;
                TerminalYawCaptureBand = "OFF";
                TerminalYawProportionalTerm = 0f;
                TerminalYawRateDampingTerm = 0f;
                CoordinatedYawCommand = 0f;
                CoordinatedYawFeedForward = 0f;
                CoordinatedYawRateCorrection = 0f;
                VirtualYawCommand = 0f;
                ResetNativeYawRateState(true);
                ClearAaNativeYawRateOverride();
                RolloutHoldActive = false;
                TerminalRollAssistDeg = 0f;
                TerminalRollAssistActive = false;
                TerminalRollAssistHoldActive = false;
                TerminalRollAssistReversePending = false;
                TerminalRollAssistFilteredDeg = 0f;
                TerminalRollAssistRawDeg = 0f;
                TerminalBankQuietZoneActive = false;
                terminalRollAssistReverseSince = 0f;
                lastUpdateTime = 0f;
                ResetSafeLowSpeedBankAuthority("HDG RELEASED");
                ResetThinAirTurnAssist(true);
                AERISLogger.Info("[HDG] released UI");
                return;
            }
            if (vessel == null || bank == null)
            {
                AERISLogger.Warn("[HDG] arm rejected: vessel/BANK unavailable.");
                return;
            }
            Armed = true;
            ResetNativeYawRateState(true);
            ClearAaNativeYawRateOverride();
            if (string.IsNullOrEmpty(TargetHeadingText)) SetCurrent(attitude);
            bank.SetArmed(true, vessel);
            CommandedBankTarget = bank.TargetBank;
            ControlState = "Armed";
            AERISLogger.Info("[HDG] armed target=" + TargetHeading.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }


        internal bool TrySetTarget(string text, out string error)
        {
            error = null;
            float value;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a heading from 0.0 to 359.9."; return false;
            }
            value = value % 360f; if (value < 0f) value += 360f;
            TargetHeading = value; TargetHeadingText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }

        internal void SetCurrent(VirtualAttitudeInstrument attitude)
        {
            if (attitude == null || !attitude.InstrumentHeadingValid)
            {
                AERISLogger.Warn("[HDG] SET CURRENT rejected: formal heading is unavailable.");
                return;
            }
            float h = attitude.InstrumentHeadingDeg % 360f; if (h < 0f) h += 360f;
            TargetHeading = h; CurrentHeading = h;
            TargetHeadingText = h.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            AERISLogger.Info("[HDG] target set to current formal heading=" + TargetHeading.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        }

        internal void Update(Vessel vessel, VirtualAttitudeInstrument attitude, AERISBankDirector bank,
            ProtectTelemetry protect, bool aerisMaster, bool standardFbwActive)
        {
            TerminalYawActive = false;
            TerminalYawRawCommand = 0f;
            TerminalYawCaptureBand = "OFF";
            TerminalYawProportionalTerm = 0f;
            TerminalYawRateDampingTerm = 0f;
            NativeYawTransportEligible = false;
            YawRateRequestDegPerSec = 0f;
            YawRateActualDegPerSec = attitude != null ? attitude.YawRateDegPerSec : 0f;
            TerminalYawRateRawDegPerSec = 0f;
            TerminalYawRateProportionalTermDegPerSec = 0f;
            TerminalYawRateDampingTermDegPerSec = 0f;
            CoordinatedYawRateTargetDegPerSec = 0f;
            CoordinatedYawFeedForward = 0f;
            CoordinatedYawRateCorrection = 0f;
            ThinAirTurnYawRateTargetDegPerSec = 0f;
            AttitudeStabilityYawRateTargetDegPerSec = 0f;
            AttitudeStabilityYawSideslipTermDegPerSec = 0f;
            AttitudeStabilityYawRateDampingTermDegPerSec = 0f;
            AttitudeStabilityYawAccelerationDampingTermDegPerSec = 0f;
            YawAssistMode = "OFF";
            TerminalRollAssistDeg = 0f;
            TerminalRollAssistActive = false;
            TerminalRollAssistHoldActive = false;
            TerminalRollAssistReversePending = false;
            TerminalRollAssistRawDeg = 0f;
            TerminalBankQuietZoneActive = false;
            ThinAirTurnPitchAssistRateDegPerSec = 0f;
            if (!Armed)
            {
                ResetSafeLowSpeedBankAuthority("HDG INACTIVE");
                if (bank != null) bank.SetHdgTerminalQuietMode(false);
                ControlState = "Inactive";
                TerminalYawCommand = 0f; CoordinatedYawCommand = 0f; VirtualYawCommand = 0f;
                ResetNativeYawRateState(true);
                ResetThinAirTurnAssist(true);
                return;
            }
            if (vessel == null || bank == null || !aerisMaster || !standardFbwActive || vessel.LandedOrSplashed || vessel.packed)
            {
                ResetSafeLowSpeedBankAuthority("EXECUTION STANDBY");
                if (bank != null) bank.SetHdgTerminalQuietMode(false);
                ControlState = "Standby";
                TerminalYawCommand = 0f; CoordinatedYawCommand = 0f; VirtualYawCommand = 0f;
                ResetNativeYawRateState(true);
                ResetThinAirTurnAssist(true);
                return;
            }
            if (attitude == null || !attitude.InstrumentHeadingValid || !attitude.InstrumentHorizonBankValid)
            {
                ResetSafeLowSpeedBankAuthority("ATTITUDE INVALID");
                bank.SetHdgTerminalQuietMode(false);
                ControlState = "HeadingInvalid";
                TerminalYawCommand = 0f; CoordinatedYawCommand = 0f; VirtualYawCommand = 0f;
                ResetNativeYawRateState(true);
                ResetThinAirTurnAssist(true);
                return;
            }

            CurrentHeading = attitude.InstrumentHeadingDeg;
            HeadingError = Mathf.DeltaAngle(CurrentHeading, TargetHeading);
            float now = Time.realtimeSinceStartup;
            float dt = lastUpdateTime > 0f ? Mathf.Clamp(now - lastUpdateTime, 0.001f, 0.10f) : Time.fixedDeltaTime;
            lastUpdateTime = now;
            ThinAirTurnObservedHeadingRateDegPerSec = hasLastThinAirHeading
                ? Mathf.DeltaAngle(lastThinAirHeadingDeg, CurrentHeading) / Mathf.Max(0.001f, dt)
                : 0f;
            lastThinAirHeadingDeg = CurrentHeading;
            hasLastThinAirHeading = true;
            // KSP/mod telemetry can transiently contain NaN/Infinity during vessel
            // unpacking or aero-model changes.  Letting either value enter q-based
            // scheduling poisons every subsequent bank/turn limiter for the frame.
            float densitySample = IsFinite((float)vessel.atmDensity)
                ? Mathf.Max(0f, (float)vessel.atmDensity) : 0f;
            float surfaceSpeedSample = IsFinite((float)vessel.srfSpeed)
                ? Mathf.Max(0f, (float)vessel.srfSpeed) : 0f;
            float qKpa = 0.5f * densitySample * surfaceSpeedSample * surfaceSpeedSample / 1000f;
            if (!IsFinite(qKpa)) qKpa = 0f;
            float qT = Mathf.Clamp01((qKpa - bank.DynamicPressureLowQKpa) / Mathf.Max(0.01f, bank.DynamicPressureMediumQKpa - bank.DynamicPressureLowQKpa));
            float maxBank = Mathf.Lerp(MaxBankLowQ, MaxBankMidQ, qT);
            float gain = Mathf.Lerp(HeadingToBankGainLowQ, HeadingToBankGainMidQ, qT);
            float highT = Mathf.Clamp01((qKpa - bank.DynamicPressureHighQStartKpa) / Mathf.Max(0.01f, bank.DynamicPressureHighQFullKpa - bank.DynamicPressureHighQStartKpa));
            // High q: retain a decisive turning envelope (up to 45 deg AUTO), not a
            // reduced one. The BANK director still owns smooth target motion and
            // reversal suppression, so this does not bypass AA or BANK protections.
            maxBank = Mathf.Lerp(maxBank, MaxBankHighQ, highT);
            gain *= Mathf.Lerp(1f, 0.92f, highT);

            float absError = Mathf.Abs(HeadingError);
            float density = densitySample;
            ThinAirBlend = 1f - Mathf.InverseLerp(ThinAirDensityFullKgM3,
                ThinAirDensityEntryKgM3, density);
            ThinAirBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(ThinAirBlend));
            float priorYawDemand = Mathf.Abs(CoordinatedYawRateCommandDegPerSec);
            // Retain actual-heading response as an efficiency diagnostic. v0.9.6 never
            // uses this ratio as an entry, continuation, or release gate: a successful
            // manoeuvre is therefore unable to cancel itself by improving response.
            float instantResponseRatio = priorYawDemand > 0.05f
                ? Mathf.Abs(ThinAirTurnObservedHeadingRateDegPerSec) / Mathf.Max(0.05f, priorYawDemand)
                : 1f;
            filteredThinAirTurnResponseRatio = Mathf.Lerp(filteredThinAirTurnResponseRatio,
                Mathf.Clamp(instantResponseRatio, 0f, 2f), Mathf.Clamp01(dt * 1.5f));
            ThinAirTurnResponseRatio = filteredThinAirTurnResponseRatio;
            ThinAirTurnWeakResponseElapsedSeconds = 0f;

            ThinAirTurnObservedAltitudeMeters = IsFinite((float)vessel.altitude)
                ? Mathf.Max(0f, (float)vessel.altitude) : 0f;
            ThinAirTurnObservedSurfaceSpeedMps = surfaceSpeedSample;
            ThinAirTurnObservedStallMarginDeg = protect != null &&
                IsFinite(protect.StallMarginDegrees) ? protect.StallMarginDegrees : 0f;
            ThinAirTurnMeasuredG = attitude != null && IsFinite(attitude.GeeForce)
                ? Mathf.Clamp(Mathf.Abs(attitude.GeeForce), 0f, 20f) : 1f;
            float measuredGRate = (ThinAirTurnMeasuredG - previousThinAirMeasuredG) / Mathf.Max(0.001f, dt);
            previousThinAirMeasuredG = ThinAirTurnMeasuredG;
            filteredThinAirGRate = Mathf.Lerp(filteredThinAirGRate, measuredGRate, Mathf.Clamp01(dt * 3f));

            bool protectAvailable = protect != null && protect.Risk != ProtectRiskLevel.Unavailable;
            bool protectCaution = protectAvailable && protect.Risk == ProtectRiskLevel.Caution;
            bool protectStallRisk = protectAvailable && protect.Risk == ProtectRiskLevel.StallRisk;
            bool protectStallDetected = protectAvailable && protect.Risk == ProtectRiskLevel.StallDetected;
            bool protectEnergyLimit = protectAvailable && protect.EnergyCollapseDetected;

            float rawStallMarginRate = 0f;
            if (protectAvailable)
            {
                if (hasPreviousThinAirStallMargin)
                    rawStallMarginRate = (protect.StallMarginDegrees - previousThinAirStallMarginDeg) /
                        Mathf.Max(0.001f, dt);
                previousThinAirStallMarginDeg = protect.StallMarginDegrees;
                hasPreviousThinAirStallMargin = true;
                filteredThinAirStallMarginRateDegPerSec = Mathf.Lerp(
                    filteredThinAirStallMarginRateDegPerSec,
                    Mathf.Clamp(rawStallMarginRate, -30f, 30f), Mathf.Clamp01(dt * 2.5f));
            }
            else
            {
                hasPreviousThinAirStallMargin = false;
                filteredThinAirStallMarginRateDegPerSec = Mathf.MoveTowards(
                    filteredThinAirStallMarginRateDegPerSec, 0f, 4f * dt);
            }
            ThinAirStallMarginRateDegPerSec = filteredThinAirStallMarginRateDegPerSec;
            ThinAirPredictedStallMarginDeg = protectAvailable
                ? protect.StallMarginDegrees + Mathf.Min(0f, ThinAirStallMarginRateDegPerSec) *
                    ThinAirMarginPredictionSeconds
                : 0f;
            float predictiveMarginDegAuthority = protectAvailable
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinAirMarginGovernorHardDeg,
                    ThinAirMarginGovernorSoftDeg, ThinAirPredictedStallMarginDeg))
                : 0f;
            float predictiveMarginNormAuthority = protectAvailable
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinAirMarginGovernorHardNormalized,
                    ThinAirMarginGovernorSoftNormalized, protect.StallMarginNormalized))
                : 0f;
            float rawPredictiveMarginAuthority = Mathf.Min(predictiveMarginDegAuthority,
                predictiveMarginNormAuthority);
            bool marginRecoveryEligible = protectAvailable && !protectStallRisk && !protectStallDetected &&
                !protectEnergyLimit && ThinAirPredictedStallMarginDeg >= ThinAirMarginGovernorRecoveryDeg &&
                protect.StallMarginNormalized >= ThinAirMarginGovernorRecoveryNormalized &&
                ThinAirStallMarginRateDegPerSec >= -0.10f;
            if (rawPredictiveMarginAuthority < filteredThinAirMarginGovernorAuthority)
            {
                thinAirMarginGovernorClearSince = 0f;
                ThinAirMarginRecoveryElapsedSeconds = 0f;
                filteredThinAirMarginGovernorAuthority = Mathf.MoveTowards(
                    filteredThinAirMarginGovernorAuthority, rawPredictiveMarginAuthority,
                    ThinAirMarginGovernorFallRatePerSec * dt);
            }
            else if (marginRecoveryEligible)
            {
                if (thinAirMarginGovernorClearSince <= 0f) thinAirMarginGovernorClearSince = now;
                ThinAirMarginRecoveryElapsedSeconds = now - thinAirMarginGovernorClearSince;
                if (ThinAirMarginRecoveryElapsedSeconds >= ThinAirMarginGovernorRecoveryDwellSeconds)
                    filteredThinAirMarginGovernorAuthority = Mathf.MoveTowards(
                        filteredThinAirMarginGovernorAuthority, rawPredictiveMarginAuthority,
                        ThinAirMarginGovernorRiseRatePerSec * dt);
            }
            else
            {
                thinAirMarginGovernorClearSince = 0f;
                ThinAirMarginRecoveryElapsedSeconds = 0f;
            }
            ThinAirMarginGovernorAuthority = Mathf.Clamp01(filteredThinAirMarginGovernorAuthority);

            float highAltitudeEnvelopeBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirHighAltitudeEnvelopeStartMeters,
                    ThinAirHighAltitudeEnvelopeFullMeters, ThinAirTurnObservedAltitudeMeters));
            float lowQEnvelopeBlend = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirLowQEnvelopeFullKpa,
                    ThinAirLowQEnvelopeClearKpa, qKpa));
            ThinAirLowQEnvelopeBlend = Mathf.Clamp01(highAltitudeEnvelopeBlend * lowQEnvelopeBlend);
            ThinAirLowQPitchCapDegPerSec = Mathf.Lerp(ThinAirMaximumPitchAssistDegPerSec,
                ThinAirLowQMaximumPitchDegPerSec, ThinAirLowQEnvelopeBlend);

            ThinAirAaPitchRequestedDegPerSec = StandardFlyByWire.LastPitchRateRequestedRadPerSec * Mathf.Rad2Deg;
            ThinAirAaPitchAppliedDegPerSec = StandardFlyByWire.LastPitchRateAppliedRadPerSec * Mathf.Rad2Deg;
            ThinAirAaPitchModerationDeltaDegPerSec = StandardFlyByWire.LastPitchRateModerationDeltaRadPerSec * Mathf.Rad2Deg;
            bool aaPitchModerationActive = StandardFlyByWire.LastPitchRateExternalControlActive &&
                StandardFlyByWire.LastPitchRateModerationEnvelopeAvailable &&
                StandardFlyByWire.LastPitchRateModerationActive;
            bool aaPitchNoseUpLimited = aaPitchModerationActive &&
                ThinAirAaPitchRequestedDegPerSec > 0.75f &&
                ThinAirAaPitchModerationDeltaDegPerSec < -0.50f &&
                ThinAirAaPitchAppliedDegPerSec < ThinAirAaPitchRequestedDegPerSec * 0.85f;
            float rawAaPitchAuthority = ThinAirAaPitchRequestedDegPerSec > 0.25f
                ? Mathf.Clamp01(Mathf.Max(0f, ThinAirAaPitchAppliedDegPerSec) /
                    Mathf.Max(0.25f, ThinAirAaPitchRequestedDegPerSec))
                : 1f;
            filteredThinAirAaPitchAuthority = Mathf.MoveTowards(filteredThinAirAaPitchAuthority,
                rawAaPitchAuthority, (rawAaPitchAuthority < filteredThinAirAaPitchAuthority ? 3.0f : 0.8f) * dt);
            ThinAirAaPitchAuthority = filteredThinAirAaPitchAuthority;

            bool entryStallSafe = protectAvailable && !protectStallRisk && !protectStallDetected &&
                !protectEnergyLimit && protect.StallMarginDegrees >= ThinAirMinimumStallMarginDeg &&
                protect.StallMarginNormalized >= ThinAirMinimumStallMarginNormalized;
            bool sustainStallSafe = protectAvailable &&
                protect.StallMarginDegrees >= ThinAirSustainStallMarginDeg &&
                protect.StallMarginNormalized >= ThinAirSustainStallMarginNormalized;
            bool criticalMarginExceeded = protectAvailable &&
                protect.StallMarginDegrees < ThinAirCriticalStallMarginDeg &&
                protect.StallMarginNormalized < 0.05f;
            bool recoverableCriticalStall = protectAvailable && (protectStallDetected || criticalMarginExceeded);
            bool hardCriticalStall = !protectAvailable || recoverableCriticalStall;
            bool transientLimitRequest = aaPitchNoseUpLimited || protectStallRisk ||
                recoverableCriticalStall || protectEnergyLimit || !protectAvailable;

            ThinAirMarginGovernorActive = ThinAirTurnLatched && protectAvailable &&
                !protectStallDetected && ThinAirMarginGovernorAuthority < 0.995f;
            ThinAirMarginGovernorReason = !ThinAirMarginGovernorActive ? "NONE" :
                (ThinAirPredictedStallMarginDeg + 0.25f < protect.StallMarginDegrees
                    ? "PREDICTED MARGIN" : "LOW MARGIN");

            ThinAirTurnAltitudeQualified = ThinAirTurnObservedAltitudeMeters >=
                (ThinAirTurnLatched ? ThinAirReleaseAltitudeMeters : ThinAirMinimumAltitudeMeters);
            ThinAirTurnSpeedQualified = ThinAirTurnObservedSurfaceSpeedMps >=
                (ThinAirTurnLatched ? ThinAirReleaseSurfaceSpeedMps : ThinAirMinimumSurfaceSpeedMps);
            ThinAirTurnStallMarginQualified = ThinAirTurnLatched ? sustainStallSafe : entryStallSafe;
            ThinAirTurnHeadingErrorQualified = absError >=
                (ThinAirTurnLatched ? ThinAirHeadingErrorReleaseDeg : ThinAirMinimumHeadingErrorDeg);

            bool entryEnvelope = ThinAirTurnAssistEnabled && UseAutoMaxBankLimit &&
                ThinAirTurnObservedAltitudeMeters >= ThinAirMinimumAltitudeMeters &&
                ThinAirTurnObservedSurfaceSpeedMps >= ThinAirMinimumSurfaceSpeedMps &&
                entryStallSafe && absError >= ThinAirMinimumHeadingErrorDeg &&
                now - thinAirLastReleaseTime >= ThinAirRearmCooldownSeconds;
            if (!ThinAirTurnLatched)
            {
                thinAirReleaseRequested = false;
                ThinAirAaLimitHoldActive = false;
                ThinAirAaLimitHoldReason = "NONE";
                ThinAirAaLimitHoldElapsedSeconds = 0f;
                ThinAirAaLimitRecoveryElapsedSeconds = 0f;
                ThinAirCriticalConditionElapsedSeconds = 0f;
                ThinAirStallRecoveryActive = false;
                ThinAirMarginGovernorActive = false;
                ThinAirMarginGovernorReason = "NONE";
                ThinAirMarginRecoveryElapsedSeconds = 0f;
                thinAirAaLimitHoldSince = 0f;
                thinAirAaLimitClearSince = 0f;
                thinAirCriticalConditionSince = 0f;
                thinAirMarginGovernorClearSince = 0f;
                ThinAirTurnReleaseReason = "NONE";
                ThinAirTurnReleaseElapsedSeconds = 0f;
                if (entryEnvelope)
                {
                    if (thinAirEntrySince <= 0f) thinAirEntrySince = now;
                    ThinAirTurnEntryElapsedSeconds = now - thinAirEntrySince;
                    ThinAirTurnPhase = "ENTRY";
                    if (ThinAirTurnEntryElapsedSeconds >= ThinAirEntryDwellSeconds)
                    {
                        ThinAirTurnLatched = true;
                        thinAirLatchedSince = now;
                        thinAirReleaseSince = 0f;
                        ThinAirTurnLatchedElapsedSeconds = 0f;
                        filteredThinAirSustainableG = Mathf.Clamp(
                            Mathf.Max(ThinAirCapabilitySeedSustainableG, ThinAirTurnMeasuredG),
                            ThinAirCapabilityMinimumG, ThinAirMaximumTargetG);
                        ThinAirEstimatedSustainableG = filteredThinAirSustainableG;
                        ThinAirCapabilityGCap = Mathf.Clamp(ThinAirEstimatedSustainableG +
                            ThinAirCapabilityHeadroomG, ThinAirCapabilityMinimumG, ThinAirMaximumTargetG);
                        float seededBankCap = Mathf.Acos(Mathf.Clamp(1f /
                            Mathf.Max(1.001f, ThinAirCapabilityGCap), 0f, 1f)) * Mathf.Rad2Deg;
                        filteredThinAirCapabilityBankCapDeg = Mathf.Clamp(seededBankCap,
                            ThinAirCapabilityMinimumBankDeg, ThinAirMaximumBankDeg);
                        ThinAirTurnPhase = "ROLL-IN";
                    }
                }
                else
                {
                    thinAirEntrySince = 0f;
                    ThinAirTurnEntryElapsedSeconds = 0f;
                    ThinAirTurnPhase = "STANDBY";
                }
            }
            else
            {
                ThinAirTurnLatchedElapsedSeconds = now - thinAirLatchedSince;
                bool belowKinematicEnvelope = ThinAirTurnObservedAltitudeMeters < ThinAirReleaseAltitudeMeters ||
                    ThinAirTurnObservedSurfaceSpeedMps < ThinAirReleaseSurfaceSpeedMps;
                if (!ThinAirTurnAssistEnabled || !UseAutoMaxBankLimit)
                {
                    thinAirReleaseRequested = true;
                    ThinAirTurnReleaseReason = !ThinAirTurnAssistEnabled ? "DISABLED" : "MANUAL BANK LIMIT";
                }

                ThinAirStallRecoveryActive = recoverableCriticalStall;
                if (!thinAirReleaseRequested && hardCriticalStall)
                {
                    if (thinAirCriticalConditionSince <= 0f) thinAirCriticalConditionSince = now;
                    ThinAirCriticalConditionElapsedSeconds = now - thinAirCriticalConditionSince;
                    float criticalReleaseDwell = !protectAvailable
                        ? ThinAirProtectUnavailableReleaseDwellSeconds
                        : ThinAirCriticalRecoveryReleaseDwellSeconds;
                    bool recoveryFailed = !protectAvailable || protectStallDetected ||
                        ThinAirPredictedStallMarginDeg <= 0.25f;
                    if (ThinAirCriticalConditionElapsedSeconds >= criticalReleaseDwell && recoveryFailed)
                    {
                        thinAirReleaseRequested = true;
                        ThinAirTurnReleaseReason = !protectAvailable ? "PROTECT UNAVAILABLE" : "STALL RECOVERY FAILED";
                    }
                }
                else if (!hardCriticalStall)
                {
                    thinAirCriticalConditionSince = 0f;
                    ThinAirCriticalConditionElapsedSeconds = 0f;
                    ThinAirStallRecoveryActive = false;
                }

                if (!thinAirReleaseRequested && absError <= ThinAirHeadingErrorReleaseDeg &&
                    ThinAirTurnLatchedElapsedSeconds >= ThinAirMinimumLatchedSeconds)
                {
                    thinAirReleaseRequested = true;
                    ThinAirTurnReleaseReason = "HDG CAPTURE";
                }
                else if (!thinAirReleaseRequested && belowKinematicEnvelope)
                {
                    if (thinAirReleaseSince <= 0f) thinAirReleaseSince = now;
                    ThinAirTurnReleaseElapsedSeconds = now - thinAirReleaseSince;
                    if (ThinAirTurnReleaseElapsedSeconds >= ThinAirReleaseDwellSeconds)
                    {
                        thinAirReleaseRequested = true;
                        ThinAirTurnReleaseReason = ThinAirTurnObservedAltitudeMeters < ThinAirReleaseAltitudeMeters
                            ? "ALTITUDE" : "SPEED";
                    }
                }
                else if (!belowKinematicEnvelope)
                {
                    thinAirReleaseSince = 0f;
                    ThinAirTurnReleaseElapsedSeconds = 0f;
                }

                if (!thinAirReleaseRequested && transientLimitRequest)
                {
                    if (thinAirAaLimitHoldSince <= 0f) thinAirAaLimitHoldSince = now;
                    thinAirAaLimitClearSince = 0f;
                    ThinAirAaLimitHoldActive = true;
                    ThinAirAaLimitHoldElapsedSeconds = now - thinAirAaLimitHoldSince;
                    ThinAirAaLimitRecoveryElapsedSeconds = 0f;
                    ThinAirAaLimitHoldReason = !protectAvailable ? "PROTECT UNAVAILABLE" :
                        (ThinAirStallRecoveryActive ? "STALL RECOVERY" :
                        (protectStallRisk ? "PROTECT STALL RISK" :
                        (protectEnergyLimit ? "ENERGY LIMIT" : "AA AOA/G LIMIT")));
                }
                else if (ThinAirAaLimitHoldActive && !thinAirReleaseRequested)
                {
                    if (thinAirAaLimitClearSince <= 0f) thinAirAaLimitClearSince = now;
                    ThinAirAaLimitRecoveryElapsedSeconds = now - thinAirAaLimitClearSince;
                    if (ThinAirAaLimitRecoveryElapsedSeconds >= ThinAirAaLimitExitDwellSeconds)
                    {
                        ThinAirAaLimitHoldActive = false;
                        ThinAirAaLimitHoldReason = "NONE";
                        ThinAirAaLimitHoldElapsedSeconds = 0f;
                        thinAirAaLimitHoldSince = 0f;
                        thinAirAaLimitClearSince = 0f;
                    }
                }
                else if (thinAirReleaseRequested)
                {
                    ThinAirAaLimitHoldActive = false;
                    ThinAirAaLimitHoldReason = "NONE";
                    ThinAirAaLimitHoldElapsedSeconds = 0f;
                    ThinAirAaLimitRecoveryElapsedSeconds = 0f;
                    thinAirAaLimitHoldSince = 0f;
                    thinAirAaLimitClearSince = 0f;
                }
            }

            float stallDegAuthority = protectAvailable
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinAirSustainStallMarginDeg,
                    ThinAirSustainStallMarginDeg + 12f, protect.StallMarginDegrees))
                : 0f;
            float stallNormAuthority = protectAvailable
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(ThinAirSustainStallMarginNormalized,
                    0.75f, protect.StallMarginNormalized))
                : 0f;
            ThinAirTurnStallAuthority = Mathf.Min(stallDegAuthority, stallNormAuthority);

            float angularAccelMagnitude = attitude != null && attitude.InstrumentAngularAccelerationValid
                ? Mathf.Max(Mathf.Abs(attitude.InstrumentRollAccelerationDegPerSec2),
                    Mathf.Abs(attitude.InstrumentPitchAccelerationDegPerSec2),
                    Mathf.Abs(attitude.InstrumentYawAccelerationDegPerSec2))
                : 35f;
            float accelerationStability = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(25f, 120f, angularAccelMagnitude));
            float angularRateMagnitude = attitude != null
                ? Mathf.Max(Mathf.Abs(attitude.InstrumentRollRateDegPerSec),
                    Mathf.Abs(attitude.InstrumentPitchRateDegPerSec),
                    Mathf.Abs(attitude.InstrumentYawRateDegPerSec))
                : 45f;
            float rateStability = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(22f, 55f, angularRateMagnitude));
            float bankTrackingStability = bank != null
                ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(8f, 32f, Mathf.Abs(bank.BankError)))
                : 0f;
            float gRateStability = 1f - Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(1.5f, 5.0f, Mathf.Abs(filteredThinAirGRate)));
            float stabilityAverage = accelerationStability * 0.30f +
                rateStability * 0.25f + bankTrackingStability * 0.25f + gRateStability * 0.20f;
            float weakestStability = Mathf.Min(accelerationStability,
                Mathf.Min(rateStability, Mathf.Min(bankTrackingStability, gRateStability)));
            ThinAirTurnStabilityScore = Mathf.Clamp01(stabilityAverage *
                Mathf.Lerp(0.35f, 1f, weakestStability));
            bool severeInstability = angularAccelMagnitude > 180f || angularRateMagnitude > 80f ||
                (bank != null && Mathf.Abs(bank.BankError) > 45f) || Mathf.Abs(filteredThinAirGRate) > 8f;

            float rawTrackingAuthority = ThinAirTurnCommandedG <= 1.20f
                ? 1f
                : Mathf.Clamp01((ThinAirTurnMeasuredG - 0.50f) /
                    Mathf.Max(0.50f, ThinAirTurnCommandedG - 0.50f));
            filteredThinAirGTrackingAuthority = Mathf.MoveTowards(filteredThinAirGTrackingAuthority,
                rawTrackingAuthority, (rawTrackingAuthority < filteredThinAirGTrackingAuthority ? 1.8f : 0.55f) * dt);
            ThinAirTurnTrackingAuthority = filteredThinAirGTrackingAuthority;

            float capabilityBankAbs = Mathf.Abs(attitude.InstrumentHorizonBankDeg);
            float capabilityRollRateAbs = Mathf.Abs(attitude.RollRateDegPerSec);
            ThinAirCapabilityTrackingErrorG = Mathf.Max(0f,
                ThinAirTurnCommandedG - ThinAirTurnMeasuredG);
            bool capabilitySampleValid = ThinAirTurnLatched && protectAvailable &&
                capabilityBankAbs >= 25f && capabilityRollRateAbs <= 12f &&
                !ThinAirStallRecoveryActive;
            bool capabilityDeficit = capabilitySampleValid &&
                (ThinAirCapabilityTrackingErrorG > ThinAirCapabilityTrackingToleranceG ||
                 ThinAirMarginGovernorAuthority < 0.80f || aaPitchNoseUpLimited);
            bool capabilityCanExplore = capabilitySampleValid && !capabilityDeficit &&
                ThinAirMarginGovernorAuthority >= 0.92f && ThinAirTurnStallAuthority >= 0.45f &&
                ThinAirTurnMeasuredG >= ThinAirTurnCommandedG - 0.15f;
            ThinAirCapabilityLimited = capabilityDeficit;
            if (capabilitySampleValid)
            {
                float observedSustainableG = Mathf.Clamp(ThinAirTurnMeasuredG,
                    ThinAirCapabilityMinimumG, ThinAirMaximumTargetG);
                if (capabilityDeficit && observedSustainableG < filteredThinAirSustainableG)
                    filteredThinAirSustainableG = Mathf.MoveTowards(filteredThinAirSustainableG,
                        observedSustainableG, ThinAirCapabilityFallPerSec * dt);
                else if (capabilityCanExplore && observedSustainableG >= filteredThinAirSustainableG - 0.05f)
                    filteredThinAirSustainableG = Mathf.MoveTowards(filteredThinAirSustainableG,
                        Mathf.Max(filteredThinAirSustainableG, observedSustainableG),
                        ThinAirCapabilityRisePerSec * dt);
                else if (observedSustainableG < filteredThinAirSustainableG - 0.15f)
                    filteredThinAirSustainableG = Mathf.MoveTowards(filteredThinAirSustainableG,
                        observedSustainableG, ThinAirCapabilityRelaxPerSec * dt);
            }
            ThinAirEstimatedSustainableG = Mathf.Clamp(filteredThinAirSustainableG,
                ThinAirCapabilityMinimumG, ThinAirMaximumTargetG);
            ThinAirCapabilityGCap = Mathf.Clamp(ThinAirEstimatedSustainableG +
                ThinAirCapabilityHeadroomG, ThinAirCapabilityMinimumG, ThinAirMaximumTargetG);
            float rawCapabilityBankCapDeg = Mathf.Acos(Mathf.Clamp(1f /
                Mathf.Max(1.001f, ThinAirCapabilityGCap), 0f, 1f)) * Mathf.Rad2Deg;
            ThinAirCapabilityBankCapDeg = Mathf.Clamp(rawCapabilityBankCapDeg,
                ThinAirCapabilityMinimumBankDeg, ThinAirMaximumBankDeg);
            filteredThinAirCapabilityBankCapDeg = Mathf.MoveTowards(
                filteredThinAirCapabilityBankCapDeg, ThinAirCapabilityBankCapDeg,
                (ThinAirCapabilityBankCapDeg < filteredThinAirCapabilityBankCapDeg
                    ? ThinAirCapabilityBankFallDegPerSec : ThinAirCapabilityBankRiseDegPerSec) * dt);
            ThinAirLowQBankCapDeg = Mathf.Lerp(ThinAirMaximumBankDeg,
                filteredThinAirCapabilityBankCapDeg, ThinAirLowQEnvelopeBlend);
            ThinAirLowQGCap = Mathf.Lerp(ThinAirMaximumTargetG,
                ThinAirCapabilityGCap, ThinAirLowQEnvelopeBlend);

            float headingDemand = Mathf.Lerp(0.25f, 1f, Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirMinimumHeadingErrorDeg, 90f, absError)));
            float speedAuthority = Mathf.Lerp(0.70f, 1f, Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirMinimumSurfaceSpeedMps, 1400f, ThinAirTurnObservedSurfaceSpeedMps)));
            float safeCapacity = severeInstability
                ? 0f : Mathf.Min(ThinAirTurnStabilityScore, ThinAirTurnStallAuthority);
            float desiredTargetG = ThinAirMinimumTargetG +
                (ThinAirMaximumTargetG - ThinAirMinimumTargetG) * safeCapacity * headingDemand * speedAuthority;
            desiredTargetG = Mathf.Min(desiredTargetG, ThinAirLowQGCap);
            float marginAuthorityShaped = ThinAirMarginGovernorAuthority * ThinAirMarginGovernorAuthority;
            float marginGovernorGCap = Mathf.Lerp(1.05f, ThinAirLowQGCap, marginAuthorityShaped);
            desiredTargetG = Mathf.Min(desiredTargetG, marginGovernorGCap);

            ThinAirAaLimitGCap = ThinAirMaximumTargetG;
            if (ThinAirAaLimitHoldActive)
            {
                float measuredHoldCap = Mathf.Clamp(ThinAirTurnMeasuredG + 0.20f,
                    1.15f, ThinAirAaLimitMaximumG);
                if (protectEnergyLimit) measuredHoldCap = Mathf.Min(measuredHoldCap,
                    ThinAirAaLimitEnergyMaximumG);
                if (ThinAirStallRecoveryActive) measuredHoldCap = Mathf.Min(measuredHoldCap, 1.20f);
                ThinAirAaLimitGCap = measuredHoldCap;
                desiredTargetG = Mathf.Min(desiredTargetG, ThinAirAaLimitGCap);
            }
            float rolloutBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirHeadingErrorReleaseDeg, ThinAirHeadingRolloutStartDeg, absError));
            if (thinAirReleaseRequested) rolloutBlend = 0f;
            ThinAirTurnTargetG = ThinAirTurnLatched
                ? Mathf.Lerp(ThinAirMinimumTargetG, Mathf.Clamp(desiredTargetG,
                    ThinAirMinimumTargetG, ThinAirMaximumTargetG), rolloutBlend)
                : ThinAirMinimumTargetG;
            float gSlew = ThinAirTurnTargetG < ThinAirTurnCommandedG
                ? ThinAirGCommandFallPerSec : ThinAirGCommandRisePerSec;
            ThinAirTurnCommandedG = Mathf.MoveTowards(ThinAirTurnCommandedG,
                ThinAirTurnTargetG, gSlew * dt);

            float assistTarget = ThinAirTurnLatched && !thinAirReleaseRequested
                ? (ThinAirStallRecoveryActive
                    ? Mathf.Max(0.35f, rolloutBlend * 0.45f)
                    : (ThinAirAaLimitHoldActive
                        ? Mathf.Max(0.25f, rolloutBlend * ThinAirAaLimitAssistBlend)
                        : Mathf.Max(0.12f, rolloutBlend)))
                : 0f;
            float assistSlew = assistTarget < ThinAirTurnAssistBlend
                ? ThinAirAssistFallRatePerSec : ThinAirAssistRiseRatePerSec;
            ThinAirTurnAssistBlend = Mathf.MoveTowards(ThinAirTurnAssistBlend,
                assistTarget, assistSlew * dt);
            ThinAirTurnAssistActive = ThinAirTurnLatched &&
                (ThinAirTurnAssistBlend > 0.02f || ThinAirTurnCommandedG > 1.05f);

            if (ThinAirTurnLatched)
            {
                if (thinAirReleaseRequested || absError < ThinAirHeadingRolloutStartDeg)
                {
                    ThinAirTurnPhase = "ROLLOUT";
                    thinAirRollInStableSince = 0f;
                }
                else if (ThinAirStallRecoveryActive)
                {
                    ThinAirTurnPhase = "STALL RECOVERY";
                }
                else if (ThinAirAaLimitHoldActive)
                {
                    ThinAirTurnPhase = "AA LIMIT HOLD";
                }
                else
                {
                    float actualBankAbs = Mathf.Abs(attitude.InstrumentHorizonBankDeg);
                    float actualRollRateAbs = Mathf.Abs(attitude.RollRateDegPerSec);
                    bool rollPlaneReady = actualBankAbs >= ThinAirPitchEnableBankDeg &&
                        actualRollRateAbs <= ThinAirRollInMaxRollRateDegPerSec;
                    if (rollPlaneReady)
                    {
                        if (thinAirRollInStableSince <= 0f) thinAirRollInStableSince = now;
                    }
                    else thinAirRollInStableSince = 0f;
                    bool rollPlaneStable = thinAirRollInStableSince > 0f &&
                        now - thinAirRollInStableSince >= ThinAirRollInStableDwellSeconds;
                    if (!rollPlaneStable) ThinAirTurnPhase = "ROLL-IN";
                    else if (ThinAirTurnCommandedG + 0.15f < ThinAirTurnTargetG)
                        ThinAirTurnPhase = "PITCH BUILD";
                    else ThinAirTurnPhase = "SUSTAIN";
                }
            }
            if (ThinAirTurnLatched && thinAirReleaseRequested &&
                ThinAirTurnAssistBlend <= 0.01f && ThinAirTurnCommandedG <= 1.05f)
            {
                ThinAirTurnLatched = false;
                thinAirLastReleaseTime = now;
                thinAirEntrySince = 0f;
                thinAirReleaseSince = 0f;
                thinAirLatchedSince = 0f;
                ThinAirTurnPhase = "COOLDOWN";
            }

            if (!ThinAirTurnAssistEnabled) ThinAirTurnQualificationStatus = "OFF";
            else if (!UseAutoMaxBankLimit) ThinAirTurnQualificationStatus = "WAIT BANK LIMIT AUTO";
            else if (ThinAirTurnLatched) ThinAirTurnQualificationStatus = ThinAirTurnPhase +
                " — " + ThinAirTurnCommandedG.ToString("F1") + "G / " + ThinAirTurnTargetG.ToString("F1") + "G" +
                (ThinAirAaLimitHoldActive ? " — " + ThinAirAaLimitHoldReason :
                    (ThinAirMarginGovernorActive ? " — MARGIN " + ThinAirMarginGovernorAuthority.ToString("F2") : ""));
            else if (now - thinAirLastReleaseTime < ThinAirRearmCooldownSeconds) ThinAirTurnQualificationStatus = "COOLDOWN";
            else if (ThinAirTurnObservedAltitudeMeters < ThinAirMinimumAltitudeMeters) ThinAirTurnQualificationStatus = "WAIT ALT >= 3000 m";
            else if (ThinAirTurnObservedSurfaceSpeedMps < ThinAirMinimumSurfaceSpeedMps) ThinAirTurnQualificationStatus = "WAIT SPEED >= 600 m/s";
            else if (!entryStallSafe) ThinAirTurnQualificationStatus = "INHIBIT — STALL MARGIN";
            else if (absError < ThinAirMinimumHeadingErrorDeg) ThinAirTurnQualificationStatus = "WAIT LARGE HDG ERROR";
            else ThinAirTurnQualificationStatus = "ENTRY ARMING";

            if (ThinAirTurnAssistActive != lastLoggedThinAirTurnAssistActive)
            {
                lastLoggedThinAirTurnAssistActive = ThinAirTurnAssistActive;
                AERISLogger.Info("[HDG][ADAPTIVE_HIGH_G_TURN] " +
                    (ThinAirTurnAssistActive ? "ACTIVE" : "RELEASED") +
                    " phase=" + ThinAirTurnPhase +
                    " reason=" + ThinAirTurnReleaseReason +
                    " alt=" + ThinAirTurnObservedAltitudeMeters.ToString("F0") + "m" +
                    " speed=" + ThinAirTurnObservedSurfaceSpeedMps.ToString("F1") + "m/s" +
                    " gCmd=" + ThinAirTurnCommandedG.ToString("F2") +
                    " gTarget=" + ThinAirTurnTargetG.ToString("F2") +
                    " gMeasured=" + ThinAirTurnMeasuredG.ToString("F2") +
                    " stability=" + ThinAirTurnStabilityScore.ToString("F2") +
                    " stallAuthority=" + ThinAirTurnStallAuthority.ToString("F2") +
                    " hdgError=" + absError.ToString("F1") + "deg" +
                    " response=" + ThinAirTurnResponseRatio.ToString("F2") + ".");
            }
            if (ThinAirAaLimitHoldActive != lastLoggedThinAirAaLimitHoldActive ||
                (ThinAirAaLimitHoldActive && ThinAirAaLimitHoldReason != lastLoggedThinAirAaLimitHoldReason))
            {
                lastLoggedThinAirAaLimitHoldActive = ThinAirAaLimitHoldActive;
                lastLoggedThinAirAaLimitHoldReason = ThinAirAaLimitHoldReason;
                AERISLogger.Info("[HDG][AA_LIMIT_HOLD] " +
                    (ThinAirAaLimitHoldActive ? "ACTIVE" : "CLEARED") +
                    " reason=" + ThinAirAaLimitHoldReason +
                    " aaReq=" + ThinAirAaPitchRequestedDegPerSec.ToString("F2") + "deg/s" +
                    " aaApplied=" + ThinAirAaPitchAppliedDegPerSec.ToString("F2") + "deg/s" +
                    " authority=" + ThinAirAaPitchAuthority.ToString("F2") +
                    " gCap=" + ThinAirAaLimitGCap.ToString("F2") +
                    " pitchCap=" + ThinAirAaLimitPitchCapDegPerSec.ToString("F2") + "deg/s.");
            }
            if (ThinAirMarginGovernorActive != lastLoggedThinAirMarginGovernorActive ||
                (ThinAirMarginGovernorActive &&
                    ThinAirMarginGovernorReason != lastLoggedThinAirMarginGovernorReason))
            {
                lastLoggedThinAirMarginGovernorActive = ThinAirMarginGovernorActive;
                lastLoggedThinAirMarginGovernorReason = ThinAirMarginGovernorReason;
                AERISLogger.Info("[HDG][MARGIN_GOVERNOR] " +
                    (ThinAirMarginGovernorActive ? "ACTIVE" : "CLEARED") +
                    " reason=" + ThinAirMarginGovernorReason +
                    " marginNow=" + ThinAirTurnObservedStallMarginDeg.ToString("F2") + "deg" +
                    " predicted=" + ThinAirPredictedStallMarginDeg.ToString("F2") + "deg" +
                    " rate=" + ThinAirStallMarginRateDegPerSec.ToString("F2") + "deg/s" +
                    " authority=" + ThinAirMarginGovernorAuthority.ToString("F2") +
                    " lowQ=" + ThinAirLowQEnvelopeBlend.ToString("F2") +
                    " sustainableG=" + ThinAirEstimatedSustainableG.ToString("F2") +
                    " gCap=" + ThinAirCapabilityGCap.ToString("F2") +
                    " bankCap=" + ThinAirLowQBankCapDeg.ToString("F1") + "deg.");
            }

            float gBankBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(1.5f, 7f, ThinAirTurnCommandedG));
            float errorBankBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(25f, 120f, absError));
            ThinAirTurnBankSpeedBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirMinimumSurfaceSpeedMps,
                    ThinAirFullBankSurfaceSpeedMps, ThinAirTurnObservedSurfaceSpeedMps));
            float weightedBankBlend = Mathf.Clamp01(errorBankBlend * 0.35f +
                gBankBlend * 0.25f + ThinAirTurnBankSpeedBlend * 0.40f);
            float highSpeedLargeErrorBlend = ThinAirTurnBankSpeedBlend * errorBankBlend;
            float adaptiveBankBlend = Mathf.Max(weightedBankBlend, highSpeedLargeErrorBlend);
            ThinAirTurnBankTargetDeg = Mathf.Lerp(ThinAirMinimumBankDeg,
                ThinAirMaximumBankDeg, adaptiveBankBlend);
            ThinAirTurnBankTargetDeg = Mathf.Min(ThinAirTurnBankTargetDeg, ThinAirLowQBankCapDeg);
            float marginBankAuthority = Mathf.SmoothStep(0f, 1f, ThinAirMarginGovernorAuthority);
            float marginBankCap = Mathf.Lerp(ThinAirRollInTargetBankDeg,
                ThinAirLowQBankCapDeg, marginBankAuthority);
            ThinAirTurnBankTargetDeg = Mathf.Min(ThinAirTurnBankTargetDeg, marginBankCap);
            if (ThinAirTurnLatched && ThinAirTurnPhase == "ROLL-IN")
                ThinAirTurnBankTargetDeg = Mathf.Clamp(ThinAirRollInTargetBankDeg,
                    0f, ThinAirLowQBankCapDeg);
            else if (ThinAirTurnLatched && ThinAirTurnPhase == "STALL RECOVERY")
                ThinAirTurnBankTargetDeg = Mathf.Min(ThinAirTurnBankTargetDeg, 48f);
            else if (ThinAirTurnLatched && ThinAirTurnPhase == "AA LIMIT HOLD")
            {
                float settledBankHold = Mathf.Clamp(Mathf.Abs(attitude.InstrumentHorizonBankDeg),
                    ThinAirRollInTargetBankDeg, Mathf.Min(60f, ThinAirLowQBankCapDeg));
                ThinAirTurnBankTargetDeg = Mathf.Min(ThinAirTurnBankTargetDeg, settledBankHold);
            }
            if (ThinAirTurnAssistActive)
                maxBank = Mathf.Max(maxBank, Mathf.Lerp(maxBank,
                    ThinAirTurnBankTargetDeg, ThinAirTurnAssistBlend));

            // Recover low-speed turn authority from measured capability, not from
            // speed or q alone. The original q schedule remains the hard fallback.
            // v0.11.7 replaces the former fixed 30-degree ceiling with a measured-G
            // ceiling up to 45 degrees. Every existing q/altitude/stall/margin gate
            // still has veto authority, so loss of margin withdraws it immediately.
            SafeLowSpeedBankSpeedBlend = protectAvailable ? Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SafeLowSpeedBankStallMarginStartNormalized,
                    SafeLowSpeedBankStallMarginFullNormalized,
                    protect.StallMarginNormalized)) : 0f;
            // Low-speed aircraft can be fully controllable below the legacy 4 kPa
            // BANK schedule boundary.  Use that boundary only as a scale, then let
            // PROTECT and measured turn response decide how far authority may grow.
            float safeBankQStart = Mathf.Max(0.50f, bank.DynamicPressureLowQKpa * 0.35f);
            float safeBankQFull = Mathf.Max(safeBankQStart + 0.25f,
                bank.DynamicPressureLowQKpa * 0.75f);
            SafeLowSpeedBankQBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(safeBankQStart, safeBankQFull, qKpa));
            float safePredictedMargin = protectAvailable
                ? Mathf.Min(protect.StallMarginDegrees, ThinAirPredictedStallMarginDeg) : 0f;
            float safeMarginDegBlend = protectAvailable ? Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SafeLowSpeedBankStallMarginStartDeg,
                    SafeLowSpeedBankStallMarginFullDeg, safePredictedMargin)) : 0f;
            float safeMarginNormBlend = protectAvailable ? Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SafeLowSpeedBankStallMarginStartNormalized,
                    SafeLowSpeedBankStallMarginFullNormalized, protect.StallMarginNormalized)) : 0f;
            SafeLowSpeedBankStallBlend = Mathf.Min(Mathf.Min(safeMarginDegBlend,
                safeMarginNormBlend), Mathf.Clamp01(ThinAirMarginGovernorAuthority));
            float radarAltitude = vessel.heightFromTerrain >= 0.0
                ? Mathf.Max(0f, (float)vessel.heightFromTerrain) : Mathf.Max(0f, (float)vessel.altitude);
            SafeLowSpeedBankAltitudeBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SafeLowSpeedBankRadarAltitudeStartM,
                    SafeLowSpeedBankRadarAltitudeFullM, radarAltitude));
            float capabilityBlend = Mathf.Min(SafeLowSpeedBankSpeedBlend,
                Mathf.Min(SafeLowSpeedBankQBlend,
                    Mathf.Min(SafeLowSpeedBankStallBlend, SafeLowSpeedBankAltitudeBlend)));
            bool safeEnvelope = UseAutoMaxBankLimit && protectAvailable &&
                !protectCaution && !protectStallRisk && !protectStallDetected && !protectEnergyLimit;
            if (!safeEnvelope) capabilityBlend = 0f;

            float observedBankAbs = Mathf.Abs(attitude.InstrumentHorizonBankDeg);
            float observedRollRateAbs = Mathf.Abs(attitude.RollRateDegPerSec);
            SafeLowSpeedBankCapabilitySampleActive = safeEnvelope &&
                qKpa >= safeBankQStart && observedBankAbs >= 18f && observedBankAbs <= 55f &&
                observedRollRateAbs <= 12f && Mathf.Abs(bank.BankError) <= 12f;
            if (SafeLowSpeedBankCapabilitySampleActive)
            {
                float measuredG = Mathf.Clamp(Mathf.Abs(attitude.GeeForce), 1f, 2.5f);
                SafeLowSpeedBankObservedG = Mathf.MoveTowards(SafeLowSpeedBankObservedG,
                    measuredG, (measuredG < SafeLowSpeedBankObservedG ? 2.0f : 0.40f) * dt);
                // A small headroom lets a stable 20-30 degree exploratory turn earn
                // additional authority progressively; it cannot jump directly to 45.
                float rawLearnedCap = Mathf.Acos(Mathf.Clamp(1f /
                    Mathf.Max(1.001f, SafeLowSpeedBankObservedG + 0.12f), 0f, 1f)) * Mathf.Rad2Deg;
                rawLearnedCap = Mathf.Clamp(rawLearnedCap,
                    SafeLowSpeedBankMaximumDeg, SafeLowSpeedBankAdaptiveMaximumDeg);
                safeLowSpeedLearnedBankCapDeg = Mathf.MoveTowards(
                    safeLowSpeedLearnedBankCapDeg, rawLearnedCap,
                    (rawLearnedCap < safeLowSpeedLearnedBankCapDeg ? 12f : 2f) * dt);
            }
            else if (!safeEnvelope)
            {
                // Safety loss immediately removes applied authority through
                // capabilityBlend and also erases optimistic learned headroom fast.
                SafeLowSpeedBankObservedG = Mathf.MoveTowards(
                    SafeLowSpeedBankObservedG, 1f, 2f * dt);
                safeLowSpeedLearnedBankCapDeg = Mathf.MoveTowards(
                    safeLowSpeedLearnedBankCapDeg, SafeLowSpeedBankMaximumDeg, 20f * dt);
            }
            SafeLowSpeedBankMeasuredMaximumDeg = Mathf.Clamp(
                Mathf.Min(SafeLowSpeedBankAdaptiveMaximumDeg,
                    Mathf.Min(safeLowSpeedLearnedBankCapDeg, ThinAirCapabilityBankCapDeg)),
                SafeLowSpeedBankMaximumDeg, SafeLowSpeedBankAdaptiveMaximumDeg);
            float safeCapabilityFloor = Mathf.Lerp(MaxBankLowQ,
                SafeLowSpeedBankMeasuredMaximumDeg, capabilityBlend);
            SafeLowSpeedBankCapabilityLimitDeg = Mathf.Max(maxBank, safeCapabilityFloor);
            float demandBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(SafeLowSpeedBankHeadingErrorStartDeg,
                    SafeLowSpeedBankHeadingErrorFullDeg, absError));
            SafeLowSpeedBankAuthorityBlend = capabilityBlend * demandBlend;
            float safeAuthorityFloor = Mathf.Lerp(MaxBankLowQ,
                SafeLowSpeedBankMeasuredMaximumDeg, SafeLowSpeedBankAuthorityBlend);
            SafeLowSpeedBankAuthorityLimitDeg = Mathf.Max(maxBank, safeAuthorityFloor);
            SafeLowSpeedBankAuthorityActive = safeEnvelope &&
                SafeLowSpeedBankAuthorityLimitDeg > maxBank + 0.05f;
            if (SafeLowSpeedBankAuthorityActive)
            {
                maxBank = SafeLowSpeedBankAuthorityLimitDeg;
                SafeLowSpeedBankAuthorityReason = "HDG SAFE AUTHORITY";
            }
            else if (!UseAutoMaxBankLimit) SafeLowSpeedBankAuthorityReason = "MANUAL BANK LIMIT";
            else if (!protectAvailable) SafeLowSpeedBankAuthorityReason = "PROTECT UNAVAILABLE";
            else if (protectCaution) SafeLowSpeedBankAuthorityReason = "PROTECT CAUTION";
            else if (protectStallDetected) SafeLowSpeedBankAuthorityReason = "STALL DETECTED";
            else if (protectStallRisk) SafeLowSpeedBankAuthorityReason = "STALL RISK";
            else if (protectEnergyLimit) SafeLowSpeedBankAuthorityReason = "ENERGY COLLAPSE";
            else if (SafeLowSpeedBankAltitudeBlend <= 0.01f) SafeLowSpeedBankAuthorityReason = "LOW ALTITUDE";
            else if (SafeLowSpeedBankStallBlend <= 0.01f) SafeLowSpeedBankAuthorityReason = "STALL MARGIN";
            else if (SafeLowSpeedBankQBlend <= 0.01f) SafeLowSpeedBankAuthorityReason = "Q BELOW GATE";
            else if (SafeLowSpeedBankSpeedBlend <= 0.01f) SafeLowSpeedBankAuthorityReason = "ENERGY MARGIN BELOW GATE";
            else SafeLowSpeedBankAuthorityReason = "LOW TURN DEMAND";
            if (SafeLowSpeedBankAuthorityActive != lastLoggedSafeLowSpeedBankAuthorityActive)
            {
                lastLoggedSafeLowSpeedBankAuthorityActive = SafeLowSpeedBankAuthorityActive;
                AERISLogger.Info("[HDG][SAFE_LOW_SPEED_BANK] " +
                    (SafeLowSpeedBankAuthorityActive ? "ACTIVE" : "RELEASED") +
                    " reason=" + SafeLowSpeedBankAuthorityReason +
                    " q=" + qKpa.ToString("F2") + "kPa" +
                    " speed=" + ThinAirTurnObservedSurfaceSpeedMps.ToString("F1") + "m/s" +
                    " stallMargin=" + safePredictedMargin.ToString("F1") + "deg" +
                    " measuredG=" + SafeLowSpeedBankObservedG.ToString("F2") +
                    " measuredMax=" + SafeLowSpeedBankMeasuredMaximumDeg.ToString("F1") + "deg" +
                    " capability=" + SafeLowSpeedBankCapabilityLimitDeg.ToString("F1") + "deg" +
                    " applied=" + SafeLowSpeedBankAuthorityLimitDeg.ToString("F1") + "deg.");
            }

            AutoMaxBankLimitDeg = maxBank;
            EffectiveMaxBankLimitDeg = UseAutoMaxBankLimit ? AutoMaxBankLimitDeg : ManualMaxBankLimitDeg;
            RawBankTarget = HeadingError * gain;
            float sign = Mathf.Sign(HeadingError);
            float saturationCorridor = EffectiveMaxBankLimitDeg / Mathf.Max(0.01f, gain);
            float lowQRolloutStart = Mathf.Max(TerminalYawEntryErrorDeg, saturationCorridor * RolloutHoldFraction);
            float scheduledRolloutStart = Mathf.Lerp(lowQRolloutStart, RolloutHoldMidQErrorDeg, qT);
            scheduledRolloutStart = Mathf.Lerp(scheduledRolloutStart, RolloutHoldHighQErrorDeg, highT);
            // Start earlier only when the measured turn rate predicts that keeping max bank
            // would carry the nose past the target. This is a director decision, not a roll write.
            float highBankLeadBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(60f,
                ThinAirMaximumBankDeg, Mathf.Max(Mathf.Abs(CommandedBankTarget),
                    Mathf.Abs(ThinAirTurnBankTargetDeg))));
            float adaptiveRolloutLeadSeconds = Mathf.Lerp(RolloutPredictedTurnLeadSeconds,
                ThinAirHighBankRolloutLeadSeconds, highBankLeadBlend);
            float projectedHeadingRate = Mathf.Max(Mathf.Abs(attitude.YawRateDegPerSec),
                Mathf.Abs(ThinAirTurnObservedHeadingRateDegPerSec));
            ThinAirTurnRolloutLeadDeg = projectedHeadingRate * adaptiveRolloutLeadSeconds;
            RolloutStartErrorDeg = Mathf.Max(scheduledRolloutStart, ThinAirTurnRolloutLeadDeg);
            float desired;
            RolloutHoldActive = absError >= RolloutStartErrorDeg;
            if (RolloutHoldActive)
            {
                desired = sign * EffectiveMaxBankLimitDeg;
            }
            else
            {
                // Smoothly roll out from the retained max-bank target to zero. SmoothStep
                // keeps both bank target and target-rate continuous at the boundary.
                float t = Mathf.Clamp01(absError / Mathf.Max(0.01f, RolloutStartErrorDeg));
                desired = sign * EffectiveMaxBankLimitDeg * Mathf.SmoothStep(0f, 1f, t);
            }

            // v0.4.90 terminal yaw: retain the former virtual-rudder calculation only as
            // a shadow diagnostic. Actual control is a direct desired yaw angular velocity
            // delivered to AA's existing YawAngularVelocityController before AA writes the
            // final FlightCtrlState. This is the yaw analogue of v0.4.88 BANK transport.
            bool terminalYawEligible = absError <= TerminalYawEntryErrorDeg &&
                                       Mathf.Abs(attitude.InstrumentHorizonBankDeg) <= TerminalYawMaxBankDeg &&
                                       Mathf.Abs(attitude.RollRateDegPerSec) <= TerminalYawMaxRollRateDegPerSec;
            if (terminalYawEligible)
            {
                float gainScale = absError <= TerminalYawPrecisionBandDeg ? 1.75f :
                                  (absError <= TerminalYawStrongBandDeg ? 1.45f : 1.15f);
                TerminalYawCaptureBand = absError <= TerminalYawPrecisionBandDeg ? "PRECISION" :
                                          (absError <= TerminalYawStrongBandDeg ? "STRONG" : "ENTRY");
                float authority = Mathf.Lerp(0.82f, 1f, qT) * Mathf.Lerp(1f, 0.97f, highT);

                // Legacy shadow: never sent to FlightCtrlState in v0.4.90.
                TerminalYawProportionalTerm = HeadingError * TerminalYawHeadingGain * gainScale;
                TerminalYawRateDampingTerm = -attitude.YawRateDegPerSec * TerminalYawRateDamping;
                float rawYawShadow = (TerminalYawProportionalTerm + TerminalYawRateDampingTerm) * authority;
                TerminalYawRawCommand = Mathf.Clamp(rawYawShadow, -TerminalYawMaxCommand, TerminalYawMaxCommand);
                TerminalYawCommand = Mathf.MoveTowards(TerminalYawCommand, TerminalYawRawCommand, TerminalYawSlewPerSec * authority * dt);

                // Actual native yaw-rate request in deg/s. The P term describes the
                // desired terminal heading motion; the D term begins removing that rate
                // before the heading target is crossed. AA owns control-surface output.
                TerminalYawRateProportionalTermDegPerSec = HeadingError * TerminalYawHeadingRateGainPerSec * gainScale;
                TerminalYawRateDampingTermDegPerSec = -attitude.YawRateDegPerSec * TerminalYawRateDampingGain;
                float rawYawRate = (TerminalYawRateProportionalTermDegPerSec + TerminalYawRateDampingTermDegPerSec) * authority;
                TerminalYawRateRawDegPerSec = Mathf.Clamp(rawYawRate, -TerminalYawNativeMaxRateDegPerSec, TerminalYawNativeMaxRateDegPerSec);
                TerminalYawRateCommandDegPerSec = Mathf.MoveTowards(TerminalYawRateCommandDegPerSec, TerminalYawRateRawDegPerSec,
                    TerminalYawNativeSlewDegPerSec2 * authority * dt);
                TerminalYawActive = true;

                // Rudder finishing is preferred, but some aircraft have weak yaw authority.
                // When a meaningful native yaw-rate request fails to create yaw-rate, retain a
                // small BANK target instead of waiting indefinitely. The assist fades in the precision band.
                bool yawWeak = Mathf.Abs(TerminalYawRateCommandDegPerSec) >= TerminalYawWeakResponseDemandDegPerSec &&
                               Mathf.Abs(attitude.YawRateDegPerSec) <= TerminalYawWeakResponseRateDegPerSec &&
                               absError >= TerminalRollAssistMinimumErrorDeg;
                if (yawWeak)
                {
                    float assistMax = absError <= TerminalYawPrecisionBandDeg
                        ? TerminalRollAssistPrecisionDeg
                        : TerminalRollAssistEntryDeg;
                    float assistT = Mathf.InverseLerp(TerminalRollAssistMinimumErrorDeg, TerminalYawEntryErrorDeg, absError);
                    float yawEffectiveness = Mathf.Clamp01(Mathf.Abs(attitude.YawRateDegPerSec) / Mathf.Max(0.01f, TerminalYawWeakResponseRateDegPerSec));
                    float yawFade = Mathf.Lerp(1f, TerminalRollAssistFadeWhenYawEffective, yawEffectiveness);
                    TerminalRollAssistRawDeg = sign * Mathf.Lerp(0.65f, assistMax, assistT) * Mathf.Lerp(0.75f, 1f, qT) * Mathf.Lerp(1f, 0.88f, highT) * yawFade;

                    float delta = TerminalRollAssistRawDeg - TerminalRollAssistFilteredDeg;
                    bool reversing = Mathf.Abs(TerminalRollAssistFilteredDeg) > 0.01f &&
                                     Mathf.Sign(TerminalRollAssistRawDeg) != Mathf.Sign(TerminalRollAssistFilteredDeg);
                    if (Mathf.Abs(delta) <= TerminalRollAssistDeadbandDeg)
                    {
                        TerminalRollAssistHoldActive = true;
                        TerminalRollAssistReversePending = false;
                        terminalRollAssistReverseSince = 0f;
                    }
                    else if (reversing)
                    {
                        if (terminalRollAssistReverseSince <= 0f) terminalRollAssistReverseSince = now;
                        float elapsed = now - terminalRollAssistReverseSince;
                        TerminalRollAssistReversePending = elapsed < TerminalRollAssistReverseDwellSeconds;
                        TerminalRollAssistHoldActive = TerminalRollAssistReversePending;
                        if (!TerminalRollAssistReversePending)
                        {
                            TerminalRollAssistFilteredDeg = TerminalRollAssistRawDeg;
                            terminalRollAssistReverseSince = 0f;
                        }
                    }
                    else
                    {
                        TerminalRollAssistFilteredDeg = TerminalRollAssistRawDeg;
                        TerminalRollAssistHoldActive = false;
                        TerminalRollAssistReversePending = false;
                        terminalRollAssistReverseSince = 0f;
                    }
                    TerminalRollAssistDeg = TerminalRollAssistFilteredDeg;
                    TerminalRollAssistActive = Mathf.Abs(TerminalRollAssistDeg) > 0.01f;
                    if (Mathf.Abs(desired) < Mathf.Abs(TerminalRollAssistDeg)) desired = TerminalRollAssistDeg;
                }
                else
                {
                    TerminalRollAssistFilteredDeg = Mathf.MoveTowards(TerminalRollAssistFilteredDeg, 0f, 3f * dt);
                    TerminalRollAssistDeg = TerminalRollAssistFilteredDeg;
                    TerminalRollAssistActive = Mathf.Abs(TerminalRollAssistDeg) > 0.01f;
                    TerminalRollAssistHoldActive = false;
                    TerminalRollAssistReversePending = false;
                    terminalRollAssistReverseSince = 0f;
                }
            }
            else
            {
                TerminalYawCommand = Mathf.MoveTowards(TerminalYawCommand, 0f, TerminalYawSlewPerSec * dt);
                TerminalYawRateCommandDegPerSec = Mathf.MoveTowards(TerminalYawRateCommandDegPerSec, 0f, TerminalYawNativeSlewDegPerSec2 * dt);
            }

            // Split yaw into two independent roles. Turn yaw fades as bank deepens;
            // attitude-stability yaw remains available to damp beta/yaw oscillation without
            // directly chasing heading. AA remains the sole final rudder/surface controller.
            float actualBankForYaw = Mathf.Abs(attitude.InstrumentHorizonBankDeg);
            ThinAirTurnYawBankFade = ComputeTurnYawBankFade(actualBankForYaw);
            bool coordinationEligible = Mathf.Abs(CommandedBankTarget) >= CoordinatedYawMinBankDeg &&
                                       absError > TerminalYawPrecisionBandDeg;
            float coordinationTargetLegacy = 0f;
            float coordinationTargetRate = 0f;
            if (coordinationEligible)
            {
                float highGAuthorityBlend = Mathf.Clamp01((ThinAirTurnCommandedG - 1f) /
                    Mathf.Max(0.01f, ThinAirMaximumTargetG - 1f));
                float coordAuthority = Mathf.Lerp(0.70f, 1f, qT) * Mathf.Lerp(1f, 0.90f, highT) *
                    Mathf.Lerp(1f, 1.35f, Mathf.Max(ThinAirTurnAssistBlend, highGAuthorityBlend));
                float desiredYawRate = CommandedBankTarget * CoordinatedYawRatePerBankDeg;
                float turnYawFade = ThinAirTurnAssistActive ? ThinAirTurnYawBankFade : 1f;

                CoordinatedYawFeedForward = CommandedBankTarget * CoordinatedYawBankGain * turnYawFade;
                CoordinatedYawRateCorrection = (desiredYawRate - attitude.YawRateDegPerSec) *
                    CoordinatedYawRateGain * turnYawFade;
                coordinationTargetLegacy = Mathf.Clamp((CoordinatedYawFeedForward + CoordinatedYawRateCorrection) * coordAuthority,
                                                        -CoordinatedYawMaxCommand, CoordinatedYawMaxCommand);

                float coordinatedYawLimit = Mathf.Lerp(CoordinatedYawNativeMaxRateDegPerSec,
                    ThinAirMaximumYawRateDegPerSec, Mathf.Max(ThinAirTurnAssistBlend, highGAuthorityBlend));
                coordinationTargetRate = Mathf.Clamp(desiredYawRate * coordAuthority * turnYawFade,
                                                      -coordinatedYawLimit,
                                                      coordinatedYawLimit);
            }
            ThinAirTurnYawRateTargetDegPerSec = coordinationTargetRate;
            CoordinatedYawCommand = Mathf.MoveTowards(CoordinatedYawCommand, coordinationTargetLegacy,
                                                       CoordinatedYawSlewPerSec * dt);
            CoordinatedYawRateTargetDegPerSec = coordinationTargetRate;
            CoordinatedYawRateCommandDegPerSec = Mathf.MoveTowards(CoordinatedYawRateCommandDegPerSec, coordinationTargetRate,
                CoordinatedYawNativeSlewDegPerSec2 * dt);

            float dynamicStabilityYawStartBank = Mathf.Lerp(ThinAirStabilityYawStartBankDeg,
                15f, ThinAirLowQEnvelopeBlend);
            float dynamicStabilityYawFullBank = Mathf.Lerp(ThinAirStabilityYawFullBankDeg,
                45f, ThinAirLowQEnvelopeBlend);
            float stabilityYawBlend = ThinAirTurnAssistActive
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(dynamicStabilityYawStartBank,
                    dynamicStabilityYawFullBank, actualBankForYaw))
                : 0f;
            float stabilityYawGainScale = Mathf.Lerp(1f, 1.35f, ThinAirLowQEnvelopeBlend);
            float sideslipDeg = protect != null ? protect.SideslipDegrees : 0f;
            AttitudeStabilityYawSideslipTermDegPerSec = Mathf.Clamp(
                -sideslipDeg * ThinAirStabilityYawSideslipGain * stabilityYawGainScale, -1.80f, 1.80f);
            AttitudeStabilityYawRateDampingTermDegPerSec = Mathf.Clamp(
                -attitude.YawRateDegPerSec * ThinAirStabilityYawRateDampingGain * stabilityYawGainScale, -1.80f, 1.80f);
            float yawAccelerationDegPerSec2 = attitude.InstrumentAngularAccelerationValid
                ? attitude.InstrumentYawAccelerationDegPerSec2 : 0f;
            AttitudeStabilityYawAccelerationDampingTermDegPerSec = Mathf.Clamp(
                -yawAccelerationDegPerSec2 * ThinAirStabilityYawAccelerationDampingGain * stabilityYawGainScale,
                -1.00f, 1.00f);
            float dynamicStabilityYawMaximumRate = Mathf.Lerp(ThinAirStabilityYawMaximumRateDegPerSec,
                3.25f, ThinAirLowQEnvelopeBlend);
            AttitudeStabilityYawRateTargetDegPerSec = Mathf.Clamp(
                (AttitudeStabilityYawSideslipTermDegPerSec +
                 AttitudeStabilityYawRateDampingTermDegPerSec +
                 AttitudeStabilityYawAccelerationDampingTermDegPerSec) * stabilityYawBlend,
                -dynamicStabilityYawMaximumRate,
                dynamicStabilityYawMaximumRate);
            AttitudeStabilityYawRateCommandDegPerSec = Mathf.MoveTowards(
                AttitudeStabilityYawRateCommandDegPerSec,
                AttitudeStabilityYawRateTargetDegPerSec,
                ThinAirStabilityYawSlewDegPerSec2 * dt);

            VirtualYawCommand = Mathf.Clamp(CoordinatedYawCommand + TerminalYawCommand, -1f, 1f);
            float combinedYawGBlend = Mathf.Clamp01((ThinAirTurnCommandedG - 1f) /
                Mathf.Max(0.01f, ThinAirMaximumTargetG - 1f));
            float combinedYawLimit = Mathf.Lerp(NativeYawCombinedMaxRateDegPerSec,
                ThinAirMaximumYawRateDegPerSec, Mathf.Max(ThinAirTurnAssistBlend, combinedYawGBlend));
            YawRateRequestDegPerSec = Mathf.Clamp(CoordinatedYawRateCommandDegPerSec +
                AttitudeStabilityYawRateCommandDegPerSec + TerminalYawRateCommandDegPerSec,
                -combinedYawLimit, combinedYawLimit);
            YawAssistMode = TerminalYawActive ? "TERMINAL" :
                (Mathf.Abs(AttitudeStabilityYawRateCommandDegPerSec) > 0.05f
                    ? (Mathf.Abs(CoordinatedYawRateCommandDegPerSec) > 0.05f ? "TURN+STABILITY" : "STABILITY")
                    : (Mathf.Abs(CoordinatedYawRateCommandDegPerSec) > 0.05f ? "TURN" : "OFF"));
            NativeYawTransportEligible = true;

            float bankForPitch = Mathf.Abs(attitude.InstrumentHorizonBankDeg);
            float bankPitchBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirPitchEnableBankDeg, ThinAirPitchFullBankDeg, bankForPitch));
            bool pitchPhaseEnabled = ThinAirTurnPhase == "PITCH BUILD" ||
                ThinAirTurnPhase == "SUSTAIN" ||
                ThinAirTurnPhase == "AA LIMIT HOLD" || ThinAirTurnPhase == "STALL RECOVERY";
            float pitchSafeAuthority = Mathf.Clamp01(Mathf.Min(ThinAirTurnStabilityScore,
                Mathf.Min(ThinAirTurnStallAuthority, ThinAirMarginGovernorAuthority)));
            float pitchLeadBlend = ThinAirPitchTargetGLeadFraction * pitchSafeAuthority;
            float pitchPlanningG = Mathf.Lerp(ThinAirTurnCommandedG, ThinAirTurnTargetG, pitchLeadBlend);
            float excessG = Mathf.Max(0f, pitchPlanningG - 1f);
            ThinAirTurnPitchKinematicRateDegPerSec = excessG * ThinAirGravityMps2 /
                Mathf.Max(150f, ThinAirTurnObservedSurfaceSpeedMps) * Mathf.Rad2Deg;
            ThinAirTurnPitchFeedbackRateDegPerSec = Mathf.Clamp(
                (pitchPlanningG - ThinAirTurnMeasuredG) * ThinAirPitchGFeedbackGain, -3f, 6f);
            float pitchSpeedBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirMinimumSurfaceSpeedMps,
                    ThinAirFullPitchSurfaceSpeedMps, ThinAirTurnObservedSurfaceSpeedMps));
            float pitchHeadingBlend = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(ThinAirMinimumHeadingErrorDeg, 120f, absError));
            float lowQPitchFloorScale = Mathf.Lerp(1f, 0.35f, ThinAirLowQEnvelopeBlend);
            ThinAirTurnPitchFloorRateDegPerSec = (ThinAirAaLimitHoldActive ||
                ThinAirMarginGovernorAuthority < 0.995f || ThinAirStallRecoveryActive) ? 0f :
                Mathf.Lerp(ThinAirMinimumPitchAssistDegPerSec,
                    ThinAirMaximumPitchFloorDegPerSec, pitchSpeedBlend * pitchHeadingBlend *
                        pitchSafeAuthority) * lowQPitchFloorScale;
            float plannedPitchRateDegPerSec;
            ThinAirAaLimitPitchCapDegPerSec = ThinAirLowQPitchCapDegPerSec;
            if (ThinAirStallRecoveryActive)
            {
                ThinAirAaLimitPitchCapDegPerSec = 0f;
                plannedPitchRateDegPerSec = 0f;
            }
            else if (ThinAirAaLimitHoldActive)
            {
                float unconstrainedHoldPitch = Mathf.Max(0f,
                    ThinAirTurnPitchKinematicRateDegPerSec + ThinAirTurnPitchFeedbackRateDegPerSec);
                float observedAppliedCap = StandardFlyByWire.LastPitchRateModerationActive
                    ? Mathf.Max(0f, ThinAirAaPitchAppliedDegPerSec * 0.75f)
                    : ThinAirAaLimitDefaultPitchCapDegPerSec;
                ThinAirAaLimitPitchCapDegPerSec = Mathf.Min(ThinAirLowQPitchCapDegPerSec,
                    Mathf.Clamp(observedAppliedCap, 0f, 3.0f));
                plannedPitchRateDegPerSec = Mathf.Min(
                    unconstrainedHoldPitch * Mathf.Max(0.10f, ThinAirAaPitchAuthority),
                    ThinAirAaLimitPitchCapDegPerSec);
            }
            else
            {
                float continuousMarginPitchCap = Mathf.Lerp(0.20f,
                    ThinAirLowQPitchCapDegPerSec, marginAuthorityShaped);
                plannedPitchRateDegPerSec = Mathf.Min(continuousMarginPitchCap,
                    Mathf.Max(ThinAirTurnPitchFloorRateDegPerSec,
                        ThinAirTurnPitchKinematicRateDegPerSec + ThinAirTurnPitchFeedbackRateDegPerSec));
            }
            ThinAirTurnPitchAssistRateDegPerSec = ThinAirTurnAssistActive && pitchPhaseEnabled
                ? Mathf.Clamp(plannedPitchRateDegPerSec * bankPitchBlend * ThinAirTurnAssistBlend,
                    -3f, ThinAirMaximumPitchAssistDegPerSec)
                : 0f;

            float slew = Mathf.Lerp(HeadingBankSlewLowQDegPerSec, HeadingBankSlewMidQDegPerSec, qT) * Mathf.Lerp(1f, 0.88f, highT);
            if (ThinAirTurnAssistActive && ThinAirLowQEnvelopeBlend > 0.01f &&
                ThinAirTurnPhase != "ROLLOUT")
            {
                float lowQTurnSlew = ThinAirTurnPhase == "ROLL-IN"
                    ? ThinAirLowQRollInSlewDegPerSec : ThinAirLowQBankTargetSlewDegPerSec;
                if (ThinAirStallRecoveryActive) lowQTurnSlew = Mathf.Max(lowQTurnSlew, 4.0f);
                slew = Mathf.Lerp(slew, Mathf.Min(slew, lowQTurnSlew), ThinAirLowQEnvelopeBlend);
            }
            ThinAirBankTargetSlewLimitDegPerSec = slew;
            // v0.4.76 terminal quiet handoff: only once the heading residual is genuinely tiny and terminal yaw is in charge,
            // stop re-projecting sub-degree heading noise back into BANK.  Fade the
            // already-small bank command toward level instead; residual heading is yaw's job.
            bool terminalQuietCandidate = TerminalYawActive &&
                                          Mathf.Abs(HeadingError) <= 0.30f &&
                                          Mathf.Abs(CommandedBankTarget) <= 1.20f &&
                                          Mathf.Abs(attitude.InstrumentHorizonBankDeg) <= 6.0f;
            if (terminalQuietCandidate)
            {
                float terminalFadeRate = Mathf.Lerp(2.2f, 3.4f, qT) * Mathf.Lerp(1f, 0.82f, highT);
                CommandedBankTarget = Mathf.MoveTowards(CommandedBankTarget, 0f, terminalFadeRate * dt);
            }
            else
            {
                CommandedBankTarget = Mathf.MoveTowards(CommandedBankTarget, desired, slew * dt);
            }
            TerminalBankQuietZoneActive = terminalQuietCandidate;
            bank.SetHdgTerminalQuietMode(TerminalBankQuietZoneActive);
            bank.SetTargetFromDirector(CommandedBankTarget);
            if (!bank.Armed) bank.SetArmed(true, vessel);
            ControlState = TerminalYawActive ? (TerminalRollAssistActive ? "ActiveTerminalCoord" : "ActiveTerminalYaw") :
                (ThinAirTurnAssistActive ? "ActiveAdaptiveHighGTurn" : "Active");
        }

        float ComputeTurnYawBankFade(float bankAbsDeg)
        {
            bankAbsDeg = Mathf.Abs(bankAbsDeg);
            float normalFade;
            if (bankAbsDeg <= ThinAirTurnYawFullBelowBankDeg) normalFade = 1f;
            else if (bankAbsDeg <= 45f)
                normalFade = Mathf.Lerp(1f, 0.70f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(ThinAirTurnYawFullBelowBankDeg, 45f, bankAbsDeg)));
            else if (bankAbsDeg <= 60f)
                normalFade = Mathf.Lerp(0.70f, 0.35f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(45f, 60f, bankAbsDeg)));
            else if (bankAbsDeg <= 70f)
                normalFade = Mathf.Lerp(0.35f, 0.10f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(60f, 70f, bankAbsDeg)));
            else if (bankAbsDeg < ThinAirTurnYawZeroAboveBankDeg)
                normalFade = Mathf.Lerp(0.10f, 0f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(70f, ThinAirTurnYawZeroAboveBankDeg, bankAbsDeg)));
            else normalFade = 0f;

            float lowQFade;
            if (bankAbsDeg <= ThinAirLowQTurnYawFullBelowBankDeg) lowQFade = 1f;
            else if (bankAbsDeg <= 40f)
                lowQFade = Mathf.Lerp(1f, 0.45f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(ThinAirLowQTurnYawFullBelowBankDeg, 40f, bankAbsDeg)));
            else if (bankAbsDeg <= 50f)
                lowQFade = Mathf.Lerp(0.45f, 0.10f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(40f, 50f, bankAbsDeg)));
            else if (bankAbsDeg < ThinAirLowQTurnYawZeroAboveBankDeg)
                lowQFade = Mathf.Lerp(0.10f, 0f, Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(50f, ThinAirLowQTurnYawZeroAboveBankDeg, bankAbsDeg)));
            else lowQFade = 0f;

            return Mathf.Lerp(normalFade, lowQFade, ThinAirLowQEnvelopeBlend);
        }

        // Called from the vessel autopilot callback, before AA StandardFlyByWire.
        // It is deliberately the only place where AERIS writes yaw ownership state;
        // AA still computes and writes the final yaw control-surface output.
        internal void ApplyAaNativeYawRateDemand(FlightCtrlState state, Vessel vessel, bool aerisMaster, bool standardFbwActive)
        {
            if (state == null || vessel == null || !Armed || !aerisMaster || !standardFbwActive ||
                vessel.LandedOrSplashed || vessel.packed || !NativeYawTransportEligible)
            {
                if (state != null) YawInputAfterNeutralization = state.yaw;
                ClearAaNativeYawRateOverride();
                return;
            }

            ControlUtils.neutralize_user_input(state, ControlUtils.YAW);
            YawInputAfterNeutralization = state.yaw;
            AaNativeYawRateDemandDegPerSec = YawRateRequestDegPerSec;
            AaNativeYawRateDemandRadPerSec = YawRateRequestDegPerSec * Mathf.Deg2Rad;
            StandardFlyByWire.ExternalYawDemand = AaNativeYawRateDemandRadPerSec;
            StandardFlyByWire.ExternalYawOverride = true;
            AaNativeYawRateOverrideActive = true;
        }

        internal void ClearAaNativeYawRateOverride()
        {
            StandardFlyByWire.ExternalYawOverride = false;
            StandardFlyByWire.ExternalYawDemand = 0f;
            AaNativeYawRateOverrideActive = false;
            AaNativeYawRateDemandDegPerSec = 0f;
            AaNativeYawRateDemandRadPerSec = 0f;
        }

        void ResetNativeYawRateState(bool resetSmoothedCommands)
        {
            NativeYawTransportEligible = false;
            YawRateRequestDegPerSec = 0f;
            YawRateActualDegPerSec = 0f;
            YawInputAfterNeutralization = 0f;
            TerminalYawRateRawDegPerSec = 0f;
            TerminalYawRateProportionalTermDegPerSec = 0f;
            TerminalYawRateDampingTermDegPerSec = 0f;
            CoordinatedYawRateTargetDegPerSec = 0f;
            ThinAirTurnYawRateTargetDegPerSec = 0f;
            ThinAirTurnYawBankFade = 1f;
            AttitudeStabilityYawRateTargetDegPerSec = 0f;
            AttitudeStabilityYawSideslipTermDegPerSec = 0f;
            AttitudeStabilityYawRateDampingTermDegPerSec = 0f;
            AttitudeStabilityYawAccelerationDampingTermDegPerSec = 0f;
            YawAssistMode = "OFF";
            if (resetSmoothedCommands)
            {
                TerminalYawRateCommandDegPerSec = 0f;
                CoordinatedYawRateCommandDegPerSec = 0f;
                AttitudeStabilityYawRateCommandDegPerSec = 0f;
            }
        }

        void ResetSafeLowSpeedBankAuthority(string reason)
        {
            if (lastLoggedSafeLowSpeedBankAuthorityActive)
                AERISLogger.Info("[HDG][SAFE_LOW_SPEED_BANK] RELEASED reason=" + reason + ".");
            lastLoggedSafeLowSpeedBankAuthorityActive = false;
            SafeLowSpeedBankAuthorityActive = false;
            SafeLowSpeedBankCapabilityLimitDeg = 0f;
            SafeLowSpeedBankAuthorityLimitDeg = 0f;
            SafeLowSpeedBankMeasuredMaximumDeg = SafeLowSpeedBankMaximumDeg;
            SafeLowSpeedBankCapabilitySampleActive = false;
            SafeLowSpeedBankObservedG = 1f;
            safeLowSpeedLearnedBankCapDeg = SafeLowSpeedBankMaximumDeg;
            SafeLowSpeedBankAuthorityBlend = 0f;
            SafeLowSpeedBankSpeedBlend = 0f;
            SafeLowSpeedBankQBlend = 0f;
            SafeLowSpeedBankStallBlend = 0f;
            SafeLowSpeedBankAltitudeBlend = 0f;
            SafeLowSpeedBankAuthorityReason = string.IsNullOrEmpty(reason) ? "INACTIVE" : reason;
        }

        void ResetThinAirTurnAssist(bool resetFilter)
        {
            if (lastLoggedThinAirTurnAssistActive)
                AERISLogger.Info("[HDG][ADAPTIVE_HIGH_G_TURN] RELEASED — director/envelope reset.");
            lastLoggedThinAirTurnAssistActive = false;
            lastLoggedThinAirAaLimitHoldActive = false;
            lastLoggedThinAirAaLimitHoldReason = "NONE";
            lastLoggedThinAirMarginGovernorActive = false;
            lastLoggedThinAirMarginGovernorReason = "NONE";
            ThinAirTurnAssistActive = false;
            ThinAirBlend = 0f;
            ThinAirTurnAssistBlend = 0f;
            ThinAirTurnBankTargetDeg = 0f;
            ThinAirTurnPitchAssistRateDegPerSec = 0f;
            ThinAirTurnWeakResponseElapsedSeconds = 0f;
            ThinAirTurnLatched = false;
            ThinAirTurnPhase = ThinAirTurnAssistEnabled ? "STANDBY" : "OFF";
            ThinAirTurnTargetG = 1f;
            ThinAirTurnCommandedG = 1f;
            ThinAirTurnMeasuredG = 1f;
            ThinAirTurnStabilityScore = 0f;
            ThinAirTurnStallAuthority = 0f;
            ThinAirTurnTrackingAuthority = 1f;
            ThinAirTurnBankSpeedBlend = 0f;
            ThinAirTurnPitchKinematicRateDegPerSec = 0f;
            ThinAirTurnPitchFloorRateDegPerSec = 0f;
            ThinAirTurnPitchFeedbackRateDegPerSec = 0f;
            ThinAirTurnRolloutLeadDeg = 0f;
            ThinAirTurnEntryElapsedSeconds = 0f;
            ThinAirTurnLatchedElapsedSeconds = 0f;
            ThinAirTurnReleaseElapsedSeconds = 0f;
            ThinAirTurnReleaseReason = "NONE";
            ThinAirAaLimitHoldActive = false;
            ThinAirAaLimitHoldReason = "NONE";
            ThinAirAaLimitHoldElapsedSeconds = 0f;
            ThinAirAaLimitRecoveryElapsedSeconds = 0f;
            ThinAirCriticalConditionElapsedSeconds = 0f;
            ThinAirAaPitchRequestedDegPerSec = 0f;
            ThinAirAaPitchAppliedDegPerSec = 0f;
            ThinAirAaPitchModerationDeltaDegPerSec = 0f;
            ThinAirAaPitchAuthority = 1f;
            ThinAirAaLimitGCap = ThinAirMaximumTargetG;
            ThinAirAaLimitPitchCapDegPerSec = ThinAirMaximumPitchAssistDegPerSec;
            ThinAirMarginGovernorActive = false;
            ThinAirMarginGovernorReason = "NONE";
            ThinAirStallMarginRateDegPerSec = 0f;
            ThinAirPredictedStallMarginDeg = 0f;
            ThinAirMarginGovernorAuthority = 1f;
            ThinAirMarginRecoveryElapsedSeconds = 0f;
            ThinAirLowQEnvelopeBlend = 0f;
            ThinAirLowQBankCapDeg = ThinAirMaximumBankDeg;
            ThinAirLowQGCap = ThinAirMaximumTargetG;
            ThinAirLowQPitchCapDegPerSec = ThinAirMaximumPitchAssistDegPerSec;
            ThinAirEstimatedSustainableG = ThinAirCapabilitySeedSustainableG;
            ThinAirCapabilityGCap = ThinAirCapabilitySeedSustainableG + ThinAirCapabilityHeadroomG;
            ThinAirCapabilityTrackingErrorG = 0f;
            ThinAirCapabilityLimited = false;
            ThinAirCapabilityBankCapDeg = 50f;
            ThinAirBankTargetSlewLimitDegPerSec = 0f;
            ThinAirStallRecoveryActive = false;
            ThinAirTurnYawBankFade = 1f;
            ThinAirTurnYawRateTargetDegPerSec = 0f;
            AttitudeStabilityYawRateTargetDegPerSec = 0f;
            AttitudeStabilityYawRateCommandDegPerSec = 0f;
            AttitudeStabilityYawSideslipTermDegPerSec = 0f;
            AttitudeStabilityYawRateDampingTermDegPerSec = 0f;
            AttitudeStabilityYawAccelerationDampingTermDegPerSec = 0f;
            YawAssistMode = "OFF";
            ThinAirTurnAltitudeQualified = false;
            ThinAirTurnSpeedQualified = false;
            ThinAirTurnStallMarginQualified = false;
            ThinAirTurnHeadingErrorQualified = false;
            ThinAirTurnQualificationStatus = ThinAirTurnAssistEnabled ? "STANDBY" : "OFF";
            ThinAirTurnObservedAltitudeMeters = 0f;
            ThinAirTurnObservedSurfaceSpeedMps = 0f;
            ThinAirTurnObservedStallMarginDeg = 0f;
            ThinAirTurnObservedHeadingRateDegPerSec = 0f;
            thinAirEntrySince = 0f;
            thinAirReleaseSince = 0f;
            thinAirLatchedSince = 0f;
            thinAirRollInStableSince = 0f;
            thinAirAaLimitHoldSince = 0f;
            thinAirAaLimitClearSince = 0f;
            thinAirCriticalConditionSince = 0f;
            thinAirMarginGovernorClearSince = 0f;
            thinAirReleaseRequested = false;
            filteredThinAirGTrackingAuthority = 1f;
            filteredThinAirMarginGovernorAuthority = 1f;
            filteredThinAirSustainableG = ThinAirCapabilitySeedSustainableG;
            filteredThinAirCapabilityBankCapDeg = 50f;
            filteredThinAirStallMarginRateDegPerSec = 0f;
            previousThinAirStallMarginDeg = 0f;
            hasPreviousThinAirStallMargin = false;
            filteredThinAirAaPitchAuthority = 1f;
            filteredThinAirGRate = 0f;
            previousThinAirMeasuredG = 1f;
            hasLastThinAirHeading = false;
            lastThinAirHeadingDeg = 0f;
            if (resetFilter)
            {
                filteredThinAirTurnResponseRatio = 1f;
                ThinAirTurnResponseRatio = 1f;
            }
        }

        internal void Release(string reason)
        {
            if (Armed) AERISLogger.Info("[HDG] released reason=" + reason);
            ClearAaNativeYawRateOverride();
            ResetNativeYawRateState(true);
            ResetSafeLowSpeedBankAuthority(reason);
            ResetThinAirTurnAssist(true);
            Armed = false; ControlState = "Inactive"; CommandedBankTarget = 0f; TerminalYawActive = false; TerminalYawCommand = 0f; TerminalYawRawCommand = 0f; CoordinatedYawCommand = 0f; CoordinatedYawFeedForward = 0f; CoordinatedYawRateCorrection = 0f; VirtualYawCommand = 0f; TerminalYawCaptureBand = "OFF"; TerminalYawProportionalTerm = 0f; TerminalYawRateDampingTerm = 0f; RolloutHoldActive = false; TerminalRollAssistDeg = 0f; TerminalRollAssistActive = false; TerminalRollAssistHoldActive = false; TerminalRollAssistReversePending = false; TerminalRollAssistFilteredDeg = 0f; TerminalRollAssistRawDeg = 0f; terminalRollAssistReverseSince = 0f; lastUpdateTime = 0f;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
