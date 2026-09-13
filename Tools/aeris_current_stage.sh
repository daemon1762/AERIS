#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="ENV4-BODY-SCOPED-TERRAIN-FINGERPRINT-HOTFIX"
RUNNER="$ROOT/Tools/aeris53_env4_body_scoped_fingerprint_hotfix.sh"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo "roadmap=CPU_SHADOW_PRODUCTION[ACCEPTED] -> PRELOAD_PTC[ACCEPTED] -> TERRAIN_ND[ACCEPTED] -> LAND[R1 PASS] -> PRELOAD_COAST_FASTPATH[C1+C2 ACCEPTED] -> PRELOAD_COAST_C3[ACCEPTED] -> ENV4_BODY_SCOPE_HOTFIX[ACTIVE] -> LAND[R2 NEXT] -> NEW_NAV[BLOCKED]"
echo

[[ -f "$RUNNER" ]] || {
  echo "STOP: AERIS53 ENV4 body-scope runner missing" >&2
  exit 20
}

exec bash "$RUNNER" "$KSP"
