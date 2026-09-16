using System;
using System.Collections.Generic;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // AERIS51 C1/C2 orchestration for the preload coastline phase.
    //
    // C1 records FAR land/water classification while the FAR tile is already hot in
    // memory. On an uninterrupted cold rebuild, a complete transient index lets the
    // coastline phase skip the later all-FAR disk reread/classification pass. If the
    // transient index is incomplete (restart, partial pre-existing DB, cancellation),
    // the caller falls back to the accepted durable scan path.
    //
    // C2 submits only certified R047 Exact CPU bodies to the dedicated immutable
    // 129x129 worker batch. Unsupported bodies retain the accepted legacy path.
    internal sealed partial class AERISTerrainPreloadBuilder
    {
        sealed class R051TransientCoastlineIndex
        {
            internal string BodyName = string.Empty;
            internal string EnvironmentHash = string.Empty;
            internal long PlanGeneration;
            internal readonly Dictionary<string, AERISTerrainTileKey> NonBoundary =
                new Dictionary<string, AERISTerrainTileKey>(StringComparer.Ordinal);
            internal readonly Dictionary<string, AERISTerrainHeightTile> Candidates =
                new Dictionary<string, AERISTerrainHeightTile>(StringComparer.Ordinal);
            internal readonly List<string> CandidateOrder = new List<string>();
            internal int CandidateCursor;
            internal bool Activated;
            internal float ActivatedRealtime;
        }

        readonly Dictionary<string, R051TransientCoastlineIndex>
            r051TransientCoastline =
                new Dictionary<string, R051TransientCoastlineIndex>(
                    StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> r051ExactCoastlineInFlight =
            new HashSet<string>(StringComparer.Ordinal);

        void R051RecordFarCoastlineClassification(
            BodyPlan plan, AERISTerrainHeightTile tile)
        {
            if (plan == null || tile == null ||
                tile.Key.Lod != AERISTerrainTileLod.Far ||
                tile.Flags == null ||
                !string.Equals(tile.Key.EnvironmentHash,
                    plan.EnvironmentHash, StringComparison.Ordinal))
                return;

            string bodyName = plan.BodyName ?? string.Empty;
            string id = tile.Key.StableId;
            bool boundary =
                AERISTerrainCoastlineExtractor.ContainsLandWaterBoundary(tile);

            lock (sync)
            {
                R051TransientCoastlineIndex index;
                if (!r051TransientCoastline.TryGetValue(bodyName, out index) ||
                    index == null ||
                    index.PlanGeneration != plan.Generation ||
                    !string.Equals(index.EnvironmentHash, plan.EnvironmentHash,
                        StringComparison.Ordinal))
                {
                    index = new R051TransientCoastlineIndex
                    {
                        BodyName = bodyName,
                        EnvironmentHash = plan.EnvironmentHash ?? string.Empty,
                        PlanGeneration = plan.Generation
                    };
                    r051TransientCoastline[bodyName] = index;
                }

                if (boundary)
                {
                    index.NonBoundary.Remove(id);
                    if (!index.Candidates.ContainsKey(id))
                        index.CandidateOrder.Add(id);
                    index.Candidates[id] = tile.CloneImmutable();
                }
                else
                {
                    index.Candidates.Remove(id);
                    index.NonBoundary[id] = tile.Key;
                }
                index.Activated = false;
            }
        }

        bool R051TryScheduleTransientCoastline(
            BodyPlan plan,
            CelestialBody body,
            long total,
            int latitudeTiles,
            int longitudeTiles,
            out bool handled)
        {
            handled = false;
            if (plan == null || body == null || total <= 0L)
                return false;

            R051TransientCoastlineIndex index;
            lock (sync)
            {
                if (!r051TransientCoastline.TryGetValue(plan.BodyName, out index) ||
                    index == null ||
                    index.PlanGeneration != plan.Generation ||
                    !string.Equals(index.EnvironmentHash, plan.EnvironmentHash,
                        StringComparison.Ordinal))
                    return false;

                long classified =
                    (long)index.NonBoundary.Count + index.Candidates.Count;
                if (classified != total)
                    return false;
                handled = true;
            }

            if (!index.Activated)
            {
                long bodyRadiusMillimetres =
                    (long)Math.Round(Math.Max(0.0, body.Radius) * 1000.0);
                EnsureCoastlineProgressGrid(
                    plan, bodyRadiusMillimetres, latitudeTiles, longitudeTiles);

                foreach (KeyValuePair<string, AERISTerrainTileKey> pair in
                    index.NonBoundary)
                    MarkCoastlineTileProcessed(plan, pair.Value);

                index.CandidateCursor = 0;
                index.Activated = true;
                index.ActivatedRealtime = UnityEngine.Time.realtimeSinceStartup;
                R052BeginDynamicCoastlineAdmission(plan.BodyName);
                stateDirty = true;
                AERISLogger.Info(
                    "[AERIS51][PRELOAD_COAST_FAST]" +
                    "; body=" + (plan.BodyName ?? string.Empty) +
                    "; event=TRANSIENT_INDEX_ACTIVATED" +
                    "; classified=" + total +
                    "; candidates=" + index.Candidates.Count +
                    "; nonboundary=" + index.NonBoundary.Count +
                    "; disk_rescan=false");
            }

            int processed = CoastlineProcessedCount(plan);
            if (processed >= total &&
                PendingCoastlineSamplingCount(plan.BodyName) == 0 &&
                CountPendingForBody(plan.BodyName) == 0)
            {
                MarkCoastlineComplete(plan);
                stateDirty = true;
                AERISLogger.Info(
                    "[AERIS51][PRELOAD_COAST_FAST]" +
                    "; body=" + (plan.BodyName ?? string.Empty) +
                    "; event=COMPLETE" +
                    "; mode=TRANSIENT_INDEX_PLUS_EXACT_WORKER" +
                    "; processed=" + processed +
                    "; total=" + total +
                    "; elapsed_s=" +
                        Math.Max(0f, UnityEngine.Time.realtimeSinceStartup -
                            index.ActivatedRealtime).ToString("0.000",
                                System.Globalization.CultureInfo.InvariantCulture));
                R052CompleteDynamicCoastlineAdmission(plan.BodyName);
                R051ClearTransientCoastlineForBody(plan.BodyName);
                return false;
            }

            int count = index.CandidateOrder.Count;
            int inspected = 0;
            while (inspected++ < count && count > 0)
            {
                if (index.CandidateCursor < 0 ||
                    index.CandidateCursor >= count)
                    index.CandidateCursor = 0;
                string id = index.CandidateOrder[index.CandidateCursor++];
                AERISTerrainHeightTile baseTile;
                if (!index.Candidates.TryGetValue(id, out baseTile) ||
                    baseTile == null)
                    continue;
                if (R051CoastlineTileProcessed(plan, baseTile.Key))
                    continue;

                lock (sync)
                {
                    if (pendingCoastlineBaseTiles.ContainsKey(id) ||
                        pendingWrites.Contains(id))
                        continue;
                }

                if (TryEnqueueHighDensityCoastline(plan, body, baseTile))
                    return true;

                // Admission pressure is not a reason to fall back to a disk scan.
                return false;
            }

            return false;
        }

        bool R051CoastlineTileProcessed(
            BodyPlan plan, AERISTerrainTileKey key)
        {
            if (plan == null ||
                plan.CoastlineProcessedBitmap == null ||
                plan.CoastlineProgressLatitudeTiles <= 0 ||
                plan.CoastlineProgressLongitudeTiles <= 0 ||
                key.LatitudeIndex < 0 ||
                key.LongitudeIndex < 0 ||
                key.LatitudeIndex >= plan.CoastlineProgressLatitudeTiles ||
                key.LongitudeIndex >= plan.CoastlineProgressLongitudeTiles)
                return false;

            long linear =
                (long)key.LatitudeIndex *
                plan.CoastlineProgressLongitudeTiles +
                key.LongitudeIndex;
            int byteIndex = (int)(linear >> 3);
            int mask = 1 << (int)(linear & 7L);
            return byteIndex >= 0 &&
                byteIndex < plan.CoastlineProcessedBitmap.Length &&
                (plan.CoastlineProcessedBitmap[byteIndex] & mask) != 0;
        }

        int R051CoastlineAdmissionLimit()
        {
            return R052ResolveDynamicCoastlineAdmissionLimit();
        }

        int R051LegacyCoastlinePendingCountLocked()
        {
            return Math.Max(0,
                pendingCoastlineBaseTiles.Count -
                r051ExactCoastlineInFlight.Count);
        }

        bool R051TrySubmitExactCpuCoastline(
            BodyPlan plan,
            CelestialBody body,
            AERISTerrainHeightTile baseTile,
            out bool exactPolicy)
        {
            exactPolicy = false;
            if (plan == null || body == null || baseTile == null)
                return false;

            AERISR042ExactCpuShadowSourceResolver.Decision decision =
                AERISR042ExactCpuShadowSourceResolver.ResolveCandidate(body);
            exactPolicy = decision != null && decision.IsCandidate &&
                AERISR042ExactCpuShadowSourceResolver.ProducerSwitchEnabled &&
                !AERISTerrainTileSystem.RuntimeProducerFallbackActiveForBody(body);
            if (!exactPolicy)
                return false;

            AERISTerrainBlockPipeline.R051CoastlineExactSnapshot snapshot;
            string failure;
            if (!blockPipeline.R051TryCaptureCoastlineExactSnapshot(
                body, plan.EnvironmentHash, out snapshot, out failure) ||
                snapshot == null || !snapshot.IsValid)
            {
                plan.Paused = true;
                plan.Generation++;
                status =
                    "PRELOAD EXACT CPU COASTLINE SNAPSHOT FAILED: " +
                    plan.BodyName;
                AERISLogger.Warn(
                    "[AERIS51][PRELOAD_COAST_FAST]" +
                    "; body=" + (plan.BodyName ?? string.Empty) +
                    "; event=SNAPSHOT_FAIL" +
                    "; failure=" + (failure ?? string.Empty) +
                    "; fail_closed=true");
                return false;
            }

            string id = baseTile.Key.StableId;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null)
                return false;

            lock (sync)
            {
                if (r051ExactCoastlineInFlight.Count >=
                    R051CoastlineAdmissionLimit())
                    return false;
                if (!r051ExactCoastlineInFlight.Add(id))
                    return false;
            }

            int resolution =
                AERISTerrainCoastlineExtractor.HighDensityResolution;
            long capturedGeneration = plan.Generation;
            string capturedEnvironment = plan.EnvironmentHash ?? string.Empty;
            double south = baseTile.SouthLatitudeDeg;
            double north = baseTile.NorthLatitudeDeg;
            double west = baseTile.WestLongitudeDeg;
            double east = baseTile.EastLongitudeDeg;

            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute,
                "preload-coast-exact:" + id,
                runtime.CaptureStamp(),
                context =>
                {
                    return AERISTerrainBlockPipeline.R051BuildExactCoastline(
                        snapshot, south, north, west, east, resolution);
                },
                value =>
                {
                    lock (sync)
                        r051ExactCoastlineInFlight.Remove(id);

                    var result =
                        value as AERISTerrainBlockPipeline.R051CoastlineExactResult;
                    if (disposed || plan.Cancelled || plan.Paused ||
                        plan.Generation != capturedGeneration ||
                        !string.Equals(plan.EnvironmentHash,
                            capturedEnvironment, StringComparison.Ordinal))
                    {
                        lock (sync)
                            pendingCoastlineBaseTiles.Remove(id);
                        return;
                    }

                    if (result == null ||
                        !string.IsNullOrEmpty(result.Error) ||
                        result.WorkerThreadId <= 0 ||
                        result.WorkerThreadId == snapshot.MainThreadId ||
                        result.Samples != resolution * resolution ||
                        result.Flags == null ||
                        result.Flags.Length != resolution * resolution)
                    {
                        lock (sync)
                            pendingCoastlineBaseTiles.Remove(id);
                        plan.Paused = true;
                        plan.Generation++;
                        status =
                            "PRELOAD EXACT CPU COASTLINE WORKER FAILED: " +
                            plan.BodyName;
                        AERISLogger.Warn(
                            "[AERIS51][PRELOAD_COAST_FAST]" +
                            "; body=" + (plan.BodyName ?? string.Empty) +
                            "; event=WORKER_FAIL" +
                            "; tile=" + id +
                            "; error=" +
                                (result == null ? "RESULT_NULL" :
                                    (result.Error ?? string.Empty)) +
                            "; fail_closed=true");
                        return;
                    }

                    R051CommitExactCoastline(plan, baseTile, result);
                },
                false);

            if (!accepted)
            {
                lock (sync)
                    r051ExactCoastlineInFlight.Remove(id);
                return false;
            }

            AERISLogger.Info(
                "[AERIS51][PRELOAD_COAST_FAST]" +
                "; body=" + (plan.BodyName ?? string.Empty) +
                "; event=EXACT_WORKER_QUEUE" +
                "; tile=" + id +
                "; resolution=" + resolution +
                "; main_thread_runtime_access=false" +
                "; worker_runtime_object_access=false");
            return true;
        }

        void R051CommitExactCoastline(
            BodyPlan plan,
            AERISTerrainHeightTile capturedBaseTile,
            AERISTerrainBlockPipeline.R051CoastlineExactResult result)
        {
            if (plan == null || capturedBaseTile == null || result == null)
                return;
            string id = capturedBaseTile.Key.StableId;
            AERISTerrainHeightTile baseTile;
            lock (sync)
            {
                if (!pendingCoastlineBaseTiles.TryGetValue(id, out baseTile) ||
                    baseTile == null)
                    return;
                pendingCoastlineBaseTiles.Remove(id);
            }

            int resolution =
                AERISTerrainCoastlineExtractor.HighDensityResolution;
            var upgraded = baseTile.CloneImmutable();
            upgraded.HighDensityCoastlineResolution = resolution;
            upgraded.HighDensityCoastlineSegments =
                result.Segments == null ? new float[0] :
                    (float[])result.Segments.Clone();
            upgraded.HighDensityCoastalFlags =
                result.Flags == null ? new byte[0] :
                    (byte[])result.Flags.Clone();
            upgraded.CreatedUtcTicks = DateTime.UtcNow.Ticks;
            upgraded.Source = AERISTerrainTileSource.PreloadBuilderGenerated;
            upgraded.SamplingComplete = true;
            upgraded.IsPreview = false;
            upgraded.RuntimeExactCpuPolicyExpected = true;
            upgraded.RuntimeExactCpuProduced = true;
            CommitGeneratedTile(plan, upgraded, true, true);
            plan.CoastlineScannedWithoutUpgrade = 0L;
            stateDirty = true;

            AERISLogger.Info(
                "[AERIS51][PRELOAD_COAST_FAST]" +
                "; body=" + (plan.BodyName ?? string.Empty) +
                "; event=EXACT_WORKER_COMMIT" +
                "; tile=" + id +
                "; resolution=" + resolution +
                "; samples=" + result.Samples +
                "; segments=" +
                    (result.Segments == null ? 0 : result.Segments.Length / 4) +
                "; worker_thread=" + result.WorkerThreadId +
                "; worker_ms=" +
                    result.WorkerMilliseconds.ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture) +
                "; authority=EXACT_CPU");
        }

        void R051ClearTransientCoastlineForBody(string bodyName)
        {
            lock (sync)
            {
                if (string.IsNullOrEmpty(bodyName))
                {
                    r051TransientCoastline.Clear();
                    r051ExactCoastlineInFlight.Clear();
                    R052ResetDynamicCoastlineAdmission(string.Empty);
                    return;
                }
                r051TransientCoastline.Remove(bodyName);

                var remove = new List<string>();
                foreach (string id in r051ExactCoastlineInFlight)
                    if (id.IndexOf("|" + bodyName + "|",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                        remove.Add(id);
                for (int i = 0; i < remove.Count; i++)
                    r051ExactCoastlineInFlight.Remove(remove[i]);
            }
            R052ResetDynamicCoastlineAdmission(bodyName);
        }
    }
}
