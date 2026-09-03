#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R041-EELOO-HEIGHTNOISE-VERTHEIGHT-IL-CLOSURE"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris41_r041_eeloo_heightnoise_vertheight_il_closure.sh" || {
  echo "STOP: AERIS41 R041 Eeloo HeightNoiseVertHeight IL closure runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris41_r041_eeloo_heightnoise_vertheight_il_closure.sh" "$KSP"
