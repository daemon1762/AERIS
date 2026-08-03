#!/usr/bin/env python3
from pathlib import Path
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 3.1 Foundation UI Compile Hotfix 1")
tile_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
window_path = SOURCE / "UI/AERISWindow.cs"
build_path = ROOT / "build_ubuntu.sh"
generated_path = SOURCE / "Properties/AERISBuildVersion.generated.cs"
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
runner_path = ROOT / "Tools/run_v01800_cp3_gate31_compile_hotfix1_acceptance.py"
acceptance_path = ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE3.1_FOUNDATION_UI_COMPILE_HOTFIX1.txt"
for path in (tile_path, window_path, build_path, generated_path, version_path,
             runner_path, acceptance_path):
    suite.check(path.is_file(), "required hotfix file exists: " + path.name)

tiles = read(tile_path)
window = read(window_path)
build = read(build_path)
generated = read(generated_path)
version = read(version_path)
runner = read(runner_path) if runner_path.is_file() else ""
acceptance = read(acceptance_path) if acceptance_path.is_file() else ""

clean_tiles = strip_csharp_comments_and_literals(tiles)
clean_window = strip_csharp_comments_and_literals(window)
suite.check(clean_tiles.count("{") == clean_tiles.count("}"),
            "tile-system braces remain balanced")
suite.check(clean_window.count("{") == clean_window.count("}"),
            "SYSTEM UI braces remain balanced")

contracts = {
    "FoundationGlobalCount": "lastFoundationGlobalCount",
    "FoundationFarCount": "lastFoundationFarCount",
    "FoundationMissingCount": "lastFoundationMissingCount",
    "FoundationRequestedCount": "lastFoundationRequestedCount",
}
for member, backing in contracts.items():
    declaration = ("internal int " + member + " { get { return " + backing + "; } }")
    suite.check(declaration in tiles,
                member + " is exposed as a read-only tile-system getter")
    suite.check(("DisplayTiles." + member) in window,
                "SYSTEM UI consumes " + member)
    suite.check((member + " { get;") not in tiles and
                ("set {" not in declaration),
                member + " cannot mutate foundation ownership")

# Resolve every direct SYSTEM-page Foundation* member reference against an actual
# AERISTerrainTileSystem declaration. This specifically catches the CS1061 class
# of error that escaped the previous brace/definite-assignment static checks.
refs = sorted(set(re.findall(r"DisplayTiles\.(Foundation[A-Za-z0-9_]+)", window)))
declared = set(re.findall(
    r"internal\s+(?:int|bool|long|string|double|float)\s+"
    r"(Foundation[A-Za-z0-9_]+)\s*\{", tiles))
missing = sorted(set(refs) - declared)
suite.check(bool(refs), "SYSTEM UI foundation member references are detected")
suite.check(not missing,
            "all SYSTEM UI foundation member references resolve on AERISTerrainTileSystem",
            ", ".join(missing))
suite.equal(refs, sorted(contracts),
            "SYSTEM UI uses exactly the four stable foundation counters")

ui = ('UiCheckpoint = "DEV CP3 GATE 3.1 — VIEWPORT-AUTHORITATIVE FAR BASE & '
      'VIRTUAL DETAIL FOUNDATION — COMPILE HOTFIX 1"')
display = ('DEV CP3 GATE 3.1 VIEWPORT AUTHORITATIVE FAR BASE '
           'VIRTUAL DETAIL FOUNDATION COMPILE HOTFIX 1')
suite.check(ui in generated and ui in build,
            "generated and build-time tab labels identify Compile Hotfix 1")
suite.check(display in generated and display in build,
            "assembly display identity identifies Compile Hotfix 1")
suite.check("COMPILE HOTFIX 1" in version.upper(),
            "AVC identity identifies Compile Hotfix 1")
suite.check("run_v01800_cp3_gate31_compile_hotfix1_acceptance.py" in build,
            "build entrypoint invokes the Compile Hotfix 1 runner")
suite.check("selftest_v01800_cp3_gate31_foundation_ui_compile_hotfix1.py" in runner,
            "active runner invokes the CS1061 regression guard")
suite.check("FoundationGlobalCount" in acceptance and "CS1061" in acceptance,
            "acceptance record identifies the repaired member contract")

suite.finish()
