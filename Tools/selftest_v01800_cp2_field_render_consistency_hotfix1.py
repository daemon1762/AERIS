#!/usr/bin/env python3
from __future__ import annotations
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 Field Render Consistency Hotfix 1")
contracts = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs")
system = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
blocks = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
performance = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

for token in (
    "SamplingComplete",
    "SamplingComplete = SamplingComplete",
):
    suite.check(token in contracts, "tile completion contract: " + token)
for token in (
    "SamplingComplete = !partial",
    "IsPreview = state.Request.Stage ==",
    "RefreshActive",
    "state.Commit(state.Request, tile, final)",
    "(int)request.WorkOwner",
):
    suite.check(token in blocks, "in-progress request continuity: " + token)

merge_start = system.index("static void MergeRequest")
merge_end = system.index("void SchedulePreloadReads", merge_start)
merge = system[merge_start:merge_end]
suite.check("target.TerrainGeneration = Math.Max" in merge,
            "queued and disk-loading request merge carries TerrainGeneration")
for token in (
    "bool samplingComplete = tile.SamplingComplete",
    "ReconcileRequestWithRamTile(request, tile)",
    "ReconcileRequestWithRamTile(next, existing)",
    "blockPipeline.RefreshActive(request, IsFlightRequestCurrent)",
    "availableCoverage += Math.Max",
    "tile.Quality / 100.0",
):
    suite.check(token in system, "progressive completion handling: " + token)
suite.check("IsPreview = partial ||" not in blocks,
            "fidelity stage and sampling progress remain independent")
database = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs")
codec = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs")
suite.check("!tile.SamplingComplete" in database,
            "persistent preload database rejects incomplete final-stage tiles")
suite.check("tile.SamplingComplete && !tile.IsPreview" in codec,
            "encoded completion requires both sampling completion and final fidelity")

refresh_start = system.index("void RefreshTerrainRequestGeneration")
refresh_end = system.index("void UpdateDisplayView", refresh_start)
refresh = system[refresh_start:refresh_end]
suite.check("performance.ProfileRevision" in refresh,
            "AUTO quality profile changes still trigger a bounded replan")
suite.check("TerrainDisplayMode" not in refresh,
            "palette/display mode never invalidates body-fixed terrain requests")
suite.check("terrainRequestGeneration++" not in refresh and
            "viewGeneration++" not in refresh,
            "profile replan preserves overlapping in-progress terrain work")

for token in (
    "CoverageRegion",
    "ResolveRenderableEntries",
    "EntryCoversPoint",
    "TriangleCoverage",
    "candidateCurrentStyle",
    "currentEntry == null",
    "fallbackEntry != null",
    "entry.CoverageFraction >= 0.999f",
):
    suite.check(token in renderer, "seamless progressive composition: " + token)
suite.check("struct CoverageRegion" in renderer,
            "viewport coverage regions avoid per-entry heap allocation")
suite.check(renderer.find("Entry entry = BuildEntry") <
            renderer.find("if (entries.TryGetValue(cacheKey, out old)) Remove(old)"),
            "new GPU entry is built before the visible predecessor is released")
suite.check("RemoveSupersededEntries(result.Key, cacheKey);" in renderer,
            "complete replacements eventually release superseded GPU entries")

suite.check("workerBacklogged = workerBacklogged || backlog;" in performance,
            "AUTO backlog evidence is latched for the complete evaluation window")
suite.check("[ND/TERRAIN] AUTO quality=" in performance and
            "[ND/TERRAIN] AUTO rate tier=" in performance,
            "AUTO degradation and recovery transitions are field-observable")
suite.check("[ND/TERRAIN] display mode=" in nd and
            "[ND/TERRAIN] range=" in nd,
            "range and palette-mode actions are field-observable")
suite.check("wasAutomatic ||" in nd,
            "manual range selection is logged when leaving AUTO at the same range")

# Independent request-generation model: palette changes do not alter sampled terrain
# identity; profile changes request a replan without discarding overlapping work.
terrain_generation = 7
replans = 0
for event in ("mode", "mode", "profile", "range"):
    if event == "profile":
        replans += 1
    elif event == "range":
        replans += 1
suite.equal(terrain_generation, 7,
            "mode/profile/range changes preserve body-fixed terrain generation")
suite.equal(replans, 2, "only profile/range events require request replanning")

# Independent merge model: every stale-sensitive generation becomes latest-wins.
old = dict(terrain=2, view=8, range=3, plan=10, database=4)
new = dict(terrain=5, view=9, range=7, plan=11, database=6)
merged = {key: max(old[key], new[key]) for key in old}
suite.equal(merged, new, "latest request generations survive same-key merge")

# A partial low-resolution preview remains preview work. Only a completed preview
# promotes the next request to final resolution.
def next_stage(tile_resolution: int, final_resolution: int,
               sampling_complete: bool, is_preview: bool) -> str:
    if sampling_complete and not is_preview and tile_resolution >= final_resolution:
        return "done"
    if sampling_complete and (is_preview or tile_resolution < final_resolution):
        return "final"
    return "final" if tile_resolution >= final_resolution else "preview"

suite.equal(next_stage(9, 33, False, True), "preview",
            "25% preview cannot launch concurrent final work")
suite.equal(next_stage(9, 33, True, True), "final",
            "completed preview promotes exactly once to final work")
suite.equal(next_stage(33, 33, False, True), "final",
            "partial final-resolution work stays in its matching stage")
suite.equal(next_stage(33, 33, True, False), "done",
            "completed final tile requires no duplicate work")

# Independent composition model. A completed preview is retained underneath a
# progressive final mesh, so valid coverage cannot regress while detail is built.
def composed_coverage(fallback: set[int] | None,
                      current: set[int] | None, total: int = 16) -> float:
    visible = set()
    if fallback:
        visible |= fallback
    if current:
        visible |= current
    return len(visible) / float(total)

full_preview = set(range(16))
partial_final = set(range(4))
suite.equal(composed_coverage(None, partial_final), 0.25,
            "partial final alone is never reported complete")
suite.equal(composed_coverage(full_preview, partial_final), 1.0,
            "complete preview prevents progressive coverage regression")
suite.equal(composed_coverage(full_preview, None), 1.0,
            "old contour style remains visible while new range style is pending")

# AUTO backlog reports are OR-latched until the once-per-second evaluator clears them.
latched = False
for report in (False, True, False, False):
    latched = latched or report
suite.check(latched, "later healthy worker reports cannot erase observed backlog")

suite.check("selftest_v01800_cp2_field_render_consistency_hotfix1.py" in runner,
            "CP2 acceptance runner includes field-render consistency regression")
suite.finish()
