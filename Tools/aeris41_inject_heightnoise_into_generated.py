#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit(
        "usage: aeris41_inject_heightnoise_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V3"
new_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V4"
old_reference = (
    "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN_LANDCONTROL_MANAGED_SHADOW")
new_reference = (
    "REAL_ORDERED_PQS_HEIGHT_CALLBACK_CHAIN_"
    "LANDCONTROL_HEIGHTNOISE_MANAGED_SHADOW")

if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 HeightNoise observer V3 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

if old_reference not in obs or old_reference not in run:
    raise SystemExit("AERIS41 HeightNoise reference marker missing")
obs = obs.replace(old_reference, new_reference)
run = run.replace(old_reference, new_reference)

# Extend the already-generated LandControl ModRecord with dedicated HeightNoise
# production fingerprints. Do not reuse ProductionMod: that field has specific
# LandControl audit semantics and ReferenceSphere handling.
mod_marker = '''            internal long ProductionSphereSxBits;\n            internal long ProductionSphereSyBits;\n        }'''
mod_replacement = '''            internal long ProductionSphereSxBits;\n            internal long ProductionSphereSyBits;\n\n            // Main-thread-only VertexHeightNoiseVertHeight isolation audit.\n            internal PQSMod HeightNoiseProductionMod;\n            internal long HeightNoiseProductionHBits;\n            internal long HeightNoiseProductionNBits;\n        }'''
if obs.count(mod_marker) != 1:
    raise SystemExit("AERIS41 HeightNoise ModRecord marker not unique")
obs = obs.replace(mod_marker, mod_replacement, 1)

needle = '''                    default:\n                        throw new InvalidOperationException(\n                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);'''
case = r'''                    case "PQSMod_VertexHeightNoiseVertHeight":
                    {
                        object runtimeNoise = RequireMember(record.Mod, "noiseMap");
                        string runtimeType = TypeName(runtimeNoise.GetType());
                        if (!string.Equals(
                                runtimeType,
                                "LibNoise.RidgedMultifractal",
                                StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                bodyName + "_HEIGHTNOISE_RUNTIME_TYPE:" + runtimeType);

                        object noiseType = RequireMember(record.Mod, "noiseType");
                        if (!string.Equals(
                                noiseType.ToString(),
                                "RidgedMultifractal",
                                StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                bodyName + "_HEIGHTNOISE_NOISE_TYPE:" + noiseType);

                        float deformity = Convert.ToSingle(
                            RequireMember(record.Mod, "deformity"),
                            CultureInfo.InvariantCulture);
                        int seed = ReadInt(record.Mod, "seed");
                        float frequency = Convert.ToSingle(
                            RequireMember(record.Mod, "frequency"),
                            CultureInfo.InvariantCulture);
                        float lacunarity = Convert.ToSingle(
                            RequireMember(record.Mod, "lacunarity"),
                            CultureInfo.InvariantCulture);
                        int octaves = ReadInt(record.Mod, "octaves");
                        int mode = Convert.ToInt32(
                            RequireMember(record.Mod, "mode"),
                            CultureInfo.InvariantCulture);
                        float heightStart = Convert.ToSingle(
                            RequireMember(record.Mod, "heightStart"),
                            CultureInfo.InvariantCulture);
                        float heightEnd = Convert.ToSingle(
                            RequireMember(record.Mod, "heightEnd"),
                            CultureInfo.InvariantCulture);
                        double hDeltaR = ReadDouble(record.Mod, "hDeltaR");
                        double heightNoiseRadiusMin = ReadDouble(pqs, "radiusMin");
                        double heightNoiseRadiusDelta = ReadDouble(pqs, "radiusDelta");

                        var ridged = SnapshotRidged(runtimeNoise, randomVectors);
                        if (BitConverter.DoubleToInt64Bits((double)frequency) !=
                                BitConverter.DoubleToInt64Bits(ridged.Frequency) ||
                            BitConverter.DoubleToInt64Bits((double)lacunarity) !=
                                BitConverter.DoubleToInt64Bits(ridged.Lacunarity) ||
                            seed != ridged.Seed ||
                            octaves != ridged.OctaveCount ||
                            mode != ridged.NoiseQuality)
                            throw new InvalidOperationException(
                                bodyName + "_HEIGHTNOISE_ONSETUP_STATE_MISMATCH");

                        pureOps[i] =
                            new AERIS41VertexHeightNoiseVertHeightPureCpuExact.OpSnapshot(
                                deformity,
                                heightNoiseRadiusMin,
                                heightNoiseRadiusDelta,
                                heightStart,
                                heightEnd,
                                hDeltaR,
                                ridged);

                        // Stock OnVertexBuildHeight writes this modifier's h and n
                        // fields for every witness vertex. Invoke the real method on
                        // a MemberwiseClone so production state remains untouched.
                        record.HeightNoiseProductionMod = record.Mod;
                        record.HeightNoiseProductionHBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.Mod, "h"));
                        record.HeightNoiseProductionNBits = BitConverter.DoubleToInt64Bits(
                            ReadDouble(record.Mod, "n"));
                        PQSMod heightNoiseShadow = ManagedMemberwiseClone(record.Mod) as PQSMod;
                        if (heightNoiseShadow == null ||
                            ReferenceEquals(heightNoiseShadow, record.Mod))
                            throw new InvalidOperationException(
                                bodyName + "_HEIGHTNOISE_REFERENCE_MOD_NOT_ISOLATED");
                        record.Mod = heightNoiseShadow;

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_VertexHeightNoiseVertHeight" +
                            "; dependency=RIDGED_MULTIFRACTAL_GRADIENT_NOISE" +
                            "; frequency=" + frequency.ToString("R", CultureInfo.InvariantCulture) +
                            "; seed=" + seed.ToString(CultureInfo.InvariantCulture) +
                            "; lacunarity=" + lacunarity.ToString("R", CultureInfo.InvariantCulture) +
                            "; octaves=" + octaves.ToString(CultureInfo.InvariantCulture) +
                            "; noise_quality=" + mode.ToString(CultureInfo.InvariantCulture) +
                            "; deformity=" + deformity.ToString("R", CultureInfo.InvariantCulture) +
                            "; height_start=" + heightStart.ToString("R", CultureInfo.InvariantCulture) +
                            "; height_end=" + heightEnd.ToString("R", CultureInfo.InvariantCulture) +
                            "; h_delta_r=" + hDeltaR.ToString("R", CultureInfo.InvariantCulture) +
                            "; reference_object=ISOLATED_MANAGED_SHADOW" +
                            "; runtime_setup_state=BIT_VERIFIED" +
                            "; ridged_gradient_core=R039_EXACT_REUSE" +
                            "; source_semantics=CAPTURED_STOCK_IL" +
                            "; exact_candidate=true" + Invariants());
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);'''
if obs.count(needle) != 1:
    raise SystemExit("AERIS41 HeightNoise observer case insertion point not unique")
obs = obs.replace(needle, case, 1)

# Audit production h/n after all reference callbacks for the body have run.
audit_marker = '''            AuditLandControlReferenceIsolation(bodyName, mods);\n\n            string topologyText = string.Join(",", topology.ToArray());'''
audit_replacement = '''            AuditLandControlReferenceIsolation(bodyName, mods);\n            AuditHeightNoiseReferenceIsolation(bodyName, mods);\n\n            string topologyText = string.Join(",", topology.ToArray());'''
if obs.count(audit_marker) != 1:
    raise SystemExit("AERIS41 HeightNoise audit-call marker not unique")
obs = obs.replace(audit_marker, audit_replacement, 1)

helper_marker = '''        static IList GetModifierList(object pqs)'''
if obs.count(helper_marker) != 1:
    raise SystemExit("AERIS41 HeightNoise helper marker not unique")
helper = r'''        static void AuditHeightNoiseReferenceIsolation(
            string bodyName,
            List<ModRecord> mods)
        {
            for (int i = 0; i < mods.Count; i++)
            {
                ModRecord record = mods[i];
                if (record.HeightNoiseProductionMod == null)
                    continue;

                bool productionHUnchanged =
                    BitConverter.DoubleToInt64Bits(
                        ReadDouble(record.HeightNoiseProductionMod, "h")) ==
                    record.HeightNoiseProductionHBits;
                bool productionNUnchanged =
                    BitConverter.DoubleToInt64Bits(
                        ReadDouble(record.HeightNoiseProductionMod, "n")) ==
                    record.HeightNoiseProductionNBits;
                bool referenceObjectIsolated =
                    !ReferenceEquals(record.HeightNoiseProductionMod, record.Mod);
                bool noiseMapSharedReadonly = ReferenceEquals(
                    ReadMember(record.HeightNoiseProductionMod, "noiseMap"),
                    ReadMember(record.Mod, "noiseMap"));
                bool sphereSharedReadonly = ReferenceEquals(
                    ReadMember(record.HeightNoiseProductionMod, "sphere"),
                    ReadMember(record.Mod, "sphere"));

                bool pass =
                    productionHUnchanged &&
                    productionNUnchanged &&
                    referenceObjectIsolated &&
                    noiseMapSharedReadonly &&
                    sphereSharedReadonly;

                AERISLogger.Info(
                    "[AERIS39][HEIGHT_CHAIN_HEIGHTNOISE_AUDIT]" +
                    "; body=" + Safe(bodyName) +
                    "; modifier_index=" + record.Index.ToString(CultureInfo.InvariantCulture) +
                    "; pass=" + Bool(pass) +
                    "; production_h_unchanged=" + Bool(productionHUnchanged) +
                    "; production_n_unchanged=" + Bool(productionNUnchanged) +
                    "; reference_object_isolated=" + Bool(referenceObjectIsolated) +
                    "; noise_map_shared_readonly=" + Bool(noiseMapSharedReadonly) +
                    "; sphere_shared_readonly=" + Bool(sphereSharedReadonly) +
                    "; reference_callback=REAL_PQSMOD_VERTEXHEIGHTNOISEVERTHEIGHT_ONVERTEXBUILDHEIGHT_MANAGED_SHADOW" +
                    Invariants());

                if (!pass)
                    throw new InvalidOperationException(
                        bodyName + "_HEIGHTNOISE_REFERENCE_ISOLATION_AUDIT_FAILED");
            }
        }

'''
obs = obs.replace(helper_marker, helper + helper_marker, 1)

# Add the pure source to the temporary csproj after the already-injected Voronoi
# source. Canonical csproj remains untouched.
csproj_line = r'''    "    <Compile Include=\"Terrain\\AERIS41VertexVoronoiPureCpuExact.cs\" />\n"
'''
csproj_new = csproj_line + r'''    "    <Compile Include=\"Terrain\\AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs\" />\n"
'''
if run.count(csproj_line) != 1:
    raise SystemExit("AERIS41 HeightNoise temp-csproj insertion point not unique")
run = run.replace(csproj_line, csproj_new, 1)

provenance = '''vertexvoronoi_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS41VertexVoronoiPureCpuExact.cs" | awk '{print $1}')'''
provenance_new = provenance + '''\nvertexheightnoise_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs" | awk '{print $1}')'''
if run.count(provenance) != 1:
    raise SystemExit("AERIS41 HeightNoise provenance insertion point not unique")
run = run.replace(provenance, provenance_new, 1)

semantics = "vertexvoronoi_reference=REAL_RUNTIME_CALLBACK"
semantics_new = semantics + (
    "\nvertexheightnoise_worker=CAPTURED_IL_PLUS_R039_RIDGED_EXACT_REUSE"
    "\nvertexheightnoise_reference=REAL_CALLBACK_MANAGED_MEMBERWISE_SHADOW"
    "\nproduction_vertexheightnoise_state_audit=REQUIRED_UNCHANGED")
if run.count(semantics) != 1:
    raise SystemExit("AERIS41 HeightNoise provenance semantics marker not unique")
run = run.replace(semantics, semantics_new, 1)

# Require the Eeloo isolation audit at acceptance, in addition to all existing
# LandControl and six-body exact gates.
accept_marker = '''  write_artifacts "$segment" "$([[ "$pass" -eq 1 ]] && echo PASS || echo FAIL_ACCEPTANCE)" "$installed_sha"'''
accept_insert = r'''  local heightnoise_audit
  heightnoise_audit="$(grep -F "[AERIS39][HEIGHT_CHAIN_HEIGHTNOISE_AUDIT]" "$segment" | grep -F "; body=Eeloo;" | tail -n 1 || true)"
  [[ -n "$heightnoise_audit" ]] || pass=0
  [[ "$heightnoise_audit" == *"; pass=true;"* ]] || pass=0
  [[ "$heightnoise_audit" == *"; production_h_unchanged=true;"* ]] || pass=0
  [[ "$heightnoise_audit" == *"; production_n_unchanged=true;"* ]] || pass=0
  [[ "$heightnoise_audit" == *"; reference_object_isolated=true;"* ]] || pass=0
  [[ "$heightnoise_audit" == *"; noise_map_shared_readonly=true;"* ]] || pass=0
  [[ "$heightnoise_audit" == *"; sphere_shared_readonly=true;"* ]] || pass=0

'''
if run.count(accept_marker) != 1:
    raise SystemExit("AERIS41 HeightNoise acceptance insertion marker not unique")
run = run.replace(accept_marker, accept_insert + accept_marker, 1)

# Generated-source collision gate: CaptureBody already owns the canonical
# radiusMin local. HeightNoise must use uniquely-prefixed locals so Mono's C#
# scoping rules cannot reject the generated switch case with CS0136.
canonical_radius_local = 'double radiusMin = ReadDouble(pqs, "radiusMin");'
if obs.count(canonical_radius_local) != 1:
    raise SystemExit(
        "AERIS41 generated observer canonical radiusMin local count=" +
        str(obs.count(canonical_radius_local)))
if obs.count('double heightNoiseRadiusMin = ReadDouble(pqs, "radiusMin");') != 1:
    raise SystemExit("AERIS41 generated observer HeightNoise radiusMin local missing")
if obs.count('double heightNoiseRadiusDelta = ReadDouble(pqs, "radiusDelta");') != 1:
    raise SystemExit("AERIS41 generated observer HeightNoise radiusDelta local missing")

for token in [
    new_candidate,
    new_reference,
    'case "PQSMod_VertexHeightNoiseVertHeight":',
    "AERIS41VertexHeightNoiseVertHeightPureCpuExact.OpSnapshot",
    "HEIGHT_CHAIN_HEIGHTNOISE_AUDIT",
    "HeightNoiseProductionHBits",
    "HeightNoiseProductionNBits",
    "RIDGED_MULTIFRACTAL_GRADIENT_NOISE",
]:
    if token not in obs:
        raise SystemExit("AERIS41 generated observer missing: " + token)

for token in [
    new_candidate,
    new_reference,
    "AERIS41VertexHeightNoiseVertHeightPureCpuExact.cs",
    "vertexheightnoise_pure_source_sha256=",
    "production_vertexheightnoise_state_audit=REQUIRED_UNCHANGED",
    "HEIGHT_CHAIN_HEIGHTNOISE_AUDIT",
]:
    if token not in run:
        raise SystemExit("AERIS41 generated runner missing: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
