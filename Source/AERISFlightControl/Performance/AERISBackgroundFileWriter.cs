using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace AERISFlightControl.Performance
{
    internal enum AERISFileRecordPriority
    {
        CriticalEvent = 0,
        Continuous = 1,
        Verbose = 2
    }

    internal enum AERISCsvFieldKind
    {
        Raw,
        Fixed4,
        Integer,
        Boolean,
        Quoted,
        UtcTimestamp
    }

    // Captured CSV values are immutable and contain no Unity/KSP references. Numeric and
    // escaping work is deliberately deferred to the dedicated writer thread.
    internal struct AERISCsvField
    {
        readonly AERISCsvFieldKind kind;
        readonly double number;
        readonly long integer;
        readonly string text;

        AERISCsvField(AERISCsvFieldKind kindValue, double numberValue,
            long integerValue, string textValue)
        {
            kind = kindValue;
            number = numberValue;
            integer = integerValue;
            text = textValue;
        }

        internal static AERISCsvField Raw(string value)
        {
            return new AERISCsvField(AERISCsvFieldKind.Raw, 0.0, 0L,
                value ?? string.Empty);
        }

        internal static AERISCsvField Fixed(double value)
        {
            return new AERISCsvField(AERISCsvFieldKind.Fixed4, value, 0L, null);
        }

        internal static AERISCsvField Flag(bool value)
        {
            return new AERISCsvField(AERISCsvFieldKind.Boolean,
                value ? 1.0 : 0.0, 0L, null);
        }

        internal static AERISCsvField Quoted(string value)
        {
            return new AERISCsvField(AERISCsvFieldKind.Quoted, 0.0, 0L,
                value ?? string.Empty);
        }

        internal static AERISCsvField Integer(long value)
        {
            return new AERISCsvField(AERISCsvFieldKind.Integer, 0.0, value, null);
        }

        internal static AERISCsvField Utc(DateTime value)
        {
            DateTime utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return new AERISCsvField(AERISCsvFieldKind.UtcTimestamp, 0.0,
                utc.Ticks, null);
        }

        internal void AppendEncoded(StringBuilder target)
        {
            if (target == null) return;
            switch (kind)
            {
                case AERISCsvFieldKind.Fixed4:
                    target.Append(number.ToString("F4", CultureInfo.InvariantCulture));
                    break;
                case AERISCsvFieldKind.Boolean:
                    target.Append(number > 0.5 ? '1' : '0');
                    break;
                case AERISCsvFieldKind.Integer:
                    target.Append(integer.ToString(CultureInfo.InvariantCulture));
                    break;
                case AERISCsvFieldKind.Quoted:
                    target.Append('"');
                    string value = text ?? string.Empty;
                    for (int i = 0; i < value.Length; i++)
                    {
                        char character = value[i];
                        if (character == '"') target.Append('"');
                        target.Append(character);
                    }
                    target.Append('"');
                    break;
                case AERISCsvFieldKind.UtcTimestamp:
                    target.Append('"');
                    target.Append(new DateTime(integer, DateTimeKind.Utc).ToString("o",
                        CultureInfo.InvariantCulture));
                    target.Append('"');
                    break;
                default:
                    target.Append(text ?? string.Empty);
                    break;
            }
        }

        public override string ToString()
        {
            var builder = new StringBuilder(32);
            AppendEncoded(builder);
            return builder.ToString();
        }

        public static implicit operator AERISCsvField(string value) { return Raw(value); }
        public static implicit operator AERISCsvField(int value)
        {
            return Integer(value);
        }
        public static implicit operator AERISCsvField(long value)
        {
            return Integer(value);
        }
    }

    internal sealed class AERISFileWriterTelemetrySnapshot
    {
        internal int QueueDepth;
        internal int Capacity;
        internal long Enqueued;
        internal long Written;
        internal long DroppedCritical;
        internal long DroppedContinuous;
        internal long DroppedVerbose;
        internal long FirstDroppedSequence;
        internal long LastDroppedSequence;
        internal long Failures;
        internal long OpenChannels;
        internal double BatchP95Milliseconds;
        internal double EncodeP95Milliseconds;
        internal double DiskP95Milliseconds;
        internal double FlushP95Milliseconds;
        internal string LastFailure = string.Empty;
    }

    // A StreamWriter-shaped, non-blocking producer facade. Dispose queues an ordered close;
    // it never waits for disk or for the writer thread.
    // Public type visibility is required because the retained public AA base class has
    // protected telemetry fields. Construction and all producer methods remain internal,
    // so this does not expose a new writable external API.
    public sealed class AERISAsyncFileChannel : IDisposable
    {
        readonly long channelId;
        readonly AERISFileRecordPriority priority;
        int disposed;

        internal AERISAsyncFileChannel(string path, bool append,
            AERISFileRecordPriority recordPriority)
        {
            priority = recordPriority;
            channelId = AERISBackgroundFileWriter.Open(path, append, recordPriority);
        }

        internal bool Available { get { return channelId > 0L && disposed == 0; } }

        internal bool Write(string value)
        {
            return Available && AERISBackgroundFileWriter.WriteText(channelId,
                value ?? string.Empty, false, priority);
        }

        internal bool WriteLine()
        {
            return WriteLine(string.Empty);
        }

        internal bool WriteLine(string value)
        {
            return Available && AERISBackgroundFileWriter.WriteText(channelId,
                value ?? string.Empty, true, priority);
        }

        internal bool WriteCsv(AERISCsvField[] fields)
        {
            if (!Available || fields == null) return false;
            // Ownership transfers to the ordered writer. Every in-tree producer creates
            // a fresh capture array and never mutates it after this call; avoiding a
            // defensive second array materially reduces recorder allocation pressure.
            return AERISBackgroundFileWriter.WriteCsv(channelId, fields, priority);
        }

        internal bool WriteHeader(string[] fields)
        {
            if (fields == null) return false;
            var captured = new AERISCsvField[fields.Length];
            for (int i = 0; i < fields.Length; i++)
                captured[i] = AERISCsvField.Raw(fields[i] ?? string.Empty);
            return WriteCsv(captured);
        }

        internal bool WriteLogEvent(DateTime utc, string prefix, string level, string message)
        {
            return Available && AERISBackgroundFileWriter.WriteLog(channelId, utc,
                prefix, level, message, priority);
        }

        internal bool WriteGeneralNumber(double value, bool trailingComma)
        {
            return Available && AERISBackgroundFileWriter.WriteNumber(channelId,
                value, trailingComma, priority);
        }

        internal void Flush()
        {
            if (Available) AERISBackgroundFileWriter.Flush(channelId);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0 || channelId <= 0L) return;
            AERISBackgroundFileWriter.Close(channelId);
        }
    }

    internal static class AERISBackgroundFileWriter
    {
        enum CommandKind
        {
            Open,
            Text,
            Csv,
            Number,
            Log,
            Flush,
            Close,
            Seal,
            Rotate,
            Stop
        }

        sealed class Command
        {
            internal long Sequence;
            internal long ChannelId;
            internal CommandKind Kind;
            internal AERISFileRecordPriority Priority;
            internal string Path;
            internal string SecondaryPath;
            internal string Text;
            internal string Prefix;
            internal string Level;
            internal DateTime Utc;
            internal bool Append;
            internal bool NewLine;
            internal bool TrailingComma;
            internal double Number;
            internal AERISCsvField[] Fields;
            internal Action Callback;
            internal bool IsControl
            {
                get
                {
                    return Kind == CommandKind.Open || Kind == CommandKind.Flush ||
                        Kind == CommandKind.Close || Kind == CommandKind.Seal ||
                        Kind == CommandKind.Rotate || Kind == CommandKind.Stop;
                }
            }
        }

        const int QueueCapacity = 8192;
        const int ReservedControlSlots = 64;
        const int DataCapacity = QueueCapacity - ReservedControlSlots;
        const int MaximumBatch = 256;
        static readonly object Sync = new object();
        static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        static readonly LinkedList<Command> Queue = new LinkedList<Command>();
        static readonly Dictionary<long, StreamWriter> Writers =
            new Dictionary<long, StreamWriter>();
        static readonly Dictionary<long, string> ChannelPaths =
            new Dictionary<long, string>();
        static readonly HashSet<long> FailedChannels = new HashSet<long>();
        static readonly HashSet<string> FailedFolders =
            new HashSet<string>(StringComparer.Ordinal);
        static readonly double[] BatchSamples = new double[128];
        static readonly double[] EncodeSamples = new double[256];
        static readonly double[] DiskSamples = new double[256];
        static readonly double[] FlushSamples = new double[128];
        static Thread thread;
        static bool accepting = true;
        static bool stopped;
        static bool stopWhenDrained;
        static int dataDepth;
        static int batchIndex, batchCount;
        static int encodeIndex, encodeCount;
        static int diskIndex, diskCount;
        static int flushIndex, flushCount;
        static long nextChannelId;
        static long nextSequence;
        static long enqueued;
        static long written;
        static long droppedCritical;
        static long droppedContinuous;
        static long droppedVerbose;
        static long firstDroppedSequence;
        static long lastDroppedSequence;
        static long failures;
        static string lastFailure = string.Empty;

        internal static long Open(string path, bool append,
            AERISFileRecordPriority priority)
        {
            if (string.IsNullOrEmpty(path)) return 0L;
            EnsureStarted();
            long id = Interlocked.Increment(ref nextChannelId);
            lock (Sync) ChannelPaths[id] = path;
            bool queued = Enqueue(new Command
            {
                ChannelId = id,
                Kind = CommandKind.Open,
                Path = path,
                Append = append,
                Priority = priority
            });
            if (!queued)
            {
                lock (Sync) ChannelPaths.Remove(id);
            }
            return queued ? id : 0L;
        }

        internal static bool Rotate(string source, string destination)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(destination)) return false;
            EnsureStarted();
            return Enqueue(new Command
            {
                Kind = CommandKind.Rotate,
                Path = source,
                SecondaryPath = destination,
                Priority = AERISFileRecordPriority.Verbose
            });
        }

        internal static bool WriteText(long channelId, string text, bool newLine,
            AERISFileRecordPriority priority)
        {
            return Enqueue(new Command
            {
                ChannelId = channelId,
                Kind = CommandKind.Text,
                Text = text ?? string.Empty,
                NewLine = newLine,
                Priority = priority
            });
        }

        internal static bool WriteCsv(long channelId, AERISCsvField[] fields,
            AERISFileRecordPriority priority)
        {
            return Enqueue(new Command
            {
                ChannelId = channelId,
                Kind = CommandKind.Csv,
                Fields = fields,
                Priority = priority
            });
        }

        internal static bool WriteLog(long channelId, DateTime utc, string prefix,
            string level, string message, AERISFileRecordPriority priority)
        {
            return Enqueue(new Command
            {
                ChannelId = channelId,
                Kind = CommandKind.Log,
                Utc = utc,
                Prefix = prefix ?? string.Empty,
                Level = level ?? string.Empty,
                Text = message ?? string.Empty,
                Priority = priority
            });
        }

        internal static bool WriteNumber(long channelId, double value, bool trailingComma,
            AERISFileRecordPriority priority)
        {
            return Enqueue(new Command
            {
                ChannelId = channelId,
                Kind = CommandKind.Number,
                Number = value,
                TrailingComma = trailingComma,
                Priority = priority
            });
        }

        internal static void Flush(long channelId)
        {
            Enqueue(new Command { ChannelId = channelId, Kind = CommandKind.Flush });
        }

        internal static void Close(long channelId)
        {
            Enqueue(new Command { ChannelId = channelId, Kind = CommandKind.Close });
        }

        // Because the writer is FIFO, this callback runs only after every preceding session
        // write and close. The callback must contain no Unity/KSP object access.
        internal static bool SealSession(string folder, Action afterSealed)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            return Enqueue(new Command
            {
                Kind = CommandKind.Seal,
                Path = folder,
                Callback = afterSealed
            });
        }

        internal static void RetainRawSession(string folder, string reason)
        {
            string normalized = NormalizePath(folder);
            lock (Sync)
            {
                if (!string.IsNullOrEmpty(normalized)) FailedFolders.Add(normalized);
                Interlocked.Increment(ref failures);
                lastFailure = string.IsNullOrEmpty(reason)
                    ? "session marked unsealable" : reason;
            }
        }

        internal static AERISFileWriterTelemetrySnapshot SnapshotTelemetry()
        {
            int queueDepth;
            int openChannels;
            string failure;
            lock (Sync)
            {
                queueDepth = Queue.Count;
                openChannels = Writers.Count;
                failure = lastFailure;
            }
            // Percentile copies/sorts are diagnostic work and must not hold the same
            // lock used by main-thread producers to enqueue flight-critical records.
            double[] batch = CopySamples(BatchSamples, batchCount);
            double[] encode = CopySamples(EncodeSamples, encodeCount);
            double[] disk = CopySamples(DiskSamples, diskCount);
            double[] flush = CopySamples(FlushSamples, flushCount);
            Array.Sort(batch); Array.Sort(encode); Array.Sort(disk); Array.Sort(flush);
            return new AERISFileWriterTelemetrySnapshot
            {
                QueueDepth = queueDepth,
                Capacity = QueueCapacity,
                Enqueued = Interlocked.Read(ref enqueued),
                Written = Interlocked.Read(ref written),
                DroppedCritical = Interlocked.Read(ref droppedCritical),
                DroppedContinuous = Interlocked.Read(ref droppedContinuous),
                DroppedVerbose = Interlocked.Read(ref droppedVerbose),
                FirstDroppedSequence = Interlocked.Read(ref firstDroppedSequence),
                LastDroppedSequence = Interlocked.Read(ref lastDroppedSequence),
                Failures = Interlocked.Read(ref failures),
                OpenChannels = openChannels,
                BatchP95Milliseconds = Percentile(batch, 0.95),
                EncodeP95Milliseconds = Percentile(encode, 0.95),
                DiskP95Milliseconds = Percentile(disk, 0.95),
                FlushP95Milliseconds = Percentile(flush, 0.95),
                LastFailure = failure
            };
        }

        internal static void ShutdownNoWait()
        {
            EnsureStarted();
            lock (Sync)
            {
                if (!accepting || stopped) return;
                var stop = new Command
                {
                    Kind = CommandKind.Stop,
                    Priority = AERISFileRecordPriority.CriticalEvent,
                    Sequence = ++nextSequence
                };
                while (Queue.Count >= QueueCapacity && EvictDataLocked()) { }
                if (Queue.Count >= QueueCapacity)
                {
                    RecordDropLocked(stop);
                    // The queue can only still be full of control records. Do not
                    // discard Close/Seal; reject new producers and stop after drain.
                    accepting = false;
                    stopWhenDrained = true;
                }
                else
                {
                    Queue.AddLast(stop);
                    Interlocked.Increment(ref enqueued);
                    // Stop is now the last accepted command. Closing this race prevents a
                    // producer from appending a write behind Stop and having it discarded.
                    accepting = false;
                }
            }
            Wake.Set();
        }

        static void EnsureStarted()
        {
            lock (Sync)
            {
                if (thread != null || stopped) return;
                accepting = true;
                thread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "AERIS Ordered File Writer",
                    Priority = ThreadPriority.BelowNormal
                };
                thread.Start();
            }
        }

        static bool Enqueue(Command command)
        {
            if (command == null) return false;
            EnsureStarted();
            lock (Sync)
            {
                if (!accepting || stopped) return false;
                command.Sequence = ++nextSequence;
                if (command.IsControl)
                {
                    while (Queue.Count >= QueueCapacity && EvictDataLocked()) { }
                    if (Queue.Count >= QueueCapacity)
                    {
                        RecordDropLocked(command);
                        return false;
                    }
                }
                else
                {
                    if (dataDepth >= DataCapacity || Queue.Count >= QueueCapacity)
                    {
                        bool madeRoom = command.Priority == AERISFileRecordPriority.CriticalEvent &&
                            EvictLowerPriorityLocked();
                        if (!madeRoom)
                        {
                            RecordDropLocked(command);
                            return false;
                        }
                    }
                    dataDepth++;
                }
                Queue.AddLast(command);
                Interlocked.Increment(ref enqueued);
            }
            Wake.Set();
            return true;
        }

        static bool EvictDataLocked()
        {
            LinkedListNode<Command> node = Queue.Last;
            while (node != null)
            {
                LinkedListNode<Command> previous = node.Previous;
                if (!node.Value.IsControl)
                {
                    Queue.Remove(node);
                    dataDepth = Math.Max(0, dataDepth - 1);
                    RecordDropLocked(node.Value);
                    return true;
                }
                node = previous;
            }
            return false;
        }

        static bool EvictLowerPriorityLocked()
        {
            for (int wanted = (int)AERISFileRecordPriority.Verbose;
                wanted >= (int)AERISFileRecordPriority.Continuous; wanted--)
            {
                LinkedListNode<Command> node = Queue.Last;
                while (node != null)
                {
                    LinkedListNode<Command> previous = node.Previous;
                    if (!node.Value.IsControl && (int)node.Value.Priority == wanted)
                    {
                        Queue.Remove(node);
                        dataDepth = Math.Max(0, dataDepth - 1);
                        RecordDropLocked(node.Value);
                        return true;
                    }
                    node = previous;
                }
            }
            return false;
        }

        static void RecordDropLocked(Command command)
        {
            long sequence = command == null ? nextSequence : command.Sequence;
            if (firstDroppedSequence == 0L) firstDroppedSequence = sequence;
            lastDroppedSequence = sequence;
            AERISFileRecordPriority priority = command == null
                ? AERISFileRecordPriority.CriticalEvent : command.Priority;
            if (priority == AERISFileRecordPriority.CriticalEvent)
                Interlocked.Increment(ref droppedCritical);
            else if (priority == AERISFileRecordPriority.Continuous)
                Interlocked.Increment(ref droppedContinuous);
            else Interlocked.Increment(ref droppedVerbose);
            if (command != null && command.ChannelId > 0L &&
                (!command.IsControl || command.Kind == CommandKind.Open ||
                 command.Kind == CommandKind.Close))
                MarkChannelUnsealableLocked(command.ChannelId);
        }

        static void MarkChannelUnsealableLocked(long channelId)
        {
            FailedChannels.Add(channelId);
            string path;
            if (!ChannelPaths.TryGetValue(channelId, out path) ||
                string.IsNullOrEmpty(path)) return;
            string folder = NormalizePath(Path.GetDirectoryName(path));
            if (!string.IsNullOrEmpty(folder)) FailedFolders.Add(folder);
        }

        static void WriterLoop()
        {
            var batch = new List<Command>(MaximumBatch);
            while (true)
            {
                batch.Clear();
                bool drainedStop;
                lock (Sync)
                {
                    while (batch.Count < MaximumBatch && Queue.Count > 0)
                    {
                        Command command = Queue.First.Value;
                        Queue.RemoveFirst();
                        if (!command.IsControl) dataDepth = Math.Max(0, dataDepth - 1);
                        batch.Add(command);
                    }
                    drainedStop = batch.Count == 0 && stopWhenDrained &&
                        Queue.Count == 0;
                }
                if (drainedStop) break;
                if (batch.Count == 0)
                {
                    try { Wake.WaitOne(100); }
                    catch { break; }
                    continue;
                }

                Stopwatch batchWatch = Stopwatch.StartNew();
                bool stop = false;
                for (int i = 0; i < batch.Count; i++)
                {
                    Command command = batch[i];
                    try
                    {
                        if (Process(command)) stop = true;
                    }
                    catch (Exception ex)
                    {
                        MarkFailure(command, ex);
                    }
                    if (stop) break;
                }
                batchWatch.Stop();
                RecordSample(BatchSamples, ref batchIndex, ref batchCount,
                    batchWatch.Elapsed.TotalMilliseconds);
                if (stop) break;
            }
            CloseAll();
            lock (Sync)
            {
                stopped = true;
                accepting = false;
                thread = null;
                Queue.Clear();
                dataDepth = 0;
            }
        }

        static bool Process(Command command)
        {
            if (command == null) return false;
            if (command.Kind == CommandKind.Stop) return true;
            if (command.Kind == CommandKind.Rotate)
            {
                string parent = Path.GetDirectoryName(command.SecondaryPath);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                if (File.Exists(command.Path)) File.Copy(command.Path,
                    command.SecondaryPath, true);
                return false;
            }
            if (command.Kind == CommandKind.Seal)
            {
                string normalized = NormalizePath(command.Path);
                lock (Sync)
                {
                    if (!string.IsNullOrEmpty(normalized) &&
                        FailedFolders.Contains(normalized)) return false;
                }
                Action callback = command.Callback;
                if (callback != null) callback();
                return false;
            }
            if (command.Kind == CommandKind.Open)
            {
                string parent = Path.GetDirectoryName(command.Path);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                var writer = new StreamWriter(command.Path, command.Append,
                    new UTF8Encoding(false), 64 * 1024);
                lock (Sync)
                {
                    StreamWriter old;
                    if (Writers.TryGetValue(command.ChannelId, out old))
                    {
                        try { old.Dispose(); } catch { }
                    }
                    Writers[command.ChannelId] = writer;
                    ChannelPaths[command.ChannelId] = command.Path;
                }
                return false;
            }

            StreamWriter target;
            lock (Sync)
            {
                Writers.TryGetValue(command.ChannelId, out target);
            }
            if (command.Kind == CommandKind.Close)
            {
                Stopwatch closeWatch = Stopwatch.StartNew();
                if (target != null)
                {
                    target.Flush();
                    target.Dispose();
                }
                // Keep the channel path registered until Flush/Dispose succeeds. If either
                // operation throws, MarkFailure can still mark the owning flight folder as
                // unsealable, preventing a silently truncated session from being archived.
                lock (Sync)
                {
                    Writers.Remove(command.ChannelId);
                    ChannelPaths.Remove(command.ChannelId);
                    FailedChannels.Remove(command.ChannelId);
                }
                closeWatch.Stop();
                RecordSample(FlushSamples, ref flushIndex, ref flushCount,
                    closeWatch.Elapsed.TotalMilliseconds);
                return false;
            }
            lock (Sync)
            {
                if (FailedChannels.Contains(command.ChannelId)) return false;
            }
            if (target == null) return false;
            Stopwatch watch = Stopwatch.StartNew();
            switch (command.Kind)
            {
                case CommandKind.Text:
                    if (command.NewLine) target.WriteLine(command.Text ?? string.Empty);
                    else target.Write(command.Text ?? string.Empty);
                    Interlocked.Increment(ref written);
                    break;
                case CommandKind.Csv:
                    WriteCsv(target, command.Fields);
                    Interlocked.Increment(ref written);
                    break;
                case CommandKind.Number:
                    target.Write(command.Number.ToString("G8", CultureInfo.InvariantCulture));
                    if (command.TrailingComma) target.Write(',');
                    Interlocked.Increment(ref written);
                    break;
                case CommandKind.Log:
                    target.Write(command.Prefix ?? string.Empty);
                    target.Write(" [");
                    target.Write(command.Utc.ToString("yyyy-MM-dd HH:mm:ss.fff",
                        CultureInfo.InvariantCulture));
                    target.Write("] [");
                    target.Write(command.Level ?? string.Empty);
                    target.Write("] ");
                    target.WriteLine(command.Text ?? string.Empty);
                    Interlocked.Increment(ref written);
                    break;
                case CommandKind.Flush:
                    target.Flush();
                    watch.Stop();
                    RecordSample(FlushSamples, ref flushIndex, ref flushCount,
                        watch.Elapsed.TotalMilliseconds);
                    return false;
            }
            watch.Stop();
            RecordSample(DiskSamples, ref diskIndex, ref diskCount,
                watch.Elapsed.TotalMilliseconds);
            return false;
        }

        static void WriteCsv(StreamWriter writer, AERISCsvField[] fields)
        {
            if (writer == null) return;
            Stopwatch encodeWatch = Stopwatch.StartNew();
            var builder = new StringBuilder(fields == null ? 16 : Math.Max(64, fields.Length * 12));
            if (fields != null)
            {
                for (int i = 0; i < fields.Length; i++)
                {
                    if (i > 0) builder.Append(',');
                    fields[i].AppendEncoded(builder);
                }
            }
            string encoded = builder.ToString();
            encodeWatch.Stop();
            RecordSample(EncodeSamples, ref encodeIndex, ref encodeCount,
                encodeWatch.Elapsed.TotalMilliseconds);
            writer.WriteLine(encoded);
        }

        static void MarkFailure(Command command, Exception exception)
        {
            StreamWriter failedWriter = null;
            lock (Sync)
            {
                Interlocked.Increment(ref failures);
                lastFailure = (exception == null ? "Unknown" : exception.GetType().Name) +
                    ": " + (exception == null ? string.Empty : exception.Message);
                if (command != null && command.ChannelId > 0L)
                {
                    FailedChannels.Add(command.ChannelId);
                    string path = command.Path;
                    if (string.IsNullOrEmpty(path))
                        ChannelPaths.TryGetValue(command.ChannelId, out path);
                    string folder = string.IsNullOrEmpty(path) ? string.Empty :
                        NormalizePath(Path.GetDirectoryName(path));
                    if (!string.IsNullOrEmpty(folder)) FailedFolders.Add(folder);
                    StreamWriter writer;
                    if (Writers.TryGetValue(command.ChannelId, out writer))
                    {
                        Writers.Remove(command.ChannelId);
                        failedWriter = writer;
                    }
                }
            }
            // A failing filesystem can make Dispose/Flush slow. Never hold the producer
            // queue lock while touching the failed stream.
            if (failedWriter != null)
            {
                try { failedWriter.Dispose(); } catch { }
            }
        }

        static void CloseAll()
        {
            List<StreamWriter> closing;
            lock (Sync)
            {
                closing = new List<StreamWriter>(Writers.Values);
                Writers.Clear();
                ChannelPaths.Clear();
                FailedChannels.Clear();
            }
            for (int i = 0; i < closing.Count; i++)
            {
                try { closing[i].Flush(); } catch { }
                try { closing[i].Dispose(); } catch { }
            }
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

        static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }
            catch { return string.Empty; }
        }
    }
}
