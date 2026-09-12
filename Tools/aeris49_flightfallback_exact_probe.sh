#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris49_flightfallback_exact_probe.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris49-terrain-nd-env4-unified-producer-hotfix"
ACCEPTED_PRELOAD="7f300b125fa7dc06e383dc1464da3af5d0396065"
PROBE_CANDIDATE="AERIS49_FLIGHTFALLBACK_EXACT_PROBE"
FINAL_CANDIDATE="AERIS49_TERRAIN_ND_ENV4_UNIFIED_PRODUCER_FINAL"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R049FlightFallbackProbe.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R049FlightFallbackProbe.generated.cs"
FINAL_PROJECT="$PROJECT_DIR/AERISFlightControl.R049UnifiedProducerFinal.csproj"
FINAL_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R049UnifiedProducerFinal.generated.cs"
PROBE_SOURCE="$PROJECT_DIR/Core/AERISR049FlightFallbackExactProbeObserver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/aeris49-flightfallback-probe/$KEY"
STATE="$STATE_DIR/state.txt"
DONE="$STATE_DIR/done.txt"

cleanup(){
  rm -f "$TEMP_PROJECT" "$TEMP_VERSION" "$FINAL_PROJECT" "$FINAL_VERSION"
}
trap cleanup EXIT

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}

segment_from_offset(){
  local off="$1" out="$2" size start
  size="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( size >= off )); then
    tail -c +"$start" "$LOG" > "$out"
  else
    cp "$LOG" "$out"
  fi
}

prepare_project(){
  local src="$1" dst="$2" version_file="$3" include_probe="$4"
  python3 - "$src" "$dst" "$version_file" "$include_probe" <<'PY'
import pathlib,sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
version_file=sys.argv[3]
include_probe=sys.argv[4].lower()=="true"
text=src.read_text(encoding="utf-8")
marker='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
parts=[f'    <Compile Include="Properties\\{version_file}" />\n',
       '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n']
if include_probe:
    parts.append('    <Compile Include="Core\\AERISR049FlightFallbackExactProbeObserver.cs" />\n')
replacement=''.join(parts)
if text.count(marker)!=1:
    raise SystemExit("version marker not unique")
if 'AERISR043RuntimeBuildIdentityObserver.cs' in text:
    raise SystemExit("identity observer unexpectedly canonical")
if 'AERISR049FlightFallbackExactProbeObserver.cs' in text:
    raise SystemExit("probe observer unexpectedly canonical")
dst.write_text(text.replace(marker,replacement,1),encoding="utf-8")
PY
}

build_candidate(){
  local project_path="$1"
  rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
  (
    cd "$PROJECT_DIR"
    xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$project_path")"
  )
  [[ -f "$DLL" ]] || {
    echo "STOP: build returned without DLL" >&2
    exit 30
  }
}

install_candidate(){
  local label="$1"
  mapfile -t targets < <(find "$GAME_DATA" -type f -name AERISFlightControl.dll -print)
  [[ "${#targets[@]}" -eq 1 ]] || {
    echo "STOP: expected one installed DLL; found=${#targets[@]}" >&2
    exit 31
  }
  TARGET="${targets[0]}"
  local old_sha new_sha backup_dir backup
  old_sha="$(sha256sum "$TARGET" | awk '{print $1}')"
  new_sha="$(sha256sum "$DLL" | awk '{print $1}')"
  backup_dir="$HOME/.cache/AERIS/mapso3-build-backups"
  mkdir -p "$backup_dir"
  backup="$backup_dir/$(date +%Y%m%d-%H%M%S)-$old_sha.AERISFlightControl.dll"
  cp -a "$TARGET" "$backup"
  install -m0644 "$DLL" "$TARGET"
  echo "$label.dll_sha=$new_sha"
  echo "$label.previous_dll_sha=$old_sha"
  echo "$label.backup=$backup"
}

cd "$ROOT"

echo "=== AERIS49 / FLIGHTFALLBACK EXACT CPU PROBE ==="
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
git merge-base --is-ancestor "$ACCEPTED_PRELOAD" HEAD || {
  echo "STOP: accepted PRELOAD_PTC base missing" >&2
  exit 12
}
[[ -f "$PROBE_SOURCE" ]] || {
  echo "STOP: probe observer source missing" >&2
  exit 13
}
! grep -Fq 'AERISR049FlightFallbackExactProbeObserver.cs' "$PROJECT" || {
  echo "STOP: probe observer must not be canonical" >&2
  exit 14
}
grep -Fq 'EXACT_CPU_ALL_PERSISTENT_OWNERS_V2'   "$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs" || {
  echo "STOP: V2 producer policy missing" >&2
  exit 15
}
grep -Fq 'request.WorkOwner != AERISTerrainWorkOwner.FlightFallback'   "$PROJECT_DIR/Terrain/AERISR043PreloadPtcPipeline.cs" || {
  echo "STOP: FlightFallback exact route missing" >&2
  exit 16
}

if [[ -f "$DONE" ]]; then
  cat "$DONE"
  exit 0
fi

HEAD_SHA="$(git rev-parse HEAD)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

# A failed probe from an older source HEAD must not pin the next diagnostic run.
# Its synthetic tile was nonpersistent, so re-arming only needs to discard probe state.
if [[ -f "$STATE" && "$(state_value head || true)" != "$HEAD_SHA" ]]; then
  echo "AERIS49_PROBE_STATE_RESET=SOURCE_HEAD_CHANGED"
  rm -f "$STATE" "$DONE"
fi

if [[ ! -f "$STATE" ]]; then
  if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
    echo "STOP: KSP must be fully exited before installing probe build" >&2
    exit 20
  fi

  rm -rf "$STATE_DIR"
  mkdir -p "$STATE_DIR"

  cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS49 FLIGHTFALLBACK EXACT PROBE"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS49 — FLIGHTFALLBACK EXACT PROBE"; internal const string CandidateName = "$PROBE_CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

  prepare_project "$PROJECT" "$TEMP_PROJECT"     "AERISBuildVersion.R049FlightFallbackProbe.generated.cs" true
  build_candidate "$TEMP_PROJECT"

  install_output="$(install_candidate probe)"
  echo "$install_output"

  OFFSET=0
  [[ -f "$LOG" ]] && OFFSET="$(stat -c %s "$LOG")"
  PROBE_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"

  cat > "$STATE" <<EOFSTATE
head=$HEAD_SHA
probe_dll_sha=$PROBE_SHA
log_offset=$OFFSET
EOFSTATE

  echo "AERIS49_FLIGHTFALLBACK_PROBE=ARMED"
  echo "probe_dll_sha256=$PROBE_SHA"
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_FLIGHTFALLBACK_EXACT_PROBE"
  echo "human_action=Launch KSP and load any flight on Kerbin or another Exact-CPU body. Wait at least 10 seconds in flight, then exit KSP normally and run the same command again. The probe never persists its synthetic tile."
  exit 0
fi

if [[ "$(state_value head)" != "$HEAD_SHA" ]]; then
  echo "STOP: branch HEAD changed after probe was armed" >&2
  exit 21
fi
if [[ "$(state_value probe_dll_sha)" != "$(sha256sum "$TARGET" | awk '{print $1}')" ]]; then
  echo "STOP: installed DLL changed after probe was armed" >&2
  exit 22
fi
if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  exit 0
fi

SEG="$(mktemp /tmp/AERIS49_FLIGHTFALLBACK_PROBE.XXXXXX)"
segment_from_offset "$(state_value log_offset)" "$SEG"

probe_pass="$(
  grep -F '[AERIS49][FLIGHTFALLBACK_EXACT_PROBE]' "$SEG" |
    grep -F '; pass=true;' |
    grep -F '; work_owner=FlightFallback;' |
    grep -F '; runtime_exact_policy_expected=true;' |
    grep -F '; runtime_exact_cpu_produced=true;' |
    grep -F -c '; persisted=false;' || true
)"
probe_fail="$(
  grep -F '[AERIS49][FLIGHTFALLBACK_EXACT_PROBE]' "$SEG" |
    grep -F -c '; pass=false;' || true
)"
integrated_exact="$(
  grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$SEG" |
    grep -F '; work_owner=FlightFallback;' |
    grep -F '; production_mode=true;' |
    grep -F '; production_authority=EXACT_CPU;' |
    grep -F -c '; worker_runtime_object_access=false' || true
)"
exact_fallback="$(grep -Fc '[AERIS47][R043_EXACT_CPU_FALLBACK]' "$SEG" || true)"
suppressed="$(grep -Fc '[AERIS49][ENV4_DB_WRITE_SUPPRESSED]' "$SEG" || true)"

echo "=== AERIS49 FLIGHTFALLBACK PROBE RESULT ==="
echo "probe_pass_events=$probe_pass"
echo "probe_fail_events=$probe_fail"
echo "integrated_flightfallback_exact_tiles=$integrated_exact"
echo "exact_fallback_events=$exact_fallback"
echo "db_write_suppressed_events=$suppressed"
echo "probe_persisted=false"

rm -f "$SEG"

fail=0
(( probe_pass > 0 )) || fail=$((fail+1))
(( probe_fail == 0 )) || fail=$((fail+1))
(( integrated_exact > 0 )) || fail=$((fail+1))
(( exact_fallback == 0 )) || fail=$((fail+1))
(( suppressed == 0 )) || fail=$((fail+1))

if (( fail != 0 )); then
  echo "AERIS49_FLIGHTFALLBACK_EXACT_PROBE_VERDICT=FAIL"
  echo "AERIS_CURRENT_STAGE=TERRAIN_ND_FLIGHTFALLBACK_PROBE_FAIL"
  echo "failed_checks=$fail"
  exit 40
fi

# Proof passed. Replace the proof DLL with a no-probe production candidate.
cat > "$FINAL_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS49 UNIFIED PRODUCER FINAL"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS49 — TERRAIN_ND UNIFIED PRODUCER FINAL"; internal const string CandidateName = "$FINAL_CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

prepare_project "$PROJECT" "$FINAL_PROJECT"   "AERISBuildVersion.R049UnifiedProducerFinal.generated.cs" false
build_candidate "$FINAL_PROJECT"
final_output="$(install_candidate final)"
echo "$final_output"
FINAL_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"

cat > "$DONE" <<EOFDONE
AERIS49_FLIGHTFALLBACK_EXACT_PROBE_VERDICT=PASS
AERIS49_UNIFIED_PRODUCER_FINAL_DLL_SHA256=$FINAL_SHA
probe_observer_compiled_in_final=false
producer_policy=EXACT_CPU_ALL_PERSISTENT_OWNERS_V2
AERIS_CURRENT_STAGE=TERRAIN_ND_UNIFIED_PRODUCER_PROVEN
next_action=Re-run TERRAIN_ND runtime certification on the final no-probe DLL, then freeze accepted state.
EOFDONE

cat "$DONE"
