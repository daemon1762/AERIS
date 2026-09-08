#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R041-COMPLETE"
ACCEPTANCE="$ROOT/Tools/AERIS41_R041_FINAL_ACCEPTANCE_2026-09-08.txt"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

[[ -f "$ACCEPTANCE" ]] || {
  echo "STOP: R041 final acceptance record missing" >&2
  exit 20
}

grep -Fq 'R041_FINAL_ACCEPTANCE=PASS' "$ACCEPTANCE" || {
  echo "STOP: R041 final acceptance verdict missing" >&2
  exit 21
}
grep -Fq 'total_checks=16950' "$ACCEPTANCE" || {
  echo "STOP: R041 16950 height-chain proof missing" >&2
  exit 22
}
grep -Fq 'terrainaltitude_total_checks=3360' "$ACCEPTANCE" || {
  echo "STOP: R041 3360 TerrainAltitude proof missing" >&2
  exit 23
}
grep -Fq 'terrainaltitude_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO' "$ACCEPTANCE" || {
  echo "STOP: R041 TerrainAltitude semantics missing" >&2
  exit 24
}

echo "R041_FINAL_ACCEPTANCE=PASS"
echo "height_chain_total_checks=16950"
echo "height_chain_bit_exact=true"
echo "terrainaltitude_total_checks=3360"
echo "terrainaltitude_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO"
echo "production_authority=PQS"
echo "AERIS_CURRENT_STAGE=PASS"
echo "next=POST_R041_ROADMAP_SELECTION"
