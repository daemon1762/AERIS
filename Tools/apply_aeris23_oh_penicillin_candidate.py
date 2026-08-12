#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

steps = [
    ROOT / "Tools/apply_aeris23_staggered_exact_refresh_candidate.py",
    ROOT / "Tools/apply_aeris23_oh_penicillin_measurement_calibration.py",
    ROOT / "Tools/apply_aeris23_oh_penicillin_runtime_hooks.py",
    ROOT / "Tools/apply_aeris23_oh_penicillin_logging_policy.py",
    ROOT / "Tools/apply_aeris23_oh_penicillin_identity_finalize.py",
    ROOT / "Tools/verify_aeris23_oh_penicillin_candidate.py",
]

for step in steps:
    if not step.is_file():
        raise SystemExit("[PENICILLIN] required candidate step missing: " + str(step))
    print("[PENICILLIN] running " + step.name)
    subprocess.run([sys.executable, str(step)], cwd=str(ROOT), check=True)

print("[PENICILLIN] AERIS23_OH_PENICILLIN calibrated candidate fully applied and source-verified")
print("[PENICILLIN] AA/AP/FBW/PROTECT/LAND control source remains outside candidate scope")
print("Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py")
print("Then: git diff --check")
print("Build only with ./build_ubuntu.sh <KSP_PATH>; require candidate identity + MATCH=YES before runtime test.")
