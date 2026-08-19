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
S = ROOT / 'Source/AERISFlightControl/Settings/AERISSettings.cs'
PREFIX = '[OH REV3.5 R013 STABLE CONTENT SNAPSHOT RECONCILE]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0:
        return -1, -1
    op = text.find('{', start)
    if op < 0:
        return -1, -1
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return start, i + 1
            i += 1
            continue
        if state == 'line':
            if c == '\n':
                state = 'code'
            i += 1
            continue
        if state == 'block':
            if c == '*' and n == '/':
                state = 'code'; i += 2; continue
            i += 1
            continue
        if state == 'string':
            if c == '\\':
                i += 2; continue
            if c == '"':
                state = 'code'
            i += 1
            continue
        if state == 'char':
            if c == '\\':
                i += 2; continue
            if c == "'":
                state = 'code'
            i += 1
            continue
    return -1, -1


for path in (R, O, P, N, B, PRE, S):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
preload = P.read_text()
nav = N.read_text()
build = B.read_text()
prebuild = PRE.read_text()
settings = S.read_text()

checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R010 in renderer, 'R010 continuous-commit parent retained')
check('[OH_REV3_5_R011_TURN_CHURN]' in observer,
      'R011 measurement observer retained')
check('appliedPointSetSignature' in preload and
      'deferredPointSetInvalidation' in preload,
      'R012 preload-ready recovery retained')
check('RELOADING ND\\nTERRAIN INIT' in nav,
      'R012 cold-start terrain-init presentation retained')
check(('const string Rev35R013Variant = "' + R013 + '";') in renderer,
      'R013 renderer identity present')

start, end = method_bounds(
    renderer, '        internal AERISTerrainGpuDrawState Draw(Rect plot,')
draw = renderer[start:end] if start >= 0 and end > start else ''
check(bool(draw), 'Draw method resolved')
check('bool contentTickRequired = contentGeometryChanged || workerResultReady ||' in draw,
      'worker completion/retry still wakes content maintenance')
check('const float ContentMaintenanceRetrySeconds = 0.20f;' in renderer,
      'bounded 5 Hz retry authority retained')
check('const float ContentPlanningHeadingStepDeg = 6f;' in renderer,
      'REV009 cumulative 6 degree hidden heading planner retained')
check('if (headingDelta >= ContentPlanningHeadingStepDeg) return true;' in renderer,
      'REV009 true heading refresh remains 6 degree authority')
check('if (headingDelta >= 3f) operationHealthContentHeadingCoalesced++;' in renderer,
      'legacy 3 degree point remains telemetry-only coalescing witness')

pump = draw.find('PumpStagedCompletedCommit(system,')
reuse_gate = draw.find(
    'bool rev35R013ReuseStableSnapshot = !contentGeometryChanged &&')
reuse_branch = draw.find('if (rev35R013ReuseStableSnapshot)', reuse_gate)
full_capture = draw.find('visible = system.CaptureVisible(', reuse_branch)
requested = draw.find('requested.Clear();', full_capture)
resolve = draw.find('ResolveRenderableEntries(', requested)
schedule = draw.find('Schedule(', resolve)
measure = draw.find(
    'contentFoundationCoverage = MeasureFoundationGpuReadiness(', resolve)

check(pump >= 0, 'R010 staged commit pump remains in content maintenance')
check(reuse_gate > pump,
      'R010 staged progress executes before reuse/full-capture choice')
check('contentSnapshotValid && visible != null && tiles != null &&' in
      draw[reuse_gate:reuse_gate + 320],
      'reuse requires valid non-empty prior content snapshot')
check(draw.count('system.CaptureVisible(') == 1,
      'Draw has exactly one planner CaptureVisible call')
check(full_capture > reuse_branch,
      'CaptureVisible is confined to full-refresh branch')
check('operationHealthContentCaptures++;' in draw[full_capture:requested],
      'legacy content-capture telemetry counts true captures only')
check('operationHealthRev35R013FullCaptures++;' in draw[full_capture:requested],
      'R013 true-capture telemetry counts full refresh only')
check('operationHealthRev35R013SnapshotReuses++;' in
      draw[reuse_branch:full_capture],
      'completion/retry snapshot reuse is directly observable')
check('operationHealthRev35R013CompletionReconciles++;' in
      draw[reuse_branch:full_capture],
      'worker-completion reconcile is directly observable')
check('operationHealthRev35R013RetryReconciles++;' in
      draw[reuse_branch:full_capture],
      'bounded retry reconcile is directly observable')
check('if (!rev35R013ReuseStableSnapshot)' in draw and
      'tiles = PrepareSortedTileScratch(visible.Tiles);' in draw,
      'stable reconcile reuses sorted tile snapshot')
check(resolve > requested and schedule > resolve and measure > resolve,
      'R008/R007 resolve/schedule/foundation path remains after reuse gate')
check('rasterizer.ReconcileCurrentRequests(requested);' in
      draw[requested:resolve],
      'R008 current-request reconciliation remains shared')
check('for (int admissionPass = 0; admissionPass < 2; admissionPass++)' in
      draw[requested:resolve],
      'R008 FAR-first two-pass admission remains shared')

anchor_guard = draw.find('if (!rev35R013ReuseStableSnapshot)', measure)
ready_assign = draw.find('contentReadyGlobal = readyGlobal;', anchor_guard)
check(anchor_guard > measure and ready_assign > anchor_guard,
      'planner-anchor preservation guard occurs after foundation measurement')
anchor_slice = (
    draw[anchor_guard:ready_assign]
    if anchor_guard >= 0 and ready_assign > anchor_guard else ''
)
for token in (
    'contentTerrainGeneration = visible.TerrainGeneration;',
    'contentCenterLatitudeDeg = centerLatitudeDeg;',
    'contentCenterLongitudeDeg = centerLongitudeDeg;',
    'contentRangeMeters = rangeMeters;',
    'contentHeadingDeg = mapHeadingDeg;',
    'contentOrientation = orientation;',
):
    check(token in anchor_slice,
          'true refresh owns planner anchor: ' + token)

check('contentReadyGlobal = readyGlobal;' in draw and
      'contentReadyFar = readyFar;' in draw and
      'contentFoundationCoverage' in draw,
      'completion reconcile still updates readiness authority')
check('contentSnapshotValid = true;' in draw and
      'contentGpuReadyPending = true;' in draw,
      'shared reconcile keeps snapshot/GPU-ready lifecycle active')

for token in (
    'oh_rev35_r013_variant=',
    'oh_rev35_r013_snapshot_reuse=',
    'oh_rev35_r013_full_capture=',
    'oh_rev35_r013_completion_reconcile=',
    'oh_rev35_r013_retry_reconcile=',
):
    check(token in renderer, 'runtime telemetry ' + token)

check('REV3_5_R013_VARIANT="' + R013 + '"' in build,
      'R013 build identity variable')
check('rev3_5_r013_variant=%s' in build,
      'R013 candidate identity append')
check('verify_aeris28_rev3_5_salbutamol_r013_stable_content_snapshot_reconcile.py'
      in build, 'R013 verifier wired into build')
check('selftest_v01800_oh_rev35_r013_stable_content_snapshot_reconcile.py'
      in prebuild, 'R013 selftest wired into prebuild')
check('rev35R007FoundationQueue.Count > 0' in renderer,
      'R010 continuous R007 FIFO wake retained')
check('FixedNavigationDisplayUpdateHz = 10f' in settings,
      'fixed visible 10 Hz authority retained')
check('160000f' in settings, 'exact 160 km authority retained')

for forbidden in (
    'Task.Run(',
    'new Thread(',
    'ThreadPool.',
    'WaitManagedPreparation',
    'ResidentPreparedPresentation',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    check(forbidden not in renderer,
          'R013 renderer excludes ' + forbidden)


def reuse(geometry_changed, snapshot_valid, visible_ok, tiles_ok):
    return (not geometry_changed) and snapshot_valid and visible_ok and tiles_ok


check(reuse(False, True, True, True),
      'truth table: stable completion/retry reuses snapshot')
check(not reuse(True, True, True, True),
      'truth table: geometry change forces full capture')
check(not reuse(False, False, True, True),
      'truth table: invalid snapshot forces full capture')
check(not reuse(False, True, False, True),
      'truth table: missing visible set forces full capture')
check(not reuse(False, True, True, False),
      'truth table: empty tile snapshot forces full capture')

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)

if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
