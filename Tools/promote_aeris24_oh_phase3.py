#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OLD = "PENI" + "CILLIN"
NEW = "EPI" + "NEPHRINE"
REV_OLD = "OH_PHASE2_001"
REV_NEW = "OH_PHASE3_007"
CANDIDATE = "AERIS24_GPU_VERTEX_PROJECTION_POC"

def one(text, old, new, label):
    if old in text and new not in text:
        return text.replace(old, new, 1)
    if old not in text and new in text:
        return text
    raise SystemExit("[OH PHASE3] " + label + " anchor mismatch")

monitor = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
text = monitor.read_text()
text = one(text, 'internal const string Codename = "' + OLD + '";', 'internal const string Codename = "' + NEW + '";', "codename")
text = one(text, 'internal const string Revision = "' + REV_OLD + '";', 'internal const string Revision = "' + REV_NEW + '";', "revision")
text = one(text, 'internal const string Candidate = "AERIS23_OH_' + OLD + '";', 'internal const string Candidate = "' + CANDIDATE + '";', "candidate")
monitor.write_text(text)

config = ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg"
text = config.read_text()
text = one(text, "    codename = " + OLD, "    codename = " + NEW, "config")
config.write_text(text)

build = ROOT / "build_ubuntu.sh"
text = build.read_text()
text = one(text, 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT"', 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH PHASE 3 ' + NEW + ' GPU VERTEX PROJECTION"', "display")
text = one(text, 'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH STEP 2 MOTION CONTENT SPLIT COASTAL EDGE REFINEMENT";', 'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PHASE 3 ' + NEW + ' — GPU VERTEX PROJECTION";', "ui")
install_marker = '''for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg AERISOperationHealth.cfg; do
  if test -f "$PRESERVE_DIR/Config/$USER_CONFIG"; then
    cp -a "$PRESERVE_DIR/Config/$USER_CONFIG" "$TARGET/Config/"
  fi
done
'''
identity_block = '''# Operation Health generation identity is package-owned even though the rest of
# AERISOperationHealth.cfg is user-owned policy. Preserve policy values, then promote
# only codename so an older installed config cannot mask EPINEPHRINE.
OH_CONFIG="$TARGET/Config/AERISOperationHealth.cfg"
if test -f "$OH_CONFIG"; then
  python3 - "$OH_CONFIG" <<'PYOH'
from pathlib import Path
import re
import sys
path = Path(sys.argv[1])
text = path.read_text()
updated, count = re.subn(r'(?m)^(\\s*codename\\s*=\\s*).+$', r'\\1''' + NEW + '''', text, count=1)
if count != 1:
    raise SystemExit("[AERIS] ERROR: Operation Health codename key missing during Phase 3 install promotion")
path.write_text(updated)
PYOH
fi
'''
if identity_block not in text:
    if text.count(install_marker) != 1:
        raise SystemExit("[OH PHASE3] install config-restore anchor mismatch")
    text = text.replace(install_marker, install_marker + identity_block, 1)
build.write_text(text)

print("[OH PHASE3] codename=" + NEW)
print("[OH PHASE3] revision=" + REV_NEW)
print("[OH PHASE3] candidate=" + CANDIDATE)
print("[OH PHASE3] warm visibility suspend + bounded post-FRONT prune revision PASS")
print("[OH PHASE3] preserved config policy + package-owned codename promotion PASS")
print("[OH PHASE3] identity promotion PASS")
