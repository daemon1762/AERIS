#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2.5 Map DRAM Cache Foundation Hotfix 1")

def read(relative):
    path = ROOT / relative
    suite.check(path.is_file(), relative + " exists")
    return path.read_text(encoding="utf-8", errors="replace") if path.is_file() else ""

cache = read("Source/AERISFlightControl/Performance/AERISMapDramCache.cs")
registry = read("Source/AERISFlightControl/Landing/AERISAirfieldRegistry.cs")
database = read("Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs")
tiles = read("Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
awareness = read("Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs")
bootstrap = read("Source/AERISFlightControl/Core/AERISBootstrap.cs")
window = read("Source/AERISFlightControl/UI/AERISWindow.cs")
project = read("Source/AERISFlightControl/AERISFlightControl.csproj")
build_version = read("Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
build = read("build_ubuntu.sh")
version = read("GameData/AERISFlightControl/AERISFlightControl.version")
runner = read("Tools/run_v01800_cp25_acceptance.py")

cache_code = strip_csharp_comments_and_literals(cache)

suite.check("internal sealed class AERISMapDramCache" in cache and
            "internal sealed class AERISMapDramSnapshot" in cache,
            "single Map DRAM owner and immutable revision snapshot exist")
suite.check("volatile AERISMapDramSnapshot current" in cache and
            "readonly object publishSync" in cache,
            "snapshot publication uses a volatile reference and serialized writer")
suite.check("WithAirfields" in cache and "WithTerrain" in cache and
            "current = AERISMapDramSnapshot.WithAirfields" in cache and
            "current = AERISMapDramSnapshot.WithTerrain" in cache,
            "airfield and terrain domains publish atomically")
suite.check("sourceAirfield.Clone()" in cache and
            "stored.Clone()" in cache,
            "published and queried airfield objects are ownership-isolated clones")
suite.check("ReadOnlyCollection<AERISAirfieldDefinition>" in cache and
            "readonly Dictionary<string, AERISAirfieldDefinition>" in cache and
            "readonly Dictionary<string, AERISRunwayDefinition>" in cache and
            "readonly Dictionary<string, AERISRunwayDirectionDefinition>" in cache,
            "airport, runway and ILS-direction registries share one revision snapshot")
suite.check("AERISMapTerrainIndexEntry" in cache and
            "readonly Dictionary<string, AERISMapTerrainIndexEntry> terrainById" in cache and
            "readonly int[] terrainLodCounts" in cache,
            "Terrain Tile/LOD index metadata is resident in the snapshot")
suite.check("TryGetAirfield" in cache and "TryGetRunway" in cache and
            "TryGetDirection" in cache and "TryGetTerrainChunkId" in cache,
            "DRAM-only lookup APIs cover map registries and terrain index")
suite.check("Interlocked.Increment" in cache and "SnapshotTelemetry" in cache and
            "SynchronousDiskLookups" in cache,
            "lookup and forbidden synchronous-disk telemetry is exposed")
suite.check("payloadBytes=0" in cache and "normalLookup=DRAM_ONLY" in cache,
            "publish logs declare metadata-only payload and DRAM-only normal lookup")
suite.check("System.IO" not in cache_code and "File." not in cache_code and
            "Directory." not in cache_code and "FileStream" not in cache_code and
            "Path." not in cache_code,
            "Map DRAM owner contains no filesystem API")
suite.check(all(token not in cache_code for token in
                ("CompressedPayload", "HeightSamples", "RenderTexture", "new Mesh", "new Material")),
            "Map DRAM snapshot contains no CP3 terrain payload or GPU object")
suite.check('Performance\\AERISMapDramCache.cs' in project,
            "Map DRAM cache is compiled by xbuild")

suite.check("readonly AERISMapDramCache mapDramCache" in registry and
            "AERISAirfieldRegistry(AERISSettings settings," in registry,
            "airfield registry receives the shared Map DRAM owner")
suite.check("PublishAirfields(airfields, databaseRevision" in registry and
            '"AIRFIELD_ATOMIC_COMMIT"' in registry,
            "only the committed airfield database revision is published")
suite.check("TryGetMapAirfield" in registry and "TryGetMapRunway" in registry and
            "TryGetMapDirection" in registry,
            "registry exposes DRAM lookup façades without disk fallback")
commit_pos = registry.find("void CommitStaged")
publish_pos = registry.find("mapDramCache.PublishAirfields", commit_pos)
revision_pos = registry.find("databaseRevision = stagedDatabaseRevision", commit_pos)
suite.check(commit_pos >= 0 and revision_pos >= 0 and publish_pos > revision_pos,
            "airfield snapshot publishes after atomic database revision commit")

suite.check("readonly AERISMapDramCache mapDramCache" in database and
            "AERISTerrainPreloadDatabase(string root, long storageLimitBytes," in database,
            "preload database receives the shared Map DRAM owner")
contains_start = database.find("internal bool Contains(AERISTerrainTileKey key)")
contains_end = database.find("internal bool TryGetChunkId", contains_start)
contains = database[contains_start:contains_end]
contains_code = strip_csharp_comments_and_literals(contains)
suite.check("mapDramCache.ContainsTerrain(key)" in contains and
            "File.Exists" not in contains_code and "FileInfo" not in contains_code,
            "normal tile-presence lookup is DRAM-only")
chunk_start = database.find("internal bool TryGetChunkId")
chunk_end = database.find("internal string ChunkIdFor", chunk_start)
chunk_lookup = database[chunk_start:chunk_end]
chunk_lookup_code = strip_csharp_comments_and_literals(chunk_lookup)
suite.check("mapDramCache.TryGetTerrainChunkId" in chunk_lookup and
            "File.Exists" not in chunk_lookup_code and "FileInfo" not in chunk_lookup_code,
            "normal chunk-location lookup is DRAM-only")
suite.check("PublishMapIndexLocked(\"STARTUP_INDEX_LOAD\")" in database and
            "PublishMapIndexLocked(\"INDEX_COMMIT\")" in database,
            "startup load and committed index revisions publish metadata snapshots")
suite.check("RUNTIME_CHUNK_INVALIDATION" in database and
            "CHUNK_REINDEX" in database and "RUNTIME_TILE_INVALIDATION" in database,
            "repair and invalidation paths revoke stale DRAM metadata")
suite.check("new List<AERISMapTerrainIndexEntry>(tileIndex.Count)" in database and
            "entries.Sort" in database and "PublishTerrainIndex" in database,
            "terrain index publication is deterministic and metadata-only")
suite.check("var unavailableChunks = new HashSet<string>" in database and
            "unavailableChunks.Contains(entry.ChunkId)" in database,
            "startup validates each chunk path once rather than once per tile")
suite.check("ReadChunkEncoded" in database and "File.ReadAllBytes" in database,
            "blob payload I/O remains on the existing explicit worker/maintenance path")

suite.check("AERISTerrainTileSystem(AERISSettings settings," in tiles and
            "AERISMapDramCache mapDramCache" in tiles and
            "ResolvePreloadLimitBytes(settings), mapDramCache" in tiles,
            "tile system injects Map DRAM into the preload database")
suite.check("AERISTerrainAwareness(AERISSettings settings," in awareness and
            "AERISMapDramCache mapDramCache" in awareness and
            "new AERISTerrainTileSystem(settings, performance," in awareness,
            "terrain awareness preserves the same cache ownership chain")
suite.check("MapDramCache=new AERISMapDramCache()" in bootstrap and
            "new AERISAirfieldRegistry(settings,MapDramCache)" in bootstrap and
            "new AERISTerrainAwareness(settings,MapDramCache)" in bootstrap,
            "bootstrap creates exactly one shared Map DRAM cache")
suite.check("separate current-body Resident Cache contract" in bootstrap and
            "payload/decode/render/GPU routes remain disconnected" in bootstrap,
            "startup identity preserves the Map DRAM metadata / CP3 payload boundary")

suite.check("CP2.5 MAP DRAM CACHE — METADATA ONLY" in window and
            "cache.SnapshotTelemetry()" in window,
            "DIAGNOSTICS exposes Map DRAM state")
suite.check("AIRFIELD \"+map.AirfieldCount" in window and
            "TERRAIN INDEX  \"+map.TerrainTileCount" in window and
            "SYNC SSD \"+map.SynchronousDiskLookups" in window,
            "DIAGNOSTICS exposes registry, Tile/LOD and synchronous-SSD telemetry")
suite.check("Current-body payload residency begins in CP3" in window,
            "DIAGNOSTICS explicitly excludes terrain payload residency")

suite.check("CP2.5 MAP DRAM CACHE FOUNDATION HOTFIX 1" in build_version and
            "CP2.5 LAND SEPARATION HOTFIX 1" in build_version and
            "CP2.5 MAP DRAM CACHE FOUNDATION HOTFIX 1" in build,
            "identity adds Gate 4 while preserving Gates 1-3")
suite.check("Map DRAM Cache Foundation Hotfix 1" in version,
            "KSP version metadata identifies Gate 4")
suite.check("selftest_v01800_cp25_map_dram_cache_foundation_hotfix1.py" in runner,
            "CP2.5 acceptance runner includes Gate 4")

combined = "\n".join((cache, registry, database, tiles, awareness, bootstrap, window))
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS"):
    suite.check(forbidden not in combined,
                "Gate 4 adds no " + forbidden + " authority/content")

suite.finish()
