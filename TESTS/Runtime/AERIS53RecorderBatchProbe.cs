using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

// Temporary sidecar: isolated admission faults never touch the production recorder.
// Live inspection reads fields only; no control, settings, logger or recorder calls.
[KSPAddon(KSPAddon.Startup.Flight, false)]
public sealed class AERIS53RecorderBatchProbe : MonoBehaviour
{
    const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;
    string report;
    string observedFolder;
    float started;
    float nextObservation;
    int firstSamples;
    long firstOrdinal;
    bool finished;

    void Awake()
    {
        try
        {
            string root = Path.Combine(KSPUtil.ApplicationRootPath, "Logs", "AERIS53_REC0304");
            Directory.CreateDirectory(root);
            report = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(report, "AERIS53 R-REC-03/04 TEMPORARY AUDIT v1\nUTC=" + DateTime.UtcNow.ToString("o") + "\n");
            Line("FAULT_SCOPE=memory-only extracted startup/cleanup; no production fault injection");
            foreach (string name in AERIS53RecorderBatchIsolated.Cases.Names)
            {
                AERIS53RecorderBatchIsolated.Cases.Run(name);
                Line("ISOLATED_PASS=" + name);
            }
            Line("ISOLATED_RESULT=15/15 PASS");
        }
        catch (Exception ex)
        {
            if (report != null) Line("AUDIT_ERROR=" + ex.GetType().Name + ": " + ex.Message);
            finished = true;
        }
    }

    void Update()
    {
        if (finished || report == null || Time.realtimeSinceStartup < nextObservation) return;
        nextObservation = Time.realtimeSinceStartup + 1f;
        try
        {
            Type hostType = Type.GetType("AERISFlightControl.Core.AERISBootstrap, AERISFlightControl", true);
            object host = hostType.GetField("instance", StaticFields).GetValue(null);
            if (host == null) return;
            object recorder = hostType.GetField("recorder", InstanceFields).GetValue(host);
            if (recorder == null) return;
            Type type = recorder.GetType();
            string folder = (string)Field(type, recorder, "folder");
            if (string.IsNullOrEmpty(folder)) return;
            int samples = (int)Field(type, recorder, "sampleCount");
            long ordinal = (long)Field(type, recorder, "sessionOrdinal");
            if (observedFolder != folder)
            {
                observedFolder = folder;
                started = Time.realtimeSinceStartup;
                firstSamples = samples;
                firstOrdinal = ordinal;
                Line("OBSERVING_FOLDER=" + folder);
                return;
            }
            if (Time.realtimeSinceStartup - started < 10f || samples - firstSamples < 20) return;
            bool disabled = !(bool)type.GetField("R016HighRateDiagnosticsEnabled", StaticFields).GetValue(null);
            string[] diagnostics = { "bankDiagnosticsWriter", "apSmoothnessWriter", "vsDiagnosticsWriter",
                "vsCruiseAccelerationGuideWriter", "pitchDiagnosticsWriter", "hdgDiagnosticsWriter",
                "altDiagnosticsWriter", "accelerationDiagnosticsWriter", "velocityDiagnosticsWriter",
                "groundTakeoffDiagnosticsWriter" };
            bool absent = true;
            foreach (string field in diagnostics) absent &= Field(type, recorder, field) == null;
            bool core = Available(Field(type, recorder, "cvrWriter")) && Available(Field(type, recorder, "fdrWriter"));
            bool healthy = disabled && absent && core && firstOrdinal == ordinal &&
                Field(type, recorder, "failedStartVesselId") == null;
            Line("HIGH_RATE_DISABLED=" + disabled + "; UNUSED_CHANNELS_ABSENT=" + absent + "; CORE_AVAILABLE=" + core);
            Line("FDR_SAMPLE_GROWTH=" + (samples - firstSamples) + "; STABLE_SESSION=" + (firstOrdinal == ordinal));
            Line("LIVE_FOLDER=" + folder);
            Line("LIVE_AERIS_ASSEMBLY=" + type.Assembly.Location);
            Line("LIVE_DLL_SHA256=" + Hash(type.Assembly.Location));
            Line("LIVE_RESULT=" + (healthy ? "LIVE_CORE_OBSERVED" : "FAIL"));
            finished = true;
        }
        catch (Exception ex)
        {
            Line("AUDIT_ERROR=" + ex.GetType().Name + ": " + ex.Message);
            finished = true;
        }
    }

    static object Field(Type type, object instance, string name)
    {
        return type.GetField(name, InstanceFields).GetValue(instance);
    }

    static bool Available(object channel)
    {
        return channel != null && (bool)channel.GetType().GetProperty("Available",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel, null);
    }

    static string Hash(string path)
    {
        using (var sha = SHA256.Create())
        using (var stream = File.OpenRead(path))
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
    }

    void Line(string text) { File.AppendAllText(report, text + "\n"); }
}
