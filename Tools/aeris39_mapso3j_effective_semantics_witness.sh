#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris39_mapso3j_effective_semantics_witness.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BASE="$ROOT/Tools/aeris39_mapso3_real_body_heightmap_witness.sh"
TMP="/tmp/aeris39_mapso3j_effective_semantics_witness.sh"

cleanup() { rm -f "$TMP"; }
trap cleanup EXIT

[[ -f "$BASE" ]] || { echo "STOP: base MAPSO-3 runner missing" >&2; exit 20; }
[[ -f "$ROOT/Source/AERISFlightControl/Terrain/AERIS39MapSoRuntimeSemanticsResolver.cs" ]] || { echo "STOP: MAPSO-3J semantics resolver missing" >&2; exit 21; }
[[ -f "$ROOT/Source/AERISFlightControl/Terrain/AERIS39MapSoEffectiveSemanticsWitnessObserver.cs" ]] || { echo "STOP: MAPSO-3J effective semantics observer missing" >&2; exit 22; }

python3 - "$BASE" "$TMP" <<'PY'
import pathlib
import sys

src = pathlib.Path(sys.argv[1]).read_text(encoding="utf-8")

old_root = 'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"'
new_root = 'ROOT="${AERIS_MAPSO3J_ROOT:?AERIS_MAPSO3J_ROOT not set}"'
if src.count(old_root) != 1:
    raise SystemExit("MAPSO3J root patch point mismatch")
src = src.replace(old_root, new_root, 1)

old_compile = r'''    "    <Compile Include=\"Terrain\\AERIS39MapSoRealBodyHeightMapWitnessObserver.cs\" />\n"'''
new_compile = r'''    "    <Compile Include=\"Terrain\\AERIS39MapSoRuntimeSemanticsResolver.cs\" />\n"
    "    <Compile Include=\"Terrain\\AERIS39MapSoEffectiveSemanticsWitnessObserver.cs\" />\n"'''
if src.count(old_compile) != 1:
    raise SystemExit("MAPSO3J compile patch point mismatch")
src = src.replace(old_compile, new_compile, 1)

old_prov = 'observer_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoRealBodyHeightMapWitnessObserver.cs" | awk \'{print $1}\')'
new_prov = (
    'observer_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoEffectiveSemanticsWitnessObserver.cs" | awk \'{print $1}\')\n'
    'semantics_resolver_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoRuntimeSemanticsResolver.cs" | awk \'{print $1}\')'
)
if src.count(old_prov) != 1:
    raise SystemExit("MAPSO3J provenance patch point mismatch")
src = src.replace(old_prov, new_prov, 1)

pathlib.Path(sys.argv[2]).write_text(src, encoding="utf-8")
PY

chmod 0700 "$TMP"
export AERIS_MAPSO3J_ROOT="$ROOT"
bash "$TMP" "$KSP"
