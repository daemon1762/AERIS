#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris51_preload_coastline_fastpath_c1_c2.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris51-preload-coastline-fastpath-c1-c2"
BASE="bfcc1e62d4f37375e537770c76ed4f6683e120b8"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris51-preload-coast-fast/$KEY"
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

echo "=== AERIS51 / PRELOAD COASTLINE FASTPATH C1+C2 ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "base_land_r1=$BASE"
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
  echo "STOP: LAND R1 base is not an ancestor" >&2
  exit 12
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: invalid KSP root" >&2
  exit 13
}

FAST="Source/AERISFlightControl/Terrain/AERISR051CoastlineExactFastPath.cs"
ORCH="Source/AERISFlightControl/Terrain/AERISR051PreloadCoastlineFastPath.cs"
BUILDER="Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs"
CSPROJ="Source/AERISFlightControl/AERISFlightControl.csproj"

[[ -f "$FAST" && -f "$ORCH" ]] || {
  echo "STOP: AERIS51 coastline fastpath sources missing" >&2
  exit 20
}
grep -Fq 'R051TryCaptureCoastlineExactSnapshot' "$FAST" || {
  echo "STOP: Exact CPU coastline snapshot capture missing" >&2
  exit 21
}
grep -Fq 'BASIS_RECONSTRUCTION_MISMATCH' "$FAST" || {
  echo "STOP: coordinate-basis runtime witness missing" >&2
  exit 22
}
grep -Fq 'R051BuildExactCoastline' "$FAST" || {
  echo "STOP: Exact CPU coastline worker missing" >&2
  exit 23
}
! grep -Fq 'TrySampleTerrainAslShared' "$FAST" || {
  echo "STOP: Exact coastline worker gained live terrain callback" >&2
  exit 24
}
grep -Fq 'TRANSIENT_INDEX_ACTIVATED' "$ORCH" || {
  echo "STOP: FAR transient coastline index missing" >&2
  exit 25
}
grep -Fq 'disk_rescan=false' "$ORCH" || {
  echo "STOP: disk-rescan bypass marker missing" >&2
  exit 26
}
grep -Fq 'EXACT_WORKER_COMMIT' "$ORCH" || {
  echo "STOP: Exact coastline commit marker missing" >&2
  exit 27
}
grep -Fq 'R051RecordFarCoastlineClassification(plan, tile)' "$BUILDER" || {
  echo "STOP: FAR generation classification hook missing" >&2
  exit 28
}
grep -Fq 'R051TryScheduleTransientCoastline(' "$BUILDER" || {
  echo "STOP: transient coastline scheduling hook missing" >&2
  exit 29
}
grep -Fq 'R051TrySubmitExactCpuCoastline(' "$BUILDER" || {
  echo "STOP: Exact CPU coastline submission hook missing" >&2
  exit 30
}
grep -Fq 'AERISR051CoastlineExactFastPath.cs' "$CSPROJ" || {
  echo "STOP: Exact coastline source not compiled" >&2
  exit 31
}
grep -Fq 'AERISR051PreloadCoastlineFastPath.cs' "$CSPROJ" || {
  echo "STOP: coastline orchestration source not compiled" >&2
  exit 32
}

! grep -Eq 'FlightCtrlState|SetArmed\(|GroundAssist|ProtectTelemetry' "$FAST" "$ORCH" || {
  echo "STOP: coastline fastpath crossed frozen flight-control boundary" >&2
  exit 33
}

while IFS= read -r path; do
  case "$path" in
    Source/AERISFlightControl/AERISFlightControl.csproj|Source/AERISFlightControl/Terrain/AERISR051CoastlineExactFastPath.cs|Source/AERISFlightControl/Terrain/AERISR051PreloadCoastlineFastPath.cs|Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs|Tools/aeris51_preload_coastline_fastpath_c1_c2.sh|Tools/aeris_current_stage.sh|Docs/AERIS51_PRELOAD_COASTLINE_FASTPATH_C1_C2.md)
      ;;
    *)
      echo "STOP: unexpected file changed from LAND R1 base: $path" >&2
      exit 34
      ;;
  esac
done < <(git diff --name-only "$BASE"...HEAD)

echo "PASS static AERIS51 coastline architecture gate"
echo "c1_far_classification=INLINE_TRANSIENT_INDEX"
echo "c1_restart_policy=FALLBACK_TO_ACCEPTED_DISK_SCAN"
echo "c2_exact_worker=IMMUTABLE_PRIMITIVES_ONLY"
echo "c2_exact_parallel_limit=MIN_8_OR_WORKER_COUNT"
echo "legacy_nonexact_limit=2"
echo "flight_control_changes=NONE"

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: exit KSP before building/arming AERIS51" >&2
    exit 40
  fi

  AERIS_PRELOAD_BRANCH="$EXPECTED_BRANCH" \
    bash Tools/AERIS_preload_build_and_go.sh \
    "$([[ "$KSP" == "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" ]] && echo laptop || echo desktop)"

  [[ -f "$TARGET" ]] || {
    echo "STOP: installed DLL missing" >&2
    exit 41
  }
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
  echo "AERIS51_PRELOAD_COAST_FASTPATH=ARMED"
  echo "installed_dll_sha256=$DLL_SHA"
  if [[ -d "$DB" ]]; then
    echo "cold_rebuild_required=YES"
    echo "preload_db=$(du -sh "$DB" 2>/dev/null | awk '{print $1}')"
  else
    echo "cold_rebuild_required=NO_DB_PRESENT"
  fi
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_AERIS51_COLD_REBUILD_RUNTIME"
  echo "human_action=Cold-rebuild TerrainPreloadDatabaseV3, launch KSP, let Kerbin reach the HD coastline phase and preferably COMPLETE, then exit normally and run this same command again."
  exit 0
fi

[[ "$(state_value head)" = "$(git rev-parse HEAD)" ]] || {
  echo "STOP: branch HEAD changed after AERIS51 was armed" >&2
  exit 50
}
[[ -f "$TARGET" ]] || {
  echo "STOP: installed DLL missing" >&2
  exit 51
}
ACTUAL_DLL="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ "$ACTUAL_DLL" = "$(state_value dll_sha)" ]] || {
  echo "STOP: installed DLL changed after AERIS51 was armed" >&2
  exit 52
}

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

SEG="$(mktemp /tmp/AERIS51_COAST_FAST.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

activation="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=TRANSIENT_INDEX_ACTIVATED' || true)"
disk_bypass="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'disk_rescan=false' || true)"
exact_queue="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=EXACT_WORKER_QUEUE' || true)"
exact_commit="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=EXACT_WORKER_COMMIT' || true)"
complete="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=COMPLETE' || true)"
snapshot_fail="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=SNAPSHOT_FAIL' || true)"
worker_fail="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -Fc 'event=WORKER_FAIL' || true)"
db_suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" || true)"
phase_elapsed="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -F 'event=COMPLETE' | tail -n1 | sed -n 's/.*; elapsed_s=\([^;]*\).*/\1/p' || true)"
last_worker_ms="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -F 'event=EXACT_WORKER_COMMIT' | tail -n1 | sed -n 's/.*; worker_ms=\([^;]*\).*/\1/p' || true)"

echo "=== AERIS51 COASTLINE RUNTIME RESULT ==="
echo "transient_index_activation=$activation"
echo "disk_rescan_bypass=$disk_bypass"
echo "exact_worker_queue=$exact_queue"
echo "exact_worker_commit=$exact_commit"
echo "coastline_complete=$complete"
echo "snapshot_fail=$snapshot_fail"
echo "worker_fail=$worker_fail"
echo "exact_db_write_suppressed=$db_suppressed"
echo "coastline_phase_elapsed_s=${phase_elapsed:-NA}"
echo "last_exact_worker_ms=${last_worker_ms:-NA}"
echo "installed_dll_sha256=$ACTUAL_DLL"

if (( snapshot_fail > 0 || worker_fail > 0 || db_suppressed > 0 )); then
  echo "AERIS51_PRELOAD_COAST_FASTPATH_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=PRELOAD_COAST_FASTPATH_RUNTIME_FAIL"
  exit 60
fi

if (( activation <= 0 || disk_bypass <= 0 || exact_queue <= 0 || exact_commit <= 0 )); then
  echo "AERIS51_PRELOAD_COAST_FASTPATH_VERDICT=INCOMPLETE"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_COAST_FASTPATH_EVIDENCE"
  exit 0
fi

if (( complete <= 0 )); then
  echo "AERIS51_PRELOAD_COAST_FASTPATH_VERDICT=RUNNING_OK"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_COASTLINE_COMPLETE"
  exit 0
fi

echo "AERIS51_PRELOAD_COAST_FASTPATH_VERDICT=PASS"
echo "AERIS_CURRENT_STAGE=PRELOAD_COAST_FASTPATH_C1_C2_PASS"
echo "next_action=Freeze the coastline optimization, then resume LAND-R2 immutable Terrain/PTC-backed approach corridor snapshots."
