using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;
using AERISFlightControl.Terrain;
using AERISFlightControl.UI;

namespace AERISFlightControl.Performance
{
    // OH REV3.5 R015: measurement-only observer for the periodic Full-GC hitch class
    // exposed after R014 reduced content-reconcile churn. Normal 10 Hz sampling performs
    // only GC counter/heap reads and fixed-ring value writes. Renderer reflection is used
    // only when a Gen2 collection is actually observed (plus low-rate target binding).
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public sealed class AERISR015PeriodicGcAttributionObserver : MonoBehaviour
    {
        const string Variant =
            "AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER";
        const string LogPrefix = "[OH_REV3_5_R015_GC_ATTR]";
        const float SampleIntervalSeconds = 0.10f;
        const float AttributionWindowSeconds = 5.0f;
        const float TargetRefreshSeconds = 1.0f;
        const int RingCapacity = 64;
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        struct HeapSample
        {
            internal float Realtime;
            internal long HeapBytes;
        }

        struct TerrainCounters
        {
            internal long ContentTicks;
            internal long ContentCaptures;
            internal long ResolveCalls;
            internal long Publications;
            internal long FullReconciles;
            internal long WorkerOnlySkips;
            internal long PublicationDeferrals;
            internal long PublicationReconciles;
            internal long RetryReconciles;
            internal long FrontSwaps;
        }

        static readonly FieldInfo BootstrapFlightInstrumentField =
            typeof(AERISBootstrap).GetField("flightInstrument", PrivateInstance);
        static readonly FieldInfo FlightInstrumentNavigationDisplayField =
            typeof(AERISFlightInstrument).GetField("navigationDisplay", PrivateInstance);
        static readonly FieldInfo NavigationDisplayRendererField =
            typeof(AERISNavigationDisplay).GetField("terrainTileRenderer", PrivateInstance);

        static readonly FieldInfo ContentTicksField = RendererField("operationHealthContentTicks");
        static readonly FieldInfo ContentCapturesField = RendererField("operationHealthContentCaptures");
        static readonly FieldInfo ResolveCallsField = RendererField("operationHealthResolveCalls");
        static readonly FieldInfo PublicationsField = RendererField("operationHealthRev35R014PublicationEvents");
        static readonly FieldInfo FullReconcilesField = RendererField("operationHealthRev35R014FullReconciles");
        static readonly FieldInfo WorkerOnlySkipsField = RendererField("operationHealthRev35R014WorkerOnlySkips");
        static readonly FieldInfo PublicationDeferralsField = RendererField("operationHealthRev35R014PublicationDeferrals");
        static readonly FieldInfo PublicationReconcilesField = RendererField("operationHealthRev35R014PublicationReconciles");
        static readonly FieldInfo RetryReconcilesField = RendererField("operationHealthRev35R014RetryReconciles");
        static readonly FieldInfo FrontSwapsField = RendererField("frontBufferSwaps");

        readonly HeapSample[] heapRing = new HeapSample[RingCapacity];
        int heapRingNext;
        int heapRingCount;

        AERISBootstrap core;
        AERISTerrainGpuTileRenderer renderer;
        float nextSampleRealtime;
        float nextTargetRefreshRealtime;
        bool wasFlight;
        bool gcBaselineValid;
        int lastGen0;
        int lastGen1;
        int lastGen2;
        float lastFullGcRealtime;
        bool counterBaselineValid;
        TerrainCounters lastFullGcCounters;
        long fullGcEvents;
        long fullGcEventsWithTerrainBaseline;
        long fullGcEventsTerrainHeavyIdle;
        bool bindingWarningLogged;

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
            nextSampleRealtime = 0f;
            nextTargetRefreshRealtime = 0f;
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextSampleRealtime) return;
            nextSampleRealtime = now + SampleIntervalSeconds;

            bool flight = HighLogic.LoadedSceneIsFlight;
            if (!flight)
            {
                if (wasFlight) ResetFlightObservation();
                wasFlight = false;
                return;
            }

            if (!wasFlight)
            {
                ResetFlightObservation();
                wasFlight = true;
            }

            SampleFlight(now);
        }

        void SampleFlight(float now)
        {
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long heapNow = GC.GetTotalMemory(false);

            if (!gcBaselineValid)
            {
                lastGen0 = gen0;
                lastGen1 = gen1;
                lastGen2 = gen2;
                gcBaselineValid = true;
                AddHeapSample(now, heapNow);
                RefreshTargetIfDue(now);
                return;
            }

            int delta0 = NonNegativeDelta(gen0, lastGen0);
            int delta1 = NonNegativeDelta(gen1, lastGen1);
            int delta2 = NonNegativeDelta(gen2, lastGen2);
            lastGen0 = gen0;
            lastGen1 = gen1;
            lastGen2 = gen2;

            RefreshTargetIfDue(now);

            if (delta2 > 0)
                EmitFullGcEvent(now, heapNow, delta0, delta1, delta2);

            AddHeapSample(now, heapNow);
        }

        void RefreshTargetIfDue(float now)
        {
            if (now < nextTargetRefreshRealtime) return;
            nextTargetRefreshRealtime = now + TargetRefreshSeconds;
            ResolveTarget();
        }

        void ResolveTarget()
        {
            if (!BindingsAvailable())
            {
                renderer = null;
                counterBaselineValid = false;
                return;
            }

            if (core == null) core = FindObjectOfType<AERISBootstrap>();
            if (core == null)
            {
                renderer = null;
                counterBaselineValid = false;
                return;
            }

            object flightInstrument = BootstrapFlightInstrumentField.GetValue(core);
            AERISNavigationDisplay navigation = flightInstrument == null ? null :
                FlightInstrumentNavigationDisplayField.GetValue(flightInstrument) as AERISNavigationDisplay;
            AERISTerrainGpuTileRenderer current = navigation == null ? null :
                NavigationDisplayRendererField.GetValue(navigation) as AERISTerrainGpuTileRenderer;

            if (!ReferenceEquals(current, renderer))
            {
                renderer = current;
                counterBaselineValid = false;
                if (renderer != null)
                {
                    lastFullGcCounters = ReadTerrainCounters(renderer);
                    counterBaselineValid = true;
                }
            }

            if (renderer == null && !bindingWarningLogged)
            {
                bindingWarningLogged = true;
                AERISLogger.Warn(LogPrefix +
                    " renderer target unavailable; GC/heap observation continues without terrain attribution.");
            }
            else if (renderer != null)
            {
                bindingWarningLogged = false;
            }
        }

        void EmitFullGcEvent(float now, long heapAfterBytes, int delta0, int delta1, int delta2)
        {
            long heapStart;
            long heapPeak;
            long heapPre;
            int windowSamples;
            ReadPriorHeapWindow(now, out heapStart, out heapPeak, out heapPre, out windowSamples);

            float periodSeconds = lastFullGcRealtime > 0f ? now - lastFullGcRealtime : -1f;
            lastFullGcRealtime = now;
            fullGcEvents += delta2;

            TerrainCounters current = new TerrainCounters();
            TerrainCounters delta = new TerrainCounters();
            bool terrainAvailable = renderer != null && TerrainBindingsAvailable();
            bool terrainDeltaValid = false;
            if (terrainAvailable)
            {
                current = ReadTerrainCounters(renderer);
                if (counterBaselineValid)
                {
                    delta = DeltaCounters(current, lastFullGcCounters);
                    terrainDeltaValid = true;
                    fullGcEventsWithTerrainBaseline += delta2;
                }
                lastFullGcCounters = current;
                counterBaselineValid = true;
            }
            else
            {
                counterBaselineValid = false;
            }

            bool terrainHeavyIdle = terrainDeltaValid &&
                delta.ContentTicks == 0 && delta.ContentCaptures == 0 &&
                delta.ResolveCalls == 0 && delta.Publications == 0 &&
                delta.FullReconciles == 0;
            if (terrainHeavyIdle) fullGcEventsTerrainHeavyIdle += delta2;

            long heapWindowGrowth = windowSamples > 0 ? heapPre - heapStart : 0;
            long heapWindowPeakGrowth = windowSamples > 0 ? heapPeak - heapStart : 0;
            long heapReclaimed = windowSamples > 0 ? heapPre - heapAfterBytes : 0;

            // Event-only logging is deliberate: the normal 10 Hz path above builds no
            // strings and performs no renderer value-field reflection. One diagnostic line
            // per observed Full GC is small compared with the measured multi-MiB heap churn.
            AERISLogger.Info(LogPrefix +
                " variant=" + Variant +
                " event=" + fullGcEvents.ToString(CultureInfo.InvariantCulture) +
                " gen0_delta=" + delta0.ToString(CultureInfo.InvariantCulture) +
                " gen1_delta=" + delta1.ToString(CultureInfo.InvariantCulture) +
                " gen2_delta=" + delta2.ToString(CultureInfo.InvariantCulture) +
                " period_s=" + FormatFloat(periodSeconds) +
                " window_samples=" + windowSamples.ToString(CultureInfo.InvariantCulture) +
                " heap_start=" + heapStart.ToString(CultureInfo.InvariantCulture) +
                " heap_peak=" + heapPeak.ToString(CultureInfo.InvariantCulture) +
                " heap_pre=" + heapPre.ToString(CultureInfo.InvariantCulture) +
                " heap_post=" + heapAfterBytes.ToString(CultureInfo.InvariantCulture) +
                " heap_growth=" + heapWindowGrowth.ToString(CultureInfo.InvariantCulture) +
                " heap_peak_growth=" + heapWindowPeakGrowth.ToString(CultureInfo.InvariantCulture) +
                " heap_reclaimed=" + heapReclaimed.ToString(CultureInfo.InvariantCulture) +
                " terrain_baseline=" + (terrainDeltaValid ? "1" : "0") +
                " terrain_heavy_idle=" + (terrainHeavyIdle ? "1" : "0") +
                " content_tick_delta=" + delta.ContentTicks.ToString(CultureInfo.InvariantCulture) +
                " content_capture_delta=" + delta.ContentCaptures.ToString(CultureInfo.InvariantCulture) +
                " resolve_delta=" + delta.ResolveCalls.ToString(CultureInfo.InvariantCulture) +
                " publication_delta=" + delta.Publications.ToString(CultureInfo.InvariantCulture) +
                " full_reconcile_delta=" + delta.FullReconciles.ToString(CultureInfo.InvariantCulture) +
                " worker_only_skip_delta=" + delta.WorkerOnlySkips.ToString(CultureInfo.InvariantCulture) +
                " publication_defer_delta=" + delta.PublicationDeferrals.ToString(CultureInfo.InvariantCulture) +
                " publication_reconcile_delta=" + delta.PublicationReconciles.ToString(CultureInfo.InvariantCulture) +
                " retry_reconcile_delta=" + delta.RetryReconciles.ToString(CultureInfo.InvariantCulture) +
                " front_swap_delta=" + delta.FrontSwaps.ToString(CultureInfo.InvariantCulture) +
                " gc_with_terrain_baseline=" + fullGcEventsWithTerrainBaseline.ToString(CultureInfo.InvariantCulture) +
                " gc_terrain_heavy_idle=" + fullGcEventsTerrainHeavyIdle.ToString(CultureInfo.InvariantCulture));
        }

        void ReadPriorHeapWindow(float now, out long heapStart, out long heapPeak,
            out long heapPre, out int samples)
        {
            heapStart = 0;
            heapPeak = 0;
            heapPre = 0;
            samples = 0;
            int oldest = heapRingCount == RingCapacity ? heapRingNext : 0;
            for (int offset = 0; offset < heapRingCount; offset++)
            {
                int index = (oldest + offset) % RingCapacity;
                HeapSample sample = heapRing[index];
                if (now - sample.Realtime > AttributionWindowSeconds) continue;
                if (samples == 0)
                {
                    heapStart = sample.HeapBytes;
                    heapPeak = sample.HeapBytes;
                }
                if (sample.HeapBytes > heapPeak) heapPeak = sample.HeapBytes;
                heapPre = sample.HeapBytes;
                samples++;
            }
        }

        void AddHeapSample(float now, long heapBytes)
        {
            heapRing[heapRingNext].Realtime = now;
            heapRing[heapRingNext].HeapBytes = heapBytes;
            heapRingNext++;
            if (heapRingNext >= RingCapacity) heapRingNext = 0;
            if (heapRingCount < RingCapacity) heapRingCount++;
        }

        void ResetFlightObservation()
        {
            heapRingNext = 0;
            heapRingCount = 0;
            gcBaselineValid = false;
            lastFullGcRealtime = 0f;
            nextTargetRefreshRealtime = 0f;
            renderer = null;
            core = null;
            counterBaselineValid = false;
            bindingWarningLogged = false;
        }

        static TerrainCounters ReadTerrainCounters(AERISTerrainGpuTileRenderer target)
        {
            TerrainCounters value = new TerrainCounters();
            value.ContentTicks = ReadLong(ContentTicksField, target);
            value.ContentCaptures = ReadLong(ContentCapturesField, target);
            value.ResolveCalls = ReadLong(ResolveCallsField, target);
            value.Publications = ReadLong(PublicationsField, target);
            value.FullReconciles = ReadLong(FullReconcilesField, target);
            value.WorkerOnlySkips = ReadLong(WorkerOnlySkipsField, target);
            value.PublicationDeferrals = ReadLong(PublicationDeferralsField, target);
            value.PublicationReconciles = ReadLong(PublicationReconcilesField, target);
            value.RetryReconciles = ReadLong(RetryReconcilesField, target);
            value.FrontSwaps = ReadLong(FrontSwapsField, target);
            return value;
        }

        static TerrainCounters DeltaCounters(TerrainCounters current, TerrainCounters previous)
        {
            TerrainCounters value = new TerrainCounters();
            value.ContentTicks = NonNegativeDelta(current.ContentTicks, previous.ContentTicks);
            value.ContentCaptures = NonNegativeDelta(current.ContentCaptures, previous.ContentCaptures);
            value.ResolveCalls = NonNegativeDelta(current.ResolveCalls, previous.ResolveCalls);
            value.Publications = NonNegativeDelta(current.Publications, previous.Publications);
            value.FullReconciles = NonNegativeDelta(current.FullReconciles, previous.FullReconciles);
            value.WorkerOnlySkips = NonNegativeDelta(current.WorkerOnlySkips, previous.WorkerOnlySkips);
            value.PublicationDeferrals = NonNegativeDelta(current.PublicationDeferrals, previous.PublicationDeferrals);
            value.PublicationReconciles = NonNegativeDelta(current.PublicationReconciles, previous.PublicationReconciles);
            value.RetryReconciles = NonNegativeDelta(current.RetryReconciles, previous.RetryReconciles);
            value.FrontSwaps = NonNegativeDelta(current.FrontSwaps, previous.FrontSwaps);
            return value;
        }

        static int NonNegativeDelta(int current, int previous)
        {
            return current >= previous ? current - previous : current;
        }

        static long NonNegativeDelta(long current, long previous)
        {
            return current >= previous ? current - previous : current;
        }

        static long ReadLong(FieldInfo field, object target)
        {
            if (field == null || target == null) return 0;
            object raw = field.GetValue(target);
            if (raw is long) return (long)raw;
            if (raw is int) return (int)raw;
            return 0;
        }

        static FieldInfo RendererField(string name)
        {
            return typeof(AERISTerrainGpuTileRenderer).GetField(name, PrivateInstance);
        }

        static bool BindingsAvailable()
        {
            return BootstrapFlightInstrumentField != null &&
                FlightInstrumentNavigationDisplayField != null &&
                NavigationDisplayRendererField != null;
        }

        static bool TerrainBindingsAvailable()
        {
            return ContentTicksField != null && ContentCapturesField != null &&
                ResolveCallsField != null && PublicationsField != null &&
                FullReconcilesField != null && WorkerOnlySkipsField != null &&
                PublicationDeferralsField != null && PublicationReconcilesField != null &&
                RetryReconcilesField != null && FrontSwapsField != null;
        }

        static string FormatFloat(float value)
        {
            return value < 0f ? "NA" : value.ToString("F3", CultureInfo.InvariantCulture);
        }
    }
}
