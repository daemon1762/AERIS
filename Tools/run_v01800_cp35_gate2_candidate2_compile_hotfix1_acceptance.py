#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
    ('Inherited CP3.5 Gate 2 Candidate 2 regression',
     'selftest_v01800_cp35_gate2_exact_keyframe_overscan_temporal_multicore_gpu_candidate2.py'),
    ('CP3.5 Gate 2 Candidate 2 Compile Hotfix 1',
     'selftest_v01800_cp35_gate2_candidate2_compile_hotfix1.py'),
    ('CP2 C# definite-assignment compile regression',
     'selftest_v01800_cp2_csharp_compile_regression.py'),
    ('CP3 Gate 4A terrain supply successor regression',
     'selftest_v01800_cp3_gate4a_supply_pipeline_successor.py'),
    ('Source SHA-256 manifest verification','verify_cp35_gate2_candidate2_source_manifest.py'),
    ('Package SHA-256 manifest verification','verify_v01708_manifest.py'),
]
for label,name in suites:
    print('\n=== '+label+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 2 Candidate 2 Compile Hotfix 1] %d/%d acceptance suites PASS' % (len(suites),len(suites)))
