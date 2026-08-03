using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace AERISFlightControl.Terrain
{
    internal sealed class AERISTerrainRamTileCache
    {
        sealed class Entry
        {
            internal AERISTerrainHeightTile Tile;
            internal LinkedListNode<string> Node;
        }

        readonly object sync = new object();
        readonly Dictionary<string, Entry> byId =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly LinkedList<string> lru = new LinkedList<string>();
        long limitBytes;
        long usedBytes;
        long accessSequence;

        internal AERISTerrainRamTileCache(long limitBytes)
        {
            SetLimit(limitBytes);
        }

        internal long UsedBytes { get { lock (sync) return usedBytes; } }
        internal long LimitBytes { get { lock (sync) return limitBytes; } }
        internal int Count { get { lock (sync) return byId.Count; } }

        internal void SetLimit(long value)
        {
            lock (sync)
            {
                limitBytes = Math.Max(8L * 1024L * 1024L, value);
                TrimLocked();
            }
        }

        internal bool TryGet(AERISTerrainTileKey key, out AERISTerrainHeightTile tile)
        {
            lock (sync)
            {
                Entry entry;
                if (!byId.TryGetValue(key.StableId, out entry) || entry == null ||
                    entry.Tile == null)
                {
                    tile = null;
                    return false;
                }
                if (entry.Node != null)
                {
                    lru.Remove(entry.Node);
                    entry.Node = lru.AddLast(key.StableId);
                }
                accessSequence++;
                tile = entry.Tile;
                return true;
            }
        }

        internal void Put(AERISTerrainHeightTile tile)
        {
            if (tile == null || tile.Elevation == null || tile.Flags == null) return;
            string id = tile.Key.StableId;
            lock (sync)
            {
                Entry existing;
                if (byId.TryGetValue(id, out existing) && existing != null)
                {
                    if (existing.Tile != null) usedBytes -= existing.Tile.EstimatedBytes;
                    if (existing.Node != null) lru.Remove(existing.Node);
                }
                else existing = new Entry();
                accessSequence++;
                existing.Tile = tile;
                existing.Node = lru.AddLast(id);
                byId[id] = existing;
                usedBytes += tile.EstimatedBytes;
                TrimLocked();
            }
        }


        internal int CountPreviewTiles(ICollection<string> stableIds)
        {
            if (stableIds == null || stableIds.Count == 0) return 0;
            int count = 0;
            lock (sync)
            {
                foreach (string id in stableIds)
                {
                    Entry entry;
                    if (string.IsNullOrEmpty(id) ||
                        !byId.TryGetValue(id, out entry) || entry == null ||
                        entry.Tile == null || !entry.Tile.IsPreview) continue;
                    count++;
                }
            }
            return count;
        }

        internal void Clear()
        {
            lock (sync)
            {
                byId.Clear();
                lru.Clear();
                usedBytes = 0L;
            }
        }

        void TrimLocked()
        {
            while (usedBytes > limitBytes && lru.First != null)
            {
                string id = lru.First.Value;
                lru.RemoveFirst();
                Entry entry;
                if (!byId.TryGetValue(id, out entry)) continue;
                byId.Remove(id);
                if (entry != null && entry.Tile != null)
                    usedBytes = Math.Max(0L, usedBytes - entry.Tile.EstimatedBytes);
            }
        }
    }

    internal sealed class AERISTerrainDiskTileCache
    {
        sealed class DiskEntry
        {
            internal string StableId;
            internal string FileName;
            internal long Bytes;
            internal long LastAccessUtcTicks;
        }

        const string IndexMagic = "AERIS_TERRAIN_INDEX_V2";
        readonly object sync = new object();
        readonly Dictionary<string, DiskEntry> byId =
            new Dictionary<string, DiskEntry>(StringComparer.Ordinal);
        readonly string root;
        readonly string tileRoot;
        readonly string indexPath;
        long limitBytes;
        long usedBytes;
        bool indexLoaded;
        bool indexDirty;

        internal AERISTerrainDiskTileCache(string root, long limitBytes)
        {
            this.root = root ?? string.Empty;
            tileRoot = Path.Combine(this.root, "Tiles");
            indexPath = Path.Combine(this.root, "index.tsv");
            SetLimit(limitBytes);
            LoadIndexOnly();
        }

        internal bool IndexLoaded { get { lock (sync) return indexLoaded; } }
        internal long UsedBytes { get { lock (sync) return usedBytes; } }
        internal long LimitBytes { get { lock (sync) return limitBytes; } }
        internal int Count { get { lock (sync) return byId.Count; } }

        internal void SetLimit(long value)
        {
            lock (sync)
            {
                limitBytes = Math.Max(64L * 1024L * 1024L, value);
                if (indexLoaded)
                {
                    PruneLocked();
                    SaveIndexLocked();
                }
            }
        }

        internal bool Contains(AERISTerrainTileKey key)
        {
            lock (sync) return byId.ContainsKey(key.StableId);
        }

        internal void LoadIndexOnly()
        {
            lock (sync)
            {
                byId.Clear();
                usedBytes = 0L;
                indexLoaded = true;
                indexDirty = false;
                try
                {
                    if (!File.Exists(indexPath)) return;
                    string[] lines = File.ReadAllLines(indexPath, Encoding.UTF8);
                    if (lines.Length == 0 || !string.Equals(lines[0], IndexMagic,
                        StringComparison.Ordinal)) return;
                    for (int i = 1; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (string.IsNullOrEmpty(line)) continue;
                        string[] parts = line.Split('\t');
                        if (parts.Length != 4) continue;
                        long bytes, ticks;
                        if (!long.TryParse(parts[2], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out bytes) || bytes <= 0L) continue;
                        if (!long.TryParse(parts[3], NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out ticks)) ticks = 0L;
                        string fileName = Path.GetFileName(parts[1]);
                        string full = Path.Combine(tileRoot, fileName);
                        if (!File.Exists(full)) continue;
                        long actualBytes;
                        try { actualBytes = new FileInfo(full).Length; }
                        catch { continue; }
                        if (actualBytes <= 0L) continue;
                        var entry = new DiskEntry
                        {
                            StableId = parts[0], FileName = fileName,
                            Bytes = actualBytes, LastAccessUtcTicks = ticks
                        };
                        DiskEntry duplicate;
                        if (byId.TryGetValue(entry.StableId, out duplicate) &&
                            duplicate != null)
                            usedBytes = Math.Max(0L, usedBytes - duplicate.Bytes);
                        byId[entry.StableId] = entry;
                        usedBytes += actualBytes;
                    }
                }
                catch
                {
                    byId.Clear();
                    usedBytes = 0L;
                }
            }
        }

        internal bool TryLoad(AERISTerrainTileKey key, out AERISTerrainHeightTile tile)
        {
            tile = null;
            DiskEntry entry;
            lock (sync)
            {
                if (!byId.TryGetValue(key.StableId, out entry) || entry == null) return false;
            }
            string path = Path.Combine(tileRoot, entry.FileName);
            try
            {
                AERISTerrainHeightTile loaded = ReadTile(path);
                if (loaded == null || !loaded.Key.Equals(key))
                {
                    RemoveBroken(key.StableId, entry.Bytes);
                    return false;
                }
                lock (sync)
                {
                    DiskEntry current;
                    if (byId.TryGetValue(key.StableId, out current) && current != null)
                    {
                        current.LastAccessUtcTicks = DateTime.UtcNow.Ticks;
                        indexDirty = true;
                    }
                }
                tile = loaded;
                return true;
            }
            catch
            {
                RemoveBroken(key.StableId, entry.Bytes);
                return false;
            }
        }

        internal bool Save(AERISTerrainHeightTile tile)
        {
            if (tile == null || !tile.SamplingComplete ||
                tile.Elevation == null || tile.Flags == null) return false;
            Directory.CreateDirectory(tileRoot);
            string fileName = tile.Key.FileStem + ".att";
            string path = Path.Combine(tileRoot, fileName);
            string temporary = path + ".tmp";
            try
            {
                WriteTile(temporary, tile);
                long bytes = new FileInfo(temporary).Length;
                if (bytes <= 0L) throw new InvalidDataException("empty terrain tile");
                AERISTerrainHeightTile verify = ReadTile(temporary);
                if (!EquivalentPayload(tile, verify))
                    throw new InvalidDataException("terrain tile full round-trip mismatch");
                AtomicMove(temporary, path);
                lock (sync)
                {
                    DiskEntry previous;
                    if (byId.TryGetValue(tile.Key.StableId, out previous) && previous != null)
                        usedBytes = Math.Max(0L, usedBytes - previous.Bytes);
                    var entry = new DiskEntry
                    {
                        StableId = tile.Key.StableId,
                        FileName = fileName,
                        Bytes = bytes,
                        LastAccessUtcTicks = DateTime.UtcNow.Ticks
                    };
                    byId[entry.StableId] = entry;
                    usedBytes += bytes;
                    indexDirty = true;
                    PruneLocked();
                    SaveIndexLocked();
                }
                return true;
            }
            catch
            {
                TryDelete(temporary);
                return false;
            }
        }

        internal void FlushIndex()
        {
            lock (sync) SaveIndexLocked();
        }

        void RemoveBroken(string stableId, long bytes)
        {
            string brokenPath = string.Empty;
            lock (sync)
            {
                DiskEntry entry;
                if (byId.TryGetValue(stableId, out entry))
                {
                    byId.Remove(stableId);
                    usedBytes = Math.Max(0L, usedBytes - Math.Max(bytes, entry.Bytes));
                    brokenPath = Path.Combine(tileRoot, entry.FileName ?? string.Empty);
                    indexDirty = true;
                    SaveIndexLocked();
                }
            }
            TryDelete(brokenPath);
        }

        void PruneLocked()
        {
            if (usedBytes <= limitBytes) return;
            var entries = new List<DiskEntry>(byId.Values);
            entries.Sort((a, b) => a.LastAccessUtcTicks.CompareTo(b.LastAccessUtcTicks));
            for (int i = 0; i < entries.Count && usedBytes > limitBytes; i++)
            {
                DiskEntry entry = entries[i];
                byId.Remove(entry.StableId);
                usedBytes = Math.Max(0L, usedBytes - entry.Bytes);
                TryDelete(Path.Combine(tileRoot, entry.FileName));
                indexDirty = true;
            }
        }

        void SaveIndexLocked()
        {
            if (!indexDirty) return;
            Directory.CreateDirectory(root);
            Directory.CreateDirectory(tileRoot);
            string temporary = indexPath + ".tmp";
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
            {
                writer.WriteLine(IndexMagic);
                var entries = new List<DiskEntry>(byId.Values);
                entries.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
                for (int i = 0; i < entries.Count; i++)
                {
                    DiskEntry entry = entries[i];
                    writer.Write(entry.StableId); writer.Write('\t');
                    writer.Write(entry.FileName); writer.Write('\t');
                    writer.Write(entry.Bytes.ToString(CultureInfo.InvariantCulture)); writer.Write('\t');
                    writer.WriteLine(entry.LastAccessUtcTicks.ToString(CultureInfo.InvariantCulture));
                }
            }
            AtomicMove(temporary, indexPath);
            indexDirty = false;
        }

        static void WriteTile(string path, AERISTerrainHeightTile tile)
        {
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write,
                FileShare.None, 65536, FileOptions.SequentialScan))
            using (var deflate = new DeflateStream(file, CompressionLevel.Fastest, false))
            using (var writer = new BinaryWriter(deflate, Encoding.UTF8, false))
            {
                writer.Write(AERISTerrainTileFormat.Magic);
                writer.Write(tile.Key.BodyName ?? string.Empty);
                writer.Write(tile.Key.BodyRadiusMillimetres);
                writer.Write(tile.Key.EnvironmentHash ?? string.Empty);
                writer.Write((int)tile.Key.Lod);
                writer.Write(tile.Key.LatitudeIndex);
                writer.Write(tile.Key.LongitudeIndex);
                writer.Write(tile.Key.FormatVersion);
                writer.Write(tile.Resolution);
                writer.Write(tile.SouthLatitudeDeg);
                writer.Write(tile.NorthLatitudeDeg);
                writer.Write(tile.WestLongitudeDeg);
                writer.Write(tile.EastLongitudeDeg);
                writer.Write(tile.MinimumElevationMeters);
                writer.Write(tile.MaximumElevationMeters);
                writer.Write(tile.CreatedUtcTicks);
                writer.Write(tile.Quality);
                writer.Write(tile.Elevation.Length);
                for (int i = 0; i < tile.Elevation.Length; i++) writer.Write(tile.Elevation[i]);
                writer.Write(tile.Flags.Length);
                writer.Write(tile.Flags);
            }
        }

        static AERISTerrainHeightTile ReadTile(string path)
        {
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.SequentialScan))
            using (var deflate = new DeflateStream(file, CompressionMode.Decompress, false))
            using (var reader = new BinaryReader(deflate, Encoding.UTF8, false))
            {
                if (!string.Equals(reader.ReadString(), AERISTerrainTileFormat.Magic,
                    StringComparison.Ordinal)) return null;
                string body = reader.ReadString();
                long radiusMm = reader.ReadInt64();
                string environment = reader.ReadString();
                AERISTerrainTileLod lod = (AERISTerrainTileLod)reader.ReadInt32();
                int lat = reader.ReadInt32();
                int lon = reader.ReadInt32();
                int format = reader.ReadInt32();
                if (format != AERISTerrainTileFormat.Version) return null;
                var key = new AERISTerrainTileKey(body, radiusMm / 1000.0,
                    environment, lod, lat, lon);
                int resolution = reader.ReadInt32();
                if (resolution < 2 || resolution > 257) return null;
                var tile = new AERISTerrainHeightTile
                {
                    Key = key,
                    Resolution = resolution,
                    SouthLatitudeDeg = reader.ReadDouble(),
                    NorthLatitudeDeg = reader.ReadDouble(),
                    WestLongitudeDeg = reader.ReadDouble(),
                    EastLongitudeDeg = reader.ReadDouble(),
                    MinimumElevationMeters = reader.ReadSingle(),
                    MaximumElevationMeters = reader.ReadSingle(),
                    CreatedUtcTicks = reader.ReadInt64(),
                    Quality = reader.ReadInt32(),
                    SamplingComplete = true
                };
                int elevationCount = reader.ReadInt32();
                long expected = (long)resolution * resolution;
                if (elevationCount != expected || elevationCount <= 0 ||
                    elevationCount > 257 * 257) return null;
                tile.Elevation = new float[elevationCount];
                for (int i = 0; i < elevationCount; i++) tile.Elevation[i] = reader.ReadSingle();
                int flagCount = reader.ReadInt32();
                if (flagCount != elevationCount) return null;
                tile.Flags = reader.ReadBytes(flagCount);
                if (tile.Flags == null || tile.Flags.Length != flagCount) return null;
                return tile;
            }
        }

        static bool EquivalentPayload(AERISTerrainHeightTile expected,
            AERISTerrainHeightTile actual)
        {
            if (expected == null || actual == null || !actual.Key.Equals(expected.Key) ||
                actual.Resolution != expected.Resolution ||
                actual.SouthLatitudeDeg != expected.SouthLatitudeDeg ||
                actual.NorthLatitudeDeg != expected.NorthLatitudeDeg ||
                actual.WestLongitudeDeg != expected.WestLongitudeDeg ||
                actual.EastLongitudeDeg != expected.EastLongitudeDeg ||
                actual.MinimumElevationMeters != expected.MinimumElevationMeters ||
                actual.MaximumElevationMeters != expected.MaximumElevationMeters ||
                actual.CreatedUtcTicks != expected.CreatedUtcTicks ||
                actual.Quality != expected.Quality || actual.Elevation == null ||
                expected.Elevation == null || actual.Flags == null || expected.Flags == null ||
                actual.Elevation.Length != expected.Elevation.Length ||
                actual.Flags.Length != expected.Flags.Length) return false;
            for (int i = 0; i < expected.Elevation.Length; i++)
                if (!actual.Elevation[i].Equals(expected.Elevation[i])) return false;
            for (int i = 0; i < expected.Flags.Length; i++)
                if (actual.Flags[i] != expected.Flags[i]) return false;
            return true;
        }

        static void AtomicMove(string source, string destination)
        {
            string backup = destination + ".bak";
            TryDelete(backup);
            bool backedUp = false;
            if (File.Exists(destination))
            {
                File.Move(destination, backup);
                backedUp = true;
            }
            try
            {
                File.Move(source, destination);
                TryDelete(backup);
            }
            catch
            {
                TryDelete(destination);
                if (backedUp && File.Exists(backup)) File.Move(backup, destination);
                throw;
            }
        }

        static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }
    }
}
