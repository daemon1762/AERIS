#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris39_mapso3h_coord_component_diagnostic.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
CANDIDATE="AERIS39_MAPSO3H_COORD_COMPONENT_DIAGNOSTIC_V1"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"

SRC="$ROOT/Source/AERISFlightControl"
CSPROJ="$SRC/AERISFlightControl.csproj"
TMP_CSPROJ="$SRC/.AERIS39_MAPSO3H.csproj"
BUILD_DLL="$SRC/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
LOG="$KSP/GameData/AERISFlightControl/Logs/AERISFlightControl.log"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
ARTIFACT_ROOT="${AERIS_ARTIFACT_ROOT:-$HOME/.cache/AERIS/artifacts}"
OUT="$ARTIFACT_ROOT/AERIS39_MAPSO3H_Coord_Component_Diagnostic"
ARCHIVE="$ARTIFACT_ROOT/AERIS39_MAPSO3H_Coord_Component_Diagnostic.tar.gz"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/mapso3h-coord-component/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() { rm -f "$TMP_CSPROJ"; }
trap cleanup EXIT

cd "$ROOT"

for cmd in git sha256sum python3 xbuild find stat grep tail tar install cp pgrep; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "STOP: required command missing: $cmd" >&2; exit 3; }
done

[[ -f "$CSPROJ" ]] || { echo "STOP: missing project" >&2; exit 4; }
[[ -f "$ASSEMBLY" ]] || { echo "STOP: missing Assembly-CSharp.dll" >&2; exit 4; }
[[ -f "$CORE" ]] || { echo "STOP: missing UnityEngine.CoreModule.dll" >&2; exit 4; }
[[ -d "$GAME_DATA_ROOT" ]] || { echo "STOP: missing AERIS GameData" >&2; exit 4; }

test "$(git branch --show-current)" = "$BRANCH" || { echo "STOP: wrong branch" >&2; exit 5; }
test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before MAPSO-3H" >&2
  git status -sb
  exit 6
}

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
[[ "$ASSEMBLY_SHA" = "$EXPECTED_ASSEMBLY_SHA" ]] || { echo "STOP: Assembly-CSharp identity mismatch" >&2; exit 7; }
[[ "$CORE_SHA" = "$EXPECTED_CORE_SHA" ]] || { echo "STOP: UnityEngine.CoreModule identity mismatch" >&2; exit 8; }

HEAD="$(git rev-parse HEAD)"

state_value() {
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^${key}=" "$STATE" | cut -d= -f2-
}

write_artifacts() {
  local segment="$1" result="$2" installed_sha="$3"
  mkdir -p "$ARTIFACT_ROOT"
  rm -rf "$OUT"
  mkdir -p "$OUT"
  grep '\[AERIS39\]\[MAPSO3H_' "$segment" > "$OUT/mapso3h_runtime_excerpt.txt" || true
  cat > "$OUT/provenance.txt" <<EOF
schema=$CANDIDATE
candidate=$CANDIDATE
result=$result
branch=$BRANCH
git_head=$HEAD
ksp_root=$KSP
assembly_sha256=$ASSEMBLY_SHA
unity_core_sha256=$CORE_SHA
installed_dll_sha256=$installed_sha
pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoPureCpuExact.cs" | awk '{print $1}')
diagnostic_observer_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoCoordComponentDiagnosticObserver.cs" | awk '{print $1}')
production_authority=PQS
db_authority=PQS
producer_switch=false
db_write=false
preload_mutation=false
production_worker_runtime_object_access=false
EOF
  (cd "$OUT" && sha256sum mapso3h_runtime_excerpt.txt provenance.txt > SHA256SUMS.txt)
  rm -f "$ARCHIVE"
  tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_MAPSO3H_Coord_Component_Diagnostic
}

harvest_if_ready() {
  [[ -f "$STATE" ]] || return 1
  local state_head state_sha state_offset state_log
  state_head="$(state_value head || true)"
  state_sha="$(state_value installed_dll_sha || true)"
  state_offset="$(state_value log_offset || true)"
  state_log="$(state_value log || true)"

  [[ "$state_head" = "$HEAD" ]] || return 1
  [[ -n "$state_sha" && -n "$state_offset" && "$state_log" = "$LOG" ]] || return 1

  mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
  [[ "${#targets[@]}" -eq 1 ]] || return 1
  local installed_sha
  installed_sha="$(sha256sum "${targets[0]}" | awk '{print $1}')"
  [[ "$installed_sha" = "$state_sha" ]] || return 1

  [[ -f "$LOG" ]] || {
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
    return 0
  }

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS39_MAPSO3H_segment.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  if grep -Fq "[AERIS39][MAPSO3H_FAIL]" "$segment"; then
    write_artifacts "$segment" "FAIL" "$installed_sha"
    echo "=== MAPSO-3H RUNTIME FAILURE ==="
    grep '\[AERIS39\]\[MAPSO3H_' "$segment" || true
    echo "archive=$ARCHIVE"
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 40
  fi

  if ! grep -Fq "[AERIS39][MAPSO3H_COMPLETE]" "$segment"; then
    local boot_seen=false
    grep -Fq "[AERIS39][MAPSO3H_BOOT]" "$segment" && boot_seen=true
    rm -f "$segment"
    echo "observer_boot_seen=$boot_seen"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
    return 0
  fi

  local complete pass=1
  complete="$(grep -F "[AERIS39][MAPSO3H_COMPLETE]" "$segment" | tail -n 1)"
  [[ "$complete" == *"; candidate=$CANDIDATE;"* ]] || pass=0
  [[ "$complete" == *"; diagnostic_complete=true;"* ]] || pass=0
  [[ "$complete" == *"; bodies=6;"* ]] || pass=0
  [[ "$complete" == *"; production_authority=PQS;"* ]] || pass=0
  [[ "$complete" == *"; db_authority=PQS;"* ]] || pass=0
  [[ "$complete" == *"; producer_switch=false;"* ]] || pass=0
  [[ "$complete" == *"; db_write=false;"* ]] || pass=0
  [[ "$complete" == *"; preload_mutation=false;"* ]] || pass=0
  [[ "$complete" == *"; production_worker_runtime_object_access=false"* ]] || pass=0
  for body in Kerbin Eve Duna Dres Moho Eeloo; do
    grep -F "[AERIS39][MAPSO3H_BODY]" "$segment" | grep -Fq "; body=$body;" || pass=0
  done

  write_artifacts "$segment" "$([[ "$pass" -eq 1 ]] && echo PASS || echo FAIL_ACCEPTANCE)" "$installed_sha"
  if [[ "$pass" -ne 1 ]]; then
    echo "=== MAPSO-3H ACCEPTANCE FAILURE ==="
    grep '\[AERIS39\]\[MAPSO3H_' "$segment" || true
    echo "archive=$ARCHIVE"
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 41
  fi

  echo "=== MAPSO-3H COORD COMPONENT DIAGNOSTIC ==="
  grep '\[AERIS39\]\[MAPSO3H_BODY\]' "$segment" || true
  grep '\[AERIS39\]\[MAPSO3H_DETAIL\]' "$segment" | head -n 36 || true
  echo "$complete"
  echo
  cat "$OUT/SHA256SUMS.txt"
  echo "archive=$ARCHIVE"
  rm -f "$segment"
  rm -rf "$STATE_DIR"
  echo "AERIS39_MAPSO3H_CAPTURE=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=MAPSO-3H_COMPONENT_ANALYSIS"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale MAPSO-3H state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo "=== MAPSO-3H BUILD ==="
echo "HEAD=$HEAD"
echo "KSP=$KSP"
echo "artifact_root=$ARTIFACT_ROOT"

python3 - "$CSPROJ" "$TMP_CSPROJ" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
needle = "</ItemGroup><Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" /></Project>"
insert = (
    "    <Compile Include=\"Terrain\\AERIS39MapSoPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39MapSoCoordComponentDiagnosticObserver.cs\" />\n"
    "</ItemGroup><Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" /></Project>"
)
if needle not in src:
    raise SystemExit("MAPSO3H project insertion point not found")
pathlib.Path(sys.argv[2]).write_text(src.replace(needle, insert, 1), encoding="utf-8")
PY

rm -rf "$SRC/bin/Release" "$SRC/obj/Release"
(cd "$SRC" && xbuild /p:Configuration=Release /p:KSPDIR="$KSP" .AERIS39_MAPSO3H.csproj)
[[ -f "$BUILD_DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 20; }
rm -f "$TMP_CSPROJ"
if [[ -n "$(git status --porcelain)" ]]; then
  echo "STOP: build dirtied repository state" >&2
  git status -sb
  exit 21
fi

mapfile -t TARGETS < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
[[ "${#TARGETS[@]}" -eq 1 ]] || { echo "STOP: expected one installed AERISFlightControl.dll" >&2; exit 22; }
TARGET="${TARGETS[0]}"
OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
BACKUP="$BACKUP_DIR/${STAMP}-${OLD_SHA}.AERISFlightControl.dll"
mkdir -p "$BACKUP_DIR"
cp -a "$TARGET" "$BACKUP"
install -m 0644 "$BUILD_DLL" "$TARGET"
cmp -s "$BUILD_DLL" "$TARGET" || { echo "STOP: installed DLL differs from build" >&2; exit 23; }
INSTALLED_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"

LOG_OFFSET=0
[[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"
mkdir -p "$STATE_DIR"
cat > "$STATE" <<EOF
head=$HEAD
installed_dll_sha=$INSTALLED_SHA
log_offset=$LOG_OFFSET
log=$LOG
ksp=$KSP
backup=$BACKUP
previous_dll_sha=$OLD_SHA
EOF

echo
echo "=== MAPSO-3H INSTALLED ==="
echo "dll_sha256=$INSTALLED_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "production_authority=PQS"
echo "db_authority=PQS"
echo "producer_switch=false"
echo "db_write=false"
echo "preload_mutation=false"
echo "AERIS39_MAPSO3H_BUILD_INSTALL=PASS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
