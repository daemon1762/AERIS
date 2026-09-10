using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // R042 Phase 5 worker execution parity proof.
    //
    // Main thread:
    // - captures the already-certified immutable R042 runtime snapshot;
    // - samples the public AERIS/PQS TerrainAltitude authority;
    // - reconstructs stock PQS direction/map coordinates exactly as R041 V5;
    // - copies only primitive scalar checks to the worker payload.
    //
    // Worker:
    // - runs only accepted pure CLR evaluators through the shared AERIS scheduler;
    // - compares public ASL doubles bit-for-bit;
    // - never touches CelestialBody/PQS/PQSMod/Unity runtime objects.
    //
    // This phase is observation-only. PQS remains production/DB authority.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR042ExactCpuShadowWorkerParityObserver : MonoBehaviour
    {
        static readonly string[] TargetBodies =
        {
            "Minmus", "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        sealed class Sample
        {
            internal string Label;
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
            internal double U;
            internal double V;
        }

        sealed class Check
        {
            internal string Label;
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
            internal double U;
            internal double V;
            internal double ExpectedAsl;
            internal long ExpectedBits;
        }

        sealed class BodyPayload
        {
            internal string BodyName;
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal Check[] Checks;
        }

        sealed class Payload
        {
            internal int MainThreadId;
            internal BodyPayload[] Bodies;
        }

        sealed class BodyResult
        {
            internal string BodyName;
            internal int Checks;
            internal int Mismatches;
            internal double MaxAbsError;
            internal long FirstExpectedBits;
            internal long FirstActualBits;
            internal string FirstMismatchLabel;
        }

        sealed class Result
        {
            internal int MainThreadId;
            internal int WorkerThreadId;
            internal BodyResult[] Bodies;
            internal int TotalChecks;
            internal int TotalMismatches;
            internal double MaxAbsError;
            internal string Failure;
        }

        int mainThreadId;
        bool captureStarted;
        bool submitted;
        bool reported;
        float nextAttempt;
        Payload pendingPayload;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (reported) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 0.5f;

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            if (!captureStarted)
            {
                captureStarted = true;
                try
                {
                    pendingPayload = CapturePayload();
                }
                catch (Exception ex)
                {
                    reported = true;
                    AERISLogger.Info(
                        "[AERIS42][R042_PHASE5_COMPLETE]" +
                        "; pass=false" +
                        "; failure=CAPTURE_EXCEPTION_" + Safe(ex.GetType().Name + ":" + ex.Message) +
                        Invariants());
                    return;
                }
                if (pendingPayload == null)
                {
                    reported = true;
                    return;
                }
            }

            if (submitted) return;

            Payload payload = pendingPayload;
            int expectedMainThread = mainThreadId;
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute,
                "r042-phase5-worker-parity",
                runtime.CaptureStamp(),
                context => EvaluatePayload(payload, expectedMainThread),
                value => CommitResult(value as Result),
                false);

            if (!accepted) return;

            submitted = true;
            AERISLogger.Info(
                "[AERIS42][R042_PHASE5_SUBMITTED]" +
                "; bodies=7" +
                "; checks=3920" +
                "; lane=GeneralCompute" +
                "; commit_required=true" +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        Payload CapturePayload()
        {
            var bodies = new BodyPayload[TargetBodies.Length];
            int totalChecks = 0;

            AERISLogger.Info(
                "[AERIS42][R042_PHASE5_BEGIN]" +
                "; target_bodies=7" +
                "; expected_checks_per_body=560" +
                "; expected_total_checks=3920" +
                "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                "; reference_input_order=LATITUDE_THEN_LONGITUDE_STOCK_IL" +
                "; terrainaltitude_semantics=PQS_RADIUS_CLAMP_NEGATIVE_TO_ZERO" +
                "; terrainaltitude_initial_vert_height=PQS_RADIUS" +
                "; terrainaltitude_uv_source=STOCK_PQS_BUILDVERTEXMAPCOORDS" +
                "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());

            for (int i = 0; i < TargetBodies.Length; i++)
            {
                string bodyName = TargetBodies[i];
                CelestialBody body = FindBody(bodyName);
                AERISR042ExactCpuShadowRuntimeSnapshot snapshot;
                string failure;
                if (!AERISR042ExactCpuShadowRuntimeSnapshotBuilder.TryCapture(
                    body, mainThreadId, out snapshot, out failure))
                {
                    LogCaptureFailure(bodyName, "SNAPSHOT:" + failure);
                    return null;
                }

                int modifierCount = snapshot.Candidate ==
                    AERISR042ExactCpuShadowSourceResolver.CandidateKind.R041HeightModifierChain
                    ? snapshot.HeightChainSnapshot.Ops.Length
                    : 1;

                List<Sample> census = BuildSamples(bodyName, modifierCount);
                var checks = new List<Check>(560);

                for (int s = 0; s < census.Count; s++)
                {
                    Sample sample = census[s];
                    if (sample.Latitude < -90.0 || sample.Latitude > 90.0 ||
                        sample.Longitude < -180.0 || sample.Longitude > 180.0)
                    {
                        AERISLogger.Info(
                            "[AERIS42][R042_PHASE5_GEODETIC_EXCLUDED]" +
                            "; body=" + Safe(bodyName) +
                            "; label=" + Safe(sample.Label) +
                            "; latitude=" + R(sample.Latitude) +
                            "; longitude=" + R(sample.Longitude) +
                            "; latitude_bits=0x" +
                                unchecked((ulong)BitConverter.DoubleToInt64Bits(sample.Latitude))
                                    .ToString("x16", CultureInfo.InvariantCulture) +
                            "; longitude_bits=0x" +
                                unchecked((ulong)BitConverter.DoubleToInt64Bits(sample.Longitude))
                                    .ToString("x16", CultureInfo.InvariantCulture) +
                            Invariants());
                        continue;
                    }

                    Vector3d inputDirection = body.GetRelSurfaceNVector(
                        sample.Latitude, sample.Longitude);
                    Vector3d direction = inputDirection.normalized;

                    double latitudeRad = Math.Asin(direction.y);
                    if (double.IsNaN(latitudeRad))
                        latitudeRad = Math.PI * 0.5;

                    Vector3d directionXZ = new Vector3d(
                        direction.x, 0.0, direction.z).normalized;
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

                    double u = longitudeRad / Math.PI * 0.5;
                    double v = latitudeRad / Math.PI + 0.5;

                    double expectedAsl;
                    if (!AERISTerrainAwareness.TrySampleTerrainAslShared(
                        body, sample.Latitude, sample.Longitude, out expectedAsl))
                    {
                        LogCaptureFailure(
                            bodyName,
                            "PUBLIC_PQS_REFERENCE_FAILED:" + sample.Label);
                        return null;
                    }

                    checks.Add(new Check
                    {
                        Label = sample.Label,
                        Latitude = sample.Latitude,
                        Longitude = sample.Longitude,
                        X = direction.x,
                        Y = direction.y,
                        Z = direction.z,
                        U = u,
                        V = v,
                        ExpectedAsl = expectedAsl,
                        ExpectedBits = BitConverter.DoubleToInt64Bits(expectedAsl)
                    });
                }

                if (checks.Count != 560)
                {
                    LogCaptureFailure(
                        bodyName,
                        "GEODETIC_SAMPLE_COUNT:" +
                        checks.Count.ToString(CultureInfo.InvariantCulture));
                    return null;
                }

                bodies[i] = new BodyPayload
                {
                    BodyName = bodyName,
                    Snapshot = snapshot,
                    Checks = checks.ToArray()
                };
                totalChecks += checks.Count;

                AERISLogger.Info(
                    "[AERIS42][R042_PHASE5_CAPTURE_BODY]" +
                    "; body=" + Safe(bodyName) +
                    "; pass=true" +
                    "; census_checks=565" +
                    "; geodetic_checks=560" +
                    "; candidate=" + snapshot.Candidate +
                    "; modifier_count=" + modifierCount.ToString(CultureInfo.InvariantCulture) +
                    "; capture_thread_is_main=" +
                        Bool(snapshot.CaptureThreadId == mainThreadId) +
                    "; runtime_object_retained=false" +
                    Invariants());
            }

            if (totalChecks != 3920)
            {
                LogCaptureFailure(
                    "ALL",
                    "TOTAL_CHECK_COUNT:" +
                    totalChecks.ToString(CultureInfo.InvariantCulture));
                return null;
            }

            return new Payload
            {
                MainThreadId = mainThreadId,
                Bodies = bodies
            };
        }

        static Result EvaluatePayload(Payload payload, int expectedMainThread)
        {
            var result = new Result
            {
                MainThreadId = expectedMainThread,
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId,
                Bodies = new BodyResult[payload.Bodies.Length]
            };

            try
            {
                for (int i = 0; i < payload.Bodies.Length; i++)
                {
                    BodyPayload body = payload.Bodies[i];
                    var bodyResult = new BodyResult
                    {
                        BodyName = body.BodyName,
                        Checks = body.Checks.Length
                    };

                    for (int c = 0; c < body.Checks.Length; c++)
                    {
                        Check check = body.Checks[c];
                        double absolute;

                        if (body.Snapshot.Candidate ==
                            AERISR042ExactCpuShadowSourceResolver.CandidateKind.R039MinmusVertexPlanet)
                        {
                            absolute = AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                                body.Snapshot.MinmusSnapshot,
                                check.X, check.Y, check.Z,
                                body.Snapshot.PqsRadius);
                        }
                        else if (body.Snapshot.Candidate ==
                            AERISR042ExactCpuShadowSourceResolver.CandidateKind.R041HeightModifierChain)
                        {
                            absolute =
                                AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                                    body.Snapshot.HeightChainSnapshot,
                                    check.X, check.Y, check.Z,
                                    check.U, check.V,
                                    body.Snapshot.PqsRadius);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                body.BodyName + "_UNSUPPORTED_CANDIDATE");
                        }

                        double rawAsl = absolute - body.Snapshot.PqsRadius;
                        double actualAsl = rawAsl;
                        if (actualAsl < 0.0)
                            actualAsl = 0.0;

                        long actualBits = BitConverter.DoubleToInt64Bits(actualAsl);
                        double error = Math.Abs(actualAsl - check.ExpectedAsl);
                        if (error > bodyResult.MaxAbsError)
                            bodyResult.MaxAbsError = error;
                        if (error > result.MaxAbsError)
                            result.MaxAbsError = error;

                        if (actualBits != check.ExpectedBits)
                        {
                            bodyResult.Mismatches++;
                            result.TotalMismatches++;
                            if (string.IsNullOrEmpty(bodyResult.FirstMismatchLabel))
                            {
                                bodyResult.FirstMismatchLabel = check.Label;
                                bodyResult.FirstExpectedBits = check.ExpectedBits;
                                bodyResult.FirstActualBits = actualBits;
                            }
                        }

                        result.TotalChecks++;
                    }

                    result.Bodies[i] = bodyResult;
                }
            }
            catch (Exception ex)
            {
                result.Failure = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }

            return result;
        }

        void CommitResult(Result result)
        {
            if (reported) return;
            reported = true;

            if (result == null)
            {
                AERISLogger.Info(
                    "[AERIS42][R042_PHASE5_COMPLETE]" +
                    "; pass=false" +
                    "; failure=NULL_WORKER_RESULT" +
                    Invariants());
                return;
            }

            bool workerNotMain =
                result.WorkerThreadId > 0 &&
                result.WorkerThreadId != mainThreadId;
            bool pass =
                string.IsNullOrEmpty(result.Failure) &&
                result.Bodies != null &&
                result.Bodies.Length == TargetBodies.Length &&
                result.TotalChecks == 3920 &&
                result.TotalMismatches == 0 &&
                workerNotMain;

            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult body = result.Bodies[i];
                if (body == null)
                {
                    pass = false;
                    continue;
                }

                bool bodyPass =
                    body.Checks == 560 &&
                    body.Mismatches == 0;

                pass &= bodyPass;

                AERISLogger.Info(
                    "[AERIS42][R042_PHASE5_BODY]" +
                    "; body=" + Safe(body.BodyName) +
                    "; pass=" + Bool(bodyPass) +
                    "; checks=" + body.Checks.ToString(CultureInfo.InvariantCulture) +
                    "; mismatch_count=" +
                        body.Mismatches.ToString(CultureInfo.InvariantCulture) +
                    "; bit_exact=" + Bool(body.Mismatches == 0) +
                    "; max_abs_error=" + R(body.MaxAbsError) +
                    "; first_mismatch=" + Safe(body.FirstMismatchLabel) +
                    "; first_expected_bits=" +
                        body.FirstExpectedBits.ToString("x16", CultureInfo.InvariantCulture) +
                    "; first_actual_bits=" +
                        body.FirstActualBits.ToString("x16", CultureInfo.InvariantCulture) +
                    "; worker_thread_id=" +
                        result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_thread_id=" +
                        mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; worker_not_main=" + Bool(workerNotMain) +
                    Invariants());
            }

            AERISLogger.Info(
                "[AERIS42][R042_PHASE5_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; bodies=" +
                    (result.Bodies == null ? "0" :
                        result.Bodies.Length.ToString(CultureInfo.InvariantCulture)) +
                "; total_checks=" +
                    result.TotalChecks.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" +
                    result.TotalMismatches.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" + Bool(result.TotalMismatches == 0) +
                "; max_abs_error=" + R(result.MaxAbsError) +
                "; worker_thread_id=" +
                    result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                "; snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY" +
                "; worker_runtime_object_access=false" +
                "; failure=" + Safe(result.Failure) +
                Invariants());
        }

        void LogCaptureFailure(string bodyName, string failure)
        {
            AERISLogger.Info(
                "[AERIS42][R042_PHASE5_COMPLETE]" +
                "; pass=false" +
                "; body=" + Safe(bodyName) +
                "; failure=" + Safe(failure) +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        static List<Sample> BuildSamples(string bodyName, int modifierCount)
        {
            var result = new List<Sample>(700);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            double[] latitudes =
            {
                -90.0, -75.0, -60.0, -45.0, -30.0, -15.0, 0.0,
                15.0, 30.0, 45.0, 60.0, 75.0, 90.0
            };
            double[] longitudes =
            {
                -180.0, -135.0, -90.0, -45.0, 0.0,
                45.0, 90.0, 135.0, 180.0
            };

            for (int lat = 0; lat < latitudes.Length; lat++)
            {
                for (int lon = 0; lon < longitudes.Length; lon++)
                {
                    double u = longitudes[lon] / 360.0 + 0.5;
                    double v = latitudes[lat] / 180.0 + 0.5;
                    AddSample(
                        result, seen,
                        "BODY_COORD_" +
                            lat.ToString("D2", CultureInfo.InvariantCulture) + "_" +
                            lon.ToString("D2", CultureInfo.InvariantCulture),
                        u, v);
                }
            }

            const int nominalWidth = 4096;
            const int nominalHeight = 2048;
            int[] xs = DistinctIndices(nominalWidth);
            int[] ys = DistinctIndices(nominalHeight);

            for (int i = 0; i < xs.Length; i++)
            {
                double axisValue = xs[i] / (double)nominalWidth;
                AddBoundarySamples(
                    result, seen,
                    "BOUNDARY_X_" +
                        xs[i].ToString(CultureInfo.InvariantCulture),
                    axisValue, 0.37109375, true);
            }

            for (int i = 0; i < ys.Length; i++)
            {
                double axisValue = ys[i] / (double)nominalHeight;
                AddBoundarySamples(
                    result, seen,
                    "BOUNDARY_Y_" +
                        ys[i].ToString(CultureInfo.InvariantCulture),
                    axisValue, 0.62890625, false);
            }

            uint state = Seed(bodyName, modifierCount);
            for (int i = 0; i < 384; i++)
            {
                state = Next(state);
                double u = state / 4294967296.0;
                state = Next(state);
                double v = state / 4294967296.0;
                AddSample(
                    result, seen,
                    "RANDOM_" + i.ToString("D3", CultureInfo.InvariantCulture),
                    u, v);
            }

            AddSample(result, seen, "PERIODIC_NEG_U", -0.125, 0.375);
            AddSample(result, seen, "PERIODIC_POS_U", 1.125, 0.375);
            AddSample(result, seen, "PERIODIC_NEG_V", 0.625, -0.125);
            AddSample(result, seen, "PERIODIC_POS_V", 0.625, 1.125);

            if (result.Count != 565)
                throw new InvalidOperationException(
                    "R041_SAMPLE_COUNT_" +
                    result.Count.ToString(CultureInfo.InvariantCulture));

            return result;
        }

        static int[] DistinctIndices(int dimension)
        {
            int[] candidates =
            {
                0,
                1,
                dimension / 4,
                dimension / 3,
                dimension / 2,
                dimension * 2 / 3,
                dimension * 3 / 4,
                Math.Max(0, dimension - 2),
                Math.Max(0, dimension - 1),
                dimension
            };

            var values = new List<int>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                int value = candidates[i];
                if (!values.Contains(value))
                    values.Add(value);
            }
            return values.ToArray();
        }

        static void AddBoundarySamples(
            List<Sample> result,
            HashSet<string> seen,
            string label,
            double axisValue,
            double otherValue,
            bool xAxis)
        {
            double down = NextDoubleDown(axisValue);
            double up = NextDoubleUp(axisValue);
            if (xAxis)
            {
                AddSample(result, seen, label + "_EXACT", axisValue, otherValue);
                AddSample(result, seen, label + "_DOWN", down, otherValue);
                AddSample(result, seen, label + "_UP", up, otherValue);
            }
            else
            {
                AddSample(result, seen, label + "_EXACT", otherValue, axisValue);
                AddSample(result, seen, label + "_DOWN", otherValue, down);
                AddSample(result, seen, label + "_UP", otherValue, up);
            }
        }

        static void AddSample(
            List<Sample> result,
            HashSet<string> seen,
            string label,
            double u,
            double v)
        {
            string key =
                BitConverter.DoubleToInt64Bits(u).ToString("x16", CultureInfo.InvariantCulture) +
                ":" +
                BitConverter.DoubleToInt64Bits(v).ToString("x16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;

            double latitude = (v - 0.5) * 180.0;
            double longitude = (u - 0.5) * 360.0;
            double latRad = latitude * Math.PI / 180.0;
            double lonRad = longitude * Math.PI / 180.0;
            double cosLat = Math.Cos(latRad);

            result.Add(new Sample
            {
                Label = label,
                Latitude = latitude,
                Longitude = longitude,
                X = cosLat * Math.Cos(lonRad),
                Y = Math.Sin(latRad),
                Z = cosLat * Math.Sin(lonRad),
                U = u,
                V = v
            });
        }

        static uint Seed(string bodyName, int modifierCount)
        {
            uint h = 2166136261u;
            string value = bodyName ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                h ^= value[i];
                h = unchecked(h * 16777619u);
            }
            h ^= unchecked((uint)modifierCount * 0x9E3779B9u);
            return h == 0 ? 1u : h;
        }

        static uint Next(uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        static double NextDoubleUp(double value)
        {
            if (double.IsNaN(value) || value == double.PositiveInfinity) return value;
            if (value == 0.0) return BitConverter.Int64BitsToDouble(1L);
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? 1L : -1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        static double NextDoubleDown(double value)
        {
            if (double.IsNaN(value) || value == double.NegativeInfinity) return value;
            if (value == 0.0)
                return BitConverter.Int64BitsToDouble(
                    unchecked((long)0x8000000000000001UL));
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? -1L : 1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null) continue;
                if (string.Equals(body.name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(body.bodyName, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
        }

        static string Invariants()
        {
            return
                "; snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; db_write=false" +
                "; preload_mutation=false";
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
