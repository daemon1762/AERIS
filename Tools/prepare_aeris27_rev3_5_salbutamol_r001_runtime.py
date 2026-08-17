#!/usr/bin/env python3
from pathlib import Path
import argparse
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001 CANONICAL RUNTIME]'
MARKER = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


parser = argparse.ArgumentParser(
    description='Canonical REV3.5 R001 runtime preparation for the accepted REV003 Observer M1 lineage.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
ready = False
if renderer.is_file():
    ready = MARKER in renderer.read_text()

if not ready:
    # Reuse the validated AERIS26 REV003 platform/shader/runtime reconstruction path.
    run([sys.executable,
         ROOT / 'Tools/prepare_aeris26_rev003_observer_runtime_hotfix.py', ksp])
    # Apply only the REV003 successor-name compatibility adaptation. This keeps the
    # REV003 Acquire/Vertices/Colours/Indices/Finalize mesh stages intact.
    run([sys.executable,
         ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_resumable_prepare_r001.py'])

# Existing R001 runtime preparer now sees the marker, skips baseline reconstruction,
# re-runs the idempotent generator, verifies, builds, installs, and checks DLL identity.
run([sys.executable,
     ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_runtime.py', ksp])

print(PREFIX + ' PASS')
print('rev3_5_variant=' + MARKER)
print('base=REV003_OBSERVER_M1')
print('worker_prepare=0 speculative=0 presentation_cache=0')
