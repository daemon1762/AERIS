#!/usr/bin/env python3
from __future__ import annotations
import hashlib
import math
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 4B AERIS Terrain Temporal Reconstruction (ATTR)")
renderer_path = SOURCE / "Terrain/AERISTerrainGpuTileRenderer.cs"
nav_path = SOURCE / "UI/AERISNavigationDisplay.cs"
backend_path = SOURCE / "Performance/AERISGpuAssistBackend.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
projection_path = SOURCE / "Terrain/AERISNdMapProjection.cs"
resident_path = SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs"
rasterizer_path = SOURCE / "Terrain/AERISTerrainGpuTileRasterizer.cs"
generated_path = SOURCE / "Properties/AERISBuildVersion.generated.cs"
csproj_path = SOURCE / "AERISFlightControl.csproj"
build_path = ROOT / "build_ubuntu.sh"
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
readme_path = ROOT / "README.md"
acceptance_path = ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE4B_AERIS_TERRAIN_TEMPORAL_RECONSTRUCTION_ATTR.txt"
spec_path = ROOT / "Docs/CP3_GATE4B_AERIS_TERRAIN_TEMPORAL_RECONSTRUCTION_ATTR_v0.18.0.0_ja.md"
card_path = ROOT / "Docs/ND_CP3_GATE4B_ATTR_TEST_CARD_v0.18.0.0_ja.md"
runner_path = ROOT / "Tools/run_v01800_cp3_gate4b_acceptance.py"

paths = [renderer_path, nav_path, backend_path, tiles_path, projection_path,
         resident_path, rasterizer_path, generated_path, csproj_path, build_path,
         version_path, readme_path, acceptance_path, spec_path, card_path, runner_path]
for path in paths:
    suite.check(path.is_file(), "required Gate 4B package file exists: " + path.name)

renderer = read(renderer_path)
nav = read(nav_path)
backend = read(backend_path)
tiles = read(tiles_path)
projection = read(projection_path)
resident = read(resident_path)
rasterizer = read(rasterizer_path)
generated = read(generated_path)
csproj = read(csproj_path)
build = read(build_path)
version = read(version_path)
readme = read(readme_path)
acceptance = read(acceptance_path)
spec = read(spec_path)
card = read(card_path)
runner = read(runner_path)

for name, text in (("renderer", renderer), ("navigation display", nav),
                   ("backend", backend), ("tile system", tiles),
                   ("projection", projection), ("resident cache", resident),
                   ("rasterizer", rasterizer), ("generated version", generated)):
    clean = strip_csharp_comments_and_literals(text)
    suite.check(clean.count("{") == clean.count("}"), name + " C# braces are balanced")
    suite.check(clean.count("(") == clean.count(")"), name + " C# parentheses are balanced")

# Gate 4A GPU-only and render-ready foundation must remain intact.
for token in ("RenderTexture backTarget", "RenderTexture frontTarget",
              '"AERIS_ND_TERRAIN_BACK"', '"AERIS_ND_TERRAIN_FRONT"'):
    suite.check(token in renderer, "GPU FRONT/BACK contract remains: " + token)
suite.check("GUI.DrawTextureWithTexCoords(plot, backTarget" not in renderer,
            "incomplete BACK target is never presented directly")
suite.check("visible.FoundationComplete" in renderer and
            "lastBackFoundationCoverage >= 0.999f" in renderer and
            "readyFar >= visible.FarFoundationCount" in renderer,
            "formal FRONT swap still requires complete FAR authority")
suite.check("AERISTerrainRenderReadyHeightField" in rasterizer and
            "TryMarkRenderReady" in resident and "TryMarkGpuReady" in resident,
            "Render-Ready -> GPU-Ready state path remains available")
suite.check("AERISTerrainRasterWorker.cs" not in csproj,
            "legacy CPU terrain raster worker remains excluded")
for token in ("CPU SAFETY FALLBACK", "CPU_FALLBACK", "UNKNOWN_TERRAIN",
              "terrainRasterWorker", "terrainTexture", "terrainThreatTexture"):
    suite.check(token not in renderer + nav,
                "GPU-only active path omits forbidden CPU fallback token: " + token)
suite.check("cpu_terrain_draw=0" in renderer,
            "GPU-only runtime telemetry keeps CPU terrain draw at zero")

# Temporal geographic reprojection is deterministic and history-based.
for token in ("TryPresentReprojectedFront", "ProjectHistoryGuiPoint",
              "AffineCoversViewport", "frontTerrainGeneration",
              "frontBodyName", "frontBodyRadiusMillimetres"):
    suite.check(token in renderer, "temporal history contract exists: " + token)
suite.check("oldProjection.UnprojectGuiToLatitudeLongitude" in renderer and
            "currentProjection.ProjectLatitudeLongitudeToGui" in renderer,
            "history reprojection uses geographic old-GUI -> lat/lon -> current-GUI mapping")
suite.check("frontTerrainGeneration != visible.TerrainGeneration" in renderer,
            "history rejects stale terrain generation")
suite.check("ageSeconds > 20f" in renderer,
            "history age is bounded")
suite.check("rangeRatio < 0.45f || rangeRatio > 1.08f" in renderer,
            "history range ratio is bounded and large zoom-out is fail-closed")
suite.check("Mathf.DeltaAngle(frontMapHeadingDeg,\n                mapHeadingDeg)) > 55f" in renderer,
            "history heading change is bounded")
suite.check("distortion > 0.06f" in renderer,
            "history affine distortion is bounded")
suite.check("confidence < 0.35f" in renderer,
            "low-confidence history is rejected")
suite.check("AffineCoversViewport(q00, axisX, axisY, determinant)" in renderer,
            "history must cover the current viewport before presentation")
suite.check("GUI.matrix = previousMatrix * transform" in renderer and
            "GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, width, height),\n                    frontTarget" in renderer,
            "accepted history is reprojected from the existing GPU FRONT texture")
suite.check("frontTarget" in renderer[renderer.index("bool TryPresentReprojectedFront"):],
            "temporal path consumes GPU FRONT only")

# Differential presentation: stable frames must not rebuild/swap every repaint.
for token in ("gpuContentRevision", "frontContentRevision",
              "lastBackAttemptViewGeneration", "lastBackAttemptContentRevision",
              "ShouldRefreshBackBuffer", "nextBackRefreshRealtime"):
    suite.check(token in renderer, "differential refresh contract exists: " + token)
suite.check(renderer.count("gpuContentRevision++") >= 2,
            "GPU content revision advances when GPU-visible tile content changes")
suite.check("frontContentRevision = gpuContentRevision" in renderer,
            "FRONT captures the content revision committed at swap")
suite.check("visible.ViewGeneration != frontViewGeneration" in renderer or
            "frontViewGeneration != visible.ViewGeneration" in renderer,
            "view generation participates in refresh decision")
suite.check("frontContentRevision != gpuContentRevision" in renderer,
            "content revision participates in refresh decision")
suite.check("if (refreshAllowed)" in renderer,
            "BACK rendering is conditional rather than unconditional per repaint")
suite.check("nextBackRefreshRealtime = Time.realtimeSinceStartup + 0.20f" in renderer,
            "incomplete BACK retry is throttled to at most 5 Hz")
suite.check("backRenderFrames++" in renderer and "skippedBackRenderFrames++" in renderer,
            "rendered and skipped BACK refreshes are separately counted")
suite.check("historyReprojectFrames++" in renderer and "directFrontFrames++" in renderer,
            "temporal and direct FRONT presentation frames are separately counted")
suite.check("[CP3_GATE4B_TEMPORAL]" in renderer,
            "Gate 4B periodic temporal telemetry is emitted")
for token in ("history_conf=", "back_render=", "back_skip=", "history_frames=",
              "history_reject=", "direct_frames=", "cpu_terrain_draw=0"):
    suite.check(token in renderer, "temporal telemetry exposes " + token)

# Virtual detail is a presentation-quality contract, not exact Route/Local residency.
suite.check('return "VIRTUAL ROUTE"' in renderer and
            'return "VIRTUAL LOCAL"' in renderer and
            'return "FAR DIRECT"' in renderer,
            "quality tiers expose FAR DIRECT / VIRTUAL ROUTE / VIRTUAL LOCAL")
suite.check("AERISTerrainTileLod.Far, point.Priority" in tiles,
            "Predictive Forward Corridor continues to warm FAR base tiles")
suite.check("AddExistingExactDetailBridge" in tiles,
            "existing exact detail remains an optional bridge, not the normal base")
suite.check("AddLandingPointWithPins" in tiles and "AERISTerrainTileLod.Land" in tiles,
            "LAND exact payload path remains available for safety-critical use")

# Existing continuity telemetry now represents actual temporal history reuse.
suite.check("terrainTileRenderer.HistoryReprojectFrames" in nav,
            "performance continuity reuse is sourced from temporal history frames")
suite.check("terrainTileRenderer.FrontBufferPresented" in nav,
            "continuity seeded state is sourced from GPU FRONT presence")
cont_idx = nav.index("RecordTerrainContinuityState")
suite.check("0L," in nav[cont_idx:cont_idx + 600],
            "unknown/CPU backing count is hard zero in GPU-only Gate 4B")
suite.check("TEMPORAL GPU PRESENTATION / CPU DATA AUTHORITY" in backend,
            "GPU backend identity names temporal GPU presentation")

# Algorithm-level contract checks matching AffineCoversViewport semantics.
def affine_covers(origin, axis_x, axis_y, margin=0.015):
    det = axis_x[0] * axis_y[1] - axis_x[1] * axis_y[0]
    if abs(det) < 1.0e-6:
        return False
    inv00, inv01 = axis_y[1] / det, -axis_y[0] / det
    inv10, inv11 = -axis_x[1] / det, axis_x[0] / det
    for px, py in ((0.0, 0.0), (1.0, 0.0), (0.0, 1.0), (1.0, 1.0)):
        dx, dy = px - origin[0], py - origin[1]
        u = inv00 * dx + inv01 * dy
        v = inv10 * dx + inv11 * dy
        if u < -margin or u > 1.0 + margin or v < -margin or v > 1.0 + margin:
            return False
    return True

suite.check(affine_covers((0.0, 0.0), (1.0, 0.0), (0.0, 1.0)),
            "identity history covers the current viewport")
suite.check(affine_covers((-0.5, -0.5), (2.0, 0.0), (0.0, 2.0)),
            "zoom-in from a wider history frame remains coverable")
suite.check(not affine_covers((0.25, 0.25), (0.5, 0.0), (0.0, 0.5)),
            "zoom-out exposing terrain outside old history is rejected")
suite.check(not affine_covers((0.4, 0.0), (1.0, 0.0), (0.0, 1.0)),
            "translated history that leaves a new outer strip is rejected")
suite.check(not affine_covers((0.0, 0.0), (0.0, 0.0), (0.0, 1.0)),
            "degenerate affine history is rejected")

# Version / package identity must be current everywhere.
ui = 'UiCheckpoint = "DEV CP3 GATE 4B — AERIS TERRAIN TEMPORAL RECONSTRUCTION (ATTR)"'
suite.check(ui in generated and ui in build,
            "generated and build-time tab labels name Gate 4B ATTR")
suite.check("DEV CP3 GATE 4B AERIS TERRAIN TEMPORAL RECONSTRUCTION ATTR" in generated and
            "DEV CP3 GATE 4B AERIS TERRAIN TEMPORAL RECONSTRUCTION ATTR" in build,
            "assembly/build display identity names Gate 4B ATTR")
suite.check("GATE 4B" in version.upper() and "TEMPORAL" in version.upper(),
            "AVC identity names Gate 4B temporal reconstruction")
suite.check("run_v01800_cp3_gate4b_acceptance.py" in build,
            "build entrypoint invokes the Gate 4B acceptance runner")
suite.check("Gate 4B" in readme and "Temporal" in readme,
            "README exposes Gate 4B temporal reconstruction")
suite.check("FAR" in acceptance and "cpu_terrain_draw=0" in acceptance,
            "formal acceptance keeps FAR authority and CPU-draw-zero requirement")
suite.check("VIRTUAL ROUTE" in spec and "VIRTUAL LOCAL" in spec,
            "Japanese specification defines reconstructed virtual detail tiers")
suite.check("大きなzoom-out" in spec,
            "spec explicitly records fail-closed large zoom-out limitation")
suite.check("250" in card and "350" in card and "360°" in card,
            "runtime card covers high-speed and full-heading temporal acceptance")

# Frozen safety/control/map areas must remain byte-identical.
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
modified = renderer + nav + backend + rasterizer + resident
suite.check("AERISRuntimeLane.SafetyLand" not in modified,
            "Gate 4B adds no Flight safety-lane work")
suite.check("ReadAllBytes" not in modified and "File.ReadAll" not in modified,
            "Gate 4B adds no synchronous SSD read path")

suite.finish()
