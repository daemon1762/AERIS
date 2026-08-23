using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR034PqsLandControlHeightPathAuditObserver : MonoBehaviour
    {
        internal const string CandidateMarker =
            "AERIS32_REV3_5_R034_PQS_LANDCONTROL_HEIGHT_PATH_AUDIT_SHADOW";

        static readonly string[] TargetBodies = new string[] { "Minmus", "Ike", "Gilly", "Pol" };
        const int MaxMethods = 128;
        const int MaxInstructions = 8192;

        static readonly Dictionary<short, OpCode> OneByte = BuildOpcodeMap(false);
        static readonly Dictionary<short, OpCode> TwoByte = BuildOpcodeMap(true);

        int mainThreadId;
        bool completed;

        sealed class IlAudit
        {
            internal int Methods;
            internal int Instructions;
            internal int DirectVertHeightWrites;
            internal int HeightNamedFieldWrites;
            internal int Failures;
            internal readonly HashSet<MethodBase> Seen = new HashSet<MethodBase>();
        }

        void Start()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            AERISLogger.Info("[R034][LANDCTRL] event=BOOT; candidate=" + CandidateMarker +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_dispatch=false; " +
                "worker_invokes_runtime_object=false; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        void Update()
        {
            if (completed) return;
            if (!AERISTerrainTileSystem.GameDataHashReady) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
            completed = true;
            RunAudit();
        }

        void RunAudit()
        {
            int bodiesFound = 0;
            int landControls = 0;
            int failures = 0;
            int totalMethods = 0;
            int totalInstructions = 0;
            int totalDirectWrites = 0;
            int totalHeightWrites = 0;

            AERISLogger.Info("[R034][LANDCTRL] event=AUDIT_START; bodies=4; main_thread_id=" +
                mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_dispatch=false; " +
                "worker_invokes_runtime_object=false; db_write=false; producer_switch=false; gpu=false; authority=PQS");

            for (int i = 0; i < TargetBodies.Length; ++i)
            {
                string bodyName = TargetBodies[i];
                try
                {
                    CelestialBody body = FindBody(bodyName);
                    if (body == null || body.pqsController == null)
                    {
                        failures++;
                        AERISLogger.Error("[R034][LANDCTRL_FAIL] stage=BODY; body=" + bodyName +
                            "; error=BODY_OR_PQS_MISSING; authority=PQS");
                        continue;
                    }
                    bodiesFound++;

                    PQSLandControl land = FindLandControl(body);
                    if (land == null)
                    {
                        failures++;
                        AERISLogger.Error("[R034][LANDCTRL_FAIL] stage=LANDCONTROL; body=" + bodyName +
                            "; error=LANDCONTROL_MISSING; authority=PQS");
                        continue;
                    }
                    landControls++;

                    bool useHeightMap = ReadBool(land, "useHeightMap", false);
                    object heightMap = ReadMember(land, "heightMap");
                    double vHeightMax = ReadDouble(land, "vHeightMax", double.NaN);
                    Array classes = ReadMember(land, "landClasses") as Array;
                    int classCount = classes == null ? -1 : classes.Length;

                    AERISLogger.Info("[R034][LANDCTRL_BODY] body=" + bodyName +
                        "; useHeightMap=" + useHeightMap.ToString() +
                        "; heightMap_present=" + (heightMap != null).ToString() +
                        "; heightMap_type=" + TypeName(heightMap) +
                        "; vHeightMax=" + F(vHeightMax) +
                        "; landClasses=" + classCount.ToString(CultureInfo.InvariantCulture) +
                        "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; authority=PQS");

                    if (classes != null)
                    {
                        for (int ci = 0; ci < classes.Length; ++ci)
                        {
                            object lc = classes.GetValue(ci);
                            if (lc == null)
                            {
                                AERISLogger.Info("[R034][LANDCTRL_CLASS] body=" + bodyName +
                                    "; index=" + ci.ToString(CultureInfo.InvariantCulture) +
                                    "; null=true; minimumRealHeight=NaN; alterRealHeight=NaN; alterApparentHeight=NaN; authority=PQS");
                                continue;
                            }

                            string lcName = ReadString(lc, "landClassName");
                            double minReal = ReadDouble(lc, "minimumRealHeight", double.NaN);
                            double alterReal = ReadDouble(lc, "alterRealHeight", double.NaN);
                            double alterApparent = ReadDouble(lc, "alterApparentHeight", double.NaN);

                            AERISLogger.Info("[R034][LANDCTRL_CLASS] body=" + bodyName +
                                "; index=" + ci.ToString(CultureInfo.InvariantCulture) +
                                "; name=" + Safe(lcName) +
                                "; minimumRealHeight=" + F(minReal) +
                                "; alterRealHeight=" + F(alterReal) +
                                "; alterApparentHeight=" + F(alterApparent) +
                                "; authority=PQS");

                            LogRange(bodyName, ci, "altitudeRange", ReadMember(lc, "altitudeRange"));
                            LogRange(bodyName, ci, "latitudeRange", ReadMember(lc, "latitudeRange"));
                            LogRange(bodyName, ci, "longitudeRange", ReadMember(lc, "longitudeRange"));
                        }
                    }

                    IlAudit audit = new IlAudit();
                    MethodInfo entry = typeof(PQSLandControl).GetMethod(
                        "OnVertexBuildHeight",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                    if (entry == null)
                    {
                        audit.Failures++;
                        AERISLogger.Error("[R034][LANDCTRL_FAIL] stage=IL_ENTRY; body=" + bodyName +
                            "; error=OnVertexBuildHeight_MISSING; authority=PQS");
                    }
                    else
                    {
                        WalkMethod(entry, audit);
                    }

                    totalMethods += audit.Methods;
                    totalInstructions += audit.Instructions;
                    totalDirectWrites += audit.DirectVertHeightWrites;
                    totalHeightWrites += audit.HeightNamedFieldWrites;
                    failures += audit.Failures;

                    AERISLogger.Info("[R034][LANDCTRL_IL_SUMMARY] body=" + bodyName +
                        "; methods=" + audit.Methods.ToString(CultureInfo.InvariantCulture) +
                        "; instructions=" + audit.Instructions.ToString(CultureInfo.InvariantCulture) +
                        "; direct_vertHeight_writes=" + audit.DirectVertHeightWrites.ToString(CultureInfo.InvariantCulture) +
                        "; height_named_field_writes=" + audit.HeightNamedFieldWrites.ToString(CultureInfo.InvariantCulture) +
                        "; failures=" + audit.Failures.ToString(CultureInfo.InvariantCulture) +
                        "; authority=PQS");
                }
                catch (Exception ex)
                {
                    failures++;
                    AERISLogger.Error("[R034][LANDCTRL_FAIL] stage=BODY_AUDIT; body=" + bodyName +
                        "; error=" + Safe(ex.GetType().Name + ":" + ex.Message) +
                        "; authority=PQS");
                }
            }

            AERISLogger.Info("[R034][LANDCTRL] event=HEIGHT_PATH_AUDIT_COMPLETE; bodies=4; bodies_found=" +
                bodiesFound.ToString(CultureInfo.InvariantCulture) +
                "; land_controls=" + landControls.ToString(CultureInfo.InvariantCulture) +
                "; methods=" + totalMethods.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" + totalInstructions.ToString(CultureInfo.InvariantCulture) +
                "; direct_vertHeight_writes=" + totalDirectWrites.ToString(CultureInfo.InvariantCulture) +
                "; height_named_field_writes=" + totalHeightWrites.ToString(CultureInfo.InvariantCulture) +
                "; failures=" + failures.ToString(CultureInfo.InvariantCulture) +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_dispatch=false; " +
                "worker_invokes_runtime_object=false; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; ++i)
            {
                CelestialBody b = FlightGlobals.Bodies[i];
                if (b != null && string.Equals(b.bodyName, name, StringComparison.Ordinal))
                    return b;
            }
            return null;
        }

        static PQSLandControl FindLandControl(CelestialBody body)
        {
            PQSMod[] mods = body.pqsController.GetComponentsInChildren<PQSMod>(true);
            if (mods == null) return null;
            for (int i = 0; i < mods.Length; ++i)
            {
                PQSLandControl lc = mods[i] as PQSLandControl;
                if (lc != null && lc.enabled) return lc;
            }
            return null;
        }

        static void LogRange(string body, int classIndex, string slot, object range)
        {
            if (range == null)
            {
                AERISLogger.Info("[R034][LANDCTRL_RANGE] body=" + body +
                    "; class_index=" + classIndex.ToString(CultureInfo.InvariantCulture) +
                    "; slot=" + slot + "; present=false; startStart=NaN; startEnd=NaN; endStart=NaN; endEnd=NaN; authority=PQS");
                return;
            }
            AERISLogger.Info("[R034][LANDCTRL_RANGE] body=" + body +
                "; class_index=" + classIndex.ToString(CultureInfo.InvariantCulture) +
                "; slot=" + slot +
                "; present=true; startStart=" + F(ReadDouble(range, "startStart", double.NaN)) +
                "; startEnd=" + F(ReadDouble(range, "startEnd", double.NaN)) +
                "; endStart=" + F(ReadDouble(range, "endStart", double.NaN)) +
                "; endEnd=" + F(ReadDouble(range, "endEnd", double.NaN)) +
                "; authority=PQS");
        }

        static object ReadMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            while (t != null)
            {
                FieldInfo f = t.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(obj);
                PropertyInfo p = t.GetProperty(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (p != null && p.GetIndexParameters().Length == 0) return p.GetValue(obj, null);
                t = t.BaseType;
            }
            return null;
        }

        static bool ReadBool(object obj, string name, bool fallback)
        {
            object v = ReadMember(obj, name);
            return v is bool ? (bool)v : fallback;
        }

        static double ReadDouble(object obj, string name, double fallback)
        {
            object v = ReadMember(obj, name);
            if (v == null) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        static string ReadString(object obj, string name)
        {
            object v = ReadMember(obj, name);
            return v == null ? string.Empty : v.ToString();
        }

        static string TypeName(object obj)
        {
            return obj == null ? "null" : Safe(obj.GetType().FullName);
        }

        static string F(double v)
        {
            return double.IsNaN(v) ? "NaN" : v.ToString("R", CultureInfo.InvariantCulture);
        }

        static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return "-";
            return text.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        }

        static Dictionary<short, OpCode> BuildOpcodeMap(bool twoByte)
        {
            Dictionary<short, OpCode> map = new Dictionary<short, OpCode>();
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; ++i)
            {
                object value = fields[i].GetValue(null);
                if (!(value is OpCode)) continue;
                OpCode op = (OpCode)value;
                bool isTwo = (op.Value & 0xff00) == 0xfe00;
                if (isTwo == twoByte) map[op.Value] = op;
            }
            return map;
        }

        static void WalkMethod(MethodBase method, IlAudit audit)
        {
            if (method == null || audit.Seen.Contains(method)) return;
            if (audit.Seen.Count >= MaxMethods)
            {
                audit.Failures++;
                return;
            }
            audit.Seen.Add(method);
            audit.Methods++;

            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch { audit.Failures++; return; }
            if (body == null) return;

            byte[] il = body.GetILAsByteArray();
            if (il == null) return;
            int pos = 0;
            Module module = method.Module;

            while (pos < il.Length)
            {
                if (audit.Instructions >= MaxInstructions)
                {
                    audit.Failures++;
                    return;
                }

                int offset = pos;
                OpCode op;
                byte first = il[pos++];
                if (first == 0xfe)
                {
                    if (pos >= il.Length) { audit.Failures++; return; }
                    short key = unchecked((short)(0xfe00 | il[pos++]));
                    if (!TwoByte.TryGetValue(key, out op)) { audit.Failures++; return; }
                }
                else
                {
                    short key = (short)first;
                    if (!OneByte.TryGetValue(key, out op)) { audit.Failures++; return; }
                }

                audit.Instructions++;
                int operandStart = pos;
                int size = OperandSize(op.OperandType, il, pos);
                if (size < 0 || pos + size > il.Length)
                {
                    audit.Failures++;
                    return;
                }

                if ((op == OpCodes.Stfld || op == OpCodes.Stsfld) && size == 4)
                {
                    int token = BitConverter.ToInt32(il, operandStart);
                    try
                    {
                        FieldInfo field = module.ResolveField(token,
                            method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        if (field != null)
                        {
                            if (string.Equals(field.Name, "vertHeight", StringComparison.Ordinal))
                                audit.DirectVertHeightWrites++;
                            if (field.Name.IndexOf("height", StringComparison.OrdinalIgnoreCase) >= 0)
                                audit.HeightNamedFieldWrites++;
                        }
                    }
                    catch
                    {
                        audit.Failures++;
                    }
                }

                if ((op == OpCodes.Call || op == OpCodes.Callvirt) && size == 4)
                {
                    int token = BitConverter.ToInt32(il, operandStart);
                    try
                    {
                        MethodBase called = module.ResolveMethod(token,
                            method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        if (called != null && IsLandControlClosure(called.DeclaringType))
                            WalkMethod(called, audit);
                    }
                    catch
                    {
                        audit.Failures++;
                    }
                }

                pos += size;
                if (pos <= offset)
                {
                    audit.Failures++;
                    return;
                }
            }
        }

        static bool IsLandControlClosure(Type t)
        {
            if (t == null) return false;
            if (t == typeof(PQSLandControl)) return true;
            Type d = t.DeclaringType;
            while (d != null)
            {
                if (d == typeof(PQSLandControl)) return true;
                d = d.DeclaringType;
            }
            return false;
        }

        static int OperandSize(OperandType type, byte[] il, int pos)
        {
            switch (type)
            {
                case OperandType.InlineNone: return 0;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: return 1;
                case OperandType.InlineVar: return 2;
                case OperandType.InlineBrTarget:
                case OperandType.InlineField:
                case OperandType.InlineI:
                case OperandType.InlineMethod:
                case OperandType.InlineSig:
                case OperandType.InlineString:
                case OperandType.InlineTok:
                case OperandType.InlineType:
                case OperandType.ShortInlineR: return 4;
                case OperandType.InlineI8:
                case OperandType.InlineR: return 8;
                case OperandType.InlineSwitch:
                    if (pos + 4 > il.Length) return -1;
                    int count = BitConverter.ToInt32(il, pos);
                    if (count < 0 || count > 4096) return -1;
                    return 4 + count * 4;
                default: return -1;
            }
        }
    }
}
