#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Candidate5 prebuild','run_v01800_cp375_candidate5_prebuild.py'),
 ('Candidate5 source manifest','verify_cp375_candidate5_source_manifest.py'),
 ('Candidate5 package manifest','verify_cp375_candidate5_package_manifest.py'),
]
for label,name in suites:
    print('\n=== '+label+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.75 Candidate5 FULL STATIC ACCEPTANCE] %d/%d suites PASS' % (len(suites),len(suites)))
