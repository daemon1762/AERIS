#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit("usage: aeris41_inject_voronoi_into_generated.py <observer> <runner>")

observer_path = pathlib.Path(sys.argv[1])
runner_path = pathlib.Path(sys.argv[2])
obs = observer_path.read_text(encoding="utf-8")
run = runner_path.read_text(encoding="utf-8")

old_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V2"
new_candidate = "AERIS39_R041_ALLBODY_HEIGHT_MODIFIER_CHAIN_SHADOW_V3"
if obs.count(old_candidate) != 1:
    raise SystemExit("AERIS41 Voronoi observer V2 candidate marker not unique")
obs = obs.replace(old_candidate, new_candidate, 1)
run = run.replace(old_candidate, new_candidate)

needle = '''                    default:\n                        throw new InvalidOperationException(\n                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);'''
case = r'''                    case "PQSMod_VertexVoronoi":
                    {
                        object runtimeVoronoi = RequireMember(record.Mod, "voronoi");
                        string runtimeType = TypeName(runtimeVoronoi.GetType());
                        if (!string.Equals(runtimeType, "LibNoise.Voronoi", StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                bodyName + "_VERTEX_VORONOI_RUNTIME_TYPE:" + runtimeType);

                        double frequency = ReadDouble(record.Mod, "voronoiFrequency");
                        double displacement = ReadDouble(record.Mod, "voronoiDisplacement");
                        int seed = ReadInt(record.Mod, "voronoiSeed");
                        bool distanceEnabled = (bool)RequireMember(record.Mod, "voronoiEnableDistance");
                        double deformation = ReadDouble(record.Mod, "deformation");

                        // OnSetup constructs LibNoise.Voronoi directly from these
                        // four fields. Fail closed unless live backing state is bit
                        // identical to that captured setup input.
                        double liveFrequency = ReadDouble(
                            runtimeVoronoi, "<Frequency>k__BackingField");
                        double liveDisplacement = ReadDouble(
                            runtimeVoronoi, "<Displacement>k__BackingField");
                        int liveSeed = ReadInt(runtimeVoronoi, "<Seed>k__BackingField");
                        bool liveDistance = (bool)RequireMember(
                            runtimeVoronoi, "<DistanceEnabled>k__BackingField");

                        if (BitConverter.DoubleToInt64Bits(frequency) !=
                                BitConverter.DoubleToInt64Bits(liveFrequency) ||
                            BitConverter.DoubleToInt64Bits(displacement) !=
                                BitConverter.DoubleToInt64Bits(liveDisplacement) ||
                            seed != liveSeed ||
                            distanceEnabled != liveDistance)
                            throw new InvalidOperationException(
                                bodyName + "_VERTEX_VORONOI_ONSETUP_STATE_MISMATCH");

                        pureOps[i] = new AERIS41VertexVoronoiPureCpuExact.OpSnapshot(
                            frequency,
                            displacement,
                            seed,
                            distanceEnabled,
                            deformation);

                        AERISLogger.Info(
                            "[AERIS39][HEIGHT_CHAIN_DEPENDENCY]" +
                            "; body=" + Safe(bodyName) +
                            "; type=PQSMod_VertexVoronoi" +
                            "; dependency=LIBNOISE_VORONOI_VALUE_NOISE" +
                            "; frequency=" + frequency.ToString("R", CultureInfo.InvariantCulture) +
                            "; displacement=" + displacement.ToString("R", CultureInfo.InvariantCulture) +
                            "; seed=" + seed.ToString(CultureInfo.InvariantCulture) +
                            "; distance_enabled=" + Bool(distanceEnabled) +
                            "; deformation=" + deformation.ToString("R", CultureInfo.InvariantCulture) +
                            "; setup_state=LIVE_BACKING_FIELDS_BIT_VERIFIED" +
                            "; value_noise=CAPTURED_INT32_IL_EXACT" +
                            "; sqrt3_bits=0x3FFBB67AE8584CAA" +
                            "; source_semantics=STOCK_ONVERTEXBUILDHEIGHT" +
                            "; exact_candidate=true" + Invariants());
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            bodyName + "_UNSUPPORTED_HEIGHT_MODIFIER:" + record.TypeName);'''
if obs.count(needle) != 1:
    raise SystemExit("AERIS41 Voronoi observer case insertion point not unique")
obs = obs.replace(needle, case, 1)

# The generated R041 runner contains the temporary-csproj Python generator.
# Extend only that generated project; the canonical csproj remains untouched.
csproj_line = r'''    "    <Compile Include=\"Terrain\\AERIS39LandControlPureCpuExact.cs\" />\n"
'''
csproj_new = csproj_line + r'''    "    <Compile Include=\"Terrain\\AERIS41VertexVoronoiPureCpuExact.cs\" />\n"
'''
if run.count(csproj_line) != 1:
    raise SystemExit("AERIS41 Voronoi temp-csproj insertion point not unique")
run = run.replace(csproj_line, csproj_new, 1)

provenance = '''landcontrol_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39LandControlPureCpuExact.cs" | awk '{print $1}')'''
provenance_new = provenance + '''\nvertexvoronoi_pure_source_sha256=$(sha256sum "$SRC/Terrain/AERIS41VertexVoronoiPureCpuExact.cs" | awk '{print $1}')'''
if run.count(provenance) != 1:
    raise SystemExit("AERIS41 Voronoi provenance insertion point not unique")
run = run.replace(provenance, provenance_new, 1)

semantics = "production_landcontrol_state_audit=REQUIRED_UNCHANGED"
semantics_new = semantics + "\nvertexvoronoi_worker=CAPTURED_IL_PURE_CPU_EXACT\nvertexvoronoi_reference=REAL_RUNTIME_CALLBACK"
if run.count(semantics) != 1:
    raise SystemExit("AERIS41 Voronoi provenance semantics marker not unique")
run = run.replace(semantics, semantics_new, 1)

for token in [
    new_candidate,
    'case "PQSMod_VertexVoronoi":',
    "AERIS41VertexVoronoiPureCpuExact.OpSnapshot",
    "VERTEX_VORONOI_ONSETUP_STATE_MISMATCH",
    "dependency=LIBNOISE_VORONOI_VALUE_NOISE",
]:
    if token not in obs:
        raise SystemExit("AERIS41 generated observer missing: " + token)

for token in [
    new_candidate,
    "AERIS41VertexVoronoiPureCpuExact.cs",
    "vertexvoronoi_pure_source_sha256=",
    "vertexvoronoi_reference=REAL_RUNTIME_CALLBACK",
]:
    if token not in run:
        raise SystemExit("AERIS41 generated runner missing: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
