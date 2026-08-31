using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS39 MAPSO-3E diagnostic only.
    // Isolates the remaining real-body bit mismatch into:
    // - stock vs reconstructed bilinear coordinates;
    // - stock GreyFloat vs primitive byte snapshot corner samples;
    // - UnityEngine.Mathf.Lerp vs locally emitted arithmetic using Mathf.Clamp01;
    // - current pure worker candidate.
    //
    // All Unity/KSP/runtime-object access is main-thread diagnostic only.
    // Production/DB authority remains PQS; no producer switch or DB mutation.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39MapSoPipelineIsolationDiagnosticObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_MAPSO3E_PIPELINE_ISOLATION_DIAGNOSTIC_V1";
        const int Byte2FloatBits = unchecked((int)0x3B808081);
        static readonly float Byte2Float = FloatFromBits(Byte2FloatBits);

        static readonly string[] TargetBodies =
        {
            "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        struct Coords
        {
            internal int MinX;
            internal int MaxX;
            internal int MinY;
            internal int MaxY;
            internal float MidX;
            internal float MidY;
        }

        sealed class Sample
        {
            internal string Label;
            internal double U;
            internal double V;
        }

        int mainThreadId;
        bool done;
        float nextAttempt;

        void Awake()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
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
                "[AERIS39][MAPSO3E_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; unity_mathf_calls_thread=MAIN_THREAD_ONLY" +
                "; native_calls_thread=MAIN_THREAD_ONLY" +
                "; snapshot_payload=PRIMITIVES_ONLY" +
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
                    AERISLogger.Error(
                        "[AERIS39][MAPSO3E_FAIL]" +
                        "; candidate=" + Candidate +
                        "; body=" + Safe(TargetBodies[i]) +
                        "; error=" + Safe(RootException(ex).GetType().FullName + ":" + RootException(ex).Message) +
                        Invariants());
                }
            }

            complete &= bodies == TargetBodies.Length;
            AERISLogger.Info(
                "[AERIS39][MAPSO3E_COMPLETE]" +
                "; candidate=" + Candidate +
                "; diagnostic_complete=" + Bool(complete) +
                "; bodies=" + bodies.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; unity_mathf_calls_thread=MAIN_THREAD_ONLY" +
                "; native_calls_thread=MAIN_THREAD_ONLY" +
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

                byte[] liveData = ReadMember(map, "_data") as byte[];
                if (liveData == null)
                    throw new InvalidOperationException(bodyName + "_MAP_DATA_NOT_BYTE_ARRAY");

                byte[] data = (byte[])liveData.Clone();
                int width = ReadInt(map, "_width");
                int height = ReadInt(map, "_height");
                int bpp = ReadInt(map, "_bpp");
                int rowWidth = ReadInt(map, "_rowWidth");
                if (bpp != 1)
                    throw new InvalidOperationException(bodyName + "_EXPECTED_BPP1");

                var snapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                    data, width, height, bpp, rowWidth);

                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo construct = map.GetType().GetMethod(
                    "ConstructBilinearCoords", flags, null,
                    new Type[] { typeof(double), typeof(double) }, null);
                MethodInfo grey = map.GetType().GetMethod(
                    "GreyFloat", flags, null,
                    new Type[] { typeof(int), typeof(int) }, null);

                if (construct == null)
                    throw new MissingMethodException(TypeName(map.GetType()), "ConstructBilinearCoords(double,double)");
                if (grey == null)
                    throw new MissingMethodException(TypeName(map.GetType()), "GreyFloat(int,int)");

                List<Sample> samples = BuildSamples(bodyName, modifierIndex);

                int checks = 0;
                int coordinateChecks = 0;
                int coordinateMismatches = 0;
                int cornerChecks = 0;
                int cornerMismatches = 0;
                int nativeStockCoordsMathfMismatch = 0;
                int nativePureCoordsMathfMismatch = 0;
                int nativePureCoordsMathfClampMismatch = 0;
                int nativeCurrentPureMismatch = 0;
                var details = new List<string>();

                for (int s = 0; s < samples.Count; s++)
                {
                    Sample sample = samples[s];
                    checks++;

                    float native = map.GetPixelFloat(sample.U, sample.V);
                    int nativeBits = AERIS39MapSoPureCpuExact.FloatBits(native);

                    object boxedCoords;
                    try
                    {
                        boxedCoords = construct.Invoke(map, new object[] { sample.U, sample.V });
                    }
                    catch (TargetInvocationException tie)
                    {
                        throw RootException(tie);
                    }

                    Coords stockCoords = ReadCoords(boxedCoords);
                    Coords pureCoords = ConstructPureCoords(sample.U, sample.V, width, height);

                    coordinateChecks++;
                    bool coordsSame = SameCoords(stockCoords, pureCoords);
                    if (!coordsSame) coordinateMismatches++;

                    int localCornerMismatches = 0;
                    localCornerMismatches += CheckCorner(map, grey, data, rowWidth, stockCoords.MinX, stockCoords.MinY, ref cornerChecks);
                    localCornerMismatches += CheckCorner(map, grey, data, rowWidth, stockCoords.MaxX, stockCoords.MinY, ref cornerChecks);
                    localCornerMismatches += CheckCorner(map, grey, data, rowWidth, stockCoords.MinX, stockCoords.MaxY, ref cornerChecks);
                    localCornerMismatches += CheckCorner(map, grey, data, rowWidth, stockCoords.MaxX, stockCoords.MaxY, ref cornerChecks);
                    cornerMismatches += localCornerMismatches;

                    float stockCoordsMathf = SampleUsingMathf(data, rowWidth, stockCoords);
                    float pureCoordsMathf = SampleUsingMathf(data, rowWidth, pureCoords);
                    float pureCoordsMathfClamp = SampleUsingCustomLerpMathfClamp(data, rowWidth, pureCoords);
                    float currentPure = AERIS39MapSoPureCpuExact.GetPixelFloat(snapshot, sample.U, sample.V);

                    int a = AERIS39MapSoPureCpuExact.FloatBits(stockCoordsMathf);
                    int b = AERIS39MapSoPureCpuExact.FloatBits(pureCoordsMathf);
                    int c = AERIS39MapSoPureCpuExact.FloatBits(pureCoordsMathfClamp);
                    int d = AERIS39MapSoPureCpuExact.FloatBits(currentPure);

                    bool aSame = a == nativeBits;
                    bool bSame = b == nativeBits;
                    bool cSame = c == nativeBits;
                    bool dSame = d == nativeBits;

                    if (!aSame) nativeStockCoordsMathfMismatch++;
                    if (!bSame) nativePureCoordsMathfMismatch++;
                    if (!cSame) nativePureCoordsMathfClampMismatch++;
                    if (!dSame) nativeCurrentPureMismatch++;

                    if (details.Count < 12 && (!aSame || !bSame || !cSame || !dSame || !coordsSame || localCornerMismatches != 0))
                    {
                        details.Add(
                            sample.Label +
                            " native=" + Hex(nativeBits) +
                            " stockcoord_mathf=" + Hex(a) +
                            " purecoord_mathf=" + Hex(b) +
                            " purecoord_mathfclamp=" + Hex(c) +
                            " current_pure=" + Hex(d) +
                            " coords_same=" + Bool(coordsSame) +
                            " corner_mismatch=" + localCornerMismatches.ToString(CultureInfo.InvariantCulture));
                    }
                }

                string classification = Classify(
                    coordinateMismatches,
                    cornerMismatches,
                    nativeStockCoordsMathfMismatch,
                    nativePureCoordsMathfMismatch,
                    nativePureCoordsMathfClampMismatch,
                    nativeCurrentPureMismatch);

                AERISLogger.Info(
                    "[AERIS39][MAPSO3E_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(bodyName) +
                    "; runtime_map_type=" + Safe(TypeName(map.GetType())) +
                    "; checks=" + checks.ToString(CultureInfo.InvariantCulture) +
                    "; coordinate_checks=" + coordinateChecks.ToString(CultureInfo.InvariantCulture) +
                    "; coordinate_mismatch=" + coordinateMismatches.ToString(CultureInfo.InvariantCulture) +
                    "; corner_checks=" + cornerChecks.ToString(CultureInfo.InvariantCulture) +
                    "; corner_mismatch=" + cornerMismatches.ToString(CultureInfo.InvariantCulture) +
                    "; native_stockcoords_mathf_mismatch=" + nativeStockCoordsMathfMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; native_purecoords_mathf_mismatch=" + nativePureCoordsMathfMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; native_purecoords_mathfclamp_mismatch=" + nativePureCoordsMathfClampMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; native_currentpure_mismatch=" + nativeCurrentPureMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; classification=" + classification +
                    Invariants());

                for (int d = 0; d < details.Count; d++)
                {
                    AERISLogger.Warn(
                        "[AERIS39][MAPSO3E_DETAIL]" +
                        "; body=" + Safe(bodyName) +
                        "; detail=" + Safe(details[d]) +
                        Invariants());
                }

                return;
            }

            throw new InvalidOperationException(bodyName + "_NO_ENABLED_VERTEX_HEIGHT_MAP");
        }

        static int CheckCorner(
            MapSO map,
            MethodInfo grey,
            byte[] data,
            int rowWidth,
            int x,
            int y,
            ref int checks)
        {
            checks++;
            object raw;
            try
            {
                raw = grey.Invoke(map, new object[] { x, y });
            }
            catch (TargetInvocationException tie)
            {
                throw RootException(tie);
            }

            float stock = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            float pure = PureGrey(data, rowWidth, x, y);
            return AERIS39MapSoPureCpuExact.FloatBits(stock) ==
                   AERIS39MapSoPureCpuExact.FloatBits(pure) ? 0 : 1;
        }

        static float SampleUsingMathf(byte[] data, int rowWidth, Coords c)
        {
            float low = Mathf.Lerp(
                PureGrey(data, rowWidth, c.MinX, c.MinY),
                PureGrey(data, rowWidth, c.MaxX, c.MinY),
                c.MidX);
            float high = Mathf.Lerp(
                PureGrey(data, rowWidth, c.MinX, c.MaxY),
                PureGrey(data, rowWidth, c.MaxX, c.MaxY),
                c.MidX);
            return Mathf.Lerp(low, high, c.MidY);
        }

        static float SampleUsingCustomLerpMathfClamp(byte[] data, int rowWidth, Coords c)
        {
            float low = LerpUsingMathfClamp(
                PureGrey(data, rowWidth, c.MinX, c.MinY),
                PureGrey(data, rowWidth, c.MaxX, c.MinY),
                c.MidX);
            float high = LerpUsingMathfClamp(
                PureGrey(data, rowWidth, c.MinX, c.MaxY),
                PureGrey(data, rowWidth, c.MaxX, c.MaxY),
                c.MidX);
            return LerpUsingMathfClamp(low, high, c.MidY);
        }

        static float LerpUsingMathfClamp(float a, float b, float t)
        {
            return a + (b - a) * Mathf.Clamp01(t);
        }

        static float PureGrey(byte[] data, int rowWidth, int x, int y)
        {
            int index = unchecked(x + y * rowWidth);
            return Byte2Float * (float)data[index];
        }

        static Coords ConstructPureCoords(double x, double y, int width, int height)
        {
            x = Math.Abs(x - Math.Floor(x));
            y = Math.Abs(y - Math.Floor(y));

            double centerX = x * (double)width;
            int minX = (int)Math.Floor(centerX);
            int maxX = (int)Math.Ceiling(centerX);
            float midX = (float)(centerX - (double)minX);
            if (maxX == width) maxX = 0;

            double centerY = y * (double)height;
            int minY = (int)Math.Floor(centerY);
            int maxY = (int)Math.Ceiling(centerY);
            float midY = (float)(centerY - (double)minY);
            if (maxY == height) maxY = 0;

            Coords c = new Coords();
            c.MinX = minX;
            c.MaxX = maxX;
            c.MinY = minY;
            c.MaxY = maxY;
            c.MidX = midX;
            c.MidY = midY;
            return c;
        }

        static Coords ReadCoords(object boxed)
        {
            if (boxed == null) throw new InvalidOperationException("STOCK_COORDS_NULL");
            Coords c = new Coords();
            c.MinX = ReadIntAny(boxed, "minX", "MinX");
            c.MaxX = ReadIntAny(boxed, "maxX", "MaxX");
            c.MinY = ReadIntAny(boxed, "minY", "MinY");
            c.MaxY = ReadIntAny(boxed, "maxY", "MaxY");
            c.MidX = ReadFloatAny(boxed, "midX", "MidX");
            c.MidY = ReadFloatAny(boxed, "midY", "MidY");
            return c;
        }

        static bool SameCoords(Coords a, Coords b)
        {
            return a.MinX == b.MinX &&
                   a.MaxX == b.MaxX &&
                   a.MinY == b.MinY &&
                   a.MaxY == b.MaxY &&
                   AERIS39MapSoPureCpuExact.FloatBits(a.MidX) == AERIS39MapSoPureCpuExact.FloatBits(b.MidX) &&
                   AERIS39MapSoPureCpuExact.FloatBits(a.MidY) == AERIS39MapSoPureCpuExact.FloatBits(b.MidY);
        }

        static string Classify(
            int coordMismatch,
            int cornerMismatch,
            int stockCoordsMathfMismatch,
            int pureCoordsMathfMismatch,
            int pureCoordsMathfClampMismatch,
            int currentPureMismatch)
        {
            if (cornerMismatch != 0) return "GREY_OR_PIXEL_INDEX";
            if (stockCoordsMathfMismatch != 0) return "PRE_MATHF_OR_UNEXPECTED_STOCK_SEMANTICS";
            if (coordMismatch != 0 || pureCoordsMathfMismatch != 0) return "BILINEAR_COORDS_CODEGEN";
            if (pureCoordsMathfClampMismatch != 0) return "MATHF_LERP_METHOD_CODEGEN";
            if (currentPureMismatch != 0) return "CUSTOM_CLAMP_OR_PURE_CALL_SHAPE";
            return "PIPELINE_MATCHES_NATIVE";
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
            string key = BitConverter.DoubleToInt64Bits(u).ToString("X16", CultureInfo.InvariantCulture) +
                ":" + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
            if (!seen.Add(key)) return;
            result.Add(new Sample { Label = label, U = u, V = v });
        }

        static int ReadIntAny(object target, params string[] names)
        {
            object raw = ReadAny(target, names);
            if (raw == null) throw new MissingMemberException(TypeName(target.GetType()), string.Join("/", names));
            return Convert.ToInt32(raw, CultureInfo.InvariantCulture);
        }

        static float ReadFloatAny(object target, params string[] names)
        {
            object raw = ReadAny(target, names);
            if (raw == null) throw new MissingMemberException(TypeName(target.GetType()), string.Join("/", names));
            return Convert.ToSingle(raw, CultureInfo.InvariantCulture);
        }

        static object ReadAny(object target, params string[] names)
        {
            for (int n = 0; n < names.Length; n++)
            {
                object value = ReadMember(target, names[n]);
                if (value != null) return value;
            }
            return null;
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

        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        static string Hex(int bits) { return "0x" + unchecked((uint)bits).ToString("X8", CultureInfo.InvariantCulture); }

        static float FloatFromBits(int bits)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }
    }
}
