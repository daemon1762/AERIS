#!/usr/bin/env python3
from __future__ import annotations
import math
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 ND auxiliary symbology and provider contracts")
nd = read(ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs")
traffic = read(ROOT / "Source/AERISFlightControl/Performance/AERISNavigationTrafficPipeline.cs")
wind = read(ROOT / "Source/AERISFlightControl/Integrations/AERISWindProviderApi.cs")
runtime = read(ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs")
fdi = read(ROOT / "Source/AERISFlightControl/UI/AERISFlightInstrument.cs")
scheduler = read(ROOT / "Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs")
settings = read(ROOT / "Source/AERISFlightControl/Settings/AERISSettings.cs")
factory = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")
csproj = read(ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj")
profiles = read(ROOT / "Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs")

for compiled in (r"Integrations\AERISWindProviderApi.cs",
                 r"Performance\AERISNavigationTrafficPipeline.cs",
                 r"Settings\AERISNavigationDisplayProfileStore.cs"):
    suite.check(compiled in csproj, "CP2 auxiliary source is compiled: " + compiled)

# Settings survive restart and have conservative defaults.
for key, expected in (("navigationDisplayTrailEnabled", "False"),
                      ("navigationDisplayTrackVectorEnabled", "True"),
                      ("navigationDisplayTrafficEnabled", "True"),
                      ("navigationDisplayWindEnabled", "True"),
                      ("navigationDisplayLandProfileSize", "Normal")):
    suite.check(key in settings, "settings load/save contract: " + key)
    suite.check((key + " = " + expected) in factory,
                "factory CFG auxiliary default: " + key)
for token in ("NavigationDisplayTrailEnabled = false",
              "NavigationDisplayTrackVectorEnabled = true",
              "NavigationDisplayTrafficEnabled = true",
              "NavigationDisplayWindEnabled = true",
              "NavigationDisplayLandProfileSize = AERISNavigationDisplayLandProfileSize.Normal"):
    suite.check(token in settings, "reset/default contract: " + token)

# Trail is bounded, body/vessel scoped, sampled slowly and movement filtered.
for token in ("List<TrailSample>(900)", "nextTrailSampleRealtime = now + 1f",
              "east * east + north * north < 400.0", "trailSamples.Count > 900",
              "RemoveRange(0, trailSamples.Count - 900)",
              "trailVesselPersistentId != vessel.persistentId",
              "string.Equals(trailBodyName, bodyName"):
    suite.check(token in nd, "bounded trail contract: " + token)
suite.check("DrawTrail" in nd and "Mathf.Lerp(0.12f, 0.68f" in nd,
            "trail is display-only and fades by age")

# Track vector is ground-track based, time/range bounded and hidden in PLAN.
for token in ("DrawTrackVector", "vessel.srfSpeed < 1.0", "planMode",
              "Math.Min(60.0", "range * 0.38 / speed", "range * 0.42",
              "int[] tickSeconds = { 15, 30, 45, 60 }", "cachedFallbackMapHeading"):
    suite.check(token in nd, "track-vector contract: " + token)

# Traffic capture is main-thread primitive-only and bounded before shared worker preprocessing.
for token in ("FlightGlobals.Vessels", "nextTrafficCaptureRealtime = now + 0.5f",
              "Math.Max(200000.0", "trafficCaptureScratch.Sort",
              "Math.Min(symbolLimit, trafficCaptureScratch.Count)",
              "Mathf.Clamp(", "16, 64", "core.Performance.CaptureStamp()",
              "SubmitNavigationTraffic"):
    suite.check(token in nd, "bounded traffic capture contract: " + token)
for token in ("AERISRuntimeLane.GeneralCompute", "SubmitLatest", "JobKey",
              '"navigation-traffic-preprocess"', "context.ThrowIfStale()",
              "ClosestApproachSeconds", "ClosestApproachMeters", "ThreatLevel",
              "LatitudeDeg = item.LatitudeDeg", "LongitudeDeg = item.LongitudeDeg",
              "Array.Sort(output, CompareTraffic)"):
    suite.check(token in traffic, "shared traffic worker contract: " + token)
for token in ("navigationTraffic = new AERISNavigationTrafficPipeline(scheduler)",
              "SubmitNavigationTraffic", "AERISPreparedTrafficFrameApi.Clear()"):
    suite.check(token in runtime, "Performance Runtime traffic integration: " + token)
suite.check("AERISNavigationTrafficPipeline.JobKey" in scheduler,
            "traffic worker contributes to shared ND worker telemetry")

# Worker boundary remains primitive and control-free.
traffic_clean = strip_csharp_comments_and_literals(traffic)
for forbidden in ("UnityEngine", "FlightGlobals", "FlightCtrlState", "Vessel",
                  "CelestialBody", "Texture2D", "GameObject", "MainThrottle",
                  "OnFlyByWire", "AERISNavDirector"):
    suite.check(forbidden not in traffic_clean,
                "traffic worker excludes Unity/KSP/control object: " + forbidden)
for forbidden in ("new Thread", "ThreadPool", "Task.Run", "Task<", "async ", "await "):
    suite.check(forbidden not in traffic,
                "traffic pipeline creates no private asynchronous runtime: " + forbidden)
suite.check("AERISRuntimeLane.SafetyLand" not in traffic,
            "traffic preprocessing cannot consume Safety/LAND lane")

# Independent closest-approach/threat sanity cases.
def cpa(east: float, north: float, rev: float, rnv: float):
    vv = rev * rev + rnv * rnv
    t = 0.0 if vv <= .01 else max(0.0, min(120.0, -(east*rev+north*rnv)/vv))
    return t, math.hypot(east + rev*t, north + rnv*t)

t, d = cpa(1000.0, 0.0, -20.0, 0.0)
suite.check(abs(t-50.0) < 1e-9 and d < 1e-9,
            "CPA predicts head-on lateral convergence")
t, d = cpa(1000.0, 0.0, 20.0, 0.0)
suite.check(t == 0.0 and abs(d-1000.0) < 1e-9,
            "CPA clamps diverging traffic to present time")
suite.check("closestMeters <= 500.0" in traffic and
            "absoluteRelativeAltitudeMeters <= 300.0" in traffic and
            "closestSeconds <= 60.0" in traffic and
            "closestSeconds > 0.0" not in traffic,
            "critical traffic gate includes present proximity and is bounded")
suite.check("closestMeters <= 1500.0" in traffic and
            "absoluteRelativeAltitudeMeters <= 750.0" in traffic and
            "closestSeconds <= 120.0" in traffic,
            "caution traffic gate is bounded")

# ND traffic symbology and explicit CENTER preview; no selection/control action.
for token in ("DrawPreparedTraffic", "DrawTrafficSymbol", "TrafficColor",
              "FormatSignedAltitude", "CPA ", "PreviewTrafficAt",
              "DrawTrafficPreviewPanel", '"CENTER"',
              "EnterPlanAt(traffic.LatitudeDeg, traffic.LongitudeDeg)"):
    suite.check(token in nd, "traffic display/preview contract: " + token)
traffic_preview = nd[nd.find("void DrawTrafficPreviewPanel"):
                     nd.find("void DrawTrail")]
for forbidden in ("SelectAirfield", "SelectDirection", "ArmLanding", "SetArmed"):
    suite.check(forbidden not in traffic_preview,
                "traffic preview has no selection/control action: " + forbidden)


# Threat events are published once per target/severity transition, mirrored to FDI and FDR log.
for token in ("AERISTrafficAlertApi", "lastLoggedThreatLevel",
              "lastLoggedStableId", "[TRAFFIC_ALERT]", "intervention=NONE",
              "AERISLogger.Error", "AERISLogger.Warn", "severity=CLEAR"):
    suite.check(token in traffic, "traffic alert transition/log contract: " + token)
for token in ("AERISTrafficAlertApi.TryGetLatest", '"TRAFFIC  "',
              "FdiTone.Red", "FdiTone.Amber"):
    suite.check(token in fdi, "FDI traffic annunciation contract: " + token)
suite.check("AERISTrafficAlertApi.Clear()" in runtime,
            "scene/vessel generation reset clears traffic alert")

# Provider-neutral wind API is the only integration boundary.
for token in ("public struct AERISWindQuery", "public struct AERISWindSample",
              "public interface IAERISWindProvider", "public static void Bind",
              "public static void Unbind", "Volatile.Read(ref provider)",
              "TryGetWind", "EastMetersPerSecond", "NorthMetersPerSecond",
              "UpMetersPerSecond"):
    suite.check(token in wind, "provider-neutral wind contract: " + token)
wind_clean = strip_csharp_comments_and_literals(wind)
for forbidden in ("UnityEngine", "FlightGlobals", "Vessel", "CelestialBody",
                  "FlightCtrlState", "MainThrottle", "FAR", "KerbalWeather"):
    suite.check(forbidden not in wind_clean,
                "wind API excludes direct game/mod/control dependency: " + forbidden)
for token in ("AERISWindProviderApi.TrySample", "Planetarium.GetUniversalTime()",
              "windFrom", "headwind", "crosswind", "DrawWindOverlay",
              "AERISWindProviderApi.ProviderName", "if (windAvailable)"):
    suite.check(token in nd, "wind display/menu contract: " + token)
suite.check('"WIND N/A"' not in nd,
            "wind item is hidden instead of shown when no provider exists")

# One compact menu owns optional layers and LAND terrain focus.
for token in ('"MENU"', '"TRAIL "', '"VECTOR "', '"TRAFFIC "',
              '"WIND "', '"LAND TERR "', "auxiliaryMenuOpen",
              "DrawAuxiliaryMenu", "if (changed) SaveSettingsAndProfile()",
              "ResolveAuxiliaryMenuRect", "auxiliaryMenuRect.Contains(e.mousePosition)"):
    suite.check(token in nd, "ND auxiliary menu contract: " + token)

# Per-craft presentation profiles are bounded, atomic, topology based and exclude runtime state.
for token in ("AERIS_ND_PROFILES", "MaximumProfiles = 128", "CreateSignature(Vessel vessel)",
              "part.craftID", "part.parent", "records.Sort(StringComparer.Ordinal)",
              "NavigationDisplayProfiles.cfg", "temporary profile round-trip failed",
              "File.Copy(PathName, backup, true)", "TerrainMode", "LandProfileSize"):
    suite.check(token in profiles, "per-craft ND profile contract: " + token)
for forbidden in ("vessel.persistentId", "vessel.latitude", "vessel.longitude",
                  "vessel.altitude", "mainBody", "FlightCtrlState", "MainThrottle"):
    suite.check(forbidden not in profiles,
                "craft signature/profile excludes runtime/control state: " + forbidden)
for token in ("SyncVesselProfile(vessel)", "SaveActiveProfile()",
              "SaveSettingsAndProfile()", "ResolveLandingProfileFraction",
              '"PROFILE " + FormatLandingProfileSize()',
              "AERISNavigationDisplayLandProfileSize.Compact",
              "AERISNavigationDisplayLandProfileSize.Large"):
    suite.check(token in nd, "ND per-craft profile integration: " + token)

# Scope boundary across all newly added auxiliary functionality.
aux_all = traffic + wind + nd[nd.find("void UpdateAuxiliarySnapshots"):]
for forbidden in ("FlightCtrlState", "MainThrottle", "wheelThrottle", "wheelSteer",
                  "OnFlyByWire", "AERISNavDirector", "TryStartNavLanding", "NAV_LANDING"):
    suite.check(forbidden not in aux_all,
                "CP2 auxiliary features remain control/NAV free: " + forbidden)

suite.finish()
