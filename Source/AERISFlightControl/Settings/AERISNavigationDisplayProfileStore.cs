using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Terrain;

namespace AERISFlightControl.Settings
{
    // Per-craft ND presentation preferences. The signature is derived only on the
    // KSP/main thread from the craft name and stable craft-part topology. Runtime
    // world position, body, fuel state, vessel persistent id and flight state are
    // deliberately excluded so relaunching the same craft reuses its profile.
    internal sealed class AERISNavigationDisplayProfileStore
    {
        const int SchemaVersion = 1;
        const int MaximumProfiles = 128;
        readonly Dictionary<string, Profile> profiles =
            new Dictionary<string, Profile>(StringComparer.Ordinal);
        readonly Profile defaults;
        bool loaded;

        sealed class Profile
        {
            internal string Signature = string.Empty;
            internal string Label = string.Empty;
            internal long LastUsedUtcTicks;
            internal float RangeMeters = 20000f;
            internal bool TrackUp = true;
            internal AERISTerrainDisplayMode TerrainMode = AERISTerrainDisplayMode.Automatic;
            internal bool Trail;
            internal bool Vector = true;
            internal bool Traffic = true;
            internal bool Wind = true;
            internal AERISNavigationDisplayLandProfileSize LandProfileSize =
                AERISNavigationDisplayLandProfileSize.Normal;
        }

        static string PathName
        {
            get
            {
                return Path.Combine(KSPUtil.ApplicationRootPath, "GameData",
                    "AERISFlightControl", "Config", "NavigationDisplayProfiles.cfg");
            }
        }

        internal AERISNavigationDisplayProfileStore(AERISSettings settings)
        {
            defaults = Capture(string.Empty, "DEFAULT", settings);
        }

        internal string Apply(Vessel vessel, AERISSettings settings)
        {
            string signature = CreateSignature(vessel);
            if (string.IsNullOrEmpty(signature) || settings == null) return string.Empty;
            EnsureLoaded();
            Profile value;
            if (!profiles.TryGetValue(signature, out value)) value = defaults;
            Apply(value, settings);
            return signature;
        }

        internal void Save(string signature, string label, AERISSettings settings)
        {
            if (string.IsNullOrEmpty(signature) || settings == null) return;
            EnsureLoaded();
            Profile value = Capture(signature, label, settings);
            value.LastUsedUtcTicks = DateTime.UtcNow.Ticks;
            profiles[signature] = value;
            Trim();
            SaveFile();
        }

        internal static string CreateSignature(Vessel vessel)
        {
            if (vessel == null || vessel.parts == null || vessel.parts.Count == 0)
                return string.Empty;
            List<string> records = new List<string>(vessel.parts.Count);
            for (int i = 0; i < vessel.parts.Count; i++)
            {
                Part part = vessel.parts[i];
                if (part == null) continue;
                string name = part.partInfo == null ? part.name : part.partInfo.name;
                uint craftId = part.craftID;
                uint parentCraftId = part.parent == null ? 0u : part.parent.craftID;
                records.Add(craftId.ToString(CultureInfo.InvariantCulture) + ":" +
                    parentCraftId.ToString(CultureInfo.InvariantCulture) + ":" +
                    Safe(name));
            }
            records.Sort(StringComparer.Ordinal);
            StringBuilder source = new StringBuilder(128 + records.Count * 32);
            source.Append("AERIS-ND-CRAFT-1|");
            source.Append(Safe(vessel.vesselName));
            source.Append('|');
            source.Append(records.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < records.Count; i++)
            {
                source.Append('|');
                source.Append(records[i]);
            }
            return "ND-" + Fnv1A64(source.ToString()).ToString("X16",
                CultureInfo.InvariantCulture);
        }

        static Profile Capture(string signature, string label, AERISSettings settings)
        {
            Profile value = new Profile();
            value.Signature = signature ?? string.Empty;
            value.Label = label ?? string.Empty;
            if (settings == null) return value;
            value.RangeMeters = AERISSettings.NormalizeNavigationRange(
                settings.NavigationDisplayManualRangeMeters);
            value.TrackUp = settings.NavigationDisplayTrackUp;
            value.TerrainMode = settings.TerrainDisplayMode;
            value.Trail = settings.NavigationDisplayTrailEnabled;
            value.Vector = settings.NavigationDisplayTrackVectorEnabled;
            value.Traffic = settings.NavigationDisplayTrafficEnabled;
            value.Wind = settings.NavigationDisplayWindEnabled;
            value.LandProfileSize = settings.NavigationDisplayLandProfileSize;
            return value;
        }

        static void Apply(Profile value, AERISSettings settings)
        {
            if (value == null || settings == null) return;
            settings.NavigationDisplayAutoRange = false;
            settings.NavigationDisplayManualRangeMeters =
                AERISSettings.NormalizeNavigationRange(value.RangeMeters);
            settings.NavigationDisplayTrackUp = value.TrackUp;
            settings.TerrainDisplayMode = value.TerrainMode;
            settings.NavigationDisplayTrailEnabled = value.Trail;
            settings.NavigationDisplayTrackVectorEnabled = value.Vector;
            settings.NavigationDisplayTrafficEnabled = value.Traffic;
            settings.NavigationDisplayWindEnabled = value.Wind;
            settings.NavigationDisplayLandProfileSize = value.LandProfileSize;
        }

        void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                if (!File.Exists(PathName)) return;
                ConfigNode root = ResolveRoot(ConfigNode.Load(PathName));
                if (root == null) return;
                int schema;
                if (!int.TryParse(root.GetValue("schemaVersion"), out schema) ||
                    schema != SchemaVersion) return;
                ConfigNode[] nodes = root.GetNodes("PROFILE");
                for (int i = 0; i < nodes.Length; i++)
                {
                    Profile value = Read(nodes[i]);
                    if (value == null || string.IsNullOrEmpty(value.Signature)) continue;
                    Profile existing;
                    if (!profiles.TryGetValue(value.Signature, out existing) ||
                        value.LastUsedUtcTicks > existing.LastUsedUtcTicks)
                        profiles[value.Signature] = value;
                }
                Trim();
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[ND_PROFILE] load failed; using defaults: " + ex.Message);
                profiles.Clear();
            }
        }

        void SaveFile()
        {
            try
            {
                string directory = Path.GetDirectoryName(PathName);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                ConfigNode root = new ConfigNode("AERIS_ND_PROFILES");
                root.AddValue("schemaVersion", SchemaVersion);
                List<Profile> ordered = new List<Profile>(profiles.Values);
                ordered.Sort((a, b) => b.LastUsedUtcTicks.CompareTo(a.LastUsedUtcTicks));
                for (int i = 0; i < ordered.Count; i++) root.AddNode(Write(ordered[i]));
                string temporary = PathName + ".tmp";
                string backup = PathName + ".bak";
                if (File.Exists(temporary)) File.Delete(temporary);
                root.Save(temporary);
                ConfigNode verified = ResolveRoot(ConfigNode.Load(temporary));
                if (verified == null ||
                    verified.GetNodes("PROFILE").Length != ordered.Count)
                    throw new InvalidDataException("temporary profile round-trip failed");
                if (File.Exists(PathName)) File.Copy(PathName, backup, true);
                if (File.Exists(PathName)) File.Delete(PathName);
                File.Move(temporary, PathName);
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[ND_PROFILE] save failed; flight display continues: " +
                    ex.Message);
            }
        }

        static ConfigNode ResolveRoot(ConfigNode loaded)
        {
            if (loaded == null) return null;
            if (string.Equals(loaded.name, "AERIS_ND_PROFILES",
                StringComparison.Ordinal)) return loaded;
            ConfigNode child = loaded.GetNode("AERIS_ND_PROFILES");
            if (child != null) return child;
            // KSP/Mono ConfigNode.Save may serialize a named root as a generic loaded
            // root whose values and PROFILE children are direct members. Accept that
            // production shape, just as the runway certification cache does.
            if (loaded.HasValue("schemaVersion")) return loaded;
            return null;
        }

        void Trim()
        {
            if (profiles.Count <= MaximumProfiles) return;
            List<Profile> ordered = new List<Profile>(profiles.Values);
            ordered.Sort((a, b) => b.LastUsedUtcTicks.CompareTo(a.LastUsedUtcTicks));
            profiles.Clear();
            int keep = Math.Min(MaximumProfiles, ordered.Count);
            for (int i = 0; i < keep; i++) profiles[ordered[i].Signature] = ordered[i];
        }

        static Profile Read(ConfigNode node)
        {
            if (node == null) return null;
            Profile value = new Profile();
            value.Signature = node.GetValue("signature") ?? string.Empty;
            value.Label = node.GetValue("label") ?? string.Empty;
            long ticks;
            long.TryParse(node.GetValue("lastUsedUtcTicks"), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out ticks);
            value.LastUsedUtcTicks = ticks;
            float range;
            if (float.TryParse(node.GetValue("rangeMeters"), NumberStyles.Float,
                CultureInfo.InvariantCulture, out range))
                value.RangeMeters = AERISSettings.NormalizeNavigationRange(range);
            value.TrackUp = ReadBool(node, "trackUp", true);
            value.Trail = ReadBool(node, "trail", false);
            value.Vector = ReadBool(node, "vector", true);
            value.Traffic = ReadBool(node, "traffic", true);
            value.Wind = ReadBool(node, "wind", true);
            value.TerrainMode = ReadEnum(node, "terrainMode",
                AERISTerrainDisplayMode.Automatic);
            value.LandProfileSize = ReadEnum(node, "landProfileSize",
                AERISNavigationDisplayLandProfileSize.Normal);
            return value;
        }

        static ConfigNode Write(Profile value)
        {
            ConfigNode node = new ConfigNode("PROFILE");
            node.AddValue("signature", value.Signature ?? string.Empty);
            node.AddValue("label", value.Label ?? string.Empty);
            node.AddValue("lastUsedUtcTicks", value.LastUsedUtcTicks.ToString(
                CultureInfo.InvariantCulture));
            node.AddValue("rangeMeters", value.RangeMeters.ToString("F0",
                CultureInfo.InvariantCulture));
            node.AddValue("trackUp", value.TrackUp);
            node.AddValue("terrainMode", value.TerrainMode);
            node.AddValue("trail", value.Trail);
            node.AddValue("vector", value.Vector);
            node.AddValue("traffic", value.Traffic);
            node.AddValue("wind", value.Wind);
            node.AddValue("landProfileSize", value.LandProfileSize);
            return node;
        }

        static bool ReadBool(ConfigNode node, string key, bool fallback)
        {
            bool value;
            return node != null && bool.TryParse(node.GetValue(key), out value) ?
                value : fallback;
        }

        static T ReadEnum<T>(ConfigNode node, string key, T fallback) where T : struct
        {
            T value;
            return node != null && Enum.TryParse(node.GetValue(key), true, out value) ?
                value : fallback;
        }

        static ulong Fnv1A64(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }
            return hash;
        }

        static string Safe(string value)
        {
            return (value ?? string.Empty).Replace("|", "_").Replace(":", "_")
                .Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
