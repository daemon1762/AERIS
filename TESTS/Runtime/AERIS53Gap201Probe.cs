using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using AERIS53Gap2Isolation.Settings;

// Temporary one-shot probe. No GUI/input hooks or production Settings.Load/Save.
[KSPAddon(KSPAddon.Startup.Flight, false)]
public sealed class AERIS53Gap201Probe : MonoBehaviour
{
    static bool completed;
    float started;
    void Start() { started = Time.realtimeSinceStartup; }
    void Update()
    {
        if (completed || !FlightGlobals.ready || Time.realtimeSinceStartup - started < 5f) return;
        completed = true;
        string report;
        try { report = AERIS53Gap2Observation.Capture(); }
        catch (Exception e) { report = "GAP2-01 RESULT=INCONCLUSIVE\n" + e + "\n"; }
        string path = Path.Combine(KSPUtil.ApplicationRootPath, "AERIS53_GAP201_repro.txt");
        try { File.WriteAllText(path, report, new UTF8Encoding(false)); Debug.Log("[AERIS53_GAP201_PROBE] report saved: " + path); }
        catch (Exception e) { Debug.LogError("[AERIS53_GAP201_PROBE] report write failed: " + e); }
    }
}

internal static class AERIS53Gap2Scratch
{
    internal static string PathName;
    internal static readonly List<string> Warnings = new List<string>();
}

internal static class AERIS53Gap2Observation
{
    static string Safe(string text) { return (text ?? "").Replace('\t',' ').Replace('\r',' ').Replace('\n',' '); }
    static string Number(float value) { return value.ToString("R", CultureInfo.InvariantCulture); }
    static string Hash(string path)
    {
        if (!File.Exists(path)) return "ABSENT";
        using (var algorithm = SHA256.Create())
            return BitConverter.ToString(algorithm.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
    }
    static ConfigNode Payload(ConfigNode loaded, out string shape)
    {
        shape = "MISSING";
        if (loaded == null) return null;
        if (loaded.name == "AERIS_SETTINGS") { shape="NAMED_ROOT"; return loaded; }
        ConfigNode child = loaded.GetNode("AERIS_SETTINGS");
        if (child != null) { shape="NAMED_CHILD"; return child; }
        if (loaded.HasValue("mainWindowX") && loaded.HasValue("flightDataArchiveLimit"))
        { shape="DIRECT_VALUES_ON_GENERIC_ROOT"; return loaded; }
        return null;
    }
    static bool SavedSentinels(ConfigNode node)
    {
        float x, aoa; int archive; bool warning;
        return node != null &&
            float.TryParse(node.GetValue("mainWindowX"), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && x==731f &&
            float.TryParse(node.GetValue("protectAoAWarningDegrees"), NumberStyles.Float, CultureInfo.InvariantCulture, out aoa) && aoa==9.5f &&
            int.TryParse(node.GetValue("flightDataArchiveLimit"), out archive) && archive==17 &&
            bool.TryParse(node.GetValue("showSasWarning"), out warning) && !warning;
    }
    internal static string Capture()
    {
        string root = Path.GetFullPath(KSPUtil.ApplicationRootPath);
        string realPath = Path.Combine(root, "GameData/AERISFlightControl/Config/AERISSettings.cfg");
        string before = Hash(realPath);
        string folder = Path.Combine(Path.Combine(root,"Logs/AERIS53_GAP201"), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var report = new StringBuilder();
        report.AppendLine("AERIS53 GAP2-01 ISOLATED SETTINGS ROUNDTRIP v1");
        report.AppendLine("UTC="+DateTime.UtcNow.ToString("o"));
        report.AppendLine("SETTINGS_SOURCE_SHA256="+AERIS53Gap2SourceIdentity.SettingsSha256);
        report.AppendLine("CONFIG_NODE_ASSEMBLY="+typeof(ConfigNode).Assembly.FullName);
        report.AppendLine("PROBE_SCRATCH="+folder);
        report.AppendLine("Full Settings.Save/Load copy with namespace/import/path isolation only; real KSP ConfigNode implementation.");
        report.AppendLine("No production settings or live Settings instances are written. This is an isolated roundtrip, not a restart persistence claim.");
        int reproduced=0, retained=0;
        try
        {
            for(int i=1;i<=2;i++)
            {
                AERIS53Gap2Scratch.PathName=Path.Combine(folder,"roundtrip-"+i+".cfg");
                AERIS53Gap2Scratch.Warnings.Clear();
                var saved=new AERISSettings { MainWindowX=731f, ProtectAoAWarningDegrees=9.5f,
                    ShowSasWarning=false, FlightDataArchiveLimit=17 };
                saved.Save();
                if(!File.Exists(AERIS53Gap2Scratch.PathName)) throw new InvalidOperationException("isolated Save produced no file");
                ConfigNode raw=ConfigNode.Load(AERIS53Gap2Scratch.PathName);
                string shape;
                ConfigNode payload=Payload(raw,out shape);
                if(!SavedSentinels(payload)) throw new InvalidOperationException("serialization did not retain all sentinels; shape="+shape);
                var loaded=AERISSettings.Load();
                if(AERIS53Gap2Scratch.Warnings.Count!=0)
                    throw new InvalidOperationException("isolated Settings warning: "+string.Join("; ",AERIS53Gap2Scratch.Warnings.ToArray()));
                bool same=loaded.MainWindowX==731f && loaded.ProtectAoAWarningDegrees==9.5f &&
                    !loaded.ShowSasWarning && loaded.FlightDataArchiveLimit==17;
                bool rejected=raw!=null && raw.name!="AERIS_SETTINGS";
                report.AppendLine("CASE="+i+"; LOADED_ROOT="+Safe(raw==null?"<NULL>":raw.name)+"; PAYLOAD_SHAPE="+shape+
                    "; SERIALIZED_SENTINELS=True; CURRENT_ROOT_GATE_REJECTS="+rejected+"; SETTINGS_RETAINED="+same);
                report.AppendLine("EXPECTED=731 / 9.5 / False / 17; ACTUAL="+Number(loaded.MainWindowX)+" / "+
                    Number(loaded.ProtectAoAWarningDegrees)+" / "+loaded.ShowSasWarning+" / "+loaded.FlightDataArchiveLimit);
                if(same) retained++;
                else if(rejected) reproduced++;
                else throw new InvalidOperationException("settings mismatch without root-gate rejection");
            }
            string after=Hash(realPath);
            report.AppendLine("REAL_SETTINGS_SHA256_BEFORE="+before);
            report.AppendLine("REAL_SETTINGS_SHA256_AFTER="+after);
            report.AppendLine("REAL_SETTINGS_SNAPSHOT_UNCHANGED="+(before==after));
            if(before!=after) throw new InvalidOperationException("production settings changed during observation; cannot attribute writer");
            report.AppendLine("REPEATED_ROOT_REJECTIONS="+reproduced+"; RETAINED_ROUNDTRIPS="+retained);
            report.AppendLine("GAP2-01 RESULT="+(reproduced==2?"ROOT_REJECTION_REPRODUCED":retained==2?"NO_REJECTION_OBSERVED":"INCONCLUSIVE_MIXED_RESULTS"));
            return report.ToString();
        }
        catch(Exception e)
        {
            report.AppendLine("GAP2-01 RESULT=INCONCLUSIVE");
            report.AppendLine(e.ToString());
            return report.ToString();
        }
        finally { AERIS53Gap2Scratch.PathName=null; AERIS53Gap2Scratch.Warnings.Clear(); }
    }
}
