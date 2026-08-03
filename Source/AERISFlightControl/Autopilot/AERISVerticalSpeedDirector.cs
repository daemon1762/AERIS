using UnityEngine;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Autopilot
{
    // V/S is a trajectory-first vertical director. It turns a requested climb/descent
    // rate into a continuous pitch-attitude trajectory, then directly tracks that
    // trajectory as a planned pitch angular rate through AA's native controller.
    // The V/S director owns both stages; it never calls PITCH.SetDirectedTarget.
    internal sealed class AERISVerticalSpeedDirector
    {
        internal bool Armed { get; private set; }
        internal float TargetVerticalSpeedMps { get; private set; }
        internal string TargetVerticalSpeedText = "0";
        internal float CurrentVerticalSpeedMps { get; private set; }
        internal float VerticalSpeedErrorMps { get; private set; }
        internal float GeneratedPitchTargetDeg { get; private set; }
        internal float PlannedPitchRateDegPerSec { get; private set; }
        internal float VsRateProportionalDegPerSec { get; private set; }
        internal float VsRateDampingDegPerSec { get; private set; }
        internal float VsRateBrakeDegPerSec { get; private set; }
        internal float VsBasePitchHoldRateDegPerSec { get; private set; }
        internal float VsAttitudeErrorDeg { get; private set; }
        internal float VsAttitudeRateProportionalDegPerSec { get; private set; }
        internal float VsAttitudeRateDampingDegPerSec { get; private set; }
        internal float VsRateTargetDegPerSec { get; private set; }
        internal float VsRateCommandSlewDegPerSec2 { get; private set; }
        internal string DirectRateScheme { get; private set; } = "Inactive";
        internal bool DirectPitchRateActive { get; private set; }
        internal string ControlState { get; private set; } = "Inactive";

        // v0.4.97: explicitly distinguish an armed/prepared V/S target from an active
        // atmospheric control sample.  This is observation-only; it makes preflight logs
        // unambiguous without changing the director target or control law.
        internal bool ControlActive { get; private set; }
        internal bool VerticalSpeedErrorValid { get; private set; }
        internal float RequestedTargetVerticalSpeedMps { get { return TargetVerticalSpeedMps; } }
        internal float EffectiveTargetVerticalSpeedMps { get; private set; }

        // ALT owns an independent, continuous V/S demand.  It never overwrites the user's
        // stored V/S target or text field; V/S simply selects this value as its effective
        // target while ALT is armed.  This makes ALT -> V/S equivalent to HDG -> BANK.
        internal bool AltitudeRateDemandActive { get; private set; }
        internal float AltitudeRateDemandMps { get; private set; }

        // ALT terminal hold can request V/S values below the normal manual V/S deadband.
        // Mark that source explicitly so V/S can track a continuous low-rate altitude
        // trim without treating each tiny sign change as a new manual target.
        internal bool AltitudePrecisionHoldActive { get; private set; }
        internal float EffectiveVerticalSpeedDeadbandMps { get; private set; }
        internal float AltitudePrecisionLowRateDeadbandMps = 0.008f;
        // A genuine V/S overshoot must be materially larger than sensor/quantization noise
        // before it resets the V/S equilibrium state.
        internal float ErrorReversalBandMps = 0.12f;

        // v0.5.4: ALT's final sub-metre trim is a genuine small V/S tracking task.
        // Do not force it into the gentle equilibrium-only profile while its requested
        // V/S is still not being achieved.  This bounded track profile is stronger than
        // hold but gentler than a full V/S recapture, and it remains active only outside
        // a small V/S error band.
        internal bool AltitudePrecisionTrackingActive { get; private set; }
        // v0.8.31: separate entry/exit bands and short dwells prevent the low-q ALT
        // endpoint from switching between Track and Hold on every tiny V/S crossing.
        // The wider entry band also keeps the stronger tracking profile asleep until
        // the residual is real; the tighter exit band confirms a calm handoff.
        internal float AltitudePrecisionTrackingEnterBandMps = 0.080f;
        internal float AltitudePrecisionTrackingExitBandMps = 0.040f;
        internal float AltitudePrecisionTrackingEnterDwellSeconds = 0.25f;
        internal float AltitudePrecisionTrackingExitDwellSeconds = 0.35f;
        internal float AltitudePrecisionTrackingEnterElapsedSeconds { get; private set; }
        internal float AltitudePrecisionTrackingExitElapsedSeconds { get; private set; }
        internal float AltitudePrecisionTrackingRateGainDegPerMpsSec = 0.82f;
        internal float AltitudePrecisionTrackingAccelerationDampingDegPerMps2Sec = 0.38f;
        internal float AltitudePrecisionTrackingRateLimitDegPerSec = 0.24f;

        // v0.8.31: the 30 km hold showed a phase-locked ~0.10 Hz BasePitch limit
        // cycle.  Quiet only the low-q ALT precision learner: reduce its rate ceiling
        // to about 68% and add acceleration damping inside the terminal corridor.
        // v0.8.32: the first flight showed that keeping the 0.68 ceiling all the way
        // to the existing 0.35 m/s precision-retention boundary made the controller
        // fall through Track -> MainTrajectory -> Hold.  Recover rate authority
        // continuously from 0.18 to 0.30 m/s, before that boundary, while retaining
        // the extra damping.  Takeoff, main capture and manual V/S remain unchanged.
        internal bool AltitudeLowQPrecisionQuietingActive { get; private set; }
        internal float AltitudeLowQPrecisionQuietingBlend { get; private set; }
        internal float AltitudeLowQPrecisionRateAuthorityRecoveryBlend { get; private set; }
        internal float AltitudeLowQPrecisionQuietingRateScale { get; private set; }
        internal float AltitudeLowQPrecisionQuietingDampingScale { get; private set; }
        internal float AltitudeLowQPrecisionEffectiveRateLimitDegPerSec { get; private set; }
        internal float AltitudeLowQPrecisionQuietingRateScaleTarget = 0.68f;
        internal float AltitudeLowQPrecisionQuietingDampingScaleTarget = 1.35f;
        internal float AltitudeLowQPrecisionRateRecoveryStartErrorMps = 0.18f;
        internal float AltitudeLowQPrecisionRateRecoveryFullErrorMps = 0.30f;
        internal float AltitudeLowQPrecisionQuietingAttackSeconds = 0.30f;
        internal float AltitudeLowQPrecisionQuietingReleaseSeconds = 0.60f;

        // v0.8.34: retain the flight-proven useful part of AA Cruise without activating its controller.
        // In the low-q precision corridor, form a continuous desired vertical acceleration
        // from the live V/S residual, compare it with measured vertical acceleration, and
        // use that acceleration residual to guide the existing BasePitch learner.  This
        // starts easing before the V/S error crosses zero and remains continuous across the
        // legacy Track/Hold/Main phase boundaries.  AA StandardFlyByWire remains the sole
        // final-axis controller.  v0.8.34 makes the flight-proven guide part of the fixed
        // V/S law; there is no runtime branch back to the v0.8.32 BasePitch-only law.
        internal bool VsCruiseAccelerationGuideActive { get; private set; }
        internal float VsCruiseAccelerationGuideBlend { get; private set; }
        internal float VsCruiseDesiredVerticalAccelerationMps2 { get; private set; }
        internal float VsCruiseAccelerationErrorMps2 { get; private set; }
        internal float VsCruiseBasePitchRateCommandDegPerSec { get; private set; }
        internal float VsCruiseLegacyBasePitchRateDegPerSec { get; private set; }
        internal float VsCruiseAppliedBasePitchRateDegPerSec { get; private set; }
        internal bool VsCruisePreBrakeActive { get; private set; }
        internal float VsCruiseAccelerationRelaxationGainPerSec = 0.35f;
        internal float VsCruiseAccelerationErrorRateGainDegPerMps2Sec = 0.80f;
        internal float VsCruiseDesiredAccelerationLimitMps2 = 0.20f;
        internal float VsCruiseBasePitchRateLimitDegPerSec = 0.12f;
        internal float VsCruiseBlendFullErrorMps = 0.30f;
        internal float VsCruiseBlendReleaseErrorMps = 0.55f;
        internal float VsCruiseBlendAttackSeconds = 0.35f;
        internal float VsCruiseBlendReleaseSeconds = 0.50f;

        // v0.4.99: ALT has its own pitch envelope (retained in v0.5.0).  It is a second safety cap used only
        // while ALT owns the external V/S demand; it never overwrites the manual V/S
        // max-pitch setting.  The effective V/S cap is the lower of the two limits.
        internal bool AltitudePitchLimitActive { get; private set; }
        internal float AltitudePitchLimitDeg { get; private set; }
        internal float EffectiveMaxPitchTargetDeg
        {
            get
            {
                float configuredLimit = AltitudeRateDemandActive && AltitudePitchLimitActive
                    ? Mathf.Min(MaxPitchTargetDeg, AltitudePitchLimitDeg)
                    : MaxPitchTargetDeg;
                // v0.7.6: at low dynamic pressure, a large V/S target must not retain the
                // sea-level ±pitch envelope.  This is an upstream V/S trajectory envelope,
                // not a final AA/control-surface limiter.
                return LowQVerticalEnvelopeActive
                    ? Mathf.Min(configuredLimit, LowQEffectiveMaxPitchTargetDeg)
                    : configuredLimit;
            }
        }

        // v0.4.64: V/S is a damped outer loop. It builds a calm continuous pitch
        // trajectory rather than integrating every residual directly into a command.
        // v0.4.94 tracks that trajectory inside V/S as a native pitch-rate demand;
        // AA remains the final actuator/controller.
        internal float VerticalSpeedGainDegPerMps = 0.92f;
        internal float VerticalAccelerationDampingDegPerMps2 = 0.78f;
        internal float VerticalSpeedTrimGainDegPerMpsSec = 0.16f;

        // v0.6.2: Data from the Phase 2 high-q run isolated one repeating pitch oscillation
        // to manual V/S=0 m/s holds above roughly 60 kPa. The core issue was not AA or the
        // PITCH transport: the measured vertical-acceleration damping term could exceed the
        // V/S proportional term, then every small V/S sign crossing reset the V/S equilibrium.
        // Apply a continuous, very narrow profile only in that regime. ALT demand, non-zero V/S,
        // PITCH, BANK and HDG retain their established laws.
        internal float DynamicPressureKpa { get; private set; }
        internal float DynamicPressureHighQSchedule { get; private set; }
        internal string DynamicPressureMode { get; private set; } = "MID_Q";
        internal bool HighQManualZeroVsProfileActive { get; private set; }
        internal float HighQManualZeroVsBlend { get; private set; }
        // v0.6.3: a lighter capture-stage damping guard bridges large zero-V/S captures
        // before the final quiet profile reaches its narrow error corridor.
        internal bool HighQManualZeroVsCaptureGuardActive { get; private set; }
        internal float HighQManualZeroVsCaptureGuardBlend { get; private set; }
        // v0.6.6: when the pilot explicitly changes a non-zero manual V/S target to zero,
        // begin a short D-term guard immediately. The older capture guard only engaged once
        // the residual was already below its error corridor, which left the first high-q
        // deceleration burst unprotected.
        internal bool ManualZeroVsTransitionGuardActive { get; private set; }
        internal float ManualZeroVsTransitionGuardBlend { get; private set; }
        internal float ManualZeroVsTransitionGuardRemainingSeconds { get; private set; }
        internal float ManualZeroVsTransitionGuardFromMps { get; private set; }
        internal float ManualZeroVsTransitionGuardPressureBlend { get; private set; }

        // v0.6.8: a large manual non-zero -> zero V/S request is not an instantaneous
        // pitch reversal.  It is a planned vertical-speed deceleration trajectory.  The
        // pilot-facing effective target remains zero, while the outer loop follows this
        // jerk-limited intermediate V/S reference until it reaches the normal zero-V/S
        // capture corridor.  ALT and every other AP mode are deliberately excluded.
        internal bool ManualZeroVsTrajectoryBrakeActive { get; private set; }
        internal float ManualZeroVsTrajectoryTargetMps { get; private set; }
        internal float ManualZeroVsTrajectoryControlErrorMps { get; private set; }
        internal float ManualZeroVsTrajectoryScheduledDecelMps2 { get; private set; }
        internal float ManualZeroVsTrajectoryAppliedDecelMps2 { get; private set; }
        internal float ManualZeroVsTrajectoryPressureBlend { get; private set; }
        internal float ManualZeroVsTrajectoryInitialMps { get; private set; }
        internal float ManualZeroVsTrajectoryElapsedSeconds { get; private set; }
        internal string ManualZeroVsTrajectoryState { get; private set; } = "Inactive";
        internal float ControlTargetVerticalSpeedMps { get; private set; }
        internal float ControlVerticalSpeedErrorMps { get; private set; }

        // v0.6.7: the v0.6.2/v0.6.6 guards intentionally cover only manual V/S=0.
        // Phase-2 high-speed climb data showed a separate limit cycle while a manual non-zero
        // target was already inside PrecisionCapture: the short-horizon vertical-acceleration
        // signal drove more pitch than the residual V/S error and held the phase out of quiet
        // equilibrium.  Keep this profile strictly limited to manual, non-zero V/S precision
        // capture at high dynamic pressure. It does not apply to ALT, large captures, PITCH,
        // BANK, HDG, or zero-V/S holding.
        internal bool HighQNonZeroVsPrecisionCaptureProfileActive { get; private set; }
        internal float HighQNonZeroVsPrecisionCaptureBlend { get; private set; }
        internal float HighQNonZeroVsPrecisionFilteredAccelerationMps2 { get; private set; }
        internal float HighQNonZeroVsPrecisionDampingScale { get; private set; }
        internal float HighQNonZeroVsPrecisionDampingLimitDeg { get; private set; }
        internal float HighQNonZeroVsPrecisionBasePitchDampingScale { get; private set; }

        // v0.7.7: HHC-3B exposed a second high-q vertical limit cycle outside PrecisionCapture.
        // During a long ALT climb, actual V/S could already be close to the commanded non-zero
        // rate while the altitude target remained far away, so the phase stayed MainTrajectory.
        // The raw vertical-acceleration D term then alternated faster than the aircraft could
        // respond.  This is a control-only tracking corridor: retain BasePitch and the requested
        // non-zero V/S, but smooth and bound only the short-horizon correction path.
        internal bool HighQNonZeroVsTrackingProfileActive { get; private set; }
        internal float HighQNonZeroVsTrackingBlend { get; private set; }
        internal float HighQNonZeroVsTrackingFilteredAccelerationMps2 { get; private set; }
        internal float HighQNonZeroVsTrackingDampingScale { get; private set; }
        internal float HighQNonZeroVsTrackingDampingLimitDeg { get; private set; }
        internal float HighQNonZeroVsTrackingPitchSlewScale { get; private set; }
        internal float HighQNonZeroVsTrackingDirectRateScale { get; private set; }
        internal float HighQNonZeroVsTrackingRateCommandSlewScale { get; private set; }
        internal float HighQNonZeroVsTrackingBasePitchDampingScale { get; private set; }

        // v0.7.8: continuous mid-q bridge between the low- and high-q envelopes.
        internal bool MidQVerticalTrackingFilterActive { get; private set; }
        internal float MidQVerticalTrackingBlend { get; private set; }
        internal float MidQFilteredAccelerationMps2 { get; private set; }
        internal float MidQProportionalScale { get; private set; }
        internal float MidQDampingScale { get; private set; }
        internal float MidQDampingLimitDeg { get; private set; }
        internal float MidQPitchSlewScale { get; private set; }
        internal float MidQDirectRateScale { get; private set; }
        internal float MidQRateCommandSlewScale { get; private set; }
        internal float MidQBasePitchDampingScale { get; private set; }

        // v0.7.9: the individual low/mid/high-q filters reduced their local correction
        // terms, but HHC-3B exposed a remaining inner-loop failure: close V/S tracking
        // could still turn a few degrees of pitch-reference separation into ±15..20 deg/s
        // AA-native demands.  The crossing of PrecisionCapture/MainTrajectory and the
        // individual q bands then made the reduction appear and disappear mid-cycle.
        // This is one continuous, phase-independent near-target envelope. It never changes
        // the commanded V/S trajectory or BasePitch ownership; it only bounds the short
        // pitch-rate correction used to follow an already-near vertical-speed trajectory.
        internal bool VerticalTrackingRateEnvelopeActive { get; private set; }
        internal float VerticalTrackingRateEnvelopeBlend { get; private set; }
        internal float VerticalTrackingFilteredAccelerationMps2 { get; private set; }
        internal float VerticalTrackingPitchSlewScale { get; private set; }
        internal float VerticalTrackingAttitudeRateDampingScale { get; private set; }
        internal float VerticalTrackingRateLimitDegPerSec { get; private set; }
        internal float VerticalTrackingRateSlewDegPerSec2 { get; private set; }
        internal bool VerticalTrackingRateReversalGateActive { get; private set; }
        internal float VerticalTrackingDampingDominanceLimitDeg { get; private set; }

        // v0.7.6: the hypersonic 30 km test exposed the complementary low-q failure mode.
        // At q≈12.6 kPa, the normal ±20 deg V/S trajectory envelope and ±22 deg/s AA-native
        // pitch-rate demand were still available. ALT terminal capture therefore saturated,
        // reversed, and created high AoA as an effect, not a cause. This continuous envelope
        // reduces only V/S authority as q falls; PITCH manual, BANK, HDG, SPEED and AA FBW
        // remain untouched.
        internal bool LowQVerticalEnvelopeActive { get; private set; }
        internal float LowQVerticalEnvelopeBlend { get; private set; }
        internal float LowQFilteredAccelerationMps2 { get; private set; }
        internal float LowQEffectiveMaxPitchTargetDeg { get; private set; }
        internal float LowQProportionalScale { get; private set; }
        internal float LowQDampingScale { get; private set; }
        internal float LowQDampingLimitDeg { get; private set; }
        internal float LowQPitchSlewScale { get; private set; }
        internal float LowQDirectRateScale { get; private set; }
        internal float LowQRateCommandSlewScale { get; private set; }
        internal float LowQBasePitchAdaptScale { get; private set; }
        internal float LowQVerticalEnvelopeAppliedBlend { get; private set; }
        internal float LowQVerticalEnvelopeStartKpa = 24.0f;
        internal float LowQVerticalEnvelopeFullKpa = 12.0f;
        internal float LowQMinimumMaxPitchDeg = 6.0f;
        internal float LowQProportionalScaleTarget = 0.35f;
        internal float LowQDampingScaleTarget = 0.35f;
        internal float LowQDampingLimitDegTarget = 0.85f;
        internal float LowQPitchSlewScaleTarget = 0.25f;
        internal float LowQDirectRateScaleTarget = 0.25f;
        internal float LowQRateCommandSlewScaleTarget = 0.35f;
        internal float LowQBasePitchAdaptScaleTarget = 0.50f;
        internal float LowQAccelerationFilter = 0.055f;

        internal float EffectiveErrorReversalBandMps { get; private set; }
        internal float HighQProportionalScale { get; private set; }
        internal float HighQDampingScale { get; private set; }
        internal float HighQDampingLimitDeg { get; private set; }
        internal float HighQPitchSlewScale { get; private set; }
        internal float HighQAppliedPitchSlewDegPerSec { get; private set; }
        internal float DynamicPressureHighQStartKpa = 24.0f;
        internal float DynamicPressureHighQFullKpa = 60.0f;
        internal float HighQManualZeroVsEntryErrorMps = 2.50f;
        internal float HighQManualZeroVsExitErrorMps = 3.50f;
        internal float HighQManualZeroVsErrorReversalBandMps = 1.20f;
        internal float HighQManualZeroVsProportionalScale = 0.75f;
        internal float HighQManualZeroVsDampingScale = 0.40f;
        internal float HighQManualZeroVsDampingLimitDeg = 1.50f;
        internal float HighQManualZeroVsPitchSlewScale = 0.45f;
        internal float HighQManualZeroVsCaptureGuardExitErrorMps = 12.0f;
        internal float HighQManualZeroVsCaptureGuardDampingScale = 0.65f;
        internal float HighQManualZeroVsCaptureGuardDampingLimitDeg = 4.0f;
        internal float ManualZeroVsTransitionGuardMinimumPriorTargetMps = 5.0f;
        internal float ManualZeroVsTransitionGuardDurationSeconds = 3.0f;
        internal float ManualZeroVsTransitionGuardStartKpa = 18.0f;
        internal float ManualZeroVsTransitionGuardFullKpa = 36.0f;
        internal float ManualZeroVsTransitionGuardDampingScale = 0.55f;
        internal float ManualZeroVsTransitionGuardDampingLimitDeg = 3.0f;

        // v0.6.8 manual zero-V/S deceleration planner.  This is a target-rate trajectory
        // rather than another pitch controller: the existing V/S outer loop and AA native
        // pitch-rate transport remain the only control path.  The high-q schedule is only
        // used to select a conservative planned deceleration; it never reads AA state.
        internal float ManualZeroVsTrajectoryMinimumStartMps = 5.0f;
        internal float ManualZeroVsTrajectoryStartKpa = 12.0f;
        internal float ManualZeroVsTrajectoryFullKpa = 36.0f;
        internal float ManualZeroVsTrajectoryMinDecelMps2 = 3.5f;
        internal float ManualZeroVsTrajectoryMaxDecelMps2 = 7.5f;
        internal float ManualZeroVsTrajectoryMaxJerkMps3 = 14.0f;
        internal float ManualZeroVsTrajectoryInitialLeadMps = 0.50f;
        internal float ManualZeroVsTrajectoryCompletionBandMps = 0.05f;

        // Manual non-zero V/S, high-q, precision-capture stabilizer.  The entry/exit
        // bands are intentionally wider than the 0.05 m/s final hold tolerance so it
        // can prevent the high-frequency D-term loop before it becomes visible.
        internal float HighQNonZeroVsPrecisionCaptureEntryErrorMps = 0.90f;
        internal float HighQNonZeroVsPrecisionCaptureExitErrorMps = 1.40f;
        internal float HighQNonZeroVsPrecisionAccelerationFilter = 0.055f;
        internal float HighQNonZeroVsPrecisionDampingScaleTarget = 0.35f;
        internal float HighQNonZeroVsPrecisionDampingLimitDegTarget = 0.85f;
        internal float HighQNonZeroVsPrecisionBasePitchDampingScaleTarget = 0.35f;

        // High-q non-zero V/S tracking corridor.  It deliberately covers MainTrajectory only;
        // PrecisionCapture keeps the already-proven v0.6.7 profile and far-from-rate-target
        // captures retain full climb/descent authority.
        internal float HighQNonZeroVsTrackingEntryErrorMps = 1.20f;
        internal float HighQNonZeroVsTrackingExitErrorMps = 2.00f;
        internal float HighQNonZeroVsTrackingAccelerationFilter = 0.055f;
        internal float HighQNonZeroVsTrackingDampingScaleTarget = 0.35f;
        internal float HighQNonZeroVsTrackingDampingLimitDegTarget = 0.85f;
        internal float HighQNonZeroVsTrackingPitchSlewScaleTarget = 0.55f;
        internal float HighQNonZeroVsTrackingDirectRateScaleTarget = 0.65f;
        internal float HighQNonZeroVsTrackingRateCommandSlewScaleTarget = 0.45f;
        internal float HighQNonZeroVsTrackingBasePitchDampingScaleTarget = 0.35f;

        // Mid-q bridge: conservative, peak at 30 kPa, only close to a non-zero V/S target.
        internal float MidQVerticalTrackingStartKpa = 18.0f;
        internal float MidQVerticalTrackingPeakKpa = 30.0f;
        internal float MidQVerticalTrackingEndKpa = 42.0f;
        internal float MidQVerticalTrackingEntryErrorMps = 1.20f;
        internal float MidQVerticalTrackingExitErrorMps = 2.00f;
        internal float MidQVerticalTrackingAccelerationFilter = 0.075f;
        internal float MidQProportionalScaleTarget = 0.82f;
        internal float MidQDampingScaleTarget = 0.58f;
        internal float MidQDampingLimitDegTarget = 1.10f;
        internal float MidQPitchSlewScaleTarget = 0.72f;
        internal float MidQDirectRateScaleTarget = 0.78f;
        internal float MidQRateCommandSlewScaleTarget = 0.62f;
        internal float MidQBasePitchDampingScaleTarget = 0.60f;

        // v0.7.9 continuous near-target pitch-rate envelope. The limits are in the
        // published AA-native pitch-rate unit (deg/s), after the existing PITCH mapping.
        // They are fully released for a materially missed V/S target so capture authority
        // remains intact; at small residuals they prevent the inner attitude loop from
        // creating a fast sign-reversing rate demand.
        internal float VerticalTrackingEnvelopeEntryErrorMps = 0.55f;
        internal float VerticalTrackingEnvelopeExitErrorMps = 6.00f;
        internal float VerticalTrackingAccelerationFilter = 0.045f;
        internal float VerticalTrackingLowQRateLimitDegPerSec = 4.5f;
        internal float VerticalTrackingMidQRateLimitDegPerSec = 7.0f;
        internal float VerticalTrackingHighQRateLimitDegPerSec = 6.0f;
        internal float VerticalTrackingLowQRateSlewDegPerSec2 = 10.0f;
        internal float VerticalTrackingMidQRateSlewDegPerSec2 = 12.0f;
        internal float VerticalTrackingHighQRateSlewDegPerSec2 = 12.0f;
        internal float VerticalTrackingPitchSlewScaleTarget = 0.40f;
        internal float VerticalTrackingAttitudeRateDampingScaleTarget = 1.50f;
        internal float VerticalTrackingReversalSlewScaleTarget = 0.40f;
        internal float VerticalTrackingDampingDominancePScale = 0.75f;
        internal float VerticalTrackingDampingDominanceFloorDeg = 0.35f;

        internal float MaxVerticalSpeedTrimDeg = 0.75f;
        internal float VerticalSpeedHoldBandMps = 0.35f;
        internal float VerticalAccelerationFilter = 0.18f;
        internal float PitchTargetSlewDegPerSec = 6.4f;
        internal float VerticalSpeedDeadbandMps = 0.03f;

        // v0.4.94: V/S keeps its proven vertical-motion / BasePitch trajectory and
        // tracks the resulting pitch reference directly as an AA-native pitch-rate
        // command.  The mapping intentionally matches PITCH's validated outer-rate
        // law, but it remains inside V/S; there is no PITCH target handoff.
        internal float VsAttitudePitchErrorGain = 0.120f;
        internal float VsAttitudePitchRateDamping = 0.022f;
        internal float VsAttitudeMaxRateCommand = 0.55f;
        internal float VsAttitudeCommandSlewPerSec = 1.20f;

        // v0.4.74: a tiny sustained zero-V/S residual becomes altitude drift.
        // Let BasePitch absorb that residual in the narrow quiet band, without
        // reintroducing position tracking or changing PITCH / AA control.
        internal float ZeroVsTargetBandMps = 0.10f;
        internal float ZeroVsHoldAdaptErrorBandMps = 0.45f;
        internal float ZeroVsHoldAdaptAccelerationBandMps2 = 0.45f;
        internal float ZeroVsBasePitchAdaptGainDegPerMpsSec = 0.46f;
        internal float ZeroVsBasePitchAdaptContributionDeg { get; private set; }

        internal float VerticalSpeedTrimDeg { get; private set; }
        // v0.4.70: BasePitch is the slow, sustained pitch required to hold a non-zero V/S.
        // It is intentionally separate from the final capture trim.
        internal float VerticalSpeedBasePitchDeg { get; private set; }
        internal float BasePitchAdaptContributionDeg { get; private set; }
        internal float DesiredPitchBeforeClampDeg { get; private set; }
        internal float DesiredPitchAfterClampDeg { get; private set; }
        internal bool PitchTargetSaturated { get; private set; }
        internal bool PitchUpperSaturated { get; private set; }
        internal bool PitchLowerSaturated { get; private set; }
        // v0.4.71: BasePitch is the sustained V/S attitude, not a tiny end-trim.
        // Use high authority while the V/S error is large, then taper near capture.
        internal float VerticalSpeedBasePitchAdaptGainDegPerMpsSec = 0.12f;
        internal float VerticalSpeedBasePitchFastAdaptGainDegPerMpsSec = 0.34f;
        internal float BasePitchFastAdaptErrorMps = 3.0f;
        internal float BasePitchAdaptAccelerationSoftening = 0.35f;
        internal float MaxVerticalSpeedBasePitchDeg = 45f;
        internal float VerticalAccelerationMps2 { get; private set; }
        // v0.4.72/v0.4.95: fixed non-zero V/S needs less pitch as airspeed rises, and
        // more as it falls. This remains a slow feed-forward adaptation of BasePitch,
        // never an inner-loop controller. At a zero-V/S target it is deliberately OFF:
        // the equilibrium learner owns the final trim so speed feed-forward cannot oppose it.
        internal float SurfaceSpeedMps { get; private set; }
        internal float SurfaceSpeedRateMps2 { get; private set; }
        internal float BasePitchSpeedAdaptContributionDeg { get; private set; }
        // v0.4.95: true only while the non-zero V/S speed feed-forward is allowed to move BasePitch.
        // This exposes the zero-V/S isolation rule in FDR and prevents a hidden trim conflict.
        internal bool BasePitchSpeedAdaptActive { get; private set; }
        internal float BasePitchSpeedAdaptGainDegPerMps2Sec = 0.045f;
        internal float BasePitchSpeedRateFilter = 0.12f;
        private float previousSurfaceSpeedMps;
        private bool havePreviousSurfaceSpeed;
        private float previousVerticalSpeedMps;
        private bool havePreviousVerticalSpeed;
        private float previousEffectiveErrorMps;
        private bool havePreviousEffectiveError;
        // v0.9.3: one-shot guard for the first active frame after a confirmed
        // automatic or manual liftoff.  ALT may activate its V/S source on that same
        // frame; keep the trajectory anchored to the live attitude, not the stale
        // ground-armed PITCH observation.
        private bool postTakeoffTrajectorySeedPending;
        internal float PrecisionTrimContributionDeg { get; private set; }

        // v0.4.73: quiet the outer-loop pitch target only in the final V/S capture band.
        // This is upstream of PITCH/AA; it never filters final FlightCtrlState.pitch.
        internal bool TerminalQuietZoneActive { get; private set; }
        internal bool TerminalPitchTargetHeld { get; private set; }
        internal float TerminalQuietHeldPitchTargetDeg { get; private set; }
        internal float TerminalQuietTargetDeltaDeg { get; private set; }
        internal float TerminalQuietHoldBandDeg = 0.08f;
        internal float TerminalQuietErrorBandMps = 0.45f;
        internal float TerminalQuietAccelerationBandMps2 = 0.45f;
        internal float TerminalQuietMinHoldSeconds = 0.18f;
        // v0.4.75: terminal capture must remain continuous; hold/release caused small stair-step pitch updates.
        internal float TerminalQuietSlewDegPerSec = 1.15f;
        private float terminalQuietHoldUntil;

        // v0.4.67: predictive capture telemetry and braking configuration.  These remain
        // entirely in the V/S outer loop; PITCH and AA FBW retain their existing roles.
        internal float PredictedVerticalSpeedMps { get; private set; }
        internal float PredictedStopErrorMps { get; private set; }
        internal float BrakeContributionDeg { get; private set; }
        internal bool OvershootLatchActive { get; private set; }
        internal string BrakeState { get; private set; } = "Inactive";
        internal float ProportionalContributionDeg { get; private set; }
        internal float DampingContributionDeg { get; private set; }
        internal float RecoveryContributionDeg { get; private set; }
        internal float VerticalSpeedBrakePredictionSeconds = 0.35f;
        internal float VerticalSpeedBrakeGainDegPerMps = 0.18f;
        internal float OvershootRecoveryGainDegPerMps = 0.72f;
        internal float PrecisionPushGainDegPerMpsSec = 0.0f;
        internal float PrecisionPushMaxDeg = 0.0f;
        private float precisionPushDeg;
        private bool targetChangedSinceUpdate;
        private bool manualZeroVsTransitionGuardPending;
        private bool manualZeroVsTrajectoryBrakePending;

        // v0.4.94: after the main V/S trajectory has nearly captured, learn the
        // steady BasePitch needed for the current speed/thrust state.  This is a
        // slow equilibrium correction, not a second pitch-rate loop.  It runs only
        // in a calm low-acceleration corridor and transfers the former short-term
        // trim into BasePitch on entry so the commanded pitch remains continuous.
        internal bool PrecisionBasePitchActive { get; private set; }
        internal bool PrecisionWithinTarget { get; private set; }
        internal float PrecisionWithinTargetElapsedSeconds { get; private set; }
        internal float PrecisionBasePitchRateDegPerSec { get; private set; }
        internal float PrecisionBasePitchAdaptContributionDeg { get; private set; }
        internal float PrecisionTrimTransferDeg { get; private set; }
        // v0.5.1 precision target: tighten the final V/S equilibrium band without
        // changing the proven main trajectory or AA-native pitch-rate transport.
        internal float PrecisionTargetToleranceMps = 0.05f;
        internal float PrecisionNeutralBandMps = 0.015f;

        // v0.4.96: phase-separate terminal equilibrium learning.  Do not start the
        // long-lived BasePitch correction while the aircraft is still clearly carrying
        // vertical acceleration toward the target.  Once the plant has calmed, use a
        // stronger bounded capture rate outside the final tolerance, then fall back to
        // the quiet hold rate inside it.  This is symmetric for climbs and descents.
        internal float PrecisionEntryBandMps = 0.85f;
        internal float PrecisionEntryAccelerationBandMps2 = 0.30f;
        internal float PrecisionEntryDwellSeconds = 0.30f;
        internal float PrecisionCaptureRateGainDegPerMpsSec = 0.68f;
        internal float PrecisionCaptureAccelerationDampingDegPerMps2Sec = 0.65f;
        internal float PrecisionCaptureRateLimitDegPerSec = 0.42f;
        internal float PrecisionBasePitchRateGainDegPerMpsSec = 0.36f;
        internal float PrecisionBasePitchAccelerationDampingDegPerMps2Sec = 0.12f;
        internal float PrecisionBasePitchRateLimitDegPerSec = 0.30f;
        internal bool PrecisionCapturePhase { get; private set; }
        internal float PrecisionActiveRateGainDegPerMpsSec { get; private set; }
        internal float PrecisionActiveAccelerationDampingDegPerMps2Sec { get; private set; }
        internal float PrecisionActiveRateLimitDegPerSec { get; private set; }
        internal float BasePitchSpeedAdaptRateDegPerSec { get; private set; }
        internal float PrecisionNetBasePitchRateDegPerSec { get; private set; }

        void ResetHighQManualZeroVsTelemetry()
        {
            DynamicPressureKpa = 0f;
            DynamicPressureHighQSchedule = 0f;
            DynamicPressureMode = "MID_Q";
            HighQManualZeroVsProfileActive = false;
            HighQManualZeroVsBlend = 0f;
            HighQManualZeroVsCaptureGuardActive = false;
            HighQManualZeroVsCaptureGuardBlend = 0f;
            ManualZeroVsTransitionGuardActive = false;
            ManualZeroVsTransitionGuardBlend = 0f;
            ManualZeroVsTransitionGuardRemainingSeconds = 0f;
            ManualZeroVsTransitionGuardFromMps = 0f;
            ManualZeroVsTransitionGuardPressureBlend = 0f;
            ManualZeroVsTrajectoryBrakeActive = false;
            ManualZeroVsTrajectoryTargetMps = 0f;
            ManualZeroVsTrajectoryControlErrorMps = 0f;
            ManualZeroVsTrajectoryScheduledDecelMps2 = 0f;
            ManualZeroVsTrajectoryAppliedDecelMps2 = 0f;
            ManualZeroVsTrajectoryPressureBlend = 0f;
            ManualZeroVsTrajectoryInitialMps = 0f;
            ManualZeroVsTrajectoryElapsedSeconds = 0f;
            ManualZeroVsTrajectoryState = "Inactive";
            ControlTargetVerticalSpeedMps = 0f;
            ControlVerticalSpeedErrorMps = 0f;
            HighQNonZeroVsPrecisionCaptureProfileActive = false;
            HighQNonZeroVsPrecisionCaptureBlend = 0f;
            HighQNonZeroVsPrecisionFilteredAccelerationMps2 = 0f;
            HighQNonZeroVsPrecisionDampingScale = 1f;
            HighQNonZeroVsPrecisionDampingLimitDeg = 0f;
            HighQNonZeroVsPrecisionBasePitchDampingScale = 1f;
            HighQNonZeroVsTrackingProfileActive = false;
            HighQNonZeroVsTrackingBlend = 0f;
            HighQNonZeroVsTrackingFilteredAccelerationMps2 = 0f;
            HighQNonZeroVsTrackingDampingScale = 1f;
            HighQNonZeroVsTrackingDampingLimitDeg = 0f;
            HighQNonZeroVsTrackingPitchSlewScale = 1f;
            HighQNonZeroVsTrackingDirectRateScale = 1f;
            HighQNonZeroVsTrackingRateCommandSlewScale = 1f;
            HighQNonZeroVsTrackingBasePitchDampingScale = 1f;
            MidQVerticalTrackingFilterActive = false;
            MidQVerticalTrackingBlend = 0f;
            MidQFilteredAccelerationMps2 = 0f;
            MidQProportionalScale = 1f;
            MidQDampingScale = 1f;
            MidQDampingLimitDeg = 0f;
            MidQPitchSlewScale = 1f;
            MidQDirectRateScale = 1f;
            MidQRateCommandSlewScale = 1f;
            MidQBasePitchDampingScale = 1f;
            VerticalTrackingRateEnvelopeActive = false;
            VerticalTrackingRateEnvelopeBlend = 0f;
            VerticalTrackingFilteredAccelerationMps2 = 0f;
            VerticalTrackingPitchSlewScale = 1f;
            VerticalTrackingAttitudeRateDampingScale = 1f;
            VerticalTrackingRateLimitDegPerSec = 0f;
            VerticalTrackingRateSlewDegPerSec2 = 0f;
            VerticalTrackingRateReversalGateActive = false;
            VerticalTrackingDampingDominanceLimitDeg = 0f;
            LowQVerticalEnvelopeActive = false;
            LowQVerticalEnvelopeBlend = 0f;
            LowQVerticalEnvelopeAppliedBlend = 0f;
            LowQFilteredAccelerationMps2 = 0f;
            LowQEffectiveMaxPitchTargetDeg = MaxPitchTargetDeg;
            LowQProportionalScale = 1f;
            LowQDampingScale = 1f;
            LowQDampingLimitDeg = 0f;
            LowQPitchSlewScale = 1f;
            LowQDirectRateScale = 1f;
            LowQRateCommandSlewScale = 1f;
            LowQBasePitchAdaptScale = 1f;
            ResetAltitudeLowQPrecisionQuietingState();
            manualZeroVsTransitionGuardPending = false;
            manualZeroVsTrajectoryBrakePending = false;
            EffectiveErrorReversalBandMps = ErrorReversalBandMps;
            HighQProportionalScale = 1f;
            HighQDampingScale = 1f;
            HighQDampingLimitDeg = 0f;
            HighQPitchSlewScale = 1f;
            HighQAppliedPitchSlewDegPerSec = 0f;
        }

        // v0.7.9: pressure schedule for the single near-target rate envelope.
        // Low q needs a small rate because authority arrives late; mid q gets the largest
        // calm tracking rate; high q is reduced again because tail authority is strong.
        float GetVerticalTrackingRateLimitForPressure(float dynamicPressureKpa)
        {
            if (dynamicPressureKpa <= LowQVerticalEnvelopeFullKpa)
                return VerticalTrackingLowQRateLimitDegPerSec;
            if (dynamicPressureKpa < LowQVerticalEnvelopeStartKpa)
                return Mathf.Lerp(VerticalTrackingLowQRateLimitDegPerSec,
                    VerticalTrackingMidQRateLimitDegPerSec,
                    Mathf.Clamp01((dynamicPressureKpa - LowQVerticalEnvelopeFullKpa) /
                        Mathf.Max(0.01f, LowQVerticalEnvelopeStartKpa - LowQVerticalEnvelopeFullKpa)));
            if (dynamicPressureKpa < MidQVerticalTrackingEndKpa)
                return VerticalTrackingMidQRateLimitDegPerSec;
            if (dynamicPressureKpa < DynamicPressureHighQFullKpa)
                return Mathf.Lerp(VerticalTrackingMidQRateLimitDegPerSec,
                    VerticalTrackingHighQRateLimitDegPerSec,
                    Mathf.Clamp01((dynamicPressureKpa - MidQVerticalTrackingEndKpa) /
                        Mathf.Max(0.01f, DynamicPressureHighQFullKpa - MidQVerticalTrackingEndKpa)));
            return VerticalTrackingHighQRateLimitDegPerSec;
        }

        float GetVerticalTrackingRateSlewForPressure(float dynamicPressureKpa)
        {
            if (dynamicPressureKpa <= LowQVerticalEnvelopeFullKpa)
                return VerticalTrackingLowQRateSlewDegPerSec2;
            if (dynamicPressureKpa < LowQVerticalEnvelopeStartKpa)
                return Mathf.Lerp(VerticalTrackingLowQRateSlewDegPerSec2,
                    VerticalTrackingMidQRateSlewDegPerSec2,
                    Mathf.Clamp01((dynamicPressureKpa - LowQVerticalEnvelopeFullKpa) /
                        Mathf.Max(0.01f, LowQVerticalEnvelopeStartKpa - LowQVerticalEnvelopeFullKpa)));
            if (dynamicPressureKpa < MidQVerticalTrackingEndKpa)
                return VerticalTrackingMidQRateSlewDegPerSec2;
            if (dynamicPressureKpa < DynamicPressureHighQFullKpa)
                return Mathf.Lerp(VerticalTrackingMidQRateSlewDegPerSec2,
                    VerticalTrackingHighQRateSlewDegPerSec2,
                    Mathf.Clamp01((dynamicPressureKpa - MidQVerticalTrackingEndKpa) /
                        Mathf.Max(0.01f, DynamicPressureHighQFullKpa - MidQVerticalTrackingEndKpa)));
            return VerticalTrackingHighQRateSlewDegPerSec2;
        }

        void ResetManualZeroVsTrajectoryBrake(string state)
        {
            ManualZeroVsTrajectoryBrakeActive = false;
            ManualZeroVsTrajectoryTargetMps = 0f;
            ManualZeroVsTrajectoryControlErrorMps = 0f;
            ManualZeroVsTrajectoryScheduledDecelMps2 = 0f;
            ManualZeroVsTrajectoryAppliedDecelMps2 = 0f;
            ManualZeroVsTrajectoryPressureBlend = 0f;
            ManualZeroVsTrajectoryInitialMps = 0f;
            ManualZeroVsTrajectoryElapsedSeconds = 0f;
            ManualZeroVsTrajectoryState = state;
            manualZeroVsTrajectoryBrakePending = false;
        }

        // v0.4.97: precision capture/hold state hysteresis.  The control gains and
        // trajectory remain unchanged; these bands only prevent the diagnostic/control
        // phase from flipping on a single noisy sample at its boundary.
        internal float PrecisionExitBandMps = 1.10f;
        internal float PrecisionExitAccelerationBandMps2 = 0.50f;
        internal float PrecisionExitDwellSeconds = 0.45f;
        internal float PrecisionHoldExitToleranceMps = 0.08f;
        internal float PrecisionHoldExitAccelerationBandMps2 = 0.45f;
        internal float PrecisionHoldEntryDwellSeconds = 0.30f;
        internal float PrecisionHoldExitDwellSeconds = 0.35f;
        internal float PrecisionEntryElapsedSeconds { get { return precisionEntryElapsedSeconds; } }
        internal float PrecisionExitElapsedSeconds { get; private set; }
        internal float PrecisionHoldEntryElapsedSeconds { get; private set; }
        internal float PrecisionHoldExitElapsedSeconds { get; private set; }
        internal string PrecisionPhase { get; private set; } = "Inactive";
        internal int PrecisionPhaseTransitions { get; private set; }
        private float precisionEntryElapsedSeconds;

        // V/S safety envelope: absolute pitch-target limit. User-configurable in UI.
        // This limits the V/S-generated PITCH target only; it does not alter AA FBW.


        internal float MaxPitchTargetDeg = 20f;
        internal string MaxPitchTargetText = "20";

        void ResetAltitudePrecisionTrackingState()
        {
            AltitudePrecisionTrackingActive = false;
            AltitudePrecisionTrackingEnterElapsedSeconds = 0f;
            AltitudePrecisionTrackingExitElapsedSeconds = 0f;
        }

        void ResetAltitudeLowQPrecisionQuietingState()
        {
            AltitudeLowQPrecisionQuietingActive = false;
            AltitudeLowQPrecisionQuietingBlend = 0f;
            AltitudeLowQPrecisionRateAuthorityRecoveryBlend = 0f;
            AltitudeLowQPrecisionQuietingRateScale = 1f;
            AltitudeLowQPrecisionQuietingDampingScale = 1f;
            AltitudeLowQPrecisionEffectiveRateLimitDegPerSec = 0f;
        }

        void ResetVsCruiseAccelerationGuideState()
        {
            VsCruiseAccelerationGuideActive = false;
            VsCruiseAccelerationGuideBlend = 0f;
            VsCruiseDesiredVerticalAccelerationMps2 = 0f;
            VsCruiseAccelerationErrorMps2 = 0f;
            VsCruiseBasePitchRateCommandDegPerSec = 0f;
            VsCruiseLegacyBasePitchRateDegPerSec = 0f;
            VsCruiseAppliedBasePitchRateDegPerSec = 0f;
            VsCruisePreBrakeActive = false;
        }

        void UpdateVsCruiseAccelerationGuide(float effectiveError)
        {
            bool precisionContext = !ManualZeroVsTrajectoryBrakeActive &&
                (AltitudePrecisionHoldActive || PrecisionBasePitchActive);
            float errorBlend = 1f - Mathf.Clamp01((Mathf.Abs(effectiveError) - VsCruiseBlendFullErrorMps) /
                Mathf.Max(0.01f, VsCruiseBlendReleaseErrorMps - VsCruiseBlendFullErrorMps));
            float targetBlend = precisionContext ? LowQVerticalEnvelopeBlend * errorBlend : 0f;

            // Do not let a manual precision state leave a tail in the broad capture path.
            if (!precisionContext)
                VsCruiseAccelerationGuideBlend = 0f;
            else
            {
                float blendTime = targetBlend > VsCruiseAccelerationGuideBlend
                    ? VsCruiseBlendAttackSeconds : VsCruiseBlendReleaseSeconds;
                VsCruiseAccelerationGuideBlend = Mathf.MoveTowards(
                    VsCruiseAccelerationGuideBlend,
                    targetBlend,
                    Time.fixedDeltaTime / Mathf.Max(0.01f, blendTime));
            }

            VsCruiseAccelerationGuideActive = VsCruiseAccelerationGuideBlend > 0.001f;
            if (!VsCruiseAccelerationGuideActive)
            {
                VsCruiseDesiredVerticalAccelerationMps2 = 0f;
                VsCruiseAccelerationErrorMps2 = 0f;
                VsCruiseBasePitchRateCommandDegPerSec = 0f;
                VsCruisePreBrakeActive = false;
                return;
            }

            VsCruiseDesiredVerticalAccelerationMps2 = Mathf.Clamp(
                effectiveError * VsCruiseAccelerationRelaxationGainPerSec,
                -VsCruiseDesiredAccelerationLimitMps2,
                 VsCruiseDesiredAccelerationLimitMps2);
            VsCruiseAccelerationErrorMps2 = VsCruiseDesiredVerticalAccelerationMps2 - VerticalAccelerationMps2;
            VsCruiseBasePitchRateCommandDegPerSec = Mathf.Clamp(
                VsCruiseAccelerationErrorMps2 * VsCruiseAccelerationErrorRateGainDegPerMps2Sec,
                -VsCruiseBasePitchRateLimitDegPerSec,
                 VsCruiseBasePitchRateLimitDegPerSec);
            VsCruisePreBrakeActive = Mathf.Abs(effectiveError) > EffectiveVerticalSpeedDeadbandMps &&
                Mathf.Abs(VsCruiseBasePitchRateCommandDegPerSec) > 0.001f &&
                Mathf.Sign(VsCruiseBasePitchRateCommandDegPerSec) != Mathf.Sign(effectiveError);
        }

        void ResetPrecisionPhaseState(string phase)
        {
            PrecisionBasePitchActive = false;
            PrecisionWithinTarget = false;
            PrecisionWithinTargetElapsedSeconds = 0f;
            PrecisionCapturePhase = false;
            precisionEntryElapsedSeconds = 0f;
            PrecisionExitElapsedSeconds = 0f;
            PrecisionHoldEntryElapsedSeconds = 0f;
            PrecisionHoldExitElapsedSeconds = 0f;
            ResetAltitudePrecisionTrackingState();
            ResetAltitudeLowQPrecisionQuietingState();
            ResetVsCruiseAccelerationGuideState();
            PrecisionPhase = phase;
        }

        void SetPrecisionPhase(string phase)
        {
            if (PrecisionPhase == phase) return;
            PrecisionPhase = phase;
            PrecisionPhaseTransitions++;
        }

        internal void SetArmed(bool armed, Vessel vessel, VirtualAttitudeInstrument attitude, AERISPitchDirector pitch)
        {
            if (Armed == armed) return;
            Armed = armed;
            if (armed)
            {
                // Do not overwrite the user's draft or applied target when MASTER/V/S is armed.
                // APPLY is the only operation that changes the active target; SET CURRENT is the
                // only operation that writes the current V/S into the input field.
                GeneratedPitchTargetDeg = pitch != null && IsFinite(pitch.CurrentPitch)
                    ? pitch.CurrentPitch : 0f;
                VerticalSpeedBasePitchDeg = GeneratedPitchTargetDeg;
                BasePitchAdaptContributionDeg = 0f;
                ZeroVsBasePitchAdaptContributionDeg = 0f;
                DesiredPitchBeforeClampDeg = GeneratedPitchTargetDeg;
                DesiredPitchAfterClampDeg = GeneratedPitchTargetDeg;
                PitchTargetSaturated = PitchUpperSaturated = PitchLowerSaturated = false;
                VerticalSpeedTrimDeg = 0f;
                VerticalAccelerationMps2 = 0f;
                previousVerticalSpeedMps = attitude != null && IsFinite(attitude.VerticalSpeedMps)
                    ? attitude.VerticalSpeedMps : 0f;
                havePreviousVerticalSpeed = false;
                SurfaceSpeedMps = attitude != null && attitude.SharedSurfaceSpeedValid &&
                    IsFinite(attitude.SurfaceSpeedMps) ? Mathf.Max(0f, attitude.SurfaceSpeedMps) : 0f;
                previousSurfaceSpeedMps = SurfaceSpeedMps;
                havePreviousSurfaceSpeed = false;
                SurfaceSpeedRateMps2 = 0f;
                BasePitchSpeedAdaptContributionDeg = 0f;
                BasePitchSpeedAdaptActive = false;
                havePreviousEffectiveError = false;
                postTakeoffTrajectorySeedPending = false;
                PrecisionTrimContributionDeg = 0f;
                TerminalQuietZoneActive = false;
                TerminalPitchTargetHeld = false;
                TerminalQuietHeldPitchTargetDeg = GeneratedPitchTargetDeg;
                TerminalQuietTargetDeltaDeg = 0f;
                terminalQuietHoldUntil = 0f;
                PredictedVerticalSpeedMps = TargetVerticalSpeedMps;
                PredictedStopErrorMps = 0f;
                BrakeContributionDeg = 0f;
                OvershootLatchActive = false;
                BrakeState = "Armed";
                ProportionalContributionDeg = 0f;
                DampingContributionDeg = 0f;
                RecoveryContributionDeg = 0f;
                precisionPushDeg = 0f;
                PrecisionBasePitchActive = false;
                PrecisionWithinTarget = false;
                PrecisionWithinTargetElapsedSeconds = 0f;
                PrecisionBasePitchRateDegPerSec = 0f;
                PrecisionBasePitchAdaptContributionDeg = 0f;
                PrecisionTrimTransferDeg = 0f;
                PrecisionCapturePhase = false;
                PrecisionActiveRateGainDegPerMpsSec = 0f;
                PrecisionActiveAccelerationDampingDegPerMps2Sec = 0f;
                PrecisionActiveRateLimitDegPerSec = 0f;
                BasePitchSpeedAdaptRateDegPerSec = 0f;
                PrecisionNetBasePitchRateDegPerSec = 0f;
                precisionEntryElapsedSeconds = 0f;
                PlannedPitchRateDegPerSec = 0f;
                VsRateProportionalDegPerSec = 0f;
                VsRateDampingDegPerSec = 0f;
                VsRateBrakeDegPerSec = 0f;
                VsBasePitchHoldRateDegPerSec = 0f;
                VsAttitudeErrorDeg = 0f;
                VsAttitudeRateProportionalDegPerSec = 0f;
                VsAttitudeRateDampingDegPerSec = 0f;
                VsRateTargetDegPerSec = 0f;
                VsRateCommandSlewDegPerSec2 = 0f;
                DirectRateScheme = "Armed";
                DirectPitchRateActive = false;
                ControlActive = false;
                VerticalSpeedErrorValid = false;
                EffectiveTargetVerticalSpeedMps = 0f;
                AltitudeRateDemandActive = false;
                AltitudeRateDemandMps = 0f;
                AltitudePrecisionHoldActive = false;
                ResetAltitudePrecisionTrackingState();
                EffectiveVerticalSpeedDeadbandMps = VerticalSpeedDeadbandMps;
                AltitudePitchLimitActive = false;
                AltitudePitchLimitDeg = 0f;
                ResetHighQManualZeroVsTelemetry();
                ResetPrecisionPhaseState("Armed");
                PrecisionPhaseTransitions = 0;
                if (pitch != null) { pitch.ClearVerticalSpeedRateDemand(); pitch.SetArmed(true, vessel, attitude); }
                ControlState = "Armed";
                AERISLogger.Info("[V/S] armed: target preserved=" + TargetVerticalSpeedText + " m/s.");
            }
            else
            {
                ControlState = "Inactive";
                VerticalSpeedBasePitchDeg = 0f;
                BasePitchAdaptContributionDeg = 0f;
                ZeroVsBasePitchAdaptContributionDeg = 0f;
                DesiredPitchBeforeClampDeg = 0f;
                DesiredPitchAfterClampDeg = 0f;
                PitchTargetSaturated = PitchUpperSaturated = PitchLowerSaturated = false;
                VerticalSpeedTrimDeg = 0f;
                VerticalAccelerationMps2 = 0f;
                SurfaceSpeedMps = 0f;
                SurfaceSpeedRateMps2 = 0f;
                BasePitchSpeedAdaptContributionDeg = 0f;
                BasePitchSpeedAdaptActive = false;
                havePreviousSurfaceSpeed = false;
                havePreviousVerticalSpeed = false;
                havePreviousEffectiveError = false;
                postTakeoffTrajectorySeedPending = false;
                PrecisionTrimContributionDeg = 0f;
                TerminalQuietZoneActive = false;
                TerminalPitchTargetHeld = false;
                TerminalQuietHeldPitchTargetDeg = GeneratedPitchTargetDeg;
                TerminalQuietTargetDeltaDeg = 0f;
                terminalQuietHoldUntil = 0f;
                PredictedVerticalSpeedMps = 0f;
                PredictedStopErrorMps = 0f;
                BrakeContributionDeg = 0f;
                OvershootLatchActive = false;
                BrakeState = "Inactive";
                ProportionalContributionDeg = 0f;
                DampingContributionDeg = 0f;
                RecoveryContributionDeg = 0f;
                precisionPushDeg = 0f;
                PrecisionBasePitchActive = false;
                PrecisionWithinTarget = false;
                PrecisionWithinTargetElapsedSeconds = 0f;
                PrecisionBasePitchRateDegPerSec = 0f;
                PrecisionBasePitchAdaptContributionDeg = 0f;
                PrecisionTrimTransferDeg = 0f;
                PrecisionCapturePhase = false;
                PrecisionActiveRateGainDegPerMpsSec = 0f;
                PrecisionActiveAccelerationDampingDegPerMps2Sec = 0f;
                PrecisionActiveRateLimitDegPerSec = 0f;
                BasePitchSpeedAdaptRateDegPerSec = 0f;
                PrecisionNetBasePitchRateDegPerSec = 0f;
                precisionEntryElapsedSeconds = 0f;
                PlannedPitchRateDegPerSec = 0f;
                VsRateProportionalDegPerSec = 0f;
                VsRateDampingDegPerSec = 0f;
                VsRateBrakeDegPerSec = 0f;
                VsBasePitchHoldRateDegPerSec = 0f;
                VsAttitudeErrorDeg = 0f;
                VsAttitudeRateProportionalDegPerSec = 0f;
                VsAttitudeRateDampingDegPerSec = 0f;
                VsRateTargetDegPerSec = 0f;
                VsRateCommandSlewDegPerSec2 = 0f;
                DirectRateScheme = "Inactive";
                DirectPitchRateActive = false;
                AltitudeRateDemandActive = false;
                AltitudeRateDemandMps = 0f;
                AltitudePrecisionHoldActive = false;
                ResetAltitudePrecisionTrackingState();
                EffectiveVerticalSpeedDeadbandMps = VerticalSpeedDeadbandMps;
                AltitudePitchLimitActive = false;
                AltitudePitchLimitDeg = 0f;
                    ResetHighQManualZeroVsTelemetry();
                if (pitch != null) pitch.ClearVerticalSpeedRateDemand();
                AERISLogger.Info("[V/S] disarmed.");
            }
        }

        internal void Disable(string reason)
        {
            if (!Armed) return;
            Armed = false;
            ControlActive = false;
            VerticalSpeedErrorValid = false;
            EffectiveTargetVerticalSpeedMps = 0f;
            AltitudeRateDemandActive = false;
            AltitudeRateDemandMps = 0f;
            AltitudePrecisionHoldActive = false;
            ResetAltitudePrecisionTrackingState();
            EffectiveVerticalSpeedDeadbandMps = VerticalSpeedDeadbandMps;
            AltitudePitchLimitActive = false;
            AltitudePitchLimitDeg = 0f;
            ResetHighQManualZeroVsTelemetry();
            ResetPrecisionPhaseState("Inactive");
            BasePitchSpeedAdaptActive = false;
            PrecisionBasePitchActive = false;
            PrecisionWithinTarget = false;
            PrecisionWithinTargetElapsedSeconds = 0f;
            PrecisionBasePitchRateDegPerSec = 0f;
            PrecisionBasePitchAdaptContributionDeg = 0f;
            PrecisionTrimTransferDeg = 0f;
            precisionEntryElapsedSeconds = 0f;
            PlannedPitchRateDegPerSec = 0f;
            VsAttitudeErrorDeg = 0f;
            VsAttitudeRateProportionalDegPerSec = 0f;
            VsAttitudeRateDampingDegPerSec = 0f;
            VsRateTargetDegPerSec = 0f;
            VsRateCommandSlewDegPerSec2 = 0f;
            DirectRateScheme = "Inactive";
            DirectPitchRateActive = false;
            postTakeoffTrajectorySeedPending = false;
            ControlState = "Inactive";
            AERISLogger.Info("[V/S] disabled: " + reason);
        }

        // A ground-armed V/S director has deliberately remained in standby while Auto
        // Takeoff (or the pilot) establishes the climb.  Start its internal trajectory
        // at the measured takeoff state so releasing execution cannot command an
        // artificial return to the pitch attitude that existed when it was armed.
        // The user's selected V/S target is preserved unchanged.
        internal void PreparePostTakeoffActivation(Vessel vessel,
            VirtualAttitudeInstrument attitude, AERISPitchDirector pitch, string source)
        {
            if (!Armed || vessel == null || attitude == null || pitch == null ||
                !attitude.InstrumentPitchValid || !attitude.SharedSurfaceSpeedValid ||
                !attitude.SharedDynamicPressureValid || !attitude.VerticalSpeedValid ||
                !IsFinite(attitude.InstrumentPitchDeg) ||
                !IsFinite(attitude.InstrumentPitchRateDegPerSec) ||
                !IsFinite(attitude.VerticalSpeedMps) || !IsFinite(attitude.SurfaceSpeedMps)) return;

            float pitchSeedDeg = attitude.InstrumentPitchDeg;
            float nativeRateLimitDegPerSec = Mathf.Max(0.1f,
                VsAttitudeMaxRateCommand * pitch.NativePitchRatePerVirtualStickDegPerSec);
            float rateSeedDegPerSec = Mathf.Clamp(attitude.InstrumentPitchRateDegPerSec,
                -nativeRateLimitDegPerSec, nativeRateLimitDegPerSec);

            CurrentVerticalSpeedMps = attitude.VerticalSpeedMps;
            previousVerticalSpeedMps = CurrentVerticalSpeedMps;
            havePreviousVerticalSpeed = true;
            SurfaceSpeedMps = Mathf.Max(0f, attitude.SurfaceSpeedMps);
            previousSurfaceSpeedMps = SurfaceSpeedMps;
            havePreviousSurfaceSpeed = true;
            VerticalAccelerationMps2 = 0f;
            SurfaceSpeedRateMps2 = 0f;
            previousEffectiveErrorMps = 0f;
            havePreviousEffectiveError = false;

            GeneratedPitchTargetDeg = pitchSeedDeg;
            VerticalSpeedBasePitchDeg = pitchSeedDeg;
            DesiredPitchBeforeClampDeg = pitchSeedDeg;
            DesiredPitchAfterClampDeg = pitchSeedDeg;
            TerminalQuietHeldPitchTargetDeg = pitchSeedDeg;
            TerminalQuietTargetDeltaDeg = 0f;
            BasePitchAdaptContributionDeg = 0f;
            ZeroVsBasePitchAdaptContributionDeg = 0f;
            BasePitchSpeedAdaptContributionDeg = 0f;
            VerticalSpeedTrimDeg = 0f;
            ProportionalContributionDeg = 0f;
            DampingContributionDeg = 0f;
            BrakeContributionDeg = 0f;
            RecoveryContributionDeg = 0f;
            precisionPushDeg = 0f;
            PlannedPitchRateDegPerSec = rateSeedDegPerSec;
            VsRateTargetDegPerSec = rateSeedDegPerSec;
            VsAttitudeErrorDeg = 0f;
            targetChangedSinceUpdate = true;
            postTakeoffTrajectorySeedPending = true;
            ResetPrecisionPhaseState("PostTakeoffSeed");
            DirectRateScheme = "PostTakeoffSeed";
            ControlState = "HandoffSeeded";

            AERISLogger.Info("[V/S] post-takeoff trajectory seeded after " + source +
                ": pitch=" + pitchSeedDeg.ToString("F2") + " deg; pitchRate=" +
                rateSeedDegPerSec.ToString("F2") + " deg/s; V/S=" +
                CurrentVerticalSpeedMps.ToString("F2") + " m/s; prepared target preserved=" +
                TargetVerticalSpeedText + " m/s.");
        }

        internal void SetAltitudeVerticalSpeedDemand(float targetMps, float altitudePitchLimitDeg,
            bool altitudePrecisionHoldActive)
        {
            bool activating = !AltitudeRateDemandActive;
            AltitudeRateDemandActive = true;
            AltitudeRateDemandMps = Mathf.Clamp(targetMps, -100f, 100f);
            AltitudePrecisionHoldActive = altitudePrecisionHoldActive;
            if (!AltitudePrecisionHoldActive)
            {
                ResetAltitudePrecisionTrackingState();
                ResetAltitudeLowQPrecisionQuietingState();
            }
            AltitudePitchLimitActive = true;
            AltitudePitchLimitDeg = Mathf.Clamp(altitudePitchLimitDeg, 0f, 90f);
            // Re-anchor V/S equilibrium once when ALT takes ownership.  Do not set this
            // every frame: ALT's rate demand is a smooth trajectory, not repeated user APPLY.
            if (activating) targetChangedSinceUpdate = true;
        }

        internal void ClearAltitudeVerticalSpeedDemand()
        {
            if (!AltitudeRateDemandActive && !AltitudePitchLimitActive) return;
            AltitudeRateDemandActive = false;
            AltitudeRateDemandMps = 0f;
            AltitudePrecisionHoldActive = false;
            ResetAltitudePrecisionTrackingState();
            ResetAltitudeLowQPrecisionQuietingState();
            EffectiveVerticalSpeedDeadbandMps = VerticalSpeedDeadbandMps;
            AltitudePitchLimitActive = false;
            AltitudePitchLimitDeg = 0f;
            // Switching ALT -> manual V/S is a genuine target-source change.
            targetChangedSinceUpdate = true;
        }

        internal void SetCurrent(VirtualAttitudeInstrument attitude)
        {
            if (attitude == null || !attitude.InstrumentValid || !attitude.VerticalSpeedValid ||
                !IsFinite(attitude.VerticalSpeedMps)) return;
            TargetVerticalSpeedMps = attitude.VerticalSpeedMps;
            TargetVerticalSpeedText = TargetVerticalSpeedMps.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            targetChangedSinceUpdate = true;
        }

        internal bool TrySetMaxPitchTarget(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
                !float.TryParse(text, out value)) || float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric maximum pitch limit.";
                return false;
            }
            if (value < 0f || value > 90f)
            {
                error = "Maximum pitch limit must be between 0 and 90 degrees.";
                return false;
            }
            MaxPitchTargetDeg = value;
            MaxPitchTargetText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            AERISLogger.Info("[V/S] max pitch limit=" + MaxPitchTargetText + " deg");
            return true;
        }

        internal bool TrySetTarget(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
                !float.TryParse(text, out value)) || float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric vertical speed target.";
                return false;
            }
            if (value < -100f || value > 100f)
            {
                error = "Vertical speed target must be between -100 and +100 m/s.";
                return false;
            }
            float previousTarget = TargetVerticalSpeedMps;
            TargetVerticalSpeedMps = value;
            TargetVerticalSpeedText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            targetChangedSinceUpdate = true;
            // Only an explicit manual non-zero -> zero request may arm this bounded guard.
            // It does not run for ALT-owned demand, non-zero manual V/S, PITCH, BANK or HDG.
            if (Armed && !AltitudeRateDemandActive && Mathf.Abs(value) <= ZeroVsTargetBandMps &&
                Mathf.Abs(previousTarget) >= ManualZeroVsTransitionGuardMinimumPriorTargetMps)
            {
                manualZeroVsTransitionGuardPending = true;
                manualZeroVsTrajectoryBrakePending = true;
                ManualZeroVsTransitionGuardFromMps = previousTarget;
                AERISLogger.Info("[V/S] manual zero-V/S transition guard and trajectory brake armed: from=" +
                    previousTarget.ToString("F1") + " m/s.");
            }
            AERISLogger.Info("[V/S] target=" + TargetVerticalSpeedText + " m/s");
            return true;
        }

        internal void Update(Vessel vessel, VirtualAttitudeInstrument attitude, AERISPitchDirector pitch, bool aerisMaster, bool standardFbwActive)
        {
            bool sensorValid = attitude != null && attitude.InstrumentPitchValid &&
                attitude.SharedSurfaceSpeedValid && attitude.SharedDynamicPressureValid &&
                attitude.VerticalSpeedValid &&
                IsFinite(attitude.InstrumentPitchDeg) &&
                IsFinite(attitude.InstrumentPitchRateDegPerSec) &&
                IsFinite(attitude.VerticalSpeedMps) && IsFinite(attitude.SurfaceSpeedMps) &&
                IsFinite(attitude.DynamicPressureKpa);
            if (!Armed || !aerisMaster || !standardFbwActive || vessel == null || vessel.packed || vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH || !sensorValid || pitch == null)
            {
                if (pitch != null) pitch.ClearVerticalSpeedRateDemand();
                BasePitchSpeedAdaptActive = false;
                EffectiveVerticalSpeedDeadbandMps = VerticalSpeedDeadbandMps;
                DirectPitchRateActive = false;
                ControlActive = false;
                VerticalSpeedErrorValid = false;
                EffectiveTargetVerticalSpeedMps = 0f;
                ResetPrecisionPhaseState(Armed ? "Standby" : "Inactive");
                PrecisionBasePitchActive = false;
                PrecisionWithinTarget = false;
                PrecisionWithinTargetElapsedSeconds = 0f;
                PrecisionBasePitchRateDegPerSec = 0f;
                PrecisionBasePitchAdaptContributionDeg = 0f;
                PrecisionTrimTransferDeg = 0f;
                PrecisionCapturePhase = false;
                PrecisionActiveRateGainDegPerMpsSec = 0f;
                PrecisionActiveAccelerationDampingDegPerMps2Sec = 0f;
                PrecisionActiveRateLimitDegPerSec = 0f;
                BasePitchSpeedAdaptRateDegPerSec = 0f;
                PrecisionNetBasePitchRateDegPerSec = 0f;
                precisionEntryElapsedSeconds = 0f;
                PlannedPitchRateDegPerSec = 0f;
                VsAttitudeErrorDeg = 0f;
                VsAttitudeRateProportionalDegPerSec = 0f;
                VsAttitudeRateDampingDegPerSec = 0f;
                VsRateTargetDegPerSec = 0f;
                VsRateCommandSlewDegPerSec2 = 0f;
                DirectRateScheme = Armed ? "Standby" : "Inactive";
                ControlState = Armed ? "Standby" : "Inactive";
                ResetHighQManualZeroVsTelemetry();
                if (!sensorValid)
                {
                    havePreviousVerticalSpeed = false;
                    havePreviousSurfaceSpeed = false;
                    VerticalAccelerationMps2 = 0f;
                    SurfaceSpeedRateMps2 = 0f;
                }
                return;
            }

            ControlActive = true;
            VerticalSpeedErrorValid = true;
            EffectiveTargetVerticalSpeedMps = AltitudeRateDemandActive
                ? AltitudeRateDemandMps : TargetVerticalSpeedMps;
            CurrentVerticalSpeedMps = attitude.VerticalSpeedMps;
            // Preserve this public/UI/FDR error as the final pilot/ALT target error.
            // The v0.6.8 planner below may expose a separate intermediate control target
            // during a manual large non-zero -> zero deceleration.
            VerticalSpeedErrorMps = EffectiveTargetVerticalSpeedMps - CurrentVerticalSpeedMps;
            EffectiveVerticalSpeedDeadbandMps = AltitudePrecisionHoldActive
                ? Mathf.Min(VerticalSpeedDeadbandMps, AltitudePrecisionLowRateDeadbandMps)
                : VerticalSpeedDeadbandMps;

            float rawAcceleration = 0f;
            if (havePreviousVerticalSpeed && Time.fixedDeltaTime > 0.0001f)
                rawAcceleration = (CurrentVerticalSpeedMps - previousVerticalSpeedMps) / Time.fixedDeltaTime;
            previousVerticalSpeedMps = CurrentVerticalSpeedMps;
            havePreviousVerticalSpeed = true;
            VerticalAccelerationMps2 = Mathf.Lerp(VerticalAccelerationMps2, rawAcceleration, VerticalAccelerationFilter);

            SurfaceSpeedMps = Mathf.Max(0f, attitude.SurfaceSpeedMps);
            float rawSpeedRate = 0f;
            if (havePreviousSurfaceSpeed && Time.fixedDeltaTime > 0.0001f)
                rawSpeedRate = (SurfaceSpeedMps - previousSurfaceSpeedMps) / Time.fixedDeltaTime;
            previousSurfaceSpeedMps = SurfaceSpeedMps;
            havePreviousSurfaceSpeed = true;
            SurfaceSpeedRateMps2 = Mathf.Lerp(SurfaceSpeedRateMps2, rawSpeedRate, BasePitchSpeedRateFilter);

            // Keep this schedule independent from AA and independent from the crosscheck.
            // It is derived from the same native vessel state already used by BANK's q schedule.
            DynamicPressureKpa = Mathf.Max(0f, attitude.DynamicPressureKpa);
            DynamicPressureHighQSchedule = Mathf.Clamp01((DynamicPressureKpa - DynamicPressureHighQStartKpa) /
                Mathf.Max(0.01f, DynamicPressureHighQFullKpa - DynamicPressureHighQStartKpa));
            DynamicPressureMode = DynamicPressureHighQSchedule >= 1f ? "HIGH_Q" :
                (DynamicPressureHighQSchedule > 0f ? "Q_HIGH_BLEND" : "MID_Q");

            // Low-q vertical authority schedule. It is the mirror image of the established
            // high-q quieting: low pressure means the V/S trajectory must ask for less
            // pitch excursion and a lower AA-native pitch-rate before a large vertical
            // error can grow into a saturated limit cycle.
            LowQVerticalEnvelopeBlend = Mathf.Clamp01((LowQVerticalEnvelopeStartKpa - DynamicPressureKpa) /
                Mathf.Max(0.01f, LowQVerticalEnvelopeStartKpa - LowQVerticalEnvelopeFullKpa));
            LowQVerticalEnvelopeActive = LowQVerticalEnvelopeBlend > 0.001f;
            LowQFilteredAccelerationMps2 = Mathf.Lerp(LowQFilteredAccelerationMps2,
                VerticalAccelerationMps2, LowQAccelerationFilter);
            LowQVerticalEnvelopeAppliedBlend = LowQVerticalEnvelopeBlend;
            LowQEffectiveMaxPitchTargetDeg = Mathf.Lerp(MaxPitchTargetDeg,
                Mathf.Min(MaxPitchTargetDeg, LowQMinimumMaxPitchDeg), LowQVerticalEnvelopeAppliedBlend);
            LowQProportionalScale = Mathf.Lerp(1f, LowQProportionalScaleTarget, LowQVerticalEnvelopeAppliedBlend);
            LowQDampingScale = Mathf.Lerp(1f, LowQDampingScaleTarget, LowQVerticalEnvelopeAppliedBlend);
            LowQDampingLimitDeg = LowQVerticalEnvelopeAppliedBlend > 0.001f ? LowQDampingLimitDegTarget : 0f;
            LowQPitchSlewScale = Mathf.Lerp(1f, LowQPitchSlewScaleTarget, LowQVerticalEnvelopeAppliedBlend);
            LowQDirectRateScale = Mathf.Lerp(1f, LowQDirectRateScaleTarget, LowQVerticalEnvelopeAppliedBlend);
            LowQRateCommandSlewScale = Mathf.Lerp(1f, LowQRateCommandSlewScaleTarget, LowQVerticalEnvelopeAppliedBlend);
            LowQBasePitchAdaptScale = Mathf.Lerp(1f, LowQBasePitchAdaptScaleTarget, LowQVerticalEnvelopeAppliedBlend);

            bool manualZeroVsFinalTarget = !AltitudeRateDemandActive &&
                Mathf.Abs(EffectiveTargetVerticalSpeedMps) <= ZeroVsTargetBandMps;
            if (manualZeroVsFinalTarget && manualZeroVsTrajectoryBrakePending)
            {
                manualZeroVsTrajectoryBrakePending = false;
                float currentMagnitude = Mathf.Abs(CurrentVerticalSpeedMps);
                float targetSign = Mathf.Sign(ManualZeroVsTransitionGuardFromMps);
                bool directionMatches = Mathf.Abs(targetSign) > 0.001f &&
                    Mathf.Sign(CurrentVerticalSpeedMps) == targetSign;
                if (directionMatches && currentMagnitude >= ManualZeroVsTrajectoryMinimumStartMps)
                {
                    ManualZeroVsTrajectoryBrakeActive = true;
                    ManualZeroVsTrajectoryInitialMps = CurrentVerticalSpeedMps;
                    ManualZeroVsTrajectoryTargetMps = CurrentVerticalSpeedMps -
                        targetSign * Mathf.Min(ManualZeroVsTrajectoryInitialLeadMps, currentMagnitude);
                    ManualZeroVsTrajectoryAppliedDecelMps2 = 0f;
                    ManualZeroVsTrajectoryElapsedSeconds = 0f;
                    ManualZeroVsTrajectoryState = "JerkIn";
                    AERISLogger.Info("[V/S] manual zero-V/S trajectory brake started: actual=" +
                        CurrentVerticalSpeedMps.ToString("F2") + " m/s, from=" +
                        ManualZeroVsTransitionGuardFromMps.ToString("F1") + " m/s.");
                }
                else
                {
                    ResetManualZeroVsTrajectoryBrake("Bypassed");
                }
            }
            if (!manualZeroVsFinalTarget)
                ResetManualZeroVsTrajectoryBrake("Inactive");

            ManualZeroVsTrajectoryPressureBlend = Mathf.Clamp01((DynamicPressureKpa - ManualZeroVsTrajectoryStartKpa) /
                Mathf.Max(0.01f, ManualZeroVsTrajectoryFullKpa - ManualZeroVsTrajectoryStartKpa));
            if (ManualZeroVsTrajectoryBrakeActive)
            {
                float remainingMagnitude = Mathf.Abs(ManualZeroVsTrajectoryTargetMps);
                ManualZeroVsTrajectoryScheduledDecelMps2 = Mathf.Lerp(
                    ManualZeroVsTrajectoryMinDecelMps2,
                    ManualZeroVsTrajectoryMaxDecelMps2,
                    ManualZeroVsTrajectoryPressureBlend);
                // As the planned rate approaches zero, reduce the requested deceleration
                // before the endpoint.  This creates a jerk-limited rate trajectory with
                // no instantaneous +50 -> 0 outer-loop error step.
                float endpointLimitedDecel = Mathf.Sqrt(Mathf.Max(0f,
                    2f * ManualZeroVsTrajectoryMaxJerkMps3 * remainingMagnitude));
                float desiredDecel = Mathf.Min(ManualZeroVsTrajectoryScheduledDecelMps2, endpointLimitedDecel);
                ManualZeroVsTrajectoryAppliedDecelMps2 = Mathf.MoveTowards(
                    ManualZeroVsTrajectoryAppliedDecelMps2, desiredDecel,
                    ManualZeroVsTrajectoryMaxJerkMps3 * Time.fixedDeltaTime);
                ManualZeroVsTrajectoryTargetMps = Mathf.MoveTowards(
                    ManualZeroVsTrajectoryTargetMps, 0f,
                    ManualZeroVsTrajectoryAppliedDecelMps2 * Time.fixedDeltaTime);
                ManualZeroVsTrajectoryElapsedSeconds += Time.fixedDeltaTime;
                ManualZeroVsTrajectoryState = desiredDecel + 0.01f < ManualZeroVsTrajectoryAppliedDecelMps2
                    ? "JerkOut"
                    : (ManualZeroVsTrajectoryAppliedDecelMps2 + 0.01f < desiredDecel ? "JerkIn" : "Decelerate");
                if (Mathf.Abs(ManualZeroVsTrajectoryTargetMps) <= ManualZeroVsTrajectoryCompletionBandMps)
                {
                    ManualZeroVsTrajectoryTargetMps = 0f;
                    ManualZeroVsTrajectoryAppliedDecelMps2 = 0f;
                    ManualZeroVsTrajectoryBrakeActive = false;
                    ManualZeroVsTrajectoryState = "Handoff";
                    AERISLogger.Info("[V/S] manual zero-V/S trajectory brake handoff after " +
                        ManualZeroVsTrajectoryElapsedSeconds.ToString("F2") + " s.");
                }
            }

            ControlTargetVerticalSpeedMps = ManualZeroVsTrajectoryBrakeActive
                ? ManualZeroVsTrajectoryTargetMps : EffectiveTargetVerticalSpeedMps;
            ControlVerticalSpeedErrorMps = ControlTargetVerticalSpeedMps - CurrentVerticalSpeedMps;
            ManualZeroVsTrajectoryControlErrorMps = ControlVerticalSpeedErrorMps;
            float effectiveError = Mathf.Abs(ControlVerticalSpeedErrorMps) <= EffectiveVerticalSpeedDeadbandMps
                ? 0f : ControlVerticalSpeedErrorMps;

            // A slower, control-only acceleration estimate is used only by the high-q
            // non-zero V/S PrecisionCapture stabilizer below. It applies identically to
            // direct V/S and to ALT-originated V/S demands; preserve the normal acceleration
            // trace for all other modes and for diagnostics.
            HighQNonZeroVsPrecisionFilteredAccelerationMps2 = Mathf.Lerp(
                HighQNonZeroVsPrecisionFilteredAccelerationMps2,
                VerticalAccelerationMps2,
                HighQNonZeroVsPrecisionAccelerationFilter);
            // ALT ultimately uses this same V/S precision-capture path.  Do not let the
            // origin of an otherwise identical non-zero V/S demand bypass the high-q damping
            // guard: that produced a raw vertical-acceleration D-term limit cycle in ALT climbs.
            bool nonZeroVsPrecisionCapture =
                Mathf.Abs(EffectiveTargetVerticalSpeedMps) > ZeroVsTargetBandMps &&
                PrecisionBasePitchActive && !PrecisionWithinTarget;
            float nonZeroVsPrecisionErrorBlend = 1f - Mathf.Clamp01(
                (Mathf.Abs(effectiveError) - HighQNonZeroVsPrecisionCaptureEntryErrorMps) /
                Mathf.Max(0.01f, HighQNonZeroVsPrecisionCaptureExitErrorMps -
                    HighQNonZeroVsPrecisionCaptureEntryErrorMps));
            HighQNonZeroVsPrecisionCaptureBlend = nonZeroVsPrecisionCapture
                ? DynamicPressureHighQSchedule * nonZeroVsPrecisionErrorBlend : 0f;
            HighQNonZeroVsPrecisionCaptureProfileActive = HighQNonZeroVsPrecisionCaptureBlend > 0.001f;
            HighQNonZeroVsPrecisionDampingScale = Mathf.Lerp(1f,
                HighQNonZeroVsPrecisionDampingScaleTarget, HighQNonZeroVsPrecisionCaptureBlend);
            HighQNonZeroVsPrecisionDampingLimitDeg = HighQNonZeroVsPrecisionCaptureProfileActive
                ? HighQNonZeroVsPrecisionDampingLimitDegTarget : 0f;
            HighQNonZeroVsPrecisionBasePitchDampingScale = Mathf.Lerp(1f,
                HighQNonZeroVsPrecisionBasePitchDampingScaleTarget, HighQNonZeroVsPrecisionCaptureBlend);

            // The same high-q D-term can oscillate while ALT is still far from altitude
            // target but already tracking its non-zero V/S command.  This path is explicitly
            // MainTrajectory-only so it does not alter large rate captures or the tested
            // PrecisionCapture/PrecisionHold profiles.
            HighQNonZeroVsTrackingFilteredAccelerationMps2 = Mathf.Lerp(
                HighQNonZeroVsTrackingFilteredAccelerationMps2,
                VerticalAccelerationMps2,
                HighQNonZeroVsTrackingAccelerationFilter);
            bool nonZeroVsTracking =
                Mathf.Abs(EffectiveTargetVerticalSpeedMps) > ZeroVsTargetBandMps &&
                !ManualZeroVsTrajectoryBrakeActive &&
                !PrecisionBasePitchActive;
            float nonZeroVsTrackingErrorBlend = 1f - Mathf.Clamp01(
                (Mathf.Abs(effectiveError) - HighQNonZeroVsTrackingEntryErrorMps) /
                Mathf.Max(0.01f, HighQNonZeroVsTrackingExitErrorMps -
                    HighQNonZeroVsTrackingEntryErrorMps));
            HighQNonZeroVsTrackingBlend = nonZeroVsTracking
                ? DynamicPressureHighQSchedule * nonZeroVsTrackingErrorBlend : 0f;
            HighQNonZeroVsTrackingProfileActive = HighQNonZeroVsTrackingBlend > 0.001f;
            HighQNonZeroVsTrackingDampingScale = Mathf.Lerp(1f,
                HighQNonZeroVsTrackingDampingScaleTarget, HighQNonZeroVsTrackingBlend);
            HighQNonZeroVsTrackingDampingLimitDeg = HighQNonZeroVsTrackingProfileActive
                ? HighQNonZeroVsTrackingDampingLimitDegTarget : 0f;
            HighQNonZeroVsTrackingPitchSlewScale = Mathf.Lerp(1f,
                HighQNonZeroVsTrackingPitchSlewScaleTarget, HighQNonZeroVsTrackingBlend);
            HighQNonZeroVsTrackingDirectRateScale = Mathf.Lerp(1f,
                HighQNonZeroVsTrackingDirectRateScaleTarget, HighQNonZeroVsTrackingBlend);
            HighQNonZeroVsTrackingRateCommandSlewScale = Mathf.Lerp(1f,
                HighQNonZeroVsTrackingRateCommandSlewScaleTarget, HighQNonZeroVsTrackingBlend);
            HighQNonZeroVsTrackingBasePitchDampingScale = Mathf.Lerp(1f,
                HighQNonZeroVsTrackingBasePitchDampingScaleTarget, HighQNonZeroVsTrackingBlend);

            MidQFilteredAccelerationMps2 = Mathf.Lerp(MidQFilteredAccelerationMps2, VerticalAccelerationMps2,
                MidQVerticalTrackingAccelerationFilter);
            float midQRise = Mathf.Clamp01((DynamicPressureKpa - MidQVerticalTrackingStartKpa) /
                Mathf.Max(0.01f, MidQVerticalTrackingPeakKpa - MidQVerticalTrackingStartKpa));
            float midQFall = Mathf.Clamp01((MidQVerticalTrackingEndKpa - DynamicPressureKpa) /
                Mathf.Max(0.01f, MidQVerticalTrackingEndKpa - MidQVerticalTrackingPeakKpa));
            float midQPressureBlend = Mathf.Min(midQRise, midQFall);
            bool midQTracking = Mathf.Abs(EffectiveTargetVerticalSpeedMps) > ZeroVsTargetBandMps &&
                !ManualZeroVsTrajectoryBrakeActive && !PrecisionBasePitchActive;
            float midQErrorBlend = 1f - Mathf.Clamp01((Mathf.Abs(effectiveError) - MidQVerticalTrackingEntryErrorMps) /
                Mathf.Max(0.01f, MidQVerticalTrackingExitErrorMps - MidQVerticalTrackingEntryErrorMps));
            MidQVerticalTrackingBlend = midQTracking ? midQPressureBlend * midQErrorBlend : 0f;
            MidQVerticalTrackingFilterActive = MidQVerticalTrackingBlend > 0.001f;
            MidQProportionalScale = Mathf.Lerp(1f, MidQProportionalScaleTarget, MidQVerticalTrackingBlend);
            MidQDampingScale = Mathf.Lerp(1f, MidQDampingScaleTarget, MidQVerticalTrackingBlend);
            MidQDampingLimitDeg = MidQVerticalTrackingFilterActive ? MidQDampingLimitDegTarget : 0f;
            MidQPitchSlewScale = Mathf.Lerp(1f, MidQPitchSlewScaleTarget, MidQVerticalTrackingBlend);
            MidQDirectRateScale = Mathf.Lerp(1f, MidQDirectRateScaleTarget, MidQVerticalTrackingBlend);
            MidQRateCommandSlewScale = Mathf.Lerp(1f, MidQRateCommandSlewScaleTarget, MidQVerticalTrackingBlend);
            MidQBasePitchDampingScale = Mathf.Lerp(1f, MidQBasePitchDampingScaleTarget, MidQVerticalTrackingBlend);

            // Unlike the earlier phase-specific q filters, this envelope deliberately spans
            // PrecisionCapture and MainTrajectory.  The blend fades continuously with
            // V/S residual; therefore passing an old phase/error threshold cannot suddenly
            // release a large AA-native pitch-rate command into the aircraft.
            VerticalTrackingFilteredAccelerationMps2 = Mathf.Lerp(
                VerticalTrackingFilteredAccelerationMps2, VerticalAccelerationMps2,
                VerticalTrackingAccelerationFilter);
            bool verticalTrackingEnvelopeEligible =
                Mathf.Abs(EffectiveTargetVerticalSpeedMps) > ZeroVsTargetBandMps &&
                !ManualZeroVsTrajectoryBrakeActive;
            float verticalTrackingErrorBlend = 1f - Mathf.Clamp01(
                (Mathf.Abs(effectiveError) - VerticalTrackingEnvelopeEntryErrorMps) /
                Mathf.Max(0.01f, VerticalTrackingEnvelopeExitErrorMps -
                    VerticalTrackingEnvelopeEntryErrorMps));
            VerticalTrackingRateEnvelopeBlend = verticalTrackingEnvelopeEligible
                ? verticalTrackingErrorBlend : 0f;
            VerticalTrackingRateEnvelopeActive = VerticalTrackingRateEnvelopeBlend > 0.001f;
            VerticalTrackingPitchSlewScale = Mathf.Lerp(1f,
                VerticalTrackingPitchSlewScaleTarget, VerticalTrackingRateEnvelopeBlend);
            VerticalTrackingAttitudeRateDampingScale = Mathf.Lerp(1f,
                VerticalTrackingAttitudeRateDampingScaleTarget, VerticalTrackingRateEnvelopeBlend);
            VerticalTrackingRateLimitDegPerSec = GetVerticalTrackingRateLimitForPressure(DynamicPressureKpa);
            VerticalTrackingRateSlewDegPerSec2 = GetVerticalTrackingRateSlewForPressure(DynamicPressureKpa);
            VerticalTrackingRateReversalGateActive = false;
            VerticalTrackingDampingDominanceLimitDeg = 0f;

            bool manualZeroVsTarget = manualZeroVsFinalTarget;
            if (manualZeroVsTarget && manualZeroVsTransitionGuardPending)
            {
                ManualZeroVsTransitionGuardRemainingSeconds = ManualZeroVsTransitionGuardDurationSeconds;
                manualZeroVsTransitionGuardPending = false;
            }
            if (!manualZeroVsTarget)
            {
                ManualZeroVsTransitionGuardRemainingSeconds = 0f;
                ManualZeroVsTransitionGuardFromMps = 0f;
                manualZeroVsTransitionGuardPending = false;
            }

            float lowErrorBlend = 1f - Mathf.Clamp01((Mathf.Abs(effectiveError) - HighQManualZeroVsEntryErrorMps) /
                Mathf.Max(0.01f, HighQManualZeroVsExitErrorMps - HighQManualZeroVsEntryErrorMps));
            HighQManualZeroVsBlend = manualZeroVsTarget
                ? DynamicPressureHighQSchedule * lowErrorBlend : 0f;
            HighQManualZeroVsProfileActive = HighQManualZeroVsBlend > 0.001f;
            // During a V/S=0 capture, apply a weaker high-q acceleration guard over a wider
            // error corridor. This is intentionally disabled for ALT and non-zero V/S targets.
            float captureErrorBlend = 1f - Mathf.Clamp01(Mathf.Abs(effectiveError) /
                Mathf.Max(0.01f, HighQManualZeroVsCaptureGuardExitErrorMps));
            HighQManualZeroVsCaptureGuardBlend = manualZeroVsTarget
                ? DynamicPressureHighQSchedule * captureErrorBlend : 0f;
            HighQManualZeroVsCaptureGuardActive = HighQManualZeroVsCaptureGuardBlend > 0.001f;

            // Explicit large manual V/S -> zero changes need protection before the residual
            // falls into the old capture-error corridor. Use a lower, dedicated q onset only
            // for this short transition; this preserves all ALT and non-zero V/S authority.
            ManualZeroVsTransitionGuardPressureBlend = Mathf.Clamp01((DynamicPressureKpa - ManualZeroVsTransitionGuardStartKpa) /
                Mathf.Max(0.01f, ManualZeroVsTransitionGuardFullKpa - ManualZeroVsTransitionGuardStartKpa));
            ManualZeroVsTransitionGuardBlend = manualZeroVsTarget && ManualZeroVsTransitionGuardRemainingSeconds > 0f
                ? ManualZeroVsTransitionGuardPressureBlend : 0f;
            ManualZeroVsTransitionGuardActive = ManualZeroVsTransitionGuardBlend > 0.001f;
            if (manualZeroVsTarget && ManualZeroVsTransitionGuardRemainingSeconds > 0f)
                ManualZeroVsTransitionGuardRemainingSeconds = Mathf.Max(0f, ManualZeroVsTransitionGuardRemainingSeconds - Time.fixedDeltaTime);

            EffectiveErrorReversalBandMps = Mathf.Lerp(ErrorReversalBandMps,
                HighQManualZeroVsErrorReversalBandMps, HighQManualZeroVsBlend);
            HighQProportionalScale = Mathf.Lerp(1f, HighQManualZeroVsProportionalScale, HighQManualZeroVsBlend);
            float dampingBlend = Mathf.Max(HighQManualZeroVsBlend,
                Mathf.Max(HighQManualZeroVsCaptureGuardBlend, ManualZeroVsTransitionGuardBlend));
            float dampingScaleTarget = 1f;
            float dampingLimitTarget = 0f;
            if (HighQManualZeroVsCaptureGuardActive)
            {
                dampingScaleTarget = Mathf.Min(dampingScaleTarget, HighQManualZeroVsCaptureGuardDampingScale);
                dampingLimitTarget = HighQManualZeroVsCaptureGuardDampingLimitDeg;
            }
            if (ManualZeroVsTransitionGuardActive)
            {
                dampingScaleTarget = Mathf.Min(dampingScaleTarget, ManualZeroVsTransitionGuardDampingScale);
                dampingLimitTarget = dampingLimitTarget > 0f
                    ? Mathf.Min(dampingLimitTarget, ManualZeroVsTransitionGuardDampingLimitDeg)
                    : ManualZeroVsTransitionGuardDampingLimitDeg;
            }
            if (HighQManualZeroVsProfileActive)
            {
                dampingScaleTarget = Mathf.Min(dampingScaleTarget, HighQManualZeroVsDampingScale);
                dampingLimitTarget = dampingLimitTarget > 0f
                    ? Mathf.Min(dampingLimitTarget, HighQManualZeroVsDampingLimitDeg)
                    : HighQManualZeroVsDampingLimitDeg;
            }
            if (HighQNonZeroVsPrecisionCaptureProfileActive)
            {
                dampingScaleTarget = Mathf.Min(dampingScaleTarget, HighQNonZeroVsPrecisionDampingScaleTarget);
                dampingLimitTarget = dampingLimitTarget > 0f
                    ? Mathf.Min(dampingLimitTarget, HighQNonZeroVsPrecisionDampingLimitDegTarget)
                    : HighQNonZeroVsPrecisionDampingLimitDegTarget;
            }
            if (HighQNonZeroVsTrackingProfileActive)
            {
                dampingScaleTarget = Mathf.Min(dampingScaleTarget, HighQNonZeroVsTrackingDampingScaleTarget);
                dampingLimitTarget = dampingLimitTarget > 0f
                    ? Mathf.Min(dampingLimitTarget, HighQNonZeroVsTrackingDampingLimitDegTarget)
                    : HighQNonZeroVsTrackingDampingLimitDegTarget;
            }
            dampingBlend = Mathf.Max(dampingBlend, HighQNonZeroVsPrecisionCaptureBlend);
            if (MidQVerticalTrackingFilterActive)
            {
                dampingScaleTarget = Mathf.Min(dampingScaleTarget, MidQDampingScaleTarget);
                dampingLimitTarget = dampingLimitTarget > 0f
                    ? Mathf.Min(dampingLimitTarget, MidQDampingLimitDegTarget)
                    : MidQDampingLimitDegTarget;
            }
            dampingBlend = Mathf.Max(dampingBlend, HighQNonZeroVsTrackingBlend);
            dampingBlend = Mathf.Max(dampingBlend, MidQVerticalTrackingBlend);
            HighQDampingScale = Mathf.Lerp(1f, dampingScaleTarget, dampingBlend);
            HighQDampingLimitDeg = dampingLimitTarget;
            float manualZeroPitchSlewScale = Mathf.Lerp(1f, HighQManualZeroVsPitchSlewScale, HighQManualZeroVsBlend);
            HighQPitchSlewScale = Mathf.Min(manualZeroPitchSlewScale,
                Mathf.Min(HighQNonZeroVsTrackingPitchSlewScale, MidQPitchSlewScale));

            // v0.4.69: rebuild the V/S outer loop around a simple PD core.
            // Large V/S errors are handled by proportional authority plus acceleration damping.
            // Trim is deliberately restricted to the final capture band so it cannot retain
            // multi-degree bias during a large command or after a target change.
            // Do not reset the equilibrium learner on a one-sample sign flip near zero.
            // v0.5.2 logged repeated +/-0.03 m/s changes at an ALT/V/S endpoint; those
            // crossed the old deadband and repeatedly tore down PrecisionHold.
            bool errorReversed = !ManualZeroVsTrajectoryBrakeActive && havePreviousEffectiveError &&
                                 Mathf.Sign(effectiveError) != Mathf.Sign(previousEffectiveErrorMps) &&
                                 Mathf.Abs(effectiveError) >= EffectiveErrorReversalBandMps &&
                                 Mathf.Abs(previousEffectiveErrorMps) >= EffectiveErrorReversalBandMps;
            if (targetChangedSinceUpdate || errorReversed)
            {
                // A changed target should not inherit a sustained bias for the old V/S.
                // On the first post-takeoff frame pitch.CurrentPitch can still be the
                // ground-arm observation because PITCH has not executed yet.  Use the
                // live instrument and keep the generated trajectory continuous.
                float reanchorPitchDeg = postTakeoffTrajectorySeedPending
                    ? attitude.InstrumentPitchDeg : pitch.CurrentPitch;
                VerticalSpeedBasePitchDeg = reanchorPitchDeg;
                if (postTakeoffTrajectorySeedPending)
                    GeneratedPitchTargetDeg = reanchorPitchDeg;
                BasePitchAdaptContributionDeg = 0f;
                BasePitchSpeedAdaptContributionDeg = 0f;
                VerticalSpeedTrimDeg = 0f;
                precisionPushDeg = 0f;
                PrecisionBasePitchActive = false;
                PrecisionWithinTarget = false;
                PrecisionWithinTargetElapsedSeconds = 0f;
                PrecisionBasePitchRateDegPerSec = 0f;
                PrecisionBasePitchAdaptContributionDeg = 0f;
                PrecisionTrimTransferDeg = 0f;
                ResetPrecisionPhaseState("MainTrajectory");
                targetChangedSinceUpdate = false;
                postTakeoffTrajectorySeedPending = false;
            }

            // v0.4.97: precision-state hysteresis.  Enter only through the original tight
            // quiet gate, but remain latched through a modestly wider exit band/dwell.
            // This removes boundary chatter without increasing gain or changing the V/S
            // pitch trajectory itself.
            // ALT terminal trim is intentionally slow and continuous.  Let it keep the
            // V/S equilibrium state through small target changes instead of re-entering
            // the capture state for every centimetre-scale altitude correction.
            float precisionEntryBand = AltitudePrecisionHoldActive ? 0.22f : PrecisionEntryBandMps;
            float precisionExitBand = AltitudePrecisionHoldActive ? 0.35f : PrecisionExitBandMps;
            float precisionEntryAccelBand = AltitudePrecisionHoldActive ? 0.65f : PrecisionEntryAccelerationBandMps2;
            float precisionExitAccelBand = AltitudePrecisionHoldActive ? 0.85f : PrecisionExitAccelerationBandMps2;
            bool precisionEntryCandidate = !ManualZeroVsTrajectoryBrakeActive && !PitchTargetSaturated && !errorReversed &&
                                           Mathf.Abs(effectiveError) <= precisionEntryBand &&
                                           Mathf.Abs(VerticalAccelerationMps2) <= precisionEntryAccelBand;
            bool precisionRetentionCandidate = !ManualZeroVsTrajectoryBrakeActive && !PitchTargetSaturated && !errorReversed &&
                                               Mathf.Abs(effectiveError) <= precisionExitBand &&
                                               Mathf.Abs(VerticalAccelerationMps2) <= precisionExitAccelBand;
            bool wasPrecisionActive = PrecisionBasePitchActive;
            if (!PrecisionBasePitchActive)
            {
                precisionEntryElapsedSeconds = precisionEntryCandidate
                    ? precisionEntryElapsedSeconds + Time.fixedDeltaTime : 0f;
                PrecisionExitElapsedSeconds = 0f;
                if (precisionEntryElapsedSeconds >= PrecisionEntryDwellSeconds)
                    PrecisionBasePitchActive = true;
            }
            else
            {
                precisionEntryElapsedSeconds = 0f;
                PrecisionExitElapsedSeconds = precisionRetentionCandidate
                    ? 0f : PrecisionExitElapsedSeconds + Time.fixedDeltaTime;
                if (PrecisionExitElapsedSeconds >= PrecisionExitDwellSeconds)
                {
                    PrecisionBasePitchActive = false;
                    PrecisionExitElapsedSeconds = 0f;
                }
            }

            bool holdEntryCandidate = !ManualZeroVsTrajectoryBrakeActive && PrecisionBasePitchActive &&
                                      Mathf.Abs(ControlVerticalSpeedErrorMps) <= PrecisionTargetToleranceMps &&
                                      Mathf.Abs(VerticalAccelerationMps2) <= PrecisionEntryAccelerationBandMps2;
            bool holdRetentionCandidate = !ManualZeroVsTrajectoryBrakeActive && PrecisionBasePitchActive &&
                                          Mathf.Abs(ControlVerticalSpeedErrorMps) <= PrecisionHoldExitToleranceMps &&
                                          Mathf.Abs(VerticalAccelerationMps2) <= PrecisionHoldExitAccelerationBandMps2;
            if (!PrecisionWithinTarget)
            {
                PrecisionHoldEntryElapsedSeconds = holdEntryCandidate
                    ? PrecisionHoldEntryElapsedSeconds + Time.fixedDeltaTime : 0f;
                PrecisionHoldExitElapsedSeconds = 0f;
                if (PrecisionHoldEntryElapsedSeconds >= PrecisionHoldEntryDwellSeconds)
                    PrecisionWithinTarget = true;
            }
            else
            {
                PrecisionHoldEntryElapsedSeconds = 0f;
                PrecisionHoldExitElapsedSeconds = holdRetentionCandidate
                    ? 0f : PrecisionHoldExitElapsedSeconds + Time.fixedDeltaTime;
                if (PrecisionHoldExitElapsedSeconds >= PrecisionHoldExitDwellSeconds)
                {
                    PrecisionWithinTarget = false;
                    PrecisionHoldExitElapsedSeconds = 0f;
                }
            }
            if (!PrecisionBasePitchActive)
            {
                PrecisionWithinTarget = false;
                PrecisionHoldEntryElapsedSeconds = 0f;
                PrecisionHoldExitElapsedSeconds = 0f;
            }
            // ALT final trim owns position, while V/S still owns the pitch/BasePitch
            // equilibrium.  v0.5.3 incorrectly forced every ALT precision request into
            // the weakest V/S hold profile, even when actual V/S remained materially away
            // from the small requested rate.  Track that residual with a bounded middle
            // profile; only the final +/- band uses the quiet equilibrium gain.
            bool altitudePrecisionTrackingContext = AltitudePrecisionHoldActive && PrecisionBasePitchActive;
            float altitudePrecisionTrackingAbsError = Mathf.Abs(effectiveError);
            if (!altitudePrecisionTrackingContext)
            {
                ResetAltitudePrecisionTrackingState();
            }
            else if (!AltitudePrecisionTrackingActive)
            {
                AltitudePrecisionTrackingEnterElapsedSeconds =
                    altitudePrecisionTrackingAbsError >= AltitudePrecisionTrackingEnterBandMps
                        ? AltitudePrecisionTrackingEnterElapsedSeconds + Time.fixedDeltaTime : 0f;
                AltitudePrecisionTrackingExitElapsedSeconds = 0f;
                if (AltitudePrecisionTrackingEnterElapsedSeconds >= AltitudePrecisionTrackingEnterDwellSeconds)
                {
                    AltitudePrecisionTrackingActive = true;
                    AltitudePrecisionTrackingEnterElapsedSeconds = 0f;
                }
            }
            else
            {
                AltitudePrecisionTrackingEnterElapsedSeconds = 0f;
                AltitudePrecisionTrackingExitElapsedSeconds =
                    altitudePrecisionTrackingAbsError <= AltitudePrecisionTrackingExitBandMps
                        ? AltitudePrecisionTrackingExitElapsedSeconds + Time.fixedDeltaTime : 0f;
                if (AltitudePrecisionTrackingExitElapsedSeconds >= AltitudePrecisionTrackingExitDwellSeconds)
                {
                    AltitudePrecisionTrackingActive = false;
                    AltitudePrecisionTrackingExitElapsedSeconds = 0f;
                }
            }
            PrecisionCapturePhase = PrecisionBasePitchActive && !PrecisionWithinTarget &&
                                    (!AltitudePrecisionHoldActive || AltitudePrecisionTrackingActive);
            SetPrecisionPhase(!PrecisionBasePitchActive ? "MainTrajectory" :
                (AltitudePrecisionTrackingActive ? "AltitudePrecisionTrack" :
                (AltitudePrecisionHoldActive ? "AltitudePrecisionHold" :
                (PrecisionWithinTarget ? "PrecisionHold" : "PrecisionCapture"))));
            PrecisionActiveRateGainDegPerMpsSec = PrecisionBasePitchActive
                ? (AltitudePrecisionTrackingActive ? AltitudePrecisionTrackingRateGainDegPerMpsSec :
                   (PrecisionCapturePhase ? PrecisionCaptureRateGainDegPerMpsSec : PrecisionBasePitchRateGainDegPerMpsSec))
                : 0f;
            PrecisionActiveAccelerationDampingDegPerMps2Sec = PrecisionBasePitchActive
                ? (AltitudePrecisionTrackingActive ? AltitudePrecisionTrackingAccelerationDampingDegPerMps2Sec :
                   (PrecisionCapturePhase ? PrecisionCaptureAccelerationDampingDegPerMps2Sec : PrecisionBasePitchAccelerationDampingDegPerMps2Sec))
                : 0f;
            PrecisionActiveRateLimitDegPerSec = PrecisionBasePitchActive
                ? (AltitudePrecisionTrackingActive ? AltitudePrecisionTrackingRateLimitDegPerSec :
                   (PrecisionCapturePhase ? PrecisionCaptureRateLimitDegPerSec : PrecisionBasePitchRateLimitDegPerSec))
                : 0f;

            float altitudeQuietingTarget = altitudePrecisionTrackingContext && LowQVerticalEnvelopeActive
                ? LowQVerticalEnvelopeBlend : 0f;
            float altitudeQuietingTime = altitudeQuietingTarget > AltitudeLowQPrecisionQuietingBlend
                ? AltitudeLowQPrecisionQuietingAttackSeconds : AltitudeLowQPrecisionQuietingReleaseSeconds;
            AltitudeLowQPrecisionQuietingBlend = Mathf.MoveTowards(
                AltitudeLowQPrecisionQuietingBlend,
                altitudeQuietingTarget,
                Time.fixedDeltaTime / Mathf.Max(0.01f, altitudeQuietingTime));
            AltitudeLowQPrecisionQuietingActive = AltitudeLowQPrecisionQuietingBlend > 0.001f;

            // Recover only the rate ceiling as the residual approaches the generic
            // 0.35 m/s precision-retention boundary.  Full authority is restored by
            // 0.30 m/s, leaving a 0.05 m/s dynamic margin.  The damping quieting uses
            // the original low-q blend independently, so authority recovery cannot
            // remove the stabilizing acceleration term at the same instant.
            AltitudeLowQPrecisionRateAuthorityRecoveryBlend =
                AltitudePrecisionHoldActive && LowQVerticalEnvelopeActive
                    ? Mathf.Clamp01((Mathf.Abs(effectiveError) -
                        AltitudeLowQPrecisionRateRecoveryStartErrorMps) /
                        Mathf.Max(0.01f, AltitudeLowQPrecisionRateRecoveryFullErrorMps -
                            AltitudeLowQPrecisionRateRecoveryStartErrorMps))
                    : 0f;
            float altitudeRateSuppressionBlend = AltitudeLowQPrecisionQuietingBlend *
                (1f - AltitudeLowQPrecisionRateAuthorityRecoveryBlend);
            AltitudeLowQPrecisionQuietingRateScale = Mathf.Lerp(1f,
                AltitudeLowQPrecisionQuietingRateScaleTarget, altitudeRateSuppressionBlend);
            AltitudeLowQPrecisionQuietingDampingScale = Mathf.Lerp(1f,
                AltitudeLowQPrecisionQuietingDampingScaleTarget, AltitudeLowQPrecisionQuietingBlend);
            AltitudeLowQPrecisionEffectiveRateLimitDegPerSec = PrecisionBasePitchActive
                ? PrecisionActiveRateLimitDegPerSec * LowQBasePitchAdaptScale *
                    AltitudeLowQPrecisionQuietingRateScale
                : 0f;
            if (PrecisionWithinTarget)
                PrecisionWithinTargetElapsedSeconds += Time.fixedDeltaTime;
            else
                PrecisionWithinTargetElapsedSeconds = 0f;
            PrecisionTrimTransferDeg = 0f;
            if (PrecisionBasePitchActive && !wasPrecisionActive && Mathf.Abs(VerticalSpeedTrimDeg) > 0.0001f)
            {
                // Preserve the total commanded pitch while moving the former temporary
                // trim into the long-lived equilibrium reference.
                float transferred = VerticalSpeedTrimDeg;
                VerticalSpeedBasePitchDeg = Mathf.Clamp(VerticalSpeedBasePitchDeg + transferred,
                    -Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg),
                     Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg));
                VerticalSpeedTrimDeg = 0f;
                PrecisionTrimTransferDeg = transferred;
            }

            PrecisionTrimContributionDeg = 0f;
            bool inTrimBand = !PrecisionBasePitchActive &&
                              Mathf.Abs(effectiveError) > EffectiveVerticalSpeedDeadbandMps &&
                              Mathf.Abs(effectiveError) <= VerticalSpeedHoldBandMps &&
                              Mathf.Abs(VerticalAccelerationMps2) <= 0.75f;
            if (inTrimBand)
            {
                float trimStep = effectiveError * VerticalSpeedTrimGainDegPerMpsSec * Time.fixedDeltaTime;
                VerticalSpeedTrimDeg += trimStep;
                VerticalSpeedTrimDeg = Mathf.Clamp(VerticalSpeedTrimDeg, -MaxVerticalSpeedTrimDeg, MaxVerticalSpeedTrimDeg);
                PrecisionTrimContributionDeg = trimStep;
            }
            else if (!PrecisionBasePitchActive)
            {
                // Outside final capture, eliminate any stale trim quickly.
                VerticalSpeedTrimDeg = Mathf.MoveTowards(VerticalSpeedTrimDeg, 0f, 1.8f * Time.fixedDeltaTime);
            }
            previousEffectiveErrorMps = effectiveError;
            havePreviousEffectiveError = true;

            // Prediction remains diagnostic/lightweight only: it supports a short brake cue,
            // not an early dominant controller that can make capture slow.
            PredictedVerticalSpeedMps = CurrentVerticalSpeedMps + VerticalAccelerationMps2 * VerticalSpeedBrakePredictionSeconds;
            PredictedStopErrorMps = ControlTargetVerticalSpeedMps - PredictedVerticalSpeedMps;
            bool approachingOvershoot = Mathf.Abs(effectiveError) > EffectiveVerticalSpeedDeadbandMps &&
                                       Mathf.Sign(ControlVerticalSpeedErrorMps) != Mathf.Sign(PredictedStopErrorMps);
            OvershootLatchActive = approachingOvershoot || errorReversed;
            BrakeContributionDeg = approachingOvershoot
                ? -Mathf.Sign(ControlVerticalSpeedErrorMps) * Mathf.Min(Mathf.Abs(effectiveError), Mathf.Abs(PredictedStopErrorMps)) * VerticalSpeedBrakeGainDegPerMps
                : 0f;
            RecoveryContributionDeg = errorReversed ? effectiveError * OvershootRecoveryGainDegPerMps : 0f;
            precisionPushDeg = 0f;

            ProportionalContributionDeg = effectiveError * VerticalSpeedGainDegPerMps *
                HighQProportionalScale * LowQProportionalScale * MidQProportionalScale;
            float dampingAccelerationForControlMps2 = HighQNonZeroVsPrecisionCaptureProfileActive
                ? Mathf.Lerp(VerticalAccelerationMps2, HighQNonZeroVsPrecisionFilteredAccelerationMps2,
                    HighQNonZeroVsPrecisionCaptureBlend)
                : VerticalAccelerationMps2;
            if (HighQNonZeroVsTrackingProfileActive)
            {
                dampingAccelerationForControlMps2 = Mathf.Lerp(dampingAccelerationForControlMps2,
                    HighQNonZeroVsTrackingFilteredAccelerationMps2, HighQNonZeroVsTrackingBlend);
            }
            if (MidQVerticalTrackingFilterActive)
            {
                dampingAccelerationForControlMps2 = Mathf.Lerp(dampingAccelerationForControlMps2,
                    MidQFilteredAccelerationMps2, MidQVerticalTrackingBlend);
            }
            if (LowQVerticalEnvelopeActive)
            {
                dampingAccelerationForControlMps2 = Mathf.Lerp(dampingAccelerationForControlMps2,
                    LowQFilteredAccelerationMps2, LowQVerticalEnvelopeBlend);
            }
            if (VerticalTrackingRateEnvelopeActive)
            {
                dampingAccelerationForControlMps2 = Mathf.Lerp(dampingAccelerationForControlMps2,
                    VerticalTrackingFilteredAccelerationMps2, VerticalTrackingRateEnvelopeBlend);
            }
            float rawDampingContributionDeg = -dampingAccelerationForControlMps2 *
                VerticalAccelerationDampingDegPerMps2 * HighQDampingScale * LowQDampingScale;
            float dampingLimitDeg = HighQDampingLimitDeg;
            if (LowQDampingLimitDeg > 0.0001f)
            {
                dampingLimitDeg = dampingLimitDeg > 0.0001f
                    ? Mathf.Min(dampingLimitDeg, LowQDampingLimitDeg)
                    : LowQDampingLimitDeg;
            }
            DampingContributionDeg = dampingLimitDeg > 0.0001f
                ? Mathf.Clamp(rawDampingContributionDeg, -dampingLimitDeg, dampingLimitDeg)
                : rawDampingContributionDeg;
            // A filtered D term may still be numerically valid while dominating the very
            // small motion error it is supposed to damp. Bound it relative to the live P
            // correction in the near-target corridor; this preserves braking authority for
            // real misses while preventing the D term from flipping the pitch trajectory alone.
            if (VerticalTrackingRateEnvelopeActive)
            {
                VerticalTrackingDampingDominanceLimitDeg = Mathf.Max(
                    VerticalTrackingDampingDominanceFloorDeg,
                    Mathf.Abs(ProportionalContributionDeg) * VerticalTrackingDampingDominancePScale +
                    VerticalTrackingDampingDominanceFloorDeg);
                DampingContributionDeg = Mathf.Clamp(DampingContributionDeg,
                    -VerticalTrackingDampingDominanceLimitDeg,
                     VerticalTrackingDampingDominanceLimitDeg);
            }

            BrakeContributionDeg = 0f;
            // direction. This is the anti-windup guard for the V/S safety pitch envelope.
            BasePitchAdaptContributionDeg = 0f;
            PrecisionBasePitchAdaptContributionDeg = 0f;
            PrecisionBasePitchRateDegPerSec = 0f;
            ZeroVsBasePitchAdaptContributionDeg = 0f;
            UpdateVsCruiseAccelerationGuide(effectiveError);
            bool canAdapt = Mathf.Abs(effectiveError) > EffectiveVerticalSpeedDeadbandMps ||
                (VsCruiseAccelerationGuideActive &&
                 Mathf.Abs(VsCruiseBasePitchRateCommandDegPerSec) > 0.001f);
            if (canAdapt)
            {
                float proposed = 0f;
                if (PrecisionBasePitchActive)
                {
                    // In the calm capture corridor, learn the equilibrium BasePitch more
                    // decisively than the broad-capture learner.  The rate is bounded and
                    // includes a small vertical-acceleration brake, so it cannot become a
                    // high-frequency V/S controller.
                    float precisionError = Mathf.Abs(effectiveError) <= PrecisionNeutralBandMps ? 0f : effectiveError;
                    // Capture and hold deliberately use different authority.  In capture,
                    // strong acceleration damping prevents a late extra BasePitch push while
                    // the V/S is already moving toward target.  In hold, the gentle legacy
                    // rate preserves a quiet equilibrium.
                    float precisionAccelerationForBasePitchMps2 = HighQNonZeroVsPrecisionCaptureProfileActive
                        ? Mathf.Lerp(VerticalAccelerationMps2, HighQNonZeroVsPrecisionFilteredAccelerationMps2,
                            HighQNonZeroVsPrecisionCaptureBlend)
                        : VerticalAccelerationMps2;
                    if (HighQNonZeroVsTrackingProfileActive)
                    {
                        precisionAccelerationForBasePitchMps2 = Mathf.Lerp(precisionAccelerationForBasePitchMps2,
                            HighQNonZeroVsTrackingFilteredAccelerationMps2, HighQNonZeroVsTrackingBlend);
                    }
                    if (LowQVerticalEnvelopeActive)
                    {
                        precisionAccelerationForBasePitchMps2 = Mathf.Lerp(precisionAccelerationForBasePitchMps2,
                            LowQFilteredAccelerationMps2, LowQVerticalEnvelopeBlend);
                    }
                    float precisionAccelerationDampingGain = PrecisionActiveAccelerationDampingDegPerMps2Sec *
                        HighQNonZeroVsPrecisionBasePitchDampingScale *
                        HighQNonZeroVsTrackingBasePitchDampingScale * MidQBasePitchDampingScale * LowQDampingScale *
                        (AltitudePrecisionHoldActive ? AltitudeLowQPrecisionQuietingDampingScale : 1f);
                    PrecisionBasePitchRateDegPerSec = precisionError * PrecisionActiveRateGainDegPerMpsSec
                        - precisionAccelerationForBasePitchMps2 * precisionAccelerationDampingGain;
                    float lowQPrecisionRateLimit = PrecisionActiveRateLimitDegPerSec * LowQBasePitchAdaptScale *
                        (AltitudePrecisionHoldActive ? AltitudeLowQPrecisionQuietingRateScale : 1f);
                    AltitudeLowQPrecisionEffectiveRateLimitDegPerSec = AltitudePrecisionHoldActive
                        ? lowQPrecisionRateLimit : 0f;
                    PrecisionBasePitchRateDegPerSec = Mathf.Clamp(PrecisionBasePitchRateDegPerSec,
                        -lowQPrecisionRateLimit, lowQPrecisionRateLimit);
                    proposed = PrecisionBasePitchRateDegPerSec * Time.fixedDeltaTime;
                }
                else
                {
                    // Do not entirely freeze BasePitch while the aircraft is accelerating;
                    // that starves low-speed / large-command authority. Soften learning instead.
                    float fastBlend = Mathf.Clamp01((Mathf.Abs(effectiveError) - VerticalSpeedHoldBandMps) /
                                                    Mathf.Max(0.01f, BasePitchFastAdaptErrorMps - VerticalSpeedHoldBandMps));
                    float adaptGain = Mathf.Lerp(VerticalSpeedBasePitchAdaptGainDegPerMpsSec,
                                                   VerticalSpeedBasePitchFastAdaptGainDegPerMpsSec,
                                                   fastBlend);
                    float accelerationSoftening = 1f / (1f + Mathf.Abs(VerticalAccelerationMps2) * BasePitchAdaptAccelerationSoftening);
                    proposed = effectiveError * adaptGain * accelerationSoftening *
                        LowQBasePitchAdaptScale * Time.fixedDeltaTime;
                }

                VsCruiseLegacyBasePitchRateDegPerSec = Time.fixedDeltaTime > 0.0001f
                    ? proposed / Time.fixedDeltaTime : 0f;
                VsCruiseAppliedBasePitchRateDegPerSec = VsCruiseAccelerationGuideActive
                    ? Mathf.Lerp(VsCruiseLegacyBasePitchRateDegPerSec,
                        VsCruiseBasePitchRateCommandDegPerSec,
                        VsCruiseAccelerationGuideBlend)
                    : VsCruiseLegacyBasePitchRateDegPerSec;
                proposed = VsCruiseAppliedBasePitchRateDegPerSec * Time.fixedDeltaTime;
                bool blockedUp = PitchUpperSaturated && proposed > 0f;
                bool blockedDown = PitchLowerSaturated && proposed < 0f;
                if (!blockedUp && !blockedDown)
                {
                    VerticalSpeedBasePitchDeg = Mathf.Clamp(VerticalSpeedBasePitchDeg + proposed,
                        -Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg),
                         Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg));
                    if (PrecisionBasePitchActive)
                        PrecisionBasePitchAdaptContributionDeg = proposed;
                    else
                        BasePitchAdaptContributionDeg = proposed;
                }
            }
            else
            {
                VsCruiseLegacyBasePitchRateDegPerSec = 0f;
                VsCruiseAppliedBasePitchRateDegPerSec = 0f;
            }
            VsBasePitchHoldRateDegPerSec = VsCruiseAccelerationGuideActive
                ? VsCruiseAppliedBasePitchRateDegPerSec : PrecisionBasePitchRateDegPerSec;

            // At zero V/S, a residual of only a few tenths m/s slowly moves altitude.
            // In this calm narrow band adapt BasePitch more decisively, rather than
            // leaving the final 0.75 deg trim permanently saturated. Only V/S error
            // is used here; no altitude/position error is introduced.
            bool zeroVsHoldAdapt = !ManualZeroVsTrajectoryBrakeActive && !AltitudePrecisionHoldActive && !PrecisionBasePitchActive &&
                                    Mathf.Abs(EffectiveTargetVerticalSpeedMps) <= ZeroVsTargetBandMps &&
                                    Mathf.Abs(effectiveError) > EffectiveVerticalSpeedDeadbandMps &&
                                    Mathf.Abs(effectiveError) <= ZeroVsHoldAdaptErrorBandMps &&
                                    Mathf.Abs(VerticalAccelerationMps2) <= ZeroVsHoldAdaptAccelerationBandMps2;
            if (zeroVsHoldAdapt)
            {
                float zeroVsProposed = effectiveError * ZeroVsBasePitchAdaptGainDegPerMpsSec * Time.fixedDeltaTime;
                bool blockedZeroUp = PitchUpperSaturated && zeroVsProposed > 0f;
                bool blockedZeroDown = PitchLowerSaturated && zeroVsProposed < 0f;
                if (!blockedZeroUp && !blockedZeroDown)
                {
                    VerticalSpeedBasePitchDeg = Mathf.Clamp(VerticalSpeedBasePitchDeg + zeroVsProposed,
                        -Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg),
                         Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg));
                    ZeroVsBasePitchAdaptContributionDeg = zeroVsProposed;
                }
            }

            // Fixed non-zero V/S needs a smaller |pitch| as airspeed rises, and a larger
            // |pitch| as it falls.  At a zero-V/S command, however, this feed-forward must
            // be completely isolated: the precision BasePitch learner is the single owner
            // of final equilibrium.  v0.4.94 accidentally kept this path alive by falling
            // back to the current BasePitch sign, which opposed the precision learner while
            // the aircraft accelerated and made the last ~0.2 m/s take tens of seconds.
            BasePitchSpeedAdaptContributionDeg = 0f;
            BasePitchSpeedAdaptRateDegPerSec = 0f;
            BasePitchSpeedAdaptActive = false;
            bool nonZeroVsSpeedAdapt = Mathf.Abs(EffectiveTargetVerticalSpeedMps) > 0.10f;
            float referenceSign = nonZeroVsSpeedAdapt ? Mathf.Sign(EffectiveTargetVerticalSpeedMps) : 0f;
            if (nonZeroVsSpeedAdapt && Mathf.Abs(referenceSign) > 0.001f && Mathf.Abs(SurfaceSpeedRateMps2) > 0.02f)
            {
                float speedProposed = -referenceSign * SurfaceSpeedRateMps2 *
                    BasePitchSpeedAdaptGainDegPerMps2Sec * LowQBasePitchAdaptScale * Time.fixedDeltaTime;
                speedProposed = Mathf.Clamp(speedProposed, -0.10f * Time.fixedDeltaTime, 0.10f * Time.fixedDeltaTime);
                bool blockedSpeedUp = PitchUpperSaturated && speedProposed > 0f;
                bool blockedSpeedDown = PitchLowerSaturated && speedProposed < 0f;
                if (!blockedSpeedUp && !blockedSpeedDown)
                {
                    VerticalSpeedBasePitchDeg = Mathf.Clamp(VerticalSpeedBasePitchDeg + speedProposed,
                        -Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg),
                         Mathf.Min(MaxVerticalSpeedBasePitchDeg, EffectiveMaxPitchTargetDeg));
                    BasePitchSpeedAdaptContributionDeg = speedProposed;
                    BasePitchSpeedAdaptRateDegPerSec = Time.fixedDeltaTime > 0.0001f ? speedProposed / Time.fixedDeltaTime : 0f;
                    BasePitchSpeedAdaptActive = true;
                }
            }

            // Expose the actual terminal BasePitch trend after the non-zero V/S speed
            // feed-forward is included.  This is diagnostic-only and makes it clear whether
            // convergence is limited by the equilibrium learner or by changing airspeed.
            PrecisionNetBasePitchRateDegPerSec = (VsCruiseAccelerationGuideActive
                ? VsCruiseAppliedBasePitchRateDegPerSec
                : (PrecisionBasePitchActive ? PrecisionBasePitchRateDegPerSec : 0f))
                + BasePitchSpeedAdaptRateDegPerSec;

            DesiredPitchBeforeClampDeg = VerticalSpeedBasePitchDeg
                               + VerticalSpeedTrimDeg
                               + precisionPushDeg
                               + ProportionalContributionDeg
                               + DampingContributionDeg
                               + BrakeContributionDeg
                               + RecoveryContributionDeg;
            DesiredPitchAfterClampDeg = Mathf.Clamp(DesiredPitchBeforeClampDeg, -EffectiveMaxPitchTargetDeg, EffectiveMaxPitchTargetDeg);
            PitchUpperSaturated = DesiredPitchBeforeClampDeg > EffectiveMaxPitchTargetDeg + 0.0001f;
            PitchLowerSaturated = DesiredPitchBeforeClampDeg < -EffectiveMaxPitchTargetDeg - 0.0001f;
            PitchTargetSaturated = PitchUpperSaturated || PitchLowerSaturated;
            // Far from target, use the aircraft's available pitch authority. Near capture,
            // retain the gentler slew that made PITCH itself stable and smooth.
            float authorityBlend = Mathf.Clamp01((Mathf.Abs(effectiveError) - VerticalSpeedHoldBandMps) / 4.5f);
            float scheduledPitchSlew = Mathf.Lerp(4.2f, 10.0f, authorityBlend);
            HighQAppliedPitchSlewDegPerSec = scheduledPitchSlew * HighQPitchSlewScale * LowQPitchSlewScale;
            scheduledPitchSlew = HighQAppliedPitchSlewDegPerSec * VerticalTrackingPitchSlewScale;

            // v0.4.75: Do not alternate between a fixed hold and discrete release near capture.
            // That made the requested pitch move in small steps and could show up as a terminal flutter.
            // Keep V/S's internal pitch trajectory continuous and rate-limited. The terminal slew is
            // deliberately low, but it never stops adapting while a small V/S residual remains.
            TerminalQuietZoneActive = Mathf.Abs(effectiveError) <= TerminalQuietErrorBandMps &&
                                      Mathf.Abs(VerticalAccelerationMps2) <= TerminalQuietAccelerationBandMps2 &&
                                      !PitchTargetSaturated;
            TerminalQuietTargetDeltaDeg = DesiredPitchAfterClampDeg - GeneratedPitchTargetDeg;
            TerminalPitchTargetHeld = false;
            float appliedSlew = TerminalQuietZoneActive
                ? Mathf.Min(scheduledPitchSlew, TerminalQuietSlewDegPerSec)
                : scheduledPitchSlew;
            GeneratedPitchTargetDeg = Mathf.MoveTowards(
                GeneratedPitchTargetDeg,
                DesiredPitchAfterClampDeg,
                appliedSlew * Time.fixedDeltaTime);
            TerminalQuietHeldPitchTargetDeg = GeneratedPitchTargetDeg;
            terminalQuietHoldUntil = 0f;

            // v0.4.94 direct-rate V/S: the proven V/S outer trajectory above creates
            // GeneratedPitchTargetDeg.  V/S itself converts the attitude residual into
            // the AA-native pitch-rate demand, using the same calibrated PITCH rate law.
            // This avoids the v0.4.92 error->rate shortcut, whose extra plant lag caused
            // late arrival and large return oscillations.  No PITCH target is written.
            VsAttitudeErrorDeg = GeneratedPitchTargetDeg - attitude.InstrumentPitchDeg;
            float effectiveAttitudeRateDamping = VsAttitudePitchRateDamping *
                VerticalTrackingAttitudeRateDampingScale;
            float rawRateCommand = VsAttitudeErrorDeg * VsAttitudePitchErrorGain
                                 - attitude.InstrumentPitchRateDegPerSec * effectiveAttitudeRateDamping;
            float effectiveRateCommandLimit = VsAttitudeMaxRateCommand *
                LowQDirectRateScale * HighQNonZeroVsTrackingDirectRateScale * MidQDirectRateScale;
            rawRateCommand = Mathf.Clamp(rawRateCommand, -effectiveRateCommandLimit, effectiveRateCommandLimit);

            float nativeRatePerCommandDegPerSec = Mathf.Max(0.001f, pitch.NativePitchRatePerVirtualStickDegPerSec);
            VsAttitudeRateProportionalDegPerSec = VsAttitudeErrorDeg * VsAttitudePitchErrorGain * nativeRatePerCommandDegPerSec;
            VsAttitudeRateDampingDegPerSec = -attitude.InstrumentPitchRateDegPerSec * effectiveAttitudeRateDamping * nativeRatePerCommandDegPerSec;
            VsRateProportionalDegPerSec = VsAttitudeRateProportionalDegPerSec;
            VsRateDampingDegPerSec = VsAttitudeRateDampingDegPerSec;
            VsRateBrakeDegPerSec = 0f;
            VsBasePitchHoldRateDegPerSec = VsCruiseAccelerationGuideActive
                ? VsCruiseAppliedBasePitchRateDegPerSec : PrecisionBasePitchRateDegPerSec;
            float nominalRateLimitDegPerSec = effectiveRateCommandLimit * nativeRatePerCommandDegPerSec;
            float envelopeRateLimitDegPerSec = Mathf.Min(nominalRateLimitDegPerSec,
                VerticalTrackingRateLimitDegPerSec);
            float appliedRateLimitDegPerSec = Mathf.Lerp(nominalRateLimitDegPerSec,
                envelopeRateLimitDegPerSec, VerticalTrackingRateEnvelopeBlend);
            VsRateTargetDegPerSec = Mathf.Clamp(rawRateCommand * nativeRatePerCommandDegPerSec,
                -appliedRateLimitDegPerSec, appliedRateLimitDegPerSec);
            float nominalRateSlewDegPerSec2 = VsAttitudeCommandSlewPerSec *
                nativeRatePerCommandDegPerSec * LowQRateCommandSlewScale *
                HighQNonZeroVsTrackingRateCommandSlewScale * MidQRateCommandSlewScale;
            float envelopeRateSlewDegPerSec2 = Mathf.Min(nominalRateSlewDegPerSec2,
                VerticalTrackingRateSlewDegPerSec2);
            VsRateCommandSlewDegPerSec2 = Mathf.Lerp(nominalRateSlewDegPerSec2,
                envelopeRateSlewDegPerSec2, VerticalTrackingRateEnvelopeBlend);
            VerticalTrackingRateReversalGateActive = VerticalTrackingRateEnvelopeActive &&
                Mathf.Abs(PlannedPitchRateDegPerSec) > 1.0f &&
                Mathf.Abs(VsRateTargetDegPerSec) > 1.0f &&
                Mathf.Sign(PlannedPitchRateDegPerSec) != Mathf.Sign(VsRateTargetDegPerSec);
            if (VerticalTrackingRateReversalGateActive)
                VsRateCommandSlewDegPerSec2 *= VerticalTrackingReversalSlewScaleTarget;
            PlannedPitchRateDegPerSec = Mathf.MoveTowards(
                PlannedPitchRateDegPerSec,
                VsRateTargetDegPerSec,
                VsRateCommandSlewDegPerSec2 * Time.fixedDeltaTime);
            DirectRateScheme = VerticalTrackingRateEnvelopeActive
                ? "AttitudeTrajectoryNativeRate-ContinuousTrackingEnvelope"
                : (HighQNonZeroVsTrackingProfileActive
                    ? "AttitudeTrajectoryNativeRate-HighQTrackingStabilized"
                    : (MidQVerticalTrackingFilterActive
                        ? "AttitudeTrajectoryNativeRate-MidQContinuousStabilized"
                        : "AttitudeTrajectoryNativeRate-PhaseSeparatedPrecisionCapture-Hysteresis"));
            DirectPitchRateActive = true;
            pitch.SetVerticalSpeedRateDemand(PlannedPitchRateDegPerSec);

            BrakeState = PrecisionBasePitchActive
                ? (AltitudePrecisionHoldActive ? "AltitudePrecisionHold" :
                   (PrecisionWithinTarget ? "PrecisionHold" : "PrecisionCapture"))
                : (approachingOvershoot ? "Brake" : (errorReversed ? "Recover" : (Mathf.Abs(effectiveError) <= VerticalSpeedHoldBandMps ? "Hold" : "Capture")));
            ControlState = "DirectRate-" + BrakeState;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
