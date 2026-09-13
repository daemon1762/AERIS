#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="LAND-R0-RESTART-AUDIT"
RUNNER="$ROOT/Tools/aeris50_land_r0_restart_audit.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACCEPTED] -> LAND[ACTIVE:R0] -> NEW_NAV[BLOCKED]"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS50 LAND R0 restart audit runner missing" >&2
  exit 20
}

exec bash "$RUNNER" "$KSP"
