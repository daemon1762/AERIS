#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="TERRAIN_ND-FINAL-RUNTIME-CERT"
RUNNER="$ROOT/Tools/aeris49_terrain_nd_final_runtime_cert.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[FINAL_CERT] -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS49 final TERRAIN_ND runtime certification runner missing" >&2
  exit 20
}

exec bash "$RUNNER" "$KSP"
