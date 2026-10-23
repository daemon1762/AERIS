# R-ND-01 / R-ND-02 repair and temporary-audit plan

Goal: remove stale traffic alerts after traffic capture is cleared and avoid
per-label style/measurement-object construction, while preserving display output
and flight authority. Continue the already authorized batch/audit/removal workflow.

Baseline: R-REC-03/04 closed at `5c328312197c6e7c1a0b14a1abd4c1402f674c8f`.
Branch: `agent/aeris53-r-nd-01-02`. Existing C#/Unity/Mono tools; no new dependencies.

The original Gate2 finding bodies could not be recovered; only the ND finding
names were available. Re-establish both defects from current executable paths.
The label issue currently appears in FDI `DrawReadableLabel`/`FitText`, not ND's
already cached `DrawLabel`. Do not claim an exact match to unavailable original
finding text. LAND-R2 remains frozen; NEW_NAV stays gated; R-PRELOAD-01 stays rejected.

## Decisions and alternatives

- Traffic: clearing only the alert leaves a pending traffic result able to revive
  it. Use the existing scheduler's traffic-key cancellation and clear both published
  frame and alert together. Add a small runtime entry point and route existing ND
  clear sites through it; preserve no-runtime fallback. Do not add a global timeout
  or change threat/CPA math, other job lanes, flight modes or ownership.
- Labels: retain fitted text, font selection, shadow and colors. Cache one fitting
  style per existing source style, clear that cache when EnsureStyles rebuilds,
  reset the fitting font for each label, and reuse measurement GUIContent. Do not
  cache arbitrary text strings or redesign the layout. Ellipsis-string allocation
  is outside this bounded object-construction fix.

## Steps

- [x] Add retained C# fixtures extracted from real ND/FDI methods. Traffic tests
  use the real scheduler/generation/traffic pipeline with only game-host/logger
  boundaries. Reproduce clear/off, invalid input, viewport exit, dispose, profile
  change, missing runtime, stale queued commit and subsequent healthy resumption.
- [x] Add label boundary tests for constructor counts after warmup, long-to-short
  recovery, independent source styles, resize invalidation, unchanged source style,
  font/ellipsis parity, GUI-color restoration including exceptions and tiny rects.
  Run against baseline before changing production.
- [x] Apply each minimal production fix and keep separate issue commits. Verify
  current findings and existing regressions, including ND auxiliary self-tests.
- [x] Prepare a separate temporary runtime auditor. Isolated controlled traffic
  and GUI scenarios must never inject threats or alter live settings/controls.
  Live inspection reads the actual instance; audit output identifies the DLL and
  bounds its claims. Ordinary label appearance still requires user observation.
- [ ] Review independently, publish one build/install batch, and obtain runtime
  audit plus normal behavior. Retire the DLL after successful collection, then
  delete temporary runtime source and installer/collector, retain ordinary tests
  and evidence, and confirm normal behavior once more before closure.

Review focuses: an already-completed worker result after clear; clearing without
a runtime; OFF-to-ON resumption; fitted-style shrink leaking into the next label;
style cache growth during repeated scale changes. No broad GC/performance or
live collision-avoidance claim follows from isolated constructor-count tests.
