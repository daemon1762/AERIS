using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // MAPSO-3I diagnostic only.
    // Determines whether Stock MapSO double-coordinate midX/midY behave like
    // normal double subtraction followed by conv.r4, or like an early
    // float-rounded center value. It also compares the Stock float overload
    // directly against the Stock double overload. Runtime object access is
    // main-thread diagnostic only; production/DB authority remains unchanged.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39MapSoMidLoweringDiagnosticObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_MAPSO3I_MID_LOWERING_DIAGNOSTIC_V1";

        static readonly string[] TargetBodies =
        {
            "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        sealed class Sample
        {
            internal string Label;
            internal double U;
            internal double V;
        }

        sealed class DoubleState
        {
            internal double CenterX;
            internal double CenterY;
            internal int MinX;
            internal int MaxX;
            internal int MinY;
            internal int MaxY;
            internal float MidX;
            internal float MidY;
        }

        sealed class FloatState
        {
            internal float CenterX;
            internal float CenterY;
            internal int MinX;
            internal int MaxX;
            internal int MinY;
            internal int MaxY;
            internal float MidX;
            internal float MidY;
        }

        int mainThreadId;
        bool done;
        float nextAttempt;

        void Awake()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            AERISLogger.Info("[AERIS39][MAPSO3I_BOOT]; candidate=" + Candidate + Invariants());
        }

        void Update()
        {
            if (done) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            done = true;
            Run();
        }

        void Run()
        {
            AERISLogger.Info(
                "[AERIS39][MAPSO3I_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; stock_runtime_calls_thread=MAIN_THREAD_ONLY" +
                Invariants());

            int bodies = 0;
            bool complete = true;

            for (int i = 0; i < TargetBodies.Length; i++)
            {
                try
                {
                    RunBody(TargetBodies[i]);
                    bodies++;
                }
                catch (Exception ex)
                {
                    complete = false;
                    Exception root = RootException(ex);
                    AERISLogger.Error(
                        "[AERIS39][MAPSO3I_FAIL]" +
                        "; candidate=" + Candidate +
                        "; body=" + Safe(TargetBodies[i]) +
                        "; error=" + Safe(root.GetType().FullName + ":" + root.Message) +
                        Invariants());
                }
            }

            complete &= bodies == TargetBodies.Length;
            AERISLogger.Info(
                "[AERIS39][MAPSO3I_COMPLETE]" +
                "; candidate=" + Candidate +
                "; diagnostic_complete=" + Bool(complete) +
                "; bodies=" + bodies.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        void RunBody(string bodyName)
        {
            CelestialBody body = FindBody(bodyName);
            if (body == null || body.pqsController == null)
                throw new InvalidOperationException(bodyName + "_PQS_MISSING");

            IList mods = GetModifierList(body.pqsController);
            if (mods == null)
                throw new InvalidOperationException(bodyName + "_MODIFIER_LIST_MISSING");

            for (int modifierIndex = 0; modifierIndex < mods.Count; modifierIndex++)
            {
                object mod = mods[modifierIndex];
                if (mod == null || !IsEnabled(mod)) continue;

                string modTypeName = TypeName(mod.GetType());
                if (!string.Equals(modTypeName, "PQSMod_VertexHeightMap", StringComparison.Ordinal) &&
                    !modTypeName.EndsWith(".PQSMod_VertexHeightMap", StringComparison.Ordinal))
                    continue;

                MapSO map = ReadMember(mod, "heightMap") as MapSO;
                if (map == null)
                    throw new InvalidOperationException(bodyName + "_HEIGHTMAP_MAPSO_MISSING");

                int width = ReadInt(map, "_width");
                int height = ReadInt(map, "_height");

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo constructD = map.GetType().GetMethod(
                    "ConstructBilinearCoords", flags, null,
                    new Type[] { typeof(double), typeof(double) }, null);
                MethodInfo constructF = map.GetType().GetMethod(
                    "ConstructBilinearCoords", flags, null,
                    new Type[] { typeof(float), typeof(float) }, null);
                if (constructD == null)
                    throw new MissingMethodException(TypeName(map.GetType()), "ConstructBilinearCoords(double,double)");
                if (constructF == null)
                    throw new MissingMethodException(TypeName(map.GetType()), "ConstructBilinearCoords(float,float)");

                List<Sample> samples = BuildSamples(bodyName, modifierIndex);
                int checks = 0;
                int centerDFormulaXMismatch = 0;
                int centerDFormulaYMismatch = 0;
                int centerFloatInputXMismatch = 0;
                int centerFloatInputYMismatch = 0;
                int midNormalXMismatch = 0;
                int midNormalYMismatch = 0;
                int midEarlyFloatXMismatch = 0;
                int midEarlyFloatYMismatch = 0;
                int doubleVsFloatMidXMismatch = 0;
                int doubleVsFloatMidYMismatch = 0;
                int doubleVsFloatIndexXMismatch = 0;
                int doubleVsFloatIndexYMismatch = 0;
                var details = new List<string>();

                for (int s = 0; s < samples.Count; s++)
                {
                    Sample sample = samples[s];
                    checks++;

                    Invoke(constructD, map, new object[] { sample.U, sample.V });
                    DoubleState d = ReadDoubleState(map);

                    float fu = (float)sample.U;
                    float fv = (float)sample.V;
                    Invoke(constructF, map, new object[] { fu, fv });
                    FloatState f = ReadFloatState(map);

                    double nx = Math.Abs(sample.U - Math.Floor(sample.U));
                    double ny = Math.Abs(sample.V - Math.Floor(sample.V));
                    double formulaCenterX = nx * (double)width;
                    double formulaCenterY = ny * (double)height;
                    double floatInputCenterX = (double)((float)nx) * (double)width;
                    double floatInputCenterY = (double)((float)ny) * (double)height;

                    float normalMidX = (float)(d.CenterX - (double)d.MinX);
                    float normalMidY = (float)(d.CenterY - (double)d.MinY);
                    float earlyMidX = (float)d.CenterX - (float)d.MinX;
                    float earlyMidY = (float)d.CenterY - (float)d.MinY;

                    bool centerDX = DoubleBits(d.CenterX) == DoubleBits(formulaCenterX);
                    bool centerDY = DoubleBits(d.CenterY) == DoubleBits(formulaCenterY);
                    bool centerFX = DoubleBits(d.CenterX) == DoubleBits(floatInputCenterX);
                    bool centerFY = DoubleBits(d.CenterY) == DoubleBits(floatInputCenterY);
                    bool normalX = FloatBits(d.MidX) == FloatBits(normalMidX);
                    bool normalY = FloatBits(d.MidY) == FloatBits(normalMidY);
                    bool earlyX = FloatBits(d.MidX) == FloatBits(earlyMidX);
                    bool earlyY = FloatBits(d.MidY) == FloatBits(earlyMidY);
                    bool floatMidX = FloatBits(d.MidX) == FloatBits(f.MidX);
                    bool floatMidY = FloatBits(d.MidY) == FloatBits(f.MidY);
                    bool floatIndexX = d.MinX == f.MinX && d.MaxX == f.MaxX;
                    bool floatIndexY = d.MinY == f.MinY && d.MaxY == f.MaxY;

                    if (!centerDX) centerDFormulaXMismatch++;
                    if (!centerDY) centerDFormulaYMismatch++;
                    if (!centerFX) centerFloatInputXMismatch++;
                    if (!centerFY) centerFloatInputYMismatch++;
                    if (!normalX) midNormalXMismatch++;
                    if (!normalY) midNormalYMismatch++;
                    if (!earlyX) midEarlyFloatXMismatch++;
                    if (!earlyY) midEarlyFloatYMismatch++;
                    if (!floatMidX) doubleVsFloatMidXMismatch++;
                    if (!floatMidY) doubleVsFloatMidYMismatch++;
                    if (!floatIndexX) doubleVsFloatIndexXMismatch++;
                    if (!floatIndexY) doubleVsFloatIndexYMismatch++;

                    if (details.Count < 24 && (!normalX || !normalY || !centerDX || !centerDY))
                    {
                        details.Add(
                            sample.Label +
                            " u=" + Hex64(DoubleBits(sample.U)) +
                            " v=" + Hex64(DoubleBits(sample.V)) +
                            " stockCenterX=" + Hex64(DoubleBits(d.CenterX)) +
                            " formulaCenterX=" + Hex64(DoubleBits(formulaCenterX)) +
                            " stockCenterY=" + Hex64(DoubleBits(d.CenterY)) +
                            " formulaCenterY=" + Hex64(DoubleBits(formulaCenterY)) +
                            " stockMidX=" + Hex32(FloatBits(d.MidX)) +
                            " normalMidX=" + Hex32(FloatBits(normalMidX)) +
                            " earlyMidX=" + Hex32(FloatBits(earlyMidX)) +
                            " floatOverloadMidX=" + Hex32(FloatBits(f.MidX)) +
                            " stockMidY=" + Hex32(FloatBits(d.MidY)) +
                            " normalMidY=" + Hex32(FloatBits(normalMidY)) +
                            " earlyMidY=" + Hex32(FloatBits(earlyMidY)) +
                            " floatOverloadMidY=" + Hex32(FloatBits(f.MidY)) +
                            " stockMinX=" + d.MinX.ToString(CultureInfo.InvariantCulture) +
                            " stockMaxX=" + d.MaxX.ToString(CultureInfo.InvariantCulture) +
                            " floatMinX=" + f.MinX.ToString(CultureInfo.InvariantCulture) +
                            " floatMaxX=" + f.MaxX.ToString(CultureInfo.InvariantCulture) +
                            " stockMinY=" + d.MinY.ToString(CultureInfo.InvariantCulture) +
                            " stockMaxY=" + d.MaxY.ToString(CultureInfo.InvariantCulture) +
                            " floatMinY=" + f.MinY.ToString(CultureInfo.InvariantCulture) +
                            " floatMaxY=" + f.MaxY.ToString(CultureInfo.InvariantCulture));
                    }
                }

                string classification;
                if (midEarlyFloatXMismatch == 0 && midEarlyFloatYMismatch == 0)
                    classification = "EARLY_FLOAT_CENTER_SUBTRACT_MATCHES_DOUBLE_OVERLOAD";
                else if (doubleVsFloatMidXMismatch == 0 && doubleVsFloatMidYMismatch == 0)
                    classification = "DOUBLE_MID_MATCHES_STOCK_FLOAT_OVERLOAD";
                else if (midNormalXMismatch == 0 && midNormalYMismatch == 0)
                    classification = "NORMAL_DOUBLE_SUBTRACT_CONV_R4";
                else
                    classification = "MIXED_OR_JIT_SPECIFIC_LOWERING";

                AERISLogger.Info(
                    "[AERIS39][MAPSO3I_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(bodyName) +
                    "; runtime_map_type=" + Safe(TypeName(map.GetType())) +
                    "; width=" + width.ToString(CultureInfo.InvariantCulture) +
                    "; height=" + height.ToString(CultureInfo.InvariantCulture) +
                    "; checks=" + checks.ToString(CultureInfo.InvariantCulture) +
                    "; center_double_formula_x_mismatch=" + centerDFormulaXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; center_double_formula_y_mismatch=" + centerDFormulaYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; center_float_input_x_mismatch=" + centerFloatInputXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; center_float_input_y_mismatch=" + centerFloatInputYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; mid_normal_x_mismatch=" + midNormalXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; mid_normal_y_mismatch=" + midNormalYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; mid_early_float_x_mismatch=" + midEarlyFloatXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; mid_early_float_y_mismatch=" + midEarlyFloatYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; double_vs_float_mid_x_mismatch=" + doubleVsFloatMidXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; double_vs_float_mid_y_mismatch=" + doubleVsFloatMidYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; double_vs_float_index_x_mismatch=" + doubleVsFloatIndexXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; double_vs_float_index_y_mismatch=" + doubleVsFloatIndexYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; classification=" + classification +
                    Invariants());

                for (int dIndex = 0; dIndex < details.Count; dIndex++)
                {
                    AERISLogger.Warn(
                        "[AERIS39][MAPSO3I_DETAIL]" +
                        "; body=" + Safe(bodyName) +
                        "; detail=" + Safe(details[dIndex]) +
                        Invariants());
                }
                return;
            }

            throw new InvalidOperationException(bodyName + "_NO_ENABLED_VERTEX_HEIGHT_MAP");
        }

        static void Invoke(MethodInfo method, object target, object[] args)
        {
            try { method.Invoke(target, args); }
            catch (TargetInvocationException tie) { throw RootException(tie); }
        }

        static DoubleState ReadDoubleState(MapSO map)
        {
            return new DoubleState
            {
                CenterX = ReadDouble(map, "centerXD"),
                CenterY = ReadDouble(map, "centerYD"),
                MinX = ReadInt(map, "minX"),
                MaxX = ReadInt(map, "maxX"),
                MinY = ReadInt(map, "minY"),
                MaxY = ReadInt(map, "maxY"),
                MidX = ReadFloat(map, "midX"),
                MidY = ReadFloat(map, "midY")
            };
        }

        static FloatState ReadFloatState(MapSO map)
        {
            return new FloatState
            {
                CenterX = ReadFloat(map, "centerX"),
                CenterY = ReadFloat(map, "centerY"),
                MinX = ReadInt(map, "minX"),
                MaxX = ReadInt(map, "maxX"),
                MinY = ReadInt(map, "minY"),
                MaxY = ReadInt(map, "maxY"),
                MidX = ReadFloat(map, "midX"),
                MidY = ReadFloat(map, "midY")
            };
        }

        static List<Sample> BuildSamples(string bodyName, int modifierIndex)
        {
            var result = new List<Sample>(300);
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

            uint state = Seed(bodyName, modifierIndex);
            for (int i = 0; i < 128; i++)
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

        static void AddSample(List<Sample> result, HashSet<string> seen, string label, double u, double v)
        {
            string key = DoubleBits(u).ToString("X16", CultureInfo.InvariantCulture) +
                ":" + DoubleBits(v).ToString("X16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;
            result.Add(new Sample { Label = label, U = u, V = v });
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

        static float ReadFloat(object target, string name)
        {
            object raw = ReadMember(target, name);
            if (raw == null) throw new MissingMemberException(TypeName(target.GetType()), name);
            return Convert.ToSingle(raw, CultureInfo.InvariantCulture);
        }

        static double ReadDouble(object target, string name)
        {
            object raw = ReadMember(target, name);
            if (raw == null) throw new MissingMemberException(TypeName(target.GetType()), name);
            return Convert.ToDouble(raw, CultureInfo.InvariantCulture);
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

        static string TypeName(Type type)
        {
            return type == null ? string.Empty : (type.FullName ?? type.Name ?? string.Empty);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        }

        static long DoubleBits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        static int FloatBits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }
        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        static string Hex64(long bits) { return "0x" + unchecked((ulong)bits).ToString("X16", CultureInfo.InvariantCulture); }
        static string Hex32(int bits) { return "0x" + unchecked((uint)bits).ToString("X8", CultureInfo.InvariantCulture); }
    }
}
