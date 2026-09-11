#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris44_r043_preload_environment_hash_stability.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris44-r043-preload-environment-hash-stable-v2"
BASE="8e5adcf87eea3e30fe98e290885b0361ded7ab71"
CANDIDATE="AERIS44_R043_PRELOAD_ENVIRONMENT_HASH_STABLE_V2"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R044Env2.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R044Env2.generated.cs"
IDENTITY="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r044-env2-stability/$KEY"
STATE="$STATE_DIR/state.txt"
FIRST_HASHES="$STATE_DIR/first_hashes.txt"
BODIES=(Kerbin Mun Minmus Moho Eve Duna Ike Laythe Vall Bop Tylo Gilly Pol Dres Eeloo)

cleanup(){ rm -f "$TEMP_PROJECT" "$TEMP_VERSION"; }
trap cleanup EXIT

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^${key}=" "$STATE" | cut -d= -f2-
}

field_value(){
  local line="$1" key="$2"
  printf '%s\n' "$line" | sed -n "s/.*; ${key}=\([^;]*\).*/\1/p"
}

latest_body_line(){
  local seg="$1" body="$2"
  grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$seg" |
    grep -F "; body=$body;" | grep -F '; environment_contract=ENV2_STABLE;' |
    tail -n1 || true
}

harvest_first(){
  local off="$1"
  local seg current start
  seg="$(mktemp /tmp/AERIS44_ENV2_FIRST.XXXXXX)"
  current="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( current >= off )); then tail -c +"$start" "$LOG" > "$seg"; else cp "$LOG" "$seg"; fi

  local missing=0 gd="" line body live this_gd
  : > "$FIRST_HASHES"
  for body in "${BODIES[@]}"; do
    line="$(latest_body_line "$seg" "$body")"
    if [[ -z "$line" ]]; then
      echo "missing_first_body=$body"
      missing=1
      continue
    fi
    live="$(field_value "$line" live_environment)"
    this_gd="$(field_value "$line" game_data_hash)"
    [[ -n "$live" && -n "$this_gd" ]] || {
      echo "malformed_first_body=$body"
      missing=1
      continue
    }
    if [[ -z "$gd" ]]; then gd="$this_gd"; fi
    [[ "$gd" = "$this_gd" ]] || {
      echo "first_gamedata_hash_disagreement=$body:$this_gd expected:$gd"
      missing=1
    }
    printf '%s=%s\n' "$body" "$live" >> "$FIRST_HASHES"
  done

  if (( missing != 0 )); then
    rm -f "$seg" "$FIRST_HASHES"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_FIRST_ENV2_LAUNCH"
    echo "human_action=Launch KSP to Main Menu, wait 30 seconds, exit KSP, then run the same command again."
    return 0
  fi

  local transitions
  transitions="$(awk '/\\[AERIS44\\]\\[R043_PRELOAD_ENV_TRANSITION\\]/ && /environment_contract=ENV2_STABLE/ { c++ } END { print c+0 }' "$seg")"

  echo "=== AERIS44 ENV2 FIRST LAUNCH CAPTURED ==="
  echo "game_data_hash=$gd"
  echo "bodies=${#BODIES[@]}"
  echo "environment_transitions=$transitions"
  cat "$FIRST_HASHES"

  cat > "$STATE.tmp" <<EOFSTATE
phase=await_second
head=$(state_value head)
dll_sha=$(state_value dll_sha)
log_offset=$current
db_backup=$(state_value db_backup)
first_game_data_hash=$gd
EOFSTATE
  mv "$STATE.tmp" "$STATE"
  rm -f "$seg"

  echo "AERIS_CURRENT_STAGE=FIRST_LAUNCH_CAPTURED"
  echo "human_action=Launch KSP again with the same build, wait 30 seconds at Main Menu, exit KSP, then run the same command again."
}

harvest_second(){
  local off="$1"
  local seg current start
  seg="$(mktemp /tmp/AERIS44_ENV2_SECOND.XXXXXX)"
  current="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( current >= off )); then tail -c +"$start" "$LOG" > "$seg"; else cp "$LOG" "$seg"; fi

  local failures=0 body line persisted live match first this_gd
  local first_gd
  first_gd="$(state_value first_game_data_hash)"
  for body in "${BODIES[@]}"; do
    line="$(latest_body_line "$seg" "$body")"
    if [[ -z "$line" ]]; then
      echo "FAIL missing_second_body=$body"
      failures=$((failures+1))
      continue
    fi
    persisted="$(field_value "$line" persisted_environment)"
    live="$(field_value "$line" live_environment)"
    match="$(field_value "$line" environment_match)"
    this_gd="$(field_value "$line" game_data_hash)"
    first="$(grep -m1 "^$body=" "$FIRST_HASHES" | cut -d= -f2-)"

    if [[ "$this_gd" != "$first_gd" ]]; then
      echo "FAIL game_data_hash_changed body=$body first=$first_gd second=$this_gd"
      failures=$((failures+1))
    fi
    if [[ "$live" != "$first" ]]; then
      echo "FAIL live_hash_changed body=$body first=$first second=$live"
      failures=$((failures+1))
    fi
    if [[ "$persisted" != "$live" || "$match" != "true" ]]; then
      echo "FAIL persisted_live_mismatch body=$body persisted=$persisted live=$live environment_match=$match"
      failures=$((failures+1))
    fi
  done

  local transitions
  transitions="$(awk '/\\[AERIS44\\]\\[R043_PRELOAD_ENV_TRANSITION\\]/ && /environment_contract=ENV2_STABLE/ { c++ } END { print c+0 }' "$seg")"
  if [[ "$transitions" != "0" ]]; then
    echo "FAIL second_launch_environment_transitions=$transitions"
    failures=$((failures+1))
    grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" |
      grep -F '; environment_contract=ENV2_STABLE;' || true
  fi

  rm -f "$seg"

  if (( failures != 0 )); then
    echo "AERIS_CURRENT_STAGE=ENV2_STABILITY_FAIL"
    echo "producer_switch=false"
    echo "db_authority=PQS"
    exit 41
  fi

  echo "=== AERIS44 ENV2 RESTART STABILITY PASS ==="
  echo "bodies=${#BODIES[@]}"
  echo "game_data_hash=$first_gd"
  echo "live_hash_changed=0"
  echo "persisted_live_mismatch=0"
  echo "second_launch_environment_transitions=0"
  echo "database_deleted_by_runner=false"
  echo "database_rebuilt_by_runner=false"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=CONTROLLED_PRELOAD_REBUILD_THEN_ENV2_RESTART_VERIFY"
}

cd "$ROOT"

echo "=== AERIS44 / R043 PRELOAD ENVIRONMENT HASH STABLE V2 ==="
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

# Persistent DB format/codec stay untouched. This stage changes only the
# environment identity contract and diagnostics/tooling.
for protected in   Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs   Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs   Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs; do
  git diff --quiet "$BASE" HEAD -- "$protected" || {
    echo "STOP: protected source changed: $protected" >&2
    exit 12
  }
done

grep -Fq 'AERIS_TERRAIN_ENV2_STABLE'   Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs || exit 13
grep -Fq 'environment_contract=ENV2_STABLE'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 14

HEAD_SHA="$(git rev-parse HEAD)"

if [[ -f "$STATE" && -f "$TARGET" && -f "$LOG" ]]; then
  RECORDED_HEAD="$(state_value head || true)"
  RECORDED_SHA="$(state_value dll_sha || true)"
  PHASE="$(state_value phase || true)"
  if [[ "$RECORDED_HEAD" = "$HEAD_SHA" &&
        -n "$RECORDED_SHA" &&
        "$(sha256sum "$TARGET" | awk '{print $1}')" = "$RECORDED_SHA" ]]; then
    if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
      echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
      exit 0
    fi
    case "$PHASE" in
      await_first)
        harvest_first "$(state_value log_offset)"
        exit 0
        ;;
      await_second)
        harvest_second "$(state_value log_offset)"
        exit 0
        ;;
    esac
  fi
fi

rm -rf "$STATE_DIR"
mkdir -p "$STATE_DIR"

# Preserve the latest pre-ENV2 DB as evidence. The runtime may invalidate the
# old environment once when ENV2 first becomes authoritative; the runner never
# deletes or rebuilds the DB itself.
DB_BACKUP=""
if [[ -d "$DB" ]]; then
  DB_BACKUP="$HOME/.cache/AERIS/preload-env2-before-$(date +%Y%m%d-%H%M%S)"
  mkdir -p "$DB_BACKUP"
  cp -a "$DB" "$DB_BACKUP/"
fi

TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS44 ENV2"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS44 — R043 PRELOAD ENVIRONMENT HASH STABLE V2"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
v='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
replacement=(
    '    <Compile Include="Properties\\AERISBuildVersion.R044Env2.generated.cs" />\n'
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
BACKUP="$BACKUP_DIR/$(date +%Y%m%d-%H%M%S)-$OLD_SHA.AERISFlightControl.dll"
cp -a "$TARGET" "$BACKUP"
install -m0644 "$DLL" "$TARGET"

OFFSET=0
[[ -f "$LOG" ]] && OFFSET="$(stat -c %s "$LOG")"

cat > "$STATE" <<EOFSTATE
phase=await_first
head=$HEAD_SHA
dll_sha=$NEW_SHA
log_offset=$OFFSET
db_backup=$DB_BACKUP
first_game_data_hash=
EOFSTATE

echo "=== AERIS44 ENV2 INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "dll_backup=$BACKUP"
echo "pre_env2_db_backup=$DB_BACKUP"
echo "environment_contract=ENV2_STABLE"
echo "database_deleted_by_runner=false"
echo "database_rebuilt_by_runner=false"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_FIRST_ENV2_LAUNCH"
echo "human_action=Launch KSP to Main Menu, wait 30 seconds, exit KSP, then run the same command again."
