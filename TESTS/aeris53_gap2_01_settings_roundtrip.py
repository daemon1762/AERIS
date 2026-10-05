#!/usr/bin/env python3
"""Exercise the production Settings class against controlled ConfigNode boundaries.

Requires mcs/mono. All files belong to a temporary fake KSP installation.
ConfigNode shapes model the observed API; these are not full KSP runtime tests.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source/AERISFlightControl"


def enum_block(text, name):
    start = text.index("internal enum " + name)
    return text[start:text.index("}", text.index("{", start)) + 1]


HARNESS = r'''
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AERISFlightControl.Settings;
namespace UnityEngine {
    public enum KeyCode { None, A, F, X, LeftAlt }
    public static class Input {
        public static bool GetKey(KeyCode k) { throw new Exception("input accessed"); }
        public static bool GetKeyDown(KeyCode k) { throw new Exception("input accessed"); }
    }
    public static class Mathf {
        public static float Clamp(float v,float lo,float hi) { return Math.Max(lo,Math.Min(hi,v)); }
        public static int Clamp(int v,int lo,int hi) { return Math.Max(lo,Math.Min(hi,v)); }
        public static float Clamp01(float v) { return Clamp(v,0,1); }
        public static float Abs(float v) { return Math.Abs(v); }
        public static float Repeat(float v,float len) { return v-(float)Math.Floor(v/len)*len; }
    }
}
namespace AERISFlightControl.Logging {
    internal static class AERISLogger {
        internal static List<string> Warnings=new List<string>();
        internal static void Info(string s) { }
        internal static void Warn(string s) { Warnings.Add(s); }
    }
}
public static class KSPUtil { public static string ApplicationRootPath; }
public class ConfigNode {
    public string name;
    public Dictionary<string,string> Values=new Dictionary<string,string>();
    public Dictionary<string,ConfigNode> Children=new Dictionary<string,ConfigNode>();
    public static string Shape="direct";
    public static ConfigNode Override;
    public static bool ReturnNull, ThrowLoad;
    public static int Saves, Loads;
    public ConfigNode(string n) { name=n; }
    public void AddValue(string k,object v) { Values[k]=Convert.ToString(v,CultureInfo.InvariantCulture); }
    public bool HasValue(string k) { return Values.ContainsKey(k); }
    public string GetValue(string k) { string v; return Values.TryGetValue(k,out v)?v:null; }
    public ConfigNode GetNode(string k) { ConfigNode n; return Children.TryGetValue(k,out n)?n:null; }
    public void Save(string path) {
        if(path!=Test.SettingsPath) throw new Exception("write outside temporary KSP settings path");
        Saves++;
        var lines=new List<string>();
        foreach(var pair in Values) lines.Add(pair.Key+" = "+pair.Value);
        File.WriteAllLines(path,lines.ToArray());
    }
    public static ConfigNode ReadValues(string path) {
        var node=new ConfigNode("AERIS_SETTINGS");
        foreach(string line in File.ReadAllLines(path)) {
            int separator=line.IndexOf('=');
            if(separator<0) throw new Exception("invalid fixture line");
            node.AddValue(line.Substring(0,separator).Trim(),line.Substring(separator+1).Trim());
        }
        return node;
    }
    public static ConfigNode Load(string path) {
        Loads++;
        if(ThrowLoad) throw new IOException("simulated ConfigNode.Load failure");
        if(ReturnNull) return null;
        if(Override!=null) return Override;
        var payload=ReadValues(path);
        if(Shape=="named") return payload;
        if(Shape=="child") { var root=new ConfigNode("root"); root.Children["AERIS_SETTINGS"]=payload; return root; }
        payload.name="root"; return payload;
    }
    public static void Reset() {
        Shape="direct"; Override=null; ReturnNull=ThrowLoad=false; Saves=Loads=0;
        AERISFlightControl.Logging.AERISLogger.Warnings.Clear();
    }
}
class Test {
    static int passed,failed;
    internal static string SettingsPath {
        get { return Path.Combine(KSPUtil.ApplicationRootPath,"GameData/AERISFlightControl/Config/AERISSettings.cfg"); }
    }
    static void Assert(bool b,string message) { if(!b) throw new Exception(message); }
    static AERISSettings Sentinels() {
        return new AERISSettings { MainWindowX=731f, ProtectAoAWarningDegrees=9.5f,
            ShowSasWarning=false, FlightDataArchiveLimit=17, SpeedAutomaticAirbrakeEnabled=false,
            EnableAABackgroundTraining=false, PerformanceWorkerOverride=2,
            NavigationDisplayMode=AERISDisplayMode.Off, FlightInstrumentDisplayMode=AERISDisplayMode.Off };
    }
    static void Retained(AERISSettings settings) {
        Assert(settings.MainWindowX==731f,"mainWindowX: "+settings.MainWindowX+" instead of 731");
        Assert(settings.ProtectAoAWarningDegrees==9.5f,"AoA warning reverted");
        Assert(!settings.ShowSasWarning,"SAS warning reverted");
        Assert(settings.FlightDataArchiveLimit==17,"archive limit reverted");
        Assert(!settings.SpeedAutomaticAirbrakeEnabled && !settings.EnableAABackgroundTraining,"saved OFF intent lost");
        Assert(settings.PerformanceWorkerOverride==2,"worker selection reverted");
        Assert(settings.NavigationDisplayMode==AERISDisplayMode.Off &&
            settings.FlightInstrumentDisplayMode==AERISDisplayMode.Off,"display OFF intent lost");
    }
    static void Run(string name,Action action) {
        ConfigNode.Reset();
        try { action(); passed++; Console.WriteLine("PASS: "+name); }
        catch(Exception e) { failed++; Console.WriteLine("FAIL: "+name+" -- "+e.Message); }
    }
    static ConfigNode Fixture(string name="root") {
        Sentinels().Save();
        ConfigNode node=ConfigNode.ReadValues(SettingsPath); node.name=name;
        ConfigNode.Saves=0; ConfigNode.Override=node; return node;
    }
    static void DefaultsWithoutWrite() {
        string before=File.ReadAllText(SettingsPath);
        var settings=AERISSettings.Load();
        Assert(settings.MainWindowX==260f && settings.ProtectAoAWarningDegrees==8f &&
            settings.ShowSasWarning && settings.FlightDataArchiveLimit==10,"untrusted node was read");
        Assert(ConfigNode.Saves==0 && File.ReadAllText(SettingsPath)==before,"rejected node caused a migration write");
    }
    static int Main(string[] args) {
        KSPUtil.ApplicationRootPath=Path.GetFullPath(args[0]);
        if(args.Length>1) {
            if(args[1]=="save") { Sentinels().Save(); return 0; }
            Retained(AERISSettings.Load());
            Assert(ConfigNode.Saves==0,"current settings should not migrate on another process load");
            Console.WriteLine("PASS: saved settings restored in a fresh process"); return 0;
        }
        foreach(string shape in new[]{"named","child","direct"}) {
            string current=shape;
            Run(current+" full Save/Load retains user values",()=> {
                ConfigNode.Shape=current; Sentinels().Save(); string before=File.ReadAllText(SettingsPath);
                var loaded=AERISSettings.Load(); Retained(loaded);
                Assert(ConfigNode.Saves==1,"current settings unexpectedly migrated");
                loaded.Save(); Assert(File.ReadAllText(SettingsPath)==before,"canonical settings roundtrip changed values");
            });
        }
        Run("named child wins over conflicting wrapper values",()=> {
            var child=Fixture("AERIS_SETTINGS"); var root=new ConfigNode("root");
            root.AddValue("mainWindowX",999); root.Children["AERIS_SETTINGS"]=child;
            ConfigNode.Override=root; Retained(AERISSettings.Load());
        });
        Run("named root remains authoritative over nested settings",()=> {
            var root=Fixture("AERIS_SETTINGS"); var child=new ConfigNode("AERIS_SETTINGS");
            child.AddValue("mainWindowX",999); root.Children["AERIS_SETTINGS"]=child;
            Retained(AERISSettings.Load());
        });
        Run("unrelated named root with settings-like values is rejected",()=> { Fixture("OTHER_MOD"); DefaultsWithoutWrite(); });
        Run("generic root without saved Settings identity is rejected",()=> {
            Fixture().Values.Remove("mainWindowX"); DefaultsWithoutWrite();
        });
        Run("empty root causes no migration or write",()=> { Fixture(); ConfigNode.Override=new ConfigNode("root"); DefaultsWithoutWrite(); });
        Run("null ConfigNode causes no migration or write",()=> { Fixture(); ConfigNode.ReturnNull=true; DefaultsWithoutWrite(); });
        Run("missing settings stay missing",()=> {
            File.Delete(SettingsPath); var loaded=AERISSettings.Load();
            Assert(loaded.MainWindowX==260f && !File.Exists(SettingsPath) && ConfigNode.Saves==0 && ConfigNode.Loads==0,"missing-file behavior changed");
        });
        Run("load exception preserves fallback and original bytes",()=> {
            Fixture(); ConfigNode.ThrowLoad=true; DefaultsWithoutWrite();
            Assert(AERISFlightControl.Logging.AERISLogger.Warnings.Count==1,"load warning lost");
        });
        Run("invalid values keep existing fallbacks",()=> {
            var node=Fixture(); node.AddValue("protectAoAWarningDegrees","bad");
            node.AddValue("showSasWarning","bad"); node.AddValue("flightDataArchiveLimit","bad");
            var loaded=AERISSettings.Load(); Assert(loaded.MainWindowX==731f && loaded.ProtectAoAWarningDegrees==8f &&
                loaded.ShowSasWarning && loaded.FlightDataArchiveLimit==10,"invalid-value fallback changed");
        });
        Run("accepted generic root keeps existing numeric limits",()=> {
            var node=Fixture(); node.AddValue("mainWindowX",99999); node.AddValue("protectAoAWarningDegrees",999);
            node.AddValue("protectAoALimitDegrees",0); node.AddValue("flightDataArchiveLimit",0);
            node.AddValue("velocityAccelerationLimitMps2",999);
            var loaded=AERISSettings.Load(); Assert(loaded.MainWindowX==5000f &&
                loaded.ProtectAoAWarningDegrees==45f && loaded.ProtectAoALimitDegrees==45.5f &&
                loaded.FlightDataArchiveLimit==1 && loaded.VelocityAccelerationLimitMps2==30f,"numeric limit changed");
        });
        Run("legacy migration preserves sentinels and runs once",()=> {
            var node=Fixture(); node.Values.Remove("terrainPreloadEnabled"); node.AddValue("terrainPreloadMode","Off");
            node.AddValue("airfieldsUiLayoutRevision",0); node.AddValue("airfieldsCertifiedExpanded",true);
            var loaded=AERISSettings.Load(); Retained(loaded);
            Assert(!loaded.TerrainPreloadEnabled && !loaded.AirfieldsCertifiedExpanded && ConfigNode.Saves==1,"legacy migration changed");
            ConfigNode.Override=null; loaded=AERISSettings.Load(); Retained(loaded);
            Assert(!loaded.TerrainPreloadEnabled && ConfigNode.Saves==1,"migration repeated or OFF intent lost");
        });
        Console.WriteLine("GAP2-01 SETTINGS "+passed+"/"+(passed+failed)+" PASS");
        return failed==0?0:1;
    }
}
'''

with tempfile.TemporaryDirectory(prefix="aeris-gap2-settings-") as folder:
    folder = Path(folder)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS)
    enums = []
    for name in ("AERISTerrainDisplayMode", "AERISTerrainGpuMode", "AERISTerrainColourPreset"):
        enums.append(enum_block((SOURCE / "Terrain/AERISTerrainTileContracts.cs").read_text(), name))
    enums.append(enum_block((SOURCE / "Terrain/AERISTerrainPreloadContracts.cs").read_text(), "AERISTerrainPreloadMode"))
    support = folder / "Enums.cs"
    support.write_text("namespace AERISFlightControl.Terrain {\n" + "\n".join(enums) + "\n}")
    exe = folder / "SettingsTests.exe"
    subprocess.run(["mcs", "-out:" + str(exe), str(fixture), str(support),
                    str(SOURCE / "Settings/AERISSettings.cs")], check=True)
    result = subprocess.run(["mono", str(exe), str(folder / "fake-ksp")])
    if result.returncode:
        raise SystemExit(result.returncode)
    subprocess.run(["mono", str(exe), str(folder / "fresh-ksp"), "save"], check=True)
    subprocess.run(["mono", str(exe), str(folder / "fresh-ksp"), "load"], check=True)
