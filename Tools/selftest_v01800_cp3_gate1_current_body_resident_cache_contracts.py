#!/usr/bin/env python3
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 1 Current Body Resident Cache Contracts")
owner = read(SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs")
tiles = read(SOURCE / "Terrain/AERISTerrainTileSystem.cs")
awareness = read(SOURCE / "Terrain/AERISTerrainAwareness.cs")
bootstrap = read(SOURCE / "Core/AERISBootstrap.cs")
map_dram_path = SOURCE / "Performance/AERISMapDramCache.cs"
preload_db_path = SOURCE / "Terrain/AERISTerrainPreloadDatabase.cs"
map_dram = read(map_dram_path)
csproj = read(SOURCE / "AERISFlightControl.csproj")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE1_CURRENT_BODY_RESIDENT_CACHE_CONTRACTS.txt")
spec = read(ROOT / "Docs/CP3_GATE1_CURRENT_BODY_RESIDENT_CACHE_CONTRACTS_v0.18.0.0_ja.md")
card = read(ROOT / "Docs/ND_CP3_GATE1_CURRENT_BODY_RESIDENT_CACHE_CONTRACTS_TEST_CARD_v0.18.0.0_ja.md")
runner = read(ROOT / "Tools/run_v01800_cp3_gate1_acceptance.py")
clean_owner = strip_csharp_comments_and_literals(owner)

suite.check('internal sealed class AERISCurrentBodyResidentCache' in owner,
            "separate Current Body Resident Cache owner exists")
suite.check('Terrain\\AERISCurrentBodyResidentCache.cs' in csproj,
            "resident owner is compiled by the project")

for state, value in (("Indexed", 0), ("SsdReady", 1), ("Decoded", 2),
                     ("RamResident", 3), ("RenderReady", 4), ("GpuReady", 5)):
    suite.check((state + " = " + str(value)) in owner,
                "state contract is fixed: " + state)

for field in ("StableId", "BodyName", "BodyRadiusMillimetres", "EnvironmentHash",
              "Lod", "ScopeGeneration", "BodyGeneration", "DatabaseGeneration"):
    suite.check(("readonly " + ("AERISTerrainTileLod " if field == "Lod" else
        "string " if field in ("StableId", "BodyName", "EnvironmentHash") else
        "long ") + field) in owner,
        "commit token carries immutable ownership field: " + field)

suite.check(clean_owner.count("{") == clean_owner.count("}"),
            "resident owner has balanced C# braces")
suite.check(clean_owner.count("(") == clean_owner.count(")"),
            "resident owner has balanced C# parentheses")
suite.check(clean_owner.count("[") == clean_owner.count("]"),
            "resident owner has balanced C# brackets")

suite.check('token.ScopeGeneration != scopeGeneration' in owner and
            'token.BodyGeneration != bodyGeneration' in owner and
            'token.DatabaseGeneration != databaseGeneration' in owner,
            "stale generation commits are rejected")
suite.check('string.Equals(token.BodyName, activeBody' in owner and
            'token.BodyRadiusMillimetres != activeBodyRadiusMillimetres' in owner and
            'token.EnvironmentHash, activeEnvironmentHash' in owner,
            "foreign body/environment commits are rejected")
suite.check('entry.Key.StableId, token.StableId' in owner and
            'entry.Key.BodyName, token.BodyName' in owner and
            'entry.Key.BodyRadiusMillimetres != token.BodyRadiusMillimetres' in owner and
            'entry.Key.EnvironmentHash, token.EnvironmentHash' in owner and
            'entry.Key.Lod != token.Lod' in owner,
            "commit validation rechecks complete immutable tile identity")
suite.check('databaseGenerationTransitions++' in owner and
            'ClearEntriesLocked(reason);' in owner and 'scopeGeneration++;' in owner,
            "database generation changes invalidate prior scope")
suite.check('bool sameIdentity =' in owner and
            'string.Equals(activeBody, normalizedBody' in owner and
            'bool sameIdentity = active &&' not in owner,
            "inactive unsupported-body identity does not churn generations every frame")
suite.check('!string.IsNullOrEmpty(normalizedEnvironment)' in owner and
            'gas giants' in owner,
            "empty environment identity remains fail-closed")
suite.check('if (hadIdentity) bodyTransitions++;' in owner,
            "first activation is not miscounted as a body transition")
suite.check('currentBodyResidentCache.Reset(reason);' in tiles and
            'currentBodyResidentCache.Dispose();' in tiles,
            "scene reset and shutdown invalidate resident ownership")

suite.check('LinkedList<string> lru' in owner and 'FindEvictionCandidateLocked' in owner and
            'RemoveEntryLocked(candidate, AERISResidentEvictionReason.Budget)' in owner,
            "bounded LRU eviction contract exists")
suite.check('AERISResidentPinLease : IDisposable' in owner and
            'Interlocked.Exchange(ref owner, null)' in owner and
            'ReleasePin(' in owner,
            "pinning uses idempotent disposable leases")
suite.check('entry.PinCount == 0' in owner and 'entry.PinCount > 0' in owner,
            "LRU skips pinned entries and pinned commits can exceed budget")
suite.check('RamBudgetBytes' in owner and 'OverBudgetBytes' in owner and
            'BudgetRejects' in owner and 'BudgetEvictions' in owner,
            "resident RAM has independent budget telemetry")
suite.check('SnapshotTelemetry()' in owner and 'StaleCommitRejects' in owner and
            'ForeignBodyRejects' in owner and 'BodyTransitions' in owner,
            "independent lifecycle and rejection telemetry exists")

suite.check('SynchronizeResidentScope(currentBody)' in tiles and
            tiles.index('SynchronizeResidentScope(currentBody)') < tiles.index('if (!flightViewportEnabled)', tiles.index('SynchronizeResidentScope(currentBody)')),
            "current body scope is synchronized before altitude-gate return")
suite.check('ResolveResidentCacheBudgetBytes' in tiles and
            'hotBudget * 4L' in tiles and '256L * 1024L * 1024L' in tiles and
            '4L * 1024L * 1024L * 1024L' in tiles,
            "Gate 1 uses a separately-accounted bounded provisional budget")
suite.check('CurrentBodyResidentCache' in awareness and 'CurrentBodyResidentCache' in bootstrap,
            "owner telemetry is reachable without draw-path wiring")

for forbidden in ("AERISWorkerScheduler", "AERISMapDramCache mapDramCache",
                  "File.Read", "FileStream", "Directory.", "RenderTexture",
                  "ComputeBuffer", "new Mesh", "ThreadPool", "Task<"):
    suite.check(forbidden not in clean_owner,
                "resident owner has no forbidden Gate 1 dependency: " + forbidden)
suite.check(tiles.count('TryCommitRamResident(') == 0 and
            tiles.count('RegisterIndexed(') == 0 and
            tiles.count('TryMarkSsdReady(') == 0,
            "live Terrain pipeline does not commit payloads in Gate 1")
suite.check('payloadRoute=DISCONNECTED' in owner and
            'payload/decode/render/GPU routes remain disconnected' in bootstrap,
            "runtime identity explicitly reports disconnected payload route")

suite.check(hashlib.sha256(map_dram_path.read_bytes()).hexdigest() ==
            '32f69a41ef84a6ecef280921fcd5ae9f13d729eba7a080ef53fee644c24679e5',
            "Map DRAM implementation remains byte-identical")
suite.check(hashlib.sha256(preload_db_path.read_bytes()).hexdigest() ==
            'de4325530b019d812fdc4566fe767758f559d6b21d7a295df56cd24a34124df9',
            "Terrain Preload Database remains byte-identical")
suite.check('compressed payload' in map_dram.lower() and 'metadata-only' in map_dram.lower() and
            'RenderTexture' in map_dram,
            "Map DRAM source still declares metadata-only separation")

identity = "CP3 GATE 1 CURRENT BODY RESIDENT CACHE CONTRACTS"
suite.check(identity in build_version and identity in build,
            "generated and build identities name CP3 Gate 1")
suite.check('run_v01800_cp3_gate1_acceptance.py' in build,
            "build entrypoint runs CP3 Gate 1 acceptance")
suite.check('selftest_v01800_cp3_gate1_current_body_resident_cache_contracts.py' in runner,
            "active CP3 runner begins with Gate 1 contract test")
suite.check('current body resident cache' in contract.lower() and 'NOT CONNECTED IN GATE 1' in contract,
            "acceptance contract records implemented and deferred scope")
suite.check('Terrain表示速度は変化しない' in spec and
            'Map DRAMへ書き込まず' in spec,
            "Japanese specification states no speed claim and owner separation")
suite.check('KSP起動は1回' in card and 'payloadRoute=DISCONNECTED' in card and
            'RAM RESIDENT tile数が0' in card,
            "runtime card tests lifecycle without expecting payload residency")

runtime = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for token in ("StartPreloadBoost", "StopPreloadBoost", "START PRELOAD BOOST",
              "STOP PRELOAD BOOST", "[PRELOAD_BOOST]"):
    suite.check(token not in runtime, "FULL BOOST remains absent: " + token)

# Protected project boundaries remain byte-identical to the accepted CP2.5 baseline.
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
    suite.check(tree_hash(rel) == expected, "protected tree remains byte-identical: " + rel)

suite.finish()
