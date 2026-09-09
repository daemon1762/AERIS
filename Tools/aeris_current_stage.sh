#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R042-PHASE5-WORKER-EXECUTION-PARITY"
RUNNER="$ROOT/Tools/aeris42_r042_phase5_worker_execution_parity.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R042 phase5 runner missing" >&2
  exit 20
}

# Phase 5 is a two-pass runtime gate. The runner owns WAITING/PASS/FAIL state
# and remains the final process so the wrapper cannot print a false PASS while
# KSP runtime evidence is still pending.
exec bash "$RUNNER" "$KSP"
