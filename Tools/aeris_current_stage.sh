#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R042-PHASE4B-MAIN-THREAD-RUNTIME-CAPTURE"
RUNNER="$ROOT/Tools/aeris42_r042_phase4b_main_thread_runtime_capture.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> LAND -> NEW_NAV"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R042 phase4B runner missing" >&2
  exit 20
}

# Phase 4B is a two-pass runtime gate. The runner itself owns WAITING/PASS/FAIL
# state and must be the final process so this wrapper cannot print a false PASS
# while KSP runtime evidence is still pending.
exec bash "$RUNNER" "$KSP"
