#!/usr/bin/env python3
from pathlib import Path

p = Path("Source/AERISFlightControl/Protect/ProtectTelemetry.cs")
s = p.read_text(encoding="utf-8")

checks = []

def check(ok, name):
    checks.append((bool(ok), name))
    print(("PASS: " if ok else "FAIL: ") + name)

check("const float StallRiskReleaseHysteresisDeg = 0.50f;" in s,
      "STALL RISK release hysteresis = 0.50 deg")
check("const float CautionReleaseHysteresisDeg = 0.50f;" in s,
      "CAUTION margin release hysteresis = 0.50 deg")
check("const float CautionPitchRateEntryDegPerSec = 12.0f;" in s and
      "const float CautionPitchRateReleaseDegPerSec = 10.0f;" in s,
      "HighPitchRate entry remains 12 deg/s with 10 deg/s release")
check("bool nearBoundary = StallMarginDegrees <= riskMargin;" in s,
      "existing STALL RISK margin entry threshold preserved")
check("bool beyondBoundary = StallMarginDegrees < -0.35f;" in s,
      "existing STALL DETECTED AoA boundary preserved")
check("else if (nearBoundary || (fastApproach && largeSideslip) || " in s,
      "existing hazardous STALL RISK entry path preserved")
check("else if (holdStallRiskForMarginRecovery)" in s,
      "STALL RISK release hold installed")
check("else if (cautionMarginActive || cautionPitchRateActive)" in s and
      "else if (holdCautionForMarginRecovery || holdCautionForPitchRateRecovery)" in s,
      "CAUTION entry and separate release holds installed")
check("internal bool ProtectActive { get { return AntiStallEnabled && (Risk == ProtectRiskLevel.StallRisk || Risk == ProtectRiskLevel.StallDetected); } }" in s,
      "ProtectActive authority remains STALL RISK / STALL DETECTED only")
check('Risk = ProtectRiskLevel.Unavailable;' in s and
      'SetUnavailable("Standby / scene reset: " + reason);' in s,
      "scene/unavailable reset still clears risk state")

classification = s[s.index("            float cautionMargin ="):
                   s.index("            ComputeThrustAssist(v);",
                           s.index("            float cautionMargin ="))]

order = [
    classification.index("if (beyondBoundary"),
    classification.index("else if (nearBoundary"),
    classification.index("else if (holdStallRiskForMarginRecovery)"),
    classification.index("else if (cautionMarginActive || cautionPitchRateActive)"),
    classification.index("else if (holdCautionForMarginRecovery || holdCautionForPitchRateRecovery)"),
]
check(order == sorted(order),
      "hazard entry remains ahead of every de-escalation hysteresis path")

failed = [name for ok, name in checks if not ok]
print()
print(f"[AERIS51 B2 PROTECT HYSTERESIS] {len(checks)-len(failed)}/{len(checks)} PASS")
if failed:
    raise SystemExit(1)
