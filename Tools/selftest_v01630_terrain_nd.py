#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import (ROOT, SOURCE, CheckSuite, extract_method, read,
    strip_csharp_comments_and_literals)

suite = CheckSuite("v0.16.3.0 adaptive multithreaded Garmin-style terrain ND")
terrain = read(SOURCE / "Terrain" / "AERISTerrainAwareness.cs")
performance = read(SOURCE / "Terrain" / "AERISTerrainPerformance.cs")
worker = read(SOURCE / "Terrain" / "AERISTerrainRasterWorker.cs")
nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
fdi = read(SOURCE / "UI" / "AERISFlightInstrument.cs")
bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
settings = read(SOURCE / "Settings" / "AERISSettings.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")
default_cfg = read(ROOT / "GameData" / "AERISFlightControl" / "Config" / "AERISSettings.cfg")
csproj = read(SOURCE / "AERISFlightControl.csproj")

for token, label in (
    ('"ECO", 13, 9', "ECO quality profile"),
    ('"BALANCED", 17, 11', "BALANCED quality profile"),
    ('"HIGH", 21, 15', "HIGH quality profile"),
    ('"ULTRA", 25, 17', "ULTRA quality profile"),
    ('AERISTerrainQualityMode.Automatic', "automatic quality mode"),
    ('AERISNavigationDisplayUpdateMode.Automatic', "automatic update-rate mode"),
    ('FrameTimeEmaMs', "measured frame-time input"),
    ('NdMainThreadEmaMs', "measured ND main-thread cost"),
    ('PqsSampleEmaMs', "measured PQS cost"),
    ('WorkerEmaMs', "measured worker cost"),
    ('automaticRateTier--', "fast adaptive update downshift"),
    ('automaticQualityIndex--', "adaptive quality downshift"),
    ('recoverySeconds >= 25f', "rate recovery hysteresis"),
    ('recoverySeconds >= 55f', "quality recovery hysteresis"),
    ('EffectiveTerrainFps', "independent terrain refresh rate"),
    ('EffectiveNavigationFps', "independent NAV/LAND refresh rate"),
    ('EffectiveSymbologyFps', "independent symbology refresh rate"),
): suite.check(token in performance, label)

for token, label in (
    ('MaximumGridColumns = 25', "maximum adaptive terrain grid width"),
    ('MaximumGridRows = 17', "maximum adaptive terrain grid height"),
    ('profile.PqsQueriesPerSecond', "profile-driven PQS query budget"),
    ('profile.MaximumSamplesPerFrame', "per-frame PQS hard cap"),
    ('sampleBudget', "time-based PQS token budget"),
    ('activeElevation', "double-buffered active terrain grid"),
    ('pendingElevation', "double-buffered pending terrain grid"),
    ('TryCaptureGridSnapshot', "immutable raster snapshot boundary"),
    ('TerrainAltitude', "KSP/PQS terrain altitude lookup"),
    ('body.ocean && elevation <= 1.0', "water classification"),
    ('MinimumPredictedClearanceMeters', "look-ahead clearance tracking"),
    ('AERISTerrainAlertLevel.PullUp', "PULL UP alert level"),
    ('stabilizedApproach', "runway approach nuisance rejection"),
    ('TERRAIN DATA DEGRADED', "safe degraded state"),
): suite.check(token in terrain, label)

terrain_code = strip_csharp_comments_and_literals(terrain)
for forbidden in ('FlightCtrlState', 'ApplyAaNative', 'SetArmed(', 'MainThrottle',
                  'wheelThrottle', 'wheelSteer', '.pitch=', '.roll=', '.yaw='):
    suite.check(forbidden not in terrain_code,
                f"terrain observer is command-free: {forbidden}")

for token, label in (
    ('Thread(WorkerLoop)', "dedicated raster worker thread"),
    ('IsBackground = true', "worker cannot block KSP shutdown"),
    ('ThreadPriority.BelowNormal', "worker is low priority"),
    ('pending = request', "latest-only bounded request slot"),
    ('if (pending != null) backlogObserved = true', "replacement/backlog observation"),
    ('result.Generation < newestRequestedGeneration', "stale worker result rejected"),
    ('byte[] NormalRgba', "primitive normal-map output buffer"),
    ('byte[] ThreatRgba', "primitive threat-map output buffer"),
    ('Rasterize(request)', "rasterization occurs on worker"),
): suite.check(token in worker, label)

worker_code = strip_csharp_comments_and_literals(worker)
for forbidden in ('Vessel', 'CelestialBody', 'PQS', 'Texture2D', 'GUI.', 'UnityEngine.Object',
                  'FlightGlobals', 'FlightCtrlState'):
    suite.check(forbidden not in worker_code,
                f"worker has no Unity/KSP object API: {forbidden}")

for token, label in (
    ('AERISTerrainRasterWorker', "ND owns raster worker"),
    ('TryTakeCompleted', "main-thread completed-buffer handoff"),
    ('LoadRawTextureData(completed.NormalRgba)', "main-thread normal texture upload"),
    ('LoadRawTextureData(completed.ThreatRgba)', "main-thread threat texture upload"),
    ('terrainTexture.Apply(false, false)', "main-thread texture apply"),
    ('completed.Generation < newestUploadedRasterGeneration', "ND rejects stale generations"),
    ('completed.GridRevision < terrain.GridRevision', "ND rejects stale terrain revisions"),
    ('UpdateRateLimitedSnapshots', "NAV/LAND and symbology rates are independently scheduled"),
    ('performance.EffectiveNavigationFps', "navigation snapshot adaptive rate"),
    ('performance.EffectiveSymbologyFps', "symbology snapshot adaptive rate"),
    ('terrain.Performance.EffectiveTerrainFps', "terrain raster adaptive rate"),
    ('RecordNdMainThreadCost', "ND reports measured draw cost"),
    ('GUI.DrawTexture(plot, texture', "terrain map remains one cached draw call"),
    ('DrawRangeRings', "range rings retained"),
    ('AERISFacilityKind.Helipad', "helipad symbol remains distinct"),
    ('DrawLandingPlan', "LAND/ILS overlay retained"),
    ('DrawLandingProfile', "glide-path inset retained"),
    ('HandleMouseWheel', "mouse-wheel zoom retained"),
    ('new GUIContent("◐", "LAND terrain overlay / focus")', "single overlay/focus button"),
    ('new GUIContent(FormatRange(range), "Map range")', "range-step button"),
    ('new GUIContent("−", "Zoom out")', "zoom-out button"),
    ('new GUIContent("+", "Zoom in")', "zoom-in button"),
): suite.check(token in nd, label)

suite.check('new object[]' not in terrain,
            "terrain sampler has no per-query object-array allocation")
suite.check('Delegate.CreateDelegate' in terrain,
            "normal terrain sampling uses a cached open-instance delegate")
suite.check('DrawTerrainContours' not in nd,
            "per-Repaint contour traversal remains removed")
suite.check('FillRect(new Rect(x, y' not in nd,
            "per-cell IMGUI rendering remains removed")
suite.check('terrainRasterWorker.Dispose()' in nd and
            'navigationDisplay.Dispose()' in fdi and 'flightInstrument.Dispose()' in bootstrap,
            "worker and runtime textures are disposed")

order = [nd.find('GUI.Button(view'), nd.find('GUI.Button(rangeRect'),
         nd.find('GUI.Button(minus'), nd.find('GUI.Button(plus')]
suite.check(all(v >= 0 for v in order) and order == sorted(order),
            "control order is [view][range][-][+]")
suite.check('"PULL UP"' not in nd and '"TERRAIN AHEAD"' not in nd,
            "ND does not duplicate FDI terrain warning text")
suite.check('core.Terrain.AlertText, FdiTone.Red' in fdi,
            "terrain warnings are routed to FDI in red")

for token in ('AERISTerrainQualityMode', 'AERISNavigationDisplayUpdateMode',
              'TerrainQualityMode', 'NavigationDisplayUpdateMode'):
    suite.check(token in settings, f"adaptive ND setting exists: {token}")
reset_defaults = extract_method(settings, "ResetToDefaults")
suite.check('TerrainQualityMode = AERISTerrainQualityMode.Automatic' in reset_defaults,
            "settings reset restores AUTO terrain quality")
suite.check('NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Automatic' in
            reset_defaults, "settings reset restores AUTO ND update rate")
suite.check(performance.count('profileRevision++;') == 2,
            "rate-only adaptation does not force a terrain-grid rebuild")
for key in ('terrainQualityMode', 'navigationDisplayUpdateMode'):
    suite.check(key in default_cfg, f"adaptive ND default exists: {key}")
for token in ('DrawTerrainQualitySelector()', 'DrawNavigationDisplayUpdateSelector()',
              'ND effective:'):
    suite.check(token in window, f"adaptive ND option UI exists: {token}")

suite.check('settings.NavigationDisplayAutoRange = false' in nd,
            "manual range interaction overrides auto range")
suite.check('index = (index + 1) % steps.Length' in nd,
            "range button cycles through all scales")
suite.check('direction.DisplayName' in nd and 'SelectedAirfield.DisplayName +' not in nd,
            "LAND header remains runway-only and low-text")

terrain_code = strip_csharp_comments_and_literals(terrain)
nd_code = strip_csharp_comments_and_literals(nd)
suite.check('vessel.heading' not in terrain_code and 'vessel.heading' not in nd_code,
            "no dependency on non-existent Vessel.heading")
suite.check('Vector3.ProjectOnPlane((Vector3)vessel.srf_velocity, up)' in terrain,
            "TRACK UP derives from horizontal surface velocity")
suite.check('ReferenceTransform.up' in terrain,
            "low-speed orientation uses control-point heading")

for path in ('Terrain\\AERISTerrainPerformance.cs',
             'Terrain\\AERISTerrainRasterWorker.cs',
             'Terrain\\AERISTerrainAwareness.cs'):
    suite.check(path in csproj, f"compiled source present: {path}")

suite.finish()
