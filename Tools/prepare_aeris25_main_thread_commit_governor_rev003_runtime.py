#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = 'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR'
OH_CODENAME = 'NOREPINEPHRINE'
OH_REVISION = 'OH_PHASE6_003'
EXPECTED_WINDOWS_PROBE_SHA = '6465e6dfa7c9809a734d5ce85b202b49ea6ee5fcaac19d55d4b75bd532a35f0d'
PREFIX = '[AERIS25 NOREPINEPHRINE REV003 RUNTIME]'


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


def assetbundle_ok(path):
    try:
        data = path.read_bytes()
    except OSError:
        return False
    return data.startswith(b'UnityFS\x00') and b'AssetBundle' in data


def identity_state():
    try:
        r = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
        m = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
        c = (ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
        u = (ROOT / 'build_ubuntu.sh').read_text()
    except OSError:
        return 'raw'
    if ('internal const string Codename = "NOREPINEPHRINE";' in m and
        'internal const string Revision = "OH_PHASE6_003";' in m and
        'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in m and
        'codename = NOREPINEPHRINE' in c and
        'AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in r and
        'verify_aeris25_authoritative_publication_lifetime_hotfix.py' in u):
        return 'phase6_3'
    if ('internal const string Codename = "NOREPINEPHRINE";' in m and
        'internal const string Revision = "OH_PHASE6_002";' in m and
        'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in m and
        'codename = NOREPINEPHRINE' in c and
        'AERIS25_STAGED_MAIN_THREAD_COMMIT' in r and
        'verify_aeris25_staged_main_thread_commit_hotfix.py' in u):
        return 'phase6_2'
    if ('internal const string Codename = "NOREPINEPHRINE";' in m and
        'internal const string Revision = "OH_PHASE6_001";' in m and
        'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in m and
        'codename = NOREPINEPHRINE' in c and
        'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR' in r and
        'verify_aeris25_main_thread_commit_governor.py' in u):
        return 'phase6_1'
    if ('internal const string Codename = "ADENOSINE";' in m and
        'internal const string Revision = "OH_PHASE5_001";' in m and
        'internal const string Candidate = "AERIS25_PERSISTENT_PRESENTATION_BATCHING";' in m and
        'codename = ADENOSINE' in c and
        'AERIS25_PERSISTENT_PRESENTATION_BATCHING' in r and
        'verify_aeris25_persistent_presentation_batching.py' in u):
        return 'phase5'
    return 'raw'


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS25-3 NOREPINEPHRINE OH_PHASE6_003 authoritative publication + deferred retirement runtime.')
parser.add_argument('ksp_path')
parser.add_argument('--rebuild-shader', action='store_true')
parser.add_argument('--unity-editor', default=os.environ.get('UNITY_EDITOR', ''))
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

state = identity_state()
if state == 'raw':
    for name in [
        'apply_aeris25_gpu_dynamic_terrain_colour_ready.py',
        'apply_aeris25_chunk_cull_guard_hotfix.py',
        'apply_aeris25_temporal_foundation_overscan_hotfix.py',
        'apply_aeris25_foundation_cull_bypass_hotfix.py',
        'apply_aeris25_renderable_entry_gate_hotfix.py',
        'apply_aeris25_gpu_vertex_reject_diagnostics_hotfix.py',
    ]:
        run([sys.executable, ROOT / 'Tools' / name])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_snapshot_mesh_lifetime_guard_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_content_generation_burst_governor_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_content_generation_burst_governor_hotfix.py'])
    print(PREFIX + ' inherited ATROPINE rev009 acceptance=PASS')
    run([sys.executable, ROOT / 'Tools/apply_aeris25_persistent_presentation_batching.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'])
    print(PREFIX + ' inherited ADENOSINE Phase5_001 acceptance=PASS')
    state = 'phase5'
elif state == 'phase5':
    run([sys.executable, ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'])
    print(PREFIX + ' generated ADENOSINE parent already present=PASS')

if state == 'phase5':
    run([sys.executable, ROOT / 'Tools/apply_aeris25_main_thread_commit_governor.py'])
    state = 'phase6_1'

if state == 'phase6_1':
    run([sys.executable, ROOT / 'Tools/verify_aeris25_main_thread_commit_governor.py'])
    print(PREFIX + ' historical Phase6_001 static parent=PASS; runtime design remains rejected')
    run([sys.executable, ROOT / 'Tools/apply_aeris25_staged_main_thread_commit_hotfix.py'])
    state = 'phase6_2'

if state == 'phase6_2':
    run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_002_inherited_selftests.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_staged_main_thread_commit_hotfix.py'])
    print(PREFIX + ' historical Phase6_002 static parent=PASS; runtime correctness remains rejected')
    run([sys.executable, ROOT / 'Tools/apply_aeris25_authoritative_publication_lifetime_hotfix.py'])
    state = 'phase6_3'

if state != 'phase6_3':
    raise SystemExit(PREFIX + ' could not reach generated Phase6_003 tree; state=' + state)

run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_002_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_003_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_authoritative_publication_lifetime_hotfix.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'])
run([sys.executable, ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'])
run(['git', 'diff', '--check'])

if (ksp / 'KSP_x64_Data/Managed/Assembly-CSharp.dll').is_file():
    shader_mode = 'windows'
    bundle_name = 'aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle'
    probe_name = 'aeris25_gpu_dynamic_colour_probe_windows.bundle'
elif ((ksp / 'KSP_Data/Managed/Assembly-CSharp.dll').is_file() or
      (ksp / 'KSP_x86_64_Data/Managed/Assembly-CSharp.dll').is_file()):
    shader_mode = 'linux'
    bundle_name = 'aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle'
    probe_name = 'aeris25_gpu_dynamic_colour_probe_linux.bundle'
else:
    raise SystemExit(PREFIX + ' could not identify KSP Unity player layout')

shader_dir = ROOT / 'GameData/AERISFlightControl/Shaders'
bundle = shader_dir / bundle_name
probe = shader_dir / probe_name
need_rebuild = args.rebuild_shader or not bundle.is_file() or not probe.is_file()
if shader_mode == 'windows' and probe.is_file() and sha256(probe) != EXPECTED_WINDOWS_PROBE_SHA:
    need_rebuild = True
if need_rebuild:
    env = os.environ.copy()
    if args.unity_editor:
        env['UNITY_EDITOR'] = args.unity_editor
    run(['bash', ROOT / 'Tools/build_aeris25_gpu_shader_bundle.sh', shader_mode], env=env)
else:
    print(PREFIX + ' no shader change; reusing accepted AERIS25 bundle/probe pair')

for path, label in ((bundle, 'shader'), (probe, 'probe')):
    if not path.is_file() or path.stat().st_size == 0 or not assetbundle_ok(path):
        raise SystemExit(PREFIX + ' invalid %s bundle: %s' % (label, path))
if shader_mode == 'windows' and sha256(probe) != EXPECTED_WINDOWS_PROBE_SHA:
    raise SystemExit(PREFIX + ' Windows probe compatibility SHA FAIL')

source_bundle_sha = sha256(bundle)
source_probe_sha = sha256(probe)
run(['bash', ROOT / 'build_ubuntu.sh', ksp])

source_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed = ksp / 'GameData/AERISFlightControl'
installed_dll = installed / 'Plugins/AERISFlightControl.dll'
installed_bundle = installed / ('Shaders/' + bundle_name)
installed_probe = installed / ('Shaders/' + probe_name)
identity = installed / 'AERISCandidateBuildIdentity.txt'
config = installed / 'Config/AERISOperationHealth.cfg'
for path in (source_dll, installed_dll, installed_bundle, installed_probe, identity, config):
    if not path.is_file():
        raise SystemExit(PREFIX + ' installed artifact missing: ' + str(path))

identity_text = identity.read_text(errors='replace')
config_text = config.read_text(errors='replace')
checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (source_bundle_sha == sha256(installed_bundle), 'shader bundle SHA'),
    (source_probe_sha == sha256(installed_probe), 'probe SHA'),
    (('candidate=' + CANDIDATE) in identity_text, 'Phase 6 candidate identity'),
    (('gpu_shader_bundle=' + bundle_name) in identity_text, 'bundle identity'),
    (('codename = ' + OH_CODENAME) in config_text, 'NOREPINEPHRINE installed config identity'),
]
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ')+label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' INSTALL IDENTITY FAIL: ' + ', '.join(failed))

print(PREFIX + ' INSTALL IDENTITY MATCH=YES')
print('candidate=' + CANDIDATE)
print('oh_codename=' + OH_CODENAME)
print('oh_revision=' + OH_REVISION)
print('dll_sha256=' + sha256(installed_dll))
print('gpu_shader_bundle=' + bundle_name)
print('gpu_shader_bundle_sha256=' + sha256(installed_bundle))
print('Correctness gate: snapshot_stale_mesh must remain exactly 0; deferred_retire must release only after packet refresh.')
print('Publication gate: publish_defer should rise under hidden-frame work, while publish occurs only on authoritative content ticks.')
print('Performance gate: inspect terrain/contour/coast/geo/finalize max telemetry and require window/stage maxima materially below rev002 18.982/17.809 ms.')
print('Test after install PASS only: 20 -> 40 -> 80 -> 160 km, then 160 km Track-Up strong turn and steady cruise.')
