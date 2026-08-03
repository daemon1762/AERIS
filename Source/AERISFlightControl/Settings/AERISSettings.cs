using System;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Terrain;

namespace AERISFlightControl.Settings
{
    internal enum AERISShortcutAction
    {
        ToggleWindow = 0,
        ToggleMaster = 1,
        EmergencyDisable = 2
    }

    internal enum AERISDisplayMode
    {
        Automatic = 0,
        Always = 1,
        // Absolute user override. OFF suppresses the selected panel and its
        // display-owned work even when AP, LAND or a provider would normally demand it.
        Off = 2
    }

    internal enum AERISTerrainQualityMode
    {
        Automatic = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        // LAND is intentionally excluded from the normal user selector. It is a
        // developer-only detail profile and Gate 3 controls when LAND requests exist.
        Land = 4
    }

    internal enum AERISNavigationDisplayLandProfileSize
    {
        Compact = 0,
        Normal = 1,
        Large = 2
    }

    internal enum AERISNavigationDisplayUpdateMode
    {
        Automatic = 0,
        Fps10 = 1,
        Fps20 = 2,
        Fps30 = 3,
        Fps45 = 4,
        Fps60 = 5
    }

    // Temporary CP2 diagnostic control. DIRECT preserves the RenderTexture's
    // GL.LoadOrtho orientation when presenting it in IMGUI. FLIPPED applies one
    // explicit vertical inversion. The old backend-derived heuristic is deliberately
    // not used because it displaced the moving-map terrain origin away from ownship
    // under the observed Proton/DXVK runtime.
    internal enum AERISTerrainRenderTargetOrientation
    {
        Direct = 0,
        Flipped = 1
    }

    internal sealed class AERISSettings
    {
        internal static readonly float[] NavigationDisplayRangeStepsMeters =
            { 5000f, 10000f, 20000f, 40000f, 80000f, 160000f };
        internal const float DefaultMainWindowWidth = 520f;
        internal const float DefaultMainWindowHeight = 610f;
        internal float MainWindowX = 260f;
        internal float MainWindowY = 120f;
        internal float MainWindowWidth = DefaultMainWindowWidth;
        internal float MainWindowHeight = DefaultMainWindowHeight;
        internal bool MainWindowVisible;
        internal bool DebugVisible;
        internal bool ShowSasWarning = true;
        internal bool SoftAssistArmed;
        internal string SoftAssistStrength = "Low";
        internal bool SoftAssistPitchEnabled = true;
        internal bool SoftAssistRollEnabled = true;
        internal bool SoftAssistYawEnabled = true;
        internal float ProtectAoAWarningDegrees = 8f;
        internal float ProtectAoALimitDegrees = 12f;
        internal bool AoADebugForceTrigger;
        internal bool AoADebugTraceEnabled = true;
        internal bool DebugFlightOverlayVisible;
        internal bool AppIntegrationEnabled = true;
        internal bool EnableAABackgroundTraining = true;
        internal bool AllowAASpeedAutoBrakes = false;
        // Observation-only by design. This remains independent from both AERIS and AA control paths.
        internal bool EnableAAComparisonTelemetry = true;
        // CP3.75 preserved non-ND feature from AERIS20/CP3.5: bounded verified FDR/CVR ZIP retention.
        // One verified flight ZIP contains both FDR and CVR payloads. Values are clamped to 1..30.
        internal int FlightDataArchiveLimit = 10;
        // Performance-runtime launch policy.  Zero selects the scheduler's
        // processor-aware AUTO sizing.  A value of two is the documented minimum
        // worker acceptance configuration; larger explicit values remain available
        // to advanced users through the CFG.  Changes take effect on the next start.
        internal int PerformanceWorkerOverride;
        internal bool PerformanceGpuAccelerationEnabled = true;
        // v0.7.4 SPEED: symmetric VEL -> ACC acceleration cap, persisted across sessions.
        // v0.9.4: new/factory-reset installs start at the director's conservative 4.0 m/s² default.
        internal float VelocityAccelerationLimitMps2 = 4.0f;
        // v0.9.5: optional SPEED-owned, continuously variable aerodynamic braking.
        // ON is the flight-control default. Existing users who explicitly saved OFF keep it.
        // Wheel brakes are never part of this feature.
        internal bool SpeedAutomaticAirbrakeEnabled = true;
        // Dedicated thin-air turn coordination is enabled by default. It still uses
        // BANK/PITCH/HDG directors and AA's existing native rate controllers.
        internal bool ThinAirTurnAssistEnabled = true;

        // v0.9.4: last accepted AP entry values.  These are configuration values only;
        // armed/active state is deliberately never persisted across scenes.
        internal float ApBankTargetDeg;
        internal float ApHeadingTargetDeg;
        internal bool ApHeadingAutoBankLimit = true;
        internal float ApHeadingManualBankLimitDeg = 30f;
        // Data-only flight-plan library selection. These values have no ARM or control authority.
        internal string FlightPlanSelectedBody = string.Empty;
        internal string FlightPlanSelectedId = string.Empty;
        // Independent LAND foundation selection. Every new registry/flight starts neutral;
        // an explicit user selection may be preserved only across an in-session registry reload.
        internal string LandSelectedAirfieldId = string.Empty;
        internal string LandSelectedDirectionId = string.Empty;
        internal bool LandSelectionExplicitlyCleared = true;
        internal bool LandSectionExpanded = true;
        internal const int CurrentAirfieldsUiLayoutRevision = 2;
        internal int AirfieldsUiLayoutRevision = CurrentAirfieldsUiLayoutRevision;
        internal const int CurrentTerrainQualityModelRevision = 3;
        internal const float FixedNavigationDisplayUpdateHz = 10f;
        internal int TerrainQualityModelRevision = CurrentTerrainQualityModelRevision;
        internal const int CurrentDisplayPolicyRevision = 1;
        internal int DisplayPolicyRevision = CurrentDisplayPolicyRevision;
        internal const int CurrentProtectDefaultPolicyRevision = 1;
        internal int ProtectDefaultPolicyRevision = CurrentProtectDefaultPolicyRevision;
        internal bool AirfieldsCertifiedExpanded;
        internal bool AirfieldsUserCalibratedExpanded;
        internal bool AirfieldsFailedExpanded;
        internal bool AirfieldsPendingExpanded;
        internal bool AirfieldsRevalidationExpanded;
        internal bool AirfieldsProvisionalExpanded;
        // CP2-only runway candidate presentation. This field and every consumer are
        // mandatory removal items before CP2 CLOSE; it grants no guidance authority.
        internal AERISDisplayMode NavigationDisplayMode = AERISDisplayMode.Automatic;
        internal bool NavigationDisplayTrackUp = true;
        internal bool NavigationDisplayAutoRange = false;
        internal float NavigationDisplayManualRangeMeters = 20000f;
        // CP3.75 Candidate 2 consolidation: terrain quality is fixed to Golden LOW and
        // ND presentation cadence is fixed to 10 Hz. Legacy enum values remain readable
        // only to migrate older user CFG files without breaking compatibility.
        internal AERISTerrainQualityMode TerrainQualityMode = AERISTerrainQualityMode.Low;
        // Retained for backward CFG/source compatibility only. Candidate 2 does not expose
        // or activate a separate LAND terrain-quality preset.
        internal bool TerrainLandRuntimeQualityEnabled;
        internal AERISNavigationDisplayUpdateMode NavigationDisplayUpdateMode =
            AERISNavigationDisplayUpdateMode.Fps10;
        internal AERISTerrainDisplayMode TerrainDisplayMode = AERISTerrainDisplayMode.Automatic;
        internal AERISTerrainGpuMode TerrainGpuMode = AERISTerrainGpuMode.Automatic;
        internal AERISTerrainRenderTargetOrientation TerrainRenderTargetOrientation =
            AERISTerrainRenderTargetOrientation.Direct;
        internal AERISTerrainColourPreset TerrainColourPreset = AERISTerrainColourPreset.Standard;
        // Zero selects the active quality profile's bounded cache budget. Explicit values
        // are clamped and affect only AERIS terrain presentation caches.
        internal int TerrainRamCacheLimitMiB;
        internal int TerrainDiskCacheLimitMiB;
        internal int TerrainVramCacheLimitMiB;
        // Candidate 13 final-test policy: preload has one validated configuration.
        // The only user preference is whether automatic preload is enabled.
        internal bool TerrainPreloadEnabled = true;
        internal bool TerrainContoursEnabled = true;
        internal bool TerrainShadingEnabled = true;
        internal bool NavigationDisplayTrailEnabled;
        internal bool NavigationDisplayTrackVectorEnabled = true;
        internal bool NavigationDisplayTrafficEnabled = true;
        internal bool NavigationDisplayWindEnabled = true;
        internal AERISNavigationDisplayLandProfileSize NavigationDisplayLandProfileSize =
            AERISNavigationDisplayLandProfileSize.Normal;
        // LAND/ILS and future NAV can either overlay guidance on the terrain map or
        // use a clean guidance-focused presentation. Each preference is independent.
        internal bool NavigationDisplayLandOverlay = true;
        internal bool NavigationDisplayNavOverlay = true;
        // FDI and ND are independent user-owned panels. Geometry is stored as a
        // fraction of the current KSP game window so resolution changes preserve
        // relative placement and size. A false customized flag uses the current
        // navball-relative default without overwriting the user's saved geometry.
        internal bool NavigationDisplayLayoutCustomized;
        internal float NavigationDisplayRectX01;
        internal float NavigationDisplayRectY01;
        internal float NavigationDisplayRectW01 = 380f / 1920f;
        internal float NavigationDisplayRectH01 = 244f / 1080f;

        // Persisted presentation state. These do not persist ARM state.
        internal bool FlightPlanSectionExpanded = true;
        internal bool ApLateralSectionExpanded = true;
        internal bool ApVerticalSectionExpanded = true;
        internal bool ApSpeedSectionExpanded = true;
        internal float ApPitchTargetDeg;
        internal float ApVerticalSpeedTargetMps;
        internal float ApVerticalSpeedMaxPitchDeg = 20f;
        internal float ApAltitudeTargetMeters = 1000f;
        internal float ApAltitudeMaxVerticalSpeedMps = 50f;
        internal float ApAltitudeMaxPitchDeg = 20f;
        internal float ApAccelerationTargetMps2;
        internal float ApVelocityTargetMps;
        internal bool ApVelocityTargetConfirmed;

        // Ground Assist is the ground-side PROTECT envelope. Ground Stability remains
        // its highest-priority lateral layer; Brake Assist and Airbrake Link are trajectory-first.
        internal bool GroundAssistEnabled = true;
        internal bool GroundStabilityEnabled = true;
        internal bool GroundBrakeAssistAuto = true;
        internal bool GroundAirbrakeLinkAuto = true;
        internal float GroundAirbrakeLimit = 1.0f;
        internal float GroundTargetDecelerationMps2 = 3.0f;
        internal float GroundMaximumNormalDecelerationMps2 = 5.0f;
        internal float GroundBrakeFallbackDelaySeconds = 1.0f;
        internal float GroundMaxBrake = 1.0f;
        internal float GroundLowSpeedFadeMps = 8.0f;
        internal bool GroundParkingHold = true;
        internal bool GroundDragChuteAuto = false;
        internal bool GroundReverseThrustAuto = true;
        internal float AutoTakeoffStallFactor = 1.15f;
        internal float AutoTakeoffManualVrMps = 70f;
        internal float AutoTakeoffRotationPitchDeg = 10f;
        internal float AutoTakeoffRotationRateDegPerSec = 2.5f;
        internal float AutoTakeoffInitialClimbVsMps = 20f;
        internal float AutoTakeoffHandoffRadarAltitudeM = 50f;

        // v0.6.3 SYSTEM > OPTIONS presentation settings.
        internal bool ShowVirtualAttitudeInstrument = true;
        internal bool ShowApErrorDisplay = true;
        internal bool ShowFlightStatusDisplay = true;
        // AERIS Flight Deviation Instrument (FDI). AUTOMATIC shows the panel only
        // while guidance or an intervention/provider annunciation is active. ALWAYS
        // retains the panel as a persistent flight-deck instrument. OFF is absolute.
        internal AERISDisplayMode FlightInstrumentDisplayMode = AERISDisplayMode.Automatic;
        internal bool FlightInstrumentLayoutCustomized;
        internal float FlightInstrumentRectX01;
        internal float FlightInstrumentRectY01;
        internal float FlightInstrumentRectW01 = 380f / 1920f;
        internal float FlightInstrumentRectH01 = 244f / 1080f;

        // Each AERIS command uses either one key or a chord of two simultaneous keys.
        // Defaults retain the prior visible behavior: Alt+A / Alt+F / Alt+X.
        internal KeyCode ToggleWindowPrimary = KeyCode.LeftAlt;
        internal KeyCode ToggleWindowSecondary = KeyCode.A;
        internal KeyCode ToggleMasterPrimary = KeyCode.LeftAlt;
        internal KeyCode ToggleMasterSecondary = KeyCode.F;
        internal KeyCode EmergencyDisablePrimary = KeyCode.LeftAlt;
        internal KeyCode EmergencyDisableSecondary = KeyCode.X;

        private static string PathName { get { return System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/AERISFlightControl/Config/AERISSettings.cfg"); } }

        internal static AERISSettings Load()
        {
            var settings = new AERISSettings();
            bool saveSettingsMigration = false;
            try
            {
                if (!File.Exists(PathName)) return settings;
                ConfigNode node = ConfigNode.Load(PathName);
                if (node == null || node.name != "AERIS_SETTINGS") return settings;
                settings.MainWindowX = Mathf.Clamp(ReadFloat(node, "mainWindowX", settings.MainWindowX), -5000f, 5000f);
                settings.MainWindowY = Mathf.Clamp(ReadFloat(node, "mainWindowY", settings.MainWindowY), -5000f, 5000f);
                settings.MainWindowWidth = Mathf.Clamp(ReadFloat(node, "mainWindowWidth", settings.MainWindowWidth), 360f, 1920f);
                settings.MainWindowHeight = Mathf.Clamp(ReadFloat(node, "mainWindowHeight", settings.MainWindowHeight), 300f, 1200f);
                settings.MainWindowVisible = ReadBool(node, "mainWindowVisible", false);
                settings.DebugVisible = ReadBool(node, "debugVisible", false);
                settings.ShowSasWarning = ReadBool(node, "showSasWarning", true);
                settings.SoftAssistArmed = ReadBool(node, "softAssistArmed", false);
                settings.SoftAssistStrength = ReadString(node, "softAssistStrength", settings.SoftAssistStrength);
                settings.SoftAssistPitchEnabled = ReadBool(node, "softAssistPitchEnabled", true);
                settings.SoftAssistRollEnabled = ReadBool(node, "softAssistRollEnabled", true);
                settings.SoftAssistYawEnabled = ReadBool(node, "softAssistYawEnabled", true);
                settings.ProtectAoAWarningDegrees = Mathf.Clamp(ReadFloat(node, "protectAoAWarningDegrees", settings.ProtectAoAWarningDegrees), 1f, 45f);
                settings.ProtectAoALimitDegrees = Mathf.Clamp(ReadFloat(node, "protectAoALimitDegrees", settings.ProtectAoALimitDegrees),
                    settings.ProtectAoAWarningDegrees + 0.5f, 60f);
                settings.AoADebugForceTrigger = ReadBool(node, "aoaDebugForceTrigger", false);
                settings.AoADebugTraceEnabled = ReadBool(node, "aoaDebugTraceEnabled", true);
                settings.DebugFlightOverlayVisible = ReadBool(node, "debugFlightOverlayVisible", false);
                settings.AppIntegrationEnabled = ReadBool(node, "appIntegrationEnabled", true);
                settings.EnableAABackgroundTraining = ReadBool(node, "enableAABackgroundTraining", true);
                settings.AllowAASpeedAutoBrakes = ReadBool(node, "allowAASpeedAutoBrakes", false);
                settings.EnableAAComparisonTelemetry = ReadBool(node, "enableAAComparisonTelemetry", true);
                settings.FlightDataArchiveLimit = NormalizeFlightDataArchiveLimit(ReadInt(node,
                    "flightDataArchiveLimit", settings.FlightDataArchiveLimit));
                settings.PerformanceWorkerOverride = NormalizeWorkerOverride(ReadInt(node,
                    "performanceWorkerOverride", settings.PerformanceWorkerOverride));
                settings.PerformanceGpuAccelerationEnabled = ReadBool(node,
                    "performanceGpuAccelerationEnabled", true);
                settings.VelocityAccelerationLimitMps2 = Mathf.Clamp(ReadFloat(node, "velocityAccelerationLimitMps2", settings.VelocityAccelerationLimitMps2), 0.10f, 30.0f);
                settings.SpeedAutomaticAirbrakeEnabled = ReadBool(node, "speedAutomaticAirbrakeEnabled", true);
                settings.ThinAirTurnAssistEnabled = ReadBool(node, "thinAirTurnAssistEnabled", true);
                settings.ApBankTargetDeg = Mathf.Clamp(ReadFloat(node, "apBankTargetDeg", settings.ApBankTargetDeg), -90f, 90f);
                settings.ApHeadingTargetDeg = Mathf.Repeat(ReadFloat(node, "apHeadingTargetDeg", settings.ApHeadingTargetDeg), 360f);
                settings.ApHeadingAutoBankLimit = ReadBool(node, "apHeadingAutoBankLimit", true);
                settings.ApHeadingManualBankLimitDeg = Mathf.Clamp(ReadFloat(node, "apHeadingManualBankLimitDeg", settings.ApHeadingManualBankLimitDeg), 0f, 90f);
                settings.FlightPlanSelectedBody = ReadString(node, "flightPlanSelectedBody", string.Empty);
                settings.FlightPlanSelectedId = ReadString(node, "flightPlanSelectedId", string.Empty);
                settings.LandSelectedAirfieldId = ReadString(node, "landSelectedAirfieldId", string.Empty);
                settings.LandSelectedDirectionId = ReadString(node, "landSelectedDirectionId", string.Empty);
                settings.LandSelectionExplicitlyCleared = ReadBool(node,
                    "landSelectionExplicitlyCleared", false);
                settings.LandSectionExpanded = ReadBool(node, "landSectionExpanded", true);
                int airfieldsUiLayoutRevision = ReadInt(node,
                    "airfieldsUiLayoutRevision", 0);
                if (airfieldsUiLayoutRevision < CurrentAirfieldsUiLayoutRevision)
                {
                    // One-time migration from the old open-by-default list. It also
                    // repairs existing installs whose tall expanded rows overwhelmed
                    // the compact window after runway calibration details were added.
                    settings.AirfieldsCertifiedExpanded = false;
                    settings.AirfieldsUserCalibratedExpanded = false;
                    settings.AirfieldsFailedExpanded = false;
                    settings.AirfieldsPendingExpanded = false;
                    settings.AirfieldsRevalidationExpanded = false;
                    settings.AirfieldsProvisionalExpanded = false;
                    settings.AirfieldsUiLayoutRevision = CurrentAirfieldsUiLayoutRevision;
                    saveSettingsMigration = true;
                }
                else
                {
                    settings.AirfieldsUiLayoutRevision = airfieldsUiLayoutRevision;
                    settings.AirfieldsCertifiedExpanded = ReadBool(node,
                        "airfieldsCertifiedExpanded", false);
                    settings.AirfieldsUserCalibratedExpanded = ReadBool(node,
                        "airfieldsUserCalibratedExpanded", false);
                    settings.AirfieldsFailedExpanded = ReadBool(node,
                        "airfieldsFailedExpanded", false);
                    settings.AirfieldsPendingExpanded = ReadBool(node,
                        "airfieldsPendingExpanded", false);
                    settings.AirfieldsRevalidationExpanded = ReadBool(node,
                        "airfieldsRevalidationExpanded", false);
                    settings.AirfieldsProvisionalExpanded = ReadBool(node,
                        "airfieldsProvisionalExpanded", false);
                }
                AERISDisplayMode navigationFallback = node.HasValue("navigationDisplayEnabled")
                    ? (ReadBool(node, "navigationDisplayEnabled", true)
                        ? AERISDisplayMode.Always : AERISDisplayMode.Off)
                    : AERISDisplayMode.Automatic;
                settings.NavigationDisplayMode = ReadDisplayMode(node,
                    "navigationDisplayMode", navigationFallback);
                settings.NavigationDisplayTrackUp = ReadBool(node, "navigationDisplayTrackUp", true);
                // CP1 fixed-range baseline. The legacy AUTO key remains readable for
                // compatibility but no longer changes the six certified ND ranges.
                settings.NavigationDisplayAutoRange = false;
                settings.NavigationDisplayManualRangeMeters = NormalizeNavigationRange(
                    ReadFloat(node, "navigationDisplayManualRangeMeters", 20000f));
                int terrainQualityRevision = ReadInt(node,
                    "terrainQualityModelRevision", 0);
                string rawTerrainQuality = node.HasValue("terrainQualityMode") ?
                    node.GetValue("terrainQualityMode") : string.Empty;
                bool rawLandCapability = ReadBool(node,
                    "terrainLandRuntimeQualityEnabled", false);
                AERISTerrainQualityMode loadedTerrainQuality = ReadTerrainQualityMode(node,
                    "terrainQualityMode", AERISTerrainQualityMode.Low);
                AERISNavigationDisplayUpdateMode loadedNdUpdate =
                    ReadNavigationDisplayUpdateMode(node, "navigationDisplayUpdateMode",
                        AERISNavigationDisplayUpdateMode.Fps10);

                // CP3.75 Candidate 2 settings consolidation. LOW is the only active terrain
                // quality authority and preserves the late-CP3 cartographic presentation.
                // Legacy AUTO/MEDIUM/HIGH/LAND values remain parseable only so old CFG files
                // migrate cleanly. ND presentation cadence is fixed internally at 10 Hz.
                settings.TerrainQualityMode = AERISTerrainQualityMode.Low;
                settings.TerrainLandRuntimeQualityEnabled = false;
                settings.TerrainQualityModelRevision = CurrentTerrainQualityModelRevision;
                settings.NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
                if (terrainQualityRevision != CurrentTerrainQualityModelRevision ||
                    loadedTerrainQuality != AERISTerrainQualityMode.Low ||
                    rawLandCapability ||
                    loadedNdUpdate != AERISNavigationDisplayUpdateMode.Fps10)
                {
                    saveSettingsMigration = true;
                    AERISLogger.Info("[CP3.75/ND_POLICY] consolidated legacy ND settings: " +
                        "terrain='" + rawTerrainQuality + "' -> LOW; LAND capability -> OFF; " +
                        "ND update=" + loadedNdUpdate + " -> 10 Hz.");
                }
                settings.TerrainDisplayMode = ReadEnum(node, "terrainDisplayMode",
                    AERISTerrainDisplayMode.Automatic);
                settings.TerrainGpuMode = ReadEnum(node, "terrainGpuMode",
                    AERISTerrainGpuMode.Automatic);
                settings.TerrainRenderTargetOrientation = ReadEnum(node,
                    "terrainRenderTargetOrientation",
                    AERISTerrainRenderTargetOrientation.Direct);
                settings.TerrainColourPreset = ReadEnum(node, "terrainColourPreset",
                    AERISTerrainColourPreset.Standard);
                settings.TerrainRamCacheLimitMiB = NormalizeCacheLimit(ReadInt(node,
                    "terrainRamCacheLimitMiB", 0), 64, 4096);
                settings.TerrainDiskCacheLimitMiB = NormalizeCacheLimit(ReadInt(node,
                    "terrainDiskCacheLimitMiB", 0), 256, 32768);
                settings.TerrainVramCacheLimitMiB = NormalizeCacheLimit(ReadInt(node,
                    "terrainVramCacheLimitMiB", 0), 32, 4096);
                if (node.HasValue("terrainPreloadEnabled"))
                    settings.TerrainPreloadEnabled = ReadBool(node, "terrainPreloadEnabled", true);
                else
                {
                    // Legacy mode migration: retain only the user's ON/OFF intent. All
                    // former speed/storage/idle controls are intentionally discarded.
                    AERISTerrainPreloadMode legacyMode = ReadEnum(node, "terrainPreloadMode",
                        AERISTerrainPreloadMode.AggressiveIdle);
                    settings.TerrainPreloadEnabled = legacyMode != AERISTerrainPreloadMode.Off;
                    saveSettingsMigration = true;
                }
                settings.TerrainContoursEnabled = ReadBool(node, "terrainContoursEnabled", true);
                settings.TerrainShadingEnabled = ReadBool(node, "terrainShadingEnabled", true);
                settings.NavigationDisplayTrailEnabled = ReadBool(node,
                    "navigationDisplayTrailEnabled", false);
                settings.NavigationDisplayTrackVectorEnabled = ReadBool(node,
                    "navigationDisplayTrackVectorEnabled", true);
                settings.NavigationDisplayTrafficEnabled = ReadBool(node,
                    "navigationDisplayTrafficEnabled", true);
                settings.NavigationDisplayWindEnabled = ReadBool(node,
                    "navigationDisplayWindEnabled", true);
                settings.NavigationDisplayLandProfileSize = ReadEnum(node,
                    "navigationDisplayLandProfileSize",
                    AERISNavigationDisplayLandProfileSize.Normal);
                settings.NavigationDisplayLandOverlay = ReadBool(node,
                    "navigationDisplayLandOverlay", true);
                settings.NavigationDisplayNavOverlay = ReadBool(node,
                    "navigationDisplayNavOverlay", true);
                settings.NavigationDisplayLayoutCustomized = ReadBool(node,
                    "navigationDisplayLayoutCustomized", false);
                settings.NavigationDisplayRectX01 = Mathf.Clamp(ReadFloat(node,
                    "navigationDisplayRectX01", 0f), -2f, 2f);
                settings.NavigationDisplayRectY01 = Mathf.Clamp(ReadFloat(node,
                    "navigationDisplayRectY01", 0f), -2f, 2f);
                settings.NavigationDisplayRectW01 = Mathf.Clamp(ReadFloat(node,
                    "navigationDisplayRectW01", 380f / 1920f), 0.05f, 1.50f);
                settings.NavigationDisplayRectH01 = Mathf.Clamp(ReadFloat(node,
                    "navigationDisplayRectH01", 244f / 1080f), 0.05f, 1.50f);
                settings.FlightPlanSectionExpanded = ReadBool(node, "flightPlanSectionExpanded", true);
                settings.ApLateralSectionExpanded = ReadBool(node, "apLateralSectionExpanded", true);
                settings.ApVerticalSectionExpanded = ReadBool(node, "apVerticalSectionExpanded", true);
                settings.ApSpeedSectionExpanded = ReadBool(node, "apSpeedSectionExpanded", true);
                settings.ApPitchTargetDeg = Mathf.Clamp(ReadFloat(node, "apPitchTargetDeg", settings.ApPitchTargetDeg), -85f, 85f);
                settings.ApVerticalSpeedTargetMps = Mathf.Clamp(ReadFloat(node, "apVerticalSpeedTargetMps", settings.ApVerticalSpeedTargetMps), -100f, 100f);
                settings.ApVerticalSpeedMaxPitchDeg = Mathf.Clamp(ReadFloat(node, "apVerticalSpeedMaxPitchDeg", settings.ApVerticalSpeedMaxPitchDeg), 0f, 90f);
                settings.ApAltitudeTargetMeters = Mathf.Clamp(ReadFloat(node, "apAltitudeTargetMeters", settings.ApAltitudeTargetMeters), 0f, 1000000f);
                settings.ApAltitudeMaxVerticalSpeedMps = Mathf.Clamp(ReadFloat(node, "apAltitudeMaxVerticalSpeedMps", settings.ApAltitudeMaxVerticalSpeedMps), 0.5f, 100f);
                settings.ApAltitudeMaxPitchDeg = Mathf.Clamp(ReadFloat(node, "apAltitudeMaxPitchDeg", settings.ApAltitudeMaxPitchDeg), 0f, 90f);
                settings.ApAccelerationTargetMps2 = Mathf.Clamp(ReadFloat(node, "apAccelerationTargetMps2", settings.ApAccelerationTargetMps2), -30f, 30f);
                settings.ApVelocityTargetMps = Mathf.Clamp(ReadFloat(node, "apVelocityTargetMps", settings.ApVelocityTargetMps), 0f, 5000f);
                settings.ApVelocityTargetConfirmed = ReadBool(node, "apVelocityTargetConfirmed", false);
                settings.GroundAssistEnabled = ReadBool(node, "groundAssistEnabled", true);
                settings.GroundStabilityEnabled = ReadBool(node, "groundStabilityEnabled", true);
                settings.GroundBrakeAssistAuto = ReadBool(node, "groundBrakeAssistAuto", true);
                settings.GroundAirbrakeLinkAuto = ReadBool(node, "groundAirbrakeLinkAuto", true);
                settings.GroundAirbrakeLimit = Mathf.Clamp01(ReadFloat(node, "groundAirbrakeLimit", 1.0f));
                settings.GroundTargetDecelerationMps2 = Mathf.Clamp(ReadFloat(node, "groundTargetDecelerationMps2", 3.0f), 0.5f, 8f);
                settings.GroundMaximumNormalDecelerationMps2 = Mathf.Clamp(ReadFloat(node, "groundMaximumNormalDecelerationMps2", 5.0f), 3f, 8f);
                settings.GroundBrakeFallbackDelaySeconds = Mathf.Clamp(ReadFloat(node, "groundBrakeFallbackDelaySeconds", 1.0f), 0.5f, 3f);
                settings.GroundMaxBrake = Mathf.Clamp01(ReadFloat(node, "groundMaxBrake", 1.0f));
                settings.GroundLowSpeedFadeMps = Mathf.Clamp(ReadFloat(node, "groundLowSpeedFadeMps", 8.0f), 2f, 30f);
                int protectDefaultPolicyRevision = ReadInt(node,
                    "protectDefaultPolicyRevision", 0);
                if (protectDefaultPolicyRevision < CurrentProtectDefaultPolicyRevision)
                {
                    // Candidate 13 changes the product defaults. Apply once to legacy
                    // installs; subsequent explicit OFF choices are preserved.
                    settings.GroundParkingHold = true;
                    settings.GroundReverseThrustAuto = true;
                    settings.ProtectDefaultPolicyRevision = CurrentProtectDefaultPolicyRevision;
                    saveSettingsMigration = true;
                }
                else
                {
                    settings.ProtectDefaultPolicyRevision = protectDefaultPolicyRevision;
                    settings.GroundParkingHold = ReadBool(node, "groundParkingHold", true);
                    settings.GroundReverseThrustAuto = ReadBool(node, "groundReverseThrustAuto", true);
                }
                settings.GroundDragChuteAuto = ReadBool(node, "groundDragChuteAuto", false);
                settings.AutoTakeoffStallFactor = Mathf.Clamp(ReadFloat(node, "autoTakeoffStallFactor", settings.AutoTakeoffStallFactor), 1.05f, 1.50f);
                settings.AutoTakeoffManualVrMps = Mathf.Clamp(ReadFloat(node, "autoTakeoffManualVrMps", settings.AutoTakeoffManualVrMps), 15f, 250f);
                settings.AutoTakeoffRotationPitchDeg = Mathf.Clamp(ReadFloat(node, "autoTakeoffRotationPitchDeg", settings.AutoTakeoffRotationPitchDeg), 3f, 20f);
                settings.AutoTakeoffRotationRateDegPerSec = Mathf.Clamp(ReadFloat(node, "autoTakeoffRotationRateDegPerSec", settings.AutoTakeoffRotationRateDegPerSec), 0.5f, 5f);
                settings.AutoTakeoffInitialClimbVsMps = Mathf.Clamp(ReadFloat(node, "autoTakeoffInitialClimbVsMps", settings.AutoTakeoffInitialClimbVsMps), 2f, 50f);
                settings.AutoTakeoffHandoffRadarAltitudeM = Mathf.Clamp(ReadFloat(node, "autoTakeoffHandoffRadarAltitudeM", settings.AutoTakeoffHandoffRadarAltitudeM), 20f, 250f);
                settings.ShowVirtualAttitudeInstrument = ReadBool(node, "showVirtualAttitudeInstrument", true);
                settings.ShowApErrorDisplay = ReadBool(node, "showApErrorDisplay", true);
                settings.ShowFlightStatusDisplay = ReadBool(node, "showFlightStatusDisplay", true);
                AERISDisplayMode flightInstrumentFallback =
                    node.HasValue("showFlightInstrumentWithoutGuidance")
                        ? (ReadBool(node, "showFlightInstrumentWithoutGuidance", false)
                            ? AERISDisplayMode.Always : AERISDisplayMode.Automatic)
                        : AERISDisplayMode.Automatic;
                settings.FlightInstrumentDisplayMode = ReadDisplayMode(node,
                    "flightInstrumentDisplayMode", flightInstrumentFallback);
                int displayPolicyRevision = ReadInt(node, "displayPolicyRevision", 0);
                if (displayPolicyRevision < CurrentDisplayPolicyRevision)
                {
                    // The previous model had no OFF state and shipped ALWAYS as the
                    // factory default. Migrate that old default once to demand-driven
                    // AUTO; a user may select ALWAYS again after this revision is saved.
                    if (settings.NavigationDisplayMode == AERISDisplayMode.Always)
                        settings.NavigationDisplayMode = AERISDisplayMode.Automatic;
                    if (settings.FlightInstrumentDisplayMode == AERISDisplayMode.Always)
                        settings.FlightInstrumentDisplayMode = AERISDisplayMode.Automatic;
                    settings.DisplayPolicyRevision = CurrentDisplayPolicyRevision;
                    saveSettingsMigration = true;
                    AERISLogger.Info("[DISPLAY_POLICY_MIGRATION] revision=" +
                        displayPolicyRevision + "->" + CurrentDisplayPolicyRevision +
                        "; ND=" + settings.NavigationDisplayMode + "; FDI=" +
                        settings.FlightInstrumentDisplayMode + "; OFF_AVAILABLE=True");
                }
                else settings.DisplayPolicyRevision = CurrentDisplayPolicyRevision;
                settings.FlightInstrumentLayoutCustomized = ReadBool(node,
                    "flightInstrumentLayoutCustomized", false);
                settings.FlightInstrumentRectX01 = Mathf.Clamp(ReadFloat(node,
                    "flightInstrumentRectX01", 0f), -2f, 2f);
                settings.FlightInstrumentRectY01 = Mathf.Clamp(ReadFloat(node,
                    "flightInstrumentRectY01", 0f), -2f, 2f);
                settings.FlightInstrumentRectW01 = Mathf.Clamp(ReadFloat(node,
                    "flightInstrumentRectW01", 380f / 1920f), 0.05f, 1.50f);
                settings.FlightInstrumentRectH01 = Mathf.Clamp(ReadFloat(node,
                    "flightInstrumentRectH01", 244f / 1080f), 0.05f, 1.50f);
                settings.ToggleWindowPrimary = ReadKey(node, "toggleWindowPrimary", settings.ToggleWindowPrimary);
                settings.ToggleWindowSecondary = ReadKey(node, "toggleWindowSecondary", settings.ToggleWindowSecondary);
                settings.ToggleMasterPrimary = ReadKey(node, "toggleMasterPrimary", settings.ToggleMasterPrimary);
                settings.ToggleMasterSecondary = ReadKey(node, "toggleMasterSecondary", settings.ToggleMasterSecondary);
                settings.EmergencyDisablePrimary = ReadKey(node, "emergencyDisablePrimary", settings.EmergencyDisablePrimary);
                settings.EmergencyDisableSecondary = ReadKey(node, "emergencyDisableSecondary", settings.EmergencyDisableSecondary);
            }
            catch (Exception ex) { AERISLogger.Warn("Settings load failed: " + ex.Message); }
            if (saveSettingsMigration) settings.Save();
            return settings;
        }

        internal void ResetToDefaults()
        {
            MainWindowX = 260f;
            MainWindowY = 120f;
            MainWindowWidth = DefaultMainWindowWidth;
            MainWindowHeight = DefaultMainWindowHeight;
            MainWindowVisible = true;
            DebugVisible = false;
            ShowSasWarning = true;
            SoftAssistArmed = false;
            SoftAssistStrength = "Low";
            SoftAssistPitchEnabled = true;
            SoftAssistRollEnabled = true;
            SoftAssistYawEnabled = true;
            ProtectAoAWarningDegrees = 8f;
            ProtectAoALimitDegrees = 12f;
            AoADebugForceTrigger = false;
            AoADebugTraceEnabled = true;
            DebugFlightOverlayVisible = false;
            AppIntegrationEnabled = true;
            EnableAABackgroundTraining = true;
            AllowAASpeedAutoBrakes = false;
            EnableAAComparisonTelemetry = true;
            FlightDataArchiveLimit = 10;
            PerformanceWorkerOverride = 0;
            PerformanceGpuAccelerationEnabled = true;
            VelocityAccelerationLimitMps2 = 4.0f;
            SpeedAutomaticAirbrakeEnabled = true;
            ThinAirTurnAssistEnabled = true;
            ApBankTargetDeg = 0f;
            ApHeadingTargetDeg = 0f;
            ApHeadingAutoBankLimit = true;
            ApHeadingManualBankLimitDeg = 30f;
            FlightPlanSelectedBody = string.Empty;
            FlightPlanSelectedId = string.Empty;
            LandSelectedAirfieldId = string.Empty;
            LandSelectedDirectionId = string.Empty;
            LandSelectionExplicitlyCleared = true;
            LandSectionExpanded = true;
            AirfieldsUiLayoutRevision = CurrentAirfieldsUiLayoutRevision;
            AirfieldsCertifiedExpanded = false;
            AirfieldsUserCalibratedExpanded = false;
            AirfieldsFailedExpanded = false;
            AirfieldsPendingExpanded = false;
            AirfieldsRevalidationExpanded = false;
            AirfieldsProvisionalExpanded = false;
            NavigationDisplayMode = AERISDisplayMode.Automatic;
            NavigationDisplayTrackUp = true;
            NavigationDisplayAutoRange = false;
            NavigationDisplayManualRangeMeters = 20000f;
            TerrainQualityMode = AERISTerrainQualityMode.Low;
            TerrainLandRuntimeQualityEnabled = false;
            TerrainQualityModelRevision = CurrentTerrainQualityModelRevision;
            DisplayPolicyRevision = CurrentDisplayPolicyRevision;
            NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
            TerrainDisplayMode = AERISTerrainDisplayMode.Automatic;
            TerrainGpuMode = AERISTerrainGpuMode.Automatic;
            TerrainRenderTargetOrientation = AERISTerrainRenderTargetOrientation.Direct;
            TerrainColourPreset = AERISTerrainColourPreset.Standard;
            TerrainRamCacheLimitMiB = 0;
            TerrainDiskCacheLimitMiB = 0;
            TerrainVramCacheLimitMiB = 0;
            TerrainPreloadEnabled = true;
            TerrainContoursEnabled = true;
            TerrainShadingEnabled = true;
            NavigationDisplayTrailEnabled = false;
            NavigationDisplayTrackVectorEnabled = true;
            NavigationDisplayTrafficEnabled = true;
            NavigationDisplayWindEnabled = true;
            NavigationDisplayLandProfileSize = AERISNavigationDisplayLandProfileSize.Normal;
            NavigationDisplayLandOverlay = true;
            NavigationDisplayNavOverlay = true;
            NavigationDisplayLayoutCustomized = false;
            NavigationDisplayRectX01 = 0f;
            NavigationDisplayRectY01 = 0f;
            NavigationDisplayRectW01 = 380f / 1920f;
            NavigationDisplayRectH01 = 244f / 1080f;
            FlightPlanSectionExpanded = true;
            ApLateralSectionExpanded = true;
            ApVerticalSectionExpanded = true;
            ApSpeedSectionExpanded = true;
            ApPitchTargetDeg = 0f;
            ApVerticalSpeedTargetMps = 0f;
            ApVerticalSpeedMaxPitchDeg = 20f;
            ApAltitudeTargetMeters = 1000f;
            ApAltitudeMaxVerticalSpeedMps = 50f;
            ApAltitudeMaxPitchDeg = 20f;
            ApAccelerationTargetMps2 = 0f;
            ApVelocityTargetMps = 0f;
            ApVelocityTargetConfirmed = false;
            GroundAssistEnabled = true;
            GroundStabilityEnabled = true;
            GroundBrakeAssistAuto = true;
            GroundAirbrakeLinkAuto = true;
            GroundAirbrakeLimit = 1.0f;
            GroundTargetDecelerationMps2 = 3.0f;
            GroundMaximumNormalDecelerationMps2 = 5.0f;
            GroundBrakeFallbackDelaySeconds = 1.0f;
            GroundMaxBrake = 1.0f;
            GroundLowSpeedFadeMps = 8.0f;
            ProtectDefaultPolicyRevision = CurrentProtectDefaultPolicyRevision;
            GroundParkingHold = true;
            GroundDragChuteAuto = false;
            GroundReverseThrustAuto = true;
            AutoTakeoffStallFactor = 1.15f;
            AutoTakeoffManualVrMps = 70f;
            AutoTakeoffRotationPitchDeg = 10f;
            AutoTakeoffRotationRateDegPerSec = 2.5f;
            AutoTakeoffInitialClimbVsMps = 20f;
            AutoTakeoffHandoffRadarAltitudeM = 50f;
            ShowVirtualAttitudeInstrument = true;
            ShowApErrorDisplay = true;
            ShowFlightStatusDisplay = true;
            FlightInstrumentDisplayMode = AERISDisplayMode.Automatic;
            FlightInstrumentLayoutCustomized = false;
            FlightInstrumentRectX01 = 0f;
            FlightInstrumentRectY01 = 0f;
            FlightInstrumentRectW01 = 380f / 1920f;
            FlightInstrumentRectH01 = 244f / 1080f;
            ToggleWindowPrimary = KeyCode.LeftAlt;
            ToggleWindowSecondary = KeyCode.A;
            ToggleMasterPrimary = KeyCode.LeftAlt;
            ToggleMasterSecondary = KeyCode.F;
            EmergencyDisablePrimary = KeyCode.LeftAlt;
            EmergencyDisableSecondary = KeyCode.X;
        }

        internal void GetShortcut(AERISShortcutAction action, out KeyCode primary, out KeyCode secondary)
        {
            switch (action)
            {
                case AERISShortcutAction.ToggleMaster:
                    primary = ToggleMasterPrimary; secondary = ToggleMasterSecondary; return;
                case AERISShortcutAction.EmergencyDisable:
                    primary = EmergencyDisablePrimary; secondary = EmergencyDisableSecondary; return;
                default:
                    primary = ToggleWindowPrimary; secondary = ToggleWindowSecondary; return;
            }
        }

        internal bool IsShortcutTriggered(AERISShortcutAction action)
        {
            KeyCode primary, secondary;
            GetShortcut(action, out primary, out secondary);
            if (primary == KeyCode.None) return false;
            bool primaryHeld = Input.GetKey(primary);
            bool secondaryRequired = secondary != KeyCode.None;
            bool secondaryHeld = !secondaryRequired || Input.GetKey(secondary);
            if (!primaryHeld || !secondaryHeld) return false;
            return Input.GetKeyDown(primary) || (secondaryRequired && Input.GetKeyDown(secondary));
        }

        internal string GetShortcutDisplay(AERISShortcutAction action)
        {
            KeyCode primary, secondary;
            GetShortcut(action, out primary, out secondary);
            if (primary == KeyCode.None) return "UNBOUND";
            return secondary == KeyCode.None ? primary.ToString() : primary + " + " + secondary;
        }

        internal bool TrySetShortcut(AERISShortcutAction action, KeyCode primary, KeyCode secondary, out string error)
        {
            error = null;
            if (primary == KeyCode.None)
            {
                error = "Primary key is required.";
                return false;
            }
            if (secondary == primary) secondary = KeyCode.None;
            if (ShortcutMatchesOther(action, primary, secondary))
            {
                error = "This shortcut is already assigned to another AERIS command.";
                return false;
            }
            switch (action)
            {
                case AERISShortcutAction.ToggleMaster:
                    ToggleMasterPrimary = primary; ToggleMasterSecondary = secondary; break;
                case AERISShortcutAction.EmergencyDisable:
                    EmergencyDisablePrimary = primary; EmergencyDisableSecondary = secondary; break;
                default:
                    ToggleWindowPrimary = primary; ToggleWindowSecondary = secondary; break;
            }
            return true;
        }

        internal void ClearShortcut(AERISShortcutAction action)
        {
            switch (action)
            {
                case AERISShortcutAction.ToggleMaster:
                    ToggleMasterPrimary = KeyCode.None; ToggleMasterSecondary = KeyCode.None; break;
                case AERISShortcutAction.EmergencyDisable:
                    EmergencyDisablePrimary = KeyCode.None; EmergencyDisableSecondary = KeyCode.None; break;
                default:
                    ToggleWindowPrimary = KeyCode.None; ToggleWindowSecondary = KeyCode.None; break;
            }
        }

        bool ShortcutMatchesOther(AERISShortcutAction edited, KeyCode primary, KeyCode secondary)
        {
            for (int i = 0; i < 3; i++)
            {
                var action = (AERISShortcutAction)i;
                if (action == edited) continue;
                KeyCode otherPrimary, otherSecondary;
                GetShortcut(action, out otherPrimary, out otherSecondary);
                if (otherPrimary == KeyCode.None) continue;
                if ((primary == otherPrimary && secondary == otherSecondary) ||
                    (secondary != KeyCode.None && primary == otherSecondary && secondary == otherPrimary)) return true;
            }
            return false;
        }

        internal void Save()
        {
            try
            {
                var node = new ConfigNode("AERIS_SETTINGS");
                node.AddValue("logVerbosity", "Info");
                node.AddValue("mainWindowX", MainWindowX.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("mainWindowY", MainWindowY.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("mainWindowWidth", MainWindowWidth.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("mainWindowHeight", MainWindowHeight.ToString("F0", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("mainWindowVisible", MainWindowVisible);
                node.AddValue("debugVisible", DebugVisible);
                node.AddValue("showSasWarning", ShowSasWarning);
                node.AddValue("softAssistArmed", SoftAssistArmed);
                node.AddValue("softAssistStrength", SoftAssistStrength);
                node.AddValue("softAssistPitchEnabled", SoftAssistPitchEnabled);
                node.AddValue("softAssistRollEnabled", SoftAssistRollEnabled);
                node.AddValue("softAssistYawEnabled", SoftAssistYawEnabled);
                node.AddValue("protectAoAWarningDegrees", ProtectAoAWarningDegrees.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("protectAoALimitDegrees", ProtectAoALimitDegrees.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("aoaDebugForceTrigger", AoADebugForceTrigger);
                node.AddValue("aoaDebugTraceEnabled", AoADebugTraceEnabled);
                node.AddValue("debugFlightOverlayVisible", DebugFlightOverlayVisible);
                node.AddValue("appIntegrationEnabled", AppIntegrationEnabled);
                node.AddValue("enableAABackgroundTraining", EnableAABackgroundTraining);
                node.AddValue("allowAASpeedAutoBrakes", AllowAASpeedAutoBrakes);
                node.AddValue("enableAAComparisonTelemetry", EnableAAComparisonTelemetry);
                node.AddValue("flightDataArchiveLimit",
                    NormalizeFlightDataArchiveLimit(FlightDataArchiveLimit));
                node.AddValue("performanceWorkerOverride", PerformanceWorkerOverride);
                node.AddValue("performanceGpuAccelerationEnabled", PerformanceGpuAccelerationEnabled);
                node.AddValue("velocityAccelerationLimitMps2", VelocityAccelerationLimitMps2.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("speedAutomaticAirbrakeEnabled", SpeedAutomaticAirbrakeEnabled);
                node.AddValue("thinAirTurnAssistEnabled", ThinAirTurnAssistEnabled);
                node.AddValue("apBankTargetDeg", ApBankTargetDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apHeadingTargetDeg", ApHeadingTargetDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apHeadingAutoBankLimit", ApHeadingAutoBankLimit);
                node.AddValue("apHeadingManualBankLimitDeg", ApHeadingManualBankLimitDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("flightPlanSelectedBody", FlightPlanSelectedBody ?? string.Empty);
                node.AddValue("flightPlanSelectedId", FlightPlanSelectedId ?? string.Empty);
                node.AddValue("landSelectedAirfieldId", LandSelectedAirfieldId ?? string.Empty);
                node.AddValue("landSelectedDirectionId", LandSelectedDirectionId ?? string.Empty);
                node.AddValue("landSelectionExplicitlyCleared",
                    LandSelectionExplicitlyCleared);
                node.AddValue("landSectionExpanded", LandSectionExpanded);
                node.AddValue("airfieldsUiLayoutRevision", AirfieldsUiLayoutRevision);
                node.AddValue("airfieldsCertifiedExpanded", AirfieldsCertifiedExpanded);
                node.AddValue("airfieldsUserCalibratedExpanded",
                    AirfieldsUserCalibratedExpanded);
                node.AddValue("airfieldsFailedExpanded", AirfieldsFailedExpanded);
                node.AddValue("airfieldsPendingExpanded", AirfieldsPendingExpanded);
                node.AddValue("airfieldsRevalidationExpanded", AirfieldsRevalidationExpanded);
                node.AddValue("airfieldsProvisionalExpanded", AirfieldsProvisionalExpanded);
                node.AddValue("displayPolicyRevision", CurrentDisplayPolicyRevision);
                node.AddValue("navigationDisplayMode", NavigationDisplayMode);
                node.AddValue("navigationDisplayTrackUp", NavigationDisplayTrackUp);
                node.AddValue("navigationDisplayAutoRange", false);
                NavigationDisplayManualRangeMeters = NormalizeNavigationRange(
                    NavigationDisplayManualRangeMeters);
                node.AddValue("navigationDisplayManualRangeMeters",
                    NavigationDisplayManualRangeMeters.ToString("F0",
                    System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("terrainQualityModelRevision",
                    CurrentTerrainQualityModelRevision);
                TerrainQualityMode = AERISTerrainQualityMode.Low;
                TerrainLandRuntimeQualityEnabled = false;
                NavigationDisplayUpdateMode = AERISNavigationDisplayUpdateMode.Fps10;
                node.AddValue("terrainQualityMode", TerrainQualityMode);
                node.AddValue("terrainLandRuntimeQualityEnabled", false);
                node.AddValue("navigationDisplayUpdateMode", NavigationDisplayUpdateMode);
                node.AddValue("terrainDisplayMode", TerrainDisplayMode);
                node.AddValue("terrainGpuMode", TerrainGpuMode);
                node.AddValue("terrainRenderTargetOrientation",
                    TerrainRenderTargetOrientation);
                node.AddValue("terrainColourPreset", TerrainColourPreset);
                node.AddValue("terrainRamCacheLimitMiB", NormalizeCacheLimit(
                    TerrainRamCacheLimitMiB, 64, 4096));
                node.AddValue("terrainDiskCacheLimitMiB", NormalizeCacheLimit(
                    TerrainDiskCacheLimitMiB, 256, 32768));
                node.AddValue("terrainVramCacheLimitMiB", NormalizeCacheLimit(
                    TerrainVramCacheLimitMiB, 32, 4096));
                node.AddValue("terrainPreloadEnabled", TerrainPreloadEnabled);
                node.AddValue("terrainContoursEnabled", TerrainContoursEnabled);
                node.AddValue("terrainShadingEnabled", TerrainShadingEnabled);
                node.AddValue("navigationDisplayTrailEnabled",
                    NavigationDisplayTrailEnabled);
                node.AddValue("navigationDisplayTrackVectorEnabled",
                    NavigationDisplayTrackVectorEnabled);
                node.AddValue("navigationDisplayTrafficEnabled",
                    NavigationDisplayTrafficEnabled);
                node.AddValue("navigationDisplayWindEnabled",
                    NavigationDisplayWindEnabled);
                node.AddValue("navigationDisplayLandProfileSize",
                    NavigationDisplayLandProfileSize);
                node.AddValue("navigationDisplayLandOverlay", NavigationDisplayLandOverlay);
                node.AddValue("navigationDisplayNavOverlay", NavigationDisplayNavOverlay);
                node.AddValue("navigationDisplayLayoutCustomized", NavigationDisplayLayoutCustomized);
                node.AddValue("navigationDisplayRectX01", NavigationDisplayRectX01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("navigationDisplayRectY01", NavigationDisplayRectY01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("navigationDisplayRectW01", NavigationDisplayRectW01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("navigationDisplayRectH01", NavigationDisplayRectH01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("flightPlanSectionExpanded", FlightPlanSectionExpanded);
                node.AddValue("apLateralSectionExpanded", ApLateralSectionExpanded);
                node.AddValue("apVerticalSectionExpanded", ApVerticalSectionExpanded);
                node.AddValue("apSpeedSectionExpanded", ApSpeedSectionExpanded);
                node.AddValue("apPitchTargetDeg", ApPitchTargetDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apVerticalSpeedTargetMps", ApVerticalSpeedTargetMps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apVerticalSpeedMaxPitchDeg", ApVerticalSpeedMaxPitchDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apAltitudeTargetMeters", ApAltitudeTargetMeters.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apAltitudeMaxVerticalSpeedMps", ApAltitudeMaxVerticalSpeedMps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apAltitudeMaxPitchDeg", ApAltitudeMaxPitchDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apAccelerationTargetMps2", ApAccelerationTargetMps2.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apVelocityTargetMps", ApVelocityTargetMps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("apVelocityTargetConfirmed", ApVelocityTargetConfirmed);
                node.AddValue("groundAssistEnabled", GroundAssistEnabled);
                node.AddValue("groundStabilityEnabled", GroundStabilityEnabled);
                node.AddValue("groundBrakeAssistAuto", GroundBrakeAssistAuto);
                node.AddValue("groundAirbrakeLinkAuto", GroundAirbrakeLinkAuto);
                node.AddValue("groundAirbrakeLimit", GroundAirbrakeLimit.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("groundTargetDecelerationMps2", GroundTargetDecelerationMps2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("groundMaximumNormalDecelerationMps2", GroundMaximumNormalDecelerationMps2.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("groundBrakeFallbackDelaySeconds", GroundBrakeFallbackDelaySeconds.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("groundMaxBrake", GroundMaxBrake.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("groundLowSpeedFadeMps", GroundLowSpeedFadeMps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("protectDefaultPolicyRevision", CurrentProtectDefaultPolicyRevision);
                node.AddValue("groundParkingHold", GroundParkingHold);
                node.AddValue("groundDragChuteAuto", GroundDragChuteAuto);
                node.AddValue("groundReverseThrustAuto", GroundReverseThrustAuto);
                node.AddValue("autoTakeoffStallFactor", AutoTakeoffStallFactor.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("autoTakeoffManualVrMps", AutoTakeoffManualVrMps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("autoTakeoffRotationPitchDeg", AutoTakeoffRotationPitchDeg.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("autoTakeoffRotationRateDegPerSec", AutoTakeoffRotationRateDegPerSec.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("autoTakeoffInitialClimbVsMps", AutoTakeoffInitialClimbVsMps.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("autoTakeoffHandoffRadarAltitudeM", AutoTakeoffHandoffRadarAltitudeM.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("showVirtualAttitudeInstrument", ShowVirtualAttitudeInstrument);
                node.AddValue("showApErrorDisplay", ShowApErrorDisplay);
                node.AddValue("showFlightStatusDisplay", ShowFlightStatusDisplay);
                node.AddValue("flightInstrumentDisplayMode", FlightInstrumentDisplayMode);
                node.AddValue("flightInstrumentLayoutCustomized", FlightInstrumentLayoutCustomized);
                node.AddValue("flightInstrumentRectX01", FlightInstrumentRectX01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("flightInstrumentRectY01", FlightInstrumentRectY01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("flightInstrumentRectW01", FlightInstrumentRectW01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("flightInstrumentRectH01", FlightInstrumentRectH01.ToString("F6", System.Globalization.CultureInfo.InvariantCulture));
                node.AddValue("toggleWindowPrimary", ToggleWindowPrimary);
                node.AddValue("toggleWindowSecondary", ToggleWindowSecondary);
                node.AddValue("toggleMasterPrimary", ToggleMasterPrimary);
                node.AddValue("toggleMasterSecondary", ToggleMasterSecondary);
                node.AddValue("emergencyDisablePrimary", EmergencyDisablePrimary);
                node.AddValue("emergencyDisableSecondary", EmergencyDisableSecondary);
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathName));
                node.Save(PathName);
            }
            catch (Exception ex) { AERISLogger.Warn("Settings save failed: " + ex.Message); }
        }

        private static AERISDisplayMode ReadDisplayMode(ConfigNode node, string key,
            AERISDisplayMode fallback)
        {
            if (!node.HasValue(key)) return fallback;
            AERISDisplayMode value;
            return Enum.TryParse(node.GetValue(key), true, out value) &&
                (value == AERISDisplayMode.Automatic ||
                 value == AERISDisplayMode.Always ||
                 value == AERISDisplayMode.Off)
                ? value : fallback;
        }

        private static AERISTerrainQualityMode ReadTerrainQualityMode(ConfigNode node,
            string key, AERISTerrainQualityMode fallback)
        {
            if (!node.HasValue(key)) return fallback;
            return ParseTerrainQualityMode(node.GetValue(key), fallback, false);
        }

        private static AERISTerrainQualityMode MigrateLegacyTerrainQualityMode(
            string raw, AERISTerrainQualityMode fallback)
        {
            // One-time CP2 -> CP2.5 migration contract:
            // AUTO -> AUTO, ECO -> LOW, BALANCED -> MEDIUM, HIGH -> HIGH,
            // ULTRA -> HIGH. LAND is never enabled by migration.
            return ParseTerrainQualityMode(raw, fallback, true);
        }

        private static AERISTerrainQualityMode ParseTerrainQualityMode(string raw,
            AERISTerrainQualityMode fallback, bool legacyMigration)
        {
            string value = (raw ?? string.Empty).Trim().ToUpperInvariant();
            switch (value)
            {
                case "AUTO":
                case "AUTOMATIC": return AERISTerrainQualityMode.Automatic;
                case "ECO":
                case "LOW": return AERISTerrainQualityMode.Low;
                case "BAL":
                case "BALANCED":
                case "MEDIUM": return AERISTerrainQualityMode.Medium;
                case "HIGH": return AERISTerrainQualityMode.High;
                case "ULTRA": return AERISTerrainQualityMode.High;
                // Gate 2 packages persisted LAND as a global profile. Gate 3 always
                // migrates that token to HIGH and carries activation in the separate
                // terrainLandRuntimeQualityEnabled capability flag.
                case "LAND": return AERISTerrainQualityMode.High;
                default: return fallback;
            }
        }

        internal static float NormalizeNavigationRange(float value)
        {
            int bestIndex = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < NavigationDisplayRangeStepsMeters.Length; i++)
            {
                float distance = Mathf.Abs(NavigationDisplayRangeStepsMeters[i] - value);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }
            return NavigationDisplayRangeStepsMeters[bestIndex];
        }

        private static AERISNavigationDisplayUpdateMode ReadNavigationDisplayUpdateMode(
            ConfigNode node, string key, AERISNavigationDisplayUpdateMode fallback)
        {
            if (!node.HasValue(key)) return fallback;
            AERISNavigationDisplayUpdateMode value;
            return Enum.TryParse(node.GetValue(key), true, out value) &&
                Enum.IsDefined(typeof(AERISNavigationDisplayUpdateMode), value) ? value : fallback;
        }

        private static T ReadEnum<T>(ConfigNode node, string key, T fallback) where T : struct
        {
            if (!node.HasValue(key)) return fallback;
            T value;
            return Enum.TryParse<T>(node.GetValue(key), true, out value) &&
                Enum.IsDefined(typeof(T), value) ? value : fallback;
        }

        private static int NormalizeCacheLimit(int value, int minimum, int maximum)
        {
            return value <= 0 ? 0 : Math.Min(maximum, Math.Max(minimum, value));
        }

        internal static int NormalizePreloadStorageLimit(int value)
        {
            if (value <= 0) return 0;
            int[] allowed = { 512, 1024, 2048, 5120, 10240, 20480 };
            int best = allowed[0];
            int distance = Math.Abs(value - best);
            for (int i = 1; i < allowed.Length; i++)
            {
                int candidateDistance = Math.Abs(value - allowed[i]);
                if (candidateDistance < distance)
                {
                    best = allowed[i];
                    distance = candidateDistance;
                }
            }
            return best;
        }

        private static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            bool value; return node.HasValue(key) && bool.TryParse(node.GetValue(key), out value) ? value : fallback;
        }
        internal static int NormalizeFlightDataArchiveLimit(int value)
        {
            return Math.Max(1, Math.Min(30, value));
        }

        private static float ReadFloat(ConfigNode node, string key, float fallback)
        {
            if (!node.HasValue(key)) return fallback;
            float value;
            string text = node.GetValue(key);
            if (float.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value) && IsFinite(value)) return value;
            return float.TryParse(text, out value) && IsFinite(value) ? value : fallback;
        }

        private static int ReadInt(ConfigNode node, string key, int fallback)
        {
            if (!node.HasValue(key)) return fallback;
            int value;
            return int.TryParse(node.GetValue(key), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
        }

        private static int NormalizeWorkerOverride(int value)
        {
            return value <= 0 ? 0 : Math.Min(256, Math.Max(2, value));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
        private static string ReadString(ConfigNode node, string key, string fallback)
        {
            return node.HasValue(key) ? node.GetValue(key) : fallback;
        }
        private static KeyCode ReadKey(ConfigNode node, string key, KeyCode fallback)
        {
            if (!node.HasValue(key)) return fallback;
            KeyCode value;
            return Enum.TryParse<KeyCode>(node.GetValue(key), true, out value) ? value : fallback;
        }
    }
}
