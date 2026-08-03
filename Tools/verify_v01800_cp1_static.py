#!/usr/bin/env python3
import json
import re
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import ROOT, CheckSuite, read, sha256, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP1 static package verification")
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
data = json.loads(version_path.read_text(encoding="utf-8"))
v = data["VERSION"]
semver = "%d.%d.%d.%d" % (v["MAJOR"], v["MINOR"], v["PATCH"], v.get("BUILD", 0))
suite.equal(semver, "0.18.0.0", "VERSION is v0.18.0.0")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
suite.check(('Display = "AERIS Flight Control v0.18.0.0 DEV CP1' in generated or
             'Display = "AERIS Flight Control v0.18.0.0 DEV CP2' in generated),
            "generated display identifies CP1 or a later checkpoint")
csproj = read(ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj")
compile_items = re.findall(r'<Compile Include="([^"]+)"', csproj)
missing = [name for name in compile_items if not (ROOT / "Source/AERISFlightControl" / name.replace('\\','/')).is_file()]
suite.check(not missing, "every csproj Compile item exists", ", ".join(missing))
suite.check('Performance\\AERISNavigationDisplayPipeline.cs' in compile_items,
            "CP1 navigation pipeline is in the project")

# Basic lexical integrity catches common time-limit/checkpoint truncations without claiming a C# build.
for relative in (
    "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs",
    "Source/AERISFlightControl/Performance/AERISNavigationDisplayPipeline.cs",
    "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs",
    "Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs",
    "Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs",
    "Source/AERISFlightControl/Settings/AERISSettings.cs",
):
    clean = strip_csharp_comments_and_literals(read(ROOT / relative))
    suite.equal(clean.count('{'), clean.count('}'), relative + " brace balance")
    suite.equal(clean.count('('), clean.count(')'), relative + " parenthesis balance")

bank = ROOT / "Source/AERISFlightControl/Autopilot/AERISBankDirector.cs"
expected_bank = "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7"
suite.equal(sha256(bank), expected_bank, "BANK source remains byte-identical")
all_source = "\n".join(read(path) for path in (ROOT / "Source/AERISFlightControl").rglob("*.cs"))
for legacy in ("class AERISNavDirector", "class AERISRouteSpeedPlanner",
               "class AERISTrajectoryPrimitives", "TryStartNavLanding"):
    suite.check(legacy not in all_source, "legacy NAV remains absent: " + legacy)
landing = strip_csharp_comments_and_literals(read(ROOT / "Source/AERISFlightControl/Landing/AERISLandingFoundation.cs"))
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "ApplyAaNative", ".SetArmed("):
    suite.check(forbidden not in landing, "LAND foundation remains control-free: " + forbidden)

banned = []
for path in ROOT.rglob('*'):
    if not path.is_file():
        continue
    low = path.name.lower()
    if low.endswith(('.dll','.exe','.pdb','.pyc')) or '__pycache__' in path.parts:
        banned.append(str(path.relative_to(ROOT)))
suite.check(not banned, "source package contains no binaries/debug/python cache", ", ".join(banned[:10]))
suite.finish()
