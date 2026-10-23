# AERIS53 R-REC-03 / R-REC-04 recording batch

Baseline: `cba42decb7c7fbcf18a62bef57bae7ba5701fde9`.
Branch: `agent/aeris53-r-rec-03-04`.

## Confirmed current paths

R-REC-03: `R016HighRateDiagnosticsEnabled` is false, and the built-in 50 Hz
sampling methods return before recording. Startup nevertheless opened ten
diagnostic channels, wrote their headers and required their admission along with
CVR/FDR. Rejection of an unused channel could prevent core recording.

R-REC-04: `BeginFlight` is called before the main 0.1-second sample gate. A
synchronous startup/admission failure closes partial channels and throws, but the
next update tries again immediately. Bootstrap's five-second error-log throttle
does not throttle attempts. The old audit's “every FixedUpdate” wording is not
the current primary path: main sampling is called from Update. This fix addresses
synchronous startup failure only; asynchronous disk failure delivery remains the
accepted R-REC-01 policy and does not acquire new automatic recovery behavior.

## Changes

Only production `Recording/AERISFlightDataRecorder.cs` changes.

- R-REC-03 gates diagnostic opening/headers and their admission requirements with
  the existing flag. CVR and main FDR remain mandatory. If diagnostics are enabled
  in a future build, all ten existing channels/headers remain required. Metadata
  states `HIGH_RATE_DIAGNOSTICS_ENABLED=False` and does not advertise absent files.
- R-REC-04 permits the first attempt immediately. After failure, the same vessel
  retries at five-second intervals using realtime; a different vessel or an
  explicit flight end can start immediately. Existing partial-channel cleanup,
  raw retention, ordered seal/archive and once-per-recorder recovery scan remain.

Main Sample, AA comparison, RecordCvr, extension schema/telemetry methods and all
CSV header strings were compared with the baseline and are byte-identical.
No Bootstrap/controller/flight authority/settings/archive/writer source changes.
LAND-R2 and other gated/rejected issues remain outside this batch.

## Local evidence

`TESTS/aeris53_r_rec_03_04_startup.py` compiles actual BeginFlight,
BeginFlightCore, EndFlight, channel-availability, close and metadata/helper methods.
Fields are copied; game hosting, realtime and channel admission are memory-only
boundaries. The isolated diagnostic flag is made mutable to test enabled and
disabled policies in one Mono process: a readonly flag otherwise gets JIT-folded.
The production flag is unchanged. Faults never reach production objects/files.
These are startup-policy tests, not a real KSP or real-disk fault claim.

| Case | Baseline | Fixed |
| --- | --- | --- |
| Disabled channels are not opened | FAIL | PASS |
| Unused diagnostic admission cannot block core | FAIL | PASS |
| Enabled diagnostics retain channels/headers | PASS | PASS |
| Enabled diagnostic rejection cleans up | PASS | PASS |
| Core rejection cleans up | PASS | PASS |
| 500 same-vessel calls before deadline | FAIL | PASS |
| Exact five-second retry and second deadline | FAIL | PASS |
| Healthy retry resumes without reopening | PASS | PASS |
| Different vessel starts immediately | PASS | PASS |
| Null/invalid vessel does not consume attempt | PASS | PASS |
| Explicit end permits immediate new flight | PASS | PASS |
| Healthy reentry/end/new flight | PASS | PASS |
| Partial failure retains raw and closes channels | PASS | PASS |
| Recovery scan runs once | PASS | PASS |
| Disabled metadata reflects actual files | FAIL | PASS |

Baseline 10/15; R-REC-03 alone 13/15; both fixes 15/15. The same 15 cases also
pass sequentially in a single process, as required by the temporary runtime DLL.
Existing thirteen regression drivers retain 195/195 PASS; combined 210/210 PASS.
Shell/Python syntax and `git diff --check` pass. The sidecar compiles with stubbed
Unity/KSP host boundaries. At candidate publication, actual desktop SDK
Release/probe build was pending; the reported live results are recorded below.

Collector checks were exercised on private artificial reports/archives: healthy
collection retires only the audit directory and preserves raw/ZIP evidence;
incomplete report, wrong identity, malformed CSV and unexpected disabled file
retain the auditor and reject acceptance. This does not substitute for live data.

## Temporary auditor and desktop steps (historical candidate procedure)

The separate `AERIS53RecorderBatchAudit.dll` has no production hooks or logger
sink. On the KSP main thread it runs the memory-only fixture, then reads the live
recorder's fields at one-second intervals. After ten seconds with at least twenty
new samples, it reports stable session identity, core channel admission and the
absence of all ten disabled channels. It does not call production BeginFlight,
EndFlight, Sample, writer or controls. The auditor runs once per flight and writes
its report during startup and the completed live observation; DLL hashing occurs
once. The retained CVR schema-count event describes supported schema definitions;
metadata states which diagnostic files are actually enabled for this session.
The observer's presence itself adds small main-thread work; no performance timing
claim is made from this run.

1. Fully exit KSP and run `Tools/aeris53_r_rec_03_04_build_install.sh` on the batch
   branch. It runs all regressions, builds/backs up/installs production, then builds
   and installs the sidecar. No image or source SDK is needed beyond desktop KSP.
2. Launch KSP, put a vessel on the runway and wait at least fifteen seconds.
   Perform ordinary controls/flight checks. Return to the space center, wait five
   seconds for the normal sealed archive, then fully exit KSP.
3. Run `python3 Tools/aeris53_r_rec_03_04_audit.py collect` and paste the output.
   Collection requires a report from this installation, matching installed/live
   DLL hashes, a complete isolated/live result, ZIP CRC, exact main/CVR headers and
   row widths, start/end CVR events, disabled metadata and no unused diagnostic
   files. Missing evidence is inconclusive and leaves the auditor installed.
4. On PASS, the collector moves the sidecar directory outside GameData to
   `~/.cache/AERIS/rec0304-audit-retired/<timestamp>/`, retaining manifest, report,
   collection result and ZIP hash. Production logs/archives remain unchanged.
5. After accepted live evidence, remove this batch's temporary runtime source and
   audit installation/collection script from the branch. Remove the auditor call
   from the ordinary build helper; retain startup/older regressions and this record.
   Confirm ordinary flight and FDR/CVR/ZIP again without the sidecar before closing.

The runtime audit and DLL retirement subsequently passed, as reported below.
The final ordinary run without the auditor was subsequently confirmed by the
user, as recorded below. R-REC-03 and R-REC-04 are closed for this repair scope.

## Independent review

An independent reviewer checked production lifecycle/gating, isolated-test fidelity,
read-only live inspection, collection identity/evidence and retirement. The reviewer
ran all fifteen individual startup cases and the same-process repetition, shell
syntax and diff checks. No blocking finding was reported. One report-I/O wording
clarification was applied above. The reviewer did not claim desktop SDK/runtime
verification.

## Reported desktop audit and retirement — 2026-10-24 JST

The user pasted the collector output from the desktop. Its runtime report began
at `2026-10-23T15:46:05.4249200Z` (2026-10-24 00:46:05 JST). This is reported
collector evidence; the underlying report/manifest/ZIP were not separately
uploaded for independent inspection here.

| Observation | Reported result |
| --- | --- |
| Isolated startup cases | 15/15 PASS |
| High-rate diagnostics disabled | True |
| All ten unused diagnostic channels absent | True |
| Core CVR/FDR channels available | True |
| FDR sample growth during observation | 85 |
| Session identity stable | True |
| Live observation | LIVE_CORE_OBSERVED |
| Archived main FDR | 102 columns, 428 data rows |
| Archived CVR | 5 columns, 643 data rows |
| Collector result | BATCH_AUDIT_RESULT=PASS |

Live production DLL SHA256:
`fd06c1afc27599de222403968dcacda41c1174d931a8c6c1c08268b96c487608`.

Flight archive SHA256:
`437c69a65a3047417ef669165bca0353ac4b51d3f3ce350a64d6d85819e01dd0`.

The collector reported moving the auditor directory outside GameData to
`/home/de-mon/.cache/AERIS/rec0304-audit-retired/1792770765470891241`.
The original report/manifest and collection record remain there; the production
flight archive remains in the ordinary FlightData root. This demonstrates reported
healthy core recording and isolated admission-policy checks, not live disk fault
injection, control-quality measurements or exhaustive unrelated flight behavior.

## Auditor removal and final ordinary verification

After that audit, the temporary `TESTS/Runtime/AERIS53RecorderBatchProbe.cs` and
`Tools/aeris53_r_rec_03_04_audit.py` were removed. The ordinary build/install helper
no longer invokes or reinstalls an auditor. The startup regression and older
regressions remain. Production Source is byte-identical to the audited candidate
`57120a6d87b56f420ae1c598c51a15b433d072f4`; no production hooks were present to remove.

After removal, all fourteen retained regression drivers were rerun: 210/210 PASS,
plus the same-process startup repetition. Shell/Python syntax, whitespace and
production-source comparison pass. Independent removal review found no Critical,
Important or Minor issue; it checked deletion completeness, standalone retained
tests, helper flow and the reported-evidence boundary. That reviewer did not rerun
the compiled fixtures; the coordinator did. No new desktop SDK build is claimed.

With KSP fully exited, fetch/fast-forward this branch. No production rebuild is
needed for the removal-only update: the installed production DLL is unchanged,
and the successful collector already removed the sidecar from GameData. Confirm
that `GameData/AERIS53RecorderBatchAudit` remains absent, then start KSP and check
ordinary controls/flight and FDR/CVR/ZIP creation. The result of that final check
is recorded below.

## Final ordinary acceptance — 2026-10-24 JST

The user pasted successful fast-forward installation of the removal-only update
`bfc1eb11ec5a68653df05729356f9b4e9c189421`, including the `監査なし構成 OK`
result of checking that the auditor directory was absent. After the requested
ordinary flight and FDR/CVR/ZIP check, the user reported `問題ない 次に進もう`.
This is user-reported acceptance; no additional raw flight files were supplied.

R-REC-03 and R-REC-04 are **CLOSED** for the agreed fixes. The temporary runtime
auditor and its installer/collector have been removed, permanent regressions
remain, and the ordinary build helper no longer reinstalls an auditor. No further
production rebuild is required for this acceptance-record update.
