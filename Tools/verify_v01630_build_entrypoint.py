#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.16.3.0 build entrypoint verification")
text = read(ROOT / "build_ubuntu.sh")
for token, label in (
    ('command -v xbuild', "xbuild availability is checked"),
    ('Assembly-CSharp.dll', "KSP Assembly-CSharp reference is checked"),
    ('UnityEngine.UIModule.dll', "UnityEngine UI reference is checked"),
    ('ToolbarControl.dll', "ToolbarController dependency is checked"),
    ('run_v01630_acceptance.py', "v0.16.3.0 acceptance runs before compilation"),
    ('xbuild /p:Configuration=Release', "Release xbuild command is present"),
    ('cp -f bin/Release/AERISFlightControl.dll', "compiled DLL is staged"),
    ('PRESERVE_DIR="$(mktemp -d)"', "user settings preservation is retained"),
    ('"$TARGET/Airfields/Defaults"', "package-owned Airfield defaults are replaced"),
    ('mkdir -p "$TARGET" "$TARGET/FlightPlans" "$TARGET/Airfields"',
     "user FlightPlans and Airfields directories are retained"),
): suite.check(token in text, label)
suite.check('rm -rf "$TARGET"' not in text, "installer does not delete the whole AERIS tree")
for prior in ('run_v01601_acceptance.py', 'run_v01612_acceptance.py', 'run_v01620_acceptance.py'):
    suite.check(prior not in text, f"prior acceptance runner is absent: {prior}")
suite.finish()
