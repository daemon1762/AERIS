#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R031 PTC SHADOW BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r031-ptc-source-resolver-cpu-file-exact'
R030_FIX1 = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'
R030_FIX2 = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'
R031 = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def tree_stats(path):
    if not path.is_dir(): return (False, 0, 0)
    files = [p for p in path.rglob('*') if p.is_file()]
    total = 0
    for p in files:
        try: total += p.stat().st_size
        except OSError: pass
    return (True, len(files), total)


def marker_in_bytes(data, text):
    return text.encode() in data or text.encode('utf-16le') in data

parser = argparse.ArgumentParser(description='R031 PTC source resolver + CPU FILE_EXACT shadow build. Preserves R030 V5 DB exactly.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))
branch = output(['git','branch','--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

builder = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
tile = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
phase0 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
fix1obs = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030Fix1PersistenceObserver.cs'

# Materialize the accepted R030 persistence chain only when absent. The Phase0 verifier
# must run before Fix1 mutates persistence paths.
if not builder.is_file() or R030_FIX1 not in builder.read_text():
    if not phase0.is_file():
        run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix1_stable_persistent_terrain_identity.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r030_fix1_stable_persistent_terrain_identity.py'])
if R030_FIX2 not in builder.read_text():
    run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix2_persistence_telemetry_cleanup.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r030_fix2_persistence_telemetry_cleanup.py'])
# Parent build identity hotfix is harmless and keeps intermediate identity coherent.
run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix2_hotfix1_build_identity_sync.py'])

# Freeze accepted R030 persistence/DB/renderer sources across R031 materialization.
frozen = [
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs',
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
]
pre_r031 = {str(p): sha256(p) for p in frozen}

run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r031_ptc_source_resolver_cpu_file_exact_shadow.py'])
run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r031_hotfix1_normalize_hint_compile.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r031_ptc_source_resolver_cpu_file_exact_shadow.py'])
for p in frozen:
    actual = sha256(p)
    if actual != pre_r031[str(p)]:
        raise SystemExit(PREFIX + ' R031 altered frozen R030 source: ' + str(p))
print(PREFIX + ' PASS frozen R030 persistence/DB/renderer sources unchanged')

src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
run(['xbuild','/p:Configuration=Release','/p:KSPDIR=' + str(ksp), csproj])
built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file(): raise SystemExit(PREFIX + ' built DLL missing')

repo_mod = ROOT / 'GameData/AERISFlightControl'
repo_plugins = repo_mod / 'Plugins'
repo_plugins.mkdir(parents=True, exist_ok=True)
repo_dll = repo_plugins / 'AERISFlightControl.dll'
shutil.copy2(str(built), str(repo_dll))

repo_shaders = repo_mod / 'Shaders'
historical_shaders = ROOT.parent / 'AERIS' / 'GameData/AERISFlightControl/Shaders'
if repo_shaders.is_dir():
    shader_source = repo_shaders; shader_provenance = 'R031_REPOSITORY'
elif historical_shaders.is_dir():
    shader_source = historical_shaders; shader_provenance = 'R029_HISTORICAL_WORKTREE'
else:
    raise SystemExit(PREFIX + ' shader bundle missing; refusing incomplete install')
shader_files = [p for p in shader_source.rglob('*') if p.is_file()]
if not shader_files: raise SystemExit(PREFIX + ' shader source empty')

installed_mod = ksp / 'GameData/AERISFlightControl'
plugin_data = installed_mod / 'PluginData'
before_exists, before_files, before_bytes = tree_stats(plugin_data)
settings_path = installed_mod / 'Config/AERISSettings.cfg'
settings_backup = settings_path.read_bytes() if settings_path.is_file() else None

installed_mod.parent.mkdir(parents=True, exist_ok=True)
shutil.copytree(str(repo_mod), str(installed_mod), dirs_exist_ok=True)
shutil.copytree(str(shader_source), str(installed_mod / 'Shaders'), dirs_exist_ok=True)
if settings_backup is not None:
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_bytes(settings_backup)
after_exists, after_files, after_bytes = tree_stats(plugin_data)
if before_exists and (not after_exists or before_files != after_files or before_bytes != after_bytes):
    raise SystemExit(PREFIX + ' PluginData changed during overlay: before=' +
        str((before_files,before_bytes)) + ' after=' + str((after_files,after_bytes)))

installed_dll = installed_mod / 'Plugins/AERISFlightControl.dll'
repo_sha = sha256(repo_dll); installed_sha = sha256(installed_dll)
if repo_sha != installed_sha: raise SystemExit(PREFIX + ' DLL SHA mismatch')
dll = installed_dll.read_bytes()
for token, label in ((R031,'R031 marker'),('[R031][PTC_RESOLVE]','resolver telemetry'),
    ('certification=NO_SHADOW_ONLY','shadow certification guard'),('authority=PQS','PQS authority')):
    if not marker_in_bytes(dll, token):
        raise SystemExit(PREFIX + ' installed DLL missing ' + label)

head = output(['git','rev-parse','HEAD'])
identity = (
    'aeris_candidate=' + R031 + '\n'
    'branch=' + BRANCH + '\n'
    'source_head=' + head + '\n'
    'parent=AERIS32_R030_FIX2_HOTFIX1_RUNTIME_ACCEPTED\n'
    'variant=' + R031 + '\n'
    'runtime_change=PTC_SHADOW_OBSERVER_ONLY\n'
    'terrain_db_write=NO\n'
    'producer_switch=NO\n'
    'terrain_authority=PQS\n'
    'gpu=NO\n'
    'cpu_decoders=PGM_P2_P5_RAW16_LE_SQUARE\n'
    'certification=NO_SHADOW_ONLY\n'
    'install_mode=OVERLAY_PRESERVE_PLUGINDATA_AND_USER_SETTINGS\n'
    'plugin_data_before_files=' + str(before_files) + '\n'
    'plugin_data_before_bytes=' + str(before_bytes) + '\n'
    'plugin_data_after_files=' + str(after_files) + '\n'
    'plugin_data_after_bytes=' + str(after_bytes) + '\n'
    'shader_provenance=' + shader_provenance + '\n'
    'shader_file_count=' + str(len(shader_files)) + '\n'
    'dll_sha256=' + installed_sha + '\n')
(repo_mod / 'AERISCandidateBuildIdentity.txt').write_text(identity)
(installed_mod / 'AERISCandidateBuildIdentity.txt').write_text(identity)

print(PREFIX + ' PASS')
print('branch=' + branch)
print('head=' + head)
print('runtime_change=PTC_SHADOW_OBSERVER_ONLY')
print('terrain_db_write=NO producer_switch=NO terrain_authority=PQS gpu=NO')
print('plugin_data_preserved=' + str(before_exists))
print('plugin_data_files=' + str(after_files))
print('plugin_data_bytes=' + str(after_bytes))
print('shader_provenance=' + shader_provenance)
print('dll_sha256=' + installed_sha)
print('TEST: fully restart KSP to Main Menu; wait 15-30 s for [R031][PTC] SHADOW_COMPLETE; do not delete PluginData.')
