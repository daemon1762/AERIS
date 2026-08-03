#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2.5 Map DRAM Cache Foundation Hotfix 2")

def read(relative):
    path = ROOT / relative
    suite.check(path.is_file(), relative + " exists")
    return path.read_text(encoding="utf-8", errors="replace") if path.is_file() else ""

cache = read("Source/AERISFlightControl/Performance/AERISMapDramCache.cs")
guard = read("Source/AERISFlightControl/Performance/AERISMapDramDiskGuard.cs")
registry = read("Source/AERISFlightControl/Landing/AERISAirfieldRegistry.cs")
database = read("Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs")
window = read("Source/AERISFlightControl/UI/AERISWindow.cs")
bootstrap = read("Source/AERISFlightControl/Core/AERISBootstrap.cs")
navigation = read("Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
tiles = read("Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
project = read("Source/AERISFlightControl/AERISFlightControl.csproj")
build_version = read("Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
build = read("build_ubuntu.sh")
version = read("GameData/AERISFlightControl/AERISFlightControl.version")
runner = read("Tools/run_v01800_cp25_acceptance.py")
spec = read("Docs/CP25_MAP_DRAM_CACHE_FOUNDATION_HOTFIX_2_v0.18.0.0_ja.md")
test_card = read("Docs/ND_CP25_MAP_DRAM_CACHE_FOUNDATION_HOTFIX_2_TEST_CARD_v0.18.0.0_ja.md")
contract = read("ACCEPTANCE_v0.18.0.0_CP2.5_MAP_DRAM_CACHE_FOUNDATION_HOTFIX2.txt")

cache_code = strip_csharp_comments_and_literals(cache)
guard_code = strip_csharp_comments_and_literals(guard)
registry_code = strip_csharp_comments_and_literals(registry)
database_code = strip_csharp_comments_and_literals(database)

suite.check("AERISMapDramDiskGuard.cs" in project,
            "runtime disk guard is compiled by xbuild")
suite.check("[ThreadStatic] static int normalLookupDepth" in guard and
            "EnterNormalLookup" in guard and "BeforeSynchronousDisk" in guard,
            "thread-local normal-lookup scope and disk interception exist")
suite.check("RecordSynchronousDiskOperation(violation" in guard and
            'violation ? activeDomain : "MAINTENANCE"' in guard,
            "disk guard records both allowed maintenance I/O and lookup violations")
suite.check("System.IO" not in guard_code and "File." not in guard_code and
            "Directory." not in guard_code,
            "disk guard itself performs no filesystem operation")

suite.check("SnapshotAirfields" in cache and
            'EnterNormalLookup(this, "AIRFIELD_LIST")' in cache,
            "Airfield list lookup is a measured DRAM normal-lookup path")
suite.check("TryGetAirfieldView" in cache and "TryGetRunwayView" in cache and
            "TryGetDirectionView" in cache,
            "snapshot-owned Airfield, Runway and ILS-direction ID views exist")
suite.check('EnterNormalLookup(this, "AIRFIELD_ID")' in cache and
            'EnterNormalLookup(this, "RUNWAY_ID")' in cache and
            'EnterNormalLookup(this, "ILS_DIRECTION_ID")' in cache,
            "all Airfield ID dictionaries enter normal-lookup scope")
suite.check('EnterNormalLookup(this, "TERRAIN_CONTAINS")' in cache and
            '"TERRAIN_CHUNK_ID"' in cache,
            "Terrain presence and chunk-ID dictionaries enter the same guard scope")
suite.check("GuardedSynchronousDiskOperations" in cache and
            "AllowedSynchronousDiskOperations" in cache and
            "SynchronousDiskLookups" in cache,
            "guarded, allowed and violation counters are independent")
suite.check("RecordSynchronousDiskOperation(bool violation" in cache and
            "Interlocked.Increment(ref guardedSynchronousDiskOperations)" in cache and
            "Interlocked.Increment(ref allowedSynchronousDiskOperations)" in cache,
            "runtime guard counters have actual increment call sites")
suite.check("RecordForbiddenSynchronousDiskLookup" in cache and
            "RecordSynchronousDiskOperation(true" in cache,
            "compatibility violation entry point is live rather than an orphan counter")
suite.check("[CP2.5/MAP_DRAM_VIOLATION]" in cache and
            "LastSynchronousDiskLookupDomain" in cache and
            "LastSynchronousDiskLookupOperation" in cache,
            "violations include last domain and operation evidence")
suite.check("[CP2.5/MAP_DRAM_SUMMARY]" in cache and
            'result=" + (telemetry.SynchronousDiskLookups == 0L ?' in cache,
            "shutdown summary reports PASS or VIOLATION from measured telemetry")
suite.check("System.IO" not in cache_code and "File." not in cache_code and
            "Directory." not in cache_code and "FileStream" not in cache_code,
            "Map DRAM owner remains filesystem-free")
suite.check(all(token not in cache_code for token in
                ("CompressedPayload", "HeightSamples", "RenderTexture", "new Mesh", "new Material")),
            "Hotfix 2 does not cross the CP3 terrain-payload boundary")

suite.check("mapDramCache.SnapshotAirfields()" in registry,
            "runtime Registry Airfields property reads the shared snapshot")
suite.check("return mapDramCache == null ? airfields.AsReadOnly()" in registry,
            "mutable list fallback exists only for build-only/no-cache instances")
suite.check("internal int Count { get { return Airfields.Count; } }" in registry,
            "public registry count follows the DRAM view")
suite.check("IList<AERISAirfieldDefinition> values = Airfields" in registry and
            "values[index]" in registry,
            "index-based selection reads the DRAM list")
suite.check("TryGetMapAirfield(indexed.StableId" in registry and
            "TryGetMapDirection(indexed.StableId" in registry and
            "TryGetMapRunway(indexed.StableId" in registry,
            "selected Airport, Runway and ILS direction use stable-ID dictionaries")
suite.check("TryGetAirfieldView" in registry and "TryGetRunwayView" in registry and
            "TryGetDirectionView" in registry,
            "Registry façades use zero-copy immutable snapshot views")
commit = registry[registry.find("void CommitStaged"):registry.find("void FailReload")]
publish = commit.find("mapDramCache.PublishAirfields")
restore = commit.find("RestoreSelection")
revision = commit.find("databaseRevision = stagedDatabaseRevision")
suite.check(revision >= 0 and publish > revision and restore > publish,
            "committed revision publishes before selection restore reads DRAM")
restore_method = registry[registry.find("void RestoreSelection"):registry.find("void PersistSelection")]
suite.check("IList<AERISAirfieldDefinition> values = Airfields" in restore_method and
            "airfields.Count" not in restore_method,
            "persisted selection restoration searches the DRAM snapshot")
suite.check("registry.Airfields" in navigation and "airfields.Airfields" in tiles,
            "ND symbols and Terrain runway requests consume the routed Registry view")

suite.check("AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache" in registry,
            "Airfield producer I/O is wired to the runtime guard")
for token in ("AIRFIELD_CACHE_LOAD", "AIRFIELD_WITNESS_RELOAD",
              "AIRFIELD_DISCOVERY_CREATE_DIRECTORY", "AIRFIELD_DISCOVERY_ENUMERATE_CFG",
              "AIRFIELD_DISCOVERY_PARSE_CFG", "AIRFIELD_DISCOVERY_LOAD_SURVEY_CATALOG",
              "AIRFIELD_CACHE_SAVE"):
    suite.check(token in registry, "Airfield guard covers " + token)

suite.check("void BeforeSynchronousDisk(string operation)" in database and
            "AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache" in database,
            "Terrain Preload Database has one guard adapter")
for token in ("TERRAIN_MANIFEST_OPEN_READ", "TERRAIN_MANIFEST_CHUNK_EXISTS",
              "TERRAIN_RECOVERY_ENUMERATE_CHUNKS", "TERRAIN_RECOVERY_READ_CHUNK",
              "TERRAIN_COMMIT_WRITE_RECOVERY_MARKER", "TERRAIN_COMMIT_VERIFY_READ",
              "TERRAIN_PAYLOAD_READ_CHUNK", "TERRAIN_CHUNK_OPEN_WRITE",
              "TERRAIN_MANIFEST_OPEN_WRITE", "TERRAIN_ATOMIC_REPLACE",
              "TERRAIN_DELETE_FILE"):
    suite.check(token in database, "Terrain guard covers " + token)

# Every direct filesystem API in the two Map DRAM producer owners must be adjacent to
# an explicit guard. Comments are ignored, and MemoryStream is not a filesystem API.
def direct_disk_lines(text):
    lines = text.splitlines()
    results = []
    patterns = ("File.", "Directory.", "new FileStream", "new FileInfo")
    for index, line in enumerate(lines):
        stripped = line.strip()
        if stripped.startswith("//") or "No File.Exists" in stripped:
            continue
        if any(pattern in line for pattern in patterns):
            context = "\n".join(lines[max(0, index - 4):index + 1])
            results.append((index + 1, line.strip(), "BeforeSynchronousDisk" in context or
                            "GuardedFileLength" in context))
    return results

registry_disk = direct_disk_lines(registry)
database_disk = direct_disk_lines(database)
suite.check(len(registry_disk) >= 2 and all(item[2] for item in registry_disk),
            "every direct Airfield Registry filesystem call is guard-adjacent")
suite.check(len(database_disk) >= 20 and all(item[2] for item in database_disk),
            "every direct Terrain Preload Database filesystem call is guard-adjacent")

suite.check("SSD GUARD  OBSERVED" not in window and
            "AllowedSynchronousDiskOperations" not in window,
            "successor removes Map DRAM guard debug presentation from SYSTEM")
suite.check("AIRFIELD NORMAL READ  DRAM SNAPSHOT + ID INDEX — ACTIVE" not in window and
            "AERISMapDramDiskGuard" in guard,
            "successor hides Airfield read-routing debug while runtime guard remains")
suite.check("LAST VIOLATION" not in window and
            "LastSynchronousDiskLookupOperation" in cache,
            "successor hides violation debug while actionable telemetry remains in owner")
suite.check('MapDramCache.LogShutdownSummary("AERIS_SHUTDOWN")' in bootstrap,
            "AERIS shutdown emits one Map DRAM session summary before logger shutdown")
summary_pos = bootstrap.find('MapDramCache.LogShutdownSummary("AERIS_SHUTDOWN")')
logger_shutdown_pos = bootstrap.find("AERISLogger.Shutdown()")
suite.check(summary_pos >= 0 and logger_shutdown_pos > summary_pos,
            "summary is emitted while the dedicated logger is still active")

suite.check("CP2.5 MAP DRAM CACHE FOUNDATION HOTFIX 2" in build_version and
            "CP2.5 MAP DRAM CACHE FOUNDATION HOTFIX 1" in build_version,
            "identity adds Hotfix 2 while preserving Hotfix 1 regression identity")
suite.check("CP2.5 MAP DRAM CACHE FOUNDATION HOTFIX 2" in build,
            "Ubuntu build entrypoint generates Hotfix 2 identity")
suite.check("Map DRAM Cache Foundation Hotfix 2" in version,
            "KSP version metadata identifies Hotfix 2")
suite.check("selftest_v01800_cp25_map_dram_cache_foundation_hotfix2.py" in runner,
            "CP2.5 acceptance runner includes Hotfix 2")
suite.check("Hotfix 1" in spec and "SYNC SSD 0" in test_card and
            "MAP DRAM CACHE FOUNDATION HOTFIX 2" in contract,
            "specification, runtime test card and acceptance contract are included")

combined = "\n".join((cache, guard, registry, database, window, bootstrap, navigation, tiles))
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS"):
    suite.check(forbidden not in combined,
                "Hotfix 2 adds no " + forbidden + " authority/content")
# CP3 Gate 1 legitimately exposes its separate owner through TileSystem/Bootstrap.
# The Map DRAM owner, disk guard and Preload Database themselves must remain free
# of Resident Cache ownership and payload state.
suite.check("CurrentBodyResidentCache" not in cache + guard + database,
            "Map DRAM and Terrain database remain outside CP3 Resident ownership")

suite.finish()
