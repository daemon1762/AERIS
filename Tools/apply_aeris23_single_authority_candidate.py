#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
steps = [
    ROOT / 'Tools/apply_aeris23_single_authority_terrain_pack_successor.py',
    ROOT / 'Tools/apply_aeris23_candidate_identity_guard.py',
    ROOT / 'Tools/verify_aeris23_single_authority_source.py',
    ROOT / 'Tools/fix_aeris23_single_authority_pass3_selftest.py',
    ROOT / 'Tools/fix_aeris23_single_authority_pass2_selftest.py',
]
for step in steps:
    if not step.is_file():
        raise SystemExit('[AERIS23] required candidate step missing: ' + str(step))
    print('[AERIS23] running ' + step.name)
    subprocess.run([sys.executable, str(step)], cwd=str(ROOT), check=True)

print('[AERIS23] SINGLE_AUTHORITY_TERRAIN_PACK candidate fully applied and source-verified')
print('Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py')
print('Then: git diff --check')
print('Build only with ./build_ubuntu.sh <KSP_PATH>; build now refuses a non-candidate source tree.')
