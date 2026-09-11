#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PERSISTENCE-NATURAL-PARITY-DIAGNOSTIC"
RUNNER="$ROOT/Tools/aeris44_r043_preload_persistence_natural_parity_diagnostic.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS44 preload diagnostic runner missing" >&2
  exit 20
}

# AERIS44 diagnostic hold. Do not delete/rebuild the existing preload DB.
# Capture persisted/live environment identity and the first natural Kerbin
# bit mismatch before resuming DB restart/promotion work.
exec bash "$RUNNER" "$KSP"
