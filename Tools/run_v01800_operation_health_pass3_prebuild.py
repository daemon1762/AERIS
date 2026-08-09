#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
suites=[
 ('Operation Health Step 2 Motion Content Split + Coastal Edge Refinement','selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'),
 ('Operation Health FRONT Presentation Fast Path','selftest_v01800_operation_health_front_presentation_fastpath.py'),
 ('Operation Health Retained FRONT Surface','selftest_v01800_operation_health_retained_surface.py'),
 ('Operation Health Conservative Entry Culling','selftest_v01800_operation_health_conservative_entry_culling.py'),
 ('ND Fixed Aspect Rollback','selftest_v01800_nd_fixed_aspect_resize.py'),
 ('Operation Health Pass 3 Cadence Hotfix 4 Loading Ready State','selftest_v01800_operation_health_pass3_cadence_hotfix4_loading_ready_state.py'),
 ('Operation Health Pass 3 Cadence Hotfix 3 Motion Commit','selftest_v01800_operation_health_pass3_cadence_hotfix3_motion_commit.py'),
 ('Operation Health Pass 3 Cadence Hotfix 2 Refresh Coalescing','selftest_v01800_operation_health_pass3_cadence_hotfix2_refresh_coalescing.py'),
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
