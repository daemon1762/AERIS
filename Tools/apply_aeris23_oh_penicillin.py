#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

def replace_once(text, old, new, label):
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"[PENICILLIN] {label}: expected 1 anchor, found {count}")
    return text.replace(old, new, 1)

monitor = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
if not monitor.is_file():
    raise SystemExit("[PENICILLIN] passive monitor source missing")

csproj = ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj"
text = csproj.read_text()
include = '    <Compile Include="Performance\\\\AERISOperationHealthPenicillin.cs" />\n'
if include not in text:
    anchor = '    <Compile Include="Performance\\\\AERISPerformanceRuntime.cs" />\n'
    if anchor not in text:
        raise SystemExit("[PENICILLIN] csproj PerformanceRuntime anchor missing")
    text = text.replace(anchor, anchor + include, 1)
    csproj.write_text(text)
    print("[PENICILLIN] csproj compile include applied")
else:
    print("[PENICILLIN] csproj compile include already applied")

runtime = ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs"
text = runtime.read_text()
marker = "AERISOperationHealthPenicillin.RecordRuntimeFrame("
if marker not in text:
    old = '''            double measuredMainMilliseconds =
                (capturedThisFrame ? snapshotCaptureMilliseconds : 0.0) +
                commitDrainMilliseconds;
            if (IsFinite(frameMilliseconds) && frameMilliseconds >= 0.0)
'''
    new = '''            double measuredMainMilliseconds =
                (capturedThisFrame ? snapshotCaptureMilliseconds : 0.0) +
                commitDrainMilliseconds;
            // Operation Health PENICILLIN: passive observation only. This hook reads
            // already-computed timing values and never changes AA/AP/FBW scheduling.
            AERISOperationHealthPenicillin.RecordRuntimeFrame(
                frameMilliseconds, measuredMainMilliseconds, commitDrainMilliseconds);
            if (IsFinite(frameMilliseconds) && frameMilliseconds >= 0.0)
'''
    text = replace_once(text, old, new, "performance runtime passive hook")
    runtime.write_text(text)
    print("[PENICILLIN] performance runtime passive hook applied")
else:
    print("[PENICILLIN] performance runtime passive hook already applied")

renderer = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
text = renderer.read_text()
marker = "AERISOperationHealthPenicillin.RecordNavigationDisplayBack("
if marker not in text:
    required = [
        "long exactRefreshesAtBackStart = operationHealthProjectionExactRefreshes;",
        "bool staggerBurstTelemetryEligible = frontBufferValid && requestedViewReady;",
        "oh_stagger_back_peak="
    ]
    missing = [token for token in required if token not in text]
    if missing:
        raise SystemExit("[PENICILLIN] stagger burst telemetry prerequisite missing: " +
                         ", ".join(missing))
    old = '''            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Gpu.RecordFrameCost((Stopwatch.GetTimestamp() - frameStartTicks) *
                    1000.0 / Stopwatch.Frequency);
            return rendered;
'''
    new = '''            long penicillinBackEndTicks = Stopwatch.GetTimestamp();
            double penicillinBackMilliseconds =
                (penicillinBackEndTicks - frameStartTicks) *
                1000.0 / Stopwatch.Frequency;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
                runtime.Gpu.RecordFrameCost(penicillinBackMilliseconds);
            long penicillinExactThisBack = Math.Max(0L,
                operationHealthProjectionExactRefreshes - exactRefreshesAtBackStart);
            AERISOperationHealthPenicillin.RecordNavigationDisplayBack(
                penicillinBackMilliseconds, penicillinExactThisBack,
                staggerBurstTelemetryEligible);
            return rendered;
'''
    text = replace_once(text, old, new, "ND BACK passive hook")
    renderer.write_text(text)
    print("[PENICILLIN] ND BACK passive hook applied")
else:
    print("[PENICILLIN] ND BACK passive hook already applied")

build = ROOT / "build_ubuntu.sh"
text = build.read_text()
old_loop = "for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg; do"
new_loop = "for USER_CONFIG in AERISSettings.cfg NavigationDisplayProfiles.cfg AERISOperationHealth.cfg; do"
if old_loop in text:
    count = text.count(old_loop)
    if count != 2:
        raise SystemExit(f"[PENICILLIN] expected 2 config-preserve loops, found {count}")
    text = text.replace(old_loop, new_loop)
elif text.count(new_loop) != 2:
    raise SystemExit("[PENICILLIN] config-preserve loop anchor missing")
build.write_text(text)

config = ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg"
if not config.is_file():
    raise SystemExit("[PENICILLIN] default Operation Health config missing")

print("[PENICILLIN] config preservation applied")
print("[PENICILLIN] passive monitor hooks complete; AA/AP/FBW/PROTECT source untouched")
