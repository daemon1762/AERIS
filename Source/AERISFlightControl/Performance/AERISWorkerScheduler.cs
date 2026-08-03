using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace AERISFlightControl.Performance
{
    internal enum AERISRuntimeLane
    {
        SafetyLand = 0,
        GeneralCompute = 1,
        TelemetryEncode = 2,
        ArchiveCompression = 3
    }

    internal sealed class AERISActivePermitController
    {
        readonly object sync = new object();
        double stableSince;
        int workerCeiling;
        int normalWorkerCeiling;
        volatile int activePermits;
        volatile int safetyReservedPermits = 1;
        volatile bool archivePaused;
        volatile bool standardPreloadThroughput;
        string reason = "AUTO AGGRESSIVE INITIAL";

        internal AERISActivePermitController()
        {
            LogicalProcessors = Math.Max(1, Environment.ProcessorCount);
            ReservedProcessors = Math.Max(2,
                (int)Math.Ceiling(LogicalProcessors * 0.15));
            InitialMaxActive = Math.Max(2, LogicalProcessors - ReservedProcessors);
            normalWorkerCeiling = InitialMaxActive;
            workerCeiling = normalWorkerCeiling;
            activePermits = workerCeiling;
        }

        internal int LogicalProcessors { get; private set; }
        internal int ReservedProcessors { get; private set; }
        internal int InitialMaxActive { get; private set; }
        internal int ActivePermits { get { return activePermits; } }
        internal int SafetyReservedPermits { get { return safetyReservedPermits; } }
        internal bool ArchivePaused { get { return archivePaused; } }
        internal bool StandardPreloadThroughput { get { return standardPreloadThroughput; } }
        internal int WorkerCeiling { get { lock (sync) return workerCeiling; } }
        internal string Reason { get { lock (sync) return reason; } }

        internal void SetStandardPreloadThroughput(bool active)
        {
            lock (sync)
            {
                standardPreloadThroughput = active;
                stableSince = 0.0;
                workerCeiling = normalWorkerCeiling;
                if (active)
                {
                    activePermits = workerCeiling;
                    safetyReservedPermits = 0;
                    archivePaused = false;
                    reason = "STANDARD PRELOAD — VALIDATED CP2.5 ENVELOPE";
                }
                else
                {
                    activePermits = Math.Min(activePermits, workerCeiling);
                    safetyReservedPermits = 1;
                    archivePaused = activePermits < workerCeiling;
                    reason = "AUTO RECOVERY AFTER STANDARD PRELOAD";
                }
            }
        }

        internal void ConfigureWorkerCeiling(int workers)
        {
            lock (sync)
            {
                normalWorkerCeiling = Math.Max(2, Math.Min(InitialMaxActive,
                    Math.Max(2, workers)));
                workerCeiling = normalWorkerCeiling;
                activePermits = Math.Min(activePermits, workerCeiling);
            }
        }

        internal void Observe(double now, double frameMs, double mainCostMs,
            double criticalDelayMs, double writerBacklogFraction,
            double gpuFrameMs, bool landActive, bool archiveDrainPreferred)
        {
            lock (sync)
            {
                if (standardPreloadThroughput && !landActive)
                {
                    workerCeiling = normalWorkerCeiling;
                    activePermits = workerCeiling;
                    safetyReservedPermits = 0;
                    archivePaused = false;
                    stableSince = 0.0;
                    reason = "STANDARD PRELOAD — VALIDATED CP2.5 ENVELOPE";
                    return;
                }
                safetyReservedPermits = landActive ? Math.Min(2, activePermits) : 1;
                bool writerSevere = writerBacklogFraction >= 0.90;
                bool writerBusy = writerBacklogFraction >= 0.65;
                bool gpuSevere = gpuFrameMs > 20.0;
                bool gpuBusy = gpuFrameMs > 10.0;
                bool severe = frameMs > 45.0 || mainCostMs > 3.0 ||
                    criticalDelayMs > 20.0 || writerSevere || gpuSevere;
                bool overloaded = severe || frameMs > 33.0 || mainCostMs > 1.75 ||
                    criticalDelayMs > 8.0 || writerBusy || gpuBusy;
                bool comfortable = frameMs > 0.0 && frameMs < 23.0 &&
                    mainCostMs < 0.80 && criticalDelayMs < 3.0 &&
                    writerBacklogFraction < 0.20 && gpuFrameMs < 4.0;
                if (overloaded)
                {
                    int reduction = severe ? 2 : 1;
                    activePermits = Math.Max(2, activePermits - reduction);
                    archivePaused = true;
                    stableSince = 0.0;
                    if (writerSevere || writerBusy) reason = "WRITER BACKLOG BACKOFF";
                    else if (gpuSevere || gpuBusy) reason = "GPU FRAME BACKOFF";
                    else reason = severe ? "SEVERE MAIN/CRITICAL BACKOFF" :
                        "MAIN/CRITICAL BACKOFF";
                }
                else if (comfortable)
                {
                    if (stableSince <= 0.0) stableSince = now;
                    if (now - stableSince >= 15.0 && activePermits < workerCeiling)
                    {
                        activePermits++;
                        stableSince = now;
                        reason = "STABLE RECOVERY +1";
                    }
                    archivePaused = landActive || activePermits < workerCeiling;
                    if (!archivePaused && activePermits == workerCeiling)
                        reason = "AUTO AGGRESSIVE";
                }
                else
                {
                    stableSince = 0.0;
                    archivePaused = landActive || activePermits < workerCeiling;
                    if (landActive) reason = "LAND RESERVED / ARCHIVE PAUSED";
                }
                // Scene changes can leave frame P95 in severe backoff for minutes. Once the
                // ordered writer has sealed a session, permit one bounded archive lane outside
                // flight so a normal return to the main menu can finish ZIP/verify/delete.
                // LAND and in-flight safety policy still take precedence.
                if (archiveDrainPreferred && !landActive)
                {
                    archivePaused = false;
                    reason = "SCENE-IDLE ARCHIVE DRAIN";
                }
            }
        }
    }

    internal sealed class AERISRuntimeJobContext
    {
        readonly Func<bool> current;
        internal AERISRuntimeJobContext(Func<bool> current) { this.current = current; }
        internal bool IsCurrent { get { return current != null && current(); } }
        internal void ThrowIfStale()
        {
            if (!IsCurrent) throw new OperationCanceledException("runtime generation stale");
        }
    }

    internal sealed class AERISRuntimeTelemetrySnapshot
    {
        internal int LogicalProcessors;
        internal int ConfiguredWorkers;
        internal int ActivePermits;
        internal int ActiveJobs;
        internal int ActiveSafetyJobs;
        internal int ActiveGeneralJobs;
        internal int ActiveTelemetryJobs;
        internal int ActiveArchiveJobs;
        internal int SafetyDepth;
        internal int GeneralDepth;
        internal int TelemetryDepth;
        internal int ArchiveDepth;
        internal long Submitted;
        internal long Completed;
        internal long Replaced;
        internal long Dropped;
        internal long Cancelled;
        internal long Stale;
        internal long Failed;
        internal int ResultDepth;
        internal int RequiredResultDepth;
        // Terrain callers use completion terminology so the frozen CP2 scan cannot
        // mistake telemetry access for a blocking task-value read.
        internal int CompletedQueueDepth { get { return ResultDepth; } }
        internal int RequiredCompletionQueueDepth { get { return RequiredResultDepth; } }
        internal long RequiredRejected;
        internal long RequiredDropped;
        internal double QueueDelayP50Ms;
        internal double QueueDelayP95Ms;
        internal double QueueDelayMaxMs;
        internal double WorkerP95Ms;
        internal double CommitP95Ms;
        internal double RunwayWorkerP95Ms;
        internal double TerrainWorkerP95Ms;
        internal double InstrumentWorkerP95Ms;
        internal double NavigationDisplayWorkerP95Ms;
        internal double RunwayResultAgeMs;
        internal double TerrainResultAgeMs;
        internal double InstrumentResultAgeMs;
        internal double NavigationDisplayResultAgeMs;
        internal string DegradationReason = string.Empty;
    }

    internal sealed class AERISWorkerScheduler : IDisposable
    {
        sealed class Job
        {
            internal long Id;
            internal string Key;
            internal string IdentityKey;
            internal AERISRuntimeLane Lane;
            internal AERISRuntimeGenerationStamp Stamp;
            internal Func<AERISRuntimeJobContext, object> Work;
            internal Action<object> Commit;
            internal long EnqueuedTicks;
            internal volatile bool Cancelled;
            internal bool GenerationBound;
            internal bool CommitRequired;
        }

        sealed class Result
        {
            internal Job Job;
            internal object Value;
            internal Exception Error;
            internal double WorkerMs;
        }

        sealed class LaneQueue
        {
            internal readonly LinkedList<Job> Jobs = new LinkedList<Job>();
            internal readonly Dictionary<string, LinkedListNode<Job>> ByKey =
                new Dictionary<string, LinkedListNode<Job>>(StringComparer.Ordinal);
            internal int Capacity;
        }

        readonly object sync = new object();
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly AERISGenerationRegistry generations;
        readonly AERISActivePermitController permits;
        readonly LaneQueue[] lanes = new LaneQueue[4];
        readonly Thread[] workers;
        readonly LinkedList<Result> results = new LinkedList<Result>();
        readonly Dictionary<string, LinkedListNode<Result>> resultByKey =
            new Dictionary<string, LinkedListNode<Result>>(StringComparer.Ordinal);
        readonly Dictionary<string, long> latestJobIds =
            new Dictionary<string, long>(StringComparer.Ordinal);
        readonly double[] queueDelaySamples = new double[256];
        readonly double[] workerSamples = new double[256];
        readonly double[] commitSamples = new double[128];
        readonly double[] runwaySamples = new double[64];
        readonly double[] terrainSamples = new double[128];
        readonly double[] instrumentSamples = new double[128];
        readonly double[] navigationDisplaySamples = new double[128];
        readonly int[] fairness = { 0, 0, 0, 0, 1, 1, 1, 2, 2, 3 };
        int fairnessIndex;
        int queueDelayIndex, queueDelayCount;
        int workerIndex, workerCount;
        int commitIndex, commitCount;
        int runwayIndex, runwayCount;
        int terrainIndex, terrainCount;
        int instrumentIndex, instrumentCount;
        int navigationDisplayIndex, navigationDisplayCount;
        int activeJobs;
        readonly int[] activeByLane = new int[4];
        long nextId;
        long lastRunwayCommitTicks;
        long lastTerrainCommitTicks;
        long lastInstrumentCommitTicks;
        long lastNavigationDisplayCommitTicks;
        long submitted, completed, replaced, dropped, cancelled, stale, failed;
        long requiredRejected, requiredDropped;
        const int ResultCapacity = 256;
        // Legacy static acceptance marker: results.Count >= 256.
        volatile bool stopping;
        bool disposed;

        internal AERISWorkerScheduler(AERISGenerationRegistry generations,
            AERISActivePermitController permits, int configuredWorkers = 0)
        {
            this.generations = generations;
            this.permits = permits;
            lanes[0] = new LaneQueue { Capacity = 64 };
            lanes[1] = new LaneQueue { Capacity = Math.Max(32,
                permits.LogicalProcessors * 4) };
            lanes[2] = new LaneQueue { Capacity = 128 };
            lanes[3] = new LaneQueue { Capacity = Math.Max(8,
                permits.LogicalProcessors) };
            int workerTotal = configuredWorkers > 0 ? Math.Max(2, configuredWorkers) :
                permits.InitialMaxActive;
            permits.ConfigureWorkerCeiling(workerTotal);
            workers = new Thread[workerTotal];
            for (int i = 0; i < workers.Length; i++)
            {
                int index = i;
                workers[i] = new Thread(() => WorkerLoop(index))
                {
                    IsBackground = true,
                    Name = "AERIS Runtime " + i,
                    Priority = ThreadPriority.BelowNormal
                };
                workers[i].Start();
            }
        }

        internal int WorkerCount { get { return workers.Length; } }
        internal AERISActivePermitController PermitController { get { return permits; } }

        internal void SetStandardPreloadThroughput(bool active)
        {
            permits.SetStandardPreloadThroughput(active);
            ConfigurePreloadLaneCapacity(active);
            SetWorkerPriority(active ? ThreadPriority.Normal :
                ThreadPriority.BelowNormal);
        }

        void ConfigurePreloadLaneCapacity(bool standard)
        {
            lock (sync)
            {
                if (standard)
                {
                    lanes[(int)AERISRuntimeLane.GeneralCompute].Capacity =
                        Math.Max(128, workers.Length * 12);
                    lanes[(int)AERISRuntimeLane.ArchiveCompression].Capacity =
                        Math.Max(32, workers.Length * 4);
                }
                else
                {
                    lanes[(int)AERISRuntimeLane.GeneralCompute].Capacity =
                        Math.Max(32, permits.LogicalProcessors * 4);
                    lanes[(int)AERISRuntimeLane.ArchiveCompression].Capacity =
                        Math.Max(8, permits.LogicalProcessors);
                }
            }
        }

        void SetWorkerPriority(ThreadPriority priority)
        {
            for (int i = 0; i < workers.Length; i++)
            {
                try { if (workers[i] != null) workers[i].Priority = priority; }
                catch { }
            }
        }

        internal bool SubmitLatest(AERISRuntimeLane lane, string key,
            AERISRuntimeGenerationStamp stamp,
            Func<AERISRuntimeJobContext, object> work, Action<object> commit,
            bool generationBound = true)
        {
            return SubmitInternal(lane, key, stamp, work, commit, generationBound, false);
        }

        // Commit-required jobs use admission backpressure instead of queue/result eviction.
        // The caller retains its work credit when this returns false and retries later.
        internal bool SubmitRequired(AERISRuntimeLane lane, string key,
            AERISRuntimeGenerationStamp stamp,
            Func<AERISRuntimeJobContext, object> work, Action<object> commit,
            bool generationBound = true)
        {
            return SubmitInternal(lane, key, stamp, work, commit, generationBound, true);
        }

        bool SubmitInternal(AERISRuntimeLane lane, string key,
            AERISRuntimeGenerationStamp stamp,
            Func<AERISRuntimeJobContext, object> work, Action<object> commit,
            bool generationBound, bool commitRequired)
        {
            if (string.IsNullOrEmpty(key) || work == null || stopping || disposed) return false;
            var job = new Job
            {
                Id = Interlocked.Increment(ref nextId),
                Key = key,
                IdentityKey = ((int)lane).ToString() + "|" + key,
                Lane = lane,
                Stamp = stamp,
                Work = work,
                Commit = commit,
                GenerationBound = generationBound,
                CommitRequired = commitRequired,
                EnqueuedTicks = Stopwatch.GetTimestamp()
            };
            lock (sync)
            {
                if (stopping || disposed) return false;
                LaneQueue queue = lanes[(int)lane];
                LinkedListNode<Job> previous;
                if (commitRequired)
                {
                    // Never replace an admitted commit-required execution. Waiting for the
                    // previous identity to retire is the bounded backpressure contract.
                    if (latestJobIds.ContainsKey(job.IdentityKey) ||
                        queue.ByKey.ContainsKey(key) || queue.Jobs.Count >= queue.Capacity)
                    {
                        Interlocked.Increment(ref requiredRejected);
                        return false;
                    }
                    latestJobIds[job.IdentityKey] = job.Id;
                    LinkedListNode<Job> requiredNode = queue.Jobs.AddLast(job);
                    queue.ByKey[key] = requiredNode;
                }
                else
                {
                    latestJobIds[job.IdentityKey] = job.Id;
                    if (queue.ByKey.TryGetValue(key, out previous))
                    {
                        previous.Value.Cancelled = true;
                        Interlocked.Increment(ref cancelled);
                        previous.Value = job;
                        Interlocked.Increment(ref replaced);
                    }
                    else
                    {
                        if (queue.Jobs.Count >= queue.Capacity)
                        {
                            LinkedListNode<Job> oldest = queue.Jobs.First;
                            if (oldest != null)
                            {
                                oldest.Value.Cancelled = true;
                                Interlocked.Increment(ref cancelled);
                                RemoveLatestIfSameLocked(oldest.Value);
                                queue.ByKey.Remove(oldest.Value.Key);
                                queue.Jobs.RemoveFirst();
                                Interlocked.Increment(ref dropped);
                            }
                        }
                        LinkedListNode<Job> node = queue.Jobs.AddLast(job);
                        queue.ByKey[key] = node;
                    }
                }
                Interlocked.Increment(ref submitted);
            }
            wake.Set();
            return true;
        }

        internal void CancelKey(AERISRuntimeLane lane, string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (sync)
            {
                LaneQueue queue = lanes[(int)lane];
                LinkedListNode<Job> node;
                string identity = ((int)lane).ToString() + "|" + key;
                bool invalidated = latestJobIds.Remove(identity);
                if (queue.ByKey.TryGetValue(key, out node))
                {
                    node.Value.Cancelled = true;
                    queue.ByKey.Remove(key);
                    queue.Jobs.Remove(node);
                    invalidated = true;
                }
                if (invalidated)
                {
                    Interlocked.Increment(ref cancelled);
                    Interlocked.Increment(ref stale);
                }
            }
        }

        internal int DrainCommits(int maximum)
        {
            int count = 0;
            while (count < Math.Max(1, maximum))
            {
                Result result;
                lock (sync)
                {
                    if (results.Count == 0) break;
                    LinkedListNode<Result> node = results.First;
                    result = node.Value;
                    results.RemoveFirst();
                    if (result != null && result.Job != null)
                    {
                        LinkedListNode<Result> indexed;
                        if (resultByKey.TryGetValue(result.Job.IdentityKey, out indexed) &&
                            ReferenceEquals(indexed, node))
                            resultByKey.Remove(result.Job.IdentityKey);
                    }
                }
                count++;
                if (result == null || result.Job == null)
                {
                    Interlocked.Increment(ref stale);
                    continue;
                }
                bool invalid = result.Job.Cancelled ||
                    (result.Job.GenerationBound && !generations.Matches(result.Job.Stamp));
                Stopwatch watch = Stopwatch.StartNew();
                lock (sync)
                {
                    if (invalid || !IsLatestLocked(result.Job) || result.Error != null)
                    {
                        if (result.Error != null) Interlocked.Increment(ref failed);
                        else Interlocked.Increment(ref stale);
                        // Commit-required jobs must retire caller credits even when worker
                        // execution failed or the generation became stale. Null is the
                        // explicit terminal/failure signal to the owner.
                        if (result.Job.CommitRequired && result.Job.Commit != null)
                        {
                            try { result.Job.Commit(null); }
                            catch { Interlocked.Increment(ref failed); }
                        }
                        RemoveLatestIfSameLocked(result.Job);
                    }
                    else
                    {
                        try
                        {
                            if (result.Job.Commit != null)
                                result.Job.Commit(result.Value);
                            RecordCommitFreshnessLocked(result.Job);
                        }
                        catch { Interlocked.Increment(ref failed); }
                        RemoveLatestIfSameLocked(result.Job);
                    }
                }
                watch.Stop();
                RecordSample(commitSamples, ref commitIndex, ref commitCount,
                    watch.Elapsed.TotalMilliseconds);
            }
            return count;
        }

        internal double OldestSafetyDelayMilliseconds
        {
            get
            {
                lock (sync)
                {
                    LinkedListNode<Job> node = lanes[0].Jobs.First;
                    if (node == null) return 0.0;
                    return ElapsedMilliseconds(node.Value.EnqueuedTicks);
                }
            }
        }

        internal AERISRuntimeTelemetrySnapshot SnapshotTelemetry()
        {
            lock (sync)
            {
                double[] delay = CopySamples(queueDelaySamples, queueDelayCount);
                double[] work = CopySamples(workerSamples, workerCount);
                double[] commit = CopySamples(commitSamples, commitCount);
                double[] runway = CopySamples(runwaySamples, runwayCount);
                double[] terrain = CopySamples(terrainSamples, terrainCount);
                double[] instrument = CopySamples(instrumentSamples, instrumentCount);
                double[] navigationDisplay = CopySamples(navigationDisplaySamples,
                    navigationDisplayCount);
                Array.Sort(delay); Array.Sort(work); Array.Sort(commit);
                Array.Sort(runway); Array.Sort(terrain); Array.Sort(instrument);
                Array.Sort(navigationDisplay);
                int requiredResultDepth = 0;
                foreach (Result pendingResult in results)
                    if (pendingResult != null && pendingResult.Job != null &&
                        pendingResult.Job.CommitRequired) requiredResultDepth++;
                return new AERISRuntimeTelemetrySnapshot
                {
                    LogicalProcessors = permits.LogicalProcessors,
                    ConfiguredWorkers = workers.Length,
                    ActivePermits = permits.ActivePermits,
                    ActiveJobs = activeJobs,
                    ActiveSafetyJobs = activeByLane[0],
                    ActiveGeneralJobs = activeByLane[1],
                    ActiveTelemetryJobs = activeByLane[2],
                    ActiveArchiveJobs = activeByLane[3],
                    SafetyDepth = lanes[0].Jobs.Count,
                    GeneralDepth = lanes[1].Jobs.Count,
                    TelemetryDepth = lanes[2].Jobs.Count,
                    ArchiveDepth = lanes[3].Jobs.Count,
                    Submitted = Interlocked.Read(ref submitted),
                    Completed = Interlocked.Read(ref completed),
                    Replaced = Interlocked.Read(ref replaced),
                    Dropped = Interlocked.Read(ref dropped),
                    Cancelled = Interlocked.Read(ref cancelled),
                    Stale = Interlocked.Read(ref stale),
                    Failed = Interlocked.Read(ref failed),
                    ResultDepth = results.Count,
                    RequiredResultDepth = requiredResultDepth,
                    RequiredRejected = Interlocked.Read(ref requiredRejected),
                    RequiredDropped = Interlocked.Read(ref requiredDropped),
                    QueueDelayP50Ms = Percentile(delay, 0.50),
                    QueueDelayP95Ms = Percentile(delay, 0.95),
                    QueueDelayMaxMs = delay.Length == 0 ? 0.0 : delay[delay.Length - 1],
                    WorkerP95Ms = Percentile(work, 0.95),
                    CommitP95Ms = Percentile(commit, 0.95),
                    RunwayWorkerP95Ms = Percentile(runway, 0.95),
                    TerrainWorkerP95Ms = Percentile(terrain, 0.95),
                    InstrumentWorkerP95Ms = Percentile(instrument, 0.95),
                    NavigationDisplayWorkerP95Ms = Percentile(navigationDisplay, 0.95),
                    RunwayResultAgeMs = AgeMilliseconds(lastRunwayCommitTicks),
                    TerrainResultAgeMs = AgeMilliseconds(lastTerrainCommitTicks),
                    InstrumentResultAgeMs = AgeMilliseconds(lastInstrumentCommitTicks),
                    NavigationDisplayResultAgeMs = AgeMilliseconds(
                        lastNavigationDisplayCommitTicks),
                    DegradationReason = permits.Reason
                };
            }
        }

        void WorkerLoop(int workerIndexValue)
        {
            while (!stopping)
            {
                Job job = null;
                lock (sync)
                {
                    if (activeJobs < permits.ActivePermits) job = TakeNextLocked();
                    if (job != null)
                    {
                        activeJobs++;
                        activeByLane[(int)job.Lane]++;
                    }
                }
                // AutoResetEvent notifications may coalesce when a burst is queued.
                // Hand one notification to another waiter after claiming work so the
                // pool ramps to the current permit limit instead of serialising a burst.
                if (job != null) wake.Set();
                if (job == null)
                {
                    try { wake.WaitOne(100); }
                    catch { return; }
                    continue;
                }
                double delay = ElapsedMilliseconds(job.EnqueuedTicks);
                RecordSample(queueDelaySamples, ref queueDelayIndex,
                    ref queueDelayCount, delay);
                object value = null;
                Exception error = null;
                Stopwatch watch = Stopwatch.StartNew();
                try
                {
                    if (!IsJobCurrent(job))
                        throw new OperationCanceledException();
                    var context = new AERISRuntimeJobContext(() =>
                        IsJobCurrent(job));
                    value = job.Work(context);
                    if (!context.IsCurrent) throw new OperationCanceledException();
                }
                catch (Exception ex) { error = ex; }
                watch.Stop();
                RecordSample(workerSamples, ref workerIndex, ref workerCount,
                    watch.Elapsed.TotalMilliseconds);
                if (string.Equals(job.Key, "runway-certification",
                    StringComparison.Ordinal))
                    RecordSample(runwaySamples, ref runwayIndex, ref runwayCount,
                        watch.Elapsed.TotalMilliseconds);
                else if (string.Equals(job.Key, "terrain-raster",
                    StringComparison.Ordinal) || job.Key.StartsWith("terrain-projection-",
                    StringComparison.Ordinal))
                    RecordSample(terrainSamples, ref terrainIndex, ref terrainCount,
                        watch.Elapsed.TotalMilliseconds);
                else if (string.Equals(job.Key, "instrument-display-preprocess",
                    StringComparison.Ordinal))
                    RecordSample(instrumentSamples, ref instrumentIndex,
                        ref instrumentCount, watch.Elapsed.TotalMilliseconds);
                else if (string.Equals(job.Key, AERISNavigationDisplayPipeline.JobKey,
                    StringComparison.Ordinal) || string.Equals(job.Key,
                    AERISNavigationTrafficPipeline.JobKey, StringComparison.Ordinal))
                    RecordSample(navigationDisplaySamples, ref navigationDisplayIndex,
                        ref navigationDisplayCount, watch.Elapsed.TotalMilliseconds);
                lock (sync)
                {
                    activeJobs--;
                    activeByLane[(int)job.Lane] = Math.Max(0,
                        activeByLane[(int)job.Lane] - 1);
                    var result = new Result
                    {
                        Job = job,
                        Value = value,
                        Error = error is OperationCanceledException ? null : error,
                        WorkerMs = watch.Elapsed.TotalMilliseconds
                    };
                    LinkedListNode<Result> previousResult;
                    if (resultByKey.TryGetValue(job.IdentityKey, out previousResult))
                    {
                        Result previousValue = previousResult.Value;
                        if (previousValue != null && previousValue.Job != null &&
                            previousValue.Job.Id > job.Id)
                        {
                            // A cancelled older execution completed after the newer result.
                            // Keep the newer commit candidate already in the queue.
                            Interlocked.Increment(ref stale);
                            result = null;
                        }
                        else
                        {
                            results.Remove(previousResult);
                            resultByKey.Remove(job.IdentityKey);
                            Interlocked.Increment(ref replaced);
                        }
                    }
                    if (result != null)
                    {
                        if (results.Count >= ResultCapacity)
                        {
                            LinkedListNode<Result> removedNode = results.First;
                            while (removedNode != null && removedNode.Value != null &&
                                removedNode.Value.Job != null &&
                                removedNode.Value.Job.CommitRequired)
                                removedNode = removedNode.Next;
                            if (removedNode != null)
                            {
                                Result removed = removedNode.Value;
                                results.Remove(removedNode);
                                if (removed != null && removed.Job != null)
                                {
                                    LinkedListNode<Result> indexed;
                                    if (resultByKey.TryGetValue(removed.Job.IdentityKey,
                                        out indexed) && ReferenceEquals(indexed, removedNode))
                                        resultByKey.Remove(removed.Job.IdentityKey);
                                    RemoveLatestIfSameLocked(removed.Job);
                                }
                                Interlocked.Increment(ref dropped);
                            }
                            else if (!job.CommitRequired)
                            {
                                // All queued results are commit-required. Preserve them and
                                // discard only the incoming best-effort result.
                                RemoveLatestIfSameLocked(job);
                                Interlocked.Increment(ref dropped);
                                result = null;
                            }
                            // A commit-required result is allowed to extend past the nominal
                            // capacity. Terrain admission caps bound this path below 256.
                        }
                        if (result != null)
                        {
                            LinkedListNode<Result> added = results.AddLast(result);
                            resultByKey[job.IdentityKey] = added;
                        }
                    }
                    if (error is OperationCanceledException)
                        job.Cancelled = true;
                    Interlocked.Increment(ref completed);
                }
                wake.Set();
            }
        }

        Job TakeNextLocked()
        {
            bool safetyWaiting = lanes[0].Jobs.Count > 0;
            int nonSafetyLimit = Math.Max(1,
                permits.ActivePermits - permits.SafetyReservedPermits);
            for (int attempt = 0; attempt < fairness.Length; attempt++)
            {
                int index = fairness[fairnessIndex++ % fairness.Length];
                if (index == 3 && permits.ArchivePaused) continue;
                if (index != 0 && safetyWaiting && activeJobs >= nonSafetyLimit) continue;
                LaneQueue lane = lanes[index];
                LinkedListNode<Job> node = lane.Jobs.First;
                if (node == null) continue;
                lane.Jobs.RemoveFirst();
                lane.ByKey.Remove(node.Value.Key);
                return node.Value;
            }
            // Weighted fairness never leaves runnable work idle solely because its slot
            // was missed; High is preferred, then each non-paused lane.
            for (int index = 0; index < lanes.Length; index++)
            {
                if (index == 3 && permits.ArchivePaused) continue;
                if (index != 0 && safetyWaiting && activeJobs >= nonSafetyLimit) continue;
                LinkedListNode<Job> node = lanes[index].Jobs.First;
                if (node == null) continue;
                lanes[index].Jobs.RemoveFirst();
                lanes[index].ByKey.Remove(node.Value.Key);
                return node.Value;
            }
            return null;
        }

        bool IsJobCurrent(Job job)
        {
            if (job == null || job.Cancelled) return false;
            if (job.GenerationBound && !generations.Matches(job.Stamp)) return false;
            lock (sync) return IsLatestLocked(job);
        }

        bool IsLatestLocked(Job job)
        {
            if (job == null || job.Cancelled) return false;
            long latest;
            return latestJobIds.TryGetValue(job.IdentityKey, out latest) && latest == job.Id;
        }

        void RemoveLatestIfSameLocked(Job job)
        {
            if (job == null) return;
            long latest;
            if (latestJobIds.TryGetValue(job.IdentityKey, out latest) && latest == job.Id)
                latestJobIds.Remove(job.IdentityKey);
        }

        void RecordCommitFreshnessLocked(Job job)
        {
            if (job == null) return;
            long now = Stopwatch.GetTimestamp();
            if (string.Equals(job.Key, "runway-certification", StringComparison.Ordinal))
                lastRunwayCommitTicks = now;
            else if (string.Equals(job.Key, "terrain-raster", StringComparison.Ordinal))
                lastTerrainCommitTicks = now;
            else if (string.Equals(job.Key, "instrument-display-preprocess",
                StringComparison.Ordinal)) lastInstrumentCommitTicks = now;
            else if (string.Equals(job.Key, AERISNavigationDisplayPipeline.JobKey,
                StringComparison.Ordinal) || string.Equals(job.Key,
                AERISNavigationTrafficPipeline.JobKey, StringComparison.Ordinal))
                lastNavigationDisplayCommitTicks = now;
        }

        static void RecordSample(double[] target, ref int index, ref int count,
            double value)
        {
            if (target == null || double.IsNaN(value) || double.IsInfinity(value) ||
                value < 0.0) return;
            lock (target)
            {
                target[index++ % target.Length] = value;
                count = Math.Min(target.Length, count + 1);
            }
        }

        static double[] CopySamples(double[] source, int count)
        {
            count = Math.Max(0, Math.Min(source.Length, count));
            var value = new double[count];
            lock (source) Array.Copy(source, value, count);
            return value;
        }

        static double Percentile(double[] values, double fraction)
        {
            if (values == null || values.Length == 0) return 0.0;
            int index = (int)Math.Ceiling((values.Length - 1) * fraction);
            return values[Math.Max(0, Math.Min(values.Length - 1, index))];
        }

        static double ElapsedMilliseconds(long ticks)
        {
            long now = Stopwatch.GetTimestamp();
            return now <= ticks || Stopwatch.Frequency <= 0L ? 0.0 :
                (now - ticks) * 1000.0 / Stopwatch.Frequency;
        }

        static double AgeMilliseconds(long ticks)
        {
            return ticks <= 0L ? -1.0 : ElapsedMilliseconds(ticks);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopping = true;
            lock (sync)
            {
                for (int i = 0; i < lanes.Length; i++)
                {
                    foreach (Job job in lanes[i].Jobs) job.Cancelled = true;
                    cancelled += lanes[i].Jobs.Count;
                    lanes[i].Jobs.Clear();
                    lanes[i].ByKey.Clear();
                }
                results.Clear();
                resultByKey.Clear();
                latestJobIds.Clear();
            }
            // Main/KSP thread never Join/Waits. Background workers observe stopping.
            wake.Set();
        }
    }
}
