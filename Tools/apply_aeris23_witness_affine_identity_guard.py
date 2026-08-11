#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]

# First install the already-validated generic DLL/source identity machinery.
base=ROOT/'Tools/apply_aeris23_candidate_identity_guard.py'
if not base.is_file():
    raise SystemExit('[AERIS23 AFFINE IDENTITY] base identity guard missing')
subprocess.run([sys.executable,str(base)],cwd=str(ROOT),check=True)

build=ROOT/'build_ubuntu.sh'
text=build.read_text()
old='CANDIDATE_NAME="AERIS23_SINGLE_AUTHORITY_TERRAIN_PACK"'
new='CANDIDATE_NAME="AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION"'
if old in text:
    text=text.replace(old,new,1)
elif new not in text:
    raise SystemExit('[AERIS23 AFFINE IDENTITY] candidate-name anchor missing')
old_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_single_authority_source.py"'
new_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_witness_affine_source.py"'
if old_verify in text:
    text=text.replace(old_verify,new_verify,1)
elif new_verify not in text:
    raise SystemExit('[AERIS23 AFFINE IDENTITY] verifier anchor missing')
build.write_text(text)
print('[AERIS23 AFFINE IDENTITY] candidate=AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION')
print('[AERIS23 AFFINE IDENTITY] build preflight now requires Witness-Bounded Affine source')