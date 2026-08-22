using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.Protect;
using AERISFlightControl.Autopilot;
using AERISFlightControl.API;
using AERISFlightControl.FlightState;
using System.Collections.Generic;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Recording
{
    // Flight-scoped black-box recorder. CVR records all AERIS log events; FDR is sampled at a fixed rate.
    // Capture remains on the KSP thread; CSV encoding and every file operation are handled
    // by the bounded ordered background writer.
    internal sealed class AERISFlightDataRecorder
    {
        const float SampleIntervalSeconds = 0.10f; // 10 Hz Flight Test mode
        const float FlushIntervalSeconds = 1.0f;
        const int MaxExtensionTelemetryChannels = 256;
        // R016 isolation test: suppress only built-in control-cadence diagnostic CSV producers.
        // Core 10 Hz FDR, CVR, extension telemetry and AA comparison remain untouched.
        static readonly bool R016HighRateDiagnosticsEnabled = false;
        const string R016IsolationVariant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION";
        internal static string R016IsolationMarker { get { return R016IsolationVariant; } }
        long sessionOrdinal;
        bool recoveryScanRequested;

        // v0.5.7 Phase-1/2 AERIS-AA FlightState crosscheck schema. It is written only when
        // the DEBUG opt-in is enabled. Keep this independent from all flight-control FDR streams.
        static readonly string[] AaComparisonHeader = new string[] {
            "utc",
            "ut",
            "fixed_time_s",
            "fixed_delta_time_s",
            "vessel_name",
            "vessel_id",
            "situation",
            "main_body",
            "mode",
            "comparison_enabled",
            "comparison_ready",
            "comparison_exclusion_reason",
            "comparison_sample_time_diff_s",
            "aeris_state_valid",
            "aeris_sample_age_s",
            "aeris_last_sample_fixed_time_s",
            "aa_model_available",
            "aa_state_valid",
            "aa_sample_age_s",
            "aa_last_model_update_fixed_time_s",
            "aa_model_update_sequence",
            "aa_warmup_complete",
            "aa_reference_attitude_valid",
            "aa_reference_heading_valid",
            "aa_virtual_attitude_valid",
            "aa_virtual_heading_valid",
            "aeris_master_active",
            "lateral_mode",
            "vertical_mode",
            "speed_mode",
            "bank_active",
            "hdg_active",
            "pitch_active",
            "vs_active",
            "alt_active",
            "vel_active",
            "acc_active",
            "ap_target_bank_deg",
            "ap_target_heading_deg",
            "ap_target_pitch_deg",
            "ap_target_vs_mps",
            "ap_target_altitude_m",
            "ap_target_speed_mps",
            "actual_bank_deg",
            "actual_heading_deg",
            "actual_pitch_deg",
            "actual_vs_mps",
            "actual_altitude_asl_m",
            "actual_radar_altitude_m",
            "actual_surface_speed_mps",
            "dynamic_pressure_band",
            "stall_margin_deg",
            "protect_active",
            "aeris_pitch_deg",
            "aa_reference_pitch_deg",
            "pitch_reference_diff_deg",
            "aeris_roll_deg",
            "aa_reference_roll_deg",
            "roll_reference_diff_deg",
            "aeris_heading_deg",
            "aa_reference_heading_deg",
            "heading_reference_diff_deg",
            "aeris_virtual_attitude_available",
            "aeris_virtual_pitch_deg",
            "aa_virtual_pitch_deg",
            "virtual_pitch_diff_deg",
            "aeris_virtual_roll_deg",
            "aa_virtual_roll_deg",
            "virtual_roll_diff_deg",
            "aeris_virtual_heading_deg",
            "aa_virtual_heading_deg",
            "virtual_heading_diff_deg",
            "aeris_reference_vs_aa_virtual_quaternion_diff_deg",
            "aeris_pitch_rate_deg_s",
            "aa_pitch_rate_deg_s",
            "pitch_rate_diff_deg_s",
            "aeris_roll_rate_deg_s",
            "aa_roll_rate_deg_s",
            "roll_rate_diff_deg_s",
            "aeris_yaw_rate_deg_s",
            "aa_yaw_rate_deg_s",
            "yaw_rate_diff_deg_s",
            "aeris_pitch_acc_deg_s2",
            "aa_pitch_acc_deg_s2",
            "pitch_acc_diff_deg_s2",
            "aeris_roll_acc_deg_s2",
            "aa_roll_acc_deg_s2",
            "roll_acc_diff_deg_s2",
            "aeris_yaw_acc_deg_s2",
            "aa_yaw_acc_deg_s2",
            "yaw_acc_diff_deg_s2",
            "aeris_surface_speed_mps",
            "aa_surface_speed_mps",
            "surface_speed_diff_mps",
            "true_air_speed_available",
            "aeris_true_air_speed_mps",
            "aa_true_air_speed_mps",
            "true_air_speed_diff_mps",
            "true_air_speed_source",
            "aeris_vertical_speed_mps",
            "aa_vertical_speed_mps",
            "vertical_speed_diff_mps",
            "aeris_altitude_asl_m",
            "aa_altitude_asl_m",
            "altitude_asl_diff_m",
            "radar_altitude_available",
            "aeris_radar_altitude_m",
            "aa_radar_altitude_m",
            "radar_altitude_diff_m",
            "aeris_dynamic_pressure_kpa",
            "aa_dynamic_pressure_kpa",
            "dynamic_pressure_diff_kpa",
            "aeris_atmospheric_density_kg_m3",
            "aa_atmospheric_density_kg_m3",
            "atmospheric_density_diff_kg_m3",
            "aeris_aoa_valid",
            "aeris_pitch_aoa_deg",
            "aa_pitch_aoa_deg",
            "pitch_aoa_diff_deg",
            "aeris_roll_aoa_deg",
            "aa_roll_aoa_deg",
            "roll_aoa_diff_deg",
            "aeris_yaw_aoa_deg",
            "aa_yaw_aoa_deg",
            "yaw_aoa_diff_deg",
            "aeris_common_kinematic_baseline_valid",
            "aeris_common_kinematic_baseline_source",
            "aeris_attitude_source",
            "aa_reference_attitude_source",
            "aa_virtual_attitude_source",
            "aeris_angular_acceleration_source",
            "aa_angular_acceleration_source",
            "aa_surface_speed_source",
            "aa_vertical_speed_source",
            "aa_altitude_source",
            "radar_altitude_source",
            "aa_dynamic_pressure_source",
            "density_source",
            "aoa_source",
            "aoa_difference_semantics",
            "analysis_ready",
            "analysis_flight_regime",
            "analysis_lateral_regime",
            "analysis_vertical_regime",
            "analysis_turn_regime",
            "analysis_speed_regime",
            "analysis_dynamic_pressure_regime",
            "analysis_altitude_regime",
            "analysis_maneuver_regime",
            "analysis_condition_key",
            "analysis_summary_eligible",
            "comparison_schema"
        };

        // Phase 2 summary is written at the end of a flight. It aggregates only rows with
        // comparison_ready=1; all categories are labels for analysis and never controller inputs.
        static readonly string[] AaComparisonSummaryHeader = new string[] {
            "comparison_schema",
            "analysis_condition_key",
            "analysis_flight_regime",
            "analysis_lateral_regime",
            "analysis_vertical_regime",
            "analysis_turn_regime",
            "analysis_speed_regime",
            "analysis_dynamic_pressure_regime",
            "analysis_altitude_regime",
            "analysis_maneuver_regime",
            "ready_samples",
            "condition_run_count",
            "condition_observed_duration_s",
            "condition_first_seen_s",
            "condition_last_seen_s",
            "condition_span_s",
            "pitch_reference_abs_mean_deg", "pitch_reference_rms_deg", "pitch_reference_abs_max_deg",
            "roll_reference_abs_mean_deg", "roll_reference_rms_deg", "roll_reference_abs_max_deg",
            "heading_reference_abs_mean_deg", "heading_reference_rms_deg", "heading_reference_abs_max_deg",
            "pitch_rate_abs_mean_deg_s", "pitch_rate_rms_deg_s", "pitch_rate_abs_max_deg_s",
            "roll_rate_abs_mean_deg_s", "roll_rate_rms_deg_s", "roll_rate_abs_max_deg_s",
            "yaw_rate_abs_mean_deg_s", "yaw_rate_rms_deg_s", "yaw_rate_abs_max_deg_s",
            "vertical_speed_abs_mean_mps", "vertical_speed_rms_mps", "vertical_speed_abs_max_mps",
            "pitch_aoa_abs_mean_deg", "pitch_aoa_rms_deg", "pitch_aoa_abs_max_deg",
            "roll_aoa_abs_mean_deg", "roll_aoa_rms_deg", "roll_aoa_abs_max_deg",
            "yaw_aoa_abs_mean_deg", "yaw_aoa_rms_deg", "yaw_aoa_abs_max_deg",
            "reference_virtual_quaternion_mean_deg", "reference_virtual_quaternion_rms_deg", "reference_virtual_quaternion_max_deg"
        };
        const string AaComparisonSchema = "AERIS-AA-FlightStateCrosscheck-v2.2";
        // Phase 1.1 readiness threshold. This does not gate any controller; it only marks
        // comparison rows that are mature enough for statistical analysis.
        const int AaComparisonWarmupModelUpdates = 16;
        readonly object sync = new object();
        AERISAsyncFileChannel cvrWriter;
        AERISAsyncFileChannel fdrWriter;
        AERISAsyncFileChannel bankDiagnosticsWriter;
        AERISAsyncFileChannel apSmoothnessWriter;
        AERISAsyncFileChannel vsDiagnosticsWriter;
        AERISAsyncFileChannel vsCruiseAccelerationGuideWriter;
        AERISAsyncFileChannel pitchDiagnosticsWriter;
        AERISAsyncFileChannel hdgDiagnosticsWriter;
        // v0.5.4 keeps the validated trajectory laws and repairs ALT terminal phase handoff: earlier rollout lead, tighter precision entry, and bounded V/S low-rate tracking.
        AERISAsyncFileChannel altDiagnosticsWriter;
        // Dedicated SPEED traces. VEL is the upper trajectory planner and ACC remains
        // the lower acceleration/throttle director.
        AERISAsyncFileChannel accelerationDiagnosticsWriter;
        AERISAsyncFileChannel velocityDiagnosticsWriter;
        AERISAsyncFileChannel groundTakeoffDiagnosticsWriter;
        // v0.6.0 default-ON, independent observation-only crosscheck stream plus condition-labelled Phase-2 analysis.
        AERISAsyncFileChannel aaComparisonWriter;
        string folder;
        string vesselId;
        string vesselName;
        float nextSample;
        float nextFlush;
        float nextBankDiagnosticsSample;
        float nextApSmoothnessSample;
        float nextVsDiagnosticsSample;
        float nextPitchDiagnosticsSample;
        float nextHdgDiagnosticsSample;
        float nextAltDiagnosticsSample;
        float nextAccelerationDiagnosticsSample;
        float nextVelocityDiagnosticsSample;
        float nextGroundTakeoffDiagnosticsSample;
        float previousApSampleTime;
        float previousApSpeed;
        float previousApPitchCommand;
        float previousApRollCommand;
        float previousApYawCommand;
        float previousApThrottleCommand;
        int sampleCount;
        int eventCount;
        float maxSpeed;
        float maxAoA;
        float maxG;
        int protectInterventions;
        bool previousProtect;
        readonly Dictionary<string, AERISRecorderTelemetrySchema> schemas = new Dictionary<string, AERISRecorderTelemetrySchema>();
        readonly Dictionary<string, AERISAsyncFileChannel> telemetryWriters = new Dictionary<string, AERISAsyncFileChannel>();
        readonly Dictionary<string, float> nextTelemetryWrite = new Dictionary<string, float>();
        readonly HashSet<string> disabledTelemetryChannels = new HashSet<string>();
        // Phase 2: observer-only, flight-local aggregates keyed by the per-row condition label.
        readonly Dictionary<string, AaComparisonConditionSummary> aaComparisonSummaries = new Dictionary<string, AaComparisonConditionSummary>();

        sealed class AaComparisonMetric
        {
            internal int Count;
            internal double SumAbs;
            internal double SumSquares;
            internal float MaxAbs = float.NaN;

            internal void Add(float value)
            {
                if (float.IsNaN(value) || float.IsInfinity(value)) return;
                float absolute = Mathf.Abs(value);
                Count++;
                SumAbs += absolute;
                SumSquares += (double)value * value;
                if (float.IsNaN(MaxAbs) || absolute > MaxAbs) MaxAbs = absolute;
            }

            internal float MeanAbs { get { return Count > 0 ? (float)(SumAbs / Count) : float.NaN; } }
            internal float Rms { get { return Count > 0 ? (float)Math.Sqrt(SumSquares / Count) : float.NaN; } }
        }

        sealed class AaComparisonConditionSummary
        {
            internal readonly string Key;
            internal readonly string FlightRegime;
            internal readonly string LateralRegime;
            internal readonly string VerticalRegime;
            internal readonly string TurnRegime;
            internal readonly string SpeedRegime;
            internal readonly string DynamicPressureRegime;
            internal readonly string AltitudeRegime;
            internal readonly string ManeuverRegime;
            internal int Samples;
            internal int RunCount;
            internal float ObservedDurationSeconds;
            internal float FirstFixedTime = float.NaN;
            internal float LastFixedTime = float.NaN;
            internal float PreviousFixedTime = float.NaN;
            internal readonly AaComparisonMetric PitchReference = new AaComparisonMetric();
            internal readonly AaComparisonMetric RollReference = new AaComparisonMetric();
            internal readonly AaComparisonMetric HeadingReference = new AaComparisonMetric();
            internal readonly AaComparisonMetric PitchRate = new AaComparisonMetric();
            internal readonly AaComparisonMetric RollRate = new AaComparisonMetric();
            internal readonly AaComparisonMetric YawRate = new AaComparisonMetric();
            internal readonly AaComparisonMetric VerticalSpeed = new AaComparisonMetric();
            internal readonly AaComparisonMetric PitchAoA = new AaComparisonMetric();
            internal readonly AaComparisonMetric RollAoA = new AaComparisonMetric();
            internal readonly AaComparisonMetric YawAoA = new AaComparisonMetric();
            internal readonly AaComparisonMetric ReferenceVirtualQuaternion = new AaComparisonMetric();

            internal AaComparisonConditionSummary(string key, string flightRegime, string lateralRegime, string verticalRegime,
                string turnRegime, string speedRegime, string dynamicPressureRegime, string altitudeRegime, string maneuverRegime)
            {
                Key = key; FlightRegime = flightRegime; LateralRegime = lateralRegime; VerticalRegime = verticalRegime;
                TurnRegime = turnRegime; SpeedRegime = speedRegime; DynamicPressureRegime = dynamicPressureRegime;
                AltitudeRegime = altitudeRegime; ManeuverRegime = maneuverRegime;
            }

            internal void Add(float fixedTime, float pitchReference, float rollReference, float headingReference,
                float pitchRate, float rollRate, float yawRate, float verticalSpeed,
                float pitchAoA, float rollAoA, float yawAoA, float referenceVirtualQuaternion)
            {
                Samples++;
                if (float.IsNaN(FirstFixedTime))
                {
                    FirstFixedTime = fixedTime;
                    RunCount = 1;
                }
                else
                {
                    float gap = fixedTime - PreviousFixedTime;
                    // Recorder output is normally 10 Hz while FixedUpdate may be 0.02 s.
                    // A longer interruption starts a new contiguous run instead of inflating duration.
                    if (gap > 0f && gap <= 0.26f) ObservedDurationSeconds += gap;
                    else if (gap > 0.26f) RunCount++;
                }
                LastFixedTime = fixedTime;
                PreviousFixedTime = fixedTime;
                PitchReference.Add(pitchReference); RollReference.Add(rollReference); HeadingReference.Add(headingReference);
                PitchRate.Add(pitchRate); RollRate.Add(rollRate); YawRate.Add(yawRate); VerticalSpeed.Add(verticalSpeed);
                PitchAoA.Add(pitchAoA); RollAoA.Add(rollAoA); YawAoA.Add(yawAoA);
                ReferenceVirtualQuaternion.Add(referenceVirtualQuaternion);
            }
        }

        internal string CurrentFolder { get { return folder ?? string.Empty; } }

        static string RootPath
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "AERISFlightControl", "FlightData"); }
        }

        internal void BeginFlight(Vessel vessel)
        {
            AERISFlightDataArchive.DrainResults();
            if (vessel == null || vessel.transform == null) return;
            lock (sync)
            {
                try
                {
                    BeginFlightCore(vessel);
                }
                catch (Exception ex)
                {
                    // Queue admission is not assumed to be atomic. Always close any channel
                    // accepted before the failing one so a later attempt starts cleanly.
                    EndFlight("recorder-open-failed");
                    throw new IOException("Could not start the AERIS flight recorder", ex);
                }
            }
        }

        void BeginFlightCore(Vessel vessel)
        {
            string id = vessel.id.ToString();
            if (cvrWriter != null && id == vesselId) return;
            EndFlight("vessel-change");
            // Scan only before this recorder opens its first session. Re-scanning on every
            // vessel change can race the ordered Close/Seal commands for the session that
            // just ended and mistake a still-open folder for abandoned raw data.
            aaComparisonSummaries.Clear();

            vesselId = id;
            vesselName = vessel.vesselName ?? "unknown-vessel";
            string safeName = Sanitize(vesselName);
            folder = Path.Combine(RootPath, DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss_fff") +
                "_" + (++sessionOrdinal).ToString("D6", CultureInfo.InvariantCulture) +
                "_" + safeName + "_" + StableHash8(id));
            if (!recoveryScanRequested)
            {
                recoveryScanRequested = true;
                // The scan runs asynchronously and may start after the Open commands have
                // created this new folder, so carry an explicit exclusion rather than relying
                // on timing between the archive and writer lanes.
                AERISFlightDataArchive.QueueRecoveryArchives(RootPath, folder);
            }
            cvrWriter = new AERISAsyncFileChannel(Path.Combine(folder, "cvr_events.csv"), false,
                AERISFileRecordPriority.CriticalEvent);
            fdrWriter = new AERISAsyncFileChannel(Path.Combine(folder, "fdr_flight.csv"), false,
                AERISFileRecordPriority.Continuous);
            bankDiagnosticsWriter = OpenDiagnostic("fdr_bank_diagnostics.csv");
            apSmoothnessWriter = OpenDiagnostic("fdr_ap_smoothness.csv");
            vsDiagnosticsWriter = OpenDiagnostic("fdr_vs_diagnostics.csv");
            vsCruiseAccelerationGuideWriter = OpenDiagnostic("fdr_vs_cruise_acceleration_guide.csv");
            pitchDiagnosticsWriter = OpenDiagnostic("fdr_pitch_diagnostics.csv");
            hdgDiagnosticsWriter = OpenDiagnostic("fdr_hdg_diagnostics.csv");
            altDiagnosticsWriter = OpenDiagnostic("fdr_alt_diagnostics.csv");
            accelerationDiagnosticsWriter = OpenDiagnostic("fdr_acc_diagnostics.csv");
            velocityDiagnosticsWriter = OpenDiagnostic("fdr_vel_diagnostics.csv");
            groundTakeoffDiagnosticsWriter = OpenDiagnostic("fdr_ground_takeoff.csv");
            if (!CoreChannelsAvailable())
            {
                AERISBackgroundFileWriter.RetainRawSession(folder,
                    "recorder channel admission failed; raw session retained");
                throw new IOException("one or more recorder channels were rejected");
            }
            cvrWriter.WriteLine("utc,ut,source,level,message");
            groundTakeoffDiagnosticsWriter.WriteLine("utc,ut,ground_enabled,ground_available,reliable_grounded,liftoff_confirmed,ground_control_active,ground_status,surface_speed_mps,radar_altitude_m,vertical_speed_mps,target_heading_deg,current_heading_deg,heading_error_deg,pilot_yaw,pilot_roll,pilot_shared_control,yaw_rate_demand_deg_s,roll_rate_demand_deg_s,yaw_authority_scale,roll_authority_scale,aa_yaw_override,aa_roll_override,post_touchdown_session,ground_throttle_cut_active,reverse_thrust_control_active,aa_ground_throttle_override,ground_assist_master_enabled,ground_brake_assist_configured,ground_brake_assist_active,ground_brake_assist_status,touchdown_stable_s,requested_decel_mps2,measured_decel_mps2,brake_demand,final_brake_demand,wheel_brake_applied_demand,wheel_brake_stock_fallback_active,wheel_brake_module_count,pilot_brake_request_active,ground_ownership_blend,brake_fallback_evidence_s,ground_stability_allowance,brake_capability_mps2_per_unit,ground_airbrake_link_configured,ground_airbrake_link_demand,ground_parking_hold_configured,ground_parking_hold_active,ground_parking_hold_pilot_release_count,ground_drag_chute_auto_configured,ground_drag_chute_status,ground_drag_chute_deployed_count,ground_reverse_auto_configured,ground_reverse_status,ground_reverse_demand,ground_reverse_provider_id,auto_propulsion_mode,auto_external_propulsion_takeoff,auto_attempt_generation,auto_armed_vessel_persistent_id,auto_engine_stage_number,auto_engine_stage_status,auto_brake_release_confirmed,auto_phase,auto_status,auto_armed,auto_executing,stall_estimate_mps,selected_vr_mps,vr_source,vr_detail,vr_frozen,rotation_gate_ready,rotation_gate_reason,auto_pitch_rate_demand_deg_s,auto_throttle_demand,aa_pitch_override,aa_throttle_override,brakes,final_pitch,final_roll,final_yaw,final_throttle");
            bankDiagnosticsWriter.WriteLine("utc,ut,control_dt_s,surface_speed_mps,dynamic_pressure_kpa,horizon_bank_deg,bank_target_deg,bank_error_deg,bank_horizon_rate_raw_deg_s,bank_horizon_rate_trend_deg_s,bank_horizon_rate_trend_residual_deg,bank_horizon_rate_trend_window_s,bank_horizon_rate_trend_span_s,bank_horizon_rate_trend_samples,bank_actual_roll_rate_deg_s,bank_rate_request_deg_s,bank_rate_error_deg_s,bank_target_stick_legacy_shadow,bank_legacy_virtual_stick_shadow,bank_roll_input_after_neutralization,bank_aa_native_rate_override_active,bank_aa_native_rate_demand_deg_s,bank_aa_native_rate_demand_rad_s,bank_virtual_roll_delta_legacy,bank_virtual_roll_slew_per_s_legacy,bank_command_sign,bank_command_sign_flips_1s,bank_rate_sign_flips_1s,bank_error_sign_flips_1s,bank_step_score,bank_oscillation_score,bank_terminal_chatter_suppressed,bank_terminal_slew_scale,bank_terminal_lock_remaining_s,bank_transition_quieting_active,bank_transition_slew_scale,bank_transition_hold_remaining_s,bank_transition_update_gated,bank_transition_update_interval_s,bank_transition_held_target,bank_transition_command_updates_1s,bank_transition_delta_suppressed,bank_transition_command_delta,bank_transition_command_deadband,bank_transition_raw_rate_request,bank_transition_shaped_rate_request,bank_transition_rate_accel_limit,bank_transition_rate_shaper_active,bank_transition_zero_capture_active,bank_transition_zero_capture_rate_deg_s,bank_transition_rate_feedback_deadband_active,bank_transition_rate_feedback_deadband_deg_s,bank_effective_roll_rate_deg_s,bank_dynamic_pressure_kpa,bank_q_schedule,bank_q_high_schedule,bank_q_mode,bank_q_rate_scale,bank_limited_roll_rate_deg_s,bank_terminal_latched,bank_settle_quiet_elapsed_s,bank_settle_brake_reentry_pending,bank_settle_brake_reentry_elapsed_s,bank_settle_brake_reentry_forced,bank_trajectory_hold_latched,bank_trajectory_hold_quiet_elapsed_s,bank_trajectory_hold_entry_band_deg,bank_trajectory_hold_exit_band_deg,bank_stopping_rate_limit_deg_s,bank_trajectory_rate_error_deg_s,bank_trajectory_terminal_blend,bank_trajectory_scheduled_decel_deg_s2,bank_precision_altitude_eligible,bank_precision_altitude_m,bank_precision_hold_active,bank_precision_correction_active,bank_precision_within_target,bank_precision_within_target_elapsed_s,bank_precision_target_tolerance_deg,bank_precision_neutral_band_deg,bank_precision_rate_command_deg_s,bank_precision_rate_limit_deg_s,bank_precision_rate_gain_per_s,bank_precision_rate_damping,bank_control_state,bank_capture_phase");
            apSmoothnessWriter.WriteLine("utc,ut,active_any,roll_axis_active,pitch_axis_active,yaw_axis_active,throttle_axis_active,lateral_mode,vertical_mode,speed_mode,control_dt_s,surface_speed_mps,speed_delta_mps,speed_accel_mps2,vertical_speed_mps,dynamic_pressure_kpa,g_force,aoa_deg,beta_deg,bank_deg,pitch_deg,heading_deg,roll_rate_deg_s,pitch_rate_deg_s,yaw_rate_deg_s,pitch_legacy_virtual_input_shadow,pitch_input_after_neutralization,pitch_aa_native_rate_override_active,pitch_aa_native_rate_demand_deg_s,pitch_aa_native_rate_demand_rad_s,roll_input_after_neutralization,yaw_input_after_neutralization,virtual_throttle,hdg_legacy_virtual_yaw_shadow,hdg_aa_native_yaw_rate_override_active,hdg_aa_native_yaw_rate_demand_deg_s,hdg_aa_native_yaw_rate_demand_rad_s,final_pitch,final_roll,final_yaw,final_throttle,pitch_legacy_virtual_input_shadow_delta,roll_input_after_neutralization_delta,yaw_input_after_neutralization_delta,virtual_throttle_delta,pitch_legacy_virtual_input_shadow_slew_per_s,roll_input_after_neutralization_slew_per_s,yaw_input_after_neutralization_slew_per_s,virtual_throttle_slew_per_s,bank_target_deg,bank_error_deg,bank_rate_request_deg_s,bank_dynamic_pressure_kpa,bank_q_schedule,bank_q_high_schedule,bank_q_mode,bank_q_rate_scale,bank_limited_roll_rate_deg_s,bank_step_score,bank_oscillation_score,bank_terminal_chatter_suppressed,bank_terminal_slew_scale,bank_terminal_lock_remaining_s,bank_transition_quieting_active,bank_transition_slew_scale,bank_transition_hold_remaining_s,bank_transition_update_gated,bank_transition_update_interval_s,bank_transition_held_target,bank_transition_command_updates_1s,bank_transition_delta_suppressed,bank_transition_command_delta,bank_transition_command_deadband,bank_transition_raw_rate_request,bank_transition_shaped_rate_request,bank_transition_rate_accel_limit,bank_transition_rate_shaper_active,bank_transition_zero_capture_active,bank_transition_zero_capture_rate_deg_s,bank_transition_rate_feedback_deadband_active,bank_transition_rate_feedback_deadband_deg_s,bank_effective_roll_rate_deg_s,final_roll_minus_roll_input_after_neutralization,final_roll_delta,final_roll_slew_per_s,aa_roll_activity_governor_active_legacy,aa_roll_activity_governor_suppressed_legacy,aa_final_roll_observed_input,aa_final_roll_observed_output,aa_final_roll_governor_delta_legacy,bank_aa_native_rate_override_active,bank_aa_native_rate_demand_deg_s,bank_aa_native_rate_demand_rad_s,bank_precision_altitude_eligible,bank_precision_hold_active,bank_precision_correction_active,bank_precision_within_target,bank_precision_target_tolerance_deg,bank_precision_rate_command_deg_s,axis_roll_state,axis_roll_reversals,axis_pitch_state,axis_pitch_reversals,axis_yaw_state,axis_yaw_reversals,pitch_aafbw_external_rate_control_active,pitch_aafbw_moderation_envelope_available,pitch_aafbw_moderation_active,pitch_aafbw_requested_rate_deg_s,pitch_aafbw_applied_rate_deg_s,pitch_aafbw_moderation_delta_deg_s,pitch_aafbw_lower_rate_limit_deg_s,pitch_aafbw_upper_rate_limit_deg_s,pitch_aafbw_aoa_moderation_enabled,pitch_aafbw_g_moderation_enabled");
            vsDiagnosticsWriter.WriteLine("utc,ut,vs_armed,vs_control_active,vs_error_valid,vs_requested_target_mps,vs_effective_target_mps,vs_control_target_mps,vs_target_mps,vs_current_mps,vs_error_mps,vs_control_error_mps,vs_accel_mps2,vs_predicted_mps,vs_predicted_stop_error_mps,vs_proportional_pitch_deg,vs_damping_pitch_deg,vs_brake_pitch_deg,vs_recovery_pitch_deg,vs_trim_pitch_deg,vs_precision_trim_step_deg,vs_base_pitch_deg,vs_base_pitch_adapt_deg,vs_base_pitch_speed_adapt_deg,vs_base_pitch_speed_adapt_active,vs_precision_base_pitch_active,vs_precision_within_target,vs_precision_within_target_elapsed_s,vs_precision_target_tolerance_mps,vs_precision_neutral_band_mps,vs_precision_base_pitch_rate_deg_s,vs_precision_base_pitch_adapt_deg,vs_precision_trim_transfer_deg,vs_precision_entry_elapsed_s,vs_precision_exit_elapsed_s,vs_precision_hold_entry_elapsed_s,vs_precision_hold_exit_elapsed_s,vs_precision_phase,vs_precision_phase_transitions,vs_precision_capture_phase,vs_precision_rate_gain_deg_per_mps_s,vs_precision_accel_damping_deg_per_mps2_s,vs_precision_rate_limit_deg_s,vs_base_pitch_speed_adapt_rate_deg_s,vs_precision_net_base_pitch_rate_deg_s,vs_surface_speed_mps,vs_surface_speed_rate_mps2,vs_desired_pitch_before_clamp_deg,vs_desired_pitch_after_clamp_deg,vs_pitch_target_saturated,vs_pitch_upper_saturated,vs_pitch_lower_saturated,vs_generated_pitch_target_deg,vs_planned_pitch_rate_deg_s,vs_direct_pitch_rate_active,vs_rate_p_deg_s,vs_rate_damping_deg_s,vs_rate_brake_deg_s,vs_base_pitch_hold_rate_deg_s,vs_attitude_error_deg,vs_attitude_rate_p_deg_s,vs_attitude_rate_damping_deg_s,vs_rate_target_deg_s,vs_rate_command_slew_deg_s2,vs_direct_rate_scheme,vs_max_pitch_deg,vs_altitude_pitch_limit_active,vs_altitude_pitch_limit_deg,vs_altitude_precision_hold_active,vs_effective_deadband_mps,vs_error_reversal_band_mps,vs_effective_max_pitch_deg,vs_overshoot_latch,vs_state,pitch_armed,pitch_target_deg,pitch_current_deg,pitch_error_deg,pitch_rate_request_deg_s,pitch_aa_native_rate_override_active,pitch_aa_native_rate_demand_deg_s,pitch_aa_native_rate_demand_rad_s,vs_dynamic_pressure_kpa,vs_q_high_schedule,vs_q_mode,vs_high_q_manual_zero_profile_active,vs_high_q_manual_zero_blend,vs_high_q_manual_zero_capture_guard_active,vs_high_q_manual_zero_capture_guard_blend,vs_manual_zero_transition_guard_active,vs_manual_zero_transition_guard_blend,vs_manual_zero_transition_guard_remaining_s,vs_manual_zero_transition_guard_from_mps,vs_manual_zero_transition_guard_q_blend,vs_manual_zero_trajectory_active,vs_manual_zero_trajectory_target_mps,vs_manual_zero_trajectory_scheduled_decel_mps2,vs_manual_zero_trajectory_applied_decel_mps2,vs_manual_zero_trajectory_q_blend,vs_manual_zero_trajectory_initial_mps,vs_manual_zero_trajectory_elapsed_s,vs_manual_zero_trajectory_state,vs_high_q_nonzero_precision_capture_active,vs_high_q_nonzero_precision_capture_blend,vs_high_q_nonzero_precision_filtered_accel_mps2,vs_high_q_nonzero_precision_damping_scale,vs_high_q_nonzero_precision_damping_limit_deg,vs_high_q_nonzero_precision_base_pitch_damping_scale,vs_high_q_tracking_active,vs_high_q_tracking_blend,vs_high_q_tracking_filtered_accel_mps2,vs_high_q_tracking_damping_scale,vs_high_q_tracking_damping_limit_deg,vs_high_q_tracking_pitch_slew_scale,vs_high_q_tracking_direct_rate_scale,vs_high_q_tracking_rate_command_slew_scale,vs_high_q_tracking_base_pitch_damping_scale,vs_effective_error_reversal_band_mps,vs_high_q_proportional_scale,vs_high_q_damping_scale,vs_high_q_damping_limit_deg,vs_high_q_pitch_slew_scale,vs_high_q_applied_pitch_slew_deg_s,vs_low_q_vertical_envelope_active,vs_low_q_vertical_envelope_blend,vs_low_q_vertical_envelope_applied_blend,vs_low_q_filtered_accel_mps2,vs_low_q_effective_max_pitch_deg,vs_low_q_proportional_scale,vs_low_q_damping_scale,vs_low_q_damping_limit_deg,vs_low_q_pitch_slew_scale,vs_low_q_direct_rate_scale,vs_low_q_rate_command_slew_scale,vs_low_q_base_pitch_adapt_scale,vs_mid_q_tracking_active,vs_mid_q_tracking_blend,vs_mid_q_filtered_accel_mps2,vs_mid_q_p_scale,vs_mid_q_d_scale,vs_mid_q_d_limit_deg,vs_mid_q_pitch_slew_scale,vs_mid_q_direct_rate_scale,vs_mid_q_rate_command_slew_scale,vs_mid_q_base_pitch_damping_scale,vs_tracking_envelope_active,vs_tracking_envelope_blend,vs_tracking_filtered_accel_mps2,vs_tracking_pitch_slew_scale,vs_tracking_attitude_rate_damping_scale,vs_tracking_rate_limit_deg_s,vs_tracking_rate_slew_deg_s2,vs_tracking_reversal_gate_active,vs_tracking_damping_dominance_limit_deg,vs_alt_precision_tracking_latched,vs_alt_precision_tracking_enter_elapsed_s,vs_alt_precision_tracking_exit_elapsed_s,vs_alt_low_q_precision_quieting_active,vs_alt_low_q_precision_quieting_blend,vs_alt_low_q_precision_rate_authority_recovery_blend,vs_alt_low_q_precision_quieting_rate_scale,vs_alt_low_q_precision_quieting_damping_scale,vs_alt_low_q_precision_effective_rate_limit_deg_s");
            vsCruiseAccelerationGuideWriter.WriteLine("utc,ut,vs_cruise_guide_active,vs_cruise_guide_blend,vs_error_mps,vs_measured_accel_mps2,vs_cruise_desired_accel_mps2,vs_cruise_accel_error_mps2,vs_cruise_rate_command_deg_s,vs_cruise_legacy_base_pitch_rate_deg_s,vs_cruise_applied_base_pitch_rate_deg_s,vs_cruise_pre_brake_active,vs_precision_phase,vs_low_q_blend,pitch_aafbw_moderation_active,pitch_aafbw_requested_rate_deg_s,pitch_aafbw_applied_rate_deg_s,pitch_aafbw_moderation_delta_deg_s");
            hdgDiagnosticsWriter.WriteLine("utc,ut,hdg_armed,hdg_control_state,hdg_target_deg,hdg_current_deg,hdg_error_deg,hdg_raw_bank_target_deg,hdg_commanded_bank_target_deg,hdg_auto_max_bank_limit_deg,hdg_effective_max_bank_limit_deg,hdg_safe_low_speed_bank_active,hdg_safe_low_speed_bank_sample_active,hdg_safe_low_speed_bank_observed_g,hdg_safe_low_speed_bank_measured_max_deg,hdg_safe_low_speed_bank_capability_limit_deg,hdg_safe_low_speed_bank_authority_limit_deg,hdg_safe_low_speed_bank_authority_blend,hdg_safe_low_speed_bank_speed_blend,hdg_safe_low_speed_bank_q_blend,hdg_safe_low_speed_bank_stall_blend,hdg_safe_low_speed_bank_altitude_blend,hdg_safe_low_speed_bank_reason,hdg_rollout_start_error_deg,hdg_rollout_hold_active,bank_target_deg,horizon_bank_deg,bank_error_deg,bank_actual_roll_rate_deg_s,bank_aa_native_rate_demand_deg_s,bank_aa_native_rate_override_active,hdg_terminal_yaw_active,hdg_terminal_yaw_capture_band,hdg_terminal_yaw_raw_legacy_command,hdg_terminal_yaw_legacy_command,hdg_terminal_yaw_legacy_p_term,hdg_terminal_yaw_legacy_rate_damping_term,hdg_coordinated_yaw_legacy_command,hdg_coordinated_yaw_legacy_feedforward,hdg_coordinated_yaw_legacy_rate_correction,hdg_legacy_virtual_yaw_shadow,hdg_yaw_rate_actual_deg_s,hdg_terminal_yaw_rate_raw_deg_s,hdg_terminal_yaw_rate_command_deg_s,hdg_terminal_yaw_rate_p_term_deg_s,hdg_terminal_yaw_rate_damping_term_deg_s,hdg_coordinated_yaw_rate_target_deg_s,hdg_coordinated_yaw_rate_command_deg_s,hdg_yaw_rate_request_deg_s,hdg_aa_native_yaw_rate_override_active,hdg_aa_native_yaw_rate_demand_deg_s,hdg_aa_native_yaw_rate_demand_rad_s,hdg_yaw_input_after_neutralization,final_yaw,hdg_terminal_roll_assist_active,hdg_terminal_roll_assist_raw_deg,hdg_terminal_roll_assist_filtered_deg,hdg_terminal_roll_assist_deg,hdg_terminal_roll_assist_hold_active,hdg_terminal_roll_assist_reverse_pending,hdg_terminal_bank_quiet_zone_active,hdg_thin_air_assist_enabled,hdg_thin_air_assist_active,hdg_high_g_turn_latched,hdg_high_g_turn_phase,hdg_thin_air_blend,hdg_thin_air_assist_blend,hdg_thin_air_response_ratio,hdg_thin_air_weak_elapsed_s,hdg_thin_air_bank_target_deg,hdg_thin_air_bank_speed_blend,hdg_thin_air_pitch_assist_deg_s,hdg_thin_air_pitch_kinematic_deg_s,hdg_thin_air_pitch_floor_deg_s,hdg_thin_air_pitch_feedback_deg_s,hdg_high_g_rollout_lead_deg,hdg_high_g_target,hdg_high_g_commanded,hdg_high_g_measured,hdg_high_g_stability_score,hdg_high_g_stall_authority,hdg_high_g_tracking_authority,hdg_high_g_entry_elapsed_s,hdg_high_g_latched_elapsed_s,hdg_high_g_release_elapsed_s,hdg_high_g_release_reason,hdg_vertical_turn_altitude_m,hdg_vertical_turn_speed_m_s,hdg_vertical_turn_stall_margin_deg,hdg_vertical_turn_heading_rate_deg_s,hdg_vertical_turn_altitude_gate,hdg_vertical_turn_speed_gate,hdg_vertical_turn_stall_margin_gate,hdg_vertical_turn_heading_error_gate,hdg_vertical_turn_qualification_status,hdg_aa_limit_hold_active,hdg_aa_limit_hold_reason,hdg_aa_limit_hold_elapsed_s,hdg_aa_limit_recovery_elapsed_s,hdg_critical_condition_elapsed_s,hdg_aa_pitch_requested_deg_s,hdg_aa_pitch_applied_deg_s,hdg_aa_pitch_moderation_delta_deg_s,hdg_aa_pitch_authority,hdg_aa_limit_g_cap,hdg_aa_limit_pitch_cap_deg_s,hdg_margin_governor_active,hdg_margin_governor_reason,hdg_stall_margin_rate_deg_s,hdg_predicted_stall_margin_deg,hdg_margin_governor_authority,hdg_margin_recovery_elapsed_s,hdg_estimated_sustainable_g,hdg_capability_g_cap,hdg_capability_tracking_error_g,hdg_capability_limited,hdg_capability_bank_cap_deg,hdg_bank_target_slew_limit_deg_s,hdg_low_q_envelope_blend,hdg_low_q_bank_cap_deg,hdg_low_q_g_cap,hdg_low_q_pitch_cap_deg_s,hdg_stall_recovery_active,hdg_turn_yaw_bank_fade,hdg_turn_yaw_rate_target_deg_s,hdg_attitude_stability_yaw_target_deg_s,hdg_attitude_stability_yaw_command_deg_s,hdg_attitude_stability_yaw_sideslip_term_deg_s,hdg_attitude_stability_yaw_rate_damping_term_deg_s,hdg_attitude_stability_yaw_accel_damping_term_deg_s,hdg_yaw_assist_mode");
                altDiagnosticsWriter.WriteLine("utc,ut,alt_armed,alt_control_active,alt_target_m,alt_hold_reference_m,alt_hold_reference_offset_m,alt_hold_band_lower_m,alt_hold_band_upper_m,alt_current_m,alt_error_m,alt_selected_target_error_m,alt_hold_band_error_m,alt_inside_preferred_hold_band,alt_current_vs_mps,alt_reference_vs_mps,alt_reconciled_vs_mps,alt_rate_bias_mps,alt_rate_reconciliation_active,alt_rate_reconciliation_blend,alt_rate_command_bias_mps,alt_desired_vs_mps,alt_planned_vs_mps,alt_vs_demand_mps,alt_stopping_rate_limit_mps,alt_stop_distance_m,alt_transport_lead_m,alt_measured_brake_lag_rate_mps,alt_measured_brake_lag_lead_m,alt_terminal_vs_damping_per_s,alt_terminal_effective_fine_band_m,alt_terminal_effective_max_rate_mps,alt_terminal_inner_settle_active,alt_terminal_inner_settle_effective_band_m,alt_terminal_inner_settle_effective_exit_band_m,alt_terminal_inner_settle_effective_max_rate_mps,alt_terminal_inner_settle_effective_brake_rate_mps,alt_terminal_inner_settle_effective_damping_per_s,alt_terminal_predictive_brake_active,alt_terminal_predictive_brake_effective_lead_s,alt_terminal_predictive_brake_effective_band_m,alt_terminal_predictive_brake_inbound_rate_mps,alt_terminal_predictive_brake_time_to_target_s,alt_terminal_predictive_brake_demand_mps,alt_precision_reference_vs_mps,alt_precision_reference_rate_active,alt_precision_direct_reference_active,alt_precision_reference_delta_vs_reconciled_mps,alt_precision_entry_measured_rate_ok,alt_precision_entry_planned_rate_ok,alt_precision_entry_direction_ok,alt_precision_entry_ready,alt_precision_entry_physical_planned_rate_mps,alt_hold_neutral_command_mps,alt_bank_support_eligible,alt_bank_support_active,alt_bank_support_bank_deg,alt_bank_support_roll_rate_deg_s,alt_bank_support_load_factor_excess,alt_bank_support_sink_activation,alt_bank_support_transition_rate_mps,alt_bank_support_target_rate_mps,alt_bank_support_rate_mps,alt_bank_support_terminal_band_m,alt_hold_disturbance_recovery_active,alt_hold_disturbance_exit_elapsed_s,alt_hold_disturbance_tracking_band_mps,alt_hold_disturbance_direction_gate_active,alt_hold_disturbance_outward_rate_mps,alt_hold_disturbance_raw_exit_candidate,alt_hold_disturbance_precision_ownership_active,alt_hold_disturbance_precision_ownership_band_m,alt_hold_capture_brake_active,alt_hold_capture_brake_hysteresis_active,alt_hold_capture_brake_completion_blend,alt_hold_capture_brake_taper_exponent,alt_hold_capture_brake_enter_mps,alt_hold_capture_brake_exit_mps,alt_hold_capture_brake_outward_rate_mps,alt_hold_capture_brake_effective_damping_per_s,alt_hold_capture_brake_effective_max_rate_mps,alt_hold_neutral_rate_brake_active,alt_hold_neutral_rate_brake_abs_rate_mps,alt_hold_neutral_rate_brake_enter_mps,alt_hold_neutral_rate_brake_exit_mps,alt_hold_neutral_rate_brake_full_mps,alt_hold_neutral_rate_brake_completion_blend,alt_hold_residual_rate_completion_active,alt_hold_residual_rate_completion_release_active,alt_hold_residual_rate_completion_calm,alt_hold_residual_rate_completion_physical_rate_mps,alt_hold_residual_rate_completion_abs_rate_mps,alt_hold_residual_rate_completion_planned_rate_mps,alt_hold_residual_rate_completion_planned_exit_mps,alt_hold_residual_rate_completion_damping_tail_scale,alt_hold_residual_rate_completion_damping_blend,alt_hold_residual_rate_completion_position_blend,alt_hold_residual_rate_completion_position_release_per_s,alt_hold_residual_rate_completion_effective_position_gain_per_s,alt_hold_pipeline_unload_active,alt_hold_pipeline_unload_gain,alt_hold_pipeline_unload_physical_gate_start_mps,alt_hold_pipeline_unload_physical_gate_full_mps,alt_hold_pipeline_unload_planned_gate_start_mps,alt_hold_pipeline_unload_planned_gate_full_mps,alt_hold_pipeline_unload_physical_toward_rate_mps,alt_hold_pipeline_unload_planned_physical_rate_mps,alt_hold_pipeline_unload_planned_toward_rate_mps,alt_hold_pipeline_unload_physical_gate_blend,alt_hold_pipeline_unload_planned_gate_blend,alt_hold_pipeline_unload_blend,alt_hold_pipeline_unload_raw_before_mps,alt_hold_pipeline_unload_requested_rate_mps,alt_hold_pipeline_unload_applied_rate_mps,alt_precision_low_q_rate_gain_active,alt_precision_low_q_rate_gain_q_blend,alt_precision_low_q_rate_gain_error_blend,alt_precision_low_q_rate_gain_blend,alt_precision_low_q_rate_gain_baseline_per_s,alt_precision_low_q_rate_gain_target_per_s,alt_precision_low_q_rate_gain_effective_per_s,alt_precision_low_q_rate_gain_full_band_m,alt_precision_low_q_rate_gain_release_band_m,alt_precision_low_q_damping_active,alt_precision_low_q_damping_q_blend,alt_precision_low_q_damping_baseline_per_s,alt_precision_low_q_damping_target_per_s,alt_precision_low_q_damping_effective_per_s,alt_micro_trim_enabled,alt_micro_trim_eligible,alt_micro_trim_pulse_active,alt_micro_trim_observation_active,alt_micro_trim_pulse_rate_mps,alt_micro_trim_pulse_elapsed_s,alt_micro_trim_wait_elapsed_s,alt_micro_trim_learned_pulse_magnitude_mps,alt_micro_trim_learned_pulse_duration_s,alt_micro_trim_learned_wait_s,alt_micro_trim_learned_delay_s,alt_micro_trim_learned_response_gain,alt_micro_trim_observed_response_mps,alt_micro_trim_applied_rate_mps,alt_micro_trim_pulse_count,alt_micro_trim_observer_ready,alt_micro_trim_observer_correlation,alt_micro_trim_cycle_period_s,alt_micro_trim_half_cycle_s,alt_micro_trim_pulse_scheduled,alt_micro_trim_scheduled_wait_s,alt_micro_trim_predicted_future_rate_mps,alt_micro_trim_base_raw_rate_mps,alt_micro_trim_safe_magnitude_mps,alt_micro_trim_target_crossing_count,alt_micro_trim_last_crossing_rate_mps,alt_micro_trim_future_half_cycles,alt_micro_trim_observer_input_command_mps,alt_micro_trim_observer_base_command_mps,alt_micro_trim_pair_guard_active,alt_micro_trim_last_applied_pulse_direction,alt_micro_trim_positive_pulse_count,alt_micro_trim_negative_pulse_count,alt_micro_trim_bias_estimate_m,alt_micro_trim_bias_guard_active,alt_micro_trim_bias_guard_elapsed_s,alt_micro_trim_bias_recovery_active,alt_micro_trim_bias_recovery_blend,alt_micro_trim_bias_corrective_direction,alt_micro_trim_bias_pulse_scale,alt_micro_trim_bias_hard_guard_active,alt_micro_trim_bias_hard_guard_recovery_permitted,alt_micro_trim_bias_hard_guard_inhibit_active,alt_micro_trim_bias_hard_guard_reason,alt_hold_inbound_arrival_brake_active,alt_hold_inbound_arrival_brake_enter_mps,alt_hold_inbound_arrival_brake_full_mps,alt_hold_inbound_arrival_brake_lead_start_s,alt_hold_inbound_arrival_brake_lead_full_s,alt_hold_inbound_arrival_brake_low_q_damping_per_s,alt_hold_inbound_arrival_brake_rate_mps,alt_hold_inbound_arrival_brake_time_to_target_s,alt_hold_inbound_arrival_brake_rate_gate_blend,alt_hold_inbound_arrival_brake_blend,alt_hold_inbound_arrival_brake_effective_damping_per_s,alt_hold_disturbance_exit_candidate,alt_hold_disturbance_required_dwell_s,alt_rollout_active,alt_hold_latched,alt_precision_correction_active,alt_precision_neutral_enter_band_m,alt_precision_neutral_exit_band_m,alt_precision_min_rate_mps,alt_precision_correction_rate_mps,alt_precision_raw_rate_mps,alt_precision_rate_gain_per_s,alt_precision_vertical_speed_damping_per_s,alt_precision_command_slew_mps2,alt_hold_entry_elapsed_s,alt_hold_exit_elapsed_s,alt_rate_accel_limit_mps2,alt_rate_brake_accel_limit_mps2,alt_scheduled_decel_mps2,alt_max_vs_mps,alt_max_pitch_deg,alt_low_q_vertical_envelope_active,alt_low_q_dynamic_pressure_kpa,alt_low_q_blend,alt_low_q_vs_cap_mps,alt_low_q_effective_accel_limit_mps2,alt_low_q_effective_brake_accel_limit_mps2,alt_low_q_effective_scheduled_decel_mps2,alt_low_q_effective_terminal_corridor_m,alt_low_q_symmetric_rate_cap_active,alt_low_q_output_vs_mps,alt_aoa_climb_governor_active,alt_aoa_climb_governor_aoa_valid,alt_aoa_climb_governor_aoa_deg,alt_aoa_climb_governor_blend,alt_aoa_climb_governor_target_vs_cap_mps,alt_aoa_climb_governor_applied_vs_cap_mps,alt_aoa_climb_governor_output_vs_mps,alt_aoa_climb_governor_surface_speed_mps,alt_control_state,vs_armed,vs_control_active,vs_altitude_rate_demand_active,vs_altitude_rate_demand_mps,vs_altitude_pitch_limit_active,vs_altitude_pitch_limit_deg,vs_effective_max_pitch_deg,vs_effective_target_mps,vs_current_mps,vs_error_mps,vs_precision_phase,pitch_aa_native_rate_demand_deg_s,vs_alt_precision_tracking_latched,vs_alt_precision_tracking_enter_elapsed_s,vs_alt_precision_tracking_exit_elapsed_s,vs_alt_low_q_precision_quieting_active,vs_alt_low_q_precision_quieting_blend,vs_alt_low_q_precision_rate_authority_recovery_blend,vs_alt_low_q_precision_quieting_rate_scale,vs_alt_low_q_precision_quieting_damping_scale,vs_alt_low_q_precision_effective_rate_limit_deg_s");
            accelerationDiagnosticsWriter.WriteLine("utc,ut,acc_armed,acc_control_active,acc_error_valid,acc_control_state,acc_target_mps2,acc_surface_speed_mps,acc_raw_measured_mps2,acc_filtered_mps2,acc_error_mps2,acc_effective_error_mps2,acc_effective_deadband_mps2,acc_velocity_planner_precision_active,acc_velocity_planner_throttle_bias,acc_velocity_planner_bias_adaptation,acc_velocity_planner_bias_limit,acc_velocity_planner_coast_authority_active,acc_velocity_planner_bias_at_limit,acc_base_throttle,acc_base_adaptation,acc_zero_hold_trim_active,acc_zero_hold_trim_adaptation,acc_zero_hold_trim_error_mps2,acc_throttle_correction,acc_raw_throttle_demand,acc_throttle_demand,acc_throttle_slew_per_s,acc_dynamic_pressure_kpa,acc_q_correction_scale,acc_thrust_saturated,acc_coast_limited,acc_limit_state,acc_limit_elapsed_s,acc_thrust_saturated_elapsed_s,acc_coast_limited_elapsed_s,acc_aa_native_throttle_override_active,acc_aa_native_throttle_demand,aa_final_throttle,acc_airbrake_demand,acc_airbrake_decel_shortfall_mps2,airbrake_auto_enabled,airbrake_auto_active,airbrake_requested_demand,airbrake_applied_demand,airbrake_q_limit_scale,airbrake_applied_angle_deg,airbrake_eligible_surface_count,airbrake_status");
            velocityDiagnosticsWriter.WriteLine("utc,ut,vel_armed,vel_target_confirmed,vel_control_active,vel_error_valid,vel_control_state,vel_target_speed_mps,vel_current_speed_mps,vel_error_mps,vel_predicted_error_mps,vel_measured_acceleration_mps2,vel_projected_stopping_speed_lead_mps,vel_acceleration_tracking_lead_mps,vel_acceleration_tracking_lead_s,vel_desired_acceleration_mps2,vel_planned_acceleration_mps2,vel_published_acceleration_mps2,vel_hold_active,vel_dynamic_pressure_kpa,vel_q_planner_scale,vel_configured_accel_limit_mps2,vel_effective_max_accel_mps2,vel_effective_max_decel_mps2,vel_effective_jerk_mps3,acc_effective_target_mps2,acc_filtered_acceleration_mps2,acc_error_mps2,acc_effective_deadband_mps2,acc_velocity_planner_precision_active,acc_velocity_planner_bias_limit,acc_velocity_planner_coast_authority_active,acc_velocity_planner_bias_at_limit,acc_control_state,acc_limit_state");
            pitchDiagnosticsWriter.WriteLine("utc,ut,pitch_armed,pitch_control_state,pitch_target_deg,pitch_current_deg,pitch_error_deg,pitch_actual_rate_deg_s,pitch_target_preserved_on_arm,pitch_armed_target_snapshot_deg,pitch_armed_target_text_snapshot,pitch_legacy_virtual_input_shadow,pitch_raw_pilot_input,pitch_input_after_neutralization,pitch_rate_request_deg_s,pitch_aa_native_rate_override_active,pitch_aa_native_rate_demand_deg_s,pitch_aa_native_rate_demand_rad_s,pitch_vs_direct_rate_active,pitch_vs_direct_rate_demand_deg_s,final_pitch,pitch_aafbw_external_rate_control_active,pitch_aafbw_moderation_envelope_available,pitch_aafbw_moderation_active,pitch_aafbw_requested_rate_deg_s,pitch_aafbw_applied_rate_deg_s,pitch_aafbw_moderation_delta_deg_s,pitch_aafbw_lower_rate_limit_deg_s,pitch_aafbw_upper_rate_limit_deg_s,pitch_aafbw_aoa_moderation_enabled,pitch_aafbw_g_moderation_enabled,pitch_lateral_turn_assist_active,pitch_lateral_turn_assist_rate_deg_s,pitch_lateral_turn_priority_active,pitch_lateral_turn_priority_floor_deg_s,pitch_lateral_turn_base_rate_deg_s,pitch_lateral_turn_suppressed_opposing_rate_deg_s");

            // Every recorder schema is self-consistent. Dedicated V/S and HDG traces keep
            // vertical and terminal-heading tuning independent from legacy AP compatibility data.
            RecordCvr("RECORDER", "INFO", "schema main=102 bank=86 ap=115 pitch=37 vs=163 vscruise=18 hdg=131 alt=243 acc=49 vel=35 ground=83; legacy NAV channels absent; aa-comparison=v2.2 default-on (header fields, including utc/ut)");
            fdrWriter.WriteLine("utc,ut,altitude_m,radar_alt_m,surface_speed_mps,heading_deg,pitch_deg,roll_deg,aoa_deg,beta_deg,g_force,pilot_pitch,pilot_roll,pilot_yaw,pilot_throttle,master,pitch_mode_armed,pitch_target_deg,pitch_current_deg,pitch_error_deg,pitch_rate_request_deg_s,pitch_aa_native_rate_override_active,pitch_aa_native_rate_demand_deg_s,pitch_aa_native_rate_demand_rad_s,pitch_raw_pilot_input,pitch_input_after_neutralization,pitch_control_state,bank_mode_armed,bank_target_deg,bank_current_deg,bank_error_deg,bank_rate_request_deg_s,bank_actual_roll_rate_deg_s,bank_aa_native_rate_demand_deg_s,bank_raw_pilot_roll,bank_roll_input_after_neutralization,bank_aa_native_rate_override_active,bank_aa_native_rate_demand_rad_s,bank_control_state,bank_capture_phase,hdg_mode_armed,hdg_target_deg,hdg_heading_error_deg,hdg_aa_native_yaw_rate_override_active,hdg_aa_native_yaw_rate_demand_deg_s,hdg_aa_native_yaw_rate_demand_rad_s,hdg_yaw_input_after_neutralization,avai_valid,avai_confidence,avai_bank_deg,avai_bank_wrapped_deg,avai_horizon_bank_deg,avai_horizon_bank_valid,avai_horizon_bank_confidence,avai_pitch_deg,avai_pitch_valid,avai_heading_deg,avai_heading_valid,avai_roll_rate_deg_s,avai_pitch_rate_deg_s,avai_yaw_rate_deg_s,avai_surface_speed_mps,avai_vertical_speed_mps,avai_dynamic_pressure_kpa,avai_static_pressure_kpa,avai_density_kg_m3,avai_g_force,final_pitch,final_roll,final_yaw,final_throttle,protect_risk,protect_active,assist_floor,required_thrust_kn,actual_thrust_kn,prop_provider,protect_dynamic_pressure_kpa,protect_speed_decel_active,protect_high_energy_decel_active,protect_intentional_decel_active,protect_decel_thrust_inhibit_active,protect_thrust_assist_inhibited_by_decel,alt_mode_armed,alt_control_active,alt_target_m,alt_current_m,alt_error_m,alt_vs_demand_mps,alt_max_vs_mps,alt_max_pitch_deg,alt_control_state,pitch_aafbw_external_rate_control_active,pitch_aafbw_moderation_envelope_available,pitch_aafbw_moderation_active,pitch_aafbw_requested_rate_deg_s,pitch_aafbw_applied_rate_deg_s,pitch_aafbw_moderation_delta_deg_s,pitch_aafbw_lower_rate_limit_deg_s,pitch_aafbw_upper_rate_limit_deg_s,pitch_aafbw_aoa_moderation_enabled,pitch_aafbw_g_moderation_enabled");
            WriteMetadata(vessel);
            nextSample = Time.realtimeSinceStartup;
            nextFlush = Time.realtimeSinceStartup + FlushIntervalSeconds;
            nextBankDiagnosticsSample = Time.realtimeSinceStartup;
            nextApSmoothnessSample = Time.realtimeSinceStartup;
            nextVsDiagnosticsSample = Time.realtimeSinceStartup;
            nextPitchDiagnosticsSample = Time.realtimeSinceStartup;
            nextHdgDiagnosticsSample = Time.realtimeSinceStartup;
            nextAltDiagnosticsSample = Time.realtimeSinceStartup;
            previousApSampleTime = 0f;
            previousApSpeed = 0f;
            previousApPitchCommand = 0f;
            previousApRollCommand = 0f;
            previousApYawCommand = 0f;
            previousApThrottleCommand = 0f;
            sampleCount = 0;
            eventCount = 0;
            maxSpeed = 0f;
            maxAoA = 0f;
            maxG = 0f;
            protectInterventions = 0;
            previousProtect = false;
            RecordCvr("FDR", "INFO", "flight recorder started; vessel=" + vesselName + "; sampling_hz=10");
        }

        internal void RecordExtensionEvent(string providerId, string category, string action, AERISRecorderSeverity severity, string message)
        {
            RecordCvr("EXT:" + SafeToken(providerId), severity.ToString().ToUpperInvariant(),
                "category=" + SafeToken(category) + "; action=" + SafeToken(action) + "; " + (message ?? string.Empty));
        }

        internal bool RegisterTelemetrySchema(AERISRecorderTelemetrySchema schema)
        {
            if (string.IsNullOrEmpty(schema.ProviderId) || string.IsNullOrEmpty(schema.ChannelId) ||
                schema.ProviderId.Length > 128 || schema.ChannelId.Length > 128 ||
                schema.Fields == null || schema.Fields.Length == 0 || schema.Fields.Length > 128 ||
                float.IsNaN(schema.RequestedHz) || float.IsInfinity(schema.RequestedHz)) return false;
            string[] fields = new string[schema.Fields.Length];
            var normalizedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "utc", "ut" };
            for (int i = 0; i < schema.Fields.Length; i++)
            {
                if (string.IsNullOrEmpty(schema.Fields[i]) || schema.Fields[i].Length > 128)
                    return false;
                string normalized = CsvHeader(schema.Fields[i]);
                if (!normalizedFields.Add(normalized)) return false;
                fields[i] = schema.Fields[i];
            }
            schema.Fields = fields;
            schema.RequestedHz = Mathf.Clamp(schema.RequestedHz, 0.2f, 50f);
            string key = Key(schema.ProviderId, schema.ChannelId);
            lock (sync)
            {
                AERISRecorderTelemetrySchema existing;
                if (schemas.TryGetValue(key, out existing))
                {
                    if (existing.Fields == null || existing.Fields.Length != schema.Fields.Length)
                        return false;
                    for (int i = 0; i < existing.Fields.Length; i++)
                        if (!string.Equals(existing.Fields[i], schema.Fields[i], StringComparison.Ordinal))
                            return false;
                    return true;
                }
                if (schemas.Count >= MaxExtensionTelemetryChannels) return false;
                schemas[key] = schema;
                return true;
            }
        }

        internal void RecordExtensionTelemetry(AERISRecorderTelemetryFrame frame)
        {
            if (frame.Vessel == null || string.IsNullOrEmpty(frame.ProviderId) || string.IsNullOrEmpty(frame.ChannelId) || frame.Values == null) return;
            if (vesselId == null || frame.Vessel.id.ToString() != vesselId) return;
            string key = Key(frame.ProviderId, frame.ChannelId);
            lock (sync)
            {
                if (disabledTelemetryChannels.Contains(key)) return;
                AERISRecorderTelemetrySchema schema;
                if (!schemas.TryGetValue(key, out schema) || schema.Fields == null || frame.Values.Length != schema.Fields.Length) return;
                try
                {
                    float now = Time.realtimeSinceStartup;
                    float next;
                    if (nextTelemetryWrite.TryGetValue(key, out next) && now < next) return;
                    nextTelemetryWrite[key] = now + 1f / Mathf.Max(0.2f, schema.RequestedHz);
                    AERISAsyncFileChannel writer;
                    if (!telemetryWriters.TryGetValue(key, out writer))
                    {
                        if (string.IsNullOrEmpty(folder)) return;
                        string name = "fdr_ext_" + SafeFile(schema.ProviderId) + "_" +
                            SafeFile(schema.ChannelId) + "_" + StableHash8(key) + ".csv";
                        writer = new AERISAsyncFileChannel(Path.Combine(folder, name), false,
                            AERISFileRecordPriority.Verbose);
                        var header = new AERISCsvField[schema.Fields.Length + 2];
                        header[0] = AERISCsvField.Raw("utc");
                        header[1] = AERISCsvField.Raw("ut");
                        for (int i = 0; i < schema.Fields.Length; i++)
                            header[i + 2] = AERISCsvField.Raw(CsvHeader(schema.Fields[i]));
                        writer.WriteCsv(header);
                        telemetryWriters[key] = writer;
                    }
                    var values = new AERISCsvField[frame.Values.Length + 2];
                    values[0] = Utc(DateTime.UtcNow);
                    values[1] = F(Planetarium.GetUniversalTime());
                    for (int i = 0; i < frame.Values.Length; i++)
                        values[i + 2] = Csv(frame.Values[i]);
                    writer.WriteCsv(values);
                }
                catch
                {
                    AERISAsyncFileChannel failed;
                    if (telemetryWriters.TryGetValue(key, out failed))
                    {
                        telemetryWriters.Remove(key);
                        CloseWriterBestEffort(failed);
                    }
                    disabledTelemetryChannels.Add(key);
                }
            }
        }

        internal void RecordCvr(string source, string level, string message)
        {
            lock (sync)
            {
                if (cvrWriter == null) return;
                double ut = Planetarium.GetUniversalTime();
                cvrWriter.WriteCsv(new AERISCsvField[] {
                    Utc(DateTime.UtcNow), F(ut), Csv(source),
                    Csv(level), Csv(message)
                });
                eventCount++;
            }
        }

        // Dedicated control-cadence BANK trace. This file is intentionally separate from the
        // 10 Hz general FDR so step motion and high-speed command chatter are not aliased away.
        internal void SampleBankDiagnostics(Vessel vessel, AERISBankDirector bank, VirtualAttitudeInstrument attitude)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || bank == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextBankDiagnosticsSample) return;
            nextBankDiagnosticsSample = now + 0.02f; // 50 Hz diagnostic stream

            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()), F(bank.DiagnosticControlDt),
                F((float)vessel.srfSpeed), F(attitude != null ? attitude.DynamicPressureKpa : 0f),
                F(attitude != null ? attitude.InstrumentHorizonBankDeg : 0f), F(bank.TargetBank), F(bank.BankError),
                F(bank.HorizonBankRawRateDegPerSec), F(bank.HorizonBankTrendRateDegPerSec), F(bank.HorizonBankTrendResidualDeg), F(bank.HorizonRateTrendWindowSeconds), F(bank.HorizonBankTrendSpanSeconds), bank.HorizonBankTrendSampleCount,
                F(bank.ActualRollRate), F(bank.RollRateRequest), F(bank.DiagnosticRateError), F(bank.DiagnosticTargetStick),
                F(bank.VirtualPilotRoll), F(bank.InjectedRoll), B(bank.AaNativeRollRateOverrideActive), F(bank.AaNativeRollRateDemandDegPerSec), F(bank.AaNativeRollRateDemandRadPerSec), F(bank.DiagnosticVirtualRollDelta), F(bank.DiagnosticVirtualRollSlewPerSec),
                bank.DiagnosticCommandSign, bank.DiagnosticCommandSignFlips1s,
                bank.DiagnosticRateSignFlips1s, bank.DiagnosticErrorSignFlips1s,
                F(bank.DiagnosticStepScore), F(bank.DiagnosticOscillationScore), B(bank.TerminalChatterSuppressed), F(bank.TerminalSlewScale), F(bank.TerminalCommandLockRemaining), B(bank.TransitionQuietingActive), F(bank.TransitionSlewScale), F(bank.TransitionCommandHoldRemaining), B(bank.TransitionUpdateGated), F(bank.TransitionUpdateInterval), F(bank.TransitionHeldTarget), bank.TransitionCommandUpdates1s, B(bank.TransitionDeltaSuppressed), F(bank.TransitionCommandDelta), F(bank.TransitionCommandDeadband), F(bank.TransitionRawRateRequest), F(bank.TransitionShapedRateRequest), F(bank.TransitionRateAccelLimit), B(bank.TransitionRateShaperActive), B(bank.TransitionZeroCaptureActive), F(bank.TransitionZeroCaptureRate), B(bank.TransitionRateFeedbackDeadbandActive), F(bank.TransitionRateFeedbackDeadbandDegPerSec), F(bank.EffectiveRollRateForControl), F(bank.DynamicPressureKpa), F(bank.DynamicPressureSchedule), F(bank.DynamicPressureHighQSchedule), Csv(bank.DynamicPressureMode), F(bank.DynamicPressureRateScale), F(bank.LimitedRollRateRequest), B(bank.TerminalLatched), F(bank.DiagnosticSettleQuietElapsed), B(bank.SettleBrakeReentryPending), F(bank.SettleBrakeReentryElapsed), B(bank.SettleBrakeReentryForced), B(bank.TrajectoryHoldLatched), F(bank.TrajectoryHoldQuietElapsed), F(bank.TrajectoryHoldEntryBandDeg), F(bank.TrajectoryHoldExitBandDeg), F(bank.TrajectoryStoppingRateLimit), F(bank.TrajectoryRateError), F(bank.TrajectoryTerminalBlend), F(bank.TrajectoryScheduledDecel),
                B(bank.PrecisionAltitudeEligible), F(bank.PrecisionAltitudeMeters), B(bank.PrecisionHoldActive), B(bank.PrecisionCorrectionActive), B(bank.PrecisionWithinTarget), F(bank.PrecisionWithinTargetElapsed), F(bank.PrecisionTargetToleranceDeg), F(bank.PrecisionNeutralBandDeg), F(bank.PrecisionRateCommandDegPerSec), F(bank.PrecisionRateLimitDegPerSec), F(bank.PrecisionRateGainPerSec), F(bank.PrecisionRateDamping),
                Csv(bank.ControlState), Csv(bank.CapturePhase)
            });
            lock (sync)
            {
                if (bankDiagnosticsWriter == null) return;
                bankDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) bankDiagnosticsWriter.Flush();
            }
        }


        // Dedicated HDG trace. It records only AERIS lateral-director intent and the
        // virtual yaw/roll handoff before AA; AA internals and final control law remain untouched.
        internal void SampleHeadingDiagnostics(Vessel vessel, AERISHdgDirector hdg, AERISBankDirector bank, VirtualAttitudeInstrument attitude)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || hdg == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextHdgDiagnosticsSample) return;
            nextHdgDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()), B(hdg.Armed), Csv(hdg.ControlState),
                F(hdg.TargetHeading), F(hdg.CurrentHeading), F(hdg.HeadingError), F(hdg.RawBankTarget), F(hdg.CommandedBankTarget),
                F(hdg.AutoMaxBankLimitDeg), F(hdg.EffectiveMaxBankLimitDeg),
                B(hdg.SafeLowSpeedBankAuthorityActive), B(hdg.SafeLowSpeedBankCapabilitySampleActive),
                F(hdg.SafeLowSpeedBankObservedG), F(hdg.SafeLowSpeedBankMeasuredMaximumDeg),
                F(hdg.SafeLowSpeedBankCapabilityLimitDeg),
                F(hdg.SafeLowSpeedBankAuthorityLimitDeg), F(hdg.SafeLowSpeedBankAuthorityBlend),
                F(hdg.SafeLowSpeedBankSpeedBlend), F(hdg.SafeLowSpeedBankQBlend),
                F(hdg.SafeLowSpeedBankStallBlend), F(hdg.SafeLowSpeedBankAltitudeBlend),
                Csv(hdg.SafeLowSpeedBankAuthorityReason), F(hdg.RolloutStartErrorDeg), B(hdg.RolloutHoldActive),
                F(bank != null ? bank.TargetBank : 0f), F(attitude != null ? attitude.InstrumentHorizonBankDeg : 0f), F(bank != null ? bank.BankError : 0f), F(bank != null ? bank.ActualRollRate : 0f), F(bank != null ? bank.AaNativeRollRateDemandDegPerSec : 0f), B(bank != null && bank.AaNativeRollRateOverrideActive),
                B(hdg.TerminalYawActive), Csv(hdg.TerminalYawCaptureBand), F(hdg.TerminalYawRawCommand), F(hdg.TerminalYawCommand), F(hdg.TerminalYawProportionalTerm), F(hdg.TerminalYawRateDampingTerm),
                F(hdg.CoordinatedYawCommand), F(hdg.CoordinatedYawFeedForward), F(hdg.CoordinatedYawRateCorrection), F(hdg.VirtualYawCommand),
                F(hdg.YawRateActualDegPerSec), F(hdg.TerminalYawRateRawDegPerSec), F(hdg.TerminalYawRateCommandDegPerSec), F(hdg.TerminalYawRateProportionalTermDegPerSec), F(hdg.TerminalYawRateDampingTermDegPerSec), F(hdg.CoordinatedYawRateTargetDegPerSec), F(hdg.CoordinatedYawRateCommandDegPerSec), F(hdg.YawRateRequestDegPerSec), B(hdg.AaNativeYawRateOverrideActive), F(hdg.AaNativeYawRateDemandDegPerSec), F(hdg.AaNativeYawRateDemandRadPerSec), F(hdg.YawInputAfterNeutralization), F(StandardFlyByWire.LastFinalYaw),
                B(hdg.TerminalRollAssistActive), F(hdg.TerminalRollAssistRawDeg), F(hdg.TerminalRollAssistFilteredDeg), F(hdg.TerminalRollAssistDeg), B(hdg.TerminalRollAssistHoldActive), B(hdg.TerminalRollAssistReversePending), B(hdg.TerminalBankQuietZoneActive),
                B(hdg.ThinAirTurnAssistEnabled), B(hdg.ThinAirTurnAssistActive), B(hdg.ThinAirTurnLatched), Csv(hdg.ThinAirTurnPhase), F(hdg.ThinAirBlend),
                F(hdg.ThinAirTurnAssistBlend), F(hdg.ThinAirTurnResponseRatio),
                F(hdg.ThinAirTurnWeakResponseElapsedSeconds), F(hdg.ThinAirTurnBankTargetDeg),
                F(hdg.ThinAirTurnBankSpeedBlend), F(hdg.ThinAirTurnPitchAssistRateDegPerSec),
                F(hdg.ThinAirTurnPitchKinematicRateDegPerSec), F(hdg.ThinAirTurnPitchFloorRateDegPerSec),
                F(hdg.ThinAirTurnPitchFeedbackRateDegPerSec), F(hdg.ThinAirTurnRolloutLeadDeg),
                F(hdg.ThinAirTurnTargetG),
                F(hdg.ThinAirTurnCommandedG), F(hdg.ThinAirTurnMeasuredG),
                F(hdg.ThinAirTurnStabilityScore), F(hdg.ThinAirTurnStallAuthority),
                F(hdg.ThinAirTurnTrackingAuthority), F(hdg.ThinAirTurnEntryElapsedSeconds),
                F(hdg.ThinAirTurnLatchedElapsedSeconds), F(hdg.ThinAirTurnReleaseElapsedSeconds),
                Csv(hdg.ThinAirTurnReleaseReason),
                F(hdg.ThinAirTurnObservedAltitudeMeters), F(hdg.ThinAirTurnObservedSurfaceSpeedMps),
                F(hdg.ThinAirTurnObservedStallMarginDeg), F(hdg.ThinAirTurnObservedHeadingRateDegPerSec),
                B(hdg.ThinAirTurnAltitudeQualified),
                B(hdg.ThinAirTurnSpeedQualified), B(hdg.ThinAirTurnStallMarginQualified),
                B(hdg.ThinAirTurnHeadingErrorQualified), Csv(hdg.ThinAirTurnQualificationStatus),
                B(hdg.ThinAirAaLimitHoldActive), Csv(hdg.ThinAirAaLimitHoldReason),
                F(hdg.ThinAirAaLimitHoldElapsedSeconds), F(hdg.ThinAirAaLimitRecoveryElapsedSeconds),
                F(hdg.ThinAirCriticalConditionElapsedSeconds), F(hdg.ThinAirAaPitchRequestedDegPerSec),
                F(hdg.ThinAirAaPitchAppliedDegPerSec), F(hdg.ThinAirAaPitchModerationDeltaDegPerSec),
                F(hdg.ThinAirAaPitchAuthority), F(hdg.ThinAirAaLimitGCap),
                F(hdg.ThinAirAaLimitPitchCapDegPerSec), B(hdg.ThinAirMarginGovernorActive),
                Csv(hdg.ThinAirMarginGovernorReason), F(hdg.ThinAirStallMarginRateDegPerSec),
                F(hdg.ThinAirPredictedStallMarginDeg), F(hdg.ThinAirMarginGovernorAuthority),
                F(hdg.ThinAirMarginRecoveryElapsedSeconds), F(hdg.ThinAirEstimatedSustainableG),
                F(hdg.ThinAirCapabilityGCap), F(hdg.ThinAirCapabilityTrackingErrorG),
                B(hdg.ThinAirCapabilityLimited), F(hdg.ThinAirCapabilityBankCapDeg),
                F(hdg.ThinAirBankTargetSlewLimitDegPerSec),
                F(hdg.ThinAirLowQEnvelopeBlend), F(hdg.ThinAirLowQBankCapDeg),
                F(hdg.ThinAirLowQGCap), F(hdg.ThinAirLowQPitchCapDegPerSec),
                B(hdg.ThinAirStallRecoveryActive), F(hdg.ThinAirTurnYawBankFade),
                F(hdg.ThinAirTurnYawRateTargetDegPerSec), F(hdg.AttitudeStabilityYawRateTargetDegPerSec),
                F(hdg.AttitudeStabilityYawRateCommandDegPerSec), F(hdg.AttitudeStabilityYawSideslipTermDegPerSec),
                F(hdg.AttitudeStabilityYawRateDampingTermDegPerSec),
                F(hdg.AttitudeStabilityYawAccelerationDampingTermDegPerSec), Csv(hdg.YawAssistMode)
            });
            lock (sync)
            {
                if (hdgDiagnosticsWriter == null) return;
                hdgDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) hdgDiagnosticsWriter.Flush();
            }
        }

        // Common AP smoothness trace. It is intentionally observation-only and records every
        // AERIS-owned axis at control cadence; currently BANK owns roll, while later modes can
        // set the other ownership flags without changing this recorder schema.
        internal void SampleApSmoothness(Vessel vessel, AERISBankDirector bank, AERISPitchDirector pitch, AERISHdgDirector hdg, AERISAltitudeDirector alt, AERISAccelerationDirector acc, AERISVelocityDirector vel, VirtualAttitudeInstrument attitude, ProtectTelemetry protect, AERISAxisStabilitySupervisor axisSupervisor, FlightCtrlState state)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || state == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextApSmoothnessSample) return;
            nextApSmoothnessSample = now + 0.02f;
            float dt = previousApSampleTime > 0f ? Mathf.Max(0.001f, now - previousApSampleTime) : 0.02f;
            float speed = (float)vessel.srfSpeed;
            float accel = previousApSampleTime > 0f ? (speed - previousApSpeed) / dt : 0f;
            // PITCH v0.4.91 no longer writes a virtual stick into state.pitch. Keep its
            // legacy normalized output as a shadow, and record the actual neutralized pitch
            // input plus the native AA rate demand separately.
            float vp = pitch != null ? pitch.VirtualPilotPitch : state.pitch;
            float pitchAfterNeutralization = pitch != null ? pitch.PitchInputAfterNeutralization : state.pitch;
            float vr = state.roll;
            float vy = state.yaw;
            float vt = state.mainThrottle;
            float dp = vp - previousApPitchCommand;
            float dr = vr - previousApRollCommand;
            float dy = vy - previousApYawCommand;
            float dtc = vt - previousApThrottleCommand;
            bool rollActive = bank != null && bank.Armed;
            bool pitchActive = pitch != null && pitch.AaNativePitchRateOverrideActive;
            bool yawActive = hdg != null && hdg.AaNativeYawRateOverrideActive;
            bool throttleActive = acc != null && acc.AaNativeThrottleOverrideActive;
            bool activeAny = rollActive || pitchActive || yawActive || throttleActive;
            if (!activeAny) return;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()), B(activeAny), B(rollActive), B(pitchActive), B(yawActive), B(throttleActive),
                Csv(hdg != null && hdg.Armed ? "HDG" : (rollActive ? "BANK" : "NONE")), Csv(alt != null && alt.Armed ? "ALT" : (pitch != null && pitch.VerticalSpeedDirectRateActive ? "V/S" : (pitch != null && pitch.Armed ? "PITCH" : "NONE"))), Csv(throttleActive ? (vel != null && vel.Armed ? "VEL" : "ACC") : "NONE"), F(dt), F(speed), F(previousApSampleTime > 0f ? speed - previousApSpeed : 0f), F(accel),
                F(attitude != null ? attitude.VerticalSpeedMps : 0f), F(attitude != null ? attitude.DynamicPressureKpa : 0f), F((float)vessel.geeForce),
                F(protect != null ? protect.AoADegrees : 0f), F(protect != null ? protect.SideslipDegrees : 0f), F(attitude != null ? attitude.InstrumentHorizonBankDeg : 0f), F(attitude != null ? attitude.InstrumentPitchDeg : 0f), F(attitude != null ? attitude.InstrumentHeadingDeg : 0f),
                F(attitude != null ? attitude.InstrumentRollRateDegPerSec : 0f), F(attitude != null ? attitude.InstrumentPitchRateDegPerSec : 0f), F(attitude != null ? attitude.InstrumentYawRateDegPerSec : 0f),
                F(vp), F(pitchAfterNeutralization), B(pitch != null && pitch.AaNativePitchRateOverrideActive), F(pitch != null ? pitch.AaNativePitchRateDemandDegPerSec : 0f), F(pitch != null ? pitch.AaNativePitchRateDemandRadPerSec : 0f), F(vr), F(vy), F(vt), F(hdg != null ? hdg.VirtualYawCommand : 0f), B(hdg != null && hdg.AaNativeYawRateOverrideActive), F(hdg != null ? hdg.AaNativeYawRateDemandDegPerSec : 0f), F(hdg != null ? hdg.AaNativeYawRateDemandRadPerSec : 0f), F(StandardFlyByWire.LastFinalPitch), F(StandardFlyByWire.LastFinalRoll), F(StandardFlyByWire.LastFinalYaw), F(StandardFlyByWire.LastFinalThrottle),
                F(dp), F(dr), F(dy), F(dtc), F(dp / dt), F(dr / dt), F(dy / dt), F(dtc / dt),
                F(bank != null ? bank.TargetBank : 0f), F(bank != null ? bank.BankError : 0f), F(bank != null ? bank.RollRateRequest : 0f), F(bank != null ? bank.DynamicPressureKpa : 0f), F(bank != null ? bank.DynamicPressureSchedule : 0f), F(bank != null ? bank.DynamicPressureHighQSchedule : 0f), Csv(bank != null ? bank.DynamicPressureMode : "NONE"), F(bank != null ? bank.DynamicPressureRateScale : 0f), F(bank != null ? bank.LimitedRollRateRequest : 0f), F(bank != null ? bank.DiagnosticStepScore : 0f), F(bank != null ? bank.DiagnosticOscillationScore : 0f),
                B(bank != null && bank.TerminalChatterSuppressed), F(bank != null ? bank.TerminalSlewScale : 1f), F(bank != null ? bank.TerminalCommandLockRemaining : 0f), B(bank != null && bank.TransitionQuietingActive), F(bank != null ? bank.TransitionSlewScale : 1f), F(bank != null ? bank.TransitionCommandHoldRemaining : 0f), B(bank != null && bank.TransitionUpdateGated), F(bank != null ? bank.TransitionUpdateInterval : 0f), F(bank != null ? bank.TransitionHeldTarget : 0f), (bank != null ? bank.TransitionCommandUpdates1s : 0), B(bank != null && bank.TransitionDeltaSuppressed), F(bank != null ? bank.TransitionCommandDelta : 0f), F(bank != null ? bank.TransitionCommandDeadband : 0f), F(bank != null ? bank.TransitionRawRateRequest : 0f), F(bank != null ? bank.TransitionShapedRateRequest : 0f), F(bank != null ? bank.TransitionRateAccelLimit : 0f), B(bank != null && bank.TransitionRateShaperActive), B(bank != null && bank.TransitionZeroCaptureActive), F(bank != null ? bank.TransitionZeroCaptureRate : 0f), B(bank != null && bank.TransitionRateFeedbackDeadbandActive), F(bank != null ? bank.TransitionRateFeedbackDeadbandDegPerSec : 0f), F(bank != null ? bank.EffectiveRollRateForControl : 0f), F(StandardFlyByWire.LastFinalRoll - vr), F(StandardFlyByWire.LastFinalRoll - previousApRollCommand), F((StandardFlyByWire.LastFinalRoll - previousApRollCommand) / dt), 0, 0, F(StandardFlyByWire.LastFinalRoll), F(StandardFlyByWire.LastFinalRoll), F(0f), B(bank != null && bank.AaNativeRollRateOverrideActive), F(bank != null ? bank.AaNativeRollRateDemandDegPerSec : 0f), F(bank != null ? bank.AaNativeRollRateDemandRadPerSec : 0f), B(bank != null && bank.PrecisionAltitudeEligible), B(bank != null && bank.PrecisionHoldActive), B(bank != null && bank.PrecisionCorrectionActive), B(bank != null && bank.PrecisionWithinTarget), F(bank != null ? bank.PrecisionTargetToleranceDeg : 0f), F(bank != null ? bank.PrecisionRateCommandDegPerSec : 0f),
                Csv(axisSupervisor != null ? axisSupervisor.RollState : "UNAVAILABLE"), (axisSupervisor != null ? axisSupervisor.RollReversals : 0),
                Csv(axisSupervisor != null ? axisSupervisor.PitchState : "UNAVAILABLE"), (axisSupervisor != null ? axisSupervisor.PitchReversals : 0),
                Csv(axisSupervisor != null ? axisSupervisor.YawState : "UNAVAILABLE"), (axisSupervisor != null ? axisSupervisor.YawReversals : 0),
                B(StandardFlyByWire.LastPitchRateExternalControlActive), B(StandardFlyByWire.LastPitchRateModerationEnvelopeAvailable),
                B(StandardFlyByWire.LastPitchRateModerationActive), F(StandardFlyByWire.LastPitchRateRequestedRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateAppliedRadPerSec * Mathf.Rad2Deg), F(StandardFlyByWire.LastPitchRateModerationDeltaRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateLowerLimitRadPerSec * Mathf.Rad2Deg), F(StandardFlyByWire.LastPitchRateUpperLimitRadPerSec * Mathf.Rad2Deg),
                B(StandardFlyByWire.LastPitchRateAoAModerationEnabled), B(StandardFlyByWire.LastPitchRateGModerationEnabled)
            });
            lock (sync)
            {
                if (apSmoothnessWriter == null) return;
                apSmoothnessWriter.WriteCsv(line);
                if (now >= nextFlush) apSmoothnessWriter.Flush();
            }
            previousApSampleTime = now; previousApSpeed = speed; previousApPitchCommand = vp; previousApRollCommand = vr; previousApYawCommand = vy; previousApThrottleCommand = vt;
        }

        // Dedicated PITCH trace. It makes the native pitch-rate transport and the
        // retained outer PITCH/V/S target law independently inspectable at 50 Hz.
        internal void SamplePitchDiagnostics(Vessel vessel, AERISPitchDirector pitch)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || pitch == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextPitchDiagnosticsSample) return;
            nextPitchDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()), B(pitch.Armed), Csv(pitch.ControlState),
                F(pitch.TargetPitch), F(pitch.CurrentPitch), F(pitch.PitchError), F(pitch.ActualPitchRate),
                B(pitch.TargetPreservedOnArm), F(pitch.ArmedTargetPitchSnapshotDeg), Csv(pitch.ArmedTargetPitchTextSnapshot), F(pitch.VirtualPilotPitch),
                F(pitch.RawPilotPitch), F(pitch.PitchInputAfterNeutralization), F(pitch.PitchRateRequestDegPerSec),
                B(pitch.AaNativePitchRateOverrideActive), F(pitch.AaNativePitchRateDemandDegPerSec),
                F(pitch.AaNativePitchRateDemandRadPerSec), B(pitch.VerticalSpeedDirectRateActive), F(pitch.VerticalSpeedDirectRateDemandDegPerSec), F(StandardFlyByWire.LastFinalPitch),
                B(StandardFlyByWire.LastPitchRateExternalControlActive), B(StandardFlyByWire.LastPitchRateModerationEnvelopeAvailable),
                B(StandardFlyByWire.LastPitchRateModerationActive), F(StandardFlyByWire.LastPitchRateRequestedRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateAppliedRadPerSec * Mathf.Rad2Deg), F(StandardFlyByWire.LastPitchRateModerationDeltaRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateLowerLimitRadPerSec * Mathf.Rad2Deg), F(StandardFlyByWire.LastPitchRateUpperLimitRadPerSec * Mathf.Rad2Deg),
                B(StandardFlyByWire.LastPitchRateAoAModerationEnabled), B(StandardFlyByWire.LastPitchRateGModerationEnabled),
                B(pitch.LateralTurnAssistActive), F(pitch.LateralTurnAssistRateDegPerSec),
                B(pitch.LateralTurnPitchPriorityActive), F(pitch.LateralTurnPitchPriorityFloorDegPerSec),
                F(pitch.LateralTurnBaseRateDegPerSec), F(pitch.LateralTurnSuppressedOpposingRateDegPerSec)
            });
            lock (sync)
            {
                if (pitchDiagnosticsWriter == null) return;
                pitchDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) pitchDiagnosticsWriter.Flush();
            }
        }

        // Dedicated V/S trace. Keeps V/S tuning independent from the legacy AP smoothness schema.
        internal void SampleVerticalSpeedDiagnostics(Vessel vessel, AERISVerticalSpeedDirector vs, AERISPitchDirector pitch)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || vs == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextVsDiagnosticsSample) return;
            nextVsDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()), B(vs.Armed), B(vs.ControlActive), B(vs.VerticalSpeedErrorValid),
                F(vs.RequestedTargetVerticalSpeedMps), F(vs.EffectiveTargetVerticalSpeedMps), F(vs.ControlTargetVerticalSpeedMps), F(vs.TargetVerticalSpeedMps), F(vs.CurrentVerticalSpeedMps), F(vs.VerticalSpeedErrorMps), F(vs.ControlVerticalSpeedErrorMps), F(vs.VerticalAccelerationMps2),
                F(vs.PredictedVerticalSpeedMps), F(vs.PredictedStopErrorMps), F(vs.ProportionalContributionDeg), F(vs.DampingContributionDeg),
                F(vs.BrakeContributionDeg), F(vs.RecoveryContributionDeg), F(vs.VerticalSpeedTrimDeg), F(vs.PrecisionTrimContributionDeg),
                F(vs.VerticalSpeedBasePitchDeg), F(vs.BasePitchAdaptContributionDeg), F(vs.BasePitchSpeedAdaptContributionDeg), B(vs.BasePitchSpeedAdaptActive), B(vs.PrecisionBasePitchActive), B(vs.PrecisionWithinTarget), F(vs.PrecisionWithinTargetElapsedSeconds), F(vs.PrecisionTargetToleranceMps), F(vs.PrecisionNeutralBandMps), F(vs.PrecisionBasePitchRateDegPerSec), F(vs.PrecisionBasePitchAdaptContributionDeg), F(vs.PrecisionTrimTransferDeg), F(vs.PrecisionEntryElapsedSeconds), F(vs.PrecisionExitElapsedSeconds), F(vs.PrecisionHoldEntryElapsedSeconds), F(vs.PrecisionHoldExitElapsedSeconds), Csv(vs.PrecisionPhase), vs.PrecisionPhaseTransitions, B(vs.PrecisionCapturePhase), F(vs.PrecisionActiveRateGainDegPerMpsSec), F(vs.PrecisionActiveAccelerationDampingDegPerMps2Sec), F(vs.PrecisionActiveRateLimitDegPerSec), F(vs.BasePitchSpeedAdaptRateDegPerSec), F(vs.PrecisionNetBasePitchRateDegPerSec), F(vs.SurfaceSpeedMps), F(vs.SurfaceSpeedRateMps2), F(vs.DesiredPitchBeforeClampDeg), F(vs.DesiredPitchAfterClampDeg), B(vs.PitchTargetSaturated), B(vs.PitchUpperSaturated), B(vs.PitchLowerSaturated),
                F(vs.GeneratedPitchTargetDeg), F(vs.PlannedPitchRateDegPerSec), B(vs.DirectPitchRateActive), F(vs.VsRateProportionalDegPerSec), F(vs.VsRateDampingDegPerSec), F(vs.VsRateBrakeDegPerSec), F(vs.VsBasePitchHoldRateDegPerSec), F(vs.VsAttitudeErrorDeg), F(vs.VsAttitudeRateProportionalDegPerSec), F(vs.VsAttitudeRateDampingDegPerSec), F(vs.VsRateTargetDegPerSec), F(vs.VsRateCommandSlewDegPerSec2), Csv(vs.DirectRateScheme), F(vs.MaxPitchTargetDeg), B(vs.AltitudePitchLimitActive), F(vs.AltitudePitchLimitDeg), B(vs.AltitudePrecisionHoldActive), F(vs.EffectiveVerticalSpeedDeadbandMps), F(vs.ErrorReversalBandMps), F(vs.EffectiveMaxPitchTargetDeg), B(vs.OvershootLatchActive), Csv(vs.BrakeState),
                B(pitch != null && pitch.Armed), F(pitch != null ? pitch.TargetPitch : 0f), F(pitch != null ? pitch.CurrentPitch : 0f), F(pitch != null ? pitch.PitchError : 0f), F(pitch != null ? pitch.PitchRateRequestDegPerSec : 0f), B(pitch != null && pitch.AaNativePitchRateOverrideActive), F(pitch != null ? pitch.AaNativePitchRateDemandDegPerSec : 0f), F(pitch != null ? pitch.AaNativePitchRateDemandRadPerSec : 0f),
                F(vs.DynamicPressureKpa), F(vs.DynamicPressureHighQSchedule), Csv(vs.DynamicPressureMode), B(vs.HighQManualZeroVsProfileActive), F(vs.HighQManualZeroVsBlend),
                B(vs.HighQManualZeroVsCaptureGuardActive), F(vs.HighQManualZeroVsCaptureGuardBlend),
                B(vs.ManualZeroVsTransitionGuardActive), F(vs.ManualZeroVsTransitionGuardBlend), F(vs.ManualZeroVsTransitionGuardRemainingSeconds), F(vs.ManualZeroVsTransitionGuardFromMps), F(vs.ManualZeroVsTransitionGuardPressureBlend),
                B(vs.ManualZeroVsTrajectoryBrakeActive), F(vs.ManualZeroVsTrajectoryTargetMps), F(vs.ManualZeroVsTrajectoryScheduledDecelMps2), F(vs.ManualZeroVsTrajectoryAppliedDecelMps2), F(vs.ManualZeroVsTrajectoryPressureBlend), F(vs.ManualZeroVsTrajectoryInitialMps), F(vs.ManualZeroVsTrajectoryElapsedSeconds), Csv(vs.ManualZeroVsTrajectoryState),
                B(vs.HighQNonZeroVsPrecisionCaptureProfileActive), F(vs.HighQNonZeroVsPrecisionCaptureBlend), F(vs.HighQNonZeroVsPrecisionFilteredAccelerationMps2), F(vs.HighQNonZeroVsPrecisionDampingScale), F(vs.HighQNonZeroVsPrecisionDampingLimitDeg), F(vs.HighQNonZeroVsPrecisionBasePitchDampingScale),
                B(vs.HighQNonZeroVsTrackingProfileActive), F(vs.HighQNonZeroVsTrackingBlend), F(vs.HighQNonZeroVsTrackingFilteredAccelerationMps2), F(vs.HighQNonZeroVsTrackingDampingScale), F(vs.HighQNonZeroVsTrackingDampingLimitDeg), F(vs.HighQNonZeroVsTrackingPitchSlewScale), F(vs.HighQNonZeroVsTrackingDirectRateScale), F(vs.HighQNonZeroVsTrackingRateCommandSlewScale), F(vs.HighQNonZeroVsTrackingBasePitchDampingScale),
                F(vs.EffectiveErrorReversalBandMps), F(vs.HighQProportionalScale), F(vs.HighQDampingScale), F(vs.HighQDampingLimitDeg), F(vs.HighQPitchSlewScale), F(vs.HighQAppliedPitchSlewDegPerSec),
                B(vs.LowQVerticalEnvelopeActive), F(vs.LowQVerticalEnvelopeBlend),
                F(vs.LowQVerticalEnvelopeAppliedBlend), F(vs.LowQFilteredAccelerationMps2),
                F(vs.LowQEffectiveMaxPitchTargetDeg), F(vs.LowQProportionalScale), F(vs.LowQDampingScale),
                F(vs.LowQDampingLimitDeg), F(vs.LowQPitchSlewScale), F(vs.LowQDirectRateScale),
                F(vs.LowQRateCommandSlewScale), F(vs.LowQBasePitchAdaptScale),
                B(vs.MidQVerticalTrackingFilterActive), F(vs.MidQVerticalTrackingBlend), F(vs.MidQFilteredAccelerationMps2),
                F(vs.MidQProportionalScale), F(vs.MidQDampingScale), F(vs.MidQDampingLimitDeg),
                F(vs.MidQPitchSlewScale), F(vs.MidQDirectRateScale), F(vs.MidQRateCommandSlewScale), F(vs.MidQBasePitchDampingScale),
                B(vs.VerticalTrackingRateEnvelopeActive), F(vs.VerticalTrackingRateEnvelopeBlend), F(vs.VerticalTrackingFilteredAccelerationMps2),
                F(vs.VerticalTrackingPitchSlewScale), F(vs.VerticalTrackingAttitudeRateDampingScale), F(vs.VerticalTrackingRateLimitDegPerSec),
                F(vs.VerticalTrackingRateSlewDegPerSec2), B(vs.VerticalTrackingRateReversalGateActive), F(vs.VerticalTrackingDampingDominanceLimitDeg),
                B(vs.AltitudePrecisionTrackingActive), F(vs.AltitudePrecisionTrackingEnterElapsedSeconds),
                F(vs.AltitudePrecisionTrackingExitElapsedSeconds), B(vs.AltitudeLowQPrecisionQuietingActive),
                F(vs.AltitudeLowQPrecisionQuietingBlend), F(vs.AltitudeLowQPrecisionRateAuthorityRecoveryBlend),
                F(vs.AltitudeLowQPrecisionQuietingRateScale),
                F(vs.AltitudeLowQPrecisionQuietingDampingScale), F(vs.AltitudeLowQPrecisionEffectiveRateLimitDegPerSec)
            });
            lock (sync)
            {
                if (vsDiagnosticsWriter == null) return;
                vsDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) vsDiagnosticsWriter.Flush();
            }
            WriteVsCruiseAccelerationGuideDiagnostics(vs, now);
        }

        internal void WriteVsCruiseAccelerationGuideDiagnostics(AERISVerticalSpeedDirector vs, float now)
        {
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()),
                B(vs.VsCruiseAccelerationGuideActive),
                F(vs.VsCruiseAccelerationGuideBlend), F(vs.ControlVerticalSpeedErrorMps),
                F(vs.VerticalAccelerationMps2), F(vs.VsCruiseDesiredVerticalAccelerationMps2),
                F(vs.VsCruiseAccelerationErrorMps2), F(vs.VsCruiseBasePitchRateCommandDegPerSec),
                F(vs.VsCruiseLegacyBasePitchRateDegPerSec), F(vs.VsCruiseAppliedBasePitchRateDegPerSec),
                B(vs.VsCruisePreBrakeActive), Csv(vs.PrecisionPhase), F(vs.LowQVerticalEnvelopeBlend),
                B(StandardFlyByWire.LastPitchRateModerationActive),
                F(StandardFlyByWire.LastPitchRateRequestedRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateAppliedRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateModerationDeltaRadPerSec * Mathf.Rad2Deg)
            });
            lock (sync)
            {
                if (vsCruiseAccelerationGuideWriter == null) return;
                vsCruiseAccelerationGuideWriter.WriteCsv(line);
                if (now >= nextFlush) vsCruiseAccelerationGuideWriter.Flush();
            }
        }

        // Dedicated SPEED/ACC trace. It records AERIS target generation, requested
        // throttle transport, AA's final throttle, and q scheduling without changing AA's
        // final FlightCtrlState write. VEL feeds ACC rather than creating a second throttle path.
        internal void SampleAccelerationDiagnostics(Vessel vessel, AERISAccelerationDirector acc,
            AERISSpeedAirbrakeController airbrake, VirtualAttitudeInstrument attitude)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || acc == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextAccelerationDiagnosticsSample) return;
            nextAccelerationDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()),
                B(acc.Armed), B(acc.ControlActive), B(acc.AccelerationErrorValid), Csv(acc.ControlState),
                F(acc.TargetAccelerationMps2), F(acc.CurrentSurfaceSpeedMps), F(acc.MeasuredAccelerationMps2),
                F(acc.FilteredAccelerationMps2), F(acc.AccelerationErrorMps2), F(acc.EffectiveAccelerationErrorMps2),
                F(acc.EffectiveAccelerationDeadbandMps2), B(acc.VelocityPlannerPrecisionActive),
                F(acc.VelocityPlannerThrottleBias), F(acc.VelocityPlannerThrottleBiasAdaptation),
                F(acc.VelocityPlannerThrottleBiasLimit), B(acc.VelocityPlannerCoastAuthorityActive),
                B(acc.VelocityPlannerBiasAtLimit), F(acc.BaseThrottle), F(acc.BaseThrottleAdaptation),
                B(acc.ZeroAccelerationFineTrimActive), F(acc.ZeroAccelerationFineTrimAdaptation), F(acc.ZeroAccelerationFineTrimErrorMps2),
                F(acc.ThrottleCorrection), F(acc.RawThrottleDemand), F(acc.ThrottleDemand), F(acc.AppliedThrottleSlewPerSec),
                F(acc.DynamicPressureKpa), F(acc.DynamicPressureCorrectionScale),
                B(acc.ThrustSaturated), B(acc.CoastLimited), Csv(acc.AccelerationLimitState), F(acc.AccelerationLimitElapsedSeconds),
                F(acc.ThrustSaturatedElapsedSeconds), F(acc.CoastLimitedElapsedSeconds),
                B(acc.AaNativeThrottleOverrideActive), F(acc.AaNativeThrottleDemand),
                F(StandardFlyByWire.LastFinalThrottle), F(acc.AirbrakeDemand),
                F(acc.AirbrakeDecelerationShortfallMps2), B(airbrake != null && airbrake.Enabled),
                B(airbrake != null && airbrake.Active), F(airbrake != null ? airbrake.RequestedDemand : 0f),
                F(airbrake != null ? airbrake.AppliedDemand : 0f), F(airbrake != null ? airbrake.DynamicPressureLimitScale : 0f),
                F(airbrake != null ? airbrake.AppliedAngleDegrees : 0f),
                (airbrake != null ? airbrake.EligibleSurfaceCount : 0),
                Csv(airbrake != null ? airbrake.Status : "UNAVAILABLE")
            });
            lock (sync)
            {
                if (accelerationDiagnosticsWriter == null) return;
                accelerationDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) accelerationDiagnosticsWriter.Flush();
            }
        }

        // Dedicated SPEED/VEL trace. VEL is strictly an upper planner; this log makes
        // the VEL->ACC handoff visible without adding a second throttle path.
        internal void SampleVelocityDiagnostics(Vessel vessel, AERISVelocityDirector vel,
            AERISAccelerationDirector acc, VirtualAttitudeInstrument attitude)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || vel == null || acc == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextVelocityDiagnosticsSample) return;
            nextVelocityDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()),
                B(vel.Armed), B(vel.TargetConfirmed), B(vel.ControlActive), B(vel.VelocityErrorValid), Csv(vel.ControlState),
                F(vel.TargetSurfaceSpeedMps), F(vel.CurrentSurfaceSpeedMps), F(vel.VelocityErrorMps),
                F(vel.PredictedVelocityErrorMps), F(vel.MeasuredAccelerationMps2), F(vel.ProjectedStoppingSpeedLeadMps),
                F(vel.AccelerationTrackingLeadMps), F(vel.AccelerationTrackingLeadSeconds),
                F(vel.DesiredAccelerationMps2), F(vel.PlannedAccelerationMps2), F(vel.PublishedAccelerationMps2),
                B(vel.VelocityHoldActive), F(vel.DynamicPressureKpa), F(vel.DynamicPressurePlannerScale),
                F(vel.ConfiguredAccelerationLimitMps2), F(vel.EffectiveMaxAccelerationMps2), F(vel.EffectiveMaxDecelerationMps2), F(vel.EffectiveJerkLimitMps3),
                F(acc.TargetAccelerationMps2), F(acc.FilteredAccelerationMps2), F(acc.AccelerationErrorMps2),
                F(acc.EffectiveAccelerationDeadbandMps2), B(acc.VelocityPlannerPrecisionActive),
                F(acc.VelocityPlannerThrottleBiasLimit), B(acc.VelocityPlannerCoastAuthorityActive),
                B(acc.VelocityPlannerBiasAtLimit), Csv(acc.ControlState), Csv(acc.AccelerationLimitState)
            });
            lock (sync)
            {
                if (velocityDiagnosticsWriter == null) return;
                velocityDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) velocityDiagnosticsWriter.Flush();
            }
        }

        // Dedicated ALT trace.  ALT remains an outer trajectory director: every row
        // shows the altitude-derived planned V/S demand handed to the existing V/S director.
        internal void SampleAltitudeDiagnostics(Vessel vessel, AERISAltitudeDirector alt,
            AERISVerticalSpeedDirector vs, AERISPitchDirector pitch, VirtualAttitudeInstrument attitude)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || alt == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextAltDiagnosticsSample) return;
            nextAltDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()),
                B(alt.Armed), B(alt.ControlActive), F(alt.TargetAltitudeMeters),
                F(alt.AltitudeHoldReferenceMeters), F(alt.AltitudeHoldReferenceOffsetMeters),
                F(alt.AltitudeHoldBandLowerMeters), F(alt.AltitudeHoldBandUpperMeters),
                F(alt.CurrentAltitudeMeters), F(alt.AltitudeControlErrorMeters),
                F(alt.AltitudeErrorMeters), F(alt.AltitudeHoldBandErrorMeters),
                B(alt.AltitudeInsidePreferredHoldBand), F(alt.CurrentVerticalSpeedMps),
                F(alt.AltitudeReferenceVerticalSpeedMps), F(alt.AltitudeReconciledVerticalSpeedMps),
                F(alt.AltitudeRateBiasMps), B(alt.AltitudeRateReconciliationActive),
                F(alt.AltitudeRateReconciliationBlend), F(alt.AltitudeRateCommandBiasMps),
                F(alt.DesiredVerticalSpeedMps), F(alt.PlannedVerticalSpeedMps), F(alt.AltitudeRateDemandMps), F(alt.StoppingRateLimitMps),
                F(alt.StopDistanceMeters), F(alt.TransportLeadMeters), F(alt.MeasuredBrakeLagRateMps),
                F(alt.MeasuredBrakeLagLeadMeters), F(alt.AltitudeTerminalVerticalSpeedDampingPerSec),
                F(alt.AltitudeTerminalEffectiveFineBandMeters),
                F(alt.AltitudeTerminalEffectiveMaxRateMps),
                B(alt.AltitudeTerminalInnerSettleActive),
                F(alt.AltitudeTerminalInnerSettleEffectiveBandMeters),
                F(alt.AltitudeTerminalInnerSettleEffectiveExitBandMeters),
                F(alt.AltitudeTerminalInnerSettleEffectiveMaxRateMps),
                F(alt.AltitudeTerminalInnerSettleEffectiveBrakeRateMps),
                F(alt.AltitudeTerminalInnerSettleEffectiveDampingPerSec),
                B(alt.AltitudeTerminalPredictiveBrakeActive),
                F(alt.AltitudeTerminalPredictiveBrakeEffectiveLeadSeconds),
                F(alt.AltitudeTerminalPredictiveBrakeEffectiveBandMeters),
                F(alt.AltitudeTerminalPredictiveBrakeInboundRateMps),
                F(alt.AltitudeTerminalPredictiveBrakeTimeToTargetSeconds),
                F(alt.AltitudeTerminalPredictiveBrakeDemandMps),
                F(alt.AltitudePrecisionReferenceVerticalSpeedMps),
                B(alt.AltitudePrecisionReferenceRateActive),
                B(alt.AltitudePrecisionDirectReferenceRateActive),
                F(alt.AltitudePrecisionReferenceDeltaVsReconciledMps),
                B(alt.AltitudePrecisionEntryMeasuredRateOk), B(alt.AltitudePrecisionEntryPlannedRateOk),
                B(alt.AltitudePrecisionEntryDirectionOk), B(alt.AltitudePrecisionEntryReady),
                F(alt.AltitudePrecisionEntryPhysicalPlannedRateMps),
                F(alt.AltitudeHoldNeutralCommandMps),
                B(alt.BankVerticalSupportEligible), B(alt.BankVerticalSupportActive), F(alt.BankVerticalSupportBankDeg),
                F(alt.BankVerticalSupportRollRateDegPerSec), F(alt.BankVerticalSupportLoadFactorExcess),
                F(alt.BankVerticalSupportSinkActivation), F(alt.BankVerticalSupportTransitionRateMps),
                F(alt.BankVerticalSupportTargetRateMps), F(alt.BankVerticalSupportRateMps),
                F(alt.AltitudeBankSupportTerminalBandMeters),
                B(alt.HoldDisturbanceRecoveryActive), F(alt.HoldDisturbanceExitElapsedSeconds),
                F(alt.AltitudeHoldDisturbanceTrackingBandMps), B(alt.HoldDisturbanceDirectionGateActive),
                F(alt.HoldDisturbanceOutwardRateMps), B(alt.HoldDisturbanceRawExitCandidate),
                B(alt.HoldDisturbancePrecisionOwnershipActive),
                F(alt.HoldDisturbancePrecisionOwnershipBandMeters),
                B(alt.HoldCaptureBrakeActive),
                B(alt.HoldCaptureBrakeHysteresisActive),
                F(alt.HoldCaptureBrakeCompletionBlend),
                F(alt.AltitudeHoldCaptureBrakeTaperExponent),
                F(alt.AltitudeHoldDisturbanceOutwardRateMps),
                F(alt.AltitudeHoldCaptureBrakeExitMps),
                F(alt.HoldCaptureBrakeOutwardRateMps),
                F(alt.HoldCaptureBrakeEffectiveDampingPerSec),
                F(alt.HoldCaptureBrakeEffectiveMaxRateMps),
                B(alt.HoldNeutralRateBrakeActive),
                F(alt.HoldNeutralRateBrakeAbsRateMps),
                F(alt.AltitudeHoldNeutralRateBrakeEnterMps),
                F(alt.AltitudeHoldNeutralRateBrakeExitMps),
                F(alt.AltitudeHoldNeutralRateBrakeFullMps),
                F(alt.HoldNeutralRateBrakeCompletionBlend),
                B(alt.HoldResidualRateCompletionActive),
                B(alt.HoldResidualRateCompletionReleaseActive),
                B(alt.HoldResidualRateCompletionCalm),
                F(alt.HoldResidualRateCompletionPhysicalRateMps),
                F(alt.HoldResidualRateCompletionAbsRateMps),
                F(alt.HoldResidualRateCompletionPlannedRateMps),
                F(alt.AltitudeHoldResidualRateCompletionPlannedExitMps),
                F(alt.AltitudeHoldResidualRateCompletionDampingTailScale),
                F(alt.HoldResidualRateCompletionDampingBlend),
                F(alt.HoldResidualRateCompletionPositionBlend),
                F(alt.AltitudeHoldResidualRateCompletionPositionReleasePerSec),
                F(alt.HoldResidualRateCompletionEffectivePositionGainPerSec),
                B(alt.HoldPipelineUnloadActive),
                F(alt.AltitudeHoldPipelineUnloadGain),
                F(alt.AltitudeHoldPipelineUnloadPhysicalGateStartMps),
                F(alt.AltitudeHoldPipelineUnloadPhysicalGateFullMps),
                F(alt.AltitudeHoldPipelineUnloadPlannedGateStartMps),
                F(alt.AltitudeHoldPipelineUnloadPlannedGateFullMps),
                F(alt.HoldPipelineUnloadPhysicalTowardRateMps),
                F(alt.HoldPipelineUnloadPlannedPhysicalRateMps),
                F(alt.HoldPipelineUnloadPlannedTowardRateMps),
                F(alt.HoldPipelineUnloadPhysicalGateBlend),
                F(alt.HoldPipelineUnloadPlannedGateBlend),
                F(alt.HoldPipelineUnloadBlend),
                F(alt.HoldPipelineUnloadRawBeforeMps),
                F(alt.HoldPipelineUnloadRequestedRateMps),
                F(alt.HoldPipelineUnloadAppliedRateMps),
                B(alt.PrecisionLowQRateGainActive),
                F(alt.PrecisionLowQRateGainQBlend),
                F(alt.PrecisionLowQRateGainErrorBlend),
                F(alt.PrecisionLowQRateGainBlend),
                F(alt.AltitudePrecisionRateGainPerSec),
                F(alt.AltitudePrecisionLowQRateGainPerSec),
                F(alt.PrecisionEffectiveRateGainPerSec),
                F(alt.AltitudePrecisionLowQGainFullBandMeters),
                F(alt.AltitudePrecisionLowQGainReleaseBandMeters),
                B(alt.PrecisionLowQDampingActive),
                F(alt.PrecisionLowQDampingQBlend),
                F(alt.AltitudePrecisionVerticalSpeedDampingPerSec),
                F(alt.AltitudePrecisionLowQDampingPerSec),
                F(alt.PrecisionEffectiveBaseDampingPerSec),
                B(alt.MicroTrimEnabled), B(alt.MicroTrimEligible),
                B(alt.MicroTrimPulseActive), B(alt.MicroTrimObservationActive),
                F(alt.MicroTrimPulseRateMps), F(alt.MicroTrimPulseElapsedSeconds),
                F(alt.MicroTrimWaitElapsedSeconds), F(alt.MicroTrimLearnedPulseMagnitudeMps),
                F(alt.MicroTrimLearnedPulseDurationSeconds), F(alt.MicroTrimLearnedWaitSeconds),
                F(alt.MicroTrimLearnedDelaySeconds), F(alt.MicroTrimLearnedResponseGain),
                F(alt.MicroTrimObservedResponseMps), F(alt.MicroTrimAppliedRateMps),
                AERISCsvField.Integer(alt.MicroTrimPulseCount),
                B(alt.MicroTrimObserverReady), F(alt.MicroTrimObserverCorrelation),
                F(alt.MicroTrimLearnedCyclePeriodSeconds),
                F(alt.MicroTrimLearnedHalfCycleSeconds),
                B(alt.MicroTrimPulseScheduled), F(alt.MicroTrimScheduledWaitSeconds),
                F(alt.MicroTrimPredictedFutureRateMps),
                F(alt.MicroTrimBaseRawRateMps), F(alt.MicroTrimSafeMagnitudeMps),
                AERISCsvField.Integer(alt.MicroTrimTargetCrossingCount),
                F(alt.MicroTrimLastCrossingRateMps),
                AERISCsvField.Integer(alt.MicroTrimFutureHalfCycles),
                F(alt.MicroTrimObserverInputCommandMps),
                F(alt.MicroTrimObserverBaseCommandMps),
                B(alt.MicroTrimPairGuardActive),
                F(alt.MicroTrimLastAppliedPulseDirection),
                AERISCsvField.Integer(alt.MicroTrimPositivePulseCount),
                AERISCsvField.Integer(alt.MicroTrimNegativePulseCount),
                F(alt.MicroTrimBiasEstimateMeters),
                B(alt.MicroTrimBiasGuardActive),
                F(alt.MicroTrimBiasGuardElapsedSeconds),
                B(alt.MicroTrimBiasRecoveryActive),
                F(alt.MicroTrimBiasRecoveryBlend),
                F(alt.MicroTrimBiasCorrectiveDirection),
                F(alt.MicroTrimBiasPulseScale),
                B(alt.MicroTrimBiasHardGuardActive),
                B(alt.MicroTrimBiasHardGuardRecoveryPermitted),
                B(alt.MicroTrimBiasHardGuardInhibitActive),
                Csv(alt.MicroTrimBiasHardGuardReason),
                B(alt.HoldInboundArrivalBrakeActive),
                F(alt.AltitudeHoldInboundArrivalBrakeEnterMps),
                F(alt.AltitudeHoldInboundArrivalBrakeFullMps),
                F(alt.AltitudeHoldInboundArrivalBrakeLeadStartSeconds),
                F(alt.AltitudeHoldInboundArrivalBrakeLeadFullSeconds),
                F(alt.AltitudeHoldInboundArrivalBrakeLowQDampingPerSec),
                F(alt.HoldInboundArrivalBrakeRateMps),
                F(alt.HoldInboundArrivalBrakeTimeToTargetSeconds),
                F(alt.HoldInboundArrivalBrakeRateGateBlend),
                F(alt.HoldInboundArrivalBrakeBlend),
                F(alt.HoldInboundArrivalBrakeEffectiveDampingPerSec),
                B(alt.HoldDisturbanceExitCandidate),
                F(alt.HoldDisturbanceRequiredDwellSeconds), B(alt.RolloutActive), B(alt.HoldLatched),
                B(alt.PrecisionCorrectionActive), F(alt.AltitudePrecisionNeutralEnterBandMeters),
                F(alt.AltitudePrecisionNeutralExitBandMeters), F(alt.AltitudePrecisionMinRateMps),
                F(alt.PrecisionCorrectionRateMps), F(alt.PrecisionRawRateMps), F(alt.AltitudePrecisionRateGainPerSec),
                F(alt.AltitudePrecisionVerticalSpeedDampingPerSec), F(alt.AltitudePrecisionCommandSlewMps2),
                F(alt.HoldEntryElapsedSeconds), F(alt.HoldExitElapsedSeconds),
                F(alt.AltitudeRateAccelLimitMps2), F(alt.AltitudeRateBrakeAccelLimitMps2),
                F(alt.ScheduledVerticalDecelMps2), F(alt.MaxAltitudeVerticalSpeedMps), F(alt.MaxAltitudePitchDeg),
                B(alt.LowQVerticalEnvelopeActive), F(alt.LowQVerticalEnvelopeDynamicPressureKpa),
                F(alt.LowQVerticalEnvelopeBlend), F(alt.LowQVerticalEnvelopeVsCapMps),
                F(alt.LowQVerticalEnvelopeAppliedAccelLimitMps2), F(alt.LowQVerticalEnvelopeAppliedBrakeAccelLimitMps2),
                F(alt.LowQVerticalEnvelopeEffectiveScheduledDecelMps2), F(alt.LowQVerticalEnvelopeEffectiveTerminalCorridorMeters),
                B(alt.LowQVerticalEnvelopeSymmetricRateCapActive), F(alt.LowQVerticalEnvelopeOutputVsMps),
                B(alt.AoAClimbGovernorActive), B(alt.AoAClimbGovernorAoAValid), F(alt.AoAClimbGovernorAoADeg),
                F(alt.AoAClimbGovernorBlend), F(alt.AoAClimbGovernorTargetVsCapMps),
                F(alt.AoAClimbGovernorAppliedVsCapMps), F(alt.AoAClimbGovernorOutputVsMps),
                F(alt.AoAClimbGovernorSurfaceSpeedMps), Csv(alt.ControlState),
                B(vs != null && vs.Armed), B(vs != null && vs.ControlActive),
                B(vs != null && vs.AltitudeRateDemandActive), F(vs != null ? vs.AltitudeRateDemandMps : 0f),
                B(vs != null && vs.AltitudePitchLimitActive), F(vs != null ? vs.AltitudePitchLimitDeg : 0f), F(vs != null ? vs.EffectiveMaxPitchTargetDeg : 0f),
                F(vs != null ? vs.EffectiveTargetVerticalSpeedMps : 0f), F(vs != null ? vs.CurrentVerticalSpeedMps : 0f),
                F(vs != null ? vs.VerticalSpeedErrorMps : 0f), Csv(vs != null ? vs.PrecisionPhase : "Unavailable"),
                F(pitch != null ? pitch.AaNativePitchRateDemandDegPerSec : 0f),
                B(vs != null && vs.AltitudePrecisionTrackingActive), F(vs != null ? vs.AltitudePrecisionTrackingEnterElapsedSeconds : 0f),
                F(vs != null ? vs.AltitudePrecisionTrackingExitElapsedSeconds : 0f), B(vs != null && vs.AltitudeLowQPrecisionQuietingActive),
                F(vs != null ? vs.AltitudeLowQPrecisionQuietingBlend : 0f),
                F(vs != null ? vs.AltitudeLowQPrecisionRateAuthorityRecoveryBlend : 0f),
                F(vs != null ? vs.AltitudeLowQPrecisionQuietingRateScale : 1f),
                F(vs != null ? vs.AltitudeLowQPrecisionQuietingDampingScale : 1f), F(vs != null ? vs.AltitudeLowQPrecisionEffectiveRateLimitDegPerSec : 0f)
            });
            lock (sync)
            {
                if (altDiagnosticsWriter == null) return;
                altDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) altDiagnosticsWriter.Flush();
            }
        }

        internal void SampleGroundTakeoffDiagnostics(Vessel vessel, GroundStabilityProtection ground,
            AERISAutoTakeoffDirector takeoff, VirtualAttitudeInstrument attitude, FlightCtrlState state)
        {
            if (!R016HighRateDiagnosticsEnabled) return;
            if (vessel == null || state == null) return;
            BeginFlight(vessel);
            float now = Time.realtimeSinceStartup;
            if (now < nextGroundTakeoffDiagnosticsSample) return;
            nextGroundTakeoffDiagnosticsSample = now + 0.02f;
            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()),
                B(ground != null && ground.Enabled), B(ground != null && ground.Available),
                B(ground != null && ground.ReliableGrounded), B(ground != null && ground.LiftoffConfirmed),
                B(ground != null && ground.ControlActive), Csv(ground != null ? ground.Status : "UNAVAILABLE"),
                F(attitude != null ? attitude.SurfaceSpeedMps : (float)vessel.srfSpeed),
                F(attitude != null ? attitude.RadarAltitudeM : (float)vessel.heightFromTerrain),
                F(attitude != null ? attitude.VerticalSpeedMps : 0f),
                F(ground != null ? ground.TargetHeadingDeg : 0f), F(ground != null ? ground.CurrentHeadingDeg : 0f),
                F(ground != null ? ground.HeadingErrorDeg : 0f), F(ground != null ? ground.PilotYaw : 0f),
                F(ground != null ? ground.PilotRoll : 0f), B(ground != null && ground.PilotSharedControlActive),
                F(ground != null ? ground.YawRateDemandDegPerSec : 0f), F(ground != null ? ground.RollRateDemandDegPerSec : 0f),
                F(ground != null ? ground.YawAuthorityScale : 0f), F(ground != null ? ground.RollAuthorityScale : 0f),
                B(ground != null && ground.AaNativeYawOverrideActive), B(ground != null && ground.AaNativeRollOverrideActive),
                B(ground != null && ground.PostTouchdownSessionActive), B(ground != null && ground.ThrottleCutActive),
                B(ground != null && ground.ReverseThrustControlActive), B(ground != null && ground.AaNativeThrottleOverrideActive),
                B(ground != null && ground.GroundAssistMasterEnabled), B(ground != null && ground.BrakeAssistConfigured),
                B(ground != null && ground.BrakeAssistActive), Csv(ground != null ? ground.BrakeAssistStatus : "UNAVAILABLE"),
                F(ground != null ? ground.TouchdownStableSeconds : 0f), F(ground != null ? ground.RequestedDecelerationMps2 : 0f),
                F(ground != null ? ground.MeasuredDecelerationMps2 : 0f), F(ground != null ? ground.BrakeDemand : 0f),
                F(ground != null ? ground.FinalBrakeDemand : 0f),
                F(ground != null ? ground.WheelBrakeAppliedDemand : 0f),
                B(ground != null && ground.WheelBrakeStockFallbackActive),
                F(ground != null ? ground.WheelBrakeModuleCount : 0),
                B(ground != null && ground.PilotBrakeRequestActive),
                F(ground != null ? ground.GroundOwnershipBlend : 1f),
                F(ground != null ? ground.BrakeFallbackEvidenceSeconds : 0f),
                F(ground != null ? ground.GroundStabilityAllowance : 0f),
                F(ground != null ? ground.BrakeCapabilityMps2PerUnit : 0f), B(ground != null && ground.AirbrakeLinkConfigured),
                F(ground != null ? ground.AirbrakeLinkDemand : 0f), B(ground != null && ground.ParkingHoldConfigured),
                B(ground != null && ground.ParkingHoldActive),
                F(ground != null ? ground.ParkingHoldPilotReleaseCount : 0),
                B(ground != null && ground.DragChuteAutoConfigured),
                Csv(ground != null ? ground.DragChuteStatus : "UNAVAILABLE"),
                F(ground != null ? ground.DragChuteDeployedCount : 0),
                B(ground != null && ground.ReverseThrustAutoConfigured),
                Csv(ground != null ? ground.ReverseThrustStatus : "UNAVAILABLE"),
                F(ground != null ? ground.ReverseThrustDemand : 0f),
                Csv(ground != null ? ground.ReverseProviderId : "None"),
                Csv(takeoff != null ? takeoff.PropulsionMode : "UNAVAILABLE"), B(takeoff != null && takeoff.ExternalPropulsionTakeoff),
                F(takeoff != null ? (double)takeoff.AttemptGeneration : 0d),
                F(takeoff != null ? (double)takeoff.ArmedVesselPersistentId : 0d),
                F(takeoff != null ? takeoff.EngineStageNumber : -1), Csv(takeoff != null ? takeoff.EngineStageStatus : "UNAVAILABLE"),
                B(takeoff != null && takeoff.BrakeReleaseConfirmed),
                Csv(takeoff != null ? takeoff.PhaseText : "UNAVAILABLE"), Csv(takeoff != null ? takeoff.Status : "UNAVAILABLE"),
                B(takeoff != null && takeoff.Armed), B(takeoff != null && takeoff.Executing),
                F(takeoff != null ? takeoff.SelectedStallSpeedMps : 0f), F(takeoff != null ? takeoff.SelectedVrMps : 0f),
                Csv(takeoff != null ? takeoff.SelectedVrSource : "NONE"), Csv(takeoff != null ? takeoff.SelectedVrDetail : string.Empty),
                B(takeoff != null && takeoff.VrFrozen), B(takeoff != null && takeoff.RotationGateReady),
                Csv(takeoff != null ? takeoff.RotationGateReason : "NONE"), F(takeoff != null ? takeoff.PitchRateDemandDegPerSec : 0f),
                F(takeoff != null ? takeoff.ThrottleDemand : 0f), B(takeoff != null && takeoff.AaNativePitchOverrideActive),
                B(takeoff != null && takeoff.AaNativeThrottleOverrideActive), B(vessel.ActionGroups[KSPActionGroup.Brakes]),
                F(StandardFlyByWire.LastFinalPitch), F(StandardFlyByWire.LastFinalRoll),
                F(StandardFlyByWire.LastFinalYaw), F(StandardFlyByWire.LastFinalThrottle)
            });
            lock (sync)
            {
                if (groundTakeoffDiagnosticsWriter == null) return;
                groundTakeoffDiagnosticsWriter.WriteCsv(line);
                if (now >= nextFlush) groundTakeoffDiagnosticsWriter.Flush();
            }
        }

        internal void Sample(Vessel vessel, ProtectTelemetry protect, AERISBankDirector bank, AERISPitchDirector pitchDirector, AERISVerticalSpeedDirector vs, AERISAltitudeDirector alt, AERISHdgDirector hdg, AERISAccelerationDirector acc, AERISVelocityDirector vel, VirtualAttitudeInstrument attitude, TopModuleManager manager, bool master, bool aaComparisonTelemetryEnabled)
        {
            if (vessel == null || vessel.transform == null) return;
            BeginFlight(vessel);
            if (Time.realtimeSinceStartup < nextSample) return;
            nextSample = Time.realtimeSinceStartup + SampleIntervalSeconds;

            FlightCtrlState input = FlightInputHandler.state;
            float inputPitch = input != null ? input.pitch : 0f;
            float inputRoll = input != null ? input.roll : 0f;
            float inputYaw = input != null ? input.yaw : 0f;
            float inputThrottle = input != null ? input.mainThrottle : 0f;
            Vector3 e = vessel.transform.rotation.eulerAngles;
            float pitch = NormalizeSigned(e.x);
            float roll = NormalizeSigned(e.z);
            float heading = vessel.transform.rotation.eulerAngles.y;
            float g = (float)vessel.geeForce;
            float aoa = protect != null ? protect.AoADegrees : 0f;
            bool pActive = protect != null && protect.ProtectActive;
            if (pActive && !previousProtect) protectInterventions++;
            previousProtect = pActive;
            maxSpeed = Mathf.Max(maxSpeed, (float)vessel.srfSpeed);
            maxAoA = Mathf.Max(maxAoA, Mathf.Abs(aoa));
            maxG = Mathf.Max(maxG, Mathf.Abs(g));
            if (float.IsNaN(maxSpeed) || float.IsInfinity(maxSpeed)) maxSpeed = 0f;
            if (float.IsNaN(maxAoA) || float.IsInfinity(maxAoA)) maxAoA = 0f;
            if (float.IsNaN(maxG) || float.IsInfinity(maxG)) maxG = 0f;

            AERISCsvField[] line = CaptureCsv(new AERISCsvField[] {
                Utc(DateTime.UtcNow), F(Planetarium.GetUniversalTime()), F(vessel.altitude), F(protect != null ? protect.RadarAltitude : 0f), F((float)vessel.srfSpeed), F(heading), F(pitch), F(roll), F(aoa), F(protect != null ? protect.SideslipDegrees : 0f), F(g),
                F(inputPitch), F(inputRoll), F(inputYaw), F(inputThrottle), B(master), B(pitchDirector != null && pitchDirector.Armed), F(pitchDirector != null ? pitchDirector.TargetPitch : 0f), F(pitchDirector != null ? pitchDirector.CurrentPitch : 0f), F(pitchDirector != null ? pitchDirector.PitchError : 0f), F(pitchDirector != null ? pitchDirector.PitchRateRequestDegPerSec : 0f), B(pitchDirector != null && pitchDirector.AaNativePitchRateOverrideActive), F(pitchDirector != null ? pitchDirector.AaNativePitchRateDemandDegPerSec : 0f), F(pitchDirector != null ? pitchDirector.AaNativePitchRateDemandRadPerSec : 0f), F(pitchDirector != null ? pitchDirector.RawPilotPitch : 0f), F(pitchDirector != null ? pitchDirector.PitchInputAfterNeutralization : 0f), Csv(pitchDirector != null ? pitchDirector.ControlState : "Unavailable"), B(bank != null && bank.Armed), F(bank != null ? bank.TargetBank : 0f), F(bank != null ? bank.CurrentBank : 0f), F(bank != null ? bank.BankError : 0f), F(bank != null ? bank.RollRateRequest : 0f), F(bank != null ? bank.ActualRollRate : 0f), F(bank != null ? bank.AaNativeRollRateDemandDegPerSec : 0f), F(bank != null ? bank.RawPilotRoll : 0f), F(bank != null ? bank.InjectedRoll : 0f), B(bank != null && bank.AaNativeRollRateOverrideActive), F(bank != null ? bank.AaNativeRollRateDemandRadPerSec : 0f), Csv(bank != null ? bank.ControlState : "Unavailable"), Csv(bank != null ? bank.CapturePhase : "Unavailable"), B(hdg != null && hdg.Armed), F(hdg != null ? hdg.TargetHeading : 0f), F(hdg != null ? hdg.HeadingError : 0f), B(hdg != null && hdg.AaNativeYawRateOverrideActive), F(hdg != null ? hdg.AaNativeYawRateDemandDegPerSec : 0f), F(hdg != null ? hdg.AaNativeYawRateDemandRadPerSec : 0f), F(hdg != null ? hdg.YawInputAfterNeutralization : 0f),
                B(attitude != null && attitude.InstrumentValid), F(attitude != null ? attitude.InstrumentConfidence : 0f), F(attitude != null ? attitude.InstrumentBankDeg : 0f), F(attitude != null ? attitude.InstrumentBankWrappedDeg : 0f), F(attitude != null ? attitude.InstrumentHorizonBankDeg : 0f), B(attitude != null && attitude.InstrumentHorizonBankValid), F(attitude != null ? attitude.InstrumentHorizonBankConfidence : 0f), F(attitude != null ? attitude.InstrumentPitchDeg : 0f), B(attitude != null && attitude.InstrumentPitchValid), F(attitude != null ? attitude.InstrumentHeadingDeg : 0f), B(attitude != null && attitude.InstrumentHeadingValid), F(attitude != null ? attitude.InstrumentRollRateDegPerSec : 0f), F(attitude != null ? attitude.InstrumentPitchRateDegPerSec : 0f), F(attitude != null ? attitude.InstrumentYawRateDegPerSec : 0f), F(attitude != null ? attitude.SurfaceSpeedMps : 0f), F(attitude != null ? attitude.VerticalSpeedMps : 0f), F(attitude != null ? attitude.DynamicPressureKpa : 0f), F(attitude != null ? attitude.StaticPressureKpa : 0f), F(attitude != null ? attitude.DensityKgM3 : 0f), F(attitude != null ? attitude.GeeForce : 0f),
                F(StandardFlyByWire.LastFinalPitch), F(StandardFlyByWire.LastFinalRoll), F(StandardFlyByWire.LastFinalYaw), F(StandardFlyByWire.LastFinalThrottle),
                Csv(protect != null ? protect.RiskText : "Unavailable"), B(pActive), F(protect != null ? protect.RequestedAssistThrottle : 0f), F(protect != null ? protect.RequiredForwardThrustkN : 0f), F(protect != null ? protect.ActualAvailableForwardThrustkN : 0f), Csv(protect != null ? protect.PropulsionProviderId : "None"),
                F(protect != null ? protect.DynamicPressureKpa : 0f), B(protect != null && protect.SpeedDirectorDecelerationActive),
                B(protect != null && protect.HighEnergyDecelerationActive), B(protect != null && protect.IntentionalDecelerationActive),
                B(protect != null && protect.DecelerationThrustInhibitActive), B(protect != null && protect.ThrustAssistInhibitedByDeceleration),
                B(alt != null && alt.Armed), B(alt != null && alt.ControlActive), F(alt != null ? alt.TargetAltitudeMeters : 0f), F(alt != null ? alt.CurrentAltitudeMeters : 0f), F(alt != null ? alt.AltitudeErrorMeters : 0f), F(alt != null ? alt.AltitudeRateDemandMps : 0f), F(alt != null ? alt.MaxAltitudeVerticalSpeedMps : 0f), F(alt != null ? alt.MaxAltitudePitchDeg : 0f), Csv(alt != null ? alt.ControlState : "Unavailable"),
                B(StandardFlyByWire.LastPitchRateExternalControlActive), B(StandardFlyByWire.LastPitchRateModerationEnvelopeAvailable),
                B(StandardFlyByWire.LastPitchRateModerationActive), F(StandardFlyByWire.LastPitchRateRequestedRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateAppliedRadPerSec * Mathf.Rad2Deg), F(StandardFlyByWire.LastPitchRateModerationDeltaRadPerSec * Mathf.Rad2Deg),
                F(StandardFlyByWire.LastPitchRateLowerLimitRadPerSec * Mathf.Rad2Deg), F(StandardFlyByWire.LastPitchRateUpperLimitRadPerSec * Mathf.Rad2Deg),
                B(StandardFlyByWire.LastPitchRateAoAModerationEnabled), B(StandardFlyByWire.LastPitchRateGModerationEnabled)
            });
            lock (sync)
            {
                if (fdrWriter == null) return;
                fdrWriter.WriteCsv(line);
                sampleCount++;
                if (Time.realtimeSinceStartup >= nextFlush)
                {
                    nextFlush = Time.realtimeSinceStartup + FlushIntervalSeconds;
                    cvrWriter.Flush();
                    fdrWriter.Flush();
                }
            }
            SampleAaComparison(vessel, protect, bank, pitchDirector, vs, alt, hdg, acc, vel, attitude, manager, master, aaComparisonTelemetryEnabled);
        }

        internal void EndFlight(string reason)
        {
            AERISFlightDataArchive.DrainResults();
            string completedFolder = null;
            lock (sync)
            {
                if (cvrWriter == null && fdrWriter == null && telemetryWriters.Count == 0) return;
                if (cvrWriter != null) { try { RecordCvr("FDR", "INFO", "flight recorder stopped; reason=" + reason); } catch { } }
                try { WriteAaComparisonConditionSummary(); } catch { }
                try { WriteSummary(reason); } catch { }

                CloseWriter(ref cvrWriter);
                CloseWriter(ref fdrWriter);
                CloseWriter(ref bankDiagnosticsWriter);
                CloseWriter(ref apSmoothnessWriter);
                CloseWriter(ref vsDiagnosticsWriter);
                CloseWriter(ref vsCruiseAccelerationGuideWriter);
                CloseWriter(ref pitchDiagnosticsWriter);
                CloseWriter(ref hdgDiagnosticsWriter);
                CloseWriter(ref altDiagnosticsWriter);
                CloseWriter(ref accelerationDiagnosticsWriter);
                CloseWriter(ref velocityDiagnosticsWriter);
                CloseWriter(ref groundTakeoffDiagnosticsWriter);
                CloseWriter(ref aaComparisonWriter);
                foreach (var writer in telemetryWriters.Values) CloseWriterBestEffort(writer);
                telemetryWriters.Clear();
                nextTelemetryWrite.Clear();
                disabledTelemetryChannels.Clear();
                aaComparisonSummaries.Clear();
                completedFolder = folder;
                folder = null;
                vesselId = null;
                vesselName = null;
            }
            // The ordered writer invokes the archive only after every preceding session
            // close is complete. No KSP/Unity object is captured by this callback.
            string sealedFolder = completedFolder;
            if (!AERISBackgroundFileWriter.SealSession(sealedFolder,
                () => AERISFlightDataArchive.QueueArchive(sealedFolder)))
                AERISBackgroundFileWriter.RetainRawSession(sealedFolder,
                    "session seal was rejected; raw data retained");
        }

        static void CloseWriter(ref AERISAsyncFileChannel writer)
        {
            AERISAsyncFileChannel closing = writer;
            writer = null;
            CloseWriterBestEffort(closing);
        }

        static void CloseWriterBestEffort(AERISAsyncFileChannel writer)
        {
            if (writer == null) return;
            try { writer.Flush(); } catch { }
            try { writer.Dispose(); } catch { }
        }

        void WriteMetadata(Vessel v)
        {
            using (var w = new AERISAsyncFileChannel(Path.Combine(folder, "metadata.txt"), false,
                AERISFileRecordPriority.Verbose))
            {
                w.WriteLine("AERIS Flight Data Recorder");
                w.WriteLine("UTC=" + DateTime.UtcNow.ToString("o"));
                w.WriteLine("AERIS_VERSION=" + AERISBuildVersion.Semantic);
                w.WriteLine("Vessel=" + v.vesselName);
                w.WriteLine("VesselId=" + v.id);
                w.WriteLine("Body=" + (v.mainBody != null ? v.mainBody.bodyName : "unknown"));
                w.WriteLine("FDRSampleHz=10");
                w.WriteLine("ARCHIVE=End-of-flight managed ZIP; temporary .zip.tmp; verified before atomic rename; source retained on failure; no shell or platform-specific command");
                w.WriteLine("BANK_DIAGNOSTICS=fdr_bank_diagnostics.csv; dedicated 50 Hz BANK trace including raw/trend H-BANK rates, exact native AA roll-rate transport, and all-altitude precision-hold acceptance telemetry.");
                w.WriteLine("AP_SMOOTHNESS=fdr_ap_smoothness.csv; 50 Hz common AP-axis trace with schema header/data columns kept identical, including native PITCH/HDG rate transport and retained v0.8.31 AAFBW pitch-rate moderation request/applied/bounds telemetry.");
                w.WriteLine("PITCH_DIAGNOSTICS=fdr_pitch_diagnostics.csv; dedicated 50 Hz PITCH target/current/error/rate/shadow/native-transport trace with retained v0.8.31 AAFBW external ownership, moderation availability, intervention and rate-bound telemetry, plus v0.9.7 adaptive high-G pitch-rate attribution and vertical-demand priority arbitration.");
                w.WriteLine("VS_DIAGNOSTICS=fdr_vs_diagnostics.csv; dedicated 50 Hz V/S target/current/error/prediction/contribution trace, internal pitch-trajectory tracking, v0.4.97 phase-separated BasePitch precision-capture/hold telemetry with entry/exit hysteresis and explicit armed-vs-active target semantics, zero-V/S speed-feed-forward isolation telemetry, v0.6.6 manual non-zero-to-zero V/S transition D-term guard telemetry, v0.6.7 high-q non-zero V/S PrecisionCapture stabilization telemetry, v0.6.8 jerk-limited manual large-V/S-to-zero deceleration trajectory telemetry, v0.7.6 low-q vertical authority-envelope telemetry, v0.7.7 high-q MainTrajectory tracking-stabilizer telemetry, v0.8.31 ALT precision track/hold hysteresis and low-q BasePitch quieting telemetry, v0.8.32 boundary-before-release rate-authority recovery telemetry, and the native AA pitch-rate handoff.");
                w.WriteLine("VS_CRUISE_ACCELERATION_GUIDE=fdr_vs_cruise_acceleration_guide.csv; dedicated 50 Hz v0.8.34 fixed low-q precision guide trace: active blend, desired/measured vertical acceleration, acceleration residual, legacy/guide/applied BasePitch rates, pre-brake state, precision phase, low-q blend and retained AAFBW requested/applied/intervention values.");
                w.WriteLine("HDG_DIAGNOSTICS=fdr_hdg_diagnostics.csv; dedicated 50 Hz HDG target/error/BANK handoff and AA-native yaw-rate trace. v0.11.7 replaces fixed 90 m/s / 4 kPa entry gates with a relative-q 15-to-30-degree exploration envelope, then learns a 30-to-45-degree ceiling only from stable BANK tracking and measured sustainable G while radar-altitude/predicted-stall-margin/PROTECT gates retain veto authority. v0.9.14 retains v0.9.12 roll-first high-energy phases, continuous predictive stall-margin authority and recovery dwell, measured sustainable-G capability/cap/tracking error, capability-derived low-q BANK/G limits, STALL RECOVERY, AA LIMIT HOLD state and prior-frame AA pitch moderation request/applied authority, 1-9G scheduling, 80-degree hard bank limit, low-q-dependent turn-yaw fade, and independent attitude-stability yaw terms.");
                w.WriteLine("ALT_DIAGNOSTICS=fdr_alt_diagnostics.csv; dedicated 50 Hz ALT target/current/error/stopping-distance/planned-V-S/V/S-handoff trace, including configured max V/S, ALT max-pitch, symmetric low-q climb/descent authority, q-scheduled stopping deceleration and terminal corridor, effective V/S pitch-envelope, measured V/S brake-lag lead, terminal vertical-rate damping, low-q predictive terminal braking, hysteretic inner settling handoff with preserved braking authority, reference-consistent precision neutral, PrecisionHold residual-ownership versus true-disturbance recovery telemetry, v0.8.23 reconciled PrecisionHold rate feedback with direct-derivative comparison telemetry, withdrawn v0.8.20 outward-tail, v0.8.21 pipeline-unload, and v0.8.22 position-gain compatibility fields plus symmetric low-q PrecisionHold base-damping telemetry, retained exponent-shaped hold-capture and tapered neutral-crossing brake telemetry, symmetric low-q inbound precision-arrival gate telemetry, v0.8.29 selected-target / display-safe-window / balanced-control-reference telemetry, v0.8.30 fault-aware hard-bias guard disposition/reason telemetry, retained v0.8.31 V/S precision phase-latch and low-q quieting state/scale/effective-rate telemetry, and v0.8.32 boundary-before-release rate-authority recovery telemetry, plus the retained session-local phase-locked Micro-Trim actual-command observer, strict pulse-pair guard, pair-amplitude bias recovery, schedule and cancellation telemetry, bank-aware anti-sink support telemetry, and terminal altitude-reference rate reconciliation. ALT is an outer trajectory director only.");
                w.WriteLine("ACC_DIAGNOSTICS=fdr_acc_diagnostics.csv; dedicated 50 Hz ACC lower-director trace: effective target/measured surface acceleration, manual-ACC BaseThrottle equilibrium adaptation, VEL precision deadband and reversible throttle bias, zero-acceleration fine trim, throttle correction/slew, q schedule, persistent thrust/coast saturation awareness, VEL full-coast bias authority, AERIS native throttle request, AA final throttle, deceleration shortfall, and optional variable-angle automatic airbrake demand/applied state.");
                w.WriteLine("VEL_DIAGNOSTICS=fdr_vel_diagnostics.csv; dedicated 50 Hz VEL upper-director trace: target/current surface speed, prediction lead, configured symmetric acceleration cap, desired/planned/published acceleration trajectory, hold state, q schedule, and the ACC handoff. VEL uses ACC for throttle; when the user enables automatic airbrakes, only Brakes-group ModuleControlSurface deployment angle is modulated after zero-throttle coast authority is exhausted.");
                w.WriteLine("GROUND_TAKEOFF_DIAGNOSTICS=fdr_ground_takeoff.csv; dedicated 50 Hz / 83-column integrated Ground Assist and Auto Takeoff trace: trajectory Brake Assist, Ground Stability allowance, measured deceleration/capability, Airbrake Link, Auto Stop/Parking Hold, throttle cut, APP demand-bus propulsion mode and re-ARM generation, plus reliable-ground/liftoff latch, post-touchdown session, current-heading target/error, shared pilot inputs, bounded native yaw/roll demands, forward-throttle cut/reverse-thrust exception, two-step takeoff phase, hybrid AA/manual Vr source and freeze, rotation interlocks, native pitch/throttle ownership, brakes and AA final outputs.");
                w.WriteLine("AA_COMPARISON_TELEMETRY=fdr_aa_comparison.csv plus fdr_aa_comparison_summary.csv; v0.6.3 independent default-ON Phase-2 AERIS-AA FlightState Crosscheck. Summary v2.2 separates contiguous observed duration from first-to-last span. It records Phase-1.1 state/freshness/source data plus observer-only condition labels and end-of-flight per-condition difference summaries. It never changes AERIS or AA control.");
                w.WriteLine("CVR=all AERIS logger events mirrored into this flight folder");
                w.WriteLine("FDR_AP_CHAIN=GROUND current-HDG target plus bounded pilot sharing -> AERIS yaw/roll rate demands -> AA native yaw/roll controllers -> AA final FlightCtrlState; AUTO TAKEOFF phase -> brake action group plus AERIS throttle/pitch-rate demands -> AA native pitch/throttle path -> confirmed liftoff -> current-HDG and V/S handoff; BANK target -> AERIS planned roll rate -> AA native RollAngularVelocityController demand -> AA final FlightCtrlState -> aircraft response; ALT target -> AERIS planned vertical-speed trajectory -> V/S trajectory director -> fixed low-q precision desired-acceleration guide -> AERIS planned pitch rate -> AA native PitchAngularVelocityController demand -> AA final FlightCtrlState -> aircraft response; HDG yaw/vertical-turn plan -> AERIS planned yaw and bounded pitch rates -> AA native angular-velocity controllers -> AA final FlightCtrlState -> aircraft response; VEL target -> AERIS jerk-limited target-acceleration trajectory -> ACC target -> AERIS BaseThrottle plus measured-acceleration correction -> AA external throttle owner -> AA final FlightCtrlState.mainThrottle -> propulsion response; optional SPEED airbrake controller -> Brakes-group control-surface deploy angle only");
                w.WriteLine("BANK_CONVENTION=right-positive; left-negative; target range -90..+90 deg");
                w.WriteLine("BANK_CONTROL=dynamic-pressure-scheduled desired bank rate + causal 5 Hz filtered H-BANK derivative feedback + AERIS-only motion planning + AA native roll-rate demand; at every valid altitude a bounded precision corridor removes residual bank error with a small native rate demand and a zero-demand neutral band; AERIS neutralizes the owned pilot roll input before AA reads it.");
                w.WriteLine("BANK_RATE_TRACE=control uses the causal 5 Hz filtered H-BANK derivative exposed as bank_actual_roll_rate_deg_s; raw single-frame and causal 0.24 s least-squares trend rates are diagnostics-only");
                w.WriteLine("VIRTUAL_ATTITUDE_INSTRUMENT=v0.4.27 read-only flight-state publisher; BankWrappedDeg preserves continuous quaternion-derived roll history; HorizonBankDeg is the direct local-gravity/active-control-frame wing-bank reference used by BANK AP and is invalid near vertical flight; both use player-visible right-positive / left-negative convention; PitchDeg uses active-control longitudinal axis versus local gravity; HeadingDeg uses local north from the active body rotation axis and is invalid near vertical flight or either pole; standard FDR/API expose formal flight-state outputs only; validation-only KSP comparison telemetry is no longer part of the standard recorder contract; does not write FlightCtrlState");
                w.WriteLine("ATTITUDE_ESTIMATOR=quaternion-delta local-frame estimator; BANK publishes planned roll rate to AA's existing RollAngularVelocityController, PITCH publishes attitude-derived pitch rate, V/S builds a continuous V/S-generated pitch trajectory then directly tracks it as a planned pitch rate through AA's existing PitchAngularVelocityController, ALT v0.5.6 converts altitude error into a measured-rate-aware stopping-distance planned V/S trajectory, adds terminal-only bank-aware anti-sink support from observed HorizonBankDeg / roll transition / vertical-speed state, and hands that demand to V/S without overwriting the user V/S target; ALT defaults to ±50 m/s maximum V/S and an ALT-specific ±20 deg pitch cap, while V/S applies the lower of its manual and ALT pitch limits, and HDG publishes planned yaw rate to AA's existing YawAngularVelocityController only while their owned modes are active; owned pilot roll/pitch/yaw inputs are neutralized before AA reads them, but AERIS does not issue a command after AA or write final FlightCtrlState; PITCH arming preserves the prepared/applied target and only SET CURRENT captures the current pitch; ALT target arming likewise preserves the prepared/applied altitude and only SET CURRENT captures vessel altitude; HorizonBankDeg is BANK feedback; InstrumentPitchRateDegPerSec is PITCH feedback; InstrumentYawRateDegPerSec is HDG yaw-rate feedback; BANK owns roll, PITCH/V/S/ALT own pitch, HDG owns yaw, and ACC owns throttle only while SPEED/ACC or SPEED/VEL is armed; VEL only publishes an acceleration target into ACC; Ground Stability owns bounded yaw/roll during its protected ground/takeoff window; after a confirmed touchdown Ground Assist generates stability-allowed trajectory wheel braking, optional Brakes-group airbrake deployment, Auto Stop/Parking Hold and a zero-forward-throttle ceiling unless reverse-thrust control is active; Auto Takeoff exclusively owns brakes, throttle and pitch during its execution window, while gear/flaps remain manual and Auto Gear is transiently inhibited. Inactive/invalid/rails/unowned ground states clear native AA overrides.");
            }
        }

        void WriteSummary(string reason)
        {
            if (string.IsNullOrEmpty(folder)) return;
            using (var w = new AERISAsyncFileChannel(Path.Combine(folder, "summary.txt"), false,
                AERISFileRecordPriority.Continuous))
            {
                w.WriteLine("reason=" + reason);
                w.WriteLine("samples=" + sampleCount);
                w.WriteLine("events=" + eventCount);
                w.WriteLine("max_speed_mps=" + F(maxSpeed));
                w.WriteLine("max_abs_aoa_deg=" + F(maxAoA));
                w.WriteLine("max_abs_g=" + F(maxG));
                w.WriteLine("protect_interventions=" + protectInterventions);
            }
        }

        // AA Comparison Telemetry is intentionally a pure observer. It reads AERIS formal
        // instrument values and AA FlightModel/reference-frame values into a separate CSV;
        // no value from this method is returned to a controller or written to FlightCtrlState.
        // v0.6.0 Phase 2 keeps this as an observer only. It adds condition labels and
        // flight-end summary statistics for comparative analysis; no control value is returned
        // or written from this recorder.
        void SampleAaComparison(Vessel vessel, ProtectTelemetry protect, AERISBankDirector bank,
            AERISPitchDirector pitch, AERISVerticalSpeedDirector vs, AERISAltitudeDirector alt, AERISHdgDirector hdg,
            AERISAccelerationDirector acc, AERISVelocityDirector vel, VirtualAttitudeInstrument attitude, TopModuleManager manager, bool master, bool enabled)
        {
            if (!enabled)
            {
                CloseAaComparisonWriter();
                return;
            }
            if (vessel == null || string.IsNullOrEmpty(folder)) return;

            float nan = float.NaN;
            float fixedNow = Time.fixedTime;
            float fixedDt = TimeWarp.fixedDeltaTime;
            var fm = manager != null ? manager.FlightModel : null;
            bool aaAvailable = fm != null;
            float aaLastUpdate = aaAvailable ? fm.LastModelUpdateFixedTime : nan;
            float aaSampleAge = aaAvailable && aaLastUpdate >= 0f ? Mathf.Max(0f, fixedNow - aaLastUpdate) : nan;
            bool aaStateValid = aaAvailable && fm.ModelUpdateSequence > 0 && aaLastUpdate >= 0f && aaSampleAge <= 0.50f;
            bool aaWarmupComplete = aaStateValid && fm.ModelUpdateSequence >= AaComparisonWarmupModelUpdates;

            bool aaReferenceAttitudeValid;
            bool aaReferenceHeadingValid;
            float aaReferencePitch;
            float aaReferenceRoll;
            float aaReferenceHeading;
            ReadAttitudeFromRotation(vessel, vessel.ReferenceTransform != null ? vessel.ReferenceTransform.rotation : Quaternion.identity,
                out aaReferenceAttitudeValid, out aaReferenceHeadingValid,
                out aaReferencePitch, out aaReferenceRoll, out aaReferenceHeading);

            bool aaVirtualAttitudeValid = false;
            bool aaVirtualHeadingValid = false;
            float aaVirtualPitch = nan;
            float aaVirtualRoll = nan;
            float aaVirtualHeading = nan;
            if (aaAvailable)
            {
                ReadAttitudeFromRotation(vessel, fm.virtualRotation,
                    out aaVirtualAttitudeValid, out aaVirtualHeadingValid,
                    out aaVirtualPitch, out aaVirtualRoll, out aaVirtualHeading);
            }

            float aaPitchRate = aaAvailable ? fm.AngularVel(AutopilotModule.PITCH) * Mathf.Rad2Deg : nan;
            float aaRollRate = aaAvailable ? fm.AngularVel(AutopilotModule.ROLL) * Mathf.Rad2Deg : nan;
            float aaYawRate = aaAvailable ? fm.AngularVel(AutopilotModule.YAW) * Mathf.Rad2Deg : nan;
            float aaPitchAcc = aaAvailable ? (float)(fm.AngularAcc(AutopilotModule.PITCH) * Mathf.Rad2Deg) : nan;
            float aaRollAcc = aaAvailable ? (float)(fm.AngularAcc(AutopilotModule.ROLL) * Mathf.Rad2Deg) : nan;
            float aaYawAcc = aaAvailable ? (float)(fm.AngularAcc(AutopilotModule.YAW) * Mathf.Rad2Deg) : nan;
            float aaPitchAoA = aaAvailable ? fm.AoA(AutopilotModule.PITCH) * Mathf.Rad2Deg : nan;
            float aaRollAoA = aaAvailable ? fm.AoA(AutopilotModule.ROLL) * Mathf.Rad2Deg : nan;
            float aaYawAoA = aaAvailable ? fm.AoA(AutopilotModule.YAW) * Mathf.Rad2Deg : nan;
            // AA stores rho*v^2 in Pa. Normalize to conventional q=0.5*rho*v^2 in kPa.
            float aaDynamicPressure = aaAvailable ? (float)(0.5d * fm.dyn_pressure / 1000d) : nan;
            float aaDensity = aaAvailable ? (float)vessel.atmDensity : nan;
            float aaSurfaceSpeed = aaAvailable ? (float)fm.surface_v_magnitude : nan;
            float aaVerticalSpeed = aaAvailable && vessel.mainBody != null
                ? (float)Vector3d.Dot(fm.surface_v, (vessel.CoM - vessel.mainBody.position).normalized) : nan;
            float aaAltitude = aaAvailable ? (float)vessel.altitude : nan;
            float fallbackRadarAltitude = vessel != null ? Mathf.Max(0f, (float)vessel.heightFromTerrain) : nan;
            // Stock KSP has no atmospheric wind model. In an atmosphere, surface-relative
            // velocity is therefore the available true-air-speed representation for both sides.
            bool trueAirSpeedAvailable = vessel != null && vessel.atmDensity > 0.000001d;
            float quaternionDifference = aaAvailable && vessel.ReferenceTransform != null
                ? Quaternion.Angle(vessel.ReferenceTransform.rotation, fm.virtualRotation) : nan;

            bool aerisStateValid = attitude != null && attitude.InstrumentValid;
            float aerisSampleAge = attitude != null ? attitude.SampleAgeSeconds : nan;
            float aerisLastSample = attitude != null ? attitude.LastSampleFixedTime : nan;
            float aerisPitch = attitude != null && attitude.InstrumentPitchValid ? attitude.InstrumentPitchDeg : nan;
            float aerisRoll = attitude != null && attitude.InstrumentHorizonBankValid ? attitude.InstrumentHorizonBankDeg : nan;
            float aerisHeading = attitude != null && attitude.InstrumentHeadingValid ? attitude.InstrumentHeadingDeg : nan;
            float aerisPitchRate = attitude != null ? attitude.InstrumentPitchRateDegPerSec : nan;
            float aerisRollRate = attitude != null ? attitude.InstrumentRollRateDegPerSec : nan;
            float aerisYawRate = attitude != null ? attitude.InstrumentYawRateDegPerSec : nan;
            float aerisPitchAcc = attitude != null && attitude.InstrumentAngularAccelerationValid ? attitude.InstrumentPitchAccelerationDegPerSec2 : nan;
            float aerisRollAcc = attitude != null && attitude.InstrumentAngularAccelerationValid ? attitude.InstrumentRollAccelerationDegPerSec2 : nan;
            float aerisYawAcc = attitude != null && attitude.InstrumentAngularAccelerationValid ? attitude.InstrumentYawAccelerationDegPerSec2 : nan;
            bool aerisCommonKinematicBaselineValid = attitude != null && attitude.CommonKinematicBaselineValid;
            string aerisCommonKinematicBaselineSource = attitude != null ? attitude.CommonKinematicBaselineSource : "UNAVAILABLE";
            float aerisSurfaceSpeed = attitude != null ? attitude.SurfaceSpeedMps : nan;
            float aerisVerticalSpeed = attitude != null ? attitude.VerticalSpeedMps : nan;
            float aerisAltitude = attitude != null ? attitude.AltitudeAslM : (float)vessel.altitude;
            float radarAltitude = attitude != null ? attitude.RadarAltitudeM : fallbackRadarAltitude;
            float aerisQ = attitude != null ? attitude.DynamicPressureKpa : nan;
            float aerisDensity = attitude != null ? attitude.DensityKgM3 : nan;
            bool aerisAoAValid = attitude != null && attitude.EstimatedAoAValid;
            float aerisPitchAoA = aerisAoAValid ? attitude.EstimatedPitchAoADeg : nan;
            float aerisRollAoA = aerisAoAValid ? attitude.EstimatedRollAoADeg : nan;
            float aerisYawAoA = aerisAoAValid ? attitude.EstimatedYawAoADeg : nan;
            float aerisTrueAirSpeed = trueAirSpeedAvailable ? aerisSurfaceSpeed : nan;
            float aaTrueAirSpeed = aaAvailable && trueAirSpeedAvailable ? aaSurfaceSpeed : nan;
            bool comparisonReady = aerisStateValid && aaWarmupComplete && aaReferenceAttitudeValid && aaVirtualAttitudeValid;
            string comparisonExclusionReason = ComparisonExclusionReason(
                aerisStateValid, aaAvailable, aaStateValid, aaWarmupComplete,
                aaReferenceAttitudeValid, aaVirtualAttitudeValid);
            float comparisonSampleTimeDiff = Difference(aerisLastSample, aaLastUpdate);

            string lateralMode = hdg != null && hdg.Armed ? "HDG" : (bank != null && bank.Armed ? "BANK" : "NONE");
            string verticalMode = alt != null && alt.Armed ? "ALT" : (vs != null && vs.Armed ? "V/S" : (pitch != null && pitch.Armed ? "PITCH" : "NONE"));
            string dynamicPressureBand = bank != null ? bank.DynamicPressureMode : DynamicPressureBand(aerisQ);
            string speedMode = vel != null && vel.Armed ? "VEL" : (acc != null && acc.Armed ? "ACC" : "NONE");
            string combinedMode = lateralMode + "/" + verticalMode + "/" + speedMode;

            // Phase 2 classifications are derived from already-observed state only. They are
            // written for conditional analysis and are never read by a controller.
            string analysisFlightRegime = ClassifyFlightRegime(vessel);
            string analysisLateralRegime = ClassifyLateralRegime(bank, hdg, aerisRoll, aerisRollRate);
            string analysisVerticalRegime = ClassifyVerticalRegime(pitch, vs, alt);
            string analysisTurnRegime = ClassifyTurnRegime(aerisRoll, aerisRollRate);
            string analysisSpeedRegime = ClassifySpeedRegime(aerisSurfaceSpeed);
            string analysisDynamicPressureRegime = ClassifyDynamicPressureRegime(aerisQ, vessel);
            string analysisAltitudeRegime = ClassifyAltitudeRegime(aerisAltitude, vessel);
            string analysisManeuverRegime = ClassifyManeuverRegime(aerisRollRate, aerisPitchRate, aerisYawRate);
            string analysisConditionKey = BuildAnalysisConditionKey(analysisFlightRegime, analysisLateralRegime, analysisVerticalRegime,
                analysisTurnRegime, analysisSpeedRegime, analysisDynamicPressureRegime, analysisAltitudeRegime, analysisManeuverRegime);
            bool analysisSummaryEligible = comparisonReady;

            if (analysisSummaryEligible)
            {
                AccumulateAaComparisonSummary(analysisConditionKey, analysisFlightRegime, analysisLateralRegime, analysisVerticalRegime,
                    analysisTurnRegime, analysisSpeedRegime, analysisDynamicPressureRegime, analysisAltitudeRegime, analysisManeuverRegime,
                    fixedNow, AngleDifference(aerisPitch, aaReferencePitch), AngleDifference(aerisRoll, aaReferenceRoll),
                    HeadingDifference(aerisHeading, aaReferenceHeading), Difference(aerisPitchRate, aaPitchRate),
                    Difference(aerisRollRate, aaRollRate), Difference(aerisYawRate, aaYawRate),
                    Difference(aerisVerticalSpeed, aaVerticalSpeed), AngleDifference(aerisPitchAoA, aaPitchAoA),
                    AngleDifference(aerisRollAoA, aaRollAoA), AngleDifference(aerisYawAoA, aaYawAoA), quaternionDifference);
            }

            var values = new List<AERISCsvField>(AaComparisonHeader.Length);
            values.Add(Utc(DateTime.UtcNow));
            values.Add(F(Planetarium.GetUniversalTime()));
            values.Add(F(fixedNow));
            values.Add(F(fixedDt));
            values.Add(Csv(vessel.vesselName));
            values.Add(Csv(vessel.id.ToString()));
            values.Add(Csv(vessel.situation.ToString()));
            values.Add(Csv(vessel.mainBody != null ? vessel.mainBody.bodyName : "unknown"));
            values.Add(Csv(combinedMode));
            values.Add(B(enabled));
            values.Add(B(comparisonReady));
            values.Add(Csv(comparisonExclusionReason));
            values.Add(F(comparisonSampleTimeDiff));
            values.Add(B(aerisStateValid));
            values.Add(F(aerisSampleAge));
            values.Add(F(aerisLastSample));
            values.Add(B(aaAvailable));
            values.Add(B(aaStateValid));
            values.Add(F(aaSampleAge));
            values.Add(F(aaLastUpdate));
            values.Add(aaAvailable ? (long)fm.ModelUpdateSequence : 0L);
            values.Add(B(aaWarmupComplete));
            values.Add(B(aaReferenceAttitudeValid));
            values.Add(B(aaReferenceHeadingValid));
            values.Add(B(aaVirtualAttitudeValid));
            values.Add(B(aaVirtualHeadingValid));
            values.Add(B(master));
            values.Add(Csv(lateralMode));
            values.Add(Csv(verticalMode));
            values.Add(Csv(speedMode));
            values.Add(B(bank != null && bank.Armed));
            values.Add(B(hdg != null && hdg.Armed));
            values.Add(B(pitch != null && pitch.Armed));
            values.Add(B(vs != null && vs.ControlActive));
            values.Add(B(alt != null && alt.ControlActive));
            values.Add(B(vel != null && vel.ControlActive));
            values.Add(B(acc != null && acc.ControlActive));
            values.Add(F(bank != null ? bank.TargetBank : 0f));
            values.Add(F(hdg != null ? hdg.TargetHeading : 0f));
            values.Add(F(vs != null && vs.Armed ? vs.GeneratedPitchTargetDeg : (pitch != null ? pitch.TargetPitch : 0f)));
            values.Add(F(vs != null ? vs.RequestedTargetVerticalSpeedMps : 0f));
            values.Add(F(alt != null ? alt.TargetAltitudeMeters : 0f));
            values.Add(F(vel != null ? vel.TargetSurfaceSpeedMps : 0f));
            values.Add(F(aerisRoll));
            values.Add(F(aerisHeading));
            values.Add(F(aerisPitch));
            values.Add(F(aerisVerticalSpeed));
            values.Add(F(aerisAltitude));
            values.Add(F(radarAltitude));
            values.Add(F(aerisSurfaceSpeed));
            values.Add(Csv(dynamicPressureBand));
            values.Add(F(protect != null ? protect.StallMarginDegrees : nan));
            values.Add(B(protect != null && protect.ProtectActive));
            values.Add(F(aerisPitch));
            values.Add(F(aaReferencePitch));
            values.Add(F(AngleDifference(aerisPitch, aaReferencePitch)));
            values.Add(F(aerisRoll));
            values.Add(F(aaReferenceRoll));
            values.Add(F(AngleDifference(aerisRoll, aaReferenceRoll)));
            values.Add(F(aerisHeading));
            values.Add(F(aaReferenceHeading));
            values.Add(F(HeadingDifference(aerisHeading, aaReferenceHeading)));
            values.Add(B(aerisStateValid));
            values.Add(F(aerisPitch));
            values.Add(F(aaVirtualPitch));
            values.Add(F(AngleDifference(aerisPitch, aaVirtualPitch)));
            values.Add(F(aerisRoll));
            values.Add(F(aaVirtualRoll));
            values.Add(F(AngleDifference(aerisRoll, aaVirtualRoll)));
            values.Add(F(aerisHeading));
            values.Add(F(aaVirtualHeading));
            values.Add(F(HeadingDifference(aerisHeading, aaVirtualHeading)));
            values.Add(F(quaternionDifference));
            values.Add(F(aerisPitchRate));
            values.Add(F(aaPitchRate));
            values.Add(F(Difference(aerisPitchRate, aaPitchRate)));
            values.Add(F(aerisRollRate));
            values.Add(F(aaRollRate));
            values.Add(F(Difference(aerisRollRate, aaRollRate)));
            values.Add(F(aerisYawRate));
            values.Add(F(aaYawRate));
            values.Add(F(Difference(aerisYawRate, aaYawRate)));
            values.Add(F(aerisPitchAcc));
            values.Add(F(aaPitchAcc));
            values.Add(F(Difference(aerisPitchAcc, aaPitchAcc)));
            values.Add(F(aerisRollAcc));
            values.Add(F(aaRollAcc));
            values.Add(F(Difference(aerisRollAcc, aaRollAcc)));
            values.Add(F(aerisYawAcc));
            values.Add(F(aaYawAcc));
            values.Add(F(Difference(aerisYawAcc, aaYawAcc)));
            values.Add(F(aerisSurfaceSpeed));
            values.Add(F(aaSurfaceSpeed));
            values.Add(F(Difference(aerisSurfaceSpeed, aaSurfaceSpeed)));
            values.Add(B(trueAirSpeedAvailable));
            values.Add(F(aerisTrueAirSpeed));
            values.Add(F(aaTrueAirSpeed));
            values.Add(F(Difference(aerisTrueAirSpeed, aaTrueAirSpeed)));
            values.Add(Csv("Stock KSP has no wind model: vessel.srfSpeed and AA FlightModel.surface_v_magnitude are atmospheric/surface-relative aliases; unavailable in vacuum"));
            values.Add(F(aerisVerticalSpeed));
            values.Add(F(aaVerticalSpeed));
            values.Add(F(Difference(aerisVerticalSpeed, aaVerticalSpeed)));
            values.Add(F(aerisAltitude));
            values.Add(F(aaAltitude));
            values.Add(F(Difference(aerisAltitude, aaAltitude)));
            values.Add(B(vessel != null));
            values.Add(F(radarAltitude));
            values.Add(F(radarAltitude));
            values.Add(F(0f));
            values.Add(F(aerisQ));
            values.Add(F(aaDynamicPressure));
            values.Add(F(Difference(aerisQ, aaDynamicPressure)));
            values.Add(F(aerisDensity));
            values.Add(F(aaDensity));
            values.Add(F(Difference(aerisDensity, aaDensity)));
            values.Add(B(aerisAoAValid));
            values.Add(F(aerisPitchAoA));
            values.Add(F(aaPitchAoA));
            values.Add(F(AngleDifference(aerisPitchAoA, aaPitchAoA)));
            values.Add(F(aerisRollAoA));
            values.Add(F(aaRollAoA));
            values.Add(F(AngleDifference(aerisRollAoA, aaRollAoA)));
            values.Add(F(aerisYawAoA));
            values.Add(F(aaYawAoA));
            values.Add(F(AngleDifference(aerisYawAoA, aaYawAoA)));
            values.Add(B(aerisCommonKinematicBaselineValid));
            values.Add(Csv(aerisCommonKinematicBaselineSource));
            values.Add(Csv("AERIS VirtualAttitudeInstrument control-frame geometric attitude; no smoothing"));
            values.Add(Csv("Vessel.ReferenceTransform geometric attitude"));
            values.Add(Csv("AA FlightModel.virtualRotation smoothed attitude"));
            values.Add(Csv("AERIS instrument-rate finite difference at FixedUpdate"));
            values.Add(Csv("AA FlightModel.AngularAcc(axis)"));
            values.Add(Csv("AERIS accepted shared-native baseline: vessel.srfSpeed; AA FlightModel.surface_v_magnitude is the stock no-wind alias"));
            values.Add(Csv("AA FlightModel.surface_v radial projection"));
            values.Add(Csv("AERIS native vessel.altitude; excluded from shared baseline because AA sample timing can lag one FixedUpdate during rapid vertical motion"));
            values.Add(Csv("AERIS accepted shared-native baseline: vessel.heightFromTerrain; AA has no independent radar altitude"));
            values.Add(Csv("AERIS accepted shared-native baseline q=0.5*rho*v^2; AA FlightModel.dyn_pressure is independently normalized for crosscheck"));
            values.Add(Csv("vessel.atmDensity shared KSP source; AA has no separate density field"));
            values.Add(Csv("AERIS unsmoothed control-frame geometry vs AA FlightModel.virtualRotation geometry"));
            values.Add(Csv("Mathf.DeltaAngle(AA, AERIS), wrapped to [-180,+180] deg; AoA axis conventions may still differ"));
            values.Add(B(comparisonReady));
            values.Add(Csv(analysisFlightRegime));
            values.Add(Csv(analysisLateralRegime));
            values.Add(Csv(analysisVerticalRegime));
            values.Add(Csv(analysisTurnRegime));
            values.Add(Csv(analysisSpeedRegime));
            values.Add(Csv(analysisDynamicPressureRegime));
            values.Add(Csv(analysisAltitudeRegime));
            values.Add(Csv(analysisManeuverRegime));
            values.Add(Csv(analysisConditionKey));
            values.Add(B(analysisSummaryEligible));
            values.Add(Csv(AaComparisonSchema));

            if (values.Count != AaComparisonHeader.Length)
            {
                // A schema mismatch must never affect flight controls; suppress this telemetry sample.
                return;
            }
            lock (sync)
            {
                if (aaComparisonWriter == null)
                {
                    aaComparisonWriter = new AERISAsyncFileChannel(
                        Path.Combine(folder, "fdr_aa_comparison.csv"), false,
                        AERISFileRecordPriority.Verbose);
                    aaComparisonWriter.WriteHeader(AaComparisonHeader);
                }
                aaComparisonWriter.WriteCsv(values.ToArray());
                if (Time.realtimeSinceStartup >= nextFlush) aaComparisonWriter.Flush();
            }
        }

        // Phase 2 summary writer. It is called only when a flight ends, uses only ready rows,
        // and produces a separate analysis artifact. It has no route into any controller.
        void WriteAaComparisonConditionSummary()
        {
            if (string.IsNullOrEmpty(folder) || aaComparisonSummaries.Count == 0) return;
            string path = Path.Combine(folder, "fdr_aa_comparison_summary.csv");
            using (var writer = new AERISAsyncFileChannel(path, false,
                AERISFileRecordPriority.Verbose))
            {
                writer.WriteHeader(AaComparisonSummaryHeader);
                var keys = new List<string>(aaComparisonSummaries.Keys);
                keys.Sort(StringComparer.Ordinal);
                for (int i = 0; i < keys.Count; i++)
                {
                    AaComparisonConditionSummary summary;
                    if (!aaComparisonSummaries.TryGetValue(keys[i], out summary) || summary == null) continue;
                    float span = !float.IsNaN(summary.FirstFixedTime) && !float.IsNaN(summary.LastFixedTime)
                        ? Mathf.Max(0f, summary.LastFixedTime - summary.FirstFixedTime) : float.NaN;
                    var values = new List<AERISCsvField>(AaComparisonSummaryHeader.Length);
                    values.Add(Csv(AaComparisonSchema));
                    values.Add(Csv(summary.Key));
                    values.Add(Csv(summary.FlightRegime));
                    values.Add(Csv(summary.LateralRegime));
                    values.Add(Csv(summary.VerticalRegime));
                    values.Add(Csv(summary.TurnRegime));
                    values.Add(Csv(summary.SpeedRegime));
                    values.Add(Csv(summary.DynamicPressureRegime));
                    values.Add(Csv(summary.AltitudeRegime));
                    values.Add(Csv(summary.ManeuverRegime));
                    values.Add(summary.Samples);
                    values.Add(summary.RunCount);
                    values.Add(F(summary.ObservedDurationSeconds));
                    values.Add(F(summary.FirstFixedTime));
                    values.Add(F(summary.LastFixedTime));
                    values.Add(F(span));
                    AppendMetricSummary(values, summary.PitchReference);
                    AppendMetricSummary(values, summary.RollReference);
                    AppendMetricSummary(values, summary.HeadingReference);
                    AppendMetricSummary(values, summary.PitchRate);
                    AppendMetricSummary(values, summary.RollRate);
                    AppendMetricSummary(values, summary.YawRate);
                    AppendMetricSummary(values, summary.VerticalSpeed);
                    AppendMetricSummary(values, summary.PitchAoA);
                    AppendMetricSummary(values, summary.RollAoA);
                    AppendMetricSummary(values, summary.YawAoA);
                    AppendMetricSummary(values, summary.ReferenceVirtualQuaternion);
                    if (values.Count == AaComparisonSummaryHeader.Length)
                        writer.WriteCsv(values.ToArray());
                }
            }
        }

        static void AppendMetricSummary(List<AERISCsvField> values, AaComparisonMetric metric)
        {
            values.Add(F(metric != null ? metric.MeanAbs : float.NaN));
            values.Add(F(metric != null ? metric.Rms : float.NaN));
            values.Add(F(metric != null ? metric.MaxAbs : float.NaN));
        }

        void AccumulateAaComparisonSummary(string key, string flightRegime, string lateralRegime, string verticalRegime,
            string turnRegime, string speedRegime, string dynamicPressureRegime, string altitudeRegime, string maneuverRegime,
            float fixedTime, float pitchReference, float rollReference, float headingReference, float pitchRate, float rollRate,
            float yawRate, float verticalSpeed, float pitchAoA, float rollAoA, float yawAoA, float referenceVirtualQuaternion)
        {
            AaComparisonConditionSummary summary;
            if (!aaComparisonSummaries.TryGetValue(key, out summary))
            {
                summary = new AaComparisonConditionSummary(key, flightRegime, lateralRegime, verticalRegime, turnRegime,
                    speedRegime, dynamicPressureRegime, altitudeRegime, maneuverRegime);
                aaComparisonSummaries.Add(key, summary);
            }
            summary.Add(fixedTime, pitchReference, rollReference, headingReference, pitchRate, rollRate, yawRate, verticalSpeed,
                pitchAoA, rollAoA, yawAoA, referenceVirtualQuaternion);
        }

        static string ClassifyFlightRegime(Vessel vessel)
        {
            if (vessel == null) return "UNAVAILABLE";
            if (vessel.situation != Vessel.Situations.FLYING) return vessel.situation.ToString();
            return vessel.atmDensity > 0.000001d ? "FLYING_ATMOSPHERE" : "FLYING_VACUUM";
        }

        static string ClassifyLateralRegime(AERISBankDirector bank, AERISHdgDirector hdg, float bankDeg, float rollRateDegPerSec)
        {
            if (hdg != null && hdg.Armed)
            {
                return Mathf.Abs(hdg.HeadingError) > 1.0f || Mathf.Abs(rollRateDegPerSec) > 1.0f ? "HDG_TURN" : "HDG_HOLD";
            }
            if (bank != null && bank.Armed)
            {
                if (bank.CapturePhase == "Hold") return "BANK_HOLD";
                if (bank.CapturePhase == "Precision") return "BANK_PRECISION";
                return "BANK_" + (string.IsNullOrEmpty(bank.CapturePhase) ? "ACTIVE" : bank.CapturePhase);
            }
            return Mathf.Abs(bankDeg) >= 3.0f || Mathf.Abs(rollRateDegPerSec) >= 1.0f ? "MANUAL_TURN" : "MANUAL_STRAIGHT";
        }

        static string ClassifyVerticalRegime(AERISPitchDirector pitch, AERISVerticalSpeedDirector vs, AERISAltitudeDirector alt)
        {
            if (alt != null && alt.Armed)
            {
                if (alt.HoldLatched) return "ALT_HOLD";
                if (alt.PrecisionCorrectionActive) return "ALT_PRECISION";
                if (alt.RolloutActive) return "ALT_ROLLOUT";
                return "ALT_CAPTURE";
            }
            if (vs != null && vs.ControlActive)
            {
                return string.IsNullOrEmpty(vs.PrecisionPhase) || vs.PrecisionPhase == "MainTrajectory"
                    ? "VS_MAIN" : "VS_" + vs.PrecisionPhase;
            }
            if (pitch != null && pitch.Armed) return "PITCH";
            return "MANUAL_VERTICAL";
        }

        static string ClassifyTurnRegime(float bankDeg, float rollRateDegPerSec)
        {
            float absoluteBank = Mathf.Abs(bankDeg);
            float absoluteRate = Mathf.Abs(rollRateDegPerSec);
            if (absoluteBank < 3f && absoluteRate < 1f) return "LEVEL";
            if (absoluteBank < 15f) return "GENTLE";
            if (absoluteBank < 35f) return "MODERATE";
            return "STEEP";
        }

        static string ClassifySpeedRegime(float speedMps)
        {
            if (float.IsNaN(speedMps) || float.IsInfinity(speedMps)) return "UNAVAILABLE";
            if (speedMps < 50f) return "LOW";
            if (speedMps < 150f) return "MID";
            if (speedMps < 300f) return "HIGH";
            return "VERY_HIGH";
        }

        static string ClassifyDynamicPressureRegime(float qKpa, Vessel vessel)
        {
            if (vessel != null && vessel.atmDensity <= 0.000001d) return "VACUUM";
            if (float.IsNaN(qKpa) || float.IsInfinity(qKpa) || qKpa < 0f) return "UNAVAILABLE";
            if (qKpa < 1.5f) return "LOW_Q";
            if (qKpa < 12f) return "MID_Q";
            return "HIGH_Q";
        }

        static string ClassifyAltitudeRegime(float altitudeMeters, Vessel vessel)
        {
            if (vessel != null && vessel.situation != Vessel.Situations.FLYING) return "GROUND_OR_RAILS";
            if (float.IsNaN(altitudeMeters) || float.IsInfinity(altitudeMeters)) return "UNAVAILABLE";
            if (altitudeMeters < 8000f) return "LOW_ALT";
            if (altitudeMeters < 20000f) return "MID_ALT";
            return "HIGH_ALT";
        }

        static string ClassifyManeuverRegime(float rollRateDegPerSec, float pitchRateDegPerSec, float yawRateDegPerSec)
        {
            float maximum = Mathf.Max(Mathf.Abs(rollRateDegPerSec), Mathf.Max(Mathf.Abs(pitchRateDegPerSec), Mathf.Abs(yawRateDegPerSec)));
            if (float.IsNaN(maximum) || float.IsInfinity(maximum)) return "UNAVAILABLE";
            if (maximum < 2f) return "QUIET";
            if (maximum < 12f) return "MANEUVER";
            return "AGGRESSIVE";
        }

        static string BuildAnalysisConditionKey(string flightRegime, string lateralRegime, string verticalRegime, string turnRegime,
            string speedRegime, string dynamicPressureRegime, string altitudeRegime, string maneuverRegime)
        {
            return "flight=" + flightRegime + "|lateral=" + lateralRegime + "|vertical=" + verticalRegime +
                "|turn=" + turnRegime + "|speed=" + speedRegime + "|q=" + dynamicPressureRegime +
                "|alt=" + altitudeRegime + "|maneuver=" + maneuverRegime;
        }

        void CloseAaComparisonWriter()
        {
            lock (sync)
            {
                if (aaComparisonWriter == null) return;
                try { aaComparisonWriter.Flush(); aaComparisonWriter.Dispose(); } catch { }
                aaComparisonWriter = null;
            }
        }

        static void ReadAttitudeFromRotation(Vessel vessel, Quaternion rotation, out bool valid, out bool headingValid,
            out float pitchDeg, out float rollDeg, out float headingDeg)
        {
            valid = false;
            headingValid = false;
            pitchDeg = rollDeg = headingDeg = float.NaN;
            if (vessel == null || vessel.mainBody == null) return;
            Vector3 gravityUp = ((Vector3)(vessel.CoM - vessel.mainBody.position)).normalized;
            if (gravityUp.sqrMagnitude < 0.0001f) return;

            Vector3 longitudinal = (rotation * Vector3.up).normalized;
            Vector3 lateral = (rotation * Vector3.right).normalized;
            Vector3 nominalUp = (rotation * -Vector3.forward).normalized;
            pitchDeg = Mathf.Asin(Mathf.Clamp(Vector3.Dot(longitudinal, gravityUp), -1f, 1f)) * Mathf.Rad2Deg;
            float horizontal = Vector3.ProjectOnPlane(longitudinal, gravityUp).magnitude;
            rollDeg = horizontal > 0.02f
                ? NormalizeSigned(-Mathf.Atan2(Vector3.Dot(lateral, gravityUp), Vector3.Dot(nominalUp, gravityUp)) * Mathf.Rad2Deg)
                : float.NaN;
            valid = !float.IsNaN(pitchDeg) && !float.IsNaN(rollDeg) && !float.IsInfinity(pitchDeg) && !float.IsInfinity(rollDeg);

            Vector3 forwardHorizontal = Vector3.ProjectOnPlane(longitudinal, gravityUp);
            Vector3d upD = (vessel.CoM - vessel.mainBody.position).normalized;
            Vector3 localNorth = (Vector3)Vector3d.Exclude(upD, vessel.mainBody.RotationAxis);
            if (forwardHorizontal.sqrMagnitude > 0.0004f && localNorth.sqrMagnitude > 0.0004f)
            {
                forwardHorizontal.Normalize();
                localNorth.Normalize();
                headingDeg = Mathf.Repeat(Vector3.SignedAngle(localNorth, forwardHorizontal, gravityUp), 360f);
                headingValid = !float.IsNaN(headingDeg) && !float.IsInfinity(headingDeg);
            }
        }

        static string ComparisonExclusionReason(bool aerisStateValid, bool aaAvailable, bool aaStateValid,
            bool aaWarmupComplete, bool aaReferenceAttitudeValid, bool aaVirtualAttitudeValid)
        {
            if (!aerisStateValid) return "AERIS_STATE_INVALID";
            if (!aaAvailable) return "AA_MODEL_UNAVAILABLE";
            if (!aaStateValid) return "AA_MODEL_STALE_OR_UNINITIALIZED";
            if (!aaWarmupComplete) return "AA_WARMING_UP";
            if (!aaReferenceAttitudeValid) return "AA_REFERENCE_ATTITUDE_INVALID";
            if (!aaVirtualAttitudeValid) return "AA_VIRTUAL_ATTITUDE_INVALID";
            return "READY";
        }

        static float Difference(float aeris, float aa)
        {
            return float.IsNaN(aeris) || float.IsNaN(aa) ? float.NaN : aeris - aa;
        }

        static float HeadingDifference(float aeris, float aa)
        {
            return float.IsNaN(aeris) || float.IsNaN(aa) ? float.NaN : Mathf.DeltaAngle(aa, aeris);
        }

        static float AngleDifference(float aeris, float aa)
        {
            return float.IsNaN(aeris) || float.IsNaN(aa) ? float.NaN : Mathf.DeltaAngle(aa, aeris);
        }

        static string DynamicPressureBand(float qKpa)
        {
            if (float.IsNaN(qKpa) || qKpa < 0f) return "UNAVAILABLE";
            if (qKpa < 1.5f) return "LOW";
            if (qKpa < 12f) return "MID";
            return "HIGH";
        }

        static string Key(string providerId, string channelId) { return providerId + "|" + channelId; }
        AERISAsyncFileChannel OpenDiagnostic(string fileName)
        {
            return new AERISAsyncFileChannel(Path.Combine(folder, fileName), false,
                AERISFileRecordPriority.Verbose);
        }
        bool CoreChannelsAvailable()
        {
            return cvrWriter != null && cvrWriter.Available &&
                fdrWriter != null && fdrWriter.Available &&
                bankDiagnosticsWriter != null && bankDiagnosticsWriter.Available &&
                apSmoothnessWriter != null && apSmoothnessWriter.Available &&
                vsDiagnosticsWriter != null && vsDiagnosticsWriter.Available &&
                vsCruiseAccelerationGuideWriter != null &&
                    vsCruiseAccelerationGuideWriter.Available &&
                pitchDiagnosticsWriter != null && pitchDiagnosticsWriter.Available &&
                hdgDiagnosticsWriter != null && hdgDiagnosticsWriter.Available &&
                altDiagnosticsWriter != null && altDiagnosticsWriter.Available &&
                accelerationDiagnosticsWriter != null &&
                    accelerationDiagnosticsWriter.Available &&
                velocityDiagnosticsWriter != null && velocityDiagnosticsWriter.Available &&
                groundTakeoffDiagnosticsWriter != null &&
                    groundTakeoffDiagnosticsWriter.Available;
        }
        static AERISCsvField[] CaptureCsv(AERISCsvField[] fields)
        {
            return fields ?? new AERISCsvField[0];
        }
        static string StableHash8(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= 16777619u;
                }
                return hash.ToString("x8", CultureInfo.InvariantCulture);
            }
        }
        static string SafeToken(string value) { return string.IsNullOrEmpty(value) ? "unknown" : value.Replace(";", "_").Replace("=", "_"); }
        static string SafeFile(string value) { return Sanitize(value ?? "unknown"); }
        static string CsvHeader(string value)
        {
            string result = string.IsNullOrEmpty(value) ? "unknown" : value;
            return result.Replace(',', '_').Replace('\r', '_').Replace('\n', '_')
                .Replace('"', '_');
        }
        static AERISCsvField F(double value) { return AERISCsvField.Fixed(value); }
        static AERISCsvField B(bool value) { return AERISCsvField.Flag(value); }
        static AERISCsvField Utc(DateTime value) { return AERISCsvField.Utc(value); }
        static float NormalizeSigned(float value) { return value > 180f ? value - 360f : value; }
        static AERISCsvField Csv(string value)
        {
            return AERISCsvField.Quoted(value);
        }
        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown-vessel";
            foreach (char c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '_');
            value = value.Replace(' ', '_');
            return value.Length <= 80 ? value : value.Substring(0, 80);
        }
    }
}
