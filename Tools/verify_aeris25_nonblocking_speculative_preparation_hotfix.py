#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
C = (ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
U = (ROOT / 'build_ubuntu.sh').read_text()
P5V = (ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py').read_text()
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


def method_body(signature):
    start = R.find(signature)
    if start < 0:
        return ''
    op = R.find('{', start)
    if op < 0:
        return ''
    depth = 0
    state = 'code'
    i = op
    while i < len(R):
        c = R[i]
        n = R[i + 1] if i + 1 < len(R) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'str'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return R[start:i + 1]
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
        if state in ('str', 'char'):
            if c == '\\':
                i += 2; continue
            if (state == 'str' and c == '"') or (state == 'char' and c == "'"):
                state = 'code'
            i += 1
    return ''


pump = method_body('        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
advance = method_body('        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,')
submit = method_body('        bool SubmitManagedPreparation(PendingEntryCommit pending)')
has_ready = method_body('        bool HasReadyManagedPreparationWaiter()')
activate = method_body('        bool TryActivateReadyManagedPreparationWaiter()')
detach = method_body('        void DetachManagedPreparationWaiter(PendingEntryCommit pending)')
cancel_waiters = method_body('        void CancelManagedPreparationWaiters()')
buildprep = method_body('        static ManagedPreparationPayload BuildManagedPreparation(')
finalize = method_body('        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
release = method_body('        void ReleaseDeferredEntryRetirements(bool force)')

ck('internal const string Codename = "NOREPINEPHRINE";' in M and
   'internal const string Revision = "OH_PHASE6_005";' in M and
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
   'codename = NOREPINEPHRINE' in C,
   'NOREPINEPHRINE OH_PHASE6_005 identity is authoritative')
ck('REV005 NON-BLOCKING SPECULATIVE PREPARATION' in U and
   'verify_aeris25_nonblocking_speculative_preparation_hotfix.py' in U,
   'build/in-game identity and final verifier are rev005')
ck('AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION' in R and
   'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in R and
   'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in R,
   'rev005 extends rev004 worker preparation and rev003 lifetime authority')

ck('const int ManagedPreparationMaximumInFlight = 4;' in R and
   'readonly List<PendingEntryCommit> managedPreparationWaiters' in R,
   'speculative managed-preparation concurrency is explicitly bounded to four Entries')
ck(has_ready and 'ManagedPreparationCompleted' in has_ready and
   'ManagedPreparationFailed' in has_ready,
   'detached waiters are polled only for terminal worker state')
ck(pump and 'TryActivateReadyManagedPreparationWaiter()' in pump and
   'managedPreparationWaiters.Count >=' in pump and
   'ManagedPreparationMaximumInFlight' in pump and
   'rasterizer.Drain(completed, 1)' in pump,
   'pump resumes ready waiters and uses waiter capacity as backpressure')
ck(pump and '(pendingEntryCommit == null ? 0 : 1) + managedPreparationWaiters.Count' in pump,
   'main-commit backlog/pending telemetry includes detached waiters')

submit_case = advance[advance.find('case PendingEntryCommitStage.SubmitManagedPreparation:'):
                      advance.find('case PendingEntryCommitStage.WaitManagedPreparation:')]
wait_case = advance[advance.find('case PendingEntryCommitStage.WaitManagedPreparation:'):
                    advance.find('case PendingEntryCommitStage.PrepareSources:')]
ck(submit_case and 'DetachManagedPreparationWaiter(pending);' in submit_case and
   'return true;' in submit_case and
   'return false;' not in submit_case[submit_case.find('pending.Stage = PendingEntryCommitStage.WaitManagedPreparation;'):],
   'accepted async worker submission detaches instead of blocking the single pending head')
ck(wait_case and 'ApplyManagedPreparation(pending);' in wait_case,
   'WaitManagedPreparation remains only as a ready-result re-entry stage')
ck(activate and 'ManagedPreparationCompleted' in activate and
   'ManagedPreparationFailed' in activate and
   'pendingEntryCommit = pending;' in activate and
   'operationHealthManagedPrepReadyResumed++' in activate,
   'only completed/failed detached work is reactivated for Unity upload')
ck(detach and 'landSurfaceScratch = new SurfaceBuilder();' in detach and
   'waterSurfaceScratch = new SurfaceBuilder();' in detach and
   'pendingEntryCommit = null;' in detach and
   'operationHealthManagedPrepHolBypass++' in detach,
   'worker-owned SurfaceBuilder data is detached before another Entry may clip')

ck(submit and 'runtime.Scheduler.SubmitRequired(' in submit and
   'AERISRuntimeLane.GeneralCompute' in submit and
   'terrain-gpu-managed-prep:' in submit and
   'if (!ReferenceEquals(pendingEntryCommit, pending)) return;' not in submit and
   'pending.ManagedPreparationCompleted = true;' in submit,
   'completion callback targets closure-owned Entry, not the current single pending head')
ck(submit and 'Task.Run' not in submit and 'new Thread' not in submit,
   'rev005 creates no ad-hoc thread/task path')
for forbidden in ('Mesh ', 'Graphics.', 'RenderTexture', 'GameObject', 'Transform',
                  'Rigidbody', 'KSPUtil', 'FlightGlobals', 'AcquireMesh(', 'SetUVs(',
                  'UploadMeshData(', 'DrawMeshNow'):
    ck(forbidden not in buildprep,
       'worker BuildManagedPreparation avoids Unity/KSP object API: ' + forbidden)

ck(cancel_waiters and 'runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute' in cancel_waiters and
   'managedPreparationWaiters.Clear();' in cancel_waiters,
   'view/reset lifecycle cancels detached scheduler work without waiting')
reset_start = R.find('        void ResetContentSnapshot()')
reset_end = R.find('        void RefreshPresentationPackets', reset_start)
reset = R[reset_start:reset_end] if reset_start >= 0 and reset_end > reset_start else ''
dispose = method_body('        public void Dispose()')
ck('CancelPendingEntryCommit();' in reset and 'CancelManagedPreparationWaiters();' in reset and
   'CancelPendingEntryCommit();' in dispose and 'CancelManagedPreparationWaiters();' in dispose,
   'snapshot reset/dispose invalidates current and detached work')

non_tick_start = R.find('            if (!authoritativeTickDue)')
non_tick_end = R.find('            operationHealthAuthoritativeTicks++;', non_tick_start)
non_tick = R[non_tick_start:non_tick_end] if non_tick_start >= 0 and non_tick_end > non_tick_start else ''
ck('HasReadyManagedPreparationWaiter()' in non_tick and
   'CaptureVisible(' not in non_tick and 'RenderBackBuffer(' not in non_tick,
   'hidden frames may drain ready prep but retain no-visible-publication contract')
ck(advance and 'if (!allowPublication)' in advance and
   'FinalizePendingEntryCommit(pending, system);' in advance,
   'Finalize remains authoritative-publication gated')
ck(finalize and 'DetachEntryForDeferredRetirement(old);' in finalize and
   release and 'presentationEntryPins.Contains(entry)' in release,
   'rev003 deferred retirement/snapshot Mesh lifetime architecture remains intact')

for field in ('oh_managed_prep_waiters=', 'oh_managed_prep_waiter_peak=',
              'oh_managed_prep_detached=', 'oh_managed_prep_ready_resume=',
              'oh_managed_prep_hol_bypass=', 'oh_managed_prep_submitted=',
              'oh_managed_prep_completed=', 'oh_managed_prep_worker_max_ms=',
              'oh_managed_prep_bytes_total=', 'oh_snapshot_stale_mesh=',
              'oh_deferred_retire_pending='):
    ck(field in R, 'runtime telemetry publishes ' + field[:-1])

ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'presentationEntryPins.Contains(entry)' in R,
   'ADENOSINE packets/O(1) snapshot pin authority remain intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R,
   'rev008 Snapshot Mesh Lifetime Guard remains intact')
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and
   'oh_heading_plan_coalesced=' in R,
   'ATROPINE rev009 burst governor remains intact')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'visible ND presentation authority remains fixed 10 Hz')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION' not in SH,
   'rev005 changes no shader equations or shader bytes')
ck('OH_PHASE6_005' in P5V and
   'verify_aeris25_nonblocking_speculative_preparation_hotfix.py' in P5V,
   'ADENOSINE inherited verifier explicitly admits exact rev005 descendant')

active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('verify_aeris25_nonblocking_speculative_preparation_hotfix.py' in active and
   'verify_aeris25_managed_preparation_pipeline_hotfix.py' not in active,
   'rev005 build uses exactly one final-tree Phase6 verifier')

frozen = ['Source/AERISFlightControl/AA', 'Source/AERISFlightControl/Autopilot',
          'Source/AERISFlightControl/Protect', 'Source/AERISFlightControl/Landing']
try:
    changed = subprocess.check_output(
        ['git', '-C', str(ROOT), 'diff', '--name-only', 'HEAD', '--'] + frozen,
        text=True).strip().splitlines()
except Exception:
    changed = ['GIT_DIFF_UNAVAILABLE']
ck(changed == [], 'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed = [name for ok, name in checks if not ok]
print('\n[AERIS25 NOREPINEPHRINE PHASE6_005 NON-BLOCKING SPECULATIVE PREPARATION] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    msg = '; '.join(failed)
    print('FAILED: ' + msg)
    print('::error title=NOREPINEPHRINE Phase6_005 verifier::' + msg)
    raise SystemExit(1)
print('[AERIS25 NOREPINEPHRINE PHASE6_005 NON-BLOCKING SPECULATIVE PREPARATION] STATIC PASS')
