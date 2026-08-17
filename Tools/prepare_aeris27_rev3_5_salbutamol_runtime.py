#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R001 RUNTIME]'
MARKER = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
BASE_OBSERVER = 'AERIS26_REV003_OBSERVER_M1'
BASE_REVISION = 'OH_PHASE6_003'
BASE_CANDIDATE = 'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR'


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


def rev35_generated_tree_present():
    try:
        renderer = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
        build = (ROOT / 'build_ubuntu.sh').read_text()
    except OSError:
        return False
    return MARKER in renderer and ('REV3_5_VARIANT="' + MARKER + '"') in build


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS27 Operation Health REV3.5 SALBUTAMOL SULFATE R001: resumable main-thread managed Prepare over the accepted REV003 Observer M1 parent.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

if not rev35_generated_tree_present():
    # Deliberately reuse the already validated AERIS26 platform/shader authority path.
    # R001 pays one baseline build/install before the delta build so platform selection,
    # historical shader reuse and exact REV003 reconstruction are not duplicated here.
    run([sys.executable,
         ROOT / 'Tools/prepare_aeris26_rev003_observer_runtime_hotfix.py', ksp])
else:
    print(PREFIX + ' existing REV3.5 generated tree detected; baseline rebuild skipped')

run([sys.executable, ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_resumable_prepare.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_resumable_prepare.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris26_rev003_observer.py'])
run(['git', 'diff', '--check'])

for generated in (
    ROOT / 'Source/AERISFlightControl/bin',
    ROOT / 'Source/AERISFlightControl/obj',
):
    if generated.exists():
        print(PREFIX + ' removing stale build directory: ' + str(generated))
        shutil.rmtree(generated)

run(['bash', ROOT / 'build_ubuntu.sh', ksp])

source_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed_root = ksp / 'GameData/AERISFlightControl'
installed_dll = installed_root / 'Plugins/AERISFlightControl.dll'
identity = installed_root / 'AERISCandidateBuildIdentity.txt'
config = installed_root / 'Config/AERISOperationHealth.cfg'
for path in (source_dll, installed_dll, identity, config):
    if not path.is_file():
        raise SystemExit(PREFIX + ' installed artifact missing: ' + str(path))

identity_text = identity.read_text(errors='replace')
config_text = config.read_text(errors='replace')
dll = installed_dll.read_bytes()
git_head = subprocess.check_output(
    ['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()

checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (('candidate=' + BASE_CANDIDATE) in identity_text, 'REV003 behavior candidate retained'),
    (('observer_variant=' + BASE_OBSERVER) in identity_text, 'Observer M1 identity retained'),
    (('rev3_5_variant=' + MARKER) in identity_text, 'REV3.5 identity marker'),
    (('git=' + git_head) in identity_text, 'identity git HEAD'),
    ('codename = NOREPINEPHRINE' in config_text, 'base behavior config remains NOREPINEPHRINE'),
    (marker_in_bytes(dll, BASE_REVISION), 'DLL embeds base OH_PHASE6_003'),
    (marker_in_bytes(dll, BASE_OBSERVER), 'DLL embeds Observer M1'),
    (marker_in_bytes(dll, MARKER), 'DLL embeds REV3.5 R001 marker'),
    (marker_in_bytes(dll, 'oh_rev35_prepare_source_yield=') and
     marker_in_bytes(dll, 'oh_rev35_prepare_packed_yield='),
     'DLL embeds REV3.5 prepare-yield telemetry'),
    (not marker_in_bytes(dll, 'OH_PHASE6_004') and
     not marker_in_bytes(dll, 'OH_PHASE6_005') and
     not marker_in_bytes(dll, 'OH_PHASE7_001'),
     'DLL excludes rejected descendant identities'),
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
print('behavior_base=NOREPINEPHRINE')
print('base_revision=' + BASE_REVISION)
print('observer=' + BASE_OBSERVER)
print('rev3_5_variant=' + MARKER)
print('git=' + git_head)
print('dll_sha256=' + sha256(installed_dll))
print('R001 SCOPE: managed PrepareSources/PreparePackedTerrain resumable only; Unity mesh upload remains main-thread authoritative and unchanged.')
print('R001 BAN CHECK: worker_prepare=0 speculative=0 presentation_cache=0.')
