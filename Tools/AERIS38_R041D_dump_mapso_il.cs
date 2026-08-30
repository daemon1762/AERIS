using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;

internal static class AERIS38R041DMapSoIlDump
{
    static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();

    static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("usage: AERIS38_R041D_dump_mapso_il.exe <KSP Managed directory>");
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
            Type type = assembly.GetType("MapSO", true, false);

            Console.WriteLine("=== AERIS38 R041D STOCK MAPSO MANAGED IL ===");
            Console.WriteLine("assembly=" + assemblyPath);
            Console.WriteLine("assembly_full_name=" + assembly.FullName);
            Console.WriteLine("type=" + type.FullName);

            MethodInfo[] methods = type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            Array.Sort(methods, delegate(MethodInfo a, MethodInfo b)
            {
                int c = string.CompareOrdinal(a.Name, b.Name);
                if (c != 0) return c;
                return a.MetadataToken.CompareTo(b.MetadataToken);
            });

            int emitted = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!Wanted(method.Name)) continue;
                DumpMethod(method);
                emitted++;
            }

            Console.WriteLine("method_count=" + emitted.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("AERIS38_R041D_MAPSO_IL_DUMP=PASS");
            return emitted > 0 ? 0 : 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            return 5;
        }
    }

    static bool Wanted(string name)
    {
        return name == "ConstructBilinearCoords" ||
               name == "PixelIndex" ||
               name == "GetPixelFloat" ||
               name == "GetPixelByte" ||
               name == "GreyByte" ||
               name == "GreyFloat";
    }

    static void DumpMethod(MethodInfo method)
    {
        Console.WriteLine();
        Console.WriteLine("--- METHOD ---");
        Console.WriteLine("signature=" + Signature(method));
        Console.WriteLine("metadata_token=0x" + method.MetadataToken.ToString("X8", CultureInfo.InvariantCulture));

        MethodBody body = method.GetMethodBody();
        if (body == null)
        {
            Console.WriteLine("managed_body=false");
            return;
        }

        byte[] il = body.GetILAsByteArray();
        if (il == null)
        {
            Console.WriteLine("managed_body=true");
            Console.WriteLine("il_bytes=0");
            return;
        }

        Console.WriteLine("managed_body=true");
        Console.WriteLine("il_bytes=" + il.Length.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("max_stack=" + body.MaxStackSize.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("locals=" + body.LocalVariables.Count.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < body.LocalVariables.Count; i++)
        {
            LocalVariableInfo local = body.LocalVariables[i];
            Console.WriteLine(
                "LOCAL " + i.ToString(CultureInfo.InvariantCulture) +
                " " + TypeName(local.LocalType) +
                " pinned=" + local.IsPinned);
        }

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

            string operand = ReadOperand(method.Module, il, ref p, op);
            Console.WriteLine(
                "IL " + index.ToString("D3", CultureInfo.InvariantCulture) +
                " IL_" + offset.ToString("X4", CultureInfo.InvariantCulture) +
                " " + op.Name +
                (string.IsNullOrEmpty(operand) ? string.Empty : " " + operand));
            index++;
        }
    }

    static string ReadOperand(Module module, byte[] il, ref int p, OpCode op)
    {
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
                return ResolveToken(module, token, op.OperandType);
            }
            default:
                throw new NotSupportedException("operand type " + op.OperandType);
        }
    }

    static string ResolveToken(Module module, int token, OperandType operandType)
    {
        try
        {
            MemberInfo member;
            if (operandType == OperandType.InlineField)
                member = module.ResolveField(token);
            else if (operandType == OperandType.InlineMethod)
                member = module.ResolveMethod(token);
            else if (operandType == OperandType.InlineType)
                member = module.ResolveType(token);
            else
                member = module.ResolveMember(token);

            if (member == null)
                return "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);

            string owner = member.DeclaringType == null ? string.Empty : TypeName(member.DeclaringType) + "::";
            MethodBase method = member as MethodBase;
            if (method != null)
                return owner + Signature(method);
            FieldInfo field = member as FieldInfo;
            if (field != null)
                return owner + TypeName(field.FieldType) + " " + field.Name;
            Type type = member as Type;
            if (type != null)
                return TypeName(type);
            return owner + member.Name;
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

    static string Safe(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/');
    }

    static Dictionary<ushort, OpCode> BuildCodes()
    {
        var result = new Dictionary<ushort, OpCode>();
        FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i].FieldType != typeof(OpCode)) continue;
            OpCode op = (OpCode)fields[i].GetValue(null);
            unchecked
            {
                result[(ushort)op.Value] = op;
            }
        }
        return result;
    }
}
