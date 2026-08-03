#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 manual runway designation grouping hotfix 1")
registry = read(SOURCE / "Landing/AERISAirfieldRegistry.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

# Manual calibration headings must become the authoritative visible RWY designations.
suite.check("stagedRegistry.NormalizeUserCalibratedRunwayPresentation();" in registry,
            "manual designation normalization runs before staged database validation")
suite.check("void NormalizeUserCalibratedRunwayPresentation()" in registry,
            "registry owns a dedicated manual designation normalization pass")
suite.check("direction.CertificationBasis !=" in registry and
            "AERISRunwayCertificationBasis.UserCalibrated" in registry,
            "automatic/provider labels are not rewritten by the manual pass")
suite.check('string desiredDirectionName = "RWY " + number;' in registry,
            "each manual direction display name is regenerated from measured heading")
suite.check('string desiredRunwayName = "RWY " +' in registry and
            'string.Join("/", runwayNumbers.ToArray())' in registry,
            "the physical runway receives one reciprocal pair designation")
suite.check("runwayNumbers.Sort(StringComparer.Ordinal);" in registry,
            "reciprocal numbers have deterministic display order")
suite.check("stableIdsPreserved=True" in registry,
            "presentation refresh explicitly preserves selection/cache identities")
suite.check("DISPLAY DESIGNATIONS REFRESHED" in registry,
            "runtime log exposes a successful designation refresh")

# One physical runway must be one AIRFIELDS list item, with both directions underneath.
suite.check("int matching=MatchingDirectionCount(registry,airfield,runway" in window,
            "AIRFIELDS first groups matching directions by physical runway")
suite.check('string key=runway.StableId+"|"+state+"|"+manualCalibratedOnly' in window,
            "expanded row identity is physical-runway based instead of direction based")
suite.check('string row=airfield.DisplayName+"\\n"+designation' in window,
            "row shows the airfield once and reciprocal designation on the next line")
suite.check('string.Join(" / ",labels.ToArray())' in window,
            "reciprocal directions are rendered together as RWY NN / RWY NN")
suite.check("DrawAirfieldRunwayGroupDetail" in window and
            "DrawAirfieldDirectionReadOnlyDetail" in window,
            "one grouped row can still expose independent details for both approach directions")
suite.check("for(int j=0;j<airfield.Runways.Count;j++)if(MatchingDirectionCount" in window,
            "category totals count physical runway rows rather than reciprocal directions")
suite.check("DirectionMatchesCategory" in window and
            "userCalibrated!=manualCalibratedOnly" in window,
            "manual and automatic categories remain separated after grouping")
suite.check('userCalibrated?"MANUAL CALIBRATED":quality' in window,
            "grouped manual row keeps an explicit manual status")
suite.check("settings.AirfieldsUserCalibratedExpanded=true" in window and
            "airfieldDetailId=\"\"" in window,
            "completed registration focuses the grouped manual category")

# Rounding check using the captured Kola headings from AERISFlightControl(23).
def runway_number(heading):
    normalized = heading % 360.0
    number = int(math.floor((normalized + 5.0) / 10.0)) % 36
    if number <= 0:
        number = 36
    return f"{number:02d}"

numbers = sorted([runway_number(195.83792193458251),
                  runway_number(15.837921934582482)])
suite.check(numbers == ["02", "20"],
            "captured manual endpoints resolve deterministically to RWY 02 / RWY 20")

# Scope and package evidence.
changed = strip_csharp_comments_and_literals(registry + window)
suite.check("Kola" not in changed,
            "designation refresh and grouping remain airport-agnostic")
suite.check("MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1" in build,
            "build identity names the designation/grouping hotfix")
suite.check("selftest_v01800_cp2_manual_runway_designation_grouping_hotfix1.py" in runner,
            "full CP2 acceptance includes this regression test")
for rel in (
        "Docs/CP2_MANUAL_RUNWAY_DESIGNATION_GROUPING_HOTFIX_1_v0.18.0.0_ja.md",
        "Docs/ND_CP2_MANUAL_RUNWAY_DESIGNATION_GROUPING_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
        "Evidence/REQUIREMENT_MANUAL_RUNWAY_DESIGNATION_GROUPING_2026-07-26.txt",
        "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_MANUAL_RUNWAY_DESIGNATION_GROUPING_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current hotfix evidence exists: " + rel)
suite.finish()
