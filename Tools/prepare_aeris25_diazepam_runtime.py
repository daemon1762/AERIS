#!/usr/bin/env python3
from pathlib import Path
import runpy
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
HELPER = Path(__file__).with_name('fix_aeris25_diazepam_rejected_generated_tree_residue.py')
subprocess.run([sys.executable, str(HELPER)], cwd=str(ROOT), check=True)

# INSTALL IDENTITY HOTFIX 1:
# - force an authoritative compile instead of allowing stale Mono bin/obj outputs;
# - preserve the REV006 preparer, but make its legacy GNU `strings` probe understand
#   .NET user strings stored as UTF-16LE as well as UTF-8/ASCII.
for generated_dir in (
    ROOT / 'Source/AERISFlightControl/bin',
    ROOT / 'Source/AERISFlightControl/obj',
):
    if generated_dir.exists():
        print('[AERIS25 DIAZEPAM REV006 INSTALL IDENTITY HOTFIX 1] removing stale generated build directory: ' + str(generated_dir))
        shutil.rmtree(generated_dir)

_original_check_output = subprocess.check_output
_markers = (
    'OH_PHASE7_001',
    'REV006 RESIDENT RAM REUSE STRENGTHENING',
    'oh_resident_prep_hit=',
    'oh_resident_prep_bytes=',
)

def _check_output_compat(args, *pargs, **kwargs):
    try:
        argv = [str(x) for x in args]
    except TypeError:
        argv = []
    if len(argv) == 2 and Path(argv[0]).name == 'strings':
        dll = Path(argv[1])
        data = dll.read_bytes()
        found = []
        for text in _markers:
            if text.encode('utf-8') in data or text.encode('utf-16le') in data:
                found.append(text)
        rendered = '\n'.join(found) + ('\n' if found else '')
        if kwargs.get('text') or kwargs.get('universal_newlines') or kwargs.get('encoding'):
            return rendered
        return rendered.encode(kwargs.get('encoding') or 'utf-8')
    return _original_check_output(args, *pargs, **kwargs)

subprocess.check_output = _check_output_compat
try:
    runpy.run_path(str(Path(__file__).with_name('prepare_aeris25_diazepam_rev006_runtime.py')), run_name='__main__')
finally:
    subprocess.check_output = _original_check_output
