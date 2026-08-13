#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OLD_OH = "PENI" + "CILLIN"
NEW_OH = "EPI" + "NEPHRINE"

# The branch packages the new OH codename, but parent reconstruction is deliberately
# verified under its original identity before the Phase-3 promotion step runs.
config = ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg"
config_text = config.read_text()
old_line = "    codename = " + OLD_OH
new_line = "    codename = " + NEW_OH
if new_line in config_text and old_line not in config_text:
    config.write_text(config_text.replace(new_line, old_line, 1))
elif old_line not in config_text:
    raise SystemExit("[AERIS24 OH PHASE3] parent config identity anchor missing")

steps = [
    ("align final parent/Stagger anchors",
     ROOT / "Tools/fix_aeris24_gpu_vertex_projection_poc_anchors.py"),
    ("apply AERIS24 GPU Vertex Projection PoC",
     ROOT / "Tools/apply_aeris24_gpu_vertex_projection_poc.py"),
    ("upgrade inherited Single-Authority Pass3 assertion",
     ROOT / "Tools/fix_aeris24_gpu_vertex_single_authority_selftest.py"),
    ("verify AERIS24 GPU Vertex source/safety/math",
     ROOT / "Tools/verify_aeris24_gpu_vertex_projection_poc.py"),
    ("promote Operation Health Phase 3 identity",
     ROOT / "Tools/promote_aeris24_oh_phase3.py"),
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
print("codename=" + NEW_OH)
print("revision=OH_PHASE3_001")
print("candidate=AERIS24_GPU_VERTEX_PROJECTION_POC")
print("Inherited Operation Health Pass3 prebuild must report 20/20 PASS above.")
print("Next asset gate: Tools/build_aeris24_gpu_shader_bundle.sh windows")
print("Then build/install with ./build_ubuntu.sh <KSP_PATH> and require MATCH=YES.")
