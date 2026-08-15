#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "Tools/apply_aeris25_gpu_dynamic_terrain_colour.py"
text = path.read_text()
changed = False

pairs = [
    ("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; shader=",
     "[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; requested="),
    ("[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; shader=",
     "[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; requested="),
]
for old, new in pairs:
    if new in text:
        continue
    if text.count(old) != 1:
        raise SystemExit("[AERIS25 ANCHORS] expected one applicator token: " + old)
    text = text.replace(old, new, 1)
    changed = True

# rev007 SYSTEM options/residency changed visibility suspension from full backend
# release to resident retention. The AERIS25 applicator must anchor to that accepted
# predecessor in both its old and replacement blocks.
release = "gpuVertexProjection.ReleaseForSuspension();"
retain = "gpuVertexProjection.RetainForViewportSuspension();"
release_count = text.count(release)
if release_count:
    if release_count != 2:
        raise SystemExit("[AERIS25 ANCHORS] expected two renderer residency applicator tokens, found %d" % release_count)
    text = text.replace(release, retain)
    changed = True
elif text.count(retain) < 2:
    raise SystemExit("[AERIS25 ANCHORS] rev007 renderer residency tokens missing")

if changed:
    path.write_text(text)
    print("[AERIS25 ANCHORS] aligned AERIS25 applicator to accepted rev007 anchors")
else:
    print("[AERIS25 ANCHORS] rev007 applicator anchors already aligned")
