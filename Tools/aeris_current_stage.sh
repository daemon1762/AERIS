#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="MAPSO-3E-FIX1-VOID-COORDS-PIPELINE-ISOLATION"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris39_mapso3e_fix1_void_coords.sh" || {
  echo "STOP: MAPSO-3E Fix1 runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris39_mapso3e_fix1_void_coords.sh" "$KSP"
