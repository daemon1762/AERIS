#!/usr/bin/env python3
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 2 Decode RAM Resident")
owner_path = SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
preload_path = SOURCE / "Terrain/AERISTerrainPreloadDatabase.cs"
ram_path = SOURCE / "Terrain/AERISTerrainTileCache.cs"
map_path = SOURCE / "Performance/AERISMapDramCache.cs"
owner = read(owner_path)
tiles = read(tiles_path)
preload = read(preload_path)
ram = read(ram_path)
map_dram = read(map_path)
ui = read(SOURCE / "UI/AERISWindow.cs")
bootstrap = read(SOURCE / "Core/AERISBootstrap.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
build = read(ROOT / "build_ubuntu.sh")
generated = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE2_DECODE_RAM_RESIDENT.txt")
spec = read(ROOT / "Docs/CP3_GATE2_DECODE_RAM_RESIDENT_v0.18.0.0_ja.md")
card = read(ROOT / "Docs/ND_CP3_GATE2_DECODE_RAM_RESIDENT_TEST_CARD_v0.18.0.0_ja.md")
runner = read(ROOT / "Tools/run_v01800_cp3_gate2_acceptance.py")
clean_owner = strip_csharp_comments_and_literals(owner)
clean_tiles = strip_csharp_comments_and_literals(tiles)
clean_preload = strip_csharp_comments_and_literals(preload)

suite.check(clean_owner.count("{") == clean_owner.count("}"),
            "resident owner C# braces are balanced")
suite.check(clean_tiles.count("{") == clean_tiles.count("}"),
            "terrain tile system C# braces are balanced")
suite.check(clean_preload.count("{") == clean_preload.count("}"),
            "preload database C# braces are balanced")
suite.check(clean_owner.count("(") == clean_owner.count(")"),
            "resident owner C# parentheses are balanced")
suite.check(clean_tiles.count("(") == clean_tiles.count(")"),
            "terrain tile system C# parentheses are balanced")

for state, value in (("Indexed", 0), ("SsdReady", 1), ("Decoded", 2),
                     ("RamResident", 3), ("RenderReady", 4), ("GpuReady", 5)):
    suite.check((state + " = " + str(value)) in owner,
                "state contract remains fixed: " + state)

suite.check("internal enum AERISResidentCommitResult" in owner and
            "BudgetRejected = 4" in owner,
            "RAM commit publishes an explicit budget result")
suite.check("TryPrepareSsdDecode" in owner and
            "AERISResidentTileState.SsdReady" in owner and
            "asyncDecodeSubmissions++" in owner,
            "indexed metadata atomically enters SSD READY")
suite.check("TryMarkDecoded" in owner and
            "TryCommitRamResident" in owner and
            "AERISResidentTileState.RamResident" in owner,
            "DECODED to RAM RESIDENT ownership transfer exists")
suite.check("payloadRoute=ASYNC_DECODE_RAM_RESIDENT" in owner,
            "runtime scope reports the connected Gate 2 payload route")
suite.check("RecordDecodeFailure" in owner and "AsyncDecodeFailures" in owner,
            "asynchronous decode failures are independently counted")
suite.check("GlobalCount" in owner and "FarCount" in owner and
            "RouteCount" in owner and "LocalCount" in owner,
            "resident telemetry publishes per-LOD counts")

priority = owner[owner.find("static int ResidencyPriority"):owner.find(
    "void IncrementBudgetRejectLocked")]
for marker in ("Global: return 4", "Far: return 3", "Route: return 2",
               "Local: return 1"):
    suite.check(marker in priority, "resident protection priority: " + marker)
suite.check("ResidencyPriority(incomingLod)" in owner and
            "priority <= maximumResidencyPriority" in owner,
            "lower-priority admission cannot evict a higher-priority foundation")
suite.check("IncrementBudgetRejectLocked" in owner and
            "IncrementBudgetEvictionLocked" in owner,
            "staged degradation has per-LOD rejection and eviction telemetry")
suite.check("lod == AERISTerrainTileLod.Global" in owner and
            "lod == AERISTerrainTileLod.Local" in owner and
            "AERISTerrainTileLod.Land" not in owner[owner.find(
                "static bool IsGate2ResidencyLod"):owner.find(
                "static int ResidencyPriority")],
            "Gate 2 resident owner accepts Global/Far/Route/Local but not LAND")

suite.check("SnapshotCompleteKeysForBody" in preload and
            "entry.State != AERISTerrainGenerationState.Complete" in preload,
            "current-body population plan enumerates complete indexed metadata")
snapshot_method = preload[preload.find("SnapshotCompleteKeysForBody"):preload.find(
    "internal void LoadIndexOnly", preload.find("SnapshotCompleteKeysForBody"))]
for forbidden in ("File.", "Directory.", "FileStream", "ReadAll"):
    suite.check(forbidden not in snapshot_method,
                "population metadata snapshot performs no disk I/O: " + forbidden)
suite.check("((int)a.Lod).CompareTo((int)b.Lod)" in snapshot_method,
            "population metadata is ordered Global -> Far -> Route -> Local")

suite.check("residentPopulationPlan" in tiles and
            "RefreshResidentPopulationPlan" in tiles and
            "ScheduleResidentPopulationRead" in tiles,
            "current-body background population planner is connected")
suite.check("residentLoadsInFlight > 0" in tiles and
            "terrain-resident-populate:" in tiles,
            "background population is bounded to one chunk in flight")
suite.check("diskLoadsInFlight + residentLoadsInFlight < limit" in tiles,
            "viewport and resident reads share the existing bounded read budget")
suite.check(tiles.find("SchedulePreloadReads();") < tiles.find(
                "ScheduleResidentPopulationRead();", tiles.find("SchedulePreloadReads();")),
            "normal viewport preload reads are scheduled before background population")
suite.check("AERISRuntimeLane.GeneralCompute" in tiles and
            "terrain-resident-populate:" in tiles,
            "resident SSD/decode work uses the shared GeneralCompute lane")
population_method = tiles[tiles.find("void ScheduleResidentPopulationRead"):
                          tiles.find("bool CompleteChunkLoadTracking")]
suite.check("AERISRuntimeLane.SafetyLand" not in population_method,
            "resident population never occupies the Flight safety lane")
suite.check("preloadDatabase.TryLoadBatch" in population_method and
            population_method.find("preloadDatabase.TryLoadBatch") >
            population_method.find("runtime.Scheduler.SubmitLatest"),
            "SSD read/decode remains inside the scheduler worker")
suite.check("TryMarkDecoded(token)" in population_method and
            "TryCommitRamResident(token, tile" in population_method,
            "background worker commits DECODED payloads to RAM residency")
suite.check("residentPopulationBlockedFromLod" in population_method and
            "AERISResidentCommitResult.BudgetRejected" in population_method,
            "population stops the rejected LOD and all lower-priority levels")
suite.check("RESIDENT POPULATION CONTINUES" in tiles,
            "altitude-gate and display-off paths retain RAM population")

suite.check("TryGetRamResident(request.Key" in tiles and
            "CURRENT BODY RAM RESIDENT HIT" in tiles,
            "normal viewport path reuses current-body RAM payloads")
suite.check("TryPrepareSsdDecode(request.Key" in tiles and
            "TryMarkDecoded(pair.Value)" in tiles and
            "TryCommitRamResident(pair.Value, tile)" in tiles,
            "normal SSD path promotes payload into Resident Cache")
suite.check("tile.LastAccessSequence =" not in ram,
            "transient viewport LRU does not mutate resident-owned payload metadata")
suite.check("ram.Put(tile);" in tiles,
            "viewport cache can share immutable resident payload references")

scope_method = tiles[tiles.find("void SynchronizeResidentScope"):
                     tiles.find("void BeginBody", tiles.find("void SynchronizeResidentScope"))]
suite.check("preloadDatabase.RequestGeneration" in scope_method and
            "preloadDatabase.DatabaseGeneration" not in scope_method.split(
                "RefreshResidentPopulationPlan")[0],
            "resident commit scope uses the invalidating database request epoch")
suite.check("databaseGeneration" in owner and "scopeGeneration++" in owner and
            "ClearEntriesLocked(reason);" in owner,
            "body/database request-epoch transitions reject stale commits")
suite.check("currentBodyResidentCache.Reset(reason);" in tiles and
            "currentBodyResidentCache.Dispose();" in tiles,
            "scene reset and shutdown evict resident ownership")

suite.check("AERISBuildVersion.UiCheckpoint" in ui and
            "DEV CP2 — \"+suffix" not in ui,
            "tab menu version is sourced from the current CP3 checkpoint")
suite.check('UiCheckpoint = "DEV CP3 GATE 2 — DECODE / RAM RESIDENT"' in generated and
            'UiCheckpoint = "DEV CP3 GATE 2 — DECODE / RAM RESIDENT"' in build,
            "generated/build version constants name Gate 2")
suite.check("CP3 Resident:" in ui and "AsyncDecodeSuccesses" in ui and
            "rs.GlobalCount" in ui,
            "SYSTEM performance page exposes Gate 2 resident telemetry")
suite.check("DEV CP3 GATE 2 DECODE RAM RESIDENT" in generated and
            "DEV CP3 GATE 2 DECODE RAM RESIDENT" in build,
            "assembly and build identities name Gate 2")
suite.check("CP3 Gate 2 Decode RAM Resident" in version,
            "AVC package identity names Gate 2")
suite.check("CP3 Gate 2 asynchronously promotes" in bootstrap and
            "LAND promotion" in bootstrap,
            "startup log states the active and deferred Gate 2 boundaries")

suite.check("void ApplyStandardSchedulerState(bool active)" in builder and
            "runtime.Scheduler.SetStandardPreloadThroughput(active);" in builder,
            "Gate 1 compile hotfix remains present")
suite.check("internal void SetStandardPreloadThroughput(bool active)" in scheduler,
            "STANDARD scheduler API remains available")
suite.check(hashlib.sha256(map_path.read_bytes()).hexdigest() ==
            "32f69a41ef84a6ecef280921fcd5ae9f13d729eba7a080ef53fee644c24679e5",
            "Map DRAM implementation remains byte-identical and metadata-only")
suite.check("compressed payload" in map_dram.lower() and
            "metadata-only" in map_dram.lower() and "RenderTexture" in map_dram,
            "Map DRAM source still forbids decoded/GPU payload ownership")

suite.check("Global → Far → Route → Local" in spec and
            "Local → Route → Far → Global" in spec,
            "Japanese Gate 2 spec records population and degradation order")
suite.check("LAND LODのResident population" in spec and
            "Forward Corridor" in spec,
            "Gate 3/4 work remains explicitly deferred")
suite.check("DEV CP2" in card and "CP3 GATE 1" in card and
            "現在build表記として残っていればFAIL" in card,
            "runtime card explicitly rejects stale tab version labels")
suite.check("NOT IMPLEMENTED IN GATE 2" in contract and
            "Other-body RAM residency" in contract,
            "acceptance contract forbids scope expansion")
suite.check("selftest_v01800_cp3_gate2_decode_ram_resident.py" in runner,
            "active Gate 2 runner executes the dedicated test")
for superseded in (
    "selftest_v01800_cp25_final_closure_standard_preload_only.py",
    "selftest_v01800_cp25_integrated_acceptance_candidate1.py",
    "selftest_v01800_cp25_map_dram_cache_foundation_hotfix1.py",
):
    suite.check(superseded in runner and "superseded_boundaries" in runner,
                "Gate 2 runner explicitly replaces obsolete absence boundary: " + superseded)
suite.check("run_v01800_cp3_gate2_acceptance.py" in build,
            "build entrypoint runs Gate 2 acceptance")

runtime = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for token in ("StartPreloadBoost", "StopPreloadBoost", "START PRELOAD BOOST",
              "STOP PRELOAD BOOST", "[PRELOAD_BOOST]"):
    suite.check(token not in runtime, "FULL BOOST remains absent: " + token)

# Flight-control and runway data remain frozen.
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

suite.finish()
