#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Gate 4 terrain quality architecture','selftest_v01800_cp35_gate4_terrain_quality_architecture_candidate1.py'),
 ('CP2 C# definite-assignment compile regression','selftest_v01800_cp2_csharp_compile_regression.py'),
]
for label,name in suites:
 print('\n=== '+label+' ===',flush=True)
 r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
 if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 4 Terrain Quality Architecture Candidate 1 PREBUILD] %d/%d lightweight suites PASS' % (len(suites),len(suites)))
print('[AERIS] PREBUILD COMPLETE: native xbuild and KSP runtime remain required.')
