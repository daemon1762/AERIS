using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // Versioned binary index + spatially grouped external chunk blobs. Startup reads only
    // the manifest/index. Tile blobs are touched exclusively from shared runtime workers.
    internal sealed class AERISTerrainPreloadDatabase : IDisposable
    {
        sealed class IndexEntry
        {
            internal string StableId = string.Empty;
            internal AERISTerrainTileKey Key;
            internal string ChunkId = string.Empty;
            internal string RelativePath = string.Empty;
            internal long StoredBytes;
            internal long GenerationUtcTicks;
            internal long LastAccessUtcTicks;
            internal int Quality;
            internal AERISTerrainGenerationState State;
        }

        sealed class ParsedChunkCacheEntry
        {
            internal Dictionary<string, AERISTerrainPreloadEncodedTile> Values;
            internal long EstimatedBytes;
            internal long LastAccessSequence;
        }

        sealed class ChunkIndex
        {
            internal string ChunkId = string.Empty;
            internal string RelativePath = string.Empty;
            internal string BodyName = string.Empty;
            internal AERISTerrainTileLod Lod;
            internal int ChunkX;
            internal int ChunkY;
            internal long Bytes;
            internal long LastAccessUtcTicks;
            internal readonly HashSet<string> TileIds =
                new HashSet<string>(StringComparer.Ordinal);
        }

        readonly AERISMapDramCache mapDramCache;
        readonly object indexSync = new object();
        readonly object writerSync = new object();
        readonly object parsedChunkCacheSync = new object();
        readonly Dictionary<string, IndexEntry> tileIndex =
            new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, ChunkIndex> chunks =
            new Dictionary<string, ChunkIndex>(StringComparer.Ordinal);
        readonly Dictionary<string, AERISTerrainBodyPriority> retentionPriorities =
            new Dictionary<string, AERISTerrainBodyPriority>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, ParsedChunkCacheEntry> parsedChunkCache =
            new Dictionary<string, ParsedChunkCacheEntry>(StringComparer.Ordinal);
        // Immutable Map DRAM wrappers are reused across routine index commits. Candidate 7
        // rebuilt one wrapper object for every known tile on every publish; long preload runs
        // therefore created millions of identical short-lived objects.
        readonly Dictionary<string, AERISMapTerrainIndexEntry> mapIndexEntryCache =
            new Dictionary<string, AERISMapTerrainIndexEntry>(StringComparer.Ordinal);
        const long ParsedChunkCacheLimitBytes = 64L * 1024L * 1024L;
        long parsedChunkCacheBytes;
        long parsedChunkCacheSequence;
        long parsedChunkCacheHits;
        long parsedChunkCacheMisses;
        readonly string root;
        readonly string chunksRoot;
        readonly string manifestPath;
        readonly string journalRoot;
        long storageLimitBytes;
        long usedBytes;
        long databaseGeneration;
        // Changes that can invalidate an in-flight blob read use a separate epoch.
        // Normal append commits advance DatabaseGeneration but intentionally do not
        // cancel unrelated reads already decoding immutable chunk bytes.
        long requestGeneration = 1L;
        bool indexLoaded;
        bool indexDirty;
        bool indexRecoveryNeeded;
        bool journalRecoveryNeeded;
        bool disposed;
        long crcFailures;
        long repairedEntries;
        string activeProtectedBodyName = string.Empty;

        internal AERISTerrainPreloadDatabase(string root, long storageLimitBytes)
            : this(root, storageLimitBytes, null)
        {
        }

        internal AERISTerrainPreloadDatabase(string root, long storageLimitBytes,
            AERISMapDramCache mapDramCache)
        {
            this.mapDramCache = mapDramCache;
            this.root = root ?? string.Empty;
            chunksRoot = Path.Combine(this.root, "Chunks");
            manifestPath = Path.Combine(this.root, "manifest.atm");
            journalRoot = Path.Combine(this.root, "Journal");
            SetLimit(storageLimitBytes);
            RecoverJournal();
            LoadIndexOnly();
        }

        internal bool IndexLoaded { get { lock (indexSync) return indexLoaded; } }
        internal bool IndexRecoveryNeeded
        {
            get { lock (indexSync) return indexRecoveryNeeded; }
        }
        internal long UsedBytes { get { lock (indexSync) return usedBytes; } }
        internal long LimitBytes { get { lock (indexSync) return storageLimitBytes; } }
        internal int Count { get { lock (indexSync) return tileIndex.Count; } }
        internal long DatabaseGeneration { get { lock (indexSync) return databaseGeneration; } }
        internal long RequestGeneration { get { lock (indexSync) return requestGeneration; } }
        internal long CrcFailures { get { lock (indexSync) return crcFailures; } }
        internal long RepairedEntries { get { lock (indexSync) return repairedEntries; } }
        internal string RootPath { get { return root; } }

        void BeforeSynchronousDisk(string operation)
        {
            AERISMapDramDiskGuard.BeforeSynchronousDisk(mapDramCache, operation);
        }

        long GuardedFileLength(string path, string operation)
        {
            BeforeSynchronousDisk(operation);
            return new FileInfo(path).Length;
        }

        internal void SetLimit(long bytes)
        {
            lock (indexSync)
            {
                storageLimitBytes = bytes <= 0L ? long.MaxValue :
                    Math.Max(512L * 1024L * 1024L, bytes);
            }
        }

        internal void SetBodyRetentionPriority(string bodyName,
            AERISTerrainBodyPriority priority)
        {
            if (string.IsNullOrEmpty(bodyName)) return;
            lock (indexSync) retentionPriorities[bodyName] = priority;
        }

        internal void SetActiveBodyProtection(string bodyName)
        {
            lock (indexSync) activeProtectedBodyName = bodyName ?? string.Empty;
        }

        internal bool Contains(AERISTerrainTileKey key)
        {
            // Normal presence queries are served from the immutable Map DRAM snapshot.
            // No File.Exists, FileInfo or chunk read is permitted on this path.
            if (mapDramCache != null) return mapDramCache.ContainsTerrain(key);
            lock (indexSync)
            {
                IndexEntry entry;
                return tileIndex.TryGetValue(key.StableId, out entry) && entry != null &&
                    entry.State == AERISTerrainGenerationState.Complete &&
                    entry.Quality >= 100;
            }
        }

        internal bool TryGetChunkId(AERISTerrainTileKey key, out string chunkId)
        {
            // Tile payload reads are scheduled later on worker lanes. The lookup itself
            // is metadata-only and must remain DRAM-only during normal Flight queries.
            if (mapDramCache != null)
                return mapDramCache.TryGetTerrainChunkId(key, out chunkId);
            chunkId = string.Empty;
            lock (indexSync)
            {
                IndexEntry entry;
                if (!tileIndex.TryGetValue(key.StableId, out entry) || entry == null ||
                    entry.State != AERISTerrainGenerationState.Complete ||
                    entry.Quality < 100)
                    return false;
                chunkId = entry.ChunkId;
                return !string.IsNullOrEmpty(chunkId);
            }
        }

        internal string ChunkIdFor(AERISTerrainTileKey key)
        {
            int chunkX = AERISTerrainSpatialKey.ChunkCoordinate(key.LongitudeIndex);
            int chunkY = AERISTerrainSpatialKey.ChunkCoordinate(key.LatitudeIndex);
            string bodyHash = AERISTerrainHash.Fnv1A64Hex(key.BodyName + "|" +
                key.BodyRadiusMillimetres + "|" + key.EnvironmentHash);
            return bodyHash + "|" + (int)key.Lod + "|" + chunkX + "|" + chunkY;
        }

        // Metadata-only snapshot for CP3 current-body population planning. No chunk
        // payload, filesystem probe or decode is performed on this path.
        internal AERISTerrainTileKey[] SnapshotCompleteKeysForBody(string bodyName,
            string environmentHash)
        {
            string normalizedBody = bodyName ?? string.Empty;
            string normalizedEnvironment = environmentHash ?? string.Empty;
            var result = new List<AERISTerrainTileKey>();
            lock (indexSync)
            {
                foreach (IndexEntry entry in tileIndex.Values)
                {
                    if (entry == null ||
                        entry.State != AERISTerrainGenerationState.Complete ||
                        entry.Quality < 100 ||
                        !string.Equals(entry.Key.BodyName, normalizedBody,
                            StringComparison.Ordinal) ||
                        !string.Equals(entry.Key.EnvironmentHash,
                            normalizedEnvironment, StringComparison.Ordinal)) continue;
                    result.Add(entry.Key);
                }
            }
            result.Sort((a, b) =>
            {
                int lod = ((int)a.Lod).CompareTo((int)b.Lod);
                if (lod != 0) return lod;
                int chunk = string.CompareOrdinal(ChunkIdFor(a), ChunkIdFor(b));
                if (chunk != 0) return chunk;
                return string.CompareOrdinal(a.StableId, b.StableId);
            });
            return result.ToArray();
        }

        internal void LoadIndexOnly()
        {
            lock (indexSync)
            {
                requestGeneration++;
                ClearParsedChunkCache();
                tileIndex.Clear();
                chunks.Clear();
                usedBytes = 0L;
                indexLoaded = true;
                indexDirty = false;
                indexRecoveryNeeded = false;
                bool loaded = TryLoadManifestLocked(manifestPath);
                if (!loaded) loaded = TryLoadManifestLocked(manifestPath + ".bak");
                if (!loaded)
                {
                    tileIndex.Clear();
                    chunks.Clear();
                    usedBytes = 0L;
                    indexDirty = true;
                    // Do not synchronously inspect blob contents at startup. A bounded
                    // non-Flight maintenance job will rebuild the index from valid chunks.
                    BeforeSynchronousDisk("TERRAIN_STARTUP_CHUNKS_EXISTS");
                    indexRecoveryNeeded = Directory.Exists(chunksRoot);
                }
                indexRecoveryNeeded = indexRecoveryNeeded || journalRecoveryNeeded;
                PublishMapIndexLocked("STARTUP_INDEX_LOAD");
            }
        }

        bool TryLoadManifestLocked(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            BeforeSynchronousDisk("TERRAIN_MANIFEST_EXISTS");
            if (!File.Exists(path)) return false;
            var loadedTiles = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
            var loadedChunks = new Dictionary<string, ChunkIndex>(StringComparer.Ordinal);
            var unavailableChunks = new HashSet<string>(StringComparer.Ordinal);
            long loadedBytes = 0L;
            long loadedGeneration = 0L;
            try
            {
                BeforeSynchronousDisk("TERRAIN_MANIFEST_OPEN_READ");
                using (var stream = new FileStream(path, FileMode.Open,
                    FileAccess.Read, FileShare.Read))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    string magic = reader.ReadString();
                    if (!string.Equals(magic, AERISTerrainPreloadFormat.ManifestMagic,
                        StringComparison.Ordinal)) return false;
                    int version = reader.ReadInt32();
                    if (version != AERISTerrainPreloadFormat.DatabaseFormatVersion)
                        return false;
                    loadedGeneration = reader.ReadInt64();
                    int count = reader.ReadInt32();
                    if (count < 0 || count > 100000000)
                        throw new InvalidDataException("preload manifest count invalid");
                    for (int i = 0; i < count; i++)
                    {
                        IndexEntry entry = ReadIndexEntry(reader);
                        if (entry == null || string.IsNullOrEmpty(entry.ChunkId) ||
                            unavailableChunks.Contains(entry.ChunkId)) continue;
                        ChunkIndex chunk;
                        if (!loadedChunks.TryGetValue(entry.ChunkId, out chunk))
                        {
                            string blobPath = Path.Combine(root, entry.RelativePath);
                            BeforeSynchronousDisk("TERRAIN_MANIFEST_CHUNK_EXISTS");
                            if (!File.Exists(blobPath))
                            {
                                unavailableChunks.Add(entry.ChunkId);
                                continue;
                            }
                            chunk = new ChunkIndex
                            {
                                ChunkId = entry.ChunkId,
                                RelativePath = entry.RelativePath,
                                BodyName = entry.Key.BodyName,
                                Lod = entry.Key.Lod,
                                ChunkX = AERISTerrainSpatialKey.ChunkCoordinate(
                                    entry.Key.LongitudeIndex),
                                ChunkY = AERISTerrainSpatialKey.ChunkCoordinate(
                                    entry.Key.LatitudeIndex),
                                LastAccessUtcTicks = entry.LastAccessUtcTicks
                            };
                            try
                            {
                                BeforeSynchronousDisk("TERRAIN_MANIFEST_CHUNK_LENGTH");
                                chunk.Bytes = new FileInfo(blobPath).Length;
                            }
                            catch { chunk.Bytes = 0L; }
                            loadedChunks[entry.ChunkId] = chunk;
                            loadedBytes += Math.Max(0L, chunk.Bytes);
                        }
                        loadedTiles[entry.StableId] = entry;
                        chunk.TileIds.Add(entry.StableId);
                    }
                    if (stream.Position != stream.Length)
                        throw new InvalidDataException("preload manifest trailing data");
                }
            }
            catch { return false; }

            tileIndex.Clear();
            chunks.Clear();
            foreach (KeyValuePair<string, IndexEntry> pair in loadedTiles)
                tileIndex[pair.Key] = pair.Value;
            foreach (KeyValuePair<string, ChunkIndex> pair in loadedChunks)
                chunks[pair.Key] = pair.Value;
            usedBytes = loadedBytes;
            databaseGeneration = Math.Max(0L, loadedGeneration);
            indexLoaded = true;
            bool primary = string.Equals(path, manifestPath, StringComparison.Ordinal);
            indexDirty = !primary;
            // A backup manifest gives immediate availability, then a non-Flight scan
            // reincorporates any chunks committed after that backup was written.
            if (!primary) BeforeSynchronousDisk("TERRAIN_BACKUP_CHUNKS_EXISTS");
            indexRecoveryNeeded = !primary && Directory.Exists(chunksRoot);
            return true;
        }

        internal bool RecoverIndexFromChunks(out int validTiles, out int invalidChunks)
        {
            validTiles = 0;
            invalidChunks = 0;
            lock (writerSync)
            {
                ClearParsedChunkCache();
                var recoveredTiles = new Dictionary<string, IndexEntry>(
                    StringComparer.Ordinal);
                var recoveredChunks = new Dictionary<string, ChunkIndex>(
                    StringComparer.Ordinal);
                long recoveredBytes = 0L;
                string[] files;
                try
                {
                    BeforeSynchronousDisk("TERRAIN_RECOVERY_CHUNKS_EXISTS");
                    if (Directory.Exists(chunksRoot))
                    {
                        BeforeSynchronousDisk("TERRAIN_RECOVERY_ENUMERATE_CHUNKS");
                        files = Directory.GetFiles(chunksRoot, "*.atb",
                            SearchOption.AllDirectories);
                    }
                    else files = new string[0];
                }
                catch { return false; }
                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length; i++)
                {
                    string file = files[i];
                    try
                    {
                        BeforeSynchronousDisk("TERRAIN_RECOVERY_READ_CHUNK");
                        Dictionary<string, AERISTerrainPreloadEncodedTile> values =
                            ReadChunkEncoded(File.ReadAllBytes(file), null, null);
                        if (values.Count == 0)
                        {
                            invalidChunks++;
                            continue;
                        }
                        AERISTerrainPreloadEncodedTile first = null;
                        foreach (AERISTerrainPreloadEncodedTile candidate in values.Values)
                        {
                            first = candidate;
                            break;
                        }
                        if (first == null)
                        {
                            invalidChunks++;
                            continue;
                        }
                        string chunkId = ChunkIdFor(first.Key);
                        string relative = file.Substring(root.Length).TrimStart(
                            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var chunk = new ChunkIndex
                        {
                            ChunkId = chunkId,
                            RelativePath = relative,
                            BodyName = first.Key.BodyName,
                            Lod = first.Key.Lod,
                            ChunkX = AERISTerrainSpatialKey.ChunkCoordinate(
                                first.Key.LongitudeIndex),
                            ChunkY = AERISTerrainSpatialKey.ChunkCoordinate(
                                first.Key.LatitudeIndex),
                            LastAccessUtcTicks = DateTime.UtcNow.Ticks,
                            Bytes = GuardedFileLength(file,
                                "TERRAIN_RECOVERY_CHUNK_LENGTH")
                        };
                        foreach (AERISTerrainPreloadEncodedTile encoded in values.Values)
                        {
                            if (encoded == null || !string.Equals(ChunkIdFor(encoded.Key),
                                chunkId, StringComparison.Ordinal))
                            {
                                invalidChunks++;
                                continue;
                            }
                            IndexEntry entry = FromEncoded(encoded, chunkId, relative,
                                encoded.EstimatedBytes);
                            recoveredTiles[entry.StableId] = entry;
                            chunk.TileIds.Add(entry.StableId);
                            validTiles++;
                        }
                        if (chunk.TileIds.Count > 0)
                        {
                            recoveredChunks[chunkId] = chunk;
                            recoveredBytes += Math.Max(0L, chunk.Bytes);
                        }
                    }
                    catch
                    {
                        invalidChunks++;
                    }
                }
                lock (indexSync)
                {
                    tileIndex.Clear();
                    chunks.Clear();
                    foreach (KeyValuePair<string, IndexEntry> pair in recoveredTiles)
                        tileIndex[pair.Key] = pair.Value;
                    foreach (KeyValuePair<string, ChunkIndex> pair in recoveredChunks)
                        chunks[pair.Key] = pair.Value;
                    usedBytes = recoveredBytes;
                    databaseGeneration++;
                    requestGeneration++;
                    indexLoaded = true;
                    indexRecoveryNeeded = false;
                    indexDirty = true;
                    SaveManifestLocked();
                    journalRecoveryNeeded = false;
                }
                ClearRecoveryMarkers();
                return invalidChunks == 0 || validTiles > 0 || files.Length == 0;
            }
        }

        internal bool TryLoadBatch(IList<AERISTerrainTileKey> keys,
            AERISTerrainWarmTileCache warmCache,
            IDictionary<string, AERISTerrainHeightTile> output,
            AERISTerrainPreloadTelemetry telemetry, string currentGameDataHash)
        {
            if (keys == null || output == null || keys.Count == 0) return false;
            var byChunk = new Dictionary<string, List<AERISTerrainTileKey>>(
                StringComparer.Ordinal);
            int warmHits = 0;
            for (int i = 0; i < keys.Count; i++)
            {
                AERISTerrainTileKey key = keys[i];
                AERISTerrainPreloadEncodedTile warm;
                if (warmCache != null && warmCache.TryGet(key, out warm))
                {
                    try
                    {
                        if (!MetadataMatches(warm, key, currentGameDataHash, telemetry))
                            throw new InvalidDataException("preload warm metadata mismatch");
                        AERISTerrainHeightTile tile = AERISTerrainPreloadCodec.Decode(warm);
                        tile.Source = AERISTerrainTileSource.WarmRam;
                        output[key.StableId] = tile;
                        warmHits++;
                        continue;
                    }
                    catch
                    {
                        if (telemetry != null) telemetry.DecompressFailures++;
                        warmCache.Remove(key);
                    }
                }
                string chunkId;
                if (!TryGetChunkId(key, out chunkId)) continue;
                List<AERISTerrainTileKey> list;
                if (!byChunk.TryGetValue(chunkId, out list))
                {
                    list = new List<AERISTerrainTileKey>();
                    byChunk[chunkId] = list;
                }
                list.Add(key);
            }

            long requested = keys.Count;
            long diskRequests = 0L;
            long bytesRead = 0L;
            Stopwatch allWatch = Stopwatch.StartNew();
            foreach (KeyValuePair<string, List<AERISTerrainTileKey>> pair in byChunk)
            {
                ChunkIndex chunk;
                lock (indexSync)
                {
                    if (!chunks.TryGetValue(pair.Key, out chunk) || chunk == null) continue;
                }
                string path = Path.Combine(root, chunk.RelativePath);
                Dictionary<string, AERISTerrainPreloadEncodedTile> encoded;
                long chunkBytes;
                bool diskRead;
                if (!TryReadParsedChunk(pair.Key, path, telemetry, out encoded,
                    out chunkBytes, out diskRead))
                {
                    MarkChunkForRepair(pair.Key);
                    continue;
                }
                if (diskRead)
                {
                    diskRequests++;
                    bytesRead += chunkBytes;
                }
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    AERISTerrainTileKey key = pair.Value[i];
                    AERISTerrainPreloadEncodedTile value;
                    if (!encoded.TryGetValue(key.StableId, out value) || value == null)
                    {
                        // A structurally valid chunk may contain one CRC-damaged tile.
                        // Invalidate only that index entry so PQS can regenerate it; do not
                        // discard neighbouring valid tiles or the entire body database.
                        RemoveTileIndex(key.StableId, false);
                        continue;
                    }
                    try
                    {
                        if (!MetadataMatches(value, key, currentGameDataHash, telemetry))
                        {
                            RemoveTileIndex(key.StableId, false);
                            continue;
                        }
                        Stopwatch decodeWatch = Stopwatch.StartNew();
                        AERISTerrainHeightTile tile = AERISTerrainPreloadCodec.Decode(value);
                        decodeWatch.Stop();
                        tile.Source = AERISTerrainTileSource.PreloadDatabase;
                        output[key.StableId] = tile;
                        if (warmCache != null) warmCache.Put(value, 2);
                        if (telemetry != null)
                        {
                            telemetry.DecompressTimeMilliseconds = Ema(
                                telemetry.DecompressTimeMilliseconds,
                                decodeWatch.Elapsed.TotalMilliseconds, 0.15);
                            double seconds = Math.Max(0.000001,
                                decodeWatch.Elapsed.TotalSeconds);
                            telemetry.DecompressMbps = Ema(telemetry.DecompressMbps,
                                value.UncompressedSize / seconds / 1048576.0, 0.15);
                        }
                        Touch(key.StableId);
                    }
                    catch
                    {
                        if (telemetry != null) telemetry.DecompressFailures++;
                        RemoveTileIndex(key.StableId, false);
                    }
                }
            }
            allWatch.Stop();
            if (telemetry != null)
            {
                telemetry.DatabaseReadRequests += diskRequests;
                telemetry.DatabaseCoalescedReads += Math.Max(0L,
                    requested - warmHits - diskRequests);
                telemetry.DatabaseReadLatencyMilliseconds = Ema(
                    telemetry.DatabaseReadLatencyMilliseconds,
                    diskRequests <= 0L ? 0.0 : allWatch.Elapsed.TotalMilliseconds /
                    diskRequests, 0.15);
                double seconds = Math.Max(0.000001, allWatch.Elapsed.TotalSeconds);
                telemetry.DatabaseReadMbps = Ema(telemetry.DatabaseReadMbps,
                    bytesRead / seconds / 1048576.0, 0.15);
                double total = Math.Max(1.0, requested);
                telemetry.DatabaseCacheHitRatio = Ema(
                    telemetry.DatabaseCacheHitRatio,
                    output.Count / total, 0.10);
                telemetry.DatabaseCrcFailures = CrcFailures;
                telemetry.DatabaseParsedChunkCacheHits = ParsedChunkCacheHits;
                telemetry.DatabaseParsedChunkCacheMisses = ParsedChunkCacheMisses;
                long parsedTotal = ParsedChunkCacheHits + ParsedChunkCacheMisses;
                telemetry.DatabaseParsedChunkCacheHitRatio = parsedTotal <= 0L ? 0.0 :
                    ParsedChunkCacheHits / (double)parsedTotal;
            }
            return output.Count > 0;
        }

        static bool MetadataMatches(AERISTerrainPreloadEncodedTile encoded,
            AERISTerrainTileKey requestedKey, string currentGameDataHash,
            AERISTerrainPreloadTelemetry telemetry)
        {
            if (encoded == null || !encoded.Key.Equals(requestedKey) ||
                !string.Equals(encoded.PqsConfigurationHash,
                    requestedKey.EnvironmentHash, StringComparison.Ordinal)) return false;
            if (!string.IsNullOrEmpty(currentGameDataHash) &&
                !string.IsNullOrEmpty(encoded.GameDataHash) &&
                !string.Equals(encoded.GameDataHash, currentGameDataHash,
                    StringComparison.Ordinal) && telemetry != null)
                telemetry.DatabaseHashMismatches++;
            // GameData is validated and reported, but the body-specific live PQS hash is
            // authoritative. An unrelated GameData edit must not invalidate every body.
            return true;
        }

        internal bool Save(AERISTerrainHeightTile tile, string pqsHash,
            string gameDataHash, long terrainGenerationId,
            AERISTerrainCodecId preferredCodec,
            out long storedBytes, out double compressionRatio)
        {
            int chunksCommitted;
            return SaveBatch(tile == null ? null :
                new AERISTerrainHeightTile[] { tile }, pqsHash, gameDataHash,
                terrainGenerationId, preferredCodec, out storedBytes,
                out compressionRatio, out chunksCommitted);
        }

        internal bool SaveBatch(IList<AERISTerrainHeightTile> tiles, string pqsHash,
            string gameDataHash, long terrainGenerationId,
            AERISTerrainCodecId preferredCodec, out long storedBytes,
            out double compressionRatio, out int chunksCommitted)
        {
            storedBytes = 0L;
            compressionRatio = 1.0;
            chunksCommitted = 0;
            if (tiles == null || tiles.Count == 0) return false;
            var encodedTiles = new List<AERISTerrainPreloadEncodedTile>(tiles.Count);
            for (int i = 0; i < tiles.Count; i++)
            {
                AERISTerrainHeightTile tile = tiles[i];
                if (tile == null || tile.IsPreview || !tile.SamplingComplete ||
                    tile.Quality < 100 || tile.Elevation == null ||
                    tile.Flags == null) continue;
                try
                {
                    encodedTiles.Add(AERISTerrainPreloadCodec.Encode(tile, pqsHash,
                        gameDataHash, terrainGenerationId, preferredCodec));
                }
                catch { }
            }
            return SaveEncodedBatch(encodedTiles, out storedBytes,
                out compressionRatio, out chunksCommitted);
        }

        // Candidate 4 preload throughput path: CPU workers encode tiles in parallel, then
        // one SSD super-batch commits many already-compressed chunks under one manifest
        // transaction. Flight reads keep their existing independent priority lanes.
        internal bool SaveEncodedBatch(IList<AERISTerrainPreloadEncodedTile> encodedTiles,
            out long storedBytes, out double compressionRatio,
            out int chunksCommitted)
        {
            storedBytes = 0L;
            compressionRatio = 1.0;
            chunksCommitted = 0;
            if (encodedTiles == null || encodedTiles.Count == 0) return false;
            var byChunk = new Dictionary<string,
                List<AERISTerrainPreloadEncodedTile>>(StringComparer.Ordinal);
            long uncompressedBytes = 0L;
            long compressedBytes = 0L;
            for (int i = 0; i < encodedTiles.Count; i++)
            {
                AERISTerrainPreloadEncodedTile encoded = encodedTiles[i];
                if (encoded == null || encoded.CompressedPayload == null ||
                    encoded.GenerationState != AERISTerrainGenerationState.Complete)
                    continue;
                string chunkId = ChunkIdFor(encoded.Key);
                List<AERISTerrainPreloadEncodedTile> list;
                if (!byChunk.TryGetValue(chunkId, out list))
                {
                    list = new List<AERISTerrainPreloadEncodedTile>();
                    byChunk[chunkId] = list;
                }
                list.Add(encoded);
                uncompressedBytes += Math.Max(0, encoded.UncompressedSize);
                compressedBytes += encoded.CompressedPayload.LongLength;
            }
            if (byChunk.Count == 0) return false;
            compressionRatio = compressedBytes <= 0L ? 1.0 :
                Math.Max(1.0, uncompressedBytes / (double)compressedBytes);

            bool allCommitted = true;
            lock (writerSync)
            {
                foreach (KeyValuePair<string, List<AERISTerrainPreloadEncodedTile>>
                    pair in byChunk)
                {
                    long bytes;
                    if (CommitEncodedChunkLocked(pair.Key, pair.Value, out bytes))
                    {
                        storedBytes += Math.Max(0L, bytes);
                        chunksCommitted++;
                    }
                    else allCommitted = false;
                }
                if (chunksCommitted > 0)
                {
                    lock (indexSync)
                    {
                        databaseGeneration++;
                        indexDirty = true;
                        PruneLocked();
                        // One manifest commit covers the entire SSD super-batch.
                        SaveManifestLocked();
                    }
                }
            }
            return allCommitted && chunksCommitted == byChunk.Count;
        }

        bool CommitEncodedChunkLocked(string chunkId,
            IList<AERISTerrainPreloadEncodedTile> additions, out long storedBytes)
        {
            storedBytes = 0L;
            if (string.IsNullOrEmpty(chunkId) || additions == null ||
                additions.Count == 0) return false;
            AERISTerrainTileKey representative = additions[0].Key;
            string relative = RelativeChunkPath(representative);
            string finalPath = Path.Combine(root, relative);
            string journalStem = AERISTerrainHash.Fnv1A64Hex(chunkId);
            string temporary = Path.Combine(journalRoot, journalStem + ".tmp");
            string recoveryMarker = Path.Combine(journalRoot,
                journalStem + ".pending");
            try
            {
                BeforeSynchronousDisk("TERRAIN_COMMIT_CREATE_CHUNK_DIRECTORY");
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath));
                BeforeSynchronousDisk("TERRAIN_COMMIT_CREATE_JOURNAL_DIRECTORY");
                Directory.CreateDirectory(journalRoot);
                BeforeSynchronousDisk("TERRAIN_COMMIT_WRITE_RECOVERY_MARKER");
                File.WriteAllText(recoveryMarker, relative ?? string.Empty, Encoding.UTF8);
                Dictionary<string, AERISTerrainPreloadEncodedTile> values;
                long ignoredBytes;
                bool ignoredDiskRead;
                BeforeSynchronousDisk("TERRAIN_COMMIT_EXISTING_CHUNK_EXISTS");
                if (File.Exists(finalPath))
                {
                    if (!TryReadParsedChunk(chunkId, finalPath, null, out values,
                        out ignoredBytes, out ignoredDiskRead))
                        values = new Dictionary<string,
                            AERISTerrainPreloadEncodedTile>(StringComparer.Ordinal);
                    else values = CloneEncodedDictionary(values);
                }
                else values = new Dictionary<string,
                    AERISTerrainPreloadEncodedTile>(StringComparer.Ordinal);
                for (int i = 0; i < additions.Count; i++)
                    values[additions[i].Key.StableId] = additions[i];
                WriteChunk(temporary, chunkId, values);
                BeforeSynchronousDisk("TERRAIN_COMMIT_VERIFY_READ");
                byte[] verifyBytes = File.ReadAllBytes(temporary);
                Dictionary<string, AERISTerrainPreloadEncodedTile> verify =
                    ReadChunkEncoded(verifyBytes, chunkId, null);
                for (int i = 0; i < additions.Count; i++)
                    if (!verify.ContainsKey(additions[i].Key.StableId))
                        throw new InvalidDataException(
                            "preload chunk batch round-trip mismatch");
                AtomicReplace(temporary, finalPath);
                long newBytes = GuardedFileLength(finalPath,
                    "TERRAIN_COMMIT_CHUNK_LENGTH");
                storedBytes = newBytes;
                lock (indexSync)
                {
                    ChunkIndex previousChunk;
                    long previousBytes = chunks.TryGetValue(chunkId,
                        out previousChunk) && previousChunk != null ?
                        previousChunk.Bytes : 0L;
                    usedBytes = Math.Max(0L, usedBytes - previousBytes + newBytes);
                    var chunk = previousChunk ?? new ChunkIndex();
                    chunk.ChunkId = chunkId;
                    chunk.RelativePath = relative;
                    chunk.BodyName = representative.BodyName;
                    chunk.Lod = representative.Lod;
                    chunk.ChunkX = AERISTerrainSpatialKey.ChunkCoordinate(
                        representative.LongitudeIndex);
                    chunk.ChunkY = AERISTerrainSpatialKey.ChunkCoordinate(
                        representative.LatitudeIndex);
                    chunk.Bytes = newBytes;
                    chunk.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
                    chunk.TileIds.Clear();
                    foreach (AERISTerrainPreloadEncodedTile item in values.Values)
                    {
                        IndexEntry entry = FromEncoded(item, chunkId, relative,
                            item.CompressedPayload == null ? 0L :
                            item.CompressedPayload.LongLength);
                        tileIndex[entry.StableId] = entry;
                        chunk.TileIds.Add(entry.StableId);
                    }
                    chunks[chunkId] = chunk;
                }
                UpdateParsedChunkCache(chunkId, values);
                TryDelete(recoveryMarker);
                return true;
            }
            catch
            {
                TryDelete(temporary);
                InvalidateParsedChunkCache(chunkId);
                return false;
            }
        }

        internal bool VerifyAndRepair(string bodyName, out int valid,
            out int invalid)
        {
            int recoveredValid;
            int recoveredInvalid;
            RecoverIndexFromChunks(out recoveredValid, out recoveredInvalid);
            valid = 0;
            invalid = recoveredInvalid;
            List<ChunkIndex> candidates = new List<ChunkIndex>();
            lock (indexSync)
            {
                foreach (ChunkIndex chunk in chunks.Values)
                    if (chunk != null && (string.IsNullOrEmpty(bodyName) ||
                        string.Equals(chunk.BodyName, bodyName,
                        StringComparison.OrdinalIgnoreCase))) candidates.Add(chunk);
            }
            for (int i = 0; i < candidates.Count; i++)
            {
                ChunkIndex chunk = candidates[i];
                try
                {
                    BeforeSynchronousDisk("TERRAIN_VERIFY_READ_CHUNK");
                    Dictionary<string, AERISTerrainPreloadEncodedTile> values =
                        ReadChunkEncoded(File.ReadAllBytes(Path.Combine(root,
                        chunk.RelativePath)), chunk.ChunkId, null);
                    valid += values.Count;
                    int expected = chunk.TileIds.Count;
                    if (values.Count != expected)
                    {
                        invalid += Math.Max(0, expected - values.Count);
                        ReindexChunk(chunk, values);
                    }
                }
                catch
                {
                    invalid += chunk.TileIds.Count;
                    RemoveChunkIndex(chunk.ChunkId, true);
                }
            }
            FlushIndex();
            return invalid == 0;
        }

        internal int InvalidateBodyEnvironment(string bodyName,
            string currentEnvironmentHash)
        {
            if (string.IsNullOrEmpty(bodyName)) return 0;
            var chunkIds = new HashSet<string>(StringComparer.Ordinal);
            lock (indexSync)
            {
                foreach (IndexEntry entry in tileIndex.Values)
                {
                    if (entry == null || !string.Equals(entry.Key.BodyName, bodyName,
                        StringComparison.OrdinalIgnoreCase)) continue;
                    if (entry.Key.FormatVersion != AERISTerrainTileFormat.Version ||
                        !string.Equals(entry.Key.EnvironmentHash,
                            currentEnvironmentHash ?? string.Empty,
                            StringComparison.Ordinal)) chunkIds.Add(entry.ChunkId);
                }
            }
            foreach (string chunkId in chunkIds) RemoveChunkIndex(chunkId, true);
            if (chunkIds.Count > 0) FlushIndex();
            return chunkIds.Count;
        }

        internal long BodyStorageBytes(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return 0L;
            long total = 0L;
            lock (indexSync)
            {
                foreach (ChunkIndex chunk in chunks.Values)
                    if (chunk != null && string.Equals(chunk.BodyName, bodyName,
                        StringComparison.OrdinalIgnoreCase)) total += Math.Max(0L, chunk.Bytes);
            }
            return total;
        }

        internal bool DeleteBody(string bodyName)
        {
            if (string.IsNullOrEmpty(bodyName)) return false;
            List<string> chunkIds = new List<string>();
            lock (indexSync)
                foreach (ChunkIndex chunk in chunks.Values)
                    if (chunk != null && string.Equals(chunk.BodyName, bodyName,
                        StringComparison.OrdinalIgnoreCase)) chunkIds.Add(chunk.ChunkId);
            for (int i = 0; i < chunkIds.Count; i++) RemoveChunkIndex(chunkIds[i], true);
            FlushIndex();
            return chunkIds.Count > 0;
        }

        internal AERISTerrainPreloadBodyStatus[] SnapshotBodies(
            IDictionary<string, AERISTerrainBodyPriority> priorities,
            IDictionary<string, AERISTerrainTileLod> qualityLimits)
        {
            var grouped = new Dictionary<string, AERISTerrainPreloadBodyStatus>(
                StringComparer.OrdinalIgnoreCase);
            lock (indexSync)
            {
                foreach (IndexEntry entry in tileIndex.Values)
                {
                    if (entry == null) continue;
                    AERISTerrainPreloadBodyStatus status;
                    if (!grouped.TryGetValue(entry.Key.BodyName, out status))
                    {
                        AERISTerrainBodyPriority priority =
                            AERISTerrainBodyPriority.Normal;
                        if (priorities != null) priorities.TryGetValue(entry.Key.BodyName,
                            out priority);
                        AERISTerrainTileLod quality = AERISTerrainTileLod.Route;
                        if (qualityLimits != null) qualityLimits.TryGetValue(
                            entry.Key.BodyName, out quality);
                        status = new AERISTerrainPreloadBodyStatus
                        {
                            BodyName = entry.Key.BodyName,
                            Priority = priority,
                            QualityLimit = quality,
                            Pinned = priority == AERISTerrainBodyPriority.Pinned,
                            Supported = true,
                            Status = "READY"
                        };
                        grouped[entry.Key.BodyName] = status;
                    }
                    status.StorageBytes += Math.Max(0L, entry.StoredBytes);
                    if (entry.State == AERISTerrainGenerationState.Complete)
                        status.CompleteTiles++;
                    else if (entry.State == AERISTerrainGenerationState.Invalid)
                        status.InvalidTiles++;
                    else status.PendingTiles++;
                }
            }
            var result = new List<AERISTerrainPreloadBodyStatus>(grouped.Values);
            result.Sort((a, b) => string.Compare(a.BodyName, b.BodyName,
                StringComparison.OrdinalIgnoreCase));
            return result.ToArray();
        }

        internal void FlushIndex()
        {
            lock (indexSync) SaveManifestLocked();
        }

        void Touch(string stableId)
        {
            lock (indexSync)
            {
                IndexEntry entry;
                if (!tileIndex.TryGetValue(stableId, out entry) || entry == null) return;
                entry.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
                ChunkIndex chunk;
                if (chunks.TryGetValue(entry.ChunkId, out chunk) && chunk != null)
                    chunk.LastAccessUtcTicks = entry.LastAccessUtcTicks;
                indexDirty = true;
            }
        }

        void MarkChunkForRepair(string chunkId)
        {
            InvalidateParsedChunkCache(chunkId);
            lock (indexSync)
            {
                ChunkIndex chunk;
                if (!chunks.TryGetValue(chunkId, out chunk) || chunk == null) return;
                foreach (string id in chunk.TileIds)
                {
                    IndexEntry entry;
                    if (tileIndex.TryGetValue(id, out entry) && entry != null)
                        entry.State = AERISTerrainGenerationState.Invalid;
                }
                indexDirty = true;
                PublishMapIndexLocked("RUNTIME_CHUNK_INVALIDATION");
            }
        }

        void ReindexChunk(ChunkIndex chunk,
            Dictionary<string, AERISTerrainPreloadEncodedTile> values)
        {
            if (chunk == null || values == null) return;
            lock (indexSync)
            {
                var old = new List<string>(chunk.TileIds);
                for (int i = 0; i < old.Count; i++) tileIndex.Remove(old[i]);
                chunk.TileIds.Clear();
                foreach (AERISTerrainPreloadEncodedTile encoded in values.Values)
                {
                    IndexEntry entry = FromEncoded(encoded, chunk.ChunkId,
                        chunk.RelativePath, encoded.EstimatedBytes);
                    tileIndex[entry.StableId] = entry;
                    chunk.TileIds.Add(entry.StableId);
                    repairedEntries++;
                }
                databaseGeneration++;
                requestGeneration++;
                indexDirty = true;
                PublishMapIndexLocked("CHUNK_REINDEX");
            }
            UpdateParsedChunkCache(chunk.ChunkId, values);
        }

        void RemoveTileIndex(string stableId, bool deleteEmptyChunk)
        {
            string chunkId = string.Empty;
            lock (indexSync)
            {
                IndexEntry entry;
                if (!tileIndex.TryGetValue(stableId, out entry) || entry == null) return;
                chunkId = entry.ChunkId;
                tileIndex.Remove(stableId);
                ChunkIndex chunk;
                if (chunks.TryGetValue(chunkId, out chunk) && chunk != null)
                    chunk.TileIds.Remove(stableId);
                databaseGeneration++;
                requestGeneration++;
                indexDirty = true;
                if (deleteEmptyChunk && chunks.TryGetValue(chunkId, out chunk) &&
                    chunk != null && chunk.TileIds.Count == 0)
                    RemoveChunkIndexLocked(chunkId, true);
                PublishMapIndexLocked("RUNTIME_TILE_INVALIDATION");
            }
        }

        void RemoveChunkIndex(string chunkId, bool deleteFile)
        {
            lock (indexSync) RemoveChunkIndexLocked(chunkId, deleteFile);
        }

        void RemoveChunkIndexLocked(string chunkId, bool deleteFile)
        {
            InvalidateParsedChunkCache(chunkId);
            ChunkIndex chunk;
            if (!chunks.TryGetValue(chunkId, out chunk) || chunk == null) return;
            chunks.Remove(chunkId);
            foreach (string id in chunk.TileIds) tileIndex.Remove(id);
            usedBytes = Math.Max(0L, usedBytes - Math.Max(0L, chunk.Bytes));
            databaseGeneration++;
            requestGeneration++;
            indexDirty = true;
            if (deleteFile) TryDelete(Path.Combine(root, chunk.RelativePath));
        }

        void PruneLocked()
        {
            if (usedBytes <= storageLimitBytes) return;
            var candidates = new List<ChunkIndex>(chunks.Values);
            candidates.Sort((a, b) =>
            {
                int aRetention = RetentionScore(a);
                int bRetention = RetentionScore(b);
                int retain = aRetention.CompareTo(bRetention);
                if (retain != 0) return retain;
                int lod = ((int)b.Lod).CompareTo((int)a.Lod);
                if (lod != 0) return lod;
                return a.LastAccessUtcTicks.CompareTo(b.LastAccessUtcTicks);
            });
            for (int i = 0; i < candidates.Count && usedBytes > storageLimitBytes; i++)
            {
                ChunkIndex chunk = candidates[i];
                if (RetentionScore(chunk) >= 1000) continue;
                RemoveChunkIndexLocked(chunk.ChunkId, true);
            }
        }

        int RetentionScore(ChunkIndex chunk)
        {
            if (chunk == null) return 0;
            AERISTerrainBodyPriority priority;
            if (!retentionPriorities.TryGetValue(chunk.BodyName, out priority))
                priority = AERISTerrainBodyPriority.Normal;
            int score = (int)priority * 100;
            bool protectedBody = priority == AERISTerrainBodyPriority.Pinned ||
                string.Equals(chunk.BodyName, "Kerbin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(chunk.BodyName, activeProtectedBodyName,
                    StringComparison.OrdinalIgnoreCase);
            if (protectedBody) score += 2000;
            if (chunk.Lod == AERISTerrainTileLod.Global) score += 1500;
            else if (chunk.Lod == AERISTerrainTileLod.Land) score += 1200;
            else if (chunk.Lod == AERISTerrainTileLod.Far) score += 300;
            else if (chunk.Lod == AERISTerrainTileLod.Route) score += 100;
            return score;
        }

        void PublishMapIndexLocked(string cause)
        {
            if (mapDramCache == null) return;
            var entries = new List<AERISMapTerrainIndexEntry>(tileIndex.Count);
            foreach (IndexEntry entry in tileIndex.Values)
            {
                if (entry == null || string.IsNullOrEmpty(entry.StableId) ||
                    entry.State != AERISTerrainGenerationState.Complete ||
                    entry.Quality < 100) continue;
                AERISMapTerrainIndexEntry published;
                if (!mapIndexEntryCache.TryGetValue(entry.StableId, out published) ||
                    published == null || !published.Key.Equals(entry.Key) ||
                    !string.Equals(published.ChunkId, entry.ChunkId,
                        StringComparison.Ordinal) ||
                    !string.Equals(published.RelativePath, entry.RelativePath,
                        StringComparison.Ordinal) ||
                    published.StoredBytes != entry.StoredBytes ||
                    published.GenerationUtcTicks != entry.GenerationUtcTicks ||
                    published.Quality != entry.Quality ||
                    published.State != entry.State)
                {
                    published = new AERISMapTerrainIndexEntry(entry.StableId, entry.Key,
                        entry.ChunkId, entry.RelativePath, entry.StoredBytes,
                        entry.GenerationUtcTicks, entry.Quality, entry.State);
                    mapIndexEntryCache[entry.StableId] = published;
                }
                entries.Add(published);
            }
            if (mapIndexEntryCache.Count != entries.Count)
            {
                var stale = new List<string>();
                foreach (string stableId in mapIndexEntryCache.Keys)
                    if (!tileIndex.ContainsKey(stableId)) stale.Add(stableId);
                for (int i = 0; i < stale.Count; i++)
                    mapIndexEntryCache.Remove(stale[i]);
            }
            // Map DRAM stores a keyed immutable snapshot; list ordering has no lookup or
            // presentation semantics. The persisted manifest remains deterministically sorted.
            mapDramCache.PublishTerrainIndex(entries, databaseGeneration, cause);
        }

        void SaveManifestLocked()
        {
            if (!indexDirty || disposed) return;
            BeforeSynchronousDisk("TERRAIN_MANIFEST_CREATE_DIRECTORY");
            Directory.CreateDirectory(root);
            string temporary = manifestPath + ".tmp";
            BeforeSynchronousDisk("TERRAIN_MANIFEST_OPEN_WRITE");
            using (var stream = new FileStream(temporary, FileMode.Create,
                FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(AERISTerrainPreloadFormat.ManifestMagic);
                writer.Write(AERISTerrainPreloadFormat.DatabaseFormatVersion);
                writer.Write(databaseGeneration);
                var entries = new List<IndexEntry>(tileIndex.Values);
                entries.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
                writer.Write(entries.Count);
                for (int i = 0; i < entries.Count; i++) WriteIndexEntry(writer, entries[i]);
                writer.Flush();
                stream.Flush();
            }
            AtomicReplace(temporary, manifestPath, true);
            indexDirty = false;
            PublishMapIndexLocked("INDEX_COMMIT");
        }

        static IndexEntry FromEncoded(AERISTerrainPreloadEncodedTile encoded,
            string chunkId, string relativePath, long bytes)
        {
            return new IndexEntry
            {
                StableId = encoded.Key.StableId,
                Key = encoded.Key,
                ChunkId = chunkId,
                RelativePath = relativePath,
                StoredBytes = Math.Max(0L, bytes),
                GenerationUtcTicks = encoded.GenerationUtcTicks,
                LastAccessUtcTicks = encoded.LastAccessUtcTicks,
                Quality = encoded.Quality,
                State = encoded.GenerationState
            };
        }

        static void WriteIndexEntry(BinaryWriter writer, IndexEntry entry)
        {
            writer.Write(entry.StableId ?? string.Empty);
            WriteKey(writer, entry.Key);
            writer.Write(entry.ChunkId ?? string.Empty);
            writer.Write(entry.RelativePath ?? string.Empty);
            writer.Write(entry.StoredBytes);
            writer.Write(entry.GenerationUtcTicks);
            writer.Write(entry.LastAccessUtcTicks);
            writer.Write(entry.Quality);
            writer.Write((int)entry.State);
        }

        static IndexEntry ReadIndexEntry(BinaryReader reader)
        {
            return new IndexEntry
            {
                StableId = reader.ReadString(),
                Key = ReadKey(reader),
                ChunkId = reader.ReadString(),
                RelativePath = reader.ReadString(),
                StoredBytes = reader.ReadInt64(),
                GenerationUtcTicks = reader.ReadInt64(),
                LastAccessUtcTicks = reader.ReadInt64(),
                Quality = reader.ReadInt32(),
                State = (AERISTerrainGenerationState)reader.ReadInt32()
            };
        }

        long ParsedChunkCacheHits
        {
            get { lock (parsedChunkCacheSync) return parsedChunkCacheHits; }
        }

        long ParsedChunkCacheMisses
        {
            get { lock (parsedChunkCacheSync) return parsedChunkCacheMisses; }
        }

        bool TryReadParsedChunk(string chunkId, string path,
            AERISTerrainPreloadTelemetry telemetry,
            out Dictionary<string, AERISTerrainPreloadEncodedTile> values,
            out long bytesRead, out bool diskRead)
        {
            values = null;
            bytesRead = 0L;
            diskRead = false;
            lock (parsedChunkCacheSync)
            {
                ParsedChunkCacheEntry cached;
                if (parsedChunkCache.TryGetValue(chunkId, out cached) &&
                    cached != null && cached.Values != null)
                {
                    cached.LastAccessSequence = ++parsedChunkCacheSequence;
                    parsedChunkCacheHits++;
                    values = cached.Values;
                    return true;
                }
                parsedChunkCacheMisses++;
            }
            byte[] bytes;
            try
            {
                BeforeSynchronousDisk("TERRAIN_PAYLOAD_READ_CHUNK");
                bytes = File.ReadAllBytes(path);
            }
            catch { return false; }
            diskRead = true;
            bytesRead = bytes.LongLength;
            try { values = ReadChunkEncoded(bytes, chunkId, telemetry); }
            catch { return false; }
            UpdateParsedChunkCache(chunkId, values);
            return true;
        }

        void UpdateParsedChunkCache(string chunkId,
            Dictionary<string, AERISTerrainPreloadEncodedTile> values)
        {
            if (string.IsNullOrEmpty(chunkId) || values == null) return;
            Dictionary<string, AERISTerrainPreloadEncodedTile> immutable =
                CloneEncodedDictionary(values);
            long bytes = EstimateEncodedDictionaryBytes(immutable);
            lock (parsedChunkCacheSync)
            {
                ParsedChunkCacheEntry previous;
                if (parsedChunkCache.TryGetValue(chunkId, out previous) &&
                    previous != null)
                    parsedChunkCacheBytes = Math.Max(0L, parsedChunkCacheBytes -
                        previous.EstimatedBytes);
                parsedChunkCache[chunkId] = new ParsedChunkCacheEntry
                {
                    Values = immutable,
                    EstimatedBytes = bytes,
                    LastAccessSequence = ++parsedChunkCacheSequence
                };
                parsedChunkCacheBytes += bytes;
                while (parsedChunkCacheBytes > ParsedChunkCacheLimitBytes &&
                    parsedChunkCache.Count > 1)
                {
                    string oldestId = null;
                    ParsedChunkCacheEntry oldest = null;
                    foreach (KeyValuePair<string, ParsedChunkCacheEntry> pair in
                        parsedChunkCache)
                        if (oldest == null || pair.Value.LastAccessSequence <
                            oldest.LastAccessSequence)
                        {
                            oldestId = pair.Key;
                            oldest = pair.Value;
                        }
                    if (oldestId == null || oldest == null) break;
                    parsedChunkCache.Remove(oldestId);
                    parsedChunkCacheBytes = Math.Max(0L, parsedChunkCacheBytes -
                        oldest.EstimatedBytes);
                }
            }
        }

        void InvalidateParsedChunkCache(string chunkId)
        {
            if (string.IsNullOrEmpty(chunkId)) return;
            lock (parsedChunkCacheSync)
            {
                ParsedChunkCacheEntry value;
                if (!parsedChunkCache.TryGetValue(chunkId, out value)) return;
                parsedChunkCache.Remove(chunkId);
                if (value != null) parsedChunkCacheBytes = Math.Max(0L,
                    parsedChunkCacheBytes - value.EstimatedBytes);
            }
        }

        void ClearParsedChunkCache()
        {
            lock (parsedChunkCacheSync)
            {
                parsedChunkCache.Clear();
                parsedChunkCacheBytes = 0L;
            }
        }

        static Dictionary<string, AERISTerrainPreloadEncodedTile>
            CloneEncodedDictionary(
                Dictionary<string, AERISTerrainPreloadEncodedTile> source)
        {
            var result = new Dictionary<string, AERISTerrainPreloadEncodedTile>(
                StringComparer.Ordinal);
            if (source == null) return result;
            foreach (KeyValuePair<string, AERISTerrainPreloadEncodedTile> pair in source)
                if (pair.Value != null) result[pair.Key] = pair.Value.CloneImmutable();
            return result;
        }

        static long EstimateEncodedDictionaryBytes(
            Dictionary<string, AERISTerrainPreloadEncodedTile> values)
        {
            long result = 256L;
            if (values == null) return result;
            foreach (AERISTerrainPreloadEncodedTile value in values.Values)
                if (value != null) result += Math.Max(0L, value.EstimatedBytes);
            return result;
        }

        void WriteChunk(string path, string chunkId,
            Dictionary<string, AERISTerrainPreloadEncodedTile> values)
        {
            BeforeSynchronousDisk("TERRAIN_CHUNK_OPEN_WRITE");
            using (var stream = new FileStream(path, FileMode.Create,
                FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(AERISTerrainPreloadFormat.ChunkMagic);
                writer.Write(AERISTerrainPreloadFormat.DatabaseFormatVersion);
                writer.Write(chunkId ?? string.Empty);
                var list = new List<AERISTerrainPreloadEncodedTile>(values.Values);
                list.Sort((a, b) =>
                {
                    ulong am = AERISTerrainSpatialKey.Morton(a.Key.LongitudeIndex,
                        a.Key.LatitudeIndex);
                    ulong bm = AERISTerrainSpatialKey.Morton(b.Key.LongitudeIndex,
                        b.Key.LatitudeIndex);
                    int result = am.CompareTo(bm);
                    return result != 0 ? result : string.CompareOrdinal(
                        a.Key.StableId, b.Key.StableId);
                });
                writer.Write(list.Count);
                for (int i = 0; i < list.Count; i++)
                {
                    byte[] record;
                    using (var recordStream = new MemoryStream())
                    using (var recordWriter = new BinaryWriter(recordStream, Encoding.UTF8))
                    {
                        WriteEncoded(recordWriter, list[i]);
                        recordWriter.Flush();
                        record = recordStream.ToArray();
                    }
                    writer.Write(record.Length);
                    writer.Write(AERISTerrainCrc32.Compute(record));
                    writer.Write(record);
                }
                writer.Flush();
                stream.Flush();
            }
        }

        Dictionary<string, AERISTerrainPreloadEncodedTile> ReadChunkEncoded(
            byte[] bytes, string expectedChunkId, AERISTerrainPreloadTelemetry telemetry)
        {
            var result = new Dictionary<string, AERISTerrainPreloadEncodedTile>(
                StringComparer.Ordinal);
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                if (!string.Equals(reader.ReadString(),
                    AERISTerrainPreloadFormat.ChunkMagic, StringComparison.Ordinal))
                    throw new InvalidDataException("preload chunk magic mismatch");
                if (reader.ReadInt32() != AERISTerrainPreloadFormat.DatabaseFormatVersion)
                    throw new InvalidDataException("preload chunk version mismatch");
                string chunkId = reader.ReadString();
                if (!string.IsNullOrEmpty(expectedChunkId) &&
                    !string.Equals(chunkId, expectedChunkId, StringComparison.Ordinal))
                    throw new InvalidDataException("preload chunk identity mismatch");
                int count = reader.ReadInt32();
                if (count < 0 || count > 4096)
                    throw new InvalidDataException("preload chunk tile count invalid");
                for (int i = 0; i < count; i++)
                {
                    if (stream.Length - stream.Position < 8L)
                    {
                        RecordChunkFailure(telemetry);
                        break;
                    }
                    int recordLength = reader.ReadInt32();
                    uint recordCrc = reader.ReadUInt32();
                    if (recordLength <= 0 || recordLength > 256 * 1024 * 1024 ||
                        recordLength > stream.Length - stream.Position)
                    {
                        RecordChunkFailure(telemetry);
                        break;
                    }
                    byte[] record = reader.ReadBytes(recordLength);
                    if (record.Length != recordLength ||
                        AERISTerrainCrc32.Compute(record) != recordCrc)
                    {
                        RecordChunkFailure(telemetry);
                        continue;
                    }
                    try
                    {
                        AERISTerrainPreloadEncodedTile encoded;
                        using (var recordStream = new MemoryStream(record, false))
                        using (var recordReader = new BinaryReader(recordStream, Encoding.UTF8))
                        {
                            encoded = ReadEncoded(recordReader);
                            if (recordStream.Position != recordStream.Length)
                                throw new InvalidDataException(
                                    "preload tile record trailing data");
                        }
                        if (encoded.CompressedPayload == null ||
                            AERISTerrainCrc32.Compute(encoded.CompressedPayload) !=
                            encoded.PayloadCrc)
                        {
                            RecordChunkFailure(telemetry);
                            continue;
                        }
                        result[encoded.Key.StableId] = encoded;
                    }
                    catch
                    {
                        // Length framing keeps the next tile independently recoverable.
                        RecordChunkFailure(telemetry);
                    }
                }
            }
            return result;
        }

        void RecordChunkFailure(AERISTerrainPreloadTelemetry telemetry)
        {
            lock (indexSync) crcFailures++;
            if (telemetry != null) telemetry.DatabaseCrcFailures++;
        }

        static void WriteEncoded(BinaryWriter writer,
            AERISTerrainPreloadEncodedTile value)
        {
            WriteKey(writer, value.Key);
            writer.Write(value.Resolution);
            writer.Write(value.SouthLatitudeDeg);
            writer.Write(value.NorthLatitudeDeg);
            writer.Write(value.WestLongitudeDeg);
            writer.Write(value.EastLongitudeDeg);
            writer.Write(value.MinimumElevationMeters);
            writer.Write(value.MaximumElevationMeters);
            writer.Write(value.HeightOffset);
            writer.Write(value.HeightScale);
            writer.Write(value.Quality);
            writer.Write((int)value.GenerationState);
            writer.Write(value.GenerationUtcTicks);
            writer.Write(value.LastAccessUtcTicks);
            writer.Write(value.PqsConfigurationHash ?? string.Empty);
            writer.Write(value.GameDataHash ?? string.Empty);
            writer.Write(value.TerrainGenerationId);
            writer.Write((int)value.CodecId);
            writer.Write(value.CodecVersion);
            writer.Write(value.UncompressedSize);
            writer.Write(value.PayloadCrc);
            writer.Write(value.WaterOnly);
            writer.Write(value.ConstantHeight);
            writer.Write(value.FlatTile);
            int count = value.CompressedPayload == null ? 0 : value.CompressedPayload.Length;
            writer.Write(count);
            if (count > 0) writer.Write(value.CompressedPayload);
        }

        static AERISTerrainPreloadEncodedTile ReadEncoded(BinaryReader reader)
        {
            var value = new AERISTerrainPreloadEncodedTile
            {
                Key = ReadKey(reader),
                Resolution = reader.ReadInt32(),
                SouthLatitudeDeg = reader.ReadDouble(),
                NorthLatitudeDeg = reader.ReadDouble(),
                WestLongitudeDeg = reader.ReadDouble(),
                EastLongitudeDeg = reader.ReadDouble(),
                MinimumElevationMeters = reader.ReadSingle(),
                MaximumElevationMeters = reader.ReadSingle(),
                HeightOffset = reader.ReadSingle(),
                HeightScale = reader.ReadSingle(),
                Quality = reader.ReadInt32(),
                GenerationState = (AERISTerrainGenerationState)reader.ReadInt32(),
                GenerationUtcTicks = reader.ReadInt64(),
                LastAccessUtcTicks = reader.ReadInt64(),
                PqsConfigurationHash = reader.ReadString(),
                GameDataHash = reader.ReadString(),
                TerrainGenerationId = reader.ReadInt64(),
                CodecId = (AERISTerrainCodecId)reader.ReadInt32(),
                CodecVersion = reader.ReadInt32(),
                UncompressedSize = reader.ReadInt32(),
                PayloadCrc = reader.ReadUInt32(),
                WaterOnly = reader.ReadBoolean(),
                ConstantHeight = reader.ReadBoolean(),
                FlatTile = reader.ReadBoolean()
            };
            int count = reader.ReadInt32();
            if (count < 0 || count > 256 * 1024 * 1024)
                throw new InvalidDataException("preload tile blob length invalid");
            value.CompressedPayload = reader.ReadBytes(count);
            if (value.CompressedPayload.Length != count)
                throw new EndOfStreamException();
            return value;
        }

        static void WriteKey(BinaryWriter writer, AERISTerrainTileKey key)
        {
            writer.Write(key.BodyName ?? string.Empty);
            writer.Write(key.BodyRadiusMillimetres);
            writer.Write(key.EnvironmentHash ?? string.Empty);
            writer.Write((int)key.Lod);
            writer.Write(key.LatitudeIndex);
            writer.Write(key.LongitudeIndex);
            writer.Write(key.FormatVersion);
        }

        static AERISTerrainTileKey ReadKey(BinaryReader reader)
        {
            string body = reader.ReadString();
            long radiusMm = reader.ReadInt64();
            string environment = reader.ReadString();
            AERISTerrainTileLod lod = (AERISTerrainTileLod)reader.ReadInt32();
            int latitude = reader.ReadInt32();
            int longitude = reader.ReadInt32();
            int ignoredVersion = reader.ReadInt32();
            return new AERISTerrainTileKey(body, radiusMm / 1000.0,
                environment, lod, latitude, longitude);
        }

        string RelativeChunkPath(AERISTerrainTileKey key)
        {
            int chunkX = AERISTerrainSpatialKey.ChunkCoordinate(key.LongitudeIndex);
            int chunkY = AERISTerrainSpatialKey.ChunkCoordinate(key.LatitudeIndex);
            string bodyHash = AERISTerrainHash.Fnv1A64Hex(key.BodyName + "|" +
                key.BodyRadiusMillimetres + "|" + key.EnvironmentHash);
            string file = "c_" + AERISTerrainSpatialKey.Morton(chunkX, chunkY).
                ToString("X16", CultureInfo.InvariantCulture) + "_" + chunkX + "_" +
                chunkY + ".atb";
            return Path.Combine("Chunks", bodyHash,
                ((int)key.Lod).ToString(CultureInfo.InvariantCulture), file);
        }

        void RecoverJournal()
        {
            try
            {
                BeforeSynchronousDisk("TERRAIN_JOURNAL_EXISTS");
                if (!Directory.Exists(journalRoot)) return;
                BeforeSynchronousDisk("TERRAIN_JOURNAL_ENUMERATE_TEMP");
                string[] temporary = Directory.GetFiles(journalRoot, "*.tmp");
                for (int i = 0; i < temporary.Length; i++) TryDelete(temporary[i]);
                BeforeSynchronousDisk("TERRAIN_JOURNAL_ENUMERATE_PENDING");
                journalRecoveryNeeded = Directory.GetFiles(journalRoot,
                    "*.pending").Length > 0;
            }
            catch
            {
                // A conservative recovery request is safer than silently trusting an index
                // after the journal could not be inspected.
                journalRecoveryNeeded = true;
            }
        }

        void ClearRecoveryMarkers()
        {
            try
            {
                BeforeSynchronousDisk("TERRAIN_RECOVERY_MARKER_DIRECTORY_EXISTS");
                if (!Directory.Exists(journalRoot)) return;
                BeforeSynchronousDisk("TERRAIN_RECOVERY_MARKER_ENUMERATE");
                string[] markers = Directory.GetFiles(journalRoot, "*.pending");
                for (int i = 0; i < markers.Length; i++) TryDelete(markers[i]);
            }
            catch { }
        }

        void AtomicReplace(string source, string destination)
        {
            AtomicReplace(source, destination, false);
        }

        void AtomicReplace(string source, string destination,
            bool preserveBackup)
        {
            string backup = destination + ".bak";
            TryDelete(backup);
            BeforeSynchronousDisk("TERRAIN_ATOMIC_DESTINATION_EXISTS");
            if (File.Exists(destination))
            {
                try
                {
                    BeforeSynchronousDisk("TERRAIN_ATOMIC_REPLACE");
                    File.Replace(source, destination, backup);
                    if (!preserveBackup) TryDelete(backup);
                    return;
                }
                catch
                {
                    BeforeSynchronousDisk("TERRAIN_ATOMIC_BACKUP_COPY");
                    File.Copy(destination, backup, true);
                    BeforeSynchronousDisk("TERRAIN_ATOMIC_DESTINATION_DELETE");
                    File.Delete(destination);
                }
            }
            BeforeSynchronousDisk("TERRAIN_ATOMIC_MOVE");
            File.Move(source, destination);
            if (!preserveBackup) TryDelete(backup);
        }

        void TryDelete(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                BeforeSynchronousDisk("TERRAIN_DELETE_EXISTS");
                if (File.Exists(path))
                {
                    BeforeSynchronousDisk("TERRAIN_DELETE_FILE");
                    File.Delete(path);
                }
            }
            catch { }
        }

        static double Ema(double previous, double sample, double weight)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample) || sample < 0.0)
                return previous;
            return previous <= 0.0 ? sample : previous + (sample - previous) * weight;
        }

        public void Dispose()
        {
            if (disposed) return;
            lock (indexSync)
            {
                SaveManifestLocked();
                disposed = true;
            }
        }
    }
}
