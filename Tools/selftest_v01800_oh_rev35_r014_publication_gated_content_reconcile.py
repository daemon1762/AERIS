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
STEP2 = ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'
PREFIX = '[OH REV3.5 R014 PUBLICATION GATED CONTENT RECONCILE]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'


def block_bounds(text, op):
    depth = 0; state = 'code'; i = op
    while i < len(text):
        c = text[i]; n = text[i + 1] if i + 1 < len(text) else ''
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
    return -1, -1


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0: return -1, -1
    op = text.find('{', start)
    if op < 0: return -1, -1
    _, end = block_bounds(text, op)
    return (start, end) if end > op else (-1, -1)


for path in (R, O, P, N, B, PRE, S, STEP2):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text(); observer = O.read_text(); preload = P.read_text()
nav = N.read_text(); build = B.read_text(); prebuild = PRE.read_text(); settings = S.read_text()
step2 = STEP2.read_text()
checks = []

def check(value, label):
    checks.append((bool(value), label))

check(R010 in renderer, 'R010 continuous-commit parent retained')
check('[OH_REV3_5_R011_TURN_CHURN]' in observer, 'R011 observer retained')
check('appliedPointSetSignature' in preload and 'deferredPointSetInvalidation' in preload,
      'R012 preload-ready recovery retained')
check('RELOADING ND\\nTERRAIN INIT' in nav,
      'R012 cold-start terrain-init presentation retained')
check(R013 not in renderer, 'rejected R013 renderer experiment absent')
check('REV3_5_R013_VARIANT=' not in build and 'rev3_5_r013_variant=' not in build,
      'rejected R013 build identity absent')
check('r013_stable_content_snapshot_reconcile' not in prebuild,
      'rejected R013 selftest wiring absent')
check(('const string Rev35R014Variant = "' + R014 + '";') in renderer,
      'R014 renderer identity present')

f0, f1 = method_bounds(renderer, '        bool FinalizePendingEntryCommit(')
finalize = renderer[f0:f1] if f0 >= 0 and f1 > f0 else ''
check(bool(finalize), 'FinalizePendingEntryCommit resolved')
check(finalize.count('AddEntry(entry);') == 1,
      'successful Finalize has singular Entry publication')
check(finalize.count('MarkGpuContentDirty();') == 1,
      'successful Finalize has singular dirty publication')
check(finalize.count('rev35R014PublicationSerial++;') == 1,
      'successful Finalize advances R014 publication serial exactly once')
check(finalize.count('operationHealthRev35R014PublicationEvents++;') == 1,
      'successful Finalize records publication exactly once')
add_pos = finalize.find('AddEntry(entry);')
dirty_pos = finalize.find('MarkGpuContentDirty();')
serial_pos = finalize.find('rev35R014PublicationSerial++;')
check(add_pos >= 0 and dirty_pos > add_pos and serial_pos > dirty_pos,
      'publication serial follows actual AddEntry + dirty authority')

check('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in renderer,
      'Phase6_003 authoritative publication marker retained')
check(renderer.count('PendingEntryCommit pendingEntryCommit;') == 1,
      'single PendingEntryCommit lane retained')
check('rev35R007FoundationQueue.Count > 0' in renderer,
      'R010 continuous R007 FIFO wake retained')

start, end = method_bounds(renderer, '        internal AERISTerrainGpuDrawState Draw(Rect plot,')
draw = renderer[start:end] if start >= 0 and end > start else ''
check(bool(draw), 'Draw method resolved')
check('const float ContentMaintenanceRetrySeconds = 0.20f;' in renderer,
      'inherited 0.20 second / 5 Hz content cadence retained')
check('bool rev35R014PublicationPendingBeforeTick =' in draw and
      'rev35R014PublicationSerial != rev35R014ReconciledPublicationSerial;' in draw,
      'deferred publication remains an explicit content wake')
check('bool contentTickRequired = contentGeometryChanged || workerResultReady ||' in draw and
      'contentRetryDue || rev35R014PublicationPendingBeforeTick;' in draw,
      'worker/retry/geometry/publication wake authority retained')
check('PumpStagedCompletedCommit(system,' in draw,
      'R010 staged pump retained in content path')
check('bool rev35R014PublicationPending =' in draw,
      'post-pump publication state is re-evaluated')
check('bool rev35R014ContentCadenceDue =' in draw and
      'presentationNow >= nextContentMaintenanceRealtime;' in draw,
      'full reconcile is tied to inherited content-maintenance deadline')
check('bool rev35R014ReconcileRequired = contentGeometryChanged ||' in draw and
      '(rev35R014PublicationPending || contentRetryDue)' in draw,
      'full reconcile is geometry-immediate or cadence-batched publication/retry')
check('operationHealthRev35R014WorkerOnlySkips++;' in draw,
      'worker-only content tick skip is observable')
check('operationHealthRev35R014PublicationDeferrals++;' in draw,
      'publication batching deferral is observable')
check('operationHealthRev35R014FullReconciles++;' in draw,
      'full reconcile is observable')
check('operationHealthRev35R014PublicationReconciles++;' in draw,
      'batched publication reconcile is observable')
check('operationHealthRev35R014RetryReconciles++;' in draw,
      'safety retry reconcile is observable')

pump = draw.find('PumpStagedCompletedCommit(system,')
gate = draw.find('bool rev35R014PublicationPending =', pump)
reconcile_if = draw.find('if (!rev35R014ReconcileRequired)', gate)
capture = draw.find('visible = system.CaptureVisible(', reconcile_if)
requested = draw.find('requested.Clear();', capture)
r008 = draw.find('rasterizer.ReconcileCurrentRequests(requested);', requested)
legacy_admission = draw.find('for (int admissionPass = 0; admissionPass < 2; admissionPass++)', r008)
accepted_r023_admission = draw.find('for (int admissionPass = 0; admissionPass < 4; admissionPass++)', r008)
admission = legacy_admission if legacy_admission >= 0 else accepted_r023_admission
resolve = draw.find('ResolveRenderableEntries(', admission)
measure = draw.find('contentFoundationCoverage = MeasureFoundationGpuReadiness(', resolve)
reconciled = draw.find('rev35R014ReconciledPublicationSerial =', measure)
check(pump >= 0 and gate > pump,
      'staged progress pump executes before publication/full-reconcile decision')
check(capture > reconcile_if,
      'CaptureVisible is behind R014 batching gate')
check(requested > capture and r008 > requested,
      'requested rebuild and R008 reconcile are behind R014 batching gate')
accepted_r023_shape = (
    accepted_r023_admission > r008 and
    'bool r021VisibleFar = r021Far &&' in draw[accepted_r023_admission:resolve] and
    'rev35R019VisibleFarKeys.Contains(tile.Key);' in draw[accepted_r023_admission:resolve] and
    'bool r022PrewarmFar = r021Far && !r021VisibleFar &&' in draw[accepted_r023_admission:resolve] and
    'rev35R022SuccessorPrewarmFarKeys.Contains(tile.Key);' in draw[accepted_r023_admission:resolve]
)
check(admission > r008 and resolve > admission and measure > resolve and
      (legacy_admission >= 0 or accepted_r023_shape),
      'R008 FAR-first resolve/foundation chain remains inside full reconcile through accepted R023 descendant')
check(reconciled > measure,
      'newest publication serial is acknowledged only after full reconcile')
check(draw.count('system.CaptureVisible(') == 1,
      'single CaptureVisible authority retained')

phase6_packet_refresh = '''                RefreshPresentationPackets(tiles, drawEntriesScratch);
                // Phase6_003: publication may detach the previously published Entry, but
                // Mesh recycling is delayed until the authoritative packet refresh proves
                // that the old Entry is no longer referenced by the persistent snapshot.
                ReleaseDeferredEntryRetirements(false);'''
check(phase6_packet_refresh in renderer,
      'Phase6_003 packet refresh -> deferred retirement textual contract retained')

ensure = draw.find('EnsureResources(plot, effectiveMode, currentPreset, virtualDetail);')
prune_gate = draw.find('if (rev35R014ReconcileRan)', ensure)
prune_open = draw.find('{', prune_gate) if prune_gate >= 0 else -1
_, prune_end = block_bounds(draw, prune_open) if prune_open >= 0 else (-1, -1)
prune_block = draw[prune_gate:prune_end] if prune_gate >= 0 and prune_end > prune_gate else ''
check(ensure >= 0 and prune_gate > ensure and bool(prune_block),
      'R014 gates the inherited post-resource prune block only')
for token, label in (
    ('long vramLimitBytes = ResolveVramLimitBytes();', 'Warm Visibility VRAM limit staging retained'),
    ('warmVisibilityPrunePending', 'Warm Visibility reload prune deferral retained'),
    ('PruneWarmResume(vramLimitBytes, 4)', 'bounded warm-resume prune retained'),
    ('Prune(vramLimitBytes);', 'normal bounded prune retained'),
    ('PruneRenderReady(ResolveRenderReadyLimitBytes());', 'RenderReady prune retained')):
    check(token in prune_block, label)
check('if (contentTickRequired)' not in prune_block,
      'worker-only content tick no longer admits warm/normal prune block')

check("r014_prune_successor = ('AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE' in R and" in step2 and
      "(r014_prune_successor and 'if (rev35R014ReconcileRan)' in post)" in step2 and
      "'pruning remains content/full-reconcile-only work'" in step2,
      'inherited Step2 selftest admits only exact R014 full-reconcile prune successor')

check('const float ContentPlanningHeadingStepDeg = 6f;' in renderer,
      'REV009 cumulative 6 degree hidden heading planner retained')
check('if (headingDelta >= ContentPlanningHeadingStepDeg) return true;' in renderer,
      'REV009 6 degree refresh authority retained')
check('FixedNavigationDisplayUpdateHz = 10f' in settings,
      'fixed visible 10 Hz authority retained')
check('160000f' in settings, 'exact 160 km authority retained')

for token in ('oh_rev35_r014_variant=', 'oh_rev35_r014_pub_serial=',
              'oh_rev35_r014_reconciled_serial=', 'oh_rev35_r014_publications=',
              'oh_rev35_r014_full_reconcile=', 'oh_rev35_r014_worker_only_skip=',
              'oh_rev35_r014_publication_defer=', 'oh_rev35_r014_publication_reconcile=',
              'oh_rev35_r014_retry_reconcile='):
    check(token in renderer, 'runtime telemetry ' + token)
check('REV3_5_R014_VARIANT="' + R014 + '"' in build,
      'R014 build identity variable')
check('rev3_5_r014_variant=%s' in build,
      'R014 candidate identity append')
check('verify_aeris28_rev3_5_salbutamol_r014_publication_gated_content_reconcile.py' in build,
      'R014 verifier wired into build')
check('selftest_v01800_oh_rev35_r014_publication_gated_content_reconcile.py' in prebuild,
      'R014 selftest wired into prebuild')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in renderer, 'R014 renderer excludes ' + forbidden)

def full_reconcile(geometry, publication, retry, cadence_due):
    return geometry or (cadence_due and (publication or retry))

check(full_reconcile(True, False, False, False),
      'truth table: geometry forces immediate reconcile')
check(not full_reconcile(False, True, False, False),
      'truth table: publication before cadence is batched/deferred')
check(full_reconcile(False, True, False, True),
      'truth table: batched publication reconciles when cadence is due')
check(not full_reconcile(False, False, False, True),
      'truth table: worker-only wake never causes full reconcile')
check(full_reconcile(False, False, True, True),
      'truth table: inherited retry reconciles when due')

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
