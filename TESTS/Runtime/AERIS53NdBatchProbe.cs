using System;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Security.Cryptography;
using UnityEngine;

// Temporary sidecar. Controlled scenarios run only in renamed private fixture types.
// Live inspection never calls a production clear/publish/draw or changes settings.
[KSPAddon(KSPAddon.Startup.Flight, false)]
public sealed class AERIS53NdBatchProbe : MonoBehaviour
{
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Statics = BindingFlags.Static | BindingFlags.NonPublic;
    string report;
    float nextCheck, trafficOffSince = -1f, ndOffSince = -1f;
    bool trafficOffObserved, trafficOnAgain, ndOffObserved, ndOnAgain, labelStylesReady, finished;

    void Awake()
    {
        try
        {
            string root = Path.Combine(KSPUtil.ApplicationRootPath, "Logs", "AERIS53_ND0102");
            Directory.CreateDirectory(root);
            report = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllText(report, "AERIS53 ND-01/02 TEMPORARY AUDIT v2\nUTC=" + DateTime.UtcNow.ToString("o") + "\n");
            Line("FAULT_SCOPE=renamed private traffic scheduler and GUI boundary fixtures; no live injection");
            foreach (string name in AERIS53NdTestHost.TrafficCases.Names)
            {
                AERIS53NdTestHost.TrafficCases.Run(name);
                Line("ISOLATED_TRAFFIC_PASS=" + name);
            }
            foreach (string name in AERIS53LabelTestHost.LabelCases.Names)
            {
                AERIS53LabelTestHost.LabelCases.Run(name);
                Line("ISOLATED_LABEL_PASS=" + name);
            }
            Line("ISOLATED_RESULT=20/20 PASS");
        }
        catch (Exception ex) { Fail(ex); }
    }

    void Update()
    {
        if (finished || report == null || Time.realtimeSinceStartup < nextCheck) return;
        float now = Time.realtimeSinceStartup;
        nextCheck = now + 0.5f;
        try
        {
            Type hostType = Type.GetType("AERISFlightControl.Core.AERISBootstrap, AERISFlightControl", true);
            object host = hostType.GetField("instance", Statics).GetValue(null);
            if (host == null) return;
            object settings = Read(host, "settings"), instrument = Read(host, "flightInstrument");
            if (settings == null || instrument == null) return;
            object nd = Read(instrument, "navigationDisplay");
            if (nd == null) return;
            Type alertType = hostType.Assembly.GetType("AERISFlightControl.Performance.AERISTrafficAlertApi", true);
            object alert = alertType.GetField("latest", Statics).GetValue(null);
            bool hasAlert = alert != null && (int)Read(alert, "ThreatLevel") > 0;
            bool trafficOn = (bool)Read(settings, "NavigationDisplayTrafficEnabled");
            bool ndOn = Read(settings, "NavigationDisplayMode").ToString() != "Off";
            bool viewport = (bool)Read(nd, "flightViewportActive");

            if (!trafficOn)
            {
                if (trafficOffSince < 0f) trafficOffSince = now;
                if (now - trafficOffSince >= 1f)
                {
                    if (hasAlert) throw new Exception("live alert remains with TRAFFIC OFF");
                    if (!trafficOffObserved)
                    {
                        trafficOffObserved = true;
                        Line("TRAFFIC_OFF_ALERT_ABSENT=True");
                    }
                }
            }
            else { trafficOffSince = -1f; if (trafficOffObserved) trafficOnAgain = true; }
            if (!ndOn)
            {
                if (ndOffSince < 0f) ndOffSince = now;
                if (now - ndOffSince >= 1f)
                {
                    if (hasAlert || viewport) throw new Exception("ND OFF did not clear/deactivate");
                    if (!ndOffObserved)
                    {
                        ndOffObserved = true;
                        Line("ND_OFF_ALERT_ABSENT=True");
                    }
                }
            }
            else { ndOffSince = -1f; if (ndOffObserved && viewport) ndOnAgain = true; }

            IDictionary cache = (IDictionary)Read(instrument, "readableLabelStyles");
            object content = Read(instrument, "fittingContent");
            string heldText = (string)content.GetType().GetProperty("text").GetValue(content, null);
            if (cache.Count > 4 || !string.IsNullOrEmpty(heldText))
                throw new Exception("live label cache/content invariant failed");
            // EnsureStyles may clear the fitting cache again when switching from
            // the resized FDI panel to the gauge scale at the end of a GUI event.
            // An empty cache between events is valid; do not mistake it for no UI.
            if (Read(instrument, "labelStyle") != null &&
                Read(settings, "FlightInstrumentDisplayMode").ToString() != "Off")
                labelStylesReady = true;
            if (!trafficOffObserved || !trafficOnAgain || !ndOffObserved || !ndOnAgain || !labelStylesReady) return;
            Line("TRAFFIC_ON_AGAIN=True; ND_ON_AGAIN=True; LABEL_STYLES_READY=True");
            Line("LABEL_CACHE_COUNT=" + cache.Count + "; FITTING_CONTENT_EMPTY=True");
            Line("LIVE_AERIS_ASSEMBLY=" + hostType.Assembly.Location);
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(hostType.Assembly.Location))
                Line("LIVE_DLL_SHA256=" + BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant());
            Line("LIVE_RESULT=PASS");
            finished = true;
        }
        catch (Exception ex) { Fail(ex); }
    }

    static object Read(object owner, string name)
    {
        return owner.GetType().GetField(name, Fields).GetValue(owner);
    }
    void Fail(Exception ex)
    {
        finished = true;
        if (report != null) { try { Line("AUDIT_ERROR=" + ex.GetType().Name + ": " + ex.Message); } catch { } }
    }
    void Line(string text) { File.AppendAllText(report, text + "\n"); }
}
