#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_exact_public_pqs_fix1_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V5_EXACT_PUBLIC_PQS"
if obs.count(candidate) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix1 candidate marker not unique")
if candidate not in run:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix1 runner candidate missing")

old = '''                }
            result.TerrainFirstMismatches = terrainMismatches.ToArray();'''
new = '''                }
            }
            result.TerrainFirstMismatches = terrainMismatches.ToArray();'''
if obs.count(old) != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 fix1 worker-loop marker not unique: " +
        str(obs.count(old)))
obs = obs.replace(old, new, 1)

# Static structural gates for the final generated evaluator.
required = (
    "for (int ti = 0; ti < body.TerrainChecks.Length; ti++)",
    "body.PqsRadius",
    "TerrainPqsPublicMatches",
    "result.TerrainFirstMismatches = terrainMismatches.ToArray();",
)
for token in required:
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude V5 fix1 lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
