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

# UiCheckpoint is embedded inside build_ubuntu.sh's printf-generated C# line, not a
# standalone shell line. Match AERIS24's promotion strategy: substring replace once.
ui_active = '''bu = replace_active_line(bu,\n    'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PHASE 3 ' + OLD_OH + ' — GPU VERTEX PROJECTION";',\n    'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ' + NEW_OH + ' — GPU DYNAMIC TERRAIN COLOUR";',\n    'in-game checkpoint identity')'''
ui_once = '''bu = replace_once(bu,\n    'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PHASE 3 ' + OLD_OH + ' — GPU VERTEX PROJECTION";',\n    'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ' + NEW_OH + ' — GPU DYNAMIC TERRAIN COLOUR";',\n    'in-game checkpoint identity')'''
if ui_once not in text:
    if text.count(ui_active) != 1:
        raise SystemExit("[AERIS25 ANCHORS] embedded UiCheckpoint applicator block mismatch")
    text = text.replace(ui_active, ui_once, 1)
    changed = True

# The inherited AERIS24 bundle guard text is user-facing. AERIS25 owns a separate
# builder because shader content and bundle names have changed.
guidance_anchor = "bu = bu.replace('[AERIS24 GPU VERTEX]', '[AERIS25 GPU DYNAMIC COLOUR]')"
guidance_new = guidance_anchor + "\nbu = bu.replace('Run Tools/build_aeris24_gpu_shader_bundle.sh',\n                'Run Tools/build_aeris25_gpu_shader_bundle.sh')"
if guidance_new not in text:
    if text.count(guidance_anchor) != 1:
        raise SystemExit("[AERIS25 ANCHORS] bundle guidance applicator anchor mismatch")
    text = text.replace(guidance_anchor, guidance_new, 1)
    changed = True

if changed:
    path.write_text(text)
    print("[AERIS25 ANCHORS] aligned AERIS25 applicator to accepted rev007 anchors")
else:
    print("[AERIS25 ANCHORS] rev007 applicator anchors already aligned")
