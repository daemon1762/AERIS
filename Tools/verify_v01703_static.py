#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import (ROOT, SOURCE, CheckSuite, all_text, csharp_balance,
    compile_includes, package_files, parse_version, read, sha256, source_files,
    strip_csharp_comments_and_literals)

suite = CheckSuite("v0.17.0.3 runway-registry identity hotfix static verification")
version_txt, version_json, version_cs = parse_version()
suite.equal(version_txt, "0.17.0.3", "VERSION is v0.17.0.3")
suite.equal(version_json, version_txt, "KSP version JSON matches VERSION")
suite.equal(version_cs, version_txt, "generated assembly version matches VERSION")

csproj = read(SOURCE / "AERISFlightControl.csproj")
includes = compile_includes(csproj)
missing = [name for name in includes if not (SOURCE / Path(name.replace('\\', '/'))).is_file()]
suite.check(not missing, "every csproj Compile item exists", ", ".join(missing))
performance_files = (
    "Performance\\AERISRuntimeGeneration.cs",
    "Performance\\AERISWorkerScheduler.cs",
    "Performance\\AERISCanonicalSnapshot.cs",
    "Performance\\AERISGpuAssistBackend.cs",
    "Performance\\AERISPerformanceRuntime.cs",
    "Performance\\AERISBackgroundFileWriter.cs",
    "Performance\\AERISInstrumentPipeline.cs",
)
for name in performance_files:
    suite.check(name in includes, "Performance Runtime source is compiled: " + name)

generation = read(SOURCE / "Performance" / "AERISRuntimeGeneration.cs")
for field in ("SceneGeneration", "VesselPersistentId", "VesselInstanceGeneration",
              "BodyId", "ControlPointRevision", "DockingSeparationRevision",
              "RunwayDatabaseRevision", "RunwaySelectionRevision",
              "FlightPlanRevision", "DisplayLayoutRevision", "Sequence",
              "MonotonicTimestamp"):
    suite.check(" " + field + ";" in generation,
                "generation identity field exists: " + field)
for token in ("scene == value.SceneGeneration", "vesselId == value.VesselPersistentId",
              "vesselInstance == value.VesselInstanceGeneration",
              "runwayDatabase == value.RunwayDatabaseRevision",
              "displayLayout == value.DisplayLayoutRevision"):
    suite.check(token in generation, "commit generation match gate: " + token)

scheduler = read(SOURCE / "Performance" / "AERISWorkerScheduler.cs")
for token in ("Math.Ceiling(LogicalProcessors * 0.15)",
              "Math.Max(2, LogicalProcessors - ReservedProcessors)",
              "configuredWorkers > 0 ? Math.Max(2, configuredWorkers)",
              "latestJobIds", "IsLatestLocked", "GenerationBound",
              "Capacity = 64", "Capacity = 128", "results.Count >= 256",
              "SafetyReservedPermits", "ArchivePaused", "STABLE RECOVERY +1",
              "WRITER BACKLOG BACKOFF", "GPU FRAME BACKOFF"):
    suite.check(token in scheduler, "scheduler contract: " + token)
suite.check("volatile int activePermits" in scheduler and
            "volatile int safetyReservedPermits" in scheduler and
            "volatile bool archivePaused" in scheduler,
            "adaptive permit policy is safely published to worker threads")
suite.check("Math.Min(12" not in scheduler and "workers = 12" not in scheduler,
            "worker count has no fixed 12-thread ceiling")
suite.check("Join(" not in scheduler and ".Wait(" not in scheduler and ".Result" not in scheduler,
            "scheduler never synchronously waits on the main thread")

worker_paths = (
    SOURCE / "Landing" / "AERISRunwayCertificationWorker.cs",
    SOURCE / "Terrain" / "AERISTerrainRasterWorker.cs",
    SOURCE / "Performance" / "AERISInstrumentPipeline.cs",
)
for path in worker_paths:
    code = strip_csharp_comments_and_literals(read(path))
    for forbidden in ("FlightGlobals", "FlightCtrlState", "GameObject", "Texture2D",
                      "CelestialBody", "UnityEngine.Object"):
        suite.check(re.search(r"\b" + re.escape(forbidden) + r"\b", code) is None,
                    path.name + " worker boundary excludes " + forbidden)

writer = read(SOURCE / "Performance" / "AERISBackgroundFileWriter.cs")
for token in ("QueueCapacity = 8192", "ReservedControlSlots = 64",
              "EvictLowerPriorityLocked", "FirstDroppedSequence",
              "MarkChannelUnsealableLocked", "SealSession", "stopWhenDrained",
              "accepting = false", "AERISCsvField", "EncodeP95Milliseconds"):
    suite.check(token in writer, "ordered writer contract: " + token)
suite.check("Never hold the producer" in writer and
            writer.index("if (failedWriter != null)") >
            writer.index("static void MarkFailure"),
            "failed stream disposal is isolated from the producer queue lock")
suite.check("Percentile copies/sorts are diagnostic work" in writer,
            "writer percentile sorting does not hold the producer queue lock")
suite.check("new StreamWriter" in writer, "disk stream ownership is centralized")
suite.check("public sealed class AERISAsyncFileChannel" in writer,
            "public AA protected fields have a consistently accessible channel type")

for relative in ("Logging/AERISLogger.cs", "Recording/AERISFlightDataRecorder.cs",
                 "AA/BackgroundThread.cs", "AA/Modules/AngularAccAdaptiveController.cs"):
    code = strip_csharp_comments_and_literals(read(SOURCE / relative))
    suite.check("new StreamWriter" not in code and "File.AppendAllText" not in code and
                "File.CreateText" not in code,
                relative + " performs no synchronous stream/file write")

archive = read(SOURCE / "Recording" / "AERISFlightDataArchive.cs")
for token in ("ArchiveCompression", "PendingCapacity = 64", "ResultCapacity = 256",
              "ArchiveIoSync", "context.ThrowIfStale()", "VerifyArchive",
              "entry/source content mismatch", "File.Move(temporaryPath, finalPath)",
              "TryDeleteSource", "raw data retained"):
    suite.check(token in archive, "archive integrity contract: " + token)
suite.check("ThreadPool" not in archive, "archive uses the shared bounded scheduler")

bootstrap = strip_csharp_comments_and_literals(read(SOURCE / "Core" / "AERISBootstrap.cs"))
for forbidden in (".Wait(", ".Result", ".Join("):
    suite.check(forbidden not in bootstrap, "KSP main path has no blocking call: " + forbidden)
for token in ("Performance.Tick", "SyncRuntimeRevisions", "PublishCanonicalSnapshot",
              "ResetAutomationState(\"shutdown\",true)",
              "AERISBackgroundFileWriter.ShutdownNoWait"):
    suite.check(token in read(SOURCE / "Core" / "AERISBootstrap.cs"),
                "bootstrap runtime integration: " + token)
bootstrap_source = read(SOURCE / "Core" / "AERISBootstrap.cs")
suite.check("settings=AERISSettings.Load(); Performance=new AERISPerformanceRuntime(" in
            bootstrap_source,
            "performance launch policy is loaded before runtime construction")
suite.check("settings.PerformanceWorkerOverride,!settings.PerformanceGpuAccelerationEnabled" in
            bootstrap_source,
            "persisted worker/GPU acceptance policy reaches runtime construction")

settings_code = read(SOURCE / "Settings" / "AERISSettings.cs")
settings_cfg = read(ROOT / "GameData" / "AERISFlightControl" / "Config" /
                    "AERISSettings.cfg")
for token in ("PerformanceWorkerOverride", "PerformanceGpuAccelerationEnabled",
              "NormalizeWorkerOverride", "performanceWorkerOverride",
              "performanceGpuAccelerationEnabled"):
    suite.check(token in settings_code, "performance launch setting contract: " + token)
suite.check("performanceWorkerOverride = 0" in settings_cfg and
            "performanceGpuAccelerationEnabled = True" in settings_cfg,
            "factory CFG uses AUTO workers and permits optional GPU assist")
window_code = read(SOURCE / "UI" / "AERISWindow.cs")
suite.check("2-WORKER TEST" in window_code and "RESTART REQUIRED" in window_code,
            "minimum-resource acceptance mode is user-selectable and restart-scoped")
suite.check("System.StringComparison.Ordinal" in window_code,
            "AIRFIELDS detail comparison resolves System.StringComparison explicitly")
suite.check('GUILayout.Label("RESULT "+registry.Status)' in window_code,
            "AIRFIELDS page exposes the full reload result")

provider_code = read(SOURCE / "Landing" / "AERISAirfieldProviders.cs")
registry_code = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
suite.check("record.ProviderSiteId = name" in provider_code and
            "record.ProviderGroup = name" in provider_code and
            'record.ProviderGroup = "KSP"' not in provider_code,
            "stock KSP facilities use independent stable identities")
suite.check("DiscoveredAirfieldIdentity(record)" in registry_code and
            "UsesProviderAirfieldGroup(record)" in registry_code,
            "registry separates provider-site identity from KK/SLE airport grouping")

unresolved_string_comparison = []
for path in source_files(".cs"):
    code = strip_csharp_comments_and_literals(read(path))
    uses_unqualified = re.search(r"(?<![\w.])StringComparison\s*\.", code) is not None
    imports_system = re.search(r"^\s*using\s+System\s*;", code, re.MULTILINE) is not None
    if uses_unqualified and not imports_system:
        unresolved_string_comparison.append(str(path.relative_to(ROOT)))
suite.check(not unresolved_string_comparison,
            "unqualified StringComparison uses import the System namespace",
            ", ".join(unresolved_string_comparison))

runtime = read(SOURCE / "Performance" / "AERISPerformanceRuntime.cs")
for token in ("frame_p95_ms", "aeris_main_p95_ms", "snapshot_capture_p95_ms",
              "commit_drain_p95_ms", "runway_worker_p95_ms",
              "terrain_worker_p95_ms", "instrument_worker_p95_ms",
              "writer_encode_p95_ms", "archive_deferred", "gc_positive_delta_ema_bytes"):
    suite.check(token in runtime, "required performance telemetry: " + token)
suite.check("commitWatch.Elapsed.TotalMilliseconds" in runtime,
            "adaptive main-thread cost includes asynchronous result commits")
suite.check("capturedThisFrame" in runtime and
            "lastPerformanceSnapshotCostMilliseconds=-1.0" in
            read(SOURCE / "Core" / "AERISBootstrap.cs"),
            "snapshot capture cost is sampled once instead of repeated on idle frames")
suite.check("get { return runtimeTelemetry; }" in runtime and
            "get { return writerTelemetry; }" in runtime and
            "core.Performance.WriterTelemetry" in
            read(SOURCE / "UI" / "AERISWindow.cs"),
            "diagnostics reuse 1 Hz telemetry snapshots without per-frame sorting")
suite.check("Volatile.Read(ref current)" in runtime and
            "Interlocked.CompareExchange(ref current, null, this)" in runtime,
            "runtime publication is cross-thread visible and old instances cannot clear new ones")

instrument = read(SOURCE / "Performance" / "AERISInstrumentPipeline.cs")
suite.check("IReadOnlyList<double> PitchLadderDegrees" in instrument and
            "Array.AsReadOnly(pitchDegrees)" in instrument,
            "prepared display geometry is externally immutable")

terrain_worker = read(SOURCE / "Terrain" / "AERISTerrainRasterWorker.cs")
for token in ("byteCountLong > 64L * 1024L * 1024L",
              "!Finite(request.AircraftAltitudeAslMeters)",
              "Finite(neighbourElevation)"):
    suite.check(token in terrain_worker,
                "terrain raster input/allocation guard: " + token)

gpu = read(SOURCE / "Performance" / "AERISGpuAssistBackend.cs")
for token in ("supportsComputeShaders", "CPU FALLBACK", "DisableAndFallback",
              "CPU EXACT VALIDATION"):
    suite.check(token in gpu, "GPU assist/fallback contract: " + token)

bank = SOURCE / "Autopilot" / "AERISBankDirector.cs"
suite.equal(sha256(bank),
            "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7",
            "reference BANK roll-in/pre-brake/zero-rate law is byte-identical")

landing = strip_csharp_comments_and_literals(read(
    SOURCE / "Landing" / "AERISLandingFoundation.cs"))
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "ApplyAaNative", ".SetArmed("):
    suite.check(forbidden not in landing,
                "LAND observation layer remains control-free: " + forbidden)

joined = all_text(source_files(".cs"))
for forbidden in ("AERISNavDirector", "AERISRouteSpeedPlanner",
                  "AERISTrajectoryPrimitives", "TryStartNavLanding", "NAV_LANDING"):
    suite.check(forbidden not in joined, "legacy NAV remains absent: " + forbidden)

balance_failures = []
for path in source_files(".cs"):
    ok, detail = csharp_balance(path)
    if not ok: balance_failures.append(str(path.relative_to(ROOT)) + ": " + detail)
suite.check(not balance_failures, "all C# files have balanced lexical structure",
            "; ".join(balance_failures[:5]))
bad = [str(path.relative_to(ROOT)) for path in package_files()
       if path.suffix.lower() in (".dll", ".pdb", ".pyc") or "__pycache__" in path.parts]
suite.check(not bad, "source package contains no compiled/cache artifact", ", ".join(bad[:10]))
suite.finish()
