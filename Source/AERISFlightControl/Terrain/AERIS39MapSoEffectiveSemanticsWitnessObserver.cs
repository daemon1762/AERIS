using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // MAPSO-3J formal real-body witness.
    // Main thread resolves the effective runtime MapSO coordinate semantics
    // (Stock or Harmony/KSPCommunityFixes) and freezes only a primitive enum
    // into the immutable worker snapshot. Runtime objects never cross the
    // worker boundary.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39MapSoEffectiveSemanticsWitnessObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_MAPSO3_REAL_BODY_HEIGHTMAP_WITNESS_V1";

        static readonly string[] TargetBodies =
        {
            "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        sealed class NativeSample
        {
            internal string Label;
            internal double U;
            internal double V;
            internal bool HasValue;
            internal int ValueBits;
            internal string ExceptionType;
        }

        sealed class BodyCase
        {
            internal string Name;
            internal int ModifierIndex;
            internal AERIS39MapSoPureCpuExact.MapSnapshot Snapshot;
            internal NativeSample[] Samples;
        }

        sealed class BodyResult
        {
            internal string Name;
            internal int Checks;
            internal int ValueMatches;
            internal int ExceptionMatches;
            internal int ExceptionMismatches;
            internal int Mismatches;
            internal string[] FirstMismatches;
            internal bool Pass;
        }

        sealed class WorkerResult
        {
            internal int WorkerThreadId;
            internal BodyResult[] Bodies;
            internal string Error;
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
                if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
                StartWitness();
                return;
            }

            if (workerTask == null || !workerTask.IsCompleted) return;
            reported = true;
            Report(workerTask.Result);
        }

        void StartWitness()
        {
            started = true;
            AERISLogger.Info(
                "[AERIS39][MAPSO3_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; native_calls_thread=MAIN_THREAD_ONLY" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());

            try
            {
                var cases = new BodyCase[TargetBodies.Length];
                for (int i = 0; i < TargetBodies.Length; i++)
                    cases[i] = CaptureBody(TargetBodies[i]);

                workerTask = Task.Factory.StartNew(
                    () => RunWorker(cases),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                reported = true;
                AERISLogger.Error(
                    "[AERIS39][MAPSO3_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=MAIN_THREAD_SNAPSHOT" +
                    "; error=" + Safe(RootException(ex).GetType().FullName + ":" + RootException(ex).Message) +
                    Invariants());
            }
        }

        BodyCase CaptureBody(string bodyName)
        {
            CelestialBody body = FindBody(bodyName);
            if (body == null || body.pqsController == null)
                throw new InvalidOperationException(bodyName + "_PQS_MISSING");

            IList mods = GetModifierList(body.pqsController);
            if (mods == null)
                throw new InvalidOperationException(bodyName + "_MODIFIER_LIST_MISSING");

            for (int i = 0; i < mods.Count; i++)
            {
                object mod = mods[i];
                if (mod == null || !IsEnabled(mod)) continue;
                string typeName = TypeName(mod.GetType());
                if (!string.Equals(typeName, "PQSMod_VertexHeightMap", StringComparison.Ordinal) &&
                    !typeName.EndsWith(".PQSMod_VertexHeightMap", StringComparison.Ordinal))
                    continue;

                MapSO map = ReadMember(mod, "heightMap") as MapSO;
                if (map == null)
                    throw new InvalidOperationException(bodyName + "_HEIGHTMAP_MAPSO_MISSING");

                byte[] data = ReadMember(map, "_data") as byte[];
                if (data == null)
                    throw new InvalidOperationException(bodyName + "_MAP_DATA_NOT_BYTE_ARRAY");

                int width = ReadInt(map, "_width");
                int height = ReadInt(map, "_height");
                int bpp = ReadInt(map, "_bpp");
                int rowWidth = ReadInt(map, "_rowWidth");
                if (width <= 0 || height <= 0 || bpp <= 0 || rowWidth <= 0)
                    throw new InvalidOperationException(bodyName + "_MAP_DIMENSIONS_INVALID");

                string semanticsEvidence;
                AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                    AERIS39MapSoRuntimeSemanticsResolver.Resolve(map, out semanticsEvidence);

                var snapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                    data, width, height, bpp, rowWidth, semantics);

                List<NativeSample> samples = BuildSamples(width, height, bodyName, i);
                for (int s = 0; s < samples.Count; s++)
                    CaptureNative(map, samples[s]);

                int order = ReadIntDefault(mod, "order", 0);
                AERISLogger.Info(
                    "[AERIS39][MAPSO3_SNAPSHOT]" +
                    "; body=" + Safe(bodyName) +
                    "; modifier_index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; order=" + order.ToString(CultureInfo.InvariantCulture) +
                    "; width=" + width.ToString(CultureInfo.InvariantCulture) +
                    "; height=" + height.ToString(CultureInfo.InvariantCulture) +
                    "; bpp=" + bpp.ToString(CultureInfo.InvariantCulture) +
                    "; row_width=" + rowWidth.ToString(CultureInfo.InvariantCulture) +
                    "; data_bytes=" + data.Length.ToString(CultureInfo.InvariantCulture) +
                    "; samples=" + samples.Count.ToString(CultureInfo.InvariantCulture) +
                    "; coordinate_semantics=" + Safe(semantics.ToString()) +
                    "; semantics_evidence=" + Safe(semanticsEvidence) +
                    "; native_calls_thread=MAIN_THREAD_ONLY" +
                    "; snapshot_payload=PRIMITIVES_ONLY" +
                    Invariants());

                return new BodyCase
                {
                    Name = bodyName,
                    ModifierIndex = i,
                    Snapshot = snapshot,
                    Samples = samples.ToArray()
                };
            }

            throw new InvalidOperationException(bodyName + "_NO_ENABLED_VERTEX_HEIGHT_MAP");
        }

        static WorkerResult RunWorker(BodyCase[] cases)
        {
            var result = new WorkerResult
            {
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId
            };

            try
            {
                result.Bodies = new BodyResult[cases.Length];
                for (int b = 0; b < cases.Length; b++)
                    result.Bodies[b] = EvaluateBody(cases[b]);
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().FullName + ":" + ex.Message;
            }

            return result;
        }

        static BodyResult EvaluateBody(BodyCase body)
        {
            var result = new BodyResult { Name = body.Name };
            var mismatches = new List<string>();

            for (int i = 0; i < body.Samples.Length; i++)
            {
                NativeSample expected = body.Samples[i];
                result.Checks++;

                try
                {
                    float pure = AERIS39MapSoPureCpuExact.GetPixelFloat(
                        body.Snapshot, expected.U, expected.V);
                    int pureBits = AERIS39MapSoPureCpuExact.FloatBits(pure);

                    if (expected.HasValue && pureBits == expected.ValueBits)
                    {
                        result.ValueMatches++;
                        continue;
                    }

                    result.Mismatches++;
                    if (!expected.HasValue) result.ExceptionMismatches++;
                    if (mismatches.Count < 12)
                    {
                        mismatches.Add(
                            expected.Label +
                            " native=" + (expected.HasValue
                                ? Hex(expected.ValueBits)
                                : "EX:" + expected.ExceptionType) +
                            " pure=" + Hex(pureBits));
                    }
                }
                catch (Exception ex)
                {
                    string pureType = ex.GetType().FullName ?? ex.GetType().Name;
                    if (!expected.HasValue &&
                        string.Equals(expected.ExceptionType, pureType, StringComparison.Ordinal))
                    {
                        result.ExceptionMatches++;
                        continue;
                    }

                    result.Mismatches++;
                    result.ExceptionMismatches++;
                    if (mismatches.Count < 12)
                    {
                        mismatches.Add(
                            expected.Label +
                            " native=" + (expected.HasValue
                                ? Hex(expected.ValueBits)
                                : "EX:" + expected.ExceptionType) +
                            " pure=EX:" + pureType);
                    }
                }
            }

            result.FirstMismatches = mismatches.ToArray();
            result.Pass = result.Checks > 0 && result.Mismatches == 0 && result.ExceptionMismatches == 0;
            return result;
        }

        void Report(WorkerResult result)
        {
            if (result == null || !string.IsNullOrEmpty(result.Error))
            {
                AERISLogger.Error(
                    "[AERIS39][MAPSO3_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=WORKER" +
                    "; error=" + Safe(result == null ? "NULL_WORKER_RESULT" : result.Error) +
                    Invariants());
                return;
            }

            bool workerNotMain = result.WorkerThreadId != mainThreadId;
            bool pass = workerNotMain && result.Bodies != null && result.Bodies.Length == TargetBodies.Length;
            int checks = 0;
            int valueMatches = 0;
            int exceptionMatches = 0;
            int exceptionMismatches = 0;
            int mismatches = 0;

            for (int b = 0; b < result.Bodies.Length; b++)
            {
                BodyResult body = result.Bodies[b];
                pass &= body != null && body.Pass;
                if (body == null) continue;

                checks += body.Checks;
                valueMatches += body.ValueMatches;
                exceptionMatches += body.ExceptionMatches;
                exceptionMismatches += body.ExceptionMismatches;
                mismatches += body.Mismatches;

                AERISLogger.Info(
                    "[AERIS39][MAPSO3_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(body.Name) +
                    "; pass=" + Bool(body.Pass) +
                    "; maps=1" +
                    "; checks=" + body.Checks.ToString(CultureInfo.InvariantCulture) +
                    "; value_matches=" + body.ValueMatches.ToString(CultureInfo.InvariantCulture) +
                    "; exception_matches=" + body.ExceptionMatches.ToString(CultureInfo.InvariantCulture) +
                    "; exception_mismatches=" + body.ExceptionMismatches.ToString(CultureInfo.InvariantCulture) +
                    "; mismatch_count=" + body.Mismatches.ToString(CultureInfo.InvariantCulture) +
                    "; bit_exact=" + Bool(body.Mismatches == 0) +
                    "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; snapshot_payload=PRIMITIVES_ONLY" +
                    Invariants());

                if (body.FirstMismatches == null) continue;
                for (int i = 0; i < body.FirstMismatches.Length; i++)
                {
                    AERISLogger.Warn(
                        "[AERIS39][MAPSO3_MISMATCH]" +
                        "; body=" + Safe(body.Name) +
                        "; detail=" + Safe(body.FirstMismatches[i]) +
                        Invariants());
                }
            }

            pass &= checks > 0 && mismatches == 0 && exceptionMismatches == 0;

            AERISLogger.Info(
                "[AERIS39][MAPSO3_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; candidate=" + Candidate +
                "; bodies=" + result.Bodies.Length.ToString(CultureInfo.InvariantCulture) +
                "; maps=" + result.Bodies.Length.ToString(CultureInfo.InvariantCulture) +
                "; total_checks=" + checks.ToString(CultureInfo.InvariantCulture) +
                "; value_matches=" + valueMatches.ToString(CultureInfo.InvariantCulture) +
                "; exception_match_count=" + exceptionMatches.ToString(CultureInfo.InvariantCulture) +
                "; exception_mismatch_count=" + exceptionMismatches.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" + Bool(mismatches == 0) +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; native_calls_thread=MAIN_THREAD_ONLY" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());
        }

        static List<NativeSample> BuildSamples(int width, int height, string bodyName, int modifierIndex)
        {
            var result = new List<NativeSample>(700);
            var seen = new HashSet<string>(StringComparer.Ordinal);

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

            for (int a = 0; a < latitudes.Length; a++)
            {
                for (int o = 0; o < longitudes.Length; o++)
                {
                    AddSample(result, seen,
                        "BODY_COORD lat=" + R(latitudes[a]) + " lon=" + R(longitudes[o]),
                        longitudes[o] / 360.0 + 0.5,
                        latitudes[a] / 180.0 + 0.5);
                }
            }

            int[] xs = DistinctIndices(width);
            int[] ys = DistinctIndices(height);
            for (int i = 0; i < xs.Length; i++)
                AddBoundarySamples(result, seen, "X_EDGE_" + xs[i], (double)xs[i] / width, 0.37109375, true);
            for (int i = 0; i < ys.Length; i++)
                AddBoundarySamples(result, seen, "Y_EDGE_" + ys[i], 0.62890625, (double)ys[i] / height, false);

            uint state = Seed(bodyName, modifierIndex);
            for (int i = 0; i < 384; i++)
            {
                state = Next(state);
                double u = (double)state / 4294967296.0;
                state = Next(state);
                double v = (double)state / 4294967296.0;
                AddSample(result, seen, "RANDOM_" + i.ToString(CultureInfo.InvariantCulture), u, v);
            }

            AddSample(result, seen, "PERIODIC_NEG_U", -0.125, 0.375);
            AddSample(result, seen, "PERIODIC_POS_U", 1.125, 0.375);
            AddSample(result, seen, "PERIODIC_NEG_V", 0.625, -0.125);
            AddSample(result, seen, "PERIODIC_POS_V", 0.625, 1.125);
            return result;
        }

        static int[] DistinctIndices(int dimension)
        {
            var values = new List<int>();
            AddIndex(values, 0, dimension);
            AddIndex(values, 1, dimension);
            AddIndex(values, dimension / 4, dimension);
            AddIndex(values, dimension / 3, dimension);
            AddIndex(values, dimension / 2, dimension);
            AddIndex(values, dimension * 2 / 3, dimension);
            AddIndex(values, dimension * 3 / 4, dimension);
            AddIndex(values, Math.Max(0, dimension - 2), dimension);
            AddIndex(values, Math.Max(0, dimension - 1), dimension);
            AddIndex(values, dimension, dimension);
            return values.ToArray();
        }

        static void AddIndex(List<int> values, int value, int dimension)
        {
            if (value < 0 || value > dimension) return;
            if (!values.Contains(value)) values.Add(value);
        }

        static void AddBoundarySamples(List<NativeSample> result, HashSet<string> seen, string label, double axis, double other, bool xAxis)
        {
            double down = NextDoubleDown(axis);
            double up = NextDoubleUp(axis);
            if (xAxis)
            {
                AddSample(result, seen, label + "_EXACT", axis, other);
                AddSample(result, seen, label + "_DOWN", down, other);
                AddSample(result, seen, label + "_UP", up, other);
            }
            else
            {
                AddSample(result, seen, label + "_EXACT", other, axis);
                AddSample(result, seen, label + "_DOWN", other, down);
                AddSample(result, seen, label + "_UP", other, up);
            }
        }

        static void AddSample(List<NativeSample> result, HashSet<string> seen, string label, double u, double v)
        {
            string key = BitConverter.DoubleToInt64Bits(u).ToString("X16", CultureInfo.InvariantCulture) +
                ":" + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;
            result.Add(new NativeSample { Label = label, U = u, V = v });
        }

        static void CaptureNative(MapSO map, NativeSample sample)
        {
            try
            {
                float value = map.GetPixelFloat(sample.U, sample.V);
                sample.HasValue = true;
                sample.ValueBits = AERIS39MapSoPureCpuExact.FloatBits(value);
                sample.ExceptionType = string.Empty;
            }
            catch (Exception ex)
            {
                Exception root = RootException(ex);
                sample.HasValue = false;
                sample.ExceptionType = root.GetType().FullName ?? root.GetType().Name;
            }
        }

        static IList GetModifierList(object pqs)
        {
            return (ReadMember(pqs, "mods") ?? ReadMember(pqs, "modifiers") ?? ReadMember(pqs, "pqsMods")) as IList;
        }

        static bool IsEnabled(object mod)
        {
            object raw = ReadMember(mod, "modEnabled");
            if (raw is bool) return (bool)raw;
            raw = ReadMember(mod, "enabled");
            return !(raw is bool) || (bool)raw;
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

        static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type cursor = target.GetType(); cursor != null; cursor = cursor.BaseType)
            {
                FieldInfo field = cursor.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    try { return field.GetValue(target); } catch { }
                }
                PropertyInfo property = cursor.GetProperty(name, flags | BindingFlags.DeclaredOnly);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                {
                    try { return property.GetValue(target, null); } catch { }
                }
            }
            return null;
        }

        static int ReadInt(object target, string name)
        {
            object raw = ReadMember(target, name);
            if (raw == null) throw new MissingMemberException(TypeName(target.GetType()), name);
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        static int ReadIntDefault(object target, string name, int fallback)
        {
            object raw = ReadMember(target, name);
            if (raw == null) return fallback;
            try { return Convert.ToInt32(raw, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static uint Seed(string bodyName, int modifierIndex)
        {
            uint h = 2166136261u;
            string value = bodyName ?? string.Empty;
            for (int i = 0; i < value.Length; i++)
            {
                h ^= value[i];
                h = unchecked(h * 16777619u);
            }
            h ^= unchecked((uint)modifierIndex * 0x9E3779B9u);
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
            if (value == 0.0) return BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000001UL));
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? -1L : 1L;
            return BitConverter.Int64BitsToDouble(bits);
        }

        static Exception RootException(Exception ex)
        {
            Exception current = ex;
            while (current is TargetInvocationException && current.InnerException != null)
                current = current.InnerException;
            return current;
        }

        static string Invariants()
        {
            return
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; production_worker_runtime_object_access=false";
        }

        static string TypeName(Type type) { return type == null ? string.Empty : (type.FullName ?? type.Name ?? string.Empty); }
        static string Safe(string value) { return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' '); }
        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        static string Hex(int bits) { return "0x" + unchecked((uint)bits).ToString("X8", CultureInfo.InvariantCulture); }
    }
}
