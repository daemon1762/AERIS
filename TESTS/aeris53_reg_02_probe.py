#!/usr/bin/env python3
"""Exercise the temporary observer against controlled KSP/KK boundaries."""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
HARNESS = r'''
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine {
    public class MonoBehaviour { }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug { public static void Log(string s) { } public static void LogError(string s) { } }
}
public class KSPAddon : Attribute { public enum Startup { Flight } public KSPAddon(Startup s, bool once) { } }
public static class FlightGlobals { public static bool ready; }
public static class KSPUtil { public static string ApplicationRootPath; }
public class FixtureTransform {
    public string name;
    public FixtureTransform parent;
}
public class Facility {
    public string facilityDisplayName = "Example Runway", facilityName = "Example Runway";
    public string name = "Internal Facility", pqsName = "Kerbin", facilityTransformName = "KSC/Runway";
    public object facilityTransform = new object();
    public string source = "Stock";
    public bool squad = true;
}
public class PSystemSetup {
    public static PSystemSetup Instance = new PSystemSetup();
    public Facility[] SpaceCenterFacilities;
}
#if WITH_KK
namespace KerbalKonstructs.Core {
    public class Site {
        public string LaunchSiteName = "Example Runway";
        public bool isSquad;
        public Facility spaceCenterFacility;
    }
    public static class LaunchSiteManager { public static List<object> AllLaunchSites = new List<object>(); }
}
#endif
namespace AERISFlightControl.Landing {
    public class AERISProviderFacilityRecord {
        public string DisplayName, ProviderSiteId, Source, Body = "Kerbin", FacilityKind = "Runway";
        public bool IsSquadOwned;
        public object RuntimeInstanceTransform;
        public List<AERISProviderFacilityRecord> ProviderAliases = new List<AERISProviderFacilityRecord>();
    }
    public static class AERISKspFacilityProvider {
        public static void Collect(IList<AERISProviderFacilityRecord> output, out string status) {
            foreach (Facility facility in PSystemSetup.Instance.SpaceCenterFacilities)
                output.Add(new AERISProviderFacilityRecord { DisplayName = facility.facilityDisplayName,
                    ProviderSiteId = facility.facilityDisplayName, Source = facility.source,
                    IsSquadOwned = facility.squad, RuntimeInstanceTransform = facility.facilityTransform });
            status = output.Count + " KSP";
        }
    }
    public static class AERISKerbalKonstructsProvider {
        public static void Collect(IList<AERISProviderFacilityRecord> output, out string status) {
#if WITH_KK
            foreach (object value in KerbalKonstructs.Core.LaunchSiteManager.AllLaunchSites) {
                var site = value as KerbalKonstructs.Core.Site;
                if (site != null && !site.isSquad)
                    output.Add(new AERISProviderFacilityRecord { DisplayName = site.LaunchSiteName,
                        ProviderSiteId = site.LaunchSiteName, Source = "KerbalKonstructs", IsSquadOwned = false });
            }
#endif
            status = output.Count + " KK";
        }
    }
    public static class AERISPhysicalRunwayIdentity {
        public static bool DuplicateAliasFixture;
        public static List<AERISProviderFacilityRecord> Canonicalize(IList<AERISProviderFacilityRecord> input, out string summary) {
            var result = new List<AERISProviderFacilityRecord>();
            foreach (var raw in input) {
                AERISProviderFacilityRecord match = result.Find(x => x.DisplayName == raw.DisplayName);
                if (match == null) { result.Add(raw); raw.ProviderAliases.Add(raw); }
                else { match.ProviderAliases.Add(raw); }
            }
            if (DuplicateAliasFixture && input.Count > 1) {
                var duplicate = new AERISProviderFacilityRecord { DisplayName = input[0].DisplayName, Source = "Stock" };
                duplicate.ProviderAliases.Add(input[1]); result.Add(duplicate);
            }
            summary = "test provider federation";
            return result;
        }
    }
    public static class AERISAirfieldRegistry {
        public static bool AutomaticCertificationAllowed(AERISProviderFacilityRecord r) { return r.Source == "Stock"; }
    }
}
class Test {
    static int passed, failed;
    static void Contains(string report, string expected) {
        if (!report.Contains(expected)) throw new Exception("missing: " + expected + "\n" + report);
    }
    static void Run(string name, Action test) {
        try { test(); passed++; Console.WriteLine("PASS: " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL: " + name + " -- " + e.Message); }
    }
    static void Throws(Action action) {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("must fail inconclusively instead of reporting a result");
    }
    static int Main() {
        var facility = new Facility();
        PSystemSetup.Instance.SpaceCenterFacilities = new[] { facility };
#if WITH_KK
        var site = new KerbalKonstructs.Core.Site { spaceCenterFacility = facility };
        var table = KerbalKonstructs.Core.LaunchSiteManager.AllLaunchSites;
        table.Add(site);
        Run("real object link identifies non-stock misclassification", () => {
            string report = Reg02Observation.Capture();
            Contains(report, "REG-02 RESULT=UPSTREAM_MISCLASSIFICATION_REPRODUCED");
            Contains(report, "MISCLASSIFIED_STOCK=1; PROVIDER_PAIR_ELIGIBLE=0; ISOLATED_KSP_ONLY_ELIGIBLE=1");
            Contains(report, "not that an approach was certified");
            if (table.Count != 1 || !ReferenceEquals(table[0], site) ||
                !ReferenceEquals(PSystemSetup.Instance.SpaceCenterFacilities[0], facility) ||
                facility.source != "Stock" || site.isSquad)
                throw new Exception("observer mutated live inputs");
        });
        Run("name coincidence without an object link is not reproduction", () => {
            site.spaceCenterFacility = new Facility();
            Contains(Reg02Observation.Capture(), "REG-02 RESULT=INCONCLUSIVE_NO_LINKED_NON_STOCK_RUNWAY");
            site.spaceCenterFacility = facility;
        });
        Run("ambiguous physical clusters cannot create false authority evidence", () => {
            AERISFlightControl.Landing.AERISPhysicalRunwayIdentity.DuplicateAliasFixture = true;
            try { Throws(() => Reg02Observation.Capture()); }
            finally { AERISFlightControl.Landing.AERISPhysicalRunwayIdentity.DuplicateAliasFixture = false; }
        });
        Run("non-stock classification cannot be reported as Stock", () => {
            facility.source = "KerbalKonstructs"; facility.squad = false;
            string report = Reg02Observation.Capture();
            Contains(report, "REG-02 RESULT=NO_MISCLASSIFICATION_OBSERVED");
            Contains(report, "MISCLASSIFIED_STOCK=0; PROVIDER_PAIR_ELIGIBLE=0; ISOLATED_KSP_ONLY_ELIGIBLE=0");
            facility.source = "Stock"; facility.squad = true;
        });
        Run("Squad sites are not counted as mod runways", () => {
            site.isSquad = true;
            Contains(Reg02Observation.Capture(), "NON_STOCK_LINKED_RUNWAYS=0; MISCLASSIFIED_STOCK=0");
            site.isSquad = false;
        });
        Run("an empty KK table is inconclusive", () => {
            table.Clear();
            Contains(Reg02Observation.Capture(), "REG-02 RESULT=INCONCLUSIVE_NO_LINKED_NON_STOCK_RUNWAY");
            table.Add(site);
        });
        Run("raw origin metadata uses object linkage and preserves internal IDs", () => {
            string report = Reg02Observation.Capture();
            Contains(report, "READ-ONLY REPRO v2");
            Contains(report, "ORIGIN\tNON_SQUAD\tInternal Facility\tExample Runway\tExample Runway\tKerbin\tKSC/Runway\t");
            site.spaceCenterFacility = new Facility();
            try { Contains(Reg02Observation.Capture(), "ORIGIN\tUNLINKED\tInternal Facility\t"); }
            finally { site.spaceCenterFacility = facility; }
        });
        Run("unknown KK ownership fields cannot silently report success", () => {
            table.Clear(); table.Add(new object());
            Throws(() => Reg02Observation.Capture());
            table.Clear(); table.Add(site);
        });
#else
        Run("KK absent produces a distinct baseline result", () => {
            string report = Reg02Observation.Capture();
            Contains(report, "KK_DETECTED=False; KK_SITES=0");
            Contains(report, "REG-02 RESULT=KK_NOT_PRESENT_BASELINE");
            Contains(report, "ORIGIN\tUNLINKED\tInternal Facility\tExample Runway\tExample Runway\tKerbin\tKSC/Runway\t");
        });
#endif
        Run("origin hierarchy is ordered and reports a truncated root", () => {
            object original = facility.facilityTransform;
            try {
                var root = new FixtureTransform { name = "Root" };
                facility.facilityTransform = new FixtureTransform { name = "Leaf\tName", parent = root };
                Contains(Reg02Observation.Capture(), "Root/Leaf Name");
                for (int i = 0; i < 9; i++) root = new FixtureTransform { name = "Node", parent = root };
                facility.facilityTransform = root;
                Contains(Reg02Observation.Capture(), "<TRUNCATED>/");
            } finally { facility.facilityTransform = original; }
        });
        Run("unready facilities are inconclusive", () => {
            PSystemSetup.Instance.SpaceCenterFacilities = null;
            Throws(() => Reg02Observation.Capture());
        });
        Console.WriteLine("REG-02 OBSERVER " + passed + "/" + (passed + failed) + " PASS");
        return failed == 0 ? 0 : 1;
    }
}
'''

with tempfile.TemporaryDirectory(prefix="aeris-reg02-") as folder:
    folder = Path(folder)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS)
    for mode in ("with-kk", "without-kk"):
        exe = folder / (mode + ".exe")
        command = ["mcs", "-out:" + str(exe), str(ROOT / "TESTS/Runtime/AERIS53Reg02Probe.cs"), str(fixture)]
        if mode == "with-kk":
            command.append("-define:WITH_KK")
        subprocess.run(command, check=True)
        subprocess.run(["mono", str(exe)], check=True)
