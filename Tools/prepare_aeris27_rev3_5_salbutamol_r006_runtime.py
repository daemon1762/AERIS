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
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R006 RUNTIME]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
R005 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
TTY = sys.stdout.isatty()
GREEN = '\033[1;32m' if TTY else ''
RED = '\033[1;31m' if TTY else ''
CYAN = '\033[1;36m' if TTY else ''
YELLOW = '\033[1;33m' if TTY else ''
MAGENTA = '\033[1;35m' if TTY else ''
RESET = '\033[0m' if TTY else ''


def info(message):
    print(CYAN + PREFIX + RESET + ' ' + message)


def run(args, quiet=False):
    args = [str(x) for x in args]
    info('$ ' + ' '.join(args))
    if quiet:
        subprocess.run(args, cwd=str(ROOT), check=True,
                       stdout=subprocess.DEVNULL)
    else:
        subprocess.run(args, cwd=str(ROOT), check=True)


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode('utf-8') in data or text.encode('utf-16le') in data


def renderer_text():
    try:
        return R.read_text()
    except OSError:
        return ''


def marker_present(marker):
    return marker in renderer_text()


def reconstruct_r005_parent():
    info('reconstructing accepted generated lineage through R005 without an intermediate install')
    scripts = (
        'apply_aeris25_gpu_dynamic_terrain_colour_ready.py',
        'apply_aeris25_chunk_cull_guard_hotfix.py',
        'apply_aeris25_temporal_foundation_overscan_hotfix.py',
        'apply_aeris25_foundation_cull_bypass_hotfix.py',
        'apply_aeris25_renderable_entry_gate_hotfix.py',
        'apply_aeris25_gpu_vertex_reject_diagnostics_hotfix.py',
        'apply_aeris25_snapshot_mesh_lifetime_guard_hotfix.py',
        'apply_aeris25_content_generation_burst_governor_hotfix.py',
        'apply_aeris25_persistent_presentation_batching.py',
        'apply_aeris25_main_thread_commit_governor.py',
        'apply_aeris25_staged_main_thread_commit_hotfix.py',
        'fix_aeris25_phase6_002_inherited_selftests.py',
        'apply_aeris25_authoritative_publication_lifetime_hotfix.py',
        'fix_aeris25_phase6_002_inherited_selftests.py',
        'fix_aeris25_phase6_003_inherited_selftests.py',
        'apply_aeris26_rev003_observer.py',
        'apply_aeris27_rev3_5_salbutamol_r001_compile_hotfix1.py',
        'apply_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py',
        'apply_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py',
        'apply_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py',
        'apply_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py',
    )
    for script in scripts:
        run([sys.executable, ROOT / 'Tools' / script])
    if not marker_present(R005):
        raise SystemExit(RED + PREFIX + ' R005 reconstruction marker missing' + RESET)


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS27 REV3.5 SALBUTAMOL SULFATE R006. R006 keeps R005/R004 behavior, reuses only snapshot-safe retired bare GeographicUnitPoint arrays, transfers already-complete source Vector3 arrays into CPU fallback projected ownership, and adds measurement-only foundation/finalize/GPU-scratch observers. No foundation admission or publication semantics are changed.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(RED + PREFIX + ' KSP path not found: ' + str(ksp) + RESET)

if not marker_present(R006):
    if not marker_present(R005):
        reconstruct_r005_parent()
    run([sys.executable,
         ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py'])
else:
    info('existing R006 generated tree detected')

verifiers = (
    'verify_aeris25_authoritative_publication_lifetime_hotfix.py',
    'verify_aeris27_rev3_5_salbutamol_resumable_prepare.py',
    'verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py',
    'verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py',
    'verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py',
    'verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py',
    'verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',
)
for verifier in verifiers:
    run([sys.executable, ROOT / 'Tools' / verifier])
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
    (('rev3_5_r004_variant=' + R004) in identity_text, 'R004 parent identity retained'),
    (('rev3_5_r005_variant=' + R005) in identity_text, 'R005 parent identity retained'),
    (('rev3_5_r006_variant=' + R006) in identity_text, 'R006 identity marker'),
    (('git=' + git_head) in identity_text, 'identity git HEAD'),
    (marker_in_bytes(dll, R001), 'DLL embeds R001 parent'),
    (marker_in_bytes(dll, R002), 'DLL embeds R002 parent'),
    (marker_in_bytes(dll, R003), 'DLL embeds R003 parent'),
    (marker_in_bytes(dll, R004), 'DLL embeds R004 parent'),
    (marker_in_bytes(dll, R005), 'DLL embeds R005 parent'),
    (marker_in_bytes(dll, R006), 'DLL embeds R006 marker'),
    (marker_in_bytes(dll, 'oh_rev35_r006_geo_pool_hit=') and
     marker_in_bytes(dll, 'oh_rev35_r006_missing_upstream=') and
     marker_in_bytes(dll, 'oh_rev35_r006_foundation_wait_max_ms=') and
     marker_in_bytes(dll, 'oh_rev35_r006_finalize_wait_max_ms=') and
     marker_in_bytes(dll, 'oh_rev35_r006_gpu_attr_grow_max_ms='),
     'DLL embeds R006 allocation/foundation/finalize observers'),
    (not marker_in_bytes(dll, 'WaitManagedPreparation') and
     not marker_in_bytes(dll, 'ResidentPreparedPresentation') and
     not marker_in_bytes(dll, 'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'),
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
print('parent_r004=' + R004)
print('parent_r005=' + R005)
print('r006=' + R006)
print('git=' + git_head)
print('dll_sha256=' + sha256(installed_dll))
print(CYAN + 'R006 ACTIVE:' + RESET + ' snapshot-safe exact-length geographic managed-array reuse (8 MiB / 16 arrays) + duplicate projected-array ownership transfer.')
print(MAGENTA + 'R006 OBSERVERS:' + RESET + ' foundation missing path, contour-only fallback, true Finalize wait, geographic miss allocation, GPU attribute scratch growth.')
print(YELLOW + 'R006 FROZEN:' + RESET + ' R004 adaptive commit + R005 lanes; foundation admission unchanged; publication authority unchanged; worker_count_change=0 quality_change=0 10Hz_change=0 160km_change=0.')
