#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
MARKER = "oh_nd_warm_suspend_count="


def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS24 WARM VISIBILITY] %s: expected 1 anchor, found %d" %
                         (label, count))
    return text.replace(old, new, 1)


renderer_path = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
ui_path = ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs"
backend_path = ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"

R = renderer_path.read_text()
U = ui_path.read_text()
B = backend_path.read_text()

if MARKER in R and "SuspendVisibilityWarm" in R and "ResumeVisibilityWarm" in R and \
   "PruneWarmResume" in R and "SuspendVisibilityWarm();" in U:
    print("[AERIS24 WARM VISIBILITY] already patched")
else:
    if "oh_gpu_vertex_resident_suspend=" not in R or \
       "RetainForViewportSuspension" not in B or \
       "oh_nd_reload_snapshot=" not in R:
        raise SystemExit("[AERIS24 WARM VISIBILITY] rev006 predecessor absent")

    # Visibility-only suspension has a different lifecycle from Terrain OFF/subsystem
    # teardown. Keep the complete last presentation resident and stop only work authority.
    R = replace_once(R,
'''        long operationHealthViewInvalidations;\n        long operationHealthMeshPoolHits;''',
'''        long operationHealthViewInvalidations;\n        // AERIS24 rev007 Warm Visibility Suspend. Display OFF stops work but retains\n        // the last complete presentation resources. Fresh resume uses black reload and\n        // amortized stale-entry pruning instead of synchronous teardown/rebuild.\n        long operationHealthWarmVisibilitySuspends;\n        long operationHealthWarmVisibilityResumes;\n        long operationHealthWarmPruneTicks;\n        long operationHealthWarmPruneRemoved;\n        long operationHealthWarmPruneDeferrals;\n        long operationHealthMeshPoolHits;''',
'warm visibility telemetry fields')

    R = replace_once(R,
'''        bool gpuFailed;\n        bool disposed;\n        float lastCoverageFraction;''',
'''        bool gpuFailed;\n        bool disposed;\n        bool warmVisibilitySuspended;\n        bool warmVisibilityPrunePending;\n        bool warmVisibilityPruneActive;\n        int warmVisibilityPreservedEntries;\n        long warmVisibilityPreservedBytes;\n        long warmVisibilityMeshDestroyBaseline;\n        long warmVisibilityAttributeUploadBaseline;\n        float lastCoverageFraction;''',
'warm visibility state fields')

    R = replace_once(R,
'''        internal void SuspendViewport()\n        {\n            generation++;''',
'''        internal void SuspendVisibilityWarm()\n        {\n            if (disposed || warmVisibilitySuspended) return;\n            warmVisibilitySuspended = true;\n            warmVisibilityPrunePending = false;\n            warmVisibilityPruneActive = false;\n            warmVisibilityPreservedEntries = entries.Count;\n            warmVisibilityPreservedBytes = usedEntryBytes + backTargetBytes +\n                frontTargetBytes + renderReadyBytes;\n            warmVisibilityMeshDestroyBaseline = operationHealthMeshPoolDestroys;\n            warmVisibilityAttributeUploadBaseline = operationHealthGpuVertexAttributeUploads;\n            operationHealthWarmVisibilitySuspends++;\n\n            // Reuse the existing transactional view invalidation: it cancels obsolete\n            // worker work and starts a new black-reload generation, but deliberately\n            // retains FRONT/BACK, Entry meshes, render-ready fields and GPU attributes.\n            InvalidatePendingForViewChange();\n            lastVisualCoverageFraction = 0f;\n            gpuVertexProjection.RetainForViewportSuspension();\n            AERISLogger.Info("[AERIS24_ND_WARM_SUSPEND] ENTER; entries=" +\n                warmVisibilityPreservedEntries + "; bytes=" + warmVisibilityPreservedBytes +\n                "; meshDestroyBaseline=" + warmVisibilityMeshDestroyBaseline +\n                "; attrUploadBaseline=" + warmVisibilityAttributeUploadBaseline +\n                "; reloadGeneration=" + ndReloadGeneration + ".");\n        }\n\n        internal void ResumeVisibilityWarm()\n        {\n            if (disposed || !warmVisibilitySuspended) return;\n            warmVisibilitySuspended = false;\n            warmVisibilityPrunePending = true;\n            warmVisibilityPruneActive = false;\n            operationHealthWarmVisibilityResumes++;\n            AERISLogger.Info("[AERIS24_ND_WARM_SUSPEND] RESUME; preservedEntries=" +\n                warmVisibilityPreservedEntries + "; currentEntries=" + entries.Count +\n                "; meshDestroyDelta=" + Math.Max(0L, operationHealthMeshPoolDestroys -\n                    warmVisibilityMeshDestroyBaseline) +\n                "; attrUploadDelta=" + Math.Max(0L, operationHealthGpuVertexAttributeUploads -\n                    warmVisibilityAttributeUploadBaseline) +\n                "; reloadGeneration=" + ndReloadGeneration + ".");\n        }\n\n        internal void SuspendViewport()\n        {\n            generation++;''',
'warm visibility lifecycle methods')

    # ND visibility policy owns warm lifecycle. Terrain Display OFF continues to call the
    # original cold SuspendViewport() path from inside Draw().
    U = replace_once(U,
'''            if (active)\n            {\n                nextNavigationSnapshotRealtime = 0f;\n                nextSymbologySnapshotRealtime = 0f;\n                nextNavigationCaptureRealtime = 0f;\n                return;\n            }\n\n            if (terrainTileRenderer != null) terrainTileRenderer.SuspendViewport();''',
'''            if (active)\n            {\n                if (terrainTileRenderer != null) terrainTileRenderer.ResumeVisibilityWarm();\n                nextNavigationSnapshotRealtime = 0f;\n                nextSymbologySnapshotRealtime = 0f;\n                nextNavigationCaptureRealtime = 0f;\n                return;\n            }\n\n            if (terrainTileRenderer != null) terrainTileRenderer.SuspendVisibilityWarm();''',
'ND visibility uses warm lifecycle')

    # During black reload after a visibility resume, retain all old Entry resources until
    # the fresh FRONT has committed. Afterwards reclaim at most four stale entries per
    # content-maintenance tick so Unity Mesh destruction cannot bunch into one frame.
    R = replace_once(R,
'''            if (contentTickRequired)\n            {\n                Prune(ResolveVramLimitBytes());\n                PruneRenderReady(ResolveRenderReadyLimitBytes());\n            }''',
'''            if (contentTickRequired)\n            {\n                long vramLimitBytes = ResolveVramLimitBytes();\n                if (warmVisibilityPrunePending)\n                {\n                    if (!Reloading)\n                    {\n                        warmVisibilityPrunePending = false;\n                        warmVisibilityPruneActive = true;\n                    }\n                    else operationHealthWarmPruneDeferrals++;\n                }\n\n                if (warmVisibilityPruneActive)\n                {\n                    operationHealthWarmPruneTicks++;\n                    warmVisibilityPruneActive = PruneWarmResume(vramLimitBytes, 4);\n                }\n                else if (!warmVisibilityPrunePending)\n                    Prune(vramLimitBytes);\n\n                // Do not compete with fresh-FRONT construction by dropping managed\n                // render-ready payloads during the warm black-reload interval.\n                if (!warmVisibilityPrunePending)\n                    PruneRenderReady(ResolveRenderReadyLimitBytes());\n            }''',
'warm resume prune scheduling')

    R = replace_once(R,
'''        void Prune(long totalLimit)\n        {''',
'''        bool PruneWarmResume(long totalLimit, int maximumRemovals)\n        {\n            totalLimit = Math.Max(16L * 1024L * 1024L, totalLimit);\n            long fixedBytes = Math.Max(0L, backTargetBytes) +\n                Math.Max(0L, frontTargetBytes);\n            long entryLimit = Math.Max(4L * 1024L * 1024L, totalLimit - fixedBytes);\n            int removed = 0;\n            int budget = Math.Max(1, maximumRemovals);\n            while (usedEntryBytes > entryLimit && entries.Count > 1 && removed < budget)\n            {\n                Entry oldest = null;\n                foreach (Entry entry in entries.Values)\n                {\n                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;\n                }\n                if (oldest == null) break;\n                Remove(oldest);\n                removed++;\n                operationHealthWarmPruneRemoved++;\n            }\n            bool stillOverLimit = usedEntryBytes > entryLimit && entries.Count > 1;\n            if (stillOverLimit) operationHealthWarmPruneDeferrals++;\n            return stillOverLimit;\n        }\n\n        void Prune(long totalLimit)\n        {''',
'bounded warm resume prune helper')

    R = replace_once(R,
'''                "; oh_gpu_vertex_activation=" + gpuVertexProjection.ActivationCount +\n                "; oh_gpu_vertex_resident_suspend=" + gpuVertexProjection.ResidentSuspensionCount +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +''',
'''                "; oh_gpu_vertex_activation=" + gpuVertexProjection.ActivationCount +\n                "; oh_gpu_vertex_resident_suspend=" + gpuVertexProjection.ResidentSuspensionCount +\n                "; oh_nd_warm_visibility=" + (warmVisibilitySuspended ? "HIDDEN" : "LIVE") +\n                "; oh_nd_warm_suspend_count=" + operationHealthWarmVisibilitySuspends +\n                "; oh_nd_warm_resume_count=" + operationHealthWarmVisibilityResumes +\n                "; oh_nd_warm_preserved_entries=" + warmVisibilityPreservedEntries +\n                "; oh_nd_warm_preserved_bytes=" + warmVisibilityPreservedBytes +\n                "; oh_nd_warm_mesh_destroy_delta=" + Math.Max(0L,\n                    operationHealthMeshPoolDestroys - warmVisibilityMeshDestroyBaseline) +\n                "; oh_nd_warm_attr_upload_delta=" + Math.Max(0L,\n                    operationHealthGpuVertexAttributeUploads - warmVisibilityAttributeUploadBaseline) +\n                "; oh_nd_warm_prune_pending=" + (warmVisibilityPrunePending ? 1 : 0) +\n                "; oh_nd_warm_prune_active=" + (warmVisibilityPruneActive ? 1 : 0) +\n                "; oh_nd_warm_prune_ticks=" + operationHealthWarmPruneTicks +\n                "; oh_nd_warm_prune_removed=" + operationHealthWarmPruneRemoved +\n                "; oh_nd_warm_prune_deferred=" + operationHealthWarmPruneDeferrals +\n                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +''',
'warm visibility telemetry publication')

    renderer_path.write_text(R)
    ui_path.write_text(U)

verifier = ROOT / "Tools/verify_aeris24_warm_visibility_suspend_hotfix.py"
if verifier.is_file():
    subprocess.run([sys.executable, str(verifier)], cwd=str(ROOT), check=True)
print("[AERIS24 WARM VISIBILITY] PASS")
