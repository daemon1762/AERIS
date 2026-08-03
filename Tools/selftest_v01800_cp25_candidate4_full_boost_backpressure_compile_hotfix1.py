#!/usr/bin/env python3
import json,sys,hashlib
from pathlib import Path
sys.dont_write_bytecode=True
from v01700_testlib import ROOT, CheckSuite, read
suite=CheckSuite("v0.18.0.0 CP2.5 Candidate 4 Full Boost Backpressure Compile Hotfix 1")
build=read(ROOT/"build_ubuntu.sh")
generated=read(ROOT/"Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
legacy=read(ROOT/"Tools/verify_v01800_cp2_static.py")
version=json.loads((ROOT/"GameData/AERISFlightControl/AERISFlightControl.version").read_text())
frozen='AERIS Flight Control v0.18.0.0 DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1 COMPILE HOTFIX 1 MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1 COMPILE HOTFIX 1 AXIS REFERENCE HOTFIX 2 RUNWAY WITNESS ANCHOR SCAN CALIBRATION HOTFIX 3 GENERIC RUNWAY PLACEMENT VERIFICATION MANUAL CALIBRATION FINAL CANDIDATE 3 COMPILE HOTFIX 1 CALIBRATION ROUND-TRIP HOTFIX 1 BIDIRECTIONAL RUNWAY PAIR HOTFIX 1 ND NAVIGATION SNAPSHOT PUBLISH AIRFIELDS UI COLLAPSE HOTFIX 1 RESPONSIVE AIRFIELDS UI LAYOUT RESIZE HOTFIX 1 MANUAL CALIBRATED RUNWAY SEPARATION PRESERVATION HOTFIX 1 MANUAL CALIBRATION REFLECTION HOTFIX 1 MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1 MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1'
suite.check(('BASELINE_DISPLAY="'+frozen+'"') in build,"build entrypoint preserves a pure frozen CP2 baseline")
suite.check("CP2.5" not in build.split("BASELINE_DISPLAY=",1)[1].splitlines()[0],"frozen baseline contains no CP2.5 successor identity")
suite.check("FULL BOOST BACKPRESSURE COMPILE HOTFIX 1" in build,"build display names compile hotfix 1")
suite.check("FULL BOOST BACKPRESSURE COMPILE HOTFIX 1" in generated,"generated display names compile hotfix 1")
suite.check(('Cp2FrozenBaselineDisplay = "'+frozen+'"') in generated,"generated source exposes the exact frozen CP2 identity")
suite.check("Cp2FrozenBaselineDisplay" in legacy,"legacy CP2 verifier reads the dedicated frozen baseline field")
suite.check("suite.check('Display = \"AERIS Flight Control v0.18.0.0 DEV CP2 KK" not in legacy,"legacy verifier no longer requires CP2 to be the current top-level display")
suite.check("Full Boost Backpressure Compile Hotfix 1" in version.get("NAME",""),"AVC metadata names compile hotfix 1")
suite.equal(hashlib.sha256((ROOT/'Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs').read_bytes()).hexdigest(),'fa6fcc42e70b2bfc421d532e6e9719da1e96f0ce94359b4664b1ef38ba54a654',"runtime source remains byte-identical: Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs")
suite.equal(hashlib.sha256((ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs').read_bytes()).hexdigest(),'d39def4033c9d37fe46d90a9d678b1d438738140189feb5e473a6ddde4b01e5a',"runtime source remains byte-identical: Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs")
suite.check(hashlib.sha256((ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs').read_bytes()).hexdigest() in ('8e7349b9f12473573d72ff87edda67e232c554851213d253c353b6b8d98f3c57','fdb94bdb4abc742477ebd3763b57afd32747404c0cff1e4da7a01c42b1759318'),"runtime source is compile-hotfix baseline or audited downstream successor: Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs")
suite.check(hashlib.sha256((ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs').read_bytes()).hexdigest() in ('8947a20ee86acac9f4091d8fe5daaebfb3f36e38ba7525a287dac85caf206e1f','1664be55de113a8cab6b03cd7b13546be151fd014ed24b2758249f3d86470972'),"runtime source is compile-hotfix baseline or audited downstream successor: Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs")
suite.check(hashlib.sha256((ROOT/'Source/AERISFlightControl/UI/AERISWindow.cs').read_bytes()).hexdigest() in ('70b19593bb6ed14bbc3fd658c2f4ce6c141ce0dc3f4f7259c99a895353021530','72fe8edd6943b40b37ceac381182b0b7addc14a9f8301f7283bc728a6fe031e2','cec70118a316762494a793a62208303bda5a8009441309440a6819465c3f4e74'),"runtime source is compile-hotfix baseline or audited downstream successor: Source/AERISFlightControl/UI/AERISWindow.cs")
suite.equal(hashlib.sha256((ROOT/'Source/AERISFlightControl/AA/SyncModuleControlSurface.cs').read_bytes()).hexdigest(),'93d5161d9280e26e45ee3cfe6a3083f0e58a518216d67815d8534430151f6336',"runtime source remains byte-identical: Source/AERISFlightControl/AA/SyncModuleControlSurface.cs")
suite.check("FlightInputHandler.state" not in build,"compile hotfix adds no control authority")
suite.finish()
