#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
RPATH = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
MPATH = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
UPATH = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_005]'
REV004 = 'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE'
REV005 = 'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION'


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch count=%d' % (PREFIX, label, count))
    return text.replace(old, new, 1)


def require(text, needle, label):
    if needle not in text:
        raise SystemExit('%s missing %s' % (PREFIX, label))


r = RPATH.read_text()
m = MPATH.read_text()
u = UPATH.read_text()
if REV005 in r:
    print(PREFIX + ' non-blocking speculative preparation already present')
    raise SystemExit(0)
require(r, REV004, 'rev004 parent renderer marker')
require(m, 'internal const string Revision = "OH_PHASE6_004";', 'rev004 OH identity')
require(u, 'REV004 MANAGED PREPARATION PIPELINE', 'rev004 build identity')
require(u, 'verify_aeris25_managed_preparation_pipeline_hotfix.py', 'rev004 final verifier')

# Marker: REV005 is a narrow successor. It preserves REV004 worker preparation but
# removes worker completion from the single authoritative pending-commit critical path.
marker_anchor = '''        // AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE: large managed array\n'''
marker_insert = '''        // AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION: worker-managed\n        // preparation is speculative and detached from the single staged-commit head.\n        // Up to four immutable Entry preparations may be in flight; only completed\n        // payloads re-enter Unity Mesh upload. No worker completion is a presentation\n        // authority prerequisite and one slow Entry cannot head-of-line block later work.\n''' + marker_anchor
r = replace_once(r, marker_anchor, marker_insert, 'rev005 renderer marker')

# Detached waiter pool. Four is deliberately below the scheduler's normal GeneralCompute
# capacity and bounds the extra managed allocation pressure observed in REV004.
field_anchor = '''        PendingEntryCommit pendingEntryCommit;\n        long operationHealthMainCommitStageYields;\n'''
field_insert = '''        const int ManagedPreparationMaximumInFlight = 4;\n        PendingEntryCommit pendingEntryCommit;\n        readonly List<PendingEntryCommit> managedPreparationWaiters =\n            new List<PendingEntryCommit>(ManagedPreparationMaximumInFlight);\n        long operationHealthMainCommitStageYields;\n'''
r = replace_once(r, field_anchor, field_insert, 'detached waiter fields')

telemetry_anchor = '''        long operationHealthManagedPrepBytesTotal;\n        long operationHealthCpuFallbackLazyAllocations;\n'''
telemetry_insert = '''        long operationHealthManagedPrepBytesTotal;\n        long operationHealthManagedPrepDetached;\n        long operationHealthManagedPrepReadyResumed;\n        long operationHealthManagedPrepHolBypass;\n        int operationHealthManagedPrepWaiterPeak;\n        long operationHealthCpuFallbackLazyAllocations;\n'''
r = replace_once(r, telemetry_anchor, telemetry_insert, 'rev005 telemetry fields')

# Hidden frames and authoritative content ticks only pump detached work once a callback
# has made a waiter terminal/ready. This avoids a per-frame busy poll while workers run.
r = replace_once(r,
'''                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0)\n''',
'''                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||\n                    HasReadyManagedPreparationWaiter())\n''', 'hidden-frame ready gate')
r = replace_once(r,
'''            bool workerResultReady = pendingEntryCommit != null || rasterizer.CompletedCount > 0;\n''',
'''            bool workerResultReady = pendingEntryCommit != null ||\n                rasterizer.CompletedCount > 0 || HasReadyManagedPreparationWaiter();\n''', 'authoritative ready gate')

# Pump: prefer any completed detached preparation. If none is ready, admit another raster
# result only while the bounded detached pool has capacity. The rasterizer queue itself
# becomes backpressure instead of one WaitManagedPreparation object blocking the head.
pump_old = '''                if (pendingEntryCommit == null)\n                {\n                    completed.Clear();\n                    if (rasterizer.Drain(completed, 1) <= 0) break;\n                    AERISTerrainGpuTileRasterResult result = completed[0];\n                    if (!TryBeginPendingEntryCommit(result)) continue;\n                }\n'''
pump_new = '''                if (pendingEntryCommit == null)\n                {\n                    if (!TryActivateReadyManagedPreparationWaiter())\n                    {\n                        if (managedPreparationWaiters.Count >=\n                            ManagedPreparationMaximumInFlight) break;\n                        completed.Clear();\n                        if (rasterizer.Drain(completed, 1) <= 0) break;\n                        AERISTerrainGpuTileRasterResult result = completed[0];\n                        if (!TryBeginPendingEntryCommit(result)) continue;\n                    }\n                }\n'''
r = replace_once(r, pump_old, pump_new, 'pump detached waiter activation')

r = replace_once(r,
'''            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +\n                (pendingEntryCommit == null ? 0 : 1);\n''',
'''            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +\n                (pendingEntryCommit == null ? 0 : 1) + managedPreparationWaiters.Count;\n''', 'backlog includes detached waiters')
r = replace_once(r,
'''            operationHealthMainCommitPendingPeak = Math.Max(\n                operationHealthMainCommitPendingPeak, pendingEntryCommit == null ? 0 : 1);\n''',
'''            operationHealthMainCommitPendingPeak = Math.Max(\n                operationHealthMainCommitPendingPeak,\n                (pendingEntryCommit == null ? 0 : 1) + managedPreparationWaiters.Count);\n''', 'pending peak includes detached waiters')

# Submit stage: after an asynchronous worker is admitted, detach this Entry from the
# authoritative single pending slot. A synchronous fallback remains current and proceeds
# directly through the existing Wait/apply path.
submit_old = '''                    case PendingEntryCommitStage.SubmitManagedPreparation:\n                        if (!SubmitManagedPreparation(pending))\n                        {\n                            operationHealthMainCommitStageYields++;\n                            return false;\n                        }\n                        pending.Stage = PendingEntryCommitStage.WaitManagedPreparation;\n                        return false;\n'''
submit_new = '''                    case PendingEntryCommitStage.SubmitManagedPreparation:\n                        if (!SubmitManagedPreparation(pending))\n                        {\n                            operationHealthMainCommitStageYields++;\n                            return false;\n                        }\n                        pending.Stage = PendingEntryCommitStage.WaitManagedPreparation;\n                        if (!pending.ManagedPreparationCompleted &&\n                            !pending.ManagedPreparationFailed)\n                        {\n                            DetachManagedPreparationWaiter(pending);\n                            return true;\n                        }\n                        break;\n'''
r = replace_once(r, submit_old, submit_new, 'submit stage detach')

# REV004's callback deliberately ignored any PendingEntryCommit that was no longer the
# single head. REV005 makes the closure-owned PendingEntryCommit itself the completion
# mailbox. DrainCommits runs this callback on the main thread, so no Unity/API cross-thread
# access or extra lock is introduced.
r = replace_once(r,
'''                }, value =>\n                {\n                    if (!ReferenceEquals(pendingEntryCommit, pending)) return;\n                    pending.ManagedPreparationSubmitted = false;\n''',
'''                }, value =>\n                {\n                    pending.ManagedPreparationSubmitted = false;\n''', 'detached completion callback')

# Helpers are inserted immediately before SubmitManagedPreparation so all worker-building
# code remains byte-for-byte REV004 except for completion ownership.
helper_anchor = '''        bool SubmitManagedPreparation(PendingEntryCommit pending)\n'''
helpers = '''        bool HasReadyManagedPreparationWaiter()\n        {\n            for (int i = 0; i < managedPreparationWaiters.Count; i++)\n            {\n                PendingEntryCommit pending = managedPreparationWaiters[i];\n                if (pending != null && (pending.ManagedPreparationCompleted ||\n                    pending.ManagedPreparationFailed)) return true;\n            }\n            return false;\n        }\n\n        bool TryActivateReadyManagedPreparationWaiter()\n        {\n            for (int i = 0; i < managedPreparationWaiters.Count; i++)\n            {\n                PendingEntryCommit pending = managedPreparationWaiters[i];\n                if (pending == null || pending.Result == null)\n                {\n                    managedPreparationWaiters.RemoveAt(i--);\n                    continue;\n                }\n                if (!pending.ManagedPreparationCompleted &&\n                    !pending.ManagedPreparationFailed) continue;\n                managedPreparationWaiters.RemoveAt(i);\n                pendingEntryCommit = pending;\n                operationHealthManagedPrepReadyResumed++;\n                return true;\n            }\n            return false;\n        }\n\n        void DetachManagedPreparationWaiter(PendingEntryCommit pending)\n        {\n            if (pending == null || !ReferenceEquals(pendingEntryCommit, pending)) return;\n            if (!managedPreparationWaiters.Contains(pending))\n                managedPreparationWaiters.Add(pending);\n            bool detachedScratch = false;\n            if (ReferenceEquals(landSurfaceScratch, pending.Land))\n            {\n                landSurfaceScratch = new SurfaceBuilder();\n                detachedScratch = true;\n            }\n            if (ReferenceEquals(waterSurfaceScratch, pending.Water))\n            {\n                waterSurfaceScratch = new SurfaceBuilder();\n                detachedScratch = true;\n            }\n            if (detachedScratch) operationHealthManagedPrepScratchDetached++;\n            pendingEntryCommit = null;\n            operationHealthManagedPrepDetached++;\n            operationHealthManagedPrepHolBypass++;\n            operationHealthManagedPrepWaiterPeak = Math.Max(\n                operationHealthManagedPrepWaiterPeak, managedPreparationWaiters.Count);\n        }\n\n        void CancelManagedPreparationWaiters()\n        {\n            if (managedPreparationWaiters.Count == 0) return;\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            for (int i = 0; i < managedPreparationWaiters.Count; i++)\n            {\n                PendingEntryCommit pending = managedPreparationWaiters[i];\n                if (pending == null) continue;\n                if (pending.ManagedPreparationSubmitted &&\n                    !pending.ManagedPreparationCompleted && runtime != null &&\n                    !string.IsNullOrEmpty(pending.ManagedPreparationKey))\n                    runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,\n                        pending.ManagedPreparationKey);\n                RecycleMesh(ref pending.PackedMesh);\n                RecycleMesh(ref pending.ContourMesh);\n                RecycleMesh(ref pending.CoastlineMesh);\n                pending.ManagedPreparation = null;\n            }\n            managedPreparationWaiters.Clear();\n        }\n\n'''
r = replace_once(r, helper_anchor, helpers + helper_anchor,
                 'nonblocking waiter helper insertion')

# Snapshot/lifecycle reset must invalidate both the current commit and every detached job.
r = replace_once(r,
'''            ReleaseDeferredEntryRetirements(true);\n            CancelPendingEntryCommit();\n        }\n\n        void RefreshPresentationPackets''',
'''            ReleaseDeferredEntryRetirements(true);\n            CancelPendingEntryCommit();\n            CancelManagedPreparationWaiters();\n        }\n\n        void RefreshPresentationPackets''', 'snapshot reset cancels waiters')
r = replace_once(r,
'''            disposed = true;\n            CancelPendingEntryCommit();\n            rasterizer.Dispose();\n''',
'''            disposed = true;\n            CancelPendingEntryCommit();\n            CancelManagedPreparationWaiters();\n            rasterizer.Dispose();\n''', 'dispose cancels waiters')

# Runtime telemetry makes the failure mode directly observable. Existing REV004 fields are
# retained for A/B, while waiter count/peak and HOL bypass prove the new path actually runs.
r = replace_once(r,
'''                "; oh_managed_prep_scratch_detach=" + operationHealthManagedPrepScratchDetached +\n                "; oh_cpu_fallback_lazy_alloc=" + operationHealthCpuFallbackLazyAllocations +\n''',
'''                "; oh_managed_prep_scratch_detach=" + operationHealthManagedPrepScratchDetached +\n                "; oh_managed_prep_waiters=" + managedPreparationWaiters.Count +\n                "; oh_managed_prep_waiter_peak=" + operationHealthManagedPrepWaiterPeak +\n                "; oh_managed_prep_detached=" + operationHealthManagedPrepDetached +\n                "; oh_managed_prep_ready_resume=" + operationHealthManagedPrepReadyResumed +\n                "; oh_managed_prep_hol_bypass=" + operationHealthManagedPrepHolBypass +\n                "; oh_cpu_fallback_lazy_alloc=" + operationHealthCpuFallbackLazyAllocations +\n''', 'rev005 telemetry output')

# Identity remains NOREPINEPHRINE / same technical candidate; this is a hotfix revision.
m = replace_once(m,
'''        internal const string Revision = "OH_PHASE6_004";\n''',
'''        internal const string Revision = "OH_PHASE6_005";\n''', 'OH revision')
u = replace_once(u,
'AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE',
'AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV005 NON-BLOCKING SPECULATIVE PREPARATION',
'build display')
u = replace_once(u,
'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE',
'DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV005 NON-BLOCKING SPECULATIVE PREPARATION',
'UI checkpoint')
u = replace_once(u,
'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_managed_preparation_pipeline_hotfix.py"',
'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_nonblocking_speculative_preparation_hotfix.py"',
'final build verifier')

RPATH.write_text(r)
MPATH.write_text(m)
UPATH.write_text(u)
print(PREFIX + ' applied non-blocking speculative preparation')
print('revision=OH_PHASE6_005')
print('max_in_flight=%d' % 4)
