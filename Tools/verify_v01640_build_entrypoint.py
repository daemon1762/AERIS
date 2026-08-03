#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01640_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.16.4.0 build entrypoint verification")
text = read(ROOT / "build_ubuntu.sh")
for token, label in (
    ('command -v xbuild', "xbuild availability is checked"),
    ('Assembly-CSharp.dll', "KSP Assembly-CSharp reference is checked"),
    ('UnityEngine.UIModule.dll', "Unity UI reference is checked"),
    ('ToolbarControl.dll', "ToolbarController dependency is checked"),
    ('run_v01640_acceptance.py', "current acceptance runs before compilation"),
    ('xbuild /p:Configuration=Release', "Release xbuild command is present"),
    ('cp -f bin/Release/AERISFlightControl.dll', "compiled DLL is staged"),
    ('PRESERVE_DIR="$(mktemp -d)"', "user settings preservation is retained"),
    ('"$TARGET/Airfields/Defaults"', "package-owned airfield defaults are replaced"),
    ('mkdir -p "$TARGET" "$TARGET/FlightPlans" "$TARGET/Airfields"',
     "user FlightPlans and Airfields roots are retained"),
):
    suite.check(token in text, label)
suite.check('rm -rf "$TARGET"' not in text,
            "installer never deletes the complete user AERIS tree")
suite.finish()
