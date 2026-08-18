#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_r008_runtime.py'
PREFIX = '[AERIS27 R007 RUNTIME COMPATIBILITY ROUTER]'

if not TARGET.is_file():
    raise SystemExit(PREFIX + ' R008 runtime preparer missing: ' + str(TARGET))

print(PREFIX + ' legacy R007 command redirected to R008 CURRENT FOUNDATION UPSTREAM PRIORITY')
result = subprocess.run([sys.executable, str(TARGET)] + sys.argv[1:], cwd=str(ROOT))
raise SystemExit(result.returncode)
