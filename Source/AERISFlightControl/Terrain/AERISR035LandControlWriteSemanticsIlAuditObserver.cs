using System;
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
    internal sealed class AERISR035LandControlWriteSemanticsIlAuditObserver : MonoBehaviour
    {
        internal const string CandidateMarker =
            "AERIS32_REV3_5_R035_LANDCONTROL_WRITE_SEMANTICS_IL_AUDIT_SHADOW";

        static readonly string[] TargetBodies = new string[] { "Minmus", "Ike", "Gilly", "Pol" };
        static readonly Dictionary<short, OpCode> OneByte = BuildOpcodeMap(false);
        static readonly Dictionary<short, OpCode> TwoByte = BuildOpcodeMap(true);
        const int MaxInstructions = 4096;

        int mainThreadId;
        bool completed;

        sealed class DecodedInstruction
        {
            internal int Offset;
            internal OpCode Op;
            internal string Operand;
            internal bool VertHeightWrite;
        }

        void Start()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            AERISLogger.Info("[R035][LANDCTRL_IL] event=BOOT; candidate=" + CandidateMarker +
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
            int failures = 0;
            int bodiesFound = 0;
            int landControls = 0;

            AERISLogger.Info("[R035][LANDCTRL_IL] event=AUDIT_START; bodies=4; " +
                "runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_dispatch=false; " +
                "worker_invokes_runtime_object=false; db_write=false; producer_switch=false; gpu=false; authority=PQS");

            for (int i = 0; i < TargetBodies.Length; ++i)
            {
                string name = TargetBodies[i];
                try
                {
                    CelestialBody body = FindBody(name);
                    if (body == null || body.pqsController == null)
                    {
                        failures++;
                        AERISLogger.Error("[R035][LANDCTRL_IL_FAIL] stage=BODY; body=" + name +
                            "; error=BODY_OR_PQS_MISSING; authority=PQS");
                        continue;
                    }
                    bodiesFound++;
                    PQSLandControl land = FindLandControl(body);
                    if (land == null)
                    {
                        failures++;
                        AERISLogger.Error("[R035][LANDCTRL_IL_FAIL] stage=LANDCONTROL; body=" + name +
                            "; error=LANDCONTROL_MISSING; authority=PQS");
                        continue;
                    }
                    landControls++;

                    bool useHeightMap = ReadBool(land, "useHeightMap", false);
                    Array classes = ReadMember(land, "landClasses") as Array;
                    int classCount = classes == null ? -1 : classes.Length;
                    int nonzeroAlterReal = 0;
                    int nonzeroAlterApparent = 0;
                    int nonzeroMinimumReal = 0;
                    if (classes != null)
                    {
                        for (int ci = 0; ci < classes.Length; ++ci)
                        {
                            object lc = classes.GetValue(ci);
                            if (lc == null) continue;
                            double ar = ReadDouble(lc, "alterRealHeight", double.NaN);
                            double aa = ReadDouble(lc, "alterApparentHeight", double.NaN);
                            double mr = ReadDouble(lc, "minimumRealHeight", double.NaN);
                            if (!double.IsNaN(ar) && ar != 0.0) nonzeroAlterReal++;
                            if (!double.IsNaN(aa) && aa != 0.0) nonzeroAlterApparent++;
                            if (!double.IsNaN(mr) && mr != 0.0) nonzeroMinimumReal++;
                        }
                    }

                    AERISLogger.Info("[R035][LANDCTRL_CONFIG] body=" + name +
                        "; useHeightMap=" + useHeightMap.ToString() +
                        "; landClasses=" + classCount.ToString(CultureInfo.InvariantCulture) +
                        "; nonzero_alterRealHeight=" + nonzeroAlterReal.ToString(CultureInfo.InvariantCulture) +
                        "; nonzero_alterApparentHeight=" + nonzeroAlterApparent.ToString(CultureInfo.InvariantCulture) +
                        "; nonzero_minimumRealHeight=" + nonzeroMinimumReal.ToString(CultureInfo.InvariantCulture) +
                        "; authority=PQS");
                }
                catch (Exception ex)
                {
                    failures++;
                    AERISLogger.Error("[R035][LANDCTRL_IL_FAIL] stage=CONFIG; body=" + name +
                        "; error=" + Safe(ex.GetType().Name + ":" + ex.Message) + "; authority=PQS");
                }
            }

            MethodInfo entry = typeof(PQSLandControl).GetMethod(
                "OnVertexBuildHeight",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            List<DecodedInstruction> decoded = new List<DecodedInstruction>();
            int directWrites = 0;
            int methodFailures = 0;
            int maxStack = -1;
            int localCount = -1;
            bool initLocals = false;
            int codeSize = 0;

            if (entry == null)
            {
                methodFailures++;
                AERISLogger.Error("[R035][LANDCTRL_IL_FAIL] stage=ENTRY; error=OnVertexBuildHeight_MISSING; authority=PQS");
            }
            else
            {
                try
                {
                    MethodBody body = entry.GetMethodBody();
                    if (body == null)
                    {
                        methodFailures++;
                    }
                    else
                    {
                        maxStack = body.MaxStackSize;
                        localCount = body.LocalVariables == null ? 0 : body.LocalVariables.Count;
                        initLocals = body.InitLocals;
                        byte[] il = body.GetILAsByteArray();
                        codeSize = il == null ? 0 : il.Length;
                        if (il == null)
                        {
                            methodFailures++;
                        }
                        else
                        {
                            DecodeMethod(entry, il, decoded, ref directWrites, ref methodFailures);
                        }
                    }
                }
                catch (Exception ex)
                {
                    methodFailures++;
                    AERISLogger.Error("[R035][LANDCTRL_IL_FAIL] stage=DECODE; error=" +
                        Safe(ex.GetType().Name + ":" + ex.Message) + "; authority=PQS");
                }
            }

            AERISLogger.Info("[R035][LANDCTRL_METHOD] declaring_type=PQSLandControl; method=OnVertexBuildHeight" +
                "; metadata_token=" + (entry == null ? "-1" : entry.MetadataToken.ToString(CultureInfo.InvariantCulture)) +
                "; code_size=" + codeSize.ToString(CultureInfo.InvariantCulture) +
                "; max_stack=" + maxStack.ToString(CultureInfo.InvariantCulture) +
                "; locals=" + localCount.ToString(CultureInfo.InvariantCulture) +
                "; init_locals=" + initLocals.ToString() +
                "; instructions=" + decoded.Count.ToString(CultureInfo.InvariantCulture) +
                "; direct_vertHeight_writes=" + directWrites.ToString(CultureInfo.InvariantCulture) +
                "; failures=" + methodFailures.ToString(CultureInfo.InvariantCulture) +
                "; authority=PQS");

            for (int i = 0; i < decoded.Count; ++i)
            {
                DecodedInstruction d = decoded[i];
                AERISLogger.Info("[R035][LANDCTRL_IL_INSN] index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; offset=" + d.Offset.ToString(CultureInfo.InvariantCulture) +
                    "; opcode=" + Safe(d.Op.Name) +
                    "; operand=" + Safe(d.Operand) +
                    "; vertHeight_write=" + d.VertHeightWrite.ToString() +
                    "; authority=PQS");
            }

            failures += methodFailures;
            AERISLogger.Info("[R035][LANDCTRL_IL] event=WRITE_SEMANTICS_IL_AUDIT_COMPLETE; bodies=4; bodies_found=" +
                bodiesFound.ToString(CultureInfo.InvariantCulture) +
                "; land_controls=" + landControls.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" + decoded.Count.ToString(CultureInfo.InvariantCulture) +
                "; direct_vertHeight_writes=" + directWrites.ToString(CultureInfo.InvariantCulture) +
                "; failures=" + failures.ToString(CultureInfo.InvariantCulture) +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_dispatch=false; " +
                "worker_invokes_runtime_object=false; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static void DecodeMethod(MethodBase method, byte[] il, List<DecodedInstruction> output,
            ref int directWrites, ref int failures)
        {
            Module module = method.Module;
            int pos = 0;
            while (pos < il.Length)
            {
                if (output.Count >= MaxInstructions)
                {
                    failures++;
                    return;
                }

                int offset = pos;
                OpCode op;
                byte first = il[pos++];
                if (first == 0xfe)
                {
                    if (pos >= il.Length) { failures++; return; }
                    short key = unchecked((short)(0xfe00 | il[pos++]));
                    if (!TwoByte.TryGetValue(key, out op)) { failures++; return; }
                }
                else
                {
                    short key = (short)first;
                    if (!OneByte.TryGetValue(key, out op)) { failures++; return; }
                }

                int operandStart = pos;
                int size = OperandSize(op.OperandType, il, pos);
                if (size < 0 || pos + size > il.Length)
                {
                    failures++;
                    return;
                }

                bool write = false;
                string operand = DecodeOperand(method, module, op, il, operandStart, size, ref write, ref failures);
                if (write) directWrites++;

                DecodedInstruction d = new DecodedInstruction();
                d.Offset = offset;
                d.Op = op;
                d.Operand = operand;
                d.VertHeightWrite = write;
                output.Add(d);
                pos += size;
            }
        }

        static string DecodeOperand(MethodBase method, Module module, OpCode op, byte[] il,
            int start, int size, ref bool vertHeightWrite, ref int failures)
        {
            try
            {
                switch (op.OperandType)
                {
                    case OperandType.InlineNone:
                        return "-";
                    case OperandType.ShortInlineI:
                        return unchecked((sbyte)il[start]).ToString(CultureInfo.InvariantCulture);
                    case OperandType.InlineI:
                        return BitConverter.ToInt32(il, start).ToString(CultureInfo.InvariantCulture);
                    case OperandType.InlineI8:
                        return BitConverter.ToInt64(il, start).ToString(CultureInfo.InvariantCulture);
                    case OperandType.ShortInlineR:
                        return BitConverter.ToSingle(il, start).ToString("R", CultureInfo.InvariantCulture);
                    case OperandType.InlineR:
                        return BitConverter.ToDouble(il, start).ToString("R", CultureInfo.InvariantCulture);
                    case OperandType.ShortInlineVar:
                        return "var:" + il[start].ToString(CultureInfo.InvariantCulture);
                    case OperandType.InlineVar:
                        return "var:" + BitConverter.ToUInt16(il, start).ToString(CultureInfo.InvariantCulture);
                    case OperandType.ShortInlineBrTarget:
                        return "target:" + (start + size + unchecked((sbyte)il[start])).ToString(CultureInfo.InvariantCulture);
                    case OperandType.InlineBrTarget:
                        return "target:" + (start + size + BitConverter.ToInt32(il, start)).ToString(CultureInfo.InvariantCulture);
                    case OperandType.InlineSwitch:
                    {
                        int count = BitConverter.ToInt32(il, start);
                        int baseOffset = start + 4 + count * 4;
                        System.Text.StringBuilder sb = new System.Text.StringBuilder();
                        sb.Append("targets:");
                        for (int i = 0; i < count; ++i)
                        {
                            if (i != 0) sb.Append(',');
                            sb.Append((baseOffset + BitConverter.ToInt32(il, start + 4 + i * 4))
                                .ToString(CultureInfo.InvariantCulture));
                        }
                        return sb.ToString();
                    }
                    case OperandType.InlineString:
                    {
                        int token = BitConverter.ToInt32(il, start);
                        return "string:" + module.ResolveString(token);
                    }
                    case OperandType.InlineField:
                    {
                        int token = BitConverter.ToInt32(il, start);
                        FieldInfo f = module.ResolveField(token,
                            method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        if (f == null) return "field:null";
                        string full = "field:" + TypeLabel(f.DeclaringType) + "." + f.Name + ":" + TypeLabel(f.FieldType);
                        if ((op == OpCodes.Stfld || op == OpCodes.Stsfld) &&
                            string.Equals(f.Name, "vertHeight", StringComparison.Ordinal))
                            vertHeightWrite = true;
                        return full;
                    }
                    case OperandType.InlineMethod:
                    {
                        int token = BitConverter.ToInt32(il, start);
                        MethodBase m = module.ResolveMethod(token,
                            method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        return "method:" + MethodLabel(m);
                    }
                    case OperandType.InlineType:
                    {
                        int token = BitConverter.ToInt32(il, start);
                        Type t = module.ResolveType(token,
                            method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        return "type:" + TypeLabel(t);
                    }
                    case OperandType.InlineTok:
                    {
                        int token = BitConverter.ToInt32(il, start);
                        MemberInfo m = module.ResolveMember(token,
                            method.DeclaringType == null ? null : method.DeclaringType.GetGenericArguments(),
                            method.IsGenericMethod ? method.GetGenericArguments() : null);
                        return "member:" + (m == null ? "null" : TypeLabel(m.DeclaringType) + "." + m.Name);
                    }
                    case OperandType.InlineSig:
                        return "sig_token:" + BitConverter.ToInt32(il, start).ToString(CultureInfo.InvariantCulture);
                    default:
                        return "raw_size:" + size.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex)
            {
                failures++;
                return "resolve_error:" + ex.GetType().Name;
            }
        }

        static string MethodLabel(MethodBase m)
        {
            if (m == null) return "null";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(TypeLabel(m.DeclaringType)).Append('.').Append(m.Name).Append('(');
            ParameterInfo[] ps = m.GetParameters();
            for (int i = 0; i < ps.Length; ++i)
            {
                if (i != 0) sb.Append(',');
                sb.Append(TypeLabel(ps[i].ParameterType));
            }
            sb.Append(')');
            MethodInfo mi = m as MethodInfo;
            if (mi != null) sb.Append(':').Append(TypeLabel(mi.ReturnType));
            return sb.ToString();
        }

        static string TypeLabel(Type t)
        {
            return t == null ? "null" : (t.FullName ?? t.Name);
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

        static string Safe(string text)
        {
            if (string.IsNullOrEmpty(text)) return "-";
            return text.Replace(';', ',').Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
