#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PTC-SHADOW-LIVE-PRELOAD"
RUNNER="$ROOT/Tools/aeris43_r043_preload_ptc_shadow_live_preload.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R043 PRELOAD_PTC live-preload runner missing" >&2
  exit 20
}

# R043 Phase3 exercises the persistent AERIS PreloadBuilder -> real BlockPipeline
# -> encode -> SaveEncodedBatch DB path. Existing DB content is not deleted or
# rebuilt; exact CPU remains shadow-only and PQS remains production/DB authority.
exec bash "$RUNNER" "$KSP"
