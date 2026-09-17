# AERIS54 LAND-R2 Immutable Terrain Corridor Producer — Design

Date: 2026-09-17
Branch: `agent/aeris54-land-r2-immutable-terrain-corridor-snapshots`
Base accepted prerequisite: `agent/aeris54-preload-producer-coherence-accepted` @ `86e7fa0135dfc07016672383e0a93fc3f4dce982`

## 1. Objective

LAND-R2 adds a read-only producer that publishes immutable terrain-corridor snapshots for certified runway directions into the existing LAND approach-planning path.

The implementation remains observation/planning/display only. It must not acquire AP, `FlightCtrlState`, AA/FBW, PROTECT, throttle, brake, steering, or any other flight-control authority.

The selected architecture is **B: bounded lazy production for every certified runway direction, with selected/ARMED directions given priority**.

## 2. Existing safety contracts preserved

The existing approach planner already treats a missing obstacle snapshot or `CorridorComplete == false` as `PENDING`. LAND-R2 preserves that gate unchanged.

The existing `AERISApproachObstacleSnapshot` model is immutable-by-convention and contains no Unity objects. R2 extends its identity/coverage metadata but does not weaken cloning or publication isolation.

Normal terrain authority remains the accepted Terrain/PTC/Preload DB/cache stack. R2 must never add a normal synchronous PQS sampling loop.

The producer-coherence hotfix remains authoritative. R2 must use the current body/environment identity supplied by the terrain stack, including `PQS_RUNTIME_FALLBACK_V1` environments when that is the accepted effective producer policy for the process.

## 3. Architecture

### 3.1 Components

R2 adds two focused components.

`Terrain/AERISTerrainCorridorReadService.cs`

- Internal read-only broker over accepted terrain metadata and preload DB/cache reads.
- Exposes only LAND-specific immutable request/result contracts.
- Resolves body/environment-matched tile keys without exposing the mutable terrain database object to Landing code.
- Schedules any chunk read/decode through the existing shared performance scheduler.
- Never calls PQS and never generates missing terrain.

`Landing/AERISApproachTerrainCorridorProducer.cs`

- Owns the bounded lazy queue of certified runway directions.
- Captures runway/body/environment/revision identity on the main thread.
- Requests immutable terrain payloads from the terrain read service.
- Submits pure corridor analysis to a shared worker lane.
- Rejects stale results on the main thread.
- Publishes cloned `AERISApproachObstacleSnapshot` instances keyed by runway-direction stable ID.
- Gives selected and ARMED runway directions priority over background directions.

No dedicated thread is created. The existing `AERISPerformanceRuntime` scheduler remains the only worker execution mechanism.

### 3.2 Integration points

`AERISBootstrap`

- Owns one `AERISApproachTerrainCorridorProducer` instance.
- Constructs it after `Airfields`, `Approaches`, `Landing`, and `Terrain` are available.
- Ticks it from the normal main-thread runtime loop.
- Rebuilds `AERISApproachRegistry` only when the producer publication generation or the airfield database revision changes.
- Resets producer state on scene/vessel/body lifecycle boundaries without touching flight-control state.

`AERISTerrainAwareness` / terrain tile system

- Exposes the LAND read service or the minimal internal APIs required to construct it.
- Supplies current body/environment identity and accepted preload DB metadata.
- Does not grant LAND ownership of PQS or terrain generation.

`AERISApproachModels`

- Extends `AERISApproachObstacleSnapshot` with explicit identity and coverage fields described below.

`AERISFlightControl.csproj`

- Adds explicit `<Compile>` entries for the new source files because the project uses explicit compile items.

## 4. Direction scheduling policy

The producer tracks every runway direction that is both present in the current airfield registry and `HasCertifiedGeometry == true`.

Directions are lazy, not eager. A direction enters the queue when no current snapshot exists for its identity tuple or when an accepted identity/revision changes.

Priority order is:

1. ARMED LAND direction;
2. currently selected direction;
3. other certified directions on the active vessel body;
4. certified directions on other bodies.

The queue is bounded. At most two direction jobs may be in I/O/decode or compute at once. This is an admission limit, not a thread count. The shared scheduler chooses execution threads.

A direction with missing accepted terrain data is not generated from PQS. It remains pending and is retried only after a relevant terrain database generation/environment revision changes, or when it becomes selected/ARMED and the accepted terrain stack independently makes new data available.

Repeated per-frame requeueing is forbidden.

## 5. Identity and stale-work rules

Each corridor request captures an immutable identity tuple:

- `DirectionStableId`;
- runway geometry revision;
- airfield database revision;
- body name;
- body radius;
- terrain environment hash/signature;
- terrain database request generation;
- terrain database content generation;
- producer request generation.

A worker result may be published only if all captured values still match the current main-thread state.

Any mismatch causes a silent functional rejection plus one bounded diagnostic log event. The result is never patched into the new identity and never persisted under a different environment.

Changing selection alone does not invalidate a valid snapshot. Changing certified runway geometry, body/environment identity, or relevant terrain database content does.

## 6. Terrain read contract

### 6.1 Metadata phase

The main thread requests a metadata-only coverage plan for the runway corridor. This phase may inspect immutable/locked terrain index metadata but performs no chunk file I/O and no PQS access.

The plan contains only value data and tile keys.

### 6.2 I/O/decode phase

The read service submits one bounded batch to the shared scheduler. The worker may use the accepted preload database read/decode path and warm cache, but the result returned to the corridor producer is a deep-cloned immutable tile payload set.

Landing code never receives the terrain database instance, mutable cache collections, `CelestialBody`, `PQS`, `PQSMod`, `Vessel`, `Transform`, `GameObject`, or other Unity/KSP runtime objects.

### 6.3 Missing data

If any required sample position has no environment-matched accepted terrain tile, the read result records incomplete terrain coverage.

R2 does not synchronously fill the gap. It publishes either no snapshot or a partial diagnostic snapshot with `TerrainCoverageComplete=false`; in either case `CorridorComplete=false`.

## 7. Corridor geometry and sampling

The analysis frame follows the existing model convention:

- positive along-track = outward from landing threshold along reciprocal final course;
- cross-track positive = right of inbound localizer;
- terrain sample elevations are ASL meters;
- every R2 sample has `IsTerrain=true`.

The final-approach terrain domain extends from threshold to `AERISApproachPlanningLimits.MaximumCaptureDistanceMeters` and laterally to at least `CorridorHalfWidthMeters` on both sides.

The missed-approach terrain domain extends from threshold in the runway heading direction far enough to evaluate the existing minimum missed-approach altitude constraint. R2 records that terrain-only result separately and does not convert it into general obstacle clearance.

Sampling density is source-aware rather than hardcoded to a single world spacing:

- prefer the highest-fidelity environment-matched accepted tile already available for a sample position;
- never downgrade below complete Far coverage for a point counted as terrain-covered;
- interpolate only inside one immutable tile whose sampling is complete and quality is 100;
- do not interpolate across a missing tile seam;
- include source tile stable ID and source LOD in the terrain signature input.

R2 does not claim certified obstacle clearance from Far-only terrain. Fidelity metadata is diagnostic in R2 and becomes an input to later LAND stages.

## 8. Pure worker analysis

After tile decode, corridor analysis is pure compute over copied value objects.

The compute payload contains:

- runway threshold latitude/longitude/elevation;
- certified inbound heading;
- body radius;
- planning limits;
- immutable terrain tiles/value arrays;
- identity tuple;
- sampling plan.

The worker performs:

- geodesic/local along-track and cross-track transforms;
- terrain interpolation from immutable tiles;
- final-corridor sample construction;
- terrain-only clearance diagnostics;
- missed-approach terrain analysis;
- deterministic terrain signature generation;
- coverage/fidelity summary generation.

The worker must not dereference mutable Unity/KSP objects and must not call logging APIs that require main-thread runtime state.

## 9. Snapshot model changes

`AERISApproachObstacleSnapshot` gains explicit fields:

- `BodyName`;
- `EnvironmentSignature`;
- `AirfieldDatabaseRevision`;
- `RunwayGeometryRevision`;
- `TerrainDatabaseGeneration`;
- `TerrainCoverageComplete`;
- `ObstacleCoverageComplete`;
- `MinimumTerrainSourceLod` or equivalent fidelity summary;
- `TerrainMissedApproachClear` as a terrain-only diagnostic.

Existing fields retain these semantics:

- `TerrainSignature` = deterministic signature of terrain inputs/result;
- `ObstacleSignature` = coverage-state marker plus future obstacle signature material;
- `MissedApproachClear` = general obstacle-cleared claim, therefore **false in R2**;
- `CorridorComplete` = `TerrainCoverageComplete && ObstacleCoverageComplete`, therefore **false in R2** because general obstacle coverage is not yet implemented.

`ObstacleSignature` in R2 uses an explicit marker such as `R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE`; it must not imply that non-terrain obstacles were surveyed.

## 10. Publication and Approach Registry behavior

Publication occurs only on the main thread.

The producer stores a deep clone per direction and increments a publication generation only when a direction snapshot materially changes.

`AERISApproachRegistry.Rebuild` receives a snapshot dictionary produced by the producer. The registry continues cloning procedures and remains read-only to control systems.

Because every R2 snapshot has `CorridorComplete=false`, the adaptive planner continues to emit `PENDING` procedures. R2 terrain samples may be displayed or logged diagnostically, but no procedure becomes shadow-guidance eligible solely because of R2 terrain data.

No glide angle, dogleg, missed-approach clearance, or procedure availability may be invented from incomplete obstacle coverage.

## 11. Reset and invalidation

The producer cancels or invalidates outstanding requests when any of the following changes:

- active scene reset;
- airfield database revision changes;
- runway geometry revision changes;
- terrain environment signature changes;
- terrain database request generation invalidates reads;
- body identity changes;
- producer is disposed/reset.

A normal append that advances terrain database content generation does not cancel unrelated already-decoded immutable work unless it changes a required tile or makes a previously incomplete direction eligible for retry.

Selection changes only reprioritize work.

## 12. Logging and observability

R2 adds bounded structured markers under `[AERIS54][LAND_R2]` for:

- `QUEUE`;
- `READ_READY`;
- `READ_INCOMPLETE`;
- `WORKER_COMPLETE`;
- `STALE_REJECT`;
- `PUBLISH`;
- `DIRECTION_PENDING`;
- `RESET`;
- periodic `SUMMARY`.

Every event includes direction stable ID, body, environment signature prefix, request generation, and reason where applicable.

Logs must prove:

- no PQS normal-production sampling;
- no mutable Unity/KSP object on worker payloads;
- selected/ARMED priority;
- bounded in-flight count;
- stale-result rejection;
- terrain-only publication keeps `CorridorComplete=false`;
- zero control authority.

## 13. Static and pure-compute tests

Implementation adds a LAND-R2 audit/test tool that verifies at minimum:

1. project compiles with zero errors;
2. new producer/read-service files are included explicitly in the csproj;
3. no LAND-R2 source references PQS sampling entry points or `FlightCtrlState` writes;
4. synthetic complete tiles produce deterministic terrain samples/signature;
5. missing tile coverage produces `TerrainCoverageComplete=false`;
6. terrain-only complete coverage still produces `ObstacleCoverageComplete=false` and `CorridorComplete=false`;
7. stale identity/revision is rejected before publication;
8. selected/ARMED priority ordering is deterministic;
9. worker payload types contain no Unity/KSP runtime object references;
10. reset invalidates outstanding generations.

## 14. Runtime acceptance gate

LAND-R2 is a candidate only after runtime evidence shows:

- current producer-coherence accepted DLL behavior remains intact;
- certified runway directions enter the bounded lazy queue;
- selected/ARMED direction receives priority;
- terrain reads use accepted DB/cache data;
- at least one complete terrain-read corridor publishes immutable terrain samples;
- at least one intentionally incomplete coverage case remains pending/fail-closed;
- stale work is demonstrably rejected by a controlled revision/environment transition or equivalent deterministic harness;
- all R2 snapshots keep `CorridorComplete=false` and `MissedApproachClear=false`;
- Approach Registry procedures remain `PENDING` from obstacle incompleteness;
- no synchronous PQS normal-production loop appears;
- no AP/AA/FBW/PROTECT/`FlightCtrlState` authority is introduced;
- no ENV4/preload regression or DB write suppression recurrence occurs.

## 15. Explicit non-goals

LAND-R2 does not implement:

- obstacle-provider ingestion beyond terrain;
- procedure certification;
- adaptive glide-angle authority;
- dogleg availability authority;
- LOC capture;
- vertical path capture;
- speed/airbrake coordination;
- flare/touchdown control;
- ground assist handoff;
- go-around control;
- NEW_NAV work.

Those remain later roadmap stages.

## 16. Implementation boundary

The implementation plan should keep changes focused to:

- one LAND corridor producer;
- one terrain read-only broker;
- snapshot identity/coverage model extensions;
- bootstrap lifecycle/publication wiring;
- explicit csproj entries;
- dedicated static/pure/runtime audit tooling.

No unrelated terrain, AP, UI, preload-format, or runway-provider refactor belongs in R2.
