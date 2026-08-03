#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 FMJ-style Preload status toolbar")
toolbar = read(ROOT / "Source/AERISFlightControl/UI/ToolbarBridge.cs")
window = read(ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs")
bootstrap = read(ROOT / "Source/AERISFlightControl/Core/AERISBootstrap.cs")
build_version = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
closure_active = "CP2.5 FINAL CLOSURE STANDARD PRELOAD ONLY" in build_version

for token in (
    "private static ToolbarBridge owner",
    "Duplicate ToolbarBridge initialization rejected",
    "ToolbarControl.RegisterMod(ModId, ModName)",
    "ApplicationLauncher.AppScenes.ALWAYS",
    "ApplicationLauncher.AppScenes.TRACKSTATION",
    "GameEvents.onGUIApplicationLauncherReady.Add(HandleLauncherReady)",
    "GameEvents.onGUIApplicationLauncherDestroyed.Add(HandleLauncherDestroyed)",
    "GameEvents.onGUIApplicationLauncherReady.Remove(HandleLauncherReady)",
    "GameEvents.onGUIApplicationLauncherDestroyed.Remove(HandleLauncherDestroyed)",
    "hasAppliedVisibleState = false",
    "InvalidateSceneBinding",
    "AERIS.ToolbarIcon.Stock38",
    "AERIS.ToolbarIcon.Blizzy24",
):
    suite.check(token in toolbar, "persistent scene-safe toolbar contract: " + token)

suite.check("DrawMainMenuButton" not in toolbar and "GUI.Toggle" not in toolbar,
            "no separate Main Menu overlay button; all scenes use the FMJ-style launcher owner")
suite.equal(toolbar.count("gameObject.AddComponent<ToolbarControl>()"), 1,
            "exactly one ToolbarControl component is created")
suite.equal(toolbar.count("toolbar.AddToAllToolbars("), 1,
            "exactly one toolbar registration is issued")

for token in (
    "internal bool PreloadStatusVisible",
    "ToolbarVisibleState",
    "ShowForCurrentScene",
    "HideForCurrentScene",
    "OnSceneBoundary(){Visible=false;PreloadStatusVisible=false;}",
    "DrawPreloadOnly",
    "PreloadOnlyContent",
    "PRELOAD TERRAIN CONTROL",
    "AERIS PRELOAD",
    "DrawPreloadTerrainMapsPage();",
    "Main Menu / Space Center / VAB / SPH control surface",
):
    suite.check(token in window, "non-Flight Preload control window: " + token)

preload_start = window.find("void PreloadOnlyContent()")
preload_end = window.find("void DrawPreloadTerrainMapsPage(){", preload_start)
preload = window[preload_start:preload_end]
suite.check(preload_start >= 0 and preload_end > preload_start,
            "non-Flight Preload control method is structurally present")
suite.check("DrawPreloadTerrainMapsPage();" in window,
            "non-Flight UI exposes full terrain-map control")
if closure_active:
    suite.check("PRELOAD STANDARD — CP2.5 FINAL" in window,
                "successor closure exposes the single STANDARD mode")
    suite.check("START PRELOAD BOOST" not in window and
                "STOP PRELOAD BOOST" not in window,
                "successor closure removes obsolete FULL controls")
else:
    for required in ("START PRELOAD BOOST", "STOP PRELOAD BOOST"):
        suite.check(required in window,
                    "non-Flight UI exposes legacy manual control: " + required)
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "SetArmed("):
    suite.check(forbidden not in preload,
                "non-Flight Preload surface has no flight-control authority: " + forbidden)

for token in (
    "window.ShowForCurrentScene()",
    "window.HideForCurrentScene()",
    "window.OnSceneBoundary()",
    "toolbar.InvalidateSceneBinding()",
    "toolbar.SetState(window.ToolbarVisibleState)",
    "window.DrawPreloadOnly()",
    "currentScene!=observedUiScene",
    "HandleUiSceneBoundary(currentScene)",
):
    suite.check(token in bootstrap, "bootstrap scene routing: " + token)

terrain_tick = bootstrap.find("Terrain.Tick(FlightGlobals.ActiveVessel,Landing,Airfields);")
toolbar_sync = bootstrap.find("toolbar.SetState(window.ToolbarVisibleState)")
nonflight_return = bootstrap.find("if(!inFlight)return;", toolbar_sync)
suite.check(terrain_tick >= 0 and toolbar_sync > terrain_tick and nonflight_return > toolbar_sync,
            "toolbar state is synchronized in non-Flight scenes after Preload tick and before return")

for rel, source in (("ToolbarBridge.cs", toolbar), ("AERISWindow.cs", window),
                    ("AERISBootstrap.cs", bootstrap)):
    clean = strip_csharp_comments_and_literals(source)
    suite.equal(clean.count('{'), clean.count('}'), rel + " brace balance")
    suite.equal(clean.count('('), clean.count(')'), rel + " parenthesis balance")
    suite.equal(clean.count('['), clean.count(']'), rel + " bracket balance")

suite.finish()
