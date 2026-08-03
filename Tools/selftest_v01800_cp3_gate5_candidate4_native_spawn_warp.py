#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 4 Native Spawn Warp Utility Safety Hotfix 2')
warp_path=SOURCE/'Landing/AERISSandboxNativeSpawnWarpUtility.cs'
ui_path=SOURCE/'UI/AERISWindow.cs'
project_path=SOURCE/'AERISFlightControl.csproj'
version_path=SOURCE/'Properties/AERISBuildVersion.generated.cs'
for p in (warp_path,ui_path,project_path,version_path):
    suite.check(p.is_file(),str(p.relative_to(ROOT))+' exists')
warp=read(warp_path); ui=read(ui_path); project=read(project_path); version=read(version_path)
build=read(ROOT/'build_ubuntu.sh'); avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
registry=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
warp_code=strip_csharp_comments_and_literals(warp)
ui_code=strip_csharp_comments_and_literals(ui)
for name,text in (('warp',warp),('window',ui)):
    c=strip_csharp_comments_and_literals(text)
    suite.check(c.count('{')==c.count('}'),name+' braces balanced')
    suite.check(c.count('(')==c.count(')'),name+' parens balanced')
expected='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"'
suite.check(expected in version and expected in build,'Candidate 4 tab/build identity exact')
suite.check('Candidate 4 Native Spawn Warp Utility Safety Hotfix 2' in avc,'Candidate 4 AVC identity')
suite.check('DEV CP3 GATE 5 INTEGRATED ACCEPTANCE CANDIDATE 4 NATIVE SPAWN WARP UTILITY SAFETY HOTFIX 2 / DEV CP3 GATE 5 INTEGRATED ACCEPTANCE CANDIDATE 3 GENERATION BRIDGE HOTFIX 1' in version,'Candidate 3 lineage retained')
suite.check('Landing\\AERISSandboxNativeSpawnWarpUtility.cs' in project,'native spawn warp utility is compiled')
suite.check('AERISSandboxRunwayWarpUtility.cs' not in project,'discarded threshold-derived warp utility is not compiled')
suite.check('Game.Modes.SANDBOX' in warp,'warp is explicitly Sandbox-only')
suite.check('HighLogic.LoadedSceneIsFlight' in warp,'warp is Flight-scene only')
suite.check('AERISAirfieldSource.KerbalKonstructs' in warp and 'AERISAirfieldSource.StockLaunchsitesExpansion' in warp,'warp is limited to mod-native KK/SLE launch-site providers')
suite.check('string.Equals(airfield.Body, vessel.mainBody.bodyName' in warp,'warp is same-body only')
suite.check('AERISKerbalKonstructsProvider.Collect(records' in warp,'native provider state is rescanned at click time')
suite.check('AERISKspFacilityProvider.Collect(records' in warp,'provider scan includes KSP records for canonical identity parity')
suite.check('AERISPhysicalRunwayIdentity.Canonicalize(records' in warp,'click-time provider records use the same physical-runway federation contract')
suite.check('AERISProviderIdentity.StableRecordId(record)' in warp,'native spawn matching honors canonical provider identity')
suite.check('RuntimeLaunchTransform' in warp and 'live.position' in warp and 'live.forward.normalized' in warp,'live provider LaunchPadTransform is first authority')
suite.check('RuntimeInstanceTransform' in warp and 'RuntimePrefabLaunchTransform' in warp,'provider-native prefab launch frame may be reconstructed when live mesh is absent')
suite.check('instance.TransformPoint(localPosition)' in warp and 'instance.TransformDirection(localForward)' in warp,'reconstructed spawn remains provider transform derived')
suite.check('RuntimeLaunchPosition' not in warp_code,'stale cached world launch position is never warp authority')
for forbidden in ('InterpolateGeo(', 'SpawnInsetMeters', 'StagingClearanceMeters', 'FinalClearanceMeters', 'direction.Threshold', 'direction.OppositeThreshold'):
    suite.check(forbidden not in warp_code,'no AERIS runway-derived spawn path: '+forbidden)
suite.check('TryWarp(AERISAirfieldDefinition airfield,\n            AERISRunwayDefinition runway' in warp,'warp API is physical-runway based, not approach-direction based')
suite.check('AERISRunwayDirectionDefinition direction' not in warp_code,'warp utility has no runway-direction target parameter')
suite.check('WARP TO MOD NATIVE SPAWN' in ui,'AIRFIELDS exposes one provider-native spawn action')
suite.check(ui.count('WARP TO MOD NATIVE SPAWN')==1,'native spawn action label occurs once in AIRFIELDS implementation')
suite.check('AERISSandboxNativeSpawnWarpUtility.TryWarp(airfield,runway' in ui,'AIRFIELDS passes physical runway, never approach direction')
suite.check('AERISSandboxNativeSpawnWarpUtility.TryWarp(airfield,direction' not in ui,'no direction-specific native warp remains')
suite.check(ui.find('WARP TO MOD NATIVE SPAWN') < ui.find('for(int i=0;i<runway.Directions.Count;i++)',ui.find('void DrawAirfieldRunwayGroupDetail')),'single native-spawn button is outside the direction loop')
suite.check('if(AERISSandboxNativeSpawnWarpUtility.ShouldShow(airfield))' in ui,'warp UI is limited to detected mod-native runway providers in Sandbox')
suite.check('return Available && airfield != null && airfield.ProviderDetected' in warp,'native spawn button is hidden for cached/unavailable providers and non-Sandbox modes')
suite.check('No approach-direction offset is calculated.' in ui,'UI describes native spawn rather than approach-direction placement')
suite.check('FlightGlobals.fetch.SetVesselPosition' in warp,'warp delegates loaded-vessel relocation to KSP stock Set Position')
suite.check('EaseGravityMultiplier = 0.05' in warp and 'spawnAltitudeAgl, inclinationDeg, headingDeg, true, true,' in warp,'stock Set Position uses surface-relative altitude with physics easing enabled')
suite.check('TimeWarp.SetRate(0, true)' in warp,'warp exits time warp before relocation')
suite.check('Orbit orbit =' not in warp_code and '.Clone()' not in warp_code and 'SetOrbit(' not in warp_code,'unsupported Orbit clone/set path is absent')
suite.check('TryResolveNativeAttitude' in warp and 'Math.Atan2(Vector3.Dot(tangentForward, east)' in warp,'aircraft control frame aligns to provider native forward and local surface')
for forbidden in ('FlightCtrlState','mainThrottle','wheelThrottle','wheelSteer','brakes =','SelectAirfield(','SelectDirection(','CertificationState =','MarkUserRunwayCalibration(','ClearUserRunwayCalibration('):
    suite.check(forbidden not in warp_code,'warp does not write '+forbidden)
suite.check('generationBridgeFrames++' in renderer and 'gen_bridge_frames=' in renderer,'Candidate 3 generation bridge retained')
suite.check('cpu_terrain_draw=0' in renderer,'CPU terrain presentation remains hard zero')
suite.check(renderer.count('TryPresentReprojectedFront(')==1,'rejected GUI temporal warp remains quarantined')
suite.check('internal bool LandSelectionExplicitlyCleared = true;' in settings,'startup airport/runway selection remains neutral')
suite.check('startup neutral; airport=NONE; runway=NONE' in registry,'startup NONE/NONE telemetry retained')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE4_NATIVE_SPAWN_WARP_UTILITY_SAFETY_HOTFIX2.txt').is_file(),'Candidate 4 Safety Hotfix 2 acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE4_NATIVE_SPAWN_WARP_SAFETY_HOTFIX2_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Candidate 4 Safety Hotfix 2 runtime test card included')
suite.check('selftest_v01800_cp3_gate5_candidate4_native_spawn_warp.py' in read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py'),'Gate 5 runner invokes Candidate 4 dedicated test')
suite.finish()
