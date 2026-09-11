#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris43_r043_preload_ptc_shadow_live_preload.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris43-r043-preload-ptc-shadow-live-preload"
INTEGRATION_ACCEPTED="7689376a73865e33b273809e89a9d617687ccc1c"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
CANDIDATE_NAME="AERIS43_R043_PRELOAD_PTC_SHADOW_LIVE_PRELOAD"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R043PreloadPtcLivePreload.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R043PreloadPtcLivePreload.generated.cs"
INTEGRATION="$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs"
LIVE_PROOF="$PROJECT_DIR/Terrain/AERISR043PreloadPtcLiveProof.cs"
LIVE_OBSERVER="$PROJECT_DIR/Terrain/AERISR043PreloadPtcLivePreloadObserver.cs"
IDENTITY_OBSERVER="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
PIPELINE="$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs"
PRELOAD="$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs"
DATABASE="$PROJECT_DIR/Terrain/AERISTerrainPreloadDatabase.cs"
TILE_SYSTEM="$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs"
AWARENESS="$PROJECT_DIR/Terrain/AERISTerrainAwareness.cs"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
SNAPSHOT="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs"
SNAPSHOT_BUILDER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA_ROOT/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r043-preload-ptc-shadow-live-preload/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() {
  rm -f "$TEMP_PROJECT" "$TEMP_VERSION"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS43 / R043 PRELOAD PTC SHADOW LIVE PRELOAD ==="
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

for file in "$PROJECT" "$INTEGRATION" "$LIVE_PROOF" "$LIVE_OBSERVER"   "$IDENTITY_OBSERVER" "$PIPELINE" "$PRELOAD" "$DATABASE" "$TILE_SYSTEM"   "$AWARENESS" "$RESOLVER" "$SNAPSHOT" "$SNAPSHOT_BUILDER"; do
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

# The live gate may add proof/evidence seams to the PreloadBuilder and TileSystem,
# but the database codec/storage implementation and R042 exact evaluator contracts
# remain byte-identical to the accepted real-BlockPipeline integration.
for protected_path in   Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs   Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs   Source/AERISFlightControl/Terrain/AERIS39AllBodyHeightModifierChainPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERISR039PtcMinmusPureCpuExact.cs; do
  if ! git diff --quiet "$INTEGRATION_ACCEPTED" HEAD -- "$protected_path"; then
    echo "STOP: protected path changed in live-preload gate: $protected_path" >&2
    exit 18
  fi
done

grep -Fq 'internal sealed partial class AERISTerrainPreloadBuilder' "$PRELOAD" || exit 20
grep -Fq 'R043NoteDurableBatch(payload);' "$PRELOAD" || exit 21
grep -Fq 'R043RequestLivePreloadProof' "$LIVE_PROOF" || exit 22
grep -Fq 'database.SaveEncodedBatch' "$PRELOAD" || exit 23
grep -Fq 'database.Contains(encoded.Key)' "$LIVE_PROOF" || exit 24
grep -Fq 'R043RegisterLivePreloadProofStableId' "$LIVE_PROOF" || exit 25
grep -Fq 'live_preload_proof=' "$INTEGRATION" || exit 26
grep -Fq 'R043RequestLivePreloadProof' "$TILE_SYSTEM" || exit 27
grep -Fq 'R043LivePreloadProofDurable' "$TILE_SYSTEM" || exit 28
grep -Fq 'expected_total_checks=578' "$LIVE_OBSERVER" || exit 29
grep -Fq 'REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB' "$LIVE_OBSERVER" || exit 30
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || exit 31
grep -Fq 'Terrain\AERISR043PreloadPtcLiveProof.cs' "$PROJECT" || exit 32

if grep -Eq 'new[[:space:]]+Thread|Task\.|ThreadPool\.|Parallel\.' "$LIVE_PROOF"; then
  echo "STOP: live proof seam contains private worker path" >&2
  exit 33
fi
if grep -Eq '\.OnVertexBuildHeight[[:space:]]*\(' "$LIVE_PROOF"; then
  echo "STOP: live proof seam invokes live height callback" >&2
  exit 34
fi

HEAD_SHA="$(git rev-parse HEAD)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS43 R043 PRELOAD PTC SHADOW LIVE PRELOAD"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS43 — R043 — PRELOAD PTC SHADOW LIVE PRELOAD"; internal const string CandidateName = "$CANDIDATE_NAME"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
text = src.read_text(encoding='utf-8')
observer_needle = '    <Compile Include="Terrain\\AERISR043PreloadPtcLiveProof.cs" />\n'
observer_insert = (
    observer_needle +
    '    <Compile Include="Terrain\\AERISR043PreloadPtcLivePreloadObserver.cs" />\n' +
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
version_needle = '    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
version_replacement = '    <Compile Include="Properties\\AERISBuildVersion.R043PreloadPtcLivePreload.generated.cs" />\n'
if text.count(observer_needle) != 1:
    raise SystemExit('R043 live observer insertion marker not unique')
if text.count(version_needle) != 1:
    raise SystemExit('R043 live version replacement marker not unique')
if 'AERISR043PreloadPtcLivePreloadObserver.cs' in text:
    raise SystemExit('live observer unexpectedly in canonical csproj')
if 'AERISR043RuntimeBuildIdentityObserver.cs' in text:
    raise SystemExit('identity observer unexpectedly in canonical csproj')
text = text.replace(observer_needle, observer_insert, 1)
text = text.replace(version_needle, version_replacement, 1)
dst.write_text(text, encoding='utf-8')
PY

state_value() {
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^${key}=" "$STATE" | cut -d= -f2-
}

field_value() {
  local line="$1"
  local key="$2"
  printf '%s\n' "$line" | sed -n "s/.*; ${key}=\([^;]*\).*/\1/p"
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
    echo "human_action=Launch KSP to Main Menu, leave Preload enabled, wait for LIVE_PRELOAD completion, exit KSP, then run the same command again."
    return 0
  }

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS43_R043_PTC_LIVE.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  local complete identity
  complete="$(grep -F '[AERIS43][R043_PRELOAD_PTC_LIVE_COMPLETE]' "$segment" | tail -n 1 || true)"
  identity="$(grep -F '[AERIS43][R043_BUILD_IDENTITY]' "$segment" | tail -n 1 || true)"

  if [[ -z "$complete" ]]; then
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, leave Preload enabled, wait for LIVE_PRELOAD completion, exit KSP, then run the same command again."
    return 0
  fi

  local pass=1
  [[ -n "$identity" ]] || pass=0
  [[ "$identity" == *"; candidate=$CANDIDATE_NAME;"* ]] || pass=0
  [[ "$identity" == *"; source_git_sha=$state_head;"* ]] || pass=0
  [[ "$identity" == *"; source_tree_sha256=$state_tree;"* ]] || pass=0

  [[ "$complete" == *'; pass=true;'* ]] || pass=0
  [[ "$complete" == *'; bodies=2;'* ]] || pass=0
  [[ "$complete" == *'; evaluator_families=2;'* ]] || pass=0
  [[ "$complete" == *'; expected_total_checks=578;'* ]] || pass=0
  [[ "$complete" == *'; path=REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB;'* ]] || pass=0
  [[ "$complete" == *'; db_commit=SaveEncodedBatch;'* ]] || pass=0
  [[ "$complete" == *'; production_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; producer_switch=false;'* ]] || pass=0
  [[ "$complete" == *'; db_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; exact_cpu_db_write=false;'* ]] || pass=0

  for body in Minmus Kerbin; do
    local tile durable tile_id durable_id
    tile="$(grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$segment" |
      grep -F '; live_preload_proof=true;' | grep -F "; body=$body;" | tail -n 1 || true)"
    durable="$(grep -F '[AERIS43][R043_PRELOAD_PTC_LIVE_DB_DURABLE]' "$segment" |
      grep -F "; body=$body;" | tail -n 1 || true)"

    [[ -n "$tile" && -n "$durable" ]] || { pass=0; continue; }

    [[ "$tile" == *'; pass=true;'* ]] || pass=0
    [[ "$tile" == *'; lod=Global;'* ]] || pass=0
    [[ "$tile" == *'; resolution=17;'* ]] || pass=0
    [[ "$tile" == *'; checks=289;'* ]] || pass=0
    [[ "$tile" == *'; expected_checks=289;'* ]] || pass=0
    [[ "$tile" == *'; mismatch_count=0;'* ]] || pass=0
    [[ "$tile" == *'; nonfinite_count=0;'* ]] || pass=0
    [[ "$tile" == *'; bit_exact=true;'* ]] || pass=0
    [[ "$tile" == *'; worker_not_main=true;'* ]] || pass=0
    [[ "$tile" == *'; work_owner=PreloadBuilder;'* ]] || pass=0
    [[ "$tile" == *'; tile_commit_authority=PQS;'* ]] || pass=0
    [[ "$tile" == *'; exact_cpu_db_write=false;'* ]] || pass=0

    [[ "$durable" == *'; pass=true;'* ]] || pass=0
    [[ "$durable" == *'; key_match=true;'* ]] || pass=0
    [[ "$durable" == *'; database_contains=true;'* ]] || pass=0
    [[ "$durable" == *'; save_encoded_batch=true;'* ]] || pass=0
    [[ "$durable" == *'; db_authority=PQS;'* ]] || pass=0
    [[ "$durable" == *'; exact_cpu_db_write=false'* ]] || pass=0

    tile_id="$(field_value "$tile" stable_id)"
    durable_id="$(field_value "$durable" stable_id)"
    [[ -n "$tile_id" && "$tile_id" = "$durable_id" ]] || pass=0
  done

  echo "=== R043 PRELOAD PTC SHADOW LIVE PRELOAD HARVEST ==="
  echo "$identity"
  grep -F '[AERIS43][R043_PRELOAD_PTC_LIVE_REQUEST]' "$segment" || true
  grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$segment" |
    grep -F '; live_preload_proof=true;' || true
  grep -F '[AERIS43][R043_PRELOAD_PTC_LIVE_DB_DURABLE]' "$segment" || true
  echo "$complete"
  rm -f "$segment"

  if [[ "$pass" -ne 1 ]]; then
    echo "R043_PRELOAD_PTC_SHADOW_LIVE_PRELOAD=FAIL"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 50
  fi

  rm -rf "$STATE_DIR"
  echo "R042_PHASE5_ACCEPTANCE=PRESERVED"
  echo "R043_PRELOAD_PTC_TILEGRID_ACCEPTANCE=PRESERVED"
  echo "R043_PRELOAD_PTC_INTEGRATION_ACCEPTANCE=PRESERVED"
  echo "live_preload_path=REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB"
  echo "live_preload_bodies=2"
  echo "live_preload_evaluator_families=2"
  echo "live_preload_checks_per_body=289"
  echo "live_preload_total_checks=578"
  echo "live_preload_bit_exact=true"
  echo "db_commit=SaveEncodedBatch"
  echo "db_durable_witness=true"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "exact_cpu_db_write=false"
  echo "build_source_git_sha=$state_head"
  echo "build_source_tree_sha256=$state_tree"
  echo "installed_dll_sha256=$state_sha"
  echo "R043_PRELOAD_PTC_SHADOW_LIVE_PRELOAD=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R043_PRELOAD_PTC_DB_RESTART_REPRO"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale R043 live-preload state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo
echo "=== XBUILD / R043 PRELOAD PTC SHADOW LIVE PRELOAD ==="
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
echo "=== R043 PRELOAD PTC SHADOW LIVE PRELOAD INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "source_tree_sha256=$TREE_SHA256"
echo "candidate=$CANDIDATE_NAME"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "live_preload_path=REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB"
echo "proof_bodies=Minmus,Kerbin"
echo "evaluator_families=R039_MINMUS,R041_HEIGHT_CHAIN"
echo "proof_lod=GLOBAL"
echo "proof_resolution=17"
echo "checks_per_body=289"
echo "expected_checks=578"
echo "db_commit=SaveEncodedBatch"
echo "existing_db_deleted=false"
echo "rebuild_forced=false"
echo "exact_cpu_db_write=false"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, leave Preload enabled, wait for LIVE_PRELOAD completion, exit KSP, then run the same command again."
