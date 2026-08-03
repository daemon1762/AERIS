using UnityEngine;
using AtmosphereAutopilot;
using AERISFlightControl.Core;
using AERISFlightControl.Settings;
using AERISFlightControl.Logging;
using AERISFlightControl.Protect;
using AERISFlightControl.Autopilot;
using AERISFlightControl.Integrations;
using AERISFlightControl.API;
using AERISFlightControl.FlightPlans;
using AERISFlightControl.Landing;
using AERISFlightControl.Performance;
using AERISFlightControl.Recording;
using AERISFlightControl.Terrain;
namespace AERISFlightControl.UI
{
 internal sealed class AERISWindow
 {
  const int Id=741129;
  const int ResizeControlHint=741130;
  const float MinWindowWidth=480f;
  const float MinWindowHeight=360f;
  const float MaxWindowWidth=1050f;
  const float MaxWindowHeight=920f;
  const float ResizeGripSize=42f;
  const float ResizeGripWidth=48f;
  const float FooterHeight=46f;
  const float HeaderDragHeight=22f;
  const float CompactButtonHeight=20f;
  const float MasterButtonHeight=40f;
  static readonly string[] FlightArchiveLimitLabels=new string[]{"1","2","3","4","5","6","7","8","9","10","11","12","13","14","15","16","17","18","19","20","21","22","23","24","25","26","27","28","29","30"};
  const float SmallButtonWidth=82f;
  const float ActionButtonWidth=124f;
  const float CompactLabelWidth=82f;
  const float CompactFieldWidth=78f;
  // CP3.5 Gate 1 UI geometry contract: control geometry follows window size only.
  // Text/content is never allowed to wrap or resize/reflow a control. This restores
  // the visually responsive pre-Candidate-9 behaviour without reintroducing
  // content-driven button jumps.
  const float BaseWideButtonWidth=390f;
  // Historical responsive baseline retained for inherited Gate 5 regression; top-row
  // Candidate 2 geometry is computed from the full available row width instead.
  const float BaseTabButtonWidth=126f;
  const int BaseTabColumns=3;
  const float TopTabGap=2f;
  const float BaseSelectorWidth=330f;
  const float BaseAirfieldActionWidth=360f;
  const float AirfieldActionButtonHeight=36f;
  const float UiTelemetryRefreshSeconds=0.25f;
  Rect rect; readonly AERISSettings settings; readonly AERISBootstrap core; internal bool Visible; internal bool PreloadStatusVisible; int tab=3; int systemPage; int autopilotPage=1; bool advanced; bool confirmResetAllFbw; bool confirmResetSettings; int lateralMode; int verticalMode; int speedMode; bool verticalMaster; bool speedMaster; bool takeoffConfigExpanded; Vector2 contentScroll; Vector2 navPlanScroll; Vector2 landAirfieldScroll; Vector2 landDirectionScroll; Vector2 airfieldsScroll; Vector2 preloadMapsScroll; string airfieldDetailId=""; string preloadDestructiveConfirm=""; string navLibraryMessage="READY"; string landMessage="READY"; string runwayCalibrationMessage="READY"; string runwayWarpMessage="READY"; GUIStyle wrappedLabelStyle; GUIStyle airfieldRowButtonStyle; GUIStyle airfieldActionButtonStyle; GUIStyle responsiveButtonStyle;
  bool resizing; int resizeControlId; Vector2 resizeStartSize; Vector2 resizeStartPointerScreen; Vector2 resizeAccumulatedDelta; bool resizeCommitPending; bool resizeHasChanged; Vector2 pendingResizeSize;
  int hotkeyCaptureAction=-1; KeyCode capturePrimary=KeyCode.None; KeyCode captureSecondary=KeyCode.None;
  internal bool IsCapturingShortcut { get { return hotkeyCaptureAction>=0; } }
  internal AERISWindow(AERISSettings s,AERISBootstrap c){settings=s;core=c;Visible=s.MainWindowVisible;PreloadStatusVisible=false;tab=3;systemPage=0;rect=new Rect(s.MainWindowX,s.MainWindowY,ClampWidth(s.MainWindowWidth),ClampHeight(s.MainWindowHeight));}
  internal void ResetArmUiState(){verticalMaster=false;speedMaster=false;takeoffConfigExpanded=false;hotkeyCaptureAction=-1;capturePrimary=KeyCode.None;captureSecondary=KeyCode.None;}
  internal bool ToolbarVisibleState { get { return HighLogic.LoadedSceneIsFlight ? Visible : PreloadStatusVisible; } }
  internal void ShowForCurrentScene(){if(HighLogic.LoadedSceneIsFlight)Visible=true;else PreloadStatusVisible=true;}
  internal void HideForCurrentScene(){if(HighLogic.LoadedSceneIsFlight)Visible=false;else PreloadStatusVisible=false;}
  internal void OnSceneBoundary(){Visible=false;PreloadStatusVisible=false;}
  internal void Draw(){
   if(!Visible)return;
   ClampRectToScreen();
   // Explicit width/height constraints are required here. Without them GUILayout.Window
   // reflows to its content minimum width after every repaint, which silently erased
   // horizontal resize requests even though the drag handler had captured them.
   GUIStyle skinLabel=GUI.skin.label; GUIStyle skinButton=GUI.skin.button; GUIStyle skinToggle=GUI.skin.toggle; GUIStyle skinBox=GUI.skin.box;
   bool oldLabelWrap=skinLabel.wordWrap; TextClipping oldLabelClip=skinLabel.clipping;
   bool oldButtonWrap=skinButton.wordWrap; TextClipping oldButtonClip=skinButton.clipping;
   bool oldToggleWrap=skinToggle.wordWrap; TextClipping oldToggleClip=skinToggle.clipping;
   bool oldBoxWrap=skinBox.wordWrap; TextClipping oldBoxClip=skinBox.clipping;
   Rect drawnRect;
   try{
    skinLabel.wordWrap=false;skinLabel.clipping=TextClipping.Clip;
    skinButton.wordWrap=false;skinButton.clipping=TextClipping.Clip;
    skinToggle.wordWrap=false;skinToggle.clipping=TextClipping.Clip;
    skinBox.wordWrap=false;skinBox.clipping=TextClipping.Clip;
    drawnRect=GUILayout.Window(Id,rect,_=>Content(),"AERIS — Flight Control",
     GUILayout.Width(rect.width),GUILayout.Height(rect.height));
   }finally{
    skinLabel.wordWrap=oldLabelWrap;skinLabel.clipping=oldLabelClip;
    skinButton.wordWrap=oldButtonWrap;skinButton.clipping=oldButtonClip;
    skinToggle.wordWrap=oldToggleWrap;skinToggle.clipping=oldToggleClip;
    skinBox.wordWrap=oldBoxWrap;skinBox.clipping=oldBoxClip;
   }
   rect.x=drawnRect.x;rect.y=drawnRect.y;
   if(resizeCommitPending){rect.width=ClampWidth(pendingResizeSize.x);rect.height=ClampHeight(pendingResizeSize.y);resizeCommitPending=false;}
   else{rect.width=ClampWidth(drawnRect.width);rect.height=ClampHeight(drawnRect.height);}
   ClampRectToScreen();
   settings.MainWindowX=rect.x;settings.MainWindowY=rect.y;settings.MainWindowWidth=rect.width;settings.MainWindowHeight=rect.height;
  }
  internal void DrawPreloadOnly(){
   // AERIS PRELOAD control is opened only through the existing AERIS
   // toolbar/window route. CP2.5 exposes one standard Preload mode only.
   if(HighLogic.LoadedSceneIsFlight||!PreloadStatusVisible)return;
   ClampRectToScreen();
   GUIStyle skinLabel=GUI.skin.label; GUIStyle skinButton=GUI.skin.button; GUIStyle skinToggle=GUI.skin.toggle; GUIStyle skinBox=GUI.skin.box;
   bool oldLabelWrap=skinLabel.wordWrap; TextClipping oldLabelClip=skinLabel.clipping;
   bool oldButtonWrap=skinButton.wordWrap; TextClipping oldButtonClip=skinButton.clipping;
   bool oldToggleWrap=skinToggle.wordWrap; TextClipping oldToggleClip=skinToggle.clipping;
   bool oldBoxWrap=skinBox.wordWrap; TextClipping oldBoxClip=skinBox.clipping;
   Rect drawnRect;
   try{
    skinLabel.wordWrap=false;skinLabel.clipping=TextClipping.Clip;
    skinButton.wordWrap=false;skinButton.clipping=TextClipping.Clip;
    skinToggle.wordWrap=false;skinToggle.clipping=TextClipping.Clip;
    skinBox.wordWrap=false;skinBox.clipping=TextClipping.Clip;
    drawnRect=GUILayout.Window(Id,rect,_=>PreloadOnlyContent(),"AERIS — Preload Terrain",
     GUILayout.Width(rect.width),GUILayout.Height(rect.height));
   }finally{
    skinLabel.wordWrap=oldLabelWrap;skinLabel.clipping=oldLabelClip;
    skinButton.wordWrap=oldButtonWrap;skinButton.clipping=oldButtonClip;
    skinToggle.wordWrap=oldToggleWrap;skinToggle.clipping=oldToggleClip;
    skinBox.wordWrap=oldBoxWrap;skinBox.clipping=oldBoxClip;
   }
   rect.x=drawnRect.x;rect.y=drawnRect.y;
   if(resizeCommitPending){rect.width=ClampWidth(pendingResizeSize.x);rect.height=ClampHeight(pendingResizeSize.y);resizeCommitPending=false;}
   else{rect.width=ClampWidth(drawnRect.width);rect.height=ClampHeight(drawnRect.height);}
   ClampRectToScreen();
   settings.MainWindowX=rect.x;settings.MainWindowY=rect.y;settings.MainWindowWidth=rect.width;settings.MainWindowHeight=rect.height;
  }
  void PreloadOnlyContent(){
   GUILayout.Label(UiBuildTitle("PRELOAD TERRAIN CONTROL"));
   GUILayout.Label("Main Menu / Space Center / VAB / SPH preload control. Automatic preload uses the fixed AERIS default policy; only ON/OFF and body operations are exposed.");
   DrawPreloadTerrainMapsPage();
   GUILayout.BeginHorizontal(GUILayout.Height(ResponsiveHeight(FooterHeight)));
   GUILayout.Label("Resize: drag the dedicated ↘ control",GUILayout.ExpandWidth(true));
   if(GUILayout.Button("Close",GUILayout.Width(ResponsiveWidth(70f)),GUILayout.Height(CompactControlHeight())))PreloadStatusVisible=false;
   Rect resizeGrip=GUILayoutUtility.GetRect(ResizeGripWidth,ResizeGripSize,GUILayout.Width(ResizeGripWidth),GUILayout.Height(ResizeGripSize));
   GUILayout.EndHorizontal();
   DrawResizeGrip(resizeGrip);
   GUI.DragWindow(new Rect(0,0,Mathf.Max(0f,rect.width-ResizeGripWidth-4f),HeaderDragHeight));
  }
  static float ClampWidth(float width){float screenLimit=Mathf.Max(320f,Mathf.Min(MaxWindowWidth,Screen.width-12f));float minimum=Mathf.Min(MinWindowWidth,screenLimit);return Mathf.Clamp(width<=0f?AERISSettings.DefaultMainWindowWidth:width,minimum,screenLimit);}
  static float ClampHeight(float height){float screenLimit=Mathf.Max(280f,Mathf.Min(MaxWindowHeight,Screen.height-12f));float minimum=Mathf.Min(MinWindowHeight,screenLimit);return Mathf.Clamp(height<=0f?AERISSettings.DefaultMainWindowHeight:height,minimum,screenLimit);}
  void ClampRectToScreen(){rect.width=ClampWidth(rect.width);rect.height=ClampHeight(rect.height);float maxX=Mathf.Max(0f,Screen.width-rect.width);float maxY=Mathf.Max(0f,Screen.height-rect.height);rect.x=Mathf.Clamp(rect.x,0f,maxX);rect.y=Mathf.Clamp(rect.y,0f,maxY);}
  void Content(){
   GUILayout.Label(UiBuildTitle("FLIGHT CONTROL"));
   DrawMasterSwitch();
   tab=MainTabs(tab);

   // The footer owns its own layout row.  The scroll view ends above it, so its
   // vertical scrollbar can never overlap the resize hit area.
   int tabRows=MainTabRowCount();
   float headerReserve=150f+(tabRows-1)*ResponsiveHeight(32f);
   float scrollHeight=Mathf.Max(96f,rect.height-headerReserve);
   contentScroll=GUILayout.BeginScrollView(contentScroll,false,true,GUILayout.Height(scrollHeight));
   if(tab==0) DrawFbw(); else if(tab==1) DrawProtect(); else if(tab==2) DrawAutopilot(); else if(tab==3) DrawSystem(); else DrawExtendAddons();
   GUILayout.EndScrollView();

   GUILayout.BeginHorizontal(GUILayout.Height(ResponsiveHeight(FooterHeight)));
   GUILayout.Label("Resize: drag the dedicated ↘ control",GUILayout.ExpandWidth(true));
   if(GUILayout.Button("Close",GUILayout.Width(ResponsiveWidth(70f)),GUILayout.Height(CompactControlHeight()))) Visible=false;
   Rect resizeGrip=GUILayoutUtility.GetRect(ResizeGripWidth,ResizeGripSize,GUILayout.Width(ResizeGripWidth),GUILayout.Height(ResizeGripSize));
   GUILayout.EndHorizontal();

   DrawResizeGrip(resizeGrip);
   GUI.DragWindow(new Rect(0,0,Mathf.Max(0f,rect.width-ResizeGripWidth-4f),HeaderDragHeight));
  }
  int MainTabRowCount(){
   // Screenshot-reference topology is fixed: 3 primary tabs, then 2 secondary tabs.
   // Keep the inherited expression marker; the result is always two rows and never
   // depends on text content or window-width thresholds.
   return Mathf.CeilToInt(5f/BaseTabColumns);
  }
  void DrawResizeGrip(Rect grip){
   // This rect is allocated in the unscrollable footer.  Do not derive it from
   // rect.width/height: that placed the old grip on top of the scroll bar.
   GUI.Box(grip,"↘",GUI.skin.button);
   Event e=Event.current;
   if(e==null)return;
   int controlId=GUIUtility.GetControlID(ResizeControlHint,FocusType.Passive,grip);
   if(e.type==EventType.MouseDown&&e.button==0&&grip.Contains(e.mousePosition)&&GUIUtility.hotControl==0){
    resizing=true;
    resizeControlId=controlId;
    resizeStartSize=new Vector2(rect.width,rect.height);
    resizeStartPointerScreen=GUIUtility.GUIToScreenPoint(e.mousePosition);
    resizeAccumulatedDelta=Vector2.zero;
    pendingResizeSize=resizeStartSize;
    GUIUtility.hotControl=controlId;
    AERISLogger.Info("[SYSTEM] Window resize start: "+resizeStartSize.x.ToString("0")+" x "+resizeStartSize.y.ToString("0"));
    e.Use();
    return;
   }
   if(!resizing||GUIUtility.hotControl!=resizeControlId)return;
   if(e.type==EventType.MouseDrag){
    // Derive the delta from a fixed screen-space pointer anchor.  The grip itself moves
    // while the window is resized, so accumulating window-local Event.delta caused large
    // jumps and made it difficult to stop at an intermediate size.
    Vector2 pointerScreen=GUIUtility.GUIToScreenPoint(e.mousePosition);
    resizeAccumulatedDelta=pointerScreen-resizeStartPointerScreen;
    pendingResizeSize=new Vector2(
     ClampWidth(resizeStartSize.x+resizeAccumulatedDelta.x),
     ClampHeight(resizeStartSize.y+resizeAccumulatedDelta.y));
    resizeCommitPending=true;
    resizeHasChanged=true;
    GUI.changed=true;
    e.Use();
   }else if(e.type==EventType.MouseUp&&e.button==0){
    resizing=false;
    GUIUtility.hotControl=0;
    if(resizeHasChanged){
     settings.MainWindowWidth=pendingResizeSize.x;
     settings.MainWindowHeight=pendingResizeSize.y;
     settings.Save();
     if(core!=null&&core.Performance!=null)core.Performance.DisplayLayoutChanged();
     AERISLogger.Info("[SYSTEM] Window size saved: "+pendingResizeSize.x.ToString("0")+" x "+pendingResizeSize.y.ToString("0")+
      " (drag delta "+resizeAccumulatedDelta.x.ToString("+0;-0;0")+", "+resizeAccumulatedDelta.y.ToString("+0;-0;0")+")");
     resizeHasChanged=false;
    }
    e.Use();
   }
  }
  int MainTabs(int selected){
   string[] labels={"FLIGHT CONTROL","PROTECT","AUTOPILOT","SYSTEM","EXTEND ADDONS"};
   int next=selected;
   DrawSymmetricTabRow(ref next,labels,0,3);
   GUILayout.Space(TopTabGap);
   DrawSymmetricTabRow(ref next,labels,3,2);
   return next;
  }
  int SystemTabs(int selected){
   string[] labels={"STATUS","OPTIONS","AIRFIELDS","PRELOAD MAPS"};
   int next=selected;
   DrawSymmetricTabRow(ref next,labels,0,3);
   GUILayout.Space(TopTabGap);
   // Candidate 13 intentionally removed DIAGNOSTICS. The remaining PRELOAD MAPS row fills
   // the available width rather than leaving the old missing-button gap on one side.
   DrawSymmetricTabRow(ref next,labels,3,1);
   return next;
  }
  void DrawSymmetricTabRow(ref int selected,string[] labels,int start,int count){
   EnsureAirfieldStyles();
   float height=CompactControlHeight();
   Rect row=GUILayoutUtility.GetRect(1f,height,GUILayout.ExpandWidth(true),GUILayout.Height(height));
   if(labels==null||count<=0)return;
   int end=Mathf.Min(labels.Length,start+count);
   int actual=Mathf.Max(0,end-start);
   if(actual<=0)return;
   float totalGap=TopTabGap*Mathf.Max(0,actual-1);
   float width=Mathf.Max(1f,(row.width-totalGap)/actual);
   for(int i=0;i<actual;i++){
    int page=start+i;
    Rect buttonRect=new Rect(row.x+i*(width+TopTabGap),row.y,width,row.height);
    if(GUI.Toggle(buttonRect,selected==page,labels[page],responsiveButtonStyle))selected=page;
   }
  }
  static GUIStyle masterButtonStyle;
  static GUIStyle MasterButtonStyle(){if(masterButtonStyle!=null)return masterButtonStyle;masterButtonStyle=new GUIStyle(GUI.skin.button){alignment=TextAnchor.MiddleCenter,fontSize=14,fontStyle=FontStyle.Bold,wordWrap=false,clipping=TextClipping.Clip,fixedHeight=0f,stretchHeight=false};return masterButtonStyle;}
  void DrawMasterSwitch(){
   string standbyReason=string.Empty;bool amber=core.MasterAmber;bool standby=core.Master&&!amber&&core.IsFbwStandby(out standbyReason);Color border=amber?new Color(1.00f,0.72f,0.08f,1f):(standby?new Color(0.55f,0.57f,0.60f,1f):(core.Master?new Color(0.10f,0.85f,0.24f,1f):new Color(0.95f,0.16f,0.16f,1f)));string label=amber?"MASTER ARM  ON   —   GROUND ASSIST ACTIVE":(standby?"MASTER ARM  ON   —   STANDBY":(core.Master?"MASTER ARM  ON   —   AA FBW ACTIVE":"MASTER ARM  OFF   —   CLICK TO ARM AA FBW"));float masterHeight=ResponsiveHeight(MasterButtonHeight);Rect r=GUILayoutUtility.GetRect(1f,masterHeight,GUILayout.ExpandWidth(true),GUILayout.Height(masterHeight));Color oldBackground=GUI.backgroundColor;Color oldColor=GUI.color;try{GUI.backgroundColor=border;if(GUI.Button(r,label,MasterButtonStyle())){bool requested=!core.Master;core.Master=requested;AERISLogger.Info("UI MASTER request: "+(requested?"ON":"OFF"));}GUI.color=border;GUI.DrawTexture(new Rect(r.x,r.y,r.width,1f),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.x,r.yMax-1f,r.width,1f),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.x,r.y,1f,r.height),Texture2D.whiteTexture);GUI.DrawTexture(new Rect(r.xMax-1f,r.y,1f,r.height),Texture2D.whiteTexture);}finally{GUI.backgroundColor=oldBackground;GUI.color=oldColor;}if(standby&&!string.IsNullOrEmpty(standbyReason))GUILayout.Label("STANDBY: "+standbyReason);GUILayout.Space(2);
  }
  void DrawSystem(){GUILayout.Label("SYSTEM CONTROL");systemPage=SystemTabs(systemPage);GUILayout.Space(3);if(systemPage==0)DrawSystemStatus();else if(systemPage==1)DrawSystemOptions();else if(systemPage==2)DrawAirfieldsPage();else DrawPreloadTerrainMapsPage();}
  void DrawSystemStatus(){
   GUILayout.BeginVertical("box");
   GUILayout.Label("SHORTCUTS");
   GUILayout.Label("Window: "+settings.GetShortcutDisplay(AERISShortcutAction.ToggleWindow));
   GUILayout.Label("MASTER: "+settings.GetShortcutDisplay(AERISShortcutAction.ToggleMaster));
   GUILayout.Label("Emergency disable: "+settings.GetShortcutDisplay(AERISShortcutAction.EmergencyDisable));
   GUILayout.Label("Change these in SYSTEM > OPTIONS.");
   GUILayout.EndVertical();
  }
  void DrawSystemOptions(){
   ConsumeShortcutCaptureInput();
   GUILayout.BeginVertical("box");
   GUILayout.Label("DISPLAY");
   DrawDisplayModeSelector("FDI",ref settings.FlightInstrumentDisplayMode);
   DrawDisplayModeSelector("ND",ref settings.NavigationDisplayMode);
   ToggleOption(ref settings.NavigationDisplayTrackUp,"Navigation display track-up");
   GUILayout.Label("ND ranges are fixed at 5 / 10 / 20 / 40 / 80 / 160 km. Drag the map to enter PLAN; RECENTER restores the prior orientation.");
   DrawTerrainQualitySelector();
   DrawTerrainModeSelector();
   DrawTerrainGpuSelector();
   DrawTerrainColourSelector();
   GUILayout.Label("ND presentation cadence is fixed at 10 Hz during CP3.75 stabilization.");
   ToggleOption(ref settings.TerrainContoursEnabled,"Terrain contour lines");
   ToggleOption(ref settings.TerrainShadingEnabled,"Terrain relief shading");
   GUILayout.BeginHorizontal();
   if(ActionButton("RESET FDI LAYOUT")){
    settings.FlightInstrumentLayoutCustomized=false;settings.Save();if(core!=null&&core.Performance!=null)core.Performance.DisplayLayoutChanged();AERISLogger.Info("[SYSTEM/OPTIONS] FDI layout reset to the vertical-gauge-right default.");
   }
   if(ActionButton("RESET ND LAYOUT")){
    settings.NavigationDisplayLayoutCustomized=false;settings.Save();if(core!=null&&core.Performance!=null)core.Performance.DisplayLayoutChanged();AERISLogger.Info("[SYSTEM/OPTIONS] ND layout reset to the navball-left default.");
   }
   GUILayout.EndHorizontal();
   GUILayout.Label("Drag either panel by its title bar. Resize with the dedicated bottom-right corner control. Layout is stored relative to the KSP game window.");
   GUILayout.Label("AUTO is the default: panels appear only on demand. ALWAYS keeps a panel visible. OFF suppresses the panel plus display-owned work.");
   GUILayout.EndVertical();
   GUILayout.BeginVertical("box");GUILayout.Label("FDR / CVR ARCHIVE");DrawFlightDataArchiveLimitSelector();GUILayout.Label("Only verified committed ZIPs are pruned. Active/raw sessions, .zip.tmp and unverified ZIPs are never deleted.");GUILayout.EndVertical();
   GUILayout.BeginVertical("box");GUILayout.Label("SHORTCUT KEYS — UP TO TWO SIMULTANEOUS KEYS");GUILayout.Label("Choose one key or capture two keys. The command fires when all bound keys are held and either key is pressed.");DrawShortcutBinding("Toggle window",AERISShortcutAction.ToggleWindow);DrawShortcutBinding("Toggle MASTER",AERISShortcutAction.ToggleMaster);DrawShortcutBinding("Emergency disable",AERISShortcutAction.EmergencyDisable);if(hotkeyCaptureAction>=0)DrawShortcutCaptureBox();GUILayout.EndVertical();
   GUILayout.BeginVertical("box");GUILayout.Label("SETTINGS RESET");GUILayout.Label("Resets AERIS UI, display, hotkey, integration and recorder settings. It does not reset AA learned/model data or current-aircraft FBW values.");if(!confirmResetSettings){if(ActionButton("RESET AERIS SETTINGS"))confirmResetSettings=true;}else{GUILayout.Label("Reset all AERIS settings to defaults?");GUILayout.BeginHorizontal();if(SmallButton("RESET NOW")){core.ResetAerisSettingsFromUi();ApplySettingsGeometry();confirmResetSettings=false;settings.Save();}if(SmallButton("CANCEL"))confirmResetSettings=false;GUILayout.EndHorizontal();}GUILayout.EndVertical();
  }
  void DrawPreloadTerrainMapsPage(){
   var tiles=core!=null&&core.Terrain!=null?core.Terrain.DisplayTiles:null;
   GUILayout.BeginVertical("box");
   GUILayout.Label("PRELOAD TERRAIN MAPS");
   if(tiles==null){GUILayout.Label("Terrain preload system is initializing.");GUILayout.EndVertical();return;}
   bool enabled=GUILayout.Toggle(settings.TerrainPreloadEnabled,"AUTOMATIC PRELOAD");
   if(enabled!=settings.TerrainPreloadEnabled)tiles.SetPreloadEnabled(enabled);
   GUILayout.EndVertical();
   AERISTerrainPreloadStatusSnapshot snapshot=tiles.PreloadStatus;
   float preloadBodyListReserve=190f;
   preloadMapsScroll=GUILayout.BeginScrollView(preloadMapsScroll,GUILayout.Height(Mathf.Max(140f,rect.height-preloadBodyListReserve)));
   try{
    AERISTerrainPreloadBodyStatus[] bodies=snapshot.Bodies??new AERISTerrainPreloadBodyStatus[0];
    if(bodies.Length==0)GUILayout.Label("No supported solid-surface bodies are indexed yet.");
    for(int i=0;i<bodies.Length;i++)DrawPreloadBodyRow(tiles,bodies[i]);
   }finally{GUILayout.EndScrollView();}
  }
  void DrawPreloadBodyRow(AERISTerrainTileSystem tiles,AERISTerrainPreloadBodyStatus body){
   if(body==null||string.IsNullOrEmpty(body.BodyName))return;
   GUILayout.BeginVertical("box");
   GUILayout.BeginHorizontal();
   GUILayout.Label(body.BodyName,GUILayout.ExpandWidth(true));
   GUILayout.Label((System.Math.Max(0.0,System.Math.Min(1.0,body.CoverageRatio))*100.0).ToString("0.0")+"%",GUILayout.Width(72f));
   GUILayout.EndHorizontal();
   string rebuildKey="REBUILD|"+body.BodyName;string deleteKey="DELETE|"+body.BodyName;
   GUILayout.BeginHorizontal();
   if(SmallButton("BUILD")){tiles.PreloadBuild(body.BodyName);preloadDestructiveConfirm="";}
   bool paused=string.Equals(body.Status,"PAUSED",System.StringComparison.OrdinalIgnoreCase);
   if(SmallButton(paused?"RESUME":"PAUSE")){if(paused)tiles.PreloadResume(body.BodyName);else tiles.PreloadPause(body.BodyName);preloadDestructiveConfirm="";}
   bool deleteArmed=string.Equals(preloadDestructiveConfirm,deleteKey,System.StringComparison.Ordinal);
   if(SmallButton(deleteArmed?"DELETE?":"DELETE")){if(deleteArmed){tiles.PreloadDelete(body.BodyName);preloadDestructiveConfirm="";}else preloadDestructiveConfirm=deleteKey;}
   bool rebuildArmed=string.Equals(preloadDestructiveConfirm,rebuildKey,System.StringComparison.Ordinal);
   if(SmallButton(rebuildArmed?"REBUILD?":"REBUILD")){if(rebuildArmed){tiles.PreloadRebuild(body.BodyName);preloadDestructiveConfirm="";}else preloadDestructiveConfirm=rebuildKey;}
   GUILayout.EndHorizontal();
   GUILayout.EndVertical();
  }

  void DrawAirfieldsPage(){
   EnsureAirfieldStyles();
   var registry=core==null?null:core.Airfields;var land=core==null?null:core.Landing;
   GUILayout.BeginVertical("box");
   string reloadLabel="↻ RELOAD / RESCAN";
   GUILayout.BeginHorizontal();GUILayout.Label("AIRFIELD / RUNWAY CERTIFICATION",GUILayout.Width(210f));
   if(GUILayout.Button(reloadLabel,responsiveButtonStyle,GUILayout.Width(ReadableButtonWidth(reloadLabel,176f)),GUILayout.Height(CompactControlHeight()))){if(registry!=null)registry.RequestManualReload();}
   GUILayout.EndHorizontal();
   if(registry==null){WrappedAirfieldLabel("Registry initializing.");GUILayout.EndVertical();return;}
   GUILayout.EndVertical();
   float airfieldChrome=320f+4f*(CompactControlHeight()-CompactButtonHeight);
   airfieldsScroll=GUILayout.BeginScrollView(airfieldsScroll,false,true,GUILayout.Height(Mathf.Max(140f,rect.height-airfieldChrome)));
   try{
    DrawAirfieldCategory(registry,AERISRunwayCertificationState.Certified,"MOD / USER RUNWAYS — MANUAL",ref settings.AirfieldsUserCalibratedExpanded,true);
    DrawAirfieldCategory(registry,AERISRunwayCertificationState.Certified,"VANILLA RUNWAYS",ref settings.AirfieldsCertifiedExpanded,false);
    DrawAirfieldCategory(registry,AERISRunwayCertificationState.Provisional,"PROVISIONAL — NON-SELECTABLE",ref settings.AirfieldsProvisionalExpanded,false);
    DrawAirfieldCategory(registry,AERISRunwayCertificationState.Failed,"FAILED",ref settings.AirfieldsFailedExpanded,false);
    DrawAirfieldCategory(registry,AERISRunwayCertificationState.Pending,"PENDING",ref settings.AirfieldsPendingExpanded,false);
    DrawAirfieldCategory(registry,AERISRunwayCertificationState.Revalidation,"REVALIDATION",ref settings.AirfieldsRevalidationExpanded,false);
   }finally{GUILayout.EndScrollView();}
  }
  void DrawAirfieldCategory(AERISAirfieldRegistry registry,AERISRunwayCertificationState state,string label,ref bool expanded,bool manualCalibratedOnly){
   EnsureAirfieldStyles();
   int count=CountAirfieldCategory(registry,state,manualCalibratedOnly);
   if(count==0){
    if(expanded){expanded=false;settings.Save();AERISLogger.Info("[AIRFIELDS/UI] category forced collapsed because count=0: "+label);}
    string detailSuffix="|"+state+"|"+manualCalibratedOnly;
    if(!string.IsNullOrEmpty(airfieldDetailId)&&airfieldDetailId.EndsWith(detailSuffix,System.StringComparison.Ordinal))airfieldDetailId="";
    string emptyLabel="▶ "+label+" (0)";
    bool previousEnabled=GUI.enabled;
    GUI.enabled=false;
    try{GUILayout.Button(emptyLabel,responsiveButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()));}
    finally{GUI.enabled=previousEnabled;}
    return;
   }
   string categoryLabel=(expanded?"▼ ":"▶ ")+label+" ("+count+")";
   bool next=GUILayout.Toggle(expanded,categoryLabel,responsiveButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()));
   if(next!=expanded){expanded=next;settings.Save();}
   if(!expanded)return;
   System.Collections.Generic.IList<AERISAirfieldDefinition> airfieldView=registry.Airfields;
   for(int i=0;i<airfieldView.Count;i++){
    AERISAirfieldDefinition airfield=airfieldView[i];if(airfield==null||!registry.IsAirfieldPresentationAvailable(airfield))continue;
    if(state==AERISRunwayCertificationState.Pending&&!manualCalibratedOnly&&registry.IsDlcRunwayPresentationPlaceholder(airfield))DrawDlcRunwayPlaceholder(registry,airfield,state,manualCalibratedOnly);
    for(int j=0;j<airfield.Runways.Count;j++){
     AERISRunwayDefinition runway=airfield.Runways[j];if(runway==null)continue;
     int matching=MatchingDirectionCount(registry,airfield,runway,state,manualCalibratedOnly);if(matching==0)continue;
     string key=runway.StableId+"|"+state+"|"+manualCalibratedOnly;bool detail=string.Equals(airfieldDetailId,key,System.StringComparison.Ordinal);
     string designation=RunwayGroupDesignation(registry,airfield,runway,state,manualCalibratedOnly);
     string status=RunwayGroupStatus(registry,airfield,runway,state,manualCalibratedOnly);
     string row=airfield.DisplayName+"\n"+designation+" | "+runway.LengthMeters.ToString("0")+" m | "+status;
     string rowLabel=(detail?"▼ ":"▶ ")+row;
     float rowHeight=ResponsiveHeight(38f);
     if(GUILayout.Button(rowLabel,airfieldRowButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(rowHeight)))airfieldDetailId=detail?"":key;
     if(detail)DrawAirfieldRunwayGroupDetail(registry,airfield,runway,state,manualCalibratedOnly);
    }
   }
  }
  void DrawDlcRunwayPlaceholder(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   EnsureAirfieldStyles();
   string key=airfield.StableId+"|DLC_PLACEHOLDER|"+state+"|"+manualCalibratedOnly;bool detail=string.Equals(airfieldDetailId,key,System.StringComparison.Ordinal);
   string row=airfield.DisplayName+"\nRWY -- | "+registry.DlcRunwayPresentationText(airfield);
   string rowLabel=(detail?"▼ ":"▶ ")+row;
   float rowHeight=ResponsiveHeight(38f);
   if(GUILayout.Button(rowLabel,airfieldRowButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(rowHeight)))airfieldDetailId=detail?"":key;
   if(!detail)return;
   GUILayout.BeginVertical("box");
   WrappedAirfieldLabel("AIRFIELD: "+airfield.DisplayName);
   WrappedAirfieldLabel(registry.UserRunwayCalibrationSummary(airfield));
   WrappedAirfieldLabel(registry.UserRunwayCalibrationEndpointSummary(airfield));
   GUILayout.BeginHorizontal();
   if(SmallButton("MARK A"))HandleDlcPlaceholderCalibrationMark(registry,airfield,true);
   if(SmallButton("MARK B"))HandleDlcPlaceholderCalibrationMark(registry,airfield,false);
   if(SmallButton("CLEAR")){string message;registry.ClearUserRunwayCalibration(airfield,out message);runwayCalibrationMessage=message;}
   GUILayout.EndHorizontal();
   if(!string.IsNullOrEmpty(runwayCalibrationMessage))WrappedAirfieldLabel(runwayCalibrationMessage);
   WrappedAirfieldLabel("Park at each physical runway end (<= 5 m/s), MARK A then MARK B.");
   GUILayout.EndVertical();
  }
  bool DirectionMatchesCategory(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDirectionDefinition direction,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   if(direction==null||registry.EffectiveState(direction)!=state)return false;
   if(registry.IsSupersededByUserCalibration(airfield,direction))return false;
   bool userCalibrated=direction.CertificationBasis==AERISRunwayCertificationBasis.UserCalibrated;
   bool dlcVanilla=airfield!=null&&airfield.Source==AERISAirfieldSource.Dlc;
   if(state==AERISRunwayCertificationState.Certified){
    if(dlcVanilla)return !manualCalibratedOnly;
    if(userCalibrated!=manualCalibratedOnly)return false;
   }
   return true;
  }
  int MatchingDirectionCount(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDefinition runway,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   int count=0;if(runway==null)return count;
   for(int i=0;i<runway.Directions.Count;i++)if(DirectionMatchesCategory(registry,airfield,runway.Directions[i],state,manualCalibratedOnly))count++;
   return count;
  }
  AERISRunwayDirectionDefinition FirstMatchingDirection(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDefinition runway,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   if(runway==null)return null;
   for(int i=0;i<runway.Directions.Count;i++)if(DirectionMatchesCategory(registry,airfield,runway.Directions[i],state,manualCalibratedOnly))return runway.Directions[i];
   return null;
  }
  string RunwayGroupDesignation(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDefinition runway,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   var labels=new System.Collections.Generic.List<string>();
   for(int i=0;i<runway.Directions.Count;i++){
    AERISRunwayDirectionDefinition direction=runway.Directions[i];if(!DirectionMatchesCategory(registry,airfield,direction,state,manualCalibratedOnly))continue;
    string value=string.IsNullOrEmpty(direction.DisplayName)?"RWY --":direction.DisplayName;
    if(!labels.Contains(value))labels.Add(value);
   }
   labels.Sort(System.StringComparer.Ordinal);
   return labels.Count==0?(string.IsNullOrEmpty(runway.DisplayName)?"RWY --":runway.DisplayName):string.Join(" / ",labels.ToArray());
  }
  string RunwayGroupStatus(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDefinition runway,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   bool allHigh=true;bool any=false;bool userCalibrated=false;string commonFailure="";bool mixedFailure=false;
   for(int i=0;i<runway.Directions.Count;i++){
    AERISRunwayDirectionDefinition direction=runway.Directions[i];if(!DirectionMatchesCategory(registry,airfield,direction,state,manualCalibratedOnly))continue;
    any=true;userCalibrated|=direction.CertificationBasis==AERISRunwayCertificationBasis.UserCalibrated;
    if(direction.GeometryConfidence<0.93||direction.ClassificationConfidence<0.95)allHigh=false;
    string failure=HumanizeIdentifier(direction.FailureCode.ToString()).ToUpperInvariant();
    if(string.IsNullOrEmpty(commonFailure))commonFailure=failure;else if(!string.Equals(commonFailure,failure,System.StringComparison.Ordinal))mixedFailure=true;
   }
   if(!any)return "NONE";
   if(state==AERISRunwayCertificationState.Certified){
    string quality=allHigh?"HIGH":"USABLE";
    if(airfield!=null&&airfield.Source==AERISAirfieldSource.Dlc&&userCalibrated)return "FIELD VERIFIED";
    return userCalibrated?"MANUAL CALIBRATED":quality;
   }
   if(state==AERISRunwayCertificationState.Failed)return mixedFailure?"MULTIPLE FAILURES":commonFailure;
   return state.ToString().ToUpperInvariant();
  }
  int CountAirfieldCategory(AERISAirfieldRegistry registry,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   int count=0;if(registry==null)return count;
   System.Collections.Generic.IList<AERISAirfieldDefinition> airfieldView=registry.Airfields;
   for(int i=0;i<airfieldView.Count;i++){
    AERISAirfieldDefinition airfield=airfieldView[i];if(airfield==null||!registry.IsAirfieldPresentationAvailable(airfield))continue;
    if(state==AERISRunwayCertificationState.Pending&&!manualCalibratedOnly&&registry.IsDlcRunwayPresentationPlaceholder(airfield))count++;
    for(int j=0;j<airfield.Runways.Count;j++)if(MatchingDirectionCount(registry,airfield,airfield.Runways[j],state,manualCalibratedOnly)>0)count++;
   }
   return count;
  }
  void DrawAirfieldRunwayGroupDetail(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDefinition runway,AERISRunwayCertificationState state,bool manualCalibratedOnly){
   EnsureAirfieldStyles();
   GUILayout.BeginVertical("box");
   WrappedAirfieldLabel("AIRFIELD: "+airfield.DisplayName);
   WrappedAirfieldLabel("PHYSICAL RUNWAY: "+RunwayGroupDesignation(registry,airfield,runway,state,manualCalibratedOnly));
   WrappedAirfieldLabel("Runway: "+runway.LengthMeters.ToString("0.0")+" m × "+runway.WidthMeters.ToString("0.0")+" m");
   if(AERISSandboxNativeSpawnWarpUtility.ShouldShow(airfield)){
    string warpReason;bool canWarp=AERISSandboxNativeSpawnWarpUtility.CanWarp(airfield,runway,out warpReason);
    string warpLabel="WARP TO MOD NATIVE SPAWN";
    bool oldEnabled=GUI.enabled;GUI.enabled=canWarp;
    try{if(GUILayout.Button(new GUIContent(warpLabel,canWarp?"Sandbox-only registration utility: move the active vessel to this runway site's provider-native aircraft spawn transform. No approach-direction offset is calculated.":warpReason),airfieldActionButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()))){string message;AERISSandboxNativeSpawnWarpUtility.TryWarp(airfield,runway,out message);runwayWarpMessage=message;}}
    finally{GUI.enabled=oldEnabled;}
    WrappedAirfieldLabel("WARP ACTION: "+runwayWarpMessage);
   }
   for(int i=0;i<runway.Directions.Count;i++){
    AERISRunwayDirectionDefinition direction=runway.Directions[i];if(!DirectionMatchesCategory(registry,airfield,direction,state,manualCalibratedOnly))continue;
    GUILayout.BeginVertical("box");
    WrappedAirfieldLabel(direction.DisplayName+" — COURSE "+direction.HeadingDeg.ToString("000")+"°");
    DrawAirfieldDirectionReadOnlyDetail(airfield,direction,state);
    GUILayout.EndVertical();
   }
   AERISRunwayDirectionDefinition representative=FirstMatchingDirection(registry,airfield,runway,state,manualCalibratedOnly);
   if(representative!=null)DrawAirfieldCalibrationControls(registry,airfield,runway,representative);
   WrappedAirfieldLabel("CALIBRATION ACTION: "+runwayCalibrationMessage);
   GUILayout.EndVertical();
  }
  void DrawAirfieldDirectionReadOnlyDetail(AERISAirfieldDefinition airfield,AERISRunwayDirectionDefinition direction,AERISRunwayCertificationState effectiveState){
   if(effectiveState==AERISRunwayCertificationState.Certified)WrappedAirfieldLabel("RUNWAY READY");
   else if(effectiveState==AERISRunwayCertificationState.Failed)WrappedAirfieldLabel("RUNWAY NOT AVAILABLE");
   else WrappedAirfieldLabel("RUNWAY NOT READY");
  }
  void DrawAirfieldCalibrationControls(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,AERISRunwayDefinition runway,AERISRunwayDirectionDefinition direction){
   GUILayout.Space(3);
   WrappedAirfieldLabel("RUNWAY PLACEMENT VERIFICATION / TWO-POINT CALIBRATION — park on the physical runway. This never controls the vessel.");
   WrappedAirfieldLabel(registry.UserRunwayCalibrationSummary(airfield));bool manualCalibrated=direction.CertificationBasis==AERISRunwayCertificationBasis.UserCalibrated;
   if(manualCalibrated){WrappedAirfieldLabel("MANUAL A/B CALIBRATION IS AUTHORITATIVE AND PROTECTED. CHECK HERE IS DISABLED FOR THIS ENTRY. USE CLEAR EXPLICITLY BEFORE REPLACING THE ENDPOINTS.");}
   else if(airfield.Source!=AERISAirfieldSource.Stock){WrappedAirfieldLabel("NON-STOCK RUNWAY: AUTOMATIC / PROVIDER GEOMETRY IS NOT OPERATIONAL AUTHORITY. USE MARK A AND MARK B AT THE TWO PHYSICAL RUNWAY ENDS.");}
   else{string checkLabel="CHECK HERE — VERIFY CURRENT VESSEL AGAINST THIS RUNWAY";if(GUILayout.Button(checkLabel,airfieldActionButtonStyle,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()))){string message;registry.VerifyRunwayPlacement(airfield,runway,direction,FlightGlobals.ActiveVessel,out message);runwayCalibrationMessage=message;}}
   GUILayout.BeginHorizontal();if(SmallButton("MARK A"))HandleRunwayCalibrationMark(registry,airfield,true);if(SmallButton("MARK B"))HandleRunwayCalibrationMark(registry,airfield,false);if(SmallButton("CLEAR")){string message;registry.ClearUserRunwayCalibration(airfield,out message);runwayCalibrationMessage=message;}GUILayout.EndHorizontal();
  }
  void HandleDlcPlaceholderCalibrationMark(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,bool thresholdA){
   string message;bool ok=registry.MarkUserRunwayCalibration(airfield,thresholdA,FlightGlobals.ActiveVessel,out message);runwayCalibrationMessage=message;
   // Keep the DLC placeholder detail open after either point. Candidate 11 is a
   // field-capture bridge: a complete A/B pair may exist before KSP exposes a
   // runtime runway object, so do not redirect into an empty certified category.
   if(ok){settings.AirfieldsPendingExpanded=true;settings.Save();}
  }
  void HandleRunwayCalibrationMark(AERISAirfieldRegistry registry,AERISAirfieldDefinition airfield,bool thresholdA){
   string message;bool ok=registry.MarkUserRunwayCalibration(airfield,thresholdA,FlightGlobals.ActiveVessel,out message);runwayCalibrationMessage=message;
   if(!ok||!registry.HasStoredUserCalibration(airfield))return;
   settings.AirfieldsUserCalibratedExpanded=true;
   settings.AirfieldsCertifiedExpanded=false;
   settings.AirfieldsProvisionalExpanded=false;
   settings.AirfieldsFailedExpanded=false;
   settings.AirfieldsPendingExpanded=false;
   settings.AirfieldsRevalidationExpanded=false;
   airfieldsScroll=Vector2.zero;
   airfieldDetailId="";
   settings.Save();
  }
  void EnsureAirfieldStyles(){
   if(wrappedLabelStyle==null){wrappedLabelStyle=new GUIStyle(GUI.skin.label);wrappedLabelStyle.wordWrap=false;wrappedLabelStyle.clipping=TextClipping.Clip;wrappedLabelStyle.stretchWidth=true;wrappedLabelStyle.stretchHeight=false;wrappedLabelStyle.fixedHeight=0f;}
   if(airfieldRowButtonStyle==null){airfieldRowButtonStyle=new GUIStyle(GUI.skin.button);airfieldRowButtonStyle.wordWrap=false;airfieldRowButtonStyle.clipping=TextClipping.Clip;airfieldRowButtonStyle.alignment=TextAnchor.MiddleLeft;airfieldRowButtonStyle.padding=new RectOffset(6,6,4,4);airfieldRowButtonStyle.stretchHeight=false;airfieldRowButtonStyle.fixedHeight=0f;}
   if(airfieldActionButtonStyle==null){airfieldActionButtonStyle=new GUIStyle(GUI.skin.button);airfieldActionButtonStyle.wordWrap=false;airfieldActionButtonStyle.clipping=TextClipping.Clip;airfieldActionButtonStyle.alignment=TextAnchor.MiddleCenter;airfieldActionButtonStyle.padding=new RectOffset(6,6,4,4);airfieldActionButtonStyle.stretchHeight=false;airfieldActionButtonStyle.fixedHeight=0f;}
   if(responsiveButtonStyle==null){responsiveButtonStyle=new GUIStyle(GUI.skin.button);responsiveButtonStyle.wordWrap=false;responsiveButtonStyle.clipping=TextClipping.Clip;responsiveButtonStyle.alignment=TextAnchor.MiddleCenter;responsiveButtonStyle.padding=new RectOffset(6,6,3,3);responsiveButtonStyle.stretchHeight=false;responsiveButtonStyle.fixedHeight=0f;}
  }
  void WrappedAirfieldLabel(string text){EnsureAirfieldStyles();GUILayout.Label(text??string.Empty,wrappedLabelStyle,GUILayout.ExpandWidth(true));}
  float ResponsiveWidth(float baseline){
   float baseContent=Mathf.Max(1f,AERISSettings.DefaultMainWindowWidth-34f);
   float currentContent=Mathf.Max(1f,rect.width-34f);
   return Mathf.Max(1f,baseline*(currentContent/baseContent));
  }
  float ResponsiveHeight(float baseline){
   float ratio=rect.height/Mathf.Max(1f,AERISSettings.DefaultMainWindowHeight);
   return Mathf.Clamp(baseline*ratio,baseline*0.82f,baseline*1.35f);
  }
  float CompactControlHeight(){
   EnsureAirfieldStyles();
   float textHeight=GUI.skin.button.CalcSize(new GUIContent("Ag")).y+4f;
   return Mathf.Max(textHeight,ResponsiveHeight(CompactButtonHeight));
  }
  float ReadableButtonWidth(string label,float baseline){
   EnsureAirfieldStyles();
   float textWidth=GUI.skin.button.CalcSize(new GUIContent(label??string.Empty)).x+14f;
   return Mathf.Max(textWidth,ResponsiveWidth(baseline));
  }
  bool CompactFullRowButton(string label){
   EnsureAirfieldStyles();
   return GUILayout.Button(label,responsiveButtonStyle,GUILayout.ExpandWidth(true),
    GUILayout.Height(CompactControlHeight()));
  }
  bool CompactFullRowToggle(string label,bool value){
   EnsureAirfieldStyles();
   return GUILayout.Toggle(value,label,responsiveButtonStyle,GUILayout.ExpandWidth(true),
    GUILayout.Height(CompactControlHeight()));
  }
  static string HumanizeIdentifier(string value){
   if(string.IsNullOrEmpty(value))return string.Empty;
   System.Text.StringBuilder output=new System.Text.StringBuilder(value.Length+8);
   for(int i=0;i<value.Length;i++){
    char current=value[i];
    if(current=='_'||current=='-'){if(output.Length>0&&output[output.Length-1]!=' ')output.Append(' ');continue;}
    if(i>0&&char.IsUpper(current)&&(char.IsLower(value[i-1])||char.IsDigit(value[i-1])))output.Append(' ');
    output.Append(current);
   }
   return output.ToString();
  }
  static string UiBuildTitle(string suffix){return "AERIS v"+AERISBuildVersion.Semantic+" "+AERISBuildVersion.UiCheckpoint+" — "+suffix;}
  void DrawDisplayModeSelector(string label,ref AERISDisplayMode mode){
   GUILayout.BeginHorizontal();GUILayout.Label(label+" display",GUILayout.Width(150f));
   int selected=GUILayout.SelectionGrid((int)mode,new string[]{"AUTO","ALWAYS","OFF"},3,GUILayout.Width(ResponsiveWidth(270f)),GUILayout.Height(CompactControlHeight()));
   GUILayout.EndHorizontal();
   AERISDisplayMode next=(AERISDisplayMode)Mathf.Clamp(selected,0,2);
   if(next!=mode){mode=next;settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] "+label+" display mode="+next);}
  }
  void DrawTerrainQualitySelector(){GUILayout.BeginHorizontal();GUILayout.Label("Terrain quality",GUILayout.Width(150f));bool oldEnabled=GUI.enabled;try{GUI.enabled=false;GUILayout.Button("LOW  (LOCKED)",GUILayout.Width(ResponsiveWidth(BaseSelectorWidth)),GUILayout.Height(CompactControlHeight()));}finally{GUI.enabled=oldEnabled;}GUILayout.EndHorizontal();}
  void DrawTerrainModeSelector(){GUILayout.BeginHorizontal();GUILayout.Label("Terrain mode",GUILayout.Width(150f));int selected=GUILayout.SelectionGrid((int)settings.TerrainDisplayMode,new string[]{"AUTO","TOPO","REL","OFF"},4,GUILayout.Width(ResponsiveWidth(BaseSelectorWidth)),GUILayout.Height(CompactControlHeight()));GUILayout.EndHorizontal();var next=(AERISTerrainDisplayMode)Mathf.Clamp(selected,0,3);if(next!=settings.TerrainDisplayMode){settings.TerrainDisplayMode=next;settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Terrain mode="+next);}}
  void DrawTerrainGpuSelector(){GUILayout.BeginHorizontal();GUILayout.Label("Terrain GPU",GUILayout.Width(150f));int selected=GUILayout.SelectionGrid((int)settings.TerrainGpuMode,new string[]{"AUTO","ON","OFF"},3,GUILayout.Width(ResponsiveWidth(BaseSelectorWidth)),GUILayout.Height(CompactControlHeight()));GUILayout.EndHorizontal();var next=(AERISTerrainGpuMode)Mathf.Clamp(selected,0,2);if(next!=settings.TerrainGpuMode){settings.TerrainGpuMode=next;settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Terrain GPU="+next);}}
  void DrawTerrainColourSelector(){GUILayout.BeginHorizontal();GUILayout.Label("Terrain colours",GUILayout.Width(150f));int selected=GUILayout.SelectionGrid((int)settings.TerrainColourPreset,new string[]{"STD","RG","BY","HIGH"},4,GUILayout.Width(ResponsiveWidth(BaseSelectorWidth)),GUILayout.Height(CompactControlHeight()));GUILayout.EndHorizontal();var next=(AERISTerrainColourPreset)Mathf.Clamp(selected,0,3);if(next!=settings.TerrainColourPreset){settings.TerrainColourPreset=next;settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Terrain colours="+next);}}
  void DrawFlightDataArchiveLimitSelector(){GUILayout.Label("Verified flight ZIP limit   (default 10 / range 1-30)");int current=AERISSettings.NormalizeFlightDataArchiveLimit(settings.FlightDataArchiveLimit)-1;int selected=GUILayout.SelectionGrid(current,FlightArchiveLimitLabels,10,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()*3f));if(selected>=0&&selected<30){int next=selected+1;if(next!=settings.FlightDataArchiveLimit){settings.FlightDataArchiveLimit=next;settings.Save();AERISFlightDataArchive.ConfigureRetention(next);AERISLogger.Info("[SYSTEM/OPTIONS] FDR/CVR verified ZIP limit="+next);}}}
  void ToggleOption(ref bool value,string label){bool next=GUILayout.Toggle(value,label);if(next!=value){value=next;settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] "+label+"="+next);}}
  void DrawShortcutBinding(string label,AERISShortcutAction action){GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(150f));GUILayout.Label(settings.GetShortcutDisplay(action),GUILayout.ExpandWidth(true));bool capturing=hotkeyCaptureAction==(int)action;if(GUILayout.Button(capturing?"CAPTURE":"SET",GUILayout.Width(ResponsiveWidth(76f)),GUILayout.Height(CompactControlHeight())))BeginShortcutCapture(action);if(GUILayout.Button("CLEAR",GUILayout.Width(ResponsiveWidth(58f)),GUILayout.Height(CompactControlHeight()))){settings.ClearShortcut(action);settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Shortcut cleared: "+label);}GUILayout.EndHorizontal();}
  void BeginShortcutCapture(AERISShortcutAction action){hotkeyCaptureAction=(int)action;capturePrimary=KeyCode.None;captureSecondary=KeyCode.None;AERISLogger.Info("[SYSTEM/OPTIONS] Shortcut capture started: "+action);}
  void DrawShortcutCaptureBox(){GUILayout.BeginVertical("box");var action=(AERISShortcutAction)hotkeyCaptureAction;GUILayout.Label("Capturing "+ShortcutLabel(action)+": "+(capturePrimary==KeyCode.None?"press first key":capturePrimary+(captureSecondary==KeyCode.None?" — press optional second key or APPLY":(" + "+captureSecondary))));GUILayout.Label("ENTER applies a one-key or two-key binding. BACKSPACE clears the second key. ESC cancels.");GUILayout.BeginHorizontal();if(SmallButton("APPLY")){string error;if(settings.TrySetShortcut(action,capturePrimary,captureSecondary,out error)){settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Shortcut set: "+ShortcutLabel(action)+" = "+settings.GetShortcutDisplay(action));hotkeyCaptureAction=-1;}else AERISLogger.Warn("[SYSTEM/OPTIONS] Shortcut rejected: "+error);}if(SmallButton("CANCEL")){hotkeyCaptureAction=-1;capturePrimary=KeyCode.None;captureSecondary=KeyCode.None;}GUILayout.EndHorizontal();GUILayout.EndVertical();}
  void ConsumeShortcutCaptureInput(){if(hotkeyCaptureAction<0)return;Event e=Event.current;if(e==null||e.type!=EventType.KeyDown)return;if(e.keyCode==KeyCode.Escape){hotkeyCaptureAction=-1;capturePrimary=KeyCode.None;captureSecondary=KeyCode.None;e.Use();return;}if(e.keyCode==KeyCode.Return||e.keyCode==KeyCode.KeypadEnter){if(capturePrimary!=KeyCode.None){string error;if(settings.TrySetShortcut((AERISShortcutAction)hotkeyCaptureAction,capturePrimary,captureSecondary,out error)){settings.Save();AERISLogger.Info("[SYSTEM/OPTIONS] Shortcut set: "+ShortcutLabel((AERISShortcutAction)hotkeyCaptureAction)+" = "+settings.GetShortcutDisplay((AERISShortcutAction)hotkeyCaptureAction));hotkeyCaptureAction=-1;}else AERISLogger.Warn("[SYSTEM/OPTIONS] Shortcut rejected: "+error);}e.Use();return;}if(e.keyCode==KeyCode.Backspace||e.keyCode==KeyCode.Delete){captureSecondary=KeyCode.None;e.Use();return;}if(capturePrimary==KeyCode.None)capturePrimary=e.keyCode;else if(captureSecondary==KeyCode.None&&e.keyCode!=capturePrimary)captureSecondary=e.keyCode;else if(e.keyCode!=capturePrimary)captureSecondary=e.keyCode;e.Use();}
  static string ShortcutLabel(AERISShortcutAction action){return action==AERISShortcutAction.ToggleMaster?"Toggle MASTER":(action==AERISShortcutAction.EmergencyDisable?"Emergency disable":"Toggle window");}
  void ApplySettingsGeometry(){rect.width=ClampWidth(settings.MainWindowWidth);rect.height=ClampHeight(settings.MainWindowHeight);rect.x=settings.MainWindowX;rect.y=settings.MainWindowY;ClampRectToScreen();Visible=true;}

  void DrawFbw(){
   var m=core.Manager; if(m==null){GUILayout.Label("Awaiting active-vessel AA manager."); return;}
   var s=m.Standard; var p=m.PitchController; var r=m.RollController; var y=m.YawController;
   if(s==null||p==null||r==null||y==null){GUILayout.Label("AA FBW modules are not initialized yet. Turn MASTER ON once in flight.");return;}
   GUILayout.Label("AA Standard Fly-By-Wire");
   ToggleValue(s.moderation_switch,"Moderation (AoA + G, pitch/yaw)",v=>s.moderation_switch=v);
   ToggleValue(s.coord_turn,"Coordinated turn",v=>s.coord_turn=v);
   GUILayout.BeginVertical("box"); GUILayout.Label("Pitch / AoA / G");
   Toggle(ref p.moderate_aoa,"AoA moderation"); Toggle(ref p.moderate_g,"G moderation");
   Slider(ref p.max_aoa,"Max AoA",3f,45f,"F1"); Slider(ref p.max_g_force,"Max G",1f,30f,"F1");
   Slider(ref p.max_v_construction,"Pitch response",0.05f,3.0f,"F2");
   GUILayout.EndVertical();
   GUILayout.BeginVertical("box"); GUILayout.Label("Roll");
   Toggle(ref r.wing_leveler,"Wing leveler"); Slider(ref r.leveler_snap_angle,"Leveler snap angle",0.5f,15f,"F1"); Slider(ref r.snapping_Kp,"Leveler gain",0.02f,1.00f,"F2"); Slider(ref r.max_v_construction,"Roll response",0.1f,6.0f,"F2");
   GUILayout.EndVertical();
   GUILayout.BeginVertical("box"); GUILayout.Label("Yaw");
   Toggle(ref y.moderate_aoa,"Yaw AoA moderation"); Toggle(ref y.moderate_g,"Yaw G moderation"); Slider(ref y.max_v_construction,"Yaw response",0.05f,3.0f,"F2");
   GUILayout.EndVertical();
   advanced=GUILayout.Toggle(advanced," Advanced AA parameters");
   if(advanced){GUILayout.BeginVertical("box"); Slider(ref p.user_input_deriv_clamp,"Pitch input derivative limit",0.1f,12f,"F2"); Slider(ref r.user_input_deriv_clamp,"Roll input derivative limit",0.1f,12f,"F2"); Slider(ref y.user_input_deriv_clamp,"Yaw input derivative limit",0.1f,12f,"F2"); Slider(ref p.relaxation_Kp,"Pitch relaxation Kp",0.0f,2f,"F2"); Slider(ref r.relaxation_Kp,"Roll relaxation Kp",0.0f,2f,"F2"); Slider(ref y.relaxation_Kp,"Yaw relaxation Kp",0.0f,2f,"F2"); GUILayout.EndVertical();}
   GUILayout.Space(3);
   GUILayout.BeginVertical("box");
   GUILayout.Label("CURRENT AIRCRAFT — FBW RESET");
   GUILayout.Label("Restores visible AA FBW settings to the bundled AERIS/AA mod defaults. AERIS learning profiles are not present.");
   if(!confirmResetAllFbw){
    if(ActionButton("Reset FBW Values")) confirmResetAllFbw=true;
   } else {
    GUILayout.Label("Reset all visible FBW settings for the current aircraft to mod defaults?");
    GUILayout.BeginHorizontal();
    if(SmallButton("RESET NOW")){ core.ResetAllFbwValues(); confirmResetAllFbw=false; }
    if(SmallButton("CANCEL")) confirmResetAllFbw=false;
    GUILayout.EndHorizontal();
   }
   GUILayout.EndVertical();
  }

  void DrawProtect(){
   var p=core.Protect;
   GUILayout.Label("AERIS PROTECT — fixed-wing protection");
   if(p==null){GUILayout.Label("Protect initializing.");return;}

   GUILayout.BeginVertical("box");
   GUILayout.Label("1. ANTI-STALL / ENVELOPE PROTECTION");
   bool stallDetection=GUILayout.Toggle(p.AntiStallEnabled,"Enable stall detection");
   if(stallDetection!=p.AntiStallEnabled){p.AntiStallEnabled=stallDetection;AERISLogger.Info("PROTECT/AntiStall enabled="+stallDetection);}
   bool thrustAssist=GUILayout.Toggle(p.ThrustAssistEnabled,"Enable adaptive thrust assist");
   if(thrustAssist!=p.ThrustAssistEnabled){p.ThrustAssistEnabled=thrustAssist;AERISLogger.Info("PROTECT/ThrustAssist enabled="+thrustAssist);}
   GUILayout.EndVertical();

   GUILayout.BeginVertical("box");
   GUILayout.Label("2. GROUND ASSIST — PROTECT");
   var ground=core.GroundStability;
   bool groundMaster=GUILayout.Toggle(settings.GroundAssistEnabled,"GROUND ASSIST MASTER (default ON)");
   if(groundMaster!=settings.GroundAssistEnabled){settings.GroundAssistEnabled=groundMaster;settings.Save();AERISLogger.Info("[GROUND_ASSIST] master="+groundMaster);}
   if(ground==null)GUILayout.Label("Ground Assist initializing.");
   else {
    bool groundEnabled=GUILayout.Toggle(ground.Enabled,"GROUND STABILITY (default ON)");
    if(groundEnabled!=ground.Enabled){ground.Enabled=groundEnabled;settings.GroundStabilityEnabled=groundEnabled;settings.Save();}
    bool brakeAuto=GUILayout.Toggle(settings.GroundBrakeAssistAuto,"BRAKE ASSIST — AUTO");
    if(brakeAuto!=settings.GroundBrakeAssistAuto){settings.GroundBrakeAssistAuto=brakeAuto;settings.Save();}
    bool airLink=GUILayout.Toggle(settings.GroundAirbrakeLinkAuto,"AIRBRAKE LINK — AUTO");
    if(airLink!=settings.GroundAirbrakeLinkAuto){settings.GroundAirbrakeLinkAuto=airLink;settings.Save();}
    bool chute=GUILayout.Toggle(settings.GroundDragChuteAuto,"DRAG CHUTE — AUTO (Brakes-group chute)");
    if(chute!=settings.GroundDragChuteAuto){settings.GroundDragChuteAuto=chute;settings.Save();}
    bool reverse=GUILayout.Toggle(settings.GroundReverseThrustAuto,"THRUST REVERSE — AUTO (default ON / provider required)");
    if(reverse!=settings.GroundReverseThrustAuto){settings.GroundReverseThrustAuto=reverse;settings.Save();}
    bool parking=GUILayout.Toggle(settings.GroundParkingHold,"PARKING HOLD after stop (default ON)");
    if(parking!=settings.GroundParkingHold){settings.GroundParkingHold=parking;settings.Save();}
    GroundSettingSlider(ref settings.GroundTargetDecelerationMps2,"TARGET DECEL",0.5f,12f,"F1");
    GroundSettingSlider(ref settings.GroundMaxBrake,"MAX BRAKE",0f,1f,"F2");
    GroundSettingSlider(ref settings.GroundAirbrakeLimit,"AIRBRAKE LIMIT",0f,1f,"F2");
    GroundSettingSlider(ref settings.GroundLowSpeedFadeMps,"LOW SPEED FADE",2f,30f,"F1");
    if(ground.ParkingHoldActive)GUILayout.Label("Press the stock Brakes action once to release Parking Hold.");
   }
   GUILayout.EndVertical();

   GUILayout.BeginVertical("box");
   GUILayout.Label("3. LANDING CONFIGURATION / AUTO GEAR");
   bool autoGear=GUILayout.Toggle(p.AutoGearEnabled,"Enable Auto Gear — extend <95m / retract >105m radar altitude");
   if(autoGear!=p.AutoGearEnabled){p.AutoGearEnabled=autoGear;AERISLogger.Info("PROTECT/AutoGear enabled="+autoGear);}
   if(p.AutoGearPilotOverride)GUILayout.Label("Pilot override is latched. Re-arm: toggle Auto Gear OFF then ON, or cycle MASTER.");
   GUILayout.EndVertical();
  }

  void DrawAutopilot(){
   GUILayout.Label("AERIS AUTOPILOT — TAKEOFF / FLIGHT / NAV / LAND");
   autopilotPage=DrawAutopilotCategoryTabs(autopilotPage);
   GUILayout.Space(3);
   bool drawAutopilotEnabled=GUI.enabled;
   var takeoff=core.AutoTakeoff;
   if(autopilotPage==0){DrawAutoTakeoffPage(takeoff);GUI.enabled=drawAutopilotEnabled;return;}
   if(autopilotPage==2){DrawFlightPlanLibrary();GUI.enabled=drawAutopilotEnabled;return;}
   if(autopilotPage==3){DrawLandingFoundation();GUI.enabled=drawAutopilotEnabled;return;}

   var bank=core.Bank;
   var hdg=core.Hdg;
   if(bank==null){GUILayout.Label("Autopilot directors initializing.");GUI.enabled=drawAutopilotEnabled;return;}

   if(core.AnyNormalApArmed)GUILayout.Label("FLIGHT "+(core.NormalApExecutionPermitted?"ACTIVE — ":"ARMED / WAITING FOR CONFIRMED LIFTOFF — ")+core.ArmedApModesText);
   else GUILayout.Label("FLIGHT: no prepared post-takeoff modes.");
   bool normalControlsLocked=takeoff!=null && takeoff.Executing;
   if(normalControlsLocked)GUILayout.Label("FLIGHT controls are locked during Auto Takeoff; the prepared reservation is preserved.");
   GUI.enabled=drawAutopilotEnabled&&!normalControlsLocked;


   GUILayout.BeginVertical("box");
   GUILayout.Label("LATERAL — FLIGHT");
   bool oldLateralGui=GUI.enabled;
   bool lateralOn=bank.Armed || (hdg!=null && hdg.Armed);
   GUILayout.BeginHorizontal();
   if(GUILayout.Button(settings.ApLateralSectionExpanded?"▼":"▶",GUILayout.Width(ResponsiveWidth(28f)),GUILayout.Height(CompactControlHeight()))){settings.ApLateralSectionExpanded=!settings.ApLateralSectionExpanded;settings.Save();}
   GUI.enabled=oldLateralGui;
   bool requestedLateral=WideAxisSwitch("LATERAL",lateralOn);
   GUI.enabled=oldLateralGui;
   GUILayout.Space(ResponsiveWidth(28f));
   GUILayout.EndHorizontal();
   if(requestedLateral!=lateralOn){
    if(!requestedLateral){
     if(hdg!=null) hdg.SetArmed(false,FlightGlobals.ActiveVessel,bank,core.Attitude); bank.Disable("LATERAL permit OFF");
    } else if(lateralMode==0) {
     bank.SetArmed(true,FlightGlobals.ActiveVessel);
    } else if(lateralMode==1 && hdg!=null) {
     hdg.SetArmed(true,FlightGlobals.ActiveVessel,bank,core.Attitude);
    }
   }
   if(settings.ApLateralSectionExpanded){
   GUI.enabled=oldLateralGui;
   int displayedLateralMode=lateralMode;

   GUILayout.BeginHorizontal();
   if(ModeSelectButton("BANK",displayedLateralMode==0,true)){
    lateralMode=0;
    if(lateralOn){ if(hdg!=null) hdg.SetArmed(false,FlightGlobals.ActiveVessel,bank,core.Attitude); bank.SetArmed(true,FlightGlobals.ActiveVessel); }
   }
   if(ModeSelectButton("HDG",displayedLateralMode==1,true)){
    lateralMode=1;
    if(lateralOn && hdg!=null){ bank.Disable("HDG mode selected"); hdg.SetArmed(true,FlightGlobals.ActiveVessel,bank,core.Attitude); }
   }
   GUILayout.EndHorizontal();
   GUILayout.Space(3);

   // Shared lateral detail area: the mode buttons select which compact content is shown here.
   if(displayedLateralMode==0){
    GUILayout.Label("Current bank: "+bank.CurrentBank.ToString("+0.0;-0.0;0.0")+"°");
    if(settings.ShowApErrorDisplay) GUILayout.Label("Bank error: "+bank.BankError.ToString("+0.0;-0.0;0.0")+"°");
    GUILayout.BeginHorizontal();
    GUILayout.Label("Target bank:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth)));
    bank.TargetBankText=AERISNumericField.TextField(bank.TargetBankText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth)));
    GUILayout.EndHorizontal();
    GUILayout.BeginHorizontal();
    if(ActionButton("APPLY")){string err; if(!bank.TrySetTarget(bank.TargetBankText,out err))AERISLogger.Warn("[BANK] target rejected: "+err);else SaveBankInput(bank);}
    if(ActionButton("SET CURRENT")){bank.SetCurrent(FlightGlobals.ActiveVessel);SaveBankInput(bank);}
    GUILayout.EndHorizontal();
   } else if(displayedLateralMode==1){
    if(hdg==null){GUILayout.Label("HDG director initializing.");}
    else {
     GUILayout.Label("Current HDG: "+hdg.CurrentHeading.ToString("000.0")+"°");
     if(settings.ShowApErrorDisplay) GUILayout.Label("HDG error: "+hdg.HeadingError.ToString("+0.0;-0.0;0.0")+"°");
     GUILayout.BeginHorizontal();
     GUILayout.Label("Target HDG:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth)));
     hdg.TargetHeadingText=AERISNumericField.TextField(hdg.TargetHeadingText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth)));
     GUILayout.EndHorizontal();
     GUILayout.BeginHorizontal();
     if(ActionButton("APPLY")){string err; if(!hdg.TrySetTarget(hdg.TargetHeadingText,out err))AERISLogger.Warn("[HDG] target rejected: "+err);else SaveHdgInput(hdg);}
     if(ActionButton("SET CURRENT")){hdg.SetCurrent(core.Attitude);SaveHdgInput(hdg);}
     GUILayout.EndHorizontal();
     GUILayout.Space(2);
     GUILayout.BeginHorizontal();
     if(ActionButton("BANK LIMIT: AUTO")){hdg.SetAutoMaxBankLimit();SaveHdgInput(hdg);}
     GUILayout.Label(hdg.UseAutoMaxBankLimit ? ("AUTO " + hdg.EffectiveMaxBankLimitDeg.ToString("0.0") + "°") : ("MANUAL " + hdg.ManualMaxBankLimitDeg.ToString("0.0") + "°"),GUILayout.Width(ResponsiveWidth(ActionButtonWidth)));
     GUILayout.EndHorizontal();
     GUILayout.BeginHorizontal();
     GUILayout.Label("Max bank:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth)));
     hdg.ManualMaxBankLimitText=AERISNumericField.TextField(hdg.ManualMaxBankLimitText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth)));
     if(SmallButton("SET")){ string limErr; if(!hdg.TrySetManualMaxBankLimit(hdg.ManualMaxBankLimitText,out limErr)) AERISLogger.Warn("[HDG] bank limit rejected: "+limErr); else SaveHdgInput(hdg); }
     GUILayout.EndHorizontal();
     bool thinAirAssist=GUILayout.Toggle(settings.ThinAirTurnAssistEnabled,"Enable roll-first high-energy turn (AUTO: 80° hard max, continuous margin + measured-G capability)");
     if(thinAirAssist!=settings.ThinAirTurnAssistEnabled){settings.ThinAirTurnAssistEnabled=thinAirAssist;hdg.ThinAirTurnAssistEnabled=thinAirAssist;settings.Save();AERISLogger.Info("[HDG] adaptive high-G turn assist="+thinAirAssist);}
     GUILayout.Label("Energy turn: "+hdg.ThinAirTurnQualificationStatus+" | phase "+hdg.ThinAirTurnPhase+" | G cmd/target/measured "+hdg.ThinAirTurnCommandedG.ToString("0.00")+" / "+hdg.ThinAirTurnTargetG.ToString("0.00")+" / "+hdg.ThinAirTurnMeasuredG.ToString("0.00"));
     GUILayout.Label("Stability "+hdg.ThinAirTurnStabilityScore.ToString("0.00")+" | stall authority "+hdg.ThinAirTurnStallAuthority.ToString("0.00")+" | G tracking "+hdg.ThinAirTurnTrackingAuthority.ToString("0.00")+" | response(diag) "+hdg.ThinAirTurnResponseRatio.ToString("0.00"));
     GUILayout.Label("Bank plan "+hdg.ThinAirTurnBankTargetDeg.ToString("0.0")+"° / 80° max | pitch assist "+hdg.ThinAirTurnPitchAssistRateDegPerSec.ToString("0.00")+"°/s | release "+hdg.ThinAirTurnReleaseReason);
     GUILayout.Label("AA limit "+(hdg.ThinAirAaLimitHoldActive?"HOLD":"clear")+" "+hdg.ThinAirAaLimitHoldReason+" | req/applied "+hdg.ThinAirAaPitchRequestedDegPerSec.ToString("0.00")+" / "+hdg.ThinAirAaPitchAppliedDegPerSec.ToString("0.00")+"°/s | authority "+hdg.ThinAirAaPitchAuthority.ToString("0.00"));
     GUILayout.Label("Margin "+(hdg.ThinAirMarginGovernorActive?"LIMIT":"clear")+" "+hdg.ThinAirMarginGovernorReason+" | now/pred/rate "+hdg.ThinAirTurnObservedStallMarginDeg.ToString("0.00")+" / "+hdg.ThinAirPredictedStallMarginDeg.ToString("0.00")+"° / "+hdg.ThinAirStallMarginRateDegPerSec.ToString("0.00")+"°/s | authority "+hdg.ThinAirMarginGovernorAuthority.ToString("0.00")+" recover "+hdg.ThinAirMarginRecoveryElapsedSeconds.ToString("0.0")+"s");
     GUILayout.Label("G capability sustainable/cap/error "+hdg.ThinAirEstimatedSustainableG.ToString("0.00")+" / "+hdg.ThinAirCapabilityGCap.ToString("0.00")+" / "+hdg.ThinAirCapabilityTrackingErrorG.ToString("0.00")+"G | "+(hdg.ThinAirCapabilityLimited?"LIMITED":"tracking")+" | bank cap "+hdg.ThinAirCapabilityBankCapDeg.ToString("0.0")+"°");
     GUILayout.Label("Low-q envelope "+hdg.ThinAirLowQEnvelopeBlend.ToString("0.00")+" | dynamic caps BANK/G/PITCH "+hdg.ThinAirLowQBankCapDeg.ToString("0.0")+"° / "+hdg.ThinAirLowQGCap.ToString("0.00")+"G / "+hdg.ThinAirLowQPitchCapDegPerSec.ToString("0.00")+"°/s | bank slew "+hdg.ThinAirBankTargetSlewLimitDegPerSec.ToString("0.00")+"°/s | stall recovery "+(hdg.ThinAirStallRecoveryActive?"ACTIVE":"clear"));
     GUILayout.Label("Yaw "+hdg.YawAssistMode+" | turn fade "+hdg.ThinAirTurnYawBankFade.ToString("0.00")+" | turn/stability "+hdg.ThinAirTurnYawRateTargetDegPerSec.ToString("0.00")+" / "+hdg.AttitudeStabilityYawRateCommandDegPerSec.ToString("0.00")+"°/s");
     GUILayout.Label("Pitch kinematic/floor/fb "+hdg.ThinAirTurnPitchKinematicRateDegPerSec.ToString("0.00")+" / "+hdg.ThinAirTurnPitchFloorRateDegPerSec.ToString("0.00")+" / "+hdg.ThinAirTurnPitchFeedbackRateDegPerSec.ToString("0.00")+"°/s | rollout lead "+hdg.ThinAirTurnRolloutLeadDeg.ToString("0.0")+"°");
     GUILayout.Label("Entry gates: ALT "+(hdg.ThinAirTurnAltitudeQualified?"OK":"NO")+" | SPEED "+(hdg.ThinAirTurnSpeedQualified?"OK":"NO")+" | STALL MARGIN "+(hdg.ThinAirTurnStallMarginQualified?"OK":"NO")+" | HDG ERROR "+(hdg.ThinAirTurnHeadingErrorQualified?"OK":"NO")+" (entry >=3000 m, >=600 m/s, margin >=5°, HDG >=20°)");
    }
   }
   }
   GUI.enabled=oldLateralGui;
   GUILayout.EndVertical();

   GUILayout.BeginVertical("box");
   GUILayout.Label("VERTICAL — FLIGHT");
   var pitch=core.Pitch;
   var vs=core.VerticalSpeed;
   var alt=core.Altitude;
   int displayedVerticalMode=verticalMode;
   bool displayedVerticalMaster=verticalMaster;
   bool oldVerticalGui=GUI.enabled;
   GUILayout.BeginHorizontal();
   if(GUILayout.Button(settings.ApVerticalSectionExpanded?"▼":"▶",GUILayout.Width(ResponsiveWidth(28f)),GUILayout.Height(CompactControlHeight()))){settings.ApVerticalSectionExpanded=!settings.ApVerticalSectionExpanded;settings.Save();}
   GUI.enabled=oldVerticalGui;
   bool requestedVertical=WideAxisSwitch("VERTICAL",displayedVerticalMaster);
   GUI.enabled=oldVerticalGui;
   GUILayout.Space(ResponsiveWidth(28f));
   GUILayout.EndHorizontal();
   if(requestedVertical!=verticalMaster){
    verticalMaster=requestedVertical;
    ApplyVerticalMode(pitch,vs,alt);
   }
   if(settings.ApVerticalSectionExpanded){
   GUI.enabled=oldVerticalGui;
   GUILayout.BeginHorizontal();
   if(ModeSelectButton("PITCH",displayedVerticalMode==0,true)){ verticalMode=0; ApplyVerticalMode(pitch,vs,alt); }
   if(ModeSelectButton("V/S",displayedVerticalMode==1,true)){ verticalMode=1; ApplyVerticalMode(pitch,vs,alt); }
   if(ModeSelectButton("ALT",displayedVerticalMode==2,true)){ verticalMode=2; ApplyVerticalMode(pitch,vs,alt); }
   GUILayout.EndHorizontal();
   GUILayout.Space(2);
   if(displayedVerticalMode==0){
    if(pitch==null){GUILayout.Label("PITCH director initializing.");}
    else {
     GUILayout.Label("Current pitch: "+pitch.CurrentPitch.ToString("+0.0;-0.0;0.0")+"°");
     if(settings.ShowApErrorDisplay) GUILayout.Label("Pitch error: "+pitch.PitchError.ToString("+0.0;-0.0;0.0")+"°");
     GUILayout.BeginHorizontal(); GUILayout.Label("Target pitch:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); pitch.TargetPitchText=AERISNumericField.TextField(pitch.TargetPitchText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); GUILayout.EndHorizontal();
     GUILayout.BeginHorizontal(); if(ActionButton("APPLY")){string err; if(!pitch.TrySetTarget(pitch.TargetPitchText,out err))AERISLogger.Warn("[PITCH] target rejected: "+err);else SavePitchInput(pitch);}
     if(ActionButton("SET CURRENT")){pitch.SetCurrent(core.Attitude);SavePitchInput(pitch);} GUILayout.EndHorizontal();
    }
   } else if(displayedVerticalMode==1){
    if(vs==null){GUILayout.Label("V/S director initializing.");}
    else {
     GUILayout.Label("Current V/S: "+vs.CurrentVerticalSpeedMps.ToString("+0.0;-0.0;0.0")+" m/s");
     if(settings.ShowApErrorDisplay) GUILayout.Label("V/S error: "+vs.VerticalSpeedErrorMps.ToString("+0.0;-0.0;0.0")+" m/s");
     GUILayout.Label("Pitch target: "+vs.GeneratedPitchTargetDeg.ToString("+0.0;-0.0;0.0")+"°");
     GUILayout.BeginHorizontal(); GUILayout.Label("Target V/S:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); vs.TargetVerticalSpeedText=AERISNumericField.TextField(vs.TargetVerticalSpeedText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); GUILayout.EndHorizontal();
     GUILayout.BeginHorizontal(); if(ActionButton("APPLY")){string err; if(!vs.TrySetTarget(vs.TargetVerticalSpeedText,out err))AERISLogger.Warn("[V/S] target rejected: "+err);else SaveVsInput(vs);}
     if(ActionButton("SET CURRENT")){vs.SetCurrent(core.Attitude);SaveVsInput(vs);} GUILayout.EndHorizontal();
     GUILayout.Space(3);
     GUILayout.BeginHorizontal(); GUILayout.Label("Max pitch:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); vs.MaxPitchTargetText=AERISNumericField.TextField(vs.MaxPitchTargetText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); if(SmallButton("SET LIMIT")){string limitErr; if(!vs.TrySetMaxPitchTarget(vs.MaxPitchTargetText,out limitErr))AERISLogger.Warn("[V/S] max pitch rejected: "+limitErr);else SaveVsInput(vs);} GUILayout.EndHorizontal();
     GUILayout.Label("V/S pitch limit: ±"+vs.MaxPitchTargetDeg.ToString("0.0")+"°");
     if(vs.VsCruiseAccelerationGuideActive) GUILayout.Label("V/S ACC GUIDE: blend "+vs.VsCruiseAccelerationGuideBlend.ToString("0.00")+" | a* "+vs.VsCruiseDesiredVerticalAccelerationMps2.ToString("+0.000;-0.000;0.000")+" m/s² | BasePitch "+vs.VsCruiseAppliedBasePitchRateDegPerSec.ToString("+0.000;-0.000;0.000")+"°/s"+(vs.VsCruisePreBrakeActive?" | PRE-BRAKE":""));
     if(vs.HighQNonZeroVsTrackingProfileActive) GUILayout.Label("HIGH-Q V/S TRACK STAB: q "+vs.DynamicPressureKpa.ToString("0.0")+" kPa | D scale "+vs.HighQNonZeroVsTrackingDampingScale.ToString("0.00")+" | rate slew "+vs.HighQNonZeroVsTrackingRateCommandSlewScale.ToString("0.00"));
     if(vs.VerticalTrackingRateEnvelopeActive) GUILayout.Label("V/S TRACK ENVELOPE: q "+vs.DynamicPressureKpa.ToString("0.0")+" kPa | rate cap "+vs.VerticalTrackingRateLimitDegPerSec.ToString("0.0")+"°/s | slew "+vs.VerticalTrackingRateSlewDegPerSec2.ToString("0.0")+(vs.VerticalTrackingRateReversalGateActive?" | REV GATE":""));
    }
   } else {
    if(alt==null){GUILayout.Label("ALT director initializing.");}
    else {
     GUILayout.Label("Current ALT: "+alt.CurrentAltitudeMeters.ToString("0.00")+" m ASL");
     GUILayout.Label("Display-safe band: "+alt.AltitudeHoldBandLowerMeters.ToString("0.00")+"–"+alt.AltitudeHoldBandUpperMeters.ToString("0.00")+" m | reference "+alt.AltitudeHoldReferenceMeters.ToString("0.000"));
     if(settings.ShowApErrorDisplay) GUILayout.Label("ALT center error: "+alt.AltitudeControlErrorMeters.ToString("+0.00;-0.00;0.00")+" m | band "+(alt.AltitudeInsidePreferredHoldBand?"IN":(alt.AltitudeHoldBandErrorMeters>0f?"LOW":"HIGH"))+" | "+alt.ControlState);
     GUILayout.Label("Planned V/S: "+alt.PlannedVerticalSpeedMps.ToString("+0.0;-0.0;0.0")+" m/s | V/S actual: "+alt.CurrentVerticalSpeedMps.ToString("+0.0;-0.0;0.0")+" m/s");
     GUILayout.BeginHorizontal(); GUILayout.Label("Target ALT:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); alt.TargetAltitudeText=AERISNumericField.TextField(alt.TargetAltitudeText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); GUILayout.EndHorizontal();
     GUILayout.BeginHorizontal(); if(ActionButton("APPLY")){string err; if(!alt.TrySetTarget(alt.TargetAltitudeText,out err))AERISLogger.Warn("[ALT] target rejected: "+err);else SaveAltInput(alt);}
     if(ActionButton("SET CURRENT")){alt.SetCurrent(FlightGlobals.ActiveVessel);SaveAltInput(alt);} GUILayout.EndHorizontal();
     GUILayout.Space(2);
     GUILayout.BeginHorizontal(); GUILayout.Label("ALT max V/S:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); alt.MaxAltitudeVerticalSpeedText=AERISNumericField.TextField(alt.MaxAltitudeVerticalSpeedText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); if(SmallButton("SET LIMIT")){string limitErr; if(!alt.TrySetMaxVerticalSpeed(alt.MaxAltitudeVerticalSpeedText,out limitErr))AERISLogger.Warn("[ALT] V/S limit rejected: "+limitErr);else SaveAltInput(alt);} GUILayout.EndHorizontal();
     GUILayout.BeginHorizontal(); GUILayout.Label("ALT pitch:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); alt.MaxAltitudePitchText=AERISNumericField.TextField(alt.MaxAltitudePitchText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); if(SmallButton("SET LIMIT")){string limitErr; if(!alt.TrySetMaxPitch(alt.MaxAltitudePitchText,out limitErr))AERISLogger.Warn("[ALT] pitch limit rejected: "+limitErr);else SaveAltInput(alt);} GUILayout.EndHorizontal();
     GUILayout.Label("ALT V/S limit: ±"+alt.MaxAltitudeVerticalSpeedMps.ToString("0.0")+" m/s | ALT pitch limit: ±"+alt.MaxAltitudePitchDeg.ToString("0.0")+"° | Effective cap: ±"+(vs!=null?vs.EffectiveMaxPitchTargetDeg:alt.MaxAltitudePitchDeg).ToString("0.0")+"°");
     if(vs!=null && vs.VsCruiseAccelerationGuideActive) GUILayout.Label("V/S ACC GUIDE: blend "+vs.VsCruiseAccelerationGuideBlend.ToString("0.00")+" | a* "+vs.VsCruiseDesiredVerticalAccelerationMps2.ToString("+0.000;-0.000;0.000")+" m/s² | BasePitch "+vs.VsCruiseAppliedBasePitchRateDegPerSec.ToString("+0.000;-0.000;0.000")+"°/s"+(vs.VsCruisePreBrakeActive?" | PRE-BRAKE":""));
     if(vs!=null && vs.HighQNonZeroVsTrackingProfileActive) GUILayout.Label("HIGH-Q V/S TRACK STAB: q "+vs.DynamicPressureKpa.ToString("0.0")+" kPa | D scale "+vs.HighQNonZeroVsTrackingDampingScale.ToString("0.00")+" | rate slew "+vs.HighQNonZeroVsTrackingRateCommandSlewScale.ToString("0.00"));
     if(vs!=null && vs.VerticalTrackingRateEnvelopeActive) GUILayout.Label("V/S TRACK ENVELOPE: q "+vs.DynamicPressureKpa.ToString("0.0")+" kPa | rate cap "+vs.VerticalTrackingRateLimitDegPerSec.ToString("0.0")+"°/s | slew "+vs.VerticalTrackingRateSlewDegPerSec2.ToString("0.0")+(vs.VerticalTrackingRateReversalGateActive?" | REV GATE":""));
     if(alt.AoAClimbGovernorActive) GUILayout.Label("ALT AoA CLIMB GOV: "+alt.AoAClimbGovernorAoADeg.ToString("0.0")+"° | V/S cap "+alt.AoAClimbGovernorAppliedVsCapMps.ToString("0.0")+" m/s");
     else GUILayout.Label("ALT AoA climb governor: standby | AoA "+alt.AoAClimbGovernorAoADeg.ToString("0.0")+"°");
     GUILayout.Label("Stop distance: "+alt.StopDistanceMeters.ToString("0.0")+" m");
    }
   }
   }
   GUI.enabled=oldVerticalGui;
   GUILayout.EndVertical();

   GUILayout.BeginVertical("box");
   GUILayout.Label("SPEED — FLIGHT");
   var acc=core.Acceleration;
   var vel=core.Velocity;
   bool displayedSpeedMaster=speedMaster;
   int displayedSpeedMode=speedMode;
   bool oldSpeedGui=GUI.enabled;
   GUILayout.BeginHorizontal();
   if(GUILayout.Button(settings.ApSpeedSectionExpanded?"▼":"▶",GUILayout.Width(ResponsiveWidth(28f)),GUILayout.Height(CompactControlHeight()))){settings.ApSpeedSectionExpanded=!settings.ApSpeedSectionExpanded;settings.Save();}
   GUI.enabled=oldSpeedGui;
   bool requestedSpeed=WideAxisSwitch("SPEED",displayedSpeedMaster);
   GUI.enabled=oldSpeedGui;
   GUILayout.Space(ResponsiveWidth(28f));
   GUILayout.EndHorizontal();
   if(requestedSpeed!=speedMaster){
    speedMaster=requestedSpeed;
    ApplySpeedMode(acc,vel);
   }
   if(settings.ApSpeedSectionExpanded){
   GUI.enabled=oldSpeedGui;
   GUILayout.BeginHorizontal();
   if(ModeSelectButton("ACC",displayedSpeedMode==0,true)){ speedMode=0; ApplySpeedMode(acc,vel); }
   if(ModeSelectButton("VEL",displayedSpeedMode==1,true)){ speedMode=1; ApplySpeedMode(acc,vel); }
   GUILayout.EndHorizontal();
   GUILayout.Space(2);
   if(acc==null){GUILayout.Label("SPEED directors initializing.");}
   else if(displayedSpeedMode==0){
    GUILayout.Label("Surface speed: "+acc.CurrentSurfaceSpeedMps.ToString("0.0")+" m/s | Accel: "+acc.FilteredAccelerationMps2.ToString("+0.00;-0.00;0.00")+" m/s²");
    if(settings.ShowApErrorDisplay) GUILayout.Label("ACC error: "+acc.AccelerationErrorMps2.ToString("+0.00;-0.00;0.00")+" m/s² | "+acc.ControlState);
    if(acc.AccelerationLimitState!="NONE") GUILayout.Label("ACC LIMIT: "+acc.AccelerationLimitState+" | residual: "+acc.AccelerationErrorMps2.ToString("+0.00;-0.00;0.00")+" m/s²");
    GUILayout.BeginHorizontal(); GUILayout.Label("Target ACC:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); acc.TargetAccelerationText=AERISNumericField.TextField(acc.TargetAccelerationText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); GUILayout.Label("m/s²"); GUILayout.EndHorizontal();
    GUILayout.BeginHorizontal();
    if(ActionButton("APPLY")){string err; if(!acc.TrySetTarget(acc.TargetAccelerationText,out err))AERISLogger.Warn("[ACC] target rejected: "+err);else SaveAccInput(acc);}
    if(ActionButton("SET 0")){acc.SetZeroTarget();SaveAccInput(acc);}
    GUILayout.EndHorizontal();
    GUILayout.Label("Base THR: "+acc.BaseThrottle.ToString("0.000")+" | Demand: "+acc.ThrottleDemand.ToString("0.000")+" | q: "+acc.DynamicPressureKpa.ToString("0.0")+" kPa");
    if(acc.ZeroAccelerationFineTrimActive) GUILayout.Label("ACC 0 HOLD TRIM: "+acc.ZeroAccelerationFineTrimAdaptation.ToString("+0.00000;-0.00000;0.00000")+" / tick");
    GUILayout.Label("ACC: direct target acceleration. VEL is available as the upper speed trajectory director.");
   } else if(vel==null){GUILayout.Label("VEL director initializing.");}
   else {
    GUILayout.Label("Surface speed: "+vel.CurrentSurfaceSpeedMps.ToString("0.0")+" m/s | VEL state: "+vel.ControlState);
    if(settings.ShowApErrorDisplay) GUILayout.Label("VEL error: "+vel.VelocityErrorMps.ToString("+0.00;-0.00;0.00")+" m/s | ACC plan: "+vel.PublishedAccelerationMps2.ToString("+0.00;-0.00;0.00")+" m/s²");
    GUILayout.BeginHorizontal(); GUILayout.Label("Target VEL:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); vel.TargetSurfaceSpeedText=AERISNumericField.TextField(vel.TargetSurfaceSpeedText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); GUILayout.Label("m/s"); GUILayout.EndHorizontal();
    GUILayout.BeginHorizontal();
    if(ActionButton("APPLY")){string err; if(!vel.TrySetTarget(vel.TargetSurfaceSpeedText,out err))AERISLogger.Warn("[VEL] target rejected: "+err);else SaveVelInput(vel);}
    if(ActionButton("SET CURRENT")){vel.SetCurrent(core.Attitude);SaveVelInput(vel);}
    GUILayout.EndHorizontal();
    GUILayout.BeginHorizontal(); GUILayout.Label("VEL ACC LIMIT:",GUILayout.Width(ResponsiveWidth(CompactLabelWidth))); vel.AccelerationLimitText=AERISNumericField.TextField(vel.AccelerationLimitText,GUILayout.Width(ResponsiveWidth(CompactFieldWidth))); GUILayout.Label("m/s²",GUILayout.Width(34));
    if(SmallButton("SET LIMIT")){string limitErr; if(!vel.TrySetAccelerationLimit(vel.AccelerationLimitText,out limitErr))AERISLogger.Warn("[VEL] acceleration limit rejected: "+limitErr); else SaveVelInput(vel);} GUILayout.EndHorizontal();
    GUILayout.Label("VEL accel cap: ±"+vel.ConfiguredAccelerationLimitMps2.ToString("0.0")+" m/s² (before q schedule)");
    if(!vel.TargetConfirmed) GUILayout.Label("VEL SAFETY: set a target or SET CURRENT before throttle control begins.");
    GUILayout.Label("Plan: "+vel.PlannedAccelerationMps2.ToString("+0.00;-0.00;0.00")+" m/s² | ACC: "+acc.FilteredAccelerationMps2.ToString("+0.00;-0.00;0.00")+" m/s² | q: "+vel.DynamicPressureKpa.ToString("0.0")+" kPa");
    GUILayout.Label("Base THR: "+acc.BaseThrottle.ToString("0.000")+" | Demand: "+acc.ThrottleDemand.ToString("0.000")+" | Hold: "+vel.VelocityHoldActive);
    if(acc.AccelerationLimitState!="NONE") GUILayout.Label("ACC LIMIT: "+acc.AccelerationLimitState+" | residual: "+acc.AccelerationErrorMps2.ToString("+0.00;-0.00;0.00")+" m/s²");
    GUILayout.Label("VEL: jerk-limited speed trajectory -> ACC.");
   }
   GUI.enabled=oldSpeedGui;
   GUILayout.Space(3);
   var speedAirbrake=core.SpeedAirbrake;
   bool autoAirbrake=GUILayout.Toggle(settings.SpeedAutomaticAirbrakeEnabled,"Enable SPEED automatic airbrake (variable angle; wheel brakes untouched)");
   if(autoAirbrake!=settings.SpeedAutomaticAirbrakeEnabled){settings.SpeedAutomaticAirbrakeEnabled=autoAirbrake;if(speedAirbrake!=null){speedAirbrake.Enabled=autoAirbrake;if(!autoAirbrake)speedAirbrake.Release("user disabled");}settings.Save();AERISLogger.Info("[SPEED][AIRBRAKE] enabled="+autoAirbrake);}
   if(speedAirbrake!=null)GUILayout.Label("AIRBRAKE: "+speedAirbrake.Status+" | request/applied "+(speedAirbrake.RequestedDemand*100f).ToString("0")+"/"+(speedAirbrake.AppliedDemand*100f).ToString("0")+"% | angle "+speedAirbrake.AppliedAngleDegrees.ToString("0.0")+"° | surfaces "+speedAirbrake.EligibleSurfaceCount+" | q limit "+speedAirbrake.DynamicPressureLimitScale.ToString("0.00"));
   }
   GUI.enabled=oldSpeedGui;
   GUILayout.EndVertical();

   GUILayout.BeginVertical("box");
   GUILayout.Label("STATUS");
   string lateralState=!lateralOn?"OFF":(lateralMode==0?"BANK":"HDG");
   string speedState=!speedMaster?"OFF":(speedMode==0?"ACC":"VEL");
   GUILayout.Label("Lateral: "+lateralState+" | Vertical: "+(verticalMaster?(verticalMode==0?"PITCH":(verticalMode==1?"V/S":"ALT")):"OFF")+" | Speed: "+speedState);
   GUILayout.EndVertical();
   GUI.enabled=drawAutopilotEnabled;
  }
  int DrawAutopilotCategoryTabs(int selected){
   string[] labels={"TAKEOFF","FLIGHT","NAV","LAND"};
   bool[] active={
    core.AutoTakeoff!=null&&(core.AutoTakeoff.Armed||core.AutoTakeoff.Executing),
    core.AnyNormalApArmed,
    false,
    core.Landing!=null&&core.Landing.Armed
   };
   EnsureAirfieldStyles();
   float height=CompactControlHeight();
   Rect row=GUILayoutUtility.GetRect(1f,height,GUILayout.ExpandWidth(true),GUILayout.Height(height));
   float gap=TopTabGap;
   float width=Mathf.Max(1f,(row.width-gap*3f)/4f);
   for(int i=0;i<4;i++){
    Rect buttonRect=new Rect(row.x+i*(width+gap),row.y,width,row.height);
    Color old=GUI.backgroundColor;
    try{
     GUI.backgroundColor=active[i]?new Color(0.12f,0.75f,0.16f,1f):new Color(0.78f,0.12f,0.12f,1f);
     if(GUI.Toggle(buttonRect,selected==i,labels[i],responsiveButtonStyle))selected=i;
    }finally{GUI.backgroundColor=old;}
   }
   return selected;
  }

  void DrawAutoTakeoffPage(AERISAutoTakeoffDirector takeoff){
   GUILayout.BeginVertical("box");
   GUILayout.Label("AUTO TAKEOFF — TWO-STEP ARM / EXECUTE");
   if(takeoff==null)GUILayout.Label("Auto Takeoff initializing.");
   else {
    GUILayout.Label("Phase: "+takeoff.PhaseText+" | "+takeoff.Status);
    GUILayout.Label("Propulsion: "+takeoff.PropulsionMode+" | "+takeoff.EngineStageStatus);
    GUILayout.Label("Attempt generation: "+takeoff.AttemptGeneration+" | Takeoff thrust: MAX (1.000 fixed)");
    GUILayout.Label("Brakes: "+takeoff.BrakeStatus+(takeoff.BrakeWasAppliedAtExecute?" | pre-EXECUTE parking brake was ON":""));
    GUILayout.Label("Stall estimate: "+(takeoff.SelectedStallSpeedMps>0f?takeoff.SelectedStallSpeedMps.ToString("0.0")+" m/s":"N/A")+" | Vr: "+takeoff.SelectedVrMps.ToString("0.0")+" m/s | "+takeoff.SelectedVrSource+(takeoff.VrFrozen?" (FROZEN)":""));
    if(!string.IsNullOrEmpty(takeoff.SelectedVrDetail))GUILayout.Label("Vr detail: "+takeoff.SelectedVrDetail);
    GUILayout.Label("Speed/radar/V/S: "+takeoff.SurfaceSpeedMps.ToString("0.0")+" m/s / "+takeoff.RadarAltitudeM.ToString("0.0")+" m / "+takeoff.VerticalSpeedMps.ToString("+0.0;-0.0;0.0")+" m/s");
    if(takeoff.Phase==AutoTakeoffPhase.GroundRoll)GUILayout.Label("Rotation gate: "+takeoff.RotationGateReason);
    GUILayout.BeginHorizontal();
    if(!takeoff.Armed){if(GUILayout.Button("ARM AUTO TAKEOFF",GUILayout.Width(ReadableButtonWidth("ARM AUTO TAKEOFF",160f)),GUILayout.Height(CompactControlHeight()))){string err;if(!core.ArmAutoTakeoff(out err))AERISLogger.Warn("[AUTO_TAKEOFF] ARM rejected: "+err);}}
    else if(takeoff.Phase==AutoTakeoffPhase.Armed){if(GUILayout.Button("EXECUTE TAKEOFF",GUILayout.Width(ReadableButtonWidth("EXECUTE TAKEOFF",160f)),GUILayout.Height(CompactControlHeight()))){string err;if(!core.ExecuteAutoTakeoff(out err))AERISLogger.Warn("[AUTO_TAKEOFF] EXECUTE rejected: "+err);}if(SmallButton("DISARM"))core.DisarmAutoTakeoff("user disarm");}
    else if(GUILayout.Button("ABORT / TRANSFER",GUILayout.Width(ReadableButtonWidth("ABORT / TRANSFER",160f)),GUILayout.Height(CompactControlHeight())))core.DisarmAutoTakeoff("user abort / transfer");
    GUILayout.EndHorizontal();
    GUILayout.Space(2);
    if(CompactFullRowButton((takeoffConfigExpanded?"▼ ":"▶ ")+"TAKEOFF CONFIGURATION"))
     takeoffConfigExpanded=!takeoffConfigExpanded;
    if(takeoffConfigExpanded){
     bool configEnabled=GUI.enabled;GUI.enabled=configEnabled&&!takeoff.Executing;
     TakeoffSettingSlider(ref settings.AutoTakeoffStallFactor,"AA stall factor",1.05f,1.50f,"F2");
     TakeoffSettingSlider(ref settings.AutoTakeoffManualVrMps,"Fallback Vr m/s",15f,250f,"F0");
     TakeoffSettingSlider(ref settings.AutoTakeoffRotationPitchDeg,"Rotation pitch deg",3f,20f,"F1");
     TakeoffSettingSlider(ref settings.AutoTakeoffRotationRateDegPerSec,"Pitch-rate max deg/s",0.5f,5f,"F1");
     TakeoffSettingSlider(ref settings.AutoTakeoffInitialClimbVsMps,"Initial V/S m/s",2f,50f,"F0");
     TakeoffSettingSlider(ref settings.AutoTakeoffHandoffRadarAltitudeM,"Handoff radar m",20f,250f,"F0");
     GUI.enabled=configEnabled;
    }
    GUILayout.Label("Gear and flaps remain manual. Brake input aborts. Strong sustained input transfers control; Ground Stability remains independent.");
   }
   GUILayout.EndVertical();
  }

  void DrawLandingFoundation(){
   GUILayout.BeginVertical("box");
   GUILayout.Label("LAND — INDEPENDENT AIRFIELD REGISTRY / FIRST GATE");
   var registry=core.Airfields;
   var land=core.Landing;
   if(registry==null || land==null){GUILayout.Label("LAND foundation initializing.");GUILayout.EndVertical();return;}
   GUILayout.BeginHorizontal();
   if(GUILayout.Button(settings.LandSectionExpanded?"▼":"▶",GUILayout.Width(ResponsiveWidth(28f)),GUILayout.Height(CompactControlHeight()))){settings.LandSectionExpanded=!settings.LandSectionExpanded;settings.Save();}
   bool armed=land.Armed;
   Color oldArmColor=GUI.backgroundColor;
   try{
    GUI.backgroundColor=armed?new Color(0.95f,0.72f,0.16f,1f):oldArmColor;
    GUILayout.Label("LAND "+(armed?"ARMED":"STANDBY")+" — CONTROL "+land.ControlText+" / LOC "+land.LocalizerText+" / GS "+land.GlidePathText,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()));
   }finally{GUI.backgroundColor=oldArmColor;}
   GUILayout.EndHorizontal();
   if(!settings.LandSectionExpanded){GUILayout.EndVertical();return;}

   GUILayout.Label("Registry: "+registry.Status);
   GUILayout.Label(registry.SourceSummary);
   GUILayout.Label("KSP: "+registry.KspProviderStatus);
   GUILayout.Label("KK: "+registry.KerbalKonstructsProviderStatus);
   GUILayout.Label("SURVEY: "+registry.RunwaySurveyStatus);

   landAirfieldScroll=GUILayout.BeginScrollView(landAirfieldScroll,GUILayout.Height(142f));
   try{
    System.Collections.Generic.IList<AERISAirfieldDefinition> airfieldView=registry.Airfields;
    for(int i=0;i<airfieldView.Count;i++){
     AERISAirfieldDefinition airfield=airfieldView[i]; if(airfield==null||!registry.IsAirfieldPresentationAvailable(airfield))continue;
     if(registry.SelectableDirectionCount(airfield)==0)continue;
     bool selected=i==registry.SelectedAirfieldIndex;
     Color old=GUI.backgroundColor;
     try{
      if(selected)GUI.backgroundColor=new Color(0.12f,0.65f,0.85f,1f);
      string label=(selected?"▶ ":"   ")+airfield.DisplayName+"  ["+airfield.Source.ToString().ToUpperInvariant()+"]  "+registry.ValidationText(airfield);
      bool oldEnabled=GUI.enabled;GUI.enabled=oldEnabled&&!land.Armed;
      if(GUILayout.Button(label,GUI.skin.button,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight())))if(registry.SelectAirfield(i))landMessage="Selected "+airfield.DisplayName;
      GUI.enabled=oldEnabled;
     }finally{GUI.backgroundColor=old;}
    }
   }finally{GUILayout.EndScrollView();}

   AERISAirfieldDefinition selectedAirfield=registry.SelectedAirfield;
   if(selectedAirfield==null){
    GUILayout.Label("Select a runway.");
   }else{
    GUILayout.Label("Selected runway: "+selectedAirfield.DisplayName+" | "+selectedAirfield.Body);
    GUILayout.Label("Validation: "+registry.ValidationText(selectedAirfield)+" | Provider: "+selectedAirfield.ProviderRuntimeStatus);
    if(!string.IsNullOrEmpty(selectedAirfield.Description))GUILayout.Label(selectedAirfield.Description);
    if(registry.SelectedDirectionCount>0){
     GUILayout.Label("RUNWAY DIRECTION — each physical direction is an independent approach");
     landDirectionScroll=GUILayout.BeginScrollView(landDirectionScroll,GUILayout.Height(58f));
     try{
      GUILayout.BeginHorizontal();
      for(int i=0;i<registry.SelectedDirectionCount;i++){
       AERISRunwayDirectionDefinition direction=registry.SelectableDirectionAt(selectedAirfield,i); if(direction==null)continue;
       bool selected=i==registry.SelectedDirectionIndex;
       Color old=GUI.backgroundColor;
       try{
        if(selected)GUI.backgroundColor=new Color(0.12f,0.75f,0.16f,1f);
        bool oldEnabled=GUI.enabled;GUI.enabled=oldEnabled&&!land.Armed;
        if(GUILayout.Button(direction.DisplayName+"  "+direction.HeadingDeg.ToString("000.0")+"°",GUI.skin.button,GUILayout.Width(ReadableButtonWidth(direction.DisplayName+"  "+direction.HeadingDeg.ToString("000.0")+"°",160f)),GUILayout.Height(CompactControlHeight())))if(registry.SelectDirection(i))landMessage="Selected "+direction.DisplayName;
        GUI.enabled=oldEnabled;
       }finally{GUI.backgroundColor=old;}
      }
      GUILayout.EndHorizontal();
     }finally{GUILayout.EndScrollView();}
    }else{
     GUILayout.Label("RUNWAY GEOMETRY REQUIRED — LAND ARM INHIBITED");
    }
   }

   AERISRunwayDirectionDefinition selectedDirection=registry.SelectedDirection;
   AERISRunwayDefinition selectedRunway=registry.SelectedRunway;
   if(selectedDirection!=null){
    GUILayout.Label("Approach: "+selectedDirection.DisplayName+" | HDG "+selectedDirection.HeadingDeg.ToString("000.0")+"° | GP "+selectedDirection.GlidePathAngleDeg.ToString("0.0")+"°");
    GUILayout.Label("Geometry bearing "+selectedDirection.ThresholdBearingDeg.ToString("000.0")+"° | heading error "+selectedDirection.HeadingGeometryErrorDeg.ToString("0.0")+"°");
    if(selectedDirection.GeometryDirectionAutoCorrected)GUILayout.Label("RECIPROCAL ENDPOINT ORDER AUTO-CORRECTED");
    if(!selectedDirection.HeadingMatchesGeometry)GUILayout.Label("RUNWAY GEOMETRY INVALID — LAND ARM INHIBITED");
    if(selectedRunway!=null)GUILayout.Label("Runway: "+selectedRunway.DisplayName+" | "+selectedRunway.LengthMeters.ToString("0")+" m × "+selectedRunway.WidthMeters.ToString("0")+" m | "+selectedRunway.Surface);
   }

   if(!land.Armed){
    bool oldArmEnabled=GUI.enabled;
    GUI.enabled=oldArmEnabled&&selectedDirection!=null&&selectedDirection.HasCertifiedGeometry&&selectedDirection.HeadingMatchesGeometry;
    if(CompactFullRowButton("ARM LAND — OBSERVE")){
     string error;
     if(core.ArmLanding(out error))landMessage="LAND ARMED — CONTROL REMAINS PILOT";
     else{landMessage=error;AERISLogger.Warn("[LAND_FOUNDATION] ARM rejected: "+error);}
    }
    GUI.enabled=oldArmEnabled;
   }else if(CompactFullRowButton("DISARM LAND")){
    core.DisarmLanding("user disarm");
    landMessage="LAND DISARMED";
   }
   GUILayout.Label("Status: "+land.Status,GUILayout.ExpandWidth(true));

   AERISRunwayObservation observation=land.Observation;
   if(observation!=null && observation.Valid){
    GUILayout.Label("Threshold "+observation.DistanceToThresholdMeters.ToString("0")+" m | XTK "+observation.CrossTrackMeters.ToString("+0;-0;0")+" m | Intercept "+observation.InterceptAngleDeg.ToString("0.0")+"°");
    if(observation.OnApproachSide)GUILayout.Label("GP target "+observation.GlidePathTargetAltitudeMeters.ToString("0")+" m ASL | error "+observation.GlidePathErrorMeters.ToString("+0;-0;0")+" m");
    else GUILayout.Label("GP target N/A | NOT ON APPROACH SIDE");
    GUILayout.Label("Capture geometry: LOC "+(observation.OnApproachSide?(observation.LocalizerGeometryEligible?"ELIGIBLE":"WAIT"):"N/A")+" | GS "+(observation.OnApproachSide?(observation.GlidePathGeometryEligible?"ELIGIBLE":"WAIT"):"N/A"));
    GUILayout.Label("Reason: "+observation.InhibitReason);
   }else if(observation!=null && !string.IsNullOrEmpty(observation.InhibitReason)){
    GUILayout.Label("Reason: "+observation.InhibitReason);
   }
   if(!string.IsNullOrEmpty(landMessage))GUILayout.Label("UI: "+landMessage);
   GUILayout.Label("FIRST GATE SAFETY: display and geometry observation only. No AP, throttle, brake, airbrake, Ground Assist or FlightCtrlState command is issued.");
   GUILayout.EndVertical();
  }

  void DrawFlightPlanLibrary(){
   GUILayout.BeginVertical("box");
   GUILayout.Label("NAV — RESET / FLIGHT PLAN LIBRARY");
   GUILayout.Label("CONTROL UNAVAILABLE — INDEPENDENT LAND DEVELOPMENT IN PROGRESS");
   GUILayout.Label("This library is data-only. Selecting a plan cannot arm AP modes or write FlightCtrlState.");
   var library=core.FlightPlans;
   if(library==null){GUILayout.Label("Flight plan library initializing.");GUILayout.EndVertical();return;}
   GUILayout.BeginHorizontal();
   if(GUILayout.Button(settings.FlightPlanSectionExpanded?"▼":"▶",GUILayout.Width(ResponsiveWidth(28f)),GUILayout.Height(CompactControlHeight()))){settings.FlightPlanSectionExpanded=!settings.FlightPlanSectionExpanded;settings.Save();}
   bool oldEnabled=GUI.enabled;
   GUI.enabled=false;
   GUILayout.Toggle(false,"NAV ARM  UNAVAILABLE",GUI.skin.button,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()));
   GUI.enabled=oldEnabled;
   GUILayout.EndHorizontal();
   if(!settings.FlightPlanSectionExpanded){GUILayout.EndVertical();return;}
   GUILayout.Label("Library: "+library.Status+" | "+library.Count+" plan(s)");
   navPlanScroll=GUILayout.BeginScrollView(navPlanScroll,GUILayout.Height(150f));
   try{
    for(int i=0;i<library.Count;i++){
     AERISFlightPlanDefinition plan=library.At(i); if(plan==null)continue;
     bool selected=i==library.SelectedIndex;
     Color old=GUI.backgroundColor;
     try{
      if(selected)GUI.backgroundColor=new Color(0.12f,0.65f,0.85f,1f);
      string label=(selected?"▶ ":"   ")+plan.Name+"  ["+(string.IsNullOrEmpty(plan.Body)?"ANY":plan.Body)+"]  "+plan.Fixes.Count+" FIX";
      if(GUILayout.Button(label,GUI.skin.button,GUILayout.ExpandWidth(true),GUILayout.Height(CompactControlHeight()))){if(library.Select(i))navLibraryMessage="Selected "+plan.Name+" (data only)";}
     }finally{GUI.backgroundColor=old;}
    }
   }finally{GUILayout.EndScrollView();}
   AERISFlightPlanDefinition selectedPlan=library.Selected;
   if(selectedPlan!=null){
    GUILayout.Label("Selected: "+selectedPlan.Name+" | body "+(string.IsNullOrEmpty(selectedPlan.Body)?"ANY":selectedPlan.Body)+" | "+selectedPlan.Fixes.Count+" fix(es)");
    if(!string.IsNullOrEmpty(selectedPlan.Description))GUILayout.Label(selectedPlan.Description);
    GUILayout.Label("Source: "+selectedPlan.SourcePath);
   }else GUILayout.Label("Selected: NONE");
   GUILayout.BeginHorizontal();
   if(SmallButton("RELOAD")){library.Reload();navLibraryMessage=library.Status;}
   bool saved=GUI.enabled;GUI.enabled=false;
   SmallButton("ARM NAV");
   GUI.enabled=saved;
   GUILayout.EndHorizontal();
   if(!string.IsNullOrEmpty(navLibraryMessage))GUILayout.Label("Status: "+navLibraryMessage);
   GUILayout.Label("No route, leg, arc, vertical, speed, landing or integrity guidance exists in Phase 1.");
   GUILayout.EndVertical();
  }

  void ApplyVerticalMode(AERISPitchDirector pitch,AERISVerticalSpeedDirector vs,AERISAltitudeDirector alt){
   var vessel=FlightGlobals.ActiveVessel;
   if(!verticalMaster){
    if(alt!=null)alt.SetArmed(false,vessel,core.Attitude,vs,pitch);
    if(vs!=null)vs.SetArmed(false,vessel,core.Attitude,pitch);
    if(pitch!=null)pitch.SetArmed(false,vessel,core.Attitude);
    return;
   }
   if(verticalMode==0){
    if(alt!=null)alt.SetArmed(false,vessel,core.Attitude,vs,pitch);
    if(vs!=null)vs.SetArmed(false,vessel,core.Attitude,pitch);
    if(pitch!=null)pitch.SetArmed(true,vessel,core.Attitude);
   } else if(verticalMode==1){
    if(alt!=null)alt.SetArmed(false,vessel,core.Attitude,vs,pitch);
    if(vs!=null)vs.SetArmed(true,vessel,core.Attitude,pitch);
    // V/S may already be armed while switching from ALT. Ensure its AA pitch
    // transport remains armed even when V/S.SetArmed(true) is edge-suppressed.
    if(pitch!=null)pitch.SetArmed(true,vessel,core.Attitude);
   } else {
    if(vs!=null)vs.SetArmed(true,vessel,core.Attitude,pitch);
    // Same invariant for ALT: V/S owns the trajectory, PITCH owns native transport.
    if(pitch!=null)pitch.SetArmed(true,vessel,core.Attitude);
    if(alt!=null)alt.SetArmed(true,vessel,core.Attitude,vs,pitch);
   }
  }

  void ApplySpeedMode(AERISAccelerationDirector acc,AERISVelocityDirector vel){
   var vessel=FlightGlobals.ActiveVessel;
   if(acc==null)return;
   // Both modes use ACC as the only throttle owner. VEL is an upper trajectory planner
   // that publishes only an acceleration target into ACC; it never writes throttle directly.
   bool enableSpeed=speedMaster && (speedMode==0 || speedMode==1);
   if(speedMode!=1 && vel!=null) vel.SetArmed(false,vessel,core.Attitude,acc);
   acc.SetArmed(enableSpeed,vessel,core.Attitude);
   if(speedMode==1 && vel!=null) vel.SetArmed(speedMaster,vessel,core.Attitude,acc);
  }
  bool WideAxisSwitch(string label,bool value){
   Color old=GUI.backgroundColor;
   try{
    GUI.backgroundColor=value?new Color(0.12f,0.75f,0.16f,1f):new Color(0.78f,0.12f,0.12f,1f);
    return CompactFullRowToggle(label+"  "+(value?"ON":"OFF"),value);
   }finally{GUI.backgroundColor=old;}
  }
  bool ModeSelectButton(string label,bool selected,bool interactive=true){
   Color oldColor=GUI.backgroundColor;
   bool oldEnabled=GUI.enabled;
   try{
    GUI.enabled=true;
    GUI.backgroundColor=selected?new Color(0.12f,0.75f,0.16f,1f):oldColor;
    bool clicked=GUILayout.Button(label,GUI.skin.button,GUILayout.Width(ReadableButtonWidth(label,SmallButtonWidth)),GUILayout.Height(CompactControlHeight()));
    return interactive&&clicked;
   }finally{GUI.backgroundColor=oldColor;GUI.enabled=oldEnabled;}
  }
  bool SmallButton(string label){ return GUILayout.Button(label,GUILayout.Width(ReadableButtonWidth(label,SmallButtonWidth)),GUILayout.Height(CompactControlHeight())); }
  bool ActionButton(string label){ return GUILayout.Button(label,GUILayout.Width(ReadableButtonWidth(label,ActionButtonWidth)),GUILayout.Height(CompactControlHeight())); }
  void PlannedModeButton(string label){ bool old=GUI.enabled;try{GUI.enabled=false;GUILayout.Toggle(false,label,GUI.skin.button,GUILayout.Width(ReadableButtonWidth(label,SmallButtonWidth)),GUILayout.Height(CompactControlHeight()));}finally{GUI.enabled=old;} }


  void DrawExtendAddons(){
   GUILayout.Label("EXTEND ADDONS — integration availability and user control");
   GUILayout.BeginVertical("box"); GUILayout.Label("AtmosphereAutopilot (embedded)");
   GUILayout.Label("Status: REQUIRED / "+(core.Manager!=null?"ACTIVE":"AWAITING VESSEL"));
   GUILayout.Label("Role: fixed-wing FBW surface control; AERIS ACC releases AA legacy speed PID while ACC owns throttle."); GUILayout.Label("AA internal model training: "+(settings.EnableAABackgroundTraining?"ON":"OFF")); GUILayout.EndVertical();
   GUILayout.BeginVertical("box"); GUILayout.Label("AutoPropPitch");
   GUILayout.Label("Status: "+(AddonIntegration.AppInstalled?"INSTALLED — "+AddonIntegration.AppProviderId:"NOT INSTALLED"));
   bool enabled=GUILayout.Toggle(settings.AppIntegrationEnabled,"Enable external propulsion integration");
   if(enabled!=settings.AppIntegrationEnabled){settings.AppIntegrationEnabled=enabled;AERISLogger.Info("[ADDONS] External propulsion integration="+enabled);}
   bool vessel=AddonIntegration.CurrentVesselSupportsApp;
   GUILayout.Label("Current vessel: "+AddonIntegration.AppStatusText);
   GUILayout.Label("External thrust bridge: "+(AddonIntegration.AppInstalled&&settings.AppIntegrationEnabled?"Enabled":"Disabled"));
   GUILayout.Label("Autopilot propulsion bridge: ACTIVE — VEL/ACC, Protect, Auto Takeoff and Ground Assist zero-demand");
   GUILayout.Label("Propulsion Moment Protection: Planned after FBW completion"); GUILayout.EndVertical();
   GUILayout.BeginVertical("box"); GUILayout.Label("Future supported addons");
   GUILayout.Label("Additional integrations will appear here only after their API is detected."); GUILayout.EndVertical();
  }


  void Toggle(ref bool value,string label){ bool n=GUILayout.Toggle(value,label); if(n!=value){value=n; AERISLogger.Info("FBW setting "+label+"="+n);} }
  void ToggleValue(bool value,string label,System.Action<bool> setter){ bool n=GUILayout.Toggle(value,label); if(n!=value){setter(n); AERISLogger.Info("FBW setting "+label+"="+n);} }
  void Slider(ref float value,string label,float min,float max,string format){GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(140));float n=GUILayout.HorizontalSlider(value,min,max,GUILayout.Width(110));GUILayout.Label(n.ToString(format),GUILayout.Width(40));GUILayout.EndHorizontal();if(Mathf.Abs(n-value)>0.0001f){value=n;AERISLogger.Info("FBW setting "+label+"="+n.ToString("F3"));}}
  void SaveBankInput(AERISBankDirector d){if(d==null)return;settings.ApBankTargetDeg=d.TargetBank;settings.Save();}
  void SaveHdgInput(AERISHdgDirector d){if(d==null)return;settings.ApHeadingTargetDeg=d.TargetHeading;settings.ApHeadingAutoBankLimit=d.UseAutoMaxBankLimit;settings.ApHeadingManualBankLimitDeg=d.ManualMaxBankLimitDeg;settings.Save();}
  void SavePitchInput(AERISPitchDirector d){if(d==null)return;settings.ApPitchTargetDeg=d.TargetPitch;settings.Save();}
  void SaveVsInput(AERISVerticalSpeedDirector d){if(d==null)return;settings.ApVerticalSpeedTargetMps=d.TargetVerticalSpeedMps;settings.ApVerticalSpeedMaxPitchDeg=d.MaxPitchTargetDeg;settings.Save();}
  void SaveAltInput(AERISAltitudeDirector d){if(d==null)return;settings.ApAltitudeTargetMeters=d.TargetAltitudeMeters;settings.ApAltitudeMaxVerticalSpeedMps=d.MaxAltitudeVerticalSpeedMps;settings.ApAltitudeMaxPitchDeg=d.MaxAltitudePitchDeg;settings.Save();}
  void SaveAccInput(AERISAccelerationDirector d){if(d==null)return;settings.ApAccelerationTargetMps2=d.ManualTargetAccelerationMps2;settings.Save();}
  void SaveVelInput(AERISVelocityDirector d){if(d==null)return;settings.ApVelocityTargetMps=d.TargetSurfaceSpeedMps;settings.ApVelocityTargetConfirmed=d.TargetConfirmed;settings.VelocityAccelerationLimitMps2=d.ConfiguredAccelerationLimitMps2;settings.Save();}
  void GroundSettingSlider(ref float value,string label,float min,float max,string format){GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(155));float n=GUILayout.HorizontalSlider(value,min,max,GUILayout.Width(110));GUILayout.Label(n.ToString(format),GUILayout.Width(45));GUILayout.EndHorizontal();if(Mathf.Abs(n-value)>0.0001f){value=n;settings.Save();AERISLogger.Info("[GROUND_ASSIST] setting "+label+"="+n.ToString("F3"));}}
  void TakeoffSettingSlider(ref float value,string label,float min,float max,string format){GUILayout.BeginHorizontal();GUILayout.Label(label,GUILayout.Width(155));float n=GUILayout.HorizontalSlider(value,min,max,GUILayout.Width(110));GUILayout.Label(n.ToString(format),GUILayout.Width(45));GUILayout.EndHorizontal();if(Mathf.Abs(n-value)>0.0001f){value=n;settings.Save();AERISLogger.Info("[AUTO_TAKEOFF] setting "+label+"="+n.ToString("F3"));}}

 }
}
