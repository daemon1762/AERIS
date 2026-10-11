# AERIS53 R-REC-01 — notify ordered-writer failures on the main thread

Baseline: `edceee46ad439f0a5f40f38ac5345084a5412206`.
Candidate branch: `agent/aeris53-r-rec-01`.

## Proven path and bounded correction

The real ordered writer catches asynchronous filesystem exceptions and calls
`MarkFailure`. This already increments its existing `Failures` counter, records
`LastFailure`, disables processing on failed channels and marks their raw flight
folders unsealable. The performance CSV already includes the failure count;
the failure is therefore not wholly unrecorded. However, the exception does not
reach a normal WARN/ERROR notice. The producer facade can still report Available
and accept a write after the underlying channel fails; neither return value is
a disk-success acknowledgment. Synchronous recorder exception handling does not
catch an exception occurring later on the writer thread.

An isolated real-file open failure reproduced `Failures=1`, `Available=True`
and `WriteLine=True`. The independent healthy channel still saved and the failed
folder's seal callback was suppressed. This establishes the current component
path, not a report that the user's disk or installed game has failed.

Only two production files change:

- `Performance/AERISBackgroundFileWriter.cs` stores a pending exception count and
  the latest immutable operation/path/error text under its existing lock.
  `TryTakeFailureNotice` atomically claims and clears that pending state. It does
  not log, access Unity/KSP or perform filesystem work.
- `Recording/AERISFlightDataArchive.cs` uses its existing main-thread
  `DrainResults` callers to emit one `[FILE_WRITER]` WARN for the claimed group.
  Logging occurs after the writer lock is released. Logger's existing CVR sink
  therefore invokes the game clock on the main thread.

Exceptions between drains coalesce into one notice, containing their count and
the latest cause. Intermediate individual reasons are not all retained. New
exceptions after a claim remain pending for a later drain. Unchanged failed-
channel writes do not create repeated failures; failure of Logger's own files
does not recursively generate more exception notices. WARN uses the existing
Unity/KSP logging route even with routine logging OFF. Healthy file logs and
CVR also receive it through the existing logger, when writable.

There is no new monitor, audit configuration, UI, telemetry column or background
task. Producer admission/Available semantics, sampling, retries, queue/drop
policy, existing telemetry values, folder protection, compression, verification,
retention and flight control remain unchanged. Explicit `RetainRawSession`
calls and queue drops do not acquire new notices in this correction.

The notice describes a writer operation, not only FDR/CVR: the same existing
writer also serves dedicated logs and other files. Its UTC/CVR UT timestamps
are the main drain time. A failure after the final shutdown drain may remain
unreported before exit. `CloseAll` best-effort exceptions and silent admission
rejections are outside this proven `MarkFailure` path; no broader guarantee of
all disk failures being reported is claimed.

## Verification

`TESTS/aeris53_r_rec_01_write_failures.py` compiles the complete production
writer, archive, logger, scheduler and generation registry. It extracts actual
`RecordCvr` and its CSV helpers verbatim. Other recorder methods and the full
KSP lifecycle are not compiled. Unity/config/runtime/clock are boundary stubs;
the clock counts actual calling thread IDs without throwing. Real writer
threads and files are used, with new private paths and a fresh process per case.

Open and rotation errors use a real blocking filesystem path. Write, Flush
and Dispose errors replace only the stream I/O boundary with a throwing stream
after the real ordered open/barrier. This exercises actual production processing
and failure handling, without production injection hooks or changes to game files.

The 14 cases cover:

1. Healthy file output, zero failure notices.
2. Failed async open, operation/path/exception and unchanged admission semantics.
3. Write failure, preserved raw folder and an independent healthy channel.
4. Flush failure with the same safety checks.
5. Close/Dispose failure with the same safety checks.
6. 300 failed opens coalesced without changing the failure count.
7. Both dedicated logger files and their rotation fail; Unity logging and healthy
   CVR still receive one warning, without a feedback loop.
8. CVR itself fails; notification does not create a repeated failure loop.
9. Logging OFF preserves WARN.
10. Reentrant EventSink and a concurrent telemetry reader prove logging occurs
    outside the writer lock and does not redeliver the claimed notice.
11. Repeated writes to a failed channel stay silent; a later independent failure
    gets a fresh notice.
12. Explicit raw retention keeps its existing policy and counter.
13. Failed log rotation leaves its source bytes intact.
14. An exception in a seal callback reaches the same notice path.

Successful cases independently parse the actual emitted CVR CSV for its existing
five-column header, row counts, UTC format, UT, source, WARN severity and text.
An unavailable CVR is not claimed to contain a warning it cannot write.

Final fixture against baseline: **2/14 PASS**; twelve failures specifically from
missing main-drain warnings. Corrected fixture: **14/14 PASS**, with no compiler
warnings. Retained R-REC-02 and earlier suites: **181/181 PASS**. Combined:
**195/195 PASS**. Existing AA2-01 and REG-02 fixture warnings are unchanged.
Independent read-only review found no Critical, Important or Minor issues,
separately passed the **14/14** fixture and checked installer syntax/whitespace.
It approved isolated publication pending desktop SDK/live validation. It did
not independently run the retained 181 checks or the complete KSP lifecycle.

## Desktop build and live check

Exit KSP completely, then run:

```bash
cd /home/de-mon/AERIS42_R042 && \
git fetch origin refs/heads/agent/aeris53-r-rec-01 && \
(git switch agent/aeris53-r-rec-01 || git switch -c agent/aeris53-r-rec-01 FETCH_HEAD) && \
git merge --ff-only FETCH_HEAD && \
bash Tools/aeris53_r_rec_01_build_install.sh
```

The helper permits Source changes only in the two listed files, runs all 195
checks, makes a clean Release build against the desktop SDK, backs up the
installed DLL and existing settings, then installs and compares DLL bytes/hashes.
It preserves settings and untracked shader/build-identity files.

After restart, fly briefly, return to the space center, then start another flight.
Check ordinary controls and expected FDR/CVR and ZIP storage. A healthy session
should not acquire `[FILE_WRITER]` failure notices. Send any such notice for
diagnosis. Live disk-fault injection is not required for this check.

The requested ordinary live checks have a user-reported pass below. Desktop
build output and installed DLL identity remain unverified in supplied evidence.
The candidate is for isolated publication; no main-branch merge is performed.

## User-reported installation and ordinary live check — 2026-10-12 JST

At 01:45 JST the user reported installation completion and proceeding to checks.
At 01:54 JST the user reported: 「現作業でのすべての動作の正常を確認」.
In the context of the requested ordinary flight/session and FDR/CVR/ZIP storage
checks, record those checks as completed normally by the user. This completes
the requested ordinary desktop check for this candidate.

No build/install log, installed DLL hash or recording files were supplied for
independent analysis. The report does not claim live disk-fault injection or
measurement of game-clock thread identity. Fault-notification coverage remains
the isolated component fixture described above, with its stated boundaries.
This evidence update changes documentation only; no rebuild is required.
