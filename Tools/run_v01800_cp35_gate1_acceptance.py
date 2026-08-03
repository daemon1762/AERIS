#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
tests=[
 'selftest_v01800_cp3_gate5_candidate14_solid_surface_preload_exclusion_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate9_ui_stability_telemetry_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate8_nd_phantom_runway_performance_hotfix1.py',
 'selftest_v01800_cp2_csharp_compile_regression.py',
 'selftest_v01800_cp3_gate4a_supply_pipeline_successor.py',
 'selftest_v01800_cp3_gate4a_runway_terrain_safety_successor.py',
 'selftest_v01800_cp3_gate4a_runway_map_lock_successor.py',
 'selftest_v01800_cp3_gate4a_nd_core_successor.py',
 'selftest_v01800_cp3_gate4a_alignment_diagnostic_successor.py',
 'selftest_v01800_cp3_gate5_predictive_corridor_successor.py',
 'selftest_v01800_cp25_map_dram_cache_foundation_hotfix2.py',
 'selftest_v01800_cp35_gate1_presentation_cadence_responsive_ui_candidate1.py',
]
for name in tests:
    print('\n=== '+name+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3.5 Gate 1 Presentation Cadence / Responsive UI Candidate 1] %d/%d scripts PASS' % (len(tests),len(tests)))
