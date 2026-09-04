#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_pqs_uv_witness_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V2_RADIUSMIN"
new_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V3_STOCK_PQS_UV"

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

capture_old = r'''                var terrainCheck = new TerrainAltitudeCheck
                {
                    Label = coord.Label,
                    U = coord.U,
                    V = coord.V,
                    Latitude = coord.Latitude,
                    Longitude = coord.Longitude,
                    X = coord.X,
                    Y = coord.Y,
                    Z = coord.Z
                };

                double expectedAsl;'''

capture_new = r'''                // Public PQS queries do not consume the synthetic census u/v.
                // They first create the KSP surface direction and then derive map
                // coordinates through PQS.BuildVertexMapCoords. Reconstruct that exact
                // coordinate state here on the main thread and copy only primitive
                // scalars to the worker.
                Vector3d pqsDirection = body.GetRelSurfaceNVector(
                    coord.Latitude,
                    coord.Longitude);
                double pqsLatitudeRad = Math.Asin(pqsDirection.y);
                double pqsLongitudeRad = Math.Atan2(
                    pqsDirection.z,
                    pqsDirection.x);
                double pqsU = pqsLongitudeRad / Math.PI * 0.5;
                double pqsV = pqsLatitudeRad / Math.PI + 0.5;

                var terrainCheck = new TerrainAltitudeCheck
                {
                    Label = coord.Label,
                    U = pqsU,
                    V = pqsV,
                    Latitude = coord.Latitude,
                    Longitude = coord.Longitude,
                    X = pqsDirection.x,
                    Y = pqsDirection.y,
                    Z = pqsDirection.z
                };

                double expectedAsl;'''

if obs.count(capture_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V3 capture marker not unique")
obs = obs.replace(capture_old, capture_new, 1)

snapshot_old = '''                "; radius_minus_radius_min=" + R(body.Radius - radiusMin) +
                "; semantics_candidates=BODY_RADIUS_RAW_ASL,BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO,PQS_RADIUSMIN_RAW_ASL,PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" +'''
snapshot_new = '''                "; radius_minus_radius_min=" + R(body.Radius - radiusMin) +
                "; terrain_direction_source=CELESTIALBODY_GETRELSURFACENVECTOR" +
                "; terrain_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS" +
                "; terrain_u_formula=ATAN2_Z_X_OVER_PI_TIMES_HALF" +
                "; terrain_v_formula=ASIN_Y_OVER_PI_PLUS_HALF" +
                "; synthetic_census_uv_reused=false" +
                "; semantics_candidates=BODY_RADIUS_RAW_ASL,BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO,PQS_RADIUSMIN_RAW_ASL,PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" +'''
if obs.count(snapshot_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V3 snapshot marker not unique")
obs = obs.replace(snapshot_old, snapshot_new, 1)

provenance_old = "terrainaltitude_semantics=RUNTIME_UNIQUE_BODY_RADIUS_OR_PQS_RADIUSMIN_RAW_OR_CLAMP0_CLOSURE"
provenance_new = provenance_old + (
    "\nterrainaltitude_direction_source=CELESTIALBODY_GETRELSURFACENVECTOR"
    "\nterrainaltitude_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS"
    "\nterrainaltitude_u_formula=ATAN2_Z_X_OVER_PI_TIMES_HALF"
    "\nterrainaltitude_v_formula=ASIN_Y_OVER_PI_PLUS_HALF"
    "\nterrainaltitude_synthetic_census_uv_reused=false")
if run.count(provenance_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V3 provenance marker not unique")
run = run.replace(provenance_old, provenance_new, 1)

# Require every body snapshot to prove the corrected public-PQS coordinate path.
accept_anchor = '''  for terrain_body in Kerbin Eve Duna Dres Moho Eeloo; do
    local terrain_body_line
    terrain_body_line="$(grep -F "[AERIS41][TERRAINALTITUDE_BODY]" "$segment" | grep -F "; body=$terrain_body;" | tail -n 1 || true)"
    [[ -n "$terrain_body_line" ]] || pass=0
  done'''
accept_new = accept_anchor + r'''
  for terrain_body in Kerbin Eve Duna Dres Moho Eeloo; do
    local terrain_snapshot_line
    terrain_snapshot_line="$(grep -F "[AERIS41][TERRAINALTITUDE_SNAPSHOT]" "$segment" | grep -F "; body=$terrain_body;" | tail -n 1 || true)"
    [[ -n "$terrain_snapshot_line" ]] || { pass=0; continue; }
    [[ "$terrain_snapshot_line" == *"; terrain_direction_source=CELESTIALBODY_GETRELSURFACENVECTOR;"* ]] || pass=0
    [[ "$terrain_snapshot_line" == *"; terrain_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS;"* ]] || pass=0
    [[ "$terrain_snapshot_line" == *"; synthetic_census_uv_reused=false;"* ]] || pass=0
  done'''
if run.count(accept_anchor) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V3 acceptance anchor not unique")
run = run.replace(accept_anchor, accept_new, 1)

for token in (
    new_candidate,
    "GetRelSurfaceNVector",
    "pqsLongitudeRad / Math.PI * 0.5",
    "pqsLatitudeRad / Math.PI + 0.5",
    "terrain_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS",
    "synthetic_census_uv_reused=false",
):
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude V3 observer lost token: " + token)

for token in (
    new_candidate,
    "terrainaltitude_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS",
    "synthetic_census_uv_reused=false",
):
    if token not in run:
        raise SystemExit("AERIS41 TerrainAltitude V3 runner lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
