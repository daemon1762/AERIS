using System;
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
        internal const string Codename = "PENICILLIN";
        internal const string Revision = "OH_PHASE2_001";
        internal const string Candidate = "AERIS23_OH_PENICILLIN";

        const int FrameSampleCapacity = 256;
        const double DefaultSummaryIntervalSeconds = 1.0;
        const double DefaultFrameHitchThresholdMs = 25.0;
        const double DefaultNdHeavyThresholdMs = 8.0;
        const double DefaultTraceCooldownSeconds = 0.25;
        const double MinimumSummaryIntervalSeconds = 0.50;
        const double MaximumSummaryIntervalSeconds = 5.0;

        static AERISOperationHealthPenicillin current;

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
        double latestFiveSecondRealtimeRatio = 1.0;
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
                double debtMs = Math.Max(0.0,
                    wallSinceWindow - fixedSimSecondsWindow) * 1000.0;
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
