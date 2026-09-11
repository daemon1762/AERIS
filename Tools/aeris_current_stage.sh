#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-PTC-DB-RESTART-REPRO"
RUNNER="$ROOT/Tools/aeris43_r043_preload_ptc_db_restart_repro.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R043 PRELOAD_PTC DB restart-repro runner missing" >&2
  exit 20
}

# R043 Phase4 proves restart persistence and official codec/readback equivalence.
# It reads the previously committed Minmus/Kerbin GLOBAL tiles through the
# production TryLoadBatch path and compares them against fresh PQS -> official codec
# references. No DB write or producer switch is permitted.
exec bash "$RUNNER" "$KSP"
