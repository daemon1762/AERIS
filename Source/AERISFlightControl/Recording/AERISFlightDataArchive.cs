using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Recording
{
    // Cross-platform, end-of-flight archive worker.  It never calls Unity/KSP APIs
    // from the worker thread: only immutable file-system paths cross the boundary.
    internal static class AERISFlightDataArchive
    {
        sealed class ArchiveResult
        {
            internal string Folder;
            internal string ArchivePath;
            internal bool Success;
            internal bool SourceDeleted;
            internal long SourceBytes;
            internal long ArchiveBytes;
            internal int FileCount;
            internal string Detail;
            internal double DurationMilliseconds;
            internal int RetentionLimit;
            internal int PrunedArchives;
            internal string RetentionDetail;
        }

        sealed class RecoveryScanResult
        {
            internal string Root;
            internal string[] Folders;
            internal string Error;
        }

        sealed class ExpectedFile
        {
            internal string Path;
            internal long Length;
        }

        const int PendingCapacity = 64;
        const int ResultCapacity = 256;
        const string VerifiedMarkerSuffix = ".verified";
        static readonly object Sync = new object();
        static readonly object ArchiveIoSync = new object();
        static readonly Queue<string> Pending = new Queue<string>();
        static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal);
        static readonly Queue<ArchiveResult> Results = new Queue<ArchiveResult>();
        static bool archiveRunning;
        static bool recoveryScanQueued;
        static string activeFolder;
        static AERISPerformanceRuntime activeRuntime;
        static long deferredCount;
        static long completedCount;
        static long failureCount;
        static double lastDurationMilliseconds;
        static double maximumDurationMilliseconds;
        static int retentionLimit = 10;

        internal static int PendingCount
        {
            get { lock (Sync) return Pending.Count + (archiveRunning ? 1 : 0); }
        }
        internal static long DeferredCount { get { return Interlocked.Read(ref deferredCount); } }
        internal static long CompletedCount { get { return Interlocked.Read(ref completedCount); } }
        internal static long FailureCount { get { return Interlocked.Read(ref failureCount); } }
        internal static double LastDurationMilliseconds
        {
            get { lock (Sync) return lastDurationMilliseconds; }
        }
        internal static double MaximumDurationMilliseconds
        {
            get { lock (Sync) return maximumDurationMilliseconds; }
        }

        internal static void ConfigureRetention(int limit)
        {
            Interlocked.Exchange(ref retentionLimit, Math.Max(1, Math.Min(30, limit)));
        }

        internal static int RetentionLimit
        {
            get { return Math.Max(1, Math.Min(30, Interlocked.CompareExchange(ref retentionLimit, 0, 0))); }
        }

        // Runtime instances are scene-scoped. If a scene closes while compression is
        // active, release the coordinator state immediately; the cooperative worker
        // will observe scheduler invalidation and retain the raw folder.
        internal static void NotifyRuntimeAvailable(AERISPerformanceRuntime runtime)
        {
            if (runtime == null) return;
            lock (Sync)
            {
                if (archiveRunning && activeRuntime != null &&
                    !ReferenceEquals(activeRuntime, runtime))
                    AbandonActiveLocked("runtime replaced; raw data retained");
            }
            ScheduleNext();
        }

        internal static void NotifyRuntimeStopping(AERISPerformanceRuntime runtime)
        {
            if (runtime == null) return;
            lock (Sync)
            {
                if (archiveRunning && ReferenceEquals(activeRuntime, runtime))
                    AbandonActiveLocked("runtime stopped; raw data retained for recovery");
                recoveryScanQueued = false;
            }
        }

        internal static void QueueRecoveryArchives(string rootPath,
            string excludedOpenFolder)
        {
            if (string.IsNullOrEmpty(rootPath)) return;
            lock (Sync)
            {
                if (recoveryScanQueued) return;
                recoveryScanQueued = true;
            }

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null)
            {
                lock (Sync) recoveryScanQueued = false;
                Interlocked.Increment(ref deferredCount);
                return;
            }
            string immutableRoot = rootPath;
            string immutableExcluded = string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(excludedOpenFolder))
                    immutableExcluded = Path.GetFullPath(excludedOpenFolder).TrimEnd(
                        Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch { immutableExcluded = string.Empty; }
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.ArchiveCompression, "flightdata-recovery-scan",
                runtime.CaptureStamp(), context =>
                {
                    var scan = new RecoveryScanResult { Root = immutableRoot };
                    try
                    {
                        if (!Directory.Exists(immutableRoot))
                            scan.Folders = new string[0];
                        else
                        {
                            string[] discovered = Directory.GetDirectories(immutableRoot);
                            var recoverable = new List<string>(discovered.Length);
                            for (int i = 0; i < discovered.Length; i++)
                            {
                                string normalized = Path.GetFullPath(discovered[i]).TrimEnd(
                                    Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar);
                                if (!string.IsNullOrEmpty(immutableExcluded) &&
                                    string.Equals(normalized, immutableExcluded,
                                        StringComparison.OrdinalIgnoreCase)) continue;
                                recoverable.Add(discovered[i]);
                            }
                            scan.Folders = recoverable.ToArray();
                            Array.Sort(scan.Folders, StringComparer.Ordinal);
                        }
                    }
                    catch (Exception ex)
                    {
                        scan.Error = ex.GetType().Name + ": " + ex.Message;
                        scan.Folders = new string[0];
                    }
                    return scan;
                }, value =>
                {
                    RecoveryScanResult scan = value as RecoveryScanResult;
                    lock (Sync) recoveryScanQueued = false;
                    if (scan == null || !string.IsNullOrEmpty(scan.Error))
                    {
                        EnqueueResult(new ArchiveResult
                        {
                            Folder = immutableRoot,
                            Success = false,
                            Detail = "recovery scan failed: " +
                                (scan == null ? "no result" : scan.Error)
                        });
                        return;
                    }
                    for (int i = 0; i < scan.Folders.Length; i++)
                        QueueArchive(scan.Folders[i]);
                }, false);
            if (!accepted)
            {
                lock (Sync) recoveryScanQueued = false;
                Interlocked.Increment(ref deferredCount);
            }
        }

        internal static void QueueArchive(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            string normalized;
            try { normalized = Path.GetFullPath(folder); }
            catch { return; }

            int pendingDepth;
            lock (Sync)
            {
                if (!Known.Add(normalized)) return;
                if (Pending.Count >= PendingCapacity)
                {
                    Known.Remove(normalized);
                    Interlocked.Increment(ref deferredCount);
                    EnqueueResultLocked(new ArchiveResult
                    {
                        Folder = normalized,
                        Success = false,
                        Detail = "archive queue full; raw data retained"
                    });
                    return;
                }
                Pending.Enqueue(normalized);
                pendingDepth = Pending.Count;
            }
            AERISLogger.Info("[FDR][ARCHIVE] queued; pending=" + pendingDepth +
                "; folder=" + normalized);
            ScheduleNext();
        }

        internal static void DrainResults()
        {
            while (true)
            {
                ArchiveResult result;
                lock (Sync)
                {
                    if (Results.Count == 0) return;
                    result = Results.Dequeue();
                }

                if (result.Success)
                {
                    double ratio = result.SourceBytes > 0
                        ? 100.0 * (1.0 - (double)result.ArchiveBytes / result.SourceBytes)
                        : 0.0;
                    string message = "[FDR][ARCHIVE] ZIP verified: " +
                        result.ArchivePath + "; files=" + result.FileCount +
                        "; source=" + result.SourceBytes + " B; zip=" +
                        result.ArchiveBytes + " B; reduction=" +
                        ratio.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                        "%; sourceDeleted=" + result.SourceDeleted +
                        "; retention=" + result.RetentionLimit +
                        "; pruned=" + result.PrunedArchives + ".";
                    if (result.SourceDeleted) AERISLogger.Info(message);
                    else AERISLogger.Warn(message + " Source folder was retained: " + result.Detail);
                    if (!string.IsNullOrEmpty(result.RetentionDetail))
                        AERISLogger.Info("[FDR][RETENTION] " + result.RetentionDetail);
                }
                else
                {
                    AERISLogger.Warn("[FDR][ARCHIVE] ZIP creation failed; raw flight data retained. folder=" +
                        result.Folder + "; detail=" + result.Detail);
                }
            }
        }

        static void ScheduleNext()
        {
            string folder;
            lock (Sync)
            {
                if (archiveRunning || Pending.Count == 0) return;
                folder = Pending.Dequeue();
                archiveRunning = true;
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null)
            {
                lock (Sync)
                {
                    archiveRunning = false;
                    Known.Remove(folder);
                    Interlocked.Increment(ref deferredCount);
                    EnqueueResultLocked(new ArchiveResult
                    {
                        Folder = folder,
                        Success = false,
                        Detail = "performance runtime unavailable; raw data retained"
                    });
                }
                ScheduleNext();
                return;
            }
            lock (Sync)
            {
                activeFolder = folder;
                activeRuntime = runtime;
            }
            int immutableRetentionLimit = RetentionLimit;
            bool accepted = runtime.Scheduler.SubmitLatest(
                AERISRuntimeLane.ArchiveCompression, "flightdata-archive",
                runtime.CaptureStamp(), context =>
                {
                    Stopwatch watch = Stopwatch.StartNew();
                    ArchiveResult result;
                    try
                    {
                        context.ThrowIfStale();
                        lock (ArchiveIoSync)
                        {
                            context.ThrowIfStale();
                            result = ArchiveFolder(folder, context, immutableRetentionLimit);
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        result = new ArchiveResult
                        {
                            Folder = folder,
                            Success = false,
                            Detail = ex.GetType().Name + ": " + ex.Message
                        };
                    }
                    watch.Stop();
                    result.DurationMilliseconds = watch.Elapsed.TotalMilliseconds;
                    return result;
                }, value =>
                {
                    ArchiveResult result = value as ArchiveResult;
                    if (result == null)
                        result = new ArchiveResult
                        {
                            Folder = folder,
                            Success = false,
                            Detail = "archive worker returned no result"
                        };
                    lock (Sync)
                    {
                        archiveRunning = false;
                        activeFolder = null;
                        activeRuntime = null;
                        Known.Remove(folder);
                        lastDurationMilliseconds = result.DurationMilliseconds;
                        maximumDurationMilliseconds = Math.Max(maximumDurationMilliseconds,
                            result.DurationMilliseconds);
                        Interlocked.Increment(ref completedCount);
                        if (!result.Success) Interlocked.Increment(ref failureCount);
                        EnqueueResultLocked(result);
                    }
                    ScheduleNext();
                }, false);
            if (accepted)
                AERISLogger.Info("[FDR][ARCHIVE] scheduler accepted; folder=" + folder);
            if (!accepted)
            {
                lock (Sync)
                {
                    archiveRunning = false;
                    activeFolder = null;
                    activeRuntime = null;
                    Known.Remove(folder);
                    Interlocked.Increment(ref deferredCount);
                    EnqueueResultLocked(new ArchiveResult
                    {
                        Folder = folder,
                        Success = false,
                        Detail = "archive scheduler rejected work; raw data retained"
                    });
                }
                ScheduleNext();
            }
        }

        static ArchiveResult ArchiveFolder(string folder,
            AERISRuntimeJobContext context, int archiveRetentionLimit)
        {
            context.ThrowIfStale();
            var result = new ArchiveResult { Folder = folder };
            if (!Directory.Exists(folder))
            {
                result.Detail = "source folder no longer exists";
                return result;
            }

            List<string> files = new List<string>();
            CollectFiles(folder, files);
            files.Sort(StringComparer.Ordinal);
            if (files.Count == 0)
            {
                result.Detail = "source folder contains no regular files";
                return result;
            }

            string root = Path.GetFullPath(folder).TrimEnd(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = root + Path.DirectorySeparatorChar;
            string finalPath = root + ".zip";
            string temporaryPath = finalPath + ".tmp";
            var expected = new Dictionary<string, ExpectedFile>(StringComparer.Ordinal);
            long sourceBytes = 0;

            for (int i = 0; i < files.Count; i++)
            {
                context.ThrowIfStale();
                string full = Path.GetFullPath(files[i]);
                if (!full.StartsWith(prefix, StringComparison.Ordinal))
                    throw new IOException("file escaped source folder: " + full);
                string relative = full.Substring(prefix.Length)
                    .Replace(Path.DirectorySeparatorChar, '/')
                    .Replace(Path.AltDirectorySeparatorChar, '/');
                long length = new FileInfo(full).Length;
                expected.Add(relative, new ExpectedFile { Path = full, Length = length });
                sourceBytes += length;
            }

            result.SourceBytes = sourceBytes;
            result.FileCount = expected.Count;
            result.ArchivePath = finalPath;

            string validationError;
            if (File.Exists(finalPath) && VerifyArchive(finalPath, expected, context,
                out validationError))
            {
                context.ThrowIfStale();
                result.Success = true;
                result.ArchiveBytes = new FileInfo(finalPath).Length;
                result.SourceDeleted = TryDeleteSource(folder, out result.Detail);
                ApplyVerifiedRetention(finalPath, archiveRetentionLimit, context, result);
                return result;
            }

            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch (Exception ex)
                {
                    result.Detail = "cannot remove stale temporary archive: " + ex.Message;
                    return result;
                }
            }
            if (File.Exists(finalPath))
            {
                try { File.Delete(finalPath); }
                catch (Exception ex)
                {
                    result.Detail = "existing invalid archive cannot be replaced: " + ex.Message;
                    return result;
                }
            }

            try
            {
                using (var output = new FileStream(temporaryPath, FileMode.CreateNew,
                    FileAccess.ReadWrite, FileShare.None, 1024 * 128,
                    FileOptions.SequentialScan))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    byte[] buffer = new byte[1024 * 128];
                    for (int i = 0; i < files.Count; i++)
                    {
                        context.ThrowIfStale();
                        string full = Path.GetFullPath(files[i]);
                        string relative = full.Substring(prefix.Length)
                            .Replace(Path.DirectorySeparatorChar, '/')
                            .Replace(Path.AltDirectorySeparatorChar, '/');
                        ZipArchiveEntry entry = archive.CreateEntry(relative,
                            CompressionLevel.Optimal);
                        DateTime modified = File.GetLastWriteTimeUtc(full);
                        if (modified.Year < 1980) modified = new DateTime(1980, 1, 1, 0, 0, 0,
                            DateTimeKind.Utc);
                        if (modified.Year > 2107) modified = new DateTime(2107, 12, 31, 23, 59, 58,
                            DateTimeKind.Utc);
                        entry.LastWriteTime = new DateTimeOffset(modified);
                        using (Stream target = entry.Open())
                        using (var input = new FileStream(full, FileMode.Open, FileAccess.Read,
                            FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                        {
                            int read;
                            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                context.ThrowIfStale();
                                target.Write(buffer, 0, read);
                            }
                        }
                    }
                }

                context.ThrowIfStale();
                if (!VerifyArchive(temporaryPath, expected, context, out validationError))
                    throw new InvalidDataException("temporary ZIP validation failed: " + validationError);

                File.Move(temporaryPath, finalPath);
                result.ArchiveBytes = new FileInfo(finalPath).Length;
                result.Success = true;
                context.ThrowIfStale();
                result.SourceDeleted = TryDeleteSource(folder, out result.Detail);
                ApplyVerifiedRetention(finalPath, archiveRetentionLimit, context, result);
                return result;
            }
            catch
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
                throw;
            }
        }

        static void ApplyVerifiedRetention(string finalPath, int archiveRetentionLimit,
            AERISRuntimeJobContext context, ArchiveResult result)
        {
            if (result == null || string.IsNullOrEmpty(finalPath)) return;
            result.RetentionLimit = Math.Max(1, Math.Min(30, archiveRetentionLimit));
            string markerError;
            if (!TryWriteVerifiedMarker(finalPath, out markerError))
            {
                result.RetentionDetail = "verified ZIP retained but verification marker could not be written; pruning skipped: " + markerError;
                return;
            }
            context.ThrowIfStale();
            string pruneDetail;
            result.PrunedArchives = PruneVerifiedArchives(
                Path.GetDirectoryName(finalPath), result.RetentionLimit, finalPath,
                context, out pruneDetail);
            result.RetentionDetail = pruneDetail;
        }

        static bool TryWriteVerifiedMarker(string archivePath, out string error)
        {
            error = string.Empty;
            try
            {
                string markerPath = archivePath + VerifiedMarkerSuffix;
                string temporaryMarker = markerPath + ".tmp";
                try { if (File.Exists(temporaryMarker)) File.Delete(temporaryMarker); }
                catch { }
                using (var writer = new StreamWriter(new FileStream(temporaryMarker,
                    FileMode.Create, FileAccess.Write, FileShare.None)))
                {
                    writer.WriteLine("AERIS_VERIFIED_FLIGHT_ARCHIVE_V1");
                    writer.WriteLine(DateTime.UtcNow.ToString("o",
                        System.Globalization.CultureInfo.InvariantCulture));
                }
                if (File.Exists(markerPath)) File.Delete(markerPath);
                File.Move(temporaryMarker, markerPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        sealed class VerifiedArchiveCandidate
        {
            internal string Path;
            internal DateTime LastWriteUtc;
        }

        static int PruneVerifiedArchives(string rootPath, int limit,
            string currentArchivePath, AERISRuntimeJobContext context,
            out string detail)
        {
            detail = string.Empty;
            limit = Math.Max(1, Math.Min(30, limit));
            if (string.IsNullOrEmpty(rootPath) || !Directory.Exists(rootPath))
            {
                detail = "retention root unavailable; no pruning performed";
                return 0;
            }
            string current = string.Empty;
            try { current = Path.GetFullPath(currentArchivePath); }
            catch { current = currentArchivePath ?? string.Empty; }
            var verified = new List<VerifiedArchiveCandidate>();
            string[] archives = Directory.GetFiles(rootPath, "*.zip", SearchOption.TopDirectoryOnly);
            for (int i = 0; i < archives.Length; i++)
            {
                context.ThrowIfStale();
                string path = archives[i];
                if (string.IsNullOrEmpty(path) || path.EndsWith(".zip.tmp",
                    StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(path + VerifiedMarkerSuffix)) continue;
                DateTime lastWrite;
                try { lastWrite = File.GetLastWriteTimeUtc(path); }
                catch { continue; }
                verified.Add(new VerifiedArchiveCandidate { Path = path, LastWriteUtc = lastWrite });
            }
            verified.Sort((left, right) =>
            {
                int time = left.LastWriteUtc.CompareTo(right.LastWriteUtc);
                if (time != 0) return time;
                return string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase);
            });
            int toRemove = Math.Max(0, verified.Count - limit);
            int removed = 0;
            for (int i = 0; i < verified.Count && removed < toRemove; i++)
            {
                context.ThrowIfStale();
                string candidate = verified[i].Path;
                string normalized;
                try { normalized = Path.GetFullPath(candidate); }
                catch { normalized = candidate; }
                if (string.Equals(normalized, current, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    File.Delete(candidate);
                    try
                    {
                        string marker = candidate + VerifiedMarkerSuffix;
                        if (File.Exists(marker)) File.Delete(marker);
                    }
                    catch { }
                    removed++;
                }
                catch (Exception ex)
                {
                    detail = "retention delete stopped after " + removed + " archive(s): " +
                        ex.GetType().Name + ": " + ex.Message;
                    return removed;
                }
            }
            detail = "verified ZIP retention=" + limit + "; pruned=" + removed +
                "; unverified ZIP/raw/tmp entries untouched";
            return removed;
        }

        static void CollectFiles(string directory, List<string> files)
        {
            string[] localFiles = Directory.GetFiles(directory);
            for (int i = 0; i < localFiles.Length; i++)
            {
                FileAttributes attributes = File.GetAttributes(localFiles[i]);
                if ((attributes & FileAttributes.ReparsePoint) == 0) files.Add(localFiles[i]);
            }

            string[] directories = Directory.GetDirectories(directory);
            for (int i = 0; i < directories.Length; i++)
            {
                FileAttributes attributes = File.GetAttributes(directories[i]);
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                CollectFiles(directories[i], files);
            }
        }

        static bool VerifyArchive(string archivePath,
            Dictionary<string, ExpectedFile> expected,
            AERISRuntimeJobContext context,
            out string error)
        {
            error = string.Empty;
            try
            {
                using (var input = new FileStream(archivePath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 1024 * 128, FileOptions.SequentialScan))
                using (var archive = new ZipArchive(input, ZipArchiveMode.Read, false))
                {
                    if (archive.Entries.Count != expected.Count)
                    {
                        error = "entry count mismatch";
                        return false;
                    }
                    byte[] archiveBuffer = new byte[1024 * 128];
                    byte[] sourceBuffer = new byte[1024 * 128];
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        context.ThrowIfStale();
                        ZipArchiveEntry entry = archive.Entries[i];
                        ExpectedFile expectedFile;
                        if (!expected.TryGetValue(entry.FullName, out expectedFile) ||
                            expectedFile == null)
                        {
                            error = "unexpected entry: " + entry.FullName;
                            return false;
                        }
                        if (!seen.Add(entry.FullName) || entry.Length != expectedFile.Length)
                        {
                            error = "entry length/duplicate mismatch: " + entry.FullName;
                            return false;
                        }
                        long readTotal = 0L;
                        using (Stream stream = entry.Open())
                        using (var source = new FileStream(expectedFile.Path, FileMode.Open,
                            FileAccess.Read, FileShare.Read, sourceBuffer.Length,
                            FileOptions.SequentialScan))
                        {
                            while (true)
                            {
                                context.ThrowIfStale();
                                int archiveRead = ReadBlock(stream, archiveBuffer);
                                int sourceRead = ReadBlock(source, sourceBuffer);
                                if (archiveRead != sourceRead)
                                {
                                    error = "entry/source read length mismatch: " +
                                        entry.FullName;
                                    return false;
                                }
                                if (archiveRead == 0) break;
                                for (int offset = 0; offset < archiveRead; offset++)
                                {
                                    if (archiveBuffer[offset] == sourceBuffer[offset]) continue;
                                    error = "entry/source content mismatch: " + entry.FullName;
                                    return false;
                                }
                                readTotal += archiveRead;
                            }
                        }
                        if (readTotal != expectedFile.Length)
                        {
                            error = "entry readback mismatch: " + entry.FullName;
                            return false;
                        }
                    }
                    if (seen.Count != expected.Count)
                    {
                        error = "missing entries";
                        return false;
                    }
                }
                return true;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                error = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        static int ReadBlock(Stream stream, byte[] buffer)
        {
            int total = 0;
            while (total < buffer.Length)
            {
                int read = stream.Read(buffer, total, buffer.Length - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        static bool TryDeleteSource(string folder, out string detail)
        {
            try
            {
                Directory.Delete(folder, true);
                detail = "source removed after verified archive commit";
                return true;
            }
            catch (Exception ex)
            {
                detail = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        static void EnqueueResult(ArchiveResult result)
        {
            lock (Sync) EnqueueResultLocked(result);
        }

        static void EnqueueResultLocked(ArchiveResult result)
        {
            if (result == null) return;
            if (Results.Count >= ResultCapacity) Results.Dequeue();
            Results.Enqueue(result);
        }

        static void AbandonActiveLocked(string detail)
        {
            string folder = activeFolder;
            archiveRunning = false;
            activeFolder = null;
            activeRuntime = null;
            if (!string.IsNullOrEmpty(folder)) Known.Remove(folder);
            Interlocked.Increment(ref deferredCount);
            EnqueueResultLocked(new ArchiveResult
            {
                Folder = folder ?? string.Empty,
                Success = false,
                Detail = detail ?? "archive abandoned; raw data retained"
            });
        }
    }
}
