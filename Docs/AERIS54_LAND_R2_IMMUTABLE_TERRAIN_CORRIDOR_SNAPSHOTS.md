# AERIS54 — LAND-R2 Immutable Terrain/PTC-backed Approach Corridor Snapshots

Producer-coherence prerequisite: ACCEPTED

Base accepted branch: `agent/aeris54-preload-producer-coherence-accepted`  
Base accepted SHA: `86e7fa0135dfc07016672383e0a93fc3f4dce982`

## Objective

Connect the existing LAND Approach Registry / Adaptive Approach Planner to an
immutable, fail-closed corridor snapshot produced from the accepted terrain/PTC
supply path.

LAND-R2 remains observation/planning/display only.

No flight-control authority is granted.

## Existing contracts to preserve

The current planner already refuses certification unless:

`AERISApproachObstacleSnapshot.CorridorComplete == true`

The snapshot model is immutable-by-convention and contains no Unity objects.

LAND-R2 must preserve that behavior.

## Authoritative terrain source

Normal LAND-R2 terrain authority must come from the accepted AERIS terrain stack:

1. TerrainPreloadDatabase / immutable indexed cache data;
2. accepted Exact CPU producer results already persisted through that path;
3. current in-memory immutable terrain representations where equivalent and
   environment-matched.

Normal approach planning must not reintroduce a synchronous PQS sampling loop.

PQS may remain diagnostic/fallback evidence only if explicitly isolated and never
silently promoted to normal production authority.

## Proposed execution law

### Main thread

Main thread owns only runtime-sensitive capture/publication:

- certified runway-direction snapshot;
- body/environment identity;
- generation/revision capture;
- immutable terrain/cache handles or copied scalar input;
- final immutable publication into the Approach Registry.

### Worker

Worker execution is pure compute over immutable/copied data:

- corridor grid construction;
- terrain-height interpolation/aggregation;
- along-track / cross-track transform;
- clearance calculations;
- missed-approach terrain analysis;
- signature generation.

Workers must not dereference mutable Unity/KSP runtime objects.

## Corridor completeness

Fail closed.

A terrain-only result must not claim general obstacle completeness.

Until all required terrain and obstacle sources for a runway direction are proven
complete:

- `CorridorComplete=false`;
- procedure remains `PENDING`;
- no invented glide angle;
- no LAND control authority.

R2 may publish partial diagnostic terrain samples while still keeping
`CorridorComplete=false`.

## Snapshot publication

The intended R2 pipeline is:

`Accepted Terrain/PTC/DB -> read-only corridor sampling -> immutable worker result -> main-thread immutable publication -> AERISApproachObstacleSnapshot -> Approach Registry`

Every published snapshot must carry enough identity to reject stale work:

- runway direction stable ID;
- terrain/environment signature;
- obstacle signature/coverage state;
- generation/revision;
- body identity.

## Safety boundaries

R2 must not change:

- AA / FBW;
- normal AP BANK / HDG / PITCH / V/S / ALT / ACC / VEL laws;
- PROTECT;
- Ground Assist behavior;
- AutoPropPitch boundary;
- accepted TERRAIN_ND behavior;
- accepted PRELOAD_PTC / Exact CPU authority;
- accepted AERIS53 ENV4 HF2 identity;
- legacy NAV removal.

Authority remains:

```text
LAND = OBSERVATION / PLANNING / DISPLAY ONLY
CONTROL = PILOT
```

## R2 acceptance gate

R2 is not complete until runtime evidence proves:

- no synchronous-PQS normal production loop;
- immutable snapshot publication;
- stale-result rejection;
- environment/body/runway revision correctness;
- incomplete coverage remains PENDING;
- accepted terrain DB/cache reads remain stable;
- no AP/FlightCtrlState authority;
- no regression in AERIS53 preload/environment behavior.

## Next after R2

R3 and later LAND stages may consume the immutable corridor snapshot for richer
procedure availability, adaptive vertical-path work and later guidance integration,
while preserving the separate authority gate.
