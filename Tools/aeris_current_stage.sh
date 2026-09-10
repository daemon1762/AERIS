#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PTC-SHADOW-INTEGRATION"
RUNNER="$ROOT/Tools/aeris43_r043_preload_ptc_shadow_integration.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R043 PRELOAD_PTC integration runner missing" >&2
  exit 20
}

# R043 Phase2 exercises the real BlockPipeline path for PreloadBuilder-owned
# requests while PQS remains production/DB authority. The proof harness commits
# to RAM only; the runner owns WAITING/PASS/FAIL and remains the final process.
exec bash "$RUNNER" "$KSP"
