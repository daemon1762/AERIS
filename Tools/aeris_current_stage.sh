#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R042-PHASE5-GEODETIC-CENSUS-DIAGNOSTIC"
RUNNER="$ROOT/Tools/aeris42_r042_phase5_geodetic_census_diagnostic.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

if [[ ! -f "$RUNNER" ]]; then
  echo "STOP: R042 Phase5 geodetic diagnostic runner missing" >&2
  exit 20
fi

# Diagnostic-only two-pass runtime gate. The runner builds/installs on pass 1,
# then harvests the exact Phase5 BuildSamples rejected values after KSP has run.
# It does not promote the producer or modify DB/preload authority.
exec bash "$RUNNER" "$KSP"
