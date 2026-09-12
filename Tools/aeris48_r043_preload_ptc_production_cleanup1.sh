#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris48_r043_preload_ptc_production_cleanup1.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris48-r043-preload-ptc-production-cleanup1"
ACCEPTED_SHA="8b9f48a8592dc6e4e2e241b69a65ffde21359991"
CANDIDATE="AERIS48_R043_PTC_PRODUCTION_CLEANUP1"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R048PtcCleanup1.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R048PtcCleanup1.generated.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r048-ptc-cleanup1/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup(){ rm -f "$TEMP_PROJECT" "$TEMP_VERSION"; }
trap cleanup EXIT

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}

db_bytes(){
  du -sb "$DB" 2>/dev/null | awk '{print $1}'
}
db_files(){
  find "$DB" -type f -printf '.' 2>/dev/null | wc -c | tr -d ' '
}
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

static_gate(){
  local fail=0
  grep -Fq 'AERISR043PreloadPtcIntegratedShadow.cs' "$PROJECT" || fail=$((fail+1))
  grep -Fq 'AERISR047ExactCpuProduction.cs' "$PROJECT" || fail=$((fail+1))
  ! grep -Fq 'AERISR043PreloadPtcLiveProof.cs' "$PROJECT" || fail=$((fail+1))
  ! grep -Fq 'R043NoteDurableBatch(payload);'     "$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs" || fail=$((fail+1))
  ! grep -Fq 'ProofRequest'     "$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs" || fail=$((fail+1))
  ! grep -Fq 'r043LiveProof'     "$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs" || fail=$((fail+1))
  ! grep -Fq 'R043RegisterLivePreloadProofStableId'     "$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs" || fail=$((fail+1))
  ! grep -Fq 'R043RequestLivePreloadProof'     "$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs" || fail=$((fail+1))
  ! grep -Fq 'R043LivePreloadProofDurable'     "$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs" || fail=$((fail+1))
  grep -Fq 'R047ShouldUseExactCpuProduction'     "$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs" || fail=$((fail+1))
  grep -Fq 'ProductionElevation = state.SamplingElevation'     "$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs" || fail=$((fail+1))
  grep -Fq 'R047TryCaptureExactCpuProductionSample'     "$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs" || fail=$((fail+1))
  grep -Fq 'ProducerSwitchEnabled = true'     "$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs" || fail=$((fail+1))
  grep -Fq 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID'     "$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs" || fail=$((fail+1))
  (( fail == 0 )) || {
    echo "STOP: cleanup1 static gate failed count=$fail" >&2
    exit 15
  }
}

harvest(){
  local off="$1"
  local seg identity fallback transitions liveproof
  local before_bytes before_files before_tree
  local after_bytes after_files after_tree
  seg="$(mktemp /tmp/AERIS48_CLEANUP1.XXXXXX)"
  segment_from_offset "$off" "$seg"

  before_bytes="$(state_value db_bytes_before)"
  before_files="$(state_value db_files_before)"
  before_tree="$(state_value db_tree_sha_before)"
  after_bytes="$(db_bytes)"
  after_files="$(db_files)"
  after_tree="$(db_tree_sha)"

  identity="$(grep -Fc "candidate=$CANDIDATE" "$seg" || true)"
  fallback="$(grep -Fc '[AERIS47][R043_EXACT_CPU_FALLBACK]' "$seg" || true)"
  transitions="$(
    grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" |
      grep -Fc '; environment_contract=ENV4_EXACTCPU_HYBRID;' || true
  )"
  liveproof="$(
    grep -E -c '\[AERIS43\]\[R043_PRELOAD_PTC_LIVE_(REQUEST|DB_DURABLE)\]' "$seg" || true
  )"

  echo "=== AERIS48 CLEANUP1 RESTART VERIFICATION ==="
  echo "candidate_identity_events=$identity"
  echo "exact_fallback_events=$fallback"
  echo "env4_environment_transitions=$transitions"
  echo "legacy_live_proof_events=$liveproof"
  echo "db_bytes_before=$before_bytes"
  echo "db_bytes_after=$after_bytes"
  echo "db_files_before=$before_files"
  echo "db_files_after=$after_files"
  echo "db_tree_sha256_before=$before_tree"
  echo "db_tree_sha256_after=$after_tree"
  echo "db_bytes_equal=$([[ "$before_bytes" = "$after_bytes" ]] && echo true || echo false)"
  echo "db_files_equal=$([[ "$before_files" = "$after_files" ]] && echo true || echo false)"
  echo "db_tree_equal=$([[ "$before_tree" = "$after_tree" ]] && echo true || echo false)"

  rm -f "$seg"

  if (( identity < 1 )); then
    echo "AERIS48_CLEANUP1_VERDICT=FAIL_NO_BUILD_IDENTITY"
    exit 41
  fi
  if (( fallback != 0 )); then
    echo "AERIS48_CLEANUP1_VERDICT=FAIL_EXACT_FALLBACK"
    exit 42
  fi
  if (( transitions != 0 )); then
    echo "AERIS48_CLEANUP1_VERDICT=FAIL_ENVIRONMENT_TRANSITION"
    exit 43
  fi
  if (( liveproof != 0 )); then
    echo "AERIS48_CLEANUP1_VERDICT=FAIL_LEGACY_LIVE_PROOF_ACTIVE"
    exit 44
  fi
  if [[ "$before_bytes" != "$after_bytes" ||
        "$before_files" != "$after_files" ||
        "$before_tree" != "$after_tree" ]]; then
    echo "AERIS48_CLEANUP1_VERDICT=FAIL_DB_MUTATION"
    exit 45
  fi

  echo "AERIS48_CLEANUP1_VERDICT=PASS"
  echo "AERIS_CURRENT_STAGE=PRELOAD_PTC_PRODUCTION_CLEANUP1_PASS"
  echo "next_action=Audit and rename the remaining R043Shadow production state without changing semantics."
}

cd "$ROOT"

echo "=== AERIS48 / R043 PRELOAD PTC PRODUCTION CLEANUP1 ==="
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
  echo "STOP: AERIS47 accepted base is not an ancestor" >&2
  exit 12
}
[[ -d "$DB" ]] || {
  echo "STOP: active preload DB missing" >&2
  exit 13
}

static_gate

HEAD_SHA="$(git rev-parse HEAD)"

if [[ -f "$STATE" && -f "$TARGET" && -f "$LOG" ]]; then
  RECORDED_HEAD="$(state_value head || true)"
  RECORDED_SHA="$(state_value dll_sha || true)"
  SOURCE_COMPATIBLE=false
  if [[ "$RECORDED_HEAD" = "$HEAD_SHA" ]]; then
    SOURCE_COMPATIBLE=true
  elif [[ -n "$RECORDED_HEAD" ]] &&
       git diff --quiet "$RECORDED_HEAD" HEAD -- Source/AERISFlightControl; then
    SOURCE_COMPATIBLE=true
  fi
  if [[ "$SOURCE_COMPATIBLE" = true &&
        -n "$RECORDED_SHA" &&
        "$(sha256sum "$TARGET" | awk '{print $1}')" = "$RECORDED_SHA" ]]; then
    if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
      echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
      exit 0
    fi
    harvest "$(state_value log_offset)"
    exit 0
  fi
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "STOP: KSP must be fully exited before install" >&2
  exit 20
fi

rm -rf "$STATE_DIR"
mkdir -p "$STATE_DIR"

DB_BYTES_BEFORE="$(db_bytes)"
DB_FILES_BEFORE="$(db_files)"
DB_TREE_BEFORE="$(db_tree_sha)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS48 PTC CLEANUP1"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS48 — R043 PTC PRODUCTION CLEANUP1"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
marker='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
replacement=(
    '    <Compile Include="Properties\\AERISBuildVersion.R048PtcCleanup1.generated.cs" />\n'
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
if text.count(marker)!=1:
    raise SystemExit("version marker not unique")
if 'AERISR043RuntimeBuildIdentityObserver.cs' in text:
    raise SystemExit("identity observer unexpectedly canonical")
dst.write_text(text.replace(marker,replacement,1),encoding="utf-8")
PY

rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$TEMP_PROJECT")"
)
[[ -f "$DLL" ]] || {
  echo "STOP: build returned without DLL" >&2
  exit 30
}

mapfile -t targets < <(find "$GAME_DATA" -type f -name AERISFlightControl.dll -print)
[[ "${#targets[@]}" -eq 1 ]] || {
  echo "STOP: expected one installed DLL; found=${#targets[@]}" >&2
  exit 31
}
TARGET="${targets[0]}"

OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
NEW_SHA="$(sha256sum "$DLL" | awk '{print $1}')"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
mkdir -p "$BACKUP_DIR"
DLL_BACKUP="$BACKUP_DIR/$(date +%Y%m%d-%H%M%S)-$OLD_SHA.AERISFlightControl.dll"
cp -a "$TARGET" "$DLL_BACKUP"
install -m0644 "$DLL" "$TARGET"

OFFSET=0
[[ -f "$LOG" ]] && OFFSET="$(stat -c %s "$LOG")"

cat > "$STATE" <<EOFSTATE
head=$HEAD_SHA
dll_sha=$NEW_SHA
log_offset=$OFFSET
db_bytes_before=$DB_BYTES_BEFORE
db_files_before=$DB_FILES_BEFORE
db_tree_sha_before=$DB_TREE_BEFORE
EOFSTATE

echo "=== AERIS48 CLEANUP1 INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "dll_backup=$DLL_BACKUP"
echo "db_bytes_before=$DB_BYTES_BEFORE"
echo "db_files_before=$DB_FILES_BEFORE"
echo "db_tree_sha256_before=$DB_TREE_BEFORE"
echo "legacy_live_proof_compiled=false"
echo "production_chain_changed=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_RESTART"
echo "human_action=Launch KSP to Main Menu, wait until AERIS preload status settles, exit KSP normally, then run the same command again."
