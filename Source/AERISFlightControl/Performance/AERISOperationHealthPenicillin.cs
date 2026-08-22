using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Performance
{
    internal enum AERISOperationHealthLogLevel
    {
        Off = 0,
        Normal = 1,
        Diagnostic = 2,
        Trace = 3
    }

    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal sealed class AERISOperationHealthPenicillin : MonoBehaviour
    {
        internal const string Codename = "NOREPINEPHRINE";
        internal const string Revision = "OH_PHASE6_003";
        internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";
        internal const string ObserverVariant = "AERIS26_REV003_OBSERVER_M1";

        const int FrameSampleCapacity = 256;
        const double DefaultSummaryIntervalSeconds = 1.0;
        const double DefaultFrameHitchThresholdMs = 25.0;
        const double DefaultNdHeavyThresholdMs = 8.0;
        const double DefaultTraceCooldownSeconds = 0.25;
        const double MinimumSummaryIntervalSeconds = 0.50;
        const double MaximumSummaryIntervalSeconds = 5.0;

        static AERISOperationHealthPenicillin current;

        const int Rev003ObserverMapLimit = 16384;
        static readonly object rev003ObserverSync = new object();
        static readonly Dictionary<string, long> rev003ObserverLastHitTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> rev003ObserverLastEvictionTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> rev003ObserverDecodeSubmitTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly Dictionary<string, long> rev003ObserverResidentCommitTicks =
            new Dictionary<string, long>(StringComparer.Ordinal);
        static readonly long[] rev003ObserverReuseBuckets = new long[6];
        static readonly long[] rev003ObserverRerequestBuckets = new long[6];
        static readonly long[] rev003ObserverDecodeBuckets = new long[6];
        static readonly long[] rev003ObserverResidentLifeBuckets = new long[6];
        static readonly long[] rev003ObserverEvictionIdleBuckets = new long[6];
        static readonly long[] rev003ObserverLodEvents = new long[5];
        static long rev003ObserverAccessTotal;
        static long rev003ObserverPrepHit;
        static long rev003ObserverDecodeSubmit;
        static long rev003ObserverGetHit;
        static long rev003ObserverGetMiss;
        static long rev003ObserverRamCommit;
        static long rev003ObserverDecodeFailure;
        static long rev003ObserverEvictions;
        static long rev003ObserverBudgetEvictions;
        static long rev003ObserverEvictedBytes;
        static long rev003ObserverReuseSamples;
        static double rev003ObserverReuseTotalMs;
        static double rev003ObserverReuseMaxMs;
        static long rev003ObserverRerequestSamples;
        static double rev003ObserverRerequestTotalMs;
        static double rev003ObserverRerequestMaxMs;
        static long rev003ObserverDecodeSamples;
        static double rev003ObserverDecodeTotalMs;
        static double rev003ObserverDecodeMaxMs;
        static long rev003ObserverResidentLifeSamples;
        static double rev003ObserverResidentLifeTotalMs;
        static double rev003ObserverResidentLifeMaxMs;
        static long rev003ObserverEvictionIdleSamples;
        static double rev003ObserverEvictionIdleTotalMs;
        static double rev003ObserverEvictionIdleMaxMs;
        static long rev003ObserverScopeResets;
        static long rev003ObserverMapResets;
        static long rev003ObserverSelfCalls;
        static long rev003ObserverSelfTicks;
        static long rev003ObserverSelfMaxTicks;

        readonly double[] frameGapSamples = new double[FrameSampleCapacity];
        readonly double[] percentileScratch = new double[FrameSampleCapacity];

        AERISOperationHealthLogLevel logLevel = AERISOperationHealthLogLevel.Diagnostic;
        double summaryIntervalSeconds = DefaultSummaryIntervalSeconds;
        double frameHitchThresholdMs = DefaultFrameHitchThresholdMs;
        double ndHeavyThresholdMs = DefaultNdHeavyThresholdMs;
        double traceCooldownSeconds = DefaultTraceCooldownSeconds;

        int frameSampleIndex;
        int frameSampleCount;
        long lastUpdateTicks;
        long lastFixedTicks;
        long summaryWindowStartTicks;
        long lastTraceTicks;
        int lastGc0;
        int lastGc1;
        int lastGc2;
        int lastGcFrame = -1000000;
        int fixedStepsSinceLastUpdate;
        int catchupFramesWindow;
        int catchupPeakWindow;
        int frameHitchCountWindow;
        int gc0Window;
        int gc1Window;
        int gc2Window;
        int ndBackSamplesWindow;
        int ndHeavyWindow;
        int ndExactPeakWindow;
        int collisionNdFrameWindow;
        int collisionNdPhysicsWindow;
        int collisionNdGcWindow;
        int runtimeSamplesWindow;
        int deferredSummaryCount;
        bool oneXWindowEligible = true;
        bool pendingSummary;
        bool destroyed;

        double fixedSimSecondsWindow;
        double fiveSecondWallSeconds;
        double fiveSecondSimSeconds;
        double latestFiveSecondRealtimeRatio = double.NaN;
        double fixedWallGapMaxMsWindow;
        double physicsDebtPeakMsWindow;
        double frameGapMaxMsWindow;
        double lastFrameGapMs;
        double lastFixedGapMs;
        double ndBackMaxMsWindow;
        double knownMainMaxMsWindow;
        double knownMainEmaMs;
        double commitDrainMaxMsWindow;
        double runtimeFrameMaxMsWindow;
        double selfMaxMsWindow;
        double selfEmaMs;

        Snapshot pending;

        struct Snapshot
        {
            internal double WallSeconds;
            internal double FrameP95Ms;
            internal double FrameMaxMs;
            internal int FrameHitches;
            internal double PhysicsRatio1S;
            internal double PhysicsRatio5S;
            internal double PhysicsDebtPeakMs;
            internal double FixedGapMaxMs;
            internal int CatchupFrames;
            internal int CatchupPeak;
            internal int Gc0;
            internal int Gc1;
            internal int Gc2;
            internal int NdBackSamples;
            internal double NdBackMaxMs;
            internal int NdHeavy;
            internal int NdExactPeak;
            internal int CollisionNdFrame;
            internal int CollisionNdPhysics;
            internal int CollisionNdGc;
            internal int RuntimeSamples;
            internal double RuntimeFrameMaxMs;
            internal double KnownMainMaxMs;
            internal double KnownMainEmaMs;
            internal double CommitDrainMaxMs;
            internal double SelfMaxMs;
            internal double SelfEmaMs;
            internal int DeferredSummaries;
            internal bool OneXEligible;
        }


        internal static void RecordRev003ObserverAccess(string stableId, int lod,
            int kind, long bytes)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;

            long start = Stopwatch.GetTimestamp();
            long now = start;
            lock (rev003ObserverSync)
            {
                rev003ObserverAccessTotal++;
                if (kind == 1) rev003ObserverPrepHit++;
                else if (kind == 2) rev003ObserverDecodeSubmit++;
                else if (kind == 3) rev003ObserverGetHit++;
                else if (kind == 4) rev003ObserverGetMiss++;

                if (lod >= 0 && lod < rev003ObserverLodEvents.Length)
                    rev003ObserverLodEvents[lod]++;

                bool hit = kind == 1 || kind == 3;
                if (hit)
                {
                    long previousHit;
                    if (rev003ObserverLastHitTicks.TryGetValue(stableId,
                        out previousHit) && previousHit > 0L && now >= previousHit)
                    {
                        double ageMs = (now - previousHit) * 1000.0 /
                            Stopwatch.Frequency;
                        if (FiniteNonNegative(ageMs))
                        {
                            rev003ObserverReuseSamples++;
                            rev003ObserverReuseTotalMs += ageMs;
                            rev003ObserverReuseMaxMs = Math.Max(
                                rev003ObserverReuseMaxMs, ageMs);
                            rev003ObserverReuseBuckets[ObserverAgeBucket(ageMs)]++;
                        }
                    }
                    rev003ObserverLastHitTicks[stableId] = now;
                }

                long evicted;
                if (rev003ObserverLastEvictionTicks.TryGetValue(stableId,
                    out evicted) && evicted > 0L && now >= evicted)
                {
                    double ageMs = (now - evicted) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(ageMs))
                    {
                        rev003ObserverRerequestSamples++;
                        rev003ObserverRerequestTotalMs += ageMs;
                        rev003ObserverRerequestMaxMs = Math.Max(
                            rev003ObserverRerequestMaxMs, ageMs);
                        rev003ObserverRerequestBuckets[ObserverAgeBucket(ageMs)]++;
                    }
                    rev003ObserverLastEvictionTicks.Remove(stableId);
                }

                if (kind == 2 &&
                    !rev003ObserverDecodeSubmitTicks.ContainsKey(stableId))
                    rev003ObserverDecodeSubmitTicks[stableId] = now;

                BoundRev003ObserverMapsLocked();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverRamCommit(string stableId, int lod,
            long bytes)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;

            long start = Stopwatch.GetTimestamp();
            long now = start;
            lock (rev003ObserverSync)
            {
                rev003ObserverRamCommit++;
                long submitted;
                if (rev003ObserverDecodeSubmitTicks.TryGetValue(stableId,
                    out submitted) && submitted > 0L && now >= submitted)
                {
                    double latencyMs = (now - submitted) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(latencyMs))
                    {
                        rev003ObserverDecodeSamples++;
                        rev003ObserverDecodeTotalMs += latencyMs;
                        rev003ObserverDecodeMaxMs = Math.Max(
                            rev003ObserverDecodeMaxMs, latencyMs);
                        rev003ObserverDecodeBuckets[ObserverDecodeBucket(latencyMs)]++;
                    }
                    rev003ObserverDecodeSubmitTicks.Remove(stableId);
                }
                rev003ObserverResidentCommitTicks[stableId] = now;
                BoundRev003ObserverMapsLocked();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverDecodeFailure(string stableId)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;
            long start = Stopwatch.GetTimestamp();
            lock (rev003ObserverSync)
            {
                rev003ObserverDecodeFailure++;
                rev003ObserverDecodeSubmitTicks.Remove(stableId);
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverEviction(string stableId, int lod,
            bool budget, long bytes)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off ||
                string.IsNullOrEmpty(stableId)) return;

            long start = Stopwatch.GetTimestamp();
            long now = start;
            lock (rev003ObserverSync)
            {
                rev003ObserverEvictions++;
                if (budget) rev003ObserverBudgetEvictions++;
                rev003ObserverEvictedBytes += Math.Max(0L, bytes);
                rev003ObserverLastEvictionTicks[stableId] = now;

                long committed;
                if (rev003ObserverResidentCommitTicks.TryGetValue(stableId,
                    out committed) && committed > 0L && now >= committed)
                {
                    double lifeMs = (now - committed) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(lifeMs))
                    {
                        rev003ObserverResidentLifeSamples++;
                        rev003ObserverResidentLifeTotalMs += lifeMs;
                        rev003ObserverResidentLifeMaxMs = Math.Max(
                            rev003ObserverResidentLifeMaxMs, lifeMs);
                        rev003ObserverResidentLifeBuckets[ObserverAgeBucket(lifeMs)]++;
                    }
                    rev003ObserverResidentCommitTicks.Remove(stableId);
                }

                long hit;
                if (rev003ObserverLastHitTicks.TryGetValue(stableId, out hit) &&
                    hit > 0L && now >= hit)
                {
                    double idleMs = (now - hit) * 1000.0 /
                        Stopwatch.Frequency;
                    if (FiniteNonNegative(idleMs))
                    {
                        rev003ObserverEvictionIdleSamples++;
                        rev003ObserverEvictionIdleTotalMs += idleMs;
                        rev003ObserverEvictionIdleMaxMs = Math.Max(
                            rev003ObserverEvictionIdleMaxMs, idleMs);
                        rev003ObserverEvictionIdleBuckets[ObserverAgeBucket(idleMs)]++;
                    }
                }

                rev003ObserverDecodeSubmitTicks.Remove(stableId);
                BoundRev003ObserverMapsLocked();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        internal static void RecordRev003ObserverScopeReset()
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off) return;
            long start = Stopwatch.GetTimestamp();
            lock (rev003ObserverSync)
            {
                rev003ObserverScopeResets++;
                rev003ObserverLastHitTicks.Clear();
                rev003ObserverLastEvictionTicks.Clear();
                rev003ObserverDecodeSubmitTicks.Clear();
                rev003ObserverResidentCommitTicks.Clear();
                RecordRev003ObserverSelfLocked(start);
            }
        }

        static int ObserverAgeBucket(double milliseconds)
        {
            if (milliseconds < 1000.0) return 0;
            if (milliseconds < 5000.0) return 1;
            if (milliseconds < 15000.0) return 2;
            if (milliseconds < 60000.0) return 3;
            if (milliseconds < 300000.0) return 4;
            return 5;
        }

        static int ObserverDecodeBucket(double milliseconds)
        {
            if (milliseconds < 5.0) return 0;
            if (milliseconds < 20.0) return 1;
            if (milliseconds < 50.0) return 2;
            if (milliseconds < 100.0) return 3;
            if (milliseconds < 250.0) return 4;
            return 5;
        }

        static void BoundRev003ObserverMapsLocked()
        {
            if (rev003ObserverLastHitTicks.Count <= Rev003ObserverMapLimit &&
                rev003ObserverLastEvictionTicks.Count <= Rev003ObserverMapLimit &&
                rev003ObserverDecodeSubmitTicks.Count <= Rev003ObserverMapLimit &&
                rev003ObserverResidentCommitTicks.Count <= Rev003ObserverMapLimit)
                return;

            rev003ObserverLastHitTicks.Clear();
            rev003ObserverLastEvictionTicks.Clear();
            rev003ObserverDecodeSubmitTicks.Clear();
            rev003ObserverResidentCommitTicks.Clear();
            rev003ObserverMapResets++;
        }

        static void RecordRev003ObserverSelfLocked(long startTicks)
        {
            long elapsed = Math.Max(0L, Stopwatch.GetTimestamp() - startTicks);
            rev003ObserverSelfCalls++;
            rev003ObserverSelfTicks += elapsed;
            rev003ObserverSelfMaxTicks = Math.Max(
                rev003ObserverSelfMaxTicks, elapsed);
        }

        static string ObserverHistogram(long[] buckets)
        {
            return buckets[0] + "/" + buckets[1] + "/" + buckets[2] + "/" +
                buckets[3] + "/" + buckets[4] + "/" + buckets[5];
        }

        static string ObserverLodHistogram(long[] buckets)
        {
            return buckets[0] + "/" + buckets[1] + "/" + buckets[2] + "/" +
                buckets[3] + "/" + buckets[4];
        }

        static string Rev003ObserverSummary()
        {
            lock (rev003ObserverSync)
            {
                double reuseMean = rev003ObserverReuseSamples > 0L ?
                    rev003ObserverReuseTotalMs / rev003ObserverReuseSamples : 0.0;
                double rerequestMean = rev003ObserverRerequestSamples > 0L ?
                    rev003ObserverRerequestTotalMs / rev003ObserverRerequestSamples : 0.0;
                double decodeMean = rev003ObserverDecodeSamples > 0L ?
                    rev003ObserverDecodeTotalMs / rev003ObserverDecodeSamples : 0.0;
                double lifeMean = rev003ObserverResidentLifeSamples > 0L ?
                    rev003ObserverResidentLifeTotalMs / rev003ObserverResidentLifeSamples : 0.0;
                double idleMean = rev003ObserverEvictionIdleSamples > 0L ?
                    rev003ObserverEvictionIdleTotalMs / rev003ObserverEvictionIdleSamples : 0.0;
                double selfMeanUs = rev003ObserverSelfCalls > 0L ?
                    (rev003ObserverSelfTicks * 1000000.0 / Stopwatch.Frequency) /
                        rev003ObserverSelfCalls : 0.0;
                double selfMaxUs = rev003ObserverSelfMaxTicks * 1000000.0 /
                    Stopwatch.Frequency;

                return "; obs_variant=" + ObserverVariant +
                    "; obs_access=" + rev003ObserverAccessTotal +
                    "; obs_prep_hit=" + rev003ObserverPrepHit +
                    "; obs_decode_submit=" + rev003ObserverDecodeSubmit +
                    "; obs_get_hit=" + rev003ObserverGetHit +
                    "; obs_get_miss=" + rev003ObserverGetMiss +
                    "; obs_ram_commit=" + rev003ObserverRamCommit +
                    "; obs_decode_fail=" + rev003ObserverDecodeFailure +
                    "; obs_evict=" + rev003ObserverEvictions +
                    "; obs_budget_evict=" + rev003ObserverBudgetEvictions +
                    "; obs_evict_mib=" + F3(rev003ObserverEvictedBytes / 1048576.0) +
                    "; obs_reuse_samples=" + rev003ObserverReuseSamples +
                    "; obs_reuse_mean_ms=" + F3(reuseMean) +
                    "; obs_reuse_max_ms=" + F3(rev003ObserverReuseMaxMs) +
                    "; obs_reuse_hist=" + ObserverHistogram(rev003ObserverReuseBuckets) +
                    "; obs_rereq_samples=" + rev003ObserverRerequestSamples +
                    "; obs_rereq_mean_ms=" + F3(rerequestMean) +
                    "; obs_rereq_max_ms=" + F3(rev003ObserverRerequestMaxMs) +
                    "; obs_rereq_hist=" + ObserverHistogram(rev003ObserverRerequestBuckets) +
                    "; obs_decode_samples=" + rev003ObserverDecodeSamples +
                    "; obs_decode_mean_ms=" + F3(decodeMean) +
                    "; obs_decode_max_ms=" + F3(rev003ObserverDecodeMaxMs) +
                    "; obs_decode_hist=" + ObserverHistogram(rev003ObserverDecodeBuckets) +
                    "; obs_reslife_samples=" + rev003ObserverResidentLifeSamples +
                    "; obs_reslife_mean_s=" + F3(lifeMean / 1000.0) +
                    "; obs_reslife_max_s=" + F3(rev003ObserverResidentLifeMaxMs / 1000.0) +
                    "; obs_reslife_hist=" + ObserverHistogram(rev003ObserverResidentLifeBuckets) +
                    "; obs_evict_idle_samples=" + rev003ObserverEvictionIdleSamples +
                    "; obs_evict_idle_mean_s=" + F3(idleMean / 1000.0) +
                    "; obs_evict_idle_max_s=" + F3(rev003ObserverEvictionIdleMaxMs / 1000.0) +
                    "; obs_evict_idle_hist=" + ObserverHistogram(rev003ObserverEvictionIdleBuckets) +
                    "; obs_lod_evt_g_f_r_l_land=" + ObserverLodHistogram(rev003ObserverLodEvents) +
                    "; obs_maps=" + rev003ObserverLastHitTicks.Count + "/" +
                        rev003ObserverLastEvictionTicks.Count + "/" +
                        rev003ObserverDecodeSubmitTicks.Count + "/" +
                        rev003ObserverResidentCommitTicks.Count +
                    "; obs_scope_reset=" + rev003ObserverScopeResets +
                    "; obs_map_reset=" + rev003ObserverMapResets +
                    "; obs_self_mean_us=" + F3(selfMeanUs) +
                    "; obs_self_max_us=" + F3(selfMaxUs);
            }
        }

        internal static bool Enabled
        {
            get
            {
                AERISOperationHealthPenicillin instance = current;
                return instance != null &&
                    instance.logLevel != AERISOperationHealthLogLevel.Off;
            }
        }

        internal static void RecordRuntimeFrame(double frameMilliseconds,
            double knownMainMilliseconds, double commitDrainMilliseconds)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off)
                return;

            instance.runtimeSamplesWindow++;
            if (FiniteNonNegative(frameMilliseconds))
                instance.runtimeFrameMaxMsWindow =
                    Math.Max(instance.runtimeFrameMaxMsWindow, frameMilliseconds);

            if (FiniteNonNegative(knownMainMilliseconds))
            {
                instance.knownMainMaxMsWindow =
                    Math.Max(instance.knownMainMaxMsWindow, knownMainMilliseconds);
                instance.knownMainEmaMs = Ema(instance.knownMainEmaMs,
                    knownMainMilliseconds, 0.10);
            }

            if (FiniteNonNegative(commitDrainMilliseconds))
                instance.commitDrainMaxMsWindow =
                    Math.Max(instance.commitDrainMaxMsWindow,
                        commitDrainMilliseconds);
        }

        internal static void RecordNavigationDisplayBack(double backMilliseconds,
            long exactRefreshes, bool steadyReady)
        {
            AERISOperationHealthPenicillin instance = current;
            if (instance == null ||
                instance.logLevel == AERISOperationHealthLogLevel.Off)
                return;
            if (!FiniteNonNegative(backMilliseconds))
                return;

            instance.ndBackSamplesWindow++;
            instance.ndBackMaxMsWindow = Math.Max(instance.ndBackMaxMsWindow,
                backMilliseconds);

            if (exactRefreshes > instance.ndExactPeakWindow)
                instance.ndExactPeakWindow = exactRefreshes > int.MaxValue ?
                    int.MaxValue : (int)exactRefreshes;

            if (!steadyReady ||
                backMilliseconds < instance.ndHeavyThresholdMs)
                return;

            instance.ndHeavyWindow++;

            if (instance.lastFrameGapMs >= instance.frameHitchThresholdMs)
                instance.collisionNdFrameWindow++;

            double fixedThreshold = Math.Max(instance.frameHitchThresholdMs,
                Math.Max(1.0, Time.fixedDeltaTime * 1000.0 * 1.50));
            long nowTicks = Stopwatch.GetTimestamp();
            double sinceFixedMs = instance.lastFixedTicks <= 0L ?
                double.MaxValue :
                (nowTicks - instance.lastFixedTicks) * 1000.0 /
                    Stopwatch.Frequency;

            if (instance.lastFixedGapMs >= fixedThreshold &&
                sinceFixedMs <= Math.Max(100.0, fixedThreshold * 4.0))
                instance.collisionNdPhysicsWindow++;

            int frame = Time.frameCount;
            if (frame - instance.lastGcFrame >= 0 &&
                frame - instance.lastGcFrame <= 1)
                instance.collisionNdGcWindow++;
        }

        void Awake()
        {
            current = this;
            LoadConfiguration();

            long now = Stopwatch.GetTimestamp();
            lastUpdateTicks = now;
            lastFixedTicks = now;
            summaryWindowStartTicks = now;
            lastGc0 = GC.CollectionCount(0);
            lastGc1 = GC.CollectionCount(1);
            lastGc2 = GC.CollectionCount(2);

            if (logLevel != AERISOperationHealthLogLevel.Off)
            {
                AERISLogger.Info("[OH] codename=" + Codename +
                    "; revision=" + Revision +
                    "; candidate=" + Candidate +
                    "; observer_variant=" + ObserverVariant +
                    "; measurement_only=1" +
                    "; control_delta=0" +
                    "; level=" + logLevel.ToString().ToUpperInvariant() +
                    "; passive_only=1; aa_ap_control_touch=0");
            }
        }

        void OnDestroy()
        {
            destroyed = true;
            if (object.ReferenceEquals(current, this))
                current = null;

            if (logLevel != AERISOperationHealthLogLevel.Off)
                AERISLogger.Info("[OH] codename=" + Codename +
                    "; shutdown=1");
        }

        void FixedUpdate()
        {
            if (logLevel == AERISOperationHealthLogLevel.Off || destroyed)
                return;

            long now = Stopwatch.GetTimestamp();

            if (lastFixedTicks > 0L)
            {
                double gapMs = (now - lastFixedTicks) * 1000.0 /
                    Stopwatch.Frequency;
                if (FiniteNonNegative(gapMs))
                {
                    lastFixedGapMs = gapMs;
                    fixedWallGapMaxMsWindow =
                        Math.Max(fixedWallGapMaxMsWindow, gapMs);
                }
            }

            lastFixedTicks = now;

            if (IsOneXRealtime())
                fixedSimSecondsWindow += Math.Max(0.0, Time.fixedDeltaTime);
            else
                oneXWindowEligible = false;

            fixedStepsSinceLastUpdate++;
        }

        void Update()
        {
            if (logLevel == AERISOperationHealthLogLevel.Off || destroyed)
                return;

            long selfStart = Stopwatch.GetTimestamp();
            long now = selfStart;

            if (lastUpdateTicks > 0L)
            {
                double frameGapMs = (now - lastUpdateTicks) * 1000.0 /
                    Stopwatch.Frequency;
                if (FiniteNonNegative(frameGapMs))
                {
                    lastFrameGapMs = frameGapMs;
                    frameGapMaxMsWindow =
                        Math.Max(frameGapMaxMsWindow, frameGapMs);
                    RecordFrameGap(frameGapMs);

                    if (frameGapMs >= frameHitchThresholdMs)
                    {
                        frameHitchCountWindow++;
                        TraceHitchIfNeeded(now, frameGapMs);
                    }
                }
            }

            lastUpdateTicks = now;

            int fixedSteps = fixedStepsSinceLastUpdate;
            fixedStepsSinceLastUpdate = 0;

            if (IsOneXRealtime())
            {
                if (fixedSteps > 1)
                {
                    catchupFramesWindow++;
                    if (fixedSteps > catchupPeakWindow)
                        catchupPeakWindow = fixedSteps;
                }
            }
            else
            {
                oneXWindowEligible = false;
            }

            int gc0 = GC.CollectionCount(0);
            int gc1 = GC.CollectionCount(1);
            int gc2 = GC.CollectionCount(2);
            int d0 = Math.Max(0, gc0 - lastGc0);
            int d1 = Math.Max(0, gc1 - lastGc1);
            int d2 = Math.Max(0, gc2 - lastGc2);

            if (d0 + d1 + d2 > 0)
                lastGcFrame = Time.frameCount;

            gc0Window += d0;
            gc1Window += d1;
            gc2Window += d2;
            lastGc0 = gc0;
            lastGc1 = gc1;
            lastGc2 = gc2;

            double wallSinceWindow = (now - summaryWindowStartTicks) /
                (double)Stopwatch.Frequency;

            if (oneXWindowEligible && wallSinceWindow > 0.0)
            {
                // A healthy FixedUpdate stream can sit anywhere within one fixed-step
                // phase relative to Update. Only debt beyond that normal quantization window
                // is reported as physics backlog.
                double normalFixedPhaseSeconds =
                    Math.Max(0.0, Time.fixedDeltaTime);
                double debtMs = Math.Max(0.0,
                    wallSinceWindow - fixedSimSecondsWindow -
                    normalFixedPhaseSeconds) * 1000.0;
                physicsDebtPeakMsWindow =
                    Math.Max(physicsDebtPeakMsWindow, debtMs);
            }

            if (wallSinceWindow >= summaryIntervalSeconds)
                CaptureSummary(now, wallSinceWindow);

            if (pendingSummary && ShouldEmitSummary())
            {
                EmitSummary(pending);
                pendingSummary = false;
            }

            double selfMs = (Stopwatch.GetTimestamp() - selfStart) *
                1000.0 / Stopwatch.Frequency;

            if (FiniteNonNegative(selfMs))
            {
                selfMaxMsWindow = Math.Max(selfMaxMsWindow, selfMs);
                selfEmaMs = Ema(selfEmaMs, selfMs, 0.10);
            }
        }

        void CaptureSummary(long now, double wallSeconds)
        {
            double physicsRatio1S = double.NaN;
            if (oneXWindowEligible && wallSeconds > 0.0)
                physicsRatio1S = fixedSimSecondsWindow / wallSeconds;

            if (oneXWindowEligible && wallSeconds > 0.0)
            {
                fiveSecondWallSeconds += wallSeconds;
                fiveSecondSimSeconds += fixedSimSecondsWindow;

                if (fiveSecondWallSeconds >= 5.0)
                {
                    latestFiveSecondRealtimeRatio =
                        fiveSecondWallSeconds > 0.0 ?
                        fiveSecondSimSeconds / fiveSecondWallSeconds : 1.0;
                    fiveSecondWallSeconds = 0.0;
                    fiveSecondSimSeconds = 0.0;
                }
            }
            else
            {
                fiveSecondWallSeconds = 0.0;
                fiveSecondSimSeconds = 0.0;
            }

            Snapshot snapshot = new Snapshot();
            snapshot.WallSeconds = wallSeconds;
            snapshot.FrameP95Ms = Percentile95();
            snapshot.FrameMaxMs = frameGapMaxMsWindow;
            snapshot.FrameHitches = frameHitchCountWindow;
            snapshot.PhysicsRatio1S = physicsRatio1S;
            snapshot.PhysicsRatio5S = latestFiveSecondRealtimeRatio;
            snapshot.PhysicsDebtPeakMs = physicsDebtPeakMsWindow;
            snapshot.FixedGapMaxMs = fixedWallGapMaxMsWindow;
            snapshot.CatchupFrames = catchupFramesWindow;
            snapshot.CatchupPeak = catchupPeakWindow;
            snapshot.Gc0 = gc0Window;
            snapshot.Gc1 = gc1Window;
            snapshot.Gc2 = gc2Window;
            snapshot.NdBackSamples = ndBackSamplesWindow;
            snapshot.NdBackMaxMs = ndBackMaxMsWindow;
            snapshot.NdHeavy = ndHeavyWindow;
            snapshot.NdExactPeak = ndExactPeakWindow;
            snapshot.CollisionNdFrame = collisionNdFrameWindow;
            snapshot.CollisionNdPhysics = collisionNdPhysicsWindow;
            snapshot.CollisionNdGc = collisionNdGcWindow;
            snapshot.RuntimeSamples = runtimeSamplesWindow;
            snapshot.RuntimeFrameMaxMs = runtimeFrameMaxMsWindow;
            snapshot.KnownMainMaxMs = knownMainMaxMsWindow;
            snapshot.KnownMainEmaMs = knownMainEmaMs;
            snapshot.CommitDrainMaxMs = commitDrainMaxMsWindow;
            snapshot.SelfMaxMs = selfMaxMsWindow;
            snapshot.SelfEmaMs = selfEmaMs;
            snapshot.DeferredSummaries = deferredSummaryCount;
            snapshot.OneXEligible = oneXWindowEligible;

            pending = snapshot;
            pendingSummary = true;

            summaryWindowStartTicks = now;
            fixedSimSecondsWindow = 0.0;
            fixedWallGapMaxMsWindow = 0.0;
            physicsDebtPeakMsWindow = 0.0;
            frameGapMaxMsWindow = 0.0;
            frameHitchCountWindow = 0;
            catchupFramesWindow = 0;
            catchupPeakWindow = 0;
            gc0Window = 0;
            gc1Window = 0;
            gc2Window = 0;
            ndBackSamplesWindow = 0;
            ndBackMaxMsWindow = 0.0;
            ndHeavyWindow = 0;
            ndExactPeakWindow = 0;
            collisionNdFrameWindow = 0;
            collisionNdPhysicsWindow = 0;
            collisionNdGcWindow = 0;
            runtimeSamplesWindow = 0;
            runtimeFrameMaxMsWindow = 0.0;
            knownMainMaxMsWindow = 0.0;
            commitDrainMaxMsWindow = 0.0;
            selfMaxMsWindow = 0.0;
            oneXWindowEligible = IsOneXRealtime();
        }

        bool ShouldEmitSummary()
        {
            if (logLevel < AERISOperationHealthLogLevel.Diagnostic)
            {
                if (logLevel == AERISOperationHealthLogLevel.Normal)
                    return SnapshotNeedsAttention(pending);
                return false;
            }

            if (lastFrameGapMs >= frameHitchThresholdMs)
            {
                deferredSummaryCount++;
                return false;
            }

            return true;
        }

        static bool SnapshotNeedsAttention(Snapshot value)
        {
            if (value.FrameHitches > 0 ||
                value.CatchupFrames > 0 ||
                value.Gc1 > 0 ||
                value.Gc2 > 0 ||
                value.CollisionNdFrame > 0 ||
                value.CollisionNdPhysics > 0)
                return true;

            if (value.OneXEligible &&
                Finite(value.PhysicsRatio1S) &&
                value.PhysicsRatio1S < 0.98)
                return true;

            return false;
        }

        void EmitSummary(Snapshot value)
        {
            string ratio1 =
                value.OneXEligible && Finite(value.PhysicsRatio1S) ?
                value.PhysicsRatio1S.ToString("F4",
                    CultureInfo.InvariantCulture) : "NA";
            string ratio5 =
                value.OneXEligible && Finite(value.PhysicsRatio5S) ?
                value.PhysicsRatio5S.ToString("F4",
                    CultureInfo.InvariantCulture) : "NA";

            AERISLogger.Info("[OH] codename=" + Codename +
                "; revision=" + Revision +
                "; frame_p95_ms=" + F3(value.FrameP95Ms) +
                "; frame_max_ms=" + F3(value.FrameMaxMs) +
                "; frame_hitch=" + value.FrameHitches +
                "; physics_ratio_1s=" + ratio1 +
                "; physics_ratio_5s=" + ratio5 +
                "; physics_debt_peak_ms=" + F3(value.PhysicsDebtPeakMs) +
                "; fixed_gap_max_ms=" + F3(value.FixedGapMaxMs) +
                "; fixed_catchup_frames=" + value.CatchupFrames +
                "; fixed_catchup_peak=" + value.CatchupPeak +
                "; gc0=" + value.Gc0 +
                "; gc1=" + value.Gc1 +
                "; gc2=" + value.Gc2 +
                "; nd_back_samples=" + value.NdBackSamples +
                "; nd_back_max_ms=" + F3(value.NdBackMaxMs) +
                "; nd_heavy=" + value.NdHeavy +
                "; nd_exact_peak=" + value.NdExactPeak +
                "; collision_nd_frame=" + value.CollisionNdFrame +
                "; collision_nd_physics=" + value.CollisionNdPhysics +
                "; collision_nd_gc=" + value.CollisionNdGc +
                "; runtime_samples=" + value.RuntimeSamples +
                "; runtime_frame_max_ms=" + F3(value.RuntimeFrameMaxMs) +
                "; aeris_known_main_max_ms=" + F3(value.KnownMainMaxMs) +
                "; aeris_known_main_ema_ms=" + F3(value.KnownMainEmaMs) +
                "; commit_max_ms=" + F3(value.CommitDrainMaxMs) +
                "; oh_self_max_ms=" + F3(value.SelfMaxMs) +
                "; oh_self_ema_ms=" + F3(value.SelfEmaMs) +
                "; log_deferred=" + value.DeferredSummaries +
                Rev003ObserverSummary() +
                "; attribution=PARTIAL_PASSIVE");
        }

        void TraceHitchIfNeeded(long now, double frameGapMs)
        {
            if (logLevel != AERISOperationHealthLogLevel.Trace)
                return;

            double sinceTrace = lastTraceTicks <= 0L ?
                double.MaxValue :
                (now - lastTraceTicks) / (double)Stopwatch.Frequency;

            if (sinceTrace < traceCooldownSeconds)
                return;

            lastTraceTicks = now;

            AERISLogger.Warn("[OH_TRACE] codename=" + Codename +
                "; frame_gap_ms=" + F3(frameGapMs) +
                "; fixed_gap_last_ms=" + F3(lastFixedGapMs) +
                "; gc_recent=" +
                ((Time.frameCount - lastGcFrame <= 1) ? "1" : "0") +
                "; note=passive_observation_only");
        }

        void RecordFrameGap(double value)
        {
            frameGapSamples[frameSampleIndex] = value;
            frameSampleIndex =
                (frameSampleIndex + 1) % frameGapSamples.Length;

            if (frameSampleCount < frameGapSamples.Length)
                frameSampleCount++;
        }

        double Percentile95()
        {
            if (frameSampleCount <= 0)
                return 0.0;

            for (int i = 0; i < frameSampleCount; i++)
                percentileScratch[i] = frameGapSamples[i];

            Array.Sort(percentileScratch, 0, frameSampleCount);

            int index =
                (int)Math.Ceiling(frameSampleCount * 0.95) - 1;
            if (index < 0) index = 0;
            if (index >= frameSampleCount)
                index = frameSampleCount - 1;

            return percentileScratch[index];
        }

        void LoadConfiguration()
        {
            try
            {
                string path = System.IO.Path.Combine(
                    KSPUtil.ApplicationRootPath,
                    "GameData", "AERISFlightControl", "Config",
                    "AERISOperationHealth.cfg");

                ConfigNode root = ConfigNode.Load(path);
                if (root == null)
                    return;

                ConfigNode node = root.GetNode("AERIS_OPERATION_HEALTH");
                if (node == null &&
                    root.name == "AERIS_OPERATION_HEALTH")
                    node = root;

                if (node == null)
                    return;

                string enabledText = node.GetValue("enabled");
                bool enabledValue;
                if (!string.IsNullOrEmpty(enabledText) &&
                    bool.TryParse(enabledText, out enabledValue) &&
                    !enabledValue)
                {
                    logLevel = AERISOperationHealthLogLevel.Off;
                    return;
                }

                string levelText = node.GetValue("logLevel");
                if (!string.IsNullOrEmpty(levelText))
                {
                    string normalized =
                        levelText.Trim().ToUpperInvariant();
                    if (normalized == "OFF")
                        logLevel = AERISOperationHealthLogLevel.Off;
                    else if (normalized == "NORMAL")
                        logLevel = AERISOperationHealthLogLevel.Normal;
                    else if (normalized == "DIAGNOSTIC")
                        logLevel = AERISOperationHealthLogLevel.Diagnostic;
                    else if (normalized == "TRACE")
                        logLevel = AERISOperationHealthLogLevel.Trace;
                }

                summaryIntervalSeconds = Clamp(
                    ParseDouble(node, "summaryIntervalSeconds",
                        DefaultSummaryIntervalSeconds),
                    MinimumSummaryIntervalSeconds,
                    MaximumSummaryIntervalSeconds);

                frameHitchThresholdMs = Math.Max(5.0,
                    ParseDouble(node, "frameHitchThresholdMs",
                        DefaultFrameHitchThresholdMs));

                ndHeavyThresholdMs = Math.Max(1.0,
                    ParseDouble(node, "ndHeavyThresholdMs",
                        DefaultNdHeavyThresholdMs));

                traceCooldownSeconds = Math.Max(0.10,
                    ParseDouble(node, "traceCooldownSeconds",
                        DefaultTraceCooldownSeconds));
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[OH] codename=" + Codename +
                    "; config_load_failed=1; detail=" + ex.Message);
            }
        }

        static double ParseDouble(ConfigNode node,
            string key, double fallback)
        {
            string text = node.GetValue(key);
            double value;

            if (!string.IsNullOrEmpty(text) &&
                double.TryParse(text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out value) &&
                Finite(value))
                return value;

            return fallback;
        }

        static bool IsOneXRealtime()
        {
            float scale = Time.timeScale;
            return !float.IsNaN(scale) &&
                !float.IsInfinity(scale) &&
                scale >= 0.95f &&
                scale <= 1.05f;
        }

        static double Ema(double previous,
            double sample, double alpha)
        {
            if (!Finite(previous) || previous <= 0.0)
                return sample;

            return previous +
                alpha * (sample - previous);
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) &&
                !double.IsInfinity(value);
        }

        static bool FiniteNonNegative(double value)
        {
            return Finite(value) && value >= 0.0;
        }

        static double Clamp(double value,
            double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        static string F3(double value)
        {
            return Finite(value) ?
                value.ToString("F3",
                    CultureInfo.InvariantCulture) :
                "NA";
        }
    }
}
