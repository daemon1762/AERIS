#!/usr/bin/env python3
from __future__ import annotations
import math
import re
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 Preload Terrain integrated foundation")
base = ROOT / "Source/AERISFlightControl"
contracts = read(base / "Terrain/AERISTerrainPreloadContracts.cs")
codec = read(base / "Terrain/AERISTerrainPreloadCodec.cs")
database = read(base / "Terrain/AERISTerrainPreloadDatabase.cs")
blocks = read(base / "Terrain/AERISTerrainBlockPipeline.cs")
builder = read(base / "Terrain/AERISTerrainPreloadBuilder.cs")
tiles = read(base / "Terrain/AERISTerrainTileSystem.cs")
awareness = read(base / "Terrain/AERISTerrainAwareness.cs")
settings = read(base / "Settings/AERISSettings.cs")
factory = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")
runtime = read(base / "Performance/AERISPerformanceRuntime.cs")
bootstrap = read(base / "Core/AERISBootstrap.cs")
window = read(base / "UI/AERISWindow.cs")
csproj = read(base / "AERISFlightControl.csproj")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")

new_sources = {
    "contracts": contracts,
    "codec": codec,
    "database": database,
    "blocks": blocks,
    "builder": builder,
}
for name, text in new_sources.items():
    clean = strip_csharp_comments_and_literals(text)
    suite.equal(clean.count("{"), clean.count("}"), name + " braces balance")
    suite.equal(clean.count("("), clean.count(")"), name + " parentheses balance")
    suite.equal(clean.count("["), clean.count("]"), name + " brackets balance")

for source_name in (
    "Terrain\\AERISTerrainPreloadContracts.cs",
    "Terrain\\AERISTerrainPreloadCodec.cs",
    "Terrain\\AERISTerrainPreloadDatabase.cs",
    "Terrain\\AERISTerrainBlockPipeline.cs",
    "Terrain\\AERISTerrainPreloadBuilder.cs",
):
    suite.check(source_name in csproj, "compiled Preload source: " + source_name)

# Official terminology is PRELOAD. The former terrain-system label must not survive in
# identifiers, UI, settings, docs, or telemetry.
for path in list((ROOT / "Source").rglob("*.cs")) + list((ROOT / "Docs").rglob("*.md")) + [ROOT / "README.md"]:
    text = path.read_text(encoding="utf-8", errors="replace")
    suite.check("offline" not in text.lower(), "Preload terminology only: " + str(path.relative_to(ROOT)))

for token in (
    "Off = 0", "Manual = 1", "IdleOnly = 2", "Background = 3",
    "AggressiveIdle = 4",
):
    suite.check(token in contracts, "Preload Builder mode: " + token)
for token in (
    "Disabled = 0", "Low = 1", "Normal = 2", "High = 3", "Pinned = 4",
):
    suite.check(token in contracts, "per-body priority: " + token)
for token in (
    "Critical = 0", "High = 1", "Normal = 2", "Prefetch = 3", "Background = 4",
):
    suite.check(token in contracts, "asynchronous read lane: " + token)
for token in (
    "HotRam", "WarmRam", "PreloadDatabase", "RealtimeGenerated",
    "GlobalFallback", "PreloadBuilderGenerated",
):
    suite.check(token in contracts, "unified terrain tile source: " + token)

for token in (
    "DatabaseFormatVersion = 2", "CodecVersion = 1", "ChunkEdgeTiles = 8",
    "AERIS_PRELOAD_TERRAIN_MANIFEST_V2", "AERIS_PRELOAD_TERRAIN_CHUNK_V2",
    "AERIS_PRELOAD_TERRAIN_STATE_V1",
):
    suite.check(token in contracts, "versioned indexed database contract: " + token)
for token in (
    "Resolution", "MinimumElevationMeters", "MaximumElevationMeters", "HeightOffset",
    "HeightScale", "Quality", "GenerationState", "GenerationUtcTicks",
    "LastAccessUtcTicks", "PqsConfigurationHash", "GameDataHash",
    "TerrainGenerationId", "CodecId", "CodecVersion", "UncompressedSize",
    "PayloadCrc", "WaterOnly", "ConstantHeight", "FlatTile",
):
    suite.check(token in contracts, "tile metadata retained: " + token)

for token in (
    "span / 65535f", "WriteVarUInt(writer, ZigZag(delta))", "previous = quantized",
    "FlagWaterOnly", "FlagConstantHeight", "FlagFlatTile",
    "terrain flag RLE overflow", "AERISTerrainCrc32.Compute(compressed)",
    "CompressionLevel.Fastest", "AERISTerrainCodecId.Raw", "AERISTerrainCodecId.Deflate",
    "unsupported terrain codec version", "terrain decompressed size mismatch",
):
    suite.check(token in codec, "quantized predictive compressed codec: " + token)
suite.check("writer.Write(tile.Elevation" not in codec,
            "raw float elevation arrays are never serialized")
suite.check("CSV" not in database and ".csv" not in database.lower(),
            "CSV is not the primary terrain database")

for token in (
    "manifest.atm", "Chunks", "Journal", "LoadIndexOnly", "TryLoadBatch",
    "SaveManifestLocked", "AtomicReplace", "RecoverJournal", "VerifyAndRepair",
    "InvalidateBodyEnvironment", "DeleteBody", "PruneLocked", "RetentionScore",
    "RelativeChunkPath", "AERISTerrainSpatialKey.Morton", "WriteChunk",
    "ReadChunkEncoded", "MarkChunkForRepair", "RemoveTileIndex(key.StableId, false)",
    "RecoverIndexFromChunks", "IndexRecoveryNeeded", "journalRecoveryNeeded",
    "recoveryMarker", "*.pending", "ClearRecoveryMarkers",
    "recordLength", "recordCrc", "preload tile record trailing data",
):
    suite.check(token in database, "database resilience/indexing: " + token)
for token in (
    "SetActiveBodyProtection", "activeProtectedBodyName",
    "priority == AERISTerrainBodyPriority.Pinned",
    'string.Equals(chunk.BodyName, "Kerbin"',
    "chunk.Lod == AERISTerrainTileLod.Global",
    "chunk.Lod == AERISTerrainTileLod.Land",
):
    suite.check(token in database or token in tiles,
                "protected storage retention: " + token)

suite.check("File.ReadAllBytes" not in tiles,
            "ND/main-thread tile coordinator performs no synchronous blob read")
suite.check("TryLoadBatch(keys, warm, output" in tiles and
            "AERISRuntimeLane.GeneralCompute" in tiles,
            "disk read/decompression runs through shared scheduler")
suite.check(tiles.find("AERISRuntimeLane.GeneralCompute") <
            tiles.find("preloadDatabase.TryLoadBatch(keys, warm, output"),
            "shared scheduler encloses database read/decompression")

for token in (
    "AERISTerrainWarmTileCache", "TryGet", "Put", "Remove", "TrimLocked",
    "Priority", "UsedBytes", "limitBytes",
):
    suite.check(token in codec, "bounded warm compressed cache: " + token)
for token in (
    "AERISTerrainTileCache", "AERISTerrainWarmTileCache", "AERISTerrainPreloadDatabase",
):
    suite.check(token in tiles, "hot/warm/cold cache tier: " + token)

for token in (
    "MaximumActiveTiles = 48", "BuildBlocks", "edge = resolution <= 9 ? 2 : resolution <= 17 ? 2 : 4",
    "state.PendingBlocks >= 2", "state.CompletedBlocks * 100",
    "percent >= state.LastPublishedPercent + 25", "RemoveState(state, false)",
    "AERISRuntimeLane.GeneralCompute", "The worker stage must never fall back",
):
    suite.check(token in blocks, "Terrain Block Pipeline: " + token)
suite.check("ProcessBlock(payload);\n                CommitBlock(state, payload);" not in blocks,
            "block processing never falls back to main-thread execution")

for token in (
    "pointSetSignature", "ComputePointSignature", "ComparePreloadPoints",
    "if (string.Equals(pointSetSignature, signature",
    "Replaying an identical set", "plan.EstimatedTargetTiles = 0L",
):
    suite.check(token in builder,
                "stable Preload point-set generation guard: " + token)

for token in (
    "PRELOAD SUSPENDED / FLIGHT READ PRIORITY", "HighLogic.LoadedSceneIsFlight",
    "AERISTerrainWorkOwner.PreloadBuilder", "AERISRuntimeLane.ArchiveCompression",
    "AggressiveIdle", "ResolveIdleSeconds", "DetectInput", "Mathf.Lerp(80f, 1200f",
    "PRELOAD INDEX SCAN", "GlobalScannedWithoutMiss", "PointScannedWithoutMiss",
    "cursor = (cursor + 1L) % total", "scannedWithoutMiss >= total",
    "ResetScanState", "CaptureStateSnapshot", "WriteStateSnapshot",
    "preload_state.aps", "File.Replace", 'TryLoadState(statePath + \".bak\")',
    "BodyAtStorageLimit", "TimeSpan.FromDays(30.0)", "AutomaticPriority",
    "PriorityOverride",
):
    suite.check(token in builder, "Preload Builder behavior: " + token)
suite.check(builder.find("AERISTerrainTileLod.Global") <
            builder.find("TryNextPointTile(plan, body"),
            "global overview is completed before local point refinement")
selection_method = builder[builder.index("bool TryNextMissingRequest"):
                           builder.index("ScanResult TryNextGlobalTile",
                                         builder.index("bool TryNextMissingRequest"))]
suite.check(selection_method.find("TryNextPointTile(plan, body") <
            selection_method.find("AERISTerrainTileLod.Far"),
            "runway/LAND/current points precede global Far refinement")
for token in (
    "ResolvePerformanceLoadScale", "performance.WorkerBacklogged",
    "performance.FrameTimeEmaMs >= 45f", "return 0.25f",
    "milliseconds * loadScale",
):
    suite.check(token in builder, "Builder adaptive load backoff: " + token)

suite.check("mode = configuredMode;" in builder,
            "user settings override an older persisted Builder mode")
suite.check("NormalizePreloadStorageLimit" in builder,
            "global storage values are normalized to the supported set")

for token in (
    "terrainPreloadMode", "terrainPreloadStorageLimitMiB", "terrainPreloadIdleSeconds",
    "NormalizePreloadStorageLimit", "TerrainPreloadMode = AERISTerrainPreloadMode.AggressiveIdle",
):
    suite.check(token in settings, "persistent Preload setting: " + token)
for token in (
    "terrainPreloadMode = AggressiveIdle", "terrainPreloadStorageLimitMiB = 2048",
    "terrainPreloadIdleSeconds = 5",
):
    suite.check(token in factory, "factory Preload default: " + token)

for token in (
    "PRELOAD TERRAIN MAPS", "PRIMARY TERRAIN DATABASE", "PRELOAD MAP STORAGE",
    "OFF", "MANUAL", "IDLE ONLY", "BACKGROUND", "AGGRESSIVE IDLE",
    "BUILD", "PAUSE", "RESUME", "CANCEL", "VERIFY", "REBUILD", "DELETE",
    "PINNED", "GLOBAL", "FAR", "ROUTE", "LOCAL", "LAND",
    "DrawPreloadOnly", "PRELOAD TERRAIN CONTROL", "AERIS PRELOAD",
):
    suite.check(token in window, "Preload management UI: " + token)
suite.check("window.DrawPreloadOnly()" in bootstrap,
            "full Preload control UI is available outside Flight")
suite.check("Terrain.Tick(FlightGlobals.ActiveVessel,Landing,Airfields);" in bootstrap and
            bootstrap.find("Terrain.Tick(FlightGlobals.ActiveVessel,Landing,Airfields);") <
            bootstrap.find("if(!inFlight)return;"),
            "Preload Builder ticks in Space Center/VAB/SPH/Tracking/Main Menu scenes")
suite.check("if(inFlight&&Landing!=null)Landing.Tick" in bootstrap,
            "LAND remains Flight-only while Preload runs non-Flight")

for token in (
    "request.TerrainGeneration == terrainRequestGeneration",
    "request.ViewGeneration == viewGeneration",
    "request.RangeGeneration == rangeGeneration",
    "request.PlanGeneration == planGeneration",
    "request.DatabaseGeneration == preloadDatabase.RequestGeneration",
    "RefreshTerrainRequestGeneration", "performance.ProfileRevision",
    "request.TerrainGeneration == plan.Generation",
    "request.DatabaseGeneration == database.RequestGeneration",
    "long requestGeneration = 1L", "internal long RequestGeneration",
    "requestGeneration++",
):
    suite.check(token in tiles or token in builder or token in database,
                "end-to-end stale generation guard: " + token)

for token in (
    "ResolveWriteIoLimitLocked", "readsPending", "return Math.Max(0, limit - 1)",
    "writeLimit <= 0",
):
    suite.check(token in tiles, "Flight read/write I/O arbitration: " + token)

for token in (
    "return AERISTerrainReadLane.Critical", "AERISTerrainReadLane.High",
    "AERISTerrainReadLane.Normal", "AERISTerrainReadLane.Prefetch",
    "AERISTerrainReadLane.Background", "CompareRequests", "SchedulePreloadReads",
    "TrySchedulePreloadChunk", "requests.Sort(CompareRequests)",
    "MaximumConcurrentTileIo", "DatabaseReadLatencyMilliseconds",
    "latency > 25.0 ? 1", "preloadChunksLoading", "selectedRequests",
    "IsFlightRequestCurrent", "RangeGeneration", "PlanGeneration",
    "DatabaseGeneration", "StaleResultsDiscarded", "ScheduleDiskWrite",
):
    suite.check(token in tiles, "asynchronous multithreaded loading/generation: " + token)
suite.check(tiles.find("preloadDatabase.Contains(next.Key)") <
            tiles.find("disk.Contains(next.Key)"),
            "Preload DB is the primary source before legacy cache and PQS")
suite.check(tiles.find("preloadDatabase.Contains(next.Key)") <
            tiles.find("blockPipeline.Enqueue(activeBody"),
            "PQS Block Pipeline is used only after Preload DB misses")
suite.check("Writes never consume the last I/O slot while Flight reads are pending" in tiles,
            "Flight reads reserve I/O capacity ahead of DB writes")

for token in (
    "RequestGameDataHash", "ComputeGameDataHash", "terrain-gamedata-hash",
    "AERISRuntimeLane.GeneralCompute", "PRELOAD GAMEDATA HASHING",
    "if (!GameDataHashReady)", "if (!GameDataHashReady) return string.Empty",
):
    suite.check(token in tiles, "asynchronous GameData/PQS identity hashing: " + token)
for token in (
    "cachedBodyEnvironmentHashes", "AppendPqsConfigurationFingerprint",
    "AppendStablePrimitiveMembers", 'ReadMemberValue(pqs, "mods")',
    "StableFingerprintType", "CultureInfo.InvariantCulture",
):
    suite.check(token in tiles, "body-specific PQS configuration identity: " + token)
suite.check("builder.Append('|').Append(GameDataHash)" not in tiles,
            "global GameData hash is metadata, not a blanket all-body invalidation key")
for token in (
    "MetadataMatches", "encoded.PqsConfigurationHash",
    "requestedKey.EnvironmentHash", "DatabaseHashMismatches++",
    "body-specific live PQS hash is",
):
    suite.check(token in database,
                "body-specific metadata validation and isolated invalidation: " + token)
suite.check("terrain_db_hash_mismatches" in runtime and
            "DatabaseHashMismatches" in runtime and
            "DatabaseHashMismatches" in tiles and
            "DatabaseHashMismatches" in contracts,
            "GameData metadata mismatches are observable without global invalidation")

suite.check("EnsureGameDataHash" not in tiles,
            "GameData identity hash has no synchronous ensure path")
hash_request = tiles[tiles.index("static void RequestGameDataHash"):
                     tiles.index("static string ComputeGameDataHash")]
hash_compute = tiles[tiles.index("static string ComputeGameDataHash"):
                     tiles.index("internal static string EnvironmentHashForBody")]
suite.check("SubmitLatest" in hash_request and "File.ReadAllText" not in hash_request,
            "main-thread hash request only submits shared work")
suite.check("File.ReadAllText" in hash_compute and "Directory.GetFiles" in hash_compute,
            "GameData fallback scan exists only inside shared worker computation")

for token in (
    "preload_builder_body", "preload_builder_lod", "preload_builder_tiles_complete",
    "preload_builder_tiles_pending", "preload_builder_pqs_ms",
    "preload_builder_worker_utilization", "preload_builder_write_mbps",
    "preload_builder_compression_ratio", "preload_builder_storage_bytes",
    "terrain_db_read_requests", "terrain_db_read_latency_ms", "terrain_db_read_mbps",
    "terrain_db_read_queue_depth", "terrain_db_cache_hit_ratio",
    "terrain_db_coalesced_reads", "terrain_db_crc_failures",
    "terrain_decompress_queue_delay_ms", "terrain_decompress_time_ms",
    "terrain_decompress_mbps", "terrain_decompress_worker_active",
    "terrain_decompress_failures", "terrain_first_tile_visible_ms",
    "terrain_viewport_coverage_ratio", "terrain_preload_result_age_ms",
    "terrain_stale_results_discarded", "terrain_generation_fallback_count",
):
    suite.check(token in runtime, "Preload performance telemetry: " + token)

combined_new = "\n".join(new_sources.values())
for forbidden in ("new Thread(", "ThreadPool.", "Task.Run(", ".Wait(", ".Result"):
    suite.check(forbidden not in combined_new,
                "no private/blocking terrain worker primitive: " + forbidden)
suite.check("AERISRuntimeLane.Safety" not in combined_new and
            "AERISRuntimeLane.Land" not in combined_new,
            "Preload terrain does not consume Safety/LAND lanes")
for forbidden in ("FlightCtrlState", "MainThrottle", ".pitch", ".roll", ".yaw"):
    suite.check(forbidden not in combined_new,
                "Preload terrain has no flight-control write surface: " + forbidden)

# Independent model: UInt16 quantization must round-trip within one quantization step.
heights = [120.0 + math.sin(i * 0.17) * 37.0 + (i % 11) * 0.25 for i in range(1089)]
lo, hi = min(heights), max(heights)
scale = (hi - lo) / 65535.0
quantized = [max(0, min(65535, round((value - lo) / scale))) for value in heights]
restored = [lo + value * scale for value in quantized]
max_error = max(abs(a - b) for a, b in zip(heights, restored))
suite.check(max_error <= scale * 0.50001,
            "independent UInt16 terrain quantization stays within half a step")
# Row predictor reconstruction.
for row in range(33):
    values = quantized[row * 33:(row + 1) * 33]
    previous = 0
    deltas = []
    for value in values:
        deltas.append(value - previous)
        previous = value
    rebuilt = []
    previous = 0
    for delta in deltas:
        previous += delta
        rebuilt.append(previous)
    suite.equal(rebuilt, values, "independent row predictor round-trip row " + str(row))

# Independent model: one damaged length-framed tile record does not prevent later
# records in the same spatial chunk from being recovered.
import struct
import zlib
records = [b"tile-A", b"tile-B", b"tile-C"]
framed = bytearray()
for payload in records:
    framed += struct.pack("<II", len(payload), zlib.crc32(payload) & 0xffffffff)
    framed += payload
# Corrupt only the middle record payload while preserving its frame length.
second_payload = 8 + len(records[0]) + 8
framed[second_payload] ^= 0x55
recovered = []
offset = 0
for _ in records:
    length, crc = struct.unpack_from("<II", framed, offset)
    offset += 8
    payload = bytes(framed[offset:offset + length])
    offset += length
    if zlib.crc32(payload) & 0xffffffff == crc:
        recovered.append(payload)
suite.equal(recovered, [records[0], records[2]],
            "framed chunk corruption isolates one tile record")

# Independent model: Morton ordering is unique over one 8x8 chunk.
def part1by1(value: int) -> int:
    x = value & 0xffffffff
    x = (x | (x << 16)) & 0x0000FFFF0000FFFF
    x = (x | (x << 8)) & 0x00FF00FF00FF00FF
    x = (x | (x << 4)) & 0x0F0F0F0F0F0F0F0F
    x = (x | (x << 2)) & 0x3333333333333333
    x = (x | (x << 1)) & 0x5555555555555555
    return x
morton = {part1by1(x) | (part1by1(y) << 1) for y in range(8) for x in range(8)}
suite.equal(len(morton), 64, "independent Morton ordering is unique for an 8x8 chunk")

# Independent cyclic scan model: a cursor that advanced before a crash still revisits and
# discovers the missing tile instead of permanently declaring the phase complete.
def cyclic_missing(total: int, cursor: int, present: set[int], budget: int = 256):
    scanned = 0
    for _ in range(min(total, budget)):
        value = cursor
        cursor = (cursor + 1) % total
        if value not in present:
            return value, cursor, 0
        scanned += 1
    return None, cursor, scanned
present = set(range(100)) - {7}
found = None
cursor = 8  # simulate a crash immediately after advancing past missing tile 7
for _ in range(2):
    candidate, cursor, _ = cyclic_missing(100, cursor, present)
    if candidate is not None:
        found = candidate
        break
suite.equal(found, 7, "cyclic Builder scan recovers a tile skipped by crash timing")

# Read lane model: current viewport must win over read-ahead/background regardless of LOD.
requests = [
    (4, 0, 0.0, "background"),
    (3, 4, 1.0, "prefetch"),
    (1, 3, 2.0, "track"),
    (0, 4, 100.0, "viewport"),
]
requests.sort(key=lambda item: (item[0], item[1], item[2]))
suite.equal(requests[0][3], "viewport", "CRITICAL viewport read wins independent lane model")

suite.check("selftest_v01800_cp2_preload_terrain_integration.py" in runner,
            "CP2 acceptance runner includes Preload terrain integration")
suite.finish()
