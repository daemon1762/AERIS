using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Performance;

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
            internal int SamplingIndex;
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

        internal AERISTerrainBlockPipeline(AERISTerrainPerformanceController performance)
        {
            this.performance = performance;
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
                    IsCurrent = isCurrent,
                    Commit = commit
                };
                states[id] = state;
                roundRobin.Add(state);
                return true;
            }
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
            state.SamplingElevation = new float[block.Width * block.Height];
            state.SamplingFlags = new byte[block.Width * block.Height];
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
                if (boundary) PutBoundarySample(cacheKey, storedElevation, flag);
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

        void PutBoundarySample(BoundarySampleKey key, float elevation, byte flag)
        {
            BoundarySampleValue existing;
            if (boundarySamples.TryGetValue(key, out existing)) return;
            long sequence = ++boundarySampleSequence;
            boundarySamples[key] = new BoundarySampleValue
            {
                Elevation = elevation,
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
                Flags = state.SamplingFlags
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

        static void ProcessBlock(BlockPayload block)
        {
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

        void CommitBlock(TileState state, BlockPayload block)
        {
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
            if (final) RemoveState(state, false);
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
