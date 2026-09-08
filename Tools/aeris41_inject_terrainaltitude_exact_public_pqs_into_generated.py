#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_exact_public_pqs_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V4_ARGUMENT_ORDER_DIAGNOSTIC"
new_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V5_EXACT_PUBLIC_PQS"

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

# Public KSP 1.12.5 TerrainAltitude closure captured from Assembly-CSharp.dll
# sha256=d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4
IL_TERRAIN_ALTITUDE = "cc031fe01c2988f752a62e66c607366c71d9b0ff56fc09e15301382c75cddec1"
IL_GET_REL_SURFACE_N_VECTOR = "184e6c8cacba2476abb0dc0cce72fdb4bf59a4b2d1c3c479ea793d6bafe6120b"
IL_SPHERICAL_VECTOR = "2a291df8912b57f4af7a341e49ef8883cc61f90ffd2a6cd4bc188d8b485c3028"
IL_GET_SURFACE_HEIGHT = "51ff0d770beb616d8444b6d48df5419e9830217a1731d5ec83a4cae2cab021f0"
IL_BUILD_VERTEX_MAP_COORDS = "2efa254d046be67c1d6dbcf45bc4964c9f6dd3ee07416b35e27830425a65ca18"
IL_MOD_ON_VERTEX_BUILD_HEIGHT = "c41033e5b177545f6619f4c8ae3ad12f26709d11517902ebd3d6aeaa3fe54d7f"
IL_VERTEX_BUILD_DATA_RESET = "789cc45dddb8f4143876ebd6757da2bdb31deea350a65fad5dde581609aceb33"

body_case_old = '''            internal double Radius;
            internal double RadiusMin;
            internal TerrainAltitudeCheck[] TerrainChecks;
        }'''
body_case_new = '''            internal double Radius;
            internal double RadiusMin;
            internal double PqsRadius;
            internal TerrainAltitudeCheck[] TerrainChecks;
        }'''
if obs.count(body_case_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 BodyCase marker not unique")
obs = obs.replace(body_case_old, body_case_new, 1)

body_result_old = '''            internal double TerrainRadiusMinRawMaxError;
            internal double TerrainRadiusMinClampZeroMaxError;
            internal string[] TerrainFirstMismatches;
        }'''
body_result_new = '''            internal double TerrainRadiusMinRawMaxError;
            internal double TerrainRadiusMinClampZeroMaxError;
            internal int TerrainPqsPublicMatches;
            internal double TerrainPqsPublicMaxError;
            internal string[] TerrainFirstMismatches;
        }'''
if obs.count(body_result_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 BodyResult marker not unique")
obs = obs.replace(body_result_old, body_result_new, 1)

capture_old = r'''                // Public PQS queries do not consume the synthetic census u/v.
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

capture_new = r'''                // Exact public-PQS entry reconstruction from captured stock IL:
                // TerrainAltitude -> GetRelSurfaceNVector -> GetSurfaceHeight.
                // GetSurfaceHeight normalizes the radial vector before assigning
                // VertexBuildData.directionFromCenter. Mod_OnVertexBuildHeight then
                // invokes BuildVertexMapCoords once before modifier dispatch when map
                // coordinates are required. Do not reuse the synthetic census u/v.
                Vector3d pqsInputDirection = body.GetRelSurfaceNVector(
                    coord.Latitude,
                    coord.Longitude);
                Vector3d pqsDirection = pqsInputDirection.normalized;

                double pqsLatitudeRad = Math.Asin(pqsDirection.y);
                if (double.IsNaN(pqsLatitudeRad))
                    pqsLatitudeRad = Math.PI * 0.5;

                Vector3d pqsDirectionXZ = new Vector3d(
                    pqsDirection.x,
                    0.0,
                    pqsDirection.z).normalized;
                double pqsDirectionXZMagnitude = pqsDirectionXZ.magnitude;
                double pqsLongitudeRad;
                if (pqsDirectionXZMagnitude > 0.0)
                {
                    double longitudeBasis =
                        pqsDirectionXZ.x / pqsDirectionXZMagnitude;
                    pqsLongitudeRad = pqsDirectionXZ.z < 0.0
                        ? Math.PI - Math.Asin(longitudeBasis)
                        : Math.Asin(longitudeBasis);
                }
                else
                {
                    pqsLongitudeRad = 0.0;
                }

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
    raise SystemExit("AERIS41 TerrainAltitude V5 capture marker not unique")
obs = obs.replace(capture_old, capture_new, 1)

call_old = '''                // Diagnostic only: V1-V3 proved that the production helper's
                // current latitude-then-longitude input order does not agree with
                // the exact pure chain at the same geodetic point. Change only
                // the two scalar inputs here so the runtime can prove or reject
                // the opposite order without modifying production code yet.
                terrainCheck.HasValue = AERISTerrainAwareness.TrySampleTerrainAslShared(
                    body, coord.Longitude, coord.Latitude, out expectedAsl);'''
call_new = '''                terrainCheck.HasValue = AERISTerrainAwareness.TrySampleTerrainAslShared(
                    body, coord.Latitude, coord.Longitude, out expectedAsl);'''
if obs.count(call_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 stock reference-call marker not unique")
obs = obs.replace(call_old, call_new, 1)

return_old = '''                Radius = body.Radius,
                RadiusMin = radiusMin,
                TerrainChecks = terrainChecks.ToArray()
            };'''
return_new = '''                Radius = body.Radius,
                RadiusMin = radiusMin,
                PqsRadius = pqs.radius,
                TerrainChecks = terrainChecks.ToArray()
            };'''
if obs.count(return_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 BodyCase return marker not unique")
obs = obs.replace(return_old, return_new, 1)

# Correct stale diagnostic metadata left by V2-V4.
obs = obs.replace(
    '"; pqs_radius_min=" + R(radiusMin) +',
    '"; pqs_radius_min=" + R(radiusMin) +\n                "; pqs_radius=" + R(pqs.radius) +',
    1)
obs = obs.replace(
    "terrain_u_formula=ATAN2_Z_X_OVER_PI_TIMES_HALF",
    "terrain_u_formula=STOCK_ASIN_X_WITH_Z_HEMISPHERE_OVER_PI_TIMES_HALF")
obs = obs.replace(
    "terrain_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC",
    "terrain_reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL")
obs = obs.replace(
    "production_helper_code_unchanged=true",
    "production_helper_code_unchanged=true")
old_semantics_meta = "; semantics_candidates=BODY_RADIUS_RAW_ASL,BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO,PQS_RADIUSMIN_RAW_ASL,PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO"
new_semantics_meta = "; terrain_public_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO"
if obs.count(old_semantics_meta) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 snapshot semantics marker not unique")
obs = obs.replace(old_semantics_meta, new_semantics_meta, 1)

snapshot_anchor = '''                "; production_helper_code_unchanged=true" +'''
snapshot_il = '''                "; production_helper_code_unchanged=true" +
                "; il_terrainaltitude_sha256=''' + IL_TERRAIN_ALTITUDE + '''" +
                "; il_getrelsurfacenvector_sha256=''' + IL_GET_REL_SURFACE_N_VECTOR + '''" +
                "; il_sphericalvector_sha256=''' + IL_SPHERICAL_VECTOR + '''" +
                "; il_getsurfaceheight_sha256=''' + IL_GET_SURFACE_HEIGHT + '''" +
                "; il_buildvertexmapcoords_sha256=''' + IL_BUILD_VERTEX_MAP_COORDS + '''" +
                "; il_mod_onvertexbuildheight_sha256=''' + IL_MOD_ON_VERTEX_BUILD_HEIGHT + '''" +
                "; il_vertexbuilddata_reset_sha256=''' + IL_VERTEX_BUILD_DATA_RESET + '''" +'''
if obs.count(snapshot_anchor) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 IL snapshot anchor not unique")
obs = obs.replace(snapshot_anchor, snapshot_il, 1)

# Replace the V2 exploratory evaluator with the IL-defined public-PQS evaluator.
eval_start = obs.find(
    '''                double bodyRadiusAbsolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(''')
eval_end_marker = '''            result.TerrainFirstMismatches = terrainMismatches.ToArray();'''
if eval_start < 0:
    raise SystemExit("AERIS41 TerrainAltitude V5 evaluator start missing")
eval_end = obs.find(eval_end_marker, eval_start)
if eval_end < 0:
    raise SystemExit("AERIS41 TerrainAltitude V5 evaluator end missing")

eval_new = r'''                double pqsAbsolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                    body.Snapshot,
                    expectedTerrain.X,
                    expectedTerrain.Y,
                    expectedTerrain.Z,
                    expectedTerrain.U,
                    expectedTerrain.V,
                    body.PqsRadius);
                double pqsRawAsl = pqsAbsolute - body.PqsRadius;
                double pqsPublicAsl = pqsRawAsl;
                if (pqsPublicAsl < 0.0)
                    pqsPublicAsl = 0.0;
                double pqsPublicError = Math.Abs(
                    pqsPublicAsl - expectedTerrain.ExpectedAsl);

                if (pqsPublicError > result.TerrainPqsPublicMaxError)
                    result.TerrainPqsPublicMaxError = pqsPublicError;
                if (pqsPublicError <= TerrainToleranceMeters)
                    result.TerrainPqsPublicMatches++;

                if (pqsPublicError > TerrainToleranceMeters &&
                    terrainMismatches.Count < 12)
                {
                    terrainMismatches.Add(
                        expectedTerrain.Label +
                        " lat=" + expectedTerrain.Latitude.ToString("R", CultureInfo.InvariantCulture) +
                        " lon=" + expectedTerrain.Longitude.ToString("R", CultureInfo.InvariantCulture) +
                        " pqs_reference=" + expectedTerrain.ExpectedAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " pqs_radius=" + body.PqsRadius.ToString("R", CultureInfo.InvariantCulture) +
                        " direction=(" +
                            expectedTerrain.X.ToString("R", CultureInfo.InvariantCulture) + "," +
                            expectedTerrain.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
                            expectedTerrain.Z.ToString("R", CultureInfo.InvariantCulture) + ")" +
                        " u=" + expectedTerrain.U.ToString("R", CultureInfo.InvariantCulture) +
                        " v=" + expectedTerrain.V.ToString("R", CultureInfo.InvariantCulture) +
                        " pure_absolute=" + pqsAbsolute.ToString("R", CultureInfo.InvariantCulture) +
                        " pure_raw_asl=" + pqsRawAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " pure_public_asl=" + pqsPublicAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " error=" + pqsPublicError.ToString("R", CultureInfo.InvariantCulture));
                }
'''
obs = obs[:eval_start] + eval_new + obs[eval_end:]

# Replace the exploratory multi-semantics report with one stock-IL-defined verdict.
report_start = obs.find('''            bool terrainRawGlobal = workerNotMain;''')
report_end_marker = '''        CurveSelection SelectCurveSnapshot'''
if report_start < 0:
    raise SystemExit("AERIS41 TerrainAltitude V5 report start missing")
report_end = obs.find(report_end_marker, report_start)
if report_end < 0:
    raise SystemExit("AERIS41 TerrainAltitude V5 report end missing")

report_new = r'''            bool terrainPublicGlobal = workerNotMain;
            int terrainChecks = 0;
            int terrainReferenceValues = 0;
            int terrainPublicMatches = 0;
            double terrainPublicMaxError = 0.0;

            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult body = result.Bodies[i];
                if (body == null)
                {
                    terrainPublicGlobal = false;
                    continue;
                }

                bool bodyReferenceComplete =
                    body.TerrainChecks > 0 &&
                    body.TerrainReferenceValues == body.TerrainChecks;
                bool bodyPublic = bodyReferenceComplete &&
                    body.TerrainPqsPublicMatches == body.TerrainChecks;
                terrainPublicGlobal &= bodyPublic;
                terrainChecks += body.TerrainChecks;
                terrainReferenceValues += body.TerrainReferenceValues;
                terrainPublicMatches += body.TerrainPqsPublicMatches;
                terrainPublicMaxError = Math.Max(
                    terrainPublicMaxError, body.TerrainPqsPublicMaxError);

                AERISLogger.Info(
                    "[AERIS41][TERRAINALTITUDE_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(body.Name) +
                    "; checks=" + body.TerrainChecks.ToString(CultureInfo.InvariantCulture) +
                    "; reference_values=" + body.TerrainReferenceValues.ToString(CultureInfo.InvariantCulture) +
                    "; pqs_public_matches=" + body.TerrainPqsPublicMatches.ToString(CultureInfo.InvariantCulture) +
                    "; pqs_public_max_error_m=" + R(body.TerrainPqsPublicMaxError) +
                    "; body_semantics=" + (bodyPublic ? "PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO" : "NONE") +
                    "; terrain_tolerance_m=1E-08" +
                    "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                    "; reference_thread=MAIN_THREAD_ONLY" +
                    "; terrain_direction_source=CELESTIALBODY_GETRELSURFACENVECTOR_THEN_NORMALIZED_BY_PQS_GETSURFACEHEIGHT" +
                    "; terrain_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS" +
                    "; initial_vert_height=PQS_RADIUS" +
                    "; asl_subtract=PQS_RADIUS" +
                    "; allow_negative=false" +
                    "; snapshot_payload=PRIMITIVES_ONLY" + Invariants());

                if (body.TerrainFirstMismatches == null) continue;
                for (int m = 0; m < body.TerrainFirstMismatches.Length; m++)
                {
                    AERISLogger.Warn(
                        "[AERIS41][TERRAINALTITUDE_MISMATCH]" +
                        "; body=" + Safe(body.Name) +
                        "; detail=" + Safe(body.TerrainFirstMismatches[m]) + Invariants());
                }
            }

            bool terrainPass =
                terrainPublicGlobal &&
                terrainChecks > 0 &&
                terrainReferenceValues == terrainChecks &&
                terrainPublicMatches == terrainChecks;
            string selectedSemantics = terrainPass
                ? "PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO"
                : "NONE";

            AERISLogger.Info(
                "[AERIS41][TERRAINALTITUDE_COMPLETE]" +
                "; pass=" + Bool(terrainPass) +
                "; candidate=" + Candidate +
                "; bodies=" + result.Bodies.Length.ToString(CultureInfo.InvariantCulture) +
                "; total_checks=" + terrainChecks.ToString(CultureInfo.InvariantCulture) +
                "; reference_values=" + terrainReferenceValues.ToString(CultureInfo.InvariantCulture) +
                "; pqs_public_matches=" + terrainPublicMatches.ToString(CultureInfo.InvariantCulture) +
                "; pqs_public_global_pass=" + Bool(terrainPublicGlobal) +
                "; selected_semantics=" + selectedSemantics +
                "; pqs_public_max_error_m=" + R(terrainPublicMaxError) +
                "; terrain_tolerance_m=1E-08" +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                "; reference_thread=MAIN_THREAD_ONLY" +
                "; reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL" +
                "; direction_semantics=SPHERICALVECTOR_XZY_THEN_PQS_NORMALIZED" +
                "; mapcoord_semantics=BUILDVERTEXMAPCOORDS_CAPTURED_STOCK_IL" +
                "; initial_vert_height=PQS_RADIUS" +
                "; asl_subtract=PQS_RADIUS" +
                "; allow_negative=false" +
                "; terrain_reference_internal_state=PRODUCTION_PQS_QUERY_OWNED" +
                "; diagnostic_direct_callback_live_mutation=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" + Invariants());
        }

'''
obs = obs[:report_start] + report_new + obs[report_end:]

# Restore the production reference identity after the V4 swapped-input diagnostic.
old_reference = "AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED_SWAPPED_INPUT_DIAGNOSTIC"
new_reference = "AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED"
if obs.count(old_reference) < 1 or run.count(old_reference) < 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 swapped reference marker missing")
obs = obs.replace(old_reference, new_reference)
run = run.replace(old_reference, new_reference)

run = run.replace(
    "terrainaltitude_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC",
    "terrainaltitude_reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL")
run = run.replace(
    "terrainaltitude_u_formula=ATAN2_Z_X_OVER_PI_TIMES_HALF",
    "terrainaltitude_u_formula=STOCK_ASIN_X_WITH_Z_HEMISPHERE_OVER_PI_TIMES_HALF")

prov_old = "terrainaltitude_semantics=RUNTIME_UNIQUE_BODY_RADIUS_OR_PQS_RADIUSMIN_RAW_OR_CLAMP0_CLOSURE"
prov_new = "terrainaltitude_semantics=STOCK_IL_PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO"
if run.count(prov_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 provenance semantics marker not unique")
run = run.replace(prov_old, prov_new, 1)

prov_anchor = "terrainaltitude_production_helper_code_unchanged=true"
prov_il = (
    prov_anchor +
    "\nterrainaltitude_initial_vert_height=PQS_RADIUS" +
    "\nterrainaltitude_asl_subtract=PQS_RADIUS" +
    "\nterrainaltitude_direction_normalization=PQS_GETSURFACEHEIGHT_NORMALIZED" +
    "\nterrainaltitude_il_terrainaltitude_sha256=" + IL_TERRAIN_ALTITUDE +
    "\nterrainaltitude_il_getrelsurfacenvector_sha256=" + IL_GET_REL_SURFACE_N_VECTOR +
    "\nterrainaltitude_il_sphericalvector_sha256=" + IL_SPHERICAL_VECTOR +
    "\nterrainaltitude_il_getsurfaceheight_sha256=" + IL_GET_SURFACE_HEIGHT +
    "\nterrainaltitude_il_buildvertexmapcoords_sha256=" + IL_BUILD_VERTEX_MAP_COORDS +
    "\nterrainaltitude_il_mod_onvertexbuildheight_sha256=" + IL_MOD_ON_VERTEX_BUILD_HEIGHT +
    "\nterrainaltitude_il_vertexbuilddata_reset_sha256=" + IL_VERTEX_BUILD_DATA_RESET)
if run.count(prov_anchor) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 provenance IL anchor not unique")
run = run.replace(prov_anchor, prov_il, 1)

runner_semantics_old = '''  [[ "$terrain_semantics" = "BODY_RADIUS_RAW_ASL" ||
     "$terrain_semantics" = "BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO" ||
     "$terrain_semantics" = "PQS_RADIUSMIN_RAW_ASL" ||
     "$terrain_semantics" = "PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" ]] || pass=0'''
runner_semantics_new = '''  [[ "$terrain_semantics" = "PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO" ]] || pass=0'''
if run.count(runner_semantics_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 runner semantics marker not unique")
run = run.replace(runner_semantics_old, runner_semantics_new, 1)

success_old = '''  echo "AERIS41_R041_TERRAINALTITUDE_ARGUMENT_ORDER_DIAGNOSTIC=PASS"
  echo "terrainaltitude_reference_input_order=LONGITUDE_THEN_LATITUDE_DIAGNOSTIC"
  echo "terrainaltitude_semantics=$terrain_semantics"
  echo "terrainaltitude_total_checks=$terrain_total_checks"
  echo "production_helper_code_unchanged=true"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_REPAIR_TERRAINALTITUDE_ARGUMENT_ORDER"'''
success_new = '''  echo "AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS"
  echo "terrainaltitude_reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL"
  echo "terrainaltitude_semantics=$terrain_semantics"
  echo "terrainaltitude_total_checks=$terrain_total_checks"
  echo "terrainaltitude_initial_vert_height=PQS_RADIUS"
  echo "terrainaltitude_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS"
  echo "production_helper_code_unchanged=true"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_FINAL_ACCEPTANCE_RECORD"'''
if run.count(success_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 success-tail marker not unique")
run = run.replace(success_old, success_new, 1)

# V3 acceptance already requires every body to carry the stock-PQS snapshot markers.
# Add the V5 IL-defined fields to that same per-body gate.
accept_marker = '''    [[ "$terrain_snapshot_line" == *"; synthetic_census_uv_reused=false;"* ]] || pass=0'''
accept_new = accept_marker + '''
    [[ "$terrain_snapshot_line" == *"; terrain_reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL;"* ]] || pass=0
    [[ "$terrain_snapshot_line" == *"; terrain_public_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO;"* ]] || pass=0
    [[ "$terrain_snapshot_line" == *"; pqs_radius="* ]] || pass=0'''
if run.count(accept_marker) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V5 snapshot acceptance marker not unique")
run = run.replace(accept_marker, accept_new, 1)

required_obs_tokens = (
    new_candidate,
    "PqsRadius = pqs.radius",
    "pqsInputDirection.normalized",
    "pqsDirectionXZ.z < 0.0",
    "pqsLongitudeRad / Math.PI * 0.5",
    "TerrainPqsPublicMatches",
    "PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO",
    "initial_vert_height=PQS_RADIUS",
    IL_TERRAIN_ALTITUDE,
    IL_GET_SURFACE_HEIGHT,
    IL_BUILD_VERTEX_MAP_COORDS,
    IL_SPHERICAL_VECTOR,
)
for token in required_obs_tokens:
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude V5 observer lost token: " + token)

required_run_tokens = (
    new_candidate,
    "terrainaltitude_semantics=STOCK_IL_PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO",
    "terrainaltitude_reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL",
    "terrainaltitude_initial_vert_height=PQS_RADIUS",
    "AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS",
    "next=R041_FINAL_ACCEPTANCE_RECORD",
    IL_TERRAIN_ALTITUDE,
    IL_MOD_ON_VERTEX_BUILD_HEIGHT,
)
for token in required_run_tokens:
    if token not in run:
        raise SystemExit("AERIS41 TerrainAltitude V5 runner lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
