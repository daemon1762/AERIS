#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGE="NEW_NAV-ARCHITECTURE-AUDIT"
RUNNER="$ROOT/Tools/aeris50_new_nav_architecture_audit.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACCEPTED] -> NEW_NAV[ACTIVE] -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS50 NEW_NAV architecture audit runner missing" >&2
  exit 20
}

exec bash "$RUNNER"
