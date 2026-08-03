#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2.5 Integrated Acceptance Candidate 1")
window = read(SOURCE / "UI/AERISWindow.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_INTEGRATED_ACCEPTANCE_CANDIDATE1.txt")
spec = read(ROOT / "Docs/CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_1_v0.18.0.0_ja.md")
test_card = read(ROOT / "Docs/ND_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_1_TEST_CARD_v0.18.0.0_ja.md")
evidence = read(ROOT / "Evidence/RUNTIME_ACCEPTANCE_CP25_GATE4_HOTFIX2_AERIS31_2026-07-29.txt")

suite.check('DrawAirfieldCategory(registry,AERISRunwayCertificationState.Certified,"USER CALIBRATED — MANUAL"' in window,
            "manual-calibrated category remains present")
suite.check("if(count==0)" in window,
            "empty manual-calibrated category is covered by the generic zero-count guard")
suite.check("if(expanded){expanded=false;settings.Save();" in window,
            "persisted expanded state is normalized to collapsed when the manual list becomes empty")
suite.check('AERISLogger.Info("[AIRFIELDS/UI] category forced collapsed because count=0: "+label)' in window,
            "zero-count normalization remains observable")
suite.check('string emptyLabel="▶ "+label+" (0)"' in window,
            "empty category keeps a stable visible header")
suite.check("bool previousEnabled=GUI.enabled" in window and
            "GUI.enabled=false" in window and "GUI.enabled=previousEnabled" in window,
            "empty header is non-interactive and GUI enabled state is restored")
suite.check("GUILayout.Button(emptyLabel,responsiveButtonStyle" in window,
            "empty category header uses the same responsive visual style")
suite.check("return;\n   }\n   string categoryLabel" in window,
            "empty category exits before toggle and child layout")
suite.check('if(count==0){WrappedAirfieldLabel("None.");return;}' not in window,
            "successor hotfix removes empty child rows for non-manual categories")

suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 1" in build_version and
            "EMPTY MANUAL CATEGORY UI HOTFIX 1" in build_version,
            "generated build identity names the integrated candidate and UI fix")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 1" in build and
            "EMPTY MANUAL CATEGORY UI HOTFIX 1" in build,
            "Ubuntu build entrypoint generates the integrated identity")
suite.check("Integrated Acceptance Candidate 1" in version and
            "Empty Manual Category UI Hotfix 1" in version,
            "KSP version metadata identifies the integrated candidate")
suite.check("selftest_v01800_cp25_integrated_acceptance_candidate1.py" in runner,
            "full CP2.5 runner executes the integrated candidate regression test")
suite.check("GATE 1" in contract and "GATE 2" in contract and
            "GATE 3" in contract and "GATE 4" in contract,
            "integrated acceptance contract covers every CP2.5 gate")
suite.check("手動補正済み" in spec and "0件" in spec and
            "総合受入" in test_card,
            "Japanese specification and total test card cover the reported UI fault")
suite.check("synchronousSSD=0" in evidence and "result=PASS" in evidence and
            "airfields=93" in evidence,
            "Gate 4 Hotfix 2 runtime PASS evidence is bundled")

code = strip_csharp_comments_and_literals(window)
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache"):
    suite.check(forbidden not in code,
                "integrated UI fix adds no " + forbidden + " authority/content")

suite.finish()
