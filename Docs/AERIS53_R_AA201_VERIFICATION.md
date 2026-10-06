# AERIS53 R-AA2-01: engine-count changes in thrust balancing

## Scope and status

This finding is isolated on `agent/aeris53-r-aa2-01`, based on GAP2-01
`e967c45aa503f7216a0eda7ff91cb61bd62a2eed`.
The only production change is in
`AA/Models/FlightModel/EngineBalancing.cs`.
There is no new runtime probe, audit function, UI, telemetry or setting.
The optimizer, steering formula, discovery cadence and default OFF state remain
as before. LAND-R2 remains frozen; NEW_NAV remains gated.

Source-level reproduction and local regression results are recorded below.
The user supplied a successful full KSP SDK build/install on 2026-10-06.
The user reported no problems with ordinary dual-engine live checks on
2026-10-07. Targeted balancing OFF/count-change/ON runtime verification remains
pending.
This is not an accepted/closed runtime finding yet.

## Reproduction and cause

`RotationModel.update_moments()` normally discovers engines at cycle zero,
then increments the counter to one. `OnPreAutopilot()` initializes balancing
at counter one, after observing engine torque. Normal enabled rediscovery thus
already initializes in the same callback.

However, `init_engine_balancing()` returns immediately while balancing is OFF.
If discovery changes engine count during that interval, enabling before the
next discovery cycle leaves the previous arrays and optimizer dimensions in use.
Growth from two to three engines throws `IndexOutOfRangeException`; shrinkage
from three to two retains the third optimizer parameter. Enabling for the first
time between discovery cycles also lacks initialized balancing storage.

`Common.Realloc()` retains capacity on shrinkage. Checking capacity alone would
miss stale optimizer dimensions and could still apply a multi-engine limiter
to the sole remaining engine. These cases are part of this count-state finding.

## Minimal correction

Record the engine count covered by balancing initialization. Before updating an
enabled balancer, initialize again if its count differs from the current list.
Use the live engine count for the two-engine minimum. The post callback skips
a count mismatch so it cannot partially write old limits before initialization.
Initialization records zero-constraint cases too, preventing needless repeated
initialization. It continues to use the original allocation and optimization
methods; unchanged-count behavior follows the existing path.

This patch addresses count changes. It does not add identity tracking for
different engine lists of the same count, or alter the existing selection of
balancing axes and retention of limiter values on ordinary rediscovery.

## Regression boundary

`TESTS/aeris53_r_aa2_01_engine_count.py` compiles the complete production
`EngineBalancing.cs`, `GradientLP.cs` and `Matrix.cs`, plus the unmodified
`update_moments()` and `OnPreAutopilot()` method bodies. Only the KSP/Unity
boundary, engine discovery observations and unrelated model functions are
stubbed. No game files are touched. This fixture does not establish actual
KSP event scheduling or live engine physics.

The initial ten cases ran against the unmodified source: five passed and five
failed. The failures were growth, shrinkage, first enable between discoveries,
single-engine stale limiting, and a mismatched post callback. The fixed source
passed all ten. The final fixture additionally covers regrowth within retained
array capacity and an initially single-engine vessel.

On 2026-10-06 the final twelve-case fixture was also run against an isolated
copy of the baseline source: 6/12 passed, with all six count-state failures
reproduced. The fixed production source passed 12/12. The eight existing suites
passed 123 checks (including the Settings fresh-process check), for 135 total.
The fixture compiler emitted three unused-field warnings from its stub model;
the existing REG-02 fixture retained its known five unassigned-field warnings
in each of its two builds. None of these fixture builds had errors.

A read-only review confirmed the source lifecycle and scope, with no Critical
or Important findings. The post-only list-change case is defensive callback
coverage, not a claim that ordinary KSP discovery runs between pre/post calls.
Shell syntax and whitespace checks passed. No complete KSP assembly build has
been run in this workspace because the managed KSP SDK is unavailable here.

## User build/install

Exit KSP completely before running:

```bash
cd /home/de-mon/AERIS42_R042 && \
git fetch origin refs/heads/agent/aeris53-r-aa2-01 && \
(git switch agent/aeris53-r-aa2-01 || git switch -c agent/aeris53-r-aa2-01 FETCH_HEAD) && \
git merge --ff-only FETCH_HEAD && \
bash Tools/aeris53_r_aa2_01_build_install.sh
```

The helper verifies branch and production source scope, runs the new and eight
existing regression suites, builds Release against the installed KSP SDK,
backs up the current DLL and settings, installs exactly one DLL and compares it
with the built file. Settings are backed up but never rewritten by this helper.
Untracked shader/build-identity files are not deleted. Paste the resulting
test summaries, build result, HEAD, DLL hashes and backup path.

## User build/install evidence: 2026-10-06 09:47 JST

The user pasted the complete command output for candidate
`52989b3e59038e8f0105529d8f3963b9b70e5501`:

- New engine-count regression: 12/12 PASS.
- Existing regressions: 123 checks passed, including Settings restoration in
  a fresh process; total 135 checks passed.
- Clean Release build against the user's installed KSP managed assemblies:
  `Build succeeded`, 77 warnings, 0 errors.
- Built and installed DLL SHA-256 both:
  `2919be5e4df61a03d67ab09b195a95fdd696a8e9cb95c85ec9617f5f3e1c8ac9`.
- Backup directory:
  `/home/de-mon/.cache/AERIS/raa201-build-backups/20261006-094744-719467502`.
- Tracked working files were clean. The existing untracked candidate build
  identity and shader directory remained present.

The initial `fatal: invalid reference` came from trying to switch to a local
branch that did not exist yet. The authorized fallback created that branch,
and all subsequent tests, build and installation completed successfully.

This is user-provided build/install evidence, not a claim that this workspace
compiled against KSP or observed a live flight. Targeted runtime acceptance
remains pending; this documentation update does not require rebuilding the DLL.

## Ordinary live checks: user report, 2026-10-07 00:57 JST

After the normal-operation instructions, the user said they would build a
dual-engine vessel. The immediately preceding instruction was to make its
engines individually stoppable/restartable and, while parked with brakes on,
exercise both engines -> one engine -> both engines. The user subsequently
reported `問題なし` (no problems).

At 01:01 JST the user clarified `片方エンジン停止→再起動まで完了`:
one engine was stopped and restarted successfully. This explicitly confirms
the individual engine shutdown/restart operation in the dual-engine setup.
It does not independently observe the internal engine-list count or the
balancing toggle state.

Record this as user-reported ordinary dual-engine runtime confirmation for the
installed candidate. The exact steps performed were not enumerated in that
report, so it does not individually certify every earlier suggested check
(FBW, Shift/Z during Protect assist, or settings restoration after restart).
No KSP log, FDR or CVR from this run has been reviewed here. This evidence does
not establish whether AA thrust balancing was enabled or toggled.

## Remaining targeted runtime verification

The targeted scenario requires the existing AA `FlightModel.balance_engines`
setting (separate from Protect Thrust Assist): initialize with several engines,
turn balancing OFF, change the operational engine count, then turn it ON again.
Observe engine thrust limits and inspect existing KSP/FDR/CVR output for
exceptions. Exercise both growth and shrinkage, including a single remaining
engine. No new UI or live helper is installed to expose this setting. If the
current interface/configuration cannot operate it, report that boundary rather
than treating an ordinary flight as targeted confirmation.
