#!/usr/bin/env bash
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
PROBE="$KSP/GameData/AERIS53Reg02Probe"
REPORT="$KSP/AERIS53_REG02_repro.txt"
ARCHIVE="$HOME/.cache/AERIS/reg02-repro/$(date +%Y%m%d-%H%M%S-%N)"
MODE="${1:-install}"
cd "$ROOT"

case "$MODE" in
  collect)
    mkdir -p "$ARCHIVE"
    if test -d "$PROBE"; then mv "$PROBE" "$ARCHIVE/"; fi
    if ! test -f "$REPORT"; then
      echo "STOP: report not found; temporary probe has been retired."
      exit 20
    fi
    cp -a "$REPORT" "$ARCHIVE/"
    cat "$REPORT"
    echo "PROBE_RETIRED=$ARCHIVE"
    exit 0
    ;;
  install|candidate) ;;
  *) echo "usage: bash Tools/aeris53_reg_02_repro.sh [install|candidate|collect]"; exit 2 ;;
esac

test "$(git branch --show-current)" = "agent/aeris53-reg-02"
git diff --quiet
git diff --cached --quiet
MANAGED="$KSP/KSP_x64_Data/Managed"
for name in Assembly-CSharp UnityEngine UnityEngine.CoreModule; do test -f "$MANAGED/$name.dll"; done
mapfile -d '' TARGETS < <(find "$KSP/GameData/AERISFlightControl" -type f -name AERISFlightControl.dll -print0)
test "${#TARGETS[@]}" -eq 1
if test "$MODE" = install; then
  git diff --quiet a12d86aea741769fa0bbbe16c255efd2dbe0affc -- Source
  test "$(sha256sum "${TARGETS[0]}" | awk '{print $1}')" = e174c73c342ef1751931657f55d05d8edcff95d8eeafbdd90a57d1ff35cc9264 || {
    echo "STOP: installed AERIS DLL differs from the verified PROTECT-02 build."
    exit 21
  }
else
  cmp -s "$ROOT/Source/AERISFlightControl/bin/Release/AERISFlightControl.dll" "${TARGETS[0]}" || {
    echo "STOP: installed AERIS DLL differs from the local REG-02 candidate build."
    exit 22
  }
fi

BUILD="$(mktemp -d)"
trap 'rm -rf "$BUILD"' EXIT
mcs -target:library -out:"$BUILD/AERIS53Reg02Probe.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" TESTS/Runtime/AERIS53Reg02Probe.cs
mkdir -p "$ARCHIVE"
if test -d "$PROBE"; then mv "$PROBE" "$ARCHIVE/"; fi
if test -f "$REPORT"; then mv "$REPORT" "$ARCHIVE/"; fi
mkdir -p "$PROBE/Plugins"
install -m 0644 "$BUILD/AERIS53Reg02Probe.dll" "$PROBE/Plugins/AERIS53Reg02Probe.dll"
cmp -s "$BUILD/AERIS53Reg02Probe.dll" "$PROBE/Plugins/AERIS53Reg02Probe.dll"
echo "REG-02 PROBE INSTALLED; production DLL unchanged."
echo "機体を滑走路に出して10秒待つ。KSPを終了してから collect を実行。"
