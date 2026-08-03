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

# The historical CP2.5 final-closure test intentionally asserts that CP3 does not
# exist. Replace only that boundary test; all other accepted regression scripts run.
tests = ["selftest_v01800_cp3_gate1_scheduler_state_compile_hotfix1.py",
         "selftest_v01800_cp3_gate1_current_body_resident_cache_contracts.py"]
tests.extend(name for name in legacy_tests
             if name != "selftest_v01800_cp25_final_closure_standard_preload_only.py")
if (root / "MANIFEST_SHA256.txt").is_file():
    tests.append("verify_v01800_cp2_manifest.py")

for name in tests:
    print("\n=== " + name + " ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print("\n[v0.18.0.0 CP3 Gate 1 Current Body Resident Cache Contracts Compile Hotfix 1] " +
      str(len(tests)) + "/" + str(len(tests)) + " scripts PASS")
