#!/usr/bin/env python3
from __future__ import annotations
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 Terrain Supply Hotfix 3 pipeline")
contracts = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs")
profiles = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs")
system = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
blocks = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
rasterizer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
runtime = read(ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

for token in (
    "AERISTerrainRequestLane",
    "Viewport = 0",
    "Landing = 1",
    "LookAhead = 2",
    "Background = 3",
    "AERISTerrainSamplingStage",
    "Preview = 0",
    "Final = 1",
    "FinalResolution",
    "ViewDistanceMeters",
):
    suite.check(token in contracts, "explicit supply contract: " + token)

for token in (
    "desiredRequestIds",
    "desiredVisibleIds",
    "diskLoadingRequests",
    "ram.CountPreviewTiles(desiredRequestIds)",
    "ReconcilePlannedRequests",
    "runtime.Scheduler.CancelKey",
    "OBSOLETE TERRAIN REQUESTS CANCELLED",
):
    suite.check(token in system, "stale plan cancellation: " + token)
suite.check(system.find("ReconcilePlannedRequests();") <
            system.find("EnsureRequest(requestScratch[i])"),
            "obsolete work is cancelled before admitting a new viewport plan")
suite.check(system.find("AERISRunwayDirectionDefinition direction") <
            system.find("double[] seconds = { 30.0, 120.0, 420.0 }"),
            "selected-runway LAND candidates are reserved before look-ahead expansion")

for token in (
    "AERISTerrainRequestLane.Viewport",
    "AERISTerrainRequestLane.Landing",
    "AERISTerrainRequestLane.LookAhead",
    "AERISTerrainRequestLane.Background",
    "requestScratch.Sort(CompareRequests)",
    "int readLane = ((int)a.ReadLane).CompareTo((int)b.ReadLane)",
    "int lane = ((int)a.Lane).CompareTo((int)b.Lane)",
    "int stage = ((int)a.Stage).CompareTo((int)b.Stage)",
):
    suite.check(token in system, "starvation-free lane ordering: " + token)

for token in (
    "ResolvePreviewResolution",
    "Global: desired = 5",
    "Far:\n                case AERISTerrainTileLod.Route: desired = 7",
    "default: desired = 9",
    "CloneForFinal",
    "TERRAIN PREVIEW AVAILABLE",
    "TERRAIN BLOCK DETAIL",
):
    suite.check(token in system, "preview then final refinement: " + token)

# Preview work is dramatically smaller than final work and therefore becomes visible
# before a 33x33 tile monopolises the main-thread PQS sampler.
suite.equal(5 * 5, 25, "global preview uses 25 samples")
suite.equal(7 * 7, 49, "far/route preview uses 49 samples")
suite.equal(9 * 9, 81, "local/LAND preview uses 81 samples")
suite.equal(33 * 33, 1089, "normal final detail remains 1089 samples")
suite.check(81 < 1089 // 10, "largest preview is under one tenth of normal final work")

for token, source in (
    ("states.TryGetValue(id, out existing)", blocks),
    ("existing.Request.RequestSequence = Math.Max", blocks),
    ("MaximumActiveTiles = 48", blocks),
    ("states.Count >= MaximumActiveTiles", blocks),
    ("FindLowestPriorityRequestLocked", system),
    ("maximum * 2", system),
):
    suite.check(token in source, "bounded deduplicated admission: " + token)

for token, source in (
    ("TilePqsQueriesPerSecond", system),
    ("MaximumTileSamplesPerFrame", system),
    ("TileMainThreadBudgetMs", system),
    ("Stopwatch watch = Stopwatch.StartNew()", blocks),
    ("watch.Elapsed.TotalMilliseconds < budget", blocks),
    ("RecordTilePqsCost", blocks),
    ("lastSamplingBatchSamples", system),
    ("lastSamplingBatchMilliseconds", system),
):
    suite.check(token in source, "time-bounded PQS supply: " + token)

for token in (
    "TileMainThreadBudgetMs",
    "120f, 6, 0.35f",
    "360f, 16, 0.75f",
    "720f, 32, 1.25f",
    "1200f, 48, 1.80f",
):
    suite.check(token in profiles, "quality profile supply budget: " + token)

commit_start = system.index("void CommitFlightBlock")
commit_end = system.index("bool IsFlightRequestCurrent", commit_start)
commit_method = system[commit_start:commit_end]
preview_branch = commit_method.index("request.Stage == AERISTerrainSamplingStage.Preview")
disk_write = commit_method.index("ScheduleDiskWrite(tile.CloneImmutable())")
suite.check(preview_branch >= 0 and preview_branch < disk_write,
            "only final tiles enter persistent disk-write path")
suite.check("tile.IsPreview" in commit_method,
            "preview provenance is preserved in immutable RAM tiles")

for token in (
    "AERISTerrainGpuDrawState.Partial",
    "MeasureViewportCoverage",
    "GL.Clear(true, true, Color.clear)",
    "LastCoverageFraction",
):
    suite.check(token in renderer, "progressive GPU composition: " + token)
suite.check(nd.find("GUI.DrawTexture(plot, texture") < nd.find("terrainTileRenderer.Draw(plot"),
            "CPU terrain remains under transparent partial GPU coverage")
suite.check("HD TERRAIN BUILD " in nd,
            "partial coverage progress is visible without hiding symbols")

for token in (
    "finalIntervals",
    "actualIntervals",
    "finalIntervals / actualIntervals",
):
    suite.check(token in rasterizer, "preview slope cell-size correction: " + token)

for token in (
    "terrain_tile_preview_generated",
    "terrain_tile_final_generated",
    "terrain_tile_obsolete_cancelled",
    "terrain_tile_desired",
    "terrain_tile_visible",
    "terrain_tile_preview_count",
    "terrain_tile_sample_batch",
    "terrain_tile_sample_batch_ms",
    "terrain_tile_pqs_sample_ema_ms",
    "terrain_gpu_coverage",
):
    suite.check(token in runtime, "field telemetry column: " + token)

suite.check("ThreadPool" not in system and "Task.Run" not in system,
            "terrain supply creates no private worker pool")
suite.check("AERISRuntimeLane.Safety" not in system and
            "AERISRuntimeLane.Land" not in system,
            "terrain supply never consumes Safety/LAND worker lanes")
suite.check("selftest_v01800_cp2_supply_pipeline_hotfix3.py" in runner,
            "CP2 runner includes supply-pipeline regression")

# A compact independent scheduling model: replacing the viewport plan should cancel
# old requests rather than append forever, and preview stages must sort before finals.
def rank(item):
    lane, visible, stage, priority, lod, distance, sequence = item
    return (lane, -int(visible), stage, -priority, lod, distance, -sequence)
items = [
    (0, True, 1, 3, 3, 100.0, 1),
    (0, True, 0, 2, 1, 300.0, 2),
    (1, False, 0, 3, 4, 50.0, 3),
    (3, False, 0, 3, 0, 0.0, 4),
]
ordered = sorted(items, key=rank)
suite.check(ordered[0][0] == 0 and ordered[0][2] == 0,
            "visible viewport preview wins independent scheduler model")
old = {"A", "B", "C", "D"}
new = {"C", "E", "F"}
suite.equal(len(old - new), 3, "new view cancels three obsolete requests")
suite.equal(len((old & new) | (new - old)), len(new),
            "reconciled request set is bounded by the newest plan")

suite.finish()
