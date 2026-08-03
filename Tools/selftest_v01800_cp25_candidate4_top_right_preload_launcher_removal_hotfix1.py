#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import (ROOT, SOURCE, CheckSuite, read, extract_method,
                            strip_csharp_comments_and_literals, text_sha256)

suite = CheckSuite("v0.18.0.0 CP2.5 Candidate 4 Top-Right Preload Launcher Removal Hotfix 1")
window = read(SOURCE / "UI/AERISWindow.cs")
toolbar = read(SOURCE / "UI/ToolbarBridge.cs")
bootstrap = read(SOURCE / "Core/AERISBootstrap.cs")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
blocks = read(SOURCE / "Terrain/AERISTerrainBlockPipeline.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
contracts = read(SOURCE / "Terrain/AERISTerrainPreloadContracts.cs")
database = read(SOURCE / "Terrain/AERISTerrainPreloadDatabase.cs")
sync = read(SOURCE / "AA/SyncModuleControlSurface.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_CANDIDATE4_TOP_RIGHT_PRELOAD_LAUNCHER_REMOVAL_HOTFIX1.txt")
spec = read(ROOT / "Docs/CP25_CANDIDATE4_TOP_RIGHT_PRELOAD_LAUNCHER_REMOVAL_HOTFIX_1_v0.18.0.0_ja.md")
test_card = read(ROOT / "Docs/ND_CP25_CANDIDATE4_TOP_RIGHT_PRELOAD_LAUNCHER_REMOVAL_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md")

preload_draw = extract_method(window, "DrawPreloadOnly")
clean_draw = strip_csharp_comments_and_literals(preload_draw)
suite.check("if(HighLogic.LoadedSceneIsFlight||!PreloadStatusVisible)return;" in preload_draw,
            "closed non-Flight Preload UI exits without drawing an overlay")
for forbidden in ('GUI.Button(launcher,"AERIS PRELOAD")', "Rect launcher=",
                  "Screen.width-174f", "Screen.width - 174f"):
    suite.check(forbidden not in clean_draw,
                "top-right launcher code is absent: " + forbidden)
suite.check("GUILayout.Window(Id,rect,_=>PreloadOnlyContent()" in preload_draw,
            "visible Preload window still uses the existing full control surface")
suite.check(preload_draw.index("!PreloadStatusVisible") < preload_draw.index("GUILayout.Window"),
            "hidden state returns before any window allocation")

# Toolbar remains the sole non-Flight entry point.
for token in ("ToolbarControl.RegisterMod", "ApplicationLauncher.AppScenes.ALWAYS",
              "toolbar.AddToAllToolbars(", "HandleShow", "HandleHide"):
    suite.check(token in toolbar, "toolbar entry remains available: " + token)
suite.check("toolbar.Initialise(()=>{if(window!=null)window.ShowForCurrentScene();}" in bootstrap and
            "()=>{if(window!=null)window.HideForCurrentScene();}" in bootstrap,
            "toolbar still owns non-Flight show/hide routing")
suite.check("internal void ShowForCurrentScene(){if(HighLogic.LoadedSceneIsFlight)Visible=true;else PreloadStatusVisible=true;}" in window,
            "toolbar show opens the non-Flight Preload window")
suite.check("internal void HideForCurrentScene(){if(HighLogic.LoadedSceneIsFlight)Visible=false;else PreloadStatusVisible=false;}" in window,
            "toolbar hide closes the non-Flight Preload window")
suite.check("window.DrawPreloadOnly()" in bootstrap and
            "toolbar.SetState(window.ToolbarVisibleState)" in bootstrap,
            "bootstrap continues non-Flight draw and toolbar synchronization")

# FULL BOOST controls remain in the Preload Maps page only.
preload_page_start = window.index("void DrawPreloadTerrainMapsPage(){")
preload_page_end = window.index("void DrawPreloadBodyRow(", preload_page_start)
preload_page = window[preload_page_start:preload_page_end]
suite.equal(window.count('"START PRELOAD BOOST — FULL"'), 1,
            "one START FULL BOOST control remains")
suite.equal(window.count('"STOP PRELOAD BOOST — FULL"'), 1,
            "one STOP FULL BOOST control remains")
suite.check('"START PRELOAD BOOST — FULL"' in preload_page and
            '"STOP PRELOAD BOOST — FULL"' in preload_page,
            "FULL BOOST controls remain inside PRELOAD MAPS")
suite.check("StartPreloadBoost()" in preload_page and "StopPreloadBoost()" in preload_page,
            "tab controls retain the Candidate 4 boost actions")
suite.check("StartPreloadBoost" not in preload_draw and "StopPreloadBoost" not in preload_draw,
            "DrawPreloadOnly adds no direct boost shortcut")

# Candidate 4 throughput implementation stays exact unless the explicit
# Full Boost Backpressure successor is present. Both states are hash locked.
baseline_expected = {
    "Performance/AERISWorkerScheduler.cs": "6be25104ba1d2b0b890e3fa2adbfc74b5df1aed7d47398d50de9966f32b765ee",
    "Terrain/AERISTerrainBlockPipeline.cs": "94dde68be33357bfe0b67773c57e171d69a94760bbe9cf21ec0afcc251888f27",
    "Terrain/AERISTerrainPreloadBuilder.cs": "2bde72029a151446e75fbc38888331ccea10ac3ee0d56105a4f8294ee2978de3",
    "Terrain/AERISTerrainPreloadContracts.cs": "58d5636b645ada68b149509314f19203cfdfd91c3382158a6cb973f49bc392fb",
    "Terrain/AERISTerrainPreloadDatabase.cs": "de4325530b019d812fdc4566fe767758f559d6b21d7a295df56cd24a34124df9",
    "AA/SyncModuleControlSurface.cs": "93d5161d9280e26e45ee3cfe6a3083f0e58a518216d67815d8534430151f6336",
}
backpressure_expected = {
    "Performance/AERISWorkerScheduler.cs": "fa6fcc42e70b2bfc421d532e6e9719da1e96f0ce94359b4664b1ef38ba54a654",
    "Terrain/AERISTerrainBlockPipeline.cs": "d39def4033c9d37fe46d90a9d678b1d438738140189feb5e473a6ddde4b01e5a",
    "Terrain/AERISTerrainPreloadBuilder.cs": "8e7349b9f12473573d72ff87edda67e232c554851213d253c353b6b8d98f3c57",
    "Terrain/AERISTerrainPreloadContracts.cs": "8947a20ee86acac9f4091d8fe5daaebfb3f36e38ba7525a287dac85caf206e1f",
    "Terrain/AERISTerrainPreloadDatabase.cs": "de4325530b019d812fdc4566fe767758f559d6b21d7a295df56cd24a34124df9",
    "AA/SyncModuleControlSurface.cs": "93d5161d9280e26e45ee3cfe6a3083f0e58a518216d67815d8534430151f6336",
}
downstream_expected = {
    "Performance/AERISWorkerScheduler.cs": "fa6fcc42e70b2bfc421d532e6e9719da1e96f0ce94359b4664b1ef38ba54a654",
    "Terrain/AERISTerrainBlockPipeline.cs": "d39def4033c9d37fe46d90a9d678b1d438738140189feb5e473a6ddde4b01e5a",
    "Terrain/AERISTerrainPreloadBuilder.cs": "fdb94bdb4abc742477ebd3763b57afd32747404c0cff1e4da7a01c42b1759318",
    "Terrain/AERISTerrainPreloadContracts.cs": "1664be55de113a8cab6b03cd7b13546be151fd014ed24b2758249f3d86470972",
    "Terrain/AERISTerrainPreloadDatabase.cs": "de4325530b019d812fdc4566fe767758f559d6b21d7a295df56cd24a34124df9",
    "AA/SyncModuleControlSurface.cs": "93d5161d9280e26e45ee3cfe6a3083f0e58a518216d67815d8534430151f6336",
}
downstream_active = "FULL BOOST DOWNSTREAM COMMIT HOTFIX 1" in build_version
airfields_zero_category_active = "AIRFIELDS ZERO-CATEGORY UI HOTFIX 1" in build_version
successor_active = "FULL BOOST BACKPRESSURE HOTFIX 1" in build_version
expected = downstream_expected if downstream_active else (backpressure_expected if successor_active else baseline_expected)
for rel, digest in expected.items():
    suite.check(text_sha256(read(SOURCE / rel)) == digest,
                "Candidate 4 protected source is exact for active acceptance lineage: " + rel)

for token in ("StandardPreloadThroughput", "FullBoostMaxActive",
              "ScheduleChunkSuperBatch", "FLIGHT_SAFETY",
              "GPU COMPUTE CAPABLE — NO PRELOAD KERNEL ASSET"):
    joined = scheduler + blocks + builder + contracts + database
    suite.check(token in joined, "Candidate 4 throughput contract remains: " + token)

# Identity and acceptance assets.
identity = "TOP-RIGHT PRELOAD LAUNCHER REMOVAL HOTFIX 1"
suite.check(identity in build_version and identity in build,
            "generated and build identities name the UI hotfix")
suite.check("Top-Right Preload Launcher Removal Hotfix 1" in version,
            "KSP metadata names the UI hotfix")
suite.check("selftest_v01800_cp25_candidate4_top_right_preload_launcher_removal_hotfix1.py" in runner,
            "CP2.5 runner executes the new hotfix test")
suite.check("top-right" in contract.lower() and "ToolbarControl" in contract,
            "acceptance contract fixes launcher removal and toolbar ownership")
suite.check("画面右上" in spec and "Toolbar" in spec and "START PRELOAD BOOST" in spec,
            "Japanese specification records the exact UI boundary")
suite.check("Main Menu" in test_card and "VAB" in test_card and "SPH" in test_card,
            "runtime card covers all requested non-Flight scenes")

# No new authority or unrelated implementation.
clean = strip_csharp_comments_and_literals(window)
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache"):
    suite.check(forbidden not in clean,
                "UI hotfix adds no forbidden authority/content: " + forbidden)

suite.finish()
