#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_writebytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
U = (ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs").read_text()
B = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()

checks = []
def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)

ck('internal void SuspendVisibilityWarm()' in R and
   'internal void ResumeVisibilityWarm()' in R,
   'warm visibility lifecycle is explicit and separate')
ck('terrainTileRenderer.SuspendVisibilityWarm();' in U and
   'terrainTileRenderer.ResumeVisibilityWarm();' in U,
   'ND display visibility uses warm lifecycle')

warm_start = R.index('internal void SuspendVisibilityWarm()')
warm_end = R.index('internal void SuspendViewport()', warm_start)
warm = R[warm_start:warm_end]
ck('InvalidatePendingForViewChange();' in warm and
   'gpuVertexProjection.RetainForViewportSuspension();' in warm,
   'warm suspend cancels obsolete work and starts transactional black reload')
ck('ReleaseGpuResources();' not in warm and 'ResetFrontBufferState();' not in warm and
   'entries.Clear();' not in warm and 'DestroyRenderTexture' not in warm,
   'warm suspend retains Entry meshes and FRONT/BACK resources')
ck('warmVisibilityPreservedEntries = entries.Count;' in warm and
   'warmVisibilityPreservedBytes = usedEntryBytes + backTargetBytes +' in warm,
   'warm suspend captures retained presentation footprint')

cold_start = R.index('internal void SuspendViewport()', warm_end)
cold_end = R.index('internal void ResetGpuFailure()', cold_start)
cold = R[cold_start:cold_end]
ck('ReleaseGpuResources();' in cold and 'ResetFrontBufferState();' in cold,
   'cold Terrain/subsystem suspend retains full presentation teardown path')

resume_start = R.index('internal void ResumeVisibilityWarm()')
resume_end = R.index('internal void SuspendViewport()', resume_start)
resume = R[resume_start:resume_end]
ck('AssetBundle.' not in resume and 'LoadFromFile' not in resume and
   'TryEnsureLoaded' not in resume and 'new Material' not in resume,
   'warm resume itself performs no bundle/material initialization')
ck('warmVisibilityPrunePending = true;' in resume and
   'operationHealthWarmVisibilityResumes++;' in resume,
   'warm resume arms deferred stale-entry cleanup')

ck('if (warmVisibilityPrunePending)' in R and 'if (!Reloading)' in R and
   'else operationHealthWarmPruneDeferrals++;' in R,
   'stale-entry cleanup is deferred until fresh FRONT completes')
ck('PruneWarmResume(vramLimitBytes, 4)' in R,
   'warm cleanup is bounded to four Entry removals per content tick')

prune_start = R.index('bool PruneWarmResume(long totalLimit, int maximumRemovals)')
prune_end = R.index('void Prune(long totalLimit)', prune_start)
prune = R[prune_start:prune_end]
ck('removed < budget' in prune and 'Remove(oldest);' in prune and
   'operationHealthWarmPruneRemoved++;' in prune,
   'warm prune helper enforces bounded removal and telemetry')
ck('void Prune(long totalLimit)' in R,
   'normal VRAM pruning remains intact outside warm resume')
ck('if (!warmVisibilityPrunePending)\n                    PruneRenderReady' in R,
   'render-ready eviction does not compete with warm black reload')

ck('oh_nd_warm_visibility=' in R and 'oh_nd_warm_suspend_count=' in R and
   'oh_nd_warm_resume_count=' in R and 'oh_nd_warm_preserved_entries=' in R and
   'oh_nd_warm_mesh_destroy_delta=' in R and 'oh_nd_warm_attr_upload_delta=' in R and
   'oh_nd_warm_prune_removed=' in R,
   'warm lifecycle and churn are directly observable')
ck('RetainForViewportSuspension' in B and 'ActivationCount' in B and
   'ResidentSuspensionCount' in B,
   'rev006 AssetBundle residency contract remains intact')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz authoritative cadence remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden render-target format remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R and
   'reloadSnapshotCenterLatitudeDeg' in R,
   'Runway Map Lock, Golden coverage and rev005 snapshot remain intact')

frozen = [
    'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect',
    'Source/AERISFlightControl/Landing',
]
try:
    changed = subprocess.check_output(
        ['git', '-C', str(ROOT), 'diff', '--name-only', 'HEAD', '--'] + frozen,
        text=True).strip().splitlines()
except Exception:
    changed = ['GIT_DIFF_UNAVAILABLE']
ck(changed == [], 'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed = [name for ok, name in checks if not ok]
print("\n[AERIS24 WARM VISIBILITY SUSPEND] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS24 WARM VISIBILITY SUSPEND] STATIC PASS')
