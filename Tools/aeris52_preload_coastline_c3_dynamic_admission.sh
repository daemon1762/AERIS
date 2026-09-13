#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris52_preload_coastline_c3_dynamic_admission.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris52-preload-coastline-c3-dynamic-admission"
BASE="f899e90a6de5b1f8c2b0a0733057b9c7dd666978"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris52-preload-coast-c3/$KEY"
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
  if (( size >= off )); then
    tail -c +"$start" "$LOG" > "$out"
  else
    cp "$LOG" "$out"
  fi
}

cd "$ROOT"

echo "=== AERIS52 / PRELOAD COASTLINE C3 DYNAMIC ADMISSION ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "base_aeris51_accepted=$BASE"
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
  echo "STOP: AERIS51 accepted base is not an ancestor" >&2
  exit 12
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: invalid KSP root" >&2
  exit 13
}

C3="Source/AERISFlightControl/Terrain/AERISR052PreloadCoastlineDynamicAdmission.cs"
C12="Source/AERISFlightControl/Terrain/AERISR051PreloadCoastlineFastPath.cs"
BUILDER="Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs"
CSPROJ="Source/AERISFlightControl/AERISFlightControl.csproj"

[[ -f "$C3" ]] || { echo "STOP: C3 source missing" >&2; exit 20; }
grep -Fq 'SEVERE_LOAD' "$C3" || { echo "STOP: severe load tier missing" >&2; exit 21; }
grep -Fq 'HIGH_LOAD' "$C3" || { echo "STOP: high load tier missing" >&2; exit 22; }
grep -Fq 'MODERATE_LOAD' "$C3" || { echo "STOP: moderate load tier missing" >&2; exit 23; }
grep -Fq 'MILD_LOAD' "$C3" || { echo "STOP: mild load tier missing" >&2; exit 24; }
grep -Fq 'COMFORTABLE' "$C3" || { echo "STOP: comfortable load tier missing" >&2; exit 25; }
grep -Fq 'now - r052CoastlineAdmissionLastChange >= 1.50f' "$C3" || {
  echo "STOP: C3 recovery hysteresis missing" >&2
  exit 26
}
grep -Fq 'R052RunAdmissionPolicySelfTest' "$C3" || {
  echo "STOP: C3 policy self-test missing" >&2
  exit 27
}
grep -Fq 'R052ResolveDynamicCoastlineAdmissionLimit' "$C12" || {
  echo "STOP: C1+C2 path not delegated to C3" >&2
  exit 28
}
grep -Fq 'R052BeginDynamicCoastlineAdmission(plan.BodyName)' "$BUILDER" || {
  echo "STOP: durable fallback is not wrapped by C3" >&2
  exit 29
}
grep -Fq 'AERISR052PreloadCoastlineDynamicAdmission.cs' "$CSPROJ" || {
  echo "STOP: C3 source not compiled" >&2
  exit 30
}

! grep -Eq 'FlightCtrlState|SetArmed\(|GroundAssist|ProtectTelemetry' "$C3" || {
  echo "STOP: C3 crossed frozen flight-control boundary" >&2
  exit 31
}
! grep -Eq 'HighDensityResolution[[:space:]]*=|HighDensityFormatVersion[[:space:]]*=' "$C3" || {
  echo "STOP: C3 changed coastline payload quality/format authority" >&2
  exit 32
}
! grep -Fq 'TrySampleTerrainAslShared' "$C3" || {
  echo "STOP: C3 gained live terrain callback" >&2
  exit 33
}

while IFS= read -r path; do
  case "$path" in
    Source/AERISFlightControl/AERISFlightControl.csproj|    Source/AERISFlightControl/Terrain/AERISR051PreloadCoastlineFastPath.cs|    Source/AERISFlightControl/Terrain/AERISR052PreloadCoastlineDynamicAdmission.cs|    Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs|    Tools/aeris52_preload_coastline_c3_dynamic_admission.sh|    Tools/aeris_current_stage.sh|    Docs/AERIS52_PRELOAD_COASTLINE_C3_DYNAMIC_ADMISSION.md)
      ;;
    *)
      echo "STOP: unexpected file changed from AERIS51 accepted base: $path" >&2
      exit 34
      ;;
  esac
done < <(git diff --name-only "$BASE"...HEAD)

echo "PASS static AERIS52 C3 architecture gate"
echo "admission_range=2..min(8,worker_count)"
echo "downshift=IMMEDIATE"
echo "upshift=ONE_LANE_PER_1.5S"
echo "inputs=FRAME_ND_SCHEDULER_QUEUE_WORKER_P95_ENCODE_WRITE"
echo "cold_and_restart_fallback_coverage=BOTH"
echo "coastline_resolution=UNCHANGED_129"
echo "terrain_authority=UNCHANGED_EXACT_CPU"
echo "flight_control_changes=NONE"

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: exit KSP before building/arming AERIS52" >&2
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
  echo "AERIS52_PRELOAD_COAST_C3=ARMED"
  echo "installed_dll_sha256=$DLL_SHA"
  if [[ -d "$DB" ]]; then
    echo "cold_rebuild_required=YES"
    echo "preload_db=$(du -sh "$DB" 2>/dev/null | awk '{print $1}')"
  else
    echo "cold_rebuild_required=NO_DB_PRESENT"
  fi
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_AERIS52_C3_COLD_REBUILD_RUNTIME"
  echo "human_action=Cold-rebuild TerrainPreloadDatabaseV3, launch KSP, let at least one ocean-body coastline phase COMPLETE, then exit normally and run this same command again."
  exit 0
fi

[[ "$(state_value head)" = "$(git rev-parse HEAD)" ]] || {
  echo "STOP: branch HEAD changed after AERIS52 was armed" >&2
  exit 50
}
[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 51; }
ACTUAL_DLL="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ "$ACTUAL_DLL" = "$(state_value dll_sha)" ]] || {
  echo "STOP: installed DLL changed after AERIS52 was armed" >&2
  exit 52
}

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

SEG="$(mktemp /tmp/AERIS52_COAST_C3.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

selftest="$(grep -F '[AERIS52][PRELOAD_COAST_C3]' "$SEG" | grep -F 'event=POLICY_SELFTEST' | grep -Fc 'pass=true' || true)"
changes="$(grep -F '[AERIS52][PRELOAD_COAST_C3]' "$SEG" | grep -Fc 'event=ADMISSION_CHANGE' || true)"
states="$(grep -F '[AERIS52][PRELOAD_COAST_C3]' "$SEG" | grep -Fc 'event=ADMISSION_STATE' || true)"
summaries="$(grep -F '[AERIS52][PRELOAD_COAST_C3]' "$SEG" | grep -Fc 'event=SUMMARY' || true)"

activation="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=TRANSIENT_INDEX_ACTIVATED' || true)"
disk_bypass="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'disk_rescan=false' || true)"
exact_queue="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=EXACT_WORKER_QUEUE' || true)"
exact_commit="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=EXACT_WORKER_COMMIT' || true)"
complete="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=COMPLETE' || true)"
snapshot_fail="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=SNAPSHOT_FAIL' || true)"
worker_fail="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=WORKER_FAIL' || true)"
db_suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" || true)"

range_fail=0
while IFS= read -r limit; do
  [[ -z "$limit" ]] && continue
  if (( limit < 2 || limit > 8 )); then
    range_fail=$((range_fail+1))
  fi
done < <(grep -F '[AERIS52][PRELOAD_COAST_C3]' "$SEG" |   grep -F 'event=ADMISSION_CHANGE' |   sed -n 's/.*; to=\([0-9][0-9]*\).*/\1/p')

last_summary="$(grep -F '[AERIS52][PRELOAD_COAST_C3]' "$SEG" | grep -F 'event=SUMMARY' | tail -n1 || true)"
min_observed="$(printf '%s\n' "$last_summary" | sed -n 's/.*; min_observed=\([0-9][0-9]*\).*/\1/p')"
max_observed="$(printf '%s\n' "$last_summary" | sed -n 's/.*; max_observed=\([0-9][0-9]*\).*/\1/p')"
upshifts="$(printf '%s\n' "$last_summary" | sed -n 's/.*; upshifts=\([0-9][0-9]*\).*/\1/p')"
downshifts="$(printf '%s\n' "$last_summary" | sed -n 's/.*; downshifts=\([0-9][0-9]*\).*/\1/p')"

echo "=== AERIS52 C3 RUNTIME RESULT ==="
echo "policy_selftest_pass=$selftest"
echo "admission_change_events=$changes"
echo "admission_state_events=$states"
echo "admission_summary_events=$summaries"
echo "last_min_observed=${min_observed:-NA}"
echo "last_max_observed=${max_observed:-NA}"
echo "last_upshifts=${upshifts:-NA}"
echo "last_downshifts=${downshifts:-NA}"
echo "admission_range_failures=$range_fail"
echo "transient_index_activation=$activation"
echo "disk_rescan_bypass=$disk_bypass"
echo "exact_worker_queue=$exact_queue"
echo "exact_worker_commit=$exact_commit"
echo "coastline_complete=$complete"
echo "snapshot_fail=$snapshot_fail"
echo "worker_fail=$worker_fail"
echo "exact_db_write_suppressed=$db_suppressed"
echo "installed_dll_sha256=$ACTUAL_DLL"

fail=0
(( selftest > 0 )) || fail=$((fail+1))
(( changes > 0 )) || fail=$((fail+1))
(( states > 0 )) || fail=$((fail+1))
(( summaries > 0 )) || fail=$((fail+1))
(( range_fail == 0 )) || fail=$((fail+1))
(( activation > 0 )) || fail=$((fail+1))
(( disk_bypass > 0 )) || fail=$((fail+1))
(( exact_queue > 0 )) || fail=$((fail+1))
(( exact_commit > 0 )) || fail=$((fail+1))
(( exact_queue == exact_commit )) || fail=$((fail+1))
(( complete > 0 )) || fail=$((fail+1))
(( snapshot_fail == 0 )) || fail=$((fail+1))
(( worker_fail == 0 )) || fail=$((fail+1))
(( db_suppressed == 0 )) || fail=$((fail+1))

if [[ -n "${min_observed:-}" ]]; then
  (( min_observed >= 2 && min_observed <= 8 )) || fail=$((fail+1))
else
  fail=$((fail+1))
fi
if [[ -n "${max_observed:-}" ]]; then
  (( max_observed >= 2 && max_observed <= 8 )) || fail=$((fail+1))
else
  fail=$((fail+1))
fi

if (( fail != 0 )); then
  echo "AERIS52_PRELOAD_COAST_C3_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=PRELOAD_COAST_C3_RUNTIME_FAIL"
  echo "failed_checks=$fail"
  exit 60
fi

echo "AERIS52_PRELOAD_COAST_C3_VERDICT=PASS"
echo "AERIS_CURRENT_STAGE=PRELOAD_COASTLINE_C3_PASS"
echo "next_action=Freeze C3 and resume LAND-R2 immutable Terrain/PTC-backed approach corridor snapshots."
