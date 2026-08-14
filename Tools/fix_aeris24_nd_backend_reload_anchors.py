#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
P = ROOT / "Tools/apply_aeris24_nd_backend_reload.py"
text = P.read_text()
old_anchor = "'''        bool gpuVertexProjectionBackFailure;\n        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);''',"
new_anchor = "'''        bool gpuVertexProjectionBackFailure;''',"
old_replacement = "'''        bool gpuVertexProjectionBackFailure;\n        AERISNdProjectionBackendMode projectionBackendMode =\n            (AERISNdProjectionBackendMode)(-1);\n        long ndReloadGeneration = 1L;\n        long frontReloadGeneration;\n        long operationHealthProjectionBackendSwitches;\n        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);''',"
new_replacement = "'''        bool gpuVertexProjectionBackFailure;\n        AERISNdProjectionBackendMode projectionBackendMode =\n            (AERISNdProjectionBackendMode)(-1);\n        long ndReloadGeneration = 1L;\n        long frontReloadGeneration;\n        long operationHealthProjectionBackendSwitches;''',"
if old_anchor in text and old_replacement in text:
    text = text.replace(old_anchor, new_anchor, 1).replace(old_replacement, new_replacement, 1)
elif new_anchor in text and new_replacement in text:
    print("[AERIS24 ND BACKEND RELOAD ANCHORS] already aligned")
    raise SystemExit(0)
else:
    raise SystemExit("[AERIS24 ND BACKEND RELOAD ANCHORS] field anchor shape mismatch")
P.write_text(text)
print("[AERIS24 ND BACKEND RELOAD ANCHORS] field anchor aligned")
