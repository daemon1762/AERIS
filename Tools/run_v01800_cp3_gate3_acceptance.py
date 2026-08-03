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

# Gate 3 retains the Gate 2 payload route and supersedes the same historical
# absence-only CP2.5 boundaries. Gate 2's dedicated checkpoint test remains in
# the package as evidence but is replaced by the stricter Gate 3 contract test.
superseded_boundaries = {
    "selftest_v01800_cp25_final_closure_standard_preload_only.py",
    "selftest_v01800_cp25_integrated_acceptance_candidate1.py",
    "selftest_v01800_cp25_map_dram_cache_foundation_hotfix1.py",
    "selftest_v01800_cp25_land_separation_hotfix1.py",
    "selftest_v01800_cp2_supply_pipeline_hotfix3.py",
    "selftest_v01800_cp2_terrain_tiles.py",
}
tests = ["selftest_v01800_cp3_gate3_predictive_forward_corridor.py",
         "selftest_v01800_cp3_gate3_land_separation_successor.py",
         "selftest_v01800_cp3_gate3_supply_pipeline_successor.py",
         "selftest_v01800_cp3_gate3_terrain_tiles_successor.py"]
tests.extend(name for name in legacy_tests if name not in superseded_boundaries)
if (root / "MANIFEST_SHA256.txt").is_file():
    tests.append("verify_v01800_cp2_manifest.py")

for name in tests:
    print("\n=== " + name + " ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print("\n[v0.18.0.0 CP3 Gate 3 Predictive Forward Corridor] " +
      str(len(tests)) + "/" + str(len(tests)) + " scripts PASS")
