#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
suites=[
    ('Inherited CP3.5 Gate 1 Candidate 2 regression',
     'run_v01800_cp35_gate1_candidate2_acceptance.py'),
    ('CP3.5 Gate 2 Parallel Projection / Compact Autopilot UI Candidate 1',
     'selftest_v01800_cp35_gate2_parallel_projection_compact_autopilot_ui_candidate1.py'),
    ('Package SHA-256 manifest verification', 'verify_v01708_manifest.py'),
]
for label,name in suites:
    print('\n=== '+label+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode:
        raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 2 Candidate 1] %d/%d acceptance suites PASS' %
      (len(suites),len(suites)))
