#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
path = ROOT / "Source/AERISFlightControl/Logging/AERISLogger.cs"
text = path.read_text()

def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit(f"[PENICILLIN LOG] {label}: expected 1 anchor, found {count}")
    return src.replace(old, new, 1)

if "PENICILLIN_LOG_POLICY" not in text:
    text = replace_once(text,
'''        private static bool initialized;
        private static bool fileLoggingDisabled;
''',
'''        private static bool initialized;
        private static bool fileLoggingDisabled;
        // PENICILLIN_LOG_POLICY: central suppression only. Existing AA/AP/FBW call
        // sites are deliberately untouched; WARN/ERROR and build/runtime identity
        // markers remain visible even when routine logging is OFF.
        private static AERISOperationHealthLogLevel configuredLogLevel =
            AERISOperationHealthLogLevel.Diagnostic;
''',
        "logger policy field")

    text = replace_once(text,
'''                if (initialized) return;
                try
                {
''',
'''                if (initialized) return;
                LoadConfiguredLogLevel();
                try
                {
''',
        "logger policy load")

    text = replace_once(text,
'''        private static void Write(string level, string message)
        {
            DateTime utc = DateTime.UtcNow;
''',
'''        private static void Write(string level, string message)
        {
            // Central zero-I/O fast path. The caller may already have constructed its
            // message; eliminating call-site formatting inside AA/AP would violate the
            // PENICILLIN no-touch boundary, so that optimization is intentionally out
            // of scope for this update.
            if (!ShouldWrite(level, message)) return;
            DateTime utc = DateTime.UtcNow;
''',
        "logger write gate")

    insert_anchor = '''        private static void Write(string level, string message)
'''
    policy_methods = '''        private static void LoadConfiguredLogLevel()
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

'''
    if insert_anchor not in text:
        raise SystemExit("[PENICILLIN LOG] Write method insertion anchor missing")
    text = text.replace(insert_anchor, policy_methods + insert_anchor, 1)

    path.write_text(text)
    print("[PENICILLIN LOG] central OFF/NORMAL/DIAGNOSTIC/TRACE policy applied")
else:
    print("[PENICILLIN LOG] central logging policy already applied")

print("[PENICILLIN LOG] WARN/ERROR and candidate identity remain unsuppressed")
print("[PENICILLIN LOG] AA/AP/FBW logging call sites untouched")
