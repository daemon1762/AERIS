#!/usr/bin/env python3
from __future__ import annotations
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals, sha256

suite = CheckSuite("v0.18.0.0 CP2 closure candidate 1")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
landing = read(ROOT / "Source/AERISFlightControl/Landing/AERISLandingFoundation.cs")
policy = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs")
raster = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
csproj = read(ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")

# The runtime failure was a state-copy omission, not invalid certified geometry.
clone_start = nd.find("static AERISRunwayObservation CloneObservation")
clone_end = nd.find("void DrawTerrainMap", clone_start)
clone = nd[clone_start:clone_end]
for token in ("OnApproachSide = source.OnApproachSide",
              "RunwayGeometryDirectionValid = source.RunwayGeometryDirectionValid",
              "LocalizerGeometryEligible = source.LocalizerGeometryEligible",
              "GlidePathGeometryEligible = source.GlidePathGeometryEligible"):
    suite.check(token in clone, "ND LAND snapshot preserves: " + token)
suite.check("result.RunwayGeometryDirectionValid = direction.HeadingMatchesGeometry" in landing,
            "LAND observation derives direction validity from certified frozen geometry")
suite.check("result.OnApproachSide = result.AlongRunwayMeters <= 50.0" in landing,
            "LAND observation computes the approach-side state before ND caching")

# One conservative coastline policy drives both the fill edge and coast band.
suite.check('Compile Include="Terrain\\AERISTerrainCoastlinePolicy.cs"' in csproj,
            "shared coastline policy is compiled")
for token in ("LandInsetFraction = 0.38f", "CrossingFraction",
              "return water0 ? 1f - LandInsetFraction : LandInsetFraction"):
    suite.check(token in policy, "coastline conservative boundary policy: " + token)
suite.check("AERISTerrainCoastlinePolicy.CrossingFraction(a.Water, b.Water)" in renderer,
            "land/water fill uses the shared conservative shoreline")
suite.check("AERISTerrainCoastlinePolicy.CrossingFraction(water0, water1)" in raster,
            "visible coastline uses the identical shoreline crossing")
for token in ("AddTriangleCoastline", "Match the exact triangle diagonal",
              "pointCount != 2"):
    suite.check(token in raster, "coastline follows fill triangulation: " + token)
suite.check("(x0 + x1) * 0.5f" not in raster,
            "coastline no longer assumes a midpoint independent of the fill boundary")
suite.check("Midpoint(current, next" not in renderer and "CoastBoundaryPoint" in renderer,
            "surface clipping no longer uses an unrelated midpoint boundary")

# Independent policy values: land is inset, water receives the uncertain strip.
land_to_water = 0.38
water_to_land = 1.0 - 0.38
suite.check(0.25 < land_to_water < 0.5 and 0.5 < water_to_land < 0.75,
            "coastline bias is bounded and conservative")
suite.check(abs((1.0 - water_to_land) - land_to_water) < 1e-9,
            "crossing is orientation invariant")

# Shading is deliberately weak so safety bands and TOPO fills remain coherent.
for token in ("0.82f + diffuse * 0.20f", "0.82f, 1.04f"):
    suite.check(token in raster, "terrain source shading is softened: " + token)
for token in ("AERISTerrainDisplayMode mode", "Relative ? 0.30f : 0.55f",
              "0.94f, 1.02f", "0.88f, 1.035f"):
    suite.check(token in renderer, "mode-aware final shading is bounded: " + token)

suite.check("DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in build and
            "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in generated,
            "build identity names the CP2 closure candidate")

# Frozen control sources remain untouched.
for rel, expected in (
    ("Autopilot/AERISBankDirector.cs", "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7"),
):
    suite.equal(sha256(ROOT / "Source/AERISFlightControl" / rel), expected,
                rel + " remains byte-identical")
landing_code = strip_csharp_comments_and_literals(landing)
for forbidden in ("FlightCtrlState", "MainThrottle", "mainThrottle", "OnFlyByWire"):
    suite.check(forbidden not in landing_code,
                "LAND closure remains observation-only: " + forbidden)

suite.finish()
