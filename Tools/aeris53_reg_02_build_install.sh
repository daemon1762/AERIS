#!/usr/bin/env bash
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
BRANCH="agent/aeris53-reg-02"
KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
SRC="$ROOT/Source/AERISFlightControl"
DLL="$SRC/bin/Release/AERISFlightControl.dll"

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: expected $BRANCH"
  git branch --show-current
  exit 10
}

git diff --quiet
git diff --cached --quiet

test -f "$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" || {
  echo "STOP: desktop KSP not found: $KSP"
  exit 11
}

echo "=== REG-02 REGRESSION ==="
python3 TESTS/aeris53_reg_02_facility_provenance.py
python3 TESTS/aeris53_reg_02_probe.py
python3 TESTS/aeris53_protect_02_manual_throttle.py
python3 TESTS/aeris53_protect_01_speed_decay_state_memory.py
python3 Tools/selftest_aeris51_b2_protect_speed_decay_hysteresis.py
python3 Tools/selftest_aeris51_b2_protect_risk_hysteresis.py
python3 TESTS/aeris53_gate3_r_core_01.py
python3 TESTS/aeris53_gate3_r_core_02.py

echo
echo "=== CLEAN RELEASE BUILD ==="
rm -rf "$SRC/bin/Release" "$SRC/obj/Release"
(
  cd "$SRC"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj
)

test -f "$DLL" || {
  echo "STOP: build completed without DLL"
  exit 12
}

TARGETS=()
while IFS= read -r -d '' f; do TARGETS+=("$f"); done < <(
  find "$KSP/GameData/AERISFlightControl" -type f -name 'AERISFlightControl.dll' -print0
)

test "${#TARGETS[@]}" -eq 1 || {
  echo "STOP: expected exactly one installed AERISFlightControl.dll; found ${#TARGETS[@]}"
  printf '%s\n' "${TARGETS[@]}"
  exit 13
}

TARGET="${TARGETS[0]}"
STAMP="$(date +%Y%m%d-%H%M%S-%N)"
BACKUP_DIR="$HOME/.cache/AERIS/reg02-build-backups"
mkdir -p "$BACKUP_DIR"
cp -a "$TARGET" "$BACKUP_DIR/$STAMP.AERISFlightControl.dll"

install -m 0644 "$DLL" "$TARGET"
cmp -s "$DLL" "$TARGET"

echo
echo "=== REG-02 BUILD/INSTALL PASS ==="
echo "HEAD=$(git rev-parse HEAD)"
echo "DLL_SHA256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "INSTALLED_SHA256=$(sha256sum "$TARGET" | awk '{print $1}')"
git status -sb

# Temporary, read-only runtime verification; collect retires the probe.
bash Tools/aeris53_reg_02_repro.sh candidate
