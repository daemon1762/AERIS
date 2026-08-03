using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Settings;

namespace AERISFlightControl.FlightPlans
{
    // Data-only compatibility model. It parses the established AERIS FlightPlan CFG
    // schema but has no control, guidance, sequencing, landing or AP-write behavior.
    internal sealed class AERISFlightPlanFix
    {
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double AltitudeMeters;
        internal string Name = "WAYPOINT";
        internal bool Vertical;
        internal bool IafReference;
        internal bool FafReference;
        internal bool RunwayReference;
        internal bool StopReference;

        internal string FlagsText
        {
            get
            {
                string text = string.Empty;
                Append(ref text, "VERT", Vertical);
                Append(ref text, "IAF-REF", IafReference);
                Append(ref text, "FAF-REF", FafReference);
                Append(ref text, "RW-REF", RunwayReference);
                Append(ref text, "STOP-REF", StopReference);
                return text.Length == 0 ? "NONE" : text;
            }
        }

        static void Append(ref string text, string value, bool enabled)
        {
            if (!enabled) return;
            if (text.Length > 0) text += "+";
            text += value;
        }
    }

    internal sealed class AERISFlightPlanDefinition
    {
        internal string Body = string.Empty;
        internal string Name = "UNNAMED";
        internal string Description = string.Empty;
        internal string SourcePath = string.Empty;
        internal readonly List<AERISFlightPlanFix> Fixes = new List<AERISFlightPlanFix>();
        internal string StableId { get { return (Body ?? string.Empty) + "\n" + (Name ?? string.Empty); } }
    }

    internal sealed class AERISFlightPlanLibrary
    {
        const string RelativeDirectory = "GameData/AERISFlightControl/FlightPlans";
        readonly AERISSettings settings;
        readonly List<AERISFlightPlanDefinition> plans = new List<AERISFlightPlanDefinition>();

        internal AERISFlightPlanLibrary(AERISSettings settings)
        {
            this.settings = settings;
        }

        internal IList<AERISFlightPlanDefinition> Plans { get { return plans.AsReadOnly(); } }
        internal int Count { get { return plans.Count; } }
        internal int SelectedIndex { get; private set; } = -1;
        internal string Status { get; private set; } = "NOT LOADED";
        internal AERISFlightPlanDefinition Selected
        {
            get { return SelectedIndex >= 0 && SelectedIndex < plans.Count ? plans[SelectedIndex] : null; }
        }

        internal void Reload()
        {
            plans.Clear();
            SelectedIndex = -1;
            string directory = ResolvePath(RelativeDirectory);
            try { Directory.CreateDirectory(directory); }
            catch (Exception ex)
            {
                Status = "LIBRARY UNAVAILABLE";
                AERISLogger.Warn("[FLIGHT_PLAN_LIBRARY] directory unavailable: " + ex.Message);
                return;
            }

            string[] files;
            try { files = Directory.GetFiles(directory, "*.cfg", SearchOption.AllDirectories); }
            catch (Exception ex)
            {
                Status = "SCAN FAILED";
                AERISLogger.Warn("[FLIGHT_PLAN_LIBRARY] scan failed: " + ex.Message);
                return;
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var dedupe = new Dictionary<string, AERISFlightPlanDefinition>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files.Length; i++) LoadFile(files[i], dedupe);
            foreach (AERISFlightPlanDefinition plan in dedupe.Values) plans.Add(plan);
            plans.Sort(delegate(AERISFlightPlanDefinition a, AERISFlightPlanDefinition b)
            {
                int body = string.Compare(a.Body, b.Body, StringComparison.OrdinalIgnoreCase);
                return body != 0 ? body : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            RestoreSelection();
            Status = plans.Count == 0 ? "NO FLIGHT PLANS" : plans.Count + " PLAN(S) LOADED — DATA ONLY";
            AERISLogger.Info("[FLIGHT_PLAN_LIBRARY] " + Status + "; no guidance authority is present.");
        }

        internal bool Select(int index)
        {
            if (index < 0 || index >= plans.Count) return false;
            SelectedIndex = index;
            AERISFlightPlanDefinition plan = plans[index];
            if (settings != null)
            {
                settings.FlightPlanSelectedBody = plan.Body ?? string.Empty;
                settings.FlightPlanSelectedId = plan.StableId;
                settings.Save();
            }
            return true;
        }

        internal AERISFlightPlanDefinition At(int index)
        {
            return index >= 0 && index < plans.Count ? plans[index] : null;
        }

        void RestoreSelection()
        {
            if (plans.Count == 0) return;
            string id = settings == null ? string.Empty : settings.FlightPlanSelectedId;
            string body = settings == null ? string.Empty : settings.FlightPlanSelectedBody;
            for (int i = 0; i < plans.Count; i++)
            {
                if (!string.IsNullOrEmpty(id) && string.Equals(plans[i].StableId, id, StringComparison.OrdinalIgnoreCase))
                { SelectedIndex = i; return; }
            }
            for (int i = 0; i < plans.Count; i++)
            {
                if (!string.IsNullOrEmpty(body) && string.Equals(plans[i].Body, body, StringComparison.OrdinalIgnoreCase))
                { SelectedIndex = i; return; }
            }
            SelectedIndex = 0;
        }

        void LoadFile(string path, Dictionary<string, AERISFlightPlanDefinition> dedupe)
        {
            try
            {
                ConfigNode loaded = ConfigNode.Load(path);
                if (loaded == null) return;
                if (string.Equals(loaded.name, "AERISFlightPlans", StringComparison.OrdinalIgnoreCase))
                    ParseRoot(loaded, path, dedupe);
                ConfigNode[] roots = loaded.GetNodes("AERISFlightPlans");
                if (roots != null) for (int i = 0; i < roots.Length; i++) ParseRoot(roots[i], path, dedupe);
                if (LooksLikePlanRoot(loaded)) ParseRoot(loaded, path, dedupe);
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[FLIGHT_PLAN_LIBRARY] parse failed: " + path + "; " + ex.Message);
            }
        }

        static bool LooksLikePlanRoot(ConfigNode node)
        {
            if (node == null) return false;
            if (node.HasNode("FlightPlan")) return true;
            foreach (ConfigNode child in node.nodes)
                if (child != null && child.HasNode("FlightPlan")) return true;
            return false;
        }

        static void ParseRoot(ConfigNode root, string path,
            Dictionary<string, AERISFlightPlanDefinition> dedupe)
        {
            if (root == null) return;
            ParsePlans(root, string.Empty, path, dedupe);
            foreach (ConfigNode bodyNode in root.nodes)
            {
                if (bodyNode == null || string.Equals(bodyNode.name, "FlightPlan", StringComparison.OrdinalIgnoreCase)) continue;
                ParsePlans(bodyNode, bodyNode.name, path, dedupe);
            }
        }

        static void ParsePlans(ConfigNode parent, string bodyHint, string path,
            Dictionary<string, AERISFlightPlanDefinition> dedupe)
        {
            ConfigNode[] nodes = parent.GetNodes("FlightPlan");
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
            {
                AERISFlightPlanDefinition plan = ParsePlan(nodes[i], bodyHint, path);
                if (plan == null || plan.Fixes.Count == 0 || dedupe.ContainsKey(plan.StableId)) continue;
                dedupe.Add(plan.StableId, plan);
            }
        }

        static AERISFlightPlanDefinition ParsePlan(ConfigNode node, string bodyHint, string path)
        {
            if (node == null) return null;
            var plan = new AERISFlightPlanDefinition();
            plan.Body = ReadString(node, "planet", bodyHint);
            plan.Name = ReadString(node, "name", "UNNAMED");
            plan.Description = ReadString(node, "description", string.Empty);
            plan.SourcePath = path ?? string.Empty;
            ConfigNode fixes = node.GetNode("WayPoints") ?? node.GetNode("Waypoints");
            if (fixes == null) return plan;
            ConfigNode[] nodes = fixes.GetNodes("WayPoint");
            if (nodes == null || nodes.Length == 0) nodes = fixes.GetNodes("Waypoint");
            if (nodes == null) return plan;
            for (int i = 0; i < nodes.Length; i++)
            {
                double lat, lon, alt;
                if (!ReadDouble(nodes[i], "lat", out lat) || !ReadDouble(nodes[i], "lon", out lon)) continue;
                if (!ReadDouble(nodes[i], "alt", out alt)) alt = 0.0;
                var fix = new AERISFlightPlanFix();
                fix.LatitudeDeg = Math.Max(-90.0, Math.Min(90.0, lat));
                fix.LongitudeDeg = NormalizeLongitude(lon);
                fix.AltitudeMeters = Math.Max(0.0, Math.Min(1000000.0, alt));
                fix.Name = ReadString(nodes[i], "name", "WP" + (plan.Fixes.Count + 1));
                fix.Vertical = ReadBool(nodes[i], "Vertical", false);
                fix.IafReference = ReadBool(nodes[i], "IAF", false);
                fix.FafReference = ReadBool(nodes[i], "FAF", false);
                fix.RunwayReference = ReadBool(nodes[i], "RW", false);
                fix.StopReference = ReadBool(nodes[i], "Stop", false);
                plan.Fixes.Add(fix);
            }
            return plan;
        }

        static string ResolvePath(string relative)
        {
            string root = KSPUtil.ApplicationRootPath;
            return Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        }
        static string ReadString(ConfigNode node, string key, string fallback)
        { return node != null && node.HasValue(key) ? node.GetValue(key) ?? fallback : fallback; }
        static bool ReadBool(ConfigNode node, string key, bool fallback)
        { bool value; return node != null && node.HasValue(key) && bool.TryParse(node.GetValue(key), out value) ? value : fallback; }
        static bool ReadDouble(ConfigNode node, string key, out double value)
        {
            value = 0.0;
            return node != null && node.HasValue(key) &&
                double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value);
        }
        static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }
    }
}
