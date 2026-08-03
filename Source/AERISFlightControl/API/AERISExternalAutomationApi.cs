using System;
using AERISFlightControl.Core;

namespace AERISFlightControl.API
{
    // Contract v2 numeric slots remain stable. Navigation and AutoLanding are reserved but
    // unavailable after the legacy NAV removal; no runtime entry point advertises them.
    public enum AERISAutomationCapability
    {
        SetpointGuidance = 0,
        Navigation = 1,
        AutoLanding = 2,
        GroundPropulsionTest = 3,
        AutoTakeoff = 4,
        LearningCorridor = 5,
        EnvelopeSurvey = 6,
        AntiStallEvent = 7,
        ControlAuthorityTelemetry = 8,
        GroundAssistStop = 9,
        ExternalTrimFeedForward = 10,
        ExternalTaskDisplay = 11,
        ResourceOverrideCoordination = 12
    }

    public enum AERISAutomationPriority { Advisory = 10, TaskAutomation = 20, MissionAutomation = 30 }
    public enum AERISAutomationMissionState { Idle, Accepted, Running, Suspended, Recovering, Completed, Failed, Cancelled }
    // Additive AERIS diagnostics. These do not alter the published v2 fields above.
    public enum AERISAutomationResultCode
    {
        None, Accepted, Busy, InvalidRequest, VesselUnavailable, WrongVessel,
        CapabilityUnavailable, ProtectUnavailable, PilotOverrideActive, LeaseExpired,
        SessionNotFound, PlanNotFound, PlanIncompatible, NavUnavailable,
        ApproachUnavailable, RejectedBySafety, InternalFault, ProtectIntervention,
        AntiStallIntervention, ControlSaturation, ReverseThrust, RpmLimit, LoadLimit,
        NoForwardAcceleration, RunwayRemainingInsufficient, RouteGenerationFailed,
        TerrainClearanceFailed, SetpointUnreachable, OperationalCeiling, LandingFailed,
        GroundStopTimeout, NonFiniteState, VesselChanged, SceneChanged
    }

    public enum AERISAutomationState
    {
        Idle, Acquired, Configuring, Executing, Stabilizing, Stable, NavIntercept,
        NavEnroute, NavApproach, GoAround, Landing, GroundRoll, Completed,
        SuspendedByPilot, SuspendedByProtect, Rejected, Cancelled, Faulted, LeaseExpired
    }

    public enum AERISSetpointCompletionPolicy { CaptureOnly, StableCondition, HoldUntilReplaced }

    public sealed class AERISAutomationAcquireRequest
    {
        public Vessel Vessel;
        public Guid VesselId;
        public string ClientId;
        public string DisplayName;
        public string Purpose;
        public AERISAutomationPriority Priority;
        public AERISAutomationCapability[] RequestedCapabilities;
        public float RequestedTtlSeconds;

        // Additive compatibility/safety controls.
        public double LeaseSeconds;
        public bool AllowPilotOverride = true;
        public bool RequireProtect = true;
    }

    public sealed class AERISAutomationSession
    {
        public Guid SessionId;
        public Guid VesselId;
        public string ClientId;
        public AERISAutomationCapability[] GrantedCapabilities;
        public float ExpiresRealtime;

        // Additive diagnostic clock; realtime remains the normative v2 heartbeat clock.
        public double ExpiresUniversalTime;
    }

    public sealed class AERISAutomationCommandHandle
    {
        public Guid CommandId;
        public Guid SessionId;
        public string TaskKind;

        // Additive v1 diagnostic aliases.
        public string Kind;
        public double AcceptedUniversalTime;
    }

    public sealed class AERISAutomationResult
    {
        public bool Success;
        public string Code;
        public string Detail;

        // Additive typed result and retry hint.
        public AERISAutomationResultCode ResultCode;
        public bool Retryable;
    }

    public sealed class AERISGroundPropulsionTestRequest
    {
        public Vessel Vessel; public Guid VesselId;
        public float PowerDemand01; public float PropellerNormalizedPitch;
        public bool BrakesInitiallyApplied; public bool GroundStabilityRequired;
        public bool RunwayHeadingHoldRequired; public bool MicroRoll;
        public float MaximumDurationSeconds; public float MaximumGroundSpeedMps;
        public float MaximumTravelDistanceM; public bool AbortOnReverseThrust;
        public bool AbortOnControlSaturation; public bool CompletionRequiresFullStop;
        public bool ReplaceCurrentMission;
    }

    public sealed class AERISAutoTakeoffMissionRequest
    {
        public Vessel Vessel; public Guid VesselId;
        public float PropellerNormalizedPitch; public int TargetAltitudeM;
        public string RouteId; public string SpeedPolicy;
        public bool AutoStage; public bool ProtectRequired; public bool AntiStallRequired;
        public bool GroundStabilityRequired; public bool AllowAbort;
        public bool ReplaceCurrentMission;
    }

    public sealed class AERISLearningCorridorRequest
    {
        public Vessel Vessel; public Guid VesselId; public string RouteId; public string BodyName;
        public string AnchorPolicy; public int TargetAltitudeM; public float LegLengthM;
        public float CorridorHalfWidthM; public float MinimumTerrainClearanceM;
        public float TurnBankLimitDeg; public float TurnMarginM; public bool ShuttlePattern;
        public bool ValidateTerrainBeforeStart; public bool ObstacleAvoidanceRequired;

        // Additive planning hint used only to size an auto-generated corridor.
        public float ExpectedMaximumSpeedMps;
        public bool ReplaceCurrentMission;
    }

    public sealed class AERISSetpointMissionRequest
    {
        public Vessel Vessel; public Guid VesselId; public int AltitudeM; public float TrueAirspeedMps;
        public string RouteId; public bool RequireStraightCorridor; public bool RequireStableCondition;
        public float AltitudeToleranceM; public float SpeedToleranceMps;
        public float VerticalSpeedToleranceMps; public float BankToleranceDeg; public float StableSeconds;

        // Additive compatibility fields. Contract-v2 clients may omit these; AERIS then
        // retains the current lateral route/heading and uses TAS with a documented fallback.
        public double AltitudeMeters;
        public double SurfaceSpeedMps;
        public double HeadingDeg;
        public bool UseExplicitHeading;
        public float HeadingToleranceDeg;
        public float ThrottleHint01;
        public AERISSetpointCompletionPolicy CompletionPolicy;
        public bool ReplaceCurrentMission;
    }

    public sealed class AERISEnvelopeSurveyRequest
    {
        public Vessel Vessel; public Guid VesselId; public int AltitudeM;
        public float PropellerNormalizedPitch; public string RouteId; public bool MaximumSafePower;
        public float AccelerationPlateauMps2; public float PlateauHoldSeconds;
        public float MaximumDurationSeconds; public bool ProtectRequired;
        public bool ReplaceCurrentMission;
    }

    public sealed class AERISAntiStallSurveyRequest
    {
        public Vessel Vessel; public Guid VesselId; public int AltitudeM;
        public float PropellerNormalizedPitch; public string RouteId; public float DecelerationMps2;
        public float MinimumSurveySpeedMps; public bool AntiStallMustRemainEnabled;
        public bool StopOnAntiStallEvent; public bool RecoverAutomatically;
        public bool ReplaceCurrentMission;
    }

    public sealed class AERISClimbMissionRequest
    {
        public Vessel Vessel; public Guid VesselId; public int TargetAltitudeM;
        public float SoftTargetTrueAirspeedMps; public string RouteId;
        public bool PrioritizeAltitudeAcquisition; public bool AllowSpeedReduction;
        public float MaximumDurationSeconds;
        public bool ReplaceCurrentMission;
    }


    public sealed class AERISExternalTrimFeedForwardRequest
    {
        public Vessel Vessel; public Guid VesselId;
        public float RollFeedForward; public float PitchFeedForward; public float YawFeedForward;
        public float Confidence01; public string Reason; public float ExpiresSeconds;
    }

    public sealed class AERISExternalTaskDisplayRequest
    {
        public Vessel Vessel; public Guid VesselId;
        public string SourceId; public string DisplayName; public string Task; public string Phase;
        public string PrimaryStatus; public string SecondaryStatus;
        public float Progress01; public float ExpiresSeconds;
    }

    public sealed class AERISResourceOverrideStatusRequest
    {
        public Vessel Vessel; public Guid VesselId; public string OwnerClientId;
        public bool InfinitePropellantActive; public bool InfiniteElectricityActive;
        public string Detail; public float ExpiresSeconds;
    }

    public sealed class AERISAutomationSnapshot
    {
        public AERISAutomationMissionState State;
        public string Detail; public string FailureCode;
        public bool MissionCompleted; public bool MissionFailed; public bool ConditionStable;
        public bool ProtectIntervening; public bool AntiStallActive; public bool AntiStallEventLatched;
        public bool PilotOverride; public bool OnGround; public bool GroundAssistStopped;
        public bool StraightCorridor; public bool Turning; public bool RouteRecapture;
        public bool ObstacleAvoidance; public string CorridorDirection; public int CorridorPassIndex;
        public bool ControlSaturated;
        public float ControlAuthority01; public float RollAuthority01; public float PitchAuthority01; public float YawAuthority01;
        public float GroundSpeedMps; public float TrueAirspeedMps; public float AltitudeM; public float VerticalSpeedMps;
        public float HeadingDeg; public float BankDeg; public float RollRateDegPerSec; public float PitchRateDegPerSec; public float YawRateDegPerSec;
        public float RollControlCommand; public float PitchControlCommand; public float YawControlCommand;
        public float RequestedThrottle01;
        public float ObservedForwardAccelerationMps2; public float PredictedTakeoffDistanceM;
        public float PredictedStopDistanceM; public float RunwayRemainingM;
        public float ObservedMaxSpeedMps; public float ObservedAntiStallSpeedMps;
        public float LeaseRemainingSeconds;

        // Additive identifiers and advisory diagnostics.
        public Guid SessionId;
        public Guid VesselId;
        public string ClientId;
        public AERISAutomationState DetailedState;
        public AERISAutomationResultCode FailureResultCode;
        public string CurrentCommandId;
        public bool ExternalTrimActive;
        public string ExternalTrimReason;
        public bool ExternalTaskDisplayActive;
        public string ExternalTaskSource;
        public bool ResourceOverrideActive;
        public string ResourceOverrideOwner;
        public bool InfinitePropellantActive;
        public bool InfiniteElectricityActive;
        public string AirspeedSource;
    }

    // Additive query result retained from Contract v1.


    public static class AERISExternalAutomationApi
    {
        public const string ContractVersion = "2";
        static AERISExternalAutomationManager manager;

        internal static void Bind(AERISExternalAutomationManager value) { manager = value; }
        internal static void Unbind(AERISExternalAutomationManager value)
        {
            if (ReferenceEquals(manager, value)) manager = null;
        }

        public static AERISAutomationCapability[] GetCapabilities()
        {
            try { return manager == null ? new AERISAutomationCapability[0] : manager.GetCapabilities(); }
            catch { return new AERISAutomationCapability[0]; }
        }

        public static bool TryAcquire(AERISAutomationAcquireRequest request,
            out AERISAutomationSession session, out AERISAutomationResult result)
        {
            session = new AERISAutomationSession();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryAcquire(request, out session, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TryRenew(AERISAutomationSession session, float ttlSeconds,
            out AERISAutomationResult result)
        {
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryRenew(session, ttlSeconds, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitGroundPropulsionTest(AERISAutomationSession session,
            AERISGroundPropulsionTestRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitGroundPropulsionTest(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitAutoTakeoff(AERISAutomationSession session,
            AERISAutoTakeoffMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitAutoTakeoff(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitLearningCorridor(AERISAutomationSession session,
            AERISLearningCorridorRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitLearningCorridor(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitSetpointMission(AERISAutomationSession session,
            AERISSetpointMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitSetpointMission(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitEnvelopeSurvey(AERISAutomationSession session,
            AERISEnvelopeSurveyRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitEnvelopeSurvey(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitAntiStallSurvey(AERISAutomationSession session,
            AERISAntiStallSurveyRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitAntiStallSurvey(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TrySubmitClimbMission(AERISAutomationSession session,
            AERISClimbMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TrySubmitClimbMission(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }


        public static bool TryPublishExternalTrimFeedForward(AERISAutomationSession session,
            AERISExternalTrimFeedForwardRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryPublishExternalTrimFeedForward(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TryPublishExternalTaskDisplay(AERISAutomationSession session,
            AERISExternalTaskDisplayRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryPublishExternalTaskDisplay(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TryPublishResourceOverrideStatus(AERISAutomationSession session,
            AERISResourceOverrideStatusRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryPublishResourceOverrideStatus(session, request, out command, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TryGetStatus(AERISAutomationSession session, out AERISAutomationSnapshot snapshot)
        {
            snapshot = new AERISAutomationSnapshot();
            try { return manager != null && manager.TryGetStatus(session, out snapshot); }
            catch { return false; }
        }

        public static bool TryCancelCurrentMission(AERISAutomationSession session,
            string reason, out AERISAutomationResult result)
        {
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryCancelCurrentMission(session, reason, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }

        public static bool TryRelease(AERISAutomationSession session, out AERISAutomationResult result)
        {
            if (manager == null) { result = Unavailable("AERIS external automation manager is not ready."); return false; }
            try { return manager.TryRelease(session, out result); }
            catch (Exception ex) { result = BoundaryFault(ex); return false; }
        }


        static AERISAutomationResult Unavailable(string detail)
        {
            return new AERISAutomationResult
            {
                Success = false, Code = "INTERNAL_FAULT", ResultCode = AERISAutomationResultCode.InternalFault,
                Detail = detail, Retryable = true
            };
        }

        static AERISAutomationResult BoundaryFault(Exception ex)
        {
            return new AERISAutomationResult
            {
                Success = false, Code = "INTERNAL_FAULT", ResultCode = AERISAutomationResultCode.InternalFault,
                Detail = "AERIS API boundary isolated " + ex.GetType().Name + ".", Retryable = true
            };
        }
    }
}
