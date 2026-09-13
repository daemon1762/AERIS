using System;
using System.Globalization;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // AERIS52 / Coastline C3: load-aware admission control for the accepted
    // C1+C2 Exact CPU coastline fast path.
    //
    // This layer changes only how many already-safe Exact coastline jobs may be
    // outstanding. It never changes sample resolution, terrain authority, payload
    // format, C1 classification, C2 worker math, or persistent commit semantics.
    internal sealed partial class AERISTerrainPreloadBuilder
    {
        struct R052AdmissionInputs
        {
            internal float FrameMs;
            internal float NdMs;
            internal bool WorkerBacklogged;
            internal int GeneralDepth;
            internal int RequiredResultDepth;
            internal double QueueDelayP95Ms;
            internal double WorkerP95Ms;
            internal int EncodeBacklog;
            internal int WriteBacklog;
            internal int WriteActive;
            internal double WriteMbps;
        }

        int r052CoastlineAdmissionCurrent;
        int r052CoastlineAdmissionMinObserved = int.MaxValue;
        int r052CoastlineAdmissionMaxObserved;
        long r052CoastlineAdmissionUpshifts;
        long r052CoastlineAdmissionDownshifts;
        float r052CoastlineAdmissionNextEvaluation;
        float r052CoastlineAdmissionLastChange;
        float r052CoastlineAdmissionNextStateLog;
        string r052CoastlineAdmissionBody = string.Empty;
        string r052CoastlineAdmissionReason = "UNINITIALIZED";
        bool r052AdmissionPolicySelfTestLogged;

        void R052BeginDynamicCoastlineAdmission(string bodyName)
        {
            string normalized = bodyName ?? string.Empty;
            if (string.Equals(r052CoastlineAdmissionBody, normalized,
                StringComparison.OrdinalIgnoreCase) &&
                r052CoastlineAdmissionCurrent > 0)
                return;

            r052CoastlineAdmissionBody = normalized;
            r052CoastlineAdmissionCurrent = 0;
            r052CoastlineAdmissionMinObserved = int.MaxValue;
            r052CoastlineAdmissionMaxObserved = 0;
            r052CoastlineAdmissionUpshifts = 0L;
            r052CoastlineAdmissionDownshifts = 0L;
            r052CoastlineAdmissionNextEvaluation = 0f;
            r052CoastlineAdmissionLastChange = 0f;
            r052CoastlineAdmissionNextStateLog = 0f;
            r052CoastlineAdmissionReason = "INIT";

            R052RunAdmissionPolicySelfTest();
        }

        int R052ResolveDynamicCoastlineAdmissionLimit()
        {
            int workers = Math.Max(2, ResolveWorkerCount());
            int hardMax = Math.Max(2, Math.Min(8, workers));
            float now = Time.realtimeSinceStartup;

            if (r052CoastlineAdmissionCurrent <= 0)
            {
                r052CoastlineAdmissionCurrent = Math.Min(4, hardMax);
                r052CoastlineAdmissionMinObserved =
                    r052CoastlineAdmissionCurrent;
                r052CoastlineAdmissionMaxObserved =
                    r052CoastlineAdmissionCurrent;
                r052CoastlineAdmissionLastChange = now;
                r052CoastlineAdmissionReason = "STARTUP_RAMP";
                R052LogAdmissionChange(
                    0, r052CoastlineAdmissionCurrent,
                    hardMax, hardMax, "STARTUP_RAMP",
                    default(R052AdmissionInputs));
            }
            else if (r052CoastlineAdmissionCurrent > hardMax)
            {
                int previous = r052CoastlineAdmissionCurrent;
                r052CoastlineAdmissionCurrent = hardMax;
                r052CoastlineAdmissionDownshifts++;
                r052CoastlineAdmissionLastChange = now;
                r052CoastlineAdmissionReason = "WORKER_CEILING";
                R052ObserveAdmission(r052CoastlineAdmissionCurrent);
                R052LogAdmissionChange(
                    previous, r052CoastlineAdmissionCurrent,
                    hardMax, hardMax, "WORKER_CEILING",
                    default(R052AdmissionInputs));
            }

            if (now < r052CoastlineAdmissionNextEvaluation)
                return r052CoastlineAdmissionCurrent;
            r052CoastlineAdmissionNextEvaluation = now + 0.50f;

            R052AdmissionInputs inputs = R052CaptureAdmissionInputs();
            string reason;
            int desired = R052ComputeDesiredAdmission(
                inputs, hardMax, out reason);

            int before = r052CoastlineAdmissionCurrent;
            if (desired < before)
            {
                // Protect gameplay and required-result ownership immediately.
                r052CoastlineAdmissionCurrent = desired;
                r052CoastlineAdmissionDownshifts++;
                r052CoastlineAdmissionLastChange = now;
            }
            else if (desired > before &&
                now - r052CoastlineAdmissionLastChange >= 1.50f)
            {
                // Recovery is deliberately one lane at a time. A transient quiet
                // frame can never jump directly from the safety floor to eight jobs.
                r052CoastlineAdmissionCurrent =
                    Math.Min(desired, before + 1);
                r052CoastlineAdmissionUpshifts++;
                r052CoastlineAdmissionLastChange = now;
            }

            r052CoastlineAdmissionReason = reason;
            R052ObserveAdmission(r052CoastlineAdmissionCurrent);

            if (r052CoastlineAdmissionCurrent != before)
            {
                R052LogAdmissionChange(
                    before,
                    r052CoastlineAdmissionCurrent,
                    desired,
                    hardMax,
                    reason,
                    inputs);
            }
            else if (now >= r052CoastlineAdmissionNextStateLog)
            {
                r052CoastlineAdmissionNextStateLog = now + 5f;
                R052LogAdmissionState(
                    r052CoastlineAdmissionCurrent,
                    desired,
                    hardMax,
                    reason,
                    inputs);
            }

            return r052CoastlineAdmissionCurrent;
        }

        R052AdmissionInputs R052CaptureAdmissionInputs()
        {
            var inputs = new R052AdmissionInputs();
            inputs.FrameMs = performance == null ? 16.7f :
                performance.FrameTimeEmaMs;
            inputs.NdMs = performance == null ? 0f :
                performance.NdMainThreadEmaMs;
            inputs.WorkerBacklogged =
                performance != null && performance.WorkerBacklogged;

            AERISRuntimeTelemetrySnapshot scheduler = SnapshotScheduler();
            if (scheduler != null)
            {
                inputs.GeneralDepth = scheduler.GeneralDepth;
                inputs.RequiredResultDepth =
                    scheduler.RequiredCompletionQueueDepth;
                inputs.QueueDelayP95Ms = scheduler.QueueDelayP95Ms;
                inputs.WorkerP95Ms = scheduler.WorkerP95Ms;
            }

            lock (sync)
            {
                inputs.EncodeBacklog =
                    pendingEncodes.Count + inFlightEncodes.Count;
                inputs.WriteBacklog = pendingChunkBatches.Count;
                inputs.WriteActive = activeWriteJobs;
                inputs.WriteMbps = writeMbpsEma;
            }
            return inputs;
        }

        static int R052ComputeDesiredAdmission(
            R052AdmissionInputs inputs,
            int hardMax,
            out string reason)
        {
            hardMax = Math.Max(2, Math.Min(8, hardMax));

            bool severe =
                inputs.FrameMs >= 45f ||
                inputs.NdMs >= 3.0f ||
                inputs.WorkerBacklogged ||
                inputs.RequiredResultDepth >= Math.Max(96, hardMax * 16) ||
                inputs.GeneralDepth >= hardMax * 10 ||
                inputs.QueueDelayP95Ms >= 250.0 ||
                inputs.EncodeBacklog >= 32 ||
                inputs.WriteBacklog >= 12 ||
                (inputs.WriteActive > 0 &&
                 inputs.WriteBacklog >= 4 &&
                 inputs.WriteMbps > 0.0 &&
                 inputs.WriteMbps < 0.50);
            if (severe)
            {
                reason = "SEVERE_LOAD";
                return 2;
            }

            bool high =
                inputs.FrameMs >= 33f ||
                inputs.NdMs >= 1.75f ||
                inputs.RequiredResultDepth >= Math.Max(48, hardMax * 8) ||
                inputs.GeneralDepth >= hardMax * 6 ||
                inputs.QueueDelayP95Ms >= 150.0 ||
                inputs.WorkerP95Ms >= 180.0 ||
                inputs.EncodeBacklog >= 24 ||
                inputs.WriteBacklog >= 8;
            if (high)
            {
                reason = "HIGH_LOAD";
                return Math.Min(3, hardMax);
            }

            bool moderate =
                inputs.FrameMs >= 25f ||
                inputs.NdMs >= 0.80f ||
                inputs.RequiredResultDepth >= Math.Max(24, hardMax * 4) ||
                inputs.GeneralDepth >= hardMax * 3 ||
                inputs.QueueDelayP95Ms >= 80.0 ||
                inputs.WorkerP95Ms >= 120.0 ||
                inputs.EncodeBacklog >= 12 ||
                inputs.WriteBacklog >= 4;
            if (moderate)
            {
                reason = "MODERATE_LOAD";
                return Math.Min(4, hardMax);
            }

            bool mild =
                inputs.RequiredResultDepth >= Math.Max(12, hardMax * 2) ||
                inputs.GeneralDepth >= hardMax * 2 ||
                inputs.QueueDelayP95Ms >= 40.0 ||
                inputs.WorkerP95Ms >= 90.0 ||
                inputs.EncodeBacklog >= 8 ||
                inputs.WriteBacklog >= 2;
            if (mild)
            {
                reason = "MILD_LOAD";
                return Math.Min(6, hardMax);
            }

            reason = "COMFORTABLE";
            return hardMax;
        }

        void R052ObserveAdmission(int limit)
        {
            if (limit <= 0) return;
            if (r052CoastlineAdmissionMinObserved == int.MaxValue ||
                limit < r052CoastlineAdmissionMinObserved)
                r052CoastlineAdmissionMinObserved = limit;
            if (limit > r052CoastlineAdmissionMaxObserved)
                r052CoastlineAdmissionMaxObserved = limit;
        }

        void R052CompleteDynamicCoastlineAdmission(string bodyName)
        {
            if (r052CoastlineAdmissionCurrent <= 0) return;
            AERISLogger.Info(
                "[AERIS52][PRELOAD_COAST_C3]" +
                "; event=SUMMARY" +
                "; body=" + R052Safe(bodyName) +
                "; current=" + r052CoastlineAdmissionCurrent +
                "; min_observed=" +
                    (r052CoastlineAdmissionMinObserved == int.MaxValue ?
                        0 : r052CoastlineAdmissionMinObserved) +
                "; max_observed=" + r052CoastlineAdmissionMaxObserved +
                "; upshifts=" + r052CoastlineAdmissionUpshifts +
                "; downshifts=" + r052CoastlineAdmissionDownshifts +
                "; last_reason=" + r052CoastlineAdmissionReason +
                "; quality_changed=false" +
                "; resolution_changed=false" +
                "; authority_changed=false");
        }

        void R052ResetDynamicCoastlineAdmission(string bodyName)
        {
            if (!string.IsNullOrEmpty(bodyName) &&
                !string.Equals(r052CoastlineAdmissionBody, bodyName,
                    StringComparison.OrdinalIgnoreCase))
                return;

            r052CoastlineAdmissionBody = string.Empty;
            r052CoastlineAdmissionCurrent = 0;
            r052CoastlineAdmissionMinObserved = int.MaxValue;
            r052CoastlineAdmissionMaxObserved = 0;
            r052CoastlineAdmissionUpshifts = 0L;
            r052CoastlineAdmissionDownshifts = 0L;
            r052CoastlineAdmissionNextEvaluation = 0f;
            r052CoastlineAdmissionLastChange = 0f;
            r052CoastlineAdmissionNextStateLog = 0f;
            r052CoastlineAdmissionReason = "UNINITIALIZED";
        }

        void R052RunAdmissionPolicySelfTest()
        {
            if (r052AdmissionPolicySelfTestLogged) return;
            r052AdmissionPolicySelfTestLogged = true;

            string reason;
            int hardMax = 8;
            bool pass = true;

            var severe = new R052AdmissionInputs { FrameMs = 50f };
            pass &= R052ComputeDesiredAdmission(
                severe, hardMax, out reason) == 2;

            var high = new R052AdmissionInputs { FrameMs = 35f };
            pass &= R052ComputeDesiredAdmission(
                high, hardMax, out reason) == 3;

            var moderate = new R052AdmissionInputs { FrameMs = 27f };
            pass &= R052ComputeDesiredAdmission(
                moderate, hardMax, out reason) == 4;

            var mild = new R052AdmissionInputs { WorkerP95Ms = 100.0 };
            pass &= R052ComputeDesiredAdmission(
                mild, hardMax, out reason) == 6;

            var comfortable = new R052AdmissionInputs { FrameMs = 16.7f };
            pass &= R052ComputeDesiredAdmission(
                comfortable, hardMax, out reason) == 8;

            AERISLogger.Info(
                "[AERIS52][PRELOAD_COAST_C3]" +
                "; event=POLICY_SELFTEST" +
                "; pass=" + (pass ? "true" : "false") +
                "; expected=2/3/4/6/8" +
                "; quality_changed=false" +
                "; authority_changed=false");
        }

        void R052LogAdmissionChange(
            int previous,
            int current,
            int desired,
            int hardMax,
            string reason,
            R052AdmissionInputs inputs)
        {
            AERISLogger.Info(
                "[AERIS52][PRELOAD_COAST_C3]" +
                "; event=ADMISSION_CHANGE" +
                "; body=" + R052Safe(r052CoastlineAdmissionBody) +
                "; from=" + previous +
                "; to=" + current +
                "; desired=" + desired +
                "; hard_max=" + hardMax +
                "; reason=" + R052Safe(reason) +
                R052TelemetrySuffix(inputs));
        }

        void R052LogAdmissionState(
            int current,
            int desired,
            int hardMax,
            string reason,
            R052AdmissionInputs inputs)
        {
            AERISLogger.Info(
                "[AERIS52][PRELOAD_COAST_C3]" +
                "; event=ADMISSION_STATE" +
                "; body=" + R052Safe(r052CoastlineAdmissionBody) +
                "; current=" + current +
                "; desired=" + desired +
                "; hard_max=" + hardMax +
                "; reason=" + R052Safe(reason) +
                R052TelemetrySuffix(inputs));
        }

        static string R052TelemetrySuffix(R052AdmissionInputs inputs)
        {
            return
                "; frame_ms=" + inputs.FrameMs.ToString(
                    "0.000", CultureInfo.InvariantCulture) +
                "; nd_ms=" + inputs.NdMs.ToString(
                    "0.000", CultureInfo.InvariantCulture) +
                "; worker_backlogged=" +
                    (inputs.WorkerBacklogged ? "true" : "false") +
                "; general_depth=" + inputs.GeneralDepth +
                "; required_result_depth=" + inputs.RequiredResultDepth +
                "; queue_p95_ms=" + inputs.QueueDelayP95Ms.ToString(
                    "0.000", CultureInfo.InvariantCulture) +
                "; worker_p95_ms=" + inputs.WorkerP95Ms.ToString(
                    "0.000", CultureInfo.InvariantCulture) +
                "; encode_backlog=" + inputs.EncodeBacklog +
                "; write_backlog=" + inputs.WriteBacklog +
                "; write_active=" + inputs.WriteActive +
                "; write_mibs=" + inputs.WriteMbps.ToString(
                    "0.000", CultureInfo.InvariantCulture);
        }

        static string R052Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
