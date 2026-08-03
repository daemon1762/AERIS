using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using AERISFlightControl.Logging;
using AERISFlightControl.UI;
using AERISFlightControl.FlightState;
using AtmosphereAutopilot;

namespace AERISFlightControl.Autopilot
{
    // BANK is an outer-loop attitude director. It does not alter AA controller math or
    // write a post-FBW FlightCtrlState.  It publishes a native AA roll-rate request through
    // the existing StandardFlyByWire external-demand entry point, while neutralizing only
    // the pilot roll channel that BANK owns before AA reads it.
    internal sealed class AERISBankDirector
    {
        internal bool Armed { get; private set; }
        internal float TargetBank { get; private set; }
        internal string TargetBankText = "0";
        internal float CurrentBank { get; private set; }
        internal float BankError { get; private set; }
        internal float RollRateRequest { get; private set; } // desired signed bank-rate, deg/s
        internal float ActualRollRate { get; private set; }   // measured signed bank-rate, deg/s
        // The command actually handed to AA's native RollAngularVelocityController.
        // AA angular-rate units are rad/s; AERIS planner units remain deg/s.
        internal float FbwRollDemand { get; private set; }
        internal float AaNativeRollRateDemandRadPerSec { get; private set; }
        internal float AaNativeRollRateDemandDegPerSec { get; private set; }
        internal bool AaNativeRollRateOverrideActive { get; private set; }
        // Retained only as a v0.4.87 shadow diagnostic. It does not command the aircraft
        // in v0.4.88; BANK transport is the AA native roll-rate override above.
        internal float VirtualPilotRoll { get; private set; }
        internal float RawPilotRoll { get; private set; }
        // Value left in state.roll after BANK neutralizes the pilot channel before AA.
        // This is not an AERIS actuator command and is normally the roll trim value.
        internal float InjectedRoll { get; private set; }
        // Flight State Observer: capture pilot baseline and command-chain values without changing control.
        internal float ObserverPilotPitch { get; private set; }
        internal float ObserverPilotRoll { get; private set; }
        internal float ObserverPilotYaw { get; private set; }
        internal float ObserverPilotThrottle { get; private set; }
        internal float ObserverStatePitchBefore { get; private set; }
        internal float ObserverStateRollBefore { get; private set; }
        internal float ObserverStateYawBefore { get; private set; }
        internal float ObserverStateThrottleBefore { get; private set; }
        internal float ObserverStatePitchAfterAeris { get; private set; }
        internal float ObserverStateRollAfterAeris { get; private set; }
        internal float ObserverStateYawAfterAeris { get; private set; }
        internal float ObserverStateThrottleAfterAeris { get; private set; }
        internal bool ObserverManualRollActive { get; private set; }
        internal bool PilotRollBlocked { get { return Armed && ControlState == "Active"; } }
        internal string ControlState { get; private set; } = "Inactive";
        internal string CapturePhase { get; private set; } = "Idle";

        // v0.4.29 smoothness diagnostics. Observation only: none of these values feed back
        // into the control law. They are sampled at the virtual-pilot injection cadence.
        internal float DiagnosticControlDt { get; private set; }
        internal float DiagnosticTargetStick { get; private set; }
        internal float DiagnosticRateError { get; private set; }
        internal float DiagnosticVirtualRollDelta { get; private set; }
        internal float DiagnosticVirtualRollSlewPerSec { get; private set; }
        internal int DiagnosticCommandSign { get; private set; }
        internal int DiagnosticCommandSignFlips1s { get; private set; }
        internal int DiagnosticRateSignFlips1s { get; private set; }
        internal int DiagnosticErrorSignFlips1s { get; private set; }
        internal float DiagnosticStepScore { get; private set; }
        internal float DiagnosticOscillationScore { get; private set; }
        internal bool TerminalLatched { get { return terminalLatched; } }
        internal float DiagnosticSettleQuietElapsed { get; private set; }
        // v0.4.31 terminal chatter suppression telemetry. These fields describe only
        // the terminal command conditioner; they make its intervention visible in FDR.
        internal bool TerminalChatterSuppressed { get; private set; }
        internal float TerminalSlewScale { get; private set; }
        internal float TerminalCommandLockRemaining { get; private set; }
        // v0.4.31 BRAKE/SETTLE quieting telemetry. These values describe the
        // transition conditioner; CAPTURE authority is intentionally untouched.
        internal bool TransitionQuietingActive { get; private set; }
        internal float TransitionSlewScale { get; private set; }
        internal float TransitionCommandHoldRemaining { get; private set; }
        // v0.4.34: BRAKE/SETTLE command sample-and-hold. This slows command updates,
        // not stick motion, so AA still receives a smooth virtual-pilot input.
        internal bool TransitionUpdateGated { get; private set; }
        internal float TransitionUpdateInterval { get; private set; }
        internal float TransitionHeldTarget { get; private set; }
        internal int TransitionCommandUpdates1s { get; private set; }
        // v0.4.34: suppress insignificant BRAKE/SETTLE command changes even when
        // the cadence window permits an update. This is command deadband, not an
        // attitude deadband: large errors and decisive deceleration are preserved.
        internal bool TransitionDeltaSuppressed { get; private set; }
        internal float TransitionCommandDelta { get; private set; }
        internal float TransitionCommandDeadband { get; private set; }
        // v0.4.34 transition command shaper diagnostics. The outer loop may change
        // its requested roll rate quickly; BRAKE/SETTLE receive a rate-shaped version
        // so they do not retransmit every tiny estimate change to the control surfaces.
        internal float TransitionRawRateRequest { get; private set; }
        internal float TransitionShapedRateRequest { get; private set; }
        internal float TransitionRateAccelLimit { get; private set; }
        internal bool TransitionRateShaperActive { get; private set; }
        // v0.4.42 AERIS-only Bank Motion Planner telemetry. This shapes the requested
        // roll-rate trajectory before AA sees the virtual pilot input; AA output remains untouched.
        internal bool MotionPlannerActive { get; private set; }
        internal float MotionPlannerPlannedRate { get; private set; }
        internal float MotionPlannerPlannedAccel { get; private set; }
        internal float MotionPlannerAccelLimit { get; private set; }
        internal float MotionPlannerJerkLimit { get; private set; }
        // v0.4.37 terminal zero-capture diagnostics.
        internal bool TransitionZeroCaptureActive { get; private set; }
        internal float TransitionZeroCaptureRate { get; private set; }
        // v0.4.38 SETTLE rate-feedback deadband diagnostics. Small, alternating
        // measured roll rates are treated as natural decay rather than a new control demand.
        internal bool TransitionRateFeedbackDeadbandActive { get; private set; }
        internal float TransitionRateFeedbackDeadbandDegPerSec { get; private set; }
        internal float EffectiveRollRateForControl { get; private set; }
        // v0.4.45 three-region dynamic-pressure schedule. This is AERIS Director-side only;
        // AA remains the unchanged lower-level FBW / stability layer.
        internal float DynamicPressureKpa { get; private set; }
        internal float DynamicPressureSchedule { get; private set; }
        // v0.4.45: high-q blend is independent from the low-to-mid blend.
        // Both are continuous 0..1 schedules; no discrete control-mode switch is used.
        internal float DynamicPressureHighQSchedule { get; private set; }
        internal string DynamicPressureMode { get; private set; } = "MID_Q";
        internal float DynamicPressureRateScale { get; private set; }
        internal float DynamicPressureStickScale { get; private set; }
        internal float LimitedRollRateRequest { get; private set; }
        // v0.4.48 terminal phase re-entry diagnostics. SETTLE must not jump back to
        // BRAKE for one-frame micro-errors; only a persistent or clearly large departure
        // is allowed to re-arm active braking.
        internal bool SettleBrakeReentryPending { get; private set; }
        internal float SettleBrakeReentryElapsed { get; private set; }
        internal bool SettleBrakeReentryForced { get; private set; }
        // v0.4.53 HDG→BANK target conditioner diagnostics. These are director-input
        // conditioning only; the BANK motion planner and AA FBW remain unchanged.
        internal bool DirectorTargetHoldActive { get; private set; }
        internal bool DirectorTargetReversePending { get; private set; }
        internal float DirectorTargetReverseDwellRemaining { get; private set; }
        internal float DirectorTargetHoldBandDeg { get; private set; }
        // v0.4.58 HDG terminal quiet zone. This only conditions tiny upper-director
        // BANK target updates while terminal yaw is finishing heading capture.
        // It never modifies AA, final FlightCtrlState, or the BANK core motion law.
        internal bool HdgTerminalQuietMode { get; private set; }
        internal float HdgTerminalQuietHoldBandDeg { get; private set; }
        internal float HdgTerminalQuietReverseDwellSeconds { get; private set; }
        // v0.4.76 SETTLE rate-only damping.  This is deliberately blind to BankError:
        // it only dissipates a measurable residual roll rate before the next capture
        // cycle can start.  AA remains the final FBW controller.
        internal bool SettleRateOnlyDampingActive { get; private set; }
        internal float SettleRateOnlyDampingCommand { get; private set; }
        // v0.4.81 terminal-only AERIS virtual-roll command conditioner. It smooths only
        // small command changes before AA normal FBW; it never edits AA output.
        internal bool RollCommandConditionerActive { get; private set; }
        internal bool RollCommandReversePending { get; private set; }
        internal float RollCommandRawTarget { get; private set; }
        internal float RollCommandConditionedTarget { get; private set; }
        internal float RollCommandReverseDwellRemaining { get; private set; }
        // v0.4.84 trajectory hold telemetry. Hold has separate entry/exit corridors so
        // estimate noise cannot repeatedly restart the planned trajectory.
        internal bool TrajectoryHoldLatched { get; private set; }
        internal float TrajectoryHoldQuietElapsed { get; private set; }
        internal float TrajectoryHoldEntryBandDeg { get; private set; }
        internal float TrajectoryHoldExitBandDeg { get; private set; }
        internal float TrajectoryStoppingRateLimit { get; private set; }
        internal float TrajectoryRateError { get; private set; }
        internal float TrajectoryTerminalBlend { get; private set; }
        internal float TrajectoryScheduledDecel { get; private set; }
        // v0.4.84: when already-moving roll rate exceeds the stop envelope, planner
        // uses a stronger deceleration corridor rather than carrying momentum through target.
        internal bool TrajectoryBrakeEnvelopeActive { get; private set; }
        internal float TrajectoryBrakeAccelLimit { get; private set; }
        // v0.4.85 measured-rate recovery: when the physical roll rate has already
        // reversed away from the target, stale planner rate must fade to neutral
        // before a new trajectory is allowed to build.
        internal bool TrajectoryRateRecoveryActive { get; private set; }
        internal float TrajectoryRateRecoveryElapsed { get; private set; }
        internal float TrajectoryRecoveryReleaseRate { get; private set; }
        // v0.4.89 low-altitude precision hold. This is still an outer-loop attitude
        // director: it creates a very small AA native roll-rate demand only after the
        // main stopping-distance trajectory is quiet. It never writes AA output.
        internal bool PrecisionAltitudeEligible { get; private set; }
        internal float PrecisionAltitudeMeters { get; private set; }
        internal bool PrecisionHoldActive { get; private set; }
        internal bool PrecisionCorrectionActive { get; private set; }
        internal bool PrecisionWithinTarget { get; private set; }
        internal float PrecisionWithinTargetElapsed { get; private set; }
        internal float PrecisionTargetToleranceDeg { get; private set; }
        internal float PrecisionNeutralBandDeg { get; private set; }
        internal float PrecisionRateCommandDegPerSec { get; private set; }
        internal float PrecisionRateLimitDegPerSec { get; private set; }
        internal float PrecisionRateGainPerSec { get; private set; }
        internal float PrecisionRateDamping { get; private set; }
        // v0.4.87 dual H-BANK rate diagnostics. The BANK control rate is the original
        // causal 5 Hz low-pass of the exact H-BANK single-frame derivative. The longer
        // least-squares trend remains recorder-only evidence because its 0.24 s history
        // added destabilising phase lag during high-authority terminal motion.
        internal float HorizonBankRawRateDegPerSec { get; private set; }
        internal float HorizonBankTrendRateDegPerSec { get; private set; }
        internal float HorizonBankTrendResidualDeg { get; private set; }
        internal float HorizonBankTrendSpanSeconds { get; private set; }
        internal int HorizonBankTrendSampleCount { get; private set; }

        // Diagnostic-only BANK reference trace. No control decision uses these fields.
        internal float TraceReferenceBank { get; private set; }
        internal float TraceVesselTransformBank { get; private set; }
        internal float TraceUnprojectedBank { get; private set; }
        internal float TraceSurfaceRightBank { get; private set; }
        internal float TraceHorizonWingBank { get; private set; }
        internal bool TraceHorizonWingValid { get; private set; }
        // v0.3.15 candidate: KSP craft convention uses up=nose, right=right wing, forward=underside.
        // This is the player-facing local-horizon bank candidate; right-wing-down is positive.
        internal float TraceNavballBank { get; private set; }
        internal bool TraceNavballBankValid { get; private set; }
        // Legacy surface-reference attitude observation retained only for compatibility diagnostics.
        // AERIS remains observe-only and does not import external controller logic.
        internal float TraceLegacySurfaceReferenceBank { get; private set; }
        internal float TraceLegacySurfaceReferencePitch { get; private set; }
        internal bool TraceLegacySurfaceReferenceAttitudeValid { get; private set; }
        internal float TraceRawSignedAngle { get; private set; }
        internal float TraceLevelUpMagnitude { get; private set; }
        internal float TraceAircraftUpMagnitude { get; private set; }
        internal float TraceBodyAxisRollRate { get; private set; } // legacy: reference.forward axis
        internal float TraceVesselFacingAxisRate { get; private set; } // vessel.transform.up axis (KSP craft-facing convention)
        internal float TraceVesselForwardAxisRate { get; private set; }
        internal float TraceVesselRightAxisRate { get; private set; }
        internal float TraceReferenceUpAxisRate { get; private set; }
        internal float TraceReferenceRightAxisRate { get; private set; }
        internal float TraceAngularVelocityMagnitudeDegPerSec { get; private set; }
        internal float TraceReferenceVsVesselForwardDeg { get; private set; }
        internal float TraceForwardVsRadialDot { get; private set; }
        // Navball probe is observation-only. It uses reflection so this diagnostic survives
        // KSP UI field-name differences without binding AERIS control to internal UI code.
        internal bool TraceNavballFound { get; private set; }
        internal string TraceNavballSource { get; private set; }
        internal float TraceNavballLocalRollDeg { get; private set; }
        internal float TraceNavballWorldRollDeg { get; private set; }
        internal float TraceNavballCandidateDeltaDeg { get; private set; }
        internal string TraceReferenceName { get; private set; } = "none";
        // v0.3.15 axis calibration: all fields are observation-only. Values are dot products
        // against local radial-up and surface velocity, so we can identify which transform axis
        // actually follows the aircraft nose / wings in the active KSP control-point convention.
        internal string TraceRootPartName { get; private set; } = "none";
        internal float TraceRefForwardRadialDot { get; private set; }
        internal float TraceRefUpRadialDot { get; private set; }
        internal float TraceRefRightRadialDot { get; private set; }
        internal float TraceVesselForwardRadialDot { get; private set; }
        internal float TraceVesselUpRadialDot { get; private set; }
        internal float TraceVesselRightRadialDot { get; private set; }
        internal float TraceRootForwardRadialDot { get; private set; }
        internal float TraceRootUpRadialDot { get; private set; }
        internal float TraceRootRightRadialDot { get; private set; }
        internal float TraceRefForwardSpeedDot { get; private set; }
        internal float TraceRefUpSpeedDot { get; private set; }
        internal float TraceRefRightSpeedDot { get; private set; }
        internal float TraceVesselForwardSpeedDot { get; private set; }
        internal float TraceVesselUpSpeedDot { get; private set; }
        internal float TraceVesselRightSpeedDot { get; private set; }
        internal float TraceRootForwardSpeedDot { get; private set; }
        internal float TraceRootUpSpeedDot { get; private set; }
        internal float TraceRootRightSpeedDot { get; private set; }

        // v0.4.27 hysteresis-gated BANK capture law.
        // The controller owns an H-BANK outer loop and a causal filtered H-BANK-rate inner loop.
        // It deliberately avoids instant full-stick reversals: approach rate is bounded by stopping
        // distance, and stick output is slew-limited so the aircraft cannot "dance" around target.
        internal float MaxBankRateDegPerSec = 28f;
        internal float MaxBankDecelDegPerSec2 = 10f; // v0.4.43: earlier, gentler braking for smoother aircraft motion
        internal float MaxVirtualRoll = 0.38f;
        internal float TerminalEntryBandDeg = 0.30f;
        internal float TerminalExitBandDeg = 0.60f;
        internal float BrakeBandDeg = 0.10f;
        internal float TrimBandDeg = 0.03f;
        internal float SettleRateThresholdDegPerSec = 0.18f;
        internal float SettleQuietSeconds = 0.75f;
        internal float PrecisionToleranceDeg = 0.01f;
        internal float PrecisionCaptureSeconds = 3.0f;
        internal float PrecisionTrimGain = 0.08f;
        internal float MaxPrecisionTrim = 0.035f;
        // v0.5.2 all-altitude BANK precision profile: retain steady-state error
        // inside +/-0.03 deg wherever BANK is valid and executable. This changes
        // only the proven precision-hold corridor; it does not alter the main
        // stopping-distance trajectory, AA transport, or final output.
        // PrecisionAltitudeEligible is retained as a legacy CSV/API field name,
        // but is now a profile-availability flag rather than an altitude gate.
        internal float AllAltitudePrecisionTargetToleranceDeg = 0.03f;
        internal float AllAltitudePrecisionNeutralBandDeg = 0.008f;
        internal float AllAltitudePrecisionEntryBandDeg = 0.18f;
        internal float AllAltitudePrecisionExitBandDeg = 0.30f;
        internal float AllAltitudePrecisionEntryRateDegPerSec = 0.32f;
        internal float AllAltitudePrecisionExitRateDegPerSec = 0.72f;
        internal float AllAltitudePrecisionRateGainPerSec = 1.35f;
        internal float AllAltitudePrecisionRateDamping = 0.20f;
        internal float AllAltitudePrecisionMaxRateDegPerSec = 0.16f;
        internal float AllAltitudePrecisionPlannerAccelDegPerSec2 = 0.80f;
        internal float AllAltitudePrecisionPlannerJerkDegPerSec3 = 4.00f;
        internal float MaxStickSlewPerSec = 0.85f;
        // v0.4.87: restore the proven low-lag control estimate. The trend calculation
        // is preserved below as diagnostics only, so we can measure its phase relation
        // without allowing it to destabilise the closed roll-rate loop.
        internal float HorizonRateFilterHz = 5.0f;
        internal float HorizonRateTrendWindowSeconds = 0.24f;
        internal float HorizonRateTrendMinimumSpanSeconds = 0.08f;
        internal int HorizonRateTrendMinimumSamples = 5;
        internal float RateLoopGain = 0.0125f;
        internal float MinimumCaptureRateDegPerSec = 0.035f;
        internal float VerticalHorizonMinimum = 0.15f;
        // v0.4.31: terminal-only command conditioning. It does not alter CAPTURE/BRAKE.
        internal float TerminalChatterBandDeg = 0.10f;
        internal float TerminalChatterStickBand = 0.030f;
        internal float TerminalReverseLockSeconds = 0.18f;
        internal float TerminalSlewSpeedStartMps = 100f;
        internal float TerminalSlewSpeedFullMps = 350f;
        internal float TerminalSlewMinimumScale = 0.35f;
        // v0.4.31: BRAKE/SETTLE are allowed to decelerate, but should not
        // continuously chase small rate noise or flip small counter-commands.
        internal float TransitionQuietErrorBandDeg = 0.30f;
        internal float TransitionQuietRateBandDegPerSec = 1.20f;
        internal float TransitionReverseStickBand = 0.075f;
        internal float TransitionCommandHoldSeconds = 0.12f;
        internal float TransitionSlewMinimumScale = 0.45f;
        internal float TransitionUpdateIntervalLowSpeed = 0.08f;
        internal float TransitionUpdateIntervalHighSpeed = 0.18f;
        internal float TransitionImmediateUpdateDelta = 0.045f;
        internal float TransitionImmediateErrorBandDeg = 0.65f;
        internal float TransitionCommandDeadbandLowSpeed = 0.0030f;
        internal float TransitionCommandDeadbandHighSpeed = 0.0070f;
        // v0.4.34: command-shaper limits for BRAKE/SETTLE. Low speed receives the
        // strongest smoothing because its delayed aerodynamic response created the visible tremble.
        internal float TransitionRateAccelLowSpeedDegPerSec2 = 3.5f;
        internal float TransitionRateAccelHighSpeedDegPerSec2 = 8.0f;
        internal float TransitionRateReversalPersistenceSeconds = 0.14f;
        // v0.4.42 motion planner. Capture authority remains moderate; terminal phases
        // use slower acceleration so the craft follows a continuous S-curve into the target.
        internal float MotionPlannerCaptureAccelLowSpeedDegPerSec2 = 7.0f;
        internal float MotionPlannerCaptureAccelHighSpeedDegPerSec2 = 14.0f;
        internal float MotionPlannerTerminalAccelLowSpeedDegPerSec2 = 2.0f; // v0.4.43: softer terminal rate ramp at low speed
        internal float MotionPlannerTerminalAccelHighSpeedDegPerSec2 = 5.0f; // v0.4.43: softer terminal rate ramp at high speed
        internal float MotionPlannerCaptureJerkLowSpeedDegPerSec3 = 12.0f;
        internal float MotionPlannerCaptureJerkHighSpeedDegPerSec3 = 24.0f;
        internal float MotionPlannerTerminalJerkLowSpeedDegPerSec3 = 4.0f; // v0.4.43: gentler transition curvature at low speed
        internal float MotionPlannerTerminalJerkHighSpeedDegPerSec3 = 10.0f; // v0.4.43: gentler transition curvature at high speed
        // v0.4.37: once the terminal controller requests near-zero roll rate,
        // remove shaper momentum promptly instead of carrying it into SETTLE.
        internal float TransitionZeroCaptureRawRateBandDegPerSec = 0.15f;
        internal float TransitionZeroCaptureBankErrorBandDeg = 0.15f;
        internal float TransitionZeroCaptureTimeSeconds = 0.12f;
        // v0.4.48 terminal re-entry gate. These values apply only after BANK is already
        // in a passive terminal phase; they do not slow normal Capture/Brake entry.
        internal float SettleBrakeReentryBandDeg = 0.18f;
        internal float SettleBrakeReentryDwellSeconds = 0.12f;
        internal float SettleBrakeImmediateBandDeg = 0.55f;
        internal float SettleBrakeImmediateRollRateDegPerSec = 2.50f;
        // v0.4.81: continuous terminal-trim corridor. Inside this band BANK no
        // longer alternates between BRAKE and SETTLE; it commands one small,
        // rate-damped continuous correction toward target.
        internal float TerminalContinuousTrimBandDeg = 0.18f;
        internal float TerminalContinuousTrimRateGain = 1.30f;
        internal float TerminalContinuousTrimRateDamping = 0.60f;
        internal float TerminalContinuousTrimMaxRateDegPerSec = 0.22f;
        // v0.4.76: only a tiny rate-only damping request in SETTLE.  It is not
        // position control and is disabled inside the roll-rate noise floor.
        internal float SettleRateOnlyDampingGain = 0.0075f;
        internal float SettleRateOnlyDampingLimit = 0.018f;
        internal float SettleRateOnlyDampingMinRateDegPerSec = 0.22f;
        // v0.4.81 terminal-only virtual-roll conditioning. Normal BANK/HDG tracking
        // bypasses this entirely so the director can correct ordinary bank error promptly.
        // In terminal phases, a small opposite command is attenuated rather than held
        // at zero; same-direction corrections always pass through unchanged.
        internal float RollCommandTerminalSmallReverseBand = 0.055f;
        internal float RollCommandTerminalReverseAttenuation = 0.50f;
        // v0.4.45 Director-side dynamic-pressure schedule. q<=4 kPa is low-q;
        // q>=12 kPa is the normal medium-q baseline. Thresholds are intentionally
        // initial test values, not a fixed aircraft specification.
        internal float DynamicPressureLowQKpa = 4.0f;
        internal float DynamicPressureMediumQKpa = 12.0f;
        internal float LowQCaptureRateScale = 0.60f;
        internal float LowQTerminalRateScale = 0.70f;
        internal float LowQDecelScale = 0.65f;
        internal float LowQPlannerScale = 0.55f;
        internal float LowQStickScale = 0.70f;
        // v0.4.45 high-q schedule.  q>=24 kPa begins a gradual return to a
        // more conservative director trajectory; q>=60 kPa reaches the initial
        // high-q endpoint. Values are intentionally test baselines, not fixed specs.
        internal float DynamicPressureHighQStartKpa = 24.0f;
        internal float DynamicPressureHighQFullKpa = 60.0f;
        internal float HighQCaptureRateScale = 0.72f;
        internal float HighQTerminalRateScale = 0.82f; // terminal convergence must not become sluggish at high q
        internal float HighQDecelScale = 0.72f;
        internal float HighQPlannerScale = 0.82f;
        internal float HighQStickScale = 0.70f;
        internal float HighQReversalPersistenceScale = 2.10f; // but do not permit fast sign-flip chatter

        float lastTrace;
        bool formalAttitudeValid;
        float formalBankWrappedDeg;
        bool formalHorizonBankValid;
        float formalHorizonBankDeg;
        float formalRollRateDegPerSec;
        float precisionTrim;
        float lastControlTime;
        float previousHorizonBank;
        float previousHorizonBankTime;
        bool havePreviousHorizonBank;
        float filteredHorizonBankRate;
        float horizonBankUnwrappedDeg;
        readonly Queue<HorizonBankRateSample> horizonBankRateSamples = new Queue<HorizonBankRateSample>();
        float formalPitchDeg;

        struct HorizonBankRateSample
        {
            internal float Time;
            internal float UnwrappedBankDeg;
        }
        float settleQuietSince;
        float settleBrakeReentrySince;
        bool terminalLatched;
        float diagnosticPreviousVirtualRoll;
        int diagnosticPreviousCommandSign;
        int diagnosticPreviousRateSign;
        int diagnosticPreviousErrorSign;
        readonly Queue<float> diagnosticCommandFlipTimes = new Queue<float>();
        readonly Queue<float> diagnosticRateFlipTimes = new Queue<float>();
        readonly Queue<float> diagnosticErrorFlipTimes = new Queue<float>();
        float terminalReverseLockUntil;
        float transitionCommandHoldUntil;
        float transitionNextUpdateTime;
        float transitionHeldTarget;
        readonly Queue<float> transitionUpdateTimes = new Queue<float>();
        float transitionShapedRate;
        float transitionReverseCandidateSince;
        float transitionShapedRateAccel; // v0.4.36 jerk-limited command-shaper state
        // v0.4.53 upper-director target conditioner state.
        float directorRawTarget;
        float directorLastTargetDelta;
        float directorReverseCandidateSince;
        float rollCommandReverseCandidateSince;
        float trajectoryHoldQuietSince;
        float trajectoryRateRecoverySince;
        float precisionWithinTargetSince;

        internal void SetCurrent(Vessel vessel)
        {
            if (vessel == null) return;
            TargetBank = ReadObservedBank(vessel);
            TargetBankText = AERISNumericField.Format(TargetBank);
        }

        internal bool TrySetTarget(string text, out string error)
        {
            float value;
            if (!AERISNumericField.TryParseSigned(text, out value))
            {
                error = "BANK accepts ASCII digits, optional leading + or -, and one decimal point.";
                return false;
            }
            TargetBank = Mathf.Clamp(value, -90f, 90f);
            TargetBankText = AERISNumericField.Format(TargetBank);
            error = null;
            return true;
        }

        // Used only by upper-level AERIS lateral directors (currently HDG).
        // The BANK controller remains the sole roll executor.
        internal void SetHdgTerminalQuietMode(bool active)
        {
            HdgTerminalQuietMode = active;
            if (!active)
            {
                HdgTerminalQuietHoldBandDeg = 0f;
                HdgTerminalQuietReverseDwellSeconds = 0f;
            }
        }

        internal void SetTargetFromDirector(float targetBank)
        {
            if (float.IsNaN(targetBank) || float.IsInfinity(targetBank)) targetBank = 0f;
            float candidate = Mathf.Clamp(targetBank, -90f, 90f);
            float now = Time.realtimeSinceStartup;
            float qT = DynamicPressureSchedule;
            float highT = DynamicPressureHighQSchedule;
            // Continuous q scheduling: no discrete target-mode switch.
            DirectorTargetHoldBandDeg = Mathf.Lerp(0.10f, 0.18f, qT);
            DirectorTargetHoldBandDeg = Mathf.Lerp(DirectorTargetHoldBandDeg, 0.32f, highT);
            float reverseDwell = Mathf.Lerp(0.10f, 0.18f, qT);
            reverseDwell = Mathf.Lerp(reverseDwell, 0.32f, highT);
            if (HdgTerminalQuietMode)
            {
                // Terminal yaw owns the final heading trim. BANK should not repeatedly
                // chase 0.1-degree-class HDG target changes or rapid small reversals.
                float quietHold = Mathf.Lerp(0.26f, 0.38f, qT);
                quietHold = Mathf.Lerp(quietHold, 0.50f, highT);
                float quietDwell = Mathf.Lerp(0.28f, 0.42f, qT);
                quietDwell = Mathf.Lerp(quietDwell, 0.55f, highT);
                HdgTerminalQuietHoldBandDeg = quietHold;
                HdgTerminalQuietReverseDwellSeconds = quietDwell;
                DirectorTargetHoldBandDeg = Mathf.Max(DirectorTargetHoldBandDeg, quietHold);
                reverseDwell = Mathf.Max(reverseDwell, quietDwell);
            }
            else
            {
                HdgTerminalQuietHoldBandDeg = 0f;
                HdgTerminalQuietReverseDwellSeconds = 0f;
            }
            float reverseBand = DirectorTargetHoldBandDeg * 1.8f;
            float delta = candidate - TargetBank;
            bool wantsReverse = directorLastTargetDelta != 0f && delta != 0f && Mathf.Sign(directorLastTargetDelta) != Mathf.Sign(delta);
            DirectorTargetHoldActive = false;
            DirectorTargetReversePending = false;
            DirectorTargetReverseDwellRemaining = 0f;
            directorRawTarget = candidate;
            if (Mathf.Abs(delta) <= DirectorTargetHoldBandDeg)
            {
                DirectorTargetHoldActive = true;
                directorReverseCandidateSince = 0f;
                return;
            }
            if (wantsReverse && Mathf.Abs(delta) <= reverseBand)
            {
                if (directorReverseCandidateSince <= 0f) directorReverseCandidateSince = now;
                float elapsed = now - directorReverseCandidateSince;
                if (elapsed < reverseDwell)
                {
                    DirectorTargetHoldActive = true;
                    DirectorTargetReversePending = true;
                    DirectorTargetReverseDwellRemaining = reverseDwell - elapsed;
                    return;
                }
            }
            else directorReverseCandidateSince = 0f;
            TargetBank = candidate;
            TargetBankText = TargetBank.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            directorLastTargetDelta = delta;
        }

        internal void SetArmed(bool armed, Vessel vessel)
        {
            Armed = armed;
            if (armed)
            {
                if (string.IsNullOrEmpty(TargetBankText)) SetCurrent(vessel);
                ResetHorizonBankRateEstimator();
                precisionTrim = 0f;
                directorRawTarget = TargetBank;
                directorReverseCandidateSince = 0f;
                directorLastTargetDelta = 0f;
                DirectorTargetHoldActive = false;
                DirectorTargetReversePending = false;
                DirectorTargetReverseDwellRemaining = 0f;
                SettleRateOnlyDampingActive = false;
                SettleRateOnlyDampingCommand = 0f;
                RollCommandConditionerActive = false;
                RollCommandReversePending = false;
                RollCommandRawTarget = 0f;
                RollCommandConditionedTarget = 0f;
                RollCommandReverseDwellRemaining = 0f;
                rollCommandReverseCandidateSince = 0f;
                HdgTerminalQuietMode = false;
                HdgTerminalQuietHoldBandDeg = 0f;
                HdgTerminalQuietReverseDwellSeconds = 0f;
                lastControlTime = 0f;
                settleQuietSince = 0f;
                settleBrakeReentrySince = 0f;
                SettleBrakeReentryPending = false;
                SettleBrakeReentryElapsed = 0f;
                SettleBrakeReentryForced = false;
                terminalLatched = false;
                TrajectoryHoldLatched = false;
                trajectoryHoldQuietSince = 0f;
                TrajectoryHoldQuietElapsed = 0f;
                precisionWithinTargetSince = 0f;
                PrecisionAltitudeEligible = false;
                PrecisionAltitudeMeters = 0f;
                PrecisionHoldActive = false;
                PrecisionCorrectionActive = false;
                PrecisionWithinTarget = false;
                PrecisionWithinTargetElapsed = 0f;
                PrecisionTargetToleranceDeg = 0f;
                PrecisionNeutralBandDeg = 0f;
                PrecisionRateCommandDegPerSec = 0f;
                PrecisionRateLimitDegPerSec = 0f;
                PrecisionRateGainPerSec = 0f;
                PrecisionRateDamping = 0f;
                TrajectoryBrakeEnvelopeActive = false;
                TrajectoryBrakeAccelLimit = 0f;
                terminalReverseLockUntil = 0f;
                transitionCommandHoldUntil = 0f;
                transitionNextUpdateTime = 0f;
                transitionHeldTarget = 0f;
                transitionShapedRate = 0f;
                transitionReverseCandidateSince = 0f;
                TransitionRawRateRequest = 0f;
                TransitionShapedRateRequest = 0f;
                TransitionRateAccelLimit = 0f;
                TransitionRateShaperActive = false;
                MotionPlannerActive = false;
                MotionPlannerPlannedRate = 0f;
                MotionPlannerPlannedAccel = 0f;
                MotionPlannerAccelLimit = 0f;
                MotionPlannerJerkLimit = 0f;
                TransitionZeroCaptureActive = false;
                TransitionZeroCaptureRate = 0f;
                TransitionRateFeedbackDeadbandActive = false;
                TransitionRateFeedbackDeadbandDegPerSec = 0f;
                EffectiveRollRateForControl = 0f;
                DynamicPressureKpa = 0f;
                DynamicPressureSchedule = 1f;
                DynamicPressureHighQSchedule = 0f;
                DynamicPressureMode = "MID_Q";
                DynamicPressureRateScale = 1f;
                DynamicPressureStickScale = 1f;
                LimitedRollRateRequest = 0f;
                transitionUpdateTimes.Clear();
                TransitionUpdateGated = false;
                TransitionUpdateInterval = 0f;
                TransitionHeldTarget = 0f;
                TransitionCommandUpdates1s = 0;
                TransitionDeltaSuppressed = false;
                TransitionCommandDelta = 0f;
                TransitionCommandDeadband = 0f;
                ResetSmoothnessDiagnostics();
                CapturePhase = "Capture";
                VirtualPilotRoll = 0f;
                AERISLogger.Info("[BANK] armed target=" + TargetBank.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " transport=AA_NATIVE_ROLL_RATE");
            }
            else Release("UI");
        }

        internal void Disable(string reason) { Armed = false; Release(reason); }

        // Called by AERIS before AA's OnAutopilotUpdate handler. It owns the pilot roll channel
        // only long enough to neutralize it, then supplies AA's native angular-rate demand.
        internal void ApplyAaNativeRollRateDemand(FlightCtrlState state, Vessel vessel, bool aerisMaster, bool standardFbwActive)
        {
            bool executable = Armed && aerisMaster && standardFbwActive && vessel != null &&
                              !vessel.LandedOrSplashed && !vessel.packed && state != null;
            if (!executable) {
                // The native override is static inside AA, so clear it on every inactive path.
                // Do not repeatedly Release(): that would flood CVR and erase useful observer state.
                ClearAaNativeRollRateOverride();
                ClearPrecisionHoldTelemetry();
                ControlState = Armed ? "ObserveOnlyStandby" : "ObserveOnly";
                return;
            }

            // Snapshot both FlightInputHandler (pilot baseline) and the mutable command state
            // before AERIS writes anything. This observer must not alter input ownership.
            FlightCtrlState pilot = FlightInputHandler.state;
            ObserverPilotPitch = pilot != null ? pilot.pitch : 0f;
            ObserverPilotRoll = pilot != null ? pilot.roll : 0f;
            ObserverPilotYaw = pilot != null ? pilot.yaw : 0f;
            ObserverPilotThrottle = pilot != null ? pilot.mainThrottle : 0f;
            ObserverStatePitchBefore = state.pitch;
            ObserverStateRollBefore = state.roll;
            ObserverStateYawBefore = state.yaw;
            ObserverStateThrottleBefore = state.mainThrottle;
            ObserverManualRollActive = Mathf.Abs(ObserverPilotRoll) > 0.015f || Mathf.Abs(ObserverStateRollBefore) > 0.015f;
            RawPilotRoll = ObserverPilotRoll;
            CurrentBank = ReadObservedBank(vessel);
            UpdateBankReferenceTrace(vessel);
            BankError = Mathf.DeltaAngle(CurrentBank, TargetBank);
            float now = Time.realtimeSinceStartup;
            float dtControl = lastControlTime > 0f ? Mathf.Clamp(now - lastControlTime, 0.001f, 0.10f) : Time.fixedDeltaTime;
            lastControlTime = now;
            DiagnosticControlDt = dtControl;

            // Horizon BANK becomes ill-conditioned when the craft longitudinal axis approaches
            // local vertical. Do not continue driving a geometrically ambiguous bank angle.
            bool horizonSafe = formalHorizonBankValid && Mathf.Abs(formalPitchDeg) < 81.0f;
            if (!horizonSafe)
            {
                VirtualPilotRoll = Mathf.MoveTowards(VirtualPilotRoll, 0f, MaxStickSlewPerSec * dtControl);
                RollRateRequest = 0f;
                ActualRollRate = 0f;
                InjectedRoll = 0f;
                ClearAaNativeRollRateOverride();
                ClearPrecisionHoldTelemetry();
                ControlState = "HorizonInvalidHold";
                return;
            }

            // Derive the rate from the exact same HorizonBankDeg used by the outer loop.
            // v0.4.87 restores the original low-lag 5 Hz causal filter for BANK control.
            // The 0.24 s least-squares trend remains diagnostics-only evidence.
            // Quaternion body roll remains valuable telemetry but is not the rate of this
            // local-horizon coordinate during coupled pitch/yaw motion.
            UpdateHorizonBankRate(now);

            // Director-side q scheduling. Low-q aircraft responses are delayed and nonlinear;
            // do not demand the same roll trajectory that is appropriate in medium-q flight.
            // This deliberately does not alter AA's FBW/PID internals.
            float densitySample = (float)vessel.atmDensity;
            float speedSample = (float)vessel.srfSpeed;
            bool qInputsValid = IsFinite(densitySample) && densitySample >= 0f &&
                IsFinite(speedSample) && speedSample >= 0f;
            float dynamicPressureSample = qInputsValid
                ? 0.5f * densitySample * speedSample * speedSample / 1000f : 0f;
            DynamicPressureKpa = IsFinite(dynamicPressureSample) && dynamicPressureSample >= 0f
                ? dynamicPressureSample : 0f;
            // Three-region continuous dynamic-pressure scheduling:
            // LOW_Q -> MID_Q -> HIGH_Q.  The first blend recovers from low-q
            // conservatism, the second blend progressively adds high-q restraint.
            DynamicPressureSchedule = Mathf.Clamp01((DynamicPressureKpa - DynamicPressureLowQKpa) /
                Mathf.Max(0.01f, DynamicPressureMediumQKpa - DynamicPressureLowQKpa));
            DynamicPressureHighQSchedule = Mathf.Clamp01((DynamicPressureKpa - DynamicPressureHighQStartKpa) /
                Mathf.Max(0.01f, DynamicPressureHighQFullKpa - DynamicPressureHighQStartKpa));
            if (DynamicPressureSchedule <= 0f) DynamicPressureMode = "LOW_Q";
            else if (DynamicPressureHighQSchedule >= 1f) DynamicPressureMode = "HIGH_Q";
            else if (DynamicPressureHighQSchedule > 0f) DynamicPressureMode = "Q_HIGH_BLEND";
            else if (DynamicPressureSchedule < 1f) DynamicPressureMode = "Q_LOW_BLEND";
            else DynamicPressureMode = "MID_Q";

            float lowToMidRate = Mathf.Lerp(LowQCaptureRateScale, 1f, DynamicPressureSchedule);
            float lowToMidStick = Mathf.Lerp(LowQStickScale, 1f, DynamicPressureSchedule);
            float lowToMidTerminal = Mathf.Lerp(LowQTerminalRateScale, 1f, DynamicPressureSchedule);
            float lowToMidDecel = Mathf.Lerp(LowQDecelScale, 1f, DynamicPressureSchedule);
            float lowToMidPlanner = Mathf.Lerp(LowQPlannerScale, 1f, DynamicPressureSchedule);

            DynamicPressureRateScale = lowToMidRate * Mathf.Lerp(1f, HighQCaptureRateScale, DynamicPressureHighQSchedule);
            DynamicPressureStickScale = lowToMidStick * Mathf.Lerp(1f, HighQStickScale, DynamicPressureHighQSchedule);
            float qTerminalRateScale = lowToMidTerminal * Mathf.Lerp(1f, HighQTerminalRateScale, DynamicPressureHighQSchedule);
            float qDecelScale = lowToMidDecel * Mathf.Lerp(1f, HighQDecelScale, DynamicPressureHighQSchedule);
            float qPlannerScale = lowToMidPlanner * Mathf.Lerp(1f, HighQPlannerScale, DynamicPressureHighQSchedule);
            float qReversalPersistenceScale = Mathf.Lerp(1f, HighQReversalPersistenceScale, DynamicPressureHighQSchedule);
            float scheduledMaxBankRate = MaxBankRateDegPerSec * DynamicPressureRateScale;
            float scheduledBankDecel = MaxBankDecelDegPerSec2 * qDecelScale;

            float absError = Mathf.Abs(BankError);
            float desiredRate;
            bool rateQuiet = Mathf.Abs(ActualRollRate) <= SettleRateThresholdDegPerSec;

            // v0.4.27 terminal hysteresis: after entering the final 0.30 deg corridor,
            // do not return to the aggressive CAPTURE law until the aircraft has genuinely
            // escaped beyond 0.60 deg. This prevents Capture/Settle ping-pong.
            if (!terminalLatched && absError <= TerminalEntryBandDeg) terminalLatched = true;
            if (terminalLatched && absError > TerminalExitBandDeg)
            {
                terminalLatched = false;
                terminalReverseLockUntil = 0f;
                transitionCommandHoldUntil = 0f;
                transitionNextUpdateTime = 0f;
                transitionHeldTarget = 0f;
                transitionUpdateTimes.Clear();
                TransitionUpdateGated = false;
                TransitionUpdateInterval = 0f;
                TransitionHeldTarget = 0f;
                TransitionCommandUpdates1s = 0;
                TransitionDeltaSuppressed = false;
                TransitionCommandDelta = 0f;
                TransitionCommandDeadband = 0f;
                ResetSmoothnessDiagnostics();
                settleQuietSince = 0f;
                settleBrakeReentrySince = 0f;
                trajectoryRateRecoverySince = 0f;
            }

            // v0.4.82 trajectory-first lateral rewrite.
            // The primary law is a single, continuous stopping-distance roll-rate trajectory:
            // build roll rate while distance remains, then reduce the permitted rate BEFORE
            // the target.  It deliberately replaces the old Capture/Brake/Settle/Trim chase.
            // Any airframe/AA correction remains downstream; AERIS itself does not create
            // post-arrival left-right position corrections.
            SettleBrakeReentryPending = false;
            SettleBrakeReentryElapsed = 0f;
            SettleBrakeReentryForced = false;
            settleBrakeReentrySince = 0f;
            settleQuietSince = 0f;

            // v0.5.2 all-altitude precision hold:
            // v0.4.88 correctly eliminated AA-versus-AERIS fighting, but after a quiet
            // arrival its zero-rate HOLD intentionally allowed a static bank bias to remain.
            // The terminal latch is now a precision corridor at every valid altitude: the
            // main stopping-distance trajectory still performs arrival, then a bounded
            // native roll-rate request removes only the residual. The demand is exactly
            // zero in a smaller neutral band, so this is not a high-frequency position chase.
            float precisionAltitudeSample = vessel != null ? (float)vessel.altitude : 0f;
            PrecisionAltitudeEligible = vessel != null && IsFinite(precisionAltitudeSample);
            PrecisionAltitudeMeters = PrecisionAltitudeEligible ? Mathf.Max(0f, precisionAltitudeSample) : 0f;
            PrecisionTargetToleranceDeg = PrecisionAltitudeEligible ? AllAltitudePrecisionTargetToleranceDeg : 0f;
            PrecisionNeutralBandDeg = PrecisionAltitudeEligible ? AllAltitudePrecisionNeutralBandDeg : 0f;
            PrecisionRateLimitDegPerSec = PrecisionAltitudeEligible ? AllAltitudePrecisionMaxRateDegPerSec : 0f;
            PrecisionRateGainPerSec = PrecisionAltitudeEligible ? AllAltitudePrecisionRateGainPerSec : 0f;
            PrecisionRateDamping = PrecisionAltitudeEligible ? AllAltitudePrecisionRateDamping : 0f;

            // Every valid BANK execution now enters the same low-rate precision corridor.
            TrajectoryHoldEntryBandDeg = AllAltitudePrecisionEntryBandDeg;
            TrajectoryHoldExitBandDeg = AllAltitudePrecisionExitBandDeg;
            float holdEntryRate = AllAltitudePrecisionEntryRateDegPerSec;
            float holdExitRate = AllAltitudePrecisionExitRateDegPerSec;
            float holdDwellSeconds = 0.12f;

            float stoppingRate = Mathf.Sqrt(Mathf.Max(0f, 2f * scheduledBankDecel * absError));
            float rateMagnitude = Mathf.Min(scheduledMaxBankRate, stoppingRate);
            TrajectoryStoppingRateLimit = stoppingRate;
            TrajectoryScheduledDecel = scheduledBankDecel;

            bool holdEntryCandidate = absError <= TrajectoryHoldEntryBandDeg
                && Mathf.Abs(ActualRollRate) <= holdEntryRate
                && Mathf.Abs(transitionShapedRate) <= holdEntryRate;
            if (!TrajectoryHoldLatched)
            {
                if (holdEntryCandidate)
                {
                    if (trajectoryHoldQuietSince <= 0f) trajectoryHoldQuietSince = now;
                    if (now - trajectoryHoldQuietSince >= holdDwellSeconds) TrajectoryHoldLatched = true;
                }
                else trajectoryHoldQuietSince = 0f;
            }
            else
            {
                bool significantDeparture = absError >= TrajectoryHoldExitBandDeg
                    || Mathf.Abs(ActualRollRate) >= holdExitRate;
                if (significantDeparture)
                {
                    TrajectoryHoldLatched = false;
                    trajectoryHoldQuietSince = 0f;
                }
            }
            TrajectoryHoldQuietElapsed = trajectoryHoldQuietSince > 0f ? Mathf.Max(0f, now - trajectoryHoldQuietSince) : 0f;

            PrecisionHoldActive = TrajectoryHoldLatched && PrecisionAltitudeEligible;
            PrecisionRateCommandDegPerSec = 0f;
            PrecisionCorrectionActive = false;
            if (PrecisionAltitudeEligible && absError <= PrecisionTargetToleranceDeg)
            {
                if (precisionWithinTargetSince <= 0f) precisionWithinTargetSince = now;
                PrecisionWithinTarget = true;
                PrecisionWithinTargetElapsed = Mathf.Max(0f, now - precisionWithinTargetSince);
            }
            else
            {
                precisionWithinTargetSince = 0f;
                PrecisionWithinTarget = false;
                PrecisionWithinTargetElapsed = 0f;
            }

            if (TrajectoryHoldLatched)
            {
                if (PrecisionHoldActive)
                {
                    // Subtract a neutral band before the P term. This gives a continuous
                    // zero-demand center; AA's native rate loop is still the sole actuator.
                    float residualMagnitude = Mathf.Max(0f, absError - PrecisionNeutralBandDeg);
                    float residualError = Mathf.Sign(BankError) * residualMagnitude;
                    float rawPrecisionRate = PrecisionRateGainPerSec * residualError - PrecisionRateDamping * ActualRollRate;
                    PrecisionRateCommandDegPerSec = Mathf.Clamp(rawPrecisionRate,
                        -PrecisionRateLimitDegPerSec, PrecisionRateLimitDegPerSec);
                    PrecisionCorrectionActive = Mathf.Abs(PrecisionRateCommandDegPerSec) > 0.001f;
                    desiredRate = PrecisionRateCommandDegPerSec;
                    CapturePhase = PrecisionCorrectionActive ? "Precision" : "Hold";
                }
                else
                {
                    CapturePhase = "Hold";
                    desiredRate = 0f;
                }
                precisionTrim = Mathf.MoveTowards(precisionTrim, 0f, 0.12f * dtControl);
            }
            else
            {
                CapturePhase = "Trajectory";
                desiredRate = Mathf.Sign(BankError) * rateMagnitude;
                precisionTrim = Mathf.MoveTowards(precisionTrim, 0f, 0.10f * dtControl);
            }
            LimitedRollRateRequest = desiredRate;

            // v0.4.42 Bank Motion Planner. The outer loop creates a desired roll-rate;
            // this planner turns it into a continuous S-curve by limiting both rate
            // acceleration and jerk before virtual-pilot injection. It is AERIS-only.
            float trajectoryTerminalBlend = Mathf.SmoothStep(0f, 1f,
                1f - Mathf.Clamp01(absError / Mathf.Max(0.01f, TerminalExitBandDeg)));
            bool terminalMotion = trajectoryTerminalBlend > 0.001f;
            TrajectoryTerminalBlend = trajectoryTerminalBlend;
            MotionPlannerActive = CapturePhase == "Trajectory" || CapturePhase == "Precision" || CapturePhase == "Hold";
            TransitionRawRateRequest = desiredRate;
            TransitionRateShaperActive = MotionPlannerActive;
            TrajectoryBrakeEnvelopeActive = false;
            TrajectoryBrakeAccelLimit = 0f;
            TrajectoryRateRecoveryActive = false;
            TrajectoryRateRecoveryElapsed = 0f;
            TrajectoryRecoveryReleaseRate = 0f;
            if (MotionPlannerActive)
            {
                float speedForPlanner = vessel != null && IsFinite((float)vessel.srfSpeed)
                    ? Mathf.Max(0f, (float)vessel.srfSpeed) : 0f;
                float speedT = Mathf.Clamp01((speedForPlanner - 70f) / 250f);
                float captureAccel = Mathf.Lerp(MotionPlannerCaptureAccelLowSpeedDegPerSec2, MotionPlannerCaptureAccelHighSpeedDegPerSec2, speedT);
                float terminalAccel = Mathf.Lerp(MotionPlannerTerminalAccelLowSpeedDegPerSec2, MotionPlannerTerminalAccelHighSpeedDegPerSec2, speedT);
                float captureJerk = Mathf.Lerp(MotionPlannerCaptureJerkLowSpeedDegPerSec3, MotionPlannerCaptureJerkHighSpeedDegPerSec3, speedT);
                float terminalJerk = Mathf.Lerp(MotionPlannerTerminalJerkLowSpeedDegPerSec3, MotionPlannerTerminalJerkHighSpeedDegPerSec3, speedT);
                MotionPlannerAccelLimit = Mathf.Lerp(captureAccel, terminalAccel, trajectoryTerminalBlend);
                MotionPlannerJerkLimit = Mathf.Lerp(captureJerk, terminalJerk, trajectoryTerminalBlend);
                MotionPlannerAccelLimit *= qPlannerScale;
                MotionPlannerJerkLimit *= qPlannerScale;
                if (PrecisionHoldActive)
                {
                    // Precision mode is intentionally rate-limited and jerk-limited before
                    // the AA native controller sees it. This is the final accuracy layer,
                    // not a return to high-authority alternating terminal correction.
                    MotionPlannerAccelLimit = Mathf.Min(MotionPlannerAccelLimit, AllAltitudePrecisionPlannerAccelDegPerSec2);
                    MotionPlannerJerkLimit = Mathf.Min(MotionPlannerJerkLimit, AllAltitudePrecisionPlannerJerkDegPerSec3);
                }
                // Use stronger deceleration only when the planned rate is already moving
                // toward target faster than the stopping envelope permits. This is not
                // reverse position chasing: it only removes existing same-direction momentum.
                bool sameDirectionMotion = Mathf.Abs(transitionShapedRate) > 0.001f
                    && Mathf.Sign(transitionShapedRate) == Mathf.Sign(BankError);
                bool exceedsStoppingEnvelope = sameDirectionMotion
                    && Mathf.Abs(transitionShapedRate) > rateMagnitude + 0.05f;
                float brakeAccel = Mathf.Max(scheduledBankDecel * 1.35f, MotionPlannerAccelLimit);
                TrajectoryBrakeEnvelopeActive = exceedsStoppingEnvelope;
                TrajectoryBrakeAccelLimit = exceedsStoppingEnvelope ? brakeAccel : MotionPlannerAccelLimit;
                if (exceedsStoppingEnvelope)
                {
                    MotionPlannerAccelLimit = brakeAccel;
                    // Jerk must also permit the planner to actually enter deceleration
                    // before the remaining bank angle is consumed.
                    MotionPlannerJerkLimit = Mathf.Max(MotionPlannerJerkLimit, brakeAccel * 3.0f);
                }
                TransitionRateAccelLimit = MotionPlannerAccelLimit;

                transitionReverseCandidateSince = 0f;
                TransitionZeroCaptureActive = false;
                TransitionZeroCaptureRate = 0f;
                {
                    float desiredAccel = Mathf.Clamp((desiredRate - transitionShapedRate) / Mathf.Max(0.0001f, dtControl),
                        -MotionPlannerAccelLimit, MotionPlannerAccelLimit);
                    transitionShapedRateAccel = Mathf.MoveTowards(transitionShapedRateAccel, desiredAccel, MotionPlannerJerkLimit * dtControl);
                    transitionShapedRateAccel = Mathf.Clamp(transitionShapedRateAccel, -MotionPlannerAccelLimit, MotionPlannerAccelLimit);
                    float nextRate = transitionShapedRate + transitionShapedRateAccel * dtControl;
                    if ((desiredRate - transitionShapedRate) != 0f && Mathf.Sign(desiredRate - transitionShapedRate) != Mathf.Sign(desiredRate - nextRate))
                    {
                        nextRate = desiredRate;
                        transitionShapedRateAccel = 0f;
                    }
                    transitionShapedRate = nextRate;

                    // v0.4.85: actual-rate recovery.
                    // A planned rate is a feed-forward trajectory, not a substitute for
                    // measured motion.  Near the target, if the aircraft is already
                    // rotating AWAY from the target and the planner is still carrying a
                    // rate in that same away direction, fade that stale rate toward zero.
                    // This does not issue reverse position chase; it only removes a
                    // contradictory planner residue so the ordinary rate loop can arrest
                    // the measured motion before it grows into the next oscillation.
                    float recoveryBand = Mathf.Max(0.65f, TrajectoryHoldExitBandDeg * 2.0f);
                    bool actualMovingAway = absError <= recoveryBand
                        && Mathf.Abs(ActualRollRate) >= 0.55f
                        && Mathf.Sign(ActualRollRate) != Mathf.Sign(BankError);
                    bool stalePlannerSameAsActual = actualMovingAway
                        && Mathf.Abs(transitionShapedRate) >= 0.08f
                        && Mathf.Sign(transitionShapedRate) == Mathf.Sign(ActualRollRate);
                    if (stalePlannerSameAsActual)
                    {
                        if (trajectoryRateRecoverySince <= 0f) trajectoryRateRecoverySince = now;
                        float recoveryRelease = Mathf.Max(scheduledBankDecel * 1.10f, MotionPlannerAccelLimit * 1.25f);
                        transitionShapedRate = Mathf.MoveTowards(transitionShapedRate, 0f, recoveryRelease * dtControl);
                        transitionShapedRateAccel = 0f;
                        TrajectoryRateRecoveryActive = true;
                        TrajectoryRateRecoveryElapsed = Mathf.Max(0f, now - trajectoryRateRecoverySince);
                        TrajectoryRecoveryReleaseRate = recoveryRelease;
                    }
                    else
                    {
                        trajectoryRateRecoverySince = 0f;
                    }
                }
            }
            else
            {
                transitionShapedRate = desiredRate;
                transitionShapedRateAccel = 0f;
                transitionReverseCandidateSince = 0f;
                TransitionRateAccelLimit = 0f;
                TransitionZeroCaptureActive = false;
                TransitionZeroCaptureRate = 0f;
                trajectoryRateRecoverySince = 0f;
                TrajectoryRateRecoveryActive = false;
                TrajectoryRateRecoveryElapsed = 0f;
                TrajectoryRecoveryReleaseRate = 0f;
                MotionPlannerAccelLimit = 0f;
                MotionPlannerJerkLimit = 0f;
            }
            MotionPlannerPlannedRate = transitionShapedRate;
            MotionPlannerPlannedAccel = transitionShapedRateAccel;
            TransitionShapedRateRequest = transitionShapedRate;
            RollRateRequest = transitionShapedRate;

            // v0.4.39: SETTLE is intentionally passive.  The outer loop has already
            // asked for zero roll rate; do not turn small measured rate reversals into
            // new counter-commands.  AA normal FBW still receives the neutral virtual
            // stick and stabilizes the aircraft internally.
            float effectiveRollRate = ActualRollRate;
            bool passiveSettle = false;
            TransitionRateFeedbackDeadbandActive = passiveSettle;
            TransitionRateFeedbackDeadbandDegPerSec = passiveSettle ? Mathf.Abs(ActualRollRate) : 0f;
            if (passiveSettle) effectiveRollRate = 0f;
            EffectiveRollRateForControl = effectiveRollRate;

            float rateError = transitionShapedRate - effectiveRollRate;
            TrajectoryRateError = rateError;
            float scheduledMaxVirtualRoll = MaxVirtualRoll * DynamicPressureStickScale;
            float targetStick = Mathf.Clamp(rateError * RateLoopGain + precisionTrim, -scheduledMaxVirtualRoll, scheduledMaxVirtualRoll);
            SettleRateOnlyDampingActive = false;
            SettleRateOnlyDampingCommand = 0f;
            if (passiveSettle)
            {
                // v0.4.76: do not revive position chasing in SETTLE.  A very small
                // opposite-stick request is permitted only for a residual rate clearly
                // above the noise floor, and is bounded well below normal capture authority.
                rateError = 0f;
                float absRate = Mathf.Abs(ActualRollRate);
                if (absRate >= SettleRateOnlyDampingMinRateDegPerSec && Mathf.Abs(BankError) <= SettleBrakeReentryBandDeg)
                {
                    SettleRateOnlyDampingCommand = Mathf.Clamp(-ActualRollRate * SettleRateOnlyDampingGain,
                                                               -SettleRateOnlyDampingLimit,
                                                               SettleRateOnlyDampingLimit);
                    targetStick = SettleRateOnlyDampingCommand;
                    SettleRateOnlyDampingActive = Mathf.Abs(targetStick) > 0.0001f;
                }
                else
                {
                    targetStick = 0f;
                }
            }
            // HOLD has no position-command residue; only bounded rate damping may remain.
            if (CapturePhase == "Hold") targetStick = 0f;

            // v0.4.31 terminal chatter suppression:
            // Near target, do not let a small, one-frame rate-noise reversal immediately
            // reverse the virtual stick. During the short lock we decay toward neutral;
            // a persistent or larger correction is still allowed after the lock expires.
            // v0.4.81: active continuous Trim must remain continuous; only passive
            // phases use reverse suppression / attenuation.
            bool terminalPhase = CapturePhase == "Precision" || CapturePhase == "Hold";
            bool transitionPhase = CapturePhase == "Trajectory";
            TerminalChatterSuppressed = false;
            TerminalSlewScale = 1f;
            TerminalCommandLockRemaining = 0f;
            TransitionQuietingActive = false;
            TransitionSlewScale = 1f;
            TransitionCommandHoldRemaining = 0f;
            float speedMps = vessel != null && IsFinite((float)vessel.srfSpeed)
                ? Mathf.Max(0f, (float)vessel.srfSpeed) : 0f;
            if (terminalPhase)
            {
                TerminalSlewScale = TerminalSlewScaleForSpeed(speedMps);
                bool wantsReverse = VirtualPilotRoll != 0f && targetStick != 0f && Mathf.Sign(VirtualPilotRoll) != Mathf.Sign(targetStick);
                bool smallTerminalCommand = Mathf.Abs(BankError) <= TerminalChatterBandDeg && Mathf.Abs(targetStick) <= TerminalChatterStickBand;
                if (smallTerminalCommand && wantsReverse)
                {
                    terminalReverseLockUntil = Mathf.Max(terminalReverseLockUntil, now + TerminalReverseLockSeconds);
                    targetStick = 0f;
                    TerminalChatterSuppressed = true;
                }
                TerminalCommandLockRemaining = Mathf.Max(0f, terminalReverseLockUntil - now);
            }
            else terminalReverseLockUntil = 0f;

            // v0.4.81: terminal-only virtual-roll conditioner. v0.4.79 applied a
            // shared deadband/dwell to normal tracking and created bank-error lag.
            // Keep normal BANK/HDG tracking unfiltered; only soften tiny opposite
            // terminal corrections, and attenuate them instead of forcing neutral.
            RollCommandRawTarget = targetStick;
            RollCommandConditionerActive = false;
            RollCommandReversePending = false;
            RollCommandReverseDwellRemaining = 0f;
            rollCommandReverseCandidateSince = 0f;
            bool conditionerSmallTerminalReverse = terminalPhase &&
                VirtualPilotRoll != 0f && targetStick != 0f &&
                Mathf.Sign(VirtualPilotRoll) != Mathf.Sign(targetStick) &&
                Mathf.Abs(targetStick) <= RollCommandTerminalSmallReverseBand;
            if (conditionerSmallTerminalReverse)
            {
                targetStick *= RollCommandTerminalReverseAttenuation;
                RollCommandConditionerActive = true;
                RollCommandReversePending = true;
            }
            RollCommandConditionedTarget = targetStick;

            // v0.4.39 cleanup: BRAKE/SETTLE command cadence, delta suppression, and
            // counter-command holding are retired.  They created a stack of interacting
            // gates.  The continuous rate shaper is now the only transition conditioner.
            if (transitionPhase)
            {
                TransitionSlewScale = 1f;
                transitionHeldTarget = targetStick;
                TransitionHeldTarget = targetStick;
                TransitionCommandDelta = 0f;
                TransitionCommandDeadband = 0f;
                TransitionUpdateInterval = 0f;
                TransitionCommandUpdates1s = 0;
                TransitionUpdateGated = false;
                TransitionDeltaSuppressed = false;
                TransitionQuietingActive = false;
                TransitionCommandHoldRemaining = 0f;
                transitionCommandHoldUntil = 0f;
                transitionNextUpdateTime = 0f;
                transitionUpdateTimes.Clear();
            }
            else
            {
                transitionCommandHoldUntil = 0f;
                transitionNextUpdateTime = 0f;
                transitionHeldTarget = targetStick;
                transitionUpdateTimes.Clear();
                TransitionUpdateGated = false;
                TransitionUpdateInterval = 0f;
                TransitionHeldTarget = targetStick;
                TransitionCommandUpdates1s = 0;
                TransitionDeltaSuppressed = false;
                TransitionCommandDelta = 0f;
                TransitionCommandDeadband = 0f;
            }

            // v0.4.88 transport change: retain the former virtual-stick path only as a
            // shadow diagnostic, but do not let it command the aircraft. The planned rate
            // now goes directly to AA's existing RollAngularVelocityController entrance.
            float virtualRollBefore = VirtualPilotRoll;
            float allowedSlew = MaxStickSlewPerSec * DynamicPressureStickScale * TerminalSlewScale * TransitionSlewScale;
            VirtualPilotRoll = Mathf.MoveTowards(VirtualPilotRoll, targetStick, allowedSlew * dtControl);
            UpdateSmoothnessDiagnostics(now, dtControl, targetStick, rateError, virtualRollBefore);

            // BANK owns roll. Neutralizing the pilot roll channel is only the ownership gate
            // required so AA's native controller selects its external setpoint; it is not an
            // AERIS control-surface command and AA remains the sole final FlightCtrlState writer.
            ControlUtils.neutralize_user_input(state, ControlUtils.ROLL);
            AaNativeRollRateDemandDegPerSec = RollRateRequest;
            AaNativeRollRateDemandRadPerSec = RollRateRequest * Mathf.Deg2Rad;
            FbwRollDemand = AaNativeRollRateDemandRadPerSec;
            AaNativeRollRateOverrideActive = true;
            StandardFlyByWire.ExternalRollDemand = AaNativeRollRateDemandRadPerSec;
            StandardFlyByWire.ExternalRollOverride = true;

            // HDG yaw, when armed, uses its own AA-native yaw-rate transport before this
            // BANK call. BANK owns roll only and never writes a virtual rudder input.
            InjectedRoll = state.roll;
            ObserverStatePitchAfterAeris = state.pitch;
            ObserverStateRollAfterAeris = state.roll;
            ObserverStateYawAfterAeris = state.yaw;
            ObserverStateThrottleAfterAeris = state.mainThrottle;
            ControlState = "Active";

            if (Time.realtimeSinceStartup - lastTrace >= 0.5f)
            {
                lastTrace = Time.realtimeSinceStartup;
                AERISLogger.Info("[TRACE][BANK_VP] current=" + CurrentBank.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " target=" + TargetBank.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " err=" + BankError.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " desiredRate=" + RollRateRequest.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                    " qKpa=" + DynamicPressureKpa.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " qMode=" + DynamicPressureMode +
                    " qRateScale=" + DynamicPressureRateScale.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " limitedRate=" + LimitedRollRateRequest.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " actualRate=" + ActualRollRate.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " precisionTrim=" + precisionTrim.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " precisionTol=" + PrecisionToleranceDeg.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " precisionAltEligible=" + PrecisionAltitudeEligible +
                    " precisionTargetTol=" + PrecisionTargetToleranceDeg.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " precisionNeutral=" + PrecisionNeutralBandDeg.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " precisionRate=" + PrecisionRateCommandDegPerSec.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " precisionActive=" + PrecisionCorrectionActive +
                    " rawPilotRoll=" + RawPilotRoll.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " legacyVirtualStickShadow=" + VirtualPilotRoll.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " aaRateDemandDeg=" + AaNativeRollRateDemandDegPerSec.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " aaRateDemandRad=" + AaNativeRollRateDemandRadPerSec.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) +
                    " rollInputAfterNeutralize=" + InjectedRoll.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " phase=" + CapturePhase +
                    " terminalLatched=" + terminalLatched +
                    " state=" + ControlState +
                    " aaNativeRateOverride=" + AaNativeRollRateOverrideActive +
                    " directRollCommandInjection=false" +
                    " pilotRollBlocked=" + PilotRollBlocked +
                    " refName=" + TraceReferenceName +
                    " bankRef=" + TraceReferenceBank.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " bankVessel=" + TraceVesselTransformBank.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " bankUnprojected=" + TraceUnprojectedBank.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " bankSurfaceRight=" + TraceSurfaceRightBank.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " horizonWing=" + TraceHorizonWingBank.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " horizonValid=" + TraceHorizonWingValid +
                    " navballBank=" + TraceNavballBank.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " navballBankValid=" + TraceNavballBankValid +
                    " rawSigned=" + TraceRawSignedAngle.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " levelUpMag=" + TraceLevelUpMagnitude.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " aircraftUpMag=" + TraceAircraftUpMagnitude.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture) +
                    " refFwdAxisRate=" + TraceBodyAxisRollRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " refUpAxisRate=" + TraceReferenceUpAxisRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " refRightAxisRate=" + TraceReferenceRightAxisRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " vesselFacingAxisRate=" + TraceVesselFacingAxisRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " vesselForwardAxisRate=" + TraceVesselForwardAxisRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " vesselRightAxisRate=" + TraceVesselRightAxisRate.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " omegaMag=" + TraceAngularVelocityMagnitudeDegPerSec.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " navFound=" + TraceNavballFound +
                    " navSource=" + TraceNavballSource +
                    " navLocalRoll=" + TraceNavballLocalRollDeg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " navWorldRoll=" + TraceNavballWorldRollDeg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " navDelta=" + TraceNavballCandidateDeltaDeg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " refVsVesselFwd=" + TraceReferenceVsVesselForwardDeg.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " fwdRadialDot=" + TraceForwardVsRadialDot.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
            }
        }


        void ResetSmoothnessDiagnostics()
        {
            DiagnosticControlDt = 0f;
            DiagnosticTargetStick = 0f;
            DiagnosticRateError = 0f;
            DiagnosticVirtualRollDelta = 0f;
            DiagnosticVirtualRollSlewPerSec = 0f;
            DiagnosticCommandSign = 0;
            DiagnosticCommandSignFlips1s = 0;
            DiagnosticRateSignFlips1s = 0;
            DiagnosticErrorSignFlips1s = 0;
            DiagnosticStepScore = 0f;
            DiagnosticOscillationScore = 0f;
            DiagnosticSettleQuietElapsed = 0f;
            TerminalChatterSuppressed = false;
            TerminalSlewScale = 1f;
            TerminalCommandLockRemaining = 0f;
            TransitionQuietingActive = false;
            TransitionSlewScale = 1f;
            TransitionCommandHoldRemaining = 0f;
            diagnosticPreviousVirtualRoll = 0f;
            diagnosticPreviousCommandSign = 0;
            diagnosticPreviousRateSign = 0;
            diagnosticPreviousErrorSign = 0;
            diagnosticCommandFlipTimes.Clear();
            diagnosticRateFlipTimes.Clear();
            diagnosticErrorFlipTimes.Clear();
        }

        static int DiagnosticSign(float value, float deadband)
        {
            return value > deadband ? 1 : (value < -deadband ? -1 : 0);
        }

        static void PruneDiagnosticFlips(Queue<float> values, float now)
        {
            while (values.Count > 0 && now - values.Peek() > 1f) values.Dequeue();
        }

        void UpdateSmoothnessDiagnostics(float now, float dtControl, float targetStick, float rateError, float virtualRollBefore)
        {
            DiagnosticTargetStick = targetStick;
            DiagnosticRateError = rateError;
            DiagnosticVirtualRollDelta = VirtualPilotRoll - virtualRollBefore;
            DiagnosticVirtualRollSlewPerSec = DiagnosticVirtualRollDelta / Mathf.Max(0.001f, dtControl);
            DiagnosticCommandSign = DiagnosticSign(VirtualPilotRoll, 0.006f);
            int rateSign = DiagnosticSign(ActualRollRate, 0.10f);
            int errorSign = DiagnosticSign(BankError, 0.015f);

            if (DiagnosticCommandSign != 0 && diagnosticPreviousCommandSign != 0 && DiagnosticCommandSign != diagnosticPreviousCommandSign) diagnosticCommandFlipTimes.Enqueue(now);
            if (rateSign != 0 && diagnosticPreviousRateSign != 0 && rateSign != diagnosticPreviousRateSign) diagnosticRateFlipTimes.Enqueue(now);
            if (errorSign != 0 && diagnosticPreviousErrorSign != 0 && errorSign != diagnosticPreviousErrorSign) diagnosticErrorFlipTimes.Enqueue(now);
            if (DiagnosticCommandSign != 0) diagnosticPreviousCommandSign = DiagnosticCommandSign;
            if (rateSign != 0) diagnosticPreviousRateSign = rateSign;
            if (errorSign != 0) diagnosticPreviousErrorSign = errorSign;

            PruneDiagnosticFlips(diagnosticCommandFlipTimes, now);
            PruneDiagnosticFlips(diagnosticRateFlipTimes, now);
            PruneDiagnosticFlips(diagnosticErrorFlipTimes, now);
            DiagnosticCommandSignFlips1s = diagnosticCommandFlipTimes.Count;
            DiagnosticRateSignFlips1s = diagnosticRateFlipTimes.Count;
            DiagnosticErrorSignFlips1s = diagnosticErrorFlipTimes.Count;

            // A step is a visible discrete command jump, not normal bounded slew motion.
            DiagnosticStepScore = Mathf.Abs(DiagnosticVirtualRollDelta) >= 0.012f ? Mathf.Abs(DiagnosticVirtualRollDelta) : 0f;
            // Diagnostic-only composite: identifies repeated command/reaction reversals.
            DiagnosticOscillationScore = DiagnosticCommandSignFlips1s + DiagnosticRateSignFlips1s + (0.5f * DiagnosticErrorSignFlips1s);
            DiagnosticSettleQuietElapsed = settleQuietSince > 0f ? Mathf.Max(0f, now - settleQuietSince) : 0f;
            diagnosticPreviousVirtualRoll = VirtualPilotRoll;
        }

        float TerminalSlewScaleForSpeed(float speedMps)
        {
            if (speedMps <= TerminalSlewSpeedStartMps) return 1f;
            float span = Mathf.Max(1f, TerminalSlewSpeedFullMps - TerminalSlewSpeedStartMps);
            float t = Mathf.Clamp01((speedMps - TerminalSlewSpeedStartMps) / span);
            return Mathf.Lerp(1f, TerminalSlewMinimumScale, t);
        }

        void ResetHorizonBankRateEstimator()
        {
            previousHorizonBank = 0f;
            previousHorizonBankTime = 0f;
            havePreviousHorizonBank = false;
            filteredHorizonBankRate = 0f;
            horizonBankUnwrappedDeg = 0f;
            horizonBankRateSamples.Clear();
            HorizonBankRawRateDegPerSec = 0f;
            HorizonBankTrendRateDegPerSec = 0f;
            HorizonBankTrendResidualDeg = 0f;
            HorizonBankTrendSpanSeconds = 0f;
            HorizonBankTrendSampleCount = 0;
            ActualRollRate = 0f;
        }

        void UpdateHorizonBankRate(float now)
        {
            float rawRate = 0f;
            if (havePreviousHorizonBank)
            {
                float dt = now - previousHorizonBankTime;
                if (dt > 0.001f && dt < 0.25f)
                {
                    float delta = Mathf.DeltaAngle(previousHorizonBank, CurrentBank);
                    horizonBankUnwrappedDeg += delta;
                    rawRate = delta / dt;
                }
                else
                {
                    // A discontinuity (scene pause, rails, or delayed callback) must not
                    // create a synthetic rate. Re-anchor the causal trend at this sample.
                    horizonBankRateSamples.Clear();
                    horizonBankUnwrappedDeg = CurrentBank;
                }
            }
            else
            {
                horizonBankUnwrappedDeg = CurrentBank;
            }
            previousHorizonBank = CurrentBank;
            previousHorizonBankTime = now;
            havePreviousHorizonBank = true;
            HorizonBankRawRateDegPerSec = rawRate;

            horizonBankRateSamples.Enqueue(new HorizonBankRateSample {
                Time = now,
                UnwrappedBankDeg = horizonBankUnwrappedDeg
            });
            float window = Mathf.Max(0.05f, HorizonRateTrendWindowSeconds);
            while (horizonBankRateSamples.Count > 1 && now - horizonBankRateSamples.Peek().Time > window)
                horizonBankRateSamples.Dequeue();

            int count = horizonBankRateSamples.Count;
            float oldest = now;
            float sumT = 0f;
            float sumY = 0f;
            float sumTT = 0f;
            float sumTY = 0f;
            foreach (HorizonBankRateSample sample in horizonBankRateSamples)
            {
                float relativeTime = sample.Time - now;
                oldest = Mathf.Min(oldest, sample.Time);
                sumT += relativeTime;
                sumY += sample.UnwrappedBankDeg;
                sumTT += relativeTime * relativeTime;
                sumTY += relativeTime * sample.UnwrappedBankDeg;
            }
            float span = Mathf.Max(0f, now - oldest);
            float denominator = count * sumTT - sumT * sumT;
            float trendRate = 0f;
            bool trendReady = count >= Mathf.Max(2, HorizonRateTrendMinimumSamples)
                && span >= HorizonRateTrendMinimumSpanSeconds
                && denominator > 1e-6f;
            if (trendReady)
                trendRate = (count * sumTY - sumT * sumY) / denominator;

            float residual = 0f;
            if (trendReady)
            {
                float intercept = (sumY - trendRate * sumT) / count;
                foreach (HorizonBankRateSample sample in horizonBankRateSamples)
                {
                    float relativeTime = sample.Time - now;
                    float difference = sample.UnwrappedBankDeg - (intercept + trendRate * relativeTime);
                    residual += difference * difference;
                }
                residual = Mathf.Sqrt(residual / count);
            }

            HorizonBankTrendRateDegPerSec = trendRate;
            HorizonBankTrendResidualDeg = residual;
            HorizonBankTrendSpanSeconds = span;
            HorizonBankTrendSampleCount = count;
            // BANK control and Hold use the low-lag causal filtered derivative. The raw
            // one-frame rate and the 0.24 s least-squares trend are both retained solely
            // for recorder diagnosis and cross-checking.
            float alpha = 1f - Mathf.Exp(-2f * Mathf.PI * HorizonRateFilterHz * Mathf.Max(0.001f, Time.fixedDeltaTime));
            filteredHorizonBankRate += (rawRate - filteredHorizonBankRate) * Mathf.Clamp01(alpha);
            ActualRollRate = filteredHorizonBankRate;
        }

        // v0.3.15: attitude/reference observation must not depend on MASTER, BANK arm state,
        // or the availability of the control-writing path. This is strictly read-only.
        internal void ObserveVesselState(Vessel vessel, VirtualAttitudeInstrument attitude)
        {
            if (vessel == null || vessel.packed) return;
            formalAttitudeValid = attitude != null && attitude.InstrumentValid &&
                IsFinite(attitude.InstrumentBankWrappedDeg) &&
                IsFinite(attitude.InstrumentPitchDeg) &&
                IsFinite(attitude.InstrumentRollRateDegPerSec);
            if (formalAttitudeValid)
            {
                formalBankWrappedDeg = attitude.InstrumentBankWrappedDeg;
                formalHorizonBankValid = attitude.InstrumentHorizonBankValid &&
                    IsFinite(attitude.InstrumentHorizonBankDeg);
                formalHorizonBankDeg = attitude.InstrumentHorizonBankDeg;
                formalPitchDeg = attitude.InstrumentPitchDeg;
                formalRollRateDegPerSec = attitude.InstrumentRollRateDegPerSec;
            }
            else
            {
                formalHorizonBankValid = false;
                formalHorizonBankDeg = 0f;
                formalPitchDeg = 0f;
            }
            CurrentBank = ReadObservedBank(vessel);
            UpdateBankReferenceTrace(vessel);
            BankError = Mathf.DeltaAngle(CurrentBank, TargetBank);
            // The BANK controller derives its feedback rate from HorizonBankDeg in ApplyAaNativeRollRateDemand.
            // Preserve the quaternion roll-rate only as telemetry; it is not a horizon-bank derivative.
            ControlState = Armed ? "ShadowArmed" : "ShadowReady";
        }

        internal void Tick(object unusedManager, bool aerisMaster) { }

        void ClearPrecisionHoldTelemetry()
        {
            precisionWithinTargetSince = 0f;
            PrecisionAltitudeEligible = false;
            PrecisionAltitudeMeters = 0f;
            PrecisionHoldActive = false;
            PrecisionCorrectionActive = false;
            PrecisionWithinTarget = false;
            PrecisionWithinTargetElapsed = 0f;
            PrecisionTargetToleranceDeg = 0f;
            PrecisionNeutralBandDeg = 0f;
            PrecisionRateCommandDegPerSec = 0f;
            PrecisionRateLimitDegPerSec = 0f;
            PrecisionRateGainPerSec = 0f;
            PrecisionRateDamping = 0f;
        }

        void ClearAaNativeRollRateOverride()
        {
            StandardFlyByWire.ExternalRollOverride = false;
            StandardFlyByWire.ExternalRollDemand = 0f;
            AaNativeRollRateOverrideActive = false;
            AaNativeRollRateDemandDegPerSec = 0f;
            AaNativeRollRateDemandRadPerSec = 0f;
            FbwRollDemand = 0f;
        }

        void Release(string reason)
        {
            bool was = VirtualPilotRoll != 0f || AaNativeRollRateOverrideActive || ControlState != "Inactive";
            VirtualPilotRoll = 0f;
            RawPilotRoll = 0f;
            InjectedRoll = 0f;
            ClearAaNativeRollRateOverride();
            ObserverPilotPitch = ObserverPilotRoll = ObserverPilotYaw = ObserverPilotThrottle = 0f;
            ObserverStatePitchBefore = ObserverStateRollBefore = ObserverStateYawBefore = ObserverStateThrottleBefore = 0f;
            ObserverStatePitchAfterAeris = ObserverStateRollAfterAeris = ObserverStateYawAfterAeris = ObserverStateThrottleAfterAeris = 0f;
            ObserverManualRollActive = false;
            RollRateRequest = 0f;
            ActualRollRate = 0f;
            ResetHorizonBankRateEstimator();
            precisionTrim = 0f;
            ClearPrecisionHoldTelemetry();
            terminalReverseLockUntil = 0f;
            TerminalChatterSuppressed = false;
            TerminalSlewScale = 1f;
            TerminalCommandLockRemaining = 0f;
            lastControlTime = 0f;
            ControlState = "Inactive";
            CapturePhase = "Idle";
            if (was) AERISLogger.Info("[BANK] released reason=" + reason);
        }


        void UpdateBankReferenceTrace(Vessel vessel)
        {
            TraceReferenceBank = 0f;
            TraceVesselTransformBank = 0f;
            TraceUnprojectedBank = 0f;
            TraceSurfaceRightBank = 0f;
            TraceHorizonWingBank = 0f;
            TraceHorizonWingValid = false;
            TraceNavballBank = 0f;
            TraceNavballBankValid = false;
            TraceLegacySurfaceReferenceBank = 0f;
            TraceLegacySurfaceReferencePitch = 0f;
            TraceLegacySurfaceReferenceAttitudeValid = false;
            TraceRawSignedAngle = 0f;
            TraceLevelUpMagnitude = 0f;
            TraceAircraftUpMagnitude = 0f;
            TraceBodyAxisRollRate = 0f;
            TraceVesselFacingAxisRate = 0f;
            TraceVesselForwardAxisRate = 0f;
            TraceVesselRightAxisRate = 0f;
            TraceReferenceUpAxisRate = 0f;
            TraceReferenceRightAxisRate = 0f;
            TraceAngularVelocityMagnitudeDegPerSec = 0f;
            TraceReferenceVsVesselForwardDeg = 0f;
            TraceForwardVsRadialDot = 0f;
            TraceNavballFound = false;
            TraceNavballSource = "none";
            TraceNavballLocalRollDeg = 0f;
            TraceNavballWorldRollDeg = 0f;
            TraceNavballCandidateDeltaDeg = 0f;
            TraceReferenceName = "none";
            TraceRootPartName = "none";
            TraceRefForwardRadialDot = TraceRefUpRadialDot = TraceRefRightRadialDot = 0f;
            TraceVesselForwardRadialDot = TraceVesselUpRadialDot = TraceVesselRightRadialDot = 0f;
            TraceRootForwardRadialDot = TraceRootUpRadialDot = TraceRootRightRadialDot = 0f;
            TraceRefForwardSpeedDot = TraceRefUpSpeedDot = TraceRefRightSpeedDot = 0f;
            TraceVesselForwardSpeedDot = TraceVesselUpSpeedDot = TraceVesselRightSpeedDot = 0f;
            TraceRootForwardSpeedDot = TraceRootUpSpeedDot = TraceRootRightSpeedDot = 0f;
            if (vessel == null || vessel.mainBody == null) return;

            Transform reference = vessel.ReferenceTransform != null ? vessel.ReferenceTransform : vessel.transform;
            Transform vesselTransform = vessel.transform;
            TraceReferenceName = reference != null ? reference.name : "none";
            float rawSignedAngle;
            float levelUpMagnitude;
            float aircraftUpMagnitude;
            TraceReferenceBank = ReadSignedBankForTransform(vessel, reference, out rawSignedAngle, out levelUpMagnitude, out aircraftUpMagnitude);
            TraceRawSignedAngle = rawSignedAngle;
            TraceLevelUpMagnitude = levelUpMagnitude;
            TraceAircraftUpMagnitude = aircraftUpMagnitude;
            float ignoredRawSignedAngle;
            float ignoredLevelUpMagnitude;
            float ignoredAircraftUpMagnitude;
            TraceVesselTransformBank = ReadSignedBankForTransform(vessel, vesselTransform, out ignoredRawSignedAngle, out ignoredLevelUpMagnitude, out ignoredAircraftUpMagnitude);

            if (reference != null)
            {
                TraceForwardVsRadialDot = Vector3.Dot(reference.forward.normalized, (reference.position - vessel.mainBody.position).normalized);
                if (vesselTransform != null)
                    TraceReferenceVsVesselForwardDeg = Vector3.Angle(reference.forward, vesselTransform.forward);
                // Diagnostic only: compare every plausible craft axis against the causal H-BANK trend.
                // KSP's vessel.transform uses .up as the craft-facing axis for the AA/legacy surface-reference geometry.
                // These projections remain observation-only; BANK control uses the filtered H-BANK derivative.
                Vector3d omega = vessel.angularVelocity;
                TraceBodyAxisRollRate = (float)Vector3d.Dot(omega, (Vector3d)reference.forward) * Mathf.Rad2Deg;
                TraceReferenceUpAxisRate = (float)Vector3d.Dot(omega, (Vector3d)reference.up) * Mathf.Rad2Deg;
                TraceReferenceRightAxisRate = (float)Vector3d.Dot(omega, (Vector3d)reference.right) * Mathf.Rad2Deg;
                if (vesselTransform != null)
                {
                    TraceVesselFacingAxisRate = (float)Vector3d.Dot(omega, (Vector3d)vesselTransform.up) * Mathf.Rad2Deg;
                    TraceVesselForwardAxisRate = (float)Vector3d.Dot(omega, (Vector3d)vesselTransform.forward) * Mathf.Rad2Deg;
                    TraceVesselRightAxisRate = (float)Vector3d.Dot(omega, (Vector3d)vesselTransform.right) * Mathf.Rad2Deg;
                }
                TraceAngularVelocityMagnitudeDegPerSec = (float)omega.magnitude * Mathf.Rad2Deg;
            }

            if (reference != null)
            {
                Vector3 radialUp = (reference.position - vessel.mainBody.position).normalized;
                TraceUnprojectedBank = -Vector3.SignedAngle(radialUp, reference.up, reference.forward);
                TraceSurfaceRightBank = ReadSurfaceRightBank(vessel, reference);
                bool horizonWingValid;
                TraceHorizonWingBank = ReadHorizonWingBank(vessel, vesselTransform, out horizonWingValid);
                TraceHorizonWingValid = horizonWingValid;
                bool navballBankValid;
                TraceNavballBank = ReadNavballBankCandidate(vessel, vesselTransform, out navballBankValid);
                TraceNavballBankValid = navballBankValid;
                bool legacySurfaceAttitudeValid;
                float legacySurfacePitch;
                TraceLegacySurfaceReferenceBank = ReadLegacySurfaceReferenceBank(vessel, out legacySurfacePitch, out legacySurfaceAttitudeValid);
                TraceLegacySurfaceReferencePitch = legacySurfacePitch;
                TraceLegacySurfaceReferenceAttitudeValid = legacySurfaceAttitudeValid;
            }

            // Axis calibration is deliberately separate from BANK geometry and cannot affect control.
            Vector3 radialAnchorForAxes = vessel.rootPart != null ? vessel.rootPart.transform.position : vessel.transform.position;
            Vector3 radialUpForAxes = (radialAnchorForAxes - vessel.mainBody.position).normalized;
            Vector3 surfaceVelocityForAxes = vessel.srf_velocity.sqrMagnitude > 0.01 ? ((Vector3)vessel.srf_velocity).normalized : Vector3.zero;
            Transform rootTransform = vessel.rootPart != null ? vessel.rootPart.transform : null;
            TraceRootPartName = vessel.rootPart != null ? vessel.rootPart.partInfo.title : "none";
            if (reference != null)
            {
                TraceRefForwardRadialDot = Vector3.Dot(reference.forward, radialUpForAxes);
                TraceRefUpRadialDot = Vector3.Dot(reference.up, radialUpForAxes);
                TraceRefRightRadialDot = Vector3.Dot(reference.right, radialUpForAxes);
                TraceRefForwardSpeedDot = Vector3.Dot(reference.forward, surfaceVelocityForAxes);
                TraceRefUpSpeedDot = Vector3.Dot(reference.up, surfaceVelocityForAxes);
                TraceRefRightSpeedDot = Vector3.Dot(reference.right, surfaceVelocityForAxes);
            }
            if (vesselTransform != null)
            {
                TraceVesselForwardRadialDot = Vector3.Dot(vesselTransform.forward, radialUpForAxes);
                TraceVesselUpRadialDot = Vector3.Dot(vesselTransform.up, radialUpForAxes);
                TraceVesselRightRadialDot = Vector3.Dot(vesselTransform.right, radialUpForAxes);
                TraceVesselForwardSpeedDot = Vector3.Dot(vesselTransform.forward, surfaceVelocityForAxes);
                TraceVesselUpSpeedDot = Vector3.Dot(vesselTransform.up, surfaceVelocityForAxes);
                TraceVesselRightSpeedDot = Vector3.Dot(vesselTransform.right, surfaceVelocityForAxes);
            }
            if (rootTransform != null)
            {
                TraceRootForwardRadialDot = Vector3.Dot(rootTransform.forward, radialUpForAxes);
                TraceRootUpRadialDot = Vector3.Dot(rootTransform.up, radialUpForAxes);
                TraceRootRightRadialDot = Vector3.Dot(rootTransform.right, radialUpForAxes);
                TraceRootForwardSpeedDot = Vector3.Dot(rootTransform.forward, surfaceVelocityForAxes);
                TraceRootUpSpeedDot = Vector3.Dot(rootTransform.up, surfaceVelocityForAxes);
                TraceRootRightSpeedDot = Vector3.Dot(rootTransform.right, surfaceVelocityForAxes);
            }

            UpdateNavballReferenceTrace();
        }

        // Reflection-only Navball probe. This does not read or modify flight controls.
        void UpdateNavballReferenceTrace()
        {
            try
            {
                Type navType = typeof(FlightGlobals).Assembly.GetType("NavBall");
                if (navType == null) { TraceNavballSource = "NavBall:type-missing"; return; }
                object nav = FindNavballInstance(navType);
                if (nav == null) { TraceNavballSource = "NavBall:instance-missing"; return; }

                Transform tr = nav as Transform;
                Component component = nav as Component;
                if (tr == null && component != null) tr = component.transform;
                if (tr == null)
                {
                    FieldInfo tf = navType.GetField("transform", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    tr = tf != null ? tf.GetValue(nav) as Transform : null;
                }
                if (tr == null) { TraceNavballSource = "NavBall:transform-missing"; return; }

                TraceNavballFound = true;
                TraceNavballSource = navType.FullName + ":" + tr.name;
                TraceNavballLocalRollDeg = NormalizeSigned(tr.localEulerAngles.z);
                TraceNavballWorldRollDeg = NormalizeSigned(tr.eulerAngles.z);
                // The visible ball is normally inverse-oriented; keep both raw candidates and
                // report the closest signed candidate to AERIS bankRef for later validation.
                float direct = TraceNavballLocalRollDeg;
                float inverse = NormalizeSigned(-TraceNavballLocalRollDeg);
                float directDelta = Mathf.Abs(Mathf.DeltaAngle(TraceReferenceBank, direct));
                float inverseDelta = Mathf.Abs(Mathf.DeltaAngle(TraceReferenceBank, inverse));
                if (inverseDelta < directDelta)
                {
                    TraceNavballLocalRollDeg = inverse;
                    TraceNavballSource += ":local-inverse";
                    TraceNavballCandidateDeltaDeg = inverseDelta;
                }
                else
                {
                    TraceNavballSource += ":local-direct";
                    TraceNavballCandidateDeltaDeg = directDelta;
                }
            }
            catch (Exception ex)
            {
                TraceNavballFound = false;
                TraceNavballSource = "NavBall:error:" + ex.GetType().Name;
            }
        }

        static object FindNavballInstance(Type navType)
        {
            string[] names = { "fetch", "instance", "Instance", "navBall" };
            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo f = navType.GetField(names[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (f != null)
                {
                    object v = f.GetValue(null);
                    if (v != null) return v;
                }
                PropertyInfo p = navType.GetProperty(names[i], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null)
                {
                    object v = p.GetValue(null, null);
                    if (v != null) return v;
                }
            }
            return UnityEngine.Object.FindObjectOfType(navType);
        }


        // Player-visible horizon candidate: project the vessel right wing onto the local horizon
        // plane and measure its signed tilt against a level-right vector. Uses vessel.transform.up
        // as the craft-facing axis, matching the KSP craft convention observed in the trace.
        static float ReadHorizonWingBank(Vessel vessel, Transform craft, out bool valid)
        {
            valid = false;
            if (vessel == null || vessel.mainBody == null || craft == null) return 0f;
            Vector3 up = (craft.position - vessel.mainBody.position).normalized;
            Vector3 facing = Vector3.ProjectOnPlane(craft.up, up);
            if (facing.sqrMagnitude < 1e-6f) return 0f;
            facing.Normalize();
            Vector3 levelRight = Vector3.Cross(up, facing);
            if (levelRight.sqrMagnitude < 1e-6f) return 0f;
            levelRight.Normalize();
            Vector3 wingRight = Vector3.ProjectOnPlane(craft.right, facing);
            if (wingRight.sqrMagnitude < 1e-6f) return 0f;
            wingRight.Normalize();
            valid = true;
            return Vector3.SignedAngle(levelRight, wingRight, facing);
        }

        // v0.3.15: Local-horizon BANK candidate using the KSP craft-axis convention
        // established by v0.3.15 telemetry: craft.up = nose/longitudinal axis,
        // craft.right = right wing, craft.forward = underside. The returned sign is
        // right-positive, so a right-wing-down bank is positive.
        static float ReadNavballBankCandidate(Vessel vessel, Transform craft, out bool valid)
        {
            valid = false;
            if (vessel == null || vessel.mainBody == null || craft == null) return 0f;

            Vector3 radialUp = (craft.position - vessel.mainBody.position).normalized;
            Vector3 noseLevel = Vector3.ProjectOnPlane(craft.up, radialUp);
            if (noseLevel.sqrMagnitude < 1e-6f) return 0f;
            noseLevel.Normalize();

            Vector3 levelRight = Vector3.Cross(radialUp, noseLevel);
            if (levelRight.sqrMagnitude < 1e-6f) return 0f;
            levelRight.Normalize();

            Vector3 wingRight = Vector3.ProjectOnPlane(craft.right, noseLevel);
            if (wingRight.sqrMagnitude < 1e-6f) return 0f;
            wingRight.Normalize();

            valid = true;
            return -Vector3.SignedAngle(levelRight, wingRight, noseLevel);
        }

        // external legacy referenceAutoPilot VesselData.updateAttitude()-style geometry, adapted only to avoid
        // vessel.findWorldCenterOfMass() because that member is absent in this KSP 1.12 build.
        // Source: external legacy reference/external legacy referenceAutoPilot Source/VesselData.cs. No external legacy reference control code is used.
        static float ReadLegacySurfaceReferenceBank(Vessel vessel, out float pitchDeg, out bool valid)
        {
            pitchDeg = 0f;
            valid = false;
            if (vessel == null || vessel.mainBody == null || vessel.transform == null || vessel.ReferenceTransform == null) return 0f;
            Vector3 anchor = vessel.rootPart != null ? vessel.rootPart.transform.position : vessel.transform.position;
            Vector3 planetUp = (anchor - vessel.mainBody.position).normalized;
            Vector3 vesselFacingAxis = vessel.transform.up;
            Vector3 surfVesRight = Vector3.Cross(planetUp, vesselFacingAxis);
            if (planetUp.sqrMagnitude < 1e-6f || vesselFacingAxis.sqrMagnitude < 1e-6f || surfVesRight.sqrMagnitude < 1e-6f) return 0f;
            surfVesRight.Normalize();
            pitchDeg = 90f - Vector3.Angle(planetUp, vesselFacingAxis);
            float bank = Vector3.Angle(surfVesRight, vessel.ReferenceTransform.right) *
                         Mathf.Sign(Vector3.Dot(surfVesRight, -vessel.ReferenceTransform.forward));
            valid = !float.IsNaN(bank) && !float.IsInfinity(bank);
            return valid ? bank : 0f;
        }

        // Independent surface-right BANK observation, derived from a geometric reference:
        // planet-up x craft-facing gives the level right direction; compare it with the
        // reference transform's physical right axis and assign the sign about craft forward.
        // This is trace-only in v0.3.5: the active control reference remains ReadSignedBank.
        static float ReadSurfaceRightBank(Vessel vessel, Transform reference)
        {
            if (vessel == null || vessel.mainBody == null || reference == null) return 0f;
            Vector3 planetUp = (reference.position - vessel.mainBody.position).normalized;
            Vector3 craftFacing = vessel.transform != null ? vessel.transform.up : reference.forward;
            Vector3 surfaceRight = Vector3.Cross(planetUp, craftFacing);
            if (surfaceRight.sqrMagnitude < 1e-6f) return 0f;
            surfaceRight.Normalize();
            float magnitude = Vector3.Angle(surfaceRight, reference.right);
            float signAxis = Vector3.Dot(surfaceRight, -reference.forward);
            if (Mathf.Abs(signAxis) < 1e-6f) return 0f;
            return magnitude * Mathf.Sign(signAxis);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float NormalizeSigned(float angleDeg)
        {
            return Mathf.Repeat(angleDeg + 180f, 360f) - 180f;
        }

        static float ReadSignedBankForTransform(Vessel vessel, Transform t, out float rawSigned, out float levelMag, out float aircraftMag)
        {
            rawSigned = 0f;
            levelMag = 0f;
            aircraftMag = 0f;
            if (vessel == null || vessel.mainBody == null || t == null) return 0f;
            Vector3 forward = t.forward;
            Vector3 radialUp = (t.position - vessel.mainBody.position).normalized;
            Vector3 levelUp = Vector3.ProjectOnPlane(radialUp, forward);
            Vector3 aircraftUp = Vector3.ProjectOnPlane(t.up, forward);
            levelMag = levelUp.magnitude;
            aircraftMag = aircraftUp.magnitude;
            if (levelUp.sqrMagnitude < 1e-6f || aircraftUp.sqrMagnitude < 1e-6f) return 0f;
            rawSigned = Vector3.SignedAngle(levelUp.normalized, aircraftUp.normalized, forward.normalized);
            return -rawSigned;
        }

        float ReadObservedBank(Vessel vessel)
        {
            // Control closes around instantaneous local-horizon bank, not the accumulated roll tracker.
            if (formalAttitudeValid && formalHorizonBankValid) return formalHorizonBankDeg;
            return ReadSignedBank(vessel);
        }

        static float ReadSignedBank(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null) return 0f;
            Transform t = vessel.ReferenceTransform != null ? vessel.ReferenceTransform : vessel.transform;
            float rawSigned;
            float levelMagnitude;
            float aircraftMagnitude;
            return ReadSignedBankForTransform(vessel, t, out rawSigned, out levelMagnitude, out aircraftMagnitude); // AERIS: right wing down is positive.
        }
    }
}
