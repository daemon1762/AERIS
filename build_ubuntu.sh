#!/usr/bin/env bash
set -euo pipefail
KSP="${1:-${KSP:-$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program}}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/Source/AERISFlightControl"
VERSION_FILE="$ROOT/GameData/AERISFlightControl/AERISFlightControl.version"
GEN="$SRC/Properties/AERISBuildVersion.generated.cs"
SEMVER="$(python3 -c 'import json,sys; v=json.load(open(sys.argv[1]))["VERSION"]; print("%s.%s.%s.%s"%(v["MAJOR"],v["MINOR"],v["PATCH"],v.get("BUILD",0)))' "$VERSION_FILE")"
BASELINE_DISPLAY="AERIS Flight Control v0.18.0.0 DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1 COMPILE HOTFIX 1 MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1 COMPILE HOTFIX 1 AXIS REFERENCE HOTFIX 2 RUNWAY WITNESS ANCHOR SCAN CALIBRATION HOTFIX 3 GENERIC RUNWAY PLACEMENT VERIFICATION MANUAL CALIBRATION FINAL CANDIDATE 3 COMPILE HOTFIX 1 CALIBRATION ROUND-TRIP HOTFIX 1 BIDIRECTIONAL RUNWAY PAIR HOTFIX 1 ND NAVIGATION SNAPSHOT PUBLISH AIRFIELDS UI COLLAPSE HOTFIX 1 RESPONSIVE AIRFIELDS UI LAYOUT RESIZE HOTFIX 1 MANUAL CALIBRATED RUNWAY SEPARATION PRESERVATION HOTFIX 1 MANUAL CALIBRATION REFLECTION HOTFIX 1 MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1 MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1"
# Historical Candidate 12 Compile Hotfix 2 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 12 — DLC DESSERT FIELD-VERIFIED DEFAULT BASELINE — COMPILE HOTFIX 2"
# Historical Candidate 11 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 11 — DLC PLACEHOLDER MANUAL A/B HOTFIX 1"
# Historical Candidate 10 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 10 — AIRFIELD PROVIDER VISIBILITY / DLC LIST HOTFIX 1"
# Historical Candidate 9 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 9 — UI STABILITY / TELEMETRY HOTFIX 1"
# Historical Candidate 8 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 8 — ND PHANTOM RUNWAY / PERFORMANCE HOTFIX 1"
# Historical Candidate 7 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"
DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH PASS 3 CADENCE HOTFIX 1"
printf 'using System.Reflection;\n[assembly: AssemblyVersion("%s")]\n[assembly: AssemblyFileVersion("%s")]\nnamespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "%s"; internal const string Display = "%s"; internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PASS 3 CADENCE HOTFIX 1"; internal const string Cp2FrozenBaselineDisplay = "%s"; } }\n' "$SEMVER" "$SEMVER" "$SEMVER" "$DISPLAY" "$BASELINE_DISPLAY" > "$GEN"
echo "[AERIS] Building $DISPLAY..."
command -v xbuild >/dev/null 2>&1 || { echo "[AERIS] ERROR: xbuild is not installed. Install mono-xbuild/mono-devel first." >&2; exit 1; }
test -f "$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" || { echo "[AERIS] ERROR: KSP reference not found: $KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll" >&2; exit 1; }
test -f "$KSP/KSP_x64_Data/Managed/UnityEngine.UIModule.dll" || { echo "[AERIS] ERROR: UnityEngine.UIModule.dll not found under KSP Managed directory." >&2; exit 1; }
test -f "$KSP/GameData/001_ToolbarControl/Plugins/ToolbarControl.dll" || { echo "[AERIS] ERROR: ToolbarControl.dll not found. Install ToolbarController 0.1.9.12 or newer." >&2; exit 1; }
python3 - "$KSP" <<'PYTC'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1]) / "GameData" / "001_ToolbarControl"
candidates = [root / "ToolbarControl.version", root / "ToolbarController.version"]
for path in candidates:
    if not path.is_file():
        continue
    try:
        data = json.loads(path.read_text())
        v = data.get("VERSION", {})
        version = tuple(int(v.get(k, 0)) for k in ("MAJOR", "MINOR", "PATCH", "BUILD"))
        print("[AERIS] ToolbarController version: %d.%d.%d.%d" % version)
        if version < (0, 1, 9, 12):
            print("[AERIS] WARNING: ToolbarController 0.1.9.12 or newer is recommended for the upstream icon-update memory fix.")
    except Exception as exc:
        print("[AERIS] WARNING: could not parse %s: %s" % (path, exc))
    break
PYTC
# Historical frozen regression entrypoint retained as a traceability marker:
# PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/run_v01800_cp25_acceptance.py"
# CP3.5 Gate 1 acceptance lineage compatibility: run_v01800_cp35_gate1_acceptance.py compatibility marker.
# CP3.5 Gate 3 Candidate 2 Presentation Authority Hotfix 1 keeps the CP3 frozen
# visual path and bounded existing-only refinement, but quarantines temporal output
# from presentation authority until exact FRONT visibility is runtime revalidated.
# Historical Presentation Authority Hotfix 1 full-acceptance marker retained for lineage:
# PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/run_v01800_cp35_gate3_candidate2_presentation_authority_hotfix1_acceptance.py"
# Build Entrypoint Hotfix 2 deliberately excludes SOURCE/PACKAGE manifest hashing from
# the normal compile/install path. Run the explicit full acceptance runner separately
# when distribution-integrity verification is desired.
# Gate 4 explicit distribution audit: Tools/run_v01800_cp35_gate4_cp3_golden_cartographic_quality_candidate2_full_acceptance.py
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/run_v01800_operation_health_pass3_prebuild.py"
cd "$SRC"
xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj
mkdir -p "$ROOT/GameData/AERISFlightControl/Plugins"
cp -f bin/Release/AERISFlightControl.dll "$ROOT/GameData/AERISFlightControl/Plugins/"

# Preserve user-owned settings across source-build installs. Runtime-owned AA
# designs, FlightData, logs and user flight plans remain in place because the
# installer now replaces only package-owned directories instead of deleting the
# whole AERISFlightControl tree.
PRESERVE_DIR="$(mktemp -d)"
cleanup_preserve(){ rm -rf "$PRESERVE_DIR"; }
trap cleanup_preserve EXIT
TARGET="$KSP/GameData/AERISFlightControl"
for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg; do
  if test -f "$TARGET/Config/$USER_CONFIG"; then
    mkdir -p "$PRESERVE_DIR/Config"
    cp -a "$TARGET/Config/$USER_CONFIG" "$PRESERVE_DIR/Config/"
  fi
done

mkdir -p "$TARGET" "$TARGET/FlightPlans" "$TARGET/Airfields"
rm -rf "$TARGET/Plugins" "$TARGET/Icons" "$TARGET/FlightPlans/Defaults" "$TARGET/Airfields/Defaults"
cp -a "$ROOT/GameData/AERISFlightControl/." "$TARGET/"
for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg; do
  if test -f "$PRESERVE_DIR/Config/$USER_CONFIG"; then
    cp -a "$PRESERVE_DIR/Config/$USER_CONFIG" "$TARGET/Config/"
  fi
done
echo "[AERIS] Installed $DISPLAY: $KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
