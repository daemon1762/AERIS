#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris39_mapso3f_coord_il_closure.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
EXPECTED_ASSEMBLY_SHA="d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4"
MANAGED="$KSP/KSP_x64_Data/Managed"
ASSEMBLY="$MANAGED/Assembly-CSharp.dll"
DUMPER_SRC="$ROOT/Tools/AERIS38_R041D_dump_mapso_il.cs"
ARTIFACT_ROOT="${AERIS_ARTIFACT_ROOT:-$HOME/.cache/AERIS/artifacts}"
OUT="$ARTIFACT_ROOT/AERIS39_MAPSO3F_Coord_IL_Closure"
ARCHIVE="$ARTIFACT_ROOT/AERIS39_MAPSO3F_Coord_IL_Closure.tar.gz"
TMP="$(mktemp -d /tmp/AERIS39_MAPSO3F.XXXXXX)"
DUMPER_EXE="$TMP/AERIS38_R041D_dump_mapso_il.exe"
FULL="$OUT/mapso_full_il.txt"
COORD="$OUT/construct_bilinear_coords_il.txt"

cleanup() {
  rm -rf "$TMP"
}
trap cleanup EXIT

cd "$ROOT"
for cmd in git sha256sum mcs mono python3 tar; do
  command -v "$cmd" >/dev/null 2>&1 || {
    echo "STOP: required command missing: $cmd" >&2
    exit 3
  }
done

[[ -f "$ASSEMBLY" ]] || { echo "STOP: missing Assembly-CSharp.dll" >&2; exit 4; }
[[ -f "$DUMPER_SRC" ]] || { echo "STOP: missing MAPSO IL dumper" >&2; exit 4; }
test "$(git branch --show-current)" = "$BRANCH" || { echo "STOP: wrong branch" >&2; exit 5; }
test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before MAPSO-3F" >&2
  git status -sb
  exit 6
}

ASSEMBLY_SHA="$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
[[ "$ASSEMBLY_SHA" = "$EXPECTED_ASSEMBLY_SHA" ]] || {
  echo "STOP: Assembly-CSharp identity mismatch" >&2
  echo "expected=$EXPECTED_ASSEMBLY_SHA" >&2
  echo "actual=$ASSEMBLY_SHA" >&2
  exit 7
}

mkdir -p "$ARTIFACT_ROOT"
rm -rf "$OUT"
mkdir -p "$OUT"

mcs -optimize+ -out:"$DUMPER_EXE" "$DUMPER_SRC"
mono "$DUMPER_EXE" "$MANAGED" > "$FULL"

grep -Fq 'AERIS38_R041D_MAPSO_IL_DUMP=PASS' "$FULL" || {
  echo "STOP: stock MapSO IL dump failed" >&2
  exit 8
}

python3 - "$FULL" "$COORD" <<'PY'
from pathlib import Path
import sys

src = Path(sys.argv[1]).read_text(encoding='utf-8')
blocks = src.split('\n--- METHOD ---\n')
selected = []
for block in blocks[1:]:
    body = '--- METHOD ---\n' + block
    if 'signature=void ConstructBilinearCoords(' in body or ' ConstructBilinearCoords(' in body:
        selected.append(body.rstrip() + '\n')
if not selected:
    raise SystemExit('ConstructBilinearCoords IL block not found')
Path(sys.argv[2]).write_text('\n'.join(selected), encoding='utf-8')
print('construct_bilinear_methods=' + str(len(selected)))
PY

cat > "$OUT/provenance.txt" <<EOF
schema=AERIS39_MAPSO3F_COORD_IL_CLOSURE_V1
branch=$BRANCH
git_head=$(git rev-parse HEAD)
ksp_root=$KSP
assembly_sha256=$ASSEMBLY_SHA
dumper_source_sha256=$(sha256sum "$DUMPER_SRC" | awk '{print $1}')
production_authority=PQS
db_authority=PQS
producer_switch=false
db_write=false
preload_mutation=false
runtime_object_access=false
EOF

(
  cd "$OUT"
  sha256sum mapso_full_il.txt construct_bilinear_coords_il.txt provenance.txt > SHA256SUMS.txt
)
rm -f "$ARCHIVE"
tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_MAPSO3F_Coord_IL_Closure

echo "=== MAPSO-3F STOCK CONSTRUCT BILINEAR IL ==="
cat "$COORD"
echo
sha256sum "$COORD"
echo "archive=$ARCHIVE"
echo "AERIS39_MAPSO3F_COORD_IL_CLOSURE=PASS"
echo "AERIS_CURRENT_STAGE=PASS"
echo "next=MAPSO-3F_IL_ANALYSIS"
