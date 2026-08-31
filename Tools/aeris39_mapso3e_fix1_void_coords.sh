#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris39_mapso3e_fix1_void_coords.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
PATCH="$ROOT/Tools/AERIS39_MAPSO3E_fix1_void_coords_patch.py"
OBSERVER_IN="$ROOT/Source/AERISFlightControl/Terrain/AERIS39MapSoPipelineIsolationDiagnosticObserver.cs"
RUNNER_IN="$ROOT/Tools/aeris39_mapso3e_pipeline_isolation_diagnostic.sh"
OBSERVER_OUT="/tmp/AERIS39MapSoPipelineIsolationDiagnosticObserver.Fix1.cs"
RUNNER_OUT="/tmp/aeris39_mapso3e_fix1_pipeline_isolation_diagnostic.sh"

cleanup() {
  rm -f "$OBSERVER_OUT" "$RUNNER_OUT"
}
trap cleanup EXIT

[[ -f "$PATCH" ]] || { echo "STOP: MAPSO-3E Fix1 patch missing" >&2; exit 20; }
[[ -f "$OBSERVER_IN" ]] || { echo "STOP: MAPSO-3E observer missing" >&2; exit 21; }
[[ -f "$RUNNER_IN" ]] || { echo "STOP: MAPSO-3E base runner missing" >&2; exit 22; }

python3 "$PATCH" "$OBSERVER_IN" "$RUNNER_IN" "$OBSERVER_OUT" "$RUNNER_OUT"
export AERIS_FIX1_ROOT="$ROOT"
bash "$RUNNER_OUT" "$KSP"
