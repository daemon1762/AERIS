# AERIS51 — PRELOAD COASTLINE FASTPATH C1+C2

Base: LAND-R1 PASS  
Base SHA: `bfcc1e62d4f37375e537770c76ed4f6683e120b8`

This is a preload optimization side-step between LAND-R1 and LAND-R2. It does not add LAND control authority.

## Problem

The accepted preload path completes broad FAR terrain coverage, then performs a second all-FAR scan. Coastal FAR tiles are resampled at 129x129 before preload may report 100%.

The accepted path is safe but cold rebuilds spend disproportionate time in this final coastline phase.

## C1 — FAR-inline transient coastline index

When a newly generated FAR tile is already hot in memory, AERIS51 records whether its 33x33 class mask contains both land and water.

On an uninterrupted cold rebuild:

1. every generated FAR tile is classified exactly once while hot;
2. when FAR persistence is complete, the transient index must contain exactly the full FAR tile count;
3. non-coastal FAR tiles are marked processed directly;
4. only coastal candidates enter high-density refinement;
5. the all-FAR disk reread/classification pass is bypassed.

The transient index is not persisted and is never trusted after an interrupted/partial rebuild. If its coverage is incomplete, AERIS falls back automatically to the accepted durable disk scan.

Safety rule:

`TRANSIENT INDEX INCOMPLETE => ACCEPTED DISK SCAN`

## C2 — certified Exact CPU coastline worker

For bodies already certified for AERIS47 Exact CPU production, the 129x129 coastline refinement no longer needs to traverse the normal per-sample terrain block pipeline.

Main thread captures:

- the accepted immutable Exact CPU terrain snapshot;
- three body-relative latitude/longitude basis vectors;
- one independent reconstruction witness.

The witness must prove that pure-math reconstruction matches the current KSP body-relative coordinate convention. A mismatch fails closed.

GeneralCompute worker then:

1. reconstructs all 129x129 unit directions from primitive basis data;
2. evaluates the accepted Exact CPU snapshot;
3. derives land/water class flags;
4. builds the coastline vector;
5. returns only immutable arrays/result metadata.

Main thread validates the result and performs the existing persistent commit.

No CelestialBody, PQS, PQSMod, or Unity runtime object is retained by the worker payload.

## Parallelism

Exact coastline jobs use:

`max(2, min(8, scheduler worker count))`

Non-Exact bodies retain the historical bounded legacy path with a two-tile high-density admission limit.

## Fail-closed behavior

- Exact snapshot/capture failure: pause the body, no PQS coastline persistence into the Exact environment.
- Exact worker failure: pause the body, no unsafe commit.
- Scheduler admission pressure: retry later; do not fall back merely because the Exact worker lanes are busy.
- Unsupported/non-Exact body: use the accepted legacy coastline path.
- Interrupted/partial cold rebuild: use the accepted disk scan.

## Runtime evidence

Expected markers:

```
[AERIS51][PRELOAD_COAST_FAST]; ... event=TRANSIENT_INDEX_ACTIVATED ... disk_rescan=false
[AERIS51][PRELOAD_COAST_FAST]; ... event=EXACT_WORKER_QUEUE ...
[AERIS51][PRELOAD_COAST_FAST]; ... event=EXACT_WORKER_COMMIT ... authority=EXACT_CPU
[AERIS51][PRELOAD_COAST_FAST]; ... event=COMPLETE ...
```

Forbidden markers during a successful Exact cold rebuild:

```
event=SNAPSHOT_FAIL
event=WORKER_FAIL
[AERIS49][ENV4_DB_WRITE_SUPPRESSED]
```

## Frozen boundaries

No intended changes to:

- AA / FBW
- AP control laws
- PROTECT
- Ground Assist
- LAND authority/arbitration
- ND authority
- accepted ENV4 producer identity
- Terrain tile payload format

LAND remains observation/display-only at the LAND-R1 boundary.

## Acceptance

AERIS51 is not accepted merely because it compiles.

Acceptance requires a cold rebuild with:

- transient index activation;
- explicit `disk_rescan=false`;
- Exact worker queue and commit evidence;
- zero Exact worker/snapshot failures;
- zero ENV4 DB-write suppression;
- at least one completed ocean-body coastline phase.

After PASS, freeze this optimization and resume LAND-R2.
