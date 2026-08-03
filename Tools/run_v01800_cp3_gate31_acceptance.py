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

# Gate 3.1 supersedes historical absence-only and fixed-LOD-shape boundaries. The
# new dedicated test proves the actual rotated viewport, FAR authority and virtual
# detail split; frozen CP2.5 tests remain for every unrelated subsystem.
superseded_boundaries = {
    "selftest_v01800_cp25_final_closure_standard_preload_only.py",
    "selftest_v01800_cp25_integrated_acceptance_candidate1.py",
    "selftest_v01800_cp25_map_dram_cache_foundation_hotfix1.py",
    "selftest_v01800_cp25_land_separation_hotfix1.py",
    "selftest_v01800_cp2_supply_pipeline_hotfix3.py",
    "selftest_v01800_cp2_terrain_tiles.py",
}
tests = [
    "selftest_v01800_cp3_gate31_viewport_authoritative_far_base.py",
    "selftest_v01800_cp3_gate31_predictive_corridor_successor.py",
    "selftest_v01800_cp3_gate3_land_separation_successor.py",
    "selftest_v01800_cp3_gate31_supply_pipeline_successor.py",
    "selftest_v01800_cp3_gate3_terrain_tiles_successor.py",
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
print("\n[v0.18.0.0 CP3 Gate 3.1 Viewport-Authoritative FAR Base] " +
      str(len(tests)) + "/" + str(len(tests)) + " scripts PASS")
