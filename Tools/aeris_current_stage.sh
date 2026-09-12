#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-EXACT-CPU-PRODUCTION"
RUNNER="$ROOT/Tools/aeris47_r043_exact_cpu_production.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS47 Exact CPU production runner missing" >&2
  exit 20
}

# AERIS47: promote the seven R042-certified bodies to Exact CPU production.
# ENV4 records the hybrid producer policy and preserves prior ENV3 chunks.
# Unsupported or runtime-certification-failed bodies remain fail-closed on PQS.
exec bash "$RUNNER" "$KSP"
