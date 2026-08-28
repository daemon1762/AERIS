#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
Z = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
S = ROOT / 'Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R009 GHOST PENDING BACKPRESSURE VERIFY]'
R008 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
R009 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE'
checks = []


def check(value, label):
    ok = bool(value); checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method_body(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
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
                if depth == 0: return text[start:i + 1]
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
    return ''


for path, label in ((R,'renderer'),(Z,'rasterizer'),(S,'scheduler'),(B,'build')):
    check(path.is_file(), label + ' exists')
if not all(p.is_file() for p in (R,Z,S,B)):
    raise SystemExit(1)
r = R.read_text(); z = Z.read_text(); s = S.read_text(); b = B.read_text()
r_flat = ' '.join(r.split())
check(R008 in r and R008 in z, 'R008 parent retained')
check(R009 in r and R009 in z and R009 in s, 'R009 marker spans renderer/rasterizer/scheduler')

submit = method_body(s, '        bool SubmitInternal(AERISRuntimeLane lane, string key,')
check(bool(submit), 'scheduler SubmitInternal resolved')
check('if (commitRequired)' in submit and 'queue.Jobs.Count >= queue.Capacity' in submit and
      'Interlocked.Increment(ref requiredRejected);' in submit,
      'existing SubmitRequired bounded backpressure retained')
check('previous.Value != null && previous.Value.CommitRequired' in submit and
      'return false;' in submit,
      'best-effort cannot replace commit-required queued job')
check('LinkedListNode<Job> evictable = queue.Jobs.First;' in submit and
      'while (evictable != null && evictable.Value != null &&\n                                evictable.Value.CommitRequired)' in submit and
      'queue.Jobs.Remove(evictable);' in submit,
      'best-effort overflow skips required jobs and evicts only best-effort')
check('LinkedListNode<Job> oldest = queue.Jobs.First;' not in submit,
      'old blind oldest-job eviction removed')
check('lanes[1] = new LaneQueue { Capacity = Math.Max(32,' in s and
      'permits.LogicalProcessors * 4)' in s,
      'GeneralCompute lane capacity unchanged')
check('readonly int[] fairness = { 0, 0, 0, 0, 1, 1, 1, 2, 2, 3 };' in s,
      'scheduler fairness unchanged')
check('workers = new Thread[workerTotal];' in s,
      'scheduler worker-count construction unchanged')

enqueue = method_body(z, '        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)')
check(bool(enqueue), 'rasterizer Enqueue resolved')
check('runtime.Scheduler.SubmitRequired(AERISRuntimeLane.GeneralCompute' in enqueue,
      'terrain raster uses required admission backpressure')
check('runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute' not in enqueue,
      'terrain raster no longer uses evicting SubmitLatest')
check('ElapsedSeconds(existing.EnqueuedTicks) < 10.0' in enqueue,
      '10-second duplicate TTL retained only as safety guard')
check('rev35R009DuplicatePending++' in enqueue,
      'duplicate pending safety path observable')
reject = enqueue.find('if (!accepted)')
register = enqueue.find('pending[tileId] = new PendingState', reject + 1)
submit_pos = enqueue.find('runtime.Scheduler.SubmitRequired')
check(0 <= submit_pos < reject < register,
      'pending ownership is registered only after scheduler acceptance')
pre_submit_register = enqueue.find('pending[tileId] = new PendingState', 0, submit_pos)
check(pre_submit_register < 0,
      'no Rasterizer pending registration exists before scheduler admission')
reject_block_end = register
reject_block = enqueue[reject:reject_block_end] if reject >= 0 and register > reject else ''
check('rev35R009AdmissionRejected++' in reject_block and 'return false;' in reject_block,
      'queue-full admission rejects immediately for next-tick retry')
check('dropped++' not in reject_block,
      'backpressure rejection is not misclassified as scheduler/result drop')
check('rev35R009AdmissionAccepted++' in enqueue and
      'rev35R009PendingRegistered++' in enqueue,
      'accepted-only ownership telemetry present')
check('rev35R009TerminalNull++' in enqueue,
      'cancel/stale terminal callback observable')

check('internal void ReconcileCurrentRequests(HashSet<string> currentRequestIdentities)' in z and
      'runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute' in z,
      'R008 current-request reconcile/cancel retained')
for token in (
    'oh_rev35_r009_variant=', 'oh_rev35_r009_admit_accept=',
    'oh_rev35_r009_admit_reject=', 'oh_rev35_r009_pending_registered=',
    'oh_rev35_r009_duplicate_pending=', 'oh_rev35_r009_terminal_null='):
    check(token in r, 'runtime telemetry publishes ' + token)

check('REV3_5_R009_VARIANT="' + R009 + '"' in b,
      'build records R009 identity')
check('verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py' in b,
      'build invokes R009 verifier')
check('rev3_5_r009_variant=%s' in b,
      'candidate identity records R009')

check('Rev35R004BudgetMaximumMilliseconds = 2.00' in r,
      'R004 2.00 ms commit ceiling retained')
check('Rev35R005SourceChunkHardCap = 64' in r,
      'R005 source64 retained')
check('presentationNow + 0.10f' in r,
      'fixed visible 10 Hz retained')
legacy_r009_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount' in r
)
accepted_r018_foundation_gate = (
    'bool r018VisibleGpuComplete = operationHealthRev35R018VisiblePlanValid && operationHealthRev35R018VisibleRequiredFar > 0 && operationHealthRev35R018VisibleReadyFar >= operationHealthRev35R018VisibleRequiredFar;' in r_flat and
    'bool r018OverscanGpuComplete = visible.FoundationComplete && lastBackFoundationCoverage >= 0.999f && readyFar >= visible.FarFoundationCount;' in r_flat and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in r_flat and
    'if (!r018OverscanGpuComplete) operationHealthRev35R018OverscanHolAvoided++;' in r_flat and
    'foundationComplete = rendered && r018VisibleGpuComplete && r018OverscanGpuComplete' not in r_flat
)
check(legacy_r009_foundation_gate or accepted_r018_foundation_gate,
      'foundation publication gate is legacy R009 strict coverage or exact accepted R018 visible-GPU descendant')
check('RenderTextureFormat.ARGB32' in r and 'FilterMode.Bilinear' in r,
      'ARGB32/Bilinear Golden target retained')
check('for (int admissionPass = 0; admissionPass < 2; admissionPass++)' in r and
      'rasterizer.ReconcileCurrentRequests(requested);' in r,
      'R008 requested-first FAR-first admission retained')
check('TryBeginRev35R007QueuedFoundationCommit()' in r,
      'R007 chained admission retained')

for forbidden in ('Task.Run(', 'WaitManagedPreparation', 'ResidentPreparedPresentation',
                  'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
                  'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in r and forbidden not in z,
          'generated terrain path excludes rejected mechanism: ' + forbidden)

failed = [label for ok,label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks)-len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
