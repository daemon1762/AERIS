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
            // AERIS23 Entry-Preserving Terrain Mesh Packing. One packed triangle mesh keeps
            // the exact Candidate8 primitive order (water -> land -> coastal water -> coastal
            // land) inside this Entry while reducing four DrawMeshNow submissions to one.
            // The accepted source meshes remain resident as a candidate fallback until runtime
            // acceptance proves the packed path visually identical.
            internal Mesh PackedTerrainMesh;
            internal GeographicUnitPoint[] PackedTerrainGeographicPoints;
            internal Vector3[] PackedTerrainProjectedVertices;
            internal Color32[] PackedTerrainColours;
            internal int PackedWaterOffset;
            internal int PackedWaterCount;
            internal int PackedLandOffset;
            internal int PackedLandCount;
            internal int PackedCoastalWaterOffset;
            internal int PackedCoastalWaterCount;
            internal int PackedCoastalLandOffset;
            internal int PackedCoastalLandCount;
            internal int PackedTerrainSourceMeshCount;
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
            // AERIS23 cheap spherical-cap broad phase. These are immutable and are
            // calculated once when the Entry is built, so the 10 Hz hot path needs only
            // a dot product and one cosine-addition identity per Entry.
            internal double BoundCenterX;
            internal double BoundCenterY;
            internal double BoundCenterZ;
            internal double BoundRadiusSin;
            internal double BoundRadiusCos = -1.0;
            internal double LastProjectionCenterLatitudeDeg = double.NaN;
            internal double LastProjectionCenterLongitudeDeg = double.NaN;
            // Exact-projection origin retained as a unit vector. Motion-only 10 Hz ticks
            // can translate the immutable projected mesh instead of rewriting every vertex.
            internal double LastProjectionCenterX = double.NaN;
            internal double LastProjectionCenterY = double.NaN;
            internal double LastProjectionCenterZ = double.NaN;
            internal float LastExactProjectionRealtime = -1f;
            internal double LastProjectionBodyRadius = double.NaN;
            internal float LastProjectionRangeMeters = float.NaN;
            internal float LastProjectionAnchorBottom = float.NaN;
            internal AERISTerrainRenderTargetOrientation LastProjectionOrientation =
                (AERISTerrainRenderTargetOrientation)(-1);
            // AERIS23 Witness-Bounded Affine Projection. The cached mesh always remains
            // the last exact spherical projection. Up to eight extreme witnesses sample
            // terrain + contour + coastline geometry and validate any affine reuse before
            // the matrix is allowed to reach DrawMeshNow.
            internal GeographicUnitPoint[] ProjectionWitnessPoints;
            internal Vector2[] ProjectionWitnessExactVertices;
            internal int ProjectionWitnessCount;
            internal int ProjectionWitnessBasisA = -1;
            internal int ProjectionWitnessBasisB = -1;
            internal int ProjectionWitnessBasisC = -1;
            // Stable per-Entry refresh slot. -1 means the FNV-1a slot has not yet been
            // resolved. This is presentation-only state and never affects content authority.
            internal int ExactRefreshStaggerSlot = -1;
            // AERIS24 GPU Vertex Projection. Geographic XYZ is uploaded once into
            // TEXCOORD1. The original CPU projected arrays remain the complete fallback.
            internal bool GpuVertexProjectionAttributesReady;
            internal bool GpuVertexProjectionRejected;
            internal bool GpuDynamicColourAttributesReady;
            internal bool GpuDynamicColourRejected;
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

        // AERIS25_PERSISTENT_PRESENTATION_BATCHING: immutable submission packet for the
        // current content snapshot. Packets compact away empty tile slots and keep the
        // exact per-Entry painter contract (terrain -> contour -> coastline). They are
        // rebuilt only when content authority changes and reused on motion-only 10 Hz ticks.
        struct PresentationPacket
        {
            internal AERISTerrainHeightTile Tile;
            internal Entry Entry;
            internal bool ExactDetailOverlay;
        }

        // AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001: keep preparation on the Unity main thread, but make the
        // managed source/packing work resumable. No partial Entry is published.
        const string Rev35Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001";
        const string Rev35R002Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT";
        const string Rev35R003Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION";
        const string Rev35R004Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT";
        const double Rev35R004BudgetOneMilliseconds = 1.00;
        const double Rev35R004BudgetOneHalfMilliseconds = 1.50;
        const double Rev35R004BudgetMaximumMilliseconds = 2.00;
        const double Rev35R004FrameGuardMediumMilliseconds = 15.0;
        const double Rev35R004FrameGuardSoftMilliseconds = 20.0;
        const double Rev35R004FrameGuardHardMilliseconds = 25.0;
        const int Rev35R004PrepareChunkMedium = 128;
        const int Rev35R004PrepareChunkHigh = 256;
        // R005 keeps R004 adaptive throughput only for the lightweight packed lane.
        // Geographic/source preparation is materially heavier per item and is hard
        // capped at the R001-safe 64-item cadence to prevent 80 ms class bursts.
        const string Rev35R005Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES";
        const int Rev35R005SourceChunkHardCap = 64;
        // R006 attacks recurring managed/LOH churn without caching completed Entries.
        // Only retired exact-length geographic arrays are retained, with a hard cap.
        const string Rev35R006Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER";
        const string Rev35R006ResourceReleaseHotfix1 = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_HOTFIX1";
        const string Rev35R006ResourceReleaseOrderHotfix2 = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2";
        const long Rev35R006GeographicPoolMaximumBytes = 8L * 1024L * 1024L;
        const int Rev35R006GeographicPoolMaximumArrays = 16;
        // AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4: reuse only managed buffers whose ownership has ended.
        // PackedSource remains Entry projected-geometry authority and is never pooled.
        const string Rev35R006PackedManagedBufferHotfix4 = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4";
        const long Rev35R006Hf4ColourPoolMaximumBytes = 16L * 1024L * 1024L;
        const int Rev35R006Hf4ColourPoolMaximumArrays = 128;
        const long Rev35R006Hf4IndexPoolMaximumBytes = 8L * 1024L * 1024L;
        const int Rev35R006Hf4IndexPoolMaximumArrays = 128;
        // AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION: remove the 5 Hz re-admission gap between already-
        // RenderReady FAR foundation fields without increasing commit budget or lanes.
        const string Rev35R007Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION";
        const int Rev35R007FoundationQueueMaximum = 128;
        // AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY: current requested FAR receives upstream admission before
        // obsolete-view raster work. No worker count or commit budget is changed.
        const string Rev35R008Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY";
        const string Rev35R009Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE";
        // AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM: keep the single staged commit lane alive while the
        // existing R007 current-FAR handoff FIFO still has work.
        const string Rev35R010Variant = "AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM";
        // AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE: worker completion advances the existing single staged
        // commit lane immediately; published Entries are coalesced into the
        // inherited 0.20 s content-maintenance cadence before full reconcile.
        const string Rev35R014Variant = "AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE";
        const int Rev35R003MaximumStaleSkipsPerWindow = 8;
        const int Rev35PrepareChunkItems = 64;

        enum PendingEntryCommitStage
        {
            ClipTriangles,
            PrepareSources,
            PreparePackedTerrain,
            AcquirePackedTerrainMesh,
            UploadPackedTerrainVertices,
            UploadPackedTerrainColours,
            UploadPackedTerrainIndices,
            FinalizePackedTerrainMesh,
            PrepareContour,
            AcquireContourMesh,
            UploadContourVertices,
            UploadContourColours,
            UploadContourIndices,
            FinalizeContourMesh,
            PrepareCoastline,
            AcquireCoastlineMesh,
            UploadCoastlineVertices,
            UploadCoastlineColours,
            UploadCoastlineIndices,
            FinalizeCoastlineMesh,
            GeographicPacked,
            GeographicContour,
            GeographicCoastline,
            Finalize
        }

        sealed class PendingEntryCommit
        {
            internal string CacheKey;
            internal AERISTerrainRenderReadyHeightField Result;
            internal PendingEntryCommitStage Stage;
            internal SurfaceBuilder Land;
            internal SurfaceBuilder Water;
            internal SurfacePoint[] ClipScratch;
            internal int TriangleCursor;
            internal Vector3[] LandSource;
            internal Vector3[] WaterSource;
            internal Vector3[] CoastalLandSource;
            internal Vector3[] CoastalWaterSource;
            internal Vector3[] PackedSource;
            internal Color32[] PackedColours;
            internal int[] PackedIndices;
            internal int PackedWaterOffset, PackedWaterCount;
            internal int PackedLandOffset, PackedLandCount;
            internal int PackedCoastalWaterOffset, PackedCoastalWaterCount;
            internal int PackedCoastalLandOffset, PackedCoastalLandCount;
            internal int PackedSourceMeshCount;
            internal Vector3[] ContourSource;
            internal Color32[] ContourColours;
            internal int[] ContourIndices;
            internal int ContourPrepareCursor;
            internal bool ContourIndicesFromCache;
            internal Vector3[] CoastlineSource;
            internal Color32[] CoastlineColours;
            internal int[] CoastlineIndices;
            internal int CoastlinePrepareCursor;
            internal bool CoastlineIndicesFromCache;
            internal Mesh PackedMesh;
            internal Mesh ContourMesh;
            internal Mesh CoastlineMesh;
            internal GeographicUnitPoint[] PackedGeographic;
            internal GeographicUnitPoint[] ContourGeographic;
            internal GeographicUnitPoint[] CoastlineGeographic;
            internal int GeographicCursor;
            internal int PrepareSubstage;
            internal int PrepareCursor;
            internal int PackedIndexWriteCursor;
            internal float[] LandElevation;
            internal byte[] LandShade;
            internal long Bytes;
            internal long StartedTicks;
            internal long Rev35R006FinalizeReadyTicks;
            internal double AccumulatedMilliseconds;
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
        // AERIS23 Projection Motion Bridge. Full CPU projection/upload remains the exact
        // authority. Between exact commits only a tiny N-UP translation may be applied.
        // The bridge budget is 80% of the existing quarter-pixel movement threshold
        // (=0.20 pixel), tightens with latitude, and is disabled in polar convergence.
        const float ProjectionBridgeThresholdScale = 0.80f;
        const float ProjectionBridgeMinimumLatitudeScale = 0.35f;
        const float ProjectionBridgeLatitudeLimitDeg = 70f;
        // Successor bridge: affine reuse is accepted only after exact witness validation.
        // 0.08 px is intentionally tighter than the already-accepted 0.20 px translation
        // bridge budget. Four seconds is only a freshness rail; witness error remains the
        // primary authority and may force exact projection much sooner.
        const int AffineWitnessMaximumCount = 8;
        const float AffineWitnessAcceptancePixels = 0.08f;
        const float AffineWitnessMaximumAgeSeconds = 4.00f;
        // Stagger the former synchronized 4.0 s freshness burst across the fixed 10 Hz
        // presentation clock. Stable FNV-1a(CacheKey) selects one of twelve deadlines:
        // 2.80, 2.90, ... 3.90 s. Every deadline remains strictly inside the accepted
        // 4.00 s hard freshness rail; visual/witness acceptance is otherwise unchanged.
        const int StaggeredExactRefreshSlotCount = 12;
        const float StaggeredExactRefreshMinimumSeconds = 2.80f;
        const float StaggeredExactRefreshSlotSeconds = 0.10f;
        const float AffineWitnessSourceAreaEpsilon = 0.000000001f;
        const float AffineWitnessDeterminantMinimum = 0.80f;
        const float AffineWitnessDeterminantMaximum = 1.25f;
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
        // AERIS25_CONTENT_GENERATION_BURST_GOVERNOR: keep visible projection at
        // fixed 10 Hz while bounding only hidden content commit/retirement bursts.
        const int SteadyContentCommitMaximumResults = 2;
        const int BootstrapContentCommitMaximumResults = 4;
        const int NormalPruneMaximumRemovals = 4;
        const float ContentPlanningHeadingStepDeg = 6f;
        // AERIS25_STAGED_MAIN_THREAD_COMMIT
        // AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION: keep the rev002 resumable build,
        // but restrict Entry authority swaps and old-Mesh retirement to the fixed 10 Hz
        // authoritative content path.
        // AERIS25_MAIN_THREAD_COMMIT_GOVERNOR: preserve rev009 count ceilings as
        // hard rails, but stop consuming completed raster results by measured
        // main-thread wall-clock budget after guaranteed minimum forward progress.
        const double MainThreadCommitSteadyBudgetMilliseconds = 0.50;
        const double MainThreadCommitBootstrapBudgetMilliseconds = 1.25;

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
        readonly Queue<string> rev35R007FoundationQueue =
            new Queue<string>(Rev35R007FoundationQueueMaximum);
        readonly HashSet<string> rev35R007FoundationQueued =
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
        readonly AERISNdGpuVertexProjectionBackend gpuVertexProjection =
            new AERISNdGpuVertexProjectionBackend();
        readonly List<Vector3> gpuVertexGeographicScratch = new List<Vector3>(4096);
        readonly List<Vector3> gpuDynamicTerrainSemanticScratch = new List<Vector3>(4096);
        bool gpuVertexProjectionBackFailure;
        AERISNdProjectionBackendMode projectionBackendMode =
            (AERISNdProjectionBackendMode)(-1);
        long ndReloadGeneration = 1L;
        long frontReloadGeneration;
        long operationHealthProjectionBackendSwitches;
        // Hotfix: while the ND is deliberately black for a discrete view/backend
        // reload, freeze the geographic request at one authoritative snapshot. At
        // high groundspeed a live center can otherwise outrun FAR generation and
        // make progress regress indefinitely. The lock is presentation-only and is
        // released immediately after the fresh FRONT for this reload generation.
        bool reloadSnapshotPending = true;
        bool reloadSnapshotActive;
        double reloadSnapshotCenterLatitudeDeg;
        double reloadSnapshotCenterLongitudeDeg;
        float reloadSnapshotMapHeadingDeg;
        int reloadProgressPercentFloor;
        long operationHealthReloadSnapshotCaptures;
        long operationHealthReloadSnapshotFrames;
        // Fixed-size witness scratch is renderer-owned and allocation-free on the 10 Hz path.
        readonly double[] affineWitnessScoreScratch = new double[AffineWitnessMaximumCount];
        readonly bool[] affineWitnessValidScratch = new bool[AffineWitnessMaximumCount];
        readonly GeographicUnitPoint[] affineWitnessPointScratch =
            new GeographicUnitPoint[AffineWitnessMaximumCount];
        readonly Vector2[] affineWitnessExactScratch =
            new Vector2[AffineWitnessMaximumCount];
        readonly Vector2[] affineWitnessCurrentScratch =
            new Vector2[AffineWitnessMaximumCount];
        readonly List<Entry> releaseEntryScratch = new List<Entry>(128);
        // Recycle native Unity Mesh objects across ordinary tile eviction/supersession.
        // Terrain OFF / viewport suspension still destroys the pool, preserving the
        // existing resource-release contract.
        const int MaximumPooledMeshes = 24;
        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);
        readonly Dictionary<int, Stack<GeographicUnitPoint[]>> rev35R006GeographicPool =
            new Dictionary<int, Stack<GeographicUnitPoint[]>>();
        long rev35R006GeographicPoolBytes;
        int rev35R006GeographicPoolArrays;
        readonly Dictionary<int, Stack<Color32[]>> rev35R006Hf4ColourPool =
            new Dictionary<int, Stack<Color32[]>>();
        readonly Dictionary<int, Stack<int[]>> rev35R006Hf4IndexPool =
            new Dictionary<int, Stack<int[]>>();
        long rev35R006Hf4ColourPoolBytes;
        int rev35R006Hf4ColourPoolArrays;
        long rev35R006Hf4IndexPoolBytes;
        int rev35R006Hf4IndexPoolArrays;
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
        long operationHealthPackedTerrainBuilds;
        long operationHealthPackedTerrainDraws;
        long operationHealthPackedTerrainDrawSubmissionsSaved;
        long operationHealthDrawMeshSubmissions;
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
        long operationHealthProjectionExactRefreshes;
        long operationHealthProjectionBridgeUses;
        long operationHealthAffineBridgeUses;
        long operationHealthAffineBridgeRejects;
        long operationHealthAffineWitnessTests;
        long operationHealthAffineExactFallbacks;
        long operationHealthAffineWitnessMaxMilliPixels;
        long operationHealthStaggeredExactDue;
        long operationHealthStaggeredExactDeferrals;
        long operationHealthStaggeredExactBackPeak;
        long operationHealthStaggeredExactBackSamples;
        long operationHealthStaggeredExactBackOverEight;
        long operationHealthGpuVertexAttributeUploads;
        long operationHealthGpuVertexAttributeFailures;
        long operationHealthGpuVertexExactBypasses;
        long operationHealthGpuVertexBackFrames;
        long operationHealthGpuVertexDraws;
        long operationHealthGpuDynamicSemanticUploads;
        long operationHealthGpuDynamicSemanticFailures;
        long operationHealthGpuDynamicCpuColourBypasses;
        long operationHealthGpuDynamicVerticesSubmitted;
        long operationHealthGpuVertexPackedMismatch;
        long operationHealthGpuVertexContourMismatch;
        long operationHealthGpuVertexCoastlineMismatch;
        // AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS: diagnostic-only attribution.
        // These counters never alter render/fallback authority. Initial rejection
        // accounting must reconcile exactly with oh_gpu_vertex_attr_fail.
        const int GpuVertexRejectDiagnosticSampleLimit = 64;
        long operationHealthGpuVertexRejectInitial;
        long operationHealthGpuVertexRejectRevisits;
        long operationHealthGpuVertexRejectPackedNull;
        long operationHealthGpuVertexRejectPackedLength;
        long operationHealthGpuVertexRejectContourNull;
        long operationHealthGpuVertexRejectContourLength;
        long operationHealthGpuVertexRejectCoastNull;
        long operationHealthGpuVertexRejectCoastLength;
        long operationHealthGpuVertexRejectSemanticPackedMeshNull;
        long operationHealthGpuVertexRejectSemanticRejected;
        long operationHealthGpuVertexRejectSemanticException;
        long operationHealthGpuVertexRejectSemanticOther;
        long operationHealthGpuVertexRejectException;
        long operationHealthGpuVertexRejectOther;
        int operationHealthGpuVertexRejectDiagnosticSamples;
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
        // AERIS24 rev007 Warm Visibility Suspend. Display OFF stops work but retains
        // the last complete presentation resources. Fresh resume uses black reload and
        // amortized stale-entry pruning instead of synchronous teardown/rebuild.
        long operationHealthWarmVisibilitySuspends;
        long operationHealthWarmVisibilityResumes;
        long operationHealthWarmPruneTicks;
        long operationHealthWarmPruneRemoved;
        long operationHealthWarmPruneDeferrals;
        // AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD: a content snapshot reuses the
        // selected Entry references across motion-only 10 Hz presentations. Pruning
        // must not recycle Mesh objects still referenced by that immutable snapshot.
        long operationHealthSnapshotMeshPruneProtected;
        long operationHealthSnapshotMeshPruneDeferrals;
        long operationHealthSnapshotStaleMeshDetections;
        long operationHealthContentCommitBudgetHits;
        int operationHealthContentCommitBacklogPeak;
        long operationHealthPruneBudgetHits;
        long operationHealthPruneDebtPeakBytes;
        long operationHealthContentHeadingCoalesced;
        readonly Stopwatch mainThreadCommitStopwatch = new Stopwatch();
        long operationHealthMainCommitBudgetHits;
        int operationHealthMainCommitBacklog;
        int operationHealthMainCommitBacklogPeak;
        double operationHealthMainCommitWindowMaxMilliseconds;
        long operationHealthMainCommitOverbudget;
        long operationHealthMainCommitProcessed;
        double operationHealthMainCommitBudgetMilliseconds;
        PendingEntryCommit pendingEntryCommit;
        long operationHealthMainCommitStageYields;
        long operationHealthMainCommitPublishes;
        long operationHealthRev35PrepareSourceYields;
        long operationHealthRev35PreparePackedYields;
        double operationHealthRev35PackedSourceAllocMaxMs;
        double operationHealthRev35PackedColourAllocMaxMs;
        double operationHealthRev35PackedIndexAllocMaxMs;
        long operationHealthRev35R003StalePendingCancels;
        long operationHealthRev35R003StaleCompletedSkips;
        long operationHealthRev35R003RelevantAdmissions;
        long operationHealthRev35R004Budget050;
        long operationHealthRev35R004Budget100;
        long operationHealthRev35R004Budget150;
        long operationHealthRev35R004Budget200;
        long operationHealthRev35R004FrameGuard;
        long operationHealthRev35R004AllocationContinues;
        double operationHealthRev35R004BudgetMaxMs;
        int operationHealthRev35R004ChunkMaxItems;
        long operationHealthRev35R005SourceHardCapWindows;
        int operationHealthRev35R005PackedChunkMaxItems;
        long operationHealthRev35R006GeoPoolHit;
        long operationHealthRev35R006GeoPoolMiss;
        long operationHealthRev35R006GeoPoolReject;
        long operationHealthRev35R006GeoPoolRecycle;
        long operationHealthRev35R006ProjectedOwnershipTransfers;
        double operationHealthRev35R006GeoAllocationMaxMs;
        int operationHealthRev35R006GeoMaxItems;
        long operationHealthRev35R006FinalizeWaitSamples;
        double operationHealthRev35R006FinalizeWaitMaxMs;
        int operationHealthRev35R006FoundationMissingFar;
        int operationHealthRev35R006FoundationMissingPartial;
        int operationHealthRev35R006FoundationMissingPending;
        int operationHealthRev35R006FoundationMissingRenderReady;
        int operationHealthRev35R006FoundationMissingUpstream;
        int operationHealthRev35R006ContourOnlyFallback;
        int operationHealthRev35R006FoundationSourceIncomplete;
        float operationHealthRev35R006FoundationWaitSince = -1f;
        int operationHealthRev35R006FoundationWaitThresholdMask;
        double operationHealthRev35R006FoundationWaitCurrentMs;
        double operationHealthRev35R006FoundationWaitMaxMs;
        long operationHealthRev35R006FoundationWait500;
        long operationHealthRev35R006FoundationWait1000;
        long operationHealthRev35R006FoundationWait2000;
        long operationHealthRev35R006FoundationWait3000;
        long operationHealthRev35R006FoundationWait5000;
        long operationHealthRev35R006GpuAttrGrow;
        double operationHealthRev35R006GpuAttrGrowMaxMs;
        int operationHealthRev35R006GpuAttrCapacityMax;
        long operationHealthRev35R006Hf4ColourPoolHit;
        long operationHealthRev35R006Hf4ColourPoolMiss;
        long operationHealthRev35R006Hf4ColourPoolRecycle;
        long operationHealthRev35R006Hf4ColourPoolReject;
        long operationHealthRev35R006Hf4ColourOwnershipTransfer;
        double operationHealthRev35R006Hf4ColourNewAllocMaxMs;
        int operationHealthRev35R006Hf4ColourMaxItems;
        long operationHealthRev35R006Hf4IndexPoolHit;
        long operationHealthRev35R006Hf4IndexPoolMiss;
        long operationHealthRev35R006Hf4IndexPoolRecycle;
        long operationHealthRev35R006Hf4IndexPoolReject;
        double operationHealthRev35R006Hf4IndexNewAllocMaxMs;
        int operationHealthRev35R006Hf4IndexMaxItems;
        long operationHealthRev35R007Queued;
        long operationHealthRev35R007ChainedBegins;
        long operationHealthRev35R007ImmediateBegins;
        long operationHealthRev35R007DuplicateSkips;
        long operationHealthRev35R007StaleSkips;
        long operationHealthRev35R007AlreadyCommittedSkips;
        long operationHealthRev35R007MissingFieldSkips;
        long operationHealthRev35R007Overflow;
        long operationHealthRev35R007QueueResets;
        int operationHealthRev35R007QueuePeak;
        long operationHealthRev35R008GeometryPumpSuppress;
        long operationHealthRev35R008PendingCommitCancelled;
        long operationHealthRev35R008FoundationScheduleFirst;
        long operationHealthRev35R010QueueBacklogBudgetSamples;
        int operationHealthRev35R010QueueBacklogPeak;
        long rev35R014PublicationSerial;
        long rev35R014ReconciledPublicationSerial;
        long operationHealthRev35R014PublicationEvents;
        long operationHealthRev35R014FullReconciles;
        long operationHealthRev35R014WorkerOnlySkips;
        long operationHealthRev35R014PublicationDeferrals;
        long operationHealthRev35R014PublicationReconciles;
        long operationHealthRev35R014RetryReconciles;
        bool rev35R008GeometryReconcilePending;
        long operationHealthMainCommitPublicationDeferrals;
        double operationHealthMainCommitStageMaxMilliseconds;
        double operationHealthMainCommitClipMaxMilliseconds;
        double operationHealthMainCommitPrepareMaxMilliseconds;
        double operationHealthMainCommitTerrainUploadMaxMilliseconds;
        double operationHealthMainCommitContourMaxMilliseconds;
        double operationHealthMainCommitCoastlineMaxMilliseconds;
        double operationHealthMainCommitGeographicMaxMilliseconds;
        double operationHealthMainCommitFinalizeMaxMilliseconds;
        int operationHealthMainCommitPendingPeak;
        readonly List<Entry> deferredEntryRetirements = new List<Entry>();
        long operationHealthDeferredRetireQueued;
        long operationHealthDeferredRetireReleased;
        long operationHealthDeferredRetireProtected;
        int operationHealthDeferredRetirePeak;
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
        // Persistent compact presentation set. The HashSet is also the rev008 Mesh
        // lifetime pin authority, replacing an O(N) scan for every prune candidate.
        PresentationPacket[] presentationPackets = new PresentationPacket[0];
        int presentationPacketCount;
        readonly HashSet<Entry> presentationEntryPins = new HashSet<Entry>();
        long operationHealthPresentationPacketRebuilds;
        long operationHealthPresentationPacketReuses;
        long operationHealthPresentationPacketSlotsSkipped;
        long operationHealthPresentationPinHits;
        long operationHealthPresentationPinMisses;
        long operationHealthPresentationPacketDraws;
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
        long operationHealthDotCapCullTests;
        long operationHealthCullGuardVetoes;
        long operationHealthCullGuardConfirmed;
        long operationHealthFoundationCullBypass;
        long operationHealthNonRenderableEntryRejects;
        long operationHealthFallbackShadowPrevents;
        long operationHealthEmptyTriangleResults;
        float operationHealthContentVisibleRangeMeters;
        float operationHealthContentPlanningRangeMeters;
        long operationHealthTemporalOverscanCaptures;
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
        bool warmVisibilitySuspended;
        bool warmVisibilityPrunePending;
        bool warmVisibilityPruneActive;
        int warmVisibilityPreservedEntries;
        long warmVisibilityPreservedBytes;
        long warmVisibilityMeshDestroyBaseline;
        long warmVisibilityAttributeUploadBaseline;
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
        // R017 observation-only exact blocker predicates. These counters mirror the
        // existing foundationComplete / cadence branches and never alter their result.
        long operationHealthRev35R017BlockedRenderedFalse;
        long operationHealthRev35R017BlockedFoundationFlag;
        long operationHealthRev35R017BlockedCoverage;
        long operationHealthRev35R017BlockedReadyFar;
        long operationHealthRev35R017CadenceSkips;
        // R018 separates hidden temporal-overscan preparation from the exact visible
        // presentation gate. The canonical viewport planner is evaluated only during the
        // existing full content reconcile and its exact-current FAR readiness is cached.
        const string Rev35R018Variant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT";
        bool operationHealthRev35R018VisiblePlanValid;
        int operationHealthRev35R018VisibleRequiredFar;
        int operationHealthRev35R018VisibleReadyFar;
        float operationHealthRev35R018VisibleCoverage;
        int operationHealthRev35R018OverscanRequiredFar;
        int operationHealthRev35R018OverscanReadyFar;
        long operationHealthRev35R018OverscanHolAvoided;
        // AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY: exact-visible FAR commit urgency only.
        // No worker, rasterizer, presentation-gate, quality or range authority changes.
        const string Rev35R019Variant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY";
        // AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION: include the exact-visible priority queue in the inherited
        // R010 wake/backlog accounting without changing the single commit lane.
        const string Rev35R019Hotfix1Variant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION";
        // AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY: read-only witness for TileSystem authority-generation baseline.
        const string Rev35R020Variant = "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R020_VISIBLE_AUTHORITY_BASELINE_STABILITY";
        // AERIS30 R021: reuse R019's canonical exact-visible FAR key set at the
        // upstream admission boundary. No new geometry authority, worker, lane,
        // queue capacity, presentation gate, range or quality policy is introduced.
        const string Rev35R021Variant = "AERIS30_REV3_5_SALBUTAMOL_SULFATE_R021_EXACT_VISIBLE_UPSTREAM_PRIORITY";
        long operationHealthRev35R020AuthoritySamples;
        long operationHealthRev35R020GenerationRetained;
        long operationHealthRev35R020GenerationAdvances;
        readonly HashSet<AERISTerrainTileKey> rev35R019VisibleFarKeys =
            new HashSet<AERISTerrainTileKey>();
        readonly Queue<string> rev35R019VisibleFoundationQueue =
            new Queue<string>(Rev35R007FoundationQueueMaximum);
        long operationHealthRev35R019VisiblePriorityQueued;
        long operationHealthRev35R019VisiblePriorityBegins;
        long operationHealthRev35R019HiddenQueueBypassed;
        long operationHealthRev35R019Budget100;
        long operationHealthRev35R019Budget150;
        int operationHealthRev35R019VisibleDeficit;
        int operationHealthRev35R019VisibleKeyCount;
        int operationHealthRev35R019VisiblePriorityQueuePeak;
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
        internal bool Reloading
        {
            get { return !requestedViewReady || frontReloadGeneration != ndReloadGeneration; }
        }
        internal int ReloadProgressPercent
        {
            get
            {
                if (!Reloading) return 100;
                int measured = Mathf.Clamp(Mathf.RoundToInt(
                    Mathf.Clamp01(lastBackFoundationCoverage) * 100f), 0, 99);
                if (measured > reloadProgressPercentFloor)
                    reloadProgressPercentFloor = measured;
                return reloadProgressPercentFloor;
            }
        }
        internal string ProjectionBackendRequested
        {
            get { return gpuVertexProjection.RequestedModeName; }
        }
        internal string ProjectionBackendEffective
        {
            get { return gpuVertexProjection.EffectiveModeName; }
        }
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
            ndReloadGeneration++;
            reloadSnapshotPending = true;
            reloadSnapshotActive = false;
            reloadProgressPercentFloor = 0;
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

        internal void SuspendVisibilityWarm()
        {
            if (disposed || warmVisibilitySuspended) return;
            warmVisibilitySuspended = true;
            warmVisibilityPrunePending = false;
            warmVisibilityPruneActive = false;
            warmVisibilityPreservedEntries = entries.Count;
            warmVisibilityPreservedBytes = usedEntryBytes + backTargetBytes +
                frontTargetBytes + renderReadyBytes;
            warmVisibilityMeshDestroyBaseline = operationHealthMeshPoolDestroys;
            warmVisibilityAttributeUploadBaseline = operationHealthGpuVertexAttributeUploads;
            operationHealthWarmVisibilitySuspends++;

            // Reuse the existing transactional view invalidation: it cancels obsolete
            // worker work and starts a new black-reload generation, but deliberately
            // retains FRONT/BACK, Entry meshes, render-ready fields and GPU attributes.
            InvalidatePendingForViewChange();
            lastVisualCoverageFraction = 0f;
            gpuVertexProjection.RetainForViewportSuspension();
            AERISLogger.Info("[AERIS24_ND_WARM_SUSPEND] ENTER; entries=" +
                warmVisibilityPreservedEntries + "; bytes=" + warmVisibilityPreservedBytes +
                "; meshDestroyBaseline=" + warmVisibilityMeshDestroyBaseline +
                "; attrUploadBaseline=" + warmVisibilityAttributeUploadBaseline +
                "; reloadGeneration=" + ndReloadGeneration + ".");
        }

        internal void ResumeVisibilityWarm()
        {
            if (disposed || !warmVisibilitySuspended) return;
            warmVisibilitySuspended = false;
            warmVisibilityPrunePending = true;
            warmVisibilityPruneActive = false;
            operationHealthWarmVisibilityResumes++;
            AERISLogger.Info("[AERIS24_ND_WARM_SUSPEND] RESUME; preservedEntries=" +
                warmVisibilityPreservedEntries + "; currentEntries=" + entries.Count +
                "; meshDestroyDelta=" + Math.Max(0L, operationHealthMeshPoolDestroys -
                    warmVisibilityMeshDestroyBaseline) +
                "; attrUploadDelta=" + Math.Max(0L, operationHealthGpuVertexAttributeUploads -
                    warmVisibilityAttributeUploadBaseline) +
                "; reloadGeneration=" + ndReloadGeneration + ".");
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
            AERISNdProjectionBackendMode requestedProjectionBackend = settings == null ?
                AERISNdProjectionBackendMode.Automatic :
                settings.NavigationDisplayProjectionBackend;
            if (requestedProjectionBackend != projectionBackendMode)
            {
                projectionBackendMode = requestedProjectionBackend;
                gpuVertexProjection.SetRequestedMode(requestedProjectionBackend);
                operationHealthProjectionBackendSwitches++;
                if (frontBufferValid || requestedViewReady || contentSnapshotValid)
                    InvalidatePendingForViewChange();
                else requestedViewReady = false;
                AERISLogger.Info("[AERIS24_ND_PROJECTION_BACKEND] requested=" +
                    gpuVertexProjection.RequestedModeName + "; effective=" +
                    gpuVertexProjection.EffectiveModeName + "; reloadGeneration=" +
                    ndReloadGeneration + ".");
            }

            // Freeze only deliberate black-reload construction. Normal moving-map
            // authority remains live after the fresh FRONT is committed. Range,
            // orientation, anchor and backend are the requested target state; center
            // and heading are the motion variables that must not chase the aircraft.
            if (Reloading)
            {
                if (reloadSnapshotPending || !reloadSnapshotActive)
                {
                    reloadSnapshotCenterLatitudeDeg = centerLatitudeDeg;
                    reloadSnapshotCenterLongitudeDeg = centerLongitudeDeg;
                    reloadSnapshotMapHeadingDeg = mapHeadingDeg;
                    reloadSnapshotPending = false;
                    reloadSnapshotActive = true;
                    reloadProgressPercentFloor = 0;
                    operationHealthReloadSnapshotCaptures++;
                    AERISLogger.Info("[AERIS24_ND_RELOAD_SNAPSHOT] generation=" +
                        ndReloadGeneration + "; center=" + centerLatitudeDeg + "," +
                        centerLongitudeDeg + "; heading=" + mapHeadingDeg + ".");
                }
                centerLatitudeDeg = reloadSnapshotCenterLatitudeDeg;
                centerLongitudeDeg = reloadSnapshotCenterLongitudeDeg;
                mapHeadingDeg = reloadSnapshotMapHeadingDeg;
                operationHealthReloadSnapshotFrames++;
            }

            float presentationNow = Time.realtimeSinceStartup;
            bool authoritativeTickDue = nextAuthoritativePresentationTickRealtime <= 0f ||
                presentationNow >= nextAuthoritativePresentationTickRealtime;
            if (!authoritativeTickDue)
            {
                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||
                    rev35R019VisibleFoundationQueue.Count > 0 ||
                    rev35R007FoundationQueue.Count > 0)
                {
                    residentCache = system.CurrentBodyResidentCache;
                    PumpStagedCompletedCommit(system, false);
                }
                if (Reloading)
                {
                    operationHealthCoalescedBlankPolls++;
                    lastFrontBufferPresented = false;
                    presentedProjection.Valid = false;
                    lastDrawState = AERISTerrainGpuDrawState.Partial;
                    return lastDrawState;
                }
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

            // AERIS25_TEMPORAL_FOUNDATION_OVERSCAN: user-visible projection remains
            // exactly rangeMeters. Only the hidden content/foundation request footprint is
            // widened so 10 Hz centre/Track-Up motion cannot outrun the last content plan at
            // the ND edge. The existing bounded 1.35x / 250 km authority is reused.
            float historySurfaceRangeMeters = ResolveHistorySurfaceRange(rangeMeters);
            operationHealthContentVisibleRangeMeters = rangeMeters;
            operationHealthContentPlanningRangeMeters = historySurfaceRangeMeters;
            AERISTerrainDisplayMode effectiveMode = ResolveEffectiveMode(requestedMode,
                vessel, rangeMeters);
            AERISTerrainColourPreset currentPreset = settings == null ?
                AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;
            AERISTerrainVirtualDetailProfile virtualDetail =
                ResolveVirtualDetailProfile(rangeMeters);
            lastVirtualDetailName = virtualDetail.Name;
            float contourInterval = ResolveContourInterval(rangeMeters);
            string styleKey = BuildStyleKey(contourInterval, virtualDetail);

            bool workerResultReady = pendingEntryCommit != null || rasterizer.CompletedCount > 0;
            bool contentGeometryChanged = NeedsContentRefresh(system, vessel,
                centerLatitudeDeg, centerLongitudeDeg, rangeMeters, mapHeadingDeg,
                trackUp, anchorV, orientation, styleKey);
            bool contentRetryDue = (rasterizer.PendingCount > 0 ||
                !requestedViewReady) &&
                presentationNow >= nextContentMaintenanceRealtime;
            bool rev35R014PublicationPendingBeforeTick =
                rev35R014PublicationSerial != rev35R014ReconciledPublicationSerial;
            bool contentTickRequired = contentGeometryChanged || workerResultReady ||
                contentRetryDue || rev35R014PublicationPendingBeforeTick;
            bool rev35R014ReconcileRan = false;
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
                if (contentGeometryChanged)
                {
                    ResetRev35R007FoundationQueue();
            rev35R008GeometryReconcilePending = false;
                    rev35R008GeometryReconcilePending = true;
                }
                PumpStagedCompletedCommit(system, true);
                // R014 publication batching: worker readiness advances only the existing
                // R010 staged lane. Full geographic/request/resolve/foundation work is
                // immediate for a true geometry change, otherwise it is capped by the
                // inherited 0.20 s content-maintenance deadline. Multiple Entry publications
                // inside that window collapse into one reconcile without losing the newest
                // publication serial.
                bool rev35R014PublicationPending =
                    rev35R014PublicationSerial != rev35R014ReconciledPublicationSerial;
                bool rev35R014ContentCadenceDue =
                    presentationNow >= nextContentMaintenanceRealtime;
                bool rev35R014ReconcileRequired = contentGeometryChanged ||
                    (rev35R014ContentCadenceDue &&
                     (rev35R014PublicationPending || contentRetryDue));

                if (!rev35R014ReconcileRequired)
                {
                    operationHealthRev35R014WorkerOnlySkips++;
                    if (rev35R014PublicationPending)
                        operationHealthRev35R014PublicationDeferrals++;
                }
                else
                {
                    rev35R014ReconcileRan = true;
                    operationHealthRev35R014FullReconciles++;
                    if (rev35R014PublicationPending)
                        operationHealthRev35R014PublicationReconciles++;
                    if (contentRetryDue)
                        operationHealthRev35R014RetryReconciles++;

                // CaptureVisible owns planner-generation updates and RAM tile selection.
                // Step 2 simply stops invoking this allocation/resolve path for pure motion.
                visible = system.CaptureVisible(centerLatitudeDeg,
                    centerLongitudeDeg, historySurfaceRangeMeters, mapHeadingDeg, trackUp,
                    anchorV, orientation);
                operationHealthContentCaptures++;
                operationHealthTemporalOverscanCaptures++;
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
                ResetRev35R007FoundationQueue();
            rev35R008GeometryReconcilePending = false;
                tiles = PrepareSortedTileScratch(visible.Tiles);
                EnsureEntryScratch(tiles == null ? 0 : tiles.Length);
                RefreshRev35R019VisibleFarKeys(vessel.mainBody, tiles,
                    centerLatitudeDeg, centerLongitudeDeg, rangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation);

                // R008 phase 1: establish the complete current exact request set first.
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile requestedTile = tiles[i];
                    if (requestedTile == null) continue;
                    requested.Add(CacheKey(requestedTile.Key,
                        requestedTile.CreatedUtcTicks, styleKey));
                }
                rasterizer.ReconcileCurrentRequests(requested);
                if (pendingEntryCommit != null &&
                    !requested.Contains(pendingEntryCommit.CacheKey))
                {
                    CancelPendingEntryCommit();
                    operationHealthRev35R008PendingCommitCancelled++;
                }
                rev35R008GeometryReconcilePending = false;

                // R021: exact-visible FAR enters the existing GeneralCompute lane
                // before hidden/overscan FAR. Remaining FAR still precedes non-FAR work.
                // R008 reconciliation, worker count and FIFO scheduler semantics remain.
                for (int admissionPass = 0; admissionPass < 3; admissionPass++)
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null)
                    {
                        if (admissionPass == 0)
                        {
                            fallbackEntriesScratch[i] = null;
                            currentEntriesScratch[i] = null;
                            drawEntriesScratch[i] = null;
                        }
                        continue;
                    }
                    bool r021Far =
                        tile.Key.Lod == AERISTerrainTileLod.Far;
                    bool r021VisibleFar = r021Far &&
                        rev35R019VisibleFarKeys.Contains(tile.Key);
                    bool r021Admit =
                        admissionPass == 0 ? r021VisibleFar :
                        admissionPass == 1 ? (r021Far && !r021VisibleFar) :
                        !r021Far;
                    if (!r021Admit) continue;
                    string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
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
                    bool currentRenderable = HasRenderableTerrain(currentEntry);
                    if (!currentRenderable && currentEntry != null && fallbackEntry != null)
                        operationHealthFallbackShadowPrevents++;
                    if (fallbackEntry != null) fallbackEntry.LastUse = ++useSequence;
                    if (currentRenderable) currentEntry.LastUse = ++useSequence;
                    fallbackEntriesScratch[i] = fallbackEntry;
                    currentEntriesScratch[i] = currentRenderable ? currentEntry : null;
                    drawEntriesScratch[i] = currentRenderable ? currentEntry : fallbackEntry;
                }

                RefreshPresentationPackets(tiles, drawEntriesScratch);
                // Phase6_003: publication may detach the previously published Entry, but
                // Mesh recycling is delayed until the authoritative packet refresh proves
                // that the old Entry is no longer referenced by the persistent snapshot.
                ReleaseDeferredEntryRetirements(false);
                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,
                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);
                MeasureVisibleFoundationGpuReadiness(vessel.mainBody, tiles,
                    currentEntriesScratch, centerLatitudeDeg, centerLongitudeDeg,
                    rangeMeters, mapHeadingDeg, trackUp, anchorV, orientation,
                    out operationHealthRev35R018VisiblePlanValid,
                    out operationHealthRev35R018VisibleRequiredFar,
                    out operationHealthRev35R018VisibleReadyFar);
                operationHealthRev35R018VisibleCoverage =
                    operationHealthRev35R018VisiblePlanValid &&
                    operationHealthRev35R018VisibleRequiredFar > 0 ?
                    Mathf.Clamp01(operationHealthRev35R018VisibleReadyFar /
                        (float)operationHealthRev35R018VisibleRequiredFar) : 0f;
                ObserveRev35R006FoundationCriticalPath(visible, tiles,
                    currentEntriesScratch, fallbackEntriesScratch, styleKey,
                    readyGlobal, readyFar);
                contentVisible = visible;
                bool adoptContentPlanningHeading = !contentSnapshotValid || !trackUp ||
                    contentTrackUp != trackUp || contentOrientation != orientation ||
                    Math.Abs(contentAnchorV - anchorV) > 0.001f ||
                    Math.Abs(contentRangeMeters - rangeMeters) > 0.5f;
                if (!adoptContentPlanningHeading && vessel != null && vessel.mainBody != null)
                {
                    double contentCenterMovement = GreatCircleDistanceMeters(vessel.mainBody,
                        contentCenterLatitudeDeg, contentCenterLongitudeDeg,
                        centerLatitudeDeg, centerLongitudeDeg);
                    adoptContentPlanningHeading = double.IsNaN(contentCenterMovement) ||
                        double.IsInfinity(contentCenterMovement) ||
                        contentCenterMovement >= Math.Max(100.0,
                            Math.Max(1f, rangeMeters) * 0.02);
                }
                if (!adoptContentPlanningHeading && trackUp)
                    adoptContentPlanningHeading = Mathf.Abs(Mathf.DeltaAngle(
                        contentHeadingDeg, mapHeadingDeg)) >= ContentPlanningHeadingStepDeg;

                contentTerrainGeneration = visible.TerrainGeneration;
                contentStyleKey = styleKey;
                contentCenterLatitudeDeg = centerLatitudeDeg;
                contentCenterLongitudeDeg = centerLongitudeDeg;
                contentRangeMeters = rangeMeters;
                if (adoptContentPlanningHeading) contentHeadingDeg = mapHeadingDeg;
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
                                rev35R014ReconciledPublicationSerial =
                        rev35R014PublicationSerial;
                }
}
            else
            {
                operationHealthMotionOnlyTicks++;
                if (contentSnapshotValid) operationHealthPresentationPacketReuses++;
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
            if (rev35R014ReconcileRan)
            {
                long vramLimitBytes = ResolveVramLimitBytes();
                if (warmVisibilityPrunePending)
                {
                    if (!Reloading)
                    {
                        warmVisibilityPrunePending = false;
                        warmVisibilityPruneActive = true;
                    }
                    else operationHealthWarmPruneDeferrals++;
                }

                if (warmVisibilityPruneActive)
                {
                    operationHealthWarmPruneTicks++;
                    warmVisibilityPruneActive = PruneWarmResume(vramLimitBytes, 4);
                }
                else if (!warmVisibilityPrunePending)
                    Prune(vramLimitBytes);

                // Do not compete with fresh-FRONT construction by dropping managed
                // render-ready payloads during the warm black-reload interval.
                if (!warmVisibilityPrunePending)
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

            bool r018VisibleGpuComplete =
                operationHealthRev35R018VisiblePlanValid &&
                operationHealthRev35R018VisibleRequiredFar > 0 &&
                operationHealthRev35R018VisibleReadyFar >=
                    operationHealthRev35R018VisibleRequiredFar;
            bool r018OverscanGpuComplete = visible.FoundationComplete &&
                lastBackFoundationCoverage >= 0.999f &&
                readyFar >= visible.FarFoundationCount;
            operationHealthRev35R018OverscanRequiredFar =
                Math.Max(0, visible.FarFoundationCount);
            operationHealthRev35R018OverscanReadyFar =
                Math.Min(operationHealthRev35R018OverscanRequiredFar,
                    Math.Max(0, readyFar));

            bool rendered = false;
            bool foundationComplete = false;
            bool swapped = false;
            if (refreshAllowed)
            {
                rendered = RenderBackBuffer(presentationPackets, presentationPacketCount, projection,
                    mapRotation, effectiveMode, vessel, rangeMeters, anchorV,
                    forceCenterProjectionRefresh);
                backRenderFrames++;
                lastBackAttemptViewGeneration = visible.ViewGeneration;
                lastBackAttemptContentRevision = gpuContentRevision;
                nextBackRefreshRealtime = nextAuthoritativePresentationTickRealtime;
                foundationComplete = rendered && r018VisibleGpuComplete;
                if (foundationComplete)
                {
                    if (!r018OverscanGpuComplete)
                        operationHealthRev35R018OverscanHolAvoided++;
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
                    // R017 mirrors the exact existing foundationComplete predicates.
                    // Multiple counters may advance for one blocked attempt by design.
                    if (!rendered) operationHealthRev35R017BlockedRenderedFalse++;
                    if (!visible.FoundationComplete)
                        operationHealthRev35R017BlockedFoundationFlag++;
                    if (lastBackFoundationCoverage < 0.999f)
                        operationHealthRev35R017BlockedCoverage++;
                    if (readyFar < visible.FarFoundationCount)
                        operationHealthRev35R017BlockedReadyFar++;
                }
            }
            else if (refreshRequired)
            {
                skippedBackRenderFrames++;
                operationHealthRev35R017CadenceSkips++;
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
            if (!Reloading && directCompatible)
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
            if (!present && !Reloading && colourCompatible &&
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
            bool readyFoundationNow = r018VisibleGpuComplete;
            if (!present && readyFoundationNow && !gpuFailed)
            {
                bool recovered = RenderBackBuffer(presentationPackets, presentationPacketCount, projection,
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
            operationHealthRev35R020AuthoritySamples =
                system.Rev35R020AuthoritySampleCount;
            operationHealthRev35R020GenerationRetained =
                system.Rev35R020GenerationRetainedCount;
            operationHealthRev35R020GenerationAdvances =
                system.Rev35R020GenerationAdvanceCount;
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
            if (trackUp)
            {
                float headingDelta = Mathf.Abs(Mathf.DeltaAngle(contentHeadingDeg,
                    mapHeadingDeg));
                if (headingDelta >= ContentPlanningHeadingStepDeg) return true;
                if (headingDelta >= 3f) operationHealthContentHeadingCoalesced++;
            }
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
            operationHealthRev35R018VisiblePlanValid = false;
            operationHealthRev35R018VisibleRequiredFar = 0;
            operationHealthRev35R018VisibleReadyFar = 0;
            operationHealthRev35R018VisibleCoverage = 0f;
            operationHealthRev35R018OverscanRequiredFar = 0;
            operationHealthRev35R018OverscanReadyFar = 0;
            contentSnapshotValid = false;
            contentGpuReadyPending = false;
            nextContentMaintenanceRealtime = 0f;
            presentationPacketCount = 0;
            presentationEntryPins.Clear();
            ReleaseDeferredEntryRetirements(true);
            CancelPendingEntryCommit();
        }

        void RefreshPresentationPackets(AERISTerrainHeightTile[] tiles, Entry[] drawEntries)
        {
            int sourceCount = Math.Min(tiles == null ? 0 : tiles.Length,
                drawEntries == null ? 0 : drawEntries.Length);
            int compactCount = 0;
            bool unchanged = presentationPacketCount > 0;
            for (int i = 0; i < sourceCount; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                Entry entry = drawEntries[i];
                if (tile == null || entry == null) continue;
                bool exactDetail = tile.Key.Lod >= AERISTerrainTileLod.Route;
                if (unchanged && (compactCount >= presentationPacketCount ||
                    !ReferenceEquals(presentationPackets[compactCount].Tile, tile) ||
                    !ReferenceEquals(presentationPackets[compactCount].Entry, entry) ||
                    presentationPackets[compactCount].ExactDetailOverlay != exactDetail))
                    unchanged = false;
                compactCount++;
            }
            if (unchanged && compactCount == presentationPacketCount)
            {
                operationHealthPresentationPacketReuses++;
                return;
            }

            if (presentationPackets == null || presentationPackets.Length < sourceCount)
                presentationPackets = new PresentationPacket[sourceCount];
            presentationEntryPins.Clear();
            int count = 0;
            for (int i = 0; i < sourceCount; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                Entry entry = drawEntries[i];
                if (tile == null || entry == null)
                {
                    operationHealthPresentationPacketSlotsSkipped++;
                    continue;
                }
                presentationPackets[count++] = new PresentationPacket
                {
                    Tile = tile,
                    Entry = entry,
                    ExactDetailOverlay = tile.Key.Lod >= AERISTerrainTileLod.Route
                };
                presentationEntryPins.Add(entry);
            }
            for (int i = count; i < presentationPacketCount; i++)
                presentationPackets[i] = default(PresentationPacket);
            presentationPacketCount = count;
            operationHealthPresentationPacketRebuilds++;
        }

        bool RenderBackBuffer(PresentationPacket[] packets, int packetCount,
            AERISNdMapProjection projection, Matrix4x4 mapRotation,
            AERISTerrainDisplayMode effectiveMode, Vessel vessel, float rangeMeters,
            float anchorV, bool forceCenterProjectionRefresh)
        {
            if (forceCenterProjectionRefresh)
                operationHealthForcedProjectionRefreshes++;
            long frameStartTicks = Stopwatch.GetTimestamp();
            long exactRefreshesAtBackStart = operationHealthProjectionExactRefreshes;
            bool staggerBurstTelemetryEligible = frontBufferValid && requestedViewReady;
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
                gpuVertexProjectionBackFailure = false;
                bool gpuVertexFrameActive = gpuVertexProjection.TryEnsureLoaded();
                if (gpuVertexFrameActive)
                {
                    AERISTerrainColourPreset gpuPreset = settings == null ?
                        AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;
                    gpuVertexProjection.ConfigureProjection(projection,
                        ResolveContourColour(gpuPreset), effectiveMode, gpuPreset,
                        (float)vessel.altitude);
                    gpuVertexFrameActive = gpuVertexProjection.ValidatePassesOrFallback();
                    if (gpuVertexFrameActive) operationHealthGpuVertexBackFrames++;
                }
                float projectionThresholdMeters = Math.Max(0.25f,
                    rangeMeters / Math.Max(128f, backTarget.height) * 0.25f);
                double projectionCenterLatitudeDeg = UnitLatitude(
                    projection.CenterX, projection.CenterY, projection.CenterZ);
                double projectionCenterLongitudeDeg = UnitLongitude(
                    projection.CenterX, projection.CenterY);
                // AERIS23 dot-cap culling: the previous great-circle test was too
                // expensive at 160 km. Resolve the conservative viewport cap once per BACK
                // frame, then reject whole Entries using only precomputed cap data + a dot
                // product. This re-enables conservative culling at every range without
                // bringing per-Entry atan/sqrt/trigonometric distance work back.
                double viewportCullSin, viewportCullCos;
                bool entryCullingEnabled = ResolveViewportCullCap(vessel.mainBody,
                    rangeMeters, anchorV, out viewportCullSin, out viewportCullCos);
                int compactCount = Math.Min(Math.Max(0, packetCount),
                    packets == null ? 0 : packets.Length);
                for (int i = 0; i < compactCount; i++)
                {
                    PresentationPacket packet = packets[i];
                    AERISTerrainHeightTile tile = packet.Tile;
                    Entry drawEntry = packet.Entry;
                    if (tile == null || drawEntry == null) continue;
                    operationHealthPresentationPacketDraws++;
                    // Diagnostic witness for the exact rev007 failure class. A non-zero
                    // value after rev008 means some non-prune path still invalidated a
                    // snapshot-owned Mesh and must fail visual acceptance.
                    if (!HasRenderableTerrain(drawEntry))
                        operationHealthSnapshotStaleMeshDetections++;
                    // AERIS25_CHUNK_CULL_GUARD remains the accepted broad-phase
                    // + fail-open projected witness authority. AERIS25_RENDERABLE_ENTRY_GATE
                    // rolls back rev005 foundation-cull bypass after runtime proved it did not
                    // remove holes and caused severe submission regression. Hole correctness
                    // is now enforced at Entry promotion rather than by weakening culling.
                    if (entryCullingEnabled &&
                        ShouldCullEntryOutsidePresentation(drawEntry,
                            projection.CenterX, projection.CenterY, projection.CenterZ,
                            viewportCullSin, viewportCullCos))
                    {
                        if (TileMayIntersectPresentation(tile, projection))
                        {
                            operationHealthCulledEntries = Math.Max(0L,
                                operationHealthCulledEntries - 1L);
                            operationHealthVisibleEntries++;
                            operationHealthCullGuardVetoes++;
                        }
                        else
                        {
                            operationHealthCullGuardConfirmed++;
                            continue;
                        }
                    }
                    operationHealthPreparedEntryUses++;
                    Matrix4x4 projectionBridge = EnsureProjectedGeometry(drawEntry, projection,
                        projectionThresholdMeters, projectionCenterLatitudeDeg,
                        projectionCenterLongitudeDeg, forceCenterProjectionRefresh);
                    // Cached geometry is N-UP. Apply the tiny center-motion bridge first,
                    // then the existing exact scale-corrected TRACK-UP rotation.
                    Matrix4x4 entryMapMatrix = mapRotation * projectionBridge;
                    bool entryRendered = DrawEntry(drawEntry, entryMapMatrix, true, effectiveMode,
                        settings == null ? AERISTerrainColourPreset.Standard :
                        settings.TerrainColourPreset, (float)vessel.altitude);
                    rendered = entryRendered || rendered;
                    if (entryRendered && packet.ExactDetailOverlay)
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
            if (staggerBurstTelemetryEligible)
            {
                long exactThisBack = Math.Max(0L,
                    operationHealthProjectionExactRefreshes - exactRefreshesAtBackStart);
                operationHealthStaggeredExactBackSamples++;
                if (exactThisBack > operationHealthStaggeredExactBackPeak)
                    operationHealthStaggeredExactBackPeak = exactThisBack;
                if (exactThisBack > 8L) operationHealthStaggeredExactBackOverEight++;
            }
            long penicillinBackEndTicks = Stopwatch.GetTimestamp();
            double penicillinBackMilliseconds =
                (penicillinBackEndTicks - frameStartTicks) *
                1000.0 / Stopwatch.Frequency;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Gpu.RecordFrameCost(penicillinBackMilliseconds);
            long penicillinExactThisBack = Math.Max(0L,
                operationHealthProjectionExactRefreshes - exactRefreshesAtBackStart);
            AERISOperationHealthPenicillin.RecordNavigationDisplayBack(
                penicillinBackMilliseconds, penicillinExactThisBack,
                staggerBurstTelemetryEligible);
            return rendered && !gpuVertexProjectionBackFailure;
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
                if (!HasRenderableTerrain(current) ||
                    current.CoverageFraction < 0.999f) continue;
                operationHealthPreparedEntryUses++;
                if (tile.Key.Lod == AERISTerrainTileLod.Global) readyGlobal++;
                else readyFar++;
            }
            int required = Math.Max(0, visible.FarFoundationCount);
            int ready = Math.Min(required, readyFar);
            return required <= 0 ? 0f : Mathf.Clamp01(ready / (float)required);
        }

        // R018 exact visible-foundation readiness. Reuse the canonical Gate 3.1 planner
        // rather than inventing a second geometry approximation. The planner already owns
        // Track-Up rotation, 1.30 horizontal scale, lower-aircraft anchor and a one-tile
        // guard ring. This method runs only inside R014 full content reconcile.
        void RefreshRev35R019VisibleFarKeys(CelestialBody body,
            AERISTerrainHeightTile[] tiles, double centerLatitudeDeg,
            double centerLongitudeDeg, float visibleRangeMeters,
            float mapHeadingDeg, bool trackUp, float anchorV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            rev35R019VisibleFarKeys.Clear();
            operationHealthRev35R019VisibleKeyCount = 0;
            if (body == null || tiles == null) return;

            string environmentHash = string.Empty;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null || string.IsNullOrEmpty(tile.Key.EnvironmentHash))
                    continue;
                environmentHash = tile.Key.EnvironmentHash;
                break;
            }
            if (string.IsNullOrEmpty(environmentHash)) return;

            AERISTerrainViewportFoundationPlan plan =
                AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,
                    centerLatitudeDeg, centerLongitudeDeg, visibleRangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation);
            if (plan == null || plan.FarKeys == null) return;
            for (int i = 0; i < plan.FarKeys.Length; i++)
                rev35R019VisibleFarKeys.Add(plan.FarKeys[i]);
            operationHealthRev35R019VisibleKeyCount = rev35R019VisibleFarKeys.Count;
        }

        void MeasureVisibleFoundationGpuReadiness(CelestialBody body,
            AERISTerrainHeightTile[] tiles, Entry[] currentEntries,
            double centerLatitudeDeg, double centerLongitudeDeg,
            float visibleRangeMeters, float mapHeadingDeg, bool trackUp,
            float anchorV, AERISTerrainRenderTargetOrientation orientation,
            out bool planValid, out int requiredFar, out int readyFar)
        {
            planValid = false;
            requiredFar = 0;
            readyFar = 0;
            if (body == null || tiles == null) return;

            string environmentHash = string.Empty;
            for (int i = 0; i < tiles.Length; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null || string.IsNullOrEmpty(tile.Key.EnvironmentHash))
                    continue;
                environmentHash = tile.Key.EnvironmentHash;
                break;
            }
            if (string.IsNullOrEmpty(environmentHash)) return;

            AERISTerrainViewportFoundationPlan plan =
                AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,
                    centerLatitudeDeg, centerLongitudeDeg, visibleRangeMeters,
                    mapHeadingDeg, trackUp, anchorV, orientation);
            if (plan == null || plan.FarKeys == null || plan.FarKeys.Length <= 0)
                return;

            requiredFar = plan.FarKeys.Length;
            planValid = true;
            for (int requiredIndex = 0; requiredIndex < plan.FarKeys.Length;
                 requiredIndex++)
            {
                AERISTerrainTileKey requiredKey = plan.FarKeys[requiredIndex];
                for (int tileIndex = 0; tileIndex < tiles.Length; tileIndex++)
                {
                    AERISTerrainHeightTile tile = tiles[tileIndex];
                    if (tile == null || !tile.Key.Equals(requiredKey)) continue;
                    Entry current = currentEntries != null &&
                        tileIndex < currentEntries.Length ?
                        currentEntries[tileIndex] : null;
                    if (current != null && current.CoverageFraction >= 0.999f)
                        readyFar++;
                    break;
                }
            }
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
            frontReloadGeneration = ndReloadGeneration;
            requestedViewReady = true;
            reloadSnapshotActive = false;
            reloadSnapshotPending = false;
            reloadProgressPercentFloor = 100;
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
            if (Reloading) return false;
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

        bool TileMayIntersectPresentation(AERISTerrainHeightTile tile,
            AERISNdMapProjection projection)
        {
            if (tile == null) return true;
            const float safetyMargin = 0.06f;
            float minU = float.PositiveInfinity;
            float maxU = float.NegativeInfinity;
            float minV = float.PositiveInfinity;
            float maxV = float.NegativeInfinity;
            double latitudeSpan = tile.NorthLatitudeDeg - tile.SouthLatitudeDeg;
            double longitudeSpan = NormalizeLongitudeDelta(
                tile.EastLongitudeDeg - tile.WestLongitudeDeg);
            for (int row = 0; row < 3; row++)
            {
                double fy = row * 0.5;
                double latitudeDeg = tile.SouthLatitudeDeg + latitudeSpan * fy;
                for (int column = 0; column < 3; column++)
                {
                    double fx = column * 0.5;
                    double longitudeDeg = NormalizeLongitudeDegrees(
                        tile.WestLongitudeDeg + longitudeSpan * fx);
                    float u, v;
                    projection.ProjectLatitudeLongitudeToGui(latitudeDeg,
                        longitudeDeg, out u, out v);
                    if (float.IsNaN(u) || float.IsInfinity(u) ||
                        float.IsNaN(v) || float.IsInfinity(v)) return true;
                    minU = Math.Min(minU, u);
                    maxU = Math.Max(maxU, u);
                    minV = Math.Min(minV, v);
                    maxV = Math.Max(maxV, v);
                }
            }
            // Fail open toward drawing. The guard is deliberately conservative:
            // a projected witness box near the display is sufficient to keep the Entry.
            return maxU >= -safetyMargin && minU <= 1f + safetyMargin &&
                maxV >= -safetyMargin && minV <= 1f + safetyMargin;
        }

        bool ShouldCullEntryOutsidePresentation(Entry entry,
            double centerX, double centerY, double centerZ,
            double viewportRadiusSin, double viewportRadiusCos)
        {
            operationHealthCullTests++;
            operationHealthDotCapCullTests++;
            if (entry == null ||
                double.IsNaN(entry.BoundAngularRadiusRad) ||
                double.IsInfinity(entry.BoundAngularRadiusRad) ||
                entry.BoundAngularRadiusRad >= Math.PI * 0.50 ||
                entry.BoundRadiusCos < 0.0 ||
                double.IsNaN(entry.BoundRadiusCos) ||
                double.IsInfinity(entry.BoundRadiusCos))
            {
                operationHealthVisibleEntries++;
                return false;
            }

            // cos(viewport + entryRadius) from one viewport sin/cos pair and the
            // Entry's precomputed radius pair. If the combined cap reaches a hemisphere
            // or more, do not cull; this deliberately biases toward extra work.
            double thresholdCos = viewportRadiusCos * entry.BoundRadiusCos -
                viewportRadiusSin * entry.BoundRadiusSin;
            double combinedSin = viewportRadiusSin * entry.BoundRadiusCos +
                viewportRadiusCos * entry.BoundRadiusSin;
            if (combinedSin <= 0.0 || thresholdCos <= 0.0)
            {
                operationHealthVisibleEntries++;
                return false;
            }
            double dot = centerX * entry.BoundCenterX +
                centerY * entry.BoundCenterY + centerZ * entry.BoundCenterZ;
            if (double.IsNaN(dot) || double.IsInfinity(dot))
            {
                operationHealthVisibleEntries++;
                return false;
            }
            // Dot smaller than cos(combined radius) proves the two conservative caps
            // cannot touch. Equality remains visible, preserving the old safety bias.
            bool culled = dot < thresholdCos;
            if (culled) operationHealthCulledEntries++;
            else operationHealthVisibleEntries++;
            return culled;
        }

        static bool ResolveViewportCullCap(CelestialBody body, float rangeMeters,
            float anchorV, out double radiusSin, out double radiusCos)
        {
            radiusSin = 0.0;
            radiusCos = -1.0;
            if (body == null || body.Radius <= 0.0) return false;
            // Exactly preserve the accepted conservative viewport margins from the old
            // great-circle implementation: circumscribed rectangle *1.08 plus the larger
            // of 2.5 km or 3% range. Add one microradian only in the safe direction.
            double horizontal = Math.Max(1.0, rangeMeters * 0.65);
            double vertical = Math.Max(1.0, rangeMeters * Math.Max(
                Mathf.Clamp01(anchorV), 1f - Mathf.Clamp01(anchorV)));
            double viewportRadius = Math.Sqrt(horizontal * horizontal +
                vertical * vertical);
            double viewportSafetyRadius = viewportRadius * 1.08 +
                Math.Max(2500.0, Math.Max(1f, rangeMeters) * 0.03);
            double angularRadius = Math.Min(Math.PI * 0.499999,
                viewportSafetyRadius / body.Radius + 0.000001);
            if (angularRadius <= 0.0 || double.IsNaN(angularRadius) ||
                double.IsInfinity(angularRadius)) return false;
            radiusSin = Math.Sin(angularRadius);
            radiusCos = Math.Cos(angularRadius);
            return radiusCos > 0.0;
        }

        static void ResolveSphericalCapFastData(double centerLatitudeDeg,
            double centerLongitudeDeg, double angularRadiusRad,
            out double centerX, out double centerY, out double centerZ,
            out double radiusSin, out double radiusCos)
        {
            centerX = centerY = centerZ = 0.0;
            radiusSin = 0.0;
            radiusCos = -1.0;
            if (double.IsNaN(centerLatitudeDeg) ||
                double.IsInfinity(centerLatitudeDeg) ||
                double.IsNaN(centerLongitudeDeg) ||
                double.IsInfinity(centerLongitudeDeg) ||
                double.IsNaN(angularRadiusRad) ||
                double.IsInfinity(angularRadiusRad) ||
                angularRadiusRad <= 0.0 || angularRadiusRad >= Math.PI * 0.50) return;
            double lat = centerLatitudeDeg * Math.PI / 180.0;
            double lon = centerLongitudeDeg * Math.PI / 180.0;
            double cosLat = Math.Cos(lat);
            centerX = cosLat * Math.Cos(lon);
            centerY = cosLat * Math.Sin(lon);
            centerZ = Math.Sin(lat);
            radiusSin = Math.Sin(angularRadiusRad);
            radiusCos = Math.Cos(angularRadiusRad);
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
                "; oh_dot_cap_test=" + operationHealthDotCapCullTests +
                "; oh_cull_guard_veto=" + operationHealthCullGuardVetoes +
                "; oh_cull_guard_confirm=" + operationHealthCullGuardConfirmed +
                "; oh_foundation_cull_bypass=" + operationHealthFoundationCullBypass +
                "; oh_nonrenderable_entry_reject=" + operationHealthNonRenderableEntryRejects +
                "; oh_fallback_shadow_prevent=" + operationHealthFallbackShadowPrevents +
                "; oh_empty_triangle_result=" + operationHealthEmptyTriangleResults +
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
                "; oh_terrain_single_build=" + operationHealthPackedTerrainBuilds +
                "; oh_terrain_pack_draw=" + operationHealthPackedTerrainDraws +
                "; oh_terrain_pack_saved=" + operationHealthPackedTerrainDrawSubmissionsSaved +
                "; oh_draw_mesh=" + operationHealthDrawMeshSubmissions +
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
                "; oh_project_exact=" + operationHealthProjectionExactRefreshes +
                "; oh_project_bridge=" + operationHealthProjectionBridgeUses +
                "; oh_affine_bridge=" + operationHealthAffineBridgeUses +
                "; oh_affine_reject=" + operationHealthAffineBridgeRejects +
                "; oh_affine_witness=" + operationHealthAffineWitnessTests +
                "; oh_affine_exact_fallback=" + operationHealthAffineExactFallbacks +
                "; oh_affine_max_mpx=" + operationHealthAffineWitnessMaxMilliPixels +
                "; oh_stagger_due=" + operationHealthStaggeredExactDue +
                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +
                "; oh_stagger_back_peak=" + operationHealthStaggeredExactBackPeak +
                "; oh_stagger_back_samples=" + operationHealthStaggeredExactBackSamples +
                "; oh_stagger_back_gt8=" + operationHealthStaggeredExactBackOverEight +
                "; oh_gpu_vertex_requested=" + gpuVertexProjection.RequestedModeName +
                "; oh_gpu_vertex_projection=" + gpuVertexProjection.EffectiveModeName +
                "; oh_gpu_vertex_backend_switch=" + operationHealthProjectionBackendSwitches +
                "; oh_gpu_vertex_activation=" + gpuVertexProjection.ActivationCount +
                "; oh_gpu_vertex_resident_suspend=" + gpuVertexProjection.ResidentSuspensionCount +
                "; oh_nd_warm_visibility=" + (warmVisibilitySuspended ? "HIDDEN" : "LIVE") +
                "; oh_nd_warm_suspend_count=" + operationHealthWarmVisibilitySuspends +
                "; oh_nd_warm_resume_count=" + operationHealthWarmVisibilityResumes +
                "; oh_nd_warm_preserved_entries=" + warmVisibilityPreservedEntries +
                "; oh_nd_warm_preserved_bytes=" + warmVisibilityPreservedBytes +
                "; oh_nd_warm_mesh_destroy_delta=" + Math.Max(0L,
                    operationHealthMeshPoolDestroys - warmVisibilityMeshDestroyBaseline) +
                "; oh_nd_warm_attr_upload_delta=" + Math.Max(0L,
                    operationHealthGpuVertexAttributeUploads - warmVisibilityAttributeUploadBaseline) +
                "; oh_nd_warm_prune_pending=" + (warmVisibilityPrunePending ? 1 : 0) +
                "; oh_nd_warm_prune_active=" + (warmVisibilityPruneActive ? 1 : 0) +
                "; oh_nd_warm_prune_ticks=" + operationHealthWarmPruneTicks +
                "; oh_nd_warm_prune_removed=" + operationHealthWarmPruneRemoved +
                "; oh_nd_warm_prune_deferred=" + operationHealthWarmPruneDeferrals +
                "; oh_snapshot_mesh_prune_protect=" + operationHealthSnapshotMeshPruneProtected +
                "; oh_snapshot_mesh_prune_defer=" + operationHealthSnapshotMeshPruneDeferrals +
                "; oh_snapshot_stale_mesh=" + operationHealthSnapshotStaleMeshDetections +
                "; oh_content_commit_budget_hit=" + operationHealthContentCommitBudgetHits +
                "; oh_content_commit_backlog_peak=" + operationHealthContentCommitBacklogPeak +
                "; oh_prune_budget_hit=" + operationHealthPruneBudgetHits +
                "; oh_prune_debt_peak_bytes=" + operationHealthPruneDebtPeakBytes +
                "; oh_heading_plan_coalesced=" + operationHealthContentHeadingCoalesced +
                "; oh_presentation_packet_count=" + presentationPacketCount +
                "; oh_presentation_packet_rebuild=" + operationHealthPresentationPacketRebuilds +
                "; oh_presentation_packet_reuse=" + operationHealthPresentationPacketReuses +
                "; oh_presentation_packet_slot_skip=" + operationHealthPresentationPacketSlotsSkipped +
                "; oh_presentation_pin_hit=" + operationHealthPresentationPinHits +
                "; oh_presentation_pin_miss=" + operationHealthPresentationPinMisses +
                "; oh_presentation_packet_draw=" + operationHealthPresentationPacketDraws +
                "; oh_main_commit_budget_hit=" + operationHealthMainCommitBudgetHits +
                "; oh_main_commit_backlog=" + operationHealthMainCommitBacklog +
                "; oh_main_commit_backlog_peak=" + operationHealthMainCommitBacklogPeak +
                "; oh_main_commit_window_max_ms=" + operationHealthMainCommitWindowMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_overbudget=" + operationHealthMainCommitOverbudget +
                "; oh_main_commit_processed=" + operationHealthMainCommitProcessed +
                "; oh_main_commit_budget_ms=" + operationHealthMainCommitBudgetMilliseconds.ToString("F2", CultureInfo.InvariantCulture) +
                "; oh_main_commit_pending=" + (pendingEntryCommit == null ? 0 : 1) +
                "; oh_main_commit_pending_stage=" + (pendingEntryCommit == null ? "NONE" : pendingEntryCommit.Stage.ToString()) +
                "; oh_main_commit_stage_yield=" + operationHealthMainCommitStageYields +
                "; oh_main_commit_stage_max_ms=" + operationHealthMainCommitStageMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_clip_max_ms=" + operationHealthMainCommitClipMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_prepare_max_ms=" + operationHealthMainCommitPrepareMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_terrain_upload_max_ms=" + operationHealthMainCommitTerrainUploadMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_contour_max_ms=" + operationHealthMainCommitContourMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_coastline_max_ms=" + operationHealthMainCommitCoastlineMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_geo_max_ms=" + operationHealthMainCommitGeographicMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_finalize_max_ms=" + operationHealthMainCommitFinalizeMaxMilliseconds.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_main_commit_publish=" + operationHealthMainCommitPublishes +
                "; oh_rev35_variant=" + Rev35Variant +
                "; oh_rev35_prepare_source_yield=" + operationHealthRev35PrepareSourceYields +
                "; oh_rev35_prepare_packed_yield=" + operationHealthRev35PreparePackedYields +
                "; oh_rev35_r002_variant=" + Rev35R002Variant +
                "; oh_rev35_packed_source_alloc_max_ms=" + operationHealthRev35PackedSourceAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_packed_colour_alloc_max_ms=" + operationHealthRev35PackedColourAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_packed_index_alloc_max_ms=" + operationHealthRev35PackedIndexAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r003_variant=" + Rev35R003Variant +
                "; oh_rev35_r003_stale_pending_cancel=" + operationHealthRev35R003StalePendingCancels +
                "; oh_rev35_r003_stale_completed_skip=" + operationHealthRev35R003StaleCompletedSkips +
                "; oh_rev35_r003_relevant_admit=" + operationHealthRev35R003RelevantAdmissions +
                "; oh_rev35_r004_variant=" + Rev35R004Variant +
                "; oh_rev35_r004_budget_050=" + operationHealthRev35R004Budget050 +
                "; oh_rev35_r004_budget_100=" + operationHealthRev35R004Budget100 +
                "; oh_rev35_r004_budget_150=" + operationHealthRev35R004Budget150 +
                "; oh_rev35_r004_budget_200=" + operationHealthRev35R004Budget200 +
                "; oh_rev35_r004_frame_guard=" + operationHealthRev35R004FrameGuard +
                "; oh_rev35_r004_alloc_continue=" + operationHealthRev35R004AllocationContinues +
                "; oh_rev35_r004_budget_max_ms=" + operationHealthRev35R004BudgetMaxMs.ToString("F2", CultureInfo.InvariantCulture) +
                "; oh_rev35_r004_chunk_max_items=" + operationHealthRev35R004ChunkMaxItems +
                "; oh_rev35_r005_variant=" + Rev35R005Variant +
                "; oh_rev35_r005_source_chunk_cap=" + Rev35R005SourceChunkHardCap +
                "; oh_rev35_r005_source_windows=" + operationHealthRev35R005SourceHardCapWindows +
                "; oh_rev35_r005_packed_chunk_max_items=" + operationHealthRev35R005PackedChunkMaxItems +
                "; oh_rev35_r006_variant=" + Rev35R006Variant +
                "; oh_rev35_r006_geo_pool_hit=" + operationHealthRev35R006GeoPoolHit +
                "; oh_rev35_r006_geo_pool_miss=" + operationHealthRev35R006GeoPoolMiss +
                "; oh_rev35_r006_geo_pool_reject=" + operationHealthRev35R006GeoPoolReject +
                "; oh_rev35_r006_geo_pool_recycle=" + operationHealthRev35R006GeoPoolRecycle +
                "; oh_rev35_r006_geo_pool_arrays=" + rev35R006GeographicPoolArrays +
                "; oh_rev35_r006_geo_pool_bytes=" + rev35R006GeographicPoolBytes +
                "; oh_rev35_r006_geo_alloc_max_ms=" + operationHealthRev35R006GeoAllocationMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_geo_max_items=" + operationHealthRev35R006GeoMaxItems +
                "; oh_rev35_r006_projected_transfer=" + operationHealthRev35R006ProjectedOwnershipTransfers +
                "; oh_rev35_r006_finalize_wait_current_ms=" + CurrentRev35R006FinalizeWaitMilliseconds().ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_finalize_wait_max_ms=" + operationHealthRev35R006FinalizeWaitMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_finalize_wait_samples=" + operationHealthRev35R006FinalizeWaitSamples +
                "; oh_rev35_r006_missing_far=" + operationHealthRev35R006FoundationMissingFar +
                "; oh_rev35_r006_missing_partial=" + operationHealthRev35R006FoundationMissingPartial +
                "; oh_rev35_r006_missing_pending=" + operationHealthRev35R006FoundationMissingPending +
                "; oh_rev35_r006_missing_render_ready=" + operationHealthRev35R006FoundationMissingRenderReady +
                "; oh_rev35_r006_missing_upstream=" + operationHealthRev35R006FoundationMissingUpstream +
                "; oh_rev35_r006_contour_only_fallback=" + operationHealthRev35R006ContourOnlyFallback +
                "; oh_rev35_r006_source_incomplete=" + operationHealthRev35R006FoundationSourceIncomplete +
                "; oh_rev35_r006_foundation_wait_ms=" + operationHealthRev35R006FoundationWaitCurrentMs.ToString("F1", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_foundation_wait_max_ms=" + operationHealthRev35R006FoundationWaitMaxMs.ToString("F1", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_wait_500=" + operationHealthRev35R006FoundationWait500 +
                "; oh_rev35_r006_wait_1000=" + operationHealthRev35R006FoundationWait1000 +
                "; oh_rev35_r006_wait_2000=" + operationHealthRev35R006FoundationWait2000 +
                "; oh_rev35_r006_wait_3000=" + operationHealthRev35R006FoundationWait3000 +
                "; oh_rev35_r006_wait_5000=" + operationHealthRev35R006FoundationWait5000 +
                "; oh_rev35_r006_gpu_attr_grow=" + operationHealthRev35R006GpuAttrGrow +
                "; oh_rev35_r006_gpu_attr_grow_max_ms=" + operationHealthRev35R006GpuAttrGrowMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_gpu_attr_capacity_max=" + operationHealthRev35R006GpuAttrCapacityMax +
                "; oh_rev35_r006_hf4_variant=" + Rev35R006PackedManagedBufferHotfix4 +
                "; oh_rev35_r006_hf4_colour_pool_hit=" + operationHealthRev35R006Hf4ColourPoolHit +
                "; oh_rev35_r006_hf4_colour_pool_miss=" + operationHealthRev35R006Hf4ColourPoolMiss +
                "; oh_rev35_r006_hf4_colour_pool_recycle=" + operationHealthRev35R006Hf4ColourPoolRecycle +
                "; oh_rev35_r006_hf4_colour_pool_reject=" + operationHealthRev35R006Hf4ColourPoolReject +
                "; oh_rev35_r006_hf4_colour_pool_arrays=" + rev35R006Hf4ColourPoolArrays +
                "; oh_rev35_r006_hf4_colour_pool_bytes=" + rev35R006Hf4ColourPoolBytes +
                "; oh_rev35_r006_hf4_colour_new_alloc_max_ms=" + operationHealthRev35R006Hf4ColourNewAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_hf4_colour_max_items=" + operationHealthRev35R006Hf4ColourMaxItems +
                "; oh_rev35_r006_hf4_colour_transfer=" + operationHealthRev35R006Hf4ColourOwnershipTransfer +
                "; oh_rev35_r006_hf4_index_pool_hit=" + operationHealthRev35R006Hf4IndexPoolHit +
                "; oh_rev35_r006_hf4_index_pool_miss=" + operationHealthRev35R006Hf4IndexPoolMiss +
                "; oh_rev35_r006_hf4_index_pool_recycle=" + operationHealthRev35R006Hf4IndexPoolRecycle +
                "; oh_rev35_r006_hf4_index_pool_reject=" + operationHealthRev35R006Hf4IndexPoolReject +
                "; oh_rev35_r006_hf4_index_pool_arrays=" + rev35R006Hf4IndexPoolArrays +
                "; oh_rev35_r006_hf4_index_pool_bytes=" + rev35R006Hf4IndexPoolBytes +
                "; oh_rev35_r006_hf4_index_new_alloc_max_ms=" + operationHealthRev35R006Hf4IndexNewAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r006_hf4_index_max_items=" + operationHealthRev35R006Hf4IndexMaxItems +
                "; oh_rev35_r007_variant=" + Rev35R007Variant +
                "; oh_rev35_r007_queue=" + rev35R007FoundationQueue.Count +
                "; oh_rev35_r007_queue_peak=" + operationHealthRev35R007QueuePeak +
                "; oh_rev35_r007_queued=" + operationHealthRev35R007Queued +
                "; oh_rev35_r007_chain=" + operationHealthRev35R007ChainedBegins +
                "; oh_rev35_r007_immediate=" + operationHealthRev35R007ImmediateBegins +
                "; oh_rev35_r007_duplicate=" + operationHealthRev35R007DuplicateSkips +
                "; oh_rev35_r007_stale=" + operationHealthRev35R007StaleSkips +
                "; oh_rev35_r007_already=" + operationHealthRev35R007AlreadyCommittedSkips +
                "; oh_rev35_r007_missing=" + operationHealthRev35R007MissingFieldSkips +
                "; oh_rev35_r007_overflow=" + operationHealthRev35R007Overflow +
                "; oh_rev35_r007_reset=" + operationHealthRev35R007QueueResets +
                "; oh_rev35_r008_variant=" + Rev35R008Variant +
                "; oh_rev35_r008_pump_suppress=" + operationHealthRev35R008GeometryPumpSuppress +
                "; oh_rev35_r008_pending_cancel=" + operationHealthRev35R008PendingCommitCancelled +
                "; oh_rev35_r008_far_schedule=" + operationHealthRev35R008FoundationScheduleFirst +
                "; oh_rev35_r008_reconcile=" + rasterizer.Rev35R008Reconciliations +
                "; oh_rev35_r008_raster_pending_cancel=" + rasterizer.Rev35R008PendingCancelled +
                "; oh_rev35_r008_raster_completed_drop=" + rasterizer.Rev35R008CompletedDropped +
                "; oh_rev35_r008_scheduler_cancel=" + rasterizer.Rev35R008SchedulerCancels +
                "; oh_rev35_r009_variant=" + Rev35R009Variant +
                "; oh_rev35_r009_admit_accept=" + rasterizer.Rev35R009AdmissionAccepted +
                "; oh_rev35_r009_admit_reject=" + rasterizer.Rev35R009AdmissionRejected +
                "; oh_rev35_r009_pending_registered=" + rasterizer.Rev35R009PendingRegistered +
                "; oh_rev35_r009_duplicate_pending=" + rasterizer.Rev35R009DuplicatePending +
                "; oh_rev35_r009_terminal_null=" + rasterizer.Rev35R009TerminalNull +
                "; oh_rev35_r010_variant=" + Rev35R010Variant +
                "; oh_rev35_r010_queue_budget_samples=" + operationHealthRev35R010QueueBacklogBudgetSamples +
                "; oh_rev35_r010_queue_backlog_peak=" + operationHealthRev35R010QueueBacklogPeak +
                "; oh_rev35_r014_variant=" + Rev35R014Variant +
                "; oh_rev35_r014_pub_serial=" + rev35R014PublicationSerial +
                "; oh_rev35_r014_reconciled_serial=" + rev35R014ReconciledPublicationSerial +
                "; oh_rev35_r014_publications=" + operationHealthRev35R014PublicationEvents +
                "; oh_rev35_r014_full_reconcile=" + operationHealthRev35R014FullReconciles +
                "; oh_rev35_r014_worker_only_skip=" + operationHealthRev35R014WorkerOnlySkips +
                "; oh_rev35_r014_publication_defer=" + operationHealthRev35R014PublicationDeferrals +
                "; oh_rev35_r014_publication_reconcile=" + operationHealthRev35R014PublicationReconciles +
                "; oh_rev35_r014_retry_reconcile=" + operationHealthRev35R014RetryReconciles +
                "; oh_rev35_r018_variant=" + Rev35R018Variant +
                "; oh_rev35_r018_visible_plan_valid=" + (operationHealthRev35R018VisiblePlanValid ? 1 : 0) +
                "; oh_rev35_r018_visible_required_far=" + operationHealthRev35R018VisibleRequiredFar +
                "; oh_rev35_r018_visible_ready_far=" + operationHealthRev35R018VisibleReadyFar +
                "; oh_rev35_r018_visible_coverage=" + operationHealthRev35R018VisibleCoverage.ToString("F3", CultureInfo.InvariantCulture) +
                "; oh_rev35_r018_overscan_required_far=" + operationHealthRev35R018OverscanRequiredFar +
                "; oh_rev35_r018_overscan_ready_far=" + operationHealthRev35R018OverscanReadyFar +
                "; oh_rev35_r018_overscan_hol_avoided=" + operationHealthRev35R018OverscanHolAvoided +
                "; oh_rev35_r019_variant=" + Rev35R019Variant +
                "; oh_rev35_r019_hf1_variant=" + Rev35R019Hotfix1Variant +
                "; oh_rev35_r020_variant=" + Rev35R020Variant +
                "; oh_rev35_r021_variant=" + Rev35R021Variant +
                "; oh_rev35_r020_authority_samples=" + operationHealthRev35R020AuthoritySamples +
                "; oh_rev35_r020_generation_retained=" + operationHealthRev35R020GenerationRetained +
                "; oh_rev35_r020_generation_advance=" + operationHealthRev35R020GenerationAdvances +
                "; oh_rev35_r019_visible_keys=" + operationHealthRev35R019VisibleKeyCount +
                "; oh_rev35_r019_visible_priority_queue=" + rev35R019VisibleFoundationQueue.Count +
                "; oh_rev35_r019_visible_priority_peak=" + operationHealthRev35R019VisiblePriorityQueuePeak +
                "; oh_rev35_r019_visible_priority_queued=" + operationHealthRev35R019VisiblePriorityQueued +
                "; oh_rev35_r019_visible_priority_begin=" + operationHealthRev35R019VisiblePriorityBegins +
                "; oh_rev35_r019_hidden_queue_bypass=" + operationHealthRev35R019HiddenQueueBypassed +
                "; oh_rev35_r019_visible_deficit=" + operationHealthRev35R019VisibleDeficit +
                "; oh_rev35_r019_budget_100=" + operationHealthRev35R019Budget100 +
                "; oh_rev35_r019_budget_150=" + operationHealthRev35R019Budget150 +
                "; oh_main_commit_publish_defer=" + operationHealthMainCommitPublicationDeferrals +
                "; oh_deferred_retire_pending=" + deferredEntryRetirements.Count +
                "; oh_deferred_retire_queued=" + operationHealthDeferredRetireQueued +
                "; oh_deferred_retire_released=" + operationHealthDeferredRetireReleased +
                "; oh_deferred_retire_protected=" + operationHealthDeferredRetireProtected +
                "; oh_deferred_retire_peak=" + operationHealthDeferredRetirePeak +
                "; oh_nd_reload=" + (Reloading ? "BLACK" : "READY") +
                "; oh_nd_reload_pct=" + ReloadProgressPercent +
                "; oh_nd_reload_generation=" + ndReloadGeneration +
                "; oh_nd_front_reload_generation=" + frontReloadGeneration +
                "; oh_nd_reload_snapshot=" + (reloadSnapshotActive ? "LOCKED" : "LIVE") +
                "; oh_nd_reload_snapshot_capture=" + operationHealthReloadSnapshotCaptures +
                "; oh_nd_reload_snapshot_frames=" + operationHealthReloadSnapshotFrames +
                "; oh_gpu_vertex_attr_upload=" + operationHealthGpuVertexAttributeUploads +
                "; oh_gpu_vertex_attr_fail=" + operationHealthGpuVertexAttributeFailures +
                "; oh_gpu_vertex_packed_mismatch=" + operationHealthGpuVertexPackedMismatch +
                "; oh_gpu_vertex_contour_mismatch=" + operationHealthGpuVertexContourMismatch +
                "; oh_gpu_vertex_coast_mismatch=" + operationHealthGpuVertexCoastlineMismatch +
                "; oh_gpu_vertex_reject_initial=" + operationHealthGpuVertexRejectInitial +
                "; oh_gpu_vertex_reject_revisit=" + operationHealthGpuVertexRejectRevisits +
                "; oh_gpu_vertex_reject_packed_null=" + operationHealthGpuVertexRejectPackedNull +
                "; oh_gpu_vertex_reject_packed_length=" + operationHealthGpuVertexRejectPackedLength +
                "; oh_gpu_vertex_reject_contour_null=" + operationHealthGpuVertexRejectContourNull +
                "; oh_gpu_vertex_reject_contour_length=" + operationHealthGpuVertexRejectContourLength +
                "; oh_gpu_vertex_reject_coast_null=" + operationHealthGpuVertexRejectCoastNull +
                "; oh_gpu_vertex_reject_coast_length=" + operationHealthGpuVertexRejectCoastLength +
                "; oh_gpu_vertex_reject_semantic_mesh_null=" + operationHealthGpuVertexRejectSemanticPackedMeshNull +
                "; oh_gpu_vertex_reject_semantic_rejected=" + operationHealthGpuVertexRejectSemanticRejected +
                "; oh_gpu_vertex_reject_semantic_exception=" + operationHealthGpuVertexRejectSemanticException +
                "; oh_gpu_vertex_reject_semantic_other=" + operationHealthGpuVertexRejectSemanticOther +
                "; oh_gpu_vertex_reject_exception=" + operationHealthGpuVertexRejectException +
                "; oh_gpu_vertex_reject_other=" + operationHealthGpuVertexRejectOther +
                "; oh_gpu_vertex_reject_samples=" + operationHealthGpuVertexRejectDiagnosticSamples +
                "; oh_gpu_vertex_exact_bypass=" + operationHealthGpuVertexExactBypasses +
                "; oh_gpu_vertex_back_frames=" + operationHealthGpuVertexBackFrames +
                "; oh_gpu_vertex_draws=" + operationHealthGpuVertexDraws +
                "; oh_gpu_dynamic_colour=" +
                    (gpuVertexProjection.DynamicTerrainColourActive ? "ACTIVE" : "CPU_FALLBACK") +
                "; oh_gpu_dynamic_semantic_upload=" + operationHealthGpuDynamicSemanticUploads +
                "; oh_gpu_dynamic_semantic_fail=" + operationHealthGpuDynamicSemanticFailures +
                "; oh_gpu_dynamic_cpu_colour_bypass=" + operationHealthGpuDynamicCpuColourBypasses +
                "; oh_gpu_dynamic_vertex_submit=" + operationHealthGpuDynamicVerticesSubmitted +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +
                "; oh_ready_transition=" + operationHealthRequestedViewReadyTransitions +
                "; requested_view_ready=" + (requestedViewReady ? "1" : "0") +
                "; oh_content_tick=" + operationHealthContentTicks +
                "; oh_motion_only=" + operationHealthMotionOnlyTicks +
                "; oh_content_capture=" + operationHealthContentCaptures +
                "; oh_content_visible_range=" +
                    operationHealthContentVisibleRangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; oh_content_plan_range=" +
                    operationHealthContentPlanningRangeMeters.ToString("F0", CultureInfo.InvariantCulture) +
                "; oh_temporal_overscan_capture=" + operationHealthTemporalOverscanCaptures +
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
            if (tile.Key.Lod == AERISTerrainTileLod.Far)
                operationHealthRev35R008FoundationScheduleFirst++;
            rasterizer.Enqueue(new AERISTerrainGpuTileRasterRequest
            {
                Generation = ++generation,
                Tile = tile,
                ContoursEnabled = settings == null || settings.TerrainContoursEnabled,
                ShadingEnabled = settings == null || settings.TerrainShadingEnabled,
                ContourIntervalMeters = contourInterval,
                StyleKey = styleKey,
                VirtualDetailProfile = virtualDetail,
                RequestIdentity = cacheKey
            });
        }

        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,
            bool allowPublication)
        {
            if (rev35R008GeometryReconcilePending)
            {
                operationHealthRev35R008GeometryPumpSuppress++;
                return;
            }
            int profileMaximum = performance == null ? 2 :
                Math.Max(1, performance.ActiveProfile.MaximumConcurrentTileIo * 2);
            bool steadyCommitProfile = frontBufferValid && requestedViewReady && !Reloading;
            int burstMaximum = steadyCommitProfile ?
                SteadyContentCommitMaximumResults : BootstrapContentCommitMaximumResults;
            int hardMaximum = Math.Max(1, Math.Min(profileMaximum, burstMaximum));
            double budgetMilliseconds =
                ResolveRev35R004CommitBudget(steadyCommitProfile);
            operationHealthMainCommitBudgetMilliseconds = budgetMilliseconds;
            int publishedThisWindow = 0;
            int staleSkipsThisWindow = 0;
            mainThreadCommitStopwatch.Reset();
            mainThreadCommitStopwatch.Start();

            while (publishedThisWindow < hardMaximum)
            {
                // R003 anti-HOL gate: an immutable worker product may remain useful in the
                // existing render-ready RAM store, but main-thread GPU Entry construction is
                // admitted only while that exact cache key belongs to the latest requested
                // viewport. Cancel obsolete partial commits before they monopolize the one
                // staged-commit slot. No published Entry or FRONT is modified here.
                if (pendingEntryCommit != null && requested.Count > 0 &&
                    !requested.Contains(pendingEntryCommit.CacheKey))
                {
                    CancelPendingEntryCommit();
                    operationHealthRev35R003StalePendingCancels++;
                    staleSkipsThisWindow++;
                    if (staleSkipsThisWindow >= Rev35R003MaximumStaleSkipsPerWindow)
                        break;
                    continue;
                }
                if (pendingEntryCommit == null)
                {
                    if (!TryBeginRev35R019VisibleFoundationCommit())
                        TryBeginRev35R007QueuedFoundationCommit();
                }
                if (pendingEntryCommit == null)
                {
                    completed.Clear();
                    if (rasterizer.Drain(completed, 1) <= 0) break;
                    AERISTerrainGpuTileRasterResult result = completed[0];
                    if (!TryBeginPendingEntryCommit(result)) continue;
                    // TryBegin stores the immutable render-ready field first. If this result
                    // belongs to a tile/style no longer requested, retain that RAM ingredient
                    // for possible later reuse but do not spend staged GPU commit time on it.
                    if (pendingEntryCommit != null && requested.Count > 0 &&
                        !requested.Contains(pendingEntryCommit.CacheKey))
                    {
                        CancelPendingEntryCommit();
                        operationHealthRev35R003StaleCompletedSkips++;
                        staleSkipsThisWindow++;
                        if (staleSkipsThisWindow >= Rev35R003MaximumStaleSkipsPerWindow)
                            break;
                        continue;
                    }
                    operationHealthRev35R003RelevantAdmissions++;
                }

                // AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION: hidden Repaint frames may
                // prepare/upload an Entry, but they may never swap presentation authority or
                // retire an Entry referenced by the current persistent presentation packet.
                if (!allowPublication && pendingEntryCommit != null &&
                    pendingEntryCommit.Stage == PendingEntryCommitStage.Finalize)
                {
                    operationHealthMainCommitPublicationDeferrals++;
                    break;
                }

                bool published;
                bool finished = AdvancePendingEntryCommit(system, budgetMilliseconds,
                    allowPublication, out published);
                if (published)
                {
                    publishedThisWindow++;
                    operationHealthMainCommitProcessed++;
                    operationHealthMainCommitPublishes++;
                }
                if (!finished) break;
                if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                    budgetMilliseconds) break;
            }

            mainThreadCommitStopwatch.Stop();
            double elapsedMilliseconds = mainThreadCommitStopwatch.Elapsed.TotalMilliseconds;
            operationHealthMainCommitWindowMaxMilliseconds = Math.Max(
                operationHealthMainCommitWindowMaxMilliseconds, elapsedMilliseconds);
            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1) +
                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +
                Math.Max(0, rev35R007FoundationQueue.Count);
            operationHealthMainCommitBacklog = finalRemainingCompleted;
            operationHealthMainCommitBacklogPeak = Math.Max(
                operationHealthMainCommitBacklogPeak, finalRemainingCompleted);
            operationHealthMainCommitPendingPeak = Math.Max(
                operationHealthMainCommitPendingPeak, pendingEntryCommit == null ? 0 : 1);
            if (finalRemainingCompleted > 0 && elapsedMilliseconds >= budgetMilliseconds)
                operationHealthMainCommitBudgetHits++;
            if (elapsedMilliseconds > budgetMilliseconds * 1.5)
                operationHealthMainCommitOverbudget++;
            if (finalRemainingCompleted > 0 && publishedThisWindow >= hardMaximum)
                operationHealthContentCommitBudgetHits++;
            operationHealthContentCommitBacklogPeak = Math.Max(
                operationHealthContentCommitBacklogPeak, finalRemainingCompleted);
        }

        double ResolveRev35R004CommitBudget(bool steadyCommitProfile)
        {
            int r010QueueBacklog =
                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +
                Math.Max(0, rev35R007FoundationQueue.Count);
            int backlog = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1) + r010QueueBacklog;
            if (r010QueueBacklog > 0)
            {
                operationHealthRev35R010QueueBacklogBudgetSamples++;
                operationHealthRev35R010QueueBacklogPeak = Math.Max(
                    operationHealthRev35R010QueueBacklogPeak, r010QueueBacklog);
            }
            long generationLag = 0L;
            if (contentTerrainGeneration >= 0L && frontTerrainGeneration >= 0L)
                generationLag = Math.Max(0L,
                    contentTerrainGeneration - frontTerrainGeneration);

            double requestedBudget = steadyCommitProfile ?
                MainThreadCommitSteadyBudgetMilliseconds :
                MainThreadCommitBootstrapBudgetMilliseconds;
            if (backlog >= 24 || generationLag >= 8L)
                requestedBudget = Rev35R004BudgetMaximumMilliseconds;
            else if (backlog >= 12 || generationLag >= 4L)
                requestedBudget = Math.Max(requestedBudget,
                    Rev35R004BudgetOneHalfMilliseconds);
            else if (backlog >= 4 || generationLag >= 2L)
                requestedBudget = Math.Max(requestedBudget,
                    Rev35R004BudgetOneMilliseconds);

            int r019VisibleDeficit = Math.Max(0,
                operationHealthRev35R018VisibleRequiredFar -
                operationHealthRev35R018VisibleReadyFar);
            operationHealthRev35R019VisibleDeficit = r019VisibleDeficit;
            bool r019VisibleCommitWork = pendingEntryCommit != null ||
                rev35R019VisibleFoundationQueue.Count > 0;
            if (r019VisibleCommitWork && r019VisibleDeficit >= 2)
            {
                requestedBudget = Math.Max(requestedBudget,
                    Rev35R004BudgetOneHalfMilliseconds);
                operationHealthRev35R019Budget150++;
            }
            else if (r019VisibleCommitWork && r019VisibleDeficit == 1)
            {
                requestedBudget = Math.Max(requestedBudget,
                    Rev35R004BudgetOneMilliseconds);
                operationHealthRev35R019Budget100++;
            }

            // Real unscaled Unity frame time is only a protective ceiling.
            double frameMilliseconds = Math.Max(0.0,
                Time.unscaledDeltaTime * 1000.0);
            double frameCap = Rev35R004BudgetMaximumMilliseconds;
            if (frameMilliseconds >= Rev35R004FrameGuardHardMilliseconds)
                frameCap = MainThreadCommitSteadyBudgetMilliseconds;
            else if (frameMilliseconds >= Rev35R004FrameGuardSoftMilliseconds)
                frameCap = Rev35R004BudgetOneMilliseconds;
            else if (frameMilliseconds >= Rev35R004FrameGuardMediumMilliseconds)
                frameCap = Rev35R004BudgetOneHalfMilliseconds;
            if (frameCap < requestedBudget)
                operationHealthRev35R004FrameGuard++;

            double selected = Math.Max(MainThreadCommitSteadyBudgetMilliseconds,
                Math.Min(requestedBudget, frameCap));
            if (selected >= 1.75)
                operationHealthRev35R004Budget200++;
            else if (selected >= 1.25)
                operationHealthRev35R004Budget150++;
            else if (selected >= 0.75)
                operationHealthRev35R004Budget100++;
            else
                operationHealthRev35R004Budget050++;
            operationHealthRev35R004BudgetMaxMs = Math.Max(
                operationHealthRev35R004BudgetMaxMs, selected);
            return selected;
        }

        int ResolveRev35R004PrepareChunkItems(double budgetMilliseconds)
        {
            int chunkItems = Rev35PrepareChunkItems;
            if (budgetMilliseconds >= 1.75)
                chunkItems = Rev35R004PrepareChunkHigh;
            else if (budgetMilliseconds >= 1.00)
                chunkItems = Rev35R004PrepareChunkMedium;
            operationHealthRev35R004ChunkMaxItems = Math.Max(
                operationHealthRev35R004ChunkMaxItems, chunkItems);
            return chunkItems;
        }

        bool TryBeginPendingEntryCommit(AERISTerrainRenderReadyHeightField result)
        {
            if (!ValidResult(result)) return false;
            if (result.Triangles.Length < 3)
            {
                operationHealthEmptyTriangleResults++;
                return false;
            }
            string cacheKey = CacheKey(result.Key, result.TileCreatedUtcTicks,
                result.StyleKey);
            result.LastUseSequence = ++renderReadyUseSequence;
            AERISTerrainRenderReadyHeightField existingRenderReady;
            if (!renderReadyFields.TryGetValue(cacheKey, out existingRenderReady) ||
                !ReferenceEquals(existingRenderReady, result))
                StoreRenderReadyField(cacheKey, result);
            if (result.Key.Lod == AERISTerrainTileLod.Far)
            {
                if (result.VirtualDetailLevel ==
                    AERISTerrainVirtualDetailLevel.VirtualRoute) virtualRouteBuilds++;
                else if (result.VirtualDetailLevel ==
                    AERISTerrainVirtualDetailLevel.VirtualLocal) virtualLocalBuilds++;
            }
            landSurfaceScratch.Reset();
            waterSurfaceScratch.Reset();
            pendingEntryCommit = new PendingEntryCommit
            {
                CacheKey = cacheKey,
                Result = result,
                Stage = PendingEntryCommitStage.ClipTriangles,
                Land = landSurfaceScratch,
                Water = waterSurfaceScratch,
                ClipScratch = surfaceClipScratch,
                StartedTicks = Stopwatch.GetTimestamp()
            };
            return true;
        }

        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,
            double budgetMilliseconds, bool allowPublication, out bool published)
        {
            published = false;
            PendingEntryCommit pending = pendingEntryCommit;
            if (pending == null || pending.Result == null) return true;
            while (pendingEntryCommit != null)
            {
                PendingEntryCommitStage executedStage = pending.Stage;
                double stageStart = mainThreadCommitStopwatch.Elapsed.TotalMilliseconds;
                switch (pending.Stage)
                {
                    case PendingEntryCommitStage.ClipTriangles:
                        if (!AdvancePendingClip(pending, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.Stage = PendingEntryCommitStage.PrepareSources;
                        break;
                    case PendingEntryCommitStage.PrepareSources:
                        if (!AdvancePendingSources(pending, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.PrepareSubstage = 0;
                        pending.PrepareCursor = 0;
                        pending.Stage = PendingEntryCommitStage.PreparePackedTerrain;
                        break;
                    case PendingEntryCommitStage.PreparePackedTerrain:
                        if (!AdvancePendingPackedTerrain(pending, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.PrepareSubstage = 0;
                        pending.PrepareCursor = 0;
                        pending.Stage = PendingEntryCommitStage.AcquirePackedTerrainMesh;
                        break;
                    case PendingEntryCommitStage.AcquirePackedTerrainMesh:
                        pending.PackedMesh = AcquirePendingMesh(
                            "AERIS_TERRAIN_PACKED_" + pending.Result.Key.FileStem,
                            pending.PackedSource);
                        pending.Stage = PendingEntryCommitStage.UploadPackedTerrainVertices;
                        break;
                    case PendingEntryCommitStage.UploadPackedTerrainVertices:
                        if (pending.PackedMesh != null)
                            pending.PackedMesh.vertices = pending.PackedSource;
                        pending.Stage = PendingEntryCommitStage.UploadPackedTerrainColours;
                        break;
                    case PendingEntryCommitStage.UploadPackedTerrainColours:
                        if (pending.PackedMesh != null)
                            pending.PackedMesh.colors32 = pending.PackedColours;
                        pending.Stage = PendingEntryCommitStage.UploadPackedTerrainIndices;
                        break;
                    case PendingEntryCommitStage.UploadPackedTerrainIndices:
                        if (pending.PackedMesh != null)
                            pending.PackedMesh.triangles = pending.PackedIndices;
                        pending.Stage = PendingEntryCommitStage.FinalizePackedTerrainMesh;
                        break;
                    case PendingEntryCommitStage.FinalizePackedTerrainMesh:
                        if (pending.PackedMesh != null)
                        {
                            pending.PackedMesh.bounds = NdPresentationBounds;
                            pending.PackedMesh.UploadMeshData(false);
                            operationHealthPackedTerrainBuilds++;
                        }
                        pending.Stage = PendingEntryCommitStage.PrepareContour;
                        break;
                    case PendingEntryCommitStage.PrepareContour:
                        if (!AdvancePendingLinePreparation(
                            pending.Result.ContourSegments,
                            new Color32(255, 255, 255, 210),
                            ref pending.ContourSource, ref pending.ContourColours,
                            ref pending.ContourIndices, ref pending.ContourPrepareCursor,
                            ref pending.ContourIndicesFromCache, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.Stage = PendingEntryCommitStage.AcquireContourMesh;
                        break;
                    case PendingEntryCommitStage.AcquireContourMesh:
                        pending.ContourMesh = AcquirePendingMesh(
                            "AERIS_TERRAIN_CONTOUR_" + pending.Result.Key.FileStem,
                            pending.ContourSource);
                        pending.Stage = PendingEntryCommitStage.UploadContourVertices;
                        break;
                    case PendingEntryCommitStage.UploadContourVertices:
                        if (pending.ContourMesh != null)
                            pending.ContourMesh.vertices = pending.ContourSource;
                        pending.Stage = PendingEntryCommitStage.UploadContourColours;
                        break;
                    case PendingEntryCommitStage.UploadContourColours:
                        if (pending.ContourMesh != null)
                            pending.ContourMesh.colors32 = pending.ContourColours;
                        pending.Stage = PendingEntryCommitStage.UploadContourIndices;
                        break;
                    case PendingEntryCommitStage.UploadContourIndices:
                        if (pending.ContourMesh != null)
                            pending.ContourMesh.SetIndices(pending.ContourIndices,
                                MeshTopology.Lines, 0);
                        pending.Stage = PendingEntryCommitStage.FinalizeContourMesh;
                        break;
                    case PendingEntryCommitStage.FinalizeContourMesh:
                        if (pending.ContourMesh != null)
                        {
                            pending.ContourMesh.bounds = NdPresentationBounds;
                            pending.ContourMesh.UploadMeshData(false);
                        }
                        pending.ContourColours = null;
                        pending.ContourIndices = null;
                        pending.Stage = PendingEntryCommitStage.PrepareCoastline;
                        break;
                    case PendingEntryCommitStage.PrepareCoastline:
                        if (!AdvancePendingLinePreparation(
                            pending.Result.CoastlineSegments,
                            new Color32(185, 225, 255, 245),
                            ref pending.CoastlineSource, ref pending.CoastlineColours,
                            ref pending.CoastlineIndices, ref pending.CoastlinePrepareCursor,
                            ref pending.CoastlineIndicesFromCache, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.Stage = PendingEntryCommitStage.AcquireCoastlineMesh;
                        break;
                    case PendingEntryCommitStage.AcquireCoastlineMesh:
                        pending.CoastlineMesh = AcquirePendingMesh(
                            "AERIS_TERRAIN_COAST_" + pending.Result.Key.FileStem,
                            pending.CoastlineSource);
                        pending.Stage = PendingEntryCommitStage.UploadCoastlineVertices;
                        break;
                    case PendingEntryCommitStage.UploadCoastlineVertices:
                        if (pending.CoastlineMesh != null)
                            pending.CoastlineMesh.vertices = pending.CoastlineSource;
                        pending.Stage = PendingEntryCommitStage.UploadCoastlineColours;
                        break;
                    case PendingEntryCommitStage.UploadCoastlineColours:
                        if (pending.CoastlineMesh != null)
                            pending.CoastlineMesh.colors32 = pending.CoastlineColours;
                        pending.Stage = PendingEntryCommitStage.UploadCoastlineIndices;
                        break;
                    case PendingEntryCommitStage.UploadCoastlineIndices:
                        if (pending.CoastlineMesh != null)
                            pending.CoastlineMesh.SetIndices(pending.CoastlineIndices,
                                MeshTopology.Lines, 0);
                        pending.Stage = PendingEntryCommitStage.FinalizeCoastlineMesh;
                        break;
                    case PendingEntryCommitStage.FinalizeCoastlineMesh:
                        if (pending.CoastlineMesh != null)
                        {
                            pending.CoastlineMesh.bounds = NdPresentationBounds;
                            pending.CoastlineMesh.UploadMeshData(false);
                        }
                        pending.CoastlineColours = null;
                        pending.CoastlineIndices = null;
                        pending.Stage = PendingEntryCommitStage.GeographicPacked;
                        pending.GeographicCursor = 0;
                        break;
                    case PendingEntryCommitStage.GeographicPacked:
                        if (!AdvancePendingGeographic(pending.PackedSource,
                            ref pending.PackedGeographic, ref pending.GeographicCursor,
                            pending.Result, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.Stage = PendingEntryCommitStage.GeographicContour;
                        pending.GeographicCursor = 0;
                        break;
                    case PendingEntryCommitStage.GeographicContour:
                        if (!AdvancePendingGeographic(pending.ContourSource,
                            ref pending.ContourGeographic, ref pending.GeographicCursor,
                            pending.Result, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.Stage = PendingEntryCommitStage.GeographicCoastline;
                        pending.GeographicCursor = 0;
                        break;
                    case PendingEntryCommitStage.GeographicCoastline:
                        if (!AdvancePendingGeographic(pending.CoastlineSource,
                            ref pending.CoastlineGeographic, ref pending.GeographicCursor,
                            pending.Result, budgetMilliseconds))
                            return YieldPendingEntryCommit(executedStage, stageStart, true);
                        pending.Rev35R006FinalizeReadyTicks = Stopwatch.GetTimestamp();
                        pending.Stage = PendingEntryCommitStage.Finalize;
                        break;
                    case PendingEntryCommitStage.Finalize:
                        if (!allowPublication)
                        {
                            operationHealthMainCommitPublicationDeferrals++;
                            return false;
                        }
                        published = FinalizePendingEntryCommit(pending, system);
                        pendingEntryCommit = null;
                        RecordPendingStageCost(executedStage,
                            mainThreadCommitStopwatch.Elapsed.TotalMilliseconds - stageStart);
                        return true;
                }
                double stageElapsed = mainThreadCommitStopwatch.Elapsed.TotalMilliseconds -
                    stageStart;
                pending.AccumulatedMilliseconds += Math.Max(0.0, stageElapsed);
                RecordPendingStageCost(executedStage, stageElapsed);
                if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >= budgetMilliseconds)
                {
                    operationHealthMainCommitStageYields++;
                    return false;
                }
            }
            return true;
        }

        bool YieldPendingEntryCommit(PendingEntryCommitStage stage,
            double stageStartMilliseconds, bool accountElapsed)
        {
            double stageElapsed = mainThreadCommitStopwatch.Elapsed.TotalMilliseconds -
                stageStartMilliseconds;
            if (accountElapsed && pendingEntryCommit != null)
                pendingEntryCommit.AccumulatedMilliseconds += Math.Max(0.0, stageElapsed);
            RecordPendingStageCost(stage, stageElapsed);
            operationHealthMainCommitStageYields++;
            return false;
        }

        void RecordPendingStageCost(PendingEntryCommitStage stage,
            double elapsedMilliseconds)
        {
            elapsedMilliseconds = Math.Max(0.0, elapsedMilliseconds);
            operationHealthMainCommitStageMaxMilliseconds = Math.Max(
                operationHealthMainCommitStageMaxMilliseconds, elapsedMilliseconds);
            switch (stage)
            {
                case PendingEntryCommitStage.ClipTriangles:
                    operationHealthMainCommitClipMaxMilliseconds = Math.Max(
                        operationHealthMainCommitClipMaxMilliseconds, elapsedMilliseconds);
                    break;
                case PendingEntryCommitStage.PrepareSources:
                case PendingEntryCommitStage.PreparePackedTerrain:
                    operationHealthMainCommitPrepareMaxMilliseconds = Math.Max(
                        operationHealthMainCommitPrepareMaxMilliseconds, elapsedMilliseconds);
                    break;
                case PendingEntryCommitStage.AcquirePackedTerrainMesh:
                case PendingEntryCommitStage.UploadPackedTerrainVertices:
                case PendingEntryCommitStage.UploadPackedTerrainColours:
                case PendingEntryCommitStage.UploadPackedTerrainIndices:
                case PendingEntryCommitStage.FinalizePackedTerrainMesh:
                    operationHealthMainCommitTerrainUploadMaxMilliseconds = Math.Max(
                        operationHealthMainCommitTerrainUploadMaxMilliseconds,
                        elapsedMilliseconds);
                    break;
                case PendingEntryCommitStage.PrepareContour:
                case PendingEntryCommitStage.AcquireContourMesh:
                case PendingEntryCommitStage.UploadContourVertices:
                case PendingEntryCommitStage.UploadContourColours:
                case PendingEntryCommitStage.UploadContourIndices:
                case PendingEntryCommitStage.FinalizeContourMesh:
                    operationHealthMainCommitContourMaxMilliseconds = Math.Max(
                        operationHealthMainCommitContourMaxMilliseconds,
                        elapsedMilliseconds);
                    break;
                case PendingEntryCommitStage.PrepareCoastline:
                case PendingEntryCommitStage.AcquireCoastlineMesh:
                case PendingEntryCommitStage.UploadCoastlineVertices:
                case PendingEntryCommitStage.UploadCoastlineColours:
                case PendingEntryCommitStage.UploadCoastlineIndices:
                case PendingEntryCommitStage.FinalizeCoastlineMesh:
                    operationHealthMainCommitCoastlineMaxMilliseconds = Math.Max(
                        operationHealthMainCommitCoastlineMaxMilliseconds,
                        elapsedMilliseconds);
                    break;
                case PendingEntryCommitStage.GeographicPacked:
                case PendingEntryCommitStage.GeographicContour:
                case PendingEntryCommitStage.GeographicCoastline:
                    operationHealthMainCommitGeographicMaxMilliseconds = Math.Max(
                        operationHealthMainCommitGeographicMaxMilliseconds,
                        elapsedMilliseconds);
                    break;
                case PendingEntryCommitStage.Finalize:
                    operationHealthMainCommitFinalizeMaxMilliseconds = Math.Max(
                        operationHealthMainCommitFinalizeMaxMilliseconds,
                        elapsedMilliseconds);
                    break;
            }
        }

        Mesh AcquirePendingMesh(string name, Vector3[] source)
        {
            if (source == null || source.Length == 0) return null;
            return AcquireMesh(name, source.Length);
        }

        bool AdvancePendingLinePreparation(float[] segments, Color32 colour,
            ref Vector3[] source, ref Color32[] colours, ref int[] indices,
            ref int cursor, ref bool indicesFromCache, double budgetMilliseconds)
        {
            if (segments == null || segments.Length < 4 || segments.Length % 4 != 0)
            {
                source = null;
                colours = null;
                indices = null;
                cursor = 0;
                indicesFromCache = false;
                return true;
            }
            int vertexCount = segments.Length / 2;
            if (source == null || source.Length != vertexCount)
                source = new Vector3[vertexCount];
            if (colours == null || colours.Length != vertexCount)
                colours = new Color32[vertexCount];
            if (indices == null || indices.Length != vertexCount)
            {
                int[] cached;
                if (identityIndexCache.TryGetValue(vertexCount, out cached))
                {
                    indices = cached;
                    indicesFromCache = true;
                    operationHealthIdentityIndexHits++;
                }
                else
                {
                    indices = new int[vertexCount];
                    indicesFromCache = false;
                    operationHealthIdentityIndexMisses++;
                }
            }
            int iterations = 0;
            while (cursor < vertexCount)
            {
                source[cursor] = new Vector3(segments[cursor * 2],
                    segments[cursor * 2 + 1], 0f);
                colours[cursor] = colour;
                if (!indicesFromCache) indices[cursor] = cursor;
                cursor++;
                iterations++;
                if ((iterations & 63) == 0 &&
                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                    budgetMilliseconds)
                    return false;
            }
            if (!indicesFromCache && !identityIndexCache.ContainsKey(vertexCount))
                identityIndexCache[vertexCount] = indices;
            return true;
        }

        bool AdvancePendingClip(PendingEntryCommit pending, double budgetMilliseconds)
        {
            AERISTerrainRenderReadyHeightField result = pending.Result;
            int iterations = 0;
            while (pending.TriangleCursor + 2 < result.Triangles.Length)
            {
                int i = pending.TriangleCursor;
                SurfacePoint a = Point(result, result.Triangles[i]);
                SurfacePoint b = Point(result, result.Triangles[i + 1]);
                SurfacePoint c = Point(result, result.Triangles[i + 2]);
                AppendClippedTriangle(pending.Land, pending.ClipScratch, a, b, c, false);
                AppendClippedTriangle(pending.Water, pending.ClipScratch, a, b, c, true);
                pending.TriangleCursor += 3;
                iterations++;
                if ((iterations & 15) == 0 &&
                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >= budgetMilliseconds)
                    return false;
            }
            return true;
        }

        bool AdvancePendingSources(PendingEntryCommit pending,
            double budgetMilliseconds)
        {
            int chunkItems = Rev35R005SourceChunkHardCap;
            operationHealthRev35R005SourceHardCapWindows++;
            while (true)
            {
                int iterations = 0;
                switch (pending.PrepareSubstage)
                {
                    case 0:
                        pending.LandSource = pending.Land.Vertices.Count <= 0 ? null :
                            new Vector3[pending.Land.Vertices.Count];
                        pending.WaterSource = pending.Water.Vertices.Count <= 0 ? null :
                            new Vector3[pending.Water.Vertices.Count];
                        pending.LandElevation = new float[pending.Land.Elevation.Count];
                        pending.LandShade = new byte[pending.Land.Shade.Count];
                        pending.PrepareSubstage = 1;
                        pending.PrepareCursor = 0;
                        if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                            budgetMilliseconds)
                        {
                            operationHealthRev35PrepareSourceYields++;
                            return false;
                        }
                        break;
                    case 1:
                        if (pending.LandSource != null)
                        {
                            while (pending.PrepareCursor < pending.LandSource.Length)
                            {
                                pending.LandSource[pending.PrepareCursor] =
                                    pending.Land.Vertices[pending.PrepareCursor];
                                pending.PrepareCursor++;
                                iterations++;
                                if ((iterations % chunkItems) == 0 &&
                                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                    budgetMilliseconds)
                                {
                                    operationHealthRev35PrepareSourceYields++;
                                    return false;
                                }
                            }
                        }
                        pending.PrepareSubstage = 2;
                        pending.PrepareCursor = 0;
                        break;
                    case 2:
                        if (pending.WaterSource != null)
                        {
                            while (pending.PrepareCursor < pending.WaterSource.Length)
                            {
                                pending.WaterSource[pending.PrepareCursor] =
                                    pending.Water.Vertices[pending.PrepareCursor];
                                pending.PrepareCursor++;
                                iterations++;
                                if ((iterations % chunkItems) == 0 &&
                                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                    budgetMilliseconds)
                                {
                                    operationHealthRev35PrepareSourceYields++;
                                    return false;
                                }
                            }
                        }
                        pending.PrepareSubstage = 3;
                        pending.PrepareCursor = 0;
                        break;
                    case 3:
                        while (pending.PrepareCursor < pending.LandElevation.Length)
                        {
                            pending.LandElevation[pending.PrepareCursor] =
                                pending.Land.Elevation[pending.PrepareCursor];
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PrepareSourceYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 4;
                        pending.PrepareCursor = 0;
                        break;
                    case 4:
                        while (pending.PrepareCursor < pending.LandShade.Length)
                        {
                            pending.LandShade[pending.PrepareCursor] =
                                pending.Land.Shade[pending.PrepareCursor];
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PrepareSourceYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 5;
                        pending.PrepareCursor = 0;
                        break;
                    case 5:
                        pending.CoastalLandSource = BuildTriangleSourceVertices(
                            pending.Result.CoastalLandCorrectionVertices);
                        pending.PrepareSubstage = 6;
                        if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                            budgetMilliseconds)
                        {
                            operationHealthRev35PrepareSourceYields++;
                            return false;
                        }
                        break;
                    case 6:
                        pending.CoastalWaterSource = BuildTriangleSourceVertices(
                            pending.Result.CoastalWaterCorrectionVertices);
                        pending.PrepareSubstage = 7;
                        operationHealthSurfaceBuilderReuses++;
                        return true;
                    default:
                        return true;
                }
            }
        }

        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,
            double budgetMilliseconds)
        {
            int chunkItems = ResolveRev35R004PrepareChunkItems(budgetMilliseconds);
            operationHealthRev35R005PackedChunkMaxItems = Math.Max(
                operationHealthRev35R005PackedChunkMaxItems, chunkItems);
            Vector3[] waterSource = pending.WaterSource;
            Vector3[] landSource = pending.LandSource;
            Vector3[] coastalWaterSource = pending.CoastalWaterSource;
            Vector3[] coastalLandSource = pending.CoastalLandSource;
            Color32 waterColour = ResolveWaterColour(AERISTerrainColourPreset.Standard);
            Color32 landColour = new Color32(255, 255, 255, 255);
            while (true)
            {
                int iterations = 0;
                switch (pending.PrepareSubstage)
                {
                    case 0:
                        pending.PackedWaterCount = waterSource == null ? 0 : waterSource.Length;
                        pending.PackedLandCount = landSource == null ? 0 : landSource.Length;
                        pending.PackedCoastalWaterCount = coastalWaterSource == null ? 0 :
                            coastalWaterSource.Length;
                        pending.PackedCoastalLandCount = coastalLandSource == null ? 0 :
                            coastalLandSource.Length;
                        pending.PackedWaterOffset = 0;
                        pending.PackedLandOffset = pending.PackedWaterCount;
                        pending.PackedCoastalWaterOffset = pending.PackedLandOffset +
                            pending.PackedLandCount;
                        pending.PackedCoastalLandOffset = pending.PackedCoastalWaterOffset +
                            pending.PackedCoastalWaterCount;
                        int vertexCount = pending.PackedCoastalLandOffset +
                            pending.PackedCoastalLandCount;
                        pending.PackedSourceMeshCount =
                            (pending.PackedWaterCount > 0 ? 1 : 0) +
                            (pending.PackedLandCount > 0 ? 1 : 0) +
                            (pending.PackedCoastalWaterCount > 0 ? 1 : 0) +
                            (pending.PackedCoastalLandCount > 0 ? 1 : 0);
                        int indexCount = pending.Water.Triangles.Count +
                            pending.Land.Triangles.Count +
                            pending.PackedCoastalWaterCount +
                            pending.PackedCoastalLandCount;
                        if (vertexCount < 3 || indexCount < 3 ||
                            pending.PackedSourceMeshCount <= 0)
                            return true;
                        pending.PrepareSubstage = 1;
                        pending.PrepareCursor = 0;
                        pending.PackedIndexWriteCursor = 0;
                        break;
                    case 1:
                        {
                            int count = pending.PackedCoastalLandOffset +
                                pending.PackedCoastalLandCount;
                            long started = Stopwatch.GetTimestamp();
                            pending.PackedSource = new Vector3[count];
                            double elapsed = (Stopwatch.GetTimestamp() - started) *
                                1000.0 / Stopwatch.Frequency;
                            if (elapsed > operationHealthRev35PackedSourceAllocMaxMs)
                                operationHealthRev35PackedSourceAllocMaxMs = elapsed;
                            pending.PrepareSubstage = 2;
                            if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                            operationHealthRev35R004AllocationContinues++;
                            break;
                        }
                    case 2:
                        {
                            int count = pending.PackedCoastalLandOffset +
                                pending.PackedCoastalLandCount;
                            long started = Stopwatch.GetTimestamp();
                            pending.PackedColours =
                                AcquireRev35R006Hf4ColourBuffer(count);
                            double elapsed = (Stopwatch.GetTimestamp() - started) *
                                1000.0 / Stopwatch.Frequency;
                            if (elapsed > operationHealthRev35PackedColourAllocMaxMs)
                                operationHealthRev35PackedColourAllocMaxMs = elapsed;
                            pending.PrepareSubstage = 3;
                            if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                            operationHealthRev35R004AllocationContinues++;
                            break;
                        }
                    case 3:
                        {
                            int count = pending.Water.Triangles.Count +
                                pending.Land.Triangles.Count +
                                pending.PackedCoastalWaterCount +
                                pending.PackedCoastalLandCount;
                            long started = Stopwatch.GetTimestamp();
                            pending.PackedIndices =
                                AcquireRev35R006Hf4IndexBuffer(count);
                            double elapsed = (Stopwatch.GetTimestamp() - started) *
                                1000.0 / Stopwatch.Frequency;
                            if (elapsed > operationHealthRev35PackedIndexAllocMaxMs)
                                operationHealthRev35PackedIndexAllocMaxMs = elapsed;
                            pending.PrepareSubstage = 4;
                            if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                            operationHealthRev35R004AllocationContinues++;
                            break;
                        }
                    case 4:
                        while (pending.PrepareCursor < pending.PackedWaterCount)
                        {
                            int dst = pending.PackedWaterOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] = waterSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = waterColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 5;
                        pending.PrepareCursor = 0;
                        break;
                    case 5:
                        while (pending.PrepareCursor < pending.PackedLandCount)
                        {
                            int dst = pending.PackedLandOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] = landSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = landColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 6;
                        pending.PrepareCursor = 0;
                        break;
                    case 6:
                        while (pending.PrepareCursor < pending.PackedCoastalWaterCount)
                        {
                            int dst = pending.PackedCoastalWaterOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] =
                                coastalWaterSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = waterColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 7;
                        pending.PrepareCursor = 0;
                        break;
                    case 7:
                        while (pending.PrepareCursor < pending.PackedCoastalLandCount)
                        {
                            int dst = pending.PackedCoastalLandOffset + pending.PrepareCursor;
                            pending.PackedSource[dst] =
                                coastalLandSource[pending.PrepareCursor];
                            pending.PackedColours[dst] = landColour;
                            pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 8;
                        pending.PrepareCursor = 0;
                        break;
                    case 8:
                        while (pending.PrepareCursor < pending.Water.Triangles.Count)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedWaterOffset +
                                pending.Water.Triangles[pending.PrepareCursor++];
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 9;
                        pending.PrepareCursor = 0;
                        break;
                    case 9:
                        while (pending.PrepareCursor < pending.Land.Triangles.Count)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedLandOffset +
                                pending.Land.Triangles[pending.PrepareCursor++];
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 10;
                        pending.PrepareCursor = 0;
                        break;
                    case 10:
                        while (pending.PrepareCursor < pending.PackedCoastalWaterCount)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedCoastalWaterOffset + pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 11;
                        pending.PrepareCursor = 0;
                        break;
                    case 11:
                        while (pending.PrepareCursor < pending.PackedCoastalLandCount)
                        {
                            pending.PackedIndices[pending.PackedIndexWriteCursor++] =
                                pending.PackedCoastalLandOffset + pending.PrepareCursor++;
                            iterations++;
                            if ((iterations % chunkItems) == 0 &&
                                mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                                budgetMilliseconds)
                            {
                                operationHealthRev35PreparePackedYields++;
                                return false;
                            }
                        }
                        pending.PrepareSubstage = 12;
                        return true;
                    default:
                        return true;
                }
            }
        }

        Mesh UploadPreparedPackedTerrainMesh(string name, PendingEntryCommit pending)
        {
            if (pending.PackedSource == null || pending.PackedIndices == null ||
                pending.PackedSource.Length < 3 || pending.PackedIndices.Length < 3)
                return null;
            Mesh mesh = AcquireMesh(name, pending.PackedSource.Length);
            mesh.vertices = pending.PackedSource;
            mesh.colors32 = pending.PackedColours;
            mesh.triangles = pending.PackedIndices;
            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            operationHealthPackedTerrainBuilds++;
            return mesh;
        }

        Color32[] AcquireRev35R006Hf4ColourBuffer(int length)
        {
            if (length <= 0) return null;
            operationHealthRev35R006Hf4ColourMaxItems = Math.Max(
                operationHealthRev35R006Hf4ColourMaxItems, length);
            Stack<Color32[]> stack;
            if (rev35R006Hf4ColourPool.TryGetValue(length, out stack) &&
                stack != null && stack.Count > 0)
            {
                Color32[] buffer = stack.Pop();
                long bytes = Math.Max(0L, (long)length * 4L);
                rev35R006Hf4ColourPoolBytes = Math.Max(0L,
                    rev35R006Hf4ColourPoolBytes - bytes);
                rev35R006Hf4ColourPoolArrays = Math.Max(0,
                    rev35R006Hf4ColourPoolArrays - 1);
                if (stack.Count == 0) rev35R006Hf4ColourPool.Remove(length);
                operationHealthRev35R006Hf4ColourPoolHit++;
                return buffer;
            }
            operationHealthRev35R006Hf4ColourPoolMiss++;
            long started = Stopwatch.GetTimestamp();
            Color32[] created = new Color32[length];
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency;
            operationHealthRev35R006Hf4ColourNewAllocMaxMs = Math.Max(
                operationHealthRev35R006Hf4ColourNewAllocMaxMs, elapsed);
            return created;
        }

        void RecycleRev35R006Hf4ColourBuffer(ref Color32[] buffer)
        {
            if (buffer == null || buffer.Length <= 0)
            {
                buffer = null;
                return;
            }
            long bytes = Math.Max(0L, (long)buffer.Length * 4L);
            if (rev35R006Hf4ColourPoolArrays >=
                    Rev35R006Hf4ColourPoolMaximumArrays ||
                bytes > Rev35R006Hf4ColourPoolMaximumBytes ||
                rev35R006Hf4ColourPoolBytes + bytes >
                    Rev35R006Hf4ColourPoolMaximumBytes)
            {
                operationHealthRev35R006Hf4ColourPoolReject++;
                buffer = null;
                return;
            }
            Stack<Color32[]> stack;
            if (!rev35R006Hf4ColourPool.TryGetValue(buffer.Length, out stack) ||
                stack == null)
            {
                stack = new Stack<Color32[]>();
                rev35R006Hf4ColourPool[buffer.Length] = stack;
            }
            stack.Push(buffer);
            rev35R006Hf4ColourPoolBytes += bytes;
            rev35R006Hf4ColourPoolArrays++;
            operationHealthRev35R006Hf4ColourPoolRecycle++;
            buffer = null;
        }

        int[] AcquireRev35R006Hf4IndexBuffer(int length)
        {
            if (length <= 0) return null;
            operationHealthRev35R006Hf4IndexMaxItems = Math.Max(
                operationHealthRev35R006Hf4IndexMaxItems, length);
            Stack<int[]> stack;
            if (rev35R006Hf4IndexPool.TryGetValue(length, out stack) &&
                stack != null && stack.Count > 0)
            {
                int[] buffer = stack.Pop();
                long bytes = Math.Max(0L, (long)length * 4L);
                rev35R006Hf4IndexPoolBytes = Math.Max(0L,
                    rev35R006Hf4IndexPoolBytes - bytes);
                rev35R006Hf4IndexPoolArrays = Math.Max(0,
                    rev35R006Hf4IndexPoolArrays - 1);
                if (stack.Count == 0) rev35R006Hf4IndexPool.Remove(length);
                operationHealthRev35R006Hf4IndexPoolHit++;
                return buffer;
            }
            operationHealthRev35R006Hf4IndexPoolMiss++;
            long started = Stopwatch.GetTimestamp();
            int[] created = new int[length];
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency;
            operationHealthRev35R006Hf4IndexNewAllocMaxMs = Math.Max(
                operationHealthRev35R006Hf4IndexNewAllocMaxMs, elapsed);
            return created;
        }

        void RecycleRev35R006Hf4IndexBuffer(ref int[] buffer)
        {
            if (buffer == null || buffer.Length <= 0)
            {
                buffer = null;
                return;
            }
            long bytes = Math.Max(0L, (long)buffer.Length * 4L);
            if (rev35R006Hf4IndexPoolArrays >=
                    Rev35R006Hf4IndexPoolMaximumArrays ||
                bytes > Rev35R006Hf4IndexPoolMaximumBytes ||
                rev35R006Hf4IndexPoolBytes + bytes >
                    Rev35R006Hf4IndexPoolMaximumBytes)
            {
                operationHealthRev35R006Hf4IndexPoolReject++;
                buffer = null;
                return;
            }
            Stack<int[]> stack;
            if (!rev35R006Hf4IndexPool.TryGetValue(buffer.Length, out stack) ||
                stack == null)
            {
                stack = new Stack<int[]>();
                rev35R006Hf4IndexPool[buffer.Length] = stack;
            }
            stack.Push(buffer);
            rev35R006Hf4IndexPoolBytes += bytes;
            rev35R006Hf4IndexPoolArrays++;
            operationHealthRev35R006Hf4IndexPoolRecycle++;
            buffer = null;
        }

        void RecycleRev35R006Hf4EntryPackedBuffers(Entry entry)
        {
            if (entry == null) return;
            RecycleRev35R006Hf4ColourBuffer(ref entry.PackedTerrainColours);
        }

        void ClearRev35R006Hf4PackedPools()
        {
            rev35R006Hf4ColourPool.Clear();
            rev35R006Hf4IndexPool.Clear();
            rev35R006Hf4ColourPoolBytes = 0L;
            rev35R006Hf4ColourPoolArrays = 0;
            rev35R006Hf4IndexPoolBytes = 0L;
            rev35R006Hf4IndexPoolArrays = 0;
        }

        GeographicUnitPoint[] AcquireRev35R006GeographicBuffer(int length)
        {
            if (length <= 0) return null;
            operationHealthRev35R006GeoMaxItems = Math.Max(
                operationHealthRev35R006GeoMaxItems, length);
            Stack<GeographicUnitPoint[]> stack;
            if (rev35R006GeographicPool.TryGetValue(length, out stack) &&
                stack != null && stack.Count > 0)
            {
                GeographicUnitPoint[] buffer = stack.Pop();
                long bytes = Math.Max(0L, (long)length * 24L);
                rev35R006GeographicPoolBytes = Math.Max(0L,
                    rev35R006GeographicPoolBytes - bytes);
                rev35R006GeographicPoolArrays = Math.Max(0,
                    rev35R006GeographicPoolArrays - 1);
                if (stack.Count == 0) rev35R006GeographicPool.Remove(length);
                operationHealthRev35R006GeoPoolHit++;
                return buffer;
            }
            operationHealthRev35R006GeoPoolMiss++;
            long startTicks = Stopwatch.GetTimestamp();
            GeographicUnitPoint[] created = new GeographicUnitPoint[length];
            double elapsed = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 /
                Stopwatch.Frequency;
            operationHealthRev35R006GeoAllocationMaxMs = Math.Max(
                operationHealthRev35R006GeoAllocationMaxMs, elapsed);
            return created;
        }

        void RecycleRev35R006GeographicBuffer(ref GeographicUnitPoint[] buffer)
        {
            if (buffer == null || buffer.Length <= 0)
            {
                buffer = null;
                return;
            }
            long bytes = Math.Max(0L, (long)buffer.Length * 24L);
            if (rev35R006GeographicPoolArrays >= Rev35R006GeographicPoolMaximumArrays ||
                bytes > Rev35R006GeographicPoolMaximumBytes ||
                rev35R006GeographicPoolBytes + bytes >
                    Rev35R006GeographicPoolMaximumBytes)
            {
                operationHealthRev35R006GeoPoolReject++;
                buffer = null;
                return;
            }
            Stack<GeographicUnitPoint[]> stack;
            if (!rev35R006GeographicPool.TryGetValue(buffer.Length, out stack) ||
                stack == null)
            {
                stack = new Stack<GeographicUnitPoint[]>();
                rev35R006GeographicPool[buffer.Length] = stack;
            }
            stack.Push(buffer);
            rev35R006GeographicPoolBytes += bytes;
            rev35R006GeographicPoolArrays++;
            operationHealthRev35R006GeoPoolRecycle++;
            buffer = null;
        }

        void RecycleRev35R006EntryGeographic(Entry entry)
        {
            if (entry == null) return;
            RecycleRev35R006GeographicBuffer(
                ref entry.PackedTerrainGeographicPoints);
            RecycleRev35R006GeographicBuffer(ref entry.ContourGeographicPoints);
            RecycleRev35R006GeographicBuffer(ref entry.CoastlineGeographicPoints);
        }

        void ClearRev35R006GeographicPool()
        {
            rev35R006GeographicPool.Clear();
            rev35R006GeographicPoolBytes = 0L;
            rev35R006GeographicPoolArrays = 0;
        }

        double CurrentRev35R006FinalizeWaitMilliseconds()
        {
            if (pendingEntryCommit == null ||
                pendingEntryCommit.Stage != PendingEntryCommitStage.Finalize ||
                pendingEntryCommit.Rev35R006FinalizeReadyTicks <= 0L) return 0.0;
            return Math.Max(0.0,
                (Stopwatch.GetTimestamp() -
                    pendingEntryCommit.Rev35R006FinalizeReadyTicks) * 1000.0 /
                    Stopwatch.Frequency);
        }

        static bool Rev35R006ContourOnlyStyleDifference(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) ||
                string.Equals(left, right, StringComparison.Ordinal)) return false;
            int l1 = left.IndexOf('|'), r1 = right.IndexOf('|');
            int l2 = left.LastIndexOf('|'), r2 = right.LastIndexOf('|');
            if (l1 <= 0 || r1 <= 0 || l2 <= l1 || r2 <= r1) return false;
            if (l1 != r1 || left.Length - l2 != right.Length - r2) return false;
            if (string.CompareOrdinal(left, 0, right, 0, l1) != 0) return false;
            int suffixLength = left.Length - l2 - 1;
            return string.CompareOrdinal(left, l2 + 1, right, r2 + 1,
                suffixLength) == 0;
        }

        void ObserveRev35R006FoundationCriticalPath(
            AERISTerrainVisibleTileSet visible, AERISTerrainHeightTile[] tiles,
            Entry[] currentEntries, Entry[] fallbackEntries, string styleKey,
            int readyGlobal, int readyFar)
        {
            int missing = 0, partial = 0, pending = 0, renderReady = 0, upstream = 0;
            int contourOnlyFallback = 0;
            if (visible != null && tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null || tile.Key.Lod != AERISTerrainTileLod.Far)
                        continue;
                    Entry current = currentEntries != null && i < currentEntries.Length ?
                        currentEntries[i] : null;
                    if (current != null && current.CoverageFraction >= 0.999f)
                        continue;
                    missing++;
                    if (current != null)
                    {
                        partial++;
                        continue;
                    }
                    string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                    if (pendingEntryCommit != null &&
                        string.Equals(pendingEntryCommit.CacheKey, cacheKey,
                            StringComparison.Ordinal))
                        pending++;
                    else if (renderReadyFields.ContainsKey(cacheKey))
                        renderReady++;
                    else
                        upstream++;
                    Entry fallback = fallbackEntries != null &&
                        i < fallbackEntries.Length ? fallbackEntries[i] : null;
                    if (fallback != null &&
                        Rev35R006ContourOnlyStyleDifference(fallback.StyleKey, styleKey))
                        contourOnlyFallback++;
                }
            }
            operationHealthRev35R006FoundationMissingFar = missing;
            operationHealthRev35R006FoundationMissingPartial = partial;
            operationHealthRev35R006FoundationMissingPending = pending;
            operationHealthRev35R006FoundationMissingRenderReady = renderReady;
            operationHealthRev35R006FoundationMissingUpstream = upstream;
            operationHealthRev35R006ContourOnlyFallback = contourOnlyFallback;
            bool sourceIncomplete = visible != null && !visible.FoundationComplete;
            operationHealthRev35R006FoundationSourceIncomplete =
                sourceIncomplete ? 1 : 0;

            bool waiting = visible != null &&
                (sourceIncomplete || readyFar < visible.FarFoundationCount || missing > 0);
            float now = Time.realtimeSinceStartup;
            if (!waiting)
            {
                operationHealthRev35R006FoundationWaitSince = -1f;
                operationHealthRev35R006FoundationWaitThresholdMask = 0;
                operationHealthRev35R006FoundationWaitCurrentMs = 0.0;
                return;
            }
            if (operationHealthRev35R006FoundationWaitSince < 0f)
                operationHealthRev35R006FoundationWaitSince = now;
            double elapsed = Math.Max(0.0,
                (now - operationHealthRev35R006FoundationWaitSince) * 1000.0);
            operationHealthRev35R006FoundationWaitCurrentMs = elapsed;
            operationHealthRev35R006FoundationWaitMaxMs = Math.Max(
                operationHealthRev35R006FoundationWaitMaxMs, elapsed);
            int mask = operationHealthRev35R006FoundationWaitThresholdMask;
            if (elapsed >= 500.0 && (mask & 1) == 0)
            {
                operationHealthRev35R006FoundationWait500++; mask |= 1;
            }
            if (elapsed >= 1000.0 && (mask & 2) == 0)
            {
                operationHealthRev35R006FoundationWait1000++; mask |= 2;
            }
            if (elapsed >= 2000.0 && (mask & 4) == 0)
            {
                operationHealthRev35R006FoundationWait2000++; mask |= 4;
            }
            if (elapsed >= 3000.0 && (mask & 8) == 0)
            {
                operationHealthRev35R006FoundationWait3000++; mask |= 8;
            }
            if (elapsed >= 5000.0 && (mask & 16) == 0)
            {
                operationHealthRev35R006FoundationWait5000++; mask |= 16;
            }
            operationHealthRev35R006FoundationWaitThresholdMask = mask;
        }

        bool AdvancePendingGeographic(Vector3[] source,
            ref GeographicUnitPoint[] output, ref int cursor,
            AERISTerrainRenderReadyHeightField result, double budgetMilliseconds)
        {
            if (source == null || source.Length == 0)
            {
                output = null;
                cursor = 0;
                return true;
            }
            if (output == null || output.Length != source.Length)
            {
                if (output != null)
                    RecycleRev35R006GeographicBuffer(ref output);
                output = AcquireRev35R006GeographicBuffer(source.Length);
                if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                    budgetMilliseconds)
                    return false;
            }
            double latitudeSpan = result.NorthLatitudeDeg - result.SouthLatitudeDeg;
            double longitudeSpan = PositiveLongitudeSpan(result.WestLongitudeDeg,
                result.EastLongitudeDeg);
            int iterations = 0;
            while (cursor < source.Length)
            {
                double latitudeDeg = result.SouthLatitudeDeg + latitudeSpan * source[cursor].y;
                double longitudeDeg = NormalizeLongitude(result.WestLongitudeDeg +
                    longitudeSpan * source[cursor].x);
                double latitudeRad = latitudeDeg * Math.PI / 180.0;
                double longitudeRad = longitudeDeg * Math.PI / 180.0;
                double cosineLatitude = Math.Cos(latitudeRad);
                output[cursor] = new GeographicUnitPoint
                {
                    X = cosineLatitude * Math.Cos(longitudeRad),
                    Y = cosineLatitude * Math.Sin(longitudeRad),
                    Z = Math.Sin(latitudeRad)
                };
                cursor++;
                iterations++;
                if ((iterations & 31) == 0 &&
                    mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >= budgetMilliseconds)
                    return false;
            }
            return true;
        }

        bool FinalizePendingEntryCommit(PendingEntryCommit pending,
            AERISTerrainTileSystem system)
        {
            AERISTerrainRenderReadyHeightField result = pending.Result;
            if (pending.Rev35R006FinalizeReadyTicks > 0L)
            {
                double finalizeWait = Math.Max(0.0,
                    (Stopwatch.GetTimestamp() - pending.Rev35R006FinalizeReadyTicks) *
                    1000.0 / Stopwatch.Frequency);
                operationHealthRev35R006FinalizeWaitSamples++;
                operationHealthRev35R006FinalizeWaitMaxMs = Math.Max(
                    operationHealthRev35R006FinalizeWaitMaxMs, finalizeWait);
            }
            if (pending.PackedMesh == null)
            {
                operationHealthNonRenderableEntryRejects++;
                RemoveRenderReadyField(pending.CacheKey, result);
                CancelPendingEntryCommit();
                return false;
            }
            long projectedVertexBytes = (long)(
                (pending.PackedSource == null ? 0 : pending.PackedSource.Length) +
                (pending.ContourSource == null ? 0 : pending.ContourSource.Length) +
                (pending.CoastlineSource == null ? 0 : pending.CoastlineSource.Length)) *
                (3L * 8L + 3L * 4L);
            long bytes = result.Valid.Length + projectedVertexBytes +
                (pending.PackedSource == null ? 0L :
                    pending.PackedSource.LongLength * (3L * 4L + 4L)) +
                (pending.PackedIndices == null ? 0L : pending.PackedIndices.LongLength * 4L) +
                (pending.PackedSource == null ? 0L :
                    pending.PackedSource.LongLength * (3L * 4L)) +
                pending.Land.Vertices.Count * (4L + 1L) +
                (result.CoastalLandCorrectionElevationMeters == null ? 0L :
                    result.CoastalLandCorrectionElevationMeters.LongLength * (4L + 1L));
            if (result.ContourSegments != null) bytes += result.ContourSegments.Length * 4L;
            if (result.CoastlineSegments != null) bytes += result.CoastlineSegments.Length * 4L;
            double boundCenterLatitudeDeg, boundCenterLongitudeDeg, boundAngularRadiusRad;
            ResolveConservativeEntryBounds(result.SouthLatitudeDeg,
                result.NorthLatitudeDeg, result.WestLongitudeDeg,
                result.EastLongitudeDeg, out boundCenterLatitudeDeg,
                out boundCenterLongitudeDeg, out boundAngularRadiusRad);
            double boundCenterX = 0.0, boundCenterY = 0.0, boundCenterZ = 0.0;
            double boundRadiusSin = 0.0, boundRadiusCos = -1.0;
            ResolveSphericalCapFastData(boundCenterLatitudeDeg,
                boundCenterLongitudeDeg, boundAngularRadiusRad,
                out boundCenterX, out boundCenterY, out boundCenterZ,
                out boundRadiusSin, out boundRadiusCos);
            Entry entry = new Entry
            {
                CacheKey = pending.CacheKey,
                TileKey = result.Key,
                TileCreatedUtcTicks = result.TileCreatedUtcTicks,
                StyleKey = result.StyleKey,
                PackedTerrainMesh = pending.PackedMesh,
                PackedTerrainGeographicPoints = pending.PackedGeographic,
                PackedTerrainProjectedVertices = pending.PackedSource,
                PackedTerrainColours = pending.PackedColours,
                PackedWaterOffset = pending.PackedWaterOffset,
                PackedWaterCount = pending.PackedWaterCount,
                PackedLandOffset = pending.PackedLandOffset,
                PackedLandCount = pending.PackedLandCount,
                PackedCoastalWaterOffset = pending.PackedCoastalWaterOffset,
                PackedCoastalWaterCount = pending.PackedCoastalWaterCount,
                PackedCoastalLandOffset = pending.PackedCoastalLandOffset,
                PackedCoastalLandCount = pending.PackedCoastalLandCount,
                PackedTerrainSourceMeshCount = pending.PackedSourceMeshCount,
                ContourMesh = pending.ContourMesh,
                CoastlineMesh = pending.CoastlineMesh,
                ContourGeographicPoints = pending.ContourGeographic,
                CoastlineGeographicPoints = pending.CoastlineGeographic,
                ContourProjectedVertices = pending.ContourSource,
                CoastlineProjectedVertices = pending.CoastlineSource,
                SouthLatitudeDeg = result.SouthLatitudeDeg,
                NorthLatitudeDeg = result.NorthLatitudeDeg,
                WestLongitudeDeg = result.WestLongitudeDeg,
                EastLongitudeDeg = result.EastLongitudeDeg,
                BoundCenterLatitudeDeg = boundCenterLatitudeDeg,
                BoundCenterLongitudeDeg = boundCenterLongitudeDeg,
                BoundAngularRadiusRad = boundAngularRadiusRad,
                BoundCenterX = boundCenterX,
                BoundCenterY = boundCenterY,
                BoundCenterZ = boundCenterZ,
                BoundRadiusSin = boundRadiusSin,
                BoundRadiusCos = boundRadiusCos,
                LandElevationMeters = pending.LandElevation,
                LandShade = pending.LandShade,
                CoastalLandCorrectionElevationMeters =
                    result.CoastalLandCorrectionElevationMeters == null ? null :
                    (float[])result.CoastalLandCorrectionElevationMeters.Clone(),
                CoastalLandCorrectionShade = result.CoastalLandCorrectionShade == null ? null :
                    (byte[])result.CoastalLandCorrectionShade.Clone(),
                Resolution = result.Resolution,
                CoastlineResolution = result.CoastlineResolution,
                CoastalCorrectionParentCells = result.CoastalCorrectionParentCells,
                Valid = (byte[])result.Valid.Clone(),
                WaterColourPreset = AERISTerrainColourPreset.Standard,
                CoverageFraction = TriangleCoverage(result),
                Bytes = Math.Max(1L, bytes),
                LastUse = 0L
            };
            CaptureAndMarkRenderReady(result, system);
            Entry old;
            if (entries.TryGetValue(pending.CacheKey, out old))
                DetachEntryForDeferredRetirement(old);
            if (entry.CoverageFraction >= 0.999f)
                RemoveSupersededEntries(result.Key, pending.CacheKey);
            AddEntry(entry);
            usedEntryBytes += entry.Bytes;
            if (entry.CoastlineResolution >=
                AERISTerrainCoastlineExtractor.HighDensityResolution)
                highDensityCoastlineEntries++;
            if (entry.CoastalCorrectionParentCells > 0)
            {
                sparseCoastalCorrectionEntries++;
                sparseCoastalCorrectionParentCells += entry.CoastalCorrectionParentCells;
            }
            uploaded++;
            MarkGpuContentDirty();
            rev35R014PublicationSerial++;
            operationHealthRev35R014PublicationEvents++;
            MarkGpuReady(result);
            if (performance != null)
                performance.RecordGpuMeshPreparation(result.MeshMilliseconds,
                    result.ContourMilliseconds, rasterizer.PendingCount > 0);
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.RecordNavigationDisplayTextureUpload(
                    pending.AccumulatedMilliseconds);
            operationHealthRev35R006ProjectedOwnershipTransfers += 3;
            // Entry now owns PackedColours for dynamic REL/TOPO recolouring.
            operationHealthRev35R006Hf4ColourOwnershipTransfer++;
            pending.PackedColours = null;
            // Unity has copied triangle indices and Finalize has finished accounting.
            RecycleRev35R006Hf4IndexBuffer(ref pending.PackedIndices);
            pending.PackedMesh = null;
            pending.ContourMesh = null;
            pending.CoastlineMesh = null;
            return true;
        }

        void CancelPendingEntryCommit()
        {
            PendingEntryCommit pending = pendingEntryCommit;
            if (pending == null) return;
            RecycleMesh(ref pending.PackedMesh);
            RecycleMesh(ref pending.ContourMesh);
            RecycleMesh(ref pending.CoastlineMesh);
            RecycleRev35R006Hf4ColourBuffer(ref pending.PackedColours);
            RecycleRev35R006Hf4IndexBuffer(ref pending.PackedIndices);
            RecycleRev35R006GeographicBuffer(ref pending.PackedGeographic);
            RecycleRev35R006GeographicBuffer(ref pending.ContourGeographic);
            RecycleRev35R006GeographicBuffer(ref pending.CoastlineGeographic);
            pendingEntryCommit = null;
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

        void ResetRev35R007FoundationQueue()
        {
            if (rev35R007FoundationQueue.Count > 0 ||
                rev35R007FoundationQueued.Count > 0)
                operationHealthRev35R007QueueResets++;
            rev35R007FoundationQueue.Clear();
            rev35R019VisibleFoundationQueue.Clear();
            rev35R019VisibleFarKeys.Clear();
            operationHealthRev35R019VisibleKeyCount = 0;
            rev35R007FoundationQueued.Clear();
        }

        void QueueRev35R007FoundationField(AERISTerrainHeightTile tile,
            string cacheKey)
        {
            if (tile == null || tile.Key.Lod != AERISTerrainTileLod.Far ||
                string.IsNullOrEmpty(cacheKey)) return;
            // Only the latest exact requested viewport may enter this handoff queue.
            if (!requested.Contains(cacheKey) || entries.ContainsKey(cacheKey)) return;
            if (rev35R007FoundationQueued.Contains(cacheKey))
            {
                operationHealthRev35R007DuplicateSkips++;
                return;
            }
            int combinedQueueCount = rev35R007FoundationQueue.Count +
                rev35R019VisibleFoundationQueue.Count;
            if (combinedQueueCount >= Rev35R007FoundationQueueMaximum)
            {
                operationHealthRev35R007Overflow++;
                return;
            }
            rev35R007FoundationQueued.Add(cacheKey);
            if (rev35R019VisibleFarKeys.Contains(tile.Key))
            {
                rev35R019VisibleFoundationQueue.Enqueue(cacheKey);
                operationHealthRev35R019VisiblePriorityQueued++;
                operationHealthRev35R019VisiblePriorityQueuePeak = Math.Max(
                    operationHealthRev35R019VisiblePriorityQueuePeak,
                    rev35R019VisibleFoundationQueue.Count);
            }
            else
            {
                rev35R007FoundationQueue.Enqueue(cacheKey);
            }
            operationHealthRev35R007Queued++;
            operationHealthRev35R007QueuePeak = Math.Max(
                operationHealthRev35R007QueuePeak,
                combinedQueueCount + 1);
        }

        bool TryBeginRev35R019VisibleFoundationCommit()
        {
            while (rev35R019VisibleFoundationQueue.Count > 0)
            {
                bool bypassingHidden = rev35R007FoundationQueue.Count > 0;
                string cacheKey = rev35R019VisibleFoundationQueue.Dequeue();
                rev35R007FoundationQueued.Remove(cacheKey);
                if (!contentSnapshotValid || !requested.Contains(cacheKey))
                {
                    operationHealthRev35R007StaleSkips++;
                    continue;
                }
                if (entries.ContainsKey(cacheKey))
                {
                    operationHealthRev35R007AlreadyCommittedSkips++;
                    continue;
                }
                AERISTerrainRenderReadyHeightField field;
                if (!renderReadyFields.TryGetValue(cacheKey, out field) || field == null)
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                if (!TryBeginPendingEntryCommit(field))
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                operationHealthRev35R019VisiblePriorityBegins++;
                if (bypassingHidden)
                    operationHealthRev35R019HiddenQueueBypassed++;
                return true;
            }
            return false;
        }

        bool TryBeginRev35R007QueuedFoundationCommit()
        {
            while (rev35R007FoundationQueue.Count > 0)
            {
                string cacheKey = rev35R007FoundationQueue.Dequeue();
                rev35R007FoundationQueued.Remove(cacheKey);
                // R003 remains authoritative: a rotated/translated viewport can make a
                // queued cache key obsolete before it reaches the single commit lane.
                if (!contentSnapshotValid || !requested.Contains(cacheKey))
                {
                    operationHealthRev35R007StaleSkips++;
                    continue;
                }
                if (entries.ContainsKey(cacheKey))
                {
                    operationHealthRev35R007AlreadyCommittedSkips++;
                    continue;
                }
                AERISTerrainRenderReadyHeightField field;
                if (!renderReadyFields.TryGetValue(cacheKey, out field) || field == null)
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                if (!TryBeginPendingEntryCommit(field))
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                operationHealthRev35R007ChainedBegins++;
                return true;
            }
            return false;
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
            // Phase6_002: a render-ready RAM field is already the expensive worker product.
            // Queue it into the same resumable main-thread commit path and suppress a
            // duplicate raster worker request while the last complete Entry remains visible.
            if (pendingEntryCommit == null)
            {
                if (TryBeginPendingEntryCommit(field))
                    operationHealthRev35R007ImmediateBegins++;
            }
            else
            {
                QueueRev35R007FoundationField(tile, cacheKey);
            }
            return true;
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
                DetachEntryForDeferredRetirement(supersededScratch[i]);
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

            Vector3[] contourSource, coastlineSource;
            Vector3[] landSource = land.Vertices.Count <= 0 ? null : land.Vertices.ToArray();
            Vector3[] waterSource = water.Vertices.Count <= 0 ? null : water.Vertices.ToArray();
            Vector3[] coastalLandCorrectionSource = BuildTriangleSourceVertices(
                result.CoastalLandCorrectionVertices);
            Vector3[] coastalWaterCorrectionSource = BuildTriangleSourceVertices(
                result.CoastalWaterCorrectionVertices);
            Vector3[] packedTerrainSource;
            Color32[] packedTerrainColours;
            int packedWaterOffset, packedWaterCount;
            int packedLandOffset, packedLandCount;
            int packedCoastalWaterOffset, packedCoastalWaterCount;
            int packedCoastalLandOffset, packedCoastalLandCount;
            int packedTerrainSourceMeshCount, packedTerrainIndexCount;
            Mesh packedTerrainMesh = BuildPackedTerrainMesh(
                "AERIS_TERRAIN_PACKED_" + result.Key.FileStem,
                waterSource, water.Triangles, landSource, land.Triangles,
                coastalWaterCorrectionSource, coastalLandCorrectionSource,
                out packedTerrainSource, out packedTerrainColours,
                out packedWaterOffset, out packedWaterCount,
                out packedLandOffset, out packedLandCount,
                out packedCoastalWaterOffset, out packedCoastalWaterCount,
                out packedCoastalLandOffset, out packedCoastalLandCount,
                out packedTerrainSourceMeshCount, out packedTerrainIndexCount);
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
            long projectedVertexBytes = (long)(
                (packedTerrainSource == null ? 0 : packedTerrainSource.Length) +
                (contourSource == null ? 0 : contourSource.Length) +
                (coastlineSource == null ? 0 : coastlineSource.Length)) *
                (3L * 8L + 3L * 4L);
            long bytes = result.Valid.Length + projectedVertexBytes +
                (packedTerrainSource == null ? 0L :
                    packedTerrainSource.LongLength * (3L * 4L + 4L)) +
                packedTerrainIndexCount * 4L +
                (packedTerrainSource == null ? 0L :
                    packedTerrainSource.LongLength * (3L * 4L)) +
                land.Vertices.Count * (4L + 1L) +
                (result.CoastalLandCorrectionElevationMeters == null ? 0L :
                    result.CoastalLandCorrectionElevationMeters.LongLength * (4L + 1L));
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
            double boundCenterX = 0.0, boundCenterY = 0.0, boundCenterZ = 0.0;
            double boundRadiusSin = 0.0, boundRadiusCos = -1.0;
            ResolveSphericalCapFastData(boundCenterLatitudeDeg,
                boundCenterLongitudeDeg, boundAngularRadiusRad,
                out boundCenterX, out boundCenterY, out boundCenterZ,
                out boundRadiusSin, out boundRadiusCos);
            return new Entry
            {
                CacheKey = cacheKey,
                TileKey = result.Key,
                TileCreatedUtcTicks = result.TileCreatedUtcTicks,
                StyleKey = result.StyleKey,
                PackedTerrainMesh = packedTerrainMesh,
                PackedTerrainGeographicPoints = BuildGeographicPoints(packedTerrainSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                PackedTerrainProjectedVertices = AllocateProjectedVertices(packedTerrainSource),
                PackedTerrainColours = packedTerrainColours,
                PackedWaterOffset = packedWaterOffset,
                PackedWaterCount = packedWaterCount,
                PackedLandOffset = packedLandOffset,
                PackedLandCount = packedLandCount,
                PackedCoastalWaterOffset = packedCoastalWaterOffset,
                PackedCoastalWaterCount = packedCoastalWaterCount,
                PackedCoastalLandOffset = packedCoastalLandOffset,
                PackedCoastalLandCount = packedCoastalLandCount,
                PackedTerrainSourceMeshCount = packedTerrainSourceMeshCount,
                ContourMesh = contourMesh,
                CoastlineMesh = coastlineMesh,
                ContourGeographicPoints = BuildGeographicPoints(contourSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                CoastlineGeographicPoints = BuildGeographicPoints(coastlineSource,
                    result.SouthLatitudeDeg, result.NorthLatitudeDeg,
                    result.WestLongitudeDeg, result.EastLongitudeDeg),
                ContourProjectedVertices = AllocateProjectedVertices(contourSource),
                CoastlineProjectedVertices = AllocateProjectedVertices(coastlineSource),
                SouthLatitudeDeg = result.SouthLatitudeDeg,
                NorthLatitudeDeg = result.NorthLatitudeDeg,
                WestLongitudeDeg = result.WestLongitudeDeg,
                EastLongitudeDeg = result.EastLongitudeDeg,
                BoundCenterLatitudeDeg = boundCenterLatitudeDeg,
                BoundCenterLongitudeDeg = boundCenterLongitudeDeg,
                BoundAngularRadiusRad = boundAngularRadiusRad,
                BoundCenterX = boundCenterX,
                BoundCenterY = boundCenterY,
                BoundCenterZ = boundCenterZ,
                BoundRadiusSin = boundRadiusSin,
                BoundRadiusCos = boundRadiusCos,
                LandElevationMeters = land.Elevation.ToArray(),
                LandShade = land.Shade.ToArray(),
                CoastalLandCorrectionElevationMeters =
                    result.CoastalLandCorrectionElevationMeters == null ? null :
                    (float[])result.CoastalLandCorrectionElevationMeters.Clone(),
                CoastalLandCorrectionShade =
                    result.CoastalLandCorrectionShade == null ? null :
                    (byte[])result.CoastalLandCorrectionShade.Clone(),
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

        static Vector3[] BuildTriangleSourceVertices(float[] xy)
        {
            if (xy == null || xy.Length < 6 || (xy.Length & 1) != 0 ||
                (xy.Length / 2) % 3 != 0) return null;
            int count = xy.Length / 2;
            var output = new Vector3[count];
            for (int i = 0; i < count; i++)
                output[i] = new Vector3(xy[i * 2], xy[i * 2 + 1], 0f);
            return output;
        }

        Mesh BuildPackedTerrainMesh(string name,
            Vector3[] waterSource, List<int> waterTriangles,
            Vector3[] landSource, List<int> landTriangles,
            Vector3[] coastalWaterSource, Vector3[] coastalLandSource,
            out Vector3[] packedSource, out Color32[] packedColours,
            out int waterOffset, out int waterCount,
            out int landOffset, out int landCount,
            out int coastalWaterOffset, out int coastalWaterCount,
            out int coastalLandOffset, out int coastalLandCount,
            out int sourceMeshCount, out int packedIndexCount)
        {
            waterCount = waterSource == null ? 0 : waterSource.Length;
            landCount = landSource == null ? 0 : landSource.Length;
            coastalWaterCount = coastalWaterSource == null ? 0 : coastalWaterSource.Length;
            coastalLandCount = coastalLandSource == null ? 0 : coastalLandSource.Length;
            waterOffset = 0;
            landOffset = waterOffset + waterCount;
            coastalWaterOffset = landOffset + landCount;
            coastalLandOffset = coastalWaterOffset + coastalWaterCount;
            int vertexCount = coastalLandOffset + coastalLandCount;
            sourceMeshCount = (waterCount > 0 ? 1 : 0) + (landCount > 0 ? 1 : 0) +
                (coastalWaterCount > 0 ? 1 : 0) + (coastalLandCount > 0 ? 1 : 0);
            int waterIndexCount = waterTriangles == null ? 0 : waterTriangles.Count;
            int landIndexCount = landTriangles == null ? 0 : landTriangles.Count;
            packedIndexCount = waterIndexCount + landIndexCount +
                coastalWaterCount + coastalLandCount;
            packedSource = null;
            packedColours = null;
            if (vertexCount < 3 || packedIndexCount < 3 || sourceMeshCount <= 0)
                return null;

            packedSource = new Vector3[vertexCount];
            packedColours = new Color32[vertexCount];
            int[] indices = new int[packedIndexCount];
            Color32 waterColour = ResolveWaterColour(AERISTerrainColourPreset.Standard);
            Color32 landColour = new Color32(255, 255, 255, 255);

            if (waterCount > 0) Array.Copy(waterSource, 0, packedSource, waterOffset, waterCount);
            if (landCount > 0) Array.Copy(landSource, 0, packedSource, landOffset, landCount);
            if (coastalWaterCount > 0)
                Array.Copy(coastalWaterSource, 0, packedSource,
                    coastalWaterOffset, coastalWaterCount);
            if (coastalLandCount > 0)
                Array.Copy(coastalLandSource, 0, packedSource,
                    coastalLandOffset, coastalLandCount);
            for (int i = 0; i < waterCount; i++) packedColours[waterOffset + i] = waterColour;
            for (int i = 0; i < landCount; i++) packedColours[landOffset + i] = landColour;
            for (int i = 0; i < coastalWaterCount; i++)
                packedColours[coastalWaterOffset + i] = waterColour;
            for (int i = 0; i < coastalLandCount; i++)
                packedColours[coastalLandOffset + i] = landColour;

            int index = 0;
            if (waterTriangles != null)
                for (int i = 0; i < waterTriangles.Count; i++)
                    indices[index++] = waterOffset + waterTriangles[i];
            if (landTriangles != null)
                for (int i = 0; i < landTriangles.Count; i++)
                    indices[index++] = landOffset + landTriangles[i];
            for (int i = 0; i < coastalWaterCount; i++)
                indices[index++] = coastalWaterOffset + i;
            for (int i = 0; i < coastalLandCount; i++)
                indices[index++] = coastalLandOffset + i;

            Mesh mesh = AcquireMesh(name, vertexCount);
            mesh.vertices = packedSource;
            mesh.colors32 = packedColours;
            // Primitive order intentionally matches the accepted four draw calls:
            // base water, base land, sparse coastal water, sparse coastal land.
            mesh.triangles = indices;
            mesh.bounds = NdPresentationBounds;
            mesh.UploadMeshData(false);
            operationHealthPackedTerrainBuilds++;
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

        bool EnsureGpuDynamicTerrainColourAttributes(Entry entry)
        {
            if (entry == null || entry.PackedTerrainMesh == null || entry.GpuDynamicColourRejected) return false;
            if (entry.GpuDynamicColourAttributesReady) return true;
            try
            {
                int vertexCount = entry.PackedTerrainMesh.vertexCount;
                if (vertexCount <= 0 || entry.PackedTerrainColours == null ||
                    entry.PackedTerrainColours.Length != vertexCount)
                    throw new InvalidOperationException("packed terrain semantic vertex mismatch");
                gpuDynamicTerrainSemanticScratch.Clear();
                if (gpuDynamicTerrainSemanticScratch.Capacity < vertexCount)
                    gpuDynamicTerrainSemanticScratch.Capacity = vertexCount;
                for (int i = 0; i < vertexCount; i++)
                    gpuDynamicTerrainSemanticScratch.Add(new Vector3(0f, 255f, 0f));
                int landCount = Math.Min(entry.PackedLandCount,
                    entry.LandElevationMeters == null ? 0 : entry.LandElevationMeters.Length);
                landCount = Math.Min(landCount, entry.LandShade == null ? 0 : entry.LandShade.Length);
                for (int i = 0; i < landCount; i++)
                {
                    int target = entry.PackedLandOffset + i;
                    if (target < 0 || target >= vertexCount) continue;
                    gpuDynamicTerrainSemanticScratch[target] = new Vector3(
                        entry.LandElevationMeters[i], entry.LandShade[i], 1f);
                }
                int coastalLandCount = Math.Min(entry.PackedCoastalLandCount,
                    entry.CoastalLandCorrectionElevationMeters == null ? 0 :
                    entry.CoastalLandCorrectionElevationMeters.Length);
                for (int i = 0; i < coastalLandCount; i++)
                {
                    int target = entry.PackedCoastalLandOffset + i;
                    if (target < 0 || target >= vertexCount) continue;
                    byte shade = entry.CoastalLandCorrectionShade != null &&
                        i < entry.CoastalLandCorrectionShade.Length ?
                        entry.CoastalLandCorrectionShade[i] : (byte)255;
                    gpuDynamicTerrainSemanticScratch[target] = new Vector3(
                        entry.CoastalLandCorrectionElevationMeters[i], shade, 1f);
                }
                entry.PackedTerrainMesh.SetUVs(2, gpuDynamicTerrainSemanticScratch);
                entry.GpuDynamicColourAttributesReady = true;
                operationHealthGpuDynamicSemanticUploads++;
                return true;
            }
            catch (Exception ex)
            {
                entry.GpuDynamicColourRejected = true;
                operationHealthGpuDynamicSemanticFailures++;
                AERISLogger.Warn("[AERIS25_GPU_DYNAMIC_COLOUR] Entry CPU fallback; key=" +
                    (entry.CacheKey ?? "NONE") + "; reason=" + ex.GetType().Name + ": " + ex.Message + ".");
                return false;
            }
        }

        void RecordGpuVertexProjectionReject(Entry entry, string reason)
        {
            // AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS is observation-only. Keep the
            // generic failure counter authoritative and classify only the same initial
            // false->true reject transitions that already caused CPU exact fallback.
            operationHealthGpuVertexRejectInitial++;
            if (reason == "PACKED_GEO_NULL")
                operationHealthGpuVertexRejectPackedNull++;
            else if (reason == "PACKED_GEO_LENGTH")
                operationHealthGpuVertexRejectPackedLength++;
            else if (reason == "CONTOUR_GEO_NULL")
                operationHealthGpuVertexRejectContourNull++;
            else if (reason == "CONTOUR_GEO_LENGTH")
                operationHealthGpuVertexRejectContourLength++;
            else if (reason == "COAST_GEO_NULL")
                operationHealthGpuVertexRejectCoastNull++;
            else if (reason == "COAST_GEO_LENGTH")
                operationHealthGpuVertexRejectCoastLength++;
            else if (reason == "SEMANTIC_PACKED_MESH_NULL")
                operationHealthGpuVertexRejectSemanticPackedMeshNull++;
            else if (reason == "SEMANTIC_REJECTED")
                operationHealthGpuVertexRejectSemanticRejected++;
            else if (reason == "SEMANTIC_EXCEPTION")
                operationHealthGpuVertexRejectSemanticException++;
            else if (reason == "SEMANTIC_OTHER")
                operationHealthGpuVertexRejectSemanticOther++;
            else if (reason == "EXCEPTION")
                operationHealthGpuVertexRejectException++;
            else
                operationHealthGpuVertexRejectOther++;

            if (operationHealthGpuVertexRejectDiagnosticSamples >=
                GpuVertexRejectDiagnosticSampleLimit)
                return;

            operationHealthGpuVertexRejectDiagnosticSamples++;
            try
            {
                int packedVertices = entry == null || entry.PackedTerrainMesh == null ?
                    -1 : entry.PackedTerrainMesh.vertexCount;
                int contourVertices = entry == null || entry.ContourMesh == null ?
                    -1 : entry.ContourMesh.vertexCount;
                int coastVertices = entry == null || entry.CoastlineMesh == null ?
                    -1 : entry.CoastlineMesh.vertexCount;
                int packedGeo = entry == null || entry.PackedTerrainGeographicPoints == null ?
                    -1 : entry.PackedTerrainGeographicPoints.Length;
                int contourGeo = entry == null || entry.ContourGeographicPoints == null ?
                    -1 : entry.ContourGeographicPoints.Length;
                int coastGeo = entry == null || entry.CoastlineGeographicPoints == null ?
                    -1 : entry.CoastlineGeographicPoints.Length;
                AERISLogger.Warn("[AERIS25_GPU_VERTEX_REJECT_DIAG] sample=" +
                    operationHealthGpuVertexRejectDiagnosticSamples + "/" +
                    GpuVertexRejectDiagnosticSampleLimit + "; reason=" + reason +
                    "; key=" + (entry == null || entry.CacheKey == null ? "NONE" : entry.CacheKey) +
                    "; lod=" + (entry == null ? "NONE" : entry.TileKey.Lod.ToString()) +
                    "; packedV=" + packedVertices + "; packedGeo=" + packedGeo +
                    "; contourV=" + contourVertices + "; contourGeo=" + contourGeo +
                    "; coastV=" + coastVertices + "; coastGeo=" + coastGeo +
                    "; gpuReady=" + (entry != null && entry.GpuVertexProjectionAttributesReady) +
                    "; semanticReady=" + (entry != null && entry.GpuDynamicColourAttributesReady) +
                    "; semanticRejected=" + (entry != null && entry.GpuDynamicColourRejected) +
                    "; coverage=" + (entry == null ? "NONE" :
                        entry.CoverageFraction.ToString("F3", CultureInfo.InvariantCulture)) + ".");
            }
            catch
            {
                // Diagnostics must never change renderer/fallback behaviour.
            }
        }

        bool EnsureGpuVertexProjectionAttributes(Entry entry)
        {
            if (entry == null || !gpuVertexProjection.Active) return false;
            if (entry.GpuVertexProjectionRejected)
            {
                operationHealthGpuVertexRejectRevisits++;
                return false;
            }
            if (entry.GpuVertexProjectionAttributesReady) return true;
            try
            {
                if (!UploadGpuGeographicAttribute(entry.PackedTerrainMesh,
                        entry.PackedTerrainGeographicPoints,
                        ref operationHealthGpuVertexPackedMismatch))
                {
                    RecordGpuVertexProjectionReject(entry,
                        entry.PackedTerrainGeographicPoints == null ?
                            "PACKED_GEO_NULL" : "PACKED_GEO_LENGTH");
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                if (!UploadGpuGeographicAttribute(entry.ContourMesh,
                        entry.ContourGeographicPoints,
                        ref operationHealthGpuVertexContourMismatch))
                {
                    RecordGpuVertexProjectionReject(entry,
                        entry.ContourGeographicPoints == null ?
                            "CONTOUR_GEO_NULL" : "CONTOUR_GEO_LENGTH");
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                if (!UploadGpuGeographicAttribute(entry.CoastlineMesh,
                        entry.CoastlineGeographicPoints,
                        ref operationHealthGpuVertexCoastlineMismatch))
                {
                    RecordGpuVertexProjectionReject(entry,
                        entry.CoastlineGeographicPoints == null ?
                            "COAST_GEO_NULL" : "COAST_GEO_LENGTH");
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }

                long semanticFailuresBefore = operationHealthGpuDynamicSemanticFailures;
                if (!EnsureGpuDynamicTerrainColourAttributes(entry))
                {
                    string semanticReason;
                    if (entry.PackedTerrainMesh == null)
                        semanticReason = "SEMANTIC_PACKED_MESH_NULL";
                    else if (operationHealthGpuDynamicSemanticFailures >
                        semanticFailuresBefore)
                        semanticReason = "SEMANTIC_EXCEPTION";
                    else if (entry.GpuDynamicColourRejected)
                        semanticReason = "SEMANTIC_REJECTED";
                    else
                        semanticReason = "SEMANTIC_OTHER";
                    RecordGpuVertexProjectionReject(entry, semanticReason);
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                entry.GpuVertexProjectionAttributesReady = true;
                return true;
            }
            catch (Exception ex)
            {
                RecordGpuVertexProjectionReject(entry, "EXCEPTION");
                entry.GpuVertexProjectionRejected = true;
                operationHealthGpuVertexAttributeFailures++;
                AERISLogger.Warn("[AERIS24_GPU_VERTEX_PROJECTION] Entry CPU fallback; key=" +
                    (entry.CacheKey ?? "NONE") + "; reason=" + ex.GetType().Name +
                    ": " + ex.Message + ".");
                return false;
            }
        }

        bool UploadGpuGeographicAttribute(Mesh mesh, GeographicUnitPoint[] points,
            ref long mismatchCounter)
        {
            if (mesh == null) return true;
            if (points == null || points.Length != mesh.vertexCount)
            {
                mismatchCounter++;
                return false;
            }
            gpuVertexGeographicScratch.Clear();
            if (gpuVertexGeographicScratch.Capacity < points.Length)
            {
                long rev35R006GrowStart = Stopwatch.GetTimestamp();
                gpuVertexGeographicScratch.Capacity = points.Length;
                double rev35R006GrowMs = (Stopwatch.GetTimestamp() -
                    rev35R006GrowStart) * 1000.0 / Stopwatch.Frequency;
                operationHealthRev35R006GpuAttrGrow++;
                operationHealthRev35R006GpuAttrGrowMaxMs = Math.Max(
                    operationHealthRev35R006GpuAttrGrowMaxMs, rev35R006GrowMs);
                operationHealthRev35R006GpuAttrCapacityMax = Math.Max(
                    operationHealthRev35R006GpuAttrCapacityMax,
                    gpuVertexGeographicScratch.Capacity);
            }
            for (int i = 0; i < points.Length; i++)
            {
                GeographicUnitPoint point = points[i];
                gpuVertexGeographicScratch.Add(new Vector3((float)point.X,
                    (float)point.Y, (float)point.Z));
            }
            // UV channel 1 maps to TEXCOORD1 and is immutable after this one-time upload.
            mesh.SetUVs(1, gpuVertexGeographicScratch);
            operationHealthGpuVertexAttributeUploads++;
            return true;
        }

        Matrix4x4 EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters,
            double currentCenterLatitudeDeg, double currentCenterLongitudeDeg,
            bool forceCenterProjectionRefresh)
        {
            if (entry == null) return Matrix4x4.identity;
            if (gpuVertexProjection.Active && EnsureGpuVertexProjectionAttributes(entry))
            {
                operationHealthGpuVertexExactBypasses++;
                return Matrix4x4.identity;
            }
            bool structuralProjectionChange =
                double.IsNaN(entry.LastProjectionCenterLatitudeDeg) ||
                double.IsNaN(entry.LastProjectionCenterX) ||
                Math.Abs(entry.LastProjectionBodyRadius - context.RadiusMeters) > 0.01 ||
                Math.Abs(entry.LastProjectionRangeMeters - context.VerticalMeters) > 0.01 ||
                Math.Abs(entry.LastProjectionAnchorBottom - context.AnchorRenderV) > 0.000001f ||
                entry.LastProjectionOrientation != context.Orientation;

            double east = 0.0, north = 0.0;
            bool centerMoved = false;
            double centerMotionSquared = 0.0;
            if (!structuralProjectionChange)
            {
                ToLocalMeters(context.RadiusMeters,
                    entry.LastProjectionCenterLatitudeDeg,
                    entry.LastProjectionCenterLongitudeDeg,
                    currentCenterLatitudeDeg, currentCenterLongitudeDeg,
                    out east, out north);
                centerMotionSquared = east * east + north * north;
                centerMoved = centerMotionSquared > 0.0001;
            }

            float latitudeScale = Mathf.Max(ProjectionBridgeMinimumLatitudeScale,
                Mathf.Abs(Mathf.Cos((float)currentCenterLatitudeDeg * Mathf.Deg2Rad)));
            double exactDistanceThreshold = Math.Max(0.01,
                movementThresholdMeters * ProjectionBridgeThresholdScale * latitudeScale);
            bool polarExactOnly = Math.Abs(currentCenterLatitudeDeg) >=
                ProjectionBridgeLatitudeLimitDeg;
            float exactAge = entry.LastExactProjectionRealtime < 0f ? float.MaxValue :
                Math.Max(0f, Time.realtimeSinceStartup - entry.LastExactProjectionRealtime);

            // Structural changes and polar center motion remain exact-only. Outside that
            // safety boundary, try a witness-proved affine mapping first. The old 0.20 px
            // translation bridge remains a secondary fallback if affine validation rejects.
            bool exactProjectionDue = structuralProjectionChange ||
                centerMoved && polarExactOnly;
            if (!exactProjectionDue)
            {
                if (centerMoved || forceCenterProjectionRefresh)
                {
                    float staggeredExactDeadlineSeconds =
                        ResolveStaggeredExactRefreshDeadlineSeconds(entry);
                    bool staggeredExactDue = exactAge >= staggeredExactDeadlineSeconds;
                    Matrix4x4 affineBridge;
                    float witnessErrorPixels;
                    if (!polarExactOnly && !staggeredExactDue &&
                        exactAge < AffineWitnessMaximumAgeSeconds &&
                        TryResolveWitnessAffineBridge(entry, context,
                            out affineBridge, out witnessErrorPixels))
                    {
                        operationHealthProjectionBridgeUses++;
                        operationHealthAffineBridgeUses++;
                        long milliPixels = (long)Math.Round(
                            Math.Max(0f, witnessErrorPixels) * 1000.0);
                        if (milliPixels > operationHealthAffineWitnessMaxMilliPixels)
                            operationHealthAffineWitnessMaxMilliPixels = milliPixels;
                        if (exactAge >= StaggeredExactRefreshMinimumSeconds)
                            operationHealthStaggeredExactDeferrals++;
                        return affineBridge;
                    }

                    if (staggeredExactDue) operationHealthStaggeredExactDue++;
                    bool translationExactDue = staggeredExactDue || centerMoved &&
                        (centerMotionSquared >= exactDistanceThreshold * exactDistanceThreshold ||
                         exactAge >= ProjectionRefreshAgeSeconds);
                    if (!translationExactDue)
                    {
                        float oldCenterU, oldCenterV;
                        context.ProjectUnitToRenderNUp(entry.LastProjectionCenterX,
                            entry.LastProjectionCenterY, entry.LastProjectionCenterZ,
                            out oldCenterU, out oldCenterV);
                        float deltaU = oldCenterU - 0.5f;
                        float deltaV = oldCenterV - context.AnchorRenderV;
                        if (Mathf.Abs(deltaU) > 0.0000001f ||
                            Mathf.Abs(deltaV) > 0.0000001f)
                        {
                            operationHealthProjectionBridgeUses++;
                            return Matrix4x4.Translate(new Vector3(deltaU, deltaV, 0f));
                        }
                        return Matrix4x4.identity;
                    }
                    exactProjectionDue = true;
                    operationHealthAffineExactFallbacks++;
                }
                else return Matrix4x4.identity;
            }

            if (!exactProjectionDue) return Matrix4x4.identity;
            ProjectMesh(entry.PackedTerrainMesh,
                entry.PackedTerrainGeographicPoints,
                entry.PackedTerrainProjectedVertices, context);
            ProjectMesh(entry.ContourMesh, entry.ContourGeographicPoints,
                entry.ContourProjectedVertices, context);
            ProjectMesh(entry.CoastlineMesh, entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices, context);
            CaptureProjectionWitnesses(entry);
            entry.LastProjectionCenterLatitudeDeg = currentCenterLatitudeDeg;
            entry.LastProjectionCenterLongitudeDeg = currentCenterLongitudeDeg;
            entry.LastProjectionCenterX = context.CenterX;
            entry.LastProjectionCenterY = context.CenterY;
            entry.LastProjectionCenterZ = context.CenterZ;
            entry.LastExactProjectionRealtime = Time.realtimeSinceStartup;
            entry.LastProjectionBodyRadius = context.RadiusMeters;
            entry.LastProjectionRangeMeters = (float)context.VerticalMeters;
            entry.LastProjectionAnchorBottom = context.AnchorRenderV;
            entry.LastProjectionOrientation = context.Orientation;
            operationHealthProjectionExactRefreshes++;
            return Matrix4x4.identity;
        }

        static int ResolveStaggeredExactRefreshSlot(Entry entry)
        {
            if (entry == null) return 0;
            if (entry.ExactRefreshStaggerSlot >= 0 &&
                entry.ExactRefreshStaggerSlot < StaggeredExactRefreshSlotCount)
                return entry.ExactRefreshStaggerSlot;
            string key = entry.CacheKey ?? string.Empty;
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < key.Length; i++)
                {
                    hash ^= key[i];
                    hash *= 16777619u;
                }
                entry.ExactRefreshStaggerSlot = (int)(hash %
                    (uint)StaggeredExactRefreshSlotCount);
            }
            return entry.ExactRefreshStaggerSlot;
        }

        static float ResolveStaggeredExactRefreshDeadlineSeconds(Entry entry)
        {
            int slot = ResolveStaggeredExactRefreshSlot(entry);
            return Mathf.Min(AffineWitnessMaximumAgeSeconds,
                StaggeredExactRefreshMinimumSeconds +
                slot * StaggeredExactRefreshSlotSeconds);
        }

        void CaptureProjectionWitnesses(Entry entry)
        {
            if (entry == null) return;
            for (int i = 0; i < AffineWitnessMaximumCount; i++)
            {
                affineWitnessScoreScratch[i] = double.NegativeInfinity;
                affineWitnessValidScratch[i] = false;
            }
            AccumulateProjectionWitnessCandidates(entry.PackedTerrainGeographicPoints,
                entry.PackedTerrainProjectedVertices);
            AccumulateProjectionWitnessCandidates(entry.ContourGeographicPoints,
                entry.ContourProjectedVertices);
            AccumulateProjectionWitnessCandidates(entry.CoastlineGeographicPoints,
                entry.CoastlineProjectedVertices);

            if (entry.ProjectionWitnessPoints == null ||
                entry.ProjectionWitnessPoints.Length != AffineWitnessMaximumCount)
                entry.ProjectionWitnessPoints =
                    new GeographicUnitPoint[AffineWitnessMaximumCount];
            if (entry.ProjectionWitnessExactVertices == null ||
                entry.ProjectionWitnessExactVertices.Length != AffineWitnessMaximumCount)
                entry.ProjectionWitnessExactVertices =
                    new Vector2[AffineWitnessMaximumCount];

            int count = 0;
            for (int i = 0; i < AffineWitnessMaximumCount; i++)
            {
                if (!affineWitnessValidScratch[i]) continue;
                Vector2 exact = affineWitnessExactScratch[i];
                bool duplicate = false;
                for (int j = 0; j < count; j++)
                {
                    Vector2 prior = entry.ProjectionWitnessExactVertices[j];
                    if ((prior - exact).sqrMagnitude <= 0.000000000001f)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate) continue;
                entry.ProjectionWitnessPoints[count] = affineWitnessPointScratch[i];
                entry.ProjectionWitnessExactVertices[count] = exact;
                count++;
            }
            entry.ProjectionWitnessCount = count;
            entry.ProjectionWitnessBasisA = -1;
            entry.ProjectionWitnessBasisB = -1;
            entry.ProjectionWitnessBasisC = -1;
            float bestArea = 0f;
            for (int a = 0; a < count - 2; a++)
            for (int b = a + 1; b < count - 1; b++)
            for (int c = b + 1; c < count; c++)
            {
                Vector2 p0 = entry.ProjectionWitnessExactVertices[a];
                Vector2 p1 = entry.ProjectionWitnessExactVertices[b];
                Vector2 p2 = entry.ProjectionWitnessExactVertices[c];
                float area = Mathf.Abs((p1.x - p0.x) * (p2.y - p0.y) -
                    (p2.x - p0.x) * (p1.y - p0.y));
                if (area <= bestArea) continue;
                bestArea = area;
                entry.ProjectionWitnessBasisA = a;
                entry.ProjectionWitnessBasisB = b;
                entry.ProjectionWitnessBasisC = c;
            }
            if (bestArea < AffineWitnessSourceAreaEpsilon)
            {
                entry.ProjectionWitnessCount = 0;
                entry.ProjectionWitnessBasisA = -1;
                entry.ProjectionWitnessBasisB = -1;
                entry.ProjectionWitnessBasisC = -1;
            }
        }

        void AccumulateProjectionWitnessCandidates(GeographicUnitPoint[] points,
            Vector3[] projectedVertices)
        {
            if (points == null || projectedVertices == null) return;
            int count = Math.Min(points.Length, projectedVertices.Length);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = projectedVertices[i];
                if (float.IsNaN(p.x) || float.IsInfinity(p.x) ||
                    float.IsNaN(p.y) || float.IsInfinity(p.y)) continue;
                GeographicUnitPoint point = points[i];
                ConsiderProjectionWitness(0, p.x, point, p);
                ConsiderProjectionWitness(1, -p.x, point, p);
                ConsiderProjectionWitness(2, p.y, point, p);
                ConsiderProjectionWitness(3, -p.y, point, p);
                ConsiderProjectionWitness(4, p.x + p.y, point, p);
                ConsiderProjectionWitness(5, -(p.x + p.y), point, p);
                ConsiderProjectionWitness(6, p.x - p.y, point, p);
                ConsiderProjectionWitness(7, -p.x + p.y, point, p);
            }
        }

        void ConsiderProjectionWitness(int slot, double score,
            GeographicUnitPoint point, Vector3 exact)
        {
            if (slot < 0 || slot >= AffineWitnessMaximumCount ||
                score <= affineWitnessScoreScratch[slot]) return;
            affineWitnessScoreScratch[slot] = score;
            affineWitnessValidScratch[slot] = true;
            affineWitnessPointScratch[slot] = point;
            affineWitnessExactScratch[slot] = new Vector2(exact.x, exact.y);
        }

        bool TryResolveWitnessAffineBridge(Entry entry, AERISNdMapProjection context,
            out Matrix4x4 bridge, out float maximumErrorPixels)
        {
            bridge = Matrix4x4.identity;
            maximumErrorPixels = float.MaxValue;
            if (entry == null || entry.ProjectionWitnessCount < 3 ||
                entry.ProjectionWitnessPoints == null ||
                entry.ProjectionWitnessExactVertices == null ||
                entry.ProjectionWitnessBasisA < 0 ||
                entry.ProjectionWitnessBasisB < 0 ||
                entry.ProjectionWitnessBasisC < 0 ||
                backTarget == null || !backTarget.IsCreated()) return false;

            int count = Math.Min(AffineWitnessMaximumCount,
                entry.ProjectionWitnessCount);
            for (int i = 0; i < count; i++)
            {
                GeographicUnitPoint point = entry.ProjectionWitnessPoints[i];
                float u, v;
                context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,
                    out u, out v);
                if (float.IsNaN(u) || float.IsInfinity(u) ||
                    float.IsNaN(v) || float.IsInfinity(v))
                {
                    operationHealthAffineBridgeRejects++;
                    return false;
                }
                affineWitnessCurrentScratch[i] = new Vector2(u, v);
            }
            operationHealthAffineWitnessTests += count;

            int ia = entry.ProjectionWitnessBasisA;
            int ib = entry.ProjectionWitnessBasisB;
            int ic = entry.ProjectionWitnessBasisC;
            if (ia >= count || ib >= count || ic >= count)
            {
                operationHealthAffineBridgeRejects++;
                return false;
            }
            Vector2 p0 = entry.ProjectionWitnessExactVertices[ia];
            Vector2 p1 = entry.ProjectionWitnessExactVertices[ib];
            Vector2 p2 = entry.ProjectionWitnessExactVertices[ic];
            Vector2 q0 = affineWitnessCurrentScratch[ia];
            Vector2 q1 = affineWitnessCurrentScratch[ib];
            Vector2 q2 = affineWitnessCurrentScratch[ic];
            float px1 = p1.x - p0.x, py1 = p1.y - p0.y;
            float px2 = p2.x - p0.x, py2 = p2.y - p0.y;
            float qx1 = q1.x - q0.x, qy1 = q1.y - q0.y;
            float qx2 = q2.x - q0.x, qy2 = q2.y - q0.y;
            float sourceDeterminant = px1 * py2 - px2 * py1;
            if (Mathf.Abs(sourceDeterminant) < AffineWitnessSourceAreaEpsilon)
            {
                operationHealthAffineBridgeRejects++;
                return false;
            }
            float inverse = 1f / sourceDeterminant;
            float a00 = (qx1 * py2 - qx2 * py1) * inverse;
            float a01 = (-qx1 * px2 + qx2 * px1) * inverse;
            float a10 = (qy1 * py2 - qy2 * py1) * inverse;
            float a11 = (-qy1 * px2 + qy2 * px1) * inverse;
            float determinant = a00 * a11 - a01 * a10;
            if (float.IsNaN(determinant) || float.IsInfinity(determinant) ||
                determinant < AffineWitnessDeterminantMinimum ||
                determinant > AffineWitnessDeterminantMaximum)
            {
                operationHealthAffineBridgeRejects++;
                return false;
            }
            float tx = q0.x - a00 * p0.x - a01 * p0.y;
            float ty = q0.y - a10 * p0.x - a11 * p0.y;
            bridge = Matrix4x4.identity;
            bridge.m00 = a00;
            bridge.m01 = a01;
            bridge.m03 = tx;
            bridge.m10 = a10;
            bridge.m11 = a11;
            bridge.m13 = ty;

            float width = Math.Max(1f, backTarget.width);
            float height = Math.Max(1f, backTarget.height);
            float maximum = 0f;
            for (int i = 0; i < count; i++)
            {
                Vector2 source = entry.ProjectionWitnessExactVertices[i];
                float predictedU = a00 * source.x + a01 * source.y + tx;
                float predictedV = a10 * source.x + a11 * source.y + ty;
                Vector2 exact = affineWitnessCurrentScratch[i];
                float dx = (predictedU - exact.x) * width;
                float dy = (predictedV - exact.y) * height;
                float errorPixels = Mathf.Sqrt(dx * dx + dy * dy);
                if (errorPixels > maximum) maximum = errorPixels;
                if (errorPixels > AffineWitnessAcceptancePixels)
                {
                    maximumErrorPixels = maximum;
                    operationHealthAffineBridgeRejects++;
                    return false;
                }
            }
            maximumErrorPixels = maximum;
            return true;
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
            if (entry == null || entry.PackedTerrainMesh == null) return false;
            bool gpuEntry = gpuVertexProjection.Active &&
                entry.GpuVertexProjectionAttributesReady && entry.GpuDynamicColourAttributesReady &&
                !entry.GpuVertexProjectionRejected && !entry.GpuDynamicColourRejected;
            if (!gpuEntry)
                EnsurePackedTerrainColours(entry, mode, preset, aircraftAltitudeAslMeters);
            else
                operationHealthGpuDynamicCpuColourBypasses++;
            Material terrainDrawMaterial = gpuEntry ? gpuVertexProjection.TerrainMaterial : terrainMaterial;
            Material contourDrawMaterial = gpuEntry ? gpuVertexProjection.ContourMaterial : contourMaterial;
            Material coastlineDrawMaterial = gpuEntry ? gpuVertexProjection.CoastlineMaterial : coastlineMaterial;
            bool rendered = false;
            if (terrainDrawMaterial != null && terrainDrawMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix);
                operationHealthDrawMeshSubmissions++;
                operationHealthPackedTerrainDraws++;
                if (gpuEntry)
                {
                    operationHealthGpuVertexDraws++;
                    operationHealthGpuDynamicVerticesSubmitted +=
                        entry.PackedTerrainMesh.vertexCount;
                }
                int saved = Math.Max(0, entry.PackedTerrainSourceMeshCount - 1);
                operationHealthPackedTerrainDrawSubmissionsSaved += saved;
                operationHealthTerrainSetPassSaved += saved;
                rendered = true;
            }
            else if (gpuEntry)
            {
                gpuVertexProjectionBackFailure = true;
                gpuVertexProjection.DisableAndFallback("terrain SetPass failed after preflight");
                return false;
            }
            if (drawContours && entry.ContourMesh != null)
            {
                if (contourDrawMaterial != null && contourDrawMaterial.SetPass(0))
                {
                    Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);
                    operationHealthDrawMeshSubmissions++;
                }
                else if (gpuEntry)
                {
                    gpuVertexProjectionBackFailure = true;
                    gpuVertexProjection.DisableAndFallback("contour SetPass failed after preflight");
                    return false;
                }
            }
            if (entry.CoastlineMesh != null)
            {
                if (coastlineDrawMaterial != null && coastlineDrawMaterial.SetPass(0))
                {
                    Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);
                    operationHealthDrawMeshSubmissions++;
                }
                else if (gpuEntry)
                {
                    gpuVertexProjectionBackFailure = true;
                    gpuVertexProjection.DisableAndFallback("coastline SetPass failed after preflight");
                    return false;
                }
            }
            return rendered;
        }

        static void EnsurePackedTerrainColours(Entry entry,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || entry.PackedTerrainMesh == null ||
                entry.PackedTerrainColours == null) return;
            int altitudeBucket = mode == AERISTerrainDisplayMode.Relative ?
                Mathf.RoundToInt(aircraftAltitudeAslMeters / RelativeAltitudeBucketMeters) :
                int.MinValue;
            bool waterChanged = entry.WaterColourPreset != preset;
            bool landChanged = entry.ColourMode != mode || entry.ColourPreset != preset ||
                entry.RelativeAltitudeBucket != altitudeBucket;
            if (!waterChanged && !landChanged) return;

            if (waterChanged)
            {
                Color32 waterColour = ResolveWaterColour(preset);
                int waterEnd = Math.Min(entry.PackedTerrainColours.Length,
                    entry.PackedWaterOffset + entry.PackedWaterCount);
                for (int i = Math.Max(0, entry.PackedWaterOffset); i < waterEnd; i++)
                    entry.PackedTerrainColours[i] = waterColour;
                int coastalWaterEnd = Math.Min(entry.PackedTerrainColours.Length,
                    entry.PackedCoastalWaterOffset + entry.PackedCoastalWaterCount);
                for (int i = Math.Max(0, entry.PackedCoastalWaterOffset);
                    i < coastalWaterEnd; i++) entry.PackedTerrainColours[i] = waterColour;
                entry.WaterColourPreset = preset;
            }

            if (landChanged)
            {
                float quantizedAltitude = mode == AERISTerrainDisplayMode.Relative ?
                    altitudeBucket * RelativeAltitudeBucketMeters : aircraftAltitudeAslMeters;
                int landCount = Math.Min(entry.PackedLandCount,
                    entry.LandElevationMeters == null ? 0 : entry.LandElevationMeters.Length);
                landCount = Math.Min(landCount,
                    entry.LandShade == null ? 0 : entry.LandShade.Length);
                for (int i = 0; i < landCount; i++)
                {
                    Color32 baseColour = ResolveLandColour(mode, preset,
                        entry.LandElevationMeters[i], quantizedAltitude);
                    int target = entry.PackedLandOffset + i;
                    if (target >= 0 && target < entry.PackedTerrainColours.Length)
                        entry.PackedTerrainColours[target] =
                            ApplyShade(baseColour, entry.LandShade[i], mode);
                }
                int coastalLandCount = Math.Min(entry.PackedCoastalLandCount,
                    entry.CoastalLandCorrectionElevationMeters == null ? 0 :
                    entry.CoastalLandCorrectionElevationMeters.Length);
                for (int i = 0; i < coastalLandCount; i++)
                {
                    Color32 baseColour = ResolveLandColour(mode, preset,
                        entry.CoastalLandCorrectionElevationMeters[i], quantizedAltitude);
                    byte shade = entry.CoastalLandCorrectionShade != null &&
                        i < entry.CoastalLandCorrectionShade.Length ?
                        entry.CoastalLandCorrectionShade[i] : (byte)255;
                    int target = entry.PackedCoastalLandOffset + i;
                    if (target >= 0 && target < entry.PackedTerrainColours.Length)
                        entry.PackedTerrainColours[target] =
                            ApplyShade(baseColour, shade, mode);
                }
                entry.ColourMode = mode;
                entry.ColourPreset = preset;
                entry.RelativeAltitudeBucket = altitudeBucket;
            }
            // Water and land dirty states are merged into one packed colour upload.
            entry.PackedTerrainMesh.colors32 = entry.PackedTerrainColours;
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

        static bool HasRenderableTerrain(Entry entry)
        {
            return entry != null && entry.PackedTerrainMesh != null;
        }

        void ResolveRenderableEntries(AERISTerrainHeightTile tile, string cacheKey,
            string styleKey, out Entry fallback, out Entry current)
        {
            fallback = null;
            current = null;
            if (tile == null || string.IsNullOrEmpty(cacheKey)) return;
            operationHealthResolveCalls++;
            Entry exact;
            if (entries.TryGetValue(cacheKey, out exact) &&
                HasRenderableTerrain(exact)) current = exact;

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
                if (!HasRenderableTerrain(candidate) ||
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
                "; colourSource=" + (gpuVertexProjection.DynamicTerrainColourActive ?
                    "GPU_DYNAMIC_SEMANTIC" : "EXPLICIT_VERTEX") +
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

        bool IsEntryProtectedByContentSnapshot(Entry entry)
        {
            if (entry == null) return false;
            bool protectedEntry = presentationEntryPins.Contains(entry);
            if (protectedEntry) operationHealthPresentationPinHits++;
            else operationHealthPresentationPinMisses++;
            return protectedEntry;
        }

        bool PruneWarmResume(long totalLimit, int maximumRemovals)
        {
            totalLimit = Math.Max(16L * 1024L * 1024L, totalLimit);
            long fixedBytes = Math.Max(0L, backTargetBytes) +
                Math.Max(0L, frontTargetBytes);
            long entryLimit = Math.Max(4L * 1024L * 1024L, totalLimit - fixedBytes);
            int removed = 0;
            int budget = Math.Max(1, maximumRemovals);
            while (usedEntryBytes > entryLimit && entries.Count > 1 && removed < budget)
            {
                Entry oldest = null;
                foreach (Entry entry in entries.Values)
                {
                    if (IsEntryProtectedByContentSnapshot(entry))
                    {
                        operationHealthSnapshotMeshPruneProtected++;
                        continue;
                    }
                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;
                }
                if (oldest == null)
                {
                    operationHealthSnapshotMeshPruneDeferrals++;
                    break;
                }
                Remove(oldest);
                removed++;
                operationHealthWarmPruneRemoved++;
            }
            bool stillOverLimit = usedEntryBytes > entryLimit && entries.Count > 1;
            if (stillOverLimit) operationHealthWarmPruneDeferrals++;
            return stillOverLimit;
        }

        void Prune(long totalLimit)
        {
            totalLimit = Math.Max(16L * 1024L * 1024L, totalLimit);
            long fixedBytes = Math.Max(0L, backTargetBytes) +
                Math.Max(0L, frontTargetBytes);
            long entryLimit = Math.Max(4L * 1024L * 1024L, totalLimit - fixedBytes);
            int removed = 0;
            while (usedEntryBytes > entryLimit && entries.Count > 1 &&
                removed < NormalPruneMaximumRemovals)
            {
                Entry oldest = null;
                foreach (Entry entry in entries.Values)
                {
                    if (IsEntryProtectedByContentSnapshot(entry))
                    {
                        operationHealthSnapshotMeshPruneProtected++;
                        continue;
                    }
                    if (oldest == null || entry.LastUse < oldest.LastUse) oldest = entry;
                }
                if (oldest == null)
                {
                    operationHealthSnapshotMeshPruneDeferrals++;
                    break;
                }
                Remove(oldest);
                evicted++;
                removed++;
            }
            if (usedEntryBytes > entryLimit && entries.Count > 1)
            {
                operationHealthPruneBudgetHits++;
                operationHealthPruneDebtPeakBytes = Math.Max(
                    operationHealthPruneDebtPeakBytes, usedEntryBytes - entryLimit);
            }
        }

        void DetachEntryForDeferredRetirement(Entry entry)
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
                    sparseCoastalCorrectionParentCells - entry.CoastalCorrectionParentCells);
            }
            deferredEntryRetirements.Add(entry);
            operationHealthDeferredRetireQueued++;
            operationHealthDeferredRetirePeak = Math.Max(operationHealthDeferredRetirePeak,
                deferredEntryRetirements.Count);
        }

        void ReleaseDeferredEntryRetirements(bool force)
        {
            for (int i = deferredEntryRetirements.Count - 1; i >= 0; i--)
            {
                Entry entry = deferredEntryRetirements[i];
                if (entry == null)
                {
                    deferredEntryRetirements.RemoveAt(i);
                    continue;
                }
                if (!force && presentationEntryPins.Contains(entry))
                {
                    operationHealthDeferredRetireProtected++;
                    continue;
                }
                usedEntryBytes = Math.Max(0L,
                    usedEntryBytes - Math.Max(0L, entry.Bytes));
                RecycleRev35R006Hf4EntryPackedBuffers(entry);
                RecycleRev35R006EntryGeographic(entry);
                RecycleMesh(ref entry.PackedTerrainMesh);
                RecycleMesh(ref entry.ContourMesh);
                RecycleMesh(ref entry.CoastlineMesh);
                AERISTerrainRenderReadyHeightField field;
                if (!entries.ContainsKey(entry.CacheKey) &&
                    renderReadyFields.TryGetValue(entry.CacheKey, out field) &&
                    field != null && field.ResidentTokenValid && residentCache != null)
                    residentCache.TryDemotePresentationState(field.ResidentToken,
                        AERISResidentTileState.RenderReady);
                deferredEntryRetirements.RemoveAt(i);
                operationHealthDeferredRetireReleased++;
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
            RecycleRev35R006Hf4EntryPackedBuffers(entry);
                RecycleRev35R006EntryGeographic(entry);
            RecycleMesh(ref entry.PackedTerrainMesh);
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
            ClearRev35R006GeographicPool();
            ClearRev35R006Hf4PackedPools();
            ResetRev35R007FoundationQueue();
            rev35R008GeometryReconcilePending = false;
            identityIndexCache.Clear();
            uniformColourScratch.Clear();
            gpuVertexGeographicScratch.Clear();
            gpuDynamicTerrainSemanticScratch.Clear();
            gpuVertexProjection.RetainForViewportSuspension();
            completed.Clear();
            requested.Clear();
            scheduledThisFrame.Clear();
            ResetContentSnapshot();
            // R006 HF2: teardown above can recycle pending/deferred resources.
            // Drain both pools once more after reset; ordinary eviction still reuses.
            DestroyMeshPool();
            ClearRev35R006GeographicPool();
            ClearRev35R006Hf4PackedPools();
            ResetRev35R007FoundationQueue();
            rev35R008GeometryReconcilePending = false;
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
            CancelPendingEntryCommit();
            rasterizer.Dispose();
            ReleaseGpuResources();
            gpuVertexProjection.Dispose();
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
