#!/usr/bin/env python3
from pathlib import Path
import argparse
import hashlib
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = 'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR'
OH_CODENAME = 'NOREPINEPHRINE'
OH_REVISION = 'OH_PHASE6_004'
PREFIX = '[AERIS25 NOREPINEPHRINE REV004 RUNTIME]'


def run(args):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), check=True)


def sha256(path):
    h = hashlib.sha256()
    with path.open('rb') as f:
        for block in iter(lambda: f.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


def dll_contains_text(path, text):
    try:
        data = path.read_bytes()
    except OSError:
        return False
    probes = (text.encode('utf-8'), text.encode('utf-16le'))
    return any(probe in data for probe in probes)


def identity_value(text, key):
    prefix = key + '='
    for line in text.splitlines():
        if line.startswith(prefix):
            return line[len(prefix):].strip()
    return ''


def phase6_4_present():
    try:
        r = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
        m = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
        u = (ROOT / 'build_ubuntu.sh').read_text()
    except OSError:
        return False
    return ('AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in r and
            'internal const string Revision = "OH_PHASE6_004";' in m and
            'verify_aeris25_managed_preparation_pipeline_hotfix.py' in u)


def assert_generated_rev004_source():
    r = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
    m = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
    u = (ROOT / 'build_ubuntu.sh').read_text()
    checks = [
        ('AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE' in r,
         'rev004 renderer marker'),
        ('internal const string Revision = "OH_PHASE6_004";' in m,
         'rev004 OH source revision'),
        ('REV004 MANAGED PREPARATION PIPELINE' in u,
         'rev004 build display'),
        ('verify_aeris25_managed_preparation_pipeline_hotfix.py' in u,
         'rev004 final-tree build verifier'),
    ]
    failed = [label for ok, label in checks if not ok]
    if failed:
        raise SystemExit(PREFIX + ' GENERATED SOURCE IDENTITY FAIL: ' + ', '.join(failed))
    for _, label in checks:
        print('[PASS] ' + label)


def invalidate_parent_runtime_artifacts(ksp):
    # The rev003 parent preparer intentionally installs a valid parent package while
    # reconstructing a raw tree. Do not leave that DLL runnable if the rev004 build
    # subsequently fails. Also remove incremental compiler outputs so the final build
    # cannot accidentally reuse a rev003 binary while the generated source is rev004.
    installed = ksp / 'GameData/AERISFlightControl'
    stale_paths = [
        installed / 'Plugins/AERISFlightControl.dll',
        installed / 'AERISCandidateBuildIdentity.txt',
        ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll',
    ]
    for path in stale_paths:
        try:
            if path.is_file():
                path.unlink()
                print(PREFIX + ' removed parent artifact: ' + str(path))
        except OSError as exc:
            raise SystemExit(PREFIX + ' could not invalidate parent artifact %s: %s' %
                             (path, exc))
    source = ROOT / 'Source/AERISFlightControl'
    for path in (source / 'bin', source / 'obj'):
        if path.exists():
            shutil.rmtree(path)
            print(PREFIX + ' forced clean: ' + str(path))


parser = argparse.ArgumentParser(
    description='Prepare/install AERIS25-3 NOREPINEPHRINE OH_PHASE6_004 Managed Preparation Pipeline runtime.')
parser.add_argument('ksp_path')
parser.add_argument('--rebuild-shader', action='store_true')
parser.add_argument('--unity-editor', default='')
args = parser.parse_args()
ksp = Path(args.ksp_path).expanduser().resolve()
if not ksp.is_dir():
    raise SystemExit(PREFIX + ' KSP path not found: ' + str(ksp))

# Reuse the already-validated rev003 reconstruction/build helper as the parent generator.
# It may install rev003 on a raw tree. Rev004 then invalidates that parent DLL before its
# own forced-clean build, so a failed final build cannot masquerade as a runtime candidate.
if not phase6_4_present():
    parent = [sys.executable,
              ROOT / 'Tools/prepare_aeris25_main_thread_commit_governor_rev003_runtime.py',
              ksp]
    if args.rebuild_shader:
        parent.append('--rebuild-shader')
    if args.unity_editor:
        parent.extend(['--unity-editor', args.unity_editor])
    run(parent)
    run([sys.executable,
         ROOT / 'Tools/apply_aeris25_managed_preparation_pipeline_hotfix.py'])
else:
    print(PREFIX + ' generated Phase6_004 tree already present; rev003 parent reconstruction skipped')

run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_002_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_003_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/fix_aeris25_phase6_004_inherited_selftests.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_managed_preparation_pipeline_hotfix.py'])
run([sys.executable, ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'])
run([sys.executable, ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'])
run(['git', 'diff', '--check'])
assert_generated_rev004_source()
invalidate_parent_runtime_artifacts(ksp)
run(['bash', ROOT / 'build_ubuntu.sh', ksp])

source_dll = ROOT / 'GameData/AERISFlightControl/Plugins/AERISFlightControl.dll'
installed = ksp / 'GameData/AERISFlightControl'
installed_dll = installed / 'Plugins/AERISFlightControl.dll'
identity = installed / 'AERISCandidateBuildIdentity.txt'
config = installed / 'Config/AERISOperationHealth.cfg'
for path in (source_dll, installed_dll, identity, config):
    if not path.is_file():
        raise SystemExit(PREFIX + ' installed artifact missing: ' + str(path))

identity_text = identity.read_text(errors='replace')
config_text = config.read_text(errors='replace')
current_git = subprocess.check_output(
    ['git', '-C', str(ROOT), 'rev-parse', 'HEAD'], text=True).strip()
installed_sha = sha256(installed_dll)
checks = [
    (sha256(source_dll) == installed_sha, 'built/installed DLL SHA'),
    (identity_value(identity_text, 'built_dll_sha256') == installed_sha,
     'identity built DLL SHA'),
    (identity_value(identity_text, 'git') == current_git,
     'identity git HEAD'),
    (('candidate=' + CANDIDATE) in identity_text, 'Phase 6 candidate identity'),
    (('codename = ' + OH_CODENAME) in config_text, 'NOREPINEPHRINE installed config identity'),
    (dll_contains_text(installed_dll, OH_REVISION),
     'installed DLL embeds OH_PHASE6_004'),
    (dll_contains_text(installed_dll, 'oh_managed_prep_submitted='),
     'installed DLL embeds rev004 managed-prep telemetry'),
    (dll_contains_text(installed_dll, 'REV004 MANAGED PREPARATION PIPELINE'),
     'installed DLL embeds rev004 build display'),
]
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    # Remove the bad DLL so a failed gate cannot be followed by an accidental KSP test.
    try:
        if installed_dll.is_file():
            installed_dll.unlink()
    except OSError:
        pass
    raise SystemExit(PREFIX + ' INSTALL IDENTITY FAIL: ' + ', '.join(failed))

print(PREFIX + ' INSTALL IDENTITY MATCH=YES')
print('candidate=' + CANDIDATE)
print('oh_codename=' + OH_CODENAME)
print('oh_revision=' + OH_REVISION)
print('git=' + current_git)
print('dll_sha256=' + installed_sha)
print('Correctness gate: snapshot_stale_mesh=0; deferred retirement remains bounded and drains.')
print('Rev004 gate: managed_prep_submitted/completed rise; CPU fallback lazy alloc should remain 0 while GPU projection is ACTIVE.')
print('GC gate: main_commit_geo_max should collapse; inspect managed_prep_bytes_peak/total and GC events versus rev003.')
print('Test after install PASS only: 20 -> 40 -> 80 -> 160 km, then 160 km Track-Up strong turn and steady cruise.')
