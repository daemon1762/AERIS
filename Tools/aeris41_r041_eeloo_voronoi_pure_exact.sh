#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris41_r041_eeloo_voronoi_pure_exact.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
LAND_WRAPPER="$ROOT/Tools/aeris41_r041_landcontrol_witness_repair.sh"
VORONOI_INJECTOR="$ROOT/Tools/aeris41_inject_voronoi_into_generated.py"
HEIGHTNOISE_INJECTOR="$ROOT/Tools/aeris41_inject_heightnoise_into_generated.py"
VORONOI_PURE="$ROOT/Source/AERISFlightControl/Terrain/AERIS41VertexVoronoiPureCpuExact.cs"
HEIGHTNOISE_PURE="$ROOT/Source/AERISFlightControl/Terrain/AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs"

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before AERIS41 Eeloo exact stage" >&2
  git status -sb >&2
  exit 11
}

[[ -f "$LAND_WRAPPER" ]] || { echo "STOP: LandControl repair wrapper missing" >&2; exit 12; }
[[ -f "$VORONOI_INJECTOR" ]] || { echo "STOP: Voronoi injector missing" >&2; exit 13; }
[[ -f "$HEIGHTNOISE_INJECTOR" ]] || { echo "STOP: HeightNoise injector missing" >&2; exit 14; }
[[ -f "$VORONOI_PURE" ]] || { echo "STOP: VertexVoronoi pure source missing" >&2; exit 15; }
[[ -f "$HEIGHTNOISE_PURE" ]] || { echo "STOP: VertexHeightNoiseVertHeight pure source missing" >&2; exit 16; }

grep -Fq 'static int VoronoiCell(double value)' "$VORONOI_PURE" || {
  echo "STOP: VertexVoronoi exact cell semantics missing" >&2
  exit 17
}
grep -Fq 'AERISR039MinmusPureCpuExact.RidgedGetValue' "$HEIGHTNOISE_PURE" || {
  echo "STOP: HeightNoise R039 Ridged exact reuse missing" >&2
  exit 18
}
grep -Fq '0c6ef5f07a24e18ecb86404c162a79872d583da0cfa46e16c8c31cbfa92ad7fc' "$HEIGHTNOISE_PURE" || {
  echo "STOP: HeightNoise captured IL identity missing" >&2
  exit 19
}

TMPDIR="$(mktemp -d /tmp/AERIS41_R041_EELOO_EXACT.XXXXXX)"
WRAPPER="$TMPDIR/aeris41_r041_landcontrol_plus_eeloo_exact.sh"
cleanup() { rm -rf "$TMPDIR"; }
trap cleanup EXIT

python3 - "$LAND_WRAPPER" "$WRAPPER" <<'PY'
import pathlib
import sys

source_path = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
src = source_path.read_text(encoding="utf-8")

root_old = 'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"'
root_new = 'ROOT="${AERIS41_OUTER_ROOT:?}"'
lines = src.splitlines(True)
root_matches = [
    i for i, line in enumerate(lines)
    if line.rstrip("\r\n") == root_old
]
if len(root_matches) != 1:
    raise SystemExit(
        "AERIS41 outer-wrapper root shell-line marker not unique: " +
        str(len(root_matches)))
idx = root_matches[0]
newline = "\r\n" if lines[idx].endswith("\r\n") else "\n"
lines[idx] = root_new + newline
src = "".join(lines)

marker = 'chmod 0700 "$SHADOW_RUNNER"'
if src.count(marker) != 1:
    raise SystemExit("AERIS41 Eeloo outer-wrapper insertion point not unique")

inject = (
    'python3 "$ROOT/Tools/aeris41_inject_voronoi_into_generated.py" '
    '"$SHADOW_OBSERVER" "$SHADOW_RUNNER"\n'
    'python3 "$ROOT/Tools/aeris41_inject_heightnoise_into_generated.py" '
    '"$SHADOW_OBSERVER" "$SHADOW_RUNNER"\n\n'
)
src = src.replace(marker, inject + marker, 1)

if src.splitlines()[8] != root_new:
    raise SystemExit("AERIS41 transformed wrapper root handoff landed on wrong line")
for token in (
    'aeris41_inject_voronoi_into_generated.py',
    'aeris41_inject_heightnoise_into_generated.py',
):
    if token not in src:
        raise SystemExit("AERIS41 transformed wrapper lost injector: " + token)

out_path.write_text(src, encoding="utf-8")
PY

chmod 0700 "$WRAPPER"
export AERIS41_OUTER_ROOT="$ROOT"

set +e
bash "$WRAPPER" "$KSP"
RC=$?
set -e
exit "$RC"
