#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris49_terrain_nd_final_runtime_cert.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris49-terrain-nd-env4-unified-producer-hotfix"
FINAL_CANDIDATE="AERIS49_TERRAIN_ND_ENV4_UNIFIED_PRODUCER_FINAL"

GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
PROBE_DONE="$HOME/.cache/AERIS/aeris49-flightfallback-probe/$KEY/done.txt"
STATE_DIR="$HOME/.cache/AERIS/aeris49-terrain-nd-final-cert/$KEY"
STATE="$STATE_DIR/state.txt"

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}
done_value(){
  local key="$1"
  [[ -f "$PROBE_DONE" ]] || return 1
  grep -m1 "^$key=" "$PROBE_DONE" | cut -d= -f2-
}
db_bytes(){ du -sb "$DB" | awk '{print $1}'; }
db_files(){ find "$DB" -type f -printf '.' | wc -c | tr -d ' '; }
segment_from_offset(){
  local off="$1" out="$2" size start
  size="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( size >= off )); then tail -c +"$start" "$LOG" > "$out"; else cp "$LOG" "$out"; fi
}

cd "$ROOT"

echo "=== AERIS49 / TERRAIN_ND FINAL RUNTIME CERTIFICATION ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
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
[[ -f "$TARGET" && -f "$LOG" && -d "$DB" ]] || {
  echo "STOP: installed DLL/log/DB missing" >&2
  exit 12
}
[[ -f "$PROBE_DONE" ]] || {
  echo "STOP: successful FlightFallback Exact probe record missing" >&2
  exit 13
}
[[ "$(done_value AERIS49_FLIGHTFALLBACK_EXACT_PROBE_VERDICT)" = "PASS" ]] || {
  echo "STOP: FlightFallback Exact probe was not accepted" >&2
  exit 14
}
[[ "$(done_value probe_observer_compiled_in_final)" = "false" ]] || {
  echo "STOP: final DLL record says probe observer is still compiled" >&2
  exit 15
}
EXPECTED_DLL="$(done_value AERIS49_UNIFIED_PRODUCER_FINAL_DLL_SHA256)"
ACTUAL_DLL="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ "$ACTUAL_DLL" = "$EXPECTED_DLL" ]] || {
  echo "STOP: installed final DLL SHA mismatch" >&2
  echo "expected=$EXPECTED_DLL" >&2
  echo "actual=$ACTUAL_DLL" >&2
  exit 16
}

grep -Fq 'EXACT_CPU_ALL_PERSISTENT_OWNERS_V2'   Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs || {
  echo "STOP: V2 producer policy missing from source" >&2
  exit 17
}
! grep -Fq 'AERISR049FlightFallbackExactProbeObserver.cs'   Source/AERISFlightControl/AERISFlightControl.csproj || {
  echo "STOP: probe observer unexpectedly canonical" >&2
  exit 18
}

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: exit KSP before arming final runtime certification" >&2
    exit 20
  fi

  rm -rf "$STATE_DIR"
  mkdir -p "$STATE_DIR"
  cat > "$STATE" <<EOFSTATE
dll_sha=$ACTUAL_DLL
log_offset=$(stat -c %s "$LOG")
db_bytes_before=$(db_bytes)
db_files_before=$(db_files)
EOFSTATE

  echo "AERIS49_TERRAIN_ND_FINAL_CERT=ARMED"
  echo "final_dll_sha256=$ACTUAL_DLL"
  echo "probe_observer_compiled=false"
  echo "db_bytes_before=$(state_value db_bytes_before)"
  echo "db_files_before=$(state_value db_files_before)"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_TERRAIN_ND_FINAL_RUNTIME"
  echo "human_action=Launch KSP, load a Kerbin flight, open ND terrain, let terrain become stable, fly/turn briefly, exit normally, then run the same command again."
  exit 0
fi

[[ "$(state_value dll_sha)" = "$ACTUAL_DLL" ]] || {
  echo "STOP: installed DLL changed after final certification was armed" >&2
  exit 21
}
if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

SEG="$(mktemp /tmp/AERIS49_TERRAIN_ND_FINAL.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

identity="$(
  grep -F '[AERIS43][R043_BUILD_IDENTITY]' "$SEG" |
    grep -F -c "candidate=$FINAL_CANDIDATE" || true
)"
probe_events="$(grep -Fc '[AERIS49][FLIGHTFALLBACK_EXACT_PROBE' "$SEG" || true)"
topology_mismatch="$(grep -Fc 'R041_TOPOLOGY_MISMATCH' "$SEG" || true)"
exact_fallback="$(grep -Fc '[AERIS47][R043_EXACT_CPU_FALLBACK]' "$SEG" || true)"
suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" || true)"
env4_transitions="$(
  grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" |
    grep -Fc '; environment_contract=ENV4_EXACTCPU_HYBRID;' || true
)"
telemetry="$(grep -Fc '[CP3_TELEMETRY]' "$SEG" || true)"
resident="$(
  grep -F '[CP3_TELEMETRY]' "$SEG" |
    grep -E -c '; ram=[1-9][0-9]*/' || true
)"
foundation="$(
  grep -F '[CP3_TELEMETRY]' "$SEG" |
    grep -E -c '; foundation_gf=[0-9]+/[1-9][0-9]*;' || true
)"
presentation="$(grep -Fc '[CP3_GATE4C_VIRTUAL_DETAIL]' "$SEG" || true)"
front="$(
  grep -F '[CP3_GATE4C_VIRTUAL_DETAIL]' "$SEG" |
    grep -E -c 'front=(DIRECT|LATCHED)' || true
)"
direct="$(
  grep -F '[CP3_GATE4C_VIRTUAL_DETAIL]' "$SEG" |
    grep -F -c 'front=DIRECT' || true
)"
render_ready="$(
  grep -F '[CP3_GATE4C_VIRTUAL_DETAIL]' "$SEG" |
    grep -E -c '; render_ready=[1-9][0-9]*/' || true
)"

before_bytes="$(state_value db_bytes_before)"
before_files="$(state_value db_files_before)"
after_bytes="$(db_bytes)"
after_files="$(db_files)"

echo "=== AERIS49 TERRAIN_ND FINAL RUNTIME RESULT ==="
echo "candidate_identity_events=$identity"
echo "probe_runtime_events=$probe_events"
echo "r041_topology_mismatch_events=$topology_mismatch"
echo "exact_fallback_events=$exact_fallback"
echo "db_write_suppressed_events=$suppressed"
echo "env4_environment_transitions=$env4_transitions"
echo "cp3_telemetry_events=$telemetry"
echo "resident_ram_events=$resident"
echo "foundation_events=$foundation"
echo "gpu_presentation_events=$presentation"
echo "front_present_events=$front"
echo "direct_front_events=$direct"
echo "render_ready_events=$render_ready"
echo "db_bytes_before=$before_bytes"
echo "db_bytes_after=$after_bytes"
echo "db_files_before=$before_files"
echo "db_files_after=$after_files"
echo "db_growth_allowed=true"
echo "producer_policy=EXACT_CPU_ALL_PERSISTENT_OWNERS_V2"
echo "final_dll_sha256=$ACTUAL_DLL"
echo "probe_observer_compiled=false"

fail=0
(( identity > 0 )) || fail=$((fail+1))
(( probe_events == 0 )) || fail=$((fail+1))
(( topology_mismatch == 0 )) || fail=$((fail+1))
(( exact_fallback == 0 )) || fail=$((fail+1))
(( suppressed == 0 )) || fail=$((fail+1))
(( env4_transitions == 0 )) || fail=$((fail+1))
(( telemetry > 0 )) || fail=$((fail+1))
(( resident > 0 )) || fail=$((fail+1))
(( foundation > 0 )) || fail=$((fail+1))
(( presentation > 0 )) || fail=$((fail+1))
(( front > 0 )) || fail=$((fail+1))
(( direct > 0 )) || fail=$((fail+1))
(( render_ready > 0 )) || fail=$((fail+1))

if (( fail != 0 )); then
  echo "AERIS49_TERRAIN_ND_FINAL_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=TERRAIN_ND_FINAL_RUNTIME_FAIL"
  echo "failed_checks=$fail"
  exit 40
fi

echo "AERIS49_TERRAIN_ND_FINAL_VERDICT=PASS"
echo "AERIS_CURRENT_STAGE=TERRAIN_ND_FINAL_RUNTIME_PASS"
echo "next_action=Freeze AERIS49 TERRAIN_ND accepted state and proceed to NEW_NAV."
