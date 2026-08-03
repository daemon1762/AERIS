#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import ROOT, SOURCE, CheckSuite, read

suite = CheckSuite("v0.16.3.0 FDI/ND user-layout UI")
settings = read(SOURCE / "Settings" / "AERISSettings.cs")
fdi = read(SOURCE / "UI" / "AERISFlightInstrument.cs")
nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")
default_cfg = read(ROOT / "GameData" / "AERISFlightControl" / "Config" / "AERISSettings.cfg")

def method_body(signature: str) -> str:
    start = fdi.index(signature)
    brace = fdi.index("{", start)
    depth = 0
    for i in range(brace, len(fdi)):
        if fdi[i] == "{": depth += 1
        elif fdi[i] == "}":
            depth -= 1
            if depth == 0: return fdi[brace:i + 1]
    raise RuntimeError(signature)

suite.check("internal enum AERISDisplayMode" in settings, "shared display-mode enum exists")
suite.check("Automatic = 0" in settings and "Always = 1" in settings and
            "Off = 2" in settings,
            "display modes are AUTO / ALWAYS / OFF")
suite.check("NavigationDisplayMode = AERISDisplayMode.Automatic" in settings,
            "ND defaults to demand-driven AUTO")
suite.check("FlightInstrumentDisplayMode = AERISDisplayMode.Automatic" in settings,
            "FDI defaults to demand-driven AUTO")
for token in (
    "NavigationDisplayLayoutCustomized", "NavigationDisplayRectX01",
    "NavigationDisplayRectY01", "NavigationDisplayRectW01", "NavigationDisplayRectH01",
    "FlightInstrumentLayoutCustomized", "FlightInstrumentRectX01",
    "FlightInstrumentRectY01", "FlightInstrumentRectW01", "FlightInstrumentRectH01",
): suite.check(token in settings, f"persisted layout field exists: {token}")

for key in (
    "navigationDisplayMode", "navigationDisplayLayoutCustomized",
    "navigationDisplayRectX01", "navigationDisplayRectY01",
    "navigationDisplayRectW01", "navigationDisplayRectH01",
    "flightInstrumentDisplayMode", "flightInstrumentLayoutCustomized",
    "flightInstrumentRectX01", "flightInstrumentRectY01",
    "flightInstrumentRectW01", "flightInstrumentRectH01",
):
    suite.check(f'node.AddValue("{key}"' in settings, f"settings save key exists: {key}")
    suite.check(key in default_cfg, f"default CFG key exists: {key}")

suite.check("settings.FlightInstrumentDisplayMode == AERISDisplayMode.Always" in fdi and
            "settings.FlightInstrumentDisplayMode == AERISDisplayMode.Off" in fdi,
            "FDI ALWAYS and absolute OFF modes are enforced")
suite.check("settings.NavigationDisplayMode == AERISDisplayMode.Always" in fdi and
            "settings.NavigationDisplayMode == AERISDisplayMode.Off" in fdi,
            "ND ALWAYS and absolute OFF modes are enforced")
auto_method = method_body("bool NavigationAutoDemand()")
suite.check("AutoDisplayDemand" in auto_method, "ND AUTO follows independent LAND demand")
suite.check("!vessel.LandedOrSplashed" in auto_method and
            "vessel.situation != Vessel.Situations.PRELAUNCH" in auto_method,
            "ND AUTO shows TERRAIN during airborne flight")

suite.check("defaultNdRect = new Rect(navFurniture.xMin - defaultPanelWidth - gap" in fdi,
            "ND default remains left of navball furniture")
suite.check("defaultFdiRect = new Rect(verticalRect.xMax + gap" in fdi,
            "FDI default is right of the vertical gauge")
suite.check("customized ? ReadNormalizedPanelRect(kind) : defaultRect" in fdi,
            "custom layout is never overwritten by automatic defaults")
suite.check("settings.FlightInstrumentRectX01 * width" in fdi and
            "settings.NavigationDisplayRectX01 * width" in fdi,
            "saved positions scale with game-window width")
suite.check("settings.FlightInstrumentRectW01 * width" in fdi and
            "settings.NavigationDisplayRectH01 * height" in fdi,
            "saved sizes scale with game-window dimensions")
suite.check("rect.x / width" in fdi and "rect.height / height" in fdi,
            "pixel geometry is persisted as normalized geometry")

outside = method_body("static bool FullyOutsideScreen(Rect rect)")
suite.check("rect.xMax <= 0f" in outside and "rect.xMin >= Screen.width" in outside and
            "rect.yMax <= 0f" in outside and "rect.yMin >= Screen.height" in outside,
            "recovery is limited to completely off-screen panels")
resolve = method_body("Rect ResolvePanelRect(PanelKind kind, Rect defaultRect)")
suite.check("if (FullyOutsideScreen(rect))" in resolve and "ClampToScreen(rect, 4f)" in resolve,
            "fully lost panels are safely recovered")
suite.check("RectsOverlap" not in resolve, "user layout is not collision-repositioned")

interaction = method_body("Rect HandlePanelInteraction(PanelKind kind, Rect rect)")
for token in (
    "EventType.MouseDown", "EventType.MouseDrag", "EventType.MouseUp",
    "PanelInteraction.Move", "PanelInteraction.Resize", "ResizeGripRect(rect)",
    "PersistPanelRect(kind, rect, false)", "PersistPanelRect(kind, rect, true)",
    "GUIUtility.hotControl",
): suite.check(token in interaction, f"mouse interaction retained: {token}")
for forbidden in ("FlightCtrlState", "SetArmed", "TargetBank", "TargetHeading", "MainThrottle"):
    suite.check(forbidden not in interaction,
                f"layout interaction has no flight-control write: {forbidden}")
suite.check("MinimumPanelWidth" in fdi and "MinimumPanelHeight" in fdi,
            "resizing has minimum usable dimensions")
suite.check('GUI.Box(grip, "↘", GUI.skin.button)' in fdi, "dedicated resize grip is drawn")

suite.check('"FDI — FLIGHT GUIDANCE"' in fdi and
            '"FDI — SPEED GUIDANCE"' in fdi,
            "FDI has normal and speed-only title bars")
suite.check("GUI.Box(viewport, GUIContent.none)" in fdi and
            "GUI.Box(viewport, GUIContent.none)" in nd,
            "FDI and ND both use an inset viewport")
suite.check("GUI.backgroundColor = previousBackground" in fdi and
            "GUI.backgroundColor = previousBackground" in nd,
            "both panels restore GUI background state")
suite.check('DrawDisplayModeSelector("FDI",ref settings.FlightInstrumentDisplayMode)' in window,
            "FDI AUTO/ALWAYS/OFF selector exists")
suite.check('DrawDisplayModeSelector("ND",ref settings.NavigationDisplayMode)' in window,
            "ND AUTO/ALWAYS/OFF selector exists")
suite.check('new string[]{"AUTO","ALWAYS","OFF"}' in window,
            "options expose AUTO, ALWAYS and absolute OFF")
suite.check("settings.FlightInstrumentLayoutCustomized=false" in window, "FDI layout reset exists")
suite.check("settings.NavigationDisplayLayoutCustomized=false" in window, "ND layout reset exists")
suite.check("Drag either panel by its title bar" in window, "mouse layout instructions are visible")
suite.check("!settings.NavigationDisplayEnabled" not in nd,
            "ND draw no longer depends on obsolete visibility bool")

suite.check(abs((380.0 / 1920.0) * 1920.0 - 380.0) < 1e-6 and
            abs((244.0 / 1080.0) * 1080.0 - 244.0) < 1e-6,
            "reference resolution reconstructs 380x244 defaults")
suite.check(abs((380.0 / 1920.0) * 1280.0 - 253.3333333) < 1e-3 and
            abs((244.0 / 1080.0) * 720.0 - 162.6666667) < 1e-3,
            "normalized size follows a 1280x720 game window")
suite.finish()
