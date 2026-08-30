using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS38 R041D: first all-stock multi-body pure-CPU exact shadow.
    //
    // Main thread:
    // - copies runtime configuration into primitive snapshots;
    // - closes MapSO / AnimationCurve dependency semantics against live read-only calls;
    // - captures deterministic PQS TerrainAltitude witnesses and direction vectors.
    // Worker:
    // - receives ONLY strings, scalars, arrays and pure CLR snapshot classes;
    // - never receives or dereferences a Unity/KSP runtime object.
    // Production/DB authority remains PQS; this observer never switches a producer.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR041MohoDresPureCpuExactShadowObserver : MonoBehaviour
    {
        const string Candidate =
            "AERIS38_R041D_MOHO_DRES_PURE_CPU_EXACT_SHADOW";
        const double ToleranceMeters = 1E-08;
        static readonly string[] TargetBodies = { "Moho", "Dres" };
        static readonly HashSet<string> TargetTypes = new HashSet<string>(
            new[]
            {
                "PQSMod_VertexHeightMap",
                "PQSMod_VertexSimplexHeight",
                "PQSMod_FlattenOcean",
                "PQSMod_VertexHeightNoiseVertHeightCurve2",
                "PQSMod_VertexSimplexHeightAbsolute",
                "PQSMod_VertexHeightNoise"
            },
            StringComparer.Ordinal);

        sealed class Sample
        {
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
            internal double ExpectedAsl;
        }

        sealed class BodyCase
        {
            internal string Name;
            internal AERISR041MohoDresPureCpuExact.BodySnapshot Snapshot;
            internal Sample[] Samples;
            internal bool MapDependencyExact = true;
            internal bool CurveDependencyExact = true;
            internal int HeightOps;
        }

        sealed class CandidateResult
        {
            internal bool AbsoluteInitial;
            internal AERISR041MohoDresPureCpuExact.CoordMode CoordMode;
            internal int Samples;
            internal int Failures;
            internal double MaxError;
        }

        sealed class BodyResult
        {
            internal string Name;
            internal bool DependenciesExact;
            internal CandidateResult[] Candidates;
            internal int ExactCandidates;
            internal CandidateResult Selected;
            internal bool Pass;
        }

        sealed class WorkerResult
        {
            internal int WorkerThreadId;
            internal BodyResult[] Bodies;
            internal string Error;
        }

        sealed class ModRecord
        {
            internal object Mod;
            internal string TypeName;
            internal int Order;
            internal int Index;
        }

        sealed class MapSelection
        {
            internal AERISR041MohoDresPureCpuExact.MapSnapshot Snapshot;
            internal bool Exact;
            internal int IntegerMatches;
            internal int IntegerTests;
            internal int BilinearMatches;
            internal int BilinearTests;
            internal double MaxAbsError;
        }

        sealed class CurveSelection
        {
            internal AERISR041MohoDresPureCpuExact.CurveSnapshot Snapshot;
            internal bool Exact;
            internal int Matches;
            internal int Tests;
            internal double MaxAbsError;
        }

        int mainThreadId;
        bool started;
        bool reported;
        float nextAttempt;
        Task<WorkerResult> workerTask;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (reported) return;

            if (!started)
            {
                if (Time.realtimeSinceStartup < nextAttempt) return;
                nextAttempt = Time.realtimeSinceStartup + 1f;
                if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
                if (!AERISTerrainTileSystem.GameDataHashReady) return;
                if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

                StartShadow();
                return;
            }

            if (workerTask == null || !workerTask.IsCompleted) return;

            reported = true;
            try
            {
                WorkerResult result = workerTask.Result;
                Report(result);
            }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    "[R041D][SHADOW_FAIL]" +
                    "; stage=WORKER_RESULT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        void StartShadow()
        {
            started = true;

            AERISLogger.Info(
                "[R041D][SHADOW_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId +
                "; target_bodies=Moho,Dres" +
                "; dependency_closure=MAPSO_AND_ANIMATIONCURVE_INTEGRATED" +
                Invariants());

            try
            {
                double[] randomVectors = SnapshotLibNoiseRandomVectors();
                var bodyCases = new BodyCase[TargetBodies.Length];

                for (int i = 0; i < TargetBodies.Length; i++)
                    bodyCases[i] = CaptureBody(TargetBodies[i], randomVectors);

                // IMPORTANT: bodyCases contains no Unity/KSP/runtime object.
                BodyCase[] purePayload = bodyCases;
                int mainId = mainThreadId;
                workerTask = Task.Factory.StartNew(
                    () => RunWorker(purePayload, mainId),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                reported = true;
                AERISLogger.Error(
                    "[R041D][SHADOW_FAIL]" +
                    "; stage=MAIN_THREAD_SNAPSHOT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        BodyCase CaptureBody(string bodyName, double[] randomVectors)
        {
            CelestialBody body = FindBody(bodyName);
            if (body == null || body.pqsController == null)
                throw new InvalidOperationException(bodyName + "_PQS_MISSING");

            object pqs = body.pqsController;
            double radius = body.Radius;
            double radiusMin = ReadDouble(pqs, "radiusMin");

            List<ModRecord> mods = CollectTargetMods(pqs);
            if (mods.Count != 5)
                throw new InvalidOperationException(
                    bodyName + "_EXPECTED_5_HEIGHT_OPS_ACTUAL_" +
                    mods.Count.ToString(CultureInfo.InvariantCulture));

            var ops = new AERISR041MohoDresPureCpuExact.HeightOpSnapshot[mods.Count];
            bool mapExact = true;
            bool curveExact = true;

            for (int i = 0; i < mods.Count; i++)
            {
                ModRecord record = mods[i];
                object mod = record.Mod;

                switch (record.TypeName)
                {
                    case "PQSMod_VertexHeightMap":
                    {
                        MapSO map = RequireMember(mod, "heightMap") as MapSO;
                        if (map == null)
                            throw new InvalidOperationException(bodyName + "_MAPSO_MISSING");

                        MapSelection selection = SelectMapSnapshot(bodyName, map);
                        mapExact &= selection.Exact;
                        ops[i] = new AERISR041MohoDresPureCpuExact.HeightMapOpSnapshot(
                            ReadDouble(mod, "heightMapOffset"),
                            ReadDouble(mod, "heightMapDeformity"),
                            selection.Snapshot);
                        break;
                    }

                    case "PQSMod_VertexSimplexHeight":
                        ops[i] = new AERISR041MohoDresPureCpuExact.SimplexHeightOpSnapshot(
                            ReadDouble(mod, "deformity"),
                            SnapshotSimplex(RequireMember(mod, "simplex")));
                        break;

                    case "PQSMod_FlattenOcean":
                        ops[i] = new AERISR041MohoDresPureCpuExact.FlattenOceanOpSnapshot(
                            ReadDouble(mod, "oceanRad"));
                        break;

                    case "PQSMod_VertexHeightNoiseVertHeightCurve2":
                    {
                        AnimationCurve curve = RequireMember(mod, "simplexCurve") as AnimationCurve;
                        if (curve == null)
                            throw new InvalidOperationException(bodyName + "_CURVE_MISSING");

                        CurveSelection curveSelection = SelectCurveSnapshot(bodyName, curve);
                        curveExact &= curveSelection.Exact;

                        ops[i] = new AERISR041MohoDresPureCpuExact.Curve2OpSnapshot(
                            Convert.ToSingle(RequireMember(mod, "deformity"), CultureInfo.InvariantCulture),
                            radiusMin,
                            ReadDouble(mod, "simplexHeightStart"),
                            ReadDouble(mod, "simplexHeightEnd"),
                            ReadDouble(mod, "hDeltaR"),
                            SnapshotSimplex(RequireMember(mod, "simplex")),
                            SnapshotRidged(RequireMember(mod, "ridgedAdd"), randomVectors),
                            SnapshotRidged(RequireMember(mod, "ridgedSub"), randomVectors),
                            curveSelection.Snapshot);
                        break;
                    }

                    case "PQSMod_VertexSimplexHeightAbsolute":
                        ops[i] = new AERISR041MohoDresPureCpuExact.SimplexAbsoluteOpSnapshot(
                            ReadDouble(mod, "deformity"),
                            SnapshotSimplex(RequireMember(mod, "simplex")));
                        break;

                    case "PQSMod_VertexHeightNoise":
                    {
                        object noise = RequireMember(mod, "noiseMap");
                        string runtimeType = noise.GetType().FullName ?? noise.GetType().Name;
                        if (!string.Equals(runtimeType, "LibNoise.RidgedMultifractal",
                            StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                bodyName + "_HEIGHT_NOISE_NOT_RIDGED:" + runtimeType);

                        ops[i] = new AERISR041MohoDresPureCpuExact.HeightNoiseRidgedOpSnapshot(
                            Convert.ToSingle(RequireMember(mod, "deformity"), CultureInfo.InvariantCulture),
                            SnapshotRidged(noise, randomVectors));
                        break;
                    }

                    default:
                        throw new InvalidOperationException(
                            bodyName + "_UNSUPPORTED_TARGET_TYPE:" + record.TypeName);
                }
            }

            Sample[] samples = CaptureSamples(body);
            var snapshot = new AERISR041MohoDresPureCpuExact.BodySnapshot(
                bodyName, radius, ops);

            AERISLogger.Info(
                "[R041D][BODY_SNAPSHOT]" +
                "; body=" + bodyName +
                "; radius_m=" + R(radius) +
                "; radius_min=" + R(radiusMin) +
                "; height_ops=" + ops.Length +
                "; samples=" + samples.Length +
                "; map_dependency_exact=" + mapExact +
                "; curve_dependency_exact=" + curveExact +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());

            return new BodyCase
            {
                Name = bodyName,
                Snapshot = snapshot,
                Samples = samples,
                MapDependencyExact = mapExact,
                CurveDependencyExact = curveExact,
                HeightOps = ops.Length
            };
        }

        List<ModRecord> CollectTargetMods(object pqs)
        {
            IEnumerable enumerable = ReadMember(pqs, "mods") as IEnumerable;
            if (enumerable == null)
                throw new InvalidOperationException("PQS_MOD_LIST_MISSING");

            var result = new List<ModRecord>();
            int index = 0;
            foreach (object mod in enumerable)
            {
                if (mod == null)
                {
                    index++;
                    continue;
                }

                Type type = mod.GetType();
                string name = type.FullName ?? type.Name;
                if (!TargetTypes.Contains(name) || !ReadBoolDefault(mod, "modEnabled", true))
                {
                    index++;
                    continue;
                }

                result.Add(new ModRecord
                {
                    Mod = mod,
                    TypeName = name,
                    Order = ReadIntDefault(mod, "order", 0),
                    Index = index
                });
                index++;
            }

            result.Sort(delegate(ModRecord a, ModRecord b)
            {
                int c = a.Order.CompareTo(b.Order);
                if (c != 0) return c;
                return a.Index.CompareTo(b.Index);
            });
            return result;
        }

        MapSelection SelectMapSnapshot(string bodyName, MapSO map)
        {
            byte[] data = RequireMember(map, "_data") as byte[];
            if (data == null)
                throw new InvalidOperationException(bodyName + "_MAP_DATA_NOT_BYTE_ARRAY");

            int width = ReadInt(map, "_width");
            int height = ReadInt(map, "_height");
            int bpp = ReadInt(map, "_bpp");
            int rowWidth = ReadInt(map, "_rowWidth");
            if (rowWidth <= 0) rowWidth = checked(width * bpp);

            int[] ix =
            {
                0,
                Math.Max(0, width - 1),
                width / 2,
                width / 3,
                width * 2 / 3,
                Math.Max(0, width - 2)
            };
            int[] iy =
            {
                0,
                Math.Max(0, height - 1),
                height / 2,
                height / 3,
                height * 2 / 3,
                Math.Max(0, height - 2)
            };

            int bestChannel = 0;
            int bestIntegerMatches = -1;
            int integerTests = ix.Length * iy.Length;
            for (int channel = 0; channel < bpp; channel++)
            {
                int matches = 0;
                for (int y = 0; y < iy.Length; y++)
                {
                    for (int x = 0; x < ix.Length; x++)
                    {
                        int px = ClampInt(ix[x], 0, Math.Max(0, width - 1));
                        int py = ClampInt(iy[y], 0, Math.Max(0, height - 1));
                        int offset = checked(py * rowWidth + px * bpp + channel);
                        float pure = data[offset] * (1f / 255f);
                        float live = map.GetPixelFloat(px, py);
                        if (FloatBits(pure) == FloatBits(live)) matches++;
                    }
                }

                if (matches > bestIntegerMatches)
                {
                    bestIntegerMatches = matches;
                    bestChannel = channel;
                }
            }

            int bestMode = 0;
            int bestMatches = -1;
            double bestMaxError = double.PositiveInfinity;
            int bilinearTests = 96;

            for (int mode = 0; mode < 12; mode++)
            {
                var candidate = new AERISR041MohoDresPureCpuExact.MapSnapshot(
                    data, width, height, bpp, rowWidth, bestChannel,
                    (AERISR041MohoDresPureCpuExact.MapInterpolationMode)mode);

                int matches = 0;
                double maxError = 0.0;
                for (int i = 0; i < bilinearTests; i++)
                {
                    double u;
                    double v;
                    DependencyUv(i, out u, out v);
                    float live = map.GetPixelFloat(u, v);
                    float pure = AERISR041MohoDresPureCpuExact.EvaluateMap(candidate, u, v);
                    if (FloatBits(live) == FloatBits(pure)) matches++;
                    maxError = Math.Max(maxError, Math.Abs((double)live - (double)pure));
                }

                if (matches > bestMatches ||
                    (matches == bestMatches && maxError < bestMaxError))
                {
                    bestMode = mode;
                    bestMatches = matches;
                    bestMaxError = maxError;
                }
            }

            bool exact = bestIntegerMatches == integerTests && bestMatches == bilinearTests;
            var selected = new AERISR041MohoDresPureCpuExact.MapSnapshot(
                data, width, height, bpp, rowWidth, bestChannel,
                (AERISR041MohoDresPureCpuExact.MapInterpolationMode)bestMode);

            AERISLogger.Info(
                "[R041D][MAPSO_DEPENDENCY]" +
                "; body=" + bodyName +
                "; pass=" + exact +
                "; width=" + width +
                "; height=" + height +
                "; bpp=" + bpp +
                "; row_width=" + rowWidth +
                "; channel=" + bestChannel +
                "; integer_matches=" + bestIntegerMatches +
                "; integer_tests=" + integerTests +
                "; interpolation_mode=" +
                    ((AERISR041MohoDresPureCpuExact.MapInterpolationMode)bestMode) +
                "; bilinear_matches=" + bestMatches +
                "; bilinear_tests=" + bilinearTests +
                "; max_abs_error=" + R(bestMaxError) +
                "; data_bytes=" + data.Length +
                "; live_calls_thread=MAIN_THREAD_ONLY" +
                Invariants());

            return new MapSelection
            {
                Snapshot = selected,
                Exact = exact,
                IntegerMatches = bestIntegerMatches,
                IntegerTests = integerTests,
                BilinearMatches = bestMatches,
                BilinearTests = bilinearTests,
                MaxAbsError = bestMaxError
            };
        }

        CurveSelection SelectCurveSnapshot(string bodyName, AnimationCurve curve)
        {
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length == 0)
                throw new InvalidOperationException(bodyName + "_CURVE_NO_KEYS");

            var pureKeys = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                int weightedMode = ReadStructIntDefault(keys[i], "weightedMode", 0);
                pureKeys[i] = new AERISR041MohoDresPureCpuExact.CurveKeySnapshot(
                    keys[i].time,
                    keys[i].value,
                    keys[i].inTangent,
                    keys[i].outTangent,
                    weightedMode);
            }

            int bestMode = 0;
            int bestMatches = -1;
            double bestMaxError = double.PositiveInfinity;
            const int tests = 129;

            for (int mode = 0; mode < 4; mode++)
            {
                var candidate = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                    pureKeys,
                    (AERISR041MohoDresPureCpuExact.CurveEvaluationMode)mode,
                    (int)curve.preWrapMode,
                    (int)curve.postWrapMode);

                int matches = 0;
                double maxError = 0.0;
                for (int i = 0; i < tests; i++)
                {
                    float t = i / (float)(tests - 1);
                    float live = curve.Evaluate(t);
                    float pure = AERISR041MohoDresPureCpuExact.EvaluateCurve(candidate, t);
                    if (FloatBits(live) == FloatBits(pure)) matches++;
                    maxError = Math.Max(maxError, Math.Abs((double)live - (double)pure));
                }

                if (matches > bestMatches ||
                    (matches == bestMatches && maxError < bestMaxError))
                {
                    bestMode = mode;
                    bestMatches = matches;
                    bestMaxError = maxError;
                }
            }

            var selected = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                pureKeys,
                (AERISR041MohoDresPureCpuExact.CurveEvaluationMode)bestMode,
                (int)curve.preWrapMode,
                (int)curve.postWrapMode);

            bool exact = bestMatches == tests;

            AERISLogger.Info(
                "[R041D][CURVE_DEPENDENCY]" +
                "; body=" + bodyName +
                "; pass=" + exact +
                "; keys=" + keys.Length +
                "; weighted_keys=" + selected.HasWeightedKeys +
                "; pre_wrap=" + selected.PreWrapMode +
                "; post_wrap=" + selected.PostWrapMode +
                "; evaluation_mode=" + selected.Mode +
                "; matches=" + bestMatches +
                "; tests=" + tests +
                "; max_abs_error=" + R(bestMaxError) +
                "; live_calls_thread=MAIN_THREAD_ONLY" +
                Invariants());

            return new CurveSelection
            {
                Snapshot = selected,
                Exact = exact,
                Matches = bestMatches,
                Tests = tests,
                MaxAbsError = bestMaxError
            };
        }

        Sample[] CaptureSamples(CelestialBody body)
        {
            double[] latitudes =
            {
                -90.0, -75.0, -60.0, -45.0, -30.0, -15.0,
                0.0, 15.0, 30.0, 45.0, 60.0, 75.0, 90.0
            };
            double[] longitudes =
            {
                -180.0, -135.0, -90.0, -45.0, 0.0,
                45.0, 90.0, 135.0, 180.0
            };

            var samples = new List<Sample>(latitudes.Length * longitudes.Length + 32);
            for (int a = 0; a < latitudes.Length; a++)
            {
                for (int o = 0; o < longitudes.Length; o++)
                    AddSample(body, latitudes[a], longitudes[o], samples);
            }

            uint state = string.Equals(body.name, "Moho", StringComparison.OrdinalIgnoreCase)
                ? 0x041D4D4Fu
                : 0x041D4452u;
            for (int i = 0; i < 32; i++)
            {
                state = unchecked(state * 1664525u + 1013904223u);
                double lat = -89.5 + (state / 4294967295.0) * 179.0;
                state = unchecked(state * 1664525u + 1013904223u);
                double lon = -179.75 + (state / 4294967295.0) * 359.5;
                AddSample(body, lat, lon, samples);
            }

            if (samples.Count == 0)
                throw new InvalidOperationException(body.name + "_NO_PQS_WITNESSES");
            return samples.ToArray();
        }

        void AddSample(
            CelestialBody body,
            double latitude,
            double longitude,
            List<Sample> samples)
        {
            double authority;
            if (!AERISTerrainAwareness.TrySampleTerrainAslShared(
                body, latitude, longitude, out authority))
                return;

            Vector3d direction = body.GetRelSurfaceNVector(latitude, longitude);
            if (!Finite(direction.x) || !Finite(direction.y) || !Finite(direction.z) ||
                !Finite(authority))
                return;

            samples.Add(new Sample
            {
                Latitude = latitude,
                Longitude = longitude,
                X = direction.x,
                Y = direction.y,
                Z = direction.z,
                ExpectedAsl = authority
            });
        }

        static WorkerResult RunWorker(BodyCase[] cases, int mainThreadId)
        {
            var result = new WorkerResult();
            result.WorkerThreadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                result.Bodies = new BodyResult[cases.Length];
                for (int b = 0; b < cases.Length; b++)
                {
                    BodyCase body = cases[b];
                    var candidates = new CandidateResult[8];
                    int ci = 0;

                    for (int initial = 0; initial < 2; initial++)
                    {
                        for (int coord = 0; coord < 4; coord++)
                        {
                            var candidate = new CandidateResult
                            {
                                AbsoluteInitial = initial == 0,
                                CoordMode =
                                    (AERISR041MohoDresPureCpuExact.CoordMode)coord,
                                Samples = body.Samples.Length,
                                MaxError = 0.0
                            };

                            for (int s = 0; s < body.Samples.Length; s++)
                            {
                                Sample sample = body.Samples[s];
                                double actual = AERISR041MohoDresPureCpuExact.EvaluateBody(
                                    body.Snapshot,
                                    candidate.CoordMode,
                                    sample.Latitude,
                                    sample.Longitude,
                                    sample.X,
                                    sample.Y,
                                    sample.Z,
                                    candidate.AbsoluteInitial);

                                double error;
                                if (!Finite(actual) || !Finite(sample.ExpectedAsl))
                                    error = double.PositiveInfinity;
                                else
                                    error = Math.Abs(actual - sample.ExpectedAsl);

                                candidate.MaxError = Math.Max(candidate.MaxError, error);
                                if (error > ToleranceMeters)
                                    candidate.Failures++;
                            }

                            candidates[ci++] = candidate;
                        }
                    }

                    int exact = 0;
                    CandidateResult selected = null;
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (candidates[i].Failures == 0)
                        {
                            exact++;
                            if (selected == null) selected = candidates[i];
                        }
                    }

                    bool dependencies = body.MapDependencyExact && body.CurveDependencyExact;
                    bool pass = dependencies && exact == 1 &&
                        result.WorkerThreadId != mainThreadId;

                    result.Bodies[b] = new BodyResult
                    {
                        Name = body.Name,
                        DependenciesExact = dependencies,
                        Candidates = candidates,
                        ExactCandidates = exact,
                        Selected = selected,
                        Pass = pass
                    };
                }
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().FullName + ":" + ex.Message;
            }

            return result;
        }

        void Report(WorkerResult result)
        {
            if (result == null)
            {
                AERISLogger.Error("[R041D][SHADOW_FAIL]; stage=NULL_RESULT" + Invariants());
                return;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                AERISLogger.Error(
                    "[R041D][SHADOW_FAIL]" +
                    "; stage=WORKER" +
                    "; error=" + Safe(result.Error) +
                    "; main_thread_id=" + mainThreadId +
                    "; worker_thread_id=" + result.WorkerThreadId +
                    Invariants());
                return;
            }

            int passedBodies = 0;
            for (int b = 0; b < result.Bodies.Length; b++)
            {
                BodyResult body = result.Bodies[b];
                if (body == null) continue;

                CandidateResult best = null;
                for (int i = 0; i < body.Candidates.Length; i++)
                {
                    CandidateResult c = body.Candidates[i];
                    if (c == null) continue;
                    if (best == null || c.Failures < best.Failures ||
                        (c.Failures == best.Failures && c.MaxError < best.MaxError))
                        best = c;

                    AERISLogger.Info(
                        "[R041D][BODY_CANDIDATE]" +
                        "; body=" + body.Name +
                        "; initial=" + (c.AbsoluteInitial ? "ABSOLUTE_RADIUS" : "ZERO_DELTA") +
                        "; coord=" + c.CoordMode +
                        "; samples=" + c.Samples +
                        "; failures=" + c.Failures +
                        "; max_error_m=" + R(c.MaxError) +
                        "; tolerance_m=1E-08" +
                        "; worker_thread_id=" + result.WorkerThreadId +
                        "; main_thread_id=" + mainThreadId +
                        "; worker_runtime_object_access=false" +
                        "; production_authority=PQS" +
                        "; producer_switch=false");
                }

                if (body.Pass) passedBodies++;

                AERISLogger.Info(
                    "[R041D][BODY_RESULT]" +
                    "; body=" + body.Name +
                    "; pass=" + body.Pass +
                    "; dependencies_exact=" + body.DependenciesExact +
                    "; exact_candidates=" + body.ExactCandidates +
                    "; selected_initial=" +
                        (body.Selected == null ? "-" :
                         (body.Selected.AbsoluteInitial ? "ABSOLUTE_RADIUS" : "ZERO_DELTA")) +
                    "; selected_coord=" +
                        (body.Selected == null ? "-" : body.Selected.CoordMode.ToString()) +
                    "; best_failures=" + (best == null ? -1 : best.Failures) +
                    "; best_max_error_m=" + (best == null ? "NaN" : R(best.MaxError)) +
                    "; worker_thread_id=" + result.WorkerThreadId +
                    "; main_thread_id=" + mainThreadId +
                    "; worker_off_main_thread=" +
                        (result.WorkerThreadId != mainThreadId) +
                    Invariants());
            }

            bool completePass =
                result.Bodies != null &&
                result.Bodies.Length == TargetBodies.Length &&
                passedBodies == TargetBodies.Length &&
                result.WorkerThreadId != mainThreadId;

            AERISLogger.Info(
                "[R041D][SHADOW_COMPLETE]" +
                "; pass=" + completePass +
                "; candidate=" + Candidate +
                "; bodies_passed=" + passedBodies +
                "; bodies_total=" + TargetBodies.Length +
                "; main_thread_id=" + mainThreadId +
                "; worker_thread_id=" + result.WorkerThreadId +
                "; worker_off_main_thread=" +
                    (result.WorkerThreadId != mainThreadId) +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());
        }

        static AERISR039MinmusPureCpuExact.SimplexSnapshot SnapshotSimplex(object simplex)
        {
            int[] perm = CopyIntArray(RequireMember(simplex, "perm"), 512, "perm");
            int[][] grad3 = CopyJaggedIntArray(
                RequireMember(simplex, "grad3"), 12, "grad3");

            return new AERISR039MinmusPureCpuExact.SimplexSnapshot(
                perm,
                grad3,
                ReadDouble(simplex, "frequency"),
                ReadDouble(simplex, "octaves"),
                ReadDouble(simplex, "persistence"));
        }

        static AERISR039MinmusPureCpuExact.RidgedSnapshot SnapshotRidged(
            object noise,
            double[] randomVectors)
        {
            double[] spectral = CopyDoubleArray(
                RequireMember(noise, "SpectralWeights"), 30, "SpectralWeights");

            return new AERISR039MinmusPureCpuExact.RidgedSnapshot(
                ReadDouble(noise, "Frequency"),
                ReadInt(noise, "Seed"),
                ReadInt(noise, "NoiseQuality"),
                ReadDouble(noise, "Lacunarity"),
                ReadInt(noise, "OctaveCount"),
                spectral,
                randomVectors);
        }

        static double[] SnapshotLibNoiseRandomVectors()
        {
            Type basis = typeof(CelestialBody).Assembly.GetType(
                "LibNoise.GradientNoiseBasis", false);
            if (basis == null)
                throw new InvalidOperationException("LIBNOISE_BASIS_MISSING");

            FieldInfo field = FindField(
                basis,
                "RandomVectors",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(basis.FullName, "RandomVectors");

            return CopyDoubleArray(field.GetValue(null), 1024, "RandomVectors");
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body != null && string.Equals(
                    body.name, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
        }

        static void DependencyUv(int index, out double u, out double v)
        {
            switch (index)
            {
                case 0: u = 0.0; v = 0.0; return;
                case 1: u = 1.0; v = 0.0; return;
                case 2: u = 0.0; v = 1.0; return;
                case 3: u = 1.0; v = 1.0; return;
                case 4: u = 0.5; v = 0.5; return;
                case 5: u = 0.25; v = 0.75; return;
                case 6: u = 0.999999999; v = 0.5; return;
                case 7: u = 0.000000001; v = 0.5; return;
            }

            uint a = unchecked((uint)(0x9E3779B9u * (uint)(index + 1)));
            uint b = unchecked(a * 1664525u + 1013904223u);
            u = (a & 0x00FFFFFFu) / 16777215.0;
            v = (b & 0x00FFFFFFu) / 16777215.0;
        }

        static int FloatBits(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BitConverter.ToInt32(bytes, 0);
        }

        static int ClampInt(int value, int minimum, int maximum)
        {
            if (value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static object RequireMember(object target, string name)
        {
            object value = ReadMember(target, name);
            if (value == null)
                throw new MissingMemberException(
                    target == null ? "NULL" : target.GetType().FullName, name);
            return value;
        }

        static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();

            FieldInfo field = FindField(
                type, name,
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(field.IsStatic ? null : target);

            PropertyInfo property = FindProperty(type, name);
            if (property != null && property.CanRead &&
                property.GetIndexParameters().Length == 0)
            {
                MethodInfo getter = property.GetGetMethod(true);
                return property.GetValue(
                    getter != null && getter.IsStatic ? null : target, null);
            }

            field = FindField(
                type,
                "<" + name + ">k__BackingField",
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null)
                return field.GetValue(field.IsStatic ? null : target);

            return null;
        }

        static FieldInfo FindField(Type type, string name, BindingFlags flags)
        {
            Type current = type;
            while (current != null)
            {
                FieldInfo field = current.GetField(
                    name, flags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
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
                    BindingFlags.Instance | BindingFlags.Static |
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (property != null) return property;
                current = current.BaseType;
            }
            return null;
        }

        static double ReadDouble(object target, string name)
        {
            return Convert.ToDouble(RequireMember(target, name), CultureInfo.InvariantCulture);
        }

        static int ReadInt(object target, string name)
        {
            return Convert.ToInt32(RequireMember(target, name), CultureInfo.InvariantCulture);
        }

        static int ReadIntDefault(object target, string name, int fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            try { return Convert.ToInt32(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static bool ReadBoolDefault(object target, string name, bool fallback)
        {
            object value = ReadMember(target, name);
            if (value == null) return fallback;
            try { return Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static int ReadStructIntDefault(object value, string name, int fallback)
        {
            if (value == null) return fallback;
            try
            {
                object raw = ReadMember(value, name);
                return raw == null ? fallback :
                    Convert.ToInt32(raw, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        static int[] CopyIntArray(object value, int expected, string name)
        {
            int[] array = value as int[];
            if (array == null || array.Length != expected)
                throw new InvalidOperationException(name + "_LENGTH_OR_TYPE");
            return (int[])array.Clone();
        }

        static int[][] CopyJaggedIntArray(object value, int expected, string name)
        {
            int[][] array = value as int[][];
            if (array == null || array.Length != expected)
                throw new InvalidOperationException(name + "_LENGTH_OR_TYPE");
            int[][] copy = new int[array.Length][];
            for (int i = 0; i < array.Length; i++)
            {
                if (array[i] == null || array[i].Length < 3)
                    throw new InvalidOperationException(name + "_ROW_" + i);
                copy[i] = (int[])array[i].Clone();
            }
            return copy;
        }

        static double[] CopyDoubleArray(object value, int expected, string name)
        {
            double[] array = value as double[];
            if (array == null || array.Length != expected)
                throw new InvalidOperationException(name + "_LENGTH_OR_TYPE");
            return (double[])array.Clone();
        }

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace('\r', ' ').Replace('\n', ' ')
                .Replace(';', ',').Replace('|', '/');
        }

        static string Invariants()
        {
            return
                "; terrain_tolerance_m=1E-08" +
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; worker_runtime_object_access=false";
        }
    }
}
