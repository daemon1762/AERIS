#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_exact_public_pqs_fix3_pqsradius_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V5_EXACT_PUBLIC_PQS"
if obs.count(candidate) != 1 or candidate not in run:
    raise SystemExit("AERIS41 TerrainAltitude V5 fix3 candidate identity missing")

capture_old = '''            object pqs = body.pqsController;
            double radiusMin = ReadDouble(pqs, "radiusMin");
            List<ModRecord> mods = CollectHeightMods(pqs);'''
capture_new = '''            object pqs = body.pqsController;
            double radiusMin = ReadDouble(pqs, "radiusMin");
            double pqsRadius = ReadDouble(pqs, "radius");
            List<ModRecord> mods = CollectHeightMods(pqs);'''
if obs.count(capture_old) != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 fix3 PQS snapshot marker not unique: " +
        str(obs.count(capture_old)))
obs = obs.replace(capture_old, capture_new, 1)

log_old = '''                "; pqs_radius=" + R(pqs.radius) +'''
log_new = '''                "; pqs_radius=" + R(pqsRadius) +'''
if obs.count(log_old) != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 fix3 PQS log marker not unique: " +
        str(obs.count(log_old)))
obs = obs.replace(log_old, log_new, 1)

return_old = '''                PqsRadius = pqs.radius,'''
return_new = '''                PqsRadius = pqsRadius,'''
if obs.count(return_old) != 1:
    raise SystemExit(
        "AERIS41 TerrainAltitude V5 fix3 BodyCase radius marker not unique: " +
        str(obs.count(return_old)))
obs = obs.replace(return_old, return_new, 1)

for stale in ("pqs.radius",):
    if stale in obs:
        raise SystemExit("AERIS41 TerrainAltitude V5 fix3 stale typed-PQS access remains: " + stale)

for token in (
    'double pqsRadius = ReadDouble(pqs, "radius");',
    '"; pqs_radius=" + R(pqsRadius) +',
    'PqsRadius = pqsRadius,',
    'snapshot_payload=PRIMITIVES_ONLY',
):
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude V5 fix3 lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
