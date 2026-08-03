#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, extract_method, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2.5 Candidate 4 Full Boost Backpressure Hotfix 1")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
blocks = read(SOURCE / "Terrain/AERISTerrainBlockPipeline.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
contracts = read(SOURCE / "Terrain/AERISTerrainPreloadContracts.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_CANDIDATE4_FULL_BOOST_BACKPRESSURE_HOTFIX1.txt")
spec = read(ROOT / "Docs/CP25_CANDIDATE4_FULL_BOOST_BACKPRESSURE_HOTFIX_1_v0.18.0.0_ja.md")
test_card = read(ROOT / "Docs/ND_CP25_CANDIDATE4_FULL_BOOST_BACKPRESSURE_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md")

for path in (
    SOURCE / "Performance/AERISWorkerScheduler.cs",
    SOURCE / "Terrain/AERISTerrainBlockPipeline.cs",
    SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs",
    SOURCE / "Terrain/AERISTerrainPreloadContracts.cs",
    SOURCE / "UI/AERISWindow.cs",
):
    suite.check(path.exists(), str(path.relative_to(ROOT)) + " exists")

# Scheduler admission/result guarantees.
suite.check("internal bool SubmitRequired(" in scheduler,
            "scheduler exposes a commit-required admission API")
suite.check("bool SubmitInternal(" in scheduler and "commitRequired" in scheduler and
            "requiredRejected" in scheduler,
            "required admission has explicit telemetry")
suite.check("if (commitRequired)" in scheduler and
            "queue.Jobs.Count >= queue.Capacity" in scheduler and
            "Interlocked.Increment(ref requiredRejected);" in scheduler,
            "full required queue returns backpressure instead of eviction")
suite.check("latestJobIds.ContainsKey(job.IdentityKey)" in scheduler and
            "queue.ByKey.ContainsKey(key)" in scheduler,
            "required identities are never replaced while admitted")
suite.check("CommitRequired" in scheduler and "ResultCapacity = 256" in scheduler,
            "result entries carry required ownership under a fixed nominal capacity")
suite.check("removedNode.Value.Job.CommitRequired" in scheduler,
            "result eviction skips commit-required entries")
suite.check("else if (!job.CommitRequired)" in scheduler,
            "best-effort incoming result is dropped when only required results remain")
suite.check("result.Job.CommitRequired && result.Job.Commit != null" in scheduler and
            "result.Job.Commit(null);" in scheduler,
            "required owner receives a terminal null callback on failure/stale")
suite.check("RequiredResultDepth" in scheduler and "RequiredRejected" in scheduler and
            "RequiredDropped" in scheduler,
            "required backpressure telemetry is published")
suite.check("Interlocked.Increment(ref requiredDropped)" not in scheduler,
            "implemented scheduler path contains no required-result drop operation")

# Global credits bound required work below result capacity.
suite.check("StandardOutstandingBlocks = 96" in blocks,
            "standard global outstanding limit is 96")
suite.check("FullBoostOutstandingBlocks = 192" in blocks,
            "FULL global outstanding limit is 192")
suite.check("OutstandingBlocks >= OutstandingBlockLimit" in blocks,
            "producer stops before global credit overflow")
suite.check("TryAcquireOutstanding" in blocks and "ReleaseOutstanding" in blocks,
            "block jobs use explicit acquire/release credits")
suite.check("sealed class BlockLease" in blocks and "outstandingEpoch" in blocks,
            "credits are epoch protected across recovery")
suite.check("Interlocked.Exchange(ref lease.Released, 1)" in blocks,
            "each credit is released at most once")
suite.check("bool SubmitCompletedBlock(TileState state)" in blocks and
            "runtime.Scheduler.SubmitRequired(" in blocks,
            "terrain blocks use the required scheduler path")
submit_block = blocks[blocks.index("bool SubmitCompletedBlock(TileState state)"):blocks.index("static void ProcessBlock", blocks.index("bool SubmitCompletedBlock(TileState state)"))]
suite.check("state.SamplingBlock = null" in submit_block and
            submit_block.index("if (!accepted)") < submit_block.index("state.SamplingBlock = null"),
            "sampled block is retained when scheduler admission is rejected")
suite.check("Interlocked.Increment(ref state.PendingBlocks)" in blocks and
            "DecrementPending(state)" in blocks,
            "per-tile pending credit has symmetric retirement")
suite.check("BlockPayload result = value as BlockPayload" in blocks and
            "if (result == null)" in blocks,
            "worker terminal failure is converted into recoverable tile cancellation")
suite.check("RecoverPreloadAfterBackpressure" in blocks and
            "WorkOwner != AERISTerrainWorkOwner.PreloadBuilder" in
            extract_method(blocks, "RecoverPreloadAfterBackpressure"),
            "recovery removes only Preload Builder transient states")
suite.check("Interlocked.Increment(ref outstandingEpoch)" in
            extract_method(blocks, "RecoverPreloadAfterBackpressure") and
            "Interlocked.Exchange(ref outstandingBlocks, 0)" in
            extract_method(blocks, "RecoverPreloadAfterBackpressure"),
            "recovery atomically invalidates old credit leases")

# FULL fail-safe and standard recovery.
start = extract_method(builder, "StartBoost")
stop = extract_method(builder, "StopBoost")
health = builder[builder.index("bool MonitorFullBoostHealth(float now)"):builder.index("string ResolveGpuPreloadStage", builder.index("bool MonitorFullBoostHealth(float now)"))]
suite.check("boostRequiredDropBaseline" in start and
            "boostLastProgressRealtime" in start,
            "FULL start records drop and progress baselines")
suite.check("SCHEDULER_REQUIRED_DROP" in health and "PIPELINE_STALL" in health,
            "FULL health monitor covers result loss and stalled progress")
suite.check("now - boostLastProgressRealtime >= 6f" in health,
            "stall fail-safe uses a bounded six-second window")
suite.check("RecoverPreloadAfterBackpressure" in stop and
            "ApplyStandardSchedulerState(true)" in stop,
            "fail-safe recovery returns directly to STANDARD")
suite.check("ResetScanState(recoveryPlan)" in stop and
            "recoveryPlan.Generation++" in stop,
            "recovered body cursors are restarted so missing tiles are revisited")
suite.check("[PRELOAD_BOOST_FAILSAFE]" in builder and
            "databasePayload=UNCHANGED" in builder,
            "fail-safe is explicit and preserves durable payload")
suite.check("forcedBottleneckUntil" in builder and
            "RESULT BACKPRESSURE" in builder and "PIPELINE STALL" in builder,
            "failure diagnosis remains visible in UI/logs")

# End-to-end telemetry/UI.
for token in (
    "PipelineOutstandingBlocks", "PipelineOutstandingBlockLimit",
    "PipelineAdmissionBackpressure", "PipelineRecoveryCount",
    "SchedulerResultDepth", "SchedulerRequiredResultDepth",
    "SchedulerRequiredRejected", "SchedulerRequiredDropped", "BoostAutoStops",
):
    suite.check(token in contracts and token in builder and token in window,
                "backpressure telemetry is end-to-end: " + token)
suite.check("BACKPRESSURE  outstanding" in window and "required-drop" in window,
            "Preload UI exposes required queue safety")
suite.check("RECOVERY  admissions" in window and "auto-stop" in window,
            "Preload UI exposes recovery history")
suite.check("outstanding=" in builder and "resultRequired=" in builder and
            "requiredDropped=" in builder,
            "throughput logs expose backpressure internals")

# Identity/docs and frozen boundaries.
suite.check("FULL BOOST BACKPRESSURE HOTFIX 1" in build_version and
            "FULL BOOST BACKPRESSURE HOTFIX 1" in build,
            "generated and build identities name the hotfix")
suite.check("Full Boost Backpressure Hotfix 1" in version,
            "KSP version metadata names the hotfix")
suite.check("selftest_v01800_cp25_candidate4_full_boost_backpressure_hotfix1.py" in runner,
            "CP2.5 runner includes the hotfix test")
suite.check("requiredDropped=0" in contract and "outstanding<=192" in contract,
            "acceptance contract fixes runtime safety thresholds")
suite.check("STANDARD outstanding block上限：96" in spec and
            "FULL outstanding block上限：192" in spec,
            "Japanese specification documents global credits")
suite.check("再起動回数" in test_card and "KSP起動は1回だけ" in test_card,
            "runtime card minimizes restart count")

clean = strip_csharp_comments_and_literals("\n".join((scheduler, blocks, builder, contracts, window)))
for forbidden in (
    "FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
    "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache",
    "ComputeShader.Dispatch", "AsyncGPUReadback.Request",
):
    suite.check(forbidden not in clean,
                "hotfix adds no forbidden authority/content: " + forbidden)

suite.finish()
