#!/usr/bin/env python3
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 3.1 Predictive Corridor successor")
owner_path = SOURCE / "Terrain/AERISCurrentBodyResidentCache.cs"
tiles_path = SOURCE / "Terrain/AERISTerrainTileSystem.cs"
corridor_path = SOURCE / "Terrain/AERISPredictiveForwardCorridor.cs"
preload_path = SOURCE / "Terrain/AERISTerrainPreloadDatabase.cs"
map_path = SOURCE / "Performance/AERISMapDramCache.cs"
owner = read(owner_path)
tiles = read(tiles_path)
corridor = read(corridor_path)
preload = read(preload_path)
map_dram = read(map_path)
ui = read(SOURCE / "UI/AERISWindow.cs")
bootstrap = read(SOURCE / "Core/AERISBootstrap.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
project = read(SOURCE / "AERISFlightControl.csproj")
build = read(ROOT / "build_ubuntu.sh")
generated = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP3_GATE3_PREDICTIVE_FORWARD_CORRIDOR.txt")
spec = read(ROOT / "Docs/CP3_GATE3_PREDICTIVE_FORWARD_CORRIDOR_v0.18.0.0_ja.md")
card = read(ROOT / "Docs/ND_CP3_GATE3_PREDICTIVE_FORWARD_CORRIDOR_TEST_CARD_v0.18.0.0_ja.md")
runner = read(ROOT / "Tools/run_v01800_cp3_gate31_acceptance.py")

for name, text in (("resident owner", owner), ("terrain tile system", tiles),
                   ("predictive corridor", corridor), ("preload database", preload)):
    clean = strip_csharp_comments_and_literals(text)
    suite.check(clean.count("{") == clean.count("}"), name + " C# braces are balanced")
    suite.check(clean.count("(") == clean.count(")"), name + " C# parentheses are balanced")

suite.check('Compile Include="Terrain\\AERISPredictiveForwardCorridor.cs"' in project,
            "predictive corridor source is included by the C# project")
suite.check("internal sealed class AERISPredictiveForwardCorridor" in corridor and
            "AERISPredictiveForwardCorridorSnapshot" in corridor,
            "Gate 3 planner and telemetry snapshot exist")
suite.check("Vector3d.Exclude(up, vessel.srf_velocity)" in corridor and
            "ResolveMapHeading(vessel)" in corridor and
            "Vector3d.Dot(vessel.angularVelocity, up)" in corridor,
            "prediction inputs are horizontal ground speed, shared track and vertical-axis turn rate")
suite.check("MinimumGroundSpeed = 5.0" in corridor and
            "MaximumTurnRateDegPerSecond = 12.0" in corridor and
            "MaximumPointCount = 18" in corridor,
            "prediction thresholds and point count are bounded")
suite.check("Math.Max(30.0, Math.Min(420.0" in corridor and
            "Math.Min(250000.0" in corridor and
            "135.0 / horizon" in corridor,
            "look-ahead time, distance and total heading change are bounded")
suite.check("speed / omega * (Math.Cos(heading) - Math.Cos(end))" in corridor and
            "speed / omega * (Math.Sin(end) - Math.Sin(heading))" in corridor,
            "constant-turn-rate local tangent prediction is implemented")
suite.check("CorridorHalfWidthMeters" in corridor and
            "course - 90.0" in corridor and "course + 90.0" in corridor,
            "left/right uncertainty edges are generated")
suite.check("AERISTerrainTileLod.Land ?" in corridor and
            "AERISTerrainTileLod.Local : lod" in corridor,
            "predictive corridor itself never emits LAND LOD")
for forbidden in ("FlightCtrlState", "ctrlState", "SetBankMode", "SetHeadingMode",
                  "MainThrottle", "SafetyLand", "File.", "Directory.", "FileStream"):
    suite.check(forbidden not in corridor,
                "predictive planner remains control-free and I/O-free: " + forbidden)

plan = tiles[tiles.find("void PlanRequests"):tiles.find("void AddLandingPointWithPins")]
suite.check("predictiveCorridor.Build" in plan and
            "AERISTerrainRequestLane.LookAhead" in plan,
            "planner output feeds the existing bounded look-ahead request lane")
suite.check(plan.find("AddLandingPointWithPins") < plan.find("predictiveCorridor.Build"),
            "selected LAND runway demand is admitted before speculative corridor demand")
suite.check("point.Centerline" in plan and
            "AERISResidentPinReason.ForwardCorridor" in plan,
            "only predictive centerline points are selected for corridor pinning")
suite.check("acceptedCorridorCount" in plan and
            "admittedDetail < detailBudget" in plan and
            "acceptedRequestScratch" in plan,
            "corridor requests remain inside the non-foundation detail budget")

suite.check("landing == null || !landing.Armed" in plan and
            "!landDetailActive" in plan,
            "LAND payload demand requires both ARM and LAND detail policy")
suite.check("AERISTerrainTileLod.Land" in tiles[tiles.find("void AddLandingPointWithPins"):
            tiles.find("void MarkResidentPin")],
            "selected runway endpoints request LAND plus Local fallback")
suite.check("IsBackgroundPopulationLod" in tiles and
            "lod == AERISTerrainTileLod.Land" not in tiles[tiles.find(
                "static bool IsBackgroundPopulationLod"):tiles.find(
                "static bool IsGate3ResidentLod")],
            "cruise background population excludes LAND LOD")
suite.check("lod == AERISTerrainTileLod.Land" in tiles[tiles.find(
                "static bool IsGate3ResidentLod"):tiles.find("static int CompareRequests")],
            "live demand path allows current-body LAND promotion")

pin_section = tiles[tiles.find("void MarkResidentPin"):tiles.find("void AddPointWithFallback")]
suite.check("GlobalFoundation" in pin_section and "Viewport" in pin_section and
            "ForwardCorridor" in pin_section and "Landing" in pin_section and
            "Runway" in pin_section,
            "all Gate 3 pin reasons have explicit priority")
suite.check("preloadDatabase.Contains(key)" in pin_section and
            "RegisterIndexed" in pin_section and "TryPin" in pin_section,
            "only indexed SSD payloads receive generation-scoped resident leases")
suite.check("desiredRequestIds.Contains" in pin_section and
            "ReleaseResidentPlanPin" in pin_section,
            "stale plan leases are released when requests leave the accepted plan")
for boundary in ("VIEWPORT SUSPENDED", "TERRAIN DISPLAY OFF", "BODY TRANSITION",
                 "SHUTDOWN"):
    suite.check(boundary in tiles, "pin/corridor reset boundary exists: " + boundary)
suite.check("ReleaseResidentPlanPins();\n            predictiveCorridor.Reset(reason);" in tiles,
            "explicit reset releases plan leases before Resident scope reset")

suite.check("IsGate3ResidencyLod" in owner and
            "lod == AERISTerrainTileLod.Land" in owner[owner.find(
                "static bool IsGate3ResidencyLod"):owner.find("static int ResidencyPriority")],
            "Resident owner accepts demand-gated LAND payloads")
suite.check("case AERISTerrainTileLod.Land: return 0;" in owner,
            "unpinned LAND becomes the first budget eviction class")
suite.check("LandCount" in owner and "LandBudgetRejects" in owner and
            "LandBudgetEvictions" in owner,
            "LAND residency has independent telemetry")
suite.check("TryPrepareSsdDecode" in owner and "TryMarkDecoded" in owner and
            "TryCommitRamResident" in owner,
            "Gate 2 generation-checked async promotion remains the only payload route")
for state, value in (("Indexed", 0), ("SsdReady", 1), ("Decoded", 2),
                     ("RamResident", 3), ("RenderReady", 4), ("GpuReady", 5)):
    suite.check((state + " = " + str(value)) in owner,
                "state contract remains fixed: " + state)

population = tiles[tiles.find("void ScheduleResidentPopulationRead"):
                   tiles.find("bool CompleteChunkLoadTracking")]
suite.check("AERISRuntimeLane.GeneralCompute" in population and
            "AERISRuntimeLane.SafetyLand" not in population,
            "resident population remains on shared GeneralCompute, never SafetyLand")
suite.check("preloadDatabase.TryLoadBatch" in population and
            population.find("preloadDatabase.TryLoadBatch") >
            population.find("runtime.Scheduler.SubmitLatest"),
            "SSD read/decode remains inside the shared worker")

suite.check("G/F/R/L/LD" in ui and "rs.LandCount" in ui and
            "rs.PinnedEntryCount" in ui,
            "SYSTEM Resident line exposes LAND and active pins")
suite.check("CP3 Corridor:" in ui and "TurnRateDegPerSecond" in ui and
            "LookAheadDistanceMeters" in ui and "LandDemandActive" in ui,
            "SYSTEM page exposes predictive corridor telemetry")
suite.check(
            'UiCheckpoint = "DEV CP3 GATE 3.1 — VIEWPORT-AUTHORITATIVE FAR BASE & VIRTUAL DETAIL FOUNDATION — COMPILE HOTFIX 1"' in generated and
            'UiCheckpoint = "DEV CP3 GATE 3.1 — VIEWPORT-AUTHORITATIVE FAR BASE & VIRTUAL DETAIL FOUNDATION — COMPILE HOTFIX 1"' in build,
            "generated/build tab version names Gate 3.1")
suite.check("DEV CP3 GATE 3 PREDICTIVE FORWARD CORRIDOR" in generated and
            "DEV CP3 GATE 3 PREDICTIVE FORWARD CORRIDOR" in build,
            "assembly and build identities name Gate 3")
suite.check("CP3 Gate 3 Predictive Forward Corridor" in version,
            "AVC package identity names Gate 3")
suite.check("Gate 3 predictive corridor warming is constrained to Far base payloads" in bootstrap,
            "startup log states the live Gate 3.1 corridor boundary")
suite.check("DEV CP2" in card and "CP3 GATE 2" in card and
            "現在build表記として残っていればFAIL" in card,
            "runtime card rejects stale CP2/Gate 2 tab labels")

suite.check("30～420秒" in spec and "最大250km" in spec and
            "最大135度" in spec and "最大18点" in spec,
            "Japanese Gate 3 spec records all prediction bounds")
suite.check("LAND LODはbackground population planへ含めない" in spec and
            "DISARMまたは巡航中" in spec,
            "spec records cruise LAND exclusion")
suite.check("NOT IMPLEMENTED IN GATE 3" in contract and
            "RENDER READY" in contract and "GPU READY" in contract,
            "Gate 4 work remains explicitly deferred")
suite.check("selftest_v01800_cp3_gate31_predictive_corridor_successor.py" in runner,
            "active Gate 3.1 runner executes the corridor successor")
suite.check("run_v01800_cp3_gate31_compile_hotfix1_acceptance.py" in build,
            "build entrypoint runs Gate 3.1 Compile Hotfix 1 acceptance")

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
