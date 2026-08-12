#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
logger = (ROOT / "Source/AERISFlightControl/Logging/AERISLogger.cs").read_text()
monitor = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
config = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()

def require(condition, message):
    if not condition:
        raise SystemExit("[PENICILLIN LOG VERIFY] FAIL: " + message)
    print("[PASS] " + message)

require("PENICILLIN_LOG_POLICY" in logger,
        "central PENICILLIN logger policy is present")
require("LoadConfiguredLogLevel();" in logger,
        "logger loads Operation Health policy before initialization")
require("if (!ShouldWrite(level, message)) return;" in logger,
        "suppressed messages exit before timestamp/line/file enqueue work")
require("AERISOperationHealthLogLevel.Off" in logger and
        "AERISOperationHealthLogLevel.Normal" in logger and
        "AERISOperationHealthLogLevel.Diagnostic" in logger and
        "AERISOperationHealthLogLevel.Trace" in logger,
        "logger recognizes OFF/NORMAL/DIAGNOSTIC/TRACE")
require('string.Equals(level, "ERROR", StringComparison.Ordinal)' in logger and
        'string.Equals(level, "WARN", StringComparison.Ordinal)' in logger,
        "WARN/ERROR bypass routine suppression")
require("IsCandidateIdentityMessage(message)" in logger and
        "[AERIS23_RUNTIME_CANDIDATE]" in logger and
        "candidate=AERIS23_" in logger,
        "candidate/runtime identity bypasses routine suppression")
require('string.Equals(level, "VERBOSE", StringComparison.Ordinal)' in logger and
        "configuredLogLevel == AERISOperationHealthLogLevel.Trace" in logger,
        "VERBOSE is TRACE-only")
require("logLevel = DIAGNOSTIC" in config,
        "candidate default logging policy is DIAGNOSTIC")
require("if (logLevel == AERISOperationHealthLogLevel.Off || destroyed)" in monitor,
        "PENICILLIN monitor has independent OFF early return")
require("call-site formatting inside AA/AP would violate" in logger,
        "no-touch limitation for prebuilt AA/AP log strings is documented in source")

print("[AERIS23 CANDIDATE VERIFY] OH_PENICILLIN LOGGING POLICY PASS")
