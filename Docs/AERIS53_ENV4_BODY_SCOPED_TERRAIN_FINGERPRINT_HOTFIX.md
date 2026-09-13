# AERIS53 — ENV4 Body-Scoped Terrain Fingerprint Hotfix

## Trigger

AERIS52 accepted preload data was observed to reset scan/completion state across many
celestial bodies after GameData changed while installing runway/airfield mods.

Observed failure shape:

- prior preload state was loadable and old chunks were preserved;
- the global terrain GameData digest changed;
- ENV4 environment identity changed on unrelated bodies;
- those bodies entered `RESET_SCAN_PRESERVE_OLD_DB`.

The database itself was not treated as corrupt.

## Root cause

AERIS52 ENV4 appended one global terrain-relevant GameData hash to every body's
environment identity. The classifier was also lexical enough that custom runway/asset
configs containing terrain-like words could be admitted even without celestial-body/PQS
context.

Therefore one Kerbin-only or false-positive GameData change could invalidate Eve,
Laythe, and other unrelated body preload state.

## Hotfix contract

AERIS53 preserves the existing TerrainPreloadDatabaseV3 format and does not delete old
environment chunks automatically.

It changes only environment identity construction:

1. Build a strict terrain-config inventory in the existing background GameData scan.
2. Require actual Body/PQS/Kopernicus context instead of accepting a file merely because
   it contains words such as `MapDecal` or `FlattenArea`.
3. Retain only a compact body-scope probe for each relevant config.
4. Derive a per-body terrain-config hash.
5. Feed the per-body hash, not the global GameData digest, into
   `EnvironmentHashForBody(...)`.
6. Keep the global GameData digest as diagnostic/database metadata only.
7. Log `body_config_hash` beside the existing environment audit.
8. Run an internal scope self-test at startup.
9. Persist a body-local terrain-authority fingerprint separately from the
   canonical environment ID. On the v6 -> v7 state migration, adopt the new
   fingerprint while retaining the existing environment ID, so installing the
   hotfix itself causes zero additional cache reset.
10. Before the first AERIS53 build, the stage runner invokes a fail-closed
    incident recovery helper. It restores a legacy v6 body only when the
    existing AERIS44 log proves that exact current environment was created by an
    all-body transition from a previously automatic-complete and
    coastline-complete environment. The original state is timestamp-backed-up
    and database chunks are never modified.

Expected behavior:

- unrelated runway/UI/part config churn: no terrain environment transition;
- AERIS52 -> AERIS53 migration itself: no additional environment transition;
- the 2026-09-13 observed global-hash incident: previously complete environment
  IDs may be restored from log-proven evidence before the first AERIS53 run;
- Kerbin-only terrain config change: Kerbin may transition, unrelated bodies must not;
- wildcard/global terrain config change: affected bodies may transition;
- old environment DB chunks remain preserved;
- no change to AA/FBW, AP, PROTECT, Ground Assist, TERRAIN_ND display authority,
  Exact CPU producer authority, coastline C1/C2/C3, or LAND control authority.

## Validation

Use:

`bash Tools/aeris53_env4_body_scoped_fingerprint_hotfix.sh <KSP root>`

The runner performs:

- branch/base/diff static gates;
- fail-closed legacy-v6 incident recovery, when exact log/state evidence matches;
- build/install;
- first runtime migration evidence check requiring legacy identity adoption,
  all observed environments to match, and zero environment transitions;
- second unchanged-GameData runtime check requiring zero migration repeats,
  zero body-fingerprint changes, all observed environments to match, and zero
  environment transitions.

This is a hotfix detour before LAND-R2. LAND-R2 remains the next official roadmap step
after AERIS53 is accepted/frozen.
