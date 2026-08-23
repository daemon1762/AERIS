#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R031 PTC GRAPH BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
PARENT = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'
MARKER = 'AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW'

def run(args):
    args=[str(x) for x in args]; print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)

def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()

def sha256(path):
    h=hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda:f.read(1024*1024), b''): h.update(block)
    return h.hexdigest()

def tree_stats(path):
    if not path.is_dir(): return (False,0,0)
    files=[p for p in path.rglob('*') if p.is_file()]; total=0
    for p in files:
        try: total += p.stat().st_size
        except OSError: pass
    return (True,len(files),total)

def marker_in_bytes(data,text):
    return text.encode() in data or text.encode('utf-16le') in data

parser=argparse.ArgumentParser(description='R031 runtime PQS source graph shadow probe build/install')
parser.add_argument('ksp_path'); args=parser.parse_args()
ksp=Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir(): raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))
branch=output(['git','branch','--show-current'])
if branch != BRANCH: raise SystemExit(PREFIX + ' wrong branch: ' + branch)
parent_source=ROOT/'Source/AERISFlightControl/Terrain/AERISPtcSourceResolver.cs'
if not parent_source.is_file() or PARENT not in parent_source.read_text():
    raise SystemExit(PREFIX + ' parent R031 shadow is not materialized; run the base R031 build once first')

frozen=[
    ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',
    ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
    ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
    ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
    ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
    ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
]
pre={str(p):sha256(p) for p in frozen}
run([sys.executable, ROOT/'Tools/apply_aeris32_rev3_5_r031_runtime_source_graph_probe.py'])
run([sys.executable, ROOT/'Tools/apply_aeris32_rev3_5_r031_runtime_source_graph_identity_sync.py'])
run([sys.executable, ROOT/'Tools/verify_aeris32_rev3_5_r031_runtime_source_graph_probe.py'])
for p in frozen:
    if sha256(p) != pre[str(p)]: raise SystemExit(PREFIX + ' altered frozen R030 source: ' + str(p))
print(PREFIX + ' PASS frozen R030 persistence/DB/renderer sources unchanged')

src=ROOT/'Source/AERISFlightControl'; csproj=src/'AERISFlightControl.csproj'
run(['xbuild','/p:Configuration=Release','/p:KSPDIR='+str(ksp),csproj])
built=src/'bin/Release/AERISFlightControl.dll'
if not built.is_file(): raise SystemExit(PREFIX + ' built DLL missing')
repo_mod=ROOT/'GameData/AERISFlightControl'; repo_plugins=repo_mod/'Plugins'; repo_plugins.mkdir(parents=True,exist_ok=True)
repo_dll=repo_plugins/'AERISFlightControl.dll'; shutil.copy2(str(built),str(repo_dll))
repo_shaders=repo_mod/'Shaders'; historical=ROOT.parent/'AERIS'/'GameData/AERISFlightControl/Shaders'
if repo_shaders.is_dir(): shader_source=repo_shaders; shader_provenance='R031_REPOSITORY'
elif historical.is_dir(): shader_source=historical; shader_provenance='R029_HISTORICAL_WORKTREE'
else: raise SystemExit(PREFIX + ' shader bundle missing')
shader_files=[p for p in shader_source.rglob('*') if p.is_file()]
if not shader_files: raise SystemExit(PREFIX + ' shader source empty')
installed_mod=ksp/'GameData/AERISFlightControl'; pdata=installed_mod/'PluginData'
before_exists,before_files,before_bytes=tree_stats(pdata)
settings=installed_mod/'Config/AERISSettings.cfg'; settings_backup=settings.read_bytes() if settings.is_file() else None
installed_mod.parent.mkdir(parents=True,exist_ok=True)
shutil.copytree(str(repo_mod),str(installed_mod),dirs_exist_ok=True)
shutil.copytree(str(shader_source),str(installed_mod/'Shaders'),dirs_exist_ok=True)
if settings_backup is not None:
    settings.parent.mkdir(parents=True,exist_ok=True); settings.write_bytes(settings_backup)
after_exists,after_files,after_bytes=tree_stats(pdata)
if before_exists and (not after_exists or before_files != after_files or before_bytes != after_bytes):
    raise SystemExit(PREFIX + ' PluginData changed during overlay')
installed_dll=installed_mod/'Plugins/AERISFlightControl.dll'; dll=installed_dll.read_bytes()
if sha256(repo_dll) != sha256(installed_dll): raise SystemExit(PREFIX + ' DLL SHA mismatch')
for token,label in ((MARKER,'graph marker'),('[R031][PTC_GRAPH] event=COMPLETE','graph summary'),('authority=PQS','PQS authority')):
    if not marker_in_bytes(dll,token): raise SystemExit(PREFIX + ' installed DLL missing ' + label)
head=output(['git','rev-parse','HEAD']); dll_sha=sha256(installed_dll)
identity=(
    'aeris_candidate='+MARKER+'\nbranch='+BRANCH+'\nsource_head='+head+'\n'
    'parent='+PARENT+'\nvariant='+MARKER+'\nruntime_change=PTC_RUNTIME_SOURCE_GRAPH_SHADOW_ONLY\n'
    'terrain_db_write=NO\nproducer_switch=NO\nterrain_authority=PQS\ngpu=NO\ncertification=NO_SHADOW_ONLY\n'
    'install_mode=OVERLAY_PRESERVE_PLUGINDATA_AND_USER_SETTINGS\n'
    'plugin_data_before_files='+str(before_files)+'\nplugin_data_before_bytes='+str(before_bytes)+'\n'
    'plugin_data_after_files='+str(after_files)+'\nplugin_data_after_bytes='+str(after_bytes)+'\n'
    'shader_provenance='+shader_provenance+'\nshader_file_count='+str(len(shader_files))+'\n'
    'dll_sha256='+dll_sha+'\n')
(repo_mod/'AERISCandidateBuildIdentity.txt').write_text(identity)
(installed_mod/'AERISCandidateBuildIdentity.txt').write_text(identity)
print(PREFIX + ' PASS')
print('branch='+branch); print('head='+head)
print('runtime_change=PTC_RUNTIME_SOURCE_GRAPH_SHADOW_ONLY')
print('terrain_db_write=NO producer_switch=NO authority=PQS certification=NO')
print('plugin_data_files='+str(after_files)); print('plugin_data_bytes='+str(after_bytes))
print('dll_sha256='+dll_sha)
print('TEST: fully restart KSP to Main Menu; wait for [R031][PTC_GRAPH] event=COMPLETE; no Flight required.')
