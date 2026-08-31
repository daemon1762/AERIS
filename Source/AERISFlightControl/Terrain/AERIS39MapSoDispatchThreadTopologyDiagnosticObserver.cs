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
    // AERIS39 MAPSO-3 diagnostic classifier.
    // Separates three possible causes of the real-body ULP mismatch:
    //  1) live MapSO virtual dispatch/derived override;
    //  2) main-thread vs worker-thread runtime/JIT behavior;
    //  3) static-snapshot helper topology vs stock-like instance topology.
    // Production/DB authority remains PQS. No producer switch or DB write.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS39MapSoDispatchThreadTopologyDiagnosticObserver : MonoBehaviour
    {
        const string Candidate = "AERIS39_MAPSO3_DISPATCH_THREAD_TOPOLOGY_DIAGNOSTIC_V1";

        static readonly string[] TargetBodies =
        {
            "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        sealed class Sample
        {
            internal string Label;
            internal double U;
            internal double V;

            internal bool NativeHas;
            internal int NativeBits;
            internal string NativeEx;

            internal bool StaticMainHas;
            internal int StaticMainBits;
            internal string StaticMainEx;

            internal bool TopologyMainHas;
            internal int TopologyMainBits;
            internal string TopologyMainEx;
        }

        sealed class MapCase
        {
            internal string Body;
            internal int ModifierIndex;
            internal int Width;
            internal int Height;
            internal int Bpp;
            internal int RowWidth;
            internal string RuntimeMapType;
            internal string DispatchDeclaringType;
            internal string BaseDefinitionDeclaringType;
            internal AERIS39MapSoPureCpuExact.MapSnapshot Snapshot;
            internal Sample[] Samples;
        }

        sealed class BodyCase
        {
            internal string Name;
            internal MapCase Map;
        }

        sealed class BodyResult
        {
            internal string Name;
            internal string RuntimeMapType;
            internal string DispatchDeclaringType;
            internal string BaseDefinitionDeclaringType;
            internal int Checks;
            internal int NativeStaticMainMismatch;
            internal int NativeTopologyMainMismatch;
            internal int StaticMainWorkerMismatch;
            internal int TopologyMainWorkerMismatch;
            internal int NativeStaticWorkerMismatch;
            internal int NativeTopologyWorkerMismatch;
            internal string[] FirstDetails;
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
                StartDiagnostic();
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
                    "[AERIS39][MAPSO3D_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=REPORT" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        void StartDiagnostic()
        {
            started = true;

            AERISLogger.Info(
                "[AERIS39][MAPSO3D_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; target_bodies=" + string.Join(",", TargetBodies) +
                "; native_calls_thread=MAIN_THREAD_ONLY" +
                "; pure_main_probe=true" +
                "; pure_worker_probe=true" +
                "; topology_probe=PRIMITIVES_ONLY_INSTANCE_VIRTUAL" +
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
                    "[AERIS39][MAPSO3D_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=MAIN_THREAD_CAPTURE" +
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

            for (int i = 0; i < mods.Count; i++)
            {
                object mod = mods[i];
                if (mod == null || !IsEnabled(mod)) continue;

                Type modType = mod.GetType();
                string modTypeName = modType.FullName ?? modType.Name ?? string.Empty;
                if (!string.Equals(modTypeName, "PQSMod_VertexHeightMap", StringComparison.Ordinal) &&
                    !modTypeName.EndsWith(".PQSMod_VertexHeightMap", StringComparison.Ordinal))
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

                var snapshot = new AERIS39MapSoPureCpuExact.MapSnapshot(
                    data, width, height, bpp, rowWidth);
                var topology = new AERIS39MapSoPureCpuTopologyProbe(
                    data, width, height, bpp, rowWidth);

                Type runtimeType = map.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                MethodInfo dispatch = runtimeType.GetMethod(
                    "GetPixelFloat", flags, null,
                    new Type[] { typeof(double), typeof(double) }, null);
                MethodInfo baseDefinition = dispatch == null ? null : dispatch.GetBaseDefinition();

                string runtimeTypeName = TypeName(runtimeType);
                string dispatchDeclaring = dispatch == null ? "<missing>" : TypeName(dispatch.DeclaringType);
                string baseDeclaring = baseDefinition == null ? "<missing>" : TypeName(baseDefinition.DeclaringType);

                List<Sample> samples = BuildSamples(bodyName, i);
                for (int s = 0; s < samples.Count; s++)
                {
                    CaptureNative(map, samples[s]);
                    CaptureStaticMain(snapshot, samples[s]);
                    CaptureTopologyMain(topology, samples[s]);
                }

                AERISLogger.Info(
                    "[AERIS39][MAPSO3D_MAP]" +
                    "; body=" + Safe(bodyName) +
                    "; modifier_index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; runtime_map_type=" + Safe(runtimeTypeName) +
                    "; dispatch_declaring_type=" + Safe(dispatchDeclaring) +
                    "; base_definition_declaring_type=" + Safe(baseDeclaring) +
                    "; width=" + width.ToString(CultureInfo.InvariantCulture) +
                    "; height=" + height.ToString(CultureInfo.InvariantCulture) +
                    "; bpp=" + bpp.ToString(CultureInfo.InvariantCulture) +
                    "; row_width=" + rowWidth.ToString(CultureInfo.InvariantCulture) +
                    "; samples=" + samples.Count.ToString(CultureInfo.InvariantCulture) +
                    Invariants());

                return new BodyCase
                {
                    Name = bodyName,
                    Map = new MapCase
                    {
                        Body = bodyName,
                        ModifierIndex = i,
                        Width = width,
                        Height = height,
                        Bpp = bpp,
                        RowWidth = rowWidth,
                        RuntimeMapType = runtimeTypeName,
                        DispatchDeclaringType = dispatchDeclaring,
                        BaseDefinitionDeclaringType = baseDeclaring,
                        Snapshot = snapshot,
                        Samples = samples.ToArray()
                    }
                };
            }

            throw new InvalidOperationException(bodyName + "_NO_ENABLED_VERTEX_HEIGHT_MAP");
        }

        static WorkerResult RunWorker(BodyCase[] cases)
        {
            var result = new WorkerResult();
            result.WorkerThreadId = Thread.CurrentThread.ManagedThreadId;

            try
            {
                result.Bodies = new BodyResult[cases.Length];
                for (int i = 0; i < cases.Length; i++)
                    result.Bodies[i] = EvaluateBody(cases[i]);
            }
            catch (Exception ex)
            {
                result.Error = ex.GetType().FullName + ":" + ex.Message;
            }

            return result;
        }

        static BodyResult EvaluateBody(BodyCase body)
        {
            MapCase map = body.Map;
            var topology = new AERIS39MapSoPureCpuTopologyProbe(
                map.Snapshot.Data,
                map.Width,
                map.Height,
                map.Bpp,
                map.RowWidth);

            var result = new BodyResult
            {
                Name = body.Name,
                RuntimeMapType = map.RuntimeMapType,
                DispatchDeclaringType = map.DispatchDeclaringType,
                BaseDefinitionDeclaringType = map.BaseDefinitionDeclaringType
            };
            var details = new List<string>();

            for (int i = 0; i < map.Samples.Length; i++)
            {
                Sample s = map.Samples[i];
                result.Checks++;

                bool staticWorkerHas;
                int staticWorkerBits;
                string staticWorkerEx;
                EvalStatic(map.Snapshot, s.U, s.V,
                    out staticWorkerHas, out staticWorkerBits, out staticWorkerEx);

                bool topologyWorkerHas;
                int topologyWorkerBits;
                string topologyWorkerEx;
                EvalTopology(topology, s.U, s.V,
                    out topologyWorkerHas, out topologyWorkerBits, out topologyWorkerEx);

                if (!Same(s.NativeHas, s.NativeBits, s.NativeEx,
                          s.StaticMainHas, s.StaticMainBits, s.StaticMainEx))
                    result.NativeStaticMainMismatch++;

                if (!Same(s.NativeHas, s.NativeBits, s.NativeEx,
                          s.TopologyMainHas, s.TopologyMainBits, s.TopologyMainEx))
                    result.NativeTopologyMainMismatch++;

                if (!Same(s.StaticMainHas, s.StaticMainBits, s.StaticMainEx,
                          staticWorkerHas, staticWorkerBits, staticWorkerEx))
                    result.StaticMainWorkerMismatch++;

                if (!Same(s.TopologyMainHas, s.TopologyMainBits, s.TopologyMainEx,
                          topologyWorkerHas, topologyWorkerBits, topologyWorkerEx))
                    result.TopologyMainWorkerMismatch++;

                bool nativeStaticWorkerSame = Same(
                    s.NativeHas, s.NativeBits, s.NativeEx,
                    staticWorkerHas, staticWorkerBits, staticWorkerEx);
                if (!nativeStaticWorkerSame)
                    result.NativeStaticWorkerMismatch++;

                bool nativeTopologyWorkerSame = Same(
                    s.NativeHas, s.NativeBits, s.NativeEx,
                    topologyWorkerHas, topologyWorkerBits, topologyWorkerEx);
                if (!nativeTopologyWorkerSame)
                    result.NativeTopologyWorkerMismatch++;

                if (details.Count < 10 &&
                    (!nativeStaticWorkerSame || !nativeTopologyWorkerSame))
                {
                    details.Add(
                        s.Label +
                        " native=" + Outcome(s.NativeHas, s.NativeBits, s.NativeEx) +
                        " static_main=" + Outcome(s.StaticMainHas, s.StaticMainBits, s.StaticMainEx) +
                        " static_worker=" + Outcome(staticWorkerHas, staticWorkerBits, staticWorkerEx) +
                        " topo_main=" + Outcome(s.TopologyMainHas, s.TopologyMainBits, s.TopologyMainEx) +
                        " topo_worker=" + Outcome(topologyWorkerHas, topologyWorkerBits, topologyWorkerEx));
                }
            }

            result.FirstDetails = details.ToArray();
            return result;
        }

        void Report(WorkerResult result)
        {
            if (result == null || !string.IsNullOrEmpty(result.Error))
            {
                AERISLogger.Error(
                    "[AERIS39][MAPSO3D_FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=WORKER" +
                    "; error=" + Safe(result == null ? "NULL_RESULT" : result.Error) +
                    Invariants());
                return;
            }

            bool workerNotMain = result.WorkerThreadId != mainThreadId;
            bool complete = workerNotMain && result.Bodies != null &&
                result.Bodies.Length == TargetBodies.Length;

            for (int i = 0; i < result.Bodies.Length; i++)
            {
                BodyResult b = result.Bodies[i];
                if (b == null)
                {
                    complete = false;
                    continue;
                }

                string classification = Classify(b);
                AERISLogger.Info(
                    "[AERIS39][MAPSO3D_BODY]" +
                    "; candidate=" + Candidate +
                    "; body=" + Safe(b.Name) +
                    "; runtime_map_type=" + Safe(b.RuntimeMapType) +
                    "; dispatch_declaring_type=" + Safe(b.DispatchDeclaringType) +
                    "; base_definition_declaring_type=" + Safe(b.BaseDefinitionDeclaringType) +
                    "; checks=" + b.Checks.ToString(CultureInfo.InvariantCulture) +
                    "; native_static_main_mismatch=" + b.NativeStaticMainMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; native_topology_main_mismatch=" + b.NativeTopologyMainMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; static_main_worker_mismatch=" + b.StaticMainWorkerMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; topology_main_worker_mismatch=" + b.TopologyMainWorkerMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; native_static_worker_mismatch=" + b.NativeStaticWorkerMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; native_topology_worker_mismatch=" + b.NativeTopologyWorkerMismatch.ToString(CultureInfo.InvariantCulture) +
                    "; classification=" + classification +
                    "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                    "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    Invariants());

                if (b.FirstDetails != null)
                {
                    for (int d = 0; d < b.FirstDetails.Length; d++)
                    {
                        AERISLogger.Warn(
                            "[AERIS39][MAPSO3D_DETAIL]" +
                            "; body=" + Safe(b.Name) +
                            "; detail=" + Safe(b.FirstDetails[d]) +
                            Invariants());
                    }
                }
            }

            AERISLogger.Info(
                "[AERIS39][MAPSO3D_COMPLETE]" +
                "; candidate=" + Candidate +
                "; diagnostic_complete=" + Bool(complete) +
                "; bodies=" + (result.Bodies == null ? 0 : result.Bodies.Length).ToString(CultureInfo.InvariantCulture) +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                Invariants());
        }

        static string Classify(BodyResult b)
        {
            if (!string.Equals(b.DispatchDeclaringType, "MapSO", StringComparison.Ordinal))
                return "RUNTIME_OVERRIDE_OR_DERIVED_DISPATCH";
            if (b.NativeTopologyMainMismatch == 0 && b.NativeTopologyWorkerMismatch == 0)
                return "INSTANCE_TOPOLOGY_SOLVES";
            if (b.NativeStaticMainMismatch == 0 && b.NativeStaticWorkerMismatch > 0 &&
                b.StaticMainWorkerMismatch > 0)
                return "THREAD_OR_JIT_STATE";
            if (b.StaticMainWorkerMismatch == 0 && b.TopologyMainWorkerMismatch == 0)
                return "METHOD_SHAPE_OR_RUNTIME_CODEGEN";
            return "MIXED_REQUIRES_FURTHER_CLOSURE";
        }

        static void CaptureNative(MapSO map, Sample sample)
        {
            try
            {
                float value = map.GetPixelFloat(sample.U, sample.V);
                sample.NativeHas = true;
                sample.NativeBits = AERIS39MapSoPureCpuExact.FloatBits(value);
                sample.NativeEx = string.Empty;
            }
            catch (Exception ex)
            {
                sample.NativeHas = false;
                sample.NativeBits = 0;
                sample.NativeEx = TypeName(ex.GetType());
            }
        }

        static void CaptureStaticMain(AERIS39MapSoPureCpuExact.MapSnapshot snapshot, Sample sample)
        {
            EvalStatic(snapshot, sample.U, sample.V,
                out sample.StaticMainHas, out sample.StaticMainBits, out sample.StaticMainEx);
        }

        static void CaptureTopologyMain(AERIS39MapSoPureCpuTopologyProbe topology, Sample sample)
        {
            EvalTopology(topology, sample.U, sample.V,
                out sample.TopologyMainHas, out sample.TopologyMainBits, out sample.TopologyMainEx);
        }

        static void EvalStatic(
            AERIS39MapSoPureCpuExact.MapSnapshot snapshot,
            double u,
            double v,
            out bool has,
            out int bits,
            out string exType)
        {
            try
            {
                float value = AERIS39MapSoPureCpuExact.GetPixelFloat(snapshot, u, v);
                has = true;
                bits = AERIS39MapSoPureCpuExact.FloatBits(value);
                exType = string.Empty;
            }
            catch (Exception ex)
            {
                has = false;
                bits = 0;
                exType = TypeName(ex.GetType());
            }
        }

        static void EvalTopology(
            AERIS39MapSoPureCpuTopologyProbe topology,
            double u,
            double v,
            out bool has,
            out int bits,
            out string exType)
        {
            try
            {
                float value = topology.GetPixelFloat(u, v);
                has = true;
                bits = AERIS39MapSoPureCpuExact.FloatBits(value);
                exType = string.Empty;
            }
            catch (Exception ex)
            {
                has = false;
                bits = 0;
                exType = TypeName(ex.GetType());
            }
        }

        static bool Same(bool ah, int ab, string ae, bool bh, int bb, string be)
        {
            if (ah != bh) return false;
            if (ah) return ab == bb;
            return string.Equals(ae ?? string.Empty, be ?? string.Empty, StringComparison.Ordinal);
        }

        static string Outcome(bool has, int bits, string ex)
        {
            return has
                ? "0x" + unchecked((uint)bits).ToString("X8", CultureInfo.InvariantCulture)
                : "EX:" + Safe(ex);
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
                    double u = longitudes[o] / 360.0 + 0.5;
                    double v = latitudes[a] / 180.0 + 0.5;
                    AddSample(result, seen,
                        "BODY_COORD lat=" + R(latitudes[a]) + " lon=" + R(longitudes[o]),
                        u, v);
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

        static void AddSample(
            List<Sample> result,
            HashSet<string> seen,
            string label,
            double u,
            double v)
        {
            string key = BitConverter.DoubleToInt64Bits(u).ToString("X16", CultureInfo.InvariantCulture) +
                ":" + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
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
            if (FlightGlobals.Bodies == null) return null;
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
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            for (Type cursor = type; cursor != null; cursor = cursor.BaseType)
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
            if (raw == null) throw new MissingMemberException(target.GetType().FullName, name);
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

        static string TypeName(Type type)
        {
            return type == null ? "<null>" : (type.FullName ?? type.Name ?? "<unnamed>");
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
