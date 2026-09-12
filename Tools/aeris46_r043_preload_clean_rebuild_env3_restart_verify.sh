#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris46_r043_preload_clean_rebuild_env3_restart_verify.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris46-r043-natural-exact-parity-repair"
SOURCE_BASE="990d9c7cda1a6e5381a4665b8c2697e3dca006f5"
CANDIDATE="AERIS46_R043_PRELOAD_CLEAN_REBUILD_ENV3_RESTART_VERIFY"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R046CleanRebuildEnv3.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R046CleanRebuildEnv3.generated.cs"
IDENTITY="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
DB="$GAME_DATA/PluginData/TerrainPreloadDatabaseV3"
PRELOAD_STATE="$DB/preload_state.aps"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r046-clean-rebuild-env3/$KEY"
STATE="$STATE_DIR/state.txt"
FINAL_HASHES="$STATE_DIR/final_hashes.txt"
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

latest_final_body(){
  local seg="$1" body="$2"
  grep -F '[AERIS45][R043_PRELOAD_FINAL_STATE_BODY]' "$seg" |
    grep -F "; body=$body;" | tail -n1 || true
}

latest_state_body(){
  local seg="$1" body="$2"
  grep -F '[AERIS44][R043_PRELOAD_STATE_BODY]' "$seg" |
    grep -F "; body=$body;" | tail -n1 || true
}

latest_env_body(){
  local seg="$1" body="$2"
  grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$seg" |
    grep -F "; body=$body;" | grep -F '; environment_contract=ENV3_TERRAIN_CFG_PQS;' |
    tail -n1 || true
}

segment_from_offset(){
  local off="$1" out="$2"
  local current start
  current="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( current >= off )); then tail -c +"$start" "$LOG" > "$out"; else cp "$LOG" "$out"; fi
}

parse_preload_state(){
  local input="$1" output="$2"
  python3 - "$input" "$output" <<'PY'
import struct, sys

src, dst = sys.argv[1], sys.argv[2]

class Reader:
    def __init__(self, data):
        self.data = data
        self.p = 0
    def take(self, n):
        if n < 0 or self.p + n > len(self.data):
            raise ValueError("truncated preload state")
        out = self.data[self.p:self.p+n]
        self.p += n
        return out
    def u8(self):
        return self.take(1)[0]
    def i32(self):
        return struct.unpack("<i", self.take(4))[0]
    def i64(self):
        return struct.unpack("<q", self.take(8))[0]
    def boolean(self):
        return self.u8() != 0
    def seven(self):
        value = 0
        shift = 0
        for _ in range(5):
            b = self.u8()
            value |= (b & 0x7f) << shift
            if (b & 0x80) == 0:
                return value
            shift += 7
        raise ValueError("invalid 7-bit length")
    def string(self):
        n = self.seven()
        return self.take(n).decode("utf-8")

data = open(src, "rb").read()
r = Reader(data)
magic = r.string()
if magic != "AERIS_PRELOAD_TERRAIN_STATE_V2":
    raise SystemExit("bad state magic: " + repr(magic))
version = r.i32()
if version not in (4,5,6):
    raise SystemExit("unsupported state version: %d" % version)
mode = r.i32()
if version >= 5:
    applied_point_signature = r.string()
else:
    applied_point_signature = ""
count = r.i32()
if count < 0 or count > 10000:
    raise SystemExit("invalid plan count: %d" % count)

rows = []
for _ in range(count):
    body = r.string()
    priority = r.i32()
    priority_override = r.boolean()
    quality_limit = r.i32()
    quality_override = r.boolean()
    auto_point_only = r.boolean()
    automatic_complete = r.boolean()
    completed_quality = r.i32()
    completed_point_only = r.boolean()
    completed_environment = r.string()
    storage_limit = r.i64()
    last_visited = r.i64()
    global_cursor = r.i64()
    far_cursor = r.i64()
    route_cursor = r.i64()
    point_cursor = r.i32()
    environment = r.string()
    paused = r.boolean()
    coastline_cursor = r.i64()
    coastline_complete = r.boolean()
    coastline_format = r.i32()
    coastline_environment = r.string()

    if version >= 6:
        coast_radius_mm = r.i64()
        coast_terrain_version = r.i32()
        coast_format_version = r.i32()
        coast_progress_environment = r.string()
        coast_lat_tiles = r.i32()
        coast_lon_tiles = r.i32()
        bitmap_len = r.i32()
        if bitmap_len < 0 or bitmap_len > 1024 * 1024:
            raise SystemExit("invalid coastline bitmap length for %s" % body)
        r.take(bitmap_len)

    rows.append((
        body, environment, "true" if automatic_complete else "false",
        completed_environment, str(completed_quality),
        "true" if coastline_complete else "false",
        coastline_environment, str(global_cursor), str(far_cursor),
        str(route_cursor), str(point_cursor), str(coastline_cursor)
    ))

with open(dst, "w", encoding="utf-8") as out:
    out.write("STATE_VERSION\t%d\n" % version)
    out.write("PLAN_COUNT\t%d\n" % count)
    for row in rows:
        out.write("BODY\t" + "\t".join(row) + "\n")
PY
}

harvest_rebuild(){
  local off="$1"
  local parsed body row env auto completed coast coast_env incomplete
  parsed="$(mktemp /tmp/AERIS46_ENV3_STATE.XXXXXX)"

  if [[ ! -f "$PRELOAD_STATE" ]]; then
    rm -f "$parsed"
    echo "AERIS_CURRENT_STAGE=REBUILD_INCOMPLETE"
    echo "reason=preload_state_missing"
    echo "human_action=Launch KSP and continue Automatic Preload, then exit cleanly and run the same command again."
    return 0
  fi

  if ! parse_preload_state "$PRELOAD_STATE" "$parsed"; then
    rm -f "$parsed"
    echo "AERIS_CURRENT_STAGE=REBUILD_STATE_PARSE_FAIL"
    echo "state=$PRELOAD_STATE"
    exit 40
  fi

  echo "=== AERIS46 ENV3 CLEAN REBUILD PERSISTED STATE CHECK ==="
  grep -E '^(STATE_VERSION|PLAN_COUNT)' "$parsed" || true

  incomplete=0
  : > "$FINAL_HASHES"
  for body in "${BODIES[@]}"; do
    row="$(awk -F '\t' -v b="$body" '$1=="BODY" && $2==b {print; exit}' "$parsed")"
    if [[ -z "$row" ]]; then
      echo "WAIT body=$body reason=missing_persisted_plan"
      incomplete=$((incomplete+1))
      continue
    fi

    env="$(printf '%s\n' "$row" | cut -f3)"
    auto="$(printf '%s\n' "$row" | cut -f4)"
    completed="$(printf '%s\n' "$row" | cut -f5)"
    coast="$(printf '%s\n' "$row" | cut -f7)"
    coast_env="$(printf '%s\n' "$row" | cut -f8)"

    if [[ "$auto" != "true" || "$coast" != "true" ||
          -z "$env" || "$completed" != "$env" || "$coast_env" != "$env" ]]; then
      echo "WAIT body=$body automatic_complete=$auto coastline_complete=$coast environment=$env completed_environment=$completed coastline_environment=$coast_env"
      incomplete=$((incomplete+1))
      continue
    fi

    printf '%s=%s\n' "$body" "$env" >> "$FINAL_HASHES"
    echo "PASS body=$body environment=$env"
  done

  rm -f "$parsed"

  if (( incomplete != 0 )); then
    rm -f "$FINAL_HASHES"
    echo "AERIS_CURRENT_STAGE=REBUILD_INCOMPLETE"
    echo "incomplete_bodies=$incomplete"
    echo "human_action=Continue Main Menu Automatic Preload until all reported bodies complete, exit KSP cleanly, then run the same command again."
    return 0
  fi

  local current transitions db_bytes db_files
  current="$(stat -c %s "$LOG")"
  local seg
  seg="$(mktemp /tmp/AERIS46_ENV3_REBUILD_LOG.XXXXXX)"
  segment_from_offset "$off" "$seg"
  transitions="$(awk '/\\[AERIS44\\]\\[R043_PRELOAD_ENV_TRANSITION\\]/ && /environment_contract=ENV3_TERRAIN_CFG_PQS/ { c++ } END { print c+0 }' "$seg")"
  rm -f "$seg"

  db_bytes="$(du -sb "$DB" 2>/dev/null | awk '{print $1}' || echo 0)"
  db_files="$(find "$DB" -type f 2>/dev/null | wc -l | tr -d ' ')"

  cat > "$STATE.tmp" <<EOFSTATE
phase=await_restart
head=$(state_value head)
dll_sha=$(state_value dll_sha)
log_offset=$current
clean_backup=$(state_value clean_backup)
db_bytes=$db_bytes
db_files=$db_files
EOFSTATE
  mv "$STATE.tmp" "$STATE"

  echo "=== AERIS46 ENV3 CLEAN REBUILD COMPLETE ==="
  echo "completion_authority=preload_state.aps"
  echo "bodies=${#BODIES[@]}"
  echo "environment_transitions_during_rebuild=$transitions"
  echo "db_bytes=$db_bytes"
  echo "db_files=$db_files"
  echo "AERIS_CURRENT_STAGE=REBUILD_COMPLETE_WAITING_FOR_RESTART"
  echo "human_action=Launch KSP once more with the same build, wait 30 seconds at Main Menu without rebuilding anything, exit KSP, then run the same command again."
}

harvest_restart(){
  local off="$1"
  local seg parsed body row env_line first_env persisted completed coast_env auto coast live match failures
  seg="$(mktemp /tmp/AERIS46_ENV3_RESTART.XXXXXX)"
  parsed="$(mktemp /tmp/AERIS46_ENV3_RESTART_STATE.XXXXXX)"
  segment_from_offset "$off" "$seg"
  failures=0

  if [[ ! -f "$PRELOAD_STATE" ]]; then
    rm -f "$seg" "$parsed"
    echo "AERIS_CURRENT_STAGE=POST_REBUILD_RESTART_FAIL"
    echo "reason=preload_state_missing_after_restart"
    exit 41
  fi

  if ! parse_preload_state "$PRELOAD_STATE" "$parsed"; then
    rm -f "$seg" "$parsed"
    echo "AERIS_CURRENT_STAGE=POST_REBUILD_RESTART_FAIL"
    echo "reason=preload_state_parse_failed_after_restart"
    exit 41
  fi

  echo "=== AERIS46 ENV3 POST-REBUILD RESTART VERIFY ==="
  echo "persistence_authority=preload_state.aps"
  grep -E '^(STATE_VERSION|PLAN_COUNT)' "$parsed" || true

  for body in "${BODIES[@]}"; do
    row="$(awk -F '\t' -v b="$body" '$1=="BODY" && $2==b {print; exit}' "$parsed")"
    env_line="$(latest_env_body "$seg" "$body")"
    first_env="$(grep -m1 "^$body=" "$FINAL_HASHES" | cut -d= -f2-)"

    if [[ -z "$row" || -z "$env_line" || -z "$first_env" ]]; then
      echo "FAIL body=$body reason=missing_restart_witness state_row=$([[ -n "$row" ]] && echo true || echo false) env_log=$([[ -n "$env_line" ]] && echo true || echo false) baseline=$([[ -n "$first_env" ]] && echo true || echo false)"
      failures=$((failures+1))
      continue
    fi

    persisted="$(printf '%s\n' "$row" | cut -f3)"
    auto="$(printf '%s\n' "$row" | cut -f4)"
    completed="$(printf '%s\n' "$row" | cut -f5)"
    coast="$(printf '%s\n' "$row" | cut -f7)"
    coast_env="$(printf '%s\n' "$row" | cut -f8)"

    live="$(field_value "$env_line" live_environment)"
    match="$(field_value "$env_line" environment_match)"

    if [[ "$live" != "$first_env" || "$persisted" != "$live" ||
          "$completed" != "$live" || "$coast_env" != "$live" ||
          "$auto" != "true" || "$coast" != "true" || "$match" != "true" ]]; then
      echo "FAIL body=$body first_env=$first_env live=$live persisted=$persisted completed=$completed coastline_environment=$coast_env automatic_complete=$auto coastline_complete=$coast environment_match=$match"
      failures=$((failures+1))
    else
      echo "PASS body=$body environment=$live"
    fi
  done

  local transitions
  transitions="$(awk '/\\[AERIS44\\]\\[R043_PRELOAD_ENV_TRANSITION\\]/ && /environment_contract=ENV3_TERRAIN_CFG_PQS/ { c++ } END { print c+0 }' "$seg")"
  if [[ "$transitions" != "0" ]]; then
    echo "FAIL restart_environment_transitions=$transitions"
    failures=$((failures+1))
  fi

  local before_bytes before_files after_bytes after_files
  before_bytes="$(state_value db_bytes || echo 0)"
  before_files="$(state_value db_files || echo 0)"
  after_bytes="$(du -sb "$DB" 2>/dev/null | awk '{print $1}' || echo 0)"
  after_files="$(find "$DB" -type f 2>/dev/null | wc -l | tr -d ' ')"

  rm -f "$seg" "$parsed"

  if (( failures != 0 )); then
    echo "AERIS_CURRENT_STAGE=POST_REBUILD_RESTART_FAIL"
    echo "failures=$failures"
    echo "restart_environment_transitions=$transitions"
    echo "producer_switch=false"
    echo "db_authority=PQS"
    exit 41
  fi

  echo "=== AERIS46 ENV3 CLEAN REBUILD + RESTART PERSISTENCE PASS ==="
  echo "bodies=${#BODIES[@]}"
  echo "restart_environment_transitions=0"
  echo "automatic_complete_preserved=15"
  echo "coastline_complete_preserved=15"
  echo "environment_identity_preserved=15"
  echo "db_bytes_before_restart=$before_bytes"
  echo "db_bytes_after_restart=$after_bytes"
  echo "db_files_before_restart=$before_files"
  echo "db_files_after_restart=$after_files"
  echo "clean_backup=$(state_value clean_backup)"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "exact_cpu_db_write=false"
  echo "natural_parity_repair=REQUIRED_PREVIOUS_PASS"
  echo "environment_contract=ENV3_TERRAIN_CFG_PQS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=TERRAIN_ND_AFTER_DESKTOP_CROSSCHECK"
}

cd "$ROOT"

echo "=== AERIS46 / R043 CLEAN PRELOAD REBUILD + ENV3 RESTART VERIFY ==="
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

for protected in   Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs   Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs   Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs   Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs; do
  git diff --quiet "$SOURCE_BASE" HEAD -- "$protected" || {
    echo "STOP: protected source changed: $protected" >&2
    exit 12
  }
done

grep -Fq '[AERIS45][R043_PRELOAD_FINAL_STATE_BODY]'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 13
grep -Fq 'AERIS_TERRAIN_ENV3_TERRAIN_CFG_PQS'   Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs || exit 14
grep -Fq 'automatic_db_invalidation=false'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 15
grep -Fq 'evaluationLatitude = boundaryCacheHit'   Source/AERISFlightControl/Terrain/AERISR043PreloadPtcIntegratedShadow.cs || exit 16
grep -Fq 'PRELOAD WAITING FOR TERRAIN ENVIRONMENT HASH'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 17

HEAD_SHA="$(git rev-parse HEAD)"

if [[ -f "$STATE" && -f "$TARGET" && -f "$LOG" ]]; then
  RECORDED_HEAD="$(state_value head || true)"
  RECORDED_SHA="$(state_value dll_sha || true)"
  PHASE="$(state_value phase || true)"
  SOURCE_COMPATIBLE=false
  if [[ "$RECORDED_HEAD" = "$HEAD_SHA" ]]; then
    SOURCE_COMPATIBLE=true
  elif [[ -n "$RECORDED_HEAD" ]] &&
       git diff --quiet "$RECORDED_HEAD" HEAD -- Source/AERISFlightControl; then
    SOURCE_COMPATIBLE=true
  fi
  if [[ "$SOURCE_COMPATIBLE" = "true" &&
        -n "$RECORDED_SHA" &&
        "$(sha256sum "$TARGET" | awk '{print $1}')" = "$RECORDED_SHA" ]]; then
    if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
      echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
      exit 0
    fi
    case "$PHASE" in
      await_rebuild_completion)
        harvest_rebuild "$(state_value log_offset)"
        exit 0
        ;;
      await_restart)
        harvest_restart "$(state_value log_offset)"
        exit 0
        ;;
    esac
  fi
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "STOP: KSP must be fully exited before clean rebuild setup" >&2
  exit 20
fi

rm -rf "$STATE_DIR"
mkdir -p "$STATE_DIR"

TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS46 ENV3 CLEAN REBUILD"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS46 — R043 CLEAN PRELOAD REBUILD / ENV3 RESTART VERIFY"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
v='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
replacement=(
    '    <Compile Include="Properties\\AERISBuildVersion.R046CleanRebuildEnv3.generated.cs" />\n'
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

CLEAN_BACKUP="$HOME/.cache/AERIS/preload-clean-rebuild-before-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$CLEAN_BACKUP"
if [[ -d "$DB" ]]; then
  mv "$DB" "$CLEAN_BACKUP/"
fi

OFFSET=0
[[ -f "$LOG" ]] && OFFSET="$(stat -c %s "$LOG")"

cat > "$STATE" <<EOFSTATE
phase=await_rebuild_completion
head=$HEAD_SHA
dll_sha=$NEW_SHA
log_offset=$OFFSET
clean_backup=$CLEAN_BACKUP
db_bytes=0
db_files=0
EOFSTATE

echo "=== AERIS46 ENV3 CLEAN REBUILD ARMED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "dll_backup=$DLL_BACKUP"
echo "clean_preload_backup=$CLEAN_BACKUP"
echo "old_db_moved=true"
echo "new_db_present=$([[ -d "$DB" ]] && echo true || echo false)"
echo "environment_contract=ENV3_TERRAIN_CFG_PQS"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "exact_cpu_db_write=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_FULL_REBUILD"
echo "human_action=Launch KSP and leave Main Menu Automatic Preload running until all 15 solid bodies reach 100 percent. Then exit KSP cleanly and run the same command again."
