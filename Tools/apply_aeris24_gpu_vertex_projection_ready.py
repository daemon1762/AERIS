#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OLD_OH = "PENI" + "CILLIN"
NEW_OH = "EPI" + "NEPHRINE"
CANDIDATE = "AERIS24_GPU_VERTEX_PROJECTION_POC"
REVISION = "OH_PHASE3_003"


def run_step(label, script):
    if not script.is_file():
        raise SystemExit("[AERIS24 GPU VERTEX READY] missing step: %s (%s)" %
                         (label, script))
    print("\n[AERIS24 GPU VERTEX READY] " + label)
    subprocess.run([sys.executable, str(script)], cwd=str(ROOT), check=True)


def generated_phase3_ready():
    """Return True only for the fully generated EPINEPHRINE successor tree."""
    try:
        monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
        build = (ROOT / "build_ubuntu.sh").read_text()
        renderer = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
        config = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()
    except OSError:
        return False
    return (
        ('internal const string Codename = "' + NEW_OH + '";') in monitor and
        ('internal const string Revision = "' + REVISION + '";') in monitor and
        ('internal const string Candidate = "' + CANDIDATE + '";') in monitor and
        ('CANDIDATE_NAME="' + CANDIDATE + '"') in build and
        'verify_aeris24_gpu_vertex_projection_poc.py' in build and
        ('codename = ' + NEW_OH) in config and
        'oh_gpu_vertex_projection=' in renderer and
        'operationHealthGpuVertexExactBypasses' in renderer
    )


if generated_phase3_ready():
    print("[AERIS24 GPU VERTEX READY] generated EPINEPHRINE tree already present")
    print("[AERIS24 GPU VERTEX READY] re-entry mode: reconstruction skipped; full verification retained")
    run_step("revalidate AERIS24 GPU Vertex source/safety/math",
             ROOT / "Tools/verify_aeris24_gpu_vertex_projection_poc.py")
    run_step("revalidate Operation Health Phase 3 identity",
             ROOT / "Tools/verify_aeris24_oh_phase3.py")
    run_step("rerun inherited Operation Health Pass3 prebuild suite",
             ROOT / "Tools/run_v01800_operation_health_pass3_prebuild.py")
    subprocess.run(["git", "diff", "--check"], cwd=str(ROOT), check=True)
    print("\n[AERIS24 GPU VERTEX READY] STATIC CANDIDATE PASS (IDEMPOTENT RE-ENTRY)")
    print("codename=" + NEW_OH)
    print("revision=" + REVISION)
    print("candidate=" + CANDIDATE)
    print("Next asset gate: Tools/build_aeris24_gpu_shader_bundle.sh windows")
    print("Then build/install with ./build_ubuntu.sh <KSP_PATH> and require MATCH=YES.")
    raise SystemExit(0)

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
    ("verify Operation Health Phase 3 identity",
     ROOT / "Tools/verify_aeris24_oh_phase3.py"),
    ("run inherited Operation Health Pass3 prebuild suite",
     ROOT / "Tools/run_v01800_operation_health_pass3_prebuild.py"),
]

for label, script in steps:
    run_step(label, script)

subprocess.run(["git", "diff", "--check"], cwd=str(ROOT), check=True)

print("\n[AERIS24 GPU VERTEX READY] STATIC CANDIDATE PASS")
print("codename=" + NEW_OH)
print("revision=" + REVISION)
print("candidate=" + CANDIDATE)
print("Inherited Operation Health Pass3 prebuild must report 20/20 PASS above.")
print("Next asset gate: Tools/build_aeris24_gpu_shader_bundle.sh windows")
print("Then build/install with ./build_ubuntu.sh <KSP_PATH> and require MATCH=YES.")
