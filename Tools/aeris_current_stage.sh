#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="PRELOAD-COASTLINE-C3-DYNAMIC-ADMISSION"
RUNNER="$ROOT/Tools/aeris52_preload_coastline_c3_dynamic_admission.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACCEPTED] -> LAND[R1 PASS] -> PRELOAD_COAST_FASTPATH[C1+C2 ACCEPTED] -> PRELOAD_COAST_C3[ACTIVE] -> LAND[R2 NEXT] -> NEW_NAV[BLOCKED]"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS52 preload coastline C3 runner missing" >&2
  exit 20
}

exec bash "$RUNNER" "$KSP"
