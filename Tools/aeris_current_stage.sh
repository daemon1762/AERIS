#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris_current_stage.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
STAGE="MAPSO-2B-FIX1-LAUNCHER-COMMISSIONING"
OUT="$HOME/Desktop/AERIS39_MAPSO2B_FIX1_Exception_Parity"

cd "$ROOT"

echo "=== AERIS CURRENT STAGE ==="
echo "stage=$STAGE"
echo "KSP=$KSP"
echo "HEAD=$(git rev-parse HEAD)"
echo

test -f "$ROOT/Tools/aeris39_mapso2b_fix1_exception_parity.sh" || {
  echo "STOP: MAPSO-2B Fix1 runner missing" >&2
  exit 20
}

bash "$ROOT/Tools/aeris39_mapso2b_fix1_exception_parity.sh" "$KSP" "$OUT"

echo
echo "=== AERIS STAGE ACCEPTANCE ==="
grep -E '^(managed_identity_exact|constant_bits_exact|snapshots|coordinate_pairs|total_checks|mismatch_count|exception_match_count|exception_mismatch_count|max_abs_error|bit_exact|AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS)=' \
  "$OUT/mapso2b_fix1_exception_parity.txt" || true

grep -q '^managed_identity_exact=true$' "$OUT/mapso2b_fix1_exception_parity.txt"
grep -q '^constant_bits_exact=true$' "$OUT/mapso2b_fix1_exception_parity.txt"
grep -q '^mismatch_count=0$' "$OUT/mapso2b_fix1_exception_parity.txt"
grep -q '^exception_mismatch_count=0$' "$OUT/mapso2b_fix1_exception_parity.txt"
grep -q '^max_abs_error=0$' "$OUT/mapso2b_fix1_exception_parity.txt"
grep -q '^bit_exact=true$' "$OUT/mapso2b_fix1_exception_parity.txt"
grep -q '^AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS=PASS$' "$OUT/mapso2b_fix1_exception_parity.txt"

echo "AERIS_CURRENT_STAGE=PASS"
echo "next=MAPSO-3_REAL_BODY_HEIGHTMAP_WITNESS"
