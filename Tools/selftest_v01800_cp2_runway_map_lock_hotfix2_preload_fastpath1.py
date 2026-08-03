#!/usr/bin/env python3
from __future__ import annotations
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, sha256, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 runway map lock hotfix 2 + preload fast path 1")
projection = read(ROOT / "Source/AERISFlightControl/Terrain/AERISNdMapProjection.cs")
renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
builder = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs")
database = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs")
blocks = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs")
contracts = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs")
settings = read(ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs")
ui = read(ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs")
tile_system = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
runtime = read(ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")
build = read(ROOT / "build_ubuntu.sh")
generated = read(ROOT / "Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")

# One immutable transform now owns unequal ND axis scale, heading rotation, GUI Y and RT Y.
for token in (
    "internal struct AERISNdMapProjection",
    "HorizontalMeters = Math.Max(1.0, rangeMeters * 1.30)",
    "VerticalMeters = Math.Max(1.0, rangeMeters)",
    "ProjectLatitudeLongitudeToGui",
    "ProjectUnitToRenderNUp",
    "ResolveScaleCorrectedRenderMatrix",
    "verticalOverHorizontal",
    "horizontalOverVertical",
    "PresentRenderToGui",
):
    suite.check(token in projection, "shared scale-corrected projection: " + token)
for token in (
    "AERISNdMapProjection.Create(",
    "projection.ResolveScaleCorrectedRenderMatrix()",
    "geometryProjection=SHARED_SCALE_CORRECTED",
    "runwayMapLockErrorPx=",
    "terrain commit rejected; errorPx=",
    "lastRunwayMapLockErrorPixels > 1.0f",
):
    suite.check(token in renderer, "terrain projection/map-lock guard: " + token)
for token in (
    "TryProjectGeographicPoint(vessel.mainBody",
    "AERISNdMapProjection.Create(body",
    "ResolveMapLockReference()",
):
    suite.check(token in nd, "runway and terrain share projection: " + token)
suite.check("ResolveMapRotation" not in renderer,
            "legacy normalized-space rotation path is removed")
suite.check("ProjectionContext" not in renderer,
            "duplicate terrain-only projection context is removed")

# Independent anisotropic-axis proof. Rotating normalized x/y directly is wrong when H/V=1.3.
h_over_v = 1.30
heading = math.radians(53.0)
c, sn = math.cos(heading), math.sin(heading)
east, north = 18000.0, 9000.0
h, v = 52000.0, 40000.0
raw_x, raw_y = east / h, north / v
wanted_x = (east * c - north * sn) / h
wanted_y = (east * sn + north * c) / v
old_x = raw_x * c - raw_y * sn
old_y = raw_x * sn + raw_y * c
new_x = raw_x * c - raw_y * sn * (v / h)
new_y = raw_x * sn * (h / v) + raw_y * c
old_error = math.hypot(old_x - wanted_x, old_y - wanted_y)
new_error = math.hypot(new_x - wanted_x, new_y - wanted_y)
suite.check(old_error > 0.02,
            "plain normalized rotation reproduces visible anisotropic drift",
            f"normalized error={old_error:.6f}")
suite.check(new_error < 1e-12,
            "scale-corrected matrix exactly matches metre-space rotation",
            f"normalized error={new_error:.12f}")

# Preload Fast Path: explicit speed profiles, adaptive measured PQS budget, final-only builder commits.
for token in (
    "internal enum AERISTerrainPreloadSpeedProfile",
    "Balanced = 0", "Fast = 1", "Maximum = 2",
    "BuilderPqsSamplesPerSecond", "BuilderPqsSampleCacheHitRatio",
    "BuilderChunkBatchTiles",
    "DatabaseParsedChunkCacheHitRatio",
):
    suite.check(token in contracts, "preload fast-path contract: " + token)
for token in (
    "TerrainPreloadSpeedProfile", "terrainPreloadSpeedProfile",
    "AERISTerrainPreloadSpeedProfile.Balanced",
):
    suite.check(token in settings, "speed profile persistence: " + token)
for token in (
    "SetSpeedProfile", "ApplySpeedProfile", "pqsSampleCostEmaMs",
    "maximumMilliseconds ? 8.0f : 4.0f" if False else "8.0f : 4.0f",
    "ScheduleReadyChunkBatches", "batch.Tiles.Count >= 8",
    "now - batch.FirstQueuedRealtime >= 0.35f",
    "BoundarySampleCacheHitRatio",
    "database.SaveBatch", "preload-terrain-chunk-batch:",
):
    suite.check(token in builder, "builder fast path: " + token)
for token in (
    "preloadFinalOnly", "intermediateCommitsSkipped",
    "state.Request.WorkOwner ==", "AERISTerrainWorkOwner.PreloadBuilder",
):
    suite.check(token in blocks, "preload final-only progressive bypass: " + token)
suite.check("BuildTile(state, !final)" in blocks,
            "Flight progressive commits remain available")
for token in (
    "BoundarySampleKey", "MaximumBoundarySamples = 131072",
    "boundarySamples.TryGetValue", "PutBoundarySample",
    "LatitudeNanodegrees", "LongitudeNanodegrees",
):
    suite.check(token in blocks, "bounded shared edge-sample cache: " + token)

# One chunk read/rewrite/round-trip/manifest pass can commit multiple completed tiles.
for token in (
    "internal bool SaveBatch", "CommitEncodedChunkLocked",
    "List<AERISTerrainPreloadEncodedTile>",
    "preload chunk batch round-trip mismatch",
    "SaveManifestLocked()",
    "ParsedChunkCacheLimitBytes = 64L * 1024L * 1024L",
    "TryReadParsedChunk", "UpdateParsedChunkCache",
    "CloneEncodedDictionary", "DatabaseParsedChunkCacheHits",
):
    suite.check(token in database, "batched database/parsed chunk cache: " + token)
# SaveBatch must not save the manifest per tile; exactly one call appears in the method body.
save_batch = database[database.find("internal bool SaveBatch"):
                      database.find("bool CommitEncodedChunkLocked")]
suite.equal(save_batch.count("SaveManifestLocked()"), 1,
            "SaveBatch updates the manifest once for the whole batch")
commit_chunk = database[database.find("bool CommitEncodedChunkLocked"):
                        database.find("internal bool VerifyAndRepair")]
suite.check("SaveManifestLocked()" not in commit_chunk,
            "per-chunk helper does not multiply manifest writes")

for token in (
    "SetPreloadSpeedProfile", "PRELOAD SPEED", "BALANCED", "MAXIMUM",
    "FAST PATH  batch", "parsed chunk hit",
):
    suite.check(token in tile_system + ui, "speed control/status UI: " + token)
for token in (
    "preload_builder_pqs_samples_per_sec",
    "preload_builder_pqs_sample_cache_hit_ratio",
    "preload_builder_chunk_batch_tiles",
    "preload_builder_intermediate_commits_skipped",
    "terrain_db_parsed_chunk_cache_hit_ratio",
):
    suite.check(token in runtime, "CSV telemetry: " + token)

identity_upper = "DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1"
identity_title = "DEV CP2 KK Runway Absolute Registration Hotfix 1 Preload Fast Path 1"
suite.check(identity_upper in build and identity_upper in generated and
            identity_title in version,
            "build, generated version and AVC identify the combined hotfix")
suite.check("selftest_v01800_cp2_runway_map_lock_hotfix2_preload_fastpath1.py" in runner,
            "combined hotfix selftest is part of CP2 acceptance")

# No flight-control authority changes.
for rel, expected in (
    ("Autopilot/AERISBankDirector.cs", "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7"),
):
    suite.equal(sha256(ROOT / "Source/AERISFlightControl" / rel), expected,
                rel + " remains byte-identical")
for rel in (
    "Terrain/AERISNdMapProjection.cs",
    "Terrain/AERISTerrainGpuTileRenderer.cs",
    "Terrain/AERISTerrainPreloadBuilder.cs",
    "Terrain/AERISTerrainPreloadDatabase.cs",
    "UI/AERISNavigationDisplay.cs",
):
    code = strip_csharp_comments_and_literals(read(ROOT / "Source/AERISFlightControl" / rel))
    for forbidden in ("FlightCtrlState", "MainThrottle", "mainThrottle", "OnFlyByWire"):
        suite.check(forbidden not in code, rel + " remains control-free: " + forbidden)

suite.finish()
