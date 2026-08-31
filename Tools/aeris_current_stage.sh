#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="MAPSO-3D-DISPATCH-THREAD-TOPOLOGY-DIAGNOSTIC"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris39_mapso3_dispatch_thread_topology_diagnostic.sh" || {
  echo "STOP: MAPSO-3D runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris39_mapso3_dispatch_thread_topology_diagnostic.sh" "$KSP"
