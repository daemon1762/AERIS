#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[1]
tests = [
    "verify_v01702_static.py",
    "verify_v01702_build_entrypoint.py",
    "selftest_v0170_generation_scheduler.py",
    "selftest_v0170_async_recording.py",
    "selftest_v0170_pc1_golden.py",
    "selftest_v01702_runway_registry_identity.py",
    "selftest_v0164_consensus_runway.py",
    "selftest_v0164_reload_land_ui.py",
    "selftest_v01630_legacy_nav_removal.py",
    "selftest_v01630_foundation_data_display.py",
    "selftest_v01630_user_layout_ui.py",
]
if (root / "MANIFEST_SHA256.txt").is_file():
    tests.append("verify_v01702_manifest.py")
for name in tests:
    print("\n=== " + name + " ===", flush=True)
    result = subprocess.run([sys.executable, str(root / "Tools" / name)], cwd=str(root))
    if result.returncode != 0:
        raise SystemExit(result.returncode)
print("\n[v0.17.0.2 acceptance] " + str(len(tests)) + "/" +
      str(len(tests)) + " scripts PASS")
