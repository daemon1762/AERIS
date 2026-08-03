using System;
using System.Collections;
using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.Logging;
using AERISFlightControl.Settings;
using AERISFlightControl.UI;
using AERISFlightControl.Protect;
using AERISFlightControl.Autopilot;
using AERISFlightControl.Integrations;
using AERISFlightControl.API;
using AERISFlightControl.Recording;
using AERISFlightControl.FlightState;
using AERISFlightControl.FlightPlans;
using AERISFlightControl.Landing;
using AERISFlightControl.Terrain;
using AERISFlightControl.Performance;
namespace AERISFlightControl.Core
{
 [KSPAddon(KSPAddon.Startup.MainMenu, true)] public sealed class AERISBootstrap : MonoBehaviour
 {
  static AERISBootstrap instance;
  bool duplicateInstance;
  AERISSettings settings; AERISWindow window; AERISFlightInstrument flightInstrument; ToolbarBridge toolbar; Vessel observed; Vessel inputHookVessel; float lastLog; AERISFlightDataRecorder recorder; string lastState=""; string lastStandby=""; float lastAaThrottleDemand; float nextRecorderFailureLogTime;
	  bool wasFlightScene; GameScenes observedUiScene;
	  float airfieldProviderReadySince=-1f; Vessel airfieldProviderReadyVessel;
	  float nextCanonicalSnapshotTime; double lastPerformanceSnapshotCostMilliseconds=-1.0;
	  long lastRuntimeDatabaseRevision=-1L,lastRuntimeSelectionRevision=-1L;
  bool normalApExecutionPermittedLastFrame; Vessel normalApExecutionVessel; string pendingNormalApActivationSource;
  internal ProtectTelemetry Protect { get; private set; }
  internal GroundStabilityProtection GroundStability { get; private set; }
  internal AERISBankDirector Bank { get; private set; }
  internal AERISHdgDirector Hdg { get; private set; }
  internal AERISFlightPlanLibrary FlightPlans { get; private set; }
  internal AERISAirfieldRegistry Airfields { get; private set; }
  internal AERISMapDramCache MapDramCache { get; private set; }
  internal AERISLandingFoundation Landing { get; private set; }
  internal AERISTerrainAwareness Terrain { get; private set; }
  internal AERISCurrentBodyResidentCache CurrentBodyResidentCache { get { return Terrain==null?null:Terrain.CurrentBodyResidentCache; } }
  internal AERISPitchDirector Pitch { get; private set; }
  internal AERISVerticalSpeedDirector VerticalSpeed { get; private set; }
  internal AERISAltitudeDirector Altitude { get; private set; }
  internal AERISAccelerationDirector Acceleration { get; private set; }
  internal AERISSpeedAirbrakeController SpeedAirbrake { get; private set; }
  internal AERISVelocityDirector Velocity { get; private set; }
  internal AERISAxisStabilitySupervisor AxisSupervisor { get; private set; }
  internal AERISAutoTakeoffDirector AutoTakeoff { get; private set; }
  internal VirtualAttitudeInstrument Attitude { get; private set; }
  internal AERISSettings Settings { get { return settings; } }
  internal AERISFlightDataRecorder Recorder { get { return recorder; } }
	  internal AERISExternalAutomationManager ExternalAutomation { get; private set; }
	  internal AERISPerformanceRuntime Performance { get; private set; }
  void Awake(){
   if(instance!=null && instance!=this){ duplicateInstance=true; Debug.LogWarning("[AERIS] Duplicate persistent bootstrap rejected."); Destroy(gameObject); return; }
   instance=this; DontDestroyOnLoad(gameObject);
  }
	  void Start(){if(duplicateInstance)return; settings=AERISSettings.Load(); Performance=new AERISPerformanceRuntime(settings.PerformanceWorkerOverride,!settings.PerformanceGpuAccelerationEnabled); Protect=new ProtectTelemetry(); SpeedAirbrake=new AERISSpeedAirbrakeController(); SpeedAirbrake.Enabled=settings.SpeedAutomaticAirbrakeEnabled; GroundStability=new GroundStabilityProtection(settings,SpeedAirbrake); Bank=new AERISBankDirector(); Hdg=new AERISHdgDirector(); Hdg.ThinAirTurnAssistEnabled=settings.ThinAirTurnAssistEnabled; FlightPlans=new AERISFlightPlanLibrary(settings); MapDramCache=new AERISMapDramCache(); Airfields=new AERISAirfieldRegistry(settings,MapDramCache); Landing=new AERISLandingFoundation(Airfields); AERISRunwayTrackTokenApi.Bind(()=>{AERISRunwayTrackToken token;return Landing!=null&&Landing.TryCreateTrackToken(out token)?token:null;}); Terrain=new AERISTerrainAwareness(settings,MapDramCache); Pitch=new AERISPitchDirector(); VerticalSpeed=new AERISVerticalSpeedDirector(); Altitude=new AERISAltitudeDirector(); Acceleration=new AERISAccelerationDirector(); Velocity=new AERISVelocityDirector(); Velocity.SetAccelerationLimit(settings.VelocityAccelerationLimitMps2); AxisSupervisor=new AERISAxisStabilitySupervisor(); AutoTakeoff=new AERISAutoTakeoffDirector(settings); Attitude=new VirtualAttitudeInstrument(); AERISFlightStateApi.Publish(Attitude); ExternalAutomation=new AERISExternalAutomationManager(this); AERISExternalAutomationApi.Bind(ExternalAutomation); window=new AERISWindow(settings,this); flightInstrument=new AERISFlightInstrument(settings,this); ToolbarBridge.RegisterModOnce(); toolbar=gameObject.AddComponent<ToolbarBridge>(); toolbar.Initialise(()=>{if(window!=null)window.ShowForCurrentScene();},()=>{if(window!=null)window.HideForCurrentScene();}); AERISLogger.Initialize(); if(FlightPlans!=null){FlightPlans.Reload();Performance.FlightPlanChanged();} RestoreAutopilotInputValues(); recorder=new AERISFlightDataRecorder(); AERISLogger.EventSink=(level,message)=>{ if(recorder!=null) recorder.RecordCvr("AERIS",level,message); }; AERISFlightRecorderApi.EventSink=(provider,category,action,severity,message)=>{if(recorder!=null)recorder.RecordExtensionEvent(provider,category,action,severity,message);}; AERISFlightRecorderApi.SchemaSink=(schema)=>recorder!=null && recorder.RegisterTelemetrySchema(schema); AERISFlightRecorderApi.TelemetrySink=(frame)=>{if(recorder!=null)recorder.RecordExtensionTelemetry(frame);}; FlightModel.BackgroundTrainingEnabled = settings.EnableAABackgroundTraining; observedUiScene=HighLogic.LoadedScene; wasFlightScene=HighLogic.LoadedSceneIsFlight; if(wasFlightScene)NotifyRuntimeVessel(FlightGlobals.ActiveVessel);
  AERISLogger.Info(""+AERISBuildVersion.Display+" loaded. Legacy NAV remains removed. Independent LAND remains observation-only. The CP2.5 Map DRAM Cache holds immutable airport/runway/ILS and Terrain Tile/LOD metadata snapshots for normal lookup without synchronous SSD access; CP3 Gate 3.1 makes the real rotated ND viewport authoritative, admits every Global/Far foundation tile before refinement, limits current-body background population to the sole persistent Global/Far base, and treats Route/Local as future reconstructed quality with only an existing exact-SSD bridge and demand-gated LAND microtiles. Gate 3 predictive corridor warming is constrained to Far base payloads. Gate 4B Geometry Integrity Hotfix 2 keeps temporal GUI-matrix reprojection quarantined after field rejection; exact current-projection GPU FAR rendering is the presentation authority while ATTR is redesigned. The CP2 ND retains asynchronous shared-scheduler reads, progressive Terrain Block fallback, bounded hot/warm/disk/VRAM caches, and GPU TOPO/REL rendering; terrain and LAND remain control-free."); StandardFlyByWire.ExternalThrottleFloor=ApplyAERISThrottleFloor; StandardFlyByWire.ExternalThrottleCeiling=ApplyAERISThrottleCeiling; StandardFlyByWire.ExternalPropulsionDemand=PublishAAPropulsionDemand; ClearExternalControlDemands(); GameEvents.onVesselChange.Add(OnVesselChange); GameEvents.onVesselGoOffRails.Add(OnVesselGoOffRails); GameEvents.onVesselWasModified.Add(OnVesselWasModified); AttachVirtualPilotHook(FlightGlobals.ActiveVessel); StartCoroutine(DetectAddonsAfterStartup());}
  IEnumerator DetectAddonsAfterStartup(){ yield return new WaitForSeconds(2f); AddonIntegration.DetectAtStartup(); AddonIntegration.RefreshForVessel(FlightGlobals.ActiveVessel); if(Airfields!=null)Airfields.RequestStartupLoad(); AERISLogger.Info("[ADDONS] Startup detection complete. APP="+AddonIntegration.AppStatusText+". One airfield load requested; execution waits for stable unpacked flight providers."); }
  void OnVesselGoOffRails(Vessel v){ if(v!=null && v==FlightGlobals.ActiveVessel){ AddonIntegration.RefreshForVessel(v); AERISLogger.Info("[ADDONS] Vessel compatibility refreshed: go-off-rails."); } }
  void OnVesselWasModified(Vessel v){ if(v!=null && v==FlightGlobals.ActiveVessel){ if(Performance!=null){Performance.ControlPointChanged();Performance.DockingChanged();} if(SpeedAirbrake!=null)SpeedAirbrake.Release("vessel modified"); if(GroundStability!=null)GroundStability.InvalidateActuatorBindings("vessel modified"); AddonIntegration.RefreshForVessel(v); AERISLogger.Info("[ADDONS] Vessel compatibility refreshed: vessel modified."); } }
  void OnVesselChange(Vessel v){ NotifyRuntimeVessel(v); ResetAutomationState("active vessel change",true); AttachVirtualPilotHook(v); SafeRecorderStep(()=>{if(recorder!=null)recorder.BeginFlight(v);},"begin flight after vessel change"); AddonIntegration.RefreshForVessel(v); AERISLogger.Info("Active vessel changed with all ARM states reset: "+(v==null?"<none>":v.vesselName)); observed=v; DisableTrimLearning(); }

  void HandleUiSceneBoundary(GameScenes nextScene){
   GameScenes previousScene=observedUiScene;
   if(window!=null)window.OnSceneBoundary();
   if(toolbar!=null)toolbar.InvalidateSceneBinding();
   observedUiScene=nextScene;
   AERISLogger.Info("[TOOLBAR] UI scene boundary "+previousScene+" -> "+nextScene+
      ": previous window state closed and launcher binding invalidated.");
  }

  void HandleFlightSceneBoundary(bool enteringFlight){
   string reason=enteringFlight?"entering flight scene":"leaving flight scene";
   if(Performance!=null)Performance.SceneChanged();
   ResetAutomationState(reason,true);
   if(enteringFlight){observed=FlightGlobals.ActiveVessel;NotifyRuntimeVessel(observed);AttachVirtualPilotHook(observed);SafeRecorderStep(()=>{if(recorder!=null)recorder.BeginFlight(observed);},"begin flight after scene entry");AddonIntegration.RefreshForVessel(observed);}
   else{SafeRecorderStep(()=>{if(recorder!=null)recorder.EndFlight("scene-transition");},"end flight at scene exit");observed=null;}
   AERISLogger.Info("[SCENE_RESET] "+reason+": MASTER, normal AP, Auto Takeoff and live protection ownership reset; accepted AP entries retained.");
  }

  void ResetAutomationState(string reason,bool detachInputHook){
   SafeResetStep(()=>{if(ExternalAutomation!=null)ExternalAutomation.HandleCoreReset(reason);},"external automation",reason);
   SafeResetStep(()=>{if(Landing!=null)Landing.ResetForSceneTransition(reason);},"LAND foundation",reason);
   SafeResetStep(()=>{if(Terrain!=null)Terrain.Reset(reason);},"terrain awareness",reason);
   Vessel releaseVessel=observed!=null?observed:FlightGlobals.ActiveVessel;
   SafeResetStep(()=>{if(settings!=null)settings.Save();},"settings save",reason);
   SafeResetStep(()=>{if(AutoTakeoff!=null)AutoTakeoff.EmergencyRelease(releaseVessel,reason);},"auto takeoff",reason);
   SafeResetStep(()=>{if(GroundStability!=null)GroundStability.EmergencyRelease(reason);},"ground stability",reason);
   SafeResetStep(()=>{if(Altitude!=null)Altitude.Disable(reason,VerticalSpeed);},"ALT",reason);
   SafeResetStep(()=>{if(Velocity!=null)Velocity.Disable(reason,Acceleration);},"VEL",reason);
   SafeResetStep(()=>{if(SpeedAirbrake!=null)SpeedAirbrake.Release(reason);},"speed airbrake",reason);
   SafeResetStep(()=>{if(Acceleration!=null)Acceleration.Disable(reason);},"ACC",reason);
   SafeResetStep(()=>{if(VerticalSpeed!=null)VerticalSpeed.Disable(reason);},"V/S",reason);
   SafeResetStep(()=>{if(Pitch!=null)Pitch.Disable(reason);},"PITCH",reason);
   SafeResetStep(()=>{if(Hdg!=null)Hdg.SetArmed(false,releaseVessel,Bank,Attitude);},"HDG",reason);
   SafeResetStep(()=>{if(Bank!=null)Bank.Disable(reason);},"BANK",reason);
   SafeResetStep(()=>{if(Protect!=null)Protect.ResetForSceneTransition(reason);},"Protect",reason);
   normalApExecutionPermittedLastFrame=false;normalApExecutionVessel=null;pendingNormalApActivationSource=null;NormalApExecutionPermitted=false;
   if(window!=null)window.ResetArmUiState();
   SafeResetStep(()=>{var m=Manager;if(m!=null&&m.Active)m.SetMaster(false);},"AA MASTER",reason);
   SafeResetStep(()=>{AERISPropulsionDemandBus.Clear(releaseVessel);if(releaseVessel!=FlightGlobals.ActiveVessel)AERISPropulsionDemandBus.Clear(FlightGlobals.ActiveVessel);},"propulsion demand bus",reason);
   lastAaThrottleDemand=0f;
   if(detachInputHook)DetachVirtualPilotHook();else ClearExternalControlDemands();
  }

  static void SafeResetStep(Action action,string step,string reason){
   try{if(action!=null)action();}
   catch(Exception ex){AERISLogger.Error("[SCENE_RESET] step="+step+" failed during "+reason+": "+ex.GetType().Name+" — "+ex.Message);}
  }

  void SafeRecorderStep(Action action,string operation){
   try{if(action!=null)action();}
   catch(Exception ex){
    float now=Time.realtimeSinceStartup;
    if(now>=nextRecorderFailureLogTime){
     nextRecorderFailureLogTime=now+5f;
     AERISLogger.Error("[FDR] "+operation+" failed and was isolated from flight control: "+ex.GetType().Name+" — "+ex.Message);
    }
   }
  }

  void ClearExternalControlDemands(){
   StandardFlyByWire.ExternalThrottleOverride=false;StandardFlyByWire.ExternalThrottleDemand=0f;
   StandardFlyByWire.ExternalRollOverride=false;StandardFlyByWire.ExternalRollDemand=0f;
   StandardFlyByWire.ExternalPitchOverride=false;StandardFlyByWire.ExternalPitchDemand=0f;
   StandardFlyByWire.ExternalGroundPitchModerationBypass=false;
   StandardFlyByWire.ExternalYawOverride=false;StandardFlyByWire.ExternalYawDemand=0f;
   StandardFlyByWire.ExternalTrimFeedForwardActive=false;
   StandardFlyByWire.ExternalTrimRollInput=0f;StandardFlyByWire.ExternalTrimPitchInput=0f;StandardFlyByWire.ExternalTrimYawInput=0f;
   StandardFlyByWire.ExternalTrimRollRateRadPerSec=0f;StandardFlyByWire.ExternalTrimPitchRateRadPerSec=0f;StandardFlyByWire.ExternalTrimYawRateRadPerSec=0f;
  }
  void AttachVirtualPilotHook(Vessel v){
   if(inputHookVessel==v) return;
   DetachVirtualPilotHook();
   if(v!=null){ v.OnAutopilotUpdate += OnAutopilotInput; inputHookVessel=v; AERISLogger.Info("[BANK_RATE] input hook attached before AA activation."); }
  }
  void DetachVirtualPilotHook(){
   // The external roll setpoint is static in AA. Never allow it to follow a vessel after
   // AERIS has detached from that vessel's autopilot callback.
   ClearExternalControlDemands();
   if(inputHookVessel!=null){ try{ inputHookVessel.OnAutopilotUpdate -= OnAutopilotInput; }catch{} inputHookVessel=null; }
  }
  void OnAutopilotInput(FlightCtrlState state){
   if(Bank==null || state==null) return;
   var m=Manager;
   var vessel=FlightGlobals.ActiveVessel;
   bool standard=m!=null && m.IsStandardActive;
   if(GroundStability!=null) GroundStability.UpdateGroundState(vessel,Attitude);
   // HANDOFF is completed at the start of the frame after it was requested.  The
   // previous frame therefore reaches AA with the final Auto Takeoff demands intact;
   // this frame releases them first and immediately publishes the prepared normal AP.
   if(AutoTakeoff!=null && AutoTakeoff.HandoffRequested) CompleteAutoTakeoffHandoff(vessel);

   // Ground Stability and Auto Takeoff may own the live outputs, but they never erase
   // the pilot's prepared normal-AP plan.  Every normal director remains armed in
   // standby until the same reliable liftoff detector used by Ground Stability has
   // confirmed flight.  Auto Takeoff keeps normal execution suspended through HANDOFF.
   bool rawAirborne=vessel!=null && !vessel.LandedOrSplashed &&
      vessel.situation!=Vessel.Situations.PRELAUNCH && vessel.situation!=Vessel.Situations.SPLASHED;
   bool liftoffConfirmed=GroundStability!=null ? GroundStability.LiftoffConfirmed : rawAirborne;
   bool autoOwnsSequence=AutoTakeoff!=null && AutoTakeoff.Executing;
   bool normalApExecutionPermitted=Master && standard && liftoffConfirmed && !autoOwnsSequence;
   if(normalApExecutionVessel!=vessel){normalApExecutionVessel=vessel;normalApExecutionPermittedLastFrame=false;}
   if(normalApExecutionPermitted && !normalApExecutionPermittedLastFrame){
      string source=string.IsNullOrEmpty(pendingNormalApActivationSource)?"confirmed manual liftoff":pendingNormalApActivationSource;
      pendingNormalApActivationSource=null;
      PrepareNormalApPostTakeoffActivation(source);
   }
   normalApExecutionPermittedLastFrame=normalApExecutionPermitted;
   NormalApExecutionPermitted=normalApExecutionPermitted;

   if(Hdg!=null){ Hdg.ThinAirTurnAssistEnabled=settings!=null&&settings.ThinAirTurnAssistEnabled; Hdg.Update(vessel, Attitude, Bank, Protect, Master, normalApExecutionPermitted); }
   if(Hdg!=null) Hdg.ApplyAaNativeYawRateDemand(state, vessel, Master, normalApExecutionPermitted);
   if(Pitch!=null) Pitch.SetLateralTurnAssistRate(Hdg!=null?Hdg.ThinAirTurnPitchAssistRateDegPerSec:0f,Hdg!=null&&Hdg.ThinAirTurnAssistActive);
   if(Altitude!=null) Altitude.Update(vessel, Attitude, VerticalSpeed, Master, normalApExecutionPermitted);
   if(VerticalSpeed!=null) VerticalSpeed.Update(vessel, Attitude, Pitch, Master, normalApExecutionPermitted);
   if(Pitch!=null) Pitch.ApplyAaNativePitchRateDemand(state, vessel, Attitude, Master, normalApExecutionPermitted);
   if(Acceleration!=null){
    // Do not allow AA's legacy speed PID and AERIS ACC/VEL to own throttle simultaneously.
    // VEL only plans acceleration; ACC owns the observed throttle law and AA still writes final FlightCtrlState.
    if(Acceleration.Armed && m!=null && m.ThrustController!=null && m.ThrustController.spd_control_enabled){
     m.ThrustController.spd_control_enabled=false;
     AERISLogger.Info("[SPEED] AA legacy speed controller released; AERIS ACC/VEL owns throttle demand.");
    }
    if(Velocity!=null) Velocity.Update(vessel, Attitude, Acceleration, Master, normalApExecutionPermitted);
    Acceleration.Update(vessel, Attitude, Master, normalApExecutionPermitted);
    if(SpeedAirbrake!=null){SpeedAirbrake.Enabled=settings!=null&&settings.SpeedAutomaticAirbrakeEnabled;if(GroundStability==null||(!GroundStability.PostTouchdownSessionActive&&!GroundStability.BrakeAssistActive&&!GroundStability.ParkingHoldActive))SpeedAirbrake.Update(vessel,Acceleration,Attitude,Protect,Master,normalApExecutionPermitted);}
    if(Protect!=null)Protect.SetSpeedDecelerationContext(Acceleration.Armed&&Acceleration.ControlActive&&Acceleration.TargetAccelerationMps2< -0.10f,Acceleration.TargetAccelerationMps2,Acceleration.ThrottleDemand,Acceleration.AirbrakeDemand);
    Acceleration.ApplyAaNativeThrottleDemand(state, vessel, Attitude, Master, normalApExecutionPermitted);
   }
   Bank.ApplyAaNativeRollRateDemand(state, vessel, Master, normalApExecutionPermitted);
   if(ExternalAutomation!=null) ExternalAutomation.ApplyV2ControlDemands(state);
   // Normal directors have now either published or released their axes. Auto Takeoff
   // and Ground Stability publish last, but still before AA StandardFlyByWire consumes
   // the native demands and writes the final FlightCtrlState.
   if(AutoTakeoff!=null) AutoTakeoff.ApplyAaNativeTakeoffDemand(state,vessel,Attitude,m,GroundStability,Protect,Master,standard);
   if(GroundStability!=null) GroundStability.ApplyAaNativeGroundDemand(state,vessel,Attitude,Master,standard,
      AutoTakeoff!=null && AutoTakeoff.RequiresLateralAssist);
   if(AxisSupervisor!=null) AxisSupervisor.Update(Bank, Pitch, Hdg, Attitude);
   if(ExternalAutomation!=null) ExternalAutomation.CaptureFinalControlTelemetry();
   if(recorder!=null){
    SafeRecorderStep(()=>recorder.SampleBankDiagnostics(FlightGlobals.ActiveVessel, Bank, Attitude),"BANK diagnostics");
    SafeRecorderStep(()=>recorder.SampleHeadingDiagnostics(FlightGlobals.ActiveVessel, Hdg, Bank, Attitude),"HDG diagnostics");
    SafeRecorderStep(()=>recorder.SampleApSmoothness(FlightGlobals.ActiveVessel, Bank, Pitch, Hdg, Altitude, Acceleration, Velocity, Attitude, Protect, AxisSupervisor, state),"AP smoothness diagnostics");
    SafeRecorderStep(()=>recorder.SamplePitchDiagnostics(FlightGlobals.ActiveVessel, Pitch),"PITCH diagnostics");
    SafeRecorderStep(()=>recorder.SampleVerticalSpeedDiagnostics(FlightGlobals.ActiveVessel, VerticalSpeed, Pitch),"V/S diagnostics");
    SafeRecorderStep(()=>recorder.SampleAltitudeDiagnostics(FlightGlobals.ActiveVessel, Altitude, VerticalSpeed, Pitch, Attitude),"ALT diagnostics");
    SafeRecorderStep(()=>recorder.SampleAccelerationDiagnostics(FlightGlobals.ActiveVessel, Acceleration, SpeedAirbrake, Attitude),"ACC diagnostics");
    SafeRecorderStep(()=>recorder.SampleVelocityDiagnostics(FlightGlobals.ActiveVessel, Velocity, Acceleration, Attitude),"VEL diagnostics");
    SafeRecorderStep(()=>recorder.SampleGroundTakeoffDiagnostics(vessel,GroundStability,AutoTakeoff,Attitude,state),"ground/takeoff diagnostics");
   }
  }
  // AERIS-owned learning and profile persistence were removed in v0.2.23.
  // AA's internal runtime adaptive controller remains part of normal FBW operation,
  // but AERIS never arms or exposes persistent trim learning.
  void DisableTrimLearning(){ var m=Manager; if(m==null)return; if(m.PitchController!=null)m.PitchController.ApplyTrim=false; if(m.RollController!=null)m.RollController.ApplyTrim=false; if(m.YawController!=null)m.YawController.ApplyTrim=false; }

  internal bool ArmAutoTakeoff(out string error){
   error="Auto Takeoff unavailable";
   var vessel=FlightGlobals.ActiveVessel;
   // Revert/quickload/destruction may replace the vessel before the normal Update sensor pass.
   // Refresh the sensor and add-on compatibility synchronously at the ARM boundary.
   if(Attitude!=null)Attitude.Update(vessel);
   AddonIntegration.RefreshForVessel(vessel);
   if(GroundStability!=null)GroundStability.UpdateGroundState(vessel,Attitude);
   return AutoTakeoff!=null && AutoTakeoff.TryArm(vessel,Attitude,Manager,GroundStability,Master,out error);
  }
  internal bool ExecuteAutoTakeoff(out string error){
   error="Auto Takeoff unavailable";
   var v=FlightGlobals.ActiveVessel;
   bool ok=AutoTakeoff!=null && AutoTakeoff.TryExecute(v,GroundStability,Master,out error);
   if(!ok)return false;
   normalApExecutionPermittedLastFrame=false;
   NormalApExecutionPermitted=false;
   AERISLogger.Info("[AUTO_TAKEOFF] normal AP reservation preserved during takeoff: "+ArmedApModesText+".");
   return true;
  }
  internal void DisarmAutoTakeoff(string reason){if(AutoTakeoff!=null)AutoTakeoff.Disarm(FlightGlobals.ActiveVessel,reason);}
  internal bool SetNavigationArm(bool armed,out string error){
   error="NAV RESET — CONTROL UNAVAILABLE; INDEPENDENT LAND IS SEPARATE";
   if(armed)AERISLogger.Warn("[AUTOFLIGHT] navigation ARM rejected: legacy NAV remains removed.");
   return false;
  }
  internal bool ArmLanding(out string error){
   error="LAND ARM unavailable";
   if(Landing==null)return false;
   return Landing.TryArm(FlightGlobals.ActiveVessel,out error);
  }
  internal void DisarmLanding(string reason){
   if(Landing!=null)Landing.Disarm(reason);
  }
  void CompleteAutoTakeoffHandoff(Vessel vessel){
   if(AutoTakeoff==null || !AutoTakeoff.HandoffRequested)return;
   if(GroundStability!=null)GroundStability.ReleaseForAirHandoff();
   bool usedPreparedPlan=AnyNormalApArmed;
   AutoTakeoff.CompleteHandoff(vessel);
   normalApExecutionPermittedLastFrame=false;
   pendingNormalApActivationSource=usedPreparedPlan?"successful Auto Takeoff handoff":null;
   if(usedPreparedPlan){
    AERISLogger.Info("[AUTO_TAKEOFF] HANDOFF complete: released prepared AP plan "+ArmedApModesText+".");
   }else{
    // No normal AP mode was reserved before EXECUTE.  Auto Takeoff owns pitch,
    // throttle and lateral ground assistance only for the takeoff sequence; it must
    // not silently arm fallback HDG/PITCH/V/S modes at handoff.  Leaving every
    // director disarmed returns roll, pitch and yaw pilot authority immediately.
    ClearExternalControlDemands();
    AERISLogger.Info("[AUTO_TAKEOFF] HANDOFF complete: no prepared AP plan; all attitude axes returned to manual control.");
   }
  }

  void PrepareNormalApPostTakeoffActivation(string source){
   var vessel=FlightGlobals.ActiveVessel;
   if(VerticalSpeed!=null && VerticalSpeed.Armed)
      VerticalSpeed.PreparePostTakeoffActivation(vessel,Attitude,Pitch,source);
   if(Altitude!=null && Altitude.Armed)
      Altitude.PreparePostTakeoffActivation(vessel,Attitude,source);
   if(Acceleration!=null && Acceleration.Armed)
      Acceleration.PreparePostTakeoffActivation(StandardFlyByWire.LastFinalThrottle,source);
   AERISLogger.Info("[GROUND_ARM_AP] execution released after "+source+"; modes="+ArmedApModesText+".");
  }

  internal bool NormalApExecutionPermitted { get; private set; }
  internal bool AnyNormalApArmed { get { return
   (Bank!=null&&Bank.Armed)||(Hdg!=null&&Hdg.Armed)||(Pitch!=null&&Pitch.Armed)||
   (VerticalSpeed!=null&&VerticalSpeed.Armed)||(Altitude!=null&&Altitude.Armed)||
   (Acceleration!=null&&Acceleration.Armed)||(Velocity!=null&&Velocity.Armed); } }
  internal string ArmedApModesText { get {
   string s="";
   AppendMode(ref s,"BANK",Bank!=null&&Bank.Armed); AppendMode(ref s,"HDG",Hdg!=null&&Hdg.Armed);
   AppendMode(ref s,"PITCH",Pitch!=null&&Pitch.Armed); AppendMode(ref s,"V/S",VerticalSpeed!=null&&VerticalSpeed.Armed);
   AppendMode(ref s,"ALT",Altitude!=null&&Altitude.Armed); AppendMode(ref s,"ACC",Acceleration!=null&&Acceleration.Armed);
   AppendMode(ref s,"VEL",Velocity!=null&&Velocity.Armed); return s.Length>0?s:"NONE";
  } }
  static void AppendMode(ref string text,string mode,bool armed){if(!armed)return;if(text.Length>0)text+=" + ";text+=mode;}

  void RestoreAutopilotInputValues(){
   if(settings==null)return;
   string error;
   if(Bank!=null)Bank.TrySetTarget(ApText(settings.ApBankTargetDeg),out error);
   if(Hdg!=null){
    Hdg.TrySetTarget(ApText(settings.ApHeadingTargetDeg),out error);
    Hdg.TrySetManualMaxBankLimit(ApText(settings.ApHeadingManualBankLimitDeg),out error);
    if(settings.ApHeadingAutoBankLimit)Hdg.SetAutoMaxBankLimit();
   }
   if(Pitch!=null)Pitch.TrySetTarget(ApText(settings.ApPitchTargetDeg),out error);
   if(VerticalSpeed!=null){VerticalSpeed.TrySetTarget(ApText(settings.ApVerticalSpeedTargetMps),out error);VerticalSpeed.TrySetMaxPitchTarget(ApText(settings.ApVerticalSpeedMaxPitchDeg),out error);}
   if(Altitude!=null){Altitude.TrySetTarget(ApText(settings.ApAltitudeTargetMeters),out error);Altitude.TrySetMaxVerticalSpeed(ApText(settings.ApAltitudeMaxVerticalSpeedMps),out error);Altitude.TrySetMaxPitch(ApText(settings.ApAltitudeMaxPitchDeg),out error);}
   if(Acceleration!=null)Acceleration.TrySetTarget(ApText(settings.ApAccelerationTargetMps2),out error);
   if(Velocity!=null){Velocity.SetAccelerationLimit(settings.VelocityAccelerationLimitMps2);if(settings.ApVelocityTargetConfirmed)Velocity.TrySetTarget(ApText(settings.ApVelocityTargetMps),out error);}
   AERISLogger.Info("[AP_INPUT] Restored accepted BANK/HDG/PITCH/V/S/ALT/ACC/VEL entries; ARM state intentionally remains OFF.");
  }
  static string ApText(float value){return value.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture);}

  internal void ResetAllFbwValues(){
   var m=Manager;
   if(m==null || m.Standard==null || m.PitchController==null || m.RollController==null || m.YawController==null){AERISLogger.Warn("Reset All FBW Values ignored: AA FBW controllers are unavailable."); return;}
   FbwDefaults.Restore(m);
   AERISLogger.Info("FBW values reset to AERIS/AA mod defaults. AERIS learning is not present.");
  }
  // Factory defaults taken from the bundled AA fixed-wing controller field initializers/constructors.
  // This intentionally does not depend on values captured after a craft, save, or other mod changed them.
  static class FbwDefaults{
   internal static void Restore(TopModuleManager m){
    var s=m.Standard; var p=m.PitchController; var r=m.RollController; var y=m.YawController;
    s.moderation_switch=true; s.coord_turn=false;
    p.ApplyTrim=false; p.moderate_aoa=true; p.moderate_g=true; p.max_aoa=15.0f; p.max_g_force=15.0f; p.max_v_construction=0.7f;
    r.wing_leveler=true; r.leveler_snap_angle=3.0f; r.snapping_Kp=0.25f; r.max_v_construction=3.0f;
    y.moderate_aoa=true; y.moderate_g=true; y.max_v_construction=0.7f;
    p.user_input_deriv_clamp=5.0f; r.user_input_deriv_clamp=5.0f; y.user_input_deriv_clamp=5.0f;
    p.relaxation_Kp=0.5f; r.relaxation_Kp=0.5f; y.relaxation_Kp=0.5f;
   }
  }
  // MASTER is an edge-triggered state transition.  IMGUI may execute more than one pass
  // for a single pointer action, so duplicate requests must never re-run AA/AP teardown.
  internal bool Master {
   get { var m=Manager; return m!=null && m.Active; }
   set {
    var m=Manager;
    if(m==null){AERISLogger.Warn("MASTER request ignored: AA core manager unavailable for active vessel."); return;}
    bool current=m.Active;
    if(current==value) return;
    if(!value && Bank!=null) Bank.Disable("AERIS MASTER OFF");
    if(!value && Altitude!=null) Altitude.Disable("AERIS MASTER OFF", VerticalSpeed);
    if(!value && Pitch!=null) Pitch.Disable("AERIS MASTER OFF");
    if(!value && VerticalSpeed!=null) VerticalSpeed.Disable("AERIS MASTER OFF");
    if(!value && Acceleration!=null) Acceleration.Disable("AERIS MASTER OFF");
    if(!value && Velocity!=null) Velocity.Disable("AERIS MASTER OFF",Acceleration);
    if(!value && Hdg!=null) Hdg.SetArmed(false,FlightGlobals.ActiveVessel,Bank,Attitude);
    if(!value && AutoTakeoff!=null) AutoTakeoff.EmergencyRelease(FlightGlobals.ActiveVessel,"MASTER OFF / emergency release");
    if(!value && GroundStability!=null) GroundStability.EmergencyRelease("MASTER OFF / emergency release");
    if(!value){normalApExecutionPermittedLastFrame=false;normalApExecutionVessel=null;pendingNormalApActivationSource=null;NormalApExecutionPermitted=false;if(window!=null)window.ResetArmUiState();}
    m.SetMaster(value);
    AERISLogger.Info("MASTER "+(value?"ON":"OFF")+" applied: AA StandardFlyByWire "+(value?"activated":"deactivated")+".");
   }
  }
  internal TopModuleManager Manager { get { var aa=AtmosphereAutopilot.AtmosphereAutopilot.Instance; var v=FlightGlobals.ActiveVessel; if(aa==null||v==null)return null; var map=aa.getVesselModules(v); if(map==null)return null; AutopilotModule mod; return map.TryGetValue(typeof(TopModuleManager),out mod)?mod as TopModuleManager:null; } }
  internal string CoreState { get { var aa=AtmosphereAutopilot.AtmosphereAutopilot.Instance; var m=Manager; if(!HighLogic.LoadedSceneIsFlight)return "Flight scene required"; if(aa==null)return "AA core host starting"; if(m==null)return "Awaiting vessel manager"; return m.Active ? (m.IsStandardActive?"ACTIVE — AA owns FlightCtrlState":"ARMING") : "READY"; } }
  internal bool MasterAmber { get { return Master && GroundStability!=null && GroundStability.DisplayAmber; } }
  internal bool IsFbwStandby(out string reason)
  {
   reason=null;
   var v=FlightGlobals.ActiveVessel; var m=Manager;
   if(v==null){reason="NO VESSEL"; return true;}
   if(m==null){reason="INITIALIZING"; return true;}
   if(!m.Active) return false;
   if(!m.IsStandardActive){reason="ARMING"; return true;}
   if(v.packed){reason="ON RAILS"; return true;}
   if(v.situation==Vessel.Situations.SPLASHED){reason="WATER"; return true;}
   if(v.LandedOrSplashed || v.situation==Vessel.Situations.PRELAUNCH){
    if(GroundStability!=null && GroundStability.Enabled && GroundStability.Available && GroundStability.ReliableGrounded)return false;
    reason="GROUND PROTECTION UNAVAILABLE"; return true;
   }
   if(GroundStability!=null && GroundStability.Enabled &&
      !GroundStability.LiftoffConfirmed){reason="LIFTOFF UNCONFIRMED"; return true;}
   if(v.staticPressurekPa < 0.1073d){reason="LOW PRESSURE"; return true;}
   return false;
  }
  internal void ResetAerisSettingsFromUi(){
   if(settings==null) return;
   settings.ResetToDefaults();
   FlightModel.BackgroundTrainingEnabled=settings.EnableAABackgroundTraining;
   if(Protect!=null) Protect.ExternalPropulsionIntegrationEnabled=settings.AppIntegrationEnabled;
   if(GroundStability!=null) GroundStability.Enabled=settings.GroundStabilityEnabled;
   RestoreAutopilotInputValues();
   if(FlightPlans!=null){FlightPlans.Reload();if(Performance!=null)Performance.FlightPlanChanged();}
   if(Landing!=null)Landing.Disarm("settings reset");
   if(Airfields!=null)Airfields.Reload();
   settings.Save();
   AERISLogger.Info("[SYSTEM/OPTIONS] AERIS settings reset to defaults. AA learned/model data and current FBW values were not reset.");
  }
  void ProcessHotkeys(){
   if(settings==null || window==null || window.IsCapturingShortcut) return;
   if(settings.IsShortcutTriggered(AERISShortcutAction.EmergencyDisable)){
    Master=false;
    DisableTrimLearning();
    AERISLogger.Warn("[HOTKEY] "+settings.GetShortcutDisplay(AERISShortcutAction.EmergencyDisable)+" Emergency Disable: MASTER OFF. Auto Takeoff and Ground Stability released; Protect detection stays available while active control is disabled.");
    return;
   }
   if(settings.IsShortcutTriggered(AERISShortcutAction.ToggleMaster)){
    Master=!Master;
    AERISLogger.Info("[HOTKEY] "+settings.GetShortcutDisplay(AERISShortcutAction.ToggleMaster)+" MASTER Toggle: "+(Master?"ON":"OFF"));
    return;
   }
   if(settings.IsShortcutTriggered(AERISShortcutAction.ToggleWindow)){
    window.Visible=!window.Visible;
    if(toolbar!=null) toolbar.SetState(window.Visible);
    AERISLogger.Info("[HOTKEY] "+settings.GetShortcutDisplay(AERISShortcutAction.ToggleWindow)+" Window Toggle: "+(window.Visible?"OPEN":"CLOSED"));
   }
  }
  float ApplyAERISThrottleFloor(float aaDemand){
   if(Protect==null) return aaDemand;
   return Protect.ApplyThrottleFloor(aaDemand,FlightGlobals.ActiveVessel,Master);
  }
  float ApplyAERISThrottleCeiling(float aaDemand){
   float finiteDemand=IsFinite(aaDemand)?Mathf.Clamp01(aaDemand):0f;
   float limited=GroundStability==null ? finiteDemand : GroundStability.ApplyThrottleCeiling(finiteDemand);
   return IsFinite(limited)?Mathf.Clamp01(limited):0f;
  }

  // AERIS publishes demand only; registered propulsion providers consume it in their own FixedUpdate.  The bus must be
  // refreshed from one authoritative physical-loop publisher.  In particular, Protect recovery
  // remains active even when AP Speed Hold is OFF: clearing the bus solely because Speed Hold is
  // disabled used to release electric-propeller motor demand during an anti-stall intervention.
  void PublishAAPropulsionDemand(float aaThrottleDemand){
   // AA invokes this after its native speed controller has computed the physical throttle demand.
   // Cache it for the FixedUpdate publisher; Protect may raise it but never erase it.
   lastAaThrottleDemand=IsFinite(aaThrottleDemand)?Mathf.Clamp01(aaThrottleDemand):0f;
   PublishPropulsionDemand();
  }
  void PublishPropulsionDemand(){
   var v=FlightGlobals.ActiveVessel; var m=Manager;
   if(v==null){ AERISPropulsionDemandBus.Clear(null); return; }
   if(GroundStability!=null && GroundStability.ThrottleCutActive){
    AERISPropulsionDemandBus.Publish(v,new AERISPropulsionRequest{RequiredThrottle=0f,RequiredForwardThrustkN=0f,RequiredForwardThrustN=0f,StallWarning=false,StallRisk=false,StallDetected=false,AutopilotSpeedHold=false,TargetSpeedMps=0f});
    return;
   }
   bool speedActive=Acceleration!=null && Acceleration.Armed && Acceleration.ControlActive && Acceleration.AaNativeThrottleOverrideActive;
   bool autoTakeoffActive=AutoTakeoff!=null && AutoTakeoff.AaNativeThrottleOverrideActive;
   bool protectDemand=Protect!=null && settings!=null && settings.AppIntegrationEnabled &&
      (Protect.RequestedAssistThrottle>0.001f || Protect.ThrustAssistActive || Protect.ProtectActive);
   if(!speedActive && !autoTakeoffActive && !protectDemand){ AERISPropulsionDemandBus.Clear(v); return; }
   float protectThrottle=Protect==null ? 0f : Mathf.Clamp01(Protect.RequestedAssistThrottle);
   // VEL must publish AA's PID output. v0.2.69 accidentally published only Protect's floor,
   // which made normal Speed Hold request 0% while UI still reported VEL ACTIVE.
   float throttle=Mathf.Max(autoTakeoffActive?AutoTakeoff.ThrottleDemand:(speedActive ? lastAaThrottleDemand : 0f), protectThrottle);
   var request=new AERISPropulsionRequest{
    RequiredThrottle=throttle,
    RequiredForwardThrustkN=Protect==null ? 0f : Protect.RequiredForwardThrustkN,
    RequiredForwardThrustN=Protect==null ? 0f : Protect.RequiredThrust,
    StallWarning=Protect!=null && Protect.StallWarning,
    StallRisk=Protect!=null && Protect.Risk==ProtectRiskLevel.StallRisk,
    StallDetected=Protect!=null && Protect.StallDetected,
    // Auto Takeoff publishes fixed maximum throttle and is deliberately not marked
    // as speed hold; APP must not taper power at Vr.
    AutopilotSpeedHold=speedActive,
    TargetSpeedMps=speedActive && Velocity!=null && Velocity.TargetConfirmed ? Velocity.TargetSurfaceSpeedMps : 0f
   };
   string targetProvider=autoTakeoffActive && AutoTakeoff.ExternalPropulsionTakeoff ? AutoTakeoff.SelectedExternalProviderId : string.Empty;
   AERISPropulsionDemandBus.Publish(v,request,targetProvider);
  }
  static bool IsFinite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
  static bool IsFinite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
  static long RuntimeVesselId(Vessel vessel){if(vessel==null)return 0L;try{byte[] bytes=vessel.id.ToByteArray();return bytes!=null&&bytes.Length>=8?BitConverter.ToInt64(bytes,0):vessel.id.GetHashCode();}catch{return vessel.id.GetHashCode();}}
  static long RuntimeBodyId(Vessel vessel){return vessel==null||vessel.mainBody==null?0L:vessel.mainBody.flightGlobalsIndex;}
  void NotifyRuntimeVessel(Vessel vessel){if(Performance!=null)Performance.VesselChanged(RuntimeVesselId(vessel),RuntimeBodyId(vessel));}
  void SyncRuntimeRevisions(){if(Performance==null||Airfields==null)return;long database=Airfields.DatabaseRevision;long selection=Airfields.SelectionRevision;if(database==lastRuntimeDatabaseRevision&&selection==lastRuntimeSelectionRevision)return;lastRuntimeDatabaseRevision=database;lastRuntimeSelectionRevision=selection;Performance.UpdateRunwayRevisions(database,selection);}
  string RuntimeLateralMode(){if(Hdg!=null&&Hdg.Armed)return "HDG/"+Hdg.ControlState;if(Bank!=null&&Bank.Armed)return "BANK/"+Bank.ControlState;return "MANUAL";}
  string RuntimeVerticalMode(){if(Altitude!=null&&Altitude.Armed)return "ALT/"+Altitude.ControlState;if(VerticalSpeed!=null&&VerticalSpeed.Armed)return "VS/"+VerticalSpeed.ControlState;if(Pitch!=null&&Pitch.Armed)return "PITCH/"+Pitch.ControlState;return "MANUAL";}
  string RuntimeSpeedMode(){if(Velocity!=null&&Velocity.Armed)return "VEL/"+Velocity.ControlState;if(Acceleration!=null&&Acceleration.Armed)return "ACC/"+Acceleration.ControlState;return "MANUAL";}
  void PublishCanonicalSnapshot(){
   if(Performance==null||Attitude==null)return;float now=Time.realtimeSinceStartup;if(now<nextCanonicalSnapshotTime)return;nextCanonicalSnapshotTime=now+0.05f;
   var watch=System.Diagnostics.Stopwatch.StartNew();
   bool valid=Attitude.Valid&&Attitude.InstrumentValid&&Attitude.SharedSurfaceSpeedValid&&Attitude.VerticalSpeedValid&&Attitude.AltitudeAslValid&&Attitude.SharedDynamicPressureValid;
   double bank=Attitude.InstrumentHorizonBankValid?Attitude.InstrumentHorizonBankDeg:Attitude.InstrumentBankDeg;
   double pitch=Attitude.InstrumentPitchValid?Attitude.InstrumentPitchDeg:0.0;
   double heading=Attitude.InstrumentHeadingValid?Attitude.InstrumentHeadingDeg:0.0;
   var observation=Landing==null?null:Landing.Observation;var direction=Landing==null?null:Landing.ActiveDirection;
   double localizer=observation!=null&&observation.Valid&&IsFinite(observation.CrossTrackMeters)?observation.CrossTrackMeters:0.0;
   double glide=observation!=null&&observation.Valid&&IsFinite(observation.GlidePathErrorMeters)?observation.GlidePathErrorMeters:0.0;
   Performance.Publish(new AERISCanonicalSnapshot(Performance.CaptureStamp(),Planetarium.GetUniversalTime(),valid,
    IsFinite(bank)?bank:0.0,IsFinite(pitch)?pitch:0.0,IsFinite(heading)?heading:0.0,
    IsFinite(Attitude.SurfaceSpeedMps)?Attitude.SurfaceSpeedMps:0.0,IsFinite(Attitude.VerticalSpeedMps)?Attitude.VerticalSpeedMps:0.0,
    IsFinite(Attitude.AltitudeAslM)?Attitude.AltitudeAslM:0.0,IsFinite(Attitude.RadarAltitudeM)?Attitude.RadarAltitudeM:0.0,
    IsFinite(Attitude.DynamicPressureKpa)?Attitude.DynamicPressureKpa:0.0,RuntimeLateralMode(),RuntimeVerticalMode(),RuntimeSpeedMode(),
    Protect==null?"UNAVAILABLE":Protect.RiskText,Landing==null?"OFF":Landing.StateText,
    direction==null?string.Empty:direction.StableId,localizer,glide));
	   watch.Stop();lastPerformanceSnapshotCostMilliseconds=watch.Elapsed.TotalMilliseconds;
  }
  bool AirfieldProviderRuntimeReady(bool inFlight){
   Vessel vessel=FlightGlobals.ActiveVessel;
   bool ready=inFlight&&vessel!=null&&!vessel.packed;
   if(!ready){airfieldProviderReadyVessel=null;airfieldProviderReadySince=-1f;return false;}
   float now=Time.realtimeSinceStartup;
   if(airfieldProviderReadyVessel!=vessel){airfieldProviderReadyVessel=vessel;airfieldProviderReadySince=now;return false;}
   if(airfieldProviderReadySince<0f){airfieldProviderReadySince=now;return false;}
   return now-airfieldProviderReadySince>=1.5f;
  }
  void FixedUpdate(){
   if(!HighLogic.LoadedSceneIsFlight) return;
   // The virtual attitude instrument is a sensor pipeline. Keep its gyro integration on
   // the physics cadence rather than UI/update cadence so every KSP control step contributes.
   if(Attitude!=null) Attitude.Update(FlightGlobals.ActiveVessel);
   // Publish continuously so a propulsion provider never depends on Update/UI/status-query cadence.
   PublishPropulsionDemand();
  }
  void OnFlyByWire(FlightCtrlState state){ }
  void Update(){
   GameScenes currentScene=HighLogic.LoadedScene;
   if(currentScene!=observedUiScene)HandleUiSceneBoundary(currentScene);
   bool inFlight=HighLogic.LoadedSceneIsFlight;
   if(inFlight!=wasFlightScene){HandleFlightSceneBoundary(inFlight);wasFlightScene=inFlight;}
   float now=Time.realtimeSinceStartup;
   bool airfieldProvidersReady=AirfieldProviderRuntimeReady(inFlight);
	   if(Performance!=null){double snapshotCost=lastPerformanceSnapshotCostMilliseconds;lastPerformanceSnapshotCostMilliseconds=-1.0;Performance.Tick(now,Mathf.Max(0f,Time.unscaledDeltaTime)*1000.0,snapshotCost,Landing!=null&&Landing.Armed,inFlight);}
   AERISFlightDataArchive.DrainResults();
   if(Airfields!=null)Airfields.Tick(Landing!=null&&Landing.Armed,airfieldProvidersReady);
   SyncRuntimeRevisions();
   if(inFlight&&Landing!=null)Landing.Tick(FlightGlobals.ActiveVessel,Attitude);
   if(Terrain!=null)Terrain.Tick(FlightGlobals.ActiveVessel,Landing,Airfields);
   if(toolbar!=null&&window!=null)toolbar.SetState(window.ToolbarVisibleState);
   if(!inFlight)return;
   ProcessHotkeys();DisableTrimLearning();
   if(GroundStability!=null)GroundStability.UpdateGroundState(FlightGlobals.ActiveVessel,Attitude);
   if(Bank!=null){Bank.ObserveVesselState(FlightGlobals.ActiveVessel,Attitude);Bank.Tick(Manager,Master);}
   if(Protect!=null){Protect.ExternalPropulsionIntegrationEnabled=settings!=null&&settings.AppIntegrationEnabled;Protect.AutoGearExternalInhibit=AutoTakeoff!=null&&AutoTakeoff.Armed;Protect.Tick(Manager,Master);}
   if(ExternalAutomation!=null)ExternalAutomation.Tick();
   PublishCanonicalSnapshot();
   if(recorder!=null)SafeRecorderStep(()=>recorder.Sample(FlightGlobals.ActiveVessel,Protect,Bank,Pitch,VerticalSpeed,Altitude,Hdg,Acceleration,Velocity,Attitude,Manager,Master,settings!=null&&settings.EnableAAComparisonTelemetry),"general flight sample");
   if(now-lastLog>1f){lastLog=now;string s=CoreState;if(s!=lastState){lastState=s;AERISLogger.Info("AA Core state: "+s);}string standbyReason;bool standby=IsFbwStandby(out standbyReason);string standbyState=Master?(MasterAmber?"GROUND PROTECTION ACTIVE":(standby?"STANDBY: "+standbyReason:"ACTIVE")):"OFF";if(standbyState!=lastStandby){lastStandby=standbyState;AERISLogger.Info("MASTER display state: "+standbyState);}}
  }
  void OnGUI(){if(HighLogic.LoadedSceneIsFlight){if(flightInstrument!=null)flightInstrument.Draw();if(window!=null)window.Draw();}else if(window!=null)window.DrawPreloadOnly();}
  void OnDestroy(){
   if(duplicateInstance||instance!=this)return;
   // Flight-control ownership is released first and never waits for telemetry, disk or workers.
   try{ResetAutomationState("shutdown",true);}catch{}
   try{AERISExternalAutomationApi.Unbind(ExternalAutomation);}catch{}
   try{AERISRunwayTrackTokenApi.Unbind();if(Airfields!=null)Airfields.Dispose();}catch{}
   try{AERISLogger.EventSink=null;AERISFlightRecorderApi.EventSink=null;AERISFlightRecorderApi.SchemaSink=null;AERISFlightRecorderApi.TelemetrySink=null;if(recorder!=null)recorder.EndFlight("aeris-shutdown");}catch{}
   try{StandardFlyByWire.ExternalThrottleFloor=null;StandardFlyByWire.ExternalThrottleCeiling=null;StandardFlyByWire.ExternalPropulsionDemand=null;ClearExternalControlDemands();AERISPropulsionDemandBus.Clear(FlightGlobals.ActiveVessel);}catch{}
   try{if(flightInstrument!=null)flightInstrument.Dispose();}catch{}
   if(toolbar!=null)toolbar.Shutdown();
   GameEvents.onVesselChange.Remove(OnVesselChange);GameEvents.onVesselGoOffRails.Remove(OnVesselGoOffRails);GameEvents.onVesselWasModified.Remove(OnVesselWasModified);
   if(settings!=null)settings.Save();
   try{if(MapDramCache!=null)MapDramCache.LogShutdownSummary("AERIS_SHUTDOWN");}catch{}
   AERISLogger.Info(""+AERISBuildVersion.Display+" shutdown.");
   AERISLogger.Shutdown();
   try{if(Terrain!=null)Terrain.Dispose();}catch{}
   try{if(Performance!=null)Performance.Dispose();}catch{}
   Performance=null;
   AERISBackgroundFileWriter.ShutdownNoWait();
   instance=null;
  }
 }
}
