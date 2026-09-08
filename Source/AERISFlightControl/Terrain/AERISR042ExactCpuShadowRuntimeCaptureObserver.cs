using System;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // R042 Phase 4B runtime proof observer.
    // Main-menu only, one-shot, capture-only. No worker execution and no producer switch.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR042ExactCpuShadowRuntimeCaptureObserver : MonoBehaviour
    {
        static readonly string[] TargetBodies =
        {
            "Minmus", "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        int mainThreadId;
        bool reported;
        float nextAttempt;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (reported) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            reported = true;
            RunCapture();
        }

        void RunCapture()
        {
            int captures = 0;
            int failures = 0;
            bool allMain = true;

            AERISLogger.Info(
                "[AERIS42][R042_CAPTURE_BEGIN]" +
                "; target_bodies=7" +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; capture_thread=MAIN_THREAD_ONLY" +
                "; snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false");

            for (int i = 0; i < TargetBodies.Length; i++)
            {
                string bodyName = TargetBodies[i];
                CelestialBody body = FindBody(bodyName);
                AERISR042ExactCpuShadowRuntimeSnapshot snapshot;
                string failure;
                bool ok = AERISR042ExactCpuShadowRuntimeSnapshotBuilder.TryCapture(
                    body,
                    mainThreadId,
                    out snapshot,
                    out failure);

                bool structurallyValid = ok && snapshot != null && snapshot.IsStructurallyValid;
                bool captureThreadMain = structurallyValid && snapshot.CaptureThreadId == mainThreadId;
                bool bodyIdentity = structurallyValid &&
                    string.Equals(snapshot.BodyName, bodyName, StringComparison.OrdinalIgnoreCase);
                bool environmentHashPresent = structurallyValid &&
                    !string.IsNullOrEmpty(snapshot.EnvironmentHash);
                bool topologyPresent = structurallyValid &&
                    !string.IsNullOrEmpty(snapshot.Topology);
                bool pass = ok && structurallyValid && captureThreadMain &&
                    bodyIdentity && environmentHashPresent && topologyPresent;

                if (pass) captures++;
                else failures++;
                allMain &= captureThreadMain;

                AERISLogger.Info(
                    "[AERIS42][R042_CAPTURE_BODY]" +
                    "; body=" + Safe(bodyName) +
                    "; pass=" + Bool(pass) +
                    "; runtime_snapshot_certified=" + Bool(pass) +
                    "; structurally_valid=" + Bool(structurallyValid) +
                    "; capture_thread_is_main=" + Bool(captureThreadMain) +
                    "; candidate=" +
                        (snapshot == null ? "NONE" : snapshot.Candidate.ToString()) +
                    "; topology=" + Safe(snapshot == null ? string.Empty : snapshot.Topology) +
                    "; environment_hash=" +
                        Safe(snapshot == null ? string.Empty : snapshot.EnvironmentHash) +
                    "; failure=" + Safe(pass ? string.Empty : failure) +
                    "; runtime_object_retained=false" +
                    "; production_authority=PQS" +
                    "; producer_switch=false" +
                    "; db_write=false");
            }

            bool completePass =
                captures == TargetBodies.Length &&
                failures == 0 &&
                allMain;

            AERISLogger.Info(
                "[AERIS42][R042_CAPTURE_COMPLETE]" +
                "; pass=" + Bool(completePass) +
                "; bodies=" + TargetBodies.Length.ToString(CultureInfo.InvariantCulture) +
                "; captures=" + captures.ToString(CultureInfo.InvariantCulture) +
                "; failures=" + failures.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; all_capture_thread_main=" + Bool(allMain) +
                "; runtime_object_fields=0" +
                "; snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false");
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null) continue;
                if (string.Equals(body.name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(body.bodyName, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
