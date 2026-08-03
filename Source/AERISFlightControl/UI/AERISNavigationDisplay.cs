using System;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Settings;
using AERISFlightControl.Landing;
using AERISFlightControl.Terrain;
using AERISFlightControl.Performance;
using AERISFlightControl.Integration;
using AERISFlightControl.Logging;

namespace AERISFlightControl.UI
{
    // Display-only moving-map ND. TERRAIN is the normal background. Independent LAND
    // overlays runway/LOC/GS geometry or can switch to a clean guidance focus view.
    // This class never writes FlightCtrlState or arms an AP director.
    internal sealed class AERISNavigationDisplay
    {
        sealed class TrailSample
        {
            internal uint VesselPersistentId;
            internal string BodyName = string.Empty;
            internal double LatitudeDeg;
            internal double LongitudeDeg;
            internal float Realtime;
        }

        sealed class TrafficCaptureEntry
        {
            internal double DistanceMeters;
            internal AERISNavigationTrafficSource Source;
        }

        readonly AERISSettings settings;
        readonly AERISBootstrap core;
        readonly AERISNavigationDisplayProfileStore profileStore;
        string activeProfileSignature = string.Empty;
        string activeProfileLabel = string.Empty;
        Vessel activeProfileVessel;
        uint activeProfileRuntimeVesselId;
        int activeProfilePartCount = -1;
        GUIStyle titleStyle;
        GUIStyle textStyle;
        GUIStyle centerStyle;
        GUIStyle buttonStyle;
        GUIStyle rightTitleStyle;
        float styleScale = -1f;

        readonly AERISTerrainGpuTileRenderer terrainTileRenderer;
        float nextNavigationSnapshotRealtime;
        float nextSymbologySnapshotRealtime;
        float nextNavigationCaptureRealtime;
        AERISRunwayObservation cachedLandingObservation;
        AERISRunwayDirectionDefinition cachedLandingDirection;
        float cachedFallbackMapHeading;
        string capturedBodyName = string.Empty;
        long capturedDatabaseRevision = -1L;
        long capturedSelectionRevision = -1L;
        double capturedOriginLatitudeDeg;
        double capturedOriginLongitudeDeg;
        bool planMode;
        bool orientationBeforePlanTrackUp = true;
        double planCenterLatitudeDeg;
        double planCenterLongitudeDeg;
        bool mapPointerDown;
        bool mapDragging;
        Vector2 mapPointerStart;
        Vector2 mapPointerLast;
        string previewRunwayStableId = string.Empty;
        int previewAirfieldIndex = -1;
        int previewDirectionIndex = -1;
        string previewMessage = string.Empty;
        readonly List<TrailSample> trailSamples = new List<TrailSample>(900);
        readonly List<TrafficCaptureEntry> trafficCaptureScratch =
            new List<TrafficCaptureEntry>(64);
        uint trailVesselPersistentId;
        string trailBodyName = string.Empty;
        float nextTrailSampleRealtime;
        float nextTrafficCaptureRealtime;
        float nextWindSampleRealtime;
        bool auxiliaryMenuOpen;
        string previewTrafficStableId = string.Empty;
        string previewTrafficMessage = string.Empty;
        bool windValid;
        bool flightViewportActive;
        const float RangeChangeDebounceSeconds = 0.35f;
        float pendingManualRangeMeters = float.NaN;
        float pendingManualRangeApplyRealtime;
        AERISWindSample windSample;
        string windProviderName = string.Empty;
        float nextNdGcSampleRealtime;
        float nextTerrainTelemetrySampleRealtime;
        AERISTerrainTileCacheTelemetry cachedTerrainTileTelemetry;

        static readonly Color RunwayColor = new Color(0.92f, 0.94f, 0.98f, 1f);
        static readonly Color GuidanceColor = new Color(0.30f, 0.94f, 0.62f, 1f);
        static readonly Color ArmedColor = new Color(1f, 0.82f, 0.22f, 1f);
        static readonly Color WarningColor = new Color(1f, 0.30f, 0.24f, 1f);

        internal AERISNavigationDisplay(AERISSettings settings, AERISBootstrap core)
        {
            this.settings = settings;
            this.core = core;
            profileStore = new AERISNavigationDisplayProfileStore(settings);
            terrainTileRenderer = new AERISTerrainGpuTileRenderer(settings,
                core == null || core.Terrain == null ? null : core.Terrain.Performance);
        }

        internal void SetFlightViewportActive(bool active)
        {
            if (flightViewportActive == active) return;
            flightViewportActive = active;
            if (active)
            {
                nextNavigationSnapshotRealtime = 0f;
                nextSymbologySnapshotRealtime = 0f;
                nextNavigationCaptureRealtime = 0f;
                return;
            }

            if (terrainTileRenderer != null) terrainTileRenderer.SuspendViewport();
            nextNavigationSnapshotRealtime = 0f;
            nextSymbologySnapshotRealtime = 0f;
            nextNavigationCaptureRealtime = 0f;
            cachedLandingObservation = null;
            cachedLandingDirection = null;
            capturedBodyName = string.Empty;
            capturedDatabaseRevision = -1L;
            capturedSelectionRevision = -1L;
            AERISPreparedNavigationFrameApi.Clear();
            AERISPreparedTrafficFrameApi.Clear();
            trailSamples.Clear();
            trafficCaptureScratch.Clear();
            previewRunwayStableId = string.Empty;
            previewAirfieldIndex = -1;
            previewDirectionIndex = -1;
            previewTrafficStableId = string.Empty;
            windValid = false;
            windProviderName = string.Empty;
            nextNdGcSampleRealtime = 0f;
            nextTerrainTelemetrySampleRealtime = 0f;
            cachedTerrainTileTelemetry = null;
            pendingManualRangeMeters = float.NaN;
            pendingManualRangeApplyRealtime = 0f;
        }

        internal void Dispose()
        {
            SaveActiveProfile();
            if (terrainTileRenderer != null) terrainTileRenderer.Dispose();
            cachedLandingObservation = null;
            cachedLandingDirection = null;
            AERISPreparedNavigationFrameApi.Clear();
            AERISPreparedTrafficFrameApi.Clear();
            trailSamples.Clear();
            trafficCaptureScratch.Clear();
            previewRunwayStableId = string.Empty;
            previewAirfieldIndex = -1;
            previewDirectionIndex = -1;
            previewTrafficStableId = string.Empty;
            windValid = false;
            windProviderName = string.Empty;
            nextNdGcSampleRealtime = 0f;
            nextTerrainTelemetrySampleRealtime = 0f;
            cachedTerrainTileTelemetry = null;
            pendingManualRangeMeters = float.NaN;
            pendingManualRangeApplyRealtime = 0f;
        }

        internal void Draw(Rect rect)
        {
            if (settings == null || core == null || !flightViewportActive) return;
            FlushPendingManualRange();
            if (!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) ||
                rect.width < 64f || rect.height < 50f) return;

            EventType eventType = Event.current == null ? EventType.Repaint : Event.current.type;
            bool repaint = eventType == EventType.Repaint;
            bool layout = eventType == EventType.Layout;
            bool measure = repaint || layout;
            float now = Time.realtimeSinceStartup;
            // Frozen Gate 4A regression token; Candidate 8 samples GC only once per second:
            // measure ? GC.GetTotalMemory(false) : 0L
            bool sampleGc = repaint && now >= nextNdGcSampleRealtime;
            long gcBefore = sampleGc ? GC.GetTotalMemory(false) : 0L;
            long drawStartTicks = measure ? Stopwatch.GetTimestamp() : 0L;
            float scale = Mathf.Clamp(Mathf.Min(rect.width / 380f, rect.height / 244f), 0.30f, 1.25f);
            EnsureStyles(scale);
            Color previousColor = GUI.color;
            Color previousBackground = GUI.backgroundColor;
            Matrix4x4 previousMatrix = GUI.matrix;
            try
            {
                GUI.color = Color.white;
                GUI.backgroundColor = new Color(0.035f, 0.055f, 0.075f, 0.98f);
                GUI.Box(rect, GUIContent.none);
                GUI.BeginGroup(rect);
                try { DrawLocal(new Rect(0f, 0f, rect.width, rect.height), scale); }
                finally { GUI.EndGroup(); }
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
                GUI.backgroundColor = previousBackground;
                if (measure)
                {
                    long drawEndTicks = Stopwatch.GetTimestamp();
                    float elapsed = (float)((drawEndTicks - drawStartTicks) * 1000.0 /
                        Stopwatch.Frequency);
                    long gcDelta = sampleGc ?
                        Math.Max(0L, GC.GetTotalMemory(false) - gcBefore) : 0L;
                    if (sampleGc) nextNdGcSampleRealtime = now + 1f;
                    if (core.Terrain != null && core.Terrain.Performance != null)
                    {
                        if (layout) core.Terrain.Performance.RecordNdLayoutCost(elapsed);
                        else if (repaint) core.Terrain.Performance.RecordNdRepaintCost(elapsed);
                    }
                    if (core.Performance != null)
                        core.Performance.RecordNavigationDisplayEvent(repaint, elapsed, gcDelta);
                }
            }
        }

        void EnsureStyles(float scale)
        {
            if (titleStyle != null && Mathf.Abs(styleScale - scale) < 0.01f) return;
            styleScale = scale;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(7, Mathf.RoundToInt(11f * scale)),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            textStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(6, Mathf.RoundToInt(9f * scale)),
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip
            };
            centerStyle = new GUIStyle(textStyle)
            {
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.Max(7, Mathf.RoundToInt(10f * scale)),
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false,
                clipping = TextClipping.Clip,
                stretchHeight = false,
                padding = new RectOffset(1, 1, 1, 1)
            };
            rightTitleStyle = new GUIStyle(titleStyle)
            {
                alignment = TextAnchor.MiddleRight
            };
        }

        void DrawLocal(Rect rect, float scale)
        {
            float margin = Mathf.Max(4f, 6f * scale);
            float header = Mathf.Max(18f, 23f * scale);
            float controls = Mathf.Max(19f, 25f * scale);
            AERISLandingFoundation land = core.Landing;
            AERISAirfieldRegistry registry = core.Airfields;
            AERISTerrainAwareness terrain = core.Terrain;
            Vessel vessel = FlightGlobals.ActiveVessel;
            SyncVesselProfile(vessel);
            AERISRunwayDirectionDefinition direction = land != null ? land.ActiveDirection :
                (registry == null ? null : registry.SelectedDirection);
            AERISRunwayObservation liveObservation = land == null ? null : land.Observation;
            bool liveLandActive = land != null && land.Armed && direction != null &&
                liveObservation != null && liveObservation.Valid;
            UpdateRateLimitedSnapshots(terrain, direction, liveObservation, liveLandActive, vessel);
            CaptureNavigationSnapshot(registry, vessel);
            UpdateAuxiliarySnapshots(vessel);
            AERISRunwayObservation observation = liveLandActive ? cachedLandingObservation : null;
            if (observation == null && liveLandActive) observation = liveObservation;
            bool landActive = liveLandActive && observation != null && observation.Valid;
            bool overlay = !landActive || settings.NavigationDisplayLandOverlay;
            float requestedRange = AERISSettings.NormalizeNavigationRange(
                settings.NavigationDisplayManualRangeMeters);
            settings.NavigationDisplayManualRangeMeters = requestedRange;
            bool effectiveTrackUp = !planMode && settings.NavigationDisplayTrackUp;
            float mapHeading = effectiveTrackUp ? cachedFallbackMapHeading : 0f;

            AERISPreparedNavigationFrame frame;
            bool hasFrame = AERISPreparedNavigationFrameApi.TryGetLatest(out frame) &&
                frame != null && vessel != null && vessel.mainBody != null &&
                string.Equals(frame.BodyName, vessel.mainBody.name,
                    StringComparison.OrdinalIgnoreCase) && registry != null &&
                frame.DatabaseRevision == registry.DatabaseRevision &&
                frame.SelectionRevision == registry.SelectionRevision;
            AERISPreparedTrafficFrame trafficFrame = null;
            bool hasTrafficFrame = settings.NavigationDisplayTrafficEnabled &&
                AERISPreparedTrafficFrameApi.TryGetLatest(out trafficFrame) &&
                trafficFrame != null && vessel != null && vessel.mainBody != null &&
                string.Equals(trafficFrame.BodyName, vessel.mainBody.name,
                    StringComparison.OrdinalIgnoreCase);
            if (!hasTrafficFrame) trafficFrame = null;
            if (planMode && vessel != null && vessel.mainBody != null &&
                (!Finite((float)planCenterLatitudeDeg) || !Finite((float)planCenterLongitudeDeg)))
            {
                planCenterLatitudeDeg = vessel.latitude;
                planCenterLongitudeDeg = vessel.longitude;
            }

            string mode = planMode ? "PLAN" : (landActive ? "LAND" : "TERR");
            string orientation = planMode ? "N" : (effectiveTrackUp ? "TRK" : "N");
            DrawLabel(new Rect(margin, 0f, rect.width * 0.42f, header),
                mode + "  " + orientation, titleStyle,
                landActive ? ArmedColor : new Color(0.80f, 0.94f, 1f, 1f));
            string rightHeader = landActive && direction != null ? direction.DisplayName :
                (hasFrame ? frame.Runways.Length + " RWY" : "NAV DATA");
            DrawLabel(new Rect(rect.width * 0.42f, 0f, rect.width * 0.56f - margin, header),
                rightHeader, rightTitleStyle, RunwayColor);

            Rect viewport = new Rect(margin, header, Mathf.Max(20f, rect.width - margin * 2f),
                Mathf.Max(20f, rect.height - header - controls - margin));
            GUI.Box(viewport, GUIContent.none);
            HandleMouseWheel(viewport, requestedRange);

            Rect plan = viewport;
            Rect profile = new Rect();
            if (landActive)
            {
                float profileFraction = ResolveLandingProfileFraction();
                float planWidth = Mathf.Max(40f, viewport.width * (1f - profileFraction));
                plan = new Rect(viewport.x + 1f, viewport.y + 1f,
                    Mathf.Max(20f, planWidth - 2f), Mathf.Max(20f, viewport.height - 2f));
                profile = new Rect(viewport.x + planWidth, viewport.y + 1f,
                    Mathf.Max(20f, viewport.width - planWidth - 1f),
                    Mathf.Max(20f, viewport.height - 2f));
                GUI.Box(plan, GUIContent.none);
                GUI.Box(profile, GUIContent.none);
            }
            else
            {
                plan = new Rect(viewport.x + 1f, viewport.y + 1f,
                    Mathf.Max(20f, viewport.width - 2f), Mathf.Max(20f, viewport.height - 2f));
            }

            Rect mapControlsRect = new Rect(viewport.x, viewport.yMax + 2f,
                viewport.width, controls - 2f);
            Rect auxiliaryMenuRect = auxiliaryMenuOpen ?
                ResolveAuxiliaryMenuRect(mapControlsRect, landActive, scale) : new Rect();
            HandleMapInteraction(plan, frame, trafficFrame, vessel, requestedRange,
                auxiliaryMenuRect);
            double terrainCenterLatitudeDeg = vessel == null ? 0.0 :
                (planMode ? planCenterLatitudeDeg : vessel.latitude);
            double terrainCenterLongitudeDeg = vessel == null ? 0.0 :
                (planMode ? planCenterLongitudeDeg : vessel.longitude);
            float anchorV = planMode || !effectiveTrackUp ? 0.5f : 0.75f;
            bool repaint = Event.current == null || Event.current.type == EventType.Repaint;
            if (repaint)
            {
                if (planMode)
                {
                    DrawTerrainMap(plan, terrain, false, vessel, terrainCenterLatitudeDeg,
                        terrainCenterLongitudeDeg, requestedRange, 0f, false, anchorV);
                    DrawLabel(new Rect(plan.x + 4f, plan.y + 2f, plan.width - 8f,
                        Mathf.Max(12f, 15f * scale)), "PLAN",
                        textStyle, new Color(0.70f, 0.88f, 0.94f, 0.90f));
                }
                else if (!landActive || overlay)
                    DrawTerrainMap(plan, terrain, false, vessel, terrainCenterLatitudeDeg,
                        terrainCenterLongitudeDeg, requestedRange, mapHeading,
                        effectiveTrackUp, anchorV);
                else DrawCleanBackground(plan);

                // Gate 5 Candidate 2 map-authority latch. Terrain and all world-fixed
                // symbology must use the exact projection of the GPU FRONT that was actually
                // presented. During a short FAR boundary refresh the requested view may advance
                // while the last complete FRONT remains latched; drawing runway/traffic against
                // the newer request would make those symbols appear to float over the terrain.
                double presentedCenterLatitudeDeg = terrainCenterLatitudeDeg;
                double presentedCenterLongitudeDeg = terrainCenterLongitudeDeg;
                float presentedRange = requestedRange;
                float presentedHeading = mapHeading;
                bool presentedTrackUp = effectiveTrackUp;
                float presentedAnchorV = anchorV;
                bool terrainPresentationActive = planMode || !landActive || overlay;
                if (terrainPresentationActive && terrainTileRenderer != null)
                {
                    AERISTerrainPresentedProjection presented =
                        terrainTileRenderer.PresentedProjection;
                    if (presented.Valid)
                    {
                        presentedCenterLatitudeDeg = presented.CenterLatitudeDeg;
                        presentedCenterLongitudeDeg = presented.CenterLongitudeDeg;
                        presentedRange = presented.RangeMeters;
                        presentedHeading = presented.MapHeadingDeg;
                        presentedTrackUp = presented.TrackUp;
                        presentedAnchorV = presented.AnchorV;
                    }
                }

                double ownEast = 0.0, ownNorth = 0.0;
                double centerEast = 0.0, centerNorth = 0.0;
                if (hasFrame && vessel != null && vessel.mainBody != null)
                {
                    ToLocalMeters(vessel.mainBody, frame.OriginLatitudeDeg,
                        frame.OriginLongitudeDeg, vessel.latitude, vessel.longitude,
                        out ownEast, out ownNorth);
                    ToLocalMeters(vessel.mainBody, frame.OriginLatitudeDeg,
                        frame.OriginLongitudeDeg, presentedCenterLatitudeDeg,
                        presentedCenterLongitudeDeg, out centerEast, out centerNorth);
                }
                Vector2 aircraftPoint;
                TryMapPoint(ownEast - centerEast, ownNorth - centerNorth,
                    presentedRange, presentedHeading, presentedTrackUp, plan,
                    presentedAnchorV, out aircraftPoint);
                DrawRangeRings(plan, aircraftPoint, scale);
                DrawTrail(plan, vessel, presentedRange, presentedHeading, presentedTrackUp,
                    presentedAnchorV, presentedCenterLatitudeDeg,
                    presentedCenterLongitudeDeg, scale);
                bool drawNonRunwayFacilities = false;
                if (!landActive) drawNonRunwayFacilities = true;
                DrawPreparedNavigation(plan, frame, vessel, presentedRange,
                    presentedHeading, presentedTrackUp, presentedAnchorV, centerEast,
                    centerNorth, presentedCenterLatitudeDeg, presentedCenterLongitudeDeg,
                    scale, drawNonRunwayFacilities);
                DrawPreparedTraffic(plan, trafficFrame, vessel, presentedRange,
                    presentedHeading, presentedTrackUp, presentedAnchorV,
                    presentedCenterLatitudeDeg, presentedCenterLongitudeDeg, scale);
                DrawTrackVector(plan, aircraftPoint, vessel, presentedRange,
                    presentedHeading, presentedTrackUp, presentedAnchorV,
                    presentedCenterLatitudeDeg, presentedCenterLongitudeDeg, scale);
                DrawWindOverlay(plan, vessel, presentedHeading, presentedTrackUp, scale);
                if (landActive)
                {
                    if (!planMode)
                        DrawLandingPlan(plan, direction, observation, vessel,
                            presentedCenterLatitudeDeg, presentedCenterLongitudeDeg,
                            presentedRange, presentedHeading, presentedTrackUp,
                            presentedAnchorV, scale);
                    DrawLandingProfile(profile, direction, observation, scale);
                }
                DrawAircraftSymbol(aircraftPoint, scale);
            }
            DrawPreviewPanel(plan, frame, trafficFrame, requestedRange, scale);

            DrawMapControls(mapControlsRect, landActive, requestedRange, overlay,
                planMode, scale);
            if (core.Performance != null)
            {
                core.Performance.RecordNavigationDisplayState(planMode, requestedRange);
                if (repaint)
                {
                    float telemetryNow = Time.realtimeSinceStartup;
                    if (terrain == null || terrain.DisplayTiles == null)
                    {
                        cachedTerrainTileTelemetry = null;
                        nextTerrainTelemetrySampleRealtime = telemetryNow + 0.5f;
                    }
                    else if (cachedTerrainTileTelemetry == null ||
                        telemetryNow >= nextTerrainTelemetrySampleRealtime)
                    {
                        cachedTerrainTileTelemetry = terrain.DisplayTiles.SnapshotTelemetry();
                        nextTerrainTelemetrySampleRealtime = telemetryNow + 0.5f;
                    }
                    core.Performance.RecordTerrainTileState(cachedTerrainTileTelemetry,
                        terrainTileRenderer == null ? 0L : terrainTileRenderer.UsedBytes,
                        terrainTileRenderer == null ? 0 : terrainTileRenderer.TextureCount,
                        terrainTileRenderer == null ? 0 : terrainTileRenderer.PendingCount,
                        terrainTileRenderer == null ? 0 : terrainTileRenderer.UploadFailures,
                        terrainTileRenderer == null ? 0.0 :
                            terrainTileRenderer.LastCoverageFraction,
                        terrain == null || terrain.Performance == null ? 0.0 :
                            terrain.Performance.TilePqsSampleEmaMs,
                        terrain == null || terrain.Performance == null ? 0.0 :
                            terrain.Performance.TerrainMeshWorkerEmaMs,
                        terrain == null || terrain.Performance == null ? 0.0 :
                            terrain.Performance.TerrainContourWorkerEmaMs);
                    core.Performance.RecordTerrainContinuityState(
                        terrainTileRenderer != null &&
                            terrainTileRenderer.FrontBufferPresented,
                        terrainTileRenderer == null ? 0L :
                            terrainTileRenderer.HistoryReprojectFrames,
                        0L,
                        terrainTileRenderer == null ? 0.0 :
                            terrainTileRenderer.FrontBufferAgeMilliseconds);
                }
            }
        }

        void UpdateAuxiliarySnapshots(Vessel vessel)
        {
            UpdateTrail(vessel);
            CaptureTrafficSnapshot(vessel);
            UpdateWindSample(vessel);
        }

        void UpdateTrail(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null ||
                !settings.NavigationDisplayTrailEnabled)
            {
                trailSamples.Clear();
                trailVesselPersistentId = 0u;
                trailBodyName = string.Empty;
                return;
            }
            string bodyName = vessel.mainBody.name ?? string.Empty;
            if (trailVesselPersistentId != vessel.persistentId ||
                !string.Equals(trailBodyName, bodyName, StringComparison.OrdinalIgnoreCase))
            {
                trailSamples.Clear();
                trailVesselPersistentId = vessel.persistentId;
                trailBodyName = bodyName;
                nextTrailSampleRealtime = 0f;
            }
            float now = Time.realtimeSinceStartup;
            if (now < nextTrailSampleRealtime) return;
            nextTrailSampleRealtime = now + 1f;
            if (!Finite((float)vessel.latitude) || !Finite((float)vessel.longitude)) return;
            if (trailSamples.Count > 0)
            {
                TrailSample previous = trailSamples[trailSamples.Count - 1];
                double east, north;
                ToLocalMeters(vessel.mainBody, previous.LatitudeDeg, previous.LongitudeDeg,
                    vessel.latitude, vessel.longitude, out east, out north);
                if (east * east + north * north < 400.0) return;
            }
            trailSamples.Add(new TrailSample
            {
                VesselPersistentId = vessel.persistentId,
                BodyName = bodyName,
                LatitudeDeg = vessel.latitude,
                LongitudeDeg = vessel.longitude,
                Realtime = now
            });
            if (trailSamples.Count > 900)
                trailSamples.RemoveRange(0, trailSamples.Count - 900);
        }

        void CaptureTrafficSnapshot(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null || core.Performance == null ||
                !settings.NavigationDisplayTrafficEnabled)
            {
                AERISPreparedTrafficFrameApi.Clear();
                trafficCaptureScratch.Clear();
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (now < nextTrafficCaptureRealtime) return;
            nextTrafficCaptureRealtime = now + 0.5f;
            trafficCaptureScratch.Clear();
            IList<Vessel> vessels = FlightGlobals.Vessels;
            if (vessels == null) return;
            double maximumRangeMeters = Math.Max(200000.0,
                AERISSettings.NormalizeNavigationRange(
                    settings.NavigationDisplayManualRangeMeters) * 1.5);
            double maximumRangeSquared = maximumRangeMeters * maximumRangeMeters;
            for (int i = 0; i < vessels.Count; i++)
            {
                Vessel target = vessels[i];
                if (target == null || ReferenceEquals(target, vessel) ||
                    target.persistentId == vessel.persistentId || target.mainBody == null || !ReferenceEquals(target.mainBody, vessel.mainBody) ||
                    target.LandedOrSplashed || target.situation == Vessel.Situations.PRELAUNCH)
                    continue;
                string situation = target.situation.ToString().ToUpperInvariant();
                if (situation != "FLYING" && situation != "SUB_ORBITAL") continue;
                string type = target.vesselType.ToString().ToUpperInvariant();
                if (type == "DEBRIS" || type == "FLAG" || type == "EVA" ||
                    type == "SPACEOBJECT" || type == "UNKNOWN") continue;
                double east, north;
                ToLocalMeters(vessel.mainBody, vessel.latitude, vessel.longitude,
                    target.latitude, target.longitude, out east, out north);
                double distanceSquared = east * east + north * north;
                if (distanceSquared > maximumRangeSquared) continue;
                trafficCaptureScratch.Add(new TrafficCaptureEntry
                {
                    DistanceMeters = Math.Sqrt(Math.Max(0.0, distanceSquared)),
                    Source = new AERISNavigationTrafficSource
                    {
                        StableId = target.persistentId.ToString(),
                        Name = target.vesselName ?? string.Empty,
                        LatitudeDeg = target.latitude,
                        LongitudeDeg = target.longitude,
                        AltitudeAslMeters = target.altitude,
                        GroundTrackDeg = ResolveMapHeading(target),
                        GroundSpeedMps = Math.Max(0.0, target.srfSpeed)
                    }
                });
            }
            trafficCaptureScratch.Sort((left, right) =>
                left.DistanceMeters.CompareTo(right.DistanceMeters));
            int symbolLimit = 32;
            if (core.Terrain != null && core.Terrain.Performance != null)
                symbolLimit = Mathf.Clamp(
                    core.Terrain.Performance.ActiveProfile.MaximumFacilitySymbols * 2,
                    16, 64);
            int count = Math.Min(symbolLimit, trafficCaptureScratch.Count);
            var sources = new AERISNavigationTrafficSource[count];
            for (int i = 0; i < count; i++) sources[i] = trafficCaptureScratch[i].Source;
            core.Performance.SubmitNavigationTraffic(new AERISNavigationTrafficSnapshot
            {
                Generation = core.Performance.CaptureStamp(),
                BodyName = vessel.mainBody.name ?? string.Empty,
                BodyRadiusMeters = Math.Max(1.0, vessel.mainBody.Radius),
                OriginLatitudeDeg = vessel.latitude,
                OriginLongitudeDeg = vessel.longitude,
                OwnAltitudeAslMeters = vessel.altitude,
                OwnGroundTrackDeg = ResolveMapHeading(vessel),
                OwnGroundSpeedMps = Math.Max(0.0, vessel.srfSpeed),
                Traffic = sources
            });
        }

        void UpdateWindSample(Vessel vessel)
        {
            if (vessel == null || vessel.mainBody == null ||
                !settings.NavigationDisplayWindEnabled)
            {
                windValid = false;
                windProviderName = string.Empty;
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (now < nextWindSampleRealtime) return;
            nextWindSampleRealtime = now + 1f;
            AERISWindQuery query = new AERISWindQuery
            {
                BodyName = vessel.mainBody.name ?? string.Empty,
                UniversalTime = Planetarium.GetUniversalTime(),
                LatitudeDeg = vessel.latitude,
                LongitudeDeg = vessel.longitude,
                AltitudeAslMeters = vessel.altitude
            };
            windValid = AERISWindProviderApi.TrySample(query, out windSample,
                out windProviderName);
        }

        void CaptureNavigationSnapshot(AERISAirfieldRegistry registry, Vessel vessel)
        {
            if (registry == null || vessel == null || vessel.mainBody == null ||
                core.Performance == null) return;
            float now = Time.realtimeSinceStartup;
            string bodyName = vessel.mainBody.name ?? string.Empty;
            double movedEast, movedNorth;
            ToLocalMeters(vessel.mainBody, capturedOriginLatitudeDeg,
                capturedOriginLongitudeDeg, vessel.latitude, vessel.longitude,
                out movedEast, out movedNorth);
            double moved = Math.Sqrt(movedEast * movedEast + movedNorth * movedNorth);
            double refreshDistance = Math.Max(5000.0,
                AERISSettings.NormalizeNavigationRange(
                    settings.NavigationDisplayManualRangeMeters) * 0.20);
            bool revisionChanged = capturedDatabaseRevision != registry.DatabaseRevision ||
                capturedSelectionRevision != registry.SelectionRevision;
            bool bodyChanged = !string.Equals(capturedBodyName, bodyName,
                StringComparison.OrdinalIgnoreCase);
            if (!revisionChanged && !bodyChanged && now < nextNavigationCaptureRealtime &&
                moved <= refreshDistance) return;

            long captureStartTicks = Stopwatch.GetTimestamp();
            var runwaySources = new List<AERISNavigationRunwaySource>();
            var facilitySources = new List<AERISNavigationFacilitySource>();
            IList<AERISAirfieldDefinition> airfieldView = registry.Airfields;
            AERISAirfieldDefinition selectedAirfield = registry.SelectedAirfield;
            AERISRunwayDefinition selectedRunway = registry.SelectedRunway;
            for (int i = 0; i < airfieldView.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfieldView[i];
                if (airfield == null || !registry.IsAirfieldPresentationAvailable(airfield) ||
                    !string.Equals(airfield.Body, bodyName,
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (airfield.FacilityKind != AERISFacilityKind.Runway)
                {
                    double facilityLat, facilityLon;
                    if (TryAirfieldPoint(airfield, out facilityLat, out facilityLon))
                        facilitySources.Add(new AERISNavigationFacilitySource
                        {
                            AirfieldIndex = i,
                            StableId = airfield.StableId,
                            Name = airfield.DisplayName,
                            FacilityKind = (int)airfield.FacilityKind,
                            Selected = airfield == selectedAirfield,
                            LatitudeDeg = facilityLat,
                            LongitudeDeg = facilityLon,
                            ElevationMeters = airfield.ReferenceElevationMeters
                        });
                    continue;
                }

                for (int r = 0; r < airfield.Runways.Count; r++)
                {
                    AERISRunwayDefinition runway = airfield.Runways[r];
                    if (runway == null) continue;
                    AERISRunwayDirectionDefinition first;
                    AERISRunwayDirectionDefinition second;
                    ResolveNavigationDirectionPair(registry, airfield, runway,
                        out first, out second);
                    if (first == null) continue;
                    bool certifiedRunway = true;
                    bool provisionalRunway = false;
                    int firstSelectable = FindSelectableDirectionIndex(registry, airfield, first);
                    int secondSelectable = FindSelectableDirectionIndex(registry, airfield, second);
                    runwaySources.Add(new AERISNavigationRunwaySource
                    {
                        AirfieldIndex = i,
                        AirfieldStableId = airfield.StableId,
                        AirfieldName = airfield.DisplayName,
                        RunwayStableId = ResolveRunwayStableId(airfield, runway),
                        RunwayName = string.IsNullOrEmpty(runway.DisplayName) ? runway.Id :
                            runway.DisplayName,
                        DirectionAName = first.DisplayName,
                        DirectionBName = second == null ? string.Empty : second.DisplayName,
                        DirectionASelectableIndex = firstSelectable,
                        DirectionBSelectableIndex = secondSelectable,
                        SelectedAirfield = airfield == selectedAirfield,
                        SelectedRunway = runway == selectedRunway,
                        Certified = certifiedRunway,
                        Provisional = provisionalRunway,
                        CertificationBasis = first.CertificationBasis.ToString().ToUpperInvariant(),
                        LatitudeADeg = first.Threshold.LatitudeDeg,
                        LongitudeADeg = first.Threshold.LongitudeDeg,
                        ElevationAMeters = first.Threshold.ElevationMeters,
                        LatitudeBDeg = first.OppositeThreshold.LatitudeDeg,
                        LongitudeBDeg = first.OppositeThreshold.LongitudeDeg,
                        ElevationBMeters = first.OppositeThreshold.ElevationMeters,
                        LengthMeters = Math.Max(0.0, runway.LengthMeters),
                        WidthMeters = Math.Max(0.0, runway.WidthMeters)
                    });
                }
            }

            var snapshot = new AERISNavigationDisplaySnapshot
            {
                Generation = core.Performance.CaptureStamp(),
                BodyName = bodyName,
                BodyRadiusMeters = Math.Max(1.0, vessel.mainBody.Radius),
                OriginLatitudeDeg = vessel.latitude,
                OriginLongitudeDeg = vessel.longitude,
                DatabaseRevision = registry.DatabaseRevision,
                SelectionRevision = registry.SelectionRevision,
                Runways = runwaySources.ToArray(),
                Facilities = facilitySources.ToArray()
            };
            bool submitted = core.Performance.SubmitNavigationDisplay(snapshot);
            if (submitted)
            {
                capturedBodyName = bodyName;
                capturedDatabaseRevision = registry.DatabaseRevision;
                capturedSelectionRevision = registry.SelectionRevision;
                capturedOriginLatitudeDeg = vessel.latitude;
                capturedOriginLongitudeDeg = vessel.longitude;
                nextNavigationCaptureRealtime = now + 10f;
            }
            else
            {
                // Do not mark an unpublished database revision as captured. A busy or
                // transitioning scheduler must be retried quickly; otherwise the ND
                // rejects the previous frame as stale and every runway disappears.
                nextNavigationCaptureRealtime = now + 0.5f;
            }
            core.Performance.RecordNavigationDisplayCapture(
                (Stopwatch.GetTimestamp() - captureStartTicks) *
                    1000.0 / Stopwatch.Frequency,
                facilitySources.Count, runwaySources.Count);
        }

        static void ResolveNavigationDirectionPair(AERISAirfieldRegistry registry,
            AERISAirfieldDefinition airfield, AERISRunwayDefinition runway,
            out AERISRunwayDirectionDefinition first,
            out AERISRunwayDirectionDefinition second)
        {
            first = null;
            second = null;
            if (registry == null || airfield == null || runway == null) return;
            bool manualAuthoritative = registry.HasAuthoritativeUserCalibratedPair(airfield);
            for (int i = 0; i < runway.Directions.Count; i++)
            {
                AERISRunwayDirectionDefinition candidate = runway.Directions[i];
                if (candidate == null || !candidate.HasCertifiedGeometry ||
                    registry.EffectiveState(candidate) !=
                        AERISRunwayCertificationState.Certified) continue;
                if (manualAuthoritative && candidate.CertificationBasis !=
                    AERISRunwayCertificationBasis.UserCalibrated) continue;
                if (first == null)
                {
                    first = candidate;
                    continue;
                }
                if (ReferenceEquals(first, candidate)) continue;
                if (second == null || ReciprocalPairError(first, candidate) <
                    ReciprocalPairError(first, second)) second = candidate;
            }
        }

        static double ReciprocalPairError(AERISRunwayDirectionDefinition first,
            AERISRunwayDirectionDefinition second)
        {
            if (first == null || second == null) return double.MaxValue;
            double delta = Math.Abs(first.HeadingDeg - second.HeadingDeg);
            while (delta >= 360.0) delta -= 360.0;
            if (delta > 180.0) delta = 360.0 - delta;
            return Math.Abs(180.0 - delta);
        }

        static string ResolveRunwayStableId(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway)
        {
            if (runway == null) return string.Empty;
            if (!string.IsNullOrEmpty(runway.StableId)) return runway.StableId;
            string prefix = airfield == null ? string.Empty : airfield.StableId;
            return prefix + "\n" + (runway.Id ?? string.Empty);
        }

        static int FindSelectableDirectionIndex(AERISAirfieldRegistry registry,
            AERISAirfieldDefinition airfield, AERISRunwayDirectionDefinition direction)
        {
            if (registry == null || airfield == null || direction == null) return -1;
            int count = registry.SelectableDirectionCount(airfield);
            for (int i = 0; i < count; i++)
                if (ReferenceEquals(registry.SelectableDirectionAt(airfield, i), direction))
                    return i;
            return -1;
        }

        // Frozen Gate 4A regression token (superseded by shared projection reuse):
        // TryProjectGeographicPoint(vessel.mainBody
        void DrawPreparedNavigation(Rect plot, AERISPreparedNavigationFrame frame,
            Vessel vessel, float range, float heading, bool trackUp, float anchorV,
            double centerEast, double centerNorth, double centerLatitudeDeg,
            double centerLongitudeDeg, float scale, bool drawFacilities)
        {
            if (frame == null) return;
            AERISPreparedFacilitySymbol[] facilities = frame.Facilities ??
                new AERISPreparedFacilitySymbol[0];
            int facilityLimit = core.Terrain != null && core.Terrain.Performance != null ?
                core.Terrain.Performance.ActiveProfile.MaximumFacilitySymbols : 24;
            int facilityDrawn = 0;
            for (int i = 0; drawFacilities && i < facilities.Length &&
                facilityDrawn < facilityLimit; i++)
            {
                AERISPreparedFacilitySymbol facility = facilities[i];
                if (facility == null) continue;
                Vector2 point;
                if (!TryMapPoint(facility.EastMeters - centerEast,
                    facility.NorthMeters - centerNorth, range, heading, trackUp,
                    plot, anchorV, out point)) continue;
                DrawFacilitySymbol(point, (AERISFacilityKind)facility.FacilityKind,
                    facility.Selected ? ArmedColor : new Color(0.62f, 0.86f, 0.94f, 0.88f),
                    Mathf.Max(2f, 3.3f * scale));
                facilityDrawn++;
            }

            AERISPreparedRunwaySymbol[] runways = frame.Runways ??
                new AERISPreparedRunwaySymbol[0];
            if (vessel == null || vessel.mainBody == null) return;
            AERISTerrainRenderTargetOrientation orientation = settings == null ?
                AERISTerrainRenderTargetOrientation.Direct :
                settings.TerrainRenderTargetOrientation;
            if (terrainTileRenderer != null)
            {
                AERISTerrainPresentedProjection presented =
                    terrainTileRenderer.PresentedProjection;
                if (presented.Valid) orientation = presented.Orientation;
            }
            AERISNdMapProjection runwayProjection = AERISNdMapProjection.Create(
                vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg, range,
                heading, trackUp, anchorV, orientation);
            for (int i = runways.Length - 1; i >= 0; i--)
            {
                AERISPreparedRunwaySymbol runway = runways[i];
                if (runway == null) continue;
                if (!runway.SelectedRunway && !RunwayMayIntersectVisibleMap(runway,
                    centerEast, centerNorth, range, anchorV)) continue;
                DrawPreparedRunway(plot, runway, vessel, range, anchorV,
                    centerLatitudeDeg, centerLongitudeDeg, runwayProjection, scale);
            }
        }

        void DrawPreparedRunway(Rect plot, AERISPreparedRunwaySymbol runway,
            Vessel vessel, float range, float anchorV,
            double centerLatitudeDeg, double centerLongitudeDeg,
            AERISNdMapProjection projection, float scale)
        {
            if (runway == null || vessel == null || vessel.mainBody == null) return;
            Vector2 a, b, center;
            bool aInside = TryProjectGeographicPoint(projection,
                runway.LatitudeADeg, runway.LongitudeADeg, plot, out a);
            bool bInside = TryProjectGeographicPoint(projection,
                runway.LatitudeBDeg, runway.LongitudeBDeg, plot, out b);
            bool centerInside = TryProjectGeographicPoint(projection,
                runway.CenterLatitudeDeg, runway.CenterLongitudeDeg, plot, out center);
            bool selected = runway.SelectedRunway ||
                string.Equals(runway.RunwayStableId, previewRunwayStableId,
                    StringComparison.Ordinal);
            Color color = selected ? ArmedColor : (runway.Certified ? RunwayColor :
                (runway.Provisional ? new Color(1.00f, 0.68f, 0.12f, 0.92f) :
                    new Color(0.62f, 0.68f, 0.72f, 0.82f)));
            if (!aInside && !bInside && !centerInside)
            {
                if (runway.SelectedRunway)
                    DrawSelectedRunwayEdgePointer(plot, center, runway, color, scale,
                        anchorV, CurrentRunwayDistanceMeters(vessel.mainBody,
                            centerLatitudeDeg, centerLongitudeDeg, runway));
                return;
            }
            float widthPixels = Mathf.Clamp((float)(Math.Max(8.0, runway.WidthMeters) /
                Math.Max(1.0, range * 2.0) * plot.height), 1.2f, 7f);
            if (selected) widthPixels = Mathf.Max(widthPixels, 2.4f * scale);
            DrawLine(a, b, new Color(0.01f, 0.02f, 0.03f, 0.90f), widthPixels + 2f);
            DrawLine(a, b, color, widthPixels);
            DrawLine(a, b, new Color(color.r, color.g, color.b, 0.52f), 1f);
            bool showRunwayEndNumbers = range <= 20000f;
            Vector2 axis = b - a;
            if (showRunwayEndNumbers && axis.sqrMagnitude > 0.1f)
            {
                axis.Normalize();
                Vector2 perpendicular = new Vector2(-axis.y, axis.x);
                float tick = Mathf.Clamp(4f * scale, 2f, 7f);
                DrawLine(a - perpendicular * tick, a + perpendicular * tick,
                    color, Mathf.Max(1f, 1.3f * scale));
                DrawLine(b - perpendicular * tick, b + perpendicular * tick,
                    color, Mathf.Max(1f, 1.3f * scale));
            }
            // CP3 Gate 4C runway-end declutter: the old 36px label attempted to draw
            // the full direction name (for example "RWY 09") and collapsed to an
            // ellipsis. At the three high-magnification ranges only, draw the compact
            // runway designation itself. At 40 km and above endpoint text is omitted.
            string directionALabel = RunwayDesignationOnly(runway.DirectionAName);
            string directionBLabel = RunwayDesignationOnly(runway.DirectionBName);
            if (showRunwayEndNumbers && aInside &&
                !string.IsNullOrEmpty(directionALabel))
                DrawLabel(new Rect(a.x - 14f, a.y - 16f, 28f, 14f),
                    directionALabel, centerStyle, color);
            if (showRunwayEndNumbers && bInside &&
                !string.IsNullOrEmpty(directionBLabel))
                DrawLabel(new Rect(b.x - 14f, b.y - 16f, 28f, 14f),
                    directionBLabel, centerStyle, color);
            if ((selected || range <= 20000f) && centerInside)
                DrawLabel(new Rect(center.x - 90f, center.y + 4f, 180f, 16f),
                    runway.AirfieldName + (runway.Provisional ? " [PROVISIONAL " +
                        runway.CertificationBasis + "]" : string.Empty), centerStyle, color);
            if (runway.SelectedRunway && !centerInside)
                DrawSelectedRunwayEdgePointer(plot, center, runway, color, scale,
                    anchorV, CurrentRunwayDistanceMeters(vessel.mainBody,
                        centerLatitudeDeg, centerLongitudeDeg, runway));
        }

        static string RunwayDesignationOnly(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string text = value.Trim().ToUpperInvariant();
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsDigit(text[i])) continue;
                int start = i;
                int digits = 0;
                int number = 0;
                while (i < text.Length && char.IsDigit(text[i]) && digits < 2)
                {
                    number = number * 10 + (text[i] - '0');
                    digits++;
                    i++;
                }
                if (digits == 0 || number < 1 || number > 36)
                {
                    i = start;
                    continue;
                }
                char suffix = '\0';
                if (i < text.Length && (text[i] == 'L' || text[i] == 'C' ||
                    text[i] == 'R')) suffix = text[i];
                string result = number.ToString("00");
                return suffix == '\0' ? result : result + suffix;
            }
            return string.Empty;
        }

        void DrawSelectedRunwayEdgePointer(Rect plot, Vector2 outside,
            AERISPreparedRunwaySymbol runway, Color color, float scale, float anchorV,
            double distanceMeters)
        {
            // Track-up places the aircraft at 75% plot height.  An off-scale runway
            // pointer must originate from the same anchor, not plot.center; otherwise
            // a distant airport appears as a stationary runway displaced over water.
            Vector2 origin = new Vector2(plot.center.x,
                plot.y + plot.height * Mathf.Clamp01(anchorV));
            Vector2 delta = outside - origin;
            if (delta.sqrMagnitude < 0.01f) return;
            const float inset = 9f;
            float best = float.MaxValue;
            if (Mathf.Abs(delta.x) >= 0.001f)
            {
                float targetX = delta.x > 0f ? plot.xMax - inset : plot.xMin + inset;
                float value = (targetX - origin.x) / delta.x;
                if (value > 0f) best = Mathf.Min(best, value);
            }
            if (Mathf.Abs(delta.y) >= 0.001f)
            {
                float targetY = delta.y > 0f ? plot.yMax - inset : plot.yMin + inset;
                float value = (targetY - origin.y) / delta.y;
                if (value > 0f) best = Mathf.Min(best, value);
            }
            if (best == float.MaxValue || float.IsNaN(best) || float.IsInfinity(best))
                return;
            Vector2 edge = origin + delta * best;
            edge.x = Mathf.Clamp(edge.x, plot.xMin + inset, plot.xMax - inset);
            edge.y = Mathf.Clamp(edge.y, plot.yMin + inset, plot.yMax - inset);
            Vector2 unit = delta.normalized;
            Vector2 perpendicular = new Vector2(-unit.y, unit.x);
            float size = Mathf.Max(4f, 6f * scale);
            DrawLine(edge, edge - unit * size + perpendicular * size * 0.55f,
                color, 1.5f);
            DrawLine(edge, edge - unit * size - perpendicular * size * 0.55f,
                color, 1.5f);
            string label = (runway.RunwayName ?? string.Empty) + " " +
                FormatDistance(distanceMeters);
            float labelWidth = Mathf.Max(88f, 118f * scale);
            float labelHeight = Mathf.Max(14f, 16f * scale);
            float labelX = Mathf.Clamp(edge.x - labelWidth * 0.5f, plot.xMin + 2f,
                plot.xMax - labelWidth - 2f);
            float labelY = edge.y - labelHeight - 3f;
            if (labelY < plot.yMin + 2f) labelY = edge.y + 3f;
            labelY = Mathf.Clamp(labelY, plot.yMin + 2f, plot.yMax - labelHeight - 2f);
            DrawLabel(new Rect(labelX, labelY, labelWidth, labelHeight), label,
                centerStyle, color);
        }

        static bool RunwayMayIntersectVisibleMap(AERISPreparedRunwaySymbol runway,
            double centerEastMeters, double centerNorthMeters, float rangeMeters,
            float anchorV)
        {
            if (runway == null) return false;
            double east = runway.CenterEastMeters - centerEastMeters;
            double north = runway.CenterNorthMeters - centerNorthMeters;
            if (double.IsNaN(east) || double.IsInfinity(east) ||
                double.IsNaN(north) || double.IsInfinity(north)) return true;

            // AERISNdMapProjection uses +/-0.65*range horizontally and an anchor-dependent
            // vertical extent. Use a conservative circumscribed radius plus runway length
            // so this is only a cheap rejection test; anything that could touch the ND
            // viewport still reaches the exact spherical projection below.
            double horizontal = Math.Max(1.0, rangeMeters * 0.65);
            double vertical = Math.Max(1.0, rangeMeters *
                Math.Max(Mathf.Clamp01(anchorV), 1f - Mathf.Clamp01(anchorV)));
            double visibleRadius = Math.Sqrt(horizontal * horizontal +
                vertical * vertical);
            double padding = Math.Max(15000.0, Math.Abs(runway.LengthMeters) + 10000.0);
            double limit = visibleRadius * 1.35 + padding;
            return east * east + north * north <= limit * limit;
        }

        static double CurrentRunwayDistanceMeters(CelestialBody body,
            double centerLatitudeDeg, double centerLongitudeDeg,
            AERISPreparedRunwaySymbol runway)
        {
            if (body == null || runway == null) return 0.0;
            double east, north;
            ToLocalMeters(body, centerLatitudeDeg, centerLongitudeDeg,
                runway.CenterLatitudeDeg, runway.CenterLongitudeDeg, out east, out north);
            return Math.Sqrt(east * east + north * north);
        }

        void HandleMapInteraction(Rect plot, AERISPreparedNavigationFrame frame,
            AERISPreparedTrafficFrame trafficFrame, Vessel vessel, float range,
            Rect auxiliaryMenuRect)
        {
            Event e = Event.current;
            if (e == null || vessel == null || vessel.mainBody == null) return;
            if (auxiliaryMenuOpen && auxiliaryMenuRect.width > 0f &&
                auxiliaryMenuRect.Contains(e.mousePosition)) return;
            Rect interactivePlot = plot;
            if (!string.IsNullOrEmpty(previewRunwayStableId) ||
                !string.IsNullOrEmpty(previewTrafficStableId))
                interactivePlot.height = Mathf.Max(1f, interactivePlot.height - 56f);
            if (e.type == EventType.MouseDown && e.button == 0 &&
                interactivePlot.Contains(e.mousePosition))
            {
                mapPointerDown = true;
                mapDragging = false;
                mapPointerStart = e.mousePosition;
                mapPointerLast = e.mousePosition;
                e.Use();
                return;
            }
            if (e.type == EventType.MouseDrag && e.button == 0 && mapPointerDown)
            {
                Vector2 total = e.mousePosition - mapPointerStart;
                if (!mapDragging && total.sqrMagnitude >= 16f)
                {
                    mapDragging = true;
                    if (!planMode)
                    {
                        orientationBeforePlanTrackUp = settings.NavigationDisplayTrackUp;
                        planMode = true;
                        planCenterLatitudeDeg = vessel.latitude;
                        planCenterLongitudeDeg = vessel.longitude;
                    }
                }
                if (mapDragging)
                {
                    Vector2 delta = e.mousePosition - mapPointerLast;
                    // Exact inverse of TryMapPoint's horizontal/vertical map scales.
                    double east = -delta.x / Math.Max(1.0, plot.width) * range * 1.30;
                    double north = delta.y / Math.Max(1.0, plot.height) * range;
                    OffsetLatLon(vessel.mainBody, planCenterLatitudeDeg,
                        planCenterLongitudeDeg, east, north,
                        out planCenterLatitudeDeg, out planCenterLongitudeDeg);
                    mapPointerLast = e.mousePosition;
                }
                e.Use();
                return;
            }
            if (e.type == EventType.MouseUp && e.button == 0 && mapPointerDown)
            {
                if (!mapDragging && !PreviewTrafficAt(e.mousePosition, plot,
                    trafficFrame, vessel, range))
                    PreviewRunwayAt(e.mousePosition, plot, frame, vessel, range);
                mapPointerDown = false;
                mapDragging = false;
                e.Use();
            }
        }

        void PreviewRunwayAt(Vector2 mouse, Rect plot,
            AERISPreparedNavigationFrame frame, Vessel vessel, float range)
        {
            if (frame == null || vessel == null || vessel.mainBody == null) return;
            double centerLatitudeDeg = planMode ? planCenterLatitudeDeg :
                vessel.latitude;
            double centerLongitudeDeg = planMode ? planCenterLongitudeDeg :
                vessel.longitude;
            bool trackUp = !planMode && settings.NavigationDisplayTrackUp;
            float heading = trackUp ? cachedFallbackMapHeading : 0f;
            float anchorV = planMode || !trackUp ? 0.5f : 0.75f;
            AERISTerrainRenderTargetOrientation orientation = settings == null ?
                AERISTerrainRenderTargetOrientation.Direct :
                settings.TerrainRenderTargetOrientation;

            // Hit testing must use the exact same committed GPU FRONT projection as the
            // visible runway layer. Candidate 7 used the live requested view here, so during
            // a latched terrain FRONT an off-screen runway could be selected invisibly and
            // then appear as the selected edge pointer that followed the aircraft.
            AERISLandingFoundation landing = core.Landing;
            AERISRunwayDirectionDefinition activeDirection =
                landing == null ? null : landing.ActiveDirection;
            AERISRunwayObservation activeObservation =
                landing == null ? null : landing.Observation;
            bool landingViewActive = landing != null && landing.Armed &&
                activeDirection != null && activeObservation != null &&
                activeObservation.Valid;
            bool terrainPresentationActive = planMode || !landingViewActive ||
                (settings != null && settings.NavigationDisplayLandOverlay);
            if (terrainPresentationActive && terrainTileRenderer != null)
            {
                AERISTerrainPresentedProjection presented =
                    terrainTileRenderer.PresentedProjection;
                if (presented.Valid)
                {
                    centerLatitudeDeg = presented.CenterLatitudeDeg;
                    centerLongitudeDeg = presented.CenterLongitudeDeg;
                    range = presented.RangeMeters;
                    heading = presented.MapHeadingDeg;
                    trackUp = presented.TrackUp;
                    anchorV = presented.AnchorV;
                    orientation = presented.Orientation;
                }
            }

            AERISNdMapProjection projection = AERISNdMapProjection.Create(
                vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg, range,
                heading, trackUp, anchorV, orientation);
            double centerEast = 0.0, centerNorth = 0.0;
            ToLocalMeters(vessel.mainBody, frame.OriginLatitudeDeg,
                frame.OriginLongitudeDeg, centerLatitudeDeg, centerLongitudeDeg,
                out centerEast, out centerNorth);

            AERISPreparedRunwaySymbol best = null;
            int bestDirection = -1;
            float bestDistance = 14f;
            AERISPreparedRunwaySymbol[] runways = frame.Runways ??
                new AERISPreparedRunwaySymbol[0];
            for (int i = 0; i < runways.Length; i++)
            {
                AERISPreparedRunwaySymbol runway = runways[i];
                if (runway == null || !RunwayMayIntersectVisibleMap(runway,
                    centerEast, centerNorth, range, anchorV)) continue;
                Vector2 a, b, center;
                bool aInside = TryProjectGeographicPoint(projection,
                    runway.LatitudeADeg, runway.LongitudeADeg, plot, out a);
                bool bInside = TryProjectGeographicPoint(projection,
                    runway.LatitudeBDeg, runway.LongitudeBDeg, plot, out b);
                bool centerInside = TryProjectGeographicPoint(projection,
                    runway.CenterLatitudeDeg, runway.CenterLongitudeDeg, plot, out center);
                if (!aInside && !bInside && !centerInside) continue;
                float distance = DistancePointToSegment(mouse, a, b);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = runway;
                bestDirection = Vector2.Distance(mouse, a) <= Vector2.Distance(mouse, b) ?
                    runway.DirectionASelectableIndex : runway.DirectionBSelectableIndex;
            }
            if (best == null) return;
            previewTrafficStableId = string.Empty;
            previewTrafficMessage = string.Empty;
            previewRunwayStableId = best.RunwayStableId;
            previewAirfieldIndex = best.AirfieldIndex;
            previewDirectionIndex = bestDirection >= 0 ? bestDirection :
                (best.DirectionASelectableIndex >= 0 ? best.DirectionASelectableIndex :
                best.DirectionBSelectableIndex);
            previewMessage = string.Empty;
        }

        void DrawPreviewPanel(Rect plot, AERISPreparedNavigationFrame frame,
            AERISPreparedTrafficFrame trafficFrame, float range, float scale)
        {
            AERISPreparedTrafficSymbol traffic = FindPreparedTraffic(trafficFrame,
                previewTrafficStableId);
            if (traffic != null)
            {
                DrawTrafficPreviewPanel(plot, traffic, scale);
                return;
            }
            AERISPreparedRunwaySymbol runway = FindPreparedRunway(frame,
                previewRunwayStableId);
            AERISAirfieldRegistry registry = core.Airfields;
            if (runway == null && registry != null && registry.SelectedRunway != null)
            {
                string selectedStableId = ResolveRunwayStableId(
                    registry.SelectedAirfield, registry.SelectedRunway);
                runway = FindPreparedRunway(frame, selectedStableId);
                if (runway != null)
                {
                    previewRunwayStableId = selectedStableId;
                    previewAirfieldIndex = registry.SelectedAirfieldIndex;
                    previewDirectionIndex = registry.SelectedDirectionIndex;
                }
            }
            if (runway == null) return;
            float height = Mathf.Max(43f, 49f * scale);
            Rect panel = new Rect(plot.x + 4f, plot.yMax - height - 4f,
                Mathf.Max(120f, plot.width - 8f), height);
            FillRect(panel, new Color(0.015f, 0.025f, 0.035f, 0.94f));
            DrawRectOutline(panel, ArmedColor, 1f);
            string status = runway.Certified ? "CERT" :
                (runway.Provisional ? "PROVISIONAL — NEVER ARM" : "NOT CERT");
            string line = runway.AirfieldName + "  " + runway.RunwayName + "  " +
                FormatDistance(runway.DistanceFromOriginMeters) + "  " + status;
            DrawLabel(new Rect(panel.x + 4f, panel.y + 1f, panel.width - 8f,
                Mathf.Max(14f, 17f * scale)), line, textStyle,
                runway.Certified ? RunwayColor : ArmedColor);
            string detail = "LEN " + Math.Max(0.0, runway.LengthMeters).ToString("0") +
                "m  ELEV " + runway.ElevationMeters.ToString("0") + "m";
            if (!string.IsNullOrEmpty(previewMessage)) detail += "  " + previewMessage;
            DrawLabel(new Rect(panel.x + 4f, panel.y + Mathf.Max(14f, 17f * scale),
                panel.width - 8f, Mathf.Max(14f, 17f * scale)), detail,
                textStyle, new Color(0.72f, 0.86f, 0.92f, 1f));

            float buttonHeight = Mathf.Max(16f, 20f * scale);
            float gap = Mathf.Max(1f, 2f * scale);
            float available = Mathf.Max(150f, panel.width - 8f - gap * 4f);
            float weightTotal = 4.90f;
            float unit = available / weightTotal;
            float armWidth = unit * 0.82f;
            float centerWidth = unit * 1.00f;
            float directionWidth = unit * 1.18f;
            float selectWidth = unit * 1.00f;
            float clearWidth = unit * 0.90f;
            bool compact = panel.width < Mathf.Max(280f, 310f * scale);
            float y = panel.yMax - buttonHeight - 2f;
            float x = panel.x + 4f;
            bool oldEnabled = GUI.enabled;
            Color oldBackground = GUI.backgroundColor;
            try
            {
                bool confirmed = registry != null && registry.SelectedRunway != null &&
                    string.Equals(ResolveRunwayStableId(registry.SelectedAirfield,
                        registry.SelectedRunway), runway.RunwayStableId,
                        StringComparison.Ordinal) && registry.SelectedDirection != null;
                GUI.enabled = confirmed;
                if (GUI.Button(new Rect(x, y, armWidth, buttonHeight),
                    new GUIContent("ARM", "Arm LAND observation for the selected approach"),
                    buttonStyle))
                {
                    string error;
                    bool armed = core.ArmLanding(out error);
                    previewMessage = armed ? "LAND OBS ARMED" : error;
                }
                x += armWidth + gap;

                GUI.enabled = true;
                if (GUI.Button(new Rect(x, y, centerWidth, buttonHeight),
                    new GUIContent(compact ? "CTR" : "CENTER",
                        "Center PLAN view on this runway"), buttonStyle))
                    EnterPlanAt(runway.CenterLatitudeDeg, runway.CenterLongitudeDeg);
                x += centerWidth + gap;

                AERISRunwayDirectionDefinition previewDirection =
                    PreviewDirection(registry, runway);
                bool canCycleDirection = runway.DirectionASelectableIndex >= 0 &&
                    runway.DirectionBSelectableIndex >= 0 &&
                    runway.DirectionASelectableIndex != runway.DirectionBSelectableIndex;
                GUI.enabled = previewDirection != null &&
                    (runway.DirectionASelectableIndex >= 0 ||
                     runway.DirectionBSelectableIndex >= 0);
                string directionLabel = ApproachButtonLabel(previewDirection, compact);
                if (GUI.Button(new Rect(x, y, directionWidth, buttonHeight),
                    new GUIContent(directionLabel,
                        canCycleDirection ? "Choose the runway end used for approach" :
                            "Only one certified approach direction is available"),
                    buttonStyle) && canCycleDirection)
                {
                    previewDirectionIndex = previewDirectionIndex ==
                        runway.DirectionASelectableIndex ?
                        runway.DirectionBSelectableIndex :
                        runway.DirectionASelectableIndex;
                    previewDirection = PreviewDirection(registry, runway);
                    previewMessage = previewDirection == null ? string.Empty :
                        "APP " + previewDirection.DisplayName;
                }
                x += directionWidth + gap;

                GUI.enabled = runway.Certified && previewAirfieldIndex >= 0 &&
                    previewDirectionIndex >= 0;
                if (GUI.Button(new Rect(x, y, selectWidth, buttonHeight),
                    new GUIContent(compact ? "SEL" : "SELECT",
                        "Select this runway and the displayed approach direction"),
                    buttonStyle))
                {
                    bool selected = registry != null &&
                        registry.SelectAirfield(previewAirfieldIndex) &&
                        registry.SelectDirection(previewDirectionIndex);
                    previewMessage = selected ? "SELECTED" : "SELECT FAILED";
                }
                x += selectWidth + gap;

                GUI.enabled = registry != null && registry.SelectedAirfield != null;
                GUI.backgroundColor = new Color(0.52f, 0.28f, 0.22f, 0.96f);
                if (GUI.Button(new Rect(x, y, clearWidth, buttonHeight),
                    new GUIContent(compact ? "CLR" : "CLEAR",
                        "Disarm LAND observation and clear the selected runway"),
                    buttonStyle))
                    ClearSelectionFromNd(registry);
            }
            finally
            {
                GUI.enabled = oldEnabled;
                GUI.backgroundColor = oldBackground;
            }
        }

        AERISRunwayDirectionDefinition PreviewDirection(
            AERISAirfieldRegistry registry, AERISPreparedRunwaySymbol runway)
        {
            if (registry == null || runway == null || previewAirfieldIndex < 0)
                return null;
            AERISAirfieldDefinition airfield = registry.At(previewAirfieldIndex);
            if (airfield == null) return null;
            AERISRunwayDirectionDefinition direction =
                registry.SelectableDirectionAt(airfield, previewDirectionIndex);
            if (direction != null) return direction;
            int fallback = runway.DirectionASelectableIndex >= 0 ?
                runway.DirectionASelectableIndex : runway.DirectionBSelectableIndex;
            previewDirectionIndex = fallback;
            return fallback < 0 ? null : registry.SelectableDirectionAt(airfield, fallback);
        }

        static string ApproachButtonLabel(AERISRunwayDirectionDefinition direction,
            bool compact)
        {
            if (direction == null) return compact ? "APP" : "APP N/A";
            string value = direction.DisplayName ?? string.Empty;
            if (value.StartsWith("RWY ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(4).Trim();
            if (string.IsNullOrEmpty(value)) value = "N/A";
            return compact ? "RWY" + value : "APP " + value;
        }

        bool ClearSelectionFromNd(AERISAirfieldRegistry registry)
        {
            if (registry == null || registry.SelectedAirfield == null) return false;
            if (core.Landing != null) core.Landing.Disarm("ND selection cleared");
            bool cleared = registry.ClearSelection();
            previewRunwayStableId = string.Empty;
            previewAirfieldIndex = -1;
            previewDirectionIndex = -1;
            previewMessage = cleared ? "SELECTION CLEARED" : "CLEAR FAILED";
            cachedLandingObservation = null;
            cachedLandingDirection = null;
            capturedSelectionRevision = -1L;
            AERISPreparedNavigationFrameApi.Clear();
            AERISLogger.Info("[ND/LAND] CLEAR pressed; result=" + cleared + ".");
            return cleared;
        }

        static AERISPreparedRunwaySymbol FindPreparedRunway(
            AERISPreparedNavigationFrame frame, string stableId)
        {
            if (frame == null || string.IsNullOrEmpty(stableId)) return null;
            AERISPreparedRunwaySymbol[] runways = frame.Runways ??
                new AERISPreparedRunwaySymbol[0];
            for (int i = 0; i < runways.Length; i++)
                if (runways[i] != null && string.Equals(runways[i].RunwayStableId,
                    stableId, StringComparison.Ordinal)) return runways[i];
            return null;
        }

        bool PreviewTrafficAt(Vector2 mouse, Rect plot,
            AERISPreparedTrafficFrame frame, Vessel vessel, float range)
        {
            if (!settings.NavigationDisplayTrafficEnabled || frame == null ||
                vessel == null || vessel.mainBody == null) return false;
            double centerLatitude = planMode ? planCenterLatitudeDeg : vessel.latitude;
            double centerLongitude = planMode ? planCenterLongitudeDeg : vessel.longitude;
            double centerEast, centerNorth;
            ToLocalMeters(vessel.mainBody, frame.OriginLatitudeDeg,
                frame.OriginLongitudeDeg, centerLatitude, centerLongitude,
                out centerEast, out centerNorth);
            bool trackUp = !planMode && settings.NavigationDisplayTrackUp;
            float heading = trackUp ? cachedFallbackMapHeading : 0f;
            float anchorV = planMode || !trackUp ? 0.5f : 0.75f;
            AERISPreparedTrafficSymbol best = null;
            float bestDistance = 12f;
            AERISPreparedTrafficSymbol[] symbols = frame.Traffic ??
                new AERISPreparedTrafficSymbol[0];
            for (int i = 0; i < symbols.Length; i++)
            {
                AERISPreparedTrafficSymbol item = symbols[i];
                if (item == null || string.IsNullOrEmpty(item.StableId)) continue;
                Vector2 point;
                if (!TryMapPoint(item.EastMeters - centerEast,
                    item.NorthMeters - centerNorth, range, heading, trackUp,
                    plot, anchorV, out point)) continue;
                float distance = Vector2.Distance(mouse, point);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = item;
            }
            if (best == null) return false;
            previewTrafficStableId = best.StableId;
            previewTrafficMessage = string.Empty;
            previewRunwayStableId = string.Empty;
            previewAirfieldIndex = -1;
            previewDirectionIndex = -1;
            previewMessage = string.Empty;
            return true;
        }

        static AERISPreparedTrafficSymbol FindPreparedTraffic(
            AERISPreparedTrafficFrame frame, string stableId)
        {
            if (frame == null || string.IsNullOrEmpty(stableId)) return null;
            AERISPreparedTrafficSymbol[] traffic = frame.Traffic ??
                new AERISPreparedTrafficSymbol[0];
            for (int i = 0; i < traffic.Length; i++)
                if (traffic[i] != null && string.Equals(traffic[i].StableId,
                    stableId, StringComparison.Ordinal)) return traffic[i];
            return null;
        }

        void DrawTrafficPreviewPanel(Rect plot, AERISPreparedTrafficSymbol traffic,
            float scale)
        {
            float height = Mathf.Max(43f, 49f * scale);
            Rect panel = new Rect(plot.x + 4f, plot.yMax - height - 4f,
                Mathf.Max(120f, plot.width - 8f), height);
            FillRect(panel, new Color(0.015f, 0.025f, 0.035f, 0.94f));
            Color color = TrafficColor(traffic.ThreatLevel);
            DrawRectOutline(panel, color, 1f);
            string altitude = FormatSignedAltitude(traffic.RelativeAltitudeMeters);
            string line = "TRAFFIC  " + (traffic.Name ?? string.Empty) + "  " + altitude;
            DrawLabel(new Rect(panel.x + 4f, panel.y + 1f, panel.width - 8f,
                Mathf.Max(14f, 17f * scale)), line, textStyle, color);
            string detail = "GS " + traffic.GroundSpeedMps.ToString("0") + "m/s";
            if (traffic.ThreatLevel > 0)
                detail += "  CPA " + traffic.ClosestApproachMeters.ToString("0") +
                    "m / " + traffic.ClosestApproachSeconds.ToString("0") + "s";
            if (!string.IsNullOrEmpty(previewTrafficMessage))
                detail += "  " + previewTrafficMessage;
            DrawLabel(new Rect(panel.x + 4f, panel.y + Mathf.Max(14f, 17f * scale),
                panel.width * 0.64f, Mathf.Max(14f, 17f * scale)), detail,
                textStyle, new Color(0.72f, 0.86f, 0.92f, 1f));
            float buttonWidth = Mathf.Max(42f, 53f * scale);
            float buttonHeight = Mathf.Max(16f, 20f * scale);
            if (GUI.Button(new Rect(panel.xMax - buttonWidth,
                panel.yMax - buttonHeight - 2f, buttonWidth, buttonHeight),
                "CENTER", buttonStyle))
            {
                EnterPlanAt(traffic.LatitudeDeg, traffic.LongitudeDeg);
                previewTrafficMessage = "CENTERED";
            }
        }

        void DrawTrail(Rect plot, Vessel vessel, float range, float heading,
            bool trackUp, float anchorV, double centerLatitudeDeg,
            double centerLongitudeDeg, float scale)
        {
            if (!settings.NavigationDisplayTrailEnabled || vessel == null ||
                vessel.mainBody == null || trailSamples.Count < 2) return;
            Vector2 previous = new Vector2();
            bool previousInside = false;
            int count = trailSamples.Count;
            int stride = count > 600 ? 3 : (count > 300 ? 2 : 1);
            for (int i = 0; i < count; i += stride)
            {
                TrailSample sample = trailSamples[i];
                if (sample == null || sample.VesselPersistentId != vessel.persistentId ||
                    !string.Equals(sample.BodyName, vessel.mainBody.name,
                        StringComparison.OrdinalIgnoreCase)) continue;
                double east, north;
                ToLocalMeters(vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg,
                    sample.LatitudeDeg, sample.LongitudeDeg, out east, out north);
                Vector2 point;
                bool inside = TryMapPoint(east, north, range, heading, trackUp,
                    plot, anchorV, out point);
                if (previousInside && inside)
                {
                    float ageRatio = count <= 1 ? 1f : i / (float)(count - 1);
                    Color color = new Color(0.30f, 0.88f, 0.96f,
                        Mathf.Lerp(0.12f, 0.68f, ageRatio));
                    DrawLine(previous, point, color, Mathf.Max(1f, 1.2f * scale));
                }
                previous = point;
                previousInside = inside;
            }
        }

        void DrawTrackVector(Rect plot, Vector2 aircraftPoint, Vessel vessel,
            float range, float heading, bool trackUp, float anchorV,
            double centerLatitudeDeg, double centerLongitudeDeg, float scale)
        {
            if (!settings.NavigationDisplayTrackVectorEnabled || vessel == null ||
                vessel.mainBody == null || planMode || vessel.srfSpeed < 1.0) return;

            // CP3.75 Candidate 2: all vector geometry is map-center-relative. Candidate 1
            // projected the vector endpoint as though ownship were always at the map center,
            // while the line start used the actual ownship point. A latched GPU FRONT therefore
            // made the vector breathe/collapse as ownship moved away from the committed center.
            double ownEast, ownNorth;
            ToLocalMeters(vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg,
                vessel.latitude, vessel.longitude, out ownEast, out ownNorth);

            double speed = Math.Max(1.0, vessel.srfSpeed);
            const double horizonSeconds = 60.0;
            double distance = Math.Min(range * 0.42, speed * horizonSeconds);
            double trackRad = cachedFallbackMapHeading * Math.PI / 180.0;
            double east = ownEast + Math.Sin(trackRad) * distance;
            double north = ownNorth + Math.Cos(trackRad) * distance;
            Vector2 end;
            TryMapPoint(east, north, range, heading, trackUp, plot, anchorV, out end);
            end = ClampToRect(end, plot, 3f);
            Color color = new Color(0.55f, 0.96f, 1f, 0.88f);
            DrawLine(aircraftPoint, end, color, Mathf.Max(1f, 1.2f * scale));
            int[] tickSeconds = { 15, 30, 45, 60 };
            for (int i = 0; i < tickSeconds.Length; i++)
            {
                double tickDistance = speed * tickSeconds[i];
                if (tickDistance > distance + 1.0) continue;
                double tickEast = ownEast + Math.Sin(trackRad) * tickDistance;
                double tickNorth = ownNorth + Math.Cos(trackRad) * tickDistance;
                Vector2 tick;
                if (!TryMapPoint(tickEast, tickNorth, range, heading, trackUp,
                    plot, anchorV, out tick)) continue;
                Vector2 direction = (end - aircraftPoint).normalized;
                Vector2 perpendicular = new Vector2(-direction.y, direction.x);
                DrawLine(tick - perpendicular * 2.5f, tick + perpendicular * 2.5f,
                    color, 1f);
                if (tickSeconds[i] == 30 || tickSeconds[i] == 60)
                    DrawLabel(new Rect(tick.x + 3f, tick.y - 8f, 24f, 13f),
                        tickSeconds[i].ToString(), textStyle, color);
            }
        }

        void DrawPreparedTraffic(Rect plot, AERISPreparedTrafficFrame frame,
            Vessel vessel, float range, float heading, bool trackUp, float anchorV,
            double centerLatitudeDeg, double centerLongitudeDeg, float scale)
        {
            if (!settings.NavigationDisplayTrafficEnabled || frame == null ||
                vessel == null || vessel.mainBody == null) return;
            double centerEast, centerNorth;
            ToLocalMeters(vessel.mainBody, frame.OriginLatitudeDeg,
                frame.OriginLongitudeDeg, centerLatitudeDeg, centerLongitudeDeg,
                out centerEast, out centerNorth);
            AERISPreparedTrafficSymbol[] traffic = frame.Traffic ??
                new AERISPreparedTrafficSymbol[0];
            int labelLimit = range <= 40000f ? 24 : 12;
            int labels = 0;
            for (int i = 0; i < traffic.Length; i++)
            {
                AERISPreparedTrafficSymbol item = traffic[i];
                if (item == null || string.IsNullOrEmpty(item.StableId)) continue;
                Vector2 point;
                if (!TryMapPoint(item.EastMeters - centerEast,
                    item.NorthMeters - centerNorth, range, heading, trackUp,
                    plot, anchorV, out point)) continue;
                Color color = TrafficColor(item.ThreatLevel);
                DrawTrafficSymbol(point, item.GroundTrackDeg, heading, trackUp,
                    color, scale, item.ThreatLevel > 0);
                bool selected = string.Equals(previewTrafficStableId, item.StableId,
                    StringComparison.Ordinal);
                if (selected || item.ThreatLevel > 0 || labels < labelLimit)
                {
                    string label = FormatSignedAltitude(item.RelativeAltitudeMeters);
                    if (item.ThreatLevel > 0)
                        label += "  CPA " + item.ClosestApproachSeconds.ToString("0") + "s";
                    DrawLabel(new Rect(point.x + 6f, point.y - 9f, 100f, 16f),
                        label, textStyle, color);
                    labels++;
                }
            }
        }

        static void DrawTrafficSymbol(Vector2 point, double targetHeadingDeg,
            double mapHeadingDeg, bool trackUp, Color color, float scale, bool ring)
        {
            float relative = (float)(targetHeadingDeg - (trackUp ? mapHeadingDeg : 0.0));
            float radians = relative * Mathf.Deg2Rad;
            Vector2 forward = new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
            Vector2 right = new Vector2(-forward.y, forward.x);
            float size = Mathf.Max(3f, 4.5f * scale);
            Vector2 nose = point + forward * size;
            Vector2 left = point - forward * size * 0.65f - right * size * 0.65f;
            Vector2 rightPoint = point - forward * size * 0.65f + right * size * 0.65f;
            DrawLine(nose, left, color, 1.2f);
            DrawLine(left, rightPoint, color, 1.2f);
            DrawLine(rightPoint, nose, color, 1.2f);
            DrawLine(point - forward * size * 0.2f,
                point - forward * size * 1.4f, color, 1f);
            if (ring) DrawArc(point, size * 1.8f, 0f, 360f, 18, color, 1f);
        }

        void DrawWindOverlay(Rect plot, Vessel vessel, float mapHeading,
            bool trackUp, float scale)
        {
            if (!settings.NavigationDisplayWindEnabled || !windValid ||
                vessel == null) return;
            double east = windSample.EastMetersPerSecond;
            double north = windSample.NorthMetersPerSecond;
            double speed = Math.Sqrt(east * east + north * north);
            if (speed < 0.05) return;
            double windTo = NormalizeHeading(Math.Atan2(east, north) * 180.0 / Math.PI);
            double windFrom = NormalizeHeading(windTo + 180.0);
            double trackRad = cachedFallbackMapHeading * Math.PI / 180.0;
            double trackEast = Math.Sin(trackRad);
            double trackNorth = Math.Cos(trackRad);
            double headwind = -(east * trackEast + north * trackNorth);
            double crosswind = east * Math.Cos(trackRad) - north * Math.Sin(trackRad);
            float width = Mathf.Max(92f, 116f * scale);
            float height = Mathf.Max(29f, 36f * scale);
            Rect panel = new Rect(plot.x + 3f, plot.y + 3f, width, height);
            FillRect(panel, new Color(0.01f, 0.02f, 0.03f, 0.76f));
            Color color = new Color(0.76f, 0.94f, 1f, 0.94f);
            string line1 = "WIND " + windFrom.ToString("000") + "  " +
                speed.ToString("0.0") + "m/s";
            string line2 = "H " + headwind.ToString("+0.0;-0.0;0.0") +
                "  X " + crosswind.ToString("+0.0;-0.0;0.0");
            DrawLabel(new Rect(panel.x + 3f, panel.y, panel.width - 25f,
                height * 0.5f), line1, textStyle, color);
            DrawLabel(new Rect(panel.x + 3f, panel.y + height * 0.46f,
                panel.width - 6f, height * 0.5f), line2, textStyle, color);
            float relative = (float)(windTo - (trackUp ? mapHeading : 0f));
            float radians = relative * Mathf.Deg2Rad;
            Vector2 vector = new Vector2(Mathf.Sin(radians), -Mathf.Cos(radians));
            Vector2 center = new Vector2(panel.xMax - 13f, panel.y + height * 0.48f);
            Vector2 end = center + vector * Mathf.Max(7f, 9f * scale);
            DrawLine(center, end, color, 1.4f);
            Vector2 perpendicular = new Vector2(-vector.y, vector.x);
            DrawLine(end, end - vector * 3f + perpendicular * 2f, color, 1.2f);
            DrawLine(end, end - vector * 3f - perpendicular * 2f, color, 1.2f);
        }

        static Color TrafficColor(int threatLevel)
        {
            if (threatLevel >= 2) return new Color(1f, 0.22f, 0.20f, 1f);
            if (threatLevel == 1) return new Color(1f, 0.74f, 0.18f, 1f);
            return new Color(0.62f, 0.94f, 1f, 0.94f);
        }

        static string FormatSignedAltitude(double meters)
        {
            double absolute = Math.Abs(meters);
            if (absolute >= 1000.0)
                return (meters / 1000.0).ToString("+0.0;-0.0;0.0") + "k";
            return meters.ToString("+0;-0;0") + "m";
        }

        static Vector2 ClampToRect(Vector2 point, Rect rect, float inset)
        {
            return new Vector2(Mathf.Clamp(point.x, rect.xMin + inset, rect.xMax - inset),
                Mathf.Clamp(point.y, rect.yMin + inset, rect.yMax - inset));
        }

        void EnterPlanAt(double latitudeDeg, double longitudeDeg)
        {
            if (!planMode) orientationBeforePlanTrackUp = settings.NavigationDisplayTrackUp;
            planMode = true;
            planCenterLatitudeDeg = latitudeDeg;
            planCenterLongitudeDeg = longitudeDeg;
        }

        void RecenterPlan(Vessel vessel)
        {
            planMode = false;
            settings.NavigationDisplayTrackUp = orientationBeforePlanTrackUp;
            if (vessel != null)
            {
                planCenterLatitudeDeg = vessel.latitude;
                planCenterLongitudeDeg = vessel.longitude;
            }
            SaveSettingsAndProfile();
        }

        static void OffsetLatLon(CelestialBody body, double latitudeDeg,
            double longitudeDeg, double eastMeters, double northMeters,
            out double resultLatitudeDeg, out double resultLongitudeDeg)
        {
            double radius = body == null ? 1.0 : Math.Max(1.0, body.Radius);
            double latitudeRadians = latitudeDeg * Math.PI / 180.0;
            resultLatitudeDeg = latitudeDeg + northMeters / radius * 180.0 / Math.PI;
            double cosine = Math.Max(0.01, Math.Abs(Math.Cos(latitudeRadians)));
            resultLongitudeDeg = NormalizeLongitude(longitudeDeg +
                eastMeters / (radius * cosine) * 180.0 / Math.PI);
            resultLatitudeDeg = Math.Max(-90.0, Math.Min(90.0, resultLatitudeDeg));
        }

        static float DistancePointToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float denominator = ab.sqrMagnitude;
            if (denominator <= 0.0001f) return Vector2.Distance(point, a);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
            return Vector2.Distance(point, a + ab * t);
        }

        static string FormatDistance(double meters)
        {
            if (meters >= 1000.0) return (meters / 1000.0).ToString("0.0") + "km";
            return Math.Max(0.0, meters).ToString("0") + "m";
        }

        void UpdateRateLimitedSnapshots(AERISTerrainAwareness terrain,
            AERISRunwayDirectionDefinition direction, AERISRunwayObservation observation,
            bool landActive, Vessel vessel)
        {
            float now = Time.realtimeSinceStartup;
            AERISTerrainPerformanceController performance = terrain == null ? null : terrain.Performance;
            float navigationFps = performance == null ? 24f : performance.EffectiveNavigationFps;
            float symbologyFps = performance == null ? 45f : performance.EffectiveSymbologyFps;
            if (now >= nextNavigationSnapshotRealtime || direction != cachedLandingDirection ||
                (!landActive && cachedLandingObservation != null))
            {
                cachedLandingDirection = direction;
                cachedLandingObservation = landActive ? CloneObservation(observation) : null;
                nextNavigationSnapshotRealtime = now + 1f / Mathf.Max(5f, navigationFps);
            }
            if (now >= nextSymbologySnapshotRealtime)
            {
                cachedFallbackMapHeading = ResolveMapHeading(vessel);
                nextSymbologySnapshotRealtime = now + 1f / Mathf.Max(10f, symbologyFps);
            }
        }

        static AERISRunwayObservation CloneObservation(AERISRunwayObservation source)
        {
            if (source == null) return null;
            return new AERISRunwayObservation
            {
                Valid = source.Valid,
                Status = source.Status,
                DistanceToThresholdMeters = source.DistanceToThresholdMeters,
                BearingToThresholdDeg = source.BearingToThresholdDeg,
                ApproachDistanceMeters = source.ApproachDistanceMeters,
                AlongRunwayMeters = source.AlongRunwayMeters,
                CrossTrackMeters = source.CrossTrackMeters,
                InterceptAngleDeg = source.InterceptAngleDeg,
                GlidePathTargetAltitudeMeters = source.GlidePathTargetAltitudeMeters,
                GlidePathErrorMeters = source.GlidePathErrorMeters,
                ThresholdEastMeters = source.ThresholdEastMeters,
                ThresholdNorthMeters = source.ThresholdNorthMeters,
                OppositeEastMeters = source.OppositeEastMeters,
                OppositeNorthMeters = source.OppositeNorthMeters,
                VesselAltitudeAslMeters = source.VesselAltitudeAslMeters,
                VesselHeadingDeg = source.VesselHeadingDeg,
                OnApproachSide = source.OnApproachSide,
                RunwayGeometryDirectionValid = source.RunwayGeometryDirectionValid,
                LocalizerGeometryEligible = source.LocalizerGeometryEligible,
                GlidePathGeometryEligible = source.GlidePathGeometryEligible,
                InhibitReason = source.InhibitReason
            };
        }

        void DrawTerrainMap(Rect plot, AERISTerrainAwareness terrain, bool hazardOnly,
            Vessel vessel, double centerLatitudeDeg, double centerLongitudeDeg,
            float rangeMeters, float mapHeadingDeg, bool trackUp, float anchorV)
        {
            DrawTerrainStandbyBackground(plot);
            if (settings != null && settings.TerrainDisplayMode ==
                AERISTerrainDisplayMode.Off)
            {
                if (terrainTileRenderer != null) terrainTileRenderer.SuspendViewport();
                DrawLabel(plot, "TERR OFF", centerStyle,
                    new Color(0.48f, 0.58f, 0.62f, 1f));
                return;
            }

            AERISTerrainTileSystem tileSystem = terrain == null ? null :
                terrain.DisplayTiles;
            AERISTerrainGpuDrawState gpuState = AERISTerrainGpuDrawState.None;
            if (!hazardOnly && terrainTileRenderer != null && tileSystem != null &&
                tileSystem.BodySupported && vessel != null)
            {
                gpuState = terrainTileRenderer.Draw(plot, tileSystem, vessel,
                    centerLatitudeDeg, centerLongitudeDeg, rangeMeters, mapHeadingDeg,
                    trackUp, anchorV, ResolveMapLockReference());
            }
            if (gpuState == AERISTerrainGpuDrawState.Complete) return;
            if (gpuState == AERISTerrainGpuDrawState.Partial)
            {
                int percent = Mathf.Clamp(Mathf.RoundToInt(
                    (terrainTileRenderer == null ? 0f :
                    terrainTileRenderer.LastBackFoundationCoverage) * 100f), 0, 99);
                DrawLabel(plot, "TERRAIN GPU BUILDING " + percent + "%", centerStyle,
                    new Color(0.58f, 0.76f, 0.82f, 1f));
                return;
            }

            string unavailable;
            if (settings != null && settings.TerrainGpuMode == AERISTerrainGpuMode.Off)
                unavailable = "TERRAIN GPU OFF";
            else if (terrainTileRenderer != null && terrainTileRenderer.GpuFailed)
                unavailable = "TERRAIN GPU FAILED";
            else if (tileSystem == null || !tileSystem.BodySupported)
                unavailable = tileSystem == null ? "TERR N/A" : tileSystem.StatusText;
            else
                unavailable = "TERRAIN GPU UNAVAILABLE";
            DrawLabel(plot, unavailable, centerStyle,
                new Color(1f, 0.72f, 0.18f, 1f));
        }

        static void DrawTerrainStandbyBackground(Rect rect)
        {
            // Gate 5 Candidate 2: an explicit OFF->ON rebuild may legitimately have no
            // reusable GPU FRONT because Terrain OFF must release presentation resources.
            // Use the normal water-map background instead of flashing the near-black LAND
            // focus background while the first exact FRONT is rebuilt.
            FillRect(rect, new Color(0.025f, 0.145f, 0.285f, 1f));
        }

        static void DrawCleanBackground(Rect rect)
        {
            FillRect(rect, new Color(0.015f, 0.025f, 0.035f, 1f));
        }

        void DrawRangeRings(Rect plot, Vector2 aircraft, float scale)
        {
            Color ring = new Color(0.72f, 0.84f, 0.88f, 0.35f);
            DrawArc(aircraft, Mathf.Min(plot.width * 0.24f, plot.height * 0.24f),
                -160f, -20f, 10, ring, Mathf.Max(1f, 0.8f * scale));
            DrawArc(aircraft, Mathf.Min(plot.width * 0.48f, plot.height * 0.48f),
                -160f, -20f, 12, ring, Mathf.Max(1f, 0.8f * scale));
            DrawLine(new Vector2(plot.center.x, plot.y),
                new Vector2(plot.center.x, plot.yMax),
                new Color(0.78f, 0.86f, 0.90f, 0.20f), 1f);
        }

        void DrawAirfieldSymbols(Rect plot, AERISAirfieldRegistry registry, Vessel vessel,
            float range, float mapHeading, bool mapTrackUp, bool selectedOnly, float scale)
        {
            if (registry == null || vessel == null || vessel.mainBody == null) return;
            int drawn = 0;
            int maximumSymbols = core != null && core.Terrain != null &&
                core.Terrain.Performance != null ?
                core.Terrain.Performance.ActiveProfile.MaximumFacilitySymbols : 24;
            IList<AERISAirfieldDefinition> airfieldView = registry.Airfields;
            AERISAirfieldDefinition selectedAirfield = registry.SelectedAirfield;
            for (int i = 0; i < airfieldView.Count; i++)
            {
                AERISAirfieldDefinition airfield = airfieldView[i];
                if (airfield == null || !registry.IsAirfieldPresentationAvailable(airfield) ||
                    !string.Equals(airfield.Body, vessel.mainBody.name,
                    StringComparison.OrdinalIgnoreCase)) continue;
                bool selected = airfield == selectedAirfield;
                if (selectedOnly && !selected) continue;
                if (!selected && range > 20000f && airfield.FacilityKind != AERISFacilityKind.Runway)
                    continue;
                if (!selected && drawn >= maximumSymbols) continue;
                double latitude, longitude;
                if (!TryAirfieldPoint(airfield, out latitude, out longitude)) continue;
                double east, north;
                ToLocalMeters(vessel.mainBody, vessel.latitude, vessel.longitude,
                    latitude, longitude, out east, out north);
                Vector2 point;
                if (!TryMapPoint(east, north, range, mapHeading,
                    mapTrackUp, plot, out point)) continue;
                Color color = selected ? ArmedColor : new Color(0.72f, 0.92f, 1f, 0.92f);
                DrawFacilitySymbol(point, airfield.FacilityKind, color,
                    Mathf.Max(2f, 3.5f * scale));
                drawn++;
            }
        }

        static bool TryAirfieldPoint(AERISAirfieldDefinition airfield,
            out double latitude, out double longitude)
        {
            latitude = airfield.ReferenceLatitudeDeg;
            longitude = airfield.ReferenceLongitudeDeg;
            if (Math.Abs(latitude) > 0.000001 || Math.Abs(longitude) > 0.000001) return true;
            AERISRunwayDirectionDefinition direction = airfield.DirectionAt(0);
            if (direction == null || !direction.HasFiniteGeometry) return false;
            latitude = (direction.Threshold.LatitudeDeg +
                direction.OppositeThreshold.LatitudeDeg) * 0.5;
            longitude = (direction.Threshold.LongitudeDeg +
                direction.OppositeThreshold.LongitudeDeg) * 0.5;
            return true;
        }

        static void DrawFacilitySymbol(Vector2 point, AERISFacilityKind kind,
            Color color, float size)
        {
            if (kind == AERISFacilityKind.Runway)
            {
                DrawLine(new Vector2(point.x - size, point.y + size),
                    new Vector2(point.x + size, point.y - size), color, 2f);
                DrawLine(new Vector2(point.x - size * 0.55f, point.y + size * 1.25f),
                    new Vector2(point.x + size * 1.45f, point.y - size * 0.75f), color, 1f);
            }
            else if (kind == AERISFacilityKind.Helipad)
            {
                DrawLine(new Vector2(point.x - size, point.y),
                    new Vector2(point.x + size, point.y), color, 1.5f);
                DrawLine(new Vector2(point.x - size, point.y - size),
                    new Vector2(point.x - size, point.y + size), color, 1.5f);
                DrawLine(new Vector2(point.x + size, point.y - size),
                    new Vector2(point.x + size, point.y + size), color, 1.5f);
            }
            else if (kind == AERISFacilityKind.LaunchPad)
            {
                DrawRectOutline(new Rect(point.x - size, point.y - size,
                    size * 2f, size * 2f), color, 1f);
            }
            else if (kind == AERISFacilityKind.Harbour)
            {
                DrawLine(new Vector2(point.x, point.y - size),
                    new Vector2(point.x, point.y + size), color, 1.5f);
                DrawLine(new Vector2(point.x - size, point.y + size * 0.4f),
                    new Vector2(point.x + size, point.y + size * 0.4f), color, 1.5f);
            }
            else FillRect(new Rect(point.x - 1f, point.y - 1f, 2f, 2f), color);
        }

        void DrawLandingPlan(Rect plot, AERISRunwayDirectionDefinition direction,
            AERISRunwayObservation observation, Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float range,
            float mapHeading, bool mapTrackUp, float anchorV, float scale)
        {
            if (direction == null || observation == null ||
                !direction.HeadingMatchesGeometry ||
                !observation.RunwayGeometryDirectionValid)
            {
                DrawLabel(new Rect(plot.x + 4f, plot.y + 4f,
                    plot.width - 8f, Mathf.Max(14f, 18f * scale)),
                    "RUNWAY GEOMETRY INVALID", textStyle, WarningColor);
                return;
            }
            float heading = mapHeading;
            double ownEast = 0.0, ownNorth = 0.0;
            if (vessel != null && vessel.mainBody != null)
                ToLocalMeters(vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg,
                    vessel.latitude, vessel.longitude, out ownEast, out ownNorth);
            double thresholdEast = ownEast + observation.ThresholdEastMeters;
            double thresholdNorth = ownNorth + observation.ThresholdNorthMeters;
            double oppositeEast = ownEast + observation.OppositeEastMeters;
            double oppositeNorth = ownNorth + observation.OppositeNorthMeters;
            Vector2 threshold, opposite;
            // Observation coordinates are ownship-relative. Convert them to the exact
            // presented map-center authority before projection so a latched FRONT cannot
            // make the runway/localizer fan stretch as ownship moves across the old map.
            TryMapPoint(thresholdEast, thresholdNorth, range, heading, mapTrackUp,
                plot, anchorV, out threshold);
            TryMapPoint(oppositeEast, oppositeNorth, range, heading, mapTrackUp,
                plot, anchorV, out opposite);
            DrawClippedLine(plot, threshold, opposite, RunwayColor,
                Mathf.Max(2f, 4f * scale));
            if (!observation.OnApproachSide)
            {
                DrawLabel(new Rect(plot.x + 3f, plot.yMax - Mathf.Max(28f, 32f * scale),
                    plot.width - 6f, Mathf.Max(24f, 28f * scale)),
                    "LOC N/A\nNOT ON APPROACH SIDE", textStyle, ArmedColor);
                return;
            }

            double runwayEast = oppositeEast - thresholdEast;
            double runwayNorth = oppositeNorth - thresholdNorth;
            double runwayLength = Math.Sqrt(runwayEast * runwayEast + runwayNorth * runwayNorth);
            if (runwayLength > 1.0)
            {
                double unitEast = runwayEast / runwayLength;
                double unitNorth = runwayNorth / runwayLength;
                double captureDistance = Math.Min(direction.LocalizerCaptureDistanceMeters, range);
                double farEast = thresholdEast - unitEast * captureDistance;
                double farNorth = thresholdNorth - unitNorth * captureDistance;
                double halfFunnel = Math.Tan(direction.LocalizerCaptureAngleDeg * Math.PI / 180.0) *
                    captureDistance;
                double perpEast = unitNorth;
                double perpNorth = -unitEast;
                Vector2 farCenter, farLeft, farRight;
                TryMapPoint(farEast, farNorth, range, heading, mapTrackUp,
                    plot, anchorV, out farCenter);
                TryMapPoint(farEast + perpEast * halfFunnel,
                    farNorth + perpNorth * halfFunnel, range, heading, mapTrackUp,
                    plot, anchorV, out farLeft);
                TryMapPoint(farEast - perpEast * halfFunnel,
                    farNorth - perpNorth * halfFunnel, range, heading, mapTrackUp,
                    plot, anchorV, out farRight);
                Color color = observation.LocalizerGeometryEligible ? GuidanceColor :
                    new Color(0.28f, 0.68f, 0.92f, 1f);
                DrawClippedLine(plot, threshold, farCenter, color,
                    Mathf.Max(1f, 1.6f * scale));
                DrawClippedLine(plot, threshold, farLeft, color,
                    Mathf.Max(1f, 1.1f * scale));
                DrawClippedLine(plot, threshold, farRight, color,
                    Mathf.Max(1f, 1.1f * scale));
                DrawClippedLine(plot, farLeft, farRight, color,
                    Mathf.Max(1f, 1.1f * scale));
            }
            string xtk = Mathf.Abs((float)observation.CrossTrackMeters) < 1f ? "0" :
                observation.CrossTrackMeters.ToString("+0;-0");
            DrawLabel(new Rect(plot.x + 3f, plot.yMax - Mathf.Max(12f, 15f * scale),
                plot.width * 0.45f, Mathf.Max(12f, 15f * scale)),
                "XTK " + xtk, textStyle,
                observation.LocalizerGeometryEligible ? GuidanceColor : ArmedColor);
        }

        void DrawLandingProfile(Rect rect, AERISRunwayDirectionDefinition direction,
            AERISRunwayObservation observation, float scale)
        {
            DrawCleanBackground(rect);
            if (direction == null || observation == null ||
                !direction.HeadingMatchesGeometry ||
                !observation.RunwayGeometryDirectionValid)
            {
                DrawLabel(rect, "GS N/A\nRUNWAY GEOMETRY INVALID", textStyle,
                    WarningColor);
                return;
            }
            if (!observation.OnApproachSide ||
                double.IsNaN(observation.GlidePathTargetAltitudeMeters) ||
                double.IsInfinity(observation.GlidePathTargetAltitudeMeters) ||
                double.IsNaN(observation.GlidePathErrorMeters) ||
                double.IsInfinity(observation.GlidePathErrorMeters))
            {
                DrawLabel(rect, "GS N/A\nNOT ON APPROACH SIDE", textStyle,
                    ArmedColor);
                return;
            }
            float pad = Mathf.Max(3f, 5f * scale);
            Rect plot = new Rect(rect.x + pad, rect.y + pad,
                Mathf.Max(6f, rect.width - pad * 2f), Mathf.Max(8f, rect.height - pad * 2f));
            double distanceRange = Math.Max(1000.0, Math.Min(direction.GlidePathCaptureDistanceMeters,
                Math.Max(observation.ApproachDistanceMeters * 1.15, 5000.0)));
            double thresholdAlt = direction.Threshold.ElevationMeters;
            double nominalFarAboveThreshold = direction.ThresholdCrossingHeightMeters +
                Math.Tan(direction.GlidePathAngleDeg * Math.PI / 180.0) * distanceRange;
            // Keep the certified GS geometry legible even when the aircraft is far above
            // the capture envelope. The aircraft symbol may clamp to the profile edge, but
            // it no longer compresses the glide line into a nearly invisible strip.
            double altitudeRange = Math.Max(300.0, nominalFarAboveThreshold * 1.18);

            Vector2 threshold = ProfilePoint(0.0, direction.ThresholdCrossingHeightMeters,
                distanceRange, altitudeRange, plot);
            Vector2 farGlide = ProfilePoint(distanceRange,
                nominalFarAboveThreshold, distanceRange, altitudeRange, plot);
            double halfCaptureAngleDeg = Math.Max(0.35,
                Math.Min(1.5, direction.GlidePathAngleDeg * 0.25));
            double lowerAngleDeg = Math.Max(0.1,
                direction.GlidePathAngleDeg - halfCaptureAngleDeg);
            double upperAngleDeg = direction.GlidePathAngleDeg + halfCaptureAngleDeg;
            Vector2 farLower = ProfilePoint(distanceRange,
                direction.ThresholdCrossingHeightMeters + Math.Tan(lowerAngleDeg *
                Math.PI / 180.0) * distanceRange, distanceRange, altitudeRange, plot);
            Vector2 farUpper = ProfilePoint(distanceRange,
                direction.ThresholdCrossingHeightMeters + Math.Tan(upperAngleDeg *
                Math.PI / 180.0) * distanceRange, distanceRange, altitudeRange, plot);
            Vector2 aircraft = ProfilePoint(observation.ApproachDistanceMeters,
                observation.VesselAltitudeAslMeters - thresholdAlt, distanceRange,
                altitudeRange, plot);
            Color glide = observation.GlidePathGeometryEligible ? GuidanceColor :
                new Color(0.80f, 0.44f, 0.94f, 1f);
            Color funnel = new Color(glide.r, glide.g, glide.b, 0.58f);
            DrawLine(threshold, farLower, funnel, Mathf.Max(1f, 1.0f * scale));
            DrawLine(threshold, farUpper, funnel, Mathf.Max(1f, 1.0f * scale));
            DrawLine(farLower, farUpper, funnel, Mathf.Max(1f, 1.0f * scale));
            DrawLine(threshold, farGlide, glide, Mathf.Max(1f, 1.8f * scale));
            DrawLine(new Vector2(plot.x, plot.yMax - 1f),
                new Vector2(plot.xMax, plot.yMax - 1f),
                new Color(0.62f, 0.68f, 0.72f, 1f), 1f);
            DrawCross(aircraft, observation.GlidePathGeometryEligible ? GuidanceColor : ArmedColor,
                Mathf.Max(2f, 3f * scale));
            DrawLabel(new Rect(plot.x, plot.y, plot.width, Mathf.Max(12f, 15f * scale)),
                "GS " + observation.GlidePathErrorMeters.ToString("+0;-0;0"), textStyle,
                observation.GlidePathGeometryEligible ? GuidanceColor : ArmedColor);
        }

        string FormatTerrainMode()
        {
            if (settings == null) return "TERR AUTO";
            switch (settings.TerrainDisplayMode)
            {
                case AERISTerrainDisplayMode.Topographic: return "TOPO";
                case AERISTerrainDisplayMode.Relative: return "REL";
                case AERISTerrainDisplayMode.Off: return "TERR OFF";
                default: return "TERR AUTO";
            }
        }

        string FormatTerrainRenderTargetOrientation()
        {
            return settings != null && settings.TerrainRenderTargetOrientation ==
                AERISTerrainRenderTargetOrientation.Flipped ? "TERR Y FLIP" :
                "TERR Y DIRECT";
        }

        void CycleTerrainRenderTargetOrientation()
        {
            if (settings == null) return;
            settings.TerrainRenderTargetOrientation =
                settings.TerrainRenderTargetOrientation ==
                    AERISTerrainRenderTargetOrientation.Direct ?
                    AERISTerrainRenderTargetOrientation.Flipped :
                    AERISTerrainRenderTargetOrientation.Direct;
            if (terrainTileRenderer != null) terrainTileRenderer.ResetGpuFailure();
            AERISLogger.Info("[ND/TERRAIN_ALIGN] presentation orientation changed to " +
                settings.TerrainRenderTargetOrientation + ".");
        }

        void CycleTerrainMode()
        {
            if (settings == null) return;
            switch (settings.TerrainDisplayMode)
            {
                case AERISTerrainDisplayMode.Automatic:
                    settings.TerrainDisplayMode = AERISTerrainDisplayMode.Topographic; break;
                case AERISTerrainDisplayMode.Topographic:
                    settings.TerrainDisplayMode = AERISTerrainDisplayMode.Relative; break;
                case AERISTerrainDisplayMode.Relative:
                    settings.TerrainDisplayMode = AERISTerrainDisplayMode.Off; break;
                default:
                    settings.TerrainDisplayMode = AERISTerrainDisplayMode.Automatic; break;
            }
            AERISLogger.Info("[ND/TERRAIN] display mode=" +
                settings.TerrainDisplayMode);
        }

        void DrawMapControls(Rect rect, bool landActive, float range,
            bool overlay, bool isPlan, float scale)
        {
            float height = Mathf.Max(17f, rect.height - 2f);
            float button = Mathf.Max(23f, 31f * scale);
            float rangeButton = Mathf.Max(35f, 48f * scale);
            float wideButton = Mathf.Max(44f, 58f * scale);
            float resizeReserve = Mathf.Max(22f, 24f * scale);
            float right = rect.xMax - resizeReserve;
            Rect plus = new Rect(right - button, rect.y, button, height);
            Rect minus = new Rect(plus.x - button - 2f, rect.y, button, height);
            Rect rangeRect = new Rect(minus.x - rangeButton - 2f, rect.y, rangeButton, height);
            Rect recenter = new Rect(rangeRect.x - wideButton - 2f, rect.y, wideButton, height);
            Rect orient = new Rect(recenter.x - wideButton - 2f, rect.y, wideButton, height);
            float menuWidth = Mathf.Max(38f, 50f * scale);
            Rect view = new Rect(orient.x - menuWidth - 2f, rect.y, menuWidth, height);
            Rect terrainMode = new Rect(view.x - wideButton - 2f, rect.y, wideButton, height);

            bool oldEnabled = GUI.enabled;
            Color oldBackground = GUI.backgroundColor;
            try
            {
                GUI.enabled = true;
                GUI.backgroundColor = new Color(0.25f, 0.42f, 0.36f, 1f);
                if (GUI.Button(terrainMode, new GUIContent(FormatTerrainMode(),
                    "AUTO / TOPO / REL / OFF"), buttonStyle))
                {
                    CycleTerrainMode();
                    SaveSettingsAndProfile();
                }
                GUI.enabled = true;
                GUI.backgroundColor = auxiliaryMenuOpen ?
                    new Color(0.25f, 0.68f, 0.88f, 1f) : new Color(0.34f, 0.36f, 0.40f, 1f);
                if (GUI.Button(view, new GUIContent("MENU", "ND layers and aids"), buttonStyle))
                    auxiliaryMenuOpen = !auxiliaryMenuOpen;
                GUI.enabled = oldEnabled;
                GUI.backgroundColor = new Color(0.30f, 0.38f, 0.44f, 1f);
                string orientText = isPlan ? "N/PLAN" :
                    (settings.NavigationDisplayTrackUp ? "TRK UP" : "N UP");
                GUI.enabled = !isPlan;
                if (GUI.Button(orient, new GUIContent(orientText,
                    "TRACK UP / NORTH UP"), buttonStyle))
                {
                    settings.NavigationDisplayTrackUp = !settings.NavigationDisplayTrackUp;
                    SaveSettingsAndProfile();
                }
                GUI.enabled = isPlan;
                if (GUI.Button(recenter, new GUIContent("RECENTER",
                    "Return to ownship and restore orientation"), buttonStyle))
                    RecenterPlan(FlightGlobals.ActiveVessel);
                GUI.enabled = oldEnabled;
                float interactionRange = PendingOrCurrentRange(range);
                if (GUI.Button(rangeRect, new GUIContent(FormatRange(interactionRange),
                    "Map range (changes are coalesced for terrain generation)"), buttonStyle))
                    CycleRange(interactionRange);
                if (GUI.Button(minus, new GUIContent("−", "Zoom out"), buttonStyle))
                    ChangeRange(interactionRange, 1);
                if (GUI.Button(plus, new GUIContent("+", "Zoom in"), buttonStyle))
                    ChangeRange(interactionRange, -1);
            }
            finally
            {
                GUI.enabled = oldEnabled;
                GUI.backgroundColor = oldBackground;
            }
            if (auxiliaryMenuOpen)
                DrawAuxiliaryMenu(rect, landActive, overlay, scale);

            string compact = isPlan ? "DRAG MAP  PLAN" : "CLICK RWY  PREVIEW";
            if (landActive)
            {
                AERISLandingFoundation land = core.Landing;
                compact = "PILOT  ARM";
                if (land != null && land.Observation != null)
                {
                    compact += land.Observation.LocalizerGeometryEligible ? "  LOC" : string.Empty;
                    compact += land.Observation.GlidePathGeometryEligible ? "  GS" : string.Empty;
                }
            }
            DrawLabel(new Rect(rect.x + 2f, rect.y,
                Mathf.Max(10f, view.x - rect.x - 4f), height), compact,
                titleStyle, landActive ? ArmedColor : new Color(0.68f, 0.82f, 0.88f, 1f));
        }

        static Rect ResolveAuxiliaryMenuRect(Rect controlsRect, bool landActive,
            float scale)
        {
            float rowHeight = Mathf.Max(18f, 22f * scale);
            float width = Mathf.Max(112f, 142f * scale);
            bool windAvailable = !string.IsNullOrEmpty(AERISWindProviderApi.ProviderName);
            int rows = 4 + (windAvailable ? 1 : 0) + (landActive ? 2 : 0);
            return new Rect(controlsRect.x + 3f,
                controlsRect.y - rows * rowHeight - 5f, width,
                rows * rowHeight + 3f);
        }

        void DrawAuxiliaryMenu(Rect controlsRect, bool landActive,
            bool overlay, float scale)
        {
            float rowHeight = Mathf.Max(18f, 22f * scale);
            Rect panel = ResolveAuxiliaryMenuRect(controlsRect, landActive, scale);
            FillRect(panel, new Color(0.01f, 0.02f, 0.03f, 0.96f));
            DrawRectOutline(panel, new Color(0.46f, 0.72f, 0.82f, 0.94f), 1f);
            bool changed = false;
            float y = panel.y + 2f;
            bool oldEnabled = GUI.enabled;
            Color oldBackground = GUI.backgroundColor;
            try
            {
                GUI.enabled = true;
                GUI.backgroundColor = settings.NavigationDisplayTrailEnabled ?
                    new Color(0.22f, 0.58f, 0.66f, 1f) : new Color(0.28f, 0.31f, 0.34f, 1f);
                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    "TRAIL " + (settings.NavigationDisplayTrailEnabled ? "ON" : "OFF"),
                    buttonStyle))
                { settings.NavigationDisplayTrailEnabled = !settings.NavigationDisplayTrailEnabled; changed = true; }
                y += rowHeight;
                GUI.backgroundColor = settings.NavigationDisplayTrackVectorEnabled ?
                    new Color(0.22f, 0.58f, 0.66f, 1f) : new Color(0.28f, 0.31f, 0.34f, 1f);
                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    "VECTOR " + (settings.NavigationDisplayTrackVectorEnabled ? "ON" : "OFF"),
                    buttonStyle))
                { settings.NavigationDisplayTrackVectorEnabled = !settings.NavigationDisplayTrackVectorEnabled; changed = true; }
                y += rowHeight;
                GUI.backgroundColor = settings.NavigationDisplayTrafficEnabled ?
                    new Color(0.22f, 0.58f, 0.66f, 1f) : new Color(0.28f, 0.31f, 0.34f, 1f);
                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    "TRAFFIC " + (settings.NavigationDisplayTrafficEnabled ? "ON" : "OFF"),
                    buttonStyle))
                { settings.NavigationDisplayTrafficEnabled = !settings.NavigationDisplayTrafficEnabled; changed = true; }
                y += rowHeight;
                GUI.enabled = true;
                GUI.backgroundColor = settings.TerrainRenderTargetOrientation ==
                    AERISTerrainRenderTargetOrientation.Direct ?
                    new Color(0.22f, 0.58f, 0.66f, 1f) :
                    new Color(0.52f, 0.34f, 0.22f, 1f);
                if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                    FormatTerrainRenderTargetOrientation(), buttonStyle))
                { CycleTerrainRenderTargetOrientation(); changed = true; }
                y += rowHeight;
                bool windAvailable = !string.IsNullOrEmpty(AERISWindProviderApi.ProviderName);
                if (windAvailable)
                {
                    GUI.enabled = true;
                    GUI.backgroundColor = settings.NavigationDisplayWindEnabled ?
                        new Color(0.22f, 0.58f, 0.66f, 1f) : new Color(0.28f, 0.31f, 0.34f, 1f);
                    if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                        "WIND " + (settings.NavigationDisplayWindEnabled ? "ON" : "OFF"),
                        buttonStyle))
                    { settings.NavigationDisplayWindEnabled = !settings.NavigationDisplayWindEnabled; changed = true; }
                    y += rowHeight;
                }
                if (landActive)
                {
                    GUI.enabled = true;
                    GUI.backgroundColor = overlay ? new Color(0.25f, 0.68f, 0.88f, 1f) :
                        new Color(0.28f, 0.31f, 0.34f, 1f);
                    if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                        "LAND TERR " + (overlay ? "ON" : "OFF"), buttonStyle))
                    { settings.NavigationDisplayLandOverlay = !settings.NavigationDisplayLandOverlay; changed = true; }
                    y += rowHeight;
                    GUI.backgroundColor = new Color(0.30f, 0.42f, 0.48f, 1f);
                    if (GUI.Button(new Rect(panel.x + 2f, y, panel.width - 4f, rowHeight - 1f),
                        "PROFILE " + FormatLandingProfileSize(), buttonStyle))
                    { CycleLandingProfileSize(); changed = true; }
                }
            }
            finally
            {
                GUI.enabled = oldEnabled;
                GUI.backgroundColor = oldBackground;
            }
            if (changed) SaveSettingsAndProfile();
        }

        float ResolveLandingProfileFraction()
        {
            if (settings == null) return 0.28f;
            switch (settings.NavigationDisplayLandProfileSize)
            {
                case AERISNavigationDisplayLandProfileSize.Compact: return 0.20f;
                case AERISNavigationDisplayLandProfileSize.Large: return 0.40f;
                default: return 0.28f;
            }
        }

        string FormatLandingProfileSize()
        {
            if (settings == null) return "NORMAL";
            switch (settings.NavigationDisplayLandProfileSize)
            {
                case AERISNavigationDisplayLandProfileSize.Compact: return "COMPACT";
                case AERISNavigationDisplayLandProfileSize.Large: return "LARGE";
                default: return "NORMAL";
            }
        }

        void CycleLandingProfileSize()
        {
            if (settings == null) return;
            switch (settings.NavigationDisplayLandProfileSize)
            {
                case AERISNavigationDisplayLandProfileSize.Compact:
                    settings.NavigationDisplayLandProfileSize =
                        AERISNavigationDisplayLandProfileSize.Normal; break;
                case AERISNavigationDisplayLandProfileSize.Normal:
                    settings.NavigationDisplayLandProfileSize =
                        AERISNavigationDisplayLandProfileSize.Large; break;
                default:
                    settings.NavigationDisplayLandProfileSize =
                        AERISNavigationDisplayLandProfileSize.Compact; break;
            }
        }

        void SyncVesselProfile(Vessel vessel)
        {
            uint runtimeId = vessel == null ? 0u : vessel.persistentId;
            int partCount = vessel == null || vessel.parts == null ? 0 : vessel.parts.Count;
            if (ReferenceEquals(vessel, activeProfileVessel) &&
                runtimeId == activeProfileRuntimeVesselId &&
                partCount == activeProfilePartCount) return;
            string signature = AERISNavigationDisplayProfileStore.CreateSignature(vessel);
            if (!string.Equals(signature, activeProfileSignature,
                StringComparison.Ordinal))
            {
                SaveActiveProfile();
                activeProfileSignature = profileStore.Apply(vessel, settings);
            }
            activeProfileVessel = vessel;
            activeProfileRuntimeVesselId = runtimeId;
            activeProfilePartCount = partCount;
            activeProfileLabel = vessel == null ? string.Empty : vessel.vesselName;
            planMode = false;
            mapPointerDown = false;
            mapDragging = false;
            trailSamples.Clear();
            trailVesselPersistentId = 0u;
            trailBodyName = string.Empty;
            nextTrailSampleRealtime = 0f;
            AERISPreparedTrafficFrameApi.Clear();
            nextTrafficCaptureRealtime = 0f;
        }

        void SaveActiveProfile()
        {
            if (profileStore == null || string.IsNullOrEmpty(activeProfileSignature) ||
                settings == null) return;
            profileStore.Save(activeProfileSignature, activeProfileLabel, settings);
        }

        void SaveSettingsAndProfile()
        {
            if (settings != null) settings.Save();
            SaveActiveProfile();
        }

        void HandleMouseWheel(Rect viewport, float range)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.ScrollWheel ||
                !viewport.Contains(e.mousePosition)) return;
            ChangeRange(PendingOrCurrentRange(range), e.delta.y > 0f ? 1 : -1);
            e.Use();
        }

        void CycleRange(float current)
        {
            float[] steps = AERISSettings.NavigationDisplayRangeStepsMeters;
            int index = NearestRangeIndex(steps, current);
            index = (index + 1) % steps.Length;
            ApplyManualRange(steps[index]);
        }

        void ChangeRange(float current, int delta)
        {
            float[] steps = AERISSettings.NavigationDisplayRangeStepsMeters;
            int index = NearestRangeIndex(steps, current);
            index = Mathf.Clamp(index + delta, 0, steps.Length - 1);
            ApplyManualRange(steps[index]);
        }

        static int NearestRangeIndex(float[] steps, float current)
        {
            int index = 0;
            float best = float.MaxValue;
            for (int i = 0; i < steps.Length; i++)
            {
                float distance = Mathf.Abs(steps[i] - current);
                if (distance < best) { best = distance; index = i; }
            }
            return index;
        }

        void ApplyManualRange(float rangeMeters)
        {
            pendingManualRangeMeters = AERISSettings.NormalizeNavigationRange(rangeMeters);
            pendingManualRangeApplyRealtime = Time.realtimeSinceStartup +
                RangeChangeDebounceSeconds;
        }

        float PendingOrCurrentRange(float current)
        {
            return Finite(pendingManualRangeMeters) ? pendingManualRangeMeters : current;
        }

        void FlushPendingManualRange()
        {
            if (!Finite(pendingManualRangeMeters) ||
                Time.realtimeSinceStartup < pendingManualRangeApplyRealtime) return;
            float next = AERISSettings.NormalizeNavigationRange(pendingManualRangeMeters);
            pendingManualRangeMeters = float.NaN;
            pendingManualRangeApplyRealtime = 0f;
            float previous = settings.NavigationDisplayManualRangeMeters;
            bool wasAutomatic = settings.NavigationDisplayAutoRange;
            settings.NavigationDisplayAutoRange = false;
            settings.NavigationDisplayManualRangeMeters = next;
            SaveSettingsAndProfile();
            if (terrainTileRenderer != null)
                terrainTileRenderer.InvalidatePendingForViewChange();
            if (wasAutomatic || Mathf.Abs(previous - next) > 0.5f)
                AERISLogger.Info("[ND/TERRAIN] range=" + next.ToString("0") +
                    "m (coalesced)");
        }

        static string FormatRange(float meters)
        {
            if (meters >= 1000f) return (meters / 1000f).ToString(meters >= 10000f ? "0" : "0.#") + "k";
            return meters.ToString("0");
        }

        static float ResolveMapHeading(Vessel vessel)
        {
            return AERISTerrainAwareness.ResolveMapHeading(vessel);
        }

        AERISNdMapLockReference ResolveMapLockReference()
        {
            if (core == null || core.Airfields == null) return null;
            AERISRunwayDirectionDefinition direction = core.Airfields.SelectedDirection;
            AERISRunwayDefinition runway = core.Airfields.SelectedRunway;
            if (direction == null || runway == null || !direction.HasFiniteGeometry)
                return null;
            return new AERISNdMapLockReference
            {
                StableId = runway.StableId ?? direction.StableId ?? string.Empty,
                LatitudeADeg = direction.Threshold.LatitudeDeg,
                LongitudeADeg = direction.Threshold.LongitudeDeg,
                LatitudeBDeg = direction.OppositeThreshold.LatitudeDeg,
                LongitudeBDeg = direction.OppositeThreshold.LongitudeDeg
            };
        }

        static bool TryProjectGeographicPoint(AERISNdMapProjection projection,
            double targetLatitudeDeg, double targetLongitudeDeg, Rect plot,
            out Vector2 point)
        {
            float u, v;
            projection.ProjectLatitudeLongitudeToGui(targetLatitudeDeg,
                targetLongitudeDeg, out u, out v);
            point = new Vector2(plot.x + u * plot.width, plot.y + v * plot.height);
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        static bool TryMapPoint(double eastMeters, double northMeters, double rangeMeters,
            double headingDeg, bool trackUp, Rect plot, out Vector2 point)
        {
            return TryMapPoint(eastMeters, northMeters, rangeMeters, headingDeg,
                trackUp, plot, trackUp ? 0.75f : 0.5f, out point);
        }

        static bool TryMapPoint(double eastMeters, double northMeters, double rangeMeters,
            double headingDeg, bool trackUp, Rect plot, float anchorV,
            out Vector2 point)
        {
            double right;
            double forward;
            if (trackUp)
            {
                double h = headingDeg * Math.PI / 180.0;
                right = eastMeters * Math.Cos(h) - northMeters * Math.Sin(h);
                forward = eastMeters * Math.Sin(h) + northMeters * Math.Cos(h);
            }
            else { right = eastMeters; forward = northMeters; }
            double u = 0.5 + right / Math.Max(1.0, rangeMeters * 1.30);
            double v = anchorV - forward / Math.Max(1.0, rangeMeters);
            point = new Vector2(plot.x + (float)u * plot.width,
                plot.y + (float)v * plot.height);
            return u >= 0.0 && u <= 1.0 && v >= 0.0 && v <= 1.0;
        }

        static void ToLocalMeters(CelestialBody body, double originLatDeg,
            double originLonDeg, double targetLatDeg, double targetLonDeg,
            out double eastMeters, out double northMeters)
        {
            eastMeters = northMeters = 0.0;
            if (body == null) return;
            double lat1 = originLatDeg * Math.PI / 180.0;
            double lat2 = targetLatDeg * Math.PI / 180.0;
            double dLon = NormalizeLongitude(targetLonDeg - originLonDeg) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double bearing = Math.Atan2(y, x);
            double dLat = lat2 - lat1;
            double a = Math.Sin(dLat * 0.5) * Math.Sin(dLat * 0.5) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);
            double angle = 2.0 * Math.Atan2(Math.Sqrt(Math.Max(0.0, a)),
                Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            double distance = body.Radius * angle;
            eastMeters = Math.Sin(bearing) * distance;
            northMeters = Math.Cos(bearing) * distance;
        }

        static double NormalizeHeading(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        static Vector2 ProfilePoint(double distanceMeters, double altitudeAboveThreshold,
            double distanceRange, double altitudeRange, Rect plot)
        {
            float x = plot.xMax - (float)(Math.Max(0.0, distanceMeters) /
                Math.Max(1.0, distanceRange) * plot.width);
            float y = plot.yMax - (float)(Math.Max(0.0, altitudeAboveThreshold) /
                Math.Max(1.0, altitudeRange) * plot.height);
            return new Vector2(Mathf.Clamp(x, plot.x, plot.xMax),
                Mathf.Clamp(y, plot.y, plot.yMax));
        }

        static void DrawAircraftSymbol(Vector2 point, float scale)
        {
            // Constant-size, unfilled aircraft silhouette.  A two-pixel dark edge and
            // faint white halo preserve contrast over water, terrain, runway grey and
            // guidance colours without changing the symbol colour every frame.
            const float size = 5.5f;
            Vector2 nose = new Vector2(point.x, point.y - size * 1.3f);
            Vector2 tail = new Vector2(point.x, point.y + size);
            Vector2 wingLeft = new Vector2(point.x - size, point.y + size * 0.15f);
            Vector2 wingRight = new Vector2(point.x + size, point.y + size * 0.15f);
            Vector2 tailLeft = new Vector2(point.x - size * 0.50f, point.y + size * 0.80f);
            Vector2 tailRight = new Vector2(point.x + size * 0.50f, point.y + size * 0.80f);
            Color halo = new Color(1f, 1f, 1f, 0.14f);
            Color edge = new Color(0.005f, 0.012f, 0.018f, 0.78f);
            Color body = new Color(0.88f, 1f, 1f, 1f);
            DrawAircraftLines(nose, tail, wingLeft, wingRight, tailLeft, tailRight,
                halo, 2.0f);
            Vector2 shadow = new Vector2(0.55f, 0.65f);
            DrawAircraftLines(nose + shadow, tail + shadow, wingLeft + shadow,
                wingRight + shadow, tailLeft + shadow, tailRight + shadow, edge, 1.8f);
            DrawAircraftLines(nose, tail, wingLeft, wingRight, tailLeft, tailRight,
                body, 1.0f);
        }

        static void DrawAircraftLines(Vector2 nose, Vector2 tail, Vector2 wingLeft,
            Vector2 wingRight, Vector2 tailLeft, Vector2 tailRight, Color color,
            float width)
        {
            DrawLine(nose, tail, color, width);
            DrawLine(wingLeft, wingRight, color, width);
            DrawLine(tailLeft, tailRight, color, width);
        }

        static void DrawCross(Vector2 point, Color color, float size)
        {
            DrawLine(new Vector2(point.x - size, point.y - size),
                new Vector2(point.x + size, point.y + size), color, 1.5f);
            DrawLine(new Vector2(point.x - size, point.y + size),
                new Vector2(point.x + size, point.y - size), color, 1.5f);
        }

        static void DrawArc(Vector2 center, float radius, float startDeg, float endDeg,
            int segments, Color color, float width)
        {
            if (radius <= 0f || segments < 2) return;
            Vector2 previous = center + new Vector2(Mathf.Cos(startDeg * Mathf.Deg2Rad),
                Mathf.Sin(startDeg * Mathf.Deg2Rad)) * radius;
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float angle = Mathf.Lerp(startDeg, endDeg, t) * Mathf.Deg2Rad;
                Vector2 next = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                DrawLine(previous, next, color, width);
                previous = next;
            }
        }

        static void DrawRectOutline(Rect rect, Color color, float width)
        {
            DrawLine(new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), color, width);
            DrawLine(new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), color, width);
            DrawLine(new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax), color, width);
            DrawLine(new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMin, rect.yMin), color, width);
        }

        static void DrawClippedLine(Rect clip, Vector2 start, Vector2 end,
            Color color, float width)
        {
            if (!ClipLineToRect(clip, ref start, ref end)) return;
            DrawLine(start, end, color, width);
        }

        // Liang-Barsky line clipping keeps LAND/ILS geometry visible while either end
        // is outside the ND viewport. This avoids the old all-or-nothing early return.
        static bool ClipLineToRect(Rect rect, ref Vector2 start, ref Vector2 end)
        {
            if (!Finite(start.x) || !Finite(start.y) || !Finite(end.x) ||
                !Finite(end.y) || rect.width <= 0f || rect.height <= 0f) return false;
            float dx = end.x - start.x;
            float dy = end.y - start.y;
            float t0 = 0f;
            float t1 = 1f;
            if (!ClipTest(-dx, start.x - rect.xMin, ref t0, ref t1) ||
                !ClipTest(dx, rect.xMax - start.x, ref t0, ref t1) ||
                !ClipTest(-dy, start.y - rect.yMin, ref t0, ref t1) ||
                !ClipTest(dy, rect.yMax - start.y, ref t0, ref t1)) return false;
            Vector2 original = start;
            if (t1 < 1f) end = original + new Vector2(dx, dy) * t1;
            if (t0 > 0f) start = original + new Vector2(dx, dy) * t0;
            return (end - start).sqrMagnitude >= 0.04f;
        }

        static bool ClipTest(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 0.000001f) return q >= 0f;
            float r = q / p;
            if (p < 0f)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }

        static void DrawLine(Vector2 start, Vector2 end, Color color, float width)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (!Finite(length) || length < 0.2f) return;
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            try
            {
                GUI.color = color;
                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                GUIUtility.RotateAroundPivot(angle, start);
                GUI.DrawTexture(new Rect(start.x, start.y - width * 0.5f, length, width),
                    Texture2D.whiteTexture);
            }
            finally
            {
                GUI.matrix = previousMatrix;
                GUI.color = previousColor;
            }
        }

        static void FillRect(Rect rect, Color color)
        {
            if (!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) ||
                rect.width <= 0f || rect.height <= 0f) return;
            Color previous = GUI.color;
            try { GUI.color = color; GUI.DrawTexture(rect, Texture2D.whiteTexture); }
            finally { GUI.color = previous; }
        }

        static void DrawLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            if (!Finite(rect.x) || !Finite(rect.y) || !Finite(rect.width) || !Finite(rect.height) ||
                rect.width <= 0f || rect.height <= 0f) return;
            Color previous = GUI.color;
            try { GUI.color = color; GUI.Label(rect, text ?? string.Empty, style); }
            finally { GUI.color = previous; }
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
