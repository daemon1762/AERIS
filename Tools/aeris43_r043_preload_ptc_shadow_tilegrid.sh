#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris43_r043_preload_ptc_shadow_tilegrid.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris43-r043-preload-ptc-shadow"
PHASE5_ACCEPTED="8cb0ac36c94306e0abe0652a2b31640aa7911044"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
CANDIDATE_NAME="AERIS43_R043_PRELOAD_PTC_SHADOW_TILEGRID"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R043PreloadPtcShadow.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R043PreloadPtcShadow.generated.cs"
OBSERVER="$PROJECT_DIR/Terrain/AERISR043PreloadPtcShadowTileGridObserver.cs"
IDENTITY_OBSERVER="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
SNAPSHOT_BUILDER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs"
SNAPSHOT_CONTRACT="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
PIPELINE="$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs"
PRELOAD="$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs"
DATABASE="$PROJECT_DIR/Terrain/AERISTerrainPreloadDatabase.cs"
TILE_SYSTEM="$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs"
AWARENESS="$PROJECT_DIR/Terrain/AERISTerrainAwareness.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA_ROOT/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r043-preload-ptc-shadow-tilegrid/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() {
  rm -f "$TEMP_PROJECT" "$TEMP_VERSION"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS43 / R043 PRELOAD PTC SHADOW TILEGRID ==="
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

for file in "$PROJECT" "$OBSERVER" "$IDENTITY_OBSERVER" "$SNAPSHOT_BUILDER" \
  "$SNAPSHOT_CONTRACT" "$RESOLVER" "$PIPELINE" "$PRELOAD" "$DATABASE" \
  "$TILE_SYSTEM" "$AWARENESS"; do
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

# Phase 1 is proof-only: the live preload producer/DB/terrain authority must remain
# byte-identical to the accepted R042 Phase5 code while tile-grid semantics are proven.
for production_path in \
  Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs \
  Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs; do
  if ! git diff --quiet "$PHASE5_ACCEPTED" HEAD -- "$production_path"; then
    echo "STOP: R043 tile-grid proof modified production path: $production_path" >&2
    exit 18
  fi
done

grep -Fq 'AERISR042ExactCpuShadowRuntimeSnapshotBuilder.TryCapture' "$OBSERVER" || exit 20
grep -Fq 'AERISTerrainTileSystem.LatitudeTileCountFor' "$OBSERVER" || exit 21
grep -Fq 'AERISTerrainTileFormat.AngularSpanDegrees' "$OBSERVER" || exit 22
grep -Fq 'AERISTerrainAwareness.TrySampleTerrainAslShared' "$OBSERVER" || exit 23
grep -Fq 'runtime.Scheduler.SubmitRequired' "$OBSERVER" || exit 24
grep -Fq 'AERISRuntimeLane.GeneralCompute' "$OBSERVER" || exit 25
grep -Fq 'checks_per_body=245' "$OBSERVER" || exit 26
grep -Fq 'expected_total_checks=1715' "$OBSERVER" || exit 27
grep -Fq 'production_authority=PQS' "$OBSERVER" || exit 28
grep -Fq 'producer_switch=false' "$OBSERVER" || exit 29
grep -Fq 'db_authority=PQS' "$OBSERVER" || exit 30
grep -Fq 'exact_cpu_db_write=false' "$OBSERVER" || exit 31
grep -Fq 'production_preload_path_mutated=false' "$OBSERVER" || exit 32
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || exit 33

if grep -Eq 'new[[:space:]]+Thread|Task\.|ThreadPool\.|Parallel\.' "$OBSERVER"; then
  echo "STOP: R043 observer contains private worker execution path" >&2
  exit 34
fi
if grep -Eq '\.OnVertexBuildHeight\s*\(' "$OBSERVER"; then
  echo "STOP: R043 observer invokes live height callback" >&2
  exit 35
fi
if grep -Eq 'AERISTerrainPreload(Database|Builder).*\.(Write|Commit|Save|Store|Put)' "$OBSERVER"; then
  echo "STOP: R043 observer contains DB/preload mutation call" >&2
  exit 36
fi

for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
  grep -Fq "\"$body\"" "$OBSERVER" || {
    echo "STOP: R043 target missing: $body" >&2
    exit 37
  }
done

HEAD_SHA="$(git rev-parse HEAD)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS43 R043 PRELOAD PTC SHADOW TILEGRID"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS43 — R043 — PRELOAD PTC SHADOW TILEGRID"; internal const string CandidateName = "$CANDIDATE_NAME"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
text = src.read_text(encoding='utf-8')
observer_needle = '    <Compile Include="Terrain\\AERISR042ExactCpuShadowRuntimeCaptureObserver.cs" />\n'
observer_insert = (
    observer_needle +
    '    <Compile Include="Terrain\\AERISR043PreloadPtcShadowTileGridObserver.cs" />\n' +
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
version_needle = '    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
version_replacement = '    <Compile Include="Properties\\AERISBuildVersion.R043PreloadPtcShadow.generated.cs" />\n'
if text.count(observer_needle) != 1:
    raise SystemExit('R043 observer insertion marker not unique')
if text.count(version_needle) != 1:
    raise SystemExit('R043 version replacement marker not unique')
if 'AERISR043PreloadPtcShadowTileGridObserver.cs' in text:
    raise SystemExit('R043 tile-grid observer unexpectedly present in canonical csproj')
if 'AERISR043RuntimeBuildIdentityObserver.cs' in text:
    raise SystemExit('R043 identity observer unexpectedly present in canonical csproj')
text = text.replace(observer_needle, observer_insert, 1)
text = text.replace(version_needle, version_replacement, 1)
dst.write_text(text, encoding='utf-8')
PY

grep -Fq 'AERISR043PreloadPtcShadowTileGridObserver.cs' "$TEMP_PROJECT" || exit 40
grep -Fq 'AERISR043RuntimeBuildIdentityObserver.cs' "$TEMP_PROJECT" || exit 41
grep -Fq 'AERISBuildVersion.R043PreloadPtcShadow.generated.cs' "$TEMP_PROJECT" || exit 42
if grep -Fq 'AERISR043PreloadPtcShadowTileGridObserver.cs' "$PROJECT"; then
  echo "STOP: proof observer leaked into canonical csproj" >&2
  exit 43
fi

state_value() {
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^${key}=" "$STATE" | cut -d= -f2-
}

harvest_if_ready() {
  [[ -f "$STATE" ]] || return 1
  local state_head state_tree state_sha state_offset state_log state_candidate
  state_head="$(state_value head || true)"
  state_tree="$(state_value source_tree_sha256 || true)"
  state_sha="$(state_value installed_dll_sha || true)"
  state_offset="$(state_value log_offset || true)"
  state_log="$(state_value log || true)"
  state_candidate="$(state_value candidate || true)"
  [[ "$state_head" = "$(git rev-parse HEAD)" ]] || return 1
  [[ "$state_candidate" = "$CANDIDATE_NAME" ]] || return 1
  [[ -n "$state_tree" && -n "$state_sha" && -n "$state_offset" && "$state_log" = "$LOG" ]] || return 1

  mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
  [[ "${#targets[@]}" -eq 1 ]] || return 1
  local installed_sha
  installed_sha="$(sha256sum "${targets[0]}" | awk '{print $1}')"
  [[ "$installed_sha" = "$state_sha" ]] || return 1

  [[ -f "$LOG" ]] || {
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait for R043 PRELOAD_PTC shadow completion, exit KSP, then run the same command again."
    return 0
  }

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS43_R043_PRELOAD_PTC.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  local complete identity
  complete="$(grep -F '[AERIS43][R043_PRELOAD_PTC_SHADOW_COMPLETE]' "$segment" | tail -n 1 || true)"
  identity="$(grep -F '[AERIS43][R043_BUILD_IDENTITY]' "$segment" | tail -n 1 || true)"
  if [[ -z "$complete" ]]; then
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait for R043 PRELOAD_PTC shadow completion, exit KSP, then run the same command again."
    return 0
  fi

  local pass=1
  [[ -n "$identity" ]] || pass=0
  [[ "$identity" == *"; candidate=$CANDIDATE_NAME;"* ]] || pass=0
  [[ "$identity" == *"; source_git_sha=$state_head;"* ]] || pass=0
  [[ "$identity" == *"; source_tree_sha256=$state_tree;"* ]] || pass=0

  [[ "$complete" == *'; pass=true;'* ]] || pass=0
  [[ "$complete" == *'; bodies=7;'* ]] || pass=0
  [[ "$complete" == *'; total_checks=1715;'* ]] || pass=0
  [[ "$complete" == *'; mismatch_count=0;'* ]] || pass=0
  [[ "$complete" == *'; nonfinite_count=0;'* ]] || pass=0
  [[ "$complete" == *'; bit_exact=true;'* ]] || pass=0
  [[ "$complete" == *'; worker_not_main=true;'* ]] || pass=0
  [[ "$complete" == *'; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE;'* ]] || pass=0
  [[ "$complete" == *'; worker_runtime_object_access=false;'* ]] || pass=0
  [[ "$complete" == *'; stage=PRELOAD_PTC_SHADOW_TILEGRID;'* ]] || pass=0
  [[ "$complete" == *'; production_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; producer_switch=false;'* ]] || pass=0
  [[ "$complete" == *'; db_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; exact_cpu_db_write=false;'* ]] || pass=0
  [[ "$complete" == *'; production_preload_path_mutated=false'* ]] || pass=0

  local body_lines
  body_lines="$(grep -F '[AERIS43][R043_PRELOAD_PTC_SHADOW_BODY]' "$segment" | wc -l | tr -d ' ')"
  [[ "$body_lines" = "7" ]] || pass=0
  for body in Minmus Kerbin Eve Duna Dres Moho Eeloo; do
    local line
    line="$(grep -F '[AERIS43][R043_PRELOAD_PTC_SHADOW_BODY]' "$segment" | grep -F "; body=$body;" | tail -n 1 || true)"
    [[ -n "$line" ]] || { pass=0; continue; }
    [[ "$line" == *'; pass=true;'* ]] || pass=0
    [[ "$line" == *'; checks=245;'* ]] || pass=0
    [[ "$line" == *'; mismatch_count=0;'* ]] || pass=0
    [[ "$line" == *'; nonfinite_count=0;'* ]] || pass=0
    [[ "$line" == *'; bit_exact=true;'* ]] || pass=0
    [[ "$line" == *'; worker_not_main=true;'* ]] || pass=0
  done

  echo "=== R043 PRELOAD PTC SHADOW TILEGRID HARVEST ==="
  echo "$identity"
  grep -F '[AERIS43][R043_PRELOAD_PTC_SHADOW_CAPTURE_BODY]' "$segment" || true
  grep -F '[AERIS43][R043_PRELOAD_PTC_SHADOW_BODY]' "$segment" || true
  echo "$complete"
  rm -f "$segment"

  if [[ "$pass" -ne 1 ]]; then
    echo "R043_PRELOAD_PTC_SHADOW_TILEGRID=FAIL"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 50
  fi

  rm -rf "$STATE_DIR"
  echo "R042_PHASE5_ACCEPTANCE=PRESERVED"
  echo "preload_tilegrid_bodies=7"
  echo "preload_tilegrid_lods=5"
  echo "preload_tilegrid_checks_per_body=245"
  echo "preload_tilegrid_total_checks=1715"
  echo "preload_tilegrid_bit_exact=true"
  echo "worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE"
  echo "worker_runtime_object_access=false"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "exact_cpu_db_write=false"
  echo "production_preload_path_mutated=false"
  echo "build_source_git_sha=$state_head"
  echo "build_source_tree_sha256=$state_tree"
  echo "installed_dll_sha256=$state_sha"
  echo "R043_PRELOAD_PTC_SHADOW_TILEGRID=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R043_PRELOAD_PTC_SHADOW_INTEGRATION"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale R043 PRELOAD_PTC state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo
echo "=== XBUILD / R043 PRELOAD PTC TEMP PROOF PROJECT ==="
rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$TEMP_PROJECT")"
)
[[ -f "$DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 60; }

mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
[[ "${#targets[@]}" -eq 1 ]] || {
  echo "STOP: expected exactly one installed AERISFlightControl.dll; found=${#targets[@]}" >&2
  exit 61
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
cmp -s "$DLL" "$TARGET" || { echo "STOP: installed DLL differs from build" >&2; exit 62; }

LOG_OFFSET=0
[[ -f "$LOG" ]] && LOG_OFFSET="$(stat -c %s "$LOG")"
mkdir -p "$STATE_DIR"
cat > "$STATE" <<EOFSTATE
head=$HEAD_SHA
source_tree_sha256=$TREE_SHA256
installed_dll_sha=$NEW_SHA
log_offset=$LOG_OFFSET
log=$LOG
ksp=$KSP
backup=$BACKUP
previous_dll_sha=$OLD_SHA
candidate=$CANDIDATE_NAME
EOFSTATE

echo
echo "=== R043 PRELOAD PTC SHADOW TILEGRID INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "source_tree_sha256=$TREE_SHA256"
echo "candidate=$CANDIDATE_NAME"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "target_bodies=7"
echo "probe_lods=GLOBAL,FAR,ROUTE,LOCAL,LAND"
echo "tiles_per_body=5"
echo "checks_per_body=245"
echo "expected_checks=1715"
echo "proof_project=TEMPORARY_CSPROJ_ONLY"
echo "candidate_version_source=TEMPORARY_HEAD_BOUND_IDENTITY"
echo "canonical_csproj_mutated=false"
echo "production_preload_path_mutated=false"
echo "worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "exact_cpu_db_write=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, wait for R043 PRELOAD_PTC shadow completion, exit KSP, then run the same command again."
