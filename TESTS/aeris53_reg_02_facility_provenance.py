#!/usr/bin/env python3
"""Run real providers, federation and auto-certification gate at KSP/KK boundaries.

Requires mcs/mono. No fixture DLL is installed in GameData.
"""
from pathlib import Path
import json
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
LANDING = ROOT / "Source/AERISFlightControl/Landing"


def block(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


models = (LANDING / "AERISAirfieldModels.cs").read_text()
gate = block((LANDING / "AERISAirfieldRegistry.cs").read_text(),
             "static bool AutomaticCertificationAllowed(")
types = "namespace AERISFlightControl.Landing {\n" + "\n".join(
    block(models, "internal enum " + name)
    for name in ("AERISAirfieldSource", "AERISFacilityKind")) + "\n}"

HARNESS = r'''
using System;
using System.Collections;
using System.Collections.Generic;
using AERISFlightControl.Landing;
namespace UnityEngine {
    public struct Vector3 {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x=a; y=b; z=c; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 forward { get { return new Vector3(0,0,1); } }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public Vector3 normalized { get { return this; } }
        public static Vector3 operator *(Vector3 v, float s) { return new Vector3(v.x*s,v.y*s,v.z*s); }
    }
    public class Component { public GameObject gameObject; }
    public class Transform : Component {
        public Vector3 position;
        public bool MissingFrame, FindSelf;
        public Vector3 forward { get { if(MissingFrame) throw new Exception("missing runtime frame"); return Vector3.forward; } }
        public Transform Find(string p) { return FindSelf ? this : null; }
        public Vector3 InverseTransformPoint(Vector3 p) { return p; }
        public Vector3 InverseTransformDirection(Vector3 p) { return p; }
        public Vector3 TransformPoint(Vector3 p) { return p; }
        public Vector3 TransformDirection(Vector3 p) { return p; }
    }
    public class GameObject {
        public Transform transform;
        public GameObject() { transform = new Transform { gameObject=this }; }
    }
}
public class CelestialBody {
    public string name = "Kerbin";
    public double Radius = 600000;
    public double GetLatitude(UnityEngine.Vector3 p) { return p.x; }
    public double GetLongitude(UnityEngine.Vector3 p) { return p.y; }
    public double GetAltitude(UnityEngine.Vector3 p) { return p.z; }
}
public class Facility {
    public string name, facilityName, facilityDisplayName, facilityTransformName="", pqsName="Kerbin", editorFacility="SPH";
    public CelestialBody hostBody = new CelestialBody();
    public UnityEngine.Transform facilityTransform = new UnityEngine.GameObject().transform;
}
public class PSystemSetup {
    public static PSystemSetup Instance = new PSystemSetup();
    public Facility[] SpaceCenterFacilities;
}
public class Instance {
    public string Group="Example", configPath="KerbinSide/Example.cfg";
    public double RefLatitude, RefLongitude=25;
    public CelestialBody CelestialBody = new CelestialBody();
    public static int MeshGetterCalls;
    public object _mesh;
    public object mesh { get { MeshGetterCalls++; throw new Exception("mesh getter must not be used"); } }
}
public class Site {
    public string LaunchSiteName, Category="Runway", LaunchSiteType="SPH";
    public string LaunchPadTransform="Spawn";
    public bool isSquad;
    public Facility spaceCenterFacility;
    public Instance staticInstance = new Instance();
}
public class UnknownOwnerSite {
    public string LaunchSiteName="Example Runway", Category="Runway";
    public Facility spaceCenterFacility;
}
#if WITH_KK
public class BrokenTable : IEnumerable {
    public object First;
    public IEnumerator GetEnumerator() { if(First!=null) yield return First; throw new Exception("enumeration failed"); }
}
namespace KerbalKonstructs.Core {
    public static class LaunchSiteManager {
        public static IEnumerable Table;
        public static bool Unready;
        public static IEnumerable AllLaunchSites { get { if (Unready) throw new Exception("not ready"); return Table; } }
    }
}
#endif
namespace AERISFlightControl.Logging { public static class AERISLogger { public static void Warn(string s) { } } }
namespace AERISFlightControl.Landing {
    internal struct AERISGeoPoint { }
    internal class AERISRunwaySurveyDefinition { }
    internal static class AERISAirfieldConfigParser { internal static double NormalizeHeading(double v) { return v; } }
    internal static class Gate {
        __REAL_GATE__
        internal static bool Allowed(AERISProviderFacilityRecord r) { return AutomaticCertificationAllowed(r); }
    }
}
class Test {
    static int passed, failed;
    static void Assert(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    static void Run(string name, Action action) {
        try { Reset(); action(); passed++; Console.WriteLine("PASS: "+name); }
        catch(Exception e) { failed++; Console.WriteLine("FAIL: "+name+" -- "+e.Message); }
    }
    static void Reset() {
        PSystemSetup.Instance = new PSystemSetup(); Instance.MeshGetterCalls=0;
#if WITH_KK
        KerbalKonstructs.Core.LaunchSiteManager.Table = new object[0];
        KerbalKonstructs.Core.LaunchSiteManager.Unready=false;
#endif
    }
    static Facility Make(string id, string display, string url) {
        var f = new Facility { name=id, facilityName=id, facilityDisplayName=display, facilityTransformName=url };
        if (id=="LaunchPad") f.editorFacility="VAB";
        f.facilityTransform.position = new UnityEngine.Vector3(12,25,42);
        return f;
    }
    static AERISProviderFacilityRecord Collect(Facility f) {
        PSystemSetup.Instance.SpaceCenterFacilities = new[] { f };
        var records = new List<AERISProviderFacilityRecord>(); string status;
        Assert(AERISKspFacilityProvider.Collect(records,out status)==1,"expected one relevant facility: "+status);
        return records[0];
    }
    static void Untrusted(AERISProviderFacilityRecord r) {
        Assert(r.Source!=AERISAirfieldSource.Stock && !r.IsSquadOwned,"injected facility promoted to Squad/Stock");
        Assert(!Gate.Allowed(r),"mod facility passed automatic-certification gate");
    }
    static int Main() {
        Run("localized native Runway retains Stock authority",()=> {
            var r=Collect(Make("Runway","#autoLOC_300899","KSC/SpaceCenter/Runway"));
            Assert(r.Source==AERISAirfieldSource.Stock && r.IsSquadOwned && Gate.Allowed(r),"native runway authority lost");
            Assert(r.FacilityKind==AERISFacilityKind.Runway,"translated display broke runway classification");
        });
        Run("localized native LaunchPad remains Stock",()=> {
            var r=Collect(Make("LaunchPad","#autoLOC_300898","KSC/SpaceCenter/LaunchPad"));
            Assert(r.Source==AERISAirfieldSource.Stock && r.IsSquadOwned,"native pad authority lost");
        });
        Run("provider geometry and identifiers remain unchanged",()=> {
            var f=Make("Example Runway","Example Runway",""); var r=Collect(f);
            Assert(r.LatitudeDeg==12 && r.LongitudeDeg==25 && r.ElevationMeters==42,"coordinates changed");
            Assert(r.ProviderReferencePositionValid && r.RuntimeLaunchFrameValid,"runtime geometry lost");
            Assert(ReferenceEquals(r.RuntimeInstanceTransform,f.facilityTransform),"runtime transform changed");
            Assert(r.ProviderSiteId=="Example Runway" && r.ProviderGroup=="Example Runway","identity changed");
            Assert(Instance.MeshGetterCalls==0,"mesh activated");
        });
        Run("missing KK twin cannot grant Stock authority",()=>Untrusted(Collect(Make("Example Runway","Example Runway",""))));
        Run("native-looking display alone cannot grant Stock authority",()=>Untrusted(Collect(Make("Custom","Runway",""))));
        Run("native ID without its registered path is untrusted",()=>Untrusted(Collect(Make("Runway","Runway",""))));
        Run("arbitrary registered mod path is untrusted",()=>Untrusted(Collect(Make("Custom Runway","Custom Runway","Mods/Custom/Runway"))));
        Run("wrong native path cannot grant Stock authority",()=>Untrusted(Collect(Make("Runway","Runway","KSC/SpaceCenter/LaunchPad"))));
        Run("DLC remains excluded from automatic certification",()=> {
            var r=Collect(Make("Dessert Runway","Dessert Runway","Dessert/Runway"));
            Assert(r.Source==AERISAirfieldSource.Dlc && !Gate.Allowed(r),"DLC promoted or lost its source");
        });
        Run("unready KSP remains unavailable",()=> {
            PSystemSetup.Instance=null; var records=new List<AERISProviderFacilityRecord>(); string status;
            Assert(AERISKspFacilityProvider.Collect(records,out status)==0,"unready collection changed");
        });
#if WITH_KK
        Run("exact KK object link identifies source and denies authority",()=> {
            var f=Make("Example Runway","Example Runway","");
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { new Site { LaunchSiteName="Example Runway",spaceCenterFacility=f } };
            var r=Collect(f); Untrusted(r); Assert(r.Source==AERISAirfieldSource.KerbalKonstructs,"KK origin lost");
            Assert(Instance.MeshGetterCalls==0,"mesh getter used");
        });
        Run("SLE object link preserves SLE source",()=> {
            var f=Make("Example Runway","Example Runway",""); var s=new Site { LaunchSiteName="Example Runway",spaceCenterFacility=f };
            s.staticInstance.configPath="SLE/Kerbin/Example.cfg";
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { s };
            var r=Collect(f); Untrusted(r); Assert(r.Source==AERISAirfieldSource.StockLaunchsitesExpansion,"SLE origin lost");
        });
        Run("Squad KSC2 object link preserves native ownership",()=> {
            var f=Make("KSC2","KSC2",""); f.editorFacility="VAB";
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { new Site { LaunchSiteName="KSC2",spaceCenterFacility=f,isSquad=true } };
            var r=Collect(f); Assert(r.Source==AERISAirfieldSource.Stock && r.IsSquadOwned,"Squad KSC2 demoted");
        });
        Run("KK non-Squad link overrides a native-looking name and path",()=> {
            var f=Make("Runway","Runway","KSC/SpaceCenter/Runway");
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { new Site { LaunchSiteName="Runway",spaceCenterFacility=f } };
            Untrusted(Collect(f));
        });
        Run("name coincidence cannot demote a different native object",()=> {
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { new Site { LaunchSiteName="Runway",spaceCenterFacility=Make("Runway","Runway","") } };
            Assert(Gate.Allowed(Collect(Make("Runway","Runway","KSC/SpaceCenter/Runway"))),"name collision demoted native");
        });
        Run("unready KK table cannot promote injected facility",()=> {
            KerbalKonstructs.Core.LaunchSiteManager.Unready=true;
            Untrusted(Collect(Make("Example Runway","Example Runway","")));
            Assert(Gate.Allowed(Collect(Make("Runway","Runway","KSC/SpaceCenter/Runway"))),"native baseline lost while KK unready");
        });
        Run("unknown KK ownership never defaults to Squad",()=> {
            var f=Make("Example Runway","Example Runway","");
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { new UnknownOwnerSite { spaceCenterFacility=f } };
            Untrusted(Collect(f));
        });
        foreach(bool partial in new[] { false,true }) {
            bool yielded=partial;
            Run("KK iterator failure "+(yielded?"after":"before")+" a yield leaves native fallback available",()=> {
                var mod=Make("Example Runway","Example Runway","");
                var native=Make("Runway","#autoLOC_300899","KSC/SpaceCenter/Runway");
                KerbalKonstructs.Core.LaunchSiteManager.Table=new BrokenTable {
                    First=yielded ? new Site { LaunchSiteName="Example Runway",spaceCenterFacility=mod } : null };
                PSystemSetup.Instance.SpaceCenterFacilities=new[] { mod,native };
                var records=new List<AERISProviderFacilityRecord>(); string status;
                Assert(AERISKspFacilityProvider.Collect(records,out status)==2,"broken KK table aborted native collection: "+status);
                Untrusted(records[0]); Assert(Gate.Allowed(records[1]),"native authority lost");
            });
        }
        Run("missing KK runtime frame cannot abort later native collection",()=> {
            var mod=Make("Example Runway","Example Runway","");
            var site=new Site { LaunchSiteName="Example Runway",spaceCenterFacility=mod };
            var missing=new UnityEngine.GameObject(); missing.transform.FindSelf=true; missing.transform.MissingFrame=true;
            site.staticInstance._mesh=missing;
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] { site };
            PSystemSetup.Instance.SpaceCenterFacilities=new[] { mod,Make("Runway","#autoLOC_300899","KSC/SpaceCenter/Runway") };
            var records=new List<AERISProviderFacilityRecord>(); string status;
            Assert(AERISKspFacilityProvider.Collect(records,out status)==2,"bad linked site aborted native collection: "+status);
            Untrusted(records[0]); Assert(Gate.Allowed(records[1]),"native authority lost");
        });
        Run("partial KK table retains observed non-Squad evidence over native-looking fallback",()=> {
            var f=Make("Runway","Runway","KSC/SpaceCenter/Runway");
            KerbalKonstructs.Core.LaunchSiteManager.Table=new BrokenTable {
                First=new Site { LaunchSiteName="Runway",spaceCenterFacility=f } };
            Untrusted(Collect(f));
        });
        Run("partial Squad-only KK prefix cannot grant ownership without native registration",()=> {
            var f=Make("KSC2","KSC2",""); f.editorFacility="VAB";
            KerbalKonstructs.Core.LaunchSiteManager.Table=new BrokenTable {
                First=new Site { LaunchSiteName="KSC2",spaceCenterFacility=f,isSquad=true } };
            Untrusted(Collect(f));
        });
        Run("a conflicting non-Squad link wins over a Squad link",()=> {
            var f=Make("Runway","Runway","KSC/SpaceCenter/Runway");
            KerbalKonstructs.Core.LaunchSiteManager.Table=new object[] {
                new Site { LaunchSiteName="Runway",spaceCenterFacility=f,isSquad=true },
                new Site { LaunchSiteName="Runway",spaceCenterFacility=f } };
            Untrusted(Collect(f));
        });
        Run("all 26 reported mod runways remain denied with and without provider twins",()=> {
            string[] names=__REPORTED_RUNWAYS__;
            var facilities=new List<Facility>(); var sites=new List<object>();
            for(int i=0;i<names.Length;i++) {
                var f=Make(names[i],names[i],""); f.facilityTransform.position=new UnityEngine.Vector3(-60+i*4,25,42);
                var s=new Site { LaunchSiteName=names[i],spaceCenterFacility=f }; s.staticInstance.RefLatitude=-60+i*4;
                facilities.Add(f); sites.Add(s);
            }
            PSystemSetup.Instance.SpaceCenterFacilities=facilities.ToArray();
            KerbalKonstructs.Core.LaunchSiteManager.Table=sites.ToArray();
            var ksp=new List<AERISProviderFacilityRecord>(); string status;
            Assert(AERISKspFacilityProvider.Collect(ksp,out status)==26,"runway count changed");
            foreach(var r in ksp) Untrusted(r);
            var combined=new List<AERISProviderFacilityRecord>(); AERISKerbalKonstructsProvider.Collect(combined,out status); combined.AddRange(ksp);
            AERISPhysicalRunwayMergeSummary summary;
            foreach(var r in AERISPhysicalRunwayIdentity.Canonicalize(combined,out summary)) Assert(!Gate.Allowed(r),"provider-pair authority leaked");
            foreach(var r in AERISPhysicalRunwayIdentity.Canonicalize(ksp,out summary)) Assert(!Gate.Allowed(r),"missing-twin authority leaked");
            Assert(ReferenceEquals(PSystemSetup.Instance.SpaceCenterFacilities[0],facilities[0]) && sites.Count==26,"live inputs mutated");
        });
#endif
        Console.WriteLine("REG-02 FACILITY PROVENANCE "+passed+"/"+(passed+failed)+" PASS");
        return failed==0?0:1;
    }
}
'''

runways = ["Area 52 Long runway", "Area 52 X-Runway", "Black Krags GC Runway", "Cove Runway",
           "Dull Spot Runway", "Dundard's Edge Runway", "Glacier Lake Long Runway", "Glacier Lake Runway",
           "Goldpool Runway", "Green Coast Runway", "Green Peaks Runway", "Guardians Basin Runway",
           "Hanbert's Cape Runway", "Harvester Airfield", "Kerbin's Bottom Runway", "Kerman Lake Runway",
           "Baikerbanur Runway", "Lake Dermal Runway", "Lodnie Isles Runway", "Lushlands Runway",
           "Mahi Runway", "Sea's End Runway", "South Hope Runway", "TSC Runway 09", "TSC Runway 27", "Uberdam Airfield"]
HARNESS = HARNESS.replace("__REAL_GATE__", gate).replace(
    "__REPORTED_RUNWAYS__", "new string[] {" + ",".join(json.dumps(s) for s in runways) + "}")

with tempfile.TemporaryDirectory(prefix="aeris-reg02-provenance-") as folder:
    folder = Path(folder)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS)
    enums = folder / "Enums.cs"
    enums.write_text(types)
    failed_modes = []
    for mode in ("with-kk", "without-kk"):
        exe = folder / (mode + ".exe")
        command = ["mcs", "-out:" + str(exe), str(fixture), str(enums)] + [str(LANDING / name) for name in (
            "AERISAirfieldProviders.cs", "AERISPhysicalRunwayIdentity.cs", "AERISProviderIdentity.cs")]
        if mode == "with-kk":
            command.append("-define:WITH_KK")
        subprocess.run(command, check=True)
        if subprocess.run(["mono", str(exe)]).returncode:
            failed_modes.append(mode)
    if failed_modes:
        raise SystemExit("FAILED: " + ", ".join(failed_modes))
