#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import (ROOT, SOURCE, CheckSuite, read, extract_method,
                            strip_csharp_comments_and_literals, text_sha256)

suite = CheckSuite("v0.18.0.0 CP2.5 Integrated Acceptance Candidate 4")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
blocks = read(SOURCE / "Terrain/AERISTerrainBlockPipeline.cs")
contracts = read(SOURCE / "Terrain/AERISTerrainPreloadContracts.cs")
database = read(SOURCE / "Terrain/AERISTerrainPreloadDatabase.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
sync = read(SOURCE / "AA/SyncModuleControlSurface.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_INTEGRATED_ACCEPTANCE_CANDIDATE4.txt")
spec = read(ROOT / "Docs/CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_4_v0.18.0.0_ja.md")
test_card = read(ROOT / "Docs/ND_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_4_TEST_CARD_v0.18.0.0_ja.md")

# Standard is the former Candidate 3 boost envelope.
standard = extract_method(scheduler, "SetStandardPreloadThroughput")
full = extract_method(scheduler, "SetManualPreloadBoost")
resolve = extract_method(builder, "ResolveBudget")
tick = extract_method(builder, "Tick")
suite.check("standardPreloadThroughput = active;" in standard and
            "activePermits = workerCeiling;" in standard and
            "safetyReservedPermits = 0;" in standard and
            "archivePaused = false;" in standard,
            "standard Preload receives the former boost permit envelope")
suite.check('reason = "STANDARD PRELOAD — FORMER BOOST ENVELOPE";' in scheduler,
            "standard throughput identity is explicit")
suite.check("Mathf.Clamp(frameMilliseconds * 0.70f, 8f, 24f)" in builder and
            "qps = 100000f;" in builder and "Math.Min(4096" in builder,
            "standard producer retains the validated 8-24 ms former boost envelope")
suite.check("SetStandardPreloadThroughput(true)" in builder and
            "blockPipeline.SetPreloadThroughput(true, boostActive)" in tick,
            "standard scheduler and block pipeline are activated outside Flight")
suite.check("ThreadPriority.Normal" in scheduler and
            "ConfigurePreloadLaneCapacity(permits.StandardPreloadThroughput, active)" in scheduler,
            "standard throughput raises worker priority and bounded queue capacity")

# FULL boost CPU ceiling and scheduler expansion.
suite.check("FullBoostMaxActive = Math.Max(2, LogicalProcessors - 1);" in scheduler,
            "FULL boost reserves only one logical CPU")
suite.check("workerTotal = configuredWorkers > 0 ? Math.Max(2, configuredWorkers) :" in scheduler and
            "permits.FullBoostMaxActive" in scheduler,
            "the existing AERIS pool is sized for the FULL ceiling")
suite.check("workerCeiling = fullBoostWorkerCeiling;" in full and
            "activePermits = workerCeiling;" in full and
            "safetyReservedPermits = 0;" in full,
            "FULL boost opens every FULL permit without a non-Flight reserve")
suite.check("ThreadPriority.AboveNormal" in scheduler and
            "active ? ThreadPriority.AboveNormal" in scheduler,
            "FULL boost temporarily raises workers to AboveNormal")
suite.check("Math.Max(512, workers.Length * 32)" in scheduler and
            "Math.Max(128, workers.Length * 8)" in scheduler,
            "FULL boost expands bounded GeneralCompute and Archive queues")
suite.check("new Thread(() => WorkerLoop(index))" in scheduler and
            "Task.Run(" not in scheduler and "ThreadPool." not in scheduler,
            "FULL boost reuses the bounded AERIS pool and creates no task pool")

# Producer depth and bounded PQS work.
suite.check("StandardPreloadActiveTiles = 64" in blocks and
            "FullBoostActiveTiles = 256" in blocks,
            "block pipeline expands from 64 standard to 256 FULL tiles")
suite.check("fullPreloadBoost ? 12 : standardPreloadThroughput ? 4 : 2" in blocks,
            "pending blocks per tile expand from 4 standard to 12 FULL")
suite.check("SetPreloadThroughput(bool standard, bool fullBoost)" in blocks,
            "one explicit policy drives block-pipeline throughput")
suite.check("Math.Min(256, Math.Max(64, workers * 16))" in tick and
            "Math.Max(512, workers * 128)" in tick,
            "FULL producer keeps a deep worker-scaled bounded queue")
suite.check("Mathf.Clamp(fullFrameMilliseconds * 4.0f, 48f, 160f)" in builder and
            "qps = 10000000f;" in builder and "Math.Min(65536" in builder,
            "FULL PQS work is aggressive but bounded to 48-160 ms and 65,536 samples")
suite.check("while (attempts-- > 0" in tick and
            "PendingWorkCount() < pendingTarget" in tick,
            "producer fills the pipeline without an unbounded busy loop")

# Parallel CPU encode stage.
suite.check("sealed class PendingEncodeWork" in builder and
            "pendingEncodes" in builder and "inFlightEncodes" in builder,
            "raw tiles enter an explicit CPU encode stage")
suite.check('"preload-encode:" + work.StableId' in builder and
            "AERISRuntimeLane.GeneralCompute" in extract_method(builder, "ScheduleEncode"),
            "tile encoding runs on GeneralCompute workers")
suite.check("AERISTerrainPreloadCodec.Encode" in extract_method(builder, "ScheduleEncode") and
            "QueueEncodedTile" in extract_method(builder, "ScheduleEncode"),
            "parallel codec output is forwarded to the SSD stage")
suite.check((("Math.Max(64, workers * 16)" in builder and
              "Math.Max(16, workers * 4)" in builder) or
             ("FullEncodeCommitCeiling = 56" in builder and
              "StandardEncodeCommitCeiling = 32" in builder and
              "ResolveEncodeCommitLimit" in builder)),
            "encode concurrency scales separately for standard and FULL or uses the safer downstream commit cap")

# SSD super-batch architecture.
suite.check("SaveEncodedBatch(IList<AERISTerrainPreloadEncodedTile>" in database,
            "database accepts already-compressed worker output")
save_encoded = extract_method(database, "SaveEncodedBatch")
suite.check("Dictionary<string" in save_encoded and "CommitEncodedChunkLocked" in save_encoded,
            "encoded tiles are grouped and committed by chunk")
suite.check("SaveManifestLocked();" in save_encoded and
            save_encoded.count("SaveManifestLocked();") == 1,
            "one manifest transaction covers a whole SSD super-batch")
suite.check("ScheduleChunkSuperBatch" in builder and
            '"preload-terrain-super-batch:" + batchId' in builder,
            "builder submits an explicit multi-chunk super-batch job")
suite.check("Math.Min(64, maximumConcurrentWrites * 4)" in builder and
            "Math.Min(64, maximumConcurrentWrites * 4) : 32" in builder,
            "standard and FULL use bounded 32/64 chunk super-batches")
suite.check("lastWriteBatchChunks" in builder and "lastWriteBatchTiles" in builder and
            "writeMbpsEma" in builder,
            "SSD batch size and throughput are measured")

# Pipeline telemetry and bottleneck diagnosis.
for token in ("StandardThroughputActive", "PipelineActiveTileLimit",
              "PipelinePendingBlockLimit", "EncodeQueueDepth", "EncodeActive",
              "WriteQueueDepth", "WriteActive", "WriteBatchChunks",
              "WriteBatchTiles", "Bottleneck", "GpuPreloadStage"):
    suite.check(token in contracts and token in builder and token in window,
                "throughput telemetry is end-to-end: " + token)
update = extract_method(builder, "UpdateBottleneck")
for token in ("NO WORK", "SSD WRITE", "CPU COMPRESSION", "PQS PRODUCER",
              "PIPELINE BALANCED"):
    suite.check(token in builder and token in spec,
                "bottleneck state is implemented and documented: " + token)
suite.check("[PRELOAD_PIPELINE]" in builder and "[PRELOAD_THROUGHPUT]" in builder,
            "pipeline state and throughput are periodically logged")
suite.check("PRELOAD FULL BOOST" in window and "SUPER BATCH" in window and
            "BOTTLENECK:" in window,
            "non-Flight UI exposes FULL pipeline and bottleneck telemetry")

# Honest GPU boundary: no fake work and no false claim.
gpu = extract_method(builder, "ResolveGpuPreloadStage")
suite.check("GPU COMPUTE CAPABLE — NO PRELOAD KERNEL ASSET / CPU AUTHORITATIVE" in builder,
            "GPU-capable hardware reports the missing preload kernel honestly")
suite.check("COMPUTE UNSUPPORTED" in builder and "RUNTIME NOT READY" in builder,
            "GPU diagnostics distinguish unsupported and unavailable runtime states")
clean_builder = strip_csharp_comments_and_literals(builder)
for forbidden in ("ComputeShader.Dispatch", ".Dispatch(", "AsyncGPUReadback.Request",
                  "new RenderTexture("):
    suite.check(forbidden not in clean_builder,
                "Candidate 4 adds no fake or unbundled GPU workload: " + forbidden)
suite.check("asset bundle" in spec and "GPU使用率上昇はCandidate 4の合格条件ではない" in test_card,
            "GPU limitation and runtime acceptance boundary are explicit")

# Manual, non-persistent and Flight-safe behavior remains.
start = extract_method(builder, "StartBoost")
stop = extract_method(builder, "StopBoost")
suite.check("HighLogic.LoadedSceneIsFlight" in start and
            "trigger=MANUAL" in start and "persistence=NONE" in start,
            "FULL boost is explicit, non-persistent and non-Flight only")
suite.check('if (isFlight && boostActive) StopBoost("FLIGHT_SAFETY");' in tick and
            tick.index('StopBoost("FLIGHT_SAFETY")') < tick.index("SchedulePendingEncodes"),
            "Flight revokes FULL boost before encode/write scheduling")
suite.check("fallback=STANDARD_FORMER_BOOST" in stop and
            "ApplyStandardSchedulerState(true)" in stop,
            "manual stop returns to the new standard throughput mode")
for forbidden in ("terrainPreloadBoost", "preloadBoostEnabled", "manualPreloadBoost ="):
    suite.check(forbidden not in read(SOURCE / "Settings/AERISSettings.cs") and
                forbidden not in read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg"),
                "FULL boost has no persistent setting: " + forbidden)

# Identity, docs and frozen control boundaries.
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 4" in build_version and
            "PRELOAD THROUGHPUT ARCHITECTURE HOTFIX 1" in build_version,
            "generated identity names Candidate 4")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 3" in build_version and
            "DISPLAY DEMAND POLICY HOTFIX 1" in build_version,
            "Candidate 3 identity remains in the chain")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 4" in build and
            "Integrated Acceptance Candidate 4" in version,
            "build entrypoint and KSP metadata identify Candidate 4")
suite.check("selftest_v01800_cp25_integrated_acceptance_candidate4.py" in runner,
            "full CP2.5 runner executes Candidate 4")
suite.check("STANDARD PRELOAD" in contract and "MANUAL FULL BOOST" in contract and
            "GPU BOUNDARY" in contract,
            "acceptance contract fixes standard, FULL and GPU boundaries")
suite.check("論理CPU数－1" in spec and "最大64 chunk" in spec and
            "NO PRELOAD KERNEL ASSET" in spec,
            "Japanese specification documents CPU, SSD and GPU boundaries")
suite.check("各60秒程度" in test_card and "FLIGHT_SAFETY" in test_card and
            "synchronousSSD=0" in test_card,
            "runtime card compares standard/FULL and preserves safety checks")
ctrl = extract_method(sync, "CtrlSurfaceUpdate")
suite.check(text_sha256(ctrl) == "a8fe50749807dec9eadf51f605e5c0e87f14112cb60ace4c9cb4b89487a7484c",
            "Candidate 2 control-surface law remains byte-identical")
all_changed = "\n".join((scheduler, blocks, contracts, database, builder, window))
clean = strip_csharp_comments_and_literals(all_changed)
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache",
                  "ctrlSurfaceRange =", "authorityLimiter ="):
    suite.check(forbidden not in clean,
                "Candidate 4 adds no forbidden authority/content: " + forbidden)

suite.finish()
