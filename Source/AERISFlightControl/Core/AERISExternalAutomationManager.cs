using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.API;
using AERISFlightControl.Autopilot;
using AERISFlightControl.Logging;
using AERISFlightControl.Protect;

namespace AERISFlightControl.Core
{
    internal sealed partial class AERISExternalAutomationManager
    {
        const double MinimumLeaseSeconds = 1.0;
        const double MaximumLeaseSeconds = 120.0;
        const double DefaultLeaseSeconds = 30.0;
        const float PilotAxisThreshold = 0.35f;
        const float PilotOverrideDwellSeconds = 0.35f;
        const float StopThresholdMps = 1.0f;
        const float StopDwellSeconds = 2.0f;
        const float TouchdownConfirmSeconds = 0.25f;

        sealed class ApStateSnapshot
        {
            internal bool Valid;
            internal bool Master;
            internal bool BankArmed;
            internal bool HdgArmed;
            internal bool PitchArmed;
            internal bool VsArmed;
            internal bool AltArmed;
            internal bool AccArmed;
            internal bool VelArmed;
            internal float BankTarget;
            internal float HdgTarget;
            internal float PitchTarget;
            internal float VsTarget;
            internal float AltTarget;
            internal float AccTarget;
            internal float VelTarget;
        }

        sealed class SessionRecord
        {
            internal AERISAutomationSession Session = new AERISAutomationSession();
            internal string Purpose;
            internal AERISAutomationPriority Priority;
            internal double LeaseDurationSeconds;
            internal float LeaseExpiresRealtime;
            internal bool AllowPilotOverride;
            internal bool RequireProtect;
            internal AERISAutomationState State = AERISAutomationState.Acquired;
            internal string Detail = "ACQUIRED";
            internal AERISAutomationResultCode FailureCode = AERISAutomationResultCode.None;
            internal bool ConditionStable;
            internal bool MissionCompleted;
            internal bool MissionFailed;
            internal bool ProtectIntervening;
            internal bool PilotOverride;
            internal AERISAutomationCommandHandle Command = new AERISAutomationCommandHandle();
            internal string CommandKind = string.Empty;
            internal AERISSetpointMissionRequest SetpointRequest = new AERISSetpointMissionRequest();
            internal float MissionAcceptedRealtime;
            internal float StableSince = -1f;
            internal float PilotInputSince = -1f;
            internal float TouchdownSince = -1f;
            internal float StoppedSince = -1f;
            internal float GroundAssistStableSince = -1f;
            internal bool EverAirborne;
            internal bool TouchdownLatched;
            internal bool OwnsControl;
            internal ApStateSnapshot BeforeMission;
        }

        sealed class ExpiredSnapshotRecord
        {
            internal AERISAutomationSnapshot Snapshot = new AERISAutomationSnapshot();
            internal float RemoveAfterRealtime;
        }

        readonly AERISBootstrap core;
        readonly int mainThreadId;
        readonly Dictionary<Guid, SessionRecord> sessions = new Dictionary<Guid, SessionRecord>();
        readonly Dictionary<Guid, Guid> vesselOwners = new Dictionary<Guid, Guid>();
        readonly Dictionary<Guid, ExpiredSnapshotRecord> terminalSnapshots = new Dictionary<Guid, ExpiredSnapshotRecord>();
        readonly List<Guid> iterationBuffer = new List<Guid>();

        internal AERISExternalAutomationManager(AERISBootstrap core)
        {
            this.core = core;
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        internal bool TryAcquire(AERISAutomationAcquireRequest request,
            out AERISAutomationSession session, out AERISAutomationResult result)
        {
            session = new AERISAutomationSession();
            if (!EnsureMainThread(out result)) return false;
            if (request == null)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Acquire request is required.", false);

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (!HighLogic.LoadedSceneIsFlight || vessel == null)
                return Fail(out result, AERISAutomationResultCode.VesselUnavailable,
                    "An active flight vessel is required.", true);
            Guid requestedVesselId = request.VesselId != Guid.Empty ? request.VesselId :
                (request.Vessel == null ? Guid.Empty : request.Vessel.id);
            if (requestedVesselId == Guid.Empty) requestedVesselId = vessel.id;
            if (requestedVesselId != vessel.id || request.Vessel != null && request.Vessel != vessel)
                return Fail(out result, AERISAutomationResultCode.WrongVessel,
                    "Acquire request does not match the active vessel.", false);
            request.VesselId = requestedVesselId;

            string normalizedClientId = (request.ClientId ?? string.Empty).Trim();
            if (normalizedClientId.Length == 0 || normalizedClientId.Length > 128)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "ClientId must be a stable non-empty identifier of at most 128 characters.", false);
            if (request.DisplayName != null && request.DisplayName.Length > 128)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "DisplayName must be at most 128 characters.", false);
            if (request.Purpose != null && request.Purpose.Length > 512)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Purpose must be at most 512 characters.", false);
            if (request.Priority != AERISAutomationPriority.Advisory &&
                request.Priority != AERISAutomationPriority.TaskAutomation &&
                request.Priority != AERISAutomationPriority.MissionAutomation)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Automation priority is not a published ContractVersion 2 value.", false);

            AERISAutomationCapability[] requestedCapabilities =
                NormalizeCapabilities(request.RequestedCapabilities);
            if (requestedCapabilities.Length == 0)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "At least one automation capability must be requested.", false);
            AERISAutomationCapability[] availableCapabilities = AvailableCapabilities();
            string missing = MissingCapabilities(requestedCapabilities, availableCapabilities);
            if (!string.IsNullOrEmpty(missing))
                return Fail(out result, AERISAutomationResultCode.CapabilityUnavailable,
                    "Unsupported capability request: " + missing + ".", false);
            if (request.RequireProtect && core.Protect == null)
                return Fail(out result, AERISAutomationResultCode.ProtectUnavailable,
                    "AERIS Protect is unavailable.", true);

            Guid ownerId;
            if (vesselOwners.TryGetValue(requestedVesselId, out ownerId))
            {
                SessionRecord existing;
                if (sessions.TryGetValue(ownerId, out existing))
                {
                    if (LeaseExpired(existing))
                    {
                        Expire(existing);
                        existing = null;
                    }
                    if (existing != null && string.Equals(existing.Session.ClientId,
                        normalizedClientId, StringComparison.Ordinal))
                        return Fail(out result, AERISAutomationResultCode.Busy,
                            "This ClientId already owns the vessel; renew with the existing session token.", true);
                    if (existing != null && (int)request.Priority <= (int)existing.Priority)
                        return Fail(out result, AERISAutomationResultCode.Busy,
                            "Vessel automation is owned by " + existing.Session.ClientId +
                            " at priority " + existing.Priority + ".", true);
                    if (existing != null)
                    {
                        Terminate(existing, AERISAutomationState.Cancelled,
                            AERISAutomationResultCode.Busy,
                            "PREEMPTED BY HIGHER-PRIORITY CLIENT " + normalizedClientId, true, true);
                        RemoveSession(existing.Session.SessionId);
                    }
                }
                else vesselOwners.Remove(requestedVesselId);
            }

            double lease = request.RequestedTtlSeconds > 0f ? request.RequestedTtlSeconds : request.LeaseSeconds;
            if (double.IsNaN(lease) || double.IsInfinity(lease) || lease <= 0.0)
                lease = DefaultLeaseSeconds;
            lease = Math.Max(MinimumLeaseSeconds, Math.Min(MaximumLeaseSeconds, lease));
            double now = UniversalTime();
            var record = new SessionRecord();
            record.Session = new AERISAutomationSession
            {
                SessionId = Guid.NewGuid(), VesselId = requestedVesselId,
                ClientId = normalizedClientId,
                GrantedCapabilities = CloneCapabilities(requestedCapabilities),
                ExpiresRealtime = Time.realtimeSinceStartup + (float)lease,
                ExpiresUniversalTime = now + lease
            };
            record.Purpose = request.Purpose ?? string.Empty;
            record.Priority = request.Priority;
            record.LeaseDurationSeconds = lease;
            record.LeaseExpiresRealtime = Time.realtimeSinceStartup + (float)lease;
            record.AllowPilotOverride = request.AllowPilotOverride;
            record.RequireProtect = request.RequireProtect;
            sessions.Add(record.Session.SessionId, record);
            vesselOwners[record.Session.VesselId] = record.Session.SessionId;
            session = CloneSession(record.Session);
            result = Accepted("Automation session acquired.");
            LogTransition(record, "SESSION ACQUIRED priority=" + record.Priority +
                " capabilities=" + CapabilityList(record.Session.GrantedCapabilities) +
                " purpose=" + Safe(record.Purpose));
            return true;
        }

        internal bool TrySubmitSetpointMission(AERISAutomationSession session,
            AERISSetpointMissionRequest request, out AERISAutomationCommandHandle command,
            out AERISAutomationResult result)
        {
            command = new AERISAutomationCommandHandle();
            if (request == null) return Fail(out result, AERISAutomationResultCode.InvalidRequest, "Setpoint request is required.", false);
            SessionRecord record;
            if (!TryValidateSession(session, AERISAutomationCapability.SetpointGuidance, out record, out result)) return false;
            if (!ResolveRequestVessel(request.Vessel, ref request.VesselId, record))
                return Fail(out result, AERISAutomationResultCode.WrongVessel,
                    "Setpoint request vessel does not match the lease.", false);
            if (request.AltitudeM <= 0 && Finite(request.AltitudeMeters) && request.AltitudeMeters > 0.0)
                request.AltitudeM = (int)Math.Round(request.AltitudeMeters);
            if ((!Finite(request.SurfaceSpeedMps) || request.SurfaceSpeedMps <= 0.0) &&
                Finite(request.TrueAirspeedMps) && request.TrueAirspeedMps > 0f)
                request.SurfaceSpeedMps = request.TrueAirspeedMps;
            if (!request.UseExplicitHeading)
                request.HeadingDeg = core.Attitude != null && core.Attitude.InstrumentHeadingValid
                    ? core.Attitude.InstrumentHeadingDeg : 0.0;
            if (request.StableSeconds <= 0f) request.StableSeconds = 3f;
            if (request.AltitudeToleranceM <= 0f) request.AltitudeToleranceM = 5f;
            if (request.SpeedToleranceMps <= 0f) request.SpeedToleranceMps = 1.0f;
            if (request.VerticalSpeedToleranceMps <= 0f) request.VerticalSpeedToleranceMps = 1.0f;
            if (request.HeadingToleranceDeg <= 0f) request.HeadingToleranceDeg = 3f;
            if (request.BankToleranceDeg <= 0f) request.BankToleranceDeg = 3f;
            if (request.RequireStableCondition &&
                request.CompletionPolicy == AERISSetpointCompletionPolicy.CaptureOnly)
                request.CompletionPolicy = AERISSetpointCompletionPolicy.StableCondition;
            if (!ValidateSetpoint(request, out result)) return false;
            // PRE-LEARN corridor is a route service and remains the active mission.
            // An atomic setpoint updates its altitude/speed target without destroying
            // the shuttle route or forcing the external client to stream AP values.
            if (TryUpdateActiveLearningCorridorSetpoint(record, request,
                out command, out result)) return result.Success;
            if (NativeControlBusy(record))
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "AERIS native user-selected AP is active and has higher priority.", true);
            if (!PrepareMissionReplacement(record, request.ReplaceCurrentMission, out result)) return false;

            Vessel vessel = FlightGlobals.ActiveVessel;
            if (!record.OwnsControl || record.BeforeMission == null) record.BeforeMission = CaptureState();
            string error;
            try
            {
                if (!core.Hdg.TrySetTarget(request.HeadingDeg.ToString("0.000", CultureInfo.InvariantCulture), out error))
                    return RollbackMissionStart(record, out result, "HDG target rejected: " + error);
                if (!core.Altitude.TrySetTarget(request.AltitudeM.ToString("0.0", CultureInfo.InvariantCulture), out error))
                    return RollbackMissionStart(record, out result, "ALT target rejected: " + error);
                if (!core.Velocity.TrySetTarget(request.SurfaceSpeedMps.ToString("0.0", CultureInfo.InvariantCulture), out error))
                    return RollbackMissionStart(record, out result, "VEL target rejected: " + error);

                core.Hdg.SetArmed(true, vessel, core.Bank, core.Attitude);
                core.Pitch.SetArmed(true, vessel, core.Attitude);
                core.VerticalSpeed.SetArmed(true, vessel, core.Attitude, core.Pitch);
                core.Altitude.SetArmed(true, vessel, core.Attitude, core.VerticalSpeed, core.Pitch);
                core.Acceleration.SetArmed(true, vessel, core.Attitude);
                core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
                if (!core.Master) core.Master = true;
            }
            catch (Exception ex)
            {
                return RollbackMissionStart(record, out result, "Setpoint transaction fault: " + ex.Message);
            }

            record.SetpointRequest = SnapshotSetpointRequest(request);
            record.Command = NewCommand(record, "SETPOINT");
            record.CommandKind = "SETPOINT";
            record.State = AERISAutomationState.Configuring;
            record.Detail = "ATOMIC HDG/ALT/VEL MISSION ACCEPTED";
            record.FailureCode = AERISAutomationResultCode.None;
            record.ConditionStable = false;
            record.MissionCompleted = false;
            record.MissionFailed = false;
            record.ProtectIntervening = false;
            record.PilotOverride = false;
            record.StableSince = -1f;
            record.MissionAcceptedRealtime = Time.realtimeSinceStartup;
            record.OwnsControl = true;
            command = CloneCommand(record.Command);
            result = Accepted("Setpoint mission accepted atomically.");
            LogTransition(record, "SETPOINT ACCEPTED alt=" + request.AltitudeM.ToString("0.0") +
                " speed=" + request.SurfaceSpeedMps.ToString("0.0") +
                " hdg=" + request.HeadingDeg.ToString("0.0") +
                " throttleHint=" + Mathf.Clamp01(request.ThrottleHint01).ToString("0.00"));
            return true;
        }


        internal bool TryGetStatus(AERISAutomationSession session,
            out AERISAutomationSnapshot snapshot)
        {
            snapshot = new AERISAutomationSnapshot();
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId || session == null) return false;
            SessionRecord record;
            if (sessions.TryGetValue(session.SessionId, out record) && SessionMatches(record, session))
            {
                if (LeaseExpired(record))
                {
                    Expire(record);
                    ExpiredSnapshotRecord terminal;
                    if (terminalSnapshots.TryGetValue(session.SessionId, out terminal))
                    { snapshot = terminal.Snapshot; return true; }
                    return false;
                }
                snapshot = EnrichV2Snapshot(record, BuildSnapshot(record));
                return true;
            }
            ExpiredSnapshotRecord old;
            if (terminalSnapshots.TryGetValue(session.SessionId, out old) &&
                old.Snapshot.VesselId == session.VesselId &&
                string.Equals(old.Snapshot.ClientId, session.ClientId, StringComparison.Ordinal))
            {
                snapshot = old.Snapshot;
                return true;
            }
            return false;
        }

        internal bool TryCancelCurrentMission(AERISAutomationSession session,
            string reason, out AERISAutomationResult result)
        {
            SessionRecord record;
            if (!TryValidateSession(session, out record, out result)) return false;
            if (string.IsNullOrEmpty(record.CommandKind))
            {
                result = Accepted("No active mission to cancel.");
                return true;
            }
            Terminate(record, AERISAutomationState.Cancelled,
                AERISAutomationResultCode.None,
                string.IsNullOrEmpty(reason) ? "CANCELLED BY CLIENT" : reason, true, true);
            result = Accepted("Mission cancelled and authority safely released.");
            return true;
        }

        internal bool TryRelease(AERISAutomationSession session,
            out AERISAutomationResult result)
        {
            SessionRecord record;
            if (!TryValidateSession(session, out record, out result)) return false;
            if (!string.IsNullOrEmpty(record.CommandKind))
                Terminate(record, AERISAutomationState.Cancelled,
                    AERISAutomationResultCode.None, "SESSION RELEASE", true, true);
            else if (record.OwnsControl)
                ReleaseControl(record, "SESSION RELEASE", true);
            LogTransition(record, "SESSION RELEASED");
            RemoveSession(record.Session.SessionId);
            result = Accepted("Automation session released.");
            return true;
        }


        internal void Tick()
        {
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            CleanupTerminalSnapshots();
            iterationBuffer.Clear();
            foreach (Guid id in sessions.Keys) iterationBuffer.Add(id);
            for (int i = 0; i < iterationBuffer.Count; i++)
            {
                SessionRecord record;
                if (!sessions.TryGetValue(iterationBuffer[i], out record)) continue;
                if (LeaseExpired(record)) { Expire(record); continue; }
                if (record.Session.VesselId != ActiveVesselId())
                {
                    Terminate(record, AERISAutomationState.Faulted,
                        AERISAutomationResultCode.WrongVessel,
                        "ACTIVE VESSEL CHANGED", false, false);
                    SaveTerminalSnapshot(record);
                    RemoveSession(record.Session.SessionId);
                    continue;
                }
                UpdatePilotOverride(record);
                UpdateProtectState(record);
                TickV2Advisories(record);
                if (record.PilotOverride) continue;
                if (string.IsNullOrEmpty(record.CommandKind)) continue;
                if (UpdateV2Mission(record)) continue;
                if (record.CommandKind == "SETPOINT") UpdateSetpoint(record);
            }
        }

        internal bool TryGetDisplay(out string ownerLine, out string taskLine, out string stateLine, out bool alert)
        {
            ownerLine = taskLine = stateLine = string.Empty;
            alert = false;
            Guid ownerId;
            SessionRecord record;
            if (!vesselOwners.TryGetValue(ActiveVesselId(), out ownerId) ||
                !sessions.TryGetValue(ownerId, out record)) return false;
            ownerLine = "EXT AP: " + record.Session.ClientId;
            taskLine = "TASK: " + (string.IsNullOrEmpty(record.Purpose)
                ? (string.IsNullOrEmpty(record.CommandKind) ? "SESSION" : record.CommandKind)
                : record.Purpose);
            stateLine = "STATE: " + record.State;
            TryGetV2PrimaryDisplay(record, ref ownerLine, ref taskLine, ref stateLine);
            alert = record.State == AERISAutomationState.Faulted ||
                record.State == AERISAutomationState.Rejected ||
                record.State == AERISAutomationState.SuspendedByPilot ||
                record.State == AERISAutomationState.LeaseExpired;
            return true;
        }

        internal void HandleCoreReset(string reason)
        {
            iterationBuffer.Clear();
            foreach (Guid id in sessions.Keys) iterationBuffer.Add(id);
            for (int i = 0; i < iterationBuffer.Count; i++)
            {
                SessionRecord record;
                if (!sessions.TryGetValue(iterationBuffer[i], out record)) continue;
                ClearV2ForSession(record, true, true);
                record.State = AERISAutomationState.Cancelled;
                record.Detail = "CORE RESET — " + reason;
                record.MissionFailed = !string.IsNullOrEmpty(record.CommandKind);
                record.FailureCode = AERISAutomationResultCode.VesselUnavailable;
                SaveTerminalSnapshot(record);
            }
            sessions.Clear();
            vesselOwners.Clear();
        }

        void UpdateSetpoint(SessionRecord record)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;
            if (!core.Master || !core.Hdg.Armed || !core.Altitude.Armed || !core.Velocity.Armed)
            {
                SuspendForPilot(record, "AP MODE OR MASTER WAS RELEASED");
                return;
            }
            if (record.ProtectIntervening)
            {
                record.ConditionStable = false;
                record.StableSince = -1f;
                record.State = AERISAutomationState.SuspendedByProtect;
                record.Detail = "PROTECT INTERVENING";
                return;
            }

            AERISSetpointMissionRequest r = record.SetpointRequest;
            double verticalSpeedSample = core.Attitude != null && core.Attitude.VerticalSpeedValid
                ? core.Attitude.VerticalSpeedMps : vessel.verticalSpeed;
            double headingSample = core.Attitude != null && core.Attitude.HeadingValid
                ? core.Attitude.HeadingDeg : (core.Hdg != null ? core.Hdg.CurrentHeading : double.NaN);
            if (!Finite(vessel.altitude) || !Finite(vessel.srfSpeed) ||
                !Finite(verticalSpeedSample) || !Finite(headingSample))
            {
                record.ConditionStable = false;
                record.StableSince = -1f;
                record.State = AERISAutomationState.Stabilizing;
                record.Detail = "WAITING FOR VALID SETPOINT TELEMETRY";
                return;
            }
            float altitudeError = Mathf.Abs((float)(r.AltitudeM - vessel.altitude));
            float speedError = Mathf.Abs((float)(r.SurfaceSpeedMps - vessel.srfSpeed));
            float heading = (float)headingSample;
            float headingError = Mathf.Abs(Mathf.DeltaAngle(heading, (float)r.HeadingDeg));
            float vs = Mathf.Abs((float)verticalSpeedSample);
            float bank = core.Attitude != null && core.Attitude.InstrumentHorizonBankValid
                ? Mathf.Abs(core.Attitude.InstrumentHorizonBankDeg) : 999f;
            bool inside = altitudeError <= r.AltitudeToleranceM &&
                speedError <= r.SpeedToleranceMps && headingError <= r.HeadingToleranceDeg &&
                vs <= r.VerticalSpeedToleranceMps && bank <= r.BankToleranceDeg;
            float now = Time.realtimeSinceStartup;
            if (inside)
            {
                if (record.StableSince < 0f) record.StableSince = now;
                record.ConditionStable = now - record.StableSince >= r.StableSeconds;
            }
            else
            {
                record.StableSince = -1f;
                record.ConditionStable = false;
            }

            record.State = record.ConditionStable ? AERISAutomationState.Stable :
                (now - record.MissionAcceptedRealtime < 0.5f ? AERISAutomationState.Executing : AERISAutomationState.Stabilizing);
            record.Detail = "SETPOINT altErr=" + altitudeError.ToString("0.0") +
                "m speedErr=" + speedError.ToString("0.00") +
                "m/s hdgErr=" + headingError.ToString("0.0") +
                "deg vs=" + vs.ToString("0.00") + "m/s bank=" +
                bank.ToString("0.0") + "deg";

            bool complete = false;
            if (r.CompletionPolicy == AERISSetpointCompletionPolicy.CaptureOnly)
                complete = now - record.MissionAcceptedRealtime >= 0.25f;
            else if (r.CompletionPolicy == AERISSetpointCompletionPolicy.StableCondition)
                complete = record.ConditionStable;
            if (complete) CompleteMission(record, "SETPOINT COMPLETE", false);
        }


        void CompleteMission(SessionRecord record, string detail, bool onGround)
        {
            record.State = AERISAutomationState.Completed;
            record.Detail = detail;
            record.MissionCompleted = true;
            record.MissionFailed = false;
            record.FailureCode = AERISAutomationResultCode.None;
            record.ConditionStable = record.CommandKind == "SETPOINT" ? record.ConditionStable : true;
            LogTransition(record, detail);
            FinalizeV2MissionRuntime(record, onGround);
            if (onGround)
                ReleaseControl(record, detail, false);
            record.CommandKind = string.Empty;
        }

        void UpdatePilotOverride(SessionRecord record)
        {
            if (!record.OwnsControl || string.IsNullOrEmpty(record.CommandKind)) return;
            FlightCtrlState input = FlightInputHandler.state;
            bool strong = input != null && (Mathf.Abs(input.pitch) >= PilotAxisThreshold ||
                Mathf.Abs(input.roll) >= PilotAxisThreshold || Mathf.Abs(input.yaw) >= PilotAxisThreshold ||
                Mathf.Abs(input.wheelSteer) >= 0.65f);
            float now = Time.realtimeSinceStartup;
            if (strong)
            {
                if (record.PilotInputSince < 0f) record.PilotInputSince = now;
                if (now - record.PilotInputSince >= PilotOverrideDwellSeconds)
                    SuspendForPilot(record, "PILOT HARD OVERRIDE");
            }
            else record.PilotInputSince = -1f;
        }

        void SuspendForPilot(SessionRecord record, string reason)
        {
            if (record == null || record.PilotOverride) return;
            // Contract v2: a hard pilot override is not a resumable pause.  Pilot owns
            // the aircraft absolutely, the active mission is cancelled, all TTL-based
            // feed-forward/advisories are zeroed, and the vessel lease is released.
            record.PilotOverride = true;
            Terminate(record, AERISAutomationState.SuspendedByPilot,
                AERISAutomationResultCode.PilotOverrideActive,
                string.IsNullOrEmpty(reason) ? "PILOT_OVERRIDE" : reason,
                false, false);
            SaveTerminalSnapshot(record);
            Guid sessionId = record.Session.SessionId;
            LogTransition(record, "PILOT OVERRIDE — MISSION CANCELLED / LEASE RELEASED");
            RemoveSession(sessionId);
        }

        void UpdateProtectState(SessionRecord record)
        {
            bool active = core.Protect != null && core.Protect.ProtectActive;
            if (record.ProtectIntervening == active) return;
            record.ProtectIntervening = active;
            record.StableSince = -1f;
            record.ConditionStable = false;
            LogTransition(record, active ? "PROTECT INTERVENTION" : "PROTECT CLEARED — MISSION REEVALUATING");
        }


        bool PrepareMissionReplacement(SessionRecord record, bool replace, out AERISAutomationResult result)
        {
            if (string.IsNullOrEmpty(record.CommandKind)) { result = Accepted("Mission slot available."); return true; }
            if (!replace)
                return Fail(out result, AERISAutomationResultCode.Busy,
                    "A mission is already active for this session.", true);
            ClearV2ForSession(record, true, false);
            ReleaseAllNormalModes("REPLACED BY NEW MISSION");
            record.OwnsControl = false;
            record.BeforeMission = null;
            record.Command = null;
            record.CommandKind = string.Empty;
            record.SetpointRequest = new AERISSetpointMissionRequest();
            record.State = AERISAutomationState.Cancelled;
            record.Detail = "REPLACED BY NEW MISSION";
            record.ConditionStable = false;
            record.MissionCompleted = false;
            record.MissionFailed = false;
            record.FailureCode = AERISAutomationResultCode.None;
            result = Accepted("Existing mission replaced.");
            return true;
        }

        bool NativeControlBusy(SessionRecord record)
        {
            return core.AnyNormalApArmed && !record.OwnsControl;
        }

        bool ValidateSetpoint(AERISSetpointMissionRequest request, out AERISAutomationResult result)
        {
            if (request.CompletionPolicy != AERISSetpointCompletionPolicy.CaptureOnly &&
                request.CompletionPolicy != AERISSetpointCompletionPolicy.StableCondition &&
                request.CompletionPolicy != AERISSetpointCompletionPolicy.HoldUntilReplaced)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Setpoint completion policy is not a published ContractVersion 2 value.", false);
            if (!Finite(request.AltitudeM) || request.AltitudeM < 0.0 || request.AltitudeM > 1000000.0 ||
                !Finite(request.SurfaceSpeedMps) || request.SurfaceSpeedMps < 0.0 || request.SurfaceSpeedMps > 5000.0 ||
                !Finite(request.HeadingDeg) || request.HeadingDeg < -3600.0 || request.HeadingDeg > 3600.0)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Altitude, surface speed, or heading is outside the supported finite range.", false);
            if (!Finite(request.StableSeconds) || request.StableSeconds < 0.0 || request.StableSeconds > 600.0 ||
                !Finite(request.AltitudeToleranceM) || request.AltitudeToleranceM <= 0.0 ||
                !Finite(request.SpeedToleranceMps) || request.SpeedToleranceMps <= 0.0 ||
                !Finite(request.HeadingToleranceDeg) || request.HeadingToleranceDeg <= 0.0 ||
                !Finite(request.VerticalSpeedToleranceMps) || request.VerticalSpeedToleranceMps < 0.0)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "Stable time and setpoint tolerances must be finite positive values.", false);
            if (float.IsNaN(request.ThrottleHint01) || float.IsInfinity(request.ThrottleHint01) ||
                request.ThrottleHint01 < 0f || request.ThrottleHint01 > 1f)
                return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                    "ThrottleHint01 must be finite and between 0 and 1.", false);
            result = Accepted("Setpoint request valid.");
            return true;
        }


        bool TryValidateSession(AERISAutomationSession session, out SessionRecord record,
            out AERISAutomationResult result)
        {
            return TryValidateSession(session, (AERISAutomationCapability[])null, out record, out result);
        }

        bool TryValidateSession(AERISAutomationSession session,
            AERISAutomationCapability required, out SessionRecord record,
            out AERISAutomationResult result)
        {
            return TryValidateSession(session, new[] { required }, out record, out result);
        }

        bool TryValidateSession(AERISAutomationSession session,
            AERISAutomationCapability[] required, out SessionRecord record,
            out AERISAutomationResult result)
        {
            record = null;
            if (!EnsureMainThread(out result)) return false;
            if (session == null || !sessions.TryGetValue(session.SessionId, out record) ||
                !SessionMatches(record, session))
                return Fail(out result, AERISAutomationResultCode.SessionNotFound,
                    "Automation session was not found or token fields do not match.", false);
            if (LeaseExpired(record))
            {
                Expire(record);
                record = null;
                return Fail(out result, AERISAutomationResultCode.LeaseExpired,
                    "Automation lease expired.", false);
            }
            if (required != null)
            {
                for (int i = 0; i < required.Length; i++)
                    if (!ContainsCapability(record.Session.GrantedCapabilities, required[i]))
                        return Fail(out result, AERISAutomationResultCode.CapabilityUnavailable,
                            "Session was not granted required capability: " + required[i] + ".", false);
            }
            if (record.Session.VesselId != ActiveVesselId())
                return Fail(out result, AERISAutomationResultCode.WrongVessel,
                    "Session vessel is not the active vessel.", false);
            result = Accepted("Session valid.");
            return true;
        }

        bool RollbackMissionStart(SessionRecord record, out AERISAutomationResult result, string detail)
        {
            RestoreState(record.BeforeMission, "MISSION START ROLLBACK");
            record.BeforeMission = null;
            record.OwnsControl = false;
            return Fail(out result, AERISAutomationResultCode.InternalFault, detail, true);
        }
        ApStateSnapshot CaptureState()
        {
            var state = new ApStateSnapshot();
            try
            {
                state.Valid = true;
                state.Master = core.Master;
                state.BankArmed = core.Bank != null && core.Bank.Armed;
                state.HdgArmed = core.Hdg != null && core.Hdg.Armed;
                state.PitchArmed = core.Pitch != null && core.Pitch.Armed;
                state.VsArmed = core.VerticalSpeed != null && core.VerticalSpeed.Armed;
                state.AltArmed = core.Altitude != null && core.Altitude.Armed;
                state.AccArmed = core.Acceleration != null && core.Acceleration.Armed;
                state.VelArmed = core.Velocity != null && core.Velocity.Armed;
                state.BankTarget = core.Bank != null ? core.Bank.TargetBank : 0f;
                state.HdgTarget = core.Hdg != null ? core.Hdg.TargetHeading : 0f;
                state.PitchTarget = core.Pitch != null ? core.Pitch.TargetPitch : 0f;
                state.VsTarget = core.VerticalSpeed != null ? core.VerticalSpeed.TargetVerticalSpeedMps : 0f;
                state.AltTarget = core.Altitude != null ? core.Altitude.TargetAltitudeMeters : 0f;
                state.AccTarget = core.Acceleration != null ? core.Acceleration.TargetAccelerationMps2 : 0f;
                state.VelTarget = core.Velocity != null ? core.Velocity.TargetSurfaceSpeedMps : 0f;
                state.Valid = Finite(state.BankTarget) && Finite(state.HdgTarget) &&
                    Finite(state.PitchTarget) && Finite(state.VsTarget) &&
                    Finite(state.AltTarget) && Finite(state.AccTarget) && Finite(state.VelTarget);
                if (!state.Valid)
                    AERISLogger.Warn("[EXT_AUTOMATION] AP snapshot rejected: non-finite setpoint detected.");
            }
            catch (Exception ex)
            {
                state.Valid = false;
                AERISLogger.Warn("[EXT_AUTOMATION] AP snapshot failed: " +
                    ex.GetType().Name + " — " + ex.Message);
            }
            return state;
        }
        void RestoreState(ApStateSnapshot state, string reason)
        {
            if (state == null || !state.Valid) return;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;
            string error;
            try
            {
                ReleaseAllNormalModes(reason);
                core.Bank.TrySetTarget(state.BankTarget.ToString("0.0", CultureInfo.InvariantCulture), out error);
                core.Hdg.TrySetTarget(state.HdgTarget.ToString("0.0", CultureInfo.InvariantCulture), out error);
                core.Pitch.TrySetTarget(state.PitchTarget.ToString("0.0", CultureInfo.InvariantCulture), out error);
                core.VerticalSpeed.TrySetTarget(state.VsTarget.ToString("0.00", CultureInfo.InvariantCulture), out error);
                core.Altitude.TrySetTarget(state.AltTarget.ToString("0.0", CultureInfo.InvariantCulture), out error);
                core.Acceleration.TrySetTarget(state.AccTarget.ToString("0.00", CultureInfo.InvariantCulture), out error);
                core.Velocity.TrySetTarget(state.VelTarget.ToString("0.0", CultureInfo.InvariantCulture), out error);
                if (state.BankArmed) core.Bank.SetArmed(true, vessel);
                if (state.HdgArmed) core.Hdg.SetArmed(true, vessel, core.Bank, core.Attitude);
                if (state.PitchArmed) core.Pitch.SetArmed(true, vessel, core.Attitude);
                if (state.VsArmed) core.VerticalSpeed.SetArmed(true, vessel, core.Attitude, core.Pitch);
                if (state.AltArmed) core.Altitude.SetArmed(true, vessel, core.Attitude, core.VerticalSpeed, core.Pitch);
                if (state.AccArmed) core.Acceleration.SetArmed(true, vessel, core.Attitude);
                if (state.VelArmed) core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
                core.Master = state.Master;
            }
            catch (Exception ex) { AERISLogger.Warn("[EXT_AUTOMATION] restore failed: " + ex.Message); }
        }

        void ReleaseControl(SessionRecord record, string reason, bool restore)
        {
            if (!record.OwnsControl) return;
            if (restore && record.BeforeMission != null && record.BeforeMission.Valid)
                RestoreState(record.BeforeMission, reason);
            else
            {
                Vessel vessel = FlightGlobals.ActiveVessel;
                bool airborne = vessel != null && !vessel.LandedOrSplashed;
                if (airborne) EnterSafeHold(reason);
                else ReleaseAllNormalModes(reason);
            }
            record.OwnsControl = false;
            record.BeforeMission = null;
        }

        void EnterSafeHold(string reason)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return;
            string error;
            try
            {
                if (core.Hdg == null || core.Bank == null || core.Pitch == null ||
                    core.VerticalSpeed == null || core.Altitude == null ||
                    core.Acceleration == null || core.Velocity == null || core.Attitude == null)
                    throw new InvalidOperationException("one or more safe-hold directors are unavailable");
                double heading = core.Attitude.HeadingValid && Finite(core.Attitude.HeadingDeg)
                    ? core.Attitude.HeadingDeg : (Finite(core.Hdg.CurrentHeading)
                        ? core.Hdg.CurrentHeading : 0.0);
                double holdAltitude = Finite(vessel.altitude) ? Math.Max(0.0, vessel.altitude) : 0.0;
                double holdSpeed = Finite(vessel.srfSpeed) ? Math.Max(0.0, vessel.srfSpeed) : 0.0;
                if (!core.Hdg.TrySetTarget(heading.ToString("0.000", CultureInfo.InvariantCulture), out error))
                    throw new InvalidOperationException("HDG target rejected: " + Safe(error));
                if (!core.Altitude.TrySetTarget(holdAltitude.ToString("0.0", CultureInfo.InvariantCulture), out error))
                    throw new InvalidOperationException("ALT target rejected: " + Safe(error));
                if (!core.Velocity.TrySetTarget(holdSpeed.ToString("0.0", CultureInfo.InvariantCulture), out error))
                    throw new InvalidOperationException("VEL target rejected: " + Safe(error));
                core.Hdg.SetArmed(true, vessel, core.Bank, core.Attitude);
                core.Pitch.SetArmed(true, vessel, core.Attitude);
                core.VerticalSpeed.SetArmed(true, vessel, core.Attitude, core.Pitch);
                core.Altitude.SetArmed(true, vessel, core.Attitude, core.VerticalSpeed, core.Pitch);
                core.Acceleration.SetArmed(true, vessel, core.Attitude);
                core.Velocity.SetArmed(true, vessel, core.Attitude, core.Acceleration);
                if (!core.Master) core.Master = true;
                AERISLogger.Warn("[EXT_AUTOMATION] safe HDG/ALT/VEL hold entered: " + reason);
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[EXT_AUTOMATION] safe hold failed; releasing normal modes: " + ex.Message);
                ReleaseAllNormalModes("safe hold failed");
            }
        }

        void ReleaseAllNormalModes(string reason)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            string error;
            SafeReleaseStep(() => { if (core.Altitude != null) core.Altitude.Disable(reason,
                core.VerticalSpeed); }, "ALT", reason);
            SafeReleaseStep(() => { if (core.Velocity != null) core.Velocity.Disable(reason,
                core.Acceleration); }, "VEL", reason);
            SafeReleaseStep(() => { if (core.Acceleration != null)
                core.Acceleration.Disable(reason); }, "ACC", reason);
            SafeReleaseStep(() => { if (core.VerticalSpeed != null)
                core.VerticalSpeed.Disable(reason); }, "V/S", reason);
            SafeReleaseStep(() => { if (core.Pitch != null) core.Pitch.Disable(reason); },
                "PITCH", reason);
            SafeReleaseStep(() => { if (core.Hdg != null) core.Hdg.SetArmed(false,
                vessel, core.Bank, core.Attitude); }, "HDG", reason);
            SafeReleaseStep(() => { if (core.Bank != null) core.Bank.Disable(reason); },
                "BANK", reason);
        }

        static void SafeReleaseStep(Action action, string mode, string reason)
        {
            try { if (action != null) action(); }
            catch (Exception ex)
            {
                AERISLogger.Error("[EXT_AUTOMATION] release step=" + mode +
                    " failed during " + Safe(reason) + ": " + ex.GetType().Name +
                    " — " + ex.Message);
            }
        }

        void Terminate(SessionRecord record, AERISAutomationState state,
            AERISAutomationResultCode failureCode, string detail, bool restore, bool clearCommand)
        {
            FinalizeV2MissionRuntime(record, true);
            ClearV2Advisories(record);
            ReleaseControl(record, detail, restore);
            record.State = state;
            record.Detail = detail;
            record.FailureCode = failureCode;
            record.MissionFailed = state == AERISAutomationState.Faulted ||
                state == AERISAutomationState.Rejected || state == AERISAutomationState.LeaseExpired ||
                state == AERISAutomationState.SuspendedByPilot;
            record.MissionCompleted = false;
            record.ConditionStable = false;
            if (clearCommand) record.CommandKind = string.Empty;
            LogTransition(record, detail);
        }

        void Expire(SessionRecord record)
        {
            Terminate(record, AERISAutomationState.LeaseExpired,
                AERISAutomationResultCode.LeaseExpired,
                "LEASE EXPIRED — SAFE AUTHORITY RELEASE", false, false);
            SaveTerminalSnapshot(record);
            RemoveSession(record.Session.SessionId);
        }

        bool LeaseExpired(SessionRecord record)
        {
            return record == null || UniversalTime() > record.Session.ExpiresUniversalTime ||
                Time.realtimeSinceStartup > record.LeaseExpiresRealtime;
        }

        void SaveTerminalSnapshot(SessionRecord record)
        {
            terminalSnapshots[record.Session.SessionId] = new ExpiredSnapshotRecord
            {
                Snapshot = EnrichV2Snapshot(record, BuildSnapshot(record)),
                RemoveAfterRealtime = Time.realtimeSinceStartup + 30f
            };
        }

        void CleanupTerminalSnapshots()
        {
            iterationBuffer.Clear();
            foreach (KeyValuePair<Guid, ExpiredSnapshotRecord> pair in terminalSnapshots)
                if (Time.realtimeSinceStartup >= pair.Value.RemoveAfterRealtime) iterationBuffer.Add(pair.Key);
            for (int i = 0; i < iterationBuffer.Count; i++) terminalSnapshots.Remove(iterationBuffer[i]);
        }

        void RemoveSession(Guid sessionId)
        {
            SessionRecord record;
            if (!sessions.TryGetValue(sessionId, out record)) return;
            Guid owner;
            if (vesselOwners.TryGetValue(record.Session.VesselId, out owner) && owner == sessionId)
                vesselOwners.Remove(record.Session.VesselId);
            ClearV2ForSession(record, true, true);
            sessions.Remove(sessionId);
        }

        AERISAutomationSnapshot BuildSnapshot(SessionRecord record)
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            bool onGround = vessel != null && vessel.LandedOrSplashed;
            return new AERISAutomationSnapshot
            {
                SessionId = record.Session.SessionId,
                VesselId = record.Session.VesselId,
                ClientId = record.Session.ClientId,
                State = ToMissionState(record.State),
                DetailedState = record.State,
                Detail = record.Detail ?? string.Empty,
                FailureCode = CodeString(record.FailureCode),
                FailureResultCode = record.FailureCode,
                ConditionStable = record.ConditionStable,
                MissionCompleted = record.MissionCompleted,
                MissionFailed = record.MissionFailed,
                ProtectIntervening = record.ProtectIntervening,
                PilotOverride = record.PilotOverride,
                CurrentCommandId = record.Command == null || record.Command.CommandId == Guid.Empty ? string.Empty : record.Command.CommandId.ToString("D"),
                OnGround = onGround,
                GroundSpeedMps = ToNonNegativeFiniteFloat(vessel == null ? 0.0 : vessel.srfSpeed),
                LeaseRemainingSeconds = ToNonNegativeFiniteFloat(Math.Min(
                    record.Session.ExpiresUniversalTime - UniversalTime(),
                    record.LeaseExpiresRealtime - Time.realtimeSinceStartup))
            };
        }

        static AERISAutomationSession CloneSession(AERISAutomationSession value)
        {
            if (value == null) return new AERISAutomationSession();
            return new AERISAutomationSession
            {
                SessionId = value.SessionId, VesselId = value.VesselId, ClientId = value.ClientId,
                GrantedCapabilities = CloneCapabilities(value.GrantedCapabilities),
                ExpiresRealtime = value.ExpiresRealtime,
                ExpiresUniversalTime = value.ExpiresUniversalTime
            };
        }

        static AERISAutomationCommandHandle CloneCommand(AERISAutomationCommandHandle value)
        {
            if (value == null) return new AERISAutomationCommandHandle();
            return new AERISAutomationCommandHandle
            {
                CommandId = value.CommandId, SessionId = value.SessionId,
                TaskKind = value.TaskKind, Kind = value.Kind,
                AcceptedUniversalTime = value.AcceptedUniversalTime
            };
        }

        bool ResolveRequestVessel(Vessel requestVessel, ref Guid requestVesselId,
            SessionRecord record)
        {
            if (record == null) return false;
            Guid resolved = requestVesselId != Guid.Empty ? requestVesselId :
                (requestVessel == null ? Guid.Empty : requestVessel.id);
            if (resolved == Guid.Empty) resolved = record.Session.VesselId;
            requestVesselId = resolved;
            return resolved == record.Session.VesselId &&
                (requestVessel == null || requestVessel.id == record.Session.VesselId) &&
                ActiveVesselId() == record.Session.VesselId;
        }

        AERISAutomationCommandHandle NewCommand(SessionRecord record, string kind)
        {
            return new AERISAutomationCommandHandle
            {
                CommandId = Guid.NewGuid(),
                SessionId = record.Session.SessionId,
                Kind = kind,
                TaskKind = kind,
                AcceptedUniversalTime = UniversalTime()
            };
        }

        bool EnsureMainThread(out AERISAutomationResult result)
        {
            if (Thread.CurrentThread.ManagedThreadId == mainThreadId)
            { result = Accepted("Main thread confirmed."); return true; }
            return Fail(out result, AERISAutomationResultCode.InvalidRequest,
                "AERIS automation API calls are restricted to the Unity main thread.", true);
        }

        bool SessionMatches(SessionRecord record, AERISAutomationSession token)
        {
            return record != null && token != null && token.SessionId == record.Session.SessionId &&
                token.VesselId == record.Session.VesselId &&
                string.Equals(token.ClientId, record.Session.ClientId, StringComparison.Ordinal);
        }


        static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
        static float ToNonNegativeFiniteFloat(double value)
        {
            if (!Finite(value) || value <= 0.0) return 0f;
            if (value >= float.MaxValue) return float.MaxValue;
            return (float)value;
        }
        static double Repeat360(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }
        static Guid ActiveVesselId() { return FlightGlobals.ActiveVessel == null ? Guid.Empty : FlightGlobals.ActiveVessel.id; }
        static double UniversalTime()
        {
            try { return Planetarium.GetUniversalTime(); }
            catch { return Time.realtimeSinceStartup; }
        }
        static string Safe(string value) { return string.IsNullOrEmpty(value) ? "NONE" : value.Replace('\n', ' ').Replace('\r', ' '); }
        static AERISAutomationResult Accepted(string detail)
        {
            return new AERISAutomationResult { Success = true, Code = "ACCEPTED", ResultCode = AERISAutomationResultCode.Accepted, Detail = detail, Retryable = false };
        }
        static bool Fail(out AERISAutomationResult result, AERISAutomationResultCode code, string detail, bool retryable)
        {
            result = new AERISAutomationResult { Success = false, Code = CodeString(code), ResultCode = code, Detail = detail, Retryable = retryable };
            return false;
        }
        static void LogTransition(SessionRecord record, string message)
        {
            string safeMessage = Safe(message);
            AERISLogger.Info("[EXT_AUTOMATION] client=" + record.Session.ClientId +
                " session=" + record.Session.SessionId.ToString("N") +
                " state=" + record.State + " command=" +
                (record.Command == null || record.Command.CommandId == Guid.Empty ? "NONE" : record.Command.CommandId.ToString("N")) +
                " — " + safeMessage);
            AERISRecorderSeverity severity = record.State == AERISAutomationState.Faulted ||
                record.State == AERISAutomationState.Rejected ||
                record.State == AERISAutomationState.LeaseExpired
                ? AERISRecorderSeverity.Error
                : (record.State == AERISAutomationState.SuspendedByPilot ||
                   record.State == AERISAutomationState.SuspendedByProtect
                    ? AERISRecorderSeverity.Warning : AERISRecorderSeverity.Info);
            AERISFlightRecorderApi.RecordEvent("AERIS.ExternalAutomation", "AUTOMATION",
                record.State.ToString(), severity, "client=" + record.Session.ClientId +
                "; command=" + (record.Command == null || record.Command.CommandId == Guid.Empty ? "NONE" : record.Command.CommandId.ToString("N")) +
                "; " + safeMessage);
        }
    }
}
