#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PTC-PRODUCTION-AUDIT"
RUNNER="$ROOT/Tools/aeris48_r043_preload_ptc_production_audit.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACTIVE] -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS48 Preload PTC production audit runner missing" >&2
  exit 20
}

# AERIS48 begins with a no-behavior-change production-path audit.
# The accepted AERIS47 Exact CPU producer remains the authority while we identify
# which R043 shadow/proof seams are still required and which are historical only.
exec bash "$RUNNER" "$KSP"
