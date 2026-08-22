#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS30 REV3.5 R025 SIMPLE BUILD]'
R022 = 'AERIS30_REV3_5_SALBUTAMOL_SULFATE_R022_NEXT_EXACT_VISIBLE_BOUNDED_PREWARM'
R023 = 'AERIS30_REV3_5_SALBUTAMOL_SULFATE_R023_BOUNDED_PREWARM_ADMISSION_PACING'
R024 = 'AERIS30_REV3_5_SALBUTAMOL_SULFATE_R024_EXACT_VISIBLE_COMMIT_PREEMPTION'
R025 = 'AERIS30_REV3_5_SALBUTAMOL_SULFATE_R025_RESIDUAL_STALL_ATTRIBUTION_OBSERVER'


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


parser = argparse.ArgumentParser(description=(
    'Simplified AERIS R025 development build. Directly compiles the current C# source '
    'and installs the DLL. No applicators, historical replay, selftests, clean, reset, '
    'stash, or lineage reconstruction.'))
parser.add_argument('ksp_path')
args = parser.parse_args()

ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' FAIL: KSP path not found: ' + str(ksp))

src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
renderer = src / 'Terrain/AERISTerrainGpuTileRenderer.cs'
if not csproj.is_file() or not renderer.is_file():
    raise SystemExit(PREFIX + ' FAIL: required source missing')

source_text = renderer.read_text(encoding='utf-8')
observer = src / 'Terrain/AERISR017NdPresentationStallObserver.cs'
if not observer.is_file():
    raise SystemExit(PREFIX + ' FAIL: R017 observer missing')
observer_text = observer.read_text(encoding='utf-8')

if R025 in observer_text and R024 in source_text:
    source_revision = R025
    source_stage = 'R025'
elif R024 in source_text:
    source_revision = R024
    source_stage = 'R024_BASELINE'
elif R023 in source_text:
    source_revision = R023
    source_stage = 'R023_BASELINE'
elif R022 in source_text:
    source_revision = R022
    source_stage = 'R022_BASELINE'
else:
    raise SystemExit(PREFIX + ' FAIL: neither R022 baseline nor R023 marker found')

branch = output(['git', 'branch', '--show-current'])
head = output(['git', 'rev-parse', 'HEAD'])
dirty = output(['git', 'status', '--porcelain'], fallback='')
print(PREFIX + ' source_stage=' + source_stage)
print(PREFIX + ' source_revision=' + source_revision)
print(PREFIX + ' branch=' + branch)
print(PREFIX + ' head=' + head)
print(PREFIX + ' worktree_dirty=' + ('1' if dirty else '0'))
print(PREFIX + ' applicators=NO historical_replay=NO selftests=NO clean=NO')

run(['xbuild', '/p:Configuration=Release', '/p:KSPDIR=' + str(ksp), csproj])

built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' FAIL: DLL missing after compiler success')
repo_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed_dll = ksp / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
repo_dll.parent.mkdir(parents=True, exist_ok=True)
installed_dll.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(built), str(repo_dll))
shutil.copy2(str(built), str(installed_dll))

built_sha = sha256(built)
if built_sha != sha256(repo_dll) or built_sha != sha256(installed_dll):
    raise SystemExit(PREFIX + ' FAIL: DLL SHA mismatch after install')

identity = (
    'build_mode=AERIS30_R025_SIMPLIFIED_DEVELOPMENT\n'
    'source_stage=' + source_stage + '\n'
    'source_revision=' + source_revision + '\n'
    'git_branch=' + branch + '\n'
    'git_head=' + head + '\n'
    'worktree_dirty=' + ('1' if dirty else '0') + '\n'
    'dll_sha256=' + built_sha + '\n'
)
repo_identity = ROOT / 'GameData/AERISFlightControl/AERISSimplifiedBuildIdentity.txt'
installed_identity = ksp / 'GameData/AERISFlightControl/AERISSimplifiedBuildIdentity.txt'
repo_identity.parent.mkdir(parents=True, exist_ok=True)
installed_identity.parent.mkdir(parents=True, exist_ok=True)
repo_identity.write_text(identity, encoding='utf-8')
shutil.copy2(str(repo_identity), str(installed_identity))

print('[PASS] compiler completed successfully')
print('[PASS] repo/installed DLL SHA match')
print('[PASS] simplified identity written')
print(PREFIX + ' PASS')
print('dll_sha256=' + built_sha)
print('NOTE: fully exit and restart KSP after DLL replacement; plugin assemblies are not hot-reloaded.')
