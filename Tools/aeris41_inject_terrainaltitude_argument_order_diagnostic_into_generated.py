#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_argument_order_diagnostic_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V3_STOCK_PQS_UV"
new_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V4_ARGUMENT_ORDER_DIAGNOSTIC"

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V3 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

call_old = '''                terrainCheck.HasValue = AERISTerrainAwareness.TrySampleTerrainAslShared(
                    body, coord.Latitude, coord.Longitude, out expectedAsl);'''
call_new = '''                // Diagnostic only: V1-V3 proved that the production helper's
                // current latitude-then-longitude input order does not agree with
                // the exact pure chain at the same geodetic point. Change only
                // the two scalar inputs here so the runtime can prove or reject
                // the opposite order without modifying production code yet.
                terrainCheck.HasValue = AERISTerrainAwareness.TrySampleTerrainAslShared(
                    body, coord.Longitude, coord.Latitude, out expectedAsl);'''
if obs.count(call_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V4 reference-call marker not unique")
obs = obs.replace(call_old, call_new, 1)

snapshot_old = '''                "; synthetic_census_uv_reused=false" +
                "; semantics_candidates=BODY_RADIUS_RAW_ASL,BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO,PQS_RADIUSMIN_RAW_ASL,PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" +'''
snapshot_new = '''                "; synthetic_census_uv_reused=false" +
                "; terrain_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC" +
                "; production_helper_code_unchanged=true" +
                "; semantics_candidates=BODY_RADIUS_RAW_ASL,BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO,PQS_RADIUSMIN_RAW_ASL,PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" +'''
if obs.count(snapshot_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V4 snapshot marker not unique")
obs = obs.replace(snapshot_old, snapshot_new, 1)

old_reference = "AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED"
new_reference = "AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED_SWAPPED_INPUT_DIAGNOSTIC"
obs_ref_count = obs.count(old_reference)
run_ref_count = run.count(old_reference)
if obs_ref_count < 3:
    raise SystemExit("AERIS41 TerrainAltitude V4 observer reference markers too few: " + str(obs_ref_count))
if run_ref_count < 2:
    raise SystemExit("AERIS41 TerrainAltitude V4 runner reference markers too few: " + str(run_ref_count))
obs = obs.replace(old_reference, new_reference)
run = run.replace(old_reference, new_reference)

prov_anchor = "terrainaltitude_synthetic_census_uv_reused=false"
prov_new = prov_anchor + "\nterrainaltitude_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC\nterrainaltitude_production_helper_code_unchanged=true"
if run.count(prov_anchor) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V4 provenance marker not unique")
run = run.replace(prov_anchor, prov_new, 1)

success_old = '''  echo "AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS"
  echo "terrainaltitude_semantics=$terrain_semantics"
  echo "terrainaltitude_total_checks=$terrain_total_checks"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_POST_TERRAINALTITUDE_STAGE_PENDING"'''
success_new = '''  echo "AERIS41_R041_TERRAINALTITUDE_ARGUMENT_ORDER_DIAGNOSTIC=PASS"
  echo "terrainaltitude_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC"
  echo "terrainaltitude_semantics=$terrain_semantics"
  echo "terrainaltitude_total_checks=$terrain_total_checks"
  echo "production_helper_code_unchanged=true"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_REPAIR_TERRAINALTITUDE_ARGUMENT_ORDER"'''
if run.count(success_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V4 success-tail marker not unique")
run = run.replace(success_old, success_new, 1)

for token in (
    new_candidate,
    "body, coord.Longitude, coord.Latitude, out expectedAsl",
    "terrain_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC",
    "production_helper_code_unchanged=true",
    new_reference,
):
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude V4 observer lost token: " + token)

for token in (
    new_candidate,
    new_reference,
    "terrainaltitude_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC",
    "AERIS41_R041_TERRAINALTITUDE_ARGUMENT_ORDER_DIAGNOSTIC=PASS",
    "next=R041_REPAIR_TERRAINALTITUDE_ARGUMENT_ORDER",
):
    if token not in run:
        raise SystemExit("AERIS41 TerrainAltitude V4 runner lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
