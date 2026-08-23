#!/usr/bin/env python3
from pathlib import Path
import argparse,hashlib,shutil,subprocess,sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[0]
if ROOT.name=='Tools': ROOT=ROOT.parent
PREFIX='[AERIS33 R037 POL FLATTEN OCEAN EXACT CLOSURE BUILD]'
BRANCH='agent/aeris33-rev3-5-r037-pol-flatten-ocean-exact-closure'
PARENT='AERIS33_REV3_5_R036_PTC_COMMON_PURE_CPU_EXACT_FORMULA_CLOSURE_SHADOW'
MARKER='AERIS33_REV3_5_R037_PTC_POL_FLATTEN_OCEAN_EXACT_CLOSURE_SHADOW'
FLATTEN_IL_SHA='4b00ff62f5a99eeae99d7236b16a0aa1dfed1d22a6c9cc991d6da38fce55a112'
def run(a):
    print(PREFIX+' $ '+' '.join(str(x) for x in a))
    subprocess.run([str(x) for x in a],cwd=str(ROOT),check=True)
def out(a): return subprocess.check_output([str(x) for x in a],cwd=str(ROOT),text=True).strip()
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(1024*1024),b''): h.update(b)
    return h.hexdigest()
def stats(p):
    if not p.is_dir(): return(False,0,0)
    fs=[x for x in p.rglob('*') if x.is_file()]
    return(True,len(fs),sum(x.stat().st_size for x in fs))
def marker(data,s): return s.encode() in data or s.encode('utf-16le') in data
ap=argparse.ArgumentParser();ap.add_argument('ksp_path');args=ap.parse_args()
ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir(): raise SystemExit(PREFIX+' KSP path missing')
if out(['git','branch','--show-current'])!=BRANCH: raise SystemExit(PREFIX+' wrong branch')
parent=ROOT/'Source/AERISFlightControl/Terrain/AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs'
csproj=ROOT/'Source/AERISFlightControl/AERISFlightControl.csproj'
version=ROOT/'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
if not parent.is_file() or PARENT not in version.read_text() or 'AERISR036PtcCommonPureCpuExactFormulaClosureObserver.cs' not in csproj.read_text():
    raise SystemExit(PREFIX+' accepted R036 materialization missing. Do NOT reset/clean/stash. Materialize/build accepted R036 first, then return to R037.')
print(PREFIX+' PASS accepted R036 materialization reused')
frozen=[
ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',
ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs']
before={str(p):sha(p) for p in frozen}
run([sys.executable,ROOT/'Tools/apply_aeris33_rev3_5_r037_pol_flatten_ocean_exact_closure.py'])
run([sys.executable,ROOT/'Tools/verify_aeris33_rev3_5_r037_pol_flatten_ocean_exact_closure.py'])
for p in frozen:
    if sha(p)!=before[str(p)]: raise SystemExit(PREFIX+' frozen terrain source changed '+str(p))
print(PREFIX+' PASS frozen terrain sources unchanged')
src=ROOT/'Source/AERISFlightControl'
run(['xbuild','/p:Configuration=Release','/p:KSPDIR='+str(ksp),src/'AERISFlightControl.csproj'])
built=src/'bin/Release/AERISFlightControl.dll'
repo=ROOT/'GameData/AERISFlightControl'
(repo/'Plugins').mkdir(parents=True,exist_ok=True)
shutil.copy2(str(built),str(repo/'Plugins/AERISFlightControl.dll'))
hist=ROOT.parent/'AERIS/GameData/AERISFlightControl/Shaders'
shaders=repo/'Shaders' if (repo/'Shaders').is_dir() else hist
if not shaders.is_dir(): raise SystemExit(PREFIX+' shaders missing')
installed=ksp/'GameData/AERISFlightControl'
pdata=installed/'PluginData'
bex,bfiles,bbytes=stats(pdata)
settings=installed/'Config/AERISSettings.cfg'
sb=settings.read_bytes() if settings.is_file() else None
installed.parent.mkdir(parents=True,exist_ok=True)
shutil.copytree(str(repo),str(installed),dirs_exist_ok=True)
shutil.copytree(str(shaders),str(installed/'Shaders'),dirs_exist_ok=True)
if sb is not None:
    settings.parent.mkdir(parents=True,exist_ok=True);settings.write_bytes(sb)
aex,afiles,abytes=stats(pdata)
if bex and (not aex or bfiles!=afiles or bbytes!=abytes): raise SystemExit(PREFIX+' PluginData changed')
dll=installed/'Plugins/AERISFlightControl.dll';data=dll.read_bytes()
for token in (MARKER,'[R037][COMMON_CPU] event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE',
              'PQSMod_FlattenOcean','worker_invokes_runtime_object=false','authority=PQS'):
    if not marker(data,token): raise SystemExit(PREFIX+' DLL missing '+token)
head=out(['git','rev-parse','HEAD'])
identity=(
'aeris_candidate='+MARKER+'\n'
+'branch='+BRANCH+'\n'
+'source_head='+head+'\n'
+'parent='+PARENT+'\n'
+'runtime_change=POL_FLATTEN_OCEAN_EXACT_CLOSURE_SHADOW_ONLY\n'
+'targets=Minmus,Ike,Gilly,Pol\n'
+'expected_worker_ready=Gilly,Ike,Pol\n'
+'expected_formula_pending=Minmus\n'
+'flatten_ocean_formula=height_lt_oceanRad_then_oceanRad_else_height\n'
+'flatten_ocean_il_sha256='+FLATTEN_IL_SHA+'\n'
+'signed_simplex=R036_RUNTIME_EXACT_PLUS_R037_IL_CONFIRMATION\n'
+'landcontrol_guard=R035_GEOMETRY_INERT_RUNTIME_REVALIDATION\n'
+'snapshot_payload=PRIMITIVES_ONLY\n'
+'worker_invokes_runtime_object=NO\n'
+'terrain_db_write=NO\n'
+'producer_switch=NO\n'
+'terrain_authority=PQS\n'
+'gpu=NO\n'
+'certification=NO_SHADOW_ONLY\n'
+'plugin_data_before_files='+str(bfiles)+'\n'
+'plugin_data_before_bytes='+str(bbytes)+'\n'
+'plugin_data_after_files='+str(afiles)+'\n'
+'plugin_data_after_bytes='+str(abytes)+'\n'
+'dll_sha256='+sha(dll)+'\n')
(repo/'AERISCandidateBuildIdentity.txt').write_text(identity)
(installed/'AERISCandidateBuildIdentity.txt').write_text(identity)
print(PREFIX+' PASS')
print('head='+head)
print('plugin_data_files='+str(afiles))
print('plugin_data_bytes='+str(abytes))
print('dll_sha256='+sha(dll))
print('TEST: restart KSP to Main Menu and wait for [R037][COMMON_CPU] event=POL_FLATTEN_OCEAN_EXACT_WORKER_COMPLETE')
