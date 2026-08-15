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
S_ACTIVE = [line.strip() for line in S.splitlines()
            if line.strip() and not line.lstrip().startswith('#')]
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
   'Windows probe compatibility gate PASS' in S,
   'Windows bundle generation is gated by the accepted 1203-byte probe SHA')
ck('b"AssetBundle"' in P and 'EXPECTED_WINDOWS_PROBE_SHA' in P,
   'runtime preparation rejects malformed/unaccepted bundle containers before install')
ck((ROOT / 'Tools/apply_aeris25_assetbundle_compat_hotfix.py').is_file(),
   'AssetBundle compatibility hotfix is generated and repeatable')
ck('"dependencies": {}' in M,
   'GpuAssets project has no Package Manager dependencies')
ck(any('-noUpm' in line for line in S_ACTIVE),
   'Unity batch AssetBundle generation disables Package Manager')
ck(any(line == '-logFile "$log_file"' for line in S_ACTIVE) and
   not any(line == '-logFile -' for line in S_ACTIVE),
   'Unity/UPM logging is isolated from caller stdout to a real log file')
ck('ERR_STREAM_DESTROYED' in S,
   'builder documents Unity 2019 Package Manager destroyed-stream failure containment')

failed = [name for ok, name in checks if not ok]
print("\n[AERIS25 GPU DYNAMIC TERRAIN COLOUR READY] %d/%d PASS" %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 GPU DYNAMIC TERRAIN COLOUR READY] STATIC PASS')
