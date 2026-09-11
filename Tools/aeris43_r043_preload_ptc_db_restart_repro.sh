#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris43_r043_preload_ptc_db_restart_repro.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris43-r043-preload-ptc-db-restart-repro"
LIVE_ACCEPTED="b3541429963a3459d6e72fc27564e2be2ce99e7b"
LIVE_CANDIDATE="AERIS43_R043_PRELOAD_PTC_SHADOW_LIVE_PRELOAD"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
CANDIDATE_NAME="AERIS43_R043_PRELOAD_PTC_DB_RESTART_REPRO"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R043PreloadPtcDbRestartRepro.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R043PreloadPtcDbRestartRepro.generated.cs"
RESTART_SEAM="$PROJECT_DIR/Terrain/AERISR043PreloadPtcRestartRepro.cs"
RESTART_OBSERVER="$PROJECT_DIR/Terrain/AERISR043PreloadPtcDbRestartReproObserver.cs"
IDENTITY_OBSERVER="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
PIPELINE="$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs"
PRELOAD="$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs"
DATABASE="$PROJECT_DIR/Terrain/AERISTerrainPreloadDatabase.cs"
CODEC="$PROJECT_DIR/Terrain/AERISTerrainPreloadCodec.cs"
TILE_SYSTEM="$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs"
INTEGRATION="$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs"
LIVE_PROOF="$PROJECT_DIR/Terrain/AERISR043PreloadPtcLiveProof.cs"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA_ROOT/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r043-preload-ptc-db-restart-repro/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() {
  rm -f "$TEMP_PROJECT" "$TEMP_VERSION"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS43 / R043 PRELOAD PTC DB RESTART REPRO ==="
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

for file in "$PROJECT" "$RESTART_SEAM" "$RESTART_OBSERVER"   "$IDENTITY_OBSERVER" "$PIPELINE" "$PRELOAD" "$DATABASE" "$CODEC"   "$TILE_SYSTEM" "$INTEGRATION" "$LIVE_PROOF" "$RESOLVER"; do
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

# Restart repro is read-only. Production DB/codec/write paths and the accepted
# exact-CPU BlockPipeline integration must remain byte-identical to LIVE_ACCEPTED.
for protected_path in   Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs   Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs   Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs   Source/AERISFlightControl/Terrain/AERISR043PreloadPtcIntegratedShadow.cs   Source/AERISFlightControl/Terrain/AERISR043PreloadPtcLiveProof.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs   Source/AERISFlightControl/Terrain/AERIS39AllBodyHeightModifierChainPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERISR039PtcMinmusPureCpuExact.cs; do
  if ! git diff --quiet "$LIVE_ACCEPTED" HEAD -- "$protected_path"; then
    echo "STOP: protected path changed in restart-repro gate: $protected_path" >&2
    exit 18
  fi
done

grep -Fq 'internal sealed partial class AERISTerrainTileSystem' "$TILE_SYSTEM" || exit 20
grep -Fq 'R043LoadRestartProofBatch' "$RESTART_SEAM" || exit 21
grep -Fq 'preloadDatabase.TryLoadBatch' "$RESTART_SEAM" || exit 22
grep -Fq 'null,' "$RESTART_SEAM" || exit 23
grep -Fq 'PQS_THEN_OFFICIAL_CODEC' "$RESTART_OBSERVER" || exit 24
grep -Fq 'AERISTerrainPreloadCodec.Encode' "$RESTART_OBSERVER" || exit 25
grep -Fq 'AERISTerrainPreloadCodec.Decode' "$RESTART_OBSERVER" || exit 26
grep -Fq 'expected' /dev/null 2>/dev/null || true
grep -Fq 'ExpectedSamples = 578' "$RESTART_OBSERVER" || exit 27
grep -Fq 'db_write=false' "$RESTART_OBSERVER" || exit 28
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || exit 29
grep -Fq 'Terrain\AERISR043PreloadPtcRestartRepro.cs' "$PROJECT" || exit 30

if grep -Eq 'new[[:space:]]+Thread|Task\.|ThreadPool\.|Parallel\.' "$RESTART_SEAM" "$RESTART_OBSERVER"; then
  echo "STOP: restart repro contains private worker path" >&2
  exit 31
fi
if grep -Eq 'SaveEncodedBatch|AtomicReplace|WriteChunk' "$RESTART_SEAM" "$RESTART_OBSERVER"; then
  echo "STOP: restart repro contains DB write operation" >&2
  exit 32
fi

field_value() {
  local line="$1"
  local key="$2"
  printf '%s\n' "$line" | sed -n "s/.*; ${key}=\([^;]*\).*/\1/p"
}

# Bind this proof to the immediately preceding LIVE_PRELOAD accepted runtime.
[[ -f "$LOG" ]] || {
  echo "STOP: AERIS log missing; LIVE_PRELOAD evidence required first" >&2
  exit 33
}
PRIOR_IDENTITY="$(grep -F '[AERIS43][R043_BUILD_IDENTITY]' "$LOG" |
  grep -F "; candidate=$LIVE_CANDIDATE;" |
  grep -F "; source_git_sha=$LIVE_ACCEPTED;" | tail -n 1 || true)"
PRIOR_COMPLETE="$(grep -F '[AERIS43][R043_PRELOAD_PTC_LIVE_COMPLETE]' "$LOG" |
  grep -F '; pass=true;' | tail -n 1 || true)"
[[ -n "$PRIOR_IDENTITY" && -n "$PRIOR_COMPLETE" ]] || {
  echo "STOP: accepted LIVE_PRELOAD identity/completion not found in log" >&2
  exit 34
}
[[ "$PRIOR_COMPLETE" == *'; expected_total_checks=578;'* ]] || exit 35
[[ "$PRIOR_COMPLETE" == *'; path=REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB;'* ]] || exit 36

EXPECTED_MINMUS_ID="$(field_value "$PRIOR_COMPLETE" minmus_stable_id)"
EXPECTED_KERBIN_ID="$(field_value "$PRIOR_COMPLETE" kerbin_stable_id)"
[[ -n "$EXPECTED_MINMUS_ID" && -n "$EXPECTED_KERBIN_ID" ]] || {
  echo "STOP: LIVE_PRELOAD stable IDs missing" >&2
  exit 37
}

HEAD_SHA="$(git rev-parse HEAD)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS43 R043 PRELOAD PTC DB RESTART REPRO"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS43 — R043 — PRELOAD PTC DB RESTART REPRO"; internal const string CandidateName = "$CANDIDATE_NAME"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1])
dst = pathlib.Path(sys.argv[2])
text = src.read_text(encoding='utf-8')
observer_needle = '    <Compile Include="Terrain\\AERISR043PreloadPtcRestartRepro.cs" />\n'
observer_insert = (
    observer_needle +
    '    <Compile Include="Terrain\\AERISR043PreloadPtcDbRestartReproObserver.cs" />\n' +
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
version_needle = '    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
version_replacement = '    <Compile Include="Properties\\AERISBuildVersion.R043PreloadPtcDbRestartRepro.generated.cs" />\n'
if text.count(observer_needle) != 1:
    raise SystemExit('restart observer insertion marker not unique')
if text.count(version_needle) != 1:
    raise SystemExit('restart version replacement marker not unique')
if 'AERISR043PreloadPtcDbRestartReproObserver.cs' in text:
    raise SystemExit('restart observer unexpectedly in canonical csproj')
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

harvest_if_ready() {
  [[ -f "$STATE" ]] || return 1
  local state_head state_tree state_sha state_offset state_log state_candidate
  local state_minmus state_kerbin
  state_head="$(state_value head || true)"
  state_tree="$(state_value source_tree_sha256 || true)"
  state_sha="$(state_value installed_dll_sha || true)"
  state_offset="$(state_value log_offset || true)"
  state_log="$(state_value log || true)"
  state_candidate="$(state_value candidate || true)"
  state_minmus="$(state_value expected_minmus_id || true)"
  state_kerbin="$(state_value expected_kerbin_id || true)"

  [[ "$state_head" = "$(git rev-parse HEAD)" ]] || return 1
  [[ "$state_candidate" = "$CANDIDATE_NAME" ]] || return 1
  [[ -n "$state_tree" && -n "$state_sha" && -n "$state_offset" &&
     "$state_log" = "$LOG" && -n "$state_minmus" && -n "$state_kerbin" ]] || return 1

  mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
  [[ "${#targets[@]}" -eq 1 ]] || return 1
  local installed_sha
  installed_sha="$(sha256sum "${targets[0]}" | awk '{print $1}')"
  [[ "$installed_sha" = "$state_sha" ]] || return 1

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS43_R043_PTC_RESTART.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  local complete identity
  complete="$(grep -F '[AERIS43][R043_PRELOAD_PTC_DB_RESTART_REPRO_COMPLETE]' "$segment" | tail -n 1 || true)"
  identity="$(grep -F '[AERIS43][R043_BUILD_IDENTITY]' "$segment" | tail -n 1 || true)"

  if [[ -z "$complete" ]]; then
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu. This must be a fresh process after the LIVE_PRELOAD run. Wait for DB_RESTART_REPRO completion, exit KSP, then run the same command again."
    return 0
  fi

  local pass=1
  [[ -n "$identity" ]] || pass=0
  [[ "$identity" == *"; candidate=$CANDIDATE_NAME;"* ]] || pass=0
  [[ "$identity" == *"; source_git_sha=$state_head;"* ]] || pass=0
  [[ "$identity" == *"; source_tree_sha256=$state_tree;"* ]] || pass=0

  [[ "$complete" == *'; pass=true;'* ]] || pass=0
  [[ "$complete" == *'; bodies=2;'* ]] || pass=0
  [[ "$complete" == *'; total_checks=578;'* ]] || pass=0
  [[ "$complete" == *'; elevation_bit_mismatch=0;'* ]] || pass=0
  [[ "$complete" == *'; flag_mismatch=0;'* ]] || pass=0
  [[ "$complete" == *'; worker_not_main=true;'* ]] || pass=0
  [[ "$complete" == *'; db_read=TryLoadBatch;'* ]] || pass=0
  [[ "$complete" == *'; reference=PQS_THEN_OFFICIAL_CODEC;'* ]] || pass=0
  [[ "$complete" == *'; restart_process=true;'* ]] || pass=0
  [[ "$complete" == *'; persistent_db_read=true;'* ]] || pass=0
  [[ "$complete" == *'; db_write=false;'* ]] || pass=0
  [[ "$complete" == *'; production_authority=PQS;'* ]] || pass=0
  [[ "$complete" == *'; producer_switch=false;'* ]] || pass=0
  [[ "$complete" == *'; db_authority=PQS'* ]] || pass=0

  for body in Minmus Kerbin; do
    local prepare read bodyline expected_id prepare_id read_id body_id
    prepare="$(grep -F '[AERIS43][R043_PRELOAD_PTC_RESTART_PREPARE]' "$segment" |
      grep -F "; body=$body;" | tail -n 1 || true)"
    read="$(grep -F '[AERIS43][R043_PRELOAD_PTC_RESTART_DB_READ]' "$segment" |
      grep -F "; body=$body;" | tail -n 1 || true)"
    bodyline="$(grep -F '[AERIS43][R043_PRELOAD_PTC_RESTART_BODY]' "$segment" |
      grep -F "; body=$body;" | tail -n 1 || true)"

    [[ -n "$prepare" && -n "$read" && -n "$bodyline" ]] || {
      pass=0
      continue
    }

    [[ "$prepare" == *'; pass=true;'* ]] || pass=0
    [[ "$prepare" == *'; database_contains=true;'* ]] || pass=0
    [[ "$prepare" == *'; restart_process=true;'* ]] || pass=0
    [[ "$prepare" == *'; db_write=false;'* ]] || pass=0

    [[ "$read" == *'; pass=true;'* ]] || pass=0
    [[ "$read" == *'; source=PreloadDatabase;'* ]] || pass=0
    [[ "$read" == *'; resolution=17;'* ]] || pass=0
    [[ "$read" == *'; sampling_complete=true;'* ]] || pass=0
    [[ "$read" == *'; persistent_db_read=true;'* ]] || pass=0
    [[ "$read" == *'; db_write=false;'* ]] || pass=0

    [[ "$bodyline" == *'; pass=true;'* ]] || pass=0
    [[ "$bodyline" == *'; checks=289;'* ]] || pass=0
    [[ "$bodyline" == *'; elevation_bit_mismatch=0;'* ]] || pass=0
    [[ "$bodyline" == *'; flag_mismatch=0;'* ]] || pass=0
    [[ "$bodyline" == *'; geometry_exact=true;'* ]] || pass=0
    [[ "$bodyline" == *'; metadata_valid=true;'* ]] || pass=0
    [[ "$bodyline" == *'; db_source=PreloadDatabase;'* ]] || pass=0
    [[ "$bodyline" == *'; reference=PQS_THEN_OFFICIAL_CODEC;'* ]] || pass=0
    [[ "$bodyline" == *'; restart_process=true;'* ]] || pass=0
    [[ "$bodyline" == *'; db_write=false;'* ]] || pass=0

    if [[ "$body" = "Minmus" ]]; then
      expected_id="$state_minmus"
    else
      expected_id="$state_kerbin"
    fi
    prepare_id="$(field_value "$prepare" stable_id)"
    read_id="$(field_value "$read" stable_id)"
    body_id="$(field_value "$bodyline" stable_id)"
    [[ -n "$prepare_id" && "$prepare_id" = "$expected_id" &&
       "$read_id" = "$expected_id" && "$body_id" = "$expected_id" ]] || pass=0
  done

  echo "=== R043 PRELOAD PTC DB RESTART REPRO HARVEST ==="
  echo "$identity"
  grep -F '[AERIS43][R043_PRELOAD_PTC_RESTART_PREPARE]' "$segment" || true
  grep -F '[AERIS43][R043_PRELOAD_PTC_RESTART_DB_READ]' "$segment" || true
  grep -F '[AERIS43][R043_PRELOAD_PTC_RESTART_BODY]' "$segment" || true
  echo "$complete"
  rm -f "$segment"

  if [[ "$pass" -ne 1 ]]; then
    echo "R043_PRELOAD_PTC_DB_RESTART_REPRO=FAIL"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 50
  fi

  rm -rf "$STATE_DIR"
  echo "R042_PHASE5_ACCEPTANCE=PRESERVED"
  echo "R043_PRELOAD_PTC_TILEGRID_ACCEPTANCE=PRESERVED"
  echo "R043_PRELOAD_PTC_INTEGRATION_ACCEPTANCE=PRESERVED"
  echo "R043_PRELOAD_PTC_LIVE_PRELOAD_ACCEPTANCE=PRESERVED"
  echo "restart_repro_bodies=2"
  echo "restart_repro_total_checks=578"
  echo "restart_repro_elevation_bit_exact=true"
  echo "restart_repro_flags_exact=true"
  echo "restart_repro_geometry_exact=true"
  echo "restart_repro_metadata_valid=true"
  echo "db_read=TryLoadBatch"
  echo "reference=PQS_THEN_OFFICIAL_CODEC"
  echo "restart_process=true"
  echo "persistent_db_read=true"
  echo "db_write=false"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_authority=PQS"
  echo "build_source_git_sha=$state_head"
  echo "build_source_tree_sha256=$state_tree"
  echo "installed_dll_sha256=$state_sha"
  echo "R043_PRELOAD_PTC_DB_RESTART_REPRO=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R043_PRELOAD_PTC_PROMOTION_READINESS_AUDIT"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale R043 restart-repro state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo
echo "=== XBUILD / R043 PRELOAD PTC DB RESTART REPRO ==="
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
expected_minmus_id=$EXPECTED_MINMUS_ID
expected_kerbin_id=$EXPECTED_KERBIN_ID
EOFSTATE

echo
echo "=== R043 PRELOAD PTC DB RESTART REPRO INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "source_tree_sha256=$TREE_SHA256"
echo "candidate=$CANDIDATE_NAME"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "prior_live_head=$LIVE_ACCEPTED"
echo "expected_minmus_stable_id=$EXPECTED_MINMUS_ID"
echo "expected_kerbin_stable_id=$EXPECTED_KERBIN_ID"
echo "restart_repro_bodies=Minmus,Kerbin"
echo "restart_repro_lod=GLOBAL"
echo "restart_repro_resolution=17"
echo "checks_per_body=289"
echo "expected_checks=578"
echo "db_read=TryLoadBatch"
echo "reference=PQS_THEN_OFFICIAL_CODEC"
echo "persistent_db_read=true"
echo "db_write=false"
echo "preload_database_mutated=false"
echo "preload_codec_mutated=false"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu. This must be a fresh process after the LIVE_PRELOAD run. Wait for DB_RESTART_REPRO completion, exit KSP, then run the same command again."
