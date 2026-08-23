#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
SOURCE = SRC / 'AERISR031PtcGillyAlgorithmCalibrationObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
PARENT = 'AERIS32_REV3_5_R031_PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW'
MARKER = 'AERIS32_REV3_5_R031_PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW'

OBSERVER = r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_GILLY_ALGORITHM_CALIBRATION_SHADOW
    // Gilly-only Main Thread calibration. Runtime math objects are observed/invoked
    // only on the Main Thread. No worker, DB, producer, GPU or certification authority.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcGillyAlgorithmCalibrationObserver : MonoBehaviour
    {
        static readonly double[,] CalibrationPoints = new double[,]
        {
            { 0.0, 0.0, 0.0 },
            { 0.1, 0.2, 0.3 },
            { -0.25, 0.5, -0.75 },
            { 1.0, 2.0, 3.0 },
            { 12.345, -67.89, 0.125 },
            { -3.141592653589793, 2.718281828459045, 0.577215664901533 }
        };

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
            if (gilly == null)
            {
                AERISLogger.Info("[R031][PTC_GILLY_ALGO] event=CALIBRATION_COMPLETE; body_found=false; mods=0; math_objects=0; numeric3_methods=0; calls=0; failures=0; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
                return;
            }

            object pqs = ReadMember(gilly, "pqsController");
            IEnumerable mods = pqs == null ? null : ReadMember(pqs, "mods") as IEnumerable;
            int modCount = 0, mathObjects = 0, numeric3Methods = 0, calls = 0, failures = 0;
            if (mods != null)
            {
                foreach (object mod in mods)
                {
                    if (mod == null || !ReadBool(mod, "enabled", true)) continue;
                    string type = mod.GetType().FullName ?? mod.GetType().Name;
                    if (!IsGillyHeightContributor(type)) continue;
                    modCount++;
                    AERISLogger.Info("[R031][PTC_GILLY_MOD] body=Gilly; type=" + Safe(type) +
                        "; order=" + ReadInt(mod, "order", -1) +
                        "; instance_fields=" + Safe(DescribeFields(mod, false, 64)) +
                        "; static_fields=" + Safe(DescribeFields(mod, true, 64)) +
                        "; methods=" + Safe(DescribeMethods(mod.GetType(), 32)) +
                        "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; authority=PQS");

                    FieldInfo[] fields = mod.GetType().GetFields(BindingFlags.Instance |
                        BindingFlags.Public | BindingFlags.NonPublic);
                    for (int f = 0; f < fields.Length; f++)
                    {
                        FieldInfo field = fields[f];
                        if (field == null || field.IsStatic) continue;
                        object value;
                        try { value = field.GetValue(mod); } catch { continue; }
                        if (!InterestingMathObject(value, field.FieldType)) continue;
                        mathObjects++;
                        Type mathType = value == null ? field.FieldType : value.GetType();
                        string methods = DescribeMethods(mathType, 64);
                        int localMethods, localCalls, localFailures;
                        string calibration = Calibrate(value, mathType, out localMethods,
                            out localCalls, out localFailures);
                        numeric3Methods += localMethods;
                        calls += localCalls;
                        failures += localFailures;
                        AERISLogger.Info("[R031][PTC_GILLY_MATH] body=Gilly; owner=" +
                            Safe(type) + "; member=" + Safe(field.Name) +
                            "; runtime_type=" + Safe(mathType.FullName ?? mathType.Name) +
                            "; unity_backed=" + (value is UnityEngine.Object) +
                            "; instance_fields=" + Safe(DescribeFields(value, false, 128)) +
                            "; static_fields=" + Safe(DescribeFields(value, true, 128)) +
                            "; methods=" + Safe(methods) +
                            "; calibration=" + Safe(calibration) +
                            "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; authority=PQS");
                    }
                }
            }

            AERISLogger.Info("[R031][PTC_GILLY_ALGO] event=CALIBRATION_COMPLETE; body_found=true; mods=" + modCount +
                "; math_objects=" + mathObjects +
                "; numeric3_methods=" + numeric3Methods +
                "; calls=" + calls +
                "; failures=" + failures +
                "; runtime_object_invocation_thread=MAIN_THREAD_ONLY; worker_invokes_runtime_object=false; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static bool IsGillyHeightContributor(string type)
        {
            return string.Equals(type, "PQSMod_VertexSimplexHeightAbsolute",
                StringComparison.Ordinal) || string.Equals(type, "PQSMod_VertexHeightNoise",
                StringComparison.Ordinal);
        }

        static bool InterestingMathObject(object value, Type declared)
        {
            Type type = value == null ? declared : value.GetType();
            if (type == null) return false;
            string name = (type.FullName ?? type.Name).ToLowerInvariant();
            return name.Contains("simplex") || name.Contains("libnoise") ||
                name.Contains("ridged") || name.Contains("perlin") ||
                name.Contains("billow") || name.Contains("noisequality");
        }

        static string Calibrate(object target, Type type, out int methodCount,
            out int callCount, out int failureCount)
        {
            methodCount = 0; callCount = 0; failureCount = 0;
            if (target == null || type == null) return string.Empty;
            var result = new List<string>();
            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic); }
            catch { return string.Empty; }
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.ContainsGenericParameters ||
                    !Numeric(method.ReturnType)) continue;
                ParameterInfo[] p = method.GetParameters();
                if (p.Length != 3 || !Floating(p[0].ParameterType) ||
                    !Floating(p[1].ParameterType) || !Floating(p[2].ParameterType)) continue;
                methodCount++;
                var values = new List<string>();
                for (int c = 0; c < CalibrationPoints.GetLength(0); c++)
                {
                    object[] args = new object[3];
                    args[0] = Coerce(CalibrationPoints[c,0], p[0].ParameterType);
                    args[1] = Coerce(CalibrationPoints[c,1], p[1].ParameterType);
                    args[2] = Coerce(CalibrationPoints[c,2], p[2].ParameterType);
                    try
                    {
                        object raw = method.Invoke(target, args);
                        double value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                        values.Add(c.ToString(CultureInfo.InvariantCulture) + "=" +
                            value.ToString("R", CultureInfo.InvariantCulture));
                        callCount++;
                    }
                    catch
                    {
                        values.Add(c.ToString(CultureInfo.InvariantCulture) + "=ERR");
                        failureCount++;
                    }
                }
                result.Add(method.Name + "(" + string.Join(",", values.ToArray()) + ")");
            }
            return string.Join("/", result.ToArray());
        }

        static string DescribeMethods(Type type, int maximum)
        {
            if (type == null) return string.Empty;
            var result = new List<string>();
            MethodInfo[] methods;
            try { methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Static |
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly); }
            catch { return string.Empty; }
            Array.Sort(methods, (a,b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < methods.Length && result.Count < maximum; i++)
            {
                MethodInfo m = methods[i];
                if (m == null) continue;
                ParameterInfo[] p = m.GetParameters();
                var ps = new string[p.Length];
                for (int j = 0; j < p.Length; j++) ps[j] = p[j].ParameterType.FullName;
                result.Add((m.IsStatic ? "static:" : "instance:") + m.Name + "(" +
                    string.Join(",", ps) + ")->" + m.ReturnType.FullName);
            }
            return string.Join("/", result.ToArray());
        }

        static string DescribeFields(object target, bool statics, int maximumArrayValues)
        {
            if (target == null) return string.Empty;
            Type type = target is Type ? (Type)target : target.GetType();
            var result = new List<string>();
            BindingFlags flags = (statics ? BindingFlags.Static : BindingFlags.Instance) |
                BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo[] fields;
            try { fields = type.GetFields(flags); } catch { return string.Empty; }
            Array.Sort(fields, (a,b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < fields.Length && result.Count < 96; i++)
            {
                FieldInfo f = fields[i];
                if (f == null || f.IsStatic != statics) continue;
                object owner = statics ? null : target;
                object value;
                try { value = f.GetValue(owner); } catch { continue; }
                if (Primitive(f.FieldType))
                {
                    result.Add(f.Name + "=" + Convert.ToString(value,
                        CultureInfo.InvariantCulture));
                    continue;
                }
                if (f.FieldType != null && f.FieldType.IsArray &&
                    Primitive(f.FieldType.GetElementType()))
                {
                    Array array = value as Array;
                    int count = array == null ? 0 : array.Length;
                    int take = Math.Min(count, maximumArrayValues);
                    var values = new string[take];
                    for (int j = 0; j < take; j++)
                        values[j] = Convert.ToString(array.GetValue(j),
                            CultureInfo.InvariantCulture);
                    result.Add(f.Name + "[" + count + "]=" + string.Join(",", values) +
                        (count > take ? ",..." : string.Empty));
                }
            }
            return string.Join("/", result.ToArray());
        }

        static bool Primitive(Type type)
        {
            return type != null && (type.IsPrimitive || type.IsEnum ||
                type == typeof(string) || type == typeof(decimal));
        }
        static bool Numeric(Type type)
        {
            return type == typeof(double) || type == typeof(float) ||
                type == typeof(decimal) || type == typeof(int) || type == typeof(long);
        }
        static bool Floating(Type type)
        {
            return type == typeof(double) || type == typeof(float);
        }
        static object Coerce(double value, Type type)
        {
            return type == typeof(float) ? (object)(float)value : value;
        }
        static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name)) return null;
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.CanRead && property.GetIndexParameters().Length == 0 ?
                property.GetValue(target, null) : null;
        }
        static bool ReadBool(object target, string name, bool fallback)
        {
            object value = null; try { value = ReadMember(target, name); } catch { }
            return value is bool ? (bool)value : fallback;
        }
        static int ReadInt(object target, string name, int fallback)
        {
            object value = null; try { value = ReadMember(target, name); } catch { }
            try { return value == null ? fallback : Convert.ToInt32(value,
                CultureInfo.InvariantCulture); } catch { return fallback; }
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
    raise SystemExit('R031 Gilly calibration observer exists with unexpected content')
SOURCE.write_text(OBSERVER)

csproj = CSPROJ.read_text()
include = '    <Compile Include="Terrain\\AERISR031PtcGillyAlgorithmCalibrationObserver.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISR031PtcWorkerSnapshotFeasibilityObserver.cs" />\n'
if include not in csproj:
    if anchor not in csproj:
        raise SystemExit('R031 worker snapshot csproj anchor missing')
    csproj = csproj.replace(anchor, anchor + include, 1)
    CSPROJ.write_text(csproj)

version = VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R031 worker snapshot parent build identity missing')
version = version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC WORKER SNAPSHOT FEASIBILITY SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC GILLY ALGORITHM CALIBRATION SHADOW')
version = version.replace(PARENT, MARKER)

head = subprocess.check_output(['git','rev-parse','HEAD'], cwd=str(ROOT), text=True).strip()
h = hashlib.sha256()
files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs')) + [CSPROJ]
for path in files:
    if path == VERSION: continue
    h.update(str(path.relative_to(ROOT)).encode()); h.update(b'\0')
    h.update(path.read_bytes()); h.update(b'\0')
tree = h.hexdigest()
version = re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
    'internal const string SourceGitSha = "' + head + '";', version)
version = re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
    'internal const string SourceTreeSha256 = "' + tree + '";', version)
VERSION.write_text(version)

print('PASS: materialized R031 Gilly algorithm calibration shadow probe')
print('runtime_change=GILLY_MAIN_THREAD_ALGORITHM_CALIBRATION_ONLY')
print('worker_invokes_runtime_object=NO db_write=NO producer_switch=NO certification=NO authority=PQS')
print('source_git_sha=' + head)
print('source_tree_sha256=' + tree)
