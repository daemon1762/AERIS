#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PTC-SHADOW-TILEGRID"
RUNNER="$ROOT/Tools/aeris43_r043_preload_ptc_shadow_tilegrid.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R043 PRELOAD_PTC shadow runner missing" >&2
  exit 20
}

# R043 Phase1 is a two-pass proof-only runtime gate. The live PreloadBuilder,
# BlockPipeline and DB remain byte-identical to the accepted R042 Phase5 code.
# The runner owns WAITING/PASS/FAIL and remains the final process.
exec bash "$RUNNER" "$KSP"
