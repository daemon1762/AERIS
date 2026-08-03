#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite("v0.18.0.0 CP3 Gate 5 Candidate 2 Presentation Latch Runway Lock Hotfix 1")
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

ui='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 2 — PRESENTATION LATCH / RUNWAY LOCK HOTFIX 1"'
suite.check(ui in version and ui in build,'Candidate 2 tab/build identity exact')
suite.check('Gate 5 Integrated Acceptance Candidate 2 Presentation Latch Runway Lock Hotfix 1' in avc,'Candidate 2 AVC identity')
suite.check('run_v01800_cp3_gate5_acceptance.py' in build,'build invokes current Gate 5 runner')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE2_PRESENTATION_LATCH_RUNWAY_LOCK_HOTFIX1.txt').is_file(),'Candidate 2 acceptance contract included')
suite.check((ROOT/'Docs/ND_CP3_GATE5_INTEGRATED_ACCEPTANCE_CANDIDATE2_PRESENTATION_LATCH_RUNWAY_LOCK_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Candidate 2 runtime test card included')
suite.check((ROOT/'Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl45_GATE5_C1_BLACK_FLASH_RUNWAY_FLOAT_2026-08-01.txt').is_file(),'Candidate 1 runtime diagnosis evidence included')
suite.check('selftest_v01800_cp3_gate5_candidate2_presentation_latch_hotfix1.py' in read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py'),'Gate 5 runner invokes Candidate 2 dedicated test')

suite.check('internal struct AERISTerrainPresentedProjection' in renderer,'presented projection contract exists')
for token in ('CenterLatitudeDeg','CenterLongitudeDeg','RangeMeters','MapHeadingDeg','TrackUp','AnchorV','Orientation','AgeSeconds','Latched'):
    suite.check(token in renderer,'presented projection carries '+token)
suite.check('bool CanPresentLatchedFront(' in renderer,'latched FRONT eligibility is explicit')
suite.check('Time.realtimeSinceStartup - frontCommittedRealtime <= 8.0f' in renderer,'latched FRONT has fail-visible 8s age ceiling')
suite.check('void CapturePresentedProjection(bool latched)' in renderer,'presented FRONT projection is published')
suite.check('(lastFrontBufferLatched ? "LATCHED" : "DIRECT")' in renderer,'telemetry distinguishes LATCHED from DIRECT')
suite.check('latch_age=' in renderer,'latch age telemetry exists')
suite.check('PresentFrontDirect(plot, frontOrientation);' in renderer,'latched continuity uses unwarped GPU FRONT')
suite.check(renderer.count('TryPresentReprojectedFront(')==1,'rejected GUI temporal warp remains definition-only quarantine')
suite.check('bool present = TryPresentReprojectedFront' not in renderer,'GUI temporal warp is not presentation authority')
suite.check('RecordPresentedFrontAlignmentDiagnostic' in renderer,'alignment telemetry follows actual presented FRONT')
suite.check('frontProjection = AERISNdMapProjection.Create(' in renderer,'alignment diagnostic reconstructs committed FRONT projection')
suite.check('frontCenterLatitudeDeg, frontCenterLongitudeDeg' in renderer,'alignment diagnostic uses committed FRONT center')

suite.check('terrainTileRenderer.PresentedProjection' in nav,'ND consumes renderer presented projection')
for token in ('presentedCenterLatitudeDeg','presentedCenterLongitudeDeg','presentedRange','presentedHeading','presentedTrackUp','presentedAnchorV'):
    suite.check(token in nav,'ND latches world symbology via '+token)
suite.check('DrawPreparedNavigation(plan, frame, vessel, presentedRange' in nav,'runway/facility layer uses presented range')
suite.check('DrawPreparedTraffic(plan, trafficFrame, vessel, presentedRange' in nav,'traffic layer uses presented projection')
suite.check('DrawTrail(plan, vessel, presentedRange' in nav,'trail uses presented projection')
suite.check('TryMapPoint(ownEast - centerEast, ownNorth - centerNorth,\n                    presentedRange, presentedHeading, presentedTrackUp' in nav,'ownship uses presented projection')
suite.check('bool showRunwayEndNumbers = range <= 20000f;' in nav,'runway endpoint numbers/ticks limited to 5/10/20km')
runway_section=nav[nav.index('void DrawPreparedRunway'):nav.index('void DrawSelectedRunwayEdgePointer')]
suite.check('if (showRunwayEndNumbers && axis.sqrMagnitude > 0.1f)' in runway_section,'high-range I-shaped threshold ticks are suppressed')
suite.check('RunwayDesignationOnly(runway.DirectionAName)' in runway_section and 'RunwayDesignationOnly(runway.DirectionBName)' in runway_section,'compact runway end numbers retained')
suite.check('DrawTerrainStandbyBackground(plot);' in nav,'terrain rebuild uses non-black standby background')
suite.check('new Color(0.025f, 0.145f, 0.285f, 1f)' in nav,'standby background is normal ocean-map blue')

suite.check('Terrain\\AERISTerrainRasterWorker.cs' not in project,'retired CPU raster worker remains excluded')
suite.check('cpu_terrain_draw=0' in renderer,'CPU terrain presentation remains hard zero')
suite.check('if (!ExactDetailPayloadExists(key)) continue;' in tile,'normal viewport still does not generate missing exact Route/Local')
suite.check('source.Key.Lod != AERISTerrainTileLod.Far' in virtual,'virtual detail remains FAR-derived')
suite.check('new AERISTerrainTileKey' not in virtual,'virtual detail does not synthesize persistent Route/Local identities')
allprod='\n'.join(read(p) for p in SOURCE.rglob('*.cs'))
suite.check('FULL BOOST' not in allprod.upper(),'FULL BOOST remains absent from runtime code')
suite.check('internal bool LandSelectionExplicitlyCleared = true;' in settings,'startup selection remains neutral')
suite.check('startup neutral; airport=NONE; runway=NONE' in registry,'startup NONE/NONE telemetry retained')
suite.finish()
