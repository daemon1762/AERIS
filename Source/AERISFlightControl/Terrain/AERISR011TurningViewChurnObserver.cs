using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;
using AERISFlightControl.Settings;
using AERISFlightControl.UI;

namespace AERISFlightControl.Terrain
{
    // OH REV3.5 R011: read-only diagnostic observer for turning-view churn.
    // This class deliberately owns no terrain, AP, LAND, worker, cache, render or
    // presentation authority. It only samples the already-published R010 state at
    // the same nominal 10 Hz cadence and emits one compact five-second summary.
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class AERISR011TurningViewChurnObserver : MonoBehaviour
    {
        const float SampleIntervalSeconds = 0.10f;
        const float LogIntervalSeconds = 5.0f;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        static readonly FieldInfo BootstrapFlightInstrumentField =
            typeof(AERISBootstrap).GetField("flightInstrument", PrivateInstance);
        static readonly FieldInfo FlightInstrumentNavigationDisplayField =
            typeof(AERISFlightInstrument).GetField("navigationDisplay", PrivateInstance);
        static readonly FieldInfo NavigationDisplayRendererField =
            typeof(AERISNavigationDisplay).GetField("terrainTileRenderer", PrivateInstance);
        static readonly FieldInfo NavigationDisplayPlanModeField =
            typeof(AERISNavigationDisplay).GetField("planMode", PrivateInstance);
        static readonly FieldInfo NavigationDisplayPlanLatitudeField =
            typeof(AERISNavigationDisplay).GetField("planCenterLatitudeDeg", PrivateInstance);
        static readonly FieldInfo NavigationDisplayPlanLongitudeField =
            typeof(AERISNavigationDisplay).GetField("planCenterLongitudeDeg", PrivateInstance);
        static readonly FieldInfo NavigationDisplayMapHeadingField =
            typeof(AERISNavigationDisplay).GetField("cachedFallbackMapHeading", PrivateInstance);

        static readonly FieldInfo ContentSnapshotValidField = RendererField("contentSnapshotValid");
        static readonly FieldInfo ContentVisibleField = RendererField("contentVisible");
        static readonly FieldInfo ContentTerrainGenerationField = RendererField("contentTerrainGeneration");
        static readonly FieldInfo ContentStyleKeyField = RendererField("contentStyleKey");
        static readonly FieldInfo ContentCenterLatitudeField = RendererField("contentCenterLatitudeDeg");
        static readonly FieldInfo ContentCenterLongitudeField = RendererField("contentCenterLongitudeDeg");
        static readonly FieldInfo ContentRangeField = RendererField("contentRangeMeters");
        static readonly FieldInfo ContentHeadingField = RendererField("contentHeadingDeg");
        static readonly FieldInfo ContentTrackUpField = RendererField("contentTrackUp");
        static readonly FieldInfo ContentAnchorField = RendererField("contentAnchorV");
        static readonly FieldInfo ContentOrientationField = RendererField("contentOrientation");

        static readonly FieldInfo FrontBufferValidField = RendererField("frontBufferValid");
        static readonly FieldInfo FrontTerrainGenerationField = RendererField("frontTerrainGeneration");
        static readonly FieldInfo FrontViewGenerationField = RendererField("frontViewGeneration");
        static readonly FieldInfo FrontContentRevisionField = RendererField("frontContentRevision");
        static readonly FieldInfo GpuContentRevisionField = RendererField("gpuContentRevision");
        static readonly FieldInfo FrontCenterLatitudeField = RendererField("frontCenterLatitudeDeg");
        static readonly FieldInfo FrontCenterLongitudeField = RendererField("frontCenterLongitudeDeg");
        static readonly FieldInfo FrontMapHeadingField = RendererField("frontMapHeadingDeg");

        static readonly FieldInfo ContentTicksField = RendererField("operationHealthContentTicks");
        static readonly FieldInfo ContentCapturesField = RendererField("operationHealthContentCaptures");
        static readonly FieldInfo ResolveCallsField = RendererField("operationHealthResolveCalls");
        static readonly FieldInfo DirtyBatchesField = RendererField("operationHealthDirtyBatches");
        static readonly FieldInfo DirtyCoalescedField = RendererField("operationHealthDirtySignalsCoalesced");
        static readonly FieldInfo DirtyCommitsField = RendererField("operationHealthDirtyCommits");
        static readonly FieldInfo ViewInvalidationsField = RendererField("operationHealthViewInvalidations");
        static readonly FieldInfo MotionRefreshesField = RendererField("operationHealthMotionRefreshes");
        static readonly FieldInfo ForcedProjectionRefreshesField = RendererField("operationHealthForcedProjectionRefreshes");
        static readonly FieldInfo ProjectionExactRefreshesField = RendererField("operationHealthProjectionExactRefreshes");
        static readonly FieldInfo ProjectionBridgeUsesField = RendererField("operationHealthProjectionBridgeUses");
        static readonly FieldInfo BackRenderFramesField = RendererField("backRenderFrames");
        static readonly FieldInfo SkippedBackRenderFramesField = RendererField("skippedBackRenderFrames");
        static readonly FieldInfo FrontBufferSwapsField = RendererField("frontBufferSwaps");

        AERISBootstrap core;
        AERISNavigationDisplay navigationDisplay;
        AERISTerrainGpuTileRenderer renderer;
        Type visibleType;
        FieldInfo visibleTerrainGenerationField;
        FieldInfo visibleViewGenerationField;
        PropertyInfo visibleTerrainGenerationProperty;
        PropertyInfo visibleViewGenerationProperty;
        float nextSampleRealtime;
        float nextLogRealtime;
        bool bindingWarningLogged;
        bool rawCounterBaselineValid;

        long lastContentTicks;
        long lastContentCaptures;
        long lastResolveCalls;
        long lastDirtyBatches;
        long lastDirtyCoalesced;
        long lastDirtyCommits;
        long lastViewInvalidations;
        long lastMotionRefreshes;
        long lastForcedProjectionRefreshes;
        long lastProjectionExactRefreshes;
        long lastProjectionBridgeUses;
        long lastBackRenderFrames;
        long lastSkippedBackRenderFrames;
        long lastFrontBufferSwaps;

        long samples;
        long predictedRefreshSamples;
        long snapshotInvalidSamples;
        long visibleMissingSamples;
        long terrainGenerationMismatchSamples;
        long styleMismatchSamples;
        long trackUpMismatchSamples;
        long orientationMismatchSamples;
        long anchorMismatchSamples;
        long rangeMismatchSamples;
        long headingThresholdSamples;
        long displacementInvalidSamples;
        long displacementThresholdSamples;
        long frontInvalidSamples;
        long frontTerrainGenerationMismatchSamples;
        long frontViewGenerationMismatchSamples;
        long frontContentRevisionMismatchSamples;
        long authoritativeHeadingSamples;
        long authoritativeMovementSamples;
        long requestedViewNotReadySamples;
        long contentTickEvents;
        long contentCaptureEvents;
        long requestedClearEstimatedEvents;
        long resolveCallEvents;
        long dirtyBatchEvents;
        long dirtyCoalescedEvents;
        long dirtyCommitEvents;
        long viewInvalidationEvents;
        long motionRefreshEvents;
        long forcedProjectionRefreshEvents;
        long projectionExactRefreshEvents;
        long projectionBridgeEvents;
        long backRenderEvents;
        long skippedBackRenderEvents;
        long frontSwapEvents;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            nextSampleRealtime = 0f;
            nextLogRealtime = Time.realtimeSinceStartup + LogIntervalSeconds;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextSampleRealtime) return;
            nextSampleRealtime = now + SampleIntervalSeconds;

            if (!ResolveTargets())
            {
                if (!bindingWarningLogged && HighLogic.LoadedSceneIsFlight)
                {
                    bindingWarningLogged = true;
                    AERISLogger.Warn("[OH_REV3_5_R011_TURN_CHURN] observer target unavailable; " +
                        "R010 remains untouched and no diagnostic authority is assumed.");
                }
                return;
            }
            bindingWarningLogged = false;

            Sample(now);
            if (now >= nextLogRealtime)
            {
                nextLogRealtime = now + LogIntervalSeconds;
                LogWindow();
                ResetWindow();
            }
        }

        bool ResolveTargets()
        {
            if (!BindingsAvailable()) return false;
            if (core == null) core = FindObjectOfType<AERISBootstrap>();
            if (core == null) return false;
            object flightInstrument = BootstrapFlightInstrumentField.GetValue(core);
            if (flightInstrument == null)
            {
                navigationDisplay = null;
                renderer = null;
                rawCounterBaselineValid = false;
                return false;
            }
            AERISNavigationDisplay currentNavigation =
                FlightInstrumentNavigationDisplayField.GetValue(flightInstrument) as
                    AERISNavigationDisplay;
            AERISTerrainGpuTileRenderer currentRenderer = currentNavigation == null ? null :
                NavigationDisplayRendererField.GetValue(currentNavigation) as
                    AERISTerrainGpuTileRenderer;
            if (!ReferenceEquals(currentRenderer, renderer))
            {
                navigationDisplay = currentNavigation;
                renderer = currentRenderer;
                visibleType = null;
                visibleTerrainGenerationField = null;
                visibleViewGenerationField = null;
                visibleTerrainGenerationProperty = null;
                visibleViewGenerationProperty = null;
                rawCounterBaselineValid = false;
                ResetWindow();
            }
            else navigationDisplay = currentNavigation;
            return navigationDisplay != null && renderer != null;
        }

        void Sample(float now)
        {
            if (!HighLogic.LoadedSceneIsFlight || core == null || core.Settings == null ||
                core.Terrain == null || core.Terrain.DisplayTiles == null) return;
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null || vessel.mainBody == null) return;

            AERISSettings settings = core.Settings;
            AERISTerrainTileSystem system = core.Terrain.DisplayTiles;
            bool planMode = ReadBool(NavigationDisplayPlanModeField, navigationDisplay);
            double centerLatitudeDeg = planMode ?
                ReadDouble(NavigationDisplayPlanLatitudeField, navigationDisplay) :
                vessel.latitude;
            double centerLongitudeDeg = planMode ?
                ReadDouble(NavigationDisplayPlanLongitudeField, navigationDisplay) :
                vessel.longitude;
            float rangeMeters = AERISSettings.NormalizeNavigationRange(
                settings.NavigationDisplayManualRangeMeters);
            bool trackUp = !planMode && settings.NavigationDisplayTrackUp;
            float mapHeadingDeg = trackUp ?
                ReadFloat(NavigationDisplayMapHeadingField, navigationDisplay) : 0f;
            float anchorV = planMode || !trackUp ? 0.5f : 0.75f;
            AERISTerrainRenderTargetOrientation orientation =
                settings.TerrainRenderTargetOrientation;

            bool snapshotValid = ReadBool(ContentSnapshotValidField, renderer);
            object visible = ContentVisibleField.GetValue(renderer);
            long contentTerrainGeneration = ReadLong(ContentTerrainGenerationField, renderer);
            string contentStyleKey = ReadString(ContentStyleKeyField, renderer);
            double contentLatitudeDeg = ReadDouble(ContentCenterLatitudeField, renderer);
            double contentLongitudeDeg = ReadDouble(ContentCenterLongitudeField, renderer);
            float contentRangeMeters = ReadFloat(ContentRangeField, renderer);
            float contentHeadingDeg = ReadFloat(ContentHeadingField, renderer);
            bool contentTrackUp = ReadBool(ContentTrackUpField, renderer);
            float contentAnchorV = ReadFloat(ContentAnchorField, renderer);
            AERISTerrainRenderTargetOrientation contentOrientation =
                (AERISTerrainRenderTargetOrientation)ContentOrientationField.GetValue(renderer);

            bool snapshotInvalid = !snapshotValid;
            bool visibleMissing = visible == null;
            bool terrainGenerationMismatch =
                contentTerrainGeneration != system.TerrainGeneration;
            string expectedStyleKey = BuildObservedStyleKey(settings, core.Terrain.Performance,
                rangeMeters);
            bool styleMismatch = !string.Equals(contentStyleKey, expectedStyleKey,
                StringComparison.Ordinal);
            bool trackUpMismatch = contentTrackUp != trackUp;
            bool orientationMismatch = contentOrientation != orientation;
            bool anchorMismatch = Math.Abs(contentAnchorV - anchorV) > 0.001f;
            bool rangeMismatch = Math.Abs(contentRangeMeters - rangeMeters) > 0.5f;
            bool headingThreshold = trackUp && Mathf.Abs(Mathf.DeltaAngle(
                contentHeadingDeg, mapHeadingDeg)) >= 3f;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                contentLatitudeDeg, contentLongitudeDeg, centerLatitudeDeg,
                centerLongitudeDeg);
            bool displacementInvalid = double.IsNaN(displacement) ||
                double.IsInfinity(displacement);
            bool displacementThreshold = !displacementInvalid && displacement >=
                Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02);
            bool predictedRefresh = snapshotInvalid || visibleMissing ||
                terrainGenerationMismatch || styleMismatch || trackUpMismatch ||
                orientationMismatch || anchorMismatch || rangeMismatch ||
                headingThreshold || displacementInvalid || displacementThreshold;

            samples++;
            if (predictedRefresh) predictedRefreshSamples++;
            if (snapshotInvalid) snapshotInvalidSamples++;
            if (visibleMissing) visibleMissingSamples++;
            if (terrainGenerationMismatch) terrainGenerationMismatchSamples++;
            if (styleMismatch) styleMismatchSamples++;
            if (trackUpMismatch) trackUpMismatchSamples++;
            if (orientationMismatch) orientationMismatchSamples++;
            if (anchorMismatch) anchorMismatchSamples++;
            if (rangeMismatch) rangeMismatchSamples++;
            if (headingThreshold) headingThresholdSamples++;
            if (displacementInvalid) displacementInvalidSamples++;
            if (displacementThreshold) displacementThresholdSamples++;

            bool frontValid = ReadBool(FrontBufferValidField, renderer);
            long frontTerrainGeneration = ReadLong(FrontTerrainGenerationField, renderer);
            long frontViewGeneration = ReadLong(FrontViewGenerationField, renderer);
            long frontContentRevision = ReadLong(FrontContentRevisionField, renderer);
            long gpuContentRevision = ReadLong(GpuContentRevisionField, renderer);
            long visibleTerrainGeneration = ReadVisibleLong(visible, "TerrainGeneration");
            long visibleViewGeneration = ReadVisibleLong(visible, "ViewGeneration");
            if (!frontValid) frontInvalidSamples++;
            if (visible != null && frontTerrainGeneration != visibleTerrainGeneration)
                frontTerrainGenerationMismatchSamples++;
            if (visible != null && frontViewGeneration != visibleViewGeneration)
                frontViewGenerationMismatchSamples++;
            if (frontContentRevision != gpuContentRevision)
                frontContentRevisionMismatchSamples++;
            if (!renderer.RequestedViewReady) requestedViewNotReadySamples++;

            if (frontValid)
            {
                double frontLatitudeDeg = ReadDouble(FrontCenterLatitudeField, renderer);
                double frontLongitudeDeg = ReadDouble(FrontCenterLongitudeField, renderer);
                float frontHeadingDeg = ReadFloat(FrontMapHeadingField, renderer);
                double frontDisplacement = GreatCircleDistanceMeters(vessel.mainBody,
                    frontLatitudeDeg, frontLongitudeDeg, centerLatitudeDeg,
                    centerLongitudeDeg);
                if (trackUp && Mathf.Abs(Mathf.DeltaAngle(frontHeadingDeg,
                    mapHeadingDeg)) >= 0.05f) authoritativeHeadingSamples++;
                if (!double.IsNaN(frontDisplacement) && !double.IsInfinity(frontDisplacement) &&
                    (vessel.srfSpeed >= 0.5 && frontDisplacement >= 0.01 ||
                     frontDisplacement >= 0.25)) authoritativeMovementSamples++;
            }

            AccumulateRawCounterDeltas(snapshotValid);
        }

        void AccumulateRawCounterDeltas(bool snapshotValid)
        {
            long contentTicks = ReadLong(ContentTicksField, renderer);
            long contentCaptures = ReadLong(ContentCapturesField, renderer);
            long resolveCalls = ReadLong(ResolveCallsField, renderer);
            long dirtyBatches = ReadLong(DirtyBatchesField, renderer);
            long dirtyCoalesced = ReadLong(DirtyCoalescedField, renderer);
            long dirtyCommits = ReadLong(DirtyCommitsField, renderer);
            long viewInvalidations = ReadLong(ViewInvalidationsField, renderer);
            long motionRefreshes = ReadLong(MotionRefreshesField, renderer);
            long forcedProjectionRefreshes = ReadLong(ForcedProjectionRefreshesField, renderer);
            long projectionExactRefreshes = ReadLong(ProjectionExactRefreshesField, renderer);
            long projectionBridgeUses = ReadLong(ProjectionBridgeUsesField, renderer);
            long backRenderFrames = ReadLong(BackRenderFramesField, renderer);
            long skippedBackRenderFrames = ReadLong(SkippedBackRenderFramesField, renderer);
            long frontBufferSwaps = ReadLong(FrontBufferSwapsField, renderer);

            if (!rawCounterBaselineValid)
            {
                lastContentTicks = contentTicks;
                lastContentCaptures = contentCaptures;
                lastResolveCalls = resolveCalls;
                lastDirtyBatches = dirtyBatches;
                lastDirtyCoalesced = dirtyCoalesced;
                lastDirtyCommits = dirtyCommits;
                lastViewInvalidations = viewInvalidations;
                lastMotionRefreshes = motionRefreshes;
                lastForcedProjectionRefreshes = forcedProjectionRefreshes;
                lastProjectionExactRefreshes = projectionExactRefreshes;
                lastProjectionBridgeUses = projectionBridgeUses;
                lastBackRenderFrames = backRenderFrames;
                lastSkippedBackRenderFrames = skippedBackRenderFrames;
                lastFrontBufferSwaps = frontBufferSwaps;
                rawCounterBaselineValid = true;
                return;
            }

            long contentTickDelta = Delta(contentTicks, ref lastContentTicks);
            long contentCaptureDelta = Delta(contentCaptures, ref lastContentCaptures);
            contentTickEvents += contentTickDelta;
            contentCaptureEvents += contentCaptureDelta;
            if (snapshotValid)
                requestedClearEstimatedEvents += Math.Min(contentTickDelta,
                    contentCaptureDelta);
            resolveCallEvents += Delta(resolveCalls, ref lastResolveCalls);
            dirtyBatchEvents += Delta(dirtyBatches, ref lastDirtyBatches);
            dirtyCoalescedEvents += Delta(dirtyCoalesced, ref lastDirtyCoalesced);
            dirtyCommitEvents += Delta(dirtyCommits, ref lastDirtyCommits);
            viewInvalidationEvents += Delta(viewInvalidations, ref lastViewInvalidations);
            motionRefreshEvents += Delta(motionRefreshes, ref lastMotionRefreshes);
            forcedProjectionRefreshEvents += Delta(forcedProjectionRefreshes,
                ref lastForcedProjectionRefreshes);
            projectionExactRefreshEvents += Delta(projectionExactRefreshes,
                ref lastProjectionExactRefreshes);
            projectionBridgeEvents += Delta(projectionBridgeUses,
                ref lastProjectionBridgeUses);
            backRenderEvents += Delta(backRenderFrames, ref lastBackRenderFrames);
            skippedBackRenderEvents += Delta(skippedBackRenderFrames,
                ref lastSkippedBackRenderFrames);
            frontSwapEvents += Delta(frontBufferSwaps, ref lastFrontBufferSwaps);
        }

        void LogWindow()
        {
            if (samples <= 0) return;
            AERISLogger.Info("[OH_REV3_5_R011_TURN_CHURN] samples=" + samples +
                "; pred_refresh=" + predictedRefreshSamples +
                "; reason_snapshot=" + snapshotInvalidSamples +
                "; reason_visible=" + visibleMissingSamples +
                "; reason_terrain_gen=" + terrainGenerationMismatchSamples +
                "; reason_style=" + styleMismatchSamples +
                "; reason_trackup=" + trackUpMismatchSamples +
                "; reason_orientation=" + orientationMismatchSamples +
                "; reason_anchor=" + anchorMismatchSamples +
                "; reason_range=" + rangeMismatchSamples +
                "; reason_heading3=" + headingThresholdSamples +
                "; reason_disp_bad=" + displacementInvalidSamples +
                "; reason_disp2pct=" + displacementThresholdSamples +
                "; front_invalid=" + frontInvalidSamples +
                "; front_terrain_gen=" + frontTerrainGenerationMismatchSamples +
                "; front_view_gen=" + frontViewGenerationMismatchSamples +
                "; front_content_rev=" + frontContentRevisionMismatchSamples +
                "; auth_heading005=" + authoritativeHeadingSamples +
                "; auth_move=" + authoritativeMovementSamples +
                "; requested_not_ready=" + requestedViewNotReadySamples +
                "; content_tick=" + contentTickEvents +
                "; content_capture=" + contentCaptureEvents +
                "; requested_clear_est=" + requestedClearEstimatedEvents +
                "; resolve_calls=" + resolveCallEvents +
                "; dirty_batch=" + dirtyBatchEvents +
                "; dirty_coalesced=" + dirtyCoalescedEvents +
                "; dirty_commit=" + dirtyCommitEvents +
                "; view_invalid=" + viewInvalidationEvents +
                "; motion_refresh=" + motionRefreshEvents +
                "; force_project=" + forcedProjectionRefreshEvents +
                "; project_exact=" + projectionExactRefreshEvents +
                "; project_bridge=" + projectionBridgeEvents +
                "; back_render=" + backRenderEvents +
                "; back_skip=" + skippedBackRenderEvents +
                "; front_swap=" + frontSwapEvents + ".");
        }

        void ResetWindow()
        {
            samples = 0;
            predictedRefreshSamples = 0;
            snapshotInvalidSamples = 0;
            visibleMissingSamples = 0;
            terrainGenerationMismatchSamples = 0;
            styleMismatchSamples = 0;
            trackUpMismatchSamples = 0;
            orientationMismatchSamples = 0;
            anchorMismatchSamples = 0;
            rangeMismatchSamples = 0;
            headingThresholdSamples = 0;
            displacementInvalidSamples = 0;
            displacementThresholdSamples = 0;
            frontInvalidSamples = 0;
            frontTerrainGenerationMismatchSamples = 0;
            frontViewGenerationMismatchSamples = 0;
            frontContentRevisionMismatchSamples = 0;
            authoritativeHeadingSamples = 0;
            authoritativeMovementSamples = 0;
            requestedViewNotReadySamples = 0;
            contentTickEvents = 0;
            contentCaptureEvents = 0;
            requestedClearEstimatedEvents = 0;
            resolveCallEvents = 0;
            dirtyBatchEvents = 0;
            dirtyCoalescedEvents = 0;
            dirtyCommitEvents = 0;
            viewInvalidationEvents = 0;
            motionRefreshEvents = 0;
            forcedProjectionRefreshEvents = 0;
            projectionExactRefreshEvents = 0;
            projectionBridgeEvents = 0;
            backRenderEvents = 0;
            skippedBackRenderEvents = 0;
            frontSwapEvents = 0;
        }

        long ReadVisibleLong(object visible, string name)
        {
            if (visible == null) return -1L;
            Type type = visible.GetType();
            if (visibleType != type)
            {
                visibleType = type;
                visibleTerrainGenerationField = type.GetField("TerrainGeneration",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                visibleViewGenerationField = type.GetField("ViewGeneration",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                visibleTerrainGenerationProperty = type.GetProperty("TerrainGeneration",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                visibleViewGenerationProperty = type.GetProperty("ViewGeneration",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }
            if (string.Equals(name, "TerrainGeneration", StringComparison.Ordinal))
                return ReadMemberLong(visible, visibleTerrainGenerationField,
                    visibleTerrainGenerationProperty);
            return ReadMemberLong(visible, visibleViewGenerationField,
                visibleViewGenerationProperty);
        }

        static long ReadMemberLong(object target, FieldInfo field, PropertyInfo property)
        {
            if (target == null) return -1L;
            object value = field != null ? field.GetValue(target) :
                (property == null ? null : property.GetValue(target, null));
            return value == null ? -1L : Convert.ToInt64(value,
                CultureInfo.InvariantCulture);
        }

        static string BuildObservedStyleKey(AERISSettings settings,
            AERISTerrainPerformanceController performance, float rangeMeters)
        {
            float contourInterval = rangeMeters <= 10000f ? 50f :
                rangeMeters <= 40000f ? 100f :
                rangeMeters <= 80000f ? 250f : 500f;
            string quality = performance == null || performance.ActiveProfile == null ?
                "MEDIUM" : performance.ActiveProfile.Name;
            AERISTerrainVirtualDetailProfile detail =
                AERISTerrainVirtualDetailPolicy.Resolve(quality, rangeMeters);
            return (settings == null || settings.TerrainContoursEnabled ? "C" : "-") +
                (settings == null || settings.TerrainShadingEnabled ? "S" : "-") + "|" +
                contourInterval.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
                (detail == null ? "FAR DIRECT" : detail.Name);
        }

        static double GreatCircleDistanceMeters(CelestialBody body, double latitudeA,
            double longitudeA, double latitudeB, double longitudeB)
        {
            if (body == null || body.Radius <= 0.0) return double.PositiveInfinity;
            double latA = latitudeA * Math.PI / 180.0;
            double latB = latitudeB * Math.PI / 180.0;
            double dLat = (latitudeB - latitudeA) * Math.PI / 180.0;
            double dLon = NormalizeLongitudeDelta(longitudeB - longitudeA) *
                Math.PI / 180.0;
            double sinLat = Math.Sin(dLat * 0.5);
            double sinLon = Math.Sin(dLon * 0.5);
            double value = sinLat * sinLat + Math.Cos(latA) * Math.Cos(latB) *
                sinLon * sinLon;
            value = Math.Max(0.0, Math.Min(1.0, value));
            return body.Radius * 2.0 * Math.Atan2(Math.Sqrt(value),
                Math.Sqrt(Math.Max(0.0, 1.0 - value)));
        }

        static double NormalizeLongitudeDelta(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        static long Delta(long current, ref long previous)
        {
            long delta = current >= previous ? current - previous : 0L;
            previous = current;
            return delta;
        }

        static FieldInfo RendererField(string name)
        {
            return typeof(AERISTerrainGpuTileRenderer).GetField(name, PrivateInstance);
        }

        static bool BindingsAvailable()
        {
            return BootstrapFlightInstrumentField != null &&
                FlightInstrumentNavigationDisplayField != null &&
                NavigationDisplayRendererField != null &&
                NavigationDisplayPlanModeField != null &&
                NavigationDisplayPlanLatitudeField != null &&
                NavigationDisplayPlanLongitudeField != null &&
                NavigationDisplayMapHeadingField != null &&
                ContentSnapshotValidField != null && ContentVisibleField != null &&
                ContentTerrainGenerationField != null && ContentStyleKeyField != null &&
                ContentCenterLatitudeField != null && ContentCenterLongitudeField != null &&
                ContentRangeField != null && ContentHeadingField != null &&
                ContentTrackUpField != null && ContentAnchorField != null &&
                ContentOrientationField != null && FrontBufferValidField != null &&
                FrontTerrainGenerationField != null && FrontViewGenerationField != null &&
                FrontContentRevisionField != null && GpuContentRevisionField != null &&
                FrontCenterLatitudeField != null && FrontCenterLongitudeField != null &&
                FrontMapHeadingField != null && ContentTicksField != null &&
                ContentCapturesField != null && ResolveCallsField != null &&
                DirtyBatchesField != null && DirtyCoalescedField != null &&
                DirtyCommitsField != null && ViewInvalidationsField != null &&
                MotionRefreshesField != null && ForcedProjectionRefreshesField != null &&
                ProjectionExactRefreshesField != null && ProjectionBridgeUsesField != null &&
                BackRenderFramesField != null && SkippedBackRenderFramesField != null &&
                FrontBufferSwapsField != null;
        }

        static bool ReadBool(FieldInfo field, object target)
        {
            return field != null && target != null && (bool)field.GetValue(target);
        }

        static long ReadLong(FieldInfo field, object target)
        {
            if (field == null || target == null) return 0L;
            object value = field.GetValue(target);
            return value == null ? 0L : Convert.ToInt64(value,
                CultureInfo.InvariantCulture);
        }

        static float ReadFloat(FieldInfo field, object target)
        {
            if (field == null || target == null) return 0f;
            object value = field.GetValue(target);
            return value == null ? 0f : Convert.ToSingle(value,
                CultureInfo.InvariantCulture);
        }

        static double ReadDouble(FieldInfo field, object target)
        {
            if (field == null || target == null) return 0.0;
            object value = field.GetValue(target);
            return value == null ? 0.0 : Convert.ToDouble(value,
                CultureInfo.InvariantCulture);
        }

        static string ReadString(FieldInfo field, object target)
        {
            return field == null || target == null ? string.Empty :
                field.GetValue(target) as string ?? string.Empty;
        }
    }
}
