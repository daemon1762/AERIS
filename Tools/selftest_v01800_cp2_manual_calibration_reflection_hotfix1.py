#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 manual calibration reflection hotfix 1")
registry = read(SOURCE / "Landing/AERISAirfieldRegistry.cs")
witness = read(SOURCE / "Landing/AERISRunwayWitnessLibrary.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
nd = read(SOURCE / "UI/AERISNavigationDisplay.cs")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

# Runtime evidence from AERISFlightControl(23): the persisted pair existed and was certified,
# but failed automatic rows remained visible and the ND selected arbitrary finite directions.
suite.check("HasAuthoritativeUserCalibratedPair" in registry,
            "registry exposes an authoritative completed manual-pair predicate")
suite.check("count >= 2" in registry and
            "AERISRunwayCertificationBasis.UserCalibrated" in registry,
            "authoritative state requires two certified user-calibrated directions")
suite.check("IsSupersededByUserCalibration" in registry,
            "automatic/provider directions can be classified as superseded")
suite.check("direction.CertificationBasis !=" in registry and
            "HasAuthoritativeUserCalibratedPair(airfield)" in registry,
            "only non-manual directions are superseded by a completed pair")

# Counts, status and selection must no longer keep stale automatic failure entries active.
suite.check("if (IsSupersededByUserCalibration(airfields[i], direction)) continue;" in registry,
            "certification approach totals exclude superseded provider directions")
suite.check("IsSupersededByUserCalibration(airfield, direction)" in registry,
            "airfield state evaluation excludes superseded provider directions")
suite.check("bool manualAuthoritative = HasAuthoritativeUserCalibratedPair(airfield);" in registry,
            "selectable direction enumeration detects authoritative manual pairs")
suite.check("manualAuthoritative && direction.CertificationBasis !=" in registry,
            "selection exposes only manual directions once a pair is authoritative")
suite.check("VisibleDirectionCount(airfield)" in registry,
            "validation status denominator excludes superseded automatic entries")

# AIRFIELDS must visibly reflect the result rather than hiding it below stale FAILED entries.
manual_pos = window.find('"USER CALIBRATED — MANUAL"')
auto_pos = window.find('"CERTIFIED — AUTOMATIC / PROVIDER"')
suite.check(manual_pos >= 0 and auto_pos > manual_pos,
            "manual category is presented before automatic/provider certification")
suite.check("registry.IsSupersededByUserCalibration(airfield,direction)" in window,
            "AIRFIELDS rows suppress provider failures replaced by manual calibration")
suite.check("settings.AirfieldsUserCalibratedExpanded=true" in window,
            "successful two-point completion opens the manual category immediately")
suite.check("settings.AirfieldsFailedExpanded=false" in window and
            "airfieldsScroll=Vector2.zero" in window,
            "completion closes stale failure lists and returns the user to the reflected pair")
suite.check("registry.HasStoredUserCalibration(airfield)" in window,
            "automatic category focus changes only after a usable pair is stored")

# Persistent status must be rebuilt from current file content, not retain a stale quarantine message.
suite.check("RefreshCalibrationStatusFromStoredState();" in witness,
            "witness reload refreshes calibration status from persisted records")
suite.check("COMMITTED USER RUNWAY(S)" in witness and
            "RECIPROCAL PAIRS READY" in witness,
            "complete calibration status reports the committed reciprocal pair")
suite.check("HasUsableCalibration(AERISAirfieldDefinition airfield)" in witness,
            "UI can query committed calibration immediately after MARK A/B")

# ND publication must choose certified directions and prioritize the manual pair.
suite.check("ResolveNavigationDirectionPair(registry, airfield, runway" in nd,
            "ND capture resolves an explicit operational direction pair")
suite.check("candidate.HasCertifiedGeometry" in nd and
            "registry.EffectiveState(candidate)" in nd,
            "ND ignores failed finite geometry when selecting endpoints")
suite.check("manualAuthoritative && candidate.CertificationBasis !=" in nd,
            "ND publishes only manual geometry when a user pair is authoritative")
suite.check("ReciprocalPairError(first, candidate)" in nd,
            "ND chooses the closest reciprocal second direction")
suite.check("bool certifiedRunway = true" in nd and
            "bool provisionalRunway = false" in nd,
            "resolved pair is published as certified and never provisional")

# Scope, identity and package evidence.
changed = strip_csharp_comments_and_literals(registry + witness + window + nd)
suite.check("Kola" not in changed,
            "reflection fix remains generic and contains no airport-specific branch")
suite.check("MANUAL CALIBRATION REFLECTION HOTFIX 1" in build,
            "build identity names the reflection hotfix")
suite.check("selftest_v01800_cp2_manual_calibration_reflection_hotfix1.py" in runner,
            "full CP2 acceptance includes this regression test")
for rel in (
        "Docs/CP2_MANUAL_CALIBRATION_REFLECTION_HOTFIX_1_v0.18.0.0_ja.md",
        "Docs/ND_CP2_MANUAL_CALIBRATION_REFLECTION_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
        "Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl23_MANUAL_PAIR_NOT_REFLECTED.txt",
        "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_MANUAL_CALIBRATION_REFLECTION_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current hotfix evidence exists: " + rel)
suite.finish()
