using System;
using System.Collections.Generic;

namespace AERISFlightControl.Terrain
{
    internal enum AERISTerrainDisplayMode
    {
        Automatic = 0,
        Topographic = 1,
        Relative = 2,
        Off = 3
    }

    internal enum AERISTerrainGpuMode
    {
        Automatic = 0,
        On = 1,
        Off = 2
    }

    internal enum AERISTerrainColourPreset
    {
        Standard = 0,
        RedGreenAssist = 1,
        BlueYellowAssist = 2,
        HighContrast = 3
    }

    internal enum AERISTerrainTileLod
    {
        Global = 0,
        Far = 1,
        Route = 2,
        Local = 3,
        Land = 4
    }

    internal enum AERISTerrainTilePriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    // Request lanes are an explicit starvation boundary. Viewport coverage always
    // precedes selected-LAND/look-ahead/background refinement; no lane owns a thread.
    internal enum AERISTerrainRequestLane
    {
        Viewport = 0,
        Landing = 1,
        LookAhead = 2,
        Background = 3
    }

    internal enum AERISTerrainSamplingStage
    {
        Preview = 0,
        Final = 1
    }

    internal enum AERISTerrainGpuDrawState
    {
        None = 0,
        Partial = 1,
        Complete = 2
    }

    internal static class AERISTerrainTileFormat
    {
        internal const int Version = 3;
        internal const int DefaultResolution = 33;
        internal const int GlobalResolution = 17;
        internal const string Magic = "AERIS_TERRAIN_TILE_V3";

        internal static double NominalCellMeters(AERISTerrainTileLod lod)
        {
            switch (lod)
            {
                case AERISTerrainTileLod.Land: return 8.0;
                case AERISTerrainTileLod.Local: return 64.0;
                case AERISTerrainTileLod.Route: return 256.0;
                case AERISTerrainTileLod.Far: return 1024.0;
                default: return 8192.0;
            }
        }

        internal static int Resolution(AERISTerrainTileLod lod)
        {
            return lod == AERISTerrainTileLod.Global ? GlobalResolution : DefaultResolution;
        }

        internal static double AngularSpanDegrees(AERISTerrainTileLod lod, double bodyRadiusMeters)
        {
            int intervals = Math.Max(1, Resolution(lod) - 1);
            double radius = Math.Max(1000.0, bodyRadiusMeters);
            double degrees = NominalCellMeters(lod) * intervals / radius * 180.0 / Math.PI;
            return Math.Max(0.0001, Math.Min(45.0, degrees));
        }
    }

    internal struct AERISTerrainTileKey : IEquatable<AERISTerrainTileKey>
    {
        internal readonly string BodyName;
        internal readonly long BodyRadiusMillimetres;
        internal readonly string EnvironmentHash;
        internal readonly AERISTerrainTileLod Lod;
        internal readonly int LatitudeIndex;
        internal readonly int LongitudeIndex;
        internal readonly int FormatVersion;

        internal AERISTerrainTileKey(string bodyName, double bodyRadiusMeters,
            string environmentHash, AERISTerrainTileLod lod, int latitudeIndex,
            int longitudeIndex)
        {
            BodyName = bodyName ?? string.Empty;
            BodyRadiusMillimetres = (long)Math.Round(Math.Max(0.0, bodyRadiusMeters) * 1000.0);
            EnvironmentHash = environmentHash ?? string.Empty;
            Lod = lod;
            LatitudeIndex = latitudeIndex;
            LongitudeIndex = longitudeIndex;
            FormatVersion = AERISTerrainTileFormat.Version;
        }

        internal string StableId
        {
            get
            {
                return "T" + FormatVersion + "|" + Escape(BodyName) + "|" +
                    BodyRadiusMillimetres + "|" + Escape(EnvironmentHash) + "|" +
                    (int)Lod + "|" + LatitudeIndex + "|" + LongitudeIndex;
            }
        }

        internal string FileStem
        {
            get { return AERISTerrainHash.Fnv1A64Hex(StableId); }
        }

        public bool Equals(AERISTerrainTileKey other)
        {
            return BodyRadiusMillimetres == other.BodyRadiusMillimetres && Lod == other.Lod &&
                LatitudeIndex == other.LatitudeIndex && LongitudeIndex == other.LongitudeIndex &&
                FormatVersion == other.FormatVersion &&
                string.Equals(BodyName, other.BodyName, StringComparison.Ordinal) &&
                string.Equals(EnvironmentHash, other.EnvironmentHash, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AERISTerrainTileKey && Equals((AERISTerrainTileKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = BodyName == null ? 0 : BodyName.GetHashCode();
                hash = hash * 397 ^ BodyRadiusMillimetres.GetHashCode();
                hash = hash * 397 ^ (EnvironmentHash == null ? 0 : EnvironmentHash.GetHashCode());
                hash = hash * 397 ^ (int)Lod;
                hash = hash * 397 ^ LatitudeIndex;
                hash = hash * 397 ^ LongitudeIndex;
                hash = hash * 397 ^ FormatVersion;
                return hash;
            }
        }

        static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("|", "%7C");
        }
    }

    internal sealed class AERISTerrainHeightTile
    {
        internal AERISTerrainTileKey Key;
        internal int Resolution;
        internal double SouthLatitudeDeg;
        internal double NorthLatitudeDeg;
        internal double WestLongitudeDeg;
        internal double EastLongitudeDeg;
        internal float MinimumElevationMeters;
        internal float MaximumElevationMeters;
        internal float[] Elevation;
        internal byte[] Flags;
        internal long CreatedUtcTicks;
        internal long LastAccessSequence;
        internal int Quality;
        internal bool IsPreview;
        // True only after every sampling block for this resolution has completed.
        // IsPreview describes the fidelity stage; it must not be used as a proxy for
        // progressive completion because a complete low-resolution preview is still a
        // preview while a 25% final-resolution commit is not complete.
        internal bool SamplingComplete;
        internal AERISTerrainTileSource Source;
        internal string PqsConfigurationHash = string.Empty;
        internal string GameDataHash = string.Empty;
        internal long TerrainGenerationId;
        // CP3.75 Candidate7: coastal tiles carry a high-density boundary authority.
        // Coordinates are normalized tile-local x/y segment pairs (x0,y0,x1,y1...).
        // The 33x33/17x17 height field remains the elevation/contour authority; the
        // 129x129 class mask controls land/water fill geometry and coastline geometry.
        internal int HighDensityCoastlineResolution;
        internal float[] HighDensityCoastlineSegments;
        internal byte[] HighDensityCoastalFlags;

        internal long EstimatedBytes
        {
            get
            {
                long elevation = Elevation == null ? 0L : Elevation.LongLength * sizeof(float);
                long flags = Flags == null ? 0L : Flags.LongLength;
                long coastline = HighDensityCoastlineSegments == null ? 0L :
                    HighDensityCoastlineSegments.LongLength * sizeof(float);
                long coastalFlags = HighDensityCoastalFlags == null ? 0L :
                    HighDensityCoastalFlags.LongLength;
                return 280L + elevation + flags + coastline + coastalFlags;
            }
        }

        internal AERISTerrainHeightTile CloneImmutable()
        {
            return new AERISTerrainHeightTile
            {
                Key = Key,
                Resolution = Resolution,
                SouthLatitudeDeg = SouthLatitudeDeg,
                NorthLatitudeDeg = NorthLatitudeDeg,
                WestLongitudeDeg = WestLongitudeDeg,
                EastLongitudeDeg = EastLongitudeDeg,
                MinimumElevationMeters = MinimumElevationMeters,
                MaximumElevationMeters = MaximumElevationMeters,
                Elevation = Elevation == null ? null : (float[])Elevation.Clone(),
                Flags = Flags == null ? null : (byte[])Flags.Clone(),
                CreatedUtcTicks = CreatedUtcTicks,
                LastAccessSequence = LastAccessSequence,
                Quality = Quality,
                IsPreview = IsPreview,
                SamplingComplete = SamplingComplete,
                Source = Source,
                PqsConfigurationHash = PqsConfigurationHash ?? string.Empty,
                GameDataHash = GameDataHash ?? string.Empty,
                TerrainGenerationId = TerrainGenerationId,
                HighDensityCoastlineResolution = HighDensityCoastlineResolution,
                HighDensityCoastlineSegments = HighDensityCoastlineSegments == null ? null :
                    (float[])HighDensityCoastlineSegments.Clone(),
                HighDensityCoastalFlags = HighDensityCoastalFlags == null ? null :
                    (byte[])HighDensityCoastalFlags.Clone()
            };
        }
    }

    internal sealed class AERISTerrainTileRequest
    {
        internal AERISTerrainTileKey Key;
        internal AERISTerrainTilePriority Priority;
        internal double CenterLatitudeDeg;
        internal double CenterLongitudeDeg;
        internal double SouthLatitudeDeg;
        internal double NorthLatitudeDeg;
        internal double WestLongitudeDeg;
        internal double EastLongitudeDeg;
        internal int Resolution;
        internal int FinalResolution;
        internal AERISTerrainSamplingStage Stage;
        internal AERISTerrainRequestLane Lane;
        internal double ViewDistanceMeters;
        internal long RequestSequence;
        internal long BodyGeneration;
        internal long VesselGeneration;
        internal long TerrainGeneration;
        internal long ViewGeneration;
        internal long RangeGeneration;
        internal long PlanGeneration;
        internal long DatabaseGeneration;
        internal AERISTerrainReadLane ReadLane;
        internal AERISTerrainWorkOwner WorkOwner;
        internal bool Visible;
    }

    internal sealed class AERISTerrainVisibleTileSet
    {
        internal long ViewGeneration;
        internal long TerrainGeneration;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal double CenterLatitudeDeg;
        internal double CenterLongitudeDeg;
        internal double RangeMeters;
        internal AERISTerrainHeightTile[] Tiles = new AERISTerrainHeightTile[0];
        internal int RequestedCount;
        internal int MissingCount;
        internal int FoundationRequestedCount;
        internal int FoundationMissingCount;
        internal int GlobalFoundationCount;
        internal int FarFoundationCount;
        internal bool FoundationComplete;
        internal bool GlobalFallbackAvailable;
        internal string Status = string.Empty;
    }

    internal sealed class AERISTerrainTileCacheTelemetry
    {
        internal long RamBytes;
        internal long RamLimitBytes;
        internal long DiskBytes;
        internal long DiskLimitBytes;
        internal int RamTileCount;
        internal int DiskTileCount;
        internal long RamHits;
        internal long DiskHits;
        internal long Misses;
        internal long Reused;
        internal long Generated;
        internal long DiskWrites;
        internal long DiskFailures;
        internal long StaleCancelled;
        internal long DroppedRequests;
        internal long PreviewGenerated;
        internal long FinalGenerated;
        internal long ObsoleteCancelled;
        internal int PendingRequests;
        internal int DesiredRequests;
        internal int VisibleRequests;
        internal int PreviewTileCount;
        internal int SamplingRemaining;
        internal int LastSamplingBatchSamples;
        internal double LastSamplingBatchMilliseconds;
        internal long WarmBytes;
        internal int WarmTileCount;
        internal AERISTerrainPreloadTelemetry Preload = new AERISTerrainPreloadTelemetry();

        internal double HitRate
        {
            get
            {
                double total = RamHits + DiskHits + Misses;
                return total <= 0.0 ? 0.0 : (RamHits + DiskHits) / total;
            }
        }
    }

    internal static class AERISTerrainHash
    {
        internal static string Fnv1A64Hex(string value)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                string text = value ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    char c = text[i];
                    hash ^= (byte)(c & 0xff);
                    hash *= 1099511628211UL;
                    hash ^= (byte)((c >> 8) & 0xff);
                    hash *= 1099511628211UL;
                }
                return hash.ToString("X16");
            }
        }
    }
}
