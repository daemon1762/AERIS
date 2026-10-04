#!/usr/bin/env bash
set -euo pipefail
ROOT="$(git rev-parse --show-toplevel)"
KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
PROBE="$KSP/GameData/AERIS53Gap201Probe"
SCRATCH="$KSP/Logs/AERIS53_GAP201"
REPORT="$KSP/AERIS53_GAP201_repro.txt"
ARCHIVE="$HOME/.cache/AERIS/gap201-repro/$(date +%Y%m%d-%H%M%S-%N)"
MODE="${1:-install}"
cd "$ROOT"

case "$MODE" in
  collect)
    mkdir -p "$ARCHIVE"
    if test -d "$PROBE"; then mv "$PROBE" "$ARCHIVE/"; fi
    if test -d "$SCRATCH"; then mv "$SCRATCH" "$ARCHIVE/"; fi
    if ! test -f "$REPORT"; then
      echo "STOP: report not found; temporary probe has been retired."
      exit 20
    fi
    cp -a "$REPORT" "$ARCHIVE/"
    cat "$REPORT"
    echo "PROBE_RETIRED=$ARCHIVE"
    exit 0
    ;;
  install) ;;
  *) echo "usage: bash Tools/aeris53_gap2_01_repro.sh [install|collect]"; exit 2 ;;
esac

test "$(git branch --show-current)" = "agent/aeris53-gap2-01"
git diff --quiet
git diff --cached --quiet
git diff --quiet ebac6c8a38aa4a3d88791ca4f45468facb03bd46 -- Source
MANAGED="$KSP/KSP_x64_Data/Managed"
for name in Assembly-CSharp UnityEngine UnityEngine.CoreModule UnityEngine.InputLegacyModule; do test -f "$MANAGED/$name.dll"; done
mapfile -d '' TARGETS < <(find "$KSP/GameData/AERISFlightControl" -type f -name AERISFlightControl.dll -print0)
test "${#TARGETS[@]}" -eq 1
test "$(sha256sum "${TARGETS[0]}" | awk '{print $1}')" = 0f93061f542f596a0af38e14f54ea8e2f89bceb8278be67fcdc97e31d58b7972 || {
  echo "STOP: installed AERIS DLL differs from the verified REG-02 build."
  exit 21
}

python3 TESTS/aeris53_gap2_01_probe.py
BUILD="$(mktemp -d)"
trap 'rm -rf "$BUILD"' EXIT
python3 Tools/aeris53_gap2_01_prepare.py "$BUILD"
mcs -target:library -out:"$BUILD/AERIS53Gap201Probe.dll" \
  -r:"$MANAGED/Assembly-CSharp.dll" -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" -r:"$MANAGED/UnityEngine.InputLegacyModule.dll" \
  TESTS/Runtime/AERIS53Gap201Probe.cs "$BUILD/IsolatedSettings.cs" "$BUILD/IsolatedSupport.cs"
mkdir -p "$ARCHIVE"
if test -d "$PROBE"; then mv "$PROBE" "$ARCHIVE/"; fi
if test -d "$SCRATCH"; then mv "$SCRATCH" "$ARCHIVE/"; fi
if test -f "$REPORT"; then mv "$REPORT" "$ARCHIVE/"; fi
mkdir -p "$PROBE/Plugins"
install -m 0644 "$BUILD/AERIS53Gap201Probe.dll" "$PROBE/Plugins/AERIS53Gap201Probe.dll"
cmp -s "$BUILD/AERIS53Gap201Probe.dll" "$PROBE/Plugins/AERIS53Gap201Probe.dll"
echo "GAP2-01 ISOLATED PROBE INSTALLED; production DLL/settings unchanged."
echo "機体を滑走路に出して10秒待つ。KSPを終了してから collect を実行。"
