using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

internal static class AERIS41R041TerrainAltitudeIlDump
{
    static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();

    static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("usage: AERIS41_R041_dump_terrainaltitude_il.exe <KSP Managed directory>");
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
            Console.WriteLine("=== AERIS41 R041 TERRAINALTITUDE IL CLOSURE ===");
            Console.WriteLine("assembly=" + assemblyPath);
            Console.WriteLine("assembly_sha256=" + Sha256File(assemblyPath));
            Console.WriteLine("assembly_full_name=" + assembly.FullName);
            Console.WriteLine("module_mvid=" + assembly.ManifestModule.ModuleVersionId.ToString("D"));

            int terrainAltitudeCount = DumpNamedMethods(
                assembly.GetType("CelestialBody", true, false),
                new string[] {
                    "TerrainAltitude",
                    "GetRelSurfaceNVector",
                    "GetLatitude",
                    "GetLongitude"
                });

            int pqsCount = DumpNamedMethods(
                assembly.GetType("PQS", true, false),
                new string[] {
                    "GetSurfaceHeight",
                    "BuildVertexMapCoords",
                    "BuildVertex",
                    "BuildVertexHeight"
                });

            Type celestial = assembly.GetType("CelestialBody", true, false);
            Type pqs = assembly.GetType("PQS", true, false);
            int terrainRequired = CountNamed(celestial, "TerrainAltitude");
            int directionRequired = CountNamed(celestial, "GetRelSurfaceNVector");
            int surfaceRequired = CountNamed(pqs, "GetSurfaceHeight");
            int mapCoordsRequired = CountNamed(pqs, "BuildVertexMapCoords");

            Console.WriteLine();
            Console.WriteLine("celestial_dumped_methods=" + terrainAltitudeCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("pqs_dumped_methods=" + pqsCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_TerrainAltitude=" + terrainRequired.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_GetRelSurfaceNVector=" + directionRequired.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_GetSurfaceHeight=" + surfaceRequired.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_BuildVertexMapCoords=" + mapCoordsRequired.ToString(CultureInfo.InvariantCulture));

            bool pass = terrainRequired > 0 && directionRequired > 0 &&
                surfaceRequired > 0 && mapCoordsRequired > 0;
            Console.WriteLine("AERIS41_R041_TERRAINALTITUDE_IL_CLOSURE=" + (pass ? "PASS" : "FAIL"));
            return pass ? 0 : 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace ?? string.Empty);
            return 5;
        }
    }

    static int DumpNamedMethods(Type type, string[] wantedNames)
    {
        Console.WriteLine();
        Console.WriteLine("=== TYPE " + TypeName(type) + " ===");
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        Array.Sort(methods, delegate(MethodInfo a, MethodInfo b)
        {
            int c = string.CompareOrdinal(a.Name, b.Name);
            if (c != 0) return c;
            return a.MetadataToken.CompareTo(b.MetadataToken);
        });

        int emitted = 0;
        for (int i = 0; i < methods.Length; i++)
        {
            if (!Wanted(methods[i].Name, wantedNames)) continue;
            DumpMethod(methods[i]);
            emitted++;
        }
        return emitted;
    }

    static int CountNamed(Type type, string name)
    {
        int count = 0;
        MethodInfo[] methods = type.GetMethods(
            BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly);
        for (int i = 0; i < methods.Length; i++)
            if (string.Equals(methods[i].Name, name, StringComparison.Ordinal)) count++;
        return count;
    }

    static bool Wanted(string name, string[] wantedNames)
    {
        for (int i = 0; i < wantedNames.Length; i++)
            if (string.Equals(name, wantedNames[i], StringComparison.Ordinal)) return true;
        return false;
    }

    static void DumpMethod(MethodInfo method)
    {
        Console.WriteLine();
        Console.WriteLine("--- METHOD ---");
        Console.WriteLine("signature=" + Signature(method));
        Console.WriteLine("declaring_type=" + TypeName(method.DeclaringType));
        Console.WriteLine("metadata_token=0x" + method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture));
        Console.WriteLine("attributes=" + method.Attributes);
        Console.WriteLine("impl_flags=" + method.GetMethodImplementationFlags());

        ParameterInfo[] parameters = method.GetParameters();
        Console.WriteLine("parameter_count=" + parameters.Length.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo p = parameters[i];
            Console.WriteLine(
                "PARAM " + i.ToString(CultureInfo.InvariantCulture) +
                " name=" + Safe(p.Name) +
                " type=" + TypeName(p.ParameterType) +
                " attrs=" + p.Attributes +
                " optional=" + Bool(p.IsOptional) +
                " has_default=" + Bool((p.Attributes & ParameterAttributes.HasDefault) != 0));
        }

        MethodBody body = method.GetMethodBody();
        if (body == null)
        {
            Console.WriteLine("managed_body=false");
            return;
        }

        byte[] il = body.GetILAsByteArray() ?? new byte[0];
        Console.WriteLine("managed_body=true");
        Console.WriteLine("il_bytes=" + il.Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("il_hex=" + Hex(il));
        Console.WriteLine("il_sha256=" + Sha256Bytes(il));
        Console.WriteLine("max_stack=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("init_locals=" + Bool(body.InitLocals));
        Console.WriteLine("locals=" + body.LocalVariables.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < body.LocalVariables.Count; i++)
        {
            LocalVariableInfo local = body.LocalVariables[i];
            Console.WriteLine(
                "LOCAL " + i.ToString(CultureInfo.InvariantCulture) +
                " " + TypeName(local.LocalType) +
                " pinned=" + Bool(local.IsPinned));
        }

        int offset = 0;
        int index = 0;
        while (offset < il.Length)
        {
            int instructionOffset = offset;
            ushort key = il[offset++];
            if (key == 0xFE)
            {
                if (offset >= il.Length)
                    throw new InvalidOperationException("truncated two-byte opcode");
                key = (ushort)(0xFE00 | il[offset++]);
            }

            OpCode op;
            if (!Codes.TryGetValue(key, out op))
                throw new InvalidOperationException(
                    "unknown opcode 0x" + key.ToString("X4", CultureInfo.InvariantCulture));

            string operand = ReadOperand(method, il, ref offset, op);
            Console.WriteLine(
                "IL " + index.ToString("D3", CultureInfo.InvariantCulture) +
                " IL_" + instructionOffset.ToString("X4", CultureInfo.InvariantCulture) +
                " " + op.Name +
                (string.IsNullOrEmpty(operand) ? string.Empty : " " + operand));
            index++;
        }
    }

    static string ReadOperand(MethodInfo owner, byte[] il, ref int p, OpCode op)
    {
        Module module = owner.Module;
        switch (op.OperandType)
        {
            case OperandType.InlineNone:
                return string.Empty;
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
            case OperandType.InlineMethod:
            case OperandType.InlineType:
            case OperandType.InlineTok:
            case OperandType.InlineSig:
            {
                int token = BitConverter.ToInt32(il, p); p += 4;
                return ResolveToken(owner, token, op.OperandType);
            }
            default:
                throw new NotSupportedException("operand type " + op.OperandType);
        }
    }

    static string ResolveToken(MethodInfo owner, int token, OperandType operandType)
    {
        try
        {
            Type[] typeArgs = owner.DeclaringType != null && owner.DeclaringType.IsGenericType
                ? owner.DeclaringType.GetGenericArguments() : null;
            Type[] methodArgs = owner.IsGenericMethod ? owner.GetGenericArguments() : null;
            Module module = owner.Module;
            MemberInfo member;
            if (operandType == OperandType.InlineField)
                member = module.ResolveField(token, typeArgs, methodArgs);
            else if (operandType == OperandType.InlineMethod)
                member = module.ResolveMethod(token, typeArgs, methodArgs);
            else if (operandType == OperandType.InlineType)
                member = module.ResolveType(token, typeArgs, methodArgs);
            else
                member = module.ResolveMember(token, typeArgs, methodArgs);

            if (member == null)
                return "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);

            string ownerName = member.DeclaringType == null
                ? string.Empty : TypeName(member.DeclaringType) + "::";
            MethodBase method = member as MethodBase;
            if (method != null) return ownerName + Signature(method);
            FieldInfo field = member as FieldInfo;
            if (field != null)
                return ownerName + TypeName(field.FieldType) + " " + field.Name;
            Type type = member as Type;
            if (type != null) return TypeName(type);
            return ownerName + member.Name;
        }
        catch
        {
            return "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);
        }
    }

    static string Signature(MethodBase method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string[] parts = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
            parts[i] = TypeName(parameters[i].ParameterType) + " " + Safe(parameters[i].Name);

        MethodInfo info = method as MethodInfo;
        string returnType = info == null ? "void" : TypeName(info.ReturnType);
        return returnType + " " + method.Name + "(" + string.Join(",", parts) + ")";
    }

    static string TypeName(Type type)
    {
        if (type == null) return "<null>";
        return type.FullName ?? type.Name;
    }

    static string Safe(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Hex(byte[] bytes)
    {
        return BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
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
        var result = new Dictionary<ushort, OpCode>();
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
