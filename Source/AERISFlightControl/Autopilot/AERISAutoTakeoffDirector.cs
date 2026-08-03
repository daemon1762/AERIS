using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;
using AERISFlightControl.Protect;
using AERISFlightControl.Settings;
using AERISFlightControl.API;
using AERISFlightControl.Integrations;

namespace AERISFlightControl.Autopilot
{
    internal enum AutoTakeoffPhase
    {
        Off,
        Armed,
        BrakeHold,
        Spool,
        GroundRoll,
        Rotate,
        LiftoffConfirm,
        InitialClimb,
        Handoff,
        Aborted
    }

    // AUTO TAKEOFF is an AUTOPILOT mode, separate from Ground Stability PROTECT.
    // Ground yaw/roll remains delegated to the protection controller.  This director
    // owns only brakes, throttle and pitch-rate during the takeoff sequence.
    internal sealed class AERISAutoTakeoffDirector
    {
        internal const float MaximumTakeoffThrottle = 1.0f;
        readonly AERISSettings settings;
        bool ownsThrottle;
        bool ownsPitch;
        bool ownsBrakes;
        bool vrFrozen;
        bool handoffRequested;
        float phaseElapsed;
        float lastControlFixedTime;
        float strongPilotInputSeconds;
        float handoffStableSeconds;
        float selectedStallSpeedMps;
        float selectedVrMps;
        string selectedVrSource = "NONE";
        string selectedVrDetail = string.Empty;
        bool engineStageActivationRequested;
        int engineStageNumber = -1;
        string engineStageStatus = "NOT REQUESTED";
        bool brakeWasAppliedAtExecute;
        bool brakeReleaseConfirmed;
        uint brakeOwnedVesselPersistentId;
        bool externalPropulsionTakeoff;
        uint armedVesselPersistentId;
        uint attemptGeneration;
        string propulsionMode = "KSP ENGINE STAGE";
        string selectedExternalProviderId = string.Empty;

        internal AERISAutoTakeoffDirector(AERISSettings settings)
        {
            this.settings = settings;
            Phase = AutoTakeoffPhase.Off;
            Status = "OFF";
        }

        internal AutoTakeoffPhase Phase { get; private set; }
        internal string PhaseText { get { return PhaseName(Phase); } }
        internal string Status { get; private set; }
        internal bool Armed { get { return Phase != AutoTakeoffPhase.Off && Phase != AutoTakeoffPhase.Aborted; } }
        internal bool Executing { get { return (int)Phase >= (int)AutoTakeoffPhase.BrakeHold && (int)Phase <= (int)AutoTakeoffPhase.Handoff; } }
        internal bool RequiresGroundLateralAssist { get { return (int)Phase >= (int)AutoTakeoffPhase.BrakeHold && (int)Phase <= (int)AutoTakeoffPhase.LiftoffConfirm; } }
        internal bool RequiresAirborneLateralAssist { get { return Phase == AutoTakeoffPhase.InitialClimb || Phase == AutoTakeoffPhase.Handoff; } }
        internal bool RequiresLateralAssist { get { return RequiresGroundLateralAssist || RequiresAirborneLateralAssist; } }
        internal bool HandoffRequested { get { return handoffRequested; } }
        internal bool VrFrozen { get { return vrFrozen; } }
        internal float SelectedStallSpeedMps { get { return selectedStallSpeedMps; } }
        internal float SelectedVrMps { get { return selectedVrMps; } }
        internal string SelectedVrSource { get { return selectedVrSource; } }
        internal string SelectedVrDetail { get { return selectedVrDetail; } }
        internal float SurfaceSpeedMps { get; private set; }
        internal float RadarAltitudeM { get; private set; }
        internal float VerticalSpeedMps { get; private set; }
        internal float PitchRateDemandDegPerSec { get; private set; }
        internal float ThrottleDemand { get; private set; }
        internal bool AaNativePitchOverrideActive { get; private set; }
        internal bool AaNativeThrottleOverrideActive { get; private set; }
        internal bool RotationGateReady { get; private set; }
        internal string RotationGateReason { get; private set; }
        internal string LastAbortReason { get; private set; }
        internal bool EngineStageActivationRequested { get { return engineStageActivationRequested; } }
        internal int EngineStageNumber { get { return engineStageNumber; } }
        internal string EngineStageStatus { get { return engineStageStatus; } }
        internal bool BrakeWasAppliedAtExecute { get { return brakeWasAppliedAtExecute; } }
        internal bool BrakeReleaseConfirmed { get { return brakeReleaseConfirmed; } }
        internal bool ExternalPropulsionTakeoff { get { return externalPropulsionTakeoff; } }
        internal string PropulsionMode { get { return propulsionMode; } }
        internal string SelectedExternalProviderId { get { return selectedExternalProviderId; } }
        internal uint AttemptGeneration { get { return attemptGeneration; } }
        internal uint ArmedVesselPersistentId { get { return armedVesselPersistentId; } }
        internal string BrakeStatus
        {
            get
            {
                if (Phase == AutoTakeoffPhase.BrakeHold || Phase == AutoTakeoffPhase.Spool)
                    return "AUTO HOLD";
                if (brakeReleaseConfirmed && Executing) return "AUTO RELEASE CONFIRMED";
                return "STANDBY";
            }
        }

        internal bool TryArm(Vessel vessel, VirtualAttitudeInstrument attitude,
            TopModuleManager manager, GroundStabilityProtection ground, bool master, out string error)
        {
            error = null;
            BeginNewArmAttempt(vessel);
            if (vessel == null || attitude == null || manager == null)
                error = "Active vessel / AA manager unavailable.";
            else if (!master || !manager.IsStandardActive)
                error = "Turn MASTER ON and wait for AA Standard FBW.";
            else if (vessel.packed)
                error = "Vessel is on rails.";
            else if (vessel.situation == Vessel.Situations.SPLASHED)
                error = "Auto Takeoff is not available while splashed.";
            else if (ground == null || !ground.GroundAssistMasterEnabled || !ground.Enabled || !ground.ReliableGrounded || !ground.Available)
                error = "Ground Stability PROTECT must be enabled and reliably grounded.";
            else if (manager.PitchController == null || !manager.PitchController.moderate_aoa ||
                !manager.PitchController.moderate_g)
                error = "AA pitch AoA/G moderation must be enabled before ARM.";
            else if (!attitude.InstrumentHeadingValid || !attitude.InstrumentPitchValid)
                error = "Heading/pitch reference is not valid.";
            if (error != null) return false;

            ReleaseAxisOwnership();
            ReleaseBrakes(vessel);
            LastAbortReason = string.Empty;
            vrFrozen = false;
            handoffRequested = false;
            engineStageActivationRequested = false;
            engineStageNumber = -1;
            engineStageStatus = "AWAITING EXECUTE";
            brakeWasAppliedAtExecute = false;
            brakeReleaseConfirmed = false;
            externalPropulsionTakeoff = false;
            propulsionMode = "KSP ENGINE STAGE";
            selectedExternalProviderId = string.Empty;
            armedVesselPersistentId = vessel.persistentId;
            strongPilotInputSeconds = 0f;
            handoffStableSeconds = 0f;
            SelectVr(manager, false);
            ground.RecaptureCurrentHeading(attitude);
            SetPhase(AutoTakeoffPhase.Armed, "ARMED — verify configuration, then EXECUTE TAKEOFF");
            AERISLogger.Info("[AUTO_TAKEOFF] ARM; stall=" + selectedStallSpeedMps.ToString("F2") +
                " m/s; Vr=" + selectedVrMps.ToString("F2") + " m/s; source=" + selectedVrSource +
                "; detail=" + selectedVrDetail);
            return true;
        }

        internal bool TryExecute(Vessel vessel, GroundStabilityProtection ground, bool master, out string error)
        {
            error = null;
            if (Phase != AutoTakeoffPhase.Armed) error = "ARM AUTO TAKEOFF first.";
            else if (!master) error = "MASTER is OFF.";
            else if (vessel == null || ground == null || !ground.ReliableGrounded || !ground.Available)
                error = "Reliable ground state was lost.";
            if (error != null) return false;

            // Capture and hold the parking brake before requesting engine start.  The
            // v0.9.1 ordering asked KSP to stage first, leaving an unbraked aircraft a
            // small window in which an engine could light before AERIS owned the brake.
            brakeWasAppliedAtExecute = vessel.ActionGroups[KSPActionGroup.Brakes];
            brakeReleaseConfirmed = false;
            SetBrakes(vessel, true);
            AERISLogger.Info("[AUTO_TAKEOFF] BRAKE CAPTURE; pre-existing=" +
                (brakeWasAppliedAtExecute ? "ON" : "OFF") + "; held before engine start.");
            if (!TryActivateFirstEngineStage(vessel, out error))
            {
                if (engineStageActivationRequested)
                    FailExecuteWithBrakesHeld(vessel, error);
                else
                    RestorePreExecuteBrake(vessel);
                return false;
            }
            SetPhase(AutoTakeoffPhase.BrakeHold, "BRAKE HOLD — engine start requested; parking brake captured");
            return true;
        }

        internal void ApplyAaNativeTakeoffDemand(FlightCtrlState state, Vessel vessel,
            VirtualAttitudeInstrument attitude, TopModuleManager manager,
            GroundStabilityProtection ground, ProtectTelemetry protect, bool master,
            bool standardFbwActive)
        {
            if (!Armed) return;
            if (vessel != null && armedVesselPersistentId != 0u && vessel.persistentId != armedVesselPersistentId)
            {
                EmergencyRelease(vessel, "active vessel identity changed");
                return;
            }
            if (state == null || vessel == null || attitude == null || manager == null ||
                !master || !standardFbwActive || vessel.packed)
            {
                Abort(vessel, "control path unavailable");
                return;
            }

            float now = Time.fixedTime;
            float dt = lastControlFixedTime > 0f ? Mathf.Clamp(now - lastControlFixedTime, 0.005f, 0.05f) :
                Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.005f, 0.05f);
            lastControlFixedTime = now;
            phaseElapsed += dt;
            SurfaceSpeedMps = attitude.SurfaceSpeedMps;
            RadarAltitudeM = attitude.RadarAltitudeM;
            VerticalSpeedMps = attitude.VerticalSpeedMps;
            if (!attitude.SharedSurfaceSpeedValid || !attitude.SharedRadarAltitudeValid ||
                !attitude.VerticalSpeedValid || !IsFinite(SurfaceSpeedMps) || !IsFinite(RadarAltitudeM) ||
                !IsFinite(VerticalSpeedMps))
            {
                Abort(vessel, "non-finite takeoff sensor sample");
                return;
            }

            if (Phase == AutoTakeoffPhase.Armed)
            {
                if (ground != null && ground.LiftoffConfirmed)
                {
                    Disarm(vessel, "MANUAL TAKEOFF — normal ground-armed AP released");
                    return;
                }
                ReleaseAxisOwnership();
                return;
            }

            if (DetectStrongPilotTransfer(state, dt))
            {
                Abort(vessel, "strong sustained pilot input — control transferred");
                return;
            }
            if ((int)Phase >= (int)AutoTakeoffPhase.GroundRoll && (int)Phase <= (int)AutoTakeoffPhase.LiftoffConfirm && PilotBrakeRequested(vessel))
            {
                Abort(vessel, "pilot brake input");
                return;
            }

            switch (Phase)
            {
                case AutoTakeoffPhase.BrakeHold:
                    SetBrakes(vessel, true);
                    ApplyThrottle(0f);
                    ClearPitch();
                    SelectVr(manager, true);
                    if (phaseElapsed >= 0.75f)
                    {
                        if (externalPropulsionTakeoff)
                        {
                            engineStageStatus = "EXTERNAL PROVIDER READY — demand-bus spool";
                            SetPhase(AutoTakeoffPhase.Spool, "SPOOL — external propulsion demand active; brakes held");
                        }
                        else
                        {
                            int confirmedEngines;
                            if (!StageHasIgnitedEngine(vessel, engineStageNumber, out confirmedEngines))
                            {
                                engineStageStatus = "FAILED — ignition not confirmed in stage " + engineStageNumber;
                                Abort(vessel, "first-stage engine ignition not confirmed");
                                return;
                            }
                            engineStageStatus = "CONFIRMED — stage " + engineStageNumber + "; " +
                                confirmedEngines + " ignited engine(s)";
                            SetPhase(AutoTakeoffPhase.Spool, "SPOOL — brakes held; engine ignition confirmed");
                        }
                    }
                    break;

                case AutoTakeoffPhase.Spool:
                    SetBrakes(vessel, true);
                    ApplyThrottle(MaximumTakeoffThrottle);
                    ClearPitch();
                    SelectVr(manager, true);
                    bool propulsionReady = !externalPropulsionTakeoff || ExternalProviderAcceptsTakeoffDemand(vessel);
                    if (externalPropulsionTakeoff && !propulsionReady && phaseElapsed >= 8.0f)
                    {
                        Abort(vessel, "external propulsion provider did not accept takeoff demand within 8 seconds");
                        return;
                    }
                    if (phaseElapsed >= 1.50f && propulsionReady)
                    {
                        vrFrozen = true;
                        string releaseError;
                        if (!TryReleaseTakeoffBrakes(vessel, out releaseError))
                        {
                            Abort(vessel, releaseError);
                            return;
                        }
                        SetPhase(AutoTakeoffPhase.GroundRoll, "GROUND ROLL — Vr frozen");
                        AERISLogger.Info("[AUTO_TAKEOFF] BRAKE AUTO-RELEASE CONFIRMED; propulsion=" + propulsionMode + "; pre-existing=" +
                            (brakeWasAppliedAtExecute ? "ON" : "OFF") + "; frozen Vr=" +
                            selectedVrMps.ToString("F2") + " m/s; source=" + selectedVrSource);
                    }
                    break;

                case AutoTakeoffPhase.GroundRoll:
                    SetBrakes(vessel, false);
                    ApplyThrottle(MaximumTakeoffThrottle);
                    ClearPitch();
                    EvaluateRotationGate(vessel, attitude, manager, ground, protect);
                    if (SurfaceSpeedMps >= selectedVrMps && RotationGateReady)
                        SetPhase(AutoTakeoffPhase.Rotate, "ROTATE — pitch-rate limited");
                    break;

                case AutoTakeoffPhase.Rotate:
                    SetBrakes(vessel, false);
                    ApplyThrottle(MaximumTakeoffThrottle);
                    ApplyRotationPitch(attitude);
                    if (ground != null && !ground.ReliableGrounded)
                        SetPhase(AutoTakeoffPhase.LiftoffConfirm, "LIFTOFF CANDIDATE — confirming");
                    break;

                case AutoTakeoffPhase.LiftoffConfirm:
                    SetBrakes(vessel, false);
                    ApplyThrottle(MaximumTakeoffThrottle);
                    ApplyRotationPitch(attitude);
                    if (ground != null && ground.ReliableGrounded)
                        SetPhase(AutoTakeoffPhase.Rotate, "RECONTACT — continue rotation guard");
                    else if (ground != null && ground.LiftoffConfirmed)
                        SetPhase(AutoTakeoffPhase.InitialClimb, "INITIAL CLIMB — V/S guidance");
                    break;

                case AutoTakeoffPhase.InitialClimb:
                    SetBrakes(vessel, false);
                    ApplyThrottle(MaximumTakeoffThrottle);
                    ApplyInitialClimbPitch(attitude);
                    if (RadarAltitudeM >= settings.AutoTakeoffHandoffRadarAltitudeM && VerticalSpeedMps > 0.5f)
                        handoffStableSeconds += dt;
                    else handoffStableSeconds = 0f;
                    if (handoffStableSeconds >= 2f)
                    {
                        handoffRequested = true;
                        SetPhase(AutoTakeoffPhase.Handoff, "HANDOFF — prepared normal AP requested");
                    }
                    break;

                case AutoTakeoffPhase.Handoff:
                    ApplyThrottle(MaximumTakeoffThrottle);
                    ApplyInitialClimbPitch(attitude);
                    handoffRequested = true;
                    break;
            }
        }

        internal void CompleteHandoff(Vessel vessel)
        {
            if (Phase != AutoTakeoffPhase.Handoff) return;
            StandardFlyByWire.SetExternalThrottleReleaseBaseline(MaximumTakeoffThrottle);
            ReleaseAxisOwnership();
            ReleaseBrakes(vessel);
            handoffRequested = false;
            vrFrozen = false;
            SetPhase(AutoTakeoffPhase.Off, "COMPLETE — normal AP execution released");
        }

        internal void Disarm(Vessel vessel, string reason)
        {
            ReleaseAxisOwnership();
            ReleaseBrakes(vessel);
            handoffRequested = false;
            vrFrozen = false;
            SetPhase(AutoTakeoffPhase.Off, reason);
            armedVesselPersistentId = 0u;
            externalPropulsionTakeoff = false;
            propulsionMode = "KSP ENGINE STAGE";
            selectedExternalProviderId = string.Empty;
        }

        internal void EmergencyRelease(Vessel vessel, string reason)
        {
            Abort(vessel, reason);
            Phase = AutoTakeoffPhase.Off;
            Status = "OFF — " + reason;
            armedVesselPersistentId = 0u;
            externalPropulsionTakeoff = false;
            propulsionMode = "KSP ENGINE STAGE";
            selectedExternalProviderId = string.Empty;
            engineStageActivationRequested = false;
            engineStageNumber = -1;
            engineStageStatus = "NOT REQUESTED";
        }

        void SelectVr(TopModuleManager manager, bool upwardOnly)
        {
            float stall = 0f;
            string detail = "AA manager unavailable";
            bool aaValid = manager != null && manager.TryGetAaStallSpeedEstimate(out stall, out detail);
            float candidate = aaValid
                ? stall * Mathf.Clamp(settings.AutoTakeoffStallFactor, 1.05f, 1.50f)
                : Mathf.Clamp(settings.AutoTakeoffManualVrMps, 15f, 250f);
            candidate = Mathf.Clamp(candidate, 15f, 250f);
            if (upwardOnly && selectedVrMps > 0f && candidate < selectedVrMps) return;
            selectedStallSpeedMps = aaValid ? stall : 0f;
            selectedVrMps = candidate;
            selectedVrSource = aaValid ? "AA_ESTIMATE_X_FACTOR" : "MANUAL_FALLBACK";
            selectedVrDetail = detail;
        }

        void EvaluateRotationGate(Vessel vessel, VirtualAttitudeInstrument attitude,
            TopModuleManager manager, GroundStabilityProtection ground, ProtectTelemetry protect)
        {
            RotationGateReady = false;
            if (ground == null || !ground.ControlActive) { RotationGateReason = "ground stability not active"; return; }
            if (Mathf.Abs(ground.HeadingErrorDeg) > 12f) { RotationGateReason = "heading error > 12 deg"; return; }
            if (!attitude.InstrumentHorizonBankValid || Mathf.Abs(attitude.InstrumentHorizonBankDeg) > 8f)
            { RotationGateReason = "bank outside +/-8 deg"; return; }
            var pitch = manager.PitchController;
            var flightModel = manager.FlightModel;
            if (!attitude.InstrumentPitchValid || pitch == null || flightModel == null)
            { RotationGateReason = "AA pitch/AoA envelope unavailable"; return; }
            if (!pitch.moderate_aoa || !pitch.moderate_g)
            { RotationGateReason = "AA AoA/G moderation must be enabled"; return; }
            float modelAge = Mathf.Abs(Time.fixedTime - flightModel.LastModelUpdateFixedTime);
            float aoaDeg = flightModel.AoA(AutopilotModule.PITCH) * Mathf.Rad2Deg;
            if (flightModel.ModelUpdateSequence < 16 || modelAge > 0.25f ||
                float.IsNaN(aoaDeg) || float.IsInfinity(aoaDeg))
            { RotationGateReason = "AA flight model/AoA sample is not fresh"; return; }
            float aoaGate = Mathf.Max(3f, pitch.max_aoa - 1.5f);
            if (Mathf.Abs(aoaDeg) > aoaGate)
            { RotationGateReason = "AoA outside rotation margin"; return; }
            if (protect != null && protect.StallDetected) { RotationGateReason = "Protect stall detected"; return; }
            if (vessel.ActionGroups[KSPActionGroup.Brakes]) { RotationGateReason = "brakes applied"; return; }
            float requiredThrottle = MaximumTakeoffThrottle - 0.05f;
            if (ThrottleDemand < requiredThrottle || StandardFlyByWire.LastFinalThrottle < requiredThrottle)
            { RotationGateReason = "takeoff thrust not maintained"; return; }
            RotationGateReady = true;
            RotationGateReason = "READY";
        }

        // EXECUTE activates exactly one engine-bearing KSP stage.  v0.9.1 searched only
        // root-namespace type names and therefore missed namespaced StageManager/Staging
        // implementations in the target KSP runtime.  v0.9.2 resolves candidates by
        // simple type name across every loaded assembly.  If that public staging path is
        // still unavailable, it starts only the already-validated ModuleEngines in the
        // candidate stage; decouplers and later stages are never touched by the fallback.
        bool TryActivateFirstEngineStage(Vessel vessel, out string error)
        {
            error = null;
            if (vessel == null) { error = "Active vessel unavailable for engine staging."; return false; }
            if (settings != null && settings.AppIntegrationEnabled)
            {
                string externalError;
                if (TrySelectExternalPropulsionTakeoff(vessel, out externalError)) return true;
                // No compatible external provider is a normal condition. Fall through to
                // the validated ModuleEngines path; never let an unrelated installed mod
                // suppress conventional engine staging.
                error = null;
            }
            int nextStage = vessel.currentStage - 1;
            engineStageNumber = nextStage;
            if (nextStage < 0)
            {
                int activeStage;
                int activeCount;
                if (TryFindIgnitedEngineStage(vessel, out activeStage, out activeCount))
                {
                    engineStageNumber = activeStage;
                    engineStageStatus = "READY — stage " + activeStage + "; " + activeCount +
                        " engine(s) already ignited";
                    AERISLogger.Info("[AUTO_TAKEOFF] engine start skipped: no pending stage, but stage " +
                        activeStage + " already has " + activeCount + " ignited engine(s).");
                    return true;
                }
                engineStageStatus = "INHIBITED — no next or ignited engine stage";
                error = "No first engine stage remains to activate and no ignited engine was found.";
                return false;
            }

            var stagedEngines = new List<ModuleEngines>();
            int stagedEngineCount = 0;
            int alreadyIgnitedCount = 0;
            foreach (Part part in vessel.parts)
            {
                if (part == null || part.inverseStage != nextStage) continue;
                foreach (PartModule module in part.Modules)
                {
                    ModuleEngines engine = module as ModuleEngines;
                    if (engine == null) continue;
                    stagedEngines.Add(engine);
                    stagedEngineCount++;
                    if (IsEngineIgnited(engine)) alreadyIgnitedCount++;
                }
            }
            if (stagedEngineCount == 0)
            {
                engineStageStatus = "INHIBITED — stage " + nextStage + " has no engine";
                error = "Next stage " + nextStage + " contains no engine; automatic staging inhibited.";
                return false;
            }
            if (alreadyIgnitedCount > 0)
            {
                engineStageStatus = "READY — stage " + nextStage + " engine already ignited";
                AERISLogger.Info("[AUTO_TAKEOFF] engine staging skipped: stage " + nextStage +
                    " already has " + alreadyIgnitedCount + " ignited engine(s).");
                return true;
            }

            int currentStageBeforeRequest = vessel.currentStage;
            string invokeError;
            if (!TryInvokeNextStage(out invokeError))
            {
                if (vessel.currentStage < currentStageBeforeRequest)
                {
                    engineStageActivationRequested = true;
                    engineStageStatus = "REQUESTED — KSP stage index advanced; awaiting ignition confirmation";
                    AERISLogger.Warn("[AUTO_TAKEOFF] stage API returned an error after the KSP stage index advanced " +
                        "from " + currentStageBeforeRequest + " to " + vessel.currentStage +
                        "; direct fallback suppressed to prevent a duplicate engine start. Detail: " + invokeError);
                    return true;
                }
                int ignitedAfterStageRequest;
                if (StageHasIgnitedEngine(vessel, nextStage, out ignitedAfterStageRequest))
                {
                    engineStageActivationRequested = true;
                    engineStageStatus = "STARTED — stage API reported an error, ignition confirmed";
                    AERISLogger.Warn("[AUTO_TAKEOFF] stage API reported '" + invokeError +
                        "' after invocation, but " + ignitedAfterStageRequest +
                        " target-stage engine(s) are ignited; continuing to Brake Hold confirmation.");
                    return true;
                }

                int directRequestCount;
                string directError;
                if (!TryActivateTargetEngines(stagedEngines, out directRequestCount, out directError))
                {
                    if (directRequestCount > 0) engineStageActivationRequested = true;
                    engineStageStatus = "FAILED — stage API: " + invokeError +
                        "; direct engine start: " + directError +
                        (directRequestCount > 0 ? "; PARTIAL REQUEST — brakes held" : string.Empty);
                    error = "First engine stage activation failed: " + engineStageStatus;
                    return false;
                }
                engineStageActivationRequested = true;
                engineStageStatus = "START REQUESTED — stage " + nextStage + "; " +
                    directRequestCount + " engine(s); DIRECT ENGINE FALLBACK";
                AERISLogger.Warn("[AUTO_TAKEOFF] namespaced stage API unavailable (" + invokeError +
                    "); directly requested Activate() only on " + directRequestCount +
                    " validated engine(s) in stage " + nextStage + ". No decoupler or later stage was fired.");
                return true;
            }
            engineStageActivationRequested = true;
            engineStageStatus = "ACTIVATED — stage " + nextStage + "; " + stagedEngineCount + " engine(s)";
            AERISLogger.Info("[AUTO_TAKEOFF] first engine stage activated once: stage=" + nextStage +
                "; engines=" + stagedEngineCount + "; takeoff throttle=fixed 1.000.");
            return true;
        }


        bool TrySelectExternalPropulsionTakeoff(Vessel vessel, out string error)
        {
            error = null;
            if (settings == null || !settings.AppIntegrationEnabled) return false;
            AERISPropulsionStatus status;
            var request = new AERISPropulsionRequest
            {
                RequiredThrottle = MaximumTakeoffThrottle,
                RequiredForwardThrustkN = 0f,
                RequiredForwardThrustN = 0f,
                // Auto Takeoff is a maximum-throttle owner, not a speed-hold owner.
                // APP may optimize propeller pitch from measured state, but must not taper
                // power as the aircraft approaches Vr.
                AutopilotSpeedHold = false,
                TargetSpeedMps = 0f
            };
            string providerId;
            if (!AERISPropulsionBridge.TrySelectProvider(vessel, request, out providerId, out status) ||
                !status.HasControllablePropulsion)
            {
                error = "No registered external propulsion provider supports the current vessel.";
                return false;
            }
            selectedExternalProviderId = providerId;
            externalPropulsionTakeoff = true;
            propulsionMode = "EXTERNAL DEMAND BUS — " + selectedExternalProviderId;
            engineStageNumber = -2;
            engineStageActivationRequested = false;
            engineStageStatus = "EXTERNAL PROVIDER — KSP ENGINE STAGE NOT REQUIRED";
            AERISLogger.Info("[AUTO_TAKEOFF][EXTERNAL] compatible propulsion provider selected; engine staging bypassed. Provider=" +
                selectedExternalProviderId + "; detail=" + (status.Detail ?? string.Empty));
            return true;
        }

        bool ExternalProviderAcceptsTakeoffDemand(Vessel vessel)
        {
            if (!externalPropulsionTakeoff) return true;
            AERISPropulsionStatus status;
            var request = new AERISPropulsionRequest
            {
                RequiredThrottle = MaximumTakeoffThrottle,
                RequiredForwardThrustkN = 0f,
                RequiredForwardThrustN = 0f,
                // Auto Takeoff is a maximum-throttle owner, not a speed-hold owner.
                // APP may optimize propeller pitch from measured state, but must not taper
                // power as the aircraft approaches Vr.
                AutopilotSpeedHold = false,
                TargetSpeedMps = 0f
            };
            if (!AERISPropulsionBridge.TryGetStatus(selectedExternalProviderId, vessel, request, out status))
            {
                engineStageStatus = "EXTERNAL SPOOL — selected provider status pending";
                return false;
            }
            bool explicitReady = status.PropulsionReady;
            bool responseReady = status.MotorResponse01 >= 0.25f;
            bool thrustReady = status.ActualAvailableForwardThrustkN > 0.05f ||
                status.EstimatedAvailableForwardThrustkN > 0.05f;
            // Older external providers did not populate PropulsionReady/MotorResponse. After a
            // four-second protected spool, a healthy controllable provider may use the
            // compatibility path. Brakes remain held throughout that dwell.
            bool legacyReady = phaseElapsed >= 4.0f && status.HasControllablePropulsion &&
                !status.PropulsionUnavailable;
            bool ready = status.HasControllablePropulsion && !status.PropulsionUnavailable &&
                (explicitReady || responseReady || thrustReady || legacyReady);
            string readiness = explicitReady ? "READY" :
                (responseReady ? "MOTOR RESPONSE " + status.MotorResponse01.ToString("F2") :
                (thrustReady ? "THRUST AVAILABLE" :
                (legacyReady ? "LEGACY PROVIDER ACCEPTED AFTER DWELL" : "SPOOLING")));
            engineStageStatus = "EXTERNAL SPOOL — " + readiness;
            if (!string.IsNullOrEmpty(status.Detail)) engineStageStatus += " — " + status.Detail;
            return ready;
        }

        void BeginNewArmAttempt(Vessel vessel)
        {
            // ARM is also the recovery boundary after explosion/revert/quickload. Always discard
            // stale stage, brake and axis ownership before validating the new attempt.
            ReleaseAxisOwnership();
            // Never apply stale brake ownership to a newly-created vessel after explosion,
            // revert or quickload. Only release the action group when ownership belongs to
            // this exact persistent vessel; otherwise clear the stale token locally.
            if (vessel != null && brakeOwnedVesselPersistentId != 0u &&
                brakeOwnedVesselPersistentId == vessel.persistentId)
                ReleaseBrakes(vessel);
            else
            {
                ownsBrakes = false;
                brakeOwnedVesselPersistentId = 0u;
            }
            attemptGeneration++;
            Phase = AutoTakeoffPhase.Off;
            Status = "RESET FOR ARM ATTEMPT " + attemptGeneration;
            LastAbortReason = string.Empty;
            vrFrozen = false;
            handoffRequested = false;
            engineStageActivationRequested = false;
            engineStageNumber = -1;
            engineStageStatus = "AWAITING EXECUTE";
            externalPropulsionTakeoff = false;
            propulsionMode = "KSP ENGINE STAGE";
            selectedExternalProviderId = string.Empty;
            armedVesselPersistentId = vessel == null ? 0u : vessel.persistentId;
            brakeWasAppliedAtExecute = false;
            brakeReleaseConfirmed = false;
            strongPilotInputSeconds = 0f;
            handoffStableSeconds = 0f;
            phaseElapsed = 0f;
            lastControlFixedTime = 0f;
        }

        static bool IsEngineIgnited(ModuleEngines engine)
        {
            if (engine == null) return false;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            try
            {
                PropertyInfo property = engine.GetType().GetProperty("EngineIgnited", flags);
                if (property != null && property.PropertyType == typeof(bool))
                    return (bool)property.GetValue(engine, null);
                FieldInfo field = engine.GetType().GetField("EngineIgnited", flags);
                if (field != null && field.FieldType == typeof(bool))
                    return (bool)field.GetValue(engine);
            }
            catch { }
            return engine.isOperational || engine.finalThrust > 0.01f;
        }

        static bool StageHasIgnitedEngine(Vessel vessel, int stage, out int ignitedCount)
        {
            ignitedCount = 0;
            if (vessel == null || stage < 0) return false;
            foreach (Part part in vessel.parts)
            {
                if (part == null || part.inverseStage != stage) continue;
                foreach (PartModule module in part.Modules)
                {
                    ModuleEngines engine = module as ModuleEngines;
                    if (engine != null && IsEngineIgnited(engine)) ignitedCount++;
                }
            }
            return ignitedCount > 0;
        }

        static bool TryFindIgnitedEngineStage(Vessel vessel, out int stage, out int ignitedCount)
        {
            stage = -1;
            ignitedCount = 0;
            if (vessel == null) return false;
            foreach (Part part in vessel.parts)
            {
                if (part == null || part.inverseStage < 0) continue;
                foreach (PartModule module in part.Modules)
                {
                    ModuleEngines engine = module as ModuleEngines;
                    if (engine == null || !IsEngineIgnited(engine)) continue;
                    if (part.inverseStage > stage)
                    {
                        stage = part.inverseStage;
                        ignitedCount = 1;
                    }
                    else if (part.inverseStage == stage) ignitedCount++;
                }
            }
            return stage >= 0 && ignitedCount > 0;
        }

        static bool TryActivateTargetEngines(List<ModuleEngines> engines, out int requestedCount,
            out string error)
        {
            requestedCount = 0;
            error = null;
            if (engines == null || engines.Count == 0)
            {
                error = "target engine list is empty";
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var pendingEngines = new List<ModuleEngines>();
            var activationMethods = new List<MethodInfo>();
            for (int i = 0; i < engines.Count; i++)
            {
                ModuleEngines engine = engines[i];
                if (engine == null || IsEngineIgnited(engine)) continue;
                MethodInfo method = FindZeroArgumentMethod(engine.GetType(), "Activate", flags);
                if (method == null)
                {
                    error = "Activate() unavailable on " + engine.GetType().FullName;
                    return false;
                }
                pendingEngines.Add(engine);
                activationMethods.Add(method);
            }

            if (pendingEngines.Count == 0)
            {
                error = "all target engines disappeared before start request";
                return false;
            }

            try
            {
                for (int i = 0; i < pendingEngines.Count; i++)
                {
                    activationMethods[i].Invoke(pendingEngines[i], null);
                    requestedCount++;
                }
                return requestedCount > 0;
            }
            catch (TargetInvocationException ex)
            {
                error = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        static MethodInfo FindZeroArgumentMethod(Type type, string methodName, BindingFlags flags)
        {
            if (type == null || string.IsNullOrEmpty(methodName)) return null;
            try
            {
                MethodInfo[] methods = type.GetMethods(flags);
                for (int i = 0; i < methods.Length; i++)
                    if (string.Equals(methods[i].Name, methodName, StringComparison.Ordinal) &&
                        methods[i].GetParameters().Length == 0) return methods[i];
            }
            catch { }
            return null;
        }

        static bool TryInvokeNextStage(out string error)
        {
            error = "ActivateNextStage API unavailable";
            int matchingTypeCount = 0;
            int matchingMethodCount = 0;
            string lastFailure = null;
            try
            {
                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    AssemblyName assemblyName;
                    try { assemblyName = assemblies[i].GetName(); }
                    catch { continue; }
                    if (assemblyName == null ||
                        !string.Equals(assemblyName.Name, "Assembly-CSharp", StringComparison.Ordinal)) continue;
                    Type[] types = GetLoadableTypes(assemblies[i]);
                    for (int t = 0; t < types.Length; t++)
                    {
                        Type stageType = types[t];
                        if (stageType == null ||
                            (!string.Equals(stageType.Name, "StageManager", StringComparison.Ordinal) &&
                             !string.Equals(stageType.Name, "Staging", StringComparison.Ordinal))) continue;
                        matchingTypeCount++;

                        MethodInfo[] methods;
                        try
                        {
                            methods = stageType.GetMethods(BindingFlags.Static | BindingFlags.Instance |
                                BindingFlags.Public | BindingFlags.NonPublic);
                        }
                        catch (Exception ex)
                        {
                            lastFailure = stageType.FullName + ": " + ex.Message;
                            continue;
                        }
                        for (int m = 0; m < methods.Length; m++)
                        {
                            MethodInfo method = methods[m];
                            if (!string.Equals(method.Name, "ActivateNextStage", StringComparison.Ordinal) ||
                                method.GetParameters().Length != 0) continue;
                            matchingMethodCount++;
                            object target = null;
                            if (!method.IsStatic)
                            {
                                target = TryGetStageApiInstance(stageType);
                                if (target == null)
                                {
                                    lastFailure = stageType.FullName + ".ActivateNextStage is instance-only and no singleton is available";
                                    continue;
                                }
                            }
                            try
                            {
                                method.Invoke(target, null);
                                AERISLogger.Info("[AUTO_TAKEOFF] resolved KSP stage API: " +
                                    stageType.FullName + ".ActivateNextStage().");
                                return true;
                            }
                            catch (TargetInvocationException ex)
                            {
                                lastFailure = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                            }
                            catch (Exception ex)
                            {
                                lastFailure = ex.Message;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            if (!string.IsNullOrEmpty(lastFailure))
                error = "stage API invocation failed: " + lastFailure;
            else if (matchingTypeCount == 0)
                error = "no StageManager/Staging type found in Assembly-CSharp";
            else if (matchingMethodCount == 0)
                error = matchingTypeCount + " StageManager/Staging type(s) found, but no zero-argument ActivateNextStage method";
            return false;
        }

        static Type[] GetLoadableTypes(Assembly assembly)
        {
            if (assembly == null) return new Type[0];
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types ?? new Type[0]; }
            catch { return new Type[0]; }
        }

        static object TryGetStageApiInstance(Type stageType)
        {
            if (stageType == null) return null;
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            string[] memberNames = { "Instance", "instance" };
            for (int i = 0; i < memberNames.Length; i++)
            {
                try
                {
                    PropertyInfo property = stageType.GetProperty(memberNames[i], flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                    {
                        object value = property.GetValue(null, null);
                        if (value != null) return value;
                    }
                    FieldInfo field = stageType.GetField(memberNames[i], flags);
                    if (field != null)
                    {
                        object value = field.GetValue(null);
                        if (value != null) return value;
                    }
                }
                catch { }
            }
            return null;
        }

        void ApplyRotationPitch(VirtualAttitudeInstrument attitude)
        {
            float target = Mathf.Clamp(settings.AutoTakeoffRotationPitchDeg, 3f, 20f);
            float cap = Mathf.Clamp(settings.AutoTakeoffRotationRateDegPerSec, 0.5f, 5f);
            float error = target - attitude.InstrumentPitchDeg;
            float demand = 0.72f * error - 0.32f * attitude.InstrumentPitchRateDegPerSec;
            StandardFlyByWire.ExternalGroundPitchModerationBypass = true;
            ApplyPitchRate(Mathf.Clamp(demand, -cap, cap));
        }

        void ApplyInitialClimbPitch(VirtualAttitudeInstrument attitude)
        {
            StandardFlyByWire.ExternalGroundPitchModerationBypass = false;
            float cap = Mathf.Clamp(settings.AutoTakeoffRotationRateDegPerSec, 0.5f, 5f);
            float vsError = settings.AutoTakeoffInitialClimbVsMps - attitude.VerticalSpeedMps;
            float demand = 0.10f * vsError - 0.28f * attitude.InstrumentPitchRateDegPerSec;
            float pitchCeilingGuard = Mathf.Clamp((18f - attitude.InstrumentPitchDeg) * 0.35f, -cap, cap);
            demand = Mathf.Min(demand, pitchCeilingGuard);
            ApplyPitchRate(Mathf.Clamp(demand, -cap, cap));
        }

        void ApplyThrottle(float demand)
        {
            ThrottleDemand = IsFinite(demand) ? Mathf.Clamp01(demand) : 0f;
            StandardFlyByWire.ExternalThrottleDemand = ThrottleDemand;
            StandardFlyByWire.ExternalThrottleOverride = true;
            ownsThrottle = AaNativeThrottleOverrideActive = true;
        }

        void ApplyPitchRate(float demandDegPerSec)
        {
            PitchRateDemandDegPerSec = IsFinite(demandDegPerSec) ? demandDegPerSec : 0f;
            StandardFlyByWire.ExternalPitchDemand = PitchRateDemandDegPerSec * Mathf.Deg2Rad;
            StandardFlyByWire.ExternalPitchOverride = true;
            ownsPitch = AaNativePitchOverrideActive = true;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        bool DetectStrongPilotTransfer(FlightCtrlState state, float dt)
        {
            FlightCtrlState raw = FlightInputHandler.state;
            float pitch = Mathf.Abs(raw != null ? raw.pitch : state.pitch);
            float roll = Mathf.Abs(raw != null ? raw.roll : state.roll);
            float yaw = Mathf.Abs(raw != null ? raw.yaw : state.yaw);
            bool strong = pitch > 0.68f || roll > 0.92f || yaw > 0.92f;
            strongPilotInputSeconds = strong ? strongPilotInputSeconds + dt : 0f;
            return strongPilotInputSeconds >= 0.50f;
        }

        bool PilotBrakeRequested(Vessel vessel)
        {
            FlightCtrlState raw = FlightInputHandler.state;
            bool wheelBrake = raw != null && raw.wheelThrottle < -0.25f;
            return wheelBrake || (!ownsBrakes && vessel.ActionGroups[KSPActionGroup.Brakes]);
        }

        void SetBrakes(Vessel vessel, bool applied)
        {
            if (vessel == null) return;
            vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, applied);
            ownsBrakes = applied;
            brakeOwnedVesselPersistentId = applied ? vessel.persistentId : 0u;
        }

        bool TryReleaseTakeoffBrakes(Vessel vessel, out string error)
        {
            error = null;
            if (vessel == null)
            {
                error = "automatic brake release failed: active vessel unavailable";
                return false;
            }
            vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            if (vessel.ActionGroups[KSPActionGroup.Brakes])
            {
                ownsBrakes = true;
                brakeReleaseConfirmed = false;
                error = "automatic brake release failed: KSP brake action group remained ON";
                return false;
            }
            ownsBrakes = false;
            brakeOwnedVesselPersistentId = 0u;
            brakeReleaseConfirmed = true;
            return true;
        }

        void RestorePreExecuteBrake(Vessel vessel)
        {
            if (vessel != null)
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, brakeWasAppliedAtExecute);
            ownsBrakes = false;
            brakeOwnedVesselPersistentId = 0u;
            brakeReleaseConfirmed = false;
        }

        void FailExecuteWithBrakesHeld(Vessel vessel, string reason)
        {
            LastAbortReason = reason;
            ReleaseAxisOwnership();
            SetBrakes(vessel, true);
            handoffRequested = false;
            vrFrozen = false;
            brakeReleaseConfirmed = false;
            SetPhase(AutoTakeoffPhase.Aborted, "ABORTED — partial engine start; brakes held");
            AERISLogger.Warn("[AUTO_TAKEOFF] EXECUTE ABORT after a partial engine-start request: " +
                reason + "; parking brake remains ON. MASTER OFF or a new ARM releases AERIS ownership.");
        }

        void ReleaseBrakes(Vessel vessel)
        {
            if (ownsBrakes && vessel != null &&
                (brakeOwnedVesselPersistentId == 0u || brakeOwnedVesselPersistentId == vessel.persistentId))
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            ownsBrakes = false;
            brakeOwnedVesselPersistentId = 0u;
        }

        void Abort(Vessel vessel, string reason)
        {
            LastAbortReason = reason;
            ReleaseAxisOwnership();
            ReleaseBrakes(vessel);
            handoffRequested = false;
            vrFrozen = false;
            SetPhase(AutoTakeoffPhase.Aborted, "ABORTED — " + reason);
            AERISLogger.Warn("[AUTO_TAKEOFF] ABORT: " + reason + "; Ground Stability remains independently available.");
        }

        void ReleaseAxisOwnership()
        {
            ClearPitch();
            if (ownsThrottle)
            {
                StandardFlyByWire.ExternalThrottleOverride = false;
                StandardFlyByWire.ExternalThrottleDemand = 0f;
            }
            ownsThrottle = false;
            AaNativeThrottleOverrideActive = false;
            ThrottleDemand = 0f;
        }

        void ClearPitch()
        {
            StandardFlyByWire.ExternalGroundPitchModerationBypass = false;
            if (ownsPitch)
            {
                StandardFlyByWire.ExternalPitchOverride = false;
                StandardFlyByWire.ExternalPitchDemand = 0f;
            }
            ownsPitch = false;
            AaNativePitchOverrideActive = false;
            PitchRateDemandDegPerSec = 0f;
        }

        void SetPhase(AutoTakeoffPhase phase, string status)
        {
            if (Phase != phase)
            {
                AERISLogger.Info("[AUTO_TAKEOFF] phase=" + PhaseName(phase) + "; " + status);
                if (phase == AutoTakeoffPhase.Rotate)
                    AERISLogger.Info("[AUTO_TAKEOFF] ground ROTATE: AA native pitch-rate controller retained; landed [0,0] AoA/G moderation envelope bypassed only while ROTATE owns pitch.");
            }
            Phase = phase;
            Status = status;
            phaseElapsed = 0f;
        }

        static string PhaseName(AutoTakeoffPhase phase)
        {
            switch (phase)
            {
                case AutoTakeoffPhase.BrakeHold: return "BRAKE HOLD";
                case AutoTakeoffPhase.GroundRoll: return "GROUND ROLL";
                case AutoTakeoffPhase.LiftoffConfirm: return "LIFTOFF CONFIRM";
                case AutoTakeoffPhase.InitialClimb: return "INITIAL CLIMB";
                default: return phase.ToString().ToUpperInvariant();
            }
        }
    }
}
