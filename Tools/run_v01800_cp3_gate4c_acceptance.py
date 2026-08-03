#!/usr/bin/env python3
from pathlib import Path
import subprocess, sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
tests=[
 'selftest_v01800_cp3_gate4c_virtual_detail_exact_microtile_runway_labels.py',
 'selftest_v01800_cp2_csharp_compile_regression.py',
 'selftest_v01800_cp3_gate4a_supply_pipeline_successor.py',
 'selftest_v01800_cp3_gate4a_runway_terrain_safety_successor.py',
 'selftest_v01800_cp3_gate4a_runway_map_lock_successor.py',
 'selftest_v01800_cp3_gate4a_nd_core_successor.py',
 'selftest_v01800_cp3_gate4a_alignment_diagnostic_successor.py',
 'selftest_v01800_cp2_nd_publish_airfields_ui_hotfix1.py',
 'selftest_v01800_cp2_responsive_airfields_ui_resize_hotfix1.py',
]
for name in tests:
    print('\n=== '+name+' ===', flush=True)
    r=subprocess.run([sys.executable, str(root/'Tools'/name)], cwd=str(root))
    if r.returncode:
        raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3 Gate 4C Virtual Detail / Exact Microtile / Runway End Labels] %d/%d scripts PASS' % (len(tests),len(tests)))
