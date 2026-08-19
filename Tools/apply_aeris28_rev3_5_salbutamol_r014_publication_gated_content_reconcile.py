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
PREFIX = '[AERIS28 REV3.5 SALBUTAMOL SULFATE R014 PUBLICATION GATED CONTENT RECONCILE]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'
R012 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R012_COLD_START_PRELOAD_READY_RECOVERY'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'


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
    return block_bounds(text, op, signature)


def block_bounds(text, op, label):
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return op, i + 1
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    fail('block close missing: ' + label)


def indent_block(text, spaces=4):
    prefix = ' ' * spaces
    return ''.join(prefix + line if line.strip() else line for line in text.splitlines(True))


def statement_end(text, start):
    depth = 0
    state = 'code'
    i = start
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '(': depth += 1
            elif c == ')': depth = max(0, depth - 1)
            elif c == ';' and depth == 0: return i + 1
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    fail('statement terminator missing')


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
    fail('R010 generated parent required before R014 overlay')
if '[OH_REV3_5_R011_TURN_CHURN]' not in observer:
    fail('R011 observer required before R014 overlay')
if 'appliedPointSetSignature' not in preload or 'deferredPointSetInvalidation' not in preload:
    fail('R012 preload-ready recovery parent missing')
if 'RELOADING ND\\nTERRAIN INIT' not in nav:
    fail('R012 terrain-init presentation parent missing')
if R013 in renderer or R013 in build or 'r013_stable_content_snapshot_reconcile' in prebuild:
    fail('rejected R013 experiment must not be inherited by R014')

already = R014 in renderer
if not already:
    identity_old = '        const string Rev35R010Variant = "' + R010 + '";\n'
    identity_new = identity_old + (
        '        // ' + R014 + ': worker completion advances the existing single staged\n'
        '        // commit lane immediately; published Entries are coalesced into the\n'
        '        // inherited 0.20 s content-maintenance cadence before full reconcile.\n'
        '        const string Rev35R014Variant = "' + R014 + '";\n')
    renderer, _ = replace_once(renderer, identity_old, identity_new,
                               'R014 renderer identity')

    field_old = '''        long operationHealthRev35R010QueueBacklogBudgetSamples;
        int operationHealthRev35R010QueueBacklogPeak;
'''
    field_new = field_old + '''        long rev35R014PublicationSerial;
        long rev35R014ReconciledPublicationSerial;
        long operationHealthRev35R014PublicationEvents;
        long operationHealthRev35R014FullReconciles;
        long operationHealthRev35R014WorkerOnlySkips;
        long operationHealthRev35R014PublicationDeferrals;
        long operationHealthRev35R014PublicationReconciles;
        long operationHealthRev35R014RetryReconciles;
'''
    renderer, _ = replace_once(renderer, field_old, field_new,
                               'R014 publication/reconcile fields')

    # Phase6_003 already owns publication legality. Observe only a successful Finalize path;
    # do not create a second publication route. A dedicated serial is used instead of
    # gpuContentRevision because the latter intentionally coalesces dirty signals until FRONT.
    f0, f1 = method_bounds(renderer, '        bool FinalizePendingEntryCommit(')
    finalize = renderer[f0:f1]
    if finalize.count('AddEntry(entry);') != 1:
        fail('R014 Finalize AddEntry authority mismatch')
    if finalize.count('MarkGpuContentDirty();') != 1:
        fail('R014 Finalize dirty authority mismatch')
    publication_old = '            MarkGpuContentDirty();\n'
    publication_new = publication_old + (
        '            rev35R014PublicationSerial++;\n'
        '            operationHealthRev35R014PublicationEvents++;\n')
    finalize, _ = replace_once(finalize, publication_old, publication_new,
                               'R014 successful Entry publication serial')
    renderer = renderer[:f0] + finalize + renderer[f1:]

    d0, d1 = method_bounds(renderer,
        '        internal AERISTerrainGpuDrawState Draw(Rect plot,')
    draw = renderer[d0:d1]
    if 'bool contentTickRequired = contentGeometryChanged || workerResultReady ||' not in draw:
        fail('R014 Step2 content tick authority missing')
    if 'PumpStagedCompletedCommit(system,' not in draw:
        fail('R014 R010 staged pump missing')
    if 'rasterizer.ReconcileCurrentRequests(requested);' not in draw:
        fail('R014 R008 current-request reconcile missing')
    if 'for (int admissionPass = 0; admissionPass < 2; admissionPass++)' not in draw:
        fail('R014 R008 FAR-first admission missing')
    if draw.count('system.CaptureVisible(') != 1:
        fail('R014 expected exactly one CaptureVisible before overlay')

    # A publication that was deferred by the 0.20 s batching rail must itself keep the
    # authoritative content block awake even after rasterizer workerResultReady goes false.
    tick_old = '''            bool contentTickRequired = contentGeometryChanged || workerResultReady ||
                contentRetryDue;
'''
    tick_new = '''            bool rev35R014PublicationPendingBeforeTick =
                rev35R014PublicationSerial != rev35R014ReconciledPublicationSerial;
            bool contentTickRequired = contentGeometryChanged || workerResultReady ||
                contentRetryDue || rev35R014PublicationPendingBeforeTick;
            bool rev35R014ReconcileRan = false;
'''
    draw, _ = replace_once(draw, tick_old, tick_new,
                           'R014 publication wake + reconcile witness')

    content_if = draw.find('            if (contentTickRequired)\n            {')
    if content_if < 0:
        fail('R014 contentTickRequired block missing')
    content_open = draw.find('{', content_if)
    cb0, cb1 = block_bounds(draw, content_open, 'R014 content tick block')
    content_block = draw[cb0 + 1:cb1 - 1]

    pump_start = content_block.find('PumpStagedCompletedCommit(system,')
    if pump_start < 0:
        fail('R014 pump missing inside content block')
    pump_end = statement_end(content_block, pump_start)
    expensive = content_block[pump_end:]
    if 'visible = system.CaptureVisible(' not in expensive:
        fail('R014 CaptureVisible not downstream of staged pump')
    for token in ('requested.Clear();', 'rasterizer.ReconcileCurrentRequests(requested);',
                  'ResolveRenderableEntries(', 'MeasureFoundationGpuReadiness('):
        if token not in expensive:
            fail('R014 full reconcile token missing downstream: ' + token)

    gate = '''
                // R014 publication batching: worker readiness advances only the existing
                // R010 staged lane. Full geographic/request/resolve/foundation work is
                // immediate for a true geometry change, otherwise it is capped by the
                // inherited 0.20 s content-maintenance deadline. Multiple Entry publications
                // inside that window collapse into one reconcile without losing the newest
                // publication serial.
                bool rev35R014PublicationPending =
                    rev35R014PublicationSerial != rev35R014ReconciledPublicationSerial;
                bool rev35R014ContentCadenceDue =
                    presentationNow >= nextContentMaintenanceRealtime;
                bool rev35R014ReconcileRequired = contentGeometryChanged ||
                    (rev35R014ContentCadenceDue &&
                     (rev35R014PublicationPending || contentRetryDue));

                if (!rev35R014ReconcileRequired)
                {
                    operationHealthRev35R014WorkerOnlySkips++;
                    if (rev35R014PublicationPending)
                        operationHealthRev35R014PublicationDeferrals++;
                }
                else
                {
                    rev35R014ReconcileRan = true;
                    operationHealthRev35R014FullReconciles++;
                    if (rev35R014PublicationPending)
                        operationHealthRev35R014PublicationReconciles++;
                    if (contentRetryDue)
                        operationHealthRev35R014RetryReconciles++;
'''
    gated_expensive = gate + indent_block(expensive, 4) + (
        '                    rev35R014ReconciledPublicationSerial =\n'
        '                        rev35R014PublicationSerial;\n'
        '                }\n')
    content_block = content_block[:pump_end] + gated_expensive
    draw = draw[:cb0 + 1] + content_block + draw[cb1 - 1:]

    # Pruning belongs to a full content reconcile. Worker-only staged progress must not pay
    # the old full prune cost at every authoritative tick.
    prune_old = '''            EnsureResources(plot, effectiveMode, currentPreset, virtualDetail);
            if (contentTickRequired)
            {
                Prune(ResolveVramLimitBytes());
                PruneRenderReady(ResolveRenderReadyLimitBytes());
            }
'''
    prune_new = '''            EnsureResources(plot, effectiveMode, currentPreset, virtualDetail);
            if (rev35R014ReconcileRan)
            {
                Prune(ResolveVramLimitBytes());
                PruneRenderReady(ResolveRenderReadyLimitBytes());
            }
'''
    draw, _ = replace_once(draw, prune_old, prune_new,
                           'R014 prune batching gate')
    renderer = renderer[:d0] + draw + renderer[d1:]

    telemetry_old = (
        '                "; oh_rev35_r010_queue_backlog_peak=" + '
        'operationHealthRev35R010QueueBacklogPeak +\n')
    telemetry_new = telemetry_old + (
        '                "; oh_rev35_r014_variant=" + Rev35R014Variant +\n'
        '                "; oh_rev35_r014_pub_serial=" + rev35R014PublicationSerial +\n'
        '                "; oh_rev35_r014_reconciled_serial=" + rev35R014ReconciledPublicationSerial +\n'
        '                "; oh_rev35_r014_publications=" + operationHealthRev35R014PublicationEvents +\n'
        '                "; oh_rev35_r014_full_reconcile=" + operationHealthRev35R014FullReconciles +\n'
        '                "; oh_rev35_r014_worker_only_skip=" + operationHealthRev35R014WorkerOnlySkips +\n'
        '                "; oh_rev35_r014_publication_defer=" + operationHealthRev35R014PublicationDeferrals +\n'
        '                "; oh_rev35_r014_publication_reconcile=" + operationHealthRev35R014PublicationReconciles +\n'
        '                "; oh_rev35_r014_retry_reconcile=" + operationHealthRev35R014RetryReconciles +\n')
    renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                               'R014 telemetry publication')
else:
    print(PREFIX + ' renderer overlay already present')

# Build/test wiring is additive over R012 only. R013 is intentionally absent.
r012_var = 'REV3_5_R012_VARIANT="' + R012 + '"\n'
r014_var = r012_var + 'REV3_5_R014_VARIANT="' + R014 + '"\n'
build, _ = replace_once(build, r012_var, r014_var,
                        'R014 build identity variable')

r012_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py"\n')
r014_verify = r012_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r014_publication_gated_content_reconcile.py"\n')
build, _ = replace_once(build, r012_verify, r014_verify,
                        'R014 build verifier')

r012_identity = (
    'printf \'rev3_5_r012_variant=%s\\n\' "$REV3_5_R012_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r014_identity = r012_identity + (
    'printf \'rev3_5_r014_variant=%s\\n\' "$REV3_5_R014_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r012_identity, r014_identity,
                        'R014 candidate identity')

r012_suite = (
    " ('OH REV3.5 R012 Cold Start Preload Ready Recovery',"
    "'selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py'),\n")
r014_suite = r012_suite + (
    " ('OH REV3.5 R014 Publication Gated Content Reconcile',"
    "'selftest_v01800_oh_rev35_r014_publication_gated_content_reconcile.py'),\n")
prebuild, _ = replace_once(prebuild, r012_suite, r014_suite,
                           'R014 prebuild suite')

if 'REV3_5_R013_VARIANT=' in build or 'rev3_5_r013_variant=' in build or \
   'r013_stable_content_snapshot_reconcile' in prebuild:
    fail('R013 build/test wiring leaked into R014')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    if forbidden in renderer:
        fail('rejected mechanism present after R014: ' + forbidden)

R.write_text(renderer)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent_r010=' + R010)
print('observer_r011=' + R011)
print('bugfix_parent_r012=' + R012)
print('rejected_r013_inherited=0')
print('r014=' + R014)
print('worker_completion=R010 staged pump immediate; full reconcile not worker-triggered')
print('publication_authority=successful FinalizePendingEntryCommit serial')
print('publication_batching=inherited ContentMaintenanceRetrySeconds 0.20s maximum 5Hz')
print('geometry_change=immediate full reconcile')
print('deferred_publication=wakes content path until newest serial is reconciled')
print('r008_current_request_and_far_first=retained inside full reconcile')
print('rev009_heading_planner=6deg cumulative retained')
print('worker_change=0 scheduler_change=0 rasterizer_change=0 commit_lane_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 complete_coverage_change=0')
