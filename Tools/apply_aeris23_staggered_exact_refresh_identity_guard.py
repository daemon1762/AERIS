#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]

base=ROOT/'Tools/apply_aeris23_witness_affine_identity_guard.py'
if not base.is_file():
    raise SystemExit('[AERIS23 STAGGER IDENTITY] affine identity guard missing')
subprocess.run([sys.executable,str(base)],cwd=str(ROOT),check=True)


def active_count(text,line):
    target=line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip()==target)


def replace_active_line(text,old,new,label):
    old_count=active_count(text,old)
    new_count=active_count(text,new)
    if old_count==1 and new_count==0:
        lines=text.splitlines(True)
        for i,raw in enumerate(lines):
            if raw.strip()==old.strip():
                ending='\n' if raw.endswith('\n') else ''
                indent=raw[:len(raw)-len(raw.lstrip())]
                lines[i]=indent+new.strip()+ending
                return ''.join(lines)
        raise SystemExit('[AERIS23 STAGGER IDENTITY] '+label+': active line disappeared')
    if old_count==0 and new_count==1:
        return text
    raise SystemExit('[AERIS23 STAGGER IDENTITY] '+label+': expected one active old or new line; old=%d new=%d' % (old_count,new_count))

build=ROOT/'build_ubuntu.sh'
text=build.read_text()
old='CANDIDATE_NAME="AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION"'
new='CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
text=replace_active_line(text,old,new,'candidate-name promotion')
old_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_witness_affine_source.py"'
new_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
text=replace_active_line(text,old_verify,new_verify,'build verifier promotion')
build.write_text(text)

# Historical comments may legitimately contain old candidate names. Only executable
# assignment/command lines are authority.
verified=build.read_text()
if active_count(verified,new)!=1 or active_count(verified,old)!=0:
    raise SystemExit('[AERIS23 STAGGER IDENTITY] FATAL: executable Affine/Stagger candidate identity is mixed')
if active_count(verified,new_verify)!=1 or active_count(verified,old_verify)!=0:
    raise SystemExit('[AERIS23 STAGGER IDENTITY] FATAL: executable build verifier is mixed')
print('[AERIS23 STAGGER IDENTITY] candidate=AERIS23_AFFINE_STAGGERED_EXACT_REFRESH')
print('[AERIS23 STAGGER IDENTITY] build preflight now requires staggered exact-refresh source')
print('[AERIS23 STAGGER IDENTITY] active-line identity check PASS')
