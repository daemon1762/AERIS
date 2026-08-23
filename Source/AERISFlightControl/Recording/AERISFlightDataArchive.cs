using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
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

        // AERIS31_R029_PROTON_SAFE_ZIP32_DEFLATE_ARCHIVE
        // KSP/Proton does not reliably provide the framework compression helper assembly.
        // The archive worker therefore emits a conservative, standards-compatible
        // classic ZIP32 archive using DEFLATE (method 8), then verifies every byte and
        // CRC32 before the existing raw-folder deletion/retention logic can run.
        sealed class StoredZipWriteEntry
        {
            internal string Name;
            internal byte[] NameBytes;
            internal uint Crc32;
            internal uint CompressedSize;
            internal uint Size;
            internal ushort DosTime;
            internal ushort DosDate;
            internal uint LocalHeaderOffset;
        }

        sealed class StoredZipCentralEntry
        {
            internal string Name;
            internal uint Crc32;
            internal uint CompressedSize;
            internal uint Size;
            internal uint LocalHeaderOffset;
        }

        const uint ZipLocalHeaderSignature = 0x04034B50u;
        const uint ZipCentralHeaderSignature = 0x02014B50u;
        const uint ZipEndSignature = 0x06054B50u;
        const ushort ZipUtf8Flag = 0x0800;
        const ushort ZipDeflateMethod = 8;
        static readonly Encoding ZipUtf8 = new UTF8Encoding(false, true);
        static readonly uint[] Crc32Table = BuildCrc32Table();

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
            string excludedOpenFolder, string additionalRootPath = null)
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
            string immutableAdditionalRoot = additionalRootPath ?? string.Empty;
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
                        var recoverable = new List<string>();
                        CollectRecoveryFolders(immutableRoot, immutableExcluded,
                            recoverable, context);
                        if (!string.IsNullOrEmpty(immutableAdditionalRoot))
                        {
                            string normalizedPrimary = Path.GetFullPath(immutableRoot)
                                .TrimEnd(Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar);
                            string normalizedAdditional = Path.GetFullPath(immutableAdditionalRoot)
                                .TrimEnd(Path.DirectorySeparatorChar,
                                    Path.AltDirectorySeparatorChar);
                            if (!string.Equals(normalizedPrimary, normalizedAdditional,
                                StringComparison.OrdinalIgnoreCase))
                                CollectRecoveryFolders(immutableAdditionalRoot,
                                    immutableExcluded, recoverable, context);
                        }
                        scan.Folders = recoverable.ToArray();
                        Array.Sort(scan.Folders, StringComparer.Ordinal);
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

        static void CollectRecoveryFolders(string rootPath, string excludedFolder,
            List<string> recoverable, AERISRuntimeJobContext context)
        {
            if (recoverable == null || string.IsNullOrEmpty(rootPath) ||
                !Directory.Exists(rootPath)) return;
            string[] discovered = Directory.GetDirectories(rootPath);
            for (int i = 0; i < discovered.Length; i++)
            {
                context.ThrowIfStale();
                string normalized = Path.GetFullPath(discovered[i]).TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.IsNullOrEmpty(excludedFolder) &&
                    string.Equals(normalized, excludedFolder,
                        StringComparison.OrdinalIgnoreCase)) continue;
                if (!recoverable.Contains(discovered[i]))
                    recoverable.Add(discovered[i]);
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
                    long sizeDelta = result.ArchiveBytes - result.SourceBytes;
                    string message = "[FDR][ARCHIVE] ZIP verified: " +
                        result.ArchivePath + "; files=" + result.FileCount +
                        "; source=" + result.SourceBytes + " B; zip=" +
                        result.ArchiveBytes + " B; method=ZIP32/DEFLATE; sizeDelta=" +
                        sizeDelta + " B; sourceDeleted=" + result.SourceDeleted +
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
                WriteDeflatedZip(temporaryPath, files, prefix, context);

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

        static void WriteDeflatedZip(string archivePath, List<string> files,
            string prefix, AERISRuntimeJobContext context)
        {
            if (files == null) throw new ArgumentNullException("files");
            if (files.Count > ushort.MaxValue)
                throw new InvalidDataException("ZIP32 entry limit exceeded");

            var entries = new List<StoredZipWriteEntry>(files.Count);
            byte[] buffer = new byte[128 * 1024];
            using (var output = new FileStream(archivePath, FileMode.CreateNew,
                FileAccess.ReadWrite, FileShare.None, buffer.Length,
                FileOptions.SequentialScan))
            using (var writer = new BinaryWriter(output, ZipUtf8, true))
            {
                for (int i = 0; i < files.Count; i++)
                {
                    context.ThrowIfStale();
                    string full = Path.GetFullPath(files[i]);
                    if (!full.StartsWith(prefix, StringComparison.Ordinal))
                        throw new IOException("file escaped source folder: " + full);
                    string relative = full.Substring(prefix.Length)
                        .Replace(Path.DirectorySeparatorChar, '/')
                        .Replace(Path.AltDirectorySeparatorChar, '/');
                    byte[] nameBytes = ZipUtf8.GetBytes(relative);
                    if (nameBytes.Length == 0 || nameBytes.Length > ushort.MaxValue)
                        throw new InvalidDataException(
                            "ZIP32 entry name length invalid: " + relative);

                    long sourceLength = new FileInfo(full).Length;
                    if (sourceLength < 0L || sourceLength > uint.MaxValue)
                        throw new InvalidDataException(
                            "ZIP32 file size limit exceeded: " + relative);
                    if (output.Position < 0L || output.Position > uint.MaxValue)
                        throw new InvalidDataException(
                            "ZIP32 local-header offset limit exceeded");

                    ushort dosTime, dosDate;
                    GetDosDateTime(File.GetLastWriteTimeUtc(full), out dosTime, out dosDate);
                    long localHeaderOffsetLong = output.Position;

                    writer.Write(ZipLocalHeaderSignature);
                    writer.Write((ushort)20);
                    writer.Write(ZipUtf8Flag);
                    writer.Write(ZipDeflateMethod);
                    writer.Write(dosTime);
                    writer.Write(dosDate);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((uint)0);
                    writer.Write((ushort)nameBytes.Length);
                    writer.Write((ushort)0);
                    writer.Write(nameBytes);
                    writer.Flush();

                    long dataStart = output.Position;
                    uint crc = 0xFFFFFFFFu;
                    long copied = 0L;
                    using (var input = new FileStream(full, FileMode.Open, FileAccess.Read,
                        FileShare.Read, buffer.Length, FileOptions.SequentialScan))
                    using (var deflate = new DeflateStream(output,
                        CompressionLevel.Fastest, true))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            context.ThrowIfStale();
                            crc = UpdateCrc32(crc, buffer, read);
                            deflate.Write(buffer, 0, read);
                            copied += read;
                            if (copied > sourceLength)
                                throw new IOException(
                                    "source grew during ZIP write: " + relative);
                        }
                    }
                    crc ^= 0xFFFFFFFFu;

                    if (copied != sourceLength)
                        throw new IOException(
                            "source changed during ZIP write: " + relative);
                    long afterData = output.Position;
                    long compressedLength = afterData - dataStart;
                    if (compressedLength < 0L || compressedLength > uint.MaxValue)
                        throw new InvalidDataException(
                            "ZIP32 compressed size limit exceeded: " + relative);
                    if (afterData < 0L || afterData > uint.MaxValue)
                        throw new InvalidDataException(
                            "ZIP32 archive offset limit exceeded");

                    output.Seek(localHeaderOffsetLong + 14L, SeekOrigin.Begin);
                    writer.Write(crc);
                    writer.Write((uint)compressedLength);
                    writer.Write((uint)copied);
                    writer.Flush();
                    output.Seek(afterData, SeekOrigin.Begin);

                    entries.Add(new StoredZipWriteEntry
                    {
                        Name = relative,
                        NameBytes = nameBytes,
                        Crc32 = crc,
                        CompressedSize = (uint)compressedLength,
                        Size = (uint)copied,
                        DosTime = dosTime,
                        DosDate = dosDate,
                        LocalHeaderOffset = (uint)localHeaderOffsetLong
                    });
                }

                writer.Flush();
                long centralOffsetLong = output.Position;
                if (centralOffsetLong < 0L || centralOffsetLong > uint.MaxValue)
                    throw new InvalidDataException(
                        "ZIP32 central-directory offset limit exceeded");
                uint centralOffset = (uint)centralOffsetLong;

                for (int i = 0; i < entries.Count; i++)
                {
                    context.ThrowIfStale();
                    StoredZipWriteEntry entry = entries[i];
                    writer.Write(ZipCentralHeaderSignature);
                    writer.Write((ushort)20);
                    writer.Write((ushort)20);
                    writer.Write(ZipUtf8Flag);
                    writer.Write(ZipDeflateMethod);
                    writer.Write(entry.DosTime);
                    writer.Write(entry.DosDate);
                    writer.Write(entry.Crc32);
                    writer.Write(entry.CompressedSize);
                    writer.Write(entry.Size);
                    writer.Write((ushort)entry.NameBytes.Length);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((ushort)0);
                    writer.Write((uint)0);
                    writer.Write(entry.LocalHeaderOffset);
                    writer.Write(entry.NameBytes);
                }

                writer.Flush();
                long centralSizeLong = output.Position - centralOffsetLong;
                if (centralSizeLong < 0L || centralSizeLong > uint.MaxValue)
                    throw new InvalidDataException(
                        "ZIP32 central-directory size limit exceeded");
                writer.Write(ZipEndSignature);
                writer.Write((ushort)0);
                writer.Write((ushort)0);
                writer.Write((ushort)entries.Count);
                writer.Write((ushort)entries.Count);
                writer.Write((uint)centralSizeLong);
                writer.Write(centralOffset);
                writer.Write((ushort)0);
                writer.Flush();
                output.Flush();
            }
        }

        static uint[] BuildCrc32Table()
        {
            uint[] table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                uint value = i;
                for (int bit = 0; bit < 8; bit++)
                    value = (value & 1u) != 0u ?
                        0xEDB88320u ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }

        static uint UpdateCrc32(uint crc, byte[] buffer, int count)
        {
            for (int i = 0; i < count; i++)
                crc = Crc32Table[(int)((crc ^ buffer[i]) & 0xFFu)] ^ (crc >> 8);
            return crc;
        }

        static uint ComputeFileCrc32(string path, byte[] buffer,
            AERISRuntimeJobContext context, out long bytesRead)
        {
            uint crc = 0xFFFFFFFFu;
            bytesRead = 0L;
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, buffer.Length, FileOptions.SequentialScan))
            {
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    context.ThrowIfStale();
                    crc = UpdateCrc32(crc, buffer, read);
                    bytesRead += read;
                }
            }
            return crc ^ 0xFFFFFFFFu;
        }

        static void GetDosDateTime(DateTime timeUtc, out ushort dosTime,
            out ushort dosDate)
        {
            DateTime value = timeUtc.Kind == DateTimeKind.Utc ? timeUtc : timeUtc.ToUniversalTime();
            if (value.Year < 1980)
                value = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            else if (value.Year > 2107)
                value = new DateTime(2107, 12, 31, 23, 59, 58, DateTimeKind.Utc);
            dosTime = (ushort)((value.Hour << 11) | (value.Minute << 5) |
                (value.Second / 2));
            dosDate = (ushort)(((value.Year - 1980) << 9) | (value.Month << 5) |
                value.Day);
        }

        sealed class LimitedReadStream : Stream
        {
            readonly Stream inner;
            long remaining;

            internal LimitedReadStream(Stream inner, long length)
            {
                if (inner == null) throw new ArgumentNullException("inner");
                if (length < 0L) throw new ArgumentOutOfRangeException("length");
                this.inner = inner;
                remaining = length;
            }

            internal long Remaining { get { return remaining; } }
            public override bool CanRead { get { return true; } }
            public override bool CanSeek { get { return false; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } }
            public override long Position
            {
                get { throw new NotSupportedException(); }
                set { throw new NotSupportedException(); }
            }
            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin)
            { throw new NotSupportedException(); }
            public override void SetLength(long value)
            { throw new NotSupportedException(); }
            public override void Write(byte[] buffer, int offset, int count)
            { throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count)
            {
                if (remaining <= 0L) return 0;
                int allowed = (int)Math.Min((long)count, remaining);
                int read = inner.Read(buffer, offset, allowed);
                if (read > 0) remaining -= read;
                return read;
            }

            public override int ReadByte()
            {
                if (remaining <= 0L) return -1;
                int value = inner.ReadByte();
                if (value >= 0) remaining--;
                return value;
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
                    FileShare.Read, 128 * 1024, FileOptions.RandomAccess))
                {
                    ushort entryCount;
                    uint centralSize, centralOffset;
                    if (!TryFindZipEnd(input, out entryCount, out centralSize,
                        out centralOffset, out error)) return false;
                    if (entryCount != expected.Count)
                    {
                        error = "entry count mismatch";
                        return false;
                    }
                    long centralEnd = (long)centralOffset + centralSize;
                    if (centralEnd > input.Length)
                    {
                        error = "central directory exceeds archive length";
                        return false;
                    }

                    var entries = new List<StoredZipCentralEntry>(entryCount);
                    var seen = new HashSet<string>(StringComparer.Ordinal);
                    input.Seek(centralOffset, SeekOrigin.Begin);
                    using (var reader = new BinaryReader(input, ZipUtf8, true))
                    {
                        for (int i = 0; i < entryCount; i++)
                        {
                            context.ThrowIfStale();
                            if (reader.ReadUInt32() != ZipCentralHeaderSignature)
                            {
                                error = "central-directory signature mismatch";
                                return false;
                            }
                            reader.ReadUInt16();
                            reader.ReadUInt16();
                            ushort flags = reader.ReadUInt16();
                            ushort method = reader.ReadUInt16();
                            reader.ReadUInt16();
                            reader.ReadUInt16();
                            uint crc = reader.ReadUInt32();
                            uint compressedSize = reader.ReadUInt32();
                            uint uncompressedSize = reader.ReadUInt32();
                            ushort nameLength = reader.ReadUInt16();
                            ushort extraLength = reader.ReadUInt16();
                            ushort commentLength = reader.ReadUInt16();
                            ushort diskStart = reader.ReadUInt16();
                            reader.ReadUInt16();
                            reader.ReadUInt32();
                            uint localOffset = reader.ReadUInt32();
                            if ((flags & 0x0001) != 0 || (flags & 0x0008) != 0 ||
                                (flags & ZipUtf8Flag) == 0 ||
                                method != ZipDeflateMethod || diskStart != 0)
                            {
                                error = "unsupported ZIP entry encoding/method";
                                return false;
                            }
                            byte[] nameBytes = reader.ReadBytes(nameLength);
                            if (nameBytes.Length != nameLength)
                            {
                                error = "truncated central-directory name";
                                return false;
                            }
                            string name = ZipUtf8.GetString(nameBytes);
                            if (!seen.Add(name))
                            {
                                error = "duplicate entry: " + name;
                                return false;
                            }
                            ExpectedFile expectedFile;
                            if (!expected.TryGetValue(name, out expectedFile) ||
                                expectedFile == null ||
                                expectedFile.Length != uncompressedSize)
                            {
                                error = "unexpected entry/length: " + name;
                                return false;
                            }
                            if (extraLength > 0)
                                input.Seek(extraLength, SeekOrigin.Current);
                            if (commentLength > 0)
                                input.Seek(commentLength, SeekOrigin.Current);
                            entries.Add(new StoredZipCentralEntry
                            {
                                Name = name,
                                Crc32 = crc,
                                CompressedSize = compressedSize,
                                Size = uncompressedSize,
                                LocalHeaderOffset = localOffset
                            });
                        }
                    }
                    if (input.Position != centralEnd || seen.Count != expected.Count)
                    {
                        error = "central-directory size/missing entry mismatch";
                        return false;
                    }

                    byte[] archiveBuffer = new byte[128 * 1024];
                    byte[] sourceBuffer = new byte[128 * 1024];
                    using (var reader = new BinaryReader(input, ZipUtf8, true))
                    {
                        for (int i = 0; i < entries.Count; i++)
                        {
                            context.ThrowIfStale();
                            StoredZipCentralEntry entry = entries[i];
                            ExpectedFile expectedFile = expected[entry.Name];
                            input.Seek(entry.LocalHeaderOffset, SeekOrigin.Begin);
                            if (reader.ReadUInt32() != ZipLocalHeaderSignature)
                            {
                                error = "local-header signature mismatch: " + entry.Name;
                                return false;
                            }
                            reader.ReadUInt16();
                            ushort flags = reader.ReadUInt16();
                            ushort method = reader.ReadUInt16();
                            reader.ReadUInt16();
                            reader.ReadUInt16();
                            uint localCrc = reader.ReadUInt32();
                            uint localCompressed = reader.ReadUInt32();
                            uint localSize = reader.ReadUInt32();
                            ushort nameLength = reader.ReadUInt16();
                            ushort extraLength = reader.ReadUInt16();
                            byte[] localNameBytes = reader.ReadBytes(nameLength);
                            if (localNameBytes.Length != nameLength ||
                                ZipUtf8.GetString(localNameBytes) != entry.Name ||
                                flags != ZipUtf8Flag ||
                                method != ZipDeflateMethod ||
                                localCrc != entry.Crc32 ||
                                localCompressed != entry.CompressedSize ||
                                localSize != entry.Size)
                            {
                                error = "local/central entry mismatch: " + entry.Name;
                                return false;
                            }
                            if (extraLength > 0)
                                input.Seek(extraLength, SeekOrigin.Current);
                            long dataEnd = input.Position + entry.CompressedSize;
                            if (dataEnd > centralOffset)
                            {
                                error = "entry data overlaps central directory: " +
                                    entry.Name;
                                return false;
                            }

                            uint crc = 0xFFFFFFFFu;
                            long remaining = entry.Size;
                            using (var limited = new LimitedReadStream(
                                input, entry.CompressedSize))
                            using (var inflate = new DeflateStream(
                                limited, CompressionMode.Decompress, true))
                            using (var source = new FileStream(expectedFile.Path,
                                FileMode.Open, FileAccess.Read, FileShare.Read,
                                sourceBuffer.Length, FileOptions.SequentialScan))
                            {
                                while (remaining > 0L)
                                {
                                    context.ThrowIfStale();
                                    int wanted = (int)Math.Min(
                                        (long)archiveBuffer.Length, remaining);
                                    int archiveRead = ReadExactCount(
                                        inflate, archiveBuffer, wanted);
                                    int sourceRead = ReadExactCount(
                                        source, sourceBuffer, wanted);
                                    if (archiveRead != wanted ||
                                        sourceRead != wanted)
                                    {
                                        error = "entry/source truncated: " +
                                            entry.Name;
                                        return false;
                                    }
                                    for (int offset = 0; offset < wanted; offset++)
                                    {
                                        if (archiveBuffer[offset] ==
                                            sourceBuffer[offset]) continue;
                                        error = "entry/source content mismatch: " +
                                            entry.Name;
                                        return false;
                                    }
                                    crc = UpdateCrc32(crc, archiveBuffer, wanted);
                                    remaining -= wanted;
                                }
                                if (inflate.ReadByte() != -1)
                                {
                                    error = "decompressed entry exceeds declared size: " +
                                        entry.Name;
                                    return false;
                                }
                                if (limited.Remaining != 0L)
                                {
                                    error = "compressed entry was not fully consumed: " +
                                        entry.Name;
                                    return false;
                                }
                                if (source.ReadByte() != -1)
                                {
                                    error = "source grew during verification: " +
                                        entry.Name;
                                    return false;
                                }
                            }
                            if (input.Position != dataEnd)
                            {
                                error = "compressed entry boundary mismatch: " +
                                    entry.Name;
                                return false;
                            }
                            crc ^= 0xFFFFFFFFu;
                            if (crc != entry.Crc32)
                            {
                                error = "CRC32 mismatch: " + entry.Name;
                                return false;
                            }
                        }
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

        static bool TryFindZipEnd(FileStream input, out ushort entryCount,
            out uint centralSize, out uint centralOffset, out string error)
        {
            entryCount = 0;
            centralSize = 0u;
            centralOffset = 0u;
            error = string.Empty;
            if (input == null || input.Length < 22L)
            {
                error = "archive too short";
                return false;
            }
            int scanLength = (int)Math.Min(65557L, input.Length);
            byte[] tail = new byte[scanLength];
            input.Seek(input.Length - scanLength, SeekOrigin.Begin);
            if (ReadExactCount(input, tail, scanLength) != scanLength)
            {
                error = "could not read ZIP tail";
                return false;
            }
            for (int offset = scanLength - 22; offset >= 0; offset--)
            {
                if (ReadUInt32LittleEndian(tail, offset) != ZipEndSignature) continue;
                ushort commentLength = ReadUInt16LittleEndian(tail, offset + 20);
                if (offset + 22 + commentLength != scanLength) continue;
                ushort disk = ReadUInt16LittleEndian(tail, offset + 4);
                ushort centralDisk = ReadUInt16LittleEndian(tail, offset + 6);
                ushort diskEntries = ReadUInt16LittleEndian(tail, offset + 8);
                ushort totalEntries = ReadUInt16LittleEndian(tail, offset + 10);
                if (disk != 0 || centralDisk != 0 || diskEntries != totalEntries)
                {
                    error = "multi-disk ZIP is unsupported";
                    return false;
                }
                entryCount = totalEntries;
                centralSize = ReadUInt32LittleEndian(tail, offset + 12);
                centralOffset = ReadUInt32LittleEndian(tail, offset + 16);
                return true;
            }
            error = "end-of-central-directory not found";
            return false;
        }

        static int ReadExactCount(Stream stream, byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = stream.Read(buffer, total, count - total);
                if (read <= 0) break;
                total += read;
            }
            return total;
        }

        static ushort ReadUInt16LittleEndian(byte[] data, int offset)
        {
            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        static uint ReadUInt32LittleEndian(byte[] data, int offset)
        {
            return (uint)data[offset] |
                ((uint)data[offset + 1] << 8) |
                ((uint)data[offset + 2] << 16) |
                ((uint)data[offset + 3] << 24);
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
