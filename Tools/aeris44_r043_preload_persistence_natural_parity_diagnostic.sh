#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris44_r043_preload_persistence_natural_parity_diagnostic.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris44-r043-preload-persistence-natural-parity-diagnostic"
BASE="b3541429963a3459d6e72fc27564e2be2ce99e7b"
CANDIDATE="AERIS44_R043_PRELOAD_PERSISTENCE_NATURAL_PARITY_DIAGNOSTIC"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R044Diagnostic.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R044Diagnostic.generated.cs"
IDENTITY="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r044-preload-diagnostic/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup(){ rm -f "$TEMP_PROJECT" "$TEMP_VERSION"; }
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS44 / R043 PRELOAD PERSISTENCE + NATURAL PARITY DIAGNOSTIC ==="
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
[[ -f "$PROJECT" && -f "$IDENTITY" ]] || exit 12

for protected in   Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs   Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs   Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs; do
  git diff --quiet "$BASE" HEAD -- "$protected" || {
    echo "STOP: protected source changed: $protected" >&2
    exit 13
  }
done

grep -Fq 'first_mismatch_index='   Source/AERISFlightControl/Terrain/AERISR043PreloadPtcIntegratedShadow.cs || exit 14
grep -Fq '[AERIS44][R043_PRELOAD_ENV_OBSERVED]'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 15
grep -Fq '[AERIS44][R043_PRELOAD_ENV_TRANSITION]'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 16
grep -Fq '[AERIS44][R043_PRELOAD_STATE_BODY]'   Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs || exit 17

HEAD_SHA="$(git rev-parse HEAD)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS44 R043 PRELOAD DIAGNOSTIC"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS44 — R043 PRELOAD PERSISTENCE / NATURAL PARITY DIAGNOSTIC"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
v='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
vr='    <Compile Include="Properties\\AERISBuildVersion.R044Diagnostic.generated.cs" />\n'
marker='  </ItemGroup>\n  <Import Project="$(MSBuildToolsPath)\\Microsoft.CSharp.targets" />'
if text.count(v)!=1: raise SystemExit("version marker not unique")
if text.count(marker)!=1: raise SystemExit("compile itemgroup marker not unique")
text=text.replace(v,vr,1)
# Add only runtime identity observer to the existing compile ItemGroup.
text=text.replace(marker,
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'+marker,1)
dst.write_text(text,encoding="utf-8")
PY

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^${key}=" "$STATE" | cut -d= -f2-
}

harvest(){
  [[ -f "$STATE" ]] || return 1
  local h sha off
  h="$(state_value head || true)"
  sha="$(state_value dll_sha || true)"
  off="$(state_value log_offset || true)"
  [[ "$h" = "$(git rev-parse HEAD)" && -n "$sha" && -n "$off" ]] || return 1
  [[ -f "$TARGET" && "$(sha256sum "$TARGET"|awk '{print $1}')" = "$sha" ]] || return 1
  [[ -f "$LOG" ]] || return 1

  local seg current start
  seg="$(mktemp /tmp/AERIS44_PRELOAD_DIAG.XXXXXX)"
  current="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( current >= off )); then tail -c +"$start" "$LOG" > "$seg"; else cp "$LOG" "$seg"; fi

  local identity state_load env mismatch transition
  identity="$(grep -F '[AERIS43][R043_BUILD_IDENTITY]' "$seg" | tail -n1 || true)"
  state_load="$(grep -F '[AERIS44][R043_PRELOAD_STATE_LOAD]' "$seg" | tail -n1 || true)"
  env="$(grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$seg" | grep -F '; body=Kerbin;' | tail -n1 || true)"
  transition="$(grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" | tail -n1 || true)"
  mismatch="$(grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$seg" |
    grep -F '; body=Kerbin;' | grep -F '; pass=false;' |
    grep -F 'first_mismatch_index=' | head -n1 || true)"

  if [[ -z "$identity" || -z "$state_load" || -z "$env" || -z "$mismatch" ]]; then
    rm -f "$seg"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_DIAGNOSTIC_EVIDENCE"
    echo "human_action=Launch KSP to Main Menu with Automatic Preload enabled. Wait until Kerbin preload advances and at least one natural parity mismatch is logged, then exit KSP and run the same command again."
    return 0
  fi

  echo "=== AERIS44 DIAGNOSTIC HARVEST ==="
  echo "$identity"
  echo "$state_load"
  grep -F '[AERIS44][R043_PRELOAD_STATE_BODY]' "$seg" || true
  grep -F '[AERIS44][R043_PRELOAD_ENV_OBSERVED]' "$seg" || true
  if [[ -n "$transition" ]]; then
    echo "=== ENVIRONMENT TRANSITIONS DETECTED ==="
    grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" || true
  else
    echo "environment_transition_detected=false"
  fi
  echo "=== FIRST KERBIN NATURAL PARITY FAILURE ==="
  echo "$mismatch"
  rm -f "$seg"

  echo "AERIS_CURRENT_STAGE=DIAGNOSTIC_CAPTURED"
  echo "next=ANALYZE_PRELOAD_PERSISTENCE_AND_FIRST_NATURAL_MISMATCH"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest; then exit 0; fi
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

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
OLD_SHA="$(sha256sum "$TARGET"|awk '{print $1}')"
NEW_SHA="$(sha256sum "$DLL"|awk '{print $1}')"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
mkdir -p "$BACKUP_DIR"
BACKUP="$BACKUP_DIR/$(date +%Y%m%d-%H%M%S)-$OLD_SHA.AERISFlightControl.dll"
cp -a "$TARGET" "$BACKUP"
install -m0644 "$DLL" "$TARGET"

OFFSET=0
[[ -f "$LOG" ]] && OFFSET="$(stat -c %s "$LOG")"
mkdir -p "$STATE_DIR"
cat > "$STATE" <<EOFSTATE
head=$HEAD_SHA
dll_sha=$NEW_SHA
log_offset=$OFFSET
backup=$BACKUP
candidate=$CANDIDATE
EOFSTATE

echo "=== AERIS44 DIAGNOSTIC INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$OFFSET"
echo "database_deleted=false"
echo "database_rebuilt=false"
echo "producer_switch=false"
echo "db_authority=PQS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu with Automatic Preload enabled. Wait until Kerbin preload advances and at least one natural parity mismatch is logged, then exit KSP and run the same command again."
