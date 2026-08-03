#!/usr/bin/env python3
import ast
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True

root = Path(__file__).resolve().parents[1]
legacy_runner = root / "Tools/run_v01800_cp25_acceptance.py"
tree = ast.parse(legacy_runner.read_text())
legacy_tests = None
for node in tree.body:
    if isinstance(node, ast.Assign) and any(isinstance(t, ast.Name) and t.id == "tests" for t in node.targets):
        legacy_tests = ast.literal_eval(node.value)
        break
if legacy_tests is None:
    raise SystemExit("could not load CP2.5 regression list")

# Gate 4A replaces the historical CPU terrain texture/fallback shape and the
# Gate 3.1 identity-only boundary. Every unrelated frozen regression remains.
superseded_boundaries = {
    "selftest_v01800_cp25_final_closure_standard_preload_only.py",
    "selftest_v01800_cp25_integrated_acceptance_candidate1.py",
    "selftest_v01800_cp25_map_dram_cache_foundation_hotfix1.py",
    "selftest_v01800_cp25_land_separation_hotfix1.py",
    "selftest_v01800_cp2_supply_pipeline_hotfix3.py",
    "selftest_v01800_cp2_terrain_tiles.py",
    "selftest_v01800_cp2_render_hotfix2.py",
    "selftest_v01800_cp25_altitude_gate_hotfix1.py",
    "selftest_v01800_cp2_alignment_diagnostic_hotfix1.py",
    "selftest_v01800_cp2_runway_terrain_safety_hotfix1.py",
    "selftest_v01800_cp2_runway_map_lock_hotfix1.py",
    "selftest_v01800_cp1_nd_core.py",
}
tests = [
    "selftest_v01800_cp3_gate4a_compile_hotfix2.py",
    "selftest_v01800_cp3_gate4a_render_ready_gpu_only.py",
    "selftest_v01800_cp3_gate4a_predictive_corridor_successor.py",
    "selftest_v01800_cp3_gate3_land_separation_successor.py",
    "selftest_v01800_cp3_gate4a_supply_pipeline_successor.py",
    "selftest_v01800_cp3_gate4a_terrain_tiles_successor.py",
    "selftest_v01800_cp3_gate4a_alignment_diagnostic_successor.py",
    "selftest_v01800_cp3_gate4a_runway_terrain_safety_successor.py",
    "selftest_v01800_cp3_gate4a_runway_map_lock_successor.py",
    "selftest_v01800_cp3_gate4a_nd_core_successor.py",
    "selftest_v01800_cp2_csharp_compile_regression.py",
]
tests.extend(name for name in legacy_tests if name not in superseded_boundaries and
             name != "selftest_v01800_cp2_csharp_compile_regression.py")
if (root / "MANIFEST_SHA256.txt").is_file():
    tests.append("verify_v01800_cp2_manifest.py")

for name in tests:
    print("\n=== " + name + " ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print("\n[v0.18.0.0 CP3 Gate 4A Render-Ready GPU-Only FAR Compile Hotfix 2] " +
      str(len(tests)) + "/" + str(len(tests)) + " scripts PASS")
