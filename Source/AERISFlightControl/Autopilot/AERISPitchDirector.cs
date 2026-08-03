using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Autopilot
{
    // PITCH is the vertical attitude director.  AERIS creates the requested pitch
    // motion; AA remains the native inner angular-rate controller and the only final
    // FlightCtrlState writer.  v0.4.91 retains the existing PITCH/V/S target law but
    // transports it as an AA-native pitch-rate request instead of a virtual stick input.
    internal sealed class AERISPitchDirector
    {
        internal bool Armed { get; private set; }
        internal float TargetPitch { get; private set; }
        internal string TargetPitchText = "0";
        internal float CurrentPitch { get; private set; }
        internal float PitchError { get; private set; }
        internal float ActualPitchRate { get; private set; }

        // v0.4.97: arming evidence for the vertical-target persistence contract.  The
        // prepared input text and applied target are snapshotted before every arm reset and
        // restored explicitly; SET CURRENT remains the sole path that captures attitude.
        internal bool TargetPreservedOnArm { get; private set; }
        internal float ArmedTargetPitchSnapshotDeg { get; private set; }
        internal string ArmedTargetPitchTextSnapshot { get; private set; } = "0";

        // Legacy/director shadow.  The existing PITCH outer-law still produces this
        // normalized value so v0.4.91 can preserve its established capture character;
        // it is no longer written to FlightCtrlState.pitch as a pilot command.
        internal float VirtualPilotPitch { get; private set; }
        internal float InjectedPitch { get; private set; }
        internal float RawPilotPitch { get; private set; }
        internal float PitchInputAfterNeutralization { get; private set; }
        internal float TrimCommand { get; private set; }
        internal string ControlState { get; private set; } = "Inactive";

        // Exact native AA transport state.  AERIS units remain deg/s until the handoff;
        // StandardFlyByWire receives radians/s and invokes AA's existing
        // PitchAngularVelocityController.
        internal float PitchRateRequestDegPerSec { get; private set; }
        internal float AaNativePitchRateDemandDegPerSec { get; private set; }
        internal float AaNativePitchRateDemandRadPerSec { get; private set; }
        internal bool AaNativePitchRateOverrideActive { get; private set; }

        // V/S owns its vertical-motion trajectory and passes only its planned pitch
        // angular rate through this native AA transport. BasePitch remains inside V/S
        // as a slow sustained-attitude reference; this field never writes FlightCtrlState
        // after AA.
        internal bool VerticalSpeedDirectRateActive { get; private set; }
        internal float VerticalSpeedDirectRateDemandDegPerSec { get; private set; }
        // HDG adaptive high-energy coordination contributes through this same native AA
        // transport. It is a bounded rate bias, never a second FlightCtrlState writer.
        internal bool LateralTurnAssistActive { get; private set; }
        internal float LateralTurnAssistRateDegPerSec { get; private set; }
        internal bool LateralTurnPitchPriorityActive { get; private set; }
        internal float LateralTurnPitchPriorityFloorDegPerSec { get; private set; }
        internal float LateralTurnBaseRateDegPerSec { get; private set; }
        internal float LateralTurnSuppressedOpposingRateDegPerSec { get; private set; }
        internal float LateralTurnMinimumNetRateFraction = 0.0f;
        internal float LateralTurnMaximumAssistRateDegPerSec = 15.0f;

        // AA's bundled pitch angular-velocity controller defaults to 0.70 rad/s.
        // The existing AERIS PITCH law previously reached AA through a normalized stick,
        // where desired rate = stick * max_v_construction.  Retaining that calibrated
        // conversion makes this an input-transport replacement, not a vertical-law retune.
        internal float NativePitchRatePerVirtualStickDegPerSec = 0.70f * Mathf.Rad2Deg;

        // v0.4.60: stronger capture authority for large pitch errors, while retaining
        // a very small near-target trim term.  The outer-law remains intentionally intact
        // in v0.4.91; only the AA transport is changed.
        internal float PitchErrorGain = 0.120f;
        internal float PitchRateDamping = 0.022f;
        internal float MaxPitchCommand = 0.55f;
        internal float PitchCommandSlewPerSec = 1.20f;
        internal float PitchDeadbandDeg = 0.04f;
        internal float TrimEntryErrorDeg = 1.50f;
        internal float TrimEntryRateDegPerSec = 8.0f;
        internal float TrimIntegralGain = 0.012f;
        internal float MaxTrimCommand = 0.055f;
        internal float TrimDecayPerSec = 0.10f;

        internal void SetArmed(bool armed, Vessel vessel, VirtualAttitudeInstrument attitude)
        {
            if (Armed == armed) return;
            // Keep both the applied numeric target and any prepared text intact across
            // the arm transition.  This makes a later reset/output clear incapable of
            // substituting the observed aircraft attitude for the user's selected value.
            float preservedTargetPitch = TargetPitch;
            string preservedTargetText = TargetPitchText;
            Armed = armed;
            ResetDirectorOutputs();
            if (armed)
            {
                TargetPitch = Mathf.Clamp(preservedTargetPitch, -85f, 85f);
                TargetPitchText = string.IsNullOrEmpty(preservedTargetText)
                    ? TargetPitch.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)
                    : preservedTargetText;
                ArmedTargetPitchSnapshotDeg = TargetPitch;
                ArmedTargetPitchTextSnapshot = TargetPitchText;
                TargetPreservedOnArm = true;
                // Arming must never overwrite a prepared or applied target. APPLY owns
                // TargetPitch, and SET CURRENT is the sole explicit capture operation.
                // Refresh observations for the UI/recorder only.
                if (attitude != null && attitude.InstrumentPitchValid)
                {
                    CurrentPitch = attitude.InstrumentPitchDeg;
                    ActualPitchRate = attitude.InstrumentPitchRateDegPerSec;
                    PitchError = Mathf.Clamp(TargetPitch - CurrentPitch, -180f, 180f);
                }
                ControlState = "Armed";
                AERISLogger.Info("[PITCH] armed: target preserved=" + TargetPitchText + " deg; transport=AA_NATIVE_PITCH_RATE.");
            }
            else
            {
                TargetPreservedOnArm = false;
                ControlState = "Inactive";
                AERISLogger.Info("[PITCH] disarmed.");
            }
        }

        internal void Disable(string reason)
        {
            if (!Armed && !AaNativePitchRateOverrideActive) return;
            Armed = false;
            ResetDirectorOutputs();
            ControlState = "Inactive";
            AERISLogger.Info("[PITCH] disabled: " + reason);
        }

        internal void SetCurrent(VirtualAttitudeInstrument attitude)
        {
            if (attitude == null || !attitude.InstrumentPitchValid) return;
            TargetPitch = Mathf.Clamp(attitude.InstrumentPitchDeg, -85f, 85f);
            TargetPitchText = TargetPitch.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            TrimCommand = 0f;
        }

        // Used by upper vertical directors (V/S, later ALT). This only changes the PITCH
        // target; it does not inject controls and does not reset the near-target trim each frame.
        internal void SetDirectedTarget(float targetPitchDeg)
        {
            TargetPitch = Mathf.Clamp(targetPitchDeg, -85f, 85f);
            TargetPitchText = TargetPitch.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal void SetVerticalSpeedRateDemand(float pitchRateDegPerSec)
        {
            VerticalSpeedDirectRateDemandDegPerSec = Mathf.Clamp(pitchRateDegPerSec, -NativePitchRatePerVirtualStickDegPerSec, NativePitchRatePerVirtualStickDegPerSec);
            VerticalSpeedDirectRateActive = true;
        }

        internal void ClearVerticalSpeedRateDemand()
        {
            VerticalSpeedDirectRateActive = false;
            VerticalSpeedDirectRateDemandDegPerSec = 0f;
        }

        internal void SetLateralTurnAssistRate(float pitchRateDegPerSec, bool active)
        {
            LateralTurnAssistActive = active && Mathf.Abs(pitchRateDegPerSec) > 0.001f;
            LateralTurnAssistRateDegPerSec = LateralTurnAssistActive
                ? Mathf.Clamp(pitchRateDegPerSec, -LateralTurnMaximumAssistRateDegPerSec,
                    LateralTurnMaximumAssistRateDegPerSec)
                : 0f;
            if (!LateralTurnAssistActive)
            {
                LateralTurnPitchPriorityActive = false;
                LateralTurnPitchPriorityFloorDegPerSec = 0f;
                LateralTurnBaseRateDegPerSec = 0f;
                LateralTurnSuppressedOpposingRateDegPerSec = 0f;
            }
        }

        float CombineLateralTurnPitchPriority(float baseRateDegPerSec)
        {
            LateralTurnBaseRateDegPerSec = baseRateDegPerSec;
            LateralTurnPitchPriorityActive = false;
            LateralTurnSuppressedOpposingRateDegPerSec = 0f;
            LateralTurnPitchPriorityFloorDegPerSec = 0f;
            if (!LateralTurnAssistActive) return baseRateDegPerSec;

            // v0.9.9: vertical modes retain full authority. The turn contribution is
            // additive only; ALT/V/S/PITCH may oppose or completely cancel it whenever
            // altitude or vertical-rate recovery requires nose-down/unload demand.
            float combined = baseRateDegPerSec + LateralTurnAssistRateDegPerSec;
            LateralTurnPitchPriorityFloorDegPerSec = 0f;
            return combined;
        }

        internal bool TrySetTarget(string text, out string error)
        {
            error = null;
            float value;
            if ((!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) &&
                !float.TryParse(text, out value)) || float.IsNaN(value) || float.IsInfinity(value))
            {
                error = "Enter a numeric pitch target.";
                return false;
            }
            if (value < -85f || value > 85f)
            {
                error = "Pitch target must be between -85 and +85 degrees.";
                return false;
            }
            TargetPitch = value;
            TargetPitchText = value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            TrimCommand = 0f;
            AERISLogger.Info("[PITCH] target=" + TargetPitchText + " deg");
            return true;
        }

        // Called by AERIS before AA's StandardFlyByWire callback.  It owns the pitch pilot
        // axis only long enough to neutralize it, then publishes AA's native angular-rate
        // demand.  AERIS never writes pitch after AA and never changes AA PID/adaptive logic.
        internal void ApplyAaNativePitchRateDemand(FlightCtrlState state, Vessel vessel, VirtualAttitudeInstrument attitude, bool aerisMaster, bool standardFbwActive)
        {
            if (state == null) return;
            bool pitchOwnerActive = Armed || LateralTurnAssistActive;
            bool executable = pitchOwnerActive && aerisMaster && standardFbwActive && vessel != null &&
                              !vessel.packed && !vessel.LandedOrSplashed && vessel.situation != Vessel.Situations.PRELAUNCH &&
                              attitude != null && attitude.InstrumentPitchValid;
            if (!executable)
            {
                VirtualPilotPitch = 0f;
                RawPilotPitch = 0f;
                PitchInputAfterNeutralization = state.pitch;
                InjectedPitch = state.pitch;
                TrimCommand = 0f;
                PitchRateRequestDegPerSec = 0f;
                LateralTurnPitchPriorityActive = false;
                LateralTurnPitchPriorityFloorDegPerSec = 0f;
                LateralTurnBaseRateDegPerSec = 0f;
                LateralTurnSuppressedOpposingRateDegPerSec = 0f;
                ClearAaNativePitchRateOverride();
                ControlState = Armed ? "Standby" : "Inactive";
                return;
            }

            FlightCtrlState pilot = FlightInputHandler.state;
            RawPilotPitch = pilot != null ? pilot.pitch : state.pitch;

            CurrentPitch = attitude.InstrumentPitchDeg;
            ActualPitchRate = attitude.InstrumentPitchRateDegPerSec;
            // Direct subtraction is clear over our bounded pitch targets and avoids any
            // heading-like wrap interpretation near level flight.
            PitchError = Mathf.Clamp(TargetPitch - CurrentPitch, -180f, 180f);
            float effectiveError = Mathf.Abs(PitchError) <= PitchDeadbandDeg ? 0f : PitchError;

            // Trim only after the main capture has settled. This removes a steady residual
            // without integrating during large manoeuvres or fighting AA's rate damping.
            bool trimEligible = Mathf.Abs(PitchError) <= TrimEntryErrorDeg &&
                                Mathf.Abs(ActualPitchRate) <= TrimEntryRateDegPerSec;
            if (trimEligible)
            {
                TrimCommand += effectiveError * TrimIntegralGain * Time.fixedDeltaTime;
                TrimCommand = Mathf.Clamp(TrimCommand, -MaxTrimCommand, MaxTrimCommand);
            }
            else
            {
                TrimCommand = Mathf.MoveTowards(TrimCommand, 0f, TrimDecayPerSec * Time.fixedDeltaTime);
            }

            if (VerticalSpeedDirectRateActive)
            {
                // V/S owns the vertical motion trajectory.  Do not reconstruct a pitch
                // target and then re-run PITCH's attitude loop: publish V/S's planned
                // pitch rate directly, exactly as HDG publishes a planned yaw rate.
                TrimCommand = 0f;
                PitchRateRequestDegPerSec = Mathf.Clamp(
                    CombineLateralTurnPitchPriority(VerticalSpeedDirectRateDemandDegPerSec),
                    -NativePitchRatePerVirtualStickDegPerSec, NativePitchRatePerVirtualStickDegPerSec);
                VirtualPilotPitch = PitchRateRequestDegPerSec / Mathf.Max(0.001f, NativePitchRatePerVirtualStickDegPerSec);
                ControlState = LateralTurnPitchPriorityActive
                    ? "VsDirectNativeRate+HdgPitchPriority" : "VsDirectNativeRate";
            }
            else if (Armed)
            {
                // PITCH standalone retains the proven attitude-to-rate outer law.
                float raw = effectiveError * PitchErrorGain - ActualPitchRate * PitchRateDamping + TrimCommand;
                raw = Mathf.Clamp(raw, -MaxPitchCommand, MaxPitchCommand);
                VirtualPilotPitch = Mathf.MoveTowards(VirtualPilotPitch, raw, PitchCommandSlewPerSec * Time.fixedDeltaTime);
                PitchRateRequestDegPerSec = Mathf.Clamp(
                    CombineLateralTurnPitchPriority(
                        VirtualPilotPitch * NativePitchRatePerVirtualStickDegPerSec),
                    -NativePitchRatePerVirtualStickDegPerSec, NativePitchRatePerVirtualStickDegPerSec);
            }
            else
            {
                TrimCommand = 0f;
                PitchRateRequestDegPerSec = CombineLateralTurnPitchPriority(0f);
                VirtualPilotPitch = PitchRateRequestDegPerSec /
                    Mathf.Max(0.001f, NativePitchRatePerVirtualStickDegPerSec);
                ControlState = "HdgAdaptiveHighGAssistNativeRate";
            }

            ControlUtils.neutralize_user_input(state, ControlUtils.PITCH);
            PitchInputAfterNeutralization = state.pitch;
            InjectedPitch = state.pitch;
            AaNativePitchRateDemandDegPerSec = PitchRateRequestDegPerSec;
            AaNativePitchRateDemandRadPerSec = PitchRateRequestDegPerSec * Mathf.Deg2Rad;
            StandardFlyByWire.ExternalPitchDemand = AaNativePitchRateDemandRadPerSec;
            StandardFlyByWire.ExternalPitchOverride = true;
            AaNativePitchRateOverrideActive = true;
            if (!VerticalSpeedDirectRateActive && Armed)
                ControlState = trimEligible ? "TrimNativeRate" : "CaptureNativeRate";
        }

        // Compatibility name retained for callers compiled against the previous source tree.
        // It now performs AA-native pitch-rate transport, not a direct virtual-stick write.
        internal void ApplyVirtualPilotInput(FlightCtrlState state, Vessel vessel, VirtualAttitudeInstrument attitude, bool aerisMaster, bool standardFbwActive)
        {
            ApplyAaNativePitchRateDemand(state, vessel, attitude, aerisMaster, standardFbwActive);
        }

        internal void ClearAaNativePitchRateOverride()
        {
            StandardFlyByWire.ExternalPitchOverride = false;
            StandardFlyByWire.ExternalPitchDemand = 0f;
            AaNativePitchRateOverrideActive = false;
            AaNativePitchRateDemandDegPerSec = 0f;
            AaNativePitchRateDemandRadPerSec = 0f;
            ClearVerticalSpeedRateDemand();
        }

        void ResetDirectorOutputs()
        {
            VirtualPilotPitch = 0f;
            InjectedPitch = 0f;
            RawPilotPitch = 0f;
            PitchInputAfterNeutralization = 0f;
            TrimCommand = 0f;
            PitchRateRequestDegPerSec = 0f;
            LateralTurnPitchPriorityActive = false;
            LateralTurnPitchPriorityFloorDegPerSec = 0f;
            LateralTurnBaseRateDegPerSec = 0f;
            LateralTurnSuppressedOpposingRateDegPerSec = 0f;
            ClearVerticalSpeedRateDemand();
            SetLateralTurnAssistRate(0f, false);
            ClearAaNativePitchRateOverride();
        }
    }
}
