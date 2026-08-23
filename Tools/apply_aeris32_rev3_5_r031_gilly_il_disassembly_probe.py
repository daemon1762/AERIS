#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
SOURCE = SRC / 'AERISR031PtcGillyIlDisassemblyObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT = 'AERIS32_REV3_5_R031_PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW'
MARKER = 'AERIS32_REV3_5_R031_PTC_GILLY_IL_DISASSEMBLY_SHADOW'

OBSERVER = r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_GILLY_IL_DISASSEMBLY_SHADOW
    // Main-thread-only IL observation. No runtime object is invoked on a worker.
    // No DB mutation, producer switch, GPU path, or certification authority.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcGillyIlDisassemblyObserver : MonoBehaviour
    {
        static readonly Dictionary<ushort, OpCode> Codes = BuildCodes();
        float nextAttempt;
        bool captured;

        void Update()
        {
            if (captured || Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (!AERISTerrainTileSystem.GameDataHashReady || FlightGlobals.Bodies == null ||
                FlightGlobals.Bodies.Count == 0) return;
            Capture();
            captured = true;
        }

        void Capture()
        {
            CelestialBody gilly = null;
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body != null && string.Equals(body.name, "Gilly",
                    StringComparison.OrdinalIgnoreCase)) { gilly = body; break; }
            }
            int types = 0, methods = 0, withBody = 0, instructions = 0, failures = 0;
            if (gilly != null)
            {
                object pqs = ReadMember(gilly, "pqsController");
                IEnumerable mods = pqs == null ? null : ReadMember(pqs, "mods") as IEnumerable;
                if (mods != null)
                {
                    foreach (object mod in mods)
                    {
                        if (mod == null || !ReadBool(mod, "enabled", true)) continue;
                        string typeName = mod.GetType().FullName ?? mod.GetType().Name;
                        if (!IsGillyContributor(typeName)) continue;
                        DumpType(mod.GetType(), typeName, ref types, ref methods,
                            ref withBody, ref instructions, ref failures);

                        FieldInfo[] fields = mod.GetType().GetFields(BindingFlags.Instance |
                            BindingFlags.Public | BindingFlags.NonPublic);
                        for (int f = 0; f < fields.Length; f++)
                        {
                            FieldInfo field = fields[f];
                            if (field == null || field.IsStatic) continue;
                            object value = null;
                            try { value = field.GetValue(mod); } catch { }
                            Type rt = value == null ? field.FieldType : value.GetType();
                            if (!InterestingMathType(rt)) continue;
                            DumpType(rt, typeName + "." + field.Name, ref types, ref methods,
                                ref withBody, ref instructions, ref failures);
                        }
                    }
                }
            }
            AERISLogger.Info("[R031][PTC_GILLY_IL] event=DISASSEMBLY_COMPLETE; body_found=" +
                (gilly != null) + "; types=" + types + "; methods=" + methods +
                "; methods_with_body=" + withBody + "; instructions=" + instructions +
                "; failures=" + failures +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                "; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY" +
                "; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static void DumpType(Type type, string owner, ref int types, ref int methods,
            ref int withBody, ref int instructions, ref int failures)
        {
            if (type == null) return;
            types++;
            MethodInfo[] declared;
            try { declared = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
            catch { failures++; return; }
            Array.Sort(declared, (a,b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < declared.Length; i++)
            {
                MethodInfo method = declared[i];
                if (method == null || !Wanted(method)) continue;
                methods++;
                try
                {
                    MethodBody body = method.GetMethodBody();
                    byte[] il = body == null ? null : body.GetILAsByteArray();
                    AERISLogger.Info("[R031][PTC_GILLY_IL_METHOD] owner=" + Safe(owner) +
                        "; runtime_type=" + Safe(type.FullName ?? type.Name) +
                        "; method=" + Safe(Signature(method)) +
                        "; metadata_token=0x" + method.MetadataToken.ToString("X8",
                            CultureInfo.InvariantCulture) +
                        "; body_present=" + (il != null) +
                        "; il_bytes=" + (il == null ? 0 : il.Length) +
                        "; max_stack=" + (body == null ? -1 : body.MaxStackSize) +
                        "; local_count=" + (body == null ? -1 : body.LocalVariables.Count) +
                        "; exception_clauses=" + (body == null ? -1 : body.ExceptionHandlingClauses.Count) +
                        "; runtime_object_invocation_thread=MAIN_THREAD_ONLY" +
                        "; worker_invokes_runtime_object=false; authority=PQS");
                    if (il == null) continue;
                    withBody++;
                    List<string> decoded = Decode(method, il, ref failures);
                    instructions += decoded.Count;
                    const int PerChunk = 18;
                    int chunk = 0;
                    for (int p = 0; p < decoded.Count; p += PerChunk)
                    {
                        int take = Math.Min(PerChunk, decoded.Count - p);
                        string[] part = new string[take];
                        for (int j = 0; j < take; j++) part[j] = decoded[p+j];
                        AERISLogger.Info("[R031][PTC_GILLY_IL_CHUNK] owner=" + Safe(owner) +
                            "; method=" + Safe(method.Name) + "; chunk=" + chunk++ +
                            "; instructions=" + Safe(string.Join("/", part)) +
                            "; worker_invokes_runtime_object=false; authority=PQS");
                    }
                }
                catch (Exception ex)
                {
                    failures++;
                    AERISLogger.Info("[R031][PTC_GILLY_IL_FAIL] owner=" + Safe(owner) +
                        "; method=" + Safe(method.Name) + "; error=" + Safe(ex.GetType().Name) +
                        "; worker_invokes_runtime_object=false; authority=PQS");
                }
            }
        }

        static bool Wanted(MethodInfo m)
        {
            string n = m.Name;
            return n == "OnSetup" || n == "OnVertexBuildHeight" ||
                n == "value" || n == "noise" || n == "noiseNormalized" ||
                n == "set_seed" || n == "SetupPermTable" || n == "fastfloor" ||
                n == "dot" || n == "GetValue" || n == "CalculateSpectralWeights" ||
                n.StartsWith("get_", StringComparison.Ordinal) ||
                n.StartsWith("set_", StringComparison.Ordinal);
        }

        static List<string> Decode(MethodInfo method, byte[] il, ref int failures)
        {
            var result = new List<string>();
            int p = 0;
            while (p < il.Length)
            {
                int offset = p;
                ushort key = il[p++];
                if (key == 0xFE && p < il.Length) key = (ushort)(0xFE00 | il[p++]);
                OpCode op;
                if (!Codes.TryGetValue(key, out op))
                {
                    result.Add(offset.ToString("X4") + ":UNKNOWN_" + key.ToString("X4"));
                    failures++; break;
                }
                string operand = string.Empty;
                try { operand = ReadOperand(method, il, ref p, op, offset); }
                catch (Exception ex)
                {
                    failures++;
                    operand = "ERR:" + ex.GetType().Name;
                }
                result.Add(offset.ToString("X4") + ":" + op.Name +
                    (string.IsNullOrEmpty(operand) ? string.Empty : " " + operand));
            }
            return result;
        }

        static string ReadOperand(MethodInfo method, byte[] il, ref int p, OpCode op,
            int offset)
        {
            switch (op.OperandType)
            {
                case OperandType.InlineNone: return string.Empty;
                case OperandType.ShortInlineI: return ((sbyte)il[p++]).ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineI:
                    { int v = BitConverter.ToInt32(il,p); p+=4; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.InlineI8:
                    { long v = BitConverter.ToInt64(il,p); p+=8; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineR:
                    { float v = BitConverter.ToSingle(il,p); p+=4; return v.ToString("R",CultureInfo.InvariantCulture); }
                case OperandType.InlineR:
                    { double v = BitConverter.ToDouble(il,p); p+=8; return v.ToString("R",CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineVar: return il[p++].ToString(CultureInfo.InvariantCulture);
                case OperandType.InlineVar:
                    { ushort v = BitConverter.ToUInt16(il,p); p+=2; return v.ToString(CultureInfo.InvariantCulture); }
                case OperandType.ShortInlineBrTarget:
                    { sbyte d=(sbyte)il[p++]; return "IL_"+(p+d).ToString("X4"); }
                case OperandType.InlineBrTarget:
                    { int d=BitConverter.ToInt32(il,p); p+=4; return "IL_"+(p+d).ToString("X4"); }
                case OperandType.InlineSwitch:
                    {
                        int count=BitConverter.ToInt32(il,p); p+=4;
                        int baseOffset=p+4*count;
                        var targets=new string[count];
                        for(int i=0;i<count;i++) { int d=BitConverter.ToInt32(il,p); p+=4; targets[i]="IL_"+(baseOffset+d).ToString("X4"); }
                        return string.Join(",",targets);
                    }
                case OperandType.InlineString:
                    { int token=BitConverter.ToInt32(il,p); p+=4; return Token(method,token,true); }
                case OperandType.InlineField:
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                case OperandType.InlineSig:
                    { int token=BitConverter.ToInt32(il,p); p+=4; return Token(method,token,false); }
                default: return "operand_type=" + op.OperandType;
            }
        }

        static string Token(MethodInfo method, int token, bool isString)
        {
            string prefix = "0x" + token.ToString("X8", CultureInfo.InvariantCulture);
            try
            {
                if (isString) return prefix + "=" + Safe(method.Module.ResolveString(token));
                Type[] ta = method.DeclaringType != null && method.DeclaringType.IsGenericType ?
                    method.DeclaringType.GetGenericArguments() : Type.EmptyTypes;
                Type[] ma = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;
                MemberInfo m = method.Module.ResolveMember(token, ta, ma);
                return prefix + "=" + Safe(MemberName(m));
            }
            catch { return prefix + "=UNRESOLVED"; }
        }

        static string MemberName(MemberInfo m)
        {
            if (m == null) return "null";
            Type dt = m.DeclaringType;
            return (dt == null ? string.Empty : (dt.FullName ?? dt.Name) + ".") + m.Name;
        }

        static string Signature(MethodInfo m)
        {
            ParameterInfo[] p = m.GetParameters();
            var args = new string[p.Length];
            for (int i=0;i<p.Length;i++) args[i]=p[i].ParameterType.FullName;
            return m.Name + "(" + string.Join(",",args) + ")->" + m.ReturnType.FullName;
        }

        static Dictionary<ushort, OpCode> BuildCodes()
        {
            var d = new Dictionary<ushort, OpCode>();
            FieldInfo[] fields = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static);
            for (int i=0;i<fields.Length;i++)
            {
                if (fields[i].FieldType != typeof(OpCode)) continue;
                OpCode op=(OpCode)fields[i].GetValue(null);
                d[unchecked((ushort)op.Value)] = op;
            }
            return d;
        }

        static bool IsGillyContributor(string type)
        {
            return string.Equals(type,"PQSMod_VertexSimplexHeightAbsolute",StringComparison.Ordinal) ||
                string.Equals(type,"PQSMod_VertexHeightNoise",StringComparison.Ordinal);
        }
        static bool InterestingMathType(Type type)
        {
            if (type == null) return false;
            string n=(type.FullName ?? type.Name).ToLowerInvariant();
            return n.Contains("simplex") || n.Contains("ridged") || n.Contains("libnoise");
        }
        static object ReadMember(object target,string name)
        {
            if(target==null) return null;
            Type t=target.GetType();
            FieldInfo f=t.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(f!=null) return f.GetValue(target);
            PropertyInfo p=t.GetProperty(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            return p!=null && p.CanRead && p.GetIndexParameters().Length==0 ? p.GetValue(target,null) : null;
        }
        static bool ReadBool(object target,string name,bool fallback)
        {
            object v=null; try{v=ReadMember(target,name);}catch{}
            return v is bool ? (bool)v : fallback;
        }
        static string Safe(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty :
                value.Replace('\n',' ').Replace('\r',' ').Replace(';',',').Replace('|','/');
        }
    }
}
'''

SOURCE.parent.mkdir(parents=True, exist_ok=True)
if SOURCE.exists() and SOURCE.read_text() != OBSERVER:
    raise SystemExit('R031 Gilly IL observer exists with unexpected content')
SOURCE.write_text(OBSERVER)
csproj=CSPROJ.read_text()
include='    <Compile Include="Terrain\\AERISR031PtcGillyIlDisassemblyObserver.cs" />\n'
anchor='    <Compile Include="Terrain\\AERISR031PtcGillyAlgorithmCalibrationObserver.cs" />\n'
if include not in csproj:
    if anchor not in csproj: raise SystemExit('R031 Gilly calibration csproj anchor missing')
    csproj=csproj.replace(anchor,anchor+include,1); CSPROJ.write_text(csproj)
version=VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R031 Gilly calibration parent identity missing')
version=version.replace('AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC GILLY ALGORITHM CALIBRATION SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC GILLY IL DISASSEMBLY SHADOW')
version=version.replace(PARENT,MARKER)
head=subprocess.check_output(['git','rev-parse','HEAD'],cwd=str(ROOT),text=True).strip()
h=hashlib.sha256(); files=sorted((ROOT/'Source/AERISFlightControl').rglob('*.cs'))+[CSPROJ]
for path in files:
    if path==VERSION: continue
    h.update(str(path.relative_to(ROOT)).encode()); h.update(b'\0'); h.update(path.read_bytes()); h.update(b'\0')
tree=h.hexdigest()
version=re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";', 'internal const string SourceGitSha = "'+head+'";',version)
version=re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";', 'internal const string SourceTreeSha256 = "'+tree+'";',version)
VERSION.write_text(version)
print('PASS: materialized R031 Gilly IL disassembly shadow probe')
print('runtime_change=OBSERVATION_ONLY')
print('runtime_object_invocation_thread=MAIN_THREAD_ONLY worker_invokes_runtime_object=NO')
print('db_write=NO producer_switch=NO gpu=NO certification=NO authority=PQS')