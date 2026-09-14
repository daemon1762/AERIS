#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris53_env4_body_scoped_fingerprint_hotfix.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris53-env4-body-scoped-terrain-fingerprint-hotfix"
BASE="ddf98dea7ca1f29493520921d627f7de69412d81"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
SESSION_DIR="$GAME_DATA/Logs/Sessions"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris53-env4-body-scope/$KEY"
STATE="$STATE_DIR/state.txt"

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}

segment_from_offset(){
  local off="$1" out="$2" start_line="" session_marker=""
  : "$off"

  # AERISFlightControl.log is append-mode. AERISLogger writes a deterministic
  # "Dedicated logger initialized. session=..." marker at the start of every
  # KSP process. The reliable per-run slice is therefore the tail beginning at
  # the LAST such marker in the main log.
  #
  # Do not rely on file byte offsets across launches, and do not select the
  # newest Sessions/* file by mtime: asynchronous close/flush ordering can make
  # an adjacent session file appear newer than the runtime we are validating.
  [[ -f "$LOG" ]] || { : > "$out"; return; }

  start_line="$(
    grep -nF 'Dedicated logger initialized. session=' "$LOG" |
      tail -n1 | cut -d: -f1 || true
  )"

  if [[ -n "$start_line" ]]; then
    session_marker="$(sed -n "${start_line}p" "$LOG")"
    echo "validation_main_log_start_line=$start_line"
    echo "validation_session_marker=$session_marker"
    tail -n +"$start_line" "$LOG" > "$out"
    return
  fi

  echo "validation_session_marker=MISSING"
  : > "$out"
}

cd "$ROOT"

echo "=== AERIS53 / ENV4 BODY-SCOPED TERRAIN FINGERPRINT HOTFIX ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "base_aeris52_accepted=$BASE"
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
  echo "STOP: AERIS52 accepted base is not an ancestor" >&2
  exit 12
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: invalid KSP root" >&2
  exit 13
}

TILE="Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs"
BUILDER="Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs"
RECOVERY="Tools/aeris53_recover_preload_state.py"
LOGGER="Source/AERISFlightControl/Logging/AERISLogger.cs"

grep -Fq 'internal static string TerrainConfigHashForBody(' "$TILE" || {
  echo "STOP: body-scoped config hash API missing" >&2
  exit 20
}
grep -Fq '            CelestialBody body)' "$TILE" || {
  echo "STOP: body-scoped config hash CelestialBody signature missing" >&2
  exit 20
}
grep -Fq 'BodyEnvironmentFingerprintForBody(' "$TILE" || {
  echo "STOP: body-local authority fingerprint missing" >&2
  exit 21
}
grep -Fq 'SetEnvironmentCompatibilityOverride(' "$TILE" || {
  echo "STOP: legacy environment compatibility bridge missing" >&2
  exit 22
}
grep -Fq 'const int PreloadStateVersion = 7;' "$BUILDER" || {
  echo "STOP: AERIS53 migration state format missing" >&2
  exit 23
}
grep -Fq 'event=LEGACY_IDENTITY_ADOPTED' "$BUILDER" || {
  echo "STOP: legacy identity adoption evidence missing" >&2
  exit 24
}
grep -Fq 'return pqsContext && (bodyContext || kopernicusContext);' "$TILE" || {
  echo "STOP: strict terrain-config classifier missing" >&2
  exit 25
}
grep -Fq 'RunTerrainConfigScopeSelfTest' "$TILE" || {
  echo "STOP: terrain scope self-test missing" >&2
  exit 26
}
grep -Fq '[AERIS53][ENV4_BODY_SCOPE]' "$TILE" || {
  echo "STOP: AERIS53 runtime evidence missing" >&2
  exit 27
}
grep -Fq 'body_config_hash=' "$BUILDER" || {
  echo "STOP: environment audit lacks body config hash" >&2
  exit 28
}
grep -Fq 'ENV4_EXACTCPU_HYBRID_BODY_SCOPED_HF1' "$BUILDER" || {
  echo "STOP: environment contract evidence label missing" >&2
  exit 29
}
[[ -f "$RECOVERY" ]] || {
  echo "STOP: AERIS53 recovery helper missing" >&2
  exit 30
}
grep -Fq 'SKIP_NOT_LEGACY_V6' "$RECOVERY" || {
  echo "STOP: recovery helper is not version-gated" >&2
  exit 31
}
grep -Fq 'plan["EnvironmentHash"] != data.get("live_environment", "")' "$RECOVERY" || {
  echo "STOP: recovery helper fail-closed environment guard missing" >&2
  exit 32
}
grep -Fq 'Write("INFO", "Dedicated logger initialized. session=" + SessionPath);' "$LOGGER" || {
  echo "STOP: logger session-start marker contract missing" >&2
  exit 33
}
grep -Fq 'mainWriter = new AERISAsyncFileChannel(MainPath, true,' "$LOGGER" || {
  echo "STOP: main log append-mode contract missing" >&2
  exit 34
}

while IFS= read -r path; do
  case "$path" in
    Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs|Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs|Tools/aeris53_env4_body_scoped_fingerprint_hotfix.sh|Tools/aeris53_recover_preload_state.py|Tools/aeris_current_stage.sh|Docs/AERIS53_ENV4_BODY_SCOPED_TERRAIN_FINGERPRINT_HOTFIX.md)
      ;;
    *)
      echo "STOP: unexpected file changed from AERIS52 accepted base: $path" >&2
      exit 30
      ;;
  esac
done < <(git diff --name-only "$BASE"...HEAD)

echo "PASS static AERIS53 architecture gate"
echo "terrain_db_format=UNCHANGED"
echo "global_game_data_hash=DIAGNOSTIC_METADATA_ONLY"
echo "environment_config_identity=BODY_SCOPED"
echo "old_environment_chunks=NEVER_AUTO_DELETED"
echo "legacy_state_recovery=FAIL_CLOSED_LOG_PROVEN_ONLY"
echo "flight_control_changes=NONE"

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: exit KSP before building/arming AERIS53" >&2
    exit 40
  fi

  echo
  echo "=== AERIS53 PRE-BUILD STATE RECOVERY ==="
  python3 "$RECOVERY" "$KSP"

  AERIS_PRELOAD_BRANCH="$EXPECTED_BRANCH"     bash Tools/AERIS_preload_build_and_go.sh     "$([[ "$KSP" == "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" ]] && echo laptop || echo desktop)"

  [[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 41; }
  DLL_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
  LOG_OFFSET=0
  [[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"

  rm -rf "$STATE_DIR"
  mkdir -p "$STATE_DIR"
  cat > "$STATE" <<EOFSTATE
phase=FIRST_RUNTIME
head=$(git rev-parse HEAD)
dll_sha=$DLL_SHA
log_offset=$LOG_OFFSET
EOFSTATE

  echo
  echo "AERIS53_ENV4_BODY_SCOPE=ARMED"
  echo "installed_dll_sha256=$DLL_SHA"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_AERIS53_FIRST_RUNTIME"
  echo "human_action=Launch KSP to the main menu, wait until the AERIS preload window has populated, exit normally, then run this same command again."
  exit 0
fi

ARMED_HEAD="$(state_value head)"
CURRENT_HEAD="$(git rev-parse HEAD)"
if [[ "$ARMED_HEAD" != "$CURRENT_HEAD" ]]; then
  if git merge-base --is-ancestor "$ARMED_HEAD" "$CURRENT_HEAD" &&
     [[ -z "$(git diff --name-only "$ARMED_HEAD".."$CURRENT_HEAD" -- Source/AERISFlightControl)" ]]
  then
    echo "AERIS53_VALIDATOR_ONLY_HEAD_ADVANCE=ACCEPTED"
    echo "armed_head=$ARMED_HEAD"
    echo "current_head=$CURRENT_HEAD"
  else
    echo "STOP: compiled source changed after AERIS53 was armed" >&2
    echo "armed_head=$ARMED_HEAD" >&2
    echo "current_head=$CURRENT_HEAD" >&2
    exit 50
  fi
fi
[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 51; }
ACTUAL_DLL="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ "$ACTUAL_DLL" = "$(state_value dll_sha)" ]] || {
  echo "STOP: installed DLL changed after AERIS53 was armed" >&2
  exit 52
}

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

PHASE="$(state_value phase)"
SEG="$(mktemp /tmp/AERIS53_ENV4_SCOPE.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

if [[ "$PHASE" = "FIRST_RUNTIME" ]]; then
  selftest="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -F 'event=SELFTEST' | grep -Fc 'pass=true' || true)"
  migrated="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -Fc 'event=LEGACY_IDENTITY_ADOPTED' || true)"
  fingerprint_changed="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -Fc 'event=BODY_FINGERPRINT_CHANGED' || true)"
  observed="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'body_config_hash=' || true)"
  matches="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'environment_match=true' || true)"
  transitions="$(grep -Fc '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" || true)"
  contract="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'ENV4_EXACTCPU_HYBRID_BODY_SCOPED_HF1' || true)"
  exceptions="$(grep -Eic 'AERIS53.*(exception|error|fail)|Exception.*AERISFlightControl' "$SEG" || true)"

  echo "=== AERIS53 FIRST RUNTIME ==="
  echo "scope_selftest_pass=$selftest"
  echo "legacy_identity_adoptions=$migrated"
  echo "body_fingerprint_changes=$fingerprint_changed"
  echo "environment_observed_with_body_hash=$observed"
  echo "environment_match_true=$matches"
  echo "environment_transitions=$transitions"
  echo "body_scoped_contract_events=$contract"
  echo "suspected_exceptions=$exceptions"

  fail=0
  (( selftest >= 1 )) || fail=$((fail+1))
  (( migrated >= 1 )) || fail=$((fail+1))
  (( fingerprint_changed == 0 )) || fail=$((fail+1))
  (( observed >= 1 )) || fail=$((fail+1))
  (( matches == observed )) || fail=$((fail+1))
  (( transitions == 0 )) || fail=$((fail+1))
  (( contract == observed )) || fail=$((fail+1))
  (( exceptions == 0 )) || fail=$((fail+1))

  if (( fail != 0 )); then
    echo "AERIS53_ENV4_BODY_SCOPE_VERDICT=FAIL_FIRST_RUNTIME"
    echo "failed_checks=$fail"
    exit 60
  fi

  LOG_OFFSET=0
  [[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"
  cat > "$STATE" <<EOFSTATE
phase=STABILITY_RUNTIME
head=$(git rev-parse HEAD)
dll_sha=$ACTUAL_DLL
log_offset=$LOG_OFFSET
EOFSTATE

  echo "AERIS53_FIRST_RUNTIME=PASS"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_STABILITY_RUNTIME"
  echo "human_action=Launch KSP once more without changing GameData, reach the main menu, wait for preload status, exit normally, then run this same command again."
  exit 0
fi

if [[ "$PHASE" = "STABILITY_RUNTIME" ]]; then
  selftest="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -F 'event=SELFTEST' | grep -Fc 'pass=true' || true)"
  migrated="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -Fc 'event=LEGACY_IDENTITY_ADOPTED' || true)"
  fingerprint_changed="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -Fc 'event=BODY_FINGERPRINT_CHANGED' || true)"
  observed="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'body_config_hash=' || true)"
  matches="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'environment_match=true' || true)"
  transitions="$(grep -Fc '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" || true)"
  db_suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" || true)"
  exceptions="$(grep -Eic 'AERIS53.*(exception|error|fail)|Exception.*AERISFlightControl' "$SEG" || true)"

  echo "=== AERIS53 STABILITY RUNTIME ==="
  echo "scope_selftest_pass=$selftest"
  echo "legacy_identity_adoptions=$migrated"
  echo "body_fingerprint_changes=$fingerprint_changed"
  echo "environment_observed=$observed"
  echo "environment_match_true=$matches"
  echo "environment_transitions=$transitions"
  echo "exact_db_write_suppressed=$db_suppressed"
  echo "suspected_exceptions=$exceptions"
  echo "installed_dll_sha256=$ACTUAL_DLL"

  fail=0
  (( selftest >= 1 )) || fail=$((fail+1))
  (( migrated == 0 )) || fail=$((fail+1))
  (( fingerprint_changed == 0 )) || fail=$((fail+1))
  (( observed >= 1 )) || fail=$((fail+1))
  (( matches == observed )) || fail=$((fail+1))
  (( transitions == 0 )) || fail=$((fail+1))
  (( db_suppressed == 0 )) || fail=$((fail+1))
  (( exceptions == 0 )) || fail=$((fail+1))

  if (( fail != 0 )); then
    echo "AERIS53_ENV4_BODY_SCOPE_VERDICT=FAIL"
    echo "failed_checks=$fail"
    exit 70
  fi

  echo "AERIS53_ENV4_BODY_SCOPE_VERDICT=PASS_CANDIDATE"
  echo "AERIS_CURRENT_STAGE=ENV4_BODY_SCOPE_HOTFIX_CANDIDATE_PASS"
  echo "next_action=Review runtime evidence, then accept/freeze AERIS53 before resuming LAND-R2."
  exit 0
fi

echo "STOP: unknown AERIS53 state phase: $PHASE" >&2
exit 80
