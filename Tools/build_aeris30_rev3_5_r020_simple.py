#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS30 REV3.5 R020 SIMPLE BUILD]'
R020 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args, fallback='UNKNOWN'):
    try:
        return subprocess.check_output(
            [str(x) for x in args], cwd=str(ROOT), text=True,
            stderr=subprocess.DEVNULL).strip() or fallback
    except Exception:
        return fallback


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


parser = argparse.ArgumentParser(
    description=(
        'Simplified AERIS REV3.5 R020 build. Compiles the CURRENT materialized C# '
        'source, installs the DLL, verifies SHA equality, and writes minimal build '
        'metadata. No applicators, historical verifier replay, selftests, clean, '
        'reset, stash, or candidate-lineage reconstruction are performed.'))
parser.add_argument('ksp_path', help='Kerbal Space Program root directory')
args = parser.parse_args()

ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' FAIL: KSP path not found: ' + str(ksp))

src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
renderer = src / 'Terrain/AERISTerrainGpuTileRenderer.cs'
tile_system = src / 'Terrain/AERISTerrainTileSystem.cs'

for path in (csproj, renderer, tile_system):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL: source missing: ' + str(path))

# Only one semantic precondition: this must actually be the materialized R020 source.
# Syntax, type, member, namespace and reference mistakes belong to the C# compiler.
renderer_text = renderer.read_text(encoding='utf-8')
tile_text = tile_system.read_text(encoding='utf-8')
if R020 not in renderer_text or R020 not in tile_text:
    raise SystemExit(
        PREFIX + ' FAIL: current source is not materialized R020; refusing to label it R020')

branch = output(['git', 'branch', '--show-current'])
head = output(['git', 'rev-parse', 'HEAD'])
dirty = output(['git', 'status', '--porcelain'], fallback='')

print(PREFIX + ' mode=CURRENT_SOURCE_DIRECT_COMPILE')
print(PREFIX + ' branch=' + branch)
print(PREFIX + ' head=' + head)
print(PREFIX + ' worktree_dirty=' + ('1' if dirty else '0'))
print(PREFIX + ' historical_replay=NO applicators=NO selftests=NO clean=NO')

# Compiler is the build gate. Preserve bin/obj for a fast incremental development loop.
run([
    'xbuild',
    '/p:Configuration=Release',
    '/p:KSPDIR=' + str(ksp),
    csproj,
])

built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' FAIL: compiler reported success but DLL is missing')

repo_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed_dll = ksp / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
repo_dll.parent.mkdir(parents=True, exist_ok=True)
installed_dll.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(built), str(repo_dll))
shutil.copy2(str(built), str(installed_dll))

built_sha = sha256(built)
repo_sha = sha256(repo_dll)
installed_sha = sha256(installed_dll)
if not (built_sha == repo_sha == installed_sha):
    raise SystemExit(PREFIX + ' FAIL: DLL SHA mismatch after install')

# Minimal metadata only. The historical AERISCandidateBuildIdentity.txt is deliberately
# not required or rewritten by the simplified path.
identity_text = (
    'build_mode=AERIS30_R020_SIMPLIFIED_CURRENT_SOURCE\n'
    'revision=' + R020 + '\n'
    'git_branch=' + branch + '\n'
    'git_head=' + head + '\n'
    'worktree_dirty=' + ('1' if dirty else '0') + '\n'
    'dll_sha256=' + installed_sha + '\n'
)
repo_identity = ROOT / 'GameData/AERISFlightControl/AERISSimplifiedBuildIdentity.txt'
installed_identity = ksp / 'GameData/AERISFlightControl/AERISSimplifiedBuildIdentity.txt'
repo_identity.parent.mkdir(parents=True, exist_ok=True)
installed_identity.parent.mkdir(parents=True, exist_ok=True)
repo_identity.write_text(identity_text, encoding='utf-8')
shutil.copy2(str(repo_identity), str(installed_identity))

print('[PASS] current source carries R020 marker')
print('[PASS] compiler completed successfully')
print('[PASS] repo/installed DLL SHA match')
print('[PASS] minimal simplified identity written')
print(PREFIX + ' PASS')
print('dll_sha256=' + installed_sha)
print('NOTE: fully exit and restart KSP after DLL replacement; plugin assemblies are not hot-reloaded.')
