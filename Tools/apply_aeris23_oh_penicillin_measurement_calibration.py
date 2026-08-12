#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
text = path.read_text()

def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f"[PENICILLIN CAL] {label}: expected 1 anchor, found {count}")
    return src.replace(old, new, 1)

old_ratio = "        double latestFiveSecondRealtimeRatio = 1.0;"
new_ratio = "        double latestFiveSecondRealtimeRatio = double.NaN;"
if old_ratio in text:
    text = replace_once(text, old_ratio, new_ratio,
                        "5-second realtime ratio initialization")
elif new_ratio not in text:
    raise SystemExit("[PENICILLIN CAL] 5-second ratio initialization anchor missing")

old_debt = '''                double debtMs = Math.Max(0.0,
                    wallSinceWindow - fixedSimSecondsWindow) * 1000.0;
'''
new_debt = '''                // A healthy FixedUpdate stream can sit anywhere within one fixed-step
                // phase relative to Update. Only debt beyond that normal quantization window
                // is reported as physics backlog.
                double normalFixedPhaseSeconds =
                    Math.Max(0.0, Time.fixedDeltaTime);
                double debtMs = Math.Max(0.0,
                    wallSinceWindow - fixedSimSecondsWindow -
                    normalFixedPhaseSeconds) * 1000.0;
'''
if old_debt in text:
    text = replace_once(text, old_debt, new_debt,
                        "physics debt fixed-phase tolerance")
elif "normalFixedPhaseSeconds" not in text:
    raise SystemExit("[PENICILLIN CAL] physics debt calibration anchor missing")

path.write_text(text)
print("[PENICILLIN CAL] 5-second ratio remains NA until a real 5-second sample exists")
print("[PENICILLIN CAL] physics debt ignores one normal fixed-step phase")
print("[PENICILLIN CAL] passive measurement calibration PASS")
