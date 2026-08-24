using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS34_REV3_5_R039_MINMUS_PURE_CPU_EXACT_CERTIFIED
    //
    // Main thread:
    //   - finds Minmus / runtime VertexPlanet,
    //   - verifies the accepted main IL,
    //   - copies all primitive arrays/scalars into pure snapshots.
    //
    // Worker:
    //   - receives only copied arrays/scalars,
    //   - executes exact pure math,
    //   - compares against accepted R038 helper/native/PQS witnesses.
    //
    // This candidate remains shadow-only. It never writes the terrain DB,
    // never switches producer authority, and never touches the GPU path.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR039PtcMinmusPureCpuExactValidationObserver : MonoBehaviour
    {
        const string CandidateMarker =
            "AERIS34_REV3_5_R039_MINMUS_PURE_CPU_EXACT_CERTIFIED";

        const string ExpectedMainIlSha256 =
            "513748e2fdcc9eae0ed4958840f485ad5cd6eea4efb078e81ce5ae7bd400f687";

        const string ExpectedRandomVectorsBitSha256 =
            "783b86db486276222defdc756c4542dae60caa36fbdad4316d523055459e7ba0";

        const double PrimitiveTolerance = 1E-12;
        const double TerrainToleranceMeters = 1E-08;

        sealed class HelperWitness
        {
            internal readonly string Method;
            internal readonly double[] Args;
            internal readonly double Expected;

            internal HelperWitness(string method, double[] args, double expected)
            {
                Method = method;
                Args = args;
                Expected = expected;
            }
        }

        sealed class NativeWitness
        {
            internal readonly string Label;
            internal readonly string Method;
            internal readonly double X;
            internal readonly double Y;
            internal readonly double Z;
            internal readonly double Expected;

            internal NativeWitness(
                string label,
                string method,
                double x,
                double y,
                double z,
                double expected)
            {
                Label = label;
                Method = method;
                X = x;
                Y = y;
                Z = z;
                Expected = expected;
            }
        }

        sealed class PqsWitness
        {
            internal readonly int Index;
            internal readonly double X;
            internal readonly double Y;
            internal readonly double Z;
            internal readonly double ExpectedMeters;

            internal PqsWitness(
                int index,
                double x,
                double y,
                double z,
                double expectedMeters)
            {
                Index = index;
                X = x;
                Y = y;
                Z = z;
                ExpectedMeters = expectedMeters;
            }
        }

        sealed class WorkerResult
        {
            internal int WorkerThreadId;
            internal int HelperPassed;
            internal int HelperTotal;
            internal int NativePassed;
            internal int NativeTotal;
            internal int PqsPassed;
            internal int PqsTotal;
            internal int Failures;
            internal double MaxHelperError;
            internal double MaxNativeError;
            internal double MaxTerrainErrorMeters;
            internal readonly List<string> Mismatches = new List<string>();
        }

        static readonly HelperWitness[] HelperWitnesses = new HelperWitness[]
        {
            new HelperWitness("Lerp", new double[] { 0.125, 0.25, 0.375 }, 0.171875),
            new HelperWitness("Lerp", new double[] { 0.25, 0.5, 0.75 }, 0.4375),
            new HelperWitness("Lerp", new double[] { 0.375, 0.75, 1.125 }, 0.796875),
            new HelperWitness("Clamp", new double[] { 0.125, 0.25, 0.375 }, 0.25),
            new HelperWitness("Clamp", new double[] { 0.25, 0.5, 0.75 }, 0.5),
            new HelperWitness("Clamp", new double[] { 0.375, 0.75, 1.125 }, 0.75),
            new HelperWitness("CubicHermite", new double[] { 0.125, 0.25, 0.375, 0.5, 0.625 }, 0.170166015625),
            new HelperWitness("CubicHermite", new double[] { 0.25, 0.5, 0.75, 1.0, 1.25 }, 0.89453125),
            new HelperWitness("CubicHermite", new double[] { 0.375, 0.75, 1.125, 1.5, 1.875 }, 5.615478515625)
        };

        static readonly NativeWitness[] NativeWitnesses = new NativeWitness[]
        {
            new NativeWitness("continental", "noise", 0.0, 0.0, 0.0, 0.0),
            new NativeWitness("continental", "noise", 0.1, 0.2, 0.3, 0.52672315377181589),
            new NativeWitness("continental", "noise", -0.25, 0.5, -0.75, 0.10382063929523498),
            new NativeWitness("continental", "noise", 1.0, 2.0, 3.0, 0.0),
            new NativeWitness("continental", "noise", 12.345, -67.89, 0.125, -0.22105188458450339),
            new NativeWitness("continental", "noise", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, -0.45437756627938736),
            new NativeWitness("continental", "noiseNormalized", 0.0, 0.0, 0.0, 0.5),
            new NativeWitness("continental", "noiseNormalized", 0.1, 0.2, 0.3, 0.76336157688590789),
            new NativeWitness("continental", "noiseNormalized", -0.25, 0.5, -0.75, 0.5519103196476175),
            new NativeWitness("continental", "noiseNormalized", 1.0, 2.0, 3.0, 0.5),
            new NativeWitness("continental", "noiseNormalized", 12.345, -67.89, 0.125, 0.38947405770774829),
            new NativeWitness("continental", "noiseNormalized", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, 0.27281121686030629),
            new NativeWitness("continentalSmoothing", "noise", 0.0, 0.0, 0.0, 0.0),
            new NativeWitness("continentalSmoothing", "noise", 0.1, 0.2, 0.3, 0.51029005750610579),
            new NativeWitness("continentalSmoothing", "noise", -0.25, 0.5, -0.75, 0.10425785093234115),
            new NativeWitness("continentalSmoothing", "noise", 1.0, 2.0, 3.0, 0.0),
            new NativeWitness("continentalSmoothing", "noise", 12.345, -67.89, 0.125, -0.21209088446804125),
            new NativeWitness("continentalSmoothing", "noise", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, -0.431059623909178),
            new NativeWitness("continentalSmoothing", "noiseNormalized", 0.0, 0.0, 0.0, 0.5),
            new NativeWitness("continentalSmoothing", "noiseNormalized", 0.1, 0.2, 0.3, 0.75514502875305289),
            new NativeWitness("continentalSmoothing", "noiseNormalized", -0.25, 0.5, -0.75, 0.55212892546617054),
            new NativeWitness("continentalSmoothing", "noiseNormalized", 1.0, 2.0, 3.0, 0.5),
            new NativeWitness("continentalSmoothing", "noiseNormalized", 12.345, -67.89, 0.125, 0.39395455776597937),
            new NativeWitness("continentalSmoothing", "noiseNormalized", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, 0.284470188045411),
            new NativeWitness("continentalSharpnessMap", "noise", 0.0, 0.0, 0.0, 0.0),
            new NativeWitness("continentalSharpnessMap", "noise", 0.1, 0.2, 0.3, -0.24680203182374391),
            new NativeWitness("continentalSharpnessMap", "noise", -0.25, 0.5, -0.75, 0.13790067523078886),
            new NativeWitness("continentalSharpnessMap", "noise", 1.0, 2.0, 3.0, -0.15883433148268533),
            new NativeWitness("continentalSharpnessMap", "noise", 12.345, -67.89, 0.125, -0.30085450936122782),
            new NativeWitness("continentalSharpnessMap", "noise", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, 0.35449862099157331),
            new NativeWitness("continentalSharpnessMap", "noiseNormalized", 0.0, 0.0, 0.0, 0.5),
            new NativeWitness("continentalSharpnessMap", "noiseNormalized", 0.1, 0.2, 0.3, 0.37659898408812803),
            new NativeWitness("continentalSharpnessMap", "noiseNormalized", -0.25, 0.5, -0.75, 0.56895033761539437),
            new NativeWitness("continentalSharpnessMap", "noiseNormalized", 1.0, 2.0, 3.0, 0.42058283425865733),
            new NativeWitness("continentalSharpnessMap", "noiseNormalized", 12.345, -67.89, 0.125, 0.34957274531938609),
            new NativeWitness("continentalSharpnessMap", "noiseNormalized", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, 0.67724931049578663),
            new NativeWitness("continentalRuggedness", "noise", 0.0, 0.0, 0.0, 0.0),
            new NativeWitness("continentalRuggedness", "noise", 0.1, 0.2, 0.3, -0.71405279598268334),
            new NativeWitness("continentalRuggedness", "noise", -0.25, 0.5, -0.75, 0.056644599103014046),
            new NativeWitness("continentalRuggedness", "noise", 1.0, 2.0, 3.0, 0.17935159565526482),
            new NativeWitness("continentalRuggedness", "noise", 12.345, -67.89, 0.125, 0.03791312899888831),
            new NativeWitness("continentalRuggedness", "noise", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, -0.47409484923956113),
            new NativeWitness("continentalRuggedness", "noiseNormalized", 0.0, 0.0, 0.0, 0.5),
            new NativeWitness("continentalRuggedness", "noiseNormalized", 0.1, 0.2, 0.3, 0.14297360200865833),
            new NativeWitness("continentalRuggedness", "noiseNormalized", -0.25, 0.5, -0.75, 0.528322299551507),
            new NativeWitness("continentalRuggedness", "noiseNormalized", 1.0, 2.0, 3.0, 0.58967579782763235),
            new NativeWitness("continentalRuggedness", "noiseNormalized", 12.345, -67.89, 0.125, 0.51895656449944416),
            new NativeWitness("continentalRuggedness", "noiseNormalized", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, 0.26295257538021943),
            new NativeWitness("continentalSharpness", "GetValue", 0.0, 0.0, 0.0, 17.75),
            new NativeWitness("continentalSharpness", "GetValue", 0.1, 0.2, 0.3, 15.84912899079276),
            new NativeWitness("continentalSharpness", "GetValue", -0.25, 0.5, -0.75, 9.4184063204907744),
            new NativeWitness("continentalSharpness", "GetValue", 1.0, 2.0, 3.0, 6.4978745987727464),
            new NativeWitness("continentalSharpness", "GetValue", 12.345, -67.89, 0.125, 3.4001592852534035),
            new NativeWitness("continentalSharpness", "GetValue", -3.1415926535897931, 2.7182818284590451, 0.577215664901533, 9.3824970013495026)
        };

        static readonly PqsWitness[] PqsWitnesses = new PqsWitness[]
        {
            new PqsWitness(0, 1.0, 0.0, 0.0, 0.0),
            new PqsWitness(1, 0.0, 1.0, 0.0, 2896.4000000000015),
            new PqsWitness(2, 0.0, 0.0, 1.0, 4306.5),
            new PqsWitness(3, -1.0, 0.0, 0.0, 0.0),
            new PqsWitness(4, 0.0, -1.0, 0.0, 2981.5999999999985),
            new PqsWitness(5, 0.0, 0.0, -1.0, 1562.8000000000029),
            new PqsWitness(6, 0.57735026918962584, 0.57735026918962584, 0.57735026918962584, 3177.5999999999985),
            new PqsWitness(7, -0.57735026918962584, 0.57735026918962584, 0.57735026918962584, 1802.6999999999971),
            new PqsWitness(8, 0.57735026918962584, -0.57735026918962584, 0.57735026918962584, 248.0),
            new PqsWitness(9, 0.57735026918962584, 0.57735026918962584, -0.57735026918962584, 2883.0),
            new PqsWitness(10, 0.13375998748853216, -0.4958906853233388, 0.85802138315814536, 3156.5),
            new PqsWitness(11, -0.73123183724443586, 0.41913288619072314, 0.53817062713749186, 0.0)
        };

        readonly object resultSync = new object();
        float nextAttempt;
        bool workerStarted;
        bool reported;
        int mainThreadId;
        WorkerResult workerResult;

        void Update()
        {
            if (!workerStarted)
            {
                if (Time.realtimeSinceStartup < nextAttempt)
                    return;

                nextAttempt = Time.realtimeSinceStartup + 1f;

                if (!AERISTerrainTileSystem.GameDataHashReady ||
                    FlightGlobals.Bodies == null ||
                    FlightGlobals.Bodies.Count == 0)
                    return;

                StartValidation();
            }

            if (workerStarted && !reported)
            {
                WorkerResult result = null;
                lock (resultSync)
                {
                    result = workerResult;
                }

                if (result != null)
                {
                    Report(result);
                    reported = true;
                }
            }
        }

        void StartValidation()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                CelestialBody minmus = FindBody("Minmus");
                if (minmus == null)
                    throw new InvalidOperationException("MINMUS_NOT_FOUND");

                object pqs = RequireMember(minmus, "pqsController");
                object vertexPlanet = FindMod(pqs, "PQSMod_VertexPlanet");
                if (vertexPlanet == null)
                    throw new InvalidOperationException("VERTEXPLANET_NOT_FOUND");

                string mainIlSha = HashVertexPlanetMainIl(vertexPlanet.GetType());
                if (!string.Equals(
                    mainIlSha,
                    ExpectedMainIlSha256,
                    StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "VERTEXPLANET_MAIN_IL_SHA_MISMATCH:" + mainIlSha);
                }

                AERISR039MinmusPureCpuExact.VertexPlanetSnapshot snapshot =
                    BuildSnapshot(vertexPlanet);

                AERISLogger.Info(
                    "[R039][PURE_CPU_BEGIN]" +
                    "; body=Minmus" +
                    "; candidate=" + CandidateMarker +
                    "; main_thread_id=" +
                        mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_il_sha256=" + mainIlSha +
                    "; primitive_tolerance=" +
                        PrimitiveTolerance.ToString("R", CultureInfo.InvariantCulture) +
                    "; terrain_tolerance_m=" +
                        TerrainToleranceMeters.ToString("R", CultureInfo.InvariantCulture) +
                    "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                    "; worker_invokes_runtime_object=false" +
                    "; worker_ready=false" +
                    "; certification=NO_SHADOW_ONLY" +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; gpu=false" +
                    "; authority=PQS");

                AERISLogger.Info(
                    "[R039][PURE_CPU_SNAPSHOT]" +
                    "; body=Minmus" +
                    "; continental_perm=" + snapshot.Continental.Perm.Length +
                    "; smoothing_perm=" + snapshot.ContinentalSmoothing.Perm.Length +
                    "; sharpness_map_perm=" + snapshot.ContinentalSharpnessMap.Perm.Length +
                    "; ruggedness_perm=" + snapshot.ContinentalRuggedness.Perm.Length +
                    "; grad3=" + snapshot.Continental.Grad3.Length +
                    "; spectral_weights=" +
                        snapshot.ContinentalSharpness.SpectralWeights.Length +
                    "; random_vectors=" +
                        snapshot.ContinentalSharpness.RandomVectors.Length +
                    "; random_vectors_bit_sha256=" +
                        Sha256DoubleBits(snapshot.ContinentalSharpness.RandomVectors) +
                    "; noise_quality=" +
                        snapshot.ContinentalSharpness.NoiseQuality.ToString(
                            CultureInfo.InvariantCulture) +
                    "; runtime_object_snapshot_thread=MAIN_THREAD_ONLY" +
                    "; worker_snapshot_copy_only=true" +
                    "; authority=PQS");

                Thread thread = new Thread(
                    delegate()
                    {
                        WorkerResult result = RunWorker(snapshot, mainThreadId);
                        lock (resultSync)
                        {
                            workerResult = result;
                        }
                    });

                thread.IsBackground = true;
                thread.Name = "AERIS-R039-Minmus-PureCPU-Validation";
                workerStarted = true;
                thread.Start();
            }
            catch (Exception ex)
            {
                workerStarted = true;
                reported = true;

                AERISLogger.Info(
                    "[R039][PURE_CPU_FAIL]" +
                    "; stage=SNAPSHOT" +
                    "; error=" + Safe(ex.GetType().Name) +
                    "; message=" + Safe(ex.Message) +
                    "; worker_ready=false" +
                    "; pending=PURE_CPU_EXACT_VALIDATION" +
                    "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                    "; worker_invokes_runtime_object=false" +
                    "; certification=NO_SHADOW_ONLY" +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; gpu=false" +
                    "; authority=PQS");
            }
        }

        static WorkerResult RunWorker(
            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot snapshot,
            int expectedMainThreadId)
        {
            WorkerResult result = new WorkerResult();
            result.WorkerThreadId = Thread.CurrentThread.ManagedThreadId;

            ValidateHelpers(result);
            ValidateNative(snapshot, result);
            ValidatePqs(snapshot, result);

            result.Failures =
                (result.HelperTotal - result.HelperPassed) +
                (result.NativeTotal - result.NativePassed) +
                (result.PqsTotal - result.PqsPassed);

            if (result.WorkerThreadId == expectedMainThreadId)
            {
                result.Failures++;
                AddMismatch(result, "WORKER_NOT_OFF_MAIN_THREAD");
            }

            return result;
        }

        static void ValidateHelpers(WorkerResult result)
        {
            result.HelperTotal = HelperWitnesses.Length;

            for (int i = 0; i < HelperWitnesses.Length; i++)
            {
                HelperWitness witness = HelperWitnesses[i];
                double actual;

                if (witness.Method == "Lerp")
                {
                    actual = AERISR039MinmusPureCpuExact.Lerp(
                        witness.Args[0],
                        witness.Args[1],
                        witness.Args[2]);
                }
                else if (witness.Method == "Clamp")
                {
                    actual = AERISR039MinmusPureCpuExact.Clamp(
                        witness.Args[0],
                        witness.Args[1],
                        witness.Args[2]);
                }
                else if (witness.Method == "CubicHermite")
                {
                    actual = AERISR039MinmusPureCpuExact.CubicHermite(
                        witness.Args[0],
                        witness.Args[1],
                        witness.Args[2],
                        witness.Args[3],
                        witness.Args[4]);
                }
                else
                {
                    AddMismatch(
                        result,
                        "HELPER_UNKNOWN:" + witness.Method);
                    continue;
                }

                double error = Error(actual, witness.Expected);
                result.MaxHelperError = Math.Max(result.MaxHelperError, error);

                if (error <= PrimitiveTolerance)
                    result.HelperPassed++;
                else
                    AddMismatch(
                        result,
                        "HELPER:" + witness.Method +
                        ":expected=" + R(witness.Expected) +
                        ":actual=" + R(actual) +
                        ":error=" + R(error));
            }
        }

        static void ValidateNative(
            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot snapshot,
            WorkerResult result)
        {
            result.NativeTotal = NativeWitnesses.Length;

            for (int i = 0; i < NativeWitnesses.Length; i++)
            {
                NativeWitness witness = NativeWitnesses[i];
                double actual;

                if (witness.Label == "continentalSharpness")
                {
                    actual = AERISR039MinmusPureCpuExact.RidgedGetValue(
                        snapshot.ContinentalSharpness,
                        witness.X,
                        witness.Y,
                        witness.Z);
                }
                else
                {
                    AERISR039MinmusPureCpuExact.SimplexSnapshot simplex =
                        ResolveSimplex(snapshot, witness.Label);

                    if (simplex == null)
                    {
                        AddMismatch(
                            result,
                            "NATIVE_UNKNOWN_LABEL:" + witness.Label);
                        continue;
                    }

                    double persistence =
                        ResolveCapturedNativePersistence(witness.Label);

                    actual = AERISR039MinmusPureCpuExact.SimplexNoise(
                        simplex,
                        witness.X,
                        witness.Y,
                        witness.Z,
                        persistence);

                    if (witness.Method == "noiseNormalized")
                    {
                        actual = actual + 1.0;
                        actual = actual * 0.5;
                    }
                    else if (witness.Method != "noise")
                    {
                        AddMismatch(
                            result,
                            "NATIVE_UNKNOWN_METHOD:" +
                            witness.Label + "." + witness.Method);
                        continue;
                    }
                }

                double error = Error(actual, witness.Expected);
                result.MaxNativeError = Math.Max(result.MaxNativeError, error);

                if (error <= PrimitiveTolerance)
                    result.NativePassed++;
                else
                    AddMismatch(
                        result,
                        "NATIVE:" + witness.Label + "." + witness.Method +
                        ":expected=" + R(witness.Expected) +
                        ":actual=" + R(actual) +
                        ":error=" + R(error));
            }
        }

        static void ValidatePqs(
            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot snapshot,
            WorkerResult result)
        {
            result.PqsTotal = PqsWitnesses.Length;

            for (int i = 0; i < PqsWitnesses.Length; i++)
            {
                PqsWitness witness = PqsWitnesses[i];

                double actual =
                    AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                        snapshot,
                        witness.X,
                        witness.Y,
                        witness.Z,
                        0.0);

                double error = Error(actual, witness.ExpectedMeters);
                result.MaxTerrainErrorMeters =
                    Math.Max(result.MaxTerrainErrorMeters, error);

                if (error <= TerrainToleranceMeters)
                    result.PqsPassed++;
                else
                    AddMismatch(
                        result,
                        "PQS:index=" +
                        witness.Index.ToString(CultureInfo.InvariantCulture) +
                        ":expected_m=" + R(witness.ExpectedMeters) +
                        ":actual_m=" + R(actual) +
                        ":error_m=" + R(error));
            }
        }

        void Report(WorkerResult result)
        {
            for (int i = 0; i < result.Mismatches.Count; i++)
            {
                AERISLogger.Info(
                    "[R039][PURE_CPU_MISMATCH]" +
                    "; detail=" + Safe(result.Mismatches[i]) +
                    "; authority=R038_CAPTURED_WITNESS");
            }

            AERISLogger.Info(
                "[R039][PURE_CPU_HELPER_RESULT]" +
                "; passed=" +
                    result.HelperPassed.ToString(CultureInfo.InvariantCulture) +
                "; total=" +
                    result.HelperTotal.ToString(CultureInfo.InvariantCulture) +
                "; max_error=" +
                    R(result.MaxHelperError) +
                "; tolerance=" +
                    R(PrimitiveTolerance));

            AERISLogger.Info(
                "[R039][PURE_CPU_NATIVE_RESULT]" +
                "; passed=" +
                    result.NativePassed.ToString(CultureInfo.InvariantCulture) +
                "; total=" +
                    result.NativeTotal.ToString(CultureInfo.InvariantCulture) +
                "; max_error=" +
                    R(result.MaxNativeError) +
                "; tolerance=" +
                    R(PrimitiveTolerance));

            AERISLogger.Info(
                "[R039][PURE_CPU_PQS_RESULT]" +
                "; passed=" +
                    result.PqsPassed.ToString(CultureInfo.InvariantCulture) +
                "; total=" +
                    result.PqsTotal.ToString(CultureInfo.InvariantCulture) +
                "; max_error_m=" +
                    R(result.MaxTerrainErrorMeters) +
                "; tolerance_m=" +
                    R(TerrainToleranceMeters));

            bool pass =
                result.Failures == 0 &&
                result.HelperPassed == result.HelperTotal &&
                result.NativePassed == result.NativeTotal &&
                result.PqsPassed == result.PqsTotal &&
                result.WorkerThreadId != mainThreadId;

            AERISLogger.Info(
                "[R039][PURE_CPU_COMPLETE]" +
                "; body=Minmus" +
                "; pass=" + pass.ToString() +
                "; helper_passed=" +
                    result.HelperPassed.ToString(CultureInfo.InvariantCulture) +
                "; helper_total=" +
                    result.HelperTotal.ToString(CultureInfo.InvariantCulture) +
                "; native_passed=" +
                    result.NativePassed.ToString(CultureInfo.InvariantCulture) +
                "; native_total=" +
                    result.NativeTotal.ToString(CultureInfo.InvariantCulture) +
                "; pqs_passed=" +
                    result.PqsPassed.ToString(CultureInfo.InvariantCulture) +
                "; pqs_total=" +
                    result.PqsTotal.ToString(CultureInfo.InvariantCulture) +
                "; failures=" +
                    result.Failures.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_thread_id=" +
                    result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_off_main_thread=" +
                    (result.WorkerThreadId != mainThreadId).ToString() +
                "; worker_runtime_object_access=false" +
                "; worker_ready=" +
                    (pass ? "true" : "false") +
                "; pending=" +
                    (pass
                        ? "-"
                        : "PURE_CPU_EXACT_VALIDATION_FAILED") +
                "; certification=" +
                    (pass
                        ? "PURE_CPU_EXACT_CERTIFIED"
                        : "NO_FAIL_CLOSED") +
                "; db_write=false" +
                "; producer_switch=false" +
                "; gpu=false" +
                "; authority=PQS");

            if (pass)
            {
                AERISLogger.Info(
                    "[R039][MINMUS_WORKER_READY]" +
                    "; body=Minmus" +
                    "; worker_ready=true" +
                    "; pending=-" +
                    "; helper_passed=" +
                        result.HelperPassed.ToString(CultureInfo.InvariantCulture) +
                    "; helper_total=" +
                        result.HelperTotal.ToString(CultureInfo.InvariantCulture) +
                    "; native_passed=" +
                        result.NativePassed.ToString(CultureInfo.InvariantCulture) +
                    "; native_total=" +
                        result.NativeTotal.ToString(CultureInfo.InvariantCulture) +
                    "; pqs_passed=" +
                        result.PqsPassed.ToString(CultureInfo.InvariantCulture) +
                    "; pqs_total=" +
                        result.PqsTotal.ToString(CultureInfo.InvariantCulture) +
                    "; primitive_failures=0" +
                    "; terrain_failures=0" +
                    "; max_primitive_abs_error=" +
                        R(Math.Max(
                            result.MaxHelperError,
                            result.MaxNativeError)) +
                    "; max_terrain_abs_error_m=" +
                        R(result.MaxTerrainErrorMeters) +
                    "; primitive_tolerance=" +
                        R(PrimitiveTolerance) +
                    "; terrain_tolerance_m=" +
                        R(TerrainToleranceMeters) +
                    "; main_thread_id=" +
                        mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; worker_thread_id=" +
                        result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; worker_off_main_thread=" +
                        (result.WorkerThreadId != mainThreadId).ToString() +
                    "; worker_runtime_object_access=false" +
                    "; snapshot_payload=PRIMITIVES_ONLY" +
                    "; certification=PURE_CPU_EXACT_CERTIFIED" +
                    "; db_write=false" +
                    "; producer_switch=false" +
                    "; gpu=false" +
                    "; authority=PQS");
            }
        }

        static AERISR039MinmusPureCpuExact.SimplexSnapshot ResolveSimplex(
            AERISR039MinmusPureCpuExact.VertexPlanetSnapshot snapshot,
            string label)
        {
            if (label == "continental")
                return snapshot.Continental;
            if (label == "continentalSmoothing")
                return snapshot.ContinentalSmoothing;
            if (label == "continentalSharpnessMap")
                return snapshot.ContinentalSharpnessMap;
            if (label == "continentalRuggedness")
                return snapshot.ContinentalRuggedness;
            return null;
        }

        static double ResolveCapturedNativePersistence(string label)
        {
            // Exact primitive persistence captured by R038 before its six
            // native witness invocations for each Simplex object.
            if (label == "continental")
                return 0.38079344511570129;
            if (label == "continentalSmoothing")
                return 0.40000000596046448;
            if (label == "continentalSharpnessMap")
                return 0.550000011920929;
            if (label == "continentalRuggedness")
                return 0.25098579580799568;
            throw new InvalidOperationException("UNKNOWN_NATIVE_SIMPLEX:" + label);
        }

        static AERISR039MinmusPureCpuExact.VertexPlanetSnapshot BuildSnapshot(
            object vertexPlanet)
        {
            object continentalWrapper =
                RequireMember(vertexPlanet, "continental");
            object smoothingWrapper =
                RequireMember(vertexPlanet, "continentalSmoothing");
            object sharpnessMapWrapper =
                RequireMember(vertexPlanet, "continentalSharpnessMap");
            object ruggednessWrapper =
                RequireMember(vertexPlanet, "continentalRuggedness");
            object sharpnessWrapper =
                RequireMember(vertexPlanet, "continentalSharpness");

            AERISR039MinmusPureCpuExact.SimplexSnapshot continental =
                SnapshotSimplex(continentalWrapper);
            AERISR039MinmusPureCpuExact.SimplexSnapshot smoothing =
                SnapshotSimplex(smoothingWrapper);
            AERISR039MinmusPureCpuExact.SimplexSnapshot sharpnessMap =
                SnapshotSimplex(sharpnessMapWrapper);
            AERISR039MinmusPureCpuExact.SimplexSnapshot ruggedness =
                SnapshotSimplex(ruggednessWrapper);

            object ridgedRuntime =
                RequireMember(sharpnessWrapper, "noise");
            AERISR039MinmusPureCpuExact.RidgedSnapshot ridged =
                SnapshotRidged(ridgedRuntime);

            return new AERISR039MinmusPureCpuExact.VertexPlanetSnapshot(
                ReadDouble(vertexPlanet, "deformity"),
                ReadDouble(vertexPlanet, "oceanLevel"),
                ReadBool(vertexPlanet, "oceanSnap"),
                ReadDouble(vertexPlanet, "oceanDepth"),
                ReadDouble(vertexPlanet, "oceanStep"),
                ReadDouble(vertexPlanet, "terrainRidgeBalance"),
                ReadDouble(vertexPlanet, "terrainRidgesMin"),
                ReadDouble(vertexPlanet, "terrainRidgesMax"),
                ReadDouble(vertexPlanet, "terrainShapeStart"),
                ReadDouble(vertexPlanet, "terrainShapeEnd"),
                continental,
                smoothing,
                sharpnessMap,
                ruggedness,
                ridged,
                ReadDouble(continentalWrapper, "deformity"),
                ReadDouble(continentalWrapper, "persistance"),
                ReadDouble(smoothingWrapper, "persistance"),
                ReadDouble(sharpnessMapWrapper, "deformity"),
                ReadDouble(ruggednessWrapper, "deformity"),
                ReadDouble(ruggednessWrapper, "persistance"),
                ReadDouble(sharpnessWrapper, "deformity"));
        }

        static AERISR039MinmusPureCpuExact.SimplexSnapshot SnapshotSimplex(
            object wrapper)
        {
            object simplex = RequireMember(wrapper, "simplex");

            int[] perm = CopyIntArray(
                RequireMember(simplex, "perm"),
                512,
                "perm");

            int[][] grad3 = CopyJaggedIntArray(
                RequireMember(simplex, "grad3"),
                12,
                "grad3");

            return new AERISR039MinmusPureCpuExact.SimplexSnapshot(
                perm,
                grad3,
                ReadDouble(simplex, "frequency"),
                ReadDouble(simplex, "octaves"),
                ReadDouble(simplex, "persistence"));
        }

        static AERISR039MinmusPureCpuExact.RidgedSnapshot SnapshotRidged(
            object noise)
        {
            double[] spectral = CopyDoubleArray(
                RequireMember(noise, "SpectralWeights"),
                30,
                "SpectralWeights");

            Assembly assembly = noise.GetType().Assembly;
            Type basis = assembly.GetType(
                "LibNoise.GradientNoiseBasis",
                false);

            if (basis == null)
                throw new InvalidOperationException(
                    "LIBNOISE_GRADIENT_NOISE_BASIS_NOT_FOUND");

            FieldInfo randomVectorsField = FindField(
                basis,
                "RandomVectors",
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (randomVectorsField == null)
                throw new MissingFieldException(
                    basis.FullName,
                    "RandomVectors");

            double[] randomVectors = CopyDoubleArray(
                randomVectorsField.GetValue(null),
                1024,
                "RandomVectors");

            string randomSha = Sha256DoubleBits(randomVectors);
            if (!string.Equals(
                randomSha,
                ExpectedRandomVectorsBitSha256,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "RANDOM_VECTORS_SHA_MISMATCH:" + randomSha);
            }

            int noiseQuality = ReadInt(noise, "NoiseQuality");
            if (noiseQuality != 2)
                throw new InvalidOperationException(
                    "NOISE_QUALITY_NOT_HIGH_2:" +
                    noiseQuality.ToString(CultureInfo.InvariantCulture));

            return new AERISR039MinmusPureCpuExact.RidgedSnapshot(
                ReadDouble(noise, "Frequency"),
                ReadInt(noise, "Seed"),
                noiseQuality,
                ReadDouble(noise, "Lacunarity"),
                ReadInt(noise, "OctaveCount"),
                spectral,
                randomVectors);
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body != null &&
                    string.Equals(
                        body.name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                    return body;
            }

            return null;
        }

        static object FindMod(object pqs, string runtimeType)
        {
            if (pqs == null)
                return null;

            IEnumerable mods = ReadMember(pqs, "mods") as IEnumerable;
            if (mods == null)
                return null;

            foreach (object mod in mods)
            {
                if (mod == null)
                    continue;

                Type type = mod.GetType();
                string name = type.FullName ?? type.Name;
                if (string.Equals(
                    name,
                    runtimeType,
                    StringComparison.Ordinal))
                    return mod;
            }

            return null;
        }

        static string HashVertexPlanetMainIl(Type type)
        {
            MethodInfo selected = null;
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null ||
                    method.Name != "OnVertexBuildHeight")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters != null && parameters.Length == 1)
                {
                    selected = method;
                    break;
                }
            }

            if (selected == null)
                throw new MissingMethodException(
                    type.FullName,
                    "OnVertexBuildHeight");

            MethodBody body = selected.GetMethodBody();
            byte[] il = body == null ? null : body.GetILAsByteArray();
            if (il == null || il.Length != 897)
                throw new InvalidOperationException(
                    "VERTEXPLANET_MAIN_IL_SIZE:" +
                    (il == null
                        ? "NULL"
                        : il.Length.ToString(CultureInfo.InvariantCulture)));

            return Sha256Bytes(il);
        }

        static object RequireMember(object target, string name)
        {
            object value = ReadMember(target, name);
            if (value == null)
                throw new MissingMemberException(
                    target == null ? "NULL" : target.GetType().FullName,
                    name);
            return value;
        }

        static object ReadMember(object target, string name)
        {
            if (target == null)
                return null;

            Type type = target.GetType();

            FieldInfo field = FindField(
                type,
                name,
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (field != null)
                return field.GetValue(field.IsStatic ? null : target);

            PropertyInfo property = FindProperty(type, name);
            if (property != null &&
                property.CanRead &&
                property.GetIndexParameters().Length == 0)
            {
                MethodInfo getter = property.GetGetMethod(true);
                return property.GetValue(
                    getter != null && getter.IsStatic ? null : target,
                    null);
            }

            // Auto-property backing-field fallback.
            field = FindField(
                type,
                "<" + name + ">k__BackingField",
                BindingFlags.Instance |
                BindingFlags.Static |
                BindingFlags.Public |
                BindingFlags.NonPublic);

            if (field != null)
                return field.GetValue(field.IsStatic ? null : target);

            return null;
        }

        static FieldInfo FindField(
            Type type,
            string name,
            BindingFlags flags)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(
                    name,
                    flags | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                current = current.BaseType;
            }

            return null;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            Type current = type;
            while (current != null)
            {
                PropertyInfo property = current.GetProperty(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);

                if (property != null)
                    return property;

                current = current.BaseType;
            }

            return null;
        }

        static double ReadDouble(object target, string name)
        {
            object value = RequireMember(target, name);
            return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        }

        static int ReadInt(object target, string name)
        {
            object value = RequireMember(target, name);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }

        static bool ReadBool(object target, string name)
        {
            object value = RequireMember(target, name);
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }

        static int[] CopyIntArray(
            object value,
            int expectedLength,
            string name)
        {
            int[] array = value as int[];
            if (array == null || array.Length != expectedLength)
                throw new InvalidOperationException(
                    name + "_LENGTH_OR_TYPE");
            return (int[])array.Clone();
        }

        static int[][] CopyJaggedIntArray(
            object value,
            int expectedLength,
            string name)
        {
            int[][] array = value as int[][];
            if (array == null || array.Length != expectedLength)
                throw new InvalidOperationException(
                    name + "_LENGTH_OR_TYPE");

            int[][] copy = new int[array.Length][];
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null || array[i].Length < 3)
                    throw new InvalidOperationException(
                        name + "_ROW_" +
                        i.ToString(CultureInfo.InvariantCulture));
                copy[i] = (int[])array[i].Clone();
            }

            return copy;
        }

        static double[] CopyDoubleArray(
            object value,
            int expectedLength,
            string name)
        {
            double[] array = value as double[];
            if (array == null || array.Length != expectedLength)
                throw new InvalidOperationException(
                    name + "_LENGTH_OR_TYPE");
            return (double[])array.Clone();
        }

        static string Sha256DoubleBits(double[] values)
        {
            byte[] bytes = new byte[values.Length * sizeof(double)];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return Sha256Bytes(bytes);
        }

        static string Sha256Bytes(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                StringBuilder text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString(
                        "x2",
                        CultureInfo.InvariantCulture));
                }
                return text.ToString();
            }
        }

        static double Error(double actual, double expected)
        {
            if (double.IsNaN(actual) || double.IsNaN(expected))
                return double.PositiveInfinity;
            return Math.Abs(actual - expected);
        }

        static void AddMismatch(WorkerResult result, string detail)
        {
            if (result.Mismatches.Count < 24)
                result.Mismatches.Add(detail);
        }

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace(';', ',')
                .Replace('|', '/');
        }
    }
}
