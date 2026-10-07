# AERIS53 R-REC-02 — archive notifications and CVR thread ownership

Baseline: `e5b781825a5f270ae1622d7bf50a182d92d9d1ea`.
Candidate branch: `agent/aeris53-r-rec-02`.

## Proven path and bounded correction

The recorder's `EndFlight` submits an ordered-writer seal callback. Once
preceding writes and closes complete, that callback calls `QueueArchive`
on the file writer thread. `QueueArchive`, and its `ScheduleNext` path,
previously called `AERISLogger.Info` directly. Logger's EventSink invokes
the current recorder's `RecordCvr`, which calls `Planetarium.GetUniversalTime`
when its CVR channel is open. An active subsequent flight can therefore
expose KSP clock access on the old session's file writer thread.

The reproduction exercises this path with an open CVR channel. It does
not assert that the user's earlier flights suffered a visible failure.

The only production change is `Recording/AERISFlightDataArchive.cs`:

- The existing queued and scheduler-accepted INFO notices go to a separate
  string queue under the existing archive lock.
- The existing `DrainResults` emits them on the main thread, outside that
  lock. Its current callers are Bootstrap Update and recorder BeginFlight/
  EndFlight. No new bootstrap hook or runtime observer is added.
- Notices are capped at the existing result-capacity value (256), independently
  of archive results. On overflow, oldest routine INFO notices are replaced;
  success/failure results retain their existing independent queue and policy.
  A drain emits at most 256 notices, avoiding an unbounded producer-fed loop.

Notification text and logging policy are retained. These INFO notices now
receive their normal UTC/CVR UT timestamps when drained, rather than from
the worker callback. Same-thread archive producers also use the drain, so
their notices may appear at the next drain. Notices queued after the final
main-thread drain at shutdown are not guaranteed to be emitted before exit.

ZIP compression, verification, deletion safeguards, retention, scheduling,
file formats and flight control are unchanged. This fixes the proven archive
notification path; it is not a general thread-safety guarantee for every
external Logger/EventSink caller or recorder API. No new audit feature is added.

## Verification

`TESTS/aeris53_r_rec_02_archive_notifications.py` compiles the complete
production archive, logger, ordered writer, worker scheduler and generation
registry. It also extracts `RecordCvr` and its CSV helpers verbatim from
production. Other recorder methods are not compiled. Runtime hosting, config,
Unity logging, unrelated scheduler display keys and the game clock are
boundary stubs. The clock records calling thread IDs rather than throwing,
so Logger's best-effort exception handling cannot hide an off-thread call.

Every case runs in a new process with private scratch paths and real writer/
scheduler threads. No game settings or installed DLL is accessed.

Before correction: **2/10 PASS**, eight behavior failures. The final fixture
was also rerun against the baseline archive in a temporary directory.
The real seal
callback called the game clock once without a runtime and twice with one;
the notice burst called it 300 times from the producer thread.

After correction: **10/10 PASS**. Cases cover:

1. Seal callback without runtime: queued INFO and unavailable WARN reach CVR
   only through the main drain.
2. Seal callback with runtime: queued/accepted notice text and order preserved.
3. Main producer uses the same drain and notices are delivered once.
4. Duplicate and empty archive requests retain their admission behavior.
5. Actual ZIP output verifies, then raw data is removed. Python's standard
   ZIP reader independently checks the emitted entry and exact contents.
6. Empty-folder archive failure retains raw data and emits its WARN result.
7. Failed seal does not invoke the archive callback or delete raw data.
8. A 300-notice burst stays bounded without displacing the existing 256
   failure results.
9. Logging OFF still suppresses INFO and preserves WARN.
10. An EventSink that starts another archive producer does not block it on
    the archive lock; nested notices and results are delivered correctly.

All successful cases additionally check emitted CVR headers, five-column
CSV rows, UTC format, UT, source and severity. The existing archive retention
notice is included in the successful ZIP case. This is a component fixture,
not live KSP API validation. No full recorder lifecycle or Unity build is
claimed by the extracted-method fixture.

The helper runs these ten cases plus the retained 171 checks: **181 checks**.
The final combined local run passed **181/181**. Independent read-only
review found no Critical, Important or Minor issues, separately ran the
final **10/10** fixture, and checked helper syntax and whitespace. The
candidate is suitable for isolated publication, not a main-branch merge.
Desktop KSP SDK compilation and live checks remain pending. Existing fixture
warnings do not originate in this correction.

## Desktop build and live check

After exiting KSP completely:

```bash
cd /home/de-mon/AERIS42_R042 && \
git fetch origin refs/heads/agent/aeris53-r-rec-02 && \
(git switch agent/aeris53-r-rec-02 || git switch -c agent/aeris53-r-rec-02 FETCH_HEAD) && \
git merge --ff-only FETCH_HEAD && \
bash Tools/aeris53_r_rec_02_build_install.sh
```

The helper restricts Source changes to the archive file, runs all 181 checks,
performs a clean Release build, backs up the installed DLL and existing
settings, installs and compares the DLL bytes/hashes. Settings are not rewritten.

Restart KSP, fly briefly, return to the space center to end the session,
then start another flight. Check ordinary flight control and that completed
sessions continue producing readable ZIPs while the new session records
FDR/CVR. A folder retained on archive failure is consistent with the existing
safeguards; preserve logs for diagnosis if an archive fails. The ordinary
live check cannot prove the clock's thread identity without instrumentation,
which is not being added for this task.

R-AA2-01's targeted count-change sequence and R-AA2-02's live DMS entry
retain their documented limitations. R-AA2-03 has a reported ordinary
flight pass. R-AA2-04 is excluded legacy code and has no production patch.
