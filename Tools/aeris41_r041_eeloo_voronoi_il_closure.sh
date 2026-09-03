#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris41_r041_eeloo_voronoi_il_closure.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
CANDIDATE="AERIS41_R041_EELOO_VORONOI_IL_CLOSURE_V1"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"

SRC="$ROOT/Source/AERISFlightControl"
CSPROJ="$SRC/AERISFlightControl.csproj"
TMP_CSPROJ="$SRC/.AERIS41_R041_EELOO_VORONOI_IL.csproj"
OBSERVER="$SRC/Terrain/AERIS41EelooVoronoiIlClosureObserver.cs"
BUILD_DLL="$SRC/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
LOG="$KSP/GameData/AERISFlightControl/Logs/AERISFlightControl.log"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
ARTIFACT_ROOT="${AERIS_ARTIFACT_ROOT:-$HOME/.cache/AERIS/artifacts}"
OUT="$ARTIFACT_ROOT/AERIS41_R041_Eeloo_Voronoi_IL_Closure"
ARCHIVE="$ARTIFACT_ROOT/AERIS41_R041_Eeloo_Voronoi_IL_Closure.tar.gz"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r041-eeloo-voronoi-il/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() { rm -f "$TMP_CSPROJ"; }
trap cleanup EXIT

cd "$ROOT"

for cmd in git sha256sum python3 xbuild find stat grep tail tar install cp pgrep cmp; do
  command -v "$cmd" >/dev/null 2>&1 || {
    echo "STOP: required command missing: $cmd" >&2
    exit 3
  }
done

[[ -f "$CSPROJ" ]] || { echo "STOP: project missing" >&2; exit 4; }
[[ -f "$OBSERVER" ]] || { echo "STOP: Voronoi closure observer missing" >&2; exit 4; }
[[ -f "$ASSEMBLY" ]] || { echo "STOP: Assembly-CSharp.dll missing" >&2; exit 4; }
[[ -f "$CORE" ]] || { echo "STOP: UnityEngine.CoreModule.dll missing" >&2; exit 4; }
[[ -d "$GAME_DATA_ROOT" ]] || { echo "STOP: AERIS GameData missing" >&2; exit 4; }

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 5
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before Eeloo Voronoi IL closure" >&2
  git status -sb >&2
  exit 6
}

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
[[ "$ASSEMBLY_SHA" = "$EXPECTED_ASSEMBLY_SHA" ]] || {
  echo "STOP: Assembly-CSharp identity mismatch" >&2
  echo "actual=$ASSEMBLY_SHA" >&2
  exit 7
}
[[ "$CORE_SHA" = "$EXPECTED_CORE_SHA" ]] || {
  echo "STOP: UnityEngine.CoreModule identity mismatch" >&2
  echo "actual=$CORE_SHA" >&2
  exit 8
}

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
  grep -F '[AERIS41][VORONOI_IL]' "$segment" > "$OUT/voronoi_il_runtime_excerpt.txt" || true
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
observer_source_sha256=$(sha256sum "$OBSERVER" | awk '{print $1}')
runner_source_sha256=$(sha256sum "$ROOT/Tools/aeris41_r041_eeloo_voronoi_il_closure.sh" | awk '{print $1}')
closure=TARGET_DECLARED_IL_PLUS_RUNTIME_LIBNOISE_FIELD_GRAPH_PLUS_GRADIENT_NOISE_BASIS
invokes_pqs_callbacks=false
invokes_dependency_methods=false
arbitrary_property_getters=false
production_authority=PQS
db_authority=PQS
producer_switch=false
db_write=false
preload_mutation=false
production_worker_runtime_object_access=false
runtime_state_mutation=false
EOF
  (cd "$OUT" && sha256sum voronoi_il_runtime_excerpt.txt provenance.txt > SHA256SUMS.txt)
  rm -f "$ARCHIVE"
  tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS41_R041_Eeloo_Voronoi_IL_Closure
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
  segment="$(mktemp /tmp/AERIS41_R041_VORONOI_IL_segment.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  if grep -Fq '[AERIS41][VORONOI_IL][FAIL]' "$segment"; then
    write_artifacts "$segment" "FAIL" "$installed_sha"
    echo "=== R041 EELOO VORONOI IL CLOSURE RUNTIME FAILURE ==="
    grep -F '[AERIS41][VORONOI_IL]' "$segment" || true
    echo "archive=$ARCHIVE"
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 40
  fi

  if ! grep -Fq '[AERIS41][VORONOI_IL][COMPLETE]' "$segment"; then
    local begin_seen=false
    grep -Fq '[AERIS41][VORONOI_IL][BEGIN]' "$segment" && begin_seen=true
    rm -f "$segment"
    echo "observer_begin_seen=$begin_seen"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
    return 0
  fi

  local complete pass=1
  complete="$(grep -F '[AERIS41][VORONOI_IL][COMPLETE]' "$segment" | tail -n 1)"

  [[ "$complete" == *"; pass=true;"* ]] || pass=0
  [[ "$complete" == *"; candidate=$CANDIDATE;"* ]] || pass=0
  [[ "$complete" == *"; body=Eeloo;"* ]] || pass=0
  [[ "$complete" == *"; target_onsetup=true;"* ]] || pass=0
  [[ "$complete" == *"; target_onvertexbuildheight=true;"* ]] || pass=0
  [[ "$complete" == *"; libnoise_voronoi_type=true;"* ]] || pass=0
  [[ "$complete" == *"; libnoise_voronoi_getvalue=true;"* ]] || pass=0
  [[ "$complete" == *"; gradient_noise_basis_type=true;"* ]] || pass=0
  [[ "$complete" == *"; gradient_value_noise_3d=true;"* ]] || pass=0
  [[ "$complete" == *"; invokes_pqs_callbacks=false;"* ]] || pass=0
  [[ "$complete" == *"; invokes_dependency_methods=false;"* ]] || pass=0
  [[ "$complete" == *"; arbitrary_property_getters=false;"* ]] || pass=0
  [[ "$complete" == *"; production_authority=PQS;"* ]] || pass=0
  [[ "$complete" == *"; db_authority=PQS;"* ]] || pass=0
  [[ "$complete" == *"; producer_switch=false;"* ]] || pass=0
  [[ "$complete" == *"; db_write=false;"* ]] || pass=0
  [[ "$complete" == *"; preload_mutation=false;"* ]] || pass=0
  [[ "$complete" == *"; production_worker_runtime_object_access=false;"* ]] || pass=0
  [[ "$complete" == *"; pqs_callback_invocation=false;"* ]] || pass=0
  [[ "$complete" == *"; runtime_state_mutation=false"* ]] || pass=0

  local target_line voronoi_type voronoi_getvalue gradient_type gradient_value
  target_line="$(grep -F '[AERIS41][VORONOI_IL][TARGET]' "$segment" | grep -F '; type=PQSMod_VertexVoronoi;' | head -n 1 || true)"
  voronoi_type="$(grep -F '[AERIS41][VORONOI_IL][DEPENDENCY_TYPE]' "$segment" | grep -F '; type=LibNoise.Voronoi;' | head -n 1 || true)"
  voronoi_getvalue="$(grep -F '[AERIS41][VORONOI_IL][METHOD]' "$segment" | grep -F '; role=DEPENDENCY;' | grep -F '; type=LibNoise.Voronoi;' | grep -F '; method=GetValue(' | head -n 1 || true)"
  gradient_type="$(grep -F '[AERIS41][VORONOI_IL][DEPENDENCY_TYPE]' "$segment" | grep -F '; type=LibNoise.GradientNoiseBasis;' | head -n 1 || true)"
  gradient_value="$(grep -F '[AERIS41][VORONOI_IL][METHOD]' "$segment" | grep -F '; role=DEPENDENCY;' | grep -F '; type=LibNoise.GradientNoiseBasis;' | grep -F '; method=ValueNoise3D(' | head -n 1 || true)"

  [[ -n "$target_line" ]] || pass=0
  [[ -n "$voronoi_type" ]] || pass=0
  [[ -n "$voronoi_getvalue" ]] || pass=0
  [[ -n "$gradient_type" ]] || pass=0
  [[ -n "$gradient_value" ]] || pass=0

  if [[ "$pass" -ne 1 ]]; then
    write_artifacts "$segment" "FAIL_ACCEPTANCE" "$installed_sha"
    echo "=== R041 EELOO VORONOI IL CLOSURE ACCEPTANCE FAILURE ==="
    grep -F '[AERIS41][VORONOI_IL]' "$segment" || true
    echo "archive=$ARCHIVE"
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 41
  fi

  write_artifacts "$segment" "PASS" "$installed_sha"
  echo "=== R041 EELOO VORONOI IL CLOSURE ACCEPTANCE ==="
  grep -F '[AERIS41][VORONOI_IL][TARGET]' "$segment" || true
  grep -F '[AERIS41][VORONOI_IL][FIELD]' "$segment" || true
  grep -F '[AERIS41][VORONOI_IL][DEPENDENCY_INSTANCE]' "$segment" || true
  grep -F '[AERIS41][VORONOI_IL][DEPENDENCY_TYPE]' "$segment" || true
  grep -F '[AERIS41][VORONOI_IL][METHOD]' "$segment" || true
  grep -F '[AERIS41][VORONOI_IL][LOCAL]' "$segment" || true
  grep -F '[AERIS41][VORONOI_IL][INSN]' "$segment" || true
  echo "$complete"
  echo
  cat "$OUT/SHA256SUMS.txt"
  echo "archive=$ARCHIVE"
  rm -f "$segment"
  rm -rf "$STATE_DIR"
  echo "AERIS41_R041_EELOO_VORONOI_IL_CLOSURE=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_EELOO_VORONOI_PURE_CPU_EXACT_IMPLEMENTATION"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale Eeloo Voronoi IL closure state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo "=== R041 EELOO VORONOI IL CLOSURE BUILD ==="
echo "HEAD=$HEAD"
echo "KSP=$KSP"
echo "artifact_root=$ARTIFACT_ROOT"
echo "assembly_sha256=$ASSEMBLY_SHA"
echo "unity_core_sha256=$CORE_SHA"

python3 - "$CSPROJ" "$TMP_CSPROJ" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
needle = '</ItemGroup><Import Project="$(MSBuildToolsPath)\\Microsoft.CSharp.targets" /></Project>'
include = '    <Compile Include="Terrain\\AERIS41EelooVoronoiIlClosureObserver.cs" />\n'
if src.count(needle) != 1:
    raise SystemExit("AERIS41 Voronoi project insertion point not unique")
if 'AERIS41EelooVoronoiIlClosureObserver.cs' in src:
    raise SystemExit("AERIS41 Voronoi observer unexpectedly already in canonical project")
out = src.replace(needle, include + needle, 1)
pathlib.Path(sys.argv[2]).write_text(out, encoding="utf-8")
PY

rm -rf "$SRC/bin/Release" "$SRC/obj/Release"
(cd "$SRC" && xbuild /p:Configuration=Release /p:KSPDIR="$KSP" .AERIS41_R041_EELOO_VORONOI_IL.csproj)
[[ -f "$BUILD_DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 20; }
rm -f "$TMP_CSPROJ"

if [[ -n "$(git status --porcelain)" ]]; then
  echo "STOP: build dirtied repository state" >&2
  git status -sb >&2
  exit 21
fi

mapfile -t TARGETS < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
[[ "${#TARGETS[@]}" -eq 1 ]] || {
  echo "STOP: expected exactly one installed AERISFlightControl.dll; found=${#TARGETS[@]}" >&2
  exit 22
}
TARGET="${TARGETS[0]}"
OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
STAMP="$(date +%Y%m%d-%H%M%S)"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
BACKUP="$BACKUP_DIR/${STAMP}-${OLD_SHA}.AERISFlightControl.dll"
mkdir -p "$BACKUP_DIR"
cp -a "$TARGET" "$BACKUP"
install -m 0644 "$BUILD_DLL" "$TARGET"
cmp -s "$BUILD_DLL" "$TARGET" || {
  echo "STOP: installed DLL differs from build" >&2
  exit 23
}
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
echo "=== R041 EELOO VORONOI IL CLOSURE INSTALLED ==="
echo "candidate=$CANDIDATE"
echo "dll_sha256=$INSTALLED_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "closure=TARGET_DECLARED_IL_PLUS_RUNTIME_LIBNOISE_FIELD_GRAPH_PLUS_GRADIENT_NOISE_BASIS"
echo "invokes_pqs_callbacks=false"
echo "invokes_dependency_methods=false"
echo "arbitrary_property_getters=false"
echo "runtime_state_mutation=false"
echo "production_authority=PQS"
echo "db_authority=PQS"
echo "producer_switch=false"
echo "db_write=false"
echo "preload_mutation=false"
echo "AERIS41_R041_EELOO_VORONOI_IL_CLOSURE_BUILD_INSTALL=PASS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
