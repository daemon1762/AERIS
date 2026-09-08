#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_exact_public_pqs_fix2_runner_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V5_EXACT_PUBLIC_PQS"
if obs.count(candidate) != 1 or candidate not in run:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix2 candidate identity missing")

old = '''  [[ "$terrain_complete" == *"; unique_semantics=true;"* ]] || pass=0'''
new = '''  [[ "$terrain_complete" == *"; pqs_public_global_pass=true;"* ]] || pass=0'''
if run.count(old) != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 fix2 stale unique-semantics gate not unique: " +
        str(run.count(old)))
run = run.replace(old, new, 1)

for token in (
    'selected_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO',
    'pqs_public_global_pass=',
    'AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS',
):
    if token not in (obs + run):
        raise SystemExit("AERIS41 TerrainAltitude V5 fix2 lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
