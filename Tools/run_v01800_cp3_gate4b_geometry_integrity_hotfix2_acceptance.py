#!/usr/bin/env python3
from pathlib import Path
import subprocess, sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
tests=[
 'selftest_v01800_cp3_gate4b_geometry_integrity_neutral_selection_hotfix2.py',
 'selftest_v01800_cp2_csharp_compile_regression.py',
 'selftest_v01800_cp2_nd_publish_airfields_ui_hotfix1.py',
 'selftest_v01800_cp2_responsive_airfields_ui_resize_hotfix1.py',
]
for name in tests:
    print('\n=== '+name+' ===', flush=True)
    r=subprocess.run([sys.executable, str(root/'Tools'/name)], cwd=str(root))
    if r.returncode:
        raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3 Gate 4B Geometry Integrity & Neutral Selection Hotfix 2] %d/%d scripts PASS' % (len(tests),len(tests)))
