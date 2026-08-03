# AERIS Git Workflow — CP3.75 ND Recovery

## Long-lived references

- `main`: current accepted/integration history.
- `cp3-gate5-candidate14-golden`: immutable historical CP3 Golden authority tag.
- `cp3.75/nd-recovery`: active CP3.75 ND recovery/rebase branch.

## Candidate policy

1. One technical purpose per commit where practical.
2. One runtime gate after each risky presentation/performance change.
3. Do not port rejected CP3.5 ND presentation experiments by default.
4. Restore CP3 Golden visual/geographic behavior before performance optimization.
5. Never trade coastline/runway/map readability below late-CP3 quality for FPS.
6. Preserve protected non-ND subsystems unless an explicit dependency finding is recorded.

## Protected non-ND scope

Unless a dependency audit proves otherwise, protect at minimum:

- `Source/AERISFlightControl/AA`
- `Source/AERISFlightControl/Autopilot`
- `Source/AERISFlightControl/Protect`
- `Source/AERISFlightControl/FlightState`
- `Source/AERISFlightControl/Integrations`
- `Source/AERISFlightControl/Landing` except ND display responsibilities

## CP3.75 phase order

1. Authority and full diff audit.
2. Pure ND rebase to Candidate14 behavior.
3. Runtime geographic/visual verification.
4. Performance baseline.
5. Safe optimization, one purpose at a time.
6. Quality preset redesign only after the above passes.
