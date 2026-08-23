#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS32 R030 BUILD]'
BRANCH = 'agent/aeris32-rev3-5-r030-preload-persistence-ptc-phase0'
MARKER = 'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output([str(x) for x in args], cwd=str(ROOT), text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode() in data or text.encode('utf-16le') in data

parser = argparse.ArgumentParser(description='AERIS32 R030 observer-only build/install. Preserves PluginData between repeat installs.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

# Materialize only R030 Phase0 observation files.
run([sys.executable, ROOT / 'Tools/apply_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris32_rev3_5_r030_preload_persistence_ptc_phase0.py'])

src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
run(['xbuild', '/p:Configuration=Release', '/p:KSPDIR=' + str(ksp), csproj])

built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' built DLL missing: ' + str(built))
repo_mod = ROOT / 'GameData/AERISFlightControl'
repo_plugins = repo_mod / 'Plugins'
repo_plugins.mkdir(parents=True, exist_ok=True)
repo_dll = repo_plugins / 'AERISFlightControl.dll'
shutil.copy2(str(built), str(repo_dll))

# The accepted R029 clean Git branch did not contain the previously materialized shader
# bundle. Preserve the historical dirty worktree as an explicit asset source until that
# bundle is formally imported into Git. Never silently install an AERIS tree without it.
repo_shaders = repo_mod / 'Shaders'
historical_shaders = ROOT.parent / 'AERIS' / 'GameData/AERISFlightControl/Shaders'
if repo_shaders.is_dir():
    shader_source = repo_shaders
    shader_provenance = 'R030_REPOSITORY'
elif historical_shaders.is_dir():
    shader_source = historical_shaders
    shader_provenance = 'R029_HISTORICAL_WORKTREE'
else:
    raise SystemExit(PREFIX + ' shader bundle missing in both clean repo and historical ~/AERIS worktree; refusing incomplete install')
shader_files = [p for p in shader_source.rglob('*') if p.is_file()]
if not shader_files:
    raise SystemExit(PREFIX + ' shader source exists but contains no files: ' + str(shader_source))

installed_mod = ksp / 'GameData/AERISFlightControl'
# Overlay, never delete: repeat R030 installs must preserve PluginData/TerrainPreloadDatabaseV3
# so cold -> completed -> warm restart persistence can actually be measured.
installed_mod.parent.mkdir(parents=True, exist_ok=True)
shutil.copytree(str(repo_mod), str(installed_mod), dirs_exist_ok=True)
installed_shaders = installed_mod / 'Shaders'
shutil.copytree(str(shader_source), str(installed_shaders), dirs_exist_ok=True)

head = output(['git', 'rev-parse', 'HEAD'])
installed_dll = installed_mod / 'Plugins/AERISFlightControl.dll'
repo_sha = sha256(repo_dll)
installed_sha = sha256(installed_dll)
dll = installed_dll.read_bytes()
if repo_sha != installed_sha:
    raise SystemExit(PREFIX + ' repo/installed DLL SHA mismatch')
if not marker_in_bytes(dll, MARKER):
    raise SystemExit(PREFIX + ' installed DLL missing R030 marker')
if not marker_in_bytes(dll, '[R030][PRELOAD_PERSIST]'):
    raise SystemExit(PREFIX + ' installed DLL missing R030 persistence telemetry')

identity_text = (
    'aeris_candidate=AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0\n'
    'branch=' + BRANCH + '\n'
    'source_head=' + head + '\n'
    'variant=' + MARKER + '\n'
    'runtime_behavior_change=NONE_OBSERVER_ONLY\n'
    'shader_provenance=' + shader_provenance + '\n'
    'shader_source=' + str(shader_source) + '\n'
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
print('runtime_change=NONE observer/logging only')
print('install_mode=OVERLAY_PRESERVE_PLUGINDATA')
print('shader_provenance=' + shader_provenance)
print('shader_file_count=' + str(len(shader_files)))
print('dll_sha256=' + installed_sha)
print('installed=' + str(installed_mod))
print('NOTE: fully exit and restart KSP after DLL replacement; plugin assemblies are not hot-reloaded.')
