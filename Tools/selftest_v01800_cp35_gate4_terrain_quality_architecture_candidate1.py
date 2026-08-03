#!/usr/bin/env python3
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals
suite=CheckSuite('v0.18.0.0 CP3.5 Gate 4 Terrain Quality Architecture Candidate 1')
virtual=read(SOURCE/'Terrain/AERISTerrainVirtualDetail.cs')
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
contracts=read(SOURCE/'Terrain/AERISTerrainTileContracts.cs')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
raster=read(SOURCE/'Terrain/AERISTerrainGpuTileRasterizer.cs')
perf=read(SOURCE/'Terrain/AERISTerrainPerformance.cs')
settings=read(SOURCE/'Settings/AERISSettings.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
nav=read(SOURCE/'UI/AERISNavigationDisplay.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
identity='DEV CP3.5 GATE 4 — TERRAIN QUALITY ARCHITECTURE CANDIDATE 1'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,'generated Gate 4 identity')
suite.check('internal const string UiCheckpoint = "'+identity+'"' in build,'build-generated Gate 4 identity')
suite.check('Gate 4 Terrain Quality Architecture Candidate 1' in avc,'AVC Gate 4 identity')
# Frozen preload/base format remains 33x33.
suite.check('internal const int DefaultResolution = 33;' in contracts,'persistent/default terrain resolution remains 33')
suite.check('internal const int GlobalResolution = 17;' in contracts,'global terrain resolution remains 17')
# User quality model.
suite.check('internal const int LowRealResolution = 33;' in virtual,'LOW real resolution is 33')
suite.check('internal const int MiddleRealResolution = 33;' in virtual,'MIDDLE real resolution reuses 33')
suite.check('internal const int MiddleVirtualResolution = 65;' in virtual,'MIDDLE logical virtual resolution is 65')
suite.check('internal const int HighRealResolution = 65;' in virtual,'HIGH selective real resolution is 65')
suite.check('internal const int HighVirtualResolution = 129;' in virtual,'HIGH logical virtual resolution is 129')
suite.check('"LOW REAL 33 NATIVE"' in virtual and 'false, 1.00f' in virtual,'LOW is native 33')
suite.check('"MIDDLE REAL 33 -> VIRTUAL 65"' in virtual and 'false, 1.25f' in virtual,'MIDDLE uses lightweight presentation upscale, no geometry inflation')
suite.check('"HIGH REAL 65 -> VIRTUAL 129 + SPARSE EXACT"' in virtual and 'true, 1.50f' in virtual,'HIGH uses real65 plus virtual129')
suite.check('working.Resolution < profile.BaseRealResolution' in virtual,'virtual129 requires an actual real65 source')
suite.check('if (working.Resolution > profile.BaseRealResolution)' in virtual,'LOW/MIDDLE downsample stale high RAM payloads worker-side')
# UI and compatibility.
suite.check('new string[]{"AUTO","LOW","MIDDLE","HIGH"}' in window,'terrain quality UI is AUTO/LOW/MIDDLE/HIGH')
suite.check('case "MEDIUM":' in settings and 'case "MIDDLE": return AERISTerrainQualityMode.Medium;' in settings,'legacy MEDIUM and new MIDDLE both parse')
suite.check('new AERISTerrainPerformanceProfile("MIDDLE"' in perf,'runtime performance profile renamed MIDDLE')
# HIGH real65 is bounded and transient.
suite.check('const int Gate4HighRealResolution = 65;' in tiles,'Gate4 high real resolution constant')
suite.check('const int Gate4HighRefinementMaximumTiles = 4;' in tiles,'HIGH real65 visible work is capped at four tiles')
suite.check('PromoteGate4HighRealFarRefinement(range);' in tiles,'HIGH refinement is planned after 33 foundation admission')
suite.check('rangeMeters > 80000.0' in tiles and 'Math.Min(limit, 2)' in tiles,'long-range HIGH caps real65 at two tiles')
suite.check('rangeMeters > 40000.0' in tiles and 'Math.Min(limit, 3)' in tiles,'mid-range HIGH caps real65 at three tiles')
suite.check('performance.NdMainThreadEmaMs > 3.0f' in tiles and 'performance.TilePqsSampleEmaMs > 2.0f' in tiles,'HIGH real sampling has main-thread/PQS safety gate')
suite.check('!resident.SamplingComplete' in tiles and 'resident.Resolution < AERISTerrainTileFormat.DefaultResolution' in tiles,'HIGH refinement rejects incomplete/sub-33 foundation')
suite.check('request.Resolution = Gate4HighRealResolution;' in tiles and 'request.TransientRefinement = true;' in tiles,'selected HIGH requests become transient real65')
suite.check('internal bool TransientRefinement;' in contracts,'terrain request carries transient refinement contract')
suite.check('if (request.TransientRefinement)' in tiles and 'GATE 4 HIGH REAL 65 REFINEMENT' in tiles,'transient real65 bypass path exists')
suite.check('gate4HighRefinementPartialCommitsSuppressed++' in tiles,'partial real65 does not replace visible real33 foundation')
suite.check('if (request.TransientRefinement)\n            {\n                gate4HighRefinementCompleted++;' in tiles,'completed real65 has dedicated completion path')
transient_block=tiles[tiles.find('if (request.TransientRefinement)\n            {\n                gate4HighRefinementCompleted++;'):]
transient_block=transient_block[:transient_block.find('status = tile.Key.Lod') if 'status = tile.Key.Lod' in transient_block else 1500]
suite.check('ScheduleDiskWrite' not in transient_block,'HIGH real65 completion is RAM-only and does not pollute preload DB')
# GPU/presentation behavior.
suite.check('virtualDetail.RenderTargetScale' in renderer,'quality profile controls bounded render-target upscale')
suite.check('int resolution = left.Resolution.CompareTo(right.Resolution);' in renderer,'higher-resolution same-LOD tile overlays base tile last')
suite.check('tile.Resolution > sourceTile.Resolution' in raster,'virtual geometry telemetry counts real reconstruction only')
suite.check('AERISTerrainVirtualDetailPolicy.ReconstructFar' in raster,'worker raster path applies high virtual reconstruction')
suite.check('worldSurfaceMaterial.mainTexture = Texture2D.whiteTexture;' in renderer,'unified ND world-surface material remains present')
# Safety boundaries from successful Gate3 Hotfix1 remain.
suite.check('const bool TemporalPresentationAuthorityEnabled = false;' in renderer,'temporal presentation remains quarantined')
suite.check('GUI.matrix =' not in renderer and 'GUI.matrix=' not in renderer,'GUI.matrix warp remains prohibited')
suite.check('plan.x + plan.width * 0.5f' in nav and 'plan.y + plan.height * anchorV' in nav,'live ownship remains fixed-anchor outside PLAN')
suite.check('Vector2 end = aircraftPoint + (projectedEnd - projectionOrigin);' in nav,'prediction endpoint remains ownship-relative')
suite.check('Vector2 tick = aircraftPoint + (projectedTick - projectionOrigin);' in nav,'prediction ticks remain ownship-relative')
suite.check('Math.Abs(frontRangeMeters - currentRangeMeters)' in renderer,'latched FRONT still rejects wrong range')
# Telemetry must expose what HIGH actually did at runtime.
suite.check('[CP3.5_GATE4_QUALITY]' in tiles and 'high_refine_requested=' in tiles and 'high_refine_completed=' in tiles,'Gate4 quality/refinement telemetry included')
# Dense-file syntax sanity.
for label,text in [('virtual',virtual),('tiles',tiles),('renderer',renderer),('raster',raster),('nav',nav)]:
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),label+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),label+' parens balanced')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE4_TERRAIN_QUALITY_ARCHITECTURE_CANDIDATE1.txt').is_file(),'Gate4 acceptance contract included')
suite.check((ROOT/'Docs/CP3.5_GATE4_TERRAIN_QUALITY_ARCHITECTURE_CANDIDATE1_DESIGN_ja.md').is_file(),'Gate4 design note included')
suite.check((ROOT/'Docs/ND_CP3.5_GATE4_TERRAIN_QUALITY_ARCHITECTURE_CANDIDATE1_TEST_CARD_v0.18.0.0_ja.md').is_file(),'Gate4 runtime test card included')
suite.finish()
