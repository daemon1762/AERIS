#!/usr/bin/env python3
"""Generate a throwaway isolated Settings copy; keep production code untouched."""
from pathlib import Path
import hashlib
import sys

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source/AERISFlightControl"


def enum_block(source, name):
    marker = "internal enum " + name
    if source.count(marker) != 1:
        raise ValueError("ambiguous enum: " + name)
    start = source.index(marker)
    opening = source.index("{", start)
    end = source.index("}", opening) + 1
    return source[start:end]


def prepare(folder):
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    raw = (SOURCE / "Settings/AERISSettings.cs").read_text()
    copied = raw
    substitutions = {
        "using AERISFlightControl.Logging;": "using AERIS53Gap2Isolation.Logging;",
        "using AERISFlightControl.Terrain;": "using AERIS53Gap2Isolation.Terrain;",
        "namespace AERISFlightControl.Settings": "namespace AERIS53Gap2Isolation.Settings",
        'System.IO.Path.Combine(KSPUtil.ApplicationRootPath, "GameData/AERISFlightControl/Config/AERISSettings.cfg")':
            "global::AERIS53Gap2Scratch.PathName",
    }
    for old, new in substitutions.items():
        if copied.count(old) != 1:
            raise ValueError("source boundary changed: " + old)
        copied = copied.replace(old, new)
    (folder / "IsolatedSettings.cs").write_text(copied)
    enums = []
    for name in ("AERISTerrainDisplayMode", "AERISTerrainGpuMode", "AERISTerrainColourPreset"):
        enums.append(enum_block((SOURCE / "Terrain/AERISTerrainTileContracts.cs").read_text(), name))
    enums.append(enum_block((SOURCE / "Terrain/AERISTerrainPreloadContracts.cs").read_text(), "AERISTerrainPreloadMode"))
    support = "namespace AERIS53Gap2Isolation.Terrain {\n" + "\n".join(enums) + "\n}\n"
    support += '''namespace AERIS53Gap2Isolation.Logging {
    internal static class AERISLogger {
        internal static void Info(string text) { }
        internal static void Warn(string text) { global::AERIS53Gap2Scratch.Warnings.Add(text); }
    }
}
internal static class AERIS53Gap2SourceIdentity {
    internal const string SettingsSha256 = "__SHA__";
}
'''.replace("__SHA__", hashlib.sha256(raw.encode()).hexdigest())
    (folder / "IsolatedSupport.cs").write_text(support)


if __name__ == "__main__":
    if len(sys.argv) != 2:
        raise SystemExit("usage: aeris53_gap2_01_prepare.py OUTPUT_DIRECTORY")
    prepare(sys.argv[1])
