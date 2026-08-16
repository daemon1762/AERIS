#!/usr/bin/env python3
from pathlib import Path
import argparse, hashlib, subprocess, sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
CANDIDATE='AERIS25_MAIN_THREAD_COMMIT_GOVERNOR'
OH_CODENAME='NOREPINEPHRINE'
OH_REVISION='OH_PHASE6_004'
PREFIX='[AERIS25 NOREPINEPHRINE REV004 RUNTIME]'

def run(args):
    args=[str(x) for x in args]
    print(PREFIX+' $ '+' '.join(args))
    subprocess.run(args,cwd=str(ROOT),check=True)

def sha256(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(1024*1024),b''):h.update(block)
    return h.hexdigest()

def phase6_4_present():
    try:
        r=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
        m=(ROOT/'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
        u=(ROOT/'build_ubuntu.sh').read_text()
    except OSError:return False
    return ('AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in r and
            'internal const string Revision = "OH_PHASE6_004";' in m and
            'verify_aeris25_managed_preparation_pipeline_hotfix.py' in u)

parser=argparse.ArgumentParser(description='Prepare/install AERIS25-3 NOREPINEPHRINE OH_PHASE6_004 Managed Preparation Pipeline runtime.')
parser.add_argument('ksp_path')
parser.add_argument('--rebuild-shader',action='store_true')
parser.add_argument('--unity-editor',default='')
args=parser.parse_args()
ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():raise SystemExit(PREFIX+' KSP path not found: '+str(ksp))

# Reuse the already-validated rev003 reconstruction/build helper as the parent generator.
# This may perform one rev003 compile/install on a raw tree; rev004 immediately rebuilds
# and replaces it before this command returns. On a generated rev004 tree it is skipped.
if not phase6_4_present():
    parent=[sys.executable,ROOT/'Tools/prepare_aeris25_main_thread_commit_governor_rev003_runtime.py',ksp]
    if args.rebuild_shader:parent.append('--rebuild-shader')
    if args.unity_editor:parent.extend(['--unity-editor',args.unity_editor])
    run(parent)
    run([sys.executable,ROOT/'Tools/apply_aeris25_managed_preparation_pipeline_hotfix.py'])
else:
    print(PREFIX+' generated Phase6_004 tree already present; rev003 parent reconstruction skipped')

run([sys.executable,ROOT/'Tools/fix_aeris25_phase6_002_inherited_selftests.py'])
run([sys.executable,ROOT/'Tools/fix_aeris25_phase6_003_inherited_selftests.py'])
run([sys.executable,ROOT/'Tools/fix_aeris25_phase6_004_inherited_selftests.py'])
run([sys.executable,ROOT/'Tools/verify_aeris25_managed_preparation_pipeline_hotfix.py'])
run([sys.executable,ROOT/'Tools/verify_aeris25_persistent_presentation_batching.py'])
run([sys.executable,ROOT/'Tools/run_v01800_operation_health_pass3_prebuild.py'])
run(['git','diff','--check'])
run(['bash',ROOT/'build_ubuntu.sh',ksp])

source_dll=ROOT/'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed=ksp/'GameData/AERISFlightControl'
installed_dll=installed/'Plugins/AERISFlightControl.dll'
identity=installed/'AERISCandidateBuildIdentity.txt'
config=installed/'Config/AERISOperationHealth.cfg'
for path in (source_dll,installed_dll,identity,config):
    if not path.is_file():raise SystemExit(PREFIX+' installed artifact missing: '+str(path))
identity_text=identity.read_text(errors='replace');config_text=config.read_text(errors='replace')
checks=[(sha256(source_dll)==sha256(installed_dll),'built/installed DLL SHA'),
        (('candidate='+CANDIDATE) in identity_text,'Phase 6 candidate identity'),
        (('codename = '+OH_CODENAME) in config_text,'NOREPINEPHRINE installed config identity')]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok:failed.append(label)
if failed:raise SystemExit(PREFIX+' INSTALL IDENTITY FAIL: '+', '.join(failed))
print(PREFIX+' INSTALL IDENTITY MATCH=YES')
print('candidate='+CANDIDATE)
print('oh_codename='+OH_CODENAME)
print('oh_revision='+OH_REVISION)
print('dll_sha256='+sha256(installed_dll))
print('Correctness gate: snapshot_stale_mesh=0; deferred retirement remains bounded and drains.')
print('Rev004 gate: managed_prep_submitted/completed rise; CPU fallback lazy alloc should remain 0 while GPU projection is ACTIVE.')
print('GC gate: main_commit_geo_max should collapse; inspect managed_prep_bytes_peak/total and GC events versus rev003.')
print('Test after install PASS only: 20 -> 40 -> 80 -> 160 km, then 160 km Track-Up strong turn and steady cruise.')
