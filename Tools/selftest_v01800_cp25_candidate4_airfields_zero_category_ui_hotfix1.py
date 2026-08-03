#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, extract_method, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2.5 Candidate 4 Airfields Zero-Category UI Hotfix 1")
window = read(SOURCE / "UI/AERISWindow.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_CANDIDATE4_AIRFIELDS_ZERO_CATEGORY_UI_HOTFIX1.txt")
spec = read(ROOT / "Docs/CP25_CANDIDATE4_AIRFIELDS_ZERO_CATEGORY_UI_HOTFIX_1_v0.18.0.0_ja.md")
card = read(ROOT / "Docs/ND_CP25_CANDIDATE4_AIRFIELDS_ZERO_CATEGORY_UI_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md")
evidence = read(ROOT / "Evidence/RUNTIME_VIDEO_REVIEW_AIRFIELDS_ZERO_CATEGORY_2026-07-31.txt")
method_start = window.index("void DrawAirfieldCategory(")
method_end = window.index("bool DirectionMatchesCategory(", method_start)
method = window[method_start:method_end]
clean = strip_csharp_comments_and_literals(method)

for label in (
    '"USER CALIBRATED — MANUAL"',
    '"CERTIFIED — AUTOMATIC / PROVIDER"',
    '"PROVISIONAL — NON-SELECTABLE"',
    '"FAILED"', '"PENDING"', '"REVALIDATION"'):
    suite.check(label in window, "AIRFIELDS category remains present: " + label)

suite.check("if(count==0){" in method, "all zero-count categories enter one generic guard")
suite.check("manualCalibratedOnly&&count==0" not in method,
            "zero-count guard is no longer limited to USER CALIBRATED")
suite.check("if(expanded){expanded=false;settings.Save();" in method,
            "persisted expanded state is forced closed and saved")
suite.check('AERISLogger.Info("[AIRFIELDS/UI] category forced collapsed because count=0: "+label)' in method,
            "forced collapse is observable with the category label")
suite.check('string detailSuffix="|"+state+"|"+manualCalibratedOnly' in method and
            "airfieldDetailId.EndsWith(detailSuffix,System.StringComparison.Ordinal)" in method,
            "stale detail ownership is cleared only for the empty category")
suite.check('string emptyLabel="▶ "+label+" (0)"' in method,
            "zero-count category keeps a stable visible header")
suite.check("bool previousEnabled=GUI.enabled" in method and "GUI.enabled=false" in method and
            "finally{GUI.enabled=previousEnabled;}" in method,
            "disabled header restores the global GUI enabled state")
suite.check("GUILayout.Button(emptyLabel,responsiveButtonStyle" in method,
            "zero-count header uses the responsive AIRFIELDS style")
suite.check(method.index("if(count==0){") < method.index("string categoryLabel"),
            "zero-count path exits before toggle allocation")
suite.check("return;\n   }\n   string categoryLabel" in method,
            "zero-count path exits before any child layout")
suite.check('WrappedAirfieldLabel("None.")' not in method,
            "zero-count categories no longer create a None child row")
suite.check("GUILayout.Toggle(expanded,categoryLabel" in method,
            "nonzero categories retain the normal expandable header")
suite.check("if(!expanded)return;" in method and "registry.Airfields" in method,
            "nonzero collapsed and expanded category behavior remains")

identity = "AIRFIELDS ZERO-CATEGORY UI HOTFIX 1"
suite.check(identity in build_version and identity in build,
            "generated and build identities name the hotfix")
suite.check("Airfields Zero-Category UI Hotfix 1" in version,
            "KSP metadata names the hotfix")
suite.check("selftest_v01800_cp25_candidate4_airfields_zero_category_ui_hotfix1.py" in runner,
            "CP2.5 runner executes the dedicated regression")
suite.check("all six" in contract.lower() and "count==0" in contract,
            "acceptance contract fixes all six categories")
suite.check("全カテゴリ" in spec and "0件" in spec and "巨大" in spec,
            "Japanese specification records the reported layout fault")
suite.check("KSP起動は1回" in card and "グレーアウト" in card and "巨大な空白" in card,
            "runtime card uses one startup and checks disabled zero-count headers")
suite.check("Reviewed before source modification" in evidence and
            "manualCalibratedOnly==true" in evidence,
            "pre-fix video review and source correlation are bundled")

for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache"):
    suite.check(forbidden not in clean,
                "UI hotfix adds no forbidden authority/content: " + forbidden)

suite.finish()
