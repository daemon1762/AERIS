#!/usr/bin/env python3
from pathlib import Path
import hashlib
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
MARKER = 'AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER'
SOURCE = ROOT / 'Source/AERISFlightControl/Terrain/AERISR030PreloadPersistencePtcPhase0Observer.cs'
CSPROJ = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
VERSION = ROOT / 'Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs'

OBSERVER = r'''using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER
    // Observation only. No terrain generation, DB mutation, scheduling or display authority.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR030PreloadPersistencePtcPhase0Observer : MonoBehaviour
    {
        internal const string Variant = "AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0_OBSERVER";

        sealed class PersistedPlan
        {
            internal string BodyName = string.Empty;
            internal bool AutomaticComplete;
            internal int CompletedQualityLimit;
            internal bool CompletedPointRefinementOnly;
            internal string CompletedEnvironmentHash = string.Empty;
            internal string EnvironmentHash = string.Empty;
            internal bool CoastlineComplete;
            internal int CompletedCoastlineFormatVersion;
            internal string CompletedCoastlineEnvironmentHash = string.Empty;
        }

        float nextAttemptRealtime;
        bool captured;

        void Awake()
        {
            AERISLogger.Info("[R030][PTC0] event=OBSERVER_START; variant=" + Variant);
        }

        void Update()
        {
            if (captured) return;
            float now = Time.realtimeSinceStartup;
            if (now < nextAttemptRealtime) return;
            nextAttemptRealtime = now + 1.0f;
            if (!AERISTerrainTileSystem.GameDataHashReady) return;
            IList<CelestialBody> bodies = FlightGlobals.Bodies;
            if (bodies == null || bodies.Count == 0) return;
            Capture(bodies);
            captured = true;
        }

        void Capture(IList<CelestialBody> bodies)
        {
            string root = KSPUtil.ApplicationRootPath ?? string.Empty;
            string dbRoot = Path.Combine(root, "GameData", "AERISFlightControl", "PluginData",
                "TerrainPreloadDatabaseV3");
            string statePath = Path.Combine(dbRoot, "preload_state.aps");
            string stateBackupPath = statePath + ".bak";
            string selectedState = File.Exists(statePath) ? statePath :
                File.Exists(stateBackupPath) ? stateBackupPath : string.Empty;
            string stateStatus;
            Dictionary<string, PersistedPlan> persisted = ReadState(selectedState, out stateStatus);

            string gameData = Path.Combine(root, "GameData");
            string mmSha = Path.Combine(gameData, "ModuleManager.ConfigSHA");
            string settingsPath = Path.Combine(gameData, "AERISFlightControl", "Config",
                "AERISSettings.cfg");
            string gameDataMode = File.Exists(mmSha) ? "MODULEMANAGER_CONFIGSHA" : "CFG_FALLBACK_SCAN";
            string settingsHash = File.Exists(settingsPath) ? HashFileText(settingsPath) : "ABSENT";

            int solid = 0;
            int stateComplete = 0;
            int envMismatch = 0;
            int completionMismatch = 0;
            int fileCandidates = 0;
            AERISLogger.Info("[R030][PRELOAD_PERSIST] event=SESSION; gamedata_hash=" +
                AERISTerrainTileSystem.GameDataHash + "; gamedata_mode=" + gameDataMode +
                "; state=" + stateStatus + "; state_path=" + Safe(selectedState) +
                "; settings_hash=" + settingsHash +
                "; settings_in_fallback_scope=" + (!File.Exists(mmSha) && File.Exists(settingsPath)));

            for (int i = 0; i < bodies.Count; i++)
            {
                CelestialBody body = bodies[i];
                if (body == null || string.IsNullOrEmpty(body.name)) continue;
                object pqs = ReadPqs(body);
                bool hasSolid = pqs != null && AERISTerrainTileSystem.BodyHasSolidSurface(body);
                if (!hasSolid)
                {
                    AERISLogger.Info("[R030][PTC_BODY] body=" + Safe(body.name) +
                        "; solid=false; classification=SKIP; radius=" +
                        body.Radius.ToString("R", CultureInfo.InvariantCulture) +
                        "; ocean=" + body.ocean);
                    continue;
                }

                solid++;
                string currentEnvironment = AERISTerrainTileSystem.EnvironmentHashForBody(body);
                string structural;
                string primitive;
                string sourceHints;
                int modCount;
                BuildPqsDescriptor(body, pqs, out structural, out primitive,
                    out sourceHints, out modCount);
                string structuralHash = AERISTerrainHash.Fnv1A64Hex(structural);
                string primitiveHash = AERISTerrainHash.Fnv1A64Hex(primitive);
                string planetTerrainHash = AERISTerrainHash.Fnv1A64Hex(
                    "PTC0|" + structuralHash + "|" + primitiveHash);
                bool fileCandidate = !string.IsNullOrEmpty(sourceHints);
                if (fileCandidate) fileCandidates++;

                PersistedPlan plan;
                bool hasState = persisted.TryGetValue(body.name, out plan) && plan != null;
                bool storedReady = hasState && plan.AutomaticComplete;
                if (storedReady) stateComplete++;
                bool environmentMatch = hasState && string.Equals(plan.EnvironmentHash,
                    currentEnvironment, StringComparison.Ordinal);
                bool completedEnvironmentMatch = hasState && string.Equals(
                    plan.CompletedEnvironmentHash, currentEnvironment, StringComparison.Ordinal);
                if (hasState && !environmentMatch) envMismatch++;
                if (storedReady && !completedEnvironmentMatch) completionMismatch++;

                AERISLogger.Info("[R030][PTC_BODY] body=" + Safe(body.name) +
                    "; solid=true; classification=" + (fileCandidate ? "FILE_CANDIDATE" : "UNKNOWN") +
                    "; radius=" + body.Radius.ToString("R", CultureInfo.InvariantCulture) +
                    "; ocean=" + body.ocean + "; pqs=" + Safe(pqs.GetType().FullName) +
                    "; mods=" + modCount + "; structural_hash=" + structuralHash +
                    "; primitive_hash=" + primitiveHash + "; planet_hash=" + planetTerrainHash +
                    "; environment=" + currentEnvironment + "; source_hints=" + Safe(sourceHints) +
                    "; state_present=" + hasState + "; state_ready=" + storedReady +
                    "; state_environment=" + (hasState ? plan.EnvironmentHash : string.Empty) +
                    "; completed_environment=" + (hasState ? plan.CompletedEnvironmentHash : string.Empty) +
                    "; env_match=" + environmentMatch +
                    "; completed_env_match=" + completedEnvironmentMatch +
                    "; coastline_ready=" + (hasState && plan.CoastlineComplete));
            }

            AERISLogger.Info("[R030][PTC0] event=INVENTORY_COMPLETE; runtime_bodies=" +
                bodies.Count + "; solid_bodies=" + solid + "; persisted_plans=" + persisted.Count +
                "; persisted_ready=" + stateComplete + "; environment_mismatch=" + envMismatch +
                "; ready_environment_mismatch=" + completionMismatch +
                "; file_candidates=" + fileCandidates + "; db_root_exists=" + Directory.Exists(dbRoot) +
                "; manifest_exists=" + File.Exists(Path.Combine(dbRoot, "manifest.atm")));
        }

        static Dictionary<string, PersistedPlan> ReadState(string path, out string status)
        {
            var result = new Dictionary<string, PersistedPlan>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                status = "ABSENT";
                return result;
            }
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var reader = new BinaryReader(stream))
                {
                    string magic = reader.ReadString();
                    int version = reader.ReadInt32();
                    if (!string.Equals(magic, AERISTerrainPreloadFormat.StateMagic,
                        StringComparison.Ordinal) || version != 4)
                    {
                        status = "UNSUPPORTED:" + version;
                        return result;
                    }
                    reader.ReadInt32(); // mode
                    int count = reader.ReadInt32();
                    if (count < 0 || count > 4096) throw new InvalidDataException("state body count");
                    for (int i = 0; i < count; i++)
                    {
                        var plan = new PersistedPlan();
                        plan.BodyName = reader.ReadString();
                        reader.ReadInt32(); // priority
                        reader.ReadBoolean(); // priority override
                        reader.ReadInt32(); // quality limit
                        reader.ReadBoolean(); // quality override
                        reader.ReadBoolean(); // point refinement only
                        plan.AutomaticComplete = reader.ReadBoolean();
                        plan.CompletedQualityLimit = reader.ReadInt32();
                        plan.CompletedPointRefinementOnly = reader.ReadBoolean();
                        plan.CompletedEnvironmentHash = reader.ReadString();
                        reader.ReadInt64(); // storage limit
                        reader.ReadInt64(); // last visited
                        reader.ReadInt64(); // global cursor
                        reader.ReadInt64(); // far cursor
                        reader.ReadInt64(); // route cursor
                        reader.ReadInt32(); // point cursor
                        plan.EnvironmentHash = reader.ReadString();
                        reader.ReadBoolean(); // paused
                        reader.ReadInt64(); // coastline cursor
                        plan.CoastlineComplete = reader.ReadBoolean();
                        plan.CompletedCoastlineFormatVersion = reader.ReadInt32();
                        plan.CompletedCoastlineEnvironmentHash = reader.ReadString();
                        if (!string.IsNullOrEmpty(plan.BodyName)) result[plan.BodyName] = plan;
                    }
                    status = stream.Position == stream.Length ? "V4_OK" : "V4_TRAILING_DATA";
                }
            }
            catch (Exception ex)
            {
                status = "READ_FAIL:" + ex.GetType().Name;
                result.Clear();
            }
            return result;
        }

        static object ReadPqs(CelestialBody body)
        {
            if (body == null) return null;
            try
            {
                Type type = body.GetType();
                FieldInfo field = type.GetField("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null) return field.GetValue(body);
                PropertyInfo property = type.GetProperty("pqsController",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return property == null ? null : property.GetValue(body, null);
            }
            catch { return null; }
        }

        static void BuildPqsDescriptor(CelestialBody body, object pqs,
            out string structural, out string primitive, out string sourceHints, out int modCount)
        {
            var s = new StringBuilder(2048);
            var p = new StringBuilder(8192);
            var hints = new List<string>(8);
            s.Append(body.name).Append('|').Append(body.Radius.ToString("R", CultureInfo.InvariantCulture))
                .Append('|').Append(body.ocean ? '1' : '0').Append('|')
                .Append(pqs.GetType().AssemblyQualifiedName).Append('|');
            p.Append(s.ToString());
            AppendStablePrimitiveMembers(p, pqs, 96);
            CollectSourceHints(hints, pqs);
            object modsObject = ReadMemberValue(pqs, "mods");
            IEnumerable mods = modsObject as IEnumerable;
            modCount = 0;
            if (mods != null)
            {
                foreach (object mod in mods)
                {
                    if (mod == null || modCount >= 128) break;
                    modCount++;
                    string type = mod.GetType().AssemblyQualifiedName;
                    s.Append("MOD:").Append(type).Append('|');
                    p.Append("MOD:").Append(type).Append('|');
                    AppendStablePrimitiveMembers(p, mod, 64);
                    CollectSourceHints(hints, mod);
                }
            }
            structural = s.ToString();
            primitive = p.ToString();
            sourceHints = string.Join(",", hints.ToArray());
        }

        static object ReadMemberValue(object target, string name)
        {
            if (target == null) return null;
            Type type = target.GetType();
            FieldInfo field = type.GetField(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            return property != null && property.CanRead && property.GetIndexParameters().Length == 0 ?
                property.GetValue(target, null) : null;
        }

        static void AppendStablePrimitiveMembers(StringBuilder builder, object target, int maximum)
        {
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            Array.Sort(fields, (a, b) => string.CompareOrdinal(a.Name, b.Name));
            int written = 0;
            for (int i = 0; i < fields.Length && written < maximum; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.IsStatic || !StableType(field.FieldType)) continue;
                object value;
                try { value = field.GetValue(target); } catch { continue; }
                builder.Append(field.Name).Append('=').Append(StableValue(value)).Append('|');
                written++;
            }
        }

        static bool StableType(Type type)
        {
            return type != null && (type.IsPrimitive || type.IsEnum || type == typeof(string) ||
                type == typeof(decimal) || type == typeof(Vector2) || type == typeof(Vector3) ||
                type == typeof(Vector4) || type == typeof(Color));
        }

        static string StableValue(object value)
        {
            if (value == null) return "<null>";
            IFormattable formattable = value as IFormattable;
            return formattable == null ? value.ToString() : formattable.ToString(null,
                CultureInfo.InvariantCulture);
        }

        static void CollectSourceHints(List<string> hints, object target)
        {
            if (hints == null || target == null || hints.Count >= 8) return;
            FieldInfo[] fields = target.GetType().GetFields(BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length && hints.Count < 8; i++)
            {
                FieldInfo field = fields[i];
                if (field == null || field.IsStatic || field.FieldType != typeof(string)) continue;
                string name = field.Name ?? string.Empty;
                string lower = name.ToLowerInvariant();
                if (!(lower.Contains("map") || lower.Contains("texture") || lower.Contains("path") ||
                    lower.Contains("file"))) continue;
                string value;
                try { value = field.GetValue(target) as string; } catch { continue; }
                if (string.IsNullOrEmpty(value)) continue;
                hints.Add(target.GetType().Name + "." + name + "=" + Safe(value));
            }
        }

        static string HashFileText(string path)
        {
            try { return AERISTerrainHash.Fnv1A64Hex(File.ReadAllText(path)); }
            catch { return "READ_FAIL"; }
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace('\n', ' ').Replace('\r', ' ').Replace(';', ',').Replace('|', '/');
        }
    }
}
'''


def tree_hash():
    h = hashlib.sha256()
    files = sorted((ROOT / 'Source/AERISFlightControl').rglob('*.cs'))
    files += [CSPROJ]
    for path in files:
        if path == VERSION:
            continue
        h.update(str(path.relative_to(ROOT)).encode())
        h.update(b'\0')
        h.update(path.read_bytes())
        h.update(b'\0')
    return h.hexdigest()

SOURCE.parent.mkdir(parents=True, exist_ok=True)
if SOURCE.exists() and SOURCE.read_text() != OBSERVER:
    raise SystemExit('R030 observer exists with unexpected content: ' + str(SOURCE))
SOURCE.write_text(OBSERVER)

text = CSPROJ.read_text()
include = '    <Compile Include="Terrain\\AERISR030PreloadPersistencePtcPhase0Observer.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISTerrainPreloadContracts.cs" />\n'
if include not in text:
    if anchor not in text:
        raise SystemExit('R030 csproj anchor missing')
    text = text.replace(anchor, anchor + include, 1)
    CSPROJ.write_text(text)

head = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=str(ROOT), text=True).strip()
source_hash = tree_hash()
VERSION.write_text('using System.Reflection;\n'
    '[assembly: AssemblyVersion("0.18.0.0")]\n'
    '[assembly: AssemblyFileVersion("0.18.0.0")]\n'
    'namespace AERISFlightControl { internal static class AERISBuildVersion { '
    'internal const string Semantic = "0.18.0.0"; '
    'internal const string Display = "AERIS Flight Control v0.18.0.0 DEV CP3.75 AERIS32 REV3.5 R030 PRELOAD PERSISTENCE + PTC PHASE0 OBSERVER"; '
    'internal const string UiCheckpoint = "DEV CP3.75 — AERIS32 — REV3.5 R030 — PRELOAD PERSISTENCE + PTC PHASE0 OBSERVER"; '
    'internal const string CandidateName = "AERIS32_REV3_5_R030_PRELOAD_PERSISTENCE_PTC_PHASE0"; '
    'internal const string SourceGitSha = "' + head + '"; '
    'internal const string SourceTreeSha256 = "' + source_hash + '"; '
    'internal const string Cp2FrozenBaselineDisplay = "AERIS Flight Control v0.18.0.0 DEV CP2 FROZEN BASELINE"; } }\n')

print('PASS: materialized R030 preload persistence + PTC phase0 observer')
print('observer=' + str(SOURCE.relative_to(ROOT)))
print('source_tree_sha256=' + source_hash)
print('runtime_behavior_change=NONE (observer/logging only)')
