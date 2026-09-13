#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris50_land_r1_approach_runtime_foundation.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris50-land-r1-approach-runtime-foundation"
BASE="bd04f97b2a3850746a75da9c46ba3470360e80d9"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris50-land-r1/$KEY"
STATE="$STATE_DIR/state.txt"

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}
segment_from_offset(){
  local off="$1" out="$2" size start
  [[ -f "$LOG" ]] || { : > "$out"; return; }
  size="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( size >= off )); then tail -c +"$start" "$LOG" > "$out"; else cp "$LOG" "$out"; fi
}

cd "$ROOT"

echo "=== AERIS50 / LAND R1 APPROACH RUNTIME FOUNDATION ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "base_land_r0=$BASE"
echo "KSP=$KSP"

[[ "$(git branch --show-current)" = "$EXPECTED_BRANCH" ]] || {
  echo "STOP: wrong branch" >&2
  exit 10
}
[[ -z "$(git status --porcelain)" ]] || {
  echo "STOP: worktree dirty" >&2
  git status -sb >&2
  exit 11
}
git merge-base --is-ancestor "$BASE" HEAD || {
  echo "STOP: LAND R0 base is not an ancestor" >&2
  exit 12
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: invalid KSP root" >&2
  exit 13
}

BOOT="Source/AERISFlightControl/Core/AERISBootstrap.cs"
MODEL="Source/AERISFlightControl/Landing/AERISApproachModels.cs"
REG="Source/AERISFlightControl/Landing/AERISApproachRegistry.cs"
PLAN="Source/AERISFlightControl/Landing/AERISAdaptiveApproachPlanner.cs"
WIN="Source/AERISFlightControl/UI/AERISWindow.cs"
ND="Source/AERISFlightControl/UI/AERISNavigationDisplay.cs"
LAND="Source/AERISFlightControl/Landing/AERISLandingFoundation.cs"

grep -Fq 'internal AERISApproachRegistry Approaches' "$BOOT" || { echo "STOP: runtime registry property missing" >&2; exit 20; }
grep -Fq 'Approaches=new AERISApproachRegistry()' "$BOOT" || { echo "STOP: runtime registry instantiation missing" >&2; exit 21; }
grep -Fq 'Approaches.Rebuild(Airfields.Airfields,null,approachPlanningLimits)' "$BOOT" || { echo "STOP: runtime rebuild hook missing" >&2; exit 22; }
grep -Fq '[LAND_R1][APPROACH_REGISTRY]' "$BOOT" || { echo "STOP: R1 runtime marker missing" >&2; exit 23; }
grep -Fq 'GlideAngleStepDeg = 0.10' "$MODEL" || { echo "STOP: historical 0.1 degree glide step not restored" >&2; exit 24; }
grep -Fq 'Procedure Registry:' "$WIN" || { echo "STOP: LAND procedure registry UI missing" >&2; exit 25; }
grep -Fq 'APPROACH SURVEY PENDING' "$PLAN" || { echo "STOP: fail-closed pending procedure missing" >&2; exit 26; }
grep -Fq 'Terrain/obstacle corridor snapshot is pending.' "$PLAN" || { echo "STOP: corridor pending reason missing" >&2; exit 27; }

! grep -Eq 'FlightCtrlState[[:space:]]+(state|ctrl|fcs)([^A-Za-z_]|$)' "$REG" || { echo "STOP: Approach Registry gained FlightCtrlState writer" >&2; exit 28; }
! grep -Eq 'FlightCtrlState[[:space:]]+(state|ctrl|fcs)([^A-Za-z_]|$)' "$PLAN" || { echo "STOP: Adaptive Approach gained FlightCtrlState writer" >&2; exit 29; }
! grep -Fq 'SetArmed(' "$REG" || { echo "STOP: Approach Registry gained AP arm authority" >&2; exit 30; }
! grep -Fq 'SetArmed(' "$PLAN" || { echo "STOP: Adaptive Approach gained AP arm authority" >&2; exit 31; }
grep -Fq 'get { return "PILOT"; }' "$LAND" || { echo "STOP: LAND control owner is no longer PILOT" >&2; exit 32; }
grep -Fq 'This class never writes FlightCtrlState or arms an AP director.' "$ND" || { echo "STOP: ND display-only invariant missing" >&2; exit 33; }

echo "PASS static R1 architecture gate"
echo "approach_registry=RUNTIME_CONNECTED"
echo "corridor_snapshot_policy=FAIL_CLOSED_PENDING"
echo "glide_step_deg=0.10"
echo "land_control_authority=NONE"

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: exit KSP before building/arming LAND R1" >&2
    exit 40
  fi

  AERIS_PRELOAD_BRANCH="$EXPECTED_BRANCH"     bash Tools/AERIS_preload_build_and_go.sh     "$([[ "$KSP" == "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" ]] && echo laptop || echo desktop)"

  [[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 41; }
  DLL_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
  LOG_OFFSET=0
  [[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"

  rm -rf "$STATE_DIR"
  mkdir -p "$STATE_DIR"
  cat > "$STATE" <<EOFSTATE
head=$(git rev-parse HEAD)
dll_sha=$DLL_SHA
log_offset=$LOG_OFFSET
EOFSTATE

  echo
  echo "AERIS50_LAND_R1=ARMED"
  echo "installed_dll_sha256=$DLL_SHA"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_LAND_R1_RUNTIME"
  echo "human_action=Launch KSP, load a flight, allow the Airfield Registry to finish loading, open AERIS LAND once, then exit normally. Run this same command again."
  exit 0
fi

[[ "$(state_value head)" = "$(git rev-parse HEAD)" ]] || {
  echo "STOP: branch HEAD changed after LAND R1 was armed" >&2
  exit 50
}
[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 51; }
ACTUAL_DLL="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ "$ACTUAL_DLL" = "$(state_value dll_sha)" ]] || {
  echo "STOP: installed DLL changed after LAND R1 was armed" >&2
  exit 52
}
if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

SEG="$(mktemp /tmp/AERIS50_LAND_R1.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

events="$(grep -Fc '[LAND_R1][APPROACH_REGISTRY]' "$SEG" || true)"
pending_events="$(grep -F '[LAND_R1][APPROACH_REGISTRY]' "$SEG" | grep -E -c '[1-9][0-9]* PENDING' || true)"
authority_events="$(grep -F '[LAND_R1][APPROACH_REGISTRY]' "$SEG" | grep -Fc 'authority=DISPLAY_OBSERVATION_ONLY' || true)"
zero_corridor_events="$(grep -F '[LAND_R1][APPROACH_REGISTRY]' "$SEG" | grep -Fc 'corridorSnapshots=0' || true)"

echo "=== AERIS50 LAND R1 RUNTIME RESULT ==="
echo "approach_registry_events=$events"
echo "pending_procedure_events=$pending_events"
echo "display_observation_only_events=$authority_events"
echo "zero_corridor_snapshot_events=$zero_corridor_events"
echo "installed_dll_sha256=$ACTUAL_DLL"

fail=0
(( events > 0 )) || fail=$((fail+1))
(( pending_events > 0 )) || fail=$((fail+1))
(( authority_events > 0 )) || fail=$((fail+1))
(( zero_corridor_events > 0 )) || fail=$((fail+1))

if (( fail != 0 )); then
  echo "AERIS50_LAND_R1_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=LAND_R1_RUNTIME_FAIL"
  echo "failed_checks=$fail"
  exit 60
fi

echo "AERIS50_LAND_R1_VERDICT=PASS"
echo "AERIS_CURRENT_STAGE=LAND_R1_RUNTIME_PASS"
echo "next_action=LAND-R2 build immutable Terrain/PTC-backed approach corridor snapshots; keep procedures fail-closed until corridor completeness is proven."
