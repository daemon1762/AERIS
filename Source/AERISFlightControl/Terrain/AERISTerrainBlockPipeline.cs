using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Performance;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // Shared incremental PQS fallback/preload producer. PQS sampling stays on the KSP
    // main thread, while block statistics and immutable commit preparation use the
    // existing Performance Runtime scheduler. Jobs rotate by block, never by whole tile.
    internal sealed class AERISTerrainBlockPipeline : IDisposable
    {
        sealed class BlockDefinition
        {
            internal int Id;
            internal int X0;
            internal int Y0;
            internal int Width;
            internal int Height;
        }

        sealed class BlockLease
        {
            internal long Epoch;
            internal int Released;
        }

        struct R040BFailureSample
        {
            internal int X;
            internal int Y;
            internal int LocalX;
            internal int LocalY;
            internal double LatitudeDeg;
            internal double LongitudeDeg;
            internal bool Boundary;
            internal bool CacheHit;
            internal double ExpectedPqsMeters;
            internal double WorkerTrigMeters;
            internal double ErrorMeters;
            internal double TrigX;
            internal double TrigY;
            internal double TrigZ;
            internal int BlockId;
            internal int WorkerThreadId;
        }

        sealed class BlockPayload
        {
            internal int Id;
            internal int X0;
            internal int Y0;
            internal int Width;
            internal int Height;
            internal float[] Elevation;
            internal byte[] Flags;
            internal int Valid;
            internal float Minimum;
            internal float Maximum;

            // R040B shadow payload: primitives/scalars only.
            internal double[] AuthorityExact;
            internal AERISR039MinmusPureCpuExact.VertexPlanetSnapshot
                R040BMinmusSnapshot;
            internal int Resolution;
            internal double SouthLatitudeDeg;
            internal double NorthLatitudeDeg;
            internal double WestLongitudeDeg;
            internal double EastLongitudeDeg;
            internal int R040BMainThreadId;
            internal int R040BWorkerThreadId;
            internal int R040BShadowSamples;
            internal int R040BShadowFailures;
            internal double R040BShadowMaxErrorMeters;
            internal string R040BShadowError;
            internal byte[] R040BCacheHits;
            internal R040BFailureSample[] R040BFailureSamples;
            internal int R040BFailureSampleCount;
        }

        struct BoundarySampleKey : IEquatable<BoundarySampleKey>
        {
            internal string BodyName;
            internal long BodyRadiusMillimetres;
            internal string EnvironmentHash;
            internal long LatitudeNanodegrees;
            internal long LongitudeNanodegrees;

            public bool Equals(BoundarySampleKey other)
            {
                return BodyRadiusMillimetres == other.BodyRadiusMillimetres &&
                    LatitudeNanodegrees == other.LatitudeNanodegrees &&
                    LongitudeNanodegrees == other.LongitudeNanodegrees &&
                    string.Equals(BodyName, other.BodyName,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(EnvironmentHash, other.EnvironmentHash,
                        StringComparison.Ordinal);
            }

            public override bool Equals(object value)
            {
                return value is BoundarySampleKey && Equals((BoundarySampleKey)value);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.OrdinalIgnoreCase.GetHashCode(
                        BodyName ?? string.Empty);
                    hash = hash * 397 ^ BodyRadiusMillimetres.GetHashCode();
                    hash = hash * 397 ^ StringComparer.Ordinal.GetHashCode(
                        EnvironmentHash ?? string.Empty);
                    hash = hash * 397 ^ LatitudeNanodegrees.GetHashCode();
                    hash = hash * 397 ^ LongitudeNanodegrees.GetHashCode();
                    return hash;
                }
            }
        }

        struct BoundarySampleValue
        {
            internal float Elevation;
            // Preserve the original PQS double for R040B exactness comparison.
            internal double AuthorityExactElevation;
            internal byte Flag;
            internal long Sequence;
        }

        struct BoundarySampleQueueEntry
        {
            internal BoundarySampleKey Key;
            internal long Sequence;
        }

        sealed class TileState
        {
            internal string Id = string.Empty;
            internal CelestialBody Body;
            internal AERISTerrainTileRequest Request;
            internal AERISTerrainTileSource Source;
            internal string PqsHash = string.Empty;
            internal string GameDataHash = string.Empty;
            internal long TerrainGenerationId;
            internal float[] Elevation;
            internal byte[] Flags;
            internal BlockDefinition[] Blocks;
            internal int NextBlock;
            internal int CompletedBlocks;
            internal int PendingBlocks;
            internal BlockDefinition SamplingBlock;
            internal float[] SamplingElevation;
            internal byte[] SamplingFlags;
            internal double[] SamplingAuthorityExact;
            internal byte[] SamplingR040BCacheHits;
            internal int SamplingIndex;

            internal AERISR039MinmusPureCpuExact.VertexPlanetSnapshot
                R040BMinmusSnapshot;
            internal int R040BShadowSamples;
            internal int R040BShadowFailures;
            internal double R040BShadowMaxErrorMeters;
            internal bool R040BAllWorkersOffMainThread = true;
            internal string R040BShadowError = string.Empty;
            internal int R040BDiagnosticSamplesEmitted;

            internal int Valid;
            internal float Minimum = float.PositiveInfinity;
            internal float Maximum = float.NegativeInfinity;
            internal int LastPublishedPercent;
            internal bool Cancelled;
            internal Func<AERISTerrainTileRequest, bool> IsCurrent;
            internal Action<AERISTerrainTileRequest, AERISTerrainHeightTile, bool> Commit;
        }

        readonly object sync = new object();
        readonly Dictionary<string, TileState> states =
            new Dictionary<string, TileState>(StringComparer.Ordinal);
        readonly List<TileState> roundRobin = new List<TileState>(32);
        readonly Dictionary<BoundarySampleKey, BoundarySampleValue> boundarySamples =
            new Dictionary<BoundarySampleKey, BoundarySampleValue>();
        readonly Queue<BoundarySampleQueueEntry> boundarySampleOrder =
            new Queue<BoundarySampleQueueEntry>();
        readonly AERISTerrainPerformanceController performance;
        const int MaximumActiveTiles = 48;
        const int StandardPreloadActiveTiles = 64;
        const int StandardOutstandingBlocks = 96;
        const int MaximumBoundarySamples = 131072;

        // R040B Phase 1A is certification/shadow only.
        const double R040BShadowToleranceMeters = 1E-08;

        // AERIS37 R040B candidate2 diagnostics are strictly bounded.
        const int R040BMaximumFailureSamplesPerBlock = 8;
        const int R040BMaximumFailureDiagnosticsPerTile = 24;

        volatile bool standardPreloadThroughput;
        int roundRobinIndex;
        float sampleTokens;
        bool disposed;
        int lastBatchSamples;
        double lastBatchMilliseconds;
        long blocksCompleted;
        long staleDiscarded;
        long intermediateCommitsSkipped;
        long boundarySampleCacheHits;
        long boundarySampleCacheMisses;
        long boundarySampleSequence;
        int outstandingBlocks;
        long outstandingEpoch = 1L;
        long admissionBackpressureCount;
        long recoveryCount;
        int lastRecoveryTiles;
        string lastRecoveryReason = string.Empty;

        readonly int r040bMainThreadId;
        bool r040bMinmusSnapshotAttempted;
        AERISR039MinmusPureCpuExact.VertexPlanetSnapshot r040bMinmusSnapshot;
        string r040bMinmusSnapshotFailure = string.Empty;

        long r040bShadowTiles;
        long r040bShadowSamples;
        long r040bShadowFailures;
        double r040bShadowMaxErrorMeters;

        internal AERISTerrainBlockPipeline(AERISTerrainPerformanceController performance)
        {
            this.performance = performance;
            r040bMainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        internal int Count { get { lock (sync) return states.Count; } }
        internal int ActiveTileLimit
        {
            get
            {
                return standardPreloadThroughput ? StandardPreloadActiveTiles :
                    MaximumActiveTiles;
            }
        }
        internal int PendingBlockLimit
        {
            get { return standardPreloadThroughput ? 4 : 2; }
        }
        internal int OutstandingBlockLimit
        {
            get { return standardPreloadThroughput ? StandardOutstandingBlocks : 48; }
        }
        internal int OutstandingBlocks { get { return Math.Max(0,
            Volatile.Read(ref outstandingBlocks)); } }
        internal long AdmissionBackpressureCount
        {
            get { return Interlocked.Read(ref admissionBackpressureCount); }
        }
        internal long RecoveryCount { get { return Interlocked.Read(ref recoveryCount); } }
        internal int LastRecoveryTiles { get { return lastRecoveryTiles; } }
        internal string LastRecoveryReason { get { return lastRecoveryReason; } }
        internal int LastBatchSamples { get { return lastBatchSamples; } }
        internal double LastBatchMilliseconds { get { return lastBatchMilliseconds; } }
        internal long BlocksCompleted { get { return blocksCompleted; } }
        internal long StaleDiscarded { get { return staleDiscarded; } }
        internal long IntermediateCommitsSkipped
        {
            get { return intermediateCommitsSkipped; }
        }
        internal long BoundarySampleCacheHits
        {
            get { return boundarySampleCacheHits; }
        }
        internal long BoundarySampleCacheMisses
        {
            get { return boundarySampleCacheMisses; }
        }
        internal double BoundarySampleCacheHitRatio
        {
            get
            {
                long total = boundarySampleCacheHits + boundarySampleCacheMisses;
                return total <= 0L ? 0.0 : boundarySampleCacheHits / (double)total;
            }
        }

        internal void SetPreloadThroughput(bool standard)
        {
            standardPreloadThroughput = standard;
        }

        internal bool Enqueue(CelestialBody body, AERISTerrainTileRequest request,
            AERISTerrainTileSource source, string pqsHash, string gameDataHash,
            long terrainGenerationId, Func<AERISTerrainTileRequest, bool> isCurrent,
            Action<AERISTerrainTileRequest, AERISTerrainHeightTile, bool> commit)
        {
            if (disposed || body == null || request == null || commit == null) return false;

            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot r040bSnapshot = null;
            TryGetR040BMinmusSnapshot(body, out r040bSnapshot);

            string id = WorkId(request);
            lock (sync)
            {
                TileState existing;
                if (states.TryGetValue(id, out existing) && existing != null)
                {
                    MergeState(existing, request, isCurrent, commit);
                    return true;
                }
                if (states.Count >= MaximumActiveTiles && states.Count >= ActiveTileLimit)
                {
                    TileState worst = null;
                    for (int i = 0; i < roundRobin.Count; i++)
                    {
                        TileState candidate = roundRobin[i];
                        if (candidate == null || candidate.Cancelled) continue;
                        if (worst == null || Compare(candidate.Request, worst.Request) > 0)
                            worst = candidate;
                    }
                    if (worst == null || Compare(request, worst.Request) >= 0) return false;
                    worst.Cancelled = true;
                    states.Remove(worst.Id);
                    roundRobin.Remove(worst);
                    staleDiscarded++;
                }
                int count = checked(request.Resolution * request.Resolution);
                var state = new TileState
                {
                    Id = id,
                    Body = body,
                    Request = request,
                    Source = source,
                    PqsHash = pqsHash ?? string.Empty,
                    GameDataHash = gameDataHash ?? string.Empty,
                    TerrainGenerationId = terrainGenerationId,
                    Elevation = new float[count],
                    Flags = new byte[count],
                    Blocks = BuildBlocks(request.Resolution),
                    R040BMinmusSnapshot = r040bSnapshot,
                    IsCurrent = isCurrent,
                    Commit = commit
                };
                states[id] = state;
                roundRobin.Add(state);
                return true;
            }
        }

        bool TryGetR040BMinmusSnapshot(
            CelestialBody body,
            out AERISR039MinmusPureCpuExact.VertexPlanetSnapshot snapshot)
        {
            snapshot = null;

            if (body == null ||
                !string.Equals(
                    body.name,
                    "Minmus",
                    StringComparison.OrdinalIgnoreCase))
                return false;

            if (r040bMinmusSnapshotAttempted)
            {
                snapshot = r040bMinmusSnapshot;
                return snapshot != null;
            }

            r040bMinmusSnapshotAttempted = true;

            if (Thread.CurrentThread.ManagedThreadId != r040bMainThreadId)
            {
                r040bMinmusSnapshotFailure = "SNAPSHOT_CAPTURE_NOT_MAIN_THREAD";

                AERISLogger.Warn(
                    "[R040B][MINMUS_SNAPSHOT_FAIL]" +
                    "; reason=" + r040bMinmusSnapshotFailure +
                    "; producer_switch=false" +
                    "; db_authority=PQS" +
                    "; worker_runtime_object_access=false");

                return false;
            }

            string failure;
            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot captured;

            if (!AERISR039PtcMinmusPureCpuExactValidationObserver.
                TryCreateR040BProductionSnapshot(
                    body,
                    out captured,
                    out failure))
            {
                r040bMinmusSnapshotFailure =
                    string.IsNullOrEmpty(failure) ?
                    "UNKNOWN_CAPTURE_FAILURE" : failure;

                AERISLogger.Warn(
                    "[R040B][MINMUS_SNAPSHOT_FAIL]" +
                    "; reason=" + r040bMinmusSnapshotFailure +
                    "; producer_switch=false" +
                    "; db_authority=PQS" +
                    "; worker_runtime_object_access=false");

                return false;
            }

            r040bMinmusSnapshot = captured;
            r040bMinmusSnapshotFailure = string.Empty;
            snapshot = captured;

            AERISLogger.Info(
                "[R040B][MINMUS_SNAPSHOT]" +
                "; pass=true" +
                "; main_thread_id=" + r040bMainThreadId +
                "; payload=PRIMITIVES_ONLY" +
                "; terrain_tolerance_m=1E-08" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; worker_runtime_object_access=false");

            return true;
        }

        // A viewport/range replan must refresh matching in-progress work even when the
        // active pipeline is already at capacity. Without this path, retained same-key
        // work keeps an old generation, fails Current(), and restarts from zero.
        internal bool RefreshActive(AERISTerrainTileRequest request,
            Func<AERISTerrainTileRequest, bool> isCurrent)
        {
            if (disposed || request == null) return false;
            lock (sync)
            {
                TileState existing;
                if (!states.TryGetValue(WorkId(request), out existing) ||
                    existing == null) return false;
                MergeState(existing, request, isCurrent, null);
                return true;
            }
        }

        static void MergeState(TileState existing, AERISTerrainTileRequest request,
            Func<AERISTerrainTileRequest, bool> isCurrent,
            Action<AERISTerrainTileRequest, AERISTerrainHeightTile, bool> commit)
        {
            if (existing == null || existing.Request == null || request == null) return;
            if (request.Priority > existing.Request.Priority)
                existing.Request.Priority = request.Priority;
            if (request.Lane < existing.Request.Lane)
                existing.Request.Lane = request.Lane;
            if (request.ReadLane < existing.Request.ReadLane)
                existing.Request.ReadLane = request.ReadLane;
            existing.Request.Visible = existing.Request.Visible || request.Visible;
            existing.Request.ViewDistanceMeters = Math.Min(
                existing.Request.ViewDistanceMeters, request.ViewDistanceMeters);
            existing.Request.TerrainGeneration = Math.Max(
                existing.Request.TerrainGeneration, request.TerrainGeneration);
            existing.Request.ViewGeneration = Math.Max(existing.Request.ViewGeneration,
                request.ViewGeneration);
            existing.Request.RangeGeneration = Math.Max(existing.Request.RangeGeneration,
                request.RangeGeneration);
            existing.Request.PlanGeneration = Math.Max(existing.Request.PlanGeneration,
                request.PlanGeneration);
            existing.Request.DatabaseGeneration = Math.Max(
                existing.Request.DatabaseGeneration, request.DatabaseGeneration);
            existing.Request.VesselGeneration = Math.Max(
                existing.Request.VesselGeneration, request.VesselGeneration);
            existing.Request.RequestSequence = Math.Max(
                existing.Request.RequestSequence, request.RequestSequence);
            existing.IsCurrent = isCurrent ?? existing.IsCurrent;
            existing.Commit = commit ?? existing.Commit;
        }

        internal void CancelWhere(Func<AERISTerrainTileRequest, bool> keep)
        {
            lock (sync)
            {
                for (int i = roundRobin.Count - 1; i >= 0; i--)
                {
                    TileState state = roundRobin[i];
                    if (state == null || keep == null || !keep(state.Request))
                    {
                        if (state != null) state.Cancelled = true;
                        roundRobin.RemoveAt(i);
                        if (state != null) states.Remove(state.Id);
                        staleDiscarded++;
                    }
                }
                if (roundRobinIndex >= roundRobin.Count) roundRobinIndex = 0;
            }
        }

        internal bool Contains(AERISTerrainTileRequest request)
        {
            if (request == null) return false;
            lock (sync) return states.ContainsKey(WorkId(request));
        }

        // Frozen Gate 4A static-acceptance markers retained for source-history regression:
        // Stopwatch watch = Stopwatch.StartNew()
        // watch.Elapsed.TotalMilliseconds < budget
        internal void Tick(float queriesPerSecond, int maximumSamples,
            float mainThreadBudgetMilliseconds, float elapsedSeconds)
        {
            if (disposed) return;
            int maximum = Math.Max(1, maximumSamples);
            float elapsed = Mathf.Clamp(elapsedSeconds, 0f, 0.5f);
            sampleTokens = Mathf.Min(maximum * 4f, sampleTokens +
                elapsed * Math.Max(1f, queriesPerSecond));
            int target = Math.Min(maximum, Mathf.FloorToInt(sampleTokens));
            if (target <= 0)
            {
                lastBatchSamples = 0;
                lastBatchMilliseconds = 0.0;
                return;
            }
            double budget = Math.Max(0.05, mainThreadBudgetMilliseconds);
            int queriedPqs = 0;
            int processed = 0;
            int maximumProcessed = Math.Max(target, maximum * 4);
            long batchStartTicks = Stopwatch.GetTimestamp();
            long budgetTicks = Math.Max(1L, (long)Math.Ceiling(
                budget * Stopwatch.Frequency / 1000.0));
            while (queriedPqs < target && processed < maximumProcessed &&
                Stopwatch.GetTimestamp() - batchStartTicks < budgetTicks)
            {
                TileState state = NextRunnableState();
                if (state == null) break;
                if (!Current(state))
                {
                    RemoveState(state, true);
                    continue;
                }
                if (state.SamplingBlock != null && state.SamplingElevation != null &&
                    state.SamplingIndex >= state.SamplingElevation.Length)
                {
                    if (!SubmitCompletedBlock(state)) break;
                    continue;
                }
                if (state.SamplingBlock == null)
                {
                    if (state.NextBlock >= state.Blocks.Length) break;
                    BeginBlock(state, state.Blocks[state.NextBlock++]);
                }
                int before = state.SamplingIndex;
                bool queried = SampleOne(state);
                if (state.SamplingIndex > before)
                {
                    processed++;
                    if (queried) queriedPqs++;
                }
                if (state.SamplingIndex >= state.SamplingElevation.Length &&
                    !SubmitCompletedBlock(state)) break;
            }
            double batchMilliseconds = (Stopwatch.GetTimestamp() - batchStartTicks) *
                1000.0 / Stopwatch.Frequency;
            sampleTokens = Math.Max(0f, sampleTokens - queriedPqs);
            lastBatchSamples = queriedPqs;
            lastBatchMilliseconds = batchMilliseconds;
            if (performance != null && queriedPqs > 0)
                performance.RecordTilePqsCost((float)(batchMilliseconds / queriedPqs));
        }

        TileState NextRunnableState()
        {
            if (OutstandingBlocks >= OutstandingBlockLimit) return null;
            lock (sync)
            {
                if (roundRobin.Count == 0) return null;
                for (int attempt = 0; attempt < roundRobin.Count; attempt++)
                {
                    if (roundRobinIndex >= roundRobin.Count) roundRobinIndex = 0;
                    TileState state = roundRobin[roundRobinIndex++];
                    if (state == null || state.Cancelled) continue;
                    if (state.PendingBlocks >= 2 && state.PendingBlocks >= PendingBlockLimit) continue;
                    if (state.SamplingBlock != null || state.NextBlock < state.Blocks.Length)
                        return state;
                }
                return null;
            }
        }

        void BeginBlock(TileState state, BlockDefinition block)
        {
            state.SamplingBlock = block;

            int count = block.Width * block.Height;
            state.SamplingElevation = new float[count];
            state.SamplingFlags = new byte[count];

            state.SamplingAuthorityExact =
                state.R040BMinmusSnapshot == null ?
                null : new double[count];

            state.SamplingR040BCacheHits =
                state.R040BMinmusSnapshot == null ?
                null : new byte[count];

            state.SamplingIndex = 0;
        }

        bool SampleOne(TileState state)
        {
            BlockDefinition block = state.SamplingBlock;
            int local = state.SamplingIndex;
            int localX = local % block.Width;
            int localY = local / block.Width;
            int x = block.X0 + localX;
            int y = block.Y0 + localY;
            int resolution = state.Request.Resolution;
            double u = x / (double)(resolution - 1);
            double v = y / (double)(resolution - 1);
            double latitude = state.Request.SouthLatitudeDeg +
                (state.Request.NorthLatitudeDeg - state.Request.SouthLatitudeDeg) * v;
            double longitude = InterpolateLongitude(state.Request.WestLongitudeDeg,
                state.Request.EastLongitudeDeg, u);
            bool boundary = x == 0 || y == 0 || x == resolution - 1 ||
                y == resolution - 1;
            BoundarySampleKey cacheKey = default(BoundarySampleKey);
            BoundarySampleValue cached;
            if (boundary)
            {
                cacheKey = CreateBoundarySampleKey(state.Request.Key, latitude,
                    longitude);
                if (boundarySamples.TryGetValue(cacheKey, out cached))
                {
                    boundarySampleCacheHits++;
                    state.SamplingElevation[local] = cached.Elevation;
                    state.SamplingFlags[local] = cached.Flag;

                    if (state.SamplingAuthorityExact != null)
                        state.SamplingAuthorityExact[local] =
                            cached.AuthorityExactElevation;

                    if (state.SamplingR040BCacheHits != null)
                        state.SamplingR040BCacheHits[local] = 1;

                    state.SamplingIndex++;
                    return false;
                }
                boundarySampleCacheMisses++;
            }
            double elevation;
            if (AERISTerrainAwareness.TrySampleTerrainAslShared(state.Body, latitude,
                longitude, out elevation))
            {
                float storedElevation = (float)elevation;
                byte flag = state.Body.ocean && elevation <= 1.0 ? (byte)2 : (byte)1;
                state.SamplingElevation[local] = storedElevation;
                state.SamplingFlags[local] = flag;

                if (state.SamplingAuthorityExact != null)
                    state.SamplingAuthorityExact[local] = elevation;

                if (boundary)
                    PutBoundarySample(
                        cacheKey,
                        storedElevation,
                        elevation,
                        flag);
            }
            state.SamplingIndex++;
            return true;
        }

        static BoundarySampleKey CreateBoundarySampleKey(AERISTerrainTileKey key,
            double latitudeDeg, double longitudeDeg)
        {
            double normalizedLongitude = NormalizeLongitude(longitudeDeg);
            if (Math.Abs(normalizedLongitude - 180.0) <= 0.0000000005)
                normalizedLongitude = -180.0;
            return new BoundarySampleKey
            {
                BodyName = key.BodyName ?? string.Empty,
                BodyRadiusMillimetres = key.BodyRadiusMillimetres,
                EnvironmentHash = key.EnvironmentHash ?? string.Empty,
                LatitudeNanodegrees = (long)Math.Round(latitudeDeg * 1000000000.0),
                LongitudeNanodegrees = (long)Math.Round(
                    normalizedLongitude * 1000000000.0)
            };
        }

        void PutBoundarySample(
            BoundarySampleKey key,
            float elevation,
            double authorityExactElevation,
            byte flag)
        {
            BoundarySampleValue existing;
            if (boundarySamples.TryGetValue(key, out existing)) return;
            long sequence = ++boundarySampleSequence;
            boundarySamples[key] = new BoundarySampleValue
            {
                Elevation = elevation,
                AuthorityExactElevation = authorityExactElevation,
                Flag = flag,
                Sequence = sequence
            };
            boundarySampleOrder.Enqueue(new BoundarySampleQueueEntry
            {
                Key = key,
                Sequence = sequence
            });
            while (boundarySamples.Count > MaximumBoundarySamples &&
                boundarySampleOrder.Count > 0)
            {
                BoundarySampleQueueEntry oldest = boundarySampleOrder.Dequeue();
                BoundarySampleValue current;
                if (boundarySamples.TryGetValue(oldest.Key, out current) &&
                    current.Sequence == oldest.Sequence)
                    boundarySamples.Remove(oldest.Key);
            }
        }

        bool SubmitCompletedBlock(TileState state)
        {
            if (state == null || state.SamplingBlock == null ||
                state.SamplingElevation == null || state.SamplingFlags == null) return false;
            BlockLease lease;
            if (!TryAcquireOutstanding(out lease))
            {
                Interlocked.Increment(ref admissionBackpressureCount);
                return false;
            }
            BlockDefinition definition = state.SamplingBlock;
            var payload = new BlockPayload
            {
                Id = definition.Id,
                X0 = definition.X0,
                Y0 = definition.Y0,
                Width = definition.Width,
                Height = definition.Height,
                Elevation = state.SamplingElevation,
                Flags = state.SamplingFlags,

                AuthorityExact = state.SamplingAuthorityExact,
                R040BMinmusSnapshot = state.R040BMinmusSnapshot,
                R040BCacheHits = state.SamplingR040BCacheHits,
                R040BFailureSamples =
                    state.R040BMinmusSnapshot == null ?
                    null : new R040BFailureSample[
                        R040BMaximumFailureSamplesPerBlock],

                Resolution = state.Request.Resolution,
                SouthLatitudeDeg = state.Request.SouthLatitudeDeg,
                NorthLatitudeDeg = state.Request.NorthLatitudeDeg,
                WestLongitudeDeg = state.Request.WestLongitudeDeg,
                EastLongitudeDeg = state.Request.EastLongitudeDeg,
                R040BMainThreadId = r040bMainThreadId
            };
            Interlocked.Increment(ref state.PendingBlocks);

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null)
            {
                ReleaseOutstanding(lease);
                DecrementPending(state);
                return false;
            }
            string key = "terrain-block:" + state.Request.Key.FileStem + ":" +
                state.Request.RequestSequence + ":" + payload.Id;
            // The worker stage must never fall back to main-thread execution;
            // rejected required work retains its sampled payload and retries admission.
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute, key, runtime.CaptureStamp(), context =>
                {
                    ProcessBlock(payload);
                    return payload;
                }, value =>
                {
                    ReleaseOutstanding(lease);
                    DecrementPending(state);
                    BlockPayload result = value as BlockPayload;
                    if (result == null)
                    {
                        staleDiscarded++;
                        RemoveState(state, true);
                        return;
                    }
                    if (!Current(state))
                    {
                        RemoveState(state, true);
                        return;
                    }
                    CommitBlock(state, result);
                }, false);
            if (!accepted)
            {
                ReleaseOutstanding(lease);
                DecrementPending(state);
                Interlocked.Increment(ref admissionBackpressureCount);
                return false;
            }
            state.SamplingBlock = null;
            state.SamplingElevation = null;
            state.SamplingFlags = null;
            state.SamplingAuthorityExact = null;
            state.SamplingR040BCacheHits = null;
            state.SamplingIndex = 0;
            return true;
        }

        bool TryAcquireOutstanding(out BlockLease lease)
        {
            lease = null;
            int limit = OutstandingBlockLimit;
            while (true)
            {
                int current = Volatile.Read(ref outstandingBlocks);
                if (current >= limit) return false;
                if (Interlocked.CompareExchange(ref outstandingBlocks, current + 1,
                    current) == current)
                {
                    lease = new BlockLease { Epoch = Interlocked.Read(
                        ref outstandingEpoch) };
                    return true;
                }
            }
        }

        void ReleaseOutstanding(BlockLease lease)
        {
            if (lease == null || Interlocked.Exchange(ref lease.Released, 1) != 0) return;
            if (lease.Epoch != Interlocked.Read(ref outstandingEpoch)) return;
            while (true)
            {
                int current = Volatile.Read(ref outstandingBlocks);
                if (current <= 0) return;
                if (Interlocked.CompareExchange(ref outstandingBlocks, current - 1,
                    current) == current) return;
            }
        }

        static void DecrementPending(TileState state)
        {
            if (state == null) return;
            while (true)
            {
                int current = Volatile.Read(ref state.PendingBlocks);
                if (current <= 0) return;
                if (Interlocked.CompareExchange(ref state.PendingBlocks, current - 1,
                    current) == current) return;
            }
        }

        // Abandon only Preload Builder tiles. Persisted database content is untouched and
        // the standard producer will requeue every missing tile from its durable cursor.
        internal int RecoverPreloadAfterBackpressure(string reason)
        {
            Interlocked.Increment(ref outstandingEpoch);
            Interlocked.Exchange(ref outstandingBlocks, 0);
            int removed = 0;
            lock (sync)
            {
                for (int i = roundRobin.Count - 1; i >= 0; i--)
                {
                    TileState state = roundRobin[i];
                    if (state == null || state.Request == null ||
                        state.Request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder)
                        continue;
                    state.Cancelled = true;
                    roundRobin.RemoveAt(i);
                    states.Remove(state.Id);
                    removed++;
                }
                if (roundRobinIndex >= roundRobin.Count) roundRobinIndex = 0;
            }
            sampleTokens = 0f;
            lastRecoveryTiles = removed;
            lastRecoveryReason = string.IsNullOrEmpty(reason) ? "BACKPRESSURE" : reason;
            Interlocked.Increment(ref recoveryCount);
            staleDiscarded += removed;
            return removed;
        }

        static void CaptureR040BFailureSample(
            BlockPayload block,
            int local,
            int localX,
            int localY,
            int x,
            int y,
            double latitude,
            double longitude,
            double expected,
            double actual,
            double error,
            double px,
            double py,
            double pz)
        {
            if (block == null ||
                block.R040BFailureSamples == null ||
                block.R040BFailureSampleCount >=
                    block.R040BFailureSamples.Length)
                return;

            bool boundary =
                x == 0 ||
                y == 0 ||
                x == block.Resolution - 1 ||
                y == block.Resolution - 1;

            bool cacheHit =
                block.R040BCacheHits != null &&
                local >= 0 &&
                local < block.R040BCacheHits.Length &&
                block.R040BCacheHits[local] != 0;

            block.R040BFailureSamples[
                block.R040BFailureSampleCount++] =
                new R040BFailureSample
                {
                    X = x,
                    Y = y,
                    LocalX = localX,
                    LocalY = localY,
                    LatitudeDeg = latitude,
                    LongitudeDeg = longitude,
                    Boundary = boundary,
                    CacheHit = cacheHit,
                    ExpectedPqsMeters = expected,
                    WorkerTrigMeters = actual,
                    ErrorMeters = error,
                    TrigX = px,
                    TrigY = py,
                    TrigZ = pz,
                    BlockId = block.Id,
                    WorkerThreadId = block.R040BWorkerThreadId
                };
        }

        static void ProcessR040BMinmusShadow(BlockPayload block)
        {
            if (block == null ||
                block.R040BMinmusSnapshot == null ||
                block.AuthorityExact == null)
                return;

            block.R040BWorkerThreadId =
                Thread.CurrentThread.ManagedThreadId;

            try
            {
                if (block.Elevation == null ||
                    block.Flags == null ||
                    block.AuthorityExact.Length != block.Elevation.Length ||
                    block.Flags.Length != block.Elevation.Length ||
                    (block.R040BCacheHits != null &&
                     block.R040BCacheHits.Length != block.Elevation.Length))
                {
                    block.R040BShadowFailures = 1;
                    block.R040BShadowMaxErrorMeters =
                        double.PositiveInfinity;
                    block.R040BShadowError =
                        "SHADOW_ARRAY_LENGTH_MISMATCH";
                    return;
                }

                for (int local = 0;
                    local < block.AuthorityExact.Length;
                    local++)
                {
                    if (block.Flags[local] == 0)
                        continue;

                    int localX = local % block.Width;
                    int localY = local / block.Width;

                    int x = block.X0 + localX;
                    int y = block.Y0 + localY;

                    double denominator =
                        Math.Max(1, block.Resolution - 1);

                    double u = x / denominator;
                    double v = y / denominator;

                    double latitude =
                        block.SouthLatitudeDeg +
                        (block.NorthLatitudeDeg -
                            block.SouthLatitudeDeg) * v;

                    double longitude =
                        InterpolateLongitude(
                            block.WestLongitudeDeg,
                            block.EastLongitudeDeg,
                            u);

                    double latitudeRad =
                        latitude * Math.PI / 180.0;

                    double longitudeRad =
                        longitude * Math.PI / 180.0;

                    double cosineLatitude =
                        Math.Cos(latitudeRad);

                    // R040 certified body-relative convention:
                    // X = cos(lat) * cos(lon)
                    // Y = sin(lat)
                    // Z = cos(lat) * sin(lon)
                    double px =
                        cosineLatitude * Math.Cos(longitudeRad);

                    double py =
                        Math.Sin(latitudeRad);

                    double pz =
                        cosineLatitude * Math.Sin(longitudeRad);

                    double actual =
                        AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                            block.R040BMinmusSnapshot,
                            px,
                            py,
                            pz,
                            0.0);

                    double expected =
                        block.AuthorityExact[local];

                    block.R040BShadowSamples++;

                    if (double.IsNaN(actual) ||
                        double.IsInfinity(actual) ||
                        double.IsNaN(expected) ||
                        double.IsInfinity(expected))
                    {
                        block.R040BShadowFailures++;
                        block.R040BShadowMaxErrorMeters =
                            double.PositiveInfinity;

                        CaptureR040BFailureSample(
                            block,
                            local,
                            localX,
                            localY,
                            x,
                            y,
                            latitude,
                            longitude,
                            expected,
                            actual,
                            double.PositiveInfinity,
                            px,
                            py,
                            pz);

                        continue;
                    }

                    double error =
                        Math.Abs(actual - expected);

                    block.R040BShadowMaxErrorMeters =
                        Math.Max(
                            block.R040BShadowMaxErrorMeters,
                            error);

                    if (error > R040BShadowToleranceMeters)
                    {
                        block.R040BShadowFailures++;

                        CaptureR040BFailureSample(
                            block,
                            local,
                            localX,
                            localY,
                            x,
                            y,
                            latitude,
                            longitude,
                            expected,
                            actual,
                            error,
                            px,
                            py,
                            pz);
                    }
                }
            }
            catch (Exception ex)
            {
                block.R040BShadowFailures++;
                block.R040BShadowMaxErrorMeters =
                    double.PositiveInfinity;
                block.R040BShadowError =
                    ex.GetType().Name + ":" +
                    (ex.Message ?? string.Empty);
            }
        }

        static void ProcessBlock(BlockPayload block)
        {
            ProcessR040BMinmusShadow(block);

            block.Valid = 0;
            block.Minimum = float.PositiveInfinity;
            block.Maximum = float.NegativeInfinity;
            for (int i = 0; i < block.Elevation.Length; i++)
            {
                if (block.Flags[i] == 0) continue;
                float value = block.Elevation[i];
                block.Valid++;
                block.Minimum = Math.Min(block.Minimum, value);
                block.Maximum = Math.Max(block.Maximum, value);
            }
        }

        static double R040BDiagnosticError(double first, double second)
        {
            if (double.IsNaN(first) ||
                double.IsInfinity(first) ||
                double.IsNaN(second) ||
                double.IsInfinity(second))
                return double.PositiveInfinity;

            return Math.Abs(first - second);
        }

        static string R040BDiagnosticNumber(double value)
        {
            return value.ToString(
                "R",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        void DiagnoseR040BMinmusFailures(
            TileState state,
            BlockPayload block)
        {
            if (state == null ||
                block == null ||
                state.Body == null ||
                state.R040BMinmusSnapshot == null ||
                block.R040BFailureSamples == null ||
                block.R040BFailureSampleCount <= 0)
                return;

            int currentThreadId =
                Thread.CurrentThread.ManagedThreadId;

            if (currentThreadId != r040bMainThreadId)
            {
                AERISLogger.Warn(
                    "[R040B][MINMUS_FAILURE_DIAGNOSTIC_SKIP]" +
                    "; reason=COMMIT_NOT_MAIN_THREAD" +
                    "; current_thread_id=" + currentThreadId +
                    "; expected_main_thread_id=" + r040bMainThreadId +
                    "; producer_switch=false" +
                    "; db_authority=PQS" +
                    "; diagnostic_only=true");
                return;
            }

            int count = Math.Min(
                block.R040BFailureSampleCount,
                block.R040BFailureSamples.Length);

            for (int i = 0; i < count; i++)
            {
                if (state.R040BDiagnosticSamplesEmitted >=
                    R040BMaximumFailureDiagnosticsPerTile)
                    return;

                R040BFailureSample sample =
                    block.R040BFailureSamples[i];

                state.R040BDiagnosticSamplesEmitted++;

                try
                {
                    Vector3d native =
                        state.Body.GetRelSurfaceNVector(
                            sample.LatitudeDeg,
                            sample.LongitudeDeg);

                    double nativeMeters =
                        AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                            state.R040BMinmusSnapshot,
                            native.x,
                            native.y,
                            native.z,
                            0.0);

                    double errorAB =
                        R040BDiagnosticError(
                            sample.ExpectedPqsMeters,
                            sample.WorkerTrigMeters);

                    double errorAC =
                        R040BDiagnosticError(
                            sample.ExpectedPqsMeters,
                            nativeMeters);

                    double errorBC =
                        R040BDiagnosticError(
                            sample.WorkerTrigMeters,
                            nativeMeters);

                    bool aEqualsC =
                        errorAC <= R040BShadowToleranceMeters;

                    bool bEqualsC =
                        errorBC <= R040BShadowToleranceMeters;

                    string relation;
                    if (aEqualsC && !bEqualsC)
                        relation = "A_EQ_C_B_DIFF";
                    else if (!aEqualsC && bEqualsC)
                        relation = "B_EQ_C_A_DIFF";
                    else if (!aEqualsC && !bEqualsC)
                        relation = "ALL_DIFFER";
                    else
                        relation = "A_EQ_B_EQ_C";

                    double vectorDelta = Math.Max(
                        Math.Abs(native.x - sample.TrigX),
                        Math.Max(
                            Math.Abs(native.y - sample.TrigY),
                            Math.Abs(native.z - sample.TrigZ)));

                    string tileName =
                        state.Request == null ?
                        "<null>" :
                        state.Request.Key.FileStem;

                    AERISLogger.Info(
                        "[R040B][MINMUS_FAILURE_DIAGNOSTIC]" +
                        "; tile=" + tileName +
                        "; resolution=" + block.Resolution +
                        "; block_id=" + sample.BlockId +
                        "; x=" + sample.X +
                        "; y=" + sample.Y +
                        "; local_x=" + sample.LocalX +
                        "; local_y=" + sample.LocalY +
                        "; latitude_deg=" +
                            R040BDiagnosticNumber(sample.LatitudeDeg) +
                        "; longitude_deg=" +
                            R040BDiagnosticNumber(sample.LongitudeDeg) +
                        "; boundary=" + sample.Boundary +
                        "; cache_hit=" + sample.CacheHit +
                        "; A_pqs_m=" +
                            R040BDiagnosticNumber(
                                sample.ExpectedPqsMeters) +
                        "; B_trig_m=" +
                            R040BDiagnosticNumber(
                                sample.WorkerTrigMeters) +
                        "; C_native_m=" +
                            R040BDiagnosticNumber(nativeMeters) +
                        "; error_AB_m=" +
                            R040BDiagnosticNumber(errorAB) +
                        "; error_AC_m=" +
                            R040BDiagnosticNumber(errorAC) +
                        "; error_BC_m=" +
                            R040BDiagnosticNumber(errorBC) +
                        "; trig_x=" +
                            R040BDiagnosticNumber(sample.TrigX) +
                        "; trig_y=" +
                            R040BDiagnosticNumber(sample.TrigY) +
                        "; trig_z=" +
                            R040BDiagnosticNumber(sample.TrigZ) +
                        "; native_x=" +
                            R040BDiagnosticNumber(native.x) +
                        "; native_y=" +
                            R040BDiagnosticNumber(native.y) +
                        "; native_z=" +
                            R040BDiagnosticNumber(native.z) +
                        "; vector_max_component_delta=" +
                            R040BDiagnosticNumber(vectorDelta) +
                        "; relation=" + relation +
                        "; worker_thread_id=" +
                            sample.WorkerThreadId +
                        "; main_thread_id=" +
                            r040bMainThreadId +
                        "; diagnostic_index=" +
                            state.R040BDiagnosticSamplesEmitted +
                        "; diagnostic_limit=" +
                            R040BMaximumFailureDiagnosticsPerTile +
                        "; tolerance_m=1E-08" +
                        "; producer_switch=false" +
                        "; db_authority=PQS" +
                        "; diagnostic_only=true");
                }
                catch (Exception ex)
                {
                    AERISLogger.Warn(
                        "[R040B][MINMUS_FAILURE_DIAGNOSTIC_ERROR]" +
                        "; block_id=" + sample.BlockId +
                        "; x=" + sample.X +
                        "; y=" + sample.Y +
                        "; error=" +
                            ex.GetType().Name + ":" +
                            (ex.Message ?? string.Empty) +
                        "; producer_switch=false" +
                        "; db_authority=PQS" +
                        "; diagnostic_only=true");
                }
            }
        }

        void CommitBlock(TileState state, BlockPayload block)
        {
            if (block.R040BShadowSamples > 0 ||
                block.R040BShadowFailures > 0 ||
                !string.IsNullOrEmpty(block.R040BShadowError))
            {
                state.R040BShadowSamples +=
                    block.R040BShadowSamples;

                state.R040BShadowFailures +=
                    block.R040BShadowFailures;

                state.R040BShadowMaxErrorMeters =
                    Math.Max(
                        state.R040BShadowMaxErrorMeters,
                        block.R040BShadowMaxErrorMeters);

                state.R040BAllWorkersOffMainThread =
                    state.R040BAllWorkersOffMainThread &&
                    block.R040BWorkerThreadId != r040bMainThreadId;

                if (string.IsNullOrEmpty(state.R040BShadowError) &&
                    !string.IsNullOrEmpty(block.R040BShadowError))
                    state.R040BShadowError =
                        block.R040BShadowError;
            }

            DiagnoseR040BMinmusFailures(state, block);

            int resolution = state.Request.Resolution;
            for (int y = 0; y < block.Height; y++)
            {
                int source = y * block.Width;
                int destination = (block.Y0 + y) * resolution + block.X0;
                Array.Copy(block.Elevation, source, state.Elevation, destination,
                    block.Width);
                Array.Copy(block.Flags, source, state.Flags, destination,
                    block.Width);
            }
            state.Valid += block.Valid;
            if (block.Valid > 0)
            {
                state.Minimum = Math.Min(state.Minimum, block.Minimum);
                state.Maximum = Math.Max(state.Maximum, block.Maximum);
            }
            state.CompletedBlocks++;
            blocksCompleted++;
            int percent = state.CompletedBlocks * 100 /
                Math.Max(1, state.Blocks.Length);
            bool final = state.CompletedBlocks >= state.Blocks.Length;
            bool intermediateDue = !final &&
                percent >= state.LastPublishedPercent + 25;
            bool preloadFinalOnly = state.Request.WorkOwner ==
                AERISTerrainWorkOwner.PreloadBuilder;
            bool publish = final || (intermediateDue && !preloadFinalOnly);
            if (intermediateDue && preloadFinalOnly)
            {
                state.LastPublishedPercent = percent;
                intermediateCommitsSkipped++;
            }
            if (publish)
            {
                state.LastPublishedPercent = percent;
                AERISTerrainHeightTile tile = BuildTile(state, !final);
                if (tile != null && state.Commit != null)
                    state.Commit(state.Request, tile, final);
            }
            if (final)
            {
                ReportR040BMinmusShadow(state);
                RemoveState(state, false);
            }
        }

        void ReportR040BMinmusShadow(TileState state)
        {
            if (state == null ||
                state.R040BMinmusSnapshot == null)
                return;

            r040bShadowTiles++;
            r040bShadowSamples += state.R040BShadowSamples;
            r040bShadowFailures += state.R040BShadowFailures;

            r040bShadowMaxErrorMeters =
                Math.Max(
                    r040bShadowMaxErrorMeters,
                    state.R040BShadowMaxErrorMeters);

            bool pass =
                state.R040BShadowSamples > 0 &&
                state.R040BShadowFailures == 0 &&
                state.R040BAllWorkersOffMainThread &&
                string.IsNullOrEmpty(state.R040BShadowError);

            bool emit =
                !pass ||
                r040bShadowTiles == 1 ||
                (r040bShadowTiles % 32L) == 0L;

            if (!emit)
                return;

            string tileName =
                state.Request == null ?
                "<null>" :
                state.Request.Key.FileStem;

            AERISLogger.Info(
                "[R040B][MINMUS_BLOCK_SHADOW]" +
                "; pass=" + pass +
                "; body=Minmus" +
                "; tile=" + tileName +
                "; completed_shadow_tiles=" +
                    r040bShadowTiles +
                "; tile_samples=" +
                    state.R040BShadowSamples +
                "; tile_failures=" +
                    state.R040BShadowFailures +
                "; tile_max_error_m=" +
                    state.R040BShadowMaxErrorMeters.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture) +
                "; cumulative_samples=" +
                    r040bShadowSamples +
                "; cumulative_failures=" +
                    r040bShadowFailures +
                "; cumulative_max_error_m=" +
                    r040bShadowMaxErrorMeters.ToString(
                        "R",
                        System.Globalization.CultureInfo.InvariantCulture) +
                "; tolerance_m=1E-08" +
                "; all_workers_off_main_thread=" +
                    state.R040BAllWorkersOffMainThread +
                "; error=" +
                    (string.IsNullOrEmpty(state.R040BShadowError) ?
                     "-" : state.R040BShadowError) +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; worker_runtime_object_access=false");
        }

        static AERISTerrainHeightTile BuildTile(TileState state, bool partial)
        {
            if (state.Valid <= 0) return null;
            return new AERISTerrainHeightTile
            {
                Key = state.Request.Key,
                Resolution = state.Request.Resolution,
                SouthLatitudeDeg = state.Request.SouthLatitudeDeg,
                NorthLatitudeDeg = state.Request.NorthLatitudeDeg,
                WestLongitudeDeg = state.Request.WestLongitudeDeg,
                EastLongitudeDeg = state.Request.EastLongitudeDeg,
                MinimumElevationMeters = float.IsPositiveInfinity(state.Minimum) ? 0f :
                    state.Minimum,
                MaximumElevationMeters = float.IsNegativeInfinity(state.Maximum) ? 0f :
                    state.Maximum,
                Elevation = (float[])state.Elevation.Clone(),
                Flags = (byte[])state.Flags.Clone(),
                CreatedUtcTicks = DateTime.UtcNow.Ticks,
                Quality = state.Valid * 100 / Math.Max(1, state.Elevation.Length),
                IsPreview = state.Request.Stage ==
                    AERISTerrainSamplingStage.Preview,
                SamplingComplete = !partial,
                Source = state.Source,
                PqsConfigurationHash = state.PqsHash,
                GameDataHash = state.GameDataHash,
                TerrainGenerationId = state.TerrainGenerationId
            };
        }

        void RemoveState(TileState state, bool stale)
        {
            if (state == null) return;
            lock (sync)
            {
                state.Cancelled = true;
                states.Remove(state.Id);
                roundRobin.Remove(state);
                if (roundRobinIndex > roundRobin.Count) roundRobinIndex = 0;
            }
            if (stale) staleDiscarded++;
        }

        bool Current(TileState state)
        {
            return state != null && !state.Cancelled &&
                (state.IsCurrent == null || state.IsCurrent(state.Request));
        }

        static BlockDefinition[] BuildBlocks(int resolution)
        {
            int edge = resolution <= 9 ? 2 : resolution <= 17 ? 2 : 4;
            var blocks = new List<BlockDefinition>(edge * edge);
            int id = 0;
            for (int by = 0; by < edge; by++)
            {
                int y0 = by * resolution / edge;
                int y1 = (by + 1) * resolution / edge;
                for (int bx = 0; bx < edge; bx++)
                {
                    int x0 = bx * resolution / edge;
                    int x1 = (bx + 1) * resolution / edge;
                    blocks.Add(new BlockDefinition
                    {
                        Id = id++, X0 = x0, Y0 = y0,
                        Width = Math.Max(1, x1 - x0),
                        Height = Math.Max(1, y1 - y0)
                    });
                }
            }
            return blocks.ToArray();
        }

        static string WorkId(AERISTerrainTileRequest request)
        {
            return request.Key.StableId + "|" + (int)request.WorkOwner + "|" +
                (int)request.Stage + "|" + request.Resolution;
        }

        static int Compare(AERISTerrainTileRequest a, AERISTerrainTileRequest b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int lane = ((int)a.ReadLane).CompareTo((int)b.ReadLane);
            if (lane != 0) return lane;
            int visible = (b.Visible ? 1 : 0).CompareTo(a.Visible ? 1 : 0);
            if (visible != 0) return visible;
            int stage = ((int)a.Stage).CompareTo((int)b.Stage);
            if (stage != 0) return stage;
            int priority = ((int)b.Priority).CompareTo((int)a.Priority);
            if (priority != 0) return priority;
            return a.ViewDistanceMeters.CompareTo(b.ViewDistanceMeters);
        }

        static double InterpolateLongitude(double west, double east, double t)
        {
            double delta = NormalizeLongitude(east - west);
            if (delta <= 0.0) delta += 360.0;
            return NormalizeLongitude(west + delta * t);
        }

        static double NormalizeLongitude(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Interlocked.Increment(ref outstandingEpoch);
            Interlocked.Exchange(ref outstandingBlocks, 0);
            CancelWhere(null);
            boundarySamples.Clear();
            boundarySampleOrder.Clear();
        }
    }
}
