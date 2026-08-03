#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, CheckSuite

suite = CheckSuite("v0.18.0.0 CP2.5 quality migration hotfix 1")

def read(relative):
    path = ROOT / relative
    suite.check(path.is_file(), relative + " exists")
    return path.read_text(encoding="utf-8", errors="replace") if path.is_file() else ""

settings = read("Source/AERISFlightControl/Settings/AERISSettings.cs")
performance = read("Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs")
window = read("Source/AERISFlightControl/UI/AERISWindow.cs")
default_cfg = read("GameData/AERISFlightControl/Config/AERISSettings.cfg")
build_version = read("Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
build = read("build_ubuntu.sh")
runner = read("Tools/run_v01800_cp25_acceptance.py")

suite.check("Automatic = 0" in settings and "Low = 1" in settings and
            "Medium = 2" in settings and "High = 3" in settings and "Land = 4" in settings,
            "quality enum is AUTO / LOW / MEDIUM / HIGH plus developer-only LAND")
suite.check("CurrentTerrainQualityModelRevision = 2" in settings and
            'ReadInt(node,\n                    "terrainQualityModelRevision", 0)' in settings and
            'node.AddValue("terrainQualityModelRevision"' in settings,
            "one-time quality model revision is loaded and persisted")
suite.check("terrainQualityRevision < CurrentTerrainQualityModelRevision" in settings and
            "MigrateLegacyTerrainQualityMode" in settings and "saveSettingsMigration = true" in settings,
            "legacy settings are migrated once and saved")
migration_pairs = {
    "AUTO": 'case "AUTOMATIC": return AERISTerrainQualityMode.Automatic;',
    "ECO": 'case "LOW": return AERISTerrainQualityMode.Low;',
    "BALANCED": 'case "MEDIUM": return AERISTerrainQualityMode.Medium;',
    "HIGH": 'case "HIGH": return AERISTerrainQualityMode.High;',
    "ULTRA": 'case "ULTRA": return AERISTerrainQualityMode.High;',
}
for old, expected in migration_pairs.items():
    suite.check('case "' + old + '"' in settings, old + " legacy token is recognized")
    suite.check(expected in settings, old + " migrates to the required CP2.5 setting")
suite.check('case "LAND": return AERISTerrainQualityMode.High;' in settings and
            "gate2LandProfile" in settings and "TerrainLandRuntimeQualityEnabled" in settings,
            "legacy LAND becomes HIGH plus a separate Gate 3 capability")
suite.check('[CP2.5/TERRAIN_QUALITY] migrated setting' in settings,
            "migration is observable in the AERIS log")

suite.check('new string[]{"AUTO","LOW","MEDIUM","HIGH"}' in window,
            "normal Terrain quality selector exposes exactly four public choices")
normal_selector_start = window.find("void DrawTerrainQualitySelector()")
normal_selector_end = window.find("void DrawTerrainModeSelector()", normal_selector_start)
normal_selector = window[normal_selector_start:normal_selector_end]
suite.check(all(token not in normal_selector for token in ('"ECO"', '"BAL"', '"ULTRA"', '"LAND"')),
            "normal Terrain quality selector contains no legacy or LAND label")
suite.check("settings.TerrainQualityMode==AERISTerrainQualityMode.Land?-1" in normal_selector,
            "developer LAND selection cannot masquerade as a normal public option")

suite.check('new AERISTerrainPerformanceProfile("LOW"' in performance and
            'new AERISTerrainPerformanceProfile("MEDIUM"' in performance and
            'new AERISTerrainPerformanceProfile("HIGH"' in performance and
            'new AERISTerrainPerformanceProfile("LAND"' in performance,
            "runtime profiles use LOW / MEDIUM / HIGH / LAND names")
suite.check("MaximumAutomaticQualityIndex = 2" in performance and
            "automaticQualityIndex < MaximumAutomaticQualityIndex" in performance,
            "AUTO recovery is hard-capped at HIGH and cannot enter LAND")
suite.check("case AERISTerrainQualityMode.Low: return 0;" in performance and
            "case AERISTerrainQualityMode.Medium: return 1;" in performance and
            "case AERISTerrainQualityMode.High: return 2;" in performance and
            "case AERISTerrainQualityMode.Land: return 2;" in performance and
            "settings.TerrainLandRuntimeQualityEnabled) return Profiles[3]" in performance,
            "base settings map to LOW/MEDIUM/HIGH while Gate 3 alone activates LAND")

suite.check("void DrawDeveloperTerrainQuality()" in window and
            '"Enable LAND detail when landing demand is active"' in window and
            "TerrainLandRuntimeQualityEnabled" in window and
            "DrawDeveloperTerrainQuality();" in window,
            "LAND capability remains only on the diagnostics/developer surface")
suite.check('new string[]{"GLOBAL","FAR","ROUTE","LOCAL"}' in window and
            'new string[]{"GLOBAL","FAR","ROUTE","LOCAL","LAND"}' not in window,
            "normal preload body selector excludes LAND")
suite.check('body.BodyName+" preload LAND"' in window and
            "AERISTerrainTileLod.Land" in window,
            "developer surface retains explicit per-body LAND detail control")
suite.check("LAND remains OFF during normal cruise." in window and
            "LAND ARM / Approach / Auto Landing demand exists" in window,
            "Gate 3 separation is explained on the developer surface")

suite.check("terrainQualityModelRevision = 2" in default_cfg and
            "terrainQualityMode = Automatic" in default_cfg and
            "terrainLandRuntimeQualityEnabled = False" in default_cfg,
            "fresh configuration starts at revision 2 in AUTO with LAND capability off")
suite.check("CP2.5 QUALITY MIGRATION HOTFIX 1" in build_version and
            "CP2.5 ALTITUDE GATE HOTFIX 1" in build_version and
            "CP2.5 QUALITY MIGRATION HOTFIX 1" in build,
            "identity preserves Gate 1 while exposing Gate 2")
suite.check("selftest_v01800_cp25_quality_migration_hotfix1.py" in runner,
            "CP2.5 acceptance runner includes Gate 2")

# Gate 2 must not add control authority or curated runway content.
developer_start = window.find("void DrawDeveloperTerrainQuality()")
developer_end = window.find("void DrawDebug()", developer_start)
developer_method = window[developer_start:developer_end]
quality_logic = "\n".join((settings, performance, normal_selector, developer_method))
for forbidden in ("FlightInputHandler", "MainThrottle", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS"):
    suite.check(forbidden not in quality_logic,
                "quality migration adds no " + forbidden + " authority/content")

suite.finish()
