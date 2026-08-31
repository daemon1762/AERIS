#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="MAPSO-3-REAL-BODY-HEIGHTMAP-WITNESS"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris39_mapso3_real_body_heightmap_witness.sh" || {
  echo "STOP: MAPSO-3 runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris39_mapso3_real_body_heightmap_witness.sh" "$KSP"
