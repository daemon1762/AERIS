#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
T=ROOT/'Source/AERISFlightControl/Terrain'
W=(T/'AERISTerrainGpuTileRenderer.WorkerProjection.cs').read_text()
R=(T/'AERISTerrainGpuTileRenderer.cs').read_text()
S=(ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text()
checks=[]
def ck(v,n): checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('internal const float FixedNavigationDisplayUpdateHz = 10f' in S,
   'ND authoritative source remains fixed 10 Hz')
ck('ProjectionWorkerMinimumCommitIntervalSeconds = 0.10f' in W,
   'FRONT minimum commit interval remains exactly 0.10 seconds')
ck('ProjectionWorkerTimeoutSeconds = 0.095f' in W,
   'worker timeout is bounded inside one 10 Hz frame budget')
submit=W[W.index('bool TrySubmitProjectionWorker('):W.index('// Pure worker section.')]
completed=submit[submit.index('if (projectionWorkerCompleted != null)'):submit.index('if (projectionWorkerPending)')]
ck('operationHealthProjectionWorkerWaitHolds++' in completed and 'return true;' in completed,
   'completed worker owns refresh instead of triggering main-thread fallback')
pending=submit[submit.index('if (projectionWorkerPending)'):submit.index('if (visible == null')]
ck('pendingAge < ProjectionWorkerTimeoutSeconds' in pending and
   'operationHealthProjectionWorkerWaitHolds++' in pending and 'return true;' in pending,
   'healthy pending worker is held without duplicate exact projection')
ck('frontCommitGateOpen' in pending and
   '!frontCommitGateOpen' in pending,
   'timeout fallback is forbidden before the 0.10 second FRONT gate opens')
ck('CancelKey(AERISRuntimeLane.GeneralCompute' in pending and
   'operationHealthProjectionWorkerTimeoutFallbacks++' in pending and
   pending.rfind('return false;') > pending.index('CancelKey('),
   'only timed-out worker cancellation falls through to main exact fallback')
ck('projectionWorkerPendingSerial = request.Serial;' in submit and
   'projectionWorkerSubmittedRealtime = Time.realtimeSinceStartup;' in submit,
   'worker request owns serial and submit timestamp')
complete=W[W.index('void CompleteProjectionWorker('):W.index('bool TryCommitProjectionWorkerResult(')]
ck('projectionWorkerPendingSerial == requestSerial' in complete and
   'requestSerial != projectionWorkerSerial' in complete,
   'late callback cannot clear or publish over a newer request')
ck('projectionWorkerTimeoutCancelledSerials.Remove(requestSerial)' in complete and
   'if (timeoutCancelled)' in complete and
   'operationHealthProjectionWorkerFailures++' in complete,
   'timeout cancellation is distinguished from a real worker failure')
commit=W[W.index('bool TryCommitProjectionWorkerResult('):W.index('bool ProjectionWorkerResultStillCurrent(')]
ck('projectionWorkerLastDeferredSerial != serial' in commit,
   'commit deferral telemetry counts once per result, not once per repaint')
ck('nextAuthoritativePresentationTickRealtime =' not in commit,
   'worker commit does not rephase the 10 Hz source and add worker latency each cycle')
ck('TrySubmitProjectionWorker(visible, projection' in R and
   'if (workerEligible) operationHealthProjectionWorkerFallbacks++;' in R,
   'existing main exact fallback remains available only when worker path returns false')
ck('oh_project_worker_wait_hold=' in W and 'oh_project_worker_timeout=' in W and
   'project_worker_pending_age_ms=' in W and 'project_worker_handoff_hf=1' in W,
   'runtime handoff telemetry is published')
ck('MaximumContourLevelsPerTile = 96' in (T/'AERISTerrainGpuTileRasterizer.cs').read_text(),
   'Candidate11 contour authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'render target quality remains unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Step 3 Worker Projection Handoff Hotfix 1] %d/%d PASS' %
      (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
