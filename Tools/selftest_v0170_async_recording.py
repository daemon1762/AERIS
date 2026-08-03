#!/usr/bin/env python3
import io
import sys
import tempfile
import zipfile
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import (SOURCE, CheckSuite, extract_csv_header,
    extract_method, extract_typed_array_expressions, read, text_sha256)

suite = CheckSuite("v0.17.0.0 asynchronous recording and archive integrity")
recorder = read(SOURCE / "Recording" / "AERISFlightDataRecorder.cs")

golden = {
    "bankDiagnosticsWriter": ("SampleBankDiagnostics", 86, "e00dfa82e9885405ecb46dfe1a3e9d463baf74db249690dce3bd3f5e2bcb1b93"),
    "hdgDiagnosticsWriter": ("SampleHeadingDiagnostics", 131, "bb00519f7c8d9444b6996d99f79f1b3c9e80d6d65fd0c61ef06f08a50ec0767d"),
    "apSmoothnessWriter": ("SampleApSmoothness", 115, "9f33a11624e0e11a76e55bfec3d8256f571401bc7860c320aa66e1928c9f8404"),
    "pitchDiagnosticsWriter": ("SamplePitchDiagnostics", 37, "a004466c174748b19e739c33618f97adddabc0e832e1e0b622b9b1ff2555252e"),
    "vsDiagnosticsWriter": ("SampleVerticalSpeedDiagnostics", 163, "94a133b1fc9b5f36ae83bef0128856d485fc96271f85e5259afa75d0d1ac04a9"),
    "vsCruiseAccelerationGuideWriter": ("WriteVsCruiseAccelerationGuideDiagnostics", 18, "f407a0569b869696bd4fe8626977f7c1a82ea5ae3ebcd3642f49e4889648392b"),
    "accelerationDiagnosticsWriter": ("SampleAccelerationDiagnostics", 49, "45536e2cbb6e8757eb8bfa83f103ac6f639b52c6284b1a583544eeece4bb1776"),
    "velocityDiagnosticsWriter": ("SampleVelocityDiagnostics", 35, "9c6725c8f43e0b82e35b554d08d97dd399f2dc96a9f742cff0878a07a16eef8c"),
    "altDiagnosticsWriter": ("SampleAltitudeDiagnostics", 243, "8ebe9e60acbf12a176754fd97b53cdf2d79f03c2f40c5b2d4df63e9f5a17ef96"),
    "groundTakeoffDiagnosticsWriter": ("SampleGroundTakeoffDiagnostics", 83, "87a05e70f5dd7ad9a30b7c4f6c1a19c28ff5c83a36f62a1cb3053bd9063592fc"),
    "fdrWriter": ("Sample", 102, "7d42b087f417feecd6503890d1c00cbece4d94cb26605ca0f6059218f16641b8"),
    "cvrWriter": ("RecordCvr", 5, "077bda5a1537547018086c40eec0e946d0124510ec4d9d26558da297182d9b4e"),
}
for writer, (method, count, digest) in golden.items():
    try:
        header = extract_csv_header(recorder, writer)
        declaration = recorder.find("internal void " + method + "(")
        method_source = recorder[declaration:] if declaration >= 0 else recorder
        values = extract_typed_array_expressions(extract_method(method_source, method))
        suite.equal(len(header), count, writer + " accepted header column count")
        suite.equal(len(values), count, writer + " data/header column count")
        suite.equal(text_sha256(",".join(header)), digest,
                    writer + " header is byte-identical to PC1")
        suite.equal(len(header), len(set(header)), writer + " header names are unique")
    except Exception as exc:
        suite.check(False, writer + " schema parsed", str(exc))

writer_source = read(SOURCE / "Performance" / "AERISBackgroundFileWriter.cs")
suite.check("Queue.AddLast(command)" in writer_source and "Queue.RemoveFirst()" in writer_source,
            "writer preserves FIFO command order")
suite.check("command.Priority == AERISFileRecordPriority.CriticalEvent" in writer_source and
            "EvictLowerPriorityLocked" in writer_source,
            "critical events displace verbose/continuous records first")
suite.check("MarkChannelUnsealableLocked" in writer_source and
            "FailedFolders.Contains(normalized)" in writer_source,
            "any dropped session record prevents archive sealing")
suite.check("command.Kind == CommandKind.Open" in writer_source and
            "command.Kind == CommandKind.Close" in writer_source,
            "dropped Open/Close control records also make a session unsealable")
close_start = writer_source.index("if (command.Kind == CommandKind.Close)")
close_end = writer_source.index("return false;", close_start)
close_block = writer_source[close_start:close_end]
suite.check(close_block.index("target.Flush()") <
            close_block.index("ChannelPaths.Remove(command.ChannelId)"),
            "Close retains the channel path until final Flush/Dispose succeeds")
suite.check("stopWhenDrained" in writer_source and "accepting = false" in writer_source,
            "shutdown cannot accept writes behind Stop")
suite.check("Ownership transfers to the ordered writer" in writer_source,
            "CSV capture avoids a redundant defensive array allocation")

suite.check("if (!recoveryScanRequested)" in recorder and
            "recoveryScanRequested = true" in recorder and
            "QueueRecoveryArchives(RootPath, folder)" in recorder,
            "recovery scan cannot race each vessel-change Close/Seal sequence")
suite.check("if (!AERISBackgroundFileWriter.SealSession" in recorder and
            "session seal was rejected; raw data retained" in recorder,
            "seal queue rejection explicitly retains the raw session")
suite.check("normalizedFields.Add(normalized)" in recorder and
            ".Replace('\"', '_')" in recorder,
            "extension telemetry rejects ambiguous normalized CSV headers")

runtime_source = read(SOURCE / "Performance" / "AERISPerformanceRuntime.cs")
runtime_header = extract_typed_array_expressions(runtime_source, "string")
runtime_declaration = runtime_source.find("void WriteTelemetry(")
runtime_values = extract_typed_array_expressions(
    extract_method(runtime_source[runtime_declaration:], "WriteTelemetry"))
suite.equal(len(runtime_values), len(runtime_header),
            "performance telemetry header/data column count")
suite.equal(len(runtime_header), len(set(value.strip().strip('"') for value in runtime_header)),
            "performance telemetry header names are unique")

archive = read(SOURCE / "Recording" / "AERISFlightDataArchive.cs")
suite.check("excludedOpenFolder" in archive and
            "string.Equals(normalized, immutableExcluded" in archive,
            "startup recovery scan explicitly excludes the newly opened session")
suite.check("entry/source content mismatch" in archive,
            "archive verification compares decompressed bytes with raw source")
suite.check("result.SourceDeleted = TryDeleteSource" in archive and
            archive.index("result.Success = true") < archive.index("result.SourceDeleted = TryDeleteSource"),
            "raw deletion follows successful verification")
suite.check("catch (OperationCanceledException) { throw; }" in archive,
            "archive cancellation is not converted into an ordinary result")

# Executable reference model: a same-size content change must be detected, while an
# exact archive round-trip is accepted.
with tempfile.TemporaryDirectory(prefix="aeris_archive_model_") as temporary:
    root = Path(temporary)
    raw = root / "sample.csv"
    raw.write_bytes(b"seq,value\n1,alpha\n")
    archive_path = root / "flight.zip"
    with zipfile.ZipFile(archive_path, "w", zipfile.ZIP_DEFLATED) as package:
        package.write(raw, raw.name)
    with zipfile.ZipFile(archive_path, "r") as package:
        archived = package.read(raw.name)
    suite.equal(archived, raw.read_bytes(), "archive model exact-byte verification passes")
    changed = b"seq,value\n1,bravo\n"
    suite.equal(len(changed), len(raw.read_bytes()), "corruption model preserves file length")
    raw.write_bytes(changed)
    suite.check(archived != raw.read_bytes(),
                "same-size source corruption is rejected by byte verification")

suite.finish()
