#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,shutil,subprocess,sys
sys.dont_write_bytecode=True
R=Path(__file__).resolve().parents[1]; P='[AERIS32 R031 GILLY IL BUILD]'; B='agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'; M='AERIS32_REV3_5_R031_PTC_GILLY_IL_DISASSEMBLY_SHADOW'; PM='AERIS32_REV3_5_R031_PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW'
def run(a): print(P+' $ '+' '.join(map(str,a))); subprocess.run(list(map(str,a)),cwd=str(R),check=True)
def out(a): return subprocess.check_output(list(map(str,a)),cwd=str(R),text=True).strip()
def sha(p):
 h=hashlib.sha256(); f=p.open('rb')
 for x in iter(lambda:f.read(1048576),b''): h.update(x)
 f.close(); return h.hexdigest()
def stats(p):
 if not p.is_dir(): return (False,0,0)
 q=[x for x in p.rglob('*') if x.is_file()]; return (True,len(q),sum(x.stat().st_size for x in q))
def has(d,t): return t.encode() in d or t.encode('utf-16le') in d
ap=argparse.ArgumentParser(); ap.add_argument('ksp_path'); k=Path(ap.parse_args().ksp_path).expanduser().resolve()
if not k.is_dir() or out(['git','branch','--show-current'])!=B: raise SystemExit(P+' path/branch gate failed')
parent=R/'Source/AERISFlightControl/Terrain/AERISR031PtcGillyAlgorithmCalibrationObserver.cs'; cp=R/'Source/AERISFlightControl/AERISFlightControl.csproj'
if not parent.is_file() or PM not in parent.read_text(): run([sys.executable,R/'Tools/build_aeris32_rev3_5_r031_gilly_algorithm_calibration_shadow.py',k])
else:
 t=parent.read_text(); c=cp.read_text(); checks=[('[R031][PTC_GILLY_ALGO] event=CALIBRATION_COMPLETE' in t),('worker_invokes_runtime_object=false' in t),('AERISR031PtcGillyAlgorithmCalibrationObserver.cs' in c)]
 if not all(checks): raise SystemExit(P+' parent Gilly calibration contract failed')
 print(P+' PASS existing parent Gilly calibration materialization reused')
fz=[R/'Source/AERISFlightControl/Terrain'/x for x in ('AERISTerrainPreloadBuilder.cs','AERISTerrainTileSystem.cs','AERISTerrainPreloadDatabase.cs','AERISTerrainBlockPipeline.cs','AERISTerrainPreloadCodec.cs','AERISTerrainGpuTileRenderer.cs')]; before={str(x):sha(x) for x in fz}
run([sys.executable,R/'Tools/apply_aeris32_rev3_5_r031_gilly_il_disassembly_probe.py']); run([sys.executable,R/'Tools/verify_aeris32_rev3_5_r031_gilly_il_disassembly_probe.py'])
for x in fz:
 if sha(x)!=before[str(x)]: raise SystemExit(P+' frozen R030 source changed '+str(x))
S=R/'Source/AERISFlightControl'; run(['xbuild','/p:Configuration=Release','/p:KSPDIR='+str(k),S/'AERISFlightControl.csproj']); built=S/'bin/Release/AERISFlightControl.dll'
repo=R/'GameData/AERISFlightControl'; (repo/'Plugins').mkdir(parents=True,exist_ok=True); shutil.copy2(str(built),str(repo/'Plugins/AERISFlightControl.dll'))
sh=repo/'Shaders' if (repo/'Shaders').is_dir() else R.parent/'AERIS/GameData/AERISFlightControl/Shaders'; ins=k/'GameData/AERISFlightControl'; pd=ins/'PluginData'; _,bf,bb=stats(pd); st=ins/'Config/AERISSettings.cfg'; sb=st.read_bytes() if st.is_file() else None
ins.parent.mkdir(parents=True,exist_ok=True); shutil.copytree(str(repo),str(ins),dirs_exist_ok=True); shutil.copytree(str(sh),str(ins/'Shaders'),dirs_exist_ok=True)
if sb is not None: st.parent.mkdir(parents=True,exist_ok=True); st.write_bytes(sb)
_,af,ab=stats(pd)
if (bf,bb)!=(af,ab): raise SystemExit(P+' PluginData changed')
dll=ins/'Plugins/AERISFlightControl.dll'; d=dll.read_bytes()
for t in (M,'[R031][PTC_GILLY_IL] event=DISASSEMBLY_COMPLETE','worker_invokes_runtime_object=false','authority=PQS'):
 if not has(d,t): raise SystemExit(P+' DLL marker missing '+t)
head=out(['git','rev-parse','HEAD']); ds=sha(dll); ident='aeris_candidate='+M+'\nbranch='+B+'\nsource_head='+head+'\nparent='+PM+'\nruntime_change=PTC_GILLY_IL_DISASSEMBLY_SHADOW_ONLY\nruntime_object_invocation_thread=MAIN_THREAD_ONLY\nworker_invokes_runtime_object=NO\nterrain_db_write=NO\nproducer_switch=NO\nterrain_authority=PQS\ngpu=NO\ncertification=NO_SHADOW_ONLY\nplugin_data_before_files='+str(bf)+'\nplugin_data_before_bytes='+str(bb)+'\nplugin_data_after_files='+str(af)+'\nplugin_data_after_bytes='+str(ab)+'\ndll_sha256='+ds+'\n'
(repo/'AERISCandidateBuildIdentity.txt').write_text(ident); (ins/'AERISCandidateBuildIdentity.txt').write_text(ident)
print(P+' PASS'); print('head='+head); print('terrain_db_write=NO producer_switch=NO terrain_authority=PQS gpu=NO certification=NO'); print('plugin_data_files='+str(af)); print('plugin_data_bytes='+str(ab)); print('dll_sha256='+ds); print('TEST: restart KSP to Main Menu and wait for [R031][PTC_GILLY_IL] event=DISASSEMBLY_COMPLETE')