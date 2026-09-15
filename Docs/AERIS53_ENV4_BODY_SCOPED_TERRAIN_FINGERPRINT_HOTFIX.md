# AERIS53 — ENV4 Body-Scoped Terrain Fingerprint Hotfix [ACCEPTED]

Accepted branch: `agent/aeris53-env4-body-scoped-terrain-fingerprint-hotfix-accepted`  
Accepted source SHA: `d86632ad63625e584cd96a65a35a0b479dd1e661`  
Accepted desktop DLL SHA256: `33bd27a4c0fe7ae4f4fb27a4eaf5acc6dd03af40ed3a7cab46651f4cd2ed217c`

## Trigger

AERIS52 accepted preload data was observed to reset scan/completion state across
unrelated celestial bodies after GameData changed while runway/airfield mods were
being changed.

The database itself was not corrupt. Old chunks remained available.

## Root cause chain

### HF1 root cause

AERIS52 fed one global terrain-related GameData digest into every body's ENV4
identity. A Kerbin-local or false-positive terrain config change could therefore
invalidate Eve, Laythe and unrelated bodies.

HF1 replaced that global authority with a body-scoped terrain-config identity and
kept the global GameData hash as diagnostic metadata only.

### HF1 stability defect discovered during validation

HF1 also included the live runtime `pqsController.mods` topology in the persistent
body fingerprint. Kerbin proved that this topology can change across otherwise
unchanged KSP starts because runtime PQS attachment/enumeration order is not a
stable persistence contract.

That produced one real Kerbin-only false transition during HF1 validation.

### HF2 accepted authority

HF2 removes live PQS topology from persistent authority.

Persistent body identity is now based on:

- body-scoped terrain-config hash;
- terrain/preload format identity;
- body name;
- body radius;
- ocean state;
- stable producer policy.

Live PQS topology remains available only as a shadow diagnostic hash. It cannot
invalidate persisted terrain by itself.

## State migration and recovery

AERIS53 state format remains v7.

The HF1 -> HF2 migration:

- adopts an explicit HF2 fingerprint schema;
- retains the current canonical EnvironmentHash;
- does not reset a body merely because the fingerprint formula changed.

The recovery helper is fail-closed and supports:

1. the original AERIS52 v6 global-GameData invalidation incident;
2. the AERIS53 HF1 Kerbin false transition when exact state/log evidence matches.

Recovery never modifies or deletes TerrainPreloadDatabase chunks.

During HF2 validation the helper restored exactly Kerbin from the proven HF1 false
transition and reported `database_chunks_modified=false`.

## Accepted runtime evidence

### HF2 first runtime

- scope self-test: PASS
- legacy identity adoptions: 0
- HF2 fingerprint schema adoptions: 15
- body fingerprint changes: 0
- observed bodies: 15
- environment matches: 15/15
- environment transitions: 0
- HF2 contract events: 15
- suspected exceptions: 0

### Unchanged-GameData stability runtime

- scope self-test: PASS
- legacy identity adoptions: 0
- repeated schema adoptions: 0
- body fingerprint changes: 0
- observed bodies: 15
- environment matches: 15/15
- environment transitions: 0
- HF2 contract events: 15
- exact DB write suppressed events: 0
- suspected exceptions: 0
- verdict: `PASS_CANDIDATE`

This evidence is the acceptance basis for AERIS53.

## Frozen contract

AERIS53 acceptance freezes:

- TerrainPreloadDatabase format;
- body-scoped HF2 persistent terrain authority;
- global GameData hash as diagnostic metadata only;
- live PQS topology as diagnostic-only, never persistence authority;
- preservation of old environment chunks;
- fail-closed recovery behavior.

AERIS53 does not change:

- AA / FBW;
- BANK / HDG / PITCH / V/S / ALT / ACC / VEL control laws;
- PROTECT;
- Ground Assist;
- Exact CPU producer authority;
- accepted TERRAIN_ND behavior;
- coastline C1/C2/C3 behavior;
- LAND flight-control authority.

## Roadmap handoff

AERIS53 is accepted/frozen.

Next official stage:

`LAND-R2 — Immutable Terrain/PTC-backed Approach Corridor Snapshots`

LAND-R2 must use the accepted terrain/cache path and must not restore synchronous
PQS sampling as the normal approach authority.
