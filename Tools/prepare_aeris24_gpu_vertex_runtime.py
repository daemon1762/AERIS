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
OH_REVISION = "OH_PHASE3_001"


def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def run(args, env=None):
    print("[AERIS24 GPU VERTEX RUNTIME] $ " + " ".join(str(x) for x in args))
    subprocess.run([str(x) for x in args], cwd=str(ROOT), env=env, check=True)


parser = argparse.ArgumentParser(description="Prepare, build and install the AERIS24 GPU Vertex Projection runtime candidate.")
parser.add_argument("ksp_path", help="Kerbal Space Program installation root")
parser.add_argument("--rebuild-shader", action="store_true",
                    help="force rebuilding the platform shader AssetBundle even if it already exists")
parser.add_argument("--unity-editor", default=os.environ.get("UNITY_EDITOR", ""),
                    help="Unity 2019.4.18f1 Editor executable; also accepted through UNITY_EDITOR")
args = parser.parse_args()

ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] KSP path not found: " + str(ksp))

run([sys.executable, ROOT / "Tools/apply_aeris24_gpu_vertex_projection_ready.py"])

if (ksp / "KSP_x64_Data" / "Managed" / "Assembly-CSharp.dll").is_file():
    shader_mode = "windows"
    bundle_name = "aeris_nd_gpu_vertex_projection_windows.bundle"
elif ((ksp / "KSP_Data" / "Managed" / "Assembly-CSharp.dll").is_file() or
      (ksp / "KSP_x86_64_Data" / "Managed" / "Assembly-CSharp.dll").is_file()):
    shader_mode = "linux"
    bundle_name = "aeris_nd_gpu_vertex_projection_linux.bundle"
else:
    raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] could not identify KSP Unity player layout under: " + str(ksp))

bundle = ROOT / "GameData" / "AERISFlightControl" / "Shaders" / bundle_name
if args.rebuild_shader or not bundle.is_file():
    env = os.environ.copy()
    if args.unity_editor:
        env["UNITY_EDITOR"] = args.unity_editor
    run(["bash", ROOT / "Tools/build_aeris24_gpu_shader_bundle.sh", shader_mode], env=env)
else:
    print("[AERIS24 GPU VERTEX RUNTIME] using existing shader bundle: " + str(bundle))

if not bundle.is_file() or bundle.stat().st_size == 0:
    raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] shader bundle missing/empty after asset gate: " + str(bundle))
source_bundle_sha = sha256(bundle)
print("[AERIS24_GPU_VERTEX_BUNDLE] mode=%s; name=%s; sha256=%s" %
      (shader_mode, bundle_name, source_bundle_sha))

run(["bash", ROOT / "build_ubuntu.sh", ksp])

source_dll = ROOT / "GameData" / "AERISFlightControl" / "Plugins" / "AERISFlightControl.dll"
installed_root = ksp / "GameData" / "AERISFlightControl"
installed_dll = installed_root / "Plugins" / "AERISFlightControl.dll"
installed_bundle = installed_root / "Shaders" / bundle_name
identity = installed_root / "AERISCandidateBuildIdentity.txt"
installed_config = installed_root / "Config" / "AERISOperationHealth.cfg"
for path in (source_dll, installed_dll, installed_bundle, identity, installed_config):
    if not path.is_file():
        raise SystemExit("[AERIS24 GPU VERTEX RUNTIME] required installed identity artifact missing: " + str(path))

source_dll_sha = sha256(source_dll)
installed_dll_sha = sha256(installed_dll)
installed_bundle_sha = sha256(installed_bundle)
identity_text = identity.read_text(errors="replace")
config_text = installed_config.read_text(errors="replace")

checks = [
    (source_dll_sha == installed_dll_sha, "built/installed DLL SHA"),
    (source_bundle_sha == installed_bundle_sha, "source/installed shader bundle SHA"),
    (("candidate=" + CANDIDATE) in identity_text, "candidate identity"),
    (("gpu_shader_bundle=" + bundle_name) in identity_text, "shader bundle identity"),
    (("gpu_shader_bundle_sha256=" + source_bundle_sha) in identity_text,
     "shader bundle SHA identity"),
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
print("Next runtime gate: launch KSP and require [AERIS23_RUNTIME_CANDIDATE] candidate=" + CANDIDATE)
print("Then require [OH] codename=" + OH_CODENAME + "; revision=" + OH_REVISION + "; candidate=" + CANDIDATE)
print("Then require [AERIS24_GPU_VERTEX_PROJECTION] ACTIVE before interpreting GPU performance.")
