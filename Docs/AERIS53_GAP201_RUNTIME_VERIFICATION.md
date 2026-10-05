# AERIS53 GAP2-01 settings root compatibility

## Pre-fix evidence

The user's real KSP/Proton report at `2026-10-05T14:02:32.8040061Z` used
`Assembly-CSharp` ConfigNode and the full isolated Settings source with SHA-256
`ae0f315937684e3823d84d6a541f0eacbefebf2f2b40e88a6c92b06c367691d8`.
Both independent Save/Load cases produced `LOADED_ROOT=root` with
`PAYLOAD_SHAPE=DIRECT_VALUES_ON_GENERIC_ROOT`. Serialized sentinels survived,
but the old name-only gate rejected the root in both cases:

| Value | Saved | Loaded before fix |
|---|---:|---:|
| MainWindowX | 731 | 260 |
| ProtectAoAWarningDegrees | 9.5 | 8 |
| ShowSasWarning | False | True |
| FlightDataArchiveLimit | 17 | 10 |

Result: `ROOT_REJECTION_REPRODUCED`, two rejections and zero retained roundtrips.
Production configuration SHA-256 was unchanged before/after:
`65d938645eb4ea1e905f17d5537ba02ed56ee2eefe4abc954d8fcdde8ef98fb7`.
The user ran collect and retired the probe to
`/home/de-mon/.cache/AERIS/gap201-repro/20261005-230346-548719061`.
This evidence covers an isolated in-process roundtrip, not a game restart.

## Fix and local verification

Only production `AERISSettings.Load` node selection changes. Named
`AERIS_SETTINGS` roots retain priority, named children are resolved next,
and the observed generic `root` is accepted when it contains `mainWindowX`,
a key emitted by every Settings.Save. Unrelated named roots, empty/null
roots and generic roots without this saved-settings identity are rejected.
Value parsing, limits, defaults, migrations and Settings.Save are unchanged.

The production Settings class is compiled unmodified into a temporary boundary
executable: 14 cases plus a fresh-process disk reload check. The tests were
observed failing for the original rejection before the fix. The temporary
observer checks isolation and error handling in 15 checks. Existing REG-02,
PROTECT-01/02, B2 hysteresis and R-CORE-01/02 checks contribute another 108.
The modeled ConfigNode boundary is not a replacement for full KSP verification.

## Pending real KSP verification

Run the existing GAP2-01 repro helper with KSP closed. It first runs the
build/install helper on every installation attempt, so an old Release DLL
cannot be mistaken for the patched build. The build helper builds against the
installed KSP assemblies, backs up the production DLL and any existing settings
file, then installs only the production DLL. It does not replace the CFG.
After it installs the temporary probe, put a craft on the runway for ten
seconds, exit KSP, and collect. Keep both the build/install output (HEAD and
matching built/installed DLL hashes) and the runtime report. The v2 observer requires both cases to retain
all sentinels without isolated Settings warnings and the real CFG hash to remain
unchanged during observation: `GAP2-01 RESULT=SETTINGS_ROUNDTRIP_RETAINED`.
Mismatch, missing payload or an exception is inconclusive, never success.

The temporary observer has no GUI or flight-control hooks. Collect retires it;
its source/helper will be removed after runtime verification. No permanent
audit feature is added. A separate manual UI setting/restart check is still
needed to claim full game restart persistence. Do not change flight protection
thresholds merely to exercise persistence.
