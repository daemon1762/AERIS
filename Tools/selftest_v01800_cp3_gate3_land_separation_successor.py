#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite

suite = CheckSuite("v0.18.0.0 CP3 Gate 3 LAND separation successor")

def read(relative):
    path = ROOT / relative
    suite.check(path.is_file(), relative + " exists")
    return path.read_text(encoding="utf-8", errors="replace") if path.is_file() else ""

policy = read("Source/AERISFlightControl/Terrain/AERISTerrainLandDetailActivationPolicy.cs")
awareness = read("Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs")
performance = read("Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs")
tiles = read("Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs")
settings = read("Source/AERISFlightControl/Settings/AERISSettings.cs")
window = read("Source/AERISFlightControl/UI/AERISWindow.cs")
project = read("Source/AERISFlightControl/AERISFlightControl.csproj")
default_cfg = read("GameData/AERISFlightControl/Config/AERISSettings.cfg")
build_version = read("Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
build = read("build_ubuntu.sh")
runner = read("Tools/run_v01800_cp25_acceptance.py")
preload = read("Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs")

suite.check("[Flags]" in policy and "LandArm = 1" in policy and
            "Approach = 2" in policy and "AutoLanding = 4" in policy,
            "central LAND demand contract reserves ARM / Approach / Auto Landing")
suite.check("flightEligible && flightViewportActive" in policy and
            "capabilityEnabled && demand != AERISTerrainLandDetailDemand.None" in policy,
            "LAND detail requires flight, active viewport, capability and demand")
suite.check("LAND DETAIL STANDBY — WAITING FOR LAND ARM / APPROACH" in policy and
            "LAND DETAIL ACTIVE — " in policy,
            "policy exposes deterministic standby and active states")
suite.check("FlightCtrlState" in policy and "never writes" in policy and
            all(token not in policy for token in ("MainThrottle =", "pitch =", "roll =", "yaw =")),
            "policy is explicitly data/display-only")
suite.check('Terrain\\AERISTerrainLandDetailActivationPolicy.cs' in project,
            "new policy is compiled by the project")

suite.check("readonly AERISTerrainLandDetailActivationPolicy landDetailActivation" in awareness and
            "LandDetailActivation" in awareness,
            "terrain awareness owns and exposes the single central policy")
suite.check("bool landArmDemand = landing != null && landing.Armed;" in awareness and
            "landArmDemand, false, false" in awareness,
            "current implementation activates from LAND ARM while future inputs stay false")
suite.check("settings.TerrainLandRuntimeQualityEnabled" in awareness and
            "performance.SetLandDetailActive(landDetailActive)" in awareness and
            "displayTiles.Tick(vessel, landing, airfields, flightViewportActive," in awareness,
            "one policy result drives profile and runtime request activation")
suite.check('"[CP2.5/LAND_DETAIL] "' in awareness and
            "FormatDemand" in awareness,
            "LAND detail transitions are observable in the AERIS log")
suite.check("landDetailActivation.Reset(reason)" in awareness and
            "SetLandDetailActive(false)" in awareness,
            "scene/vessel resets revoke LAND detail")

suite.check("bool landDetailActive;" in performance and
            "internal void SetLandDetailActive(bool active)" in performance,
            "performance controller receives runtime LAND activation")
suite.check("settings.TerrainLandRuntimeQualityEnabled) return Profiles[3]" in performance and
            "Mathf.Clamp(index, 0, MaximumAutomaticQualityIndex)" in performance,
            "LAND profile is impossible without active policy and capability")
suite.check("case AERISTerrainQualityMode.Land: return 2;" in performance and
            "MaximumAutomaticQualityIndex = 2" in performance,
            "normal/legacy quality paths are capped at HIGH")

suite.check("bool landDetailActive;" in tiles and
            "bool landDetailDemand" in tiles and
            "LandDetailRequestsActive" in tiles,
            "tile system has an explicit runtime LAND request gate")
suite.check("if (landDetailActive != landDetailDemand)" in tiles and
            "nextPlanRealtime = 0f;" in tiles and
            "terrainRequestGeneration++;" in tiles and "planGeneration++;" in tiles,
            "LAND activation and release force immediate generation-safe replanning")
suite.check("direction = !landDetailActive ||" in tiles and
            "landing == null || !landing.Armed ? null : landing.ActiveDirection" in tiles,
            "selected runway cannot create runtime LAND requests while unarmed")
plan_start = tiles.find("void PlanRequests(Vessel vessel, AERISLandingFoundation landing)")
plan_end = tiles.find("void AddLandingPointWithPins", plan_start)
plan = tiles[plan_start:plan_end]
landing_start = tiles.find("void AddLandingPointWithPins", plan_start)
landing_end = tiles.find("void MarkResidentPin", landing_start)
landing_helper = tiles[landing_start:landing_end]
suite.check(plan.count("AddLandingPointWithPins(") == 2 and
            landing_helper.count("AERISTerrainTileLod.Land") == 2 and
            landing_helper.count("AERISTerrainRequestLane.Landing") == 1,
            "runtime LAND requests remain limited to the armed runway pair through the Gate 3 helper")
suite.check("IsBackgroundPopulationLod" in tiles and
            "lod == AERISTerrainTileLod.Land" not in tiles[tiles.find(
                "static bool IsBackgroundPopulationLod"):tiles.find(
                "static bool IsGate3ResidentLod")],
            "cruise background population still excludes LAND payloads")
suite.check("RefreshPreloadPoints(vessel, landing, airfields);" in tiles and
            "preloadBuilder.Tick(currentBody, flight);" in tiles,
            "SSD Preload Builder remains independent from runtime LAND gating")
suite.check("selected.QualityLimit = AERISTerrainTileLod.Land;" in preload and
            'event=PROMOTE; from=FAR_GLOBAL; to=LAND_SITES' in preload,
            "frozen CP2 automatic SSD LAND-site promotion is preserved")

suite.check("CurrentTerrainQualityModelRevision = 2" in settings and
            "TerrainLandRuntimeQualityEnabled" in settings,
            "quality model revision 2 persists a separate capability")
suite.check('"terrainLandRuntimeQualityEnabled"' in settings and
            'node.AddValue("terrainLandRuntimeQualityEnabled"' in settings,
            "LAND capability is loaded and saved")
suite.check("gate2LandProfile" in settings and
            "rawLandCapability || gate2LandProfile" in settings and
            "AERISTerrainQualityMode.High" in settings,
            "Gate 2 global LAND setting migrates to HIGH plus capability")
suite.check('case "LAND": return AERISTerrainQualityMode.High;' in settings,
            "stray LAND can no longer become a global runtime profile")
suite.check("terrainQualityModelRevision = 2" in default_cfg and
            "terrainLandRuntimeQualityEnabled = False" in default_cfg,
            "fresh installs keep LAND capability off")

suite.check('"Enable LAND detail when landing demand is active"' in window and
            "TerrainLandRuntimeQualityEnabled" in window,
            "developer control label is demand-scoped")
suite.check("landPolicy.StatusText" in window and
            "LAND remains OFF during normal cruise." in window,
            "diagnostics exposes live policy state and normal-cruise contract")
suite.check('new string[]{"AUTO","LOW","MEDIUM","HIGH"}' in window,
            "normal public quality selector remains four choices")

suite.check("CP2.5 LAND SEPARATION HOTFIX 1" in build_version and
            "CP2.5 QUALITY MIGRATION HOTFIX 1" in build_version and
            "CP2.5 LAND SEPARATION HOTFIX 1" in build,
            "identity preserves Gates 1-2 and exposes Gate 3")
suite.check("selftest_v01800_cp25_land_separation_hotfix1.py" in runner,
            "CP2.5 acceptance runner includes Gate 3")

combined = "\n".join((policy, awareness, performance, tiles, settings, window))
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS"):
    suite.check(forbidden not in combined,
                "Gate 3 adds no " + forbidden + " authority/content")

suite.finish()
