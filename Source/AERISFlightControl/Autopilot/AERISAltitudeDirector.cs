using UnityEngine;
using System;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Autopilot
{
    // ALT is the altitude analogue of HDG's outer trajectory director:
    // altitude error -> planned vertical-speed demand -> V/S trajectory director
    // -> AA native PitchAngularVelocityController.  ALT never writes pitch or
    // FlightCtrlState.  V/S retains ownership of BasePitch, vertical acceleration
    // damping, predictive capture, and the native pitch-rate handoff.
    internal sealed class AERISAltitudeDirector
    {
        internal bool Armed { get; private set; }
        internal bool ControlActive { get; private set; }
        internal string ControlState { get; private set; } = "Inactive";

        internal float TargetAltitudeMeters { get; private set; } = 1000f;
        internal string TargetAltitudeText = "1000";
        internal float CurrentAltitudeMeters { get; private set; }
        // Selected-target error remains target minus current for user-facing FDR/UI.
        internal float AltitudeErrorMeters { get; private set; }
        // v0.8.29: ALT uses a display-safe corridor around the selected target
        // and an empirically balanced reference slightly above it.  For a selected
        // 30000.00 m target, the corridor is 29999.51..30000.49 m and the shared
        // control reference is 30000.125 m.  The +0.125 m reference was selected
        // from the v0.8.28 endurance record: replaying the stable residual motion
        // around this reference predicts no 29999 or 30001 whole-metre display.
        // All trajectory, capture, PrecisionHold and Micro-Trim calculations use
        // AltitudeControlErrorMeters, so there is still no law switch at hold entry.
        internal float AltitudeHoldBandLowerOffsetMeters = -0.49f;
        internal float AltitudeHoldBandUpperOffsetMeters = 0.49f;
        internal float AltitudeHoldReferenceCommandOffsetMeters = 0.125f;
        internal float AltitudeHoldBandLowerMeters { get; private set; } = 999.51f;
        internal float AltitudeHoldBandUpperMeters { get; private set; } = 1000.49f;
        internal float AltitudeHoldReferenceMeters { get; private set; } = 1000.125f;
        internal float AltitudeHoldReferenceOffsetMeters { get; private set; } = 0.125f;
        internal float AltitudeControlErrorMeters { get; private set; }
        // Signed distance to the preferred band: positive below, negative above,
        // and exactly zero while the aircraft is inside the requested window.
        internal float AltitudeHoldBandErrorMeters { get; private set; }
        internal bool AltitudeInsidePreferredHoldBand { get; private set; }
        internal float CurrentVerticalSpeedMps { get; private set; }

        // v0.8.3 terminal altitude-reference reconciliation.
        //
        // KSP's instantaneous VerticalSpeed is the correct signal for the V/S inner
        // loop, but it can carry a stable offset relative to the derivative of the
        // same vessel.altitude reference used by ALT's position target.  This block
        // observes that offset from altitude only.  It operates only inside the final
        // terminal corridor, computes position control in the altitude reference frame,
        // then translates the small terminal request back into the V/S inner-loop frame.
        // It never writes pitch, modifies AA, or changes the main ALT capture trajectory.
        internal float AltitudeReferenceVerticalSpeedMps { get; private set; }
        internal float AltitudeReconciledVerticalSpeedMps { get; private set; }
        internal float AltitudeRateBiasMps { get; private set; }
        internal bool AltitudeRateReconciliationActive { get; private set; }
        internal float AltitudeRateReconciliationBlend { get; private set; }
        internal float AltitudeRateCommandBiasMps { get; private set; }
        internal float AltitudeRateReferenceFilterPerSec = 2.0f;
        internal float AltitudeRateBiasFilterPerSec = 1.2f;
        internal float AltitudeRateReconciliationStartBandMeters = 12f;
        internal float AltitudeRateReconciliationFullBandMeters = 3f;
        bool haveAltitudeReferenceSample;
        float lastAltitudeReferenceMeters;
        float lastAltitudeReferenceFixedTime;

        // The raw stopping-distance target and the acceleration-limited demand sent to V/S.
        internal float DesiredVerticalSpeedMps { get; private set; }
        internal float PlannedVerticalSpeedMps { get; private set; }
        internal float AltitudeRateDemandMps { get; private set; }
        internal float StoppingRateLimitMps { get; private set; }
        internal float StopDistanceMeters { get; private set; }
        internal float TransportLeadMeters { get; private set; }
        internal bool RolloutActive { get; private set; }
        internal bool HoldLatched { get; private set; }
        internal bool PrecisionCorrectionActive { get; private set; }
        internal float HoldEntryElapsedSeconds { get; private set; }
        internal float HoldExitElapsedSeconds { get; private set; }
        internal bool TargetChangedSinceUpdate { get; private set; }

        // v0.4.99 ALT envelope (retained in v0.5.0).  The ALT director owns target altitude, maximum V/S,
        // and an ALT-specific maximum pitch cap.  V/S applies the lower of this cap and
        // its manual V/S safety limit while ALT owns the external demand.
        internal float MaxAltitudeVerticalSpeedMps = 50f;
        internal string MaxAltitudeVerticalSpeedText = "50";
        internal float MaxAltitudePitchDeg = 20f;
        internal string MaxAltitudePitchText = "20";
        internal float AltitudeRateAccelLimitMps2 = 2.6f;
        internal float AltitudeRateBrakeAccelLimitMps2 = 3.4f;
        internal float ScheduledVerticalDecelMps2 = 2.4f;

        // v0.7.5 ALT AoA climb governor.
        // A large fixed +V/S demand can force a high sustained AoA on a fast aircraft
        // as dynamic pressure falls with altitude. This is not a final Protect override:
        // it is an ALT outer-trajectory envelope that reduces only a positive climb-rate
        // demand before V/S sees it. The V/S/PITCH/AA execution chain is unchanged.
        // Use only the AERIS geometric AoA estimate; it remains independent of AA and is
        // logged alongside the applied V/S cap for flight-test evidence.
        internal float AltitudeAoAClimbGovernorMinimumSurfaceSpeedMps = 250f;
        internal float AltitudeAoAClimbGovernorSoftLimitDeg = 5.5f;
        internal float AltitudeAoAClimbGovernorFullLimitDeg = 7.5f;
        internal float AltitudeAoAClimbGovernorMinimumVsMps = 10f;
        internal float AltitudeAoAClimbGovernorLimitDownRateMps2 = 6.0f;
        internal float AltitudeAoAClimbGovernorLimitRecoveryRateMps2 = 1.50f;
        internal float AltitudeAoAClimbGovernorAoAFilter = 0.08f;
        internal bool AoAClimbGovernorActive { get; private set; }
        internal bool AoAClimbGovernorAoAValid { get; private set; }
        internal float AoAClimbGovernorAoADeg { get; private set; }
        internal float AoAClimbGovernorBlend { get; private set; }
        internal float AoAClimbGovernorTargetVsCapMps { get; private set; }
        internal float AoAClimbGovernorAppliedVsCapMps { get; private set; }
        internal float AoAClimbGovernorOutputVsMps { get; private set; }
        internal float AoAClimbGovernorSurfaceSpeedMps { get; private set; }
        private bool aoaClimbGovernorInitialized;

        // v0.7.6 hypersonic low-q vertical envelope. The v0.7.5 AoA governor reacted
        // to a high-AoA result after the low-q V/S loop had already saturated. It is
        // retained only as disabled observer telemetry below. This envelope instead
        // schedules ALT's positive V/S trajectory directly from dynamic pressure.
        internal float LowQVerticalEnvelopeStartKpa = 24f;
        internal float LowQVerticalEnvelopeFullKpa = 12f;
        internal float LowQVerticalEnvelopeMinimumVsMps = 12f;
        internal float LowQVerticalEnvelopeAccelLimitMps2 = 0.75f;
        internal float LowQVerticalEnvelopeBrakeAccelLimitMps2 = 0.95f;
        // v0.8.0: the ALT stopping-distance model must use the same low-q
        // deceleration authority as the acceleration-limited V/S transport.
        // The prior implementation only limited positive climb demands, then
        // reverted to sea-level braking whenever the rollout crossed zero.
        internal float LowQVerticalEnvelopeScheduledDecelMps2 = 0.85f;
        internal float LowQVerticalEnvelopeTerminalCorridorMeters = 120f;
        internal bool LowQVerticalEnvelopeActive { get; private set; }
        internal float LowQVerticalEnvelopeDynamicPressureKpa { get; private set; }
        internal float LowQVerticalEnvelopeBlend { get; private set; }
        internal float LowQVerticalEnvelopeVsCapMps { get; private set; }
        internal float LowQVerticalEnvelopeAppliedAccelLimitMps2 { get; private set; }
        internal float LowQVerticalEnvelopeAppliedBrakeAccelLimitMps2 { get; private set; }
        internal float LowQVerticalEnvelopeEffectiveScheduledDecelMps2 { get; private set; }
        internal float LowQVerticalEnvelopeEffectiveTerminalCorridorMeters { get; private set; }
        internal bool LowQVerticalEnvelopeSymmetricRateCapActive { get; private set; }
        internal float LowQVerticalEnvelopeOutputVsMps { get; private set; }

        // v0.5.5 measured-rate recovery:
        // The initial v0.5.4 ALT schedule assumed that V/S immediately followed the
        // planned brake profile.  Flight data showed the aircraft carrying roughly
        // 0.25 s more trajectory lag and, while braking, a material measured-rate
        // excess above the plan.  Extend the stopping prediction by the measured
        // excess only while braking; this is the vertical counterpart to the
        // lateral measured-rate recovery and prevents a late zero-V/S command from
        // becoming a several-metre overrun.
        internal float TransportLeadSeconds = 1.90f;
        internal float MeasuredBrakeLagLeadSeconds = 0.30f;
        internal float MeasuredBrakeLagLeadMaxMeters = 2.0f;
        internal float MeasuredBrakeLagRateMps { get; private set; }
        internal float MeasuredBrakeLagLeadMeters { get; private set; }

        internal float AltitudePrecisionEntryBandMeters = 0.70f;
        internal float AltitudePrecisionExitBandMeters = 1.25f;
        // Before the final latch is allowed, taper into a low-rate terminal corridor.
        // This is the ALT equivalent of HDG terminal quieting: do not command the
        // sqrt stopping-speed value again when only a few metres remain.
        internal float AltitudeTerminalCorridorMeters = 4.0f;
        internal float AltitudeTerminalRateGainPerSec = 0.35f;
        internal float AltitudeTerminalVerticalSpeedDampingPerSec = 1.20f;
        internal float AltitudeTerminalMaxRateMps = 0.80f;

        // v0.8.4 ALT terminal quieting.  v0.8.3 removed the static altitude bias,
        // but low-q/hypersonic terminal flight still allowed the broad 0.80 m/s
        // rollout demand inside the last metre.  That produced a slow, smooth
        // altitude hunting cycle around 30000 m.  Keep the same trajectory law and
        // AA/V/S ownership, but schedule a smaller maximum terminal V/S very near
        // target, strongest at low q.
        internal float AltitudeTerminalFineBandMeters = 3.0f;
        // v0.8.6: retain the v0.8.4 3 m fine band at normal q, but widen it
        // continuously toward 8 m in the low-q regime.  The v0.8.5 HHC-3B
        // trace repeatedly escaped just beyond 3 m, where the terminal cap rose
        // back toward 0.80 m/s and sustained the long-period cycle.
        internal float AltitudeTerminalFineLowQBandMeters = 8.0f;
        internal float AltitudeTerminalFineMaxRateMps = 0.32f;
        internal float AltitudeTerminalFineMidQMaxRateMps = 0.55f;
        internal float AltitudeTerminalEffectiveFineBandMeters { get; private set; }
        internal float AltitudeTerminalEffectiveMaxRateMps { get; private set; }

        // v0.8.7: v0.8.6 reduced the low-q cycle from roughly +/-3 m to
        // +/-1.35 m, but the aircraft still crossed the target at about 0.55 m/s.
        // Inside a smaller q-scheduled settling corridor, stop using the hard
        // predictive-brake reversal and hand the aircraft to a continuous, strongly
        // damped low-rate PD terminal.  This changes only the AERIS ALT outer Director.
        internal float AltitudeTerminalInnerSettleNormalQBandMeters = 0.90f;
        internal float AltitudeTerminalInnerSettleLowQBandMeters = 1.80f;
        internal float AltitudeTerminalInnerSettleNormalQMaxRateMps = 0.22f;
        internal float AltitudeTerminalInnerSettleLowQMaxRateMps = 0.12f;
        // v0.8.8: v0.8.7 reduced inner braking to about 0.24 m/s at low q
        // while the HHC-3B still entered the corridor carrying 0.6..0.9 m/s.
        // Preserve the already-tested outer predictive-brake authority through the
        // inner handoff, and add hysteresis so the handoff cannot chatter at its edge.
        internal float AltitudeTerminalInnerSettleNormalQBrakeRateMps = 0.55f;
        internal float AltitudeTerminalInnerSettleLowQBrakeRateMps = 0.70f;
        internal float AltitudeTerminalInnerSettleExitBandMultiplier = 1.35f;
        internal float AltitudeTerminalInnerSettleLowQDampingPerSec = 2.20f;
        internal bool AltitudeTerminalInnerSettleActive { get; private set; }
        internal float AltitudeTerminalInnerSettleEffectiveBandMeters { get; private set; }
        internal float AltitudeTerminalInnerSettleEffectiveExitBandMeters { get; private set; }
        internal float AltitudeTerminalInnerSettleEffectiveMaxRateMps { get; private set; }
        internal float AltitudeTerminalInnerSettleEffectiveBrakeRateMps { get; private set; }
        internal float AltitudeTerminalInnerSettleEffectiveDampingPerSec { get; private set; }

        // v0.8.5 ALT terminal predictive brake.  v0.8.4 correctly quieted the
        // final target-rate cap, but HHC-3B at ~30 km / q≈12.5 kPa still crossed
        // the target with 0.36..0.90 m/s altitude-reference rate, so the precision
        // latch never entered.  Keep the quiet toward-target cap, but allow a
        // bounded opposite-sign brake target when the aircraft is already inbound
        // and predicted to cross the selected altitude soon.  This stays entirely
        // inside AERIS ALT Director: no AA source, V/S inner-loop law, or final
        // FlightCtrlState output is modified.
        internal float AltitudeTerminalPredictiveBrakeLeadSeconds = 2.40f;
        // v0.8.6: the v0.8.5 flight trace measured roughly 5.6..8.6 s from
        // brake onset to altitude-reference V/S reaching zero.  Preserve the
        // original 2.4 s lead at normal q, but schedule up to 6.5 s at low q.
        internal float AltitudeTerminalPredictiveBrakeLowQLeadSeconds = 6.50f;
        internal float AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds { get; private set; }
        internal float AltitudeTerminalPredictiveBrakeEffectiveBandMeters { get; private set; }
        internal float AltitudeTerminalPredictiveBrakeStartRateMps = 0.22f;
        internal float AltitudeTerminalPredictiveBrakeMinRateMps = 0.22f;
        internal float AltitudeTerminalPredictiveBrakeGain = 0.95f;
        internal float AltitudeTerminalPredictiveBrakeMaxRateMps = 0.70f;
        internal bool AltitudeTerminalPredictiveBrakeActive { get; private set; }
        internal float AltitudeTerminalPredictiveBrakeInboundRateMps { get; private set; }
        internal float AltitudeTerminalPredictiveBrakeTimeToTargetSeconds { get; private set; }
        internal float AltitudeTerminalPredictiveBrakeDemandMps { get; private set; }

        // v0.8.4: the precision latch should judge terminal calmness in the same
        // altitude-reference frame that ALT controls.  Raw KSP VerticalSpeed remains
        // the V/S inner-loop feedback; the reconciled value is used only for ALT
        // terminal entry/retention/disturbance decisions.
        internal float AltitudePrecisionReferenceVerticalSpeedMps { get; private set; }
        internal bool AltitudePrecisionReferenceRateActive { get; private set; }

        // v0.8.18: the v0.8.17 full switch to the direct altitude derivative moved
        // the HHC-3B terminal cycle to the opposite side of the target
        // (-0.693..+0.147 m target-minus-current, P95 about 0.598 m).  The direct
        // derivative remains diagnostic-only; PrecisionHold returns to the established
        // reconciled altitude-rate estimate.  The actual asymmetry source was the
        // near-binary inbound-arrival rate gate, which is corrected separately below.
        internal bool AltitudePrecisionDirectReferenceRateActive { get; private set; }
        internal float AltitudePrecisionReferenceDeltaVsReconciledMps { get; private set; }

        // A turn or other flight-path disturbance can inject a real V/S while ALT
        // is already in sub-metre hold.  Do not wait for the broad 0.60 s hold-exit
        // timer to expire while the commanded and measured V/S disagree.  Escape
        // precision quickly, then let the normal terminal trajectory recover.
        internal float AltitudeHoldDisturbanceTrackingBandMps = 0.08f;
        internal float AltitudeHoldDisturbanceExitDwellSeconds = 0.14f;

        // v0.8.2: terminal recovery must distinguish a genuine outward flight-path
        // disturbance from a small, commanded correction that is already moving toward
        // the selected altitude.  The old magnitude-only tracking check could leave
        // PrecisionCapture while the trim was correctly converging, then re-enter it and
        // create a Capture -> Recovery -> Rollout loop.  This gate exists only in the ALT
        // outer Director; it does not alter AA, V/S, or any final pitch output.
        internal float AltitudeHoldDisturbanceOutwardRateMps = 0.10f;
        internal float AltitudeHoldDisturbanceHardOutwardRateMps = 0.40f;
        internal float AltitudeHoldDisturbanceDirectionalExitDwellSeconds = 0.28f;
        internal bool HoldDisturbanceDirectionGateActive { get; private set; }
        internal float HoldDisturbanceOutwardRateMps { get; private set; }
        // v0.8.9 introduced residual ownership to separate expected nested-loop
        // transport lag from a true flight-path disturbance.  v0.8.10 extends that
        // ownership from the entry band to the already-existing hold-retention band
        // so the carryover brake can finish the capture without a second law switch.
        internal bool HoldDisturbanceRawExitCandidate { get; private set; }
        internal bool HoldDisturbancePrecisionOwnershipActive { get; private set; }
        internal float HoldDisturbancePrecisionOwnershipBandMeters
        {
            get { return AltitudePrecisionExitBandMeters; }
        }

        // v0.8.10: v0.8.9 proved that the ownership gate suppresses false
        // recovery triggers, but the hold latch still discarded the low-q inner
        // settling damping/authority exactly while delayed aircraft response was
        // changing sign.  Carry that braking authority into PrecisionCapture only
        // while the measured altitude-reference rate is genuinely outward.  The
        // quiet toward-target precision cap remains unchanged, and hard motion
        // above the existing 0.40 m/s threshold still exits through recovery.
        //
        // v0.8.12 kept the capture-brake state until outward altitude-reference
        // motion fell below 0.02 m/s.  The HHC-3B trace proved that retaining the
        // full low-q braking authority for that entire interval over-braked the
        // delayed aircraft response: the final cycle became strongly asymmetric
        // (-0.066..+0.832 m target-minus-current error, P95 about 0.815 m).
        //
        // v0.8.13 keeps the useful 0.10/0.02 state hysteresis, but continuously
        // tapers damping and rate authority from the tested inner-settling values
        // back to quiet PrecisionHold values across that completion interval.
        // This avoids both the v0.8.11 hard authority drop at 0.10 m/s and the
        // v0.8.12 full-authority carryover down to 0.02 m/s.  Neutral-crossing
        // rate braking remains unchanged and may still request full tested damping.
        internal float AltitudeHoldCaptureBrakeExitMps = 0.02f;
        // v0.8.14: release the strong capture brake earlier through the lower half
        // of the completion interval. v0.8.11 proved that an abrupt release at
        // 0.10 m/s was too early, while v0.8.12 proved that full authority down to
        // 0.02 m/s was too late. Squaring the already-smooth v0.8.13 blend keeps
        // continuity at both endpoints but biases the interval toward quiet hold.
        internal float AltitudeHoldCaptureBrakeTaperExponent = 2.0f;
        internal bool HoldCaptureBrakeActive { get; private set; }
        internal bool HoldCaptureBrakeHysteresisActive { get; private set; }
        internal float HoldCaptureBrakeCompletionBlend { get; private set; }
        internal float HoldCaptureBrakeOutwardRateMps { get; private set; }
        internal float HoldCaptureBrakeEffectiveDampingPerSec { get; private set; }
        internal float HoldCaptureBrakeEffectiveMaxRateMps { get; private set; }

        // v0.8.11: v0.8.10 kept the hold latch continuously active and reduced
        // the HHC-3B final-600-second altitude range to roughly -0.52..+0.25 m,
        // but a stable ~12 s cycle remained.  Every target crossing briefly
        // disabled precision correction inside the 0.02/0.07 m neutral
        // hysteresis while altitude-reference V/S was still about 0.16..0.19
        // m/s.  Do not treat that residual arrival rate as a true neutral state.
        // While position trim is intentionally neutral, apply only rate damping
        // until the physical altitude-reference rate is calm.  This uses the
        // same already-tested inner-settling damping/brake authority, remains
        // inside AERIS ALT Director, and never writes AA or final pitch.
        internal float AltitudeHoldNeutralRateBrakeEnterMps = 0.06f;
        internal float AltitudeHoldNeutralRateBrakeExitMps = 0.02f;
        // v0.8.15: v0.8.14 proved that forcing the full low-q capture-brake
        // authority at every precision-neutral crossing creates a repeatable
        // ~12.36 s limit cycle.  The HHC-3B crossed the neutral band at about
        // 0.14 m/s and the demanded V/S stepped from roughly 0.12 to 0.35 m/s.
        // Retain the neutral-crossing rate brake, but blend it continuously from
        // the existing 0.02 m/s release point toward full authority only at
        // materially larger residual rates.  This changes no AA/V/S ownership.
        internal float AltitudeHoldNeutralRateBrakeFullMps = 0.24f;
        internal bool HoldNeutralRateBrakeActive { get; private set; }
        internal float HoldNeutralRateBrakeAbsRateMps { get; private set; }
        internal float HoldNeutralRateBrakeCompletionBlend { get; private set; }

        // v0.8.21: data(14) proved that the v0.8.20 low-rate outward damping tail
        // did not reduce the HHC-3B cycle. The final 600 s remained at P95 0.2852 m
        // with an 11.8 s period, and the extra tail acted late in a plant path whose
        // planned-V/S to altitude-rate phase lag was about 5.1 s / 157 degrees.
        // Restore the exact v0.8.18 capture damping shape. The retained residual fields
        // stay in the diagnostic schema for direct v0.8.19/v0.8.20 comparison only.
        internal float AltitudeHoldResidualRateCompletionDampingTailScale = 0.0f;
        internal float AltitudeHoldResidualRateCompletionPositionReleasePerSec = 0.0f;
        internal float AltitudeHoldResidualRateCompletionPlannedExitMps = 0.03f;
        internal bool HoldResidualRateCompletionActive { get; private set; }
        internal bool HoldResidualRateCompletionReleaseActive { get; private set; }
        internal bool HoldResidualRateCompletionCalm { get; private set; }
        internal float HoldResidualRateCompletionPhysicalRateMps { get; private set; }
        internal float HoldResidualRateCompletionAbsRateMps { get; private set; }
        internal float HoldResidualRateCompletionPlannedRateMps { get; private set; }
        internal float HoldResidualRateCompletionDampingBlend { get; private set; }
        internal float HoldResidualRateCompletionPositionBlend { get; private set; } = 1f;
        internal float HoldResidualRateCompletionEffectivePositionGainPerSec { get; private set; }

        // v0.8.21 planned-rate pipeline unload is withdrawn. data(15) showed that its
        // target-direction gates were not dynamically symmetric on HHC-3B: the upward
        // correction was reduced roughly seven times more strongly than the downward
        // correction, shifting the complete hold cycle below the target altitude.
        // Keep the fields for direct CSV compatibility, but a zero gain guarantees that
        // this layer cannot alter the ALT command in v0.8.22.
        internal float AltitudeHoldPipelineUnloadGain = 0.0f;
        internal float AltitudeHoldPipelineUnloadPhysicalGateStartMps = -0.04f;
        internal float AltitudeHoldPipelineUnloadPhysicalGateFullMps = 0.02f;
        internal float AltitudeHoldPipelineUnloadPlannedGateStartMps = 0.02f;
        internal float AltitudeHoldPipelineUnloadPlannedGateFullMps = 0.10f;
        internal bool HoldPipelineUnloadActive { get; private set; }
        internal float HoldPipelineUnloadPhysicalTowardRateMps { get; private set; }
        internal float HoldPipelineUnloadPlannedPhysicalRateMps { get; private set; }
        internal float HoldPipelineUnloadPlannedTowardRateMps { get; private set; }
        internal float HoldPipelineUnloadPhysicalGateBlend { get; private set; }
        internal float HoldPipelineUnloadPlannedGateBlend { get; private set; }
        internal float HoldPipelineUnloadBlend { get; private set; }
        internal float HoldPipelineUnloadRawBeforeMps { get; private set; }
        internal float HoldPipelineUnloadRequestedRateMps { get; private set; }
        internal float HoldPipelineUnloadAppliedRateMps { get; private set; }

        // v0.8.23: data(16) rejects the v0.8.22 error-dependent position-gain
        // schedule. Although the formula used absolute altitude error, the two half-cycles
        // occupied different error ranges, so one side restored toward 0.25/s while the
        // other remained near 0.20/s. The result was a self-reinforcing asymmetric cycle,
        // repeated Rollout re-entry, and a final continuous-hold P95 near 0.67 m.
        // Keep the compatibility fields, but force the position gain back to the proven
        // v0.8.18 value at every PrecisionHold sample.
        internal float AltitudePrecisionLowQRateGainPerSec = 0.25f;
        internal float AltitudePrecisionLowQGainFullBandMeters = 0.40f;
        internal float AltitudePrecisionLowQGainReleaseBandMeters = 0.75f;
        internal bool PrecisionLowQRateGainActive { get; private set; }
        internal float PrecisionLowQRateGainQBlend { get; private set; }
        internal float PrecisionLowQRateGainErrorBlend { get; private set; }
        internal float PrecisionLowQRateGainBlend { get; private set; }
        internal float PrecisionEffectiveRateGainPerSec { get; private set; } = 0.25f;

        // v0.8.23 low-q PrecisionHold rate-damping phase-margin trial. The data(14)
        // identified plant model (about 3.2 s dead time plus about 3.0 s first-order lag)
        // predicts that increasing late rate damping reinforces the 11.8 s mode, while a
        // modest reduction of the quiet base damping improves symmetry and phase margin.
        // This schedule is direction-independent, has no altitude-error gate, changes no
        // max-rate authority, and returns to the proven 0.55/s baseline outside low q or
        // whenever bank vertical support owns the terminal correction.
        internal float AltitudePrecisionLowQDampingPerSec = 0.55f;
        internal bool PrecisionLowQDampingActive { get; private set; }
        internal float PrecisionLowQDampingQBlend { get; private set; }
        internal float PrecisionEffectiveBaseDampingPerSec { get; private set; } = 0.55f;

        // v0.8.30 retains the v0.8.27 phase-locked micro-trim attribution,
        // strict alternating pairs, and bounded pair-amplitude bias recovery.
        //
        // The 7.2 h v0.8.25 endurance trace proved that phase-locked cancellation can
        // reduce the late 600 s ALT P95 from the v0.8.18 baseline 0.2813 m to 0.1270 m
        // with essentially zero mean error.  It also exposed one unsafe adaptation
        // interval: the observer correlated the pre-pulse base command against a
        // response produced by the base command plus Micro-Trim, and the scheduler
        // allowed same-direction pulses when one target crossing was missed.  A run
        // of up to 33 same-direction pulses then held a large one-sided altitude bias.
        //
        // v0.8.30 keeps the successful phase-locked pulse law and actual-command
        // observer from the actual total ALT command, and enforces strict
        // positive/negative pulse pairing. The emergency hard-bias guard is now
        // fault-aware: a large bias remains latched and visible, but does not by
        // itself remove the only bounded recovery action while the observer and
        // flight envelope are healthy. Pulses remain cancellation-only: they may
        // reduce the existing raw ALT command but can never reverse it or increase
        // authority.
        internal bool MicroTrimEnabled = true;
        internal bool MicroTrimEligible { get; private set; }
        internal bool MicroTrimPulseActive { get; private set; }
        internal bool MicroTrimObservationActive { get; private set; }
        internal float MicroTrimPulseRateMps { get; private set; }
        internal float MicroTrimPulseElapsedSeconds { get; private set; }
        internal float MicroTrimWaitElapsedSeconds { get; private set; }
        internal float MicroTrimLearnedPulseMagnitudeMps { get; private set; } = 0.012f;
        internal float MicroTrimLearnedPulseDurationSeconds { get; private set; } = 0.35f;
        internal float MicroTrimLearnedWaitSeconds { get; private set; } = 1.0f;
        internal float MicroTrimLearnedDelaySeconds { get; private set; } = 4.0f;
        internal float MicroTrimLearnedResponseGain { get; private set; } = 0.60f;
        internal float MicroTrimObservedResponseMps { get; private set; }
        internal float MicroTrimAppliedRateMps { get; private set; }
        internal int MicroTrimPulseCount { get; private set; }

        internal bool MicroTrimObserverReady { get; private set; }
        internal float MicroTrimObserverCorrelation { get; private set; }
        internal float MicroTrimLearnedCyclePeriodSeconds { get; private set; } = 12.0f;
        internal float MicroTrimLearnedHalfCycleSeconds { get; private set; } = 6.0f;
        internal bool MicroTrimPulseScheduled { get; private set; }
        internal float MicroTrimScheduledWaitSeconds { get; private set; }
        internal float MicroTrimPredictedFutureRateMps { get; private set; }
        internal float MicroTrimBaseRawRateMps { get; private set; }
        internal float MicroTrimSafeMagnitudeMps { get; private set; }
        internal int MicroTrimTargetCrossingCount { get; private set; }
        internal float MicroTrimLastCrossingRateMps { get; private set; }
        internal int MicroTrimFutureHalfCycles { get; private set; }
        internal float MicroTrimObserverInputCommandMps { get; private set; }
        internal float MicroTrimObserverBaseCommandMps { get; private set; }
        internal bool MicroTrimPairGuardActive { get; private set; }
        internal float MicroTrimLastAppliedPulseDirection { get; private set; }
        internal int MicroTrimPositivePulseCount { get; private set; }
        internal int MicroTrimNegativePulseCount { get; private set; }
        internal float MicroTrimBiasEstimateMeters { get; private set; }
        internal bool MicroTrimBiasGuardActive { get; private set; }
        internal float MicroTrimBiasGuardElapsedSeconds { get; private set; }
        internal bool MicroTrimBiasRecoveryActive { get; private set; }
        internal float MicroTrimBiasRecoveryBlend { get; private set; }
        internal float MicroTrimBiasCorrectiveDirection { get; private set; }
        internal float MicroTrimBiasPulseScale { get; private set; } = 1f;
        internal bool MicroTrimBiasHardGuardActive { get; private set; }
        internal bool MicroTrimBiasHardGuardRecoveryPermitted { get; private set; }
        internal bool MicroTrimBiasHardGuardInhibitActive { get; private set; }
        internal string MicroTrimBiasHardGuardReason { get; private set; } = "Inactive";

        internal float MicroTrimTargetRateReductionMps = 0.010f;
        internal float MicroTrimMinimumPulseMagnitudeMps = 0.006f;
        internal float MicroTrimMaximumPulseMagnitudeMps = 0.025f;
        internal float MicroTrimMaximumRawCancellationFraction = 0.35f;
        internal float MicroTrimMinimumObserverCorrelation = 0.75f;
        internal float MicroTrimMinimumLowQBlend = 0.50f;
        internal float MicroTrimBiasGuardEnterMeters = 0.10f;
        internal float MicroTrimBiasGuardExitMeters = 0.06f;
        internal float MicroTrimBiasRecoveryFullMeters = 0.20f;
        internal float MicroTrimBiasCorrectivePulseScale = 1.45f;
        internal float MicroTrimBiasOpposingPulseScale = 0.55f;
        internal float MicroTrimBiasHardGuardEnterMeters = 0.30f;
        internal float MicroTrimBiasHardGuardExitMeters = 0.20f;
        internal float MicroTrimBiasEstimateTimeConstantSeconds = 30.0f;

        const int MicroTrimObserverCapacity = 160;
        const float MicroTrimObserverSamplePeriodSeconds = 0.10f;
        readonly float[] microTrimObserverInputRateMps =
            new float[MicroTrimObserverCapacity];
        readonly float[] microTrimObserverOutputRateMps =
            new float[MicroTrimObserverCapacity];
        Guid microTrimVesselId = Guid.Empty;
        int microTrimObserverWriteIndex;
        int microTrimObserverCount;
        int microTrimValidHalfCycleCount;
        float microTrimObserverSampleAccumulator;
        float microTrimObserverEstimateAccumulator;
        float microTrimLastErrorSign;
        float microTrimLastCrossingFixedTime = -1f;
        float microTrimScheduledDirection;
        float microTrimScheduledElapsedSeconds;
        float microTrimStoredPulseMagnitudeMps;
        float microTrimPulseDirection;
        float microTrimLastAppliedPulseDirection;
        float microTrimBiasEstimateMeters;
        bool microTrimBiasGuardLatched;
        bool microTrimBiasHardGuardLatched;
        float microTrimBiasGuardElapsedSeconds;

        // v0.8.18: v0.8.16 and v0.8.17 exposed a self-reinforcing directional
        // asymmetry in the original 0.04..0.12 m/s arrival-rate gate.  One half-cycle
        // reached nearly full extra damping while the other received little, shifting
        // the complete hold cycle below or above the selected altitude depending on
        // which rate reference was used.  Keep the useful time-to-target lead, but make
        // the rate gate fully open by 0.07 m/s and reduce the low-q damping ceiling.
        // Normal 0.08..0.15 m/s arrivals therefore receive similar, moderate damping
        // in both directions instead of a near-binary direction-dependent response.
        internal float AltitudeHoldInboundArrivalBrakeEnterMps = 0.03f;
        internal float AltitudeHoldInboundArrivalBrakeFullMps = 0.07f;
        internal float AltitudeHoldInboundArrivalBrakeLeadStartSeconds = 3.0f;
        internal float AltitudeHoldInboundArrivalBrakeLeadFullSeconds = 1.2f;
        internal float AltitudeHoldInboundArrivalBrakeLowQDampingPerSec = 0.80f;
        internal bool HoldInboundArrivalBrakeActive { get; private set; }
        internal float HoldInboundArrivalBrakeRateMps { get; private set; }
        internal float HoldInboundArrivalBrakeTimeToTargetSeconds { get; private set; }
        internal float HoldInboundArrivalBrakeRateGateBlend { get; private set; }
        internal float HoldInboundArrivalBrakeBlend { get; private set; }
        internal float HoldInboundArrivalBrakeEffectiveDampingPerSec { get; private set; }

        internal bool HoldDisturbanceExitCandidate { get; private set; }
        internal float HoldDisturbanceRequiredDwellSeconds { get; private set; }
        internal bool HoldDisturbanceRecoveryActive { get; private set; }
        internal float HoldDisturbanceExitElapsedSeconds { get; private set; }

        internal float AltitudePrecisionEntryVsMps = 0.22f;
        internal float AltitudePrecisionEntryPlannedVsMps = 0.20f;
        internal float AltitudePrecisionEntryOutwardToleranceMps = 0.03f;
        internal bool AltitudePrecisionEntryMeasuredRateOk { get; private set; }
        internal bool AltitudePrecisionEntryPlannedRateOk { get; private set; }
        internal bool AltitudePrecisionEntryDirectionOk { get; private set; }
        internal bool AltitudePrecisionEntryReady { get; private set; }
        internal float AltitudePrecisionEntryPhysicalPlannedRateMps { get; private set; }
        internal float AltitudeHoldNeutralCommandMps { get; private set; }
        internal float AltitudePrecisionExitVsMps = 0.38f;
        internal float AltitudePrecisionEntryDwellSeconds = 0.40f;
        internal float AltitudePrecisionExitDwellSeconds = 0.60f;

        // v0.5.0: the broad ALT trajectory is already quiet at capture.  The remaining
        // ±0.2..0.5 m residual came from a deliberate ±0.25 m neutral band, which made
        // the director stop correcting while the aircraft was still visibly off target.
        // Keep this sub-metre trim strictly inside the established hold latch so the
        // main trajectory, rollout timing, and V/S capture dynamics remain unchanged.
        //
        // Two bands give the correction a small Schmitt trigger: once it is correcting,
        // it continues to the tighter enter band; after it is quiet, it does not restart
        // until the error has genuinely grown past the wider exit band.
        // v0.5.4: ALT precision is a final trim only.  The prior v0.5.3 handoff
        // entered this state as far as 1.5 m from target and then limited both the
        // outer demand and the V/S response.  The result was a long ~1.4 m residual.
        // Stay on the normal terminal trajectory until the aircraft is genuinely calm,
        // then use a bounded continuous PD trim with a responsive-but-slewed handoff.
        internal float AltitudePrecisionNeutralEnterBandMeters = 0.020f;
        internal float AltitudePrecisionNeutralExitBandMeters = 0.070f;
        internal float AltitudePrecisionRateGainPerSec = 0.25f;
        internal float AltitudePrecisionVerticalSpeedDampingPerSec = 0.55f;
        // Retained as a visible telemetry/configuration field.  Zero deliberately means
        // that ALT precision uses a continuous rate rather than a forced minimum step.
        internal float AltitudePrecisionMinRateMps = 0.0f;
        internal float AltitudePrecisionMaxRateMps = 0.20f;
        internal float AltitudePrecisionCommandSlewMps2 = 0.65f;
        internal float PrecisionRawRateMps { get; private set; }
        internal float PrecisionCorrectionRateMps { get; private set; }

        // v0.5.6 Bank-Aware Vertical Support:
        // In a coordinated turn, bank can remove vertical lift before the resulting
        // sink has grown into metres of altitude error.  ALT remains an outer V/S
        // director: this is a bounded, observation-derived upward V/S bias only in
        // the terminal/hold corridor.  It never writes pitch or any post-AA axis.
        // The bias is not a permanent climb command: it is activated by bank-entry
        // motion and/or observed sink, then fades back to zero when the flight path
        // is level.  This preserves straight-flight ALT behavior and avoids a steady
        // bank-induced climb.
        internal float AltitudeBankSupportTerminalBandMeters = 10f;
        internal float AltitudeBankSupportStartBankDeg = 8f;
        internal float AltitudeBankSupportFullBankDeg = 45f;
        internal float AltitudeBankSupportLoadGainMps = 0.72f;
        internal float AltitudeBankSupportMaxRateMps = 0.28f;
        internal float AltitudeBankSupportSinkStartMps = 0.01f;
        internal float AltitudeBankSupportSinkFullMps = 0.12f;
        internal float AltitudeBankSupportRollRateFullDegPerSec = 28f;
        internal float AltitudeBankSupportTransitionMaxRateMps = 0.12f;
        internal float AltitudeBankSupportSlewMps2 = 0.75f;
        internal bool BankVerticalSupportEligible { get; private set; }
        internal bool BankVerticalSupportActive { get; private set; }
        internal float BankVerticalSupportBankDeg { get; private set; }
        internal float BankVerticalSupportRollRateDegPerSec { get; private set; }
        internal float BankVerticalSupportLoadFactorExcess { get; private set; }
        internal float BankVerticalSupportSinkActivation { get; private set; }
        internal float BankVerticalSupportTransitionRateMps { get; private set; }
        internal float BankVerticalSupportTargetRateMps { get; private set; }
        internal float BankVerticalSupportRateMps { get; private set; }

        void UpdateAltitudeReferenceRate()
        {
            float now = Time.fixedTime;
            if (!haveAltitudeReferenceSample)
            {
                haveAltitudeReferenceSample = true;
                lastAltitudeReferenceMeters = CurrentAltitudeMeters;
                lastAltitudeReferenceFixedTime = now;
                AltitudeReferenceVerticalSpeedMps = CurrentVerticalSpeedMps;
                AltitudeReconciledVerticalSpeedMps = CurrentVerticalSpeedMps;
                AltitudeRateBiasMps = 0f;
                AltitudeRateReconciliationActive = false;
                AltitudeRateReconciliationBlend = 0f;
                AltitudeRateCommandBiasMps = 0f;
                return;
            }

            float dt = Mathf.Clamp(now - lastAltitudeReferenceFixedTime, 0.001f, 0.25f);
            float measuredReferenceRate = Mathf.Clamp(
                (CurrentAltitudeMeters - lastAltitudeReferenceMeters) / Mathf.Max(0.001f, dt),
                -80f, 80f);
            float referenceBlend = Mathf.Clamp01(dt * AltitudeRateReferenceFilterPerSec);
            AltitudeReferenceVerticalSpeedMps = Mathf.Lerp(AltitudeReferenceVerticalSpeedMps,
                measuredReferenceRate, referenceBlend);

            float observedBias = CurrentVerticalSpeedMps - AltitudeReferenceVerticalSpeedMps;
            float biasBlend = Mathf.Clamp01(dt * AltitudeRateBiasFilterPerSec);
            AltitudeRateBiasMps = Mathf.Lerp(AltitudeRateBiasMps, observedBias, biasBlend);

            lastAltitudeReferenceMeters = CurrentAltitudeMeters;
            lastAltitudeReferenceFixedTime = now;
        }

        void UpdateTerminalAltitudeRateReconciliation(float absAltitudeError)
        {
            float fullBand = Mathf.Max(0.10f, AltitudeRateReconciliationFullBandMeters);
            float startBand = Mathf.Max(fullBand + 0.10f, AltitudeRateReconciliationStartBandMeters);
            float proximity = Mathf.InverseLerp(startBand, fullBand, absAltitudeError);
            AltitudeRateReconciliationBlend = Mathf.SmoothStep(0f, 1f, proximity);
            AltitudeRateReconciliationActive = haveAltitudeReferenceSample &&
                AltitudeRateReconciliationBlend > 0.0001f;
            AltitudeRateCommandBiasMps = AltitudeRateReconciliationActive
                ? AltitudeRateReconciliationBlend * AltitudeRateBiasMps : 0f;
            AltitudeReconciledVerticalSpeedMps = CurrentVerticalSpeedMps - AltitudeRateCommandBiasMps;
        }

        void UpdateBankVerticalSupport(VirtualAttitudeInstrument attitude, float absAltitudeError)
        {
            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            BankVerticalSupportEligible = false;
            BankVerticalSupportBankDeg = 0f;
            BankVerticalSupportRollRateDegPerSec = 0f;
            BankVerticalSupportLoadFactorExcess = 0f;
            BankVerticalSupportSinkActivation = 0f;
            BankVerticalSupportTransitionRateMps = 0f;
            BankVerticalSupportTargetRateMps = 0f;

            // Bank support is an anti-sink assist around the target.  Do not oppose
            // a legitimate commanded descent when the aircraft is already above it.
            bool altitudeDirectionAllowsSupport = AltitudeControlErrorMeters >= -AltitudePrecisionNeutralExitBandMeters;
            bool validBank = attitude != null && attitude.InstrumentHorizonBankValid &&
                absAltitudeError <= AltitudeBankSupportTerminalBandMeters &&
                altitudeDirectionAllowsSupport;

            if (validBank)
            {
                float absBank = Mathf.Abs(attitude.InstrumentHorizonBankDeg);
                float absRollRate = Mathf.Abs(attitude.InstrumentRollRateDegPerSec);
                BankVerticalSupportBankDeg = absBank;
                BankVerticalSupportRollRateDegPerSec = absRollRate;

                float bankFraction = Mathf.InverseLerp(AltitudeBankSupportStartBankDeg,
                    AltitudeBankSupportFullBankDeg, absBank);
                if (bankFraction > 0.0001f)
                {
                    // sec(phi)-1 is zero in wings-level flight and grows with the
                    // vertical-lift loss of a coordinated turn.  It is only a bounded
                    // feed-forward scale, never a direct pitch/load-factor command.
                    float cosine = Mathf.Max(0.35f, Mathf.Cos(absBank * Mathf.Deg2Rad));
                    BankVerticalSupportLoadFactorExcess = Mathf.Clamp(1f / cosine - 1f, 0f, 1.5f);
                    float steadySupport = Mathf.Clamp(BankVerticalSupportLoadFactorExcess *
                        AltitudeBankSupportLoadGainMps, 0f, AltitudeBankSupportMaxRateMps);

                    // Prefer observed sink; bank-rate gives a short anticipatory pulse
                    // while a HDG transition is actually building bank.  Once the sink
                    // disappears, both terms decay toward zero rather than commanding a
                    // permanent climb throughout a steady turn.
                    float sinkMps = Mathf.Max(0f, -CurrentVerticalSpeedMps);
                    BankVerticalSupportSinkActivation = Mathf.InverseLerp(
                        AltitudeBankSupportSinkStartMps, AltitudeBankSupportSinkFullMps, sinkMps);
                    float steadyRate = steadySupport * BankVerticalSupportSinkActivation;
                    float transitionFactor = bankFraction * Mathf.InverseLerp(0f,
                        AltitudeBankSupportRollRateFullDegPerSec, absRollRate);
                    BankVerticalSupportTransitionRateMps = transitionFactor *
                        AltitudeBankSupportTransitionMaxRateMps;
                    BankVerticalSupportTargetRateMps = Mathf.Clamp(steadyRate +
                        BankVerticalSupportTransitionRateMps, 0f, AltitudeBankSupportMaxRateMps);
                    BankVerticalSupportEligible = true;
                }
            }

            BankVerticalSupportRateMps = Mathf.MoveTowards(BankVerticalSupportRateMps,
                BankVerticalSupportTargetRateMps, AltitudeBankSupportSlewMps2 * dt);
            BankVerticalSupportActive = BankVerticalSupportEligible &&
                BankVerticalSupportRateMps > 0.0005f;
        }

        void ResetAoAClimbGovernor()
        {
            AoAClimbGovernorActive = false;
            AoAClimbGovernorAoAValid = false;
            AoAClimbGovernorAoADeg = 0f;
            AoAClimbGovernorBlend = 0f;
            AoAClimbGovernorTargetVsCapMps = MaxAltitudeVerticalSpeedMps;
            AoAClimbGovernorAppliedVsCapMps = MaxAltitudeVerticalSpeedMps;
            AoAClimbGovernorOutputVsMps = 0f;
            AoAClimbGovernorSurfaceSpeedMps = 0f;
            aoaClimbGovernorInitialized = false;
        }

        void ResetLowQVerticalEnvelope()
        {
            LowQVerticalEnvelopeActive = false;
            LowQVerticalEnvelopeDynamicPressureKpa = 0f;
            LowQVerticalEnvelopeBlend = 0f;
            LowQVerticalEnvelopeVsCapMps = MaxAltitudeVerticalSpeedMps;
            LowQVerticalEnvelopeAppliedAccelLimitMps2 = AltitudeRateAccelLimitMps2;
            LowQVerticalEnvelopeAppliedBrakeAccelLimitMps2 = AltitudeRateBrakeAccelLimitMps2;
            LowQVerticalEnvelopeEffectiveScheduledDecelMps2 = ScheduledVerticalDecelMps2;
            LowQVerticalEnvelopeEffectiveTerminalCorridorMeters = AltitudeTerminalCorridorMeters;
            LowQVerticalEnvelopeSymmetricRateCapActive = false;
            LowQVerticalEnvelopeOutputVsMps = 0f;
        }

        // Retained as observer-only telemetry. AoA is not used to alter the trajectory
        // until a future Protect integration explicitly owns that safety boundary.
        void ObserveDisabledAoAClimbGovernor(VirtualAttitudeInstrument attitude, float desiredVerticalSpeedMps)
        {
            AoAClimbGovernorActive = false;
            AoAClimbGovernorAoAValid = attitude != null && attitude.EstimatedAoAValid;
            AoAClimbGovernorAoADeg = AoAClimbGovernorAoAValid
                ? Mathf.Abs(attitude.EstimatedPitchAoADeg) : 0f;
            AoAClimbGovernorBlend = 0f;
            AoAClimbGovernorTargetVsCapMps = MaxAltitudeVerticalSpeedMps;
            AoAClimbGovernorAppliedVsCapMps = MaxAltitudeVerticalSpeedMps;
            AoAClimbGovernorOutputVsMps = desiredVerticalSpeedMps;
            AoAClimbGovernorSurfaceSpeedMps = attitude != null &&
                attitude.SharedSurfaceSpeedValid && IsFinite(attitude.SurfaceSpeedMps)
                ? Mathf.Max(0f, attitude.SurfaceSpeedMps) : 0f;
            aoaClimbGovernorInitialized = false;
        }

        void UpdateLowQVerticalEnvelope(VirtualAttitudeInstrument attitude)
        {
            bool dynamicPressureValid = attitude != null && attitude.SharedDynamicPressureValid &&
                IsFinite(attitude.DynamicPressureKpa);
            LowQVerticalEnvelopeDynamicPressureKpa = dynamicPressureValid
                ? Mathf.Max(0f, attitude.DynamicPressureKpa) : 0f;
            LowQVerticalEnvelopeBlend = Mathf.Clamp01((LowQVerticalEnvelopeStartKpa -
                LowQVerticalEnvelopeDynamicPressureKpa) /
                Mathf.Max(0.01f, LowQVerticalEnvelopeStartKpa - LowQVerticalEnvelopeFullKpa));

            bool stateValid = dynamicPressureValid;
            float appliedBlend = stateValid ? LowQVerticalEnvelopeBlend : 0f;
            LowQVerticalEnvelopeVsCapMps = Mathf.Lerp(MaxAltitudeVerticalSpeedMps,
                Mathf.Min(MaxAltitudeVerticalSpeedMps, LowQVerticalEnvelopeMinimumVsMps), appliedBlend);
            LowQVerticalEnvelopeAppliedAccelLimitMps2 = Mathf.Lerp(AltitudeRateAccelLimitMps2,
                LowQVerticalEnvelopeAccelLimitMps2, appliedBlend);
            LowQVerticalEnvelopeAppliedBrakeAccelLimitMps2 = Mathf.Lerp(AltitudeRateBrakeAccelLimitMps2,
                LowQVerticalEnvelopeBrakeAccelLimitMps2, appliedBlend);
            LowQVerticalEnvelopeEffectiveScheduledDecelMps2 = Mathf.Lerp(ScheduledVerticalDecelMps2,
                Mathf.Min(ScheduledVerticalDecelMps2, LowQVerticalEnvelopeScheduledDecelMps2), appliedBlend);
            LowQVerticalEnvelopeEffectiveTerminalCorridorMeters = Mathf.Lerp(AltitudeTerminalCorridorMeters,
                Mathf.Max(AltitudeTerminalCorridorMeters, LowQVerticalEnvelopeTerminalCorridorMeters), appliedBlend);
            LowQVerticalEnvelopeActive = appliedBlend > 0.001f;
            LowQVerticalEnvelopeSymmetricRateCapActive = LowQVerticalEnvelopeActive;
        }

        // Apply the q-derived rate envelope symmetrically. ALT may legitimately request
        // either climb or descent while rolling out; both directions must preserve the same
        // low-q authority and stopping-distance assumptions.
        float ApplyLowQVerticalEnvelope(float desiredVerticalSpeedMps)
        {
            float cap = Mathf.Max(0.05f, LowQVerticalEnvelopeVsCapMps);
            LowQVerticalEnvelopeOutputVsMps = LowQVerticalEnvelopeActive
                ? Mathf.Clamp(desiredVerticalSpeedMps, -cap, cap)
                : desiredVerticalSpeedMps;
            return LowQVerticalEnvelopeOutputVsMps;
        }

        // Retained for provenance only. v0.7.6 no longer calls this feedback path.
        // Schedules a positive ALT climb-rate ceiling from AERIS's own estimated pitch AoA.
        // It intentionally does not affect descent, terminal precision hold, manual V/S,
        // PITCH, BANK, HDG, ACC, VEL, AA or any final control surface input.
        float ApplyAoAClimbGovernor(VirtualAttitudeInstrument attitude, float desiredVerticalSpeedMps)
        {
            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            AoAClimbGovernorSurfaceSpeedMps = attitude != null &&
                attitude.SharedSurfaceSpeedValid && IsFinite(attitude.SurfaceSpeedMps)
                ? Mathf.Max(0f, attitude.SurfaceSpeedMps) : 0f;
            float rawAoA = attitude != null && attitude.EstimatedAoAValid
                ? Mathf.Abs(attitude.EstimatedPitchAoADeg) : 0f;
            AoAClimbGovernorAoAValid = attitude != null && attitude.EstimatedAoAValid;
            if (!aoaClimbGovernorInitialized)
            {
                AoAClimbGovernorAoADeg = rawAoA;
                AoAClimbGovernorAppliedVsCapMps = MaxAltitudeVerticalSpeedMps;
                aoaClimbGovernorInitialized = true;
            }
            else
            {
                AoAClimbGovernorAoADeg = Mathf.Lerp(AoAClimbGovernorAoADeg, rawAoA,
                    Mathf.Clamp01(AltitudeAoAClimbGovernorAoAFilter));
            }

            bool eligible = !HoldLatched && desiredVerticalSpeedMps > 0.001f &&
                AoAClimbGovernorAoAValid &&
                AoAClimbGovernorSurfaceSpeedMps >= AltitudeAoAClimbGovernorMinimumSurfaceSpeedMps;
            AoAClimbGovernorBlend = eligible
                ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(AltitudeAoAClimbGovernorSoftLimitDeg,
                    AltitudeAoAClimbGovernorFullLimitDeg, AoAClimbGovernorAoADeg)) : 0f;
            float minCap = Mathf.Min(MaxAltitudeVerticalSpeedMps, AltitudeAoAClimbGovernorMinimumVsMps);
            AoAClimbGovernorTargetVsCapMps = eligible
                ? Mathf.Lerp(MaxAltitudeVerticalSpeedMps, minCap, AoAClimbGovernorBlend)
                : MaxAltitudeVerticalSpeedMps;
            float capRate = AoAClimbGovernorTargetVsCapMps < AoAClimbGovernorAppliedVsCapMps
                ? AltitudeAoAClimbGovernorLimitDownRateMps2
                : AltitudeAoAClimbGovernorLimitRecoveryRateMps2;
            AoAClimbGovernorAppliedVsCapMps = Mathf.MoveTowards(AoAClimbGovernorAppliedVsCapMps,
                AoAClimbGovernorTargetVsCapMps, Mathf.Max(0.05f, capRate) * dt);
            AoAClimbGovernorActive = eligible && AoAClimbGovernorAppliedVsCapMps <
                MaxAltitudeVerticalSpeedMps - 0.01f;
            AoAClimbGovernorOutputVsMps = desiredVerticalSpeedMps > 0f
                ? Mathf.Min(desiredVerticalSpeedMps, AoAClimbGovernorAppliedVsCapMps)
                : desiredVerticalSpeedMps;
            return AoAClimbGovernorOutputVsMps;
        }

        internal void SetArmed(bool armed, Vessel vessel, VirtualAttitudeInstrument attitude,
            AERISVerticalSpeedDirector verticalSpeed, AERISPitchDirector pitch)
        {
            if (Armed == armed) return;
            Armed = armed;
            if (armed)
            {
                float altitudeSample = vessel != null ? (float)vessel.altitude : 0f;
                CurrentAltitudeMeters = IsFinite(altitudeSample) ? Mathf.Max(0f, altitudeSample) : 0f;
                CurrentVerticalSpeedMps = attitude != null && IsFinite(attitude.VerticalSpeedMps)
                    ? attitude.VerticalSpeedMps : 0f;
                UpdateAltitudeHoldBandReference();
                UpdateAltitudeErrors();
                DesiredVerticalSpeedMps = 0f;
                PlannedVerticalSpeedMps = 0f;
                AltitudeRateDemandMps = 0f;
                StoppingRateLimitMps = 0f;
                StopDistanceMeters = 0f;
                TransportLeadMeters = 0f;
                MeasuredBrakeLagRateMps = 0f;
                MeasuredBrakeLagLeadMeters = 0f;
                RolloutActive = false;
                HoldLatched = false;
                HoldDisturbanceDirectionGateActive = false;
                HoldDisturbanceOutwardRateMps = 0f;
                HoldDisturbanceRawExitCandidate = false;
                HoldDisturbancePrecisionOwnershipActive = false;
                HoldCaptureBrakeActive = false;
                HoldCaptureBrakeHysteresisActive = false;
                HoldCaptureBrakeCompletionBlend = 0f;
                HoldCaptureBrakeOutwardRateMps = 0f;
                HoldCaptureBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldCaptureBrakeEffectiveMaxRateMps = AltitudePrecisionMaxRateMps;
                HoldNeutralRateBrakeActive = false;
                HoldNeutralRateBrakeAbsRateMps = 0f;
                HoldNeutralRateBrakeCompletionBlend = 0f;
                HoldResidualRateCompletionActive = false;
                HoldResidualRateCompletionReleaseActive = false;
                HoldResidualRateCompletionCalm = true;
                HoldResidualRateCompletionPhysicalRateMps = 0f;
                HoldResidualRateCompletionAbsRateMps = 0f;
                HoldResidualRateCompletionPlannedRateMps = 0f;
                HoldResidualRateCompletionDampingBlend = 0f;
                HoldResidualRateCompletionPositionBlend = 1f;
                HoldResidualRateCompletionEffectivePositionGainPerSec = AltitudePrecisionRateGainPerSec;
                HoldPipelineUnloadActive = false;
                HoldPipelineUnloadPhysicalTowardRateMps = 0f;
                HoldPipelineUnloadPlannedPhysicalRateMps = 0f;
                HoldPipelineUnloadPlannedTowardRateMps = 0f;
                HoldPipelineUnloadPhysicalGateBlend = 0f;
                HoldPipelineUnloadPlannedGateBlend = 0f;
                HoldPipelineUnloadBlend = 0f;
                HoldPipelineUnloadRawBeforeMps = 0f;
                HoldPipelineUnloadRequestedRateMps = 0f;
                HoldPipelineUnloadAppliedRateMps = 0f;
                PrecisionLowQRateGainActive = false;
                PrecisionLowQRateGainQBlend = 0f;
                PrecisionLowQRateGainErrorBlend = 0f;
                PrecisionLowQRateGainBlend = 0f;
                PrecisionEffectiveRateGainPerSec = AltitudePrecisionRateGainPerSec;
                PrecisionLowQDampingActive = false;
                PrecisionLowQDampingQBlend = 0f;
                PrecisionEffectiveBaseDampingPerSec =
                    AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldInboundArrivalBrakeActive = false;
                HoldInboundArrivalBrakeRateMps = 0f;
                HoldInboundArrivalBrakeTimeToTargetSeconds = 0f;
                HoldInboundArrivalBrakeRateGateBlend = 0f;
                HoldInboundArrivalBrakeBlend = 0f;
                HoldInboundArrivalBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldDisturbanceExitCandidate = false;
                HoldDisturbanceRequiredDwellSeconds = 0f;
                HoldDisturbanceRecoveryActive = false;
                HoldDisturbanceExitElapsedSeconds = 0f;
                PrecisionCorrectionActive = false;
                PrecisionRawRateMps = 0f;
                PrecisionCorrectionRateMps = 0f;
                BankVerticalSupportEligible = false;
                BankVerticalSupportActive = false;
                BankVerticalSupportBankDeg = 0f;
                BankVerticalSupportRollRateDegPerSec = 0f;
                BankVerticalSupportLoadFactorExcess = 0f;
                BankVerticalSupportSinkActivation = 0f;
                BankVerticalSupportTransitionRateMps = 0f;
                BankVerticalSupportTargetRateMps = 0f;
                BankVerticalSupportRateMps = 0f;
                AltitudeTerminalEffectiveFineBandMeters = 0f;
                AltitudeTerminalEffectiveMaxRateMps = 0f;
                AltitudeTerminalInnerSettleActive = false;
                AltitudeTerminalInnerSettleEffectiveBandMeters = 0f;
                AltitudeTerminalInnerSettleEffectiveExitBandMeters = 0f;
                AltitudeTerminalInnerSettleEffectiveMaxRateMps = 0f;
                AltitudeTerminalInnerSettleEffectiveBrakeRateMps = 0f;
                AltitudeTerminalInnerSettleEffectiveDampingPerSec = 0f;
                AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds = 0f;
                AltitudeTerminalPredictiveBrakeEffectiveBandMeters = 0f;
                AltitudePrecisionEntryMeasuredRateOk = false;
                AltitudePrecisionEntryPlannedRateOk = false;
                AltitudePrecisionEntryDirectionOk = false;
                AltitudePrecisionEntryReady = false;
                AltitudePrecisionEntryPhysicalPlannedRateMps = 0f;
                AltitudeHoldNeutralCommandMps = 0f;
                AltitudePrecisionReferenceVerticalSpeedMps = 0f;
                AltitudePrecisionReferenceRateActive = false;
                AltitudePrecisionDirectReferenceRateActive = false;
                AltitudePrecisionReferenceDeltaVsReconciledMps = 0f;
                ResetAoAClimbGovernor();
                HoldEntryElapsedSeconds = 0f;
                HoldExitElapsedSeconds = 0f;
                TargetChangedSinceUpdate = true;
                if (verticalSpeed != null)
                    verticalSpeed.SetAltitudeVerticalSpeedDemand(0f, MaxAltitudePitchDeg, false);
                ControlState = "Armed";
                AERISLogger.Info("[ALT] armed: target preserved=" +
                    TargetAltitudeText + " m ASL; display-safe hold band=" +
                    AltitudeHoldBandLowerMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    ".." + AltitudeHoldBandUpperMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                    " m; control reference=" + AltitudeHoldReferenceMeters.ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    " m; max V/S=±" + MaxAltitudeVerticalSpeedText +
                    " m/s; max pitch=±" + MaxAltitudePitchText +
                    " deg; transport=ALT_TO_VS_TRAJECTORY.");
            }
            else
            {
                if (verticalSpeed != null) verticalSpeed.ClearAltitudeVerticalSpeedDemand();
                ResetOutputs("Inactive");
                AERISLogger.Info("[ALT] disarmed.");
            }
        }

        // ALT remains armed but non-executing on the ground.  Seed its motion planner
        // from the measured climb state immediately before the first active frame.  The
        // altitude target remains untouched; only the planner's initial condition moves
        // from the standby zero to the real aircraft rate.
        internal void PreparePostTakeoffActivation(Vessel vessel,
            VirtualAttitudeInstrument attitude, string source)
        {
            float altitudeSample = vessel != null ? (float)vessel.altitude : 0f;
            if (!Armed || vessel == null || attitude == null ||
                !attitude.InstrumentPitchValid || !attitude.SharedSurfaceSpeedValid ||
                !attitude.SharedDynamicPressureValid || !attitude.VerticalSpeedValid ||
                !IsFinite(altitudeSample) ||
                !IsFinite(attitude.VerticalSpeedMps)) return;

            CurrentAltitudeMeters = Mathf.Max(0f, altitudeSample);
            CurrentVerticalSpeedMps = attitude.VerticalSpeedMps;
            float rateSeedMps = Mathf.Clamp(CurrentVerticalSpeedMps,
                -MaxAltitudeVerticalSpeedMps, MaxAltitudeVerticalSpeedMps);
            DesiredVerticalSpeedMps = rateSeedMps;
            PlannedVerticalSpeedMps = rateSeedMps;
            AltitudeRateDemandMps = rateSeedMps;
            AltitudeReferenceVerticalSpeedMps = CurrentVerticalSpeedMps;
            AltitudeReconciledVerticalSpeedMps = CurrentVerticalSpeedMps;
            AltitudeRateBiasMps = 0f;
            AltitudeRateReconciliationActive = false;
            AltitudeRateReconciliationBlend = 0f;
            AltitudeRateCommandBiasMps = 0f;
            haveAltitudeReferenceSample = false;
            lastAltitudeReferenceMeters = CurrentAltitudeMeters;
            lastAltitudeReferenceFixedTime = Time.fixedTime;
            TargetChangedSinceUpdate = false;
            ControlState = "HandoffSeeded";

            AERISLogger.Info("[ALT] post-takeoff planner seeded after " + source +
                ": altitude=" + CurrentAltitudeMeters.ToString("F2") + " m; planned V/S=" +
                PlannedVerticalSpeedMps.ToString("F2") + " m/s; prepared target preserved=" +
                TargetAltitudeText + " m ASL.");
        }

        internal void Disable(string reason, AERISVerticalSpeedDirector verticalSpeed)
        {
            if (!Armed && !ControlActive) return;
            Armed = false;
            if (verticalSpeed != null) verticalSpeed.ClearAltitudeVerticalSpeedDemand();
            ResetOutputs("Inactive");
            AERISLogger.Info("[ALT] disabled: " + reason);
        }

        void UpdateAltitudeHoldBandReference()
        {
            float lowerOffset = Mathf.Min(AltitudeHoldBandLowerOffsetMeters,
                AltitudeHoldBandUpperOffsetMeters);
            float upperOffset = Mathf.Max(AltitudeHoldBandLowerOffsetMeters,
                AltitudeHoldBandUpperOffsetMeters);
            AltitudeHoldBandLowerMeters = Mathf.Max(0f,
                TargetAltitudeMeters + lowerOffset);
            AltitudeHoldBandUpperMeters = Mathf.Max(AltitudeHoldBandLowerMeters,
                TargetAltitudeMeters + upperOffset);
            AltitudeHoldReferenceMeters = Mathf.Clamp(
                TargetAltitudeMeters + AltitudeHoldReferenceCommandOffsetMeters,
                AltitudeHoldBandLowerMeters,
                AltitudeHoldBandUpperMeters);
            AltitudeHoldReferenceOffsetMeters =
                AltitudeHoldReferenceMeters - TargetAltitudeMeters;
        }

        void UpdateAltitudeErrors()
        {
            AltitudeErrorMeters = TargetAltitudeMeters - CurrentAltitudeMeters;
            AltitudeControlErrorMeters =
                AltitudeHoldReferenceMeters - CurrentAltitudeMeters;
            if (CurrentAltitudeMeters < AltitudeHoldBandLowerMeters)
                AltitudeHoldBandErrorMeters =
                    AltitudeHoldBandLowerMeters - CurrentAltitudeMeters;
            else if (CurrentAltitudeMeters > AltitudeHoldBandUpperMeters)
                AltitudeHoldBandErrorMeters =
                    AltitudeHoldBandUpperMeters - CurrentAltitudeMeters;
            else
                AltitudeHoldBandErrorMeters = 0f;
            AltitudeInsidePreferredHoldBand =
                AltitudeHoldBandErrorMeters == 0f;
        }

        internal void SetCurrent(Vessel vessel)
        {
            if (vessel == null) return;
            float altitudeSample = (float)vessel.altitude;
            if (!IsFinite(altitudeSample)) return;
            TargetAltitudeMeters = Mathf.Max(0f, altitudeSample);
            TargetAltitudeText = TargetAltitudeMeters.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            UpdateAltitudeHoldBandReference();
            TargetChangedSinceUpdate = true;
            AERISLogger.Info("[ALT] target set to current altitude=" + TargetAltitudeText +
                " m ASL; display-safe hold band=" +
                AltitudeHoldBandLowerMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                ".." + AltitudeHoldBandUpperMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                " m; control reference=" + ((double)AltitudeHoldReferenceMeters).ToString("0.0000",
                    System.Globalization.CultureInfo.InvariantCulture) + " m.");
        }

        internal bool TrySetTarget(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter an altitude from 0 to 1000000 m.";
                return false;
            }
            if (value < 0f || value > 1000000f)
            {
                error = "Altitude target must be between 0 and 1000000 m.";
                return false;
            }
            TargetAltitudeMeters = value;
            TargetAltitudeText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            UpdateAltitudeHoldBandReference();
            TargetChangedSinceUpdate = true;
            AERISLogger.Info("[ALT] target=" + TargetAltitudeText +
                " m ASL; display-safe hold band=" +
                AltitudeHoldBandLowerMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                ".." + AltitudeHoldBandUpperMeters.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                " m; control reference=" + ((double)AltitudeHoldReferenceMeters).ToString("0.0000",
                    System.Globalization.CultureInfo.InvariantCulture) + " m.");
            return true;
        }

        internal bool TrySetMaxVerticalSpeed(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric maximum altitude V/S.";
                return false;
            }
            if (value < 0.5f || value > 100f)
            {
                error = "ALT maximum V/S must be between 0.5 and 100 m/s.";
                return false;
            }
            MaxAltitudeVerticalSpeedMps = value;
            MaxAltitudeVerticalSpeedText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            AERISLogger.Info("[ALT] max V/S=" + MaxAltitudeVerticalSpeedText + " m/s.");
            return true;
        }

        internal bool TrySetMaxPitch(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) && !float.TryParse(text, out value)) ||
                float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric ALT maximum pitch limit.";
                return false;
            }
            if (value < 0f || value > 90f)
            {
                error = "ALT maximum pitch limit must be between 0 and 90 degrees.";
                return false;
            }
            MaxAltitudePitchDeg = value;
            MaxAltitudePitchText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            AERISLogger.Info("[ALT] max pitch=" + MaxAltitudePitchText + " deg.");
            return true;
        }

        int MicroTrimHistoryIndexFromOldest(int chronologicalOffset)
        {
            int oldest = microTrimObserverWriteIndex - microTrimObserverCount;
            while (oldest < 0) oldest += MicroTrimObserverCapacity;
            return (oldest + chronologicalOffset) % MicroTrimObserverCapacity;
        }

        void ResetMicroTrimObserver()
        {
            Array.Clear(microTrimObserverInputRateMps, 0, microTrimObserverInputRateMps.Length);
            Array.Clear(microTrimObserverOutputRateMps, 0, microTrimObserverOutputRateMps.Length);
            microTrimObserverWriteIndex = 0;
            microTrimObserverCount = 0;
            microTrimValidHalfCycleCount = 0;
            microTrimObserverSampleAccumulator = 0f;
            microTrimObserverEstimateAccumulator = 0f;
            microTrimLastErrorSign = 0f;
            microTrimLastCrossingFixedTime = -1f;
            MicroTrimObserverReady = false;
            MicroTrimObserverCorrelation = 0f;
            MicroTrimLearnedCyclePeriodSeconds = 12.0f;
            MicroTrimLearnedHalfCycleSeconds = 6.0f;
            MicroTrimTargetCrossingCount = 0;
            MicroTrimLastCrossingRateMps = 0f;
            MicroTrimObserverInputCommandMps = 0f;
            MicroTrimObserverBaseCommandMps = 0f;
        }

        void CancelMicroTrimPulseAndSchedule()
        {
            MicroTrimEligible = false;
            MicroTrimPulseActive = false;
            MicroTrimPulseScheduled = false;
            MicroTrimPulseRateMps = 0f;
            MicroTrimAppliedRateMps = 0f;
            MicroTrimPulseElapsedSeconds = 0f;
            MicroTrimWaitElapsedSeconds = 0f;
            MicroTrimScheduledWaitSeconds = 0f;
            MicroTrimPredictedFutureRateMps = 0f;
            MicroTrimBaseRawRateMps = 0f;
            MicroTrimSafeMagnitudeMps = 0f;
            MicroTrimFutureHalfCycles = 0;
            microTrimScheduledDirection = 0f;
            microTrimScheduledElapsedSeconds = 0f;
            microTrimStoredPulseMagnitudeMps = 0f;
            microTrimPulseDirection = 0f;
            MicroTrimBiasPulseScale = 1f;
        }

        void ResetMicroTrimForVessel(Vessel vessel)
        {
            microTrimVesselId = vessel != null ? vessel.id : Guid.Empty;
            CancelMicroTrimPulseAndSchedule();
            ResetMicroTrimObserver();
            MicroTrimObservationActive = false;
            MicroTrimLearnedPulseMagnitudeMps = 0.012f;
            MicroTrimLearnedPulseDurationSeconds = 0.35f;
            MicroTrimLearnedWaitSeconds = 1.0f;
            MicroTrimLearnedDelaySeconds = 4.0f;
            MicroTrimLearnedResponseGain = 0.60f;
            MicroTrimObservedResponseMps = 0f;
            MicroTrimPulseCount = 0;
            microTrimLastAppliedPulseDirection = 0f;
            MicroTrimLastAppliedPulseDirection = 0f;
            MicroTrimPositivePulseCount = 0;
            MicroTrimNegativePulseCount = 0;
            MicroTrimPairGuardActive = false;
            microTrimBiasEstimateMeters = 0f;
            microTrimBiasGuardLatched = false;
            microTrimBiasGuardElapsedSeconds = 0f;
            MicroTrimBiasEstimateMeters = 0f;
            MicroTrimBiasGuardActive = false;
            MicroTrimBiasGuardElapsedSeconds = 0f;
            MicroTrimBiasRecoveryActive = false;
            MicroTrimBiasRecoveryBlend = 0f;
            MicroTrimBiasCorrectiveDirection = 0f;
            MicroTrimBiasPulseScale = 1f;
            microTrimBiasHardGuardLatched = false;
            MicroTrimBiasHardGuardActive = false;
            MicroTrimBiasHardGuardRecoveryPermitted = false;
            MicroTrimBiasHardGuardInhibitActive = false;
            MicroTrimBiasHardGuardReason = "Inactive";
        }

        void SuspendAdaptiveMicroTrim()
        {
            bool hadObserverState = microTrimObserverCount > 0 ||
                microTrimValidHalfCycleCount > 0 ||
                microTrimLastErrorSign != 0f ||
                microTrimLastCrossingFixedTime >= 0f ||
                MicroTrimPulseActive || MicroTrimPulseScheduled;
            CancelMicroTrimPulseAndSchedule();
            if (hadObserverState) ResetMicroTrimObserver();
            MicroTrimObservationActive = false;
            microTrimLastAppliedPulseDirection = 0f;
            MicroTrimLastAppliedPulseDirection = 0f;
            MicroTrimPairGuardActive = false;
            microTrimBiasEstimateMeters = 0f;
            microTrimBiasGuardLatched = false;
            microTrimBiasGuardElapsedSeconds = 0f;
            MicroTrimBiasEstimateMeters = 0f;
            MicroTrimBiasGuardActive = false;
            MicroTrimBiasGuardElapsedSeconds = 0f;
            MicroTrimBiasRecoveryActive = false;
            MicroTrimBiasRecoveryBlend = 0f;
            MicroTrimBiasCorrectiveDirection = 0f;
            MicroTrimBiasPulseScale = 1f;
            microTrimBiasHardGuardLatched = false;
            MicroTrimBiasHardGuardActive = false;
            MicroTrimBiasHardGuardRecoveryPermitted = false;
            MicroTrimBiasHardGuardInhibitActive = false;
            MicroTrimBiasHardGuardReason = "Inactive";
        }

        void AddMicroTrimObserverSample(float inputRateMps, float outputRateMps)
        {
            microTrimObserverInputRateMps[microTrimObserverWriteIndex] = inputRateMps;
            microTrimObserverOutputRateMps[microTrimObserverWriteIndex] = outputRateMps;
            microTrimObserverWriteIndex =
                (microTrimObserverWriteIndex + 1) % MicroTrimObserverCapacity;
            if (microTrimObserverCount < MicroTrimObserverCapacity)
                microTrimObserverCount++;
        }

        void EstimateMicroTrimPlant()
        {
            if (microTrimObserverCount < 80)
            {
                MicroTrimObserverReady = false;
                return;
            }

            int minimumLagSamples = 15; // 1.5 s
            int maximumLagSamples = Mathf.Min(75, microTrimObserverCount - 40);
            float bestCorrelation = -1f;
            float bestGain = 0f;
            int bestLagSamples = 0;

            for (int lag = minimumLagSamples; lag <= maximumLagSamples; lag++)
            {
                int overlap = microTrimObserverCount - lag;
                if (overlap < 40) continue;

                double inputMean = 0.0;
                double outputMean = 0.0;
                for (int sample = 0; sample < overlap; sample++)
                {
                    int inputIndex = MicroTrimHistoryIndexFromOldest(sample);
                    int outputIndex = MicroTrimHistoryIndexFromOldest(sample + lag);
                    inputMean += microTrimObserverInputRateMps[inputIndex];
                    outputMean += microTrimObserverOutputRateMps[outputIndex];
                }
                inputMean /= overlap;
                outputMean /= overlap;

                double covariance = 0.0;
                double inputVariance = 0.0;
                double outputVariance = 0.0;
                for (int sample = 0; sample < overlap; sample++)
                {
                    int inputIndex = MicroTrimHistoryIndexFromOldest(sample);
                    int outputIndex = MicroTrimHistoryIndexFromOldest(sample + lag);
                    double inputDelta =
                        microTrimObserverInputRateMps[inputIndex] - inputMean;
                    double outputDelta =
                        microTrimObserverOutputRateMps[outputIndex] - outputMean;
                    covariance += inputDelta * outputDelta;
                    inputVariance += inputDelta * inputDelta;
                    outputVariance += outputDelta * outputDelta;
                }

                if (inputVariance < 1.0e-7 || outputVariance < 1.0e-7) continue;
                float correlation = (float)(covariance /
                    Math.Sqrt(inputVariance * outputVariance));
                float gain = (float)(covariance / inputVariance);
                if (correlation > bestCorrelation && gain > 0f)
                {
                    bestCorrelation = correlation;
                    bestGain = gain;
                    bestLagSamples = lag;
                }
            }

            if (bestLagSamples <= 0)
            {
                MicroTrimObserverReady = false;
                return;
            }

            float measuredDelay =
                bestLagSamples * MicroTrimObserverSamplePeriodSeconds;
            float measuredGain = Mathf.Clamp(bestGain, 0.10f, 2.0f);
            if (MicroTrimObserverCorrelation <= 0.0001f)
            {
                MicroTrimLearnedDelaySeconds = measuredDelay;
                MicroTrimLearnedResponseGain = measuredGain;
                MicroTrimObserverCorrelation = bestCorrelation;
            }
            else
            {
                MicroTrimLearnedDelaySeconds = Mathf.Lerp(
                    MicroTrimLearnedDelaySeconds, measuredDelay, 0.20f);
                MicroTrimLearnedResponseGain = Mathf.Lerp(
                    MicroTrimLearnedResponseGain, measuredGain, 0.20f);
                MicroTrimObserverCorrelation = Mathf.Lerp(
                    MicroTrimObserverCorrelation, bestCorrelation, 0.20f);
            }

            MicroTrimObserverReady =
                MicroTrimObserverCorrelation >= MicroTrimMinimumObserverCorrelation &&
                microTrimValidHalfCycleCount >= 2 &&
                MicroTrimLearnedHalfCycleSeconds >= 2.0f &&
                MicroTrimLearnedHalfCycleSeconds <= 15.0f &&
                MicroTrimLearnedDelaySeconds >= 0.5f &&
                MicroTrimLearnedDelaySeconds <= 12.0f;
        }

        void UpdateMicroTrimObserver(float inputRateMps, float outputRateMps, float dt)
        {
            microTrimObserverSampleAccumulator += dt;
            while (microTrimObserverSampleAccumulator >=
                MicroTrimObserverSamplePeriodSeconds)
            {
                microTrimObserverSampleAccumulator -=
                    MicroTrimObserverSamplePeriodSeconds;
                AddMicroTrimObserverSample(inputRateMps, outputRateMps);
            }

            microTrimObserverEstimateAccumulator += dt;
            if (microTrimObserverEstimateAccumulator >= 1.0f)
            {
                microTrimObserverEstimateAccumulator = 0f;
                EstimateMicroTrimPlant();
            }
            MicroTrimObservationActive = microTrimObserverCount >= 40;
        }

        void UpdateMicroTrimBiasGuard(float dt)
        {
            float tau = Mathf.Max(1.0f, MicroTrimBiasEstimateTimeConstantSeconds);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, dt) / tau);
            microTrimBiasEstimateMeters = Mathf.Lerp(
                microTrimBiasEstimateMeters, AltitudeControlErrorMeters, blend);

            float absBias = Mathf.Abs(microTrimBiasEstimateMeters);
            if (!microTrimBiasGuardLatched)
            {
                if (absBias >= Mathf.Max(
                    MicroTrimBiasGuardExitMeters,
                    MicroTrimBiasGuardEnterMeters))
                {
                    microTrimBiasGuardLatched = true;
                }
            }
            else if (absBias <= Mathf.Min(
                MicroTrimBiasGuardEnterMeters,
                MicroTrimBiasGuardExitMeters))
            {
                microTrimBiasGuardLatched = false;
            }

            if (!microTrimBiasHardGuardLatched)
            {
                if (absBias >= Mathf.Max(
                    MicroTrimBiasHardGuardExitMeters,
                    MicroTrimBiasHardGuardEnterMeters))
                {
                    microTrimBiasHardGuardLatched = true;
                }
            }
            else if (absBias <= Mathf.Min(
                MicroTrimBiasHardGuardEnterMeters,
                MicroTrimBiasHardGuardExitMeters))
            {
                microTrimBiasHardGuardLatched = false;
            }

            if (microTrimBiasGuardLatched)
                microTrimBiasGuardElapsedSeconds += Mathf.Max(0f, dt);
            else
                microTrimBiasGuardElapsedSeconds = 0f;

            float recoveryBlend = microTrimBiasGuardLatched
                ? Mathf.InverseLerp(
                    Mathf.Min(MicroTrimBiasGuardEnterMeters,
                        MicroTrimBiasGuardExitMeters),
                    Mathf.Max(MicroTrimBiasRecoveryFullMeters,
                        MicroTrimBiasGuardEnterMeters),
                    absBias)
                : 0f;

            MicroTrimBiasEstimateMeters = microTrimBiasEstimateMeters;
            // Compatibility field: in v0.8.27 this means pair-balanced bias
            // recovery is active.  It no longer suppresses all Micro-Trim pulses.
            MicroTrimBiasGuardActive = microTrimBiasGuardLatched;
            MicroTrimBiasGuardElapsedSeconds =
                microTrimBiasGuardElapsedSeconds;
            MicroTrimBiasRecoveryActive = microTrimBiasGuardLatched;
            MicroTrimBiasRecoveryBlend = Mathf.Clamp01(recoveryBlend);
            MicroTrimBiasCorrectiveDirection = absBias > 0.0001f
                ? Mathf.Sign(microTrimBiasEstimateMeters) : 0f;
            MicroTrimBiasHardGuardActive = microTrimBiasHardGuardLatched;
        }

        void UpdateMicroTrimBiasHardGuardDisposition(float absError,
            float physicalRateMps)
        {
            MicroTrimBiasHardGuardRecoveryPermitted = false;
            MicroTrimBiasHardGuardInhibitActive = false;
            MicroTrimBiasHardGuardReason = "Inactive";

            if (!MicroTrimBiasHardGuardActive) return;

            if (!MicroTrimEnabled)
                MicroTrimBiasHardGuardReason = "MicroTrimDisabled";
            else if (!HoldLatched)
                MicroTrimBiasHardGuardReason = "HoldNotLatched";
            else if (MicroTrimObserverCorrelation <
                MicroTrimMinimumObserverCorrelation)
                MicroTrimBiasHardGuardReason = "ObserverCorrelationLow";
            else if (!MicroTrimObserverReady)
                MicroTrimBiasHardGuardReason = "ObserverNotReady";
            else if (LowQVerticalEnvelopeBlend < MicroTrimMinimumLowQBlend)
                MicroTrimBiasHardGuardReason = "LowQEnvelopeIneligible";
            else if (BankVerticalSupportActive)
                MicroTrimBiasHardGuardReason = "BankSupportActive";
            else if (HoldDisturbanceRecoveryActive)
                MicroTrimBiasHardGuardReason = "DisturbanceRecovery";
            else if (absError > 0.40f)
                MicroTrimBiasHardGuardReason = "OutsideFineErrorEnvelope";
            else if (Mathf.Abs(physicalRateMps) > 0.25f)
                MicroTrimBiasHardGuardReason = "RateEnvelopeExceeded";
            else
            {
                // High bias requests bounded recovery; it is not by itself evidence
                // that the observer has failed. Preserve the existing absolute and
                // cancellation-fraction caps plus strict direction alternation.
                MicroTrimBiasHardGuardRecoveryPermitted = true;
                MicroTrimBiasHardGuardReason = "HealthyBoundedRecovery";
            }

            MicroTrimBiasHardGuardInhibitActive =
                !MicroTrimBiasHardGuardRecoveryPermitted;
        }

        float GetMicroTrimBiasPulseScale(float pulseDirection)
        {
            if (!MicroTrimBiasRecoveryActive ||
                MicroTrimBiasRecoveryBlend <= 0f ||
                pulseDirection == 0f ||
                MicroTrimBiasCorrectiveDirection == 0f)
            {
                return 1f;
            }

            bool corrective = pulseDirection ==
                MicroTrimBiasCorrectiveDirection;
            float targetScale = corrective
                ? Mathf.Max(1f, MicroTrimBiasCorrectivePulseScale)
                : Mathf.Clamp(MicroTrimBiasOpposingPulseScale, 0.10f, 1f);
            return Mathf.Lerp(1f, targetScale,
                MicroTrimBiasRecoveryBlend);
        }

        void ScheduleMicroTrimFromCrossing(float currentPhysicalRateMps)
        {
            MicroTrimPairGuardActive = false;
            if (!MicroTrimObserverReady || MicroTrimPulseActive ||
                MicroTrimPulseScheduled) return;

            float halfCycle = Mathf.Clamp(
                MicroTrimLearnedHalfCycleSeconds, 2.0f, 15.0f);
            int futureHalfCycles = Mathf.Clamp(
                Mathf.CeilToInt(MicroTrimLearnedDelaySeconds /
                    Mathf.Max(0.10f, halfCycle)), 1, 3);
            float scheduledWait = futureHalfCycles * halfCycle -
                MicroTrimLearnedDelaySeconds;
            scheduledWait = Mathf.Clamp(scheduledWait, 0f, halfCycle);

            float currentMotionSign = Mathf.Abs(currentPhysicalRateMps) > 0.02f
                ? Mathf.Sign(currentPhysicalRateMps)
                : -Mathf.Sign(AltitudeControlErrorMeters);
            if (currentMotionSign == 0f) return;
            float futureMotionSign = (futureHalfCycles % 2 == 0)
                ? currentMotionSign : -currentMotionSign;
            microTrimScheduledDirection = -futureMotionSign;

            // Fine trim must remain zero-mean.  If a missed/aborted crossing would
            // schedule the same pulse direction twice, skip this crossing and wait
            // for the opposite half-cycle instead of accumulating a one-sided bias.
            if (microTrimLastAppliedPulseDirection != 0f &&
                microTrimScheduledDirection ==
                    microTrimLastAppliedPulseDirection)
            {
                MicroTrimPairGuardActive = true;
                microTrimScheduledDirection = 0f;
                return;
            }

            float desiredPulse = MicroTrimTargetRateReductionMps /
                Mathf.Max(0.15f, MicroTrimLearnedResponseGain);
            MicroTrimLearnedPulseMagnitudeMps = Mathf.Lerp(
                MicroTrimLearnedPulseMagnitudeMps,
                Mathf.Clamp(desiredPulse,
                    MicroTrimMinimumPulseMagnitudeMps,
                    MicroTrimMaximumPulseMagnitudeMps),
                0.25f);
            MicroTrimLearnedPulseDurationSeconds = Mathf.Clamp(
                halfCycle * 0.06f, 0.25f, 0.60f);
            MicroTrimLearnedWaitSeconds = scheduledWait;
            MicroTrimScheduledWaitSeconds = scheduledWait;
            MicroTrimWaitElapsedSeconds = 0f;
            microTrimScheduledElapsedSeconds = 0f;
            MicroTrimFutureHalfCycles = futureHalfCycles;
            MicroTrimPredictedFutureRateMps =
                futureMotionSign * Mathf.Abs(currentPhysicalRateMps);
            MicroTrimPulseScheduled = true;
        }

        void UpdateMicroTrimCrossings(float absError, float physicalRateMps)
        {
            float errorSign = absError >= 0.01f
                ? Mathf.Sign(AltitudeControlErrorMeters) : 0f;
            if (errorSign == 0f) return;

            float now = Time.fixedTime;
            if (microTrimLastErrorSign != 0f &&
                errorSign != microTrimLastErrorSign &&
                (microTrimLastCrossingFixedTime < 0f ||
                    now - microTrimLastCrossingFixedTime >= 1.5f))
            {
                MicroTrimTargetCrossingCount++;
                MicroTrimLastCrossingRateMps = physicalRateMps;
                MicroTrimObservedResponseMps = Mathf.Abs(physicalRateMps);

                if (microTrimLastCrossingFixedTime >= 0f)
                {
                    float measuredHalfCycle =
                        now - microTrimLastCrossingFixedTime;
                    if (measuredHalfCycle >= 2.0f &&
                        measuredHalfCycle <= 15.0f)
                    {
                        if (microTrimValidHalfCycleCount == 0)
                            MicroTrimLearnedHalfCycleSeconds =
                                measuredHalfCycle;
                        else
                            MicroTrimLearnedHalfCycleSeconds = Mathf.Lerp(
                                MicroTrimLearnedHalfCycleSeconds,
                                measuredHalfCycle, 0.25f);
                        MicroTrimLearnedCyclePeriodSeconds =
                            MicroTrimLearnedHalfCycleSeconds * 2f;
                        microTrimValidHalfCycleCount++;
                    }
                }

                microTrimLastCrossingFixedTime = now;
                ScheduleMicroTrimFromCrossing(physicalRateMps);
            }
            microTrimLastErrorSign = errorSign;
        }

        float ApplyMicroTrimPulse(float baseRawRateMps)
        {
            MicroTrimBaseRawRateMps = baseRawRateMps;
            MicroTrimAppliedRateMps = 0f;
            MicroTrimPulseRateMps = 0f;
            MicroTrimSafeMagnitudeMps = 0f;

            if (!MicroTrimPulseActive || microTrimPulseDirection == 0f)
                return 0f;

            // A phase pulse is cancellation-only. If the raw command has already
            // changed phase, end the pulse rather than creating a new command.
            if (microTrimPulseDirection * baseRawRateMps >= -0.0001f)
            {
                MicroTrimPulseActive = false;
                MicroTrimPulseElapsedSeconds = 0f;
                MicroTrimBiasPulseScale = 1f;
                return 0f;
            }

            float safeMagnitude = Mathf.Min(
                microTrimStoredPulseMagnitudeMps,
                Mathf.Abs(baseRawRateMps) *
                    Mathf.Clamp01(MicroTrimMaximumRawCancellationFraction));
            if (safeMagnitude <= 0.0001f)
            {
                MicroTrimPulseActive = false;
                MicroTrimPulseElapsedSeconds = 0f;
                MicroTrimBiasPulseScale = 1f;
                return 0f;
            }

            MicroTrimSafeMagnitudeMps = safeMagnitude;
            MicroTrimPulseRateMps =
                microTrimPulseDirection * safeMagnitude;
            MicroTrimAppliedRateMps = MicroTrimPulseRateMps;
            return MicroTrimAppliedRateMps;
        }

        void UpdateAdaptiveMicroTrim(Vessel vessel, float absError,
            float baseRawRateMps)
        {
            if (vessel == null || microTrimVesselId != vessel.id)
                ResetMicroTrimForVessel(vessel);

            float dt = Mathf.Max(0.001f, Time.fixedDeltaTime);
            float physicalRate =
                AltitudePrecisionReferenceVerticalSpeedMps;

            // Crossing scheduling uses the last completed observer estimate.  The
            // current frame's observer sample is committed after the pulse decision
            // so the input history contains the actual base-plus-pulse command.
            MicroTrimPairGuardActive = false;
            UpdateMicroTrimBiasGuard(dt);
            UpdateMicroTrimCrossings(absError, physicalRate);
            UpdateMicroTrimBiasHardGuardDisposition(absError, physicalRate);

            bool commonSafetyEligible =
                MicroTrimEnabled && HoldLatched &&
                MicroTrimObserverReady &&
                LowQVerticalEnvelopeBlend >= MicroTrimMinimumLowQBlend &&
                !BankVerticalSupportActive &&
                !HoldDisturbanceRecoveryActive &&
                !MicroTrimBiasHardGuardInhibitActive &&
                absError <= 0.40f &&
                Mathf.Abs(physicalRate) <= 0.25f;

            if (!commonSafetyEligible)
            {
                CancelMicroTrimPulseAndSchedule();
            }
            else if (MicroTrimPulseActive)
            {
                MicroTrimPulseElapsedSeconds += dt;
                ApplyMicroTrimPulse(baseRawRateMps);
                if (MicroTrimPulseElapsedSeconds >=
                    MicroTrimLearnedPulseDurationSeconds)
                {
                    MicroTrimPulseActive = false;
                    MicroTrimPulseElapsedSeconds = 0f;
                    MicroTrimPulseRateMps = 0f;
                    MicroTrimAppliedRateMps = 0f;
                    MicroTrimSafeMagnitudeMps = 0f;
                    MicroTrimBiasPulseScale = 1f;
                }
            }
            else if (!MicroTrimPulseScheduled)
            {
                MicroTrimAppliedRateMps = 0f;
                MicroTrimPulseRateMps = 0f;
                MicroTrimWaitElapsedSeconds = 0f;
            }
            else
            {
                microTrimScheduledElapsedSeconds += dt;
                MicroTrimWaitElapsedSeconds =
                    microTrimScheduledElapsedSeconds;

                if (microTrimScheduledElapsedSeconds >=
                    MicroTrimScheduledWaitSeconds)
                {
                    // Start only while the scheduled pulse cancels the existing raw
                    // command. Otherwise discard this half-cycle.
                    if (microTrimScheduledDirection *
                        baseRawRateMps >= -0.0001f)
                    {
                        CancelMicroTrimPulseAndSchedule();
                    }
                    else if (microTrimLastAppliedPulseDirection != 0f &&
                        microTrimScheduledDirection ==
                            microTrimLastAppliedPulseDirection)
                    {
                        CancelMicroTrimPulseAndSchedule();
                        MicroTrimPairGuardActive = true;
                    }
                    else
                    {
                        MicroTrimPulseScheduled = false;
                        MicroTrimPulseActive = true;
                        MicroTrimPulseElapsedSeconds = 0f;
                        microTrimPulseDirection =
                            microTrimScheduledDirection;
                        MicroTrimBiasPulseScale =
                            GetMicroTrimBiasPulseScale(
                                microTrimPulseDirection);
                        microTrimStoredPulseMagnitudeMps = Mathf.Clamp(
                            MicroTrimLearnedPulseMagnitudeMps *
                                MicroTrimBiasPulseScale,
                            MicroTrimMinimumPulseMagnitudeMps * 0.50f,
                            MicroTrimMaximumPulseMagnitudeMps);
                        ApplyMicroTrimPulse(baseRawRateMps);
                        if (Mathf.Abs(MicroTrimAppliedRateMps) > 0.0001f)
                        {
                            MicroTrimPulseCount++;
                            microTrimLastAppliedPulseDirection =
                                microTrimPulseDirection;
                            MicroTrimLastAppliedPulseDirection =
                                microTrimLastAppliedPulseDirection;
                            if (microTrimPulseDirection > 0f)
                                MicroTrimPositivePulseCount++;
                            else if (microTrimPulseDirection < 0f)
                                MicroTrimNegativePulseCount++;
                        }
                    }
                }
                else
                {
                    MicroTrimAppliedRateMps = 0f;
                    MicroTrimPulseRateMps = 0f;
                }
            }

            MicroTrimEligible = commonSafetyEligible &&
                (MicroTrimPulseScheduled || MicroTrimPulseActive);

            MicroTrimObserverBaseCommandMps = baseRawRateMps;
            MicroTrimObserverInputCommandMps =
                baseRawRateMps + MicroTrimAppliedRateMps;
            UpdateMicroTrimObserver(
                MicroTrimObserverInputCommandMps, physicalRate, dt);
        }

        internal void Update(Vessel vessel, VirtualAttitudeInstrument attitude,
            AERISVerticalSpeedDirector verticalSpeed, bool aerisMaster, bool standardFbwActive)
        {
            float altitudeSample = vessel != null ? (float)vessel.altitude : 0f;
            bool sensorValid = attitude != null && attitude.InstrumentPitchValid &&
                attitude.SharedSurfaceSpeedValid && attitude.SharedDynamicPressureValid &&
                attitude.VerticalSpeedValid &&
                IsFinite(attitude.VerticalSpeedMps) && IsFinite(attitude.DynamicPressureKpa) &&
                IsFinite(altitudeSample);
            bool executable = Armed && aerisMaster && standardFbwActive && vessel != null &&
                !vessel.packed && !vessel.LandedOrSplashed && vessel.situation != Vessel.Situations.PRELAUNCH &&
                sensorValid && verticalSpeed != null && verticalSpeed.Armed;
            if (!executable)
            {
                if (verticalSpeed != null) verticalSpeed.ClearAltitudeVerticalSpeedDemand();
                ResetOutputs(Armed ? "Standby" : "Inactive");
                return;
            }

            ControlActive = true;
            CurrentAltitudeMeters = Mathf.Max(0f, altitudeSample);
            CurrentVerticalSpeedMps = attitude.VerticalSpeedMps;
            UpdateAltitudeReferenceRate();
            // Establish the q schedule before any stopping-distance calculation.
            UpdateLowQVerticalEnvelope(attitude);
            UpdateAltitudeHoldBandReference();
            UpdateAltitudeErrors();
            float absError = Mathf.Abs(AltitudeControlErrorMeters);
            UpdateTerminalAltitudeRateReconciliation(absError);
            UpdateBankVerticalSupport(attitude, absError);
            AltitudePrecisionReferenceRateActive = AltitudeRateReconciliationActive;
            // v0.8.18: direct altitude derivative is retained in diagnostics only.
            // Use the established reconciled physical-rate estimate for PrecisionHold;
            // the command bias below still translates physical ALT demand into the V/S
            // inner-loop frame.
            AltitudePrecisionDirectReferenceRateActive = false;
            AltitudePrecisionReferenceVerticalSpeedMps =
                AltitudeRateReconciliationActive
                    ? AltitudeReconciledVerticalSpeedMps : CurrentVerticalSpeedMps;
            AltitudePrecisionReferenceDeltaVsReconciledMps =
                AltitudeRateReconciliationActive && haveAltitudeReferenceSample
                    ? AltitudeReferenceVerticalSpeedMps -
                        AltitudeReconciledVerticalSpeedMps
                    : 0f;
            float sign = absError > 0.0001f ? Mathf.Sign(AltitudeControlErrorMeters) : 0f;
            float towardTargetRate = sign * CurrentVerticalSpeedMps;
            float movingTowardRate = Mathf.Max(0f, towardTargetRate);

            // The physical stopping distance is extended by the measured transport / V/S
            // handoff lead.  This is the altitude equivalent of HDG's predicted-turn lead:
            // reduce the commanded V/S before the remaining altitude vanishes.
            float effectiveScheduledDecelMps2 = LowQVerticalEnvelopeEffectiveScheduledDecelMps2;
            StopDistanceMeters = movingTowardRate > 0.001f
                ? movingTowardRate * movingTowardRate / Mathf.Max(0.01f, 2f * effectiveScheduledDecelMps2)
                : 0f;

            // Use the difference between the real V/S and the already planned V/S
            // only when the plan is braking.  This avoids adding fake lead during the
            // initial acceleration while making the brake schedule acknowledge that the
            // plant is still carrying more vertical speed than AERIS requested.
            float plannedTowardRate = Mathf.Max(0f, sign * PlannedVerticalSpeedMps);
            bool measuredBrakeLagEligible = movingTowardRate > 0.25f &&
                                            plannedTowardRate < movingTowardRate - 0.10f;
            MeasuredBrakeLagRateMps = measuredBrakeLagEligible
                ? Mathf.Max(0f, movingTowardRate - plannedTowardRate) : 0f;
            MeasuredBrakeLagLeadMeters = measuredBrakeLagEligible
                ? Mathf.Min(MeasuredBrakeLagLeadMaxMeters,
                    MeasuredBrakeLagRateMps * MeasuredBrakeLagLeadSeconds) : 0f;
            TransportLeadMeters = movingTowardRate * TransportLeadSeconds + MeasuredBrakeLagLeadMeters;

            float trajectoryDistance = Mathf.Max(0f, absError - TransportLeadMeters);
            StoppingRateLimitMps = Mathf.Sqrt(Mathf.Max(0f,
                2f * effectiveScheduledDecelMps2 * trajectoryDistance));
            float desiredMagnitude = Mathf.Min(MaxAltitudeVerticalSpeedMps, StoppingRateLimitMps);
            DesiredVerticalSpeedMps = sign * desiredMagnitude;
            RolloutActive = movingTowardRate > 0.01f &&
                absError <= StopDistanceMeters + TransportLeadMeters + AltitudePrecisionExitBandMeters;

            float effectiveTerminalCorridorMeters = LowQVerticalEnvelopeEffectiveTerminalCorridorMeters;
            AltitudeTerminalEffectiveFineBandMeters = AltitudeTerminalFineBandMeters;
            AltitudeTerminalEffectiveMaxRateMps = AltitudeTerminalMaxRateMps;
            AltitudeTerminalInnerSettleEffectiveBandMeters = AltitudeTerminalInnerSettleNormalQBandMeters;
            AltitudeTerminalInnerSettleEffectiveExitBandMeters =
                AltitudeTerminalInnerSettleNormalQBandMeters * AltitudeTerminalInnerSettleExitBandMultiplier;
            AltitudeTerminalInnerSettleEffectiveMaxRateMps = AltitudeTerminalInnerSettleNormalQMaxRateMps;
            AltitudeTerminalInnerSettleEffectiveBrakeRateMps = AltitudeTerminalInnerSettleNormalQBrakeRateMps;
            AltitudeTerminalInnerSettleEffectiveDampingPerSec = AltitudeTerminalVerticalSpeedDampingPerSec;
            AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds = AltitudeTerminalPredictiveBrakeLeadSeconds;
            AltitudeTerminalPredictiveBrakeEffectiveBandMeters = AltitudeTerminalFineBandMeters;
            AltitudeTerminalPredictiveBrakeActive = false;
            AltitudeTerminalPredictiveBrakeInboundRateMps = 0f;
            AltitudeTerminalPredictiveBrakeTimeToTargetSeconds = 0f;
            AltitudeTerminalPredictiveBrakeDemandMps = 0f;
            if (HoldLatched || absError > effectiveTerminalCorridorMeters)
                AltitudeTerminalInnerSettleActive = false;
            if (absError <= effectiveTerminalCorridorMeters)
            {
                // Position alone cannot reject a turn-induced sink.  Add measured V/S
                // damping to the terminal trajectory so ALT asks for corrective lift
                // as soon as the flight path departs, rather than waiting for metres
                // of position error to accumulate.
                //
                // v0.8.4: inside the final few metres, schedule the terminal V/S cap
                // down before it reaches the V/S inner loop.  At q≈12.5 kPa the old
                // 0.80 m/s limit was still enough to excite a very slow altitude cycle;
                // the fine cap preserves recovery outside the last metres while making
                // sub-metre corrections intentionally quiet.
                float lowQFineBlend = Mathf.SmoothStep(0f, 1f, LowQVerticalEnvelopeBlend);
                float fineMaxRate = Mathf.Lerp(AltitudeTerminalFineMidQMaxRateMps,
                    AltitudeTerminalFineMaxRateMps, lowQFineBlend);
                AltitudeTerminalEffectiveFineBandMeters = Mathf.Lerp(
                    AltitudeTerminalFineBandMeters, AltitudeTerminalFineLowQBandMeters,
                    lowQFineBlend);
                float fineDistanceBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                    AltitudePrecisionExitBandMeters,
                    Mathf.Max(AltitudePrecisionExitBandMeters + 0.10f,
                        AltitudeTerminalEffectiveFineBandMeters),
                    absError));
                AltitudeTerminalEffectiveMaxRateMps = Mathf.Lerp(fineMaxRate,
                    AltitudeTerminalMaxRateMps, fineDistanceBlend);

                AltitudeTerminalInnerSettleEffectiveBandMeters = Mathf.Lerp(
                    AltitudeTerminalInnerSettleNormalQBandMeters,
                    AltitudeTerminalInnerSettleLowQBandMeters, lowQFineBlend);
                AltitudeTerminalInnerSettleEffectiveExitBandMeters =
                    AltitudeTerminalInnerSettleEffectiveBandMeters *
                    AltitudeTerminalInnerSettleExitBandMultiplier;
                AltitudeTerminalInnerSettleEffectiveMaxRateMps = Mathf.Lerp(
                    AltitudeTerminalInnerSettleNormalQMaxRateMps,
                    AltitudeTerminalInnerSettleLowQMaxRateMps, lowQFineBlend);
                AltitudeTerminalInnerSettleEffectiveBrakeRateMps = Mathf.Lerp(
                    AltitudeTerminalInnerSettleNormalQBrakeRateMps,
                    AltitudeTerminalInnerSettleLowQBrakeRateMps, lowQFineBlend);
                AltitudeTerminalInnerSettleEffectiveDampingPerSec = Mathf.Lerp(
                    AltitudeTerminalVerticalSpeedDampingPerSec,
                    AltitudeTerminalInnerSettleLowQDampingPerSec, lowQFineBlend);
                if (HoldLatched)
                {
                    AltitudeTerminalInnerSettleActive = false;
                }
                else if (AltitudeTerminalInnerSettleActive)
                {
                    if (absError > AltitudeTerminalInnerSettleEffectiveExitBandMeters)
                        AltitudeTerminalInnerSettleActive = false;
                }
                else if (absError <= AltitudeTerminalInnerSettleEffectiveBandMeters)
                {
                    AltitudeTerminalInnerSettleActive = true;
                }

                float terminalDampingPerSec = AltitudeTerminalInnerSettleActive
                    ? AltitudeTerminalInnerSettleEffectiveDampingPerSec
                    : AltitudeTerminalVerticalSpeedDampingPerSec;
                float terminalReferenceRate = AltitudeControlErrorMeters * AltitudeTerminalRateGainPerSec
                    - AltitudeReconciledVerticalSpeedMps * terminalDampingPerSec
                    + BankVerticalSupportRateMps;

                float terminalTargetDirection = absError > 0.0001f ? Mathf.Sign(AltitudeControlErrorMeters) : 0f;
                float terminalInboundReferenceRate = terminalTargetDirection != 0f
                    ? terminalTargetDirection * AltitudeReconciledVerticalSpeedMps : 0f;
                AltitudeTerminalPredictiveBrakeInboundRateMps = terminalInboundReferenceRate;
                AltitudeTerminalPredictiveBrakeTimeToTargetSeconds = terminalInboundReferenceRate > 0.001f
                    ? absError / Mathf.Max(0.001f, terminalInboundReferenceRate) : 999f;

                float terminalTowardRateLimit = AltitudeTerminalEffectiveMaxRateMps;
                float terminalBrakeRateLimit = AltitudeTerminalEffectiveMaxRateMps;
                if (AltitudeTerminalInnerSettleActive)
                {
                    terminalTowardRateLimit = Mathf.Min(terminalTowardRateLimit,
                        AltitudeTerminalInnerSettleEffectiveMaxRateMps);
                    terminalBrakeRateLimit = Mathf.Max(terminalTowardRateLimit,
                        AltitudeTerminalInnerSettleEffectiveBrakeRateMps);
                }
                AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds = Mathf.Lerp(
                    AltitudeTerminalPredictiveBrakeLeadSeconds,
                    AltitudeTerminalPredictiveBrakeLowQLeadSeconds, lowQFineBlend);
                AltitudeTerminalPredictiveBrakeEffectiveBandMeters =
                    AltitudeTerminalEffectiveFineBandMeters;
                AltitudeTerminalPredictiveBrakeActive = !HoldLatched &&
                    !AltitudeTerminalInnerSettleActive &&
                    terminalTargetDirection != 0f &&
                    absError <= AltitudeTerminalPredictiveBrakeEffectiveBandMeters &&
                    terminalInboundReferenceRate > AltitudeTerminalPredictiveBrakeStartRateMps &&
                    AltitudeTerminalPredictiveBrakeTimeToTargetSeconds <=
                        AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds;
                if (AltitudeTerminalPredictiveBrakeActive)
                {
                    terminalBrakeRateLimit = Mathf.Max(terminalBrakeRateLimit,
                        AltitudeTerminalPredictiveBrakeMaxRateMps);
                    float brakeMagnitude = Mathf.Clamp(
                        AltitudeTerminalPredictiveBrakeMinRateMps +
                        (terminalInboundReferenceRate - AltitudeTerminalPredictiveBrakeStartRateMps) *
                        AltitudeTerminalPredictiveBrakeGain,
                        AltitudeTerminalPredictiveBrakeMinRateMps,
                        AltitudeTerminalPredictiveBrakeMaxRateMps);
                    AltitudeTerminalPredictiveBrakeDemandMps = -terminalTargetDirection * brakeMagnitude;
                }

                float terminalMinPhysicalRate = -terminalTowardRateLimit;
                float terminalMaxPhysicalRate = terminalTowardRateLimit;
                if (terminalTargetDirection > 0f)
                {
                    terminalMinPhysicalRate = -terminalBrakeRateLimit;
                    terminalMaxPhysicalRate = terminalTowardRateLimit;
                }
                else if (terminalTargetDirection < 0f)
                {
                    terminalMinPhysicalRate = -terminalTowardRateLimit;
                    terminalMaxPhysicalRate = terminalBrakeRateLimit;
                }
                else
                {
                    terminalMinPhysicalRate = -terminalBrakeRateLimit;
                    terminalMaxPhysicalRate = terminalBrakeRateLimit;
                }

                float terminalPhysicalRate = Mathf.Clamp(terminalReferenceRate,
                    terminalMinPhysicalRate, terminalMaxPhysicalRate);
                if (AltitudeTerminalPredictiveBrakeActive)
                {
                    bool stillCommandingTowardTarget =
                        Mathf.Sign(terminalPhysicalRate) == terminalTargetDirection &&
                        Mathf.Abs(terminalPhysicalRate) > 0.0001f;
                    if (stillCommandingTowardTarget ||
                        Mathf.Abs(AltitudeTerminalPredictiveBrakeDemandMps) >
                        Mathf.Abs(terminalPhysicalRate))
                    {
                        terminalPhysicalRate = AltitudeTerminalPredictiveBrakeDemandMps;
                    }
                }

                float terminalCommandLimit = Mathf.Max(terminalTowardRateLimit, terminalBrakeRateLimit) +
                    Mathf.Abs(AltitudeRateCommandBiasMps);
                DesiredVerticalSpeedMps = Mathf.Clamp(terminalPhysicalRate + AltitudeRateCommandBiasMps,
                    -terminalCommandLimit, terminalCommandLimit);
                RolloutActive = true;
            }

            float precisionEntryTargetDirection = absError > AltitudePrecisionNeutralExitBandMeters
                ? Mathf.Sign(AltitudeControlErrorMeters) : 0f;
            float precisionEntryInboundRate = precisionEntryTargetDirection != 0f
                ? precisionEntryTargetDirection * AltitudePrecisionReferenceVerticalSpeedMps : 0f;
            AltitudePrecisionEntryMeasuredRateOk =
                Mathf.Abs(AltitudePrecisionReferenceVerticalSpeedMps) <= AltitudePrecisionEntryVsMps;
            // PlannedVerticalSpeedMps is the raw V/S command frame.  Near target it
            // includes AltitudeRateCommandBiasMps so that zero physical ASL rate is
            // represented correctly to the V/S inner loop.  Judge terminal calmness
            // in the same physical altitude-reference frame as measured V/S.
            AltitudePrecisionEntryPhysicalPlannedRateMps =
                PlannedVerticalSpeedMps - AltitudeRateCommandBiasMps;
            AltitudePrecisionEntryPlannedRateOk =
                Mathf.Abs(AltitudePrecisionEntryPhysicalPlannedRateMps) <=
                    AltitudePrecisionEntryPlannedVsMps;
            AltitudePrecisionEntryDirectionOk = precisionEntryTargetDirection == 0f ||
                precisionEntryInboundRate >= -AltitudePrecisionEntryOutwardToleranceMps;
            AltitudePrecisionEntryReady = absError <= AltitudePrecisionEntryBandMeters &&
                AltitudePrecisionEntryMeasuredRateOk && AltitudePrecisionEntryPlannedRateOk &&
                AltitudePrecisionEntryDirectionOk && !AltitudeTerminalPredictiveBrakeActive;
            bool precisionEntry = AltitudePrecisionEntryReady;
            bool precisionRetention = absError <= AltitudePrecisionExitBandMeters &&
                                      Mathf.Abs(AltitudePrecisionReferenceVerticalSpeedMps) <= AltitudePrecisionExitVsMps;
            HoldDisturbanceRecoveryActive = false;
            if (!HoldLatched)
            {
                HoldDisturbanceDirectionGateActive = false;
                HoldDisturbanceOutwardRateMps = 0f;
                HoldDisturbanceRawExitCandidate = false;
                HoldDisturbancePrecisionOwnershipActive = false;
                HoldCaptureBrakeActive = false;
                HoldCaptureBrakeHysteresisActive = false;
                HoldCaptureBrakeCompletionBlend = 0f;
                HoldCaptureBrakeOutwardRateMps = 0f;
                HoldCaptureBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldCaptureBrakeEffectiveMaxRateMps = AltitudePrecisionMaxRateMps;
                HoldNeutralRateBrakeActive = false;
                HoldNeutralRateBrakeAbsRateMps = 0f;
                HoldNeutralRateBrakeCompletionBlend = 0f;
                HoldResidualRateCompletionActive = false;
                HoldResidualRateCompletionReleaseActive = false;
                HoldResidualRateCompletionCalm = true;
                HoldResidualRateCompletionPhysicalRateMps = 0f;
                HoldResidualRateCompletionAbsRateMps = 0f;
                HoldResidualRateCompletionPlannedRateMps = 0f;
                HoldResidualRateCompletionDampingBlend = 0f;
                HoldResidualRateCompletionPositionBlend = 1f;
                HoldResidualRateCompletionEffectivePositionGainPerSec = AltitudePrecisionRateGainPerSec;
                HoldPipelineUnloadActive = false;
                HoldPipelineUnloadPhysicalTowardRateMps = 0f;
                HoldPipelineUnloadPlannedPhysicalRateMps = 0f;
                HoldPipelineUnloadPlannedTowardRateMps = 0f;
                HoldPipelineUnloadPhysicalGateBlend = 0f;
                HoldPipelineUnloadPlannedGateBlend = 0f;
                HoldPipelineUnloadBlend = 0f;
                HoldPipelineUnloadRawBeforeMps = 0f;
                HoldPipelineUnloadRequestedRateMps = 0f;
                HoldPipelineUnloadAppliedRateMps = 0f;
                PrecisionLowQRateGainActive = false;
                PrecisionLowQRateGainQBlend = 0f;
                PrecisionLowQRateGainErrorBlend = 0f;
                PrecisionLowQRateGainBlend = 0f;
                PrecisionEffectiveRateGainPerSec = AltitudePrecisionRateGainPerSec;
                PrecisionLowQDampingActive = false;
                PrecisionLowQDampingQBlend = 0f;
                PrecisionEffectiveBaseDampingPerSec =
                    AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldInboundArrivalBrakeActive = false;
                HoldInboundArrivalBrakeRateMps = 0f;
                HoldInboundArrivalBrakeTimeToTargetSeconds = 0f;
                HoldInboundArrivalBrakeRateGateBlend = 0f;
                HoldInboundArrivalBrakeBlend = 0f;
                HoldInboundArrivalBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldDisturbanceExitCandidate = false;
                HoldDisturbanceRequiredDwellSeconds = 0f;
                HoldEntryElapsedSeconds = precisionEntry
                    ? HoldEntryElapsedSeconds + Time.fixedDeltaTime : 0f;
                HoldExitElapsedSeconds = 0f;
                HoldDisturbanceExitElapsedSeconds = 0f;
                if (HoldEntryElapsedSeconds >= AltitudePrecisionEntryDwellSeconds)
                    HoldLatched = true;
            }
            else
            {
                HoldEntryElapsedSeconds = 0f;

                // The nominal retention path is deliberately slow so small sensor
                // noise cannot throw ALT out of precision hold.  A real V/S tracking
                // mismatch is different: it is an external flight-path disturbance
                // (e.g. a HDG bank transition) and must return to terminal rollout
                // promptly before altitude error expands.
                float holdTrackingError = AltitudePrecisionReferenceVerticalSpeedMps -
                    (PlannedVerticalSpeedMps - AltitudeRateCommandBiasMps);
                bool trackingMismatch = Mathf.Abs(holdTrackingError) >
                    AltitudeHoldDisturbanceTrackingBandMps;
                float targetDirection = absError > AltitudePrecisionNeutralEnterBandMeters
                    ? Mathf.Sign(AltitudeControlErrorMeters) : 0f;
                // Positive outward rate means altitude is diverging from the target:
                // below+descending or above+climbing.  A small rate toward target is
                // intentionally not a disturbance, even when it differs from the newly
                // slewed precision demand.
                HoldDisturbanceOutwardRateMps = targetDirection != 0f
                    ? -targetDirection * AltitudePrecisionReferenceVerticalSpeedMps : 0f;
                bool outwardMotion = targetDirection != 0f &&
                    HoldDisturbanceOutwardRateMps > AltitudeHoldDisturbanceOutwardRateMps;
                bool hardOutwardMotion = targetDirection != 0f &&
                    HoldDisturbanceOutwardRateMps > AltitudeHoldDisturbanceHardOutwardRateMps;
                HoldDisturbanceRawExitCandidate = trackingMismatch && outwardMotion;
                // PrecisionHold must be allowed to absorb its own expected transport lag.
                // v0.8.10 keeps ownership through the existing retention band while the
                // carryover brake removes residual capture energy.  A genuinely hard
                // outward rate still bypasses ownership immediately; moderate motion
                // returns to normal recovery outside the retention envelope.
                HoldDisturbancePrecisionOwnershipActive =
                    HoldDisturbanceRawExitCandidate &&
                    !hardOutwardMotion &&
                    absError <= AltitudePrecisionExitBandMeters;
                HoldDisturbanceExitCandidate =
                    HoldDisturbanceRawExitCandidate &&
                    !HoldDisturbancePrecisionOwnershipActive;
                HoldDisturbanceDirectionGateActive = trackingMismatch && !outwardMotion;
                HoldDisturbanceRequiredDwellSeconds = hardOutwardMotion
                    ? AltitudeHoldDisturbanceExitDwellSeconds
                    : AltitudeHoldDisturbanceDirectionalExitDwellSeconds;
                HoldDisturbanceExitElapsedSeconds = HoldDisturbanceExitCandidate
                    ? HoldDisturbanceExitElapsedSeconds + Time.fixedDeltaTime : 0f;

                if (HoldDisturbanceExitCandidate &&
                    HoldDisturbanceExitElapsedSeconds >= HoldDisturbanceRequiredDwellSeconds)
                {
                    HoldLatched = false;
                    PrecisionCorrectionActive = false;
                    HoldExitElapsedSeconds = 0f;
                    HoldDisturbanceRecoveryActive = true;
                }
                else
                {
                    HoldExitElapsedSeconds = precisionRetention
                        ? 0f : HoldExitElapsedSeconds + Time.fixedDeltaTime;
                    if (HoldExitElapsedSeconds >= AltitudePrecisionExitDwellSeconds)
                    {
                        HoldLatched = false;
                        HoldExitElapsedSeconds = 0f;
                    }
                }
            }

            AltitudeHoldNeutralCommandMps = 0f;
            bool holdCaptureBrakeWasActive = HoldCaptureBrakeActive;
            HoldCaptureBrakeHysteresisActive = false;
            HoldCaptureBrakeCompletionBlend = 0f;
            HoldCaptureBrakeOutwardRateMps = 0f;
            HoldCaptureBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
            HoldCaptureBrakeEffectiveMaxRateMps = AltitudePrecisionMaxRateMps;
            HoldNeutralRateBrakeCompletionBlend = 0f;
            HoldInboundArrivalBrakeActive = false;
            HoldInboundArrivalBrakeRateMps = 0f;
            HoldInboundArrivalBrakeTimeToTargetSeconds = 0f;
            HoldInboundArrivalBrakeRateGateBlend = 0f;
            HoldInboundArrivalBrakeBlend = 0f;
            HoldInboundArrivalBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
            HoldPipelineUnloadActive = false;
            HoldPipelineUnloadPhysicalTowardRateMps = 0f;
            HoldPipelineUnloadPlannedPhysicalRateMps = 0f;
            HoldPipelineUnloadPlannedTowardRateMps = 0f;
            HoldPipelineUnloadPhysicalGateBlend = 0f;
            HoldPipelineUnloadPlannedGateBlend = 0f;
            HoldPipelineUnloadBlend = 0f;
            HoldPipelineUnloadRawBeforeMps = 0f;
            HoldPipelineUnloadRequestedRateMps = 0f;
            HoldPipelineUnloadAppliedRateMps = 0f;
            PrecisionLowQRateGainActive = false;
            PrecisionLowQRateGainQBlend = 0f;
            PrecisionLowQRateGainErrorBlend = 0f;
            PrecisionLowQRateGainBlend = 0f;
            PrecisionEffectiveRateGainPerSec = AltitudePrecisionRateGainPerSec;
            PrecisionLowQDampingActive = false;
            PrecisionLowQDampingQBlend = 0f;
            PrecisionEffectiveBaseDampingPerSec =
                AltitudePrecisionVerticalSpeedDampingPerSec;
            if (!HoldLatched)
            {
                SuspendAdaptiveMicroTrim();
            }
            if (HoldLatched)
            {
                // Sub-metre precision trim.  This does not alter the completed ALT
                // trajectory; it only removes the static residual after the existing
                // hold latch has already declared the vertical motion calm.
                //
                // The old single ±0.25 m deadband was the direct cause of the observed
                // 0.2..0.5 m final error.  Use entry/exit hysteresis and a continuous
                // low-rate correction so the nested V/S terminal does not see step demands.
                if (!PrecisionCorrectionActive)
                    PrecisionCorrectionActive = absError > AltitudePrecisionNeutralExitBandMeters;
                else
                    PrecisionCorrectionActive = absError > AltitudePrecisionNeutralEnterBandMeters;

                HoldNeutralRateBrakeAbsRateMps =
                    Mathf.Abs(AltitudePrecisionReferenceVerticalSpeedMps);
                bool neutralRateBrakePositionEligible =
                    !PrecisionCorrectionActive &&
                    absError <= AltitudePrecisionNeutralExitBandMeters;
                if (!HoldNeutralRateBrakeActive)
                {
                    HoldNeutralRateBrakeActive =
                        neutralRateBrakePositionEligible &&
                        HoldNeutralRateBrakeAbsRateMps >
                            AltitudeHoldNeutralRateBrakeEnterMps;
                }
                else
                {
                    HoldNeutralRateBrakeActive =
                        neutralRateBrakePositionEligible &&
                        HoldNeutralRateBrakeAbsRateMps >
                            AltitudeHoldNeutralRateBrakeExitMps;
                }

                bool applyPrecisionDemand = PrecisionCorrectionActive ||
                    BankVerticalSupportActive || HoldNeutralRateBrakeActive;
                if (applyPrecisionDemand)
                {
                    // Position term asks for a small rate toward the target; vertical-speed
                    // damping cancels residual momentum before it crosses the target.  The
                    // bank-aware support is a separately logged anti-sink overlay, active
                    // only around the target and only while bank/sink criteria are met.
                    float precisionTargetDirection =
                        absError > AltitudePrecisionNeutralEnterBandMeters
                            ? Mathf.Sign(AltitudeControlErrorMeters) : 0f;

                    HoldInboundArrivalBrakeRateMps = precisionTargetDirection != 0f
                        ? Mathf.Max(0f, precisionTargetDirection *
                            AltitudePrecisionReferenceVerticalSpeedMps)
                        : 0f;
                    HoldInboundArrivalBrakeTimeToTargetSeconds =
                        HoldInboundArrivalBrakeRateMps > 0.001f
                            ? absError / HoldInboundArrivalBrakeRateMps
                            : 0f;
                    bool inboundArrivalPositionEligible =
                        PrecisionCorrectionActive &&
                        precisionTargetDirection != 0f &&
                        absError <= AltitudePrecisionExitBandMeters;
                    HoldInboundArrivalBrakeRateGateBlend = Mathf.SmoothStep(0f, 1f,
                        Mathf.InverseLerp(AltitudeHoldInboundArrivalBrakeEnterMps,
                            AltitudeHoldInboundArrivalBrakeFullMps,
                            HoldInboundArrivalBrakeRateMps));
                    float inboundArrivalLeadBlend = HoldInboundArrivalBrakeRateMps > 0.001f
                        ? Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(AltitudeHoldInboundArrivalBrakeLeadStartSeconds,
                                AltitudeHoldInboundArrivalBrakeLeadFullSeconds,
                                HoldInboundArrivalBrakeTimeToTargetSeconds))
                        : 0f;
                    float inboundArrivalLowQBlend = Mathf.SmoothStep(0f, 1f,
                        LowQVerticalEnvelopeBlend);
                    HoldInboundArrivalBrakeBlend = inboundArrivalPositionEligible
                        ? HoldInboundArrivalBrakeRateGateBlend * inboundArrivalLeadBlend *
                            inboundArrivalLowQBlend
                        : 0f;
                    HoldInboundArrivalBrakeActive =
                        HoldInboundArrivalBrakeBlend > 0.0001f;
                    HoldInboundArrivalBrakeEffectiveDampingPerSec = Mathf.Lerp(
                        AltitudePrecisionVerticalSpeedDampingPerSec,
                        Mathf.Max(AltitudePrecisionVerticalSpeedDampingPerSec,
                            AltitudeHoldInboundArrivalBrakeLowQDampingPerSec),
                        HoldInboundArrivalBrakeBlend);

                    HoldCaptureBrakeOutwardRateMps = precisionTargetDirection != 0f
                        ? -precisionTargetDirection * AltitudePrecisionReferenceVerticalSpeedMps
                        : 0f;
                    bool holdCaptureBrakePositionEligible =
                        precisionTargetDirection != 0f &&
                        absError <= AltitudePrecisionExitBandMeters;
                    if (!holdCaptureBrakeWasActive)
                    {
                        HoldCaptureBrakeActive =
                            holdCaptureBrakePositionEligible &&
                            HoldCaptureBrakeOutwardRateMps >
                                AltitudeHoldDisturbanceOutwardRateMps;
                    }
                    else
                    {
                        HoldCaptureBrakeActive =
                            holdCaptureBrakePositionEligible &&
                            HoldCaptureBrakeOutwardRateMps >
                                AltitudeHoldCaptureBrakeExitMps;
                    }
                    HoldCaptureBrakeHysteresisActive =
                        HoldCaptureBrakeActive &&
                        HoldCaptureBrakeOutwardRateMps <=
                            AltitudeHoldDisturbanceOutwardRateMps;

                    float captureBrakeCompletionRange = Mathf.Max(0.001f,
                        AltitudeHoldDisturbanceOutwardRateMps -
                        AltitudeHoldCaptureBrakeExitMps);
                    float captureBrakeLinearBlend = HoldCaptureBrakeActive
                        ? Mathf.Clamp01((HoldCaptureBrakeOutwardRateMps -
                            AltitudeHoldCaptureBrakeExitMps) /
                            captureBrakeCompletionRange)
                        : 0f;
                    // Smoothstep keeps both ends continuous so the nested V/S
                    // trajectory never sees a new authority step at 0.10 or 0.02 m/s.
                    float captureBrakeSmoothBlend = captureBrakeLinearBlend *
                        captureBrakeLinearBlend * (3f - 2f * captureBrakeLinearBlend);
                    HoldCaptureBrakeCompletionBlend = Mathf.Pow(captureBrakeSmoothBlend,
                        Mathf.Max(1f, AltitudeHoldCaptureBrakeTaperExponent));

                    if (HoldNeutralRateBrakeActive)
                    {
                        float neutralRateBrakeRange = Mathf.Max(0.001f,
                            AltitudeHoldNeutralRateBrakeFullMps -
                            AltitudeHoldNeutralRateBrakeExitMps);
                        float neutralRateBrakeLinearBlend = Mathf.Clamp01(
                            (HoldNeutralRateBrakeAbsRateMps -
                                AltitudeHoldNeutralRateBrakeExitMps) /
                            neutralRateBrakeRange);
                        float neutralRateBrakeSmoothBlend =
                            neutralRateBrakeLinearBlend * neutralRateBrakeLinearBlend *
                            (3f - 2f * neutralRateBrakeLinearBlend);
                        HoldNeutralRateBrakeCompletionBlend = Mathf.Pow(
                            neutralRateBrakeSmoothBlend,
                            Mathf.Max(1f, AltitudeHoldCaptureBrakeTaperExponent));
                        HoldCaptureBrakeCompletionBlend = Mathf.Max(
                            HoldCaptureBrakeCompletionBlend,
                            HoldNeutralRateBrakeCompletionBlend);
                    }

                    // v0.8.21: withdraw the v0.8.20 late damping tail and restore
                    // the exact v0.8.18 capture/neutral damping blend. The residual fields
                    // remain populated as compatibility diagnostics only.
                    HoldResidualRateCompletionPhysicalRateMps =
                        AltitudePrecisionReferenceVerticalSpeedMps;
                    HoldResidualRateCompletionAbsRateMps =
                        Mathf.Abs(HoldResidualRateCompletionPhysicalRateMps);
                    HoldResidualRateCompletionPlannedRateMps =
                        PlannedVerticalSpeedMps - AltitudeRateCommandBiasMps -
                        BankVerticalSupportRateMps;
                    HoldResidualRateCompletionCalm =
                        HoldCaptureBrakeOutwardRateMps <=
                            AltitudeHoldCaptureBrakeExitMps;
                    HoldResidualRateCompletionActive = false;
                    HoldResidualRateCompletionReleaseActive = false;
                    HoldResidualRateCompletionDampingBlend =
                        HoldCaptureBrakeCompletionBlend;
                    HoldResidualRateCompletionPositionBlend = 1f;
                    HoldResidualRateCompletionEffectivePositionGainPerSec =
                        AltitudePrecisionRateGainPerSec;

                    float strongCaptureDamping = Mathf.Max(
                        AltitudePrecisionVerticalSpeedDampingPerSec,
                        AltitudeTerminalInnerSettleEffectiveDampingPerSec);
                    float strongCaptureRate = Mathf.Max(
                        AltitudePrecisionMaxRateMps,
                        AltitudeTerminalInnerSettleEffectiveBrakeRateMps);
                    HoldCaptureBrakeEffectiveDampingPerSec = Mathf.Max(
                        Mathf.Lerp(AltitudePrecisionVerticalSpeedDampingPerSec,
                            strongCaptureDamping,
                            HoldCaptureBrakeCompletionBlend),
                        HoldInboundArrivalBrakeEffectiveDampingPerSec);
                    HoldCaptureBrakeEffectiveMaxRateMps = Mathf.Lerp(
                        AltitudePrecisionMaxRateMps,
                        strongCaptureRate, HoldCaptureBrakeCompletionBlend);

                    HoldPipelineUnloadPhysicalTowardRateMps =
                        precisionTargetDirection != 0f
                            ? precisionTargetDirection *
                                AltitudePrecisionReferenceVerticalSpeedMps
                            : 0f;
                    HoldPipelineUnloadPlannedPhysicalRateMps =
                        PlannedVerticalSpeedMps - AltitudeRateCommandBiasMps -
                        BankVerticalSupportRateMps;
                    HoldPipelineUnloadPlannedTowardRateMps =
                        precisionTargetDirection != 0f
                            ? precisionTargetDirection *
                                HoldPipelineUnloadPlannedPhysicalRateMps
                            : 0f;
                    bool pipelineUnloadEligible =
                        precisionTargetDirection != 0f &&
                        !BankVerticalSupportActive &&
                        absError <= AltitudePrecisionExitBandMeters &&
                        HoldPipelineUnloadPlannedTowardRateMps > 0f;
                    HoldPipelineUnloadPhysicalGateBlend = pipelineUnloadEligible
                        ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                            AltitudeHoldPipelineUnloadPhysicalGateStartMps,
                            AltitudeHoldPipelineUnloadPhysicalGateFullMps,
                            HoldPipelineUnloadPhysicalTowardRateMps))
                        : 0f;
                    HoldPipelineUnloadPlannedGateBlend = pipelineUnloadEligible
                        ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(
                            AltitudeHoldPipelineUnloadPlannedGateStartMps,
                            AltitudeHoldPipelineUnloadPlannedGateFullMps,
                            HoldPipelineUnloadPlannedTowardRateMps))
                        : 0f;
                    HoldPipelineUnloadBlend = pipelineUnloadEligible
                        ? HoldPipelineUnloadPhysicalGateBlend *
                            HoldPipelineUnloadPlannedGateBlend *
                            Mathf.SmoothStep(0f, 1f, LowQVerticalEnvelopeBlend)
                        : 0f;

                    // v0.8.23: the v0.8.22 position-gain experiment is fully
                    // withdrawn. Do not let error magnitude select different gains on the
                    // two half-cycles.
                    PrecisionLowQRateGainActive = false;
                    PrecisionLowQRateGainQBlend = 0f;
                    PrecisionLowQRateGainErrorBlend = 0f;
                    PrecisionLowQRateGainBlend = 0f;
                    PrecisionEffectiveRateGainPerSec = AltitudePrecisionRateGainPerSec;
                    HoldResidualRateCompletionEffectivePositionGainPerSec =
                        PrecisionEffectiveRateGainPerSec;

                    PrecisionLowQDampingQBlend = !BankVerticalSupportActive
                        ? Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(LowQVerticalEnvelopeBlend))
                        : 0f;
                    PrecisionEffectiveBaseDampingPerSec = Mathf.Lerp(
                        AltitudePrecisionVerticalSpeedDampingPerSec,
                        Mathf.Min(AltitudePrecisionVerticalSpeedDampingPerSec,
                            AltitudePrecisionLowQDampingPerSec),
                        PrecisionLowQDampingQBlend);
                    PrecisionLowQDampingActive =
                        PrecisionLowQDampingQBlend > 0.0001f &&
                        PrecisionEffectiveBaseDampingPerSec <
                            AltitudePrecisionVerticalSpeedDampingPerSec - 0.0001f;

                    // Rebuild the existing capture/arrival damping from the scheduled
                    // quiet base. Strong capture damping, the 0.80/s arrival ceiling, and
                    // all max-rate authority remain unchanged.
                    HoldInboundArrivalBrakeEffectiveDampingPerSec = Mathf.Lerp(
                        PrecisionEffectiveBaseDampingPerSec,
                        Mathf.Max(PrecisionEffectiveBaseDampingPerSec,
                            AltitudeHoldInboundArrivalBrakeLowQDampingPerSec),
                        HoldInboundArrivalBrakeBlend);
                    strongCaptureDamping = Mathf.Max(
                        PrecisionEffectiveBaseDampingPerSec,
                        AltitudeTerminalInnerSettleEffectiveDampingPerSec);
                    HoldCaptureBrakeEffectiveDampingPerSec = Mathf.Max(
                        Mathf.Lerp(PrecisionEffectiveBaseDampingPerSec,
                            strongCaptureDamping,
                            HoldCaptureBrakeCompletionBlend),
                        HoldInboundArrivalBrakeEffectiveDampingPerSec);

                    HoldPipelineUnloadRawBeforeMps =
                        AltitudeControlErrorMeters * PrecisionEffectiveRateGainPerSec
                        - AltitudePrecisionReferenceVerticalSpeedMps *
                            HoldCaptureBrakeEffectiveDampingPerSec;
                    float pipelineRawTowardRateMps =
                        precisionTargetDirection != 0f
                            ? precisionTargetDirection *
                                HoldPipelineUnloadRawBeforeMps
                            : 0f;
                    HoldPipelineUnloadRequestedRateMps =
                        precisionTargetDirection * Mathf.Max(0f,
                            HoldPipelineUnloadPlannedTowardRateMps) *
                            Mathf.Clamp01(AltitudeHoldPipelineUnloadGain) *
                            HoldPipelineUnloadBlend;
                    float pipelineUnloadMagnitudeMps = Mathf.Min(
                        Mathf.Max(0f, pipelineRawTowardRateMps),
                        Mathf.Abs(HoldPipelineUnloadRequestedRateMps));
                    HoldPipelineUnloadAppliedRateMps =
                        precisionTargetDirection * pipelineUnloadMagnitudeMps;
                    HoldPipelineUnloadActive =
                        pipelineUnloadMagnitudeMps > 0.0001f;

                    PrecisionRawRateMps = HoldPipelineUnloadRawBeforeMps -
                        HoldPipelineUnloadAppliedRateMps;
                    UpdateAdaptiveMicroTrim(vessel, absError, PrecisionRawRateMps);
                    PrecisionRawRateMps += MicroTrimAppliedRateMps;
                    float precisionPhysicalRate = Mathf.Clamp(PrecisionRawRateMps +
                        BankVerticalSupportRateMps, -HoldCaptureBrakeEffectiveMaxRateMps,
                        HoldCaptureBrakeEffectiveMaxRateMps);
                    float precisionCommandLimit = HoldCaptureBrakeEffectiveMaxRateMps +
                        Mathf.Abs(AltitudeRateCommandBiasMps);
                    PrecisionCorrectionRateMps = Mathf.Clamp(precisionPhysicalRate +
                        AltitudeRateCommandBiasMps, -precisionCommandLimit,
                        precisionCommandLimit);
                    DesiredVerticalSpeedMps = PrecisionCorrectionRateMps;
                }
                else
                {
                    // Keep the phase observer on real time even through the short
                    // neutral ticks where PrecisionCorrectionActive is false.  A zero
                    // raw input is the actual ALT command in this branch; pulse
                    // application remains impossible because cancellation-only logic
                    // requires an opposing non-zero raw command.
                    UpdateAdaptiveMicroTrim(vessel, absError, 0f);
                    HoldResidualRateCompletionActive = false;
                    HoldResidualRateCompletionReleaseActive = false;
                    HoldResidualRateCompletionCalm = true;
                    HoldResidualRateCompletionPhysicalRateMps =
                        AltitudePrecisionReferenceVerticalSpeedMps;
                    HoldResidualRateCompletionAbsRateMps =
                        Mathf.Abs(HoldResidualRateCompletionPhysicalRateMps);
                    HoldResidualRateCompletionPlannedRateMps =
                        PlannedVerticalSpeedMps - AltitudeRateCommandBiasMps -
                        BankVerticalSupportRateMps;
                    HoldResidualRateCompletionDampingBlend = 0f;
                    HoldResidualRateCompletionPositionBlend = 1f;
                    HoldResidualRateCompletionEffectivePositionGainPerSec =
                        AltitudePrecisionRateGainPerSec;

                    // No precision or rate demand is being applied in this branch;
                    // clear the capture-brake state so the next correction must
                    // satisfy the normal 0.10 m/s entry threshold rather than
                    // inheriting stale hysteresis telemetry from a neutral tick.
                    HoldCaptureBrakeActive = false;
                    HoldCaptureBrakeHysteresisActive = false;
                    HoldCaptureBrakeCompletionBlend = 0f;
                    HoldNeutralRateBrakeActive = false;
                    HoldNeutralRateBrakeCompletionBlend = 0f;
                    HoldInboundArrivalBrakeActive = false;
                    HoldInboundArrivalBrakeRateMps = 0f;
                    HoldInboundArrivalBrakeTimeToTargetSeconds = 0f;
                    HoldInboundArrivalBrakeRateGateBlend = 0f;
                    HoldInboundArrivalBrakeBlend = 0f;
                    HoldInboundArrivalBrakeEffectiveDampingPerSec =
                        AltitudePrecisionVerticalSpeedDampingPerSec;
                    PrecisionRawRateMps = 0f;
                    // A raw V/S command of zero is not zero ASL rate when the KSP
                    // VerticalSpeed reference carries the measured terminal bias.
                    // Keep the neutral hold command in the reconciled frame.
                    AltitudeHoldNeutralCommandMps = AltitudeRateCommandBiasMps;
                    PrecisionCorrectionRateMps = AltitudeHoldNeutralCommandMps;
                    DesiredVerticalSpeedMps = AltitudeHoldNeutralCommandMps;
                }
                // Hold ownership is not rollout. Keeping this false makes the
                // diagnostic flag a current-phase indicator; HoldLatched and the
                // control law remain unchanged.
                RolloutActive = false;
            }
            else
            {
                PrecisionCorrectionActive = false;
                HoldCaptureBrakeActive = false;
                HoldCaptureBrakeHysteresisActive = false;
                HoldCaptureBrakeCompletionBlend = 0f;
                HoldNeutralRateBrakeActive = false;
                HoldNeutralRateBrakeAbsRateMps = 0f;
                HoldNeutralRateBrakeCompletionBlend = 0f;
                HoldResidualRateCompletionActive = false;
                HoldResidualRateCompletionReleaseActive = false;
                HoldResidualRateCompletionCalm = true;
                HoldResidualRateCompletionPhysicalRateMps = 0f;
                HoldResidualRateCompletionAbsRateMps = 0f;
                HoldResidualRateCompletionPlannedRateMps = 0f;
                HoldResidualRateCompletionDampingBlend = 0f;
                HoldResidualRateCompletionPositionBlend = 1f;
                HoldResidualRateCompletionEffectivePositionGainPerSec = AltitudePrecisionRateGainPerSec;
                HoldPipelineUnloadActive = false;
                HoldPipelineUnloadPhysicalTowardRateMps = 0f;
                HoldPipelineUnloadPlannedPhysicalRateMps = 0f;
                HoldPipelineUnloadPlannedTowardRateMps = 0f;
                HoldPipelineUnloadPhysicalGateBlend = 0f;
                HoldPipelineUnloadPlannedGateBlend = 0f;
                HoldPipelineUnloadBlend = 0f;
                HoldPipelineUnloadRawBeforeMps = 0f;
                HoldPipelineUnloadRequestedRateMps = 0f;
                HoldPipelineUnloadAppliedRateMps = 0f;
                PrecisionLowQRateGainActive = false;
                PrecisionLowQRateGainQBlend = 0f;
                PrecisionLowQRateGainErrorBlend = 0f;
                PrecisionLowQRateGainBlend = 0f;
                PrecisionEffectiveRateGainPerSec = AltitudePrecisionRateGainPerSec;
                PrecisionLowQDampingActive = false;
                PrecisionLowQDampingQBlend = 0f;
                PrecisionEffectiveBaseDampingPerSec =
                    AltitudePrecisionVerticalSpeedDampingPerSec;
                HoldInboundArrivalBrakeActive = false;
                HoldInboundArrivalBrakeRateMps = 0f;
                HoldInboundArrivalBrakeTimeToTargetSeconds = 0f;
                HoldInboundArrivalBrakeRateGateBlend = 0f;
                HoldInboundArrivalBrakeBlend = 0f;
                HoldInboundArrivalBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
                PrecisionRawRateMps = 0f;
                PrecisionCorrectionRateMps = 0f;
            }

            // v0.7.6: keep AoA observer-only; the correct intervention point is the
            // low-q ALT/V/S authority envelope, before high AoA is created by saturation.
            ObserveDisabledAoAClimbGovernor(attitude, DesiredVerticalSpeedMps);
            DesiredVerticalSpeedMps = ApplyLowQVerticalEnvelope(DesiredVerticalSpeedMps);

            // A target change may safely re-anchor only the ALT motion planner.  It does not
            // modify the V/S user's stored target and it does not reset V/S BasePitch every
            // time this continuously planned demand changes.
            if (TargetChangedSinceUpdate)
            {
                PlannedVerticalSpeedMps = Mathf.Clamp(CurrentVerticalSpeedMps,
                    -LowQVerticalEnvelopeVsCapMps, LowQVerticalEnvelopeVsCapMps);
                TargetChangedSinceUpdate = false;
            }

            bool braking = Mathf.Abs(DesiredVerticalSpeedMps) < Mathf.Abs(PlannedVerticalSpeedMps) ||
                           (Mathf.Abs(DesiredVerticalSpeedMps) > 0.001f &&
                            Mathf.Abs(PlannedVerticalSpeedMps) > 0.001f &&
                            Mathf.Sign(DesiredVerticalSpeedMps) != Mathf.Sign(PlannedVerticalSpeedMps));
            float nominalAccelLimit = HoldLatched
                ? AltitudePrecisionCommandSlewMps2
                : (braking ? AltitudeRateBrakeAccelLimitMps2 : AltitudeRateAccelLimitMps2);
            float accelLimit = HoldLatched
                ? nominalAccelLimit
                : (braking ? LowQVerticalEnvelopeAppliedBrakeAccelLimitMps2 :
                    LowQVerticalEnvelopeAppliedAccelLimitMps2);
            PlannedVerticalSpeedMps = Mathf.MoveTowards(PlannedVerticalSpeedMps,
                DesiredVerticalSpeedMps, accelLimit * Time.fixedDeltaTime);
            AltitudeRateDemandMps = PlannedVerticalSpeedMps;
            verticalSpeed.SetAltitudeVerticalSpeedDemand(AltitudeRateDemandMps, MaxAltitudePitchDeg, HoldLatched);

            ControlState = HoldDisturbanceRecoveryActive ? "DisturbanceRecovery"
                : (HoldLatched ? ((PrecisionCorrectionActive || HoldNeutralRateBrakeActive)
                    ? "PrecisionCapture" : "AltitudeHold")
                : (RolloutActive ? "Rollout" : "Capture"));
        }

        void ResetOutputs(string state)
        {
            ControlActive = false;
            SuspendAdaptiveMicroTrim();
            CurrentAltitudeMeters = 0f;
            AltitudeErrorMeters = 0f;
            AltitudeControlErrorMeters = 0f;
            AltitudeHoldBandErrorMeters = 0f;
            AltitudeInsidePreferredHoldBand = false;
            CurrentVerticalSpeedMps = 0f;
            AltitudeReferenceVerticalSpeedMps = 0f;
            AltitudeReconciledVerticalSpeedMps = 0f;
            AltitudeRateBiasMps = 0f;
            AltitudeRateReconciliationActive = false;
            AltitudeRateReconciliationBlend = 0f;
            AltitudeRateCommandBiasMps = 0f;
            haveAltitudeReferenceSample = false;
            lastAltitudeReferenceMeters = 0f;
            lastAltitudeReferenceFixedTime = 0f;
            DesiredVerticalSpeedMps = 0f;
            PlannedVerticalSpeedMps = 0f;
            AltitudeRateDemandMps = 0f;
            StoppingRateLimitMps = 0f;
            StopDistanceMeters = 0f;
            TransportLeadMeters = 0f;
            MeasuredBrakeLagRateMps = 0f;
            MeasuredBrakeLagLeadMeters = 0f;
            RolloutActive = false;
            HoldLatched = false;
            HoldDisturbanceDirectionGateActive = false;
            HoldDisturbanceOutwardRateMps = 0f;
            HoldDisturbanceRawExitCandidate = false;
            HoldDisturbancePrecisionOwnershipActive = false;
            HoldCaptureBrakeActive = false;
            HoldCaptureBrakeHysteresisActive = false;
            HoldCaptureBrakeCompletionBlend = 0f;
            HoldCaptureBrakeOutwardRateMps = 0f;
            HoldCaptureBrakeEffectiveDampingPerSec = AltitudePrecisionVerticalSpeedDampingPerSec;
            HoldCaptureBrakeEffectiveMaxRateMps = AltitudePrecisionMaxRateMps;
            HoldNeutralRateBrakeActive = false;
            HoldNeutralRateBrakeAbsRateMps = 0f;
            HoldNeutralRateBrakeCompletionBlend = 0f;
            HoldResidualRateCompletionActive = false;
            HoldResidualRateCompletionReleaseActive = false;
            HoldResidualRateCompletionCalm = true;
            HoldResidualRateCompletionPhysicalRateMps = 0f;
            HoldResidualRateCompletionAbsRateMps = 0f;
            HoldResidualRateCompletionPlannedRateMps = 0f;
            HoldResidualRateCompletionDampingBlend = 0f;
            HoldResidualRateCompletionPositionBlend = 1f;
            HoldResidualRateCompletionEffectivePositionGainPerSec = AltitudePrecisionRateGainPerSec;
            HoldPipelineUnloadActive = false;
            HoldPipelineUnloadPhysicalTowardRateMps = 0f;
            HoldPipelineUnloadPlannedPhysicalRateMps = 0f;
            HoldPipelineUnloadPlannedTowardRateMps = 0f;
            HoldPipelineUnloadPhysicalGateBlend = 0f;
            HoldPipelineUnloadPlannedGateBlend = 0f;
            HoldPipelineUnloadBlend = 0f;
            HoldPipelineUnloadRawBeforeMps = 0f;
            HoldPipelineUnloadRequestedRateMps = 0f;
            HoldPipelineUnloadAppliedRateMps = 0f;
            HoldDisturbanceExitCandidate = false;
            HoldDisturbanceRequiredDwellSeconds = 0f;
            HoldDisturbanceRecoveryActive = false;
            HoldDisturbanceExitElapsedSeconds = 0f;
            PrecisionCorrectionActive = false;
            PrecisionRawRateMps = 0f;
            PrecisionCorrectionRateMps = 0f;
            BankVerticalSupportEligible = false;
            BankVerticalSupportActive = false;
            BankVerticalSupportBankDeg = 0f;
            BankVerticalSupportRollRateDegPerSec = 0f;
            BankVerticalSupportLoadFactorExcess = 0f;
            BankVerticalSupportSinkActivation = 0f;
            BankVerticalSupportTransitionRateMps = 0f;
            BankVerticalSupportTargetRateMps = 0f;
            BankVerticalSupportRateMps = 0f;
            AltitudeTerminalEffectiveFineBandMeters = 0f;
            AltitudeTerminalEffectiveMaxRateMps = 0f;
            AltitudeTerminalInnerSettleActive = false;
            AltitudeTerminalInnerSettleEffectiveBandMeters = 0f;
            AltitudeTerminalInnerSettleEffectiveExitBandMeters = 0f;
            AltitudeTerminalInnerSettleEffectiveMaxRateMps = 0f;
            AltitudeTerminalInnerSettleEffectiveBrakeRateMps = 0f;
            AltitudeTerminalInnerSettleEffectiveDampingPerSec = 0f;
            AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds = 0f;
            AltitudeTerminalPredictiveBrakeEffectiveBandMeters = 0f;
            AltitudeTerminalPredictiveBrakeActive = false;
            AltitudeTerminalPredictiveBrakeInboundRateMps = 0f;
            AltitudeTerminalPredictiveBrakeTimeToTargetSeconds = 0f;
            AltitudeTerminalPredictiveBrakeDemandMps = 0f;
            AltitudePrecisionEntryMeasuredRateOk = false;
            AltitudePrecisionEntryPlannedRateOk = false;
            AltitudePrecisionEntryDirectionOk = false;
            AltitudePrecisionEntryReady = false;
            AltitudePrecisionEntryPhysicalPlannedRateMps = 0f;
            AltitudeHoldNeutralCommandMps = 0f;
            AltitudePrecisionReferenceVerticalSpeedMps = 0f;
            AltitudePrecisionReferenceRateActive = false;
            AltitudePrecisionDirectReferenceRateActive = false;
            AltitudePrecisionReferenceDeltaVsReconciledMps = 0f;
            ResetAoAClimbGovernor();
            ResetLowQVerticalEnvelope();
            HoldEntryElapsedSeconds = 0f;
            HoldExitElapsedSeconds = 0f;
            TargetChangedSinceUpdate = false;
            ControlState = state;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
