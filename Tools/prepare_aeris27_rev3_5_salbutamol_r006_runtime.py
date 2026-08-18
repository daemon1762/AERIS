#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_r006_hf3_runtime.py'
PREFIX = '[AERIS27 R006 RUNTIME COMPATIBILITY ROUTER]'

if not TARGET.is_file():
    raise SystemExit(PREFIX + ' HF3 runtime preparer missing: ' + str(TARGET))

print(PREFIX + ' legacy R006 command redirected to COMPLETE COVERAGE HOTFIX3')
result = subprocess.run([sys.executable, str(TARGET)] + sys.argv[1:],
                        cwd=str(ROOT))
raise SystemExit(result.returncode)
