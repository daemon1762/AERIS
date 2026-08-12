#!/usr/bin/env python3
from pathlib import Path
import subprocess,sys
ROOT=Path(__file__).resolve().parents[1]
subprocess.run([sys.executable,str(ROOT/'Tools/selftest_v01800_operation_health_staggered_exact_refresh_static.py')],cwd=str(ROOT),check=True)
print('[AERIS23] stagger static audit complete')