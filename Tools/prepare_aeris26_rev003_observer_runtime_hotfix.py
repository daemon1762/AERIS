#!/usr/bin/env python3
from pathlib import Path
import runpy
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
TARGET = Path(__file__).with_name('prepare_aeris26_rev003_observer_runtime.py')
PREFIX = '[AERIS26 REV003 OBSERVER M1 PLATFORM HOTFIX]'

if len(sys.argv) < 2:
    raise SystemExit(PREFIX + ' usage: prepare_aeris26_rev003_observer_runtime_hotfix.py <KSP_PATH>')

ksp = Path(sys.argv[1]).expanduser().resolve()
linux_managed = (
    (ksp / 'KSP_x64_Data/Managed/Assembly-CSharp.dll').is_file() or
    (ksp / 'KSP_x86_64_Data/Managed/Assembly-CSharp.dll').is_file() or
    (ksp / 'KSP_Data/Managed/Assembly-CSharp.dll').is_file()
)
native_linux = (ksp / 'KSP.x86_64').is_file() and linux_managed
windows_exe = (ksp / 'KSP_x64.exe').resolve()

_original_is_file = Path.is_file

if native_linux and _original_is_file(windows_exe):
    print(PREFIX + ' dual-layout KSP detected: native Linux player is authoritative; ignoring co-resident KSP_x64.exe for observer shader selection')

    def _is_file_platform_compat(self):
        try:
            if self.resolve() == windows_exe:
                return False
        except OSError:
            pass
        return _original_is_file(self)

    Path.is_file = _is_file_platform_compat
else:
    print(PREFIX + ' standard platform layout; no compatibility override required')

try:
    runpy.run_path(str(TARGET), run_name='__main__')
finally:
    Path.is_file = _original_is_file
