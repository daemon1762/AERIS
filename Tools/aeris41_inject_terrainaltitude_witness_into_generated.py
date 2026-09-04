#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_terrainaltitude_witness_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V5_CURVE2_EXACT_REPAIR"
new_candidate = "AERIS39_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS_V1"
terrain_tolerance = "1E-08"

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 TerrainAltitude observer V5 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

# Primitive-only public-PQS reference payload. ExpectedAsl is captured on the
# main thread through the exact AERIS production terrain-sampling entry point.
expected_marker = '''        sealed class ModRecord'''
terrain_class = r'''        sealed class TerrainAltitudeCheck
        {
            internal string Label;
            internal double U;
            internal double V;
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
            internal bool HasValue;
            internal double ExpectedAsl;
        }

'''
if obs.count(expected_marker) != 1:
    raise SystemExit("AERIS41 TerrainAltitude check-class marker not unique")
obs = obs.replace(expected_marker, terrain_class + expected_marker, 1)

body_case_old = '''            internal bool CurveDependenciesExact;
            internal AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot Snapshot;
            internal ExpectedCheck[] Checks;
        }'''
body_case_new = '''            internal bool CurveDependenciesExact;
            internal AERIS39AllBodyHeightModifierChainPureCpuExact.ChainSnapshot Snapshot;
            internal ExpectedCheck[] Checks;
            internal double Radius;
            internal TerrainAltitudeCheck[] TerrainChecks;
        }'''
if obs.count(body_case_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude BodyCase marker not unique")
obs = obs.replace(body_case_old, body_case_new, 1)

body_result_old = '''            internal int Mismatches;
            internal string[] FirstMismatches;
            internal bool Pass;
        }'''
body_result_new = '''            internal int Mismatches;
            internal string[] FirstMismatches;
            internal bool Pass;

            internal int TerrainChecks;
            internal int TerrainReferenceValues;
            internal int TerrainRawMatches;
            internal int TerrainClampZeroMatches;
            internal double TerrainRawMaxError;
            internal double TerrainClampZeroMaxError;
            internal string[] TerrainFirstMismatches;
        }'''
if obs.count(body_result_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude BodyResult marker not unique")
obs = obs.replace(body_result_old, body_result_new, 1)

# All direct-callback managed-shadow audits must complete before invoking the
# real public PQS TerrainAltitude path. TerrainAltitude is a normal production
# PQS query and may use PQS-owned transient scratch state; we do not mislabel it
# as a mutation-free diagnostic callback. Only copied scalar values cross to the
# worker.
capture_marker = '''            AuditLandControlReferenceIsolation(bodyName, mods);
            AuditCurve2ReferenceIsolation(bodyName, mods);
            AuditHeightNoiseReferenceIsolation(bodyName, mods);

            string topologyText = string.Join(",", topology.ToArray());'''
capture_replacement = r'''            AuditLandControlReferenceIsolation(bodyName, mods);
            AuditCurve2ReferenceIsolation(bodyName, mods);
            AuditHeightNoiseReferenceIsolation(bodyName, mods);

            var terrainChecks = new List<TerrainAltitudeCheck>(coords.Count);
            for (int tc = 0; tc < coords.Count; tc++)
            {
                CoordinateSample coord = coords[tc];
                // Public TerrainAltitude takes geodetic degrees. The height-chain
                // census also contains deliberate MapSO periodic/out-of-domain
                // probes; exclude those from this public-PQS integration witness.
                if (coord.Latitude < -90.0 || coord.Latitude > 90.0 ||
                    coord.Longitude < -180.0 || coord.Longitude > 180.0)
                    continue;

                var terrainCheck = new TerrainAltitudeCheck
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

                double expectedAsl;
                terrainCheck.HasValue = AERISTerrainAwareness.TrySampleTerrainAslShared(
                    body, coord.Latitude, coord.Longitude, out expectedAsl);
                terrainCheck.ExpectedAsl = expectedAsl;
                terrainChecks.Add(terrainCheck);
            }

            if (terrainChecks.Count < 500)
                throw new InvalidOperationException(
                    bodyName + "_TERRAINALTITUDE_VALID_SAMPLE_COUNT_TOO_LOW_" +
                    terrainChecks.Count.ToString(CultureInfo.InvariantCulture));

            AERISLogger.Info(
                "[AERIS41][TERRAINALTITUDE_SNAPSHOT]" +
                "; candidate=" + Candidate +
                "; body=" + Safe(bodyName) +
                "; valid_geodetic_samples=" + terrainChecks.Count.ToString(CultureInfo.InvariantCulture) +
                "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                "; reference_thread=MAIN_THREAD_ONLY" +
                "; allow_negative=false" +
                "; terrain_tolerance_m=1E-08" +
                "; semantics_candidates=RAW_CHAIN_ASL,CLAMP_NEGATIVE_TO_ZERO" +
                "; direct_callback_audits_completed_before_public_pqs_query=true" +
                "; terrain_reference_internal_state=PRODUCTION_PQS_QUERY_OWNED" +
                "; snapshot_payload=PRIMITIVES_ONLY" + Invariants());

            string topologyText = string.Join(",", topology.ToArray());'''
if obs.count(capture_marker) != 1:
    raise SystemExit("AERIS41 TerrainAltitude capture marker not unique")
obs = obs.replace(capture_marker, capture_replacement, 1)

return_old = '''                CurveDependenciesExact = curveExact,
                Snapshot = chain,
                Checks = checks.ToArray()
            };'''
return_new = '''                CurveDependenciesExact = curveExact,
                Snapshot = chain,
                Checks = checks.ToArray(),
                Radius = body.Radius,
                TerrainChecks = terrainChecks.ToArray()
            };'''
if obs.count(return_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude BodyCase return marker not unique")
obs = obs.replace(return_old, return_new, 1)

# Worker evaluates the already-certified pure chain from the physical body
# radius, then compares two explicit public-ASL semantics. No Unity/KSP/runtime
# object is present in this loop.
eval_marker = '''            result.FirstMismatches = mismatches.ToArray();'''
eval_insert = r'''            var terrainMismatches = new List<string>();
            const double TerrainToleranceMeters = 1E-08;
            if (body.TerrainChecks == null || body.TerrainChecks.Length == 0)
                throw new InvalidOperationException(body.Name + "_TERRAINALTITUDE_CHECKS_MISSING");

            for (int ti = 0; ti < body.TerrainChecks.Length; ti++)
            {
                TerrainAltitudeCheck expectedTerrain = body.TerrainChecks[ti];
                result.TerrainChecks++;
                if (!expectedTerrain.HasValue)
                {
                    if (terrainMismatches.Count < 12)
                        terrainMismatches.Add(expectedTerrain.Label + " reference=UNAVAILABLE");
                    continue;
                }

                result.TerrainReferenceValues++;
                double pureAbsolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
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
                }
            }
            result.TerrainFirstMismatches = terrainMismatches.ToArray();

'''
if obs.count(eval_marker) != 1:
    raise SystemExit("AERIS41 TerrainAltitude worker insertion marker not unique")
obs = obs.replace(eval_marker, eval_insert + eval_marker, 1)

# Keep HEIGHT_CHAIN_COMPLETE as the immutable bit-exact callback-chain result.
# Emit a separate public-PQS integration verdict. A single semantics must close
# every reference value on all six bodies; ambiguity is deliberately fail-closed.
report_tail = '''                Invariants());
        }

        CurveSelection SelectCurveSnapshot'''
terrain_report = r'''                Invariants());

            bool terrainRawGlobal = workerNotMain;
            bool terrainClampZeroGlobal = workerNotMain;
            int terrainChecks = 0;
            int terrainReferenceValues = 0;
            int terrainRawMatches = 0;
            int terrainClampZeroMatches = 0;
            double terrainRawMaxError = 0.0;
            double terrainClampZeroMaxError = 0.0;

            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult body = result.Bodies[i];
                if (body == null)
                {
                    terrainRawGlobal = false;
                    terrainClampZeroGlobal = false;
                    continue;
                }

                bool bodyReferenceComplete =
                    body.TerrainChecks > 0 &&
                    body.TerrainReferenceValues == body.TerrainChecks;
                bool bodyRaw = bodyReferenceComplete &&
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
                        : (bodyRaw && bodyClampZero ? "AMBIGUOUS" : "NONE"));

                AERISLogger.Info(
                    "[AERIS41][TERRAINALTITUDE_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(body.Name) +
                    "; checks=" + body.TerrainChecks.ToString(CultureInfo.InvariantCulture) +
                    "; reference_values=" + body.TerrainReferenceValues.ToString(CultureInfo.InvariantCulture) +
                    "; raw_matches=" + body.TerrainRawMatches.ToString(CultureInfo.InvariantCulture) +
                    "; clamp0_matches=" + body.TerrainClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                    "; raw_max_error_m=" + R(body.TerrainRawMaxError) +
                    "; clamp0_max_error_m=" + R(body.TerrainClampZeroMaxError) +
                    "; body_semantics=" + bodySemantics +
                    "; terrain_tolerance_m=1E-08" +
                    "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                    "; reference_thread=MAIN_THREAD_ONLY" +
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

            bool uniqueSemantics = terrainRawGlobal != terrainClampZeroGlobal;
            bool terrainPass =
                uniqueSemantics &&
                terrainChecks > 0 &&
                terrainReferenceValues == terrainChecks;
            string selectedSemantics = terrainRawGlobal && !terrainClampZeroGlobal
                ? "RAW_CHAIN_ASL"
                : (terrainClampZeroGlobal && !terrainRawGlobal
                    ? "CLAMP_NEGATIVE_TO_ZERO"
                    : (terrainRawGlobal && terrainClampZeroGlobal ? "AMBIGUOUS" : "NONE"));

            AERISLogger.Info(
                "[AERIS41][TERRAINALTITUDE_COMPLETE]" +
                "; pass=" + Bool(terrainPass) +
                "; candidate=" + Candidate +
                "; bodies=" + result.Bodies.Length.ToString(CultureInfo.InvariantCulture) +
                "; total_checks=" + terrainChecks.ToString(CultureInfo.InvariantCulture) +
                "; reference_values=" + terrainReferenceValues.ToString(CultureInfo.InvariantCulture) +
                "; raw_matches=" + terrainRawMatches.ToString(CultureInfo.InvariantCulture) +
                "; clamp0_matches=" + terrainClampZeroMatches.ToString(CultureInfo.InvariantCulture) +
                "; raw_global_pass=" + Bool(terrainRawGlobal) +
                "; clamp0_global_pass=" + Bool(terrainClampZeroGlobal) +
                "; unique_semantics=" + Bool(uniqueSemantics) +
                "; selected_semantics=" + selectedSemantics +
                "; raw_max_error_m=" + R(terrainRawMaxError) +
                "; clamp0_max_error_m=" + R(terrainClampZeroMaxError) +
                "; terrain_tolerance_m=1E-08" +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                "; reference_thread=MAIN_THREAD_ONLY" +
                "; allow_negative=false" +
                "; terrain_reference_internal_state=PRODUCTION_PQS_QUERY_OWNED" +
                "; diagnostic_direct_callback_live_mutation=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" + Invariants());
        }

        CurveSelection SelectCurveSnapshot'''
if obs.count(report_tail) != 1:
    raise SystemExit("AERIS41 TerrainAltitude report-tail marker not unique")
obs = obs.replace(report_tail, terrain_report, 1)

# Preserve both families of evidence in the artifact excerpt.
grep_old = '''  grep '\\[AERIS39\\]\\[HEIGHT_CHAIN_' "$segment" > "$OUT/height_chain_runtime_excerpt.txt" || true'''
grep_new = '''  grep -E '\\[AERIS39\\]\\[HEIGHT_CHAIN_|\\[AERIS41\\]\\[TERRAINALTITUDE_' "$segment" > "$OUT/height_chain_runtime_excerpt.txt" || true'''
if run.count(grep_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude artifact-grep marker not unique")
run = run.replace(grep_old, grep_new, 1)

provenance_marker = "production_curve2_state_audit=REQUIRED_UNCHANGED"
provenance_new = provenance_marker + (
    "\nterrainaltitude_reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED"
    "\nterrainaltitude_reference_thread=MAIN_THREAD_ONLY"
    "\nterrainaltitude_allow_negative=false"
    "\nterrainaltitude_tolerance_m=" + terrain_tolerance +
    "\nterrainaltitude_semantics=RUNTIME_UNIQUE_RAW_OR_CLAMP0_CLOSURE")
if run.count(provenance_marker) != 1:
    raise SystemExit("AERIS41 TerrainAltitude provenance marker not unique")
run = run.replace(provenance_marker, provenance_new, 1)

# The generated runner already validates the 16950 bit-exact chain and all
# managed-shadow audits. Add the public-PQS witness as an additional gate.
accept_marker = '''  write_artifacts "$segment" "$([[ "$pass" -eq 1 ]] && echo PASS || echo FAIL_ACCEPTANCE)" "$installed_sha"'''
accept_insert = r'''  local terrain_complete terrain_total_checks terrain_semantics
  terrain_complete="$(grep -F "[AERIS41][TERRAINALTITUDE_COMPLETE]" "$segment" | tail -n 1 || true)"
  [[ -n "$terrain_complete" ]] || pass=0
  [[ "$terrain_complete" == *"; pass=true;"* ]] || pass=0
  [[ "$terrain_complete" == *"; candidate=$CANDIDATE;"* ]] || pass=0
  [[ "$terrain_complete" == *"; bodies=6;"* ]] || pass=0
  [[ "$terrain_complete" == *"; unique_semantics=true;"* ]] || pass=0
  [[ "$terrain_complete" == *"; worker_not_main=true;"* ]] || pass=0
  [[ "$terrain_complete" == *"; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED;"* ]] || pass=0
  [[ "$terrain_complete" == *"; reference_thread=MAIN_THREAD_ONLY;"* ]] || pass=0
  [[ "$terrain_complete" == *"; allow_negative=false;"* ]] || pass=0
  [[ "$terrain_complete" == *"; terrain_tolerance_m=1E-08;"* ]] || pass=0
  [[ "$terrain_complete" == *"; snapshot_payload=PRIMITIVES_ONLY;"* ]] || pass=0
  terrain_total_checks="$(printf '%s\n' "$terrain_complete" | sed -n 's/.*; total_checks=\\([0-9][0-9]*\\);.*/\\1/p')"
  [[ -n "$terrain_total_checks" && "$terrain_total_checks" -ge 3000 ]] || pass=0
  terrain_semantics="$(printf '%s\n' "$terrain_complete" | sed -n 's/.*; selected_semantics=\\([^;]*\\);.*/\\1/p')"
  [[ "$terrain_semantics" = "RAW_CHAIN_ASL" || "$terrain_semantics" = "CLAMP_NEGATIVE_TO_ZERO" ]] || pass=0
  for terrain_body in Kerbin Eve Duna Dres Moho Eeloo; do
    local terrain_body_line
    terrain_body_line="$(grep -F "[AERIS41][TERRAINALTITUDE_BODY]" "$segment" | grep -F "; body=$terrain_body;" | tail -n 1 || true)"
    [[ -n "$terrain_body_line" ]] || pass=0
  done

'''
if run.count(accept_marker) != 1:
    raise SystemExit("AERIS41 TerrainAltitude acceptance insertion marker not unique")
run = run.replace(accept_marker, accept_insert + accept_marker, 1)

# Add explicit stage success after the inherited exact-chain success lines.
success_old = '''  echo "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW=PASS"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS"'''
success_new = '''  echo "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW=PASS"
  echo "AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS"
  echo "terrainaltitude_semantics=$terrain_semantics"
  echo "terrainaltitude_total_checks=$terrain_total_checks"
  echo "AERIS_CURRENT_STAGE=PASS"
  echo "next=R041_POST_TERRAINALTITUDE_STAGE_PENDING"'''
if run.count(success_old) != 1:
    raise SystemExit("AERIS41 TerrainAltitude success-tail marker not unique")
run = run.replace(success_old, success_new, 1)

for token in (
    new_candidate,
    "TerrainAltitudeCheck",
    "AERISTerrainAwareness.TrySampleTerrainAslShared",
    "TERRAINALTITUDE_SNAPSHOT",
    "TERRAINALTITUDE_BODY",
    "TERRAINALTITUDE_COMPLETE",
    "unique_semantics=",
    "PRODUCTION_PQS_QUERY_OWNED",
):
    if token not in obs:
        raise SystemExit("AERIS41 TerrainAltitude generated observer lost token: " + token)

for token in (
    new_candidate,
    "terrainaltitude_reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED",
    "TERRAINALTITUDE_COMPLETE",
    "AERIS41_R041_ALLBODY_PQS_TERRAINALTITUDE_WITNESS=PASS",
):
    if token not in run:
        raise SystemExit("AERIS41 TerrainAltitude generated runner lost token: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
