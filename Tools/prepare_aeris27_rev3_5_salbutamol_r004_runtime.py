#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R004 RUNTIME]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
TTY = sys.stdout.isatty()
GREEN = '\033[1;32m' if TTY else ''
RED = '\033[1;31m' if TTY else ''
CYAN = '\033[1;36m' if TTY else ''
YELLOW = '\033[1;33m' if TTY else ''
RESET = '\033[0m' if TTY else ''


def info(message):
    print(CYAN + PREFIX + RESET + ' ' + message)


def run(args):
    args = [str(x) for x in args]
    info('$ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode('utf-8') in data or text.encode('utf-16le') in data


def r004_present():
    try:
        return R004 in R.read_text()
    except OSError:
        return False


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS27 REV3.5 SALBUTAMOL SULFATE R004 Adaptive High-Flow Commit. R004 widens only the existing main-thread staged-commit pipe from 0.50 ms toward 1.00/1.50/2.00 ms when ND backlog/generation lag demands it and host-frame headroom allows it; prepare chunk width scales 64/128/256 and R002 allocation yields become budget-aware.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(RED + PREFIX + ' KSP path not found: ' + str(ksp) + RESET)

if not r004_present():
    info('reconstructing/validating R003 parent')
    run([sys.executable,
         ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_r003_runtime.py', ksp])
    run([sys.executable,
         ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py'])
else:
    info('existing R004 generated tree detected')

run([sys.executable,
     ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py'])
run([sys.executable,
     ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py'])
run([sys.executable,
     ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py'])
run([sys.executable,
     ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py'])
run(['git', 'diff', '--check'])

for generated in (ROOT / 'Source/AERISFlightControl/bin',
                  ROOT / 'Source/AERISFlightControl/obj'):
    if generated.exists():
        info('removing stale build directory: ' + str(generated))
        shutil.rmtree(generated)

run(['bash', ROOT / 'build_ubuntu.sh', ksp])

source_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed_root = ksp / 'GameData/AERISFlightControl'
installed_dll = installed_root / 'Plugins/AERISFlightControl.dll'
identity = installed_root / 'AERISCandidateBuildIdentity.txt'
for path in (source_dll, installed_dll, identity):
    if not path.is_file():
        raise SystemExit(RED + PREFIX + ' installed artifact missing: ' + str(path) + RESET)
identity_text = identity.read_text(errors='replace')
dll = installed_dll.read_bytes()
git_head = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'],
                                   text=True).strip()
checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (('rev3_5_variant=' + R001) in identity_text, 'R001 parent identity retained'),
    (('rev3_5_r002_variant=' + R002) in identity_text, 'R002 parent identity retained'),
    (('rev3_5_r003_variant=' + R003) in identity_text, 'R003 parent identity retained'),
    (('rev3_5_r004_variant=' + R004) in identity_text, 'R004 identity marker'),
    (('git=' + git_head) in identity_text, 'identity git HEAD'),
    (marker_in_bytes(dll, R001), 'DLL embeds R001 parent'),
    (marker_in_bytes(dll, R002), 'DLL embeds R002 parent'),
    (marker_in_bytes(dll, R003), 'DLL embeds R003 parent'),
    (marker_in_bytes(dll, R004), 'DLL embeds R004 marker'),
    (marker_in_bytes(dll, 'oh_rev35_r004_budget_200=') and
     marker_in_bytes(dll, 'oh_rev35_r004_frame_guard=') and
     marker_in_bytes(dll, 'oh_rev35_r004_chunk_max_items='),
     'DLL embeds R004 adaptive-flow telemetry'),
    (not marker_in_bytes(dll, 'WaitManagedPreparation') and
     not marker_in_bytes(dll, 'ResidentPreparedPresentation'),
     'DLL excludes rejected worker/presentation mechanisms'),
]
failed = []
for ok, label in checks:
    colour = GREEN if ok else RED
    print(colour + ('[PASS] ' if ok else '[FAIL] ') + label + RESET)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(RED + PREFIX + ' INSTALL IDENTITY FAIL: ' + ', '.join(failed) + RESET)

print(GREEN + PREFIX + ' INSTALL IDENTITY MATCH=YES' + RESET)
print('parent_r001=' + R001)
print('parent_r002=' + R002)
print('parent_r003=' + R003)
print('r004=' + R004)
print('git=' + git_head)
print('dll_sha256=' + sha256(installed_dll))
print(CYAN + 'R004 SCOPE:' + RESET + ' adaptive high-flow main-thread staged commit only: 0.50/1.00/1.50/2.00 ms, 64/128/256 prepare chunks, budget-aware R002 allocation yields.')
print(YELLOW + 'R004 GUARDS:' + RESET + ' host-frame caps at 15/20/25 ms; worker_count_change=0 speculative=0 presentation_cache=0 quality_change=0 10Hz_change=0 160km_change=0.')
