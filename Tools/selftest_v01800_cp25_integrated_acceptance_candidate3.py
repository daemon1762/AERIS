#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import (ROOT, SOURCE, CheckSuite, read, extract_method,
                            strip_csharp_comments_and_literals, text_sha256)

suite = CheckSuite("v0.18.0.0 CP2.5 Integrated Acceptance Candidate 3")
settings = read(SOURCE / "Settings/AERISSettings.cs")
scheduler = read(SOURCE / "Performance/AERISWorkerScheduler.cs")
contracts = read(SOURCE / "Terrain/AERISTerrainPreloadContracts.cs")
builder = read(SOURCE / "Terrain/AERISTerrainPreloadBuilder.cs")
tiles = read(SOURCE / "Terrain/AERISTerrainTileSystem.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
fdi = read(SOURCE / "UI/AERISFlightInstrument.cs")
bootstrap = read(SOURCE / "Core/AERISBootstrap.cs")
sync = read(SOURCE / "AA/SyncModuleControlSurface.cs")
default_cfg = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_INTEGRATED_ACCEPTANCE_CANDIDATE3.txt")
spec = read(ROOT / "Docs/CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_3_v0.18.0.0_ja.md")
test_card = read(ROOT / "Docs/ND_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_3_TEST_CARD_v0.18.0.0_ja.md")
evidence = read(ROOT / "Evidence/RUNTIME_OBSERVATION_CP25_CANDIDATE2_AERIS33_2026-07-30.txt")

# Non-Flight preload surface.
for token in (
    "internal void DrawPreloadOnly()", "void PreloadOnlyContent()",
    'UiBuildTitle("PRELOAD TERRAIN CONTROL")', "DrawPreloadTerrainMapsPage();",
    "Main Menu / Space Center / VAB / SPH control surface",
):
    suite.check(token in window, "non-Flight Preload UI contract: " + token)
suite.check(('GUI.Button(launcher,"AERIS PRELOAD")' in window) or
            ('if(HighLogic.LoadedSceneIsFlight||!PreloadStatusVisible)return;' in window and
             'AERIS PRELOAD control is opened only through the existing AERIS' in window),
            "non-Flight Preload opens through legacy launcher or superseding toolbar-only hotfix")
suite.check("window.DrawPreloadOnly()" in bootstrap and
            "Terrain.Tick(FlightGlobals.ActiveVessel,Landing,Airfields);" in bootstrap,
            "persistent bootstrap ticks and draws Preload outside Flight")
preload_ui_start = window.index("void PreloadOnlyContent()")
preload_ui_end = window.index("void DrawPreloadTerrainMapsPage()", preload_ui_start)
preload_ui = strip_csharp_comments_and_literals(extract_method(window, "PreloadOnlyContent"))
for forbidden in ("FlightInputHandler.state", "MainThrottle =", ".SetArmed("):
    suite.check(forbidden not in preload_ui,
                "non-Flight Preload surface has no control authority: " + forbidden)

# Manual boost entry/exit and telemetry.
start_boost = extract_method(builder, "StartBoost")
stop_boost = extract_method(builder, "StopBoost")
tick = extract_method(builder, "Tick")
resolve_budget = extract_method(builder, "ResolveBudget")
suite.check("HighLogic.LoadedSceneIsFlight" in start_boost and
            "PRELOAD BOOST REFUSED / NON-FLIGHT ONLY" in start_boost,
            "boost cannot be started in Flight")
suite.check("boostActive = true;" in start_boost and
            "trigger=MANUAL" in start_boost and "persistence=NONE" in start_boost,
            "boost starts only from an explicit non-persistent action")
suite.check("boostActive = false;" in stop_boost and
            "ApplyBoostSchedulerState(false)" in stop_boost,
            "manual stop returns the scheduler to normal control")
suite.check('if (isFlight && boostActive) StopBoost("FLIGHT_SAFETY");' in tick and
            tick.index('StopBoost("FLIGHT_SAFETY")') < tick.index("ScheduleReadyChunkBatches"),
            "Flight safety revokes boost before write scheduling")
suite.check("StartPreloadBoost" in tiles and "StopPreloadBoost" in tiles and
            "START PRELOAD BOOST" in window and "STOP PRELOAD BOOST" in window,
            "manual start/stop is exposed through the terrain owner and UI")
for token in ("BoostActive", "BoostWorkers", "BoostPermits", "BoostQueueTarget",
              "BoostMainThreadBudgetMilliseconds"):
    suite.check(token in contracts and token in builder and token in window,
                "boost telemetry is end-to-end: " + token)

# All existing AERIS worker permits, without adding a private pool.
suite.check("activePermits = workerCeiling;" in scheduler and
            "safetyReservedPermits = 0;" in scheduler and
            "archivePaused = false;" in scheduler,
            "boost grants all existing AERIS worker permits")
suite.check("ThreadPriority.Normal" in scheduler and
            "ThreadPriority.BelowNormal" in scheduler,
            "worker priority is raised only for boost and restored afterwards")
suite.check("Math.Min(48, Math.Max(8, workers * 4))" in tick and
            "while (attempts-- > 0" in tick and "workers * 16" in tick,
            "boost fills a bounded worker-scaled compute queue")
suite.check("Math.Max(2, Math.Min(32, ResolveBoostWorkerCount()))" in builder,
            "boost expands chunk-write concurrency within a fixed cap")
suite.check("Mathf.Clamp(frameMilliseconds * 0.70f, 8f, 24f)" in builder and
            "Math.Min(4096" in builder and "qps = 100000f" in builder,
            "boost uses a bounded 8-24 ms PQS budget and bounded samples")
for forbidden in ("new Thread(", "ThreadPool.", "Task.Run(",
                  "AERISRuntimeLane.SafetyLand"):
    suite.check(forbidden not in strip_csharp_comments_and_literals(builder),
                "boost creates no private/safety worker path: " + forbidden)
# Persistence must not exist in settings/config/state serialization.
persistence_surface = settings + default_cfg
for forbidden in ("terrainPreloadBoost", "preloadBoostEnabled",
                  "manualPreloadBoost ="):
    suite.check(forbidden not in persistence_surface,
                "boost has no persistent startup key: " + forbidden)

# Demand-driven display model and migration.
suite.check("Off = 2" in settings and
            "NavigationDisplayMode = AERISDisplayMode.Automatic" in settings and
            "FlightInstrumentDisplayMode = AERISDisplayMode.Automatic" in settings,
            "ND and FDI default to AUTO and expose absolute OFF")
suite.check("CurrentDisplayPolicyRevision = 1" in settings and
            'ReadInt(node, "displayPolicyRevision", 0)' in settings and
            "[DISPLAY_POLICY_MIGRATION]" in settings and
            'node.AddValue("displayPolicyRevision", CurrentDisplayPolicyRevision)' in settings,
            "old ALWAYS default is migrated once and revision-persisted")
suite.check("displayPolicyRevision = 1" in default_cfg and
            "navigationDisplayMode = Automatic" in default_cfg and
            "flightInstrumentDisplayMode = Automatic" in default_cfg,
            "factory config ships demand-driven displays")
suite.check('new string[]{"AUTO","ALWAYS","OFF"}' in window,
            "OPTIONS exposes independent AUTO / ALWAYS / OFF selectors")

# Absolute FDI OFF and speed-only display.
draw = extract_method(fdi, "Draw")
suite.check("bool fdiOff = settings.FlightInstrumentDisplayMode == AERISDisplayMode.Off;" in draw and
            "if (!fdiOff)" in draw and "extensionMessages.Clear();" in draw and
            "detailLines.Clear();" in draw,
            "FDI OFF suppresses provider/detail collection")
suite.check("bool lateralVisible = !fdiOff" in draw and
            "bool verticalVisible = !fdiOff" in draw and
            "bool speedVisible = !fdiOff" in draw and
            "bool fdiVisible = !fdiOff" in draw,
            "FDI OFF suppresses panel and all FDI gauges")
suite.check("bool speedOnlyVisible" in draw and
            '"FDI — SPEED GUIDANCE"' in fdi and
            '"AP SPEED — " + speedMode + " ONLY"' in fdi,
            "SPEED-only AP receives an explicitly labelled FDI")
suite.check('core.Velocity != null && core.Velocity.Armed' in fdi and
            'core.Acceleration != null && core.Acceleration.Armed' in fdi,
            "speed-only FDI distinguishes VEL and ACC")

# Absolute ND OFF releases ND-owned display work while preserving terrain owner.
suite.check("bool ndOff = settings.NavigationDisplayMode == AERISDisplayMode.Off;" in draw and
            "bool ndDisplayViewportActive = terrainViewportActive && !ndOff;" in draw and
            "navigationDisplay.SetFlightViewportActive(ndDisplayViewportActive);" in draw,
            "ND OFF revokes ND display-owned processing")
suite.check("core.Terrain.FlightViewportActive" in draw and
            "core.Terrain" not in preload_ui,
            "ND display policy does not replace the central terrain safety policy")

# Identity, evidence, inherited baseline and frozen boundaries.
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 3" in build_version and
            "NON-FLIGHT PRELOAD CONTROL PRELOAD BOOST DISPLAY DEMAND POLICY HOTFIX 1" in build_version,
            "generated identity names Candidate 3")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 2" in build_version and
            "AA CONTROL SURFACE EVENT LIFECYCLE HOTFIX 1" in build_version,
            "Candidate 2 lifecycle identity remains in the chain")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 3" in build and
            "Integrated Acceptance Candidate 3" in version,
            "build entrypoint and KSP metadata identify Candidate 3")
suite.check("selftest_v01800_cp25_integrated_acceptance_candidate3.py" in runner,
            "full CP2.5 runner executes Candidate 3 first")
suite.check("PRELOAD BOOST" in contract and "DISPLAY DEMAND POLICY" in contract and
            "SAFETY / FROZEN BOUNDARIES" in contract,
            "Candidate 3 acceptance contract fixes feature and safety boundaries")
suite.check("Main Menu" in spec and "8～24ms" in spec and "VEL ONLY" in spec,
            "Japanese specification documents non-Flight UI, boost budget and speed-only FDI")
suite.check("FLIGHT_SAFETY" in test_card and "AUTO / ALWAYS / OFF" in test_card and
            "3往復" in test_card,
            "runtime card covers boost safety, display policy and lifecycle regression")
suite.check("synchronousSSD=0" in evidence and "KSP.log" in evidence and
            "does not by itself close" in evidence,
            "AERIS33 evidence is bundled with its runtime limitation stated")
for rel in (
    "Evidence/STATIC_ACCEPTANCE_v0.18.0.0_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE3.tsv",
    "Evidence/STATIC_SCOPE_AUDIT_v0.18.0.0_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE3.log",
    "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE3.txt",
):
    suite.check((ROOT / rel).is_file(), "Candidate 3 evidence exists: " + rel)

# Candidate 2 control-surface source is still frozen.
ctrl = extract_method(sync, "CtrlSurfaceUpdate")
suite.check(text_sha256(ctrl) == "a8fe50749807dec9eadf51f605e5c0e87f14112cb60ace4c9cb4b89487a7484c",
            "Candidate 2 control-surface law remains byte-identical")
all_changed = "\n".join((settings, scheduler, contracts, builder, tiles, window, fdi))
clean = strip_csharp_comments_and_literals(all_changed)
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache",
                  "ctrlSurfaceRange =", "authorityLimiter ="):
    suite.check(forbidden not in clean,
                "Candidate 3 adds no forbidden authority/content: " + forbidden)

suite.finish()
