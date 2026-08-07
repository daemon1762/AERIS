#!/usr/bin/env python3
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]
WPATH = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.WorkerProjection.cs'
RUNNER = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
TEST = ROOT / 'Tools/selftest_v01800_operation_health_step3_worker_handoff_hotfix1.py'


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s: expected exactly one match, got %d' % (label, count))
    return text.replace(old, new, 1)

w = WPATH.read_text()
w = replace_once(w,
    'using System;\nusing System.Diagnostics;\n',
    'using System;\nusing System.Collections.Generic;\nusing System.Diagnostics;\n',
    'worker using')
w = replace_once(w,
    '        const float ProjectionWorkerMinimumCommitIntervalSeconds = 0.10f;\n',
    '        const float ProjectionWorkerMinimumCommitIntervalSeconds = 0.10f;\n'
    '        const float ProjectionWorkerTimeoutSeconds = 0.095f;\n',
    'worker timeout constant')
w = replace_once(w,
    '        bool projectionWorkerPending;\n'
    '        ProjectionWorkerResult projectionWorkerCompleted;\n'
    '        long projectionWorkerSerial;\n',
    '        readonly HashSet<long> projectionWorkerTimeoutCancelledSerials =\n'
    '            new HashSet<long>();\n'
    '        bool projectionWorkerPending;\n'
    '        long projectionWorkerPendingSerial = -1L;\n'
    '        float projectionWorkerSubmittedRealtime = -1f;\n'
    '        long projectionWorkerLastDeferredSerial = -1L;\n'
    '        ProjectionWorkerResult projectionWorkerCompleted;\n'
    '        long projectionWorkerSerial;\n',
    'worker state fields')
w = replace_once(w,
    '        long operationHealthProjectionWorkerCommitDeferrals;\n'
    '        long operationHealthProjectionWorkerBufferBytes;\n',
    '        long operationHealthProjectionWorkerCommitDeferrals;\n'
    '        long operationHealthProjectionWorkerWaitHolds;\n'
    '        long operationHealthProjectionWorkerTimeoutFallbacks;\n'
    '        long operationHealthProjectionWorkerBufferBytes;\n',
    'worker handoff telemetry fields')
old_submit_prefix = '''        {\n            if (projectionWorkerPending || projectionWorkerCompleted != null ||\n                visible == null || vessel == null || vessel.mainBody == null ||\n                drawEntriesScratch == null || drawEntriesScratch.Length == 0)\n                return false;\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime == null || runtime.Scheduler == null) return false;\n'''
new_submit_prefix = '''        {\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n\n            // A healthy in-flight/completed worker owns this authoritative refresh.\n            // Returning true here means \"worker path accepted/holds the refresh\", not\n            // necessarily \"a new job was submitted\". This prevents the caller from\n            // performing the old exact main-thread projection at the same time.\n            if (projectionWorkerCompleted != null)\n            {\n                operationHealthProjectionWorkerWaitHolds++;\n                return true;\n            }\n            if (projectionWorkerPending)\n            {\n                float now = Time.realtimeSinceStartup;\n                float pendingAge = projectionWorkerSubmittedRealtime < 0f ? 0f :\n                    Math.Max(0f, now - projectionWorkerSubmittedRealtime);\n                bool frontCommitGateOpen = !frontBufferValid ||\n                    now - frontCommittedRealtime >=\n                        ProjectionWorkerMinimumCommitIntervalSeconds;\n\n                // Do not duplicate healthy worker work. A timeout may fall back only\n                // after the previous FRONT has satisfied the same 0.10 s authority, so\n                // Worker -> main-thread recovery can never create a >10 Hz FRONT burst.\n                if (pendingAge < ProjectionWorkerTimeoutSeconds ||\n                    !frontCommitGateOpen || runtime == null || runtime.Scheduler == null)\n                {\n                    operationHealthProjectionWorkerWaitHolds++;\n                    return true;\n                }\n\n                long cancelledSerial = projectionWorkerPendingSerial;\n                if (cancelledSerial >= 0L)\n                    projectionWorkerTimeoutCancelledSerials.Add(cancelledSerial);\n                runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,\n                    ProjectionWorkerJobKey);\n                projectionWorkerPending = false;\n                projectionWorkerPendingSerial = -1L;\n                projectionWorkerSubmittedRealtime = -1f;\n                projectionWorkerCompleted = null;\n                operationHealthProjectionWorkerTimeoutFallbacks++;\n                return false;\n            }\n\n            if (visible == null || vessel == null || vessel.mainBody == null ||\n                drawEntriesScratch == null || drawEntriesScratch.Length == 0)\n                return false;\n            if (runtime == null || runtime.Scheduler == null) return false;\n'''
w = replace_once(w, old_submit_prefix, new_submit_prefix, 'worker handoff submit prefix')
w = replace_once(w,
    '            projectionWorkerPending = true;\n'
    '            bool accepted = runtime.Scheduler.SubmitRequired(\n',
    '            projectionWorkerPending = true;\n'
    '            projectionWorkerPendingSerial = request.Serial;\n'
    '            bool accepted = runtime.Scheduler.SubmitRequired(\n',
    'pending serial ownership')
w = replace_once(w,
    '            if (!accepted)\n'
    '            {\n'
    '                projectionWorkerPending = false;\n'
    '                return false;\n'
    '            }\n'
    '            operationHealthProjectionWorkerSubmits++;\n',
    '            if (!accepted)\n'
    '            {\n'
    '                projectionWorkerPending = false;\n'
    '                projectionWorkerPendingSerial = -1L;\n'
    '                projectionWorkerSubmittedRealtime = -1f;\n'
    '                return false;\n'
    '            }\n'
    '            projectionWorkerSubmittedRealtime = Time.realtimeSinceStartup;\n'
    '            operationHealthProjectionWorkerSubmits++;\n',
    'submit timestamp')
old_complete = '''        void CompleteProjectionWorker(ProjectionWorkerRequest request, object value)\n        {\n            // Scheduler drains this on the main thread under its own commit lock. Do not\n            // render or touch native Unity state here.\n            projectionWorkerPending = false;\n            if (disposed)\n            {\n                projectionWorkerCompleted = null;\n                return;\n            }\n            ProjectionWorkerResult result = value as ProjectionWorkerResult;\n            if (result == null || request == null || result.Request == null ||\n                result.Request.Serial != request.Serial)\n            {\n                operationHealthProjectionWorkerFailures++;\n                projectionWorkerCompleted = null;\n                return;\n            }\n            projectionWorkerCompleted = result;\n        }\n'''
new_complete = '''        void CompleteProjectionWorker(ProjectionWorkerRequest request, object value)\n        {\n            // Scheduler drains this on the main thread under its own commit lock. Do not\n            // render or touch native Unity state here. Serial ownership prevents an old\n            // timeout-cancelled callback from clearing a newer worker request.\n            long requestSerial = request == null ? -1L : request.Serial;\n            bool timeoutCancelled = requestSerial >= 0L &&\n                projectionWorkerTimeoutCancelledSerials.Remove(requestSerial);\n            if (projectionWorkerPendingSerial == requestSerial)\n            {\n                projectionWorkerPending = false;\n                projectionWorkerPendingSerial = -1L;\n                projectionWorkerSubmittedRealtime = -1f;\n            }\n            if (disposed)\n            {\n                projectionWorkerCompleted = null;\n                return;\n            }\n            if (timeoutCancelled)\n            {\n                // CancelKey intentionally terminates this request; it is not a worker\n                // failure and must not overwrite a newer completed result.\n                return;\n            }\n            if (requestSerial != projectionWorkerSerial)\n            {\n                operationHealthProjectionWorkerStale++;\n                return;\n            }\n            ProjectionWorkerResult result = value as ProjectionWorkerResult;\n            if (result == null || request == null || result.Request == null ||\n                result.Request.Serial != request.Serial)\n            {\n                operationHealthProjectionWorkerFailures++;\n                projectionWorkerCompleted = null;\n                return;\n            }\n            projectionWorkerCompleted = result;\n        }\n'''
w = replace_once(w, old_complete, new_complete, 'worker completion ownership')
old_defer = '''            if (frontBufferValid && Time.realtimeSinceStartup - frontCommittedRealtime <\n                ProjectionWorkerMinimumCommitIntervalSeconds)\n            {\n                operationHealthProjectionWorkerCommitDeferrals++;\n                return false;\n            }\n\n            projectionWorkerCompleted = null;\n'''
new_defer = '''            if (frontBufferValid && Time.realtimeSinceStartup - frontCommittedRealtime <\n                ProjectionWorkerMinimumCommitIntervalSeconds)\n            {\n                long serial = result.Request == null ? -1L : result.Request.Serial;\n                if (projectionWorkerLastDeferredSerial != serial)\n                {\n                    projectionWorkerLastDeferredSerial = serial;\n                    operationHealthProjectionWorkerCommitDeferrals++;\n                }\n                return false;\n            }\n\n            projectionWorkerLastDeferredSerial = -1L;\n            projectionWorkerCompleted = null;\n'''
w = replace_once(w, old_defer, new_defer, 'worker defer telemetry')
old_telemetry = '''                "; oh_project_worker_defer=" + operationHealthProjectionWorkerCommitDeferrals +\n                "; project_worker_buffer_bytes=" + operationHealthProjectionWorkerBufferBytes +\n                "; project_worker_ms=" + lastProjectionWorkerMilliseconds.ToString("F3",\n                    CultureInfo.InvariantCulture) +\n                "; project_worker_pending=" +\n                    (projectionWorkerPending ? "1" : "0");\n'''
new_telemetry = '''                "; oh_project_worker_defer=" + operationHealthProjectionWorkerCommitDeferrals +\n                "; oh_project_worker_wait_hold=" + operationHealthProjectionWorkerWaitHolds +\n                "; oh_project_worker_timeout=" + operationHealthProjectionWorkerTimeoutFallbacks +\n                "; project_worker_buffer_bytes=" + operationHealthProjectionWorkerBufferBytes +\n                "; project_worker_ms=" + lastProjectionWorkerMilliseconds.ToString("F3",\n                    CultureInfo.InvariantCulture) +\n                "; project_worker_pending=" +\n                    (projectionWorkerPending ? "1" : "0") +\n                "; project_worker_pending_age_ms=" +\n                    (projectionWorkerPending && projectionWorkerSubmittedRealtime >= 0f ?\n                        Math.Max(0f, (Time.realtimeSinceStartup -\n                            projectionWorkerSubmittedRealtime) * 1000f).ToString("F1",\n                            CultureInfo.InvariantCulture) : "0.0") +\n                "; project_worker_handoff_hf=1";\n'''
w = replace_once(w, old_telemetry, new_telemetry, 'worker handoff telemetry')
WPATH.write_text(w)

runner = RUNNER.read_text()
runner = replace_once(runner,
    "suites=[\n ('Operation Health Step 3 Worker Projection','selftest_v01800_operation_health_step3_worker_projection.py'),\n",
    "suites=[\n ('Operation Health Step 3 Worker Projection Handoff Hotfix 1','selftest_v01800_operation_health_step3_worker_handoff_hotfix1.py'),\n ('Operation Health Step 3 Worker Projection','selftest_v01800_operation_health_step3_worker_projection.py'),\n",
    'prebuild runner hotfix suite')
RUNNER.write_text(runner)

TEST.write_text(r'''#!/usr/bin/env python3
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
''')
print('Step 3 Worker Projection Handoff Hotfix 1 patch applied.')
