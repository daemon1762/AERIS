#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
root=Path(__file__).resolve().parents[1]
tests=[
 'selftest_v01800_cp3_gate5_candidate14_solid_surface_preload_exclusion_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate13_final_ui_preload_policy_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate12_compile_hotfix2.py',
 'selftest_v01800_cp3_gate5_candidate12_compile_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate12_dlc_dessert_field_verified_default_baseline.py',
 'selftest_v01800_cp3_gate5_candidate11_dlc_placeholder_manual_ab_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate10_airfield_provider_visibility_dlc_list_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate9_ui_stability_telemetry_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate8_nd_phantom_runway_performance_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate7_expansion_detection_status_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate6_field_verified_runway_defaults.py',
 'selftest_v01800_cp3_gate5_candidate5_manual_authority_native_spawn_altitude_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate4_native_spawn_safety_hotfix2.py',
 'selftest_v01800_cp3_gate5_candidate4_native_spawn_compile_hotfix1.py',
 'selftest_v01800_cp3_gate5_candidate4_native_spawn_warp.py',
 'selftest_v01800_cp3_gate5_gate4c_successor.py',
 'selftest_v01800_cp2_csharp_compile_regression.py',
 'selftest_v01800_cp3_gate4a_supply_pipeline_successor.py',
 'selftest_v01800_cp3_gate4a_runway_terrain_safety_successor.py',
 'selftest_v01800_cp3_gate4a_runway_map_lock_successor.py',
 'selftest_v01800_cp3_gate4a_nd_core_successor.py',
 'selftest_v01800_cp3_gate4a_alignment_diagnostic_successor.py',
 'selftest_v01800_cp3_gate5_predictive_corridor_successor.py',
 'selftest_v01800_cp25_map_dram_cache_foundation_hotfix2.py',
 'selftest_v01800_cp2_nd_publish_airfields_ui_hotfix1.py',
 'selftest_v01800_cp2_responsive_airfields_ui_resize_hotfix1.py',
]
for name in tests:
    print('\n=== '+name+' ===',flush=True)
    r=subprocess.run([sys.executable,str(root/'Tools'/name)],cwd=str(root))
    if r.returncode: raise SystemExit(r.returncode)
print('\n[v0.18.0.0 CP3 Gate 5 Candidate 14 Solid-Surface Preload Exclusion Hotfix 1] %d/%d scripts PASS' % (len(tests),len(tests)))
