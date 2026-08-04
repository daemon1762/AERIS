using System;
using System.Collections.Generic;

namespace AERISFlightControl.Terrain
{
    internal enum AERISTerrainPreloadMode
    {
        Off = 0,
        Manual = 1,
        IdleOnly = 2,
        Background = 3,
        AggressiveIdle = 4
    }

    internal enum AERISTerrainPreloadSpeedProfile
    {
        Balanced = 0,
        Fast = 1,
        Maximum = 2
    }

    internal enum AERISTerrainBodyPriority
    {
        Disabled = 0,
        Low = 1,
        Normal = 2,
        High = 3,
        Pinned = 4
    }

    internal enum AERISTerrainReadLane
    {
        Critical = 0,
        High = 1,
        Normal = 2,
        Prefetch = 3,
        Background = 4
    }

    internal enum AERISTerrainTileSource
    {
        Unknown = 0,
        HotRam = 1,
        WarmRam = 2,
        PreloadDatabase = 3,
        RealtimeGenerated = 4,
        GlobalFallback = 5,
        LegacyMigration = 6,
        PreloadBuilderGenerated = 7
    }

    internal enum AERISTerrainWorkOwner
    {
        FlightFallback = 0,
        PreloadBuilder = 1
    }

    internal enum AERISTerrainGenerationState
    {
        Missing = 0,
        Partial = 1,
        Complete = 2,
        Invalid = 3
    }

    internal enum AERISTerrainCodecId
    {
        Raw = 0,
        Deflate = 1
    }

    internal static class AERISTerrainPreloadFormat
    {
        internal const int DatabaseFormatVersion = 3;
        internal const int CodecVersion = 1;
        internal const int ChunkEdgeTiles = 8;
        internal const string ManifestMagic = "AERIS_PRELOAD_TERRAIN_MANIFEST_V3";
        internal const string ChunkMagic = "AERIS_PRELOAD_TERRAIN_CHUNK_V3";
        internal const string StateMagic = "AERIS_PRELOAD_TERRAIN_STATE_V2";
    }

    internal sealed class AERISTerrainPreloadEncodedTile
    {
        internal AERISTerrainTileKey Key;
        internal int Resolution;
        internal double SouthLatitudeDeg;
        internal double NorthLatitudeDeg;
        internal double WestLongitudeDeg;
        internal double EastLongitudeDeg;
        internal float MinimumElevationMeters;
        internal float MaximumElevationMeters;
        internal float HeightOffset;
        internal float HeightScale;
        internal int Quality;
        internal AERISTerrainGenerationState GenerationState;
        internal long GenerationUtcTicks;
        internal long LastAccessUtcTicks;
        internal string PqsConfigurationHash = string.Empty;
        internal string GameDataHash = string.Empty;
        internal long TerrainGenerationId;
        internal AERISTerrainCodecId CodecId;
        internal int CodecVersion;
        internal int UncompressedSize;
        internal byte[] CompressedPayload;
        internal uint PayloadCrc;
        internal bool WaterOnly;
        internal bool ConstantHeight;
        internal bool FlatTile;

        internal long EstimatedBytes
        {
            get { return 256L + (CompressedPayload == null ? 0L : CompressedPayload.LongLength); }
        }

        internal AERISTerrainPreloadEncodedTile CloneImmutable()
        {
            return new AERISTerrainPreloadEncodedTile
            {
                Key = Key,
                Resolution = Resolution,
                SouthLatitudeDeg = SouthLatitudeDeg,
                NorthLatitudeDeg = NorthLatitudeDeg,
                WestLongitudeDeg = WestLongitudeDeg,
                EastLongitudeDeg = EastLongitudeDeg,
                MinimumElevationMeters = MinimumElevationMeters,
                MaximumElevationMeters = MaximumElevationMeters,
                HeightOffset = HeightOffset,
                HeightScale = HeightScale,
                Quality = Quality,
                GenerationState = GenerationState,
                GenerationUtcTicks = GenerationUtcTicks,
                LastAccessUtcTicks = LastAccessUtcTicks,
                PqsConfigurationHash = PqsConfigurationHash ?? string.Empty,
                GameDataHash = GameDataHash ?? string.Empty,
                TerrainGenerationId = TerrainGenerationId,
                CodecId = CodecId,
                CodecVersion = CodecVersion,
                UncompressedSize = UncompressedSize,
                CompressedPayload = CompressedPayload == null ? null :
                    (byte[])CompressedPayload.Clone(),
                PayloadCrc = PayloadCrc,
                WaterOnly = WaterOnly,
                ConstantHeight = ConstantHeight,
                FlatTile = FlatTile
            };
        }
    }


    internal sealed class AERISTerrainPreloadPoint
    {
        internal string BodyName = string.Empty;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal AERISTerrainTileLod MaximumLod = AERISTerrainTileLod.Local;
        internal int Priority;
        internal string Reason = string.Empty;
    }

    internal sealed class AERISTerrainPreloadBodyStatus
    {
        internal string BodyName = string.Empty;
        internal AERISTerrainBodyPriority Priority;
        internal AERISTerrainTileLod QualityLimit;
        internal long StorageBytes;
        internal long StorageLimitBytes;
        internal int CompleteTiles;
        internal int PendingTiles;
        internal int InvalidTiles;
        internal double CoverageRatio;
        internal string Status = string.Empty;
        internal bool Pinned;
        internal bool Supported;
    }

    internal sealed class AERISTerrainPreloadStatusSnapshot
    {
        internal AERISTerrainPreloadMode Mode;
        internal AERISTerrainPreloadSpeedProfile SpeedProfile;
        internal bool FlightSuspended;
        internal bool Paused;
        internal bool Idle;
        // CP2.5 exposes one validated non-Flight preload throughput envelope.
        internal bool StandardThroughputActive;
        internal int PipelineActiveTileLimit;
        internal int PipelinePendingBlockLimit;
        internal int PipelineOutstandingBlocks;
        internal int PipelineOutstandingBlockLimit;
        internal long PipelineAdmissionBackpressure;
        internal long PipelineRecoveryCount;
        internal int PipelineLastRecoveryTiles;
        internal string PipelineLastRecoveryReason = string.Empty;
        internal int SchedulerResultDepth;
        internal int SchedulerRequiredResultDepth;
        internal long SchedulerRequiredRejected;
        internal long SchedulerRequiredDropped;
        internal int EncodeQueueDepth;
        internal int EncodeActive;
        internal int EncodeCommitLimit;
        internal long EncodeAdmissionBackpressure;
        internal int WriteQueueDepth;
        internal int WriteActive;
        internal int WriteCommitLimit;
        internal long WriteAdmissionBackpressure;
        internal int WriteBatchChunks;
        internal int WriteBatchTiles;
        internal string Bottleneck = string.Empty;
        internal string GpuPreloadStage = string.Empty;
        internal string ActiveBody = string.Empty;
        internal AERISTerrainTileLod ActiveLod;
        internal string Status = string.Empty;
        internal long StorageBytes;
        internal long StorageLimitBytes;
        internal int TilesComplete;
        internal int TilesPending;
        internal int BuilderQueueDepth;
        internal double BuilderPqsMilliseconds;
        internal double BuilderWorkerUtilization;
        internal double BuilderWriteMbps;
        internal double CompressionRatio;
        internal double BuilderPqsSamplesPerSecond;
        internal double BuilderPqsSampleCostMilliseconds;
        internal long BuilderPqsSampleCacheHits;
        internal long BuilderPqsSampleCacheMisses;
        internal double BuilderPqsSampleCacheHitRatio;
        internal double ChunkBatchTiles;
        internal double ChunkRewriteAmplification;
        internal double ChunkFlushMilliseconds;
        internal long IntermediateCommitsSkipped;
        internal double ParsedChunkCacheHitRatio;
        internal AERISTerrainPreloadBodyStatus[] Bodies =
            new AERISTerrainPreloadBodyStatus[0];
    }

    internal sealed class AERISTerrainPreloadTelemetry
    {
        internal string BuilderBody = string.Empty;
        internal int BuilderLod;
        internal long BuilderTilesComplete;
        internal int BuilderTilesPending;
        internal double BuilderPqsMilliseconds;
        internal double BuilderWorkerUtilization;
        internal double BuilderWriteMbps;
        internal double BuilderCompressionRatio;
        internal long BuilderStorageBytes;
        internal double BuilderPqsSamplesPerSecond;
        internal double BuilderPqsSampleCostMilliseconds;
        internal long BuilderPqsSampleCacheHits;
        internal long BuilderPqsSampleCacheMisses;
        internal double BuilderPqsSampleCacheHitRatio;
        internal double BuilderChunkBatchTiles;
        internal double BuilderChunkRewriteAmplification;
        internal double BuilderChunkFlushMilliseconds;
        internal long BuilderIntermediateCommitsSkipped;
        internal int BuilderEncodeQueueDepth;
        internal int BuilderEncodeActive;
        internal int BuilderEncodeCommitLimit;
        internal long BuilderEncodeAdmissionBackpressure;
        internal int BuilderWriteQueueDepth;
        internal int BuilderWriteActive;
        internal int BuilderWriteCommitLimit;
        internal long BuilderWriteAdmissionBackpressure;
        internal int BuilderWriteBatchChunks;
        internal int BuilderWriteBatchTiles;
        internal string BuilderBottleneck = string.Empty;
        internal string BuilderGpuPreloadStage = string.Empty;
        internal int BuilderOutstandingBlocks;
        internal int BuilderOutstandingBlockLimit;
        internal long BuilderAdmissionBackpressure;
        internal long BuilderRecoveryCount;
        internal long BuilderSchedulerRequiredRejected;
        internal long BuilderSchedulerRequiredDropped;

        internal long DatabaseReadRequests;
        internal double DatabaseReadLatencyMilliseconds;
        internal double DatabaseReadMbps;
        internal int DatabaseReadQueueDepth;
        internal double DatabaseCacheHitRatio;
        internal long DatabaseCoalescedReads;
        internal long DatabaseCrcFailures;
        internal long DatabaseHashMismatches;
        internal long DatabaseParsedChunkCacheHits;
        internal long DatabaseParsedChunkCacheMisses;
        internal double DatabaseParsedChunkCacheHitRatio;

        internal double DecompressQueueDelayMilliseconds;
        internal double DecompressTimeMilliseconds;
        internal double DecompressMbps;
        internal int DecompressWorkerActive;
        internal long DecompressFailures;

        internal double FirstTileVisibleMilliseconds;
        internal double ViewportCoverageRatio;
        internal double ResultAgeMilliseconds;
        internal long StaleResultsDiscarded;
        internal long GenerationFallbackCount;
    }

    internal static class AERISTerrainSpatialKey
    {
        internal static int ChunkCoordinate(int tileIndex)
        {
            int size = AERISTerrainPreloadFormat.ChunkEdgeTiles;
            if (tileIndex >= 0) return tileIndex / size;
            return -(((-tileIndex) + size - 1) / size);
        }

        internal static ulong Morton(int x, int y)
        {
            uint ux = unchecked((uint)(x ^ int.MinValue));
            uint uy = unchecked((uint)(y ^ int.MinValue));
            return Part1By1(ux) | (Part1By1(uy) << 1);
        }

        static ulong Part1By1(uint value)
        {
            ulong x = value;
            x = (x | (x << 16)) & 0x0000FFFF0000FFFFUL;
            x = (x | (x << 8)) & 0x00FF00FF00FF00FFUL;
            x = (x | (x << 4)) & 0x0F0F0F0F0F0F0F0FUL;
            x = (x | (x << 2)) & 0x3333333333333333UL;
            x = (x | (x << 1)) & 0x5555555555555555UL;
            return x;
        }
    }
}
