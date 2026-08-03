#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 4 Native Spawn Safety Hotfix 2')
warp=read(SOURCE/'Landing/AERISSandboxNativeSpawnWarpUtility.cs')
code=strip_csharp_comments_and_literals(warp)
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
ui=read(SOURCE/'UI/AERISWindow.cs')
for name,text in (('warp',warp),('ui',ui)):
 c=strip_csharp_comments_and_literals(text)
 suite.check(c.count('{')==c.count('}'),name+' braces balanced')
 suite.check(c.count('(')==c.count(')'),name+' parens balanced')
suite.check('FlightGlobals.fetch.SetVesselPosition' in warp,'KSP stock Set Position is relocation authority')
suite.check('spawnAltitudeAgl, inclinationDeg, headingDeg, true, true,' in warp,'surface-relative native spawn altitude and attitude feed stock physics-eased relocation')
suite.check('EaseGravityMultiplier = 0.05' in warp,'physics easing gravity is reduced to 0.05g')
suite.check('WarpCooldownSeconds = 12f' in warp,'repeat warp is blocked for the easing window')
suite.check('Time.realtimeSinceStartup < nextWarpAllowedRealtime' in warp,'easing cooldown is enforced')
suite.check('WARP REFUSED — STOCK PHYSICS EASING ACTIVE' in warp,'busy state is explicit to the operator')
suite.check('body.GetLatitude((Vector3d)spawnPosition)' in warp,'native live transform latitude retained')
suite.check('body.GetLongitude((Vector3d)spawnPosition)' in warp,'native live transform longitude retained')
suite.check('body.GetAltitude((Vector3d)spawnPosition)' in warp,'native live transform ASL retained')
suite.check('TryResolveNativeAttitude' in warp,'native launch direction is converted to stock attitude')
suite.check('Math.Atan2(Vector3.Dot(tangentForward, east)' in warp,'heading derives from provider forward in local ENU frame')
suite.check('Math.Asin(vertical)' in warp,'provider forward slope derives inclination')
suite.check('FlightGlobals.Bodies.IndexOf(body)' in warp,'stock Set Position receives the active body index')
suite.check('WARPING TO MOD NATIVE SPAWN — STOCK PHYSICS EASING' in warp,'runtime telemetry identifies safe transport')
suite.check('ease_gravity=' in warp,'runtime telemetry records easing gravity')
for forbidden in ('vessel.SetPosition(', 'vessel.SetRotation(', 'vessel.SetWorldVelocity(', 'GoOffRails(', '.Clone()', 'SetOrbit(', 'UpdateFromStateVectors('):
 suite.check(forbidden not in code,'unsafe/manual transport absent: '+forbidden)
for forbidden in ('InterpolateGeo(', 'SpawnInsetMeters', 'StagingClearanceMeters', 'FinalClearanceMeters', 'direction.Threshold', 'direction.OppositeThreshold'):
 suite.check(forbidden not in code,'no synthesized runway spawn path: '+forbidden)
suite.check('RuntimeLaunchTransform' in warp and 'live.position' in warp and 'live.forward.normalized' in warp,'live provider LaunchPadTransform remains first authority')
suite.check('WARP TO MOD NATIVE SPAWN' in ui,'AIRFIELDS action remains single native-spawn button')
suite.check(ui.count('WARP TO MOD NATIVE SPAWN')==1,'no direction-specific duplicate warp button')
suite.check('DEV CP3 GATE 5 INTEGRATED ACCEPTANCE CANDIDATE 4 NATIVE SPAWN WARP UTILITY SAFETY HOTFIX 2' in version and 'CANDIDATE 4 NATIVE SPAWN WARP UTILITY SAFETY HOTFIX 2' in build,'Safety Hotfix 2 identity retained in successor history')
suite.check('UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"' in version and 'UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"' in build,'current successor tab/build identity exact')
suite.check('Native Spawn Warp Utility Safety Hotfix 2' in avc,'Safety Hotfix 2 AVC identity')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE4_NATIVE_SPAWN_WARP_UTILITY_SAFETY_HOTFIX2.txt').is_file(),'Safety Hotfix 2 acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE4_NATIVE_SPAWN_WARP_SAFETY_HOTFIX2_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Safety Hotfix 2 runtime test card included')
suite.finish()
