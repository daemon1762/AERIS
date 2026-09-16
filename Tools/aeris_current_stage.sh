#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=PRELOAD-PRODUCER-COHERENCE-HOTFIX-ACCEPTED"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACCEPTED] -> LAND[R1 PASS] -> PRELOAD_COAST_FASTPATH[C1+C2 ACCEPTED] -> PRELOAD_COAST_C3[ACCEPTED] -> ENV4_BODY_SCOPE_HF2[ACCEPTED] -> PRODUCER_COHERENCE_HOTFIX[ACCEPTED] -> LAND[R2 NEXT] -> NEW_NAV[BLOCKED]"
echo
echo "AERIS54_PRODUCER_COHERENCE=ACCEPTED"
echo "accepted_runtime_dll_sha256=8e888eeb29861b387ebd141bd00b86106c0d90b1d35a24828f7dcf7defa90a52"
echo "kerbin_coastline_complete=6728/6728"
echo "land_control_authority=NONE_PILOT"
echo "AERIS_CURRENT_STAGE=LAND_R2_UNBLOCKED"
echo "next_action=Switch to the rebased LAND-R2 branch and run its start audit."
