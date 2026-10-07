# AERIS53 R-AA2-02: DMS coordinate seconds

## Scope and status

Branch: `agent/aeris53-r-aa2-02`.
Baseline: R-AA2-01 verification head
`8b2798bd40e313d49d30599200f5f21a37239bcc`.
R-AA2-01 retains its documented targeted-runtime limitation; carrying its
candidate forward does not close that separate finding.

The only production change is one line in
`AA/GUI/DelayedField/DelayedFieldGeoCoordinates.cs`: the seconds branch reads
`dms[2]` instead of reading the minutes at `dms[1]` a second time.
No other parser behavior, timing, field formatting, numeric setter,
serialization, GUI, navigation mode, or audit capability is changed.
AA's original implementation and author/license notices are preserved.
LAND-R2 remains frozen and NEW_NAV remains gated.

Actual-source regression verification is described below. The user supplied a
successful complete KSP SDK build/install on 2026-10-07 and subsequently
reported no problems with ordinary runtime checks. Targeted live DMS entry
verification remains unconfirmed.

## Cause and reproduction

`DelayedFieldGeoCoordinates.OnUpdate()` splits hemisphere-suffixed DMS input
into degrees, minutes and seconds. It correctly adds degrees and minutes but
the third-token branch previously parsed the second token again.

For `74 39 12W`, the expected value is approximately -74.6533333333 degrees.
The unmodified production class returned -74.6608353 (float): 39 seconds were
used instead of 12. Its original comment's example `74 39 39W` hides the bug
because minutes and seconds coincide.

`CruiseController` owns the NS/EW fields, and its GUI update calls their
`OnUpdate()` methods. Waypoint numeric assignments use `.Value`, which is a
separate path and is unchanged. This correction does not enable any navigation
feature or establish that the standard AERIS interface exposes DMS entry.

## Regression boundary and results

`TESTS/aeris53_r_aa2_02_coordinates.py` compiles the complete production
`DelayedFieldGeoCoordinates.cs` and `DelayedFieldFloat.cs`. It supplies only
Unity text-entry, clock, and logging stubs, and drives the real public
`DisplayLayout()` and `OnUpdate()` methods. Expectations are hand-derived
literals. It never changes game files or replaces the parser with a copy.

Before the production edit, the final fixture passed 8/20 and failed the twelve
DMS cases for the expected seconds defect. After the one-line fix it passed
20/20, with no compiler warnings or errors.

The nine retained regression suites also passed all 135 checks, including
R-AA2-01 and the Settings fresh-process check: 155 total checks passed.
Their existing fixture warnings remained (R-AA2-01: three unused stub fields;
REG-02: five unassigned fields per build). No fixture build had errors.
A read-only review independently reran the coordinate fixture (20/20) and
reported no Critical or Important findings. Shell syntax and whitespace checks
passed, and the production source diff is exactly one changed line in one file.
The KSP SDK is unavailable in this workspace, so the complete KSP assembly
build has not been claimed here.

Coverage: N/S/E/W in both letter cases; different minute/second values; zero
minutes with nonzero seconds and vice versa; degree-only and degree/minute
forms; signed and unsigned decimal inputs; Decimal mode; delayed commit and
editing timeout reset; invalid decimal fallback; waypoint numeric setter; and
existing decimal-comma parsing under fr-FR culture. Decimal inputs retain their
original text as before, while DMS input retains its existing numeric feedback.

The fixture verifies parsing and timing at controlled boundaries. It does not
claim real KSP GUI scheduling, a live cruise target, or flight behavior.

## User build/install

Completely exit KSP, then run:

```bash
cd /home/de-mon/AERIS42_R042 && \
git fetch origin refs/heads/agent/aeris53-r-aa2-02 && \
(git switch agent/aeris53-r-aa2-02 || git switch -c agent/aeris53-r-aa2-02 FETCH_HEAD) && \
git merge --ff-only FETCH_HEAD && \
bash Tools/aeris53_r_aa2_02_build_install.sh
```

The helper checks branch, clean tracked state and production source scope. It
runs the new coordinate fixture and nine prior suites, performs a clean Release
SDK build, requires exactly one existing install target, backs up the DLL and
settings, installs the candidate and checks byte equality. Backup location:
`~/.cache/AERIS/raa202-build-backups/<timestamp>`.
It does not write settings or remove untracked shaders/build identity files.
Paste the regression summaries, build result, HEAD, DLL hashes and backup path.

## User build/install evidence: 2026-10-07 01:19 JST

The uploaded terminal output `貼り付けたテキスト（1）(4).txt` records candidate
`3465b602eb6677fea452c69eb4a94dbfb8cdd166`:

- Coordinate fixture 20/20 PASS; nine prior suites passed 135 checks,
  including Settings restoration in a fresh process: 155 total checks passed.
- Clean Release build against the installed KSP SDK: `Build succeeded`,
  77 warnings, 0 errors.
- Built and installed DLL SHA-256 both:
  `a72db94379ba8990684183ed90acb8f9379f5c1a092bf051f4f70e822a084f96`.
- Backup:
  `/home/de-mon/.cache/AERIS/raa202-build-backups/20261007-011948-211833826`.
- Tracked working files were clean; the existing untracked candidate build
  identity and shaders remained present.

The initial missing-local-branch switch error was followed by successful branch
creation, tests, build and installation. It did not stop the command sequence.
This is user-provided build/install evidence; no live KSP GUI behavior is
established by the build output. The documentation-only record update does not
require rebuilding the installed candidate.

## Ordinary runtime report: 2026-10-07 23:41 JST

Following the instructions to check FBW, manual throttle and restart settings
retention, and conditionally test DMS entry if a suitable field was available,
the user reported `問題なし` (no problems).

Record this as user-reported ordinary runtime compatibility for the installed
R-AA2-02 candidate. The report did not enumerate individual checks or state
whether a DMS field was available and used, so it does not specifically certify
live `74 39 12W` conversion. No KSP log, FDR or CVR from this run was reviewed
here. The actual-source 20/20 fixture and successful full SDK build remain the
specific evidence for the coordinate correction.

This update records evidence only and does not require rebuilding the DLL.

## Remaining targeted DMS runtime verification

After installation, ordinary FBW/manual throttle and restart settings checks
can establish ordinary runtime compatibility. A targeted DMS runtime check
requires an already accessible coordinate field: for example `12 34 56N`
should become approximately `12.5822` after the existing two-second delay, and
`74 39 12W` should become approximately `-74.6533`.
If such a field is unavailable in the current interface, leave targeted live
entry unverified. Do not expose a new UI, bypass NEW_NAV gates, or count an
ordinary flight as a live DMS parsing test.
