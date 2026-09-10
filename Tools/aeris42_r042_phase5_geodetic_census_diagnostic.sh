#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase5_geodetic_census_diagnostic.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
OBSERVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowWorkerParityObserver.cs"
DIAGNOSTIC="$PROJECT_DIR/Terrain/AERISR042Phase5GeodeticCensusDiagnostic.cs"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
LOG="$GAME_DATA_ROOT/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r042-phase5-geodetic-census/$KEY"
STATE="$STATE_DIR/state.txt"

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE5 GEODETIC CENSUS DIAGNOSTIC ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"

if [[ "$(git branch --show-current)" != "$EXPECTED_BRANCH" ]]; then
  echo "STOP: wrong branch" >&2
  exit 10
fi
if [[ ! -f "$PROJECT" || ! -f "$OBSERVER" || ! -f "$DIAGNOSTIC" || ! -f "$RESOLVER" ]]; then
  echo "STOP: Phase5 diagnostic source set incomplete" >&2
  exit 11
fi
if [[ ! -f "$ASSEMBLY" || ! -f "$CORE" ]]; then
  echo "STOP: KSP managed assemblies missing" >&2
  exit 12
fi
if ! command -v xbuild >/dev/null 2>&1; then
  echo "STOP: xbuild not found" >&2
  exit 13
fi

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
if [[ "$ASSEMBLY_SHA" != "$EXPECTED_ASSEMBLY_SHA" ]]; then
  echo "STOP: Assembly-CSharp identity mismatch" >&2
  echo "actual=$ASSEMBLY_SHA" >&2
  exit 14
fi
if [[ "$CORE_SHA" != "$EXPECTED_CORE_SHA" ]]; then
  echo "STOP: UnityEngine.CoreModule identity mismatch" >&2
  echo "actual=$CORE_SHA" >&2
  exit 15
fi

# Static gates: compile the actual Phase5 implementation and the diagnostic,
# while retaining the R042 no-producer-switch boundary.
grep -Fq '<Compile Include="Terrain\AERISR042ExactCpuShadowWorkerParityObserver.cs" />' "$PROJECT" || {
  echo "STOP: Phase5 observer not in production project" >&2
  exit 20
}
grep -Fq '<Compile Include="Terrain\AERISR042Phase5GeodeticCensusDiagnostic.cs" />' "$PROJECT" || {
  echo "STOP: Phase5 diagnostic not in production project" >&2
  exit 21
}
grep -Fq 'BuildSamples", staticPrivate' "$DIAGNOSTIC" || {
  echo "STOP: diagnostic does not invoke Phase5 BuildSamples" >&2
  exit 22
}
grep -Fq 'R042_PHASE5_GEODETIC_REJECT' "$DIAGNOSTIC" || exit 23
grep -Fq 'R042_PHASE5_FP_BOUNDARY_PROBE' "$DIAGNOSTIC" || exit 24
grep -Fq 'BitConverter.DoubleToInt64Bits' "$DIAGNOSTIC" || exit 25
grep -Fq 'internal const bool ProducerSwitchEnabled = false;' "$RESOLVER" || {
  echo "STOP: producer switch invariant changed" >&2
  exit 26
}
if grep -Eq '\.OnVertexBuildHeight\s*\(' "$DIAGNOSTIC"; then
  echo "STOP: diagnostic invokes terrain callback" >&2
  exit 27
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

  if [[ ! -f "$LOG" ]]; then
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait until fully loaded, exit KSP, then run the same command again."
    return 0
  fi

  local current_size start_byte segment
  current_size="$(stat -c %s "$LOG")"
  start_byte=$((state_offset + 1))
  segment="$(mktemp /tmp/AERIS42_R042_PHASE5_GEODETIC.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  local diag_complete
  diag_complete="$(grep -F '[AERIS42][R042_PHASE5_GEODETIC_DIAG_COMPLETE]' "$segment" | tail -n 1 || true)"
  if [[ -z "$diag_complete" ]]; then
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, wait until fully loaded, exit KSP, then run the same command again."
    return 0
  fi

  echo
  echo "=== R042 PHASE5 GEODETIC CENSUS RUNTIME EVIDENCE ==="
  grep -F '[AERIS42][R042_PHASE5_GEODETIC_REJECT]' "$segment" || true
  grep -F '[AERIS42][R042_PHASE5_FP_BOUNDARY_PROBE]' "$segment" || true
  echo "$diag_complete"

  echo
  echo "=== PHASE5 OBSERVER RESULT FROM SAME RUN ==="
  grep -F '[AERIS42][R042_PHASE5_COMPLETE]' "$segment" | tail -n 3 || true

  local instrument_pass=1
  [[ "$diag_complete" == *'; pass=true;'* ]] || instrument_pass=0
  [[ "$diag_complete" == *'; census=565;'* ]] || instrument_pass=0
  [[ "$diag_complete" == *'; sample_generator=PHASE5_BUILDSAMPLES_REFLECTION_EXACT;'* ]] || instrument_pass=0
  [[ "$diag_complete" == *'; production_authority=PQS;'* ]] || instrument_pass=0
  [[ "$diag_complete" == *'; producer_switch=false;'* ]] || instrument_pass=0
  [[ "$diag_complete" == *'; db_write=false;'* ]] || instrument_pass=0
  [[ "$diag_complete" == *'; preload_mutation=false'* ]] || instrument_pass=0

  local reject_count probe_count
  reject_count="$(grep -Fc '[AERIS42][R042_PHASE5_GEODETIC_REJECT]' "$segment" || true)"
  probe_count="$(grep -Fc '[AERIS42][R042_PHASE5_FP_BOUNDARY_PROBE]' "$segment" || true)"
  [[ "$probe_count" -ge 1 ]] || instrument_pass=0

  rm -f "$segment"

  if [[ "$instrument_pass" -ne 1 ]]; then
    echo "R042_PHASE5_GEODETIC_CENSUS_DIAGNOSTIC=FAIL"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 40
  fi

  rm -rf "$STATE_DIR"
  echo
  echo "R041_FINAL_ACCEPTANCE=PRESERVED"
  echo "diagnostic_census=565"
  echo "diagnostic_reject_lines=$reject_count"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_write_switch=false"
  echo "preload_mutation=false"
  echo "R042_PHASE5_GEODETIC_CENSUS_DIAGNOSTIC=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=ANALYZE_R041_560_VS_R042_RUNTIME_CENSUS"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then
    exit 0
  fi
  echo "INFO: stale Phase5 diagnostic state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo
echo "=== XBUILD / PHASE5 DIAGNOSTIC PRODUCTION PROJECT ==="
rm -rf "$PROJECT_DIR/bin/Release" "$PROJECT_DIR/obj/Release"
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" AERISFlightControl.csproj
)
if [[ ! -f "$DLL" ]]; then
  echo "STOP: build returned without DLL" >&2
  exit 50
fi

mapfile -t targets < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
if [[ "${#targets[@]}" -ne 1 ]]; then
  echo "STOP: expected exactly one installed AERISFlightControl.dll; found=${#targets[@]}" >&2
  exit 51
fi
TARGET="${targets[0]}"
OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
NEW_SHA="$(sha256sum "$DLL" | awk '{print $1}')"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
BACKUP="$BACKUP_DIR/${STAMP}-${OLD_SHA}.AERISFlightControl.dll"
mkdir -p "$BACKUP_DIR"
cp -a "$TARGET" "$BACKUP"
install -m 0644 "$DLL" "$TARGET"
if ! cmp -s "$DLL" "$TARGET"; then
  echo "STOP: installed DLL differs from build" >&2
  exit 52
fi

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
echo "=== R042 PHASE5 GEODETIC DIAGNOSTIC INSTALLED ==="
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "diagnostic_target=PHASE5_BUILDSAMPLES_RUNTIME_VALUES"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_write_switch=false"
echo "preload_mutation=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, wait until fully loaded, exit KSP, then run the same command again."
