#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS29 REV3.5 R020 FAST BUILD]'
BRANCH = 'agent/aeris29-rev3-5-salbutamol-r020-current-revision-publication-catch-up'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
HF1 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION'
R020 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def output(args):
    return subprocess.check_output(
        [str(x) for x in args], cwd=str(ROOT), text=True).strip()


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode() in data or text.encode('utf-16le') in data


parser = argparse.ArgumentParser(
    description='R020 development FAST build: materialize R019+HF1+R020, run impact gates, incremental xbuild, DLL-only install.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

branch = output(['git', 'branch', '--show-current'])
if branch != BRANCH:
    raise SystemExit(PREFIX + ' wrong branch: ' + branch + ' expected=' + BRANCH)

renderer = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
tile_system = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
if not renderer.is_file() or not tile_system.is_file():
    raise SystemExit(PREFIX + ' terrain source missing')
if R018 not in renderer.read_text():
    raise SystemExit(PREFIX + ' exact materialized R018+ successor tree required before FAST overlay')

# Materialize the current successor chain locally. These applicators are idempotent and
# do not require a clean generated tree. Historical successor compatibility is applied
# before the runtime overlays, exactly as in the successful R019 FAST path.
run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_historical_verifier_successor_compat.py'])
run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py'])
run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_hotfix1_wake_backlog_integration.py'])
run([sys.executable,
     ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r020_visible_authority_baseline_stability.py'])

# R020 changes viewport-generation meaning only. Keep the impact gate focused on the
# historical commit/reconcile/presentation chain that consumes ViewGeneration plus the
# new R020 verifier/model. FORMAL remains responsible for exhaustive lineage replay.
for verifier in (
    'verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',
    'verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',
    'verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py',
    'verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py',
    'verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py',
    'verify_aeris28_rev3_5_salbutamol_r014_publication_gated_content_reconcile.py',
    'verify_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py',
    'verify_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py',
    'verify_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py',
    'verify_aeris29_rev3_5_salbutamol_r019_hotfix1_wake_backlog_integration.py',
    'verify_aeris29_rev3_5_salbutamol_r020_visible_authority_baseline_stability.py',
    'selftest_v01900_oh_rev35_r020_visible_authority_baseline_stability.py',
):
    path = ROOT / 'Tools' / verifier
    if not path.is_file():
        raise SystemExit(PREFIX + ' impact gate missing: ' + verifier)
    run([sys.executable, path])
run(['git', 'diff', '--check'])

# Frozen control-law areas remain completely outside R020 scope.
frozen = [
    'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/Protect',
    'Source/AERISFlightControl/Landing',
]
changed = output(['git', 'diff', '--name-only', 'HEAD', '--'] + frozen)
if changed:
    raise SystemExit(PREFIX + ' frozen control-law working-tree edits detected:\n' + changed)

# Assert the materialized runtime contract before compiling.
renderer_text = renderer.read_text()
tile_text = tile_system.read_text()
for token, where in (
    (R018, renderer_text),
    (R019, renderer_text),
    (HF1, renderer_text),
    (R020, renderer_text),
    (R020, tile_text),
    ('rev35R020GenerationViewLatitudeDeg', tile_text),
    ('operationHealthRev35R020GenerationRetained++', tile_text),
    ('foundationComplete = rendered && r018VisibleGpuComplete;', renderer_text),
    ('TryBeginRev35R019VisibleFoundationCommit()', renderer_text),
    ('oh_rev35_r020_generation_advance=', renderer_text),
):
    if token not in where:
        raise SystemExit(PREFIX + ' materialized runtime contract missing: ' + token)

# FAST intentionally preserves bin/obj and bypasses build_ubuntu.sh/full historical
# prebuild. This keeps the development loop incremental while FORMAL remains exhaustive.
src = ROOT / 'Source/AERISFlightControl'
csproj = src / 'AERISFlightControl.csproj'
run(['xbuild', '/p:Configuration=Release', '/p:KSPDIR=' + str(ksp), csproj])

built = src / 'bin/Release/AERISFlightControl.dll'
if not built.is_file():
    raise SystemExit(PREFIX + ' built DLL missing: ' + str(built))
repo_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
repo_dll.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(built), str(repo_dll))

# Preserve the installed/materialized R019 Hotfix1 candidate identity and append only R020.
identity = ROOT / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
if not identity.is_file():
    raise SystemExit(PREFIX + ' materialized candidate identity missing')
ident = identity.read_text()
for line in (
    'rev3_5_r018_variant=' + R018,
    'rev3_5_r019_variant=' + R019,
    'rev3_5_r019_hotfix1=' + HF1,
):
    if line not in ident:
        raise SystemExit(PREFIX + ' parent identity missing: ' + line)
r020_line = 'rev3_5_r020_variant=' + R020 + '\n'
if r020_line not in ident:
    if ident and not ident.endswith('\n'):
        ident += '\n'
    ident += r020_line
    identity.write_text(ident)

installed = ksp / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(repo_dll), str(installed))
installed_identity = ksp / 'GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt'
installed_identity.parent.mkdir(parents=True, exist_ok=True)
shutil.copy2(str(identity), str(installed_identity))

repo_sha = sha256(repo_dll)
installed_sha = sha256(installed)
dll = installed.read_bytes()
installed_ident = installed_identity.read_text()
checks = (
    (repo_sha == installed_sha, 'repo/installed DLL SHA match'),
    (marker_in_bytes(dll, R018), 'DLL embeds R018 parent marker'),
    (marker_in_bytes(dll, R019), 'DLL embeds R019 marker'),
    (marker_in_bytes(dll, HF1), 'DLL embeds R019 Hotfix1 marker'),
    (marker_in_bytes(dll, R020), 'DLL embeds R020 marker'),
    (marker_in_bytes(dll, 'oh_rev35_r020_generation_advance='),
     'DLL embeds R020 telemetry'),
    (marker_in_bytes(dll, 'rev35R020GenerationViewLatitudeDeg'),
     'DLL embeds R020 stable authority baseline field'),
    (('rev3_5_r020_variant=' + R020) in installed_ident,
     'installed identity records R020'),
)
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

print(PREFIX + ' PASS')
print('mode=FAST development R020')
print('formal_replay=NO full_prebuild=NO bin_obj_clean=NO package_copy=NO')
print('runtime_change=stable viewport-generation authority baseline only; live display sample remains current')
print('publication=R017/R018/R019/HF1 unchanged; stale BACK relabel/promotion=NO')
print('install=DLL + candidate identity only')
print('dll_sha256=' + installed_sha)
print('NOTE: fully exit and restart KSP after DLL replacement; plugin assemblies are not hot-reloaded.')
