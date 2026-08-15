#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

# Final build executes these inherited gates before rev008. Re-run exactly that matrix
# on the final generated tree so successor allowlist drift cannot hide until install.
for name in [
    'verify_aeris25_gpu_dynamic_terrain_colour_ready.py',
    'verify_aeris25_chunk_cull_guard_hotfix.py',
    'verify_aeris25_temporal_foundation_overscan_hotfix.py',
]:
    script = ROOT / 'Tools' / name
    if not script.is_file():
        raise SystemExit('[AERIS25 ATROPINE REV008] inherited final-tree verifier missing: ' + name)
    print('[AERIS25 ATROPINE REV008 INHERITED] $ ' + name)
    subprocess.run([sys.executable, str(script)], cwd=str(ROOT), check=True)
print('[AERIS25 ATROPINE REV008 INHERITED] FINAL-TREE VERIFIER MATRIX PASS')

R = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
U = (ROOT / 'build_ubuntu.sh').read_text()
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


ck('internal const string Revision = "OH_PHASE4_008";' in M,
   'ATROPINE revision is OH_PHASE4_008')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R,
   'renderer carries snapshot Mesh lifetime marker')
ck('bool IsEntryProtectedByContentSnapshot(Entry entry)' in R and
   'ReferenceEquals(drawEntriesScratch[i], entry)' in R,
   'content snapshot draw Entry references are explicit prune pins')
ck(R.count('if (IsEntryProtectedByContentSnapshot(entry))') == 2 and
   R.count('operationHealthSnapshotMeshPruneProtected++;') == 2,
   'normal prune and warm-resume prune both skip snapshot-owned Entries')
ck(R.count('operationHealthSnapshotMeshPruneDeferrals++;') == 2,
   'both prune paths fail closed by deferring when only protected Entries remain')
ck('void RecycleMesh(ref Mesh mesh)' in R and
   'RecycleMesh(ref entry.PackedTerrainMesh);' in R,
   'ordinary Mesh recycle path is preserved for unprotected Entries')

resolve = R.find('ResolveRenderableEntries(tile, cacheKey, styleKey,')
prune = R.find('Prune(vramLimitBytes);', resolve)
render = R.find('RenderBackBuffer(tiles, drawEntriesScratch,', prune)
ck(0 <= resolve < prune < render,
   'static ordering reproduces rev007 hazard, now made safe by prune pinning')
ck('if (!HasRenderableTerrain(drawEntry))\n                        operationHealthSnapshotStaleMeshDetections++;' in R,
   'draw path directly witnesses any remaining stale snapshot Mesh')
ck('oh_snapshot_mesh_prune_protect=' in R and
   'oh_snapshot_mesh_prune_defer=' in R and
   'oh_snapshot_stale_mesh=' in R,
   'runtime publishes snapshot lifetime guard telemetry')
ck('oh_gpu_vertex_reject_semantic_mesh_null=' in R and
   'AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS' in R,
   'rev007 attribution remains available to prove the failure class disappears')
ck('oh_nonrenderable_entry_reject=' in R and
   'oh_fallback_shadow_prevent=' in R and
   'oh_empty_triangle_result=' in R,
   'rev006 rejected-hypothesis witnesses remain visible')
ck('AERIS25_CHUNK_CULL_GUARD' in R and
   'AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R and
   'operationHealthFoundationCullBypass++' not in R,
   'rev003/rev004 path and rev005 rollback remain unchanged')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' not in SH,
   'rev008 changes no shader equations/bytes')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')

active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('REV008 SNAPSHOT MESH LIFETIME GUARD' in U and
   'verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py' in active and
   'verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py' not in active,
   'build identity and active successor verifier are rev008-specific')

# Final-tree inherited gates must explicitly admit rev008.
core = (ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour.py').read_text()
ready = (ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py').read_text()
cull = (ROOT / 'Tools/verify_aeris25_chunk_cull_guard_hotfix.py').read_text()
over = (ROOT / 'Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py').read_text()
ck('OH_PHASE4_008' in core and 'OH_PHASE4_008' in ready and
   'OH_PHASE4_008' in cull and 'OH_PHASE4_008' in over,
   'all inherited final build verifiers explicitly admit rev008')
ck('REV008 SNAPSHOT MESH LIFETIME GUARD' in cull and
   'REV008 SNAPSHOT MESH LIFETIME GUARD' in over,
   'inherited presentation verifiers admit rev008 build identity')

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
print('\n[AERIS25 ATROPINE REV008 SNAPSHOT MESH LIFETIME GUARD] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV008 SNAPSHOT MESH LIFETIME GUARD] STATIC PASS')
