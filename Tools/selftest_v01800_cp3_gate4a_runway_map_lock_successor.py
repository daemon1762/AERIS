#!/usr/bin/env python3
from __future__ import annotations
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, sha256, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 4A runway/map geodetic lock successor")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
raster = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs")
pipeline = read(ROOT / "Source/AERISFlightControl/Performance/AERISNavigationDisplayPipeline.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
avc = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")

# Terrain geometry is no longer stretched into one four-corner rectangle. Every fill,
# contour and coastline vertex retains an immutable geodetic unit vector and is projected
# against the live map centre before draw.
for token in (
    "GeographicUnitPoint", "AERISNdMapProjection", "BuildGeographicPoints",
    "EnsureProjectedGeometry", "ProjectMesh", "LandGeographicPoints",
    "WaterGeographicPoints", "ContourGeographicPoints",
    "CoastlineGeographicPoints", "LandProjectedVertices",
    "WaterProjectedVertices", "geometryProjection=SHARED_SCALE_CORRECTED",
    "ResolveRenderableEntries(tile, styleKey", "Entry drawEntry = currentEntry != null ? currentEntry : fallbackEntry",
    "DrawEntry(drawEntry, mapRotation",
    "mesh.MarkDynamic()", "mesh.UploadMeshData(false)", "movementThresholdMeters",
):
    suite.check(token in renderer, "per-vertex spherical terrain projection: " + token)
suite.check("Matrix4x4 tileMatrix = mapRotation * Matrix4x4.TRS" not in renderer,
            "tile rectangle is never used as the terrain draw transform")
suite.check("Graphics.DrawMeshNow(entry.LandMesh, mapMatrix)" in renderer and
            "Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix)" in renderer,
            "terrain meshes receive only the shared map-heading matrix")
for token in ("SouthLatitudeDeg", "NorthLatitudeDeg", "WestLongitudeDeg",
              "EastLongitudeDeg"):
    suite.check(token in raster, "worker carries immutable tile geography: " + token)

# Runways preserve endpoint latitude/longitude across the worker boundary and are converted
# directly from the live map centre every repaint. The old stale-frame vector subtraction is
# retained only for non-runway facilities/traffic, never for runway geometry or hit testing.
for token in ("internal double LatitudeADeg", "internal double LongitudeADeg",
              "internal double LatitudeBDeg", "internal double LongitudeBDeg",
              "LatitudeADeg = item.LatitudeADeg", "LongitudeBDeg = item.LongitudeBDeg"):
    suite.check(token in pipeline, "runway geodetic endpoint contract: " + token)
for token in (
    "TryProjectGeographicPoint(vessel.mainBody",
    "runway.LatitudeADeg",
    "runway.LongitudeBDeg",
    "runway.CenterLatitudeDeg,",
    "double centerLatitudeDeg = planMode ? planCenterLatitudeDeg",
):
    suite.check(token in nd, "live-centre runway projection: " + token)
runway_draw_start = nd.find("void DrawPreparedRunway")
runway_draw_end = nd.find("void DrawSelectedRunwayEdgePointer", runway_draw_start)
runway_draw = nd[runway_draw_start:runway_draw_end]
suite.check("runway.EastAMeters - centerEast" not in runway_draw and
            "runway.CenterEastMeters - centerEast" not in runway_draw,
            "runway draw no longer subtracts a stale prepared tangent-frame origin")
preview_start = nd.find("void PreviewRunwayAt")
preview_end = nd.find("void DrawPreviewPanel", preview_start)
preview = nd[preview_start:preview_end]
suite.check("runway.EastAMeters - centerEast" not in preview and
            "runway.LatitudeADeg" in preview,
            "runway mouse hit-testing uses the same live geodetic projection")

# Numerical reproduction from the submitted runtime evidence. The old Global-tile rectangle
# puts the Island Airfield reference point tens of metres away from the exact great-circle
# map position. The new unit-vector projection and the runway geodesic agree to sub-metre.
R = 600000.0
SOUTH, NORTH = -2.384822, 10.131632
WEST, EAST = -79.868368, -67.351914
RUNWAY = (-1.5166408674704, -71.91370498130577)
CENTRES = [(-0.3428731, -73.6800289), (-0.4776395, -73.5937885)]

def norm_lon(value: float) -> float:
    value %= 360.0
    if value > 180.0: value -= 360.0
    if value < -180.0: value += 360.0
    return value

def positive_span(a: float, b: float) -> float:
    value = norm_lon(b - a)
    if value < 0.0: value += 360.0
    return value

def local(origin, target):
    lat1 = math.radians(origin[0]); lat2 = math.radians(target[0])
    dlon = math.radians(norm_lon(target[1] - origin[1]))
    y = math.sin(dlon) * math.cos(lat2)
    x = math.cos(lat1) * math.sin(lat2) - math.sin(lat1) * math.cos(lat2) * math.cos(dlon)
    bearing = math.atan2(y, x)
    dlat = lat2 - lat1
    a = math.sin(dlat * 0.5) ** 2 + math.cos(lat1) * math.cos(lat2) * math.sin(dlon * 0.5) ** 2
    angle = 2.0 * math.atan2(math.sqrt(max(0.0, a)), math.sqrt(max(0.0, 1.0 - a)))
    distance = R * angle
    return math.sin(bearing) * distance, math.cos(bearing) * distance

def old_tile_rect(origin, target):
    corners = [(SOUTH, WEST), (SOUTH, EAST), (NORTH, WEST), (NORTH, EAST)]
    points = [local(origin, point) for point in corners]
    min_e = min(p[0] for p in points); max_e = max(p[0] for p in points)
    min_n = min(p[1] for p in points); max_n = max(p[1] for p in points)
    u = positive_span(WEST, target[1]) / positive_span(WEST, EAST)
    v = (target[0] - SOUTH) / (NORTH - SOUTH)
    return min_e + u * (max_e - min_e), min_n + v * (max_n - min_n)

def unit_projection(origin, target):
    lat0 = math.radians(origin[0]); lon0 = math.radians(origin[1])
    lat = math.radians(target[0]); lon = math.radians(target[1])
    p = (math.cos(lat) * math.cos(lon), math.cos(lat) * math.sin(lon), math.sin(lat))
    c = (math.cos(lat0) * math.cos(lon0), math.cos(lat0) * math.sin(lon0), math.sin(lat0))
    east = (-math.sin(lon0), math.cos(lon0), 0.0)
    north = (-math.sin(lat0) * math.cos(lon0), -math.sin(lat0) * math.sin(lon0), math.cos(lat0))
    eu = sum(p[i] * east[i] for i in range(3))
    nu = sum(p[i] * north[i] for i in range(3))
    q = max(0.0, eu * eu + nu * nu)
    if q <= 0.18:
        factor = 1.0 + q * (1.0/6.0 + q * (3.0/40.0 + q * (5.0/112.0 + q * (35.0/1152.0 + q * 63.0/2816.0))))
    else:
        radial = math.sqrt(q)
        dot = sum(p[i] * c[i] for i in range(3))
        factor = 1.0 if radial <= 1e-12 else math.atan2(radial, dot) / radial
    return eu * R * factor, nu * R * factor

old_errors = []
new_errors = []
for centre in CENTRES:
    exact = local(centre, RUNWAY)
    old = old_tile_rect(centre, RUNWAY)
    new = unit_projection(centre, RUNWAY)
    old_errors.append(math.hypot(old[0] - exact[0], old[1] - exact[1]))
    new_errors.append(math.hypot(new[0] - exact[0], new[1] - exact[1]))
suite.check(min(old_errors) > 40.0,
            "submitted Global rectangle reproduces >40 m runway/terrain displacement",
            ", ".join(f"{value:.3f}m" for value in old_errors))
suite.check(max(new_errors) < 0.05,
            "spherical terrain vertex and live runway geodesic agree below 5 cm",
            ", ".join(f"{value:.6f}m" for value in new_errors))

suite.check("DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in build and
            "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in generated and
            "DEV CP2 KK Runway Absolute Registration Hotfix 1 Preload Fast Path 1" in avc,
            "build and AVC identity name the runway/map lock hotfix")

# Frozen flight-control code remains byte-identical.
for rel, expected in (
    ("Autopilot/AERISBankDirector.cs", "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7"),
):
    suite.equal(sha256(ROOT / "Source/AERISFlightControl" / rel), expected,
                rel + " remains byte-identical")
for rel in ("Terrain/AERISTerrainGpuTileRenderer.cs",
            "Performance/AERISNavigationDisplayPipeline.cs",
            "UI/AERISNavigationDisplay.cs"):
    code = strip_csharp_comments_and_literals(read(ROOT / "Source/AERISFlightControl" / rel))
    for forbidden in ("FlightCtrlState", "MainThrottle", "mainThrottle", "OnFlyByWire"):
        suite.check(forbidden not in code, rel + " remains control-free: " + forbidden)

suite.finish()
