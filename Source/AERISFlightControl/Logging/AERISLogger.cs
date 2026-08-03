using System;
using System.IO;
using UnityEngine;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Logging
{
    internal static class AERISLogger
    {
        private const string Prefix = "[AERIS]";
        private static readonly object Sync = new object();
        private static bool initialized;
        private static bool fileLoggingDisabled;
        private static AERISAsyncFileChannel mainWriter;
        private static AERISAsyncFileChannel sessionWriter;
        internal static Action<string, string> EventSink;
        private static string SessionPath;
        private static string RootPath { get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "AERISFlightControl", "Logs"); } }
        private static string MainPath { get { return Path.Combine(RootPath, "AERISFlightControl.log"); } }

        internal static void Initialize()
        {
            lock (Sync)
            {
                if (initialized) return;
                try
                {
                    string previous = Path.Combine(RootPath, "AERISFlightControl-prev.log");
                    // Rotation, directory creation and stream opening are ordered work on
                    // the dedicated writer. The KSP/main thread performs no filesystem I/O.
                    AERISBackgroundFileWriter.Rotate(MainPath, previous);
                    SessionPath = Path.Combine(RootPath, "Sessions", DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss") + "_session.log");
                    mainWriter = new AERISAsyncFileChannel(MainPath, true,
                        AERISFileRecordPriority.CriticalEvent);
                    sessionWriter = new AERISAsyncFileChannel(SessionPath, false,
                        AERISFileRecordPriority.CriticalEvent);
                    initialized = mainWriter.Available || sessionWriter.Available;
                    fileLoggingDisabled = !initialized;
                    Write("INFO", "Dedicated logger initialized. session=" + SessionPath);
                }
                catch (Exception ex)
                {
                    // A read-only or full disk must never prevent the flight-control
                    // bootstrap from starting. Unity logging remains available.
                    initialized = false;
                    fileLoggingDisabled = true;
                    SessionPath = null;
                    Debug.LogWarning(Prefix + " Dedicated logger unavailable; continuing without file logging: " + ex.Message);
                }
            }
        }

        internal static void Info(string message) { Write("INFO", message); }
        internal static void Warn(string message) { Write("WARN", message); }
        internal static void Error(string message) { Write("ERROR", message); }
        internal static void Verbose(string message) { Write("VERBOSE", message); }

        internal static void Shutdown()
        {
            lock (Sync)
            {
                try { if (mainWriter != null) mainWriter.Dispose(); } catch { }
                try { if (sessionWriter != null) sessionWriter.Dispose(); } catch { }
                mainWriter = null;
                sessionWriter = null;
                initialized = false;
            }
        }

        private static void Write(string level, string message)
        {
            DateTime utc = DateTime.UtcNow;
            string line = string.Format("{0} [{1:yyyy-MM-dd HH:mm:ss.fff}] [{2}] {3}", Prefix, utc, level, message);
            Debug.Log(line);
            try { var sink = EventSink; if (sink != null) sink(level, message); } catch { }
            if (fileLoggingDisabled) return;
            try
            {
                lock (Sync)
                {
                    if (!initialized) return;
                    bool mainAccepted = mainWriter == null ||
                        mainWriter.WriteLogEvent(utc, Prefix, level, message);
                    bool sessionAccepted = sessionWriter == null ||
                        sessionWriter.WriteLogEvent(utc, Prefix, level, message);
                    if (!mainAccepted && !sessionAccepted) fileLoggingDisabled = true;
                }
            }
            catch (Exception ex)
            {
                fileLoggingDisabled = true;
                Debug.LogWarning(Prefix + " Could not write dedicated log: " + ex.Message);
            }
        }
    }
}
