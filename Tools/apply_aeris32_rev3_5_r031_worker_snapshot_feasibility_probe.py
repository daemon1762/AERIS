#!/usr/bin/env python3
from pathlib import Path
import hashlib
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
SOURCE = SRC / 'AERISR031PtcWorkerSnapshotFeasibilityObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER = 'AERIS32_REV3_5_R031_PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW'
PARENT = 'AERIS32_REV3_5_R031_PTC_CPU_RECONSTRUCTION_FEASIBILITY_SHADOW'

OBSERVER = r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_WORKER_SNAPSHOT_FEASIBILITY_SHADOW
    // Observation only. Main thread reflection inspects whether enough pure data exists
    // to build immutable worker snapshots. It never invokes PQS/Unity objects on workers.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcWorkerSnapshotFeasibilityObserver : MonoBehaviour
    {
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
            int bodies = 0, contributors = 0, managedMath = 0, mapSnapshot = 0,
                customAdapter = 0, unsupported = 0, primitiveArrays = 0,
                primitiveArrayBytes = 0, mapObjects = 0, pureBodies = 0;
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null || string.IsNullOrEmpty(body.name) ||
                    !AERISTerrainTileSystem.BodyHasSolidSurface(body)) continue;
                bodies++;
                object pqs = ReadMember(body, "pqsController");
                IEnumerable mods = pqs == null ? null : ReadMember(pqs, "mods") as IEnumerable;
                int bodyContributors = 0, bodyManaged = 0, bodyMap = 0,
                    bodyCustom = 0, bodyUnsupported = 0;
                if (mods != null)
                {
                    foreach (object mod in mods)
                    {
                        if (mod == null || !ReadBool(mod, "enabled", true)) continue;
                        string type = mod.GetType().FullName ?? mod.GetType().Name;
                        if (!LikelyHeight(type)) continue;
                        contributors++; bodyContributors++;
                        int arrays, bytes, maps;
                        string evidence;
                        string strategy = ClassifySnapshot(mod, type, out arrays,
                            out bytes, out maps, out evidence);
                        primitiveArrays += arrays;
                        primitiveArrayBytes += bytes;
                        mapObjects += maps;
                        if (strategy == "MANAGED_MATH_SNAPSHOT_CANDIDATE")
                        { managedMath++; bodyManaged++; }
                        else if (strategy == "MAPSO_MAIN_THREAD_COPY_CANDIDATE")
                        { mapSnapshot++; bodyMap++; }
                        else if (strategy == "CUSTOM_ADAPTER_REQUIRED")
                        { customAdapter++; bodyCustom++; }
                        else { unsupported++; bodyUnsupported++; }

                        AERISLogger.Info("[R031][PTC_SNAPSHOT_MOD] body=" + body.name +
                            "; type=" + Safe(type) +
                            "; order=" + ReadInt(mod, "order", -1) +
                            "; snapshot_strategy=" + strategy +
                            "; primitive_arrays=" + arrays +
                            "; primitive_array_bytes=" + bytes +
                            "; map_objects=" + maps +
                            "; evidence=" + Safe(evidence) +
                            "; worker_invokes_runtime_object=false" +
                            "; certification=NO_SHADOW_ONLY; authority=PQS");
                    }
                }
                bool pure = bodyContributors > 0 && bodyManaged == bodyContributors;
                if (pure) pureBodies++;
                string profile = pure ? "PURE_DATA_WORKER_CANDIDATE" :
                    bodyUnsupported > 0 ? "UNSUPPORTED_REQUIRES_PQS" :
                    bodyCustom > 0 ? "CUSTOM_ADAPTER_REQUIRED" :
                    bodyMap > 0 ? "MAP_PLUS_MANAGED_MATH_CANDIDATE" :
                    "MIXED_SNAPSHOT_CANDIDATE";
                AERISLogger.Info("[R031][PTC_SNAPSHOT_BODY] body=" + body.name +
                    "; contributors=" + bodyContributors +
                    "; managed_math=" + bodyManaged +
                    "; map_snapshot=" + bodyMap +
                    "; custom_adapter=" + bodyCustom +
                    "; unsupported=" + bodyUnsupported +
                    "; worker_profile=" + profile +
                    "; worker_invokes_runtime_object=false" +
                    "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; authority=PQS");
            }
            AERISLogger.Info("[R031][PTC_SNAPSHOT] event=FEASIBILITY_COMPLETE; bodies=" + bodies +
                "; contributors=" + contributors +
                "; managed_math=" + managedMath +
                "; map_snapshot=" + mapSnapshot +
                "; custom_adapter=" + customAdapter +
                "; unsupported=" + unsupported +
                "; primitive_arrays=" + primitiveArrays +
                "; primitive_array_bytes=" + primitiveArrayBytes +
                "; map_objects=" + mapObjects +
                "; pure_data_worker_bodies=" + pureBodies +
                "; worker_invokes_runtime_object=false" +
                "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static string ClassifySnapshot(object mod, string type, out int arrays,
            out int bytes, out int maps, out string evidence)
        {
            arrays = 0; bytes = 0; maps = 0;
            var evidenceParts = new List<string>(12);
            string lowerType = (type ?? string.Empty).ToLowerInvariant();
            bool local = lowerType.Contains("mapdecal") || lowerType.Contains("flattenarea");
            bool mapBacked = lowerType.Contains("heightmap") ||
                lowerType.Contains("simplexheightmap");
            bool procedural = lowerType.Contains("simplex") || lowerType.Contains("noise") ||
                lowerType.Contains("voronoi") || lowerType.Contains("crater") ||
                lowerType.Contains("vertexplanet") || lowerType.Contains("heightoffset") ||
                lowerType.Contains("flattenocean");

            FieldInfo[] fields = mod.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length && evidenceParts.Count < 12; i++)
            {
                FieldInfo f = fields[i];
                if (f == null || f.IsStatic) continue;
                object value;
                try { value = f.GetValue(mod); } catch { continue; }
                Type rt = value == null ? f.FieldType : value.GetType();
                string tn = rt == null ? string.Empty : (rt.FullName ?? rt.Name);
                string tl = tn.ToLowerInvariant();
                if (rt != null && rt.IsArray)
                {
                    Array a = value as Array;
                    Type element = rt.GetElementType();
                    if (a != null && Primitive(element))
                    {
                        int length = a.Length;
                        int itemBytes = PrimitiveSize(element);
                        arrays++;
                        bytes += itemBytes <= 0 ? 0 : length * itemBytes;
                        evidenceParts.Add(f.Name + "=" + tn + "[" + length + "]");
                    }
                    continue;
                }
                if (tl.Contains("mapso"))
                {
                    maps++;
                    int innerArrays, innerBytes;
                    string mapEvidence = DescribeObjectData(value, out innerArrays,
                        out innerBytes);
                    arrays += innerArrays; bytes += innerBytes;
                    evidenceParts.Add(f.Name + "=" + tn + "{" + mapEvidence + "}");
                    continue;
                }
                if (tl.Contains("simplex") || tl.Contains("libnoise") ||
                    tl.Contains("perlin") || tl.Contains("billow") || tl.Contains("ridged"))
                {
                    bool unityType = IsUnityBacked(rt, value);
                    MethodInfo getValue = FindThreeDoubleMethod(rt, "GetValue");
                    int innerArrays, innerBytes;
                    string data = DescribeObjectData(value, out innerArrays, out innerBytes);
                    arrays += innerArrays; bytes += innerBytes;
                    evidenceParts.Add(f.Name + "=" + tn + "{unity=" + unityType +
                        ",get_value_xyz=" + (getValue != null) + "," + data + "}");
                }
            }
            evidence = string.Join(",", evidenceParts.ToArray());

            if (local) return "CUSTOM_ADAPTER_REQUIRED";
            if (mapBacked && maps > 0) return "MAPSO_MAIN_THREAD_COPY_CANDIDATE";
            if (procedural)
            {
                // Runtime PQSMods themselves are never worker-safe. This means only that
                // their child math state is inspectable enough to justify a pure-data clone PoC.
                return "MANAGED_MATH_SNAPSHOT_CANDIDATE";
            }
            return "UNSUPPORTED_KEEP_PQS";
        }

        static string DescribeObjectData(object target, out int arrays, out int bytes)
        {
            arrays = 0; bytes = 0;
            if (target == null) return "null";
            var result = new List<string>(12);
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a,b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < fields.Length && result.Count < 12; i++)
            {
                FieldInfo f = fields[i];
                if (f == null || f.IsStatic) continue;
                object value;
                try { value = f.GetValue(target); } catch { continue; }
                Type ft = f.FieldType;
                if (Primitive(ft))
                {
                    result.Add(f.Name + "=" + Convert.ToString(value,
                        CultureInfo.InvariantCulture));
                    continue;
                }
                if (ft != null && ft.IsArray && Primitive(ft.GetElementType()))
                {
                    Array a = value as Array;
                    int length = a == null ? 0 : a.Length;
                    int itemBytes = PrimitiveSize(ft.GetElementType());
                    arrays++;
                    bytes += itemBytes <= 0 ? 0 : length * itemBytes;
                    result.Add(f.Name + "=" + ft.FullName + "[" + length + "]");
                }
            }
            return string.Join("/", result.ToArray());
        }

        static bool IsUnityBacked(Type type, object value)
        {
            if (value is UnityEngine.Object) return true;
            string name = type == null ? string.Empty : (type.FullName ?? type.Name);
            return name.StartsWith("UnityEngine.", StringComparison.Ordinal);
        }

        static MethodInfo FindThreeDoubleMethod(Type type, string name)
        {
            if (type == null) return null;
            try
            {
                MethodInfo[] methods = type.GetMethods(BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m == null || !string.Equals(m.Name, name,
                        StringComparison.Ordinal)) continue;
                    ParameterInfo[] p = m.GetParameters();
                    if (p.Length == 3 && p[0].ParameterType == typeof(double) &&
                        p[1].ParameterType == typeof(double) &&
                        p[2].ParameterType == typeof(double)) return m;
                }
            }
            catch { }
            return null;
        }

        static bool Primitive(Type type)
        {
            return type != null && (type.IsPrimitive || type.IsEnum ||
                type == typeof(string) || type == typeof(decimal));
        }

        static int PrimitiveSize(Type type)
        {
            if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(bool)) return 1;
            if (type == typeof(short) || type == typeof(ushort) || type == typeof(char)) return 2;
            if (type == typeof(int) || type == typeof(uint) || type == typeof(float)) return 4;
            if (type == typeof(long) || type == typeof(ulong) || type == typeof(double)) return 8;
            return 0;
        }

        static bool LikelyHeight(string type)
        {
            string t = (type ?? string.Empty).ToLowerInvariant();
            if (t.Contains("color") || t.Contains("material") || t.Contains("scatter") ||
                t.Contains("quad")) return false;
            return t.Contains("height") || t.Contains("crater") || t.Contains("flatten") ||
                t.Contains("vertexplanet") || t.Contains("mapdecal") || t.Contains("noise") ||
                t.Contains("simplex") || t.Contains("voronoi") || t.Contains("offset");
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
            object value = null;
            try { value = ReadMember(target, name); } catch { }
            return value is bool ? (bool)value : fallback;
        }

        static int ReadInt(object target, string name, int fallback)
        {
            object value = null;
            try { value = ReadMember(target, name); } catch { }
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
    raise SystemExit('R031 worker snapshot feasibility observer exists with unexpected content')
SOURCE.write_text(OBSERVER)

csproj = CSPROJ.read_text()
include = '    <Compile Include="Terrain\\AERISR031PtcWorkerSnapshotFeasibilityObserver.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISR031PtcCpuReconstructionFeasibilityObserver.cs" />\n'
if include not in csproj:
    if anchor not in csproj:
        raise SystemExit('R031 CPU feasibility observer csproj anchor missing')
    csproj = csproj.replace(anchor, anchor + include, 1)
    CSPROJ.write_text(csproj)

version = VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R031 CPU feasibility parent build identity missing')
version = version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC CPU RECONSTRUCTION FEASIBILITY SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC WORKER SNAPSHOT FEASIBILITY SHADOW')
version = version.replace(PARENT, MARKER)

head = subprocess.check_output(['git','rev-parse','HEAD'], cwd=str(ROOT), text=True).strip()
h = hashlib.sha256()
files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs')) + [CSPROJ]
for path in files:
    if path == VERSION: continue
    h.update(str(path.relative_to(ROOT)).encode()); h.update(b'\0')
    h.update(path.read_bytes()); h.update(b'\0')
version = re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
    'internal const string SourceGitSha = "' + head + '";', version)
version = re.sub(r'internal const string SourceTreeSha256 = "[0-9a-fA-F]*";',
    'internal const string SourceTreeSha256 = "' + h.hexdigest() + '";', version)
VERSION.write_text(version)

print('PASS: materialized R031 worker snapshot feasibility shadow probe')
print('runtime_change=OBSERVATION_ONLY')
print('worker_invokes_runtime_object=NO')
print('db_write=NO producer_switch=NO certification=NO authority=PQS')
