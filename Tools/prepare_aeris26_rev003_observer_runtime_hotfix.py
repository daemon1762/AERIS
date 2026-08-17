#!/usr/bin/env python3
from pathlib import Path
import hashlib
import runpy
import shutil
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

# REV003 Observer is measurement-only and must not rebuild or mutate accepted shader
# semantics. If the repository staging directory lacks the already-accepted bundle/probe,
# seed the exact platform pair from the currently installed AERIS package in this KSP.
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

source_pair_ok = assetbundle_ok(source_bundle) and assetbundle_ok(source_probe)
if not source_pair_ok:
    if not assetbundle_ok(installed_bundle) or not assetbundle_ok(installed_probe):
        raise SystemExit(
            PREFIX + ' accepted shader staging missing and installed platform pair is unavailable/invalid: ' +
            str(installed_shader_dir))
    if shader_mode == 'windows' and sha256(installed_probe) != EXPECTED_WINDOWS_PROBE_SHA:
        raise SystemExit(
            PREFIX + ' installed Windows probe SHA is not the accepted compatibility probe: ' +
            sha256(installed_probe))
    source_shader_dir.mkdir(parents=True, exist_ok=True)
    shutil.copy2(installed_bundle, source_bundle)
    shutil.copy2(installed_probe, source_probe)
    print(PREFIX + ' seeded accepted ' + shader_mode + ' shader/probe from installed KSP package')
    print(PREFIX + ' seeded bundle_sha256=' + sha256(source_bundle))
    print(PREFIX + ' seeded probe_sha256=' + sha256(source_probe))
else:
    if shader_mode == 'windows' and sha256(source_probe) != EXPECTED_WINDOWS_PROBE_SHA:
        raise SystemExit(
            PREFIX + ' staged Windows probe SHA is not the accepted compatibility probe: ' +
            sha256(source_probe))
    print(PREFIX + ' accepted ' + shader_mode + ' shader/probe already present in source staging')

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
