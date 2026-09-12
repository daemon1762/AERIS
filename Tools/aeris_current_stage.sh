#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PTC-PRODUCTION-CLEANUP2"
RUNNER="$ROOT/Tools/aeris48_r043_preload_ptc_production_cleanup2.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[CLEANUP2] -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS48 Preload PTC cleanup2 runner missing" >&2
  exit 20
}

# Cleanup2 is a semantics-preserving rename of the shared R043 PTC pipeline state.
# Exact CPU production, PQS fallback, ENV4 identity and DB semantics remain unchanged.
exec bash "$RUNNER" "$KSP"
