#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
BRANCH="agent/aeris53-gap2-01"
KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
SRC="$ROOT/Source/AERISFlightControl"
DLL="$SRC/bin/Release/AERISFlightControl.dll"
cd "$ROOT"
test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: expected $BRANCH"
  exit 10
}
git diff --quiet
git diff --cached --quiet
git diff --quiet de68b2bf4e2ce3e2d58d35ed3fa0f052dd3dd089 -- Source \
  ':(exclude)Source/AERISFlightControl/Settings/AERISSettings.cs'
test -f "$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" || {
  echo "STOP: desktop KSP not found: $KSP"
  exit 11
}

echo "=== GAP2-01 AND EXISTING REGRESSION ==="
python3 TESTS/aeris53_gap2_01_settings_roundtrip.py
python3 TESTS/aeris53_gap2_01_probe.py
python3 TESTS/aeris53_reg_02_facility_provenance.py
python3 TESTS/aeris53_protect_02_manual_throttle.py
python3 TESTS/aeris53_protect_01_speed_decay_state_memory.py
python3 Tools/selftest_aeris51_b2_protect_speed_decay_hysteresis.py
python3 Tools/selftest_aeris51_b2_protect_risk_hysteresis.py
python3 TESTS/aeris53_gate3_r_core_01.py
python3 TESTS/aeris53_gate3_r_core_02.py

echo "=== CLEAN RELEASE BUILD ==="
rm -rf "$SRC/bin/Release" "$SRC/obj/Release"
(
  cd "$SRC"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj
)
test -f "$DLL" || { echo "STOP: build completed without DLL"; exit 12; }

mapfile -d '' TARGETS < <(find "$KSP/GameData/AERISFlightControl" -type f -name AERISFlightControl.dll -print0)
test "${#TARGETS[@]}" -eq 1 || {
  echo "STOP: expected exactly one installed AERISFlightControl.dll; found ${#TARGETS[@]}"
  exit 13
}
BACKUP_DIR="$HOME/.cache/AERIS/gap201-build-backups/$(date +%Y%m%d-%H%M%S-%N)"
mkdir -p "$BACKUP_DIR"
cp -a "${TARGETS[0]}" "$BACKUP_DIR/AERISFlightControl.dll"
CONFIG="$KSP/GameData/AERISFlightControl/Config/AERISSettings.cfg"
if test -f "$CONFIG"; then cp -a "$CONFIG" "$BACKUP_DIR/AERISSettings.cfg"; fi
install -m 0644 "$DLL" "${TARGETS[0]}"
cmp -s "$DLL" "${TARGETS[0]}"

echo "=== GAP2-01 BUILD/INSTALL PASS ==="
echo "HEAD=$(git rev-parse HEAD)"
echo "DLL_SHA256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "INSTALLED_SHA256=$(sha256sum "${TARGETS[0]}" | awk '{print $1}')"
echo "BACKUP=$BACKUP_DIR"
git status -sb
