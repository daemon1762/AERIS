#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS29 REV3.5 R019 FAST BUILD]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
BRANCH = 'agent/aeris29-rev3-5-salbutamol-r019-visible-far-commit-priority'


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


parser = argparse.ArgumentParser(
    description='R019 development FAST build: no lineage replay, no full prebuild, no bin/obj clean, DLL-only install.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
if not renderer.is_file() or R018 not in renderer.read_text():
    raise SystemExit(PREFIX + ' exact materialized R018 renderer parent required; run from the successful R018 generated tree')

# Test-only successor compatibility first. This is idempotent and does not touch runtime.
run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_historical_verifier_successor_compat.py'])

# Overlay current revision once; applicator is idempotent.
run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py'])

# Small development gate. FORMAL remains responsible for complete lineage replay/prebuild.
for verifier in (
    'verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',
    'verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',
    'verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py',
    'verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py',
    'verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py',
    'verify_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py',
    'verify_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py',
    'selftest_v01800_oh_rev35_r019_visible_far_commit_priority.py',
):
    run([sys.executable, ROOT / 'Tools' / verifier])
run(['git', 'diff', '--check'])

# Frozen control-law areas must stay untouched even in the dirty materialized workspace.
frozen = [
    'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect',
    'Source/AERISFlightControl/Landing',
]
changed = output(['git', 'diff', '--name-only', 'HEAD', '--'] + frozen)
if changed:
    raise SystemExit(PREFIX + ' frozen control-law working-tree edits detected:\n' + changed)

# FAST deliberately does not remove bin/obj and does not invoke build_ubuntu.sh,
# because that entrypoint runs the full historical prebuild suite and package install.
src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
run(['xbuild', '/p:Configuration=Release', '/p:KSPDIR=' + str(ksp), csproj])

built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' built DLL missing: ' + str(built))
repo_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
repo_dll.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(built), str(repo_dll))

# Preserve the successful R018 formal identity and append only the development R019 line.
identity = ROOT / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
if not identity.is_file():
    raise SystemExit(PREFIX + ' R018 materialized candidate identity missing')
ident = identity.read_text()
if ('rev3_5_r018_variant=' + R018) not in ident:
    raise SystemExit(PREFIX + ' R018 identity parent missing')
r019_line = 'rev3_5_r019_variant=' + R019 + '\n'
if r019_line not in ident:
    if ident and not ident.endswith('\n'):
        ident += '\n'
    ident += r019_line
    identity.write_text(ident)

installed = ksp / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(repo_dll), str(installed))
installed_identity = ksp / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
installed_identity.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(identity), str(installed_identity))

built_sha = sha256(repo_dll)
installed_sha = sha256(installed)
dll = installed.read_bytes()
checks = (
    (built_sha == installed_sha, 'built/installed DLL SHA match'),
    (marker_in_bytes(dll, R018), 'DLL embeds R018 parent marker'),
    (marker_in_bytes(dll, R019), 'DLL embeds R019 marker'),
    (marker_in_bytes(dll, 'TryBeginRev35R019VisibleFoundationCommit'),
     'DLL embeds R019 visible-priority helper'),
    (marker_in_bytes(dll, 'oh_rev35_r019_visible_priority_begin='),
     'DLL embeds R019 priority telemetry'),
    (marker_in_bytes(dll, 'oh_rev35_r019_budget_150='),
     'DLL embeds R019 budget telemetry'),
    (('rev3_5_r019_variant=' + R019) in installed_identity.read_text(),
     'installed identity records R019'),
)
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

print(PREFIX + ' PASS')
print('mode=FAST development')
print('lineage_replay=NO full_prebuild=NO bin_obj_clean=NO package_copy=NO')
print('install=DLL + candidate identity only')
print('dll_sha256=' + installed_sha)
print('NOTE: restart KSP after DLL replacement; runtime does not hot-reload plugin assemblies.')
