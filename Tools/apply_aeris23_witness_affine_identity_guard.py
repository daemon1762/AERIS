#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]


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
        raise SystemExit('[AERIS23 AFFINE IDENTITY] '+label+': active line disappeared')
    if old_count==0 and new_count==1:
        return text
    raise SystemExit('[AERIS23 AFFINE IDENTITY] '+label+': expected one active old or new line; old=%d new=%d' % (old_count,new_count))

build=ROOT/'build_ubuntu.sh'
bootstrap=ROOT/'Source/AERISFlightControl/Core/AERISBootstrap.cs'
text=build.read_text()
bootstrap_text=bootstrap.read_text()

# Install generic source/DLL/runtime identity machinery only when it is genuinely absent.
# Re-running the generic guard after a descendant candidate has renamed CANDIDATE_NAME would
# otherwise reinsert a second identity block at the SEMVER anchor.
generic_markers=(
    'SOURCE_GIT_SHA="$(git -C "$ROOT" rev-parse HEAD)"',
    'SOURCE_TREE_SHA256=',
    '[AERIS23_CANDIDATE_BUILT]',
    '[AERIS23_CANDIDATE_INSTALLED]',
)
runtime_markers=(
    'RuntimeAssemblySha256()',
    '[AERIS23_RUNTIME_CANDIDATE]',
)
generic_ready=all(marker in text for marker in generic_markers) and all(marker in bootstrap_text for marker in runtime_markers)
if not generic_ready:
    base=ROOT/'Tools/apply_aeris23_candidate_identity_guard.py'
    if not base.is_file():
        raise SystemExit('[AERIS23 AFFINE IDENTITY] base identity guard missing')
    subprocess.run([sys.executable,str(base)],cwd=str(ROOT),check=True)
    text=build.read_text()
else:
    print('[AERIS23 AFFINE IDENTITY] generic identity machinery already present; base guard not re-run')

old='CANDIDATE_NAME="AERIS23_SINGLE_AUTHORITY_TERRAIN_PACK"'
new='CANDIDATE_NAME="AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION"'
old_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_single_authority_source.py"'
new_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_witness_affine_source.py"'
text=replace_active_line(text,old,new,'candidate-name promotion')
text=replace_active_line(text,old_verify,new_verify,'build verifier promotion')
build.write_text(text)
verified=build.read_text()
if active_count(verified,new)!=1 or active_count(verified,old)!=0:
    raise SystemExit('[AERIS23 AFFINE IDENTITY] FATAL: executable Single-Authority/Affine candidate identity is mixed')
if active_count(verified,new_verify)!=1 or active_count(verified,old_verify)!=0:
    raise SystemExit('[AERIS23 AFFINE IDENTITY] FATAL: executable build verifier is mixed')
print('[AERIS23 AFFINE IDENTITY] candidate=AERIS23_WITNESS_BOUNDED_AFFINE_PROJECTION')
print('[AERIS23 AFFINE IDENTITY] build preflight now requires Witness-Bounded Affine source')
print('[AERIS23 AFFINE IDENTITY] active-line identity check PASS')
