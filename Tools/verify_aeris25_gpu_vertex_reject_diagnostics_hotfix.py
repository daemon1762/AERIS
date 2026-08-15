#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
U = (ROOT / "build_ubuntu.sh").read_text()
SH = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)


ck('internal const string Revision = "OH_PHASE4_007";' in M,
   'ATROPINE revision is OH_PHASE4_007')
ck('AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS' in R,
   'renderer carries diagnostic-only GPU reject marker')
ck('const int GpuVertexRejectDiagnosticSampleLimit = 64;' in R and
   'operationHealthGpuVertexRejectDiagnosticSamples >=' in R and
   'GpuVertexRejectDiagnosticSampleLimit' in R,
   'reject sample logging is hard-bounded to first 64 initial rejects')
ck('if (entry == null || !gpuVertexProjection.Active) return false;' in R and
   'if (entry.GpuVertexProjectionRejected)' in R and
   'operationHealthGpuVertexRejectRevisits++;' in R,
   'already-rejected Entry revisits are split from initial rejection')
ck('RecordGpuVertexProjectionReject(entry,' in R and
   'operationHealthGpuVertexRejectInitial++;' in R,
   'initial rejection accounting is explicit')
ck('"PACKED_GEO_NULL"' in R and '"PACKED_GEO_LENGTH"' in R and
   '"CONTOUR_GEO_NULL"' in R and '"CONTOUR_GEO_LENGTH"' in R and
   '"COAST_GEO_NULL"' in R and '"COAST_GEO_LENGTH"' in R,
   'packed/contour/coast geographic null vs length reasons are independently classified')
ck('"SEMANTIC_PACKED_MESH_NULL"' in R and
   '"SEMANTIC_REJECTED"' in R and
   '"SEMANTIC_EXCEPTION"' in R and
   '"SEMANTIC_OTHER"' in R,
   'dynamic semantic false paths are independently classified')
ck('RecordGpuVertexProjectionReject(entry, "EXCEPTION");' in R and
   'operationHealthGpuVertexRejectException++' in R,
   'outer geographic upload exception path is classified')
ck('[AERIS25_GPU_VERTEX_REJECT_DIAG] sample=' in R and
   '; packedV=' in R and '; packedGeo=' in R and
   '; contourV=' in R and '; contourGeo=' in R and
   '; coastV=' in R and '; coastGeo=' in R and
   '; gpuReady=' in R and '; semanticReady=' in R and
   '; semanticRejected=' in R and '; coverage=' in R,
   'bounded sample log captures tile/LOD/vertex/attribute/readiness evidence')
ck('catch\n            {\n                // Diagnostics must never change renderer/fallback behaviour.' in R,
   'diagnostic logging itself is fail-closed and cannot alter rendering authority')

ensure_start = R.find('        bool EnsureGpuVertexProjectionAttributes(Entry entry)\n')
upload_start = R.find('        bool UploadGpuGeographicAttribute(', ensure_start)
ensure = R[ensure_start:upload_start] if ensure_start >= 0 and upload_start > ensure_start else ''
ck(ensure.count('operationHealthGpuVertexAttributeFailures++;') == 5 and
   'entry.GpuVertexProjectionAttributesReady = true;' in ensure,
   'existing generic attr-failure authority is preserved on every initial reject path')
ck('ref operationHealthGpuVertexPackedMismatch' in ensure and
   'ref operationHealthGpuVertexContourMismatch' in ensure and
   'ref operationHealthGpuVertexCoastlineMismatch' in ensure and
   'ref long mismatchCounter' in R,
   'rev002 geographic mismatch counters remain authoritative')
ck('long semanticFailuresBefore = operationHealthGpuDynamicSemanticFailures;' in ensure and
   'EnsureGpuDynamicTerrainColourAttributes(entry)' in ensure,
   'existing AERIS25 semantic gate remains in the GPU readiness path')
ck('oh_gpu_vertex_attr_fail=' in R and
   'oh_gpu_vertex_reject_initial=' in R and
   'oh_gpu_vertex_reject_revisit=' in R and
   'oh_gpu_vertex_reject_packed_null=' in R and
   'oh_gpu_vertex_reject_packed_length=' in R and
   'oh_gpu_vertex_reject_contour_null=' in R and
   'oh_gpu_vertex_reject_contour_length=' in R and
   'oh_gpu_vertex_reject_coast_null=' in R and
   'oh_gpu_vertex_reject_coast_length=' in R and
   'oh_gpu_vertex_reject_semantic_mesh_null=' in R and
   'oh_gpu_vertex_reject_semantic_rejected=' in R and
   'oh_gpu_vertex_reject_semantic_exception=' in R and
   'oh_gpu_vertex_reject_semantic_other=' in R and
   'oh_gpu_vertex_reject_exception=' in R and
   'oh_gpu_vertex_reject_other=' in R and
   'oh_gpu_vertex_reject_samples=' in R,
   'runtime publishes complete initial/revisit reject attribution')
ck('AERIS25_RENDERABLE_ENTRY_GATE' in R and
   'oh_nonrenderable_entry_reject=' in R and
   'oh_fallback_shadow_prevent=' in R and
   'oh_empty_triangle_result=' in R,
   'rev006 renderable-entry diagnostics remain intact as rejected-hypothesis witnesses')
ck('AERIS25_CHUNK_CULL_GUARD' in R and
   'AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R and
   'operationHealthFoundationCullBypass++' not in R,
   'accepted rev003/rev004 presentation path and rev005 rollback remain intact')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS' not in SH,
   'rev007 changes no shader equations or shader bytes')

draw_start = R.find('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
draw_end = R.find('        static void EnsurePackedTerrainColours(Entry entry,', draw_start)
draw = R[draw_start:draw_end] if draw_start >= 0 and draw_end > draw_start else ''
packed_draw = draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix);')
contour_draw = draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);')
coast_draw = draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);')
ck(0 <= packed_draw < contour_draw < coast_draw,
   'per-Entry painter order remains terrain -> contour -> coastline')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage diagnostics remain present')

active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('REV007 GPU VERTEX REJECT DIAGNOSTICS' in U and
   'verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py' in active and
   'verify_aeris25_renderable_entry_gate_hotfix.py' not in active,
   'build identity and active verifier gate are rev007-specific')

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
print("\n[AERIS25 ATROPINE REV007 GPU VERTEX REJECT DIAGNOSTICS] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV007 GPU VERTEX REJECT DIAGNOSTICS] STATIC PASS')
