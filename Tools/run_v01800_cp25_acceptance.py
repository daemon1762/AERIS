#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True

root = Path(__file__).resolve().parents[1]
tests = [
    "selftest_v01800_cp25_final_closure_standard_preload_only.py",
    "selftest_v01800_cp25_candidate4_airfields_zero_category_ui_hotfix1.py",
    "selftest_v01800_cp25_integrated_acceptance_candidate2.py",
    "selftest_v01800_cp25_integrated_acceptance_candidate1.py",
    "selftest_v01800_cp25_map_dram_cache_foundation_hotfix2.py",
    "selftest_v01800_cp25_map_dram_cache_foundation_hotfix1.py",
    "selftest_v01800_cp25_altitude_gate_hotfix1.py",
    "selftest_v01800_cp25_quality_migration_hotfix1.py",
    "selftest_v01800_cp25_land_separation_hotfix1.py",
    "verify_v01800_gate0_static.py",
    "verify_v01800_cp1_static.py",
    "verify_v01800_cp2_static.py",
    "verify_v01800_cp1_build_entrypoint.py",
    "verify_v01800_cp2_build_entrypoint.py",
    "selftest_v01800_cp2_preload_terrain_integration.py",
    "selftest_v01800_cp2_preload_status_toolbar.py",
    "selftest_v01800_cp2_csharp_compile_regression.py",
    "selftest_v01800_cp2_render_hotfix2.py",
    "selftest_v01800_cp2_supply_pipeline_hotfix3.py",
    "selftest_v01800_cp2_field_render_consistency_hotfix1.py",
    "selftest_v01800_cp2_alignment_diagnostic_hotfix1.py",
    "selftest_v01800_cp2_runway_terrain_safety_hotfix1.py",
    "selftest_v01800_cp2_closure_candidate1.py",
    "selftest_v01800_cp2_runway_map_lock_hotfix1.py",
    "selftest_v01800_cp2_runway_map_lock_hotfix2_preload_fastpath1.py",
    "selftest_v01800_cp2_runway_map_lock_hotfix2_compile_hotfix1.py",
    "selftest_v01800_cp2_runway_presentation_ui_hotfix1.py",
    "selftest_v01800_cp2_kk_runway_absolute_registration_hotfix1.py",
    "selftest_v01800_cp2_kk_runway_axis_registration_hotfix1.py",
    "selftest_v01800_cp2_kk_runway_axis_registration_compile_hotfix1.py",
    "selftest_v01800_cp2_kk_runway_axis_reference_hotfix2.py",
    "selftest_v01800_cp2_generic_runway_placement_verification_final_candidate3.py",
    "selftest_v01800_cp2_generic_runway_placement_fc3_compile_hotfix1.py",
    "selftest_v01800_cp2_calibration_roundtrip_hotfix1.py",
    "selftest_v01800_cp2_bidirectional_runway_pair_hotfix1.py",
    "selftest_v01800_cp2_nd_publish_airfields_ui_hotfix1.py",
    "selftest_v01800_cp2_responsive_airfields_ui_resize_hotfix1.py",
    "selftest_v01800_cp2_manual_calibrated_runway_separation_preservation_hotfix1.py",
    "selftest_v01800_cp2_manual_calibration_reflection_hotfix1.py",
    "selftest_v01800_cp2_manual_runway_designation_grouping_hotfix1.py",
    "selftest_v01800_cp2_manual_runway_absolute_geodetic_endpoint_authority_hotfix1.py",
    "selftest_v01800_cp2_absolute_geodetic_build_entrypoint_hotfix1.py",
    "selftest_v01800_cp2_mod_airfield_recovery_autopreload1.py",
    "selftest_v01800_cp2_mod_airfield_recovery_autopreload_compile_hotfix1.py",
    "selftest_v01800_cp1_nd_core.py",
    "selftest_v01800_cp2_terrain_tiles.py",
    "selftest_v01800_cp2_nd_auxiliary.py",
    "selftest_v01800_physical_runway_federation.py",
    "selftest_v01800_cache_schema7_physical_aliases.py",
    "selftest_v01800_schema7_identity_migration.py",
    "selftest_v01800_cache_roundtrip_determinism.py",
    "selftest_v01800_canonical_source_cache.py",
    "selftest_v01800_submetre_provider_frame_cache.py",
    "selftest_v01800_adaptive_approach_foundation.py",
    "selftest_v0170_generation_scheduler.py",
    "selftest_v0170_async_recording.py",
    "selftest_v0170_pc1_golden.py",
    "selftest_v01703_startup_cache_archive.py",
    "selftest_v01702_runway_registry_identity.py",
    "selftest_v0164_consensus_runway.py",
    "selftest_v0164_reload_land_ui.py",
    "selftest_v01630_legacy_nav_removal.py",
    "selftest_v01630_foundation_data_display.py",
    "selftest_v01630_user_layout_ui.py",
]
if (root / "MANIFEST_SHA256.txt").is_file():
    tests.append("verify_v01800_cp2_manifest.py")
for name in tests:
    print("\n=== " + name + " ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print("\n[v0.18.0.0 CP2.5 Final Closure Standard Preload Only / CP3 Entry Baseline] " + str(len(tests)) + "/" +
      str(len(tests)) + " scripts PASS")
