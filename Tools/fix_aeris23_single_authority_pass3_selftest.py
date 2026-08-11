#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / 'Tools/selftest_v01800_operation_health_pass3_projection_draw_reduction.py'
text = path.read_text()

old = "ck(draw.count('terrainMaterial.SetPass(0)') == 1 and 'entry.PackedTerrainMesh != null' in draw,\n   'single packed terrain authority uses one terrain SetPass per Entry')"
new = "ck(draw.count('terrainMaterial.SetPass(0)') == 1 and\n   draw.count('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') == 1 and\n   'Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)' not in draw and\n   'Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)' not in draw and\n   'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)' not in draw and\n   'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)' not in draw,\n   'single packed terrain authority uses one terrain SetPass and one packed draw per Entry')"

if new in text:
    print('[AERIS23 Pass3 Selftest Fix] already applied')
    raise SystemExit(0)
if old not in text:
    raise SystemExit('[AERIS23 Pass3 Selftest Fix] expected stale Single-Authority assertion not found')
text = text.replace(old, new, 1)
path.write_text(text)
print('[AERIS23 Pass3 Selftest Fix] applied')
print('Contract: one terrain SetPass + one PackedTerrainMesh draw + zero legacy terrain draws per Entry')
