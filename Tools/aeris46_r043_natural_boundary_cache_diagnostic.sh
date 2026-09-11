#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris46_r043_natural_boundary_cache_diagnostic.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris46-r043-natural-exact-parity-repair"
BASE="ba5283ecb0f1321416574613f741c03c98171878"
CANDIDATE="AERIS46_R043_NATURAL_BOUNDARY_CACHE_DIAGNOSTIC"

PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R046NaturalBoundaryDiagnostic.csproj"
TEMP_VERSION="$PROJECT_DIR/Properties/AERISBuildVersion.R046NaturalBoundaryDiagnostic.generated.cs"
PAIR="$PROJECT_DIR/Terrain/AERISR046NaturalParityPairDiagnostic.cs"
OBSERVER="$PROJECT_DIR/Terrain/AERISR046NaturalParityDiagnosticObserver.cs"
IDENTITY="$PROJECT_DIR/Core/AERISR043RuntimeBuildIdentityObserver.cs"
SHADOW="$PROJECT_DIR/Terrain/AERISR043PreloadPtcIntegratedShadow.cs"
PIPELINE="$PROJECT_DIR/Terrain/AERISTerrainBlockPipeline.cs"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"
GAME_DATA="$KSP/GameData/AERISFlightControl"
TARGET="$GAME_DATA/Plugins/AERISFlightControl.dll"
LOG="$GAME_DATA/Logs/AERISFlightControl.log"
KEY="$(printf '%s' "$KSP" | sha256sum | awk '{print substr($1,1,16)}')"
STATE_DIR="$HOME/.cache/AERIS/r046-natural-boundary-cache/$KEY"
STATE="$STATE_DIR/state.txt"

cleanup(){ rm -f "$TEMP_PROJECT" "$TEMP_VERSION"; }
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS46 / R043 NATURAL BOUNDARY CACHE DIAGNOSTIC ==="
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

for f in "$PROJECT" "$PAIR" "$OBSERVER" "$IDENTITY" "$SHADOW" "$PIPELINE"; do
  [[ -f "$f" ]] || { echo "STOP: missing $f" >&2; exit 12; }
done
command -v xbuild >/dev/null 2>&1 || { echo "STOP: xbuild not found" >&2; exit 13; }
command -v python3 >/dev/null 2>&1 || { echo "STOP: python3 not found" >&2; exit 14; }

for protected in   Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs   Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowSourceResolver.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs   Source/AERISFlightControl/Terrain/AERISR042ExactCpuShadowRuntimeSnapshotBuilder.cs   Source/AERISFlightControl/Terrain/AERIS39AllBodyHeightModifierChainPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS39MapSoPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS39HeightMapPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS39MapDecalPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS39MapDecalTangentPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS39FlattenAreaPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS39LandControlPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs   Source/AERISFlightControl/Terrain/AERIS41VertexVoronoiPureCpuExact.cs; do
  git diff --quiet "$BASE" HEAD -- "$protected" || {
    echo "STOP: protected exact/persistence source changed: $protected" >&2
    exit 15
  }
done

grep -Fq 'SourceLatitude' "$PIPELINE" || exit 16
grep -Fq 'first_boundary_cache_hit=' "$SHADOW" || exit 17
grep -Fq 'boundary_cache_hit_mismatch_count=' "$SHADOW" || exit 18
grep -Fq 'non_boundary_cache_hit_mismatch_count=' "$SHADOW" || exit 19
grep -Fq 'adjacent_shared_boundary=true' "$PAIR" || exit 20
grep -Fq 'db_write=false' "$PAIR" || exit 21
grep -Fq 'AERISR046NaturalParityDiagnosticObserver' "$OBSERVER" || exit 22
grep -Fq 'AERIS_TERRAIN_ENV3_TERRAIN_CFG_PQS'   "$PROJECT_DIR/Terrain/AERISTerrainTileSystem.cs" || exit 23
grep -Fq 'automatic_db_invalidation=false'   "$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs" || exit 24
grep -Fq 'evaluationLatitude = boundaryCacheHit' "$SHADOW" || exit 25
grep -Fq 'PRELOAD WAITING FOR TERRAIN ENVIRONMENT HASH'   "$PROJECT_DIR/Terrain/AERISTerrainPreloadBuilder.cs" || exit 26
grep -Fq 'ENVIRONMENT_HASH_TIMEOUT' "$OBSERVER" || exit 27
grep -Fq 'ENVIRONMENT_HASH_NOT_READY' "$PAIR" || exit 28

HEAD_SHA="$(git rev-parse HEAD)"
TREE_SHA256="$(git archive --format=tar HEAD | sha256sum | awk '{print $1}')"

cat > "$TEMP_VERSION" <<EOFV
using System.Reflection;
[assembly: AssemblyVersion("0.18.0.0")]
[assembly: AssemblyFileVersion("0.18.0.0")]
namespace AERISFlightControl { internal static class AERISBuildVersion { internal const string Semantic = "0.18.0.0"; internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS46 R043 NATURAL BOUNDARY CACHE DIAGNOSTIC"; internal const string UiCheckpoint = "DEV CP3.75 — AERIS46 — R043 NATURAL BOUNDARY CACHE DIAGNOSTIC"; internal const string CandidateName = "$CANDIDATE"; internal const string SourceGitSha = "$HEAD_SHA"; internal const string SourceTreeSha256 = "$TREE_SHA256"; internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }
EOFV

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
import pathlib, sys
src=pathlib.Path(sys.argv[1])
dst=pathlib.Path(sys.argv[2])
text=src.read_text(encoding="utf-8")
marker='    <Compile Include="Terrain\\AERISR043PreloadPtcLiveProof.cs" />\n'
insert=(
    marker +
    '    <Compile Include="Terrain\\AERISR046NaturalParityPairDiagnostic.cs" />\n' +
    '    <Compile Include="Terrain\\AERISR046NaturalParityDiagnosticObserver.cs" />\n' +
    '    <Compile Include="Core\\AERISR043RuntimeBuildIdentityObserver.cs" />\n'
)
version='    <Compile Include="Properties\\AERISBuildVersion.generated.cs" />\n'
version_new='    <Compile Include="Properties\\AERISBuildVersion.R046NaturalBoundaryDiagnostic.generated.cs" />\n'
if text.count(marker) != 1:
    raise SystemExit("diagnostic insertion marker not unique")
if text.count(version) != 1:
    raise SystemExit("version marker not unique")
for name in (
    "AERISR046NaturalParityPairDiagnostic.cs",
    "AERISR046NaturalParityDiagnosticObserver.cs",
    "AERISR043RuntimeBuildIdentityObserver.cs",
):
    if name in text:
        raise SystemExit(name + " unexpectedly present in canonical csproj")
text=text.replace(marker,insert,1).replace(version,version_new,1)
dst.write_text(text,encoding="utf-8")
PY

state_value(){
  local key="$1"
  [[ -f "$STATE" ]] || return 1
  grep -m1 "^$key=" "$STATE" | cut -d= -f2-
}

harvest(){
  [[ -f "$STATE" ]] || return 1
  local sha off head
  sha="$(state_value dll_sha || true)"
  off="$(state_value log_offset || true)"
  head="$(state_value head || true)"
  [[ "$head" = "$(git rev-parse HEAD)" && -n "$sha" && -n "$off" ]] || return 1
  [[ -f "$TARGET" && "$(sha256sum "$TARGET" | awk '{print $1}')" = "$sha" ]] || return 1
  [[ -f "$LOG" ]] || return 1

  local seg size start
  seg="$(mktemp /tmp/AERIS46_R043_NATURAL.XXXXXX)"
  size="$(stat -c %s "$LOG")"
  start=$((off+1))
  if (( size >= off )); then tail -c +"$start" "$LOG" > "$seg"; else cp "$LOG" "$seg"; fi

  local complete
  complete="$(grep -F '[AERIS46][R043_NATURAL_PAIR_COMPLETE]' "$seg" | tail -n1 || true)"
  if [[ -z "$complete" ]]; then
    echo "AERIS_CURRENT_STAGE=DIAGNOSTIC_EVIDENCE_INCOMPLETE"
    echo "human_action=Keep KSP at Main Menu until the AERIS46 pair diagnostic completes, then exit KSP and run the same command again."
    rm -f "$seg"
    return 0
  fi

  echo "=== AERIS46 NATURAL PAIR RAW EVIDENCE ==="
  grep -F '[AERIS44][R043_PRELOAD_ENV_TRANSITION]' "$seg" || true
  grep -F '[AERIS46][R043_PRELOAD_ENV_OLD_DB_PRESERVED]' "$seg" || true
  grep -F '[AERIS46][R043_NATURAL_PAIR_REQUEST]' "$seg" || true
  grep -F '[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]' "$seg" |
    grep -F '; live_preload_proof=true;' || true
  grep -F '[AERIS46][R043_NATURAL_PAIR_COMMIT]' "$seg" || true
  echo "$complete"
  echo

  python3 - "$seg" <<'PY'
import sys
p=sys.argv[1]
lines=open(p,encoding="utf-8",errors="replace").read().splitlines()
tiles=[x for x in lines if "[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]" in x and "; live_preload_proof=true;" in x]
def fields(line):
    out={}
    for part in line.split(";"):
        part=part.strip()
        if "=" in part:
            k,v=part.split("=",1)
            out[k.strip()]=v.strip()
    return out
rows=[fields(x) for x in tiles]
m=[r for r in rows if r.get("pass")=="false"]
cache_mismatch=sum(int(r.get("boundary_cache_hit_mismatch_count","0")) for r in rows)
noncache_mismatch=sum(int(r.get("non_boundary_cache_hit_mismatch_count","0")) for r in rows)
total_mismatch=sum(int(r.get("mismatch_count","0")) for r in rows)
cache_samples=sum(int(r.get("boundary_cache_hit_samples","0")) for r in rows)

coord_diff=0
for r in m:
    a=(r.get("first_lat_bits"),r.get("first_lon_bits"))
    b=(r.get("first_expected_source_lat_bits"),r.get("first_expected_source_lon_bits"))
    if a != b:
        coord_diff += 1

print("tiles_seen=%d" % len(rows))
print("mismatch_tiles=%d" % len(m))
print("total_mismatch_count=%d" % total_mismatch)
print("boundary_cache_hit_samples=%d" % cache_samples)
print("boundary_cache_hit_mismatch_count=%d" % cache_mismatch)
print("non_boundary_cache_hit_mismatch_count=%d" % noncache_mismatch)
print("first_mismatch_source_coordinate_diff_tiles=%d" % coord_diff)

if len(rows) < 4:
    verdict="EVIDENCE_INCOMPLETE"
elif total_mismatch == 0 and cache_samples > 0:
    verdict="NATURAL_PARITY_REPAIR_PASS"
elif total_mismatch == 0:
    verdict="NO_CACHE_HIT_EVIDENCE"
elif cache_mismatch == total_mismatch and noncache_mismatch == 0 and coord_diff == len(m):
    verdict="BOUNDARY_CACHE_PROVENANCE_CONFIRMED"
elif noncache_mismatch > 0:
    verdict="BOUNDARY_CACHE_NOT_SUFFICIENT"
else:
    verdict="MIXED_OR_INCONCLUSIVE"
print("AERIS46_DIAGNOSTIC_VERDICT="+verdict)
PY

  rm -f "$seg"
  echo "environment_contract=ENV3_TERRAIN_CFG_PQS"
  echo "automatic_db_invalidation=false"
  echo "old_environment_chunks_preserved=true"
  echo "production_authority=PQS"
  echo "producer_switch=false"
  echo "db_write=false"
  echo "exact_cpu_db_write=false"
  echo "AERIS_CURRENT_STAGE=DIAGNOSTIC_CAPTURED"
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
[[ -f "$TARGET" ]] || { echo "STOP: installed DLL missing: $TARGET" >&2; exit 31; }

OLD_SHA="$(sha256sum "$TARGET" | awk '{print $1}')"
NEW_SHA="$(sha256sum "$DLL" | awk '{print $1}')"
BACKUP_DIR="$HOME/.cache/AERIS/mapso3-build-backups"
mkdir -p "$BACKUP_DIR"
BACKUP="$BACKUP_DIR/$(date +%Y%m%d-%H%M%S)-$OLD_SHA.AERISFlightControl.dll"
cp -a "$TARGET" "$BACKUP"
install -m0644 "$DLL" "$TARGET"
cmp -s "$DLL" "$TARGET" || { echo "STOP: installed DLL differs from build" >&2; exit 32; }

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

echo "=== AERIS46 NATURAL BOUNDARY DIAGNOSTIC INSTALLED ==="
echo "HEAD=$HEAD_SHA"
echo "candidate=$CANDIDATE"
echo "dll_sha256=$NEW_SHA"
echo "previous_dll_sha256=$OLD_SHA"
echo "backup=$BACKUP"
echo "log_offset=$OFFSET"
echo "diagnostic_bodies=Kerbin,Eve"
echo "diagnostic_lod=Far"
echo "diagnostic_tiles=4"
echo "adjacent_shared_boundary=true"
echo "database_deleted=false"
echo "database_rebuilt=false"
echo "diagnostic_db_write=false"
echo "environment_contract=ENV3_TERRAIN_CFG_PQS"
echo "automatic_db_invalidation=false"
echo "old_environment_chunks_preserved=true"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "exact_cpu_db_write=false"
echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"
echo "human_action=Launch KSP to Main Menu with Automatic Preload enabled. Wait for the pair diagnostic to complete, exit KSP, then run the same command again."
