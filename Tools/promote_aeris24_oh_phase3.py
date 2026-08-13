#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OLD = "PENI" + "CILLIN"
NEW = "EPI" + "NEPHRINE"
REV_OLD = "OH_PHASE2_001"
REV_NEW = "OH_PHASE3_001"
CAND_OLD = "AERIS24_GPU_VERTEX_PROJECTION_POC"
CAND_NEW = "AERIS24_OH_" + NEW


def one(text, old, new, label):
    if old in text and new not in text:
        return text.replace(old, new, 1)
    if old not in text and new in text:
        return text
    raise SystemExit("[OH PHASE3] " + label + " anchor mismatch")

monitor = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
text = monitor.read_text()
text = one(text, 'internal const string Codename = "' + OLD + '";',
           'internal const string Codename = "' + NEW + '";', "codename")
text = one(text, 'internal const string Revision = "' + REV_OLD + '";',
           'internal const string Revision = "' + REV_NEW + '";', "revision")
text = one(text, 'internal const string Candidate = "AERIS23_OH_' + OLD + '";',
           'internal const string Candidate = "' + CAND_NEW + '";', "candidate")
monitor.write_text(text)

config = ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg"
text = config.read_text()
text = one(text, "    codename = " + OLD, "    codename = " + NEW, "config")
config.write_text(text)

build = ROOT / "build_ubuntu.sh"
text = build.read_text()
text = one(text, 'CANDIDATE_NAME="' + CAND_OLD + '"',
           'CANDIDATE_NAME="' + CAND_NEW + '"', "build candidate")
text = one(text,
           'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT"',
           'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH PHASE 3 ' + NEW + ' GPU VERTEX PROJECTION"',
           "build display")
text = one(text,
           'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT";',
           'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PHASE 3 ' + NEW + ' — GPU VERTEX PROJECTION";',
           "UI checkpoint")
build.write_text(text)

print("[OH PHASE3] codename=" + NEW)
print("[OH PHASE3] revision=" + REV_NEW)
print("[OH PHASE3] candidate=" + CAND_NEW)
print("[OH PHASE3] identity promotion PASS")
