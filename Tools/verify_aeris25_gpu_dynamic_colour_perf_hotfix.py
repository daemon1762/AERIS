#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
S = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
checks = []

def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)

ck('internal const string Revision = "OH_PHASE4_002";' in M,
   'ATROPINE revision is OH_PHASE4_002')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in S,
   'dynamic-colour shader carries explicit mode-split marker')
ck('if (_AerisTerrainDisplayMode > 0.5)' in S and
   'relativeMode ?' not in S,
   'REL and TOPO colour functions are mutually exclusive at shader source level')
ck('AerisRelativeColour(_AerisAircraftAltitudeMeters - semantic.x, preset)' in S and
   'AerisTopographicColour(semantic.x, preset)' in S,
   'frozen REL and TOPO colour functions remain authoritative')
ck('clearance <= 30.0' in S and 'clearance <= 300.0' in S and
   'clearance <= 600.0' in S,
   'REL threshold values remain unchanged')
ck('(elevation + 500.0) / 12500.0' in S and 'AerisGradient' in S,
   'TOPO gradient mapping remains unchanged')
ck('shadeByte / 227.0' in S and '0.30' in S and '0.55' in S and
   '0.94' in S and '1.035' in S,
   'shade law remains unchanged')
ck('oh_gpu_vertex_packed_mismatch=' in R and
   'oh_gpu_vertex_contour_mismatch=' in R and
   'oh_gpu_vertex_coast_mismatch=' in R,
   'GPU geographic attribute fallback is split by layer')
ck('ref operationHealthGpuVertexPackedMismatch' in R and
   'ref operationHealthGpuVertexContourMismatch' in R and
   'ref operationHealthGpuVertexCoastlineMismatch' in R,
   'layer mismatch counters are attached to the existing immutable-attribute gates')
ck('oh_gpu_dynamic_vertex_submit=' in R and
   'operationHealthGpuDynamicVerticesSubmitted +=' in R,
   'GPU dynamic-colour submitted vertex pressure is directly observable')
ck('SetUVs(2, gpuDynamicTerrainSemanticScratch)' in R and
   'oh_gpu_dynamic_semantic_fail=' in R,
   'one-time semantic upload and fail-closed telemetry remain intact')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render-target authority remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage diagnostics remain present')

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
print("\n[AERIS25 ATROPINE REV002 PERFORMANCE] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV002 PERFORMANCE] STATIC PASS')
