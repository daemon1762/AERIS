using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // R043 PRELOAD_PTC shared pipeline.
    //
    // This partial augments the real AERISTerrainBlockPipeline for
    // persistent terrain requests owned by PreloadBuilder or FlightFallback.
    //
    // Main thread:
    // - certifies/caches the R042 immutable pure snapshot;
    // - selects Exact CPU or fail-closed PQS authority;
    // - reconstructs stock PQS direction/map coordinates and copies primitives.
    //
    // GeneralCompute worker:
    // - evaluates the accepted pure CLR exact source from immutable data only;
    // - in validation mode, compares PQS authority bit-for-bit;
    // - in R047 production mode, writes the Exact CPU sample into the production tile;
    // - never touches Unity/KSP mutable runtime objects.
    //
    // Historical note:
    // - this seam began as a PQS-vs-Exact shadow validator;
    // - R047 reuses the same immutable worker payload as the Exact CPU production path;
    // - PQS remains the fail-closed authority for unsupported or failed bodies.
    internal sealed partial class AERISTerrainBlockPipeline
    {
        sealed class R043PtcTileState
        {
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal string BodyName = string.Empty;
            internal string PqsHash = string.Empty;
            internal bool ProductionEnabled;
            internal bool HasOcean;
            internal int ProductionSamples;
            internal int ProductionFailures;
            internal int ExpectedSamples;
            internal int Samples;
            internal int Mismatches;
            internal int NonFinite;
            internal int BoundaryCacheHitSamples;
            internal int BoundaryCacheHitMismatches;
            internal int NonBoundaryCacheHitMismatches;
            internal double MaxAbsError;
            internal int Blocks;
            internal bool AllWorkersOffMainThread = true;
            internal string Error = string.Empty;

            internal int FirstMismatchIndex = -1;
            internal long FirstExpectedBits;
            internal long FirstActualBits;
            internal double FirstExpected;
            internal double FirstActual;
            internal double FirstLatitude;
            internal double FirstLongitude;
            internal double FirstX;
            internal double FirstY;
            internal double FirstZ;
            internal double FirstU;
            internal double FirstV;
            internal int FirstBlockId = -1;
            internal int FirstGlobalX = -1;
            internal int FirstGlobalY = -1;
            internal bool FirstBoundary;
            internal bool FirstBoundaryCacheHit;
            internal double FirstExpectedSourceLatitude;
            internal double FirstExpectedSourceLongitude;

            internal double[] SamplingExpected;
            internal double[] SamplingX;
            internal double[] SamplingY;
            internal double[] SamplingZ;
            internal double[] SamplingU;
            internal double[] SamplingV;
            internal double[] SamplingLatitude;
            internal double[] SamplingLongitude;
            internal double[] SamplingExpectedSourceLatitude;
            internal double[] SamplingExpectedSourceLongitude;
            internal byte[] SamplingBoundaryCacheHit;
            internal byte[] SamplingActive;
        }

        sealed class R043PtcBlockPayload
        {
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal string BodyName = string.Empty;
            internal double[] Expected;
            internal double[] X;
            internal double[] Y;
            internal double[] Z;
            internal double[] U;
            internal double[] V;
            internal double[] Latitude;
            internal double[] Longitude;
            internal double[] ExpectedSourceLatitude;
            internal double[] ExpectedSourceLongitude;
            internal byte[] BoundaryCacheHit;
            internal byte[] Active;
            internal bool ProductionEnabled;
            internal bool HasOcean;
            internal float[] ProductionElevation;
            internal byte[] ProductionFlags;
            internal int ProductionSamples;
            internal int ProductionFailures;
            internal int BlockId;
            internal int BlockX0;
            internal int BlockY0;
            internal int BlockWidth;
            internal int Resolution;
            internal int MainThreadId;
            internal int WorkerThreadId;
            internal int Samples;
            internal int Mismatches;
            internal int NonFinite;
            internal int BoundaryCacheHitSamples;
            internal int BoundaryCacheHitMismatches;
            internal int NonBoundaryCacheHitMismatches;
            internal double MaxAbsError;
            internal string Error = string.Empty;

            internal int FirstMismatchIndex = -1;
            internal long FirstExpectedBits;
            internal long FirstActualBits;
            internal double FirstExpected;
            internal double FirstActual;
            internal double FirstLatitude;
            internal double FirstLongitude;
            internal double FirstX;
            internal double FirstY;
            internal double FirstZ;
            internal double FirstU;
            internal double FirstV;
            internal int FirstBlockId = -1;
            internal int FirstGlobalX = -1;
            internal int FirstGlobalY = -1;
            internal bool FirstBoundary;
            internal bool FirstBoundaryCacheHit;
            internal double FirstExpectedSourceLatitude;
            internal double FirstExpectedSourceLongitude;
        }

        readonly Dictionary<string, AERISR042ExactCpuShadowRuntimeSnapshot>
            r043SnapshotCache =
                new Dictionary<string, AERISR042ExactCpuShadowRuntimeSnapshot>(
                    StringComparer.Ordinal);
        readonly HashSet<string> r043SnapshotCaptureLogged =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> r043SnapshotFailureLogged =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> r043NaturalBodiesReported =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        R043PtcTileState TryCreateR043PtcState(
            CelestialBody body,
            AERISTerrainTileRequest request,
            string pqsHash)
        {
            if (body == null || request == null ||
                (request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder &&
                 request.WorkOwner != AERISTerrainWorkOwner.FlightFallback))
                return null;

            AERISR042ExactCpuShadowSourceResolver.Decision decision =
                AERISR042ExactCpuShadowSourceResolver.ResolveCandidate(body);
            if (decision == null || !decision.IsCandidate)
                return null;

            if (Thread.CurrentThread.ManagedThreadId != r040bMainThreadId)
            {
                LogR043SnapshotFailure(body.name, "CAPTURE_NOT_MAIN_THREAD");
                return null;
            }

            string bodyName = body.name ?? string.Empty;
            string authorityHash = pqsHash ?? string.Empty;
            string cacheKey = bodyName + "|" + authorityHash;
            AERISR042ExactCpuShadowRuntimeSnapshot snapshot;
            if (!r043SnapshotCache.TryGetValue(cacheKey, out snapshot) ||
                snapshot == null)
            {
                string failure;
                if (!AERISR042ExactCpuShadowRuntimeSnapshotBuilder.TryCapture(
                    body, r040bMainThreadId, out snapshot, out failure) ||
                    snapshot == null || !snapshot.IsStructurallyValid)
                {
                    LogR043SnapshotFailure(
                        bodyName,
                        string.IsNullOrEmpty(failure) ?
                            "SNAPSHOT_INVALID" : failure);
                    return null;
                }

                r043SnapshotCache[cacheKey] = snapshot;
            }

            bool productionEnabled =
                R047ShouldUseExactCpuProduction(decision, snapshot, bodyName);
            R047LogProducerSelection(bodyName, snapshot, productionEnabled);

            if (r043SnapshotCaptureLogged.Add(cacheKey))
            {
                AERISLogger.Info(
                    "[AERIS43][R043_PRELOAD_PTC_INTEGRATED_SNAPSHOT]" +
                    "; pass=true" +
                    "; body=" + SafeR043(bodyName) +
                    "; candidate=" + snapshot.Candidate +
                    "; capture_thread_id=" +
                        snapshot.CaptureThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_thread_id=" +
                        r040bMainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; capture_thread_is_main=" +
                        BoolR043(snapshot.CaptureThreadId == r040bMainThreadId) +
                    "; snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY" +
                    "; production_authority=" +
                        (productionEnabled ? "EXACT_CPU" : "PQS") +
                    "; producer_switch=" + BoolR043(productionEnabled) +
                    "; db_authority=" +
                        (productionEnabled ? "EXACT_CPU" : "PQS") +
                    "; exact_cpu_db_write=" + BoolR043(productionEnabled) +
                    "; worker_runtime_object_access=false");
            }

            int expectedSamples;
            try
            {
                expectedSamples = checked(request.Resolution * request.Resolution);
            }
            catch
            {
                return null;
            }

            return new R043PtcTileState
            {
                Snapshot = snapshot,
                BodyName = bodyName,
                PqsHash = authorityHash,
                ProductionEnabled = productionEnabled,
                HasOcean = body.ocean,
                ExpectedSamples = expectedSamples
            };
        }

        void LogR043SnapshotFailure(string bodyName, string failure)
        {
            string key = (bodyName ?? string.Empty) + "|" + (failure ?? string.Empty);
            if (!r043SnapshotFailureLogged.Add(key)) return;
            AERISLogger.Warn(
                "[AERIS43][R043_PRELOAD_PTC_INTEGRATED_SNAPSHOT]" +
                "; pass=false" +
                "; body=" + SafeR043(bodyName) +
                "; failure=" + SafeR043(failure) +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; exact_cpu_db_write=false");
        }

        static void BeginR043PtcBlock(TileState state, int count)
        {
            if (state == null || state.R043Ptc == null || count <= 0) return;
            R043PtcTileState shadow = state.R043Ptc;
            shadow.SamplingExpected = new double[count];
            shadow.SamplingX = new double[count];
            shadow.SamplingY = new double[count];
            shadow.SamplingZ = new double[count];
            shadow.SamplingU = new double[count];
            shadow.SamplingV = new double[count];
            shadow.SamplingLatitude = new double[count];
            shadow.SamplingLongitude = new double[count];
            shadow.SamplingExpectedSourceLatitude = new double[count];
            shadow.SamplingExpectedSourceLongitude = new double[count];
            shadow.SamplingBoundaryCacheHit = new byte[count];
            shadow.SamplingActive = new byte[count];
        }

        static void CaptureR043PtcSample(
            TileState state,
            int local,
            double latitude,
            double longitude,
            double expectedAsl,
            double expectedSourceLatitude,
            double expectedSourceLongitude,
            bool boundaryCacheHit)
        {
            if (state == null || state.R043Ptc == null) return;
            R043PtcTileState shadow = state.R043Ptc;
            if (shadow.SamplingExpected == null ||
                shadow.SamplingX == null ||
                shadow.SamplingY == null ||
                shadow.SamplingZ == null ||
                shadow.SamplingU == null ||
                shadow.SamplingV == null ||
                shadow.SamplingLatitude == null ||
                shadow.SamplingLongitude == null ||
                shadow.SamplingExpectedSourceLatitude == null ||
                shadow.SamplingExpectedSourceLongitude == null ||
                shadow.SamplingBoundaryCacheHit == null ||
                shadow.SamplingActive == null ||
                local < 0 || local >= shadow.SamplingExpected.Length)
            {
                if (string.IsNullOrEmpty(shadow.Error))
                    shadow.Error = "SAMPLING_ARRAY_CONTRACT";
                return;
            }

            try
            {
                if (state.Body == null)
                    throw new InvalidOperationException("BODY_NULL");

                // AERIS46 natural-parity repair: on a boundary-cache hit,
                // expectedAsl belongs to the exact source coordinate that first
                // populated the cache entry. Evaluate the shadow at that same
                // source coordinate rather than at the current rounded-key peer.
                // Production sampling/tile output remain unchanged.
                double evaluationLatitude = boundaryCacheHit ?
                    expectedSourceLatitude : latitude;
                double evaluationLongitude = boundaryCacheHit ?
                    expectedSourceLongitude : longitude;

                Vector3d inputDirection = state.Body.GetRelSurfaceNVector(
                    evaluationLatitude, evaluationLongitude);
                Vector3d direction = inputDirection.normalized;

                if (!FiniteR043(direction.x) ||
                    !FiniteR043(direction.y) ||
                    !FiniteR043(direction.z))
                    throw new InvalidOperationException("DIRECTION_NONFINITE");

                double mapU;
                double mapV;
                ComputeR043StockMapCoords(direction, out mapU, out mapV);

                shadow.SamplingExpected[local] = expectedAsl;
                shadow.SamplingX[local] = direction.x;
                shadow.SamplingY[local] = direction.y;
                shadow.SamplingZ[local] = direction.z;
                shadow.SamplingU[local] = mapU;
                shadow.SamplingV[local] = mapV;
                shadow.SamplingLatitude[local] = latitude;
                shadow.SamplingLongitude[local] = longitude;
                shadow.SamplingExpectedSourceLatitude[local] =
                    expectedSourceLatitude;
                shadow.SamplingExpectedSourceLongitude[local] =
                    expectedSourceLongitude;
                shadow.SamplingBoundaryCacheHit[local] =
                    boundaryCacheHit ? (byte)1 : (byte)0;
                shadow.SamplingActive[local] = 1;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(shadow.Error))
                    shadow.Error = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }
        }

        static R043PtcBlockPayload BuildR043PtcBlockPayload(TileState state)
        {
            if (state == null || state.R043Ptc == null) return null;
            R043PtcTileState shadow = state.R043Ptc;
            return new R043PtcBlockPayload
            {
                Snapshot = shadow.Snapshot,
                BodyName = shadow.BodyName,
                Expected = shadow.SamplingExpected,
                X = shadow.SamplingX,
                Y = shadow.SamplingY,
                Z = shadow.SamplingZ,
                U = shadow.SamplingU,
                V = shadow.SamplingV,
                Latitude = shadow.SamplingLatitude,
                Longitude = shadow.SamplingLongitude,
                ExpectedSourceLatitude = shadow.SamplingExpectedSourceLatitude,
                ExpectedSourceLongitude = shadow.SamplingExpectedSourceLongitude,
                BoundaryCacheHit = shadow.SamplingBoundaryCacheHit,
                Active = shadow.SamplingActive,
                ProductionEnabled = shadow.ProductionEnabled,
                HasOcean = shadow.HasOcean,
                ProductionElevation = state.SamplingElevation,
                ProductionFlags = state.SamplingFlags,
                BlockId = state.SamplingBlock == null ? -1 : state.SamplingBlock.Id,
                BlockX0 = state.SamplingBlock == null ? -1 : state.SamplingBlock.X0,
                BlockY0 = state.SamplingBlock == null ? -1 : state.SamplingBlock.Y0,
                BlockWidth = state.SamplingBlock == null ? 0 : state.SamplingBlock.Width,
                Resolution = state.Request == null ? 0 : state.Request.Resolution,
                MainThreadId = shadow.Snapshot == null ?
                    0 : shadow.Snapshot.CaptureThreadId,
                Error = shadow.Error
            };
        }

        static void ReleaseR043SamplingBlock(TileState state)
        {
            if (state == null || state.R043Ptc == null) return;
            R043PtcTileState shadow = state.R043Ptc;
            shadow.SamplingExpected = null;
            shadow.SamplingX = null;
            shadow.SamplingY = null;
            shadow.SamplingZ = null;
            shadow.SamplingU = null;
            shadow.SamplingV = null;
            shadow.SamplingLatitude = null;
            shadow.SamplingLongitude = null;
            shadow.SamplingExpectedSourceLatitude = null;
            shadow.SamplingExpectedSourceLongitude = null;
            shadow.SamplingBoundaryCacheHit = null;
            shadow.SamplingActive = null;
        }

        static void ProcessR043PtcBlock(R043PtcBlockPayload block)
        {
            if (block == null) return;
            block.WorkerThreadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                if (!string.IsNullOrEmpty(block.Error))
                    return;
                if (block.Snapshot == null || !block.Snapshot.IsStructurallyValid)
                    throw new InvalidOperationException("SNAPSHOT_INVALID");
                if (block.MainThreadId <= 0 ||
                    block.WorkerThreadId == block.MainThreadId)
                    throw new InvalidOperationException("WORKER_IS_MAIN");
                if (block.Expected == null ||
                    block.X == null || block.Y == null || block.Z == null ||
                    block.U == null || block.V == null ||
                    block.Latitude == null || block.Longitude == null ||
                    block.ExpectedSourceLatitude == null ||
                    block.ExpectedSourceLongitude == null ||
                    block.BoundaryCacheHit == null ||
                    block.Active == null ||
                    (block.ProductionEnabled &&
                     (block.ProductionElevation == null ||
                      block.ProductionFlags == null)))
                    throw new InvalidOperationException("PAYLOAD_ARRAY_NULL");

                int count = block.Expected.Length;
                if (block.X.Length != count ||
                    block.Y.Length != count ||
                    block.Z.Length != count ||
                    block.U.Length != count ||
                    block.V.Length != count ||
                    block.Latitude.Length != count ||
                    block.Longitude.Length != count ||
                    block.ExpectedSourceLatitude.Length != count ||
                    block.ExpectedSourceLongitude.Length != count ||
                    block.BoundaryCacheHit.Length != count ||
                    block.Active.Length != count ||
                    (block.ProductionEnabled &&
                     (block.ProductionElevation.Length != count ||
                      block.ProductionFlags.Length != count)))
                    throw new InvalidOperationException("PAYLOAD_ARRAY_LENGTH");

                for (int i = 0; i < count; i++)
                {
                    if (block.Active[i] == 0) continue;

                    double absolute;
                    if (block.Snapshot.Candidate ==
                        AERISR042ExactCpuShadowSourceResolver.CandidateKind.
                            R039MinmusVertexPlanet)
                    {
                        absolute = AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                            block.Snapshot.MinmusSnapshot,
                            block.X[i], block.Y[i], block.Z[i],
                            block.Snapshot.PqsRadius);
                    }
                    else if (block.Snapshot.Candidate ==
                        AERISR042ExactCpuShadowSourceResolver.CandidateKind.
                            R041HeightModifierChain)
                    {
                        absolute =
                            AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                                block.Snapshot.HeightChainSnapshot,
                                block.X[i], block.Y[i], block.Z[i],
                                block.U[i], block.V[i],
                                block.Snapshot.PqsRadius);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            "UNSUPPORTED_CANDIDATE:" + block.Snapshot.Candidate);
                    }

                    double actualAsl = absolute - block.Snapshot.PqsRadius;
                    if (actualAsl < 0.0) actualAsl = 0.0;

                    if (block.ProductionEnabled)
                    {
                        R047WriteExactCpuProductionSample(block, i, actualAsl);
                        block.Samples++;
                        continue;
                    }

                    double expectedAsl = block.Expected[i];

                    bool finite =
                        FiniteR043(actualAsl) && FiniteR043(expectedAsl);
                    double error = finite ?
                        Math.Abs(actualAsl - expectedAsl) :
                        double.PositiveInfinity;
                    if (error > block.MaxAbsError)
                        block.MaxAbsError = error;

                    block.Samples++;
                    bool boundaryCacheHit = block.BoundaryCacheHit[i] != 0;
                    if (boundaryCacheHit)
                        block.BoundaryCacheHitSamples++;
                    if (!finite)
                        block.NonFinite++;

                    long expectedBits =
                        BitConverter.DoubleToInt64Bits(expectedAsl);
                    long actualBits =
                        BitConverter.DoubleToInt64Bits(actualAsl);
                    if (!finite || actualBits != expectedBits)
                    {
                        block.Mismatches++;
                        if (boundaryCacheHit)
                            block.BoundaryCacheHitMismatches++;
                        else
                            block.NonBoundaryCacheHitMismatches++;
                        if (block.FirstMismatchIndex < 0)
                        {
                            block.FirstMismatchIndex = i;
                            block.FirstExpectedBits = expectedBits;
                            block.FirstActualBits = actualBits;
                            block.FirstExpected = expectedAsl;
                            block.FirstActual = actualAsl;
                            block.FirstLatitude = block.Latitude[i];
                            block.FirstLongitude = block.Longitude[i];
                            block.FirstX = block.X[i];
                            block.FirstY = block.Y[i];
                            block.FirstZ = block.Z[i];
                            block.FirstU = block.U[i];
                            block.FirstV = block.V[i];
                            block.FirstBlockId = block.BlockId;
                            int localX = block.BlockWidth > 0 ?
                                i % block.BlockWidth : -1;
                            int localY = block.BlockWidth > 0 ?
                                i / block.BlockWidth : -1;
                            block.FirstGlobalX = localX < 0 ?
                                -1 : block.BlockX0 + localX;
                            block.FirstGlobalY = localY < 0 ?
                                -1 : block.BlockY0 + localY;
                            block.FirstBoundary =
                                block.Resolution > 0 &&
                                (block.FirstGlobalX == 0 ||
                                 block.FirstGlobalY == 0 ||
                                 block.FirstGlobalX == block.Resolution - 1 ||
                                 block.FirstGlobalY == block.Resolution - 1);
                            block.FirstBoundaryCacheHit =
                                block.BoundaryCacheHit[i] != 0;
                            block.FirstExpectedSourceLatitude =
                                block.ExpectedSourceLatitude[i];
                            block.FirstExpectedSourceLongitude =
                                block.ExpectedSourceLongitude[i];
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                block.Error = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }
        }

        static void CommitR043PtcBlock(
            TileState state,
            R043PtcBlockPayload block)
        {
            if (state == null || state.R043Ptc == null || block == null) return;
            R043PtcTileState shadow = state.R043Ptc;
            shadow.Blocks++;
            shadow.Samples += block.Samples;
            shadow.ProductionSamples += block.ProductionSamples;
            shadow.ProductionFailures += block.ProductionFailures;
            shadow.Mismatches += block.Mismatches;
            shadow.NonFinite += block.NonFinite;
            shadow.BoundaryCacheHitSamples += block.BoundaryCacheHitSamples;
            shadow.BoundaryCacheHitMismatches +=
                block.BoundaryCacheHitMismatches;
            shadow.NonBoundaryCacheHitMismatches +=
                block.NonBoundaryCacheHitMismatches;
            if (block.MaxAbsError > shadow.MaxAbsError)
                shadow.MaxAbsError = block.MaxAbsError;
            shadow.AllWorkersOffMainThread =
                shadow.AllWorkersOffMainThread &&
                block.WorkerThreadId > 0 &&
                block.WorkerThreadId != block.MainThreadId;
            if (shadow.FirstMismatchIndex < 0 &&
                block.FirstMismatchIndex >= 0)
            {
                shadow.FirstMismatchIndex = block.FirstMismatchIndex;
                shadow.FirstExpectedBits = block.FirstExpectedBits;
                shadow.FirstActualBits = block.FirstActualBits;
                shadow.FirstExpected = block.FirstExpected;
                shadow.FirstActual = block.FirstActual;
                shadow.FirstLatitude = block.FirstLatitude;
                shadow.FirstLongitude = block.FirstLongitude;
                shadow.FirstX = block.FirstX;
                shadow.FirstY = block.FirstY;
                shadow.FirstZ = block.FirstZ;
                shadow.FirstU = block.FirstU;
                shadow.FirstV = block.FirstV;
                shadow.FirstBlockId = block.FirstBlockId;
                shadow.FirstGlobalX = block.FirstGlobalX;
                shadow.FirstGlobalY = block.FirstGlobalY;
                shadow.FirstBoundary = block.FirstBoundary;
                shadow.FirstBoundaryCacheHit = block.FirstBoundaryCacheHit;
                shadow.FirstExpectedSourceLatitude =
                    block.FirstExpectedSourceLatitude;
                shadow.FirstExpectedSourceLongitude =
                    block.FirstExpectedSourceLongitude;
            }
            if (string.IsNullOrEmpty(shadow.Error) &&
                !string.IsNullOrEmpty(block.Error))
                shadow.Error = block.Error;
        }

        void ReportR043PtcTile(TileState state)
        {
            if (state == null || state.Request == null ||
                state.R043Ptc == null) return;

            R043PtcTileState shadow = state.R043Ptc;
            bool pass =
                shadow.Snapshot != null &&
                shadow.Snapshot.IsStructurallyValid &&
                shadow.Samples == shadow.ExpectedSamples &&
                shadow.Mismatches == 0 &&
                shadow.NonFinite == 0 &&
                shadow.AllWorkersOffMainThread &&
                string.IsNullOrEmpty(shadow.Error);

            string stableId = state.Request.Key.StableId;
            bool emit = !pass ||
                r043NaturalBodiesReported.Add(shadow.BodyName);
            if (!emit) return;

            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]" +
                "; pass=" + BoolR043(pass) +
                "; proof_request=false" +
                "; live_preload_proof=false" +
                "; body=" + SafeR043(shadow.BodyName) +
                "; stable_id=" + SafeR043(stableId) +
                "; tile=" + SafeR043(state.Request.Key.FileStem) +
                "; lod=" + state.Request.Key.Lod +
                "; resolution=" +
                    state.Request.Resolution.ToString(CultureInfo.InvariantCulture) +
                "; checks=" +
                    shadow.Samples.ToString(CultureInfo.InvariantCulture) +
                "; expected_checks=" +
                    shadow.ExpectedSamples.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" +
                    shadow.Mismatches.ToString(CultureInfo.InvariantCulture) +
                "; boundary_cache_hit_samples=" +
                    shadow.BoundaryCacheHitSamples.ToString(
                        CultureInfo.InvariantCulture) +
                "; boundary_cache_hit_mismatch_count=" +
                    shadow.BoundaryCacheHitMismatches.ToString(
                        CultureInfo.InvariantCulture) +
                "; non_boundary_cache_hit_mismatch_count=" +
                    shadow.NonBoundaryCacheHitMismatches.ToString(
                        CultureInfo.InvariantCulture) +
                "; nonfinite_count=" +
                    shadow.NonFinite.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" +
                    BoolR043(shadow.Mismatches == 0 && shadow.NonFinite == 0) +
                "; max_abs_error=" +
                    shadow.MaxAbsError.ToString("R", CultureInfo.InvariantCulture) +
                "; first_mismatch_index=" +
                    shadow.FirstMismatchIndex.ToString(CultureInfo.InvariantCulture) +
                "; first_expected_bits=" +
                    shadow.FirstExpectedBits.ToString("x16", CultureInfo.InvariantCulture) +
                "; first_actual_bits=" +
                    shadow.FirstActualBits.ToString("x16", CultureInfo.InvariantCulture) +
                "; first_expected=" +
                    shadow.FirstExpected.ToString("R", CultureInfo.InvariantCulture) +
                "; first_actual=" +
                    shadow.FirstActual.ToString("R", CultureInfo.InvariantCulture) +
                "; first_lat=" +
                    shadow.FirstLatitude.ToString("R", CultureInfo.InvariantCulture) +
                "; first_lon=" +
                    shadow.FirstLongitude.ToString("R", CultureInfo.InvariantCulture) +
                "; first_x=" +
                    shadow.FirstX.ToString("R", CultureInfo.InvariantCulture) +
                "; first_y=" +
                    shadow.FirstY.ToString("R", CultureInfo.InvariantCulture) +
                "; first_z=" +
                    shadow.FirstZ.ToString("R", CultureInfo.InvariantCulture) +
                "; first_u=" +
                    shadow.FirstU.ToString("R", CultureInfo.InvariantCulture) +
                "; first_v=" +
                    shadow.FirstV.ToString("R", CultureInfo.InvariantCulture) +
                "; first_block_id=" +
                    shadow.FirstBlockId.ToString(CultureInfo.InvariantCulture) +
                "; first_global_x=" +
                    shadow.FirstGlobalX.ToString(CultureInfo.InvariantCulture) +
                "; first_global_y=" +
                    shadow.FirstGlobalY.ToString(CultureInfo.InvariantCulture) +
                "; first_boundary=" +
                    BoolR043(shadow.FirstBoundary) +
                "; first_boundary_cache_hit=" +
                    BoolR043(shadow.FirstBoundaryCacheHit) +
                "; first_expected_source_lat=" +
                    shadow.FirstExpectedSourceLatitude.ToString(
                        "R", CultureInfo.InvariantCulture) +
                "; first_expected_source_lon=" +
                    shadow.FirstExpectedSourceLongitude.ToString(
                        "R", CultureInfo.InvariantCulture) +
                "; first_lat_bits=0x" +
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        shadow.FirstLatitude)).ToString(
                            "x16", CultureInfo.InvariantCulture) +
                "; first_lon_bits=0x" +
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        shadow.FirstLongitude)).ToString(
                            "x16", CultureInfo.InvariantCulture) +
                "; first_expected_source_lat_bits=0x" +
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        shadow.FirstExpectedSourceLatitude)).ToString(
                            "x16", CultureInfo.InvariantCulture) +
                "; first_expected_source_lon_bits=0x" +
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        shadow.FirstExpectedSourceLongitude)).ToString(
                            "x16", CultureInfo.InvariantCulture) +
                "; blocks=" +
                    shadow.Blocks.ToString(CultureInfo.InvariantCulture) +
                "; production_mode=" + BoolR043(shadow.ProductionEnabled) +
                "; production_samples=" +
                    shadow.ProductionSamples.ToString(CultureInfo.InvariantCulture) +
                "; production_failures=" +
                    shadow.ProductionFailures.ToString(CultureInfo.InvariantCulture) +
                "; comparison_mode=" +
                    (shadow.ProductionEnabled ? "PRODUCTION_NO_PQS" : "PQS_BIT_EXACT") +
                "; worker_not_main=" +
                    BoolR043(shadow.AllWorkersOffMainThread) +
                "; snapshot_candidate=" + shadow.Snapshot.Candidate +
                "; error=" + SafeR043(shadow.Error) +
                "; work_owner=PreloadBuilder" +
                "; tile_commit_authority=" +
                    (shadow.ProductionEnabled ? "EXACT_CPU" : "PQS") +
                "; production_authority=" +
                    (shadow.ProductionEnabled ? "EXACT_CPU" : "PQS") +
                "; producer_switch=" + BoolR043(shadow.ProductionEnabled) +
                "; db_authority=" +
                    (shadow.ProductionEnabled ? "EXACT_CPU" : "PQS") +
                "; exact_cpu_db_write=" + BoolR043(shadow.ProductionEnabled) +
                "; worker_runtime_object_access=false");
        }

        static void ComputeR043StockMapCoords(
            Vector3d direction,
            out double u,
            out double v)
        {
            double latitudeRad = Math.Asin(direction.y);
            if (double.IsNaN(latitudeRad))
                latitudeRad = Math.PI * 0.5;

            Vector3d directionXZ =
                new Vector3d(direction.x, 0.0, direction.z).normalized;
            double directionXZMagnitude = directionXZ.magnitude;
            double longitudeRad;
            if (directionXZMagnitude > 0.0)
            {
                double longitudeBasis =
                    directionXZ.x / directionXZMagnitude;
                longitudeRad = directionXZ.z < 0.0
                    ? Math.PI - Math.Asin(longitudeBasis)
                    : Math.Asin(longitudeBasis);
            }
            else
            {
                longitudeRad = 0.0;
            }

            u = longitudeRad / Math.PI * 0.5;
            v = latitudeRad / Math.PI + 0.5;
        }

        static bool FiniteR043(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static string SafeR043(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }

        static string BoolR043(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
