# AERIS50 LAND-R1 — Approach Runtime Foundation

Base: LAND-R0 restart audit PASS  
Base SHA: `bd04f97b2a3850746a75da9c46ba3470360e80d9`

## Objective

Reconnect the already-existing independent LAND Approach Registry and Adaptive
Approach Planner to the current runtime without granting flight-control authority.

## R1 changes

- `AERISBootstrap` owns a runtime `AERISApproachRegistry`.
- Airfield database revision changes trigger an Approach Registry rebuild.
- The registry consumes the current immutable Airfield Registry view.
- No terrain/obstacle corridor snapshot is supplied yet.
- Therefore every otherwise eligible approach remains fail-closed `PENDING`.
- LAND UI exposes Procedure Registry status and the selected runway-direction
  procedure state.
- Historical Adaptive Glide resolution is restored from 0.25 deg to 0.10 deg.
- Pending/rejected procedures never display an invented glide angle.

## Safety boundary

R1 does not:

- write `FlightCtrlState`;
- arm BANK/HDG/PITCH/V/S/ALT/ACC/VEL;
- change AP gains, target laws or cadence;
- change AA/FBW;
- change PROTECT;
- change Ground Assist;
- change Terrain/ND producer authority;
- restore legacy NAV.

Authority remains:

```text
LAND = OBSERVATION / DISPLAY ONLY
CONTROL = PILOT
```

## Runtime proof marker

A successful registry rebuild emits:

```text
[LAND_R1][APPROACH_REGISTRY] runtime rebuild;
... PENDING ...
corridorSnapshots=0;
authority=DISPLAY_OBSERVATION_ONLY
```

The absence of corridor snapshots is deliberate in R1.

## Why procedures remain PENDING

`AERISAdaptiveApproachPlanner` already implements Direct, Dogleg and Steep Direct
procedures plus missed-approach construction, but it correctly refuses to certify
a route unless `AERISApproachObstacleSnapshot.CorridorComplete` is true.

R1 preserves that fail-closed behavior.

## Next stage

LAND-R2 must create immutable approach-corridor snapshots from the accepted
Terrain/PTC data path. It must not reintroduce synchronous PQS sampling as the
normal approach authority.

Only after terrain/obstacle completeness is proven may a procedure transition from
PENDING to AVAILABLE / CONDITIONAL.
