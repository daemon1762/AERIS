#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source/AERISFlightControl/Terrain'
SOURCE = SRC / 'AERISR031PtcRuntimeSourceGraphObserver.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'
MARKER = 'AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW'
PARENT = 'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW'

OBSERVER = r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW
    // Observation only. No DB mutation, producer switch, terrain certification or GPU work.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR031PtcRuntimeSourceGraphObserver : MonoBehaviour
    {
        internal const string Variant =
            "AERIS32_REV3_5_R031_PTC_RUNTIME_SOURCE_GRAPH_SHADOW";
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
            int solid = 0;
            int modsTotal = 0;
            int likelyHeight = 0;
            int mapObjects = 0;
            int simpleCandidates = 0;
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null || string.IsNullOrEmpty(body.name) ||
                    !AERISTerrainTileSystem.BodyHasSolidSurface(body)) continue;
                solid++;
                object pqs = ReadMember(body, "pqsController");
                IEnumerable mods = pqs == null ? null : ReadMember(pqs, "mods") as IEnumerable;
                int bodyMods = 0;
                int bodyLikelyHeight = 0;
                int bodyMapObjects = 0;
                int directHeightMaps = 0;
                if (mods != null)
                {
                    foreach (object mod in mods)
                    {
                        if (mod == null || bodyMods >= 256) break;
                        int ordinal = bodyMods++;
                        modsTotal++;
                        string type = mod.GetType().FullName ?? mod.GetType().Name;
                        bool enabled = ReadBool(mod, "enabled", true);
                        int order = ReadInt(mod, "order", ordinal);
                        bool likely = enabled && LikelyHeightContributor(type);
                        if (likely) { bodyLikelyHeight++; likelyHeight++; }
                        if (enabled && type.IndexOf("VertexHeightMap",
                            StringComparison.OrdinalIgnoreCase) >= 0) directHeightMaps++;
                        string sources = DescribeSourceObjects(mod, out int sourceCount);
                        bodyMapObjects += sourceCount;
                        mapObjects += sourceCount;
                        string parameters = DescribeParameters(mod);
                        AERISLogger.Info("[R031][PTC_GRAPH_MOD] body=" + body.name +
                            "; ordinal=" + ordinal + "; order=" + order +
                            "; enabled=" + enabled + "; type=" + Safe(type) +
                            "; likely_height_contributor=" + likely +
                            "; source_objects=" + Safe(sources) +
                            "; parameters=" + Safe(parameters) +
                            "; authority=DIAGNOSTIC_ONLY");
                    }
                }
                bool simple = directHeightMaps == 1 && bodyLikelyHeight == 1;
                if (simple) simpleCandidates++;
                AERISLogger.Info("[R031][PTC_GRAPH_BODY] body=" + body.name +
                    "; mods=" + bodyMods +
                    "; likely_height_contributors=" + bodyLikelyHeight +
                    "; direct_height_maps=" + directHeightMaps +
                    "; source_object_members=" + bodyMapObjects +
                    "; shadow_profile=" + (simple ? "SIMPLE_HEIGHTMAP_CANDIDATE" :
                        "COMPOSITE_OR_PROCEDURAL") +
                    "; certification=NO_SHADOW_ONLY; db_write=false; authority=PQS");
            }
            AERISLogger.Info("[R031][PTC_GRAPH] event=COMPLETE; solid_bodies=" + solid +
                "; mods=" + modsTotal +
                "; likely_height_contributors=" + likelyHeight +
                "; source_object_members=" + mapObjects +
                "; simple_heightmap_candidates=" + simpleCandidates +
                "; certification=NO_SHADOW_ONLY; db_write=false; producer_switch=false; gpu=false; authority=PQS");
        }

        static bool LikelyHeightContributor(string type)
        {
            if (string.IsNullOrEmpty(type)) return false;
            string t = type.ToLowerInvariant();
            if (t.Contains("color") || t.Contains("material") || t.Contains("quad") ||
                t.Contains("scatter")) return false;
            return t.Contains("height") || t.Contains("crater") || t.Contains("flatten") ||
                t.Contains("deform") || t.Contains("oblate") || t.Contains("vertexplanet") ||
                t.Contains("mapdecal") || t.Contains("coast") || t.Contains("smoothlatitude") ||
                t.Contains("simplex") || t.Contains("noise");
        }

        static string DescribeSourceObjects(object target, out int count)
        {
            count = 0;
            if (target == null) return string.Empty;
            var values = new List<string>(8);
            foreach (FieldInfo field in target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field == null || field.IsStatic || !InterestingSourceMember(field.Name,
                    field.FieldType)) continue;
                object value;
                try { value = field.GetValue(target); } catch { continue; }
                string described = DescribeObject(field.Name, value, field.FieldType);
                if (string.IsNullOrEmpty(described)) continue;
                values.Add(described); count++;
                if (values.Count >= 8) break;
            }
            if (values.Count < 8)
            {
                foreach (PropertyInfo property in target.GetType().GetProperties(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (property == null || !property.CanRead ||
                        property.GetIndexParameters().Length != 0 ||
                        !InterestingSourceMember(property.Name, property.PropertyType)) continue;
                    object value;
                    try { value = property.GetValue(target, null); } catch { continue; }
                    string described = DescribeObject(property.Name, value,
                        property.PropertyType);
                    if (string.IsNullOrEmpty(described) || values.Contains(described)) continue;
                    values.Add(described); count++;
                    if (values.Count >= 8) break;
                }
            }
            return string.Join(",", values.ToArray());
        }

        static bool InterestingSourceMember(string name, Type declared)
        {
            string n = (name ?? string.Empty).ToLowerInvariant();
            string t = declared == null ? string.Empty :
                (declared.FullName ?? declared.Name).ToLowerInvariant();
            return n.Contains("map") || n.Contains("texture") || n.Contains("height") ||
                t.Contains("mapso") || t.Contains("texture2d") || t.Contains("texture");
        }

        static string DescribeObject(string member, object value, Type declared)
        {
            if (value == null) return member + "=<null>:" +
                (declared == null ? string.Empty : declared.FullName);
            string runtime = value.GetType().FullName ?? value.GetType().Name;
            string name = string.Empty;
            UnityEngine.Object unity = value as UnityEngine.Object;
            if (unity != null)
            {
                try { name = unity.name ?? string.Empty; } catch { name = string.Empty; }
            }
            if (value is string) name = value as string;
            return member + "=" + runtime + (string.IsNullOrEmpty(name) ? string.Empty :
                "#" + name);
        }

        static string DescribeParameters(object target)
        {
            if (target == null) return string.Empty;
            string[] names = { "deformity", "offset", "frequency", "octaves", "persistence",
                "persistance", "lacunarity", "seed", "radius", "heightStart", "heightEnd",
                "scaleDeformityByRadius" };
            var result = new List<string>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                object value = null;
                try { value = ReadMember(target, names[i]); } catch { }
                if (value == null) continue;
                Type type = value.GetType();
                if (!(type.IsPrimitive || type.IsEnum || type == typeof(decimal) ||
                    type == typeof(string))) continue;
                result.Add(names[i] + "=" + Convert.ToString(value,
                    CultureInfo.InvariantCulture));
            }
            return string.Join(",", result.ToArray());
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
            return property != null && property.CanRead &&
                property.GetIndexParameters().Length == 0 ? property.GetValue(target, null) : null;
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
                value.Replace('\n', ' ').Replace('\r', ' ').Replace(';', ',').Replace('|', '/');
        }
    }
}
'''

SOURCE.parent.mkdir(parents=True, exist_ok=True)
if SOURCE.exists() and SOURCE.read_text() != OBSERVER:
    raise SystemExit('R031 runtime source graph observer exists with unexpected content')
SOURCE.write_text(OBSERVER)

csproj = CSPROJ.read_text()
include = '    <Compile Include="Terrain\\AERISR031PtcRuntimeSourceGraphObserver.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISR031PtcShadowObserver.cs" />\n'
if include not in csproj:
    if anchor not in csproj:
        raise SystemExit('R031 parent observer csproj anchor missing')
    csproj = csproj.replace(anchor, anchor + include, 1)
    CSPROJ.write_text(csproj)

version = VERSION.read_text()
if PARENT not in version and MARKER not in version:
    raise SystemExit('R031 parent build identity missing')
version = version.replace(
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC SOURCE RESOLVER + CPU FILE_EXACT SHADOW',
    'AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R031 PTC RUNTIME SOURCE GRAPH SHADOW')
version = version.replace(
    'AERIS32_REV3_5_R031_PTC_SOURCE_RESOLVER_CPU_FILE_EXACT_SHADOW', MARKER)
VERSION.write_text(version)

print('PASS: materialized R031 runtime PQS source graph shadow probe')
print('runtime_change=OBSERVATION_ONLY')
print('db_write=NO producer_switch=NO certification=NO authority=PQS')
