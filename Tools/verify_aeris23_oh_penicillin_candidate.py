#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

source_verify = ROOT / "Tools/verify_aeris23_oh_penicillin_source.py"
if not source_verify.is_file():
    raise SystemExit("[PENICILLIN FINAL VERIFY] source verifier missing")
subprocess.run([sys.executable, str(source_verify)], cwd=str(ROOT), check=True)

monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
build = (ROOT / "build_ubuntu.sh").read_text()

def require(condition, message):
    if not condition:
        raise SystemExit("[PENICILLIN FINAL VERIFY] FAIL: " + message)
    print("[PASS] " + message)

require("double latestFiveSecondRealtimeRatio = double.NaN;" in monitor,
        "5-second realtime ratio is NA until a real 5-second sample exists")
require("double normalFixedPhaseSeconds" in monitor and
        "wallSinceWindow - fixedSimSecondsWindow -" in monitor and
        "normalFixedPhaseSeconds) * 1000.0" in monitor,
        "physics debt ignores one normal FixedUpdate phase")
require('CANDIDATE_NAME="AERIS23_OH_PENICILLIN"' in build,
        "build candidate identity remains PENICILLIN")
require("verify_aeris23_oh_penicillin_candidate.py" in build,
        "build preflight requires final calibrated PENICILLIN verifier")

print("[AERIS23 CANDIDATE VERIFY] OH_PENICILLIN CALIBRATED SOURCE+SAFETY PASS")
