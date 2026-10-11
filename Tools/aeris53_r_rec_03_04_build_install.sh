#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
BRANCH="agent/aeris53-r-rec-03-04"
BASE="cba42decb7c7fbcf18a62bef57bae7ba5701fde9"
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
git diff --quiet "$BASE" -- Source \
  ':(exclude)Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'
test -f "$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" || {
  echo "STOP: desktop KSP not found: $KSP"
  exit 11
}

echo "=== R-REC-03/04 AND EXISTING REGRESSION ==="
python3 TESTS/aeris53_r_rec_03_04_startup.py
python3 TESTS/aeris53_r_rec_01_write_failures.py
python3 TESTS/aeris53_r_rec_02_archive_notifications.py
python3 TESTS/aeris53_r_aa2_03_pitch_thrust.py
python3 TESTS/aeris53_r_aa2_02_coordinates.py
python3 TESTS/aeris53_r_aa2_01_engine_count.py
python3 TESTS/aeris53_gap2_01_settings_roundtrip.py
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
BACKUP_DIR="$HOME/.cache/AERIS/rec0304-build-backups/$(date +%Y%m%d-%H%M%S-%N)"
mkdir -p "$BACKUP_DIR"
cp -a "${TARGETS[0]}" "$BACKUP_DIR/AERISFlightControl.dll"
CONFIG="$KSP/GameData/AERISFlightControl/Config/AERISSettings.cfg"
if test -f "$CONFIG"; then cp -a "$CONFIG" "$BACKUP_DIR/AERISSettings.cfg"; fi
install -m 0644 "$DLL" "${TARGETS[0]}"
cmp -s "$DLL" "${TARGETS[0]}"

echo "=== R-REC-03/04 BUILD/INSTALL PASS ==="
echo "HEAD=$(git rev-parse HEAD)"
echo "DLL_SHA256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "INSTALLED_SHA256=$(sha256sum "${TARGETS[0]}" | awk '{print $1}')"
echo "BACKUP=$BACKUP_DIR"
git status -sb

python3 Tools/aeris53_r_rec_03_04_audit.py
