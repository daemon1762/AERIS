#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP1 build-entry regression verification")
text = read(ROOT / "build_ubuntu.sh")
suite.check(
    re.search(r'^DISPLAY="AERIS Flight Control v\$SEMVER DEV CP(?:1|2|3)(?:\b|\.)', text, re.MULTILINE) is not None,
    "build display identifies CP1 or a later checkpoint")

acceptance = re.search(
    r'^PYTHONDONTWRITEBYTECODE=1 python3 "\$ROOT/Tools/(run_v01800_cp(?:1|2|3)[^"/]*_acceptance\.py)"$',
    text,
    re.MULTILINE)
suite.check(acceptance is not None,
            "current checkpoint acceptance runs without bytecode residue")
if acceptance is not None:
    acceptance_pos = acceptance.start()
    compile_pos = text.find('xbuild /p:Configuration=Release')
    suite.check(compile_pos >= 0 and acceptance_pos < compile_pos,
                "current checkpoint acceptance runs before compilation")
else:
    suite.check(False, "current checkpoint acceptance runs before compilation")

for token, label in (
    ('command -v xbuild', "xbuild availability is checked"),
    ('Assembly-CSharp.dll', "KSP Assembly-CSharp reference is checked"),
    ('UnityEngine.UIModule.dll', "Unity UI reference is checked"),
    ('ToolbarControl.dll', "ToolbarController dependency is checked"),
    ('xbuild /p:Configuration=Release', "Release xbuild command is present"),
    ('cp -f bin/Release/AERISFlightControl.dll', "compiled DLL is staged"),
    ('PRESERVE_DIR="$(mktemp -d)"', "user settings preservation is retained"),
):
    suite.check(token in text, label)
suite.check('rm -rf "$TARGET"' not in text,
            "installer never deletes the complete user AERIS tree")
suite.finish()
