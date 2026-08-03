#!/usr/bin/env python3
from __future__ import annotations
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 4B ATTR Presentation Recovery Hotfix 1")
renderer_path = SOURCE / "Terrain/AERISTerrainGpuTileRenderer.cs"
nav_path = SOURCE / "UI/AERISNavigationDisplay.cs"
resident_path = SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
generated_path = SOURCE / "Properties/AERISBuildVersion.generated.cs"
csproj_path = SOURCE / "AERISFlightControl.csproj"
build_path = ROOT / "build_ubuntu.sh"
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
readme_path = ROOT / "README.md"
spec_path = ROOT / "Docs/CP3_GATE4B_ATTR_PRESENTATION_RECOVERY_HOTFIX1_v0.18.0.0_ja.md"
card_path = ROOT / "Docs/ND_CP3_GATE4B_ATTR_PRESENTATION_RECOVERY_HOTFIX1_TEST_CARD_v0.18.0.0_ja.md"
acceptance_path = ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE4B_ATTR_PRESENTATION_RECOVERY_HOTFIX1.txt"
runner_path = ROOT / "Tools/run_v01800_cp3_gate4b_recovery_acceptance.py"
for path in (renderer_path, nav_path, resident_path, tiles_path, generated_path,
             csproj_path, build_path, version_path, readme_path, spec_path,
             card_path, acceptance_path, runner_path):
    suite.check(path.is_file(), "required recovery package file exists: " + path.name)

renderer = read(renderer_path)
nav = read(nav_path)
resident = read(resident_path)
tiles = read(tiles_path)
generated = read(generated_path)
csproj = read(csproj_path)
build = read(build_path)
version = read(version_path)
readme = read(readme_path)
spec = read(spec_path)
card = read(card_path)
acceptance = read(acceptance_path)
runner = read(runner_path)
for name, text in (("renderer", renderer), ("nav", nav), ("resident", resident),
                   ("tiles", tiles), ("generated", generated)):
    clean = strip_csharp_comments_and_literals(text)
    suite.check(clean.count("{") == clean.count("}"), name + " braces balanced")
    suite.check(clean.count("(") == clean.count(")"), name + " parentheses balanced")

# Rejected Gate 4B failure mechanism must be explicitly closed.
for token in ("NeedsProjectionRefresh", "projectionRefreshRequired",
              "ResolveHistorySurfaceRange", "HistoryOverscanScale",
              "frontSurfaceRangeMeters", "forcedRecoveryBackRenders",
              "UpdateReadyBuildingWatchdog", "readyBuildingViolations"):
    suite.check(token in renderer, "presentation-recovery contract exists: " + token)
suite.check("HistoryOverscanScale = 1.35f" in renderer,
            "history surface uses bounded 35 percent overscan")
suite.check("MaximumHistorySurfaceRangeMeters = 250000f" in renderer,
            "overscan never exceeds the CP3 250km FAR authority")
suite.check("system.CaptureVisible(centerLatitudeDeg,\n                centerLongitudeDeg, historySurfaceRangeMeters" in renderer,
            "FAR tile ownership is captured for the overscan history surface")
suite.check("double normalizedRange = Math.Max(1000.0, Math.Min(250000.0, rangeMeters))" in tiles and
            "range = Math.Max(1000.0, Math.Min(250000.0, range));" in tiles,
            "tile planner preserves the exact bounded internal overscan range")
plan = tiles[tiles.index("void PlanRequests"):tiles.index("void AddFoundationKeys")]
suite.check("range = AERISSettings.NormalizeNavigationRange((float)range)" not in plan,
            "overscan planner is not snapped back to user range selector steps")
suite.check("AERISNdMapProjection historySurfaceProjection" in renderer and
            "historySurfaceProjection.ResolveScaleCorrectedRenderMatrix" in renderer,
            "GPU BACK is rendered using the overscan projection")
suite.check("frontSurfaceRangeMeters = Math.Max(rangeMeters, surfaceRangeMeters)" in renderer,
            "FRONT records the actual overscan surface range")
suite.check("Math.Max(frontRangeMeters, frontSurfaceRangeMeters)" in renderer,
            "temporal reprojection unprojects from the overscan FRONT projection")

# Projection refresh is independent of tile-set generation.
projection_body = renderer[renderer.index("bool NeedsProjectionRefresh"):
                           renderer.index("void UpdateReadyBuildingWatchdog")]
for token in ("frontRangeMeters - rangeMeters", "ProjectionRefreshHeadingDeg",
              "GreatCircleDistanceMeters", "ProjectionRefreshAgeSeconds"):
    suite.check(token in projection_body, "projection refresh considers " + token)
suite.check("projectionRefreshRequired" in renderer[renderer.index("bool projectionRefreshRequired"):
                                                     renderer.index("bool rendered")],
            "projection refresh participates in BACK refreshRequired")

# Critical recovery invariant: complete FAR + no presentation forces current BACK now.
recover = renderer[renderer.index("bool readyFoundationNow"):
                   renderer.index("UpdateReadyBuildingWatchdog")]
suite.check("!present && readyFoundationNow && !gpuFailed" in recover,
            "ready FAR plus failed presentation enters immediate recovery")
suite.check("RenderBackBuffer(tiles, historySurfaceProjection" in recover,
            "recovery rerenders a GPU-only BACK immediately")
suite.check("forcedRecoveryBackRenders++" in recover,
            "forced recovery is counted")
suite.check("SwapFrontAndBack" in recover and "TryPresentReprojectedFront" in recover,
            "successful recovery swaps and re-presents in the same Draw call")
recovery_condition = recover.split("if (!present && readyFoundationNow",1)[1].split("{",1)[0]
suite.check("ViewGeneration" not in recovery_condition and "gpuContentRevision" not in recovery_condition,
            "emergency recovery entry condition does not wait for generation/revision change")

# Runtime invariant must expose the exact rejected condition.
watchdog = renderer[renderer.index("void UpdateReadyBuildingWatchdog"):
                    renderer.index("void PresentFrontDirect")]
suite.check("ReadyBuildingViolationSeconds = 1.0f" in renderer,
            "ready-building violation threshold is one second")
suite.check("[CP3_GATE4B_READY_BUILDING_VIOLATION]" in watchdog and
            "AERISLogger.Error" in watchdog,
            "persistent ready-but-building state is an ERROR")
for token in ("forced_recovery=", "ready_build_violation=", "history_surface_range="):
    suite.check(token in renderer, "periodic telemetry exposes " + token)

# The GPU-only safety boundary is not weakened by recovery.
suite.check("AERISTerrainRasterWorker.cs" not in csproj,
            "CPU raster worker remains outside the compilation set")
for token in ("CPU SAFETY FALLBACK", "CPU_FALLBACK", "UNKNOWN_TERRAIN",
              "terrainRasterWorker", "terrainThreatTexture"):
    suite.check(token not in renderer + nav, "forbidden CPU fallback stays absent: " + token)
suite.check("cpu_terrain_draw=0" in renderer,
            "runtime telemetry keeps CPU terrain drawing hard-zero")
suite.check("visible.FoundationComplete" in renderer and
            "lastBackFoundationCoverage >= 0.999f" in renderer and
            "readyFar >= visible.FarFoundationCount" in renderer,
            "FRONT authority still requires complete FAR coverage")
suite.check("TryMarkRenderReady" in resident and "TryMarkGpuReady" in resident,
            "Render-Ready/GPU-Ready state path remains intact")

# Version identity and runtime card must match this actual package.
ui='UiCheckpoint = "DEV CP3 GATE 4B — ATTR PRESENTATION RECOVERY HOTFIX 1"'
suite.check(ui in generated and ui in build,
            "generated and build-time tab labels name Recovery Hotfix 1")
suite.check("DEV CP3 GATE 4B ATTR PRESENTATION RECOVERY HOTFIX 1" in generated and
            "DEV CP3 GATE 4B ATTR PRESENTATION RECOVERY HOTFIX 1" in build,
            "assembly/build display identity names Recovery Hotfix 1")
suite.check("Presentation Recovery Hotfix 1" in version,
            "AVC package identity names Recovery Hotfix 1")
suite.check("run_v01800_cp3_gate4b_recovery_acceptance.py" in build,
            "build entrypoint executes the recovery acceptance runner")
suite.check("ready==required" in readme and "overscan" in readme,
            "README records rejected failure and overscan recovery")
suite.check("1秒" in spec and "同一Repaint" in spec and "CPU terrain drawing" in spec,
            "Japanese spec fixes recovery timing and GPU-only boundary")
suite.check("250～350m/s" in card and "360°" in card and
            "ready_build_violation=0" in card,
            "runtime card targets the rejected flight envelope")
suite.check("BUILDING for >=1 second" in acceptance and
            "CPU terrain draw count is hard zero" in acceptance,
            "formal acceptance captures ready-building and CPU-draw invariants")

# Frozen areas remain untouched.
def tree_hash(rel):
    base = ROOT / rel
    h = hashlib.sha256()
    for path in sorted(p for p in base.rglob("*") if p.is_file()):
        h.update(path.relative_to(ROOT).as_posix().encode("utf-8") + b"\0")
        h.update(hashlib.sha256(path.read_bytes()).digest())
    return h.hexdigest()
protected = {
    "Source/AERISFlightControl/AA": "79f241cf024a81851dd11e41f2ae38485554c6375318d8201b90383fbdbc1726",
    "Source/AERISFlightControl/Autopilot": "49557b5408e8e5a9b406ac1f42b06abe6ea49c29e6f96d6f2d4ad880721e544c",
    "Source/AERISFlightControl/Protect": "8d5a103421b2c88fc9f7c20414e87250dd00b265df2331adbfcd666001135438",
    "Source/AERISFlightControl/FlightState": "cb7cc694b8cee6935797b8050368a7e061249883d7a664483676678940cc080a",
    "Source/AERISFlightControl/Landing": "d99906b21e57e70e3d7ec4a592524d773ebd3a3a7a08c300076ba0395220abbe",
    "Source/AERISFlightControl/Integrations": "43cac3baf7f5455d61feb26a5ce488336936a81a4e573d4d48a44a5bdb4c7efc",
    "GameData/AERISFlightControl/Airfields": "f9c83c5877a3c3234eee4057cae2f43935634a837d500f5d2636dce1ec593ac9",
}
for rel, expected in protected.items():
    suite.check(tree_hash(rel) == expected, "protected tree byte-identical: " + rel)
for rel, expected in {
    "Source/AERISFlightControl/Performance/AERISMapDramCache.cs": "32f69a41ef84a6ecef280921fcd5ae9f13d729eba7a080ef53fee644c24679e5",
    "Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs": "3ab978c5bc405bcdfdff7a68ae8e01dc59d1d0d2be53317883b0940fe60c4688",
}.items():
    suite.check(hashlib.sha256((ROOT / rel).read_bytes()).hexdigest() == expected,
                "frozen implementation byte-identical: " + rel)
all_cs = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for token in ("StartPreloadBoost", "StopPreloadBoost", "[PRELOAD_BOOST]"):
    suite.check(token not in all_cs, "FULL BOOST remains absent: " + token)
suite.check("AERISRuntimeLane.SafetyLand" not in renderer,
            "recovery adds no Flight safety-lane work")
suite.check("ReadAllBytes" not in renderer and "File.ReadAll" not in renderer,
            "recovery adds no synchronous SSD read path")

suite.finish()
