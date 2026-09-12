#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris49_env4_unified_producer_hotfix.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris49-terrain-nd-env4-unified-producer-hotfix"
ACCEPTED_PRELOAD="7f300b125fa7dc06e383dc1464da3af5d0396065"
CANDIDATE="AERIS49_TERRAIN_ND_ENV4_UNIFIED_PRODUCER_HOTFIX"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R049UnifiedProducer.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R049UnifiedProducer.generated.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris49-unified-producer/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup(){ rm -f "$TEMP_PROJECT" "$TEMP_VERSION"; }
trap cleanup EXIT

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}
db_bytes(){ du -sb "$DB" 2>/dev/null | awk '{print $1}'; }
db_files(){ find "$DB" -type f -printf '.' 2>/dev/null | wc -c | tr -d ' '; }
segment_from_offset(){
  local off="$1" out="$2" size start
  size="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( size >= off )); then tail -c +"$start" "$LOG" > "$out"; else cp "$LOG" "$out"; fi
}

static_gate(){
  local fail=0
  local PTC="$PROJECT_DIR/Terrain/AERISR043PreloadPtcPipeline.cs"
  local PROD="$PROJECT_DIR/Terrain/AERISR047ExactCpuProduction.cs"
  local PIPE="$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs"
  local TILES="$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs"
  local BUILDER="$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs"
  local CONTRACTS="$PROJECT_DIR/Terrain/AERISTerrainTileContracts.cs"

  grep -Fq 'request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder &&' "$PTC" || fail=$((fail+1))
  grep -Fq 'request.WorkOwner != AERISTerrainWorkOwner.FlightFallback' "$PTC" || fail=$((fail+1))
  grep -Fq 'R047ExactCpuPolicyExpected' "$PROD" || fail=$((fail+1))
  grep -Fq 'ExactCpuPolicyExpected = exactCpuPolicyExpected' "$PIPE" || fail=$((fail+1))
  grep -Fq 'RuntimeExactCpuPolicyExpected = state.ExactCpuPolicyExpected' "$PIPE" || fail=$((fail+1))
  grep -Fq 'RuntimeExactCpuProduced = R047ExactCpuProductionActive(state)' "$PIPE" || fail=$((fail+1))
  grep -Fq 'RuntimeExactCpuPolicyExpected' "$CONTRACTS" || fail=$((fail+1))
  grep -Fq 'RuntimeExactCpuProduced' "$CONTRACTS" || fail=$((fail+1))
  grep -Fq 'EXACT_CPU_ALL_PERSISTENT_OWNERS_V2' "$TILES" || fail=$((fail+1))
  ! grep -Fq 'EXACT_CPU_IF_RUNTIME_CERTIFIED_V1' "$TILES" || fail=$((fail+1))
  grep -Fq '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$TILES" || fail=$((fail+1))
  grep -Fq '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$BUILDER" || fail=$((fail+1))
  grep -Fq 'work_owner=" + shadow.WorkOwner' "$PTC" || fail=$((fail+1))

  (( fail == 0 )) || {
    echo "STOP: unified producer static gate failed count=$fail" >&2
    exit 15
  }
}

harvest(){
  local off="$1" seg
  seg="$(mktemp /tmp/AERIS49_UNIFIED_PRODUCER.XXXXXX)"
  segment_from_offset "$off" "$seg"

  local identity exact_fallback suppressed flight_exact flight_tiles
  local nd_present transitions
  identity="$(grep -Fc "candidate=$CANDIDATE" "$seg" || true)"
  exact_fallback="$(grep -Fc '[AERIS47][R043_EXACT_CPU_FALLBACK]' "$seg" || true)"
  suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$seg" || true)"
  flight_exact="$(
    grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$seg" |
      grep -F '; work_owner=FlightFallback;' |
      grep -F '; production_mode=true;' |
      grep -F -c '; production_authority=EXACT_CPU;' || true
  )"
  flight_tiles="$(
    grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$seg" |
      grep -F -c '; work_owner=FlightFallback;' || true
  )"
  nd_present="$(
    grep -F '[CP3_GATE4C_VIRTUAL_DETAIL]' "$seg" |
      grep -E -c 'front=(DIRECT|LATCHED)' || true
  )"
  transitions="$(
    grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" |
      grep -Fc '; environment_contract=ENV4_EXACTCPU_HYBRID;' || true
  )"

  echo "=== AERIS49 UNIFIED PRODUCER RUNTIME RESULT ==="
  echo "candidate_identity_events=$identity"
  echo "flightfallback_ptc_tiles=$flight_tiles"
  echo "flightfallback_exact_cpu_tiles=$flight_exact"
  echo "exact_fallback_events=$exact_fallback"
  echo "db_write_suppressed_events=$suppressed"
  echo "nd_front_present_events=$nd_present"
  echo "env4_environment_transitions=$transitions"
  echo "db_bytes_before=$(state_value db_bytes_before)"
  echo "db_bytes_after=$(db_bytes)"
  echo "db_files_before=$(state_value db_files_before)"
  echo "db_files_after=$(db_files)"
  echo "db_growth_allowed=true"
  echo "producer_policy=EXACT_CPU_ALL_PERSISTENT_OWNERS_V2"
  echo "old_exact_v1_database_preserved=true"

  rm -f "$seg"

  local fail=0
  (( identity > 0 )) || fail=$((fail+1))
  (( flight_tiles > 0 )) || fail=$((fail+1))
  (( flight_exact > 0 )) || fail=$((fail+1))
  (( exact_fallback == 0 )) || fail=$((fail+1))
  (( suppressed == 0 )) || fail=$((fail+1))
  (( nd_present > 0 )) || fail=$((fail+1))

  if (( fail != 0 )); then
    echo "AERIS49_UNIFIED_PRODUCER_VERDICT=FAIL"
    echo "AERIS_CURRENT_STAGE=TERRAIN_ND_UNIFIED_PRODUCER_RUNTIME_FAIL"
    echo "failed_checks=$fail"
    exit 40
  fi

  echo "AERIS49_UNIFIED_PRODUCER_VERDICT=PASS"
  echo "AERIS_CURRENT_STAGE=TERRAIN_ND_UNIFIED_PRODUCER_RUNTIME_PASS"
  echo "next_action=Allow Exact-7 ENV4 policy-V2 rebuild to complete, then rerun TERRAIN_ND runtime certification."
}

cd "$ROOT"

echo "=== AERIS49 / ENV4 UNIFIED PRODUCER HOTFIX ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"

[[ "$(git branch --show-current)" = "$EXPECTED_BRANCH" ]] || { echo "STOP: wrong branch" >&2; exit 10; }
[[ -z "$(git status --porcelain)" ]] || { echo "STOP: worktree dirty" >&2; git status -sb >&2; exit 11; }
git merge-base --is-ancestor "$ACCEPTED_PRELOAD" HEAD || { echo "STOP: accepted PRELOAD_PTC base missing" >&2; exit 12; }
[[ -d "$DB" ]] || { echo "STOP: preload DB missing" >&2; exit 13; }

static_gate
HEAD_SHA="$(git rev-parse HEAD)"

if [[ -f "$STATE" && -f "$TARGET" && -f "$LOG" ]]; then
  if [[ "$(state_value head)" = "$HEAD_SHA" &&
        "$(state_value dll_sha)" = "$(sha256sum "$TARGET" | awk '{print $1}')" ]]; then
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

TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"
cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS49 UNIFIED PRODUCER"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS49 — TERRAIN_ND UNIFIED PRODUCER"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1]); dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
marker='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
replacement=(
    '    <Compile Include="Properties\\AERISBuildVersion.R049UnifiedProducer.generated.cs" />\n'
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
if text.count(marker)!=1: raise SystemExit("version marker not unique")
if 'AERISR043RuntimeBuildIdentityObserver.cs' in text: raise SystemExit("identity observer unexpectedly canonical")
dst.write_text(text.replace(marker,replacement,1),encoding="utf-8")
PY

rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$TEMP_PROJECT")"
)
[[ -f "$DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 30; }

mapfile -t targets < <(find "$GAME_DATA" -type f -name AERISFlightControl.dll -print)
[[ "${#targets[@]}" -eq 1 ]] || { echo "STOP: expected one installed DLL; found=${#targets[@]}" >&2; exit 31; }
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
db_bytes_before=$(db_bytes)
db_files_before=$(db_files)
EOFSTATE

echo "=== AERIS49 UNIFIED PRODUCER INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "dll_backup=$DLL_BACKUP"
echo "db_bytes_before=$(state_value db_bytes_before)"
echo "db_files_before=$(state_value db_files_before)"
echo "producer_policy=EXACT_CPU_ALL_PERSISTENT_OWNERS_V2"
echo "exact_bodies_environment_identity_changes=true"
echo "pqs_bodies_environment_identity_changes=false"
echo "old_database_preserved=true"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_UNIFIED_PRODUCER_RUNTIME"
echo "human_action=Launch KSP. Let preload settle for a while, then load a Kerbin flight, open ND terrain, fly/turn until terrain is stable, exit KSP normally, and run the same command again."
