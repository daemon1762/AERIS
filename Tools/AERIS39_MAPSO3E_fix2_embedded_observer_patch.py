#!/usr/bin/env python3
from pathlib import Path
import sys

if len(sys.argv) != 5:
    raise SystemExit("usage: patch.py <observer-in> <runner-in> <observer-out> <runner-out>")

observer_in, runner_in, observer_out, runner_out = map(Path, sys.argv[1:])
obs = observer_in.read_text(encoding="utf-8")
run = runner_in.read_text(encoding="utf-8")

old_candidate = "AERIS39_MAPSO3E_PIPELINE_ISOLATION_DIAGNOSTIC_V1"
new_candidate = "AERIS39_MAPSO3E_PIPELINE_ISOLATION_DIAGNOSTIC_V3_EMBEDDED_OBSERVER_FIX2"
if obs.count(old_candidate) != 1:
    raise SystemExit(f"observer candidate count mismatch: {obs.count(old_candidate)}")
obs = obs.replace(old_candidate, new_candidate, 1)

old_class = "internal sealed class AERIS39MapSoPipelineIsolationDiagnosticObserver : MonoBehaviour"
new_class = "public sealed class AERIS39MapSoPipelineIsolationDiagnosticFix2Observer : MonoBehaviour"
if obs.count(old_class) != 1:
    raise SystemExit(f"observer class count mismatch: {obs.count(old_class)}")
obs = obs.replace(old_class, new_class, 1)

old_awake = '''        void Awake()\n        {\n            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;\n        }\n'''
new_awake = '''        void Awake()\n        {\n            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;\n            AERISLogger.Info(\n                "[AERIS39][MAPSO3E_BOOT]" +\n                "; candidate=" + Candidate +\n                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +\n                Invariants());\n        }\n'''
if obs.count(old_awake) != 1:
    raise SystemExit(f"observer Awake count mismatch: {obs.count(old_awake)}")
obs = obs.replace(old_awake, new_awake, 1)

old_block = '''                    object boxedCoords;\n                    try\n                    {\n                        boxedCoords = construct.Invoke(map, new object[] { sample.U, sample.V });\n                    }\n                    catch (TargetInvocationException tie)\n                    {\n                        throw RootException(tie);\n                    }\n\n                    Coords stockCoords = ReadCoords(boxedCoords);\n'''
new_block = '''                    // Stock MapSO.ConstructBilinearCoords(double,double) mutates\n                    // MapSO scratch fields and returns void/null via reflection.\n                    try\n                    {\n                        construct.Invoke(map, new object[] { sample.U, sample.V });\n                    }\n                    catch (TargetInvocationException tie)\n                    {\n                        throw RootException(tie);\n                    }\n\n                    Coords stockCoords = ReadCoords(map);\n'''
if obs.count(old_block) != 1:
    raise SystemExit(f"observer void-coords block count mismatch: {obs.count(old_block)}")
obs = obs.replace(old_block, new_block, 1)

if run.count(old_candidate) != 1:
    raise SystemExit(f"runner candidate count mismatch: {run.count(old_candidate)}")
run = run.replace(old_candidate, new_candidate, 1)

old_root = 'ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"'
new_root = 'ROOT="${AERIS_FIX2_ROOT:?AERIS_FIX2_ROOT missing}"'
if run.count(old_root) != 1:
    raise SystemExit(f"runner ROOT count mismatch: {run.count(old_root)}")
run = run.replace(old_root, new_root, 1)

old_compile = '    "    <Compile Include=\\"Terrain\\\\AERIS39MapSoPipelineIsolationDiagnosticObserver.cs\\" />\\n"'
new_compile = '    "    <Compile Include=\\"/tmp/AERIS39MapSoPipelineIsolationDiagnosticObserver.Fix2.cs\\" />\\n"'
if run.count(old_compile) != 1:
    raise SystemExit(f"runner compile include count mismatch: {run.count(old_compile)}")
run = run.replace(old_compile, new_compile, 1)

old_provenance = 'diagnostic_observer_source_sha256=$(sha256sum "$SRC/Terrain/AERIS39MapSoPipelineIsolationDiagnosticObserver.cs" | awk \'{print $1}\')'
new_provenance = 'diagnostic_observer_source_sha256=$(sha256sum "/tmp/AERIS39MapSoPipelineIsolationDiagnosticObserver.Fix2.cs" | awk \'{print $1}\')'
if run.count(old_provenance) != 1:
    raise SystemExit(f"runner provenance count mismatch: {run.count(old_provenance)}")
run = run.replace(old_provenance, new_provenance, 1)

run = run.replace(
    'OUT="$ARTIFACT_ROOT/AERIS39_MAPSO3E_Pipeline_Isolation"',
    'OUT="$ARTIFACT_ROOT/AERIS39_MAPSO3E_FIX2_Pipeline_Isolation"',
    1)
run = run.replace(
    'ARCHIVE="$ARTIFACT_ROOT/AERIS39_MAPSO3E_Pipeline_Isolation.tar.gz"',
    'ARCHIVE="$ARTIFACT_ROOT/AERIS39_MAPSO3E_FIX2_Pipeline_Isolation.tar.gz"',
    1)
run = run.replace(
    'STATE_DIR="$HOME/.cache/AERIS/mapso3e-pipeline-isolation/$KEY"',
    'STATE_DIR="$HOME/.cache/AERIS/mapso3e-fix2-pipeline-isolation/$KEY"',
    1)
run = run.replace(
    'tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_MAPSO3E_Pipeline_Isolation',
    'tar -C "$ARTIFACT_ROOT" -czf "$ARCHIVE" AERIS39_MAPSO3E_FIX2_Pipeline_Isolation',
    1)

old_build_gate = '''[[ -f "$BUILD_DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 20; }\nrm -f "$TMP_CSPROJ"\n'''
new_build_gate = '''[[ -f "$BUILD_DLL" ]] || { echo "STOP: build returned without DLL" >&2; exit 20; }\npython3 - "$BUILD_DLL" "$CANDIDATE" <<'PY_AERIS39_FIX2_DLL'\nfrom pathlib import Path\nimport sys\ndata = Path(sys.argv[1]).read_bytes()\nmarker = sys.argv[2]\nif marker.encode("utf-8") not in data and marker.encode("utf-16le") not in data:\n    raise SystemExit("STOP: Fix2 observer candidate marker missing from built DLL")\nprint("AERIS39_MAPSO3E_FIX2_DLL_EMBED=PASS")\nPY_AERIS39_FIX2_DLL\nrm -f "$TMP_CSPROJ"\n'''
if run.count(old_build_gate) != 1:
    raise SystemExit(f"runner build gate count mismatch: {run.count(old_build_gate)}")
run = run.replace(old_build_gate, new_build_gate, 1)

old_wait = '''  if ! grep -Fq "[AERIS39][MAPSO3E_COMPLETE]" "$segment"; then\n    rm -f "$segment"\n    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"\n    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."\n    return 0\n  fi\n'''
new_wait = '''  if ! grep -Fq "[AERIS39][MAPSO3E_COMPLETE]" "$segment"; then\n    if grep -Fq "[AERIS39][MAPSO3E_BOOT]" "$segment"; then\n      echo "observer_boot_seen=true"\n      echo "diagnostic_state=BOOT_SEEN_COMPLETE_NOT_SEEN"\n      grep '\\[AERIS39\\]\\[MAPSO3E_' "$segment" || true\n    else\n      echo "observer_boot_seen=false"\n      echo "diagnostic_state=OBSERVER_NOT_SEEN_IN_LOG"\n    fi\n    rm -f "$segment"\n    echo "AERIS_CURRENT_STAGE=WAITING_FOR_KSP"\n    echo "human_action=Launch KSP to Main Menu, exit KSP, then run the same command again."\n    return 0\n  fi\n'''
if run.count(old_wait) != 1:
    raise SystemExit(f"runner wait block count mismatch: {run.count(old_wait)}")
run = run.replace(old_wait, new_wait, 1)

observer_out.write_text(obs, encoding="utf-8")
runner_out.write_text(run, encoding="utf-8")
print("AERIS39_MAPSO3E_FIX2_PATCH=PASS")
