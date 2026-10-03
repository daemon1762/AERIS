#!/usr/bin/env python3
from pathlib import Path

src = Path("Source/AERISFlightControl/Protect/ProtectTelemetry.cs").read_text()

checks = []

def check(condition, label):
    checks.append((bool(condition), label))
    print(("PASS: " if condition else "FAIL: ") + label)

check(
    "const float StallRiskSpeedDecayReleaseMps2 = 3.0f;" in src,
    "SpeedDecay StallRisk release threshold = 3.0 m/s²"
)

check(
    "SpeedDecayPerSecond > 4.0f" in src,
    "existing hazardous SpeedDecay entry remains >4.0 m/s²"
)

check(
    "bool speedDecayStallRiskLatched;" in src and
    'StallReason == "SpeedDecay+LowAoAMargin"' not in src,
    "HF2 recovery memory is dedicated control state, not diagnostic StallReason text"
)

check(
    "SpeedDecayPerSecond >= StallRiskSpeedDecayReleaseMps2" in src,
    "HF2 release Schmitt hold is installed"
)

check(
    "SurfaceSpeed > 20f" in src and
    "!IntentionalDecelerationActive" in src,
    "existing speed floor and intentional-deceleration exemption preserved"
)

check(
    "StallMarginDegrees <= cautionMargin + CautionReleaseHysteresisDeg" in src,
    "SpeedDecay recovery hold remains bounded to caution-margin recovery envelope"
)

check(
    'StallReason = "SpeedDecay+LowAoAMargin";' in src and
    "speedDecayStallRiskLatched = true;" in src,
    "SpeedDecay StallRisk reason remains diagnostic while HF2 latch remains active"
)

hazard_detected = src.index(
    "if (beyondBoundary && (meaningfulSpeedLoss || demandStillUp || largeSideslip || EnergyCollapseDetected))"
)
hazard_risk = src.index(
    "else if (nearBoundary || (fastApproach && largeSideslip) || (meaningfulSpeedLoss && StallMarginDegrees <= cautionMargin))"
)
speed_hold = src.index(
    "else if (holdStallRiskForSpeedDecayRecovery)"
)
margin_hold = src.index(
    "else if (holdStallRiskForMarginRecovery)"
)
caution = src.index(
    "else if (cautionMarginActive || cautionPitchRateActive)"
)

check(
    hazard_detected < hazard_risk < speed_hold < margin_hold < caution,
    "hazard entry remains authoritative ahead of all HF2/HF1 release holds"
)

check(
    "const float StallRiskReleaseHysteresisDeg = 0.50f;" in src and
    "const float CautionReleaseHysteresisDeg = 0.50f;" in src and
    "const float CautionPitchRateEntryDegPerSec = 12.0f;" in src and
    "const float CautionPitchRateReleaseDegPerSec = 10.0f;" in src,
    "HF1 margin/pitch hysteresis remains unchanged"
)

passed = sum(ok for ok, _ in checks)
total = len(checks)

print()
print(f"[AERIS51 B2 HF2 SPEED DECAY HYSTERESIS] {passed}/{total} PASS")

raise SystemExit(0 if passed == total else 1)
