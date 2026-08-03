#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 4 Native Spawn Compile Contract Successor')
warp=read(SOURCE/'Landing/AERISSandboxNativeSpawnWarpUtility.cs')
code=strip_csharp_comments_and_literals(warp)
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
suite.check('.Clone()' not in code,'Orbit.Clone is absent')
suite.check('SetOrbit(' not in code,'Vessel.SetOrbit is absent')
suite.check('UpdateFromStateVectors(' not in code,'manual Orbit state-vector rewrite is absent')
suite.check('vessel.SetPosition(' not in code,'unsafe direct Vessel.SetPosition transport is absent')
suite.check('vessel.SetRotation(' not in code,'unsafe direct Vessel.SetRotation transport is absent')
suite.check('vessel.SetWorldVelocity(' not in code,'unsafe direct Vessel.SetWorldVelocity transport is absent')
suite.check('GoOffRails(' not in code,'warp no longer manually changes rails state')
suite.check('vessel.orbitDriver' not in code,'warp no longer depends on OrbitDriver readiness')
suite.check('FlightGlobals.fetch.SetVesselPosition' in warp,'KSP stock Set Position transport is used')
expected='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"'
suite.check(expected in version and expected in build,'Safety Hotfix 2 tab/build identity exact')
suite.check('Native Spawn Warp Utility Safety Hotfix 2' in avc,'Safety Hotfix 2 AVC identity')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE4_NATIVE_SPAWN_WARP_UTILITY_SAFETY_HOTFIX2.txt').is_file(),'Safety Hotfix 2 acceptance contract included')
suite.finish()
