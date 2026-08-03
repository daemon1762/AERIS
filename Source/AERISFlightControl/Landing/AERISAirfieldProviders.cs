using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISProviderFacilityRecord
    {
        internal string Body = "Kerbin";
        internal string DisplayName = "UNNAMED";
        internal string ProviderSiteId = string.Empty;
        internal string ProviderGroup = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderVersion = string.Empty;
        internal string SourceMod = string.Empty;
        internal string SourcePath = string.Empty;
        internal string Description = string.Empty;
        internal AERISAirfieldSource Source = AERISAirfieldSource.Unknown;
        internal AERISFacilityKind FacilityKind = AERISFacilityKind.Unknown;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double ElevationMeters;
        internal bool ProviderReferencePositionValid;
        internal double OrientationHeadingDeg;
        internal double DeclaredLengthMeters;
        internal double DeclaredWidthMeters;
        internal string ProviderCategory = string.Empty;
        internal string ProviderSiteType = string.Empty;
        internal string ModelName = string.Empty;
        internal string LaunchPadTransform = string.Empty;
        internal CelestialBody RuntimeBody;
        internal Transform RuntimeLaunchTransform;
        internal Transform RuntimeInstanceTransform;
        internal Transform RuntimePrefabLaunchTransform;
        internal GameObject RuntimeRunwayObject;
        internal GameObject RuntimeRunwayPrefab;
        internal float RuntimeModelScale = 1f;
        internal bool RuntimeLaunchFrameValid;
        internal Vector3 RuntimeLaunchPosition;
        internal Vector3 RuntimeLaunchForward;
        internal AERISRunwaySurveyDefinition SurveyDefinition;
        internal bool SurveyGeometryValid;
        internal string SurveyRunwayId = string.Empty;
        internal string SurveyStatus = "NOT SURVEYED";

        // v0.18 Gate 0: one physical runway may be exposed through several KSP,
        // Kerbal Konstructs and Stock Launchsites Expansion provider records.
        // Only the canonical record is surveyed; aliases are retained for diagnostics
        // and cache migration, never as independent operational runways.
        internal string PhysicalRunwayId = string.Empty;
        internal bool IsCanonicalPhysicalRunway;
        internal string CanonicalProviderReason = string.Empty;
        internal List<AERISProviderAlias> ProviderAliases = new List<AERISProviderAlias>();

        internal AERISGeoPoint SurveyThresholdA;
        internal AERISGeoPoint SurveyThresholdB;
        internal double SurveyLengthMeters;
        internal double SurveyWidthMeters;
        internal bool IsSquadOwned;
    }

    internal static class AERISKspFacilityProvider
    {
        internal static int Collect(IList<AERISProviderFacilityRecord> output, out string status)
        {
            status = "KSP FACILITIES UNAVAILABLE";
            if (output == null) return 0;
            int before = output.Count;
            try
            {
                object setup = GetStaticMemberValue(typeof(PSystemSetup), "Instance", "instance");
                if (setup == null) return 0;
                object facilities = GetMemberValue(setup, "SpaceCenterFacilities", "spaceCenterFacilities");
                IEnumerable enumerable = facilities as IEnumerable;
                if (enumerable == null) return 0;
                foreach (object facility in enumerable)
                {
                    if (facility == null) continue;
                    string name = FirstNonEmpty(
                        ReadString(facility, "facilityDisplayName"),
                        ReadString(facility, "facilityName"),
                        ReadString(facility, "name"));
                    if (string.IsNullOrEmpty(name)) continue;
                    AERISFacilityKind kind = Classify(name, ReadString(facility, "editorFacility"));
                    if (kind == AERISFacilityKind.Unknown && !LooksLikeRelevantFacility(name)) continue;
                    object bodyObject = GetMemberValue(facility, "hostBody", "HostBody");
                    CelestialBody body = bodyObject as CelestialBody;
                    Transform transform = GetMemberValue(facility, "facilityTransform", "FacilityTransform") as Transform;
                    var record = new AERISProviderFacilityRecord();
                    record.DisplayName = name;
                    record.ProviderSiteId = name;
                    // PSystemSetup contains independent facilities, not one airport
                    // whose members should all be grouped under the literal "KSP".
                    // A facility-stable group prevents every unmatched stock record
                    // from collapsing to DISC_STOCK_KSP.
                    record.ProviderGroup = name;
                    record.Body = body == null ? "Kerbin" : body.name;
                    record.FacilityKind = kind;
                    record.Source = IsDlcName(name) ? AERISAirfieldSource.Dlc : AERISAirfieldSource.Stock;
                    record.SourceMod = record.Source == AERISAirfieldSource.Dlc ? "SquadExpansion" : "Squad";
                    try { record.ProviderVersion = typeof(PSystemSetup).Assembly.GetName().Version.ToString(); }
                    catch { record.ProviderVersion = string.Empty; }
                    record.SourcePath = "PSystemSetup.SpaceCenterFacilities";
                    record.ProviderCategory = kind == AERISFacilityKind.Runway ? "Runway" : kind.ToString();
                    record.IsSquadOwned = true;
                    if (body != null && transform != null)
                    {
                        try
                        {
                            record.LatitudeDeg = body.GetLatitude(transform.position);
                            record.LongitudeDeg = body.GetLongitude(transform.position);
                            record.ElevationMeters = body.GetAltitude(transform.position);
                            record.ProviderReferencePositionValid = true;
                            record.RuntimeBody = body;
                            record.RuntimeLaunchTransform = transform;
                            record.RuntimeInstanceTransform = transform;
                            record.RuntimeRunwayObject = transform.gameObject;
                            record.RuntimeLaunchPosition = transform.position;
                            record.RuntimeLaunchForward = transform.forward;
                            record.RuntimeLaunchFrameValid = transform.forward.sqrMagnitude > 0.5f;
                        }
                        catch { }
                    }
                    output.Add(record);
                }
                int added = output.Count - before;
                status = added + " KSP FACILITY RECORD(S)";
                return added;
            }
            catch (Exception ex)
            {
                status = "KSP FACILITY SCAN FAILED: " + ex.GetType().Name;
                AERISLogger.Warn("[AIRFIELD_PROVIDER][KSP] " + ex.Message);
                return output.Count - before;
            }
        }

        static bool IsDlcName(string value)
        {
            string text = (value ?? string.Empty).ToLowerInvariant();
            return text.Contains("dessert") || text.Contains("woomerang");
        }

        static bool LooksLikeRelevantFacility(string value)
        {
            string text = (value ?? string.Empty).ToLowerInvariant();
            return text.Contains("runway") || text.Contains("airfield") ||
                text.Contains("launch") || text.Contains("helipad") || text.Contains("harbour") ||
                text.Contains("harbor") || text.Contains("cove") || text.Contains("glacier") ||
                text.Contains("crater") || text.Contains("mahi");
        }

        internal static AERISFacilityKind Classify(string name, string category)
        {
            string text = ((name ?? string.Empty) + " " + (category ?? string.Empty)).ToLowerInvariant();
            if (text.Contains("runway") || text.Contains("airfield")) return AERISFacilityKind.Runway;
            if (text.Contains("helipad") || text.Contains("heli pad")) return AERISFacilityKind.Helipad;
            if (text.Contains("waterlaunch") || text.Contains("water launch") ||
                text.Contains("harbour") || text.Contains("harbor") || text.Contains("dock"))
                return AERISFacilityKind.Harbour;
            if (text.Contains("launch") || text.Contains("pad") || text.Contains("vab")) return AERISFacilityKind.LaunchPad;
            return AERISFacilityKind.Unknown;
        }

        internal static object GetMemberValue(object target, params string[] names)
        {
            if (target == null || names == null) return null;
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.IgnoreCase;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(names[i], flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return property.GetValue(target, null);
                }
                catch { }
                try
                {
                    FieldInfo field = type.GetField(names[i], flags);
                    if (field != null) return field.GetValue(target);
                }
                catch { }
            }
            return null;
        }

        internal static object GetFieldValue(object target, params string[] names)
        {
            if (target == null || names == null) return null;
            Type type = target.GetType();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.IgnoreCase;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(names[i], flags);
                    if (field != null) return field.GetValue(target);
                }
                catch { }
            }
            return null;
        }

        internal static object GetStaticMemberValue(Type type, params string[] names)
        {
            if (type == null || names == null) return null;
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.IgnoreCase;
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    PropertyInfo property = type.GetProperty(names[i], flags);
                    if (property != null && property.GetIndexParameters().Length == 0)
                        return property.GetValue(null, null);
                }
                catch { }
                try
                {
                    FieldInfo field = type.GetField(names[i], flags);
                    if (field != null) return field.GetValue(null);
                }
                catch { }
            }
            return null;
        }

        internal static string ReadString(object target, params string[] names)
        {
            object value = GetMemberValue(target, names);
            return value == null ? string.Empty : Convert.ToString(value,
                System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        internal static bool ReadBool(object target, bool fallback, params string[] names)
        {
            object value = GetMemberValue(target, names);
            if (value == null) return fallback;
            try { return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }

        internal static double ReadDouble(object target, double fallback, params string[] names)
        {
            object value = GetMemberValue(target, names);
            if (value == null) return fallback;
            try
            {
                double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                return double.IsNaN(number) || double.IsInfinity(number) ? fallback : number;
            }
            catch { return fallback; }
        }

        static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            for (int i = 0; i < values.Length; i++) if (!string.IsNullOrEmpty(values[i])) return values[i];
            return string.Empty;
        }
    }

    internal static class AERISKerbalKonstructsProvider
    {
        internal static int Collect(IList<AERISProviderFacilityRecord> output, out string status)
        {
            status = "KERBAL KONSTRUCTS NOT DETECTED";
            if (output == null) return 0;
            int before = output.Count;
            try
            {
                Type manager = FindType("KerbalKonstructs.Core.LaunchSiteManager");
                if (manager == null) return 0;
                object sites = AERISKspFacilityProvider.GetStaticMemberValue(manager,
                    "AllLaunchSites", "allLaunchSites");
                IEnumerable enumerable = sites as IEnumerable;
                if (enumerable == null)
                {
                    status = "KK DETECTED / LAUNCH SITE TABLE NOT READY";
                    return 0;
                }
                foreach (object site in enumerable)
                {
                    AERISProviderFacilityRecord record = ParseSite(site);
                    if (record != null && !record.IsSquadOwned) output.Add(record);
                }
                int added = output.Count - before;
                status = "KSP-RO KK ACTIVE / " + added + " NON-STOCK SITE(S)";
                return added;
            }
            catch (Exception ex)
            {
                status = "KK SCAN FAILED: " + ex.GetType().Name;
                AERISLogger.Warn("[AIRFIELD_PROVIDER][KK] " + ex.Message);
                return output.Count - before;
            }
        }

        static AERISProviderFacilityRecord ParseSite(object site)
        {
            if (site == null) return null;
            string name = AERISKspFacilityProvider.ReadString(site, "LaunchSiteName", "FacilityName", "name").Trim();
            if (string.IsNullOrEmpty(name)) return null;
            string category = AERISKspFacilityProvider.ReadString(site, "Category", "sitecategory");
            string siteType = AERISKspFacilityProvider.ReadString(site, "LaunchSiteType");
            object instance = AERISKspFacilityProvider.GetMemberValue(site, "staticInstance", "StaticInstance");
            bool squad = AERISKspFacilityProvider.ReadBool(site, false, "isSquad", "IsSquad");
            string group = AERISKspFacilityProvider.ReadString(instance, "Group", "group").Trim();
            string uuid = AERISKspFacilityProvider.ReadString(instance, "UUID", "uuid").Trim();
            string configPath = AERISKspFacilityProvider.ReadString(instance, "configPath", "ConfigPath");
            object configUrl = AERISKspFacilityProvider.GetMemberValue(instance, "configUrl", "ConfigUrl");
            string configUrlText = AERISKspFacilityProvider.ReadString(configUrl, "url", "Url");
            if (string.IsNullOrEmpty(configPath)) configPath = configUrlText;
            object model = AERISKspFacilityProvider.GetMemberValue(instance, "model", "Model");
            string modelName = AERISKspFacilityProvider.ReadString(model, "name", "Name").Trim();
            if (string.IsNullOrEmpty(modelName))
                modelName = AERISKspFacilityProvider.ReadString(instance, "pointername", "PointerName").Trim();
            string launchPadTransform = AERISKspFacilityProvider.ReadString(site,
                "LaunchPadTransform", "launchPadTransform").Trim();

            // Never call StaticInstance.mesh here: its getter may activate every KK static
            // during a registry scan. Read the backing field and prefab without side effects.
            GameObject runwayObject = ResolveGameObject(AERISKspFacilityProvider.GetFieldValue(instance,
                "_mesh", "meshObject"));
            GameObject instanceObject = ResolveGameObject(AERISKspFacilityProvider.GetFieldValue(instance,
                "gameObject"));
            GameObject runwayPrefab = ResolveGameObject(AERISKspFacilityProvider.GetFieldValue(model,
                "prefab"));
            Transform instanceTransform = instanceObject == null ? null : instanceObject.transform;
            Transform launchTransform = FindTransform(runwayObject, launchPadTransform);
            Transform prefabLaunchTransform = FindTransform(runwayPrefab, launchPadTransform);
            float modelScale = (float)AERISKspFacilityProvider.ReadDouble(instance, 1.0,
                "ModelScale", "modelScale");
            if (modelScale <= 0f || float.IsNaN(modelScale) || float.IsInfinity(modelScale))
                modelScale = 1f;
            Vector3 launchPosition = Vector3.zero;
            Vector3 launchForward = Vector3.forward;
            bool launchFrameValid = TryResolveLaunchFrame(instanceTransform, runwayPrefab,
                prefabLaunchTransform, modelScale, launchTransform, out launchPosition, out launchForward);
            object bodyObject = AERISKspFacilityProvider.GetMemberValue(instance, "CelestialBody", "body");
            if (bodyObject == null) bodyObject = AERISKspFacilityProvider.GetMemberValue(site, "body", "Body");
            CelestialBody body = bodyObject as CelestialBody;
            double lat = AERISKspFacilityProvider.ReadDouble(instance, 361.0, "RefLatitude", "refLatitude");
            double lon = AERISKspFacilityProvider.ReadDouble(instance, 361.0, "RefLongitude", "refLongitude");
            if (Math.Abs(lat) > 90.0) lat = AERISKspFacilityProvider.ReadDouble(site, 361.0, "refLat", "RefLat");
            if (Math.Abs(lon) > 180.0) lon = AERISKspFacilityProvider.ReadDouble(site, 361.0, "refLon", "RefLon");
            double alt = AERISKspFacilityProvider.ReadDouble(site, 0.0, "refAlt", "RefAlt");
            double heading = AERISKspFacilityProvider.ReadDouble(instance, double.NaN,
                "RotationAngle", "rotationAngle", "Heading", "heading");
            if (double.IsNaN(heading) || double.IsInfinity(heading))
            {
                object orientation = AERISKspFacilityProvider.GetMemberValue(instance,
                    "Orientation", "orientation");
                // Legacy fallback only. Current KSP-RO KK exposes RotationAngle.
                heading = AERISKspFacilityProvider.ReadDouble(orientation, 0.0, "y", "Y");
            }

            bool providerReferencePositionValid = Math.Abs(lat) <= 90.0 &&
                Math.Abs(lon) <= 180.0;
            var record = new AERISProviderFacilityRecord();
            record.DisplayName = name;
            record.ProviderSiteId = name;
            record.ProviderGroup = string.IsNullOrEmpty(group) ? name : group;
            record.ProviderUuid = uuid;
            try { record.ProviderVersion = site.GetType().Assembly.GetName().Version.ToString(); }
            catch { record.ProviderVersion = string.Empty; }
            record.SourcePath = configPath;
            record.Description = AERISKspFacilityProvider.ReadString(site, "LaunchSiteDescription", "description");
            record.Body = body == null ? "Kerbin" : body.name;
            record.FacilityKind = AERISKspFacilityProvider.Classify(name, category + " " + siteType);
            record.Source = LooksLikeSle(configPath, group, name)
                ? AERISAirfieldSource.StockLaunchsitesExpansion
                : AERISAirfieldSource.KerbalKonstructs;
            record.SourceMod = record.Source == AERISAirfieldSource.StockLaunchsitesExpansion
                ? "StockLaunchsitesExpansion" : "KerbalKonstructs";
            record.LatitudeDeg = Math.Abs(lat) <= 90.0 ? lat : 0.0;
            record.LongitudeDeg = Math.Abs(lon) <= 180.0 ? lon : 0.0;
            record.ElevationMeters = alt;
            record.ProviderReferencePositionValid = providerReferencePositionValid;
            record.OrientationHeadingDeg = AERISAirfieldConfigParser.NormalizeHeading(heading);
            record.DeclaredLengthMeters = AERISKspFacilityProvider.ReadDouble(site, 0.0,
                "LaunchSiteLength", "launchSiteLength");
            record.DeclaredWidthMeters = AERISKspFacilityProvider.ReadDouble(site, 0.0,
                "LaunchSiteWidth", "launchSiteWidth");
            record.ProviderCategory = category ?? string.Empty;
            record.ProviderSiteType = siteType ?? string.Empty;
            record.ModelName = modelName;
            record.LaunchPadTransform = launchPadTransform;
            record.RuntimeBody = body;
            record.RuntimeLaunchTransform = launchTransform;
            record.RuntimeInstanceTransform = instanceTransform;
            record.RuntimePrefabLaunchTransform = prefabLaunchTransform;
            record.RuntimeRunwayObject = runwayObject;
            record.RuntimeRunwayPrefab = runwayPrefab;
            record.RuntimeModelScale = modelScale;
            record.RuntimeLaunchFrameValid = launchFrameValid;
            record.RuntimeLaunchPosition = launchPosition;
            record.RuntimeLaunchForward = launchForward;
            record.IsSquadOwned = squad;
            return record;
        }

        static Transform FindTransform(GameObject root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path)) return null;
            try { return root.transform.Find(path); }
            catch { return null; }
        }

        static bool TryResolveLaunchFrame(Transform instanceTransform, GameObject prefab,
            Transform prefabLaunch, float modelScale, Transform liveLaunch,
            out Vector3 position, out Vector3 forward)
        {
            position = Vector3.zero;
            forward = Vector3.forward;
            if (liveLaunch != null)
            {
                position = liveLaunch.position;
                forward = liveLaunch.forward.normalized;
                return forward.sqrMagnitude > 0.5f;
            }
            if (instanceTransform == null || prefab == null || prefabLaunch == null) return false;
            try
            {
                Transform prefabRoot = prefab.transform;
                Vector3 localPosition = prefabRoot.InverseTransformPoint(prefabLaunch.position) * modelScale;
                Vector3 localForward = prefabRoot.InverseTransformDirection(prefabLaunch.forward).normalized;
                position = instanceTransform.TransformPoint(localPosition);
                forward = instanceTransform.TransformDirection(localForward).normalized;
                return forward.sqrMagnitude > 0.5f;
            }
            catch { return false; }
        }

        static GameObject ResolveGameObject(object value)
        {
            if (value == null) return null;
            GameObject gameObject = value as GameObject;
            if (gameObject != null) return gameObject;
            Component component = value as Component;
            if (component != null) return component.gameObject;
            Transform transform = value as Transform;
            if (transform != null) return transform.gameObject;
            object nested = AERISKspFacilityProvider.GetMemberValue(value, "gameObject", "GameObject");
            if (ReferenceEquals(nested, value)) return null;
            return ResolveGameObject(nested);
        }

        static bool LooksLikeSle(string path, string group, string name)
        {
            string text = ((path ?? string.Empty) + " " + (group ?? string.Empty) + " " +
                (name ?? string.Empty)).ToLowerInvariant();
            return text.Contains("/sle/") || text.Contains("\\sle\\") ||
                text.StartsWith("sle/") || text.Contains("stock launchsites expansion");
        }

        static Type FindType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(fullName, false, false);
                    if (type != null) return type;
                }
                catch { }
            }
            return null;
        }
    }
}
