#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="MAPSO-3J-KSPCF-AWARE-EFFECTIVE-SEMANTICS-ACCEPTANCE"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris39_mapso3j_effective_semantics_witness.sh" || {
  echo "STOP: MAPSO-3J effective semantics runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris39_mapso3j_effective_semantics_witness.sh" "$KSP"
