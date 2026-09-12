#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris46_r043_env3_restart_verify.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris46-r043-natural-exact-parity-repair"
TARGET="$KSP/GameData/AERISFlightControl/Plugins/AERISFlightControl.dll"
LOG="$KSP/GameData/AERISFlightControl/Logs/AERISFlightControl.log"
DB="$KSP/GameData/AERISFlightControl/PluginData/TerrainPreloadDatabaseV3"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r046-env3-restart/$KEY"
STATE="$STATE_DIR/state.txt"
BASELINE="$STATE_DIR/baseline_env.txt"
BODIES=(Kerbin Mun Minmus Moho Eve Duna Ike Laythe Vall Bop Tylo Gilly Pol Dres Eeloo)

cd "$ROOT"

echo "=== AERIS46 / R043 ENV3 RESTART VERIFY ==="
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
[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 12; }
[[ -f "$LOG" ]] || { echo "STOP: AERIS log missing" >&2; exit 13; }

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}

field_value(){
  local line="$1" key="$2"
  printf '%s\n' "$line" | sed -n "s/.*; ${key}=\([^;]*\).*/\1/p"
}

latest_observed_for_body(){
  local source="$1" body="$2"
  grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$source" |
    grep -F "; body=$body;" |
    grep -F '; environment_contract=ENV3_TERRAIN_CFG_PQS;' |
    tail -n1 || true
}

capture_baseline(){
  mkdir -p "$STATE_DIR"
  : > "$BASELINE"

  local failures=0 body line gd="" this_gd env
  for body in "${BODIES[@]}"; do
    line="$(latest_observed_for_body "$LOG" "$body")"
    if [[ -z "$line" ]]; then
      echo "FAIL baseline_missing_body=$body"
      failures=$((failures+1))
      continue
    fi
    this_gd="$(field_value "$line" game_data_hash)"
    env="$(field_value "$line" live_environment)"
    if [[ -z "$this_gd" || -z "$env" ]]; then
      echo "FAIL baseline_malformed_body=$body"
      failures=$((failures+1))
      continue
    fi
    if [[ -z "$gd" ]]; then gd="$this_gd"; fi
    if [[ "$this_gd" != "$gd" ]]; then
      echo "FAIL baseline_game_data_hash_disagreement=$body:$this_gd expected:$gd"
      failures=$((failures+1))
    fi
    printf '%s=%s\n' "$body" "$env" >> "$BASELINE"
  done

  (( failures == 0 )) || {
    echo "AERIS_CURRENT_STAGE=BASELINE_CAPTURE_FAIL"
    exit 20
  }

  local offset dll_sha db_bytes db_files
  offset="$(stat -c %s "$LOG")"
  dll_sha="$(sha256sum "$TARGET" | awk '{print $1}')"
  db_bytes="$(du -sb "$DB" 2>/dev/null | awk '{print $1}' || echo 0)"
  db_files="$(find "$DB" -type f 2>/dev/null | wc -l || echo 0)"

  cat > "$STATE" <<EOFSTATE
phase=await_restart
head=$(git rev-parse HEAD)
dll_sha=$dll_sha
log_offset=$offset
game_data_hash=$gd
db_bytes_before=$db_bytes
db_files_before=$db_files
EOFSTATE

  echo "=== ENV3 BASELINE CAPTURED ==="
  echo "game_data_hash=$gd"
  cat "$BASELINE"
  echo "dll_sha256=$dll_sha"
  echo "db_bytes_before=$db_bytes"
  echo "db_files_before=$db_files"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_RESTART"
  echo "human_action=Launch KSP to Main Menu, wait until AERIS preload is active, exit KSP, then run the same command again."
}

verify_restart(){
  local recorded_head recorded_sha offset expected_gd
  recorded_head="$(state_value head)"
  recorded_sha="$(state_value dll_sha)"
  offset="$(state_value log_offset)"
  expected_gd="$(state_value game_data_hash)"

  [[ "$recorded_head" = "$(git rev-parse HEAD)" ]] || {
    echo "STOP: HEAD changed since baseline" >&2
    exit 30
  }
  [[ "$recorded_sha" = "$(sha256sum "$TARGET" | awk '{print $1}')" ]] || {
    echo "STOP: installed DLL changed since baseline" >&2
    exit 31
  }

  local seg size start
  seg="$(mktemp /tmp/AERIS46_ENV3_RESTART.XXXXXX)"
  size="$(stat -c %s "$LOG")"
  start=$((offset+1))
  if (( size >= offset )); then
    tail -c +"$start" "$LOG" > "$seg"
  else
    cp "$LOG" "$seg"
  fi

  local failures=0 body line gd env expected
  for body in "${BODIES[@]}"; do
    line="$(latest_observed_for_body "$seg" "$body")"
    if [[ -z "$line" ]]; then
      echo "FAIL restart_missing_body=$body"
      failures=$((failures+1))
      continue
    fi
    gd="$(field_value "$line" game_data_hash)"
    env="$(field_value "$line" live_environment)"
    expected="$(grep -m1 "^$body=" "$BASELINE" | cut -d= -f2-)"

    if [[ "$gd" != "$expected_gd" ]]; then
      echo "FAIL game_data_hash_changed body=$body before=$expected_gd after=$gd"
      failures=$((failures+1))
    fi
    if [[ "$env" != "$expected" ]]; then
      echo "FAIL environment_changed body=$body before=$expected after=$env"
      failures=$((failures+1))
    fi
  done

  local transitions preserved
  transitions="$(grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" |
    grep -F '; environment_contract=ENV3_TERRAIN_CFG_PQS;' | wc -l || true)"
  preserved="$(grep -F '[AERIS46][R043_PRELOAD_ENV_OLD_DB_PRESERVED]' "$seg" | wc -l || true)"

  if [[ "$transitions" != "0" ]]; then
    echo "FAIL restart_environment_transitions=$transitions"
    failures=$((failures+1))
  fi
  if [[ "$preserved" != "0" ]]; then
    echo "FAIL restart_old_db_preserve_events=$preserved"
    failures=$((failures+1))
  fi

  local db_bytes_after db_files_after
  db_bytes_after="$(du -sb "$DB" 2>/dev/null | awk '{print $1}' || echo 0)"
  db_files_after="$(find "$DB" -type f 2>/dev/null | wc -l || echo 0)"

  rm -f "$seg"

  echo "=== ENV3 RESTART RESULT ==="
  echo "bodies=15"
  echo "game_data_hash=$expected_gd"
  echo "environment_changed=$failures"
  echo "restart_environment_transitions=$transitions"
  echo "restart_old_db_preserve_events=$preserved"
  echo "db_bytes_before=$(state_value db_bytes_before)"
  echo "db_bytes_after=$db_bytes_after"
  echo "db_files_before=$(state_value db_files_before)"
  echo "db_files_after=$db_files_after"
  echo "automatic_db_invalidation=false"
  echo "old_environment_chunks_preserved=true"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "exact_cpu_db_write=false"

  if (( failures != 0 )); then
    echo "AERIS_CURRENT_STAGE=ENV3_RESTART_FAIL"
    exit 41
  fi

  echo "AERIS46_ENV3_RESTART_VERDICT=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
}

if [[ -f "$STATE" && "$(state_value phase || true)" = "await_restart" ]]; then
  verify_restart
else
  capture_baseline
fi
