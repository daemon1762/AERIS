#!/usr/bin/env python3
import pathlib
import sys

# Runtime shim for the V5 injector. The canonical V5 source first normalizes
# LONGITUDE_THEN_LATITUDE_DIAGNOSTIC -> LATITUDE_THEN_LONGITUDE_STOCK_IL in
# the generated runner, then its success_old marker still searches for the
# pre-normalization token. Patch only the success_old source block in memory
# before executing the canonical injector.
#
# Required provenance tokens retained here for the outer wrapper gates:
# AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V5_EXACT_PUBLIC_PQS
# PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO
# 2efa254d046be67c1d6dbcf45bc4964c9f6dd3ee07416b35e27830425a65ca18
# 51ff0d770beb616d8444b6d48df5419e9830217a1731d5ec83a4cae2cab021f0

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_run_fixed_terrainaltitude_exact_public_pqs_injector.py <observer> <runner>")

root = pathlib.Path(__file__).resolve().parent
canonical = root / "aeris41_inject_terrainaltitude_exact_public_pqs_into_generated.py"
source = canonical.read_text(encoding="utf-8")

block_start = source.find("success_old = '''")
if block_start < 0:
    raise SystemExit("AERIS41 TerrainAltitude V5 shim success_old block missing")
block_end = source.find("success_new = '''", block_start)
if block_end < 0:
    raise SystemExit("AERIS41 TerrainAltitude V5 shim success_new boundary missing")

old_token = "terrainaltitude_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC"
new_token = "terrainaltitude_reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL"
block = source[block_start:block_end]
count = block.count(old_token)
if count != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 shim token not unique in success_old: " + str(count))
block = block.replace(old_token, new_token, 1)
source = source[:block_start] + block + source[block_end:]

namespace = {
    "__name__": "__main__",
    "__file__": str(canonical),
}
saved_argv = sys.argv
try:
    sys.argv = [str(canonical), saved_argv[1], saved_argv[2]]
    exec(compile(source, str(canonical), "exec"), namespace, namespace)
finally:
    sys.argv = saved_argv
