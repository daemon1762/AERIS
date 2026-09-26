#!/usr/bin/env python3

from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[1]

rec = (
    ROOT
    / "Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs"
).read_text(encoding="utf-8", errors="replace")

arc = (
    ROOT
    / "Source/AERISFlightControl/Recording/AERISFlightDataArchive.cs"
).read_text(encoding="utf-8", errors="replace")

CURRENT = (
    'Path.Combine(KSPUtil.ApplicationRootPath, '
    '"GameData", "AERISFlightControl", "Logs", "FlightData")'
)

LEGACY = (
    'Path.Combine(KSPUtil.ApplicationRootPath, '
    '"GameData", "AERISFlightControl", "FlightData")'
)

checks = [
    ("current_root_unique",
     rec.count(CURRENT) == 1),

    ("legacy_recovery_root_unique",
     rec.count(LEGACY) == 1),

    ("session_uses_current_root",
     "folder = Path.Combine(RootPath," in rec),

    ("legacy_not_session_root",
     "Path.Combine(LegacyRootPath," not in rec),

    ("recovery_receives_both_roots",
     "QueueRecoveryArchives(RootPath, folder, LegacyRootPath)" in rec),

    ("migration_contract_documented",
     "FlightData root is recovery-only so pre-R029 raw sessions are not orphaned."
     in rec),

    ("excluded_folder_normalized",
     "Path.GetFullPath(excludedOpenFolder)" in arc),

    ("primary_root_normalized",
     "Path.GetFullPath(immutableRoot)" in arc),

    ("legacy_root_normalized",
     "Path.GetFullPath(immutableAdditionalRoot)" in arc),

    ("archive_folder_normalized",
     "Path.GetFullPath(folder)" in arc),
]

failed = False

print("=== AERIS52 B3 FLIGHTDATA PATH CONTRACT ===")

for name, ok in checks:
    print(("PASS" if ok else "FAIL") + ": " + name)
    failed |= not ok

print()
print("production_contract=" + ("PASS" if not failed else "FAIL"))

sys.exit(1 if failed else 0)
