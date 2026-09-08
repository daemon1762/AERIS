#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase4b_main_thread_runtime_capture.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
BUILDER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs"
OBSERVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeCaptureObserver.cs"
CONTRACT="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA_ROOT/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r042-phase4b-runtime-capture/$KEY"
STATE="$STATE_DIR/state.txt"

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE4B MAIN THREAD RUNTIME CAPTURE ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"

test "$(git branch --show-current)" = "$EXPECTED_BRANCH" || {
  echo "STOP: wrong branch" >&2
  exit 10
}
[[ -f "$PROJECT" && -f "$BUILDER" && -f "$OBSERVER" && -f "$CONTRACT" && -f "$RESOLVER" ]] || {
  echo "STOP: Phase4B source set incomplete" >&2
  exit 11
}
[[ -f "$ASSEMBLY" && -f "$CORE" ]] || {
  echo "STOP: KSP managed assemblies missing" >&2
  exit 12
}
command -v xbuild >/dev/null 2>&1 || { echo "STOP: xbuild not found" >&2; exit 13; }

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
[[ "$ASSEMBLY_SHA" = "$EXPECTED_ASSEMBLY_SHA" ]] || {
  echo "STOP: Assembly-CSharp identity mismatch" >&2
  echo "actual=$ASSEMBLY_SHA" >&2
  exit 14
}
[[ "$CORE_SHA" = "$EXPECTED_CORE_SHA" ]] || {
  echo "STOP: UnityEngine.CoreModule identity mismatch" >&2
  echo "actual=$CORE_SHA" >&2
  exit 15
}

# Static fail-closed gates.
grep -Fq 'Thread.CurrentThread.ManagedThreadId' "$BUILDER" || exit 20
grep -Fq 'CAPTURE_NOT_ON_MAIN_THREAD' "$BUILDER" || exit 21
grep -Fq 'R041_TOPOLOGY_MISMATCH' "$BUILDER" || exit 22
grep -Fq 'int[] modePreference = { 1, 0, 2, 3 };' "$BUILDER" || exit 23
grep -Fq 'VERTEX_VORONOI_ONSETUP_STATE_MISMATCH' "$BUILDER" || exit 24
grep -Fq 'HEIGHTNOISE_ONSETUP_STATE_MISMATCH' "$BUILDER" || exit 25
grep -Fq 'TryCreateR040BProductionSnapshot' "$BUILDER" || exit 26
grep -Fq 'CertifiedBodyCount = 7' "$BUILDER" || exit 27
grep -Fq 'TargetBodies' "$OBSERVER" || exit 28
grep -Fq 'runtime_object_fields=0' "$OBSERVER" || exit 29
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || exit 30

if grep -Eq '\.OnVertexBuildHeight\s*\(' "$BUILDER"; then
  echo "STOP: Phase4B builder invokes live terrain callback" >&2
  exit 31
fi
if grep -Eq '\.SetValue\s*\(' "$BUILDER"; then
  echo "STOP: Phase4B builder writes reflected runtime state" >&2
  exit 32
fi
if grep -Fq 'Task.' "$BUILDER"; then
  echo "STOP: Phase4B capture builder contains worker scheduling" >&2
  exit 33
fi

for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
  grep -Fq "\"$body\"" "$OBSERVER" || {
    echo "STOP: observer target missing: $body" >&2
    exit 34
  }
done

state_value() {
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^${key}=" "$STATE" | cut -d= -f2-
}

harvest_if_ready() {
  [[ -f "$STATE" ]] || return 1
  local state_head state_sha state_offset state_log
  state_head="$(state_value head || true)"
  state_sha="$(state_value installed_dll_sha || true)"
  state_offset="$(state_value log_offset || true)"
  state_log="$(state_value log || true)"
  [[ "$state_head" = "$(git rev-parse HEAD)" ]] || return 1
  [[ -n "$state_sha" && -n "$state_offset" && "$state_log" = "$LOG" ]] || return 1

  mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
  [[ "${#targets[@]}" -eq 1 ]] || return 1
  local installed_sha
  installed_sha="$(sha256sum "${targets[0]}" | awk '{print $1}')"
  [[ "$installed_sha" = "$state_sha" ]] || return 1

  [[ -f "$LOG" ]] || {
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait until fully loaded, exit KSP, then run the same command again."
    return 0
  }

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS42_R042_PHASE4B.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  local complete
  complete="$(grep -F '[AERIS42][R042_CAPTURE_COMPLETE]' "$segment" | tail -n 1 || true)"
  if [[ -z "$complete" ]]; then
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait until fully loaded, exit KSP, then run the same command again."
    return 0
  fi

  local pass=1
  [[ "$complete" == *'; pass=true;'* ]] || pass=0
  [[ "$complete" == *'; bodies=7;'* ]] || pass=0
  [[ "$complete" == *'; captures=7;'* ]] || pass=0
  [[ "$complete" == *'; failures=0;'* ]] || pass=0
  [[ "$complete" == *'; all_capture_thread_main=true;'* ]] || pass=0
  [[ "$complete" == *'; runtime_object_fields=0;'* ]] || pass=0
  [[ "$complete" == *'; production_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; producer_switch=false;'* ]] || pass=0
  [[ "$complete" == *'; db_write=false'* ]] || pass=0

  for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
    local line
    line="$(grep -F '[AERIS42][R042_CAPTURE_BODY]' "$segment" | grep -F "; body=$body;" | tail -n 1 || true)"
    [[ -n "$line" ]] || { pass=0; continue; }
    [[ "$line" == *'; pass=true;'* ]] || pass=0
    [[ "$line" == *'; runtime_snapshot_certified=true;'* ]] || pass=0
    [[ "$line" == *'; structurally_valid=true;'* ]] || pass=0
    [[ "$line" == *'; capture_thread_is_main=true;'* ]] || pass=0
    [[ "$line" == *'; runtime_object_retained=false;'* ]] || pass=0
    [[ "$line" == *'; production_authority=PQS;'* ]] || pass=0
    [[ "$line" == *'; producer_switch=false;'* ]] || pass=0
  done

  echo "=== R042 PHASE4B RUNTIME CAPTURE HARVEST ==="
  grep -F '[AERIS42][R042_CAPTURE_BODY]' "$segment" || true
  echo "$complete"
  rm -f "$segment"

  if [[ "$pass" -ne 1 ]]; then
    echo "R042_PHASE4B_MAIN_THREAD_RUNTIME_CAPTURE=FAIL"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 40
  fi

  rm -rf "$STATE_DIR"
  echo "R041_FINAL_ACCEPTANCE=PRESERVED"
  echo "runtime_snapshot_capture_bodies=7"
  echo "capture_thread=MAIN_THREAD_ONLY"
  echo "snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY"
  echo "runtime_object_fields=0"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_write_switch=false"
  echo "R042_PHASE4B_MAIN_THREAD_RUNTIME_CAPTURE=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R042_PHASE5_WORKER_EXECUTION_PARITY"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale Phase4B state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo
echo "=== XBUILD / PHASE4B PRODUCTION PROJECT ==="
rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj
)
[[ -f "$DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 50; }

mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
[[ "${#targets[@]}" -eq 1 ]] || {
  echo "STOP: expected exactly one installed AERISFlightControl.dll; found=${#targets[@]}" >&2
  exit 51
}
TARGET="${targets[0]}"
OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
NEW_SHA="$(sha256sum "$DLL" | awk '{print $1}')"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
BACKUP="$BACKUP_DIR/${STAMP}-${OLD_SHA}.AERISFlightControl.dll"
mkdir -p "$BACKUP_DIR"
cp -a "$TARGET" "$BACKUP"
install -m 0644 "$DLL" "$TARGET"
cmp -s "$DLL" "$TARGET" || { echo "STOP: installed DLL differs from build" >&2; exit 52; }

LOG_OFFSET=0
[[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"
mkdir -p "$STATE_DIR"
cat > "$STATE" <<EOF
head=$(git rev-parse HEAD)
installed_dll_sha=$NEW_SHA
log_offset=$LOG_OFFSET
log=$LOG
ksp=$KSP
backup=$BACKUP
previous_dll_sha=$OLD_SHA
EOF

echo
echo "=== R042 PHASE4B INSTALLED ==="
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "capture_targets=7"
echo "capture_thread=MAIN_THREAD_ONLY"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_write_switch=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, wait until fully loaded, exit KSP, then run the same command again."
