#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris39_r041_allbody_height_modifier_chain_shadow.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
CANDIDATE="AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V1"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
EXPECTED_CORE_SHA="36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72"
EXPECTED_CHECKS=16950
EXPECTED_BODY_CHECKS=2825

SRC="$ROOT/Source/AERISFlightControl"
CSPROJ="$SRC/AERISFlightControl.csproj"
TMP_CSPROJ="$SRC/.AERIS39_R041_HEIGHT_CHAIN.csproj"
OBSERVER_SRC="$SRC/Terrain/AERIS39AllBodyHeightModifierChainShadowObserver.cs"
TMP_OBSERVER="$SRC/Terrain/.AERIS39AllBodyHeightModifierChainShadowObserver.LandControl.cs"
BUILD_DLL="$SRC/bin/Release/AERISFlightControl.dll"
ASSEMBLY="$KSP/KSP_x64_Data/Managed/Assembly-CSharp.dll"
CORE="$KSP/KSP_x64_Data/Managed/UnityEngine.CoreModule.dll"
LOG="$KSP/GameData/AERISFlightControl/Logs/AERISFlightControl.log"
GAME_DATA_ROOT="$KSP/GameData/AERISFlightControl"
ARTIFACT_ROOT="${AERIS_ARTIFACT_ROOT:-$HOME/.cache/AERIS/artifacts}"
OUT="$ARTIFACT_ROOT/AERIS39_R041_AllBody_Height_Modifier_Chain_Shadow"
ARCHIVE="$ARTIFACT_ROOT/AERIS39_R041_AllBody_Height_Modifier_Chain_Shadow.tar.gz"

KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r041-allbody-height-chain/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup() { rm -f "$TMP_CSPROJ" "$TMP_OBSERVER"; }
trap cleanup EXIT

cd "$ROOT"

for cmd in git sha256sum python3 xbuild find stat grep tail tar install cp pgrep cmp; do
  command -v "$cmd" >/dev/null 2>&1 || { echo "STOP: required command missing: $cmd" >&2; exit 3; }
done

[[ -f "$CSPROJ" ]] || { echo "STOP: missing project" >&2; exit 4; }
[[ -f "$OBSERVER_SRC" ]] || { echo "STOP: missing observer source" >&2; exit 4; }
[[ -f "$SRC/Terrain/AERIS39LandControlPureCpuExact.cs" ]] || { echo "STOP: missing LandControl pure source" >&2; exit 4; }
[[ -f "$ASSEMBLY" ]] || { echo "STOP: missing Assembly-CSharp.dll" >&2; exit 4; }
[[ -f "$CORE" ]] || { echo "STOP: missing UnityEngine.CoreModule.dll" >&2; exit 4; }
[[ -d "$GAME_DATA_ROOT" ]] || { echo "STOP: missing AERIS GameData" >&2; exit 4; }

test "$(git branch --show-current)" = "$BRANCH" || { echo "STOP: wrong branch" >&2; exit 5; }
test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before R041 all-body height chain shadow" >&2
  git status -sb
  exit 6
}

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
CORE_SHA="$(sha256sum "$CORE" | awk '{print $1}')"
[[ "$ASSEMBLY_SHA" = "$EXPECTED_ASSEMBLY_SHA" ]] || { echo "STOP: Assembly-CSharp identity mismatch" >&2; echo "actual=$ASSEMBLY_SHA" >&2; exit 7; }
[[ "$CORE_SHA" = "$EXPECTED_CORE_SHA" ]] || { echo "STOP: UnityEngine.CoreModule identity mismatch" >&2; echo "actual=$CORE_SHA" >&2; exit 8; }

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
  grep '\[AERIS39\]\[HEIGHT_CHAIN_' "$segment" > "$OUT/height_chain_runtime_excerpt.txt" || true
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
mapso_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoPureCpuExact.cs" | awk '{print $1}')
mapso_resolver_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoRuntimeSemanticsResolver.cs" | awk '{print $1}')
heightmap_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39HeightMapPureCpuExact.cs" | awk '{print $1}')
legacy_height_ops_source_sha256=$(sha256sum "$SRC/Terrain/AERISR041MohoDresPureCpuExact.cs" | awk '{print $1}')
chain_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39AllBodyHeightModifierChainPureCpuExact.cs" | awk '{print $1}')
mapdecal_tangent_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapDecalTangentPureCpuExact.cs" | awk '{print $1}')
mapdecal_classic_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapDecalPureCpuExact.cs" | awk '{print $1}')
flattenarea_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39FlattenAreaPureCpuExact.cs" | awk '{print $1}')
landcontrol_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39LandControlPureCpuExact.cs" | awk '{print $1}')
observer_source_sha256=$(sha256sum "$OBSERVER_SRC" | awk '{print $1}')
runner_source_sha256=$(sha256sum "$ROOT/Tools/aeris39_r041_allbody_height_modifier_chain_shadow.sh" | awk '{print $1}')
observer_build_semantics=CANONICAL_PLUS_DETERMINISTIC_LANDCONTROL_CASE_INJECTION
reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN
callback_invocation_thread=MAIN_THREAD_ONLY
callback_data=ISOLATED_NEW_VERTEXBUILDDATA
live_pqs_state_mutation=false
production_authority=PQS
db_authority=PQS
producer_switch=false
db_write=false
preload_mutation=false
production_worker_runtime_object_access=false
EOF
  (cd "$OUT" && sha256sum height_chain_runtime_excerpt.txt provenance.txt > SHA256SUMS.txt)
  rm -f "$ARCHIVE"
  tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_R041_AllBody_Height_Modifier_Chain_Shadow
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
  segment="$(mktemp /tmp/AERIS39_R041_HEIGHT_CHAIN_segment.XXXXXX)"
  if (( current_size >= state_offset )); then
    tail -c +"$start_byte" "$LOG" > "$segment"
  else
    cp "$LOG" "$segment"
  fi

  if grep -Fq "[AERIS39][HEIGHT_CHAIN_FAIL]" "$segment"; then
    write_artifacts "$segment" "FAIL" "$installed_sha"
    echo "=== R041 ALL-BODY HEIGHT CHAIN RUNTIME FAILURE ==="
    grep '\[AERIS39\]\[HEIGHT_CHAIN_' "$segment" || true
    echo "archive=$ARCHIVE"
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 40
  fi

  if ! grep -Fq "[AERIS39][HEIGHT_CHAIN_COMPLETE]" "$segment"; then
    local begin_seen=false
    grep -Fq "[AERIS39][HEIGHT_CHAIN_BEGIN]" "$segment" && begin_seen=true
    rm -f "$segment"
    echo "observer_begin_seen=$begin_seen"
    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
    return 0
  fi

  local complete pass=1
  complete="$(grep -F "[AERIS39][HEIGHT_CHAIN_COMPLETE]" "$segment" | tail -n 1)"
  [[ "$complete" == *"; pass=true;"* ]] || pass=0
  [[ "$complete" == *"; candidate=$CANDIDATE;"* ]] || pass=0
  [[ "$complete" == *"; bodies=6;"* ]] || pass=0
  [[ "$complete" == *"; total_checks=$EXPECTED_CHECKS;"* ]] || pass=0
  [[ "$complete" == *"; value_matches=$EXPECTED_CHECKS;"* ]] || pass=0
  [[ "$complete" == *"; exception_match_count=0;"* ]] || pass=0
  [[ "$complete" == *"; exception_mismatch_count=0;"* ]] || pass=0
  [[ "$complete" == *"; mismatch_count=0;"* ]] || pass=0
  [[ "$complete" == *"; bit_exact=true;"* ]] || pass=0
  [[ "$complete" == *"; worker_not_main=true;"* ]] || pass=0
  [[ "$complete" == *"; reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN;"* ]] || pass=0
  [[ "$complete" == *"; callback_invocation_thread=MAIN_THREAD_ONLY;"* ]] || pass=0
  [[ "$complete" == *"; callback_data=ISOLATED_NEW_VERTEXBUILDDATA;"* ]] || pass=0
  [[ "$complete" == *"; live_pqs_state_mutation=false;"* ]] || pass=0
  [[ "$complete" == *"; snapshot_payload=PRIMITIVES_ONLY;"* ]] || pass=0
  [[ "$complete" == *"; production_authority=PQS;"* ]] || pass=0
  [[ "$complete" == *"; db_authority=PQS;"* ]] || pass=0
  [[ "$complete" == *"; producer_switch=false;"* ]] || pass=0
  [[ "$complete" == *"; db_write=false;"* ]] || pass=0
  [[ "$complete" == *"; preload_mutation=false;"* ]] || pass=0
  [[ "$complete" == *"; production_worker_runtime_object_access=false"* ]] || pass=0

  for body in Kerbin Eve Duna Dres Moho Eeloo; do
    local line
    line="$(grep -F "[AERIS39][HEIGHT_CHAIN_BODY]" "$segment" | grep -F "; body=$body;" | tail -n 1 || true)"
    [[ -n "$line" ]] || { pass=0; continue; }
    [[ "$line" == *"; pass=true;"* ]] || pass=0
    [[ "$line" == *"; checks=$EXPECTED_BODY_CHECKS;"* ]] || pass=0
    [[ "$line" == *"; value_matches=$EXPECTED_BODY_CHECKS;"* ]] || pass=0
    [[ "$line" == *"; mismatch_count=0;"* ]] || pass=0
    [[ "$line" == *"; bit_exact=true;"* ]] || pass=0
  done

  write_artifacts "$segment" "$([[ "$pass" -eq 1 ]] && echo PASS || echo FAIL_ACCEPTANCE)" "$installed_sha"

  if [[ "$pass" -ne 1 ]]; then
    echo "=== R041 ALL-BODY HEIGHT CHAIN ACCEPTANCE FAILURE ==="
    grep '\[AERIS39\]\[HEIGHT_CHAIN_' "$segment" || true
    echo "archive=$ARCHIVE"
    rm -f "$segment"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 41
  fi

  echo "=== R041 ALL-BODY HEIGHT MODIFIER CHAIN ACCEPTANCE ==="
  grep '\[AERIS39\]\[HEIGHT_CHAIN_DEPENDENCY\]' "$segment" || true
  grep '\[AERIS39\]\[HEIGHT_CHAIN_SNAPSHOT\]' "$segment" || true
  grep '\[AERIS39\]\[HEIGHT_CHAIN_BODY\]' "$segment" || true
  echo "$complete"
  echo
  cat "$OUT/SHA256SUMS.txt"
  echo "archive=$ARCHIVE"
  rm -f "$segment"
  rm -rf "$STATE_DIR"
  echo "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS"
  return 0
}

if [[ -f "$STATE" ]]; then
  if harvest_if_ready; then exit 0; fi
  echo "INFO: stale R041 all-body height chain state replaced for current HEAD"
  rm -rf "$STATE_DIR"
fi

if pgrep -f "$KSP/KSP.x86_64" >/dev/null 2>&1; then
  echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP_EXIT"
  echo "human_action=Exit KSP, then run the same command again."
  exit 0
fi

echo "=== R041 ALL-BODY HEIGHT CHAIN BUILD ==="
echo "HEAD=$HEAD"
echo "KSP=$KSP"
echo "artifact_root=$ARTIFACT_ROOT"

python3 - "$OBSERVER_SRC" "$TMP_OBSERVER" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
needle = '''                    default:\n                        throw new InvalidOperationException(\n                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);'''
case = r'''                    case "PQSLandControl":
                    {
                        bool useHeightMap = (bool)RequireMember(record.Mod, "useHeightMap");
                        AERIS39MapSoPureCpuExact.MapSnapshot landHeightMap = null;
                        string mapSemantics = "NONE";
                        string mapEvidence = "USE_HEIGHT_MAP_FALSE";

                        if (useHeightMap)
                        {
                            MapSO map = RequireMember(record.Mod, "heightMap") as MapSO;
                            if (map == null)
                                throw new InvalidOperationException(bodyName + "_LANDCONTROL_HEIGHTMAP_MISSING");
                            byte[] data = RequireMember(map, "_data") as byte[];
                            if (data == null)
                                throw new InvalidOperationException(bodyName + "_LANDCONTROL_MAP_DATA_NOT_BYTE_ARRAY");
                            int width = ReadInt(map, "_width");
                            int mapHeight = ReadInt(map, "_height");
                            int bpp = ReadInt(map, "_bpp");
                            int rowWidth = ReadInt(map, "_rowWidth");
                            if (width <= 0 || mapHeight <= 0 || bpp <= 0 || rowWidth <= 0)
                                throw new InvalidOperationException(bodyName + "_LANDCONTROL_MAP_DIMENSIONS_INVALID");
                            AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                                AERIS39MapSoRuntimeSemanticsResolver.Resolve(map, out mapEvidence);
                            mapSemantics = semantics.ToString();
                            landHeightMap = new AERIS39MapSoPureCpuExact.MapSnapshot(
                                data, width, mapHeight, bpp, rowWidth, semantics);
                        }

                        Array classes = RequireMember(record.Mod, "landClasses") as Array;
                        if (classes == null)
                            throw new InvalidOperationException(bodyName + "_LANDCONTROL_CLASSES_MISSING");

                        Func<object, AERIS39LandControlPureCpuExact.LerpRangeSnapshot> rangeSnapshot =
                            delegate(object range)
                            {
                                if (range == null)
                                    throw new InvalidOperationException(bodyName + "_LANDCONTROL_RANGE_MISSING");
                                return new AERIS39LandControlPureCpuExact.LerpRangeSnapshot(
                                    ReadDouble(range, "startStart"),
                                    ReadDouble(range, "startEnd"),
                                    ReadDouble(range, "endStart"),
                                    ReadDouble(range, "endEnd"),
                                    ReadDouble(range, "startDelta"),
                                    ReadDouble(range, "endDelta"));
                            };

                        var pureClasses =
                            new AERIS39LandControlPureCpuExact.LandClassSnapshot[classes.Length];
                        for (int ci = 0; ci < classes.Length; ci++)
                        {
                            object lc = classes.GetValue(ci);
                            if (lc == null)
                                throw new InvalidOperationException(
                                    bodyName + "_LANDCONTROL_NULL_CLASS_" +
                                    ci.ToString(CultureInfo.InvariantCulture));

                            pureClasses[ci] = new AERIS39LandControlPureCpuExact.LandClassSnapshot(
                                rangeSnapshot(RequireMember(lc, "altitudeRange")),
                                rangeSnapshot(RequireMember(lc, "latitudeRange")),
                                (bool)RequireMember(lc, "latitudeDouble"),
                                rangeSnapshot(RequireMember(lc, "latitudeDoubleRange")),
                                rangeSnapshot(RequireMember(lc, "longitudeRange")),
                                Convert.ToSingle(
                                    RequireMember(lc, "coverageBlend"),
                                    CultureInfo.InvariantCulture),
                                SnapshotSimplex(RequireMember(lc, "coverageSimplex")),
                                ReadDouble(lc, "minimumRealHeight"),
                                ReadDouble(lc, "alterRealHeight"));
                        }

                        pureOps[i] = new AERIS39LandControlPureCpuExact.OpSnapshot(
                            useHeightMap,
                            landHeightMap,
                            Convert.ToSingle(
                                RequireMember(record.Mod, "vHeightMax"),
                                CultureInfo.InvariantCulture),
                            ReadDouble(pqs, "radius"),
                            ReadDouble(pqs, "sx"),
                            ReadDouble(pqs, "sy"),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "altitudeBlend"),
                                CultureInfo.InvariantCulture),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "latitudeBlend"),
                                CultureInfo.InvariantCulture),
                            Convert.ToSingle(
                                RequireMember(record.Mod, "longitudeBlend"),
                                CultureInfo.InvariantCulture),
                            SnapshotSimplex(RequireMember(record.Mod, "altitudeSimplex")),
                            SnapshotSimplex(RequireMember(record.Mod, "latitudeSimplex")),
                            SnapshotSimplex(RequireMember(record.Mod, "longitudeSimplex")),
                            pureClasses);

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSLandControl" +
                            "; dependency=LANDCLASS_RANGE_COVERAGE_SIMPLEX_HEIGHT" +
                            "; use_height_map=" + Bool(useHeightMap) +
                            "; map_semantics=" + Safe(mapSemantics) +
                            "; semantics_evidence=" + Safe(mapEvidence) +
                            "; land_classes=" + classes.Length.ToString(CultureInfo.InvariantCulture) +
                            "; sphere_sx_sy=RUNTIME_SNAPSHOTTED" +
                            "; range_setup_state=SNAPSHOTTED" +
                            "; source_semantics=STOCK_ONVERTEXBUILDHEIGHT" +
                            "; color_scatter_apparent_height=OUT_OF_HEIGHT_PATH" +
                            "; exact_candidate=true" + Invariants());
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);'''
if src.count(needle) != 1:
    raise SystemExit("R041 LandControl observer insertion point not unique")
pathlib.Path(sys.argv[2]).write_text(src.replace(needle, case, 1), encoding="utf-8")
PY

python3 - "$CSPROJ" "$TMP_CSPROJ" <<'PY'
import pathlib, sys
src = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")
needle = "</ItemGroup><Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" /></Project>"
insert = (
    "    <Compile Include=\"Terrain\\AERIS39MapSoPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39MapSoRuntimeSemanticsResolver.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39HeightMapPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39AllBodyHeightModifierChainPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39MapDecalTangentPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39MapDecalPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39FlattenAreaPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39LandControlPureCpuExact.cs\" />\n"
    "    <Compile Include=\"Terrain\\.AERIS39AllBodyHeightModifierChainShadowObserver.LandControl.cs\" />\n"
    "</ItemGroup><Import Project=\"$(MSBuildToolsPath)\\Microsoft.CSharp.targets\" /></Project>"
)
if needle not in src:
    raise SystemExit("R041 all-body height chain project insertion point not found")
pathlib.Path(sys.argv[2]).write_text(src.replace(needle, insert, 1), encoding="utf-8")
PY

rm -rf "$SRC/bin/Release" "$SRC/obj/Release"
(cd "$SRC" && xbuild /p:Configuration=Release /p:KSPDIR="$KSP" .AERIS39_R041_HEIGHT_CHAIN.csproj)
[[ -f "$BUILD_DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 20; }
rm -f "$TMP_CSPROJ" "$TMP_OBSERVER"
if [[ -n "$(git status --porcelain)" ]]; then
  echo "STOP: build dirtied repository state" >&2
  git status -sb
  exit 21
fi

mapfile -t TARGETS < <(find "$GAME_DATA_ROOT" -type f -name 'AERISFlightControl.dll' -print)
[[ "${#TARGETS[@]}" -eq 1 ]] || { echo "STOP: expected exactly one installed AERISFlightControl.dll; found=${#TARGETS[@]}" >&2; exit 22; }
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
echo "=== R041 ALL-BODY HEIGHT CHAIN INSTALLED ==="
echo "dll_sha256=$INSTALLED_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$LOG_OFFSET"
echo "expected_checks=$EXPECTED_CHECKS"
echo "reference=REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN"
echo "callback_invocation_thread=MAIN_THREAD_ONLY"
echo "callback_data=ISOLATED_NEW_VERTEXBUILDDATA"
echo "live_pqs_state_mutation=false"
echo "production_authority=PQS"
echo "db_authority=PQS"
echo "producer_switch=false"
echo "db_write=false"
echo "preload_mutation=false"
echo "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_BUILD_INSTALL=PASS"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."
