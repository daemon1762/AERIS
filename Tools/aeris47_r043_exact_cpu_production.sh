#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris47_r043_exact_cpu_production.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris47-r043-exact-cpu-production"
CANDIDATE="AERIS47_R043_EXACT_CPU_PRODUCTION"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R047ExactCpuProduction.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R047ExactCpuProduction.generated.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r047-exact-cpu-production/$KEY"
STATE="$STATE_DIR/state.txt"

EXACT_BODIES=(Kerbin Eve Duna Dres Moho Eeloo Minmus)

cleanup(){ rm -f "$TEMP_PROJECT" "$TEMP_VERSION"; }
trap cleanup EXIT

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
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

harvest(){
  local off="$1"
  local seg body exact_selected=0 exact_tile_bodies=0 fallback_count env4_transitions
  seg="$(mktemp /tmp/AERIS47_EXACTCPU.XXXXXX)"
  segment_from_offset "$off" "$seg"

  echo "=== AERIS47 EXACT CPU RAW EVIDENCE ==="
  grep -E '\[AERIS47\]\[R043_EXACT_CPU_(PRODUCER|FALLBACK)\]|\[AERIS43\]\[R043_PRELOAD_PTC_INTEGRATED_TILE\]|R043_PRELOAD_ENV_TRANSITION' "$seg" | tail -n 160 || true

  echo
  echo "=== AERIS47 EXACT CPU SUMMARY ==="
  for body in "${EXACT_BODIES[@]}"; do
    if grep -F '[AERIS47][R043_EXACT_CPU_PRODUCER]' "$seg" |
       grep -F "; body=$body;" |
       grep -F '; selected=EXACT_CPU;' >/dev/null; then
      exact_selected=$((exact_selected+1))
      echo "SELECTED body=$body producer=EXACT_CPU"
    fi
    if grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$seg" |
       grep -F "; body=$body;" |
       grep -F '; production_mode=true;' |
       grep -F '; tile_commit_authority=EXACT_CPU;' |
       grep -F '; exact_cpu_db_write=true;' >/dev/null; then
      exact_tile_bodies=$((exact_tile_bodies+1))
      echo "DB_WRITE body=$body authority=EXACT_CPU"
    fi
  done

  fallback_count="$(grep -Fc '[AERIS47][R043_EXACT_CPU_FALLBACK]' "$seg" || true)"
  env4_transitions="$(
    grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" |
    grep -Fc '; environment_contract=ENV4_EXACTCPU_HYBRID;' || true
  )"

  echo "exact_selected_bodies=$exact_selected"
  echo "exact_db_write_bodies=$exact_tile_bodies"
  echo "exact_fallback_events=$fallback_count"
  echo "env4_environment_transitions=$env4_transitions"
  echo "producer_switch=true"
  echo "environment_contract=ENV4_EXACTCPU_HYBRID"

  rm -f "$seg"

  if (( fallback_count > 0 )); then
    echo "AERIS47_PRODUCER_VERDICT=FAIL_CLOSED_TO_PQS"
    echo "AERIS_CURRENT_STAGE=EXACT_CPU_FALLBACK_DETECTED"
    exit 41
  fi
  if (( exact_tile_bodies == 7 )); then
    echo "AERIS47_PRODUCER_VERDICT=FULL_7_BODY_PASS"
    echo "AERIS_CURRENT_STAGE=EXACT_CPU_PRODUCTION_ACTIVE"
    exit 0
  fi
  if (( exact_tile_bodies > 0 )); then
    echo "AERIS47_PRODUCER_VERDICT=PARTIAL_ACTIVE"
    echo "AERIS_CURRENT_STAGE=EXACT_CPU_PRODUCTION_ACTIVE_PARTIAL"
    echo "human_action=Keep Automatic Preload running longer, exit KSP, then run the same command again."
    exit 0
  fi

  echo "AERIS47_PRODUCER_VERDICT=WAITING_FOR_EXACT_TILE"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
  echo "human_action=Launch KSP to Main Menu with Automatic Preload enabled, wait for preload work, exit KSP, then run the same command again."
}

cd "$ROOT"

echo "=== AERIS47 / R043 EXACT CPU PRODUCTION ==="
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

grep -Fq 'ProducerSwitchEnabled = true'   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs || exit 12
grep -Fq 'AERIS_TERRAIN_ENV4_EXACTCPU_HYBRID'   Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs || exit 13
grep -Fq 'R047TryCaptureExactCpuProductionSample'   Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs || exit 14
grep -Fq 'AERISR047ExactCpuProduction.cs'   Source/AERISFlightControl/AERISFlightControl.csproj || exit 15

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

TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS47 EXACT CPU PRODUCTION"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS47 — R043 EXACT CPU PRODUCTION"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
v='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
replacement=(
    '    <Compile Include="Properties\\AERISBuildVersion.R047ExactCpuProduction.generated.cs" />\n'
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
if text.count(v)!=1:
    raise SystemExit("version marker not unique")
if 'AERISR043RuntimeBuildIdentityObserver.cs' in text:
    raise SystemExit("identity observer unexpectedly canonical")
text=text.replace(v,replacement,1)
dst.write_text(text,encoding="utf-8")
PY

rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$TEMP_PROJECT")"
)
[[ -f "$DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 30; }

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
DB_BYTES="$(du -sb "$DB" 2>/dev/null | awk '{print $1}' || echo 0)"
DB_FILES="$(find "$DB" -type f 2>/dev/null | wc -l | tr -d ' ')"

cat > "$STATE" <<EOFSTATE
head=$HEAD_SHA
dll_sha=$NEW_SHA
log_offset=$OFFSET
db_bytes_before=$DB_BYTES
db_files_before=$DB_FILES
EOFSTATE

echo "=== AERIS47 EXACT CPU PRODUCTION INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "dll_backup=$DLL_BACKUP"
echo "db_bytes_before=$DB_BYTES"
echo "db_files_before=$DB_FILES"
echo "environment_contract=ENV4_EXACTCPU_HYBRID"
echo "exact_cpu_bodies=Kerbin,Eve,Duna,Dres,Moho,Eeloo,Minmus"
echo "pqs_fallback_bodies=Mun,Ike,Laythe,Vall,Bop,Tylo,Gilly,Pol"
echo "producer_switch=true"
echo "old_environment_chunks_preserved=true"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu with Automatic Preload enabled, wait for preload work, exit KSP, then run the same command again."
