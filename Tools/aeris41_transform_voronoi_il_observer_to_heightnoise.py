#!/usr/bin/env python3
import pathlib
import sys

if len(sys.argv) != 3:
    raise SystemExit("usage: aeris41_transform_voronoi_il_observer_to_heightnoise.py <source> <output>")

source_path = pathlib.Path(sys.argv[1])
out_path = pathlib.Path(sys.argv[2])
src = source_path.read_text(encoding="utf-8")

replacements = [
    ("AERIS41EelooVoronoiIlClosureObserver", "AERIS41EelooHeightNoiseVertHeightIlClosureObserver"),
    ("AERIS41_R041_EELOO_VORONOI_IL_CLOSURE_V1", "AERIS41_R041_EELOO_HEIGHTNOISE_VERTHEIGHT_IL_CLOSURE_V1"),
    ("PQSMod_VertexVoronoi", "PQSMod_VertexHeightNoiseVertHeight"),
    ("[AERIS41][VORONOI_IL]", "[AERIS41][HEIGHTNOISE_IL]"),
    ("EELOO_VORONOI_IL_CLOSURE_INCOMPLETE", "EELOO_HEIGHTNOISE_VERTHEIGHT_IL_CLOSURE_INCOMPLETE"),
    (
        "closure=TARGET_DECLARED_IL_PLUS_RUNTIME_LIBNOISE_FIELD_GRAPH_PLUS_GRADIENT_NOISE_BASIS",
        "closure=TARGET_DECLARED_IL_PLUS_RUNTIME_LIBNOISE_FIELD_GRAPH"
    ),
]
for old, new in replacements:
    if old not in src:
        raise SystemExit("missing observer transform marker: " + old)
    src = src.replace(old, new)

explicit_start = "            // Explicitly close the LibNoise types that a stock Voronoi implementation\n"
explicit_end = "                EmitLibNoiseTypeClosure(gradient, \"explicit:LibNoise.GradientNoiseBasis\", seenDependencyTypes, counters);\n\n"
start = src.find(explicit_start)
if start < 0:
    raise SystemExit("explicit dependency closure start marker missing")
end = src.find(explicit_end, start)
if end < 0:
    raise SystemExit("explicit dependency closure end marker missing")
end += len(explicit_end)
src = src[:start] + src[end:]

old_pass = '''            bool pass =
                counters.TargetInstances > 0 &&
                counters.TargetOnSetup &&
                counters.TargetOnVertexBuildHeight &&
                counters.VoronoiType &&
                counters.VoronoiGetValue &&
                counters.GradientNoiseBasisType &&
                counters.GradientValueNoise3D &&
                counters.Instructions > 0;'''
new_pass = '''            bool pass =
                counters.TargetInstances > 0 &&
                counters.TargetOnSetup &&
                counters.TargetOnVertexBuildHeight &&
                counters.Instructions > 0;'''
if src.count(old_pass) != 1:
    raise SystemExit("generic closure pass marker not unique")
src = src.replace(old_pass, new_pass, 1)

# Keep the legacy dependency counters in the diagnostic COMPLETE line only as
# non-authoritative informational fields. They are deliberately not acceptance
# requirements for this generic target.

for token in [
    "AERIS41EelooHeightNoiseVertHeightIlClosureObserver",
    "AERIS41_R041_EELOO_HEIGHTNOISE_VERTHEIGHT_IL_CLOSURE_V1",
    "PQSMod_VertexHeightNoiseVertHeight",
    "[AERIS41][HEIGHTNOISE_IL][BEGIN]",
    "[AERIS41][HEIGHTNOISE_IL][COMPLETE]",
    "closure=TARGET_DECLARED_IL_PLUS_RUNTIME_LIBNOISE_FIELD_GRAPH",
]:
    if token not in src:
        raise SystemExit("transformed observer missing token: " + token)

if "explicit:LibNoise.Voronoi" in src or "explicit:LibNoise.GradientNoiseBasis" in src:
    raise SystemExit("hard-coded Voronoi dependency closure survived transform")

out_path.write_text(src, encoding="utf-8")
