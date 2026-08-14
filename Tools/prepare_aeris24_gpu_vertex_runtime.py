#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = "AERIS24_GPU_VERTEX_PROJECTION_POC"
OH_CODENAME = "EPI" + "NEPHRINE"
OH_REVISION = "OH_PHASE3_006"


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def run(args, env=None):
    print("[AERIS24 GPU VERTEX RUNTIME] $ " + " ".join(str(x) for x in args))
    subprocess.run([str(x) for x in args], cwd=str(ROOT), env=env, check=True)


def static_candidate_ready():
    try:
        monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
        config = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()
        build = (ROOT / "build_ubuntu.sh").read_text()
        renderer = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
        backend = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
        settings = (ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs").read_text()
        ui = (ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs").read_text()
        window = (ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs").read_text()
        project = (ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj").read_text()
    except Exception:
        return False
    markers = (
        'internal const string Codename = "' + OH_CODENAME + '";' in monitor,
        'internal const string Revision = "' + OH_REVISION + '";' in monitor,
        'internal const string Candidate = "' + CANDIDATE + '";' in monitor,
        ('codename = ' + OH_CODENAME) in config,
        ('CANDIDATE_NAME="' + CANDIDATE + '"') in build,
        'OPERATION HEALTH PHASE 3 ' + OH_CODENAME + ' GPU VERTEX PROJECTION' in build,
        'oh_gpu_vertex_requested=' in renderer,
        'oh_gpu_vertex_exact_bypass=' in renderer,
        'oh_gpu_vertex_resident_suspend=' in renderer,
        'oh_nd_reload_pct=' in renderer,
        'oh_nd_reload_snapshot=' in renderer,
        'reloadSnapshotCenterLatitudeDeg' in renderer,
        'CPU_EXACT_REQUESTED' in backend,
        'RetainForViewportSuspension' in backend,
        'AERISNdProjectionBackendMode' in settings,
        'TerrainGpuMode = AERISTerrainGpuMode.On' in settings,
        'RELOADING ND' in ui,
        'DrawProjectionBackendSelector' in window,
        'GUILayout.HorizontalSlider' in window,
        'Terrain\\AERISNdGpuVertexProjectionBackend.cs' in project,
    )
    return all(markers)


def verify_static_candidate():
    print("[AERIS24 GPU VERTEX RUNTIME] existing EPINEPHRINE static candidate detected; parent reconstruction skipped")
    run([sys.executable, ROOT / "Tools/verify_aeris24_gpu_vertex_projection_poc.py"])
    run([sys.executable, ROOT / "Tools/verify_aeris24_nd_backend_reload.py"])
    run([sys.executable, ROOT / "Tools/verify_aeris24_nd_reload_snapshot_hotfix.py"])
    run([sys.executable, ROOT / "Tools/verify_aeris24_system_options_residency_hotfix.py"])
    run([sys.executable, ROOT / "Tools/verify_aeris24_oh_phase3.py"])
    run([sys.executable, ROOT / "Tools/run_v01800_operation_health_pass3_prebuild.py"])
    run(["git", "diff", "--check"])
    print("[AERIS24 GPU VERTEX RUNTIME] restart-safe static revalidation PASS")


parser = argparse.ArgumentParser(description="Prepare, build and install the AERIS24 GPU Vertex Projection runtime candidate.")
parser.add_argument("ksp_path", help="Kerbal Space Program installation root")
parser.add_argument("--rebuild-shader", action="store_true",
                    help="force rebuilding the platform shader/probe AssetBundles even if they already exist")
parser.add_argument("--unity-editor", default=os.environ.get("UNITY_EDITOR", ""),
                    help="Unity 2019.4.18f1 Editor executable; also accepted through UNITY_EDITOR")
args = parser.parse_args()

ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] KSP path not found: " + str(ksp))

if static_candidate_ready():
    verify_static_candidate()
else:
    run([sys.executable, ROOT / "Tools/apply_aeris24_gpu_vertex_projection_ready.py"])

if (ksp / "KSP_x64_Data" / "Managed" / "Assembly-CSharp.dll").is_file():
    shader_mode = "windows"
    bundle_name = "aeris_nd_gpu_vertex_projection_windows.bundle"
    probe_name = "aeris_gpu_bundle_probe_windows.bundle"
elif ((ksp / "KSP_Data" / "Managed" / "Assembly-CSharp.dll").is_file() or
      (ksp / "KSP_x86_64_Data" / "Managed" / "Assembly-CSharp.dll").is_file()):
    shader_mode = "linux"
    bundle_name = "aeris_nd_gpu_vertex_projection_linux.bundle"
    probe_name = "aeris_gpu_bundle_probe_linux.bundle"
else:
    raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] could not identify KSP Unity player layout under: " + str(ksp))

shader_dir = ROOT / "GameData" / "AERISFlightControl" / "Shaders"
bundle = shader_dir / bundle_name
probe = shader_dir / probe_name
if args.rebuild_shader or not bundle.is_file() or not probe.is_file():
    env = os.environ.copy()
    if args.unity_editor:
        env["UNITY_EDITOR"] = args.unity_editor
    run(["bash", ROOT / "Tools/build_aeris24_gpu_shader_bundle.sh", shader_mode], env=env)
else:
    print("[AERIS24 GPU VERTEX RUNTIME] using existing shader bundle: " + str(bundle))
    print("[AERIS24 GPU VERTEX RUNTIME] using existing probe bundle: " + str(probe))

for path, label in ((bundle, "shader"), (probe, "probe")):
    if not path.is_file() or path.stat().st_size == 0:
        raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] %s bundle missing/empty after asset gate: %s" %
                         (label, path))
source_bundle_sha = sha256(bundle)
source_probe_sha = sha256(probe)
print("[AERIS24_GPU_VERTEX_BUNDLE] mode=%s; name=%s; sha256=%s" %
      (shader_mode, bundle_name, source_bundle_sha))
print("[AERIS24_GPU_BUNDLE_PROBE] mode=%s; name=%s; sha256=%s" %
      (shader_mode, probe_name, source_probe_sha))

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
        raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] required installed identity artifact missing: " + str(path))

source_dll_sha = sha256(source_dll)
installed_dll_sha = sha256(installed_dll)
installed_bundle_sha = sha256(installed_bundle)
installed_probe_sha = sha256(installed_probe)
identity_text = identity.read_text(errors="replace")
config_text = installed_config.read_text(errors="replace")
checks = [
    (source_dll_sha == installed_dll_sha, "built/installed DLL SHA"),
    (source_bundle_sha == installed_bundle_sha, "source/installed shader bundle SHA"),
    (source_probe_sha == installed_probe_sha, "source/installed probe bundle SHA"),
    (("candidate=" + CANDIDATE) in identity_text, "candidate identity"),
    (("gpu_shader_bundle=" + bundle_name) in identity_text, "shader bundle identity"),
    (("gpu_shader_bundle_sha256=" + source_bundle_sha) in identity_text, "shader bundle SHA identity"),
    (("codename = " + OH_CODENAME) in config_text, "Operation Health Phase 3 config identity"),
]
failed = []
for ok, label in checks:
    print(("[PASS] " if ok else "[FAIL] ") + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] INSTALL IDENTITY FAIL: " + ", ".join(failed))

print("[AERIS24 GPU VERTEX RUNTIME] INSTALL IDENTITY MATCH=YES")
print("candidate=" + CANDIDATE)
print("oh_codename=" + OH_CODENAME)
print("oh_revision=" + OH_REVISION)
print("dll_sha256=" + installed_dll_sha)
print("gpu_shader_bundle=" + bundle_name)
print("gpu_shader_bundle_sha256=" + installed_bundle_sha)
print("gpu_probe_bundle=" + probe_name)
print("gpu_probe_bundle_sha256=" + installed_probe_sha)
print("Next runtime gate: after GPU ACTIVE, toggle SYSTEM ND display OFF/ALWAYS and verify activation count does not rise.")
print("Also verify SYSTEM projection selector sync, FDR 1..30 slider, fixed Terrain GPU ON, and one high-speed 160 km segment.")
