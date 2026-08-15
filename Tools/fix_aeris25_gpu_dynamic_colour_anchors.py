#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "Tools/apply_aeris25_gpu_dynamic_terrain_colour.py"
text = path.read_text()

old = '''    b = replace_once(b,
''' + "'''" + '''                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; shader=" +\n''' + "'''" + ''',
''' + "'''" + '''                AERISLogger.Info("[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; shader=" +\n''' + "'''" + ''', 'backend active log identity')'''
new = '''    b = replace_once(b,
''' + "'''" + '''                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; requested=" +\n''' + "'''" + ''',
''' + "'''" + '''                AERISLogger.Info("[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; requested=" +\n''' + "'''" + ''', 'backend active log identity')'''

if new in text:
    print("[AERIS25 ANCHORS] rev007 backend ACTIVE anchor already aligned")
elif text.count(old) == 1:
    path.write_text(text.replace(old, new, 1))
    print("[AERIS25 ANCHORS] aligned backend ACTIVE anchor: shader -> requested")
else:
    raise SystemExit("[AERIS25 ANCHORS] backend ACTIVE applicator anchor mismatch")
