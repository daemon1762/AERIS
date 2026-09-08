#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R042-PHASE3-SOURCE-RESOLVER-CONTRACT"
RUNNER="$ROOT/Tools/aeris42_r042_phase3_source_resolver_contract.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: R042 phase3 runner missing" >&2
  exit 20
}

bash "$RUNNER" "$KSP"

echo
echo "=== AERIS42 CURRENT STAGE RESULT ==="
echo "R041_FINAL_ACCEPTANCE=PRESERVED"
echo "exact_pure_permanent_compile=true"
echo "source_resolver_contract=true"
echo "runtime_snapshot_gate_required=true"
echo "production_authority=PQS"
echo "producer_switch=false"
echo "AERIS_CURRENT_STAGE=PASS"
echo "next=R042_MAIN_THREAD_RUNTIME_SNAPSHOT_CAPTURE"
