#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris41_r041_terrainaltitude_il_closure.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
SRC="$ROOT/Tools/AERIS41_R041_dump_terrainaltitude_il.cs"
MANAGED="$KSP/KSP_x64_Data/Managed"
ASSEMBLY="$MANAGED/Assembly-CSharp.dll"
ARTIFACT_ROOT="${AERIS_ARTIFACT_ROOT:-$HOME/.cache/AERIS/artifacts}"
OUT="$ARTIFACT_ROOT/AERIS41_R041_TerrainAltitude_IL_Closure"
TMPDIR="$(mktemp -d /tmp/AERIS41_R041_TERRAINALTITUDE_IL.XXXXXX)"
EXE="$TMPDIR/AERIS41_R041_dump_terrainaltitude_il.exe"
LOG="$OUT/terrainaltitude_il_closure.txt"
cleanup() { rm -rf "$TMPDIR"; }
trap cleanup EXIT

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before TerrainAltitude IL closure" >&2
  git status -sb >&2
  exit 11
}

for cmd in git mcs mono sha256sum tee grep mkdir rm awk; do
  command -v "$cmd" >/dev/null 2>&1 || {
    echo "STOP: required command missing: $cmd" >&2
    exit 12
  }
done

[[ -f "$SRC" ]] || { echo "STOP: TerrainAltitude IL dumper source missing" >&2; exit 13; }
[[ -f "$ASSEMBLY" ]] || { echo "STOP: Assembly-CSharp.dll missing" >&2; exit 14; }

rm -rf "$OUT"
mkdir -p "$OUT"

echo "=== AERIS41 R041 TERRAINALTITUDE IL CLOSURE BUILD ==="
echo "HEAD=$(git rev-parse HEAD)"
echo "KSP=$KSP"
echo "assembly_sha256=$(sha256sum "$ASSEMBLY" | awk '{print $1}')"
echo "source_sha256=$(sha256sum "$SRC" | awk '{print $1}')"
echo

mcs -optimize+ -out:"$EXE" "$SRC"

set +e
mono "$EXE" "$MANAGED" | tee "$LOG"
RC=${PIPESTATUS[0]}
set -e

if [[ "$RC" -ne 0 ]]; then
  echo "AERIS41_R041_TERRAINALTITUDE_IL_CLOSURE_RUN=FAIL"
  echo "runtime_rc=$RC"
  echo "artifact=$LOG"
  echo "AERIS_CURRENT_STAGE=FAIL"
  exit "$RC"
fi

if ! grep -Fq 'AERIS41_R041_TERRAINALTITUDE_IL_CLOSURE=PASS' "$LOG"; then
  echo "AERIS41_R041_TERRAINALTITUDE_IL_CLOSURE_RUN=FAIL"
  echo "reason=PASS_MARKER_MISSING"
  echo "artifact=$LOG"
  echo "AERIS_CURRENT_STAGE=FAIL"
  exit 30
fi

for required in \
  ' TerrainAltitude(' \
  ' GetRelSurfaceNVector(' \
  ' GetSurfaceHeight(' \
  ' BuildVertexMapCoords('; do
  if ! grep -Fq "$required" "$LOG"; then
    echo "AERIS41_R041_TERRAINALTITUDE_IL_CLOSURE_RUN=FAIL"
    echo "reason=REQUIRED_SIGNATURE_MISSING"
    echo "missing=$required"
    echo "artifact=$LOG"
    echo "AERIS_CURRENT_STAGE=FAIL"
    exit 31
  fi
done

echo
echo "=== AERIS41 R041 TERRAINALTITUDE IL CLOSURE RESULT ==="
echo "AERIS41_R041_TERRAINALTITUDE_IL_CLOSURE_RUN=PASS"
echo "artifact=$LOG"
echo "AERIS_CURRENT_STAGE=PASS"
echo "next=R041_TERRAINALTITUDE_COORDINATE_SEMANTICS_FROM_IL"
