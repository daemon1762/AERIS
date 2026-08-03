#!/usr/bin/env python3
import hashlib
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2.5 Final Closure Standard Preload Only / CP3 Entry Baseline")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
blocks = read(SOURCE / "Terrain/AERISTerrainBlockPipeline.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
contracts = read(SOURCE / "Terrain/AERISTerrainPreloadContracts.cs")
tiles = read(SOURCE / "Terrain/AERISTerrainTileSystem.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
settings = read(SOURCE / "Settings/AERISSettings.cs")
fdi = read(SOURCE / "UI/AERISFlightInstrument.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_FINAL_CLOSURE_STANDARD_PRELOAD_ONLY.txt")
spec = read(ROOT / "Docs/CP25_FINAL_CLOSURE_STANDARD_PRELOAD_ONLY_v0.18.0.0_ja.md")
card = read(ROOT / "Docs/ND_CP25_FINAL_CLOSURE_SMOKE_TEST_CARD_v0.18.0.0_ja.md")
cp3 = read(ROOT / "Docs/CP3_CURRENT_BODY_RESIDENT_CACHE_START_HERE_v0.18.0.0_ja.md")

runtime_sources = "\n".join((scheduler, blocks, builder, contracts, tiles, window))
for token in (
    "StartPreloadBoost", "StopPreloadBoost", "StartBoost()", "StopBoost(",
    "SetManualPreloadBoost", "ManualPreloadBoost", "FullBoostMaxActive",
    "FullBoostActiveTiles", "FullBoostOutstandingBlocks", "BoostActive",
    "BoostWorkers", "BoostPermits", "BoostQueueTarget",
    "BoostMainThreadBudgetMilliseconds", "BoostAutoStops",
    "START PRELOAD BOOST", "STOP PRELOAD BOOST", "[PRELOAD_BOOST]"):
    suite.check(token not in runtime_sources, "FULL BOOST runtime surface removed: " + token)

suite.check("InitialMaxActive = Math.Max(2, LogicalProcessors - ReservedProcessors);" in scheduler,
            "standard worker ceiling remains hardware-scaled")
suite.check("workerTotal = configuredWorkers > 0 ? Math.Max(2, configuredWorkers)" in scheduler and
            "permits.InitialMaxActive" in scheduler,
            "worker pool no longer allocates a FULL-only ceiling")
suite.check("ConfigureWorkerCeiling(workerTotal);" in scheduler,
            "one worker ceiling configures the runtime pool")
suite.check("STANDARD PRELOAD — VALIDATED CP2.5 ENVELOPE" in scheduler,
            "standard permit policy is explicit")
suite.check("SetWorkerPriority(active ? ThreadPriority.Normal" in scheduler and
            "ThreadPriority.AboveNormal" not in scheduler,
            "standard preload uses Normal priority and no FULL priority")
suite.check("Math.Max(128, workers.Length * 12)" in scheduler and
            "Math.Max(32, workers.Length * 4)" in scheduler,
            "standard GeneralCompute and Archive queues remain bounded")
suite.check("SubmitRequired" in scheduler and "RequiredDropped" in scheduler,
            "commit-required backpressure infrastructure remains")

suite.check("StandardPreloadActiveTiles = 64" in blocks,
            "standard active tile limit is 64")
suite.check("StandardOutstandingBlocks = 96" in blocks,
            "standard outstanding block limit is 96")
suite.check("return standardPreloadThroughput ? 4 : 2" in blocks,
            "standard per-tile pending block limit is 4")
suite.check("SetPreloadThroughput(bool standard)" in blocks,
            "block pipeline exposes one throughput selector")
suite.check("SubmitRequired" in blocks,
            "terrain blocks remain commit-required")
suite.check("RecoverPreloadAfterBackpressure" in blocks,
            "transient block recovery remains available")

suite.check("ApplyStandardSchedulerState(true);" in builder and
            "blockPipeline.SetPreloadThroughput(true);" in builder,
            "non-Flight Tick always selects STANDARD")
suite.check("blockPipeline.SetPreloadThroughput(false);" in builder and
            "PRELOAD SUSPENDED / FLIGHT READ PRIORITY" in builder,
            "Flight hard-suspends Preload")
suite.check("queueTarget = Math.Min(48, Math.Max(8, workers * 4))" in builder and
            "pendingTarget = Math.Max(24, workers * 16)" in builder,
            "standard producer queue is bounded")
suite.check("Mathf.Clamp(frameMilliseconds * 0.70f, 8f, 24f)" in builder and
            "Math.Min(4096" in builder,
            "standard base PQS envelope remains bounded")
suite.check("Math.Min(StandardEncodeCommitCeiling, scaled)" in builder and
            "StandardEncodeCommitCeiling = 32" in builder,
            "CPU Encode commit-required cap is 32")
suite.check("return 1;" in builder[builder.index("int ResolveWriteCommitLimit()"):builder.index("bool IsEncodeCurrent")],
            "SSD commit-required job cap is 1")
suite.check("chunksPerSuperBatch = 32" in builder,
            "STANDARD SSD super-batch is bounded to 32 chunks")
suite.check(builder.count("SubmitRequired(") >= 2,
            "CPU Encode and SSD stages use required admission")
suite.check("MonitorPreloadHealth(now)" in builder and
            "now - preloadLastProgressRealtime >= 6f" in builder,
            "generic STANDARD stall monitor is active")
suite.check("[PRELOAD_RECOVERY]" in builder and
            "databasePayload=UNCHANGED" in builder,
            "STANDARD recovery is observable and preserves durable payload")
suite.check("mode=STANDARD" in builder and "mode=FULL" not in builder,
            "throughput logs expose STANDARD only")

suite.check("PRELOAD STANDARD — CP2.5 FINAL" in window,
            "Preload UI identifies the final single mode")
suite.check("STANDARD ACTIVE — VALIDATED CP2.5 ENVELOPE" in window,
            "Preload UI exposes standard state")
suite.check("START PRELOAD BOOST" not in window and "STOP PRELOAD BOOST" not in window,
            "Preload UI has no boost controls")
suite.check("GUI.Button(launcher,\"AERIS PRELOAD\")" not in window,
            "independent top-right launcher remains absent")
suite.check("DrawPreloadOnly()" in window and "DrawPreloadTerrainMapsPage();" in window,
            "toolbar-owned non-Flight Preload surface remains")
suite.check("DOWNSTREAM REQUIRED" in window and "required-drop" in window,
            "required-stage diagnostics remain visible")

for field in ("BoostActive", "BoostWorkers", "BoostPermits", "BoostQueueTarget",
              "BoostMainThreadBudgetMilliseconds", "BoostAutoStops",
              "BuilderBoostAutoStops"):
    suite.check(field not in contracts, "boost-only telemetry removed: " + field)
suite.check("StandardThroughputActive" in contracts,
            "standard status telemetry remains")

suite.check("NavigationDisplayMode = AERISDisplayMode.Automatic" in settings and
            "FlightInstrumentDisplayMode = AERISDisplayMode.Automatic" in settings,
            "ND and FDI retain AUTO defaults")
suite.check('new string[]{"AUTO","ALWAYS","OFF"}' in window,
            "ND and FDI retain AUTO / ALWAYS / OFF selectors")
suite.check("FDI — SPEED GUIDANCE" in fdi and
            '"AP SPEED — " + speedMode + " ONLY"' in fdi and
            'core.Velocity.TargetConfirmed ? "VEL" : "ACC"' in fdi,
            "speed-only FDI remains")

suite.check("if(count==0){" in window and
            "manualCalibratedOnly&&count==0" not in window,
            "all AIRFIELDS zero-count categories remain generically disabled")
suite.check('WrappedAirfieldLabel("None.")' not in window,
            "zero-count AIRFIELDS categories allocate no None row")

identity = "CP2.5 FINAL CLOSURE STANDARD PRELOAD ONLY CP3 ENTRY BASELINE"
suite.check(identity in build_version and identity in build,
            "generated and build identities name final closure")
suite.check("Final Closure Standard Preload Only CP3 Entry Baseline" in version,
            "AVC metadata names final closure")
suite.check("selftest_v01800_cp25_final_closure_standard_preload_only.py" in runner,
            "active CP2.5 runner starts with final closure regression")
suite.check("FULL BOOST" in contract and "removed" in contract.lower() and
            "CP2.5 Track A is closed" in contract,
            "acceptance contract records removal and closure")
suite.check("完全に削除" in spec and "CP2.5 Track Aを閉じ" in spec,
            "Japanese closure specification records project decision")
suite.check("KSP起動は1回" in card and "FULL BOOST" in card and
            "存在しない" in card,
            "runtime smoke card minimizes restart and checks absence")
suite.check("AERISCurrentBodyResidentCache" in cp3 and "Gate 1" in cp3 and
            "FULL BOOST復活" in cp3,
            "CP3 start document fixes the next scope and prohibition")

# CP3 implementation must not leak into the CP2.5 closure baseline.
for token in ("class AERISCurrentBodyResidentCache", "CurrentBodyResidentCache",
              "RAM RESIDENT", "RENDER READY", "GPU READY"):
    suite.check(token not in runtime_sources,
                "CP2.5 runtime remains outside CP3 payload residency: " + token)

# Frozen project boundaries must match the last accepted Airfields UI baseline.
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

clean_runtime = strip_csharp_comments_and_literals(runtime_sources)
for forbidden in ("FlightInputHandler.state", "MainThrottle =",
                  "RunwayMasterCorrection", "CURATED_RUNWAY_GEODETIC_DEFAULTS"):
    suite.check(forbidden not in clean_runtime,
                "closure adds no forbidden authority/content: " + forbidden)

suite.finish()
