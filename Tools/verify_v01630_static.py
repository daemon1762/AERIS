#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01630_testlib import (ROOT, SOURCE, CheckSuite, all_text, csharp_balance,
    compile_includes, package_files, parse_version, read, source_files)

suite = CheckSuite("v0.16.3.0 static verification")
version_txt, version_json, version_cs = parse_version()
suite.equal(version_txt, "0.16.3.0", "VERSION is v0.16.3.0")
suite.equal(version_json, version_txt, "KSP version JSON matches VERSION")
suite.equal(version_cs, version_txt, "generated assembly semantic version matches VERSION")

csproj = read(SOURCE / "AERISFlightControl.csproj")
includes = compile_includes(csproj)
missing = [value for value in includes if not (SOURCE / Path(value.replace('\\', '/'))).is_file()]
suite.check(not missing, "every csproj Compile item exists", ", ".join(missing))
for required in (
    "Landing\\AERISAirfieldModels.cs", "Landing\\AERISAirfieldConfigParser.cs",
    "Landing\\AERISAirfieldProviders.cs", "Landing\\AERISAirfieldRegistry.cs",
    "Landing\\AERISRunwaySurveyCatalog.cs",
    "Landing\\AERISModRunwaySurveyResolver.cs",
    "Landing\\AERISLandingFoundation.cs",
    "Terrain\\AERISTerrainPerformance.cs", "Terrain\\AERISTerrainRasterWorker.cs",
    "Terrain\\AERISTerrainAwareness.cs", "UI\\AERISNavigationDisplay.cs",
    "UI\\AERISFlightInstrument.cs",
):
    suite.check(required in includes, f"required source compiled: {required}")
for removed in (
    "Autopilot\\AERISNavDirector.cs", "Autopilot\\AERISRouteSpeedPlanner.cs",
    "Autopilot\\AERISTrajectoryPrimitives.cs",
):
    suite.check(removed not in includes, f"csproj excludes {removed}")

joined = all_text(source_files(".cs"))
for forbidden in (
    "AERISNavDirector", "AERISNavFlightPlan", "AERISNavWaypoint",
    "AERISRouteSpeedPlanner", "AERISTrajectoryPrimitives", "core.Nav",
    "SetNavMaster", "SampleNavDiagnostics", "TryStartNavLanding",
    "TryGetCompatibleNavPlans", "AERISNavLandingRequest", "AERISNavPlanDescriptor",
    "NAV_LANDING", "fdr_nav_diagnostics", "ApNav", "apNav",
):
    suite.check(forbidden not in joined, f"forbidden legacy identifier absent: {forbidden}")

balance_failures = []
for path in source_files(".cs"):
    ok, detail = csharp_balance(path)
    if not ok: balance_failures.append(f"{path.relative_to(ROOT)}: {detail}")
suite.check(not balance_failures, "all C# files have balanced lexical structure",
            "; ".join(balance_failures[:5]))

bad_artifacts = [str(p.relative_to(ROOT)) for p in package_files()
                 if p.suffix.lower() in (".dll", ".pdb", ".pyc") or "__pycache__" in p.parts]
suite.check(not bad_artifacts, "source package contains no DLL/PDB/Python cache",
            ", ".join(bad_artifacts[:10]))

suite.check((ROOT / "Docs" / "RELEASE_NOTES_v0.16.3.0_ja.md").is_file(),
            "current release notes are present")
suite.check((ROOT / "Docs" / "AERIS12_TERRAIN_ND_DESIGN_ja.md").is_file(),
            "terrain ND design document is present")
suite.check((ROOT / "Docs" / "AERIS12_TERRAIN_ND_TEST_CARD_ja.md").is_file(),
            "terrain ND runtime test card is present")
suite.check((ROOT / "Docs" / "AERIS12_MOD_RUNWAY_SURVEY_DESIGN_ja.md").is_file(),
            "MOD runway survey design is present")
suite.check((ROOT / "Docs" / "AERIS12_MOD_RUNWAY_SURVEY_TEST_CARD_ja.md").is_file(),
            "MOD runway runtime test card is present")
suite.check((ROOT / "GameData" / "AERISFlightControl" / "Airfields" /
             "Defaults" / "01_Stock_DLC_Foundation.cfg").is_file(),
            "bundled Airfield Registry exists")
suite.finish()
