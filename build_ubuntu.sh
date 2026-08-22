#!/usr/bin/env bash
set -euo pipefail
KSP="${1:-${KSP:-$HOME/.steam/debian-installation/steamapps/common/Kerbal Space Program}}"
ROOT="$(cd "$(dirname "$0")" && pwd)"
SRC="$ROOT/Source/AERISFlightControl"
VERSION_FILE="$ROOT/GameData/AERISFlightControl/AERISFlightControl.version"
GEN="$SRC/Properties/AERISBuildVersion.generated.cs"
SEMVER="$(python3 -c 'import json,sys; v=json.load(open(sys.argv[1]))["VERSION"]; print("%s.%s.%s.%s"%(v["MAJOR"],v["MINOR"],v["PATCH"],v.get("BUILD",0)))' "$VERSION_FILE")"
CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"
OBSERVER_VARIANT="AERIS26_REV003_OBSERVER_M1"
REV3_5_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001"
REV3_5_R002_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT"
REV3_5_R003_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION"
REV3_5_R004_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT"
REV3_5_R005_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES"
REV3_5_R006_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER"
REV3_5_R006_HOTFIX1="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1"
REV3_5_R006_HOTFIX2="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2"
REV3_5_R006_HOTFIX3="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3"
REV3_5_R006_HOTFIX4="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4"
REV3_5_R007_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION"
REV3_5_R008_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY"
REV3_5_R009_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE"
REV3_5_R010_VARIANT="AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM"
REV3_5_R011_VARIANT="AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER"
REV3_5_R012_VARIANT="AERIS28_REV3_5_SALBUTAMOL_SULFATE_R012_COLD_START_PRELOAD_READY_RECOVERY"
REV3_5_R014_VARIANT="AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE"
REV3_5_R015_VARIANT="AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER"
REV3_5_R016_VARIANT="AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION"
REV3_5_R017_VARIANT="AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER"
REV3_5_R018_VARIANT="AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT"
REV3_5_R019_VARIANT="AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY"
REV3_5_R020_VARIANT="AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY"
SOURCE_GIT_SHA="$(git -C "$ROOT" rev-parse HEAD)"
SOURCE_TREE_SHA256="$(
  {
    printf '%s\n' "$SOURCE_GIT_SHA"
    git -C "$ROOT" diff --no-ext-diff --binary HEAD -- \
      Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs \
      Source/AERISFlightControl/Terrain/AERISCurrentBodyResidentCache.cs \
      Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs \
      Source/AERISFlightControl/Core/AERISBootstrap.cs \
      Source/AERISFlightControl/AERISFlightControl.csproj \
      build_ubuntu.sh
  } | sha256sum | awk '{print $1}'
)"
echo "[AERIS23_CANDIDATE_SOURCE] candidate=$CANDIDATE_NAME; git=$SOURCE_GIT_SHA; source_tree_sha256=$SOURCE_TREE_SHA256"
GPU_SHADER_DIR="$ROOT/GameData/AERISFlightControl/Shaders"
if test -f "$KSP/KSP_x64.exe"; then
  GPU_SHADER_BUNDLE_NAME="aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
elif test -f "$KSP/KSP.x86_64"; then
  GPU_SHADER_BUNDLE_NAME="aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
elif test -f "$GPU_SHADER_DIR/aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"; then
  GPU_SHADER_BUNDLE_NAME="aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
else
  GPU_SHADER_BUNDLE_NAME="aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"
fi
GPU_SHADER_BUNDLE="$GPU_SHADER_DIR/$GPU_SHADER_BUNDLE_NAME"
test -f "$GPU_SHADER_BUNDLE" || {
  echo "[AERIS25 GPU DYNAMIC COLOUR] ERROR: required shader bundle missing: $GPU_SHADER_BUNDLE" >&2
  echo "Run Tools/build_aeris25_gpu_shader_bundle.sh before the normal AERIS build." >&2
  exit 1
}
GPU_SHADER_BUNDLE_SHA256="$(sha256sum "$GPU_SHADER_BUNDLE" | awk '{print $1}')"
echo "[AERIS24_GPU_VERTEX_BUNDLE] name=$GPU_SHADER_BUNDLE_NAME; sha256=$GPU_SHADER_BUNDLE_SHA256"
BASELINE_DISPLAY="AERIS Flight Control v0.18.0.0 DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1 COMPILE HOTFIX 1 MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1 COMPILE HOTFIX 1 AXIS REFERENCE HOTFIX 2 RUNWAY WITNESS ANCHOR SCAN CALIBRATION HOTFIX 3 GENERIC RUNWAY PLACEMENT VERIFICATION MANUAL CALIBRATION FINAL CANDIDATE 3 COMPILE HOTFIX 1 CALIBRATION ROUND-TRIP HOTFIX 1 BIDIRECTIONAL RUNWAY PAIR HOTFIX 1 ND NAVIGATION SNAPSHOT PUBLISH AIRFIELDS UI COLLAPSE HOTFIX 1 RESPONSIVE AIRFIELDS UI LAYOUT RESIZE HOTFIX 1 MANUAL CALIBRATED RUNWAY SEPARATION PRESERVATION HOTFIX 1 MANUAL CALIBRATION REFLECTION HOTFIX 1 MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1 MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1"
# Historical Candidate 12 Compile Hotfix 2 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 12 — DLC DESSERT FIELD-VERIFIED DEFAULT BASELINE — COMPILE HOTFIX 2"
# Historical Candidate 11 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 11 — DLC PLACEHOLDER MANUAL A/B HOTFIX 1"
# Historical Candidate 10 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 10 — AIRFIELD PROVIDER VISIBILITY / DLC LIST HOTFIX 1"
# Historical Candidate 9 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 9 — UI STABILITY / TELEMETRY HOTFIX 1"
# Historical Candidate 8 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 8 — ND PHANTOM RUNWAY / PERFORMANCE HOTFIX 1"
# Historical Candidate 7 identity marker: UiCheckpoint = "DEV CP3 GATE 5 — INTEGRATED ACCEPTANCE CANDIDATE 7 — EXPANSION DETECTION / DLC RUNTIME STATUS HOTFIX 1"
DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION"
printf 'using System.Reflection;\n[assembly: AssemblyVersion("%s")]\n[assembly: AssemblyFileVersion("%s")]\nnamespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "%s"; internal const string Display = "%s"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV003 AUTHORITATIVE PUBLICATION"; internal const string Cp2FrozenBaselineDisplay = "%s"; } }\n' "$SEMVER" "$SEMVER" "$SEMVER" "$DISPLAY" "$BASELINE_DISPLAY" > "$GEN"
python3 - "$GEN" "$CANDIDATE_NAME" "$SOURCE_GIT_SHA" "$SOURCE_TREE_SHA256" <<'PYAERISCANDIDATE'
from pathlib import Path
import sys
path = Path(sys.argv[1])
candidate, git_sha, tree_sha = sys.argv[2:5]
text = path.read_text()
needle = 'internal const string Cp2FrozenBaselineDisplay = '
if needle not in text:
    raise SystemExit('[AERIS23] generated identity insertion anchor missing')
insert = (
    'internal const string CandidateName = "' + candidate + '"; '
    'internal const string SourceGitSha = "' + git_sha + '"; '
    'internal const string SourceTreeSha256 = "' + tree_sha + '"; '
)
text = text.replace(needle, insert + needle, 1)
path.write_text(text)
PYAERISCANDIDATE
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
# AERIS25-1 inherited acceptance is verified before ADENOSINE promotion.
# Phase 5 reasserts all frozen visual/control invariants on the final generated tree.
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris26_rev003_observer.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r011_turning_view_churn_observer.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r014_publication_gated_content_reconcile.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r015_periodic_gc_attribution_observer.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r016_fdr_high_rate_diagnostics_isolation.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r020_visible_authority_baseline_stability.py"
PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/run_v01800_operation_health_pass3_prebuild.py"
cd "$SRC"
xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj
mkdir -p "$ROOT/GameData/AERISFlightControl/Plugins"
cp -f bin/Release/AERISFlightControl.dll "$ROOT/GameData/AERISFlightControl/Plugins/"
BUILT_DLL="$ROOT/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
BUILT_DLL_SHA256="$(sha256sum "$BUILT_DLL" | awk '{print $1}')"
printf 'candidate=%s\ngit=%s\nsource_tree_sha256=%s\nbuilt_dll_sha256=%s\ngpu_shader_bundle=%s\ngpu_shader_bundle_sha256=%s\n' \
  "$CANDIDATE_NAME" "$SOURCE_GIT_SHA" "$SOURCE_TREE_SHA256" "$BUILT_DLL_SHA256" \
  "$GPU_SHADER_BUNDLE_NAME" "$GPU_SHADER_BUNDLE_SHA256" \
  > "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'observer_variant=%s\n' "$OBSERVER_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_variant=%s\n' "$REV3_5_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r002_variant=%s\n' "$REV3_5_R002_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r003_variant=%s\n' "$REV3_5_R003_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r004_variant=%s\n' "$REV3_5_R004_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r005_variant=%s\n' "$REV3_5_R005_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r006_variant=%s\n' "$REV3_5_R006_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r006_hotfix1=%s\n' "$REV3_5_R006_HOTFIX1" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r006_hotfix2=%s\n' "$REV3_5_R006_HOTFIX2" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r006_hotfix3=%s\n' "$REV3_5_R006_HOTFIX3" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r006_hotfix4=%s\n' "$REV3_5_R006_HOTFIX4" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r007_variant=%s\n' "$REV3_5_R007_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r008_variant=%s\n' "$REV3_5_R008_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r009_variant=%s\n' "$REV3_5_R009_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r010_variant=%s\n' "$REV3_5_R010_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r011_variant=%s\n' "$REV3_5_R011_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r012_variant=%s\n' "$REV3_5_R012_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r014_variant=%s\n' "$REV3_5_R014_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r015_variant=%s\n' "$REV3_5_R015_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r016_variant=%s\n' "$REV3_5_R016_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r017_variant=%s\n' "$REV3_5_R017_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r018_variant=%s\n' "$REV3_5_R018_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r019_variant=%s\n' "$REV3_5_R019_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
printf 'rev3_5_r020_variant=%s\n' "$REV3_5_R020_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"
echo "[AERIS23_CANDIDATE_BUILT] candidate=$CANDIDATE_NAME; dll_sha256=$BUILT_DLL_SHA256"

# Preserve user-owned settings across source-build installs. Runtime-owned AA
# designs, FlightData, logs and user flight plans remain in place because the
# installer now replaces only package-owned directories instead of deleting the
# whole AERISFlightControl tree.
PRESERVE_DIR="$(mktemp -d)"
cleanup_preserve(){ rm -rf "$PRESERVE_DIR"; }
trap cleanup_preserve EXIT
TARGET="$KSP/GameData/AERISFlightControl"
for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg AERISOperationHealth.cfg; do
  if test -f "$TARGET/Config/$USER_CONFIG"; then
    mkdir -p "$PRESERVE_DIR/Config"
    cp -a "$TARGET/Config/$USER_CONFIG" "$PRESERVE_DIR/Config/"
  fi
done

mkdir -p "$TARGET" "$TARGET/FlightPlans" "$TARGET/Airfields"
rm -rf "$TARGET/Plugins" "$TARGET/Icons" "$TARGET/FlightPlans/Defaults" "$TARGET/Airfields/Defaults"
cp -a "$ROOT/GameData/AERISFlightControl/." "$TARGET/"
for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg AERISOperationHealth.cfg; do
  if test -f "$PRESERVE_DIR/Config/$USER_CONFIG"; then
    cp -a "$PRESERVE_DIR/Config/$USER_CONFIG" "$TARGET/Config/"
  fi
done
# Operation Health generation identity is package-owned even though the rest of
# AERISOperationHealth.cfg is user-owned policy. Preserve policy values, then promote
# only codename so an older installed config cannot mask EPINEPHRINE.
OH_CONFIG="$TARGET/Config/AERISOperationHealth.cfg"
if test -f "$OH_CONFIG"; then
  python3 - "$OH_CONFIG" <<'PYOH'
from pathlib import Path
import re
import sys
path = Path(sys.argv[1])
text = path.read_text()
updated, count = re.subn(r'(?m)^(\s*codename\s*=\s*).+$', r'\1NOREPINEPHRINE', text, count=1)
if count != 1:
    raise SystemExit("[AERIS] ERROR: Operation Health codename key missing during Phase 6 install promotion")
path.write_text(updated)
PYOH
fi
INSTALLED_DLL="$KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
INSTALLED_DLL_SHA256="$(sha256sum "$INSTALLED_DLL" | awk '{print $1}')"
if [[ "$BUILT_DLL_SHA256" != "$INSTALLED_DLL_SHA256" ]]; then
  echo "[AERIS23] ERROR: installed DLL hash mismatch; built=$BUILT_DLL_SHA256 installed=$INSTALLED_DLL_SHA256" >&2
  exit 1
fi
echo "[AERIS23_CANDIDATE_INSTALLED] candidate=$CANDIDATE_NAME; git=$SOURCE_GIT_SHA; source_tree_sha256=$SOURCE_TREE_SHA256; built_dll_sha256=$BUILT_DLL_SHA256; installed_dll_sha256=$INSTALLED_DLL_SHA256; MATCH=YES"
echo "[AERIS] Installed $DISPLAY: $KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
