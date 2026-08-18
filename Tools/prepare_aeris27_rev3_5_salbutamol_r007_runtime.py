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
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R007 RUNTIME]'
R001 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001'
R002 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT'
R003 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION'
R004 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT'
R005 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF1 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1'
HF2 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_MANAGED_HEAP_ATTRIBUTION'
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
    subprocess.run(args, cwd=str(ROOT), check=True,
                   stdout=subprocess.DEVNULL if quiet else None)


def marker_present(path, marker):
    try:
        return marker in path.read_text()
    except OSError:
        return False


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode('utf-8') in data or text.encode('utf-16le') in data


def reconstruct_r005_parent():
    info('reconstructing accepted lineage through R005')
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
        run([sys.executable, ROOT / 'Tools' / script], quiet=True)
    if not marker_present(R, R005):
        raise SystemExit(RED + PREFIX + ' R005 reconstruction marker missing' + RESET)


parser = argparse.ArgumentParser(description='Prepare/install AERIS27 REV3.5 R007 managed-heap attribution observer. Measurement only: no GC forcing, no quality/authority/worker/cadence changes.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(RED + PREFIX + ' KSP path not found: ' + str(ksp) + RESET)

if not marker_present(R, R006):
    if not marker_present(R, R005):
        reconstruct_r005_parent()
    run([sys.executable, ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py'])
if not marker_present(R, HF1):
    run([sys.executable, ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py'])
if not marker_present(R, HF2):
    run([sys.executable, ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py'])
if not marker_present(T, HF3):
    run([sys.executable, ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py'])
if not marker_present(T, R007):
    run([sys.executable, ROOT / 'Tools/apply_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution.py'])
else:
    info('existing R007 generated tree detected')

verifiers = (
    'verify_aeris25_authoritative_publication_lifetime_hotfix.py',
    'verify_aeris27_rev3_5_salbutamol_resumable_prepare.py',
    'verify_aeris27_rev3_5_salbutamol_r002_packed_allocation_split.py',
    'verify_aeris27_rev3_5_salbutamol_r003_requested_view_admission.py',
    'verify_aeris27_rev3_5_salbutamol_r004_adaptive_high_flow_commit.py',
    'verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py',
    'verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py',
    'verify_aeris27_rev3_5_salbutamol_r006_resource_release_hotfix1.py',
    'verify_aeris27_rev3_5_salbutamol_r006_resource_release_order_hotfix2.py',
    'verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py',
    'verify_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution.py',
)
for verifier in verifiers:
    run([sys.executable, ROOT / 'Tools' / verifier])
run(['git', 'diff', '--check'])

for generated in (ROOT / 'Source/AERISFlightControl/bin', ROOT / 'Source/AERISFlightControl/obj'):
    if generated.exists():
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
git_head = subprocess.check_output(['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()
checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (('rev3_5_r006_hotfix3=' + HF3) in identity_text, 'HF3 parent identity retained'),
    (('rev3_5_r007_variant=' + R007) in identity_text, 'R007 identity marker'),
    (('git=' + git_head) in identity_text, 'identity git HEAD'),
    (marker_in_bytes(dll, HF3), 'DLL embeds HF3 parent'),
    (marker_in_bytes(dll, R007), 'DLL embeds R007 marker'),
    (marker_in_bytes(dll, 'oh_rev35_r007_capture_pos_bytes=') and
     marker_in_bytes(dll, 'oh_rev35_r007_resolve_pos_bytes=') and
     marker_in_bytes(dll, 'r007_plan_pos_bytes=') and
     marker_in_bytes(dll, '[R007_GC]'), 'DLL embeds R007 attribution witnesses'),
    (not marker_in_bytes(dll, 'GC.Collect('), 'DLL contains no R007 forced-GC call'),
    (not marker_in_bytes(dll, 'WaitManagedPreparation') and
     not marker_in_bytes(dll, 'ResidentPreparedPresentation') and
     not marker_in_bytes(dll, 'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'),
     'DLL excludes rejected mechanisms'),
]
failed = []
for ok, label in checks:
    print((GREEN if ok else RED) + ('[PASS] ' if ok else '[FAIL] ') + label + RESET)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(RED + PREFIX + ' INSTALL IDENTITY FAIL: ' + ', '.join(failed) + RESET)

print(GREEN + PREFIX + ' INSTALL IDENTITY MATCH=YES' + RESET)
print('r007=' + R007)
print('git=' + git_head)
print('dll_sha256=' + sha256(installed_dll))
print(CYAN + 'R007 ACTIVE:' + RESET + ' passive heap-positive windows for renderer and TileSystem stages + passive Gen2 interval observer.')
print(MAGENTA + 'R007 PURPOSE:' + RESET + ' attribute the unchanged ~17-20 s Full-GC cadence before changing allocation behavior.')
print(YELLOW + 'R007 FROZEN:' + RESET + ' forced_gc=0 quality_change=0 authority_change=0 worker_change=0 10Hz_change=0 160km_change=0.')
