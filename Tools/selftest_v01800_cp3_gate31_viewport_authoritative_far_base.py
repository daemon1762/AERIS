#!/usr/bin/env python3
from __future__ import annotations
import hashlib
import math
import random
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 3.1 Viewport-Authoritative Far Base")
planner_path = SOURCE / "Terrain/AERISTerrainViewportFoundationPlanner.cs"
projection_path = SOURCE / "Terrain/AERISNdMapProjection.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
contracts_path = SOURCE / "Terrain/AERISTerrainTileContracts.cs"
renderer_path = SOURCE / "Terrain/AERISTerrainGpuTileRenderer.cs"
resident_path = SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs"
window_path = SOURCE / "UI/AERISWindow.cs"
bootstrap_path = SOURCE / "Core/AERISBootstrap.cs"
generated_path = SOURCE / "Properties/AERISBuildVersion.generated.cs"
csproj_path = SOURCE / "AERISFlightControl.csproj"
build_path = ROOT / "build_ubuntu.sh"
version_path = ROOT / "GameData/AERISFlightControl/AERISFlightControl.version"
acceptance_path = ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE3.1_VIEWPORT_AUTHORITATIVE_FAR_BASE_VIRTUAL_DETAIL_FOUNDATION.txt"
spec_path = ROOT / "Docs/CP3_GATE3.1_VIEWPORT_AUTHORITATIVE_FAR_BASE_VIRTUAL_DETAIL_FOUNDATION_v0.18.0.0_ja.md"
card_path = ROOT / "Docs/ND_CP3_GATE3.1_VIEWPORT_AUTHORITATIVE_FAR_BASE_VIRTUAL_DETAIL_FOUNDATION_TEST_CARD_v0.18.0.0_ja.md"
runner_path = ROOT / "Tools/run_v01800_cp3_gate31_acceptance.py"

paths = [planner_path, projection_path, tiles_path, contracts_path, renderer_path,
         resident_path, window_path, bootstrap_path, generated_path, csproj_path,
         build_path, version_path, acceptance_path, spec_path, card_path]
for path in paths:
    suite.check(path.is_file(), "required package file exists: " + path.name)

planner = read(planner_path)
projection = read(projection_path)
tiles = read(tiles_path)
contracts = read(contracts_path)
renderer = read(renderer_path)
resident = read(resident_path)
window = read(window_path)
bootstrap = read(bootstrap_path)
generated = read(generated_path)
csproj = read(csproj_path)
build = read(build_path)
version = read(version_path)
acceptance = read(acceptance_path)
spec = read(spec_path)
card = read(card_path)
runner = read(runner_path) if runner_path.is_file() else ""

for name, text in (("planner", planner), ("projection", projection),
                   ("tile system", tiles), ("contracts", contracts),
                   ("renderer", renderer), ("resident cache", resident),
                   ("window", window), ("bootstrap", bootstrap),
                   ("generated version", generated)):
    clean = strip_csharp_comments_and_literals(text)
    suite.check(clean.count("{") == clean.count("}"), name + " C# braces are balanced")
    suite.check(clean.count("(") == clean.count(")"), name + " C# parentheses are balanced")

# Projection-authoritative foundation planner.
suite.check("AERISTerrainViewportFoundationPlanner.cs" in csproj,
            "new viewport foundation planner is compiled")
suite.check("AERISNdMapProjection.Create" in planner and
            "UnprojectGuiToLatitudeLongitude" in planner,
            "planner uses the shared ND projection and inverse mapping")
suite.check("HorizontalMeters = Math.Max(1.0, rangeMeters * 1.30)" in projection,
            "shared projection retains the ND 1.30 horizontal scale")
suite.check("AnchorGuiV - v" in projection and "if (TrackUp)" in projection,
            "inverse projection accounts for lower anchor and TRACK UP")
suite.check("SampleSpacingTileFraction = 0.42" in planner,
            "viewport samples are spaced below one-half FAR tile width")
suite.check("GuardRingTiles = 1" in planner and
            "for (int dy = -GuardRingTiles" in planner and
            "for (int dx = -GuardRingTiles" in planner,
            "every sampled tile receives a one-tile guard ring")
suite.check("MaximumFarKeys = 192" in planner and "MaximumGlobalKeys = 32" in planner,
            "pathological planner growth has explicit deterministic bounds")
suite.check("orientation" in planner and "AERISTerrainRenderTargetOrientation" in planner,
            "render-target orientation is part of planner input")

# Request admission and LOD architecture.
suite.check("AERISTerrainViewportFoundationPlanner.Build" in tiles,
            "PlanRequests builds the real viewport foundation")
suite.check("AddFoundationKeys(foundation.GlobalKeys" in tiles and
            "AddFoundationKeys(foundation.FarKeys" in tiles,
            "GLOBAL and FAR base keys are admitted explicitly")
suite.check("desiredFoundationIds.Contains(request.Key.StableId)" in tiles and
            "acceptedRequestScratch.Add(request)" in tiles,
            "foundation admission precedes profile-limited detail")
suite.check("admittedDetail < detailBudget" in tiles,
            "legacy request maximum budgets non-foundation detail only")
suite.check("accepted.Key.Lod == AERISTerrainTileLod.Far" in tiles and
            "visibleFoundationIds.Add(id)" in tiles,
            "FAR alone is the normal display completion authority")
suite.check("lastFoundationRequestedCount = foundation.FarKeys.Length" in tiles,
            "GLOBAL bootstrap does not delay FAR completion telemetry")
suite.check("AddCoarseCoverage" not in tiles,
            "fixed centre-radius coarse coverage path is removed")
background_body = strip_csharp_comments_and_literals(
    tiles[tiles.index("static bool IsBackgroundPopulationLod"):
          tiles.index("static bool IsGate3ResidentLod")])
suite.check("lod == AERISTerrainTileLod.Global" in background_body and
            "lod == AERISTerrainTileLod.Far" in background_body and
            "AERISTerrainTileLod.Route" not in background_body and
            "AERISTerrainTileLod.Local" not in background_body,
            "current-body background population is restricted to GLOBAL/FAR")
suite.check("AddExistingExactDetailBridge" in tiles and
            "ExactDetailPayloadExists" in tiles and
            "preloadDatabase.Contains(key)" in tiles,
            "ROUTE/LOCAL cruise bridge accepts existing exact RAM/SSD payload only")
suite.check("Math.Min(1, requestedRadius)" in tiles,
            "existing exact detail bridge is locally bounded")
suite.check("AERISTerrainTileLod.Far, point.Priority" in tiles and
            "AERISResidentPinReason.ForwardCorridor" in tiles,
            "Predictive Forward Corridor warms and pins FAR only")
suite.check("AddLandingPointWithPins" in tiles and
            "AERISTerrainTileLod.Land" in tiles[tiles.index("void AddLandingPointWithPins"):tiles.index("void MarkResidentPin") ] and
            "AERISTerrainTileLod.Local" in tiles[tiles.index("void AddLandingPointWithPins"):tiles.index("void MarkResidentPin") ],
            "LAND-selected endpoints preserve exact LOCAL/LAND demand")
suite.check("!landDetailActive" in tiles and "!landing.Armed" in tiles,
            "exact LAND payload remains demand-gated")

# Visibility, telemetry and rendering bridge.
for token in ("FoundationRequestedCount", "FoundationMissingCount",
              "GlobalFoundationCount", "FarFoundationCount", "FoundationComplete"):
    suite.check(token in contracts and token in tiles,
                "foundation contract is published: " + token)
suite.check("availableCoverage / foundationRequested" in tiles,
            "viewport preload coverage is based on FAR authority")
suite.check("foundation_missing=" in tiles and "foundation_gf=" in tiles,
            "periodic CP3 log exposes foundation counts and misses")
suite.check("CP3 Foundation: GLOBAL/FAR" in window and "detail VIRTUAL" in window,
            "SYSTEM page exposes FAR foundation and virtual-detail state")
suite.check("system.CaptureVisible(centerLatitudeDeg" in renderer and
            "mapHeadingDeg, trackUp, anchorV" in renderer,
            "renderer supplies actual view rotation and anchor to tile system")
suite.check("const int samplesPerAxis = 25" in renderer,
            "post-mesh viewport coverage retains dense 25x25 validation")
suite.check("Global/Far" in bootstrap and "Route/Local" in bootstrap,
            "startup log describes the new base/detail ownership")
suite.check("GATE 3.1 FAR BASE" in resident,
            "Resident Cache status names the Gate 3.1 architecture")

# Version identity and active runner.
ui = 'UiCheckpoint = "DEV CP3 GATE 3.1 — VIEWPORT-AUTHORITATIVE FAR BASE & VIRTUAL DETAIL FOUNDATION — COMPILE HOTFIX 1"'
suite.check(ui in generated and ui in build,
            "generated and build-time tab labels name Gate 3.1")
suite.check("DEV CP3 GATE 3.1 VIEWPORT AUTHORITATIVE FAR BASE VIRTUAL DETAIL FOUNDATION COMPILE HOTFIX 1" in generated and
            "DEV CP3 GATE 3.1 VIEWPORT AUTHORITATIVE FAR BASE VIRTUAL DETAIL FOUNDATION COMPILE HOTFIX 1" in build,
            "assembly/build display identity names Gate 3.1")
suite.check("CP3 GATE 3.1 VIEWPORT-AUTHORITATIVE FAR BASE" in version.upper(),
            "AVC package identity names Gate 3.1")
suite.check("run_v01800_cp3_gate31_compile_hotfix1_acceptance.py" in build,
            "build entrypoint invokes the Gate 3.1 Compile Hotfix 1 runner")
suite.check("selftest_v01800_cp3_gate31_viewport_authoritative_far_base.py" in runner,
            "active runner invokes the dedicated Gate 3.1 test")
suite.check("FAR is the sole persistent terrain display authority" in acceptance,
            "acceptance contract fixes FAR authority")
suite.check("VIRTUAL ROUTE" in spec and "EXACT LOCAL / LAND" in spec,
            "Japanese specification records virtual-detail architecture")
suite.check("360°旋回" in card and "foundation_missing=0/R" in card,
            "runtime card verifies full-heading viewport completion")

# Independent geometric acceptance model matching the C# projection/planner.
RADIUS = 600000.0
FAR_TILE_METERS = 1024.0 * 32.0
FAR_SPAN_DEG = FAR_TILE_METERS / RADIUS * 180.0 / math.pi
LAT_COUNT = max(1, int(math.ceil(180.0 / FAR_SPAN_DEG)))
LON_COUNT = max(1, int(math.ceil(360.0 / FAR_SPAN_DEG)))

def norm_lon(v):
    v %= 360.0
    if v >= 180.0: v -= 360.0
    return v

def direct(center_lat, center_lon, east, north):
    lat1 = math.radians(center_lat); lon1 = math.radians(center_lon)
    distance = math.hypot(east, north)
    if distance < 1e-9: return center_lat, norm_lon(center_lon)
    bearing = math.atan2(east, north)
    ad = distance / RADIUS
    lat2 = math.asin(max(-1.0, min(1.0,
        math.sin(lat1)*math.cos(ad) + math.cos(lat1)*math.sin(ad)*math.cos(bearing))))
    lon2 = lon1 + math.atan2(math.sin(bearing)*math.sin(ad)*math.cos(lat1),
        math.cos(ad)-math.sin(lat1)*math.sin(lat2))
    return math.degrees(lat2), norm_lon(math.degrees(lon2))

def unproject(center_lat, center_lon, range_m, heading_deg, track_up, anchor, u, v):
    right = (u - 0.5) * range_m * 1.30
    forward = (anchor - v) * range_m
    east, north = right, forward
    if track_up:
        r = math.radians(heading_deg); c, s = math.cos(r), math.sin(r)
        east = right*c + forward*s
        north = -right*s + forward*c
    return direct(center_lat, center_lon, east, north)

def key(lat, lon):
    yi = min(LAT_COUNT-1, max(0, int(math.floor((lat+90.0)/FAR_SPAN_DEG))))
    xi = int(math.floor((norm_lon(lon)+180.0)/FAR_SPAN_DEG)) % LON_COUNT
    return yi, xi

def plan(center_lat, center_lon, range_m, heading, track_up, anchor):
    spacing = max(250.0, FAR_TILE_METERS * 0.42)
    cols = max(4, min(32, int(math.ceil(range_m*1.30/spacing))))
    rows = max(4, min(32, int(math.ceil(range_m/spacing))))
    sampled = set()
    for row in range(rows+1):
        for col in range(cols+1):
            lat, lon = unproject(center_lat, center_lon, range_m, heading,
                                 track_up, anchor, col/max(1,cols), row/max(1,rows))
            sampled.add(key(lat,lon))
    guarded = set()
    for yi, xi in sampled:
        for dy in (-1,0,1):
            yy=yi+dy
            if yy<0 or yy>=LAT_COUNT: continue
            for dx in (-1,0,1): guarded.add((yy,(xi+dx)%LON_COUNT))
    return guarded

rng = random.Random(180031)
max_count = 0
min_large_count = 9999
all_dense_covered = True
for center_lat, center_lon in ((-0.1,-74.5),(0.0,0.0),(43.0,120.0),(-58.0,-20.0)):
    for range_km in (5,10,20,40,80,160):
        for track_up, anchor in ((False,0.5),(True,0.75)):
            for heading in range(0,360,15):
                planned = plan(center_lat, center_lon, range_km*1000.0,
                               heading, track_up, anchor)
                max_count=max(max_count,len(planned))
                if range_km>=40: min_large_count=min(min_large_count,len(planned))
                for _ in range(80):
                    u=rng.random(); v=rng.random()
                    lat,lon=unproject(center_lat,center_lon,range_km*1000.0,
                                      heading,track_up,anchor,u,v)
                    if key(lat,lon) not in planned:
                        all_dense_covered=False; break
suite.check(all_dense_covered,
            "independent spherical model covers every dense/random viewport sample")
suite.check(max_count <= 192,
            "Kerbin 5-160 km FAR foundation remains within the 192-key bound",
            "max="+str(max_count))
suite.check(min_large_count > 9,
            "40-160 km viewports are not silently clipped to fixed 3x3 FAR coverage",
            "minimum="+str(min_large_count))

# Frozen control/runway/data ownership.
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
    suite.check(hashlib.sha256((ROOT/rel).read_bytes()).hexdigest() == expected,
                "frozen implementation remains byte-identical: " + rel)

all_cs = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for token in ("StartPreloadBoost", "StopPreloadBoost", "[PRELOAD_BOOST]"):
    suite.check(token not in all_cs, "FULL BOOST remains absent: " + token)
modified_sections = planner + projection + tiles + contracts + renderer + resident + window + bootstrap
suite.check("AERISRuntimeLane.SafetyLand" not in modified_sections,
            "Gate 3.1 adds no Flight safety-lane work")
plan_section = tiles[tiles.index("void PlanRequests"):
                     tiles.index("void AddLandingPointWithPins")]
new_supply_sections = planner + projection + plan_section
suite.check("ReadAllBytes" not in new_supply_sections and
            "File.ReadAll" not in new_supply_sections,
            "Gate 3.1 adds no synchronous SSD read path")

suite.finish()
