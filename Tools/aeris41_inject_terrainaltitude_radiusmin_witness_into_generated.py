#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_radiusmin_witness_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V1"
new_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V2_RADIUSMIN"

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V1 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

body_case_old = '''            internal ExpectedCheck[] Checks;
            internal double Radius;
            internal TerrainAltitudeCheck[] TerrainChecks;
        }'''
body_case_new = '''            internal ExpectedCheck[] Checks;
            internal double Radius;
            internal double RadiusMin;
            internal TerrainAltitudeCheck[] TerrainChecks;
        }'''
if obs.count(body_case_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 BodyCase marker not unique")
obs = obs.replace(body_case_old, body_case_new, 1)

body_result_old = '''            internal int TerrainRawMatches;
            internal int TerrainClampZeroMatches;
            internal double TerrainRawMaxError;
            internal double TerrainClampZeroMaxError;
            internal string[] TerrainFirstMismatches;
        }'''
body_result_new = '''            internal int TerrainRawMatches;
            internal int TerrainClampZeroMatches;
            internal double TerrainRawMaxError;
            internal double TerrainClampZeroMaxError;
            internal int TerrainRadiusMinRawMatches;
            internal int TerrainRadiusMinClampZeroMatches;
            internal double TerrainRadiusMinRawMaxError;
            internal double TerrainRadiusMinClampZeroMaxError;
            internal string[] TerrainFirstMismatches;
        }'''
if obs.count(body_result_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 BodyResult marker not unique")
obs = obs.replace(body_result_old, body_result_new, 1)

snapshot_old = '''                "; terrain_tolerance_m=1E-08" +
                "; semantics_candidates=RAW_CHAIN_ASL,CLAMP_NEGATIVE_TO_ZERO" +'''
snapshot_new = '''                "; terrain_tolerance_m=1E-08" +
                "; body_radius=" + R(body.Radius) +
                "; pqs_radius_min=" + R(radiusMin) +
                "; radius_minus_radius_min=" + R(body.Radius - radiusMin) +
                "; semantics_candidates=BODY_RADIUS_RAW_ASL,BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO,PQS_RADIUSMIN_RAW_ASL,PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" +'''
if obs.count(snapshot_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 snapshot marker not unique")
obs = obs.replace(snapshot_old, snapshot_new, 1)

return_old = '''                Checks = checks.ToArray(),
                Radius = body.Radius,
                TerrainChecks = terrainChecks.ToArray()
            };'''
return_new = '''                Checks = checks.ToArray(),
                Radius = body.Radius,
                RadiusMin = radiusMin,
                TerrainChecks = terrainChecks.ToArray()
            };'''
if obs.count(return_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 BodyCase return marker not unique")
obs = obs.replace(return_old, return_new, 1)

eval_old = r'''                double pureAbsolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                    body.Snapshot,
                    expectedTerrain.X,
                    expectedTerrain.Y,
                    expectedTerrain.Z,
                    expectedTerrain.U,
                    expectedTerrain.V,
                    body.Radius);
                double rawAsl = pureAbsolute - body.Radius;
                double clampZeroAsl = rawAsl < 0.0 ? 0.0 : rawAsl;
                double rawError = Math.Abs(rawAsl - expectedTerrain.ExpectedAsl);
                double clampError = Math.Abs(clampZeroAsl - expectedTerrain.ExpectedAsl);

                if (rawError > result.TerrainRawMaxError)
                    result.TerrainRawMaxError = rawError;
                if (clampError > result.TerrainClampZeroMaxError)
                    result.TerrainClampZeroMaxError = clampError;
                if (rawError <= TerrainToleranceMeters)
                    result.TerrainRawMatches++;
                if (clampError <= TerrainToleranceMeters)
                    result.TerrainClampZeroMatches++;

                if (rawError > TerrainToleranceMeters &&
                    clampError > TerrainToleranceMeters &&
                    terrainMismatches.Count < 12)
                {
                    terrainMismatches.Add(
                        expectedTerrain.Label +
                        " lat=" + expectedTerrain.Latitude.ToString("R", CultureInfo.InvariantCulture) +
                        " lon=" + expectedTerrain.Longitude.ToString("R", CultureInfo.InvariantCulture) +
                        " pqs=" + expectedTerrain.ExpectedAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " raw=" + rawAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " clamp0=" + clampZeroAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " raw_error=" + rawError.ToString("R", CultureInfo.InvariantCulture) +
                        " clamp0_error=" + clampError.ToString("R", CultureInfo.InvariantCulture));
                }'''
eval_new = r'''                double bodyRadiusAbsolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                    body.Snapshot,
                    expectedTerrain.X,
                    expectedTerrain.Y,
                    expectedTerrain.Z,
                    expectedTerrain.U,
                    expectedTerrain.V,
                    body.Radius);
                double bodyRadiusRawAsl = bodyRadiusAbsolute - body.Radius;
                double bodyRadiusClampZeroAsl = bodyRadiusRawAsl < 0.0 ? 0.0 : bodyRadiusRawAsl;
                double bodyRadiusRawError = Math.Abs(
                    bodyRadiusRawAsl - expectedTerrain.ExpectedAsl);
                double bodyRadiusClampError = Math.Abs(
                    bodyRadiusClampZeroAsl - expectedTerrain.ExpectedAsl);

                double radiusMinAbsolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                    body.Snapshot,
                    expectedTerrain.X,
                    expectedTerrain.Y,
                    expectedTerrain.Z,
                    expectedTerrain.U,
                    expectedTerrain.V,
                    body.RadiusMin);
                double radiusMinRawAsl = radiusMinAbsolute - body.Radius;
                double radiusMinClampZeroAsl = radiusMinRawAsl < 0.0 ? 0.0 : radiusMinRawAsl;
                double radiusMinRawError = Math.Abs(
                    radiusMinRawAsl - expectedTerrain.ExpectedAsl);
                double radiusMinClampError = Math.Abs(
                    radiusMinClampZeroAsl - expectedTerrain.ExpectedAsl);

                if (bodyRadiusRawError > result.TerrainRawMaxError)
                    result.TerrainRawMaxError = bodyRadiusRawError;
                if (bodyRadiusClampError > result.TerrainClampZeroMaxError)
                    result.TerrainClampZeroMaxError = bodyRadiusClampError;
                if (radiusMinRawError > result.TerrainRadiusMinRawMaxError)
                    result.TerrainRadiusMinRawMaxError = radiusMinRawError;
                if (radiusMinClampError > result.TerrainRadiusMinClampZeroMaxError)
                    result.TerrainRadiusMinClampZeroMaxError = radiusMinClampError;

                if (bodyRadiusRawError <= TerrainToleranceMeters)
                    result.TerrainRawMatches++;
                if (bodyRadiusClampError <= TerrainToleranceMeters)
                    result.TerrainClampZeroMatches++;
                if (radiusMinRawError <= TerrainToleranceMeters)
                    result.TerrainRadiusMinRawMatches++;
                if (radiusMinClampError <= TerrainToleranceMeters)
                    result.TerrainRadiusMinClampZeroMatches++;

                if (bodyRadiusRawError > TerrainToleranceMeters &&
                    bodyRadiusClampError > TerrainToleranceMeters &&
                    radiusMinRawError > TerrainToleranceMeters &&
                    radiusMinClampError > TerrainToleranceMeters &&
                    terrainMismatches.Count < 12)
                {
                    terrainMismatches.Add(
                        expectedTerrain.Label +
                        " lat=" + expectedTerrain.Latitude.ToString("R", CultureInfo.InvariantCulture) +
                        " lon=" + expectedTerrain.Longitude.ToString("R", CultureInfo.InvariantCulture) +
                        " pqs=" + expectedTerrain.ExpectedAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " body_radius=" + body.Radius.ToString("R", CultureInfo.InvariantCulture) +
                        " radius_min=" + body.RadiusMin.ToString("R", CultureInfo.InvariantCulture) +
                        " body_raw=" + bodyRadiusRawAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " body_clamp0=" + bodyRadiusClampZeroAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " rmin_raw=" + radiusMinRawAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " rmin_clamp0=" + radiusMinClampZeroAsl.ToString("R", CultureInfo.InvariantCulture) +
                        " body_raw_error=" + bodyRadiusRawError.ToString("R", CultureInfo.InvariantCulture) +
                        " body_clamp0_error=" + bodyRadiusClampError.ToString("R", CultureInfo.InvariantCulture) +
                        " rmin_raw_error=" + radiusMinRawError.ToString("R", CultureInfo.InvariantCulture) +
                        " rmin_clamp0_error=" + radiusMinClampError.ToString("R", CultureInfo.InvariantCulture));
                }'''
if obs.count(eval_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 worker marker not unique")
obs = obs.replace(eval_old, eval_new, 1)

report_vars_old = '''            bool terrainRawGlobal = workerNotMain;
            bool terrainClampZeroGlobal = workerNotMain;
            int terrainChecks = 0;
            int terrainReferenceValues = 0;
            int terrainRawMatches = 0;
            int terrainClampZeroMatches = 0;
            double terrainRawMaxError = 0.0;
            double terrainClampZeroMaxError = 0.0;'''
report_vars_new = '''            bool terrainRawGlobal = workerNotMain;
            bool terrainClampZeroGlobal = workerNotMain;
            bool terrainRadiusMinRawGlobal = workerNotMain;
            bool terrainRadiusMinClampZeroGlobal = workerNotMain;
            int terrainChecks = 0;
            int terrainReferenceValues = 0;
            int terrainRawMatches = 0;
            int terrainClampZeroMatches = 0;
            int terrainRadiusMinRawMatches = 0;
            int terrainRadiusMinClampZeroMatches = 0;
            double terrainRawMaxError = 0.0;
            double terrainClampZeroMaxError = 0.0;
            double terrainRadiusMinRawMaxError = 0.0;
            double terrainRadiusMinClampZeroMaxError = 0.0;'''
if obs.count(report_vars_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 report vars marker not unique")
obs = obs.replace(report_vars_old, report_vars_new, 1)

null_old = '''                    terrainRawGlobal = false;
                    terrainClampZeroGlobal = false;
                    continue;'''
null_new = '''                    terrainRawGlobal = false;
                    terrainClampZeroGlobal = false;
                    terrainRadiusMinRawGlobal = false;
                    terrainRadiusMinClampZeroGlobal = false;
                    continue;'''
if obs.count(null_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 null-body marker not unique")
obs = obs.replace(null_old, null_new, 1)

body_report_old = r'''                bool bodyRaw = bodyReferenceComplete &&
                    body.TerrainRawMatches == body.TerrainChecks;
                bool bodyClampZero = bodyReferenceComplete &&
                    body.TerrainClampZeroMatches == body.TerrainChecks;
                terrainRawGlobal &= bodyRaw;
                terrainClampZeroGlobal &= bodyClampZero;
                terrainChecks += body.TerrainChecks;
                terrainReferenceValues += body.TerrainReferenceValues;
                terrainRawMatches += body.TerrainRawMatches;
                terrainClampZeroMatches += body.TerrainClampZeroMatches;
                terrainRawMaxError = Math.Max(terrainRawMaxError, body.TerrainRawMaxError);
                terrainClampZeroMaxError = Math.Max(
                    terrainClampZeroMaxError, body.TerrainClampZeroMaxError);

                string bodySemantics = bodyRaw && !bodyClampZero
                    ? "RAW_CHAIN_ASL"
                    : (bodyClampZero && !bodyRaw
                        ? "CLAMP_NEGATIVE_TO_ZERO"
                        : (bodyRaw && bodyClampZero ? "AMBIGUOUS" : "NONE"));'''
body_report_new = r'''                bool bodyRaw = bodyReferenceComplete &&
                    body.TerrainRawMatches == body.TerrainChecks;
                bool bodyClampZero = bodyReferenceComplete &&
                    body.TerrainClampZeroMatches == body.TerrainChecks;
                bool bodyRadiusMinRaw = bodyReferenceComplete &&
                    body.TerrainRadiusMinRawMatches == body.TerrainChecks;
                bool bodyRadiusMinClampZero = bodyReferenceComplete &&
                    body.TerrainRadiusMinClampZeroMatches == body.TerrainChecks;
                terrainRawGlobal &= bodyRaw;
                terrainClampZeroGlobal &= bodyClampZero;
                terrainRadiusMinRawGlobal &= bodyRadiusMinRaw;
                terrainRadiusMinClampZeroGlobal &= bodyRadiusMinClampZero;
                terrainChecks += body.TerrainChecks;
                terrainReferenceValues += body.TerrainReferenceValues;
                terrainRawMatches += body.TerrainRawMatches;
                terrainClampZeroMatches += body.TerrainClampZeroMatches;
                terrainRadiusMinRawMatches += body.TerrainRadiusMinRawMatches;
                terrainRadiusMinClampZeroMatches += body.TerrainRadiusMinClampZeroMatches;
                terrainRawMaxError = Math.Max(terrainRawMaxError, body.TerrainRawMaxError);
                terrainClampZeroMaxError = Math.Max(
                    terrainClampZeroMaxError, body.TerrainClampZeroMaxError);
                terrainRadiusMinRawMaxError = Math.Max(
                    terrainRadiusMinRawMaxError, body.TerrainRadiusMinRawMaxError);
                terrainRadiusMinClampZeroMaxError = Math.Max(
                    terrainRadiusMinClampZeroMaxError,
                    body.TerrainRadiusMinClampZeroMaxError);

                int bodyPassingSemantics =
                    (bodyRaw ? 1 : 0) +
                    (bodyClampZero ? 1 : 0) +
                    (bodyRadiusMinRaw ? 1 : 0) +
                    (bodyRadiusMinClampZero ? 1 : 0);
                string bodySemantics = bodyPassingSemantics == 1
                    ? (bodyRaw
                        ? "BODY_RADIUS_RAW_ASL"
                        : (bodyClampZero
                            ? "BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO"
                            : (bodyRadiusMinRaw
                                ? "PQS_RADIUSMIN_RAW_ASL"
                                : "PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO")))
                    : (bodyPassingSemantics > 1 ? "AMBIGUOUS" : "NONE");'''
if obs.count(body_report_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 body report marker not unique")
obs = obs.replace(body_report_old, body_report_new, 1)

body_log_old = '''                    "; raw_matches=" + body.TerrainRawMatches.ToString(CultureInfo.InvariantCulture) +
                    "; clamp0_matches=" + body.TerrainClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                    "; raw_max_error_m=" + R(body.TerrainRawMaxError) +
                    "; clamp0_max_error_m=" + R(body.TerrainClampZeroMaxError) +
                    "; body_semantics=" + bodySemantics +'''
body_log_new = '''                    "; body_radius_raw_matches=" + body.TerrainRawMatches.ToString(CultureInfo.InvariantCulture) +
                    "; body_radius_clamp0_matches=" + body.TerrainClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                    "; radiusmin_raw_matches=" + body.TerrainRadiusMinRawMatches.ToString(CultureInfo.InvariantCulture) +
                    "; radiusmin_clamp0_matches=" + body.TerrainRadiusMinClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                    "; body_radius_raw_max_error_m=" + R(body.TerrainRawMaxError) +
                    "; body_radius_clamp0_max_error_m=" + R(body.TerrainClampZeroMaxError) +
                    "; radiusmin_raw_max_error_m=" + R(body.TerrainRadiusMinRawMaxError) +
                    "; radiusmin_clamp0_max_error_m=" + R(body.TerrainRadiusMinClampZeroMaxError) +
                    "; body_semantics=" + bodySemantics +'''
if obs.count(body_log_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 body log marker not unique")
obs = obs.replace(body_log_old, body_log_new, 1)

complete_select_old = r'''            bool uniqueSemantics = terrainRawGlobal != terrainClampZeroGlobal;
            bool terrainPass =
                uniqueSemantics &&
                terrainChecks > 0 &&
                terrainReferenceValues == terrainChecks;
            string selectedSemantics = terrainRawGlobal && !terrainClampZeroGlobal
                ? "RAW_CHAIN_ASL"
                : (terrainClampZeroGlobal && !terrainRawGlobal
                    ? "CLAMP_NEGATIVE_TO_ZERO"
                    : (terrainRawGlobal && terrainClampZeroGlobal ? "AMBIGUOUS" : "NONE"));'''
complete_select_new = r'''            int globalPassingSemantics =
                (terrainRawGlobal ? 1 : 0) +
                (terrainClampZeroGlobal ? 1 : 0) +
                (terrainRadiusMinRawGlobal ? 1 : 0) +
                (terrainRadiusMinClampZeroGlobal ? 1 : 0);
            bool uniqueSemantics = globalPassingSemantics == 1;
            bool terrainPass =
                uniqueSemantics &&
                terrainChecks > 0 &&
                terrainReferenceValues == terrainChecks;
            string selectedSemantics = uniqueSemantics
                ? (terrainRawGlobal
                    ? "BODY_RADIUS_RAW_ASL"
                    : (terrainClampZeroGlobal
                        ? "BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO"
                        : (terrainRadiusMinRawGlobal
                            ? "PQS_RADIUSMIN_RAW_ASL"
                            : "PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO")))
                : (globalPassingSemantics > 1 ? "AMBIGUOUS" : "NONE");'''
if obs.count(complete_select_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 complete selection marker not unique")
obs = obs.replace(complete_select_old, complete_select_new, 1)

complete_log_old = '''                "; raw_matches=" + terrainRawMatches.ToString(CultureInfo.InvariantCulture) +
                "; clamp0_matches=" + terrainClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                "; raw_global_pass=" + Bool(terrainRawGlobal) +
                "; clamp0_global_pass=" + Bool(terrainClampZeroGlobal) +
                "; unique_semantics=" + Bool(uniqueSemantics) +
                "; selected_semantics=" + selectedSemantics +
                "; raw_max_error_m=" + R(terrainRawMaxError) +
                "; clamp0_max_error_m=" + R(terrainClampZeroMaxError) +'''
complete_log_new = '''                "; body_radius_raw_matches=" + terrainRawMatches.ToString(CultureInfo.InvariantCulture) +
                "; body_radius_clamp0_matches=" + terrainClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                "; radiusmin_raw_matches=" + terrainRadiusMinRawMatches.ToString(CultureInfo.InvariantCulture) +
                "; radiusmin_clamp0_matches=" + terrainRadiusMinClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                "; body_radius_raw_global_pass=" + Bool(terrainRawGlobal) +
                "; body_radius_clamp0_global_pass=" + Bool(terrainClampZeroGlobal) +
                "; radiusmin_raw_global_pass=" + Bool(terrainRadiusMinRawGlobal) +
                "; radiusmin_clamp0_global_pass=" + Bool(terrainRadiusMinClampZeroGlobal) +
                "; unique_semantics=" + Bool(uniqueSemantics) +
                "; selected_semantics=" + selectedSemantics +
                "; body_radius_raw_max_error_m=" + R(terrainRawMaxError) +
                "; body_radius_clamp0_max_error_m=" + R(terrainClampZeroMaxError) +
                "; radiusmin_raw_max_error_m=" + R(terrainRadiusMinRawMaxError) +
                "; radiusmin_clamp0_max_error_m=" + R(terrainRadiusMinClampZeroMaxError) +'''
if obs.count(complete_log_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 complete log marker not unique")
obs = obs.replace(complete_log_old, complete_log_new, 1)

provenance_old = "terrainaltitude_semantics=RUNTIME_UNIQUE_RAW_OR_CLAMP0_CLOSURE"
provenance_new = "terrainaltitude_semantics=RUNTIME_UNIQUE_BODY_RADIUS_OR_PQS_RADIUSMIN_RAW_OR_CLAMP0_CLOSURE"
if run.count(provenance_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 provenance marker not unique")
run = run.replace(provenance_old, provenance_new, 1)

runner_semantics_old = '''  [[ "$terrain_semantics" = "RAW_CHAIN_ASL" || "$terrain_semantics" = "CLAMP_NEGATIVE_TO_ZERO" ]] || pass=0'''
runner_semantics_new = '''  [[ "$terrain_semantics" = "BODY_RADIUS_RAW_ASL" ||
     "$terrain_semantics" = "BODY_RADIUS_CLAMP_NEGATIVE_TO_ZERO" ||
     "$terrain_semantics" = "PQS_RADIUSMIN_RAW_ASL" ||
     "$terrain_semantics" = "PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO" ]] || pass=0'''
if run.count(runner_semantics_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude V2 runner semantics marker not unique")
run = run.replace(runner_semantics_old, runner_semantics_new, 1)

# Do not hide public-PQS witness evidence on failure again.
terminal_old = '''    grep '\\[AERIS39\\]\\[HEIGHT_CHAIN_' "$segment" || true'''
terminal_new = '''    grep -E '\\[AERIS39\\]\\[HEIGHT_CHAIN_|\\[AERIS41\\]\\[TERRAINALTITUDE_' "$segment" || true'''
terminal_count = run.count(terminal_old)
if terminal_count < 2:
    raise SystemExit(
        "AERIS41 TerrainAltitude V2 terminal failure markers too few: " +
        str(terminal_count))
run = run.replace(terminal_old, terminal_new)

for token in (
    new_candidate,
    "RadiusMin",
    "radius_minus_radius_min=",
    "PQS_RADIUSMIN_RAW_ASL",
    "PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO",
    "TerrainRadiusMinRawMatches",
    "radiusmin_raw_global_pass=",
):
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude V2 observer lost token: " + token)

for token in (
    new_candidate,
    "RUNTIME_UNIQUE_BODY_RADIUS_OR_PQS_RADIUSMIN_RAW_OR_CLAMP0_CLOSURE",
    "PQS_RADIUSMIN_RAW_ASL",
    "PQS_RADIUSMIN_CLAMP_NEGATIVE_TO_ZERO",
):
    if token not in run:
        raise SystemExit("AERIS41 TerrainAltitude V2 runner lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
