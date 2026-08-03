/*
Atmosphere Autopilot, plugin for Kerbal Space Program.
Copyright (C) 2015-2016, Baranin Alexander aka Boris-Barboris.

Atmosphere Autopilot is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.
Atmosphere Autopilot is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.
You should have received a copy of the GNU General Public License
along with Atmosphere Autopilot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AtmosphereAutopilot
{
    public sealed class StandardFlyByWire : StateController
    {
        // AERIS callback, invoked after AA auto-throttle has produced its command.
        public static Func<float, float> ExternalThrottleFloor;
        // Ground Assist hard ceiling. Unlike the Protect floor, this may reduce demand
        // after touchdown so normal forward thrust cannot oppose landing deceleration.
        public static Func<float, float> ExternalThrottleCeiling;
        // ACC transport: AERIS may publish one bounded throttle demand before AA writes the
        // final FlightCtrlState. AA retains the sole final writer and Protect remains a floor.
        public static bool ExternalThrottleOverride;
        public static float ExternalThrottleDemand;
        // Optional external propulsion executor. Used by AERIS only after AA has generated
        // its native auto-throttle demand; it never changes the demand itself.
        public static Action<float> ExternalPropulsionDemand;

        // AERIS must never use its own mirrored FlightInputHandler value as the next
        // manual command. Keep the last genuine pilot throttle separately and restore
        // it when SPEED/PROTECT releases the axis.
        float capturedManualThrottle;
        bool capturedManualThrottleValid;
        bool automaticThrottleOwnedLastFrame;
        // AERIS axis overlays. These are consumed inside StandardFlyByWire so AA remains
        // the single writer of FlightCtrlState and pilot inputs can be selectively blocked.
        // When active, a director has neutralized the physical roll input before this point
        // and supplies the native AA desired roll rate in radians per second. The existing
        // RollAngularVelocityController and the final AA FlightCtrlState write remain unchanged.
        public static bool ExternalRollOverride;
        public static float ExternalRollDemand;
        // v0.4.90: AERIS HDG may supply a desired yaw angular velocity directly to
        // AA's existing YawAngularVelocityController. This is the yaw analogue of
        // ExternalRollDemand: AA remains the only final FlightCtrlState writer.
        public static bool ExternalYawOverride;
        public static float ExternalYawDemand;
        // v0.4.91: AERIS vertical modes may supply a desired pitch angular velocity directly to
        // AA's existing PitchAngularVelocityController. This is the pitch analogue of the
        // native roll/yaw transport; AA remains the only final FlightCtrlState writer.
        public static bool ExternalPitchOverride;
        public static float ExternalPitchDemand;
        // Contract v2 PRE-LEARN: short-lived, bounded feed-forward supplied by an
        // external learning client.  It is consumed inside AA and never bypasses
        // moderation, pilot arbitration, or AERIS Protect.
        public static bool ExternalTrimFeedForwardActive;
        public static float ExternalTrimRollInput;
        public static float ExternalTrimPitchInput;
        public static float ExternalTrimYawInput;
        public static float ExternalTrimRollRateRadPerSec;
        public static float ExternalTrimPitchRateRadPerSec;
        public static float ExternalTrimYawRateRadPerSec;
        // v0.9.3: Auto Takeoff may temporarily bypass only the ground-model AoA/G
        // moderation envelope while ROTATE owns pitch.  The AA pitch-rate controller
        // still consumes the demand and remains the sole final FlightCtrlState writer.
        // This flag must never be used by normal airborne PITCH/V/S/ALT control.
        public static bool ExternalGroundPitchModerationBypass;
        static bool externalThrottleReleaseBaselinePending;
        static float externalThrottleReleaseBaseline;
        // Snapshot after AA has applied all axis controllers. AERIS FDR consumes this
        // read-only telemetry; AA remains the sole FlightCtrlState writer.
        public static float LastFinalPitch;
        public static float LastFinalRoll;
        public static float LastFinalYaw;
        public static float LastFinalThrottle;
        // v0.8.31: pitch-rate moderation audit.  Units are radians/second here;
        // AERIS FDR converts them to degrees/second at the recording boundary.
        public static bool LastPitchRateExternalControlActive;
        public static bool LastPitchRateModerationEnvelopeAvailable;
        public static bool LastPitchRateModerationActive;
        public static bool LastPitchRateAoAModerationEnabled;
        public static bool LastPitchRateGModerationEnabled;
        public static float LastPitchRateRequestedRadPerSec;
        public static float LastPitchRateAppliedRadPerSec;
        public static float LastPitchRateModerationDeltaRadPerSec;
        public static float LastPitchRateLowerLimitRadPerSec;
        public static float LastPitchRateUpperLimitRadPerSec;
        PitchAngularVelocityController pc;
        RollAngularVelocityController rc;
        YawAngularVelocityController yvc;
        SideslipController yc;
        ProgradeThrustController tc;
        FlightModel im;
        AutopilotModule[] gui_list = new AutopilotModule[5];

        internal StandardFlyByWire(Vessel v) :
            base(v, "Standard Fly-By-Wire", 44421322) { }

        public override void InitializeDependencies(Dictionary<Type, AutopilotModule> modules)
        {
            gui_list[0] = pc = modules[typeof(PitchAngularVelocityController)] as PitchAngularVelocityController;
            gui_list[1] = rc = modules[typeof(RollAngularVelocityController)] as RollAngularVelocityController;
            gui_list[2] = yvc = modules[typeof(YawAngularVelocityController)] as YawAngularVelocityController;
            gui_list[3] = yc = modules[typeof(SideslipController)] as SideslipController;
            gui_list[4] = tc = modules[typeof(ProgradeThrustController)] as ProgradeThrustController;
            im = modules[typeof(FlightModel)] as FlightModel;
        }

        protected override void OnActivate()
        {
            ResetPitchRateModerationTelemetry();
            pc.Activate();
            pc.user_controlled = true;
            rc.Activate();
            rc.user_controlled = true;
            yvc.Activate();
            yvc.user_controlled = rocket_mode;
            yc.Activate();
            yc.user_controlled = true;
            tc.Activate();
            MessageManager.post_status_message("Standard Fly-By-Wire enabled");
        }

        protected override void OnDeactivate()
        {
            ResetPitchRateModerationTelemetry();
            pc.neutral_offset = 0.0f;
            pc.Deactivate();
            rc.Deactivate();
            yvc.Deactivate();
            yc.Deactivate();
            MessageManager.post_status_message("Standard Fly-By-Wire disabled");
        }

        [VesselSerializable("rocket_mode")]
        [AutoGuiAttr("Rocket mode", true)]
        public bool rocket_mode = false;

        public bool RocketMode
        {
            get
            {
                return rocket_mode;
            }
            set
            {
                if (value != rocket_mode)
                {
                    MessageManager.post_status_message(value ? "Rocket mode enabled" : "Rocket mode disabled");
                    rocket_mode = value;
                }
            }
        }

        [AutoGuiAttr("Moderation", true)]
        public bool moderation_switch
        {
            get
            {
                return (pc.moderate_aoa || pc.moderate_g || yvc.moderate_aoa || yvc.moderate_g);
            }
            set
            {
                if (value != moderation_switch)
                {
                    MessageManager.post_status_message(value ? "Moderation enabled" : "Moderation disabled");
                    pc.moderate_aoa = pc.moderate_g = yvc.moderate_aoa = yvc.moderate_g = value;
                }
            }
        }

        [GlobalSerializable("moderation_keycode")]
        [AutoHotkeyAttr("FBW moderation")]
        public static KeyCode moderation_keycode = KeyCode.O;

        [GlobalSerializable("rocket_mode_keycode")]
        [AutoHotkeyAttr("FBW rocket mode")]
        static KeyCode rocket_mode_keycode = KeyCode.None;

        [GlobalSerializable("coord_turn_keycode")]
        [AutoHotkeyAttr("FBW coord turn")]
        static KeyCode coord_turn_keycode = KeyCode.None;

        [AutoGuiAttr("Coordinated turn", true)]
        [VesselSerializable("coord_turn")]
        public bool coord_turn = false;

        public bool Coord_turn
        {
            get
            {
                return coord_turn;
            }
            set
            {
                if (value != coord_turn)
                {
                    MessageManager.post_status_message(value ? "Coord turn enabled" : "Coord turn disabled");
                    coord_turn = value;
                }
            }
        }

        public override void OnUpdate()
        {
            bool changed = false;
            if (Input.GetKeyDown(moderation_keycode))
            {
                moderation_switch = !moderation_switch;
                changed = true;
            }
            if (Input.GetKeyDown(rocket_mode_keycode))
            {
                RocketMode = !rocket_mode;
                changed = true;
            }
            if (Input.GetKeyDown(coord_turn_keycode))
            {
                Coord_turn = !coord_turn;
                changed = true;
            }
            if (changed)
                AtmosphereAutopilot.Instance.mainMenuGUIUpdate();
        }

        bool landed = false;
        bool need_restore = false;
        float time_after_takeoff = 0.0f;
        bool aoa_moder = true;
        bool g_moder = true;

        /// <summary>
        /// Main control function
        /// </summary>
        /// <param name="cntrl">Control state to change</param>
        public override void ApplyControl(FlightCtrlState cntrl)
        {
            if (vessel.LandedOrSplashed())
            {
                ResetPitchRateModerationTelemetry();

                // v0.9.14.4: a revert / vessel replacement can occur while AA is in
                // the short post-liftoff moderation-bypass window.  The old ground
                // branch returned before the restore path below, leaving pitch AoA/G
                // moderation permanently disabled on the replacement vessel and
                // causing Auto Takeoff ARM to be rejected.  Restore the saved pilot
                // configuration before accepting the new grounded lifecycle.
                if (need_restore)
                {
                    pc.moderate_aoa = aoa_moder;
                    pc.moderate_g = g_moder;
                    need_restore = false;
                    AERISFlightControl.Logging.AERISLogger.Info(
                        "[AA][MODERATION] restored pending post-liftoff AoA/G settings on grounded vessel lifecycle reset.");
                }

                landed = true;
                time_after_takeoff = 0.0f;
                // Normal AA ground behaviour remains transparent.  Only when AERIS
                // Ground Stability or Auto Takeoff explicitly owns an axis do we run
                // that AA-native controller on the ground.  AA is still the sole final
                // FlightCtrlState writer; AERIS supplies rate/throttle demand only.
                if (ExternalPitchOverride || ExternalRollOverride || ExternalYawOverride || ExternalThrottleOverride ||
                    ExternalTrimFeedForwardActive)
                    ApplyExternalGroundControl(cntrl);
                return;
            }

            // disable pitch moderation for two seconds after take-off
            if (landed || need_restore)
            {
                if (landed && !need_restore)
                {
                    aoa_moder = pc.moderate_aoa;
                    g_moder = pc.moderate_g;
                    pc.moderate_aoa = false;
                    pc.moderate_g = false;
                    landed = false;
                    need_restore = true;
                }
                if (time_after_takeoff > 1.5f)
                {
                    pc.moderate_aoa = aoa_moder;
                    pc.moderate_g = g_moder;
                    need_restore = false;
                }
                else
                    time_after_takeoff += TimeWarp.fixedDeltaTime;
            }

            // v0.2.76: throttle ownership with an explicit manual baseline.
            // Never read a value previously mirrored by AERIS as fresh pilot input.
            ConsumeExternalThrottleReleaseBaseline();
            float rawInputThrottle = ReadRawInputThrottle(cntrl);
            bool speedControlActive = tc.spd_control_enabled;

            // While no automatic owner is active, the raw input is the current pilot baseline.
            // Capture exactly once when SPEED/PROTECT first takes ownership.
            if (!automaticThrottleOwnedLastFrame)
            {
                capturedManualThrottle = rawInputThrottle;
                capturedManualThrottleValid = true;
            }
            else if (!capturedManualThrottleValid)
            {
                capturedManualThrottle = rawInputThrottle;
                capturedManualThrottleValid = true;
            }

            float manualThrottle = capturedManualThrottleValid
                ? Mathf.Clamp01(capturedManualThrottle)
                : rawInputThrottle;

            float speedThrottle = manualThrottle;
            if (speedControlActive)
            {
                tc.ApplyControl(cntrl, tc.setpoint.mps());
                speedThrottle = Mathf.Clamp01(cntrl.mainThrottle);
            }

            float protectFloor = 0f;
            var floorHook = ExternalThrottleFloor;
            if (floorHook != null)
            {
                try { protectFloor = Mathf.Clamp01(floorHook(0f)); }
                catch { protectFloor = 0f; }
            }

            // AERIS ACC is a native throttle owner, analogous to the existing AA speed
            // controller but with its own acceleration target law.  It is intentionally
            // selected before Protect, so Protect can still raise (never lower) the demand.
            bool externalThrottleActive = ExternalThrottleOverride;
            float externalThrottle = Mathf.Clamp01(ExternalThrottleDemand);
            float selectedOwnerThrottle = externalThrottleActive ? externalThrottle :
                (speedControlActive ? speedThrottle : manualThrottle);
            float finalThrottle = ApplyExternalThrottleCeiling(Mathf.Max(selectedOwnerThrottle, protectFloor));
            bool automaticOwnership = externalThrottleActive || speedControlActive || protectFloor > manualThrottle + 0.0001f;

            // On the release edge, restore the captured physical/manual setting to both
            // KSP throttle channels. Do not preserve the last AP/PROTECT output.
            if (!automaticOwnership && automaticThrottleOwnedLastFrame)
                finalThrottle = manualThrottle;

            cntrl.mainThrottle = finalThrottle;
            try
            {
                if (FlightInputHandler.state != null)
                {
                    FlightInputHandler.state.mainThrottle = finalThrottle;
                }
            }
            catch { }

            automaticThrottleOwnedLastFrame = automaticOwnership;
            if (!automaticOwnership)
            {
                // From the next frame on, FlightInputHandler is again allowed to refresh
                // the stored pilot baseline with actual manual input.
                capturedManualThrottle = finalThrottle;
                capturedManualThrottleValid = true;
            }

            var propulsionHook = ExternalPropulsionDemand;
            if (propulsionHook != null)
            {
                try { propulsionHook(cntrl.mainThrottle); }
                catch { }
            }

            ApplyExternalTrimInputs(cntrl);

            // Native external pitch-rate transport. When AERIS owns PITCH/V/S, it must
            // not route its attitude request through AA's pilot-input interpretation. The
            // owned pilot pitch channel is neutralized and AA's existing pitch-rate loop is
            // given the desired angular velocity directly. AA still owns all final output.
            if (ExternalPitchOverride)
            {
                ControlUtils.neutralize_user_input(cntrl, ControlUtils.PITCH);
                pc.user_controlled = false;
                pc.neutral_offset = 0.0f;
                pc.ApplyControl(cntrl, ExternalPitchDemand +
                    (ExternalTrimFeedForwardActive ? ExternalTrimPitchRateRadPerSec : 0f));
            }
            else
            {
                pc.user_controlled = true;
                if (coord_turn)
                {
                    // account for yaw velocity in pitch neutral offset to assist coordinated turn
                    Vector3 up_level_dir = Vector3.ProjectOnPlane(vessel.ReferenceTransform.position - vessel.mainBody.position,
                        vessel.ReferenceTransform.up).normalized;
                    float yaw_v_vert_project = Vector3.Dot(im.AngularVel(YAW) * vessel.ReferenceTransform.right, up_level_dir);
                    float pitch_vert_project = Vector3.Dot(up_level_dir, -vessel.ReferenceTransform.forward);
                    if (pitch_vert_project > 0.0f)
                    {
                        float level_pitch_vel = -yaw_v_vert_project / pitch_vert_project;
                        pc.neutral_offset = level_pitch_vel;
                    }
                    else
                        pc.neutral_offset = 0.0f;
                }
                else
                    pc.neutral_offset = 0.0f;
                pc.ApplyControl(cntrl, 0.0f);
            }

            LastPitchRateExternalControlActive = pc.ExternalRateControlActive;
            LastPitchRateModerationEnvelopeAvailable = pc.ModerationEnvelopeAvailable;
            LastPitchRateModerationActive = pc.ModerationEnvelopeAvailable &&
                pc.DesiredAngularVelocityModerated;
            LastPitchRateAoAModerationEnabled = pc.moderate_aoa;
            LastPitchRateGModerationEnabled = pc.moderate_g;
            LastPitchRateRequestedRadPerSec = pc.RequestedDesiredAngularVelocity;
            LastPitchRateAppliedRadPerSec = pc.AppliedDesiredAngularVelocity;
            LastPitchRateModerationDeltaRadPerSec =
                LastPitchRateAppliedRadPerSec - LastPitchRateRequestedRadPerSec;
            LastPitchRateLowerLimitRadPerSec = pc.ModerationLowerAngularVelocity;
            LastPitchRateUpperLimitRadPerSec = pc.ModerationUpperAngularVelocity;

            // Native external yaw-rate transport. When AERIS owns HDG yaw, do not
            // route an AERIS request through the pilot-input/sideslip path: pass the
            // desired angular velocity directly into AA's existing yaw-rate controller.
            // Manual yaw is neutralized only for that owned axis; AA still writes the
            // final FlightCtrlState and its own adaptive controller is unchanged.
            if (ExternalYawOverride)
            {
                ControlUtils.neutralize_user_input(cntrl, ControlUtils.YAW);
                yvc.user_controlled = false;
                yvc.ApplyControl(cntrl, ExternalYawDemand +
                    (ExternalTrimFeedForwardActive ? ExternalTrimYawRateRadPerSec : 0f));
            }
            else if (rocket_mode)
            {
                yvc.user_controlled = true;
                yvc.ApplyControl(cntrl, 0.0f);
            }
            else
            {
                yc.user_controlled = true;
                yc.ApplyControl(cntrl, 0.0f, 0.0f);
            }
            rc.user_controlled = !ExternalRollOverride;
            rc.ApplyControl(cntrl, ExternalRollOverride
                ? ExternalRollDemand + (ExternalTrimFeedForwardActive ? ExternalTrimRollRateRadPerSec : 0f)
                : 0.0f);

            // AA remains the final FlightCtrlState writer; AERIS observes this value only.
            LastFinalPitch = cntrl.pitch;
            LastFinalRoll = cntrl.roll;
            LastFinalYaw = cntrl.yaw;
            LastFinalThrottle = cntrl.mainThrottle;
        }

        public static void SetExternalThrottleReleaseBaseline(float throttle)
        {
            externalThrottleReleaseBaseline = Mathf.Clamp01(throttle);
            externalThrottleReleaseBaselinePending = true;
        }

        void ApplyExternalGroundControl(FlightCtrlState cntrl)
        {
            ConsumeExternalThrottleReleaseBaseline();

            float rawInputThrottle = ReadRawInputThrottle(cntrl);
            if (!automaticThrottleOwnedLastFrame)
            {
                capturedManualThrottle = rawInputThrottle;
                capturedManualThrottleValid = true;
            }
            float manualThrottle = capturedManualThrottleValid
                ? Mathf.Clamp01(capturedManualThrottle) : rawInputThrottle;
            bool automaticOwnership = ExternalThrottleOverride;
            float finalThrottle = automaticOwnership ? Mathf.Clamp01(ExternalThrottleDemand) : manualThrottle;
            if (!automaticOwnership && automaticThrottleOwnedLastFrame) finalThrottle = manualThrottle;
            finalThrottle = ApplyExternalThrottleCeiling(finalThrottle);
            cntrl.mainThrottle = finalThrottle;
            try
            {
                if (FlightInputHandler.state != null)
                    FlightInputHandler.state.mainThrottle = finalThrottle;
            }
            catch { }
            automaticThrottleOwnedLastFrame = automaticOwnership;
            if (!automaticOwnership)
            {
                capturedManualThrottle = finalThrottle;
                capturedManualThrottleValid = true;
            }

            var propulsionHook = ExternalPropulsionDemand;
            if (propulsionHook != null)
            {
                try { propulsionHook(cntrl.mainThrottle); }
                catch { }
            }

            ApplyExternalTrimInputs(cntrl);

            bool groundPitchModerationBypassActive =
                ExternalPitchOverride && ExternalGroundPitchModerationBypass;
            if (ExternalPitchOverride)
            {
                ControlUtils.neutralize_user_input(cntrl, ControlUtils.PITCH);
                pc.user_controlled = false;
                pc.neutral_offset = 0f;
                bool savedModerateAoA = pc.moderate_aoa;
                bool savedModerateG = pc.moderate_g;
                try
                {
                    // The landed AA model can expose a degenerate [0,0] moderation
                    // envelope.  During the deliberate Auto Takeoff ROTATE phase only,
                    // avoid that invalid envelope without bypassing AA's controller.
                    if (groundPitchModerationBypassActive)
                    {
                        pc.moderate_aoa = false;
                        pc.moderate_g = false;
                    }
                    pc.ApplyControl(cntrl, ExternalPitchDemand +
                        (ExternalTrimFeedForwardActive ? ExternalTrimPitchRateRadPerSec : 0f));
                }
                finally
                {
                    // Never leak the ground-only override into airborne control.
                    pc.moderate_aoa = savedModerateAoA;
                    pc.moderate_g = savedModerateG;
                }
            }
            if (ExternalYawOverride)
            {
                ControlUtils.neutralize_user_input(cntrl, ControlUtils.YAW);
                yvc.user_controlled = false;
                yvc.ApplyControl(cntrl, ExternalYawDemand +
                    (ExternalTrimFeedForwardActive ? ExternalTrimYawRateRadPerSec : 0f));
            }
            if (ExternalRollOverride)
            {
                ControlUtils.neutralize_user_input(cntrl, ControlUtils.ROLL);
                rc.user_controlled = false;
                rc.ApplyControl(cntrl, ExternalRollDemand +
                    (ExternalTrimFeedForwardActive ? ExternalTrimRollRateRadPerSec : 0f));
            }

            LastPitchRateExternalControlActive = ExternalPitchOverride;
            LastPitchRateModerationEnvelopeAvailable = pc.ModerationEnvelopeAvailable;
            LastPitchRateModerationActive = pc.ModerationEnvelopeAvailable && pc.DesiredAngularVelocityModerated;
            LastPitchRateAoAModerationEnabled = pc.moderate_aoa && !groundPitchModerationBypassActive;
            LastPitchRateGModerationEnabled = pc.moderate_g && !groundPitchModerationBypassActive;
            LastPitchRateRequestedRadPerSec = ExternalPitchOverride ? pc.RequestedDesiredAngularVelocity : 0f;
            LastPitchRateAppliedRadPerSec = ExternalPitchOverride ? pc.AppliedDesiredAngularVelocity : 0f;
            LastPitchRateModerationDeltaRadPerSec = LastPitchRateAppliedRadPerSec - LastPitchRateRequestedRadPerSec;
            LastPitchRateLowerLimitRadPerSec = ExternalPitchOverride ? pc.ModerationLowerAngularVelocity : 0f;
            LastPitchRateUpperLimitRadPerSec = ExternalPitchOverride ? pc.ModerationUpperAngularVelocity : 0f;
            LastFinalPitch = cntrl.pitch;
            LastFinalRoll = cntrl.roll;
            LastFinalYaw = cntrl.yaw;
            LastFinalThrottle = cntrl.mainThrottle;
        }

        static void ApplyExternalTrimInputs(FlightCtrlState cntrl)
        {
            if (cntrl == null || !ExternalTrimFeedForwardActive) return;
            // These are bounded virtual pilot-axis feed-forward values.  Rate-owned
            // axes receive the corresponding rate term instead, avoiding double use.
            if (!ExternalPitchOverride)
                cntrl.pitch = Mathf.Clamp(cntrl.pitch + Mathf.Clamp(ExternalTrimPitchInput, -0.15f, 0.15f), -1f, 1f);
            if (!ExternalRollOverride)
                cntrl.roll = Mathf.Clamp(cntrl.roll + Mathf.Clamp(ExternalTrimRollInput, -0.20f, 0.20f), -1f, 1f);
            if (!ExternalYawOverride)
                cntrl.yaw = Mathf.Clamp(cntrl.yaw + Mathf.Clamp(ExternalTrimYawInput, -0.15f, 0.15f), -1f, 1f);
        }

        static float ApplyExternalThrottleCeiling(float throttle)
        {
            float value = Mathf.Clamp01(throttle);
            var ceilingHook = ExternalThrottleCeiling;
            if (ceilingHook == null) return value;
            try { return Mathf.Clamp01(ceilingHook(value)); }
            catch { return value; }
        }

        void ConsumeExternalThrottleReleaseBaseline()
        {
            if (!externalThrottleReleaseBaselinePending) return;
            capturedManualThrottle = Mathf.Clamp01(externalThrottleReleaseBaseline);
            capturedManualThrottleValid = true;
            externalThrottleReleaseBaselinePending = false;
        }


        protected override void _drawGUI(int id) { }

        static void ResetPitchRateModerationTelemetry()
        {
            LastPitchRateExternalControlActive = false;
            LastPitchRateModerationEnvelopeAvailable = false;
            LastPitchRateModerationActive = false;
            LastPitchRateAoAModerationEnabled = false;
            LastPitchRateGModerationEnabled = false;
            LastPitchRateRequestedRadPerSec = 0f;
            LastPitchRateAppliedRadPerSec = 0f;
            LastPitchRateModerationDeltaRadPerSec = 0f;
            LastPitchRateLowerLimitRadPerSec = 0f;
            LastPitchRateUpperLimitRadPerSec = 0f;
        }

        float ReadRawInputThrottle(FlightCtrlState fallback)
        {
            try
            {
                if (FlightInputHandler.state != null)
                {
                    // When AERIS owned the previous frame this is our echo, not pilot intent.
                    if (automaticThrottleOwnedLastFrame && capturedManualThrottleValid)
                        return Mathf.Clamp01(capturedManualThrottle);
                    return Mathf.Clamp01(FlightInputHandler.state.mainThrottle);
                }
            }
            catch { }
            return fallback != null ? Mathf.Clamp01(fallback.mainThrottle) : Mathf.Clamp01(capturedManualThrottle);
        }

    }
}
