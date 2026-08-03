#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Gate 4 Candidate 2 prebuild','run_v01800_cp35_gate4_cp3_golden_cartographic_quality_candidate2_prebuild.py'),
 ('Gate 4 Candidate 2 source manifest','verify_cp35_gate4_candidate2_source_manifest.py'),
 ('Gate 4 Candidate 2 package manifest','verify_cp35_gate4_candidate2_package_manifest.py'),
]
for label,name in suites:
 print('\n=== '+label+' ===',flush=True)
 r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
 if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 4 CP3 Golden Cartographic Quality Candidate 2 FULL STATIC ACCEPTANCE] %d/%d suites PASS' % (len(suites),len(suites)))
print('[AERIS] STATIC ACCEPTANCE COMPLETE: native xbuild and KSP runtime remain required.')
