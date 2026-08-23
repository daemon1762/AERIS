#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,shutil,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
PREFIX='[AERIS32 R033 PURE PROCEDURAL INVENTORY BUILD]'
BRANCH='agent/aeris33-rev3-5-r033-pure-procedural-dependency-inventory'
PARENT='AERIS32_REV3_5_R032_PTC_GILLY_PURE_CPU_EXACT_WORKER_SHADOW'
MARKER='AERIS32_REV3_5_R033_PTC_PURE_PROCEDURAL_DEPENDENCY_INVENTORY_SHADOW'
def run(a):print(PREFIX+' $ '+' '.join(str(x) for x in a));subprocess.run([str(x) for x in a],cwd=str(ROOT),check=True)
def out(a):return subprocess.check_output([str(x) for x in a],cwd=str(ROOT),text=True).strip()
def sha(p):
 h=hashlib.sha256()
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''):h.update(b)
 return h.hexdigest()
def stats(p):
 if not p.is_dir():return(False,0,0)
 fs=[x for x in p.rglob('*') if x.is_file()];return(True,len(fs),sum(x.stat().st_size for x in fs))
def marker(data,s):return s.encode() in data or s.encode('utf-16le') in data
ap=argparse.ArgumentParser();ap.add_argument('ksp_path');args=ap.parse_args();ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():raise SystemExit(PREFIX+' KSP path missing')
if out(['git','branch','--show-current'])!=BRANCH:raise SystemExit(PREFIX+' wrong branch')
parent=ROOT/'Source/AERISFlightControl/Terrain/AERISR032PtcGillyPureCpuExactWorkerObserver.cs';csproj=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
if not parent.is_file() or PARENT not in parent.read_text() or 'AERISR032PtcGillyPureCpuExactWorkerObserver.cs' not in csproj.read_text():
 raise SystemExit(PREFIX+' accepted R032 materialization missing. Do NOT reset/clean/stash. Switch back to the accepted R032 branch, materialize/build R032 once, then return to R033.')
print(PREFIX+' PASS accepted R032 materialization reused')
frozen=[ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs']
before={str(p):sha(p) for p in frozen}
run([sys.executable,ROOT/'Tools/apply_aeris32_rev3_5_r033_pure_procedural_dependency_inventory.py'])
run([sys.executable,ROOT/'Tools/verify_aeris32_rev3_5_r033_pure_procedural_dependency_inventory.py'])
for p in frozen:
 if sha(p)!=before[str(p)]:raise SystemExit(PREFIX+' frozen R030 source changed '+str(p))
print(PREFIX+' PASS frozen R030 persistence/DB/renderer unchanged')
src=ROOT/'Source/AERISFlightControl';run(['xbuild','/p:Configuration=Release','/p:KSPDIR='+str(ksp),src/'AERISFlightControl.csproj'])
built=src/'bin/Release/AERISFlightControl.dll';repo=ROOT/'GameData/AERISFlightControl';(repo/'Plugins').mkdir(parents=True,exist_ok=True);shutil.copy2(str(built),str(repo/'Plugins/AERISFlightControl.dll'))
hist=ROOT.parent/'AERIS/GameData/AERISFlightControl/Shaders';shaders=repo/'Shaders' if (repo/'Shaders').is_dir() else hist
if not shaders.is_dir():raise SystemExit(PREFIX+' shaders missing')
installed=ksp/'GameData/AERISFlightControl';pdata=installed/'PluginData';bex,bfiles,bbytes=stats(pdata);settings=installed/'Config/AERISSettings.cfg';sb=settings.read_bytes() if settings.is_file() else None
installed.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(str(repo),str(installed),dirs_exist_ok=True);shutil.copytree(str(shaders),str(installed/'Shaders'),dirs_exist_ok=True)
if sb is not None:settings.parent.mkdir(parents=True,exist_ok=True);settings.write_bytes(sb)
aex,afiles,abytes=stats(pdata)
if bex and (not aex or bfiles!=afiles or bbytes!=abytes):raise SystemExit(PREFIX+' PluginData changed')
dll=installed/'Plugins/AERISFlightControl.dll';data=dll.read_bytes()
for token in (MARKER,'[R033][PTC_PROC] event=PURE_PROCEDURAL_INVENTORY_COMPLETE','worker_dispatch=false','worker_invokes_runtime_object=false','authority=PQS'):
 if not marker(data,token):raise SystemExit(PREFIX+' DLL missing '+token)
head=out(['git','rev-parse','HEAD']);identity=('aeris_candidate='+MARKER+'\nbranch='+BRANCH+'\nsource_head='+head+'\nparent='+PARENT+'\nruntime_change=PTC_PURE_PROCEDURAL_DEPENDENCY_INVENTORY_SHADOW_ONLY\ninventory_targets=Minmus,Ike,Gilly,Pol\nworker_dispatch=NO\nworker_invokes_runtime_object=NO\nterrain_db_write=NO\nproducer_switch=NO\nterrain_authority=PQS\ngpu=NO\ncertification=NO_SHADOW_ONLY\nplugin_data_before_files='+str(bfiles)+'\nplugin_data_before_bytes='+str(bbytes)+'\nplugin_data_after_files='+str(afiles)+'\nplugin_data_after_bytes='+str(abytes)+'\ndll_sha256='+sha(dll)+'\n')
(repo/'AERISCandidateBuildIdentity.txt').write_text(identity);(installed/'AERISCandidateBuildIdentity.txt').write_text(identity)
print(PREFIX+' PASS');print('head='+head);print('plugin_data_files='+str(afiles));print('plugin_data_bytes='+str(abytes));print('dll_sha256='+sha(dll));print('TEST: restart KSP to Main Menu and wait for [R033][PTC_PROC] event=PURE_PROCEDURAL_INVENTORY_COMPLETE')
