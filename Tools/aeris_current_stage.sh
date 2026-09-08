#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R042-PHASE1-EXACT-COMPILE-CLOSURE"
RUNNER="$ROOT/Tools/aeris42_r042_phase1_compile_closure.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R042 phase1 runner missing" >&2
  exit 20
}

bash "$RUNNER" "$KSP"

echo
echo "=== AERIS42 CURRENT STAGE RESULT ==="
echo "R041_FINAL_ACCEPTANCE=PRESERVED"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "AERIS_CURRENT_STAGE=PASS"
echo "next=R042_PERMANENT_CSPROJ_PROMOTION_AFTER_COMPILE_PROOF"
