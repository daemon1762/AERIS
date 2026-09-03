using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS41 R041 Phase B: exact closure discovery for Eeloo PQSMod_VertexVoronoi.
    //
    // HARD SAFETY CONTRACT:
    // - Main-thread metadata/state observer only.
    // - Reads fields and managed MethodBody IL only.
    // - NEVER invokes PQS modifier callbacks or arbitrary property getters.
    // - NEVER mutates PQS, DB, preload state, producer routing, or flight controls.
    // - No worker receives Unity/KSP/runtime objects.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS41EelooVoronoiIlClosureObserver : MonoBehaviour
    {
        const string Candidate = "AERIS41_R041_EELOO_VORONOI_IL_CLOSURE_V1";
        const string TargetBody = "Eeloo";
        const string TargetShortType = "PQSMod_VertexVoronoi";
        static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();

        sealed class Insn
        {
            internal int Index;
            internal int Offset;
            internal int EndOffset;
            internal OpCode Op;
            internal string Operand;
        }

        sealed class Counters
        {
            internal int TargetInstances;
            internal int TargetMethods;
            internal int DependencyTypes;
            internal int DependencyMethods;
            internal int Instructions;
            internal bool TargetOnSetup;
            internal bool TargetOnVertexBuildHeight;
            internal bool TargetGetVertexMinHeight;
            internal bool TargetGetVertexMaxHeight;
            internal bool VoronoiType;
            internal bool VoronoiGetValue;
            internal bool GradientNoiseBasisType;
            internal bool GradientValueNoise3D;
        }

        sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return obj == null ? 0 : RuntimeHelpers.GetHashCode(obj); }
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
            try
            {
                Emit();
            }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    "[AERIS41][VORONOI_IL][FAIL]" +
                    "; candidate=" + Candidate +
                    "; stage=MAIN_THREAD_READ_ONLY_CLOSURE" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        void Emit()
        {
            AERISLogger.Info(
                "[AERIS41][VORONOI_IL][BEGIN]" +
                "; candidate=" + Candidate +
                "; body=" + TargetBody +
                "; target_type=" + TargetShortType +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; closure=TARGET_DECLARED_IL_PLUS_RUNTIME_LIBNOISE_FIELD_GRAPH_PLUS_GRADIENT_NOISE_BASIS" +
                "; invokes_pqs_callbacks=false" +
                "; invokes_dependency_methods=false" +
                "; arbitrary_property_getters=false" +
                Invariants());

            CelestialBody body = FindBody(TargetBody);
            if (body == null || body.pqsController == null)
                throw new InvalidOperationException("EELOO_PQS_MISSING");

            IList mods = GetModifierList(body.pqsController);
            if (mods == null)
                throw new InvalidOperationException("EELOO_MODIFIER_LIST_MISSING");

            var counters = new Counters();
            var seenDependencyTypes = new HashSet<string>(StringComparer.Ordinal);
            var seenObjects = new HashSet<object>(ReferenceComparer.Instance);

            for (int i = 0; i < mods.Count; i++)
            {
                object mod = mods[i];
                if (mod == null || !IsEnabled(mod)) continue;
                Type type = mod.GetType();
                if (!string.Equals(type.Name, TargetShortType, StringComparison.Ordinal)) continue;
                if (FindDeclaredMethod(type, "OnVertexBuildHeight") == null) continue;

                counters.TargetInstances++;
                AERISLogger.Info(
                    "[AERIS41][VORONOI_IL][TARGET]" +
                    "; body=" + TargetBody +
                    "; source_index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; type=" + Safe(type.FullName ?? type.Name) +
                    "; assembly=" + Safe(type.Assembly.GetName().Name) +
                    "; enabled=true" +
                    "; authority=RUNTIME_TYPE_AND_READ_ONLY_FIELD_STATE" +
                    Invariants());

                EmitTargetFieldGraph(
                    mod,
                    type,
                    "target[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    seenObjects,
                    seenDependencyTypes,
                    counters);

                EmitDeclaredMethods(type, true, seenDependencyTypes, counters);
            }

            // Explicitly close the LibNoise types that a stock Voronoi implementation
            // is expected to depend on. This is metadata-only: no constructor/getter/
            // noise function is invoked here.
            Assembly stock = typeof(CelestialBody).Assembly;
            Type voronoi = stock.GetType("LibNoise.Voronoi", false);
            if (voronoi != null)
                EmitLibNoiseTypeClosure(voronoi, "explicit:LibNoise.Voronoi", seenDependencyTypes, counters);

            Type gradient = stock.GetType("LibNoise.GradientNoiseBasis", false);
            if (gradient != null)
                EmitLibNoiseTypeClosure(gradient, "explicit:LibNoise.GradientNoiseBasis", seenDependencyTypes, counters);

            bool pass =
                counters.TargetInstances > 0 &&
                counters.TargetOnSetup &&
                counters.TargetOnVertexBuildHeight &&
                counters.VoronoiType &&
                counters.VoronoiGetValue &&
                counters.GradientNoiseBasisType &&
                counters.GradientValueNoise3D &&
                counters.Instructions > 0;

            AERISLogger.Info(
                "[AERIS41][VORONOI_IL][COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; candidate=" + Candidate +
                "; body=" + TargetBody +
                "; target_instances=" + counters.TargetInstances.ToString(CultureInfo.InvariantCulture) +
                "; target_methods=" + counters.TargetMethods.ToString(CultureInfo.InvariantCulture) +
                "; dependency_types=" + counters.DependencyTypes.ToString(CultureInfo.InvariantCulture) +
                "; dependency_methods=" + counters.DependencyMethods.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" + counters.Instructions.ToString(CultureInfo.InvariantCulture) +
                "; target_onsetup=" + Bool(counters.TargetOnSetup) +
                "; target_onvertexbuildheight=" + Bool(counters.TargetOnVertexBuildHeight) +
                "; target_getvertexminheight=" + Bool(counters.TargetGetVertexMinHeight) +
                "; target_getvertexmaxheight=" + Bool(counters.TargetGetVertexMaxHeight) +
                "; libnoise_voronoi_type=" + Bool(counters.VoronoiType) +
                "; libnoise_voronoi_getvalue=" + Bool(counters.VoronoiGetValue) +
                "; gradient_noise_basis_type=" + Bool(counters.GradientNoiseBasisType) +
                "; gradient_value_noise_3d=" + Bool(counters.GradientValueNoise3D) +
                "; invokes_pqs_callbacks=false" +
                "; invokes_dependency_methods=false" +
                "; arbitrary_property_getters=false" +
                Invariants());

            if (!pass)
                throw new InvalidOperationException("EELOO_VORONOI_IL_CLOSURE_INCOMPLETE");
        }

        static void EmitTargetFieldGraph(
            object instance,
            Type type,
            string path,
            HashSet<object> seenObjects,
            HashSet<string> seenDependencyTypes,
            Counters counters)
        {
            for (Type current = type;
                current != null && typeof(PQSMod).IsAssignableFrom(current);
                current = current.BaseType)
            {
                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                SortFields(fields);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    object value;
                    try { value = field.GetValue(instance); }
                    catch (Exception ex)
                    {
                        AERISLogger.Info(
                            "[AERIS41][VORONOI_IL][FIELD_READ_FAIL]" +
                            "; path=" + Safe(path) +
                            "; declaring_type=" + Safe(current.FullName ?? current.Name) +
                            "; name=" + Safe(field.Name) +
                            "; error=" + Safe(ex.GetType().FullName) +
                            "; authority=MAIN_THREAD_READ_ONLY_FIELD_DISCOVERY");
                        continue;
                    }

                    EmitField(path, current, field, value, false);
                    if (value != null && IsLibNoiseType(value.GetType()))
                    {
                        EmitLibNoiseObjectClosure(
                            value,
                            path + "." + field.Name,
                            seenObjects,
                            seenDependencyTypes,
                            counters);
                    }
                }
            }
        }

        static void EmitLibNoiseObjectClosure(
            object instance,
            string path,
            HashSet<object> seenObjects,
            HashSet<string> seenDependencyTypes,
            Counters counters)
        {
            if (instance == null) return;
            Type runtimeType = instance.GetType();
            if (!IsLibNoiseType(runtimeType)) return;

            AERISLogger.Info(
                "[AERIS41][VORONOI_IL][DEPENDENCY_INSTANCE]" +
                "; path=" + Safe(path) +
                "; runtime_type=" + Safe(runtimeType.FullName ?? runtimeType.Name) +
                "; assembly=" + Safe(runtimeType.Assembly.GetName().Name) +
                "; authority=MAIN_THREAD_READ_ONLY_FIELD_GRAPH");

            bool firstObjectVisit = seenObjects.Add(instance);
            for (Type current = runtimeType; current != null && IsLibNoiseType(current); current = current.BaseType)
            {
                EmitLibNoiseTypeClosure(current, path, seenDependencyTypes, counters);
                if (!firstObjectVisit) continue;

                FieldInfo[] fields = current.GetFields(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                SortFields(fields);
                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    object value;
                    try { value = field.GetValue(instance); }
                    catch { continue; }
                    EmitField(path, current, field, value, false);

                    if (value != null && IsLibNoiseType(value.GetType()))
                    {
                        EmitLibNoiseObjectClosure(
                            value,
                            path + "." + field.Name,
                            seenObjects,
                            seenDependencyTypes,
                            counters);
                    }
                }
            }
        }

        static void EmitLibNoiseTypeClosure(
            Type type,
            string sourcePath,
            HashSet<string> seenDependencyTypes,
            Counters counters)
        {
            if (type == null || !IsLibNoiseType(type)) return;
            string typeName = type.FullName ?? type.Name;
            if (!seenDependencyTypes.Add(typeName)) return;

            counters.DependencyTypes++;
            if (string.Equals(typeName, "LibNoise.Voronoi", StringComparison.Ordinal))
                counters.VoronoiType = true;
            if (string.Equals(typeName, "LibNoise.GradientNoiseBasis", StringComparison.Ordinal))
                counters.GradientNoiseBasisType = true;

            AERISLogger.Info(
                "[AERIS41][VORONOI_IL][DEPENDENCY_TYPE]" +
                "; source_path=" + Safe(sourcePath) +
                "; type=" + Safe(typeName) +
                "; base_type=" + Safe(type.BaseType == null ? string.Empty : (type.BaseType.FullName ?? type.BaseType.Name)) +
                "; assembly=" + Safe(type.Assembly.GetName().Name) +
                "; authority=IL_METADATA");

            FieldInfo[] statics = type.GetFields(
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            SortFields(statics);
            for (int i = 0; i < statics.Length; i++)
            {
                object value;
                try { value = statics[i].GetValue(null); }
                catch { continue; }
                EmitField("static", type, statics[i], value, true);
            }

            EmitDeclaredMethods(type, false, seenDependencyTypes, counters);

            Type baseType = type.BaseType;
            if (baseType != null && IsLibNoiseType(baseType))
                EmitLibNoiseTypeClosure(baseType, sourcePath + ":base", seenDependencyTypes, counters);
        }

        static void EmitDeclaredMethods(
            Type type,
            bool target,
            HashSet<string> seenDependencyTypes,
            Counters counters)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Array.Sort(methods, delegate(MethodInfo a, MethodInfo b)
            {
                int at = SafeMetadataToken(a);
                int bt = SafeMetadataToken(b);
                int c = at.CompareTo(bt);
                if (c != 0) return c;
                return string.CompareOrdinal(a.Name, b.Name);
            });

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null) continue;
                int emittedInstructions = EmitMethod(type, method, target ? "TARGET" : "DEPENDENCY");
                if (emittedInstructions < 0) continue;

                counters.Instructions += emittedInstructions;
                if (target)
                {
                    counters.TargetMethods++;
                    if (string.Equals(method.Name, "OnSetup", StringComparison.Ordinal)) counters.TargetOnSetup = true;
                    if (string.Equals(method.Name, "OnVertexBuildHeight", StringComparison.Ordinal)) counters.TargetOnVertexBuildHeight = true;
                    if (string.Equals(method.Name, "GetVertexMinHeight", StringComparison.Ordinal)) counters.TargetGetVertexMinHeight = true;
                    if (string.Equals(method.Name, "GetVertexMaxHeight", StringComparison.Ordinal)) counters.TargetGetVertexMaxHeight = true;
                }
                else
                {
                    counters.DependencyMethods++;
                    string typeName = type.FullName ?? type.Name;
                    if (string.Equals(typeName, "LibNoise.Voronoi", StringComparison.Ordinal) &&
                        string.Equals(method.Name, "GetValue", StringComparison.Ordinal))
                        counters.VoronoiGetValue = true;
                    if (string.Equals(typeName, "LibNoise.GradientNoiseBasis", StringComparison.Ordinal) &&
                        string.Equals(method.Name, "ValueNoise3D", StringComparison.Ordinal))
                        counters.GradientValueNoise3D = true;
                }
            }
        }

        static int EmitMethod(Type type, MethodInfo method, string role)
        {
            MethodBody body;
            try { body = method.GetMethodBody(); }
            catch { return -1; }
            if (body == null) return -1;
            byte[] il = body.GetILAsByteArray();
            if (il == null) return -1;

            List<Insn> insns = Decode(method, il);
            AERISLogger.Info(
                "[AERIS41][VORONOI_IL][METHOD]" +
                "; role=" + role +
                "; type=" + Safe(type.FullName ?? type.Name) +
                "; method=" + Safe(Signature(method)) +
                "; metadata_token=0x" + SafeMetadataToken(method).ToString("X8", CultureInfo.InvariantCulture) +
                "; il_bytes=" + il.Length.ToString(CultureInfo.InvariantCulture) +
                "; instructions=" + insns.Count.ToString(CultureInfo.InvariantCulture) +
                "; max_stack=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture) +
                "; locals=" + body.LocalVariables.Count.ToString(CultureInfo.InvariantCulture) +
                "; il_sha256=" + Sha256(il) +
                "; authority=IL_METADATA");

            for (int i = 0; i < body.LocalVariables.Count; i++)
            {
                LocalVariableInfo local = body.LocalVariables[i];
                AERISLogger.Info(
                    "[AERIS41][VORONOI_IL][LOCAL]" +
                    "; role=" + role +
                    "; type=" + Safe(type.FullName ?? type.Name) +
                    "; method=" + Safe(method.Name) +
                    "; index=" + i.ToString(CultureInfo.InvariantCulture) +
                    "; local_type=" + Safe(local.LocalType == null ? "NULL" : (local.LocalType.FullName ?? local.LocalType.Name)) +
                    "; pinned=" + Bool(local.IsPinned) +
                    "; authority=IL_METADATA");
            }

            for (int i = 0; i < insns.Count; i++)
            {
                Insn x = insns[i];
                AERISLogger.Info(
                    "[AERIS41][VORONOI_IL][INSN]" +
                    "; role=" + role +
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

        static void EmitField(string path, Type declaringType, FieldInfo field, object value, bool isStatic)
        {
            string serialized;
            string bits;
            if (!TrySerializeScalar(value, field.FieldType, out serialized, out bits))
            {
                string arrayDigest;
                if (TryArrayDigest(value, out arrayDigest))
                    serialized = arrayDigest;
                else
                    serialized = "OBJECT:" + (value == null ? "null" : (value.GetType().FullName ?? value.GetType().Name));
            }

            AERISLogger.Info(
                "[AERIS41][VORONOI_IL][FIELD]" +
                "; path=" + Safe(path) +
                "; declaring_type=" + Safe(declaringType.FullName ?? declaringType.Name) +
                "; name=" + Safe(field.Name) +
                "; field_type=" + Safe(field.FieldType.FullName ?? field.FieldType.Name) +
                "; static=" + Bool(isStatic) +
                "; value=" + Safe(serialized) +
                "; value_bits=" + Safe(bits) +
                "; authority=MAIN_THREAD_READ_ONLY_FIELD_DISCOVERY");
        }

        static bool TrySerializeScalar(object value, Type declaredType, out string serialized, out string bits)
        {
            serialized = string.Empty;
            bits = string.Empty;
            if (value == null) { serialized = "null"; return true; }
            Type type = declaredType ?? value.GetType();
            if (type == typeof(string)) { serialized = (string)value; return true; }
            if (type == typeof(bool)) { serialized = Bool((bool)value); return true; }
            if (type.IsEnum) { serialized = value.ToString(); return true; }
            if (type == typeof(double))
            {
                double v = (double)value;
                serialized = v.ToString("R", CultureInfo.InvariantCulture);
                bits = "0x" + unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("X16", CultureInfo.InvariantCulture);
                return true;
            }
            if (type == typeof(float))
            {
                float v = (float)value;
                serialized = v.ToString("R", CultureInfo.InvariantCulture);
                bits = "0x" + unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(v), 0)).ToString("X8", CultureInfo.InvariantCulture);
                return true;
            }
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

        static bool TryArrayDigest(object value, out string serialized)
        {
            serialized = string.Empty;
            if (value == null) return false;
            Array array = value as Array;
            if (array == null || array.Rank != 1) return false;

            Type element = array.GetType().GetElementType();
            if (element == null || !(element.IsPrimitive || element.IsEnum)) return false;

            try
            {
                int byteLength = Buffer.ByteLength(array);
                byte[] bytes = new byte[byteLength];
                Buffer.BlockCopy(array, 0, bytes, 0, byteLength);
                serialized =
                    "ARRAY:length=" + array.Length.ToString(CultureInfo.InvariantCulture) +
                    ",bytes=" + byteLength.ToString(CultureInfo.InvariantCulture) +
                    ",sha256=" + Sha256(bytes);
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool IsLibNoiseType(Type type)
        {
            if (type == null) return false;
            string full = type.FullName ?? type.Name ?? string.Empty;
            return full.StartsWith("LibNoise.", StringComparison.Ordinal);
        }

        static void SortFields(FieldInfo[] fields)
        {
            Array.Sort(fields, delegate(FieldInfo a, FieldInfo b)
            {
                int c = SafeMetadataToken(a).CompareTo(SafeMetadataToken(b));
                if (c != 0) return c;
                return string.CompareOrdinal(a.Name, b.Name);
            });
        }

        static int SafeMetadataToken(MemberInfo member)
        {
            if (member == null) return 0;
            try { return member.MetadataToken; }
            catch { return 0; }
        }

        static MethodInfo FindDeclaredMethod(Type type, string name)
        {
            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < methods.Length; i++)
                if (methods[i] != null && string.Equals(methods[i].Name, name, StringComparison.Ordinal))
                    return methods[i];
            return null;
        }

        static bool IsEnabled(object mod)
        {
            object raw = ReadFieldOnly(mod, "modEnabled");
            if (raw is bool) return (bool)raw;
            raw = ReadFieldOnly(mod, "enabled");
            if (raw is bool) return (bool)raw;
            return true;
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null) continue;
                if (string.Equals(body.bodyName, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(body.name, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
        }

        static IList GetModifierList(object pqs)
        {
            object raw = ReadFieldOnly(pqs, "mods") ??
                ReadFieldOnly(pqs, "modifiers") ??
                ReadFieldOnly(pqs, "pqsMods");
            return raw as IList;
        }

        static object ReadFieldOnly(object target, string name)
        {
            if (target == null) return null;
            for (Type current = target.GetType(); current != null; current = current.BaseType)
            {
                FieldInfo field = current.GetField(
                    name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.DeclaredOnly);
                if (field == null) continue;
                try { return field.GetValue(target); }
                catch { return null; }
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
                    if (p >= il.Length) throw new InvalidOperationException("TRUNCATED_TWO_BYTE_OPCODE");
                    key = (ushort)(0xFE00 | il[p++]);
                }
                OpCode op;
                if (!Codes.TryGetValue(key, out op))
                    throw new InvalidOperationException("UNKNOWN_OPCODE_0x" + key.ToString("X4", CultureInfo.InvariantCulture));
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
                case OperandType.InlineI:
                { int v = BitConverter.ToInt32(il, p); p += 4; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.InlineI8:
                { long v = BitConverter.ToInt64(il, p); p += 8; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineR:
                { float v = BitConverter.ToSingle(il, p); p += 4; return v.ToString("R", CultureInfo.InvariantCulture); }
                case OperandType.InlineR:
                { double v = BitConverter.ToDouble(il, p); p += 8; return v.ToString("R", CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineVar: return il[p++].ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineVar:
                { ushort v = BitConverter.ToUInt16(il, p); p += 2; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineBrTarget:
                { sbyte d = (sbyte)il[p++]; int target = p + d; return "IL_" + target.ToString("X4", CultureInfo.InvariantCulture); }
                case OperandType.InlineBrTarget:
                { int d = BitConverter.ToInt32(il, p); p += 4; int target = p + d; return "IL_" + target.ToString("X4", CultureInfo.InvariantCulture); }
                case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, p); p += 4;
                    int baseOffset = p + (4 * count);
                    var names = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        int d = BitConverter.ToInt32(il, p); p += 4;
                        names[i] = "IL_" + (baseOffset + d).ToString("X4", CultureInfo.InvariantCulture);
                    }
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
                default:
                    throw new InvalidOperationException("UNSUPPORTED_OPERAND_TYPE_" + op.OperandType);
            }
        }

        static string Token(MethodInfo method, int token, bool isString)
        {
            string prefix = "0x" + token.ToString("X8", CultureInfo.InvariantCulture);
            try
            {
                if (isString) return prefix + "=" + Safe(method.Module.ResolveString(token));
                Type[] typeArgs = method.DeclaringType != null && method.DeclaringType.IsGenericType
                    ? method.DeclaringType.GetGenericArguments()
                    : Type.EmptyTypes;
                Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
                MemberInfo member = method.Module.ResolveMember(token, typeArgs, methodArgs);
                return prefix + "=" + Safe(MemberName(member));
            }
            catch
            {
                return prefix + "=UNRESOLVED";
            }
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
            return method.Name + "(" + string.Join(",", args) + ")->" +
                (rt == null ? "NULL" : (rt.FullName ?? rt.Name));
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
                for (int i = 0; i < digest.Length; i++)
                    sb.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        static string Bool(bool value) { return value ? "true" : "false"; }
        static string Safe(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Replace(';', ',').Replace('|', '/');
        }

        static string Invariants()
        {
            return
                "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; production_worker_runtime_object_access=false" +
                "; pqs_callback_invocation=false" +
                "; runtime_state_mutation=false";
        }
    }
}
