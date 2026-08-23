#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,shutil,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
PREFIX='[AERIS32 R031 GILLY DEP BUILD]'
BRANCH='agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
PARENT='AERIS32_REV3_5_R031_PTC_GILLY_IL_DISASSEMBLY_SHADOW'
MARKER='AERIS32_REV3_5_R031_PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW'
def run(a): print(PREFIX+' $ '+' '.join(str(x) for x in a)); subprocess.run([str(x) for x in a],cwd=str(ROOT),check=True)
def out(a): return subprocess.check_output([str(x) for x in a],cwd=str(ROOT),text=True).strip()
def sha(p):
 h=hashlib.sha256();
 with p.open('rb') as f:
  for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
 return h.hexdigest()
def stats(p):
 if not p.is_dir(): return (False,0,0)
 fs=[x for x in p.rglob('*') if x.is_file()]; return (True,len(fs),sum(x.stat().st_size for x in fs))
def marker(data,s): return s.encode() in data or s.encode('utf-16le') in data
ap=argparse.ArgumentParser();ap.add_argument('ksp_path');args=ap.parse_args();ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir(): raise SystemExit(PREFIX+' KSP path missing')
if out(['git','branch','--show-current'])!=BRANCH: raise SystemExit(PREFIX+' wrong branch')
parent=ROOT/'Source/AERISFlightControl/Terrain/AERISR031PtcGillyIlDisassemblyObserver.cs'; csproj=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
if not parent.is_file() or PARENT not in parent.read_text():
 run([sys.executable,ROOT/'Tools/build_aeris32_rev3_5_r031_gilly_il_disassembly_shadow.py',str(ksp)])
else:
 p=parent.read_text(); c=csproj.read_text(); checks=[('[R031][PTC_GILLY_IL] event=DISASSEMBLY_COMPLETE' in p,'parent IL summary'),('AERISR031PtcGillyIlDisassemblyObserver.cs' in c,'parent compiled'),('worker_invokes_runtime_object=false' in p,'parent worker safety')]
 bad=[label for ok,label in checks if not ok]
 for ok,label in checks: print(('[PASS] ' if ok else '[FAIL] ')+label)
 if bad: raise SystemExit(PREFIX+' invalid parent: '+', '.join(bad))
 print(PREFIX+' PASS existing parent Gilly IL materialization reused')
frozen=[ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs']
before={str(p):sha(p) for p in frozen}
run([sys.executable,ROOT/'Tools/apply_aeris32_rev3_5_r031_gilly_dependency_closure_probe.py'])
run([sys.executable,ROOT/'Tools/verify_aeris32_rev3_5_r031_gilly_dependency_closure_probe.py'])
for p in frozen:
 if sha(p)!=before[str(p)]: raise SystemExit(PREFIX+' frozen R030 source changed '+str(p))
print(PREFIX+' PASS frozen R030 persistence/DB/renderer unchanged')
src=ROOT/'Source/AERISFlightControl';run(['xbuild','/p:Configuration=Release','/p:KSPDIR='+str(ksp),src/'AERISFlightControl.csproj'])
built=src/'bin/Release/AERISFlightControl.dll';repo=ROOT/'GameData/AERISFlightControl';(repo/'Plugins').mkdir(parents=True,exist_ok=True);shutil.copy2(str(built),str(repo/'Plugins/AERISFlightControl.dll'))
hist=ROOT.parent/'AERIS/GameData/AERISFlightControl/Shaders';shaders=repo/'Shaders' if (repo/'Shaders').is_dir() else hist
if not shaders.is_dir(): raise SystemExit(PREFIX+' shaders missing')
installed=ksp/'GameData/AERISFlightControl';pdata=installed/'PluginData';bex,bfiles,bbytes=stats(pdata);settings=installed/'Config/AERISSettings.cfg';sb=settings.read_bytes() if settings.is_file() else None
installed.parent.mkdir(parents=True,exist_ok=True);shutil.copytree(str(repo),str(installed),dirs_exist_ok=True);shutil.copytree(str(shaders),str(installed/'Shaders'),dirs_exist_ok=True)
if sb is not None: settings.parent.mkdir(parents=True,exist_ok=True);settings.write_bytes(sb)
aex,afiles,abytes=stats(pdata)
if bex and (not aex or bfiles!=afiles or bbytes!=abytes): raise SystemExit(PREFIX+' PluginData changed')
dll=installed/'Plugins/AERISFlightControl.dll';data=dll.read_bytes()
for token in (MARKER,'[R031][PTC_GILLY_DEP] event=DEPENDENCY_CLOSURE_COMPLETE','worker_invokes_runtime_object=false','authority=PQS'):
 if not marker(data,token): raise SystemExit(PREFIX+' DLL missing '+token)
head=out(['git','rev-parse','HEAD']);identity=('aeris_candidate='+MARKER+'\nbranch='+BRANCH+'\nsource_head='+head+'\nparent='+PARENT+'\nruntime_change=PTC_GILLY_DEPENDENCY_CLOSURE_SHADOW_ONLY\nworker_invokes_runtime_object=NO\nterrain_db_write=NO\nproducer_switch=NO\nterrain_authority=PQS\ngpu=NO\ncertification=NO_SHADOW_ONLY\nplugin_data_before_files='+str(bfiles)+'\nplugin_data_before_bytes='+str(bbytes)+'\nplugin_data_after_files='+str(afiles)+'\nplugin_data_after_bytes='+str(abytes)+'\ndll_sha256='+sha(dll)+'\n')
(repo/'AERISCandidateBuildIdentity.txt').write_text(identity);(installed/'AERISCandidateBuildIdentity.txt').write_text(identity)
print(PREFIX+' PASS');print('head='+head);print('plugin_data_files='+str(afiles));print('plugin_data_bytes='+str(abytes));print('dll_sha256='+sha(dll));print('TEST: restart KSP to Main Menu and wait for [R031][PTC_GILLY_DEP] event=DEPENDENCY_CLOSURE_COMPLETE')
