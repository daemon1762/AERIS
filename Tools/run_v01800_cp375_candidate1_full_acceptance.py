#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ("Candidate1 prebuild","run_v01800_cp375_candidate1_prebuild.py"),
 ("Candidate1 source manifest","verify_cp375_candidate1_source_manifest.py"),
 ("Candidate1 package manifest","verify_cp375_candidate1_package_manifest.py"),
]
for label,name in suites:
    print("\n=== "+label+" ===",flush=True)
    r=subprocess.run([sys.executable,str(root/"Tools"/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print("\n[v0.18.0.0 CP3.75 Pure ND Rebase Candidate 1 FULL STATIC ACCEPTANCE] %d/%d suites PASS" % (len(suites),len(suites)))
print("[AERIS] STATIC ACCEPTANCE COMPLETE: native xbuild and KSP runtime remain required.")
