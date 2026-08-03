#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01630_testlib import SOURCE, CheckSuite, extract_csv_header, extract_method, extract_string_array_expressions, read

suite = CheckSuite("v0.16.3.0 FDR schema verification")
text = read(SOURCE / "Recording" / "AERISFlightDataRecorder.cs")

pairs = (
    ("bankDiagnosticsWriter", "SampleBankDiagnostics"),
    ("hdgDiagnosticsWriter", "SampleHeadingDiagnostics"),
    ("apSmoothnessWriter", "SampleApSmoothness"),
    ("pitchDiagnosticsWriter", "SamplePitchDiagnostics"),
    ("vsDiagnosticsWriter", "SampleVerticalSpeedDiagnostics"),
    ("vsCruiseAccelerationGuideWriter", "WriteVsCruiseAccelerationGuideDiagnostics"),
    ("accelerationDiagnosticsWriter", "SampleAccelerationDiagnostics"),
    ("velocityDiagnosticsWriter", "SampleVelocityDiagnostics"),
    ("altDiagnosticsWriter", "SampleAltitudeDiagnostics"),
    ("groundTakeoffDiagnosticsWriter", "SampleGroundTakeoffDiagnostics"),
)
for writer, method in pairs:
    try:
        header = extract_csv_header(text, writer)
        values = extract_string_array_expressions(extract_method(text, method))
        suite.equal(len(values), len(header), f"{writer} header/data column count")
        suite.equal(len(header), len(set(header)), f"{writer} header names are unique")
    except Exception as exc:
        suite.check(False, f"{writer} schema parsed", str(exc))

for forbidden in (
    "nav_", "arc_", "path_integrity", "landing_phase", "go_around",
    "hdg_managed_yaw", "vs_nav_path",
):
    suite.check(forbidden not in text.lower(), f"legacy FDR token absent: {forbidden}")

suite.finish()
