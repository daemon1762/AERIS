#!/usr/bin/env python3
from __future__ import annotations
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 4A Render-Ready GPU-Only FAR")
renderer_path = SOURCE / "Terrain/AERISTerrainGpuTileRenderer.cs"
rasterizer_path = SOURCE / "Terrain/AERISTerrainGpuTileRasterizer.cs"
resident_path = SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs"
nav_path = SOURCE / "UI/AERISNavigationDisplay.cs"
window_path = SOURCE / "UI/AERISWindow.cs"
backend_path = SOURCE / "Performance/AERISGpuAssistBackend.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
planner_path = SOURCE / "Terrain/AERISTerrainViewportFoundationPlanner.cs"
contracts_path = SOURCE / "Terrain/AERISTerrainTileContracts.cs"
generated_path = SOURCE / "Properties/AERISBuildVersion.generated.cs"
csproj_path = SOURCE / "AERISFlightControl.csproj"
build_path = ROOT / "build_ubuntu.sh"
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
acceptance_path = ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE4A_RENDER_READY_HEIGHT_FIELD_GPU_ONLY_FAR_PRESENTATION.txt"
spec_path = ROOT / "Docs/CP3_GATE4A_RENDER_READY_HEIGHT_FIELD_GPU_ONLY_FAR_PRESENTATION_v0.18.0.0_ja.md"
card_path = ROOT / "Docs/ND_CP3_GATE4A_RENDER_READY_HEIGHT_FIELD_GPU_ONLY_FAR_PRESENTATION_TEST_CARD_v0.18.0.0_ja.md"
runner_path = ROOT / "Tools/run_v01800_cp3_gate4a_acceptance.py"

paths = [renderer_path, rasterizer_path, resident_path, nav_path, window_path,
         backend_path, tiles_path, planner_path, contracts_path, generated_path,
         csproj_path, build_path, version_path, acceptance_path, spec_path,
         card_path, runner_path]
for path in paths:
    suite.check(path.is_file(), "required package file exists: " + path.name)

renderer = read(renderer_path)
rasterizer = read(rasterizer_path)
resident = read(resident_path)
nav = read(nav_path)
window = read(window_path)
backend = read(backend_path)
tiles = read(tiles_path)
planner = read(planner_path)
contracts = read(contracts_path)
generated = read(generated_path)
csproj = read(csproj_path)
build = read(build_path)
version = read(version_path)
acceptance = read(acceptance_path)
spec = read(spec_path)
card = read(card_path)
runner = read(runner_path)

for name, text in (("renderer", renderer), ("rasterizer", rasterizer),
                   ("resident", resident), ("navigation display", nav),
                   ("backend", backend), ("tile system", tiles),
                   ("planner", planner), ("contracts", contracts),
                   ("generated version", generated)):
    clean = strip_csharp_comments_and_literals(text)
    suite.check(clean.count("{") == clean.count("}"), name + " C# braces are balanced")
    suite.check(clean.count("(") == clean.count(")"), name + " C# parentheses are balanced")

# Immutable render-ready contract.
suite.check("class AERISTerrainRenderReadyHeightField" in rasterizer,
            "render-ready height-field contract exists")
render_ready_block = rasterizer[rasterizer.index("class AERISTerrainRenderReadyHeightField"):
                                rasterizer.index("internal sealed class AERISTerrainGpuTileRasterResult")]
suite.check("UnityEngine" not in render_ready_block,
            "render-ready payload contains no UnityEngine dependency")
for token in ("ElevationMeters", "Water", "Valid", "Shade", "Triangles",
              "ContourSegments", "CoastlineSegments", "EstimatedBytes"):
    suite.check(token in render_ready_block, "render-ready payload includes " + token)
suite.check("AERISTerrainGpuTileRasterResult :\n        AERISTerrainRenderReadyHeightField" in rasterizer,
            "bounded worker result derives from render-ready payload")
suite.check("request.Tile = request.Tile.CloneImmutable()" in rasterizer,
            "worker boundary keeps an immutable tile snapshot")
suite.check("AERISRuntimeLane.GeneralCompute" in rasterizer,
            "render-ready build stays on GeneralCompute lane")
suite.check("AERISRuntimeLane.SafetyLand" not in rasterizer,
            "render-ready build never occupies Flight safety lane")

# Resident presentation state machine.
for token in ("TryMarkRenderReady", "TryMarkGpuReady",
              "TryDemotePresentationState", "TryPromotePresentationState"):
    suite.check(token in resident, "resident presentation contract exists: " + token)
suite.check("AERISResidentTileState.RamResident" in resident and
            "AERISResidentTileState.RenderReady" in resident and
            "AERISResidentTileState.GpuReady" in resident,
            "resident state chain includes RAM/RENDER/GPU")
suite.check("entry.State = next" in resident,
            "presentation promotion commits the requested state")
suite.check("target != AERISResidentTileState.RamResident" in resident and
            "target != AERISResidentTileState.RenderReady" in resident,
            "presentation demotion is bounded to safe states")

# GPU front/back and invisible incomplete back.
for token in ("RenderTexture backTarget", "RenderTexture frontTarget",
              '"AERIS_ND_TERRAIN_BACK"', '"AERIS_ND_TERRAIN_FRONT"'):
    suite.check(token in renderer, "GPU double-buffer contract exists: " + token)
suite.check("RenderTexture.active = backTarget" in renderer,
            "terrain is composed into the hidden BACK target")
suite.check("GUI.DrawTextureWithTexCoords(plot, frontTarget" in renderer,
            "only the FRONT target is presented to the ND")
suite.check("GUI.DrawTextureWithTexCoords(plot, backTarget" not in renderer,
            "BACK target is never presented directly")
suite.check("visible.FoundationComplete" in renderer and
            "lastBackFoundationCoverage >= 0.999f" in renderer and
            "readyFar >= visible.FarFoundationCount" in renderer,
            "buffer swap requires complete FAR foundation authority")
suite.check("SwapFrontAndBack" in renderer and
            "frontTarget = backTarget" in renderer and "backTarget = previousFront" in renderer,
            "FRONT/BACK swap is explicit and atomic")
suite.check("blockedIncompleteSwaps++" in renderer,
            "incomplete BACK attempts are counted instead of displayed")
suite.check("IsFrontBufferCompatible" in renderer and
            "frontBufferValid" in renderer and "frontTerrainGeneration" in renderer,
            "a compatible complete GPU FRONT may remain visible while BACK builds")
suite.check("frontTerrainGeneration != visible.TerrainGeneration" in renderer,
            "stale terrain generations cannot reuse FRONT")
suite.check("frontBodyRadiusMillimetres" in renderer and
            "frontTrackUp != trackUp" in renderer and "frontOrientation != orientation" in renderer,
            "FRONT reuse validates body and presentation geometry")

# FAR authority and virtual detail behavior.
suite.check("int required = Math.Max(0, visible.FarFoundationCount)" in renderer,
            "GPU completion denominator is FAR-only")
suite.check("tile.Key.Lod != AERISTerrainTileLod.Global" in renderer and
            "tile.Key.Lod != AERISTerrainTileLod.Far" in renderer,
            "foundation readiness examines only GLOBAL/FAR")
suite.check("AERISTerrainViewportFoundationPlanner.Build" in tiles,
            "real viewport planner remains the source of FAR foundation keys")
suite.check("AddCoarseCoverage" not in tiles,
            "fixed 3x3 coarse planner remains removed")
suite.check("HorizontalMeters = Math.Max(1.0, rangeMeters * 1.30)" in
            read(SOURCE / "Terrain/AERISNdMapProjection.cs"),
            "viewport authority retains the ND 1.30 horizontal span")
suite.check("AddExistingExactDetailBridge" in tiles and
            "ExactDetailPayloadExists" in tiles,
            "existing exact detail remains an optional overlay bridge")
suite.check("AERISTerrainTileLod.Far, point.Priority" in tiles,
            "Predictive Forward Corridor continues to warm FAR")
suite.check("AddLandingPointWithPins" in tiles and "AERISTerrainTileLod.Land" in tiles,
            "LAND exact microtile demand remains available")

# CPU terrain presentation is removed, not hidden.
suite.check("AERISTerrainRasterWorker.cs" not in csproj,
            "legacy CPU terrain raster worker is not compiled")
suite.check((SOURCE / "Terrain/AERISTerrainRasterWorker.cs").is_file() and
            "RETIRED IN CP3 GATE 4A" in read(SOURCE / "Terrain/AERISTerrainRasterWorker.cs"),
            "legacy CPU terrain worker is retained only as an uncompiled audit tombstone")
for token in ("terrainRasterWorker", "terrainTexture", "terrainThreatTexture",
              "EnsureTerrainTextures", "LegacyTerrainGridMatchesView"):
    suite.check(token not in nav, "navigation display has no CPU terrain path: " + token)
for token in ("callerFallbackAvailable", "CPU_FALLBACK", "UNKNOWN_TERRAIN",
              "CPU SAFETY FALLBACK"):
    suite.check(token not in renderer + nav + backend,
                "active terrain presentation omits forbidden fallback: " + token)
suite.check("CpuTerrainDrawCount { get { return cpuTerrainDrawCount; } }" in renderer and
            "cpu_terrain_draw=0" in renderer,
            "CPU terrain draw telemetry is permanently zero")
suite.check("TERRAIN GPU BUILDING" in nav,
            "initial incomplete GPU state is reported without CPU substitution")
suite.check("AERISTerrainGpuDrawState.Partial" in nav and
            "DrawLabel(plot, \"TERRAIN GPU BUILDING " in nav,
            "partial state renders only a building indication")
suite.check("UNITY GPU-ONLY FAR PRESENTATION" in renderer and
            "GPU-ONLY PRESENTATION / CPU DATA AUTHORITY" in backend,
            "backend identity distinguishes CPU data authority from presentation")

# Resource lifecycle.
suite.check("SuspendViewport()" in nav and
            "terrainTileRenderer.SuspendViewport()" in nav,
            "ND/flight viewport suspension releases GPU presentation")
suite.check("if (settings != null && settings.TerrainDisplayMode ==" in nav and
            "terrainTileRenderer.SuspendViewport();" in nav[nav.index("void DrawTerrainMap"):],
            "Terrain OFF actively suspends GPU resources")
suite.check("ReleaseGpuResources();" in renderer[renderer.index("!AutomaticGpuCapabilityAvailable"):
            renderer.index("Event currentEvent")],
            "GPU disable/capability failure releases resources")
suite.check("DestroyRenderTargets" in renderer and "DestroyUnityObject(terrainMaterial)" in renderer,
            "GPU release destroys targets and materials")
release_block = renderer[renderer.index("void ReleaseGpuResources"):
                         renderer.index("public void Dispose")]
suite.check("renderReadyFields.Clear" not in release_block,
            "ordinary GPU release preserves render-ready CPU payload")
dispose_block = renderer[renderer.index("public void Dispose"):]
suite.check("renderReadyFields.Clear()" in dispose_block,
            "Dispose releases render-ready CPU payload")
suite.check("TryDemotePresentationState" in renderer,
            "GPU/render-ready eviction demotes Resident Cache presentation state")
suite.check("requested.Contains(pair.Key) || entries.ContainsKey(pair.Key)" in renderer,
            "render-ready prune preserves authority while a GPU entry depends on it")
suite.check("!entries.ContainsKey(cacheKey ?? string.Empty)" in renderer,
            "render-ready removal never demotes a still-live GPU entry to RAM resident")

# Version, UI and acceptance identity.
ui = 'UiCheckpoint = "DEV CP3 GATE 4A — RENDER-READY HEIGHT FIELD & GPU-ONLY FAR PRESENTATION — COMPILE HOTFIX 2"'
suite.check(ui in generated and ui in build,
            "generated and build-time tab labels name Gate 4A")
suite.check("DEV CP3 GATE 4A RENDER READY HEIGHT FIELD GPU ONLY FAR PRESENTATION" in generated and
            "DEV CP3 GATE 4A RENDER READY HEIGHT FIELD GPU ONLY FAR PRESENTATION" in build,
            "assembly/build identity names Gate 4A")
suite.check("CP3 GATE 4A RENDER-READY HEIGHT FIELD" in version.upper(),
            "AVC identity names Gate 4A")
suite.check("run_v01800_cp3_gate4a_acceptance.py" in build,
            "build entrypoint invokes the Gate 4A runner")
suite.check("RR/GPU" in window,
            "SYSTEM page exposes render-ready and GPU-ready resident counts")
for member in ("FoundationGlobalCount", "FoundationFarCount",
               "FoundationMissingCount", "FoundationRequestedCount"):
    suite.check(("internal int " + member) in tiles and member in window,
                "SYSTEM foundation member resolves on tile system: " + member)
suite.check("CPU terrain raster presentation" in acceptance and
            "Only the GPU FRONT RenderTexture" in acceptance,
            "acceptance contract forbids CPU presentation and fixes FRONT authority")
suite.check("CPU safety fallback" in spec and "TERRAIN GPU BUILDING" in spec,
            "Japanese specification records GPU-only behavior")
suite.check("360°旋回" in card and "cpu_terrain_draw=0" in card,
            "runtime card covers full-heading GPU-only acceptance")

# Frozen controls / map metadata / absence of unsafe paths.
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
    suite.check(tree_hash(rel) == expected,
                "protected tree remains byte-identical: " + rel)
for rel, expected in {
    "Source/AERISFlightControl/Performance/AERISMapDramCache.cs": "32f69a41ef84a6ecef280921fcd5ae9f13d729eba7a080ef53fee644c24679e5",
    "Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs": "3ab978c5bc405bcdfdff7a68ae8e01dc59d1d0d2be53317883b0940fe60c4688",
}.items():
    suite.check(hashlib.sha256((ROOT / rel).read_bytes()).hexdigest() == expected,
                "frozen implementation remains byte-identical: " + rel)
all_cs = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for token in ("StartPreloadBoost", "StopPreloadBoost", "[PRELOAD_BOOST]"):
    suite.check(token not in all_cs, "FULL BOOST remains absent: " + token)
modified = renderer + rasterizer + resident + nav + planner + contracts
suite.check("AERISRuntimeLane.SafetyLand" not in modified,
            "Gate 4A adds no Flight safety-lane work")
plan_section = tiles[tiles.index("void PlanRequests"):tiles.index("void AddLandingPointWithPins")]
new_supply = renderer + rasterizer + resident + nav + planner + contracts + plan_section
suite.check("ReadAllBytes" not in new_supply and "File.ReadAll" not in new_supply,
            "Gate 4A adds no synchronous SSD read path")

suite.finish()
