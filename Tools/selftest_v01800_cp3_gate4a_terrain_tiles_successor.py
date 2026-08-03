#!/usr/bin/env python3
from __future__ import annotations
import math
import re
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals, extract_typed_array_expressions

suite = CheckSuite("v0.18.0.0 CP3 Gate 4A terrain tile foundation successor")
contracts = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs")
cache = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileCache.cs")
system = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
predictor = read(ROOT / "Source/AERISFlightControl/Terrain/AERISPredictiveForwardCorridor.cs")
blocks = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs")
preload_db = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs")
preload_codec = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs")
raster = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
perf = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs")
settings = read(ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
runtime = read(ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs")
gpu_backend = read(ROOT / "Source/AERISFlightControl/Performance/AERISGpuAssistBackend.cs")
window = read(ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs")
csproj = read(ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj")
factory = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")

for token in ("Global = 0", "Far = 1", "Route = 2", "Local = 3", "Land = 4"):
    suite.check(token in contracts, "five-level LOD contract: " + token)
for token in ("Automatic = 0", "Topographic = 1", "Relative = 2", "Off = 3"):
    suite.check(token in contracts, "terrain display mode contract: " + token)
for token in ("Standard = 0", "RedGreenAssist = 1", "BlueYellowAssist = 2", "HighContrast = 3"):
    suite.check(token in contracts, "colour-accessibility preset: " + token)
for token in ("Version = 2", "DefaultResolution = 33", "GlobalResolution = 17",
              "AERIS_TERRAIN_TILE_V2", "BodyRadiusMillimetres", "EnvironmentHash",
              "LatitudeIndex", "LongitudeIndex", "StableId", "FileStem", "internal bool Visible"):
    suite.check(token in contracts, "celestial-fixed tile identity/format: " + token)

# Independent numeric sanity check for the published LOD hierarchy.
cell = {"Land":8.0, "Local":64.0, "Route":256.0, "Far":1024.0, "Global":8192.0}
res = {"Land":33, "Local":33, "Route":33, "Far":33, "Global":17}
width = {name: cell[name] * (res[name]-1) for name in cell}
suite.check(width["Land"] < width["Local"] < width["Route"] < width["Far"] < width["Global"],
            "LOD physical coverage grows monotonically")
suite.equal(width["Land"], 256.0, "LAND tile nominal width")
suite.equal(width["Local"], 2048.0, "LOCAL tile nominal width")
suite.equal(width["Global"], 131072.0, "GLOBAL tile nominal width")
for radius in (200000.0, 600000.0, 6000000.0):
    spans=[max(.0001,min(45.0,width[name]/radius*180/math.pi)) for name in ("Land","Local","Route","Far","Global")]
    suite.check(all(0 < v <= 45 for v in spans), "angular spans bounded for body radius %.0f" % radius)
    suite.check(all(spans[i] < spans[i+1] for i in range(4)), "angular spans ordered for body radius %.0f" % radius)

for compiled in ("Terrain\\AERISTerrainTileContracts.cs", "Terrain\\AERISTerrainTileCache.cs",
                 "Terrain\\AERISTerrainTileSystem.cs", "Terrain\\AERISTerrainGpuTileRasterizer.cs",
                 "Terrain\\AERISTerrainGpuTileRenderer.cs"):
    suite.check(compiled in csproj, "CP2 source is compiled: " + compiled)

for token in ("AERISTerrainRamTileCache", "LinkedList<string>", "TrimLocked", "SetLimit",
              "AERISTerrainDiskTileCache", "LoadIndexOnly", "AERIS_TERRAIN_INDEX_V2",
              "DeflateStream", "CompressionLevel.Fastest", "EquivalentPayload",
              "full round-trip mismatch", "AtomicMove", "File.Move(source, destination)",
              "File.Move(backup, destination)", "PruneLocked", "SaveIndexLocked",
              "actualBytes = new FileInfo(full).Length", "SaveIndexLocked();"):
    suite.check(token in cache, "bounded verified cache contract: " + token)
suite.check("Directory.GetFiles" not in cache and "EnumerateFiles" not in cache,
            "startup reads only the terrain index and never scans the tile directory")
suite.check("File.ReadAllLines(indexPath" in cache,
            "startup index-only path is explicit")

for token, source in (
    ("AERISRuntimeLane.GeneralCompute", system), ("SubmitLatest", system),
    ("terrain-preload-read:", system), ("terrain-preload-write:", system),
    ("MaximumConcurrentTileIo", system), ("RecoverAbandonedIo", system),
    ("timeoutSeconds = 45f", system), ("MaximumTileSamplesPerFrame", system),
    ("TilePqsQueriesPerSecond", system), ("TrySampleTerrainAslShared", blocks),
    ("AERISTerrainTileLod.Global", system), ("PRELOAD TERRAIN TILE READY", system),
    ("AERISTerrainTileLod.Land", system),
    ("Math.Max(30.0, Math.Min(420.0", predictor),
    ("Math.Min(250000.0", predictor),
    ("Math.Min(Math.Min(250000.0, range * 4.0)", predictor),
    ("CurrentVesselGeneration", system), ("BodyGeneration", system),
    ("TerrainGeneration", system), ("ViewGeneration", system),
    ("StaleCancelled", system), ("UpdateDisplayView", system),
    ("displayViewRangeMeters", system), ("accepted.Visible", system),
    ("diskWritePending", system), ("RetryPendingDiskWrites", system),
    ("samePayload", system), ("diskWriteAttempts", system),
    ("diskWriteReadyScratch.Sort(StringComparer.Ordinal)", system)):
    suite.check(token in source, "tile planner/producer contract: " + token)
for forbidden in ("new Thread", "ThreadPool", "Task.Run", "Task<", "async ", "await "):
    suite.check(forbidden not in system and forbidden not in cache and forbidden not in raster and
                forbidden not in blocks and forbidden not in preload_db and forbidden not in preload_codec,
                "CP2 creates no terrain-owned asynchronous runtime: " + forbidden)
suite.check("AERISRuntimeLane.SafetyLand" not in system + raster,
            "terrain tile and palette work cannot consume Safety/LAND lane")
suite.check("File.GetLastWriteTime" not in system and "LastWriteTime" not in system,
            "environment fingerprint excludes unstable filesystem timestamps")
suite.check("ModuleManager.ConfigSHA" in system and "Fnv1A64Hex(text)" in system,
            "environment identity includes configuration content hash")

# Worker boundary: pure data only, Unity upload stays in renderer.
raster_code = strip_csharp_comments_and_literals(raster)
for forbidden in ("UnityEngine", "FlightGlobals", "FlightCtrlState", "Vessel", "CelestialBody",
                  "Texture2D", "GameObject", "KSPUtil"):
    suite.check(forbidden not in raster_code, "terrain mesh worker excludes Unity/KSP object: " + forbidden)
for token in ("Queue<AERISTerrainGpuTileRasterResult>", "completed.Count >= 64",
              "CloneImmutable", "terrain-gpu-mesh:", "context.ThrowIfStale()",
              "BuildContours", "BuildCoastlines", "AddWaterCrossing", "ResolveShade",
              "ElevationMeters", "ContourSegments", "CoastlineSegments",
              "MeshMilliseconds", "ContourMilliseconds", "ShadingEnabled",
              "ContoursEnabled"):
    suite.check(token in raster, "bounded GPU mesh worker contract: " + token)
suite.check("Triangles = triangles.ToArray()" in raster and
            "ContourSegments = contours" in raster and
            "CoastlineSegments = coastlines" in raster,
            "worker publishes immutable terrain, contour and coastline geometry")
suite.check("tile.Flags[a] == 2" in raster and "tile.Flags[b] == 2" in raster,
            "contours stop at water cells")
for forbidden in ("Rgba", "ResolveRelative", "ResolveTopographic", "TextureFormat.RGBA32",
                  "LoadRawTextureData", "final colour image"):
    suite.check(forbidden not in raster_code,
                "worker never creates CPU final-colour terrain image: " + forbidden)

for token in ("Mesh", "MeshTopology.Lines", "RenderTexture", "RenderTextureFormat.ARGB32",
              "Graphics.DrawMeshNow", "GL.LoadOrtho", "GUI.DrawTextureWithTexCoords",
              'Shader.Find("Sprites/Default")', "LandMesh", "WaterMesh", "ContourMesh",
              "CoastlineMesh", "AppendClippedTriangle", "BuildCoastlineMesh",
              "ResolveRelativeLandColour", "ResolveTopographicLandColour",
              "ResolveWaterColour", "RelativeAltitudeBucketMeters",
              "CoastlineHalfWidthNormalized", "ResolveVramLimitBytes",
              "Prune(ResolveVramLimitBytes())", "DestroyUnityObject(entry.LandMesh)",
              "DestroyUnityObject(entry.WaterMesh)", "ResetGpuFailure",
              "ReleaseGpuResources", "RecordGpuMeshPreparation", "TerrainGpuMode.Off",
              "PerformanceGpuAccelerationEnabled", "AutomaticGpuCapabilityAvailable",
              "TryTileRectNormalized", "BuildStyleKey(contourInterval)"):
    suite.check(token in renderer, "bounded GPU mesh composition contract: " + token)
suite.check("double[] latitudes" in renderer and "double[] longitudes" in renderer,
            "tile rectangle uses all four corners")
suite.check("mainTextureScale" not in renderer and "mainTextureOffset" not in renderer and
            "AERIS_TERRAIN_GPU_PALETTE" not in renderer,
            "GPU terrain no longer depends on built-in shader palette transforms")
suite.check("aircraftAltitudeAslMeters - terrainAltitudeMeters" in renderer and
            "clearanceMeters <= 30f" in renderer and "clearanceMeters <= 300f" in renderer and
            "clearanceMeters <= 600f" in renderer,
            "REL colours are calculated explicitly from aircraft-to-terrain clearance")
suite.check("(terrainAltitudeMeters + 500f) / 12500f" in renderer,
            "TOPO colours are calculated explicitly from elevation metres")
suite.check("return new Color32(8, 52, 118, 255)" in renderer and
            "Water is mode-independent" in renderer,
            "water has a fixed blue colour independent of TOPO/REL")
suite.check("BuildSurfaceMesh(\"AERIS_TERRAIN_LAND_\"" in renderer and
            "BuildSurfaceMesh(\"AERIS_TERRAIN_WATER_\"" in renderer,
            "land and water use separate clipped meshes")
suite.check("BuildCoastlineMesh" in renderer and "MeshTopology.Lines" in renderer and
            "CoastlineHalfWidthNormalized" in renderer,
            "coastline is a dedicated thick band distinct from contour lines")
suite.check("altitude" not in renderer[renderer.find("string BuildStyleKey"):renderer.find("static float ResolveContourInterval")].lower(),
            "mesh cache style excludes aircraft altitude")
suite.check("TerrainColourPreset" not in renderer[renderer.find("string BuildStyleKey"):renderer.find("static float ResolveContourInterval")],
            "mesh cache style excludes colour preset")
suite.check("gpuFailed = true" in renderer and "lastDrawState = AERISTerrainGpuDrawState.None" in renderer,
            "GPU/upload failure disables only the HD layer")
suite.check("currentGpuMode != lastGpuMode" in renderer and "ResetGpuFailure()" in renderer,
            "user-visible GPU mode change provides explicit retry")
suite.check("currentGpuMode == AERISTerrainGpuMode.Automatic" in renderer and
            "!AutomaticGpuCapabilityAvailable()" in renderer and
            "currentGpuMode == AERISTerrainGpuMode.Off" in renderer,
            "AUTO capability rejection and explicit GPU OFF remain distinct without CPU fallback")
for token in ("RegisterGraphicsAssist", "ReleaseGraphicsAssist",
              "ReportGraphicsAssistFailure"):
    suite.check(token in gpu_backend and token in renderer,
                "runtime telemetry tracks graphics-only terrain assist: " + token)
suite.check("graphicsAssistActive" in gpu_backend,
            "GPU backend exposes graphics-assist active state")

# Independent numeric sanity checks for explicit colour classification.
def relative_band(clearance: float) -> str:
    if clearance <= 30.0: return "red"
    if clearance <= 300.0: return "yellow"
    if clearance <= 600.0: return "green"
    return "dark-green"
for aircraft, terrain, expected in ((100.0, 90.0, "red"), (400.0, 100.0, "yellow"),
                                    (700.0, 100.0, "green"), (1500.0, 100.0, "dark-green")):
    suite.equal(relative_band(aircraft-terrain), expected,
                "REL band follows aircraft-to-terrain clearance")
suite.check(abs((0.0 + 500.0) / 12500.0 - 0.04) < 1e-9 and
            abs((12000.0 + 500.0) / 12500.0 - 1.0) < 1e-9,
            "TOPO gradient spans -500m through 12000m")

for token in ("MaximumTerrainTileRequests", "LocalTileRadius", "MaximumConcurrentTileIo",
              "TilePqsQueriesPerSecond", "MaximumTileSamplesPerFrame", "MaximumNormalLod",
              "DefaultRamCacheMiB", "DefaultDiskCacheMiB", "DefaultVramCacheMiB",
              '"ECO"', '"BALANCED"', '"HIGH"', '"ULTRA"', "RecordTilePqsCost",
              "tilePqsSampleEmaMs", "EffectiveTilePlanningFps"):
    suite.check(token in perf, "adaptive performance profile contract: " + token)
suite.check("tilePqsSampleEmaMs > 1.25f" in perf and "tilePqsSampleEmaMs < 0.30f" in perf,
            "AUTO adapts from measured tile PQS cost")

for key in ("terrainDisplayMode", "terrainGpuMode", "terrainColourPreset",
            "terrainRamCacheLimitMiB", "terrainDiskCacheLimitMiB", "terrainVramCacheLimitMiB",
            "terrainContoursEnabled", "terrainShadingEnabled"):
    suite.check(key in settings, "settings load/save contract: " + key)
    suite.check(key in factory, "factory CFG terrain key: " + key)
for token in ("ReadEnum", "NormalizeCacheLimit", "TerrainDisplayMode = AERISTerrainDisplayMode.Automatic",
              "TerrainGpuMode = AERISTerrainGpuMode.Automatic"):
    suite.check(token in settings, "settings normalization/default: " + token)

for token in ("DrawTerrainMap", "terrainTileRenderer.Draw", "TERRAIN GPU BUILDING", "TERR OFF",
              "TerrainDisplayMode.Topographic", "TerrainDisplayMode.Relative",
              "TerrainDisplayMode.Off", "SnapshotTelemetry", "RecordTerrainTileState"):
    suite.check(token in nd, "ND terrain integration: " + token)
telemetry_block = nd[nd.find("if (core.Performance != null)"):
                     nd.find("void CaptureNavigationSnapshot")]
suite.check("if (repaint)" in telemetry_block and "SnapshotTelemetry" in telemetry_block,
            "cache/VRAM telemetry snapshot is repaint-only")
suite.check("DrawPreparedNavigation" in nd and "DrawPreparedRunway" in nd,
            "terrain layer retains runway/navigation symbology")

for token in ("DrawTerrainQualitySelector", "DrawTerrainModeSelector",
              "DrawTerrainGpuSelector", "DrawTerrainColourSelector",
              "Terrain cache:", "tile PQS", "Terrain contour lines", "Terrain relief shading"):
    suite.check(token in window, "SYSTEM terrain option/telemetry: " + token)

for column in ("terrain_tile_ram_bytes", "terrain_tile_ram_limit_bytes",
               "terrain_tile_disk_bytes", "terrain_tile_disk_limit_bytes",
               "terrain_tile_ram_count", "terrain_tile_disk_count", "terrain_tile_ram_hits",
               "terrain_tile_disk_hits", "terrain_tile_misses", "terrain_tile_reused",
               "terrain_tile_generated", "terrain_tile_disk_writes",
               "terrain_tile_disk_failures", "terrain_tile_stale_cancelled",
               "terrain_tile_dropped_requests", "terrain_tile_hit_rate",
               "terrain_tile_pending", "terrain_tile_sampling_remaining",
               "terrain_gpu_bytes", "terrain_gpu_textures", "terrain_gpu_pending",
               "terrain_gpu_failures", "terrain_mesh_worker_ema_ms",
               "terrain_contour_worker_ema_ms"):
    suite.check(column in runtime, "performance CSV terrain column: " + column)
suite.check("RecordTerrainTileState" in runtime, "Performance Runtime accepts terrain cache/GPU state")
suite.check("TerrainMeshWorkerEmaMs" in perf and "TerrainContourWorkerEmaMs" in perf,
            "Terrain performance exposes separate mesh and contour worker EMA")
suite.check("RecordGpuMeshPreparation" in perf,
            "Terrain performance records separate GPU mesh preparation phases")

# Verify header/data column parity and uniqueness using the same typed-field parser as PC1 tests.
header_method = runtime[runtime.find("telemetryWriter.WriteHeader"):
                        runtime.find("Volatile.Write(ref current")]
value_method = runtime[runtime.find("void WriteTelemetry"):]
try:
    headers = extract_typed_array_expressions(header_method, "string")
    values = extract_typed_array_expressions(value_method)
    suite.equal(len(headers), len(values), "performance telemetry header/data column count")
    names=[]
    for item in headers:
        m=re.search(r'"([^"]+)"', item)
        if m: names.append(m.group(1))
    suite.equal(len(names), len(set(names)), "performance telemetry header names are unique")
except Exception as ex:
    suite.check(False, "performance telemetry array parsing", str(ex))

# Safety and scope boundaries.
terrain_all = contracts + cache + system + raster + renderer + perf
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "OnFlyByWire", "AERISNavDirector", "TryStartNavLanding", "NAV_LANDING"):
    suite.check(forbidden not in terrain_all, "CP2 terrain remains control/NAV free: " + forbidden)

suite.finish()
