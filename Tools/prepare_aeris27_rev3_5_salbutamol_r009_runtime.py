#!/usr/bin/env python3
from pathlib import Path
import runpy
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
TARGET = ROOT / 'Tools/prepare_aeris27_rev3_5_salbutamol_r010_runtime.py'
PREFIX = '[AERIS27 REV3.5 R009 COMPATIBILITY ROUTER]'

if not TARGET.is_file():
    raise SystemExit(PREFIX + ' R010 runtime preparer missing: ' + str(TARGET))
print(PREFIX + ' forwarding to R010 CONTINUOUS COMMIT STREAM')
runpy.run_path(str(TARGET), run_name='__main__')
