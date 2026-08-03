#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 responsive airfields UI and resize hotfix 1")
window = read(SOURCE / "UI/AERISWindow.cs")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

# Resize geometry must stay controllable and on-screen.
suite.check("const float ResizeGripSize=42f" in window and
            "const float ResizeGripWidth=48f" in window and
            "const float FooterHeight=46f" in window,
            "resize grip and footer have a practical pointer target")
suite.check("Screen.width-12f" in window and "Screen.height-12f" in window,
            "window maximum dimensions are clamped to the current screen")
suite.check("resizeStartPointerScreen=GUIUtility.GUIToScreenPoint(e.mousePosition)" in window,
            "resize starts from a fixed screen-space pointer anchor")
suite.check("pointerScreen-resizeStartPointerScreen" in window,
            "resize delta is derived from the fixed pointer anchor")
suite.check("resizeAccumulatedDelta+=e.delta" not in window,
            "moving grip no longer accumulates unstable window-local deltas")
suite.check('rect.width<560f?"Resize ↘"' in window,
            "narrow windows use a compact footer hint")

# Candidate 9 supersedes the old global responsive-wrap policy. Only AIRFIELD
# airport/runway selection rows retain variable height; all other AERIS buttons
# use fixed geometry so text cannot move neighbouring controls.
suite.check("WrappedControlHeight(GUIStyle style,string text,float width,float minimum,float maximum)" in window,
            "shared wrapped-control height calculation remains for AIRFIELD selection rows")
suite.check("style.CalcHeight(new GUIContent(text??string.Empty)" in window,
            "AIRFIELD selection-row height is still measured by the active Unity GUI style")
suite.check("airfieldRowButtonStyle.wordWrap=true" in window and
            "responsiveButtonStyle.wordWrap=false" in window and
            "airfieldActionButtonStyle.wordWrap=false" in window,
            "only AIRFIELD selection rows retain button wrapping")
suite.check("WrappedControlHeight(airfieldRowButtonStyle,rowLabel" in window,
            "AIRFIELD airport/runway selection row remains variable-size by requirement")
suite.check(window.count("WrappedControlHeight(")==3 and
            window.count("WrappedControlHeight(airfieldRowButtonStyle,rowLabel")==2,
            "only the two SYSTEM > AIRFIELDS selection/detail row families derive size from wrapped text")
suite.check("const int FixedTabColumns=3" in window and
            "GUILayout.Width(FixedTabButtonWidth)" in window,
            "main and SYSTEM tabs use fixed row/size geometry")
suite.check("rect.width>=760f?5:(rect.width>=560f?3:2)" not in window and
            "rect.width<620f" not in window,
            "window width no longer changes tab or AIRFIELD action-button placement")
suite.check("MasterButtonHeight" in window and
            "wordWrap=false,clipping=TextClipping.Clip,fixedHeight=MasterButtonHeight" in window,
            "MASTER button no longer grows with standby text")
suite.check("GUILayout.Width(FixedAirfieldActionWidth)" in window and
            "GUILayout.Height(AirfieldActionButtonHeight)" in window,
            "AIRFIELD action buttons remain fixed while selection rows may vary")

# Long enum identifiers must gain visible wrap opportunities.
suite.check("HumanizeIdentifier(direction.FailureCode.ToString())" in window,
            "failure identifiers are converted to spaced human-readable text")
suite.check("char.IsUpper(current)" in window and "char.IsLower(value[i-1])" in window,
            "PascalCase failure names receive word boundaries")

suite.check("Kola" not in strip_csharp_comments_and_literals(window),
            "layout and resize repair remains airport-agnostic")
suite.check("RESPONSIVE AIRFIELDS UI LAYOUT RESIZE HOTFIX 1" in build,
            "build identity names the responsive layout and resize hotfix")
suite.check("selftest_v01800_cp2_responsive_airfields_ui_resize_hotfix1.py" in runner,
            "full CP2 acceptance includes this regression test")
for rel in (
        "Docs/CP2_RESPONSIVE_AIRFIELDS_UI_LAYOUT_RESIZE_HOTFIX_1_v0.18.0.0_ja.md",
        "Docs/ND_CP2_RESPONSIVE_AIRFIELDS_UI_LAYOUT_RESIZE_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
        "Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl21_AIRFIELDS_UI_RESIZE_OVERLAP.txt",
        "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_RESPONSIVE_AIRFIELDS_UI_LAYOUT_RESIZE_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current hotfix evidence exists: " + rel)
suite.finish()
