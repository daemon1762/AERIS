#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris53_env4_body_scoped_fingerprint_hotfix.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "\${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris53-env4-body-scoped-terrain-fingerprint-hotfix"
BASE="ddf98dea7ca1f29493520921d627f7de69412d81"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris53-env4-body-scope/$KEY"
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
  echo "validation_log_offset=$off"
  echo "validation_log_size=$size"
  if (( size >= off )); then
    tail -c +"$start" "$LOG" > "$out"
  else
    echo "validation_log_rotation_or_truncation=true"
    cp "$LOG" "$out"
  fi
}

build_and_arm(){
  local phase="$1"
  echo
  echo "=== AERIS53 PRE-BUILD STATE RECOVERY ==="
  python3 "$RECOVERY" "$KSP"

  AERIS_PRELOAD_BRANCH="$EXPECTED_BRANCH" \
    bash Tools/AERIS_preload_build_and_go.sh \
    "$([[ "$KSP" == "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" ]] && echo laptop || echo desktop)"

  [[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 41; }
  local dll_sha log_offset
  dll_sha="$(sha256sum "$TARGET" | awk '{print $1}')"
  log_offset=0
  [[ -f "$LOG" ]] && log_offset="$(stat -c %s "$LOG")"

  rm -rf "$STATE_DIR"
  mkdir -p "$STATE_DIR"
  cat > "$STATE" <<EOFSTATE
phase=$phase
head=$(git rev-parse HEAD)
dll_sha=$dll_sha
log_offset=$log_offset
EOFSTATE

  echo "AERIS53_ENV4_BODY_SCOPE=ARMED"
  echo "phase=$phase"
  echo "installed_dll_sha256=$dll_sha"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_AERIS53_FIRST_RUNTIME"
  echo "human_action=Launch KSP to the main menu, wait at least 60 seconds after preload status populates, exit normally, then run this same command again."
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
WRITER="Source/AERISFlightControl/Performance/AERISBackgroundFileWriter.cs"

grep -Fq 'internal static string TerrainConfigHashForBody(' "$TILE" || { echo "STOP: body-scoped config hash API missing" >&2; exit 20; }
grep -Fq 'BodyEnvironmentFingerprintForBody(' "$TILE" || { echo "STOP: body-local authority fingerprint missing" >&2; exit 21; }
grep -Fq 'SetEnvironmentCompatibilityOverride(' "$TILE" || { echo "STOP: environment compatibility bridge missing" >&2; exit 22; }
grep -Fq 'const int PreloadStateVersion = 7;' "$BUILDER" || { echo "STOP: state format gate missing" >&2; exit 23; }
grep -Fq 'return pqsContext && (bodyContext || kopernicusContext);' "$TILE" || { echo "STOP: strict terrain config classifier missing" >&2; exit 24; }
grep -Fq 'RunTerrainConfigScopeSelfTest' "$TILE" || { echo "STOP: terrain scope selftest missing" >&2; exit 25; }
grep -Fq 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID_BODY_FP_HF2|' "$TILE" || { echo "STOP: HF2 fingerprint seed missing" >&2; exit 26; }
grep -Fq 'LivePqsTopologyShadowHashForBody' "$TILE" || { echo "STOP: PQS topology shadow diagnostic missing" >&2; exit 27; }
grep -Fq 'event=BODY_FINGERPRINT_SCHEMA_ADOPTED' "$BUILDER" || { echo "STOP: HF1->HF2 schema adoption missing" >&2; exit 28; }
grep -Fq 'ENV4_EXACTCPU_HYBRID_BODY_SCOPED_HF2' "$BUILDER" || { echo "STOP: HF2 environment contract missing" >&2; exit 29; }
grep -Fq 'SUPPORTED_VERSIONS = (6, 7)' "$RECOVERY" || { echo "STOP: recovery version gate missing" >&2; exit 30; }
grep -Fq 'AERIS53_HF1_RUNTIME_PQS_FALSE_TRANSITION' "$RECOVERY" || { echo "STOP: HF1 false-transition recovery missing" >&2; exit 31; }
grep -Fq 'mainWriter = new AERISAsyncFileChannel(MainPath, true,' "$LOGGER" || { echo "STOP: append-log contract missing" >&2; exit 32; }
grep -Fq 'if (File.Exists(command.Path)) File.Copy(command.Path,' "$WRITER" || { echo "STOP: rotate-copy contract missing" >&2; exit 33; }

while IFS= read -r path_changed; do
  case "$path_changed" in
    Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs|Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs|Tools/aeris53_env4_body_scoped_fingerprint_hotfix.sh|Tools/aeris53_recover_preload_state.py|Tools/aeris_current_stage.sh|Docs/AERIS53_ENV4_BODY_SCOPED_TERRAIN_FINGERPRINT_HOTFIX.md)
      ;;
    *)
      echo "STOP: unexpected file changed from AERIS52 accepted base: $path_changed" >&2
      exit 34
      ;;
  esac
done < <(git diff --name-only "$BASE"...HEAD)

echo "PASS static AERIS53 architecture gate"
echo "terrain_db_format=UNCHANGED"
echo "global_game_data_hash=DIAGNOSTIC_METADATA_ONLY"
echo "environment_config_identity=BODY_SCOPED_HF2"
echo "live_pqs_topology=PERSISTENT_AUTHORITY_EXCLUDED_DIAGNOSTIC_ONLY"
echo "old_environment_chunks=NEVER_AUTO_DELETED"
echo "legacy_state_recovery=FAIL_CLOSED_LOG_PROVEN_ONLY"
echo "flight_control_changes=NONE"

if [[ ! -f "$STATE" ]]; then
  pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1 && {
    echo "STOP: exit KSP before building/arming AERIS53" >&2
    exit 40
  }
  build_and_arm "FIRST_RUNTIME"
  exit 0
fi

ARMED_HEAD="$(state_value head)"
CURRENT_HEAD="$(git rev-parse HEAD)"

if [[ "$ARMED_HEAD" != "$CURRENT_HEAD" ]]; then
  git merge-base --is-ancestor "$ARMED_HEAD" "$CURRENT_HEAD" || {
    echo "STOP: current HEAD is not a descendant of armed HEAD" >&2
    exit 50
  }

  SOURCE_DIFF="$(git diff --name-only "$ARMED_HEAD".."$CURRENT_HEAD" -- Source/AERISFlightControl || true)"
  if [[ -z "$SOURCE_DIFF" ]]; then
    echo "AERIS53_VALIDATOR_ONLY_HEAD_ADVANCE=ACCEPTED"
    echo "armed_head=$ARMED_HEAD"
    echo "current_head=$CURRENT_HEAD"
  else
    approved=true
    while IFS= read -r src; do
      [[ -z "$src" ]] && continue
      case "$src" in
        Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs|Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs)
          ;;
        *)
          approved=false
          ;;
      esac
    done <<< "$SOURCE_DIFF"

    if [[ "$approved" = true ]] &&
       grep -Fq 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID_BODY_FP_HF2|' "$TILE"
    then
      pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1 && {
        echo "STOP: exit KSP before re-arming AERIS53 HF2" >&2
        exit 51
      }
      echo "=== AERIS53 HF2 SOURCE REVISION ==="
      echo "armed_head=$ARMED_HEAD"
      echo "current_head=$CURRENT_HEAD"
      echo "source_diff=$(printf '%s' "$SOURCE_DIFF" | tr '\n' ',')"
      build_and_arm "HF2_FIRST_RUNTIME"
      exit 0
    fi

    echo "STOP: unapproved compiled source changed after AERIS53 was armed" >&2
    printf '%s\n' "$SOURCE_DIFF" >&2
    exit 52
  fi
fi

[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 53; }
ACTUAL_DLL="$(sha256sum "$TARGET" | awk '{print $1}')"
[[ "$ACTUAL_DLL" = "$(state_value dll_sha)" ]] || {
  echo "STOP: installed DLL changed after AERIS53 was armed" >&2
  exit 54
}

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

PHASE="$(state_value phase)"
SEG="$(mktemp /tmp/AERIS53_ENV4_SCOPE.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
segment_from_offset "$(state_value log_offset)" "$SEG"

count_event(){
  local marker="$1"
  grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -Fc "$marker" || true
}

selftest="$(grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -F 'event=SELFTEST' | grep -Fc 'pass=true' || true)"
migrated="$(count_event 'event=LEGACY_IDENTITY_ADOPTED')"
schema_adopted="$(count_event 'event=BODY_FINGERPRINT_SCHEMA_ADOPTED')"
fingerprint_changed="$(count_event 'event=BODY_FINGERPRINT_CHANGED')"
observed="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'body_config_hash=' || true)"
matches="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'environment_match=true' || true)"
transitions="$(grep -Fc '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" || true)"
contract="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -Fc 'ENV4_EXACTCPU_HYBRID_BODY_SCOPED_HF2' || true)"
db_suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" || true)"
exceptions="$(grep -Eic 'AERIS53.*(exception|error|fail)|Exception.*AERISFlightControl' "$SEG" || true)"

if [[ "$PHASE" = "FIRST_RUNTIME" || "$PHASE" = "HF2_FIRST_RUNTIME" ]]; then
  echo "=== AERIS53 FIRST RUNTIME ==="
  echo "phase=$PHASE"
  echo "scope_selftest_pass=$selftest"
  echo "legacy_identity_adoptions=$migrated"
  echo "body_fingerprint_schema_adoptions=$schema_adopted"
  echo "body_fingerprint_changes=$fingerprint_changed"
  echo "environment_observed_with_body_hash=$observed"
  echo "environment_match_true=$matches"
  echo "environment_transitions=$transitions"
  echo "body_scoped_hf2_contract_events=$contract"
  echo "suspected_exceptions=$exceptions"

  fail=0
  (( selftest >= 1 )) || fail=$((fail+1))
  if [[ "$PHASE" = "HF2_FIRST_RUNTIME" ]]; then
    (( migrated == 0 )) || fail=$((fail+1))
    (( schema_adopted >= 1 )) || fail=$((fail+1))
  else
    (( migrated >= 1 || schema_adopted >= 1 )) || fail=$((fail+1))
  fi
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
  echo "human_action=Launch KSP once more without changing GameData, wait at least 60 seconds after preload status populates, exit normally, then run this same command again."
  exit 0
fi

if [[ "$PHASE" = "STABILITY_RUNTIME" ]]; then
  echo "=== AERIS53 STABILITY RUNTIME ==="
  echo "scope_selftest_pass=$selftest"
  echo "legacy_identity_adoptions=$migrated"
  echo "body_fingerprint_schema_adoptions=$schema_adopted"
  echo "body_fingerprint_changes=$fingerprint_changed"
  echo "environment_observed=$observed"
  echo "environment_match_true=$matches"
  echo "environment_transitions=$transitions"
  echo "body_scoped_hf2_contract_events=$contract"
  echo "exact_db_write_suppressed=$db_suppressed"
  echo "suspected_exceptions=$exceptions"
  echo "installed_dll_sha256=$ACTUAL_DLL"

  if (( selftest == 0 && observed == 0 && transitions == 0 &&
        migrated == 0 && schema_adopted == 0 &&
        fingerprint_changed == 0 && exceptions == 0 )); then
    LOG_OFFSET=0
    [[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"
    cat > "$STATE" <<EOFSTATE
phase=STABILITY_RUNTIME
head=$(git rev-parse HEAD)
dll_sha=$ACTUAL_DLL
log_offset=$LOG_OFFSET
EOFSTATE
    echo "AERIS53_ENV4_BODY_SCOPE_VERDICT=INCONCLUSIVE_NO_RUNTIME_EVIDENCE"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_FRESH_STABILITY_RUNTIME"
    echo "next_log_offset=$LOG_OFFSET"
    exit 0
  fi

  fail=0
  (( selftest >= 1 )) || fail=$((fail+1))
  (( migrated == 0 )) || fail=$((fail+1))
  (( schema_adopted == 0 )) || fail=$((fail+1))
  (( fingerprint_changed == 0 )) || fail=$((fail+1))
  (( observed >= 1 )) || fail=$((fail+1))
  (( matches == observed )) || fail=$((fail+1))
  (( transitions == 0 )) || fail=$((fail+1))
  (( contract == observed )) || fail=$((fail+1))
  (( db_suppressed == 0 )) || fail=$((fail+1))
  (( exceptions == 0 )) || fail=$((fail+1))

  if (( fail != 0 )); then
    echo "AERIS53_ENV4_BODY_SCOPE_VERDICT=FAIL"
    echo "failed_checks=$fail"
    echo "=== AERIS53 FAILURE DETAIL / BODY FINGERPRINT CHANGES ==="
    grep -F '[AERIS53][ENV4_BODY_SCOPE]' "$SEG" | grep -F 'event=BODY_FINGERPRINT_CHANGED' || true
    echo "=== AERIS53 FAILURE DETAIL / ENVIRONMENT MISMATCHES ==="
    grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -F 'environment_match=false' || true
    echo "=== AERIS53 FAILURE DETAIL / ENVIRONMENT TRANSITIONS ==="
    grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" || true
    exit 70
  fi

  echo "AERIS53_ENV4_BODY_SCOPE_VERDICT=PASS_CANDIDATE"
  echo "AERIS_CURRENT_STAGE=ENV4_BODY_SCOPE_HOTFIX_CANDIDATE_PASS"
  echo "next_action=Review runtime evidence, then accept/freeze AERIS53 before resuming LAND-R2."
  exit 0
fi

echo "STOP: unknown AERIS53 state phase: $PHASE" >&2
exit 80
