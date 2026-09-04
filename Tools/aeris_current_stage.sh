#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R041-TERRAINALTITUDE-RESULT-HARVEST"
CANDIDATE="AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V1"
LOG="$KSP/GameData/AERISFlightControl/Logs/AERISFlightControl.log"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

[[ -f "$LOG" ]] || {
  echo "STOP: AERIS runtime log missing: $LOG" >&2
  exit 20
}

complete="$(grep -F '[AERIS41][TERRAINALTITUDE_COMPLETE]' "$LOG" | grep -F "; candidate=$CANDIDATE;" | tail -n 1 || true)"

if [[ -z "$complete" ]]; then
  echo "=== R041 TERRAINALTITUDE RESULT HARVEST INCOMPLETE ==="
  grep -F '[AERIS41][TERRAINALTITUDE_' "$LOG" | grep -F "; candidate=$CANDIDATE;" | tail -n 40 || true
  echo "AERIS41_R041_TERRAINALTITUDE_RESULT_HARVEST=FAIL"
  echo "AERIS_CURRENT_STAGE=FAIL"
  echo "human_action=Do not launch KSP; paste this command output."
  exit 21
fi

echo "=== R041 TERRAINALTITUDE EXISTING RUNTIME RESULT ==="
for body in Kerbin Eve Duna Dres Moho Eeloo; do
  grep -F '[AERIS41][TERRAINALTITUDE_BODY]' "$LOG" \
    | grep -F "; candidate=$CANDIDATE;" \
    | grep -F "; body=$body;" \
    | tail -n 1 || true
done

echo
echo "--- MISMATCH WITNESSES (if any) ---"
grep -F '[AERIS41][TERRAINALTITUDE_MISMATCH]' "$LOG" \
  | grep -F "; candidate=$CANDIDATE;" \
  | tail -n 24 || true

echo
echo "--- COMPLETE ---"
echo "$complete"
echo

echo "AERIS41_R041_TERRAINALTITUDE_RESULT_HARVEST=PASS"
echo "AERIS_CURRENT_STAGE=PASS"
echo "next=R041_DIAGNOSE_TERRAINALTITUDE_VERDICT"
