#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

steps = [
    ("align final PENICILLIN/Stagger anchors",
     ROOT / "Tools/fix_aeris24_gpu_vertex_projection_poc_anchors.py"),
    ("apply AERIS24 GPU Vertex Projection PoC",
     ROOT / "Tools/apply_aeris24_gpu_vertex_projection_poc.py"),
    ("upgrade inherited Single-Authority Pass3 assertion",
     ROOT / "Tools/fix_aeris24_gpu_vertex_single_authority_selftest.py"),
    ("verify AERIS24 GPU Vertex source/safety/math",
     ROOT / "Tools/verify_aeris24_gpu_vertex_projection_poc.py"),
    ("run inherited Operation Health Pass3 prebuild suite",
     ROOT / "Tools/run_v01800_operation_health_pass3_prebuild.py"),
]

for label, script in steps:
    if not script.is_file():
        raise SystemExit("[AERIS24 GPU VERTEX READY] missing step: %s (%s)" %
                         (label, script))
    print("\n[AERIS24 GPU VERTEX READY] " + label)
    subprocess.run([sys.executable, str(script)], cwd=str(ROOT), check=True)

subprocess.run(["git", "diff", "--check"], cwd=str(ROOT), check=True)

print("\n[AERIS24 GPU VERTEX READY] STATIC CANDIDATE PASS")
print("candidate=AERIS24_GPU_VERTEX_PROJECTION_POC")
print("Inherited Operation Health Pass3 prebuild must report 20/20 PASS above.")
print("Next asset gate: Tools/build_aeris24_gpu_shader_bundle.sh windows")
print("Then build/install with ./build_ubuntu.sh <KSP_PATH> and require MATCH=YES.")
