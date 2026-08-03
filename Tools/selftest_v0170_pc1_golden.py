#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import SOURCE, CheckSuite, read, sha256, strip_csharp_comments_and_literals

suite = CheckSuite("v0.17.0.0 PC1, display and safety invariants")
suite.equal(sha256(SOURCE / "Autopilot" / "AERISBankDirector.cs"),
            "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7",
            "BANK maneuver implementation remains byte-identical")

registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
for token in ("RequestStartupLoad", "RequestManualReload", "MANUAL RELOAD COALESCED",
              "AERISAirfieldReloadState.Staged", "if (!approachFrozen) CommitStaged()",
              "MarkCachedDirectionsForRevalidation", "RevokeForRevalidation",
              "SelectionRevision", "DatabaseRevision"):
    suite.check(token in registry, "PC1 registry invariant: " + token)

landing = strip_csharp_comments_and_literals(read(
    SOURCE / "Landing" / "AERISLandingFoundation.cs"))
for token in ("frozenAirfield", "frozenDirection", "frozenDatabaseRevision",
              "frozenGeometryRevision", "HasCertifiedGeometry", "CreateTrackToken"):
    suite.check(token in landing, "LAND deep-freeze invariant: " + token)
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "ApplyAaNative", ".SetArmed("):
    suite.check(forbidden not in landing, "LAND control-free invariant: " + forbidden)

bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
suite.equal(bootstrap.count("Airfields.RequestStartupLoad()"), 1,
            "startup requests exactly one automatic airfield load")
suite.check("Airfields.Tick(Landing!=null&&Landing.Armed" in bootstrap,
            "LAND approach freeze is supplied to the registry")

nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
for token in ("if (!landActive)", "DrawAirfieldSymbols", "DrawLandingPlan",
              "DrawLandingProfile", "new Color(0.88f, 1f, 1f", "1.8f", "1.0f"):
    suite.check(token in nd, "accepted ND/LAND display invariant: " + token)

instrument = read(SOURCE / "Performance" / "AERISInstrumentPipeline.cs")
canonical = read(SOURCE / "Performance" / "AERISCanonicalSnapshot.cs")
suite.check("AERISRuntimeLane.GeneralCompute" in instrument,
            "display preprocessing cannot consume the Safety/LAND reserved lane")
for token in ("LateralMode", "VerticalMode", "SpeedMode", "ProtectState", "LandState",
              "LocalizerDeviationMeters", "GlidePathDeviationMeters"):
    suite.check(token in canonical and token in instrument,
                "AERIS/AIFD canonical field is shared: " + token)

gpu = read(SOURCE / "Performance" / "AERISGpuAssistBackend.cs")
suite.check("computeShader != null" in gpu and "CPU FALLBACK" in gpu,
            "GPU unavailable path retains CPU display preparation")

all_source = "\n".join(read(path) for path in SOURCE.rglob("*.cs"))
for forbidden in ("AERISNavDirector", "AERISRouteSpeedPlanner",
                  "AERISTrajectoryPrimitives", "TryStartNavLanding", "NAV_LANDING"):
    suite.check(forbidden not in all_source, "legacy NAV remains removed: " + forbidden)
suite.finish()
