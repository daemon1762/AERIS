#!/usr/bin/env python3
import re
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, extract_method, \
    extract_string_array_expressions, extract_typed_array_expressions

suite = CheckSuite("v0.18.0.0 CP3 Gate 4A ND core successor")
settings = read(ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
terrain_renderer = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs")
pipeline = read(ROOT / "Source/AERISFlightControl/Performance/AERISNavigationDisplayPipeline.cs")
runtime = read(ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs")
scheduler = read(ROOT / "Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs")
terrain_perf = read(ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs")
window = read(ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs")
csproj = read(ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj")

m = re.search(r"NavigationDisplayRangeStepsMeters\s*=\s*\{([^}]*)\}", settings, re.S)
ranges = [float(x.rstrip('fF ')) for x in m.group(1).split(',')] if m else []
suite.equal(ranges, [5000.0, 10000.0, 20000.0, 40000.0, 80000.0, 160000.0],
            "ND exposes exactly the certified six fixed ranges")
suite.check("NavigationDisplayAutoRange = false" in settings,
            "automatic range is disabled for the CP1 fixed-range baseline")
suite.check("NormalizeNavigationRange" in settings,
            "legacy/manual values normalize to the nearest certified range")
suite.check("navigationDisplayAutoRange = false" in read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg"),
            "factory CFG uses fixed-range mode")

suite.check('Performance\\AERISNavigationDisplayPipeline.cs' in csproj,
            "ND worker pipeline is compiled")
for token in ("AERISNavigationDisplaySnapshot", "AERISPreparedNavigationFrame",
              "AERISPreparedNavigationFrameApi", 'JobKey = "navigation-display-preprocess"',
              "AERISRuntimeLane.GeneralCompute", "SubmitLatest", "context.ThrowIfStale()"):
    suite.check(token in pipeline, "shared ND pipeline contract: " + token)
for forbidden in ("FlightGlobals", "FlightCtrlState", "Vessel", "CelestialBody",
                  "UnityEngine", "Texture2D", "GameObject", "Task.Run", "new Thread"):
    suite.check(forbidden not in pipeline, "ND worker boundary excludes " + forbidden)
suite.check("AERISRuntimeLane.SafetyLand" not in pipeline,
            "ND preprocessing cannot consume the Safety/LAND lane")

for token in ("readonly AERISNavigationDisplayPipeline navigationDisplay",
              "SubmitNavigationDisplay", "AERISPreparedNavigationFrameApi.Clear()",
              "navigationDisplay.Dispose()"):
    suite.check(token in runtime, "Performance Runtime owns ND pipeline: " + token)
for token in ("NavigationDisplayWorkerP95Ms", "NavigationDisplayResultAgeMs",
              "navigationDisplaySamples", "lastNavigationDisplayCommitTicks"):
    suite.check(token in scheduler, "scheduler publishes ND metric: " + token)
for token in ("nd_layout_ema_ms", "nd_repaint_ema_ms", "nd_capture_ema_ms",
              "nd_texture_upload_ema_ms", "nd_gc_positive_delta_ema_bytes",
              "nd_facilities", "nd_runways", "nd_plan", "nd_range_m"):
    suite.check(token in runtime, "performance CSV includes " + token)
headers = extract_string_array_expressions(
    runtime[runtime.index("telemetryWriter.WriteHeader"):])
values = extract_typed_array_expressions(
    runtime[runtime.index("void WriteTelemetry"):])
suite.equal(len(headers), len(values), "performance telemetry header/data column count")
suite.equal(len(headers), len(set(headers)), "performance telemetry header names are unique")

for token in ("RecordNdLayoutCost", "RecordNdRepaintCost", "NdLayoutEmaMs",
              "NdRepaintEmaMs", "UpdateNdAggregate"):
    suite.check(token in terrain_perf, "Layout/Repaint split metric: " + token)
suite.check("RecordNavigationDisplayEvent(repaint" in nd,
            "ND reports Layout/Repaint event cost to Performance Runtime")
suite.check("RecordNavigationDisplayCapture" in nd,
            "ND main-thread capture cost is measured")
suite.check("RecordNavigationDisplayTextureUpload" in terrain_renderer,
            "GPU mesh/render-ready upload cost is measured")
suite.check("bool measure = repaint || layout" in nd and
            "measure ? GC.GetTotalMemory(false) : 0L" in nd,
            "ND timing/GC probes run only on Layout and Repaint events")
suite.check("frame.DatabaseRevision == registry.DatabaseRevision" in nd and
            "frame.SelectionRevision == registry.SelectionRevision" in nd,
            "stale prepared frames are rejected after database or selection changes")

for token in ("planMode", "mapDragging", "EnterPlanAt", "RecenterPlan",
              "orientationBeforePlanTrackUp", "N/PLAN", "RECENTER",
              "DRAG MAP  PLAN"):
    suite.check(token in nd, "PLAN interaction contract: " + token)
suite.check("bool effectiveTrackUp = !planMode && settings.NavigationDisplayTrackUp" in nd,
            "PLAN forces NORTH UP without mutating the saved orientation")
suite.check("settings.NavigationDisplayTrackUp = orientationBeforePlanTrackUp" in nd,
            "RECENTER restores the pre-PLAN orientation")
suite.check("anchorV = planMode || !effectiveTrackUp ? 0.5f : 0.75f" in nd,
            "NORTH UP centers ownship while TRACK UP uses the lower anchor")
suite.check("double east = -delta.x / Math.Max(1.0, plot.width) * range * 1.30" in nd and
            "double north = delta.y / Math.Max(1.0, plot.height) * range;" in nd,
            "PLAN drag uses the exact inverse horizontal/vertical map scale")

for token in ("DrawPreparedRunway", "DirectionAName", "DirectionBName",
              "DrawSelectedRunwayEdgePointer", "RunwayName", "AirfieldName",
              "widthPixels", "DirectionASelectableIndex", "DirectionBSelectableIndex"):
    suite.check(token in nd, "runway display contract: " + token)
suite.check("for (int i = runways.Length - 1; i >= 0; i--)" in nd,
            "all prepared runways are rendered without a runway-count cap")
suite.check("if (!landActive) drawNonRunwayFacilities = true" in nd,
            "LAND suppresses unrelated facilities but retains the runway layer")
suite.check("DrawPreparedNavigation" in nd and "DrawLandingPlan" in nd,
            "registered runway layer and selected LAND overlay coexist")

for token in ("PreviewRunwayAt", "previewRunwayStableId", '"SELECT"',
              '"CENTER"', '"ARM"'):
    suite.check(token in nd, "explicit runway preview/confirmation contract: " + token)
preview = extract_method(nd, "PreviewRunwayAt")
suite.check("SelectAirfield" not in preview and "SelectDirection" not in preview,
            "direct map click only previews and never changes registry selection")
panel = nd[nd.index("void DrawPreviewPanel"):nd.index("static AERISPreparedRunwaySymbol FindPreparedRunway")]
suite.check("SelectAirfield(previewAirfieldIndex)" in panel and
            "SelectDirection(previewDirectionIndex)" in panel,
            "SELECT explicitly confirms airfield and direction")
suite.check("core.ArmLanding" in panel,
            "LAND observation ARM is exposed only after explicit selection")

for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "AERISNavDirector", "TryStartNavLanding", "NAV_LANDING"):
    suite.check(forbidden not in pipeline, "pipeline remains control/NAV free: " + forbidden)
suite.check("ND ranges are fixed at 5 / 10 / 20 / 40 / 80 / 160 km" in window,
            "SYSTEM options explain CP1 fixed-range behavior")
suite.check("ND main EMA: layout" not in window,
            "successor removes ND performance debug readout from SYSTEM")

suite.finish()
