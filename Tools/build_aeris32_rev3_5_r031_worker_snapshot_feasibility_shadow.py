#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX='[AERIS32 R031 WORKER SNAPSHOT BUILD]'
BRANCH='agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
PARENT='AERIS32_REV3_5_R031_PTC_CPU_RECONSTRUCTION_FEASIBILITY_SHADOW'
MARKER='AERIS32_REV3_5_R031_PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW'

def run(args):
    print(PREFIX+' $ '+' '.join(str(x) for x in args))
    subprocess.run([str(x) for x in args],cwd=str(ROOT),check=True)
def out(args):
    return subprocess.check_output([str(x) for x in args],cwd=str(ROOT),text=True).strip()
def sha(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()
def stats(path):
    if not path.is_dir(): return (False,0,0)
    fs=[p for p in path.rglob('*') if p.is_file()]
    return (True,len(fs),sum(p.stat().st_size for p in fs))
def marker(data,text):
    return text.encode() in data or text.encode('utf-16le') in data

ap=argparse.ArgumentParser(); ap.add_argument('ksp_path'); args=ap.parse_args()
ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir(): raise SystemExit(PREFIX+' KSP path missing')
branch=out(['git','branch','--show-current'])
if branch!=BRANCH: raise SystemExit(PREFIX+' wrong branch '+branch)

parent_source=ROOT/'Source/AERISFlightControl/Terrain/AERISR031PtcCpuReconstructionFeasibilityObserver.cs'
csproj=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
if not parent_source.is_file() or PARENT not in parent_source.read_text():
    print(PREFIX+' parent CPU feasibility materialization absent; building parent once')
    run([sys.executable,ROOT/'Tools/build_aeris32_rev3_5_r031_cpu_reconstruction_feasibility_shadow.py',str(ksp)])
else:
    ptext=parent_source.read_text(); ctext=csproj.read_text()
    parent_checks=[
        ('[R031][PTC_CPU_MOD]' in ptext,'parent mod telemetry'),
        ('[R031][PTC_CPU_BODY]' in ptext,'parent body telemetry'),
        ('[R031][PTC_CPU] event=FEASIBILITY_COMPLETE' in ptext,'parent summary telemetry'),
        ('authority=PQS' in ptext,'parent PQS authority'),
        ('AERISR031PtcCpuReconstructionFeasibilityObserver.cs' in ctext,'parent csproj registration')]
    failed=[label for ok,label in parent_checks if not ok]
    for ok,label in parent_checks: print(('[PASS] ' if ok else '[FAIL] ')+label)
    if failed: raise SystemExit(PREFIX+' invalid parent CPU materialization: '+', '.join(failed))
    print(PREFIX+' PASS existing parent CPU materialization reused; parent rebuild skipped')

frozen=[ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',
        ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
        ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
        ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
        ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
        ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs']
before={str(p):sha(p) for p in frozen}
run([sys.executable,ROOT/'Tools/apply_aeris32_rev3_5_r031_worker_snapshot_feasibility_probe.py'])
run([sys.executable,ROOT/'Tools/verify_aeris32_rev3_5_r031_worker_snapshot_feasibility_probe.py'])
for p in frozen:
    if sha(p)!=before[str(p)]: raise SystemExit(PREFIX+' frozen R030 source changed '+str(p))
print(PREFIX+' PASS frozen R030 persistence/DB/renderer sources unchanged')

src=ROOT/'Source/AERISFlightControl'; csproj=src/'AERISFlightControl.csproj'
run(['xbuild','/p:Configuration=Release','/p:KSPDIR='+str(ksp),csproj])
built=src/'bin/Release/AERISFlightControl.dll'
if not built.is_file(): raise SystemExit(PREFIX+' DLL missing')
repo=ROOT/'GameData/AERISFlightControl'; (repo/'Plugins').mkdir(parents=True,exist_ok=True)
shutil.copy2(str(built),str(repo/'Plugins/AERISFlightControl.dll'))
hist=ROOT.parent/'AERIS/GameData/AERISFlightControl/Shaders'
shaders=repo/'Shaders' if (repo/'Shaders').is_dir() else hist
if not shaders.is_dir(): raise SystemExit(PREFIX+' shaders missing')
installed=ksp/'GameData/AERISFlightControl'; pdata=installed/'PluginData'
bex,bfiles,bbytes=stats(pdata)
settings=installed/'Config/AERISSettings.cfg'; sb=settings.read_bytes() if settings.is_file() else None
installed.parent.mkdir(parents=True,exist_ok=True)
shutil.copytree(str(repo),str(installed),dirs_exist_ok=True)
shutil.copytree(str(shaders),str(installed/'Shaders'),dirs_exist_ok=True)
if sb is not None:
    settings.parent.mkdir(parents=True,exist_ok=True); settings.write_bytes(sb)
aex,afiles,abytes=stats(pdata)
if bex and (not aex or bfiles!=afiles or bbytes!=abytes):
    raise SystemExit(PREFIX+' PluginData changed before='+str((bfiles,bbytes))+' after='+str((afiles,abytes)))

dll=installed/'Plugins/AERISFlightControl.dll'; data=dll.read_bytes()
for token in (MARKER,'[R031][PTC_SNAPSHOT] event=FEASIBILITY_COMPLETE',
    'worker_invokes_runtime_object=false','authority=PQS'):
    if not marker(data,token): raise SystemExit(PREFIX+' DLL missing '+token)
head=out(['git','rev-parse','HEAD'])
identity=(
'aeris_candidate='+MARKER+'\nbranch='+BRANCH+'\nsource_head='+head+'\n'
'parent='+PARENT+'\n'
'runtime_change=PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW_ONLY\n'
'worker_invokes_runtime_object=NO\nterrain_db_write=NO\nproducer_switch=NO\nterrain_authority=PQS\ngpu=NO\ncertification=NO_SHADOW_ONLY\n'
'plugin_data_before_files='+str(bfiles)+'\nplugin_data_before_bytes='+str(bbytes)+'\n'
'plugin_data_after_files='+str(afiles)+'\nplugin_data_after_bytes='+str(abytes)+'\n'
'dll_sha256='+sha(dll)+'\n')
(repo/'AERISCandidateBuildIdentity.txt').write_text(identity)
(installed/'AERISCandidateBuildIdentity.txt').write_text(identity)
print(PREFIX+' PASS')
print('head='+head)
print('runtime_change=PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW_ONLY')
print('worker_invokes_runtime_object=NO')
print('terrain_db_write=NO producer_switch=NO terrain_authority=PQS gpu=NO certification=NO')
print('plugin_data_files='+str(afiles)); print('plugin_data_bytes='+str(abytes)); print('dll_sha256='+sha(dll))
print('TEST: fully restart KSP to Main Menu and wait for [R031][PTC_SNAPSHOT] event=FEASIBILITY_COMPLETE')
