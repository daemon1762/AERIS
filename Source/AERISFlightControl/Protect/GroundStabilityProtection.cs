using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;
using AtmosphereAutopilot;
using ModuleWheels;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;
using AERISFlightControl.Settings;
using AERISFlightControl.Autopilot;
using AERISFlightControl.API;

namespace AERISFlightControl.Protect
{
    // Ground Stability is a PROTECT function.  It captures the aircraft's current heading
    // rather than a runway centreline, so it remains usable on unprepared terrain.  AERIS
    // publishes bounded native angular-rate demands; AA remains the only final axis writer.
    internal sealed class GroundStabilityProtection
    {
        const float LiftoffConfirmSeconds = 0.40f;
        const float PilotNeutralRecaptureSeconds = 0.30f;
        const float PilotDeadband = 0.08f;
        const float MinimumControlSpeedMps = 2.0f;
        const float AutoStopCaptureSpeedMps = 0.35f;
        const float AutoStopConfirmSeconds = 0.40f;

        sealed class WheelBrakeBinding
        {
            internal PartModule Module;
            internal ModuleWheelBrakes StockModule;
            internal FieldInfo InputField;
            internal PropertyInfo InputProperty;
        }

        readonly AERISSettings settings;
        readonly AERISSpeedAirbrakeController airbrake;
        readonly List<WheelBrakeBinding> wheelBrakeBindings = new List<WheelBrakeBinding>();
        uint wheelBrakeVesselPersistentId;
        int wheelBrakePartCount = -1;
        bool wheelBrakeBindingsScanned;
        float wheelBrakeNextRetryRealtime;
        int wheelBrakeLastLoggedCount = -1;
        uint wheelBrakeLastLoggedVesselPersistentId;
        int wheelBrakeLastLoggedPartCount = -1;
        bool wheelBrakeActionGroupFallbackActive;
        bool wheelBrakeActionGroupWasOnBeforeFallback;
        bool wheelBrakeWriteFailureLogged;
        float brakeFallbackEvidenceSeconds;
        bool pilotBrakeRequestActive;
        float groundOwnershipBlend = 1f;
        float touchdownSessionStartFixedTime;
        bool enabled;
        bool initialized;
        bool reliableGrounded;
        bool liftoffConfirmed;
        bool ownsRoll;
        bool ownsYaw;
        bool ownsThrottle;
        bool postTouchdownSessionActive;
        bool reverseThrustControlActive;
        bool lateralSessionActive;
        bool recapturePending;
        float liftoffCandidateSeconds;
        float liftoffCandidateStartFixedTime = -1f;
        float pilotNeutralSeconds;
        float targetHeadingDeg;
        float lastControlFixedTime;
        float lastBrakeFixedTime;
        float previousBrakeSpeedMps;
        float touchdownStableSeconds;
        float stopStableSeconds;
        bool parkingBrakeWasAppliedBeforeHold;
        uint parkingHoldVesselPersistentId;
        float parkingHoldCaptureFixedTime;
        int parkingHoldPilotReleaseCount;
        float brakeCapabilityMps2PerUnit = 5.0f;
        float brakeDemandState;
        bool dragChuteDeploymentAttempted;
        int dragChuteDeployedCount;
        float reverseThrustDemand;
        string reverseProviderId = "None";
        string dragChuteStatus = "OFF";
        string reverseThrustStatus = "OFF";
        string lastLoggedState = string.Empty;

        internal GroundStabilityProtection(AERISSettings settings, AERISSpeedAirbrakeController airbrake)
        {
            this.settings = settings;
            this.airbrake = airbrake;
            enabled = settings == null || settings.GroundStabilityEnabled;
            Status = enabled ? "ARMED / AWAITING GROUND" : "DISABLED";
        }

        internal bool Enabled
        {
            get { return enabled; }
            set
            {
                if (enabled == value) return;
                enabled = value;
                if (!enabled) Release("disabled");
                AERISLogger.Info("[GROUND_STABILITY] enabled=" + enabled);
            }
        }

        internal bool ReliableGrounded { get { return reliableGrounded; } }
        internal bool LiftoffConfirmed { get { return liftoffConfirmed; } }
        internal bool ControlActive { get; private set; }
        internal bool HeadingHoldActive { get; private set; }
        internal bool PilotSharedControlActive { get; private set; }
        internal bool LowSpeedTransparent { get; private set; }
        internal bool Available { get; private set; }
        internal bool DisplayAmber { get { return enabled && Available && reliableGrounded; } }
        internal string Status { get; private set; }
        internal float TargetHeadingDeg { get { return targetHeadingDeg; } }
        internal float CurrentHeadingDeg { get; private set; }
        internal float HeadingErrorDeg { get; private set; }
        internal float SurfaceSpeedMps { get; private set; }
        internal float RadarAltitudeM { get; private set; }
        internal float VerticalSpeedMps { get; private set; }
        internal float PilotYaw { get; private set; }
        internal float PilotRoll { get; private set; }
        internal float YawRateDemandDegPerSec { get; private set; }
        internal float RollRateDemandDegPerSec { get; private set; }
        internal float YawAuthorityScale { get; private set; }
        internal float RollAuthorityScale { get; private set; }
        internal bool AaNativeYawOverrideActive { get; private set; }
        internal bool AaNativeRollOverrideActive { get; private set; }
        internal bool AaNativeThrottleOverrideActive { get; private set; }
        internal bool PostTouchdownSessionActive { get { return postTouchdownSessionActive; } }
        internal bool ThrottleCutActive { get; private set; }
        internal bool ReverseThrustControlActive { get { return reverseThrustControlActive; } }
        internal bool GroundAssistMasterEnabled { get { return settings == null || settings.GroundAssistEnabled; } }
        internal bool BrakeAssistConfigured { get { return settings != null && settings.GroundBrakeAssistAuto; } }
        internal bool AirbrakeLinkConfigured { get { return settings != null && settings.GroundAirbrakeLinkAuto; } }
        internal bool ParkingHoldConfigured { get { return settings != null && settings.GroundParkingHold; } }
        internal bool DragChuteAutoConfigured { get { return settings != null && settings.GroundDragChuteAuto; } }
        internal bool ReverseThrustAutoConfigured { get { return settings != null && settings.GroundReverseThrustAuto; } }
        internal bool BrakeAssistActive { get; private set; }
        internal bool ParkingHoldActive { get; private set; }
        internal int ParkingHoldPilotReleaseCount { get { return parkingHoldPilotReleaseCount; } }
        internal float TouchdownStableSeconds { get { return touchdownStableSeconds; } }
        internal float RequestedDecelerationMps2 { get; private set; }
        internal float MeasuredDecelerationMps2 { get; private set; }
        internal float BrakeDemand { get; private set; }
        internal float FinalBrakeDemand { get; private set; }
        internal float GroundStabilityAllowance { get; private set; } = 1f;
        internal float BrakeCapabilityMps2PerUnit { get { return brakeCapabilityMps2PerUnit; } }
        internal float AirbrakeLinkDemand { get; private set; }
        internal float WheelBrakeAppliedDemand { get; private set; }
        internal bool WheelBrakeStockFallbackActive { get { return wheelBrakeActionGroupFallbackActive; } }
        internal int WheelBrakeModuleCount { get { return wheelBrakeBindings.Count; } }
        internal bool PilotBrakeRequestActive { get { return pilotBrakeRequestActive; } }
        internal float GroundOwnershipBlend { get { return groundOwnershipBlend; } }
        internal float BrakeFallbackEvidenceSeconds { get { return brakeFallbackEvidenceSeconds; } }
        internal string BrakeAssistStatus { get; private set; } = "STANDBY";
        internal string DragChuteStatus { get { return dragChuteStatus; } }
        internal int DragChuteDeployedCount { get { return dragChuteDeployedCount; } }
        internal string ReverseThrustStatus { get { return reverseThrustStatus; } }
        internal float ReverseThrustDemand { get { return reverseThrustDemand; } }
        internal string ReverseProviderId { get { return reverseProviderId; } }

        internal void UpdateGroundState(Vessel vessel, VirtualAttitudeInstrument attitude)
        {
            if (vessel == null || attitude == null)
            {
                initialized = false;
                reliableGrounded = false;
                liftoffConfirmed = false;
                postTouchdownSessionActive = false;
                Available = false;
                ReleaseThrottleCut("vessel unavailable", true);
                ReleaseBrakeAssist(null, "vessel unavailable", true);
                EnsureWheelBrakeBindings(null);
                return;
            }

            SurfaceSpeedMps = attitude.SurfaceSpeedMps;
            RadarAltitudeM = attitude.RadarAltitudeM;
            VerticalSpeedMps = attitude.VerticalSpeedMps;
            CurrentHeadingDeg = attitude.InstrumentHeadingDeg;
            if (!attitude.SharedSurfaceSpeedValid || !attitude.SharedRadarAltitudeValid ||
                !attitude.VerticalSpeedValid || !IsFinite(SurfaceSpeedMps) || !IsFinite(RadarAltitudeM) ||
                !IsFinite(VerticalSpeedMps) || !IsFinite(CurrentHeadingDeg))
            {
                initialized = false;
                reliableGrounded = false;
                liftoffConfirmed = false;
                Available = false;
                Release("invalid ground sensor sample");
                return;
            }

            bool hadInitialized = initialized;
            bool wasReliableGrounded = reliableGrounded;
            bool wasLiftoffConfirmed = liftoffConfirmed;
            bool water = vessel.situation == Vessel.Situations.SPLASHED;
            bool rawGround = !water && (vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH);
            bool contactLike = rawGround || (!liftoffConfirmed && RadarAltitudeM < 1.25f && VerticalSpeedMps < 1.5f);
            Available = !water && !vessel.packed && attitude.InstrumentHeadingValid && attitude.InstrumentHorizonBankValid;

            if (!initialized)
            {
                initialized = true;
                reliableGrounded = contactLike;
                liftoffConfirmed = !contactLike && RadarAltitudeM >= 3f;
                targetHeadingDeg = CurrentHeadingDeg;
            }

            if (contactLike)
            {
                bool touchdownEdge = hadInitialized && !wasReliableGrounded && wasLiftoffConfirmed;
                if (touchdownEdge)
                {
                    postTouchdownSessionActive = true;
                    touchdownStableSeconds = 0f;
                    stopStableSeconds = 0f;
                    MeasuredDecelerationMps2 = 0f;
                    brakeDemandState = 0f;
                    previousBrakeSpeedMps = SurfaceSpeedMps;
                    lastBrakeFixedTime = Time.fixedTime;
                    ParkingHoldActive = false;
                    parkingHoldVesselPersistentId = 0u;
                    WheelBrakeAppliedDemand = 0f;
                    brakeFallbackEvidenceSeconds = 0f;
                    pilotBrakeRequestActive = false;
                    groundOwnershipBlend = 0f;
                    touchdownSessionStartFixedTime = Time.fixedTime;
                    EnsureWheelBrakeBindings(vessel);
                    dragChuteDeploymentAttempted = false;
                    dragChuteDeployedCount = 0;
                    dragChuteStatus = settings != null && settings.GroundDragChuteAuto ? "AUTO — ARMED" : "OFF";
                    reverseThrustStatus = settings != null && settings.GroundReverseThrustAuto ? "AUTO — ARMED" : "OFF";
                    AERISLogger.Info("[GROUND_ASSIST][THROTTLE] touchdown session latched; normal forward thrust will be held at zero unless reverse-thrust control is active.");
                }
                reliableGrounded = true;
                liftoffConfirmed = false;
                liftoffCandidateSeconds = 0f;
                liftoffCandidateStartFixedTime = -1f;
            }
            else if (reliableGrounded)
            {
                bool validDeparture = RadarAltitudeM >= 3f && VerticalSpeedMps > 0.5f;
                if (validDeparture)
                {
                    if (liftoffCandidateStartFixedTime < 0f)
                        liftoffCandidateStartFixedTime = Time.fixedTime;
                    // UpdateGroundState is intentionally callable from physics, UI and
                    // state-observation paths.  Measure one physical-time interval instead
                    // of adding fixedDeltaTime per call, otherwise the 0.40 s dwell would
                    // be shortened whenever more than one caller observes the same frame.
                    liftoffCandidateSeconds = Mathf.Max(0f,
                        Time.fixedTime - liftoffCandidateStartFixedTime);
                }
                else
                {
                    liftoffCandidateSeconds = 0f;
                    liftoffCandidateStartFixedTime = -1f;
                }
                if (liftoffCandidateSeconds >= LiftoffConfirmSeconds)
                {
                    reliableGrounded = false;
                    liftoffConfirmed = true;
                    postTouchdownSessionActive = false;
                    ReleaseThrottleCut("liftoff confirmed", true);
                    ReleaseBrakeAssist(vessel, "liftoff confirmed", true);
                    liftoffCandidateStartFixedTime = -1f;
                }
            }
        }

        internal bool WantsLateralOwnership(bool master, bool standardFbwActive, bool autoTakeoffLateralAssist)
        {
            return GroundAssistMasterEnabled && enabled && master && standardFbwActive && Available &&
                (reliableGrounded || autoTakeoffLateralAssist);
        }

        internal void SetReverseThrustControlActive(bool active)
        {
            if (reverseThrustControlActive == active) return;
            reverseThrustControlActive = active;
            AERISLogger.Info("[GROUND_ASSIST][THROTTLE] reverse-thrust control active=" + active);
        }

        internal float ApplyThrottleCeiling(float requestedThrottle)
        {
            return ThrottleCutActive && !reverseThrustControlActive ? 0f :
                (IsFinite(requestedThrottle) ? Mathf.Clamp01(requestedThrottle) : 0f);
        }

        internal void ApplyAaNativeGroundDemand(FlightCtrlState state, Vessel vessel,
            VirtualAttitudeInstrument attitude, bool master, bool standardFbwActive,
            bool autoTakeoffLateralAssist)
        {
            UpdateGroundState(vessel, attitude);
            bool eligible = state != null && WantsLateralOwnership(master, standardFbwActive, autoTakeoffLateralAssist);
            if (!eligible)
            {
                if (!GroundAssistMasterEnabled || !enabled || !master || !standardFbwActive)
                    Release(!GroundAssistMasterEnabled ? "ground-assist-off" :
                        (!enabled ? "disabled" : (!master ? "master-off" : "aa-unavailable")));
                else
                    ReleaseLateral("unavailable");
                ApplyGroundBrakeAssist(state, vessel, attitude, master, standardFbwActive, autoTakeoffLateralAssist);
                ApplyGroundThrottleInterlock(master, standardFbwActive, autoTakeoffLateralAssist);
                return;
            }

            float now = Time.fixedTime;
            float dt = lastControlFixedTime > 0f ? Mathf.Clamp(now - lastControlFixedTime, 0.005f, 0.05f) :
                Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.005f, 0.05f);
            lastControlFixedTime = now;
            if (autoTakeoffLateralAssist || !postTouchdownSessionActive) groundOwnershipBlend = 1f;
            else
            {
                float transferElapsed = Mathf.Max(0f, now - touchdownSessionStartFixedTime);
                groundOwnershipBlend = Mathf.SmoothStep(0f, 1f,
                    Mathf.InverseLerp(0.15f, 1.20f, transferElapsed));
            }

            if (!lateralSessionActive)
            {
                targetHeadingDeg = CurrentHeadingDeg;
                lateralSessionActive = true;
                recapturePending = false;
                pilotNeutralSeconds = 0f;
            }

            ReadPilotInputs(state);
            float pilotMagnitude = Mathf.Max(Mathf.Abs(PilotYaw), Mathf.Abs(PilotRoll));
            PilotSharedControlActive = pilotMagnitude > PilotDeadband;
            if (PilotSharedControlActive)
            {
                pilotNeutralSeconds = 0f;
                recapturePending = true;
            }
            else pilotNeutralSeconds += dt;

            // Manual yaw moves the held-heading reference rather than fighting a frozen line.
            float pilotYawShaped = ShapeInput(PilotYaw, PilotDeadband);
            float manualHeadingRate = pilotYawShaped * ManualYawRateLimit(SurfaceSpeedMps);
            if (Mathf.Abs(pilotYawShaped) > 0f)
                targetHeadingDeg = Mathf.Repeat(targetHeadingDeg + manualHeadingRate * dt, 360f);
            else if (recapturePending && pilotNeutralSeconds >= PilotNeutralRecaptureSeconds)
            {
                targetHeadingDeg = CurrentHeadingDeg;
                recapturePending = false;
            }

            HeadingErrorDeg = Mathf.DeltaAngle(CurrentHeadingDeg, targetHeadingDeg);
            float yawRate = attitude.InstrumentYawRateDegPerSec;
            float bank = attitude.InstrumentHorizonBankDeg;
            float rollRate = attitude.InstrumentRollRateDegPerSec;

            LowSpeedTransparent = reliableGrounded && SurfaceSpeedMps < MinimumControlSpeedMps;
            YawAuthorityScale = autoTakeoffLateralAssist && !reliableGrounded
                ? 0.65f : Mathf.Clamp01((SurfaceSpeedMps - MinimumControlSpeedMps) / 10f);
            RollAuthorityScale = autoTakeoffLateralAssist && !reliableGrounded
                ? 0.75f : Mathf.Clamp01((SurfaceSpeedMps - 4f) / 16f);

            float yawLimit = GroundYawRateLimit(SurfaceSpeedMps);
            float yawHold = 0.65f * HeadingErrorDeg - 0.45f * yawRate;
            YawRateDemandDegPerSec = Mathf.Clamp(manualHeadingRate + yawHold * YawAuthorityScale,
                -yawLimit, yawLimit) * groundOwnershipBlend;

            float pilotRollShaped = ShapeInput(PilotRoll, PilotDeadband);
            float rollLimit = GroundRollRateLimit(SurfaceSpeedMps);
            float rollHold = -0.55f * bank - 0.38f * rollRate;
            RollRateDemandDegPerSec = Mathf.Clamp(
                pilotRollShaped * rollLimit + rollHold * RollAuthorityScale,
                -rollLimit, rollLimit) * groundOwnershipBlend;
            // Continuous bank envelope: shared manual input is accepted until it would
            // drive an already excessive ground bank farther outward.
            float safeBankLimit = reliableGrounded ? 6f : 12f;
            if (bank > safeBankLimit)
                RollRateDemandDegPerSec = Mathf.Min(RollRateDemandDegPerSec,
                    -Mathf.Min(rollLimit, 0.45f * (bank - safeBankLimit)));
            else if (bank < -safeBankLimit)
                RollRateDemandDegPerSec = Mathf.Max(RollRateDemandDegPerSec,
                    Mathf.Min(rollLimit, 0.45f * (-safeBankLimit - bank)));

            // Do not seize a nearly stationary taxiing aircraft.  Above the transparent
            // zone, only the axes explicitly owned here are neutralized inside AA.
            bool yawOwn = !LowSpeedTransparent || autoTakeoffLateralAssist;
            bool rollOwn = RollAuthorityScale > 0.05f || autoTakeoffLateralAssist;
            if (yawOwn)
            {
                StandardFlyByWire.ExternalYawDemand = YawRateDemandDegPerSec * Mathf.Deg2Rad;
                StandardFlyByWire.ExternalYawOverride = true;
                ownsYaw = AaNativeYawOverrideActive = true;
            }
            else ClearYaw();

            if (rollOwn)
            {
                StandardFlyByWire.ExternalRollDemand = RollRateDemandDegPerSec * Mathf.Deg2Rad;
                StandardFlyByWire.ExternalRollOverride = true;
                ownsRoll = AaNativeRollOverrideActive = true;
            }
            else ClearRoll();

            ControlActive = yawOwn || rollOwn;
            HeadingHoldActive = yawOwn && !PilotSharedControlActive;
            Status = LowSpeedTransparent ? "LOW-SPEED TRANSPARENT" :
                (PilotSharedControlActive ? "SHARED PILOT CONTROL" :
                (reliableGrounded ? "GROUND HEADING HOLD" : "TAKEOFF LATERAL HOLD"));
            ApplyGroundBrakeAssist(state, vessel, attitude, master, standardFbwActive, autoTakeoffLateralAssist);
            ApplyGroundThrottleInterlock(master, standardFbwActive, autoTakeoffLateralAssist);
            LogStateEdge(Status);
        }

        void ApplyGroundThrottleInterlock(bool master, bool standardFbwActive, bool autoTakeoffLateralAssist)
        {
            // The post-touchdown session ends only after Auto Stop confirms near-zero speed.
            // This keeps normal forward thrust at zero through the complete rollout rather
            // than releasing it in the 2 m/s lateral-transparent region.
            if (postTouchdownSessionActive && reliableGrounded &&
                SurfaceSpeedMps <= AutoStopCaptureSpeedMps &&
                (!BrakeAssistConfigured || !GroundAssistMasterEnabled || !enabled))
            {
                postTouchdownSessionActive = false;
                ReleaseThrottleCut("low-speed fallback stop complete", true);
            }

            bool shouldCut = GroundAssistMasterEnabled && enabled && master && standardFbwActive && reliableGrounded &&
                postTouchdownSessionActive && !autoTakeoffLateralAssist &&
                !reverseThrustControlActive && SurfaceSpeedMps > AutoStopCaptureSpeedMps;
            if (shouldCut)
            {
                if (!ownsThrottle)
                {
                    StandardFlyByWire.SetExternalThrottleReleaseBaseline(0f);
                    AERISLogger.Info("[GROUND_ASSIST][THROTTLE] CUT ACTIVE; forward throttle ceiling=0.000.");
                }
                StandardFlyByWire.ExternalThrottleDemand = 0f;
                StandardFlyByWire.ExternalThrottleOverride = true;
                ownsThrottle = true;
                ThrottleCutActive = true;
                AaNativeThrottleOverrideActive = true;
                return;
            }

            if (autoTakeoffLateralAssist || reverseThrustControlActive)
            {
                // A later-published Auto Takeoff/reverse-thrust owner retains the shared
                // AA throttle transport. Ground Assist only relinquishes its own ceiling.
                ownsThrottle = false;
                ThrottleCutActive = false;
                AaNativeThrottleOverrideActive = false;
                return;
            }
            ReleaseThrottleCut(!GroundAssistMasterEnabled ? "ground-assist-off" :
                (!enabled ? "disabled" : (!master ? "master-off" : "not-active")), false);
        }

        void ApplyGroundBrakeAssist(FlightCtrlState state, Vessel vessel,
            VirtualAttitudeInstrument attitude, bool master, bool standardFbwActive,
            bool autoTakeoffLateralAssist)
        {
            float now = Time.fixedTime;
            float dt = lastBrakeFixedTime > 0f ? Mathf.Clamp(now - lastBrakeFixedTime, 0.005f, 0.10f)
                : Mathf.Clamp(TimeWarp.fixedDeltaTime, 0.005f, 0.10f);
            bool firstSample = lastBrakeFixedTime <= 0f;
            lastBrakeFixedTime = now;
            float speed = Mathf.Max(0f, SurfaceSpeedMps);
            float rawDecel = !firstSample && dt > 0.0001f ? (previousBrakeSpeedMps - speed) / dt : 0f;
            previousBrakeSpeedMps = speed;
            MeasuredDecelerationMps2 = Mathf.Lerp(MeasuredDecelerationMps2,
                Mathf.Clamp(rawDecel, -15f, 20f), 1f - Mathf.Exp(-dt * 4f));

            // AERIS sets the stock Brakes action group ON when Parking Hold captures.
            // A subsequent stock brake-button toggle drives that action group OFF. Treat
            // the falling edge as an explicit pilot release instead of reasserting it.
            if (ParkingHoldActive && vessel != null &&
                (parkingHoldVesselPersistentId == 0u || parkingHoldVesselPersistentId == vessel.persistentId) &&
                now - parkingHoldCaptureFixedTime >= 0.15f &&
                !vessel.ActionGroups[KSPActionGroup.Brakes])
            {
                ReleaseParkingHoldByPilot(vessel);
                return;
            }

            bool configured = settings != null && settings.GroundAssistEnabled &&
                settings.GroundBrakeAssistAuto && enabled;
            bool eligible = configured && master && standardFbwActive && state != null && vessel != null &&
                reliableGrounded && postTouchdownSessionActive && !autoTakeoffLateralAssist;
            if (!eligible)
            {
                bool mustReleaseParking = ParkingHoldActive &&
                    (!configured || !master || !standardFbwActive || vessel == null || !reliableGrounded);
                if (mustReleaseParking) ReleaseBrakeAssist(vessel, "parking hold released", true);
                else if (!ParkingHoldActive) ReleaseBrakeAssist(vessel,
                    !configured ? "disabled" : "standby", false);
                return;
            }

            float bank = attitude != null ? Mathf.Abs(attitude.InstrumentHorizonBankDeg) : 0f;
            float yawRate = attitude != null ? Mathf.Abs(attitude.InstrumentYawRateDegPerSec) : 0f;
            float heading = Mathf.Abs(HeadingErrorDeg);
            bool stableContact = Mathf.Abs(VerticalSpeedMps) < 3.0f && bank < 8f && heading < 12f;
            touchdownStableSeconds = stableContact ? touchdownStableSeconds + dt :
                Mathf.Max(0f, touchdownStableSeconds - 2f * dt);

            float headingAllowance = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 15f, heading));
            float yawAllowance = Mathf.Lerp(1f, 0.20f,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2f, 10f, yawRate)));
            float bankAllowance = Mathf.Lerp(1f, 0.25f,
                Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3f, 9f, bank)));
            float pilotAllowance = PilotSharedControlActive ? 0.60f : 1f;
            GroundStabilityAllowance = heading > 25f || yawRate > 15f || bank > 12f
                ? 0f : Mathf.Clamp01(headingAllowance * yawAllowance * bankAllowance * pilotAllowance);

            bool stopCandidate = speed <= AutoStopCaptureSpeedMps && stableContact;
            stopStableSeconds = stopCandidate ? stopStableSeconds + dt : 0f;
            if (stopStableSeconds >= AutoStopConfirmSeconds)
            {
                postTouchdownSessionActive = false;
                AirbrakeLinkDemand = 0f;
                if (airbrake != null && airbrake.GroundAssistActive)
                    airbrake.UpdateGroundAssist(vessel, 0f, settings.GroundAirbrakeLimit, false);
                if (settings.GroundParkingHold)
                {
                    if (!ParkingHoldActive)
                        parkingBrakeWasAppliedBeforeHold = vessel.ActionGroups[KSPActionGroup.Brakes];
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
                    ApplyWheelBrakeDemand(vessel, 1f);
                    ParkingHoldActive = true;
                    parkingHoldVesselPersistentId = vessel.persistentId;
                    if (parkingHoldCaptureFixedTime <= 0f) parkingHoldCaptureFixedTime = now;
                    BrakeAssistActive = true;
                    BrakeDemand = FinalBrakeDemand = 1f;
                    BrakeAssistStatus = "PARKING HOLD";
                    ReleaseThrottleCut("parking hold captured", true);
                }
                else
                {
                    ReleaseBrakeAssist(vessel, "auto stop complete", false);
                    BrakeAssistStatus = "STOP COMPLETE";
                    ReleaseThrottleCut("auto stop complete", true);
                }
                return;
            }

            float ramp = Mathf.SmoothStep(0f, 1f,
                Mathf.InverseLerp(0.20f, 1.20f, touchdownStableSeconds));
            float maximumNormalDecel = Mathf.Clamp(settings.GroundMaximumNormalDecelerationMps2, 3f, 8f);
            float targetDecel = Mathf.Min(maximumNormalDecel,
                Mathf.Clamp(settings.GroundTargetDecelerationMps2, 0.5f, 12f)) * ramp;
            float fadeEnd = Mathf.Max(2f, settings.GroundLowSpeedFadeMps);
            float lowFade = Mathf.Clamp01(Mathf.InverseLerp(0.25f, fadeEnd, speed));
            RequestedDecelerationMps2 = targetDecel * lowFade;
            if (speed > AutoStopCaptureSpeedMps && speed < 2f && ramp > 0.5f)
                RequestedDecelerationMps2 = Mathf.Max(RequestedDecelerationMps2, 0.35f);

            float capability = Mathf.Clamp(brakeCapabilityMps2PerUnit, 1.5f, 12f);
            float feedForward = RequestedDecelerationMps2 / capability;
            float correction = 0.12f *
                (RequestedDecelerationMps2 - Mathf.Max(0f, MeasuredDecelerationMps2));
            float requestedBrake = Mathf.Clamp(feedForward + correction, 0f,
                Mathf.Clamp01(settings.GroundMaxBrake));
            brakeDemandState = Mathf.MoveTowards(brakeDemandState, requestedBrake,
                (requestedBrake > brakeDemandState ? 0.65f : 1.40f) * dt);
            BrakeDemand = brakeDemandState;
            if (MeasuredDecelerationMps2 > maximumNormalDecel)
            {
                float overDecel = MeasuredDecelerationMps2 - maximumNormalDecel;
                requestedBrake = Mathf.Max(0f, requestedBrake - overDecel * 0.18f);
                brakeDemandState = Mathf.MoveTowards(brakeDemandState, requestedBrake, 3.0f * dt);
            }
            FinalBrakeDemand = Mathf.Clamp01(brakeDemandState * GroundStabilityAllowance *
                Mathf.Clamp01(groundOwnershipBlend));

            if (brakeDemandState > 0.15f && FinalBrakeDemand > 0.10f &&
                MeasuredDecelerationMps2 > 0.2f && GroundStabilityAllowance > 0.70f &&
                dragChuteDeployedCount == 0 && !reverseThrustControlActive &&
                reverseThrustDemand <= 0.01f)
            {
                float observedCapability = Mathf.Clamp(MeasuredDecelerationMps2 /
                    Mathf.Max(0.10f, FinalBrakeDemand), 1.5f, 12f);
                brakeCapabilityMps2PerUnit = Mathf.MoveTowards(brakeCapabilityMps2PerUnit,
                    observedCapability, 0.30f * dt);
            }

            // Separate pilot state from the stock action-group bit owned by AERIS.
            // The v0.11.7 self-latch interpreted its own fallback bit as a new pilot
            // full-brake command on the next frame.
            bool stockBrakeOn = vessel.ActionGroups[KSPActionGroup.Brakes];
            pilotBrakeRequestActive = stockBrakeOn && !wheelBrakeActionGroupFallbackActive &&
                !ParkingHoldActive;
            float wheelBrakeDemand = pilotBrakeRequestActive ? 1f : FinalBrakeDemand;
            bool meaningfulAutoDemand = !pilotBrakeRequestActive && FinalBrakeDemand >= 0.25f;
            bool analogResponseMissing = wheelBrakeBindings.Count == 0 ||
                (touchdownStableSeconds >= 0.35f && MeasuredDecelerationMps2 < 0.15f);
            brakeFallbackEvidenceSeconds = meaningfulAutoDemand && analogResponseMissing
                ? brakeFallbackEvidenceSeconds + dt : Mathf.Max(0f, brakeFallbackEvidenceSeconds - 2f * dt);
            ApplyWheelBrakeDemand(vessel, wheelBrakeDemand);
            BrakeAssistActive = FinalBrakeDemand > 0.01f || pilotBrakeRequestActive;
            BrakeAssistStatus = touchdownStableSeconds < 0.20f ? "TOUCHDOWN CONFIRM" :
                (GroundStabilityAllowance <= 0.01f ? "STABILITY INHIBIT" :
                (GroundStabilityAllowance < 0.95f ? "STABILITY-LIMITED" :
                (speed < 2f ? "AUTO STOP" : "TRAJECTORY BRAKING")));

            AirbrakeLinkDemand = settings.GroundAirbrakeLinkAuto
                ? Mathf.Clamp01(FinalBrakeDemand) * Mathf.Clamp01(settings.GroundAirbrakeLimit) : 0f;
            if (airbrake != null)
                airbrake.UpdateGroundAssist(vessel, FinalBrakeDemand,
                    settings.GroundAirbrakeLimit, settings.GroundAirbrakeLinkAuto);
            UpdateAuxiliaryDeceleration(vessel, stableContact, speed);
        }


        void EnsureWheelBrakeBindings(Vessel vessel)
        {
            if (vessel == null)
            {
                wheelBrakeBindings.Clear();
                wheelBrakeVesselPersistentId = 0u;
                wheelBrakePartCount = -1;
                wheelBrakeBindingsScanned = false;
                wheelBrakeNextRetryRealtime = 0f;
                return;
            }
            int partCount = vessel.parts == null ? 0 : vessel.parts.Count;
            bool sameIdentity = wheelBrakeVesselPersistentId == vessel.persistentId &&
                wheelBrakePartCount == partCount;
            float now = Time.realtimeSinceStartup;
            if (sameIdentity && wheelBrakeBindingsScanned)
            {
                if (wheelBrakeBindings.Count > 0) return;
                // A legitimate zero-result scan is not the same as "not scanned".
                // Retry at a bounded cadence so destroyed-wheel and wheel-less vessels
                // do not trigger a full part/module walk and identical log line every frame.
                if (now < wheelBrakeNextRetryRealtime) return;
            }

            wheelBrakeBindings.Clear();
            wheelBrakeVesselPersistentId = vessel.persistentId;
            wheelBrakePartCount = partCount;
            wheelBrakeBindingsScanned = true;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (vessel.parts != null)
            {
                foreach (Part part in vessel.parts)
                {
                    if (part == null || part.Modules == null) continue;
                    foreach (PartModule module in part.Modules)
                    {
                        if (module == null || module.GetType().Name.IndexOf("WheelBrakes",
                            StringComparison.OrdinalIgnoreCase) < 0) continue;
                        ModuleWheelBrakes stock = module as ModuleWheelBrakes;
                        Type type = module.GetType();
                        FieldInfo field = stock == null ? type.GetField("brakeInput", flags) : null;
                        PropertyInfo property = stock == null ? type.GetProperty("brakeInput", flags) : null;
                        bool fieldUsable = field != null && (field.FieldType == typeof(float) ||
                            field.FieldType == typeof(double));
                        bool propertyUsable = property != null && property.CanWrite &&
                            (property.PropertyType == typeof(float) || property.PropertyType == typeof(double));
                        if (stock == null && !fieldUsable && !propertyUsable) continue;
                        wheelBrakeBindings.Add(new WheelBrakeBinding
                        {
                            Module = module,
                            StockModule = stock,
                            InputField = fieldUsable ? field : null,
                            InputProperty = propertyUsable ? property : null
                        });
                    }
                }
            }
            wheelBrakeNextRetryRealtime = wheelBrakeBindings.Count == 0 ? now + 2.0f : float.PositiveInfinity;
            bool identityChanged = wheelBrakeLastLoggedVesselPersistentId != vessel.persistentId ||
                wheelBrakeLastLoggedPartCount != partCount;
            if (identityChanged || wheelBrakeLastLoggedCount != wheelBrakeBindings.Count)
            {
                wheelBrakeLastLoggedVesselPersistentId = vessel.persistentId;
                wheelBrakeLastLoggedPartCount = partCount;
                wheelBrakeLastLoggedCount = wheelBrakeBindings.Count;
                AERISLogger.Info("[GROUND_ASSIST][BRAKE] analog wheel-brake bindings=" +
                    wheelBrakeBindings.Count + "; vessel=" + vessel.vesselName +
                    (wheelBrakeBindings.Count == 0 ? "; retry=2.0s." : "."));
            }
        }

        void ApplyWheelBrakeDemand(Vessel vessel, float demand)
        {
            EnsureWheelBrakeBindings(vessel);
            float clamped = Mathf.Clamp01(demand);
            int applied = 0;
            foreach (WheelBrakeBinding binding in wheelBrakeBindings)
            {
                if (binding == null || binding.Module == null) continue;
                try
                {
                    if (binding.StockModule != null)
                        binding.StockModule.brakeInput = clamped;
                    else if (binding.InputField != null)
                    {
                        if (binding.InputField.FieldType == typeof(double))
                            binding.InputField.SetValue(binding.Module, (double)clamped);
                        else binding.InputField.SetValue(binding.Module, clamped);
                    }
                    else if (binding.InputProperty != null)
                    {
                        if (binding.InputProperty.PropertyType == typeof(double))
                            binding.InputProperty.SetValue(binding.Module, (double)clamped, null);
                        else binding.InputProperty.SetValue(binding.Module, clamped, null);
                    }
                    applied++;
                }
                catch (Exception ex)
                {
                    if (!wheelBrakeWriteFailureLogged)
                    {
                        wheelBrakeWriteFailureLogged = true;
                        AERISLogger.Warn("[GROUND_ASSIST][BRAKE] analog wheel-brake write failed; stock Brakes fallback remains available; " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }

            // Stock Brakes is a verified fallback, not a parallel actuator.  Engage it
            // only after analog demand has produced no measurable response for the
            // configured dwell; otherwise preserve continuously modulated braking.
            float fallbackEngage = 0.25f;
            float fallbackRelease = 0.10f;
            float fallbackDelay = settings != null
                ? Mathf.Clamp(settings.GroundBrakeFallbackDelaySeconds, 0.5f, 3f) : 1f;
            bool fallbackEvidenceReady = brakeFallbackEvidenceSeconds >= fallbackDelay;
            bool requireFallback = vessel != null && postTouchdownSessionActive && !ParkingHoldActive &&
                (wheelBrakeActionGroupFallbackActive ? clamped > fallbackRelease :
                    (clamped >= fallbackEngage && fallbackEvidenceReady));
            if (requireFallback)
            {
                if (!wheelBrakeActionGroupFallbackActive)
                {
                    wheelBrakeActionGroupWasOnBeforeFallback = vessel.ActionGroups[KSPActionGroup.Brakes];
                    wheelBrakeActionGroupFallbackActive = true;
                    AERISLogger.Info("[GROUND_ASSIST][BRAKE] stock Brakes automatic fallback ACTIVE; modules=" +
                        wheelBrakeBindings.Count + ".");
                }
                if (!vessel.ActionGroups[KSPActionGroup.Brakes])
                    vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, true);
            }
            else if (wheelBrakeActionGroupFallbackActive)
                ReleaseWheelBrakeActionGroupFallback(vessel, "demand below automatic-brake release threshold");

            WheelBrakeAppliedDemand = applied > 0 || wheelBrakeActionGroupFallbackActive ? clamped : 0f;
        }

        void ReleaseWheelBrakeActionGroupFallback(Vessel vessel, string reason)
        {
            if (!wheelBrakeActionGroupFallbackActive) return;
            if (vessel != null)
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, wheelBrakeActionGroupWasOnBeforeFallback);
            wheelBrakeActionGroupFallbackActive = false;
            wheelBrakeActionGroupWasOnBeforeFallback = false;
            AERISLogger.Info("[GROUND_ASSIST][BRAKE] stock Brakes automatic fallback RELEASED; reason=" + reason + ".");
        }

        void ReleaseWheelBrakeDemand(Vessel vessel)
        {
            ReleaseWheelBrakeActionGroupFallback(vessel, "wheel-brake ownership released");
            if (vessel == null)
            {
                WheelBrakeAppliedDemand = 0f;
                wheelBrakeBindings.Clear();
                wheelBrakeVesselPersistentId = 0u;
                wheelBrakePartCount = -1;
                wheelBrakeBindingsScanned = false;
                wheelBrakeNextRetryRealtime = 0f;
                wheelBrakeWriteFailureLogged = false;
                return;
            }
            float pilotDemand = vessel.ActionGroups[KSPActionGroup.Brakes] ? 1f : 0f;
            ApplyWheelBrakeDemand(vessel, pilotDemand);
            if (pilotDemand <= 0f) WheelBrakeAppliedDemand = 0f;
        }

        void UpdateAuxiliaryDeceleration(Vessel vessel, bool stableContact, float speed)
        {
            UpdateDragChuteAssist(vessel, stableContact, speed);

            reverseThrustDemand = 0f;
            reverseProviderId = AERISGroundAssistBridge.ProviderId;
            if (settings == null || !settings.GroundReverseThrustAuto || vessel == null)
            {
                ReleaseReverseThrustProvider(vessel, "disabled");
                reverseThrustStatus = "OFF";
                return;
            }

            bool eligible = postTouchdownSessionActive && reliableGrounded && stableContact &&
                speed >= 8f && GroundStabilityAllowance >= 0.65f;
            float decelShortfall = Mathf.Max(0f,
                RequestedDecelerationMps2 - Mathf.Max(0f, MeasuredDecelerationMps2));
            reverseThrustDemand = eligible
                ? Mathf.Clamp01(decelShortfall / Mathf.Max(0.5f, settings.GroundTargetDecelerationMps2))
                : 0f;
            var request = new AERISGroundAssistRequest
            {
                TouchdownSessionActive = postTouchdownSessionActive,
                StableGroundContact = stableContact,
                SurfaceSpeedMps = speed,
                TargetDecelerationMps2 = RequestedDecelerationMps2,
                MeasuredDecelerationMps2 = MeasuredDecelerationMps2,
                StabilityAllowance01 = GroundStabilityAllowance,
                ReverseThrustRequested = reverseThrustDemand > 0.01f,
                ReverseThrustDemand01 = reverseThrustDemand
            };
            AERISGroundAssistStatus status;
            if (!AERISGroundAssistBridge.TryApply(vessel, request, out status))
            {
                SetReverseThrustControlActive(false);
                reverseThrustStatus = "AUTO — NO PROVIDER";
                return;
            }
            if (status.AsymmetricReverseThrust)
            {
                request.ReverseThrustRequested = false;
                request.ReverseThrustDemand01 = 0f;
                AERISGroundAssistBridge.TryApply(vessel, request, out status);
                reverseThrustDemand = 0f;
                SetReverseThrustControlActive(false);
                reverseThrustStatus = "INHIBITED — ASYMMETRIC";
                return;
            }
            bool active = status.ReverseThrustAvailable && status.ReverseThrustActive &&
                reverseThrustDemand > 0.01f;
            SetReverseThrustControlActive(active);
            reverseThrustStatus = active ? "ACTIVE — " + (reverseThrustDemand * 100f).ToString("F0") + "%" :
                (status.ReverseThrustAvailable ? "AUTO — STANDBY" : "AUTO — UNAVAILABLE");
            if (!string.IsNullOrEmpty(status.Detail)) reverseThrustStatus += " — " + status.Detail;
        }

        void UpdateDragChuteAssist(Vessel vessel, bool stableContact, float speed)
        {
            if (settings == null || !settings.GroundDragChuteAuto)
            {
                dragChuteStatus = "OFF";
                return;
            }
            if (dragChuteDeploymentAttempted)
            {
                dragChuteStatus = dragChuteDeployedCount > 0
                    ? "DEPLOYED — " + dragChuteDeployedCount
                    : "AUTO — NO BRAKES-GROUP CHUTE";
                return;
            }
            if (!postTouchdownSessionActive || !stableContact || speed < 15f ||
                touchdownStableSeconds < 0.75f)
            {
                dragChuteStatus = "AUTO — ARMED";
                return;
            }
            dragChuteDeploymentAttempted = true;
            dragChuteDeployedCount = TryDeployBrakeGroupParachutes(vessel);
            dragChuteStatus = dragChuteDeployedCount > 0
                ? "DEPLOYED — " + dragChuteDeployedCount
                : "AUTO — NO BRAKES-GROUP CHUTE";
            AERISLogger.Info("[GROUND_ASSIST][DRAG_CHUTE] " + dragChuteStatus + ".");
        }

        static int TryDeployBrakeGroupParachutes(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null) return 0;
            int deployed = 0;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (Part part in vessel.parts)
            {
                if (part == null || part.Modules == null) continue;
                foreach (PartModule module in part.Modules)
                {
                    if (module == null || module.GetType().Name.IndexOf("Parachute",
                        StringComparison.OrdinalIgnoreCase) < 0 || !HasBrakesAction(module)) continue;
                    try
                    {
                        MethodInfo deploy = module.GetType().GetMethod("Deploy", flags, null,
                            Type.EmptyTypes, null);
                        if (deploy == null) continue;
                        deploy.Invoke(module, null);
                        deployed++;
                    }
                    catch { }
                }
            }
            return deployed;
        }

        static bool HasBrakesAction(PartModule module)
        {
            try
            {
                foreach (BaseAction action in module.Actions)
                    if (action != null &&
                        (action.actionGroup & KSPActionGroup.Brakes) == KSPActionGroup.Brakes)
                        return true;
            }
            catch { }
            return false;
        }

        void ReleaseReverseThrustProvider(Vessel vessel, string reason)
        {
            if (reverseThrustControlActive || reverseThrustDemand > 0.001f)
            {
                var request = new AERISGroundAssistRequest
                {
                    TouchdownSessionActive = false,
                    ReverseThrustRequested = false,
                    ReverseThrustDemand01 = 0f
                };
                AERISGroundAssistStatus ignored;
                AERISGroundAssistBridge.TryApply(vessel, request, out ignored);
            }
            reverseThrustDemand = 0f;
            SetReverseThrustControlActive(false);
            if (settings != null && settings.GroundReverseThrustAuto && reason != "disabled")
                reverseThrustStatus = "AUTO — STANDBY";
        }

        void ReleaseParkingHoldByPilot(Vessel vessel)
        {
            ReleaseBrakeAssist(vessel, "pilot vanilla brake toggle", true);
            if (vessel != null) vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, false);
            ReleaseWheelBrakeDemand(vessel);
            ParkingHoldActive = false;
            parkingBrakeWasAppliedBeforeHold = false;
            parkingHoldVesselPersistentId = 0u;
            parkingHoldCaptureFixedTime = 0f;
            parkingHoldPilotReleaseCount++;
            BrakeAssistStatus = "PARKING RELEASED — PILOT BRAKES";
            AERISLogger.Info("[GROUND_ASSIST][BRAKE] PARKING HOLD RELEASED by stock Brakes action; count=" +
                parkingHoldPilotReleaseCount + ".");
        }

        void ReleaseBrakeAssist(Vessel vessel, string reason, bool releaseParkingHold)
        {
            bool wasActive = BrakeAssistActive || FinalBrakeDemand > 0.001f ||
                AirbrakeLinkDemand > 0.001f || ParkingHoldActive;
            BrakeAssistActive = false;
            RequestedDecelerationMps2 = 0f;
            BrakeDemand = 0f;
            FinalBrakeDemand = 0f;
            AirbrakeLinkDemand = 0f;
            GroundStabilityAllowance = 1f;
            brakeDemandState = 0f;
            touchdownStableSeconds = 0f;
            stopStableSeconds = 0f;
            lastBrakeFixedTime = 0f;
            previousBrakeSpeedMps = SurfaceSpeedMps;
            MeasuredDecelerationMps2 = 0f;
            brakeFallbackEvidenceSeconds = 0f;
            pilotBrakeRequestActive = false;
            groundOwnershipBlend = 1f;
            touchdownSessionStartFixedTime = 0f;
            if (airbrake != null && airbrake.GroundAssistActive)
                airbrake.Release("Ground Assist " + reason);
            ReleaseReverseThrustProvider(vessel, reason);
            if (releaseParkingHold && ParkingHoldActive && vessel != null &&
                (parkingHoldVesselPersistentId == 0u || parkingHoldVesselPersistentId == vessel.persistentId))
                vessel.ActionGroups.SetGroup(KSPActionGroup.Brakes, parkingBrakeWasAppliedBeforeHold);
            if (releaseParkingHold)
            {
                ParkingHoldActive = false;
                parkingBrakeWasAppliedBeforeHold = false;
                parkingHoldVesselPersistentId = 0u;
                parkingHoldCaptureFixedTime = 0f;
            }
            if (!ParkingHoldActive) ReleaseWheelBrakeDemand(vessel);
            BrakeAssistStatus = ParkingHoldActive ? "PARKING HOLD" : "STANDBY";
            if (wasActive) AERISLogger.Info("[GROUND_ASSIST][BRAKE] released; reason=" + reason + ".");
        }

        internal void InvalidateActuatorBindings(string reason)
        {
            wheelBrakeBindings.Clear();
            wheelBrakeVesselPersistentId = 0u;
            wheelBrakePartCount = -1;
            wheelBrakeBindingsScanned = false;
            wheelBrakeWriteFailureLogged = false;
            wheelBrakeNextRetryRealtime = 0f;
            WheelBrakeAppliedDemand = 0f;
            brakeFallbackEvidenceSeconds = 0f;
            pilotBrakeRequestActive = false;
            AERISLogger.Info("[GROUND_ASSIST][BRAKE] actuator bindings invalidated; reason=" + reason + ".");
        }

        internal void RecaptureCurrentHeading(VirtualAttitudeInstrument attitude)
        {
            if (attitude == null || !attitude.InstrumentHeadingValid) return;
            targetHeadingDeg = attitude.InstrumentHeadingDeg;
            CurrentHeadingDeg = targetHeadingDeg;
            HeadingErrorDeg = 0f;
            pilotNeutralSeconds = 0f;
            recapturePending = false;
            AERISLogger.Info("[GROUND_STABILITY] heading captured=" + targetHeadingDeg.ToString("F2") + " deg");
        }

        internal void EmergencyRelease(string reason)
        {
            Release(reason);
            reliableGrounded = false;
            liftoffConfirmed = false;
            liftoffCandidateSeconds = 0f;
            liftoffCandidateStartFixedTime = -1f;
            postTouchdownSessionActive = false;
            reverseThrustControlActive = false;
            reverseThrustDemand = 0f;
            ReleaseBrakeAssist(FlightGlobals.ActiveVessel, reason, true);
            initialized = false;
        }

        internal void ReleaseForAirHandoff()
        {
            Release("air handoff");
        }

        void ReadPilotInputs(FlightCtrlState state)
        {
            FlightCtrlState raw = FlightInputHandler.state;
            float yaw = raw != null ? raw.yaw : state.yaw;
            float roll = raw != null ? raw.roll : state.roll;
            PilotYaw = IsFinite(yaw) ? Mathf.Clamp(yaw, -1f, 1f) : 0f;
            PilotRoll = IsFinite(roll) ? Mathf.Clamp(roll, -1f, 1f) : 0f;
        }

        static float ShapeInput(float input, float deadband)
        {
            float magnitude = Mathf.Abs(input);
            if (magnitude <= deadband) return 0f;
            float normalized = (magnitude - deadband) / (1f - deadband);
            return Mathf.Sign(input) * normalized * normalized;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float ManualYawRateLimit(float speed)
        {
            return Mathf.Lerp(18f, 5f, Mathf.Clamp01((speed - 5f) / 65f));
        }

        static float GroundYawRateLimit(float speed)
        {
            return Mathf.Lerp(16f, 4.5f, Mathf.Clamp01((speed - 8f) / 70f));
        }

        static float GroundRollRateLimit(float speed)
        {
            return Mathf.Lerp(3.5f, 1.5f, Mathf.Clamp01((speed - 15f) / 70f));
        }

        void Release(string reason)
        {
            ReleaseLateral(reason);
            ReleaseThrottleCut(reason, true);
            ReleaseBrakeAssist(FlightGlobals.ActiveVessel, reason, true);
            if (!enabled || reason.IndexOf("master", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("emergency", StringComparison.OrdinalIgnoreCase) >= 0 ||
                reason.IndexOf("ground-assist", StringComparison.OrdinalIgnoreCase) >= 0)
                postTouchdownSessionActive = false;
        }

        void ReleaseLateral(string reason)
        {
            ClearYaw();
            ClearRoll();
            ControlActive = false;
            HeadingHoldActive = false;
            PilotSharedControlActive = false;
            LowSpeedTransparent = false;
            YawRateDemandDegPerSec = 0f;
            RollRateDemandDegPerSec = 0f;
            YawAuthorityScale = 0f;
            RollAuthorityScale = 0f;
            lastControlFixedTime = 0f;
            lateralSessionActive = false;
            recapturePending = false;
            Status = !enabled ? "DISABLED" : reason.ToUpperInvariant();
            LogStateEdge(Status);
        }

        void ReleaseThrottleCut(string reason, bool forceBaselineZero)
        {
            if (forceBaselineZero) StandardFlyByWire.SetExternalThrottleReleaseBaseline(0f);
            if (ownsThrottle)
            {
                StandardFlyByWire.ExternalThrottleOverride = false;
                StandardFlyByWire.ExternalThrottleDemand = 0f;
                AERISLogger.Info("[GROUND_ASSIST][THROTTLE] CUT RELEASED; reason=" + reason + ".");
            }
            ownsThrottle = false;
            ThrottleCutActive = false;
            AaNativeThrottleOverrideActive = false;
        }

        void ClearYaw()
        {
            if (ownsYaw)
            {
                StandardFlyByWire.ExternalYawOverride = false;
                StandardFlyByWire.ExternalYawDemand = 0f;
            }
            ownsYaw = false;
            AaNativeYawOverrideActive = false;
        }

        void ClearRoll()
        {
            if (ownsRoll)
            {
                StandardFlyByWire.ExternalRollOverride = false;
                StandardFlyByWire.ExternalRollDemand = 0f;
            }
            ownsRoll = false;
            AaNativeRollOverrideActive = false;
        }

        void LogStateEdge(string state)
        {
            if (state == lastLoggedState) return;
            lastLoggedState = state;
            AERISLogger.Info("[GROUND_STABILITY] state=" + state +
                "; grounded=" + reliableGrounded + "; liftoff=" + liftoffConfirmed);
        }
    }
}
