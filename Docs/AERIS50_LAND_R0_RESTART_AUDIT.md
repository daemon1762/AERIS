# AERIS50 LAND R0 Restart Audit

Date: 2026-09-13  
Base: AERIS49 accepted TERRAIN_ND  
Base SHA: `648c94bd83fee37b4d12fb4299ae61be542793bd`  
Branch: `agent/aeris50-land-r0-restart-audit`

## Purpose

Restart the independent LAND line from the accepted AERIS49 terrain/ND baseline without
restoring legacy NAV and without granting LAND any flight-control authority during R0.

Historical order restored for this line:

```text
CPU_SHADOW_PRODUCTION [ACCEPTED]
-> PRELOAD_PTC [ACCEPTED]
-> TERRAIN_ND [ACCEPTED]
-> LAND [ACTIVE]
-> NEW_NAV [BLOCKED UNTIL LAND ACCEPTED]
```

R0 is an architecture/status audit only.

## Confirmed runtime-connected assets

### Airfield / runway

- `AERISAirfieldRegistry` is instantiated by `AERISBootstrap`.
- Physical runway identity, provider identity, geometry survey, geometry worker,
  certification worker/cache, operational resolver and field-verified calibration
  infrastructure remain compiled.
- Runway geometry is frozen when LAND is armed.
- Database and geometry revisions are retained across the frozen approach snapshot.

### LAND foundation

`AERISLandingFoundation` is active at runtime and currently implements:

- runway selection state
- LAND ARM
- active-vessel and certification invalidation
- frozen runway-direction snapshot
- approach-side detection
- localizer cross-track geometry
- intercept-angle observation
- glide-path target/error observation
- LOC / glide-path geometry eligibility
- inhibit reason publication
- immutable `AERISRunwayTrackToken` production

Current authority is intentionally:

```text
CONTROL = PILOT
LAND = OBSERVATION ONLY
```

Capture guidance is explicitly disabled in the current foundation.

### ND / UI

The existing ND already consumes LAND observation data and contains:

- runway/approach presentation
- LAND plan drawing
- vertical profile drawing
- LAND ARM / CLEAR controls
- direction selection
- LOC / glide-path eligibility presentation

ND remains display-only and does not own flight control.

### Ground Assist

Ground Assist remains a mature post-touchdown control-capable subsystem with:

- touchdown-session detection
- lateral ground stability
- wheel-brake control
- airbrake link
- throttle interlock
- optional reverse-thrust provider bridge
- drag-chute / parking-hold infrastructure

The public `AERISRunwayTrackToken` contract exists specifically as a future
Touchdown -> Ground Assist geometry handoff, but Ground Assist does not yet consume the token.

## Compiled but runtime-disconnected LAND assets

The following are already compiled but are not instantiated/wired by `AERISBootstrap`:

- `AERISApproachRegistry`
- `AERISAdaptiveApproachPlanner`

This is the most important R0 finding.

The planner already contains:

- Direct / Offset / Dogleg / Steep Direct procedure types
- outer capture, dogleg, final localizer and missed-approach legs
- terrain/obstacle snapshot model
- adaptive glide limits
- final-localizer centerline invariant
- missed-approach construction
- display/shadow-guidance-only procedure contract

Therefore LAND-R1 should connect this existing foundation before creating replacement logic.

## Historical-plan drift found by R0

Historical AERIS13 policy:

```text
3.0 deg -> 6.0 deg
0.1 deg increments
choose the minimum safe angle per runway direction
```

Current `AERISApproachPlanningLimits`:

```text
MinimumGlideAngleDeg           = 2.5
PreferredGlideAngleDeg         = 3.0
NormalMaximumGlideAngleDeg     = 4.0
ObstacleMaximumGlideAngleDeg   = 5.0
ConditionalMaximumGlideAngleDeg= 6.0
GlideAngleStepDeg              = 0.25
MinimumFinalStraightMeters     = 4000
```

The 0.25-degree step is a documented drift from the historical 0.1-degree policy.
R0 does not change it.

## Historical capture gates not yet integrated in the current foundation

The current LAND foundation capture eligibility is primarily geometric.
The historical LAND design also required integration of:

- speed envelope
- BANK / V/S state
- major PROTECT intervention
- runway-length / stopping-distance eligibility
- aircraft landing profile confidence
- stabilized-approach criteria

These remain future LAND gates and must not be silently treated as already implemented.

## Frozen boundaries

R0 must not change:

- AA / FBW
- BANK / HDG / PITCH / V/S / ALT / ACC / VEL laws
- AP gains, targets or cadence
- PROTECT
- Ground Assist behavior
- control arbitration
- accepted TERRAIN_ND behavior
- accepted PRELOAD_PTC / Exact CPU producer behavior
- Contract v2 numbering
- legacy NAV removal

Legacy NAV remains deleted.

## R0 classification

```text
runway_registry              = RUNTIME_CONNECTED
land_foundation              = RUNTIME_CONNECTED_OBSERVATION_ONLY
nd_land_presentation         = RUNTIME_CONNECTED_DISPLAY_ONLY
adaptive_approach_planner    = COMPILED_DISCONNECTED
approach_registry            = COMPILED_DISCONNECTED
runway_track_token           = PUBLISHED_CONTROL_FREE
ground_assist                = EXISTING_CONTROL_CAPABLE_POST_TOUCHDOWN
land_flight_control_authority= NONE
legacy_nav                   = REMOVED
new_nav                      = BLOCKED_UNTIL_LAND_ACCEPTED
```

## Next action: LAND-R1

Connect the existing Approach Registry / Adaptive Approach foundation as
**observation and presentation data only**.

LAND-R1 must:

1. instantiate and publish the approach registry;
2. bind it to certified runway-direction revisions;
3. feed it immutable terrain/obstacle snapshots compatible with current
   TERRAIN_ND/main-thread rules;
4. publish selected procedure and adaptive vertical-path data to LAND/ND;
5. preserve the final-localizer runway-centerline invariant;
6. keep all AP and FlightCtrlState authority disabled.

Only after R1 runtime acceptance should LAND capture-control work begin.
