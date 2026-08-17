#!/usr/bin/env python3
from pathlib import Path
import hashlib
import os
import runpy
import shutil
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
TARGET = Path(__file__).with_name('prepare_aeris26_rev003_observer_runtime.py')
PREFIX = '[AERIS26 REV003 OBSERVER M1 PLATFORM HOTFIX]'
EXPECTED_WINDOWS_PROBE_SHA = '6465e6dfa7c9809a734d5ce85b202b49ea6ee5fcaac19d55d4b75bd532a35f0d'

if len(sys.argv) < 2:
    raise SystemExit(PREFIX + ' usage: prepare_aeris26_rev003_observer_runtime_hotfix.py <KSP_PATH>')


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
    return len(data) > 16 and data.startswith(b'UnityFS\x00') and b'AssetBundle' in data


def run(args, env=None):
    args = [str(x) for x in args]
    print(PREFIX + ' $ ' + ' '.join(args))
    subprocess.run(args, cwd=str(ROOT), env=env, check=True)


ksp = Path(sys.argv[1]).expanduser().resolve()
windows_layout = (ksp / 'KSP_x64_Data/Managed/Assembly-CSharp.dll').is_file()
linux_layout = (
    (ksp / 'KSP_x86_64_Data/Managed/Assembly-CSharp.dll').is_file() or
    (ksp / 'KSP_Data/Managed/Assembly-CSharp.dll').is_file()
)
windows_exe = (ksp / 'KSP_x64.exe').resolve()
_original_is_file = Path.is_file

if windows_layout:
    shader_mode = 'windows'
    print(PREFIX + ' authoritative layout=windows/proton (KSP_x64_Data)')
elif linux_layout:
    shader_mode = 'linux'
    print(PREFIX + ' authoritative layout=native-linux (KSP_Data/KSP_x86_64_Data)')
else:
    shader_mode = 'windows' if _original_is_file(windows_exe) else 'linux'
    print(PREFIX + ' layout fallback=' + shader_mode)

if shader_mode == 'windows':
    bundle_name = 'aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle'
    probe_name = 'aeris25_gpu_dynamic_colour_probe_windows.bundle'
else:
    bundle_name = 'aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle'
    probe_name = 'aeris25_gpu_dynamic_colour_probe_linux.bundle'

source_shader_dir = ROOT / 'GameData/AERISFlightControl/Shaders'
installed_shader_dir = ksp / 'GameData/AERISFlightControl/Shaders'
source_bundle = source_shader_dir / bundle_name
source_probe = source_shader_dir / probe_name
installed_bundle = installed_shader_dir / bundle_name
installed_probe = installed_shader_dir / probe_name


def historical_pair_ok(bundle, probe):
    if not assetbundle_ok(bundle) or not assetbundle_ok(probe):
        return False
    if shader_mode == 'windows' and sha256(probe) != EXPECTED_WINDOWS_PROBE_SHA:
        return False
    return True


def locally_built_pair_ok(bundle, probe):
    if not assetbundle_ok(bundle) or not assetbundle_ok(probe):
        return False
    if shader_mode == 'windows' and probe.stat().st_size < 1000:
        return False
    return True


# REV003 Observer is measurement-only and never changes shader semantics. Asset staging
# follows three tiers so every development machine can build independently:
#   1) reuse the historical exact accepted source pair;
#   2) seed that exact pair from the machine's installed KSP package;
#   3) if neither exists, build locally with pinned Unity 2019.4.18f1. A local build
#      must pass Unity semantic/container/stale-size gates inside the builder AND must
#      reproduce identical bundle/probe SHA values across two clean rebuilds.
if historical_pair_ok(source_bundle, source_probe):
    print(PREFIX + ' historical exact accepted ' + shader_mode + ' shader/probe already present in source staging')
elif historical_pair_ok(installed_bundle, installed_probe):
    source_shader_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(installed_bundle, source_bundle)
    shutil.copy2(installed_probe, source_probe)
    print(PREFIX + ' seeded historical exact accepted ' + shader_mode + ' shader/probe from installed KSP package')
    print(PREFIX + ' seeded bundle_sha256=' + sha256(source_bundle))
    print(PREFIX + ' seeded probe_sha256=' + sha256(source_probe))
else:
    print(PREFIX + ' historical accepted shader pair unavailable; invoking reproducible local AERIS25 shader build')
    env = os.environ.copy()
    try:
        run(['bash', ROOT / 'Tools/build_aeris25_gpu_shader_bundle.sh', shader_mode], env=env)
    except subprocess.CalledProcessError as exc:
        raise SystemExit(
            PREFIX + ' local shader build pass1 failed (status=' + str(exc.returncode) + '). '
            'Check the Unity log printed above and rerun the same command after fixing the reported build error.')
    if not locally_built_pair_ok(source_bundle, source_probe):
        raise SystemExit(PREFIX + ' local shader build pass1 completed but structural acceptance failed')
    pass1_bundle_sha = sha256(source_bundle)
    pass1_probe_sha = sha256(source_probe)
    pass1_probe_size = source_probe.stat().st_size
    print(PREFIX + ' local build pass1 bundle_sha256=' + pass1_bundle_sha)
    print(PREFIX + ' local build pass1 probe_sha256=' + pass1_probe_sha + ' size=' + str(pass1_probe_size))

    try:
        run(['bash', ROOT / 'Tools/build_aeris25_gpu_shader_bundle.sh', shader_mode], env=env)
    except subprocess.CalledProcessError as exc:
        raise SystemExit(PREFIX + ' local shader build pass2 failed (status=' + str(exc.returncode) + ')')
    if not locally_built_pair_ok(source_bundle, source_probe):
        raise SystemExit(PREFIX + ' local shader build pass2 completed but structural acceptance failed')
    pass2_bundle_sha = sha256(source_bundle)
    pass2_probe_sha = sha256(source_probe)
    pass2_probe_size = source_probe.stat().st_size
    print(PREFIX + ' local build pass2 bundle_sha256=' + pass2_bundle_sha)
    print(PREFIX + ' local build pass2 probe_sha256=' + pass2_probe_sha + ' size=' + str(pass2_probe_size))

    if pass1_bundle_sha != pass2_bundle_sha or pass1_probe_sha != pass2_probe_sha:
        raise SystemExit(
            PREFIX + ' local shader build reproducibility FAIL: clean rebuild bytes changed. '
            'bundle=' + pass1_bundle_sha + ' -> ' + pass2_bundle_sha + '; probe=' +
            pass1_probe_sha + ' -> ' + pass2_probe_sha)

    print(PREFIX + ' local shader clean-rebuild reproducibility=PASS')
    if shader_mode == 'windows' and pass2_probe_sha == EXPECTED_WINDOWS_PROBE_SHA:
        print(PREFIX + ' local Windows probe also matches historical exact accepted SHA')
    elif shader_mode == 'windows':
        print(PREFIX + ' local Windows probe uses cross-machine semantic acceptance; historical SHA differs as expected when prior Unity asset GUIDs were machine-local')
    print(PREFIX + ' local ' + shader_mode + ' shader build acceptance=PASS')

# The original observer preparer used KSP_x64.exe as its selector. Only override that
# legacy selector when the Managed layout proves a native-Linux player. KSP_x64_Data
# remains authoritative Windows/Proton even if unrelated Linux executables coexist.
if shader_mode == 'linux' and _original_is_file(windows_exe):
    print(PREFIX + ' native-Linux layout with co-resident KSP_x64.exe; masking exe only for legacy observer selector')

    def _is_file_platform_compat(self):
        try:
            if self.resolve() == windows_exe:
                return False
        except OSError:
            pass
        return _original_is_file(self)

    Path.is_file = _is_file_platform_compat

try:
    runpy.run_path(str(TARGET), run_name='__main__')
finally:
    Path.is_file = _original_is_file
