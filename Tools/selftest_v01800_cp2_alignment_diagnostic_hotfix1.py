#!/usr/bin/env python3
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, sha256

suite = CheckSuite("v0.18.0.0 CP2 Alignment Diagnostic Hotfix 1")
settings = read(SOURCE / "Settings" / "AERISSettings.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
nav = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
renderer = read(SOURCE / "Terrain" / "AERISTerrainGpuTileRenderer.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData" / "AERISFlightControl" / "AERISFlightControl.version")
default_cfg = read(ROOT / "GameData" / "AERISFlightControl" / "Config" / "AERISSettings.cfg")

suite.check("internal enum AERISTerrainRenderTargetOrientation" in settings and
            "Direct = 0" in settings and "Flipped = 1" in settings,
            "explicit RenderTexture presentation orientation enum exists")
suite.check("TerrainRenderTargetOrientation =\n            AERISTerrainRenderTargetOrientation.Direct" in settings,
            "DIRECT is the safe diagnostic default")
suite.check('"terrainRenderTargetOrientation"' in settings and
            'node.AddValue("terrainRenderTargetOrientation"' in settings,
            "terrain orientation is loaded and persisted")
suite.check("terrainRenderTargetOrientation = Direct" in default_cfg,
            "packaged default configuration selects DIRECT")
suite.check("SystemInfo.graphicsUVStartsAtTop ?" not in renderer,
            "backend UV heuristic no longer controls terrain presentation")
suite.check("flipVertically ? new Rect(0f, 1f, 1f, -1f)" in renderer and
            "new Rect(0f, 0f, 1f, 1f)" in renderer,
            "DIRECT/FLIPPED presentation paths are explicit")
suite.equal(renderer.count("static double PositiveLongitudeSpan("), 1,
            "alignment longitude helper has one definition")
suite.check("using System.Globalization;" in renderer,
            "alignment diagnostics have invariant numeric formatting support")
# Track-up ownship center is stored at anchorBottom in RenderTexture space.
# DIRECT maps it to GUI anchorV; FLIPPED leaves it on the opposite vertical side.
anchor_v = 0.75
anchor_bottom = 1.0 - anchor_v
direct_gui_v = 1.0 - anchor_bottom
flipped_gui_v = anchor_bottom
suite.check(abs(direct_gui_v - anchor_v) < 1e-9 and
            abs(flipped_gui_v - anchor_v) > 0.49,
            "DIRECT presentation maps the RenderTexture ownship anchor to GUI ownship")
suite.check("[ND/TERRAIN_ALIGN]" in renderer and
            "visualCoverage=" in renderer and "requestedCoverage=" in renderer and
            "deltaPx=" in renderer,
            "rate-limited alignment diagnostics expose coordinates and coverage")
suite.check("lastVisualCoverageFraction" in renderer and
            "styleKey, true" in renderer and "styleKey, false" in renderer,
            "visual fallback coverage is separated from requested-quality coverage")
suite.check("if (includeFallback && fallbackEntry != null)" in renderer,
            "requested-quality coverage cannot count fallback entries")

suite.check("internal bool LandSelectionExplicitlyCleared" in settings and
            '"landSelectionExplicitlyCleared"' in settings,
            "explicitly cleared LAND selection persists")
suite.check("internal bool ClearSelection()" in registry and
            "SelectedAirfieldIndex = -1;" in registry and
            "SelectedDirectionIndex = -1;" in registry and
            "LandSelectionExplicitlyCleared = true" in registry,
            "registry can clear airfield, runway direction, and persisted state")
suite.check("settings.LandSelectionExplicitlyCleared = false" in registry,
            "a new user selection cancels the explicit-clear latch")
suite.check('new GUIContent(compact ? "CLR" : "CLEAR"' in nav and
            'core.Landing.Disarm("ND selection cleared")' in nav and
            "registry.ClearSelection()" in nav and
            "ClearSelectionFromNd" in nav,
            "ND CLEAR disarms LAND observation and clears the registry")
suite.check("interactivePlot.height = Mathf.Max(1f, interactivePlot.height - 56f)" in nav,
            "preview-panel controls are excluded from map drag/preview handling")

landing_start = nav.index("void DrawLandingPlan(")
landing_end = nav.index("void DrawLandingProfile(", landing_start)
landing = nav[landing_start:landing_end]
suite.check("out threshold)) return" not in landing and
            "out opposite)) return" not in landing,
            "LAND geometry no longer disappears when a runway endpoint is off-screen")
suite.check(landing.count("DrawClippedLine(plot") >= 5,
            "runway and LOC funnel segments use viewport clipping")
suite.check("static bool ClipLineToRect" in nav and "static bool ClipTest" in nav,
            "bounded Liang-Barsky line clipping is implemented")
profile_start = nav.index("void DrawLandingProfile(")
profile_end = nav.index("string FormatTerrainMode()", profile_start)
profile = nav[profile_start:profile_end]
suite.check("nominalFarAboveThreshold * 1.18" in profile and
            "observation.VesselAltitudeAslMeters + 100.0" not in profile,
            "high aircraft altitude no longer compresses GS geometry")
suite.check("farLower" in profile and "farUpper" in profile and
            "DrawLine(farLower, farUpper" in profile,
            "GS profile includes persistent upper/lower funnel bounds")

# Independent clipping examples: crossing, fully outside, and vertical crossing.
def clip(rect, start, end):
    xmin, ymin, xmax, ymax = rect
    x0, y0 = start
    x1, y1 = end
    dx, dy = x1-x0, y1-y0
    t0, t1 = 0.0, 1.0
    for p, q in ((-dx, x0-xmin), (dx, xmax-x0), (-dy, y0-ymin), (dy, ymax-y0)):
        if abs(p) < 1e-9:
            if q < 0: return None
            continue
        r = q/p
        if p < 0:
            if r > t1: return None
            t0 = max(t0, r)
        else:
            if r < t0: return None
            t1 = min(t1, r)
    return ((x0+dx*t0, y0+dy*t0), (x0+dx*t1, y0+dy*t1))

cross = clip((0,0,100,100), (-50,50), (150,50))
suite.check(cross is not None and abs(cross[0][0]) < 1e-6 and
            abs(cross[1][0]-100) < 1e-6,
            "clipping model preserves a horizontal ILS segment across the viewport")
suite.check(clip((0,0,100,100), (-50,-20), (-10,-5)) is None,
            "clipping model rejects a fully external segment")
vertical = clip((0,0,100,100), (50,-20), (50,120))
suite.check(vertical is not None and abs(vertical[0][1]) < 1e-6 and
            abs(vertical[1][1]-100) < 1e-6,
            "clipping model preserves a vertical crossing segment")

suite.check("DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1" in build,
            "build entrypoint identifies the diagnostic checkpoint")
suite.check("DEV CP2 KK Runway Absolute Registration Hotfix 1 Preload Fast Path 1" in version,
            "AVC metadata identifies the diagnostic checkpoint")
suite.equal(sha256(SOURCE / "Autopilot" / "AERISBankDirector.cs"),
            "bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7",
            "BANK source remains byte-identical")
for rel in ("Autopilot/AERISHdgDirector.cs", "Autopilot/AERISPitchDirector.cs",
            "Autopilot/AERISVerticalSpeedDirector.cs", "Protect/GroundStabilityProtection.cs"):
    current = SOURCE / rel
    baseline = ROOT.parent / "aeris14_source" / "AERISFlightControl-v0.18.0.0_DEV_CP2_FieldRenderConsistencyHotfix1_Source" / "Source" / "AERISFlightControl" / rel
    if baseline.is_file():
        suite.equal(sha256(current), sha256(baseline), rel + " remains byte-identical")

suite.finish()
