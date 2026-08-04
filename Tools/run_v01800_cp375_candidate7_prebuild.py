#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
suites=[
 ('CP3.75 Candidate7 coastal-boundary authority','selftest_v01800_cp375_candidate7_high_density_coastal_boundary_authority.py'),
 ('CP2 C# compile regression','selftest_v01800_cp2_csharp_compile_regression.py'),
]
for label,name in suites:
    print('\n=== '+label+' ===')
    r=subprocess.run([sys.executable,str(ROOT/'Tools'/name)],cwd=str(ROOT))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.75 Candidate7 PREBUILD] %d/%d suites PASS' % (len(suites),len(suites)))
