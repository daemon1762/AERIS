#!/usr/bin/env python3
"""Exercise full isolated Settings.Save/Load and temporary observer at boundaries.

Fixture ConfigNode shapes are simulations, not KSP runtime evidence.
"""
from pathlib import Path
import importlib.util
import re
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("gap2_prepare", ROOT / "Tools/aeris53_gap2_01_prepare.py")
prepare = importlib.util.module_from_spec(spec)
spec.loader.exec_module(prepare)

HARNESS = r'''
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AERIS53Gap2Isolation.Settings;
namespace UnityEngine {
    public class MonoBehaviour { }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug { public static void Log(string s) { } public static void LogError(string s) { } }
    public enum KeyCode { None, A, F, X, LeftAlt }
    public static class Input { public static bool GetKey(KeyCode k) { throw new Exception("input accessed"); }
        public static bool GetKeyDown(KeyCode k) { throw new Exception("input accessed"); } }
    public static class Mathf {
        public static float Clamp(float v,float lo,float hi) { return Math.Max(lo,Math.Min(hi,v)); }
        public static int Clamp(int v,int lo,int hi) { return Math.Max(lo,Math.Min(hi,v)); }
        public static float Clamp01(float v) { return Clamp(v,0,1); }
        public static float Abs(float v) { return Math.Abs(v); }
        public static float Repeat(float v,float length) { return v-(float)Math.Floor(v/length)*length; }
    }
}
public class KSPAddon : Attribute { public enum Startup { Flight } public KSPAddon(Startup s,bool once) { } }
public static class FlightGlobals { public static bool ready; }
public static class KSPUtil { public static string ApplicationRootPath; }
public class ConfigNode {
    public string name;
    Dictionary<string,string> values=new Dictionary<string,string>();
    Dictionary<string,ConfigNode> children=new Dictionary<string,ConfigNode>();
    static Dictionary<string,ConfigNode> saved=new Dictionary<string,ConfigNode>();
    public static string Mode;
    public static int SaveCount, LoadCount;
    public static List<string> WritePaths=new List<string>();
    public ConfigNode(string n) { name=n; }
    public void AddValue(string k,object v) { values[k]=Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture); }
    public bool HasValue(string k) { return values.ContainsKey(k); }
    public string GetValue(string k) { string v; return values.TryGetValue(k,out v)?v:null; }
    public ConfigNode GetNode(string k) { ConfigNode n; return children.TryGetValue(k,out n)?n:null; }
    public void Save(string path) {
        if(Mode=="save-error") throw new IOException("simulated Save failure");
        SaveCount++; WritePaths.Add(path);
        if(Mode=="no-file") return;
        saved[path]=this;
        File.WriteAllText(path,name+"\n"+string.Join("\n",values.Select(x=>x.Key+"="+x.Value).ToArray()));
    }
    public static ConfigNode Load(string path) {
        LoadCount++;
        if(Mode=="load-error") throw new IOException("simulated Load failure");
        if(Mode=="mixed" && SaveCount==2) return null;
        ConfigNode payload;
        if(!saved.TryGetValue(path,out payload)) return null;
        if(Mode=="lost-value") payload.values.Remove("flightDataArchiveLimit");
        if(Mode=="named-mismatch" && LoadCount%2==0) return new ConfigNode("AERIS_SETTINGS");
        if(Mode=="named" || Mode=="named-mismatch" || (Mode=="mixed" && SaveCount==1)) return payload;
        if(Mode=="child") { var root=new ConfigNode("root"); root.children["AERIS_SETTINGS"]=payload; return root; }
        if(Mode=="missing-payload") return new ConfigNode("root");
        var generic=new ConfigNode("root"); generic.values=payload.values; return generic;
    }
    public static void Reset(string mode) { saved.Clear(); WritePaths.Clear(); Mode=mode; SaveCount=LoadCount=0; }
}
class Test {
    static int passed,failed;
    static string protectedPath, original="UNCHANGED PRODUCTION CONFIG";
    static void Assert(bool b,string s) { if(!b) throw new Exception(s); }
    static void Run(string name,string mode,Action<string> verify,bool absent=false) {
        try {
            if(absent) File.Delete(protectedPath);
            ConfigNode.Reset(mode);
            string report=AERIS53Gap2Observation.Capture(); verify(report);
            Assert(absent ? !File.Exists(protectedPath) : File.ReadAllText(protectedPath)==original,"real settings modified");
            foreach(string path in ConfigNode.WritePaths)
                Assert(path.StartsWith(Path.Combine(KSPUtil.ApplicationRootPath,"Logs/AERIS53_GAP201")+Path.DirectorySeparatorChar),"write outside probe scratch: "+path);
            Assert(AERIS53Gap2Scratch.PathName==null,"scratch path leaked after capture");
            passed++; Console.WriteLine("PASS: "+name);
        } catch(Exception e) { failed++; Console.WriteLine("FAIL: "+name+" -- "+e.Message); }
        finally { if(absent) File.WriteAllText(protectedPath,original); }
    }
    static void Contains(string report,string expected) { Assert(report.Contains(expected),"missing "+expected+"\n"+report); }
    static void Inconclusive(string report) {
        Contains(report,"GAP2-01 RESULT=INCONCLUSIVE");
        Assert(!report.Contains("GAP2-01 RESULT=SETTINGS_ROUNDTRIP_RETAINED"),"false retention claim");
    }
    static int Main(string[] args) {
        KSPUtil.ApplicationRootPath=Path.GetFullPath(args[0]);
        protectedPath=Path.Combine(KSPUtil.ApplicationRootPath,"GameData/AERISFlightControl/Config/AERISSettings.cfg");
        Directory.CreateDirectory(Path.GetDirectoryName(protectedPath)); File.WriteAllText(protectedPath,original);
        Run("named root executes full Settings roundtrip without rejection","named",report=> {
            Contains(report,"GAP2-01 RESULT=SETTINGS_ROUNDTRIP_RETAINED");
            Contains(report,"RETAINED_ROUNDTRIPS=2");
            Contains(report,"EXPECTED=731 / 9.5 / False / 17; ACTUAL=731 / 9.5 / False / 17");
        });
        Run("named child retains saved settings twice","child",report=> {
            Contains(report,"PAYLOAD_SHAPE=NAMED_CHILD");
            Contains(report,"GAP2-01 RESULT=SETTINGS_ROUNDTRIP_RETAINED");
            Contains(report,"RETAINED_ROUNDTRIPS=2");
        });
        Run("generic root direct values retain saved settings twice","direct",report=> {
            Contains(report,"PAYLOAD_SHAPE=DIRECT_VALUES_ON_GENERIC_ROOT");
            Contains(report,"GAP2-01 RESULT=SETTINGS_ROUNDTRIP_RETAINED");
            Contains(report,"RETAINED_ROUNDTRIPS=2");
        });
        Run("Save exception cannot masquerade as successful retention","save-error",Inconclusive);
        Run("Save without a file is inconclusive","no-file",Inconclusive);
        Run("Load exception is inconclusive","load-error",Inconclusive);
        Run("missing payload is inconclusive","missing-payload",Inconclusive);
        Run("serialization loses a sentinel before Settings load","lost-value",Inconclusive);
        Run("named-root mismatch cannot falsely claim retention","named-mismatch",Inconclusive);
        Run("successful first roundtrip cannot hide a failed second case","mixed",Inconclusive);
        Run("absent production config stays absent","direct",report=> {
            Contains(report,"REAL_SETTINGS_SHA256_BEFORE=ABSENT");
            Contains(report,"REAL_SETTINGS_SHA256_AFTER=ABSENT");
            Contains(report,"REAL_SETTINGS_SNAPSHOT_UNCHANGED=True");
            Contains(report,"No production settings or live Settings instances are written");
            Contains(report,"not a restart persistence claim");
        },true);
        // No fallback to the production path when the isolated copy is unconfigured.
        ConfigNode.Reset("named"); AERIS53Gap2Scratch.PathName=null;
        new AERISSettings().Save();
        if(File.ReadAllText(protectedPath)==original && ConfigNode.WritePaths.Count==0)
        { passed++; Console.WriteLine("PASS: unset isolated path never falls back to production settings"); }
        else { failed++; Console.WriteLine("FAIL: unset isolated path wrote production settings"); }
        Console.WriteLine("GAP2-01 OBSERVER "+passed+"/"+(passed+failed)+" PASS");
        return failed==0?0:1;
    }
}
'''

with tempfile.TemporaryDirectory(prefix="aeris-gap2-probe-") as folder:
    folder = Path(folder)
    prepare.prepare(folder)
    original = (prepare.SOURCE / "Settings/AERISSettings.cs").read_text()
    copied = (folder / "IsolatedSettings.cs").read_text()
    restored = copied.replace("AERIS53Gap2Isolation", "AERISFlightControl").replace(
        "global::AERIS53Gap2Scratch.PathName",
        'System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/AERISFlightControl/Config/AERISSettings.cfg")')
    assert restored == original, "Save/Load code changed beyond approved isolation"
    print("PASS: isolated copy differs only in namespaces/imports and private path", flush=True)
    # Model Unity's module boundary: Input is forwarded by UnityEngine.dll.
    # The former single-assembly fixture could not detect a missing module reference.
    unity_start = HARNESS.index("namespace UnityEngine {")
    unity_end = HARNESS.index("public class KSPAddon")
    unity = HARNESS[unity_start:unity_end]
    input_start = unity.index("    public static class Input")
    input_end = unity.index("    public static class Mathf")
    input_class = unity[input_start:input_end]
    core = folder / "Core.cs"
    core.write_text("using System;\n" + unity[:input_start] + unity[input_end:])
    legacy = folder / "InputLegacy.cs"
    legacy.write_text("using System;\nnamespace UnityEngine {\n" + input_class + "}\n")
    facade = folder / "Facade.cs"
    facade.write_text("[assembly: System.Runtime.CompilerServices.TypeForwardedTo(typeof(UnityEngine.Input))]\n")
    core_dll = folder / "UnityEngine.CoreModule.dll"
    legacy_dll = folder / "UnityEngine.InputLegacyModule.dll"
    facade_dll = folder / "UnityEngine.dll"
    for source, output, references in (
        (core, core_dll, []),
        (legacy, legacy_dll, [core_dll]),
        (facade, facade_dll, [core_dll, legacy_dll]),
    ):
        subprocess.run(["mcs", "-target:library", "-out:" + str(output)] +
                       ["-r:" + str(ref) for ref in references] + [str(source)], check=True)
    fixture = folder / "Fixture.cs"
    fixture.write_text(HARNESS[:unity_start] + HARNESS[unity_end:])
    exe = folder / "ProbeTests.exe"
    compile_command = ["mcs", "-out:" + str(exe), str(fixture), str(folder / "IsolatedSettings.cs"),
                       str(folder / "IsolatedSupport.cs"), str(ROOT / "TESTS/Runtime/AERIS53Gap201Probe.cs")]
    missing_reference = subprocess.run(compile_command + ["-r:" + str(core_dll), "-r:" + str(facade_dll)],
                                       capture_output=True, text=True)
    assert missing_reference.returncode != 0 and "CS1070" in missing_reference.stderr and \
        "UnityEngine.InputLegacyModule" in missing_reference.stderr, "module-boundary failure not reproduced"
    print("PASS: omitted InputLegacyModule reproduces forwarded Input compile failure", flush=True)
    helper = (ROOT / "Tools/aeris53_gap2_01_repro.sh").read_text()
    references = re.findall(r'-r:"\$MANAGED/(UnityEngine(?:\.[A-Za-z0-9]+)?\.dll)"', helper)
    subprocess.run(compile_command + ["-r:" + str(folder / name) for name in references], check=True)
    print("PASS: installer Unity module references compile the full isolated Settings copy", flush=True)
    subprocess.run(["mono", str(exe), str(folder / "fake-ksp")], check=True)
