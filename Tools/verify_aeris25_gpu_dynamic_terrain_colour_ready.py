#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
core = ROOT / "Tools/verify_aeris25_gpu_dynamic_terrain_colour.py"
if not core.is_file():
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR READY] core verifier missing")
subprocess.run([sys.executable, str(core)], cwd=str(ROOT), check=True)

R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
B = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
E = (ROOT / "GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs").read_text()
U = (ROOT / "build_ubuntu.sh").read_text()
S = (ROOT / "Tools/build_aeris25_gpu_shader_bundle.sh").read_text()
P = (ROOT / "Tools/prepare_aeris25_gpu_dynamic_terrain_colour_runtime.py").read_text()
M = (ROOT / "GpuAssets/Packages/manifest.json").read_text()
SH = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
MON = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
OBSERVER_WRAPPER_PATH = ROOT / "Tools/prepare_aeris26_rev003_observer_runtime_hotfix.py"
OW = OBSERVER_WRAPPER_PATH.read_text() if OBSERVER_WRAPPER_PATH.is_file() else ""
checks = []
def ck(v, name):
    ok = bool(v)
    checks.append((ok, name))
    print(("[PASS] " if ok else "[FAIL] ") + name)

ck('packedTerrainSource.LongLength * (3L * 4L)) +' in R,
   'AERIS-managed byte accounting includes independent TEXCOORD2 float3 semantics (+12 bytes/packed vertex)')
ck('PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py"' in U,
   'build invokes hardened AERIS25 verifier')
ck((ROOT / 'Tools/build_aeris25_gpu_shader_bundle.sh').is_file(),
   'AERIS25-specific shader bundle build path exists')
ck((ROOT / 'Tools/prepare_aeris25_gpu_dynamic_terrain_colour_runtime.py').is_file(),
   'AERIS25-specific runtime preparation path exists')
ck('aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle' in U and
   'aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle' in U,
   'build identity references only AERIS25 dynamic-colour runtime shader bundles')
ck('aeris25_gpu_dynamic_colour_probe_windows.bundle' in B and
   'aeris25_gpu_dynamic_colour_probe_linux.bundle' in B,
   'runtime backend uses AERIS25-specific compatibility probes')
ck('aeris25_gpu_dynamic_colour_probe_windows.bundle' in E and
   'aeris25_gpu_dynamic_colour_probe_linux.bundle' in E,
   'Unity builder emits AERIS25-specific compatibility probes')
ck('rm -rf "$PROJECT/Library" "$PROJECT/Temp" "$PROJECT/obj"' in S,
   'deliberate shader rebuild clears stale Unity import/build caches')
ck('6465e6dfa7c9809a734d5ce85b202b49ea6ee5fcaac19d55d4b75bd532a35f0d' in S and
   'Windows probe historical exact-SHA gate PASS' in S and
   'probe semantic validation PASS' in S and
   'historical SHA differs; semantic/reproducibility gate required' in S and
   'CANONICAL_PROBE_GUID' in S and
   'local shader clean-rebuild reproducibility=PASS' in OW,
   'Windows bundle generation preserves historical exact-SHA acceptance and adds reproducible semantic cross-machine acceptance')
ck('b"AssetBundle"' in P and 'EXPECTED_WINDOWS_PROBE_SHA' in P,
   'legacy AERIS25 runtime preparation still rejects malformed/unaccepted historical bundle containers')
ck((ROOT / 'Tools/apply_aeris25_assetbundle_compat_hotfix.py').is_file(),
   'AssetBundle compatibility hotfix is generated and repeatable')
ck('"dependencies": {}' in M,
   'GpuAssets project has no Package Manager dependencies')
ck('-noUpm' in S,
   'Unity batch AssetBundle generation disables Package Manager')
active_shader_build_lines = [line for line in S.splitlines()
                             if line.strip() and not line.lstrip().startswith('#')]
active_shader_build = '\n'.join(active_shader_build_lines)
ck('-logFile "$log_file"' in active_shader_build and '-logFile -' not in active_shader_build,
   'Unity/UPM logging is isolated from caller stdout to a real log file')
ck('ERR_STREAM_DESTROYED' in S,
   'builder documents Unity 2019 Package Manager destroyed-stream failure containment')
ck((ROOT / 'Tools/apply_aeris25_gpu_dynamic_colour_perf_hotfix.py').is_file() and
   (ROOT / 'Tools/verify_aeris25_gpu_dynamic_colour_perf_hotfix.py').is_file(),
   'ATROPINE rev002 performance hotfix is generated and independently verifiable')
ck((('internal const string Revision = "OH_PHASE4_002";' in MON) or
    ('internal const string Revision = "OH_PHASE4_003";' in MON) or
    ('internal const string Revision = "OH_PHASE4_004";' in MON) or
    ('internal const string Revision = "OH_PHASE4_005";' in MON) or
    ('internal const string Revision = "OH_PHASE4_006";' in MON) or
    ('internal const string Revision = "OH_PHASE4_007";' in MON)) and
   'AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH,
   'final READY tree carries rev002 mode split or approved rev003/rev004/rev005/rev006/rev007 descendant')
ck('oh_gpu_dynamic_vertex_submit=' in R and
   'oh_gpu_vertex_packed_mismatch=' in R and
   'oh_gpu_vertex_contour_mismatch=' in R and
   'oh_gpu_vertex_coast_mismatch=' in R,
   'rev002 runtime publishes GPU vertex pressure and fallback-cause telemetry')

failed = [name for ok, name in checks if not ok]
print("\n[AERIS25 GPU DYNAMIC TERRAIN COLOUR READY] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 GPU DYNAMIC TERRAIN COLOUR READY] STATIC PASS')
