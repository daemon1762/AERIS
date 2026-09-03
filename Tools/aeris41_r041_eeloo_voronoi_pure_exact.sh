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
INJECTOR="$ROOT/Tools/aeris41_inject_voronoi_into_generated.py"
PURE="$ROOT/Source/AERISFlightControl/Terrain/AERIS41VertexVoronoiPureCpuExact.cs"

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before AERIS41 Eeloo Voronoi exact stage" >&2
  git status -sb >&2
  exit 11
}

[[ -f "$LAND_WRAPPER" ]] || { echo "STOP: LandControl repair wrapper missing" >&2; exit 12; }
[[ -f "$INJECTOR" ]] || { echo "STOP: Voronoi generated-stage injector missing" >&2; exit 13; }
[[ -f "$PURE" ]] || { echo "STOP: VertexVoronoi pure source missing" >&2; exit 14; }

grep -Fq 'static int VoronoiCell(double value)' "$PURE" || {
  echo "STOP: VertexVoronoi exact cell semantics missing" >&2
  exit 15
}
grep -Fq 'internal static int IntValueNoise(int x, int y, int z, int seed)' "$PURE" || {
  echo "STOP: VertexVoronoi exact IntValueNoise missing" >&2
  exit 16
}
grep -Fq 'const double Sqrt3 = 1.7320508075688772;' "$PURE" || {
  echo "STOP: captured LibNoise.Math.Sqrt3 missing" >&2
  exit 17
}

TMPDIR="$(mktemp -d /tmp/AERIS41_R041_VORONOI_EXACT.XXXXXX)"
WRAPPER="$TMPDIR/aeris41_r041_landcontrol_plus_voronoi.sh"
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
if src.count(root_old) != 1:
    raise SystemExit("AERIS41 outer-wrapper root marker not unique")
src = src.replace(root_old, root_new, 1)

marker = 'chmod 0700 "$SHADOW_RUNNER"'
if src.count(marker) != 1:
    raise SystemExit("AERIS41 Voronoi outer-wrapper insertion point not unique")

inject = (
    'python3 "$ROOT/Tools/aeris41_inject_voronoi_into_generated.py" '
    '"$SHADOW_OBSERVER" "$SHADOW_RUNNER"\n\n'
)
src = src.replace(marker, inject + marker, 1)

if 'AERIS41_OUTER_ROOT' not in src:
    raise SystemExit("AERIS41 transformed wrapper lost root handoff")
if 'aeris41_inject_voronoi_into_generated.py' not in src:
    raise SystemExit("AERIS41 transformed wrapper lost Voronoi injector")

out_path.write_text(src, encoding="utf-8")
PY

chmod 0700 "$WRAPPER"
export AERIS41_OUTER_ROOT="$ROOT"

set +e
bash "$WRAPPER" "$KSP"
RC=$?
set -e
exit "$RC"
