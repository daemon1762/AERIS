using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.API;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Core
{
    internal sealed partial class AERISExternalAutomationManager
    {
        const string MissionGroundTest = "GROUND_PROPULSION_TEST";
        const string MissionAutoTakeoff = "AUTO_TAKEOFF";
        const string MissionCorridor = "LEARNING_CORRIDOR";
        const string MissionEnvelope = "ENVELOPE_SURVEY";
        const string MissionAntiStall = "ANTI_STALL_SURVEY";
        const string MissionClimb = "CLIMB";

        sealed class TimedSpeedSample
        {
            internal float Time;
            internal float Speed;
        }

        sealed class V2Runtime
        {
            internal string MissionKind = string.Empty;
            internal string Phase = "IDLE";
            internal float Started;
            internal float PhaseSince;
            internal float StableSince = -1f;
            internal float FailureSince = -1f;
            internal float PreviousSpeed;
            internal float PreviousSpeedTime;
            internal float ForwardAcceleration;
            internal float ObservedMaxSpeed;
            internal float ObservedAntiStallSpeed;
            internal float AntiStallActiveSince = -1f;
            internal bool AntiStallEventLatched;
            internal bool RecoveryCommanded;
            internal float MissionTimeout;
            internal float StartLatitude;
            internal float StartLongitude;
            internal float LegStartLatitude;
            internal float LegStartLongitude;
            internal float BaseHeading;
            internal float CorridorLegLength;
            internal float CorridorHalfWidth;
            internal float CorridorTurnMargin;
            internal float MinimumTerrainClearance;
            internal float CorridorCruiseSpeed;
            internal float CorridorTurnSpeed;
            internal float CorridorTurnRadius;
            internal float CorridorRequiredTurnReserve;
            internal bool TurnPreparation;
            internal float TurnStartLatitude;
            internal float TurnStartLongitude;
            internal float TurnTargetHeading;
            internal float TurnDirectionSign;
            internal int CorridorPassIndex;
            internal string CorridorDirection = string.Empty;
            internal bool StraightCorridor;
            internal bool Turning;
            internal bool RouteRecapture;
            internal bool ObstacleAvoidance;
            internal float CrossTrackMeters;
            internal float AlongTrackMeters;
            internal float LastHeadingPublish;
            internal bool HdgLimitCaptured;
            internal bool PriorHdgAutoLimit;
            internal float PriorHdgManualLimit;
            internal float GroundStartLatitude;
            internal float GroundStartLongitude;
            internal float GroundTravelDistance;
            internal float GroundMaximumDistance;
            internal float GroundMaximumSpeed;
            internal float GroundPower;
            internal bool GroundMicroRoll;
            internal bool GroundStopping;
            internal float GroundStopSince = -1f;
            internal float GroundControlSaturationSince = -1f;
            internal float RequestedThrottle;
            internal float PredictedTakeoffDistance;
            internal float PredictedStopDistance;
            internal float RunwayRemaining;
            internal bool RunwayGeometryValid;
            internal AERISGroundPropulsionTestRequest GroundRequest;
            internal AERISAutoTakeoffMissionRequest TakeoffRequest;
            internal AERISLearningCorridorRequest CorridorRequest;
            internal AERISEnvelopeSurveyRequest EnvelopeRequest;
            internal AERISAntiStallSurveyRequest AntiStallRequest;
            internal AERISClimbMissionRequest ClimbRequest;
            internal readonly List<TimedSpeedSample> SpeedWindow = new List<TimedSpeedSample>();
        }

        sealed class V2AdvisoryState
        {
            internal bool TrimPublished;
            internal float TrimExpires;
            internal float TrimTargetRoll;
            internal float TrimTargetPitch;
            internal float TrimTargetYaw;
            internal float TrimAppliedRoll;
            internal float TrimAppliedPitch;
            internal float TrimAppliedYaw;
            internal float TrimConfidence;
            internal string TrimReason = string.Empty;
            internal bool TaskPublished;
            internal float TaskExpires;
            internal AERISExternalTaskDisplayRequest Task;
            internal bool ResourcePublished;
            internal float ResourceExpires;
            internal AERISResourceOverrideStatusRequest Resource;
        }

        readonly Dictionary<Guid, V2Runtime> v2Runtimes = new Dictionary<Guid, V2Runtime>();
        readonly Dictionary<Guid, V2AdvisoryState> v2Advisories = new Dictionary<Guid, V2AdvisoryState>();
        float authorityControl01;
        float authorityRoll01;
        float authorityPitch01;
        float authorityYaw01;
        bool authoritySaturated;
        static bool terrainMethodResolved;
        static MethodInfo terrainAltitudeMethod;

        internal AERISAutomationCapability[] GetCapabilities()
        {
            // Capability discovery reads live Unity/KSP director state. Return an empty
            // set off the Unity main thread rather than exposing unsafe engine access.
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId)
                return new AERISAutomationCapability[0];
            return AvailableCapabilities();
        }

        AERISAutomationCapability[] AvailableCapabilities()
        {
            var values = new List<AERISAutomationCapability>();
            if (core == null) return values.ToArray();

            bool flightGuidance = core.Hdg != null && core.Bank != null && core.Pitch != null &&
                core.VerticalSpeed != null && core.Altitude != null && core.Acceleration != null &&
                core.Velocity != null && core.Attitude != null;
            bool ground = core.GroundStability != null;
            bool protect = core.Protect != null;
            bool antiStall = protect && core.Protect.AntiStallEnabled;

            if (flightGuidance) AddCapability(values, AERISAutomationCapability.SetpointGuidance);
            if (ground && core.Acceleration != null && core.Attitude != null && protect)
                AddCapability(values, AERISAutomationCapability.GroundPropulsionTest);
            if (ground && core.AutoTakeoff != null && flightGuidance && antiStall)
                AddCapability(values, AERISAutomationCapability.AutoTakeoff);
            if (flightGuidance && antiStall && TerrainSamplingAvailable())
                AddCapability(values, AERISAutomationCapability.LearningCorridor);
            if (flightGuidance && antiStall)
                AddCapability(values, AERISAutomationCapability.EnvelopeSurvey);
            if (flightGuidance && antiStall)
                AddCapability(values, AERISAutomationCapability.AntiStallEvent);
            if (flightGuidance)
                AddCapability(values, AERISAutomationCapability.ControlAuthorityTelemetry);
            if (ground)
                AddCapability(values, AERISAutomationCapability.GroundAssistStop);

            // These facilities are implemented by the manager/AA/FDI integration in this build.
            AddCapability(values, AERISAutomationCapability.ExternalTrimFeedForward);
            AddCapability(values, AERISAutomationCapability.ExternalTaskDisplay);
            AddCapability(values, AERISAutomationCapability.ResourceOverrideCoordination);
            return values.ToArray();
        }

        static void AddCapability(List<AERISAutomationCapability> values,
            AERISAutomationCapability capability)
        {
            if (!ContainsCapability(values, capability)) values.Add(capability);
        }

        static bool ContainsCapability(List<AERISAutomationCapability> values,
            AERISAutomationCapability capability)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Count; i++) if (values[i] == capability) return true;
            return false;
        }

        static bool ContainsCapability(AERISAutomationCapability[] values,
            AERISAutomationCapability capability)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++) if (values[i] == capability) return true;
            return false;
        }

        static AERISAutomationCapability[] NormalizeCapabilities(
            AERISAutomationCapability[] requested)
        {
            var values = new List<AERISAutomationCapability>();
            if (requested == null) return values.ToArray();
            for (int i = 0; i < requested.Length; i++)
            {
                AERISAutomationCapability capability = requested[i];
                if (!Enum.IsDefined(typeof(AERISAutomationCapability), capability)) continue;
                AddCapability(values, capability);
            }
            return values.ToArray();
        }

        static string MissingCapabilities(AERISAutomationCapability[] requested,
            AERISAutomationCapability[] available)
        {
            var names = new List<string>();
            if (requested != null)
                for (int i = 0; i < requested.Length; i++)
                    if (!ContainsCapability(available, requested[i])) names.Add(requested[i].ToString());
            return names.Count == 0 ? string.Empty : string.Join(", ", names.ToArray());
        }

        static string CapabilityList(AERISAutomationCapability[] values)
        {
            if (values == null || values.Length == 0) return "NONE";
            var names = new string[values.Length];
            for (int i = 0; i < values.Length; i++) names[i] = values[i].ToString();
            return string.Join(",", names);
        }

        static AERISAutomationCapability[] CloneCapabilities(AERISAutomationCapability[] values)
        {
            if (values == null || values.Length == 0) return new AERISAutomationCapability[0];
            var clone = new AERISAutomationCapability[values.Length];
            Array.Copy(values, clone, values.Length);
            return clone;
        }

        // Public API request DTOs are mutable by design for broad KSP-mod compatibility.
        // Never retain a caller-owned instance after validation: a client changing the
        // object after submission must not be able to alter an accepted mission.
        static AERISGroundPropulsionTestRequest SnapshotGroundTestRequest(
            AERISGroundPropulsionTestRequest value)
        {
            if (value == null) return null;
            return new AERISGroundPropulsionTestRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                PowerDemand01 = value.PowerDemand01,
                PropellerNormalizedPitch = value.PropellerNormalizedPitch,
                BrakesInitiallyApplied = value.BrakesInitiallyApplied,
                GroundStabilityRequired = value.GroundStabilityRequired,
                RunwayHeadingHoldRequired = value.RunwayHeadingHoldRequired,
                MicroRoll = value.MicroRoll,
                MaximumDurationSeconds = value.MaximumDurationSeconds,
                MaximumGroundSpeedMps = value.MaximumGroundSpeedMps,
                MaximumTravelDistanceM = value.MaximumTravelDistanceM,
                AbortOnReverseThrust = value.AbortOnReverseThrust,
                AbortOnControlSaturation = value.AbortOnControlSaturation,
                CompletionRequiresFullStop = value.CompletionRequiresFullStop,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }

        static AERISAutoTakeoffMissionRequest SnapshotAutoTakeoffRequest(
            AERISAutoTakeoffMissionRequest value)
        {
            if (value == null) return null;
            return new AERISAutoTakeoffMissionRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                PropellerNormalizedPitch = value.PropellerNormalizedPitch,
                TargetAltitudeM = value.TargetAltitudeM, RouteId = value.RouteId,
                SpeedPolicy = value.SpeedPolicy, AutoStage = value.AutoStage,
                ProtectRequired = value.ProtectRequired,
                AntiStallRequired = value.AntiStallRequired,
                GroundStabilityRequired = value.GroundStabilityRequired,
                AllowAbort = value.AllowAbort,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }

        static AERISLearningCorridorRequest SnapshotLearningCorridorRequest(
            AERISLearningCorridorRequest value)
        {
            if (value == null) return null;
            return new AERISLearningCorridorRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId, RouteId = value.RouteId,
                BodyName = value.BodyName, AnchorPolicy = value.AnchorPolicy,
                TargetAltitudeM = value.TargetAltitudeM, LegLengthM = value.LegLengthM,
                CorridorHalfWidthM = value.CorridorHalfWidthM,
                MinimumTerrainClearanceM = value.MinimumTerrainClearanceM,
                TurnBankLimitDeg = value.TurnBankLimitDeg, TurnMarginM = value.TurnMarginM,
                ShuttlePattern = value.ShuttlePattern,
                ValidateTerrainBeforeStart = value.ValidateTerrainBeforeStart,
                ObstacleAvoidanceRequired = value.ObstacleAvoidanceRequired,
                ExpectedMaximumSpeedMps = value.ExpectedMaximumSpeedMps,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }

        static AERISEnvelopeSurveyRequest SnapshotEnvelopeRequest(
            AERISEnvelopeSurveyRequest value)
        {
            if (value == null) return null;
            return new AERISEnvelopeSurveyRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                AltitudeM = value.AltitudeM,
                PropellerNormalizedPitch = value.PropellerNormalizedPitch,
                RouteId = value.RouteId, MaximumSafePower = value.MaximumSafePower,
                AccelerationPlateauMps2 = value.AccelerationPlateauMps2,
                PlateauHoldSeconds = value.PlateauHoldSeconds,
                MaximumDurationSeconds = value.MaximumDurationSeconds,
                ProtectRequired = value.ProtectRequired,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }

        static AERISAntiStallSurveyRequest SnapshotAntiStallRequest(
            AERISAntiStallSurveyRequest value)
        {
            if (value == null) return null;
            return new AERISAntiStallSurveyRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                AltitudeM = value.AltitudeM,
                PropellerNormalizedPitch = value.PropellerNormalizedPitch,
                RouteId = value.RouteId, DecelerationMps2 = value.DecelerationMps2,
                MinimumSurveySpeedMps = value.MinimumSurveySpeedMps,
                AntiStallMustRemainEnabled = value.AntiStallMustRemainEnabled,
                StopOnAntiStallEvent = value.StopOnAntiStallEvent,
                RecoverAutomatically = value.RecoverAutomatically,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }

        static AERISClimbMissionRequest SnapshotClimbRequest(
            AERISClimbMissionRequest value)
        {
            if (value == null) return null;
            return new AERISClimbMissionRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                TargetAltitudeM = value.TargetAltitudeM,
                SoftTargetTrueAirspeedMps = value.SoftTargetTrueAirspeedMps,
                RouteId = value.RouteId,
                PrioritizeAltitudeAcquisition = value.PrioritizeAltitudeAcquisition,
                AllowSpeedReduction = value.AllowSpeedReduction,
                MaximumDurationSeconds = value.MaximumDurationSeconds,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }

        static AERISSetpointMissionRequest SnapshotSetpointRequest(
            AERISSetpointMissionRequest value)
        {
            if (value == null) return null;
            return new AERISSetpointMissionRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                AltitudeM = value.AltitudeM, TrueAirspeedMps = value.TrueAirspeedMps,
                RouteId = value.RouteId, RequireStraightCorridor = value.RequireStraightCorridor,
                RequireStableCondition = value.RequireStableCondition,
                AltitudeToleranceM = value.AltitudeToleranceM,
                SpeedToleranceMps = value.SpeedToleranceMps,
                VerticalSpeedToleranceMps = value.VerticalSpeedToleranceMps,
                BankToleranceDeg = value.BankToleranceDeg, StableSeconds = value.StableSeconds,
                AltitudeMeters = value.AltitudeMeters,
                SurfaceSpeedMps = value.SurfaceSpeedMps,
                HeadingDeg = Repeat360(value.HeadingDeg),
                UseExplicitHeading = value.UseExplicitHeading,
                HeadingToleranceDeg = value.HeadingToleranceDeg,
                ThrottleHint01 = Mathf.Clamp01(value.ThrottleHint01),
                CompletionPolicy = value.CompletionPolicy,
                ReplaceCurrentMission = value.ReplaceCurrentMission
            };
        }


        static AERISExternalTaskDisplayRequest SnapshotTaskDisplayRequest(
            AERISExternalTaskDisplayRequest value)
        {
            if (value == null) return null;
            return new AERISExternalTaskDisplayRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId, SourceId = value.SourceId,
                DisplayName = value.DisplayName, Task = value.Task, Phase = value.Phase,
                PrimaryStatus = value.PrimaryStatus, SecondaryStatus = value.SecondaryStatus,
                Progress01 = value.Progress01, ExpiresSeconds = value.ExpiresSeconds
            };
        }

        static AERISResourceOverrideStatusRequest SnapshotResourceStatusRequest(
            AERISResourceOverrideStatusRequest value)
        {
            if (value == null) return null;
            return new AERISResourceOverrideStatusRequest
            {
                Vessel = value.Vessel, VesselId = value.VesselId,
                OwnerClientId = value.OwnerClientId,
                InfinitePropellantActive = value.InfinitePropellantActive,
                InfiniteElectricityActive = value.InfiniteElectricityActive,
                Detail = value.Detail, ExpiresSeconds = value.ExpiresSeconds
            };
        }

        static AERISAutomationMissionState ToMissionState(AERISAutomationState state)
        {
            switch (state)
            {
                case AERISAutomationState.Acquired:
                case AERISAutomationState.Configuring:
                    return AERISAutomationMissionState.Accepted;
                case AERISAutomationState.Executing:
                case AERISAutomationState.Stabilizing:
                case AERISAutomationState.Stable:
                case AERISAutomationState.NavIntercept:
                case AERISAutomationState.NavEnroute:
                case AERISAutomationState.NavApproach:
                case AERISAutomationState.Landing:
                case AERISAutomationState.GroundRoll:
                    return AERISAutomationMissionState.Running;
                case AERISAutomationState.GoAround:
                    return AERISAutomationMissionState.Recovering;
                case AERISAutomationState.SuspendedByPilot:
                case AERISAutomationState.SuspendedByProtect:
                    return AERISAutomationMissionState.Suspended;
                case AERISAutomationState.Completed:
                    return AERISAutomationMissionState.Completed;
                case AERISAutomationState.Cancelled:
                case AERISAutomationState.LeaseExpired:
                    return AERISAutomationMissionState.Cancelled;
                case AERISAutomationState.Rejected:
                case AERISAutomationState.Faulted:
                    return AERISAutomationMissionState.Failed;
                default:
                    return AERISAutomationMissionState.Idle;
            }
        }

        static string CodeString(AERISAutomationResultCode code)
        {
            switch (code)
            {
                case AERISAutomationResultCode.None: return "NONE";
                case AERISAutomationResultCode.Accepted: return "ACCEPTED";
                case AERISAutomationResultCode.Busy: return "LEASE_BUSY";
                case AERISAutomationResultCode.InvalidRequest: return "INVALID_REQUEST";
                case AERISAutomationResultCode.VesselUnavailable: return "VESSEL_UNAVAILABLE";
                case AERISAutomationResultCode.WrongVessel: return "VESSEL_CHANGED";
                case AERISAutomationResultCode.CapabilityUnavailable: return "CAPABILITY_UNAVAILABLE";
                case AERISAutomationResultCode.ProtectUnavailable: return "PROTECT_UNAVAILABLE";
                case AERISAutomationResultCode.PilotOverrideActive: return "PILOT_OVERRIDE";
                case AERISAutomationResultCode.LeaseExpired: return "LEASE_EXPIRED";
                case AERISAutomationResultCode.SessionNotFound: return "SESSION_NOT_FOUND";
                case AERISAutomationResultCode.PlanNotFound: return "NAV_PLAN_UNAVAILABLE";
                case AERISAutomationResultCode.PlanIncompatible: return "NAV_PLAN_INCOMPATIBLE";
                case AERISAutomationResultCode.NavUnavailable: return "NAV_PLAN_UNAVAILABLE";
                case AERISAutomationResultCode.ApproachUnavailable: return "APPROACH_UNAVAILABLE";
                case AERISAutomationResultCode.RejectedBySafety: return "REJECTED_BY_SAFETY";
                case AERISAutomationResultCode.ProtectIntervention: return "PROTECT_INTERVENTION";
                case AERISAutomationResultCode.AntiStallIntervention: return "ANTI_STALL_INTERVENTION";
                case AERISAutomationResultCode.ControlSaturation: return "CONTROL_SATURATION";
                case AERISAutomationResultCode.ReverseThrust: return "REVERSE_THRUST";
                case AERISAutomationResultCode.RpmLimit: return "RPM_LIMIT";
                case AERISAutomationResultCode.LoadLimit: return "LOAD_LIMIT";
                case AERISAutomationResultCode.NoForwardAcceleration: return "NO_FORWARD_ACCELERATION";
                case AERISAutomationResultCode.RunwayRemainingInsufficient: return "RUNWAY_REMAINING_INSUFFICIENT";
                case AERISAutomationResultCode.RouteGenerationFailed: return "ROUTE_GENERATION_FAILED";
                case AERISAutomationResultCode.TerrainClearanceFailed: return "TERRAIN_CLEARANCE_FAILED";
                case AERISAutomationResultCode.SetpointUnreachable: return "SETPOINT_UNREACHABLE";
                case AERISAutomationResultCode.OperationalCeiling: return "OPERATIONAL_CEILING";
                case AERISAutomationResultCode.LandingFailed: return "LANDING_FAILED";
                case AERISAutomationResultCode.GroundStopTimeout: return "GROUND_STOP_TIMEOUT";
                case AERISAutomationResultCode.NonFiniteState: return "NON_FINITE_STATE";
                case AERISAutomationResultCode.VesselChanged: return "VESSEL_CHANGED";
                case AERISAutomationResultCode.SceneChanged: return "SCENE_CHANGED";
                default: return "INTERNAL_FAULT";
            }
        }

        internal bool TryRenew(AERISAutomationSession session, float ttlSeconds,
            out AERISAutomationResult result)
        {
            SessionRecord record;
            if (!TryValidateSession(session, out record, out result))
                return false;
            double ttl = ttlSeconds;
            if (!Finite(ttl) || ttl <= 0.0) ttl = DefaultLeaseSeconds;
            ttl = Math.Max(MinimumLeaseSeconds, Math.Min(MaximumLeaseSeconds, ttl));
            record.LeaseDurationSeconds = ttl;
            record.LeaseExpiresRealtime = Time.realtimeSinceStartup + (float)ttl;
            record.Session.ExpiresRealtime = record.LeaseExpiresRealtime;
            record.Session.ExpiresUniversalTime = UniversalTime() + ttl;
            result = Accepted("Automation lease renewed for " + ttl.ToString("0.0", CultureInfo.InvariantCulture) + " seconds.");
            return true;
        }

        internal bool TrySubmitGroundPropulsionTest(AERISAutomationSession session,
            AERISGroundPropulsionTestRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Ground propulsion test request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.GroundPropulsionTest,
                out record, out result)) return false;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            if (vessel == null || !vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.SPLASHED)
                return Fail(out result, AERISAutomationResultCode.RejectedBySafety,
                    "Ground propulsion test requires a reliably grounded non-water vessel.", false);
            if (!Finite(request.PowerDemand01) || request.PowerDemand01 < 0f || request.PowerDemand01 > 1f ||
                !Finite(request.PropellerNormalizedPitch) || request.PropellerNormalizedPitch < -1f ||
                request.PropellerNormalizedPitch > 1f)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Ground test power and normalized propeller pitch must be finite and bounded.", false);
            if (!Finite(request.MaximumDurationSeconds) ||
                !Finite(request.MaximumGroundSpeedMps) ||
                !Finite(request.MaximumTravelDistanceM))
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Ground test duration, speed and travel limits must be finite.", false);

            if (request.MaximumDurationSeconds <= 0f) request.MaximumDurationSeconds = request.MicroRoll ? 8f : 5f;
            request.MaximumDurationSeconds = Mathf.Clamp(request.MaximumDurationSeconds, 1f, 60f);
            if (request.MaximumGroundSpeedMps <= 0f) request.MaximumGroundSpeedMps = request.MicroRoll ? 4f : 0.35f;
            if (request.MaximumTravelDistanceM <= 0f) request.MaximumTravelDistanceM = request.MicroRoll ? 12f : 1f;
            request.MaximumGroundSpeedMps = Mathf.Clamp(request.MaximumGroundSpeedMps, 0.1f, 20f);
            request.MaximumTravelDistanceM = Mathf.Clamp(request.MaximumTravelDistanceM, 0.5f, 500f);
            request.BrakesInitiallyApplied = true;
            request.GroundStabilityRequired = true;
            request.RunwayHeadingHoldRequired = true;
            request.AbortOnReverseThrust = true;
            request.AbortOnControlSaturation = true;
            request.CompletionRequiresFullStop = true;
            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "AERIS native AP is active and must be released before a ground test.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result)) return false;

            record.BeforeMission = CaptureState();
            record.OwnsControl = true;
            record.Command = NewCommand(record, MissionGroundTest);
            record.CommandKind = MissionGroundTest;
            record.State = AERISAutomationState.Configuring;
            record.Detail = "GROUND TEST BRAKE HOLD";
            record.FailureCode = AERISAutomationResultCode.None;
            record.MissionCompleted = false;
            record.MissionFailed = false;
            record.ConditionStable = false;

            var runtime = NewV2Runtime(record, MissionGroundTest);
            runtime.GroundRequest = SnapshotGroundTestRequest(request);
            runtime.GroundPower = Mathf.Clamp01(request.PowerDemand01);
            runtime.GroundMicroRoll = request.MicroRoll;
            runtime.GroundMaximumDistance = request.MaximumTravelDistanceM;
            runtime.GroundMaximumSpeed = request.MaximumGroundSpeedMps;
            runtime.GroundStartLatitude = (float)vessel.latitude;
            runtime.GroundStartLongitude = (float)vessel.longitude;
            runtime.MissionTimeout = request.MaximumDurationSeconds + 12f;
            runtime.Phase = "BRAKE_HOLD";
            ApplyBrake(vessel, true);
            if (!core.Master) core.Master = true;
            if (core.GroundStability != null)
            {
                core.GroundStability.RecaptureCurrentHeading(core.Attitude);
                core.GroundStability.UpdateGroundState(vessel, core.Attitude);
            }
            command = CloneCommand(record.Command);
            result = Accepted("Ground propulsion test accepted; AERIS owns brake, heading, power gate and full stop.");
            LogTransition(record, "GROUND PROPULSION TEST ACCEPTED power=" + runtime.GroundPower.ToString("0.00") +
                " pitch=" + request.PropellerNormalizedPitch.ToString("+0.00;-0.00;0.00") +
                " mode=" + (request.MicroRoll ? "MICRO-ROLL" : "STATIC"));
            return true;
        }

        internal bool TrySubmitAutoTakeoff(AERISAutomationSession session,
            AERISAutoTakeoffMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Auto Takeoff request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.AutoTakeoff,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.SPLASHED)
                return Fail(out result, AERISAutomationResultCode.RejectedBySafety,
                    "Auto Takeoff requires a grounded non-water vessel.", false);
            if (request.TargetAltitudeM <= 0) request.TargetAltitudeM = 1000;
            if (request.TargetAltitudeM > 1000000 || !Finite(request.PropellerNormalizedPitch) ||
                request.PropellerNormalizedPitch < -1f || request.PropellerNormalizedPitch > 1f)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Auto Takeoff altitude or external propeller pitch verification is invalid.", false);
            if (core.Protect == null || !core.Protect.AntiStallEnabled)
                return Fail(out result, AERISAutomationResultCode.ProtectUnavailable,
                    "Auto Takeoff requires active AERIS Protect and Anti-Stall.", true);
            request.AutoStage = true;
            request.ProtectRequired = true;
            request.AntiStallRequired = true;
            request.GroundStabilityRequired = true;
            request.AllowAbort = true;
            if (request.RouteId != null && request.RouteId.Length > 4096 ||
                request.SpeedPolicy != null && request.SpeedPolicy.Length > 128)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Auto Takeoff route or speed policy exceeds the Contract v2 length limit.", false);
            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "Native AP is active outside this lease.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result)) return false;

            record.BeforeMission = CaptureState();
            record.OwnsControl = true;
            string error;
            try
            {
                if (!core.Master) core.Master = true;
                float heading = core.Attitude != null && core.Attitude.InstrumentHeadingValid
                    ? core.Attitude.InstrumentHeadingDeg : 90f;
                if (!core.Hdg.TrySetTarget(heading.ToString("0.000", CultureInfo.InvariantCulture), out error))
                    return RollbackMissionStart(record, out result, "Auto Takeoff HDG rejected: " + error);
                if (!core.Altitude.TrySetTarget(request.TargetAltitudeM.ToString(CultureInfo.InvariantCulture), out error))
                    return RollbackMissionStart(record, out result, "Auto Takeoff ALT rejected: " + error);
                float targetSpeed = Mathf.Max(80f, vessel.srfSpeed > 20.0 ? (float)vessel.srfSpeed : 160f);
                if (!core.Velocity.TrySetTarget(targetSpeed.ToString("0.0", CultureInfo.InvariantCulture), out error))
                    return RollbackMissionStart(record, out result, "Auto Takeoff VEL rejected: " + error);
                core.Hdg.SetArmed(true, vessel, core.Bank, core.Attitude);
                core.Pitch.SetArmed(true, vessel, core.Attitude);
                core.VerticalSpeed.SetArmed(true, vessel, core.Attitude, core.Pitch);
                core.Altitude.SetArmed(true, vessel, core.Attitude, core.VerticalSpeed, core.Pitch);
                core.Acceleration.SetArmed(true, vessel, core.Attitude);
                core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
                if (!core.ArmAutoTakeoff(out error))
                    return RollbackMissionStart(record, out result, "Auto Takeoff ARM rejected: " + error);
                if (!core.ExecuteAutoTakeoff(out error))
                    return RollbackMissionStart(record, out result, "Auto Takeoff EXECUTE rejected: " + error);
            }
            catch (Exception ex)
            {
                return RollbackMissionStart(record, out result, "Auto Takeoff configuration fault: " + ex.Message);
            }

            record.Command = NewCommand(record, MissionAutoTakeoff);
            record.CommandKind = MissionAutoTakeoff;
            record.State = AERISAutomationState.Executing;
            record.Detail = "AUTO TAKEOFF — " + core.AutoTakeoff.PhaseText;
            record.FailureCode = AERISAutomationResultCode.None;
            record.MissionCompleted = false;
            record.MissionFailed = false;
            var runtime = NewV2Runtime(record, MissionAutoTakeoff);
            runtime.TakeoffRequest = SnapshotAutoTakeoffRequest(request);
            runtime.MissionTimeout = 180f;
            runtime.Phase = core.AutoTakeoff.PhaseText;
            runtime.GroundStartLatitude = (float)vessel.latitude;
            runtime.GroundStartLongitude = (float)vessel.longitude;
            float runwayRemaining;
            runtime.RunwayGeometryValid = TryEstimateKscRunwayRemaining(vessel,
                out runwayRemaining);
            runtime.GroundMaximumDistance = runtime.RunwayGeometryValid
                ? Mathf.Max(300f, runwayRemaining) : 2300f;
            runtime.RunwayRemaining = runtime.GroundMaximumDistance;
            command = CloneCommand(record.Command);
            result = Accepted("Auto Takeoff accepted and delegated to the AERIS takeoff director.");
            LogTransition(record, "AUTO TAKEOFF ACCEPTED targetAlt=" + request.TargetAltitudeM +
                " verifiedPitch=" + request.PropellerNormalizedPitch.ToString("+0.00;-0.00;0.00") +
                " runwayRemaining=" + runtime.RunwayRemaining.ToString("0",
                    CultureInfo.InvariantCulture) + "m geometry=" +
                (runtime.RunwayGeometryValid ? "KSC" : "CONSERVATIVE_FALLBACK"));
            return true;
        }

        internal bool TrySubmitLearningCorridor(AERISAutomationSession session,
            AERISLearningCorridorRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Learning corridor request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.LearningCorridor,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.mainBody == null || vessel.LandedOrSplashed)
                return Fail(out result, AERISAutomationResultCode.RejectedBySafety,
                    "Learning corridor requires an airborne vessel.", false);
            string bodyName = vessel.mainBody.bodyName ?? string.Empty;
            if (!string.IsNullOrEmpty(request.BodyName) &&
                !string.Equals(request.BodyName, bodyName, StringComparison.OrdinalIgnoreCase))
                return Fail(out result, AERISAutomationResultCode.RouteGenerationFailed,
                    "Requested corridor body does not match the active vessel body.", false);
            string anchor = string.IsNullOrEmpty(request.AnchorPolicy) ? "StockKscEastOcean" : request.AnchorPolicy;
            if (!string.Equals(anchor, "StockKscEastOcean", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase))
                return Fail(out result, AERISAutomationResultCode.RouteGenerationFailed,
                    "Contract v2 currently advertises only the validated StockKscEastOcean corridor on Kerbin.", false);
            if (request.RouteId != null && request.RouteId.Length > 4096 ||
                request.BodyName != null && request.BodyName.Length > 128 ||
                request.AnchorPolicy != null && request.AnchorPolicy.Length > 128 ||
                !Finite(request.ExpectedMaximumSpeedMps) || !Finite(request.LegLengthM) ||
                !Finite(request.CorridorHalfWidthM) ||
                !Finite(request.MinimumTerrainClearanceM) ||
                !Finite(request.TurnBankLimitDeg) || !Finite(request.TurnMarginM) ||
                request.TargetAltitudeM > 1000000)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Learning corridor fields must be finite and inside Contract v2 bounds.", false);

            if (request.TargetAltitudeM <= 0) request.TargetAltitudeM = Math.Max(1000, (int)Math.Ceiling(vessel.altitude));
            float expected = request.ExpectedMaximumSpeedMps > 0f ? request.ExpectedMaximumSpeedMps :
                Mathf.Max(100f, (float)vessel.srfSpeed);
            float generatedLeg = Mathf.Max(80000f, expected * 180f);
            request.LegLengthM = Mathf.Clamp(request.LegLengthM > 0f ? request.LegLengthM : generatedLeg,
                80000f, 250000f);
            request.CorridorHalfWidthM = request.CorridorHalfWidthM > 0f
                ? Mathf.Clamp(request.CorridorHalfWidthM, 500f, 10000f) : 2500f;
            request.MinimumTerrainClearanceM = request.MinimumTerrainClearanceM > 0f
                ? Mathf.Clamp(request.MinimumTerrainClearanceM, 300f, 10000f) : 500f;
            request.TurnBankLimitDeg = request.TurnBankLimitDeg > 0f
                ? Mathf.Clamp(request.TurnBankLimitDeg, 10f, 35f) : 25f;
            request.TurnMarginM = request.TurnMarginM > 0f
                ? Mathf.Max(10000f, request.TurnMarginM) : 10000f;
            request.ShuttlePattern = true;
            request.ValidateTerrainBeforeStart = true;
            request.ObstacleAvoidanceRequired = true;

            string terrainDetail;
            if (!TryValidateCorridorTerrain(vessel, request, out terrainDetail))
                return Fail(out result, AERISAutomationResultCode.TerrainClearanceFailed,
                    terrainDetail, false);

            float corridorCruiseSpeed = Mathf.Max(60f, (float)vessel.srfSpeed);
            float corridorEfficiency = 0.82f; // Conservative independent prior; legacy NAV learning removed.
            float corridorGravity = LocalGravity(vessel);
            float corridorTurnRadius = Mathf.Max(300f, Mathf.Min(
                request.CorridorHalfWidthM * 0.45f, request.TurnMarginM * 0.45f));
            float corridorTurnSpeed = Mathf.Sqrt(Mathf.Max(100f,
                corridorTurnRadius * corridorGravity *
                Mathf.Tan(request.TurnBankLimitDeg * Mathf.Deg2Rad) * corridorEfficiency));
            corridorTurnSpeed = Mathf.Clamp(corridorTurnSpeed, 45f, corridorCruiseSpeed);
            float corridorDecel = CorridorPlanningDeceleration();
            float corridorReserve = CorridorRequiredReserve(corridorCruiseSpeed,
                corridorTurnSpeed, corridorDecel, corridorTurnRadius);
            request.TurnMarginM = Mathf.Max(request.TurnMarginM,
                Mathf.Min(request.LegLengthM * 0.45f, corridorReserve));

            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "Native AP is active outside this lease.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result))
                return false;

            record.BeforeMission = CaptureState();
            record.OwnsControl = true;
            if (!ConfigureFlightHold(vessel, 90f, request.TargetAltitudeM,
                corridorCruiseSpeed, true, out result))
                return RollbackMissionStart(record, out result, result.Detail);

            var runtime = NewV2Runtime(record, MissionCorridor);
            runtime.CorridorRequest = SnapshotLearningCorridorRequest(request);
            runtime.BaseHeading = 90f;
            runtime.CorridorDirection = "EASTBOUND";
            runtime.CorridorLegLength = request.LegLengthM;
            runtime.CorridorHalfWidth = request.CorridorHalfWidthM;
            runtime.CorridorTurnMargin = request.TurnMarginM;
            runtime.MinimumTerrainClearance = request.MinimumTerrainClearanceM;
            runtime.CorridorCruiseSpeed = corridorCruiseSpeed;
            runtime.CorridorTurnSpeed = corridorTurnSpeed;
            runtime.CorridorTurnRadius = corridorTurnRadius;
            runtime.CorridorRequiredTurnReserve = corridorReserve;
            runtime.LegStartLatitude = (float)vessel.latitude;
            runtime.LegStartLongitude = (float)vessel.longitude;
            runtime.StartLatitude = runtime.LegStartLatitude;
            runtime.StartLongitude = runtime.LegStartLongitude;
            runtime.MissionTimeout = 24f * 3600f;
            CaptureAndSetHdgBankLimit(runtime, request.TurnBankLimitDeg);

            record.Command = NewCommand(record, MissionCorridor);
            record.CommandKind = MissionCorridor;
            record.State = AERISAutomationState.Stabilizing;
            record.Detail = "LEARNING CORRIDOR — EASTBOUND CAPTURE";
            record.FailureCode = AERISAutomationResultCode.None;
            command = CloneCommand(record.Command);
            result = Accepted("Kerbin KSC east-ocean learning corridor generated and accepted.");
            LogTransition(record, "LEARNING CORRIDOR ACCEPTED leg=" + request.LegLengthM.ToString("0") +
                "m halfWidth=" + request.CorridorHalfWidthM.ToString("0") +
                "m bankLimit=" + request.TurnBankLimitDeg.ToString("0.0") +
                " turnSpeed=" + corridorTurnSpeed.ToString("0.0") +
                "m/s turnRadius=" + corridorTurnRadius.ToString("0") +
                "m reserve=" + request.TurnMarginM.ToString("0") +
                "m terrain=" + terrainDetail);
            return true;
        }

        private bool TryUpdateActiveLearningCorridorSetpoint(SessionRecord record,
            AERISSetpointMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            V2Runtime runtime;
            if (record == null || request == null ||
                !v2Runtimes.TryGetValue(record.Session.SessionId, out runtime) ||
                runtime == null || runtime.MissionKind != MissionCorridor)
            {
                result = Accepted("No active learning corridor setpoint to update.");
                return false;
            }
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.LandedOrSplashed)
            {
                Fail(out result, AERISAutomationResultCode.VesselUnavailable,
                    "Active learning corridor vessel is unavailable.", true);
                return true;
            }
            float requestedSpeed = Mathf.Max(10f, (float)request.SurfaceSpeedMps);
            float decel = CorridorPlanningDeceleration();
            float requiredReserve = CorridorRequiredReserve(requestedSpeed,
                runtime.CorridorTurnSpeed, decel, runtime.CorridorTurnRadius);
            float remaining = Mathf.Max(0f, runtime.CorridorLegLength - runtime.AlongTrackMeters);
            if (!runtime.Turning && !runtime.TurnPreparation &&
                requiredReserve > remaining + runtime.CorridorTurnMargin)
            {
                Fail(out result, AERISAutomationResultCode.SetpointUnreachable,
                    "Corridor TAS is incompatible with the remaining turn reserve. required=" +
                    requiredReserve.ToString("0", CultureInfo.InvariantCulture) + "m available=" +
                    (remaining + runtime.CorridorTurnMargin).ToString("0", CultureInfo.InvariantCulture) + "m.", true);
                return true;
            }

            string error;
            float previousAltitudeTarget = core.Altitude.TargetAltitudeMeters;
            float previousSpeedTarget = core.Velocity.TargetSurfaceSpeedMps;
            bool altitudeApplied = false;
            try
            {
                if (!core.Altitude.TrySetTarget(request.AltitudeM.ToString("0.0",
                    CultureInfo.InvariantCulture), out error))
                {
                    Fail(out result, AERISAutomationResultCode.SetpointUnreachable,
                        "Corridor ALT setpoint rejected: " + error, true);
                    return true;
                }
                altitudeApplied = true;
                if (!core.Velocity.TrySetTarget(requestedSpeed.ToString("0.0",
                    CultureInfo.InvariantCulture), out error))
                {
                    RestoreCorridorSetpoints(previousAltitudeTarget, previousSpeedTarget);
                    Fail(out result, AERISAutomationResultCode.SetpointUnreachable,
                        "Corridor TAS/surface fallback setpoint rejected; ALT/TAS transaction rolled back: " +
                        error, true);
                    return true;
                }
                core.Pitch.SetArmed(true, vessel, core.Attitude);
                core.VerticalSpeed.SetArmed(true, vessel, core.Attitude, core.Pitch);
                core.Altitude.SetArmed(true, vessel, core.Attitude, core.VerticalSpeed, core.Pitch);
                core.Acceleration.SetArmed(true, vessel, core.Attitude);
                core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
            }
            catch (Exception ex)
            {
                if (altitudeApplied)
                    RestoreCorridorSetpoints(previousAltitudeTarget, previousSpeedTarget);
                Fail(out result, AERISAutomationResultCode.InternalFault,
                    "Corridor setpoint transaction fault; ALT/TAS transaction rolled back: " +
                    ex.Message, true);
                return true;
            }
            runtime.CorridorRequest.TargetAltitudeM = request.AltitudeM;
            runtime.CorridorCruiseSpeed = requestedSpeed;
            runtime.CorridorRequiredTurnReserve = requiredReserve;
            runtime.CorridorTurnMargin = Mathf.Max(10000f,
                Mathf.Min(runtime.CorridorLegLength * 0.45f, requiredReserve));
            runtime.CorridorRequest.TurnMarginM = runtime.CorridorTurnMargin;
            record.SetpointRequest = SnapshotSetpointRequest(request);
            record.Command = NewCommand(record, "CORRIDOR_SETPOINT");
            record.ConditionStable = false;
            runtime.StableSince = -1f;
            command = CloneCommand(record.Command);
            result = Accepted("Atomic corridor ALT/TAS setpoint accepted; stock-no-wind surface speed fallback is active.");
            LogTransition(record, "CORRIDOR SETPOINT alt=" + request.AltitudeM +
                " speed=" + requestedSpeed.ToString("0.0", CultureInfo.InvariantCulture) +
                " requiredTurnReserve=" + requiredReserve.ToString("0", CultureInfo.InvariantCulture));
            return true;
        }


        void RestoreCorridorSetpoints(float altitudeTarget, float speedTarget)
        {
            string ignored;
            try
            {
                if (core != null && core.Altitude != null && Finite(altitudeTarget))
                    core.Altitude.TrySetTarget(altitudeTarget.ToString("0.0",
                        CultureInfo.InvariantCulture), out ignored);
                if (core != null && core.Velocity != null && Finite(speedTarget))
                    core.Velocity.TrySetTarget(speedTarget.ToString("0.0",
                        CultureInfo.InvariantCulture), out ignored);
            }
            catch (Exception ex)
            {
                AERISLogger.Error("[EXT_AUTOMATION][CORRIDOR] setpoint rollback fault: " +
                    ex.Message);
            }
        }

        internal bool TrySubmitEnvelopeSurvey(AERISAutomationSession session,
            AERISEnvelopeSurveyRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Envelope survey request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.EnvelopeSurvey,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.LandedOrSplashed)
                return Fail(out result, AERISAutomationResultCode.RejectedBySafety,
                    "Envelope survey requires an airborne vessel.", false);
            if (!Finite(request.PropellerNormalizedPitch) ||
                request.PropellerNormalizedPitch < -1f || request.PropellerNormalizedPitch > 1f ||
                !Finite(request.AccelerationPlateauMps2) ||
                !Finite(request.PlateauHoldSeconds) ||
                !Finite(request.MaximumDurationSeconds) || request.AltitudeM > 1000000 ||
                request.RouteId != null && request.RouteId.Length > 4096)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Envelope survey fields must be finite and inside Contract v2 bounds.", false);
            if (request.AltitudeM <= 0) request.AltitudeM = (int)Math.Round(vessel.altitude);
            if (request.AccelerationPlateauMps2 <= 0f) request.AccelerationPlateauMps2 = 0.10f;
            request.AccelerationPlateauMps2 = Mathf.Clamp(request.AccelerationPlateauMps2, 0.03f, 0.30f);
            if (request.PlateauHoldSeconds <= 0f) request.PlateauHoldSeconds = 15f;
            request.PlateauHoldSeconds = Mathf.Clamp(request.PlateauHoldSeconds, 5f, 30f);
            if (request.MaximumDurationSeconds <= 0f) request.MaximumDurationSeconds = 90f;
            request.MaximumDurationSeconds = Mathf.Clamp(request.MaximumDurationSeconds, 20f, 180f);
            request.ProtectRequired = true;
            if (core.Protect == null || !core.Protect.AntiStallEnabled)
                return Fail(out result, AERISAutomationResultCode.ProtectUnavailable,
                    "Envelope survey requires Protect and Anti-Stall enabled.", false);
            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "Native AP is active outside this lease.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result)) return false;

            record.BeforeMission = CaptureState();
            record.OwnsControl = true;
            float heading = core.Attitude != null && core.Attitude.InstrumentHeadingValid
                ? core.Attitude.InstrumentHeadingDeg : 0f;
            if (!ConfigureAccelerationMission(vessel, heading, request.AltitudeM, 4f, out result))
                return RollbackMissionStart(record, out result, result.Detail);

            var runtime = NewV2Runtime(record, MissionEnvelope);
            runtime.EnvelopeRequest = SnapshotEnvelopeRequest(request);
            runtime.MissionTimeout = request.MaximumDurationSeconds;
            runtime.ObservedMaxSpeed = (float)vessel.srfSpeed;
            record.Command = NewCommand(record, MissionEnvelope);
            record.CommandKind = MissionEnvelope;
            record.State = AERISAutomationState.Executing;
            record.Detail = "ENVELOPE SURVEY — ACCELERATING";
            record.FailureCode = AERISAutomationResultCode.None;
            command = CloneCommand(record.Command);
            result = Accepted("Envelope survey accepted; ALT and Protect remain authoritative.");
            LogTransition(record, "ENVELOPE SURVEY ACCEPTED alt=" + request.AltitudeM +
                " timeout=" + request.MaximumDurationSeconds.ToString("0") + "s");
            return true;
        }

        internal bool TrySubmitAntiStallSurvey(AERISAutomationSession session,
            AERISAntiStallSurveyRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Anti-Stall survey request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.AntiStallEvent,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.LandedOrSplashed)
                return Fail(out result, AERISAutomationResultCode.RejectedBySafety,
                    "Anti-Stall survey requires an airborne vessel.", false);
            if (core.Protect == null || !core.Protect.AntiStallEnabled)
                return Fail(out result, AERISAutomationResultCode.ProtectUnavailable,
                    "Anti-Stall survey will not disable or bypass Anti-Stall; enable it first.", false);
            if (!Finite(request.PropellerNormalizedPitch) ||
                request.PropellerNormalizedPitch < -1f || request.PropellerNormalizedPitch > 1f ||
                !Finite(request.DecelerationMps2) || !Finite(request.MinimumSurveySpeedMps) ||
                request.AltitudeM > 1000000 ||
                request.RouteId != null && request.RouteId.Length > 4096)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Anti-Stall survey fields must be finite and inside Contract v2 bounds.", false);
            if (request.AltitudeM <= 0) request.AltitudeM = (int)Math.Round(vessel.altitude);
            if (request.DecelerationMps2 <= 0f) request.DecelerationMps2 = 0.5f;
            request.DecelerationMps2 = Mathf.Clamp(request.DecelerationMps2, 0.10f, 0.50f);
            if (request.MinimumSurveySpeedMps <= 0f) request.MinimumSurveySpeedMps = 50f;
            request.MinimumSurveySpeedMps = Mathf.Clamp(request.MinimumSurveySpeedMps, 10f, 500f);
            request.AntiStallMustRemainEnabled = true;
            request.StopOnAntiStallEvent = true;
            request.RecoverAutomatically = true;
            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "Native AP is active outside this lease.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result)) return false;

            record.BeforeMission = CaptureState();
            record.OwnsControl = true;
            float heading = core.Attitude != null && core.Attitude.InstrumentHeadingValid
                ? core.Attitude.InstrumentHeadingDeg : 0f;
            if (!ConfigureAccelerationMission(vessel, heading, request.AltitudeM,
                -request.DecelerationMps2, out result))
                return RollbackMissionStart(record, out result, result.Detail);

            var runtime = NewV2Runtime(record, MissionAntiStall);
            runtime.AntiStallRequest = SnapshotAntiStallRequest(request);
            runtime.MissionTimeout = 180f;
            record.Command = NewCommand(record, MissionAntiStall);
            record.CommandKind = MissionAntiStall;
            record.State = AERISAutomationState.Executing;
            record.Detail = "ANTI-STALL SURVEY — CONTROLLED DECELERATION";
            record.FailureCode = AERISAutomationResultCode.None;
            command = CloneCommand(record.Command);
            result = Accepted("Anti-Stall survey accepted; Anti-Stall remains enabled and owns recovery.");
            LogTransition(record, "ANTI-STALL SURVEY ACCEPTED decel=" + request.DecelerationMps2.ToString("0.00") +
                "m/s2 floor=" + request.MinimumSurveySpeedMps.ToString("0.0") + "m/s");
            return true;
        }

        internal bool TrySubmitClimbMission(AERISAutomationSession session,
            AERISClimbMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Climb mission request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.SetpointGuidance,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.LandedOrSplashed || request.TargetAltitudeM <= vessel.altitude + 5.0)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Climb mission target must be above the current airborne altitude.", false);
            if (!Finite(request.SoftTargetTrueAirspeedMps) ||
                !Finite(request.MaximumDurationSeconds) || request.TargetAltitudeM > 1000000 ||
                request.RouteId != null && request.RouteId.Length > 4096)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Climb mission fields must be finite and inside Contract v2 bounds.", false);
            if (request.SoftTargetTrueAirspeedMps <= 0f)
                request.SoftTargetTrueAirspeedMps = Mathf.Max(60f, (float)vessel.srfSpeed);
            if (request.MaximumDurationSeconds <= 0f) request.MaximumDurationSeconds = 300f;
            request.MaximumDurationSeconds = Mathf.Clamp(request.MaximumDurationSeconds, 30f, 1800f);
            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "Native AP is active outside this lease.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result)) return false;

            record.BeforeMission = CaptureState();
            record.OwnsControl = true;
            float heading = core.Attitude != null && core.Attitude.InstrumentHeadingValid
                ? core.Attitude.InstrumentHeadingDeg : 0f;
            if (!ConfigureFlightHold(vessel, heading, request.TargetAltitudeM,
                request.SoftTargetTrueAirspeedMps, true, out result))
                return RollbackMissionStart(record, out result, result.Detail);

            var runtime = NewV2Runtime(record, MissionClimb);
            runtime.ClimbRequest = SnapshotClimbRequest(request);
            runtime.MissionTimeout = request.MaximumDurationSeconds;
            record.Command = NewCommand(record, MissionClimb);
            record.CommandKind = MissionClimb;
            record.State = AERISAutomationState.Executing;
            record.Detail = "CLIMB — ALTITUDE ACQUISITION";
            record.FailureCode = AERISAutomationResultCode.None;
            command = CloneCommand(record.Command);
            result = Accepted("Climb mission accepted.");
            LogTransition(record, "CLIMB ACCEPTED targetAlt=" + request.TargetAltitudeM +
                " softTAS=" + request.SoftTargetTrueAirspeedMps.ToString("0.0"));
            return true;
        }

        internal bool TryPublishExternalTrimFeedForward(AERISAutomationSession session,
            AERISExternalTrimFeedForwardRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "External trim feed-forward request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.ExternalTrimFeedForward,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            if (!Finite(request.RollFeedForward) || !Finite(request.PitchFeedForward) ||
                !Finite(request.YawFeedForward) || !Finite(request.Confidence01) ||
                Mathf.Abs(request.RollFeedForward) > 1f || Mathf.Abs(request.PitchFeedForward) > 1f ||
                Mathf.Abs(request.YawFeedForward) > 1f || request.Confidence01 < 0f || request.Confidence01 > 1f)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "External trim feed-forward values must be finite, signed -1..+1 and confidence 0..1.", false);
            float ttl = Mathf.Clamp(request.ExpiresSeconds > 0f ? request.ExpiresSeconds : 2f, 0.10f, 10f);
            V2AdvisoryState advisory = AdvisoryFor(record.Session.SessionId);
            advisory.TrimPublished = true;
            advisory.TrimExpires = Time.realtimeSinceStartup + ttl;
            advisory.TrimTargetRoll = request.RollFeedForward * request.Confidence01;
            advisory.TrimTargetPitch = request.PitchFeedForward * request.Confidence01;
            advisory.TrimTargetYaw = request.YawFeedForward * request.Confidence01;
            advisory.TrimConfidence = request.Confidence01;
            advisory.TrimReason = Safe(request.Reason);
            command = NewCommand(record, "EXTERNAL_TRIM_FEED_FORWARD");
            result = Accepted("TTL-bounded external trim feed-forward published.");
            return true;
        }

        internal bool TryPublishExternalTaskDisplay(AERISAutomationSession session,
            AERISExternalTaskDisplayRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "External task display request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.ExternalTaskDisplay,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            if (string.IsNullOrEmpty(request.SourceId) || request.SourceId.Length > 128 ||
                request.DisplayName != null && request.DisplayName.Length > 128 ||
                request.Task != null && request.Task.Length > 128 ||
                request.Phase != null && request.Phase.Length > 128 ||
                request.PrimaryStatus != null && request.PrimaryStatus.Length > 256 ||
                request.SecondaryStatus != null && request.SecondaryStatus.Length > 256 ||
                !Finite(request.Progress01))
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "External task display contains invalid or over-length fields.", false);
            request.Progress01 = Mathf.Clamp01(request.Progress01);
            float ttl = Mathf.Clamp(request.ExpiresSeconds > 0f ? request.ExpiresSeconds : 2f, 0.10f, 30f);
            V2AdvisoryState advisory = AdvisoryFor(record.Session.SessionId);
            advisory.TaskPublished = true;
            advisory.TaskExpires = Time.realtimeSinceStartup + ttl;
            advisory.Task = SnapshotTaskDisplayRequest(request);
            command = NewCommand(record, "EXTERNAL_TASK_DISPLAY");
            result = Accepted("Supplementary external task display published.");
            return true;
        }

        internal bool TryPublishResourceOverrideStatus(AERISAutomationSession session,
            AERISResourceOverrideStatusRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Resource override status request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.ResourceOverrideCoordination,
                out record, out result)) return false;
            if (!ValidateMissionVessel(request.Vessel, request.VesselId, record, out result)) return false;
            if (string.IsNullOrEmpty(request.OwnerClientId) || request.OwnerClientId.Length > 128 ||
                request.Detail != null && request.Detail.Length > 512)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Resource override owner/detail is invalid.", false);
            if (!string.Equals(request.OwnerClientId, record.Session.ClientId, StringComparison.Ordinal))
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "OwnerClientId must exactly match the active lease ClientId.", false);
            float ttl = Mathf.Clamp(request.ExpiresSeconds > 0f ? request.ExpiresSeconds : 3f, 0.10f, 30f);
            V2AdvisoryState advisory = AdvisoryFor(record.Session.SessionId);
            advisory.ResourcePublished = true;
            advisory.ResourceExpires = Time.realtimeSinceStartup + ttl;
            advisory.Resource = SnapshotResourceStatusRequest(request);
            command = NewCommand(record, "RESOURCE_OVERRIDE_STATUS");
            result = Accepted("Resource override status recorded as notification only; AERIS did not write CheatOptions.");
            return true;
        }

        V2Runtime NewV2Runtime(SessionRecord record, string kind)
        {
            ClearV2MissionRuntime(record, false);
            var runtime = new V2Runtime
            {
                MissionKind = kind,
                Phase = "ACCEPTED",
                Started = Time.realtimeSinceStartup,
                PhaseSince = Time.realtimeSinceStartup,
                PreviousSpeed = FlightGlobals.ActiveVessel == null ? 0f : (float)FlightGlobals.ActiveVessel.srfSpeed,
                PreviousSpeedTime = Time.realtimeSinceStartup
            };
            v2Runtimes[record.Session.SessionId] = runtime;
            return runtime;
        }

        V2AdvisoryState AdvisoryFor(Guid sessionId)
        {
            V2AdvisoryState value;
            if (!v2Advisories.TryGetValue(sessionId, out value))
            {
                value = new V2AdvisoryState();
                v2Advisories.Add(sessionId, value);
            }
            return value;
        }

        bool ValidateMissionVessel(Vessel requestVessel, Guid requestVesselId,
            SessionRecord record, out AERISAutomationResult result)
        {
            Guid resolved = requestVesselId != Guid.Empty ? requestVesselId :
                (requestVessel == null ? Guid.Empty : requestVessel.id);
            if (resolved == Guid.Empty) resolved = record.Session.VesselId;
            if (resolved != record.Session.VesselId || ActiveVesselId() != record.Session.VesselId)
                return Fail(out result, AERISAutomationResultCode.WrongVessel,
                    "Request vessel does not match the active leased vessel.", false);
            result = Accepted("Vessel identity confirmed.");
            return true;
        }

        bool ConfigureFlightHold(Vessel vessel, float headingDeg, float altitudeM,
            float speedMps, bool useVelocity, out AERISAutomationResult result)
        {
            string error;
            try
            {
                if (!core.Hdg.TrySetTarget(Repeat360(headingDeg).ToString("0.000", CultureInfo.InvariantCulture), out error))
                    return Fail(out result, AERISAutomationResultCode.InternalFault, "HDG rejected: " + error, true);
                if (!core.Altitude.TrySetTarget(Mathf.Max(0f, altitudeM).ToString("0.0", CultureInfo.InvariantCulture), out error))
                    return Fail(out result, AERISAutomationResultCode.InternalFault, "ALT rejected: " + error, true);
                if (useVelocity && !core.Velocity.TrySetTarget(Mathf.Max(0f, speedMps).ToString("0.0", CultureInfo.InvariantCulture), out error))
                    return Fail(out result, AERISAutomationResultCode.InternalFault, "VEL rejected: " + error, true);
                core.Hdg.SetArmed(true, vessel, core.Bank, core.Attitude);
                core.Pitch.SetArmed(true, vessel, core.Attitude);
                core.VerticalSpeed.SetArmed(true, vessel, core.Attitude, core.Pitch);
                core.Altitude.SetArmed(true, vessel, core.Attitude, core.VerticalSpeed, core.Pitch);
                core.Acceleration.SetArmed(true, vessel, core.Attitude);
                if (useVelocity) core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
                if (!core.Master) core.Master = true;
                result = Accepted("Flight hold configured.");
                return true;
            }
            catch (Exception ex)
            {
                return Fail(out result, AERISAutomationResultCode.InternalFault,
                    "Flight hold configuration failed: " + ex.Message, true);
            }
        }

        bool ConfigureAccelerationMission(Vessel vessel, float headingDeg, float altitudeM,
            float accelerationMps2, out AERISAutomationResult result)
        {
            if (!ConfigureFlightHold(vessel, headingDeg, altitudeM,
                Mathf.Max(0f, (float)vessel.srfSpeed), false, out result)) return false;
            string error;
            if (!core.Acceleration.TrySetTarget(accelerationMps2.ToString("0.00", CultureInfo.InvariantCulture), out error))
                return Fail(out result, AERISAutomationResultCode.InternalFault,
                    "ACC rejected: " + error, true);
            core.Acceleration.SetArmed(true, vessel, core.Attitude);
            return true;
        }

        void CaptureAndSetHdgBankLimit(V2Runtime runtime, float limitDeg)
        {
            if (core.Hdg == null || runtime == null) return;
            runtime.HdgLimitCaptured = true;
            runtime.PriorHdgAutoLimit = core.Hdg.UseAutoMaxBankLimit;
            runtime.PriorHdgManualLimit = core.Hdg.ManualMaxBankLimitDeg;
            string error;
            core.Hdg.TrySetManualMaxBankLimit(Mathf.Clamp(limitDeg, 10f, 35f).ToString("0.0",
                CultureInfo.InvariantCulture), out error);
        }

        void RestoreHdgBankLimit(V2Runtime runtime)
        {
            if (runtime == null || !runtime.HdgLimitCaptured || core.Hdg == null) return;
            string error;
            core.Hdg.TrySetManualMaxBankLimit(runtime.PriorHdgManualLimit.ToString("0.0",
                CultureInfo.InvariantCulture), out error);
            if (runtime.PriorHdgAutoLimit) core.Hdg.SetAutoMaxBankLimit();
            runtime.HdgLimitCaptured = false;
        }

        private bool UpdateV2Mission(SessionRecord record)
        {
            V2Runtime runtime;
            if (record == null || !v2Runtimes.TryGetValue(record.Session.SessionId, out runtime) ||
                runtime == null || string.IsNullOrEmpty(runtime.MissionKind)) return false;
            UpdateAuthorityTelemetry();
            UpdateRuntimeKinematics(runtime);
            float elapsed = Time.realtimeSinceStartup - runtime.Started;
            if (runtime.MissionTimeout > 0f && elapsed > runtime.MissionTimeout)
            {
                if (runtime.MissionKind == MissionGroundTest)
                {
                    if (!runtime.GroundStopping)
                    {
                        FailGroundTestToStop(record, runtime,
                            AERISAutomationResultCode.GroundStopTimeout,
                            "GROUND TEST TIMEOUT");
                        return true;
                    }
                    if (elapsed > runtime.MissionTimeout + 10f)
                    {
                        FailV2Mission(record, AERISAutomationResultCode.GroundStopTimeout,
                            "GROUND TEST STOP TIMEOUT — POWER CUT / BRAKE LATCHED");
                        return true;
                    }
                }
                else
                {
                    FailV2Mission(record, AERISAutomationResultCode.SetpointUnreachable,
                        runtime.MissionKind + " TIMEOUT");
                    return true;
                }
            }
            switch (runtime.MissionKind)
            {
                case MissionGroundTest: UpdateGroundTest(record, runtime); break;
                case MissionAutoTakeoff: UpdateExternalAutoTakeoff(record, runtime); break;
                case MissionCorridor: UpdateLearningCorridor(record, runtime); break;
                case MissionEnvelope: UpdateEnvelopeSurvey(record, runtime); break;
                case MissionAntiStall: UpdateAntiStallSurvey(record, runtime); break;
                case MissionClimb: UpdateClimbMission(record, runtime); break;
            }
            return true;
        }

        void UpdateRuntimeKinematics(V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (runtime == null || vessel == null) return;
            float now = Time.realtimeSinceStartup;
            float speed = Finite(vessel.srfSpeed) ? Mathf.Max(0f, (float)vessel.srfSpeed) : 0f;
            float dt = runtime.PreviousSpeedTime > 0f ? Mathf.Clamp(now - runtime.PreviousSpeedTime, 0.01f, 1f) : 0f;
            runtime.ForwardAcceleration = dt > 0f ? (speed - runtime.PreviousSpeed) / dt : 0f;
            runtime.PreviousSpeed = speed;
            runtime.PreviousSpeedTime = now;
            runtime.ObservedMaxSpeed = Mathf.Max(runtime.ObservedMaxSpeed, speed);
            runtime.PredictedStopDistance = speed * speed / (2f * Mathf.Max(0.5f,
                core.GroundStability != null ? core.GroundStability.BrakeCapabilityMps2PerUnit : 2f));
            runtime.PredictedTakeoffDistance = runtime.ForwardAcceleration > 0.05f
                ? speed * speed / (2f * runtime.ForwardAcceleration) : float.PositiveInfinity;
        }

        void UpdateGroundTest(SessionRecord record, V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || !vessel.LandedOrSplashed)
            {
                FailV2Mission(record, AERISAutomationResultCode.RejectedBySafety,
                    "GROUND TEST LOST WEIGHT-ON-WHEELS");
                return;
            }
            if (!Finite(vessel.srfSpeed) || !Finite(runtime.ForwardAcceleration))
            {
                FailV2Mission(record, AERISAutomationResultCode.NonFiniteState,
                    "GROUND TEST NON-FINITE STATE");
                return;
            }
            if (record.ProtectIntervening && !runtime.GroundStopping)
            {
                FailGroundTestToStop(record, runtime,
                    AERISAutomationResultCode.ProtectIntervention,
                    "GROUND TEST PROTECT INTERVENTION");
                return;
            }
            float now = Time.realtimeSinceStartup;
            float elapsed = now - runtime.Started;
            float speed = (float)vessel.srfSpeed;
            runtime.GroundTravelDistance = (float)SurfaceDistanceMeters(vessel,
                runtime.GroundStartLatitude, runtime.GroundStartLongitude,
                vessel.latitude, vessel.longitude);
            runtime.RunwayRemaining = Mathf.Max(0f, runtime.GroundMaximumDistance - runtime.GroundTravelDistance);
            float signedForward = SignedForwardSpeed(vessel);
            if (runtime.GroundRequest.AbortOnReverseThrust && signedForward < -0.50f)
            {
                FailGroundTestToStop(record, runtime, AERISAutomationResultCode.ReverseThrust,
                    "REVERSE THRUST DETECTED");
                return;
            }
            if (!runtime.GroundStopping && (speed > runtime.GroundMaximumSpeed + 0.10f ||
                runtime.GroundTravelDistance > runtime.GroundMaximumDistance + 0.25f))
            {
                FailGroundTestToStop(record, runtime, AERISAutomationResultCode.RejectedBySafety,
                    "GROUND TEST SPEED/DISTANCE GATE");
                return;
            }
            if (!runtime.GroundStopping && runtime.GroundRequest.AbortOnControlSaturation &&
                authorityControl01 >= 0.85f)
            {
                if (runtime.GroundControlSaturationSince < 0f) runtime.GroundControlSaturationSince = now;
                if (now - runtime.GroundControlSaturationSince >= 1f)
                {
                    FailGroundTestToStop(record, runtime, AERISAutomationResultCode.ControlSaturation,
                        "GROUND CONTROL SATURATION");
                    return;
                }
            }
            else runtime.GroundControlSaturationSince = -1f;

            if (runtime.GroundStopping)
            {
                runtime.RequestedThrottle = 0f;
                ApplyBrake(vessel, true);
                record.State = AERISAutomationState.GroundRoll;
                record.Detail = "GROUND TEST — POWER CUT / STOP";
                if (speed < 0.50f)
                {
                    if (runtime.GroundStopSince < 0f) runtime.GroundStopSince = now;
                    if (now - runtime.GroundStopSince >= 2f)
                    {
                        if (record.MissionFailed)
                        {
                            record.State = AERISAutomationState.Faulted;
                            record.Detail += " — STOPPED";
                            record.CommandKind = string.Empty;
                            FinalizeV2MissionRuntime(record, true);
                        }
                        else CompleteMission(record, "GROUND PROPULSION TEST COMPLETE — FULL STOP", true);
                    }
                }
                else runtime.GroundStopSince = -1f;
                return;
            }

            if (elapsed < 0.75f)
            {
                runtime.Phase = "BRAKE_HOLD";
                runtime.RequestedThrottle = 0f;
                ApplyBrake(vessel, true);
                record.State = AERISAutomationState.Configuring;
                record.Detail = "GROUND TEST — BRAKE HOLD";
                return;
            }
            runtime.Phase = runtime.GroundMicroRoll ? "MICRO_ROLL" : "STATIC_POWER";
            runtime.RequestedThrottle = runtime.GroundPower;
            ApplyBrake(vessel, !runtime.GroundMicroRoll);
            record.State = AERISAutomationState.Executing;
            record.Detail = "GROUND TEST — " + runtime.Phase;
            record.ConditionStable = !runtime.GroundMicroRoll ? speed <= 0.35f : speed <= runtime.GroundMaximumSpeed;
            if (runtime.GroundPower >= 0.30f && elapsed >= 3f &&
                runtime.ForwardAcceleration < 0.02f && speed < 0.25f && runtime.GroundMicroRoll)
            {
                FailGroundTestToStop(record, runtime, AERISAutomationResultCode.NoForwardAcceleration,
                    "NO FORWARD ACCELERATION");
                return;
            }
            bool testComplete = runtime.GroundMicroRoll
                ? (elapsed >= 2.25f || runtime.GroundTravelDistance >= Mathf.Min(12f, runtime.GroundMaximumDistance) ||
                   speed >= runtime.GroundMaximumSpeed * 0.95f)
                : elapsed >= Mathf.Min(runtime.GroundRequest.MaximumDurationSeconds, 5f);
            if (testComplete)
            {
                runtime.GroundStopping = true;
                runtime.Phase = "STOPPING";
                runtime.PhaseSince = now;
                runtime.RequestedThrottle = 0f;
                ApplyBrake(vessel, true);
            }
        }

        void FailGroundTestToStop(SessionRecord record, V2Runtime runtime,
            AERISAutomationResultCode code, string detail)
        {
            record.MissionFailed = true;
            record.MissionCompleted = false;
            record.FailureCode = code;
            record.Detail = detail + " — POWER CUT / BRAKE";
            runtime.GroundStopping = true;
            runtime.Phase = "ABORT_STOP";
            runtime.RequestedThrottle = 0f;
            runtime.GroundStopSince = -1f;
            ApplyBrake(FlightGlobals.ActiveVessel, true);
            LogTransition(record, record.Detail);
        }

        void UpdateExternalAutoTakeoff(SessionRecord record, V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || core.AutoTakeoff == null)
            {
                FailV2Mission(record, AERISAutomationResultCode.InternalFault,
                    "AUTO TAKEOFF DIRECTOR UNAVAILABLE");
                return;
            }
            if (!Finite(vessel.srfSpeed) || !Finite(vessel.latitude) ||
                !Finite(vessel.longitude))
            {
                core.AutoTakeoff.EmergencyRelease(vessel,
                    "external mission non-finite runway state");
                FailV2Mission(record, AERISAutomationResultCode.NonFiniteState,
                    "AUTO TAKEOFF NON-FINITE RUNWAY STATE");
                ApplyBrake(vessel, true);
                return;
            }

            runtime.Phase = core.AutoTakeoff.PhaseText;
            record.Detail = "AUTO TAKEOFF — " + runtime.Phase;
            record.State = vessel.LandedOrSplashed
                ? AERISAutomationState.GroundRoll : AERISAutomationState.Executing;
            runtime.RequestedThrottle = core.AutoTakeoff.ThrottleDemand;

            if (vessel.LandedOrSplashed)
            {
                runtime.GroundTravelDistance = (float)SurfaceDistanceMeters(vessel,
                    runtime.GroundStartLatitude, runtime.GroundStartLongitude,
                    vessel.latitude, vessel.longitude);
                runtime.RunwayRemaining = Mathf.Max(0f,
                    runtime.GroundMaximumDistance - runtime.GroundTravelDistance);
                float speed = Mathf.Max(0f, (float)vessel.srfSpeed);
                float stopMargin = runtime.PredictedStopDistance * 1.35f + 100f;
                float vr = Mathf.Max(1f, core.AutoTakeoff.SelectedVrMps);
                float acceleration = Mathf.Max(0.05f, runtime.ForwardAcceleration);
                runtime.PredictedTakeoffDistance = speed >= vr ? 0f :
                    (vr * vr - speed * speed) / (2f * acceleration);

                bool runwayGateActive =
                    core.AutoTakeoff.Phase == AERISFlightControl.Autopilot.AutoTakeoffPhase.GroundRoll ||
                    core.AutoTakeoff.Phase == AERISFlightControl.Autopilot.AutoTakeoffPhase.Rotate;
                if (runwayGateActive && speed > 5f &&
                    runtime.RunwayRemaining < stopMargin)
                {
                    string abort = "runway remaining " +
                        runtime.RunwayRemaining.ToString("0",
                            CultureInfo.InvariantCulture) +
                        "m below stop gate " + stopMargin.ToString("0",
                            CultureInfo.InvariantCulture) + "m";
                    core.AutoTakeoff.EmergencyRelease(vessel, abort);
                    FailV2Mission(record,
                        AERISAutomationResultCode.RunwayRemainingInsufficient,
                        "AUTO TAKEOFF ABORTED — " + abort);
                    ApplyBrake(vessel, true);
                    return;
                }
                record.Detail += " RWY=" +
                    runtime.RunwayRemaining.ToString("0",
                        CultureInfo.InvariantCulture) + "m STOP=" +
                    stopMargin.ToString("0", CultureInfo.InvariantCulture) + "m";
            }

            if (core.AutoTakeoff.Phase ==
                AERISFlightControl.Autopilot.AutoTakeoffPhase.Aborted)
            {
                FailV2Mission(record, AERISAutomationResultCode.RejectedBySafety,
                    "AUTO TAKEOFF ABORTED — " +
                    Safe(core.AutoTakeoff.LastAbortReason));
                ApplyBrake(vessel, true);
                return;
            }
            if (!core.AutoTakeoff.Armed && !core.AutoTakeoff.Executing &&
                !vessel.LandedOrSplashed && core.NormalApExecutionPermitted)
            {
                CompleteMission(record,
                    "AUTO TAKEOFF COMPLETE — NORMAL AP HANDOFF", false);
            }
        }

        void UpdateLearningCorridor(SessionRecord record, V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.LandedOrSplashed || core.Attitude == null ||
                runtime == null || runtime.CorridorRequest == null)
            {
                FailV2Mission(record, AERISAutomationResultCode.RejectedBySafety,
                    "LEARNING CORRIDOR AIRBORNE STATE LOST");
                return;
            }
            if (!Finite(vessel.latitude) || !Finite(vessel.longitude) || !Finite(vessel.altitude) ||
                !Finite(vessel.heightFromTerrain) || !Finite(vessel.srfSpeed))
            {
                FailV2Mission(record, AERISAutomationResultCode.NonFiniteState,
                    "LEARNING CORRIDOR NON-FINITE FLIGHT STATE");
                return;
            }
            if (record.ProtectIntervening)
            {
                runtime.StraightCorridor = false;
                runtime.StableSince = -1f;
                record.ConditionStable = false;
                record.State = AERISAutomationState.SuspendedByProtect;
                record.Detail = "LEARNING CORRIDOR SUSPENDED BY PROTECT";
                return;
            }

            float now = Time.realtimeSinceStartup;
            double distance = SurfaceDistanceMeters(vessel, runtime.LegStartLatitude,
                runtime.LegStartLongitude, vessel.latitude, vessel.longitude);
            float bearing = (float)InitialBearing(runtime.LegStartLatitude,
                runtime.LegStartLongitude, vessel.latitude, vessel.longitude);
            float delta = Mathf.DeltaAngle(runtime.BaseHeading, bearing) * Mathf.Deg2Rad;
            runtime.CrossTrackMeters = (float)distance * Mathf.Sin(delta);
            runtime.AlongTrackMeters = Mathf.Max(0f, (float)distance * Mathf.Cos(delta));

            float terrainAsl = (float)vessel.altitude -
                Mathf.Max(0f, (float)vessel.heightFromTerrain);
            float requestedAlt = runtime.CorridorRequest.TargetAltitudeM;
            float safeAlt = terrainAsl + runtime.MinimumTerrainClearance;
            runtime.ObstacleAvoidance = safeAlt > requestedAlt + 5f;
            float targetAlt = Mathf.Max(requestedAlt, safeAlt);
            string error;
            if (Mathf.Abs(core.Altitude.TargetAltitudeMeters - targetAlt) > 2f)
                core.Altitude.TrySetTarget(targetAlt.ToString("0.0",
                    CultureInfo.InvariantCulture), out error);

            float turnStartAlong = Mathf.Max(0f,
                runtime.CorridorLegLength - runtime.CorridorTurnRadius);
            float turnPrepAlong = Mathf.Max(0f,
                runtime.CorridorLegLength - Mathf.Max(runtime.CorridorTurnMargin,
                    runtime.CorridorRequiredTurnReserve));

            if (!runtime.Turning && !runtime.TurnPreparation &&
                runtime.AlongTrackMeters >= turnPrepAlong)
            {
                runtime.TurnPreparation = true;
                runtime.StraightCorridor = false;
                runtime.RouteRecapture = false;
                runtime.Phase = "TURN_PREP";
                runtime.PhaseSince = now;
                runtime.StableSince = -1f;
                core.Velocity.TrySetTarget(runtime.CorridorTurnSpeed.ToString("0.0",
                    CultureInfo.InvariantCulture), out error);
                record.State = AERISAutomationState.Stabilizing;
                record.Detail = "LEARNING CORRIDOR — TURN PREP " +
                    runtime.CorridorDirection + " target=" +
                    runtime.CorridorTurnSpeed.ToString("0.0", CultureInfo.InvariantCulture) + "m/s";
            }

            if (runtime.TurnPreparation)
            {
                runtime.StraightCorridor = false;
                runtime.RouteRecapture = false;
                record.ConditionStable = false;
                float speed = Mathf.Max(0f, (float)vessel.srfSpeed);
                bool speedReady = speed <= runtime.CorridorTurnSpeed * 1.08f;
                bool positionReady = runtime.AlongTrackMeters >= turnStartAlong;
                float available = Mathf.Max(1f, turnStartAlong - runtime.AlongTrackMeters);
                float requiredDecel = speed > runtime.CorridorTurnSpeed
                    ? (speed * speed - runtime.CorridorTurnSpeed * runtime.CorridorTurnSpeed) /
                      (2f * available) : 0f;
                float decelLimit = CorridorPlanningDeceleration();

                if (!speedReady && runtime.AlongTrackMeters >=
                    runtime.CorridorLegLength - runtime.CorridorTurnRadius * 0.25f)
                {
                    FailV2Mission(record, AERISAutomationResultCode.SetpointUnreachable,
                        "LEARNING CORRIDOR TURN SPEED NOT CAPTURED BEFORE GEOMETRIC TURN LIMIT");
                    return;
                }
                if (requiredDecel > decelLimit * 1.25f &&
                    runtime.AlongTrackMeters >= turnPrepAlong + 100f)
                {
                    record.Detail = "LEARNING CORRIDOR — TURN PREP DECEL AUTHORITY " +
                        requiredDecel.ToString("0.00", CultureInfo.InvariantCulture) + "m/s2";
                }

                if (speedReady && positionReady)
                {
                    runtime.TurnPreparation = false;
                    runtime.Turning = true;
                    runtime.Phase = "TURNING";
                    runtime.PhaseSince = now;
                    runtime.StableSince = -1f;
                    runtime.TurnStartLatitude = (float)vessel.latitude;
                    runtime.TurnStartLongitude = (float)vessel.longitude;

                    float preferredSign;
                    if (Mathf.Abs(runtime.CrossTrackMeters) > 50f)
                        preferredSign = runtime.CrossTrackMeters > 0f ? -1f : 1f;
                    else preferredSign = (runtime.CorridorPassIndex & 1) == 0 ? -1f : 1f;
                    runtime.TurnDirectionSign = preferredSign;
                    runtime.TurnTargetHeading = Mathf.Repeat(
                        runtime.BaseHeading + preferredSign * 179.5f, 360f);
                    core.Hdg.TrySetTarget(runtime.TurnTargetHeading.ToString("0.000",
                        CultureInfo.InvariantCulture), out error);
                    record.State = AERISAutomationState.Executing;
                    record.Detail = "LEARNING CORRIDOR — TURNING " +
                        (preferredSign < 0f ? "LEFT" : "RIGHT") + " TO " +
                        Mathf.Repeat(runtime.BaseHeading + 180f, 360f).ToString("0.0",
                            CultureInfo.InvariantCulture);
                    LogTransition(record, "CORRIDOR TURN START speed=" +
                        speed.ToString("0.0", CultureInfo.InvariantCulture) +
                        " target=" + runtime.CorridorTurnSpeed.ToString("0.0",
                            CultureInfo.InvariantCulture) +
                        " radiusBudget=" + runtime.CorridorTurnRadius.ToString("0",
                            CultureInfo.InvariantCulture));
                }
                return;
            }

            if (runtime.Turning)
            {
                runtime.StraightCorridor = false;
                runtime.RouteRecapture = false;
                record.ConditionStable = false;

                float turnExcursion = (float)SurfaceDistanceMeters(vessel,
                    runtime.TurnStartLatitude, runtime.TurnStartLongitude,
                    vessel.latitude, vessel.longitude);
                float allowedExcursion = Mathf.Max(runtime.CorridorTurnMargin,
                    runtime.CorridorHalfWidth * 2.5f);
                if (turnExcursion > allowedExcursion ||
                    Mathf.Abs(runtime.CrossTrackMeters) > runtime.CorridorHalfWidth * 1.10f)
                {
                    FailV2Mission(record, AERISAutomationResultCode.RouteGenerationFailed,
                        "LEARNING CORRIDOR TURN EXCURSION EXCEEDED SAFE GEOMETRY");
                    return;
                }

                float newBaseHeading = Mathf.Repeat(runtime.BaseHeading + 180f, 360f);
                float headingError = Mathf.Abs(Mathf.DeltaAngle(
                    core.Attitude.InstrumentHeadingDeg, newBaseHeading));
                float bank = Mathf.Abs(core.Attitude.InstrumentHorizonBankDeg);
                if (headingError <= 5f)
                    core.Hdg.TrySetTarget(newBaseHeading.ToString("0.000",
                        CultureInfo.InvariantCulture), out error);

                if (headingError <= 5f && bank <= 5f)
                {
                    if (runtime.StableSince < 0f) runtime.StableSince = now;
                    if (now - runtime.StableSince >= 2f)
                    {
                        runtime.Turning = false;
                        runtime.RouteRecapture = true;
                        runtime.BaseHeading = newBaseHeading;
                        runtime.CorridorDirection = runtime.BaseHeading < 180f
                            ? "EASTBOUND" : "WESTBOUND";
                        runtime.CorridorPassIndex++;
                        runtime.LegStartLatitude = (float)vessel.latitude;
                        runtime.LegStartLongitude = (float)vessel.longitude;
                        runtime.StableSince = -1f;
                        runtime.Phase = "ROUTE_RECAPTURE";
                        core.Velocity.TrySetTarget(runtime.CorridorCruiseSpeed.ToString("0.0",
                            CultureInfo.InvariantCulture), out error);
                        LogTransition(record, "CORRIDOR TURN COMPLETE pass=" +
                            runtime.CorridorPassIndex + " direction=" +
                            runtime.CorridorDirection + " cruise=" +
                            runtime.CorridorCruiseSpeed.ToString("0.0",
                                CultureInfo.InvariantCulture));
                    }
                }
                else runtime.StableSince = -1f;
                return;
            }

            runtime.RouteRecapture = runtime.RouteRecapture ||
                Mathf.Abs(runtime.CrossTrackMeters) > runtime.CorridorHalfWidth * 0.45f;
            if (Mathf.Abs(runtime.CrossTrackMeters) > runtime.CorridorHalfWidth)
            {
                FailV2Mission(record, AERISAutomationResultCode.SetpointUnreachable,
                    "LEARNING CORRIDOR HALF-WIDTH EXCEEDED");
                return;
            }

            float lookahead = Mathf.Max(3000f,
                Mathf.Min(15000f, (float)vessel.srfSpeed * 40f));
            float intercept = Mathf.Atan2(-runtime.CrossTrackMeters, lookahead) *
                Mathf.Rad2Deg;
            intercept = Mathf.Clamp(intercept, -20f, 20f);
            float headingCommand = Mathf.Repeat(runtime.BaseHeading + intercept, 360f);
            if (now - runtime.LastHeadingPublish >= 0.25f ||
                Mathf.Abs(Mathf.DeltaAngle(core.Hdg.TargetHeading, headingCommand)) >= 0.25f)
            {
                runtime.LastHeadingPublish = now;
                core.Hdg.TrySetTarget(headingCommand.ToString("0.000",
                    CultureInfo.InvariantCulture), out error);
            }

            float headingErrorStraight = Mathf.Abs(Mathf.DeltaAngle(
                core.Attitude.InstrumentHeadingDeg, runtime.BaseHeading));
            float bankAbs = Mathf.Abs(core.Attitude.InstrumentHorizonBankDeg);
            float altitudeError = Mathf.Abs((float)vessel.altitude - targetAlt);
            float speedTarget = runtime.CorridorCruiseSpeed > 0f
                ? runtime.CorridorCruiseSpeed : core.Velocity.TargetSurfaceSpeedMps;
            float speedError = Mathf.Abs((float)vessel.srfSpeed - speedTarget);
            AERISSetpointMissionRequest setpoint = record.SetpointRequest;
            float altTolerance = setpoint != null && setpoint.AltitudeToleranceM > 0f
                ? setpoint.AltitudeToleranceM : 15f;
            float speedTolerance = setpoint != null && setpoint.SpeedToleranceMps > 0f
                ? setpoint.SpeedToleranceMps : 2f;
            float vsTolerance = setpoint != null &&
                setpoint.VerticalSpeedToleranceMps > 0f
                ? setpoint.VerticalSpeedToleranceMps : 2f;
            float bankTolerance = setpoint != null && setpoint.BankToleranceDeg > 0f
                ? setpoint.BankToleranceDeg : 3f;
            float stableSeconds = setpoint != null && setpoint.StableSeconds > 0f
                ? setpoint.StableSeconds : 3f;

            bool stable = !runtime.ObstacleAvoidance &&
                Mathf.Abs(runtime.CrossTrackMeters) <= runtime.CorridorHalfWidth * 0.20f &&
                headingErrorStraight <= 3f && bankAbs <= bankTolerance &&
                altitudeError <= altTolerance && speedError <= speedTolerance &&
                Mathf.Abs(core.Attitude.VerticalSpeedMps) <= vsTolerance &&
                authorityControl01 < 0.60f && !record.ProtectIntervening;
            if (stable)
            {
                if (runtime.StableSince < 0f) runtime.StableSince = now;
                runtime.StraightCorridor = now - runtime.StableSince >= stableSeconds;
            }
            else
            {
                runtime.StableSince = -1f;
                runtime.StraightCorridor = false;
            }
            runtime.RouteRecapture = runtime.RouteRecapture && !runtime.StraightCorridor;
            if (runtime.StraightCorridor)
            {
                runtime.RouteRecapture = false;
                runtime.Phase = "STRAIGHT";
            }
            else if (runtime.RouteRecapture) runtime.Phase = "ROUTE_RECAPTURE";
            else runtime.Phase = "CAPTURE";

            record.ConditionStable = runtime.StraightCorridor;
            record.State = runtime.StraightCorridor
                ? AERISAutomationState.Stable : AERISAutomationState.Stabilizing;
            record.Detail = "LEARNING CORRIDOR — " +
                (runtime.StraightCorridor ? "STRAIGHT " :
                 runtime.RouteRecapture ? "RECAPTURE " : "CAPTURE ") +
                runtime.CorridorDirection + " XTE=" +
                runtime.CrossTrackMeters.ToString("0", CultureInfo.InvariantCulture) +
                "m VERR=" + speedError.ToString("0.0", CultureInfo.InvariantCulture) + "m/s";
        }

        void UpdateEnvelopeSurvey(SessionRecord record, V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || core.Attitude == null)
            {
                FailV2Mission(record, AERISAutomationResultCode.VesselUnavailable,
                    "ENVELOPE SURVEY VESSEL UNAVAILABLE");
                return;
            }
            float now = Time.realtimeSinceStartup;
            float altitudeError = Mathf.Abs((float)vessel.altitude - runtime.EnvelopeRequest.AltitudeM);
            bool altitudeFailure = altitudeError > 25f || core.Attitude.VerticalSpeedMps < -1.5f;
            if (altitudeFailure)
            {
                if (runtime.FailureSince < 0f) runtime.FailureSince = now;
                if (now - runtime.FailureSince >= 5f)
                {
                    FailV2Mission(record, AERISAutomationResultCode.OperationalCeiling,
                        "ENVELOPE SURVEY ALTITUDE/VERTICAL PATH UNAVAILABLE");
                    return;
                }
            }
            else runtime.FailureSince = -1f;
            if (record.ProtectIntervening)
            {
                FailV2Mission(record, AERISAutomationResultCode.ProtectIntervention,
                    "ENVELOPE SURVEY STOPPED BY PROTECT");
                return;
            }
            if (authorityControl01 >= 0.85f)
            {
                FailV2Mission(record, AERISAutomationResultCode.ControlSaturation,
                    "ENVELOPE SURVEY CONTROL AUTHORITY LIMIT");
                return;
            }
            float acceleration = core.Acceleration != null
                ? core.Acceleration.FilteredAccelerationMps2 : runtime.ForwardAcceleration;
            if (!Finite(acceleration))
            {
                FailV2Mission(record, AERISAutomationResultCode.NonFiniteState,
                    "ENVELOPE SURVEY NON-FINITE ACCELERATION");
                return;
            }
            bool plateau = acceleration <= runtime.EnvelopeRequest.AccelerationPlateauMps2 &&
                core.Acceleration != null && core.Acceleration.ThrottleDemand >= 0.95f;
            if (plateau)
            {
                if (runtime.StableSince < 0f) runtime.StableSince = now;
                record.State = AERISAutomationState.Stabilizing;
                record.Detail = "ENVELOPE SURVEY — PLATEAU CONFIRM " +
                    (now - runtime.StableSince).ToString("0.0") + "s";
                if (now - runtime.StableSince >= runtime.EnvelopeRequest.PlateauHoldSeconds)
                {
                    record.ConditionStable = true;
                    CompleteMission(record, "ENVELOPE SURVEY COMPLETE — SPEED PLATEAU", false);
                }
            }
            else
            {
                runtime.StableSince = -1f;
                record.State = AERISAutomationState.Executing;
                record.Detail = "ENVELOPE SURVEY — ACCELERATING";
            }
        }

        void UpdateAntiStallSurvey(SessionRecord record, V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || core.Protect == null || core.Attitude == null)
            {
                FailV2Mission(record, AERISAutomationResultCode.InternalFault,
                    "ANTI-STALL SURVEY SENSOR UNAVAILABLE");
                return;
            }
            if (!core.Protect.AntiStallEnabled)
            {
                FailV2Mission(record, AERISAutomationResultCode.ProtectUnavailable,
                    "ANTI-STALL WAS DISABLED DURING SURVEY");
                return;
            }
            float now = Time.realtimeSinceStartup;
            float speed = (float)vessel.srfSpeed;
            runtime.SpeedWindow.Add(new TimedSpeedSample { Time = now, Speed = speed });
            for (int i = runtime.SpeedWindow.Count - 1; i >= 0; i--)
                if (now - runtime.SpeedWindow[i].Time > 1.25f) runtime.SpeedWindow.RemoveAt(i);

            bool eventActive = core.Protect.ProtectActive || core.Protect.StallDetected;
            if (!runtime.AntiStallEventLatched && eventActive)
            {
                if (runtime.AntiStallActiveSince < 0f) runtime.AntiStallActiveSince = now;
                if (now - runtime.AntiStallActiveSince >= 0.35f)
                {
                    runtime.AntiStallEventLatched = true;
                    runtime.ObservedAntiStallSpeed = MedianSpeed(runtime.SpeedWindow, now - 1f);
                    record.State = AERISAutomationState.Executing;
                    record.Detail = "ANTI-STALL EVENT — RECOVERY";
                    BeginAntiStallRecovery(vessel, runtime);
                }
            }
            else if (!eventActive && !runtime.AntiStallEventLatched)
                runtime.AntiStallActiveSince = -1f;

            if (!runtime.AntiStallEventLatched && speed <= runtime.AntiStallRequest.MinimumSurveySpeedMps)
            {
                record.Detail = "ANTI-STALL SURVEY COMPLETE — BELOW_GRID";
                record.ConditionStable = true;
                CompleteMission(record, record.Detail, false);
                return;
            }
            if (runtime.AntiStallEventLatched)
            {
                if (!runtime.RecoveryCommanded) BeginAntiStallRecovery(vessel, runtime);
                float recoveryTarget = Mathf.Max(runtime.ObservedAntiStallSpeed * 1.25f,
                    runtime.ObservedAntiStallSpeed + 15f);
                bool recovered = !core.Protect.ProtectActive && speed >= recoveryTarget * 0.95f &&
                    Mathf.Abs(core.Attitude.VerticalSpeedMps) <= 5f;
                if (recovered)
                {
                    if (runtime.StableSince < 0f) runtime.StableSince = now;
                    if (now - runtime.StableSince >= 2f)
                        CompleteMission(record, "ANTI-STALL SURVEY COMPLETE — EVENT LATCHED / RECOVERED", false);
                }
                else runtime.StableSince = -1f;
            }
            else
            {
                record.State = AERISAutomationState.Executing;
                record.Detail = "ANTI-STALL SURVEY — CONTROLLED DECELERATION";
            }
        }

        void BeginAntiStallRecovery(Vessel vessel, V2Runtime runtime)
        {
            if (vessel == null || runtime == null) return;
            string error;
            float recoveryTarget = Mathf.Max(runtime.ObservedAntiStallSpeed * 1.25f,
                runtime.ObservedAntiStallSpeed + 15f);
            recoveryTarget = Mathf.Max(recoveryTarget, (float)vessel.srfSpeed + 5f);
            core.Acceleration.ClearVelocityPlannerTarget();
            core.Velocity.TrySetTarget(recoveryTarget.ToString("0.0", CultureInfo.InvariantCulture), out error);
            core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
            runtime.RecoveryCommanded = true;
        }

        void UpdateClimbMission(SessionRecord record, V2Runtime runtime)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || core.Attitude == null)
            {
                FailV2Mission(record, AERISAutomationResultCode.VesselUnavailable,
                    "CLIMB VESSEL UNAVAILABLE");
                return;
            }
            if (record.ProtectIntervening)
            {
                record.State = AERISAutomationState.SuspendedByProtect;
                record.Detail = "CLIMB SUSPENDED BY PROTECT";
                return;
            }
            float altitudeError = runtime.ClimbRequest.TargetAltitudeM - (float)vessel.altitude;
            bool captured = Mathf.Abs(altitudeError) <= 5f &&
                Mathf.Abs(core.Attitude.VerticalSpeedMps) <= 1f;
            if (captured)
            {
                if (runtime.StableSince < 0f) runtime.StableSince = Time.realtimeSinceStartup;
                record.State = AERISAutomationState.Stabilizing;
                if (Time.realtimeSinceStartup - runtime.StableSince >= 3f)
                    CompleteMission(record, "CLIMB COMPLETE — ALT CAPTURED", false);
            }
            else
            {
                runtime.StableSince = -1f;
                record.State = AERISAutomationState.Executing;
                record.Detail = "CLIMB — ALT ERROR " + altitudeError.ToString("+0;-0;0") + "m";
            }
        }

        void FailV2Mission(SessionRecord record, AERISAutomationResultCode code, string detail)
        {
            if (record == null) return;
            record.MissionFailed = true;
            record.MissionCompleted = false;
            record.ConditionStable = false;
            record.FailureCode = code;
            record.State = AERISAutomationState.Faulted;
            record.Detail = detail;
            LogTransition(record, detail);
            ReleaseControl(record, detail, false);
            record.CommandKind = string.Empty;
            FinalizeV2MissionRuntime(record, true);
        }

        private void TickV2Advisories(SessionRecord record)
        {
            if (record == null) return;
            UpdateAuthorityTelemetry();
            V2AdvisoryState advisory;
            if (!v2Advisories.TryGetValue(record.Session.SessionId, out advisory))
            {
                ClearStandardTrimFeedForward();
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (advisory.TaskPublished && now >= advisory.TaskExpires) advisory.TaskPublished = false;
            if (advisory.ResourcePublished && now >= advisory.ResourceExpires) advisory.ResourcePublished = false;
            bool trimAllowed = advisory.TrimPublished && now < advisory.TrimExpires &&
                !record.PilotOverride && !record.ProtectIntervening &&
                ContainsCapability(record.Session.GrantedCapabilities, AERISAutomationCapability.ExternalTrimFeedForward);
            if (!trimAllowed)
            {
                advisory.TrimPublished = false;
                advisory.TrimTargetRoll = advisory.TrimTargetPitch = advisory.TrimTargetYaw = 0f;
                // Protect and pilot authority are hard priorities, not blend targets.
                // Remove already-applied external trim in the same tick so no residual
                // feed-forward remains while either authority is intervening.
                if (record.PilotOverride || record.ProtectIntervening)
                    advisory.TrimAppliedRoll = advisory.TrimAppliedPitch =
                        advisory.TrimAppliedYaw = 0f;
            }
            float dt = Mathf.Clamp(Time.unscaledDeltaTime, 0.005f, 0.10f);
            advisory.TrimAppliedRoll = Mathf.MoveTowards(advisory.TrimAppliedRoll,
                advisory.TrimTargetRoll, 1.5f * dt);
            advisory.TrimAppliedPitch = Mathf.MoveTowards(advisory.TrimAppliedPitch,
                advisory.TrimTargetPitch, 1.5f * dt);
            advisory.TrimAppliedYaw = Mathf.MoveTowards(advisory.TrimAppliedYaw,
                advisory.TrimTargetYaw, 1.5f * dt);
            StandardFlyByWire.ExternalTrimFeedForwardActive =
                Mathf.Abs(advisory.TrimAppliedRoll) > 0.0001f ||
                Mathf.Abs(advisory.TrimAppliedPitch) > 0.0001f ||
                Mathf.Abs(advisory.TrimAppliedYaw) > 0.0001f;
            StandardFlyByWire.ExternalTrimRollInput = Mathf.Clamp(advisory.TrimAppliedRoll * 0.20f, -0.20f, 0.20f);
            StandardFlyByWire.ExternalTrimPitchInput = Mathf.Clamp(advisory.TrimAppliedPitch * 0.15f, -0.15f, 0.15f);
            StandardFlyByWire.ExternalTrimYawInput = Mathf.Clamp(advisory.TrimAppliedYaw * 0.15f, -0.15f, 0.15f);
            StandardFlyByWire.ExternalTrimRollRateRadPerSec = advisory.TrimAppliedRoll * 4f * Mathf.Deg2Rad;
            StandardFlyByWire.ExternalTrimPitchRateRadPerSec = advisory.TrimAppliedPitch * 2f * Mathf.Deg2Rad;
            StandardFlyByWire.ExternalTrimYawRateRadPerSec = advisory.TrimAppliedYaw * 2f * Mathf.Deg2Rad;
        }

        internal void ApplyV2ControlDemands(FlightCtrlState state)
        {
            Guid owner;
            SessionRecord record;
            V2Runtime runtime;
            if (!vesselOwners.TryGetValue(ActiveVesselId(), out owner) ||
                !sessions.TryGetValue(owner, out record) ||
                !v2Runtimes.TryGetValue(owner, out runtime) || runtime == null) return;
            if (runtime.MissionKind == MissionGroundTest)
            {
                StandardFlyByWire.ExternalThrottleOverride = true;
                StandardFlyByWire.ExternalThrottleDemand = Mathf.Clamp01(runtime.RequestedThrottle);
            }
        }

        internal void CaptureFinalControlTelemetry()
        {
            UpdateAuthorityTelemetry();
        }

        void UpdateAuthorityTelemetry()
        {
            authorityRoll01 = Mathf.Clamp01(Mathf.Abs(StandardFlyByWire.LastFinalRoll));
            authorityPitch01 = Mathf.Clamp01(Mathf.Abs(StandardFlyByWire.LastFinalPitch));
            authorityYaw01 = Mathf.Clamp01(Mathf.Abs(StandardFlyByWire.LastFinalYaw));
            if (core != null && core.Bank != null && core.Hdg != null && core.Bank.Armed)
            {
                float bankLimit = Mathf.Max(1f, core.Hdg.EffectiveMaxBankLimitDeg);
                float bankRatio = Mathf.Abs(core.Bank.TargetBank) / bankLimit;
                authorityRoll01 = Mathf.Max(authorityRoll01, Mathf.Clamp01(bankRatio));
            }
            authorityControl01 = Mathf.Max(authorityRoll01, Mathf.Max(authorityPitch01, authorityYaw01));
            if (core != null && core.Acceleration != null && core.Acceleration.ThrustSaturated)
                authorityControl01 = 1f;
            authoritySaturated = authorityControl01 >= 0.98f ||
                core != null && core.Acceleration != null && core.Acceleration.ThrustSaturated;
        }

        void ClearStandardTrimFeedForward()
        {
            StandardFlyByWire.ExternalTrimFeedForwardActive = false;
            StandardFlyByWire.ExternalTrimRollInput = 0f;
            StandardFlyByWire.ExternalTrimPitchInput = 0f;
            StandardFlyByWire.ExternalTrimYawInput = 0f;
            StandardFlyByWire.ExternalTrimRollRateRadPerSec = 0f;
            StandardFlyByWire.ExternalTrimPitchRateRadPerSec = 0f;
            StandardFlyByWire.ExternalTrimYawRateRadPerSec = 0f;
        }

        private void ClearV2ForSession(SessionRecord record, bool safetyStop, bool clearAdvisories)
        {
            if (record == null) return;
            ClearV2MissionRuntime(record, safetyStop);
            if (clearAdvisories) ClearV2Advisories(record);
        }

        private void ClearV2Advisories(SessionRecord record)
        {
            if (record == null) return;
            v2Advisories.Remove(record.Session.SessionId);
            ClearStandardTrimFeedForward();
        }

        private void FinalizeV2MissionRuntime(SessionRecord record, bool safetyStop)
        {
            if (record == null) return;
            V2Runtime runtime;
            if (!v2Runtimes.TryGetValue(record.Session.SessionId, out runtime) || runtime == null) return;
            RestoreHdgBankLimit(runtime);
            if (runtime.MissionKind == MissionGroundTest)
            {
                StandardFlyByWire.ExternalThrottleOverride = false;
                StandardFlyByWire.ExternalThrottleDemand = 0f;
                if (safetyStop) ApplyBrake(FlightGlobals.ActiveVessel, true);
            }
            runtime.Phase = record.MissionCompleted ? "COMPLETED" :
                record.MissionFailed ? "FAILED" : "TERMINATED";
            runtime.MissionKind = string.Empty;
        }

        void ClearV2MissionRuntime(SessionRecord record, bool safetyStop)
        {
            if (record == null) return;
            V2Runtime runtime;
            if (!v2Runtimes.TryGetValue(record.Session.SessionId, out runtime)) return;
            RestoreHdgBankLimit(runtime);
            if (runtime.MissionKind == MissionGroundTest)
            {
                StandardFlyByWire.ExternalThrottleOverride = false;
                StandardFlyByWire.ExternalThrottleDemand = 0f;
                if (safetyStop) ApplyBrake(FlightGlobals.ActiveVessel, true);
            }
            v2Runtimes.Remove(record.Session.SessionId);
        }

        private AERISAutomationSnapshot EnrichV2Snapshot(SessionRecord record,
            AERISAutomationSnapshot snapshot)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            UpdateAuthorityTelemetry();
            snapshot.AntiStallActive = core.Protect != null && core.Protect.ProtectActive;
            snapshot.ControlAuthority01 = authorityControl01;
            snapshot.RollAuthority01 = authorityRoll01;
            snapshot.PitchAuthority01 = authorityPitch01;
            snapshot.YawAuthority01 = authorityYaw01;
            snapshot.ControlSaturated = authoritySaturated;
            snapshot.RollControlCommand = StandardFlyByWire.LastFinalRoll;
            snapshot.PitchControlCommand = StandardFlyByWire.LastFinalPitch;
            snapshot.YawControlCommand = StandardFlyByWire.LastFinalYaw;
            snapshot.RequestedThrottle01 = StandardFlyByWire.LastFinalThrottle;
            snapshot.AirspeedSource = "SURFACE_SPEED_FALLBACK";
            if (core.Attitude != null)
            {
                snapshot.TrueAirspeedMps = core.Attitude.SurfaceSpeedMps;
                snapshot.AltitudeM = core.Attitude.AltitudeAslM;
                snapshot.VerticalSpeedMps = core.Attitude.VerticalSpeedMps;
                snapshot.HeadingDeg = core.Attitude.InstrumentHeadingDeg;
                snapshot.BankDeg = core.Attitude.InstrumentHorizonBankDeg;
                snapshot.RollRateDegPerSec = core.Attitude.InstrumentRollRateDegPerSec;
                snapshot.PitchRateDegPerSec = core.Attitude.InstrumentPitchRateDegPerSec;
                snapshot.YawRateDegPerSec = core.Attitude.InstrumentYawRateDegPerSec;
            }
            snapshot.GroundSpeedMps = vessel != null && Finite(vessel.srfSpeed) ? (float)vessel.srfSpeed : 0f;
            bool reliableGround = vessel != null && vessel.LandedOrSplashed &&
                (core.GroundStability == null || core.GroundStability.ReliableGrounded);
            bool stopped = reliableGround && snapshot.GroundSpeedMps < 0.5f;
            if (stopped)
            {
                if (record.GroundAssistStableSince < 0f)
                    record.GroundAssistStableSince = Time.realtimeSinceStartup;
            }
            else record.GroundAssistStableSince = -1f;
            snapshot.GroundAssistStopped = stopped && record.GroundAssistStableSince >= 0f &&
                Time.realtimeSinceStartup - record.GroundAssistStableSince >= 3f;

            bool tasMission = string.Equals(record.CommandKind, "SETPOINT", StringComparison.Ordinal) ||
                string.Equals(record.CommandKind, MissionCorridor, StringComparison.Ordinal) ||
                string.Equals(record.CommandKind, MissionEnvelope, StringComparison.Ordinal) ||
                string.Equals(record.CommandKind, MissionAntiStall, StringComparison.Ordinal) ||
                string.Equals(record.CommandKind, MissionClimb, StringComparison.Ordinal);
            if (tasMission && (snapshot.Detail == null ||
                snapshot.Detail.IndexOf("TAS_SOURCE=", StringComparison.Ordinal) < 0))
                snapshot.Detail = Safe(snapshot.Detail) + " | TAS_SOURCE=SURFACE_SPEED_FALLBACK";

            V2Runtime runtime;
            if (v2Runtimes.TryGetValue(record.Session.SessionId, out runtime) && runtime != null)
            {
                snapshot.AntiStallEventLatched = runtime.AntiStallEventLatched;
                snapshot.StraightCorridor = runtime.StraightCorridor;
                snapshot.Turning = runtime.Turning;
                snapshot.RouteRecapture = runtime.RouteRecapture;
                snapshot.ObstacleAvoidance = runtime.ObstacleAvoidance;
                snapshot.CorridorDirection = runtime.CorridorDirection ?? string.Empty;
                snapshot.CorridorPassIndex = runtime.CorridorPassIndex;
                snapshot.ObservedForwardAccelerationMps2 = runtime.ForwardAcceleration;
                snapshot.PredictedTakeoffDistanceM = Finite(runtime.PredictedTakeoffDistance)
                    ? runtime.PredictedTakeoffDistance : 0f;
                snapshot.PredictedStopDistanceM = Finite(runtime.PredictedStopDistance)
                    ? runtime.PredictedStopDistance : 0f;
                snapshot.RunwayRemainingM = runtime.RunwayRemaining;
                snapshot.ObservedMaxSpeedMps = runtime.ObservedMaxSpeed;
                snapshot.ObservedAntiStallSpeedMps = runtime.ObservedAntiStallSpeed;
            }
            V2AdvisoryState advisory;
            if (v2Advisories.TryGetValue(record.Session.SessionId, out advisory) && advisory != null)
            {
                snapshot.ExternalTrimActive = advisory.TrimPublished &&
                    Time.realtimeSinceStartup < advisory.TrimExpires;
                snapshot.ExternalTrimReason = advisory.TrimReason ?? string.Empty;
                snapshot.ExternalTaskDisplayActive = advisory.TaskPublished &&
                    advisory.Task != null && Time.realtimeSinceStartup < advisory.TaskExpires;
                snapshot.ExternalTaskSource = advisory.Task != null
                    ? advisory.Task.SourceId ?? string.Empty : string.Empty;
                snapshot.ResourceOverrideActive = advisory.ResourcePublished &&
                    advisory.Resource != null && Time.realtimeSinceStartup < advisory.ResourceExpires;
                snapshot.ResourceOverrideOwner = advisory.Resource != null
                    ? advisory.Resource.OwnerClientId ?? string.Empty : string.Empty;
                snapshot.InfinitePropellantActive = snapshot.ResourceOverrideActive &&
                    advisory.Resource.InfinitePropellantActive;
                snapshot.InfiniteElectricityActive = snapshot.ResourceOverrideActive &&
                    advisory.Resource.InfiniteElectricityActive;
            }
            return snapshot;
        }

        private bool TryGetV2PrimaryDisplay(SessionRecord record, ref string ownerLine,
            ref string taskLine, ref string stateLine)
        {
            if (record == null) return false;
            V2AdvisoryState advisory;
            if (v2Advisories.TryGetValue(record.Session.SessionId, out advisory) && advisory != null &&
                advisory.TaskPublished && advisory.Task != null &&
                Time.realtimeSinceStartup < advisory.TaskExpires)
            {
                ownerLine = "EXT AP: " + (string.IsNullOrEmpty(advisory.Task.DisplayName)
                    ? record.Session.ClientId : advisory.Task.DisplayName);
                taskLine = "TASK: " + Safe(advisory.Task.Task);
                stateLine = "STATE: " + Safe(advisory.Task.Phase) + " " +
                    Mathf.RoundToInt(Mathf.Clamp01(advisory.Task.Progress01) * 100f) + "%";
                return true;
            }
            return false;
        }

        internal bool TryGetV2SupplementalDisplay(out string primary,
            out string secondary, out string resource)
        {
            primary = secondary = resource = string.Empty;
            Guid ownerId;
            SessionRecord record;
            if (!vesselOwners.TryGetValue(ActiveVesselId(), out ownerId) ||
                !sessions.TryGetValue(ownerId, out record)) return false;
            return TryGetV2SupplementalDisplay(record, out primary, out secondary, out resource);
        }

        private bool TryGetV2SupplementalDisplay(SessionRecord record,
            out string primary, out string secondary, out string resource)
        {
            primary = secondary = resource = string.Empty;
            if (record == null) return false;
            V2AdvisoryState advisory;
            if (!v2Advisories.TryGetValue(record.Session.SessionId, out advisory) || advisory == null)
                return false;
            float now = Time.realtimeSinceStartup;
            if (advisory.TaskPublished && advisory.Task != null && now < advisory.TaskExpires)
            {
                primary = Safe(advisory.Task.PrimaryStatus);
                secondary = Safe(advisory.Task.SecondaryStatus);
            }
            if (advisory.ResourcePublished && advisory.Resource != null &&
                now < advisory.ResourceExpires)
            {
                resource = "RESOURCE: " + Safe(advisory.Resource.OwnerClientId) + " " +
                    (advisory.Resource.InfinitePropellantActive ? "∞FUEL " : string.Empty) +
                    (advisory.Resource.InfiniteElectricityActive ? "∞EC" : string.Empty);
            }
            return !string.IsNullOrEmpty(primary) || !string.IsNullOrEmpty(secondary) ||
                !string.IsNullOrEmpty(resource);
        }

        bool TryEstimateKscRunwayRemaining(Vessel vessel, out float remaining)
        {
            remaining = 0f;
            if (vessel == null || vessel.mainBody == null ||
                !string.Equals(vessel.mainBody.bodyName, "Kerbin",
                    StringComparison.OrdinalIgnoreCase)) return false;
            float heading = core != null && core.Attitude != null &&
                core.Attitude.InstrumentHeadingValid
                ? core.Attitude.InstrumentHeadingDeg : 90f;
            bool eastbound = Mathf.Abs(Mathf.DeltaAngle(heading, 90f)) <=
                Mathf.Abs(Mathf.DeltaAngle(heading, 270f));
            const double eastLatitude = -0.050185;
            const double eastLongitude = -74.4947394;
            const double westLatitude = -0.0485981;
            const double westLongitude = -74.7359549847328;
            double targetLatitude = eastbound ? eastLatitude : westLatitude;
            double targetLongitude = eastbound ? eastLongitude : westLongitude;
            double value = SurfaceDistanceMeters(vessel, vessel.latitude,
                vessel.longitude, targetLatitude, targetLongitude);
            double nearestEnd = Math.Min(
                SurfaceDistanceMeters(vessel, vessel.latitude, vessel.longitude,
                    eastLatitude, eastLongitude),
                SurfaceDistanceMeters(vessel, vessel.latitude, vessel.longitude,
                    westLatitude, westLongitude));
            if (!Finite(value) || !Finite(nearestEnd) || nearestEnd > 3500.0)
                return false;
            remaining = Mathf.Clamp((float)value, 0f, 3500f);
            return remaining >= 100f;
        }

        static bool TerrainSamplingAvailable()
        {
            if (terrainMethodResolved) return terrainAltitudeMethod != null;
            terrainMethodResolved = true;
            try
            {
                MethodInfo[] methods = typeof(CelestialBody).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (!string.Equals(method.Name, "TerrainAltitude",
                        StringComparison.Ordinal)) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    bool two = parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(double) &&
                        parameters[1].ParameterType == typeof(double);
                    bool three = parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(double) &&
                        parameters[1].ParameterType == typeof(double) &&
                        parameters[2].ParameterType == typeof(bool);
                    if (two || three)
                    {
                        terrainAltitudeMethod = method;
                        break;
                    }
                }
            }
            catch
            {
                terrainAltitudeMethod = null;
            }
            return terrainAltitudeMethod != null;
        }

        static bool TrySampleTerrainAsl(CelestialBody body, double latitude,
            double longitude, out double terrainAsl)
        {
            terrainAsl = 0.0;
            if (body == null || !Finite(latitude) || !Finite(longitude) ||
                !TerrainSamplingAvailable()) return false;
            try
            {
                ParameterInfo[] parameters = terrainAltitudeMethod.GetParameters();
                object raw = parameters.Length == 2
                    ? terrainAltitudeMethod.Invoke(body, new object[] { latitude, longitude })
                    : terrainAltitudeMethod.Invoke(body,
                        new object[] { latitude, longitude, false });
                terrainAsl = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return Finite(terrainAsl);
            }
            catch
            {
                terrainAsl = 0.0;
                return false;
            }
        }

        bool TryValidateCorridorTerrain(Vessel vessel,
            AERISLearningCorridorRequest request, out string detail)
        {
            detail = string.Empty;
            if (vessel == null || vessel.mainBody == null || request == null)
            {
                detail = "Learning corridor terrain scan has no valid vessel/body/request.";
                return false;
            }
            if (!TerrainSamplingAvailable())
            {
                detail = "CelestialBody.TerrainAltitude is unavailable; LearningCorridor capability is not executable.";
                return false;
            }

            const double kscLatitude = -0.050185;
            const double kscLongitude = -74.490867;
            double distanceFromKsc = SurfaceDistanceMeters(vessel, vessel.latitude,
                vessel.longitude, kscLatitude, kscLongitude);
            if (!Finite(distanceFromKsc) || distanceFromKsc > 30000.0)
            {
                detail = "StockKscEastOcean corridor requires the vessel within 30 km of KSC Runway 09.";
                return false;
            }

            double radius = Math.Max(1.0, vessel.mainBody.Radius +
                Math.Max(0.0, request.TargetAltitudeM));
            double scanLength = Math.Min(260000.0,
                Math.Max(80000.0, request.LegLengthM) +
                Math.Max(10000.0, request.TurnMarginM));
            double step = 5000.0;
            int samples = 0;
            double maximumTerrain = double.MinValue;
            double requiredMinimumAltitude = double.MinValue;
            for (double along = 0.0; along <= scanLength + 1.0; along += step)
            {
                double centerLat;
                double centerLon;
                DestinationPoint(radius, vessel.latitude, vessel.longitude, 90.0,
                    along, out centerLat, out centerLon);
                for (int side = -1; side <= 1; side++)
                {
                    double sampleLat = centerLat;
                    double sampleLon = centerLon;
                    if (side != 0)
                    {
                        double crossBearing = side < 0 ? 0.0 : 180.0;
                        DestinationPoint(radius, centerLat, centerLon, crossBearing,
                            request.CorridorHalfWidthM, out sampleLat, out sampleLon);
                    }
                    double terrain;
                    if (!TrySampleTerrainAsl(vessel.mainBody, sampleLat, sampleLon,
                        out terrain))
                    {
                        detail = "Learning corridor terrain scan failed at sample " +
                            samples.ToString(CultureInfo.InvariantCulture) + ".";
                        return false;
                    }
                    terrain = Math.Max(0.0, terrain);
                    maximumTerrain = Math.Max(maximumTerrain, terrain);
                    requiredMinimumAltitude = Math.Max(requiredMinimumAltitude,
                        terrain + request.MinimumTerrainClearanceM);
                    samples++;
                }
            }

            double endpointLat;
            double endpointLon;
            DestinationPoint(radius, vessel.latitude, vessel.longitude, 90.0,
                Math.Max(80000.0, request.LegLengthM), out endpointLat, out endpointLon);
            double turnRadius = Math.Max(300.0, Math.Min(
                request.CorridorHalfWidthM * 0.45, request.TurnMarginM * 0.45));
            for (int angle = 0; angle < 360; angle += 30)
            {
                double sampleLat;
                double sampleLon;
                DestinationPoint(radius, endpointLat, endpointLon, angle,
                    turnRadius * 2.0, out sampleLat, out sampleLon);
                double terrain;
                if (!TrySampleTerrainAsl(vessel.mainBody, sampleLat, sampleLon,
                    out terrain))
                {
                    detail = "Learning corridor turn-zone terrain scan failed.";
                    return false;
                }
                terrain = Math.Max(0.0, terrain);
                maximumTerrain = Math.Max(maximumTerrain, terrain);
                requiredMinimumAltitude = Math.Max(requiredMinimumAltitude,
                    terrain + request.MinimumTerrainClearanceM);
                samples++;
            }

            if (request.TargetAltitudeM + 0.5 < requiredMinimumAltitude)
            {
                detail = "Requested corridor altitude " + request.TargetAltitudeM +
                    "m is below scanned terrain clearance requirement " +
                    requiredMinimumAltitude.ToString("0", CultureInfo.InvariantCulture) +
                    "m ASL (max terrain " +
                    maximumTerrain.ToString("0", CultureInfo.InvariantCulture) + "m).";
                return false;
            }
            detail = "PASS samples=" + samples.ToString(CultureInfo.InvariantCulture) +
                " maxTerrain=" + maximumTerrain.ToString("0",
                    CultureInfo.InvariantCulture) + "m requiredAlt=" +
                requiredMinimumAltitude.ToString("0",
                    CultureInfo.InvariantCulture) + "m";
            return true;
        }

        float CorridorPlanningDeceleration()
        {
            float value = core != null && core.Settings != null
                ? core.Settings.VelocityAccelerationLimitMps2 : 4f;
            return Mathf.Clamp(value, 0.5f, 10f);
        }

        static float CorridorRequiredReserve(float cruiseSpeed, float turnSpeed,
            float deceleration, float turnRadius)
        {
            cruiseSpeed = Mathf.Max(0f, cruiseSpeed);
            turnSpeed = Mathf.Clamp(turnSpeed, 0f, cruiseSpeed);
            deceleration = Mathf.Max(0.1f, deceleration);
            float kinetic = cruiseSpeed > turnSpeed
                ? (cruiseSpeed * cruiseSpeed - turnSpeed * turnSpeed) /
                  (2f * deceleration) : 0f;
            return Mathf.Max(10000f, kinetic + cruiseSpeed * 3f +
                Mathf.Max(300f, turnRadius) + 1000f);
        }

        static float LocalGravity(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null) return 9.80665f;
            try
            {
                double radius = vessel.mainBody.Radius +
                    Math.Max(0.0, vessel.altitude);
                double gravity = vessel.mainBody.gravParameter /
                    Math.Max(1.0, radius * radius);
                return Finite(gravity)
                    ? Mathf.Clamp((float)gravity, 0.1f, 100f) : 9.80665f;
            }
            catch
            {
                return 9.80665f;
            }
        }

        static void DestinationPoint(double radius, double latitude,
            double longitude, double bearingDeg, double distanceMeters,
            out double destinationLatitude, out double destinationLongitude)
        {
            double angular = distanceMeters / Math.Max(1.0, radius);
            double bearing = bearingDeg * Math.PI / 180.0;
            double latitudeRad = latitude * Math.PI / 180.0;
            double longitudeRad = longitude * Math.PI / 180.0;
            double sinLatitude = Math.Sin(latitudeRad);
            double cosLatitude = Math.Cos(latitudeRad);
            double sinAngular = Math.Sin(angular);
            double cosAngular = Math.Cos(angular);
            double destinationLatitudeRad = Math.Asin(
                sinLatitude * cosAngular +
                cosLatitude * sinAngular * Math.Cos(bearing));
            double destinationLongitudeRad = longitudeRad + Math.Atan2(
                Math.Sin(bearing) * sinAngular * cosLatitude,
                cosAngular - sinLatitude * Math.Sin(destinationLatitudeRad));
            destinationLatitude = destinationLatitudeRad * 180.0 / Math.PI;
            destinationLongitude = Repeat360(
                destinationLongitudeRad * 180.0 / Math.PI + 180.0) - 180.0;
        }

        static float MedianSpeed(List<TimedSpeedSample> samples, float minimumTime)
        {
            var values = new List<float>();
            if (samples != null)
                for (int i = 0; i < samples.Count; i++)
                    if (samples[i].Time >= minimumTime && Finite(samples[i].Speed)) values.Add(samples[i].Speed);
            if (values.Count == 0) return 0f;
            values.Sort();
            int middle = values.Count / 2;
            return values.Count % 2 == 0 ? (values[middle - 1] + values[middle]) * 0.5f : values[middle];
        }

        static void ApplyBrake(Vessel vessel, bool on)
        {
            if (vessel == null) return;
            try { vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, on); }
            catch { }
        }

        static float SignedForwardSpeed(Vessel vessel)
        {
            if (vessel == null || vessel.ReferenceTransform == null) return 0f;
            float value = (float)Vector3d.Dot(vessel.srf_velocity, vessel.ReferenceTransform.up);
            return Finite(value) ? value : 0f;
        }

        static double SurfaceDistanceMeters(Vessel vessel, double lat1, double lon1,
            double lat2, double lon2)
        {
            if (vessel == null || vessel.mainBody == null) return 0.0;
            double r = Math.Max(1.0, vessel.mainBody.Radius + Math.Max(0.0, vessel.altitude));
            double p1 = lat1 * Math.PI / 180.0;
            double p2 = lat2 * Math.PI / 180.0;
            double dp = (lat2 - lat1) * Math.PI / 180.0;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dp * 0.5) * Math.Sin(dp * 0.5) +
                Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl * 0.5) * Math.Sin(dl * 0.5);
            return 2.0 * r * Math.Atan2(Math.Sqrt(Math.Max(0.0, a)),
                Math.Sqrt(Math.Max(0.0, 1.0 - a)));
        }

        static double InitialBearing(double lat1, double lon1, double lat2, double lon2)
        {
            double p1 = lat1 * Math.PI / 180.0;
            double p2 = lat2 * Math.PI / 180.0;
            double dl = (lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dl) * Math.Cos(p2);
            double x = Math.Cos(p1) * Math.Sin(p2) -
                Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
            return Repeat360(Math.Atan2(y, x) * 180.0 / Math.PI);
        }
    }
}
