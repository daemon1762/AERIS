#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite("v0.18.0.0 CP3 Gate 5 Integrated Acceptance Candidate 1")
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
project=read(SOURCE/'AERISFlightControl.csproj')
resident=read(SOURCE/'Terrain/AERISCurrentBodyResidentCache.cs')
tile=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
virtual=read(SOURCE/'Terrain/AERISTerrainVirtualDetail.cs')
mapdram=read(SOURCE/'Performance/AERISMapDramCache.cs')
preload=read(SOURCE/'Terrain/AERISTerrainPreloadBuilder.cs')
registry=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
nav=read(SOURCE/'UI/AERISNavigationDisplay.cs')
ui='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 1"'
suite.check(ui in version and ui in build,"Gate 5 tab/build identity exact")
suite.check('Gate 5 Integrated Acceptance Candidate 1' in avc,"Gate 5 AVC identity")
suite.check('run_v01800_cp3_gate5_acceptance.py' in build,"build invokes Gate 5 acceptance runner")
suite.check('run_v01800_cp3_gate4c_acceptance.py"' not in build,"build no longer directly invokes prior Gate 4C runner")
suite.check((ROOT/'Tools/analyze_v01800_cp3_gate5_runtime.py').is_file(),"runtime evidence analyzer included")
suite.check((ROOT/'Docs/ND_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE1_TEST_CARD_v0.18.0.0_ja.md').is_file(),"Gate 5 test card included")

suite.check('Terrain\\AERISTerrainRasterWorker.cs' not in project,"retired CPU raster worker remains excluded")
suite.check('cpu_terrain_draw=0' in renderer,"CPU terrain presentation hard-zero contract retained")
suite.check(renderer.count('TryPresentReprojectedFront(')==1,"rejected temporal GUI warp remains definition-only quarantine")
suite.check('void ReleaseGpuResources()' in renderer and 'ReleaseGpuResources();' in renderer,"GPU resource release contract remains compiled")
suite.check('bool present = TryPresentReprojectedFront' not in renderer,"temporal GUI warp is not presentation authority")
suite.check('AERISTerrainVirtualDetailPolicy.ReconstructFar' in read(SOURCE/'Terrain/AERISTerrainGpuTileRasterizer.cs'),"Gate 4C virtual detail retained")
suite.check('source.Key.Lod != AERISTerrainTileLod.Far' in virtual,"virtual detail remains FAR-derived")
suite.check('new AERISTerrainTileKey' not in virtual,"virtual detail creates no persistent Route/Local identities")
suite.check('if (!ExactDetailPayloadExists(key)) continue;' in tile,"normal viewport does not generate missing exact Route/Local")
suite.check('AERISTerrainTileLod.Land' in tile and 'AERISResidentPinReason.Landing' in tile,"LAND exact microtile authority retained")

suite.check('AERISResidentEvictionReason.BodyTransition' in resident,"resident cache retains explicit body-transition eviction")
suite.check('ForeignBodyRejects' in resident,"foreign-body resident rejection telemetry retained")
suite.check('StaleCommitRejects' in resident,"stale generation commit rejection telemetry retained")
suite.check('RamBudgetBytes' in resident and 'OverBudgetBytes' in resident,"resident RAM budget telemetry retained")
suite.check('AERISResidentTileState.GpuReady' in resident,"full INDEXED→GPU READY state model retained")

suite.check('payloadBytes=0' in mapdram,"Map DRAM remains metadata-only")
suite.check('synchronousSSD' in mapdram,"Map DRAM synchronous-SSD guard telemetry retained")
allprod='\n'.join(read(p) for p in SOURCE.rglob('*.cs'))
suite.check('FULL BOOST' not in allprod.upper(),"FULL BOOST runtime code remains absent")
suite.check('AERISWorkerLane.Safety' not in read(SOURCE/'Terrain/AERISTerrainTileSystem.cs'),"Terrain tile system does not occupy Flight safety lane")
suite.check('AERISWorkerLane.Safety' not in read(SOURCE/'Terrain/AERISTerrainGpuTileRasterizer.cs'),"Terrain render preparation does not occupy Flight safety lane")

suite.check('ReactivateBelowAltitudeAslMeters = 39500.0' in read(SOURCE/'Terrain/AERISTerrainViewportActivationPolicy.cs'),"altitude gate lower hysteresis remains 39.5 km")
suite.check('DeactivateAtOrAboveAltitudeAslMeters = 40500.0' in read(SOURCE/'Terrain/AERISTerrainViewportActivationPolicy.cs'),"altitude gate upper hysteresis remains 40.5 km")
suite.check('internal bool LandSelectionExplicitlyCleared = true;' in settings,"airport/runway startup remains neutral")
suite.check('startup neutral; airport=NONE; runway=NONE' in registry,"startup neutral telemetry retained")
suite.check('bool showRunwayEndNumbers = range <= 20000f;' in nav,"Gate 4C runway-end number range rule retained")
suite.check('RunwayDesignationOnly(runway.DirectionAName)' in nav,"compact runway designation retained")

an=read(ROOT/'Tools/analyze_v01800_cp3_gate5_runtime.py')
for token in ('terrain_gpu_failures','terrain_db_crc_failures','terrain_db_hash_mismatches','terrain_decompress_failures','cp3_resident_decode_failures','cp3_resident_ram_bytes','cp3_resident_ram_budget_bytes','synchronousSSD=0','ready_build_violation=0','cpu_terrain_draw=0'):
    suite.check(token in an,"runtime analyzer checks "+token)
suite.finish()
