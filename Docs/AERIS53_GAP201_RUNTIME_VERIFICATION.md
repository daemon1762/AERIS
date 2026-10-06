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

## Verified real KSP build and roundtrip

The user supplied `貼り付けたテキスト（1）(3).txt`. The candidate at
`b56ea87e2e63d8a550284dce26c9f9163a2e9ef2` passed all 138 checks and a clean
Release build against installed KSP assemblies: 77 warnings and zero errors.
The warning count matches the accepted REG-02 build. Built and installed DLL
SHA-256 both equal:

`432411b83e764b4b0d42c4756a098edc0b19d54f3c054923ddba713a38faea81`

The build helper backed up the prior DLL and existing settings file to
`/home/de-mon/.cache/AERIS/gap201-build-backups/20261006-000208-891775870`.
It installed the production DLL without replacing the CFG. Probe installation
required this fresh build/install, rather than accepting a stale Release DLL.

The v2 report at `2026-10-05T15:09:00.6311140Z` records Settings source SHA-256
`35c69c5026a673f36ac8e4fd72cfbbb0fa0a9fac8bfcc209c1d52e9898e250a2`, which
matches the patched source. Both cases loaded real KSP `ConfigNode` generic
`root` direct values and retained all four sentinels:

| Value | Saved | Case 1 | Case 2 |
|---|---:|---:|---:|
| MainWindowX | 731 | 731 | 731 |
| ProtectAoAWarningDegrees | 9.5 | 9.5 | 9.5 |
| ShowSasWarning | False | False | False |
| FlightDataArchiveLimit | 17 | 17 | 17 |

Result: `SETTINGS_ROUNDTRIP_RETAINED`, with `RETAINED_ROUNDTRIPS=2`.
Real settings SHA-256 before/after remained
`65d938645eb4ea1e905f17d5537ba02ed56ee2eefe4abc954d8fcdde8ef98fb7`.
No isolated Settings warnings or inconclusive result occurred. The report's
`PRE_FIX_ROOT_GATE_REJECTS=True` describes the former name-only predicate;
it is not a rejection by the patched loader.

## Finalization and manual restart acceptance

The user's collect retired the entire probe directory and scratch data to
`/home/de-mon/.cache/AERIS/gap201-repro/20261006-001109-698891150`.
The temporary observer, observer fixture, preparation tool and repro installer
are removed from the branch. The durable production Settings regression remains.
The build/install helper now runs 123 checks and installs only the production
DLL. No permanent audit feature is added. Untracked candidate identity and
shader artifacts are preserved.

Finalization changes no production source, so the verified installed DLL needs
no rebuild for this cleanup. The branch remains `agent/aeris53-gap2-01` without
a main or accepted-branch merge.

The runtime observation exercised the full isolated Settings copy with the
real KSP API; it did not modify live production Settings or test a UI change
across a full game restart.

On 2026-10-06 (Asia/Tokyo), after the requested window-position change and full
KSP exit/restart check, the user reported: "肉眼で保持を確認した". This is
user-reported visual confirmation that the window position persisted across
the game restart; no additional log or screenshot was required. It complements
the real-ConfigNode roundtrip evidence, rather than extending restart coverage
to every Settings field. GAP2-01 acceptance is complete for the reproduced
root-rejection fault and the observed window-position restart path.
