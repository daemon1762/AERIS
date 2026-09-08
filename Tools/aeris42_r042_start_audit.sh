#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

EXPECTED_BRANCH="agent/aeris42-r042-exact-cpu-shadow-production"
ACCEPTANCE="Tools/AERIS41_R041_FINAL_ACCEPTANCE_2026-09-08.txt"
CSPROJ="Source/AERISFlightControl/AERISFlightControl.csproj"
PIPELINE="Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs"

printf '%s\n' '=== AERIS42 / R042 START AUDIT ==='
printf 'branch=%s\n' "$(git branch --show-current)"
printf 'HEAD=%s\n' "$(git rev-parse HEAD)"

test "$(git branch --show-current)" = "$EXPECTED_BRANCH" || {
  echo "STOP: wrong branch" >&2
  exit 10
}
[[ -f "$ACCEPTANCE" ]] || { echo "STOP: R041 final acceptance missing" >&2; exit 11; }
grep -Fq 'R041_FINAL_ACCEPTANCE=PASS' "$ACCEPTANCE" || { echo "STOP: R041 final acceptance not PASS" >&2; exit 12; }
grep -Fq 'total_checks=16950' "$ACCEPTANCE" || { echo "STOP: R041 16950 proof missing" >&2; exit 13; }
grep -Fq 'terrainaltitude_total_checks=3360' "$ACCEPTANCE" || { echo "STOP: R041 3360 public-PQS proof missing" >&2; exit 14; }
grep -Fq 'production_authority=PQS' "$ACCEPTANCE" || { echo "STOP: R041 production authority invariant missing" >&2; exit 15; }
grep -Fq 'producer_switch=false' "$ACCEPTANCE" || { echo "STOP: R041 producer-switch invariant missing" >&2; exit 16; }

grep -Fq 'AERISTerrainAwareness.TrySampleTerrainAslShared(state.Body, latitude,' "$PIPELINE" || {
  echo "STOP: current PQS production hook not found" >&2
  exit 17
}

sources=(
  AERIS39MapSoPureCpuExact.cs
  AERIS39HeightMapPureCpuExact.cs
  AERIS39MapDecalPureCpuExact.cs
  AERIS39MapDecalTangentPureCpuExact.cs
  AERIS39LandControlPureCpuExact.cs
  AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs
  AERIS41VertexVoronoiPureCpuExact.cs
  AERIS39AllBodyHeightModifierChainPureCpuExact.cs
)
missing_source=0
missing_compile=0
for name in "${sources[@]}"; do
  path="Source/AERISFlightControl/Terrain/$name"
  if [[ ! -f "$path" ]]; then
    echo "source_missing=$name"
    missing_source=$((missing_source + 1))
    continue
  fi
  echo "source_present=$name"
  if ! grep -Fq "Terrain\\$name" "$CSPROJ"; then
    echo "permanent_compile_missing=$name"
    missing_compile=$((missing_compile + 1))
  else
    echo "permanent_compile_present=$name"
  fi
done

printf 'missing_source_count=%d\n' "$missing_source"
printf 'missing_permanent_compile_count=%d\n' "$missing_compile"
if (( missing_source != 0 )); then
  echo 'R042_PROMOTION_AUDIT=FAIL_SOURCE_CLOSURE'
  exit 20
fi

if (( missing_compile != 0 )); then
  echo 'permanent_compile_promotion_required=true'
else
  echo 'permanent_compile_promotion_required=false'
fi

echo 'current_production_authority=PQS'
echo 'target_production_authority=CERTIFIED_EXACT_CPU_SHADOW_WITH_PQS_FALLBACK'
echo 'R042_PROMOTION_AUDIT=PASS'
