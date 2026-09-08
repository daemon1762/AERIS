#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris41_r041_eeloo_voronoi_pure_exact_v5fix3.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
BASE="$ROOT/Tools/aeris41_r041_eeloo_voronoi_pure_exact_v5shim.sh"
FIX3="$ROOT/Tools/aeris41_inject_terrainaltitude_exact_public_pqs_fix3_pqsradius_into_generated.py"

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before AERIS41 V5 fix3 stage" >&2
  git status -sb >&2
  exit 11
}

[[ -f "$BASE" ]] || { echo "STOP: V5 shim wrapper missing" >&2; exit 12; }
[[ -f "$FIX3" ]] || { echo "STOP: V5 PQS-radius fix3 injector missing" >&2; exit 13; }
grep -Fq 'double pqsRadius = ReadDouble(pqs, "radius");' "$FIX3" || {
  echo "STOP: V5 PQS-radius reflection snapshot gate missing" >&2
  exit 14
}

TMPDIR="$(mktemp -d /tmp/AERIS41_R041_V5FIX3.XXXXXX)"
WRAPPER="$TMPDIR/aeris41_r041_v5shim_plus_fix3.sh"
cleanup() { rm -rf "$TMPDIR"; }
trap cleanup EXIT

python3 - "$BASE" "$WRAPPER" <<'PY'
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
        "AERIS41 V5 fix3 root marker not unique: " + str(len(root_matches)))
idx = root_matches[0]
newline = "\r\n" if lines[idx].endswith("\r\n") else "\n"
lines[idx] = root_new + newline
src = "".join(lines)

identity_block = '''    'python3 "$ROOT/Tools/aeris41_inject_runtime_state_identity_into_generated.py" '
    '"$SHADOW_OBSERVER" "$SHADOW_RUNNER"\\n\\n'
'''
if src.count(identity_block) != 1:
    raise SystemExit(
        "AERIS41 V5 fix3 embedded state-identity marker not unique: " +
        str(src.count(identity_block)))
fix3_block = '''    'python3 "$ROOT/Tools/aeris41_inject_terrainaltitude_exact_public_pqs_fix3_pqsradius_into_generated.py" '
    '"$SHADOW_OBSERVER" "$SHADOW_RUNNER"\\n'
'''
src = src.replace(identity_block, fix3_block + identity_block, 1)

required_token = 'aeris41_inject_terrainaltitude_exact_public_pqs_fix3_pqsradius_into_generated.py'
if src.count(required_token) != 1:
    raise SystemExit(
        "AERIS41 V5 fix3 transformed wrapper token not unique: " +
        str(src.count(required_token)))

out_path.write_text(src, encoding="utf-8")
PY

chmod 0700 "$WRAPPER"
export AERIS41_OUTER_ROOT="$ROOT"

set +e
bash "$WRAPPER" "$KSP"
RC=$?
set -e
exit "$RC"
