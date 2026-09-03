#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
  echo "usage: bash Tools/aeris41_r041_eeloo_voronoi_pure_exact.sh <KSP root>" >&2
  exit 2
fi

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
KSP="$1"
BRANCH="agent/aeris39-r041-mapso-exact-cpu-shadow"
LAND_WRAPPER="$ROOT/Tools/aeris41_r041_landcontrol_witness_repair.sh"
PURE="$ROOT/Source/AERISFlightControl/Terrain/AERIS41VertexVoronoiPureCpuExact.cs"

cd "$ROOT"

test "$(git branch --show-current)" = "$BRANCH" || {
  echo "STOP: wrong branch" >&2
  git branch --show-current >&2
  exit 10
}

test -z "$(git status --porcelain)" || {
  echo "STOP: worktree dirty before AERIS41 Eeloo Voronoi exact stage" >&2
  git status -sb >&2
  exit 11
}

[[ -f "$LAND_WRAPPER" ]] || { echo "STOP: LandControl repair wrapper missing" >&2; exit 12; }
[[ -f "$PURE" ]] || { echo "STOP: VertexVoronoi pure source missing" >&2; exit 13; }

grep -Fq 'static int VoronoiCell(double value)' "$PURE" || {
  echo "STOP: VertexVoronoi exact cell semantics missing" >&2
  exit 14
}
grep -Fq 'internal static int IntValueNoise(int x, int y, int z, int seed)' "$PURE" || {
  echo "STOP: VertexVoronoi exact IntValueNoise missing" >&2
  exit 15
}
grep -Fq 'const double Sqrt3 = 1.7320508075688772;' "$PURE" || {
  echo "STOP: captured LibNoise.Math.Sqrt3 missing" >&2
  exit 16
}

TMPDIR="$(mktemp -d /tmp/AERIS41_R041_VORONOI_EXACT.XXXXXX)"
WRAPPER="$TMPDIR/aeris41_r041_landcontrol_plus_voronoi.sh"
cleanup() { rm -rf "$TMPDIR"; }
trap cleanup EXIT

python3 - "$LAND_WRAPPER" "$WRAPPER" <<'PY'
import pathlib, sys
src_path = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
src = src_path.read_text(encoding="utf-8")
marker = 'chmod 0700 "$SHADOW_RUNNER"'
if src.count(marker) != 1:
    raise SystemExit("AERIS41 Voronoi outer-wrapper insertion point not unique")

injection = r'''# AERIS41 Phase B: after the already-accepted LandControl managed-shadow
# transform has produced its observer/runner, add only the Eeloo Voronoi
# pure snapshot/evaluator path. Production Voronoi remains the real reference
# callback target; only the worker path is reconstructed.
python3 - "$SHADOW_OBSERVER" "$SHADOW_RUNNER" <<'AERIS41_VORONOI_PY'
import pathlib

observer_path = pathlib.Path("$SHADOW_OBSERVER")
runner_path = pathlib.Path("$SHADOW_RUNNER")
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

# The underlying R041 runner generates a temporary project. Add the new pure
# source next to the already-injected LandControl pure source without touching
# the canonical csproj.
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

semantics = 'production_landcontrol_state_audit=REQUIRED_UNCHANGED'
semantics_new = semantics + '\nvertexvoronoi_worker=CAPTURED_IL_PURE_CPU_EXACT\nvertexvoronoi_reference=REAL_RUNTIME_CALLBACK'
if run.count(semantics) != 1:
    raise SystemExit("AERIS41 Voronoi provenance semantics marker not unique")
run = run.replace(semantics, semantics_new, 1)

for token in [
    new_candidate,
    'case "PQSMod_VertexVoronoi":',
    'AERIS41VertexVoronoiPureCpuExact.OpSnapshot',
    'VERTEX_VORONOI_ONSETUP_STATE_MISMATCH',
    'dependency=LIBNOISE_VORONOI_VALUE_NOISE',
]:
    if token not in obs:
        raise SystemExit("AERIS41 generated observer missing: " + token)
for token in [
    new_candidate,
    'AERIS41VertexVoronoiPureCpuExact.cs',
    'vertexvoronoi_pure_source_sha256=',
    'vertexvoronoi_reference=REAL_RUNTIME_CALLBACK',
]:
    if token not in run:
        raise SystemExit("AERIS41 generated runner missing: " + token)

observer_path.write_text(obs, encoding="utf-8")
runner_path.write_text(run, encoding="utf-8")
AERIS41_VORONOI_PY
'''

src = src.replace(marker, injection + marker, 1)
out_path.write_text(src, encoding="utf-8")
PY

chmod 0700 "$WRAPPER"

# The wrapped LandControl stage still enforces all previously accepted
# production-state isolation gates. This outer stage adds no worktree mutation.
set +e
bash "$WRAPPER" "$KSP"
RC=$?
set -e
exit "$RC"
