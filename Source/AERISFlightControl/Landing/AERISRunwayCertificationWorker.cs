using System;
using System.Threading;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Landing
{
    // Latest-only adapter over the shared Performance Runtime. Every job contains only
    // immutable numeric runway data; final certification remains a main-thread commit.
    internal sealed class AERISRunwayCertificationWorker : IDisposable
    {
        const string JobKey = "runway-certification";
        readonly object sync = new object();
        AERISRunwaySurveyResult completed;
        int pending;
        long submitted;
        long completedCount;
        long replacedCount;
        long staleCount;
        bool disposed;

        internal long Submitted { get { return Interlocked.Read(ref submitted); } }
        internal long Completed { get { return Interlocked.Read(ref completedCount); } }
        internal long Replaced { get { return Interlocked.Read(ref replacedCount); } }
        internal long Stale { get { return Interlocked.Read(ref staleCount); } }
        internal int QueueDepth { get { return Volatile.Read(ref pending); } }

        internal bool Submit(AERISRunwaySurveyJob job)
        {
            if (job == null || job.Snapshot == null || disposed) return false;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return false;
            if (Interlocked.Exchange(ref pending, 1) != 0)
                Interlocked.Increment(ref replacedCount);
            Interlocked.Increment(ref submitted);
            AERISRunwaySurveyJob immutableJob = job;
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.GeneralCompute, JobKey, runtime.CaptureStamp(), context =>
                {
                    context.ThrowIfStale();
                    AERISRunwaySurveyResult result =
                        AERISRunwayGeometryWorker.Execute(immutableJob);
                    context.ThrowIfStale();
                    return result;
                }, value =>
                {
                    AERISRunwaySurveyResult result = value as AERISRunwaySurveyResult;
                    Interlocked.Exchange(ref pending, 0);
                    if (result == null)
                    {
                        Interlocked.Increment(ref staleCount);
                        return;
                    }
                    lock (sync)
                    {
                        if (completed == null || result.Sequence >= completed.Sequence)
                        {
                            if (completed != null) Interlocked.Increment(ref staleCount);
                            completed = result;
                        }
                        else Interlocked.Increment(ref staleCount);
                    }
                    Interlocked.Increment(ref completedCount);
                });
            if (!accepted) Interlocked.Exchange(ref pending, 0);
            return accepted;
        }

        internal bool TryTakeCompleted(long currentGeneration,
            out AERISRunwaySurveyResult result)
        {
            result = null;
            lock (sync)
            {
                if (completed == null) return false;
                if (completed.Generation != currentGeneration)
                {
                    completed = null;
                    Interlocked.Increment(ref staleCount);
                    return false;
                }
                result = completed;
                completed = null;
                return true;
            }
        }

        internal void Invalidate(long currentGeneration)
        {
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute, JobKey);
            if (Interlocked.Exchange(ref pending, 0) != 0)
                Interlocked.Increment(ref staleCount);
            lock (sync)
            {
                if (completed != null && completed.Generation != currentGeneration)
                {
                    completed = null;
                    Interlocked.Increment(ref staleCount);
                }
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute, JobKey);
            Interlocked.Exchange(ref pending, 0);
            lock (sync) completed = null;
        }
    }
}
