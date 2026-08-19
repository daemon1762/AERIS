#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
N = ROOT / 'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS28 REV3.5 SALBUTAMOL SULFATE R013 STABLE CONTENT SNAPSHOT RECONCILE]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'
R012 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R012_COLD_START_PRELOAD_READY_RECOVERY'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, O, P, N, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
preload = P.read_text()
nav = N.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R010 not in renderer:
    fail('R010 generated parent required before R013 overlay')
if '[OH_REV3_5_R011_TURN_CHURN]' not in observer:
    fail('R011 observer required before R013 overlay')
if 'appliedPointSetSignature' not in preload or 'deferredPointSetInvalidation' not in preload:
    fail('R012 preload-ready recovery parent missing')
if 'RELOADING ND\\nTERRAIN INIT' not in nav:
    fail('R012 terrain-init presentation parent missing')
if R013 in renderer:
    print(PREFIX + ' already present')
    raise SystemExit(0)

# R013 does not change the 10 Hz presentation clock or the REV009 6-degree hidden
# planner threshold. It separates a true geographic/content snapshot refresh from a
# completion/retry reconcile. Completion-only maintenance keeps the exact same captured
# visible tile set and planning anchors, then re-runs the existing Entry resolution,
# foundation readiness and scheduling path against that stable material snapshot.
identity_old = '        const string Rev35R010Variant = "' + R010 + '";\n'
identity_new = identity_old + (
    '        // ' + R013 + ': worker completion/retry maintenance may reconcile the\n'
    '        // existing immutable content snapshot without re-running CaptureVisible.\n'
    '        const string Rev35R013Variant = "' + R013 + '";\n')
renderer, _ = replace_once(renderer, identity_old, identity_new,
                           'R013 renderer identity')

field_old = '''        long operationHealthRev35R010QueueBacklogBudgetSamples;
        int operationHealthRev35R010QueueBacklogPeak;
'''
field_new = field_old + '''        long operationHealthRev35R013SnapshotReuses;
        long operationHealthRev35R013FullCaptures;
        long operationHealthRev35R013CompletionReconciles;
        long operationHealthRev35R013RetryReconciles;
'''
renderer, _ = replace_once(renderer, field_old, field_new,
                           'R013 telemetry fields')

capture_old = '''                DrainCompleted(system);
                // CaptureVisible owns planner-generation updates and RAM tile selection.
                // Step 2 simply stops invoking this allocation/resolve path for pure motion.
                visible = system.CaptureVisible(centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
                operationHealthContentCaptures++;
                if (visible == null || visible.Tiles == null ||
                    visible.Tiles.Length == 0)
                {
                    ResetContentSnapshot();
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                    lastCoverageFraction = 0f;
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    TryPresentCoalescedFront(plot, vessel);
                    return lastDrawState;
                }

'''
capture_new = '''                DrainCompleted(system);
                // R013: a worker completion or bounded retry does not by itself change the
                // geographic request. Keep the captured material snapshot and its planning
                // anchors stable; only true geometry/view change re-enters CaptureVisible.
                bool rev35R013ReuseStableSnapshot = !contentGeometryChanged &&
                    contentSnapshotValid && visible != null && tiles != null &&
                    tiles.Length > 0;
                if (rev35R013ReuseStableSnapshot)
                {
                    operationHealthRev35R013SnapshotReuses++;
                    if (workerResultReady)
                        operationHealthRev35R013CompletionReconciles++;
                    if (contentRetryDue)
                        operationHealthRev35R013RetryReconciles++;
                }
                else
                {
                    visible = system.CaptureVisible(centerLatitudeDeg,
                        centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                        anchorV, orientation);
                    operationHealthContentCaptures++;
                    operationHealthRev35R013FullCaptures++;
                    if (visible == null || visible.Tiles == null ||
                        visible.Tiles.Length == 0)
                    {
                        ResetContentSnapshot();
                        nextContentMaintenanceRealtime = presentationNow +
                            ContentMaintenanceRetrySeconds;
                        lastCoverageFraction = 0f;
                        lastDrawState = AERISTerrainGpuDrawState.Partial;
                        TryPresentCoalescedFront(plot, vessel);
                        return lastDrawState;
                    }
                }

'''
renderer, _ = replace_once(renderer, capture_old, capture_new,
                           'R013 stable snapshot reuse gate')

sort_old = '                tiles = PrepareSortedTileScratch(visible.Tiles);\n'
sort_new = '''                if (!rev35R013ReuseStableSnapshot)
                    tiles = PrepareSortedTileScratch(visible.Tiles);
'''
renderer, _ = replace_once(renderer, sort_old, sort_new,
                           'R013 stable sorted tile reuse')

# REV009 already corrected heading planning to cumulative 6 degrees. Its remaining
# assignment block still advances geographic center/range anchors on every completion
# maintenance tick. Wrap the whole planner-anchor adoption block so completion/retry
# reconcile cannot silently move the reference snapshot.
anchor_old = '''                bool adoptContentPlanningHeading = !contentSnapshotValid || !trackUp ||
                    contentTrackUp != trackUp || contentOrientation != orientation ||
                    Math.Abs(contentAnchorV - anchorV) > 0.001f ||
                    Math.Abs(contentRangeMeters - rangeMeters) > 0.5f;
                if (!adoptContentPlanningHeading && vessel != null && vessel.mainBody != null)
                {
                    double contentCenterMovement = GreatCircleDistanceMeters(vessel.mainBody,
                        contentCenterLatitudeDeg, contentCenterLongitudeDeg,
                        centerLatitudeDeg, centerLongitudeDeg);
                    adoptContentPlanningHeading = double.IsNaN(contentCenterMovement) ||
                        double.IsInfinity(contentCenterMovement) ||
                        contentCenterMovement >= Math.Max(100.0,
                            Math.Max(1f, rangeMeters) * 0.02);
                }
                if (!adoptContentPlanningHeading && trackUp)
                    adoptContentPlanningHeading = Mathf.Abs(Mathf.DeltaAngle(
                        contentHeadingDeg, mapHeadingDeg)) >= ContentPlanningHeadingStepDeg;

                contentTerrainGeneration = visible.TerrainGeneration;
                contentStyleKey = styleKey;
                contentCenterLatitudeDeg = centerLatitudeDeg;
                contentCenterLongitudeDeg = centerLongitudeDeg;
                contentRangeMeters = rangeMeters;
                if (adoptContentPlanningHeading) contentHeadingDeg = mapHeadingDeg;
                contentTrackUp = trackUp;
                contentAnchorV = anchorV;
                contentOrientation = orientation;
'''
anchor_new = '''                if (!rev35R013ReuseStableSnapshot)
                {
                    bool adoptContentPlanningHeading = !contentSnapshotValid || !trackUp ||
                        contentTrackUp != trackUp || contentOrientation != orientation ||
                        Math.Abs(contentAnchorV - anchorV) > 0.001f ||
                        Math.Abs(contentRangeMeters - rangeMeters) > 0.5f;
                    if (!adoptContentPlanningHeading && vessel != null && vessel.mainBody != null)
                    {
                        double contentCenterMovement = GreatCircleDistanceMeters(vessel.mainBody,
                            contentCenterLatitudeDeg, contentCenterLongitudeDeg,
                            centerLatitudeDeg, centerLongitudeDeg);
                        adoptContentPlanningHeading = double.IsNaN(contentCenterMovement) ||
                            double.IsInfinity(contentCenterMovement) ||
                            contentCenterMovement >= Math.Max(100.0,
                                Math.Max(1f, rangeMeters) * 0.02);
                    }
                    if (!adoptContentPlanningHeading && trackUp)
                        adoptContentPlanningHeading = Mathf.Abs(Mathf.DeltaAngle(
                            contentHeadingDeg, mapHeadingDeg)) >= ContentPlanningHeadingStepDeg;

                    contentTerrainGeneration = visible.TerrainGeneration;
                    contentStyleKey = styleKey;
                    contentCenterLatitudeDeg = centerLatitudeDeg;
                    contentCenterLongitudeDeg = centerLongitudeDeg;
                    contentRangeMeters = rangeMeters;
                    if (adoptContentPlanningHeading) contentHeadingDeg = mapHeadingDeg;
                    contentTrackUp = trackUp;
                    contentAnchorV = anchorV;
                    contentOrientation = orientation;
                }
'''
renderer, _ = replace_once(renderer, anchor_old, anchor_new,
                           'R013 planner anchor preservation')

telemetry_old = (
    '                "; oh_rev35_r010_queue_backlog_peak=" + operationHealthRev35R010QueueBacklogPeak +\n')
telemetry_new = telemetry_old + (
    '                "; oh_rev35_r013_variant=" + Rev35R013Variant +\n'
    '                "; oh_rev35_r013_snapshot_reuse=" + operationHealthRev35R013SnapshotReuses +\n'
    '                "; oh_rev35_r013_full_capture=" + operationHealthRev35R013FullCaptures +\n'
    '                "; oh_rev35_r013_completion_reconcile=" + operationHealthRev35R013CompletionReconciles +\n'
    '                "; oh_rev35_r013_retry_reconcile=" + operationHealthRev35R013RetryReconciles +\n')
renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                           'R013 telemetry publication')

r012_var = 'REV3_5_R012_VARIANT="' + R012 + '"\n'
r013_var = r012_var + 'REV3_5_R013_VARIANT="' + R013 + '"\n'
build, _ = replace_once(build, r012_var, r013_var,
                          'R013 build identity variable')

r012_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py"\n'
r013_verify = r012_verify + 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r013_stable_content_snapshot_reconcile.py"\n'
build, _ = replace_once(build, r012_verify, r013_verify,
                          'R013 build verifier')

r012_identity = 'printf \'rev3_5_r012_variant=%s\\n\' "$REV3_5_R012_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
r013_identity = r012_identity + 'printf \'rev3_5_r013_variant=%s\\n\' "$REV3_5_R013_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
build, _ = replace_once(build, r012_identity, r013_identity,
                          'R013 candidate identity')

r012_suite = " ('OH REV3.5 R012 Cold Start Preload Ready Recovery','selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py'),\n"
r013_suite = r012_suite + " ('OH REV3.5 R013 Stable Content Snapshot Reconcile','selftest_v01800_oh_rev35_r013_stable_content_snapshot_reconcile.py'),\n"
prebuild, _ = replace_once(prebuild, r012_suite, r013_suite,
                           'R013 prebuild suite')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    if forbidden in renderer:
        fail('rejected mechanism present after R013: ' + forbidden)

R.write_text(renderer)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent_r010=' + R010)
print('observer_r011=' + R011)
print('bugfix_parent_r012=' + R012)
print('r013=' + R013)
print('capture_authority=true geometry/view refresh only')
print('completion_retry=reuse stable contentVisible + sorted tile snapshot; existing resolve/foundation/schedule retained')
print('planner_anchor=preserved across completion-only/retry reconcile')
print('rev009_heading_planner=6deg cumulative retained')
print('worker_change=0 scheduler_change=0 rasterizer_change=0 commit_lane_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 publication_authority_change=0')
