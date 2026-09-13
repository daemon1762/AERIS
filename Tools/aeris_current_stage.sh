#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="PRELOAD-COASTLINE-FASTPATH-C1-C2"
RUNNER="$ROOT/Tools/aeris51_preload_coastline_fastpath_c1_c2.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACCEPTED] -> LAND[R1 PASS] -> PRELOAD_COAST_FASTPATH[ACTIVE:C1+C2] -> LAND[R2 NEXT] -> NEW_NAV[BLOCKED]"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS51 preload coastline fastpath runner missing" >&2
  exit 20
}

exec bash "$RUNNER" "$KSP"
