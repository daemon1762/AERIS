using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace AERISFlightControl.Terrain
{
    internal static class AERISTerrainCrc32
    {
        static readonly uint[] Table = BuildTable();

        internal static uint Compute(byte[] data)
        {
            if (data == null) return 0U;
            return Compute(data, 0, data.Length);
        }

        internal static uint Compute(byte[] data, int offset, int count)
        {
            uint crc = 0xffffffffU;
            if (data == null) return ~crc;
            int start = Math.Max(0, offset);
            long requestedEnd = (long)start + Math.Max(0, count);
            int end = (int)Math.Min(data.Length, requestedEnd);
            for (int i = start; i < end; i++)
                crc = Table[(crc ^ data[i]) & 0xffU] ^ (crc >> 8);
            return ~crc;
        }

        static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1U) != 0U ? 0xedb88320U ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }
    }

    internal static class AERISTerrainPreloadCodec
    {
        const byte PayloadVersion = 1;
        const byte FlagWaterOnly = 1;
        const byte FlagConstantHeight = 2;
        const byte FlagFlatTile = 4;

        internal static AERISTerrainPreloadEncodedTile Encode(AERISTerrainHeightTile tile,
            string pqsHash, string gameDataHash, long terrainGenerationId,
            AERISTerrainCodecId preferredCodec)
        {
            if (tile == null || tile.Elevation == null || tile.Flags == null)
                throw new ArgumentNullException("tile");
            int sampleCount = checked(tile.Resolution * tile.Resolution);
            if (tile.Elevation.Length < sampleCount || tile.Flags.Length < sampleCount)
                throw new InvalidDataException("terrain tile payload is incomplete");

            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            bool allWater = true;
            bool allSameHeight = true;
            float first = tile.Elevation.Length == 0 ? 0f : tile.Elevation[0];
            for (int i = 0; i < sampleCount; i++)
            {
                float value = tile.Elevation[i];
                if (float.IsNaN(value) || float.IsInfinity(value)) value = 0f;
                minimum = Math.Min(minimum, value);
                maximum = Math.Max(maximum, value);
                allWater = allWater && tile.Flags[i] == 2;
                allSameHeight = allSameHeight && Math.Abs(value - first) <= 0.0001f;
            }
            if (float.IsPositiveInfinity(minimum)) minimum = 0f;
            if (float.IsNegativeInfinity(maximum)) maximum = minimum;
            float span = Math.Max(0f, maximum - minimum);
            float scale = span <= 0.0001f ? 0f : span / 65535f;
            bool flat = span <= 0.05f;
            bool constant = allSameHeight || flat;

            byte[] raw;
            using (var memory = new MemoryStream(Math.Max(64, sampleCount * 3)))
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write(PayloadVersion);
                byte flags = 0;
                if (allWater) flags |= FlagWaterOnly;
                if (constant) flags |= FlagConstantHeight;
                if (flat) flags |= FlagFlatTile;
                writer.Write(flags);
                writer.Write(tile.Resolution);
                writer.Write(minimum);
                writer.Write(scale);
                if (!constant)
                {
                    for (int row = 0; row < tile.Resolution; row++)
                    {
                        int previous = 0;
                        for (int column = 0; column < tile.Resolution; column++)
                        {
                            int index = row * tile.Resolution + column;
                            int quantized = scale <= 0f ? 0 : (int)Math.Round(
                                Math.Max(0f, Math.Min(65535f,
                                (tile.Elevation[index] - minimum) / scale)));
                            int delta = quantized - previous;
                            WriteVarUInt(writer, ZigZag(delta));
                            previous = quantized;
                        }
                    }
                }
                if (!allWater)
                {
                    int index = 0;
                    while (index < sampleCount)
                    {
                        byte value = tile.Flags[index];
                        int run = 1;
                        while (index + run < sampleCount && tile.Flags[index + run] == value &&
                            run < 65535) run++;
                        writer.Write(value);
                        WriteVarUInt(writer, (uint)run);
                        index += run;
                    }
                }
                writer.Flush();
                raw = memory.ToArray();
            }

            AERISTerrainCodecId codec = preferredCodec;
            byte[] compressed = Compress(raw, codec);
            if (compressed == null || compressed.Length >= raw.Length)
            {
                codec = AERISTerrainCodecId.Raw;
                compressed = raw;
            }
            return new AERISTerrainPreloadEncodedTile
            {
                Key = tile.Key,
                Resolution = tile.Resolution,
                SouthLatitudeDeg = tile.SouthLatitudeDeg,
                NorthLatitudeDeg = tile.NorthLatitudeDeg,
                WestLongitudeDeg = tile.WestLongitudeDeg,
                EastLongitudeDeg = tile.EastLongitudeDeg,
                MinimumElevationMeters = minimum,
                MaximumElevationMeters = maximum,
                HeightOffset = minimum,
                HeightScale = scale,
                Quality = tile.Quality,
                GenerationState = tile.SamplingComplete && !tile.IsPreview ?
                    AERISTerrainGenerationState.Complete :
                    AERISTerrainGenerationState.Partial,
                GenerationUtcTicks = tile.CreatedUtcTicks <= 0L ? DateTime.UtcNow.Ticks :
                    tile.CreatedUtcTicks,
                LastAccessUtcTicks = DateTime.UtcNow.Ticks,
                PqsConfigurationHash = pqsHash ?? string.Empty,
                GameDataHash = gameDataHash ?? string.Empty,
                TerrainGenerationId = terrainGenerationId,
                CodecId = codec,
                CodecVersion = AERISTerrainPreloadFormat.CodecVersion,
                UncompressedSize = raw.Length,
                CompressedPayload = compressed,
                PayloadCrc = AERISTerrainCrc32.Compute(compressed),
                WaterOnly = allWater,
                ConstantHeight = constant,
                FlatTile = flat
            };
        }

        internal static AERISTerrainHeightTile Decode(AERISTerrainPreloadEncodedTile encoded)
        {
            if (encoded == null || encoded.CompressedPayload == null)
                throw new ArgumentNullException("encoded");
            if (encoded.CodecVersion != AERISTerrainPreloadFormat.CodecVersion)
                throw new InvalidDataException("unsupported terrain codec version");
            if (AERISTerrainCrc32.Compute(encoded.CompressedPayload) != encoded.PayloadCrc)
                throw new InvalidDataException("terrain payload CRC mismatch");
            byte[] raw = Decompress(encoded.CompressedPayload, encoded.CodecId,
                encoded.UncompressedSize);
            using (var memory = new MemoryStream(raw, false))
            using (var reader = new BinaryReader(memory))
            {
                byte version = reader.ReadByte();
                if (version != PayloadVersion)
                    throw new InvalidDataException("unsupported terrain payload version");
                byte payloadFlags = reader.ReadByte();
                int resolution = reader.ReadInt32();
                if (resolution < 2 || resolution > 1025 || resolution != encoded.Resolution)
                    throw new InvalidDataException("terrain resolution mismatch");
                float offset = reader.ReadSingle();
                float scale = reader.ReadSingle();
                int sampleCount = checked(resolution * resolution);
                var elevation = new float[sampleCount];
                var flags = new byte[sampleCount];
                bool waterOnly = (payloadFlags & FlagWaterOnly) != 0;
                bool constant = (payloadFlags & FlagConstantHeight) != 0;
                if (constant)
                {
                    for (int i = 0; i < sampleCount; i++) elevation[i] = offset;
                }
                else
                {
                    for (int row = 0; row < resolution; row++)
                    {
                        int previous = 0;
                        for (int column = 0; column < resolution; column++)
                        {
                            int delta = UnZigZag(ReadVarUInt(reader));
                            int quantized = Math.Max(0, Math.Min(65535, previous + delta));
                            elevation[row * resolution + column] = offset + quantized * scale;
                            previous = quantized;
                        }
                    }
                }
                if (waterOnly)
                {
                    for (int i = 0; i < sampleCount; i++) flags[i] = 2;
                }
                else
                {
                    int index = 0;
                    while (index < sampleCount && memory.Position < memory.Length)
                    {
                        byte value = reader.ReadByte();
                        int run = checked((int)ReadVarUInt(reader));
                        if (run <= 0 || index + run > sampleCount)
                            throw new InvalidDataException("terrain flag RLE overflow");
                        for (int i = 0; i < run; i++) flags[index++] = value;
                    }
                    if (index != sampleCount)
                        throw new InvalidDataException("terrain flag RLE incomplete");
                }
                return new AERISTerrainHeightTile
                {
                    Key = encoded.Key,
                    Resolution = resolution,
                    SouthLatitudeDeg = encoded.SouthLatitudeDeg,
                    NorthLatitudeDeg = encoded.NorthLatitudeDeg,
                    WestLongitudeDeg = encoded.WestLongitudeDeg,
                    EastLongitudeDeg = encoded.EastLongitudeDeg,
                    MinimumElevationMeters = encoded.MinimumElevationMeters,
                    MaximumElevationMeters = encoded.MaximumElevationMeters,
                    Elevation = elevation,
                    Flags = flags,
                    CreatedUtcTicks = encoded.GenerationUtcTicks,
                    Quality = encoded.Quality,
                    IsPreview = encoded.GenerationState != AERISTerrainGenerationState.Complete,
                    SamplingComplete = encoded.GenerationState ==
                        AERISTerrainGenerationState.Complete,
                    Source = AERISTerrainTileSource.PreloadDatabase,
                    PqsConfigurationHash = encoded.PqsConfigurationHash,
                    GameDataHash = encoded.GameDataHash,
                    TerrainGenerationId = encoded.TerrainGenerationId
                };
            }
        }

        static byte[] Compress(byte[] raw, AERISTerrainCodecId codec)
        {
            if (codec == AERISTerrainCodecId.Raw) return raw;
            if (codec != AERISTerrainCodecId.Deflate)
                throw new InvalidDataException("unsupported terrain codec");
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, true))
                    deflate.Write(raw, 0, raw.Length);
                return output.ToArray();
            }
        }

        static byte[] Decompress(byte[] payload, AERISTerrainCodecId codec,
            int expectedSize)
        {
            if (codec == AERISTerrainCodecId.Raw) return (byte[])payload.Clone();
            if (codec != AERISTerrainCodecId.Deflate)
                throw new InvalidDataException("unsupported terrain codec");
            using (var input = new MemoryStream(payload, false))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(Math.Max(64, expectedSize)))
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = deflate.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, read);
                byte[] raw = output.ToArray();
                if (expectedSize > 0 && raw.Length != expectedSize)
                    throw new InvalidDataException("terrain decompressed size mismatch");
                return raw;
            }
        }

        static uint ZigZag(int value)
        {
            return unchecked((uint)((value << 1) ^ (value >> 31)));
        }

        static int UnZigZag(uint value)
        {
            return unchecked((int)((value >> 1) ^ (uint)-(int)(value & 1U)));
        }

        static void WriteVarUInt(BinaryWriter writer, uint value)
        {
            while (value >= 0x80U)
            {
                writer.Write((byte)(value | 0x80U));
                value >>= 7;
            }
            writer.Write((byte)value);
        }

        static uint ReadVarUInt(BinaryReader reader)
        {
            uint value = 0U;
            int shift = 0;
            while (shift < 35)
            {
                byte next = reader.ReadByte();
                value |= (uint)(next & 0x7f) << shift;
                if ((next & 0x80) == 0) return value;
                shift += 7;
            }
            throw new InvalidDataException("terrain varint overflow");
        }
    }

    internal sealed class AERISTerrainWarmTileCache
    {
        sealed class Entry
        {
            internal AERISTerrainPreloadEncodedTile Tile;
            internal LinkedListNode<string> Node;
            internal int Priority;
        }

        readonly object sync = new object();
        readonly Dictionary<string, Entry> entries =
            new Dictionary<string, Entry>(StringComparer.Ordinal);
        readonly LinkedList<string> lru = new LinkedList<string>();
        long limitBytes;
        long usedBytes;

        internal AERISTerrainWarmTileCache(long limitBytes)
        {
            SetLimit(limitBytes);
        }

        internal long UsedBytes { get { lock (sync) return usedBytes; } }
        internal int Count { get { lock (sync) return entries.Count; } }

        internal void SetLimit(long bytes)
        {
            lock (sync)
            {
                limitBytes = Math.Max(8L * 1024L * 1024L, bytes);
                TrimLocked();
            }
        }

        internal bool TryGet(AERISTerrainTileKey key,
            out AERISTerrainPreloadEncodedTile tile)
        {
            tile = null;
            lock (sync)
            {
                Entry entry;
                if (!entries.TryGetValue(key.StableId, out entry) || entry == null ||
                    entry.Tile == null) return false;
                if (entry.Node != null) lru.Remove(entry.Node);
                entry.Node = lru.AddLast(key.StableId);
                tile = entry.Tile.CloneImmutable();
                return true;
            }
        }

        internal void Put(AERISTerrainPreloadEncodedTile tile, int priority)
        {
            if (tile == null || tile.CompressedPayload == null) return;
            string id = tile.Key.StableId;
            lock (sync)
            {
                Entry previous;
                if (entries.TryGetValue(id, out previous) && previous != null)
                {
                    usedBytes -= previous.Tile == null ? 0L : previous.Tile.EstimatedBytes;
                    if (previous.Node != null) lru.Remove(previous.Node);
                }
                var entry = new Entry
                {
                    Tile = tile.CloneImmutable(),
                    Priority = priority
                };
                entry.Node = lru.AddLast(id);
                entries[id] = entry;
                usedBytes += entry.Tile.EstimatedBytes;
                TrimLocked();
            }
        }

        internal void Remove(AERISTerrainTileKey key)
        {
            string id = key.StableId;
            lock (sync)
            {
                Entry entry;
                if (!entries.TryGetValue(id, out entry) || entry == null) return;
                entries.Remove(id);
                if (entry.Node != null) lru.Remove(entry.Node);
                usedBytes = Math.Max(0L, usedBytes -
                    (entry.Tile == null ? 0L : entry.Tile.EstimatedBytes));
            }
        }

        internal void Clear()
        {
            lock (sync)
            {
                entries.Clear();
                lru.Clear();
                usedBytes = 0L;
            }
        }

        void TrimLocked()
        {
            while (usedBytes > limitBytes && lru.Count > 0)
            {
                LinkedListNode<string> candidate = lru.First;
                LinkedListNode<string> scan = lru.First;
                int lowestPriority = int.MaxValue;
                int scanned = 0;
                while (scan != null && scanned++ < 64)
                {
                    Entry value;
                    if (entries.TryGetValue(scan.Value, out value) && value != null &&
                        value.Priority < lowestPriority)
                    {
                        lowestPriority = value.Priority;
                        candidate = scan;
                    }
                    scan = scan.Next;
                }
                string id = candidate.Value;
                lru.Remove(candidate);
                Entry entry;
                if (!entries.TryGetValue(id, out entry)) continue;
                entries.Remove(id);
                usedBytes -= entry.Tile == null ? 0L : entry.Tile.EstimatedBytes;
            }
            if (usedBytes < 0L) usedBytes = 0L;
        }
    }
}
