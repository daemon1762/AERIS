#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Authority/Palette V3 Hotfix','selftest_v01800_cp35_gate3_candidate3_ownship_prediction_range_palettev3_hotfix1.py'),
 ('CP2 C# compile regression','selftest_v01800_cp2_csharp_compile_regression.py'),
 ('Source manifest','verify_cp35_gate3_candidate3_ownship_prediction_range_palettev3_hotfix1_source_manifest.py'),
 ('Package manifest','verify_cp35_gate3_candidate3_ownship_prediction_range_palettev3_hotfix1_package_manifest.py'),
]
for label,name in suites:
 print('\n=== '+label+' ===',flush=True)
 result=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
 if result.returncode: raise SystemExit(result.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 3 Candidate 3 Authority/Palette V3 Hotfix 1 ACCEPTANCE] %d/%d suites PASS' % (len(suites),len(suites)))
print('[AERIS] STATIC ACCEPTANCE ONLY: native xbuild and KSP runtime are still required.')
