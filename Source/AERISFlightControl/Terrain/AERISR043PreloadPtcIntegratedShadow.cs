using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // R043 PRELOAD_PTC Phase2.
    //
    // This partial augments the real AERISTerrainBlockPipeline without changing
    // production terrain authority. Only PreloadBuilder-owned requests are eligible.
    //
    // Main thread:
    // - certifies/caches the R042 immutable pure snapshot;
    // - keeps PQS/TerrainAwareness as the authority sampled into the existing tile;
    // - reconstructs stock PQS direction/map coordinates and copies primitives.
    //
    // GeneralCompute worker:
    // - evaluates the accepted pure CLR exact source from immutable data only;
    // - compares the PQS authority double bit-for-bit;
    // - never writes Elevation/Flags/DB state and never touches Unity/KSP runtime objects.
    //
    // Commit:
    // - aggregates shadow evidence only;
    // - the existing PQS-derived float tile remains the only committed tile payload.
    internal sealed partial class AERISTerrainBlockPipeline
    {
        sealed class R043ShadowTileState
        {
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal string BodyName = string.Empty;
            internal string PqsHash = string.Empty;
            internal bool ProofRequest;
            internal int ExpectedSamples;
            internal int Samples;
            internal int Mismatches;
            internal int NonFinite;
            internal double MaxAbsError;
            internal int Blocks;
            internal bool AllWorkersOffMainThread = true;
            internal string Error = string.Empty;

            internal double[] SamplingExpected;
            internal double[] SamplingX;
            internal double[] SamplingY;
            internal double[] SamplingZ;
            internal double[] SamplingU;
            internal double[] SamplingV;
            internal byte[] SamplingActive;
        }

        sealed class R043ShadowBlockPayload
        {
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal string BodyName = string.Empty;
            internal double[] Expected;
            internal double[] X;
            internal double[] Y;
            internal double[] Z;
            internal double[] U;
            internal double[] V;
            internal byte[] Active;
            internal int MainThreadId;
            internal int WorkerThreadId;
            internal int Samples;
            internal int Mismatches;
            internal int NonFinite;
            internal double MaxAbsError;
            internal string Error = string.Empty;
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

        R043ShadowTileState TryCreateR043ShadowState(
            CelestialBody body,
            AERISTerrainTileRequest request,
            string pqsHash)
        {
            if (body == null || request == null ||
                request.WorkOwner != AERISTerrainWorkOwner.PreloadBuilder)
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
                    "; production_authority=PQS" +
                    "; producer_switch=false" +
                    "; db_authority=PQS" +
                    "; exact_cpu_db_write=false" +
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

            return new R043ShadowTileState
            {
                Snapshot = snapshot,
                BodyName = bodyName,
                PqsHash = authorityHash,
                ProofRequest = IsR043ProofRequest(request),
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

        static bool IsR043ProofRequest(AERISTerrainTileRequest request)
        {
            return request != null &&
                request.Key.EnvironmentHash != null &&
                request.Key.EnvironmentHash.StartsWith(
                    "R043_INTEGRATION_PROOF:",
                    StringComparison.Ordinal);
        }

        static void BeginR043ShadowBlock(TileState state, int count)
        {
            if (state == null || state.R043Shadow == null || count <= 0) return;
            R043ShadowTileState shadow = state.R043Shadow;
            shadow.SamplingExpected = new double[count];
            shadow.SamplingX = new double[count];
            shadow.SamplingY = new double[count];
            shadow.SamplingZ = new double[count];
            shadow.SamplingU = new double[count];
            shadow.SamplingV = new double[count];
            shadow.SamplingActive = new byte[count];
        }

        static void CaptureR043ShadowSample(
            TileState state,
            int local,
            double latitude,
            double longitude,
            double expectedAsl)
        {
            if (state == null || state.R043Shadow == null) return;
            R043ShadowTileState shadow = state.R043Shadow;
            if (shadow.SamplingExpected == null ||
                shadow.SamplingX == null ||
                shadow.SamplingY == null ||
                shadow.SamplingZ == null ||
                shadow.SamplingU == null ||
                shadow.SamplingV == null ||
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

                Vector3d inputDirection = state.Body.GetRelSurfaceNVector(
                    latitude, longitude);
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
                shadow.SamplingActive[local] = 1;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(shadow.Error))
                    shadow.Error = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }
        }

        static R043ShadowBlockPayload BuildR043ShadowBlockPayload(TileState state)
        {
            if (state == null || state.R043Shadow == null) return null;
            R043ShadowTileState shadow = state.R043Shadow;
            return new R043ShadowBlockPayload
            {
                Snapshot = shadow.Snapshot,
                BodyName = shadow.BodyName,
                Expected = shadow.SamplingExpected,
                X = shadow.SamplingX,
                Y = shadow.SamplingY,
                Z = shadow.SamplingZ,
                U = shadow.SamplingU,
                V = shadow.SamplingV,
                Active = shadow.SamplingActive,
                MainThreadId = shadow.Snapshot == null ?
                    0 : shadow.Snapshot.CaptureThreadId,
                Error = shadow.Error
            };
        }

        static void ReleaseR043SamplingBlock(TileState state)
        {
            if (state == null || state.R043Shadow == null) return;
            R043ShadowTileState shadow = state.R043Shadow;
            shadow.SamplingExpected = null;
            shadow.SamplingX = null;
            shadow.SamplingY = null;
            shadow.SamplingZ = null;
            shadow.SamplingU = null;
            shadow.SamplingV = null;
            shadow.SamplingActive = null;
        }

        static void ProcessR043ShadowBlock(R043ShadowBlockPayload block)
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
                    block.U == null || block.V == null || block.Active == null)
                    throw new InvalidOperationException("PAYLOAD_ARRAY_NULL");

                int count = block.Expected.Length;
                if (block.X.Length != count ||
                    block.Y.Length != count ||
                    block.Z.Length != count ||
                    block.U.Length != count ||
                    block.V.Length != count ||
                    block.Active.Length != count)
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
                    double expectedAsl = block.Expected[i];

                    bool finite =
                        FiniteR043(actualAsl) && FiniteR043(expectedAsl);
                    double error = finite ?
                        Math.Abs(actualAsl - expectedAsl) :
                        double.PositiveInfinity;
                    if (error > block.MaxAbsError)
                        block.MaxAbsError = error;

                    block.Samples++;
                    if (!finite)
                        block.NonFinite++;

                    if (!finite ||
                        BitConverter.DoubleToInt64Bits(actualAsl) !=
                            BitConverter.DoubleToInt64Bits(expectedAsl))
                        block.Mismatches++;
                }
            }
            catch (Exception ex)
            {
                block.Error = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }
        }

        static void CommitR043ShadowBlock(
            TileState state,
            R043ShadowBlockPayload block)
        {
            if (state == null || state.R043Shadow == null || block == null) return;
            R043ShadowTileState shadow = state.R043Shadow;
            shadow.Blocks++;
            shadow.Samples += block.Samples;
            shadow.Mismatches += block.Mismatches;
            shadow.NonFinite += block.NonFinite;
            if (block.MaxAbsError > shadow.MaxAbsError)
                shadow.MaxAbsError = block.MaxAbsError;
            shadow.AllWorkersOffMainThread =
                shadow.AllWorkersOffMainThread &&
                block.WorkerThreadId > 0 &&
                block.WorkerThreadId != block.MainThreadId;
            if (string.IsNullOrEmpty(shadow.Error) &&
                !string.IsNullOrEmpty(block.Error))
                shadow.Error = block.Error;
        }

        void ReportR043ShadowTile(TileState state)
        {
            if (state == null || state.Request == null ||
                state.R043Shadow == null) return;

            R043ShadowTileState shadow = state.R043Shadow;
            bool pass =
                shadow.Snapshot != null &&
                shadow.Snapshot.IsStructurallyValid &&
                shadow.Samples == shadow.ExpectedSamples &&
                shadow.Mismatches == 0 &&
                shadow.NonFinite == 0 &&
                shadow.AllWorkersOffMainThread &&
                string.IsNullOrEmpty(shadow.Error);

            bool emit = shadow.ProofRequest || !pass ||
                r043NaturalBodiesReported.Add(shadow.BodyName);
            if (!emit) return;

            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_INTEGRATED_TILE]" +
                "; pass=" + BoolR043(pass) +
                "; proof_request=" + BoolR043(shadow.ProofRequest) +
                "; body=" + SafeR043(shadow.BodyName) +
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
                "; nonfinite_count=" +
                    shadow.NonFinite.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" +
                    BoolR043(shadow.Mismatches == 0 && shadow.NonFinite == 0) +
                "; max_abs_error=" +
                    shadow.MaxAbsError.ToString("R", CultureInfo.InvariantCulture) +
                "; blocks=" +
                    shadow.Blocks.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" +
                    BoolR043(shadow.AllWorkersOffMainThread) +
                "; snapshot_candidate=" + shadow.Snapshot.Candidate +
                "; error=" + SafeR043(shadow.Error) +
                "; work_owner=PreloadBuilder" +
                "; tile_commit_authority=PQS" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; exact_cpu_db_write=false" +
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
