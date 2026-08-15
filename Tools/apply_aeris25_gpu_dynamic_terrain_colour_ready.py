#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_writebytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = "AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR"
CODENAME = "ATRO" + "PINE"
REVISION = "OH_PHASE4_001"


def run(path):
    print("[AERIS25 GPU DYNAMIC COLOUR READY] $ " + str(path))
    subprocess.run([sys.executable, str(path)], cwd=str(ROOT), check=True)


def core_ready():
    try:
        monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
        renderer = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
        backend = (ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs").read_text()
        shader = (ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader").read_text()
        build = (ROOT / "build_ubuntu.sh").read_text()
    except OSError:
        return False
    return all((
        ('internal const string Codename = "' + CODENAME + '";') in monitor,
        ('internal const string Revision = "' + REVISION + '";') in monitor,
        ('internal const string Candidate = "' + CANDIDATE + '";') in monitor,
        'oh_gpu_dynamic_colour=' in renderer,
        'SetUVs(2, gpuDynamicTerrainSemanticScratch)' in renderer,
        'DynamicTerrainSemanticModeId' in backend,
        '_AerisTerrainSemanticMode' in shader,
        ('CANDIDATE_NAME="' + CANDIDATE + '"') in build,
    ))


if not core_ready():
    fixer = ROOT / "Tools/fix_aeris25_gpu_dynamic_colour_anchors.py"
    base = ROOT / "Tools/apply_aeris25_gpu_dynamic_terrain_colour.py"
    if not fixer.is_file() or not base.is_file():
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR READY] fixer/base applicator missing")
    run(fixer)
    run(base)
else:
    print("[AERIS25 GPU DYNAMIC COLOUR READY] core ATROPINE tree already present; reconstruction skipped")

renderer = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
text = renderer.read_text()
semantic_accounting = '''                (packedTerrainSource == null ? 0L :
                    packedTerrainSource.LongLength * (3L * 4L)) +'''
if semantic_accounting not in text:
    anchor = '''                packedTerrainIndexCount * 4L +'''
    if text.count(anchor) != 1:
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR READY] packed terrain index-accounting anchor mismatch")
    text = text.replace(anchor, anchor + "\n" + semantic_accounting, 1)
    renderer.write_text(text)
    print("[AERIS25 GPU DYNAMIC COLOUR READY] added independent TEXCOORD2 float3 semantic accounting (+12 bytes/packed vertex)")
else:
    print("[AERIS25 GPU DYNAMIC COLOUR READY] TEXCOORD2 semantic accounting already present")

build_path = ROOT / "build_ubuntu.sh"
build = build_path.read_text()
old_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_dynamic_terrain_colour.py"'
new_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py"'
old_count = sum(1 for line in build.splitlines() if line.strip() == old_verify)
new_count = sum(1 for line in build.splitlines() if line.strip() == new_verify)
if old_count == 1 and new_count == 0:
    lines = build.splitlines(True)
    for i, line in enumerate(lines):
        if line.strip() == old_verify:
            indent = line[:len(line) - len(line.lstrip())]
            ending = "\n" if line.endswith("\n") else ""
            lines[i] = indent + new_verify + ending
            break
    build_path.write_text("".join(lines))
elif old_count == 0 and new_count == 1:
    pass
else:
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR READY] build verifier anchor mismatch old=%d new=%d" %
                     (old_count, new_count))

verifier = ROOT / "Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py"
if not verifier.is_file():
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR READY] hardened verifier missing")
run(verifier)
subprocess.run(["git", "diff", "--check"], cwd=str(ROOT), check=True)
print("[AERIS25 GPU DYNAMIC COLOUR READY] STATIC READY PASS")
print("candidate=" + CANDIDATE)
print("oh_codename=" + CODENAME)
print("oh_revision=" + REVISION)
