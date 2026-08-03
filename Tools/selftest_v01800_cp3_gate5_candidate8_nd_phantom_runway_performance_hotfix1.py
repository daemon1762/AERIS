#!/usr/bin/env python3
from pathlib import Path
import hashlib
import sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3 Gate 5 Candidate 8 ND Phantom Runway / Performance Hotfix 1')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
map_dram=read(SOURCE/'Performance/AERISMapDramCache.cs')
db=read(SOURCE/'Terrain/AERISTerrainPreloadDatabase.cs')
builder=read(SOURCE/'Terrain/AERISTerrainPreloadBuilder.cs')
pipeline=read(SOURCE/'Terrain/AERISTerrainBlockPipeline.cs')
gpu=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
runner=read(ROOT/'Tools/run_v01800_cp3_gate5_acceptance.py')
readme=read(ROOT/'README.md')

for name,text in (
    ('ND',nd),('Map DRAM',map_dram),('Preload DB',db),('Preload builder',builder),
    ('Terrain block pipeline',pipeline),('GPU renderer',gpu)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

clean_nd=strip_csharp_comments_and_literals(nd)
clean_map=strip_csharp_comments_and_literals(map_dram)
clean_db=strip_csharp_comments_and_literals(db)
clean_pipeline=strip_csharp_comments_and_literals(pipeline)
clean_gpu=strip_csharp_comments_and_literals(gpu)

# Phantom-runway root cause and regression boundary.
preview_start=nd.find('void PreviewRunwayAt')
preview_end=nd.find('void DrawPreviewPanel',preview_start)
preview=nd[preview_start:preview_end]
draw_nav_start=nd.find('void DrawPreparedNavigation')
draw_nav_end=nd.find('void DrawPreparedRunway',draw_nav_start)
draw_nav=nd[draw_nav_start:draw_nav_end]
draw_runway_start=nd.find('void DrawPreparedRunway')
draw_runway_end=nd.find('void DrawSelectedRunwayEdgePointer',draw_runway_start)
draw_runway=nd[draw_runway_start:draw_runway_end]

suite.check('terrainTileRenderer.PresentedProjection' in preview and
            'centerLatitudeDeg = presented.CenterLatitudeDeg' in preview and
            'range = presented.RangeMeters' in preview and
            'orientation = presented.Orientation' in preview,
            'runway hit-testing consumes exact committed GPU FRONT projection')
suite.check('if (!aInside && !bInside && !centerInside) continue;' in preview,
            'invisible runway cannot become a click-preview candidate')
suite.check('RunwayMayIntersectVisibleMap' in preview and
            'RunwayMayIntersectVisibleMap' in draw_nav,
            'runway hit-testing and rendering share conservative off-screen pre-cull')
suite.check('if (!runway.SelectedRunway && !RunwayMayIntersectVisibleMap' in draw_nav,
            'selected runway edge-pointer behavior bypasses pre-cull')
suite.check('AERISNdMapProjection runwayProjection = AERISNdMapProjection.Create' in draw_nav,
            'runway layer creates one shared spherical projection per repaint')
suite.check('AERISNdMapProjection.Create' not in draw_runway,
            'per-runway projection reconstruction is removed')
suite.check('TryProjectGeographicPoint(projection' in draw_runway and
            'TryProjectGeographicPoint(projection' in preview,
            'renderer and hit-test reuse immutable map projection')
suite.check('DrawSelectedRunwayEdgePointer' in nd,
            'explicitly selected off-scale runway pointer remains available')

# Quality-neutral ND instrumentation reductions.
suite.check('nextTerrainTelemetrySampleRealtime = telemetryNow + 0.5f' in nd and
            'cachedTerrainTileTelemetry' in nd,
            'terrain cache telemetry snapshot allocation is limited to 2 Hz')
suite.check('sampleGc = repaint && now >= nextNdGcSampleRealtime' in nd and
            'nextNdGcSampleRealtime = now + 1f' in nd,
            'ND GC memory sampling is limited to 1 Hz repaint sampling')
suite.check('Stopwatch.GetTimestamp()' in nd and 'Stopwatch.StartNew()' not in
            strip_csharp_comments_and_literals(nd),
            'ND event timing probe is allocation-free')

# Map DRAM hot path.
suite.check('AirfieldEstimatedBytes' in map_dram and 'TerrainEstimatedBytes' in map_dram,
            'Map DRAM snapshot carries cached domain-size estimates')
with_terrain=map_dram[map_dram.find('WithTerrain('):map_dram.find('static string BuildStatus')]
suite.check('EstimateAirfieldBytes(previous.airfields)' not in with_terrain and
            'terrainBytes += entry.EstimatedBytes' in with_terrain,
            'terrain publish avoids redundant full airfield/terrain size rescans')
suite.check('suppressedRoutineTerrainPublishLogs' in map_dram and
            'suppressedRoutineTerrainPublishLogs >= 64L' in map_dram,
            'routine terrain-index INFO logs are coalesced instead of emitted per commit')
suite.check('mapIndexEntryCache' in db and
            'published.Key.Equals(entry.Key)' in db,
            'unchanged immutable Map DRAM tile wrappers are reused')
publish_start=db.find('void PublishMapIndexLocked')
publish_end=db.find('void SaveManifestLocked',publish_start)
publish=db[publish_start:publish_end]
suite.check('entries.Sort' not in publish,
            'DRAM-only publish no longer sorts a list whose ordering has no lookup semantics')
suite.check('entries.Sort' in db[db.find('void SaveManifestLocked'):],
            'persistent manifest keeps deterministic sorted order')

# Main-thread PQS work must yield under measured frame/ND pressure without changing data quality.
budget=builder[builder.find('bool ResolveBudget'):builder.find('void ApplySpeedProfile')]
suite.check('ResolvePerformanceLoadScale()' in budget and
            'milliseconds = Math.Max(1.0f, milliseconds * loadScale)' in budget and
            'samples = Math.Max(32, Math.Min(samples' in budget,
            'background PQS preload budget load-sheds under runtime pressure')
suite.check('Mathf.Clamp(frameMilliseconds * 0.70f, 8f, 24f)' in budget and
            'qps = 100000f' in budget,
            'validated CP2.5 standard producer envelope remains the starting contract')
suite.check('Stopwatch.GetTimestamp()' in pipeline and
            'watch.Elapsed.TotalMilliseconds < budget' not in
            strip_csharp_comments_and_literals(pipeline),
            'PQS per-frame deadline uses allocation-free timestamp timing')
suite.check('Stopwatch.GetTimestamp()' in gpu and
            'Stopwatch.StartNew()' not in strip_csharp_comments_and_literals(gpu),
            'GPU frame/upload timing probes are allocation-free')

# Visual-quality and authority files that must remain byte-identical to Candidate 7.
frozen={
 'Terrain/AERISTerrainTileContracts.cs':'7790977cd845c58767a70f193db3efbfc573812706466b477846b06447440f86',
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
}
for rel,expected in frozen.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==expected,
                'Candidate 7 quality/authority boundary remains byte-identical: '+rel)
suite.check((ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg').read_text(errors='replace').count('Calibration\n{')>=40,
            'original 40-runway field-verified baseline remains present; successor may add verified defaults')
for token in ('FlightCtrlState','MainThrottle','mainThrottle','OnFlyByWire'):
    suite.check(token not in clean_map and token not in clean_db and
                token not in clean_pipeline and token not in clean_gpu and
                token not in clean_nd,
                'performance/ND hotfix remains control-authority free: '+token)

# Identity, evidence and active acceptance.
expected='UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 8 — ND PHANTOM RUNWAY / PERFORMANCE HOTFIX 1"'
suite.check(expected in version and expected in build,'Candidate 8 tab/build identity exact')
suite.check('Candidate 8 ND Phantom Runway / Performance Hotfix 1' in avc,
            'Candidate 8 AVC identity')
suite.check('Candidate 8' in readme and 'phantom-runway' in readme.lower() and
            'quality' in readme.lower(),'README documents Candidate 8 scope')
suite.check('selftest_v01800_cp3_gate5_candidate8_nd_phantom_runway_performance_hotfix1.py' in runner,
            'Gate 5 acceptance executes Candidate 8 regression first')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3_GATE5_CANDIDATE8_ND_PHANTOM_RUNWAY_PERFORMANCE_HOTFIX1.txt').is_file(),
            'Candidate 8 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3_GATE5_CANDIDATE8_ND_PHANTOM_RUNWAY_PERFORMANCE_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 8 runtime test card present')
suite.check((ROOT/'Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl50_ND_PHANTOM_RUNWAY_PERFORMANCE.txt').is_file(),
            'Candidate 8 runtime diagnosis evidence present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3_GATE5_CANDIDATE8_ND_PHANTOM_RUNWAY_PERFORMANCE_HOTFIX1.txt').is_file(),
            'Candidate 8 source diff audit present')
suite.finish()
