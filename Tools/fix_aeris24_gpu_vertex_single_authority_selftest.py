#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "Tools/selftest_v01800_operation_health_pass3_projection_draw_reduction.py"
if not path.is_file():
    raise SystemExit("[AERIS24 GPU VERTEX TEST FIX] Pass3 selftest missing")

text = path.read_text()
old = """ck(draw.count('terrainMaterial.SetPass(0)') == 1 and
   draw.count('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') == 1 and
   'Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)' not in draw and
   'Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)' not in draw and
   'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)' not in draw and
   'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)' not in draw,
   'single packed terrain authority uses one terrain SetPass and one packed draw per Entry')"""
new = """ck('Material terrainDrawMaterial = gpuEntry ?' in draw and
   'gpuVertexProjection.TerrainMaterial : terrainMaterial;' in draw and
   draw.count('terrainDrawMaterial.SetPass(0)') == 1 and
   draw.count('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)') == 1 and
   'Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)' not in draw and
   'Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)' not in draw and
   'Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix)' not in draw and
   'Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix)' not in draw,
   'single packed terrain authority uses one selected CPU/GPU terrain SetPass and one packed draw per Entry')"""

if new in text:
    print("[AERIS24 GPU VERTEX TEST FIX] already applied")
    raise SystemExit(0)
if old not in text:
    raise SystemExit("[AERIS24 GPU VERTEX TEST FIX] expected AERIS23 Single-Authority assertion not found")
text = text.replace(old, new, 1)
path.write_text(text)
print("[AERIS24 GPU VERTEX TEST FIX] applied")
print("Contract: selected CPU/GPU terrain material -> exactly one SetPass + one PackedTerrainMesh draw + zero legacy terrain draws")
