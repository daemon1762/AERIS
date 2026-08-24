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
    // AERIS34_REV3_5_R039_MINMUS_PRIMITIVE_DEEP_CLOSURE_SHADOW
    // Main-thread observation only. Runtime math objects are never invoked on workers.
    // No DB write, producer switch, GPU path, or certification authority.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR039PtcMinmusPrimitiveDeepClosureObserver : MonoBehaviour
    {
        const string CandidateMarker =
            "AERIS34_REV3_5_R039_MINMUS_PRIMITIVE_DEEP_CLOSURE_SHADOW";

        static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();

        float nextAttempt;
        bool captured;

        void Update()
        {
            if (captured || Time.realtimeSinceStartup < nextAttempt) return;

            nextAttempt = Time.realtimeSinceStartup + 1f;

            if (!AERISTerrainTileSystem.GameDataHashReady ||
                FlightGlobals.Bodies == null ||
                FlightGlobals.Bodies.Count == 0)
                return;

            Capture();
            captured = true;
        }

        void Capture()
        {
            int failures = 0;
            int methods = 0;
            int instructions = 0;
            int arrays = 0;
            int arrayElements = 0;

            CelestialBody minmus = FindBody("Minmus");
            if (minmus == null)
            {
                AERISLogger.Info(
                    "[R039][PRIMITIVE_DEEP_FAIL] stage=BODY; error=MINMUS_NOT_FOUND");
                return;
            }

            object pqs = ReadMember(minmus, "pqsController");
            object vertexPlanet = FindMod(pqs, "PQSMod_VertexPlanet");

            if (vertexPlanet == null)
            {
                AERISLogger.Info(
                    "[R039][PRIMITIVE_DEEP_FAIL] stage=MOD; error=VERTEXPLANET_NOT_FOUND");
                return;
            }

            AERISLogger.Info(
                "[R039][PRIMITIVE_DEEP_BEGIN]" +
                "; body=Minmus" +
                "; candidate=" + CandidateMarker +
                "; runtime_type=" + Safe(vertexPlanet.GetType().FullName) +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                "; worker_invokes_runtime_object=false" +
                "; certification=NO_SHADOW_ONLY" +
                "; db_write=false; producer_switch=false; gpu=false; authority=PQS");

            string[] labels =
            {
                "continental",
                "continentalSmoothing",
                "continentalSharpnessMap",
                "continentalRuggedness",
                "continentalSharpness"
            };

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < labels.Length; i++)
            {
                string label = labels[i];

                object wrapper = null;
                try { wrapper = ReadMember(vertexPlanet, label); }
                catch { }

                if (wrapper == null)
                {
                    failures++;
                    AERISLogger.Info(
                        "[R039][PRIMITIVE_DEEP_FAIL]" +
                        "; stage=WRAPPER" +
                        "; label=" + Safe(label) +
                        "; error=NULL");
                    continue;
                }

                bool noiseWrapper =
                    string.Equals(label, "continentalSharpness",
                        StringComparison.Ordinal);

                object primitive = null;
                try
                {
                    primitive = ReadMember(
                        wrapper,
                        noiseWrapper ? "noise" : "simplex");
                }
                catch { }

                if (primitive == null)
                {
                    failures++;
                    AERISLogger.Info(
                        "[R039][PRIMITIVE_DEEP_FAIL]" +
                        "; stage=PRIMITIVE" +
                        "; label=" + Safe(label) +
                        "; error=NULL");
                    continue;
                }

                Type primitiveType = primitive.GetType();

                AERISLogger.Info(
                    "[R039][PRIMITIVE_OBJECT]" +
                    "; label=" + Safe(label) +
                    "; wrapper_type=" + Safe(wrapper.GetType().FullName) +
                    "; primitive_type=" + Safe(primitiveType.FullName) +
                    "; assembly=" + Safe(primitiveType.Assembly.FullName));

                DumpArrays(
                    primitive,
                    label,
                    ref arrays,
                    ref arrayElements,
                    ref failures);

                MethodInfo[] roots;
                try
                {
                    roots = primitiveType.GetMethods(
                        BindingFlags.Instance |
                        BindingFlags.Static |
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.DeclaredOnly);
                }
                catch
                {
                    failures++;
                    continue;
                }

                Array.Sort(
                    roots,
                    delegate(MethodInfo a, MethodInfo b)
                    {
                        int c = string.CompareOrdinal(a.Name, b.Name);
                        if (c != 0) return c;
                        return a.MetadataToken.CompareTo(b.MetadataToken);
                    });

                for (int m = 0; m < roots.Length; m++)
                {
                    MethodInfo root = roots[m];
                    if (root == null || !WantedRoot(root)) continue;

                    DumpMethodRecursive(
                        root,
                        label,
                        0,
                        seen,
                        ref methods,
                        ref instructions,
                        ref failures);
                }
            }

            AERISLogger.Info(
                "[R039][PRIMITIVE_DEEP_COMPLETE]" +
                "; body=Minmus" +
                "; methods=" + methods +
                "; instructions=" + instructions +
                "; arrays=" + arrays +
                "; array_elements=" + arrayElements +
                "; failures=" + failures +
                "; worker_ready=false" +
                "; pending=PURE_CPU_PRIMITIVE_RECONSTRUCTION" +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                "; worker_invokes_runtime_object=false" +
                "; certification=NO_SHADOW_ONLY" +
                "; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static bool WantedRoot(MethodInfo method)
        {
            string n = method.Name;

            return n == "noise" ||
                   n == "noiseNormalized" ||
                   n == "fastfloor" ||
                   n == "dot" ||
                   n == "SetupPermTable" ||
                   n == "set_seed" ||
                   n == "GetValue" ||
                   n == "CalculateSpectralWeights";
        }

        static void DumpMethodRecursive(
            MethodBase method,
            string owner,
            int depth,
            HashSet<string> seen,
            ref int methods,
            ref int instructions,
            ref int failures)
        {
            if (method == null) return;

            if (depth > 16)
            {
                failures++;
                AERISLogger.Info(
                    "[R039][PRIMITIVE_DEEP_FAIL]" +
                    "; stage=DEPTH" +
                    "; method=" + Safe(MethodName(method)));
                return;
            }

            string key =
                method.Module.ModuleVersionId.ToString("N") +
                ":" +
                method.MetadataToken.ToString("X8",
                    CultureInfo.InvariantCulture);

            if (!seen.Add(key)) return;

            MethodBody body = null;
            byte[] il = null;

            try
            {
                body = method.GetMethodBody();
                il = body == null ? null : body.GetILAsByteArray();
            }
            catch (Exception ex)
            {
                failures++;
                AERISLogger.Info(
                    "[R039][PRIMITIVE_DEEP_FAIL]" +
                    "; stage=METHOD_BODY" +
                    "; owner=" + Safe(owner) +
                    "; method=" + Safe(MethodName(method)) +
                    "; error=" + Safe(ex.GetType().Name));
                return;
            }

            methods++;

            AERISLogger.Info(
                "[R039][PRIMITIVE_METHOD]" +
                "; owner=" + Safe(owner) +
                "; depth=" + depth +
                "; runtime_type=" +
                    Safe(method.DeclaringType == null
                        ? string.Empty
                        : method.DeclaringType.FullName) +
                "; method=" + Safe(Signature(method)) +
                "; metadata_token=0x" +
                    method.MetadataToken.ToString(
                        "X8", CultureInfo.InvariantCulture) +
                "; body_present=" + (il != null) +
                "; il_bytes=" + (il == null ? 0 : il.Length) +
                "; il_sha256=" + (il == null ? string.Empty : Sha256(il)) +
                "; max_stack=" + (body == null ? -1 : body.MaxStackSize) +
                "; local_count=" +
                    (body == null ? -1 : body.LocalVariables.Count) +
                "; exception_clauses=" +
                    (body == null
                        ? -1
                        : body.ExceptionHandlingClauses.Count));

            if (il == null) return;

            var decoded = new List<string>();
            var dependencies = new List<MethodBase>();

            Decode(
                method,
                il,
                decoded,
                dependencies,
                ref failures);

            instructions += decoded.Count;

            const int PerChunk = 16;

            for (int p = 0, chunk = 0;
                 p < decoded.Count;
                 p += PerChunk, chunk++)
            {
                int take = Math.Min(PerChunk, decoded.Count - p);
                string[] part = new string[take];

                for (int j = 0; j < take; j++)
                    part[j] = decoded[p + j];

                AERISLogger.Info(
                    "[R039][PRIMITIVE_IL_CHUNK]" +
                    "; owner=" + Safe(owner) +
                    "; depth=" + depth +
                    "; method=" + Safe(MethodName(method)) +
                    "; chunk=" + chunk +
                    "; instructions=" +
                        Safe(string.Join("/", part)));
            }

            for (int i = 0; i < dependencies.Count; i++)
            {
                MethodBase dep = dependencies[i];
                if (dep == null || dep.DeclaringType == null)
                    continue;

                if (AllowedMathType(dep.DeclaringType))
                {
                    DumpMethodRecursive(
                        dep,
                        owner,
                        depth + 1,
                        seen,
                        ref methods,
                        ref instructions,
                        ref failures);
                }
                else
                {
                    AERISLogger.Info(
                        "[R039][PRIMITIVE_EXTERNAL_CALL]" +
                        "; owner=" + Safe(owner) +
                        "; from=" + Safe(MethodName(method)) +
                        "; target=" + Safe(MethodName(dep)));
                }
            }
        }

        static void Decode(
            MethodBase method,
            byte[] il,
            List<string> result,
            List<MethodBase> dependencies,
            ref int failures)
        {
            int p = 0;

            while (p < il.Length)
            {
                int offset = p;

                ushort key = il[p++];

                if (key == 0xFE && p < il.Length)
                    key = (ushort)(0xFE00 | il[p++]);

                OpCode op;

                if (!Codes.TryGetValue(key, out op))
                {
                    result.Add(
                        offset.ToString("X4") +
                        ":UNKNOWN_" +
                        key.ToString("X4"));

                    failures++;
                    break;
                }

                string operand = string.Empty;

                try
                {
                    operand = ReadOperand(
                        method,
                        il,
                        ref p,
                        op,
                        dependencies);
                }
                catch (Exception ex)
                {
                    failures++;
                    operand = "ERR:" + ex.GetType().Name;
                }

                result.Add(
                    offset.ToString("X4") +
                    ":" +
                    op.Name +
                    (string.IsNullOrEmpty(operand)
                        ? string.Empty
                        : " " + operand));
            }
        }

        static string ReadOperand(
            MethodBase method,
            byte[] il,
            ref int p,
            OpCode op,
            List<MethodBase> dependencies)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone:
                    return string.Empty;

                case OperandType.ShortInlineI:
                    return ((sbyte)il[p++]).ToString(
                        CultureInfo.InvariantCulture);

                case OperandType.InlineI:
                {
                    int v = BitConverter.ToInt32(il, p);
                    p += 4;
                    return v.ToString(CultureInfo.InvariantCulture);
                }

                case OperandType.InlineI8:
                {
                    long v = BitConverter.ToInt64(il, p);
                    p += 8;
                    return v.ToString(CultureInfo.InvariantCulture);
                }

                case OperandType.ShortInlineR:
                {
                    float v = BitConverter.ToSingle(il, p);
                    p += 4;
                    return v.ToString(
                        "R", CultureInfo.InvariantCulture);
                }

                case OperandType.InlineR:
                {
                    double v = BitConverter.ToDouble(il, p);
                    p += 8;
                    return v.ToString(
                        "R", CultureInfo.InvariantCulture);
                }

                case OperandType.ShortInlineVar:
                    return il[p++].ToString(
                        CultureInfo.InvariantCulture);

                case OperandType.InlineVar:
                {
                    ushort v = BitConverter.ToUInt16(il, p);
                    p += 2;
                    return v.ToString(CultureInfo.InvariantCulture);
                }

                case OperandType.ShortInlineBrTarget:
                {
                    sbyte d = (sbyte)il[p++];
                    return "IL_" +
                        (p + d).ToString(
                            "X4", CultureInfo.InvariantCulture);
                }

                case OperandType.InlineBrTarget:
                {
                    int d = BitConverter.ToInt32(il, p);
                    p += 4;
                    return "IL_" +
                        (p + d).ToString(
                            "X4", CultureInfo.InvariantCulture);
                }

                case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, p);
                    p += 4;

                    int baseOffset = p + 4 * count;
                    string[] targets = new string[count];

                    for (int i = 0; i < count; i++)
                    {
                        int d = BitConverter.ToInt32(il, p);
                        p += 4;

                        targets[i] =
                            "IL_" +
                            (baseOffset + d).ToString(
                                "X4",
                                CultureInfo.InvariantCulture);
                    }

                    return string.Join(",", targets);
                }

                case OperandType.InlineString:
                {
                    int token = BitConverter.ToInt32(il, p);
                    p += 4;

                    try
                    {
                        return "0x" +
                            token.ToString("X8") +
                            "=" +
                            Safe(method.Module.ResolveString(token));
                    }
                    catch
                    {
                        return "0x" +
                            token.ToString("X8") +
                            "=UNRESOLVED";
                    }
                }

                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                {
                    int token = BitConverter.ToInt32(il, p);
                    p += 4;
                    return ResolveToken(method, token, null);
                }

                case OperandType.InlineMethod:
                {
                    int token = BitConverter.ToInt32(il, p);
                    p += 4;

                    MethodBase resolved = null;
                    string text = ResolveToken(
                        method,
                        token,
                        dependencies,
                        out resolved);

                    if (resolved != null)
                        dependencies.Add(resolved);

                    return text;
                }

                default:
                    return "operand_type=" + op.OperandType;
            }
        }

        static string ResolveToken(
            MethodBase method,
            int token,
            List<MethodBase> unused)
        {
            MethodBase ignored;
            return ResolveToken(method, token, unused, out ignored);
        }

        static string ResolveToken(
            MethodBase method,
            int token,
            List<MethodBase> unused,
            out MethodBase resolvedMethod)
        {
            resolvedMethod = null;

            string prefix =
                "0x" +
                token.ToString(
                    "X8", CultureInfo.InvariantCulture);

            try
            {
                Type[] ta =
                    method.DeclaringType != null &&
                    method.DeclaringType.IsGenericType
                        ? method.DeclaringType.GetGenericArguments()
                        : Type.EmptyTypes;

                Type[] ma =
                    method.IsGenericMethod
                        ? method.GetGenericArguments()
                        : Type.EmptyTypes;

                MemberInfo member =
                    method.Module.ResolveMember(token, ta, ma);

                MethodBase mb = member as MethodBase;

                if (mb != null)
                    resolvedMethod = mb;

                return prefix + "=" + Safe(MemberName(member));
            }
            catch
            {
                return prefix + "=UNRESOLVED";
            }
        }

        static void DumpArrays(
            object primitive,
            string label,
            ref int arrays,
            ref int elements,
            ref int failures)
        {
            if (primitive == null) return;

            Type type = primitive.GetType();

            FieldInfo[] fields;

            try
            {
                fields = type.GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            }
            catch
            {
                failures++;
                return;
            }

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];

                if (field == null) continue;

                string n = field.Name;

                if (n != "perm" &&
                    n != "grad3" &&
                    n != "SpectralWeights")
                    continue;

                object value = null;

                try
                {
                    value = field.GetValue(
                        field.IsStatic ? null : primitive);
                }
                catch (Exception ex)
                {
                    failures++;

                    AERISLogger.Info(
                        "[R039][PRIMITIVE_DEEP_FAIL]" +
                        "; stage=ARRAY_READ" +
                        "; label=" + Safe(label) +
                        "; name=" + Safe(n) +
                        "; error=" + Safe(ex.GetType().Name));

                    continue;
                }

                Array array = value as Array;

                if (array == null)
                {
                    failures++;

                    AERISLogger.Info(
                        "[R039][PRIMITIVE_DEEP_FAIL]" +
                        "; stage=ARRAY_TYPE" +
                        "; label=" + Safe(label) +
                        "; name=" + Safe(n));

                    continue;
                }

                arrays++;
                elements += array.Length;

                Type elementType =
                    array.GetType().GetElementType();

                AERISLogger.Info(
                    "[R039][PRIMITIVE_ARRAY]" +
                    "; label=" + Safe(label) +
                    "; primitive_type=" + Safe(type.FullName) +
                    "; name=" + Safe(n) +
                    "; length=" + array.Length +
                    "; element_type=" +
                        Safe(elementType == null
                            ? string.Empty
                            : elementType.FullName));

                const int PerChunk = 24;

                for (int start = 0, chunk = 0;
                     start < array.Length;
                     start += PerChunk, chunk++)
                {
                    int take =
                        Math.Min(
                            PerChunk,
                            array.Length - start);

                    string[] values =
                        new string[take];

                    for (int j = 0; j < take; j++)
                    {
                        int index = start + j;

                        object element = null;

                        try
                        {
                            element = array.GetValue(index);
                        }
                        catch
                        {
                            failures++;
                        }

                        values[j] =
                            index.ToString(
                                CultureInfo.InvariantCulture) +
                            ":" +
                            FormatValue(element);
                    }

                    AERISLogger.Info(
                        "[R039][PRIMITIVE_ARRAY_CHUNK]" +
                        "; label=" + Safe(label) +
                        "; name=" + Safe(n) +
                        "; chunk=" + chunk +
                        "; values=" +
                            Safe(string.Join("~", values)));
                }
            }
        }

        static string FormatValue(object value)
        {
            if (value == null) return "null";

            if (value is double)
                return ((double)value).ToString(
                    "R", CultureInfo.InvariantCulture);

            if (value is float)
                return ((float)value).ToString(
                    "R", CultureInfo.InvariantCulture);

            if (value is decimal)
                return ((decimal)value).ToString(
                    "G29", CultureInfo.InvariantCulture);

            Type type = value.GetType();

            if (type.IsPrimitive || type.IsEnum)
                return Convert.ToString(
                    value,
                    CultureInfo.InvariantCulture);

            Array array = value as Array;

            if (array != null)
            {
                int count = Math.Min(8, array.Length);
                string[] parts = new string[count];

                for (int i = 0; i < count; i++)
                    parts[i] =
                        FormatValue(array.GetValue(i));

                return "[" + string.Join(",", parts) + "]";
            }

            object x = TryReadMember(value, "x");
            object y = TryReadMember(value, "y");
            object z = TryReadMember(value, "z");

            if (x != null && y != null && z != null)
                return "(" +
                    FormatValue(x) + "," +
                    FormatValue(y) + "," +
                    FormatValue(z) + ")";

            return Safe(value.ToString());
        }

        static bool AllowedMathType(Type type)
        {
            if (type == null) return false;

            string name =
                type.FullName ?? type.Name;

            return string.Equals(
                       name,
                       "Simplex",
                       StringComparison.Ordinal) ||
                   name.EndsWith(
                       ".Simplex",
                       StringComparison.Ordinal) ||
                   name.StartsWith(
                       "LibNoise.",
                       StringComparison.Ordinal);
        }

        static CelestialBody FindBody(string name)
        {
            if (FlightGlobals.Bodies == null)
                return null;

            for (int i = 0;
                 i < FlightGlobals.Bodies.Count;
                 i++)
            {
                CelestialBody body =
                    FlightGlobals.Bodies[i];

                if (body != null &&
                    string.Equals(
                        body.name,
                        name,
                        StringComparison.OrdinalIgnoreCase))
                    return body;
            }

            return null;
        }

        static object FindMod(
            object pqs,
            string typeName)
        {
            if (pqs == null) return null;

            IEnumerable mods =
                ReadMember(pqs, "mods") as IEnumerable;

            if (mods == null) return null;

            foreach (object mod in mods)
            {
                if (mod == null) continue;

                if (!ReadBool(mod, "enabled", true))
                    continue;

                Type type = mod.GetType();

                string name =
                    type.FullName ?? type.Name;

                if (string.Equals(
                    name,
                    typeName,
                    StringComparison.Ordinal))
                    return mod;
            }

            return null;
        }

        static object ReadMember(
            object target,
            string name)
        {
            if (target == null) return null;

            Type type = target.GetType();

            FieldInfo field =
                type.GetField(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (field != null)
                return field.GetValue(
                    field.IsStatic ? null : target);

            PropertyInfo property =
                type.GetProperty(
                    name,
                    BindingFlags.Instance |
                    BindingFlags.Static |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);

            if (property != null &&
                property.CanRead &&
                property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);

            return null;
        }

        static object TryReadMember(
            object target,
            string name)
        {
            try { return ReadMember(target, name); }
            catch { return null; }
        }

        static bool ReadBool(
            object target,
            string name,
            bool fallback)
        {
            object value = null;

            try { value = ReadMember(target, name); }
            catch { }

            return value is bool
                ? (bool)value
                : fallback;
        }

        static string Signature(MethodBase method)
        {
            ParameterInfo[] parameters =
                method.GetParameters();

            string[] args =
                new string[parameters.Length];

            for (int i = 0;
                 i < parameters.Length;
                 i++)
                args[i] =
                    parameters[i].ParameterType.FullName;

            MethodInfo mi =
                method as MethodInfo;

            string ret =
                mi == null
                    ? "System.Void"
                    : mi.ReturnType.FullName;

            return method.Name +
                "(" +
                string.Join(",", args) +
                ")->" +
                ret;
        }

        static string MethodName(MethodBase method)
        {
            if (method == null) return "null";

            Type type = method.DeclaringType;

            return
                (type == null
                    ? string.Empty
                    : (type.FullName ?? type.Name) + ".") +
                method.Name;
        }

        static string MemberName(MemberInfo member)
        {
            if (member == null) return "null";

            Type type = member.DeclaringType;

            return
                (type == null
                    ? string.Empty
                    : (type.FullName ?? type.Name) + ".") +
                member.Name;
        }

        static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                var sb = new StringBuilder(hash.Length * 2);

                for (int i = 0;
                     i < hash.Length;
                     i++)
                    sb.Append(
                        hash[i].ToString(
                            "x2",
                            CultureInfo.InvariantCulture));

                return sb.ToString();
            }
        }

        static Dictionary<ushort, OpCode> BuildCodes()
        {
            var result =
                new Dictionary<ushort, OpCode>();

            FieldInfo[] fields =
                typeof(OpCodes).GetFields(
                    BindingFlags.Public |
                    BindingFlags.Static);

            for (int i = 0;
                 i < fields.Length;
                 i++)
            {
                if (fields[i].FieldType !=
                    typeof(OpCode))
                    continue;

                OpCode op =
                    (OpCode)fields[i].GetValue(null);

                result[
                    unchecked((ushort)op.Value)] = op;
            }

            return result;
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace(';', ',')
                .Replace('|', '/');
        }
    }
}
