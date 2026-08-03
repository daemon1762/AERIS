using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using AERISFlightControl.Landing;
using AERISFlightControl.Logging;
using AERISFlightControl.Terrain;

namespace AERISFlightControl.Performance
{
    // Metadata-only terrain index entry. It deliberately contains no compressed payload,
    // decoded height samples, RenderTexture, mesh or GPU state. Gate 4 is the Map DRAM
    // Cache; Current-Body terrain payload residency remains a separate CP3 responsibility.
    internal sealed class AERISMapTerrainIndexEntry
    {
        internal readonly string StableId;
        internal readonly AERISTerrainTileKey Key;
        internal readonly string ChunkId;
        internal readonly string RelativePath;
        internal readonly long StoredBytes;
        internal readonly long GenerationUtcTicks;
        internal readonly int Quality;
        internal readonly AERISTerrainGenerationState State;

        internal AERISMapTerrainIndexEntry(string stableId,
            AERISTerrainTileKey key, string chunkId, string relativePath,
            long storedBytes, long generationUtcTicks, int quality,
            AERISTerrainGenerationState state)
        {
            StableId = stableId ?? string.Empty;
            Key = key;
            ChunkId = chunkId ?? string.Empty;
            RelativePath = relativePath ?? string.Empty;
            StoredBytes = Math.Max(0L, storedBytes);
            GenerationUtcTicks = generationUtcTicks;
            Quality = quality;
            State = state;
        }

        internal long EstimatedBytes
        {
            get
            {
                return 160L + EstimateString(StableId) + EstimateString(ChunkId) +
                    EstimateString(RelativePath) + EstimateString(Key.BodyName) +
                    EstimateString(Key.EnvironmentHash);
            }
        }

        static long EstimateString(string value)
        {
            return string.IsNullOrEmpty(value) ? 0L : 24L + value.Length * 2L;
        }
    }

    internal sealed class AERISMapDramTelemetrySnapshot
    {
        internal long Revision;
        internal long AirfieldRevision;
        internal long TerrainIndexRevision;
        internal long PublishedUtcTicks;
        internal int AirfieldCount;
        internal int RunwayCount;
        internal int DirectionCount;
        internal int TerrainTileCount;
        internal int TerrainChunkCount;
        internal int TerrainGlobalCount;
        internal int TerrainFarCount;
        internal int TerrainRouteCount;
        internal int TerrainLocalCount;
        internal int TerrainLandCount;
        internal long EstimatedBytes;
        internal double LastAirfieldPublishMilliseconds;
        internal double LastTerrainPublishMilliseconds;
        internal long AirfieldLookupHits;
        internal long AirfieldLookupMisses;
        internal long TerrainLookupHits;
        internal long TerrainLookupMisses;
        internal long GuardedSynchronousDiskOperations;
        internal long AllowedSynchronousDiskOperations;
        internal long SynchronousDiskLookups;
        internal long LastSynchronousDiskLookupUtcTicks;
        internal string LastSynchronousDiskLookupDomain = string.Empty;
        internal string LastSynchronousDiskLookupOperation = string.Empty;
        internal string Status = "EMPTY";
    }

    internal sealed class AERISMapDramSnapshot
    {
        static readonly ReadOnlyCollection<AERISAirfieldDefinition> EmptyAirfields =
            new List<AERISAirfieldDefinition>().AsReadOnly();

        readonly ReadOnlyCollection<AERISAirfieldDefinition> airfields;
        readonly Dictionary<string, AERISAirfieldDefinition> airfieldById;
        readonly Dictionary<string, AERISRunwayDefinition> runwayById;
        readonly Dictionary<string, AERISRunwayDirectionDefinition> directionById;
        readonly Dictionary<string, AERISMapTerrainIndexEntry> terrainById;
        readonly Dictionary<string, string> terrainChunkById;
        readonly int[] terrainLodCounts;

        internal readonly long Revision;
        internal readonly long AirfieldRevision;
        internal readonly long TerrainIndexRevision;
        internal readonly long PublishedUtcTicks;
        internal readonly int RunwayCount;
        internal readonly int DirectionCount;
        internal readonly int TerrainChunkCount;
        internal readonly long AirfieldEstimatedBytes;
        internal readonly long TerrainEstimatedBytes;
        internal readonly long EstimatedBytes;
        internal readonly double AirfieldPublishMilliseconds;
        internal readonly double TerrainPublishMilliseconds;
        internal readonly string Status;

        internal static AERISMapDramSnapshot Empty
        {
            get
            {
                return new AERISMapDramSnapshot(0L, 0L, 0L,
                    EmptyAirfields,
                    new Dictionary<string, AERISAirfieldDefinition>(
                        StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, AERISRunwayDefinition>(
                        StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, AERISRunwayDirectionDefinition>(
                        StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, AERISMapTerrainIndexEntry>(
                        StringComparer.Ordinal),
                    new Dictionary<string, string>(StringComparer.Ordinal),
                    new int[5], 0, 0, 0L, 0L, 0.0, 0.0, "EMPTY");
            }
        }

        AERISMapDramSnapshot(long revision, long airfieldRevision,
            long terrainIndexRevision,
            ReadOnlyCollection<AERISAirfieldDefinition> airfields,
            Dictionary<string, AERISAirfieldDefinition> airfieldById,
            Dictionary<string, AERISRunwayDefinition> runwayById,
            Dictionary<string, AERISRunwayDirectionDefinition> directionById,
            Dictionary<string, AERISMapTerrainIndexEntry> terrainById,
            Dictionary<string, string> terrainChunkById, int[] terrainLodCounts,
            int runwayCount, int directionCount, long airfieldEstimatedBytes,
            long terrainEstimatedBytes, double airfieldPublishMilliseconds,
            double terrainPublishMilliseconds, string status)
        {
            Revision = revision;
            AirfieldRevision = airfieldRevision;
            TerrainIndexRevision = terrainIndexRevision;
            PublishedUtcTicks = DateTime.UtcNow.Ticks;
            this.airfields = airfields ?? EmptyAirfields;
            this.airfieldById = airfieldById ??
                new Dictionary<string, AERISAirfieldDefinition>(
                    StringComparer.OrdinalIgnoreCase);
            this.runwayById = runwayById ??
                new Dictionary<string, AERISRunwayDefinition>(
                    StringComparer.OrdinalIgnoreCase);
            this.directionById = directionById ??
                new Dictionary<string, AERISRunwayDirectionDefinition>(
                    StringComparer.OrdinalIgnoreCase);
            this.terrainById = terrainById ??
                new Dictionary<string, AERISMapTerrainIndexEntry>(StringComparer.Ordinal);
            this.terrainChunkById = terrainChunkById ??
                new Dictionary<string, string>(StringComparer.Ordinal);
            this.terrainLodCounts = terrainLodCounts == null ? new int[5] :
                (int[])terrainLodCounts.Clone();
            RunwayCount = Math.Max(0, runwayCount);
            DirectionCount = Math.Max(0, directionCount);
            TerrainChunkCount = this.terrainChunkById.Count;
            AirfieldEstimatedBytes = Math.Max(0L, airfieldEstimatedBytes);
            TerrainEstimatedBytes = Math.Max(0L, terrainEstimatedBytes);
            EstimatedBytes = AirfieldEstimatedBytes + TerrainEstimatedBytes;
            AirfieldPublishMilliseconds = Math.Max(0.0, airfieldPublishMilliseconds);
            TerrainPublishMilliseconds = Math.Max(0.0, terrainPublishMilliseconds);
            Status = status ?? string.Empty;
        }

        internal ReadOnlyCollection<AERISAirfieldDefinition> Airfields
        {
            get { return airfields; }
        }

        internal int TerrainTileCount { get { return terrainById.Count; } }

        internal int TerrainLodCount(AERISTerrainTileLod lod)
        {
            int index = (int)lod;
            return index < 0 || index >= terrainLodCounts.Length ? 0 :
                terrainLodCounts[index];
        }

        internal bool TryGetAirfield(string stableId,
            out AERISAirfieldDefinition airfield)
        {
            airfield = null;
            return !string.IsNullOrEmpty(stableId) &&
                airfieldById.TryGetValue(stableId, out airfield) && airfield != null;
        }

        internal bool TryGetRunway(string stableId,
            out AERISRunwayDefinition runway)
        {
            runway = null;
            return !string.IsNullOrEmpty(stableId) &&
                runwayById.TryGetValue(stableId, out runway) && runway != null;
        }

        internal bool TryGetDirection(string stableId,
            out AERISRunwayDirectionDefinition direction)
        {
            direction = null;
            return !string.IsNullOrEmpty(stableId) &&
                directionById.TryGetValue(stableId, out direction) && direction != null;
        }

        internal bool TryGetTerrain(AERISTerrainTileKey key,
            out AERISMapTerrainIndexEntry entry)
        {
            return terrainById.TryGetValue(key.StableId, out entry) && entry != null;
        }

        internal bool TryGetTerrainChunkId(AERISTerrainTileKey key,
            out string chunkId)
        {
            chunkId = string.Empty;
            AERISMapTerrainIndexEntry entry;
            if (!TryGetTerrain(key, out entry) ||
                entry.State != AERISTerrainGenerationState.Complete ||
                string.IsNullOrEmpty(entry.ChunkId)) return false;
            chunkId = entry.ChunkId;
            return true;
        }

        internal static AERISMapDramSnapshot WithAirfields(
            AERISMapDramSnapshot previous, IList<AERISAirfieldDefinition> source,
            long revision, long airfieldRevision, string cause)
        {
            long startTicks = Stopwatch.GetTimestamp();
            previous = previous ?? Empty;
            var values = new List<AERISAirfieldDefinition>();
            var byAirfield = new Dictionary<string, AERISAirfieldDefinition>(
                StringComparer.OrdinalIgnoreCase);
            var byRunway = new Dictionary<string, AERISRunwayDefinition>(
                StringComparer.OrdinalIgnoreCase);
            var byDirection = new Dictionary<string, AERISRunwayDirectionDefinition>(
                StringComparer.OrdinalIgnoreCase);
            int runwayCount = 0;
            int directionCount = 0;
            long bytes = 512L;
            if (source != null)
                for (int i = 0; i < source.Count; i++)
                {
                    AERISAirfieldDefinition sourceAirfield = source[i];
                    if (sourceAirfield == null) continue;
                    AERISAirfieldDefinition airfield = sourceAirfield.Clone();
                    values.Add(airfield);
                    if (!string.IsNullOrEmpty(airfield.StableId))
                        byAirfield[airfield.StableId] = airfield;
                    bytes += EstimateAirfieldBytes(airfield);
                    for (int j = 0; j < airfield.Runways.Count; j++)
                    {
                        AERISRunwayDefinition runway = airfield.Runways[j];
                        if (runway == null) continue;
                        runwayCount++;
                        if (!string.IsNullOrEmpty(runway.StableId))
                            byRunway[runway.StableId] = runway;
                        for (int k = 0; k < runway.Directions.Count; k++)
                        {
                            AERISRunwayDirectionDefinition direction =
                                runway.Directions[k];
                            if (direction == null) continue;
                            directionCount++;
                            if (!string.IsNullOrEmpty(direction.StableId))
                                byDirection[direction.StableId] = direction;
                        }
                    }
                }
            double elapsedMilliseconds = (Stopwatch.GetTimestamp() - startTicks) *
                1000.0 / Stopwatch.Frequency;
            return new AERISMapDramSnapshot(revision, airfieldRevision,
                previous.TerrainIndexRevision, values.AsReadOnly(), byAirfield,
                byRunway, byDirection, previous.terrainById,
                previous.terrainChunkById, previous.terrainLodCounts,
                runwayCount, directionCount, bytes, previous.TerrainEstimatedBytes,
                elapsedMilliseconds, previous.TerrainPublishMilliseconds,
                BuildStatus(values.Count, runwayCount, directionCount,
                    previous.terrainById.Count, cause));
        }

        internal static AERISMapDramSnapshot WithTerrain(
            AERISMapDramSnapshot previous,
            IList<AERISMapTerrainIndexEntry> entries,
            long revision, long terrainIndexRevision, string cause)
        {
            long startTicks = Stopwatch.GetTimestamp();
            previous = previous ?? Empty;
            var terrain = new Dictionary<string, AERISMapTerrainIndexEntry>(
                StringComparer.Ordinal);
            var chunks = new Dictionary<string, string>(StringComparer.Ordinal);
            var lodCounts = new int[5];
            long terrainBytes = 512L;
            if (entries != null)
                for (int i = 0; i < entries.Count; i++)
                {
                    AERISMapTerrainIndexEntry entry = entries[i];
                    if (entry == null || string.IsNullOrEmpty(entry.StableId)) continue;
                    terrain[entry.StableId] = entry;
                    terrainBytes += entry.EstimatedBytes;
                    if (!string.IsNullOrEmpty(entry.ChunkId))
                        chunks[entry.ChunkId] = entry.RelativePath;
                    int lod = (int)entry.Key.Lod;
                    if (lod >= 0 && lod < lodCounts.Length) lodCounts[lod]++;
                }
            double elapsedMilliseconds = (Stopwatch.GetTimestamp() - startTicks) *
                1000.0 / Stopwatch.Frequency;
            return new AERISMapDramSnapshot(revision,
                previous.AirfieldRevision, terrainIndexRevision,
                previous.airfields, previous.airfieldById, previous.runwayById,
                previous.directionById, terrain, chunks, lodCounts,
                previous.RunwayCount, previous.DirectionCount,
                previous.AirfieldEstimatedBytes, terrainBytes,
                previous.AirfieldPublishMilliseconds, elapsedMilliseconds,
                BuildStatus(previous.airfields.Count, previous.RunwayCount,
                    previous.DirectionCount, terrain.Count, cause));
        }

        static string BuildStatus(int airfields, int runways, int directions,
            int terrainTiles, string cause)
        {
            return "READY / DRAM-ONLY LOOKUP / AIRFIELD " + airfields +
                " / RWY " + runways + " / ILS-DIR " + directions +
                " / TERRAIN INDEX " + terrainTiles + " / " +
                (string.IsNullOrEmpty(cause) ? "PUBLISH" : cause.ToUpperInvariant());
        }

        static long EstimateAirfieldBytes(IList<AERISAirfieldDefinition> values)
        {
            long bytes = 512L;
            if (values == null) return bytes;
            for (int i = 0; i < values.Count; i++)
                bytes += EstimateAirfieldBytes(values[i]);
            return bytes;
        }

        static long EstimateAirfieldBytes(AERISAirfieldDefinition value)
        {
            if (value == null) return 0L;
            long bytes = 512L + EstimateString(value.Id) + EstimateString(value.Body) +
                EstimateString(value.DisplayName) + EstimateString(value.Description) +
                EstimateString(value.ProviderSiteId) + EstimateString(value.ProviderGroup) +
                EstimateString(value.ProviderUuid) + EstimateString(value.SourcePath);
            for (int i = 0; i < value.Runways.Count; i++)
            {
                AERISRunwayDefinition runway = value.Runways[i];
                if (runway == null) continue;
                bytes += 512L + EstimateString(runway.StableId) +
                    EstimateString(runway.DisplayName);
                bytes += runway.UsablePolygon.Count * 48L +
                    runway.WidthProfileMeters.Count * sizeof(double);
                for (int j = 0; j < runway.Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction = runway.Directions[j];
                    if (direction == null) continue;
                    bytes += 384L + EstimateString(direction.StableId) +
                        EstimateString(direction.DisplayName);
                }
            }
            return bytes;
        }

        static long EstimateTerrainBytes(
            Dictionary<string, AERISMapTerrainIndexEntry> values)
        {
            long bytes = 512L;
            if (values == null) return bytes;
            foreach (AERISMapTerrainIndexEntry entry in values.Values)
                if (entry != null) bytes += entry.EstimatedBytes;
            return bytes;
        }

        static long EstimateString(string value)
        {
            return string.IsNullOrEmpty(value) ? 0L : 24L + value.Length * 2L;
        }
    }

    // Single ownership point for map metadata resident in DRAM. This class never opens,
    // reads, writes, enumerates or probes files. Producers perform bounded startup or
    // maintenance I/O and atomically publish immutable revision snapshots here.
    internal sealed class AERISMapDramCache
    {
        readonly object publishSync = new object();
        volatile AERISMapDramSnapshot current = AERISMapDramSnapshot.Empty;
        long revision;
        long airfieldLookupHits;
        long airfieldLookupMisses;
        long terrainLookupHits;
        long terrainLookupMisses;
        long guardedSynchronousDiskOperations;
        long allowedSynchronousDiskOperations;
        long synchronousDiskLookups;
        long lastSynchronousDiskLookupUtcTicks;
        string lastSynchronousDiskLookupDomain = string.Empty;
        string lastSynchronousDiskLookupOperation = string.Empty;
        long suppressedRoutineTerrainPublishLogs;

        internal void PublishAirfields(IList<AERISAirfieldDefinition> airfields,
            long sourceRevision, string cause)
        {
            lock (publishSync)
            {
                long next = ++revision;
                current = AERISMapDramSnapshot.WithAirfields(current, airfields,
                    next, Math.Max(0L, sourceRevision), cause);
                AERISLogger.Info("[CP2.5/MAP_DRAM] domain=AIRFIELD; revision=" +
                    current.Revision + "; sourceRevision=" + current.AirfieldRevision +
                    "; airfields=" + current.Airfields.Count + "; runways=" +
                    current.RunwayCount + "; directions=" + current.DirectionCount +
                    "; publish_ms=" + current.AirfieldPublishMilliseconds.ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "; normalLookup=DRAM_ONLY; cause=" + (cause ?? string.Empty) + ".");
            }
        }

        internal void PublishTerrainIndex(IList<AERISMapTerrainIndexEntry> entries,
            long sourceRevision, string cause)
        {
            lock (publishSync)
            {
                long next = ++revision;
                current = AERISMapDramSnapshot.WithTerrain(current, entries, next,
                    Math.Max(0L, sourceRevision), cause);
                bool routineCommit = string.Equals(cause, "INDEX_COMMIT",
                    StringComparison.Ordinal);
                if (routineCommit) suppressedRoutineTerrainPublishLogs++;
                if (!routineCommit || suppressedRoutineTerrainPublishLogs >= 64L)
                {
                    long suppressed = routineCommit ?
                        suppressedRoutineTerrainPublishLogs - 1L : 0L;
                    AERISLogger.Info("[CP2.5/MAP_DRAM] domain=TERRAIN_INDEX; revision=" +
                        current.Revision + "; sourceRevision=" +
                        current.TerrainIndexRevision + "; tiles=" +
                        current.TerrainTileCount + "; chunks=" +
                        current.TerrainChunkCount + "; publish_ms=" +
                        current.TerrainPublishMilliseconds.ToString("0.000",
                            System.Globalization.CultureInfo.InvariantCulture) +
                        "; payloadBytes=0; normalLookup=DRAM_ONLY; cause=" +
                        (cause ?? string.Empty) + "; suppressedRoutinePublishes=" +
                        suppressed + ".");
                    if (routineCommit) suppressedRoutineTerrainPublishLogs = 0L;
                }
            }
        }

        internal IList<AERISAirfieldDefinition> SnapshotAirfields()
        {
            using (AERISMapDramDiskGuard.EnterNormalLookup(this, "AIRFIELD_LIST"))
            {
                AERISMapDramSnapshot snapshot = current;
                bool ready = snapshot.AirfieldRevision > 0L;
                if (ready) Interlocked.Increment(ref airfieldLookupHits);
                else Interlocked.Increment(ref airfieldLookupMisses);
                return snapshot.Airfields;
            }
        }

        internal bool TryGetAirfieldView(string stableId,
            out AERISAirfieldDefinition airfield)
        {
            using (AERISMapDramDiskGuard.EnterNormalLookup(this, "AIRFIELD_ID"))
            {
                bool found = current.TryGetAirfield(stableId, out airfield);
                if (found) Interlocked.Increment(ref airfieldLookupHits);
                else Interlocked.Increment(ref airfieldLookupMisses);
                return found;
            }
        }

        internal bool TryGetRunwayView(string stableId,
            out AERISRunwayDefinition runway)
        {
            using (AERISMapDramDiskGuard.EnterNormalLookup(this, "RUNWAY_ID"))
            {
                bool found = current.TryGetRunway(stableId, out runway);
                if (found) Interlocked.Increment(ref airfieldLookupHits);
                else Interlocked.Increment(ref airfieldLookupMisses);
                return found;
            }
        }

        internal bool TryGetDirectionView(string stableId,
            out AERISRunwayDirectionDefinition direction)
        {
            using (AERISMapDramDiskGuard.EnterNormalLookup(this, "ILS_DIRECTION_ID"))
            {
                bool found = current.TryGetDirection(stableId, out direction);
                if (found) Interlocked.Increment(ref airfieldLookupHits);
                else Interlocked.Increment(ref airfieldLookupMisses);
                return found;
            }
        }

        internal bool TryGetAirfield(string stableId,
            out AERISAirfieldDefinition airfield)
        {
            AERISAirfieldDefinition stored;
            bool found = TryGetAirfieldView(stableId, out stored);
            airfield = found && stored != null ? stored.Clone() : null;
            return found;
        }

        internal bool TryGetRunway(string stableId,
            out AERISRunwayDefinition runway)
        {
            AERISRunwayDefinition stored;
            bool found = TryGetRunwayView(stableId, out stored);
            runway = found && stored != null ? stored.Clone() : null;
            return found;
        }

        internal bool TryGetDirection(string stableId,
            out AERISRunwayDirectionDefinition direction)
        {
            AERISRunwayDirectionDefinition stored;
            bool found = TryGetDirectionView(stableId, out stored);
            direction = found && stored != null ? stored.Clone() : null;
            return found;
        }

        internal bool ContainsTerrain(AERISTerrainTileKey key)
        {
            using (AERISMapDramDiskGuard.EnterNormalLookup(this, "TERRAIN_CONTAINS"))
            {
                AERISMapTerrainIndexEntry entry;
                bool found = current.TryGetTerrain(key, out entry) && entry != null &&
                    entry.State == AERISTerrainGenerationState.Complete;
                if (found) Interlocked.Increment(ref terrainLookupHits);
                else Interlocked.Increment(ref terrainLookupMisses);
                return found;
            }
        }

        internal bool TryGetTerrainChunkId(AERISTerrainTileKey key,
            out string chunkId)
        {
            using (AERISMapDramDiskGuard.EnterNormalLookup(this,
                "TERRAIN_CHUNK_ID"))
            {
                bool found = current.TryGetTerrainChunkId(key, out chunkId);
                if (found) Interlocked.Increment(ref terrainLookupHits);
                else Interlocked.Increment(ref terrainLookupMisses);
                return found;
            }
        }

        internal void RecordSynchronousDiskOperation(bool violation, string domain,
            string operation)
        {
            Interlocked.Increment(ref guardedSynchronousDiskOperations);
            if (!violation)
            {
                Interlocked.Increment(ref allowedSynchronousDiskOperations);
                return;
            }
            long count = Interlocked.Increment(ref synchronousDiskLookups);
            Interlocked.Exchange(ref lastSynchronousDiskLookupUtcTicks,
                DateTime.UtcNow.Ticks);
            lastSynchronousDiskLookupDomain = domain ?? string.Empty;
            lastSynchronousDiskLookupOperation = operation ?? string.Empty;
            if (count <= 4L || (count & (count - 1L)) == 0L)
                AERISLogger.Error("[CP2.5/MAP_DRAM_VIOLATION] count=" + count +
                    "; domain=" + lastSynchronousDiskLookupDomain +
                    "; operation=" + lastSynchronousDiskLookupOperation +
                    "; normalLookup=DRAM_ONLY; synchronousSSD=FORBIDDEN.");
        }

        internal void RecordForbiddenSynchronousDiskLookup(string domain,
            string operation)
        {
            RecordSynchronousDiskOperation(true, domain, operation);
        }

        internal void LogShutdownSummary(string cause)
        {
            AERISMapDramTelemetrySnapshot telemetry = SnapshotTelemetry();
            AERISLogger.Info("[CP2.5/MAP_DRAM_SUMMARY] cause=" +
                (cause ?? string.Empty) + "; revision=" + telemetry.Revision +
                "; airfieldRevision=" + telemetry.AirfieldRevision +
                "; terrainRevision=" + telemetry.TerrainIndexRevision +
                "; airfields=" + telemetry.AirfieldCount +
                "; runways=" + telemetry.RunwayCount +
                "; directions=" + telemetry.DirectionCount +
                "; terrainTiles=" + telemetry.TerrainTileCount +
                "; terrainChunks=" + telemetry.TerrainChunkCount +
                "; guardedSSD=" + telemetry.GuardedSynchronousDiskOperations +
                "; allowedSSD=" + telemetry.AllowedSynchronousDiskOperations +
                "; lookupAirfield=" + telemetry.AirfieldLookupHits + "/" +
                telemetry.AirfieldLookupMisses + "; lookupTerrain=" +
                telemetry.TerrainLookupHits + "/" + telemetry.TerrainLookupMisses +
                "; synchronousSSD=" + telemetry.SynchronousDiskLookups +
                "; result=" + (telemetry.SynchronousDiskLookups == 0L ?
                    "PASS" : "VIOLATION") + ".");
        }

        internal AERISMapDramTelemetrySnapshot SnapshotTelemetry()
        {
            AERISMapDramSnapshot snapshot = current;
            return new AERISMapDramTelemetrySnapshot
            {
                Revision = snapshot.Revision,
                AirfieldRevision = snapshot.AirfieldRevision,
                TerrainIndexRevision = snapshot.TerrainIndexRevision,
                PublishedUtcTicks = snapshot.PublishedUtcTicks,
                AirfieldCount = snapshot.Airfields.Count,
                RunwayCount = snapshot.RunwayCount,
                DirectionCount = snapshot.DirectionCount,
                TerrainTileCount = snapshot.TerrainTileCount,
                TerrainChunkCount = snapshot.TerrainChunkCount,
                TerrainGlobalCount = snapshot.TerrainLodCount(
                    AERISTerrainTileLod.Global),
                TerrainFarCount = snapshot.TerrainLodCount(
                    AERISTerrainTileLod.Far),
                TerrainRouteCount = snapshot.TerrainLodCount(
                    AERISTerrainTileLod.Route),
                TerrainLocalCount = snapshot.TerrainLodCount(
                    AERISTerrainTileLod.Local),
                TerrainLandCount = snapshot.TerrainLodCount(
                    AERISTerrainTileLod.Land),
                EstimatedBytes = snapshot.EstimatedBytes,
                LastAirfieldPublishMilliseconds =
                    snapshot.AirfieldPublishMilliseconds,
                LastTerrainPublishMilliseconds =
                    snapshot.TerrainPublishMilliseconds,
                AirfieldLookupHits = Interlocked.Read(ref airfieldLookupHits),
                AirfieldLookupMisses = Interlocked.Read(ref airfieldLookupMisses),
                TerrainLookupHits = Interlocked.Read(ref terrainLookupHits),
                TerrainLookupMisses = Interlocked.Read(ref terrainLookupMisses),
                GuardedSynchronousDiskOperations = Interlocked.Read(
                    ref guardedSynchronousDiskOperations),
                AllowedSynchronousDiskOperations = Interlocked.Read(
                    ref allowedSynchronousDiskOperations),
                SynchronousDiskLookups = Interlocked.Read(ref synchronousDiskLookups),
                LastSynchronousDiskLookupUtcTicks = Interlocked.Read(
                    ref lastSynchronousDiskLookupUtcTicks),
                LastSynchronousDiskLookupDomain =
                    lastSynchronousDiskLookupDomain ?? string.Empty,
                LastSynchronousDiskLookupOperation =
                    lastSynchronousDiskLookupOperation ?? string.Empty,
                Status = snapshot.Status
            };
        }
    }
}
