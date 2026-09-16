#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris54_preload_producer_coherence_hotfix.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris54-preload-producer-coherence-hotfix"
BASE="d86632ad63625e584cd96a65a35a0b479dd1e661"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris54-producer-coherence/$KEY"
STATE="$STATE_DIR/state.txt"

cd "$ROOT"

echo "=== AERIS54 / PRELOAD PRODUCER-COHERENCE HOTFIX ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "base_aeris53_accepted=$BASE"
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
  echo "STOP: AERIS53 accepted base is not an ancestor" >&2
  exit 12
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || {
  echo "STOP: invalid KSP root" >&2
  exit 13
}

TILE="Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs"
PIPE="Source/AERISFlightControl/Terrain/AERISR043PreloadPtcPipeline.cs"
R047="Source/AERISFlightControl/Terrain/AERISR047ExactCpuProduction.cs"
R051="Source/AERISFlightControl/Terrain/AERISR051PreloadCoastlineFastPath.cs"
BLOCK="Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs"
BUILDER="Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs"

grep -Fq 'PQS_RUNTIME_FALLBACK_V1' "$TILE" || {
  echo "STOP: runtime PQS fallback identity missing" >&2; exit 20; }
grep -Fq 'RefreshRuntimeProducerPolicyForBody' "$TILE" || {
  echo "STOP: runtime producer certification refresh missing" >&2; exit 21; }
grep -Fq 'runtimeProducerCertifiedTopologyHashes' "$TILE" || {
  echo "STOP: topology recertification cache missing" >&2; exit 22; }
grep -Fq 'RuntimeProducerFallbackActiveForBody(body)' "$PIPE" || {
  echo "STOP: pipeline fallback short-circuit missing" >&2; exit 23; }
grep -Fq 'RegisterRuntimeProducerFallbackForBody' "$R047" || {
  echo "STOP: Exact CPU failure propagation missing" >&2; exit 24; }
grep -Fq '!AERISTerrainTileSystem.RuntimeProducerFallbackActiveForBody(body)' "$R051" || {
  echo "STOP: coastline runtime fallback routing missing" >&2; exit 25; }
grep -Fq 'TryCreateR043PtcState(body, request, pqsHash);' "$BLOCK" || {
  echo "STOP: producer-resolution ordering seam missing" >&2; exit 26; }
grep -Fq '[AERIS54][ENV4_STALE_PRODUCER_TILE_DROPPED]' "$BUILDER" || {
  echo "STOP: preload stale-environment write guard missing" >&2; exit 27; }
grep -Fq '[AERIS54][ENV4_STALE_PRODUCER_TILE_DROPPED]' "$TILE" || {
  echo "STOP: flight stale-environment write guard missing" >&2; exit 28; }

echo "PASS static producer-coherence architecture gate"
echo "persistent_identity=HF2_BODY_SCOPE_PLUS_EFFECTIVE_PRODUCER_POLICY"
echo "live_pqs_topology=PERSISTENT_IDENTITY_EXCLUDED_RECERTIFICATION_TRIGGER_ONLY"
echo "runtime_exact_failure=PQS_RUNTIME_FALLBACK_V1_STICKY_PER_PROCESS"
echo "stale_old_environment_writes=FORBIDDEN"
echo "land_control_changes=NONE"

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "STOP: exit KSP before build/install" >&2
  exit 30
fi

CURRENT_HEAD="$(git rev-parse HEAD)"
ARMED_HEAD=""
[[ -f "$STATE" ]] && ARMED_HEAD="$(grep -m1 '^head=' "$STATE" | cut -d= -f2- || true)"

if [[ ! -f "$STATE" || "$ARMED_HEAD" != "$CURRENT_HEAD" ]]; then
  AERIS_PRELOAD_BRANCH="$EXPECTED_BRANCH" \
    bash Tools/AERIS_preload_build_and_go.sh \
    "$([[ "$KSP" == "$HOME/.local/share/Steam/steamapps/common/Kerbal Space Program" ]] && echo laptop || echo desktop)"

  [[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 31; }
  mkdir -p "$STATE_DIR"
  LOG_OFFSET=0
  [[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"
  DLL_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
  cat > "$STATE" <<EOFSTATE
head=$CURRENT_HEAD
dll_sha=$DLL_SHA
log_offset=$LOG_OFFSET
EOFSTATE

  echo "AERIS54_PRODUCER_COHERENCE=ARMED"
  echo "installed_dll_sha256=$DLL_SHA"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_RUNTIME"
  echo "human_action=Launch KSP to the main menu with the runway mods enabled, wait until preload status settles, exit normally, then run this same command again."
  exit 0
fi

[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing" >&2; exit 40; }
DLL_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
EXPECTED_DLL="$(grep -m1 '^dll_sha=' "$STATE" | cut -d= -f2-)"
[[ "$DLL_SHA" = "$EXPECTED_DLL" ]] || {
  echo "STOP: installed DLL changed after arming" >&2
  exit 41
}
if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

OFFSET="$(grep -m1 '^log_offset=' "$STATE" | cut -d= -f2-)"
SEG="$(mktemp /tmp/AERIS54_PRODUCER_COHERENCE.XXXXXX)"
trap 'rm -f "$SEG"' EXIT
if [[ -f "$LOG" ]]; then
  SIZE="$(stat -c %s "$LOG")"
  if (( SIZE >= OFFSET )); then
    tail -c +"$((OFFSET+1))" "$LOG" > "$SEG"
  else
    cp "$LOG" "$SEG"
  fi
else
  : > "$SEG"
fi

fallback_kerbin="$(grep -F '[AERIS54][ENV4_RUNTIME_PRODUCER_FALLBACK]' "$SEG" | grep -F 'body=Kerbin' | grep -Fc 'effective_policy=PQS_RUNTIME_FALLBACK_V1' || true)"
observed_kerbin="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$SEG" | grep -F 'body=Kerbin' | grep -Fc 'producer_policy=PQS_RUNTIME_FALLBACK_V1' || true)"
transition_kerbin="$(grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$SEG" | grep -Fc 'body=Kerbin' || true)"
suppressed_kerbin="$(grep -F '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" | grep -F 'body=Kerbin' | wc -l | tr -d ' ' || true)"
stale_dropped="$(grep -Fc '[AERIS54][ENV4_STALE_PRODUCER_TILE_DROPPED]' "$SEG" || true)"
coast_exact_not_selected="$(grep -F '[AERIS51][PRELOAD_COAST_FAST]' "$SEG" | grep -F 'body=Kerbin' | grep -Fc 'failure=EXACT_CPU_NOT_SELECTED' || true)"
coast_legacy_queue="$(grep -F '[PRELOAD_COAST_HD]' "$SEG" | grep -F 'body=Kerbin' | grep -Fc 'path=LEGACY_BOUNDED' || true)"
exceptions="$(grep -Eic 'AERIS54.*(exception|error)|Exception.*AERISFlightControl' "$SEG" || true)"

echo "=== AERIS54 RUNTIME AUDIT ==="
echo "kerbin_runtime_fallback=$fallback_kerbin"
echo "kerbin_observed_fallback_policy=$observed_kerbin"
echo "kerbin_environment_transitions=$transition_kerbin"
echo "kerbin_db_write_suppressed=$suppressed_kerbin"
echo "kerbin_coast_exact_not_selected=$coast_exact_not_selected"
echo "kerbin_coast_legacy_queue=$coast_legacy_queue"
echo "stale_old_environment_tiles_dropped=$stale_dropped"
echo "suspected_exceptions=$exceptions"
echo "installed_dll_sha256=$DLL_SHA"

fail=0
(( fallback_kerbin >= 1 )) || fail=$((fail+1))
(( observed_kerbin >= 1 )) || fail=$((fail+1))
(( suppressed_kerbin == 0 )) || fail=$((fail+1))
(( coast_exact_not_selected == 0 )) || fail=$((fail+1))
(( coast_legacy_queue >= 1 )) || fail=$((fail+1))
(( exceptions == 0 )) || fail=$((fail+1))

if (( fail != 0 )); then
  echo "AERIS54_PRODUCER_COHERENCE_VERDICT=FAIL"
  echo "failed_checks=$fail"
  echo "=== FALLBACK EVIDENCE ==="
  grep -E '\[AERIS54\]\[ENV4_RUNTIME_PRODUCER_(CERT|FALLBACK)\]|\[AERIS53\]\[ENV4_BODY_SCOPE\]|\[AERIS44\]\[R043_PRELOAD_ENV_TRANSITION\]|\[AERIS49\]\[ENV4_DB_WRITE_SUPPRESSED\]|\[AERIS51\]\[PRELOAD_COAST_FAST\]|\[PRELOAD_COAST_HD\]' "$SEG" | tail -160 || true
  exit 50
fi

echo "AERIS54_PRODUCER_COHERENCE_VERDICT=PASS_CANDIDATE"
echo "AERIS_CURRENT_STAGE=PRODUCER_COHERENCE_HOTFIX_CANDIDATE_PASS"
echo "next_action=If Kerbin remained paused from the pre-hotfix incident, resume it once in the preload UI; then verify progress and freeze this hotfix before rebasing LAND-R2."
