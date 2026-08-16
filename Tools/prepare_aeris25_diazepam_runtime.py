#!/usr/bin/env python3
from pathlib import Path
import runpy
runpy.run_path(str(Path(__file__).with_name('prepare_aeris25_diazepam_rev006_runtime.py')),run_name='__main__')
