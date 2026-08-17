#!/usr/bin/env python3
from pathlib import Path
import argparse
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001 COMPILE HOTFIX1 RUNTIME]'
MARKER = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def r001_present():
    try:
        return MARKER in R.read_text()
    except OSError:
        return False


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS27 REV3.5 SALBUTAMOL SULFATE R001 Compile Hotfix1. Repairs the REV003 YieldPendingEntryCommit call contract without changing R001 performance semantics.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

# A clean checkout contains the historical generated baseline rather than the R001 overlay.
# Reuse the already validated REV003 Observer M1 reconstruction path before applying Hotfix1.
if not r001_present():
    print(PREFIX + ' R001 generated tree absent; reconstructing frozen REV003 Observer M1 parent')
    run([sys.executable,
         ROOT / 'Tools/prepare_aeris26_rev003_observer_runtime_hotfix.py', ksp])
else:
    print(PREFIX + ' existing R001 generated tree detected')

run([sys.executable,
     ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r001_compile_hotfix1.py'])

# The normal R001 runtime preparer now sees an existing corrected R001 tree. Its legacy
# generator exits as already-present, then all normal static checks/build/install/identity
# checks execute unchanged.
run([sys.executable,
     ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_runtime.py', ksp])

print(PREFIX + ' INSTALL PASS')
print('compile_hotfix=AERIS27_R001_COMPILE_HOTFIX1')
print('fix=YieldPendingEntryCommit_REV003_3ARG')
print('scope=compile-contract-only; R001 resumable Prepare semantics unchanged')
