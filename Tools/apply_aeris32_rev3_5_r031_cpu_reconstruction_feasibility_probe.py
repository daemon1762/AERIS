#!/usr/bin/env python3
from pathlib import Path
import re
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
SOURCE = SRC / 'AERISR031PtcCpuReconstructionFeasibilityObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER = 'AERIS32_REV3_5_R031_PTC_CPU_RECONSTRUCTION_FEASIBILITY_SHADOW'
PARENT = 'AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW'

OBSERVER = r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_CPU_RECONSTRUCTION_FEASIBILITY_SHADOW
    // Observation only: no DB mutation, no producer switch, no certification.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcCpuReconstructionFeasibilityObserver : MonoBehaviour
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
            int bodies = 0, contributors = 0, mapBacked = 0, procedural = 0,
                local = 0, unknown = 0, directEntry = 0, mapObjects = 0,
                noiseObjects = 0;
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null || string.IsNullOrEmpty(body.name) ||
                    !AERISTerrainTileSystem.BodyHasSolidSurface(body)) continue;
                bodies++;
                object pqs = ReadMember(body, "pqsController");
                IEnumerable mods = pqs == null ? null : ReadMember(pqs, "mods") as IEnumerable;
                int bodyContributors = 0, bodyMap = 0, bodyProc = 0, bodyLocal = 0,
                    bodyUnknown = 0;
                if (mods != null)
                {
                    foreach (object mod in mods)
                    {
                        if (mod == null) continue;
                        string type = mod.GetType().FullName ?? mod.GetType().Name;
                        if (!ReadBool(mod, "enabled", true) || !LikelyHeight(type)) continue;
                        bodyContributors++; contributors++;
                        string strategy = Strategy(type);
                        if (strategy == "MAP_BACKED") { bodyMap++; mapBacked++; }
                        else if (strategy == "PROCEDURAL") { bodyProc++; procedural++; }
                        else if (strategy == "LOCAL_MODIFIER") { bodyLocal++; local++; }
                        else { bodyUnknown++; unknown++; }

                        MethodInfo heightMethod = FindMethod(mod.GetType(), "OnVertexBuildHeight");
                        MethodInfo buildMethod = FindMethod(mod.GetType(), "OnVertexBuild");
                        if (heightMethod != null || buildMethod != null) directEntry++;

                        int maps, noises;
                        string graph = DescribeObjectGraph(mod, out maps, out noises);
                        mapObjects += maps; noiseObjects += noises;
                        AERISLogger.Info("[R031][PTC_CPU_MOD] body=" + body.name +
                            "; type=" + Safe(type) +
                            "; order=" + ReadInt(mod, "order", -1) +
                            "; strategy=" + strategy +
                            "; on_vertex_build_height=" + (heightMethod != null) +
                            "; on_vertex_build=" + (buildMethod != null) +
                            "; object_graph=" + Safe(graph) +
                            "; params=" + Safe(DescribeParameters(mod)) +
                            "; certification=NO_SHADOW_ONLY; authority=PQS");
                    }
                }
                string profile = bodyLocal > 0 ? "HYBRID_LOCAL_COMPLEX" :
                    bodyMap > 0 && bodyProc > 0 ? "HYBRID_MAP_PLUS_PROCEDURAL" :
                    bodyMap > 0 ? "MAP_DOMINANT" :
                    bodyProc > 0 ? "PURE_PROCEDURAL" : "UNKNOWN";
                AERISLogger.Info("[R031][PTC_CPU_BODY] body=" + body.name +
                    "; contributors=" + bodyContributors +
                    "; map_backed=" + bodyMap +
                    "; procedural=" + bodyProc +
                    "; local_modifiers=" + bodyLocal +
                    "; unknown=" + bodyUnknown +
                    "; reconstruction_profile=" + profile +
                    "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; authority=PQS");
            }
            AERISLogger.Info("[R031][PTC_CPU] event=FEASIBILITY_COMPLETE; bodies=" + bodies +
                "; contributors=" + contributors +
                "; map_backed=" + mapBacked +
                "; procedural=" + procedural +
                "; local_modifiers=" + local +
                "; unknown=" + unknown +
                "; direct_entry_methods=" + directEntry +
                "; map_objects=" + mapObjects +
                "; noise_objects=" + noiseObjects +
                "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static string Strategy(string type)
        {
            string t = (type ?? string.Empty).ToLowerInvariant();
            if (t.Contains("mapdecal") || t.Contains("flattenarea")) return "LOCAL_MODIFIER";
            if (t.Contains("heightmap") || t.Contains("simplexheightmap")) return "MAP_BACKED";
            if (t.Contains("simplex") || t.Contains("noise") || t.Contains("voronoi") ||
                t.Contains("crater") || t.Contains("vertexplanet") || t.Contains("heightoffset") ||
                t.Contains("flattenocean")) return "PROCEDURAL";
            return "UNKNOWN";
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

        static MethodInfo FindMethod(Type type, string name)
        {
            if (type == null) return null;
            try
            {
                return type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic);
            }
            catch { return null; }
        }

        static string DescribeObjectGraph(object target, out int maps, out int noises)
        {
            maps = 0; noises = 0;
            if (target == null) return string.Empty;
            var values = new List<string>(12);
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length && values.Count < 12; i++)
            {
                FieldInfo f = fields[i];
                if (f == null || f.IsStatic) continue;
                object value;
                try { value = f.GetValue(target); } catch { continue; }
                Type rt = value == null ? f.FieldType : value.GetType();
                string tn = rt == null ? string.Empty : (rt.FullName ?? rt.Name);
                string lower = tn.ToLowerInvariant();
                bool map = lower.Contains("mapso") || lower.Contains("texture");
                bool noise = lower.Contains("libnoise") || lower.Contains("simplex") ||
                    lower.Contains("perlin") || lower.Contains("billow") ||
                    lower.Contains("ridged");
                if (!map && !noise) continue;
                if (map) maps++;
                if (noise) noises++;
                string name = string.Empty;
                UnityEngine.Object unity = value as UnityEngine.Object;
                if (unity != null) try { name = unity.name ?? string.Empty; } catch { }
                values.Add(f.Name + "=" + tn + (string.IsNullOrEmpty(name) ? string.Empty :
                    "#" + name) + "{" + DescribePrimitiveFields(value, 8) + "}");
            }
            return string.Join(",", values.ToArray());
        }

        static string DescribePrimitiveFields(object target, int maximum)
        {
            if (target == null) return string.Empty;
            var result = new List<string>(maximum);
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a,b) => string.CompareOrdinal(a.Name, b.Name));
            for (int i = 0; i < fields.Length && result.Count < maximum; i++)
            {
                FieldInfo f = fields[i];
                if (f == null || f.IsStatic || !Primitive(f.FieldType)) continue;
                object value;
                try { value = f.GetValue(target); } catch { continue; }
                result.Add(f.Name + "=" + Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            return string.Join("/", result.ToArray());
        }

        static string DescribeParameters(object target)
        {
            string[] names = { "deformity", "offset", "frequency", "octaves", "persistence",
                "persistance", "lacunarity", "seed", "radius", "heightStart", "heightEnd",
                "scaleDeformityByRadius" };
            var result = new List<string>();
            for (int i = 0; i < names.Length; i++)
            {
                object value = null;
                try { value = ReadMember(target, names[i]); } catch { }
                if (value == null || !Primitive(value.GetType())) continue;
                result.Add(names[i] + "=" + Convert.ToString(value,
                    CultureInfo.InvariantCulture));
            }
            return string.Join(",", result.ToArray());
        }

        static bool Primitive(Type type)
        {
            return type != null && (type.IsPrimitive || type.IsEnum || type == typeof(string) ||
                type == typeof(decimal));
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
    raise SystemExit('R031 CPU feasibility observer exists with unexpected content')
SOURCE.write_text(OBSERVER)

csproj = CSPROJ.read_text()
include = '    <Compile Include="Terrain\\AERISR031PtcCpuReconstructionFeasibilityObserver.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISR031PtcRuntimeSourceGraphObserver.cs" />\n'
if include not in csproj:
    if anchor not in csproj:
        raise SystemExit('R031 runtime graph observer csproj anchor missing')
    csproj = csproj.replace(anchor, anchor + include, 1)
    CSPROJ.write_text(csproj)

version = VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R031 runtime graph parent build identity missing')
version = version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC RUNTIME SOURCE GRAPH SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC CPU RECONSTRUCTION FEASIBILITY SHADOW')
version = version.replace(PARENT, MARKER)
head = subprocess.check_output(['git','rev-parse','HEAD'], cwd=str(ROOT), text=True).strip()
version = re.sub(r'internal const string SourceGitSha = "[0-9a-fA-F]*";',
    'internal const string SourceGitSha = "' + head + '";', version)
VERSION.write_text(version)

print('PASS: materialized R031 CPU reconstruction feasibility shadow probe')
print('runtime_change=OBSERVATION_ONLY')
print('db_write=NO producer_switch=NO certification=NO authority=PQS')
