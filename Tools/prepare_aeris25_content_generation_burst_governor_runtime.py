#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = 'AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR'
OH_CODENAME = 'ATRO' + 'PINE'
OH_REVISION = 'OH_PHASE4_009'
EXPECTED_WINDOWS_PROBE_SHA = '6465e6dfa7c9809a734d5ce85b202b49ea6ee5fcaac19d55d4b75bd532a35f0d'


def run(args, env=None):
    args = [str(x) for x in args]
    print('[AERIS25 REV009 RUNTIME] $ ' + ' '.join(args))
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


def rev009_ready():
    try:
        r = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
        t = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs').read_text()
        m = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
        u = (ROOT / 'build_ubuntu.sh').read_text()
    except OSError:
        return False
    return ('internal const string Revision = "OH_PHASE4_009";' in m and
            'AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in r and
            'AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in t and
            'oh_content_commit_budget_hit=' in r and
            'oh_prune_budget_hit=' in r and
            'oh_heading_plan_coalesced=' in r and
            'AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in r and
            'REV009 CONTENT GENERATION BURST GOVERNOR' in u and
            'verify_aeris25_content_generation_burst_governor_hotfix.py' in u)


parser = argparse.ArgumentParser(description='Prepare/install AERIS25 ATROPINE rev009 Content Generation Burst Governor.')
parser.add_argument('ksp_path')
parser.add_argument('--rebuild-shader', action='store_true')
parser.add_argument('--unity-editor', default=os.environ.get('UNITY_EDITOR', ''))
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit('[AERIS25 REV009 RUNTIME] KSP path not found: ' + str(ksp))

if not rev009_ready():
    steps = [
        'apply_aeris25_gpu_dynamic_terrain_colour_ready.py',
        'apply_aeris25_chunk_cull_guard_hotfix.py',
        'apply_aeris25_temporal_foundation_overscan_hotfix.py',
        'apply_aeris25_foundation_cull_bypass_hotfix.py',
        'apply_aeris25_renderable_entry_gate_hotfix.py',
        'apply_aeris25_gpu_vertex_reject_diagnostics_hotfix.py',
    ]
    for name in steps:
        run([sys.executable, ROOT / 'Tools' / name])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_snapshot_mesh_lifetime_guard_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py'])
    run([sys.executable, ROOT / 'Tools/apply_aeris25_content_generation_burst_governor_hotfix.py'])
else:
    print('[AERIS25 REV009 RUNTIME] generated rev009 tree already present; reconstruction skipped')

run([sys.executable, ROOT / 'Tools/verify_aeris25_content_generation_burst_governor_hotfix.py'])
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
    raise SystemExit('[AERIS25 REV009 RUNTIME] could not identify KSP Unity player layout')

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
    print('[AERIS25 REV009 RUNTIME] no shader change; reusing accepted AERIS25 bundle/probe pair')

for path, label in ((bundle, 'shader'), (probe, 'probe')):
    if not path.is_file() or path.stat().st_size == 0 or not assetbundle_ok(path):
        raise SystemExit('[AERIS25 REV009 RUNTIME] invalid %s bundle: %s' % (label, path))
if shader_mode == 'windows' and sha256(probe) != EXPECTED_WINDOWS_PROBE_SHA:
    raise SystemExit('[AERIS25 REV009 RUNTIME] Windows probe compatibility SHA FAIL')

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
        raise SystemExit('[AERIS25 REV009 RUNTIME] installed artifact missing: ' + str(path))

identity_text = identity.read_text(errors='replace')
config_text = config.read_text(errors='replace')
checks = [
    (sha256(source_dll) == sha256(installed_dll), 'built/installed DLL SHA'),
    (source_bundle_sha == sha256(installed_bundle), 'shader bundle SHA'),
    (source_probe_sha == sha256(installed_probe), 'probe SHA'),
    (('candidate=' + CANDIDATE) in identity_text, 'candidate identity'),
    (('gpu_shader_bundle=' + bundle_name) in identity_text, 'bundle identity'),
    (('codename = ' + OH_CODENAME) in config_text, 'ATROPINE config identity'),
]
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit('[AERIS25 REV009 RUNTIME] INSTALL IDENTITY FAIL: ' + ', '.join(failed))

print('[AERIS25 REV009 RUNTIME] INSTALL IDENTITY MATCH=YES')
print('candidate=' + CANDIDATE)
print('oh_codename=' + OH_CODENAME)
print('oh_revision=' + OH_REVISION)
print('dll_sha256=' + sha256(installed_dll))
print('gpu_shader_bundle=' + bundle_name)
print('gpu_shader_bundle_sha256=' + sha256(installed_bundle))
print('Runtime correctness gate: GPU_ACTIVE + Dynamic Colour ACTIVE + oh_snapshot_stale_mesh=0 + attr_fail=0.')
print('Burst gate: exercise 80->160 km and a sharp Track-Up turn; inspect commit/prune/heading governor counters.')
print('Reference rev008 spikes: ~161.7 ms range-change frame and ~208.5 ms turn frame; both should materially improve.')
print('Golden gate: zero rectangular holes, visualCoverage=1.000, Runway Map Lock ~0 px, fixed 10 Hz authority.')
