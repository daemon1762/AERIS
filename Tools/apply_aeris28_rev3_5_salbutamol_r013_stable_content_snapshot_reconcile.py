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


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0:
        fail('method missing: ' + signature)
    op = text.find('{', start)
    if op < 0:
        fail('method open missing: ' + signature)
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
    fail('method close missing: ' + signature)


def indent_block(text, spaces=4):
    prefix = ' ' * spaces
    return ''.join(prefix + line if line.strip() else line for line in text.splitlines(True))


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

already = R013 in renderer
if not already:
    identity_old = '        const string Rev35R010Variant = "' + R010 + '";\n'
    identity_new = identity_old + (
        '        // ' + R013 + ': completion/retry maintenance may reconcile the\n'
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

    d0, d1 = method_bounds(renderer,
        '        internal AERISTerrainGpuDrawState Draw(Rect plot,')
    draw = renderer[d0:d1]

    if 'PumpStagedCompletedCommit(system,' not in draw:
        fail('R010 staged commit pump missing before R013 overlay')
    if draw.count('system.CaptureVisible(') != 1:
        fail('expected exactly one CaptureVisible in Draw before R013 overlay')

    capture_start = draw.find('                visible = system.CaptureVisible(')
    requested_start = draw.find('                requested.Clear();', capture_start)
    if capture_start < 0 or requested_start <= capture_start:
        fail('R013 CaptureVisible/requested boundary not found')

    capture_block = draw[capture_start:requested_start]
    if 'operationHealthContentCaptures++;' not in capture_block:
        fail('R013 legacy content capture telemetry missing')
    if 'visible == null || visible.Tiles == null ||' not in capture_block:
        fail('R013 CaptureVisible null/empty safety block missing')

    capture_block = capture_block.replace(
        '                operationHealthContentCaptures++;\n',
        '                operationHealthContentCaptures++;\n'
        '                operationHealthRev35R013FullCaptures++;\n', 1)

    reuse_block = '''                // R013: R010 staged completion/retry progress does not by itself
                // change geographic material authority. Reuse the last captured tile set;
                // only a true geometry/view change (or missing snapshot) recaptures it.
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
''' + indent_block(capture_block, 4) + '''                }

'''
    draw = draw[:capture_start] + reuse_block + draw[requested_start:]

    sort_old = '                tiles = PrepareSortedTileScratch(visible.Tiles);\n'
    sort_new = '''                if (!rev35R013ReuseStableSnapshot)
                    tiles = PrepareSortedTileScratch(visible.Tiles);
'''
    if draw.count(sort_old) != 1:
        fail('R013 sorted tile scratch anchor mismatch old=%d' % draw.count(sort_old))
    draw = draw.replace(sort_old, sort_new, 1)

    measure = draw.find(
        '                contentFoundationCoverage = MeasureFoundationGpuReadiness(')
    anchor_start = draw.find(
        '                bool adoptContentPlanningHeading =', measure)
    ready_start = draw.find(
        '                contentReadyGlobal = readyGlobal;', anchor_start)
    if measure < 0 or anchor_start < 0 or ready_start <= anchor_start:
        fail('R013 planner-anchor block boundary not found')

    anchor_block = draw[anchor_start:ready_start]
    for token in (
        'contentTerrainGeneration = visible.TerrainGeneration;',
        'contentCenterLatitudeDeg = centerLatitudeDeg;',
        'contentCenterLongitudeDeg = centerLongitudeDeg;',
        'contentRangeMeters = rangeMeters;',
        'contentHeadingDeg = mapHeadingDeg;',
        'contentOrientation = orientation;',
    ):
        if token not in anchor_block:
            fail('R013 planner anchor token missing: ' + token)

    guarded_anchor = '''                if (!rev35R013ReuseStableSnapshot)
                {
''' + indent_block(anchor_block, 4) + '''                }
'''
    draw = draw[:anchor_start] + guarded_anchor + draw[ready_start:]
    renderer = renderer[:d0] + draw + renderer[d1:]

    telemetry_old = (
        '                "; oh_rev35_r010_queue_backlog_peak=" + '
        'operationHealthRev35R010QueueBacklogPeak +\n')
    telemetry_new = telemetry_old + (
        '                "; oh_rev35_r013_variant=" + Rev35R013Variant +\n'
        '                "; oh_rev35_r013_snapshot_reuse=" + operationHealthRev35R013SnapshotReuses +\n'
        '                "; oh_rev35_r013_full_capture=" + operationHealthRev35R013FullCaptures +\n'
        '                "; oh_rev35_r013_completion_reconcile=" + operationHealthRev35R013CompletionReconciles +\n'
        '                "; oh_rev35_r013_retry_reconcile=" + operationHealthRev35R013RetryReconciles +\n')
    renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                               'R013 telemetry publication')
else:
    print(PREFIX + ' renderer overlay already present')

r012_var = 'REV3_5_R012_VARIANT="' + R012 + '"\n'
r013_var = r012_var + 'REV3_5_R013_VARIANT="' + R013 + '"\n'
build, _ = replace_once(build, r012_var, r013_var,
                        'R013 build identity variable')

r012_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py"\n')
r013_verify = r012_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r013_stable_content_snapshot_reconcile.py"\n')
build, _ = replace_once(build, r012_verify, r013_verify,
                        'R013 build verifier')

r012_identity = (
    'printf \'rev3_5_r012_variant=%s\\n\' "$REV3_5_R012_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r013_identity = r012_identity + (
    'printf \'rev3_5_r013_variant=%s\\n\' "$REV3_5_R013_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r012_identity, r013_identity,
                        'R013 candidate identity')

r012_suite = (
    " ('OH REV3.5 R012 Cold Start Preload Ready Recovery',"
    "'selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py'),\n")
r013_suite = r012_suite + (
    " ('OH REV3.5 R013 Stable Content Snapshot Reconcile',"
    "'selftest_v01800_oh_rev35_r013_stable_content_snapshot_reconcile.py'),\n")
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
print('completion_retry=R010 staged pump retained; stable contentVisible/sorted tiles reused')
print('resolve_foundation_schedule=existing R008/R007 shared path retained')
print('planner_anchor=preserved across completion-only/retry reconcile')
print('rev009_heading_planner=6deg cumulative retained')
print('worker_change=0 scheduler_change=0 rasterizer_change=0 commit_lane_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 publication_authority_change=0')
