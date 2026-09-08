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

# V1/V2 exploratory acceptance required unique_semantics=true. V5 no longer
# searches candidate semantics: captured stock IL defines the single public-PQS
# semantics, and the observer reports pqs_public_global_pass=true only when all
# public TerrainAltitude checks close. Replace only that stale runner gate.
old = '''  [[ "$terrain_complete" == *"; unique_semantics=true;"* ]] || pass=0'''
new = '''  [[ "$terrain_complete" == *"; pqs_public_global_pass=true;"* ]] || pass=0'''
if run.count(old) != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 fix2 stale unique-semantics gate not unique: " +
        str(run.count(old)))
run = run.replace(old, new, 1)

# Validate the structures that actually exist after the canonical V5 injector.
# selected_semantics is emitted dynamically by the observer; the runner consumes
# it through terrain_semantics, so do not require a nonexistent literal
# 'selected_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO' in generated source.
runner_semantics_gate = '''  [[ "$terrain_semantics" = "PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO" ]] || pass=0'''
if runner_semantics_gate not in run:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix2 runner semantics gate missing")
if 'PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO' not in obs:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix2 observer public semantics missing")
if 'pqs_public_global_pass=true' not in run:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix2 public-global runner gate missing")
if 'AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS' not in run:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix2 success marker missing")

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
