#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Candidate4 prebuild','run_v01800_cp375_candidate4_prebuild.py'),
 ('Candidate4 source manifest','verify_cp375_candidate4_source_manifest.py'),
 ('Candidate4 package manifest','verify_cp375_candidate4_package_manifest.py'),
]
for label,name in suites:
    print('\n=== '+label+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.75 Candidate4 FULL STATIC ACCEPTANCE] %d/%d suites PASS' % (len(suites),len(suites)))
