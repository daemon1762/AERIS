#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 manual calibrated runway separation preservation hotfix 1")
registry = read(SOURCE / "Landing/AERISAirfieldRegistry.cs")
witness = read(SOURCE / "Landing/AERISRunwayWitnessLibrary.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
settings = read(SOURCE / "Settings/AERISSettings.cs")
default_cfg = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

# Runtime failure boundary from AERISFlightControl(22): CHECK HERE must never erase a complete A/B pair.
guard = registry.find("AERISRunwayCertificationBasis.UserCalibrated")
quarantine = registry.find("RecordPlacementMismatch(")
suite.check(guard >= 0 and quarantine > guard,
            "user-calibrated verification guard executes before automatic quarantine")
suite.check("result=USER_CALIBRATION_PRESERVED" in registry and
            "automaticQuarantine=False" in registry,
            "registry reports a protected manual calibration instead of reloading it away")
suite.check("USE CLEAR" in registry and "THEN MARK A/B" in registry,
            "replacement of a complete manual calibration requires an explicit clear")

witness_guard = witness.find("calibration.IsUsable")
witness_clear = witness.find("calibration.HasStart = false", witness_guard)
suite.check(witness_guard >= 0 and witness_clear > witness_guard,
            "witness storage protects a usable pair before endpoint-clearing quarantine code")
suite.check("COMPLETE USER A/B CALIBRATION PRESERVED" in witness and
            "result=MANUAL_CALIBRATION_PRESERVED" in witness,
            "storage layer independently refuses automatic deletion of manual endpoints")
suite.check("if (!created && calibration != null && calibration.IsUsable)" in witness,
            "only a complete existing A/B pair receives the preservation lock")

# AIRFIELDS presentation: manual entries are operationally certified but not mixed with automatic/provider certification.
suite.check('"CERTIFIED — AUTOMATIC / PROVIDER"' in window,
            "automatic/provider certification has its own list category")
suite.check('"USER CALIBRATED — MANUAL"' in window,
            "manual A/B runway pairs have a separate list category")
suite.check("manualCalibratedOnly" in window and
            "userCalibrated!=manualCalibratedOnly" in window,
            "certified rows are partitioned by certification basis")
suite.check('userCalibrated?"MANUAL CALIBRATED":quality' in window,
            "manual rows carry an unambiguous status label")
suite.check("listed separately from automatic/provider certification" in window,
            "detail text explains the operational status without calling it automatic certification")
suite.check("CHECK HERE IS DISABLED FOR THIS ENTRY" in window,
            "manual entries do not expose the destructive generic placement button")
suite.check("USE CLEAR EXPLICITLY BEFORE REPLACING THE ENDPOINTS" in window,
            "manual replacement workflow is explicit in the UI")

# New category is closed by default and persisted independently.
suite.check("CurrentAirfieldsUiLayoutRevision = 2" in settings,
            "airfields layout migration revision advances for the new category")
suite.check("internal bool AirfieldsUserCalibratedExpanded;" in settings,
            "manual category has an independent persisted expansion state")
suite.check('"airfieldsUserCalibratedExpanded", false' in settings,
            "existing settings default the manual category closed")
suite.check("settings.AirfieldsUserCalibratedExpanded = false;" in settings,
            "migration and reset close the manual category")
suite.check('node.AddValue("airfieldsUserCalibratedExpanded"' in settings,
            "manual category expansion state is saved")
suite.check("airfieldsUiLayoutRevision = 2" in default_cfg and
            "airfieldsUserCalibratedExpanded = False" in default_cfg,
            "shipped defaults keep the manual category collapsed")

# Scope and package identity.
changed = strip_csharp_comments_and_literals(registry + witness + window + settings)
suite.check("Kola" not in changed,
            "preservation and category separation remain airport-agnostic")
suite.check("MANUAL CALIBRATED RUNWAY SEPARATION PRESERVATION HOTFIX 1" in build,
            "build identity names this hotfix")
suite.check("selftest_v01800_cp2_manual_calibrated_runway_separation_preservation_hotfix1.py" in runner,
            "full CP2 acceptance includes this regression test")
for rel in (
        "Docs/CP2_MANUAL_CALIBRATED_RUNWAY_SEPARATION_PRESERVATION_HOTFIX_1_v0.18.0.0_ja.md",
        "Docs/ND_CP2_MANUAL_CALIBRATED_RUNWAY_SEPARATION_PRESERVATION_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
        "Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl22_MANUAL_CALIBRATION_DISAPPEARANCE.txt",
        "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_MANUAL_CALIBRATED_RUNWAY_SEPARATION_PRESERVATION_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current hotfix evidence exists: " + rel)
suite.finish()
