#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True

root = Path(__file__).resolve().parents[1]
phases = [
    "selftest_v01800_cp3_gate31_foundation_ui_compile_hotfix1.py",
    "run_v01800_cp3_gate31_acceptance.py",
]
for name in phases:
    print("\n=== " + name + " ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print("\n[v0.18.0.0 CP3 Gate 3.1 Foundation UI Compile Hotfix 1] " +
      str(len(phases)) + "/" + str(len(phases)) + " acceptance phases PASS")
