#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 3 Generation Bridge Hotfix 1')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
nav=read(SOURCE/'UI/AERISNavigationDisplay.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
project=read(SOURCE/'AERISFlightControl.csproj')
registry=read(SOURCE/'Landing/AERISAirfieldRegistry.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
tile=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
virtual=read(SOURCE/'Terrain/AERISTerrainVirtualDetail.cs')
for name,text in (("renderer",renderer),("navigation",nav)):
    c=strip_csharp_comments_and_literals(text)
    suite.check(c.count('{')==c.count('}'),name+' braces balanced')
    suite.check(c.count('(')==c.count(')'),name+' parens balanced')
ui='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 3 — GENERATION BRIDGE HOTFIX 1"'
suite.check(ui in version and ui in build,'Candidate 3 tab/build identity exact')
suite.check('Gate 5 Integrated Acceptance Candidate 3 Generation Bridge Hotfix 1' in avc,'Candidate 3 AVC identity')
suite.check('DEV CP3 GATE 5 INTEGRATED ACCEPTANCE CANDIDATE 3 GENERATION BRIDGE HOTFIX 1 / DEV CP3 GATE 5 INTEGRATED ACCEPTANCE CANDIDATE 2' in version,'Candidate 2 lineage retained')
suite.check('run_v01800_cp3_gate5_acceptance.py' in build,'build invokes current Gate 5 runner')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE3_GENERATION_BRIDGE_HOTFIX1.txt').is_file(),'Candidate 3 acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE3_GENERATION_BRIDGE_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Candidate 3 runtime test card included')
suite.check((ROOT/'Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl46_GATE5_C2_RESIDUAL_BLACK_FLASH_2026-08-01.txt').is_file(),'Candidate 2 runtime diagnosis evidence included')
suite.check('selftest_v01800_cp3_gate5_candidate3_generation_bridge_hotfix1.py' in read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py'),'Gate 5 runner invokes Candidate 3 dedicated test')

# Presentation bridge contract.
suite.check('bool CanPresentLatchedFront(' in renderer,'latched FRONT eligibility remains explicit')
section=renderer[renderer.index('bool CanPresentLatchedFront('):renderer.index('void CapturePresentedProjection')]
suite.check('frontTerrainGeneration != visible.TerrainGeneration' not in section,'generation mismatch no longer rejects display latch')
suite.check('string.Equals(frontBodyName, visible.BodyName' in section,'generation bridge is constrained to same body')
suite.check('bodyRadiusMillimetres != frontBodyRadiusMillimetres' in section,'generation bridge requires same body radius')
suite.check('Time.realtimeSinceStartup - frontCommittedRealtime <= 8.0f' in section,'generation bridge keeps 8s fail-visible ceiling')
suite.check('if (frontTerrainGeneration != visible.TerrainGeneration)' in renderer and 'generationBridgeFrames++' in renderer,'generation rollover bridge frames are counted')
suite.check('gen_bridge_frames=' in renderer,'generation bridge telemetry emitted')
suite.check('gen_bridge_rejects=' in renderer,'generation bridge reject telemetry emitted')
suite.check('front_gen=' in renderer and 'current_gen=' in renderer,'front/current generation telemetry emitted')
suite.check('PresentFrontDirect(plot, frontOrientation);' in renderer,'bridge presents unwarped completed GPU FRONT')
suite.check('CapturePresentedProjection(true);' in renderer,'bridge publishes the latched FRONT projection')
suite.check(renderer.count('TryPresentReprojectedFront(')==1,'rejected GUI temporal warp remains definition-only quarantine')
suite.check('bool present = TryPresentReprojectedFront' not in renderer,'GUI temporal warp is not presentation authority')
suite.check('cpu_terrain_draw=0' in renderer,'CPU terrain presentation remains hard zero')
suite.check('Terrain\\AERISTerrainRasterWorker.cs' not in project,'retired CPU raster worker remains excluded')

# World layers continue to use actual presented projection while bridge is active.
suite.check('terrainTileRenderer.PresentedProjection' in nav,'ND consumes renderer presented projection')
for token in ('presentedCenterLatitudeDeg','presentedCenterLongitudeDeg','presentedRange','presentedHeading','presentedTrackUp','presentedAnchorV'):
    suite.check(token in nav,'ND world layers use '+token)
suite.check('DrawPreparedNavigation(plan, frame, vessel, presentedRange' in nav,'runway/facility layer uses presented range')
suite.check('DrawPreparedTraffic(plan, trafficFrame, vessel, presentedRange' in nav,'traffic layer uses presented projection')
suite.check('DrawTrail(plan, vessel, presentedRange' in nav,'trail uses presented projection')
suite.check('bool showRunwayEndNumbers = range <= 20000f;' in nav,'runway end labels remain 5/10/20km only')
suite.check('DrawTerrainStandbyBackground(plot);' in nav,'terrain OFF/rebuild retains non-black standby')

# Gate 4C/CP3 frozen boundaries.
suite.check('if (!ExactDetailPayloadExists(key)) continue;' in tile,'normal viewport does not generate missing exact Route/Local')
suite.check('source.Key.Lod != AERISTerrainTileLod.Far' in virtual,'virtual detail remains FAR-derived')
suite.check('new AERISTerrainTileKey' not in virtual,'virtual detail creates no persistent Route/Local identity')
allprod='\n'.join(read(p) for p in SOURCE.rglob('*.cs'))
suite.check('FULL BOOST' not in allprod.upper(),'FULL BOOST remains absent from runtime code')
suite.check('internal bool LandSelectionExplicitlyCleared = true;' in settings,'startup selection remains neutral')
suite.check('startup neutral; airport=NONE; runway=NONE' in registry,'startup NONE/NONE telemetry retained')
suite.finish()
