#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="TERRAIN_ND-RUNTIME-GATE"
RUNNER="$ROOT/Tools/aeris49_terrain_nd_runtime_gate.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[RUNTIME] -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS49 TERRAIN_ND runtime gate missing" >&2
  exit 20
}

# Runtime-only proof: no source/DLL mutation. The accepted PRELOAD_PTC DLL remains installed.
exec bash "$RUNNER" "$KSP"
