#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Gate 3 Candidate 3','selftest_v01800_cp35_gate3_sparse_hires_palettev2_archive_retention_candidate3.py'),
 ('Candidate 3 build entrypoint','selftest_v01800_cp35_gate3_candidate3_build_entrypoint.py'),
 ('CP2 C# definite-assignment compile regression','selftest_v01800_cp2_csharp_compile_regression.py'),
 ('Source SHA-256 manifest','verify_cp35_gate3_candidate3_source_manifest.py'),
 ('Package SHA-256 manifest','verify_cp35_gate3_candidate3_package_manifest.py'),
]
for label,name in suites:
 print('\n=== '+label+' ===',flush=True)
 r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
 if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 3 Candidate 3 FULL ACCEPTANCE] %d/%d suites PASS' % (len(suites),len(suites)))
