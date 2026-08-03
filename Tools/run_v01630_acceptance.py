#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
tests = [
    "verify_v01630_static.py",
    "verify_v01630_build_entrypoint.py",
    "selftest_v01630_legacy_nav_removal.py",
    "selftest_v01630_common_regressions.py",
    "selftest_v01630_fdr_schema.py",
    "selftest_v01630_foundation_data_display.py",
    "selftest_v01630_user_layout_ui.py",
    "selftest_v01630_land_foundation.py",
    "selftest_v01630_terrain_nd.py",
    "selftest_v01630_improvements.py",
    "selftest_v01630_mod_runway_survey.py",
    "verify_v01630_manifest.py",
]
for name in tests:
    print(f"\n=== {name} ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print(f"\n[v0.16.3.0 acceptance] {len(tests)}/{len(tests)} scripts PASS")
