using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// Temporary, one-shot observation. No GUI, input hooks, registry writes or surveys.
[KSPAddon(KSPAddon.Startup.Flight, false)]
public sealed class AERIS53Reg02Probe : MonoBehaviour
{
    static bool completed;
    float started;
    void Start() { started = Time.realtimeSinceStartup; }
    void Update()
    {
        if (completed || !FlightGlobals.ready || Time.realtimeSinceStartup - started < 5f) return;
        completed = true;
        string report;
        try { report = Reg02Observation.Capture(); }
        catch (Exception e) { report = "REG-02 RESULT=INCONCLUSIVE\n" + e + "\n"; }
        string path = Path.Combine(KSPUtil.ApplicationRootPath, "AERIS53_REG02_repro.txt");
        try
        {
            File.WriteAllText(path, report, new UTF8Encoding(false));
            Debug.Log("[AERIS53_REG02_PROBE] one-shot report saved: " + path);
        }
        catch (Exception e) { Debug.LogError("[AERIS53_REG02_PROBE] report write failed: " + e); }
    }
}

internal static class Reg02Observation
{
    const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    internal static object Member(object target, string name)
    {
        if (target == null) return null;
        Type type = target as Type ?? target.GetType();
        object instance = target is Type ? null : target;
        FieldInfo field = type.GetField(name, Flags);
        if (field != null) return field.GetValue(instance);
        PropertyInfo property = type.GetProperty(name, Flags);
        return property == null ? null : property.GetValue(instance, null);
    }
    static string Text(object target, string name) { return Convert.ToString(Member(target, name)) ?? ""; }
    static string Safe(string text) { return text.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '); }
    static Type Find(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(name, false);
            if (type != null) return type;
        }
        return null;
    }
    static IList Collect(Type provider, Type record, out string status)
    {
        if (provider == null || record == null) throw new InvalidOperationException("AERIS provider types missing");
        IList records = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(record));
        object[] args = { records, null };
        provider.GetMethod("Collect", Flags).Invoke(null, args);
        status = Convert.ToString(args[1]);
        return records;
    }
    static IList Canonicalize(Type identity, IList input)
    {
        object[] args = { input, null };
        return (IList)identity.GetMethod("Canonicalize", Flags).Invoke(null, args);
    }
    static bool Allowed(Type registry, object record)
    {
        return (bool)registry.GetMethod("AutomaticCertificationAllowed", Flags).Invoke(null, new[] { record });
    }
    static bool Includes(IList facilities, object facility)
    {
        foreach (object item in facilities) if (ReferenceEquals(item, facility)) return true;
        return false;
    }
    static object RecordFor(IList records, object facility)
    {
        object found = null;
        string name = Text(facility, "facilityDisplayName");
        if (name.Length == 0) name = Text(facility, "facilityName");
        object transform = Member(facility, "facilityTransform");
        foreach (object record in records)
        {
            if (Text(record, "DisplayName") != name) continue;
            object frame = Member(record, "RuntimeInstanceTransform");
            if (frame != null && !ReferenceEquals(frame, transform)) continue;
            if (found != null) throw new InvalidOperationException("Ambiguous provider record: " + name);
            found = record;
        }
        return found;
    }
    static bool CanonicalAllowed(Type registry, IList canonical, object raw)
    {
        foreach (object candidate in canonical)
            if (ReferenceEquals(candidate, raw)) return Allowed(registry, candidate);
        object matched = null;
        foreach (object candidate in canonical)
        {
            if (Text(candidate, "Body") != Text(raw, "Body")) continue;
            IEnumerable aliases = Member(candidate, "ProviderAliases") as IEnumerable;
            if (aliases == null) continue;
            foreach (object alias in aliases)
                if (Text(alias, "Source") == Text(raw, "Source") &&
                    Text(alias, "ProviderSiteId") == Text(raw, "ProviderSiteId") &&
                    Text(alias, "DisplayName") == Text(raw, "DisplayName"))
                {
                    if (matched != null && !ReferenceEquals(matched, candidate))
                        throw new InvalidOperationException("Ambiguous physical cluster: " + Text(raw, "DisplayName"));
                    matched = candidate;
                }
        }
        if (matched == null) throw new InvalidOperationException("Missing physical cluster: " + Text(raw, "DisplayName"));
        return Allowed(registry, matched);
    }
    internal static string Capture()
    {
        Type setup = Find("PSystemSetup");
        object runtime = Member(setup, "Instance");
        IList facilities = Member(runtime, "SpaceCenterFacilities") as IList;
        if (facilities == null || facilities.Count == 0) throw new InvalidOperationException("KSP facilities not ready");
        Type manager = Find("KerbalKonstructs.Core.LaunchSiteManager");
        IEnumerable table = Member(manager, "AllLaunchSites") as IEnumerable;
        var sites = new List<object>();
        if (table != null) foreach (object site in table) if (site != null) sites.Add(site);
        Type recordType = Find("AERISFlightControl.Landing.AERISProviderFacilityRecord");
        Type ksp = Find("AERISFlightControl.Landing.AERISKspFacilityProvider");
        Type kk = Find("AERISFlightControl.Landing.AERISKerbalKonstructsProvider");
        Type identity = Find("AERISFlightControl.Landing.AERISPhysicalRunwayIdentity");
        Type registry = Find("AERISFlightControl.Landing.AERISAirfieldRegistry");
        string kspStatus, kkStatus;
        IList kspRecords = Collect(ksp, recordType, out kspStatus);
        IList kkRecords = Collect(kk, recordType, out kkStatus);
        // These lists contain newly collected detached records, not the live registry.
        IList combined = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(recordType));
        foreach (object item in kkRecords) combined.Add(item);
        foreach (object item in kspRecords) combined.Add(item);
        IList canonical = Canonicalize(identity, combined);
        // A second fresh collection models a missing KK twin without removing real sites.
        string isolatedStatus;
        IList isolatedInput = Collect(ksp, recordType, out isolatedStatus);
        IList isolatedCanonical = Canonicalize(identity, isolatedInput);
        var report = new StringBuilder();
        report.AppendLine("AERIS53 REG-02 READ-ONLY REPRO v1");
        report.AppendLine("UTC=" + DateTime.UtcNow.ToString("o"));
        report.AppendLine("AERIS_ASSEMBLY=" + recordType.Assembly.Location);
        report.AppendLine("KK_DETECTED=" + (manager != null) + "; KK_SITES=" + sites.Count);
        report.AppendLine("KSP=" + kspStatus + "; KK=" + kkStatus);
        report.AppendLine("ELIGIBLE means the automatic-certification gate accepts a record, not that an approach was certified.");
        report.AppendLine("PROVIDER_PAIR uses detached KSP+KK records without the survey catalog; it is not the live registry.");
        report.AppendLine("ISOLATED_KSP_ONLY is a detached missing-twin test; the live KK table is never changed.");
        report.AppendLine("SITE\tKK_SQUAD\tLINKED_KSP_FACILITY\tAERIS_SOURCE\tAERIS_SQUAD\tKIND\tPROVIDER_PAIR_ELIGIBLE\tISOLATED_KSP_ONLY_ELIGIBLE");
        int linkedNonStockRunways = 0, misclassified = 0, normalEligible = 0, isolatedEligible = 0;
        foreach (object site in sites)
        {
            object squad = Member(site, "isSquad");
            if (!(squad is bool)) throw new InvalidOperationException("KK isSquad field unavailable");
            object facility = Member(site, "spaceCenterFacility");
            bool linked = facility != null && Includes(facilities, facility);
            object raw = linked ? RecordFor(kspRecords, facility) : null;
            object isolated = linked ? RecordFor(isolatedInput, facility) : null;
            bool normal = raw != null && CanonicalAllowed(registry, canonical, raw);
            bool withoutTwin = isolated != null && CanonicalAllowed(registry, isolatedCanonical, isolated);
            bool runway = raw != null && Text(raw, "FacilityKind") == "Runway";
            if (!(bool)squad && linked && runway)
            {
                linkedNonStockRunways++;
                if (Text(raw, "Source") == "Stock" && (bool)Member(raw, "IsSquadOwned")) misclassified++;
                if (normal) normalEligible++;
                if (withoutTwin) isolatedEligible++;
            }
            report.AppendLine(Safe(Text(site, "LaunchSiteName")) + "\t" + squad + "\t" + linked + "\t" +
                (raw == null ? "NO_AERIS_RECORD" : Text(raw, "Source")) + "\t" +
                (raw == null ? "NA" : Text(raw, "IsSquadOwned")) + "\t" +
                (raw == null ? "NA" : Text(raw, "FacilityKind")) + "\t" + normal + "\t" + withoutTwin);
        }
        report.AppendLine("NON_STOCK_LINKED_RUNWAYS=" + linkedNonStockRunways + "; MISCLASSIFIED_STOCK=" + misclassified +
            "; PROVIDER_PAIR_ELIGIBLE=" + normalEligible + "; ISOLATED_KSP_ONLY_ELIGIBLE=" + isolatedEligible);
        string result = manager == null ? "KK_NOT_PRESENT_BASELINE" :
            linkedNonStockRunways == 0 ? "INCONCLUSIVE_NO_LINKED_NON_STOCK_RUNWAY" :
            misclassified > 0 ? "UPSTREAM_MISCLASSIFICATION_REPRODUCED" : "NO_MISCLASSIFICATION_OBSERVED";
        report.AppendLine("REG-02 RESULT=" + result);
        return report.ToString();
    }
}
