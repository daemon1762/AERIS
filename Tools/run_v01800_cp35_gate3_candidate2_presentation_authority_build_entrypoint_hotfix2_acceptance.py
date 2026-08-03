#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
 ('Inherited Gate 3 Candidate 2 frozen visual recovery','selftest_v01800_cp35_gate3_cp3_frozen_visual_path_recovery_candidate2.py'),
 ('Presentation Authority Hotfix 1','selftest_v01800_cp35_gate3_candidate2_presentation_authority_hotfix1.py'),
 ('Build Entrypoint Hotfix 2','selftest_v01800_cp35_gate3_candidate2_build_entrypoint_hotfix2.py'),
 ('CP2 C# definite-assignment compile regression','selftest_v01800_cp2_csharp_compile_regression.py'),
 ('Source SHA-256 manifest verification','verify_cp35_gate3_candidate2_build_entrypoint_hotfix2_source_manifest.py'),
 ('Package SHA-256 manifest verification','verify_cp35_gate3_candidate2_build_entrypoint_hotfix2_package_manifest.py'),
]
for label,name in suites:
    print('\n=== '+label+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 3 Candidate 2 Presentation Authority Hotfix 1 Build Entrypoint Hotfix 2] %d/%d acceptance suites PASS' % (len(suites),len(suites)))
