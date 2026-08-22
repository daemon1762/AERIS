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
        // PENICILLIN_LOG_POLICY: central suppression only. Existing AA/AP/FBW call
        // sites are deliberately untouched; WARN/ERROR and build/runtime identity
        // markers remain visible even when routine logging is OFF.
        private static AERISOperationHealthLogLevel configuredLogLevel =
            AERISOperationHealthLogLevel.Diagnostic;
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
                LoadConfiguredLogLevel();
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

        private static void LoadConfiguredLogLevel()
        {
            configuredLogLevel = AERISOperationHealthLogLevel.Diagnostic;
            try
            {
                string path = Path.Combine(KSPUtil.ApplicationRootPath,
                    "GameData", "AERISFlightControl", "Config",
                    "AERISOperationHealth.cfg");
                ConfigNode root = ConfigNode.Load(path);
                if (root == null) return;
                ConfigNode node = root.GetNode("AERIS_OPERATION_HEALTH");
                if (node == null && root.name == "AERIS_OPERATION_HEALTH") node = root;
                if (node == null) return;
                string enabledText = node.GetValue("enabled");
                bool enabledValue;
                if (!string.IsNullOrEmpty(enabledText) &&
                    bool.TryParse(enabledText, out enabledValue) && !enabledValue)
                {
                    configuredLogLevel = AERISOperationHealthLogLevel.Off;
                    return;
                }
                string levelText = node.GetValue("logLevel");
                if (string.IsNullOrEmpty(levelText)) return;
                switch (levelText.Trim().ToUpperInvariant())
                {
                    case "OFF": configuredLogLevel = AERISOperationHealthLogLevel.Off; break;
                    case "NORMAL": configuredLogLevel = AERISOperationHealthLogLevel.Normal; break;
                    case "DIAGNOSTIC": configuredLogLevel = AERISOperationHealthLogLevel.Diagnostic; break;
                    case "TRACE": configuredLogLevel = AERISOperationHealthLogLevel.Trace; break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(Prefix + " PENICILLIN logging policy load failed; using DIAGNOSTIC: " + ex.Message);
                configuredLogLevel = AERISOperationHealthLogLevel.Diagnostic;
            }
        }

        private static bool ShouldWrite(string level, string message)
        {
            if (string.Equals(level, "ERROR", StringComparison.Ordinal) ||
                string.Equals(level, "WARN", StringComparison.Ordinal))
                return true;

            if (IsCandidateIdentityMessage(message))
                return true;

            if (configuredLogLevel == AERISOperationHealthLogLevel.Off)
                return false;

            if (string.Equals(level, "VERBOSE", StringComparison.Ordinal))
                return configuredLogLevel == AERISOperationHealthLogLevel.Trace;

            return true;
        }

        private static bool IsCandidateIdentityMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;
            return message.IndexOf("[AERIS23_RUNTIME_CANDIDATE]", StringComparison.Ordinal) >= 0 ||
                message.IndexOf("[AERIS23_CANDIDATE", StringComparison.Ordinal) >= 0 ||
                message.IndexOf("candidate=AERIS23_", StringComparison.Ordinal) >= 0;
        }

        private static void Write(string level, string message)
        {
            // Central zero-I/O fast path. The caller may already have constructed its
            // message; eliminating call-site formatting inside AA/AP would violate the
            // PENICILLIN no-touch boundary, so that optimization is intentionally out
            // of scope for this update.
            if (!ShouldWrite(level, message)) return;
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
