#!/usr/bin/env python3
import re
import sys
from pathlib import Path
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP3 Gate 1 Scheduler State Compile Hotfix 1")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
clean = strip_csharp_comments_and_literals(builder)
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
runner = read(ROOT / "Tools/run_v01800_cp3_gate1_acceptance.py")
build = read(ROOT / "build_ubuntu.sh")
generated = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")

signature = "void ApplyStandardSchedulerState(bool active)"
suite.check(clean.count(signature) == 1,
            "missing STANDARD scheduler bridge has exactly one definition")
suite.check(clean.count("ApplyStandardSchedulerState(") == 4,
            "three call sites plus one definition are present")

start = clean.find(signature)
end = clean.find("AERISRuntimeTelemetrySnapshot SnapshotScheduler()", start)
method = clean[start:end] if start >= 0 and end > start else ""
suite.check("if (standardSchedulerApplied == active) return;" in method,
            "bridge avoids redundant scheduler policy writes")
suite.check("AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;" in method,
            "bridge resolves the active performance runtime")
suite.check("runtime == null || runtime.Scheduler == null" in method,
            "bridge fails safely while runtime is unavailable")
suite.check("runtime.Scheduler.SetStandardPreloadThroughput(active);" in method,
            "bridge routes to the existing STANDARD scheduler API")
suite.check(method.find("SetStandardPreloadThroughput(active);") <
            method.find("standardSchedulerApplied = active;"),
            "applied state is committed only after scheduler update")
suite.check("internal void SetStandardPreloadThroughput(bool active)" in scheduler,
            "target scheduler API remains present")

suite.check("ApplyStandardSchedulerState(false);" in clean and
            "ApplyStandardSchedulerState(true);" in clean,
            "Flight suspension, non-Flight STANDARD, and Dispose call sites compile")
suite.check("CURRENT BODY RESIDENT CACHE CONTRACTS COMPILE HOTFIX 1" in build and
            "CURRENT BODY RESIDENT CACHE CONTRACTS COMPILE HOTFIX 1" in generated,
            "build identities name Compile Hotfix 1")
suite.check("Current Body Resident Cache Contracts Compile Hotfix 1" in version,
            "AVC identity names Compile Hotfix 1")
suite.check("selftest_v01800_cp3_gate1_scheduler_state_compile_hotfix1.py" in runner,
            "active Gate 1 runner executes the compile regression")

for forbidden in ("StartPreloadBoost", "StopPreloadBoost", "ApplyBoostSchedulerState",
                  "File.Read", "MainThrottle =", "FlightInputHandler.state"):
    suite.check(forbidden not in method,
                "compile bridge adds no forbidden behavior: " + forbidden)

suite.finish()
