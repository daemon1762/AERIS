using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;

internal static class AERIS41R041TerrainAltitudeDeepIlDump
{
    static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();

    static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("usage: AERIS41_R041_dump_terrainaltitude_deep_il.exe <KSP Managed directory>");
            return 2;
        }

        string managed = Path.GetFullPath(args[0]);
        string assemblyPath = Path.Combine(managed, "Assembly-CSharp.dll");
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine("FAIL: Assembly-CSharp.dll missing: " + assemblyPath);
            return 3;
        }

        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            try
            {
                string name = new AssemblyName(e.Name).Name + ".dll";
                string candidate = Path.Combine(managed, name);
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            }
            catch { return null; }
        };

        try
        {
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            Console.WriteLine("=== AERIS41 R041 TERRAINALTITUDE DEEP IL CLOSURE ===");
            Console.WriteLine("assembly=" + assemblyPath);
            Console.WriteLine("assembly_sha256=" + Sha256File(assemblyPath));
            Console.WriteLine("assembly_full_name=" + assembly.FullName);
            Console.WriteLine("module_mvid=" + assembly.ManifestModule.ModuleVersionId.ToString("D"));

            Type planetarium = assembly.GetType("Planetarium", true, false);
            Type pqs = assembly.GetType("PQS", true, false);
            Type vb = assembly.GetType("PQS+VertexBuildData", true, false);

            int sphericalCount = DumpNamedMethods(planetarium, new string[] { "SphericalVector" });
            int modHeightCount = DumpNamedMethods(pqs, new string[] { "Mod_OnVertexBuildHeight" });
            int resetCount = DumpNamedMethods(vb, new string[] { "Reset" });

            int sphericalRequired = CountNamed(planetarium, "SphericalVector");
            int modHeightRequired = CountNamed(pqs, "Mod_OnVertexBuildHeight");
            int resetRequired = CountNamed(vb, "Reset");

            Console.WriteLine();
            Console.WriteLine("planetarium_dumped_methods=" + sphericalCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("pqs_dumped_methods=" + modHeightCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("vertexbuilddata_dumped_methods=" + resetCount.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_SphericalVector=" + sphericalRequired.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_Mod_OnVertexBuildHeight=" + modHeightRequired.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("required_VertexBuildData_Reset=" + resetRequired.ToString(CultureInfo.InvariantCulture));

            bool pass = sphericalRequired > 0 && modHeightRequired > 0 && resetRequired > 0;
            Console.WriteLine("AERIS41_R041_TERRAINALTITUDE_DEEP_IL_CLOSURE=" + (pass ? "PASS" : "FAIL"));
            return pass ? 0 : 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace ?? string.Empty);
            return 5;
        }
    }

    static int DumpNamedMethods(Type type, string[] names)
    {
        Console.WriteLine();
        Console.WriteLine("=== TYPE " + TypeName(type) + " ===");
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Array.Sort(methods, delegate(MethodInfo a, MethodInfo b)
        {
            int c = string.CompareOrdinal(a.Name, b.Name);
            return c != 0 ? c : a.MetadataToken.CompareTo(b.MetadataToken);
        });
        int emitted = 0;
        for (int i = 0; i < methods.Length; i++)
        {
            if (!Wanted(methods[i].Name, names)) continue;
            DumpMethod(methods[i]);
            emitted++;
        }
        return emitted;
    }

    static bool Wanted(string name, string[] names)
    {
        for (int i = 0; i < names.Length; i++)
            if (string.Equals(name, names[i], StringComparison.Ordinal)) return true;
        return false;
    }

    static int CountNamed(Type type, string name)
    {
        int count = 0;
        MethodInfo[] methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        for (int i = 0; i < methods.Length; i++)
            if (string.Equals(methods[i].Name, name, StringComparison.Ordinal)) count++;
        return count;
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

        ParameterInfo[] ps = method.GetParameters();
        Console.WriteLine("parameter_count=" + ps.Length.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < ps.Length; i++)
        {
            Console.WriteLine("PARAM " + i.ToString(CultureInfo.InvariantCulture) +
                " name=" + Safe(ps[i].Name) + " type=" + TypeName(ps[i].ParameterType) +
                " attrs=" + ps[i].Attributes + " optional=" + Bool(ps[i].IsOptional) +
                " has_default=" + Bool((ps[i].Attributes & ParameterAttributes.HasDefault) != 0));
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
            Console.WriteLine("LOCAL " + i.ToString(CultureInfo.InvariantCulture) + " " +
                TypeName(local.LocalType) + " pinned=" + Bool(local.IsPinned));
        }

        int p = 0;
        int index = 0;
        while (p < il.Length)
        {
            int at = p;
            ushort key = il[p++];
            if (key == 0xFE)
            {
                if (p >= il.Length) throw new InvalidOperationException("truncated two-byte opcode");
                key = (ushort)(0xFE00 | il[p++]);
            }
            OpCode op;
            if (!Codes.TryGetValue(key, out op))
                throw new InvalidOperationException("unknown opcode 0x" + key.ToString("X4", CultureInfo.InvariantCulture));
            string operand = ReadOperand(method, il, ref p, op);
            Console.WriteLine("IL " + index.ToString("D3", CultureInfo.InvariantCulture) +
                " IL_" + at.ToString("X4", CultureInfo.InvariantCulture) + " " + op.Name +
                (string.IsNullOrEmpty(operand) ? string.Empty : " " + operand));
            index++;
        }
    }

    static string ReadOperand(MethodInfo owner, byte[] il, ref int p, OpCode op)
    {
        Module module = owner.Module;
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
            case OperandType.ShortInlineBrTarget: { sbyte d = (sbyte)il[p++]; return "IL_" + (p + d).ToString("X4", CultureInfo.InvariantCulture); }
            case OperandType.InlineBrTarget: { int d = BitConverter.ToInt32(il, p); p += 4; return "IL_" + (p + d).ToString("X4", CultureInfo.InvariantCulture); }
            case OperandType.InlineSwitch:
            {
                int count = BitConverter.ToInt32(il, p); p += 4;
                int baseOffset = p + count * 4;
                string[] targets = new string[count];
                for (int i = 0; i < count; i++) { int d = BitConverter.ToInt32(il, p); p += 4; targets[i] = "IL_" + (baseOffset + d).ToString("X4", CultureInfo.InvariantCulture); }
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
            default: throw new NotSupportedException("operand type " + op.OperandType);
        }
    }

    static string ResolveToken(MethodInfo owner, int token, OperandType operandType)
    {
        try
        {
            Type[] typeArgs = owner.DeclaringType != null && owner.DeclaringType.IsGenericType ? owner.DeclaringType.GetGenericArguments() : null;
            Type[] methodArgs = owner.IsGenericMethod ? owner.GetGenericArguments() : null;
            Module module = owner.Module;
            MemberInfo member;
            if (operandType == OperandType.InlineField) member = module.ResolveField(token, typeArgs, methodArgs);
            else if (operandType == OperandType.InlineMethod) member = module.ResolveMethod(token, typeArgs, methodArgs);
            else if (operandType == OperandType.InlineType) member = module.ResolveType(token, typeArgs, methodArgs);
            else member = module.ResolveMember(token, typeArgs, methodArgs);
            if (member == null) return "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture);
            string ownerName = member.DeclaringType == null ? string.Empty : TypeName(member.DeclaringType) + "::";
            MethodBase mb = member as MethodBase;
            if (mb != null) return ownerName + Signature(mb);
            FieldInfo fi = member as FieldInfo;
            if (fi != null) return ownerName + TypeName(fi.FieldType) + " " + fi.Name;
            Type t = member as Type;
            if (t != null) return TypeName(t);
            return ownerName + member.Name;
        }
        catch { return "token:0x" + token.ToString("X8", CultureInfo.InvariantCulture); }
    }

    static string Signature(MethodBase method)
    {
        ParameterInfo[] ps = method.GetParameters();
        string[] parts = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++) parts[i] = TypeName(ps[i].ParameterType) + " " + Safe(ps[i].Name);
        MethodInfo mi = method as MethodInfo;
        return (mi == null ? "void" : TypeName(mi.ReturnType)) + " " + method.Name + "(" + string.Join(",", parts) + ")";
    }

    static string TypeName(Type type) { return type == null ? "<null>" : (type.FullName ?? type.Name); }
    static string Safe(string s) { return string.IsNullOrEmpty(s) ? string.Empty : s.Replace('\r', ' ').Replace('\n', ' ').Replace('|', '/'); }
    static string Bool(bool v) { return v ? "true" : "false"; }
    static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", string.Empty).ToLowerInvariant(); }
    static string Sha256File(string path) { using (FileStream s = File.OpenRead(path)) using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(s)); }
    static string Sha256Bytes(byte[] b) { using (SHA256 sha = SHA256.Create()) return Hex(sha.ComputeHash(b)); }

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
