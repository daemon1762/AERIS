#!/usr/bin/env python3
from __future__ import annotations
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read

suite = CheckSuite("v0.18.0.0 CP2 progressive HD overlay and profile persistence")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
profiles = read(ROOT / "Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs")
contracts = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

for token in (
    "AERISTerrainGpuDrawState",
    "MeasureViewportCoverage",
    "samplesPerAxis = 25",
    "coverageRects",
    "ResolveScaleCorrectedRenderMatrix",
    "GL.Clear(true, !lastContinuitySeeded",
    "GUI.DrawTextureWithTexCoords(plot, renderTarget, uv, true)",
    "GUI.DrawTextureWithTexCoords",
):
    suite.check(token in renderer, "progressive transparent HD overlay: " + token)

suite.check("HasCompleteViewportCoverage" not in renderer,
            "all-or-nothing viewport promotion gate was removed")
suite.check("AERISTerrainGpuDrawState.Partial" in renderer and
            "AERISTerrainGpuDrawState.Complete" in renderer,
            "renderer reports partial and complete coverage explicitly")
suite.check("lastCoverageFraction >= 0.999f" in renderer,
            "complete state is reserved for effectively full viewport coverage")
suite.check(renderer.find("MeasureViewportCoverage") < renderer.find("EnsureResources(plot"),
            "coverage is measured before GPU target composition")

cpu_draw = nd.find("GUI.DrawTexture(plot, texture")
gpu_draw = nd.find("terrainTileRenderer.Draw(plot")
suite.check(cpu_draw >= 0 and gpu_draw >= 0 and cpu_draw < gpu_draw,
            "aligned CPU terrain is drawn before progressive HD overlay")
for token in (
    "AERISTerrainGpuDrawState.Partial",
    "HD TERRAIN BUILD ",
    "LegacyTerrainGridMatchesView",
    "TERRAIN TILE COVERAGE LOADING",
    "terrain.TrackUp != trackUp",
    "Mathf.DeltaAngle(terrain.MapHeadingDeg",
):
    suite.check(token in nd, "safe progressive fallback: " + token)
suite.check("displacement > Math.Max(250.0, rangeMeters * 0.05)" in nd,
            "remote PLAN never stretches the aircraft-centred legacy grid")

for token in (
    "ResolveRoot(ConfigNode.Load(PathName))",
    "ResolveRoot(ConfigNode.Load(temporary))",
    "loaded.GetNode(\"AERIS_ND_PROFILES\")",
    "loaded.HasValue(\"schemaVersion\")",
):
    suite.check(token in profiles, "ConfigNode production-root compatibility: " + token)
suite.check("verified.name != \"AERIS_ND_PROFILES\"" not in profiles,
            "round-trip validation accepts Mono generic roots")

suite.check("Partial = 1" in contracts and "Complete = 2" in contracts,
            "draw-state contract has stable none/partial/complete values")

# Independent 11x11 viewport-coverage model. One tile may be useful without being
# complete; a wide coarse tile must cover both NORTH-UP and rotated TRACK-UP.
def fraction(rects, heading_deg, anchor_bottom=.25, n=11):
    angle = math.radians(-heading_deg)
    c, s = math.cos(angle), math.sin(angle)
    covered = 0
    for row in range(n):
        fy = (row + .5) / n
        for col in range(n):
            fx = (col + .5) / n
            dx, dy = fx - .5, fy - anchor_bottom
            sx = .5 + c * dx - s * dy
            sy = anchor_bottom + s * dx + c * dy
            if any(x0 <= sx <= x1 and y0 <= sy <= y1 for x0,y0,x1,y1 in rects):
                covered += 1
    return covered / float(n*n)

partial = fraction([(0.48, 0.15, 0.98, 0.65)], 140.0)
suite.check(0.0 < partial < 1.0,
            "isolated TRACK-UP tile is visible as partial, never promoted complete")
suite.equal(fraction([(-.75, -.75, 1.75, 1.75)], 0.0), 1.0,
            "wide coarse coverage is complete in NORTH-UP")
suite.equal(fraction([(-.75, -.75, 1.75, 1.75)], 140.0), 1.0,
            "wide coarse coverage is complete in rotated TRACK-UP")

suite.check("selftest_v01800_cp2_render_hotfix2.py" in runner,
            "CP2 acceptance runner retains progressive render regression")
suite.finish()
