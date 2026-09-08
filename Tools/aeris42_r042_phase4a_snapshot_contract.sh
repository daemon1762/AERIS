#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris42_r042_phase4a_snapshot_contract.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
PROJECT_DIR="$ROOT/Source/AERISFlightControl"
PROJECT="$PROJECT_DIR/AERISFlightControl.csproj"
TEMP_PROJECT="$PROJECT_DIR/AERISFlightControl.R042Phase4A.csproj"
RESOLVER="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowSourceResolver.cs"
SNAPSHOT="$PROJECT_DIR/Terrain/AERISR042ExactCpuShadowRuntimeSnapshot.cs"
ACCEPTANCE="$ROOT/Tools/AERIS41_R041_FINAL_ACCEPTANCE_2026-09-08.txt"
V5_WRAPPER="$ROOT/Tools/aeris41_r041_eeloo_voronoi_pure_exact_v5shim.sh"
FIX3_WRAPPER="$ROOT/Tools/aeris41_r041_eeloo_voronoi_pure_exact_v5fix3.sh"
DLL="$PROJECT_DIR/bin/Release/AERISFlightControl.dll"

cleanup() {
  rm -f "$TEMP_PROJECT"
}
trap cleanup EXIT

cd "$ROOT"

echo "=== AERIS42 / R042 PHASE4A IMMUTABLE SNAPSHOT CONTRACT ==="
echo "branch=$(git branch --show-current)"
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"

test "$(git branch --show-current)" = "$EXPECTED_BRANCH" || {
  echo "STOP: wrong branch" >&2
  exit 10
}
[[ -d "$KSP/KSP_x64_Data/Managed" ]] || { echo "STOP: KSP managed directory missing" >&2; exit 11; }
[[ -f "$RESOLVER" ]] || { echo "STOP: resolver missing" >&2; exit 12; }
[[ -f "$SNAPSHOT" ]] || { echo "STOP: runtime snapshot contract missing" >&2; exit 13; }
[[ -f "$ACCEPTANCE" ]] || { echo "STOP: R041 acceptance missing" >&2; exit 14; }
[[ -f "$V5_WRAPPER" ]] || { echo "STOP: R041 V5 shim wrapper missing" >&2; exit 15; }
[[ -f "$FIX3_WRAPPER" ]] || { echo "STOP: R041 V5 fix3 wrapper missing" >&2; exit 16; }
command -v xbuild >/dev/null 2>&1 || { echo "STOP: xbuild not found" >&2; exit 17; }

# Preserve the exact accepted proof before promoting any runtime contract.
grep -Fq 'R041_FINAL_ACCEPTANCE=PASS' "$ACCEPTANCE" || exit 20
grep -Fq 'total_checks=16950' "$ACCEPTANCE" || exit 21
grep -Fq 'terrainaltitude_total_checks=3360' "$ACCEPTANCE" || exit 22
grep -Fq 'production_authority=PQS' "$ACCEPTANCE" || exit 23
grep -Fq 'producer_switch=false' "$ACCEPTANCE" || exit 24

# Final V5 provenance. Phase 4B capture must be derived from this generated chain,
# not from the older canonical V1 observer alone.
for token in \
  aeris41_inject_voronoi_into_generated.py \
  aeris41_inject_heightnoise_into_generated.py \
  aeris41_inject_curve2_exact_repair_into_generated.py \
  aeris41_run_fixed_terrainaltitude_exact_public_pqs_injector.py \
  aeris41_inject_runtime_state_identity_into_generated.py; do
  grep -Fq "$token" "$V5_WRAPPER" || {
    echo "STOP: final V5 provenance token missing: $token" >&2
    exit 25
  }
  echo "v5_provenance_present=$token"
done

grep -Fq 'aeris41_inject_terrainaltitude_exact_public_pqs_fix3_pqsradius_into_generated.py' "$FIX3_WRAPPER" || {
  echo "STOP: final V5 PQS-radius fix3 provenance missing" >&2
  exit 26
}
echo "v5_provenance_present=PQS_RADIUS_FIX3"

# Worker payload contract must carry pure snapshots only. Runtime KSP objects are
# intentionally absent from fields and constructor arguments.
grep -Fq 'readonly AERISR039MinmusPureCpuExact.VertexPlanetSnapshot MinmusSnapshot;' "$SNAPSHOT" || exit 30
grep -Fq 'readonly AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot HeightChainSnapshot;' "$SNAPSHOT" || exit 31
grep -Fq 'readonly double PqsRadius;' "$SNAPSHOT" || exit 32
grep -Fq 'readonly int CaptureThreadId;' "$SNAPSHOT" || exit 33
grep -Fq 'if (hasMinmus == hasHeightChain)' "$SNAPSHOT" || exit 34
if grep -Eq '^[[:space:]]*(internal|public|private|protected)[^;]*(CelestialBody|PQSMod|PQS[[:space:]])[^;]*;' "$SNAPSHOT"; then
  echo "STOP: runtime KSP object field leaked into worker snapshot" >&2
  exit 35
fi

python3 - "$PROJECT" "$TEMP_PROJECT" <<'PY'
from pathlib import Path
import sys
src = Path(sys.argv[1])
dst = Path(sys.argv[2])
text = src.read_text(encoding="utf-8")
entries = [
    '    <Compile Include="Terrain\\AERISR042ExactCpuShadowSourceResolver.cs" />\n',
    '    <Compile Include="Terrain\\AERISR042ExactCpuShadowRuntimeSnapshot.cs" />\n',
]
marker = '    <Compile Include="Terrain\\AERISTerrainPreloadCodec.cs" />'
if marker not in text:
    raise SystemExit("STOP: csproj insertion marker not found")
for entry in entries:
    if entry.strip() not in text:
        text = text.replace(marker, entry + marker, 1)
dst.write_text(text, encoding="utf-8")
print("temporary_resolver_compile_entry=true")
print("temporary_snapshot_contract_compile_entry=true")
PY

echo
echo "=== XBUILD / PHASE4A SNAPSHOT CONTRACT ==="
(
  cd "$PROJECT_DIR"
  xbuild /p:Configuration=Release /p:KSPDIR="$KSP" "$(basename "$TEMP_PROJECT")"
)

[[ -f "$DLL" ]] || { echo "STOP: expected DLL not produced" >&2; exit 40; }

echo
echo "=== R042 PHASE4A RESULT ==="
echo "dll=$DLL"
echo "dll_sha256=$(sha256sum "$DLL" | awk '{print $1}')"
echo "snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY"
echo "runtime_object_fields=0"
echo "snapshot_family_exclusive=true"
echo "final_v5_provenance_locked=true"
echo "runtime_behavior_changed=false"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "db_write_switch=false"
echo "R042_PHASE4A_SNAPSHOT_CONTRACT=PASS"
echo "next=R042_PHASE4B_MAIN_THREAD_RUNTIME_CAPTURE"
