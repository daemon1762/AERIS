#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = "AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR"
OH_CODENAME = "ATRO" + "PINE"
OH_REVISION = "OH_PHASE4_001"


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def run(args, env=None):
    print("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] $ " + " ".join(str(x) for x in args))
    subprocess.run([str(x) for x in args], cwd=str(ROOT), env=env, check=True)


def static_candidate_ready():
    try:
        monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
        config = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()
        build = (ROOT / "build_ubuntu.sh").read_text()
        renderer = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
        backend = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
        shader = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
    except Exception:
        return False
    return all((
        ('internal const string Codename = "' + OH_CODENAME + '";') in monitor,
        ('internal const string Revision = "' + OH_REVISION + '";') in monitor,
        ('internal const string Candidate = "' + CANDIDATE + '";') in monitor,
        ('codename = ' + OH_CODENAME) in config,
        ('CANDIDATE_NAME="' + CANDIDATE + '"') in build,
        'oh_gpu_dynamic_colour=' in renderer,
        'GpuDynamicColourAttributesReady' in renderer,
        'SetUVs(2, gpuDynamicTerrainSemanticScratch)' in renderer,
        'packedTerrainSource.LongLength * (3L * 4L)) +' in renderer,
        '_AerisTerrainSemanticMode' in backend,
        '_AerisTerrainSemanticMode' in shader,
        'AerisRelativeColour' in shader,
        'AerisTopographicColour' in shader,
        'verify_aeris25_gpu_dynamic_terrain_colour_ready.py' in build,
    ))


parser = argparse.ArgumentParser(
    description="Prepare, build and install AERIS25 GPU Dynamic Terrain Colour.")
parser.add_argument("ksp_path", help="Kerbal Space Program installation root")
parser.add_argument("--rebuild-shader", action="store_true",
                    help="force rebuilding the platform AERIS25 shader bundle")
parser.add_argument("--unity-editor", default=os.environ.get("UNITY_EDITOR", ""),
                    help="Unity 2019.4.18f1 Editor executable; also accepted through UNITY_EDITOR")
args = parser.parse_args()

ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] KSP path not found: " + str(ksp))

if not static_candidate_ready():
    run([sys.executable, ROOT / "Tools/apply_aeris25_gpu_dynamic_terrain_colour_ready.py"])
run([sys.executable, ROOT / "Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py"])
run([sys.executable, ROOT / "Tools/run_v01800_operation_health_pass3_prebuild.py"])
run(["git", "diff", "--check"])

if (ksp / "KSP_x64_Data" / "Managed" / "Assembly-CSharp.dll").is_file():
    shader_mode = "windows"
    bundle_name = "aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
    probe_name = "aeris_gpu_bundle_probe_windows.bundle"
elif ((ksp / "KSP_Data" / "Managed" / "Assembly-CSharp.dll").is_file() or
      (ksp / "KSP_x86_64_Data" / "Managed" / "Assembly-CSharp.dll").is_file()):
    shader_mode = "linux"
    bundle_name = "aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
    probe_name = "aeris_gpu_bundle_probe_linux.bundle"
else:
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] could not identify KSP Unity player layout under: " + str(ksp))

shader_dir = ROOT / "GameData" / "AERISFlightControl" / "Shaders"
bundle = shader_dir / bundle_name
probe = shader_dir / probe_name
if args.rebuild_shader or not bundle.is_file() or not probe.is_file():
    env = os.environ.copy()
    if args.unity_editor:
        env["UNITY_EDITOR"] = args.unity_editor
    run(["bash", ROOT / "Tools/build_aeris25_gpu_shader_bundle.sh", shader_mode], env=env)
else:
    print("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] using existing AERIS25 shader bundle: " + str(bundle))

for path, label in ((bundle, "shader"), (probe, "probe")):
    if not path.is_file() or path.stat().st_size == 0:
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] %s bundle missing/empty: %s" %
                         (label, path))
source_bundle_sha = sha256(bundle)
source_probe_sha = sha256(probe)
print("[AERIS25_GPU_DYNAMIC_COLOUR_BUNDLE] mode=%s; name=%s; sha256=%s" %
      (shader_mode, bundle_name, source_bundle_sha))

run(["bash", ROOT / "build_ubuntu.sh", ksp])

source_dll = ROOT / "GameData" / "AERISFlightControl" / "Plugins" / "AERISFlightControl.dll"
installed_root = ksp / "GameData" / "AERISFlightControl"
installed_dll = installed_root / "Plugins" / "AERISFlightControl.dll"
installed_bundle = installed_root / "Shaders" / bundle_name
installed_probe = installed_root / "Shaders" / probe_name
identity = installed_root / "AERISCandidateBuildIdentity.txt"
installed_config = installed_root / "Config" / "AERISOperationHealth.cfg"
for path in (source_dll, installed_dll, installed_bundle, installed_probe,
             identity, installed_config):
    if not path.is_file():
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] required installed artifact missing: " + str(path))

source_dll_sha = sha256(source_dll)
installed_dll_sha = sha256(installed_dll)
installed_bundle_sha = sha256(installed_bundle)
installed_probe_sha = sha256(installed_probe)
identity_text = identity.read_text(errors="replace")
config_text = installed_config.read_text(errors="replace")
checks = [
    (source_dll_sha == installed_dll_sha, "built/installed DLL SHA"),
    (source_bundle_sha == installed_bundle_sha, "source/installed AERIS25 shader bundle SHA"),
    (source_probe_sha == installed_probe_sha, "source/installed probe SHA"),
    (("candidate=" + CANDIDATE) in identity_text, "AERIS25 candidate identity"),
    (("gpu_shader_bundle=" + bundle_name) in identity_text, "AERIS25 shader bundle identity"),
    (("gpu_shader_bundle_sha256=" + source_bundle_sha) in identity_text, "AERIS25 shader bundle SHA identity"),
    (("codename = " + OH_CODENAME) in config_text, "Operation Health ATROPINE config identity"),
]
failed = []
for ok, label in checks:
    print(("[PASS] " if ok else "[FAIL] ") + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] INSTALL IDENTITY FAIL: " + ", ".join(failed))

print("[AERIS25 GPU DYNAMIC COLOUR RUNTIME] INSTALL IDENTITY MATCH=YES")
print("candidate=" + CANDIDATE)
print("oh_codename=" + OH_CODENAME)
print("oh_revision=" + OH_REVISION)
print("dll_sha256=" + installed_dll_sha)
print("gpu_shader_bundle=" + bundle_name)
print("gpu_shader_bundle_sha256=" + installed_bundle_sha)
print("gpu_probe_bundle=" + probe_name)
print("gpu_probe_bundle_sha256=" + installed_probe_sha)
print("Runtime gate: verify oh_gpu_dynamic_colour=ACTIVE, then TOPO/REL and all palettes while flying.")
print("Semantic upload should converge; oh_gpu_dynamic_cpu_colour_bypass must rise without repeated mesh colour uploads.")
print("Golden visualCoverage=1.000, Runway Map Lock, 10 Hz authority and warm OFF/ON behavior must remain unchanged.")
