#!/usr/bin/env python3
from pathlib import Path
import runpy
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
HELPER = Path(__file__).with_name('fix_aeris25_diazepam_rejected_generated_tree_residue.py')
subprocess.run([sys.executable, str(HELPER)], cwd=str(ROOT), check=True)
runpy.run_path(str(Path(__file__).with_name('prepare_aeris25_diazepam_rev006_runtime.py')), run_name='__main__')
