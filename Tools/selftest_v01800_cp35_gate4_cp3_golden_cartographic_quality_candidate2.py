#!/usr/bin/env python3
import re,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 4 CP3 Golden Cartographic Quality Candidate 2')
virtual=read(SOURCE/'Terrain/AERISTerrainVirtualDetail.cs')
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
contracts=read(SOURCE/'Terrain/AERISTerrainTileContracts.cs')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
raster=read(SOURCE/'Terrain/AERISTerrainGpuTileRasterizer.cs')
coast=read(SOURCE/'Terrain/AERISTerrainCoastlinePolicy.cs')
perf=read(SOURCE/'Terrain/AERISTerrainPerformance.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
nav=read(SOURCE/'UI/AERISNavigationDisplay.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
identity='DEV CP3.5 GATE 4 — CP3 GOLDEN CARTOGRAPHIC QUALITY CANDIDATE 2'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,'generated Candidate 2 identity')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,'build-generated Candidate 2 identity')
suite.check('Gate 4 CP3 Golden Cartographic Quality Candidate 2' in avc,'AVC Candidate 2 identity')
suite.check('run_v01800_cp35_gate4_cp3_golden_cartographic_quality_candidate2_prebuild.py' in build,'normal build invokes Candidate 2 lightweight prebuild')
suite.check('run_v01800_cp35_gate4_cp3_golden_cartographic_quality_candidate2_full_acceptance.py' in build,'full Candidate 2 audit is documented in build entrypoint')

# Persistent terrain/preload authority is intentionally unchanged.
suite.check('internal const int DefaultResolution = 33;' in contracts,'persistent/default FAR terrain remains REAL33')
suite.check('internal const int GlobalResolution = 17;' in contracts,'global terrain remains REAL17')
suite.check('internal const int LowRealResolution = 33;' in virtual,'LOW source remains REAL33')
suite.check('internal const int MiddleRealResolution = 33;' in virtual,'MIDDLE source remains REAL33')
suite.check('internal const int HighRealResolution = 65;' in virtual,'HIGH bounded real refinement remains REAL65')
suite.check('internal const int Cp3GoldenRouteResolution = 65;' in virtual,'CP3 Golden VIRTUAL ROUTE is 65')
suite.check('internal const int Cp3GoldenLocalResolution = 97;' in virtual,'CP3 Golden VIRTUAL LOCAL is 97')
suite.check('internal const int HighVirtualResolution = 129;' in virtual,'HIGH refined presentation target is 129')

# Hard visual floor: CP3 final visual reconstruction is active even in LOW.
suite.check('"LOW CP3 GOLDEN VIRTUAL LOCAL 97"' in virtual,'LOW <=20 km uses CP3 VIRTUAL LOCAL 97')
suite.check('"LOW CP3 GOLDEN VIRTUAL ROUTE 65"' in virtual,'LOW 20..80 km uses CP3 VIRTUAL ROUTE 65')
suite.check('"LOW CP3 GOLDEN FAR HI-DPI"' in virtual and 'true, 1.25f' in virtual,'LOW long range keeps CP3 FAR Hi-DPI presentation')
suite.check('if (range <= 20000f) return LowGoldenLocal;' in virtual,'LOW near-range CP3 Local threshold')
suite.check('if (range <= 80000f) return LowGoldenRoute;' in virtual,'LOW medium-range CP3 Route threshold')
suite.check('"MIDDLE CP3 GOLDEN VIRTUAL LOCAL 97"' in virtual,'MIDDLE near range uses CP3 Local 97')
suite.check('"MIDDLE CP3 GOLDEN VIRTUAL ROUTE 65"' in virtual,'MIDDLE medium range uses CP3 Route 65')
suite.check('"MIDDLE CP3 GOLDEN LONG RANGE 65"' in virtual,'MIDDLE long range remains reconstructed 65')
suite.check('"HIGH CP3 GOLDEN 97 / REAL65 -> VIRTUAL129"' in virtual,'HIGH near fallback is CP3 Local and refined target 129')
suite.check('"HIGH CP3 GOLDEN 65 / REAL65 -> VIRTUAL129"' in virtual,'HIGH farther fallback is CP3 Route and refined target 129')
suite.check('source.Resolution >= profile.RefinedSourceResolution' in virtual and 'profile.RefinedVirtualResolution > targetResolution' in virtual,'VIRTUAL129 requires complete refined REAL65 source')
suite.check('if (targetResolution == source.Resolution) return source;' in virtual,'worker avoids pointless resample')

# CP3 categorical map reconstruction is preserved. No synthetic blended coastline classes.
suite.check('bool sameClass = f00 == nearestFlag && f10 == nearestFlag' in virtual,'CP3 same-class interpolation rule restored')
suite.check('NearestClassHeight' in virtual and 'nearestFlag' in virtual,'CP3 categorical boundary reconstruction restored')
suite.check('landScore' not in virtual,'synthetic land-score coast interpolation stays absent')
suite.check('FirstValidFlag' in virtual,'invalid boundary samples use conservative CP3 flag recovery')

# CP3 cartographic primitives are still generated from the reconstructed tile.
suite.check('BuildContours(tile, Math.Max(25f, request.ContourIntervalMeters))' in raster,'contours are built after virtual reconstruction')
suite.check('float[] coastlines = BuildCoastlines(tile);' in raster,'coastline vector geometry is always built')
suite.check('tile.Flags[a] == 2 || tile.Flags[b] == 2' in raster,'contours remain land-only')
suite.check('AddTriangleCoastline' in raster,'coastline follows exact fill triangle topology')
suite.check('AERISTerrainCoastlinePolicy.CrossingFraction' in raster,'sub-cell coastline crossing is used')
suite.check('OceanSurfaceMeters = 1.0f' in coast,'coastline elevation crossing uses 1 m ocean surface authority')
suite.check('rangeMeters <= 10000f ? 50f' in renderer and 'rangeMeters <= 40000f ? 100f' in renderer and 'rangeMeters <= 80000f ? 250f : 500f' in renderer,'late-CP3 contour density schedule retained')

# Candidate 1 fake MIDDLE path must be impossible.
suite.check('"MIDDLE REAL 33 -> VIRTUAL 65"' not in virtual,'Candidate 1 render-target-only MIDDLE path removed')
suite.check('"LOW REAL 33 NATIVE"' not in virtual,'Candidate 1 blocky LOW native path removed at useful map ranges')
suite.check('ReconstructVirtualGeometry = reconstructVirtualGeometry;' in virtual,'profile owns explicit virtual geometry reconstruction')
suite.check('AERISTerrainVirtualDetailPolicy.ReconstructFar' in raster,'raster worker actually invokes CP3 virtual reconstruction')
suite.check('tile.Resolution > sourceTile.Resolution' in raster,'runtime virtual build telemetry observes geometry reconstruction')

# Candidate 1 HIGH FRONT churn fix: transient REAL65 is detail, not a world generation.
suite.check('if (!request.TransientRefinement) terrainGeneration++;' in tiles,'transient REAL65 no longer increments global terrainGeneration')
suite.check('request.TransientRefinement = true;' in tiles and 'request.Resolution = Gate4HighRealResolution;' in tiles,'HIGH REAL65 remains an explicit transient refinement')
suite.check('gate4HighRefinementPartialCommitsSuppressed++' in tiles,'partial REAL65 cannot replace complete FAR33 foundation')
complete_marker='if (request.TransientRefinement)\n            {\n                gate4HighRefinementCompleted++;'
idx=tiles.find(complete_marker)
suite.check(idx>=0,'completed transient REAL65 has dedicated atomic completion branch')
if idx>=0:
    block=tiles[idx:tiles.find('status = tile.Key.Lod',idx)]
    suite.check('ScheduleDiskWrite' not in block and 'return;' in block,'completed REAL65 remains RAM-only and does not pollute preload DB')
else:
    suite.check(False,'completed REAL65 remains RAM-only and does not pollute preload DB')
suite.check('gpuContentRevision++;' in renderer,'render-ready content revision can rebuild BACK without terrain generation churn')
suite.check('[CP3.5_GATE4_QUALITY]' in tiles and 'quality_floor=CP3_GOLDEN' in tiles,'runtime telemetry declares CP3 Golden quality floor')

# Bounded high-quality work: no all-visible REAL65/129 PQS promotion.
suite.check('const int Gate4HighRefinementMaximumTiles = 4;' in tiles,'HIGH REAL65 visible work remains capped at four tiles')
suite.check('rangeMeters > 80000.0' in tiles and 'Math.Min(limit, 2)' in tiles,'HIGH long-range REAL65 cap remains two tiles')
suite.check('rangeMeters > 40000.0' in tiles and 'Math.Min(limit, 3)' in tiles,'HIGH mid-range REAL65 cap remains three tiles')
suite.check('performance.NdMainThreadEmaMs > 3.0f' in tiles and 'performance.TilePqsSampleEmaMs > 2.0f' in tiles,'HIGH real sampling still has runtime safety gate')
suite.check('AddAdaptiveExactDetailBridge' not in tiles,'all-visible/generated exact viewport bridge stays prohibited')
suite.check('if (!ExactDetailPayloadExists(key)) continue;' in tiles,'missing Route/Local exact detail is not generated by live viewport')
suite.check('radius = Math.Max(0, Math.Min(1, requestedRadius))' in tiles,'existing exact microtile bridge stays radius <=1')

# Accepted Hotfix 1 authority fixes stay intact.
suite.check('const bool TemporalPresentationAuthorityEnabled = false;' in renderer,'temporal presentation remains quarantined')
suite.check('GUI.matrix =' not in renderer and 'GUI.matrix=' not in renderer,'GUI.matrix temporal warp remains forbidden')
suite.check('Math.Abs(frontRangeMeters - currentRangeMeters)' in renderer,'Exact FRONT still rejects wrong range')
suite.check('plan.x + plan.width * 0.5f' in nav and 'plan.y + plan.height * anchorV' in nav,'ownship remains fixed live-map anchor')
suite.check('Vector2 end = aircraftPoint + (projectedEnd - projectionOrigin);' in nav,'prediction endpoint remains ownship-relative')
suite.check('Vector2 tick = aircraftPoint + (projectedTick - projectionOrigin);' in nav,'prediction ticks remain ownship-relative')

# UI/user-quality contract.
suite.check('new string[]{"AUTO","LOW","MIDDLE","HIGH"}' in window,'quality UI remains AUTO/LOW/MIDDLE/HIGH')
suite.check('case "MEDIUM":' in settings and 'case "MIDDLE": return AERISTerrainQualityMode.Medium;' in settings,'legacy MEDIUM and new MIDDLE both parse')
suite.check('new AERISTerrainPerformanceProfile("MIDDLE"' in perf,'runtime quality identity is MIDDLE')
suite.check('48, 1, 3, 720f, 32, 1.25f, AERISTerrainTileLod.Local, 256, 2048, 192' in perf,'HIGH resource envelope remains bounded CP3/Gate2 profile')

# Syntax and package contracts.
for label,text in [('virtual',virtual),('tiles',tiles),('renderer',renderer),('raster',raster),('nav',nav)]:
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),label+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),label+' parens balanced')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE4_CP3_GOLDEN_CARTOGRAPHIC_QUALITY_CANDIDATE2.txt').is_file(),'Candidate 2 acceptance contract included')
suite.check((ROOT/'Docs/CP3.5_GATE4_CP3_GOLDEN_CARTOGRAPHIC_QUALITY_CANDIDATE2_DESIGN_ja.md').is_file(),'Candidate 2 design note included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE4_CP3_GOLDEN_CARTOGRAPHIC_QUALITY_CANDIDATE2_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Candidate 2 runtime test card included')
golden=ROOT/'Evidence/CP3_GOLDEN_VISUAL_REFERENCE'
suite.check((golden/'CP3_Golden_20km_DenseContours.png').is_file(),'20 km CP3 Golden visual reference embedded')
suite.check((golden/'CP3_Golden_160km_CoastlineReadability.png').is_file(),'160 km CP3 Golden visual reference embedded')
suite.check((golden/'CP3_Golden_LandSeaSilhouette.png').is_file(),'land/sea CP3 Golden visual reference embedded')
suite.check((golden/'README_ja.md').is_file(),'Golden visual reference contract README included')
suite.check('CP3 Golden Cartographic Quality Candidate 2' in read(ROOT/'README.md'),'top-level README documents Candidate 2')
suite.check((ROOT/'AERISFlightControl-v0.18.0.0_DEV_CP3.5_Gate4_CP3GoldenCartographicQuality_Candidate2_VERIFICATION.txt').is_file(),'final Candidate 2 verification report included')
suite.finish()
