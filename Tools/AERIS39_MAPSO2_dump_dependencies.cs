using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

internal static class AERIS39MapSo2DependencyDump
{
    static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();
    static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "MapSO",
        "UnityEngine.Mathf",
        "UnityEngine.Color",
        "UnityEngine.Color32"
    };

    static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("usage: AERIS39_MAPSO2_dump_dependencies.exe <KSP Managed directory>");
            return 2;
        }

        string managed = Path.GetFullPath(args[0]);
        string assemblyPath = Path.Combine(managed, "Assembly-CSharp.dll");
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine("FAIL: Assembly-CSharp.dll missing: " + assemblyPath);
            return 3;
        }

        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs)
        {
            try
            {
                string name = new AssemblyName(eventArgs.Name).Name + ".dll";
                string candidate = Path.Combine(managed, name);
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            }
            catch
            {
                return null;
            }
        };

        try
        {
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            Type mapSo = assembly.GetType("MapSO", true, false);

            Console.WriteLine("=== AERIS39 MAPSO-2A MANAGED DEPENDENCY CLOSURE ===");
            Console.WriteLine("assembly=" + assemblyPath);
            Console.WriteLine("assembly_sha256=" + Sha256File(assemblyPath));
            Console.WriteLine("assembly_full_name=" + assembly.FullName);
            Console.WriteLine("module_name=" + mapSo.Module.Name);
            Console.WriteLine("module_mvid=" + mapSo.Module.ModuleVersionId.ToString("D"));
            Console.WriteLine("type=" + mapSo.FullName);

            Queue<MethodBase> queue = new Queue<MethodBase>();
            HashSet<string> queued = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> dumped = new HashSet<string>(StringComparer.Ordinal);

            MethodInfo[] methods = mapSo.GetMethods(
                BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);

            Array.Sort(methods, delegate(MethodInfo a, MethodInfo b)
            {
                int c = string.CompareOrdinal(a.Name, b.Name);
                if (c != 0) return c;
                return a.MetadataToken.CompareTo(b.MetadataToken);
            });

            for (int i = 0; i < methods.Length; i++)
            {
                if (WantedMapSoRoot(methods[i].Name))
                    Enqueue(queue, queued, methods[i]);
            }

            ConstructorInfo cctor = mapSo.TypeInitializer;
            bool cctorPresent = cctor != null;
            if (cctor != null)
                Enqueue(queue, queued, cctor);

            int methodCount = 0;
            int managedCount = 0;
            int nonManagedCount = 0;

            bool gotPixelFloat = false;
            bool gotPixelColor32 = false;
            bool gotPixelColor = false;
            bool gotMathfLerp = false;
            bool gotColorLerp = false;
            bool gotColor32Lerp = false;
            bool gotColor32Implicit = false;

            while (queue.Count > 0)
            {
                MethodBase method = queue.Dequeue();
                string key = MethodKey(method);
                if (!dumped.Add(key))
                    continue;

                if (!ShouldDump(method))
                    continue;

                List<MethodBase> references;
                bool hasBody = DumpMethod(method, out references);
                methodCount++;
                if (hasBody) managedCount++; else nonManagedCount++;

                string owner = method.DeclaringType == null ? "" : method.DeclaringType.FullName;
                if (owner == "MapSO" && method.Name == "GetPixelFloat") gotPixelFloat = true;
                if (owner == "MapSO" && method.Name == "GetPixelColor32") gotPixelColor32 = true;
                if (owner == "MapSO" && method.Name == "GetPixelColor") gotPixelColor = true;
                if (owner == "UnityEngine.Mathf" && method.Name == "Lerp") gotMathfLerp = true;
                if (owner == "UnityEngine.Color" && method.Name == "Lerp") gotColorLerp = true;
                if (owner == "UnityEngine.Color32" && method.Name == "Lerp") gotColor32Lerp = true;
                if (owner == "UnityEngine.Color32" && method.Name == "op_Implicit") gotColor32Implicit = true;

                for (int i = 0; i < references.Count; i++)
                {
                    MethodBase referenced = references[i];
                    if (ShouldDump(referenced))
                        Enqueue(queue, queued, referenced);
                }
            }

            Console.WriteLine();
            Console.WriteLine("method_count=" + methodCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("managed_body_count=" + managedCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("nonmanaged_body_count=" + nonManagedCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("mapso_cctor_present=" + Bool(cctorPresent));
            Console.WriteLine("required_MapSO_GetPixelFloat=" + Bool(gotPixelFloat));
            Console.WriteLine("required_MapSO_GetPixelColor32=" + Bool(gotPixelColor32));
            Console.WriteLine("required_MapSO_GetPixelColor=" + Bool(gotPixelColor));
            Console.WriteLine("dependency_Mathf_Lerp=" + Bool(gotMathfLerp));
            Console.WriteLine("dependency_Color_Lerp=" + Bool(gotColorLerp));
            Console.WriteLine("dependency_Color32_Lerp=" + Bool(gotColor32Lerp));
            Console.WriteLine("dependency_Color32_op_Implicit=" + Bool(gotColor32Implicit));

            bool pass = gotPixelFloat && gotPixelColor32 && gotPixelColor && cctorPresent;
            Console.WriteLine("AERIS39_MAPSO2A_DEPENDENCY_DUMP=" + (pass ? "PASS" : "FAIL"));
            return pass ? 0 : 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            return 5;
        }
    }

    static bool WantedMapSoRoot(string name)
    {
        return name == "ConstructBilinearCoords" ||
               name == "PixelIndex" ||
               name == "GetPixelFloat" ||
               name == "GetPixelByte" ||
               name == "GreyByte" ||
               name == "GreyFloat" ||
               name == "GetPixelColor" ||
               name == "GetPixelColor32";
    }

    static bool ShouldDump(MethodBase method)
    {
        if (method == null || method.DeclaringType == null)
            return false;
        return AllowedTypes.Contains(method.DeclaringType.FullName);
    }

    static void Enqueue(Queue<MethodBase> queue, HashSet<string> queued, MethodBase method)
    {
        if (method == null) return;
        string key = MethodKey(method);
        if (queued.Add(key))
            queue.Enqueue(method);
    }

    static string MethodKey(MethodBase method)
    {
        string mvid;
        try { mvid = method.Module.ModuleVersionId.ToString("D"); }
        catch { mvid = "<no-mvid>"; }

        string token;
        try { token = method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture); }
        catch { token = Signature(method); }

        return mvid + ":" + token;
    }

    static bool DumpMethod(MethodBase method, out List<MethodBase> references)
    {
        references = new List<MethodBase>();

        Console.WriteLine();
        Console.WriteLine("--- METHOD ---");
        Console.WriteLine("signature=" + Signature(method));
        Console.WriteLine("declaring_type=" + TypeName(method.DeclaringType));
        Console.WriteLine("metadata_token=0x" + method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture));
        Console.WriteLine("attributes=" + method.Attributes);
        Console.WriteLine("impl_flags=" + method.GetMethodImplementationFlags());
        Console.WriteLine("module_name=" + method.Module.Name);
        Console.WriteLine("module_mvid=" + method.Module.ModuleVersionId.ToString("D"));

        string modulePath = SafeModulePath(method.Module);
        Console.WriteLine("module_path=" + modulePath);
        Console.WriteLine("module_sha256=" + (File.Exists(modulePath) ? Sha256File(modulePath) : ""));

        MethodBody body = null;
        try
        {
            body = method.GetMethodBody();
        }
        catch (Exception ex)
        {
            Console.WriteLine("managed_body=false");
            Console.WriteLine("body_error=" + Safe(ex.GetType().FullName + ":" + ex.Message));
            return false;
        }

        if (body == null)
        {
            Console.WriteLine("managed_body=false");
            return false;
        }

        byte[] il = body.GetILAsByteArray();
        if (il == null) il = new byte[0];

        Console.WriteLine("managed_body=true");
        Console.WriteLine("il_bytes=" + il.Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("il_hex=" + Hex(il));
        Console.WriteLine("il_sha256=" + Sha256Bytes(il));
        Console.WriteLine("max_stack=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("init_locals=" + Bool(body.InitLocals));
        Console.WriteLine("locals=" + body.LocalVariables.Count.ToString(CultureInfo.InvariantCulture));

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

            string operand = ReadOperand(method.Module, il, ref p, op, references);
            Console.WriteLine(
                "IL " + index.ToString("D3", CultureInfo.InvariantCulture) +
                " IL_" + offset.ToString("X4", CultureInfo.InvariantCulture) +
                " " + op.Name +
                (string.IsNullOrEmpty(operand) ? "" : " " + operand));
            index++;
        }

        return true;
    }

    static string ReadOperand(Module module, byte[] il, ref int p, OpCode op, List<MethodBase> references)
    {
        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                return "";
            case OperandType.ShortInlineI:
                return ((sbyte)il[p++]).ToString(CultureInfo.InvariantCulture);
            case OperandType.InlineI:
            {
                int value = BitConverter.ToInt32(il, p); p += 4;
                return value.ToString(CultureInfo.InvariantCulture);
            }
            case OperandType.InlineI8:
            {
                long value = BitConverter.ToInt64(il, p); p += 8;
                return value.ToString(CultureInfo.InvariantCulture);
            }
            case OperandType.ShortInlineR:
            {
                float value = BitConverter.ToSingle(il, p); p += 4;
                return value.ToString("R", CultureInfo.InvariantCulture);
            }
            case OperandType.InlineR:
            {
                double value = BitConverter.ToDouble(il, p); p += 8;
                return value.ToString("R", CultureInfo.InvariantCulture);
            }
            case OperandType.ShortInlineVar:
                return il[p++].ToString(CultureInfo.InvariantCulture);
            case OperandType.InlineVar:
            {
                ushort value = BitConverter.ToUInt16(il, p); p += 2;
                return value.ToString(CultureInfo.InvariantCulture);
            }
            case OperandType.ShortInlineBrTarget:
            {
                sbyte delta = (sbyte)il[p++];
                return "IL_" + (p + delta).ToString("X4", CultureInfo.InvariantCulture);
            }
            case OperandType.InlineBrTarget:
            {
                int delta = BitConverter.ToInt32(il, p); p += 4;
                return "IL_" + (p + delta).ToString("X4", CultureInfo.InvariantCulture);
            }
            case OperandType.InlineSwitch:
            {
                int count = BitConverter.ToInt32(il, p); p += 4;
                int baseOffset = p + count * 4;
                string[] targets = new string[count];
                for (int i = 0; i < count; i++)
                {
                    int delta = BitConverter.ToInt32(il, p); p += 4;
                    targets[i] = "IL_" + (baseOffset + delta).ToString("X4", CultureInfo.InvariantCulture);
                }
                return string.Join(",", targets);
            }
            case OperandType.InlineString:
            {
                int token = BitConverter.ToInt32(il, p); p += 4;
                try { return "string:" + Safe(module.ResolveString(token)); }
                catch { return "string_token:0x" + token.ToString("X8", CultureInfo.InvariantCulture); }
            }
            case OperandType.InlineField:
            {
                int token = BitConverter.ToInt32(il, p); p += 4;
                try
                {
                    FieldInfo field = module.ResolveField(token);
                    return TypeName(field.DeclaringType) + "::" + TypeName(field.FieldType) + " " + field.Name;
                }
                catch
                {
                    return "field_token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);
                }
            }
            case OperandType.InlineMethod:
            {
                int token = BitConverter.ToInt32(il, p); p += 4;
                try
                {
                    MethodBase target = module.ResolveMethod(token);
                    if (target != null) references.Add(target);
                    return target == null ? "method_token:0x" + token.ToString("X8", CultureInfo.InvariantCulture) :
                        TypeName(target.DeclaringType) + "::" + Signature(target);
                }
                catch
                {
                    return "method_token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);
                }
            }
            case OperandType.InlineType:
            case OperandType.InlineTok:
            {
                int token = BitConverter.ToInt32(il, p); p += 4;
                try
                {
                    MemberInfo member = module.ResolveMember(token);
                    return member == null ? "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture) :
                        TypeName(member.DeclaringType) + "::" + member.Name;
                }
                catch
                {
                    return "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);
                }
            }
            case OperandType.InlineSig:
            {
                int token = BitConverter.ToInt32(il, p); p += 4;
                return "sig_token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);
            }
            default:
                throw new NotSupportedException("operand type " + op.OperandType);
        }
    }

    static string Signature(MethodBase method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string[] parts = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            parts[i] = TypeName(parameters[i].ParameterType);

        MethodInfo info = method as MethodInfo;
        string returnType = info == null ? "void" : TypeName(info.ReturnType);
        return returnType + " " + method.Name + "(" + string.Join(",", parts) + ")";
    }

    static string TypeName(Type type)
    {
        if (type == null) return "<null>";
        return type.FullName ?? type.Name;
    }

    static string SafeModulePath(Module module)
    {
        try { return Path.GetFullPath(module.FullyQualifiedName); }
        catch { return ""; }
    }

    static string Safe(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Hex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    static string Sha256File(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
            return Hex(sha.ComputeHash(stream));
    }

    static string Sha256Bytes(byte[] bytes)
    {
        using (SHA256 sha = SHA256.Create())
            return Hex(sha.ComputeHash(bytes));
    }

    static Dictionary<ushort, OpCode> BuildCodes()
    {
        Dictionary<ushort, OpCode> result = new Dictionary<ushort, OpCode>();
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].FieldType != typeof(OpCode)) continue;
            OpCode op = (OpCode)fields[i].GetValue(null);
            unchecked { result[(ushort)op.Value] = op; }
        }
        return result;
    }
}
