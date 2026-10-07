# AERIS53 R-AA2-03 — pitch thrust acceleration

## Scope and cause

Baseline: `69bfe4053c995524c7514f94d538852a3477a5e2`.
Candidate branch: `agent/aeris53-r-aa2-03`.

The only production change is in AA `Models/FlightModel/LinearModel.cs`.
The original Atmosphere Autopilot author attribution and license remain intact.

`update_pitch_rot_model()` calculates `pitch_coeffs.et0` from transverse
engine thrust divided by `sum_mass`. It is already an acceleration. In the
normal delayed pitch model, the `dyn_pressure >= 60.0` branch divides this
coefficient by mass again before adding it to gravity, noninertial and lift
accelerations. This disagrees with both the general and undelayed pitch
models, the low-pressure branch, and the corresponding yaw model.

The correction removes only this second division. The speed division,
pressure boundary, signs, lift/torque coefficients, gimbal lag, model
selection, controllers and tuning are unchanged. No runtime observer,
audit feature, configuration migration or UI control is added.

The delayed pitch model is consumed by the existing pitch angular
acceleration and AoA controllers. This corrects their model input; it does
not establish that a particular reported flight symptom has been reproduced.

## Reproduction and regression evidence

`TESTS/aeris53_r_aa2_03_pitch_thrust.py` compiles the complete production
`LinearModel.cs`, `LinearSystemModel.cs` and `Matrix.cs` unchanged. A test
partial supplies values normally collected by the other FlightModel
partials and KSP/Unity. Private model updates are invoked through reflection;
coefficients and the actual model's `eval_row` output are checked against
hand-derived literal expectations. No game files are touched.

Before the production fix: **6/16 PASS**. Ten cases failed numerically,
without compilation errors. For mass 10, transverse thrust 20 and speed
100, the expected zero-state AoA derivative is 0.02; the old delayed model
returned 0.002. Mass 5 returned 0.008 instead of 0.04; mass 0.5 returned
0.8 instead of 0.4. Zero thrust and unit mass conceal the defect.

After removing the second division: **16/16 PASS**. Cases cover mass
scaling, negative/zero thrust, unit mass, the exact pressure boundary,
Stock/FAR branches, additive gravity/noninertial/lift accelerations,
nonzero-state model evaluation, gimbal lag/angular torque, yaw and roll.
These are model tests, not a Unity flight simulation or live KSP flight.

The build/install helper runs these 16 cases and all 155 retained checks
from R-AA2-02: **171/171 PASS locally**. The existing test fixtures emit known
unused-field warnings; the new fixture compiles without warnings.

Independent read-only review found no Critical, Important or Minor issues
and separately confirmed 16/16 new checks, helper shell syntax and
`git diff --check`. The candidate is suitable for publication; runtime
acceptance remains pending.

## Desktop build and runtime status

Full KSP SDK Release build and installation: **pending user execution**.
The local test environment does not contain the KSP managed SDK.

`Tools/aeris53_r_aa2_03_build_install.sh` checks the candidate branch and
tracked cleanliness, permits only `LinearModel.cs` to differ under Source
from the baseline, runs regression checks, performs a clean Release build,
backs up the installed DLL and existing settings, installs the DLL, and
compares built/installed bytes and SHA256. Settings are not rewritten.

After exiting KSP completely, run:

```bash
cd /home/de-mon/AERIS42_R042 && \
git fetch origin refs/heads/agent/aeris53-r-aa2-03 && \
(git switch agent/aeris53-r-aa2-03 || git switch -c agent/aeris53-r-aa2-03 FETCH_HEAD) && \
git merge --ff-only FETCH_HEAD && \
bash Tools/aeris53_r_aa2_03_build_install.sh
```

Then restart KSP and check an existing aircraft in ordinary forward flight:
FBW/manual pitch response and settling after a throttle change, including
powered and idle flight. Report new pitch oscillation, persistent drift or
unresponsive control. This ordinary check cannot quantitatively isolate the
transverse-thrust correction or substitute for an instrumented flight test.
No new instrumentation is required for this candidate.

Live verification remains pending. R-AA2-01's targeted OFF/count-change/ON
sequence and R-AA2-02's targeted live DMS entry remain unconfirmed; their
ordinary flight/settings reports and source-fixture results are retained.
