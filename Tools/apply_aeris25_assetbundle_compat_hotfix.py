#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
BACKEND = ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"
BUILDER = ROOT / "GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs"

OLD_PROBE_WINDOWS = "aeris_gpu_bundle_probe_windows.bundle"
OLD_PROBE_LINUX = "aeris_gpu_bundle_probe_linux.bundle"
NEW_PROBE_WINDOWS = "aeris25_gpu_dynamic_colour_probe_windows.bundle"
NEW_PROBE_LINUX = "aeris25_gpu_dynamic_colour_probe_linux.bundle"


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS25 ASSETBUNDLE COMPAT] %s anchor mismatch old=%d" % (label, count))
    return text.replace(old, new, 1), True


backend = BACKEND.read_text()
changed = False
backend, hit = replace_once(
    backend,
    'const string ProbeWindows = "' + OLD_PROBE_WINDOWS + '";',
    'const string ProbeWindows = "' + NEW_PROBE_WINDOWS + '";',
    'backend Windows probe identity')
changed |= hit
backend, hit = replace_once(
    backend,
    'const string ProbeLinux = "' + OLD_PROBE_LINUX + '";',
    'const string ProbeLinux = "' + NEW_PROBE_LINUX + '";',
    'backend Linux probe identity')
changed |= hit
if changed:
    BACKEND.write_text(backend)

builder = BUILDER.read_text()
builder_changed = False
builder, hit = replace_once(
    builder,
    '"' + OLD_PROBE_WINDOWS + '",',
    '"' + NEW_PROBE_WINDOWS + '",',
    'builder Windows installed probe')
builder_changed |= hit
builder, hit = replace_once(
    builder,
    '"' + OLD_PROBE_LINUX + '",',
    '"' + NEW_PROBE_LINUX + '",',
    'builder Linux installed probe')
builder_changed |= hit
if builder_changed:
    BUILDER.write_text(builder)

print("[AERIS25 ASSETBUNDLE COMPAT] AERIS25 probe namespace isolated")
print("windows_probe=" + NEW_PROBE_WINDOWS)
print("linux_probe=" + NEW_PROBE_LINUX)
print("AERIS24 known-good probe names are no longer build targets of AERIS25")
