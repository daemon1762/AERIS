using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Landing;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // Celestial-fixed terrain tile producer/cache. PQS access remains incremental on the
    // KSP main thread. Compression, disk I/O and immutable tile post-processing use the
    // existing shared Performance Runtime scheduler. CP3 Gate 3.1 makes the actual
    // rotated ND viewport authoritative and admits a complete Global/Far foundation
    // before any exact refinement or predictive work. Route/Local are transitional
    // exact bridges only; no ND-owned thread or unbounded queue is created here.
    internal sealed class AERISTerrainTileSystem : IDisposable
    {
        static readonly object environmentSync = new object();
        static string cachedGameDataHash = string.Empty;
        static bool gameDataHashReady;
        static bool gameDataHashRequested;
        static readonly Dictionary<string, string> cachedBodyEnvironmentHashes =
            new Dictionary<string, string>(StringComparer.Ordinal);
        readonly AERISSettings settings;
        readonly AERISTerrainPerformanceController performance;
        readonly AERISTerrainRamTileCache ram;
        readonly AERISCurrentBodyResidentCache currentBodyResidentCache;
        // Legacy per-file cache is retained only as a migration source. New reads and all
        // writes use the indexed Preload Terrain Database.
        readonly AERISTerrainDiskTileCache disk;
        readonly AERISTerrainPreloadDatabase preloadDatabase;
        readonly AERISTerrainWarmTileCache warm;
        readonly AERISTerrainBlockPipeline blockPipeline;
        readonly AERISTerrainPreloadBuilder preloadBuilder;
        readonly AERISTerrainPreloadTelemetry preloadTelemetry =
            new AERISTerrainPreloadTelemetry();
        // Candidate 9 UI telemetry contract: PreloadStatus is display telemetry, not a
        // simulation control input. SnapshotStatus() walks the complete preload index,
        // so rebuilding it for every IMGUI Layout/Repaint event can halve frame rate
        // while the AERIS window is open. Cache it at 4 Hz and invalidate immediately
        // after user operations. Terrain generation / preload scheduling remain live.
        const float PreloadStatusUiRefreshSeconds = 0.25f;
        AERISTerrainPreloadStatusSnapshot cachedPreloadStatus;
        float nextPreloadStatusUiRefreshRealtime;
        readonly object sync = new object();
        readonly Dictionary<string, AERISTerrainTileRequest> queued =
            new Dictionary<string, AERISTerrainTileRequest>(StringComparer.Ordinal);
        readonly HashSet<string> diskLoading = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> preloadChunksLoading = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> residentChunksLoading =
            new HashSet<string>(StringComparer.Ordinal);
        readonly List<AERISTerrainTileKey> residentPopulationPlan =
            new List<AERISTerrainTileKey>();
        readonly AERISPredictiveForwardCorridor predictiveCorridor =
            new AERISPredictiveForwardCorridor();
        readonly Dictionary<string, AERISResidentPinLease> residentPlanPins =
            new Dictionary<string, AERISResidentPinLease>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISResidentPinReason> residentPlanPinReasons =
            new Dictionary<string, AERISResidentPinReason>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainTileKey> desiredResidentPinKeys =
            new Dictionary<string, AERISTerrainTileKey>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISResidentPinReason> desiredResidentPinReasons =
            new Dictionary<string, AERISResidentPinReason>(StringComparer.Ordinal);
        readonly Dictionary<string, List<string>> preloadChunkTileIds =
            new Dictionary<string, List<string>>(StringComparer.Ordinal);
        readonly HashSet<string> diskWriting = new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, float> diskLoadingSince =
            new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainTileRequest> diskLoadingRequests =
            new Dictionary<string, AERISTerrainTileRequest>(StringComparer.Ordinal);
        readonly HashSet<string> desiredRequestIds =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> desiredVisibleIds =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> desiredFoundationIds =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> visibleFoundationIds =
            new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> cancellationScratch = new List<string>(128);
        readonly Dictionary<string, float> diskWritingSince =
            new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainHeightTile> diskWritePending =
            new Dictionary<string, AERISTerrainHeightTile>(StringComparer.Ordinal);
        readonly Dictionary<string, float> diskWriteRetryAfter =
            new Dictionary<string, float>(StringComparer.Ordinal);
        readonly Dictionary<string, int> diskWriteAttempts =
            new Dictionary<string, int>(StringComparer.Ordinal);
        readonly List<AERISTerrainTileRequest> requestScratch =
            new List<AERISTerrainTileRequest>(384);
        readonly List<AERISTerrainTileRequest> acceptedRequestScratch =
            new List<AERISTerrainTileRequest>(256);
        readonly List<AERISTerrainTileKey> visibleKeys = new List<AERISTerrainTileKey>(128);
        readonly List<AERISTerrainHeightTile> visibleTiles = new List<AERISTerrainHeightTile>(128);
        readonly List<string> diskWriteReadyScratch = new List<string>(64);
        readonly AERISTerrainTileCacheTelemetry telemetry = new AERISTerrainTileCacheTelemetry();

        CelestialBody activeBody;
        string activeBodyName = string.Empty;
        string environmentHash = string.Empty;
        long bodyGeneration;
        long terrainGeneration;
        long terrainRequestGeneration;
        long viewGeneration;
        long rangeGeneration;
        long planGeneration;
        long requestSequence;
        int lastPerformanceProfileRevision = int.MinValue;
        float nextPlanRealtime;
        float lastSampleRealtime;
        float nextFaultLogRealtime;
        int lastSamplingBatchSamples;
        double lastSamplingBatchMilliseconds;
        bool displayViewValid;
        double displayViewLatitudeDeg;
        double displayViewLongitudeDeg;
        double displayViewRangeMeters;
        double displayViewHeadingDeg;
        bool displayViewTrackUp;
        float displayViewAnchorGuiV = 0.5f;
        AERISTerrainRenderTargetOrientation displayViewOrientation =
            AERISTerrainRenderTargetOrientation.Direct;
        int lastFoundationRequestedCount;
        int lastFoundationMissingCount;
        int lastFoundationGlobalCount;
        int lastFoundationFarCount;
        int diskLoadsInFlight;
        int residentLoadsInFlight;
        int residentPopulationCursor;
        int residentPopulationBlockedFromLod = 5;
        long residentPopulationScopeGeneration = -1L;
        long residentPopulationIndexGeneration = -1L;
        float nextPreloadPointRefreshRealtime;
        float nextCp3TelemetrySampleRealtime;
        float nextCp3TelemetryLogRealtime;
        AERISCurrentBodyResidentTelemetrySnapshot cp3TelemetryResidentSnapshot;
        AERISPredictiveForwardCorridorSnapshot cp3TelemetryCorridorSnapshot;
        float firstViewRequestRealtime;
        float lastTerrainResultRealtime;
        bool fallbackActive;
        bool flightViewportActive;
        bool landDetailActive;
        bool disposed;
        string status = "TERRAIN CACHE INDEX READY";

        internal AERISTerrainTileSystem(AERISSettings settings,
            AERISTerrainPerformanceController performance)
            : this(settings, performance, null)
        {
        }

        internal AERISTerrainTileSystem(AERISSettings settings,
            AERISTerrainPerformanceController performance,
            AERISMapDramCache mapDramCache)
        {
            this.settings = settings;
            this.performance = performance;
            long ramLimit = ResolveRamLimitBytes(settings, performance);
            long diskLimit = ResolveDiskLimitBytes(settings, performance);
            ram = new AERISTerrainRamTileCache(ramLimit);
            currentBodyResidentCache = new AERISCurrentBodyResidentCache(
                ResolveResidentCacheBudgetBytes(settings, performance));
            string legacyRoot = Path.Combine(KSPUtil.ApplicationRootPath, "GameData",
                "AERISFlightControl", "PluginData", "TerrainCache");
            disk = new AERISTerrainDiskTileCache(legacyRoot, diskLimit);
            string preloadRoot = Path.Combine(KSPUtil.ApplicationRootPath, "GameData",
                "AERISFlightControl", "PluginData", "TerrainPreloadDatabaseV3");
            preloadDatabase = new AERISTerrainPreloadDatabase(preloadRoot,
                ResolvePreloadLimitBytes(settings), mapDramCache);
            warm = new AERISTerrainWarmTileCache(Math.Max(16L * 1024L * 1024L,
                ramLimit / 2L));
            blockPipeline = new AERISTerrainBlockPipeline(performance);
            preloadBuilder = new AERISTerrainPreloadBuilder(settings, performance,
                preloadDatabase, blockPipeline, preloadTelemetry);
            RequestGameDataHash();
        }

        internal bool IndexLoaded { get { return preloadDatabase.IndexLoaded; } }
        internal AERISCurrentBodyResidentCache CurrentBodyResidentCache
        {
            get { return currentBodyResidentCache; }
        }
        internal AERISPredictiveForwardCorridorSnapshot PredictiveCorridorSnapshot
        {
            get { return predictiveCorridor.Snapshot(); }
        }
        internal bool BodySupported { get; private set; }
        internal string StatusText { get { return status; } }
        internal long TerrainGeneration { get { return terrainGeneration; } }
        internal long ViewGeneration { get { return viewGeneration; } }
        internal string ActiveBodyName { get { return activeBodyName; } }
        internal bool LandDetailRequestsActive { get { return landDetailActive; } }
        // CP3 Gate 3.1 Compile Hotfix 1: SYSTEM UI reads these values directly
        // from the tile-system owner. Keep the public-facing names stable while the
        // internal telemetry snapshot retains GlobalFoundationCount/FarFoundationCount.
        internal int FoundationGlobalCount { get { return lastFoundationGlobalCount; } }
        internal int FoundationFarCount { get { return lastFoundationFarCount; } }
        internal int FoundationMissingCount { get { return lastFoundationMissingCount; } }
        internal int FoundationRequestedCount { get { return lastFoundationRequestedCount; } }
        internal AERISTerrainPreloadStatusSnapshot PreloadStatus
        {
            get
            {
                if (preloadBuilder == null)
                {
                    if (cachedPreloadStatus == null)
                        cachedPreloadStatus = new AERISTerrainPreloadStatusSnapshot();
                    return cachedPreloadStatus;
                }
                float now = Time.realtimeSinceStartup;
                if (cachedPreloadStatus == null ||
                    now >= nextPreloadStatusUiRefreshRealtime)
                {
                    cachedPreloadStatus = preloadBuilder.SnapshotStatus();
                    nextPreloadStatusUiRefreshRealtime =
                        now + PreloadStatusUiRefreshSeconds;
                }
                return cachedPreloadStatus;
            }
        }

        void InvalidatePreloadStatusUiSnapshot()
        {
            nextPreloadStatusUiRefreshRealtime = 0f;
        }

        internal void SetPreloadEnabled(bool enabled)
        {
            if (preloadBuilder != null) preloadBuilder.SetEnabled(enabled);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadBuild(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.RequestBuild(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadPause(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.Pause(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadResume(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.Resume(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadCancel(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.Cancel(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadVerify(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.RequestVerify(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadRebuild(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.RequestRebuild(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void PreloadDelete(string bodyName)
        {
            if (preloadBuilder != null) preloadBuilder.RequestDelete(bodyName);
            InvalidatePreloadStatusUiSnapshot();
        }

        internal void Reset(string reason)
        {
            ReleaseResidentPlanPins();
            predictiveCorridor.Reset(reason);
            if (currentBodyResidentCache != null)
                currentBodyResidentCache.Reset(reason);
            activeBody = null;
            activeBodyName = string.Empty;
            preloadDatabase.SetActiveBodyProtection(string.Empty);
            environmentHash = string.Empty;
            BodySupported = false;
            lastSampleRealtime = 0f;
            lastSamplingBatchSamples = 0;
            lastSamplingBatchMilliseconds = 0.0;
            nextPlanRealtime = 0f;
            displayViewValid = false;
            displayViewLatitudeDeg = 0.0;
            displayViewLongitudeDeg = 0.0;
            displayViewRangeMeters = 0.0;
            displayViewHeadingDeg = 0.0;
            displayViewTrackUp = false;
            displayViewAnchorGuiV = 0.5f;
            displayViewOrientation = AERISTerrainRenderTargetOrientation.Direct;
            lastFoundationRequestedCount = 0;
            lastFoundationMissingCount = 0;
            lastFoundationGlobalCount = 0;
            lastFoundationFarCount = 0;
            flightViewportActive = false;
            landDetailActive = false;
            bodyGeneration++;
            terrainGeneration++;
            terrainRequestGeneration++;
            viewGeneration++;
            rangeGeneration++;
            planGeneration++;
            blockPipeline.CancelWhere(request => request == null ||
                request.WorkOwner != AERISTerrainWorkOwner.FlightFallback);
            lock (sync)
            {
                queued.Clear();
                diskLoading.Clear();
                preloadChunksLoading.Clear();
                residentChunksLoading.Clear();
                residentPopulationPlan.Clear();
                residentPopulationCursor = 0;
                residentPopulationBlockedFromLod = 5;
                residentPopulationScopeGeneration = -1L;
                residentPopulationIndexGeneration = -1L;
                residentLoadsInFlight = 0;
                preloadChunkTileIds.Clear();
                diskLoadingSince.Clear();
                diskLoadingRequests.Clear();
                desiredRequestIds.Clear();
                desiredVisibleIds.Clear();
                diskLoadsInFlight = 0;
                visibleKeys.Clear();
            }
            status = string.IsNullOrEmpty(reason) ? "TERRAIN TILE RESET" :
                "TERRAIN TILE RESET: " + reason.ToUpperInvariant();
        }

        internal void Tick(Vessel vessel, AERISLandingFoundation landing)
        {
            Tick(vessel, landing, null);
        }

        internal void Tick(Vessel vessel, AERISLandingFoundation landing,
            AERISAirfieldRegistry airfields)
        {
            Tick(vessel, landing, airfields, true);
        }

        internal void Tick(Vessel vessel, AERISLandingFoundation landing,
            AERISAirfieldRegistry airfields, bool flightViewportEnabled)
        {
            Tick(vessel, landing, airfields, flightViewportEnabled,
                landing != null && landing.Armed);
        }

        internal void Tick(Vessel vessel, AERISLandingFoundation landing,
            AERISAirfieldRegistry airfields, bool flightViewportEnabled,
            bool landDetailDemand)
        {
            if (disposed) return;
            if (landDetailActive != landDetailDemand)
            {
                landDetailActive = landDetailDemand;
                nextPlanRealtime = 0f;
                terrainRequestGeneration++;
                viewGeneration++;
                planGeneration++;
                status = landDetailActive ?
                    "LAND DETAIL REQUEST LANE ACTIVATED" :
                    "LAND DETAIL REQUEST LANE RELEASED";
            }
            RequestGameDataHash();
            bool flight = HighLogic.LoadedSceneIsFlight;
            CelestialBody currentBody = vessel == null ? null : vessel.mainBody;
            RefreshPreloadPoints(vessel, landing, airfields);
            if (!GameDataHashReady)
            {
                if (!flightViewportEnabled) SuspendFlightViewport();
                status = "PRELOAD GAMEDATA HASHING";
                PublishPreloadTelemetry();
                UpdateTelemetry();
                return;
            }
            if (preloadBuilder != null) preloadBuilder.Tick(currentBody, flight);
            PublishPreloadTelemetry();
            if (flight && currentBody != null) SynchronizeResidentScope(currentBody);
            if (!flightViewportEnabled)
            {
                SuspendFlightViewport();
                ScheduleResidentPopulationRead();
                status = "FLIGHT TERRAIN VIEWPORT ALTITUDE-GATED OFF — PRELOAD CONTINUES; RESIDENT POPULATION CONTINUES";
                UpdateTelemetry();
                return;
            }
            if (!flight || vessel == null || currentBody == null)
            {
                SuspendFlightViewport();
                UpdateTelemetry();
                return;
            }
            ResumeFlightViewport();
            CelestialBody body = currentBody;
            if (!ReferenceEquals(activeBody, body) ||
                !string.Equals(activeBodyName, body.name, StringComparison.Ordinal))
                BeginBody(body);
            if (!BodySupported)
            {
                UpdateTelemetry();
                return;
            }
            RefreshTerrainRequestGeneration();
            if (settings != null && settings.TerrainDisplayMode == AERISTerrainDisplayMode.Off)
            {
                ReleaseResidentPlanPins();
                predictiveCorridor.Reset("TERRAIN DISPLAY OFF");
                ScheduleResidentPopulationRead();
                status = "TERRAIN DISPLAY OFF — RESIDENT POPULATION CONTINUES";
                UpdateTelemetry();
                return;
            }

            long ramLimit = ResolveRamLimitBytes(settings, performance);
            ram.SetLimit(ramLimit);
            warm.SetLimit(Math.Max(16L * 1024L * 1024L, ramLimit / 2L));
            preloadDatabase.SetLimit(ResolvePreloadLimitBytes(settings));
            disk.SetLimit(ResolveDiskLimitBytes(settings, performance));
            float now = Time.realtimeSinceStartup;
            RecoverAbandonedIo(now);
            RetryPendingDiskWrites(now);
            if (now >= nextPlanRealtime)
            {
                nextPlanRealtime = now + ResolvePlanningIntervalSeconds(performance);
                PlanRequests(vessel, landing);
            }
            SchedulePreloadReads();
            ScheduleResidentPopulationRead();
            StartNextRequestIfNeeded();
            AERISTerrainPerformanceProfile profile = performance == null ? null :
                performance.ActiveProfile;
            float qps = profile == null ? 180f : profile.TilePqsQueriesPerSecond;
            int maximumSamples = profile == null ? 8 : profile.MaximumTileSamplesPerFrame;
            float budget = profile == null ? 0.60f :
                Math.Max(0.10f, profile.TileMainThreadBudgetMs);
            float elapsed = lastSampleRealtime > 0f ?
                Mathf.Clamp(now - lastSampleRealtime, 0f, 0.25f) : 0f;
            lastSampleRealtime = now;
            blockPipeline.Tick(qps, maximumSamples, budget, elapsed);
            lastSamplingBatchSamples = blockPipeline.LastBatchSamples;
            lastSamplingBatchMilliseconds = blockPipeline.LastBatchMilliseconds;
            UpdateTelemetry();
        }

        void SuspendFlightViewport()
        {
            ReleaseResidentPlanPins();
            predictiveCorridor.Reset("VIEWPORT SUSPENDED");
            if (!flightViewportActive) return;
            flightViewportActive = false;
            displayViewValid = false;
            displayViewLatitudeDeg = 0.0;
            displayViewLongitudeDeg = 0.0;
            displayViewRangeMeters = 0.0;
            displayViewHeadingDeg = 0.0;
            displayViewTrackUp = false;
            displayViewAnchorGuiV = 0.5f;
            displayViewOrientation = AERISTerrainRenderTargetOrientation.Direct;
            lastFoundationRequestedCount = 0;
            lastFoundationMissingCount = 0;
            lastFoundationGlobalCount = 0;
            lastFoundationFarCount = 0;
            nextPlanRealtime = 0f;
            terrainRequestGeneration++;
            viewGeneration++;
            rangeGeneration++;
            planGeneration++;
            desiredRequestIds.Clear();
            desiredVisibleIds.Clear();
            desiredFoundationIds.Clear();
            visibleFoundationIds.Clear();
            visibleKeys.Clear();
            ReconcilePlannedRequests();
            fallbackActive = false;
            preloadTelemetry.ViewportCoverageRatio = 0.0;
            preloadTelemetry.FirstTileVisibleMilliseconds = 0.0;
            status = "FLIGHT TERRAIN VIEWPORT SUSPENDED";
        }

        void ResumeFlightViewport()
        {
            if (flightViewportActive) return;
            flightViewportActive = true;
            displayViewValid = false;
            nextPlanRealtime = 0f;
            firstViewRequestRealtime = 0f;
            terrainRequestGeneration++;
            viewGeneration++;
            rangeGeneration++;
            planGeneration++;
            status = "FLIGHT TERRAIN VIEWPORT REACTIVATED";
        }

        void SynchronizeResidentScope(CelestialBody body)
        {
            if (currentBodyResidentCache == null || body == null) return;
            bool supported = BodyHasSolidSurface(body);
            string residentEnvironmentHash = supported ?
                EnvironmentHashForBody(body) : string.Empty;
            currentBodyResidentCache.SetRamBudget(
                ResolveResidentCacheBudgetBytes(settings, performance),
                "PERFORMANCE PROFILE");
            long requestEpoch = preloadDatabase == null ? 0L :
                preloadDatabase.RequestGeneration;
            bool scopeChanged = currentBodyResidentCache.BeginBody(body.name,
                body.Radius, residentEnvironmentHash, requestEpoch,
                "CURRENT BODY / DATABASE REQUEST EPOCH SYNC");
            RefreshResidentPopulationPlan(body.name, residentEnvironmentHash,
                scopeChanged);
        }

        void RefreshResidentPopulationPlan(string bodyName, string residentEnvironmentHash,
            bool scopeChanged)
        {
            if (preloadDatabase == null || currentBodyResidentCache == null ||
                !currentBodyResidentCache.Active) return;
            long scope = currentBodyResidentCache.ScopeGeneration;
            long indexGeneration = preloadDatabase.DatabaseGeneration;
            lock (sync)
            {
                if (!scopeChanged && residentPopulationScopeGeneration == scope &&
                    residentPopulationIndexGeneration == indexGeneration) return;
            }

            AERISTerrainTileKey[] indexed = preloadDatabase.SnapshotCompleteKeysForBody(
                bodyName, residentEnvironmentHash);
            lock (sync)
            {
                residentPopulationPlan.Clear();
                for (int i = 0; i < indexed.Length; i++)
                    if (IsBackgroundPopulationLod(indexed[i].Lod))
                        residentPopulationPlan.Add(indexed[i]);
                residentPopulationCursor = 0;
                residentPopulationBlockedFromLod = 5;
                residentPopulationScopeGeneration = scope;
                residentPopulationIndexGeneration = indexGeneration;
                if (scopeChanged)
                {
                    residentChunksLoading.Clear();
                    residentLoadsInFlight = 0;
                }
            }
        }

        void BeginBody(CelestialBody body)
        {
            ReleaseResidentPlanPins();
            predictiveCorridor.Reset("BODY TRANSITION");
            activeBody = body;
            activeBodyName = body == null ? string.Empty : body.name;
            preloadDatabase.SetActiveBodyProtection(activeBodyName);
            BodySupported = BodyHasSolidSurface(body);
            environmentHash = BodySupported ? EnvironmentHashForBody(body) : string.Empty;
            if (currentBodyResidentCache != null)
                currentBodyResidentCache.BeginBody(activeBodyName,
                    body == null ? 0.0 : body.Radius, environmentHash,
                    preloadDatabase == null ? 0L : preloadDatabase.RequestGeneration,
                    "TERRAIN BODY BEGIN / DATABASE REQUEST EPOCH");
            lastSampleRealtime = Time.realtimeSinceStartup;
            lastSamplingBatchSamples = 0;
            lastSamplingBatchMilliseconds = 0.0;
            displayViewValid = false;
            displayViewLatitudeDeg = 0.0;
            displayViewLongitudeDeg = 0.0;
            displayViewRangeMeters = 0.0;
            displayViewHeadingDeg = 0.0;
            displayViewTrackUp = false;
            displayViewAnchorGuiV = 0.5f;
            displayViewOrientation = AERISTerrainRenderTargetOrientation.Direct;
            lastFoundationRequestedCount = 0;
            lastFoundationMissingCount = 0;
            lastFoundationGlobalCount = 0;
            lastFoundationFarCount = 0;
            bodyGeneration++;
            terrainGeneration++;
            terrainRequestGeneration++;
            viewGeneration++;
            rangeGeneration++;
            planGeneration++;
            blockPipeline.CancelWhere(request => request == null ||
                request.WorkOwner != AERISTerrainWorkOwner.FlightFallback);
            lock (sync)
            {
                queued.Clear();
                diskLoading.Clear();
                preloadChunksLoading.Clear();
                residentChunksLoading.Clear();
                residentPopulationPlan.Clear();
                residentPopulationCursor = 0;
                residentPopulationBlockedFromLod = 5;
                residentPopulationScopeGeneration = -1L;
                residentPopulationIndexGeneration = -1L;
                residentLoadsInFlight = 0;
                preloadChunkTileIds.Clear();
                diskLoadingSince.Clear();
                diskLoadingRequests.Clear();
                desiredRequestIds.Clear();
                desiredVisibleIds.Clear();
                desiredFoundationIds.Clear();
                visibleFoundationIds.Clear();
                visibleKeys.Clear();
                diskLoadsInFlight = 0;
            }
            if (preloadBuilder != null && body != null)
                preloadBuilder.NoteVisitedBody(body.name);
            status = BodySupported ? "PRELOAD TERRAIN READ / BLOCK FALLBACK READY" :
                "NO SOLID TERRAIN SURFACE";
        }

        void RefreshPreloadPoints(Vessel vessel, AERISLandingFoundation landing,
            AERISAirfieldRegistry airfields)
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextPreloadPointRefreshRealtime) return;
            nextPreloadPointRefreshRealtime = now + 2f;
            var values = new List<AERISTerrainPreloadPoint>(256);
            if (vessel != null && vessel.mainBody != null &&
                IsFinite(vessel.latitude) && IsFinite(vessel.longitude))
            {
                values.Add(new AERISTerrainPreloadPoint
                {
                    BodyName = vessel.mainBody.name,
                    LatitudeDeg = vessel.latitude,
                    LongitudeDeg = vessel.longitude,
                    MaximumLod = AERISTerrainTileLod.Local,
                    Priority = 120,
                    Reason = "CURRENT VESSEL"
                });
            }
            AERISRunwayDirectionDefinition active = landing == null ? null :
                landing.ActiveDirection;
            if (active != null && active.Threshold != null && active.Threshold.IsFinite)
            {
                string bodyName = vessel != null && vessel.mainBody != null ?
                    vessel.mainBody.name : "Kerbin";
                values.Add(new AERISTerrainPreloadPoint
                {
                    BodyName = bodyName,
                    LatitudeDeg = active.Threshold.LatitudeDeg,
                    LongitudeDeg = active.Threshold.LongitudeDeg,
                    MaximumLod = AERISTerrainTileLod.Land,
                    Priority = 140,
                    Reason = "SELECTED LAND RUNWAY"
                });
            }
            if (airfields != null && airfields.Airfields != null)
            {
                IList<AERISAirfieldDefinition> all = airfields.Airfields;
                for (int i = 0; i < all.Count && values.Count < 256; i++)
                {
                    AERISAirfieldDefinition airfield = all[i];
                    if (airfield == null || airfield.Runways == null) continue;
                    for (int r = 0; r < airfield.Runways.Count && values.Count < 256; r++)
                    {
                        AERISRunwayDefinition runway = airfield.Runways[r];
                        if (runway == null || runway.Directions == null) continue;
                        for (int d = 0; d < runway.Directions.Count && values.Count < 256; d++)
                        {
                            AERISRunwayDirectionDefinition direction = runway.Directions[d];
                            if (direction == null || direction.Threshold == null ||
                                !direction.Threshold.IsFinite) continue;
                            values.Add(new AERISTerrainPreloadPoint
                            {
                                BodyName = string.IsNullOrEmpty(airfield.Body) ? "Kerbin" :
                                    airfield.Body,
                                LatitudeDeg = direction.Threshold.LatitudeDeg,
                                LongitudeDeg = direction.Threshold.LongitudeDeg,
                                MaximumLod = direction.HasCertifiedGeometry ?
                                    AERISTerrainTileLod.Land : AERISTerrainTileLod.Local,
                                Priority = direction.HasCertifiedGeometry ? 100 : 70,
                                Reason = "REGISTERED RUNWAY"
                            });
                        }
                    }
                }
            }
            if (preloadBuilder != null) preloadBuilder.UpdatePoints(values);
        }

        void PlanRequests(Vessel vessel, AERISLandingFoundation landing)
        {
            if (vessel == null || activeBody == null) return;
            double range = displayViewValid ? displayViewRangeMeters :
                AERISSettings.NormalizeNavigationRange(settings == null ? 20000f :
                    settings.NavigationDisplayManualRangeMeters);
            // displayViewRangeMeters can be an internal temporal-overscan range.
            // Preserve that exact bounded value for viewport-foundation planning.
            range = Math.Max(1000.0, Math.Min(250000.0, range));
            double vesselLatitude = vessel.latitude;
            double vesselLongitude = NormalizeLongitude(vessel.longitude);
            double latitude = displayViewValid ? displayViewLatitudeDeg : vesselLatitude;
            double longitude = displayViewValid ? displayViewLongitudeDeg : vesselLongitude;
            AERISTerrainPerformanceProfile profile = performance == null ? null :
                performance.ActiveProfile;
            int detailBudget = profile == null ? 28 :
                profile.MaximumTerrainTileRequests;
            int planningCapacity = Math.Max(256, detailBudget * 4 +
                AERISTerrainViewportFoundationPlanner.MaximumFarKeys +
                AERISTerrainViewportFoundationPlanner.MaximumGlobalKeys);
            requestScratch.Clear();
            acceptedRequestScratch.Clear();
            visibleKeys.Clear();
            desiredRequestIds.Clear();
            desiredVisibleIds.Clear();
            desiredFoundationIds.Clear();
            visibleFoundationIds.Clear();
            desiredResidentPinKeys.Clear();
            desiredResidentPinReasons.Clear();

            // CP3 Gate 3.1 foundation authority: derive Global/Far requests from the
            // actual ND projection (rotation, 1.30 horizontal scale and aircraft anchor).
            // This set is admitted in full before any exact detail or look-ahead work.
            AERISTerrainViewportFoundationPlan foundation =
                AERISTerrainViewportFoundationPlanner.Build(activeBody,
                    environmentHash, latitude, longitude, range,
                    displayViewHeadingDeg, displayViewTrackUp,
                    displayViewAnchorGuiV, displayViewOrientation);
            AddFoundationKeys(foundation.GlobalKeys, planningCapacity);
            AddFoundationKeys(foundation.FarKeys, planningCapacity);
            lastFoundationGlobalCount = foundation.GlobalKeys.Length;
            lastFoundationFarCount = foundation.FarKeys.Length;
            // FAR is the sole persistent display authority. GLOBAL remains an
            // unconditional bootstrap/fallback layer but does not delay FAR-complete
            // telemetry or the transition to reconstructed detail.
            lastFoundationRequestedCount = foundation.FarKeys.Length;

            AERISTerrainTileLod nearLod = ResolveNearLod(range, profile);
            AddExistingExactDetailBridge(latitude, longitude, nearLod,
                planningCapacity, profile == null ? 1 : profile.LocalTileRadius);

            // PLAN may be centred away from the aircraft. Keep only a coarse, non-visible
            // current-vessel fallback hot for RECENTER. Route/Local are no longer
            // background-populated or generated merely because the aircraft may return.
            AddPoint(vesselLatitude, vesselLongitude, AERISTerrainTileLod.Far,
                AERISTerrainTilePriority.High, AERISTerrainRequestLane.Background,
                planningCapacity, false);

            // Selected runway terrain remains exact and demand-gated. It is the only
            // normal path allowed to request LOCAL/LAND payloads that do not already
            // exist in the database while the virtual-detail renderer is introduced.
            AERISRunwayDirectionDefinition direction = !landDetailActive ||
                landing == null || !landing.Armed ? null : landing.ActiveDirection;
            if (direction != null)
            {
                double thresholdLat = direction.Threshold == null ? double.NaN :
                    direction.Threshold.LatitudeDeg;
                double thresholdLon = direction.Threshold == null ? double.NaN :
                    direction.Threshold.LongitudeDeg;
                AddLandingPointWithPins(thresholdLat, thresholdLon, planningCapacity);

                double reciprocalLat = direction.OppositeThreshold == null ? double.NaN :
                    direction.OppositeThreshold.LatitudeDeg;
                double reciprocalLon = direction.OppositeThreshold == null ? double.NaN :
                    direction.OppositeThreshold.LongitudeDeg;
                AddLandingPointWithPins(reciprocalLat, reciprocalLon, planningCapacity);
            }

            IList<AERISPredictiveCorridorPoint> corridor = predictiveCorridor.Build(
                activeBody, vessel, range, nearLod, direction != null);
            for (int i = 0; i < corridor.Count; i++)
            {
                AERISPredictiveCorridorPoint point = corridor[i];
                if (point == null) continue;
                // The predictive corridor now warms the sole persistent FAR base. Route
                // and Local quality will be reconstructed from this base in Gate 4B;
                // exact detail is reserved for viewport hits already on SSD and LAND.
                AddPoint(point.LatitudeDeg, point.LongitudeDeg,
                    AERISTerrainTileLod.Far, point.Priority,
                    AERISTerrainRequestLane.LookAhead, planningCapacity, false);
                if (point.Centerline)
                    MarkResidentPin(KeyForPoint(activeBody, environmentHash,
                        AERISTerrainTileLod.Far, point.LatitudeDeg,
                        point.LongitudeDeg), AERISResidentPinReason.ForwardCorridor);
            }

            requestScratch.Sort(CompareRequests);
            // Foundation keys are accepted unconditionally. The legacy profile maximum
            // now budgets only non-foundation exact/corridor work, so LOW quality cannot
            // clip the coarse viewport back to a fixed 3x3 square.
            for (int i = 0; i < requestScratch.Count; i++)
            {
                AERISTerrainTileRequest request = requestScratch[i];
                if (request != null &&
                    desiredFoundationIds.Contains(request.Key.StableId))
                    acceptedRequestScratch.Add(request);
            }
            int admittedDetail = 0;
            for (int i = 0; i < requestScratch.Count && admittedDetail < detailBudget; i++)
            {
                AERISTerrainTileRequest request = requestScratch[i];
                if (request == null ||
                    desiredFoundationIds.Contains(request.Key.StableId)) continue;
                acceptedRequestScratch.Add(request);
                admittedDetail++;
            }

            int acceptedCorridorCount = 0;
            for (int i = 0; i < acceptedRequestScratch.Count; i++)
            {
                AERISTerrainTileRequest accepted = acceptedRequestScratch[i];
                string id = accepted.Key.StableId;
                desiredRequestIds.Add(id);
                if (accepted.Lane == AERISTerrainRequestLane.LookAhead)
                    acceptedCorridorCount++;
                if (accepted.Visible)
                {
                    desiredVisibleIds.Add(id);
                    if (accepted.Key.Lod == AERISTerrainTileLod.Far &&
                        desiredFoundationIds.Contains(id))
                        visibleFoundationIds.Add(id);
                    if (!ContainsVisibleKey(accepted.Key)) visibleKeys.Add(accepted.Key);
                }
            }

            // Remove work that belonged to an old range/centre before admitting the new
            // plan. This is the principal bound that prevents pending=192 style growth.
            ReconcilePlannedRequests();
            for (int i = 0; i < acceptedRequestScratch.Count; i++)
                EnsureRequest(acceptedRequestScratch[i]);
            RefreshResidentPlanPins();
            predictiveCorridor.SetRuntimeCounts(acceptedCorridorCount,
                residentPlanPins.Count);
        }

        void AddFoundationKeys(AERISTerrainTileKey[] keys, int planningCapacity)
        {
            if (keys == null) return;
            for (int i = 0; i < keys.Length; i++)
            {
                AERISTerrainTileKey key = keys[i];
                desiredFoundationIds.Add(key.StableId);
                AddKey(key, AERISTerrainTilePriority.Critical,
                    AERISTerrainRequestLane.Viewport, planningCapacity, true);
                MarkResidentPin(key, key.Lod == AERISTerrainTileLod.Global ?
                    AERISResidentPinReason.GlobalFoundation :
                    AERISResidentPinReason.Viewport);
            }
        }

        void AddExistingExactDetailBridge(double latitude, double longitude,
            AERISTerrainTileLod nearLod, int planningCapacity, int requestedRadius)
        {
            if (nearLod <= AERISTerrainTileLod.Far || activeBody == null) return;
            AERISTerrainTileKey center = KeyForPoint(activeBody, environmentHash,
                nearLod, latitude, longitude);
            int radius = Math.Max(0, Math.Min(1, requestedRadius));
            int latitudeCount = LatitudeTileCount(activeBody, nearLod);
            int longitudeCount = LongitudeTileCount(activeBody, nearLod);
            for (int dy = -radius; dy <= radius; dy++)
            {
                int latitudeIndex = center.LatitudeIndex + dy;
                if (latitudeIndex < 0 || latitudeIndex >= latitudeCount) continue;
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int longitudeIndex = WrapIndex(center.LongitudeIndex + dx,
                        longitudeCount);
                    var key = new AERISTerrainTileKey(activeBodyName,
                        activeBody.Radius, environmentHash, nearLod, latitudeIndex,
                        longitudeIndex);
                    if (!ExactDetailPayloadExists(key)) continue;
                    AddKey(key, AERISTerrainTilePriority.High,
                        AERISTerrainRequestLane.Viewport, planningCapacity, true);
                    MarkResidentPin(key, AERISResidentPinReason.Viewport);
                }
            }
        }

        bool ExactDetailPayloadExists(AERISTerrainTileKey key)
        {
            AERISTerrainHeightTile existing;
            if (ram.TryGet(key, out existing) && existing != null) return true;
            return preloadDatabase != null && preloadDatabase.Contains(key);
        }

        void AddLandingPointWithPins(double latitude, double longitude, int maximum)
        {
            if (!IsFinite(latitude) || !IsFinite(longitude)) return;
            AddPointWithFallback(latitude, longitude, AERISTerrainTileLod.Land,
                AERISTerrainTilePriority.Critical, AERISTerrainRequestLane.Landing,
                maximum, false);
            MarkResidentPin(KeyForPoint(activeBody, environmentHash,
                AERISTerrainTileLod.Land, latitude, longitude),
                AERISResidentPinReason.Landing);
            MarkResidentPin(KeyForPoint(activeBody, environmentHash,
                AERISTerrainTileLod.Local, latitude, longitude),
                AERISResidentPinReason.Runway);
        }

        void MarkResidentPin(AERISTerrainTileKey key,
            AERISResidentPinReason reason)
        {
            if (reason == AERISResidentPinReason.None ||
                !IsGate3ResidentLod(key.Lod)) return;
            string id = key.StableId;
            AERISResidentPinReason existing;
            if (desiredResidentPinReasons.TryGetValue(id, out existing) &&
                PinPriority(existing) >= PinPriority(reason)) return;
            desiredResidentPinKeys[id] = key;
            desiredResidentPinReasons[id] = reason;
        }

        void RefreshResidentPlanPins()
        {
            var stale = new List<string>();
            foreach (KeyValuePair<string, AERISResidentPinLease> pair in residentPlanPins)
            {
                AERISResidentPinReason activeReason;
                AERISResidentPinReason desiredReason;
                bool keep = desiredRequestIds.Contains(pair.Key) &&
                    residentPlanPinReasons.TryGetValue(pair.Key, out activeReason) &&
                    desiredResidentPinReasons.TryGetValue(pair.Key, out desiredReason) &&
                    activeReason == desiredReason;
                if (!keep) stale.Add(pair.Key);
            }
            for (int i = 0; i < stale.Count; i++) ReleaseResidentPlanPin(stale[i]);

            if (currentBodyResidentCache == null || !currentBodyResidentCache.Active ||
                preloadDatabase == null) return;
            foreach (KeyValuePair<string, AERISResidentPinReason> pair in
                desiredResidentPinReasons)
            {
                string id = pair.Key;
                if (!desiredRequestIds.Contains(id) || residentPlanPins.ContainsKey(id))
                    continue;
                AERISTerrainTileKey key;
                if (!desiredResidentPinKeys.TryGetValue(id, out key) ||
                    !preloadDatabase.Contains(key)) continue;
                AERISResidentCommitToken token;
                if (!currentBodyResidentCache.RegisterIndexed(key,
                    preloadDatabase.RequestGeneration, 0L, out token)) continue;
                AERISResidentPinLease lease;
                if (!currentBodyResidentCache.TryPin(key, pair.Value, out lease) ||
                    lease == null) continue;
                residentPlanPins[id] = lease;
                residentPlanPinReasons[id] = pair.Value;
            }
        }

        void ReleaseResidentPlanPin(string stableId)
        {
            AERISResidentPinLease lease;
            if (residentPlanPins.TryGetValue(stableId ?? string.Empty, out lease))
            {
                residentPlanPins.Remove(stableId ?? string.Empty);
                residentPlanPinReasons.Remove(stableId ?? string.Empty);
                if (lease != null) lease.Dispose();
            }
        }

        void ReleaseResidentPlanPins()
        {
            var leases = new List<AERISResidentPinLease>(residentPlanPins.Values);
            residentPlanPins.Clear();
            residentPlanPinReasons.Clear();
            desiredResidentPinKeys.Clear();
            desiredResidentPinReasons.Clear();
            for (int i = 0; i < leases.Count; i++)
                if (leases[i] != null) leases[i].Dispose();
        }

        static int PinPriority(AERISResidentPinReason reason)
        {
            switch (reason)
            {
                case AERISResidentPinReason.Landing: return 6;
                case AERISResidentPinReason.Runway: return 5;
                case AERISResidentPinReason.Viewport: return 4;
                case AERISResidentPinReason.GlobalFoundation: return 3;
                case AERISResidentPinReason.ForwardCorridor: return 2;
                default: return 0;
            }
        }

        void AddPointWithFallback(double latitude, double longitude,
            AERISTerrainTileLod lod, AERISTerrainTilePriority priority,
            AERISTerrainRequestLane lane, int maximum, bool visible = true)
        {
            if (!IsFinite(latitude) || !IsFinite(longitude)) return;
            AddPoint(latitude, longitude, lod, priority, lane, maximum, visible);
            if (lod > AERISTerrainTileLod.Global)
            {
                AERISTerrainTileLod fallback = lod == AERISTerrainTileLod.Land ?
                    AERISTerrainTileLod.Local : (AERISTerrainTileLod)((int)lod - 1);
                AddPoint(latitude, longitude, fallback,
                    priority > AERISTerrainTilePriority.Low ?
                        (AERISTerrainTilePriority)((int)priority - 1) : priority,
                    lane, maximum, visible);
            }
        }

        void AddNeighbourhood(double latitude, double longitude,
            AERISTerrainTileLod lod, AERISTerrainTilePriority priority,
            AERISTerrainRequestLane lane, int maximum, int radius, bool visible = true)
        {
            if (!IsFinite(latitude) || !IsFinite(longitude)) return;
            AERISTerrainTileKey center = KeyForPoint(activeBody, environmentHash,
                lod, latitude, longitude);
            int countLon = LongitudeTileCount(activeBody, lod);
            int countLat = LatitudeTileCount(activeBody, lod);
            for (int ring = 0; ring <= radius; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                        if (requestScratch.Count >= maximum * 2) return;
                        int latIndex = center.LatitudeIndex + dy;
                        if (latIndex < 0 || latIndex >= countLat) continue;
                        int lonIndex = WrapIndex(center.LongitudeIndex + dx, countLon);
                        AddKey(new AERISTerrainTileKey(activeBodyName, activeBody.Radius,
                            environmentHash, lod, latIndex, lonIndex), priority,
                            lane, maximum, visible);
                    }
                }
            }
        }

        void AddPoint(double latitude, double longitude, AERISTerrainTileLod lod,
            AERISTerrainTilePriority priority, AERISTerrainRequestLane lane,
            int maximum, bool visible)
        {
            if (!IsFinite(latitude) || !IsFinite(longitude)) return;
            AddKey(KeyForPoint(activeBody, environmentHash, lod, latitude, longitude),
                priority, lane, maximum, visible);
        }

        void AddKey(AERISTerrainTileKey key, AERISTerrainTilePriority priority,
            AERISTerrainRequestLane lane, int maximum, bool visible)
        {
            if (requestScratch.Count >= maximum * 2) return;
            for (int i = 0; i < requestScratch.Count; i++)
            {
                if (!requestScratch[i].Key.Equals(key)) continue;
                if (priority > requestScratch[i].Priority)
                    requestScratch[i].Priority = priority;
                if (lane < requestScratch[i].Lane) requestScratch[i].Lane = lane;
                requestScratch[i].Visible = requestScratch[i].Visible || visible;
                return;
            }
            AERISTerrainTileRequest request = CreateRequest(key, priority, lane);
            request.Visible = visible;
            requestScratch.Add(request);
        }

        bool ContainsVisibleKey(AERISTerrainTileKey key)
        {
            for (int i = 0; i < visibleKeys.Count; i++)
                if (visibleKeys[i].Equals(key)) return true;
            return false;
        }

        AERISTerrainTileRequest CreateRequest(AERISTerrainTileKey key,
            AERISTerrainTilePriority priority, AERISTerrainRequestLane lane)
        {
            double span = AERISTerrainTileFormat.AngularSpanDegrees(key.Lod,
                activeBody == null ? key.BodyRadiusMillimetres / 1000.0 : activeBody.Radius);
            double south = -90.0 + key.LatitudeIndex * span;
            double north = Math.Min(90.0, south + span);
            double west = -180.0 + key.LongitudeIndex * span;
            double east = west + span;
            int finalResolution = AERISTerrainTileFormat.Resolution(key.Lod);
            int previewResolution = ResolvePreviewResolution(key.Lod, finalResolution);
            double centerLatitude = (south + north) * 0.5;
            double centerLongitude = NormalizeLongitude((west + east) * 0.5);
            double viewLatitude = displayViewValid ? displayViewLatitudeDeg : centerLatitude;
            double viewLongitude = displayViewValid ? displayViewLongitudeDeg : centerLongitude;
            return new AERISTerrainTileRequest
            {
                Key = key,
                Priority = priority,
                Lane = lane,
                Stage = previewResolution < finalResolution ?
                    AERISTerrainSamplingStage.Preview : AERISTerrainSamplingStage.Final,
                CenterLatitudeDeg = centerLatitude,
                CenterLongitudeDeg = centerLongitude,
                SouthLatitudeDeg = Math.Max(-90.0, south),
                NorthLatitudeDeg = north,
                WestLongitudeDeg = NormalizeLongitude(west),
                EastLongitudeDeg = NormalizeLongitude(east),
                Resolution = previewResolution,
                FinalResolution = finalResolution,
                ViewDistanceMeters = GreatCircleDistanceMeters(activeBody,
                    viewLatitude, viewLongitude, centerLatitude, centerLongitude),
                RequestSequence = ++requestSequence,
                BodyGeneration = bodyGeneration,
                VesselGeneration = CurrentVesselGeneration(),
                TerrainGeneration = terrainRequestGeneration,
                ViewGeneration = viewGeneration,
                RangeGeneration = rangeGeneration,
                PlanGeneration = planGeneration,
                DatabaseGeneration = preloadDatabase.RequestGeneration,
                ReadLane = MapReadLane(lane, priority),
                WorkOwner = AERISTerrainWorkOwner.FlightFallback
            };
        }

        void ReconcilePlannedRequests()
        {
            int removed = 0;
            var staleChunks = new List<string>();
            var staleLegacy = new List<AERISTerrainTileRequest>();
            lock (sync)
            {
                cancellationScratch.Clear();
                foreach (KeyValuePair<string, AERISTerrainTileRequest> pair in queued)
                    if (!desiredRequestIds.Contains(pair.Key)) cancellationScratch.Add(pair.Key);
                for (int i = 0; i < cancellationScratch.Count; i++)
                    if (queued.Remove(cancellationScratch[i])) removed++;

                foreach (KeyValuePair<string, List<string>> pair in preloadChunkTileIds)
                {
                    bool anyDesired = false;
                    for (int i = 0; i < pair.Value.Count; i++)
                        if (desiredRequestIds.Contains(pair.Value[i]))
                        { anyDesired = true; break; }
                    if (!anyDesired) staleChunks.Add(pair.Key);
                }
                foreach (KeyValuePair<string, AERISTerrainTileRequest> pair in
                    diskLoadingRequests)
                {
                    if (desiredRequestIds.Contains(pair.Key)) continue;
                    bool chunkOwned = false;
                    foreach (List<string> ids in preloadChunkTileIds.Values)
                        if (ids.Contains(pair.Key)) { chunkOwned = true; break; }
                    if (!chunkOwned && pair.Value != null) staleLegacy.Add(pair.Value);
                }
                cancellationScratch.Clear();
            }

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            for (int i = 0; i < staleChunks.Count; i++)
            {
                string chunkId = staleChunks[i];
                List<string> ids = null;
                lock (sync)
                {
                    List<string> tracked;
                    if (preloadChunkTileIds.TryGetValue(chunkId, out tracked))
                        ids = new List<string>(tracked);
                }
                if (runtime != null)
                    runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,
                        "terrain-preload-read:" + AERISTerrainHash.Fnv1A64Hex(chunkId));
                CompleteChunkLoadTracking(chunkId, ids);
                removed += ids == null ? 1 : ids.Count;
            }
            for (int i = 0; i < staleLegacy.Count; i++)
            {
                AERISTerrainTileRequest request = staleLegacy[i];
                string id = request.Key.StableId;
                if (runtime != null)
                    runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,
                        "terrain-legacy-migrate:" + request.Key.FileStem);
                lock (sync)
                {
                    diskLoadingRequests.Remove(id);
                    diskLoadingSince.Remove(id);
                    if (diskLoading.Remove(id))
                        diskLoadsInFlight = Math.Max(0, diskLoadsInFlight - 1);
                }
                removed++;
            }

            int beforeBlocks = blockPipeline.Count;
            blockPipeline.CancelWhere(request => request == null ||
                request.WorkOwner != AERISTerrainWorkOwner.FlightFallback ||
                desiredRequestIds.Contains(request.Key.StableId));
            removed += Math.Max(0, beforeBlocks - blockPipeline.Count);
            if (removed > 0)
            {
                telemetry.StaleCancelled += removed;
                telemetry.ObsoleteCancelled += removed;
                preloadTelemetry.StaleResultsDiscarded += removed;
                status = "OBSOLETE TERRAIN REQUESTS CANCELLED";
            }
        }

        void EnsureRequest(AERISTerrainTileRequest request)
        {
            if (request == null) return;
            AERISTerrainHeightTile tile;
            bool hasRamTile = ram.TryGet(request.Key, out tile) && tile != null;
            if (hasRamTile)
            {
                telemetry.RamHits++;
                telemetry.Reused++;
                if (ReconcileRequestWithRamTile(request, tile)) return;
            }
            else if (IsGate3ResidentLod(request.Key.Lod) &&
                currentBodyResidentCache != null)
            {
                AERISResidentCommitToken residentToken;
                if (currentBodyResidentCache.TryGetRamResident(request.Key, out tile,
                    out residentToken) && tile != null)
                {
                    // The transient viewport cache stores a shared immutable payload
                    // reference. Its LRU no longer mutates tile-owned metadata.
                    ram.Put(tile);
                    telemetry.RamHits++;
                    telemetry.Reused++;
                    terrainGeneration++;
                    lastTerrainResultRealtime = Time.realtimeSinceStartup;
                    status = "CURRENT BODY RAM RESIDENT HIT";
                    if (ReconcileRequestWithRamTile(request, tile)) return;
                }
            }

            if (blockPipeline.RefreshActive(request, IsFlightRequestCurrent))
            {
                telemetry.Reused++;
                return;
            }

            string id = request.Key.StableId;
            bool newlyQueued = false;
            lock (sync)
            {
                AERISTerrainTileRequest loading;
                if (diskLoadingRequests.TryGetValue(id, out loading))
                {
                    MergeRequest(loading, request);
                    return;
                }

                AERISTerrainTileRequest existing;
                if (queued.TryGetValue(id, out existing))
                {
                    MergeRequest(existing, request);
                    return;
                }

                int capacity = ResolveQueueCapacity(performance);
                if (queued.Count >= capacity)
                {
                    string worstId = FindLowestPriorityRequestLocked();
                    AERISTerrainTileRequest worst;
                    if (string.IsNullOrEmpty(worstId) ||
                        !queued.TryGetValue(worstId, out worst) ||
                        CompareRequests(request, worst) >= 0)
                    {
                        telemetry.DroppedRequests++;
                        return;
                    }
                    queued.Remove(worstId);
                    telemetry.DroppedRequests++;
                }
                queued[id] = request;
                newlyQueued = true;
            }

            if (!preloadDatabase.Contains(request.Key) && !disk.Contains(request.Key) &&
                newlyQueued) telemetry.Misses++;
        }

        // Returns true when the requested final fidelity is already complete. Both the
        // planning path and the queue-admission path use this routine so a progressive
        // Preview cannot be promoted merely because it reached RAM between those paths.
        static bool ReconcileRequestWithRamTile(AERISTerrainTileRequest request,
            AERISTerrainHeightTile tile)
        {
            if (request == null || tile == null) return false;
            bool samplingComplete = tile.SamplingComplete;
            if (samplingComplete && !tile.IsPreview &&
                tile.Resolution >= request.FinalResolution) return true;
            if (samplingComplete &&
                (tile.IsPreview || tile.Resolution < request.FinalResolution))
            {
                request.Stage = AERISTerrainSamplingStage.Final;
                request.Resolution = request.FinalResolution;
            }
            else
            {
                // A 25/50/75% commit refreshes the matching active stage; it must
                // never promote an unfinished preview into a concurrent final build.
                request.Stage = tile.Resolution >= request.FinalResolution ?
                    AERISTerrainSamplingStage.Final :
                    AERISTerrainSamplingStage.Preview;
                request.Resolution = tile.Resolution;
            }
            return false;
        }

        static void MergeRequest(AERISTerrainTileRequest target,
            AERISTerrainTileRequest source)
        {
            if (target == null || source == null) return;
            if (source.Priority > target.Priority) target.Priority = source.Priority;
            if (source.Lane < target.Lane) target.Lane = source.Lane;
            target.Visible = target.Visible || source.Visible;
            target.ViewDistanceMeters = Math.Min(target.ViewDistanceMeters,
                source.ViewDistanceMeters);
            target.TerrainGeneration = Math.Max(target.TerrainGeneration,
                source.TerrainGeneration);
            target.ViewGeneration = Math.Max(target.ViewGeneration, source.ViewGeneration);
            target.RangeGeneration = Math.Max(target.RangeGeneration, source.RangeGeneration);
            target.PlanGeneration = Math.Max(target.PlanGeneration, source.PlanGeneration);
            target.DatabaseGeneration = Math.Max(target.DatabaseGeneration,
                source.DatabaseGeneration);
            if (source.ReadLane < target.ReadLane) target.ReadLane = source.ReadLane;
            target.WorkOwner = source.WorkOwner;
            target.VesselGeneration = source.VesselGeneration;
            target.RequestSequence = Math.Max(target.RequestSequence,
                source.RequestSequence);
            target.FinalResolution = Math.Max(target.FinalResolution,
                source.FinalResolution);
            if (source.Stage > target.Stage)
            {
                target.Stage = source.Stage;
                target.Resolution = source.Resolution;
            }
        }

        void SchedulePreloadReads()
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || preloadDatabase == null) return;
            int limit = ResolveConcurrentReadLimit();
            while (diskLoadsInFlight + residentLoadsInFlight < limit)
            {
                AERISTerrainTileRequest best = null;
                string chunkId = string.Empty;
                lock (sync)
                {
                    foreach (AERISTerrainTileRequest request in queued.Values)
                    {
                        if (request == null || diskLoading.Contains(request.Key.StableId) ||
                            !desiredRequestIds.Contains(request.Key.StableId) ||
                            !preloadDatabase.Contains(request.Key)) continue;
                        string candidateChunk;
                        if (!preloadDatabase.TryGetChunkId(request.Key, out candidateChunk) ||
                            preloadChunksLoading.Contains(candidateChunk)) continue;
                        if (best == null || CompareRequests(request, best) < 0)
                        {
                            best = request;
                            chunkId = candidateChunk;
                        }
                    }
                }
                if (best == null || string.IsNullOrEmpty(chunkId)) break;
                if (!TrySchedulePreloadChunk(chunkId, best)) break;
            }
        }

        bool TrySchedulePreloadChunk(string chunkId, AERISTerrainTileRequest seed)
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || string.IsNullOrEmpty(chunkId) || seed == null) return false;
            var requests = new List<AERISTerrainTileRequest>(16);
            var selectedRequests = new List<AERISTerrainTileRequest>(16);
            var keys = new List<AERISTerrainTileKey>(16);
            var ids = new List<string>(16);
            var residentTokens = new Dictionary<string, AERISResidentCommitToken>(
                StringComparer.Ordinal);
            float now = Time.realtimeSinceStartup;
            lock (sync)
            {
                if (preloadChunksLoading.Contains(chunkId)) return false;
                foreach (AERISTerrainTileRequest request in queued.Values)
                {
                    if (request == null || diskLoading.Contains(request.Key.StableId) ||
                        !desiredRequestIds.Contains(request.Key.StableId)) continue;
                    string candidateChunk;
                    if (!preloadDatabase.TryGetChunkId(request.Key, out candidateChunk) ||
                        !string.Equals(candidateChunk, chunkId, StringComparison.Ordinal)) continue;
                    requests.Add(request);
                }
                requests.Sort(CompareRequests);
                int maximum = Math.Min(16, requests.Count);
                for (int i = 0; i < maximum; i++)
                {
                    AERISTerrainTileRequest request = requests[i];
                    string id = request.Key.StableId;
                    AERISResidentCommitToken residentToken;
                    AERISTerrainHeightTile residentTile;
                    if (IsGate3ResidentLod(request.Key.Lod) &&
                        currentBodyResidentCache != null &&
                        currentBodyResidentCache.TryGetRamResident(request.Key,
                            out residentTile, out residentToken) && residentTile != null)
                    {
                        queued.Remove(id);
                        ram.Put(residentTile);
                        telemetry.RamHits++;
                        telemetry.Reused++;
                        terrainGeneration++;
                        lastTerrainResultRealtime = now;
                        status = "CURRENT BODY RAM RESIDENT HIT";
                        continue;
                    }

                    bool alreadyResident = false;
                    if (IsGate3ResidentLod(request.Key.Lod) &&
                        currentBodyResidentCache != null &&
                        currentBodyResidentCache.TryPrepareSsdDecode(request.Key,
                            request.DatabaseGeneration, 0L, out residentToken,
                            out alreadyResident))
                    {
                        if (alreadyResident &&
                            currentBodyResidentCache.TryGetRamResident(request.Key,
                                out residentTile, out residentToken) && residentTile != null)
                        {
                            queued.Remove(id);
                            ram.Put(residentTile);
                            telemetry.RamHits++;
                            telemetry.Reused++;
                            terrainGeneration++;
                            lastTerrainResultRealtime = now;
                            status = "CURRENT BODY RAM RESIDENT HIT";
                            continue;
                        }
                        residentTokens[id] = residentToken;
                    }

                    diskLoading.Add(id);
                    diskLoadingRequests[id] = request;
                    diskLoadingSince[id] = now;
                    selectedRequests.Add(request);
                    ids.Add(id);
                    keys.Add(request.Key);
                }
                if (keys.Count == 0) return false;
                preloadChunksLoading.Add(chunkId);
                preloadChunkTileIds[chunkId] = ids;
                diskLoadsInFlight++;
                preloadTelemetry.DatabaseReadQueueDepth = diskLoadsInFlight;
            }
            Stopwatch queuedWatch = Stopwatch.StartNew();
            bool accepted = runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute,
                "terrain-preload-read:" + AERISTerrainHash.Fnv1A64Hex(chunkId),
                runtime.CaptureStamp(), context =>
                {
                    double queueDelayMilliseconds = queuedWatch.Elapsed.TotalMilliseconds;
                    System.Threading.Interlocked.Increment(
                        ref preloadTelemetry.DecompressWorkerActive);
                    try
                    {
                        var output = new Dictionary<string, AERISTerrainHeightTile>(
                            StringComparer.Ordinal);
                        preloadDatabase.TryLoadBatch(keys, warm, output, preloadTelemetry,
                            GameDataHash);
                        var residentCommitted = new HashSet<string>(StringComparer.Ordinal);
                        foreach (KeyValuePair<string, AERISResidentCommitToken> pair in
                            residentTokens)
                        {
                            AERISTerrainHeightTile tile;
                            if (!output.TryGetValue(pair.Key, out tile) || tile == null)
                            {
                                currentBodyResidentCache.RecordDecodeFailure(pair.Value,
                                    "SSD DECODE FAILED OR TILE MISSING");
                                continue;
                            }
                            tile.IsPreview = false;
                            tile.SamplingComplete = true;
                            tile.Source = AERISTerrainTileSource.PreloadDatabase;
                            if (currentBodyResidentCache.TryMarkDecoded(pair.Value) &&
                                currentBodyResidentCache.TryCommitRamResident(pair.Value, tile))
                                residentCommitted.Add(pair.Key);
                        }
                        return new object[] { output, residentCommitted,
                            queueDelayMilliseconds };
                    }
                    finally
                    {
                        System.Threading.Interlocked.Decrement(
                            ref preloadTelemetry.DecompressWorkerActive);
                    }
                }, value =>
                {
                    queuedWatch.Stop();
                    object[] result = value as object[];
                    var loaded = result != null && result.Length > 0 ?
                        result[0] as Dictionary<string, AERISTerrainHeightTile> : null;
                    var residentCommitted = result != null && result.Length > 1 ?
                        result[1] as HashSet<string> : null;
                    double queueDelayMilliseconds = result != null && result.Length > 2 &&
                        result[2] is double ? (double)result[2] : 0.0;
                    preloadTelemetry.DecompressQueueDelayMilliseconds = UpdateEma(
                        preloadTelemetry.DecompressQueueDelayMilliseconds,
                        queueDelayMilliseconds, 0.15);
                    CompleteChunkLoadTracking(chunkId, ids);
                    for (int i = 0; i < selectedRequests.Count; i++)
                    {
                        AERISTerrainTileRequest request = selectedRequests[i];
                        string id = request.Key.StableId;
                        AERISTerrainHeightTile tile = null;
                        AERISResidentCommitToken residentToken;
                        if (residentCommitted != null && residentCommitted.Contains(id) &&
                            currentBodyResidentCache != null)
                            currentBodyResidentCache.TryGetRamResident(request.Key, out tile,
                                out residentToken);
                        if (tile == null && loaded != null) loaded.TryGetValue(id, out tile);
                        if (tile == null) continue;
                        lock (sync) queued.Remove(id);
                        if (IsFlightRequestCurrent(request))
                        {
                            // Non-resident fallback payloads are finalized here; resident
                            // payload metadata was frozen before ownership transfer.
                            if (residentCommitted == null ||
                                !residentCommitted.Contains(id))
                            {
                                tile.IsPreview = false;
                                tile.SamplingComplete = true;
                                tile.Source = AERISTerrainTileSource.PreloadDatabase;
                            }
                            ram.Put(tile);
                            telemetry.DiskHits++;
                            terrainGeneration++;
                            lastTerrainResultRealtime = Time.realtimeSinceStartup;
                            status = residentCommitted != null &&
                                residentCommitted.Contains(id) ?
                                "RAM RESIDENT TERRAIN TILE READY" :
                                "PRELOAD TERRAIN TILE READY";
                            if (preloadTelemetry.FirstTileVisibleMilliseconds <= 0.0)
                                preloadTelemetry.FirstTileVisibleMilliseconds =
                                    Math.Max(0.0, queuedWatch.Elapsed.TotalMilliseconds);
                        }
                        else
                        {
                            telemetry.StaleCancelled++;
                            preloadTelemetry.StaleResultsDiscarded++;
                        }
                    }
                }, false);
            if (!accepted)
            {
                foreach (KeyValuePair<string, AERISResidentCommitToken> pair in
                    residentTokens)
                    currentBodyResidentCache.RecordDecodeFailure(pair.Value,
                        "SHARED SCHEDULER REJECTED DECODE");
                CompleteChunkLoadTracking(chunkId, ids);
                telemetry.DroppedRequests++;
            }
            return accepted;
        }

        void ScheduleResidentPopulationRead()
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || preloadDatabase == null ||
                currentBodyResidentCache == null || !currentBodyResidentCache.Active) return;
            int limit = ResolveConcurrentReadLimit();
            var keys = new List<AERISTerrainTileKey>(16);
            var tokens = new Dictionary<string, AERISResidentCommitToken>(
                StringComparer.Ordinal);
            string chunkId = string.Empty;
            lock (sync)
            {
                if (residentLoadsInFlight > 0 ||
                    diskLoadsInFlight + residentLoadsInFlight >= limit) return;
                while (residentPopulationCursor < residentPopulationPlan.Count)
                {
                    AERISTerrainTileKey seed =
                        residentPopulationPlan[residentPopulationCursor];
                    if ((int)seed.Lod >= residentPopulationBlockedFromLod ||
                        !preloadDatabase.Contains(seed))
                    {
                        residentPopulationCursor++;
                        continue;
                    }
                    AERISTerrainHeightTile residentTile;
                    AERISResidentCommitToken token;
                    if (currentBodyResidentCache.TryGetRamResident(seed,
                        out residentTile, out token) && residentTile != null)
                    {
                        residentPopulationCursor++;
                        continue;
                    }
                    if (!preloadDatabase.TryGetChunkId(seed, out chunkId) ||
                        string.IsNullOrEmpty(chunkId))
                    {
                        residentPopulationCursor++;
                        continue;
                    }
                    if (residentChunksLoading.Contains(chunkId)) return;

                    int cursor = residentPopulationCursor;
                    while (cursor < residentPopulationPlan.Count && keys.Count < 16)
                    {
                        AERISTerrainTileKey key = residentPopulationPlan[cursor];
                        if ((int)key.Lod >= residentPopulationBlockedFromLod)
                        {
                            cursor++;
                            continue;
                        }
                        string candidateChunk;
                        if (!preloadDatabase.TryGetChunkId(key, out candidateChunk) ||
                            !string.Equals(candidateChunk, chunkId,
                                StringComparison.Ordinal)) break;
                        residentPopulationCursor = cursor + 1;
                        cursor++;
                        bool alreadyResident;
                        if (!currentBodyResidentCache.TryPrepareSsdDecode(key,
                            currentBodyResidentCache.DatabaseGeneration, 0L,
                            out token, out alreadyResident) || alreadyResident) continue;
                        keys.Add(key);
                        tokens[key.StableId] = token;
                    }
                    if (keys.Count == 0) continue;
                    residentChunksLoading.Add(chunkId);
                    residentLoadsInFlight++;
                    break;
                }
            }
            if (keys.Count == 0 || string.IsNullOrEmpty(chunkId)) return;

            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.GeneralCompute,
                "terrain-resident-populate:" + AERISTerrainHash.Fnv1A64Hex(chunkId),
                runtime.CaptureStamp(), context =>
                {
                    var output = new Dictionary<string, AERISTerrainHeightTile>(
                        StringComparer.Ordinal);
                    preloadDatabase.TryLoadBatch(keys, warm, output, preloadTelemetry,
                        GameDataHash);
                    int blockedFromLod = 5;
                    int committed = 0;
                    foreach (AERISTerrainTileKey key in keys)
                    {
                        AERISResidentCommitToken token;
                        if (!tokens.TryGetValue(key.StableId, out token)) continue;
                        AERISTerrainHeightTile tile;
                        if (!output.TryGetValue(key.StableId, out tile) || tile == null)
                        {
                            currentBodyResidentCache.RecordDecodeFailure(token,
                                "CURRENT BODY POPULATION TILE MISSING");
                            continue;
                        }
                        tile.IsPreview = false;
                        tile.SamplingComplete = true;
                        tile.Source = AERISTerrainTileSource.PreloadDatabase;
                        AERISResidentCommitResult commitResult =
                            AERISResidentCommitResult.InvalidTransition;
                        if (currentBodyResidentCache.TryMarkDecoded(token) &&
                            currentBodyResidentCache.TryCommitRamResident(token, tile,
                                out commitResult))
                        {
                            committed++;
                            continue;
                        }
                        if (commitResult == AERISResidentCommitResult.BudgetRejected)
                        {
                            blockedFromLod = Math.Min(blockedFromLod, (int)key.Lod);
                            break;
                        }
                    }
                    return new int[] { blockedFromLod, committed };
                }, value =>
                {
                    int[] result = value as int[];
                    int blockedFromLod = result == null || result.Length == 0 ? 5 :
                        result[0];
                    int committed = result == null || result.Length < 2 ? 0 :
                        result[1];
                    lock (sync)
                    {
                        residentChunksLoading.Remove(chunkId);
                        residentLoadsInFlight = Math.Max(0, residentLoadsInFlight - 1);
                        if (blockedFromLod < residentPopulationBlockedFromLod)
                            residentPopulationBlockedFromLod = blockedFromLod;
                    }
                    if (blockedFromLod < 5)
                        status = "CURRENT BODY GLOBAL/FAR FOUNDATION BUDGET DEGRADED AT " +
                            ((AERISTerrainTileLod)blockedFromLod).ToString().ToUpperInvariant();
                    else if (committed > 0)
                        status = "CURRENT BODY GLOBAL/FAR FOUNDATION POPULATION ACTIVE";
                }, false);
            if (!accepted)
            {
                foreach (KeyValuePair<string, AERISResidentCommitToken> pair in tokens)
                    currentBodyResidentCache.RecordDecodeFailure(pair.Value,
                        "RESIDENT POPULATION SCHEDULER REJECTED");
                lock (sync)
                {
                    residentChunksLoading.Remove(chunkId);
                    residentLoadsInFlight = Math.Max(0, residentLoadsInFlight - 1);
                }
            }
        }

        bool CompleteChunkLoadTracking(string chunkId, IList<string> ids)
        {
            lock (sync)
            {
                bool tracked = preloadChunksLoading.Remove(chunkId);
                preloadChunkTileIds.Remove(chunkId);
                if (tracked) diskLoadsInFlight = Math.Max(0, diskLoadsInFlight - 1);
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        string id = ids[i];
                        diskLoading.Remove(id);
                        diskLoadingRequests.Remove(id);
                        diskLoadingSince.Remove(id);
                    }
                }
                preloadTelemetry.DatabaseReadQueueDepth = diskLoadsInFlight;
                return tracked;
            }
        }

        void TryScheduleLegacyLoad(AERISTerrainTileRequest request)
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || request == null || !disk.Contains(request.Key)) return;
            string id = request.Key.StableId;
            lock (sync)
            {
                if (diskLoading.Contains(id) || diskLoadsInFlight >= ResolveConcurrentReadLimit())
                    return;
                diskLoading.Add(id);
                diskLoadingRequests[id] = request;
                diskLoadingSince[id] = Time.realtimeSinceStartup;
                diskLoadsInFlight++;
            }
            AERISTerrainTileKey key = request.Key;
            bool accepted = runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute,
                "terrain-legacy-migrate:" + key.FileStem, runtime.CaptureStamp(), context =>
                {
                    AERISTerrainHeightTile loaded;
                    return disk.TryLoad(key, out loaded) ? loaded : null;
                }, value =>
                {
                    AERISTerrainHeightTile loaded = value as AERISTerrainHeightTile;
                    lock (sync)
                    {
                        bool tracked = diskLoading.Remove(id);
                        diskLoadingRequests.Remove(id);
                        diskLoadingSince.Remove(id);
                        if (tracked) diskLoadsInFlight = Math.Max(0, diskLoadsInFlight - 1);
                        if (loaded != null) queued.Remove(id);
                        preloadTelemetry.DatabaseReadQueueDepth = diskLoadsInFlight;
                    }
                    if (loaded != null && IsFlightRequestCurrent(request))
                    {
                        loaded.SamplingComplete = true;
                        loaded.Source = AERISTerrainTileSource.LegacyMigration;
                        loaded.PqsConfigurationHash = environmentHash;
                        loaded.GameDataHash = GameDataHash;
                        loaded.TerrainGenerationId = preloadDatabase.DatabaseGeneration;
                        ram.Put(loaded);
                        telemetry.DiskHits++;
                        terrainGeneration++;
                        lastTerrainResultRealtime = Time.realtimeSinceStartup;
                        ScheduleDiskWrite(loaded.CloneImmutable());
                        status = "LEGACY TERRAIN MIGRATED";
                    }
                    else if (loaded != null)
                    {
                        telemetry.StaleCancelled++;
                        preloadTelemetry.StaleResultsDiscarded++;
                    }
                }, false);
            if (!accepted)
            {
                lock (sync)
                {
                    bool tracked = diskLoading.Remove(id);
                    diskLoadingRequests.Remove(id);
                    diskLoadingSince.Remove(id);
                    if (tracked) diskLoadsInFlight = Math.Max(0, diskLoadsInFlight - 1);
                    preloadTelemetry.DatabaseReadQueueDepth = diskLoadsInFlight;
                }
            }
        }

        int ResolveConcurrentReadLimit()
        {
            int profileLimit = performance == null ? 2 :
                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo);
            double latency = preloadTelemetry.DatabaseReadLatencyMilliseconds;
            int adaptive = latency > 25.0 ? 1 : latency > 10.0 ? 2 : latency > 4.0 ? 3 : 6;
            return Math.Max(1, Math.Min(profileLimit, adaptive));
        }

        void RecoverAbandonedIo(float now)
        {
            const float timeoutSeconds = 45f;
            var staleChunks = new List<string>();
            lock (sync)
            {
                foreach (KeyValuePair<string, List<string>> pair in preloadChunkTileIds)
                {
                    bool stale = false;
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        float since;
                        if (diskLoadingSince.TryGetValue(pair.Value[i], out since) &&
                            now - since >= timeoutSeconds) { stale = true; break; }
                    }
                    if (stale) staleChunks.Add(pair.Key);
                }
            }
            for (int i = 0; i < staleChunks.Count; i++)
            {
                string chunk = staleChunks[i];
                List<string> ids = null;
                lock (sync)
                {
                    List<string> tracked;
                    if (preloadChunkTileIds.TryGetValue(chunk, out tracked))
                        ids = new List<string>(tracked);
                }
                CompleteChunkLoadTracking(chunk, ids);
                telemetry.StaleCancelled++;
                preloadTelemetry.StaleResultsDiscarded++;
            }
            lock (sync)
            {
                if (diskWritingSince.Count == 0) return;
                cancellationScratch.Clear();
                foreach (KeyValuePair<string, float> pair in diskWritingSince)
                    if (now - pair.Value >= timeoutSeconds)
                        cancellationScratch.Add(pair.Key);
                for (int i = 0; i < cancellationScratch.Count; i++)
                {
                    string id = cancellationScratch[i];
                    diskWritingSince.Remove(id);
                    diskWriting.Remove(id);
                    if (diskWritePending.ContainsKey(id))
                        diskWriteRetryAfter[id] = now + 1f;
                    telemetry.StaleCancelled++;
                }
                cancellationScratch.Clear();
            }
        }

        void StartNextRequestIfNeeded()
        {
            int admitted = 0;
            int maximumActive = performance == null ? 8 :
                Math.Max(4, Math.Min(16,
                    performance.ActiveProfile.MaximumTerrainTileRequests / 2));
            while (blockPipeline.Count < maximumActive && admitted < 4)
            {
                AERISTerrainTileRequest next = null;
                lock (sync)
                {
                    foreach (AERISTerrainTileRequest request in queued.Values)
                    {
                        if (request == null || diskLoading.Contains(request.Key.StableId) ||
                            !desiredRequestIds.Contains(request.Key.StableId)) continue;
                        if (next == null || CompareRequests(request, next) < 0) next = request;
                    }
                }
                if (next == null) return;
                if (preloadDatabase.Contains(next.Key))
                {
                    SchedulePreloadReads();
                    return;
                }
                if (disk.Contains(next.Key))
                {
                    TryScheduleLegacyLoad(next);
                    return;
                }
                string id = next.Key.StableId;
                if (!IsFlightRequestCurrent(next))
                {
                    lock (sync) queued.Remove(id);
                    telemetry.StaleCancelled++;
                    telemetry.ObsoleteCancelled++;
                    continue;
                }
                AERISTerrainHeightTile existing;
                if (ram.TryGet(next.Key, out existing) && existing != null)
                {
                    if (ReconcileRequestWithRamTile(next, existing))
                    {
                        lock (sync) queued.Remove(id);
                        telemetry.Reused++;
                        continue;
                    }
                }
                bool accepted = blockPipeline.Enqueue(activeBody, next,
                    AERISTerrainTileSource.RealtimeGenerated, environmentHash,
                    GameDataHash, preloadDatabase.DatabaseGeneration,
                    IsFlightRequestCurrent,
                    (committedRequest, tile, complete) =>
                        CommitFlightBlock(committedRequest, tile, complete));
                if (!accepted)
                {
                    telemetry.DroppedRequests++;
                    return;
                }
                lock (sync) queued.Remove(id);
                admitted++;
                status = next.Stage == AERISTerrainSamplingStage.Preview ?
                    "TERRAIN BLOCK PREVIEW" : "TERRAIN BLOCK DETAIL";
            }
        }

        void CommitFlightBlock(AERISTerrainTileRequest request,
            AERISTerrainHeightTile tile, bool requestComplete)
        {
            if (request == null || tile == null) return;
            if (!IsFlightRequestCurrent(request))
            {
                telemetry.StaleCancelled++;
                preloadTelemetry.StaleResultsDiscarded++;
                return;
            }
            tile.Source = AERISTerrainTileSource.RealtimeGenerated;
            tile.PqsConfigurationHash = environmentHash;
            tile.GameDataHash = GameDataHash;
            tile.TerrainGenerationId = preloadDatabase.DatabaseGeneration;
            ram.Put(tile);
            telemetry.Generated++;
            terrainGeneration++;
            lastTerrainResultRealtime = Time.realtimeSinceStartup;
            if (!requestComplete)
            {
                status = "TERRAIN BLOCK PROGRESSIVE COMMIT " + tile.Quality + "%";
                return;
            }
            if (request.Stage == AERISTerrainSamplingStage.Preview && tile.IsPreview)
            {
                telemetry.PreviewGenerated++;
                status = "TERRAIN PREVIEW AVAILABLE";
                if (desiredRequestIds.Contains(request.Key.StableId))
                    EnsureRequest(CloneForFinal(request));
                return;
            }
            tile.IsPreview = false;
            telemetry.FinalGenerated++;
            status = tile.Key.Lod >= AERISTerrainTileLod.Local ?
                "LOCAL TERRAIN AVAILABLE" : "GLOBAL TERRAIN AVAILABLE";
            ScheduleDiskWrite(tile.CloneImmutable());
        }

        bool IsFlightRequestCurrent(AERISTerrainTileRequest request)
        {
            if (request == null || request.WorkOwner !=
                AERISTerrainWorkOwner.FlightFallback) return false;
            return flightViewportActive && HighLogic.LoadedSceneIsFlight &&
                request.BodyGeneration == bodyGeneration &&
                request.VesselGeneration == CurrentVesselGeneration() &&
                request.TerrainGeneration == terrainRequestGeneration &&
                request.ViewGeneration == viewGeneration &&
                request.RangeGeneration == rangeGeneration &&
                request.PlanGeneration == planGeneration &&
                request.DatabaseGeneration == preloadDatabase.RequestGeneration &&
                string.Equals(request.Key.BodyName, activeBodyName,
                    StringComparison.Ordinal) &&
                string.Equals(request.Key.EnvironmentHash, environmentHash,
                    StringComparison.Ordinal) &&
                desiredRequestIds.Contains(request.Key.StableId);
        }

        static AERISTerrainTileRequest CloneForFinal(AERISTerrainTileRequest source)
        {
            return new AERISTerrainTileRequest
            {
                Key = source.Key,
                Priority = source.Priority,
                Lane = source.Lane,
                Stage = AERISTerrainSamplingStage.Final,
                CenterLatitudeDeg = source.CenterLatitudeDeg,
                CenterLongitudeDeg = source.CenterLongitudeDeg,
                SouthLatitudeDeg = source.SouthLatitudeDeg,
                NorthLatitudeDeg = source.NorthLatitudeDeg,
                WestLongitudeDeg = source.WestLongitudeDeg,
                EastLongitudeDeg = source.EastLongitudeDeg,
                Resolution = source.FinalResolution,
                FinalResolution = source.FinalResolution,
                ViewDistanceMeters = source.ViewDistanceMeters,
                RequestSequence = source.RequestSequence,
                BodyGeneration = source.BodyGeneration,
                VesselGeneration = source.VesselGeneration,
                TerrainGeneration = source.TerrainGeneration,
                ViewGeneration = source.ViewGeneration,
                RangeGeneration = source.RangeGeneration,
                PlanGeneration = source.PlanGeneration,
                DatabaseGeneration = source.DatabaseGeneration,
                ReadLane = source.ReadLane,
                WorkOwner = source.WorkOwner,
                Visible = source.Visible
            };
        }

        void ScheduleDiskWrite(AERISTerrainHeightTile tile)
        {
            if (tile == null) return;
            string id = tile.Key.StableId;
            float now = Time.realtimeSinceStartup;
            lock (sync)
            {
                diskWritePending[id] = tile;
                diskWriteRetryAfter[id] = now;
                if (!diskWriteAttempts.ContainsKey(id)) diskWriteAttempts[id] = 0;
            }
            TrySubmitDiskWrite(id, now);
        }

        void RetryPendingDiskWrites(float now)
        {
            diskWriteReadyScratch.Clear();
            int available = 0;
            lock (sync)
            {
                int limit = performance == null ? 1 :
                    Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo);
                int writeLimit = ResolveWriteIoLimitLocked(limit);
                available = Math.Max(0, writeLimit - diskWriting.Count);
                if (available <= 0) return;
                foreach (KeyValuePair<string, AERISTerrainHeightTile> pair in diskWritePending)
                {
                    if (diskWriting.Contains(pair.Key)) continue;
                    float retryAt;
                    if (diskWriteRetryAfter.TryGetValue(pair.Key, out retryAt) &&
                        now < retryAt) continue;
                    diskWriteReadyScratch.Add(pair.Key);
                }
            }
            diskWriteReadyScratch.Sort(StringComparer.Ordinal);
            int count = Math.Min(available, diskWriteReadyScratch.Count);
            for (int i = 0; i < count; i++)
                TrySubmitDiskWrite(diskWriteReadyScratch[i], now);
        }

        int ResolveWriteIoLimitLocked(int totalIoLimit)
        {
            int limit = Math.Max(1, totalIoLimit);
            bool readsPending = diskLoadsInFlight > 0 || queued.Count > 0 ||
                diskLoading.Count > 0 || preloadChunksLoading.Count > 0;
            if (!readsPending) return limit;
            return Math.Max(0, limit - 1);
        }

        void TrySubmitDiskWrite(string id, float now)
        {
            if (string.IsNullOrEmpty(id)) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            AERISTerrainHeightTile payload;
            lock (sync)
            {
                int limit = performance == null ? 1 :
                    Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo);
                // Writes never consume the last I/O slot while Flight reads are pending.
                int writeLimit = ResolveWriteIoLimitLocked(limit);
                if (writeLimit <= 0 || diskWriting.Count >= writeLimit ||
                    diskWriting.Contains(id) ||
                    !diskWritePending.TryGetValue(id, out payload) || payload == null) return;
                float retryAt;
                if (diskWriteRetryAfter.TryGetValue(id, out retryAt) && now < retryAt) return;
                diskWriting.Add(id);
                diskWritingSince[id] = now;
                int attempts;
                diskWriteAttempts.TryGetValue(id, out attempts);
                diskWriteAttempts[id] = attempts + 1;
            }

            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.ArchiveCompression,
                "terrain-preload-write:" + payload.Key.FileStem,
                runtime.CaptureStamp(), context =>
                {
                    long bytes;
                    double ratio;
                    bool ok = preloadDatabase.Save(payload,
                        string.IsNullOrEmpty(payload.PqsConfigurationHash) ?
                            payload.Key.EnvironmentHash : payload.PqsConfigurationHash,
                        string.IsNullOrEmpty(payload.GameDataHash) ? GameDataHash :
                            payload.GameDataHash,
                        payload.TerrainGenerationId <= 0L ?
                            preloadDatabase.DatabaseGeneration : payload.TerrainGenerationId,
                        AERISTerrainCodecId.Deflate, out bytes, out ratio);
                    return new object[] { ok, bytes, ratio };
                }, value =>
                {
                    object[] result = value as object[];
                    bool ok = result != null && result.Length >= 3 && (bool)result[0];
                    float callbackNow = Time.realtimeSinceStartup;
                    lock (sync)
                    {
                        diskWriting.Remove(id);
                        diskWritingSince.Remove(id);
                        AERISTerrainHeightTile newest;
                        bool hasNewest = diskWritePending.TryGetValue(id, out newest) &&
                            newest != null;
                        bool samePayload = hasNewest &&
                            newest.CreatedUtcTicks == payload.CreatedUtcTicks;
                        if (ok && samePayload)
                        {
                            diskWritePending.Remove(id);
                            diskWriteRetryAfter.Remove(id);
                            diskWriteAttempts.Remove(id);
                        }
                        else if (ok && hasNewest)
                        {
                            diskWriteRetryAfter[id] = callbackNow;
                            diskWriteAttempts[id] = 0;
                        }
                        else if (!ok)
                        {
                            int attempts;
                            diskWriteAttempts.TryGetValue(id, out attempts);
                            if (!hasNewest || attempts >= 5)
                            {
                                diskWritePending.Remove(id);
                                diskWriteRetryAfter.Remove(id);
                                diskWriteAttempts.Remove(id);
                            }
                            else diskWriteRetryAfter[id] = callbackNow +
                                Math.Min(30f, 2f * Math.Max(1, attempts));
                        }
                    }
                    if (ok)
                    {
                        telemetry.DiskWrites++;
                        status = "PRELOAD TERRAIN DATABASE COMMIT";
                    }
                    else telemetry.DiskFailures++;
                }, false);
            if (!accepted)
            {
                lock (sync)
                {
                    diskWriting.Remove(id);
                    diskWritingSince.Remove(id);
                    diskWriteRetryAfter[id] = now + 1f;
                    int attempts;
                    diskWriteAttempts.TryGetValue(id, out attempts);
                    diskWriteAttempts[id] = Math.Max(0, attempts - 1);
                }
                telemetry.DroppedRequests++;
            }
        }


        void RefreshTerrainRequestGeneration()
        {
            int profileRevision = performance == null ? 0 : performance.ProfileRevision;
            if (profileRevision == lastPerformanceProfileRevision) return;
            lastPerformanceProfileRevision = profileRevision;
            // Profile changes alter request budgets and desired LOD, not the meaning of
            // an already sampled body-fixed height tile. Replan and reconcile instead of
            // invalidating overlapping work. Display mode is a GPU palette selection and
            // deliberately does not enter terrain request generations at all.
            if (nextPlanRealtime - Time.realtimeSinceStartup > 0.05f)
                nextPlanRealtime = Time.realtimeSinceStartup + 0.05f;
        }

        void UpdateDisplayView(double latitudeDeg, double longitudeDeg,
            double rangeMeters, double headingDeg, bool trackUp, float anchorGuiV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (!IsFinite(latitudeDeg) || !IsFinite(longitudeDeg) ||
                !IsFinite(rangeMeters) || rangeMeters <= 0.0) return;
            double normalizedLatitude = Math.Max(-90.0, Math.Min(90.0, latitudeDeg));
            double normalizedLongitude = NormalizeLongitude(longitudeDeg);
            // Internal presentation planning may deliberately use a non-UI overscan
            // range (Gate 4B temporal history surface). Do not snap it back to the
            // 5/10/20/40/80/160 km user selector steps. The public UI range remains
            // normalized before it reaches the renderer; this value is planner-only.
            double normalizedRange = Math.Max(1000.0, Math.Min(250000.0, rangeMeters));
            double normalizedHeading = NormalizeHeading(headingDeg);
            float normalizedAnchor = Mathf.Clamp01(anchorGuiV);
            bool rangeChanged = !displayViewValid ||
                Math.Abs(displayViewRangeMeters - normalizedRange) > 0.5;
            double centerMovement = !displayViewValid ? double.MaxValue :
                GreatCircleDistanceMeters(activeBody, displayViewLatitudeDeg,
                    displayViewLongitudeDeg, normalizedLatitude, normalizedLongitude);
            bool centerChanged = !displayViewValid || centerMovement >
                Math.Max(100.0, normalizedRange * 0.02);
            bool orientationChanged = !displayViewValid ||
                displayViewTrackUp != trackUp || displayViewOrientation != orientation ||
                Math.Abs(displayViewAnchorGuiV - normalizedAnchor) > 0.001f;
            bool headingChanged = !displayViewValid || (trackUp &&
                Math.Abs(DeltaAngle(displayViewHeadingDeg, normalizedHeading)) > 3.0);
            bool materiallyChanged = rangeChanged || centerChanged ||
                orientationChanged || headingChanged;
            displayViewValid = true;
            displayViewLatitudeDeg = normalizedLatitude;
            displayViewLongitudeDeg = normalizedLongitude;
            displayViewRangeMeters = normalizedRange;
            displayViewHeadingDeg = normalizedHeading;
            displayViewTrackUp = trackUp;
            displayViewAnchorGuiV = normalizedAnchor;
            displayViewOrientation = orientation;
            if (rangeChanged) rangeGeneration++;
            if (centerChanged || orientationChanged || headingChanged) planGeneration++;
            if (materiallyChanged)
            {
                viewGeneration++;
                firstViewRequestRealtime = Time.realtimeSinceStartup;
                preloadTelemetry.FirstTileVisibleMilliseconds = 0.0;
                if (nextPlanRealtime - Time.realtimeSinceStartup > 0.05f)
                    nextPlanRealtime = Time.realtimeSinceStartup + 0.05f;
            }
        }

        internal AERISTerrainVisibleTileSet CaptureVisible(double centerLatitudeDeg,
            double centerLongitudeDeg, double rangeMeters, double headingDeg,
            bool trackUp, float anchorGuiV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (!flightViewportActive)
                return new AERISTerrainVisibleTileSet
                {
                    ViewGeneration = viewGeneration,
                    TerrainGeneration = terrainGeneration,
                    BodyName = activeBodyName,
                    BodyRadiusMeters = activeBody == null ? 0.0 : activeBody.Radius,
                    CenterLatitudeDeg = centerLatitudeDeg,
                    CenterLongitudeDeg = centerLongitudeDeg,
                    RangeMeters = rangeMeters,
                    Tiles = new AERISTerrainHeightTile[0],
                    RequestedCount = 0,
                    MissingCount = 0,
                    FoundationRequestedCount = 0,
                    FoundationMissingCount = 0,
                    GlobalFoundationCount = 0,
                    FarFoundationCount = 0,
                    FoundationComplete = false,
                    GlobalFallbackAvailable = false,
                    Status = "FLIGHT TERRAIN VIEWPORT INACTIVE"
                };
            UpdateDisplayView(centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                headingDeg, trackUp, anchorGuiV, orientation);
            visibleTiles.Clear();
            int missing = 0;
            int foundationMissing = 0;
            double availableCoverage = 0.0;
            bool global = false;
            for (int i = 0; i < visibleKeys.Count; i++)
            {
                AERISTerrainTileKey key = visibleKeys[i];
                bool foundationKey = visibleFoundationIds.Contains(key.StableId);
                AERISTerrainHeightTile tile;
                if (ram.TryGet(key, out tile) && tile != null)
                {
                    visibleTiles.Add(tile);
                    if (foundationKey)
                        availableCoverage += Math.Max(0.0, Math.Min(1.0,
                            tile.Quality / 100.0));
                    if (tile.Key.Lod == AERISTerrainTileLod.Global) global = true;
                }
                else
                {
                    missing++;
                    if (foundationKey) foundationMissing++;
                }
            }
            visibleTiles.Sort((a, b) => ((int)a.Key.Lod).CompareTo((int)b.Key.Lod));
            int foundationRequested = visibleFoundationIds.Count;
            preloadTelemetry.ViewportCoverageRatio = foundationRequested <= 0 ? 0.0 :
                Math.Max(0.0, Math.Min(1.0,
                    availableCoverage / foundationRequested));
            lastFoundationRequestedCount = foundationRequested;
            lastFoundationMissingCount = foundationMissing;
            float now = Time.realtimeSinceStartup;
            if (visibleTiles.Count > 0 && firstViewRequestRealtime > 0f &&
                preloadTelemetry.FirstTileVisibleMilliseconds <= 0.0)
                preloadTelemetry.FirstTileVisibleMilliseconds =
                    Math.Max(0.0, (now - firstViewRequestRealtime) * 1000.0);
            preloadTelemetry.ResultAgeMilliseconds = lastTerrainResultRealtime <= 0f ? 0.0 :
                Math.Max(0.0, (now - lastTerrainResultRealtime) * 1000.0);
            bool fallback = foundationMissing > 0 ||
                availableCoverage + 0.001 < foundationRequested;
            if (fallback && !fallbackActive) preloadTelemetry.GenerationFallbackCount++;
            fallbackActive = fallback;
            return new AERISTerrainVisibleTileSet
            {
                ViewGeneration = viewGeneration,
                TerrainGeneration = terrainGeneration,
                BodyName = activeBodyName,
                BodyRadiusMeters = activeBody == null ? 0.0 : activeBody.Radius,
                CenterLatitudeDeg = centerLatitudeDeg,
                CenterLongitudeDeg = centerLongitudeDeg,
                RangeMeters = rangeMeters,
                Tiles = visibleTiles.ToArray(),
                RequestedCount = visibleKeys.Count,
                MissingCount = missing,
                FoundationRequestedCount = foundationRequested,
                FoundationMissingCount = foundationMissing,
                GlobalFoundationCount = lastFoundationGlobalCount,
                FarFoundationCount = lastFoundationFarCount,
                FoundationComplete = foundationRequested > 0 && foundationMissing == 0 &&
                    availableCoverage + 0.001 >= foundationRequested,
                GlobalFallbackAvailable = global,
                Status = status
            };
        }

        void PublishPreloadTelemetry()
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
            {
                runtime.RecordTerrainPreloadState(preloadTelemetry,
                    warm == null ? 0L : warm.UsedBytes,
                    warm == null ? 0 : warm.Count);
                float now = Time.realtimeSinceStartup;
                if (now >= nextCp3TelemetrySampleRealtime)
                {
                    nextCp3TelemetrySampleRealtime = now + 1f;
                    cp3TelemetryResidentSnapshot = currentBodyResidentCache == null ?
                        null : currentBodyResidentCache.SnapshotTelemetry();
                    cp3TelemetryCorridorSnapshot = predictiveCorridor == null ?
                        null : predictiveCorridor.Snapshot();
                    runtime.RecordCp3ResidentCorridorState(
                        cp3TelemetryResidentSnapshot, cp3TelemetryCorridorSnapshot);
                }
                if (now >= nextCp3TelemetryLogRealtime)
                {
                    nextCp3TelemetryLogRealtime = now + 10f;
                    AERISCurrentBodyResidentTelemetrySnapshot resident =
                        cp3TelemetryResidentSnapshot;
                    AERISPredictiveForwardCorridorSnapshot corridor =
                        cp3TelemetryCorridorSnapshot;
                    AERISLogger.Info("[CP3_TELEMETRY] body=" +
                        (resident == null ? string.Empty : resident.ActiveBody) +
                        "; ram=" + (resident == null ? 0L : resident.RamBytes) + "/" +
                        (resident == null ? 0L : resident.RamBudgetBytes) +
                        "; lod_gfrll=" + (resident == null ? 0 : resident.GlobalCount) +
                        "/" + (resident == null ? 0 : resident.FarCount) +
                        "/" + (resident == null ? 0 : resident.RouteCount) +
                        "/" + (resident == null ? 0 : resident.LocalCount) +
                        "/" + (resident == null ? 0 : resident.LandCount) +
                        "; decode=" + (resident == null ? 0L :
                            resident.AsyncDecodeSuccesses) + "/" +
                        (resident == null ? 0L : resident.AsyncDecodeSubmissions) +
                        "; corridor=" + (corridor == null ? "INACTIVE" :
                            corridor.Status) + "; req_pin=" +
                        (corridor == null ? 0 : corridor.RequestedTiles) + "/" +
                        (corridor == null ? 0 : corridor.PinnedTiles) +
                        "; land=" + (corridor != null && corridor.LandDemandActive ?
                            "DEMAND" : "OFF") + "; foundation_gf=" +
                        lastFoundationGlobalCount + "/" + lastFoundationFarCount +
                        "; foundation_missing=" + lastFoundationMissingCount + "/" +
                        lastFoundationRequestedCount + ".");
                }
            }
        }

        internal AERISTerrainTileCacheTelemetry SnapshotTelemetry()
        {
            UpdateTelemetry();
            return new AERISTerrainTileCacheTelemetry
            {
                RamBytes = telemetry.RamBytes,
                RamLimitBytes = telemetry.RamLimitBytes,
                DiskBytes = telemetry.DiskBytes,
                DiskLimitBytes = telemetry.DiskLimitBytes,
                RamTileCount = telemetry.RamTileCount,
                DiskTileCount = telemetry.DiskTileCount,
                RamHits = telemetry.RamHits,
                DiskHits = telemetry.DiskHits,
                Misses = telemetry.Misses,
                Reused = telemetry.Reused,
                Generated = telemetry.Generated,
                DiskWrites = telemetry.DiskWrites,
                DiskFailures = telemetry.DiskFailures,
                StaleCancelled = telemetry.StaleCancelled,
                DroppedRequests = telemetry.DroppedRequests,
                PreviewGenerated = telemetry.PreviewGenerated,
                FinalGenerated = telemetry.FinalGenerated,
                ObsoleteCancelled = telemetry.ObsoleteCancelled,
                PendingRequests = telemetry.PendingRequests,
                DesiredRequests = telemetry.DesiredRequests,
                VisibleRequests = telemetry.VisibleRequests,
                PreviewTileCount = telemetry.PreviewTileCount,
                SamplingRemaining = telemetry.SamplingRemaining,
                LastSamplingBatchSamples = telemetry.LastSamplingBatchSamples,
                LastSamplingBatchMilliseconds = telemetry.LastSamplingBatchMilliseconds,
                WarmBytes = telemetry.WarmBytes,
                WarmTileCount = telemetry.WarmTileCount,
                Preload = ClonePreloadTelemetry(telemetry.Preload)
            };
        }

        void UpdateTelemetry()
        {
            telemetry.RamBytes = ram.UsedBytes;
            telemetry.RamLimitBytes = ram.LimitBytes;
            telemetry.RamTileCount = ram.Count;
            telemetry.DiskBytes = preloadDatabase.UsedBytes;
            telemetry.DiskLimitBytes = preloadDatabase.LimitBytes;
            telemetry.DiskTileCount = preloadDatabase.Count;
            telemetry.WarmBytes = warm.UsedBytes;
            telemetry.WarmTileCount = warm.Count;
            telemetry.Preload = ClonePreloadTelemetry(preloadTelemetry);
            lock (sync)
            {
                // A disk-load request remains in queued until its commit callback. Count
                // unique tile work rather than double-counting the same ID in both sets.
                telemetry.PendingRequests = queued.Count + blockPipeline.Count;
                telemetry.DesiredRequests = desiredRequestIds.Count;
                telemetry.VisibleRequests = desiredVisibleIds.Count;
            }
            telemetry.PreviewTileCount = ram.CountPreviewTiles(desiredRequestIds);
            telemetry.SamplingRemaining = blockPipeline.Count;
            telemetry.LastSamplingBatchSamples = Math.Max(0, lastSamplingBatchSamples);
            telemetry.LastSamplingBatchMilliseconds = Math.Max(0.0,
                lastSamplingBatchMilliseconds);
        }

        static AERISTerrainPreloadTelemetry ClonePreloadTelemetry(
            AERISTerrainPreloadTelemetry source)
        {
            if (source == null) return new AERISTerrainPreloadTelemetry();
            return new AERISTerrainPreloadTelemetry
            {
                BuilderBody = source.BuilderBody,
                BuilderLod = source.BuilderLod,
                BuilderTilesComplete = source.BuilderTilesComplete,
                BuilderTilesPending = source.BuilderTilesPending,
                BuilderPqsMilliseconds = source.BuilderPqsMilliseconds,
                BuilderWorkerUtilization = source.BuilderWorkerUtilization,
                BuilderWriteMbps = source.BuilderWriteMbps,
                BuilderCompressionRatio = source.BuilderCompressionRatio,
                BuilderStorageBytes = source.BuilderStorageBytes,
                BuilderPqsSamplesPerSecond = source.BuilderPqsSamplesPerSecond,
                BuilderPqsSampleCostMilliseconds =
                    source.BuilderPqsSampleCostMilliseconds,
                BuilderPqsSampleCacheHits = source.BuilderPqsSampleCacheHits,
                BuilderPqsSampleCacheMisses = source.BuilderPqsSampleCacheMisses,
                BuilderPqsSampleCacheHitRatio =
                    source.BuilderPqsSampleCacheHitRatio,
                BuilderChunkBatchTiles = source.BuilderChunkBatchTiles,
                BuilderChunkRewriteAmplification =
                    source.BuilderChunkRewriteAmplification,
                BuilderChunkFlushMilliseconds = source.BuilderChunkFlushMilliseconds,
                BuilderIntermediateCommitsSkipped =
                    source.BuilderIntermediateCommitsSkipped,
                DatabaseReadRequests = source.DatabaseReadRequests,
                DatabaseReadLatencyMilliseconds = source.DatabaseReadLatencyMilliseconds,
                DatabaseReadMbps = source.DatabaseReadMbps,
                DatabaseReadQueueDepth = source.DatabaseReadQueueDepth,
                DatabaseCacheHitRatio = source.DatabaseCacheHitRatio,
                DatabaseCoalescedReads = source.DatabaseCoalescedReads,
                DatabaseCrcFailures = source.DatabaseCrcFailures,
                DatabaseHashMismatches = source.DatabaseHashMismatches,
                DatabaseParsedChunkCacheHits =
                    source.DatabaseParsedChunkCacheHits,
                DatabaseParsedChunkCacheMisses =
                    source.DatabaseParsedChunkCacheMisses,
                DatabaseParsedChunkCacheHitRatio =
                    source.DatabaseParsedChunkCacheHitRatio,
                DecompressQueueDelayMilliseconds = source.DecompressQueueDelayMilliseconds,
                DecompressTimeMilliseconds = source.DecompressTimeMilliseconds,
                DecompressMbps = source.DecompressMbps,
                DecompressWorkerActive = source.DecompressWorkerActive,
                DecompressFailures = source.DecompressFailures,
                FirstTileVisibleMilliseconds = source.FirstTileVisibleMilliseconds,
                ViewportCoverageRatio = source.ViewportCoverageRatio,
                ResultAgeMilliseconds = source.ResultAgeMilliseconds,
                StaleResultsDiscarded = source.StaleResultsDiscarded,
                GenerationFallbackCount = source.GenerationFallbackCount
            };
        }

        static double UpdateEma(double previous, double sample, double weight)
        {
            if (previous <= 0.0) return sample;
            return previous + Math.Max(0.01, Math.Min(1.0, weight)) *
                (sample - previous);
        }

        string FindLowestPriorityRequestLocked()
        {
            AERISTerrainTileRequest worst = null;
            string worstId = string.Empty;
            foreach (KeyValuePair<string, AERISTerrainTileRequest> pair in queued)
            {
                if (pair.Value == null) continue;
                if (worst == null || CompareRequests(pair.Value, worst) > 0)
                {
                    worst = pair.Value;
                    worstId = pair.Key;
                }
            }
            return worstId;
        }

        static AERISTerrainReadLane MapReadLane(AERISTerrainRequestLane lane,
            AERISTerrainTilePriority priority)
        {
            if (lane == AERISTerrainRequestLane.Viewport ||
                priority == AERISTerrainTilePriority.Critical)
                return AERISTerrainReadLane.Critical;
            if (lane == AERISTerrainRequestLane.Landing ||
                priority == AERISTerrainTilePriority.High)
                return AERISTerrainReadLane.High;
            if (lane == AERISTerrainRequestLane.LookAhead)
                return AERISTerrainReadLane.Prefetch;
            return lane == AERISTerrainRequestLane.Background ?
                AERISTerrainReadLane.Background : AERISTerrainReadLane.Normal;
        }

        static bool IsBackgroundPopulationLod(AERISTerrainTileLod lod)
        {
            // Gate 3.1 keeps only the coarse authoritative base in the current-body
            // sweep. Route/Local become reconstructed quality levels and are admitted
            // only as existing exact bridge payloads or demand-gated LAND microtiles.
            return lod == AERISTerrainTileLod.Global ||
                lod == AERISTerrainTileLod.Far;
        }

        static bool IsGate3ResidentLod(AERISTerrainTileLod lod)
        {
            return lod == AERISTerrainTileLod.Global ||
                lod == AERISTerrainTileLod.Far ||
                lod == AERISTerrainTileLod.Route ||
                lod == AERISTerrainTileLod.Local ||
                lod == AERISTerrainTileLod.Land;
        }

        static int CompareRequests(AERISTerrainTileRequest a, AERISTerrainTileRequest b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int readLane = ((int)a.ReadLane).CompareTo((int)b.ReadLane);
            if (readLane != 0) return readLane;
            int lane = ((int)a.Lane).CompareTo((int)b.Lane);
            if (lane != 0) return lane;
            int visible = (b.Visible ? 1 : 0).CompareTo(a.Visible ? 1 : 0);
            if (visible != 0) return visible;
            // Every visible preview is supplied before any final refinement. This keeps
            // the GPU fed continuously and prevents one 33x33 tile monopolising PQS.
            int stage = ((int)a.Stage).CompareTo((int)b.Stage);
            if (stage != 0) return stage;
            int priority = ((int)b.Priority).CompareTo((int)a.Priority);
            if (priority != 0) return priority;
            int lod = ((int)a.Key.Lod).CompareTo((int)b.Key.Lod);
            if (lod != 0) return lod;
            int distance = a.ViewDistanceMeters.CompareTo(b.ViewDistanceMeters);
            if (distance != 0) return distance;
            return b.RequestSequence.CompareTo(a.RequestSequence);
        }

        static int ResolvePreviewResolution(AERISTerrainTileLod lod,
            int finalResolution)
        {
            int desired;
            switch (lod)
            {
                case AERISTerrainTileLod.Global: desired = 5; break;
                case AERISTerrainTileLod.Far:
                case AERISTerrainTileLod.Route: desired = 7; break;
                default: desired = 9; break;
            }
            desired = Math.Max(3, Math.Min(finalResolution, desired));
            if ((desired & 1) == 0) desired--;
            return Math.Max(3, desired);
        }

        static double NormalizeHeading(double value)
        {
            if (!IsFinite(value)) return 0.0;
            value %= 360.0;
            return value < 0.0 ? value + 360.0 : value;
        }

        static double DeltaAngle(double fromDeg, double toDeg)
        {
            double delta = NormalizeHeading(toDeg) - NormalizeHeading(fromDeg);
            if (delta > 180.0) delta -= 360.0;
            if (delta < -180.0) delta += 360.0;
            return delta;
        }

        static double GreatCircleDistanceMeters(CelestialBody body,
            double latitudeA, double longitudeA, double latitudeB, double longitudeB)
        {
            double radius = Math.Max(1000.0, body == null ? 600000.0 : body.Radius);
            double lat1 = latitudeA * Math.PI / 180.0;
            double lat2 = latitudeB * Math.PI / 180.0;
            double dLat = lat2 - lat1;
            double dLon = NormalizeLongitude(longitudeB - longitudeA) * Math.PI / 180.0;
            double sinLat = Math.Sin(dLat * 0.5);
            double sinLon = Math.Sin(dLon * 0.5);
            double a = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) *
                sinLon * sinLon;
            double angle = 2.0 * Math.Atan2(Math.Sqrt(Math.Max(0.0, a)),
                Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            return radius * angle;
        }

        static AERISTerrainTileLod ResolveNearLod(double range,
            AERISTerrainPerformanceProfile profile)
        {
            AERISTerrainTileLod maximum = profile == null ? AERISTerrainTileLod.Local :
                profile.MaximumNormalLod;
            AERISTerrainTileLod desired = range <= 10000.0 ? AERISTerrainTileLod.Local :
                range <= 40000.0 ? AERISTerrainTileLod.Route : AERISTerrainTileLod.Far;
            return desired > maximum ? maximum : desired;
        }

        internal static AERISTerrainTileKey KeyForPoint(CelestialBody body,
            string environmentHash, AERISTerrainTileLod lod, double latitude,
            double longitude)
        {
            double radius = body == null ? 600000.0 : Math.Max(1000.0, body.Radius);
            if (!IsFinite(latitude)) latitude = 0.0;
            if (!IsFinite(longitude)) longitude = 0.0;
            double span = AERISTerrainTileFormat.AngularSpanDegrees(lod, radius);
            int latitudeCount = Math.Max(1, (int)Math.Ceiling(180.0 / span));
            int longitudeCount = Math.Max(1, (int)Math.Ceiling(360.0 / span));
            int latIndex = Math.Max(0, Math.Min(latitudeCount - 1,
                (int)Math.Floor((Math.Max(-90.0, Math.Min(89.999999, latitude)) + 90.0) / span)));
            int lonIndex = WrapIndex((int)Math.Floor((NormalizeLongitude(longitude) + 180.0) / span),
                longitudeCount);
            return new AERISTerrainTileKey(body == null ? string.Empty : body.name,
                radius, environmentHash, lod, latIndex, lonIndex);
        }

        internal static int LatitudeTileCountFor(CelestialBody body,
            AERISTerrainTileLod lod)
        {
            return LatitudeTileCount(body, lod);
        }

        static int LatitudeTileCount(CelestialBody body, AERISTerrainTileLod lod)
        {
            double radius = body == null ? 600000.0 : Math.Max(1000.0, body.Radius);
            return Math.Max(1, (int)Math.Ceiling(180.0 /
                AERISTerrainTileFormat.AngularSpanDegrees(lod, radius)));
        }

        internal static int LongitudeTileCountFor(CelestialBody body,
            AERISTerrainTileLod lod)
        {
            return LongitudeTileCount(body, lod);
        }

        static int LongitudeTileCount(CelestialBody body, AERISTerrainTileLod lod)
        {
            double radius = body == null ? 600000.0 : Math.Max(1000.0, body.Radius);
            return Math.Max(1, (int)Math.Ceiling(360.0 /
                AERISTerrainTileFormat.AngularSpanDegrees(lod, radius)));
        }

        internal static int WrapTileIndex(int value, int count)
        {
            return WrapIndex(value, count);
        }

        static int WrapIndex(int value, int count)
        {
            if (count <= 0) return 0;
            int wrapped = value % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        static int ResolveQueueCapacity(AERISTerrainPerformanceController controller)
        {
            AERISTerrainPerformanceProfile profile = controller == null ? null :
                controller.ActiveProfile;
            return profile == null ? 64 : Math.Max(16, profile.MaximumTerrainTileRequests * 2);
        }

        static float ResolvePlanningIntervalSeconds(AERISTerrainPerformanceController controller)
        {
            if (controller == null) return 0.5f;
            return 1f / Mathf.Clamp(controller.EffectiveTilePlanningFps, 0.5f, 5f);
        }

        static long ResolveRamLimitBytes(AERISSettings settings,
            AERISTerrainPerformanceController controller)
        {
            int configured = settings == null ? 0 : settings.TerrainRamCacheLimitMiB;
            if (configured > 0) return configured * 1024L * 1024L;
            AERISTerrainPerformanceProfile profile = controller == null ? null : controller.ActiveProfile;
            return (profile == null ? 128L : profile.DefaultRamCacheMiB) * 1024L * 1024L;
        }

        static long ResolveResidentCacheBudgetBytes(AERISSettings settings,
            AERISTerrainPerformanceController controller)
        {
            // Gate 1 keeps a separately accounted owner/budget without changing the
            // existing hot viewport cache setting. Gate 2 may expose a dedicated user
            // setting after measured payload sizes are available.
            long hotBudget = ResolveRamLimitBytes(settings, controller);
            long minimum = 256L * 1024L * 1024L;
            long maximum = 4L * 1024L * 1024L * 1024L;
            return Math.Min(maximum, Math.Max(minimum, hotBudget * 4L));
        }

        static long ResolveDiskLimitBytes(AERISSettings settings,
            AERISTerrainPerformanceController controller)
        {
            int configured = settings == null ? 0 : settings.TerrainDiskCacheLimitMiB;
            if (configured > 0) return configured * 1024L * 1024L;
            AERISTerrainPerformanceProfile profile = controller == null ? null : controller.ActiveProfile;
            return (profile == null ? 2048L : profile.DefaultDiskCacheMiB) * 1024L * 1024L;
        }

        static long ResolvePreloadLimitBytes(AERISSettings settings)
        {
            // Candidate 13 final policy: persistent preload storage is unlimited.
            return long.MaxValue;
        }

        internal static bool BodyHasSolidSurface(CelestialBody body)
        {
            // Candidate 14 safety contract: terrain support is body-local and fail-closed.
            // A global/shared PQS availability fallback can report Kerbin's sampler while
            // inspecting a different body, which can accidentally admit stars or gas giants
            // into automatic preload. Only an actual PQS controller on THIS body proves a
            // solid terrain surface. Unknown/reflection-failure bodies are unsupported.
            if (body == null) return false;
            try
            {
                FieldInfo field = body.GetType().GetField("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(body) != null;
                PropertyInfo property = body.GetType().GetProperty("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null) return property.GetValue(body, null) != null;
            }
            catch { return false; }
            return false;
        }

        internal static bool GameDataHashReady
        {
            get
            {
                lock (environmentSync) return gameDataHashReady;
            }
        }

        internal static string GameDataHash
        {
            get
            {
                lock (environmentSync) return cachedGameDataHash;
            }
        }

        static void RequestGameDataHash()
        {
            lock (environmentSync)
            {
                if (gameDataHashReady || gameDataHashRequested) return;
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            string applicationRoot = KSPUtil.ApplicationRootPath;
            lock (environmentSync)
            {
                if (gameDataHashReady || gameDataHashRequested) return;
                gameDataHashRequested = true;
            }
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.GeneralCompute, "terrain-gamedata-hash",
                runtime.CaptureStamp(), context =>
                {
                    return ComputeGameDataHash(applicationRoot);
                }, value =>
                {
                    string result = value as string;
                    lock (environmentSync)
                    {
                        cachedGameDataHash = string.IsNullOrEmpty(result) ?
                            AERISTerrainHash.Fnv1A64Hex("PRELOAD_DB_UNKNOWN") : result;
                        gameDataHashReady = true;
                        gameDataHashRequested = false;
                    }
                }, false);
            if (!accepted)
            {
                lock (environmentSync) gameDataHashRequested = false;
            }
        }

        static string ComputeGameDataHash(string applicationRoot)
        {
            var builder = new System.Text.StringBuilder(256);
            builder.Append("PRELOAD_DB_")
                .Append(AERISTerrainPreloadFormat.DatabaseFormatVersion).Append('|');
            try
            {
                string gameData = Path.Combine(applicationRoot ?? string.Empty,
                    "GameData");
                string configSha = Path.Combine(gameData, "ModuleManager.ConfigSHA");
                if (File.Exists(configSha))
                {
                    string text = File.ReadAllText(configSha);
                    builder.Append(text.Length).Append('|')
                        .Append(AERISTerrainHash.Fnv1A64Hex(text));
                }
                else
                {
                    string[] files = Directory.Exists(gameData) ?
                        Directory.GetFiles(gameData, "*.cfg",
                            SearchOption.AllDirectories) : new string[0];
                    Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string relative = files[i].StartsWith(gameData,
                            StringComparison.OrdinalIgnoreCase) ?
                            files[i].Substring(gameData.Length) : files[i];
                        string text = File.ReadAllText(files[i]);
                        builder.Append(relative).Append('|').Append(text.Length)
                            .Append('|').Append(AERISTerrainHash.Fnv1A64Hex(text))
                            .Append(';');
                    }
                }
            }
            catch (Exception ex)
            {
                builder.Append("UNKNOWN|").Append(ex.GetType().FullName);
            }
            return AERISTerrainHash.Fnv1A64Hex(builder.ToString());
        }

        internal static string EnvironmentHashForBody(CelestialBody body)
        {
            if (!GameDataHashReady) return string.Empty;
            string cacheKey = (body == null ? string.Empty : body.name) + "|" +
                (body == null ? 0.0 : body.Radius).ToString("R",
                    CultureInfo.InvariantCulture) + "|" +
                (body != null && body.ocean ? "1" : "0") + "|" + GameDataHash;
            lock (environmentSync)
            {
                string cached;
                if (cachedBodyEnvironmentHashes.TryGetValue(cacheKey, out cached))
                    return cached;
            }
            var builder = new System.Text.StringBuilder(4096);
            builder.Append(AERISTerrainTileFormat.Version).Append('|');
            builder.Append(AERISTerrainPreloadFormat.DatabaseFormatVersion).Append('|');
            builder.Append(body == null ? string.Empty : body.name).Append('|');
            builder.Append((body == null ? 0.0 : body.Radius).ToString("R",
                CultureInfo.InvariantCulture)).Append('|');
            builder.Append(body != null && body.ocean ? "1" : "0").Append('|');
            AppendPqsConfigurationFingerprint(builder, body);
            string result = AERISTerrainHash.Fnv1A64Hex(builder.ToString());
            lock (environmentSync) cachedBodyEnvironmentHashes[cacheKey] = result;
            return result;
        }

        static void AppendPqsConfigurationFingerprint(System.Text.StringBuilder builder,
            CelestialBody body)
        {
            if (builder == null || body == null) return;
            try
            {
                FieldInfo field = body.GetType().GetField("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo property = body.GetType().GetProperty("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object pqs = field != null ? field.GetValue(body) :
                    property == null ? null : property.GetValue(body, null);
                if (pqs == null) return;
                builder.Append(pqs.GetType().AssemblyQualifiedName).Append('|');
                AppendStablePrimitiveMembers(builder, pqs, 96);
                object mods = ReadMemberValue(pqs, "mods");
                System.Collections.IEnumerable enumerable = mods as System.Collections.IEnumerable;
                if (enumerable == null) return;
                int count = 0;
                foreach (object mod in enumerable)
                {
                    if (mod == null || count++ >= 128) break;
                    builder.Append("MOD:").Append(mod.GetType().AssemblyQualifiedName).Append('|');
                    AppendStablePrimitiveMembers(builder, mod, 64);
                }
            }
            catch (Exception ex)
            {
                builder.Append("PQS_HASH_ERROR:").Append(ex.GetType().FullName).Append('|');
            }
        }

        static object ReadMemberValue(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.GetIndexParameters().Length == 0 &&
                property.CanRead ? property.GetValue(target, null) : null;
        }

        static void AppendStablePrimitiveMembers(System.Text.StringBuilder builder,
            object target, int maximum)
        {
            if (builder == null || target == null || maximum <= 0) return;
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            int written = 0;
            for (int i = 0; i < fields.Length && written < maximum; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.IsStatic || !StableFingerprintType(field.FieldType))
                    continue;
                object value;
                try { value = field.GetValue(target); }
                catch { continue; }
                builder.Append(field.Name).Append('=').Append(StableFingerprintValue(value))
                    .Append('|');
                written++;
            }
        }

        static bool StableFingerprintType(Type type)
        {
            if (type == null) return false;
            return type.IsPrimitive || type.IsEnum || type == typeof(string) ||
                type == typeof(decimal) || type == typeof(Vector2) ||
                type == typeof(Vector3) || type == typeof(Vector4) ||
                type == typeof(Color);
        }

        static string StableFingerprintValue(object value)
        {
            if (value == null) return "<null>";
            if (value is Vector2)
            {
                Vector2 vector = (Vector2)value;
                return vector.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    vector.y.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is Vector3)
            {
                Vector3 vector = (Vector3)value;
                return vector.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    vector.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    vector.z.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is Vector4)
            {
                Vector4 vector = (Vector4)value;
                return vector.x.ToString("R", CultureInfo.InvariantCulture) + "," +
                    vector.y.ToString("R", CultureInfo.InvariantCulture) + "," +
                    vector.z.ToString("R", CultureInfo.InvariantCulture) + "," +
                    vector.w.ToString("R", CultureInfo.InvariantCulture);
            }
            if (value is Color)
            {
                Color colour = (Color)value;
                return colour.r.ToString("R", CultureInfo.InvariantCulture) + "," +
                    colour.g.ToString("R", CultureInfo.InvariantCulture) + "," +
                    colour.b.ToString("R", CultureInfo.InvariantCulture) + "," +
                    colour.a.ToString("R", CultureInfo.InvariantCulture);
            }
            IFormattable formattable = value as IFormattable;
            return formattable == null ? value.ToString() :
                formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        static long CurrentVesselGeneration()
        {
            Vessel vessel = FlightGlobals.ActiveVessel;
            if (vessel == null) return 0L;
            try { return vessel.id.GetHashCode(); }
            catch { return 0L; }
        }

        static double InterpolateLongitude(double west, double east, double t)
        {
            double delta = NormalizeLongitude(east - west);
            if (delta <= 0.0) delta += 360.0;
            return NormalizeLongitude(west + delta * t);
        }

        static void OffsetLatLon(CelestialBody body, double originLatDeg,
            double originLonDeg, double eastMeters, double northMeters,
            out double latitudeDeg, out double longitudeDeg)
        {
            double radius = Math.Max(1000.0, body == null ? 600000.0 : body.Radius);
            double distance = Math.Sqrt(eastMeters * eastMeters + northMeters * northMeters);
            if (distance < 0.001)
            {
                latitudeDeg = originLatDeg;
                longitudeDeg = NormalizeLongitude(originLonDeg);
                return;
            }
            double bearing = Math.Atan2(eastMeters, northMeters);
            double angular = distance / radius;
            double lat1 = originLatDeg * Math.PI / 180.0;
            double lon1 = originLonDeg * Math.PI / 180.0;
            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angular) +
                Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing));
            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) * Math.Cos(lat1),
                Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
            latitudeDeg = lat2 * 180.0 / Math.PI;
            longitudeDeg = NormalizeLongitude(lon2 * 180.0 / Math.PI);
        }

        static double NormalizeLongitude(double longitude)
        {
            while (longitude > 180.0) longitude -= 360.0;
            while (longitude < -180.0) longitude += 360.0;
            return longitude;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            ReleaseResidentPlanPins();
            predictiveCorridor.Reset("SHUTDOWN");
            disposed = true;
            if (preloadBuilder != null) preloadBuilder.Dispose();
            if (blockPipeline != null) blockPipeline.Dispose();
            lock (sync)
            {
                queued.Clear();
                diskLoading.Clear();
                preloadChunksLoading.Clear();
                residentChunksLoading.Clear();
                residentPopulationPlan.Clear();
                residentPopulationCursor = 0;
                residentPopulationBlockedFromLod = 5;
                residentPopulationScopeGeneration = -1L;
                residentPopulationIndexGeneration = -1L;
                residentLoadsInFlight = 0;
                preloadChunkTileIds.Clear();
                diskWriting.Clear();
                diskLoadingSince.Clear();
                diskLoadingRequests.Clear();
                desiredRequestIds.Clear();
                desiredVisibleIds.Clear();
                diskWritingSince.Clear();
                diskWritePending.Clear();
                diskWriteRetryAfter.Clear();
                diskWriteAttempts.Clear();
                visibleKeys.Clear();
                visibleTiles.Clear();
                diskLoadsInFlight = 0;
            }
            if (currentBodyResidentCache != null) currentBodyResidentCache.Dispose();
            if (preloadDatabase != null) preloadDatabase.Dispose();
            disk.FlushIndex();
            if (warm != null) warm.Clear();
            ram.Clear();
        }
    }
}
