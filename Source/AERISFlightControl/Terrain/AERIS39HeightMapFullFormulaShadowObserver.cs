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
    // R041 HEIGHTMAP FULL FORMULA SHADOW.
    //
    // Main thread:
    // - discovers enabled PQSMod_VertexHeightMap instances;
    // - resolves effective MapSO runtime semantics (including Harmony/KSPCF);
    // - copies only immutable scalars/byte arrays into pure snapshots;
    // - captures live MapSO samples and applies the R041C-certified
    //   VertexHeightMap IL arithmetic order as the main-thread reference.
    // Worker:
    // - receives only primitives/arrays/pure CLR snapshots;
    // - evaluates pure MapSO + full HeightMap formula;
    // - requires exact IEEE-754 double-bit parity.
    //
    // No PQS modifier callback is invoked. Production/DB authority remains PQS.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39HeightMapFullFormulaShadowObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_R041_HEIGHTMAP_FULL_FORMULA_SHADOW_V1";
        static readonly string[] TargetBodies =
        {
            "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        sealed class CoordinateSample
        {
            internal string Label;
            internal double U;
            internal double V;
        }

        sealed class ExpectedCheck
        {
            internal string Label;
            internal double U;
            internal double V;
            internal double InputHeight;
            internal bool HasValue;
            internal long ValueBits;
            internal string ExceptionType;
        }

        sealed class BodyCase
        {
            internal string Name;
            internal int ModifierIndex;
            internal int ModifierOrder;
            internal int Width;
            internal int Height;
            internal int Bpp;
            internal int RowWidth;
            internal double Offset;
            internal double Deformity;
            internal string Semantics;
            internal string SemanticsEvidence;
            internal AERIS39HeightMapPureCpuExact.Snapshot Snapshot;
            internal ExpectedCheck[] Checks;
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
                StartShadow();
                return;
            }

            if (workerTask == null || !workerTask.IsCompleted) return;

            reported = true;
            try
            {
                Report(workerTask.Result);
            }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    "[AERIS39][HEIGHTMAP_SHADOW_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=WORKER_RESULT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        void StartShadow()
        {
            started = true;

            AERISLogger.Info(
                "[AERIS39][HEIGHTMAP_SHADOW_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; formula_authority=R041C_VERTEXHEIGHTMAP_IL_ORDER" +
                "; native_map_calls_thread=MAIN_THREAD_ONLY" +
                "; pqs_callbacks_invoked=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());

            try
            {
                var cases = new BodyCase[TargetBodies.Length];
                for (int i = 0; i < TargetBodies.Length; i++)
                    cases[i] = CaptureBody(TargetBodies[i]);

                // HARD BOUNDARY: cases contain no Unity/KSP/runtime objects.
                BodyCase[] purePayload = cases;
                workerTask = Task.Factory.StartNew(
                    () => RunWorker(purePayload),
                    CancellationToken.None,
                    TaskCreationOptions.None,
                    TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                reported = true;
                AERISLogger.Error(
                    "[AERIS39][HEIGHTMAP_SHADOW_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=MAIN_THREAD_SNAPSHOT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
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

            object selected = null;
            int selectedIndex = -1;
            for (int i = 0; i < mods.Count; i++)
            {
                object mod = mods[i];
                if (mod == null || !IsEnabled(mod)) continue;
                string typeName = TypeName(mod.GetType());
                if (!string.Equals(typeName, "PQSMod_VertexHeightMap", StringComparison.Ordinal) &&
                    !typeName.EndsWith(".PQSMod_VertexHeightMap", StringComparison.Ordinal))
                    continue;

                if (selected != null)
                    throw new InvalidOperationException(bodyName + "_MULTIPLE_ENABLED_VERTEX_HEIGHT_MAPS");
                selected = mod;
                selectedIndex = i;
            }

            if (selected == null)
                throw new InvalidOperationException(bodyName + "_NO_ENABLED_VERTEX_HEIGHT_MAP");

            MapSO map = ReadMember(selected, "heightMap") as MapSO;
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

            double offset = ReadDouble(selected, "heightMapOffset");
            double deformity = ReadDouble(selected, "heightMapDeformity");
            int order = ReadIntDefault(selected, "order", 0);

            string semanticsEvidence;
            AERIS39MapSoPureCpuExact.CoordinateSemantics semantics =
                AERIS39MapSoRuntimeSemanticsResolver.Resolve(map, out semanticsEvidence);

            var mapSnapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                data, width, height, bpp, rowWidth, semantics);
            var snapshot = new AERIS39HeightMapPureCpuExact.Snapshot(
                offset, deformity, mapSnapshot);

            List<CoordinateSample> coords = BuildSamples(width, height, bodyName, selectedIndex);
            double[] seedHeights = BuildSeedHeights(body.Radius, offset, deformity);
            var checks = new List<ExpectedCheck>(coords.Count * seedHeights.Length);

            for (int c = 0; c < coords.Count; c++)
            {
                CoordinateSample coord = coords[c];
                for (int h = 0; h < seedHeights.Length; h++)
                {
                    var check = new ExpectedCheck
                    {
                        Label = coord.Label + " HEIGHT_" + h.ToString(CultureInfo.InvariantCulture),
                        U = coord.U,
                        V = coord.V,
                        InputHeight = seedHeights[h]
                    };
                    CaptureReference(map, offset, deformity, check);
                    checks.Add(check);
                }
            }

            AERISLogger.Info(
                "[AERIS39][HEIGHTMAP_SHADOW_SNAPSHOT]" +
                "; candidate=" + Candidate +
                "; body=" + Safe(bodyName) +
                "; modifier_index=" + selectedIndex.ToString(CultureInfo.InvariantCulture) +
                "; order=" + order.ToString(CultureInfo.InvariantCulture) +
                "; width=" + width.ToString(CultureInfo.InvariantCulture) +
                "; height=" + height.ToString(CultureInfo.InvariantCulture) +
                "; bpp=" + bpp.ToString(CultureInfo.InvariantCulture) +
                "; row_width=" + rowWidth.ToString(CultureInfo.InvariantCulture) +
                "; offset=" + R(offset) +
                "; deformity=" + R(deformity) +
                "; coordinate_samples=" + coords.Count.ToString(CultureInfo.InvariantCulture) +
                "; height_seeds=" + seedHeights.Length.ToString(CultureInfo.InvariantCulture) +
                "; checks=" + checks.Count.ToString(CultureInfo.InvariantCulture) +
                "; map_semantics=" + semantics.ToString() +
                "; semantics_evidence=" + Safe(semanticsEvidence) +
                "; formula_authority=R041C_VERTEXHEIGHTMAP_IL_ORDER" +
                "; pqs_callbacks_invoked=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());

            return new BodyCase
            {
                Name = bodyName,
                ModifierIndex = selectedIndex,
                ModifierOrder = order,
                Width = width,
                Height = height,
                Bpp = bpp,
                RowWidth = rowWidth,
                Offset = offset,
                Deformity = deformity,
                Semantics = semantics.ToString(),
                SemanticsEvidence = semanticsEvidence,
                Snapshot = snapshot,
                Checks = checks.ToArray()
            };
        }

        static void CaptureReference(
            MapSO map,
            double offset,
            double deformity,
            ExpectedCheck check)
        {
            try
            {
                // Exact R041C PQSMod_VertexHeightMap arithmetic order, with
                // live runtime MapSO as the dependency reference.
                float pixel = map.GetPixelFloat(check.U, check.V);
                double product = deformity * (double)pixel;
                double value = check.InputHeight + offset;
                value = value + product;
                check.HasValue = true;
                check.ValueBits = BitConverter.DoubleToInt64Bits(value);
                check.ExceptionType = string.Empty;
            }
            catch (Exception ex)
            {
                check.HasValue = false;
                check.ValueBits = 0L;
                check.ExceptionType = ex.GetType().FullName ?? ex.GetType().Name;
            }
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

            for (int i = 0; i < body.Checks.Length; i++)
            {
                ExpectedCheck expected = body.Checks[i];
                result.Checks++;
                try
                {
                    double pure = AERIS39HeightMapPureCpuExact.Evaluate(
                        body.Snapshot,
                        expected.U,
                        expected.V,
                        expected.InputHeight);
                    long bits = AERIS39HeightMapPureCpuExact.DoubleBits(pure);

                    if (expected.HasValue && bits == expected.ValueBits)
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
                                ? "0x" + unchecked((ulong)expected.ValueBits).ToString("X16", CultureInfo.InvariantCulture)
                                : "EX:" + expected.ExceptionType) +
                            " pure=0x" + unchecked((ulong)bits).ToString("X16", CultureInfo.InvariantCulture));
                    }
                }
                catch (Exception ex)
                {
                    string type = ex.GetType().FullName ?? ex.GetType().Name;
                    if (!expected.HasValue && string.Equals(type, expected.ExceptionType, StringComparison.Ordinal))
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
                                ? "0x" + unchecked((ulong)expected.ValueBits).ToString("X16", CultureInfo.InvariantCulture)
                                : "EX:" + expected.ExceptionType) +
                            " pure=EX:" + type);
                    }
                }
            }

            result.FirstMismatches = mismatches.ToArray();
            result.Pass = result.Checks > 0 && result.Mismatches == 0 && result.ExceptionMismatches == 0;
            return result;
        }

        void Report(WorkerResult result)
        {
            if (result == null)
            {
                AERISLogger.Error(
                    "[AERIS39][HEIGHTMAP_SHADOW_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=NULL_WORKER_RESULT" + Invariants());
                return;
            }

            if (!string.IsNullOrEmpty(result.Error))
            {
                AERISLogger.Error(
                    "[AERIS39][HEIGHTMAP_SHADOW_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=WORKER" +
                    "; error=" + Safe(result.Error) + Invariants());
                return;
            }

            bool workerNotMain = result.WorkerThreadId != mainThreadId;
            bool pass = workerNotMain && result.Bodies != null && result.Bodies.Length == TargetBodies.Length;
            int checks = 0;
            int matches = 0;
            int exceptionMatches = 0;
            int exceptionMismatches = 0;
            int mismatches = 0;

            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult body = result.Bodies[i];
                pass &= body != null && body.Pass;
                if (body == null) continue;
                checks += body.Checks;
                matches += body.ValueMatches;
                exceptionMatches += body.ExceptionMatches;
                exceptionMismatches += body.ExceptionMismatches;
                mismatches += body.Mismatches;

                AERISLogger.Info(
                    "[AERIS39][HEIGHTMAP_SHADOW_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(body.Name) +
                    "; pass=" + Bool(body.Pass) +
                    "; checks=" + body.Checks.ToString(CultureInfo.InvariantCulture) +
                    "; value_matches=" + body.ValueMatches.ToString(CultureInfo.InvariantCulture) +
                    "; exception_matches=" + body.ExceptionMatches.ToString(CultureInfo.InvariantCulture) +
                    "; exception_mismatches=" + body.ExceptionMismatches.ToString(CultureInfo.InvariantCulture) +
                    "; mismatch_count=" + body.Mismatches.ToString(CultureInfo.InvariantCulture) +
                    "; bit_exact=" + Bool(body.Mismatches == 0) +
                    "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; pqs_callbacks_invoked=false" +
                    "; snapshot_payload=PRIMITIVES_ONLY" +
                    Invariants());

                if (body.FirstMismatches == null) continue;
                for (int m = 0; m < body.FirstMismatches.Length; m++)
                {
                    AERISLogger.Warn(
                        "[AERIS39][HEIGHTMAP_SHADOW_MISMATCH]" +
                        "; body=" + Safe(body.Name) +
                        "; detail=" + Safe(body.FirstMismatches[m]) +
                        Invariants());
                }
            }

            pass &= checks > 0 && mismatches == 0 && exceptionMismatches == 0;

            AERISLogger.Info(
                "[AERIS39][HEIGHTMAP_SHADOW_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; candidate=" + Candidate +
                "; bodies=" + (result.Bodies == null ? 0 : result.Bodies.Length).ToString(CultureInfo.InvariantCulture) +
                "; total_checks=" + checks.ToString(CultureInfo.InvariantCulture) +
                "; value_matches=" + matches.ToString(CultureInfo.InvariantCulture) +
                "; exception_match_count=" + exceptionMatches.ToString(CultureInfo.InvariantCulture) +
                "; exception_mismatch_count=" + exceptionMismatches.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" + Bool(mismatches == 0) +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; formula_authority=R041C_VERTEXHEIGHTMAP_IL_ORDER" +
                "; native_map_calls_thread=MAIN_THREAD_ONLY" +
                "; pqs_callbacks_invoked=false" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
                Invariants());
        }

        static double[] BuildSeedHeights(double radius, double offset, double deformity)
        {
            return new[]
            {
                0.0,
                radius,
                radius + 0.125,
                radius - 1234.56789,
                offset - deformity * 0.375
            };
        }

        static List<CoordinateSample> BuildSamples(
            int width,
            int height,
            string bodyName,
            int modifierIndex)
        {
            var result = new List<CoordinateSample>(700);
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
            {
                double u = (double)xs[i] / (double)width;
                AddBoundarySamples(result, seen, "X_EDGE_" + xs[i], u, 0.37109375, true);
            }
            for (int i = 0; i < ys.Length; i++)
            {
                double v = (double)ys[i] / (double)height;
                AddBoundarySamples(result, seen, "Y_EDGE_" + ys[i], 0.62890625, v, false);
            }

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

        static void AddBoundarySamples(
            List<CoordinateSample> result,
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
            List<CoordinateSample> result,
            HashSet<string> seen,
            string label,
            double u,
            double v)
        {
            string key = BitConverter.DoubleToInt64Bits(u).ToString("X16", CultureInfo.InvariantCulture) +
                ":" + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;
            result.Add(new CoordinateSample { Label = label, U = u, V = v });
        }

        static IList GetModifierList(object pqs)
        {
            object raw = ReadMember(pqs, "mods") ?? ReadMember(pqs, "modifiers") ?? ReadMember(pqs, "pqsMods");
            return raw as IList;
        }

        static bool IsEnabled(object mod)
        {
            object raw = ReadMember(mod, "modEnabled");
            if (raw is bool) return (bool)raw;
            raw = ReadMember(mod, "enabled");
            if (raw is bool) return (bool)raw;
            return true;
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

        static double ReadDouble(object target, string name)
        {
            object raw = ReadMember(target, name);
            if (raw == null) throw new MissingMemberException(TypeName(target.GetType()), name);
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        }

        static string TypeName(Type type)
        {
            return type == null ? string.Empty : (type.FullName ?? type.Name ?? string.Empty);
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
            if (value == 0.0)
                return BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000001UL));
            long bits = BitConverter.DoubleToInt64Bits(value);
            bits += value > 0.0 ? -1L : 1L;
            return BitConverter.Int64BitsToDouble(bits);
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

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    }
}
