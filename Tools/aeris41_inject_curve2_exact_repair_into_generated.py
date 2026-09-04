#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_curve2_exact_repair_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V4"
new_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V5_CURVE2_EXACT_REPAIR"
old_reference = (
    "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN_"
    "LANDCONTROL_HEIGHTNOISE_MANAGED_SHADOW")
new_reference = (
    "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN_"
    "LANDCONTROL_CURVE2_HEIGHTNOISE_MANAGED_SHADOW")
callback_il_sha = "6c68df85bb2f8d294c4df5299d05c893ac3edf43a76804939622f5f58c33d625"

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 Curve2 observer V4 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

if old_reference not in obs or old_reference not in run:
    raise SystemExit("AERIS41 Curve2 reference marker missing")
obs = obs.replace(old_reference, new_reference)
run = run.replace(old_reference, new_reference)

# HeightNoise V4 already extended ModRecord. Add independent Curve2 production
# fingerprints so the real stock callback can run on a managed shadow only.
mod_marker = '''            internal PQSMod HeightNoiseProductionMod;
            internal long HeightNoiseProductionHBits;
            internal long HeightNoiseProductionNBits;
        }'''
mod_replacement = '''            internal PQSMod HeightNoiseProductionMod;
            internal long HeightNoiseProductionHBits;
            internal long HeightNoiseProductionNBits;

            // Main-thread-only VertexHeightNoiseVertHeightCurve2 isolation audit.
            internal PQSMod Curve2ProductionMod;
            internal long Curve2ProductionHBits;
            internal long Curve2ProductionRBits;
            internal long Curve2ProductionSBits;
            internal int Curve2ProductionTBits;
        }'''
if obs.count(mod_marker) != 1:
    raise SystemExit("AERIS41 Curve2 ModRecord marker not unique")
obs = obs.replace(mod_marker, mod_replacement, 1)

# Unity's native AnimationCurve cache is represented by PolynomialFloat in the
# pure evaluator. Some curves make both the source-shaped Hermite basis and the
# native-cache polynomial agree on the coarse 129-point census. Prefer the
# native-cache polynomial on an exact tie; if it is not exact, the existing
# selection logic still chooses the best matching alternative.
sel_start = obs.find("        CurveSelection SelectCurveSnapshot(string bodyName, AnimationCurve curve)")
sel_end = obs.find("        CurveSelection SelectRidgedCurveSnapshot", sel_start)
if sel_start < 0 or sel_end < 0:
    raise SystemExit("AERIS41 Curve2 SelectCurveSnapshot block missing")
sel = obs[sel_start:sel_end]
loop_marker = '''            for (int mode = 0; mode < 4; mode++)
            {
                var candidate = new AERISR041MohoDresPureCpuExact.CurveSnapshot('''
loop_replacement = '''            int[] modePreference = { 1, 0, 2, 3 };
            for (int modeRank = 0; modeRank < modePreference.Length; modeRank++)
            {
                int mode = modePreference[modeRank];
                var candidate = new AERISR041MohoDresPureCpuExact.CurveSnapshot('''
if sel.count(loop_marker) != 1:
    raise SystemExit("AERIS41 Curve2 selection loop marker not unique")
sel = sel.replace(loop_marker, loop_replacement, 1)

log_marker = '''                "; max_abs_error=" + R(bestMaxError) +
                "; exact=" + Bool(exact) +'''
log_replacement = '''                "; max_abs_error=" + R(bestMaxError) +
                "; selection_policy=POLYNOMIAL_FLOAT_TIE_PREFERRED" +
                "; exact=" + Bool(exact) +'''
if sel.count(log_marker) != 1:
    raise SystemExit("AERIS41 Curve2 selection log marker not unique")
sel = sel.replace(log_marker, log_replacement, 1)
obs = obs[:sel_start] + sel + obs[sel_end:]

# Isolate every Curve2 reference object after all primitive worker state has
# been captured. MemberwiseClone is supplied by the accepted LandControl V2
# transformation. The callback's h/r/s/t writes then land only on the shadow.
case_start = obs.find('                    case "PQSMod_VertexHeightNoiseVertHeightCurve2":')
case_end = obs.find('                    case "PQSMod_VertexRidgedAltitudeCurve":', case_start)
if case_start < 0 or case_end < 0:
    raise SystemExit("AERIS41 Curve2 case block missing")
case = obs[case_start:case_end]
end_marker = '''                            SnapshotRidged(RequireMember(record.Mod, "ridgedSub"), randomVectors),
                            selection.Snapshot);
                        break;'''
end_replacement = '''                            SnapshotRidged(RequireMember(record.Mod, "ridgedSub"), randomVectors),
                            selection.Snapshot);

                        record.Curve2ProductionMod = record.Mod;
                        record.Curve2ProductionHBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.Mod, "h"));
                        record.Curve2ProductionRBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.Mod, "r"));
                        record.Curve2ProductionSBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.Mod, "s"));
                        record.Curve2ProductionTBits = FloatBits(Convert.ToSingle(
                            RequireMember(record.Mod, "t"), CultureInfo.InvariantCulture));

                        PQSMod curve2Shadow = ManagedMemberwiseClone(record.Mod) as PQSMod;
                        if (curve2Shadow == null || ReferenceEquals(curve2Shadow, record.Mod))
                            throw new InvalidOperationException(
                                bodyName + "_CURVE2_REFERENCE_MOD_NOT_ISOLATED");
                        record.Mod = curve2Shadow;

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_VertexHeightNoiseVertHeightCurve2" +
                            "; reference_object=ISOLATED_MANAGED_SHADOW" +
                            "; callback_il_sha256=6c68df85bb2f8d294c4df5299d05c893ac3edf43a76804939622f5f58c33d625" +
                            "; curve_selection_policy=POLYNOMIAL_FLOAT_TIE_PREFERRED" +
                            "; source_semantics=CAPTURED_STOCK_IL" +
                            "; exact_candidate=true" + Invariants());
                        break;'''
if case.count(end_marker) != 1:
    raise SystemExit("AERIS41 Curve2 case insertion marker not unique")
case = case.replace(end_marker, end_replacement, 1)
obs = obs[:case_start] + case + obs[case_end:]

# Audit original Curve2 h/r/s/t state after every witness callback has executed.
audit_marker = '''            AuditLandControlReferenceIsolation(bodyName, mods);
            AuditHeightNoiseReferenceIsolation(bodyName, mods);

            string topologyText = string.Join(",", topology.ToArray());'''
audit_replacement = '''            AuditLandControlReferenceIsolation(bodyName, mods);
            AuditCurve2ReferenceIsolation(bodyName, mods);
            AuditHeightNoiseReferenceIsolation(bodyName, mods);

            string topologyText = string.Join(",", topology.ToArray());'''
if obs.count(audit_marker) != 1:
    raise SystemExit("AERIS41 Curve2 audit-call marker not unique")
obs = obs.replace(audit_marker, audit_replacement, 1)

helper_marker = '''        static IList GetModifierList(object pqs)'''
if obs.count(helper_marker) != 1:
    raise SystemExit("AERIS41 Curve2 helper marker not unique")
helper = r'''        static void AuditCurve2ReferenceIsolation(
            string bodyName,
            List<ModRecord> mods)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                ModRecord record = mods[i];
                if (record.Curve2ProductionMod == null)
                    continue;

                bool productionHUnchanged =
                    BitConverter.DoubleToInt64Bits(
                        ReadDouble(record.Curve2ProductionMod, "h")) ==
                    record.Curve2ProductionHBits;
                bool productionRUnchanged =
                    BitConverter.DoubleToInt64Bits(
                        ReadDouble(record.Curve2ProductionMod, "r")) ==
                    record.Curve2ProductionRBits;
                bool productionSUnchanged =
                    BitConverter.DoubleToInt64Bits(
                        ReadDouble(record.Curve2ProductionMod, "s")) ==
                    record.Curve2ProductionSBits;
                bool productionTUnchanged =
                    FloatBits(Convert.ToSingle(
                        RequireMember(record.Curve2ProductionMod, "t"),
                        CultureInfo.InvariantCulture)) ==
                    record.Curve2ProductionTBits;
                bool referenceObjectIsolated =
                    !ReferenceEquals(record.Curve2ProductionMod, record.Mod);

                bool sphereSharedReadonly = ReferenceEquals(
                    ReadMember(record.Curve2ProductionMod, "sphere"),
                    ReadMember(record.Mod, "sphere"));
                bool simplexSharedReadonly = ReferenceEquals(
                    ReadMember(record.Curve2ProductionMod, "simplex"),
                    ReadMember(record.Mod, "simplex"));
                bool ridgedAddSharedReadonly = ReferenceEquals(
                    ReadMember(record.Curve2ProductionMod, "ridgedAdd"),
                    ReadMember(record.Mod, "ridgedAdd"));
                bool ridgedSubSharedReadonly = ReferenceEquals(
                    ReadMember(record.Curve2ProductionMod, "ridgedSub"),
                    ReadMember(record.Mod, "ridgedSub"));
                bool curveSharedReadonly = ReferenceEquals(
                    ReadMember(record.Curve2ProductionMod, "simplexCurve"),
                    ReadMember(record.Mod, "simplexCurve"));

                bool pass =
                    productionHUnchanged &&
                    productionRUnchanged &&
                    productionSUnchanged &&
                    productionTUnchanged &&
                    referenceObjectIsolated &&
                    sphereSharedReadonly &&
                    simplexSharedReadonly &&
                    ridgedAddSharedReadonly &&
                    ridgedSubSharedReadonly &&
                    curveSharedReadonly;

                AERISLogger.Info(
                    "[AERIS39][HEIGHT_CHAIN_CURVE2_AUDIT]" +
                    "; body=" + Safe(bodyName) +
                    "; modifier_index=" + record.Index.ToString(CultureInfo.InvariantCulture) +
                    "; pass=" + Bool(pass) +
                    "; production_h_unchanged=" + Bool(productionHUnchanged) +
                    "; production_r_unchanged=" + Bool(productionRUnchanged) +
                    "; production_s_unchanged=" + Bool(productionSUnchanged) +
                    "; production_t_unchanged=" + Bool(productionTUnchanged) +
                    "; reference_object_isolated=" + Bool(referenceObjectIsolated) +
                    "; sphere_shared_readonly=" + Bool(sphereSharedReadonly) +
                    "; simplex_shared_readonly=" + Bool(simplexSharedReadonly) +
                    "; ridged_add_shared_readonly=" + Bool(ridgedAddSharedReadonly) +
                    "; ridged_sub_shared_readonly=" + Bool(ridgedSubSharedReadonly) +
                    "; curve_shared_readonly=" + Bool(curveSharedReadonly) +
                    "; reference_callback=REAL_PQSMOD_VERTEXHEIGHTNOISEVERTHEIGHTCURVE2_ONVERTEXBUILDHEIGHT_MANAGED_SHADOW" +
                    Invariants());

                if (!pass)
                    throw new InvalidOperationException(
                        bodyName + "_CURVE2_REFERENCE_ISOLATION_AUDIT_FAILED");
            }
        }

'''
obs = obs.replace(helper_marker, helper + helper_marker, 1)

# Provenance and acceptance. Six stock Curve2 instances are in the target set:
# Kerbin=1, Eve=1, Duna=3, Moho=1.
provenance_marker = "production_vertexheightnoise_state_audit=REQUIRED_UNCHANGED"
provenance_new = provenance_marker + (
    "\ncurve2_callback_il_sha256=" + callback_il_sha +
    "\ncurve2_curve_selection=POLYNOMIAL_FLOAT_TIE_PREFERRED"
    "\nproduction_curve2_state_audit=REQUIRED_UNCHANGED")
if run.count(provenance_marker) != 1:
    raise SystemExit("AERIS41 Curve2 provenance marker not unique")
run = run.replace(provenance_marker, provenance_new, 1)

accept_marker = '''  write_artifacts "$segment" "$([[ "$pass" -eq 1 ]] && echo PASS || echo FAIL_ACCEPTANCE)" "$installed_sha"'''
accept_insert = r'''  local curve2_audit_count curve2_audit_fail
  curve2_audit_count="$(grep -F "[AERIS39][HEIGHT_CHAIN_CURVE2_AUDIT]" "$segment" | wc -l | tr -d ' ')"
  [[ "$curve2_audit_count" = "6" ]] || pass=0
  curve2_audit_fail="$(grep -F "[AERIS39][HEIGHT_CHAIN_CURVE2_AUDIT]" "$segment" | grep -F "; pass=false;" || true)"
  [[ -z "$curve2_audit_fail" ]] || pass=0

  local curve2_body_count
  for body_count_spec in "Kerbin:1" "Eve:1" "Duna:3" "Moho:1"; do
    local curve2_body expected_count
    curve2_body="${body_count_spec%%:*}"
    expected_count="${body_count_spec##*:}"
    curve2_body_count="$(grep -F "[AERIS39][HEIGHT_CHAIN_CURVE2_AUDIT]" "$segment" | grep -F "; body=$curve2_body;" | wc -l | tr -d ' ')"
    [[ "$curve2_body_count" = "$expected_count" ]] || pass=0
  done

'''
if run.count(accept_marker) != 1:
    raise SystemExit("AERIS41 Curve2 acceptance insertion marker not unique")
run = run.replace(accept_marker, accept_insert + accept_marker, 1)

for token in [
    new_candidate,
    new_reference,
    "POLYNOMIAL_FLOAT_TIE_PREFERRED",
    "Curve2ProductionHBits",
    "HEIGHT_CHAIN_CURVE2_AUDIT",
    "REAL_PQSMOD_VERTEXHEIGHTNOISEVERTHEIGHTCURVE2_ONVERTEXBUILDHEIGHT_MANAGED_SHADOW",
    callback_il_sha,
]:
    if token not in obs:
        raise SystemExit("AERIS41 generated Curve2 observer missing: " + token)

for token in [
    new_candidate,
    new_reference,
    "curve2_callback_il_sha256=" + callback_il_sha,
    "curve2_curve_selection=POLYNOMIAL_FLOAT_TIE_PREFERRED",
    "production_curve2_state_audit=REQUIRED_UNCHANGED",
    "HEIGHT_CHAIN_CURVE2_AUDIT",
]:
    if token not in run:
        raise SystemExit("AERIS41 generated Curve2 runner missing: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
