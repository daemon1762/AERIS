#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase5_worker_execution_parity.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-phase5-worker-parity"
PHASE4B_BASE="9d9b4d785a91721e6cca394bea16147cb1b021a4"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R042Phase5.csproj"
OBSERVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowWorkerParityObserver.cs"
BUILDER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs"
CONTRACT="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
PIPELINE="$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs"
PRELOAD="$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs"
DATABASE="$PROJECT_DIR/Terrain/AERISTerrainPreloadDatabase.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA_ROOT/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r042-phase5-worker-parity/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() {
  rm -f "$TEMP_PROJECT"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE5 WORKER EXECUTION PARITY ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"

test "$(git branch --show-current)" = "$EXPECTED_BRANCH" || {
  echo "STOP: wrong branch" >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty" >&2
  git status -sb >&2
  exit 11
}

for file in "$PROJECT" "$OBSERVER" "$BUILDER" "$CONTRACT" "$RESOLVER" "$PIPELINE" "$PRELOAD" "$DATABASE"; do
  [[ -f "$file" ]] || { echo "STOP: missing $file" >&2; exit 12; }
done
[[ -f "$ASSEMBLY" && -f "$CORE" ]] || {
  echo "STOP: KSP managed assemblies missing" >&2
  exit 13
}
command -v xbuild >/dev/null 2>&1 || { echo "STOP: xbuild not found" >&2; exit 14; }
command -v python3 >/dev/null 2>&1 || { echo "STOP: python3 not found" >&2; exit 15; }

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
[[ "$ASSEMBLY_SHA" = "$EXPECTED_ASSEMBLY_SHA" ]] || {
  echo "STOP: Assembly-CSharp identity mismatch" >&2
  echo "actual=$ASSEMBLY_SHA" >&2
  exit 16
}
[[ "$CORE_SHA" = "$EXPECTED_CORE_SHA" ]] || {
  echo "STOP: UnityEngine.CoreModule identity mismatch" >&2
  echo "actual=$CORE_SHA" >&2
  exit 17
}

# Phase 5 is proof-only. The production terrain/DB/preload sources must remain
# byte-identical to the accepted Phase 4B base across committed history.
for production_path in \
  Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs; do
  if ! git diff --quiet "$PHASE4B_BASE" HEAD -- "$production_path"; then
    echo "STOP: Phase5 modified production path: $production_path" >&2
    exit 18
  fi
done

# Static fail-closed gates.
grep -Fq 'AERISR042ExactCpuShadowRuntimeSnapshotBuilder.TryCapture' "$OBSERVER" || exit 20
grep -Fq 'AERISTerrainAwareness.TrySampleTerrainAslShared' "$OBSERVER" || exit 21
grep -Fq 'runtime.Scheduler.SubmitRequired' "$OBSERVER" || exit 22
grep -Fq 'AERISRuntimeLane.GeneralCompute' "$OBSERVER" || exit 23
grep -Fq 'expected_checks_per_body=560' "$OBSERVER" || exit 24
grep -Fq 'expected_total_checks=3920' "$OBSERVER" || exit 25
grep -Fq 'worker_runtime_object_access=false' "$OBSERVER" || exit 26
grep -Fq 'production_authority=PQS' "$OBSERVER" || exit 27
grep -Fq 'producer_switch=false' "$OBSERVER" || exit 28
grep -Fq 'db_authority=PQS' "$OBSERVER" || exit 29
grep -Fq 'db_write=false' "$OBSERVER" || exit 30
grep -Fq 'preload_mutation=false' "$OBSERVER" || exit 31
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || exit 32

if grep -Eq 'new[[:space:]]+Thread|Task\.|ThreadPool\.|Parallel\.' "$OBSERVER"; then
  echo "STOP: Phase5 observer contains private worker execution path" >&2
  exit 33
fi
if grep -Eq '\.OnVertexBuildHeight\s*\(' "$OBSERVER"; then
  echo "STOP: Phase5 observer invokes live height callback" >&2
  exit 34
fi
if grep -Eq 'AERISTerrainPreload(Database|Builder).*\.(Write|Commit|Save|Store|Put)' "$OBSERVER"; then
  echo "STOP: Phase5 observer contains DB/preload mutation call" >&2
  exit 35
fi

for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
  grep -Fq "\"$body\"" "$OBSERVER" || {
    echo "STOP: Phase5 target missing: $body" >&2
    exit 36
  }
done

# Proof-only source injection. The canonical production csproj is never modified.
python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
text = src.read_text(encoding='utf-8')
needle = '    <Compile Include="Terrain\\AERISR042ExactCpuShadowRuntimeCaptureObserver.cs" />\n'
insert = needle + '    <Compile Include="Terrain\\AERISR042ExactCpuShadowWorkerParityObserver.cs" />\n'
if text.count(needle) != 1:
    raise SystemExit('Phase5 temp-csproj insertion marker not unique')
if 'AERISR042ExactCpuShadowWorkerParityObserver.cs' in text:
    raise SystemExit('Phase5 observer unexpectedly present in canonical csproj')
text = text.replace(needle, insert, 1)
dst.write_text(text, encoding='utf-8')
PY

grep -Fq 'AERISR042ExactCpuShadowWorkerParityObserver.cs' "$TEMP_PROJECT" || exit 37
if grep -Fq 'AERISR042ExactCpuShadowWorkerParityObserver.cs' "$PROJECT"; then
  echo "STOP: proof observer leaked into canonical csproj" >&2
  exit 38
fi

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
    echo "human_action=Launch KSP to Main Menu, wait for the Phase5 completion log, exit KSP, then run the same command again."
    return 0
  }

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS42_R042_PHASE5.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  local complete
  complete="$(grep -F '[AERIS42][R042_PHASE5_COMPLETE]' "$segment" | tail -n 1 || true)"
  if [[ -z "$complete" ]]; then
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait for the Phase5 completion log, exit KSP, then run the same command again."
    return 0
  fi

  local pass=1
  [[ "$complete" == *'; pass=true;'* ]] || pass=0
  [[ "$complete" == *'; bodies=7;'* ]] || pass=0
  [[ "$complete" == *'; total_checks=3920;'* ]] || pass=0
  [[ "$complete" == *'; mismatch_count=0;'* ]] || pass=0
  [[ "$complete" == *'; bit_exact=true;'* ]] || pass=0
  [[ "$complete" == *'; worker_not_main=true;'* ]] || pass=0
  [[ "$complete" == *'; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE;'* ]] || pass=0
  [[ "$complete" == *'; worker_runtime_object_access=false;'* ]] || pass=0
  [[ "$complete" == *'; production_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; producer_switch=false;'* ]] || pass=0
  [[ "$complete" == *'; db_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; db_write=false;'* ]] || pass=0
  [[ "$complete" == *'; preload_mutation=false'* ]] || pass=0

  local body_lines
  body_lines="$(grep -F '[AERIS42][R042_PHASE5_BODY]' "$segment" | wc -l | tr -d ' ')"
  [[ "$body_lines" = "7" ]] || pass=0

  for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
    local line
    line="$(grep -F '[AERIS42][R042_PHASE5_BODY]' "$segment" | grep -F "; body=$body;" | tail -n 1 || true)"
    [[ -n "$line" ]] || { pass=0; continue; }
    [[ "$line" == *'; pass=true;'* ]] || pass=0
    [[ "$line" == *'; checks=560;'* ]] || pass=0
    [[ "$line" == *'; mismatch_count=0;'* ]] || pass=0
    [[ "$line" == *'; bit_exact=true;'* ]] || pass=0
    [[ "$line" == *'; worker_not_main=true;'* ]] || pass=0
    [[ "$line" == *'; production_authority=PQS;'* ]] || pass=0
    [[ "$line" == *'; producer_switch=false;'* ]] || pass=0
    [[ "$line" == *'; db_authority=PQS;'* ]] || pass=0
  done

  echo "=== R042 PHASE5 WORKER PARITY HARVEST ==="
  grep -F '[AERIS42][R042_PHASE5_BODY]' "$segment" || true
  echo "$complete"
  rm -f "$segment"

  if [[ "$pass" -ne 1 ]]; then
    echo "R042_PHASE5_WORKER_EXECUTION_PARITY=FAIL"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 40
  fi

  rm -rf "$STATE_DIR"
  echo "R041_FINAL_ACCEPTANCE=PRESERVED"
  echo "phase4b_runtime_snapshot_capture=PRESERVED"
  echo "worker_parity_bodies=7"
  echo "worker_parity_checks=3920"
  echo "worker_parity_bit_exact=true"
  echo "worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE"
  echo "worker_runtime_object_access=false"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "db_write_switch=false"
  echo "preload_mutation=false"
  echo "R042_PHASE5_WORKER_EXECUTION_PARITY=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R042_PHASE6_PIPELINE_DB_PARITY_SHADOW"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale Phase5 state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo
echo "=== XBUILD / PHASE5 TEMP PROOF PROJECT ==="
rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$TEMP_PROJECT")"
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
echo "=== R042 PHASE5 INSTALLED ==="
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "target_bodies=7"
echo "expected_checks=3920"
echo "proof_project=TEMPORARY_CSPROJ_ONLY"
echo "canonical_csproj_mutated=false"
echo "worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "db_write_switch=false"
echo "preload_mutation=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, wait for the Phase5 completion log, exit KSP, then run the same command again."
