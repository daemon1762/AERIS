#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
path = ROOT / 'Tools/selftest_v01800_operation_health_pass2_persistent_geometry.py'
text = path.read_text(encoding='utf-8')
old = "check('ordinary entry removal recycles meshes', 'RecycleMesh(ref entry.LandMesh);' in renderer and 'RecycleMesh(ref entry.CoastlineMesh);' in renderer)"
new = "check('ordinary entry removal recycles meshes', 'RecycleMesh(ref entry.PackedTerrainMesh);' in renderer and 'RecycleMesh(ref entry.ContourMesh);' in renderer and 'RecycleMesh(ref entry.CoastlineMesh);' in renderer and 'RecycleMesh(ref entry.LandMesh);' not in renderer and 'RecycleMesh(ref entry.WaterMesh);' not in renderer)"
if new in text:
    print('[AERIS23 Pass2 Selftest Fix] already applied')
    raise SystemExit(0)
if text.count(old) != 1:
    raise SystemExit('Pass2 ordinary-removal recycle anchor mismatch')
text = text.replace(old, new, 1)
path.write_text(text, encoding='utf-8')
print('[AERIS23 Pass2 Selftest Fix] applied')
print('Contract: ordinary removal recycles PackedTerrainMesh + Contour + Coastline and never requires legacy terrain Mesh objects')
