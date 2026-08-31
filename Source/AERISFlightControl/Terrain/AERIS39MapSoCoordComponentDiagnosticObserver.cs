using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // MAPSO-3H diagnostic only.
    // Compares the actual Stock MapSO bilinear scratch fields against the
    // actual current pure evaluator scratch fields after identical double
    // inputs. No production/DB authority changes are made.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39MapSoCoordComponentDiagnosticObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_MAPSO3H_COORD_COMPONENT_DIAGNOSTIC_V1";

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

        sealed class CoordState
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

        int mainThreadId;
        bool done;
        float nextAttempt;

        void Awake()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            AERISLogger.Info("[AERIS39][MAPSO3H_BOOT]; candidate=" + Candidate + Invariants());
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
                "[AERIS39][MAPSO3H_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; stock_runtime_calls_thread=MAIN_THREAD_ONLY" +
                "; pure_probe_thread=MAIN_THREAD_ONLY" +
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
                        "[AERIS39][MAPSO3H_FAIL]" +
                        "; candidate=" + Candidate +
                        "; body=" + Safe(TargetBodies[i]) +
                        "; error=" + Safe(root.GetType().FullName + ":" + root.Message) +
                        Invariants());
                }
            }

            complete &= bodies == TargetBodies.Length;
            AERISLogger.Info(
                "[AERIS39][MAPSO3H_COMPLETE]" +
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

                byte[] liveData = ReadMember(map, "_data") as byte[];
                if (liveData == null)
                    throw new InvalidOperationException(bodyName + "_MAP_DATA_NOT_BYTE_ARRAY");

                int width = ReadInt(map, "_width");
                int height = ReadInt(map, "_height");
                int bpp = ReadInt(map, "_bpp");
                int rowWidth = ReadInt(map, "_rowWidth");
                var snapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                    (byte[])liveData.Clone(), width, height, bpp, rowWidth);

                const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo construct = map.GetType().GetMethod(
                    "ConstructBilinearCoords", instanceFlags, null,
                    new Type[] { typeof(double), typeof(double) }, null);
                if (construct == null)
                    throw new MissingMethodException(TypeName(map.GetType()), "ConstructBilinearCoords(double,double)");

                const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.NonPublic;
                FieldInfo pureScratchField = typeof(AERIS39MapSoPureCpuExact).GetField(
                    "threadBilinearScratch", staticFlags);
                if (pureScratchField == null)
                    throw new MissingFieldException(TypeName(typeof(AERIS39MapSoPureCpuExact)), "threadBilinearScratch");

                List<Sample> samples = BuildSamples(bodyName, modifierIndex);
                int checks = 0;
                int centerXMismatch = 0;
                int centerYMismatch = 0;
                int minXMismatch = 0;
                int maxXMismatch = 0;
                int minYMismatch = 0;
                int maxYMismatch = 0;
                int midXMismatch = 0;
                int midYMismatch = 0;
                int anyCoordMismatch = 0;
                var details = new List<string>();

                for (int s = 0; s < samples.Count; s++)
                {
                    Sample sample = samples[s];
                    checks++;

                    try
                    {
                        construct.Invoke(map, new object[] { sample.U, sample.V });
                    }
                    catch (TargetInvocationException tie)
                    {
                        throw RootException(tie);
                    }
                    CoordState stock = ReadStockState(map);

                    // Exercise the actual current pure evaluator, then inspect its
                    // thread-local CLR-only scratch object on this main diagnostic thread.
                    AERIS39MapSoPureCpuExact.GetPixelFloat(snapshot, sample.U, sample.V);
                    object pureScratch = pureScratchField.GetValue(null);
                    if (pureScratch == null)
                        throw new InvalidOperationException("PURE_SCRATCH_NULL");
                    CoordState pure = ReadPureState(pureScratch);

                    bool cx = DoubleBits(stock.CenterX) == DoubleBits(pure.CenterX);
                    bool cy = DoubleBits(stock.CenterY) == DoubleBits(pure.CenterY);
                    bool minx = stock.MinX == pure.MinX;
                    bool maxx = stock.MaxX == pure.MaxX;
                    bool miny = stock.MinY == pure.MinY;
                    bool maxy = stock.MaxY == pure.MaxY;
                    bool midx = FloatBits(stock.MidX) == FloatBits(pure.MidX);
                    bool midy = FloatBits(stock.MidY) == FloatBits(pure.MidY);

                    if (!cx) centerXMismatch++;
                    if (!cy) centerYMismatch++;
                    if (!minx) minXMismatch++;
                    if (!maxx) maxXMismatch++;
                    if (!miny) minYMismatch++;
                    if (!maxy) maxYMismatch++;
                    if (!midx) midXMismatch++;
                    if (!midy) midYMismatch++;

                    bool coordSame = minx && maxx && miny && maxy && midx && midy;
                    if (!coordSame) anyCoordMismatch++;

                    if (details.Count < 18 && (!cx || !cy || !coordSame))
                    {
                        details.Add(
                            sample.Label +
                            " u=" + Hex64(DoubleBits(sample.U)) +
                            " v=" + Hex64(DoubleBits(sample.V)) +
                            " stockCenterX=" + Hex64(DoubleBits(stock.CenterX)) +
                            " pureCenterX=" + Hex64(DoubleBits(pure.CenterX)) +
                            " stockCenterY=" + Hex64(DoubleBits(stock.CenterY)) +
                            " pureCenterY=" + Hex64(DoubleBits(pure.CenterY)) +
                            " stockMinX=" + stock.MinX.ToString(CultureInfo.InvariantCulture) +
                            " pureMinX=" + pure.MinX.ToString(CultureInfo.InvariantCulture) +
                            " stockMaxX=" + stock.MaxX.ToString(CultureInfo.InvariantCulture) +
                            " pureMaxX=" + pure.MaxX.ToString(CultureInfo.InvariantCulture) +
                            " stockMinY=" + stock.MinY.ToString(CultureInfo.InvariantCulture) +
                            " pureMinY=" + pure.MinY.ToString(CultureInfo.InvariantCulture) +
                            " stockMaxY=" + stock.MaxY.ToString(CultureInfo.InvariantCulture) +
                            " pureMaxY=" + pure.MaxY.ToString(CultureInfo.InvariantCulture) +
                            " stockMidX=" + Hex32(FloatBits(stock.MidX)) +
                            " pureMidX=" + Hex32(FloatBits(pure.MidX)) +
                            " stockMidY=" + Hex32(FloatBits(stock.MidY)) +
                            " pureMidY=" + Hex32(FloatBits(pure.MidY)));
                    }
                }

                string first = FirstDivergence(
                    centerXMismatch, centerYMismatch,
                    minXMismatch, maxXMismatch, minYMismatch, maxYMismatch,
                    midXMismatch, midYMismatch);

                AERISLogger.Info(
                    "[AERIS39][MAPSO3H_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(bodyName) +
                    "; checks=" + checks.ToString(CultureInfo.InvariantCulture) +
                    "; center_x_mismatch=" + centerXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; center_y_mismatch=" + centerYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; min_x_mismatch=" + minXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; max_x_mismatch=" + maxXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; min_y_mismatch=" + minYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; max_y_mismatch=" + maxYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; mid_x_mismatch=" + midXMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; mid_y_mismatch=" + midYMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; any_coord_mismatch=" + anyCoordMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; first_divergence=" + first +
                    Invariants());

                for (int d = 0; d < details.Count; d++)
                {
                    AERISLogger.Warn(
                        "[AERIS39][MAPSO3H_DETAIL]" +
                        "; body=" + Safe(bodyName) +
                        "; detail=" + Safe(details[d]) +
                        Invariants());
                }
                return;
            }

            throw new InvalidOperationException(bodyName + "_NO_ENABLED_VERTEX_HEIGHT_MAP");
        }

        static CoordState ReadStockState(MapSO map)
        {
            return new CoordState
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

        static CoordState ReadPureState(object scratch)
        {
            return new CoordState
            {
                CenterX = ReadDouble(scratch, "CenterXD"),
                CenterY = ReadDouble(scratch, "CenterYD"),
                MinX = ReadInt(scratch, "MinX"),
                MaxX = ReadInt(scratch, "MaxX"),
                MinY = ReadInt(scratch, "MinY"),
                MaxY = ReadInt(scratch, "MaxY"),
                MidX = ReadFloat(scratch, "MidX"),
                MidY = ReadFloat(scratch, "MidY")
            };
        }

        static string FirstDivergence(
            int cx, int cy, int minx, int maxx, int miny, int maxy, int midx, int midy)
        {
            if (cx != 0 || cy != 0) return "CENTER_DOUBLE";
            if (minx != 0 || maxx != 0 || miny != 0 || maxy != 0) return "INDEX_INT";
            if (midx != 0 || midy != 0) return "MID_FLOAT";
            return "NONE";
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

        static string Bool(bool value) { return value ? "true" : "false"; }
        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        static int FloatBits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value), 0); }
        static long DoubleBits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        static string Hex32(int bits) { return "0x" + unchecked((uint)bits).ToString("X8", CultureInfo.InvariantCulture); }
        static string Hex64(long bits) { return "0x" + unchecked((ulong)bits).ToString("X16", CultureInfo.InvariantCulture); }
    }
}
