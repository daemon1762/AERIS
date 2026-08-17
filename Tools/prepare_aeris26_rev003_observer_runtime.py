#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import os
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS26 REV003 OBSERVER M1 RUNTIME]'
OBSERVER = 'AERIS26_REV003_OBSERVER_M1'
REV003_CANDIDATE = 'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR'
REV003_REVISION = 'OH_PHASE6_003'


def run(args, env=None):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), env=env, check=True)


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def marker_in_bytes(data, text):
    return text.encode('utf-8') in data or text.encode('utf-16le') in data


def identity_state():
    try:
        r = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
        m = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
    except OSError:
        return 'raw'
    if OBSERVER in m and 'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in r:
        return 'observer'
    if ('AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE' in r or
            'internal const string Revision = "OH_PHASE7_001";' in m):
        return 'phase7'
    if ('AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION' in r or
            'internal const string Revision = "OH_PHASE6_005";' in m):
        return 'phase6_5'
    if ('AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in r or
            'internal const string Revision = "OH_PHASE6_004";' in m):
        return 'phase6_4'
    if ('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in r and
            'internal const string Revision = "OH_PHASE6_003";' in m):
        return 'phase6_3'
    if ('AERIS25_STAGED_MAIN_THREAD_COMMIT' in r and
            'internal const string Revision = "OH_PHASE6_002";' in m):
        return 'phase6_2'
    if ('AERIS25_MAIN_THREAD_COMMIT_GOVERNOR' in r and
            'internal const string Revision = "OH_PHASE6_001";' in m):
        return 'phase6_1'
    if ('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in r and
            'internal const string Revision = "OH_PHASE5_001";' in m):
        return 'phase5'
    return 'raw'


parser = argparse.ArgumentParser(
    description='Prepare/install exact frozen NOREPINEPHRINE REV003 with measurement-only AERIS26 REV003 OBSERVER M1 telemetry overlay.')
parser.add_argument('ksp_path')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

# Normalize only known generated runtime residue. The helper refuses ambiguous local edits.
run([sys.executable, ROOT / 'Tools/fix_aeris25_diazepam_rejected_generated_tree_residue.py'])
state = identity_state()

if state in ('phase7', 'phase6_5', 'phase6_4', 'observer'):
    raise SystemExit(PREFIX + ' generated-tree cleanup did not normalize rejected/observer state: ' + state)

if state == 'raw':
    for name in (
        'apply_aeris25_gpu_dynamic_terrain_colour_ready.py',
        'apply_aeris25_chunk_cull_guard_hotfix.py',
        'apply_aeris25_temporal_foundation_overscan_hotfix.py',
        'apply_aeris25_foundation_cull_bypass_hotfix.py',
        'apply_aeris25_renderable_entry_gate_hotfix.py',
        'apply_aeris25_gpu_vertex_reject_diagnostics_hotfix.py',
    ):
        run([sys.executable, ROOT / 'Tools' / name])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_snapshot_mesh_lifetime_guard_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_content_generation_burst_governor_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_content_generation_burst_governor_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_persistent_presentation_batching.py'])
    state = 'phase5'

if state == 'phase5':
    run([sys.executable, ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_main_thread_commit_governor.py'])
    state = 'phase6_1'

if state == 'phase6_1':
    run([sys.executable, ROOT / 'Tools/verify_aeris25_main_thread_commit_governor.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_staged_main_thread_commit_hotfix.py'])
    state = 'phase6_2'

if state == 'phase6_2':
    run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_002_inherited_selftests.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_staged_main_thread_commit_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_authoritative_publication_lifetime_hotfix.py'])
    state = 'phase6_3'

if state != 'phase6_3':
    raise SystemExit(PREFIX + ' could not reconstruct frozen REV003; state=' + state)

run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_002_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_003_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py'])
print(PREFIX + ' frozen NOREPINEPHRINE REV003 reconstruction=PASS')

# Snapshot frozen files that the observer is forbidden to change.
frozen_paths = [
    ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs',
]
frozen_hashes = {str(p): sha256(p) for p in frozen_paths}

run([sys.executable, ROOT / 'Tools/apply_aeris26_rev003_observer.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris26_rev003_observer.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'])
run([sys.executable, ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'])
run(['git', 'diff', '--check'])

for p in frozen_paths:
    if sha256(p) != frozen_hashes[str(p)]:
        raise SystemExit(PREFIX + ' measurement overlay changed frozen renderer: ' + str(p))

# Force a clean compile so no rejected descendant binary can leak into this measurement build.
for generated in (
    ROOT / 'Source/AERISFlightControl/bin',
    ROOT / 'Source/AERISFlightControl/obj',
):
    if generated.exists():
        print(PREFIX + ' removing stale build directory: ' + str(generated))
        shutil.rmtree(generated)

# Reuse accepted AERIS25 shader assets unchanged; observer never rebuilds or edits them.
shader_dir = ROOT / 'GameData/AERISFlightControl/Shaders'
if (ksp / 'KSP_x64.exe').is_file():
    bundle_name = 'aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle'
    probe_name = 'aeris25_gpu_dynamic_colour_probe_windows.bundle'
else:
    bundle_name = 'aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle'
    probe_name = 'aeris25_gpu_dynamic_colour_probe_linux.bundle'
bundle = shader_dir / bundle_name
probe = shader_dir / probe_name
for p in (bundle, probe):
    if not p.is_file() or p.stat().st_size <= 0:
        raise SystemExit(PREFIX + ' accepted shader asset missing: ' + str(p))
source_bundle_sha = sha256(bundle)
source_probe_sha = sha256(probe)

run(['bash', ROOT / 'build_ubuntu.sh', ksp])

source_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed_root = ksp / 'GameData/AERISFlightControl'
installed_dll = installed_root / 'Plugins/AERISFlightControl.dll'
installed_bundle = installed_root / 'Shaders' / bundle_name
installed_probe = installed_root / 'Shaders' / probe_name
identity = installed_root / 'AERISCandidateBuildIdentity.txt'
config = installed_root / 'Config/AERISOperationHealth.cfg'
for p in (source_dll, installed_dll, installed_bundle, installed_probe, identity, config):
    if not p.is_file():
        raise SystemExit(PREFIX + ' installed artifact missing: ' + str(p))

identity_text = identity.read_text(errors='replace')
config_text = config.read_text(errors='replace')
dll = installed_dll.read_bytes()
git_head = subprocess.check_output(
    ['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()

checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (source_bundle_sha == sha256(installed_bundle), 'shader bundle SHA'),
    (source_probe_sha == sha256(installed_probe), 'probe SHA'),
    (('candidate=' + REV003_CANDIDATE) in identity_text, 'REV003 candidate identity'),
    (('git=' + git_head) in identity_text, 'identity git HEAD'),
    (('observer_variant=' + OBSERVER) in identity_text, 'observer identity file marker'),
    ('codename = NOREPINEPHRINE' in config_text, 'installed config remains NOREPINEPHRINE'),
    (marker_in_bytes(dll, REV003_REVISION), 'DLL embeds OH_PHASE6_003'),
    (marker_in_bytes(dll, OBSERVER), 'DLL embeds observer variant'),
    (marker_in_bytes(dll, 'obs_reuse_samples=') and
     marker_in_bytes(dll, 'obs_rereq_samples=') and
     marker_in_bytes(dll, 'obs_decode_mean_ms='), 'DLL embeds observer telemetry'),
    (not marker_in_bytes(dll, 'OH_PHASE6_004') and
     not marker_in_bytes(dll, 'OH_PHASE6_005') and
     not marker_in_bytes(dll, 'OH_PHASE7_001'), 'DLL excludes rejected descendant identities'),
    (not marker_in_bytes(dll, 'WaitManagedPreparation') and
     not marker_in_bytes(dll, 'ResidentPreparedPresentation'), 'DLL excludes rejected worker/presentation mechanisms'),
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
print('oh_revision=' + REV003_REVISION)
print('candidate=' + REV003_CANDIDATE)
print('observer_variant=' + OBSERVER)
print('git=' + git_head)
print('dll_sha256=' + sha256(installed_dll))
print('MEASUREMENT ONLY: no control/render/cache-budget/worker-count tuning delta from frozen REV003.')
print('Flight freely. Preserve the full AERISFlightControl log bundle; observer metrics are appended to existing [OH] summaries without per-event file I/O.')
