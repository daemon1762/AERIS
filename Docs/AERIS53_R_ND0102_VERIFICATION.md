# AERIS53 R-ND-01 / R-ND-02 current-source repair batch

Branch: `agent/aeris53-r-nd-01-02`.
Baseline: `5c328312197c6e7c1a0b14a1abd4c1402f674c8f` (recording batch closed).
Plan: `Docs/AERIS53_R_ND0102_PLAN.md`.

## Scope and evidence provenance

The old Gate2 finding bodies/line references are unavailable; the retrieved names
were “traffic alert latch” and “label GUI allocations.” Both defects were therefore
re-established from current source and failing executable fixtures. This record
does not claim to reconstruct the original finding text. The label issue is in
FDI `AERISFlightInstrument.DrawReadableLabel`/`FitText`; ND `DrawLabel` already uses
existing styles and is not the per-label construction path being fixed.

Only three production files change: `Performance/AERISPerformanceRuntime.cs`,
`UI/AERISNavigationDisplay.cs`, and `UI/AERISFlightInstrument.cs`. No aircraft
control, LAND, terrain authority, threat/CPA formula, settings serialization or
recording/writer schema changes. LAND-R2 stays frozen; NEW_NAV stays gated;
R-PRELOAD-01 remains excluded. No claim of a comprehensive UI/GC optimization.

## R-ND-01: clearing capture must also retire its warning and pending result

Four ND clear paths (viewport deactivation, Dispose, invalid/disabled capture,
vessel-profile switch) cleared the traffic frame but left `AERISTrafficAlertApi`
set. The FDI detail reader can therefore keep showing an old warning. A completed
worker result not yet committed could also republish the warning after a clear.

The new internal runtime clear entry point cancels only the existing traffic job
key on GeneralCompute, then clears both publications. ND routes those existing
clear sites through it, with frame/alert-only fallback when no runtime exists.
Existing generation guards and scheduler cancellation implementation are unchanged.
This fixes those explicit clear paths; it does not add a global warning timeout or
promise freshness in every possible hidden/paused-display situation.

`TESTS/aeris53_r_nd_01_traffic_clear.py` compiles the whole real traffic pipeline,
scheduler and generation registry with extracted ND lifecycle/runtime methods.
Game objects/profile hosting and logger are private boundary stand-ins. A seeded
threat is confined to that fixture. The pending-result case waits for the actual
worker result queue, clears, and drains the actual commit path.

| Case | Baseline | Fixed |
| --- | --- | --- |
| TRAFFIC OFF | FAIL | PASS |
| Invalid vessel | FAIL | PASS |
| Missing performance runtime | FAIL | PASS |
| Viewport off | FAIL | PASS |
| ND disposal | FAIL | PASS |
| Vessel profile switch | FAIL | PASS |
| Completed pending traffic result cannot revive alert | FAIL | PASS |
| Clear followed by healthy traffic resumption | FAIL | PASS |
| Existing normal empty-frame warning clear | PASS | PASS |

Baseline 1/9; fixed 9/9.

## R-ND-02: reuse fitting objects without changing fitted output

Each FDI readable label cloned a GUIStyle. Font fitting constructed GUIContent
for every measurement, including multiple measurements for a long label.
The fix caches one fitting style per existing source style, clears the cache when
EnsureStyles rebuilds the sources, and resets the fitting font on every label.
One measurement GUIContent is reused; its text is cleared in finally. The source
styles, fitting algorithm, minimum-font behavior, ellipsis, shadow and tones stay.
Substring/ellipsis strings and Unity's own internal GUI allocations are outside
this bounded object-construction fix.

`TESTS/aeris53_r_nd_02_label_objects.py` extracts the actual drawing, fitting and
style setup. A deterministic font/GUI boundary counts constructors and captures
output. A frozen pre-fix fitting algorithm supplies expected text/font results.
It is not a Unity-native rendering or whole-engine allocation measurement.

The final fixture against the baseline is **7/11**, with warm-style,
warm-content, alternating-source reuse and resize reuse failing. Fixed is 11/11,
including source-style preservation, long-to-short font recovery, resized cache
boundedness, shadow/tone/color restoration, label/measurement exceptions, tiny
rectangles and fitted-text/font parity across widths, minima and Unicode/null text.
An earlier, weaker fixture was 8/11; the final 7/11 red result is authoritative.

## Verification and known historical checks

The sixteen current regression drivers pass **230/230** (210 retained + 9 + 11).
The generated auditor's twenty isolated cases also pass sequentially in one
process. The sidecar compiles against private stubbed KSP/Unity host boundaries;
the real desktop SDK build and actual Unity visual check remain pending. Shell,
Python syntax, scope and whitespace checks pass. No new test-only production hook.

Two additionally attempted historical checks are red on both baseline and candidate:

| Historical check | Baseline = candidate | Unchanged failures |
| --- | --- | --- |
| `Tools/selftest_v01800_cp2_nd_auxiliary.py` | 174/176 | 2 track-vector token checks |
| `Tools/selftest_v01800_cp35_gate1_presentation_cadence_responsive_ui_candidate1.py` | 40/64 | 24 old cadence/UI/frozen-hash/version checks |

The baseline was extracted from git into a private directory, both checks were
run there and here, and the full failing-label lists were compared identical.
All 26 names are retained in `Docs/AERIS53_R_ND0102_LEGACY_CHECKS.txt`. These are
not silently counted as passing; they are not added to the current build gate or
changed to manufacture a green result. This batch does not change their unrelated
terrain/UI/version expectations. The ordinary build helper retains the existing
fourteen current drivers and adds the two executable ND/FDI regression drivers.

## Temporary runtime auditor

`AERIS53NdBatchAudit.dll` is a separate sidecar. Actual scheduler/pipeline/generation
copies and extracted host/GUI fixtures are renamed into private namespaces. No
isolated threat, logger, GUI boundary or worker crosses into production instances.
The actual flight-side observer reads settings, the warning and live label cache;
it never calls production clear/publish/draw or writes a setting/control.

Live observation requires TRAFFIC OFF held for at least a second, ON again, ND
OFF held for at least a second, ND active again, and initialized FDI styles while
FDI is enabled. It checks no sampled alert while OFF, cache count at most four and no retained
measurement text. This is not a live seeded-threat test: seeded threat and pending
commit checks remain isolated. It is not a native allocation benchmark. The user
must judge actual text appearance, resizing and ordinary flight behavior.
The fitting cache may be empty between GUI events: EnsureStyles can switch from
the resized panel scale back to gauge scale, clearing it legitimately. The auditor
accepts that state; object reuse after warmup is proved by the isolated tests,
not inferred from a positive live cache count.

With KSP fully exited, run `Tools/aeris53_r_nd_01_02_build_install.sh`. It enforces
the branch/production-file scope, refuses an existing uncollected sidecar, runs
the sixteen current drivers, clean-builds production with the desktop SDK, backs
up/installs/checks its DLL and then builds/installs the temporary sidecar.

In KSP, display FDI (ALWAYS if needed) and ND. Switch TRAFFIC OFF for three seconds
then ON. Switch ND OFF for three seconds then display it again. Check label text,
shadow, size changes and ordinary flight. Exit KSP and run
`python3 Tools/aeris53_r_nd_01_02_audit.py collect`.

Collection requires a report from this installation, complete isolated/live
observations, matching loaded/installed DLL identity and unchanged source hashes.
Failure/inconclusive leaves the auditor in place. PASS moves only the sidecar
directory outside GameData under `~/.cache/AERIS/nd0102-audit-retired/<timestamp>`
and retains report/manifest/collection hashes. Private artificial collector tests
pass for healthy retirement and rejection/retention on incomplete evidence,
identity mismatch, explicit audit error and source mismatch; production bytes stay.

After accepted live evidence, remove the temporary runtime source and installer/
collector plus helper invocation, keep ordinary tests/evidence, and verify normal
behavior once more without the sidecar. Runtime audit, retirement and final
ordinary acceptance remain **PENDING**. Neither repair is formally closed yet.

## Independent review

Early production review independently passed traffic 9/9 and labels 11/11 and
found no Critical/Important production issue. It verified cancellation and fitting
cache/font reset, and independently reproduced the final 7/11 baseline label
result. Final sidecar/collector/build review found no blocking issue. A minor
coverage observation was addressed: the live probe now checks the invariants on
every post-grace OFF update, using flags only to suppress duplicate report lines.
The actual probe Update was tested against a private reflected host: delayed
traffic-alert revival, ND-alert revival and ND-viewport reactivation are all
detected after the initial successful OFF sample. The previous guarded observer
missed all three; the fixed observer detects all three. This is a host-boundary
test, not a second live KSP claim. A fourth host-boundary case confirms complete
OFF/ON observation succeeds with a valid zero-entry cache between GUI events;
initialized styles are checked separately. Final collector checks were repeated
against the corresponding report schema and retain their five-case PASS result.
