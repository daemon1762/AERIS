#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "Tools/apply_aeris25_gpu_dynamic_terrain_colour.py"
text = path.read_text()

pairs = [
    ("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; shader=",
     "[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; requested="),
    ("[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; shader=",
     "[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; requested="),
]
changed = False
for old, new in pairs:
    if new in text:
        continue
    if text.count(old) != 1:
        raise SystemExit("[AERIS25 ANCHORS] expected one applicator token: " + old)
    text = text.replace(old, new, 1)
    changed = True

if changed:
    path.write_text(text)
    print("[AERIS25 ANCHORS] aligned backend ACTIVE applicator tokens: shader -> requested")
else:
    print("[AERIS25 ANCHORS] rev007 backend ACTIVE tokens already aligned")
