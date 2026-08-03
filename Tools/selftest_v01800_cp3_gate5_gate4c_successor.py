#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite("v0.18.0.0 CP3 Gate 5 Gate 4C Frozen Successor")
virtual_path=SOURCE/"Terrain/AERISTerrainVirtualDetail.cs"
raster_path=SOURCE/"Terrain/AERISTerrainGpuTileRasterizer.cs"
renderer_path=SOURCE/"Terrain/AERISTerrainGpuTileRenderer.cs"
tile_path=SOURCE/"Terrain/AERISTerrainTileSystem.cs"
nav_path=SOURCE/"UI/AERISNavigationDisplay.cs"
registry_path=SOURCE/"Landing/AERISAirfieldRegistry.cs"
settings_path=SOURCE/"Settings/AERISSettings.cs"
project_path=SOURCE/"AERISFlightControl.csproj"
version_path=SOURCE/"Properties/AERISBuildVersion.generated.cs"
for p in (virtual_path,raster_path,renderer_path,tile_path,nav_path,registry_path,settings_path,project_path,version_path):
    suite.check(p.is_file(), str(p.relative_to(ROOT))+" exists")
virtual=read(virtual_path); raster=read(raster_path); renderer=read(renderer_path)
tile=read(tile_path); nav=read(nav_path); registry=read(registry_path); settings=read(settings_path)
project=read(project_path); version=read(version_path); build=read(ROOT/"build_ubuntu.sh")
for name,text in (("virtual detail",virtual),("rasterizer",raster),("renderer",renderer),("navigation",nav)):
    c=strip_csharp_comments_and_literals(text)
    suite.check(c.count("{")==c.count("}"),name+" braces balanced")
    suite.check(c.count("(")==c.count(")"),name+" parens balanced")

suite.check('Terrain\\AERISTerrainVirtualDetail.cs' in project,"virtual-detail contract is compiled")
suite.check("enum AERISTerrainVirtualDetailLevel" in virtual,"virtual detail has explicit level enum")
suite.check('"FAR DIRECT", 1, 33, 1.0f' in virtual,"LOW/far direct keeps authoritative FAR grid")
suite.check('"VIRTUAL ROUTE", 2, 65, 1.25f' in virtual,"virtual route reconstructs FAR to at most 65x65")
suite.check('"VIRTUAL LOCAL", 3, 97, 1.50f' in virtual,"virtual local reconstructs FAR to at most 97x97")
suite.check('high && range <= 20000f' in virtual,"HIGH virtual local is limited to high-magnification range")
suite.check('(land && range <= 40000f)' in virtual,"LAND profile may retain virtual local to 40km")
suite.check('(land || high || medium) && range <= 80000f' in virtual,"medium/high virtual route ends at 80km")
suite.check('source.Key.Lod != AERISTerrainTileLod.Far' in virtual,"only FAR payloads are reconstructed")
suite.check('Key = source.Key' in virtual,"virtual detail does not create persistent Route/Local tile identities")
suite.check('bool sameClass = f00 == nearestFlag && f10 == nearestFlag' in virtual,"reconstruction requires categorical class agreement for interpolation")
suite.check('NearestClassHeight(source, sourceX, sourceY' in virtual,"coast/invalid boundary uses conservative same-class sample")
suite.check('new AERISTerrainTileKey' not in virtual,"virtual reconstruction cannot synthesize new persistent tile keys")

suite.check('VirtualDetailProfile' in raster,"worker request carries immutable virtual-detail profile")
suite.check('AERISTerrainVirtualDetailPolicy.ReconstructFar' in raster,"GeneralCompute raster worker performs pure-data reconstruction")
suite.check('VirtualDetailLevel = request.VirtualDetailProfile' in raster,"render-ready result records virtual detail level")
suite.check('BuildStyleKey(contourInterval, virtualDetail)' in renderer,"GPU/render-ready cache identity includes virtual detail profile")
suite.check('VirtualDetailProfile = virtualDetail' in renderer,"scheduled worker receives current detail profile")
suite.check('virtualDetail.RenderTargetScale' in renderer,"upper quality increases GPU presentation resolution without Route/Local textures")
suite.check('tile.Key.Lod >= AERISTerrainTileLod.Route' in renderer and 'exactDetailOverlayDraws++' in renderer,"exact Route/Local/LAND tiles remain authoritative overlays")
suite.check('[CP3_GATE4C_VIRTUAL_DETAIL]' in renderer,"Gate 4C telemetry is explicit")
suite.check('cpu_terrain_draw=0' in renderer,"CPU terrain presentation remains forbidden")
suite.check('bool present = TryPresentReprojectedFront' not in renderer,"rejected GUI temporal warp remains non-authoritative")
suite.check(renderer.count('TryPresentReprojectedFront(')==1,"temporal warp remains definition-only quarantine")

suite.check('AddExistingExactDetailBridge(latitude, longitude, nearLod' in tile,"existing exact detail remains demand-only bridge")
suite.check('if (!ExactDetailPayloadExists(key)) continue;' in tile,"normal viewport never generates missing exact Route/Local")
suite.check('AddLandingPointWithPins(thresholdLat, thresholdLon' in tile,"LAND retains exact microtile path")
suite.check('AERISTerrainTileLod.Land' in tile and 'AERISResidentPinReason.Landing' in tile,"LAND exact payload is explicitly pinned")
suite.check('AddPoint(point.LatitudeDeg, point.LongitudeDeg,\n                    AERISTerrainTileLod.Far' in tile,"predictive corridor remains FAR-only during cruise")

suite.check('bool showRunwayEndNumbers = range <= 20000f;' in nav,"runway endpoint text appears only at 5/10/20km")
suite.check('RunwayDesignationOnly(runway.DirectionAName)' in nav and 'RunwayDesignationOnly(runway.DirectionBName)' in nav,"endpoint labels use compact designation helper")
suite.check('runway.DirectionAName, centerStyle' not in nav and 'runway.DirectionBName, centerStyle' not in nav,"full direction names no longer enter 36px endpoint labels")
suite.check('number.ToString("00")' in nav,"runway numbers are zero-padded")
suite.check("text[i] == 'L' || text[i] == 'C' ||" in nav,"L/C/R suffixes are preserved")
suite.check('range <= 40000f' not in nav[nav.index('void DrawPreparedRunway'):nav.index('void DrawSelectedRunwayEdgePointer')],"40km endpoint label rule is removed")

suite.check('internal bool LandSelectionExplicitlyCleared = true;' in settings,"startup selection remains neutral")
suite.check('if (!startupComplete)\n                ResetSelectionForStartup();' in registry,"first registry commit still forces airport/runway NONE")
suite.check('startup neutral; airport=NONE; runway=NONE' in registry,"neutral selection telemetry remains")
ui='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"'
suite.check(ui in version and ui in build,"Gate 5 successor tab/build identity")
suite.check('Gate 5 Integrated Acceptance Candidate 2 Presentation Latch Runway Lock Hotfix 1' in read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version'),"AVC identity is Gate 5 successor")
suite.check('run_v01800_cp3_gate5_acceptance.py' in build,"build entrypoint invokes Gate 5 acceptance")
suite.check('run_v01800_cp3_gate4b_geometry_integrity_hotfix2_acceptance.py' not in build,"build entrypoint no longer invokes obsolete Gate 4B identity-fixed runner")
suite.finish()
