#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R041-HEIGHTMAP-FULL-FORMULA-SHADOW"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris39_r041_heightmap_full_formula_shadow.sh" || {
  echo "STOP: R041 HeightMap full formula shadow runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris39_r041_heightmap_full_formula_shadow.sh" "$KSP"
