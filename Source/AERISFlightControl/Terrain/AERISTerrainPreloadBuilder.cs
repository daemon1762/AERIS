using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // Non-Flight terrain producer. It owns no thread: PQS sampling is a bounded main-thread
    // input stage, block processing uses GeneralCompute, and compression/commit uses the
    // existing ArchiveCompression lane so Flight reads always win the I/O arbitration.
    internal sealed class AERISTerrainPreloadBuilder : IDisposable
    {
        sealed class BodyPlan
        {
            internal string BodyName = string.Empty;
            internal AERISTerrainBodyPriority Priority = AERISTerrainBodyPriority.Normal;
            internal bool PriorityOverride;
            internal AERISTerrainTileLod QualityLimit = AERISTerrainTileLod.Far;
            internal bool QualityOverride;
            // Automatic progression first completes broad Far coverage on every solid
            // body, then refines registered/current high-priority sites without forcing
            // a prohibitively large global Route pass.
            internal bool AutomaticPointRefinementOnly;
            internal bool AutomaticComplete;
            internal AERISTerrainTileLod CompletedQualityLimit = AERISTerrainTileLod.Global;
            internal bool CompletedPointRefinementOnly;
            internal string CompletedEnvironmentHash = string.Empty;
            internal long StorageLimitBytes;
            internal long LastVisitedUtcTicks;
            internal long GlobalCursor;
            internal long FarCursor;
            internal long RouteCursor;
            internal int PointCursor;
            // Candidate6 high-density coastline upgrade scan. This is independent of
            // terrain LOD progress so promoting FAR_GLOBAL to LAND_SITES never rebuilds
            // already-complete coastline vectors.
            internal long CoastlineCursor;
            internal long CoastlineScannedWithoutUpgrade;
            internal bool CoastlineComplete;
            internal int CompletedCoastlineFormatVersion;
            internal string CompletedCoastlineEnvironmentHash = string.Empty;
            // Transient cyclic scan counters. They are deliberately not persisted: after
            // restart every phase is safely rechecked against the index, so a crash between
            // cursor advance and atomic tile commit can never strand a missing tile forever.
            internal long GlobalScannedWithoutMiss;
            internal long FarScannedWithoutMiss;
            internal long RouteScannedWithoutMiss;
            internal int PointScannedWithoutMiss;
            internal bool Paused;
            internal bool Cancelled;
            internal bool ManualRequested;
            internal string EnvironmentHash = string.Empty;
            internal long Generation = 1L;
            internal long EstimatedTargetTiles;
        }

        enum OperationKind
        {
            Verify,
            Delete,
            Rebuild
        }

        enum ScanResult
        {
            Found,
            Pending,
            Complete
        }

        sealed class Operation
        {
            internal OperationKind Kind;
            internal string BodyName = string.Empty;
        }

        sealed class PendingEncodeWork
        {
            internal BodyPlan Plan;
            internal AERISTerrainHeightTile Tile;
            internal string StableId = string.Empty;
            internal string PqsHash = string.Empty;
            internal string GameDataHash = string.Empty;
            internal long PlanGeneration;
            internal long DatabaseGeneration;
            internal int Attempts;
        }

        sealed class PendingChunkBatch
        {
            internal BodyPlan Plan;
            internal string ChunkId = string.Empty;
            internal float FirstQueuedRealtime;
            internal readonly Dictionary<string, AERISTerrainPreloadEncodedTile> Tiles =
                new Dictionary<string, AERISTerrainPreloadEncodedTile>(StringComparer.Ordinal);
        }

        sealed class ChunkWritePayload
        {
            internal string BatchId = string.Empty;
            internal string[] ChunkIds = new string[0];
            internal AERISTerrainPreloadEncodedTile[] Tiles =
                new AERISTerrainPreloadEncodedTile[0];
            internal string[] StableIds = new string[0];
            internal PendingChunkBatch[] SourceBatches =
                new PendingChunkBatch[0];
        }

        readonly AERISSettings settings;
        readonly AERISTerrainPerformanceController performance;
        readonly AERISTerrainPreloadDatabase database;
        readonly AERISTerrainBlockPipeline blockPipeline;
        readonly object sync = new object();
        readonly Dictionary<string, BodyPlan> plans =
            new Dictionary<string, BodyPlan>(StringComparer.OrdinalIgnoreCase);
        readonly List<AERISTerrainPreloadPoint> points =
            new List<AERISTerrainPreloadPoint>(128);
        readonly Queue<Operation> operations = new Queue<Operation>();
        readonly HashSet<string> pendingWrites =
            new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, PendingEncodeWork> pendingEncodes =
            new Dictionary<string, PendingEncodeWork>(StringComparer.Ordinal);
        readonly HashSet<string> inFlightEncodes =
            new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, PendingChunkBatch> pendingChunkBatches =
            new Dictionary<string, PendingChunkBatch>(StringComparer.Ordinal);
        readonly HashSet<string> inFlightChunkWrites =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> validatedEnvironments =
            new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainHeightTile> pendingCoastlineBaseTiles =
            new Dictionary<string, AERISTerrainHeightTile>(StringComparer.Ordinal);
        readonly string statePath;
        readonly string stateTemporaryPath;
        readonly AERISTerrainPreloadTelemetry telemetry;

        string pointSetSignature = string.Empty;
        AERISTerrainPreloadMode mode;
        AERISTerrainPreloadSpeedProfile speedProfile;
        string activeBodyName = string.Empty;
        AERISTerrainTileLod activeLod;
        string status = "PRELOAD INDEX READY";
        float lastInputRealtime;
        Vector3 lastMousePosition;
        float lastTickRealtime;
        float nextBodyRefreshRealtime;
        float nextStateSaveRealtime;
        bool stateDirty;
        bool flightSuspended;
        bool operationInFlight;
        bool indexRecoveryInFlight;
        bool standardSchedulerApplied;
        bool coastlineScanInFlight;
        string coastlineScanBodyName = string.Empty;
        bool disposed;
        int completedSinceStart;
        int failedSinceStart;
        double writeMbpsEma;
        double compressionRatioEma = 1.0;
        double pqsSampleCostEmaMs;
        double pqsSamplesPerSecondEma;
        double chunkBatchTilesEma;
        double chunkRewriteAmplificationEma = 1.0;
        double chunkFlushMillisecondsEma;
        int lastWriteBatchChunks;
        int lastWriteBatchTiles;
        int activeWriteJobs;
        long writeBatchSequence;
        string bottleneck = "NO WORK";
        string lastLoggedBottleneck = string.Empty;
        float nextThroughputLogRealtime;
        const int StandardEncodeCommitFloor = 16;
        const int StandardEncodeCommitCeiling = 32;
        const int CoastlineScanBatchSize = 8;
        const int CoastlineSamplingActiveLimit = 2;

        long encodeAdmissionBackpressure;
        long writeAdmissionBackpressure;
        long preloadRequiredDropBaseline;
        long preloadLastBlocksCompleted;
        int preloadLastTilesComplete;
        float preloadLastProgressRealtime;

        internal AERISTerrainPreloadBuilder(AERISSettings settings,
            AERISTerrainPerformanceController performance,
            AERISTerrainPreloadDatabase database,
            AERISTerrainBlockPipeline blockPipeline,
            AERISTerrainPreloadTelemetry telemetry)
        {
            this.settings = settings;
            this.performance = performance;
            this.database = database;
            this.blockPipeline = blockPipeline;
            this.telemetry = telemetry ?? new AERISTerrainPreloadTelemetry();
            AERISTerrainPreloadMode configuredMode = settings == null ||
                settings.TerrainPreloadEnabled ? AERISTerrainPreloadMode.AggressiveIdle :
                AERISTerrainPreloadMode.Off;
            mode = configuredMode;
            speedProfile = AERISTerrainPreloadSpeedProfile.Balanced;
            string root = database == null ? string.Empty : database.RootPath;
            statePath = Path.Combine(root, "preload_state.aps");
            stateTemporaryPath = statePath + ".tmp";
            LoadState();
            // AERISSettings is the user-visible source of truth. The state file preserves
            // progress, but an older persisted mode must not override a newer CFG choice.
            mode = configuredMode;
            lastInputRealtime = Time.realtimeSinceStartup;
            lastMousePosition = Input.mousePosition;
            lastTickRealtime = Time.realtimeSinceStartup;
            preloadLastProgressRealtime = lastTickRealtime;
        }

        internal AERISTerrainPreloadMode Mode { get { return mode; } }
        internal AERISTerrainPreloadSpeedProfile SpeedProfile
        {
            get { return speedProfile; }
        }
        internal bool FlightSuspended { get { return flightSuspended; } }
        internal string StatusText { get { return status; } }

        int ResolveWorkerCount()
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            return runtime == null || runtime.Scheduler == null ? 1 :
                Math.Max(1, runtime.Scheduler.WorkerCount);
        }

        internal void SetEnabled(bool enabled)
        {
            mode = enabled ? AERISTerrainPreloadMode.AggressiveIdle :
                AERISTerrainPreloadMode.Off;
            speedProfile = AERISTerrainPreloadSpeedProfile.Balanced;
            if (settings != null)
            {
                settings.TerrainPreloadEnabled = enabled;
                settings.Save();
            }
            stateDirty = true;
            status = enabled ? "PRELOAD ON" : "PRELOAD OFF";
        }

        internal void UpdatePoints(IEnumerable<AERISTerrainPreloadPoint> values)
        {
            var next = new List<AERISTerrainPreloadPoint>(128);
            if (values != null)
            {
                foreach (AERISTerrainPreloadPoint value in values)
                {
                    if (value == null || string.IsNullOrEmpty(value.BodyName) ||
                        !Finite(value.LatitudeDeg) || !Finite(value.LongitudeDeg)) continue;
                    next.Add(new AERISTerrainPreloadPoint
                    {
                        BodyName = value.BodyName,
                        LatitudeDeg = Math.Max(-90.0, Math.Min(90.0, value.LatitudeDeg)),
                        LongitudeDeg = NormalizeLongitude(value.LongitudeDeg),
                        MaximumLod = value.MaximumLod,
                        Priority = value.Priority,
                        Reason = value.Reason ?? string.Empty
                    });
                }
            }
            next.Sort(ComparePreloadPoints);
            string signature = ComputePointSignature(next);
            lock (sync)
            {
                // Registry snapshots are refreshed periodically. Replaying an identical set
                // must not advance Builder generations or cancel a slow in-progress tile.
                if (string.Equals(pointSetSignature, signature,
                    StringComparison.Ordinal)) return;
                pointSetSignature = signature;
                points.Clear();
                points.AddRange(next);
                foreach (BodyPlan plan in plans.Values)
                {
                    plan.PointCursor = 0;
                    plan.PointScannedWithoutMiss = 0;
                    plan.EstimatedTargetTiles = 0L;
                    InvalidateAutomaticCompletion(plan);
                    plan.Generation++;
                }
                stateDirty = true;
            }
        }

        static int ComparePreloadPoints(AERISTerrainPreloadPoint a,
            AERISTerrainPreloadPoint b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int priority = b.Priority.CompareTo(a.Priority);
            if (priority != 0) return priority;
            int body = string.Compare(a.BodyName, b.BodyName,
                StringComparison.OrdinalIgnoreCase);
            if (body != 0) return body;
            int latitude = a.LatitudeDeg.CompareTo(b.LatitudeDeg);
            if (latitude != 0) return latitude;
            int longitude = a.LongitudeDeg.CompareTo(b.LongitudeDeg);
            if (longitude != 0) return longitude;
            int lod = ((int)a.MaximumLod).CompareTo((int)b.MaximumLod);
            if (lod != 0) return lod;
            return string.Compare(a.Reason, b.Reason, StringComparison.Ordinal);
        }

        static string ComputePointSignature(IList<AERISTerrainPreloadPoint> values)
        {
            var builder = new System.Text.StringBuilder(256);
            int count = values == null ? 0 : values.Count;
            builder.Append(count).Append('|');
            for (int i = 0; i < count; i++)
            {
                AERISTerrainPreloadPoint value = values[i];
                builder.Append(value.BodyName ?? string.Empty).Append('|')
                    .Append(value.LatitudeDeg.ToString("R", CultureInfo.InvariantCulture))
                    .Append('|')
                    .Append(value.LongitudeDeg.ToString("R", CultureInfo.InvariantCulture))
                    .Append('|').Append((int)value.MaximumLod).Append('|')
                    .Append(value.Priority).Append('|')
                    .Append(value.Reason ?? string.Empty).Append(';');
            }
            return AERISTerrainHash.Fnv1A64Hex(builder.ToString());
        }

        internal void NoteVisitedBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            CelestialBody body = FindBody(bodyName);
            if (body == null || !AERISTerrainTileSystem.BodyHasSolidSurface(body)) return;
            BodyPlan plan = GetOrCreatePlan(bodyName);
            plan.LastVisitedUtcTicks = DateTime.UtcNow.Ticks;
            stateDirty = true;
        }

        BodyPlan GetSupportedBodyPlan(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return null;
            CelestialBody body = FindBody(bodyName);
            if (body == null || !AERISTerrainTileSystem.BodyHasSolidSurface(body))
            {
                status = "PRELOAD SKIPPED / NO SOLID SURFACE: " + bodyName;
                return null;
            }
            return GetOrCreatePlan(bodyName);
        }

        internal void RequestBuild(string bodyName)
        {
            BodyPlan plan = GetSupportedBodyPlan(bodyName);
            if (plan == null) return;
            plan.ManualRequested = true;
            plan.Paused = false;
            plan.Cancelled = false;
            InvalidateAutomaticCompletion(plan);
            ResetScanState(plan);
            plan.Generation++;
            status = "PRELOAD BUILD QUEUED: " + bodyName;
            stateDirty = true;
        }

        internal void Pause(string bodyName)
        {
            BodyPlan plan = GetSupportedBodyPlan(bodyName);
            if (plan == null) return;
            plan.Paused = true;
            plan.Generation++;
            status = "PRELOAD PAUSED: " + bodyName;
            stateDirty = true;
        }

        internal void Resume(string bodyName)
        {
            BodyPlan plan = GetSupportedBodyPlan(bodyName);
            if (plan == null) return;
            plan.Paused = false;
            plan.Cancelled = false;
            plan.ManualRequested = true;
            InvalidateAutomaticCompletion(plan);
            ResetScanState(plan);
            plan.Generation++;
            status = "PRELOAD RESUMED: " + bodyName;
            stateDirty = true;
        }

        internal void Cancel(string bodyName)
        {
            BodyPlan plan = GetOrCreatePlan(bodyName);
            if (plan == null) return;
            plan.Cancelled = true;
            plan.ManualRequested = false;
            plan.Generation++;
            blockPipeline.CancelWhere(request => request == null ||
                request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder ||
                !string.Equals(request.Key.BodyName, bodyName,
                    StringComparison.OrdinalIgnoreCase));
            ClearPendingCoastlineForBody(bodyName);
            status = "PRELOAD CANCELLED: " + bodyName;
            stateDirty = true;
        }

        internal void RequestVerify(string bodyName)
        {
            EnqueueOperation(OperationKind.Verify, bodyName);
        }

        internal void RequestDelete(string bodyName)
        {
            EnqueueOperation(OperationKind.Delete, bodyName);
        }

        internal void RequestRebuild(string bodyName)
        {
            if (GetSupportedBodyPlan(bodyName) == null) return;
            EnqueueOperation(OperationKind.Rebuild, bodyName);
        }

        internal void Tick(CelestialBody currentBody, bool isFlight)
        {
            if (disposed) return;
            float now = Time.realtimeSinceStartup;
            float elapsed = lastTickRealtime > 0f ? Mathf.Clamp(now - lastTickRealtime,
                0f, 0.5f) : 0f;
            lastTickRealtime = now;
            DetectInput(now);
            RefreshBodies(currentBody, now);
            SaveStateIfNeeded(now);
            if (isFlight)
            {
                ApplyStandardSchedulerState(false);
                blockPipeline.SetPreloadThroughput(false);
                if (!flightSuspended)
                {
                    flightSuspended = true;
                    blockPipeline.CancelWhere(request => request == null ||
                        request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder);
                    ClearPendingCoastlineForBody(string.Empty);
                }
                status = "PRELOAD SUSPENDED / FLIGHT READ PRIORITY";
                bottleneck = "FLIGHT SAFETY";
                UpdateTelemetry(false, 0.0, 0, 0.0);
                return;
            }
            flightSuspended = false;
            ApplyStandardSchedulerState(true);
            blockPipeline.SetPreloadThroughput(true);
            SchedulePendingEncodes();
            ScheduleReadyChunkBatches(now, false);
            if (database == null || !database.IndexLoaded)
            {
                status = "PRELOAD INDEX NOT READY";
                UpdateTelemetry(false, 0.0, 0, 0.0);
                return;
            }
            if (database.IndexRecoveryNeeded || IndexRecoveryInFlight())
            {
                ScheduleIndexRecovery();
                status = "PRELOAD INDEX RECOVERY";
                UpdateTelemetry(false, 0.0, 0, 0.0);
                return;
            }
            ProcessOperationIfPossible(false);
            if (OperationInFlight())
            {
                status = "PRELOAD DATABASE MAINTENANCE";
                UpdateTelemetry(false, 0.0, 0, 0.0);
                return;
            }

            bool idle = now - lastInputRealtime >= ResolveIdleSeconds();
            float qps;
            int samples;
            float milliseconds;
            if (!ResolveBudget(idle, out qps, out samples, out milliseconds))
            {
                UpdateTelemetry(idle, 0.0, 0, 0.0);
                return;
            }

            BodyPlan plan;
            CelestialBody body;
            if (!TrySelectPlan(currentBody, out plan, out body))
            {
                if (!TryAdvanceAutomaticPlan(currentBody) ||
                    !TrySelectPlan(currentBody, out plan, out body))
                {
                    status = "PRELOAD READY / ALL AUTOMATIC TARGETS COMPLETE";
                    UpdateTelemetry(idle, 0.0, 0, 0.0);
                    return;
                }
            }
            activeBodyName = plan.BodyName;
            EnsureEnvironment(plan, body);
            bool queued = false;
            int workers = ResolveWorkerCount();
            int queueTarget = Math.Min(48, Math.Max(8, workers * 4));
            int pendingTarget = Math.Max(24, workers * 16);
            int attempts = queueTarget;
            while (attempts-- > 0 && PendingWorkCount() < pendingTarget &&
                blockPipeline.Count < queueTarget)
            {
                if (!QueueNextTile(plan, body)) break;
                queued = true;
            }
            blockPipeline.Tick(qps, samples, milliseconds, elapsed);
            UpdatePqsCostTelemetry(elapsed);
            SchedulePendingEncodes();
            ScheduleReadyChunkBatches(now, false);
            UpdateBottleneck(queued);
            if (MonitorPreloadHealth(now))
            {
                UpdateTelemetry(idle, qps, samples, milliseconds);
                return;
            }
            if (queued || blockPipeline.Count > 0 || PendingWorkCount() > 0)
                status = "PRELOAD STANDARD " + activeBodyName + " " +
                    activeLod.ToString().ToUpperInvariant();
            UpdateTelemetry(idle, qps, samples, milliseconds);
        }

        internal AERISTerrainPreloadStatusSnapshot SnapshotStatus()
        {
            Dictionary<string, AERISTerrainBodyPriority> priorities =
                new Dictionary<string, AERISTerrainBodyPriority>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, AERISTerrainTileLod> qualities =
                new Dictionary<string, AERISTerrainTileLod>(StringComparer.OrdinalIgnoreCase);
            lock (sync)
            {
                foreach (BodyPlan plan in plans.Values)
                {
                    priorities[plan.BodyName] = plan.Priority;
                    qualities[plan.BodyName] = plan.QualityLimit;
                }
            }
            AERISTerrainPreloadBodyStatus[] fromDatabase = database == null ?
                new AERISTerrainPreloadBodyStatus[0] :
                database.SnapshotBodies(priorities, qualities);
            var byName = new Dictionary<string, AERISTerrainPreloadBodyStatus>(
                StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < fromDatabase.Length; i++)
            {
                AERISTerrainPreloadBodyStatus indexed = fromDatabase[i];
                CelestialBody indexedBody = indexed == null ? null : FindBody(indexed.BodyName);
                if (indexed != null && indexedBody != null &&
                    AERISTerrainTileSystem.BodyHasSolidSurface(indexedBody))
                    byName[indexed.BodyName] = indexed;
            }
            lock (sync)
            {
                foreach (BodyPlan plan in plans.Values)
                {
                    CelestialBody supportedBody = plan == null ? null : FindBody(plan.BodyName);
                    if (plan == null || supportedBody == null ||
                        !AERISTerrainTileSystem.BodyHasSolidSurface(supportedBody)) continue;
                    AERISTerrainPreloadBodyStatus body;
                    if (!byName.TryGetValue(plan.BodyName, out body))
                    {
                        body = new AERISTerrainPreloadBodyStatus
                        {
                            BodyName = plan.BodyName,
                            Supported = true
                        };
                        byName[plan.BodyName] = body;
                    }
                    body.Priority = plan.Priority;
                    body.Pinned = plan.Priority == AERISTerrainBodyPriority.Pinned;
                    body.QualityLimit = plan.QualityLimit;
                    body.StorageLimitBytes = plan.StorageLimitBytes;
                    body.PendingTiles = CountPendingForBody(plan.BodyName);
                    long target = EstimateTargetTiles(plan, supportedBody);
                    body.CoverageRatio = target <= 0L ? 0.0 :
                        Math.Max(0.0, Math.Min(1.0, body.CompleteTiles / (double)target));
                    body.Status = plan.Paused ? "PAUSED" : plan.Cancelled ? "CANCELLED" :
                        BodyAtStorageLimit(plan) ? "CAP REACHED" :
                        string.Equals(activeBodyName, plan.BodyName,
                            StringComparison.OrdinalIgnoreCase) && !flightSuspended ?
                            "BUILDING" : body.CompleteTiles > 0 ? "READY" : "QUEUED";
                }
            }
            var bodies = new List<AERISTerrainPreloadBodyStatus>(byName.Values);
            bodies.Sort((a, b) =>
            {
                int priority = ((int)b.Priority).CompareTo((int)a.Priority);
                return priority != 0 ? priority : string.Compare(a.BodyName, b.BodyName,
                    StringComparison.OrdinalIgnoreCase);
            });
            int encodeQueue;
            int encodeActive;
            int writeQueue;
            int writeActive;
            lock (sync)
            {
                encodeQueue = pendingEncodes.Count;
                encodeActive = inFlightEncodes.Count;
                writeQueue = pendingChunkBatches.Count;
                writeActive = activeWriteJobs;
            }
            AERISRuntimeTelemetrySnapshot schedulerSnapshot = SnapshotScheduler();
            return new AERISTerrainPreloadStatusSnapshot
            {
                Mode = mode,
                SpeedProfile = speedProfile,
                FlightSuspended = flightSuspended,
                Paused = false,
                Idle = Time.realtimeSinceStartup - lastInputRealtime >= ResolveIdleSeconds(),
                StandardThroughputActive = !flightSuspended && standardSchedulerApplied,
                PipelineActiveTileLimit = blockPipeline.ActiveTileLimit,
                PipelinePendingBlockLimit = blockPipeline.PendingBlockLimit,
                PipelineOutstandingBlocks = blockPipeline.OutstandingBlocks,
                PipelineOutstandingBlockLimit = blockPipeline.OutstandingBlockLimit,
                PipelineAdmissionBackpressure =
                    blockPipeline.AdmissionBackpressureCount,
                PipelineRecoveryCount = blockPipeline.RecoveryCount,
                PipelineLastRecoveryTiles = blockPipeline.LastRecoveryTiles,
                PipelineLastRecoveryReason = blockPipeline.LastRecoveryReason,
                SchedulerResultDepth = schedulerSnapshot == null ? 0 :
                    schedulerSnapshot.CompletedQueueDepth,
                SchedulerRequiredResultDepth = schedulerSnapshot == null ? 0 :
                    schedulerSnapshot.RequiredCompletionQueueDepth,
                SchedulerRequiredRejected = schedulerSnapshot == null ? 0L :
                    schedulerSnapshot.RequiredRejected,
                SchedulerRequiredDropped = schedulerSnapshot == null ? 0L :
                    schedulerSnapshot.RequiredDropped,
                EncodeQueueDepth = encodeQueue,
                EncodeActive = encodeActive,
                EncodeCommitLimit = ResolveEncodeCommitLimit(ResolveWorkerCount()),
                EncodeAdmissionBackpressure = encodeAdmissionBackpressure,
                WriteQueueDepth = writeQueue,
                WriteActive = writeActive,
                WriteCommitLimit = ResolveWriteCommitLimit(),
                WriteAdmissionBackpressure = writeAdmissionBackpressure,
                WriteBatchChunks = lastWriteBatchChunks,
                WriteBatchTiles = lastWriteBatchTiles,
                Bottleneck = bottleneck,
                GpuPreloadStage = ResolveGpuPreloadStage(),
                ActiveBody = activeBodyName,
                ActiveLod = activeLod,
                Status = status,
                StorageBytes = database == null ? 0L : database.UsedBytes,
                StorageLimitBytes = database == null ? 0L : database.LimitBytes,
                TilesComplete = database == null ? 0 : database.Count,
                TilesPending = blockPipeline.Count + PendingWorkCount(),
                BuilderQueueDepth = blockPipeline.Count,
                BuilderPqsMilliseconds = blockPipeline.LastBatchMilliseconds,
                BuilderWorkerUtilization = Math.Min(1.0, blockPipeline.Count /
                    (double)Math.Max(1, blockPipeline.ActiveTileLimit)),
                BuilderWriteMbps = writeMbpsEma,
                CompressionRatio = compressionRatioEma,
                BuilderPqsSamplesPerSecond = pqsSamplesPerSecondEma,
                BuilderPqsSampleCostMilliseconds = pqsSampleCostEmaMs,
                BuilderPqsSampleCacheHits = blockPipeline.BoundarySampleCacheHits,
                BuilderPqsSampleCacheMisses = blockPipeline.BoundarySampleCacheMisses,
                BuilderPqsSampleCacheHitRatio =
                    blockPipeline.BoundarySampleCacheHitRatio,
                ChunkBatchTiles = chunkBatchTilesEma,
                ChunkRewriteAmplification = chunkRewriteAmplificationEma,
                ChunkFlushMilliseconds = chunkFlushMillisecondsEma,
                IntermediateCommitsSkipped = blockPipeline.IntermediateCommitsSkipped,
                ParsedChunkCacheHitRatio = telemetry == null ? 0.0 :
                    telemetry.DatabaseParsedChunkCacheHitRatio,
                Bodies = bodies.ToArray()
            };
        }

        bool QueueNextTile(BodyPlan plan, CelestialBody body)
        {
            if (plan == null || body == null) return false;
            if (BodyAtStorageLimit(plan))
            {
                status = "PRELOAD BODY CAP REACHED: " + plan.BodyName;
                return false;
            }
            AERISTerrainTileRequest request;
            bool scanPending;
            if (!TryNextMissingRequest(plan, body, out request, out scanPending) ||
                request == null)
            {
                if (scanPending)
                {
                    status = "PRELOAD INDEX SCAN: " + plan.BodyName;
                    return false;
                }

                // Candidate6 adds one post-FAR refinement phase without changing the
                // established terrain tiles. Existing v1 payloads are decoded, only
                // mixed land/water FAR tiles are resampled at 129x129, and the resulting
                // coastline vector is embedded back into the original 33x33 tile.
                if (!CoastlineTargetComplete(plan, body))
                {
                    activeLod = AERISTerrainTileLod.Far;
                    bool coastlineQueued = ScheduleCoastlineUpgradeScan(plan, body);
                    status = "PRELOAD COASTLINE HD: " + plan.BodyName;
                    return coastlineQueued;
                }

                plan.ManualRequested = false;
                MarkAutomaticComplete(plan);
                status = "PRELOAD COMPLETE: " + plan.BodyName + " " +
                    AutomaticTargetLabel(plan);
                AERISLogger.Info("[PRELOAD_AUTO] body=" + plan.BodyName +
                    "; event=COMPLETE; target=" + AutomaticTargetLabel(plan) +
                    "; coastline_hd=" +
                    AERISTerrainCoastlineExtractor.HighDensityResolution +
                    "; environment=" + (plan.EnvironmentHash ?? string.Empty));
                stateDirty = true;
                return false;
            }
            activeLod = request.Key.Lod;
            request.WorkOwner = AERISTerrainWorkOwner.PreloadBuilder;
            request.ReadLane = AERISTerrainReadLane.Background;
            request.Lane = AERISTerrainRequestLane.Background;
            request.Priority = plan.Priority >= AERISTerrainBodyPriority.High ?
                AERISTerrainTilePriority.Normal : AERISTerrainTilePriority.Low;
            bool accepted = blockPipeline.Enqueue(body, request,
                AERISTerrainTileSource.PreloadBuilderGenerated,
                plan.EnvironmentHash, CurrentGameDataHash(),
                database.DatabaseGeneration,
                candidate => IsBuilderRequestCurrent(plan, candidate),
                (committedRequest, tile, final) =>
                    CommitGeneratedTile(plan, tile, final));
            if (!accepted) failedSinceStart++;
            return accepted;
        }

        bool TryNextMissingRequest(BodyPlan plan, CelestialBody body,
            out AERISTerrainTileRequest request, out bool scanPending)
        {
            request = null;
            scanPending = false;
            if (plan == null || body == null) return false;

            // Each phase is a cyclic bounded index scan. A later phase cannot begin until
            // the earlier phase has made a full pass with no missing tile. This guarantees
            // global minimum coverage first and also recovers a cursor that advanced just
            // before a crash or failed atomic commit.
            ScanResult result = TryNextGlobalTile(plan, body,
                AERISTerrainTileLod.Global, ref plan.GlobalCursor,
                ref plan.GlobalScannedWithoutMiss, out request);
            if (result == ScanResult.Found) return true;
            if (result == ScanResult.Pending) { scanPending = true; return false; }

            result = TryNextPointTile(plan, body, out request);
            if (result == ScanResult.Found) return true;
            if (result == ScanResult.Pending) { scanPending = true; return false; }

            result = TryNextGlobalTile(plan, body, AERISTerrainTileLod.Far,
                ref plan.FarCursor, ref plan.FarScannedWithoutMiss, out request);
            if (result == ScanResult.Found) return true;
            if (result == ScanResult.Pending) { scanPending = true; return false; }

            if (!plan.AutomaticPointRefinementOnly)
            {
                result = TryNextGlobalTile(plan, body, AERISTerrainTileLod.Route,
                    ref plan.RouteCursor, ref plan.RouteScannedWithoutMiss, out request);
                if (result == ScanResult.Found) return true;
                if (result == ScanResult.Pending) { scanPending = true; return false; }
            }
            return false;
        }

        ScanResult TryNextGlobalTile(BodyPlan plan, CelestialBody body,
            AERISTerrainTileLod lod, ref long cursor, ref long scannedWithoutMiss,
            out AERISTerrainTileRequest request)
        {
            request = null;
            if (plan.QualityLimit < lod) return ScanResult.Complete;
            int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
            int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
            long total = (long)latCount * lonCount;
            if (total <= 0L) return ScanResult.Complete;
            if (cursor < 0L || cursor >= total) cursor = 0L;
            if (scannedWithoutMiss >= total) return ScanResult.Complete;

            int inspected = 0;
            int maximum = (int)Math.Min(256L, total);
            while (inspected++ < maximum)
            {
                long value = cursor;
                cursor = (cursor + 1L) % total;
                int lat = (int)(value / lonCount);
                int lon = (int)(value - (long)lat * lonCount);
                var key = new AERISTerrainTileKey(body.name, body.Radius,
                    plan.EnvironmentHash, lod, lat, lon);
                if (database.Contains(key))
                {
                    scannedWithoutMiss++;
                    if (scannedWithoutMiss >= total) return ScanResult.Complete;
                    continue;
                }
                scannedWithoutMiss = 0L;
                request = CreateBuilderRequest(body, key, plan);
                stateDirty = true;
                return ScanResult.Found;
            }
            stateDirty = true;
            return ScanResult.Pending;
        }

        ScanResult TryNextPointTile(BodyPlan plan, CelestialBody body,
            out AERISTerrainTileRequest request)
        {
            request = null;
            List<AERISTerrainPreloadPoint> localPoints =
                new List<AERISTerrainPreloadPoint>();
            lock (sync)
                for (int i = 0; i < points.Count; i++)
                    if (string.Equals(points[i].BodyName, body.name,
                        StringComparison.OrdinalIgnoreCase)) localPoints.Add(points[i]);
            if (localPoints.Count == 0 ||
                plan.QualityLimit < AERISTerrainTileLod.Local)
                return ScanResult.Complete;

            int variants = plan.QualityLimit >= AERISTerrainTileLod.Land ? 18 : 9;
            int total = localPoints.Count * variants;
            if (total <= 0) return ScanResult.Complete;
            if (plan.PointCursor < 0 || plan.PointCursor >= total) plan.PointCursor = 0;
            if (plan.PointScannedWithoutMiss >= total) return ScanResult.Complete;

            int inspected = 0;
            int maximum = Math.Min(256, total);
            while (inspected++ < maximum)
            {
                int linear = plan.PointCursor;
                plan.PointCursor = (plan.PointCursor + 1) % total;
                AERISTerrainPreloadPoint point = localPoints[(linear / variants) %
                    localPoints.Count];
                int variant = linear % variants;
                AERISTerrainTileLod lod = variant >= 9 ?
                    AERISTerrainTileLod.Land : AERISTerrainTileLod.Local;
                if (lod > plan.QualityLimit || lod > point.MaximumLod)
                {
                    plan.PointScannedWithoutMiss++;
                    if (plan.PointScannedWithoutMiss >= total)
                        return ScanResult.Complete;
                    continue;
                }
                int around = variant % 9;
                int dx = around % 3 - 1;
                int dy = around / 3 - 1;
                AERISTerrainTileKey center = AERISTerrainTileSystem.KeyForPoint(body,
                    plan.EnvironmentHash, lod, point.LatitudeDeg, point.LongitudeDeg);
                int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
                int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
                int lat = Math.Max(0, Math.Min(latCount - 1,
                    center.LatitudeIndex + dy));
                int lon = AERISTerrainTileSystem.WrapTileIndex(
                    center.LongitudeIndex + dx, lonCount);
                var key = new AERISTerrainTileKey(body.name, body.Radius,
                    plan.EnvironmentHash, lod, lat, lon);
                if (database.Contains(key))
                {
                    plan.PointScannedWithoutMiss++;
                    if (plan.PointScannedWithoutMiss >= total)
                        return ScanResult.Complete;
                    continue;
                }
                plan.PointScannedWithoutMiss = 0;
                request = CreateBuilderRequest(body, key, plan);
                stateDirty = true;
                return ScanResult.Found;
            }
            stateDirty = true;
            return ScanResult.Pending;
        }

        AERISTerrainTileRequest CreateBuilderRequest(CelestialBody body,
            AERISTerrainTileKey key, BodyPlan plan)
        {
            double span = AERISTerrainTileFormat.AngularSpanDegrees(key.Lod, body.Radius);
            double south = Math.Max(-90.0, -90.0 + key.LatitudeIndex * span);
            double north = Math.Min(90.0, south + span);
            double west = NormalizeLongitude(-180.0 + key.LongitudeIndex * span);
            double east = NormalizeLongitude(west + span);
            int resolution = AERISTerrainTileFormat.Resolution(key.Lod);
            return new AERISTerrainTileRequest
            {
                Key = key,
                Stage = AERISTerrainSamplingStage.Final,
                Resolution = resolution,
                FinalResolution = resolution,
                SouthLatitudeDeg = south,
                NorthLatitudeDeg = north,
                WestLongitudeDeg = west,
                EastLongitudeDeg = east,
                CenterLatitudeDeg = (south + north) * 0.5,
                CenterLongitudeDeg = NormalizeLongitude((west + east) * 0.5),
                ViewDistanceMeters = double.MaxValue,
                RequestSequence = DateTime.UtcNow.Ticks,
                BodyGeneration = plan.LastVisitedUtcTicks,
                TerrainGeneration = plan.Generation,
                DatabaseGeneration = database.RequestGeneration,
                WorkOwner = AERISTerrainWorkOwner.PreloadBuilder,
                Visible = false
            };
        }

        void CommitGeneratedTile(BodyPlan plan, AERISTerrainHeightTile tile, bool final)
        {
            if (tile == null || plan == null) return;
            if (!final)
            {
                telemetry.BuilderPqsMilliseconds = blockPipeline.LastBatchMilliseconds;
                return;
            }
            string id = tile.Key.StableId;
            var work = new PendingEncodeWork
            {
                Plan = plan,
                Tile = tile.CloneImmutable(),
                StableId = id,
                PqsHash = plan.EnvironmentHash,
                GameDataHash = CurrentGameDataHash(),
                PlanGeneration = plan.Generation,
                DatabaseGeneration = database.DatabaseGeneration
            };
            lock (sync)
            {
                if (pendingWrites.Contains(id)) return;
                pendingWrites.Add(id);
                pendingEncodes[id] = work;
            }
            SchedulePendingEncodes();
        }

        bool CoastlineTargetComplete(BodyPlan plan, CelestialBody body)
        {
            if (plan == null || body == null) return false;
            if (!body.ocean)
            {
                MarkCoastlineComplete(plan);
                return true;
            }
            return plan.CoastlineComplete &&
                plan.CompletedCoastlineFormatVersion ==
                    AERISTerrainCoastlineExtractor.HighDensityFormatVersion &&
                string.Equals(plan.CompletedCoastlineEnvironmentHash,
                    plan.EnvironmentHash, StringComparison.Ordinal);
        }

        static void MarkCoastlineComplete(BodyPlan plan)
        {
            if (plan == null) return;
            plan.CoastlineComplete = true;
            plan.CompletedCoastlineFormatVersion =
                AERISTerrainCoastlineExtractor.HighDensityFormatVersion;
            plan.CompletedCoastlineEnvironmentHash = plan.EnvironmentHash ?? string.Empty;
            plan.CoastlineScannedWithoutUpgrade = 0L;
        }

        static void InvalidateCoastlineCompletion(BodyPlan plan)
        {
            if (plan == null) return;
            plan.CoastlineComplete = false;
            plan.CompletedCoastlineFormatVersion = 0;
            plan.CompletedCoastlineEnvironmentHash = string.Empty;
            plan.CoastlineCursor = 0L;
            plan.CoastlineScannedWithoutUpgrade = 0L;
        }

        bool ScheduleCoastlineUpgradeScan(BodyPlan plan, CelestialBody body)
        {
            if (plan == null || body == null || database == null || disposed) return false;
            if (CoastlineTargetComplete(plan, body)) return false;

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return false;

            lock (sync)
            {
                if (coastlineScanInFlight ||
                    PendingCoastlineSamplingCountLocked(plan.BodyName) >=
                        CoastlineSamplingActiveLimit) return false;
            }

            int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body,
                AERISTerrainTileLod.Far);
            int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body,
                AERISTerrainTileLod.Far);
            long total = (long)latCount * lonCount;
            if (total <= 0L)
            {
                MarkCoastlineComplete(plan);
                stateDirty = true;
                return false;
            }
            if (plan.CoastlineCursor < 0L || plan.CoastlineCursor >= total)
                plan.CoastlineCursor = 0L;

            var keys = new List<AERISTerrainTileKey>(CoastlineScanBatchSize);
            long cursor = plan.CoastlineCursor;
            int inspected = 0;
            while (inspected++ < CoastlineScanBatchSize && keys.Count < CoastlineScanBatchSize)
            {
                long linear = cursor;
                cursor = (cursor + 1L) % total;
                int lat = (int)(linear / lonCount);
                int lon = (int)(linear % lonCount);
                var key = new AERISTerrainTileKey(body.name, body.Radius,
                    plan.EnvironmentHash, AERISTerrainTileLod.Far, lat, lon);
                // The normal FAR phase is authoritative. If a tile disappeared after the
                // terrain scan, let that phase recover it instead of inventing coastline
                // data around a missing base payload.
                if (!database.Contains(key))
                {
                    plan.FarScannedWithoutMiss = 0L;
                    plan.CoastlineScannedWithoutUpgrade = 0L;
                    stateDirty = true;
                    return false;
                }
                keys.Add(key);
            }
            if (keys.Count == 0) return false;

            string gameDataHash = CurrentGameDataHash();
            long capturedGeneration = plan.Generation;
            string capturedEnvironment = plan.EnvironmentHash ?? string.Empty;
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute,
                "preload-coast-scan:" + plan.BodyName,
                runtime.CaptureStamp(), context =>
                {
                    var output = new Dictionary<string, AERISTerrainHeightTile>(
                        StringComparer.Ordinal);
                    database.TryLoadBatch(keys, null, output, null, gameDataHash);
                    return output;
                }, value =>
                {
                    lock (sync)
                    {
                        coastlineScanInFlight = false;
                        coastlineScanBodyName = string.Empty;
                    }
                    var loaded = value as Dictionary<string, AERISTerrainHeightTile>;
                    if (disposed || HighLogic.LoadedSceneIsFlight || plan.Cancelled ||
                        plan.Paused || plan.Generation != capturedGeneration ||
                        !string.Equals(plan.EnvironmentHash, capturedEnvironment,
                            StringComparison.Ordinal)) return;

                    bool incomplete = loaded == null || loaded.Count != keys.Count;
                    int upgrades = 0;
                    for (int i = 0; i < keys.Count; i++)
                    {
                        AERISTerrainTileKey key = keys[i];
                        AERISTerrainHeightTile tile;
                        if (loaded == null || !loaded.TryGetValue(key.StableId, out tile) ||
                            tile == null)
                        {
                            incomplete = true;
                            continue;
                        }
                        if (AERISTerrainCoastlineExtractor.HasCurrentHighDensityPayload(tile))
                            continue;
                        if (!AERISTerrainCoastlineExtractor.ContainsLandWaterBoundary(tile))
                            continue;
                        if (TryEnqueueHighDensityCoastline(plan, body, tile)) upgrades++;
                        else incomplete = true;
                    }

                    if (!incomplete && upgrades == 0)
                        plan.CoastlineScannedWithoutUpgrade += keys.Count;
                    else
                        plan.CoastlineScannedWithoutUpgrade = 0L;

                    if (plan.CoastlineScannedWithoutUpgrade >= total &&
                        PendingCoastlineSamplingCount(plan.BodyName) == 0 &&
                        CountPendingForBody(plan.BodyName) == 0)
                    {
                        MarkCoastlineComplete(plan);
                        AERISLogger.Info("[PRELOAD_COAST_HD] body=" + plan.BodyName +
                            "; event=COMPLETE; resolution=" +
                            AERISTerrainCoastlineExtractor.HighDensityResolution +
                            "; format=" +
                            AERISTerrainCoastlineExtractor.HighDensityFormatVersion +
                            "; environment=" + (plan.EnvironmentHash ?? string.Empty));
                    }
                    stateDirty = true;
                }, false);
            if (!accepted) return false;

            lock (sync)
            {
                coastlineScanInFlight = true;
                coastlineScanBodyName = plan.BodyName;
            }
            plan.CoastlineCursor = cursor;
            stateDirty = true;
            return true;
        }

        bool TryEnqueueHighDensityCoastline(BodyPlan plan, CelestialBody body,
            AERISTerrainHeightTile baseTile)
        {
            if (plan == null || body == null || baseTile == null ||
                baseTile.Key.Lod != AERISTerrainTileLod.Far) return false;
            string id = baseTile.Key.StableId;
            lock (sync)
            {
                if (pendingCoastlineBaseTiles.ContainsKey(id) || pendingWrites.Contains(id) ||
                    PendingCoastlineSamplingCountLocked(plan.BodyName) >=
                        CoastlineSamplingActiveLimit) return false;
                pendingCoastlineBaseTiles[id] = baseTile.CloneImmutable();
            }

            AERISTerrainTileRequest request = CreateHighDensityCoastlineRequest(body,
                baseTile, plan);
            bool accepted = blockPipeline.Enqueue(body, request,
                AERISTerrainTileSource.PreloadBuilderGenerated,
                plan.EnvironmentHash, CurrentGameDataHash(), database.DatabaseGeneration,
                candidate => IsBuilderRequestCurrent(plan, candidate),
                (committedRequest, tile, final) =>
                    CommitGeneratedHighDensityCoastline(plan, tile, final));
            if (accepted)
            {
                AERISLogger.Info("[PRELOAD_COAST_HD] body=" + plan.BodyName +
                    "; event=QUEUE; tile=" + id + "; base_res=" + baseTile.Resolution +
                    "; target_res=" +
                    AERISTerrainCoastlineExtractor.HighDensityResolution);
                return true;
            }
            lock (sync) pendingCoastlineBaseTiles.Remove(id);
            return false;
        }

        AERISTerrainTileRequest CreateHighDensityCoastlineRequest(CelestialBody body,
            AERISTerrainHeightTile baseTile, BodyPlan plan)
        {
            int resolution = AERISTerrainCoastlineExtractor.HighDensityResolution;
            return new AERISTerrainTileRequest
            {
                Key = baseTile.Key,
                Stage = AERISTerrainSamplingStage.Final,
                Resolution = resolution,
                FinalResolution = resolution,
                SouthLatitudeDeg = baseTile.SouthLatitudeDeg,
                NorthLatitudeDeg = baseTile.NorthLatitudeDeg,
                WestLongitudeDeg = baseTile.WestLongitudeDeg,
                EastLongitudeDeg = baseTile.EastLongitudeDeg,
                CenterLatitudeDeg = (baseTile.SouthLatitudeDeg +
                    baseTile.NorthLatitudeDeg) * 0.5,
                CenterLongitudeDeg = NormalizeLongitude((baseTile.WestLongitudeDeg +
                    baseTile.EastLongitudeDeg) * 0.5),
                ViewDistanceMeters = double.MaxValue,
                RequestSequence = DateTime.UtcNow.Ticks,
                BodyGeneration = plan.LastVisitedUtcTicks,
                TerrainGeneration = plan.Generation,
                DatabaseGeneration = database.RequestGeneration,
                WorkOwner = AERISTerrainWorkOwner.PreloadBuilder,
                ReadLane = AERISTerrainReadLane.Background,
                Lane = AERISTerrainRequestLane.Background,
                Priority = AERISTerrainTilePriority.Low,
                Visible = false
            };
        }

        void CommitGeneratedHighDensityCoastline(BodyPlan plan,
            AERISTerrainHeightTile sampledTile, bool final)
        {
            if (!final || plan == null || sampledTile == null) return;
            string id = sampledTile.Key.StableId;
            AERISTerrainHeightTile baseTile;
            lock (sync)
            {
                if (!pendingCoastlineBaseTiles.TryGetValue(id, out baseTile) ||
                    baseTile == null) return;
                pendingCoastlineBaseTiles.Remove(id);
            }
            if (!IsBuilderRequestCurrent(plan, new AERISTerrainTileRequest
                {
                    Key = sampledTile.Key,
                    WorkOwner = AERISTerrainWorkOwner.PreloadBuilder,
                    TerrainGeneration = plan.Generation,
                    DatabaseGeneration = database.RequestGeneration
                })) return;

            float[] segments = AERISTerrainCoastlineExtractor.Build(sampledTile);
            AERISTerrainHeightTile upgraded = baseTile.CloneImmutable();
            upgraded.HighDensityCoastlineResolution =
                AERISTerrainCoastlineExtractor.HighDensityResolution;
            upgraded.HighDensityCoastlineSegments = segments ?? new float[0];
            upgraded.CreatedUtcTicks = DateTime.UtcNow.Ticks;
            upgraded.Source = AERISTerrainTileSource.PreloadBuilderGenerated;
            upgraded.SamplingComplete = true;
            upgraded.IsPreview = false;
            CommitGeneratedTile(plan, upgraded, true);
            plan.CoastlineScannedWithoutUpgrade = 0L;
            stateDirty = true;
            AERISLogger.Info("[PRELOAD_COAST_HD] body=" + plan.BodyName +
                "; event=READY; tile=" + id + "; resolution=" +
                AERISTerrainCoastlineExtractor.HighDensityResolution +
                "; segments=" + (segments == null ? 0 : segments.Length / 4));
        }

        int PendingCoastlineSamplingCount(string bodyName)
        {
            lock (sync) return PendingCoastlineSamplingCountLocked(bodyName);
        }

        int PendingCoastlineSamplingCountLocked(string bodyName)
        {
            int count = 0;
            foreach (KeyValuePair<string, AERISTerrainHeightTile> pair in
                pendingCoastlineBaseTiles)
            {
                AERISTerrainHeightTile tile = pair.Value;
                if (tile != null && string.Equals(tile.Key.BodyName, bodyName,
                    StringComparison.OrdinalIgnoreCase)) count++;
            }
            return count;
        }

        void ClearPendingCoastlineForBody(string bodyName)
        {
            lock (sync)
            {
                var remove = new List<string>();
                foreach (KeyValuePair<string, AERISTerrainHeightTile> pair in
                    pendingCoastlineBaseTiles)
                {
                    AERISTerrainHeightTile tile = pair.Value;
                    if (string.IsNullOrEmpty(bodyName) || (tile != null &&
                        string.Equals(tile.Key.BodyName, bodyName,
                            StringComparison.OrdinalIgnoreCase)))
                        remove.Add(pair.Key);
                }
                for (int i = 0; i < remove.Count; i++)
                    pendingCoastlineBaseTiles.Remove(remove[i]);
                if (string.IsNullOrEmpty(bodyName) || string.Equals(
                    coastlineScanBodyName, bodyName, StringComparison.OrdinalIgnoreCase))
                {
                    coastlineScanInFlight = false;
                    coastlineScanBodyName = string.Empty;
                }
            }
        }

        void SchedulePendingEncodes()
        {
            if (disposed || HighLogic.LoadedSceneIsFlight) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return;
            int workers = ResolveWorkerCount();
            int maximumActive = ResolveEncodeCommitLimit(workers);
            var ready = new List<PendingEncodeWork>();
            lock (sync)
            {
                int available = Math.Max(0, maximumActive - inFlightEncodes.Count);
                if (available <= 0 || pendingEncodes.Count == 0) return;
                var keys = new List<string>(pendingEncodes.Keys);
                for (int i = 0; i < keys.Count && ready.Count < available; i++)
                {
                    PendingEncodeWork work;
                    if (!pendingEncodes.TryGetValue(keys[i], out work) || work == null)
                        continue;
                    pendingEncodes.Remove(keys[i]);
                    inFlightEncodes.Add(keys[i]);
                    ready.Add(work);
                }
            }
            for (int i = 0; i < ready.Count; i++)
                ScheduleEncode(ready[i], runtime);
        }

        void ScheduleEncode(PendingEncodeWork work, AERISPerformanceRuntime runtime)
        {
            if (work == null || runtime == null || runtime.Scheduler == null) return;
            work.Attempts++;
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute,
                "preload-encode:" + work.StableId, runtime.CaptureStamp(), context =>
                {
                    return AERISTerrainPreloadCodec.Encode(work.Tile, work.PqsHash,
                        work.GameDataHash, work.DatabaseGeneration,
                        AERISTerrainCodecId.Deflate);
                }, value =>
                {
                    AERISTerrainPreloadEncodedTile encoded = value as
                        AERISTerrainPreloadEncodedTile;
                    lock (sync) inFlightEncodes.Remove(work.StableId);
                    if (encoded != null && IsEncodeCurrent(work))
                        QueueEncodedTile(work, encoded);
                    else RequeueEncode(work);
                    SchedulePendingEncodes();
                    ScheduleReadyChunkBatches(Time.realtimeSinceStartup, false);
                }, false);
            if (!accepted)
            {
                work.Attempts = Math.Max(0, work.Attempts - 1);
                encodeAdmissionBackpressure++;
                lock (sync) inFlightEncodes.Remove(work.StableId);
                RequeueEncode(work);
            }
        }

        int ResolveEncodeCommitLimit(int workers)
        {
            int scaled = Math.Max(StandardEncodeCommitFloor, workers * 2);
            return Math.Min(StandardEncodeCommitCeiling, scaled);
        }

        int ResolveWriteCommitLimit()
        {
            return 1;
        }

        bool IsEncodeCurrent(PendingEncodeWork work)
        {
            return !disposed && !HighLogic.LoadedSceneIsFlight && work != null &&
                work.Plan != null && !work.Plan.Paused && !work.Plan.Cancelled &&
                work.Plan.Generation == work.PlanGeneration &&
                string.Equals(work.Plan.EnvironmentHash, work.PqsHash,
                    StringComparison.Ordinal);
        }

        void RequeueEncode(PendingEncodeWork work)
        {
            if (work == null) return;
            lock (sync)
            {
                if (!disposed && !HighLogic.LoadedSceneIsFlight &&
                    work.Attempts < 3 && work.Plan != null &&
                    !work.Plan.Paused && !work.Plan.Cancelled)
                {
                    pendingEncodes[work.StableId] = work;
                    return;
                }
                pendingWrites.Remove(work.StableId);
                failedSinceStart++;
            }
        }

        void QueueEncodedTile(PendingEncodeWork work,
            AERISTerrainPreloadEncodedTile encoded)
        {
            if (work == null || encoded == null) return;
            string chunkId = database.ChunkIdFor(encoded.Key);
            lock (sync)
            {
                PendingChunkBatch batch;
                if (!pendingChunkBatches.TryGetValue(chunkId, out batch) ||
                    batch == null)
                {
                    batch = new PendingChunkBatch
                    {
                        Plan = work.Plan,
                        ChunkId = chunkId,
                        FirstQueuedRealtime = Time.realtimeSinceStartup
                    };
                    pendingChunkBatches[chunkId] = batch;
                }
                batch.Tiles[work.StableId] = encoded.CloneImmutable();
            }
        }

        void ScheduleReadyChunkBatches(float now, bool force)
        {
            if (disposed || HighLogic.LoadedSceneIsFlight) return;
            List<PendingChunkBatch> ready = null;
            lock (sync)
            {
                int maximumWriteJobs = ResolveWriteCommitLimit();
                if (activeWriteJobs >= maximumWriteJobs) return;
                int chunksPerSuperBatch = 32;
                var keys = new List<string>(pendingChunkBatches.Keys);
                for (int i = 0; i < keys.Count; i++)
                {
                    PendingChunkBatch batch;
                    if (!pendingChunkBatches.TryGetValue(keys[i], out batch) ||
                        batch == null || inFlightChunkWrites.Contains(keys[i])) continue;
                    bool standardAgeDue = now - batch.FirstQueuedRealtime >= 0.35f;
                    bool due = force || batch.Tiles.Count >= 8 ||
                        standardAgeDue;
                    if (!due) continue;
                    if (ready == null) ready = new List<PendingChunkBatch>();
                    pendingChunkBatches.Remove(keys[i]);
                    inFlightChunkWrites.Add(keys[i]);
                    ready.Add(batch);
                    if (ready.Count >= chunksPerSuperBatch) break;
                }
                if (ready == null || ready.Count == 0) return;
                activeWriteJobs++;
            }
            ScheduleChunkSuperBatch(ready);
        }

        void ScheduleChunkSuperBatch(IList<PendingChunkBatch> batches)
        {
            if (batches == null || batches.Count == 0) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null)
            {
                RequeueChunkBatches(batches);
                return;
            }
            var tiles = new List<AERISTerrainPreloadEncodedTile>();
            var ids = new List<string>();
            var chunkIds = new List<string>();
            for (int i = 0; i < batches.Count; i++)
            {
                PendingChunkBatch batch = batches[i];
                if (batch == null) continue;
                chunkIds.Add(batch.ChunkId);
                foreach (KeyValuePair<string, AERISTerrainPreloadEncodedTile> pair
                    in batch.Tiles)
                {
                    ids.Add(pair.Key);
                    tiles.Add(pair.Value);
                }
            }
            if (tiles.Count == 0)
            {
                RequeueChunkBatches(batches);
                return;
            }
            string batchId = (++writeBatchSequence).ToString(
                CultureInfo.InvariantCulture);
            var payload = new ChunkWritePayload
            {
                BatchId = batchId,
                ChunkIds = chunkIds.ToArray(),
                Tiles = tiles.ToArray(),
                StableIds = ids.ToArray(),
                SourceBatches = new List<PendingChunkBatch>(batches).ToArray()
            };
            Stopwatch queuedAt = Stopwatch.StartNew();
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.ArchiveCompression,
                "preload-terrain-super-batch:" + batchId, runtime.CaptureStamp(),
                context =>
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    long bytes;
                    double ratio;
                    int chunks;
                    bool ok = database.SaveEncodedBatch(payload.Tiles, out bytes,
                        out ratio, out chunks);
                    watch.Stop();
                    return new object[] { ok, bytes, ratio,
                        watch.Elapsed.TotalSeconds, queuedAt.Elapsed.TotalMilliseconds,
                        chunks, payload.Tiles.Length };
                }, value =>
                {
                    object[] result = value as object[];
                    bool ok = result != null && result.Length >= 7 && (bool)result[0];
                    ReleaseWriteBatch(payload);
                    if (ok)
                    {
                        lock (sync)
                            for (int i = 0; i < payload.StableIds.Length; i++)
                                pendingWrites.Remove(payload.StableIds[i]);
                        long bytes = (long)result[1];
                        double ratio = (double)result[2];
                        double seconds = Math.Max(0.000001, (double)result[3]);
                        int chunks = (int)result[5];
                        int tileCount = (int)result[6];
                        writeMbpsEma = Ema(writeMbpsEma,
                            bytes / seconds / 1048576.0, 0.20);
                        compressionRatioEma = Ema(compressionRatioEma, ratio, 0.20);
                        chunkBatchTilesEma = Ema(chunkBatchTilesEma, tileCount, 0.25);
                        chunkRewriteAmplificationEma = Ema(
                            chunkRewriteAmplificationEma,
                            chunks / (double)Math.Max(1, tileCount), 0.25);
                        chunkFlushMillisecondsEma = Ema(chunkFlushMillisecondsEma,
                            seconds * 1000.0, 0.25);
                        lastWriteBatchChunks = chunks;
                        lastWriteBatchTiles = tileCount;
                        completedSinceStart += tileCount;
                        stateDirty = true;
                    }
                    else
                    {
                        failedSinceStart += payload.Tiles.Length;
                        RequeueChunkBatches(payload.SourceBatches, false);
                    }
                    ScheduleReadyChunkBatches(Time.realtimeSinceStartup, false);
                }, false);
            if (!accepted)
            {
                writeAdmissionBackpressure++;
                RequeueChunkBatches(payload.SourceBatches);
            }
        }

        void ReleaseWriteBatch(ChunkWritePayload payload)
        {
            lock (sync)
            {
                activeWriteJobs = Math.Max(0, activeWriteJobs - 1);
                if (payload != null && payload.ChunkIds != null)
                    for (int i = 0; i < payload.ChunkIds.Length; i++)
                        inFlightChunkWrites.Remove(payload.ChunkIds[i]);
            }
        }

        void RequeueChunkBatches(IList<PendingChunkBatch> batches)
        {
            RequeueChunkBatches(batches, true);
        }

        void RequeueChunkBatches(IList<PendingChunkBatch> batches,
            bool releaseActiveJob)
        {
            if (batches == null) return;
            lock (sync)
            {
                if (releaseActiveJob) activeWriteJobs =
                    Math.Max(0, activeWriteJobs - 1);
                for (int i = 0; i < batches.Count; i++)
                {
                    PendingChunkBatch batch = batches[i];
                    if (batch == null) continue;
                    inFlightChunkWrites.Remove(batch.ChunkId);
                    PendingChunkBatch existing;
                    if (!pendingChunkBatches.TryGetValue(batch.ChunkId, out existing) ||
                        existing == null)
                    {
                        batch.FirstQueuedRealtime = Time.realtimeSinceStartup;
                        pendingChunkBatches[batch.ChunkId] = batch;
                        continue;
                    }
                    foreach (KeyValuePair<string, AERISTerrainPreloadEncodedTile>
                        pair in batch.Tiles) existing.Tiles[pair.Key] = pair.Value;
                    existing.FirstQueuedRealtime = Math.Min(
                        existing.FirstQueuedRealtime, batch.FirstQueuedRealtime);
                }
            }
        }

        bool IsBuilderRequestCurrent(BodyPlan plan, AERISTerrainTileRequest request)
        {
            return !disposed && !HighLogic.LoadedSceneIsFlight && plan != null &&
                request != null && request.WorkOwner == AERISTerrainWorkOwner.PreloadBuilder &&
                !plan.Paused && !plan.Cancelled &&
                request.TerrainGeneration == plan.Generation &&
                request.DatabaseGeneration == database.RequestGeneration &&
                string.Equals(plan.BodyName, request.Key.BodyName,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(plan.EnvironmentHash, request.Key.EnvironmentHash,
                    StringComparison.Ordinal);
        }

        bool TrySelectPlan(CelestialBody currentBody, out BodyPlan selected,
            out CelestialBody body)
        {
            selected = null;
            body = null;
            lock (sync)
            {
                foreach (BodyPlan candidate in plans.Values)
                {
                    if (candidate == null || candidate.Paused || candidate.Cancelled ||
                        candidate.Priority == AERISTerrainBodyPriority.Disabled ||
                        BodyAtStorageLimit(candidate)) continue;
                    bool manualEligible = candidate.ManualRequested;
                    bool automaticEligible =
                        mode != AERISTerrainPreloadMode.Off &&
                        mode != AERISTerrainPreloadMode.Manual &&
                        !AutomaticTargetComplete(candidate);
                    if (!manualEligible && !automaticEligible) continue;
                    CelestialBody candidateBody = FindBody(candidate.BodyName);
                    if (candidateBody == null ||
                        !AERISTerrainTileSystem.BodyHasSolidSurface(candidateBody)) continue;
                    if (selected == null || ComparePlans(candidate, selected, currentBody) < 0)
                    {
                        selected = candidate;
                        body = candidateBody;
                    }
                }
            }
            return selected != null && body != null;
        }

        bool TryAdvanceAutomaticPlan(CelestialBody currentBody)
        {
            if (mode == AERISTerrainPreloadMode.Off ||
                mode == AERISTerrainPreloadMode.Manual || HasManualRequest()) return false;
            BodyPlan selected = null;
            lock (sync)
            {
                foreach (BodyPlan candidate in plans.Values)
                {
                    if (candidate == null || candidate.Paused || candidate.Cancelled ||
                        candidate.QualityOverride ||
                        candidate.Priority < AERISTerrainBodyPriority.High ||
                        candidate.QualityLimit != AERISTerrainTileLod.Far ||
                        !AutomaticTargetComplete(candidate) ||
                        !HasPreloadPointsLocked(candidate.BodyName) ||
                        BodyAtStorageLimit(candidate)) continue;
                    CelestialBody body = FindBody(candidate.BodyName);
                    if (body == null ||
                        !AERISTerrainTileSystem.BodyHasSolidSurface(body)) continue;
                    if (selected == null ||
                        ComparePlans(candidate, selected, currentBody) < 0)
                        selected = candidate;
                }
                if (selected == null) return false;
                selected.QualityLimit = AERISTerrainTileLod.Land;
                selected.AutomaticPointRefinementOnly = true;
                selected.EstimatedTargetTiles = 0L;
                InvalidateAutomaticCompletion(selected);
                ResetScanState(selected);
                selected.Generation++;
                stateDirty = true;
            }
            status = "PRELOAD AUTO REFINE SITES: " + selected.BodyName;
            AERISLogger.Info("[PRELOAD_AUTO] body=" + selected.BodyName +
                "; event=PROMOTE; from=FAR_GLOBAL; to=LAND_SITES; " +
                "routeGlobal=False");
            return true;
        }

        bool HasPreloadPointsLocked(string bodyName)
        {
            for (int i = 0; i < points.Count; i++)
                if (string.Equals(points[i].BodyName, bodyName,
                    StringComparison.OrdinalIgnoreCase) &&
                    points[i].MaximumLod >= AERISTerrainTileLod.Local) return true;
            return false;
        }

        static bool AutomaticTargetComplete(BodyPlan plan)
        {
            return plan != null && plan.AutomaticComplete &&
                plan.CompletedQualityLimit == plan.QualityLimit &&
                plan.CompletedPointRefinementOnly ==
                    plan.AutomaticPointRefinementOnly &&
                string.Equals(plan.CompletedEnvironmentHash,
                    plan.EnvironmentHash, StringComparison.Ordinal) &&
                plan.CoastlineComplete &&
                plan.CompletedCoastlineFormatVersion ==
                    AERISTerrainCoastlineExtractor.HighDensityFormatVersion &&
                string.Equals(plan.CompletedCoastlineEnvironmentHash,
                    plan.EnvironmentHash, StringComparison.Ordinal);
        }

        static void MarkAutomaticComplete(BodyPlan plan)
        {
            if (plan == null) return;
            plan.AutomaticComplete = true;
            plan.CompletedQualityLimit = plan.QualityLimit;
            plan.CompletedPointRefinementOnly =
                plan.AutomaticPointRefinementOnly;
            plan.CompletedEnvironmentHash = plan.EnvironmentHash ?? string.Empty;
        }

        static void InvalidateAutomaticCompletion(BodyPlan plan)
        {
            if (plan == null) return;
            plan.AutomaticComplete = false;
            plan.CompletedQualityLimit = AERISTerrainTileLod.Global;
            plan.CompletedPointRefinementOnly = false;
            plan.CompletedEnvironmentHash = string.Empty;
        }

        static string AutomaticTargetLabel(BodyPlan plan)
        {
            if (plan == null) return "UNKNOWN";
            return plan.AutomaticPointRefinementOnly
                ? "LAND_SITES" : plan.QualityLimit.ToString().ToUpperInvariant() + "_GLOBAL";
        }

        static int ComparePlans(BodyPlan a, BodyPlan b, CelestialBody currentBody)
        {
            bool aCurrent = currentBody != null && string.Equals(a.BodyName,
                currentBody.name, StringComparison.OrdinalIgnoreCase);
            bool bCurrent = currentBody != null && string.Equals(b.BodyName,
                currentBody.name, StringComparison.OrdinalIgnoreCase);
            int current = (bCurrent ? 1 : 0).CompareTo(aCurrent ? 1 : 0);
            if (current != 0) return current;
            int priority = ((int)b.Priority).CompareTo((int)a.Priority);
            if (priority != 0) return priority;
            int recent = b.LastVisitedUtcTicks.CompareTo(a.LastVisitedUtcTicks);
            if (recent != 0) return recent;
            return string.Compare(a.BodyName, b.BodyName,
                StringComparison.OrdinalIgnoreCase);
        }

        bool ResolveBudget(bool idle, out float qps, out int samples,
            out float milliseconds)
        {
            qps = 0f;
            samples = 0;
            milliseconds = 0f;
            bool manual = HasManualRequest();
            if (mode == AERISTerrainPreloadMode.Off && !manual)
            {
                status = "PRELOAD OFF";
                return false;
            }
            if (mode == AERISTerrainPreloadMode.Manual && !manual)
            {
                status = "PRELOAD MANUAL / READY";
                return false;
            }
            if (mode == AERISTerrainPreloadMode.IdleOnly && !idle && !manual)
            {
                status = "PRELOAD WAITING FOR IDLE";
                return false;
            }

            // CP2.5 standard producer envelope. It is calculated unchanged for regression
            // compatibility; Candidate 8 may subsequently load-shed only the amount of
            // background work performed per frame. Tile resolution and generated data are
            // unchanged, so ND visual quality is not reduced.
            int standardWorkers = ResolveWorkerCount();
            float frameMilliseconds = Mathf.Max(1f,
                Time.unscaledDeltaTime * 1000f);
            milliseconds = Mathf.Clamp(frameMilliseconds * 0.70f, 8f, 24f);
            qps = 100000f;
            int standardDynamicSamples = pqsSampleCostEmaMs > 0.000001 ?
                Mathf.FloorToInt((float)(milliseconds / pqsSampleCostEmaMs)) :
                standardWorkers * 256;
            samples = Math.Max(256, Math.Min(4096,
                Math.Max(standardWorkers * 128, standardDynamicSamples)));
            float loadScale = ResolvePerformanceLoadScale();
            if (loadScale < 0.999f)
            {
                milliseconds = Math.Max(1.0f, milliseconds * loadScale);
                qps = Math.Max(1.0f, qps * loadScale);
                int loadLimitedSamples = pqsSampleCostEmaMs > 0.000001 ?
                    Mathf.FloorToInt((float)(milliseconds / pqsSampleCostEmaMs)) :
                    Mathf.RoundToInt(samples * loadScale);
                samples = Math.Max(32, Math.Min(samples,
                    Math.Max(32, loadLimitedSamples)));
                status = "PRELOAD STANDARD / RUNTIME LOAD SHED " +
                    loadScale.ToString("0.00", CultureInfo.InvariantCulture);
            }
            else status = "PRELOAD STANDARD / VALIDATED CP2.5 ENVELOPE";
            return samples > 0;
        }

        // Frozen CP2 static-acceptance marker retained for source-history regression
        // verification. The active CP2.5 runtime uses the single STANDARD envelope.
        static float FrozenCp2AdaptiveLoadContract(float milliseconds,
            float loadScale)
        {
            float qps = Mathf.Lerp(80f, 1200f, loadScale);
            return Math.Min(qps, milliseconds * loadScale);
        }

        // Frozen CP2 fast-path identifiers retained for regression traceability:
        // database.SaveBatch / preload-terrain-chunk-batch:

        void UpdatePqsCostTelemetry(float elapsedSeconds)
        {
            int batchSamples = blockPipeline.LastBatchSamples;
            double batchMilliseconds = blockPipeline.LastBatchMilliseconds;
            if (batchSamples > 0 && batchMilliseconds >= 0.0)
            {
                pqsSampleCostEmaMs = Ema(pqsSampleCostEmaMs,
                    batchMilliseconds / batchSamples, 0.12);
                double seconds = Math.Max(0.000001, elapsedSeconds);
                pqsSamplesPerSecondEma = Ema(pqsSamplesPerSecondEma,
                    batchSamples / seconds, 0.12);
            }
        }

        float ResolvePerformanceLoadScale()
        {
            if (performance == null) return 1f;
            if (performance.WorkerBacklogged || performance.FrameTimeEmaMs >= 45f ||
                performance.NdMainThreadEmaMs >= 3.0f) return 0.25f;
            if (performance.FrameTimeEmaMs >= 33f ||
                performance.NdMainThreadEmaMs >= 1.75f) return 0.50f;
            if (performance.FrameTimeEmaMs >= 25f ||
                performance.NdMainThreadEmaMs >= 0.80f) return 0.75f;
            return 1f;
        }

        void RefreshBodies(CelestialBody currentBody, float now)
        {
            if (now < nextBodyRefreshRealtime) return;
            nextBodyRefreshRealtime = now + 5f;
            try
            {
                if (FlightGlobals.Bodies == null) return;
                foreach (CelestialBody body in FlightGlobals.Bodies)
                {
                    if (body == null || string.IsNullOrEmpty(body.name)) continue;
                    if (!AERISTerrainTileSystem.BodyHasSolidSurface(body))
                    {
                        if (database != null)
                            database.SetBodyRetentionPriority(body.name,
                                AERISTerrainBodyPriority.Disabled);
                        continue;
                    }
                    BodyPlan plan = GetOrCreatePlan(body.name);
                    if (!plan.PriorityOverride)
                    {
                        plan.Priority = AutomaticPriority(body, currentBody);
                        if (plan.Priority < AERISTerrainBodyPriority.High &&
                            plan.LastVisitedUtcTicks > 0L)
                        {
                            long ageTicks = Math.Max(0L, DateTime.UtcNow.Ticks -
                                plan.LastVisitedUtcTicks);
                            if (ageTicks <= TimeSpan.FromDays(30.0).Ticks)
                                plan.Priority = AERISTerrainBodyPriority.High;
                            else if (plan.Priority < AERISTerrainBodyPriority.Normal)
                                plan.Priority = AERISTerrainBodyPriority.Normal;
                        }
                    }
                    if (AERISTerrainTileSystem.BodyHasSolidSurface(body))
                        EnsureEnvironment(plan, body);
                    if (database != null)
                        database.SetBodyRetentionPriority(body.name, plan.Priority);
                }
            }
            catch { }
            if (currentBody != null) NoteVisitedBody(currentBody.name);
        }

        AERISTerrainBodyPriority AutomaticPriority(CelestialBody body,
            CelestialBody currentBody)
        {
            if (body == null || !AERISTerrainTileSystem.BodyHasSolidSurface(body))
                return AERISTerrainBodyPriority.Disabled;
            if (currentBody != null && string.Equals(body.name, currentBody.name,
                StringComparison.OrdinalIgnoreCase)) return AERISTerrainBodyPriority.High;
            if (string.Equals(body.name, "Kerbin", StringComparison.OrdinalIgnoreCase))
                return AERISTerrainBodyPriority.High;
            lock (sync)
                for (int i = 0; i < points.Count; i++)
                    if (string.Equals(points[i].BodyName, body.name,
                        StringComparison.OrdinalIgnoreCase) && points[i].Priority >= 50)
                        return AERISTerrainBodyPriority.High;
            if (string.Equals(body.name, "Mun", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(body.name, "Minmus", StringComparison.OrdinalIgnoreCase))
                return AERISTerrainBodyPriority.Normal;
            return AERISTerrainBodyPriority.Low;
        }

        void EnsureEnvironment(BodyPlan plan, CelestialBody body)
        {
            string environment = AERISTerrainTileSystem.EnvironmentHashForBody(body);
            if (string.Equals(plan.EnvironmentHash, environment,
                StringComparison.Ordinal)) return;
            plan.EnvironmentHash = environment;
            InvalidateAutomaticCompletion(plan);
            InvalidateCoastlineCompletion(plan);
            plan.Generation++;
            ResetScanState(plan);
            stateDirty = true;
            string validationKey = body.name + "|" + environment;
            if (validatedEnvironments.Contains(validationKey)) return;
            validatedEnvironments.Add(validationKey);
            ScheduleEnvironmentInvalidation(body.name, environment);
        }

        void ScheduleEnvironmentInvalidation(string bodyName, string environmentHash)
        {
            if (database == null) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            runtime.Scheduler.SubmitLatest(AERISRuntimeLane.ArchiveCompression,
                "preload-environment:" + bodyName, runtime.CaptureStamp(), context =>
                {
                    return database.InvalidateBodyEnvironment(bodyName, environmentHash);
                }, value => { }, false);
        }

        void ScheduleIndexRecovery()
        {
            if (database == null) return;
            lock (sync)
            {
                if (indexRecoveryInFlight) return;
                indexRecoveryInFlight = true;
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null)
            {
                lock (sync) indexRecoveryInFlight = false;
                return;
            }
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.ArchiveCompression, "preload-index-recovery",
                runtime.CaptureStamp(), context =>
                {
                    int valid;
                    int invalid;
                    bool ok = database.RecoverIndexFromChunks(out valid, out invalid);
                    return new int[] { ok ? 1 : 0, valid, invalid };
                }, value =>
                {
                    lock (sync) indexRecoveryInFlight = false;
                    int[] result = value as int[];
                    bool ok = result != null && result.Length >= 3 && result[0] == 1;
                    int valid = result == null || result.Length < 2 ? 0 : result[1];
                    int invalid = result == null || result.Length < 3 ? 0 : result[2];
                    status = ok ? "PRELOAD INDEX RECOVERED " + valid +
                        " TILES / INVALID " + invalid : "PRELOAD INDEX RECOVERY FAILED";
                }, false);
            if (!accepted) lock (sync) indexRecoveryInFlight = false;
        }

        void ProcessOperationIfPossible(bool isFlight)
        {
            if (isFlight) return;
            Operation operation;
            lock (sync)
            {
                if (operationInFlight || operations.Count == 0) return;
                operation = operations.Dequeue();
                operationInFlight = true;
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null)
            {
                lock (sync) operationInFlight = false;
                return;
            }
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.ArchiveCompression,
                "preload-maintenance:" + operation.Kind + ":" + operation.BodyName,
                runtime.CaptureStamp(), context =>
                {
                    int valid;
                    int invalid;
                    switch (operation.Kind)
                    {
                        case OperationKind.Delete:
                            return database.DeleteBody(operation.BodyName);
                        case OperationKind.Rebuild:
                            database.DeleteBody(operation.BodyName);
                            return true;
                        default:
                            return database.VerifyAndRepair(operation.BodyName,
                                out valid, out invalid);
                    }
                }, value =>
                {
                    lock (sync) operationInFlight = false;
                    bool ok = value is bool && (bool)value;
                    BodyPlan plan = operation.Kind == OperationKind.Rebuild ?
                        GetSupportedBodyPlan(operation.BodyName) : GetOrCreatePlan(operation.BodyName);
                    if (operation.Kind == OperationKind.Rebuild && plan != null)
                    {
                        ResetScanState(plan);
                        InvalidateAutomaticCompletion(plan);
                        InvalidateCoastlineCompletion(plan);
                        plan.ManualRequested = true;
                        plan.Cancelled = false;
                        plan.Paused = false;
                    }
                    status = "PRELOAD " + operation.Kind.ToString().ToUpperInvariant() +
                        (ok ? " COMPLETE: " : " FAILED: ") + operation.BodyName;
                    stateDirty = true;
                }, false);
            if (!accepted)
            {
                lock (sync)
                {
                    operationInFlight = false;
                    operations.Enqueue(operation);
                }
            }
        }

        void EnqueueOperation(OperationKind kind, string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (sync) operations.Enqueue(new Operation { Kind = kind,
                BodyName = bodyName });
            status = "PRELOAD " + kind.ToString().ToUpperInvariant() + " QUEUED: " +
                bodyName;
        }

        void DetectInput(float now)
        {
            Vector3 mouse = Input.mousePosition;
            bool moved = (mouse - lastMousePosition).sqrMagnitude > 0.25f;
            if (Input.anyKey || moved || Input.GetMouseButton(0) || Input.GetMouseButton(1) ||
                Input.GetMouseButton(2)) lastInputRealtime = now;
            lastMousePosition = mouse;
        }

        bool HasManualRequest()
        {
            lock (sync)
                foreach (BodyPlan plan in plans.Values)
                    if (plan != null && plan.ManualRequested && !plan.Paused &&
                        !plan.Cancelled) return true;
            return false;
        }

        BodyPlan GetOrCreatePlan(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return null;
            lock (sync)
            {
                BodyPlan plan;
                if (plans.TryGetValue(bodyName, out plan)) return plan;
                plan = new BodyPlan { BodyName = bodyName };
                if (string.Equals(bodyName, "Kerbin", StringComparison.OrdinalIgnoreCase))
                    plan.Priority = AERISTerrainBodyPriority.High;
                plans[bodyName] = plan;
                stateDirty = true;
                return plan;
            }
        }

        int PendingWriteCount()
        {
            lock (sync) return pendingWrites.Count;
        }

        int PendingWorkCount()
        {
            // pendingWrites is the unique stable-id set spanning encode and SSD stages.
            lock (sync) return pendingWrites.Count;
        }

        void UpdateBottleneck(bool producerQueued)
        {
            int encodeQueued;
            int encodeActive;
            int writeQueued;
            int writeActive;
            lock (sync)
            {
                encodeQueued = pendingEncodes.Count;
                encodeActive = inFlightEncodes.Count;
                writeQueued = pendingChunkBatches.Count;
                writeActive = activeWriteJobs;
            }
            AERISRuntimeTelemetrySnapshot scheduler = SnapshotScheduler();
            float now = Time.realtimeSinceStartup;
            if (scheduler != null && scheduler.RequiredCompletionQueueDepth >= 224)
                bottleneck = "RESULT BACKPRESSURE";
            else if (!producerQueued && blockPipeline.Count == 0 && encodeQueued == 0 &&
                encodeActive == 0 && writeQueued == 0 && writeActive == 0)
                bottleneck = "NO WORK";
            else if (writeQueued >= 8 || writeActive > 0 &&
                writeMbpsEma < 1.0) bottleneck = "SSD WRITE";
            else if (encodeQueued + encodeActive >= 8)
                bottleneck = "CPU COMPRESSION";
            else if (blockPipeline.Count < Math.Max(2,
                blockPipeline.ActiveTileLimit / 8)) bottleneck = "PQS PRODUCER";
            else bottleneck = "PIPELINE BALANCED";

            if (!string.Equals(lastLoggedBottleneck, bottleneck,
                StringComparison.Ordinal))
            {
                lastLoggedBottleneck = bottleneck;
                AERISLogger.Info("[PRELOAD_PIPELINE] bottleneck=" + bottleneck +
                    "; mode=STANDARD" +
                    "; pqsTiles=" + blockPipeline.Count +
                    "; encodeQueued=" + encodeQueued +
                    "; encodeActive=" + encodeActive +
                    "; writeChunks=" + writeQueued +
                    "; writeJobs=" + writeActive);
            }
            if (now >= nextThroughputLogRealtime)
            {
                nextThroughputLogRealtime = now + 5f;
                AERISLogger.Info("[PRELOAD_THROUGHPUT] mode=STANDARD" +
                    "; pqsMs=" + blockPipeline.LastBatchMilliseconds.ToString(
                        "0.000", CultureInfo.InvariantCulture) +
                    "; pqsSampleRate=" + pqsSamplesPerSecondEma.ToString(
                        "0", CultureInfo.InvariantCulture) +
                    "; pipeline=" + blockPipeline.Count + "/" +
                        blockPipeline.ActiveTileLimit +
                    "; outstanding=" + blockPipeline.OutstandingBlocks + "/" +
                        blockPipeline.OutstandingBlockLimit +
                    "; resultRequired=" + (scheduler == null ? 0 :
                        scheduler.RequiredCompletionQueueDepth) +
                    "; requiredRejected=" + (scheduler == null ? 0L :
                        scheduler.RequiredRejected) +
                    "; requiredDropped=" + (scheduler == null ? 0L :
                        scheduler.RequiredDropped) +
                    "; encodeRequired=" + encodeQueued + "/" + encodeActive +
                        "/" + ResolveEncodeCommitLimit(ResolveWorkerCount()) +
                        "; encodeReject=" + encodeAdmissionBackpressure +
                    "; ssdRequired=" + writeQueued + "/" + writeActive +
                        "/" + ResolveWriteCommitLimit() +
                        "; writeReject=" + writeAdmissionBackpressure +
                    "; writeMiBs=" + writeMbpsEma.ToString(
                        "0.0", CultureInfo.InvariantCulture) +
                    "; lastSuperBatch=" + lastWriteBatchChunks + "chunks/" +
                        lastWriteBatchTiles + "tiles; gpu=" +
                        ResolveGpuPreloadStage());
            }
        }

        void ApplyStandardSchedulerState(bool active)
        {
            if (standardSchedulerApplied == active) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return;
            runtime.Scheduler.SetStandardPreloadThroughput(active);
            standardSchedulerApplied = active;
        }

        AERISRuntimeTelemetrySnapshot SnapshotScheduler()
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            return runtime == null || runtime.Scheduler == null ? null :
                runtime.Scheduler.SnapshotTelemetry();
        }

        bool MonitorPreloadHealth(float now)
        {
            AERISRuntimeTelemetrySnapshot scheduler = SnapshotScheduler();
            if (scheduler != null && scheduler.RequiredDropped >
                preloadRequiredDropBaseline)
            {
                RecoverStandardPreload("SCHEDULER_REQUIRED_DROP", now,
                    scheduler.RequiredDropped);
                return true;
            }
            long blocks = blockPipeline.BlocksCompleted;
            int tiles = database == null ? 0 : database.Count;
            if (blocks != preloadLastBlocksCompleted || tiles != preloadLastTilesComplete)
            {
                preloadLastBlocksCompleted = blocks;
                preloadLastTilesComplete = tiles;
                preloadLastProgressRealtime = now;
                if (scheduler != null)
                    preloadRequiredDropBaseline = scheduler.RequiredDropped;
                return false;
            }
            int encodeActive;
            int writeActive;
            lock (sync)
            {
                encodeActive = inFlightEncodes.Count;
                writeActive = activeWriteJobs;
            }
            bool pending = blockPipeline.Count > 0 &&
                blockPipeline.OutstandingBlocks > 0;
            bool producerIdle = blockPipeline.LastBatchSamples == 0 &&
                blockPipeline.LastBatchMilliseconds < 0.05;
            if (pending && producerIdle && encodeActive == 0 && writeActive == 0 &&
                now - preloadLastProgressRealtime >= 6f)
            {
                RecoverStandardPreload("PIPELINE_STALL", now,
                    scheduler == null ? 0L : scheduler.RequiredDropped);
                return true;
            }
            return false;
        }

        void RecoverStandardPreload(string reason, float now, long requiredDropped)
        {
            int recovered = blockPipeline.RecoverPreloadAfterBackpressure(reason);
            if (!string.IsNullOrEmpty(activeBodyName))
            {
                BodyPlan recoveryPlan = GetOrCreatePlan(activeBodyName);
                if (recoveryPlan != null)
                {
                    ResetScanState(recoveryPlan);
                    recoveryPlan.Generation++;
                    recoveryPlan.Cancelled = false;
                    recoveryPlan.Paused = false;
                    stateDirty = true;
                }
            }
            preloadRequiredDropBaseline = requiredDropped;
            preloadLastBlocksCompleted = blockPipeline.BlocksCompleted;
            preloadLastTilesComplete = database == null ? 0 : database.Count;
            preloadLastProgressRealtime = now;
            bottleneck = string.Equals(reason, "PIPELINE_STALL",
                StringComparison.Ordinal) ? "PIPELINE STALL" : "RESULT BACKPRESSURE";
            status = "PRELOAD STANDARD RECOVERY / " + reason;
            AERISLogger.Error("[PRELOAD_RECOVERY] reason=" + reason +
                "; recoveredTiles=" + recovered +
                "; mode=STANDARD; databasePayload=UNCHANGED");
        }

        string ResolveGpuPreloadStage()
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Gpu == null)
                return "GPU STAGE UNAVAILABLE — RUNTIME NOT READY";
            if (!runtime.Gpu.Supported)
                return "GPU STAGE UNAVAILABLE — COMPUTE UNSUPPORTED";
            return "GPU COMPUTE CAPABLE — NO PRELOAD KERNEL ASSET / CPU AUTHORITATIVE";
        }

        bool OperationInFlight()
        {
            lock (sync) return operationInFlight;
        }

        bool IndexRecoveryInFlight()
        {
            lock (sync) return indexRecoveryInFlight;
        }

        static void ResetScanState(BodyPlan plan)
        {
            if (plan == null) return;
            plan.GlobalCursor = 0L;
            plan.FarCursor = 0L;
            plan.RouteCursor = 0L;
            plan.PointCursor = 0;
            plan.CoastlineCursor = 0L;
            plan.CoastlineScannedWithoutUpgrade = 0L;
            plan.GlobalScannedWithoutMiss = 0L;
            plan.FarScannedWithoutMiss = 0L;
            plan.RouteScannedWithoutMiss = 0L;
            plan.PointScannedWithoutMiss = 0;
        }

        int CountPendingForBody(string bodyName)
        {
            int count = 0;
            lock (sync)
            {
                foreach (string id in pendingWrites)
                    if (id.IndexOf("|" + bodyName + "|", StringComparison.OrdinalIgnoreCase) >= 0)
                        count++;
            }
            return count + (string.Equals(activeBodyName, bodyName,
                StringComparison.OrdinalIgnoreCase) ? blockPipeline.Count : 0);
        }

        bool BodyAtStorageLimit(BodyPlan plan)
        {
            return false;
        }

        long EstimateTargetTiles(BodyPlan plan, CelestialBody body)
        {
            if (plan == null || body == null) return 0L;
            if (plan.EstimatedTargetTiles > 0L) return plan.EstimatedTargetTiles;
            long total = CountTiles(body, AERISTerrainTileLod.Global);
            int pointCount = 0;
            lock (sync)
                for (int i = 0; i < points.Count; i++)
                    if (string.Equals(points[i].BodyName, body.name,
                        StringComparison.OrdinalIgnoreCase)) pointCount++;
            if (plan.QualityLimit >= AERISTerrainTileLod.Local) total += pointCount * 9L;
            if (plan.QualityLimit >= AERISTerrainTileLod.Land) total += pointCount * 9L;
            if (plan.QualityLimit >= AERISTerrainTileLod.Far)
                total += CountTiles(body, AERISTerrainTileLod.Far);
            if (plan.QualityLimit >= AERISTerrainTileLod.Route &&
                !plan.AutomaticPointRefinementOnly)
                total += CountTiles(body, AERISTerrainTileLod.Route);
            plan.EstimatedTargetTiles = Math.Max(1L, total);
            return plan.EstimatedTargetTiles;
        }

        static long CountTiles(CelestialBody body, AERISTerrainTileLod lod)
        {
            return (long)AERISTerrainTileSystem.LatitudeTileCountFor(body, lod) *
                AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
        }

        void UpdateTelemetry(bool idle, double qps, int samples, double milliseconds)
        {
            if (telemetry == null) return;
            telemetry.BuilderBody = activeBodyName;
            telemetry.BuilderLod = (int)activeLod;
            telemetry.BuilderTilesComplete = database == null ? 0L : database.Count;
            telemetry.BuilderTilesPending = blockPipeline.Count + PendingWorkCount();
            telemetry.BuilderPqsMilliseconds = blockPipeline.LastBatchMilliseconds;
            telemetry.BuilderWorkerUtilization = samples <= 0 ? 0.0 :
                Math.Min(1.0, blockPipeline.Count / (double)Math.Max(1,
                    blockPipeline.ActiveTileLimit));
            telemetry.BuilderWriteMbps = writeMbpsEma;
            telemetry.BuilderCompressionRatio = compressionRatioEma;
            telemetry.BuilderStorageBytes = database == null ? 0L : database.UsedBytes;
            telemetry.BuilderPqsSamplesPerSecond = pqsSamplesPerSecondEma;
            telemetry.BuilderPqsSampleCostMilliseconds = pqsSampleCostEmaMs;
            telemetry.BuilderPqsSampleCacheHits =
                blockPipeline.BoundarySampleCacheHits;
            telemetry.BuilderPqsSampleCacheMisses =
                blockPipeline.BoundarySampleCacheMisses;
            telemetry.BuilderPqsSampleCacheHitRatio =
                blockPipeline.BoundarySampleCacheHitRatio;
            telemetry.BuilderChunkBatchTiles = chunkBatchTilesEma;
            telemetry.BuilderChunkRewriteAmplification =
                chunkRewriteAmplificationEma;
            telemetry.BuilderChunkFlushMilliseconds = chunkFlushMillisecondsEma;
            telemetry.BuilderIntermediateCommitsSkipped =
                blockPipeline.IntermediateCommitsSkipped;
            lock (sync)
            {
                telemetry.BuilderEncodeQueueDepth = pendingEncodes.Count;
                telemetry.BuilderEncodeActive = inFlightEncodes.Count;
                telemetry.BuilderEncodeCommitLimit = ResolveEncodeCommitLimit(
                    ResolveWorkerCount());
                telemetry.BuilderEncodeAdmissionBackpressure =
                    encodeAdmissionBackpressure;
                telemetry.BuilderWriteQueueDepth = pendingChunkBatches.Count;
                telemetry.BuilderWriteActive = activeWriteJobs;
                telemetry.BuilderWriteCommitLimit = ResolveWriteCommitLimit();
                telemetry.BuilderWriteAdmissionBackpressure =
                    writeAdmissionBackpressure;
            }
            telemetry.BuilderWriteBatchChunks = lastWriteBatchChunks;
            telemetry.BuilderWriteBatchTiles = lastWriteBatchTiles;
            telemetry.BuilderBottleneck = bottleneck;
            telemetry.BuilderGpuPreloadStage = ResolveGpuPreloadStage();
            telemetry.BuilderOutstandingBlocks = blockPipeline.OutstandingBlocks;
            telemetry.BuilderOutstandingBlockLimit =
                blockPipeline.OutstandingBlockLimit;
            telemetry.BuilderAdmissionBackpressure =
                blockPipeline.AdmissionBackpressureCount;
            telemetry.BuilderRecoveryCount = blockPipeline.RecoveryCount;
            AERISRuntimeTelemetrySnapshot scheduler = SnapshotScheduler();
            telemetry.BuilderSchedulerRequiredRejected = scheduler == null ? 0L :
                scheduler.RequiredRejected;
            telemetry.BuilderSchedulerRequiredDropped = scheduler == null ? 0L :
                scheduler.RequiredDropped;
        }

        float ResolveIdleSeconds()
        {
            return 5f;
        }

        CelestialBody FindBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return null;
            try
            {
                if (FlightGlobals.Bodies == null) return null;
                foreach (CelestialBody body in FlightGlobals.Bodies)
                    if (body != null && string.Equals(body.name, bodyName,
                        StringComparison.OrdinalIgnoreCase)) return body;
            }
            catch { }
            return null;
        }

        void LoadState()
        {
            if (TryLoadState(statePath)) return;
            TryLoadState(statePath + ".bak");
        }

        bool TryLoadState(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
            var loaded = new Dictionary<string, BodyPlan>(
                StringComparer.OrdinalIgnoreCase);
            AERISTerrainPreloadMode loadedMode;
            try
            {
                using (var stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream))
                {
                    string magic = reader.ReadString();
                    if (!string.Equals(magic, AERISTerrainPreloadFormat.StateMagic,
                        StringComparison.Ordinal)) return false;
                    int version = reader.ReadInt32();
                    if (version != 1 && version != 2 && version != 3) return false;
                    loadedMode = (AERISTerrainPreloadMode)reader.ReadInt32();
                    if (!Enum.IsDefined(typeof(AERISTerrainPreloadMode), loadedMode))
                        loadedMode = AERISTerrainPreloadMode.AggressiveIdle;
                    int count = reader.ReadInt32();
                    if (count < 0 || count > 10000) return false;
                    for (int i = 0; i < count; i++)
                    {
                        var plan = new BodyPlan
                        {
                            BodyName = reader.ReadString(),
                            Priority = (AERISTerrainBodyPriority)reader.ReadInt32(),
                            PriorityOverride = reader.ReadBoolean(),
                            QualityLimit = (AERISTerrainTileLod)reader.ReadInt32()
                        };
                        if (version >= 2)
                        {
                            plan.QualityOverride = reader.ReadBoolean();
                            plan.AutomaticPointRefinementOnly = reader.ReadBoolean();
                            plan.AutomaticComplete = reader.ReadBoolean();
                            plan.CompletedQualityLimit =
                                (AERISTerrainTileLod)reader.ReadInt32();
                            plan.CompletedPointRefinementOnly = reader.ReadBoolean();
                            plan.CompletedEnvironmentHash = reader.ReadString();
                        }
                        plan.StorageLimitBytes = reader.ReadInt64();
                        plan.LastVisitedUtcTicks = reader.ReadInt64();
                        plan.GlobalCursor = reader.ReadInt64();
                        plan.FarCursor = reader.ReadInt64();
                        plan.RouteCursor = reader.ReadInt64();
                        plan.PointCursor = reader.ReadInt32();
                        plan.EnvironmentHash = reader.ReadString();
                        plan.Paused = reader.ReadBoolean();
                        if (version >= 3)
                        {
                            plan.CoastlineCursor = reader.ReadInt64();
                            plan.CoastlineComplete = reader.ReadBoolean();
                            plan.CompletedCoastlineFormatVersion = reader.ReadInt32();
                            plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        }
                        // Candidate 13 removes all per-body preload tuning. Legacy
                        // overrides and caps are discarded while progress/cursors remain.
                        plan.PriorityOverride = false;
                        plan.QualityOverride = false;
                        plan.StorageLimitBytes = 0L;
                        plan.Priority = AERISTerrainBodyPriority.Normal;
                        plan.QualityLimit = AERISTerrainTileLod.Far;
                        plan.AutomaticPointRefinementOnly = false;
                        if (!Enum.IsDefined(typeof(AERISTerrainTileLod),
                            plan.CompletedQualityLimit))
                            InvalidateAutomaticCompletion(plan);
                        if (version < 3 ||
                            plan.CompletedCoastlineFormatVersion !=
                                AERISTerrainCoastlineExtractor.HighDensityFormatVersion ||
                            !string.Equals(plan.CompletedCoastlineEnvironmentHash,
                                plan.EnvironmentHash, StringComparison.Ordinal))
                            InvalidateCoastlineCompletion(plan);
                        if (!string.IsNullOrEmpty(plan.BodyName))
                            loaded[plan.BodyName] = plan;
                    }
                    if (stream.Position != stream.Length) return false;
                }
            }
            catch { return false; }
            lock (sync)
            {
                plans.Clear();
                foreach (KeyValuePair<string, BodyPlan> pair in loaded)
                    plans[pair.Key] = pair.Value;
                // Candidate 13 final policy: persisted legacy modes are never restored.
                // State files retain only progress/cursors/paused state; the product
                // policy is fixed to AggressiveIdle when enabled, or Off when disabled.
                mode = settings == null || settings.TerrainPreloadEnabled ?
                    AERISTerrainPreloadMode.AggressiveIdle : AERISTerrainPreloadMode.Off;
                speedProfile = AERISTerrainPreloadSpeedProfile.Balanced;
            }
            return true;
        }

        void SaveStateIfNeeded(float now)
        {
            if (!stateDirty || now < nextStateSaveRealtime ||
                string.IsNullOrEmpty(statePath)) return;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            nextStateSaveRealtime = now + 5f;
            List<BodyPlan> snapshot = CaptureStateSnapshot();
            AERISTerrainPreloadMode snapshotMode = mode;
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.ArchiveCompression, "preload-state-save",
                runtime.CaptureStamp(), context => WriteStateSnapshot(snapshot,
                    snapshotMode), value =>
                {
                    bool ok = value is bool && (bool)value;
                    if (!ok) stateDirty = true;
                }, false);
            if (accepted) stateDirty = false;
        }

        List<BodyPlan> CaptureStateSnapshot()
        {
            var snapshot = new List<BodyPlan>();
            lock (sync)
                foreach (BodyPlan plan in plans.Values)
                    snapshot.Add(new BodyPlan
                    {
                        BodyName = plan.BodyName,
                        Priority = plan.Priority,
                        PriorityOverride = plan.PriorityOverride,
                        QualityLimit = plan.QualityLimit,
                        QualityOverride = plan.QualityOverride,
                        AutomaticPointRefinementOnly = plan.AutomaticPointRefinementOnly,
                        AutomaticComplete = plan.AutomaticComplete,
                        CompletedQualityLimit = plan.CompletedQualityLimit,
                        CompletedPointRefinementOnly = plan.CompletedPointRefinementOnly,
                        CompletedEnvironmentHash = plan.CompletedEnvironmentHash,
                        StorageLimitBytes = plan.StorageLimitBytes,
                        LastVisitedUtcTicks = plan.LastVisitedUtcTicks,
                        GlobalCursor = plan.GlobalCursor,
                        FarCursor = plan.FarCursor,
                        RouteCursor = plan.RouteCursor,
                        PointCursor = plan.PointCursor,
                        EnvironmentHash = plan.EnvironmentHash,
                        Paused = plan.Paused,
                        CoastlineCursor = plan.CoastlineCursor,
                        CoastlineComplete = plan.CoastlineComplete,
                        CompletedCoastlineFormatVersion =
                            plan.CompletedCoastlineFormatVersion,
                        CompletedCoastlineEnvironmentHash =
                            plan.CompletedCoastlineEnvironmentHash
                    });
            return snapshot;
        }

        bool WriteStateSnapshot(IList<BodyPlan> snapshot,
            AERISTerrainPreloadMode snapshotMode)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                using (var stream = new FileStream(stateTemporaryPath,
                    FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new BinaryWriter(stream))
                {
                    writer.Write(AERISTerrainPreloadFormat.StateMagic);
                    writer.Write(3);
                    writer.Write((int)snapshotMode);
                    writer.Write(snapshot == null ? 0 : snapshot.Count);
                    if (snapshot != null)
                    {
                        for (int i = 0; i < snapshot.Count; i++)
                        {
                            BodyPlan plan = snapshot[i];
                            writer.Write(plan.BodyName ?? string.Empty);
                            writer.Write((int)plan.Priority);
                            writer.Write(plan.PriorityOverride);
                            writer.Write((int)plan.QualityLimit);
                            writer.Write(plan.QualityOverride);
                            writer.Write(plan.AutomaticPointRefinementOnly);
                            writer.Write(plan.AutomaticComplete);
                            writer.Write((int)plan.CompletedQualityLimit);
                            writer.Write(plan.CompletedPointRefinementOnly);
                            writer.Write(plan.CompletedEnvironmentHash ?? string.Empty);
                            writer.Write(plan.StorageLimitBytes);
                            writer.Write(plan.LastVisitedUtcTicks);
                            writer.Write(plan.GlobalCursor);
                            writer.Write(plan.FarCursor);
                            writer.Write(plan.RouteCursor);
                            writer.Write(plan.PointCursor);
                            writer.Write(plan.EnvironmentHash ?? string.Empty);
                            writer.Write(plan.Paused);
                            writer.Write(plan.CoastlineCursor);
                            writer.Write(plan.CoastlineComplete);
                            writer.Write(plan.CompletedCoastlineFormatVersion);
                            writer.Write(plan.CompletedCoastlineEnvironmentHash ??
                                string.Empty);
                        }
                    }
                    writer.Flush();
                    stream.Flush();
                }
                string backup = statePath + ".bak";
                if (File.Exists(statePath))
                {
                    try
                    {
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Replace(stateTemporaryPath, statePath, backup);
                    }
                    catch
                    {
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(statePath, backup);
                        File.Move(stateTemporaryPath, statePath);
                    }
                }
                else File.Move(stateTemporaryPath, statePath);
                return true;
            }
            catch
            {
                try { if (File.Exists(stateTemporaryPath)) File.Delete(stateTemporaryPath); }
                catch { }
                return false;
            }
        }

        static string CurrentGameDataHash()
        {
            return AERISTerrainTileSystem.GameDataHash;
        }

        static double NormalizeLongitude(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static double Ema(double previous, double sample, double weight)
        {
            if (previous <= 0.0) return sample;
            return previous + Math.Max(0.01, Math.Min(1.0, weight)) * (sample - previous);
        }

        public void Dispose()
        {
            ApplyStandardSchedulerState(false);
            blockPipeline.SetPreloadThroughput(false);
            if (disposed) return;
            blockPipeline.CancelWhere(request => request == null ||
                request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder);
            ClearPendingCoastlineForBody(string.Empty);
            FlushPendingBatchesSynchronously();
            // Final shutdown checkpoint is intentionally synchronous: Flight control is no
            // longer active and losing builder progress is worse than a bounded close flush.
            WriteStateSnapshot(CaptureStateSnapshot(), mode);
            disposed = true;
        }

        void FlushPendingBatchesSynchronously()
        {
            var encoded = new List<AERISTerrainPreloadEncodedTile>();
            var stableIds = new List<string>();
            var encodeWork = new List<PendingEncodeWork>();
            var batches = new List<PendingChunkBatch>();
            lock (sync)
            {
                encodeWork.AddRange(pendingEncodes.Values);
                pendingEncodes.Clear();
                batches.AddRange(pendingChunkBatches.Values);
                pendingChunkBatches.Clear();
            }
            for (int i = 0; i < encodeWork.Count; i++)
            {
                PendingEncodeWork work = encodeWork[i];
                if (work == null || work.Tile == null) continue;
                try
                {
                    encoded.Add(AERISTerrainPreloadCodec.Encode(work.Tile,
                        work.PqsHash, work.GameDataHash, work.DatabaseGeneration,
                        AERISTerrainCodecId.Deflate));
                    stableIds.Add(work.StableId);
                }
                catch { failedSinceStart++; }
            }
            for (int i = 0; i < batches.Count; i++)
            {
                PendingChunkBatch batch = batches[i];
                if (batch == null) continue;
                foreach (KeyValuePair<string, AERISTerrainPreloadEncodedTile> pair
                    in batch.Tiles)
                {
                    stableIds.Add(pair.Key);
                    encoded.Add(pair.Value);
                }
            }
            if (encoded.Count == 0) return;
            long bytes;
            double ratio;
            int chunks;
            bool ok = database.SaveEncodedBatch(encoded, out bytes, out ratio,
                out chunks);
            lock (sync)
                for (int i = 0; i < stableIds.Count; i++)
                    pendingWrites.Remove(stableIds[i]);
            if (ok) completedSinceStart += encoded.Count;
            else failedSinceStart += encoded.Count;
        }
    }
}
