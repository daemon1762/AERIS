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
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R002 RUNTIME]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode('utf-8') in data or text.encode('utf-16le') in data


def r002_present():
    try:
        return R002 in R.read_text()
    except OSError:
        return False


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS27 REV3.5 SALBUTAMOL SULFATE R002. R002 keeps R001 resumable managed Prepare but isolates the three large packed CLR array allocations into separate KSP frames and records per-allocation maxima.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

if not r002_present():
    print(PREFIX + ' reconstructing/validating R001 Compile Hotfix1 parent')
    run([sys.executable,
         ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_r001_compile_hotfix1_runtime.py',
         ksp])
    run([sys.executable,
         ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py'])
else:
    print(PREFIX + ' existing R002 generated tree detected')

run([sys.executable,
     ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py'])
run([sys.executable,
     ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py'])
run(['git', 'diff', '--check'])

for generated in (ROOT / 'Source/AERISFlightControl/bin',
                  ROOT / 'Source/AERISFlightControl/obj'):
    if generated.exists():
        print(PREFIX + ' removing stale build directory: ' + str(generated))
        shutil.rmtree(generated)

run(['bash', ROOT / 'build_ubuntu.sh', ksp])

source_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed_root = ksp / 'GameData/AERISFlightControl'
installed_dll = installed_root / 'Plugins/AERISFlightControl.dll'
identity = installed_root / 'AERISCandidateBuildIdentity.txt'
for path in (source_dll, installed_dll, identity):
    if not path.is_file():
        raise SystemExit(PREFIX + ' installed artifact missing: ' + str(path))
identity_text = identity.read_text(errors='replace')
dll = installed_dll.read_bytes()
git_head = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'],
                                   text=True).strip()
checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (('rev3_5_variant=' + R001) in identity_text, 'R001 parent identity retained'),
    (('rev3_5_r002_variant=' + R002) in identity_text, 'R002 identity marker'),
    (('git=' + git_head) in identity_text, 'identity git HEAD'),
    (marker_in_bytes(dll, R001), 'DLL embeds R001 parent'),
    (marker_in_bytes(dll, R002), 'DLL embeds R002 marker'),
    (marker_in_bytes(dll, 'oh_rev35_packed_source_alloc_max_ms=') and
     marker_in_bytes(dll, 'oh_rev35_packed_colour_alloc_max_ms=') and
     marker_in_bytes(dll, 'oh_rev35_packed_index_alloc_max_ms='),
     'DLL embeds per-allocation telemetry'),
    (not marker_in_bytes(dll, 'WaitManagedPreparation') and
     not marker_in_bytes(dll, 'ResidentPreparedPresentation'),
     'DLL excludes rejected worker/presentation mechanisms'),
]
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' INSTALL IDENTITY FAIL: ' + ', '.join(failed))

print(PREFIX + ' INSTALL IDENTITY MATCH=YES')
print('parent=' + R001)
print('r002=' + R002)
print('git=' + git_head)
print('dll_sha256=' + sha256(installed_dll))
print('R002 SCOPE: packed CLR source/colour/index allocations are one-per-frame; R001 copy/index resumable loops and main-thread publication authority retained.')
print('R002 BAN CHECK: worker_prepare=0 speculative=0 presentation_cache=0.')
