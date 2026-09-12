#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="TERRAIN_ND-INTEGRATION-AUDIT"
RUNNER="$ROOT/Tools/aeris49_terrain_nd_integration_audit.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACTIVE] -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS49 TERRAIN_ND integration audit runner missing" >&2
  exit 20
}

# No behavior change in this gate. It certifies that the accepted R023 ND core still
# consumes the evolved TileSystem/Resident/ENV4 chain after PRELOAD_PTC production.
exec bash "$RUNNER" "$KSP"
