using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // AERIS43 / R042 Phase5 repaired worker parity proof.
    // Legal public PQS geodetic queries and deliberately out-of-range semantics
    // are separate contracts. Only the 560 legal samples/body participate in
    // the 3920 bit-exact worker witness.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR043Phase5LegalGeodeticWorkerParityObserver : MonoBehaviour
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
            internal double U;
            internal double V;
        }

        sealed class Check
        {
            internal string Label;
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
                    LogComplete(false, 0, 0, 0, false,
                        "CAPTURE_EXCEPTION_" + Safe(ex.GetType().Name + ":" + ex.Message));
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
                "r042-phase5-legal-geodetic-worker-parity",
                runtime.CaptureStamp(),
                context => EvaluatePayload(payload, expectedMainThread),
                value => CommitResult(value as Result),
                false);

            if (!accepted) return;

            submitted = true;
            AERISLogger.Info(
                "[AERIS43][R042_PHASE5_SUBMITTED]" +
                "; bodies=7" +
                "; legal_checks=3920" +
                "; out_of_range_checks=6" +
                "; lane=GeneralCompute" +
                "; commit_required=true" +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        Payload CapturePayload()
        {
            if (!ValidateOutOfRangeContract())
                return null;

            var bodies = new BodyPayload[TargetBodies.Length];
            int totalChecks = 0;

            AERISLogger.Info(
                "[AERIS43][R042_PHASE5_BEGIN]" +
                "; target_bodies=7" +
                "; legal_checks_per_body=560" +
                "; legal_total_checks=3920" +
                "; out_of_range_contract_checks=6" +
                "; legacy_census=565" +
                "; legacy_runtime_excluded=6" +
                "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());

            for (int i = 0; i < TargetBodies.Length; i++)
            {
                string bodyName = TargetBodies[i];
                CelestialBody body = FindBody(bodyName);
                if (body == null)
                {
                    LogCaptureFailure(bodyName, "BODY_NOT_FOUND");
                    return null;
                }

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

                List<Sample> legal = BuildLegalSamples(bodyName, modifierCount);
                if (legal.Count != 560)
                {
                    LogCaptureFailure(bodyName,
                        "LEGAL_SAMPLE_COUNT:" + legal.Count.ToString(CultureInfo.InvariantCulture));
                    return null;
                }

                var checks = new Check[560];
                for (int s = 0; s < legal.Count; s++)
                {
                    Sample sample = legal[s];
                    if (!IsLegalGeodetic(sample))
                    {
                        LogCaptureFailure(bodyName,
                            "LEGAL_SAMPLE_OUT_OF_RANGE:" + sample.Label);
                        return null;
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
                        double longitudeBasis = directionXZ.x / directionXZMagnitude;
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
                        LogCaptureFailure(bodyName,
                            "PUBLIC_PQS_REFERENCE_FAILED:" + sample.Label);
                        return null;
                    }

                    checks[s] = new Check
                    {
                        Label = sample.Label,
                        X = direction.x,
                        Y = direction.y,
                        Z = direction.z,
                        U = u,
                        V = v,
                        ExpectedAsl = expectedAsl,
                        ExpectedBits = BitConverter.DoubleToInt64Bits(expectedAsl)
                    };
                }

                bodies[i] = new BodyPayload
                {
                    BodyName = bodyName,
                    Snapshot = snapshot,
                    Checks = checks
                };
                totalChecks += checks.Length;

                AERISLogger.Info(
                    "[AERIS43][R042_PHASE5_CAPTURE_BODY]" +
                    "; body=" + Safe(bodyName) +
                    "; pass=true" +
                    "; legal_checks=560" +
                    "; out_of_range_checks=6" +
                    "; candidate=" + snapshot.Candidate +
                    "; modifier_count=" + modifierCount.ToString(CultureInfo.InvariantCulture) +
                    "; capture_thread_is_main=" + Bool(snapshot.CaptureThreadId == mainThreadId) +
                    "; runtime_object_retained=false" +
                    Invariants());
            }

            if (totalChecks != 3920)
            {
                LogCaptureFailure("ALL",
                    "TOTAL_CHECK_COUNT:" + totalChecks.ToString(CultureInfo.InvariantCulture));
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
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId,
                Bodies = new BodyResult[payload.Bodies.Length]
            };

            try
            {
                if (payload.MainThreadId != expectedMainThread)
                    throw new InvalidOperationException("MAIN_THREAD_ID_PAYLOAD_CHANGED");

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
                            absolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
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

                        double actualAsl = absolute - body.Snapshot.PqsRadius;
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
                LogComplete(false, 0, 0, 0, false, "NULL_WORKER_RESULT");
                return;
            }

            bool workerNotMain = result.WorkerThreadId > 0 &&
                result.WorkerThreadId != mainThreadId;
            bool pass = string.IsNullOrEmpty(result.Failure) &&
                result.Bodies != null &&
                result.Bodies.Length == 7 &&
                result.TotalChecks == 3920 &&
                result.TotalMismatches == 0 &&
                workerNotMain;

            if (result.Bodies != null)
            {
                for (int i = 0; i < result.Bodies.Length; i++)
                {
                    BodyResult body = result.Bodies[i];
                    if (body == null)
                    {
                        pass = false;
                        continue;
                    }

                    bool bodyPass = body.Checks == 560 && body.Mismatches == 0;
                    pass &= bodyPass;

                    AERISLogger.Info(
                        "[AERIS43][R042_PHASE5_BODY]" +
                        "; body=" + Safe(body.BodyName) +
                        "; pass=" + Bool(bodyPass) +
                        "; checks=" + body.Checks.ToString(CultureInfo.InvariantCulture) +
                        "; mismatch_count=" + body.Mismatches.ToString(CultureInfo.InvariantCulture) +
                        "; bit_exact=" + Bool(body.Mismatches == 0) +
                        "; max_abs_error=" + R(body.MaxAbsError) +
                        "; first_mismatch=" + Safe(body.FirstMismatchLabel) +
                        "; first_expected_bits=" +
                            unchecked((ulong)body.FirstExpectedBits).ToString("x16", CultureInfo.InvariantCulture) +
                        "; first_actual_bits=" +
                            unchecked((ulong)body.FirstActualBits).ToString("x16", CultureInfo.InvariantCulture) +
                        "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                        "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                        "; worker_not_main=" + Bool(workerNotMain) +
                        Invariants());
                }
            }

            LogComplete(pass, result.TotalChecks, result.TotalMismatches,
                result.WorkerThreadId, workerNotMain, result.Failure);
        }

        void LogComplete(bool pass, int totalChecks, int mismatches,
            int workerThreadId, bool workerNotMain, string failure)
        {
            AERISLogger.Info(
                "[AERIS43][R042_PHASE5_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; bodies=7" +
                "; total_checks=" + totalChecks.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" + Bool(mismatches == 0 && totalChecks == 3920) +
                "; worker_thread_id=" + workerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                "; worker_runtime_object_access=false" +
                "; failure=" + Safe(failure) +
                Invariants());
        }

        void LogCaptureFailure(string bodyName, string failure)
        {
            AERISLogger.Info(
                "[AERIS43][R042_PHASE5_COMPLETE]" +
                "; pass=false" +
                "; body=" + Safe(bodyName) +
                "; failure=" + Safe(failure) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        bool ValidateOutOfRangeContract()
        {
            List<Sample> samples = BuildOutOfRangeSamples();
            if (samples.Count != 6)
            {
                LogCaptureFailure("ALL",
                    "OUT_OF_RANGE_SAMPLE_COUNT:" + samples.Count.ToString(CultureInfo.InvariantCulture));
                return false;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                Sample sample = samples[i];
                if (IsLegalGeodetic(sample))
                {
                    LogCaptureFailure("ALL", "OUT_OF_RANGE_SAMPLE_BECAME_LEGAL:" + sample.Label);
                    return false;
                }

                AERISLogger.Info(
                    "[AERIS43][R042_PHASE5_OUT_OF_RANGE_CONTRACT]" +
                    "; label=" + Safe(sample.Label) +
                    "; latitude=" + R(sample.Latitude) +
                    "; longitude=" + R(sample.Longitude) +
                    "; latitude_bits=0x" +
                        unchecked((ulong)BitConverter.DoubleToInt64Bits(sample.Latitude))
                            .ToString("x16", CultureInfo.InvariantCulture) +
                    "; longitude_bits=0x" +
                        unchecked((ulong)BitConverter.DoubleToInt64Bits(sample.Longitude))
                            .ToString("x16", CultureInfo.InvariantCulture) +
                    "; public_pqs_query=false" +
                    "; worker_parity=false" +
                    Invariants());
            }

            AERISLogger.Info(
                "[AERIS43][R042_PHASE5_OUT_OF_RANGE_SUITE]" +
                "; pass=true" +
                "; checks=6" +
                "; semantics=CLASSIFICATION_ONLY" +
                "; public_pqs_query=false" +
                "; worker_parity=false" +
                Invariants());
            return true;
        }

        static List<Sample> BuildLegalSamples(string bodyName, int modifierCount)
        {
            var result = new List<Sample>(560);
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
                    AddSample(result, seen,
                        "BODY_COORD_" + lat.ToString("D2", CultureInfo.InvariantCulture) + "_" +
                        lon.ToString("D2", CultureInfo.InvariantCulture),
                        longitudes[lon] / 360.0 + 0.5,
                        latitudes[lat] / 180.0 + 0.5);
                }
            }

            const int nominalWidth = 4096;
            const int nominalHeight = 2048;
            int[] xs = DistinctIndices(nominalWidth);
            int[] ys = DistinctIndices(nominalHeight);

            for (int i = 0; i < xs.Length; i++)
            {
                double axisValue = xs[i] / (double)nominalWidth;
                AddLegalBoundarySamples(result, seen,
                    "BOUNDARY_X_" + xs[i].ToString(CultureInfo.InvariantCulture),
                    axisValue, 0.37109375, true);
            }

            for (int i = 0; i < ys.Length; i++)
            {
                double axisValue = ys[i] / (double)nominalHeight;
                AddLegalBoundarySamples(result, seen,
                    "BOUNDARY_Y_" + ys[i].ToString(CultureInfo.InvariantCulture),
                    axisValue, 0.62890625, false);
            }

            uint state = Seed(bodyName, modifierCount);
            for (int i = 0; i < 384; i++)
            {
                state = Next(state);
                double u = state / 4294967296.0;
                state = Next(state);
                double v = state / 4294967296.0;
                AddSample(result, seen,
                    "RANDOM_" + i.ToString("D3", CultureInfo.InvariantCulture), u, v);
            }

            AddSample(result, seen, "LEGAL_INTERIOR_ULP_CANARY",
                NextDoubleUp(0.3125), NextDoubleDown(0.6875));

            return result;
        }

        static List<Sample> BuildOutOfRangeSamples()
        {
            var result = new List<Sample>(6);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            AddSample(result, seen, "BOUNDARY_X_4096_UP", NextDoubleUp(1.0), 0.37109375);
            AddSample(result, seen, "BOUNDARY_Y_2048_UP", 0.62890625, NextDoubleUp(1.0));
            AddSample(result, seen, "PERIODIC_NEG_U", -0.125, 0.375);
            AddSample(result, seen, "PERIODIC_POS_U", 1.125, 0.375);
            AddSample(result, seen, "PERIODIC_NEG_V", 0.625, -0.125);
            AddSample(result, seen, "PERIODIC_POS_V", 0.625, 1.125);
            return result;
        }

        static void AddLegalBoundarySamples(List<Sample> result, HashSet<string> seen,
            string label, double axisValue, double otherValue, bool xAxis)
        {
            double down = NextDoubleDown(axisValue);
            double up = NextDoubleUp(axisValue);
            if (xAxis)
            {
                AddSample(result, seen, label + "_EXACT", axisValue, otherValue);
                AddSample(result, seen, label + "_DOWN", down, otherValue);
                if (axisValue < 1.0)
                    AddSample(result, seen, label + "_UP", up, otherValue);
            }
            else
            {
                AddSample(result, seen, label + "_EXACT", otherValue, axisValue);
                AddSample(result, seen, label + "_DOWN", otherValue, down);
                if (axisValue < 1.0)
                    AddSample(result, seen, label + "_UP", otherValue, up);
            }
        }

        static void AddSample(List<Sample> result, HashSet<string> seen,
            string label, double u, double v)
        {
            string key =
                BitConverter.DoubleToInt64Bits(u).ToString("x16", CultureInfo.InvariantCulture) + ":" +
                BitConverter.DoubleToInt64Bits(v).ToString("x16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;

            result.Add(new Sample
            {
                Label = label,
                Latitude = (v - 0.5) * 180.0,
                Longitude = (u - 0.5) * 360.0,
                U = u,
                V = v
            });
        }

        static bool IsLegalGeodetic(Sample sample)
        {
            return sample != null &&
                sample.Latitude >= -90.0 && sample.Latitude <= 90.0 &&
                sample.Longitude >= -180.0 && sample.Longitude <= 180.0;
        }

        static int[] DistinctIndices(int dimension)
        {
            int[] candidates =
            {
                0, 1, dimension / 4, dimension / 3, dimension / 2,
                dimension * 2 / 3, dimension * 3 / 4,
                Math.Max(0, dimension - 2), Math.Max(0, dimension - 1), dimension
            };
            var values = new List<int>(candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
                if (!values.Contains(candidates[i])) values.Add(candidates[i]);
            return values.ToArray();
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
                return BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000001UL));
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
                "; legal_geodetic_contract=560_PER_BODY" +
                "; out_of_range_contract=SEPARATE_6" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; build_source_git_sha=" + Safe(global::AERISFlightControl.AERISBuildVersion.SourceGitSha) +
                "; build_candidate=" + Safe(global::AERISFlightControl.AERISBuildVersion.CandidateName);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    }
}
