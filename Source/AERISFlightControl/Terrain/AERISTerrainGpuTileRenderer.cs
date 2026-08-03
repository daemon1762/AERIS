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
        internal bool Reprojected;
        internal float ReprojectionErrorPixels;
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
    // world-fixed overlay is drawn in that same coordinate authority. CP3.5 Gate 3 folds
    // world-locked runway/facility geometry into that same surface and progressively refines
    // Route/Local detail without any landing-specific terrain-quality tier.
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
            internal Mesh ContourMesh;
            internal Mesh CoastlineMesh;
            internal GeographicUnitPoint[] LandGeographicPoints;
            internal GeographicUnitPoint[] WaterGeographicPoints;
            internal GeographicUnitPoint[] ContourGeographicPoints;
            internal GeographicUnitPoint[] CoastlineGeographicPoints;
            internal Vector3[] LandProjectedVertices;
            internal Vector3[] WaterProjectedVertices;
            internal Vector3[] ContourProjectedVertices;
            internal Vector3[] CoastlineProjectedVertices;
            internal double SouthLatitudeDeg;
            internal double NorthLatitudeDeg;
            internal double WestLongitudeDeg;
            internal double EastLongitudeDeg;
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
            internal long PreparedProjectionBatchId = -1L;
            internal long PreparedColourBatchId = -1L;
            internal AERISTerrainDisplayMode ColourMode = (AERISTerrainDisplayMode)(-1);
            internal AERISTerrainColourPreset ColourPreset = (AERISTerrainColourPreset)(-1);
            internal int RelativeAltitudeBucket = int.MinValue;
            internal int TopoMinimumMeters = int.MinValue;
            internal int TopoMaximumMeters = int.MinValue;
            internal int Resolution;
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

        // Fixed-value, allocation-free sample carried only by the sampled BACK render. Detailed
        // instrumentation runs once per BackRenderDetailedProfileStride BACKs; all BACKs still
        // contribute the outer total-time average. No per-tile log messages are emitted.
        struct BackRenderDetailedProfile
        {
            internal double SetupClearMs;
            internal double ProjectionCpuMs;
            internal double MeshVertexUploadMs;
            internal double BoundsMs;
            internal double ColourCpuMs;
            internal double ColourUploadMs;
            internal double DrawSubmitMs;
            internal double WorldSurfaceMs;
            internal double FinalizeMs;
            internal long ProjectedVertices;
            internal long DrawCalls;
            internal long WorldSurfacePrimitives;
            internal long TilesVisited;
            internal long EntriesReprojected;
        }

        // Gate 2 projection batches deliberately carry only immutable projection snapshots
        // plus managed arrays.  No worker touches Mesh, Material, RenderTexture, Graphics,
        // Event, Vessel or any other Unity/KSP object.  Each chunk owns a disjoint set of
        // Entry scratch arrays, allowing all currently-permitted scheduler workers to run
        // in parallel without locks or cross-thread Unity calls.
        sealed class ProjectionBatch
        {
            internal long Id;
            internal AERISNdMapProjection Projection;
            internal Matrix4x4 MapRotation;
            internal Entry[] Entries;
            internal Entry[][] Chunks;
            internal AERISTerrainDisplayMode Mode;
            internal AERISTerrainColourPreset Preset;
            internal float AircraftAltitudeAslMeters;
            internal int RelativeAltitudeBucket;
            internal int TopoMinimumMeters;
            internal int TopoMaximumMeters;
            internal double CenterLatitudeDeg;
            internal double CenterLongitudeDeg;
            internal float RangeMeters;
            internal float SurfaceRangeMeters;
            internal float MapHeadingDeg;
            internal bool TrackUp;
            internal float AnchorV;
            internal AERISTerrainRenderTargetOrientation Orientation;
            internal long ViewGeneration;
            internal long TerrainGeneration;
            internal long ContentRevision;
            internal string BodyName;
            internal long BodyRadiusMillimetres;
            internal bool FoundationComplete;
            internal float FoundationCoverage;
            internal int ReadyFar;
            internal int RequiredFar;
            internal AERISTerrainHeightTile[] Tiles;
            internal AERISPreparedNavigationFrame NavigationFrame;
            internal bool IncludeFacilities;
            internal long WorldSurfaceRevision;
            internal int ExpectedChunks;
            internal int CompletedChunks;
            internal bool SubmissionFailed;
            internal bool Ready;
            internal long ProjectedVertices;
            internal long ColourVertices;
            internal double WorkerMilliseconds;
            internal float SubmittedRealtime;
        }

        sealed class ProjectionChunkResult
        {
            internal ProjectionBatch Batch;
            internal int ChunkIndex;
            internal long ProjectedVertices;
            internal long ColourVertices;
            internal double WorkerMilliseconds;
        }

        sealed class SurfaceBuilder
        {
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<float> Elevation = new List<float>();
            internal readonly List<byte> Shade = new List<byte>();
            internal readonly List<int> Triangles = new List<int>();

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

        const string GraphicsAssistName = "UNITY GPU UNIFIED WORLD SURFACE TEMPORAL REPROJECTION";
        const float CoastlineHalfWidthNormalized = 0.0025f;
        const float RelativeAltitudeBucketMeters = 5f;
        const float TopographicWindowBucketMeters = 250f;
        const float TopographicMinimumSpanMeters = 1500f;
        // CP3.5 Gate 2 Candidate 2: every authoritative map is an exact key frame prepared
        // by the multicore worker pool.  The key frame covers a hidden overscan surface;
        // between key frames a tiny continuous reprojection grid maps the current exact ND
        // projection back into that surface and the GPU resamples it into a third presentation
        // RenderTexture.  This is not GUI.matrix warping: all geographic mapping is computed
        // from AERISNdMapProjection and accepted only while the measured interpolation error
        // stays below a strict sub-pixel limit.
        const float HistoryOverscanScale = 1.25f;
        const float MaximumHistorySurfaceRangeMeters = 250000f;
        const float ReadyBuildingViolationSeconds = 1.0f;
        const float KeyFrameMinimumIntervalSeconds = 0.35f;
        const float KeyFrameMaximumAgeSeconds = 1.25f;
        const float KeyFrameRefreshHeadingDeg = 3.0f;
        const float KeyFrameRefreshDriftPixels = 36f;
        const float KeyFrameRefreshErrorPixels = 0.30f;
        const float TemporalMaximumErrorPixels = 0.75f;
        const float TemporalMinimumUvMargin = 0.0025f;
        const int TemporalGridCells = 8;
        const int TemporalGridPointsPerAxis = TemporalGridCells + 1;
        const int TemporalGridPointCount = TemporalGridPointsPerAxis * TemporalGridPointsPerAxis;
        // Gate 3 Candidate 2 Presentation Authority Hotfix 1: temporal reprojection is
        // retained as a shadow-quality probe, but it is not allowed to own presentation
        // until the exact FRONT authority has been revalidated in runtime. Any valid
        // committed FRONT must remain directly visible even if temporal generation fails.
        const bool TemporalPresentationAuthorityEnabled = false;
        // Candidate 3: exact FRONT remains the only presentation authority, so the
        // 9x9 temporal shadow grid is disabled during normal operation. A one-point
        // geographic drift/heading metric keeps adaptive key-frame refresh intact.
        const bool TemporalShadowSamplingEnabled = false;
        const int BackRenderDetailedProfileStride = 4;
        const int ProjectionCancellationCheckStride = 4096;

        readonly AERISSettings settings;
        readonly AERISTerrainPerformanceController performance;
        readonly AERISTerrainGpuTileRasterizer rasterizer =
            new AERISTerrainGpuTileRasterizer();
        readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainRenderReadyHeightField>
            renderReadyFields =
            new Dictionary<string, AERISTerrainRenderReadyHeightField>(StringComparer.Ordinal);
        readonly List<AERISTerrainGpuTileRasterResult> completed =
            new List<AERISTerrainGpuTileRasterResult>(16);
        readonly HashSet<string> requested = new HashSet<string>(StringComparer.Ordinal);
        readonly List<CoverageRegion> coverageRects =
            new List<CoverageRegion>(128);
        readonly List<Entry> supersededScratch = new List<Entry>(16);
        long useSequence;
        long usedEntryBytes;
        long backTargetBytes;
        long frontTargetBytes;
        long presentationTargetBytes;
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
        Material worldSurfaceMaterial;
        Material reprojectionMaterial;
        RenderTexture backTarget;
        RenderTexture frontTarget;
        RenderTexture presentationTarget;
        bool frontBufferValid;
        long frontViewGeneration = -1L;
        long frontTerrainGeneration = -1L;
        string frontBodyName = string.Empty;
        long frontBodyRadiusMillimetres;
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
        long frontWorldSurfaceRevision = -1L;
        AERISPreparedNavigationFrame worldSurfaceNavigationFrame;
        bool worldSurfaceIncludeFacilities;
        long worldSurfaceRevision;
        string worldSurfaceBodyName = string.Empty;
        long worldSurfaceDatabaseRevision = -1L;
        long worldSurfaceSelectionRevision = -1L;
        int worldSurfaceRunwayCount = -1;
        int worldSurfaceFacilityCount = -1;
        AERISTerrainColourPreset activePalettePreset = (AERISTerrainColourPreset)(-1);
        long paletteGeneration;
        long lastBackAttemptViewGeneration = -1L;
        long lastBackAttemptContentRevision = -1L;
        float nextBackRefreshRealtime;
        long historyReprojectFrames;
        long historyRejectedFrames;
        long directFrontFrames;
        long backRenderFrames;
        long skippedBackRenderFrames;
        long forcedRecoveryBackRenders;
        long suppressedForcedRecoveryFrames;
        float lastBackRefreshCadenceSeconds;
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
        string lastVirtualDetailName = "FAR DIRECT";

        // CP3.5 Gate 1 Candidate 2 BACK composition profiler. Main-thread only; no locks, no
        // allocations on the render path, and no per-frame file writes. Aggregates are flushed
        // into one human-readable log line at the existing 5 s presentation telemetry cadence.
        long backProfileSequence;
        long backProfileAllSamples;
        double backProfileAllTotalMs;
        double backProfileAllMaxTotalMs;
        long backProfileDetailedSamples;
        double backProfileDetailedTotalMs;
        double backProfileSetupClearMs;
        double backProfileProjectionCpuMs;
        double backProfileMeshVertexUploadMs;
        double backProfileBoundsMs;
        double backProfileColourCpuMs;
        double backProfileColourUploadMs;
        double backProfileDrawSubmitMs;
        double backProfileWorldSurfaceMs;
        double backProfileFinalizeMs;
        long backProfileProjectedVertices;
        long backProfileDrawCalls;
        long backProfileWorldSurfacePrimitives;
        long backProfileTilesVisited;
        long backProfileEntriesReprojected;

        ProjectionBatch pendingProjectionBatch;
        long projectionBatchSequence;
        long projectionBatchesSubmitted;
        long projectionBatchesCompleted;
        long projectionBatchesDiscarded;
        long projectionBatchesSubmissionFailed;
        long projectionWorkerProjectedVertices;
        long projectionWorkerColourVertices;
        double projectionWorkerMilliseconds;
        double projectionWorkerWallMilliseconds;
        int lastProjectionWorkerCount;

        readonly Vector2[] temporalSourceUv = new Vector2[TemporalGridPointCount];
        long temporalFrames;
        long temporalRejects;
        long temporalKeyFrameRequests;
        double temporalGridMilliseconds;
        double temporalSubmitMilliseconds;
        double lastTemporalMaxErrorPixels;
        float lastTemporalMinUvMargin;
        float lastTemporalDriftPixels;
        float lastTemporalHeadingDeltaDeg;
        long temporalShadowEligibleFrames;
        long temporalPresentationBlockedFrames;
        long exactFrontAuthorityFrames;

        // Gate 3 Candidate 1: world-locked navigation geometry is latched into the
        // same exact key-frame surface as terrain. New pipeline frames with identical
        // database/selection identity do not force a redraw; only meaningful geometry
        // authority changes advance the surface revision.
        internal void SetWorldSurfaceNavigationFrame(AERISPreparedNavigationFrame frame,
            bool includeFacilities)
        {
            string body = frame == null ? string.Empty : (frame.BodyName ?? string.Empty);
            long databaseRevision = frame == null ? -1L : frame.DatabaseRevision;
            long selectionRevision = frame == null ? -1L : frame.SelectionRevision;
            int runwayCount = frame == null || frame.Runways == null ? 0 : frame.Runways.Length;
            int facilityCount = frame == null || frame.Facilities == null ? 0 : frame.Facilities.Length;
            bool changed = !string.Equals(body, worldSurfaceBodyName, StringComparison.Ordinal) ||
                databaseRevision != worldSurfaceDatabaseRevision ||
                selectionRevision != worldSurfaceSelectionRevision ||
                runwayCount != worldSurfaceRunwayCount ||
                facilityCount != worldSurfaceFacilityCount ||
                includeFacilities != worldSurfaceIncludeFacilities;
            worldSurfaceNavigationFrame = frame;
            worldSurfaceIncludeFacilities = includeFacilities;
            if (!changed) return;
            worldSurfaceBodyName = body;
            worldSurfaceDatabaseRevision = databaseRevision;
            worldSurfaceSelectionRevision = selectionRevision;
            worldSurfaceRunwayCount = runwayCount;
            worldSurfaceFacilityCount = facilityCount;
            worldSurfaceRevision++;
            gpuContentRevision++;
            CancelProjectionBatch();
            nextBackRefreshRealtime = 0f;
        }

        internal bool IsWorldSurfaceNavigationCurrent(AERISPreparedNavigationFrame frame,
            bool includeFacilities)
        {
            if (!frontBufferValid || frontWorldSurfaceRevision != worldSurfaceRevision ||
                includeFacilities != worldSurfaceIncludeFacilities) return false;
            if (frame == null) return worldSurfaceNavigationFrame == null;
            return string.Equals(frame.BodyName ?? string.Empty, worldSurfaceBodyName,
                StringComparison.Ordinal) &&
                frame.DatabaseRevision == worldSurfaceDatabaseRevision &&
                frame.SelectionRevision == worldSurfaceSelectionRevision;
        }

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
                    Math.Max(0L, frontTargetBytes) + Math.Max(0L, presentationTargetBytes) +
                    Math.Max(0L, renderReadyBytes);
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
            generation++;
            rasterizer.CancelAll();
            requested.Clear();
            // A user-selected range/view change is interactive and must not wait behind the
            // previous key-frame cadence. Existing worker chunks are retired safely by the
            // normal batch contract; the successor exact FRONT may be requested immediately.
            CancelProjectionBatch();
            nextBackRefreshRealtime = 0f;
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
            lastFrontBufferPresented = false;
            lastFrontBufferLatched = false;
            presentedProjection.Valid = false;
            if (disposed || system == null || vessel == null || vessel.mainBody == null ||
                plot.width < 8f || plot.height < 8f)
            {
                lastCoverageFraction = 0f;
                lastVisualCoverageFraction = 0f;
                lastDrawState = AERISTerrainGpuDrawState.None;
                return lastDrawState;
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

            Event currentEvent = Event.current;
            bool repaint = currentEvent == null || currentEvent.type == EventType.Repaint;
            if (!repaint) return lastDrawState;

            AERISTerrainRenderTargetOrientation orientation = settings == null ?
                AERISTerrainRenderTargetOrientation.Direct :
                settings.TerrainRenderTargetOrientation;
            float historySurfaceRangeMeters = ResolveHistorySurfaceRange(rangeMeters);
            AERISTerrainVisibleTileSet visible = system.CaptureVisible(centerLatitudeDeg,
                centerLongitudeDeg, historySurfaceRangeMeters, mapHeadingDeg, trackUp,
                anchorV, orientation);
            if (visible == null || visible.Tiles == null || visible.Tiles.Length == 0)
            {
                lastCoverageFraction = 0f;
                lastVisualCoverageFraction = 0f;
                lastDrawState = AERISTerrainGpuDrawState.Partial;
                return lastDrawState;
            }

            AERISTerrainDisplayMode effectiveMode = ResolveEffectiveMode(requestedMode,
                vessel, rangeMeters);
            AERISTerrainVirtualDetailProfile virtualDetail =
                ResolveVirtualDetailProfile(rangeMeters);
            lastVirtualDetailName = virtualDetail.Name;
            float contourInterval = ResolveContourInterval(rangeMeters);
            string styleKey = BuildStyleKey(contourInterval, virtualDetail);
            DrainCompleted(system);
            requested.Clear();

            AERISTerrainHeightTile[] tiles = (AERISTerrainHeightTile[])visible.Tiles.Clone();
            Array.Sort(tiles, CompareTilesCoarseFirst);
            int topoMinimumMeters, topoMaximumMeters;
            ResolveTopographicWindow(tiles, out topoMinimumMeters, out topoMaximumMeters);
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null) continue;
                string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                requested.Add(cacheKey);
                Entry fallbackEntry, currentEntry;
                ResolveRenderableEntries(tile, styleKey, out fallbackEntry,
                    out currentEntry);
                if (currentEntry == null)
                {
                    if (!TryUploadRenderReadyField(tile, styleKey, system,
                        out currentEntry))
                        Schedule(tile, styleKey, contourInterval, virtualDetail);
                }
                if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                if (currentEntry != null) currentEntry.LastUse = ++useSequence;
            }

            AERISNdMapProjection projection = AERISNdMapProjection.Create(
                vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                mapHeadingDeg, trackUp, anchorV, orientation);
            Matrix4x4 mapRotation = projection.ResolveScaleCorrectedRenderMatrix();
            AERISNdMapProjection historySurfaceProjection = AERISNdMapProjection.Create(
                vessel.mainBody, centerLatitudeDeg, centerLongitudeDeg,
                historySurfaceRangeMeters, mapHeadingDeg, trackUp, anchorV, orientation);
            Matrix4x4 historySurfaceMapRotation =
                historySurfaceProjection.ResolveScaleCorrectedRenderMatrix();
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

            int readyGlobal, readyFar;
            lastBackFoundationCoverage = MeasureFoundationGpuReadiness(visible, tiles,
                styleKey, out readyGlobal, out readyFar);
            lastCoverageFraction = lastBackFoundationCoverage;

            EnsureResources(plot, effectiveMode,
                settings == null ? AERISTerrainColourPreset.Standard :
                settings.TerrainColourPreset, virtualDetail);
            Prune(ResolveVramLimitBytes());
            PruneRenderReady(ResolveRenderReadyLimitBytes());
            if (backTarget == null || !backTarget.IsCreated() || frontTarget == null ||
                !frontTarget.IsCreated() || presentationTarget == null ||
                !presentationTarget.IsCreated())
            {
                lastDrawState = AERISTerrainGpuDrawState.None;
                return lastDrawState;
            }

            AERISTerrainColourPreset currentPreset = settings == null ?
                AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;
            HandlePaletteGeneration(currentPreset);

            bool preparedSwapped;
            TryRenderReadyProjectionBatch(visible, effectiveMode, currentPreset,
                rangeMeters, historySurfaceRangeMeters, trackUp, orientation,
                out preparedSwapped);

            double temporalErrorPixels;
            float temporalUvMargin;
            float temporalDriftPixels;
            float temporalHeadingDelta;
            bool temporalAvailable = TryBuildRefreshMetrics(vessel, projection,
                rangeMeters, mapHeadingDeg, trackUp, out temporalErrorPixels,
                out temporalUvMargin, out temporalDriftPixels, out temporalHeadingDelta);
            lastTemporalMaxErrorPixels = temporalErrorPixels;
            lastTemporalMinUvMargin = temporalUvMargin;
            lastTemporalDriftPixels = temporalDriftPixels;
            lastTemporalHeadingDeltaDeg = temporalHeadingDelta;

            bool refreshRequired = NeedsKeyFrameRefresh(visible, vessel, rangeMeters,
                historySurfaceRangeMeters, temporalAvailable, temporalErrorPixels,
                temporalUvMargin, temporalDriftPixels, temporalHeadingDelta);
            bool refreshAllowed = ShouldRefreshBackBuffer(visible, refreshRequired);
            bool swapped = preparedSwapped;
            if (refreshAllowed && pendingProjectionBatch == null)
            {
                temporalKeyFrameRequests++;
                bool asynchronous = TryStartProjectionBatch(visible, tiles,
                    historySurfaceProjection, historySurfaceMapRotation, styleKey,
                    effectiveMode, currentPreset, vessel, rangeMeters,
                    historySurfaceRangeMeters, mapHeadingDeg, readyFar,
                    topoMinimumMeters, topoMaximumMeters);
                lastBackAttemptViewGeneration = visible.ViewGeneration;
                lastBackAttemptContentRevision = gpuContentRevision;
                lastBackRefreshCadenceSeconds = ResolveBackRefreshCadenceSeconds(rangeMeters);
                nextBackRefreshRealtime = Time.realtimeSinceStartup +
                    lastBackRefreshCadenceSeconds;

                // Fail closed if the shared scheduler cannot admit a key frame. Never fall
                // back to the former ~28 ms main-thread full-vertex projection path: keep the
                // last exact FRONT and retry after the bounded admission cadence instead.
                if (!asynchronous)
                {
                    projectionBatchesSubmissionFailed++;
                    skippedBackRenderFrames++;
                }
            }
            else if (refreshRequired)
            {
                skippedBackRenderFrames++;
            }

            // A freshly completed key frame may make reprojection available immediately.
            if (swapped)
            {
                temporalAvailable = TryBuildRefreshMetrics(vessel, projection,
                    rangeMeters, mapHeadingDeg, trackUp, out temporalErrorPixels,
                    out temporalUvMargin, out temporalDriftPixels, out temporalHeadingDelta);
                lastTemporalMaxErrorPixels = temporalErrorPixels;
                lastTemporalMinUvMargin = temporalUvMargin;
                lastTemporalDriftPixels = temporalDriftPixels;
                lastTemporalHeadingDeltaDeg = temporalHeadingDelta;
            }

            lastHistoryReprojected = false;
            lastHistoryConfidence = TemporalShadowSamplingEnabled && temporalAvailable ?
                ResolveTemporalConfidence(temporalErrorPixels, temporalUvMargin) : 0f;
            bool present = false;

            // Temporal stays in shadow mode for this hotfix. Runtime evidence showed that
            // presentationTarget could be empty/blue while temporal confidence still read
            // 1.000, so confidence alone cannot grant presentation authority.
            if (TemporalShadowSamplingEnabled && temporalAvailable)
            {
                temporalShadowEligibleFrames++;
                if (TemporalPresentationAuthorityEnabled)
                {
                    bool submitted = RenderTemporalReprojection(plot, projection,
                        temporalErrorPixels, temporalUvMargin);
                    if (submitted)
                    {
                        temporalFrames++;
                        historyReprojectFrames++;
                        lastHistoryReprojected = true;
                        lastFrontBufferPresented = true;
                        lastFrontBufferLatched = false;
                        CapturePresentedProjectionCurrent(centerLatitudeDeg,
                            centerLongitudeDeg, rangeMeters, mapHeadingDeg, trackUp,
                            anchorV, orientation, temporalErrorPixels);
                        lastVisualCoverageFraction = 1f;
                        present = true;
                    }
                    else temporalPresentationBlockedFrames++;
                }
                else temporalPresentationBlockedFrames++;
            }
            else if (frontBufferValid)
            {
                temporalRejects++;
                historyRejectedFrames++;
            }

            bool readyFoundationNow = visible.FoundationComplete &&
                lastBackFoundationCoverage >= 0.999f &&
                readyFar >= visible.FarFoundationCount;
            if (!present && readyFoundationNow && !gpuFailed && refreshRequired &&
                !refreshAllowed)
                suppressedForcedRecoveryFrames++;

            // Exact FRONT is the hard presentation authority. Draw the committed
            // RenderTexture directly, cropped from its overscan surface back to the visible
            // ND range. No temporal grid, presentationTarget clear, or reprojection material
            // participates in this fallback/authority path.
            if (!present && CanPresentLatchedFront(visible, vessel, rangeMeters,
                trackUp, anchorV, orientation) &&
                PresentFrontDirect(plot, frontOrientation))
            {
                if (frontTerrainGeneration != visible.TerrainGeneration)
                    generationBridgeFrames++;
                directFrontFrames++;
                exactFrontAuthorityFrames++;
                lastFrontBufferPresented = true;
                lastFrontBufferLatched = true;
                CapturePresentedProjection(true);
                lastVisualCoverageFraction = 1f;
                present = true;
            }

            if (!present && frontBufferValid) generationBridgeRejects++;

            UpdateReadyBuildingWatchdog(present, readyFoundationNow, visible,
                readyGlobal, readyFar);
            LogGpuOnlyPresentation(visible, readyGlobal, readyFar, swapped);
            lastDrawState = present ? AERISTerrainGpuDrawState.Complete :
                AERISTerrainGpuDrawState.Partial;
            return lastDrawState;
        }

        bool TryStartProjectionBatch(AERISTerrainVisibleTileSet visible,
            AERISTerrainHeightTile[] tiles, AERISNdMapProjection projection,
            Matrix4x4 mapRotation, string styleKey, AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset, Vessel vessel, float rangeMeters,
            float surfaceRangeMeters, float mapHeadingDeg, int readyFar,
            int topoMinimumMeters, int topoMaximumMeters)
        {
            if (pendingProjectionBatch != null || visible == null || tiles == null ||
                vessel == null || vessel.mainBody == null) return false;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return false;

            var unique = new HashSet<Entry>();
            var drawEntries = new List<Entry>(tiles.Length);
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null) continue;
                Entry fallback, current;
                ResolveRenderableEntries(tile, styleKey, out fallback, out current);
                Entry entry = current != null ? current : fallback;
                if (entry == null || !unique.Add(entry)) continue;
                drawEntries.Add(entry);
            }
            if (drawEntries.Count == 0) return false;

            int permitted = Math.Max(1, runtime.Scheduler.PermitController.ActivePermits);
            int workerCount = Math.Max(1, Math.Min(drawEntries.Count,
                Math.Min(runtime.Scheduler.WorkerCount, permitted)));
            var chunkLists = new List<Entry>[workerCount];
            long[] chunkWeights = new long[workerCount];
            for (int i = 0; i < workerCount; i++) chunkLists[i] = new List<Entry>();

            // Greedy vertex-weight distribution gives every permitted core a similar amount
            // of projection work even when one FAR tile contains substantially more coastline
            // or clipped land geometry than its neighbours.
            drawEntries.Sort((a, b) => EntryProjectionVertexCount(b).CompareTo(
                EntryProjectionVertexCount(a)));
            for (int i = 0; i < drawEntries.Count; i++)
            {
                int lightest = 0;
                for (int j = 1; j < workerCount; j++)
                    if (chunkWeights[j] < chunkWeights[lightest]) lightest = j;
                Entry entry = drawEntries[i];
                chunkLists[lightest].Add(entry);
                chunkWeights[lightest] += EntryProjectionVertexCount(entry);
            }

            var chunks = new Entry[workerCount][];
            for (int i = 0; i < workerCount; i++) chunks[i] = chunkLists[i].ToArray();
            var batch = new ProjectionBatch
            {
                Id = ++projectionBatchSequence,
                Projection = projection,
                MapRotation = mapRotation,
                Entries = drawEntries.ToArray(),
                Chunks = chunks,
                Mode = mode,
                Preset = preset,
                AircraftAltitudeAslMeters = (float)vessel.altitude,
                RelativeAltitudeBucket = mode == AERISTerrainDisplayMode.Relative ?
                    Mathf.RoundToInt((float)vessel.altitude / RelativeAltitudeBucketMeters) :
                    int.MinValue,
                TopoMinimumMeters = topoMinimumMeters,
                TopoMaximumMeters = topoMaximumMeters,
                CenterLatitudeDeg = UnitLatitude(projection.CenterX, projection.CenterY,
                    projection.CenterZ),
                CenterLongitudeDeg = UnitLongitude(projection.CenterX, projection.CenterY),
                RangeMeters = rangeMeters,
                SurfaceRangeMeters = surfaceRangeMeters,
                MapHeadingDeg = mapHeadingDeg,
                TrackUp = projection.TrackUp,
                AnchorV = projection.AnchorGuiV,
                Orientation = projection.Orientation,
                ViewGeneration = visible.ViewGeneration,
                TerrainGeneration = visible.TerrainGeneration,
                ContentRevision = gpuContentRevision,
                BodyName = visible.BodyName ?? string.Empty,
                BodyRadiusMillimetres = (long)Math.Round(
                    Math.Max(0.0, vessel.mainBody.Radius) * 1000.0),
                FoundationComplete = visible.FoundationComplete &&
                    lastBackFoundationCoverage >= 0.999f &&
                    readyFar >= visible.FarFoundationCount,
                FoundationCoverage = lastBackFoundationCoverage,
                ReadyFar = readyFar,
                RequiredFar = visible.FarFoundationCount,
                Tiles = (AERISTerrainHeightTile[])tiles.Clone(),
                NavigationFrame = worldSurfaceNavigationFrame,
                IncludeFacilities = worldSurfaceIncludeFacilities,
                WorldSurfaceRevision = worldSurfaceRevision,
                ExpectedChunks = 0,
                CompletedChunks = 0,
                SubmittedRealtime = Time.realtimeSinceStartup
            };
            pendingProjectionBatch = batch;
            lastProjectionWorkerCount = workerCount;

            AERISRuntimeGenerationStamp stamp = runtime.CaptureStamp();
            int accepted = 0;
            for (int i = 0; i < workerCount; i++)
            {
                int chunkIndex = i;
                string key = "terrain-projection-" + chunkIndex;
                bool submitted = runtime.Scheduler.SubmitRequired(
                    AERISRuntimeLane.GeneralCompute, key, stamp,
                    context => ProjectProjectionChunk(batch, chunkIndex, context),
                    value => CommitProjectionChunk(batch,
                        value as ProjectionChunkResult), false);
                if (submitted) accepted++;
                else batch.SubmissionFailed = true;
            }
            batch.ExpectedChunks = accepted;
            projectionBatchesSubmitted++;
            if (accepted == 0)
            {
                batch.SubmissionFailed = true;
                batch.Ready = true;
                projectionBatchesSubmissionFailed++;
            }
            else if (accepted != workerCount)
            {
                projectionBatchesSubmissionFailed++;
            }
            return true;
        }

        static long EntryProjectionVertexCount(Entry entry)
        {
            if (entry == null) return 0L;
            return PointCount(entry.LandGeographicPoints) +
                PointCount(entry.WaterGeographicPoints) +
                PointCount(entry.ContourGeographicPoints) +
                PointCount(entry.CoastlineGeographicPoints);
        }

        static long PointCount(GeographicUnitPoint[] points)
        {
            return points == null ? 0L : points.LongLength;
        }

        static ProjectionChunkResult ProjectProjectionChunk(ProjectionBatch batch,
            int chunkIndex, AERISRuntimeJobContext context)
        {
            long start = Stopwatch.GetTimestamp();
            long projected = 0L;
            long coloured = 0L;
            if (batch == null || batch.Chunks == null || chunkIndex < 0 ||
                chunkIndex >= batch.Chunks.Length)
                return new ProjectionChunkResult { Batch = batch, ChunkIndex = chunkIndex };
            Entry[] chunk = batch.Chunks[chunkIndex];
            for (int i = 0; i < chunk.Length; i++)
            {
                context.ThrowIfStale();
                Entry entry = chunk[i];
                if (entry == null) continue;
                projected += ProjectPointsWorker(entry.LandGeographicPoints,
                    entry.LandProjectedVertices, batch.Projection, context);
                projected += ProjectPointsWorker(entry.WaterGeographicPoints,
                    entry.WaterProjectedVertices, batch.Projection, context);
                projected += ProjectPointsWorker(entry.ContourGeographicPoints,
                    entry.ContourProjectedVertices, batch.Projection, context);
                projected += ProjectPointsWorker(entry.CoastlineGeographicPoints,
                    entry.CoastlineProjectedVertices, batch.Projection, context);
                entry.PreparedProjectionBatchId = batch.Id;

                int altitudeBucket = batch.RelativeAltitudeBucket;
                if (entry.LandElevationMeters != null && entry.LandShade != null &&
                    (entry.ColourMode != batch.Mode || entry.ColourPreset != batch.Preset ||
                    entry.RelativeAltitudeBucket != altitudeBucket ||
                    (batch.Mode == AERISTerrainDisplayMode.Topographic &&
                    (entry.TopoMinimumMeters != batch.TopoMinimumMeters ||
                    entry.TopoMaximumMeters != batch.TopoMaximumMeters))))
                {
                    if (entry.LandColours == null ||
                        entry.LandColours.Length != entry.LandElevationMeters.Length)
                        entry.LandColours = new Color32[entry.LandElevationMeters.Length];
                    float quantizedAltitude = batch.Mode == AERISTerrainDisplayMode.Relative ?
                        altitudeBucket * RelativeAltitudeBucketMeters :
                        batch.AircraftAltitudeAslMeters;
                    for (int c = 0; c < entry.LandColours.Length; c++)
                    {
                        if ((c & (ProjectionCancellationCheckStride - 1)) == 0)
                            context.ThrowIfStale();
                        Color32 baseColour = ResolveLandColour(batch.Mode, batch.Preset,
                            entry.LandElevationMeters[c], quantizedAltitude,
                            batch.TopoMinimumMeters, batch.TopoMaximumMeters);
                        entry.LandColours[c] = ApplyShade(baseColour, entry.LandShade[c],
                            batch.Mode);
                    }
                    entry.PreparedColourBatchId = batch.Id;
                    coloured += entry.LandColours.LongLength;
                }
            }
            return new ProjectionChunkResult
            {
                Batch = batch,
                ChunkIndex = chunkIndex,
                ProjectedVertices = projected,
                ColourVertices = coloured,
                WorkerMilliseconds = ElapsedMilliseconds(start)
            };
        }

        static long ProjectPointsWorker(GeographicUnitPoint[] points,
            Vector3[] output, AERISNdMapProjection projection,
            AERISRuntimeJobContext context)
        {
            if (points == null || output == null || points.Length != output.Length) return 0L;
            for (int i = 0; i < points.Length; i++)
            {
                if ((i & (ProjectionCancellationCheckStride - 1)) == 0)
                    context.ThrowIfStale();
                GeographicUnitPoint point = points[i];
                float u, v;
                projection.ProjectUnitToRenderNUp(point.X, point.Y, point.Z, out u, out v);
                output[i] = new Vector3(u, v, 0f);
            }
            return points.LongLength;
        }

        void CommitProjectionChunk(ProjectionBatch owner,
            ProjectionChunkResult result)
        {
            if (owner == null || !ReferenceEquals(owner, pendingProjectionBatch)) return;
            ProjectionBatch batch = owner;
            batch.CompletedChunks++;
            if (result == null || result.Batch == null ||
                !ReferenceEquals(result.Batch, batch))
            {
                // SubmitRequired delivers null on worker failure/staleness.  Retire the
                // chunk credit so the batch cannot deadlock; the whole batch is discarded.
                batch.SubmissionFailed = true;
            }
            else
            {
                batch.ProjectedVertices += result.ProjectedVertices;
                batch.ColourVertices += result.ColourVertices;
                batch.WorkerMilliseconds += result.WorkerMilliseconds;
            }
            if (batch.CompletedChunks >= batch.ExpectedChunks) batch.Ready = true;
        }

        bool TryRenderReadyProjectionBatch(AERISTerrainVisibleTileSet currentVisible,
            AERISTerrainDisplayMode currentMode, AERISTerrainColourPreset currentPreset,
            float currentRangeMeters, float currentSurfaceRangeMeters, bool currentTrackUp,
            AERISTerrainRenderTargetOrientation currentOrientation,
            out bool swapped)
        {
            swapped = false;
            ProjectionBatch batch = pendingProjectionBatch;
            if (batch == null || !batch.Ready) return false;
            pendingProjectionBatch = null;
            double wallMs = Math.Max(0.0,
                (Time.realtimeSinceStartup - batch.SubmittedRealtime) * 1000.0);

            bool compatible = !batch.SubmissionFailed && currentVisible != null &&
                batch.TerrainGeneration == currentVisible.TerrainGeneration &&
                batch.ContentRevision == gpuContentRevision &&
                string.Equals(batch.BodyName, currentVisible.BodyName,
                    StringComparison.OrdinalIgnoreCase) &&
                Math.Abs(batch.RangeMeters - currentRangeMeters) <=
                    Math.Max(1f, currentRangeMeters * 0.001f) &&
                Math.Abs(batch.SurfaceRangeMeters - currentSurfaceRangeMeters) <=
                    Math.Max(1f, currentSurfaceRangeMeters * 0.001f) &&
                batch.TrackUp == currentTrackUp &&
                batch.Orientation == currentOrientation &&
                batch.Mode == currentMode && batch.Preset == currentPreset &&
                ProjectionBatchEntriesCurrent(batch);
            if (!compatible)
            {
                projectionBatchesDiscarded++;
                return false;
            }

            projectionWorkerWallMilliseconds += wallMs;
            bool rendered = RenderPreparedBackBuffer(batch);
            backRenderFrames++;
            projectionBatchesCompleted++;
            projectionWorkerProjectedVertices += batch.ProjectedVertices;
            projectionWorkerColourVertices += batch.ColourVertices;
            projectionWorkerMilliseconds += batch.WorkerMilliseconds;
            if (rendered && batch.FoundationComplete)
            {
                SwapFrontAndBack(batch);
                MarkVisibleGpuReady(batch.Tiles);
                swapped = true;
            }
            else blockedIncompleteSwaps++;
            return rendered;
        }

        bool ProjectionBatchEntriesCurrent(ProjectionBatch batch)
        {
            if (batch == null || batch.Entries == null) return false;
            for (int i = 0; i < batch.Entries.Length; i++)
            {
                Entry entry = batch.Entries[i];
                if (entry == null) return false;
                Entry current;
                if (!entries.TryGetValue(entry.CacheKey, out current) ||
                    !ReferenceEquals(entry, current) ||
                    entry.PreparedProjectionBatchId != batch.Id) return false;
            }
            return true;
        }

        bool RenderPreparedBackBuffer(ProjectionBatch batch)
        {
            if (batch == null || batch.Entries == null) return false;
            long frameStartTicks = Stopwatch.GetTimestamp();
            bool detailedProfile = (backProfileSequence++ % BackRenderDetailedProfileStride) == 0L;
            BackRenderDetailedProfile profile = new BackRenderDetailedProfile();
            RenderTexture previous = RenderTexture.active;
            bool matrixPushed = false;
            bool rendered = false;
            bool failed = false;
            try
            {
                long setupStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
                RenderTexture.active = backTarget;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadOrtho();
                GL.Clear(true, true, Color.clear);
                if (detailedProfile) profile.SetupClearMs += ElapsedMilliseconds(setupStartTicks);

                for (int i = 0; i < batch.Entries.Length; i++)
                {
                    Entry entry = batch.Entries[i];
                    if (entry == null || entry.PreparedProjectionBatchId != batch.Id) continue;
                    if (detailedProfile) profile.TilesVisited++;
                    UploadPreparedProjection(entry, batch, detailedProfile, ref profile);
                    bool entryRendered = DrawPreparedEntry(entry, batch.MapRotation, true,
                        batch, detailedProfile, ref profile);
                    rendered = entryRendered || rendered;
                    if (entryRendered && entry.TileKey.Lod >= AERISTerrainTileLod.Route)
                        exactDetailOverlayDraws++;
                }
                DrawWorldSurfaceNavigation(batch, detailedProfile, ref profile);
            }
            catch (Exception ex)
            {
                failed = true;
                FailGpuTerrain(ex);
            }
            finally
            {
                long finalizeStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
                if (matrixPushed) { try { GL.PopMatrix(); } catch { } }
                RenderTexture.active = previous;
                if (detailedProfile) profile.FinalizeMs += ElapsedMilliseconds(finalizeStartTicks);
                double totalMs = ElapsedMilliseconds(frameStartTicks);
                RecordBackRenderProfile(totalMs, detailedProfile, ref profile);
                AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
                if (runtime != null) runtime.Gpu.RecordFrameCost(totalMs);
            }
            return !failed && rendered;
        }

        static void UploadPreparedProjection(Entry entry, ProjectionBatch batch,
            bool detailedProfile, ref BackRenderDetailedProfile profile)
        {
            if (entry == null || batch == null) return;
            UploadPreparedMesh(entry.LandMesh, entry.LandProjectedVertices,
                detailedProfile, ref profile);
            UploadPreparedMesh(entry.WaterMesh, entry.WaterProjectedVertices,
                detailedProfile, ref profile);
            UploadPreparedMesh(entry.ContourMesh, entry.ContourProjectedVertices,
                detailedProfile, ref profile);
            UploadPreparedMesh(entry.CoastlineMesh, entry.CoastlineProjectedVertices,
                detailedProfile, ref profile);
            entry.LastProjectionCenterLatitudeDeg = batch.CenterLatitudeDeg;
            entry.LastProjectionCenterLongitudeDeg = batch.CenterLongitudeDeg;
            entry.LastProjectionBodyRadius = batch.Projection.RadiusMeters;
            entry.LastProjectionRangeMeters = (float)batch.Projection.VerticalMeters;
            entry.LastProjectionAnchorBottom = batch.Projection.AnchorRenderV;
            entry.LastProjectionOrientation = batch.Projection.Orientation;
            if (detailedProfile)
            {
                profile.EntriesReprojected++;
                profile.ProjectedVertices += EntryProjectionVertexCount(entry);
            }
        }

        static void UploadPreparedMesh(Mesh mesh, Vector3[] vertices,
            bool detailedProfile, ref BackRenderDetailedProfile profile)
        {
            if (mesh == null || vertices == null) return;
            long uploadStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            mesh.vertices = vertices;
            if (detailedProfile)
                profile.MeshVertexUploadMs += ElapsedMilliseconds(uploadStartTicks);
            long boundsStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            SetProjectionSafeBounds(mesh);
            if (detailedProfile) profile.BoundsMs += ElapsedMilliseconds(boundsStartTicks);
        }

        bool DrawPreparedEntry(Entry entry, Matrix4x4 mapMatrix,
            bool drawContours, ProjectionBatch batch, bool detailedProfile,
            ref BackRenderDetailedProfile profile)
        {
            if (entry == null || batch == null ||
                (entry.LandMesh == null && entry.WaterMesh == null)) return false;
            if (entry.PreparedColourBatchId == batch.Id && entry.LandMesh != null)
            {
                long colourUploadStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
                entry.LandMesh.colors32 = entry.LandColours;
                if (detailedProfile)
                    profile.ColourUploadMs += ElapsedMilliseconds(colourUploadStartTicks);
                entry.ColourMode = batch.Mode;
                entry.ColourPreset = batch.Preset;
                entry.RelativeAltitudeBucket = batch.RelativeAltitudeBucket;
                entry.TopoMinimumMeters = batch.TopoMinimumMeters;
                entry.TopoMaximumMeters = batch.TopoMaximumMeters;
                entry.PreparedColourBatchId = -1L;
            }
            long drawStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            bool rendered = false;
            if (entry.WaterMesh != null && terrainMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
                rendered = true;
            }
            if (entry.LandMesh != null && terrainMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
                rendered = true;
            }
            if (drawContours && entry.ContourMesh != null && contourMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
            }
            if (entry.CoastlineMesh != null && coastlineMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
            }
            if (detailedProfile) profile.DrawSubmitMs += ElapsedMilliseconds(drawStartTicks);
            return rendered;
        }

        void SwapFrontAndBack(ProjectionBatch batch)
        {
            RenderTexture previousFront = frontTarget;
            frontTarget = backTarget;
            backTarget = previousFront;
            long previousFrontBytes = frontTargetBytes;
            frontTargetBytes = backTargetBytes;
            backTargetBytes = previousFrontBytes;
            frontBufferValid = true;
            frontViewGeneration = batch.ViewGeneration;
            frontTerrainGeneration = batch.TerrainGeneration;
            frontBodyName = batch.BodyName ?? string.Empty;
            frontBodyRadiusMillimetres = batch.BodyRadiusMillimetres;
            frontCenterLatitudeDeg = batch.CenterLatitudeDeg;
            frontCenterLongitudeDeg = batch.CenterLongitudeDeg;
            frontRangeMeters = batch.RangeMeters;
            frontSurfaceRangeMeters = Math.Max(batch.RangeMeters, batch.SurfaceRangeMeters);
            frontMapHeadingDeg = batch.MapHeadingDeg;
            frontTrackUp = batch.TrackUp;
            frontAnchorV = batch.AnchorV;
            frontOrientation = batch.Orientation;
            frontCommittedRealtime = Time.realtimeSinceStartup;
            frontContentRevision = batch.ContentRevision;
            frontWorldSurfaceRevision = batch.WorldSurfaceRevision;
            frontBufferSwaps++;
        }

        void CancelProjectionBatch()
        {
            ProjectionBatch batch = pendingProjectionBatch;
            if (batch == null) return;
            // Do not invalidate already-running jobs: every chunk owns scratch arrays inside
            // the same Entry objects. Let the bounded single batch retire normally, mark it
            // non-committable, then discard it on the main thread. This prevents a reset or
            // RenderTexture resize from starting a successor batch while cancelled workers
            // could still be writing the same managed arrays.
            batch.SubmissionFailed = true;
            if (batch.ExpectedChunks <= 0) batch.Ready = true;
        }

        bool RenderBackBuffer(AERISTerrainHeightTile[] tiles,
            AERISNdMapProjection projection, Matrix4x4 mapRotation, string styleKey,
            AERISTerrainDisplayMode effectiveMode, Vessel vessel, float rangeMeters)
        {
            long frameStartTicks = Stopwatch.GetTimestamp();
            bool detailedProfile = (backProfileSequence++ % BackRenderDetailedProfileStride) == 0L;
            BackRenderDetailedProfile profile = new BackRenderDetailedProfile();
            RenderTexture previous = RenderTexture.active;
            bool matrixPushed = false;
            bool rendered = false;
            bool failed = false;
            try
            {
                long setupStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
                RenderTexture.active = backTarget;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadOrtho();
                // Back is never visible before a complete FAR foundation commit, so a
                // transparent clear cannot expose a black wedge to the user.
                GL.Clear(true, true, Color.clear);
                if (detailedProfile)
                    profile.SetupClearMs += ElapsedMilliseconds(setupStartTicks);
                float projectionThresholdMeters = Math.Max(0.25f,
                    rangeMeters / Math.Max(128f, backTarget.height) * 0.25f);
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    Entry fallbackEntry, currentEntry;
                    ResolveRenderableEntries(tile, styleKey, out fallbackEntry,
                        out currentEntry);
                    Entry drawEntry = currentEntry != null ? currentEntry : fallbackEntry;
                    if (drawEntry == null) continue;
                    if (detailedProfile) profile.TilesVisited++;
                    EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, detailedProfile, ref profile);
                    bool entryRendered = DrawEntry(drawEntry, mapRotation, true, effectiveMode,
                        settings == null ? AERISTerrainColourPreset.Standard :
                        settings.TerrainColourPreset, (float)vessel.altitude,
                        detailedProfile, ref profile);
                    rendered = entryRendered || rendered;
                    if (entryRendered && tile.Key.Lod >= AERISTerrainTileLod.Route)
                        exactDetailOverlayDraws++;
                }
            }
            catch (Exception ex)
            {
                failed = true;
                FailGpuTerrain(ex);
            }
            finally
            {
                long finalizeStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
                if (matrixPushed)
                {
                    try { GL.PopMatrix(); } catch { }
                }
                RenderTexture.active = previous;
                if (detailedProfile)
                    profile.FinalizeMs += ElapsedMilliseconds(finalizeStartTicks);
                double totalMs = ElapsedMilliseconds(frameStartTicks);
                RecordBackRenderProfile(totalMs, detailedProfile, ref profile);
                AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
                if (runtime != null) runtime.Gpu.RecordFrameCost(totalMs);
            }
            return !failed && rendered;
        }

        float MeasureFoundationGpuReadiness(AERISTerrainVisibleTileSet visible,
            AERISTerrainHeightTile[] tiles, string styleKey, out int readyGlobal,
            out int readyFar)
        {
            readyGlobal = 0;
            readyFar = 0;
            if (visible == null || tiles == null) return 0f;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null || tile.Key.Lod != AERISTerrainTileLod.Global &&
                    tile.Key.Lod != AERISTerrainTileLod.Far) continue;
                Entry fallback, current;
                ResolveRenderableEntries(tile, styleKey, out fallback, out current);
                if (current == null || current.CoverageFraction < 0.999f) continue;
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
                mapHeadingDeg)) > 0.5f) return false;
            double displacement = GreatCircleDistanceMeters(vessel.mainBody,
                frontCenterLatitudeDeg, frontCenterLongitudeDeg, centerLatitudeDeg,
                centerLongitudeDeg);
            return !double.IsNaN(displacement) && !double.IsInfinity(displacement) &&
                displacement <= Math.Max(25.0, rangeMeters * 0.0015);
        }

        bool ShouldRefreshBackBuffer(AERISTerrainVisibleTileSet visible,
            bool refreshRequired)
        {
            if (!refreshRequired || visible == null) return false;
            // CP3.5 Gate 1 has exactly one bypass: the very first attempt needed to create
            // an initial FRONT. Once an attempt has occurred, ViewGeneration, content
            // revision, movement, heading and range invalidations all obey the same explicit
            // cadence. This prevents any hidden path from recreating the old forced-render loop.
            if (!frontBufferValid && lastBackAttemptViewGeneration < 0L) return true;
            return Time.realtimeSinceStartup >= nextBackRefreshRealtime;
        }

        static float ResolveBackRefreshCadenceSeconds(float rangeMeters)
        {
            // Candidate 2 decouples display refresh from key-frame generation. The GPU
            // reprojects every Repaint; authoritative exact key frames only have a bounded
            // minimum interval and are otherwise requested adaptively from drift/error/age.
            return KeyFrameMinimumIntervalSeconds;
        }

        static float ResolveHistorySurfaceRange(float visibleRangeMeters)
        {
            float visible = Math.Max(1f, visibleRangeMeters);
            return Mathf.Clamp(visible * HistoryOverscanScale, visible,
                MaximumHistorySurfaceRangeMeters);
        }

        bool NeedsKeyFrameRefresh(AERISTerrainVisibleTileSet visible, Vessel vessel,
            float rangeMeters, float surfaceRangeMeters, bool temporalAvailable,
            double temporalErrorPixels, float temporalUvMargin, float temporalDriftPixels,
            float temporalHeadingDeltaDeg)
        {
            if (!frontBufferValid || visible == null || vessel == null ||
                vessel.mainBody == null) return true;
            if (frontTerrainGeneration != visible.TerrainGeneration ||
                frontContentRevision != gpuContentRevision ||
                !string.Equals(frontBodyName, visible.BodyName,
                    StringComparison.OrdinalIgnoreCase)) return true;
            long bodyRadiusMillimetres = (long)Math.Round(
                Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);
            if (bodyRadiusMillimetres != frontBodyRadiusMillimetres) return true;
            if (Math.Abs(frontRangeMeters - rangeMeters) >
                Math.Max(1f, rangeMeters * 0.001f)) return true;
            if (Math.Abs(frontSurfaceRangeMeters - surfaceRangeMeters) >
                Math.Max(1f, surfaceRangeMeters * 0.001f)) return true;
            float age = Math.Max(0f, Time.realtimeSinceStartup - frontCommittedRealtime);
            if (age >= KeyFrameMaximumAgeSeconds) return true;
            if (!temporalAvailable) return true;
            if (temporalErrorPixels >= KeyFrameRefreshErrorPixels) return true;
            if (temporalDriftPixels >= KeyFrameRefreshDriftPixels) return true;
            if (temporalHeadingDeltaDeg >= KeyFrameRefreshHeadingDeg) return true;
            float marginPixels = temporalUvMargin * Math.Max(1,
                Math.Min(frontTarget == null ? 1 : frontTarget.width,
                    frontTarget == null ? 1 : frontTarget.height));
            return marginPixels < 18f;
        }

        bool CanPresentLatchedFront(AERISTerrainVisibleTileSet visible, Vessel vessel,
            float currentRangeMeters, bool currentTrackUp, float currentAnchorV,
            AERISTerrainRenderTargetOrientation currentOrientation)
        {
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||
                visible == null || vessel == null || vessel.mainBody == null) return false;
            // A latch may bridge movement/content-generation supply gaps only inside the
            // same display scale/orientation. Presenting a 160 km FRONT after the pilot has
            // selected 5 km is not continuity; it is a false projection and amplifies
            // ownship/symbology displacement by the range ratio.
            if (Math.Abs(frontRangeMeters - currentRangeMeters) >
                Math.Max(1f, currentRangeMeters * 0.001f) ||
                frontTrackUp != currentTrackUp || frontOrientation != currentOrientation ||
                Math.Abs(frontAnchorV - currentAnchorV) > 0.001f) return false;
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
            presentedProjection.Reprojected = false;
            presentedProjection.ReprojectionErrorPixels = 0f;
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


        void CapturePresentedProjectionCurrent(double centerLatitudeDeg,
            double centerLongitudeDeg, float rangeMeters, float mapHeadingDeg,
            bool trackUp, float anchorV, AERISTerrainRenderTargetOrientation orientation,
            double reprojectionErrorPixels)
        {
            presentedProjection.Valid = frontBufferValid;
            presentedProjection.Latched = false;
            presentedProjection.Reprojected = true;
            presentedProjection.ReprojectionErrorPixels = (float)Math.Max(0.0,
                reprojectionErrorPixels);
            presentedProjection.CenterLatitudeDeg = centerLatitudeDeg;
            presentedProjection.CenterLongitudeDeg = centerLongitudeDeg;
            presentedProjection.RangeMeters = rangeMeters;
            presentedProjection.MapHeadingDeg = mapHeadingDeg;
            presentedProjection.TrackUp = trackUp;
            presentedProjection.AnchorV = anchorV;
            presentedProjection.Orientation = orientation;
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

        bool PresentFrontDirect(Rect plot,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (frontTarget == null || !frontTarget.IsCreated() ||
                !frontBufferValid || frontRangeMeters <= 0f ||
                frontSurfaceRangeMeters <= 0f) return false;

            // frontTarget is an overscan exact key-frame. Recover the committed visible ND
            // range by cropping about the same projection pivots used by AERISNdMapProjection:
            // horizontal center 0.5 and the committed vertical anchor. This is an exact
            // scale crop because center, heading and orientation are unchanged.
            float ratio = Mathf.Clamp01(frontRangeMeters /
                Math.Max(frontRangeMeters, frontSurfaceRangeMeters));
            float u0 = 0.5f + (0f - 0.5f) * ratio;
            float u1 = 0.5f + (1f - 0.5f) * ratio;
            float guiV0 = frontAnchorV + (0f - frontAnchorV) * ratio;
            float guiV1 = frontAnchorV + (1f - frontAnchorV) * ratio;
            float uvY, uvH;
            if (orientation == AERISTerrainRenderTargetOrientation.Flipped)
            {
                uvY = 1f - guiV0;
                uvH = -(guiV1 - guiV0);
            }
            else
            {
                uvY = guiV0;
                uvH = guiV1 - guiV0;
            }
            Rect uv = new Rect(u0, uvY, u1 - u0, uvH);
            GUI.DrawTextureWithTexCoords(plot, frontTarget, uv, true);
            return true;
        }

        static void PresentTextureDirect(Rect plot, Texture texture,
            AERISTerrainRenderTargetOrientation orientation)
        {
            if (texture == null) return;
            bool flipVertically = orientation ==
                AERISTerrainRenderTargetOrientation.Flipped;
            Rect uv = flipVertically ? new Rect(0f, 1f, 1f, -1f) :
                new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(plot, texture, uv, true);
        }

        bool TryBuildTemporalReprojection(Vessel vessel,
            AERISNdMapProjection currentProjection, float currentRangeMeters,
            out double maxErrorPixels, out float minimumUvMargin,
            out float driftPixels, out float headingDeltaDeg)
        {
            maxErrorPixels = double.PositiveInfinity;
            minimumUvMargin = -1f;
            driftPixels = float.PositiveInfinity;
            headingDeltaDeg = 180f;
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated() ||
                vessel == null || vessel.mainBody == null || frontSurfaceRangeMeters <= 1f)
                return false;
            if (!string.Equals(frontBodyName, vessel.mainBody.bodyName,
                    StringComparison.OrdinalIgnoreCase)) return false;
            long bodyRadiusMillimetres = (long)Math.Round(
                Math.Max(0.0, vessel.mainBody.Radius) * 1000.0);
            if (bodyRadiusMillimetres != frontBodyRadiusMillimetres) return false;
            if (Math.Abs(frontRangeMeters - currentRangeMeters) >
                Math.Max(1f, currentRangeMeters * 0.001f)) return false;
            if (Time.realtimeSinceStartup - frontCommittedRealtime > 8.0f) return false;

            long startTicks = Stopwatch.GetTimestamp();
            AERISNdMapProjection sourceProjection = AERISNdMapProjection.Create(
                vessel.mainBody, frontCenterLatitudeDeg, frontCenterLongitudeDeg,
                frontSurfaceRangeMeters, frontMapHeadingDeg, frontTrackUp,
                frontAnchorV, frontOrientation);
            minimumUvMargin = 1f;
            for (int gy = 0; gy < TemporalGridPointsPerAxis; gy++)
            {
                float guiV = gy / (float)TemporalGridCells;
                for (int gx = 0; gx < TemporalGridPointsPerAxis; gx++)
                {
                    float guiU = gx / (float)TemporalGridCells;
                    double latitudeDeg, longitudeDeg;
                    currentProjection.UnprojectGuiToLatitudeLongitude(guiU, guiV,
                        out latitudeDeg, out longitudeDeg);
                    float sourceGuiU, sourceGuiV;
                    sourceProjection.ProjectLatitudeLongitudeToGui(latitudeDeg,
                        longitudeDeg, out sourceGuiU, out sourceGuiV);
                    if (!Finite(sourceGuiU) || !Finite(sourceGuiV)) return false;
                    float sourceRenderV = frontOrientation ==
                        AERISTerrainRenderTargetOrientation.Flipped ?
                        sourceGuiV : 1f - sourceGuiV;
                    if (!Finite(sourceRenderV)) return false;
                    int index = gy * TemporalGridPointsPerAxis + gx;
                    temporalSourceUv[index] = new Vector2(sourceGuiU, sourceRenderV);
                    minimumUvMargin = Math.Min(minimumUvMargin,
                        Math.Min(Math.Min(sourceGuiU, 1f - sourceGuiU),
                            Math.Min(sourceRenderV, 1f - sourceRenderV)));
                }
            }

            maxErrorPixels = 0.0;
            for (int gy = 0; gy < TemporalGridCells; gy++)
            {
                float guiV = (gy + 0.5f) / TemporalGridCells;
                for (int gx = 0; gx < TemporalGridCells; gx++)
                {
                    float guiU = (gx + 0.5f) / TemporalGridCells;
                    double latitudeDeg, longitudeDeg;
                    currentProjection.UnprojectGuiToLatitudeLongitude(guiU, guiV,
                        out latitudeDeg, out longitudeDeg);
                    float exactU, exactGuiV;
                    sourceProjection.ProjectLatitudeLongitudeToGui(latitudeDeg,
                        longitudeDeg, out exactU, out exactGuiV);
                    if (!Finite(exactU) || !Finite(exactGuiV)) return false;
                    float exactV = frontOrientation ==
                        AERISTerrainRenderTargetOrientation.Flipped ?
                        exactGuiV : 1f - exactGuiV;
                    if (!Finite(exactV)) return false;
                    int i00 = gy * TemporalGridPointsPerAxis + gx;
                    int i10 = i00 + 1;
                    int i01 = i00 + TemporalGridPointsPerAxis;
                    int i11 = i01 + 1;
                    Vector2 interpolated = (temporalSourceUv[i00] +
                        temporalSourceUv[i10] + temporalSourceUv[i01] +
                        temporalSourceUv[i11]) * 0.25f;
                    double dx = (interpolated.x - exactU) * Math.Max(1, frontTarget.width);
                    double dy = (interpolated.y - exactV) * Math.Max(1, frontTarget.height);
                    double error = Math.Sqrt(dx * dx + dy * dy);
                    if (error > maxErrorPixels) maxErrorPixels = error;
                }
            }

            float centerSourceU, centerSourceGuiV;
            sourceProjection.ProjectLatitudeLongitudeToGui(
                UnitLatitude(currentProjection.CenterX, currentProjection.CenterY,
                    currentProjection.CenterZ),
                UnitLongitude(currentProjection.CenterX, currentProjection.CenterY),
                out centerSourceU, out centerSourceGuiV);
            float dxPixels = (centerSourceU - 0.5f) * Math.Max(1, frontTarget.width);
            float dyPixels = (centerSourceGuiV - frontAnchorV) *
                Math.Max(1, frontTarget.height);
            driftPixels = Mathf.Sqrt(dxPixels * dxPixels + dyPixels * dyPixels);
            float sourceHeadingDeg = frontTrackUp ? frontMapHeadingDeg : 0f;
            float currentHeadingDeg = currentProjection.TrackUp ?
                Mathf.Atan2(currentProjection.HeadingSin, currentProjection.HeadingCos) *
                Mathf.Rad2Deg : 0f;
            headingDeltaDeg = frontTrackUp == currentProjection.TrackUp ?
                Mathf.Abs(Mathf.DeltaAngle(sourceHeadingDeg, currentHeadingDeg)) : 180f;
            temporalGridMilliseconds += ElapsedMilliseconds(startTicks);
            if (double.IsNaN(maxErrorPixels) || double.IsInfinity(maxErrorPixels) ||
                !Finite(minimumUvMargin) || !Finite(driftPixels) ||
                !Finite(headingDeltaDeg)) return false;
            return minimumUvMargin >= TemporalMinimumUvMargin &&
                maxErrorPixels <= TemporalMaximumErrorPixels;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        bool RenderTemporalReprojection(Rect plot,
            AERISNdMapProjection currentProjection, double errorPixels,
            float uvMargin)
        {
            if (frontTarget == null || presentationTarget == null ||
                reprojectionMaterial == null || !frontTarget.IsCreated() ||
                !presentationTarget.IsCreated()) return false;
            for (int i = 0; i < temporalSourceUv.Length; i++)
                if (!Finite(temporalSourceUv[i].x) || !Finite(temporalSourceUv[i].y))
                    return false;
            reprojectionMaterial.mainTexture = frontTarget;
            reprojectionMaterial.color = Color.white;
            // Failed material setup must never clear a previously valid presentation surface.
            if (!reprojectionMaterial.SetPass(0)) return false;
            RenderTexture previous = RenderTexture.active;
            bool matrixPushed = false;
            long submitTicks = Stopwatch.GetTimestamp();
            try
            {
                RenderTexture.active = presentationTarget;
                GL.PushMatrix();
                matrixPushed = true;
                GL.LoadOrtho();
                GL.Clear(true, true, Color.clear);
                GL.Begin(GL.QUADS);
                for (int gy = 0; gy < TemporalGridCells; gy++)
                {
                    float guiV0 = gy / (float)TemporalGridCells;
                    float guiV1 = (gy + 1f) / TemporalGridCells;
                    float renderV0 = currentProjection.Orientation ==
                        AERISTerrainRenderTargetOrientation.Flipped ?
                        guiV0 : 1f - guiV0;
                    float renderV1 = currentProjection.Orientation ==
                        AERISTerrainRenderTargetOrientation.Flipped ?
                        guiV1 : 1f - guiV1;
                    for (int gx = 0; gx < TemporalGridCells; gx++)
                    {
                        float u0 = gx / (float)TemporalGridCells;
                        float u1 = (gx + 1f) / TemporalGridCells;
                        int i00 = gy * TemporalGridPointsPerAxis + gx;
                        int i10 = i00 + 1;
                        int i01 = i00 + TemporalGridPointsPerAxis;
                        int i11 = i01 + 1;
                        EmitTemporalVertex(temporalSourceUv[i00], u0, renderV0);
                        EmitTemporalVertex(temporalSourceUv[i10], u1, renderV0);
                        EmitTemporalVertex(temporalSourceUv[i11], u1, renderV1);
                        EmitTemporalVertex(temporalSourceUv[i01], u0, renderV1);
                    }
                }
                GL.End();
            }
            catch (Exception ex)
            {
                FailGpuTerrain(ex);
                return false;
            }
            finally
            {
                if (matrixPushed) { try { GL.PopMatrix(); } catch { } }
                RenderTexture.active = previous;
                temporalSubmitMilliseconds += ElapsedMilliseconds(submitTicks);
            }
            PresentTextureDirect(plot, presentationTarget, currentProjection.Orientation);
            return true;
        }

        static void EmitTemporalVertex(Vector2 sourceUv, float outputU,
            float outputRenderV)
        {
            GL.TexCoord2(sourceUv.x, sourceUv.y);
            GL.Vertex3(outputU, outputRenderV, 0f);
        }

        static float ResolveTemporalConfidence(double errorPixels, float uvMargin)
        {
            float errorConfidence = 1f - Mathf.Clamp01((float)(errorPixels /
                Math.Max(0.001, TemporalMaximumErrorPixels)));
            float marginConfidence = Mathf.Clamp01((uvMargin - TemporalMinimumUvMargin) /
                Math.Max(0.001f, 0.05f - TemporalMinimumUvMargin));
            return Mathf.Clamp01(Math.Min(errorConfidence, marginConfidence));
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
                (lastHistoryReprojected ? "TEMPORAL" :
                (lastFrontBufferLatched ? "EXACT_LATCH" : "EXACT"));
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
                forcedRecoveryBackRenders + "; forced_recovery_suppressed=" +
                suppressedForcedRecoveryFrames + "; back_cadence_s=" +
                lastBackRefreshCadenceSeconds.ToString("F2", CultureInfo.InvariantCulture) +
                "; gen_bridge_frames=" +
                generationBridgeFrames + "; gen_bridge_rejects=" +
                generationBridgeRejects + "; front_gen=" + frontTerrainGeneration +
                "; current_gen=" + (visible == null ? -1L : visible.TerrainGeneration) +
                "; ready_build_violation=" + readyBuildingViolations +
                "; history_surface_range=" +
                frontSurfaceRangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; history_frames_quarantined=" + historyReprojectFrames +
                "; history_reject=" + historyRejectedFrames + "; direct_frames=" +
                directFrontFrames + "; exact_authority_frames=" + exactFrontAuthorityFrames +
                "; temporal_shadow_eligible=" + temporalShadowEligibleFrames +
                "; temporal_authority_blocked=" + temporalPresentationBlockedFrames +
                "; blocked=" + blockedIncompleteSwaps +
                "; render_ready=" + renderReadyFields.Count + "/" + renderReadyBytes +
                "; virtual_builds=" + virtualRouteBuilds + "/" +
                virtualLocalBuilds + "; exact_overlay_draws=" +
                exactDetailOverlayDraws + "; cpu_terrain_draw=0.");
            LogBackRenderProfile();
            LogGate2ParallelProjection();
            LogGate2TemporalReprojection();
        }

        void LogGate2ParallelProjection()
        {
            long completed = Math.Max(1L, projectionBatchesCompleted);
            AERISLogger.Info("[CP3.5_GATE2_PARALLEL] submitted=" +
                projectionBatchesSubmitted + "; completed=" + projectionBatchesCompleted +
                "; discarded=" + projectionBatchesDiscarded +
                "; admission_failed=" + projectionBatchesSubmissionFailed +
                "; pending=" + (pendingProjectionBatch == null ? "0" : "1") +
                "; workers_last=" + lastProjectionWorkerCount +
                "; worker_cpu_ms_per_completed=" +
                (projectionWorkerMilliseconds / completed).ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; worker_wall_ms_per_completed=" +
                (projectionWorkerWallMilliseconds / completed).ToString("F3",
                    CultureInfo.InvariantCulture) +
                "; projected_vertices=" + projectionWorkerProjectedVertices +
                "; colour_vertices=" + projectionWorkerColourVertices +
                "; keyframe_min_interval_s=" + KeyFrameMinimumIntervalSeconds.ToString("F2",
                    CultureInfo.InvariantCulture) + ".");
        }

        void LogGate2TemporalReprojection()
        {
            long frames = Math.Max(1L, temporalFrames);
            AERISLogger.Info("[CP3.5_GATE2_TEMPORAL] frames=" + temporalFrames +
                "; rejects=" + temporalRejects + "; keyframe_requests=" +
                temporalKeyFrameRequests + "; overscan_scale=" +
                HistoryOverscanScale.ToString("F2", CultureInfo.InvariantCulture) +
                "; grid=" + TemporalGridCells + "x" + TemporalGridCells +
                "; max_error_px=" + lastTemporalMaxErrorPixels.ToString("F3",
                    CultureInfo.InvariantCulture) + "; min_uv_margin=" +
                lastTemporalMinUvMargin.ToString("F4", CultureInfo.InvariantCulture) +
                "; drift_px=" + lastTemporalDriftPixels.ToString("F1",
                    CultureInfo.InvariantCulture) + "; heading_delta_deg=" +
                lastTemporalHeadingDeltaDeg.ToString("F2", CultureInfo.InvariantCulture) +
                "; grid_cpu_ms_per_frame=" + (temporalGridMilliseconds / frames).ToString("F3",
                    CultureInfo.InvariantCulture) + "; submit_ms_per_frame=" +
                (temporalSubmitMilliseconds / frames).ToString("F3",
                    CultureInfo.InvariantCulture) + "; confidence=" +
                lastHistoryConfidence.ToString("F3", CultureInfo.InvariantCulture) +
                "; presentation_authority=" +
                (TemporalPresentationAuthorityEnabled ? "TEMPORAL_ALLOWED" :
                    "EXACT_FRONT_ONLY") + "; shadow_eligible=" +
                temporalShadowEligibleFrames + "; authority_blocked=" +
                temporalPresentationBlockedFrames + ".");
        }

        static double ElapsedMilliseconds(long startTicks)
        {
            return (Stopwatch.GetTimestamp() - startTicks) * 1000.0 / Stopwatch.Frequency;
        }

        void RecordBackRenderProfile(double totalMs, bool detailed,
            ref BackRenderDetailedProfile profile)
        {
            backProfileAllSamples++;
            backProfileAllTotalMs += totalMs;
            if (totalMs > backProfileAllMaxTotalMs) backProfileAllMaxTotalMs = totalMs;
            if (!detailed) return;
            backProfileDetailedSamples++;
            backProfileDetailedTotalMs += totalMs;
            backProfileSetupClearMs += profile.SetupClearMs;
            backProfileProjectionCpuMs += profile.ProjectionCpuMs;
            backProfileMeshVertexUploadMs += profile.MeshVertexUploadMs;
            backProfileBoundsMs += profile.BoundsMs;
            backProfileColourCpuMs += profile.ColourCpuMs;
            backProfileColourUploadMs += profile.ColourUploadMs;
            backProfileDrawSubmitMs += profile.DrawSubmitMs;
            backProfileWorldSurfaceMs += profile.WorldSurfaceMs;
            backProfileFinalizeMs += profile.FinalizeMs;
            backProfileProjectedVertices += profile.ProjectedVertices;
            backProfileDrawCalls += profile.DrawCalls;
            backProfileWorldSurfacePrimitives += profile.WorldSurfacePrimitives;
            backProfileTilesVisited += profile.TilesVisited;
            backProfileEntriesReprojected += profile.EntriesReprojected;
        }

        void LogBackRenderProfile()
        {
            if (backProfileAllSamples <= 0) return;
            double allDivisor = Math.Max(1L, backProfileAllSamples);
            double detailedDivisor = Math.Max(1L, backProfileDetailedSamples);
            double sampledKnown = backProfileSetupClearMs + backProfileProjectionCpuMs +
                backProfileMeshVertexUploadMs + backProfileBoundsMs +
                backProfileColourCpuMs + backProfileColourUploadMs +
                backProfileDrawSubmitMs + backProfileWorldSurfaceMs +
                backProfileFinalizeMs;
            double sampledOther = Math.Max(0.0, backProfileDetailedTotalMs - sampledKnown);
            AERISLogger.Info("[CP3.5_GATE1_BACK_PROFILE] all_samples=" +
                backProfileAllSamples + "; detailed_samples=" + backProfileDetailedSamples +
                "; detailed_stride=" + BackRenderDetailedProfileStride +
                "; total_all_avg_ms=" + (backProfileAllTotalMs / allDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; total_all_max_ms=" + backProfileAllMaxTotalMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; sampled_total_avg_ms=" + (backProfileDetailedTotalMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; setup_clear_avg_ms=" + (backProfileSetupClearMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; projection_cpu_avg_ms=" + (backProfileProjectionCpuMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; mesh_vertex_upload_avg_ms=" + (backProfileMeshVertexUploadMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; bounds_avg_ms=" + (backProfileBoundsMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; colour_cpu_avg_ms=" + (backProfileColourCpuMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; colour_upload_avg_ms=" + (backProfileColourUploadMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; draw_submit_avg_ms=" + (backProfileDrawSubmitMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; world_surface_avg_ms=" + (backProfileWorldSurfaceMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; world_surface_primitives_avg=" + (backProfileWorldSurfacePrimitives / detailedDivisor).ToString("F1", CultureInfo.InvariantCulture) +
                "; finalize_avg_ms=" + (backProfileFinalizeMs / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; other_avg_ms=" + (sampledOther / detailedDivisor).ToString("F3", CultureInfo.InvariantCulture) +
                "; tiles_avg=" + (backProfileTilesVisited / detailedDivisor).ToString("F1", CultureInfo.InvariantCulture) +
                "; reprojected_entries_avg=" + (backProfileEntriesReprojected / detailedDivisor).ToString("F1", CultureInfo.InvariantCulture) +
                "; projected_vertices_avg=" + (backProfileProjectedVertices / detailedDivisor).ToString("F0", CultureInfo.InvariantCulture) +
                "; draw_calls_avg=" + (backProfileDrawCalls / detailedDivisor).ToString("F1", CultureInfo.InvariantCulture) +
                "; cadence_s=" + lastBackRefreshCadenceSeconds.ToString("F2", CultureInfo.InvariantCulture) + ".");
            backProfileAllSamples = 0L;
            backProfileAllTotalMs = 0.0;
            backProfileAllMaxTotalMs = 0.0;
            backProfileDetailedSamples = 0L;
            backProfileDetailedTotalMs = 0.0;
            backProfileSetupClearMs = 0.0;
            backProfileProjectionCpuMs = 0.0;
            backProfileMeshVertexUploadMs = 0.0;
            backProfileBoundsMs = 0.0;
            backProfileColourCpuMs = 0.0;
            backProfileColourUploadMs = 0.0;
            backProfileDrawSubmitMs = 0.0;
            backProfileWorldSurfaceMs = 0.0;
            backProfileFinalizeMs = 0.0;
            backProfileProjectedVertices = 0L;
            backProfileDrawCalls = 0L;
            backProfileWorldSurfacePrimitives = 0L;
            backProfileTilesVisited = 0L;
            backProfileEntriesReprojected = 0L;
        }

        void ResetFrontBufferState()
        {
            frontBufferValid = false;
            frontViewGeneration = -1L;
            frontTerrainGeneration = -1L;
            frontBodyName = string.Empty;
            frontBodyRadiusMillimetres = 0L;
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
            frontWorldSurfaceRevision = -1L;
            lastBackAttemptViewGeneration = -1L;
            lastBackAttemptContentRevision = -1L;
            nextBackRefreshRealtime = 0f;
            lastFrontBufferPresented = false;
            lastFrontBufferLatched = false;
            presentedProjection.Valid = false;
            presentedProjection.Reprojected = false;
            presentedProjection.ReprojectionErrorPixels = 0f;
            lastHistoryReprojected = false;
            lastHistoryConfidence = 0f;
            lastBackFoundationCoverage = 0f;
            readyBuildingSinceRealtime = -1f;
            readyBuildingViolationLatched = false;
            CancelProjectionBatch();
        }

        void Schedule(AERISTerrainHeightTile tile, string styleKey,
            float contourInterval, AERISTerrainVirtualDetailProfile virtualDetail)
        {
            string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
            if (requested.Contains(cacheKey + "|PENDING")) return;
            requested.Add(cacheKey + "|PENDING");
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
                    entries[cacheKey] = entry;
                    usedEntryBytes += entry.Bytes;
                    uploaded++;
                    gpuContentRevision++;
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

        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string styleKey,
            AERISTerrainTileSystem system, out Entry entry)
        {
            entry = null;
            if (tile == null) return false;
            string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
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
                entries[cacheKey] = entry;
                usedEntryBytes += entry.Bytes;
                uploaded++;
                gpuContentRevision++;
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
            foreach (Entry entry in entries.Values)
            {
                if (entry == null || string.Equals(entry.CacheKey, keepCacheKey,
                    StringComparison.Ordinal)) continue;
                if (entry.TileKey.Equals(key)) supersededScratch.Add(entry);
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

        static Entry BuildEntry(string cacheKey,
            AERISTerrainRenderReadyHeightField result)
        {
            var land = new SurfaceBuilder();
            var water = new SurfaceBuilder();
            var clipped = new SurfacePoint[6];
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
            Mesh contourMesh = BuildLineMesh("AERIS_TERRAIN_CONTOUR_" +
                result.Key.FileStem, result.ContourSegments,
                new Color32(255, 255, 255, 210), out contourSource);
            Mesh coastlineMesh = BuildCoastlineMesh("AERIS_TERRAIN_COAST_" +
                result.Key.FileStem, result.CoastlineSegments, out coastlineSource);

            // Each drawable vertex retains one unit-sphere point (3 doubles) and one
            // projected Vector3 (3 floats) so cache accounting remains conservative.
            long projectedVertexBytes = (long)(land.Vertices.Count +
                water.Vertices.Count + (contourSource == null ? 0 : contourSource.Length) +
                (coastlineSource == null ? 0 : coastlineSource.Length)) * (3L * 8L + 3L * 4L);
            long bytes = result.Valid.Length + projectedVertexBytes +
                land.Vertices.Count * (3L * 4L + 4L + 4L) +
                water.Vertices.Count * (3L * 4L + 4L) +
                (land.Triangles.Count + water.Triangles.Count) * 4L;
            if (result.ContourSegments != null)
                bytes += result.ContourSegments.Length * 4L;
            if (result.CoastlineSegments != null)
                bytes += result.CoastlineSegments.Length * 4L * 4L;
            return new Entry
            {
                CacheKey = cacheKey,
                TileKey = result.Key,
                TileCreatedUtcTicks = result.TileCreatedUtcTicks,
                StyleKey = result.StyleKey,
                LandMesh = landMesh,
                WaterMesh = waterMesh,
                ContourMesh = contourMesh,
                CoastlineMesh = coastlineMesh,
                LandGeographicPoints = BuildGeographicPoints(landSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                WaterGeographicPoints = BuildGeographicPoints(waterSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                ContourGeographicPoints = BuildGeographicPoints(contourSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastlineGeographicPoints = BuildGeographicPoints(coastlineSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                LandProjectedVertices = AllocateProjectedVertices(landSource),
                WaterProjectedVertices = AllocateProjectedVertices(waterSource),
                ContourProjectedVertices = AllocateProjectedVertices(contourSource),
                CoastlineProjectedVertices = AllocateProjectedVertices(coastlineSource),
                SouthLatitudeDeg = result.SouthLatitudeDeg,
                NorthLatitudeDeg = result.NorthLatitudeDeg,
                WestLongitudeDeg = result.WestLongitudeDeg,
                EastLongitudeDeg = result.EastLongitudeDeg,
                LandElevationMeters = land.Elevation.ToArray(),
                LandShade = land.Shade.ToArray(),
                LandColours = new Color32[land.Vertices.Count],
                Resolution = result.Resolution,
                Valid = (byte[])result.Valid.Clone(),
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
            SurfacePoint[] input = { a, b, c };
            int count = 0;
            for (int i = 0; i < 3; i++)
            {
                SurfacePoint current = input[i];
                SurfacePoint next = input[(i + 1) % 3];
                bool currentInside = current.Water == targetWater;
                bool nextInside = next.Water == targetWater;
                if (currentInside) output[count++] = current;
                if (currentInside != nextInside)
                    output[count++] = CoastBoundaryPoint(current, next, targetWater);
            }
            builder.AddPolygon(output, count);
        }

        static Mesh BuildSurfaceMesh(string name, SurfaceBuilder builder, bool water,
            out Vector3[] sourceVertices)
        {
            sourceVertices = null;
            if (builder == null || builder.Vertices.Count < 3 ||
                builder.Triangles.Count < 3) return null;
            var colours = new Color32[builder.Vertices.Count];
            Color32 initial = water ? ResolveWaterColour() :
                new Color32(255, 255, 255, 255);
            for (int i = 0; i < colours.Length; i++) colours[i] = initial;
            var mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.MarkDynamic();
            sourceVertices = builder.Vertices.ToArray();
            mesh.vertices = sourceVertices;
            mesh.colors32 = colours;
            mesh.triangles = builder.Triangles.ToArray();
            SetProjectionSafeBounds(mesh);
            // Colours and geographic projection are updated in flight; retain CPU access.
            mesh.UploadMeshData(false);
            return mesh;
        }

        static Mesh BuildLineMesh(string name, float[] segments, Color32 colour,
            out Vector3[] sourceVertices)
        {
            sourceVertices = null;
            if (segments == null || segments.Length < 4 || segments.Length % 4 != 0)
                return null;
            int vertexCount = segments.Length / 2;
            var vertices = new Vector3[vertexCount];
            var indices = new int[vertexCount];
            var colours = new Color32[vertexCount];
            for (int i = 0; i < vertexCount; i++)
            {
                vertices[i] = new Vector3(segments[i * 2], segments[i * 2 + 1], 0f);
                indices[i] = i;
                colours[i] = colour;
            }
            var mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.MarkDynamic();
            sourceVertices = vertices;
            mesh.vertices = sourceVertices;
            mesh.colors32 = colours;
            mesh.SetIndices(indices, MeshTopology.Lines, 0);
            SetProjectionSafeBounds(mesh);
            mesh.UploadMeshData(false);
            return mesh;
        }

        static Mesh BuildCoastlineMesh(string name, float[] segments,
            out Vector3[] sourceVertices)
        {
            sourceVertices = null;
            if (segments == null || segments.Length < 4 || segments.Length % 4 != 0)
                return null;
            var vertices = new List<Vector3>(segments.Length);
            var triangles = new List<int>(segments.Length * 3 / 2);
            var colours = new List<Color32>(segments.Length);
            Color32 colour = new Color32(185, 225, 255, 245);
            for (int i = 0; i < segments.Length; i += 4)
            {
                float x0 = segments[i], y0 = segments[i + 1];
                float x1 = segments[i + 2], y1 = segments[i + 3];
                float dx = x1 - x0, dy = y1 - y0;
                float length = Mathf.Sqrt(dx * dx + dy * dy);
                if (length <= 0.000001f) continue;
                float nx = -dy / length * CoastlineHalfWidthNormalized;
                float ny = dx / length * CoastlineHalfWidthNormalized;
                int start = vertices.Count;
                vertices.Add(new Vector3(x0 + nx, y0 + ny, 0f));
                vertices.Add(new Vector3(x0 - nx, y0 - ny, 0f));
                vertices.Add(new Vector3(x1 + nx, y1 + ny, 0f));
                vertices.Add(new Vector3(x1 - nx, y1 - ny, 0f));
                for (int j = 0; j < 4; j++) colours.Add(colour);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start + 2); triangles.Add(start + 1); triangles.Add(start + 3);
            }
            if (vertices.Count == 0) return null;
            var mesh = new Mesh();
            mesh.name = name;
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.MarkDynamic();
            sourceVertices = vertices.ToArray();
            mesh.vertices = sourceVertices;
            mesh.colors32 = colours.ToArray();
            mesh.triangles = triangles.ToArray();
            SetProjectionSafeBounds(mesh);
            mesh.UploadMeshData(false);
            return mesh;
        }

        static void SetProjectionSafeBounds(Mesh mesh)
        {
            if (mesh == null) return;
            // Projection vertices may sit outside the visible 0..1 viewport while the tile
            // crosses an edge.  Use one conservative fixed bound instead of rebuilding
            // bounds for ~500k vertices every BACK. Graphics.DrawMeshNow still clips against
            // the render target, and the wide bound prevents false Unity-side culling.
            mesh.bounds = new Bounds(new Vector3(0.5f, 0.5f, 0f),
                new Vector3(16f, 16f, 2f));
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

        static void EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters,
            bool detailedProfile, ref BackRenderDetailedProfile profile)
        {
            if (entry == null) return;
            bool projectionChanged = double.IsNaN(entry.LastProjectionCenterLatitudeDeg) ||
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
                    UnitLatitude(context.CenterX, context.CenterY, context.CenterZ),
                    UnitLongitude(context.CenterX, context.CenterY),
                    out east, out north);
                projectionChanged = east * east + north * north >=
                    movementThresholdMeters * movementThresholdMeters;
            }
            if (!projectionChanged) return;
            if (detailedProfile) profile.EntriesReprojected++;

            ProjectMesh(entry.LandMesh, entry.LandGeographicPoints,
                entry.LandProjectedVertices, context, detailedProfile, ref profile);
            ProjectMesh(entry.WaterMesh, entry.WaterGeographicPoints,
                entry.WaterProjectedVertices, context, detailedProfile, ref profile);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,
                entry.ContourProjectedVertices, context, detailedProfile, ref profile);
            ProjectMesh(entry.CoastlineMesh, entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices, context, detailedProfile, ref profile);
            entry.LastProjectionCenterLatitudeDeg =
                UnitLatitude(context.CenterX, context.CenterY, context.CenterZ);
            entry.LastProjectionCenterLongitudeDeg =
                UnitLongitude(context.CenterX, context.CenterY);
            entry.LastProjectionBodyRadius = context.RadiusMeters;
            entry.LastProjectionRangeMeters = (float)context.VerticalMeters;
            entry.LastProjectionAnchorBottom = context.AnchorRenderV;
            entry.LastProjectionOrientation = context.Orientation;
        }

        static void ProjectMesh(Mesh mesh, GeographicUnitPoint[] points,
            Vector3[] projectedVertices, AERISNdMapProjection context,
            bool detailedProfile, ref BackRenderDetailedProfile profile)
        {
            if (mesh == null || points == null || projectedVertices == null ||
                points.Length != projectedVertices.Length) return;
            if (!detailedProfile)
            {
                for (int i = 0; i < points.Length; i++)
                {
                    GeographicUnitPoint point = points[i];
                    float u, v;
                    context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                        out u, out v);
                    projectedVertices[i] = new Vector3(u, v, 0f);
                }
                mesh.vertices = projectedVertices;
                SetProjectionSafeBounds(mesh);
                return;
            }

            long projectionStartTicks = Stopwatch.GetTimestamp();
            for (int i = 0; i < points.Length; i++)
            {
                GeographicUnitPoint point = points[i];
                float u, v;
                context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                    out u, out v);
                projectedVertices[i] = new Vector3(u, v, 0f);
            }
            profile.ProjectionCpuMs += ElapsedMilliseconds(projectionStartTicks);
            profile.ProjectedVertices += points.LongLength;

            long uploadStartTicks = Stopwatch.GetTimestamp();
            mesh.vertices = projectedVertices;
            profile.MeshVertexUploadMs += ElapsedMilliseconds(uploadStartTicks);

            long boundsStartTicks = Stopwatch.GetTimestamp();
            SetProjectionSafeBounds(mesh);
            profile.BoundsMs += ElapsedMilliseconds(boundsStartTicks);
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
            float aircraftAltitudeAslMeters, bool detailedProfile,
            ref BackRenderDetailedProfile profile)
        {
            if (entry == null || (entry.LandMesh == null && entry.WaterMesh == null))
                return false;
            EnsureLandColours(entry, mode, preset, aircraftAltitudeAslMeters,
                detailedProfile, ref profile);
            long drawStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            bool rendered = false;
            if (entry.WaterMesh != null && terrainMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
                rendered = true;
            }
            if (entry.LandMesh != null && terrainMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
                rendered = true;
            }
            if (drawContours && entry.ContourMesh != null &&
                contourMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
            }
            if (entry.CoastlineMesh != null && coastlineMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);
                if (detailedProfile) profile.DrawCalls++;
            }
            if (detailedProfile)
                profile.DrawSubmitMs += ElapsedMilliseconds(drawStartTicks);
            return rendered;
        }

        static void EnsureLandColours(Entry entry, AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset, float aircraftAltitudeAslMeters,
            bool detailedProfile, ref BackRenderDetailedProfile profile)
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
            long colourCpuStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            for (int i = 0; i < entry.LandColours.Length; i++)
            {
                Color32 baseColour = ResolveLandColour(mode, preset,
                    entry.LandElevationMeters[i], quantizedAltitude,
                    entry.TopoMinimumMeters, entry.TopoMaximumMeters);
                entry.LandColours[i] = ApplyShade(baseColour, entry.LandShade[i], mode);
            }
            if (detailedProfile)
                profile.ColourCpuMs += ElapsedMilliseconds(colourCpuStartTicks);
            long colourUploadStartTicks = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            entry.LandMesh.colors32 = entry.LandColours;
            if (detailedProfile)
                profile.ColourUploadMs += ElapsedMilliseconds(colourUploadStartTicks);
            entry.ColourMode = mode;
            entry.ColourPreset = preset;
            entry.RelativeAltitudeBucket = altitudeBucket;
        }

        void ResolveRenderableEntries(AERISTerrainHeightTile tile,
            string styleKey, out Entry fallback, out Entry current)
        {
            fallback = null;
            current = null;
            if (tile == null) return;
            string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
            Entry exact;
            if (entries.TryGetValue(cacheKey, out exact) && exact != null &&
                (exact.LandMesh != null || exact.WaterMesh != null)) current = exact;

            foreach (Entry candidate in entries.Values)
            {
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
                coastlineMaterial == null || worldSurfaceMaterial == null || reprojectionMaterial == null)
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

                worldSurfaceMaterial = new Material(shader);
                worldSurfaceMaterial.name = "AERIS_ND_WORLD_SURFACE_MATERIAL";
                worldSurfaceMaterial.hideFlags = HideFlags.HideAndDontSave;
                worldSurfaceMaterial.mainTexture = Texture2D.whiteTexture;
                worldSurfaceMaterial.color = Color.white;

                Shader textureShader = Shader.Find("Unlit/Texture");
                if (textureShader == null) textureShader = Shader.Find("Sprites/Default");
                if (textureShader == null) textureShader = Shader.Find("UI/Default");
                if (textureShader == null) throw new InvalidOperationException(
                    "No compatible built-in texture reprojection shader");
                reprojectionMaterial = new Material(textureShader);
                reprojectionMaterial.name = "AERIS_ND_TEMPORAL_REPROJECTION_MATERIAL";
                reprojectionMaterial.hideFlags = HideFlags.HideAndDontSave;
                reprojectionMaterial.color = Color.white;
            }
            terrainMaterial.color = Color.white;
            contourMaterial.color = ResolveContourColour(preset);
            coastlineMaterial.color = Color.white;
            worldSurfaceMaterial.color = Color.white;
        }



        void EnsureRenderTarget(Rect plot,
            AERISTerrainVirtualDetailProfile virtualDetail)
        {
            float scale = virtualDetail == null ? 1f :
                virtualDetail.RenderTargetScale;
            int width = Mathf.Clamp(Mathf.CeilToInt(plot.width * scale), 128, 1024);
            int height = Mathf.Clamp(Mathf.CeilToInt(plot.height * scale), 128, 1024);
            if (backTarget != null && frontTarget != null && presentationTarget != null &&
                backTarget.width == width && backTarget.height == height &&
                frontTarget.width == width && frontTarget.height == height &&
                presentationTarget.width == width && presentationTarget.height == height &&
                backTarget.IsCreated() && frontTarget.IsCreated() &&
                presentationTarget.IsCreated()) return;

            DestroyRenderTargets();
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
            presentationTarget = new RenderTexture(width, height, 0,
                RenderTextureFormat.ARGB32);
            presentationTarget.name = "AERIS_ND_TERRAIN_PRESENTATION";
            presentationTarget.hideFlags = HideFlags.HideAndDontSave;
            presentationTarget.filterMode = FilterMode.Bilinear;
            presentationTarget.wrapMode = TextureWrapMode.Clamp;
            presentationTarget.useMipMap = false;
            presentationTarget.autoGenerateMips = false;
            if (!presentationTarget.Create())
                throw new InvalidOperationException(
                    "Terrain presentation RenderTexture.Create failed");
            backTargetBytes = (long)width * height * 4L;
            frontTargetBytes = (long)width * height * 4L;
            presentationTargetBytes = (long)width * height * 4L;
            ResetFrontBufferState();
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
                ResolveRenderableEntries(tile, styleKey, out fallbackEntry,
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
                "LOW" : performance.ActiveProfile.Name;
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

        static void ResolveTopographicWindow(AERISTerrainHeightTile[] tiles,
            out int minimumMeters, out int maximumMeters)
        {
            float minimum = float.MaxValue;
            float maximum = float.MinValue;
            if (tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null) continue;
                    if (Finite(tile.MinimumElevationMeters))
                        minimum = Math.Min(minimum, tile.MinimumElevationMeters);
                    if (Finite(tile.MaximumElevationMeters))
                        maximum = Math.Max(maximum, tile.MaximumElevationMeters);
                }
            }
            if (minimum == float.MaxValue || maximum == float.MinValue)
            {
                minimumMeters = -500;
                maximumMeters = 12000;
                return;
            }
            // Palette V3: water is rendered as its own categorical surface, therefore
            // ocean-depth minima must not consume most of the land gradient. This keeps
            // Kerbin-like coastal terrain spread across the useful colour range.
            if (minimum < 0f && maximum > 0f) minimum = 0f;
            float quantizedMinimum = Mathf.Floor(minimum / TopographicWindowBucketMeters) *
                TopographicWindowBucketMeters;
            float quantizedMaximum = Mathf.Ceil(maximum / TopographicWindowBucketMeters) *
                TopographicWindowBucketMeters;
            if (quantizedMaximum - quantizedMinimum < TopographicMinimumSpanMeters)
                quantizedMaximum = quantizedMinimum + TopographicMinimumSpanMeters;
            minimumMeters = Mathf.RoundToInt(quantizedMinimum);
            maximumMeters = Mathf.RoundToInt(quantizedMaximum);
        }

        bool TryBuildRefreshMetrics(Vessel vessel, AERISNdMapProjection projection,
            float rangeMeters, float mapHeadingDeg, bool trackUp,
            out double maximumErrorPixels, out float minimumUvMargin,
            out float driftPixels, out float headingDeltaDeg)
        {
            if (TemporalShadowSamplingEnabled)
                return TryBuildTemporalReprojection(vessel, projection, rangeMeters,
                    out maximumErrorPixels, out minimumUvMargin, out driftPixels,
                    out headingDeltaDeg);
            maximumErrorPixels = 0.0;
            minimumUvMargin = 1f;
            driftPixels = 0f;
            headingDeltaDeg = 0f;
            if (!frontBufferValid || frontTarget == null || !frontTarget.IsCreated())
                return false;
            float oldCenterU, oldCenterV;
            projection.ProjectLatitudeLongitudeToGui(frontCenterLatitudeDeg,
                frontCenterLongitudeDeg, out oldCenterU, out oldCenterV);
            float dx = (oldCenterU - 0.5f) * Math.Max(1, frontTarget.width);
            float dy = (oldCenterV - projection.AnchorGuiV) *
                Math.Max(1, frontTarget.height);
            driftPixels = Mathf.Sqrt(dx * dx + dy * dy);
            if (frontTrackUp != trackUp) headingDeltaDeg = 180f;
            else if (trackUp) headingDeltaDeg = Mathf.Abs(Mathf.DeltaAngle(
                frontMapHeadingDeg, mapHeadingDeg));
            return true;
        }

        static int CompareTilesCoarseFirst(AERISTerrainHeightTile left,
            AERISTerrainHeightTile right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int lod = ((int)left.Key.Lod).CompareTo((int)right.Key.Lod);
            if (lod != 0) return lod;
            // Within one geographic LOD, draw lower-resolution foundation first and
            // transient REAL65/exact refinement last so detail overlays never disappear
            // beneath the 33x33 base merely because timestamps happen to compare oddly.
            int resolution = left.Resolution.CompareTo(right.Resolution);
            if (resolution != 0) return resolution;
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
            usedEntryBytes = Math.Max(0L,
                usedEntryBytes - Math.Max(0L, entry.Bytes));
            DestroyUnityObject(entry.LandMesh);
            DestroyUnityObject(entry.WaterMesh);
            DestroyUnityObject(entry.ContourMesh);
            DestroyUnityObject(entry.CoastlineMesh);
            entry.LandMesh = null;
            entry.WaterMesh = null;
            entry.ContourMesh = null;
            entry.CoastlineMesh = null;
            AERISTerrainRenderReadyHeightField field;
            if (renderReadyFields.TryGetValue(entry.CacheKey, out field) &&
                field != null && field.ResidentTokenValid && residentCache != null)
                residentCache.TryDemotePresentationState(field.ResidentToken,
                    AERISResidentTileState.RenderReady);
        }

        void HandlePaletteGeneration(AERISTerrainColourPreset preset)
        {
            if (activePalettePreset == (AERISTerrainColourPreset)(-1))
            {
                activePalettePreset = preset;
                return;
            }
            if (activePalettePreset == preset) return;
            AERISTerrainColourPreset previous = activePalettePreset;
            activePalettePreset = preset;
            paletteGeneration++;
            gpuContentRevision++;
            CancelProjectionBatch();
            ResetFrontBufferState();
            nextBackRefreshRealtime = 0f;
            AERISLogger.Info("[CP3.5/ACCESSIBILITY] palette generation " +
                paletteGeneration + "; " + previous + " -> " + preset +
                "; exact key-frame/presentation cache invalidated.");
        }

        void DrawWorldSurfaceNavigation(ProjectionBatch batch, bool detailedProfile,
            ref BackRenderDetailedProfile profile)
        {
            if (batch == null || batch.NavigationFrame == null ||
                worldSurfaceMaterial == null || backTarget == null) return;
            AERISPreparedNavigationFrame frame = batch.NavigationFrame;
            if (!string.Equals(frame.BodyName ?? string.Empty, batch.BodyName ?? string.Empty,
                StringComparison.Ordinal)) return;
            long start = detailedProfile ? Stopwatch.GetTimestamp() : 0L;
            if (!worldSurfaceMaterial.SetPass(0)) return;
            long primitives = 0L;
            GL.Begin(GL.QUADS);
            try
            {
                AERISPreparedRunwaySymbol[] runways = frame.Runways ??
                    new AERISPreparedRunwaySymbol[0];
                for (int i = 0; i < runways.Length; i++)
                {
                    AERISPreparedRunwaySymbol runway = runways[i];
                    if (runway == null) continue;
                    float au, avGui, bu, bvGui;
                    batch.Projection.ProjectLatitudeLongitudeToGui(runway.LatitudeADeg,
                        runway.LongitudeADeg, out au, out avGui);
                    batch.Projection.ProjectLatitudeLongitudeToGui(runway.LatitudeBDeg,
                        runway.LongitudeBDeg, out bu, out bvGui);
                    float av = GuiToRenderV(batch.Orientation, avGui);
                    float bv = GuiToRenderV(batch.Orientation, bvGui);
                    if (!SegmentNearSurface(au, av, bu, bv)) continue;
                    Color colour = runway.SelectedRunway ? new Color(0.20f, 1f, 0.34f, 0.96f) :
                        (runway.Certified ? new Color(0.72f, 0.88f, 0.95f, 0.92f) :
                        (runway.Provisional ? new Color(1f, 0.68f, 0.12f, 0.92f) :
                        new Color(0.62f, 0.68f, 0.72f, 0.82f)));
                    float physicalPixels = (float)(Math.Max(8.0, runway.WidthMeters) /
                        Math.Max(1.0, batch.Projection.VerticalMeters) * backTarget.height);
                    float widthPixels = Mathf.Clamp(physicalPixels, 1.2f, 7f);
                    if (runway.SelectedRunway) widthPixels = Mathf.Max(widthPixels, 2.4f);
                    EmitWorldLineQuad(au, av, bu, bv, widthPixels + 2f,
                        new Color(0.01f, 0.02f, 0.03f, 0.90f));
                    EmitWorldLineQuad(au, av, bu, bv, widthPixels, colour);
                    primitives += 2L;
                }
                if (batch.IncludeFacilities)
                {
                    AERISPreparedFacilitySymbol[] facilities = frame.Facilities ??
                        new AERISPreparedFacilitySymbol[0];
                    int facilityLimit = performance == null || performance.ActiveProfile == null ?
                        24 : performance.ActiveProfile.MaximumFacilitySymbols;
                    int drawn = 0;
                    for (int i = 0; i < facilities.Length && drawn < facilityLimit; i++)
                    {
                        AERISPreparedFacilitySymbol facility = facilities[i];
                        if (facility == null || !facility.HasGeographicPosition) continue;
                        float u, vGui;
                        batch.Projection.ProjectLatitudeLongitudeToGui(facility.LatitudeDeg,
                            facility.LongitudeDeg, out u, out vGui);
                        float v = GuiToRenderV(batch.Orientation, vGui);
                        if (u < -0.04f || u > 1.04f || v < -0.04f || v > 1.04f) continue;
                        Color c = facility.Selected ? new Color(0.20f, 1f, 0.34f, 0.96f) :
                            new Color(0.62f, 0.86f, 0.94f, 0.88f);
                        float dx = 4f / Math.Max(128f, backTarget.width);
                        float dy = 4f / Math.Max(128f, backTarget.height);
                        EmitWorldLineQuad(u - dx, v, u + dx, v, 1.4f, c);
                        EmitWorldLineQuad(u, v - dy, u, v + dy, 1.4f, c);
                        primitives += 2L;
                        drawn++;
                    }
                }
            }
            finally
            {
                GL.End();
            }
            if (detailedProfile)
            {
                profile.WorldSurfaceMs += ElapsedMilliseconds(start);
                profile.WorldSurfacePrimitives += primitives;
                if (primitives > 0) profile.DrawCalls++;
            }
        }

        static float GuiToRenderV(AERISTerrainRenderTargetOrientation orientation,
            float guiV)
        {
            return orientation == AERISTerrainRenderTargetOrientation.Flipped ? guiV :
                1f - guiV;
        }

        static bool SegmentNearSurface(float ax, float ay, float bx, float by)
        {
            float minX = Math.Min(ax, bx), maxX = Math.Max(ax, bx);
            float minY = Math.Min(ay, by), maxY = Math.Max(ay, by);
            return maxX >= -0.08f && minX <= 1.08f && maxY >= -0.08f && minY <= 1.08f;
        }

        void EmitWorldLineQuad(float ax, float ay, float bx, float by,
            float widthPixels, Color colour)
        {
            float dxPixels = (bx - ax) * Math.Max(128f, backTarget.width);
            float dyPixels = (by - ay) * Math.Max(128f, backTarget.height);
            float length = Mathf.Sqrt(dxPixels * dxPixels + dyPixels * dyPixels);
            if (length <= 0.001f) return;
            float half = Math.Max(0.5f, widthPixels * 0.5f);
            float nx = -dyPixels / length * half / Math.Max(128f, backTarget.width);
            float ny = dxPixels / length * half / Math.Max(128f, backTarget.height);
            GL.Color(colour);
            GL.Vertex3(ax + nx, ay + ny, 0f);
            GL.Vertex3(bx + nx, by + ny, 0f);
            GL.Vertex3(bx - nx, by - ny, 0f);
            GL.Vertex3(ax - nx, ay - ny, 0f);
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
            float aircraftAltitudeAslMeters, int topoMinimumMeters,
            int topoMaximumMeters)
        {
            if (mode == AERISTerrainDisplayMode.Relative)
                return ResolveRelativeLandColour(preset,
                    aircraftAltitudeAslMeters - terrainAltitudeMeters);
            float minimum = topoMinimumMeters == int.MinValue ? -500f : topoMinimumMeters;
            float maximum = topoMaximumMeters == int.MinValue ? 12000f : topoMaximumMeters;
            if (maximum - minimum < TopographicMinimumSpanMeters)
                maximum = minimum + TopographicMinimumSpanMeters;
            float t = Mathf.Clamp01((terrainAltitudeMeters - minimum) /
                Mathf.Max(1f, maximum - minimum));
            return ResolveTopographicLandColour(preset, t);
        }

        static Color32 ResolveRelativeLandColour(AERISTerrainColourPreset preset,
            float clearanceMeters)
        {
            // Palette V3 deliberately separates hue AND luminance. Profiles must remain
            // distinguishable in a quick cockpit glance rather than being near-neighbour
            // recolours of the Standard palette.
            if (clearanceMeters <= 30f)
            {
                if (preset == AERISTerrainColourPreset.RedGreenAssist)
                    return new Color32(255, 48, 196, 255);   // magenta danger
                if (preset == AERISTerrainColourPreset.BlueYellowAssist)
                    return new Color32(255, 54, 54, 255);    // red danger
                if (preset == AERISTerrainColourPreset.HighContrast)
                    return new Color32(255, 32, 16, 255);
                return new Color32(232, 24, 18, 255);
            }
            if (clearanceMeters <= 300f)
            {
                if (preset == AERISTerrainColourPreset.RedGreenAssist)
                    return new Color32(255, 224, 32, 255);
                if (preset == AERISTerrainColourPreset.BlueYellowAssist)
                    return new Color32(218, 80, 238, 255);
                if (preset == AERISTerrainColourPreset.HighContrast)
                    return new Color32(255, 232, 24, 255);
                return new Color32(244, 174, 12, 255);
            }
            if (clearanceMeters <= 600f)
            {
                if (preset == AERISTerrainColourPreset.RedGreenAssist)
                    return new Color32(32, 196, 255, 255);
                if (preset == AERISTerrainColourPreset.BlueYellowAssist)
                    return new Color32(36, 208, 132, 255);
                if (preset == AERISTerrainColourPreset.HighContrast)
                    return new Color32(28, 220, 255, 255);
                return new Color32(68, 162, 56, 255);
            }
            if (preset == AERISTerrainColourPreset.RedGreenAssist)
                return new Color32(54, 66, 82, 255);
            if (preset == AERISTerrainColourPreset.BlueYellowAssist)
                return new Color32(46, 76, 62, 255);
            if (preset == AERISTerrainColourPreset.HighContrast)
                return new Color32(28, 34, 42, 255);
            return new Color32(28, 70, 34, 255);
        }

        static Color32 ResolveTopographicLandColour(
            AERISTerrainColourPreset preset, float t)
        {
            switch (preset)
            {
                case AERISTerrainColourPreset.RedGreenAssist:
                    return Gradient(t,
                        new Color32(34, 46, 62, 255),
                        new Color32(28, 158, 226, 255),
                        new Color32(255, 226, 52, 255),
                        new Color32(236, 122, 64, 255),
                        new Color32(250, 250, 246, 255));
                case AERISTerrainColourPreset.BlueYellowAssist:
                    return Gradient(t,
                        new Color32(28, 66, 50, 255),
                        new Color32(48, 190, 104, 255),
                        new Color32(202, 72, 224, 255),
                        new Color32(246, 82, 64, 255),
                        new Color32(250, 246, 232, 255));
                case AERISTerrainColourPreset.HighContrast:
                    return Gradient(t,
                        new Color32(18, 20, 24, 255),
                        new Color32(86, 98, 110, 255),
                        new Color32(255, 226, 28, 255),
                        new Color32(255, 92, 28, 255),
                        new Color32(255, 255, 255, 255));
                default:
                    return Gradient(t,
                        new Color32(20, 66, 30, 255),
                        new Color32(52, 144, 54, 255),
                        new Color32(178, 158, 52, 255),
                        new Color32(142, 82, 54, 255),
                        new Color32(244, 244, 238, 255));
            }
        }

        static Color32 ResolveWaterColour()
        {
            // Kept invariant for safety/readability; Palette V3 spends its contrast budget
            // on land and hazard bands while water remains a stable deep-blue reference.
            return new Color32(6, 42, 112, 255);
        }

        static Color32 ApplyShade(Color32 colour, byte shade,
            AERISTerrainDisplayMode mode)
        {
            float raw = Mathf.Clamp(shade / 227f, 0.82f, 1.04f);
            // REL bands are safety symbology: keep their red/yellow/green identity
            // dominant and use only subtle relief shading. TOPO may retain a little
            // more relief, but no longer produces dark triangular blotches.
            float blend = mode == AERISTerrainDisplayMode.Relative ? 0.24f : 0.48f;
            float factor = Mathf.Lerp(1f, raw, blend);
            factor = mode == AERISTerrainDisplayMode.Relative ?
                Mathf.Clamp(factor, 0.95f, 1.02f) :
                Mathf.Clamp(factor, 0.90f, 1.03f);
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

        void DestroyRenderTargets()
        {
            DestroyRenderTexture(ref backTarget);
            DestroyRenderTexture(ref frontTarget);
            DestroyRenderTexture(ref presentationTarget);
            backTargetBytes = 0L;
            frontTargetBytes = 0L;
            presentationTargetBytes = 0L;
            ResetFrontBufferState();
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
            Entry[] snapshot = new Entry[entries.Count];
            entries.Values.CopyTo(snapshot, 0);
            for (int i = 0; i < snapshot.Length; i++) Remove(snapshot[i]);
            entries.Clear();
            completed.Clear();
            requested.Clear();
            DestroyRenderTargets();
            DestroyUnityObject(terrainMaterial);
            DestroyUnityObject(contourMaterial);
            DestroyUnityObject(coastlineMaterial);
            DestroyUnityObject(worldSurfaceMaterial);
            DestroyUnityObject(reprojectionMaterial);
            terrainMaterial = null;
            contourMaterial = null;
            coastlineMaterial = null;
            worldSurfaceMaterial = null;
            reprojectionMaterial = null;
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
