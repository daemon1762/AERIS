using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using AERISFlightControl.Recording;
using AERISFlightControl.Terrain;

namespace AERISFlightControl.Performance
{
    internal sealed class AERISPerformanceRuntime : IDisposable
    {
        readonly AERISGenerationRegistry generations;
        readonly AERISActivePermitController permits;
        readonly AERISWorkerScheduler scheduler;
        readonly AERISGpuAssistBackend gpu;
        readonly AERISInstrumentPipeline instruments;
        readonly AERISNavigationDisplayPipeline navigationDisplay;
        readonly AERISNavigationTrafficPipeline navigationTraffic;
        readonly AERISAsyncFileChannel telemetryWriter;
        AERISRuntimeTelemetrySnapshot runtimeTelemetry;
        AERISFileWriterTelemetrySnapshot writerTelemetry;
        double nextPolicyEvaluation;
        double nextTelemetryWrite;
        double frameMillisecondsEma;
        double mainMillisecondsEma;
        double snapshotCaptureMillisecondsEma;
        double commitDrainMillisecondsEma;
        double frameMillisecondsP95;
        double mainMillisecondsP95;
        double snapshotCaptureMillisecondsP95;
        double commitDrainMillisecondsP95;
        double gcBytesPerFrameEma;
        double ndLayoutMillisecondsEma;
        double ndRepaintMillisecondsEma;
        double ndCaptureMillisecondsEma;
        double ndTextureUploadMillisecondsEma;
        double ndGcAllocationBytesEma;
        int ndCapturedFacilityCount;
        int ndCapturedRunwayCount;
        int ndPlanMode;
        double ndRangeMeters;
        long terrainTileRamBytes;
        long terrainTileRamLimitBytes;
        long terrainTileDiskBytes;
        long terrainTileDiskLimitBytes;
        int terrainTileRamCount;
        int terrainTileDiskCount;
        long terrainTileRamHits;
        long terrainTileDiskHits;
        long terrainTileMisses;
        long terrainTileReused;
        long terrainTileGenerated;
        long terrainTileDiskWrites;
        long terrainTileDiskFailures;
        long terrainTileStaleCancelled;
        long terrainTileDroppedRequests;
        long terrainTilePreviewGenerated;
        long terrainTileFinalGenerated;
        long terrainTileObsoleteCancelled;
        double terrainTileHitRate;
        int terrainTilePending;
        int terrainTileDesired;
        int terrainTileVisible;
        int terrainTilePreviewCount;
        int terrainTileSamplingRemaining;
        int terrainTileSampleBatch;
        double terrainTileSampleBatchMs;
        double terrainTilePqsSampleEmaMs;
        long terrainGpuBytes;
        int terrainGpuTextures;
        int terrainGpuPending;
        int terrainGpuFailures;
        double terrainGpuCoverage;
        double terrainMeshWorkerEmaMs;
        double terrainContourWorkerEmaMs;
        long terrainTileWarmBytes;
        int terrainTileWarmCount;
        string preloadBuilderBody = string.Empty;
        int preloadBuilderLod;
        long preloadBuilderTilesComplete;
        int preloadBuilderTilesPending;
        double preloadBuilderPqsMs;
        double preloadBuilderWorkerUtilization;
        double preloadBuilderWriteMbps;
        double preloadBuilderCompressionRatio;
        long preloadBuilderStorageBytes;
        double preloadBuilderPqsSamplesPerSecond;
        double preloadBuilderPqsSampleCostMs;
        long preloadBuilderPqsSampleCacheHits;
        long preloadBuilderPqsSampleCacheMisses;
        double preloadBuilderPqsSampleCacheHitRatio;
        double preloadBuilderChunkBatchTiles;
        double preloadBuilderChunkRewriteAmplification;
        double preloadBuilderChunkFlushMs;
        long preloadBuilderIntermediateCommitsSkipped;
        long terrainDbReadRequests;
        double terrainDbReadLatencyMs;
        double terrainDbReadMbps;
        int terrainDbReadQueueDepth;
        double terrainDbCacheHitRatio;
        long terrainDbCoalescedReads;
        long terrainDbCrcFailures;
        long terrainDbHashMismatches;
        long terrainDbParsedChunkCacheHits;
        long terrainDbParsedChunkCacheMisses;
        double terrainDbParsedChunkCacheHitRatio;
        double terrainDecompressQueueDelayMs;
        double terrainDecompressTimeMs;
        double terrainDecompressMbps;
        int terrainDecompressWorkerActive;
        long terrainDecompressFailures;
        double terrainFirstTileVisibleMs;
        double terrainViewportCoverageRatio;
        double terrainPreloadResultAgeMs;
        long terrainStaleResultsDiscarded;
        long terrainGenerationFallbackCount;
        int cp3ResidentActive;
        string cp3ResidentBody = string.Empty;
        long cp3ResidentRamBytes;
        long cp3ResidentRamBudgetBytes;
        int cp3ResidentEntryCount;
        int cp3ResidentRamCount;
        int cp3ResidentGlobalCount;
        int cp3ResidentFarCount;
        int cp3ResidentRouteCount;
        int cp3ResidentLocalCount;
        int cp3ResidentLandCount;
        int cp3ResidentPinnedCount;
        long cp3ResidentCacheHits;
        long cp3ResidentCacheMisses;
        long cp3ResidentDecodeSubmissions;
        long cp3ResidentDecodeSuccesses;
        long cp3ResidentDecodeFailures;
        long cp3ResidentStaleRejects;
        long cp3ResidentBudgetRejects;
        long cp3ResidentEvictions;
        int cp3CorridorActive;
        double cp3CorridorGroundSpeedMps;
        double cp3CorridorTurnRateDegPerSecond;
        double cp3CorridorLookAheadSeconds;
        double cp3CorridorLookAheadDistanceMeters;
        double cp3CorridorHalfWidthMeters;
        int cp3CorridorPointCount;
        int cp3CorridorRequestedTiles;
        int cp3CorridorPinnedTiles;
        int cp3CorridorLandDemand;
        string cp3CorridorStatus = string.Empty;
        int terrainContinuitySeeded;
        long terrainContinuityReuseFrames;
        long terrainUnknownBackingFrames;
        double terrainContinuityAgeMs;
        readonly double[] frameSamples = new double[120];
        readonly double[] mainSamples = new double[120];
        readonly double[] snapshotCaptureSamples = new double[120];
        readonly double[] commitDrainSamples = new double[120];
        int frameSampleIndex, frameSampleCount;
        int mainSampleIndex, mainSampleCount;
        int snapshotCaptureSampleIndex, snapshotCaptureSampleCount;
        int commitDrainSampleIndex, commitDrainSampleCount;
        long previousGcBytes;
        bool disposed;
        static AERISPerformanceRuntime current;

        internal AERISPerformanceRuntime(int configuredWorkers = 0, bool disableGpu = false)
        {
            generations = new AERISGenerationRegistry();
            permits = new AERISActivePermitController();
            scheduler = new AERISWorkerScheduler(generations, permits, configuredWorkers);
            gpu = new AERISGpuAssistBackend();
            instruments = new AERISInstrumentPipeline(scheduler);
            navigationDisplay = new AERISNavigationDisplayPipeline(scheduler);
            navigationTraffic = new AERISNavigationTrafficPipeline(scheduler);
            runtimeTelemetry = scheduler.SnapshotTelemetry();
            writerTelemetry = AERISBackgroundFileWriter.SnapshotTelemetry();
            if (disableGpu) gpu.DisableAndFallback("GPU OFF acceptance mode");
            string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData",
                "AERISFlightControl", "Logs", "Sessions",
                DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss_fff") +
                "_performance_runtime.csv");
            telemetryWriter = new AERISAsyncFileChannel(path, false,
                AERISFileRecordPriority.Continuous);
            telemetryWriter.WriteHeader(new string[] {
                "utc","runtime_s","logical_processors","configured_workers",
                "active_permits","active_total","active_safety","active_general",
                "active_telemetry","active_archive","queue_safety","queue_general",
                "queue_telemetry","queue_archive","queue_delay_p50_ms",
                "queue_delay_p95_ms","queue_delay_max_ms","worker_p95_ms",
                "commit_p95_ms","runway_worker_p95_ms","terrain_worker_p95_ms",
                "instrument_worker_p95_ms","navigation_display_worker_p95_ms",
                "runway_result_age_ms","terrain_result_age_ms",
                "instrument_result_age_ms","navigation_display_result_age_ms",
                "nd_layout_ema_ms","nd_repaint_ema_ms","nd_capture_ema_ms",
                "nd_texture_upload_ema_ms","nd_gc_positive_delta_ema_bytes",
                "nd_facilities","nd_runways","nd_plan","nd_range_m",
                "terrain_tile_ram_bytes","terrain_tile_ram_limit_bytes",
                "terrain_tile_disk_bytes","terrain_tile_disk_limit_bytes",
                "terrain_tile_ram_count","terrain_tile_disk_count",
                "terrain_tile_ram_hits","terrain_tile_disk_hits","terrain_tile_misses",
                "terrain_tile_reused","terrain_tile_generated","terrain_tile_disk_writes",
                "terrain_tile_disk_failures","terrain_tile_stale_cancelled",
                "terrain_tile_dropped_requests","terrain_tile_preview_generated",
                "terrain_tile_final_generated","terrain_tile_obsolete_cancelled",
                "terrain_tile_hit_rate","terrain_tile_pending","terrain_tile_desired",
                "terrain_tile_visible","terrain_tile_preview_count",
                "terrain_tile_sampling_remaining","terrain_tile_sample_batch",
                "terrain_tile_sample_batch_ms","terrain_tile_pqs_sample_ema_ms",
                "terrain_gpu_bytes","terrain_gpu_textures","terrain_gpu_pending",
                "terrain_gpu_failures","terrain_gpu_coverage",
                "terrain_mesh_worker_ema_ms",
                "terrain_contour_worker_ema_ms",
                "terrain_tile_warm_bytes","terrain_tile_warm_count",
                "preload_builder_body","preload_builder_lod",
                "preload_builder_tiles_complete","preload_builder_tiles_pending",
                "preload_builder_pqs_ms","preload_builder_worker_utilization",
                "preload_builder_write_mbps","preload_builder_compression_ratio",
                "preload_builder_storage_bytes",
                "preload_builder_pqs_samples_per_sec",
                "preload_builder_pqs_sample_cost_ms",
                "preload_builder_pqs_sample_cache_hits",
                "preload_builder_pqs_sample_cache_misses",
                "preload_builder_pqs_sample_cache_hit_ratio",
                "preload_builder_chunk_batch_tiles",
                "preload_builder_chunk_rewrite_amplification",
                "preload_builder_chunk_flush_ms",
                "preload_builder_intermediate_commits_skipped",
                "terrain_db_read_requests","terrain_db_read_latency_ms",
                "terrain_db_read_mbps","terrain_db_read_queue_depth",
                "terrain_db_cache_hit_ratio","terrain_db_coalesced_reads",
                "terrain_db_crc_failures","terrain_db_hash_mismatches",
                "terrain_db_parsed_chunk_cache_hits",
                "terrain_db_parsed_chunk_cache_misses",
                "terrain_db_parsed_chunk_cache_hit_ratio",
                "terrain_decompress_queue_delay_ms",
                "terrain_decompress_time_ms","terrain_decompress_mbps",
                "terrain_decompress_worker_active","terrain_decompress_failures",
                "terrain_first_tile_visible_ms","terrain_viewport_coverage_ratio",
                "terrain_preload_result_age_ms","terrain_stale_results_discarded",
                "terrain_generation_fallback_count",
                "cp3_resident_active","cp3_resident_body",
                "cp3_resident_ram_bytes","cp3_resident_ram_budget_bytes",
                "cp3_resident_entries","cp3_resident_ram_count",
                "cp3_resident_global_count","cp3_resident_far_count",
                "cp3_resident_route_count","cp3_resident_local_count",
                "cp3_resident_land_count","cp3_resident_pinned_count",
                "cp3_resident_cache_hits","cp3_resident_cache_misses",
                "cp3_resident_decode_submissions","cp3_resident_decode_successes",
                "cp3_resident_decode_failures","cp3_resident_stale_rejects",
                "cp3_resident_budget_rejects","cp3_resident_evictions",
                "cp3_corridor_active","cp3_corridor_ground_speed_mps",
                "cp3_corridor_turn_rate_deg_s","cp3_corridor_lookahead_s",
                "cp3_corridor_distance_m","cp3_corridor_half_width_m",
                "cp3_corridor_points","cp3_corridor_requested_tiles",
                "cp3_corridor_pinned_tiles","cp3_corridor_land_demand",
                "cp3_corridor_status","terrain_continuity_seeded",
                "terrain_continuity_reuse_frames","terrain_unknown_backing_frames",
                "terrain_continuity_age_ms",
                "submitted","completed","replaced","dropped",
                "cancelled","stale","failed","frame_ema_ms","aeris_main_ema_ms",
                "snapshot_capture_ema_ms","commit_drain_ema_ms","frame_p95_ms",
                "aeris_main_p95_ms","snapshot_capture_p95_ms","commit_drain_p95_ms",
                "gpu_active","gpu_backend","gpu_frame_ms","writer_depth",
                "writer_capacity","writer_written","writer_drop_critical",
                "writer_drop_continuous","writer_drop_verbose","writer_drop_first_seq",
                "writer_drop_last_seq","writer_failures","writer_batch_p95_ms",
                "writer_encode_p95_ms","writer_disk_p95_ms","writer_flush_p95_ms","archive_pending",
                "archive_completed","archive_failed","archive_deferred",
                "archive_last_ms","archive_max_ms","gc_positive_delta_ema_bytes",
                "degradation_reason"
            });
            Volatile.Write(ref current, this);
            AERISFlightDataArchive.NotifyRuntimeAvailable(this);
        }

        internal static AERISPerformanceRuntime Current
        {
            get { return Volatile.Read(ref current); }
        }
        internal AERISWorkerScheduler Scheduler { get { return scheduler; } }
        internal AERISGpuAssistBackend Gpu { get { return gpu; } }
        internal AERISRuntimeGenerationStamp CaptureStamp()
        {
            return generations.Capture(Stopwatch.GetTimestamp());
        }

        internal void SceneChanged()
        {
            generations.SceneChanged();
            ClearPublishedSnapshots();
        }
        internal void VesselChanged(long persistentId, long bodyId)
        {
            generations.VesselChanged(persistentId, bodyId);
            ClearPublishedSnapshots();
        }
        internal void ControlPointChanged()
        {
            generations.ControlPointChanged(); ClearPublishedSnapshots();
        }
        internal void DockingChanged()
        {
            generations.DockingChanged(); ClearPublishedSnapshots();
        }
        internal void FlightPlanChanged()
        {
            generations.FlightPlanChanged(); ClearPublishedSnapshots();
        }
        internal void DisplayLayoutChanged()
        {
            generations.DisplayLayoutChanged(); ClearPublishedSnapshots();
        }
        internal void UpdateRunwayRevisions(long database, long selection)
        {
            generations.UpdateRunway(database, selection);
            ClearPublishedSnapshots();
        }

        static void ClearPublishedSnapshots()
        {
            AERISCanonicalSnapshotApi.Clear();
            AERISPreparedDisplayFrameApi.Clear();
            AERISPreparedNavigationFrameApi.Clear();
            AERISPreparedTrafficFrameApi.Clear();
            AERISTrafficAlertApi.Clear();
        }

        internal void Tick(double now, double frameMilliseconds,
            double snapshotCaptureMilliseconds, bool landActive, bool inFlight)
        {
            Stopwatch commitWatch = Stopwatch.StartNew();
            scheduler.DrainCommits(24);
            commitWatch.Stop();
            double commitDrainMilliseconds = commitWatch.Elapsed.TotalMilliseconds;
            bool capturedThisFrame = IsFinite(snapshotCaptureMilliseconds) &&
                snapshotCaptureMilliseconds >= 0.0;
            double measuredMainMilliseconds =
                (capturedThisFrame ? snapshotCaptureMilliseconds : 0.0) +
                commitDrainMilliseconds;
            // Operation Health PENICILLIN: passive observation only. This hook reads
            // already-computed timing values and never changes AA/AP/FBW scheduling.
            AERISOperationHealthPenicillin.RecordRuntimeFrame(
                frameMilliseconds, measuredMainMilliseconds, commitDrainMilliseconds);
            if (IsFinite(frameMilliseconds) && frameMilliseconds >= 0.0)
            {
                frameMillisecondsEma = Ema(frameMillisecondsEma, frameMilliseconds, 0.10);
                RecordSample(frameSamples, ref frameSampleIndex, ref frameSampleCount,
                    frameMilliseconds);
            }
            if (IsFinite(measuredMainMilliseconds))
            {
                mainMillisecondsEma = Ema(mainMillisecondsEma,
                    measuredMainMilliseconds, 0.10);
                RecordSample(mainSamples, ref mainSampleIndex, ref mainSampleCount,
                    measuredMainMilliseconds);
            }
            if (capturedThisFrame)
            {
                snapshotCaptureMillisecondsEma = Ema(snapshotCaptureMillisecondsEma,
                    snapshotCaptureMilliseconds, 0.10);
                RecordSample(snapshotCaptureSamples, ref snapshotCaptureSampleIndex,
                    ref snapshotCaptureSampleCount, snapshotCaptureMilliseconds);
            }
            if (IsFinite(commitDrainMilliseconds) && commitDrainMilliseconds >= 0.0)
            {
                commitDrainMillisecondsEma = Ema(commitDrainMillisecondsEma,
                    commitDrainMilliseconds, 0.10);
                RecordSample(commitDrainSamples, ref commitDrainSampleIndex,
                    ref commitDrainSampleCount, commitDrainMilliseconds);
            }
            if (now >= nextPolicyEvaluation)
            {
                nextPolicyEvaluation = now + 1.0;
                frameMillisecondsP95 = SamplePercentile(frameSamples,
                    frameSampleCount, 0.95);
                mainMillisecondsP95 = SamplePercentile(mainSamples,
                    mainSampleCount, 0.95);
                snapshotCaptureMillisecondsP95 = SamplePercentile(snapshotCaptureSamples,
                    snapshotCaptureSampleCount, 0.95);
                commitDrainMillisecondsP95 = SamplePercentile(commitDrainSamples,
                    commitDrainSampleCount, 0.95);
                writerTelemetry = AERISBackgroundFileWriter.SnapshotTelemetry();
                double writerBacklog = writerTelemetry.Capacity <= 0 ? 0.0 :
                    (double)writerTelemetry.QueueDepth / writerTelemetry.Capacity;
                bool archiveDrainPreferred = !inFlight &&
                    AERISFlightDataArchive.PendingCount > 0;
                permits.Observe(now, frameMillisecondsP95, mainMillisecondsP95,
                    scheduler.OldestSafetyDelayMilliseconds, writerBacklog,
                    gpu.FrameMilliseconds, landActive, archiveDrainPreferred);
                runtimeTelemetry = scheduler.SnapshotTelemetry();
                long gcBytes = GC.GetTotalMemory(false);
                if (previousGcBytes > 0L && gcBytes > previousGcBytes)
                    gcBytesPerFrameEma = Ema(gcBytesPerFrameEma,
                        gcBytes - previousGcBytes, 0.10);
                previousGcBytes = gcBytes;
            }
            if (now >= nextTelemetryWrite)
            {
                nextTelemetryWrite = now + 1.0;
                WriteTelemetry(now);
            }
        }

        internal void Publish(AERISCanonicalSnapshot snapshot)
        {
            if (disposed || snapshot == null) return;
            AERISCanonicalSnapshotApi.Publish(snapshot);
            instruments.Submit(snapshot);
        }


        internal bool SubmitNavigationDisplay(AERISNavigationDisplaySnapshot snapshot)
        {
            return !disposed && navigationDisplay != null &&
                navigationDisplay.Submit(snapshot);
        }

        internal bool SubmitNavigationTraffic(AERISNavigationTrafficSnapshot snapshot)
        {
            return !disposed && navigationTraffic != null &&
                navigationTraffic.Submit(snapshot);
        }

        internal void RecordNavigationDisplayEvent(bool repaint, double milliseconds,
            long positiveGcAllocationBytes)
        {
            if (!IsFinite(milliseconds) || milliseconds < 0.0 || milliseconds > 1000.0)
                return;
            if (repaint)
                ndRepaintMillisecondsEma = Ema(ndRepaintMillisecondsEma,
                    milliseconds, 0.12);
            else
                ndLayoutMillisecondsEma = Ema(ndLayoutMillisecondsEma,
                    milliseconds, 0.12);
            if (positiveGcAllocationBytes > 0L)
                ndGcAllocationBytesEma = Ema(ndGcAllocationBytesEma,
                    positiveGcAllocationBytes, 0.10);
        }

        internal void RecordNavigationDisplayCapture(double milliseconds,
            int facilityCount, int runwayCount)
        {
            if (IsFinite(milliseconds) && milliseconds >= 0.0 && milliseconds <= 1000.0)
                ndCaptureMillisecondsEma = Ema(ndCaptureMillisecondsEma,
                    milliseconds, 0.12);
            ndCapturedFacilityCount = Math.Max(0, facilityCount);
            ndCapturedRunwayCount = Math.Max(0, runwayCount);
        }

        internal void RecordNavigationDisplayTextureUpload(double milliseconds)
        {
            if (IsFinite(milliseconds) && milliseconds >= 0.0 && milliseconds <= 1000.0)
                ndTextureUploadMillisecondsEma = Ema(ndTextureUploadMillisecondsEma,
                    milliseconds, 0.12);
        }

        internal void RecordNavigationDisplayState(bool planMode, double rangeMeters)
        {
            ndPlanMode = planMode ? 1 : 0;
            ndRangeMeters = IsFinite(rangeMeters) ? Math.Max(0.0, rangeMeters) : 0.0;
        }

        internal void RecordTerrainPreloadState(AERISTerrainPreloadTelemetry preload,
            long warmBytes, int warmCount)
        {
            terrainTileWarmBytes = Math.Max(0L, warmBytes);
            terrainTileWarmCount = Math.Max(0, warmCount);
            ApplyTerrainPreloadTelemetry(preload);
        }

        internal void RecordTerrainTileState(AERISTerrainTileCacheTelemetry value,
            long gpuBytes, int gpuTextures, int gpuPending, int gpuFailures,
            double gpuCoverage, double tilePqsSampleEmaMs,
            double meshWorkerEmaMs, double contourWorkerEmaMs)
        {
            if (value != null)
            {
                terrainTileRamBytes = Math.Max(0L, value.RamBytes);
                terrainTileRamLimitBytes = Math.Max(0L, value.RamLimitBytes);
                terrainTileDiskBytes = Math.Max(0L, value.DiskBytes);
                terrainTileDiskLimitBytes = Math.Max(0L, value.DiskLimitBytes);
                terrainTileRamCount = Math.Max(0, value.RamTileCount);
                terrainTileDiskCount = Math.Max(0, value.DiskTileCount);
                terrainTileRamHits = Math.Max(0L, value.RamHits);
                terrainTileDiskHits = Math.Max(0L, value.DiskHits);
                terrainTileMisses = Math.Max(0L, value.Misses);
                terrainTileReused = Math.Max(0L, value.Reused);
                terrainTileGenerated = Math.Max(0L, value.Generated);
                terrainTileDiskWrites = Math.Max(0L, value.DiskWrites);
                terrainTileDiskFailures = Math.Max(0L, value.DiskFailures);
                terrainTileStaleCancelled = Math.Max(0L, value.StaleCancelled);
                terrainTileDroppedRequests = Math.Max(0L, value.DroppedRequests);
                terrainTilePreviewGenerated = Math.Max(0L, value.PreviewGenerated);
                terrainTileFinalGenerated = Math.Max(0L, value.FinalGenerated);
                terrainTileObsoleteCancelled = Math.Max(0L, value.ObsoleteCancelled);
                terrainTileHitRate = IsFinite(value.HitRate) ?
                    Math.Max(0.0, Math.Min(1.0, value.HitRate)) : 0.0;
                terrainTilePending = Math.Max(0, value.PendingRequests);
                terrainTileDesired = Math.Max(0, value.DesiredRequests);
                terrainTileVisible = Math.Max(0, value.VisibleRequests);
                terrainTilePreviewCount = Math.Max(0, value.PreviewTileCount);
                terrainTileSamplingRemaining = Math.Max(0, value.SamplingRemaining);
                terrainTileSampleBatch = Math.Max(0, value.LastSamplingBatchSamples);
                terrainTileSampleBatchMs = IsFinite(value.LastSamplingBatchMilliseconds) ?
                    Math.Max(0.0, value.LastSamplingBatchMilliseconds) : 0.0;
                terrainTileWarmBytes = Math.Max(0L, value.WarmBytes);
                terrainTileWarmCount = Math.Max(0, value.WarmTileCount);
                ApplyTerrainPreloadTelemetry(value.Preload);
            }
            terrainGpuBytes = Math.Max(0L, gpuBytes);
            terrainGpuTextures = Math.Max(0, gpuTextures);
            terrainGpuPending = Math.Max(0, gpuPending);
            terrainGpuFailures = Math.Max(0, gpuFailures);
            terrainGpuCoverage = IsFinite(gpuCoverage) ?
                Math.Max(0.0, Math.Min(1.0, gpuCoverage)) : 0.0;
            terrainTilePqsSampleEmaMs = IsFinite(tilePqsSampleEmaMs) ?
                Math.Max(0.0, tilePqsSampleEmaMs) : 0.0;
            terrainMeshWorkerEmaMs = IsFinite(meshWorkerEmaMs) ?
                Math.Max(0.0, meshWorkerEmaMs) : 0.0;
            terrainContourWorkerEmaMs = IsFinite(contourWorkerEmaMs) ?
                Math.Max(0.0, contourWorkerEmaMs) : 0.0;
        }

        internal void RecordCp3ResidentCorridorState(
            AERISCurrentBodyResidentTelemetrySnapshot resident,
            AERISPredictiveForwardCorridorSnapshot corridor)
        {
            if (resident != null)
            {
                cp3ResidentActive = resident.Active ? 1 : 0;
                cp3ResidentBody = resident.ActiveBody ?? string.Empty;
                cp3ResidentRamBytes = Math.Max(0L, resident.RamBytes);
                cp3ResidentRamBudgetBytes = Math.Max(0L, resident.RamBudgetBytes);
                cp3ResidentEntryCount = Math.Max(0, resident.EntryCount);
                cp3ResidentRamCount = Math.Max(0, resident.RamResidentCount);
                cp3ResidentGlobalCount = Math.Max(0, resident.GlobalCount);
                cp3ResidentFarCount = Math.Max(0, resident.FarCount);
                cp3ResidentRouteCount = Math.Max(0, resident.RouteCount);
                cp3ResidentLocalCount = Math.Max(0, resident.LocalCount);
                cp3ResidentLandCount = Math.Max(0, resident.LandCount);
                cp3ResidentPinnedCount = Math.Max(0, resident.PinnedEntryCount);
                cp3ResidentCacheHits = Math.Max(0L, resident.CacheHits);
                cp3ResidentCacheMisses = Math.Max(0L, resident.CacheMisses);
                cp3ResidentDecodeSubmissions = Math.Max(0L,
                    resident.AsyncDecodeSubmissions);
                cp3ResidentDecodeSuccesses = Math.Max(0L,
                    resident.AsyncDecodeSuccesses);
                cp3ResidentDecodeFailures = Math.Max(0L,
                    resident.AsyncDecodeFailures);
                cp3ResidentStaleRejects = Math.Max(0L, resident.StaleCommitRejects);
                cp3ResidentBudgetRejects = Math.Max(0L, resident.BudgetRejects);
                cp3ResidentEvictions = Math.Max(0L, resident.Evictions);
            }
            if (corridor != null)
            {
                cp3CorridorActive = corridor.Active ? 1 : 0;
                cp3CorridorGroundSpeedMps = SafeNonNegative(
                    corridor.GroundSpeedMetersPerSecond);
                cp3CorridorTurnRateDegPerSecond = IsFinite(
                    corridor.TurnRateDegPerSecond) ?
                    corridor.TurnRateDegPerSecond : 0.0;
                cp3CorridorLookAheadSeconds = SafeNonNegative(
                    corridor.LookAheadSeconds);
                cp3CorridorLookAheadDistanceMeters = SafeNonNegative(
                    corridor.LookAheadDistanceMeters);
                cp3CorridorHalfWidthMeters = SafeNonNegative(
                    corridor.MaximumHalfWidthMeters);
                cp3CorridorPointCount = Math.Max(0, corridor.TotalPoints);
                cp3CorridorRequestedTiles = Math.Max(0, corridor.RequestedTiles);
                cp3CorridorPinnedTiles = Math.Max(0, corridor.PinnedTiles);
                cp3CorridorLandDemand = corridor.LandDemandActive ? 1 : 0;
                cp3CorridorStatus = corridor.Status ?? string.Empty;
            }
        }

        internal void RecordTerrainContinuityState(bool seeded,
            long reuseFrames, long unknownBackingFrames, double ageMilliseconds)
        {
            terrainContinuitySeeded = seeded ? 1 : 0;
            terrainContinuityReuseFrames = Math.Max(0L, reuseFrames);
            terrainUnknownBackingFrames = Math.Max(0L, unknownBackingFrames);
            terrainContinuityAgeMs = SafeNonNegative(ageMilliseconds);
        }

        void ApplyTerrainPreloadTelemetry(AERISTerrainPreloadTelemetry preload)
        {
            if (preload == null) return;
            preloadBuilderBody = preload.BuilderBody ?? string.Empty;
            preloadBuilderLod = Math.Max(0, preload.BuilderLod);
            preloadBuilderTilesComplete = Math.Max(0L, preload.BuilderTilesComplete);
            preloadBuilderTilesPending = Math.Max(0, preload.BuilderTilesPending);
            preloadBuilderPqsMs = SafeNonNegative(preload.BuilderPqsMilliseconds);
            preloadBuilderWorkerUtilization = SafeRatio(preload.BuilderWorkerUtilization);
            preloadBuilderWriteMbps = SafeNonNegative(preload.BuilderWriteMbps);
            preloadBuilderCompressionRatio = SafeNonNegative(
                preload.BuilderCompressionRatio);
            preloadBuilderStorageBytes = Math.Max(0L, preload.BuilderStorageBytes);
            preloadBuilderPqsSamplesPerSecond = SafeNonNegative(
                preload.BuilderPqsSamplesPerSecond);
            preloadBuilderPqsSampleCostMs = SafeNonNegative(
                preload.BuilderPqsSampleCostMilliseconds);
            preloadBuilderPqsSampleCacheHits = Math.Max(0L,
                preload.BuilderPqsSampleCacheHits);
            preloadBuilderPqsSampleCacheMisses = Math.Max(0L,
                preload.BuilderPqsSampleCacheMisses);
            preloadBuilderPqsSampleCacheHitRatio = SafeRatio(
                preload.BuilderPqsSampleCacheHitRatio);
            preloadBuilderChunkBatchTiles = SafeNonNegative(
                preload.BuilderChunkBatchTiles);
            preloadBuilderChunkRewriteAmplification = SafeNonNegative(
                preload.BuilderChunkRewriteAmplification);
            preloadBuilderChunkFlushMs = SafeNonNegative(
                preload.BuilderChunkFlushMilliseconds);
            preloadBuilderIntermediateCommitsSkipped = Math.Max(0L,
                preload.BuilderIntermediateCommitsSkipped);
            terrainDbReadRequests = Math.Max(0L, preload.DatabaseReadRequests);
            terrainDbReadLatencyMs = SafeNonNegative(
                preload.DatabaseReadLatencyMilliseconds);
            terrainDbReadMbps = SafeNonNegative(preload.DatabaseReadMbps);
            terrainDbReadQueueDepth = Math.Max(0, preload.DatabaseReadQueueDepth);
            terrainDbCacheHitRatio = SafeRatio(preload.DatabaseCacheHitRatio);
            terrainDbCoalescedReads = Math.Max(0L, preload.DatabaseCoalescedReads);
            terrainDbCrcFailures = Math.Max(0L, preload.DatabaseCrcFailures);
            terrainDbHashMismatches = Math.Max(0L, preload.DatabaseHashMismatches);
            terrainDbParsedChunkCacheHits = Math.Max(0L,
                preload.DatabaseParsedChunkCacheHits);
            terrainDbParsedChunkCacheMisses = Math.Max(0L,
                preload.DatabaseParsedChunkCacheMisses);
            terrainDbParsedChunkCacheHitRatio = SafeRatio(
                preload.DatabaseParsedChunkCacheHitRatio);
            terrainDecompressQueueDelayMs = SafeNonNegative(
                preload.DecompressQueueDelayMilliseconds);
            terrainDecompressTimeMs = SafeNonNegative(preload.DecompressTimeMilliseconds);
            terrainDecompressMbps = SafeNonNegative(preload.DecompressMbps);
            terrainDecompressWorkerActive = Math.Max(0, preload.DecompressWorkerActive);
            terrainDecompressFailures = Math.Max(0L, preload.DecompressFailures);
            terrainFirstTileVisibleMs = SafeNonNegative(
                preload.FirstTileVisibleMilliseconds);
            terrainViewportCoverageRatio = SafeRatio(preload.ViewportCoverageRatio);
            terrainPreloadResultAgeMs = SafeNonNegative(preload.ResultAgeMilliseconds);
            terrainStaleResultsDiscarded = Math.Max(0L,
                preload.StaleResultsDiscarded);
            terrainGenerationFallbackCount = Math.Max(0L,
                preload.GenerationFallbackCount);
        }

        internal AERISRuntimeTelemetrySnapshot Telemetry
        {
            get { return runtimeTelemetry; }
        }

        internal AERISFileWriterTelemetrySnapshot WriterTelemetry
        {
            get { return writerTelemetry; }
        }

        internal string Summary
        {
            get
            {
                AERISRuntimeTelemetrySnapshot value = runtimeTelemetry;
                return "CPU " + value.LogicalProcessors + " / WORKERS " +
                    value.ConfiguredWorkers + " / PERMITS " + value.ActivePermits +
                    " / ACTIVE " + value.ActiveJobs + " | Q " + value.SafetyDepth +
                    "/" + value.GeneralDepth + "/" + value.TelemetryDepth + "/" +
                    value.ArchiveDepth + " | GPU " + gpu.Backend + " | WRITER Q " +
                    writerTelemetry.QueueDepth;
            }
        }

        internal string MainThreadSummary
        {
            get
            {
                return "AERIS MAIN P95 " + mainMillisecondsP95.ToString("0.000") +
                    " ms | SNAP " + snapshotCaptureMillisecondsP95.ToString("0.000") +
                    " | COMMIT " + commitDrainMillisecondsP95.ToString("0.000") + " ms";
            }
        }

        void WriteTelemetry(double now)
        {
            if (telemetryWriter == null || !telemetryWriter.Available) return;
            AERISRuntimeTelemetrySnapshot runtime = runtimeTelemetry;
            AERISFileWriterTelemetrySnapshot writer = writerTelemetry;
            telemetryWriter.WriteCsv(new AERISCsvField[] {
                AERISCsvField.Utc(DateTime.UtcNow),
                AERISCsvField.Fixed(now), runtime.LogicalProcessors,
                runtime.ConfiguredWorkers, runtime.ActivePermits, runtime.ActiveJobs,
                runtime.ActiveSafetyJobs, runtime.ActiveGeneralJobs,
                runtime.ActiveTelemetryJobs, runtime.ActiveArchiveJobs,
                runtime.SafetyDepth, runtime.GeneralDepth, runtime.TelemetryDepth,
                runtime.ArchiveDepth, AERISCsvField.Fixed(runtime.QueueDelayP50Ms),
                AERISCsvField.Fixed(runtime.QueueDelayP95Ms),
                AERISCsvField.Fixed(runtime.QueueDelayMaxMs),
                AERISCsvField.Fixed(runtime.WorkerP95Ms),
                AERISCsvField.Fixed(runtime.CommitP95Ms),
                AERISCsvField.Fixed(runtime.RunwayWorkerP95Ms),
                AERISCsvField.Fixed(runtime.TerrainWorkerP95Ms),
                AERISCsvField.Fixed(runtime.InstrumentWorkerP95Ms),
                AERISCsvField.Fixed(runtime.NavigationDisplayWorkerP95Ms),
                AERISCsvField.Fixed(runtime.RunwayResultAgeMs),
                AERISCsvField.Fixed(runtime.TerrainResultAgeMs),
                AERISCsvField.Fixed(runtime.InstrumentResultAgeMs),
                AERISCsvField.Fixed(runtime.NavigationDisplayResultAgeMs),
                AERISCsvField.Fixed(ndLayoutMillisecondsEma),
                AERISCsvField.Fixed(ndRepaintMillisecondsEma),
                AERISCsvField.Fixed(ndCaptureMillisecondsEma),
                AERISCsvField.Fixed(ndTextureUploadMillisecondsEma),
                AERISCsvField.Fixed(ndGcAllocationBytesEma),
                ndCapturedFacilityCount, ndCapturedRunwayCount, ndPlanMode,
                AERISCsvField.Fixed(ndRangeMeters),
                terrainTileRamBytes, terrainTileRamLimitBytes,
                terrainTileDiskBytes, terrainTileDiskLimitBytes,
                terrainTileRamCount, terrainTileDiskCount, terrainTileRamHits,
                terrainTileDiskHits, terrainTileMisses, terrainTileReused,
                terrainTileGenerated, terrainTileDiskWrites, terrainTileDiskFailures,
                terrainTileStaleCancelled, terrainTileDroppedRequests,
                terrainTilePreviewGenerated, terrainTileFinalGenerated,
                terrainTileObsoleteCancelled,
                AERISCsvField.Fixed(terrainTileHitRate), terrainTilePending,
                terrainTileDesired, terrainTileVisible, terrainTilePreviewCount,
                terrainTileSamplingRemaining, terrainTileSampleBatch,
                AERISCsvField.Fixed(terrainTileSampleBatchMs),
                AERISCsvField.Fixed(terrainTilePqsSampleEmaMs),
                terrainGpuBytes, terrainGpuTextures, terrainGpuPending,
                terrainGpuFailures, AERISCsvField.Fixed(terrainGpuCoverage),
                AERISCsvField.Fixed(terrainMeshWorkerEmaMs),
                AERISCsvField.Fixed(terrainContourWorkerEmaMs),
                terrainTileWarmBytes, terrainTileWarmCount,
                AERISCsvField.Quoted(preloadBuilderBody), preloadBuilderLod,
                preloadBuilderTilesComplete, preloadBuilderTilesPending,
                AERISCsvField.Fixed(preloadBuilderPqsMs),
                AERISCsvField.Fixed(preloadBuilderWorkerUtilization),
                AERISCsvField.Fixed(preloadBuilderWriteMbps),
                AERISCsvField.Fixed(preloadBuilderCompressionRatio),
                preloadBuilderStorageBytes,
                AERISCsvField.Fixed(preloadBuilderPqsSamplesPerSecond),
                AERISCsvField.Fixed(preloadBuilderPqsSampleCostMs),
                preloadBuilderPqsSampleCacheHits,
                preloadBuilderPqsSampleCacheMisses,
                AERISCsvField.Fixed(preloadBuilderPqsSampleCacheHitRatio),
                AERISCsvField.Fixed(preloadBuilderChunkBatchTiles),
                AERISCsvField.Fixed(preloadBuilderChunkRewriteAmplification),
                AERISCsvField.Fixed(preloadBuilderChunkFlushMs),
                preloadBuilderIntermediateCommitsSkipped, terrainDbReadRequests,
                AERISCsvField.Fixed(terrainDbReadLatencyMs),
                AERISCsvField.Fixed(terrainDbReadMbps), terrainDbReadQueueDepth,
                AERISCsvField.Fixed(terrainDbCacheHitRatio),
                terrainDbCoalescedReads, terrainDbCrcFailures,
                terrainDbHashMismatches, terrainDbParsedChunkCacheHits,
                terrainDbParsedChunkCacheMisses,
                AERISCsvField.Fixed(terrainDbParsedChunkCacheHitRatio),
                AERISCsvField.Fixed(terrainDecompressQueueDelayMs),
                AERISCsvField.Fixed(terrainDecompressTimeMs),
                AERISCsvField.Fixed(terrainDecompressMbps),
                terrainDecompressWorkerActive, terrainDecompressFailures,
                AERISCsvField.Fixed(terrainFirstTileVisibleMs),
                AERISCsvField.Fixed(terrainViewportCoverageRatio),
                AERISCsvField.Fixed(terrainPreloadResultAgeMs),
                terrainStaleResultsDiscarded, terrainGenerationFallbackCount,
                cp3ResidentActive, AERISCsvField.Quoted(cp3ResidentBody),
                cp3ResidentRamBytes, cp3ResidentRamBudgetBytes,
                cp3ResidentEntryCount, cp3ResidentRamCount,
                cp3ResidentGlobalCount, cp3ResidentFarCount,
                cp3ResidentRouteCount, cp3ResidentLocalCount,
                cp3ResidentLandCount, cp3ResidentPinnedCount,
                cp3ResidentCacheHits, cp3ResidentCacheMisses,
                cp3ResidentDecodeSubmissions, cp3ResidentDecodeSuccesses,
                cp3ResidentDecodeFailures, cp3ResidentStaleRejects,
                cp3ResidentBudgetRejects, cp3ResidentEvictions,
                cp3CorridorActive,
                AERISCsvField.Fixed(cp3CorridorGroundSpeedMps),
                AERISCsvField.Fixed(cp3CorridorTurnRateDegPerSecond),
                AERISCsvField.Fixed(cp3CorridorLookAheadSeconds),
                AERISCsvField.Fixed(cp3CorridorLookAheadDistanceMeters),
                AERISCsvField.Fixed(cp3CorridorHalfWidthMeters),
                cp3CorridorPointCount, cp3CorridorRequestedTiles,
                cp3CorridorPinnedTiles, cp3CorridorLandDemand,
                AERISCsvField.Quoted(cp3CorridorStatus),
                terrainContinuitySeeded, terrainContinuityReuseFrames,
                terrainUnknownBackingFrames,
                AERISCsvField.Fixed(terrainContinuityAgeMs),
                runtime.Submitted,
                runtime.Completed, runtime.Replaced, runtime.Dropped,
                runtime.Cancelled, runtime.Stale, runtime.Failed,
                AERISCsvField.Fixed(frameMillisecondsEma),
                AERISCsvField.Fixed(mainMillisecondsEma),
                AERISCsvField.Fixed(snapshotCaptureMillisecondsEma),
                AERISCsvField.Fixed(commitDrainMillisecondsEma),
                AERISCsvField.Fixed(frameMillisecondsP95),
                AERISCsvField.Fixed(mainMillisecondsP95),
                AERISCsvField.Fixed(snapshotCaptureMillisecondsP95),
                AERISCsvField.Fixed(commitDrainMillisecondsP95),
                AERISCsvField.Flag(gpu.Active), AERISCsvField.Quoted(gpu.Backend),
                AERISCsvField.Fixed(gpu.FrameMilliseconds), writer.QueueDepth,
                writer.Capacity, writer.Written, writer.DroppedCritical,
                writer.DroppedContinuous, writer.DroppedVerbose,
                writer.FirstDroppedSequence, writer.LastDroppedSequence,
                writer.Failures, AERISCsvField.Fixed(writer.BatchP95Milliseconds),
                AERISCsvField.Fixed(writer.EncodeP95Milliseconds),
                AERISCsvField.Fixed(writer.DiskP95Milliseconds),
                AERISCsvField.Fixed(writer.FlushP95Milliseconds),
                AERISFlightDataArchive.PendingCount,
                AERISFlightDataArchive.CompletedCount,
                AERISFlightDataArchive.FailureCount,
                AERISFlightDataArchive.DeferredCount,
                AERISCsvField.Fixed(AERISFlightDataArchive.LastDurationMilliseconds),
                AERISCsvField.Fixed(AERISFlightDataArchive.MaximumDurationMilliseconds),
                AERISCsvField.Fixed(gcBytesPerFrameEma),
                AERISCsvField.Quoted(runtime.DegradationReason)
            });
        }

        static double SafeNonNegative(double value)
        {
            return IsFinite(value) ? Math.Max(0.0, value) : 0.0;
        }

        static double SafeRatio(double value)
        {
            return IsFinite(value) ? Math.Max(0.0, Math.Min(1.0, value)) : 0.0;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static double Ema(double previous, double sample, double weight)
        {
            return previous <= 0.0 ? sample : previous * (1.0 - weight) + sample * weight;
        }

        static void RecordSample(double[] samples, ref int index, ref int count,
            double value)
        {
            samples[index++ % samples.Length] = value;
            count = Math.Min(samples.Length, count + 1);
        }

        static double SamplePercentile(double[] samples, int count, double fraction)
        {
            count = Math.Max(0, Math.Min(samples.Length, count));
            if (count == 0) return 0.0;
            var copy = new double[count];
            Array.Copy(samples, copy, count);
            Array.Sort(copy);
            int index = (int)Math.Ceiling((copy.Length - 1) * fraction);
            return copy[Math.Max(0, Math.Min(copy.Length - 1, index))];
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ClearPublishedSnapshots();
            if (navigationDisplay != null) navigationDisplay.Dispose();
            if (navigationTraffic != null) navigationTraffic.Dispose();
            instruments.Dispose();
            if (telemetryWriter != null) telemetryWriter.Dispose();
            gpu.Dispose();
            AERISFlightDataArchive.NotifyRuntimeStopping(this);
            scheduler.Dispose();
            Interlocked.CompareExchange(ref current, null, this);
        }
    }
}
