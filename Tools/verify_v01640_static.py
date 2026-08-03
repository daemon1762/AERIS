#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01640_testlib import (ROOT, SOURCE, CheckSuite, all_text, csharp_balance,
    compile_includes, enum_members, package_files, parse_version, read, sha256,
    source_files, strip_csharp_comments_and_literals)

suite = CheckSuite("v0.16.4.0 consensus runway certification static verification")
version_txt, version_json, version_cs = parse_version()
suite.equal(version_txt, "0.16.4.0", "VERSION is v0.16.4.0")
suite.equal(version_json, version_txt, "KSP version JSON matches VERSION")
suite.equal(version_cs, version_txt, "generated assembly version matches VERSION")

csproj = read(SOURCE / "AERISFlightControl.csproj")
includes = compile_includes(csproj)
missing = [p for p in includes if not (SOURCE / Path(p.replace('\\', '/'))).is_file()]
suite.check(not missing, "every csproj Compile item exists", ", ".join(missing))
required = (
    "Landing\\AERISRunwaySurveyContracts.cs",
    "Landing\\AERISRunwaySurveyPlanner.cs",
    "Landing\\AERISRunwaySnapshotBuilder.cs",
    "Landing\\AERISRunwayGeometryWorker.cs",
    "Landing\\AERISRunwayCertificationWorker.cs",
    "Landing\\AERISRunwayCertificationCache.cs",
    "Landing\\AERISOperationalRunwayResolver.cs",
    "Landing\\AERISRunwayTrackToken.cs",
)
for value in required:
    suite.check(value in includes, f"precision runway source is compiled: {value}")
suite.check("Landing\\AERISModRunwaySurveyResolver.cs" not in includes,
            "obsolete fixed/manual resolver is not compiled")

models = read(SOURCE / "Landing" / "AERISAirfieldModels.cs")
methods = enum_members(models, "AERISRunwayMeasurementMethod")
suite.equal(len([m for m in methods if m != "None"]), 26,
            "all 26 measurement methods are represented")
for number in range(1, 27):
    suite.check(any(m.startswith(f"M{number:02d}") for m in methods),
                f"measurement method M{number:02d} exists")
families = enum_members(models, "AERISRunwayEvidenceFamily")
for family in ("MetadataSemantic", "GeometryTopology", "AviationMarkingLighting",
               "OperationalLayout", "SurfaceElevationTerrain"):
    suite.check(family in families, f"evidence family exists: {family}")
failures = enum_members(models, "AERISRunwayFailureCode")
for failure in ("MultipleGeometrySolutions", "WholeSiteBoundsOnly",
                "DisplacedThresholdUnresolved", "ApproachTerrainBlocked",
                "MeshFingerprintChanged", "SurveyTimeout", "WorkerFailure"):
    suite.check(failure in failures, f"typed failure exists: {failure}")

contracts_path = SOURCE / "Landing" / "AERISRunwaySurveyContracts.cs"
contracts = strip_csharp_comments_and_literals(read(contracts_path))
for forbidden in ("UnityEngine", "GameObject", "Transform", "Mesh", "Collider",
                  "CelestialBody", "FlightGlobals", "FlightCtrlState"):
    suite.check(re.search(r"\b" + re.escape(forbidden) + r"\b", contracts) is None,
                f"worker DTO is free of Unity/KSP object type: {forbidden}")
suite.check("CurrentAlgorithmVersion = 1640" in contracts,
            "survey algorithm revision is explicit")

worker_path = SOURCE / "Landing" / "AERISRunwayGeometryWorker.cs"
worker = strip_csharp_comments_and_literals(read(worker_path))
suite.check("UnityEngine" not in worker and "FlightGlobals" not in worker,
            "numeric runway worker has no Unity/KSP dependency")
for token in ("ClassificationConfidence >= 0.90", "GeometryConfidence >= 0.85",
              "families >= 3", "GeometryTopology", "IntervalCoverage",
              "continuity < 0.65", "flatness > 8.0", "MarkerOnly",
              "BlastPad", "Stopway", "WholeSiteBoundsOnly"):
    suite.check(token in read(worker_path), f"geometry safety gate exists: {token}")
suite.check("declaredWidth * 0.55" in read(worker_path),
            "parallel-runway band gate uses a narrow seam allowance")

builder_path = SOURCE / "Landing" / "AERISRunwaySnapshotBuilder.cs"
builder = read(builder_path)
for token in ("TargetSliceMilliseconds = 0.50", "HardSliceMilliseconds = 1.50",
              "MaximumPoints = 32768", "MaximumPrimitives = 4096",
              "BuildFingerprint", "SHA256.Create", "sharedMesh", "sharedMaterials"):
    suite.check(token in builder, f"bounded main-thread snapshot contract: {token}")
suite.check("long rectangle from metadata" in builder and
            "Provider length/width/heading are priors" in builder,
            "provider metadata cannot manufacture runway geometry")

cert_worker = read(SOURCE / "Landing" / "AERISRunwayCertificationWorker.cs")
for token in ("pending = job", "Interlocked.Increment(ref replacedCount)", "IsBackground = true",
              "ThreadPriority.Lowest", "Invalidate", "stale"):
    suite.check(token in cert_worker, f"bounded latest-only worker rule: {token}")

cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
for token in ("CurrentSchemaVersion = 3", 'path + ".tmp"', 'path + ".bak"',
              "File.Replace", "TryLoadFile", "FailureRecord", "InputFingerprint",
              "Parameter", "PhysicalStart", "UsableStart", "TouchdownAim"):
    suite.check(token in cache, f"certification cache contract: {token}")

registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
for token in ("RequestStartupLoad", "RequestManualReload", "MANUAL RELOAD COALESCED",
              "AERISAirfieldReloadState.Staged", "if (!approachFrozen) CommitStaged()",
              "ElapsedSeconds(activeCaptureStartedTimestamp) > 60.0",
              "ElapsedSeconds(activeSurveyStartedTimestamp) > 30.0",
              "MarkCachedDirectionsForRevalidation", "RevokeForRevalidation",
              "SelectableDirectionCount", "EffectiveState"):
    suite.check(token in registry, f"atomic reload/certification rule: {token}")

landing = read(SOURCE / "Landing" / "AERISLandingFoundation.cs")
for token in ("HasCertifiedGeometry", "ActiveDirection", "frozenDirection",
              "frozenDatabaseRevision", "frozenGeometryRevision",
              "CreateTrackToken", "registry.IsDirectionRevoked"):
    suite.check(token in landing, f"LAND certification/freeze rule: {token}")
landing_code = strip_csharp_comments_and_literals(landing)
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "ApplyAaNative", ".SetArmed("):
    suite.check(forbidden not in landing_code,
                f"LAND observation layer has no control write: {forbidden}")

token_api = strip_csharp_comments_and_literals(read(
    SOURCE / "Landing" / "AERISRunwayTrackToken.cs"))
for token in ("AERISRunwayTrackToken", "OperationalThreshold", "RolloutEnd",
              "GeometryFingerprint", "DatabaseGeneration", "TryGet"):
    suite.check(token in token_api, f"read-only Ground Assist token field: {token}")
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelSteer", "SetArmed"):
    suite.check(forbidden not in token_api, f"track token is control-free: {forbidden}")

window = read(SOURCE / "UI" / "AERISWindow.cs")
for token in ('string[] labels={"STATUS","OPTIONS","AIRFIELDS","DIAGNOSTICS"}',
              '"↻ RELOAD / RESCAN"', '"CERTIFIED"', '"FAILED"', '"PENDING"',
              '"REVALIDATION"', "SelectableDirectionCount", "EffectiveState(direction)"):
    suite.check(token in window, f"AIRFIELDS/LAND UI contract: {token}")
nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
suite.check("if (!landActive)" in nd and "DrawAirfieldSymbols" in nd,
            "LAND display suppresses non-selected facility symbols")
for token in ("1.8f", "1.0f", "new Color(0.88f, 1f, 1f"):
    suite.check(token in nd, f"thin cyan/white ownship symbol contract: {token}")

bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
suite.equal(bootstrap.count("Airfields.RequestStartupLoad()"), 1,
            "startup requests exactly one automatic airfield load")
suite.check("Airfields.Tick(Landing!=null&&Landing.Armed)" in bootstrap,
            "registry receives LAND approach-freeze state every update")

bank = SOURCE / "Autopilot" / "AERISBankDirector.cs"
suite.equal(sha256(bank),
            "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7",
            "image-reference BANK roll-in/pre-brake/zero-rate law is unchanged")

joined = all_text(source_files(".cs"))
for forbidden in ("AERISNavDirector", "AERISRouteSpeedPlanner",
                  "AERISTrajectoryPrimitives", "TryStartNavLanding", "NAV_LANDING"):
    suite.check(forbidden not in joined, f"legacy NAV remains absent: {forbidden}")

balance_failures = []
for path in source_files(".cs"):
    ok, detail = csharp_balance(path)
    if not ok:
        balance_failures.append(f"{path.relative_to(ROOT)}: {detail}")
suite.check(not balance_failures, "all C# files have balanced lexical structure",
            "; ".join(balance_failures[:5]))
bad = [str(p.relative_to(ROOT)) for p in package_files()
       if p.suffix.lower() in (".dll", ".pdb", ".pyc") or "__pycache__" in p.parts]
suite.check(not bad, "source package contains no compiled/cache artifact", ", ".join(bad[:10]))
suite.finish()
