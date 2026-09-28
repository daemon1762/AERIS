#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DESKTOP_KSP="$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program"
LAPTOP_KSP="$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program"
KSP="${1:-${KSP:-}}"

if [[ -z "$KSP" ]]; then
  if [[ -f "$DESKTOP_KSP/KSP.x86_64" || -f "$DESKTOP_KSP/KSP_x64.exe" ]]; then
    KSP="$DESKTOP_KSP"
  elif [[ -f "$LAPTOP_KSP/KSP.x86_64" || -f "$LAPTOP_KSP/KSP_x64.exe" ]]; then
    KSP="$LAPTOP_KSP"
  else
    echo "[R-CORE-02] ERROR: KSP installation not found." >&2
    exit 1
  fi
fi

PRE="$ROOT/Tools/run_v01800_operation_health_pass3_prebuild.py"
GEN="$ROOT/Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs"
PRE_BAK="$(mktemp)"
GEN_BAK="$(mktemp)"
cp -a "$PRE" "$PRE_BAK"
cp -a "$GEN" "$GEN_BAK"

restore_tracked_helpers() {
  cp -a "$PRE_BAK" "$PRE"
  cp -a "$GEN_BAK" "$GEN"
  rm -f "$PRE_BAK" "$GEN_BAK"
}
trap restore_tracked_helpers EXIT

echo "=== AERIS53 R-CORE-02 targeted regression ==="
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/TESTS/aeris53_gate3_r_core_02.py" "$ROOT"

echo
echo "=== Carry-forward: R-CORE-01 ==="
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/TESTS/aeris53_gate3_r_core_01.py" "$ROOT"

echo
echo "=== Carry-forward: GAP3-01 ==="
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/selftest_aeris53_gap3_01.py" "$ROOT"

echo
echo "=== Temporary R015-R020 prebuild wiring ==="
python3 - "$PRE" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text(encoding='utf-8')
entries = [
    ("Operation Health R015 Periodic GC Attribution Observer",
     "selftest_v01800_oh_rev35_r015_periodic_gc_attribution_observer.py"),
    ("Operation Health R016 FDR High Rate Diagnostics Isolation",
     "selftest_v01800_oh_rev35_r016_fdr_high_rate_diagnostics_isolation.py"),
    ("Operation Health R017 ND Presentation Stall Observer",
     "selftest_v01800_oh_rev35_r017_nd_presentation_stall_observer.py"),
    ("Operation Health R018 Visible Foundation Presentation Gate Split",
     "selftest_v01800_oh_rev35_r018_visible_foundation_presentation_gate_split.py"),
    ("Operation Health R019 Visible FAR Commit Priority",
     "selftest_v01800_oh_rev35_r019_visible_far_commit_priority.py"),
    ("Operation Health R020 Visible Authority Baseline Stability",
     "selftest_v01900_oh_rev35_r020_visible_authority_baseline_stability.py"),
]
missing = [(label, name) for label, name in entries if name not in text]
if missing:
    marker = "]\nfor label,name in suites:"
    if marker not in text:
        raise SystemExit("[R-CORE-02] prebuild suite insertion marker missing")
    addition = "".join(" ('%s','%s'),\n" % item for item in missing)
    text = text.replace(marker, addition + marker, 1)
    path.write_text(text, encoding='utf-8')
print("[R-CORE-02] temporary prebuild wiring ready: +%d" % len(missing))
PY

echo
echo "=== AERIS build + install ==="
"$ROOT/build_ubuntu.sh" "$KSP"

BUILT="$ROOT/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
INSTALLED="$KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
test -f "$BUILT"
test -f "$INSTALLED"
BUILT_SHA="$(sha256sum "$BUILT" | awk '{print $1}')"
INSTALLED_SHA="$(sha256sum "$INSTALLED" | awk '{print $1}')"
echo
echo "=== R-CORE-02 build/install summary ==="
echo "KSP=$KSP"
echo "BUILT_SHA256=$BUILT_SHA"
echo "INSTALLED_SHA256=$INSTALLED_SHA"
if [[ "$BUILT_SHA" != "$INSTALLED_SHA" ]]; then
  echo "MATCH=NO" >&2
  exit 1
fi
echo "MATCH=YES"
echo "[R-CORE-02] BUILD/INSTALL GREEN"
