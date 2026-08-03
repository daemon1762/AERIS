#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, extract_method

suite = CheckSuite("v0.18.0.0 CP2.5 Candidate 4 Full Boost Downstream Commit Hotfix 1")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
contracts = read(SOURCE / "Terrain/AERISTerrainPreloadContracts.cs")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_CANDIDATE4_FULL_BOOST_DOWNSTREAM_COMMIT_HOTFIX1.txt")
spec = read(ROOT / "Docs/CP25_CANDIDATE4_FULL_BOOST_DOWNSTREAM_COMMIT_HOTFIX_1_v0.18.0.0_ja.md")
card = read(ROOT / "Docs/ND_CP25_CANDIDATE4_FULL_BOOST_DOWNSTREAM_COMMIT_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md")

for path in (
    SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs",
    SOURCE / "Terrain/AERISTerrainPreloadContracts.cs",
    SOURCE / "Performance/AERISWorkerScheduler.cs",
    SOURCE / "UI/AERISWindow.cs",
):
    suite.check(path.exists(), str(path.relative_to(ROOT)) + " exists")

encode = extract_method(builder, "ScheduleEncode")
write = extract_method(builder, "ScheduleChunkSuperBatch")
stop = extract_method(builder, "StopBoost")
limit = extract_method(builder, "ResolveEncodeCommitLimit")

suite.check("runtime.Scheduler.SubmitRequired(" in encode,
            "CPU encode uses the non-evictable commit-required scheduler path")
suite.check("runtime.Scheduler.SubmitLatest(" not in encode,
            "CPU encode no longer uses the best-effort path")
suite.check("runtime.Scheduler.SubmitRequired(" in write,
            "SSD super-batch uses the non-evictable commit-required scheduler path")
suite.check("runtime.Scheduler.SubmitLatest(" not in write,
            "SSD super-batch no longer uses the best-effort path")
suite.check("Commit(null)" in scheduler and "CommitRequired" in scheduler,
            "required worker failure still returns a terminal owner callback")
suite.check("removedNode.Value.Job.CommitRequired" in scheduler and
            "else if (!job.CommitRequired)" in scheduler,
            "required completions cannot be evicted by result pressure")

suite.check("StandardEncodeCommitFloor = 16" in builder and
            "StandardEncodeCommitCeiling = 32" in builder,
            "STANDARD encode admission is bounded to 16-32")
suite.check("FullEncodeCommitFloor = 32" in builder and
            "FullEncodeCommitCeiling = 56" in builder,
            "FULL encode admission is bounded to 32-56")
suite.check("workers * 4" in builder and "workers * 2" in builder,
            "encode capacity still scales with available workers")
suite.check("Math.Min(FullEncodeCommitCeiling, scaled)" in builder and
            "Math.Min(StandardEncodeCommitCeiling, scaled)" in builder,
            "scaled encode work remains below the fixed commit-result budget")
suite.check("FullBoostOutstandingBlocks = 192" in read(SOURCE / "Terrain/AERISTerrainBlockPipeline.cs") and
            "FullEncodeCommitCeiling = 56" in builder and "return boostActive ? 2 : 1" in builder,
            "FULL block plus encode plus write commitments remain at or below 250")
suite.check("ResultCapacity = 256" in scheduler,
            "scheduler nominal result capacity remains 256")

suite.check("work.Attempts = Math.Max(0, work.Attempts - 1);" in encode,
            "encode admission rejection does not consume an execution retry")
suite.check("encodeAdmissionBackpressure++" in encode,
            "encode backpressure is counted explicitly")
suite.check("writeAdmissionBackpressure++" in write,
            "write backpressure is counted explicitly")
suite.check("RequeueEncode(work);" in encode,
            "rejected encode remains owned and retryable")
suite.check("RequeueChunkBatches(payload.SourceBatches);" in write,
            "rejected SSD batch remains owned and retryable")
suite.check("ReleaseWriteBatch(payload);" in write,
            "SSD completion always retires the active write ownership")
suite.check("encoded != null && IsEncodeCurrent(work)" in encode and
            "else RequeueEncode(work);" in encode,
            "null/stale encode completion returns to the retry path")

suite.check("ApplyBoostSchedulerState(false);" in stop and
            "ApplyStandardSchedulerState(true);" in stop,
            "manual STOP returns scheduler policy to STANDARD")
suite.check("blockPipeline.SetPreloadThroughput(true, false);" in stop,
            "manual STOP restores STANDARD producer limits")
suite.check("ResolveEncodeCommitLimit(workers)" in builder,
            "post-STOP scheduling observes the lower STANDARD encode cap")
suite.check("maximumWriteJobs = ResolveWriteCommitLimit();" in builder,
            "post-STOP scheduling observes the lower STANDARD write cap")

for token in (
    "EncodeCommitLimit", "EncodeAdmissionBackpressure",
    "WriteCommitLimit", "WriteAdmissionBackpressure",
):
    suite.check(token in contracts and token in builder and token in window,
                "downstream telemetry is end-to-end: " + token)
suite.check("DOWNSTREAM REQUIRED" in window,
            "Preload UI labels downstream work as commit-required")
suite.check("STOP drains admitted FULL work under STANDARD limits" in window,
            "Preload UI states the STOP drain contract")
suite.check("encodeRequired=" in builder and "ssdRequired=" in builder and
            "encodeReject=" in builder and "writeReject=" in builder,
            "throughput logs expose downstream ownership and admission pressure")

suite.check("FULL BOOST DOWNSTREAM COMMIT HOTFIX 1" in build_version and
            "FULL BOOST DOWNSTREAM COMMIT HOTFIX 1" in build,
            "generated and build identities name the hotfix")
suite.check("Full Boost Downstream Commit Hotfix 1" in version,
            "KSP version metadata names the hotfix")
suite.check("selftest_v01800_cp25_candidate4_full_boost_downstream_commit_hotfix1.py" in runner,
            "CP2.5 runner executes the downstream hotfix first")
suite.check("SubmitRequired" in contract and "required-drop = 0" in contract,
            "acceptance contract fixes the non-evictable downstream boundary")
suite.check("CPU Encode" in spec and "SSD" in spec and "250" in spec,
            "Japanese specification documents the bounded downstream budget")
suite.check("再起動" in card and "1回" in card and "STOP" in card,
            "runtime card keeps restart count to one and tests STOP recovery")

for forbidden in (
    "FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
    "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache",
    "ComputeShader.Dispatch", "AsyncGPUReadback.Request",
):
    suite.check(forbidden not in builder + contracts + window,
                "hotfix adds no forbidden authority/content: " + forbidden)

suite.finish()
