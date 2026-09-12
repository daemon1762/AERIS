#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris49_terrain_nd_runtime_gate.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris49-terrain-nd-integration-audit"
ACCEPTED_SHA="7f300b125fa7dc06e383dc1464da3af5d0396065"

GAME_DATA="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"
DLL="$GAME_DATA/Plugins/AERISFlightControl.dll"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris49-terrain-nd-runtime/$KEY"
STATE="$STATE_DIR/state.txt"

cd "$ROOT"

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}
db_bytes(){ du -sb "$DB" | awk '{print $1}'; }
db_files(){ find "$DB" -type f -printf '.' | wc -c | tr -d ' '; }
db_tree_sha(){
  (
    cd "$DB"
    LC_ALL=C find . -type f -print0 |
      LC_ALL=C sort -z |
      xargs -0 -r sha256sum
  ) | sha256sum | awk '{print $1}'
}
segment_from_offset(){
  local off="$1" out="$2"
  local size start
  size="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( size >= off )); then
    tail -c +"$start" "$LOG" > "$out"
  else
    cp "$LOG" "$out"
  fi
}

echo "=== AERIS49 / TERRAIN_ND RUNTIME GATE ==="
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
git merge-base --is-ancestor "$ACCEPTED_SHA" HEAD || {
  echo "STOP: PRELOAD_PTC accepted base is not an ancestor" >&2
  exit 12
}
[[ -f "$DLL" && -f "$LOG" && -d "$DB" ]] || {
  echo "STOP: installed DLL/log/DB missing" >&2
  exit 13
}

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: exit KSP before arming runtime gate" >&2
    exit 20
  fi
  rm -rf "$STATE_DIR"
  mkdir -p "$STATE_DIR"

  cat > "$STATE" <<EOFSTATE
head=$(git rev-parse HEAD)
dll_sha=$(sha256sum "$DLL" | awk '{print $1}')
log_offset=$(stat -c %s "$LOG")
db_bytes_before=$(db_bytes)
db_files_before=$(db_files)
db_tree_sha_before=$(db_tree_sha)
EOFSTATE

  echo "AERIS49_RUNTIME_GATE=ARMED"
  echo "dll_sha256=$(state_value dll_sha)"
  echo "db_bytes_before=$(state_value db_bytes_before)"
  echo "db_files_before=$(state_value db_files_before)"
  echo "db_tree_sha256_before=$(state_value db_tree_sha_before)"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_TERRAIN_ND_RUNTIME"
  echo "human_action=Launch KSP; load a flight on a solid body; open the ND with terrain enabled; keep it visible until terrain is drawn and stable; fly/turn briefly; exit KSP normally; then run the same command again."
  exit 0
fi

RECORDED_HEAD="$(state_value head)"
RECORDED_DLL="$(state_value dll_sha)"
if [[ "$RECORDED_HEAD" != "$(git rev-parse HEAD)" ]]; then
  echo "STOP: branch HEAD changed after runtime gate was armed" >&2
  exit 21
fi
if [[ "$RECORDED_DLL" != "$(sha256sum "$DLL" | awk '{print $1}')" ]]; then
  echo "STOP: installed DLL changed after runtime gate was armed" >&2
  exit 22
fi
if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

SEG="$(mktemp /tmp/AERIS49_TERRAIN_ND.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

telemetry_lines="$(grep -F '[CP3_TELEMETRY]' "$SEG" || true)"
presentation_lines="$(grep -F '[CP3_GATE4C_VIRTUAL_DETAIL]' "$SEG" || true)"

telemetry_events="$(printf '%s
' "$telemetry_lines" | grep -c . || true)"
presentation_events="$(printf '%s
' "$presentation_lines" | grep -c . || true)"
resident_body_events="$(printf '%s
' "$telemetry_lines" | grep -E -c 'body=[^;]+' || true)"
resident_ram_events="$(printf '%s
' "$telemetry_lines" | grep -E -c '; ram=[1-9][0-9]*/' || true)"
foundation_events="$(printf '%s
' "$telemetry_lines" | grep -E -c '; foundation_gf=[0-9]+/[1-9][0-9]*;' || true)"
front_present_events="$(printf '%s
' "$presentation_lines" | grep -E -c 'front=(DIRECT|LATCHED)' || true)"
direct_front_events="$(printf '%s
' "$presentation_lines" | grep -F -c 'front=DIRECT' || true)"
swap_events="$(printf '%s
' "$presentation_lines" | grep -E -c '; swap=1;' || true)"
render_ready_events="$(printf '%s
' "$presentation_lines" | grep -E -c '; render_ready=[1-9][0-9]*/' || true)"
ready_far_events="$(printf '%s
' "$presentation_lines" | grep -E -c '; ready_gf=[0-9]+/[1-9][0-9]*;' || true)"
fallback_events="$(grep -Fc '[AERIS47][R043_EXACT_CPU_FALLBACK]' "$SEG" || true)"
env4_transitions="$(
  grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" |
    grep -Fc '; environment_contract=ENV4_EXACTCPU_HYBRID;' || true
)"

before_bytes="$(state_value db_bytes_before)"
before_files="$(state_value db_files_before)"
before_tree="$(state_value db_tree_sha_before)"
after_bytes="$(db_bytes)"
after_files="$(db_files)"
after_tree="$(db_tree_sha)"

echo "=== AERIS49 TERRAIN_ND RUNTIME RESULT ==="
echo "cp3_telemetry_events=$telemetry_events"
echo "gpu_presentation_events=$presentation_events"
echo "resident_body_events=$resident_body_events"
echo "resident_ram_events=$resident_ram_events"
echo "foundation_events=$foundation_events"
echo "front_present_events=$front_present_events"
echo "direct_front_events=$direct_front_events"
echo "front_swap_events=$swap_events"
echo "render_ready_events=$render_ready_events"
echo "ready_far_events=$ready_far_events"
echo "exact_fallback_events=$fallback_events"
echo "env4_environment_transitions=$env4_transitions"
echo "db_bytes_before=$before_bytes"
echo "db_bytes_after=$after_bytes"
echo "db_files_before=$before_files"
echo "db_files_after=$after_files"
echo "db_tree_sha256_before=$before_tree"
echo "db_tree_sha256_after=$after_tree"
echo "db_bytes_equal=$([[ "$before_bytes" = "$after_bytes" ]] && echo true || echo false)"
echo "db_files_equal=$([[ "$before_files" = "$after_files" ]] && echo true || echo false)"
echo "db_tree_equal=$([[ "$before_tree" = "$after_tree" ]] && echo true || echo false)"

fail=0
(( telemetry_events > 0 )) || fail=$((fail+1))
(( presentation_events > 0 )) || fail=$((fail+1))
(( resident_body_events > 0 )) || fail=$((fail+1))
(( resident_ram_events > 0 )) || fail=$((fail+1))
(( foundation_events > 0 )) || fail=$((fail+1))
(( front_present_events > 0 )) || fail=$((fail+1))
(( direct_front_events > 0 || swap_events > 0 )) || fail=$((fail+1))
(( render_ready_events > 0 )) || fail=$((fail+1))
(( ready_far_events > 0 )) || fail=$((fail+1))
(( fallback_events == 0 )) || fail=$((fail+1))
(( env4_transitions == 0 )) || fail=$((fail+1))
[[ "$before_bytes" = "$after_bytes" ]] || fail=$((fail+1))
[[ "$before_files" = "$after_files" ]] || fail=$((fail+1))
[[ "$before_tree" = "$after_tree" ]] || fail=$((fail+1))

if (( fail != 0 )); then
  echo "AERIS49_TERRAIN_ND_RUNTIME_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=TERRAIN_ND_RUNTIME_GATE_FAIL"
  echo "failed_checks=$fail"
  exit 40
fi

echo "AERIS49_TERRAIN_ND_RUNTIME_VERDICT=PASS"
echo "AERIS_CURRENT_STAGE=TERRAIN_ND_RUNTIME_GATE_PASS"
echo "next_action=Freeze TERRAIN_ND accepted state and proceed to NEW_NAV."
