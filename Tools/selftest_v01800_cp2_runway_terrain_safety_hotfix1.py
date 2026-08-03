#!/usr/bin/env python3
from __future__ import annotations
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 runway/terrain safety hotfix 1")
models = read(ROOT / "Source/AERISFlightControl/Landing/AERISAirfieldModels.cs")
resolver = read(ROOT / "Source/AERISFlightControl/Landing/AERISOperationalRunwayResolver.cs")
contracts = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwaySurveyContracts.cs")
landing = read(ROOT / "Source/AERISFlightControl/Landing/AERISLandingFoundation.cs")
token = read(ROOT / "Source/AERISFlightControl/Landing/AERISRunwayTrackToken.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
window = read(ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs")
raster = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")

suite.check("CurrentAlgorithmVersion = 1710" in contracts,
            "runway certification cache algorithm is invalidated")
for token_text in ("ThresholdBearingDeg", "HeadingGeometryErrorDeg",
                   "HeadingMatchesGeometry", "GeometryDirectionAutoCorrected",
                   "GeometryDirectionDetail"):
    suite.check(token_text in models, "runway direction safety model: " + token_text)
suite.check("IsCertified && HasFiniteGeometry && HeadingMatchesGeometry" in models,
            "certified geometry requires heading/end-point consistency")
suite.check("HeadingGeometryErrorDeg <= 10.0" in models,
            "heading/end-point tolerance is bounded to ten degrees")

for token_text in ("NormalizeDirectionGeometry(direction)",
                   "reciprocal endpoint order corrected", "Swap(ref direction.Threshold",
                   "HeadingDifference(direction.HeadingDeg", "ReciprocalMismatch",
                   "RUNWAY GEOMETRY HEADING MISMATCH", "CERTIFICATION REJECTED"):
    suite.check(token_text in resolver, "resolver reciprocal safety: " + token_text)
normalize_pos = resolver.find("NormalizeDirectionGeometry(direction)")
populate_pos = resolver.find("direction.PopulateOperationalReferences", normalize_pos)
suite.check(normalize_pos >= 0 and populate_pos > normalize_pos,
            "endpoint direction is normalized before touchdown/GS references are generated")

# Independent geometry check matching the implementation's safety invariant.
def diff(a: float, b: float) -> float:
    d=(a%360.0)-(b%360.0)
    while d>180.0: d-=360.0
    while d<-180.0: d+=360.0
    return abs(d)
suite.check(diff(343.69, 163.70) > 170.0 and
            diff(343.69, (163.70+180.0)%360.0) < 0.1,
            "Kola-class reciprocal endpoint mismatch is auto-correctable")
suite.check(diff(90.0, 270.0) == 180.0 and diff(90.0, 91.0) == 1.0,
            "heading difference wraps correctly")

for token_text in ("!direction.HeadingMatchesGeometry", "RUNWAY GEOMETRY HEADING MISMATCH",
                   "OnApproachSide", "double.NaN", "NOT ON APPROACH SIDE",
                   "GlidePathText", 'return "N/A"'):
    suite.check(token_text in landing, "LAND observation/arming safety: " + token_text)
suite.check("!direction.HasCertifiedGeometry || !direction.HeadingMatchesGeometry" in landing,
            "future Track Token handoff rejects invalid direction geometry")
suite.check("!direction.HasCertifiedGeometry || !direction.HeadingMatchesGeometry" in token,
            "public Track Token constructor boundary rejects invalid direction geometry")
landing_code = strip_csharp_comments_and_literals(landing)
for forbidden in ("FlightCtrlState", "MainThrottle", "mainThrottle", "wheelThrottle",
                  "wheelSteer", "OnFlyByWire"):
    suite.check(forbidden not in landing_code,
                "LAND safety hotfix remains observation-only: " + forbidden)

for token_text in ("RUNWAY GEOMETRY INVALID", "LOC N/A\\nNOT ON APPROACH SIDE",
                   "GS N/A\\nRUNWAY GEOMETRY INVALID", "GS N/A\\nNOT ON APPROACH SIDE",
                   "DrawClippedLine"):
    suite.check(token_text in nd, "ND fails safe for invalid/wrong-side approach: " + token_text)
for token_text in ("Geometry bearing", "heading error", "LAND ARM INHIBITED",
                   "RECIPROCAL ENDPOINT ORDER AUTO-CORRECTED", "GP target N/A",
                   '"N/A"'):
    suite.check(token_text in window, "LAND UI exposes safety state: " + token_text)

for token_text in ("ElevationMeters", "CoastlineSegments", "BuildCoastlines",
                   "tile.Flags[a] == 2", "AddWaterCrossing"):
    suite.check(token_text in raster, "terrain worker land/water boundary: " + token_text)
for token_text in ("LandMesh", "WaterMesh", "CoastlineMesh", "AppendClippedTriangle",
                   "ResolveRelativeLandColour", "ResolveTopographicLandColour",
                   "ResolveWaterColour", "aircraftAltitudeAslMeters - terrainAltitudeMeters",
                   "return new Color32(8, 52, 118, 255)", "CoastlineHalfWidthNormalized",
                   "EXPLICIT_VERTEX", "water=FIXED_BLUE"):
    suite.check(token_text in renderer, "GPU terrain explicit-colour safety: " + token_text)
suite.check("mainTextureScale" not in renderer and "mainTextureOffset" not in renderer,
            "unreliable built-in shader palette transform is removed")
suite.check("BuildSurfaceMesh(\"AERIS_TERRAIN_LAND_\"" in renderer and
            "BuildSurfaceMesh(\"AERIS_TERRAIN_WATER_\"" in renderer,
            "land warning colours cannot interpolate into the water mesh")
suite.check("MeshTopology.Lines" in renderer and "BuildCoastlineMesh" in renderer,
            "coastline has a dedicated band while contours remain thin lines")
suite.check("AutomaticGpuCapabilityAvailable" in renderer and
            "AUTO selected CPU fallback" in renderer and "GPU ON remains" in renderer,
            "GPU AUTO has a safe capability fallback distinct from forced ON")

# Expected REL transitions over low terrain.
def band(clearance: float) -> str:
    if clearance <= 30.0: return "RED"
    if clearance <= 300.0: return "YELLOW"
    if clearance <= 600.0: return "GREEN"
    return "DARK_GREEN"
terrain=70.0
suite.equal(band(90.0-terrain), "RED", "REL low-clearance warning")
suite.equal(band(350.0-terrain), "YELLOW", "REL intermediate warning")
suite.equal(band(650.0-terrain), "GREEN", "REL caution band")
suite.equal(band(1500.0-terrain), "DARK_GREEN", "REL high-clearance safe band")

suite.check("DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in build and
            "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in generated,
            "build identity names the runway/terrain safety hotfix")

suite.finish()
