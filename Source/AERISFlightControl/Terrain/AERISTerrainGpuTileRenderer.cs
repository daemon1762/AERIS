using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{

    internal struct AERISTerrainPresentedProjection
    {
        internal bool Valid;
        internal bool Latched;
        internal double CenterLatitudeDeg;
        internal double CenterLongitudeDeg;
        internal float RangeMeters;
        internal float MapHeadingDeg;
        internal bool TrackUp;
        internal float AnchorV;
        internal AERISTerrainRenderTargetOrientation Orientation;
        internal float AgeSeconds;
    }

    // Unity-main-thread GPU composition layer. Workers prepare immutable height topology,
    // slope factors, land-only contours and coastline segments. Final TOPO/REL colours are
    // calculated explicitly from elevation metres on the main thread and uploaded as vertex
    // colours. This avoids built-in-shader texture-scale/offset ambiguity. Land and water are
    // separate clipped meshes, so land warning colours cannot bleed across the coastline. A
    // mesh/material/RenderTexture fault disables only this terrain presentation layer; ND
    // symbols, runways, LAND observation and AP remain available. Gate 4C keeps the Gate 4A
    // GPU-only contract and the Gate 4B geometry-integrity rule. Gate 5 Candidate 2 adds a
    // presentation latch: when the next exact projection is not ready, the last complete GPU
    // FRONT remains visible without any warp and publishes its committed projection so every
    // world-fixed overlay is drawn in that same coordinate authority. Route/Local display
    // quality is reconstructed from FAR; exact existing/LAND microtiles remain authoritative.
    internal sealed class AERISTerrainGpuTileRenderer : IDisposable
    {
        sealed class Entry
        {
            internal string CacheKey;
            internal AERISTerrainTileKey TileKey;
            internal long TileCreatedUtcTicks;
            internal string StyleKey;
            internal Mesh LandMesh;
            internal Mesh WaterMesh;
            // Candidate8 sparse coastal correction overlays. These replace only the
            // coarse parent cells crossed by the 129x129 boundary; they never promote
            // the whole FAR tile to a 129x129 surface mesh.
            internal Mesh CoastalLandCorrectionMesh;
            internal Mesh CoastalWaterCorrectionMesh;
            internal Mesh ContourMesh;
            internal Mesh CoastlineMesh;
            internal GeographicUnitPoint[] LandGeographicPoints;
            internal GeographicUnitPoint[] WaterGeographicPoints;
            internal GeographicUnitPoint[] CoastalLandCorrectionGeographicPoints;
            internal GeographicUnitPoint[] CoastalWaterCorrectionGeographicPoints;
            internal GeographicUnitPoint[] ContourGeographicPoints;
            internal GeographicUnitPoint[] CoastlineGeographicPoints;
            internal Vector3[] LandProjectedVertices;
            internal Vector3[] WaterProjectedVertices;
            internal Vector3[] CoastalLandCorrectionProjectedVertices;
            internal Vector3[] CoastalWaterCorrectionProjectedVertices;
            internal Vector3[] ContourProjectedVertices;
            internal Vector3[] CoastlineProjectedVertices;
            internal double SouthLatitudeDeg;
            internal double NorthLatitudeDeg;
            internal double WestLongitudeDeg;
            internal double EastLongitudeDeg;
            // Conservative spherical bound for whole-entry rejection before any exact
            // per-vertex projection/upload. Invalid or very large bounds disable culling.
            internal double BoundCenterLatitudeDeg;
            internal double BoundCenterLongitudeDeg;
            internal double BoundAngularRadiusRad = Math.PI;
            internal double LastProjectionCenterLatitudeDeg = double.NaN;
            internal double LastProjectionCenterLongitudeDeg = double.NaN;
            internal double LastProjectionBodyRadius = double.NaN;
            internal float LastProjectionRangeMeters = float.NaN;
            internal float LastProjectionAnchorBottom = float.NaN;
            internal AERISTerrainRenderTargetOrientation LastProjectionOrientation =
                (AERISTerrainRenderTargetOrientation)(-1);
            internal float[] LandElevationMeters;
            internal byte[] LandShade;
            internal Color32[] LandColours;
            internal float[] CoastalLandCorrectionElevationMeters;
            internal byte[] CoastalLandCorrectionShade;
            internal Color32[] CoastalLandCorrectionColours;
            internal AERISTerrainDisplayMode ColourMode = (AERISTerrainDisplayMode)(-1);
            internal AERISTerrainColourPreset ColourPreset = (AERISTerrainColourPreset)(-1);
            internal AERISTerrainColourPreset WaterColourPreset =
                (AERISTerrainColourPreset)(-1);
            internal int RelativeAltitudeBucket = int.MinValue;
            internal int Resolution;
            internal int CoastlineResolution;
            internal int CoastalCorrectionParentCells;
            internal byte[] Valid;
            internal float CoverageFraction;
            internal long Bytes;
            internal long LastUse;
        }

        struct SurfacePoint
        {
            internal float X;
            internal float Y;
            internal float ElevationMeters;
            internal byte Shade;
            internal bool Water;
        }

        struct GeographicUnitPoint
        {
            internal double X;
            internal double Y;
            internal double Z;
        }

        sealed class SurfaceBuilder
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<float> Elevation = new List<float>();
            internal readonly List<byte> Shade = new List<byte>();
            internal readonly List<int> Triangles = new List<int>();

            internal void Reset()
            {
                Vertices.Clear();
                Elevation.Clear();
                Shade.Clear();
                Triangles.Clear();
            }

            internal void AddPolygon(SurfacePoint[] points, int count)
            {
                if (points == null || count < 3) return;
                int start = Vertices.Count;
                for (int i = 0; i < count; i++)
                {
                    Vertices.Add(new Vector3(points[i].X, points[i].Y, 0f));
                    Elevation.Add(points[i].ElevationMeters);
                    Shade.Add(points[i].Shade);
                }
                for (int i = 1; i < count - 1; i++)
                {
                    Triangles.Add(start);
                    Triangles.Add(start + i);
                    Triangles.Add(start + i + 1);
                }
            }
        }

        struct CoverageRegion
        {
            internal Rect Rect;
            internal Entry Entry;
        }

        const string GraphicsAssistName = "UNITY GPU EXACT FAR PRESENTATION";
        const float RelativeAltitudeBucketMeters = 5f;
        // Gate 4B Recovery Hotfix 1: keep a wider GPU history surface than the
        // visible ND viewport so normal aircraft translation/rotation does not
        // instantly invalidate the temporal FRONT. The user-visible projection
        // is still the exact requested range; only the hidden FAR history surface
        // is oversized.
        const float HistoryOverscanScale = 1.35f;
        const float MaximumHistorySurfaceRangeMeters = 250000f;
        const float ProjectionRefreshAgeSeconds = 0.50f;
        const float ProjectionRefreshHeadingDeg = 8f;
        // Cadence Hotfix 3: the 0.50s/large-distance rules remain safety/fallback
        // authorities, but a genuinely moving map commits on every fixed 10 Hz
        // authoritative tick. The speed guard prevents parked floating-origin noise
        // from turning into needless BACK renders.
        const float AuthoritativeMotionSpeedMetersPerSecond = 0.5f;
        const double AuthoritativeMotionDistanceMeters = 0.01;
        const double AuthoritativeMotionFallbackDistanceMeters = 0.25;
        const float AuthoritativeMotionHeadingDeg = 0.05f;
        const float ReadyBuildingViolationSeconds = 1.0f;
        // Operation Health Step 2: while current content is still being assembled,
        // content maintenance may run at most 5 Hz. Once READY, it becomes generation /
        // movement driven while exact projection/presentation remains 10 Hz.
        const float ContentMaintenanceRetrySeconds = 0.20f;

        readonly AERISSettings settings;
        readonly AERISTerrainPerformanceController performance;
        readonly AERISTerrainGpuTileRasterizer rasterizer =
            new AERISTerrainGpuTileRasterizer();
        readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        // Operation Health Pass 1: entry selection is keyed by immutable TileKey.
        // This preserves the exact Candidate11 current/fallback selection rules while
        // eliminating repeated scans over unrelated GPU entries every repaint.
        readonly Dictionary<AERISTerrainTileKey, List<Entry>> entriesByTile =
            new Dictionary<AERISTerrainTileKey, List<Entry>>();
        readonly Dictionary<string, AERISTerrainRenderReadyHeightField>
            renderReadyFields =
            new Dictionary<string, AERISTerrainRenderReadyHeightField>(StringComparer.Ordinal);
        readonly List<AERISTerrainGpuTileRasterResult> completed =
            new List<AERISTerrainGpuTileRasterResult>(16);
        readonly HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        // Pending markers used to be represented as cacheKey + "|PENDING", allocating a
        // second string per scheduled tile. Keep scheduling identity in its own set.
        readonly HashSet<string> scheduledThisFrame =
            new HashSet<string>(StringComparer.Ordinal);
        readonly List<CoverageRegion> coverageRects =
            new List<CoverageRegion>(128);
        readonly List<Entry> supersededScratch = new List<Entry>(16);
        // Operation Health Pass 2: BuildEntry is main-thread serialized. Keep the large
        // List backing arrays and clipping storage alive between tile uploads instead of
        // re-growing and collecting them for every replacement tile.
        readonly SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();
        readonly SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();
        readonly SurfacePoint[] surfaceClipScratch = new SurfacePoint[6];
        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);
        // Recycle native Unity Mesh objects across ordinary tile eviction/supersession.
        // Terrain OFF / viewport suspension still destroys the pool, preserving the
        // existing resource-release contract.
        const int MaximumPooledMeshes = 24;
        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);
        // Operation Health Pass 3: immutable identity indices and uniform-colour upload
        // buffers are keyed by vertex count. Unity copies these arrays on assignment, so
        // the same managed buffers can safely serve later meshes without visual coupling.
        readonly Dictionary<int, int[]> identityIndexCache = new Dictionary<int, int[]>();
        readonly Dictionary<int, Color32[]> uniformColourScratch = new Dictionary<int, Color32[]>();
        static readonly Bounds NdPresentationBounds = new Bounds(
            new Vector3(0.5f, 0.5f, 0f), new Vector3(32f, 32f, 4f));
        static readonly Rect FrontUvDirect = new Rect(0f, 0f, 1f, 1f);
        static readonly Rect FrontUvFlipped = new Rect(0f, 1f, 1f, -1f);
        long operationHealthIdentityIndexHits;
        long operationHealthIdentityIndexMisses;
        long operationHealthUniformColourReuses;
        long operationHealthBoundsSkips;
        long operationHealthTerrainSetPassSaved;
        // Cadence Hotfix 1: content/view revisions may request a refresh, but they may
        // not bypass the fixed 10 Hz presentation gate. Bootstrap remains the only
        // normal-path immediate exception when no FRONT has ever been attempted.
        long operationHealthCadenceDeferrals;
        long operationHealthCadenceBootstrapBypasses;
        // Cadence Hotfix 2 / Refresh Coalescing: only the fixed 10 Hz authoritative
        // tick may capture/resolve/upload/render terrain. Intervening KSP Repaints
        // only re-present the already committed FRONT texture.
        long operationHealthAuthoritativeTicks;
        long operationHealthCoalescedPresentFrames;
        // AERIS23 retained-surface path. Authoritative presentation state advances at
        // fixed 10 Hz; intervening IMGUI Repaints perform only the unavoidable final blit.
        long operationHealthAuthoritativePresents;
        long operationHealthRetainedSurfaceBlits;
        long operationHealthCoalescedBlankPolls;
        long operationHealthAuthoritativeSafetyBypasses;
        long operationHealthDirtyBatches;
        long operationHealthDirtySignalsCoalesced;
        long operationHealthDirtyCommits;
        long operationHealthMotionRefreshes;
        long operationHealthForcedProjectionRefreshes;
        long operationHealthLoadingBackdropFrames;
        long operationHealthRequestedViewReadyTransitions;
        // Step 2 splits expensive content maintenance from the fixed motion clock.
        long operationHealthContentTicks;
        long operationHealthMotionOnlyTicks;
        long operationHealthContentCaptures;
        long operationHealthContentWorkerDrains;
        long operationHealthContentRetries;
        long operationHealthObsoleteJobsCancelled;
        long operationHealthViewInvalidations;
        long operationHealthMeshPoolHits;
        long operationHealthMeshPoolMisses;
        long operationHealthMeshPoolRecycles;
        long operationHealthMeshPoolDestroys;
        long operationHealthSurfaceBuilderReuses;
        // Reusable exact-length presentation scratch. No visual or ordering authority is
        // changed; this only removes Clone()/temporary entry lookup churn on Repaint.
        AERISTerrainHeightTile[] sortedTilesScratch = new AERISTerrainHeightTile[0];
        Entry[] fallbackEntriesScratch = new Entry[0];
        Entry[] currentEntriesScratch = new Entry[0];
        Entry[] drawEntriesScratch = new Entry[0];
        // Step 2 content snapshot. These arrays/entries remain immutable between content
        // maintenance ticks and are only reprojected by the 10 Hz motion path.
        AERISTerrainVisibleTileSet contentVisible;
        long contentTerrainGeneration = -1L;
        string contentStyleKey = string.Empty;
        double contentCenterLatitudeDeg;
        double contentCenterLongitudeDeg;
        float contentRangeMeters;
        float contentHeadingDeg;
        bool contentTrackUp;
        float contentAnchorV;
        AERISTerrainRenderTargetOrientation contentOrientation;
        int contentReadyGlobal;
        int contentReadyFar;
        float contentFoundationCoverage;
        bool contentSnapshotValid;
        bool contentGpuReadyPending;
        float nextContentMaintenanceRealtime;
        long operationHealthResolveCalls;
        long operationHealthResolveCandidates;
        long operationHealthTileScratchResizes;
        long operationHealthPreparedEntryUses;
        long operationHealthCullTests;
        long operationHealthCulledEntries;
        long operationHealthVisibleEntries;
        long operationHealthWideRangeCullBypassFrames;
        long useSequence;
        long usedEntryBytes;
        long backTargetBytes;
        long frontTargetBytes;
        int generation;
        int uploadFailures;
        int uploaded;
        int evicted;
        bool gpuFailed;
        bool disposed;
        float lastCoverageFraction;
        float lastVisualCoverageFraction;
        float nextAlignmentLogRealtime;
        float lastRunwayMapLockErrorPixels;
        AERISTerrainGpuDrawState lastDrawState;
        string fault = string.Empty;
        AERISTerrainGpuMode lastGpuMode = (AERISTerrainGpuMode)(-1);
        bool lastGlobalGpuAllowed;
        bool automaticCapabilityWarningLogged;
        Material terrainMaterial;
        Material contourMaterial;
        Material coastlineMaterial;
        RenderTexture backTarget;
        RenderTexture frontTarget;
        bool frontBufferValid;
        long frontViewGeneration = -1L;
        long frontTerrainGeneration = -1L;
        string frontBodyName = string.Empty;
        long frontBodyRadiusMillimetres;
        // AERIS23 FRONT presentation fast path: exact body object identity is captured
        // only on authoritative FRONT swap. Non-authoritative IMGUI Repaints can then
        // validate the committed surface without repeated string/radius work.
        CelestialBody frontBodyReference;
        double frontCenterLatitudeDeg;
        double frontCenterLongitudeDeg;
        float frontRangeMeters;
        float frontSurfaceRangeMeters;
        float frontMapHeadingDeg;
        bool frontTrackUp;
        float frontAnchorV;
        AERISTerrainRenderTargetOrientation frontOrientation;
        float frontCommittedRealtime;
        bool lastFrontBufferPresented;
        bool lastFrontBufferLatched;
        AERISTerrainPresentedProjection presentedProjection;
        float lastBackFoundationCoverage;
        long frontBufferSwaps;
        long blockedIncompleteSwaps;
        long cpuTerrainDrawCount;
        long renderReadyBytes;
        long gpuContentRevision;
        long frontContentRevision;
        bool gpuContentDirty;
        // Cadence Hotfix 4: presentation continuity and requested-view readiness are
        // different states. A stale/latched FRONT may remain visible as a backdrop while
        // the newly requested range/view is still BUILDING.
        bool requestedViewReady;
        // Candidate9: a FRONT is authoritative only for the colour mode/preset
        // with which it was rendered. Palette or AUTO REL/TOPO transitions
        // must never present a stale texture under new annunciation state.
        AERISTerrainDisplayMode frontColourMode = (AERISTerrainDisplayMode)(-1);
        AERISTerrainColourPreset frontColourPreset = (AERISTerrainColourPreset)(-1);
        long lastBackAttemptViewGeneration = -1L;
        long lastBackAttemptContentRevision = -1L;
        float nextBackRefreshRealtime;
        float nextAuthoritativePresentationTickRealtime;
        long historyReprojectFrames;
        long historyRejectedFrames;
        long directFrontFrames;
        long backRenderFrames;
        long skippedBackRenderFrames;
        long forcedRecoveryBackRenders;
        long generationBridgeFrames;
        long generationBridgeRejects;
        long readyBuildingViolations;
        float readyBuildingSinceRealtime = -1f;
        bool readyBuildingViolationLatched;
        float lastHistoryConfidence;
        bool lastHistoryReprojected;
        long renderReadyUseSequence;
        int renderReadyEvictions;
        AERISCurrentBodyResidentCache residentCache;
        float nextPresentationLogRealtime;
        long virtualRouteBuilds;
        long virtualLocalBuilds;
        long exactDetailOverlayDraws;
        int highDensityCoastlineEntries;
        int sparseCoastalCorrectionEntries;
        long sparseCoastalCorrectionParentCells;
        string lastVirtualDetailName = "FAR DIRECT";

        internal AERISTerrainGpuTileRenderer(AERISSettings settings,
            AERISTerrainPerformanceController performance)
        {
            this.settings = settings;
            this.performance = performance;
        }

        internal bool GpuFailed { get { return gpuFailed; } }
        internal string FaultText { get { return fault; } }
        internal long UsedBytes
        {
            get
            {
                return Math.Max(0L, usedEntryBytes) + Math.Max(0L, backTargetBytes) +
                    Math.Max(0L, frontTargetBytes) + Math.Max(0L, renderReadyBytes);
            }
        }
        // Kept as TextureCount in telemetry for backward column compatibility. CP2 entries
        // are GPU mesh groups with bounded vertex-colour updates, not terrain textures.
        internal int TextureCount { get { return entries.Count; } }
        internal int PendingCount { get { return rasterizer.PendingCount; } }
        internal int UploadFailures
        {
            get { return uploadFailures + rasterizer.FailureCount; }
        }
        internal int UploadedCount { get { return uploaded; } }
        internal int EvictedCount { get { return evicted; } }
        internal float LastCoverageFraction { get { return lastCoverageFraction; } }
        internal float LastVisualCoverageFraction { get { return lastVisualCoverageFraction; } }
        internal AERISTerrainGpuDrawState LastDrawState { get { return lastDrawState; } }
        internal bool RequestedViewReady { get { return requestedViewReady; } }
        internal float LastRunwayMapLockErrorPixels
        {
            get { return lastRunwayMapLockErrorPixels; }
        }
        internal bool FrontBufferPresented { get { return lastFrontBufferPresented; } }
        internal bool FrontBufferLatched { get { return lastFrontBufferLatched; } }
        internal AERISTerrainPresentedProjection PresentedProjection
        {
            get { return presentedProjection; }
        }
        internal float LastBackFoundationCoverage { get { return lastBackFoundationCoverage; } }
        internal long FrontBufferSwaps { get { return frontBufferSwaps; } }
        internal long BlockedIncompleteSwaps { get { return blockedIncompleteSwaps; } }
        internal long CpuTerrainDrawCount { get { return cpuTerrainDrawCount; } }
        internal long HistoryReprojectFrames { get { return historyReprojectFrames; } }
        internal long HistoryRejectedFrames { get { return historyRejectedFrames; } }
        internal long DirectFrontFrames { get { return directFrontFrames; } }
        internal long BackRenderFrames { get { return backRenderFrames; } }
        internal long SkippedBackRenderFrames { get { return skippedBackRenderFrames; } }
        internal float LastHistoryConfidence { get { return lastHistoryConfidence; } }
        internal bool HistoryReprojected { get { return lastHistoryReprojected; } }
        internal string VirtualDetailLevel
        {
            get { return lastVirtualDetailName; }
        }
        internal long VirtualRouteBuilds { get { return virtualRouteBuilds; } }
        internal long VirtualLocalBuilds { get { return virtualLocalBuilds; } }
        internal long ExactDetailOverlayDraws { get { return exactDetailOverlayDraws; } }
        internal long RenderReadyBytes { get { return renderReadyBytes; } }
        internal int RenderReadyCount { get { return renderReadyFields.Count; } }
        internal int RenderReadyEvictions { get { return renderReadyEvictions; } }
        internal double FrontBufferAgeMilliseconds
        {
            get
            {
                return !frontBufferValid ? 0.0 : Math.Max(0.0,
                    (Time.realtimeSinceStartup - frontCommittedRealtime) * 1000.0);
            }
        }

        internal void InvalidatePendingForViewChange()
        {
            int obsoletePending = rasterizer.PendingCount;
            if (obsoletePending > 0)
                operationHealthObsoleteJobsCancelled += obsoletePending;
            operationHealthViewInvalidations++;
            generation++;
            rasterizer.CancelAll();
            requested.Clear();
            scheduledThisFrame.Clear();
            ResetContentSnapshot();
            // The previous FRONT is intentionally retained as a continuity backdrop, but
            // it no longer satisfies the newly requested range/view. Reset only the new
            // view progress/readiness authority so UI shows BUILDING immediately.
            requestedViewReady = false;
            lastBackFoundationCoverage = 0f;
            lastCoverageFraction = 0f;
            lastDrawState = AERISTerrainGpuDrawState.Partial;
            // Do not reset nextAuthoritativePresentationTickRealtime here. Range/view
            // changes are consumed by the next regular 10 Hz tick instead of creating
            // an extra immediate presentation frame.
        }

        internal void SuspendViewport()
        {
            generation++;
            rasterizer.CancelAll();
            ReleaseGpuResources();
            lastCoverageFraction = 0f;
            lastVisualCoverageFraction = 0f;
            lastRunwayMapLockErrorPixels = 0f;
            lastDrawState = AERISTerrainGpuDrawState.None;
            ResetFrontBufferState();
        }

        internal void ResetGpuFailure()
        {
            bool rebuildResources = gpuFailed;
            gpuFailed = false;
            fault = string.Empty;
            lastCoverageFraction = 0f;
            lastVisualCoverageFraction = 0f;
            lastDrawState = AERISTerrainGpuDrawState.None;
            if (rebuildResources) ReleaseGpuResources();
        }

        internal AERISTerrainGpuDrawState Draw(Rect plot,
            AERISTerrainTileSystem system, Vessel vessel, double centerLatitudeDeg,
            double centerLongitudeDeg, float rangeMeters, float mapHeadingDeg,
            bool trackUp, float anchorV, AERISNdMapLockReference lockReference)
        {
            if (disposed || system == null || vessel == null || vessel.mainBody == null ||
                plot.width < 8f || plot.height < 8f)
            {
                lastCoverageFraction = 0f;
                lastVisualCoverageFraction = 0f;
                lastDrawState = AERISTerrainGpuDrawState.None;
                return lastDrawState;
            }

            Event currentEvent = Event.current;
            bool repaint = currentEvent == null || currentEvent.type == EventType.Repaint;
            if (!repaint) return lastDrawState;

            // Retained FRONT gate: this executes before resident-cache, settings, GPU-mode,
            // projection or content work. Unity IMGUI still needs one final texture blit on
            // each rendered frame to reconstruct the framebuffer, but AERIS performs no
            // additional presentation work until the next fixed 10 Hz authoritative tick.
            float presentationNow = Time.realtimeSinceStartup;
            bool authoritativeTickDue = nextAuthoritativePresentationTickRealtime <= 0f ||
                presentationNow >= nextAuthoritativePresentationTickRealtime;
            if (!authoritativeTickDue)
            {
                if (TryPresentCoalescedFront(plot, vessel))
                    return lastDrawState;
                if (!frontBufferValid)
                {
                    operationHealthCoalescedBlankPolls++;
                    return lastDrawState;
                }
                operationHealthAuthoritativeSafetyBypasses++;
                authoritativeTickDue = true;
            }

            residentCache = system.CurrentBodyResidentCache;

            AERISTerrainGpuMode currentGpuMode = settings == null ?
                AERISTerrainGpuMode.Automatic : settings.TerrainGpuMode;
            bool globalGpuAllowed = settings == null ||
                settings.PerformanceGpuAccelerationEnabled;
            if (currentGpuMode != lastGpuMode || globalGpuAllowed != lastGlobalGpuAllowed)
            {
                lastGpuMode = currentGpuMode;
                lastGlobalGpuAllowed = globalGpuAllowed;
                automaticCapabilityWarningLogged = false;
                if (globalGpuAllowed && currentGpuMode != AERISTerrainGpuMode.Off)
                    ResetGpuFailure();
            }
            if (currentGpuMode == AERISTerrainGpuMode.Automatic &&
                !AutomaticGpuCapabilityAvailable())
            {
                if (!automaticCapabilityWarningLogged)
                {
                    automaticCapabilityWarningLogged = true;
                    AERISLogger.Warn("[ND/TERRAIN_GPU] GPU presentation unavailable; " +
                        "CPU terrain presentation is disabled by Gate 4A contract.");
                }
                ReleaseGpuResources();
                ResetFrontBufferState();
                lastCoverageFraction = 0f;
                lastVisualCoverageFraction = 0f;
                lastDrawState = AERISTerrainGpuDrawState.None;
                return lastDrawState;
            }
            if (gpuFailed || !globalGpuAllowed ||
                currentGpuMode == AERISTerrainGpuMode.Off)
            {
                ReleaseGpuResources();
                ResetFrontBufferState();
                lastCoverageFraction = 0f;
                lastVisualCoverageFraction = 0f;
                lastDrawState = AERISTerrainGpuDrawState.None;
                return lastDrawState;
            }

            AERISTerrainDisplayMode requestedMode = settings == null ?
                AERISTerrainDisplayMode.Automatic : settings.TerrainDisplayMode;
            if (requestedMode == AERISTerrainDisplayMode.Off)
            {
                SuspendViewport();
                return lastDrawState;
            }

            AERISTerrainRenderTargetOrientation orientation = settings == null ?
                AERISTerrainRenderTargetOrientation.Direct :
                settings.TerrainRenderTargetOrientation;

            // Only the authoritative tick may invalidate/rebuild the published presentation
            // state. Retained Repaints returned above without touching these fields.
            lastFrontBufferPresented = false;
            lastFrontBufferLatched = false;
            presentedProjection.Valid = false;
            nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f;
            operationHealthAuthoritativeTicks++;

            float historySurfaceRangeMeters = rangeMeters;
            AERISTerrainDisplayMode effectiveMode = ResolveEffectiveMode(requestedMode,
                vessel, rangeMeters);
            AERISTerrainColourPreset currentPreset = settings == null ?
                AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;
            AERISTerrainVirtualDetailProfile virtualDetail =
                ResolveVirtualDetailProfile(rangeMeters);
            lastVirtualDetailName = virtualDetail.Name;
            float contourInterval = ResolveContourInterval(rangeMeters);
            string styleKey = BuildStyleKey(contourInterval, virtualDetail);

            bool workerResultReady = rasterizer.CompletedCount > 0;
            bool contentGeometryChanged = NeedsContentRefresh(system, vessel,
                centerLatitudeDeg, centerLongitudeDeg, rangeMeters, mapHeadingDeg,
                trackUp, anchorV, orientation, styleKey);
            bool contentRetryDue = (rasterizer.PendingCount > 0 ||
                !requestedViewReady) &&
                presentationNow >= nextContentMaintenanceRealtime;
            bool contentTickRequired = contentGeometryChanged || workerResultReady ||
                contentRetryDue;
            if (contentRetryDue && !contentGeometryChanged && !workerResultReady)
                operationHealthContentRetries++;

            AERISTerrainVisibleTileSet visible = contentVisible;
            AERISTerrainHeightTile[] tiles = sortedTilesScratch;
            int readyGlobal = contentReadyGlobal;
            int readyFar = contentReadyFar;

            if (contentTickRequired)
            {
                operationHealthContentTicks++;
                if (workerResultReady) operationHealthContentWorkerDrains++;
                DrainCompleted(system);
                // CaptureVisible owns planner-generation updates and RAM tile selection.
                // Step 2 simply stops invoking this allocation/resolve path for pure motion.
                visible = system.CaptureVisible(centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
                operationHealthContentCaptures++;
                if (visible == null || visible.Tiles == null ||
                    visible.Tiles.Length == 0)
                {
                    ResetContentSnapshot();
                    nextContentMaintenanceRealtime = presentationNow +
                        ContentMaintenanceRetrySeconds;
                    lastCoverageFraction = 0f;
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    TryPresentCoalescedFront(plot, vessel);
                    return lastDrawState;
                }

                requested.Clear();
                scheduledThisFrame.Clear();
                tiles = PrepareSortedTileScratch(visible.Tiles);
                EnsureEntryScratch(tiles == null ? 0 : tiles.Length);
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null)
                    {
                        fallbackEntriesScratch[i] = null;
                        currentEntriesScratch[i] = null;
                        drawEntriesScratch[i] = null;
                        continue;
                    }
                    string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                    requested.Add(cacheKey);
                    Entry fallbackEntry, currentEntry;
                    ResolveRenderableEntries(tile, cacheKey, styleKey,
                        out fallbackEntry, out currentEntry);
                    if (currentEntry == null)
                    {
                        if (!TryUploadRenderReadyField(tile, cacheKey, styleKey, system,
                            out currentEntry))
                            Schedule(tile, cacheKey, styleKey, contourInterval,
                                virtualDetail);
                    }
                    if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                    if (currentEntry != null) currentEntry.LastUse = ++useSequence;
                    fallbackEntriesScratch[i] = fallbackEntry;
                    currentEntriesScratch[i] = currentEntry;
                    drawEntriesScratch[i] = currentEntry != null ?
                        currentEntry : fallbackEntry;
                }

                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,
                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);
                contentVisible = visible;
                contentTerrainGeneration = visible.TerrainGeneration;
                contentStyleKey = styleKey;
                contentCenterLatitudeDeg = centerLatitudeDeg;
                contentCenterLongitudeDeg = centerLongitudeDeg;
                contentRangeMeters = rangeMeters;
                contentHeadingDeg = mapHeadingDeg;
                contentTrackUp = trackUp;
                contentAnchorV = anchorV;
                contentOrientation = orientation;
                contentReadyGlobal = readyGlobal;
                contentReadyFar = readyFar;
                contentSnapshotValid = true;
                contentGpuReadyPending = true;
                nextContentMaintenanceRealtime = presentationNow +
                    ContentMaintenanceRetrySeconds;
                lastBackFoundationCoverage = contentFoundationCoverage;
                lastCoverageFraction = contentFoundationCoverage;
            }
            else
            {
                operationHealthMotionOnlyTicks++;
                if (!contentSnapshotValid || visible == null || tiles == null ||
                    tiles.Length == 0)
                {
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    TryPresentCoalescedFront(plot, vessel);
                    return lastDrawState;
                }
                lastBackFoundationCoverage = contentFoundationCoverage;
                lastCoverageFraction = contentFoundationCoverage;
            }

            AERISNdMapProjection projection = AERISNdMapProjection.Create(
                vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                mapHeadingDeg, trackUp, anchorV, orientation);
            Matrix4x4 mapRotation = projection.ResolveScaleCorrectedRenderMatrix();
            AERISNdMapProjection historySurfaceProjection = projection;
            Matrix4x4 historySurfaceMapRotation = mapRotation;
            lastRunwayMapLockErrorPixels = MeasureRunwayMapLockError(plot,
                projection, mapRotation, lockReference);
            if (lastRunwayMapLockErrorPixels > 1.0f)
            {
                AERISLogger.Warn("[ND/RUNWAY_MAP_LOCK] terrain commit rejected; errorPx=" +
                    lastRunwayMapLockErrorPixels.ToString("F3",
                    CultureInfo.InvariantCulture) + "; runway=" +
                    (lockReference == null ? "NONE" : lockReference.StableId) + ".");
                lastDrawState = AERISTerrainGpuDrawState.Partial;
                return lastDrawState;
            }

            EnsureResources(plot, effectiveMode, currentPreset, virtualDetail);
            if (contentTickRequired)
            {
                Prune(ResolveVramLimitBytes());
                PruneRenderReady(ResolveRenderReadyLimitBytes());
            }
            if (backTarget == null || !backTarget.IsCreated() || frontTarget == null ||
                !frontTarget.IsCreated())
            {
                lastDrawState = AERISTerrainGpuDrawState.None;
                return lastDrawState;
            }

            bool forceCenterProjectionRefresh;
            bool authoritativeMotionRefreshRequired = NeedsAuthoritativeMotionRefresh(
                vessel, centerLatitudeDeg, centerLongitudeDeg, mapHeadingDeg, trackUp,
                out forceCenterProjectionRefresh);
            if (authoritativeMotionRefreshRequired) operationHealthMotionRefreshes++;
            bool projectionRefreshRequired = authoritativeMotionRefreshRequired ||
                NeedsProjectionRefresh(visible, vessel, centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
            bool colourRefreshRequired = frontColourMode != effectiveMode ||
                frontColourPreset != currentPreset;
            bool refreshRequired = !frontBufferValid ||
                frontTerrainGeneration != visible.TerrainGeneration ||
                frontViewGeneration != visible.ViewGeneration ||
                frontContentRevision != gpuContentRevision ||
                colourRefreshRequired || projectionRefreshRequired;
            bool refreshAllowed = ShouldRefreshBackBuffer(visible, refreshRequired);
            bool rendered = false;
            bool foundationComplete = false;
            bool swapped = false;
            if (refreshAllowed)
            {
                rendered = RenderBackBuffer(tiles, drawEntriesScratch, projection,
                    mapRotation, effectiveMode, vessel, rangeMeters, anchorV,
                    forceCenterProjectionRefresh);
                backRenderFrames++;
                lastBackAttemptViewGeneration = visible.ViewGeneration;
                lastBackAttemptContentRevision = gpuContentRevision;
                nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime;
                foundationComplete = rendered && visible.FoundationComplete &&
                    lastBackFoundationCoverage >= 0.999f &&
                    readyFar >= visible.FarFoundationCount;
                if (foundationComplete)
                {
                    SwapFrontAndBack(visible, vessel, centerLatitudeDeg,
                        centerLongitudeDeg, rangeMeters, rangeMeters,
                        mapHeadingDeg, trackUp, anchorV, orientation);
                    frontColourMode = effectiveMode;
                    frontColourPreset = currentPreset;
                    if (contentGpuReadyPending)
                    {
                        MarkVisibleGpuReady(tiles);
                        contentGpuReadyPending = false;
                    }
                    swapped = true;
                }
                else
                {
                    blockedIncompleteSwaps++;
                }
            }
            else if (refreshRequired)
            {
                skippedBackRenderFrames++;
            }

            bool colourCompatible = frontColourMode == effectiveMode &&
                frontColourPreset == currentPreset;
            bool directCompatible = colourCompatible &&
                IsFrontBufferCompatible(visible, vessel, centerLatitudeDeg,
                    centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
            lastHistoryReprojected = false;
            lastHistoryConfidence = 0f;
            bool present = false;
            if (directCompatible)
            {
                PresentFrontDirect(plot, frontOrientation);
                directFrontFrames++;
                lastFrontBufferPresented = true;
                lastFrontBufferLatched = false;
                CapturePresentedProjection(false);
                lastVisualCoverageFraction = 1f;
                present = true;
                RecordPresentedFrontAlignmentDiagnostic(plot, tiles, vessel,
                    effectiveMode, currentPreset, lockReference);
            }
            else
            {
                lastFrontBufferPresented = false;
                lastVisualCoverageFraction = 0f;
            }

            // CP3.75 Candidate 2 presentation continuity. Never force a full BACK render
            // merely because ownship outran the old Candidate1 exact-center tolerance. The
            // scheduled refresh path above owns normal map recentering. Until it commits, keep
            // the last complete FRONT visible and publish that FRONT projection so terrain,
            // ownship, runway, vector and LAND geometry share one coordinate authority.
            if (!present && colourCompatible &&
                CanPresentLatchedFront(visible, vessel))
            {
                if (frontTerrainGeneration != visible.TerrainGeneration)
                    generationBridgeFrames++;
                PresentFrontDirect(plot, frontOrientation);
                lastFrontBufferPresented = true;
                lastFrontBufferLatched = true;
                CapturePresentedProjection(true);
                lastVisualCoverageFraction = 1f;
                present = true;
                RecordPresentedFrontAlignmentDiagnostic(plot, tiles, vessel,
                    effectiveMode, currentPreset, lockReference);
            }

            // Last-resort recovery only. This path is no longer a normal high-speed update
            // mechanism; it runs only when no complete FRONT can be presented while the FAR
            // foundation is already ready. This preserves the no-blank safety invariant
            // without turning vessel speed into main-thread render frequency.
            bool readyFoundationNow = visible.FoundationComplete &&
                lastBackFoundationCoverage >= 0.999f &&
                readyFar >= visible.FarFoundationCount;
            if (!present && readyFoundationNow && !gpuFailed)
            {
                bool recovered = RenderBackBuffer(tiles, drawEntriesScratch, projection,
                    mapRotation, effectiveMode, vessel, rangeMeters, anchorV,
                    forceCenterProjectionRefresh);
                backRenderFrames++;
                forcedRecoveryBackRenders++;
                lastBackAttemptViewGeneration = visible.ViewGeneration;
                lastBackAttemptContentRevision = gpuContentRevision;
                nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime;
                if (recovered)
                {
                    SwapFrontAndBack(visible, vessel, centerLatitudeDeg,
                        centerLongitudeDeg, rangeMeters, rangeMeters,
                        mapHeadingDeg, trackUp, anchorV, orientation);
                    frontColourMode = effectiveMode;
                    frontColourPreset = currentPreset;
                    if (contentGpuReadyPending)
                    {
                        MarkVisibleGpuReady(tiles);
                        contentGpuReadyPending = false;
                    }
                    swapped = true;
                    PresentFrontDirect(plot, frontOrientation);
                    directFrontFrames++;
                    lastHistoryReprojected = false;
                    lastHistoryConfidence = 0f;
                    lastFrontBufferPresented = true;
                    lastFrontBufferLatched = false;
                    CapturePresentedProjection(false);
                    lastVisualCoverageFraction = 1f;
                    present = true;
                    RecordPresentedFrontAlignmentDiagnostic(plot, tiles, vessel,
                        effectiveMode, settings == null ? AERISTerrainColourPreset.Standard :
                        settings.TerrainColourPreset, lockReference);
                }
            }

            if (!present && frontBufferValid)
                generationBridgeRejects++;

            UpdateReadyBuildingWatchdog(present, readyFoundationNow, visible,
                readyGlobal, readyFar);

            if (present && !requestedViewReady) operationHealthLoadingBackdropFrames++;
            if (present) operationHealthAuthoritativePresents++;
            LogGpuOnlyPresentation(visible, readyGlobal, readyFar, swapped);
            // A continuity FRONT is allowed to remain visible, but only an exact FRONT
            // committed for the currently requested view may report Complete.
            lastDrawState = present && requestedViewReady ?
                AERISTerrainGpuDrawState.Complete : AERISTerrainGpuDrawState.Partial;
            return lastDrawState;
        }

        bool NeedsContentRefresh(AERISTerrainTileSystem system, Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation, string styleKey)
        {
            if (!contentSnapshotValid || contentVisible == null || system == null ||
                vessel == null || vessel.mainBody == null) return true;
            if (contentTerrainGeneration != system.TerrainGeneration ||
                !string.Equals(contentStyleKey, styleKey, StringComparison.Ordinal) ||
                contentTrackUp != trackUp || contentOrientation != orientation ||
                Math.Abs(contentAnchorV - anchorV) > 0.001f ||
                Math.Abs(contentRangeMeters - rangeMeters) > 0.5f) return true;
            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(contentHeadingDeg,
                mapHeadingDeg)) >= 3f) return true;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                contentCenterLatitudeDeg, contentCenterLongitudeDeg,
                centerLatitudeDeg, centerLongitudeDeg);
            if (double.IsNaN(displacement) || double.IsInfinity(displacement))
                return true;
            return displacement >= Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02);
        }

        void ResetContentSnapshot()
        {
            contentVisible = null;
            contentTerrainGeneration = -1L;
            contentStyleKey = string.Empty;
            contentCenterLatitudeDeg = 0.0;
            contentCenterLongitudeDeg = 0.0;
            contentRangeMeters = 0f;
            contentHeadingDeg = 0f;
            contentTrackUp = false;
            contentAnchorV = 0.5f;
            contentOrientation = AERISTerrainRenderTargetOrientation.Direct;
            contentReadyGlobal = 0;
            contentReadyFar = 0;
            contentFoundationCoverage = 0f;
            contentSnapshotValid = false;
            contentGpuReadyPending = false;
            nextContentMaintenanceRealtime = 0f;
        }

        bool RenderBackBuffer(AERISTerrainHeightTile[] tiles, Entry[] drawEntries,
            AERISNdMapProjection projection, Matrix4x4 mapRotation,
            AERISTerrainDisplayMode effectiveMode, Vessel vessel, float rangeMeters,
            float anchorV, bool forceCenterProjectionRefresh)
        {
            if (forceCenterProjectionRefresh)
                operationHealthForcedProjectionRefreshes++;
            long frameStartTicks = Stopwatch.GetTimestamp();
            RenderTexture previous = RenderTexture.active;
            bool matrixPushed = false;
            bool rendered = false;
            try
            {
                RenderTexture.active = backTarget;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadOrtho();
                // Back is never visible before a complete FAR foundation commit, so a
                // transparent clear cannot expose a black wedge to the user.
                GL.Clear(true, true, Color.clear);
                float projectionThresholdMeters = Math.Max(0.25f,
                    rangeMeters / Math.Max(128f, backTarget.height) * 0.25f);
                double projectionCenterLatitudeDeg = UnitLatitude(
                    projection.CenterX, projection.CenterY, projection.CenterZ);
                double projectionCenterLongitudeDeg = UnitLongitude(
                    projection.CenterX, projection.CenterY);
                // Runtime A/B on 160 km showed ~1000 spherical cull tests/sec while only
                // rejecting ~7-10% of entries. The great-circle test itself became more
                // expensive than the work it saved. Keep culling for narrower views where
                // rejection measured ~60%+, but bypass it entirely for the 160 km preset.
                bool entryCullingEnabled = rangeMeters < 120000f;
                if (!entryCullingEnabled) operationHealthWideRangeCullBypassFrames++;
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    Entry drawEntry = drawEntries != null && i < drawEntries.Length ?
                        drawEntries[i] : null;
                    if (drawEntry == null) continue;
                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry, vessel.mainBody,
                            projectionCenterLatitudeDeg, projectionCenterLongitudeDeg,
                            rangeMeters, anchorV)) continue;
                    operationHealthPreparedEntryUses++;
                    EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg, forceCenterProjectionRefresh);
                    bool entryRendered = DrawEntry(drawEntry, mapRotation, true, effectiveMode,
                        settings == null ? AERISTerrainColourPreset.Standard :
                        settings.TerrainColourPreset, (float)vessel.altitude);
                    rendered = entryRendered || rendered;
                    if (entryRendered && tile.Key.Lod >= AERISTerrainTileLod.Route)
                        exactDetailOverlayDraws++;
                }
            }
            catch (Exception ex)
            {
                FailGpuTerrain(ex);
                return false;
            }
            finally
            {
                if (matrixPushed)
                {
                    try { GL.PopMatrix(); } catch { }
                }
                RenderTexture.active = previous;
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Gpu.RecordFrameCost((Stopwatch.GetTimestamp() - frameStartTicks) *
                    1000.0 / Stopwatch.Frequency);
            return rendered;
        }

        float MeasureFoundationGpuReadiness(AERISTerrainVisibleTileSet visible,
            AERISTerrainHeightTile[] tiles, Entry[] currentEntries,
            out int readyGlobal, out int readyFar)
        {
            readyGlobal = 0;
            readyFar = 0;
            if (visible == null || tiles == null) return 0f;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null || tile.Key.Lod != AERISTerrainTileLod.Global &&
                    tile.Key.Lod != AERISTerrainTileLod.Far) continue;
                Entry current = currentEntries != null && i < currentEntries.Length ?
                    currentEntries[i] : null;
                if (current == null || current.CoverageFraction < 0.999f) continue;
                operationHealthPreparedEntryUses++;
                if (tile.Key.Lod == AERISTerrainTileLod.Global) readyGlobal++;
                else readyFar++;
            }
            int required = Math.Max(0, visible.FarFoundationCount);
            int ready = Math.Min(required, readyFar);
            return required <= 0 ? 0f : Mathf.Clamp01(ready / (float)required);
        }

        void SwapFrontAndBack(AERISTerrainVisibleTileSet visible, Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float surfaceRangeMeters, float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            RenderTexture previousFront = frontTarget;
            frontTarget = backTarget;
            backTarget = previousFront;
            long previousFrontBytes = frontTargetBytes;
            frontTargetBytes = backTargetBytes;
            backTargetBytes = previousFrontBytes;
            frontBufferValid = true;
            frontViewGeneration = visible.ViewGeneration;
            frontTerrainGeneration = visible.TerrainGeneration;
            frontBodyName = visible.BodyName ?? string.Empty;
            frontBodyReference = vessel == null ? null : vessel.mainBody;
            frontBodyRadiusMillimetres = vessel == null || vessel.mainBody == null ? 0L :
                (long)Math.Round(Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);
            frontCenterLatitudeDeg = centerLatitudeDeg;
            frontCenterLongitudeDeg = centerLongitudeDeg;
            frontRangeMeters = rangeMeters;
            frontSurfaceRangeMeters = Math.Max(rangeMeters, surfaceRangeMeters);
            frontMapHeadingDeg = mapHeadingDeg;
            frontTrackUp = trackUp;
            frontAnchorV = anchorV;
            frontOrientation = orientation;
            frontCommittedRealtime = Time.realtimeSinceStartup;
            frontContentRevision = gpuContentRevision;
            if (!requestedViewReady) operationHealthRequestedViewReadyTransitions++;
            requestedViewReady = true;
            if (gpuContentDirty) operationHealthDirtyCommits++;
            gpuContentDirty = false;
            frontBufferSwaps++;
        }

        bool IsFrontBufferCompatible(AERISTerrainVisibleTileSet visible, Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||
                visible == null || vessel == null || vessel.mainBody == null) return false;
            if (frontTerrainGeneration != visible.TerrainGeneration ||
                !string.Equals(frontBodyName, visible.BodyName,
                    StringComparison.OrdinalIgnoreCase)) return false;
            long bodyRadiusMillimetres = (long)Math.Round(
                Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);
            if (bodyRadiusMillimetres != frontBodyRadiusMillimetres ||
                frontTrackUp != trackUp || frontOrientation != orientation ||
                Math.Abs(frontAnchorV - anchorV) > 0.001f) return false;
            if (Math.Abs(frontRangeMeters - rangeMeters) >
                Math.Max(1f, rangeMeters * 0.001f)) return false;
            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(frontMapHeadingDeg,
                mapHeadingDeg)) > ProjectionRefreshHeadingDeg) return false;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                frontCenterLatitudeDeg, frontCenterLongitudeDeg, centerLatitudeDeg,
                centerLongitudeDeg);
            return !double.IsNaN(displacement) && !double.IsInfinity(displacement) &&
                displacement <= ProjectionRefreshDistanceMeters(rangeMeters);
        }

        bool TryPresentCoalescedFront(Rect plot, Vessel vessel)
        {
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||
                vessel == null || vessel.mainBody == null ||
                !ReferenceEquals(frontBodyReference, vessel.mainBody)) return false;
            // Non-authoritative Repaint must still place the retained texture once because
            // Unity IMGUI rebuilds the framebuffer every rendered frame. Everything else
            // reuses state established by the 10 Hz authoritative FRONT commit.
            PresentFrontDirect(plot, frontOrientation);
            // Keep only the tiny retained-state refresh needed by consumers that read
            // PresentedProjection between authoritative commits. Geometry/content/lifecycle
            // state is untouched on this path.
            lastFrontBufferPresented = true;
            lastFrontBufferLatched = true;
            presentedProjection.Valid = true;
            presentedProjection.Latched = true;
            presentedProjection.AgeSeconds = Math.Max(0f,
                Time.realtimeSinceStartup - frontCommittedRealtime);
            lastVisualCoverageFraction = 1f;
            operationHealthRetainedSurfaceBlits++;
            if (!requestedViewReady) operationHealthLoadingBackdropFrames++;
            return true;
        }

        void MarkGpuContentDirty()
        {
            if (gpuContentDirty)
            {
                operationHealthDirtySignalsCoalesced++;
                return;
            }
            gpuContentDirty = true;
            gpuContentRevision++;
            operationHealthDirtyBatches++;
        }

        bool ShouldRefreshBackBuffer(AERISTerrainVisibleTileSet visible,
            bool refreshRequired)
        {
            if (!refreshRequired || visible == null) return false;
            // First ever FRONT construction must not wait on a stale/default timer.
            // After that, every ordinary BACK render request shares one absolute
            // 0.10 s gate, including ViewGeneration and gpuContentRevision changes.
            if (!frontBufferValid && lastBackAttemptViewGeneration < 0L)
            {
                operationHealthCadenceBootstrapBypasses++;
                return true;
            }
            if (Time.realtimeSinceStartup < nextBackRefreshRealtime)
            {
                operationHealthCadenceDeferrals++;
                return false;
            }
            return true;
        }

        static float ResolveHistorySurfaceRange(float visibleRangeMeters)
        {
            float visible = Math.Max(1f, visibleRangeMeters);
            return Mathf.Clamp(visible * HistoryOverscanScale, visible,
                MaximumHistorySurfaceRangeMeters);
        }

        bool NeedsAuthoritativeMotionRefresh(Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float mapHeadingDeg,
            bool trackUp, out bool forceCenterProjectionRefresh)
        {
            forceCenterProjectionRefresh = false;
            if (!frontBufferValid || vessel == null || vessel.mainBody == null) return false;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                frontCenterLatitudeDeg, frontCenterLongitudeDeg, centerLatitudeDeg,
                centerLongitudeDeg);
            if (double.IsNaN(displacement) || double.IsInfinity(displacement)) return true;
            bool speedConfirmedMotion = vessel.srfSpeed >=
                AuthoritativeMotionSpeedMetersPerSecond &&
                displacement >= AuthoritativeMotionDistanceMeters;
            bool displacementConfirmedMotion = displacement >=
                AuthoritativeMotionFallbackDistanceMeters;
            forceCenterProjectionRefresh = speedConfirmedMotion ||
                displacementConfirmedMotion;
            bool headingMotion = trackUp && Mathf.Abs(Mathf.DeltaAngle(
                frontMapHeadingDeg, mapHeadingDeg)) >= AuthoritativeMotionHeadingDeg;
            return forceCenterProjectionRefresh || headingMotion;
        }

        bool NeedsProjectionRefresh(AERISTerrainVisibleTileSet visible, Vessel vessel,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (!frontBufferValid || visible == null || vessel == null ||
                vessel.mainBody == null) return true;
            if (frontTerrainGeneration != visible.TerrainGeneration ||
                !string.Equals(frontBodyName, visible.BodyName,
                    StringComparison.OrdinalIgnoreCase) ||
                frontTrackUp != trackUp || frontOrientation != orientation ||
                Math.Abs(frontAnchorV - anchorV) > 0.001f) return true;
            if (Math.Abs(frontRangeMeters - rangeMeters) >
                Math.Max(1f, rangeMeters * 0.0025f)) return true;
            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(frontMapHeadingDeg,
                mapHeadingDeg)) >= ProjectionRefreshHeadingDeg) return true;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                frontCenterLatitudeDeg, frontCenterLongitudeDeg, centerLatitudeDeg,
                centerLongitudeDeg);
            if (double.IsNaN(displacement) || double.IsInfinity(displacement)) return true;
            if (displacement >= ProjectionRefreshDistanceMeters(rangeMeters)) return true;
            return Time.realtimeSinceStartup - frontCommittedRealtime >=
                ProjectionRefreshAgeSeconds;
        }

        static double ProjectionRefreshDistanceMeters(float rangeMeters)
        {
            return Math.Max(250.0, Math.Max(1f, rangeMeters) * 0.06);
        }

        bool CanPresentLatchedFront(AERISTerrainVisibleTileSet visible, Vessel vessel)
        {
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||
                visible == null || vessel == null || vessel.mainBody == null) return false;
            // Gate 5 Candidate 3: TerrainGeneration is a supply-generation boundary, not a
            // presentation-invalidity boundary. The last fully committed GPU FRONT is still
            // geographically self-consistent for its own published projection and may bridge
            // a short generation rollover. The ND consumes that FRONT projection for every
            // world-locked layer, so this cannot create the former floating-runway mismatch.
            // Safety/LAND authority never consumes this stale presentation surface.
            if (!string.Equals(frontBodyName, visible.BodyName,
                    StringComparison.OrdinalIgnoreCase)) return false;
            long bodyRadiusMillimetres = (long)Math.Round(
                Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);
            if (bodyRadiusMillimetres != frontBodyRadiusMillimetres) return false;
            // A stale FRONT is a continuity bridge, not a permanent frozen map. If supply
            // stalls for an abnormal interval, fail visibly rather than presenting old data
            // indefinitely. Normal FAR boundary transitions complete far below this limit.
            return Time.realtimeSinceStartup - frontCommittedRealtime <= 8.0f;
        }

        void CapturePresentedProjection(bool latched)
        {
            presentedProjection.Valid = frontBufferValid;
            presentedProjection.Latched = latched;
            presentedProjection.CenterLatitudeDeg = frontCenterLatitudeDeg;
            presentedProjection.CenterLongitudeDeg = frontCenterLongitudeDeg;
            presentedProjection.RangeMeters = frontRangeMeters;
            presentedProjection.MapHeadingDeg = frontMapHeadingDeg;
            presentedProjection.TrackUp = frontTrackUp;
            presentedProjection.AnchorV = frontAnchorV;
            presentedProjection.Orientation = frontOrientation;
            presentedProjection.AgeSeconds = Math.Max(0f,
                Time.realtimeSinceStartup - frontCommittedRealtime);
        }

        void UpdateReadyBuildingWatchdog(bool presented, bool readyFoundation,
            AERISTerrainVisibleTileSet visible, int readyGlobal, int readyFar)
        {
            if (presented || !readyFoundation)
            {
                readyBuildingSinceRealtime = -1f;
                readyBuildingViolationLatched = false;
                return;
            }
            float now = Time.realtimeSinceStartup;
            if (readyBuildingSinceRealtime < 0f)
                readyBuildingSinceRealtime = now;
            if (readyBuildingViolationLatched ||
                now - readyBuildingSinceRealtime < ReadyBuildingViolationSeconds) return;
            readyBuildingViolationLatched = true;
            readyBuildingViolations++;
            AERISLogger.Error("[CP3_GATE4B_READY_BUILDING_VIOLATION] FAR foundation is " +
                "complete but no GPU FRONT could be presented for >=1s; ready_gf=" +
                readyGlobal + "/" + readyFar + "; required_gf=" +
                (visible == null ? 0 : visible.GlobalFoundationCount) + "/" +
                (visible == null ? 0 : visible.FarFoundationCount) +
                "; forcing presentation recovery.");
        }

        void PresentFrontDirect(Rect plot,
            AERISTerrainRenderTargetOrientation orientation)
        {
            Rect uv = orientation == AERISTerrainRenderTargetOrientation.Flipped ?
                FrontUvFlipped : FrontUvDirect;
            GUI.DrawTextureWithTexCoords(plot, frontTarget, uv, true);
        }

        bool TryPresentReprojectedFront(Rect plot, AERISTerrainVisibleTileSet visible,
            Vessel vessel, AERISNdMapProjection currentProjection, double centerLatitudeDeg,
            double centerLongitudeDeg, float rangeMeters, float mapHeadingDeg,
            bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation, out float confidence)
        {
            confidence = 0f;
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||
                visible == null || vessel == null || vessel.mainBody == null ||
                frontRangeMeters <= 0f || plot.width < 8f || plot.height < 8f) return false;
            if (frontTerrainGeneration != visible.TerrainGeneration) return false;
            if (!string.Equals(frontBodyName, vessel.mainBody.name,
                StringComparison.OrdinalIgnoreCase) || frontTrackUp != trackUp ||
                frontOrientation != orientation || Math.Abs(frontAnchorV - anchorV) > 0.001f)
                return false;
            long bodyRadiusMillimetres = (long)Math.Round(
                Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);
            if (bodyRadiusMillimetres != frontBodyRadiusMillimetres) return false;

            float ageSeconds = Math.Max(0f,
                Time.realtimeSinceStartup - frontCommittedRealtime);
            if (ageSeconds > 20f) return false;
            float rangeRatio = rangeMeters / Math.Max(1f, frontSurfaceRangeMeters);
            // The FRONT is an oversized history surface. Zoom-in can reuse a much
            // smaller part of that surface; zoom-out is allowed only while the current
            // viewport remains inside the already-rendered overscan authority.
            if (rangeRatio < 0.06f || rangeRatio > 1.00f) return false;
            if (trackUp && Mathf.Abs(Mathf.DeltaAngle(frontMapHeadingDeg,
                mapHeadingDeg)) > 70f) return false;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                frontCenterLatitudeDeg, frontCenterLongitudeDeg, centerLatitudeDeg,
                centerLongitudeDeg);
            if (double.IsNaN(displacement) || double.IsInfinity(displacement) ||
                displacement > Math.Max(3500.0, rangeMeters * 0.32)) return false;

            AERISNdMapProjection oldProjection = AERISNdMapProjection.Create(
                vessel.mainBody, frontCenterLatitudeDeg, frontCenterLongitudeDeg,
                Math.Max(frontRangeMeters, frontSurfaceRangeMeters), frontMapHeadingDeg,
                frontTrackUp, frontAnchorV,
                frontOrientation);
            Vector2 q00, q10, q01, q11;
            if (!ProjectHistoryGuiPoint(oldProjection, currentProjection, 0f, 0f, out q00) ||
                !ProjectHistoryGuiPoint(oldProjection, currentProjection, 1f, 0f, out q10) ||
                !ProjectHistoryGuiPoint(oldProjection, currentProjection, 0f, 1f, out q01) ||
                !ProjectHistoryGuiPoint(oldProjection, currentProjection, 1f, 1f, out q11))
                return false;
            Vector2 axisX = q10 - q00;
            Vector2 axisY = q01 - q00;
            float determinant = axisX.x * axisY.y - axisY.x * axisX.y;
            if (Mathf.Abs(determinant) < 0.08f || Mathf.Abs(determinant) > 8f)
                return false;
            Vector2 predicted11 = q00 + axisX + axisY;
            float distortion = Vector2.Distance(predicted11, q11);
            if (distortion > 0.06f) return false;
            if (!AffineCoversViewport(q00, axisX, axisY, determinant)) return false;

            float headingPenalty = trackUp ? Mathf.Clamp01(
                Mathf.Abs(Mathf.DeltaAngle(frontMapHeadingDeg, mapHeadingDeg)) / 70f) : 0f;
            float displacementPenalty = Mathf.Clamp01((float)(displacement /
                Math.Max(1.0, rangeMeters * 0.32)));
            float agePenalty = Mathf.Clamp01(ageSeconds / 20f);
            float distortionPenalty = Mathf.Clamp01(distortion / 0.06f);
            confidence = Mathf.Clamp01(1f - 0.24f * headingPenalty -
                0.24f * displacementPenalty - 0.20f * agePenalty -
                0.32f * distortionPenalty);
            if (confidence < 0.35f) return false;

            Matrix4x4 previousMatrix = GUI.matrix;
            bool groupBegun = false;
            try
            {
                GUI.BeginGroup(plot);
                groupBegun = true;
                Matrix4x4 transform = Matrix4x4.identity;
                float width = Math.Max(1f, plot.width);
                float height = Math.Max(1f, plot.height);
                transform.m00 = axisX.x;
                transform.m01 = axisY.x * width / height;
                transform.m03 = q00.x * width;
                transform.m10 = axisX.y * height / width;
                transform.m11 = axisY.y;
                transform.m13 = q00.y * height;
                GUI.matrix = previousMatrix * transform;
                bool flipVertically = frontOrientation ==
                    AERISTerrainRenderTargetOrientation.Flipped;
                Rect uv = flipVertically ? new Rect(0f, 1f, 1f, -1f) :
                    new Rect(0f, 0f, 1f, 1f);
                GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, width, height),
                    frontTarget, uv, true);
            }
            catch
            {
                confidence = 0f;
                return false;
            }
            finally
            {
                GUI.matrix = previousMatrix;
                if (groupBegun) GUI.EndGroup();
            }
            return true;
        }

        static bool ProjectHistoryGuiPoint(AERISNdMapProjection oldProjection,
            AERISNdMapProjection currentProjection, float oldU, float oldV,
            out Vector2 current)
        {
            current = Vector2.zero;
            double latitudeDeg, longitudeDeg;
            oldProjection.UnprojectGuiToLatitudeLongitude(oldU, oldV,
                out latitudeDeg, out longitudeDeg);
            if (double.IsNaN(latitudeDeg) || double.IsInfinity(latitudeDeg) ||
                double.IsNaN(longitudeDeg) || double.IsInfinity(longitudeDeg)) return false;
            float u, v;
            currentProjection.ProjectLatitudeLongitudeToGui(latitudeDeg, longitudeDeg,
                out u, out v);
            if (float.IsNaN(u) || float.IsInfinity(u) || float.IsNaN(v) ||
                float.IsInfinity(v)) return false;
            current = new Vector2(u, v);
            return true;
        }

        static bool AffineCoversViewport(Vector2 origin, Vector2 axisX,
            Vector2 axisY, float determinant)
        {
            Vector2[] corners =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(0f, 1f), new Vector2(1f, 1f)
            };
            float inverseDeterminant = 1f / determinant;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector2 delta = corners[i] - origin;
                float sourceU = (delta.x * axisY.y - delta.y * axisY.x) *
                    inverseDeterminant;
                float sourceV = (axisX.x * delta.y - axisX.y * delta.x) *
                    inverseDeterminant;
                if (sourceU < -0.02f || sourceU > 1.02f || sourceV < -0.02f ||
                    sourceV > 1.02f) return false;
            }
            return true;
        }

        bool ShouldCullEntryOutsidePresentation(Entry entry, CelestialBody body,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float anchorV)
        {
            operationHealthCullTests++;
            if (entry == null || body == null || body.Radius <= 0.0 ||
                double.IsNaN(entry.BoundAngularRadiusRad) ||
                double.IsInfinity(entry.BoundAngularRadiusRad) ||
                entry.BoundAngularRadiusRad >= Math.PI * 0.50)
            {
                operationHealthVisibleEntries++;
                return false;
            }
            double centerDistance = GreatCircleDistanceMeters(body,
                centerLatitudeDeg, centerLongitudeDeg, entry.BoundCenterLatitudeDeg,
                entry.BoundCenterLongitudeDeg);
            if (double.IsNaN(centerDistance) || double.IsInfinity(centerDistance))
            {
                operationHealthVisibleEntries++;
                return false;
            }

            // AERISNdMapProjection uses +/-0.65*range horizontally. Vertically the
            // ownship anchor divides one full range; use the farther edge. The resulting
            // circumscribed radius contains the complete rectangular ND viewport for any
            // heading. Extra multiplicative and absolute margins deliberately bias toward
            // false negatives (extra work), never false positives (missing terrain).
            double horizontal = Math.Max(1.0, rangeMeters * 0.65);
            double vertical = Math.Max(1.0, rangeMeters * Math.Max(
                Mathf.Clamp01(anchorV), 1f - Mathf.Clamp01(anchorV)));
            double viewportRadius = Math.Sqrt(horizontal * horizontal +
                vertical * vertical);
            double viewportSafetyRadius = viewportRadius * 1.08 +
                Math.Max(2500.0, Math.Max(1f, rangeMeters) * 0.03);
            double entryRadiusMeters = Math.Max(0.0, body.Radius *
                entry.BoundAngularRadiusRad);
            bool culled = centerDistance - entryRadiusMeters > viewportSafetyRadius;
            if (culled) operationHealthCulledEntries++;
            else operationHealthVisibleEntries++;
            return culled;
        }

        static void ResolveConservativeEntryBounds(double southLatitudeDeg,
            double northLatitudeDeg, double westLongitudeDeg, double eastLongitudeDeg,
            out double centerLatitudeDeg, out double centerLongitudeDeg,
            out double angularRadiusRad)
        {
            centerLatitudeDeg = 0.0;
            centerLongitudeDeg = 0.0;
            angularRadiusRad = Math.PI;
            if (double.IsNaN(southLatitudeDeg) || double.IsInfinity(southLatitudeDeg) ||
                double.IsNaN(northLatitudeDeg) || double.IsInfinity(northLatitudeDeg) ||
                double.IsNaN(westLongitudeDeg) || double.IsInfinity(westLongitudeDeg) ||
                double.IsNaN(eastLongitudeDeg) || double.IsInfinity(eastLongitudeDeg))
                return;
            double latitudeSpan = Math.Abs(northLatitudeDeg - southLatitudeDeg);
            double rawLongitudeSpan = Math.Abs(eastLongitudeDeg - westLongitudeDeg);
            // Global/hemispheric entries are deliberately never culled. Their broad
            // geographic authority is more valuable than a marginal projection saving.
            if (latitudeSpan >= 120.0 || rawLongitudeSpan >= 180.0) return;
            centerLatitudeDeg = Math.Max(-90.0, Math.Min(90.0,
                (southLatitudeDeg + northLatitudeDeg) * 0.5));
            double longitudeSpan = NormalizeLongitudeDelta(
                eastLongitudeDeg - westLongitudeDeg);
            centerLongitudeDeg = NormalizeLongitudeDegrees(
                westLongitudeDeg + longitudeSpan * 0.5);
            double radius = 0.0;
            radius = Math.Max(radius, AngularDistanceRadians(centerLatitudeDeg,
                centerLongitudeDeg, southLatitudeDeg, westLongitudeDeg));
            radius = Math.Max(radius, AngularDistanceRadians(centerLatitudeDeg,
                centerLongitudeDeg, southLatitudeDeg, eastLongitudeDeg));
            radius = Math.Max(radius, AngularDistanceRadians(centerLatitudeDeg,
                centerLongitudeDeg, northLatitudeDeg, westLongitudeDeg));
            radius = Math.Max(radius, AngularDistanceRadians(centerLatitudeDeg,
                centerLongitudeDeg, northLatitudeDeg, eastLongitudeDeg));
            // 10% spherical-bound growth plus a fixed angular pad protects against
            // interpolation/coast correction points lying infinitesimally outside nominal
            // source bounds due to floating-point conversion.
            angularRadiusRad = Math.Min(Math.PI, radius * 1.10 + 0.0005);
        }

        static double AngularDistanceRadians(double latitudeA, double longitudeA,
            double latitudeB, double longitudeB)
        {
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
            return 2.0 * Math.Atan2(Math.Sqrt(value),
                Math.Sqrt(Math.Max(0.0, 1.0 - value)));
        }

        static double NormalizeLongitudeDegrees(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
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

        static bool AutomaticGpuCapabilityAvailable()
        {
            return SystemInfo.supportsRenderTextures &&
                SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32) &&
                SystemInfo.graphicsShaderLevel >= 20;
        }

        void LogGpuOnlyPresentation(AERISTerrainVisibleTileSet visible,
            int readyGlobal, int readyFar, bool swapped)
        {
            if (Time.realtimeSinceStartup < nextPresentationLogRealtime) return;
            nextPresentationLogRealtime = Time.realtimeSinceStartup + 5f;
            string frontMode = !lastFrontBufferPresented ? "BUILDING" :
                (lastFrontBufferLatched ? "LATCHED" : "DIRECT");
            AERISLogger.Info("[CP3_GATE4C_VIRTUAL_DETAIL] front=" + frontMode +
                "; detail=" + VirtualDetailLevel + "; history_conf=" +
                lastHistoryConfidence.ToString("F3", CultureInfo.InvariantCulture) +
                "; latch_age=" + (lastFrontBufferLatched ?
                    Math.Max(0f, Time.realtimeSinceStartup - frontCommittedRealtime) : 0f)
                    .ToString("F3", CultureInfo.InvariantCulture) +
                "; back_foundation=" +
                lastBackFoundationCoverage.ToString("F3", CultureInfo.InvariantCulture) +
                "; ready_gf=" + readyGlobal + "/" + readyFar + "; required_gf=" +
                (visible == null ? 0 : visible.GlobalFoundationCount) + "/" +
                (visible == null ? 0 : visible.FarFoundationCount) + "; swap=" +
                (swapped ? "1" : "0") + "; swaps=" + frontBufferSwaps +
                "; back_render=" + backRenderFrames + "; back_skip=" +
                skippedBackRenderFrames + "; forced_recovery=" +
                forcedRecoveryBackRenders + "; gen_bridge_frames=" +
                generationBridgeFrames + "; gen_bridge_rejects=" +
                generationBridgeRejects + "; front_gen=" + frontTerrainGeneration +
                "; current_gen=" + (visible == null ? -1L : visible.TerrainGeneration) +
                "; ready_build_violation=" + readyBuildingViolations +
                "; history_surface_range=" +
                frontSurfaceRangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; history_frames_quarantined=" + historyReprojectFrames +
                "; history_reject=" + historyRejectedFrames + "; direct_frames=" +
                directFrontFrames + "; blocked=" + blockedIncompleteSwaps +
                "; render_ready=" + renderReadyFields.Count + "/" + renderReadyBytes +
                "; virtual_builds=" + virtualRouteBuilds + "/" +
                virtualLocalBuilds + "; exact_overlay_draws=" +
                exactDetailOverlayDraws + "; coast_hd_entries=" +
                highDensityCoastlineEntries + "; coast_hd_res=" +
                AERISTerrainCoastlineExtractor.HighDensityResolution +
                "; coast_sparse_entries=" + sparseCoastalCorrectionEntries +
                "; coast_sparse_parents=" + sparseCoastalCorrectionParentCells +
                "; oh_resolve_calls=" + operationHealthResolveCalls +
                "; oh_resolve_candidates=" + operationHealthResolveCandidates +
                "; oh_entry_buckets=" + entriesByTile.Count +
                "; oh_tile_scratch_resize=" + operationHealthTileScratchResizes +
                "; oh_prepared_entry_uses=" + operationHealthPreparedEntryUses +
                "; oh_cull_test=" + operationHealthCullTests +
                "; oh_culled_entry=" + operationHealthCulledEntries +
                "; oh_visible_entry=" + operationHealthVisibleEntries +
                "; oh_cull_wide_bypass=" + operationHealthWideRangeCullBypassFrames +
                "; oh_mesh_pool=" + meshPool.Count +
                "; oh_mesh_pool_hit=" + operationHealthMeshPoolHits +
                "; oh_mesh_pool_miss=" + operationHealthMeshPoolMisses +
                "; oh_mesh_recycle=" + operationHealthMeshPoolRecycles +
                "; oh_mesh_destroy=" + operationHealthMeshPoolDestroys +
                "; oh_surface_builder_reuse=" + operationHealthSurfaceBuilderReuses +
                "; oh_identity_index_hit=" + operationHealthIdentityIndexHits +
                "; oh_identity_index_miss=" + operationHealthIdentityIndexMisses +
                "; oh_uniform_colour_reuse=" + operationHealthUniformColourReuses +
                "; oh_bounds_skip=" + operationHealthBoundsSkips +
                "; oh_setpass_saved=" + operationHealthTerrainSetPassSaved +
                "; oh_cadence_defer=" + operationHealthCadenceDeferrals +
                "; oh_cadence_bootstrap=" + operationHealthCadenceBootstrapBypasses +
                "; oh_auth_tick=" + operationHealthAuthoritativeTicks +
                "; oh_auth_present=" + operationHealthAuthoritativePresents +
                "; oh_retained_blit=" + operationHealthRetainedSurfaceBlits +
                "; oh_coalesced_present=" + operationHealthCoalescedPresentFrames +
                "; oh_coalesced_blank=" + operationHealthCoalescedBlankPolls +
                "; oh_tick_safety=" + operationHealthAuthoritativeSafetyBypasses +
                "; oh_dirty_batch=" + operationHealthDirtyBatches +
                "; oh_dirty_coalesced=" + operationHealthDirtySignalsCoalesced +
                "; oh_dirty_commit=" + operationHealthDirtyCommits +
                "; oh_motion_refresh=" + operationHealthMotionRefreshes +
                "; oh_forced_project=" + operationHealthForcedProjectionRefreshes +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +
                "; oh_ready_transition=" + operationHealthRequestedViewReadyTransitions +
                "; requested_view_ready=" + (requestedViewReady ? "1" : "0") +
                "; oh_content_tick=" + operationHealthContentTicks +
                "; oh_motion_only=" + operationHealthMotionOnlyTicks +
                "; oh_content_capture=" + operationHealthContentCaptures +
                "; oh_content_drain=" + operationHealthContentWorkerDrains +
                "; oh_content_retry=" + operationHealthContentRetries +
                "; content_snapshot=" + (contentSnapshotValid ? "1" : "0") +
                "; oh_obsolete_cancel=" + operationHealthObsoleteJobsCancelled +
                "; oh_view_invalidate=" + operationHealthViewInvalidations +
                "; cpu_terrain_draw=0.");
        }

        void ResetFrontBufferState(bool preserveCadenceAndContent = false)
        {
            frontBufferValid = false;
            frontViewGeneration = -1L;
            frontTerrainGeneration = -1L;
            frontBodyName = string.Empty;
            frontBodyRadiusMillimetres = 0L;
            frontBodyReference = null;
            frontCenterLatitudeDeg = 0.0;
            frontCenterLongitudeDeg = 0.0;
            frontRangeMeters = 0f;
            frontSurfaceRangeMeters = 0f;
            frontMapHeadingDeg = 0f;
            frontTrackUp = false;
            frontAnchorV = 0.5f;
            frontOrientation = AERISTerrainRenderTargetOrientation.Direct;
            frontCommittedRealtime = 0f;
            frontContentRevision = -1L;
            frontColourMode = (AERISTerrainDisplayMode)(-1);
            frontColourPreset = (AERISTerrainColourPreset)(-1);
            if (!preserveCadenceAndContent)
            {
                lastBackAttemptViewGeneration = -1L;
                lastBackAttemptContentRevision = -1L;
                nextBackRefreshRealtime = 0f;
                nextAuthoritativePresentationTickRealtime = 0f;
                gpuContentDirty = false;
                ResetContentSnapshot();
            }
            requestedViewReady = false;
            lastFrontBufferPresented = false;
            lastFrontBufferLatched = false;
            presentedProjection.Valid = false;
            lastHistoryReprojected = false;
            lastHistoryConfidence = 0f;
            if (!preserveCadenceAndContent)
                lastBackFoundationCoverage = 0f;
            readyBuildingSinceRealtime = -1f;
            readyBuildingViolationLatched = false;
        }

        void Schedule(AERISTerrainHeightTile tile, string cacheKey, string styleKey,
            float contourInterval, AERISTerrainVirtualDetailProfile virtualDetail)
        {
            if (tile == null || string.IsNullOrEmpty(cacheKey) ||
                !scheduledThisFrame.Add(cacheKey)) return;
            rasterizer.Enqueue(new AERISTerrainGpuTileRasterRequest
            {
                Generation = ++generation,
                Tile = tile,
                ContoursEnabled = settings == null || settings.TerrainContoursEnabled,
                ShadingEnabled = settings == null || settings.TerrainShadingEnabled,
                ContourIntervalMeters = contourInterval,
                StyleKey = styleKey,
                VirtualDetailProfile = virtualDetail
            });
        }

        void DrainCompleted(AERISTerrainTileSystem system)
        {
            completed.Clear();
            int maximum = performance == null ? 2 :
                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo * 2);
            rasterizer.Drain(completed, maximum);
            for (int i = 0; i < completed.Count; i++)
            {
                AERISTerrainGpuTileRasterResult result = completed[i];
                if (!ValidResult(result)) continue;
                string cacheKey = CacheKey(result.Key, result.TileCreatedUtcTicks,
                    result.StyleKey);
                result.LastUseSequence = ++renderReadyUseSequence;
                StoreRenderReadyField(cacheKey, result);
                if (result.Key.Lod == AERISTerrainTileLod.Far)
                {
                    if (result.VirtualDetailLevel ==
                        AERISTerrainVirtualDetailLevel.VirtualRoute) virtualRouteBuilds++;
                    else if (result.VirtualDetailLevel ==
                        AERISTerrainVirtualDetailLevel.VirtualLocal) virtualLocalBuilds++;
                }
                CaptureAndMarkRenderReady(result, system);
                long uploadStartTicks = Stopwatch.GetTimestamp();
                try
                {
                    Entry entry = BuildEntry(cacheKey, result);
                    Entry old;
                    if (entries.TryGetValue(cacheKey, out old)) Remove(old);
                    // A progressive 25/50/75% mesh overlays the last complete preview.
                    // Superseded entries are released only once the replacement itself
                    // spans the complete tile, preventing visible regression to holes.
                    if (entry.CoverageFraction >= 0.999f)
                        RemoveSupersededEntries(result.Key, cacheKey);
                    AddEntry(entry);
                    usedEntryBytes += entry.Bytes;
                    if (entry.CoastlineResolution >=
                        AERISTerrainCoastlineExtractor.HighDensityResolution)
                        highDensityCoastlineEntries++;
                    if (entry.CoastalCorrectionParentCells > 0)
                    {
                        sparseCoastalCorrectionEntries++;
                        sparseCoastalCorrectionParentCells +=
                            entry.CoastalCorrectionParentCells;
                    }
                    uploaded++;
                    MarkGpuContentDirty();
                    MarkGpuReady(result);
                    if (performance != null)
                        performance.RecordGpuMeshPreparation(
                            result.MeshMilliseconds, result.ContourMilliseconds,
                            rasterizer.PendingCount > 0);
                }
                catch (Exception ex)
                {
                    FailGpuTerrain(ex);
                    break;
                }
                finally
                {
                    AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
                    if (runtime != null)
                        runtime.RecordNavigationDisplayTextureUpload(
                            (Stopwatch.GetTimestamp() - uploadStartTicks) *
                            1000.0 / Stopwatch.Frequency);
                }
            }
        }

        void CaptureAndMarkRenderReady(AERISTerrainRenderReadyHeightField field,
            AERISTerrainTileSystem system)
        {
            if (field == null || system == null || system.CurrentBodyResidentCache == null)
                return;
            AERISResidentCommitToken token;
            if (!system.CurrentBodyResidentCache.TryCaptureCommitToken(field.Key,
                out token)) return;
            if (!system.CurrentBodyResidentCache.TryMarkRenderReady(token)) return;
            field.ResidentToken = token;
            field.ResidentTokenValid = true;
        }

        void MarkGpuReady(AERISTerrainRenderReadyHeightField field)
        {
            if (field == null || !field.ResidentTokenValid || residentCache == null) return;
            residentCache.TryMarkGpuReady(field.ResidentToken);
        }

        void StoreRenderReadyField(string cacheKey,
            AERISTerrainRenderReadyHeightField field)
        {
            if (string.IsNullOrEmpty(cacheKey) || field == null) return;
            AERISTerrainRenderReadyHeightField previous;
            if (renderReadyFields.TryGetValue(cacheKey, out previous) && previous != null)
                RemoveRenderReadyField(cacheKey, previous);
            renderReadyFields[cacheKey] = field;
            renderReadyBytes += field.EstimatedBytes;
        }

        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string cacheKey,
            string styleKey, AERISTerrainTileSystem system, out Entry entry)
        {
            entry = null;
            if (tile == null || string.IsNullOrEmpty(cacheKey)) return false;
            AERISTerrainRenderReadyHeightField field;
            if (!renderReadyFields.TryGetValue(cacheKey, out field) || field == null)
                return false;
            field.LastUseSequence = ++renderReadyUseSequence;
            long uploadStartTicks = Stopwatch.GetTimestamp();
            try
            {
                entry = BuildEntry(cacheKey, field);
                Entry old;
                if (entries.TryGetValue(cacheKey, out old)) Remove(old);
                AddEntry(entry);
                usedEntryBytes += entry.Bytes;
                if (entry.CoastlineResolution >=
                    AERISTerrainCoastlineExtractor.HighDensityResolution)
                    highDensityCoastlineEntries++;
                if (entry.CoastalCorrectionParentCells > 0)
                {
                    sparseCoastalCorrectionEntries++;
                    sparseCoastalCorrectionParentCells +=
                        entry.CoastalCorrectionParentCells;
                }
                uploaded++;
                MarkGpuContentDirty();
                MarkGpuReady(field);
                return true;
            }
            catch (Exception ex)
            {
                FailGpuTerrain(ex);
                entry = null;
                return false;
            }
            finally
            {
                AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
                if (runtime != null)
                    runtime.RecordNavigationDisplayTextureUpload(
                        (Stopwatch.GetTimestamp() - uploadStartTicks) *
                        1000.0 / Stopwatch.Frequency);
            }
        }

        long ResolveRenderReadyLimitBytes()
        {
            long residentBudget = residentCache == null ? 0L : residentCache.RamBudgetBytes;
            if (residentBudget <= 0L) residentBudget = 512L * 1024L * 1024L;
            return Math.Max(64L * 1024L * 1024L,
                Math.Min(512L * 1024L * 1024L, residentBudget / 2L));
        }

        void PruneRenderReady(long maximumBytes)
        {
            maximumBytes = Math.Max(64L * 1024L * 1024L, maximumBytes);
            while (renderReadyBytes > maximumBytes && renderReadyFields.Count > 0)
            {
                string oldestKey = null;
                AERISTerrainRenderReadyHeightField oldest = null;
                foreach (KeyValuePair<string, AERISTerrainRenderReadyHeightField> pair in
                    renderReadyFields)
                {
                    // A GPU entry depends on its immutable render-ready height field
                    // for accurate Resident Cache state and lossless re-upload after
                    // an ordinary GPU lifecycle release. Never prune that authority
                    // before the GPU entry itself is evicted.
                    if (requested.Contains(pair.Key) || entries.ContainsKey(pair.Key))
                        continue;
                    if (oldest == null || pair.Value.LastUseSequence < oldest.LastUseSequence)
                    {
                        oldestKey = pair.Key;
                        oldest = pair.Value;
                    }
                }
                if (oldest == null) break;
                RemoveRenderReadyField(oldestKey, oldest);
                renderReadyEvictions++;
            }
        }

        void RemoveRenderReadyField(string cacheKey,
            AERISTerrainRenderReadyHeightField field)
        {
            if (field == null) return;
            renderReadyFields.Remove(cacheKey ?? string.Empty);
            renderReadyBytes = Math.Max(0L, renderReadyBytes - field.EstimatedBytes);
            if (!entries.ContainsKey(cacheKey ?? string.Empty) &&
                field.ResidentTokenValid && residentCache != null)
                residentCache.TryDemotePresentationState(field.ResidentToken,
                    AERISResidentTileState.RamResident);
        }

        void MarkVisibleGpuReady(AERISTerrainHeightTile[] tiles)
        {
            if (tiles == null || residentCache == null) return;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null) continue;
                AERISResidentCommitToken token;
                if (residentCache.TryCaptureCommitToken(tile.Key, out token))
                {
                    if (!residentCache.TryMarkRenderReady(token)) continue;
                    residentCache.TryMarkGpuReady(token);
                }
            }
        }

        void RemoveSupersededEntries(AERISTerrainTileKey key,
            string keepCacheKey)
        {
            supersededScratch.Clear();
            List<Entry> bucket;
            if (!entriesByTile.TryGetValue(key, out bucket) || bucket == null) return;
            for (int i = 0; i < bucket.Count; i++)
            {
                Entry entry = bucket[i];
                if (entry == null || string.Equals(entry.CacheKey, keepCacheKey,
                    StringComparison.Ordinal)) continue;
                supersededScratch.Add(entry);
            }
            for (int i = 0; i < supersededScratch.Count; i++)
                Remove(supersededScratch[i]);
            supersededScratch.Clear();
        }

        static bool ValidResult(AERISTerrainRenderReadyHeightField result)
        {
            if (result == null || result.Resolution < 2 ||
                result.VertexX == null || result.VertexY == null ||
                result.ElevationMeters == null || result.Water == null ||
                result.Valid == null || result.Shade == null ||
                result.Triangles == null) return false;
            int count = result.Resolution * result.Resolution;
            if (result.VertexX.Length != count || result.VertexY.Length != count ||
                result.ElevationMeters.Length != count || result.Water.Length != count ||
                result.Valid.Length != count || result.Shade.Length != count ||
                result.Triangles.Length % 3 != 0) return false;
            for (int i = 0; i < result.Triangles.Length; i++)
                if (result.Triangles[i] < 0 || result.Triangles[i] >= count) return false;
            return true;
        }

        Entry BuildEntry(string cacheKey,
            AERISTerrainRenderReadyHeightField result)
        {
            SurfaceBuilder land = landSurfaceScratch;
            SurfaceBuilder water = waterSurfaceScratch;
            land.Reset();
            water.Reset();
            SurfacePoint[] clipped = surfaceClipScratch;
            operationHealthSurfaceBuilderReuses++;
            for (int i = 0; i + 2 < result.Triangles.Length; i += 3)
            {
                SurfacePoint a = Point(result, result.Triangles[i]);
                SurfacePoint b = Point(result, result.Triangles[i + 1]);
                SurfacePoint c = Point(result, result.Triangles[i + 2]);
                AppendClippedTriangle(land, clipped, a, b, c, false);
                AppendClippedTriangle(water, clipped, a, b, c, true);
            }

            Vector3[] landSource, waterSource, contourSource, coastlineSource;
            Mesh landMesh = BuildSurfaceMesh("AERIS_TERRAIN_LAND_" +
                result.Key.FileStem, land, false, out landSource);
            Mesh waterMesh = BuildSurfaceMesh("AERIS_TERRAIN_WATER_" +
                result.Key.FileStem, water, true, out waterSource);
            Vector3[] coastalLandCorrectionSource, coastalWaterCorrectionSource;
            Mesh coastalLandCorrectionMesh = BuildTriangleListMesh(
                "AERIS_TERRAIN_COAST_LAND_FIX_" + result.Key.FileStem,
                result.CoastalLandCorrectionVertices, false,
                out coastalLandCorrectionSource);
            Mesh coastalWaterCorrectionMesh = BuildTriangleListMesh(
                "AERIS_TERRAIN_COAST_WATER_FIX_" + result.Key.FileStem,
                result.CoastalWaterCorrectionVertices, true,
                out coastalWaterCorrectionSource);
            Mesh contourMesh = BuildLineMesh("AERIS_TERRAIN_CONTOUR_" +
                result.Key.FileStem, result.ContourSegments,
                new Color32(255, 255, 255, 210), out contourSource);
            // CP3.75 Candidate 3 baseline repair: render coastline segments through
            // the same MeshTopology.Lines path as contour segments.  The previous
            // per-segment quad expansion produced variable-width/join artifacts that
            // appeared as cut/tape marks along otherwise correct coastline geometry.
            // Keep the Golden coastline extraction data unchanged; only presentation
            // is unified with the proven contour-line path.
            Mesh coastlineMesh = BuildLineMesh("AERIS_TERRAIN_COAST_" +
                result.Key.FileStem, result.CoastlineSegments,
                new Color32(185, 225, 255, 245), out coastlineSource);

            // Each drawable vertex retains one unit-sphere point (3 doubles) and one
            // projected Vector3 (3 floats) so cache accounting remains conservative.
            long projectedVertexBytes = (long)(land.Vertices.Count +
                water.Vertices.Count +
                (coastalLandCorrectionSource == null ? 0 : coastalLandCorrectionSource.Length) +
                (coastalWaterCorrectionSource == null ? 0 : coastalWaterCorrectionSource.Length) +
                (contourSource == null ? 0 : contourSource.Length) +
                (coastlineSource == null ? 0 : coastlineSource.Length)) * (3L * 8L + 3L * 4L);
            long bytes = result.Valid.Length + projectedVertexBytes +
                land.Vertices.Count * (3L * 4L + 4L + 4L) +
                water.Vertices.Count * (3L * 4L + 4L) +
                (land.Triangles.Count + water.Triangles.Count) * 4L +
                (coastalLandCorrectionSource == null ? 0L :
                    coastalLandCorrectionSource.LongLength * (3L * 4L + 4L + 4L)) +
                (coastalWaterCorrectionSource == null ? 0L :
                    coastalWaterCorrectionSource.LongLength * (3L * 4L + 4L));
            if (result.ContourSegments != null)
                bytes += result.ContourSegments.Length * 4L;
            if (result.CoastlineSegments != null)
                // Candidate 4: coastline now uses the same line topology as contours;
                // account the immutable float segment payload once, not the retired
                // four-vertex quad expansion from pre-Candidate3 presentation.
                bytes += result.CoastlineSegments.Length * 4L;
            double boundCenterLatitudeDeg, boundCenterLongitudeDeg,
                boundAngularRadiusRad;
            ResolveConservativeEntryBounds(result.SouthLatitudeDeg,
                result.NorthLatitudeDeg, result.WestLongitudeDeg,
                result.EastLongitudeDeg, out boundCenterLatitudeDeg,
                out boundCenterLongitudeDeg, out boundAngularRadiusRad);
            return new Entry
            {
                CacheKey = cacheKey,
                TileKey = result.Key,
                TileCreatedUtcTicks = result.TileCreatedUtcTicks,
                StyleKey = result.StyleKey,
                LandMesh = landMesh,
                WaterMesh = waterMesh,
                CoastalLandCorrectionMesh = coastalLandCorrectionMesh,
                CoastalWaterCorrectionMesh = coastalWaterCorrectionMesh,
                ContourMesh = contourMesh,
                CoastlineMesh = coastlineMesh,
                LandGeographicPoints = BuildGeographicPoints(landSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                WaterGeographicPoints = BuildGeographicPoints(waterSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastalLandCorrectionGeographicPoints = BuildGeographicPoints(
                    coastalLandCorrectionSource, result.SouthLatitudeDeg,
                    result.NorthLatitudeDeg, result.WestLongitudeDeg,
                    result.EastLongitudeDeg),
                CoastalWaterCorrectionGeographicPoints = BuildGeographicPoints(
                    coastalWaterCorrectionSource, result.SouthLatitudeDeg,
                    result.NorthLatitudeDeg, result.WestLongitudeDeg,
                    result.EastLongitudeDeg),
                ContourGeographicPoints = BuildGeographicPoints(contourSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastlineGeographicPoints = BuildGeographicPoints(coastlineSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                LandProjectedVertices = AllocateProjectedVertices(landSource),
                WaterProjectedVertices = AllocateProjectedVertices(waterSource),
                CoastalLandCorrectionProjectedVertices =
                    AllocateProjectedVertices(coastalLandCorrectionSource),
                CoastalWaterCorrectionProjectedVertices =
                    AllocateProjectedVertices(coastalWaterCorrectionSource),
                ContourProjectedVertices = AllocateProjectedVertices(contourSource),
                CoastlineProjectedVertices = AllocateProjectedVertices(coastlineSource),
                SouthLatitudeDeg = result.SouthLatitudeDeg,
                NorthLatitudeDeg = result.NorthLatitudeDeg,
                WestLongitudeDeg = result.WestLongitudeDeg,
                EastLongitudeDeg = result.EastLongitudeDeg,
                BoundCenterLatitudeDeg = boundCenterLatitudeDeg,
                BoundCenterLongitudeDeg = boundCenterLongitudeDeg,
                BoundAngularRadiusRad = boundAngularRadiusRad,
                LandElevationMeters = land.Elevation.ToArray(),
                LandShade = land.Shade.ToArray(),
                LandColours = new Color32[land.Vertices.Count],
                CoastalLandCorrectionElevationMeters =
                    result.CoastalLandCorrectionElevationMeters == null ? null :
                    (float[])result.CoastalLandCorrectionElevationMeters.Clone(),
                CoastalLandCorrectionShade =
                    result.CoastalLandCorrectionShade == null ? null :
                    (byte[])result.CoastalLandCorrectionShade.Clone(),
                CoastalLandCorrectionColours = coastalLandCorrectionSource == null ? null :
                    new Color32[coastalLandCorrectionSource.Length],
                Resolution = result.Resolution,
                CoastlineResolution = result.CoastlineResolution,
                CoastalCorrectionParentCells = result.CoastalCorrectionParentCells,
                Valid = (byte[])result.Valid.Clone(),
                // Water meshes are created with the frozen Standard water colour. Mark that
                // fact so the first Standard draw does not allocate and upload an identical
                // colour array. Non-Standard presets still update through the existing path.
                WaterColourPreset = AERISTerrainColourPreset.Standard,
                CoverageFraction = TriangleCoverage(result),
                Bytes = Math.Max(1L, bytes),
                LastUse = 0L
            };
        }

        static SurfacePoint Point(AERISTerrainRenderReadyHeightField result, int index)
        {
            return new SurfacePoint
            {
                X = result.VertexX[index],
                Y = result.VertexY[index],
                ElevationMeters = result.ElevationMeters[index],
                Shade = result.Shade[index],
                Water = result.Water[index] != 0
            };
        }

        static SurfacePoint CoastBoundaryPoint(SurfacePoint a, SurfacePoint b,
            bool water)
        {
            float t = AERISTerrainCoastlinePolicy.CrossingFraction(a.Water, b.Water,
                a.ElevationMeters, b.ElevationMeters);
            return new SurfacePoint
            {
                X = a.X + (b.X - a.X) * t,
                Y = a.Y + (b.Y - a.Y) * t,
                ElevationMeters = a.ElevationMeters +
                    (b.ElevationMeters - a.ElevationMeters) * t,
                Shade = (byte)Mathf.Clamp(Mathf.RoundToInt(a.Shade +
                    (b.Shade - a.Shade) * t), 0, 255),
                Water = water
            };
        }

        static void AppendClippedTriangle(SurfaceBuilder builder,
            SurfacePoint[] output, SurfacePoint a, SurfacePoint b, SurfacePoint c,
            bool targetWater)
        {
            int count = 0;
            AppendClippedEdge(output, ref count, a, b, targetWater);
            AppendClippedEdge(output, ref count, b, c, targetWater);
            AppendClippedEdge(output, ref count, c, a, targetWater);
            builder.AddPolygon(output, count);
        }

        static void AppendClippedEdge(SurfacePoint[] output, ref int count,
            SurfacePoint current, SurfacePoint next, bool targetWater)
        {
            bool currentInside = current.Water == targetWater;
            bool nextInside = next.Water == targetWater;
            if (currentInside) output[count++] = current;
            if (currentInside != nextInside)
                output[count++] = CoastBoundaryPoint(current, next, targetWater);
        }

        Mesh BuildSurfaceMesh(string name, SurfaceBuilder builder, bool water,
            out Vector3[] sourceVertices)
        {
            sourceVertices = null;
            if (builder == null || builder.Vertices.Count < 3 ||
                builder.Triangles.Count < 3) return null;
            var colours = new Color32[builder.Vertices.Count];
            Color32 initial = water ?
                ResolveWaterColour(AERISTerrainColourPreset.Standard) :
                new Color32(255, 255, 255, 255);
            for (int i = 0; i < colours.Length; i++) colours[i] = initial;
            Mesh mesh = AcquireMesh(name, builder.Vertices.Count);
            sourceVertices = builder.Vertices.ToArray();
            mesh.vertices = sourceVertices;
            mesh.colors32 = colours;
            mesh.triangles = builder.Triangles.ToArray();
            // ND geometry is rendered in normalized presentation space. Use one conservative
            // bound instead of rescanning every projected vertex on each map recenter.
            mesh.bounds = NdPresentationBounds;
            // Colours and geographic projection are updated in flight; retain CPU access.
            mesh.UploadMeshData(false);
            return mesh;
        }

        Mesh BuildTriangleListMesh(string name, float[] xy,
            bool water, out Vector3[] sourceVertices)
        {
            sourceVertices = null;
            if (xy == null || xy.Length < 6 || (xy.Length & 1) != 0) return null;
            int vertexCount = xy.Length / 2;
            if (vertexCount % 3 != 0) return null;
            sourceVertices = new Vector3[vertexCount];
            int[] indices = GetIdentityIndices(vertexCount);
            var colours = new Color32[vertexCount];
            Color32 initial = water ? ResolveWaterColour(AERISTerrainColourPreset.Standard) :
                new Color32(255, 255, 255, 255);
            for (int i = 0; i < vertexCount; i++)
            {
                sourceVertices[i] = new Vector3(xy[i * 2], xy[i * 2 + 1], 0f);
                colours[i] = initial;
            }
            Mesh mesh = AcquireMesh(name, vertexCount);
            mesh.vertices = sourceVertices;
            mesh.colors32 = colours;
            mesh.triangles = indices;
            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            return mesh;
        }

        Mesh BuildLineMesh(string name, float[] segments, Color32 colour,
            out Vector3[] sourceVertices)
        {
            sourceVertices = null;
            if (segments == null || segments.Length < 4 || segments.Length % 4 != 0)
                return null;
            int vertexCount = segments.Length / 2;
            var vertices = new Vector3[vertexCount];
            int[] indices = GetIdentityIndices(vertexCount);
            var colours = new Color32[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(segments[i * 2], segments[i * 2 + 1], 0f);
                colours[i] = colour;
            }
            Mesh mesh = AcquireMesh(name, vertexCount);
            sourceVertices = vertices;
            mesh.vertices = sourceVertices;
            mesh.colors32 = colours;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            return mesh;
        }

        int[] GetIdentityIndices(int vertexCount)
        {
            vertexCount = Math.Max(0, vertexCount);
            int[] indices;
            if (identityIndexCache.TryGetValue(vertexCount, out indices))
            {
                operationHealthIdentityIndexHits++;
                return indices;
            }
            indices = new int[vertexCount];
            for (int i = 0; i < vertexCount; i++) indices[i] = i;
            identityIndexCache[vertexCount] = indices;
            operationHealthIdentityIndexMisses++;
            return indices;
        }

        Color32[] GetUniformColourScratch(int vertexCount, Color32 colour)
        {
            vertexCount = Math.Max(0, vertexCount);
            Color32[] colours;
            if (!uniformColourScratch.TryGetValue(vertexCount, out colours))
            {
                colours = new Color32[vertexCount];
                uniformColourScratch[vertexCount] = colours;
            }
            else operationHealthUniformColourReuses++;
            for (int i = 0; i < colours.Length; i++) colours[i] = colour;
            return colours;
        }

        Mesh AcquireMesh(string name, int vertexCount)
        {
            Mesh mesh = null;
            while (meshPool.Count > 0 && mesh == null)
                mesh = meshPool.Dequeue();
            if (mesh == null)
            {
                mesh = new Mesh();
                operationHealthMeshPoolMisses++;
            }
            else
            {
                mesh.Clear();
                operationHealthMeshPoolHits++;
            }
            mesh.name = name ?? "AERIS_TERRAIN_MESH";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.indexFormat = vertexCount > 65535 ?
                UnityEngine.Rendering.IndexFormat.UInt32 :
                UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.MarkDynamic();
            return mesh;
        }

        void RecycleMesh(ref Mesh mesh)
        {
            if (mesh == null) return;
            Mesh value = mesh;
            mesh = null;
            if (!disposed && meshPool.Count < MaximumPooledMeshes)
            {
                try
                {
                    value.Clear();
                    value.name = "AERIS_TERRAIN_MESH_POOL";
                    meshPool.Enqueue(value);
                    operationHealthMeshPoolRecycles++;
                    return;
                }
                catch { }
            }
            DestroyUnityObject(value);
            operationHealthMeshPoolDestroys++;
        }

        void DestroyMeshPool()
        {
            while (meshPool.Count > 0)
            {
                Mesh mesh = meshPool.Dequeue();
                DestroyUnityObject(mesh);
                operationHealthMeshPoolDestroys++;
            }
        }

        static Vector3[] AllocateProjectedVertices(Vector3[] sourceVertices)
        {
            return sourceVertices == null ? null : new Vector3[sourceVertices.Length];
        }

        static GeographicUnitPoint[] BuildGeographicPoints(Vector3[] sourceVertices,
            double southLatitudeDeg, double northLatitudeDeg,
            double westLongitudeDeg, double eastLongitudeDeg)
        {
            if (sourceVertices == null || sourceVertices.Length == 0) return null;
            var output = new GeographicUnitPoint[sourceVertices.Length];
            double latitudeSpan = northLatitudeDeg - southLatitudeDeg;
            double longitudeSpan = PositiveLongitudeSpan(westLongitudeDeg,
                eastLongitudeDeg);
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                double latitudeDeg = southLatitudeDeg +
                    latitudeSpan * sourceVertices[i].y;
                double longitudeDeg = NormalizeLongitude(westLongitudeDeg +
                    longitudeSpan * sourceVertices[i].x);
                double latitudeRad = latitudeDeg * Math.PI / 180.0;
                double longitudeRad = longitudeDeg * Math.PI / 180.0;
                double cosineLatitude = Math.Cos(latitudeRad);
                output[i] = new GeographicUnitPoint
                {
                    X = cosineLatitude * Math.Cos(longitudeRad),
                    Y = cosineLatitude * Math.Sin(longitudeRad),
                    Z = Math.Sin(latitudeRad)
                };
            }
            return output;
        }

        void EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters,
            double currentCenterLatitudeDeg, double currentCenterLongitudeDeg,
            bool forceCenterProjectionRefresh)
        {
            if (entry == null) return;
            bool projectionChanged = forceCenterProjectionRefresh ||
                double.IsNaN(entry.LastProjectionCenterLatitudeDeg) ||
                Math.Abs(entry.LastProjectionBodyRadius - context.RadiusMeters) > 0.01 ||
                Math.Abs(entry.LastProjectionRangeMeters - context.VerticalMeters) > 0.01 ||
                Math.Abs(entry.LastProjectionAnchorBottom - context.AnchorRenderV) > 0.000001f ||
                entry.LastProjectionOrientation != context.Orientation;
            if (!projectionChanged)
            {
                double east, north;
                ToLocalMeters(context.RadiusMeters,
                    entry.LastProjectionCenterLatitudeDeg,
                    entry.LastProjectionCenterLongitudeDeg,
                    currentCenterLatitudeDeg, currentCenterLongitudeDeg,
                    out east, out north);
                projectionChanged = east * east + north * north >=
                    movementThresholdMeters * movementThresholdMeters;
            }
            if (!projectionChanged) return;

            ProjectMesh(entry.LandMesh, entry.LandGeographicPoints,
                entry.LandProjectedVertices, context);
            ProjectMesh(entry.WaterMesh, entry.WaterGeographicPoints,
                entry.WaterProjectedVertices, context);
            ProjectMesh(entry.CoastalLandCorrectionMesh,
                entry.CoastalLandCorrectionGeographicPoints,
                entry.CoastalLandCorrectionProjectedVertices, context);
            ProjectMesh(entry.CoastalWaterCorrectionMesh,
                entry.CoastalWaterCorrectionGeographicPoints,
                entry.CoastalWaterCorrectionProjectedVertices, context);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,
                entry.ContourProjectedVertices, context);
            ProjectMesh(entry.CoastlineMesh, entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices, context);
            entry.LastProjectionCenterLatitudeDeg = currentCenterLatitudeDeg;
            entry.LastProjectionCenterLongitudeDeg = currentCenterLongitudeDeg;
            entry.LastProjectionBodyRadius = context.RadiusMeters;
            entry.LastProjectionRangeMeters = (float)context.VerticalMeters;
            entry.LastProjectionAnchorBottom = context.AnchorRenderV;
            entry.LastProjectionOrientation = context.Orientation;
        }

        void ProjectMesh(Mesh mesh, GeographicUnitPoint[] points,
            Vector3[] projectedVertices, AERISNdMapProjection context)
        {
            if (mesh == null || points == null || projectedVertices == null ||
                points.Length != projectedVertices.Length) return;
            for (int i = 0; i < points.Length; i++)
            {
                GeographicUnitPoint point = points[i];
                float u, v;
                context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                    out u, out v);
                projectedVertices[i] = new Vector3(u, v, 0f);
            }
            mesh.vertices = projectedVertices;
            operationHealthBoundsSkips++;
        }

        static double UnitLatitude(double x, double y, double z)
        {
            return Math.Atan2(z, Math.Sqrt(x * x + y * y)) * 180.0 / Math.PI;
        }

        static double UnitLongitude(double x, double y)
        {
            return Math.Atan2(y, x) * 180.0 / Math.PI;
        }

        static float TriangleCoverage(AERISTerrainRenderReadyHeightField result)
        {
            if (result == null || result.Resolution < 2 ||
                result.Triangles == null) return 0f;
            long maximum = (long)(result.Resolution - 1) *
                (result.Resolution - 1) * 6L;
            return Mathf.Clamp01(result.Triangles.LongLength /
                (float)Math.Max(1L, maximum));
        }

        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || (entry.LandMesh == null && entry.WaterMesh == null))
                return false;
            EnsureLandColours(entry, mode, preset, aircraftAltitudeAslMeters);
            EnsureWaterColour(entry, preset);
            bool rendered = false;
            int terrainMeshCount = (entry.WaterMesh == null ? 0 : 1) +
                (entry.LandMesh == null ? 0 : 1) +
                (entry.CoastalWaterCorrectionMesh == null ? 0 : 1) +
                (entry.CoastalLandCorrectionMesh == null ? 0 : 1);
            if (terrainMeshCount > 0 && terrainMaterial.SetPass(0))
            {
                // Candidate8 painter order is unchanged: base water, base land, sparse
                // coastal water, sparse coastal land. Pass 3 only removes redundant
                // Material.SetPass calls between meshes using the identical material.
                if (entry.WaterMesh != null) Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);
                if (entry.LandMesh != null) Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);
                if (entry.CoastalWaterCorrectionMesh != null)
                    Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);
                if (entry.CoastalLandCorrectionMesh != null)
                    Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);
                rendered = true;
                operationHealthTerrainSetPassSaved += Math.Max(0, terrainMeshCount - 1);
            }
            if (drawContours && entry.ContourMesh != null &&
                contourMaterial.SetPass(0))
                Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);
            if (entry.CoastlineMesh != null && coastlineMaterial.SetPass(0))
                Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);
            return rendered;
        }

        void EnsureWaterColour(Entry entry,
            AERISTerrainColourPreset preset)
        {
            if (entry == null || entry.WaterColourPreset == preset) return;
            Color32 colour = ResolveWaterColour(preset);
            ApplyUniformMeshColour(entry.WaterMesh, colour);
            ApplyUniformMeshColour(entry.CoastalWaterCorrectionMesh, colour);
            entry.WaterColourPreset = preset;
        }

        void ApplyUniformMeshColour(Mesh mesh, Color32 colour)
        {
            if (mesh == null || mesh.vertexCount <= 0) return;
            mesh.colors32 = GetUniformColourScratch(mesh.vertexCount, colour);
        }

        static void EnsureLandColours(Entry entry, AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset, float aircraftAltitudeAslMeters)
        {
            if (entry == null || entry.LandMesh == null ||
                entry.LandElevationMeters == null || entry.LandShade == null) return;
            int altitudeBucket = mode == AERISTerrainDisplayMode.Relative ?
                Mathf.RoundToInt(aircraftAltitudeAslMeters / RelativeAltitudeBucketMeters) :
                int.MinValue;
            if (entry.ColourMode == mode && entry.ColourPreset == preset &&
                entry.RelativeAltitudeBucket == altitudeBucket) return;
            if (entry.LandColours == null ||
                entry.LandColours.Length != entry.LandElevationMeters.Length)
                entry.LandColours = new Color32[entry.LandElevationMeters.Length];
            float quantizedAltitude = mode == AERISTerrainDisplayMode.Relative ?
                altitudeBucket * RelativeAltitudeBucketMeters : aircraftAltitudeAslMeters;
            for (int i = 0; i < entry.LandColours.Length; i++)
            {
                Color32 baseColour = ResolveLandColour(mode, preset,
                    entry.LandElevationMeters[i], quantizedAltitude);
                entry.LandColours[i] = ApplyShade(baseColour, entry.LandShade[i], mode);
            }
            entry.LandMesh.colors32 = entry.LandColours;
            if (entry.CoastalLandCorrectionMesh != null &&
                entry.CoastalLandCorrectionElevationMeters != null &&
                entry.CoastalLandCorrectionShade != null)
            {
                int count = entry.CoastalLandCorrectionElevationMeters.Length;
                if (entry.CoastalLandCorrectionColours == null ||
                    entry.CoastalLandCorrectionColours.Length != count)
                    entry.CoastalLandCorrectionColours = new Color32[count];
                for (int i = 0; i < count; i++)
                {
                    Color32 baseColour = ResolveLandColour(mode, preset,
                        entry.CoastalLandCorrectionElevationMeters[i], quantizedAltitude);
                    byte shade = i < entry.CoastalLandCorrectionShade.Length ?
                        entry.CoastalLandCorrectionShade[i] : (byte)255;
                    entry.CoastalLandCorrectionColours[i] =
                        ApplyShade(baseColour, shade, mode);
                }
                entry.CoastalLandCorrectionMesh.colors32 =
                    entry.CoastalLandCorrectionColours;
            }
            entry.ColourMode = mode;
            entry.ColourPreset = preset;
            entry.RelativeAltitudeBucket = altitudeBucket;
        }

        void ResolveRenderableEntries(AERISTerrainHeightTile tile, string cacheKey,
            string styleKey, out Entry fallback, out Entry current)
        {
            fallback = null;
            current = null;
            if (tile == null || string.IsNullOrEmpty(cacheKey)) return;
            operationHealthResolveCalls++;
            Entry exact;
            if (entries.TryGetValue(cacheKey, out exact) && exact != null &&
                (exact.LandMesh != null || exact.WaterMesh != null)) current = exact;

            List<Entry> bucket;
            if (!entriesByTile.TryGetValue(tile.Key, out bucket) || bucket == null)
            {
                if (current != null && current.CoverageFraction >= 0.999f)
                    fallback = null;
                return;
            }
            operationHealthResolveCandidates += bucket.Count;
            for (int bucketIndex = 0; bucketIndex < bucket.Count; bucketIndex++)
            {
                Entry candidate = bucket[bucketIndex];
                if (candidate == null || candidate.LandMesh == null && candidate.WaterMesh == null ||
                    ReferenceEquals(candidate, current) ||
                    !candidate.TileKey.Equals(tile.Key)) continue;
                bool candidateCurrentStyle = string.Equals(candidate.StyleKey,
                    styleKey, StringComparison.Ordinal);
                bool fallbackCurrentStyle = fallback != null &&
                    string.Equals(fallback.StyleKey, styleKey,
                        StringComparison.Ordinal);
                if (fallback == null ||
                    candidate.CoverageFraction > fallback.CoverageFraction + 0.0001f ||
                    (Math.Abs(candidate.CoverageFraction -
                        fallback.CoverageFraction) <= 0.0001f &&
                        (candidateCurrentStyle && !fallbackCurrentStyle ||
                        (candidateCurrentStyle == fallbackCurrentStyle &&
                        (candidate.Resolution > fallback.Resolution ||
                        (candidate.Resolution == fallback.Resolution &&
                        candidate.TileCreatedUtcTicks >
                            fallback.TileCreatedUtcTicks))))))
                    fallback = candidate;
            }
            if (current != null && current.CoverageFraction >= 0.999f)
                fallback = null;
        }

        void AddEntry(Entry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.CacheKey)) return;
            entries[entry.CacheKey] = entry;
            List<Entry> bucket;
            if (!entriesByTile.TryGetValue(entry.TileKey, out bucket) || bucket == null)
            {
                bucket = new List<Entry>(4);
                entriesByTile[entry.TileKey] = bucket;
            }
            if (!bucket.Contains(entry)) bucket.Add(entry);
        }

        AERISTerrainHeightTile[] PrepareSortedTileScratch(AERISTerrainHeightTile[] source)
        {
            if (source == null || source.Length == 0) return new AERISTerrainHeightTile[0];
            if (sortedTilesScratch == null || sortedTilesScratch.Length != source.Length)
            {
                sortedTilesScratch = new AERISTerrainHeightTile[source.Length];
                operationHealthTileScratchResizes++;
            }
            Array.Copy(source, sortedTilesScratch, source.Length);
            Array.Sort(sortedTilesScratch, CompareTilesCoarseFirst);
            return sortedTilesScratch;
        }

        void EnsureEntryScratch(int count)
        {
            count = Math.Max(0, count);
            if (fallbackEntriesScratch != null && fallbackEntriesScratch.Length == count &&
                currentEntriesScratch != null && currentEntriesScratch.Length == count &&
                drawEntriesScratch != null && drawEntriesScratch.Length == count) return;
            fallbackEntriesScratch = new Entry[count];
            currentEntriesScratch = new Entry[count];
            drawEntriesScratch = new Entry[count];
        }

        void EnsureResources(Rect plot, AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset,
            AERISTerrainVirtualDetailProfile virtualDetail)
        {
            EnsureMaterials(preset);
            EnsureRenderTarget(plot, virtualDetail);
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null) runtime.Gpu.RegisterGraphicsAssist(GraphicsAssistName);
        }

        void EnsureMaterials(AERISTerrainColourPreset preset)
        {
            if (terrainMaterial == null || contourMaterial == null ||
                coastlineMaterial == null)
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Unlit/Transparent");
                if (shader == null) shader = Shader.Find("UI/Default");
                if (shader == null) throw new InvalidOperationException(
                    "No compatible built-in vertex-colour shader");

                terrainMaterial = new Material(shader);
                terrainMaterial.name = "AERIS_TERRAIN_VERTEX_COLOUR_MATERIAL";
                terrainMaterial.hideFlags = HideFlags.HideAndDontSave;
                terrainMaterial.mainTexture = Texture2D.whiteTexture;
                terrainMaterial.color = Color.white;

                contourMaterial = new Material(shader);
                contourMaterial.name = "AERIS_TERRAIN_CONTOUR_MATERIAL";
                contourMaterial.hideFlags = HideFlags.HideAndDontSave;
                contourMaterial.mainTexture = Texture2D.whiteTexture;

                coastlineMaterial = new Material(shader);
                coastlineMaterial.name = "AERIS_TERRAIN_COASTLINE_MATERIAL";
                coastlineMaterial.hideFlags = HideFlags.HideAndDontSave;
                coastlineMaterial.mainTexture = Texture2D.whiteTexture;
            }
            terrainMaterial.color = Color.white;
            contourMaterial.color = ResolveContourColour(preset);
            coastlineMaterial.color = Color.white;
        }



        void EnsureRenderTarget(Rect plot,
            AERISTerrainVirtualDetailProfile virtualDetail)
        {
            float scale = virtualDetail == null ? 1f :
                virtualDetail.RenderTargetScale;
            int width = Mathf.Clamp(Mathf.CeilToInt(plot.width * scale), 128, 1024);
            int height = Mathf.Clamp(Mathf.CeilToInt(plot.height * scale), 128, 1024);
            if (backTarget != null && frontTarget != null &&
                backTarget.width == width && backTarget.height == height &&
                frontTarget.width == width && frontTarget.height == height &&
                backTarget.IsCreated() && frontTarget.IsCreated()) return;

            // AERIS23 Resize Cadence Guard: render-target reallocation is a
            // presentation-resource event, not a new authoritative-clock bootstrap.
            // Preserve the fixed 10 Hz deadlines and Step 2 content snapshot while
            // invalidating only the destroyed FRONT/BACK presentation resources.
            DestroyRenderTargets(true);
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32))
                throw new InvalidOperationException("ARGB32 RenderTexture unsupported");
            backTarget = new RenderTexture(width, height, 0,
                RenderTextureFormat.ARGB32);
            backTarget.name = "AERIS_ND_TERRAIN_BACK";
            backTarget.hideFlags = HideFlags.HideAndDontSave;
            backTarget.filterMode = FilterMode.Bilinear;
            backTarget.wrapMode = TextureWrapMode.Clamp;
            backTarget.useMipMap = false;
            backTarget.autoGenerateMips = false;
            if (!backTarget.Create())
                throw new InvalidOperationException("Terrain RenderTexture.Create failed");
            frontTarget = new RenderTexture(width, height, 0,
                RenderTextureFormat.ARGB32);
            frontTarget.name = "AERIS_ND_TERRAIN_FRONT";
            frontTarget.hideFlags = HideFlags.HideAndDontSave;
            frontTarget.filterMode = FilterMode.Bilinear;
            frontTarget.wrapMode = TextureWrapMode.Clamp;
            frontTarget.useMipMap = false;
            frontTarget.autoGenerateMips = false;
            if (!frontTarget.Create())
                throw new InvalidOperationException(
                    "Terrain front RenderTexture.Create failed");
            backTargetBytes = (long)width * height * 4L;
            frontTargetBytes = (long)width * height * 4L;
            ResetFrontBufferState(true);
        }



        float MeasureViewportCoverage(AERISTerrainHeightTile[] tiles,
            AERISNdMapProjection projection, Matrix4x4 mapRotation,
            string styleKey, bool includeFallback)
        {
            coverageRects.Clear();
            if (tiles == null) return 0f;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null) continue;
                Rect normalized;
                if (!TryTileRectNormalized(tile, projection, out normalized))
                    continue;
                Entry fallbackEntry, currentEntry;
                string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                ResolveRenderableEntries(tile, cacheKey, styleKey, out fallbackEntry,
                    out currentEntry);
                if (includeFallback && fallbackEntry != null)
                    coverageRects.Add(new CoverageRegion
                    {
                        Rect = normalized,
                        Entry = fallbackEntry
                    });
                if (currentEntry != null)
                    coverageRects.Add(new CoverageRegion
                    {
                        Rect = normalized,
                        Entry = currentEntry
                    });
            }
            if (coverageRects.Count == 0) return 0f;

            const int samplesPerAxis = 25;
            int coveredSamples = 0;
            int totalSamples = samplesPerAxis * samplesPerAxis;
            Matrix4x4 inverse = mapRotation.inverse;
            for (int row = 0; row < samplesPerAxis; row++)
            {
                float finalY = (row + 0.5f) / samplesPerAxis;
                for (int column = 0; column < samplesPerAxis; column++)
                {
                    float finalX = (column + 0.5f) / samplesPerAxis;
                    Vector3 source = inverse.MultiplyPoint3x4(
                        new Vector3(finalX, finalY, 0f));
                    float sourceX = source.x;
                    float sourceY = source.y;
                    bool covered = false;
                    for (int i = 0; i < coverageRects.Count; i++)
                    {
                        CoverageRegion region = coverageRects[i];
                        if (region.Entry == null) continue;
                        Rect rect = region.Rect;
                        if (sourceX >= rect.xMin && sourceX <= rect.xMax &&
                            sourceY >= rect.yMin && sourceY <= rect.yMax)
                        {
                            float localX = (sourceX - rect.xMin) /
                                Math.Max(0.000001f, rect.width);
                            float localY = (sourceY - rect.yMin) /
                                Math.Max(0.000001f, rect.height);
                            if (EntryCoversPoint(region.Entry, localX, localY))
                            {
                                covered = true;
                                break;
                            }
                        }
                    }
                    if (covered) coveredSamples++;
                }
            }
            return coveredSamples / (float)Math.Max(1, totalSamples);
        }

        static bool EntryCoversPoint(Entry entry, float localX, float localY)
        {
            if (entry == null || entry.Valid == null || entry.Resolution < 2 ||
                entry.Valid.Length != entry.Resolution * entry.Resolution) return false;
            float scaledX = Mathf.Clamp01(localX) * (entry.Resolution - 1);
            float scaledY = Mathf.Clamp01(localY) * (entry.Resolution - 1);
            int column = Math.Min(entry.Resolution - 2,
                Math.Max(0, Mathf.FloorToInt(scaledX)));
            int row = Math.Min(entry.Resolution - 2,
                Math.Max(0, Mathf.FloorToInt(scaledY)));
            float fractionX = scaledX - column;
            float fractionY = scaledY - row;
            int a = row * entry.Resolution + column;
            int b = a + 1;
            int c = a + entry.Resolution;
            int d = c + 1;
            return fractionX + fractionY <= 1f ?
                entry.Valid[a] != 0 && entry.Valid[b] != 0 && entry.Valid[c] != 0 :
                entry.Valid[b] != 0 && entry.Valid[c] != 0 && entry.Valid[d] != 0;
        }

        static string CacheKey(AERISTerrainTileKey key, long createdUtcTicks,
            string styleKey)
        {
            return key.StableId + "|" +
                createdUtcTicks.ToString(CultureInfo.InvariantCulture) + "|" +
                (styleKey ?? string.Empty);
        }

        string BuildStyleKey(float contourInterval,
            AERISTerrainVirtualDetailProfile virtualDetail)
        {
            string detail = virtualDetail == null ? "FAR DIRECT" :
                virtualDetail.Name;
            return (settings == null || settings.TerrainContoursEnabled ? "C" : "-") +
                (settings == null || settings.TerrainShadingEnabled ? "S" : "-") + "|" +
                contourInterval.ToString("0.###", CultureInfo.InvariantCulture) + "|" +
                detail;
        }

        AERISTerrainVirtualDetailProfile ResolveVirtualDetailProfile(float rangeMeters)
        {
            string quality = performance == null || performance.ActiveProfile == null ?
                "MEDIUM" : performance.ActiveProfile.Name;
            return AERISTerrainVirtualDetailPolicy.Resolve(quality, rangeMeters);
        }

        static float ResolveContourInterval(float rangeMeters)
        {
            return rangeMeters <= 10000f ? 50f :
                rangeMeters <= 40000f ? 100f :
                rangeMeters <= 80000f ? 250f : 500f;
        }

        static AERISTerrainDisplayMode ResolveEffectiveMode(
            AERISTerrainDisplayMode mode, Vessel vessel, float rangeMeters)
        {
            if (mode != AERISTerrainDisplayMode.Automatic) return mode;
            double radar = vessel == null ? double.NaN : vessel.heightFromTerrain;
            if ((!double.IsNaN(radar) && !double.IsInfinity(radar) && radar < 6000.0) ||
                rangeMeters <= 40000f) return AERISTerrainDisplayMode.Relative;
            return AERISTerrainDisplayMode.Topographic;
        }

        static int CompareTilesCoarseFirst(AERISTerrainHeightTile left,
            AERISTerrainHeightTile right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int lod = ((int)left.Key.Lod).CompareTo((int)right.Key.Lod);
            if (lod != 0) return lod;
            return left.CreatedUtcTicks.CompareTo(right.CreatedUtcTicks);
        }

        static bool TryTileRectNormalized(AERISTerrainHeightTile tile,
            AERISNdMapProjection projection, out Rect rect)
        {
            rect = new Rect();
            if (tile == null) return false;
            double[] latitudes = { tile.SouthLatitudeDeg, tile.SouthLatitudeDeg,
                tile.NorthLatitudeDeg, tile.NorthLatitudeDeg };
            double[] longitudes = { tile.WestLongitudeDeg, tile.EastLongitudeDeg,
                tile.WestLongitudeDeg, tile.EastLongitudeDeg };
            float x0 = float.PositiveInfinity, x1 = float.NegativeInfinity;
            float y0 = float.PositiveInfinity, y1 = float.NegativeInfinity;
            for (int i = 0; i < 4; i++)
            {
                double latRad = latitudes[i] * Math.PI / 180.0;
                double lonRad = longitudes[i] * Math.PI / 180.0;
                double cosLat = Math.Cos(latRad);
                float x, y;
                projection.ProjectUnitToRenderNUp(cosLat * Math.Cos(lonRad),
                    cosLat * Math.Sin(lonRad), Math.Sin(latRad), out x, out y);
                x0 = Math.Min(x0, x); x1 = Math.Max(x1, x);
                y0 = Math.Min(y0, y); y1 = Math.Max(y1, y);
            }
            if (x1 < 0f || x0 > 1f || y1 < 0f || y0 > 1f) return false;
            rect = Rect.MinMaxRect(x0, y0, x1, y1);
            return rect.width > 0.000001f && rect.height > 0.000001f;
        }

        void RecordPresentedFrontAlignmentDiagnostic(Rect plot,
            AERISTerrainHeightTile[] tiles, Vessel vessel,
            AERISTerrainDisplayMode effectiveMode, AERISTerrainColourPreset preset,
            AERISNdMapLockReference lockReference)
        {
            if (!frontBufferValid || vessel == null || vessel.mainBody == null) return;
            AERISNdMapProjection frontProjection = AERISNdMapProjection.Create(
                vessel.mainBody, frontCenterLatitudeDeg, frontCenterLongitudeDeg,
                frontRangeMeters, frontMapHeadingDeg, frontTrackUp, frontAnchorV,
                frontOrientation);
            Matrix4x4 frontMapRotation =
                frontProjection.ResolveScaleCorrectedRenderMatrix();
            RecordAlignmentDiagnostic(plot, tiles, vessel, frontProjection,
                frontMapRotation, frontCenterLatitudeDeg, frontCenterLongitudeDeg,
                frontRangeMeters, frontMapHeadingDeg, frontTrackUp, frontAnchorV,
                1f - Mathf.Clamp01(frontAnchorV), frontOrientation, effectiveMode,
                preset, lockReference);
        }

        void RecordAlignmentDiagnostic(Rect plot, AERISTerrainHeightTile[] tiles,
            Vessel vessel, AERISNdMapProjection projection, Matrix4x4 mapRotation,
            double centerLatitudeDeg, double centerLongitudeDeg, float rangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV, float anchorBottom,
            AERISTerrainRenderTargetOrientation orientation,
            AERISTerrainDisplayMode effectiveMode, AERISTerrainColourPreset preset,
            AERISNdMapLockReference lockReference)
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextAlignmentLogRealtime) return;
            nextAlignmentLogRealtime = now + 2f;

            string tileText = "NONE";
            float sourceX, sourceY;
            double centerLatRad = centerLatitudeDeg * Math.PI / 180.0;
            double centerLonRad = centerLongitudeDeg * Math.PI / 180.0;
            double centerCosLat = Math.Cos(centerLatRad);
            projection.ProjectUnitToRenderNUp(centerCosLat * Math.Cos(centerLonRad),
                centerCosLat * Math.Sin(centerLonRad), Math.Sin(centerLatRad),
                out sourceX, out sourceY);
            Vector3 rotatedCenter = mapRotation.MultiplyPoint3x4(
                new Vector3(sourceX, sourceY, 0f));
            sourceX = rotatedCenter.x;
            sourceY = rotatedCenter.y;
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null || centerLatitudeDeg < tile.SouthLatitudeDeg - 1e-9 ||
                        centerLatitudeDeg > tile.NorthLatitudeDeg + 1e-9 ||
                        !LongitudeInside(centerLongitudeDeg, tile.WestLongitudeDeg,
                            tile.EastLongitudeDeg)) continue;
                    tileText = tile.Key.FileStem + " bounds=" +
                        tile.SouthLatitudeDeg.ToString("F6", CultureInfo.InvariantCulture) +
                        ".." + tile.NorthLatitudeDeg.ToString("F6",
                        CultureInfo.InvariantCulture) + "," +
                        tile.WestLongitudeDeg.ToString("F6", CultureInfo.InvariantCulture) +
                        ".." + tile.EastLongitudeDeg.ToString("F6",
                        CultureInfo.InvariantCulture);
                    break;
                }
            }
            float presentedGuiU, presentedGuiV;
            projection.PresentRenderToGui(sourceX, sourceY,
                out presentedGuiU, out presentedGuiV);
            float deltaPixelsX = (presentedGuiU - 0.5f) * plot.width;
            float deltaPixelsY = (presentedGuiV - anchorV) * plot.height;
            lastRunwayMapLockErrorPixels = MeasureRunwayMapLockError(plot,
                projection, mapRotation, lockReference);
            string graphicsType = SystemInfo.graphicsDeviceType.ToString();
            AERISLogger.Info("[ND/TERRAIN_ALIGN] orientation=" + orientation +
                "; graphics=" + graphicsType +
                "; graphicsUVStartsAtTop=" + SystemInfo.graphicsUVStartsAtTop +
                "; center=" + centerLatitudeDeg.ToString("F7",
                    CultureInfo.InvariantCulture) + "," +
                    centerLongitudeDeg.ToString("F7", CultureInfo.InvariantCulture) +
                "; vessel=" + (vessel == null ? "NONE" :
                    vessel.latitude.ToString("F7", CultureInfo.InvariantCulture) + "," +
                    vessel.longitude.ToString("F7", CultureInfo.InvariantCulture)) +
                "; range=" + rangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; heading=" + mapHeadingDeg.ToString("F2",
                    CultureInfo.InvariantCulture) +
                "; trackUp=" + trackUp +
                "; colourSource=EXPLICIT_VERTEX" +
                "; geometryProjection=SHARED_SCALE_CORRECTED" +
                "; effectiveMode=" + effectiveMode +
                "; preset=" + preset +
                "; aircraftAsl=" + (vessel == null ? "N/A" :
                    vessel.altitude.ToString("F1", CultureInfo.InvariantCulture)) +
                "; water=FIXED_BLUE" +
                "; anchorGui=" + anchorV.ToString("F3", CultureInfo.InvariantCulture) +
                "; sourceRT=" + sourceX.ToString("F3", CultureInfo.InvariantCulture) +
                    "," + sourceY.ToString("F3", CultureInfo.InvariantCulture) +
                "; presentedGui=" + presentedGuiU.ToString("F3",
                    CultureInfo.InvariantCulture) + "," +
                    presentedGuiV.ToString("F3", CultureInfo.InvariantCulture) +
                "; deltaPx=" + deltaPixelsX.ToString("F1",
                    CultureInfo.InvariantCulture) + "," +
                    deltaPixelsY.ToString("F1", CultureInfo.InvariantCulture) +
                "; runwayMapLockErrorPx=" + lastRunwayMapLockErrorPixels.ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; visualCoverage=" + lastVisualCoverageFraction.ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; requestedCoverage=" + lastCoverageFraction.ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; tile=" + tileText + ".");
        }

        static float MeasureRunwayMapLockError(Rect plot,
            AERISNdMapProjection projection, Matrix4x4 mapRotation,
            AERISNdMapLockReference reference)
        {
            if (reference == null) return 0f;
            float maximum = 0f;
            maximum = Math.Max(maximum, MeasureProjectionPathError(plot, projection,
                mapRotation, reference.LatitudeADeg, reference.LongitudeADeg));
            maximum = Math.Max(maximum, MeasureProjectionPathError(plot, projection,
                mapRotation, reference.LatitudeBDeg, reference.LongitudeBDeg));
            return maximum;
        }

        static float MeasureProjectionPathError(Rect plot,
            AERISNdMapProjection projection, Matrix4x4 mapRotation,
            double latitudeDeg, double longitudeDeg)
        {
            float guiU, guiV;
            projection.ProjectLatitudeLongitudeToGui(latitudeDeg, longitudeDeg,
                out guiU, out guiV);
            double latRad = latitudeDeg * Math.PI / 180.0;
            double lonRad = longitudeDeg * Math.PI / 180.0;
            double cosLat = Math.Cos(latRad);
            float renderU, renderV;
            projection.ProjectUnitToRenderNUp(cosLat * Math.Cos(lonRad),
                cosLat * Math.Sin(lonRad), Math.Sin(latRad), out renderU,
                out renderV);
            Vector3 rotated = mapRotation.MultiplyPoint3x4(
                new Vector3(renderU, renderV, 0f));
            float presentedU, presentedV;
            projection.PresentRenderToGui(rotated.x, rotated.y,
                out presentedU, out presentedV);
            float dx = (presentedU - guiU) * plot.width;
            float dy = (presentedV - guiV) * plot.height;
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        static bool LongitudeInside(double longitudeDeg, double westDeg,
            double eastDeg)
        {
            double span = PositiveLongitudeSpan(westDeg, eastDeg);
            double offset = PositiveLongitudeSpan(westDeg, longitudeDeg);
            return offset <= span + 1e-9;
        }

        static double PositiveLongitudeSpan(double fromDeg, double toDeg)
        {
            double span = NormalizeLongitude(toDeg - fromDeg);
            if (span < 0.0) span += 360.0;
            return span;
        }

        static void ToLocalMeters(CelestialBody body, double originLatDeg,
            double originLonDeg, double targetLatDeg, double targetLonDeg,
            out double eastMeters, out double northMeters)
        {
            eastMeters = northMeters = 0.0;
            if (body == null) return;
            ToLocalMeters(body.Radius, originLatDeg, originLonDeg,
                targetLatDeg, targetLonDeg, out eastMeters, out northMeters);
        }

        static void ToLocalMeters(double radiusMeters, double originLatDeg,
            double originLonDeg, double targetLatDeg, double targetLonDeg,
            out double eastMeters, out double northMeters)
        {
            eastMeters = northMeters = 0.0;
            double lat1 = originLatDeg * Math.PI / 180.0;
            double lat2 = targetLatDeg * Math.PI / 180.0;
            double dLon = NormalizeLongitude(targetLonDeg - originLonDeg) *
                Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double bearing = Math.Atan2(y, x);
            double dLat = lat2 - lat1;
            double a = Math.Sin(dLat * 0.5) * Math.Sin(dLat * 0.5) +
                Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);
            double angle = 2.0 * Math.Atan2(Math.Sqrt(Math.Max(0.0, a)),
                Math.Sqrt(Math.Max(0.0, 1.0 - a)));
            double distance = Math.Max(1000.0, radiusMeters) * angle;
            eastMeters = Math.Sin(bearing) * distance;
            northMeters = Math.Cos(bearing) * distance;
        }

        static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        long ResolveVramLimitBytes()
        {
            int configured = settings == null ? 0 : settings.TerrainVramCacheLimitMiB;
            if (configured > 0) return configured * 1024L * 1024L;
            int value = performance == null ? 96 :
                performance.ActiveProfile.DefaultVramCacheMiB;
            return value * 1024L * 1024L;
        }

        void Prune(long totalLimit)
        {
            totalLimit = Math.Max(16L * 1024L * 1024L, totalLimit);
            long fixedBytes = Math.Max(0L, backTargetBytes) +
                Math.Max(0L, frontTargetBytes);
            long entryLimit = Math.Max(4L * 1024L * 1024L, totalLimit - fixedBytes);
            while (usedEntryBytes > entryLimit && entries.Count > 1)
            {
                Entry oldest = null;
                foreach (Entry entry in entries.Values)
                {
                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;
                }
                if (oldest == null) break;
                Remove(oldest);
                evicted++;
            }
        }

        void Remove(Entry entry)
        {
            if (entry == null) return;
            entries.Remove(entry.CacheKey);
            List<Entry> bucket;
            if (entriesByTile.TryGetValue(entry.TileKey, out bucket) && bucket != null)
            {
                bucket.Remove(entry);
                if (bucket.Count == 0) entriesByTile.Remove(entry.TileKey);
            }
            if (entry.CoastlineResolution >=
                AERISTerrainCoastlineExtractor.HighDensityResolution)
                highDensityCoastlineEntries = Math.Max(0, highDensityCoastlineEntries - 1);
            if (entry.CoastalCorrectionParentCells > 0)
            {
                sparseCoastalCorrectionEntries = Math.Max(0,
                    sparseCoastalCorrectionEntries - 1);
                sparseCoastalCorrectionParentCells = Math.Max(0L,
                    sparseCoastalCorrectionParentCells -
                    entry.CoastalCorrectionParentCells);
            }
            usedEntryBytes = Math.Max(0L,
                usedEntryBytes - Math.Max(0L, entry.Bytes));
            RecycleMesh(ref entry.LandMesh);
            RecycleMesh(ref entry.WaterMesh);
            RecycleMesh(ref entry.CoastalLandCorrectionMesh);
            RecycleMesh(ref entry.CoastalWaterCorrectionMesh);
            RecycleMesh(ref entry.ContourMesh);
            RecycleMesh(ref entry.CoastlineMesh);
            AERISTerrainRenderReadyHeightField field;
            if (renderReadyFields.TryGetValue(entry.CacheKey, out field) &&
                field != null && field.ResidentTokenValid && residentCache != null)
                residentCache.TryDemotePresentationState(field.ResidentToken,
                    AERISResidentTileState.RenderReady);
        }

        void FailGpuTerrain(Exception ex)
        {
            gpuFailed = true;
            uploadFailures++;
            fault = ex == null ? "GPU TERRAIN FAILURE" :
                ex.GetType().Name + ": " + ex.Message;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Gpu.ReportGraphicsAssistFailure(GraphicsAssistName, fault);
        }

        static Color32 ResolveLandColour(AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset, float terrainAltitudeMeters,
            float aircraftAltitudeAslMeters)
        {
            if (mode == AERISTerrainDisplayMode.Relative)
                return ResolveRelativeLandColour(preset,
                    aircraftAltitudeAslMeters - terrainAltitudeMeters);
            float t = Mathf.Clamp01((terrainAltitudeMeters + 500f) / 12500f);
            return ResolveTopographicLandColour(preset, t);
        }

        static Color32 ResolveRelativeLandColour(AERISTerrainColourPreset preset,
            float clearanceMeters)
        {
            if (clearanceMeters <= 30f)
            {
                if (preset == AERISTerrainColourPreset.RedGreenAssist)
                    return new Color32(190, 45, 210, 255);
                return new Color32(224, 31, 20, 255);
            }
            if (clearanceMeters <= 300f)
                return preset == AERISTerrainColourPreset.BlueYellowAssist ?
                    new Color32(242, 235, 225, 255) :
                    new Color32(235, 184, 20, 255);
            if (clearanceMeters <= 600f)
            {
                if (preset == AERISTerrainColourPreset.RedGreenAssist)
                    return new Color32(35, 105, 210, 255);
                if (preset == AERISTerrainColourPreset.HighContrast)
                    return new Color32(70, 235, 70, 255);
                return new Color32(51, 122, 41, 255);
            }
            if (preset == AERISTerrainColourPreset.RedGreenAssist)
                return new Color32(15, 35, 75, 255);
            if (preset == AERISTerrainColourPreset.HighContrast)
                return new Color32(12, 72, 24, 255);
            return new Color32(26, 61, 31, 255);
        }

        static Color32 ResolveTopographicLandColour(
            AERISTerrainColourPreset preset, float t)
        {
            switch (preset)
            {
                case AERISTerrainColourPreset.RedGreenAssist:
                    return Gradient(t,
                        new Color32(25, 55, 105, 255),
                        new Color32(45, 110, 175, 255),
                        new Color32(225, 175, 70, 255),
                        new Color32(150, 105, 85, 255),
                        new Color32(245, 245, 245, 255));
                case AERISTerrainColourPreset.BlueYellowAssist:
                    return Gradient(t,
                        new Color32(25, 70, 48, 255),
                        new Color32(70, 135, 75, 255),
                        new Color32(160, 150, 80, 255),
                        new Color32(125, 90, 75, 255),
                        new Color32(245, 245, 245, 255));
                case AERISTerrainColourPreset.HighContrast:
                    return Gradient(t,
                        new Color32(5, 35, 15, 255),
                        new Color32(40, 150, 40, 255),
                        new Color32(255, 220, 40, 255),
                        new Color32(160, 70, 30, 255),
                        new Color32(255, 255, 255, 255));
                default:
                    return Gradient(t,
                        new Color32(18, 65, 35, 255),
                        new Color32(55, 125, 55, 255),
                        new Color32(150, 145, 70, 255),
                        new Color32(120, 85, 65, 255),
                        new Color32(235, 235, 235, 255));
            }
        }

        static Color32 ResolveWaterColour(AERISTerrainColourPreset preset)
        {
            // CP3.75 Candidate 5: RG assistance must distinguish water from the
            // blue low-elevation land bands without changing any other palette.
            // Keep STD/BY/HIGH at the frozen Candidate4 water colour.
            if (preset == AERISTerrainColourPreset.RedGreenAssist)
                return new Color32(0, 20, 70, 255);
            return new Color32(8, 52, 118, 255);
        }

        static Color32 ApplyShade(Color32 colour, byte shade,
            AERISTerrainDisplayMode mode)
        {
            float raw = Mathf.Clamp(shade / 227f, 0.82f, 1.04f);
            // REL bands are safety symbology: keep their red/yellow/green identity
            // dominant and use only subtle relief shading. TOPO may retain a little
            // more relief, but no longer produces dark triangular blotches.
            float blend = mode == AERISTerrainDisplayMode.Relative ? 0.30f : 0.55f;
            float factor = Mathf.Lerp(1f, raw, blend);
            factor = mode == AERISTerrainDisplayMode.Relative ?
                Mathf.Clamp(factor, 0.94f, 1.02f) :
                Mathf.Clamp(factor, 0.88f, 1.035f);
            return new Color32(
                (byte)Mathf.Clamp(Mathf.RoundToInt(colour.r * factor), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(colour.g * factor), 0, 255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(colour.b * factor), 0, 255),
                colour.a);
        }



        static Color ResolveContourColour(AERISTerrainColourPreset preset)
        {
            switch (preset)
            {
                case AERISTerrainColourPreset.RedGreenAssist:
                    return new Color(1f, 0.94f, 0.60f, 0.74f);
                case AERISTerrainColourPreset.BlueYellowAssist:
                    return new Color(0.95f, 0.90f, 1f, 0.74f);
                case AERISTerrainColourPreset.HighContrast:
                    return new Color(1f, 1f, 1f, 0.92f);
                default:
                    return new Color(0.82f, 0.90f, 0.82f, 0.68f);
            }
        }

        static Color32 Gradient(float t, Color32 a, Color32 b, Color32 c,
            Color32 d, Color32 e)
        {
            if (t <= 0.25f) return Lerp(a, b, t * 4f);
            if (t <= 0.50f) return Lerp(b, c, (t - 0.25f) * 4f);
            if (t <= 0.75f) return Lerp(c, d, (t - 0.50f) * 4f);
            return Lerp(d, e, (t - 0.75f) * 4f);
        }

        static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Color32(
                (byte)Mathf.RoundToInt(a.r + (b.r - a.r) * t),
                (byte)Mathf.RoundToInt(a.g + (b.g - a.g) * t),
                (byte)Mathf.RoundToInt(a.b + (b.b - a.b) * t),
                (byte)Mathf.RoundToInt(a.a + (b.a - a.a) * t));
        }

        void DestroyRenderTargets(bool preserveCadenceAndContent = false)
        {
            DestroyRenderTexture(ref backTarget);
            DestroyRenderTexture(ref frontTarget);
            backTargetBytes = 0L;
            frontTargetBytes = 0L;
            ResetFrontBufferState(preserveCadenceAndContent);
        }

        static void DestroyRenderTexture(ref RenderTexture target)
        {
            if (target == null) return;
            try
            {
                if (target.IsCreated()) target.Release();
            }
            catch { }
            DestroyUnityObject(target);
            target = null;
        }

        static void DestroyUnityObject(UnityEngine.Object value)
        {
            if (value == null) return;
            try
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(value);
                else UnityEngine.Object.DestroyImmediate(value);
            }
            catch { }
        }


        void ReleaseGpuResources()
        {
            releaseEntryScratch.Clear();
            foreach (Entry entry in entries.Values) releaseEntryScratch.Add(entry);
            for (int i = 0; i < releaseEntryScratch.Count; i++)
                Remove(releaseEntryScratch[i]);
            releaseEntryScratch.Clear();
            entries.Clear();
            entriesByTile.Clear();
            // Terrain OFF/suspension means release presentation GPU resources, including
            // the bounded recycle pool. Ordinary eviction retains the pool for reuse.
            DestroyMeshPool();
            identityIndexCache.Clear();
            uniformColourScratch.Clear();
            completed.Clear();
            requested.Clear();
            scheduledThisFrame.Clear();
            ResetContentSnapshot();
            DestroyRenderTargets();
            DestroyUnityObject(terrainMaterial);
            DestroyUnityObject(contourMaterial);
            DestroyUnityObject(coastlineMaterial);
            terrainMaterial = null;
            contourMaterial = null;
            coastlineMaterial = null;
            usedEntryBytes = 0L;
            lastCoverageFraction = 0f;
            lastVisualCoverageFraction = 0f;
            lastDrawState = AERISTerrainGpuDrawState.None;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            rasterizer.Dispose();
            ReleaseGpuResources();
            var renderReadySnapshot = new List<KeyValuePair<string,
                AERISTerrainRenderReadyHeightField>>(renderReadyFields);
            for (int i = 0; i < renderReadySnapshot.Count; i++)
                RemoveRenderReadyField(renderReadySnapshot[i].Key,
                    renderReadySnapshot[i].Value);
            renderReadyFields.Clear();
            renderReadyBytes = 0L;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null) runtime.Gpu.ReleaseGraphicsAssist(GraphicsAssistName);
        }
    }
}
