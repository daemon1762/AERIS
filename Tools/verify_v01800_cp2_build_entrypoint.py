#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 build entrypoint verification")
s = read(ROOT / "build_ubuntu.sh")

suite.check(
    re.search(r'^DISPLAY="AERIS Flight Control v\$SEMVER DEV CP(?:2|3)(?:\b|\.)', s, re.MULTILINE) is not None,
    'build display identifies CP2 or a later checkpoint')

acceptance = re.search(
    r'^PYTHONDONTWRITEBYTECODE=1 python3 "\$ROOT/Tools/(run_v01800_cp(?:2|3)[^"/]*_acceptance\.py)"$',
    s,
    re.MULTILINE)
suite.check(acceptance is not None, 'current acceptance is declared')
if acceptance is not None:
    compile_pos = s.find('xbuild /p:Configuration=Release')
    suite.check(compile_pos >= 0 and acceptance.start() < compile_pos,
                'current acceptance runs before compilation')
else:
    suite.check(False, 'current acceptance runs before compilation')

for token, label in (
    ('PYTHONDONTWRITEBYTECODE=1', 'acceptance prevents bytecode residue'),
    ('command -v xbuild', 'xbuild availability is checked'),
    ('Assembly-CSharp.dll', 'KSP Assembly-CSharp reference is checked'),
    ('UnityEngine.UIModule.dll', 'Unity UI reference is checked'),
    ('ToolbarControl.dll', 'ToolbarController dependency is checked'),
    ('xbuild /p:Configuration=Release', 'Release xbuild command is present'),
    ('AERISFlightControl.dll', 'compiled DLL is staged'),
    ('AERISSettings.cfg', 'user settings preservation remains explicit'),
    ('NavigationDisplayProfiles.cfg', 'per-craft ND profiles are preserved')):
    suite.check(token in s, label)
suite.check('rm -rf "$KSP/GameData/AERISFlightControl"' not in s,
            'installer never deletes complete user AERIS tree')
suite.finish()
