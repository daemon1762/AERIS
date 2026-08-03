#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ("CP3.75 Candidate1 pure ND rebase authority","selftest_v01800_cp375_candidate1_pure_nd_rebase.py"),
 ("CP2 C# definite-assignment compile regression","selftest_v01800_cp2_csharp_compile_regression.py"),
]
for label,name in suites:
    print("\n=== "+label+" ===",flush=True)
    r=subprocess.run([sys.executable,str(root/"Tools"/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print("\n[v0.18.0.0 CP3.75 Pure ND Rebase Candidate 1 PREBUILD] %d/%d suites PASS" % (len(suites),len(suites)))
print("[AERIS] PREBUILD COMPLETE: native xbuild and KSP runtime remain required.")
