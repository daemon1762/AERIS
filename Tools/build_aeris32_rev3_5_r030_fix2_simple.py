#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R030 FIX2 BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r030-preload-persistence-ptc-phase0'
BASE_MARKER = 'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER'
FIX1 = 'AERIS32_REV3_5_R030_FIX1_STABLE_PERSISTENT_TERRAIN_IDENTITY'
MARKER = 'AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT),
        text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode() in data or text.encode('utf-16le') in data


def tree_stats(path):
    if not path.is_dir():
        return (False, 0, 0)
    files = [p for p in path.rglob('*') if p.is_file()]
    total = 0
    for p in files:
        try:
            total += p.stat().st_size
        except OSError:
            pass
    return (True, len(files), total)


parser = argparse.ArgumentParser(
    description='AERIS32 R030 Fix2 build/install. Keeps accepted Fix1 persistence behavior and cleans migration/Phase0 telemetry.')
parser.add_argument('ksp_path')
args = parser.parse_args()

ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

src = ROOT / 'Source/AERISFlightControl'
builder = src / 'Terrain/AERISTerrainPreloadBuilder.cs'
tile = src / 'Terrain/AERISTerrainTileSystem.cs'
base_observer = src / 'Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
fix1_observer = src / 'Terrain/AERISR030Fix1PersistenceObserver.cs'

fix2_materialized = builder.is_file() and base_observer.is_file() and \
    MARKER in builder.read_text() and MARKER in base_observer.read_text()

if not fix2_materialized:
    # Materialize the exact accepted lineage without installing between stages.
    if not base_observer.is_file() or BASE_MARKER not in base_observer.read_text():
        run([sys.executable,
            ROOT / 'Tools/apply_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])
        run([sys.executable,
            ROOT / 'Tools/verify_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])

    fix1_materialized = builder.is_file() and tile.is_file() and \
        FIX1 in builder.read_text() and FIX1 in tile.read_text() and \
        fix1_observer.is_file()
    if not fix1_materialized:
        run([sys.executable,
            ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix1_stable_persistent_terrain_identity.py'])
    run([sys.executable,
        ROOT / 'Tools/verify_aeris32_rev3_5_r030_fix1_stable_persistent_terrain_identity.py'])
    run([sys.executable,
        ROOT / 'Tools/apply_aeris32_rev3_5_r030_fix2_persistence_telemetry_cleanup.py'])
else:
    print(PREFIX + ' existing Fix2 materialization detected')

run([sys.executable,
    ROOT / 'Tools/verify_aeris32_rev3_5_r030_fix2_persistence_telemetry_cleanup.py'])

csproj = src / 'AERISFlightControl.csproj'
run(['xbuild', '/p:Configuration=Release',
    '/p:KSPDIR=' + str(ksp), csproj])

built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' built DLL missing: ' + str(built))

repo_mod = ROOT / 'GameData/AERISFlightControl'
repo_plugins = repo_mod / 'Plugins'
repo_plugins.mkdir(parents=True, exist_ok=True)
repo_dll = repo_plugins / 'AERISFlightControl.dll'
shutil.copy2(str(built), str(repo_dll))

repo_shaders = repo_mod / 'Shaders'
historical_shaders = ROOT.parent / 'AERIS' / 'GameData/AERISFlightControl/Shaders'
if repo_shaders.is_dir():
    shader_source = repo_shaders
    shader_provenance = 'R030_REPOSITORY'
elif historical_shaders.is_dir():
    shader_source = historical_shaders
    shader_provenance = 'R029_HISTORICAL_WORKTREE'
else:
    raise SystemExit(PREFIX +
        ' shader bundle missing in both clean repo and historical ~/AERIS worktree; refusing incomplete install')
shader_files = [p for p in shader_source.rglob('*') if p.is_file()]
if not shader_files:
    raise SystemExit(PREFIX + ' shader source exists but contains no files: ' +
        str(shader_source))

installed_mod = ksp / 'GameData/AERISFlightControl'
installed_plugin_data = installed_mod / 'PluginData'
before_exists, before_files, before_bytes = tree_stats(installed_plugin_data)
settings_path = installed_mod / 'Config/AERISSettings.cfg'
settings_backup = settings_path.read_bytes() if settings_path.is_file() else None

installed_mod.parent.mkdir(parents=True, exist_ok=True)
shutil.copytree(str(repo_mod), str(installed_mod), dirs_exist_ok=True)
shutil.copytree(str(shader_source), str(installed_mod / 'Shaders'),
    dirs_exist_ok=True)
if settings_backup is not None:
    settings_path.parent.mkdir(parents=True, exist_ok=True)
    settings_path.write_bytes(settings_backup)

after_exists, after_files, after_bytes = tree_stats(installed_plugin_data)
if before_exists and (not after_exists or before_files != after_files or
    before_bytes != after_bytes):
    raise SystemExit(PREFIX +
        ' PluginData changed during overlay install: before=' +
        str((before_files, before_bytes)) + ' after=' +
        str((after_files, after_bytes)))

installed_dll = installed_mod / 'Plugins/AERISFlightControl.dll'
repo_sha = sha256(repo_dll)
installed_sha = sha256(installed_dll)
if repo_sha != installed_sha:
    raise SystemExit(PREFIX + ' repo/installed DLL SHA mismatch')

dll = installed_dll.read_bytes()
for token, label in (
    (BASE_MARKER, 'R030 parent observer'),
    (FIX1, 'R030 Fix1 persistence authority'),
    (MARKER, 'R030 Fix2 telemetry cleanup'),
    ('[R030_FIX1][SUMMARY]', 'Fix1 summary telemetry'),
    ('log_policy=ONE_SHOT_PER_BODY', 'one-shot migration telemetry'),
    ('hash_authority=DIAGNOSTIC_ONLY', 'diagnostic Phase0 hash label'),
):
    if not marker_in_bytes(dll, token):
        raise SystemExit(PREFIX + ' installed DLL missing ' + label + ': ' + token)

head = output(['git', 'rev-parse', 'HEAD'])
identity_text = (
    'aeris_candidate=AERIS32_REV3_5_R030_FIX2_PERSISTENCE_TELEMETRY_CLEANUP\n'
    'branch=' + BRANCH + '\n'
    'source_head=' + head + '\n'
    'parent_variant=' + FIX1 + '\n'
    'variant=' + MARKER + '\n'
    'runtime_change=FIX1_PERSISTENCE_UNCHANGED_TELEMETRY_CLEANUP_ONLY\n'
    'state_format=V5_BACKWARD_READ_V4\n'
    'migration_log=ONE_SHOT_PER_BODY_PER_PROCESS\n'
    'phase0_state_reader=V4_V5\n'
    'phase0_runtime_hash=DIAGNOSTIC_ONLY\n'
    'install_mode=OVERLAY_PRESERVE_PLUGINDATA_AND_USER_SETTINGS\n'
    'plugin_data_before_files=' + str(before_files) + '\n'
    'plugin_data_before_bytes=' + str(before_bytes) + '\n'
    'plugin_data_after_files=' + str(after_files) + '\n'
    'plugin_data_after_bytes=' + str(after_bytes) + '\n'
    'shader_provenance=' + shader_provenance + '\n'
    'shader_file_count=' + str(len(shader_files)) + '\n'
    'dll_sha256=' + installed_sha + '\n'
)
repo_identity = repo_mod / 'AERISCandidateBuildIdentity.txt'
installed_identity = installed_mod / 'AERISCandidateBuildIdentity.txt'
repo_identity.write_text(identity_text)
installed_identity.write_text(identity_text)

print(PREFIX + ' PASS')
print('branch=' + branch)
print('head=' + head)
print('runtime_change=FIX1_PERSISTENCE_UNCHANGED_TELEMETRY_CLEANUP_ONLY')
print('state_format=V5_BACKWARD_READ_V4')
print('migration_log=ONE_SHOT_PER_BODY_PER_PROCESS')
print('phase0_state_reader=V4_V5')
print('plugin_data_preserved=' + str(before_exists))
print('plugin_data_files=' + str(after_files))
print('plugin_data_bytes=' + str(after_bytes))
print('shader_provenance=' + shader_provenance)
print('shader_file_count=' + str(len(shader_files)))
print('dll_sha256=' + installed_sha)
print('installed=' + str(installed_mod))
print('TEST: preserve PluginData; one normal warm KSP start is sufficient for Fix2 telemetry acceptance.')
