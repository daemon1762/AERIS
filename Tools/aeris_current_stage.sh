#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="R043-PRELOAD-ENVIRONMENT-HASH-STABLE-V2"
RUNNER="$ROOT/Tools/aeris44_r043_preload_environment_hash_stability.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION -> PRELOAD_PTC -> TERRAIN_ND -> NEW_NAV -> LAND"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS44 ENV2 stability runner missing" >&2
  exit 20
}

# AERIS44 ENV2: prove the persistent terrain environment key is stable across
# two process restarts before any controlled full Preload rebuild. The runner
# preserves a pre-ENV2 DB backup and never deletes/rebuilds the DB itself.
exec bash "$RUNNER" "$KSP"
