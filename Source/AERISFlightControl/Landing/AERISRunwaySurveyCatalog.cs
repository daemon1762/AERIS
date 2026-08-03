using System;
using System.Collections.Generic;
using System.Globalization;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISRunwaySurveyCatalog
    {
        readonly List<AERISRunwaySurveyDefinition> entries =
            new List<AERISRunwaySurveyDefinition>();

        internal IList<AERISRunwaySurveyDefinition> Entries { get { return entries.AsReadOnly(); } }
        internal int Count { get { return entries.Count; } }

        internal static AERISRunwaySurveyCatalog Load(string[] paths)
        {
            var catalog = new AERISRunwaySurveyCatalog();
            if (paths == null) return catalog;
            for (int i = 0; i < paths.Length; i++) catalog.ParseFile(paths[i]);
            return catalog;
        }

        internal AERISRunwaySurveyDefinition Match(AERISProviderFacilityRecord record)
        {
            if (record == null) return null;
            for (int i = 0; i < entries.Count; i++)
            {
                AERISRunwaySurveyDefinition value = entries[i];
                if (value != null && value.Matches(record.ProviderUuid, record.ProviderSiteId,
                    record.ProviderGroup, record.SourcePath, record.ModelName)) return value;
            }
            return null;
        }

        internal AERISRunwaySurveyDefinition MatchPhysical(
            AERISProviderFacilityRecord record)
        {
            AERISRunwaySurveyDefinition direct = Match(record);
            if (direct != null || record == null || record.ProviderAliases == null)
                return direct;
            for (int i = 0; i < entries.Count; i++)
            {
                AERISRunwaySurveyDefinition value = entries[i];
                if (value == null) continue;
                for (int j = 0; j < record.ProviderAliases.Count; j++)
                {
                    AERISProviderAlias alias = record.ProviderAliases[j];
                    if (alias != null && value.Matches(alias.ProviderUuid,
                        alias.ProviderSiteId, alias.ProviderGroup, alias.SourcePath,
                        alias.ModelName)) return value;
                }
            }
            return null;
        }

        void ParseFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                ConfigNode loaded = ConfigNode.Load(path);
                if (loaded == null) return;
                if (string.Equals(loaded.name, "AERISRunwaySurveyCatalog",
                    StringComparison.OrdinalIgnoreCase)) ParseRoot(loaded, path);
                ConfigNode[] roots = loaded.GetNodes("AERISRunwaySurveyCatalog");
                if (roots != null)
                    for (int i = 0; i < roots.Length; i++) ParseRoot(roots[i], path);
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[RUNWAY_SURVEY] catalog parse failed: " + path + "; " + ex.Message);
            }
        }

        void ParseRoot(ConfigNode root, string path)
        {
            if (root == null) return;
            ConfigNode[] nodes = root.GetNodes("RunwaySurvey");
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
            {
                AERISRunwaySurveyDefinition value = ParseEntry(nodes[i]);
                if (value == null) continue;
                bool duplicate = false;
                for (int j = 0; j < entries.Count; j++)
                    if (string.Equals(entries[j].Id, value.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        duplicate = true;
                        break;
                    }
                if (duplicate)
                {
                    AERISLogger.Warn("[RUNWAY_SURVEY] duplicate catalog id ignored: " + value.Id +
                        " from " + path);
                    continue;
                }
                entries.Add(value);
            }
        }

        static AERISRunwaySurveyDefinition ParseEntry(ConfigNode node)
        {
            if (node == null) return null;
            var value = new AERISRunwaySurveyDefinition();
            value.Id = ReadString(node, "id", string.Empty).Trim();
            value.ProviderUuid = ReadString(node, "providerUUID", string.Empty).Trim();
            value.ProviderSiteId = ReadString(node, "providerSiteId", string.Empty).Trim();
            value.ProviderGroup = ReadString(node, "providerGroup", string.Empty).Trim();
            value.SourcePathContains = ReadString(node, "sourcePathContains", string.Empty).Trim();
            value.ModelName = ReadString(node, "modelName", string.Empty).Trim();
            value.Method = ReadEnum(node, "method", AERISRunwaySurveyMethod.ManualRequired);
            value.PairKey = ReadString(node, "pairKey", string.Empty).Trim();
            value.Surface = ReadString(node, "surface", "PAVED").Trim();
            value.SourceMod = ReadString(node, "sourceMod", string.Empty).Trim();
            value.Notes = ReadString(node, "notes", string.Empty).Trim();
            ReadDouble(node, "minimumLength", ref value.MinimumLengthMeters);
            ReadDouble(node, "maximumLength", ref value.MaximumLengthMeters);
            ReadDouble(node, "minimumWidth", ref value.MinimumWidthMeters);
            ReadDouble(node, "maximumWidth", ref value.MaximumWidthMeters);
            ReadDouble(node, "minimumAspectRatio", ref value.MinimumAspectRatio);
            ReadDouble(node, "defaultWidth", ref value.DefaultWidthMeters);
            if (string.IsNullOrEmpty(value.Id) ||
                (string.IsNullOrEmpty(value.ProviderUuid) && string.IsNullOrEmpty(value.ProviderSiteId)))
                return null;
            if (value.Method == AERISRunwaySurveyMethod.PairedThresholds &&
                string.IsNullOrEmpty(value.PairKey)) return null;
            return value;
        }

        static string ReadString(ConfigNode node, string key, string fallback)
        {
            return node != null && node.HasValue(key) ? node.GetValue(key) ?? fallback : fallback;
        }

        static T ReadEnum<T>(ConfigNode node, string key, T fallback) where T : struct
        {
            if (node == null || !node.HasValue(key)) return fallback;
            T value;
            return Enum.TryParse<T>(node.GetValue(key), true, out value) ? value : fallback;
        }

        static void ReadDouble(ConfigNode node, string key, ref double value)
        {
            if (node == null || !node.HasValue(key)) return;
            double parsed;
            if (double.TryParse(node.GetValue(key), NumberStyles.Float,
                CultureInfo.InvariantCulture, out parsed) && !double.IsNaN(parsed) &&
                !double.IsInfinity(parsed)) value = parsed;
        }
    }
}
