using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS38 R041C: targeted exact IL closure capture for the first
    // all-stock pure-formula candidates (Moho + Dres).
    //
    // Safety contract:
    // - Main-thread observer only.
    // - Reflection metadata / MethodBody inspection only.
    // - NEVER invokes PQS modifier callbacks.
    // - NEVER changes PQS, preload, DB, producer routing, AP/AA/PROTECT/LAND.
    // - NEVER passes Unity/KSP runtime objects to workers.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR041CMohoDresHeightIlClosureObserver : MonoBehaviour
    {
        const string Candidate = "AERIS38_R041C_MOHO_DRES_HEIGHT_IL_CLOSURE";
        const double ExactToleranceMeters = 1E-08;
        static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();
        static readonly string[] TargetBodies = { "Moho", "Dres" };
        static readonly string[] TargetMethods =
        {
            "OnVertexBuildHeight",
            "OnSetup",
            "GetVertexMinHeight",
            "GetVertexMaxHeight"
        };

        sealed class Insn
        {
            internal int Index;
            internal int Offset;
            internal int EndOffset;
            internal OpCode Op;
            internal string Operand;
        }

        bool emitted;
        float nextAttempt;
        int mainThreadId;

        void Awake()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (emitted) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            emitted = true;
            try { Emit(); }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    "[R041C][IL_CLOSURE_FAIL]" +
                    "; candidate=" + Candidate +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    "; terrain_tolerance_m=1E-08" +
                    "; invokes_pqs_callbacks=false" +
                    "; production_authority=PQS" +
                    "; db_authority=PQS" +
                    "; producer_switch=false" +
                    "; db_write=false" +
                    "; preload_mutation=false" +
                    "; worker_runtime_object_access=false");
            }
        }

        void Emit()
        {
            AERISLogger.Info(
                "[R041C][IL_CLOSURE_BEGIN]" +
                "; candidate=" + Candidate +
                "; main_thread_id=" + mainThreadId +
                "; bodies=Moho,Dres" +
                "; invokes_pqs_callbacks=false" +
                "; terrain_tolerance_m=" + R(ExactToleranceMeters) +
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; worker_runtime_object_access=false");

            var seenTypes = new HashSet<string>(StringComparer.Ordinal);
            int bodyCount = 0;
            int typeCount = 0;
            int methodCount = 0;
            int instructionCount = 0;

            for (int b = 0; b < TargetBodies.Length; b++)
            {
                CelestialBody body = FindBody(TargetBodies[b]);
                if (body == null || body.pqsController == null)
                    throw new InvalidOperationException(TargetBodies[b] + " body/PQS missing");

                bodyCount++;
                IList mods = GetModifierList(body.pqsController);
                if (mods == null)
                    throw new InvalidOperationException(TargetBodies[b] + " modifier list missing");

                for (int i = 0; i < mods.Count; i++)
                {
                    object mod = mods[i];
                    if (mod == null) continue;
                    if (!IsEnabled(mod)) continue;

                    Type type = mod.GetType();
                    MethodInfo height = FindDeclaredMethod(type, "OnVertexBuildHeight");
                    if (height == null) continue;

                    string typeName = type.FullName ?? type.Name;
                    if (!seenTypes.Add(typeName)) continue;
                    typeCount++;

                    AERISLogger.Info(
                        "[R041C][TYPE]" +
                        "; source_body=" + Safe(TargetBodies[b]) +
                        "; source_index=" + i.ToString(CultureInfo.InvariantCulture) +
                        "; type=" + Safe(typeName) +
                        "; assembly=" + Safe(type.Assembly.GetName().Name) +
                        "; invokes_pqs_callbacks=false" +
                        "; authority=IL_METADATA");

                    EmitScalarFields(mod, type);

                    for (int m = 0; m < TargetMethods.Length; m++)
                    {
                        MethodInfo method = FindDeclaredMethod(type, TargetMethods[m]);
                        if (method == null) continue;
                        int emittedInstructions = EmitMethod(type, method);
                        methodCount++;
                        instructionCount += emittedInstructions;
                    }
                }
            }

            AERISLogger.Info(
                "[R041C][IL_CLOSURE_COMPLETE]" +
                "; pass=true" +
                "; candidate=" + Candidate +
                "; bodies=" + bodyCount.ToString(CultureInfo.InvariantCulture) +
                "; unique_height_types=" + typeCount.ToString(CultureInfo.InvariantCulture) +
                "; methods=" + methodCount.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" + instructionCount.ToString(CultureInfo.InvariantCulture) +
                "; invokes_pqs_callbacks=false" +
                "; terrain_tolerance_m=1E-08" +
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; worker_runtime_object_access=false");
        }

        static int EmitMethod(Type type, MethodInfo method)
        {
            MethodBody body = method.GetMethodBody();
            if (body == null) return 0;
            byte[] il = body.GetILAsByteArray();
            if (il == null) return 0;

            List<Insn> insns = Decode(method, il);
            string sha = Sha256(il);

            AERISLogger.Info(
                "[R041C][METHOD]" +
                "; type=" + Safe(type.FullName ?? type.Name) +
                "; method=" + Safe(Signature(method)) +
                "; metadata_token=0x" + method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture) +
                "; il_bytes=" + il.Length.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" + insns.Count.ToString(CultureInfo.InvariantCulture) +
                "; max_stack=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture) +
                "; locals=" + body.LocalVariables.Count.ToString(CultureInfo.InvariantCulture) +
                "; il_sha256=" + sha +
                "; authority=IL_METADATA");

            for (int i = 0; i < body.LocalVariables.Count; i++)
            {
                LocalVariableInfo local = body.LocalVariables[i];
                AERISLogger.Info(
                    "[R041C][LOCAL]" +
                    "; type=" + Safe(type.FullName ?? type.Name) +
                    "; method=" + Safe(method.Name) +
                    "; index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; local_type=" + Safe(local.LocalType == null ? "NULL" : (local.LocalType.FullName ?? local.LocalType.Name)) +
                    "; pinned=" + local.IsPinned +
                    "; authority=IL_METADATA");
            }

            for (int i = 0; i < insns.Count; i++)
            {
                Insn x = insns[i];
                AERISLogger.Info(
                    "[R041C][INSN]" +
                    "; type=" + Safe(type.FullName ?? type.Name) +
                    "; method=" + Safe(method.Name) +
                    "; index=" + x.Index.ToString(CultureInfo.InvariantCulture) +
                    "; offset=IL_" + x.Offset.ToString("X4", CultureInfo.InvariantCulture) +
                    "; end=IL_" + x.EndOffset.ToString("X4", CultureInfo.InvariantCulture) +
                    "; opcode=" + Safe(x.Op.Name) +
                    "; operand=" + Safe(x.Operand) +
                    "; flow=" + Safe(x.Op.FlowControl.ToString()) +
                    "; authority=IL_METADATA");
            }

            return insns.Count;
        }

        static void EmitScalarFields(object instance, Type type)
        {
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b) { return string.CompareOrdinal(a.Name, b.Name); });
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                object value = null;
                try { value = field.GetValue(instance); }
                catch { continue; }

                string serialized;
                if (!TrySerializeScalar(value, field.FieldType, out serialized))
                    serialized = "OBJECT:" + Safe(value == null ? "null" : (value.GetType().FullName ?? value.GetType().Name));

                AERISLogger.Info(
                    "[R041C][FIELD]" +
                    "; type=" + Safe(type.FullName ?? type.Name) +
                    "; name=" + Safe(field.Name) +
                    "; field_type=" + Safe(field.FieldType.FullName ?? field.FieldType.Name) +
                    "; value=" + Safe(serialized) +
                    "; authority=MAIN_THREAD_READ_ONLY_SNAPSHOT_DISCOVERY");
            }
        }

        static bool TrySerializeScalar(object value, Type type, out string serialized)
        {
            serialized = string.Empty;
            if (value == null) { serialized = "null"; return true; }
            if (type == typeof(string)) { serialized = (string)value; return true; }
            if (type == typeof(bool)) { serialized = ((bool)value) ? "true" : "false"; return true; }
            if (type.IsEnum) { serialized = value.ToString(); return true; }
            if (type == typeof(double)) { serialized = ((double)value).ToString("R", CultureInfo.InvariantCulture); return true; }
            if (type == typeof(float)) { serialized = ((float)value).ToString("R", CultureInfo.InvariantCulture); return true; }
            if (type == typeof(int)) { serialized = ((int)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(uint)) { serialized = ((uint)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(long)) { serialized = ((long)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(ulong)) { serialized = ((ulong)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(short)) { serialized = ((short)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(ushort)) { serialized = ((ushort)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(byte)) { serialized = ((byte)value).ToString(CultureInfo.InvariantCulture); return true; }
            if (type == typeof(sbyte)) { serialized = ((sbyte)value).ToString(CultureInfo.InvariantCulture); return true; }
            return false;
        }

        static MethodInfo FindDeclaredMethod(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i] != null && string.Equals(methods[i].Name, name, StringComparison.Ordinal))
                    return methods[i];
            }
            return null;
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
                if (body != null && (string.Equals(body.bodyName, name, StringComparison.Ordinal) || string.Equals(body.name, name, StringComparison.Ordinal)))
                    return body;
            }
            return null;
        }

        static IList GetModifierList(object pqs)
        {
            object raw = ReadMember(pqs, "mods") ?? ReadMember(pqs, "modifiers") ?? ReadMember(pqs, "pqsMods");
            return raw as IList;
        }

        static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(name, flags);
            if (field != null) { try { return field.GetValue(target); } catch { } }
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
            {
                try { return property.GetValue(target, null); } catch { }
            }
            return null;
        }

        static List<Insn> Decode(MethodInfo method, byte[] il)
        {
            var result = new List<Insn>();
            int p = 0;
            int index = 0;
            while (p < il.Length)
            {
                int offset = p;
                ushort key = il[p++];
                if (key == 0xFE)
                {
                    if (p >= il.Length) throw new InvalidOperationException("truncated two-byte opcode");
                    key = (ushort)(0xFE00 | il[p++]);
                }
                OpCode op;
                if (!Codes.TryGetValue(key, out op))
                    throw new InvalidOperationException("unknown opcode 0x" + key.ToString("X4", CultureInfo.InvariantCulture));
                var insn = new Insn { Index = index++, Offset = offset, Op = op };
                insn.Operand = ReadOperand(method, il, ref p, op);
                insn.EndOffset = p;
                result.Add(insn);
            }
            return result;
        }

        static string ReadOperand(MethodInfo method, byte[] il, ref int p, OpCode op)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone: return string.Empty;
                case OperandType.ShortInlineI: return ((sbyte)il[p++]).ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineI: { int v = BitConverter.ToInt32(il, p); p += 4; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.InlineI8: { long v = BitConverter.ToInt64(il, p); p += 8; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineR: { float v = BitConverter.ToSingle(il, p); p += 4; return v.ToString("R", CultureInfo.InvariantCulture); }
                case OperandType.InlineR: { double v = BitConverter.ToDouble(il, p); p += 8; return v.ToString("R", CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineVar: return il[p++].ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineVar: { ushort v = BitConverter.ToUInt16(il, p); p += 2; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineBrTarget:
                { sbyte d = (sbyte)il[p++]; int target = p + d; return "IL_" + target.ToString("X4", CultureInfo.InvariantCulture); }
                case OperandType.InlineBrTarget:
                { int d = BitConverter.ToInt32(il, p); p += 4; int target = p + d; return "IL_" + target.ToString("X4", CultureInfo.InvariantCulture); }
                case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, p); p += 4; int baseOffset = p + 4 * count;
                    var names = new string[count];
                    for (int i = 0; i < count; i++) { int d = BitConverter.ToInt32(il, p); p += 4; names[i] = "IL_" + (baseOffset + d).ToString("X4", CultureInfo.InvariantCulture); }
                    return string.Join(",", names);
                }
                case OperandType.InlineString:
                { int token = BitConverter.ToInt32(il, p); p += 4; return Token(method, token, true); }
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                { int token = BitConverter.ToInt32(il, p); p += 4; return Token(method, token, false); }
                default: throw new InvalidOperationException("unsupported operand type " + op.OperandType);
            }
        }

        static string Token(MethodInfo method, int token, bool isString)
        {
            string prefix = "0x" + token.ToString("X8", CultureInfo.InvariantCulture);
            try
            {
                if (isString) return prefix + "=" + Safe(method.Module.ResolveString(token));
                Type[] typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : Type.EmptyTypes;
                Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
                MemberInfo member = method.Module.ResolveMember(token, typeArgs, methodArgs);
                return prefix + "=" + Safe(MemberName(member));
            }
            catch { return prefix + "=UNRESOLVED"; }
        }

        static string MemberName(MemberInfo member)
        {
            if (member == null) return "null";
            Type declaring = member.DeclaringType;
            return (declaring == null ? string.Empty : (declaring.FullName ?? declaring.Name) + ".") + member.Name;
        }

        static string Signature(MethodInfo method)
        {
            ParameterInfo[] parameters = method.GetParameters();
            var args = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                Type t = parameters[i].ParameterType;
                args[i] = t == null ? "NULL" : (t.FullName ?? t.Name);
            }
            Type rt = method.ReturnType;
            return method.Name + "(" + string.Join(",", args) + ")->" + (rt == null ? "NULL" : (rt.FullName ?? rt.Name));
        }

        static Dictionary<ushort, OpCode> BuildCodes()
        {
            var result = new Dictionary<ushort, OpCode>();
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i = 0; i < fields.Length; i++)
            {
                if (fields[i].FieldType != typeof(OpCode)) continue;
                OpCode op = (OpCode)fields[i].GetValue(null);
                result[unchecked((ushort)op.Value)] = op;
            }
            return result;
        }

        static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var sb = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++) sb.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string R(double value) { return value.ToString("R", CultureInfo.InvariantCulture); }
        static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Replace(';', ',').Replace('|', '/');
        }
    }
}
