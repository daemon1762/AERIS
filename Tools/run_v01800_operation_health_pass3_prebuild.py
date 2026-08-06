#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
suites=[
 ('Operation Health Pass 3 Cadence Hotfix 1','selftest_v01800_operation_health_pass3_cadence_hotfix1.py'),
 ('Operation Health Pass 3 projection/draw reduction','selftest_v01800_operation_health_pass3_projection_draw_reduction.py'),
 ('Operation Health Pass 2 persistent geometry','selftest_v01800_operation_health_pass2_persistent_geometry.py'),
 ('Operation Health Pass 1 zero-visual-cost','selftest_v01800_operation_health_pass1_zero_visual_cost.py'),
 ('Operation Health Pass 1 Hotfix 1 symbology sync','selftest_v01800_operation_health_pass1_hotfix1_symbology_sync.py'),
 ('CP3.75 Candidate11 contour authority','selftest_v01800_cp375_candidate11_contour_level_budget_coastal_clip.py'),
 ('CP2 C# compile regression','selftest_v01800_cp2_csharp_compile_regression.py'),
]
for label,name in suites:
 print('\n=== '+label+' ===')
 r=subprocess.run([sys.executable,str(ROOT/'Tools'/name)],cwd=str(ROOT))
 if r.returncode: raise SystemExit(r.returncode)
print('\n[Operation Health Pass 3 PREBUILD] %d/%d suites PASS' % (len(suites),len(suites)))
