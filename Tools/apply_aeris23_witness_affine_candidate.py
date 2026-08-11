#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
steps=[
    ROOT/'Tools/apply_aeris23_single_authority_candidate.py',
    ROOT/'Tools/apply_aeris23_witness_bounded_affine_projection.py',
    ROOT/'Tools/apply_aeris23_witness_affine_identity_guard.py',
    ROOT/'Tools/verify_aeris23_witness_affine_source.py',
]
for step in steps:
    if not step.is_file():
        raise SystemExit('[AERIS23 AFFINE] required candidate step missing: '+str(step))
    print('[AERIS23 AFFINE] running '+step.name)
    subprocess.run([sys.executable,str(step)],cwd=str(ROOT),check=True)
print('[AERIS23 AFFINE] WITNESS_BOUNDED_AFFINE_PROJECTION candidate fully applied and source-verified')
print('Next: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py')
print('Then: git diff --check')
print('Build only with ./build_ubuntu.sh <KSP_PATH>; require MATCH=YES before runtime test.')