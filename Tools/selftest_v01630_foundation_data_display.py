#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
import re
from v01630_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.16.3.0 foundation data/display")
defaults = ROOT / "GameData" / "AERISFlightControl" / "FlightPlans" / "Defaults"
files = sorted(defaults.glob("*.cfg"))
suite.equal(len(files), 6, "six existing data-only flight-plan CFG files are retained")
for path in files:
    text = read(path)
    code = re.sub(r'//.*', '', text)
    suite.equal(code.count('{'), code.count('}'), f"balanced CFG braces: {path.name}")
    suite.check(re.search(r'\bFlightPlan\b', code) is not None, f"FlightPlan node exists: {path.name}")
    waypoint_count = len(re.findall(r'\bWayPoint\b\s*\{', code, re.I))
    suite.check(waypoint_count >= 2, f"at least two fixes exist: {path.name}", str(waypoint_count))

library = read(SOURCE / "FlightPlans" / "AERISFlightPlanLibrary.cs")
for token in ('AERISFlightPlanFix', 'AERISFlightPlanDefinition', 'AERISFlightPlanLibrary',
              'NormalizeLongitude', 'ReadDouble', 'double.IsNaN', 'double.IsInfinity'):
    suite.check(token in library, f"flight-plan data guard retained: {token}")
for token in ('FlightCtrlState', 'SetArmed(', 'AERISBankDirector', 'CommandBank'):
    suite.check(token not in library, f"flight-plan library remains control-free: {token}")

nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
for token, label in (
    ('if (!Finite(rect.x)', "ND rejects non-finite/bad viewport"),
    ('GUI.BeginGroup(rect)', "ND confines drawing to its group"),
    ('finally { GUI.EndGroup(); }', "ND always closes its GUI group"),
    ('GUI.matrix = previousMatrix', "ND restores GUI matrix"),
    ('GUI.color = previousColor', "ND restores GUI color"),
    ('GUI.backgroundColor = previousBackground', "ND restores GUI background"),
    ('TextClipping.Clip', "ND clips text to its viewport"),
    ('DrawTerrainMap', "ND draws terrain moving map"),
    ('DrawLandingPlan', "ND draws runway/localizer plan geometry"),
    ('DrawLandingProfile', "ND draws glide-path profile geometry"),
): suite.check(token in nd, label)
suite.finish()
