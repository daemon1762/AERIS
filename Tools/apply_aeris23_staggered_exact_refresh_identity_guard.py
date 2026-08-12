#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]

base=ROOT/'Tools/apply_aeris23_witness_affine_identity_guard.py'
if not base.is_file():
    raise SystemExit('[AERIS23 STAGGER IDENTITY] affine identity guard missing')
subprocess.run([sys.executable,str(base)],cwd=str(ROOT),check=True)

build=ROOT/'build_ubuntu.sh'
text=build.read_text()
old='CANDIDATE_NAME="AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION"'
new='CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
if old in text:
    text=text.replace(old,new,1)
elif new not in text:
    raise SystemExit('[AERIS23 STAGGER IDENTITY] candidate-name anchor missing')
old_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_witness_affine_source.py"'
new_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
if old_verify in text:
    text=text.replace(old_verify,new_verify,1)
elif new_verify not in text:
    raise SystemExit('[AERIS23 STAGGER IDENTITY] verifier anchor missing')
build.write_text(text)
print('[AERIS23 STAGGER IDENTITY] candidate=AERIS23_AFFINE_STAGGERED_EXACT_REFRESH')
print('[AERIS23 STAGGER IDENTITY] build preflight now requires staggered exact-refresh source')