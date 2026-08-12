#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

source_verify = ROOT / "Tools/verify_aeris23_oh_penicillin_source.py"
logging_verify = ROOT / "Tools/verify_aeris23_oh_penicillin_logging.py"
for verifier in (source_verify, logging_verify):
    if not verifier.is_file():
        raise SystemExit("[PENICILLIN FINAL VERIFY] verifier missing: " + str(verifier))
    subprocess.run([sys.executable, str(verifier)], cwd=str(ROOT), check=True)

monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
build = (ROOT / "build_ubuntu.sh").read_text()

def require(condition, message):
    if not condition:
        raise SystemExit("[PENICILLIN FINAL VERIFY] FAIL: " + message)
    print("[PASS] " + message)

def active_count(text, line):
    target = line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip() == target)

require("double latestFiveSecondRealtimeRatio = double.NaN;" in monitor,
        "5-second realtime ratio is NA until a real 5-second sample exists")
require("double normalFixedPhaseSeconds" in monitor and
        "wallSinceWindow - fixedSimSecondsWindow -" in monitor and
        "normalFixedPhaseSeconds) * 1000.0" in monitor,
        "physics debt ignores one normal FixedUpdate phase")
require(active_count(build, 'CANDIDATE_NAME="AERIS23_OH_PENICILLIN"') == 1,
        "build executable candidate identity remains PENICILLIN")
require(active_count(build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_oh_penicillin_candidate.py"') == 1,
        "build preflight requires final calibrated PENICILLIN verifier")

print("[AERIS23 CANDIDATE VERIFY] OH_PENICILLIN CALIBRATED SOURCE+LOGGING+SAFETY PASS")
