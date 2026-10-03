#!/usr/bin/env python3
from pathlib import Path

src = Path("Source/AERISFlightControl/Protect/ProtectTelemetry.cs").read_text()

checks = []

def check(condition, label):
    checks.append((bool(condition), label))
    print(("PASS: " if condition else "FAIL: ") + label)

check(
    "bool speedDecayStallRiskLatched;" in src,
    "PROTECT-01 uses dedicated SpeedDecay StallRisk memory"
)

check(
    'StallReason == "SpeedDecay+LowAoAMargin"' not in src,
    "mutable StallReason text is not used as SpeedDecay state-machine memory"
)

check(
    "const float StallRiskSpeedDecayReleaseMps2 = 3.0f;" in src and
    "SpeedDecayPerSecond > 4.0f" in src,
    "accepted 4.0 m/s² entry and 3.0 m/s² release thresholds remain unchanged"
)

check(
    "const float StallRiskReleaseHysteresisDeg = 0.50f;" in src and
    "const float CautionReleaseHysteresisDeg = 0.50f;" in src and
    "const float CautionPitchRateEntryDegPerSec = 12.0f;" in src and
    "const float CautionPitchRateReleaseDegPerSec = 10.0f;" in src,
    "accepted B2/HF1 hysteresis thresholds remain unchanged"
)

check(
    "bool speedDecayStallRiskEntry =" in src and
    "meaningfulSpeedLoss && StallMarginDegrees <= cautionMargin;" in src,
    "qualifying SpeedDecay entry is tracked independently of display reason priority"
)

check(
    "speedDecayStallRiskLatched &&" in src and
    "Risk == ProtectRiskLevel.StallRisk &&" in src and
    "SpeedDecayPerSecond >= StallRiskSpeedDecayReleaseMps2" in src and
    "StallMarginDegrees <= cautionMargin + CautionReleaseHysteresisDeg" in src,
    "release hold reads dedicated latch and preserves accepted recovery envelope"
)

anti = src.index("if (!AntiStallEnabled)")
hazard_detected = src.index(
    "if (beyondBoundary && (meaningfulSpeedLoss || demandStillUp || largeSideslip || EnergyCollapseDetected))"
)
hazard_risk = src.index(
    "else if (nearBoundary || (fastApproach && largeSideslip) || speedDecayStallRiskEntry)"
)
speed_hold = src.index("else if (holdStallRiskForSpeedDecayRecovery)")
margin_hold = src.index("else if (holdStallRiskForMarginRecovery)")
caution = src.index("else if (cautionMarginActive || cautionPitchRateActive)")
safe = src.index("else\n            {\n                speedDecayStallRiskLatched = false;", caution)

check(
    hazard_detected < hazard_risk < speed_hold < margin_hold < caution < safe,
    "StallDetected and hazardous entry remain authoritative ahead of release holds"
)

anti_block = src[anti:src.index("float cautionMargin", anti)]
check(
    "speedDecayStallRiskLatched = false;" in anti_block,
    "Anti-Stall OFF clears SpeedDecay memory"
)

detected_block = src[hazard_detected:hazard_risk]
check(
    "speedDecayStallRiskLatched = false;" in detected_block,
    "StallDetected preemption clears stale StallRisk-origin memory"
)

risk_block = src[hazard_risk:speed_hold]
check(
    "speedDecayStallRiskLatched = speedDecayStallRiskEntry || holdStallRiskForSpeedDecayRecovery;"
    in " ".join(risk_block.split()),
    "StallRisk classification preserves/enters dedicated SpeedDecay memory"
)

speed_hold_block = src[speed_hold:margin_hold]
check(
    "speedDecayStallRiskLatched = true;" in speed_hold_block,
    "SpeedDecay hysteretic hold keeps explicit memory active"
)

margin_hold_block = src[margin_hold:caution]
check(
    "speedDecayStallRiskLatched = false;" in margin_hold_block,
    "non-SpeedDecay StallRisk hold cannot retain stale SpeedDecay memory"
)

set_unavailable = src.index("void SetUnavailable(string status)")
reset_scene = src.index("internal void ResetForSceneTransition", set_unavailable)
unavailable_block = src[set_unavailable:reset_scene]
check(
    "speedDecayStallRiskLatched = false;" in unavailable_block,
    "unavailable/MASTER-off/scene-reset path clears SpeedDecay memory"
)

check(
    "!IntentionalDecelerationActive" in src and
    "StallReason += \"+DecelerationCoordination\";" in src,
    "intentional-deceleration exemption and diagnostic text remain preserved"
)

passed = sum(ok for ok, _ in checks)
total = len(checks)
print()
print(f"[AERIS53 PROTECT-01 DEDICATED SPEED-DECAY MEMORY] {passed}/{total} PASS")
raise SystemExit(0 if passed == total else 1)
