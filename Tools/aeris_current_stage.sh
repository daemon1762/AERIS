#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-CLEAN-REBUILD-ENV3-RESTART-VERIFY"
RUNNER="$ROOT/Tools/aeris46_r043_preload_clean_rebuild_env3_restart_verify.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS46 ENV3 clean rebuild verifier missing" >&2
  exit 20
}

# AERIS46: move the current DB to a timestamped backup, create a clean ENV3
# preload from zero, then prove all-body completion survives a process restart.
# Natural exact parity repair is already laptop-certified; desktop cross-check
# remains deferred until the user returns.
# Exact CPU remains shadow-only; PQS remains production and DB authority.
exec bash "$RUNNER" "$KSP"
