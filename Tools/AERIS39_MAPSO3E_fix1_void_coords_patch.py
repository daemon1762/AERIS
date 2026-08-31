#!/usr/bin/env python3
from pathlib import Path
import sys

if len(sys.argv) != 5:
    raise SystemExit("usage: patch.py <observer-in> <runner-in> <observer-out> <runner-out>")

observer_in, runner_in, observer_out, runner_out = map(Path, sys.argv[1:])
obs = observer_in.read_text(encoding="utf-8")
run = runner_in.read_text(encoding="utf-8")

old_candidate = "AERIS39_MAPSO3E_PIPELINE_ISOLATION_DIAGNOSTIC_V1"
new_candidate = "AERIS39_MAPSO3E_PIPELINE_ISOLATION_DIAGNOSTIC_V2_VOID_COORDS_FIX1"
if obs.count(old_candidate) != 1:
    raise SystemExit(f"observer candidate count mismatch: {obs.count(old_candidate)}")
obs = obs.replace(old_candidate, new_candidate, 1)

old_block = '''                    object boxedCoords;
                    try
                    {
                        boxedCoords = construct.Invoke(map, new object[] { sample.U, sample.V });
                    }
                    catch (TargetInvocationException tie)
                    {
                        throw RootException(tie);
                    }

                    Coords stockCoords = ReadCoords(boxedCoords);
'''
new_block = '''                    // Stock MapSO.ConstructBilinearCoords(double,double) is a void-style
                    // scratch-state helper. Reflection Invoke therefore returns null by design.
                    // The observable coordinates live on the MapSO instance itself.
                    try
                    {
                        construct.Invoke(map, new object[] { sample.U, sample.V });
                    }
                    catch (TargetInvocationException tie)
                    {
                        throw RootException(tie);
                    }

                    Coords stockCoords = ReadCoords(map);
'''
if obs.count(old_block) != 1:
    raise SystemExit(f"observer void-coords block count mismatch: {obs.count(old_block)}")
obs = obs.replace(old_block, new_block, 1)

if run.count(old_candidate) != 1:
    raise SystemExit(f"runner candidate count mismatch: {run.count(old_candidate)}")
run = run.replace(old_candidate, new_candidate, 1)

old_root = 'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"'
new_root = 'ROOT="${AERIS_FIX1_ROOT:?AERIS_FIX1_ROOT missing}"'
if run.count(old_root) != 1:
    raise SystemExit(f"runner ROOT count mismatch: {run.count(old_root)}")
run = run.replace(old_root, new_root, 1)

old_compile = '    "    <Compile Include=\\"Terrain\\\\AERIS39MapSoPipelineIsolationDiagnosticObserver.cs\\" />\\n"'
new_compile = '    "    <Compile Include=\\"/tmp/AERIS39MapSoPipelineIsolationDiagnosticObserver.Fix1.cs\\" />\\n"'
if run.count(old_compile) != 1:
    raise SystemExit(f"runner compile include count mismatch: {run.count(old_compile)}")
run = run.replace(old_compile, new_compile, 1)

old_provenance = 'diagnostic_observer_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoPipelineIsolationDiagnosticObserver.cs" | awk \'{print $1}\')'
new_provenance = 'diagnostic_observer_source_sha256=$(sha256sum "/tmp/AERIS39MapSoPipelineIsolationDiagnosticObserver.Fix1.cs" | awk \'{print $1}\')'
if run.count(old_provenance) != 1:
    raise SystemExit(f"runner provenance count mismatch: {run.count(old_provenance)}")
run = run.replace(old_provenance, new_provenance, 1)

run = run.replace(
    'OUT="$ARTIFACT_ROOT/AERIS39_MAPSO3E_Pipeline_Isolation"',
    'OUT="$ARTIFACT_ROOT/AERIS39_MAPSO3E_FIX1_Pipeline_Isolation"',
    1)
run = run.replace(
    'ARCHIVE="$ARTIFACT_ROOT/AERIS39_MAPSO3E_Pipeline_Isolation.tar.gz"',
    'ARCHIVE="$ARTIFACT_ROOT/AERIS39_MAPSO3E_FIX1_Pipeline_Isolation.tar.gz"',
    1)
run = run.replace(
    'STATE_DIR="$HOME/.cache/AERIS/mapso3e-pipeline-isolation/$KEY"',
    'STATE_DIR="$HOME/.cache/AERIS/mapso3e-fix1-pipeline-isolation/$KEY"',
    1)
run = run.replace(
    'tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_MAPSO3E_Pipeline_Isolation',
    'tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_MAPSO3E_FIX1_Pipeline_Isolation',
    1)

observer_out.write_text(obs, encoding="utf-8")
runner_out.write_text(run, encoding="utf-8")
print("AERIS39_MAPSO3E_FIX1_PATCH=PASS")
