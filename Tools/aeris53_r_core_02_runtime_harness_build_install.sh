#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
KSP="${1:-${KSP:-}}"

if [[ -z "$KSP" ]]; then
  for candidate in \
    "$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program" \
    "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
  do
    if [[ -f "$candidate/KSP.x86_64" || -f "$candidate/KSP_x64.exe" ]]; then
      KSP="$candidate"
      break
    fi
  done
fi

if [[ -z "$KSP" ]]; then
  echo "[R-CORE-02 HARNESS] ERROR: KSP directory not found." >&2
  echo "Usage: bash Tools/aeris53_r_core_02_runtime_harness_build_install.sh '/path/to/Kerbal Space Program'" >&2
  exit 1
fi

SRC="$ROOT/TESTS/RCORE02RuntimeHarness/AERIS53RCore02RuntimeHarness.cs"
AERIS_DLL="$KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
MANAGED="$KSP/KSP_x64_Data/Managed"
OUT_DIR="$ROOT/TESTS/RCORE02RuntimeHarness/bin"
OUT_DLL="$OUT_DIR/AERIS53RCore02RuntimeHarness.dll"
INSTALL_DIR="$KSP/GameData/AERIS53RCore02Harness/Plugins"
INSTALL_DLL="$INSTALL_DIR/AERIS53RCore02RuntimeHarness.dll"

command -v mcs >/dev/null 2>&1 || {
  echo "[R-CORE-02 HARNESS] ERROR: mcs not found; install mono-devel." >&2
  exit 1
}

for required in \
  "$SRC" \
  "$AERIS_DLL" \
  "$MANAGED/Assembly-CSharp.dll" \
  "$MANAGED/UnityEngine.dll" \
  "$MANAGED/UnityEngine.CoreModule.dll" \
  "$MANAGED/UnityEngine.IMGUIModule.dll"
do
  if [[ ! -f "$required" ]]; then
    echo "[R-CORE-02 HARNESS] ERROR: required file missing: $required" >&2
    exit 1
  fi
done

mkdir -p "$OUT_DIR" "$INSTALL_DIR"
rm -f "$OUT_DLL" "$INSTALL_DLL"

echo "=== AERIS53 R-CORE-02 runtime harness build ==="
mcs \
  -target:library \
  -optimize+ \
  -langversion:7.2 \
  -out:"$OUT_DLL" \
  -r:"$MANAGED/Assembly-CSharp.dll" \
  -r:"$MANAGED/UnityEngine.dll" \
  -r:"$MANAGED/UnityEngine.CoreModule.dll" \
  -r:"$MANAGED/UnityEngine.IMGUIModule.dll" \
  -r:"$AERIS_DLL" \
  "$SRC"

cp -f "$OUT_DLL" "$INSTALL_DLL"

BUILT_SHA="$(sha256sum "$OUT_DLL" | awk '{print $1}')"
INSTALLED_SHA="$(sha256sum "$INSTALL_DLL" | awk '{print $1}')"

echo "built=$OUT_DLL"
echo "installed=$INSTALL_DLL"
echo "built_sha256=$BUILT_SHA"
echo "installed_sha256=$INSTALLED_SHA"

if [[ "$BUILT_SHA" != "$INSTALLED_SHA" ]]; then
  echo "[R-CORE-02 HARNESS] ERROR: installed harness hash mismatch." >&2
  exit 1
fi

echo "MATCH=YES"
echo "[R-CORE-02 HARNESS] INSTALL GREEN"
echo
echo "Next:"
echo "  1. Start KSP."
echo "  2. Enter Flight with ALT and VEL disarmed."
echo "  3. In 'AERIS53 R-CORE-02 Runtime Test', press 'RUN ALL 4 CASES'."
echo "  4. Expected: RESULT: 4/4 PASS"
echo
echo "Remove after test:"
echo "  rm -rf \"$KSP/GameData/AERIS53RCore02Harness\""
