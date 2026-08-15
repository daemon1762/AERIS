#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
T = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs"
M = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
U = ROOT / "build_ubuntu.sh"


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS25 ATROPINE REV009] %s anchor mismatch old=%d" % (label, count))
    return text.replace(old, new, 1), True


renderer = R.read_text()

const_old = '''        const float ContentMaintenanceRetrySeconds = 0.20f;\n'''
const_new = '''        const float ContentMaintenanceRetrySeconds = 0.20f;\n        // AERIS25_CONTENT_GENERATION_BURST_GOVERNOR: keep visible projection at\n        // fixed 10 Hz while bounding only hidden content commit/retirement bursts.\n        const int SteadyContentCommitMaximumResults = 2;\n        const int BootstrapContentCommitMaximumResults = 4;\n        const int NormalPruneMaximumRemovals = 4;\n        const float ContentPlanningHeadingStepDeg = 6f;\n'''
renderer, c1 = replace_once(renderer, const_old, const_new,
                            'burst governor constants')

field_old = '''        long operationHealthSnapshotMeshPruneProtected;\n        long operationHealthSnapshotMeshPruneDeferrals;\n        long operationHealthSnapshotStaleMeshDetections;\n'''
field_new = '''        long operationHealthSnapshotMeshPruneProtected;\n        long operationHealthSnapshotMeshPruneDeferrals;\n        long operationHealthSnapshotStaleMeshDetections;\n        long operationHealthContentCommitBudgetHits;\n        int operationHealthContentCommitBacklogPeak;\n        long operationHealthPruneBudgetHits;\n        long operationHealthPruneDebtPeakBytes;\n        long operationHealthContentHeadingCoalesced;\n'''
renderer, c2 = replace_once(renderer, field_old, field_new,
                            'burst governor telemetry fields')

needs_old = '''            if (contentTerrainGeneration != system.TerrainGeneration ||\n                !string.Equals(contentStyleKey, styleKey, StringComparison.Ordinal) ||\n                contentTrackUp != trackUp || contentOrientation != orientation ||\n                Math.Abs(contentAnchorV - anchorV) > 0.001f ||\n                Math.Abs(contentRangeMeters - rangeMeters) > 0.5f) return true;\n            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(contentHeadingDeg,\n                mapHeadingDeg)) >= 3f) return true;\n            double displacement = GreatCircleDistanceMeters(vessel.mainBody,\n'''
needs_new = '''            if (contentTerrainGeneration != system.TerrainGeneration ||\n                !string.Equals(contentStyleKey, styleKey, StringComparison.Ordinal) ||\n                contentTrackUp != trackUp || contentOrientation != orientation ||\n                Math.Abs(contentAnchorV - anchorV) > 0.001f ||\n                Math.Abs(contentRangeMeters - rangeMeters) > 0.5f) return true;\n            if (trackUp)\n            {\n                float headingDelta = Mathf.Abs(Mathf.DeltaAngle(contentHeadingDeg,\n                    mapHeadingDeg));\n                if (headingDelta >= ContentPlanningHeadingStepDeg) return true;\n                if (headingDelta >= 3f) operationHealthContentHeadingCoalesced++;\n            }\n            double displacement = GreatCircleDistanceMeters(vessel.mainBody,\n'''
renderer, c3 = replace_once(renderer, needs_old, needs_new,
                            'content heading coalescing threshold')

assign_old = '''                contentTerrainGeneration = visible.TerrainGeneration;\n                contentStyleKey = styleKey;\n                contentCenterLatitudeDeg = centerLatitudeDeg;\n                contentCenterLongitudeDeg = centerLongitudeDeg;\n                contentRangeMeters = rangeMeters;\n                contentHeadingDeg = mapHeadingDeg;\n                contentTrackUp = trackUp;\n'''
assign_new = '''                bool adoptContentPlanningHeading = !contentSnapshotValid || !trackUp ||\n                    contentTrackUp != trackUp || contentOrientation != orientation ||\n                    Math.Abs(contentAnchorV - anchorV) > 0.001f ||\n                    Math.Abs(contentRangeMeters - rangeMeters) > 0.5f;\n                if (!adoptContentPlanningHeading && vessel != null && vessel.mainBody != null)\n                {\n                    double contentCenterMovement = GreatCircleDistanceMeters(vessel.mainBody,\n                        contentCenterLatitudeDeg, contentCenterLongitudeDeg,\n                        centerLatitudeDeg, centerLongitudeDeg);\n                    adoptContentPlanningHeading = double.IsNaN(contentCenterMovement) ||\n                        double.IsInfinity(contentCenterMovement) ||\n                        contentCenterMovement >= Math.Max(100.0,\n                            Math.Max(1f, rangeMeters) * 0.02);\n                }\n                if (!adoptContentPlanningHeading && trackUp)\n                    adoptContentPlanningHeading = Mathf.Abs(Mathf.DeltaAngle(\n                        contentHeadingDeg, mapHeadingDeg)) >= ContentPlanningHeadingStepDeg;\n\n                contentTerrainGeneration = visible.TerrainGeneration;\n                contentStyleKey = styleKey;\n                contentCenterLatitudeDeg = centerLatitudeDeg;\n                contentCenterLongitudeDeg = centerLongitudeDeg;\n                contentRangeMeters = rangeMeters;\n                if (adoptContentPlanningHeading) contentHeadingDeg = mapHeadingDeg;\n                contentTrackUp = trackUp;\n'''
renderer, c4 = replace_once(renderer, assign_old, assign_new,
                            'content planning heading anchor')

drain_old = '''            int maximum = performance == null ? 2 :\n                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo * 2);\n            rasterizer.Drain(completed, maximum);\n'''
drain_new = '''            int profileMaximum = performance == null ? 2 :\n                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo * 2);\n            int burstMaximum = frontBufferValid && requestedViewReady ?\n                SteadyContentCommitMaximumResults : BootstrapContentCommitMaximumResults;\n            int maximum = Math.Max(1, Math.Min(profileMaximum, burstMaximum));\n            rasterizer.Drain(completed, maximum);\n            int deferredCompleted = Math.Max(0, rasterizer.CompletedCount);\n            if (deferredCompleted > 0)\n            {\n                operationHealthContentCommitBudgetHits++;\n                operationHealthContentCommitBacklogPeak = Math.Max(\n                    operationHealthContentCommitBacklogPeak, deferredCompleted);\n            }\n'''
renderer, c5 = replace_once(renderer, drain_old, drain_new,
                            'completed result commit governor')

prune_old = '''        void Prune(long totalLimit)\n        {\n            totalLimit = Math.Max(16L * 1024L * 1024L, totalLimit);\n            long fixedBytes = Math.Max(0L, backTargetBytes) +\n                Math.Max(0L, frontTargetBytes);\n            long entryLimit = Math.Max(4L * 1024L * 1024L, totalLimit - fixedBytes);\n            while (usedEntryBytes > entryLimit && entries.Count > 1)\n            {\n                Entry oldest = null;\n                foreach (Entry entry in entries.Values)\n                {\n                    if (IsEntryProtectedByContentSnapshot(entry))\n                    {\n                        operationHealthSnapshotMeshPruneProtected++;\n                        continue;\n                    }\n                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;\n                }\n                if (oldest == null)\n                {\n                    operationHealthSnapshotMeshPruneDeferrals++;\n                    break;\n                }\n                Remove(oldest);\n                evicted++;\n            }\n        }\n'''
prune_new = '''        void Prune(long totalLimit)\n        {\n            totalLimit = Math.Max(16L * 1024L * 1024L, totalLimit);\n            long fixedBytes = Math.Max(0L, backTargetBytes) +\n                Math.Max(0L, frontTargetBytes);\n            long entryLimit = Math.Max(4L * 1024L * 1024L, totalLimit - fixedBytes);\n            int removed = 0;\n            while (usedEntryBytes > entryLimit && entries.Count > 1 &&\n                removed < NormalPruneMaximumRemovals)\n            {\n                Entry oldest = null;\n                foreach (Entry entry in entries.Values)\n                {\n                    if (IsEntryProtectedByContentSnapshot(entry))\n                    {\n                        operationHealthSnapshotMeshPruneProtected++;\n                        continue;\n                    }\n                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;\n                }\n                if (oldest == null)\n                {\n                    operationHealthSnapshotMeshPruneDeferrals++;\n                    break;\n                }\n                Remove(oldest);\n                evicted++;\n                removed++;\n            }\n            if (usedEntryBytes > entryLimit && entries.Count > 1)\n            {\n                operationHealthPruneBudgetHits++;\n                operationHealthPruneDebtPeakBytes = Math.Max(\n                    operationHealthPruneDebtPeakBytes, usedEntryBytes - entryLimit);\n            }\n        }\n'''
renderer, c6 = replace_once(renderer, prune_old, prune_new,
                            'normal prune retirement governor')

telemetry_old = '''                "; oh_snapshot_stale_mesh=" + operationHealthSnapshotStaleMeshDetections +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +\n'''
telemetry_new = '''                "; oh_snapshot_stale_mesh=" + operationHealthSnapshotStaleMeshDetections +\n                "; oh_content_commit_budget_hit=" + operationHealthContentCommitBudgetHits +\n                "; oh_content_commit_backlog_peak=" + operationHealthContentCommitBacklogPeak +\n                "; oh_prune_budget_hit=" + operationHealthPruneBudgetHits +\n                "; oh_prune_debt_peak_bytes=" + operationHealthPruneDebtPeakBytes +\n                "; oh_heading_plan_coalesced=" + operationHealthContentHeadingCoalesced +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +\n'''
renderer, c7 = replace_once(renderer, telemetry_old, telemetry_new,
                            'burst governor telemetry publication')

if any((c1, c2, c3, c4, c5, c6, c7)):
    R.write_text(renderer)
    print('[AERIS25 ATROPINE REV009] content generation burst governor applied')
else:
    print('[AERIS25 ATROPINE REV009] content generation burst governor already present')

# Hidden viewport planning follows the same 6-degree cumulative Track-Up heading step.
# Unlike the renderer projection, this is request-planning authority only.
tile = T.read_text()
tile_old = '''            bool orientationChanged = !displayViewValid ||\n                displayViewTrackUp != trackUp || displayViewOrientation != orientation ||\n                Math.Abs(displayViewAnchorGuiV - normalizedAnchor) > 0.001f;\n            bool headingChanged = !displayViewValid || (trackUp &&\n                Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading)) > 3.0);\n            bool materiallyChanged = rangeChanged || centerChanged ||\n                orientationChanged || headingChanged;\n            displayViewValid = true;\n            displayViewLatitudeDeg = normalizedLatitude;\n            displayViewLongitudeDeg = normalizedLongitude;\n            displayViewRangeMeters = normalizedRange;\n            displayViewHeadingDeg = normalizedHeading;\n            displayViewTrackUp = trackUp;\n'''
tile_new = '''            bool orientationChanged = !displayViewValid ||\n                displayViewTrackUp != trackUp || displayViewOrientation != orientation ||\n                Math.Abs(displayViewAnchorGuiV - normalizedAnchor) > 0.001f;\n            // AERIS25_CONTENT_GENERATION_BURST_GOVERNOR: Track-Up visible projection\n            // still follows current heading at 10 Hz in the renderer. Only hidden\n            // foundation/request planning waits for a cumulative 6-degree step.\n            double planningHeadingDelta = !displayViewValid ? double.MaxValue :\n                Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading));\n            bool headingChanged = !displayViewValid || (trackUp &&\n                planningHeadingDelta >= 6.0);\n            bool structuralViewChanged = rangeChanged || centerChanged ||\n                orientationChanged;\n            bool materiallyChanged = structuralViewChanged || headingChanged;\n            bool acceptPlanningHeading = !displayViewValid || !trackUp ||\n                structuralViewChanged || headingChanged;\n            displayViewValid = true;\n            displayViewLatitudeDeg = normalizedLatitude;\n            displayViewLongitudeDeg = normalizedLongitude;\n            displayViewRangeMeters = normalizedRange;\n            if (acceptPlanningHeading) displayViewHeadingDeg = normalizedHeading;\n            displayViewTrackUp = trackUp;\n'''
tile, t1 = replace_once(tile, tile_old, tile_new,
                        'tile-system Track-Up planning coalescer')
if t1:
    T.write_text(tile)
    print('[AERIS25 ATROPINE REV009] TileSystem hidden Track-Up planner coalescer applied')
else:
    print('[AERIS25 ATROPINE REV009] TileSystem hidden Track-Up planner coalescer already present')

monitor = M.read_text()
if 'internal const string Revision = "OH_PHASE4_009";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_008";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV009] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_008";',
                              'internal const string Revision = "OH_PHASE4_009";', 1)
    M.write_text(monitor)
    print('[AERIS25 ATROPINE REV009] revision=OH_PHASE4_009')
else:
    print('[AERIS25 ATROPINE REV009] revision already OH_PHASE4_009')

build = U.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV008 SNAPSHOT MESH LIFETIME GUARD"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV009 CONTENT GENERATION BURST GOVERNOR"'
build, b1 = replace_once(build, old_display, new_display, 'build display identity')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV008 SNAPSHOT MESH LIFETIME GUARD";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV009 CONTENT GENERATION BURST GOVERNOR";'
build, b2 = replace_once(build, old_checkpoint, new_checkpoint, 'checkpoint identity')
old_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py"'
new_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_content_generation_burst_governor_hotfix.py"'
if new_verify not in build:
    if build.count(old_verify) != 1:
        raise SystemExit('[AERIS25 ATROPINE REV009] active rev008 verifier anchor mismatch')
    build = build.replace(old_verify, new_verify, 1)
    b3 = True
else:
    b3 = False
if any((b1, b2, b3)):
    U.write_text(build)
    print('[AERIS25 ATROPINE REV009] build identity/verifier promoted')
else:
    print('[AERIS25 ATROPINE REV009] build identity/verifier already promoted')

# Final build executes READY + rev003 + rev004 before the active rev009 verifier.
def promote_final_tree_verifier(path, variable='M'):
    text = path.read_text()
    if 'OH_PHASE4_009' not in text:
        needle = "('internal const string Revision = \"OH_PHASE4_008\";' in %s)" % variable
        if needle not in text:
            raise SystemExit('[AERIS25 ATROPINE REV009] descendant revision anchor missing in ' + path.name)
        text = text.replace(needle,
            needle + " or\n   ('internal const string Revision = \"OH_PHASE4_009\";' in %s)" % variable,
            1)
    if path.name == 'verify_aeris25_chunk_cull_guard_hotfix.py' and \
       "('REV009 CONTENT GENERATION BURST GOVERNOR' in U)" not in text:
        needle = "('REV008 SNAPSHOT MESH LIFETIME GUARD' in U),"
        if needle not in text:
            raise SystemExit('[AERIS25 ATROPINE REV009] rev003 build descendant anchor missing')
        text = text.replace(needle,
            "('REV008 SNAPSHOT MESH LIFETIME GUARD' in U) or\n   ('REV009 CONTENT GENERATION BURST GOVERNOR' in U),", 1)
    if path.name == 'verify_aeris25_temporal_foundation_overscan_hotfix.py' and \
       "('REV009 CONTENT GENERATION BURST GOVERNOR' in U)" not in text:
        needle = "('REV008 SNAPSHOT MESH LIFETIME GUARD' in U)) and"
        if needle not in text:
            raise SystemExit('[AERIS25 ATROPINE REV009] rev004 build descendant anchor missing')
        text = text.replace(needle,
            "('REV008 SNAPSHOT MESH LIFETIME GUARD' in U) or\n    ('REV009 CONTENT GENERATION BURST GOVERNOR' in U)) and", 1)
    path.write_text(text)

core = ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour.py'
core_text = core.read_text()
if '"OH_PHASE4_009"' not in core_text:
    old = '"OH_PHASE4_006", "OH_PHASE4_007", "OH_PHASE4_008")'
    new = '"OH_PHASE4_006", "OH_PHASE4_007", "OH_PHASE4_008", "OH_PHASE4_009")'
    if old not in core_text:
        raise SystemExit('[AERIS25 ATROPINE REV009] core accepted-revision anchor missing')
    core.write_text(core_text.replace(old, new, 1))

promote_final_tree_verifier(ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py', 'MON')
promote_final_tree_verifier(ROOT / 'Tools/verify_aeris25_chunk_cull_guard_hotfix.py', 'M')
promote_final_tree_verifier(ROOT / 'Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py', 'M')

print('[AERIS25 ATROPINE REV009] CONTENT GENERATION BURST GOVERNOR HOTFIX APPLIED')
print('Visible authority unchanged: exact 10 Hz projection / 160 km / Golden / Runway Map Lock')
print('Hidden governor: <=2 steady completed commits per tick, <=4 normal prunes per tick, cumulative 6deg Track-Up planning')
print('Expected runtime: stale_mesh=0, attr_fail=0, materially lower 160km/turn frame spikes')
