# AERIS54 PRELOAD Producer-Coherence Hotfix

## Base

- Accepted base: `d86632ad63625e584cd96a65a35a0b479dd1e661`
- Branch: `agent/aeris54-preload-producer-coherence-hotfix`
- LAND-R2 remains blocked until this hotfix is accepted.

## Incident

Additional Kerbin runway/scenery PQS height modifiers caused the accepted Exact CPU
runtime snapshot certificate to fail closed. R043 correctly fell back to PQS, but the
HF2 persistent ENV4 producer identity still declared Kerbin as
`EXACT_CPU_ALL_PERSISTENT_OWNERS_V2`. AERIS49 then correctly refused to persist the
PQS tile under the Exact CPU environment and paused the preload plan.

The bug was therefore a contract mismatch between:

1. runtime Exact CPU availability, and
2. persistent ENV4 producer identity.

The individual fail-closed mechanisms were correct.

## Hotfix contract

- Live PQS topology remains excluded from persistent HF2 identity.
- The live topology shadow hash is used only as a trigger to re-run the accepted runtime
  Exact CPU certificate.
- If certification succeeds, the existing persistent policy remains
  `EXACT_CPU_ALL_PERSISTENT_OWNERS_V2`.
- If certification fails, the body is pinned for the current process to
  `PQS_RUNTIME_FALLBACK_V1`.
- The effective producer policy participates in the existing HF2 body fingerprint, so a
  fallback creates a body-local environment transition without deleting the old DB.
- Any tile that completes under the previous environment after a producer transition is
  dropped before persistent write and must be regenerated under the live environment.
- Exact CPU production failures discovered after enqueue also register the same runtime
  PQS fallback identity.
- Runtime fallback is sticky for the process to prevent producer oscillation caused by
  late-attached startup topology.
- Flight-control authority is unchanged.
- LAND control authority remains NONE / PILOT.

## Modified source

- `AERISTerrainTileSystem.cs`
- `AERISR043PreloadPtcPipeline.cs`
- `AERISR047ExactCpuProduction.cs`
- `AERISTerrainBlockPipeline.cs`
- `AERISTerrainPreloadBuilder.cs`

## Expected Kerbin runtime with the runway mods

The accepted Exact CPU certificate is expected to reject the extra Kerbin height chain.
The correct result is then:

```
effective producer = PQS_RUNTIME_FALLBACK_V1
body-local ENV4 transition = allowed
old Exact CPU environment DB = preserved
new PQS fallback DB writes = allowed only under fallback identity
ENV4_DB_WRITE_SUPPRESSED for Kerbin = 0 after transition
preload plan pause caused by producer mismatch = no recurrence
```

A pre-hotfix persisted pause is intentionally not auto-cleared because the existing state
format cannot distinguish a user-requested pause from the historical internal safety pause.
If Kerbin is still paused after installing the hotfix, resume it once explicitly.

## Runtime audit

Use:

```bash
bash Tools/aeris_current_stage.sh "<KSP root>"
```

First invocation builds/installs and arms the log offset. Launch KSP to the main menu with
the runway mods enabled, wait for preload status to settle, exit normally, then run the
same command again.

Candidate acceptance requires:

- Kerbin runtime producer fallback observed.
- Kerbin ENV4 observed with `producer_policy=PQS_RUNTIME_FALLBACK_V1`.
- No new Kerbin `ENV4_DB_WRITE_SUPPRESSED` event.
- No AERIS54 exception/error evidence.
- Preload progress resumes after an explicit one-time Resume if the pre-hotfix state was
  already persisted as paused.

## Runtime follow-up: coastline fast-path coherence

The first hotfix runtime proved the ENV4 producer transition itself, but exposed a second
coherence seam in AERIS51 C2. The coastline fast path selected Exact CPU from the static
candidate resolver only, ignoring the new runtime PQS fallback state. Kerbin therefore
entered `PQS_RUNTIME_FALLBACK_V1` correctly, completed FAR generation, then the coastline
phase repeatedly attempted an Exact-CPU snapshot, produced
`failure=EXACT_CPU_NOT_SELECTED`, paused the plan, and stopped with no work queued.

The follow-up fix makes the AERIS51 exact-coastline policy consult the effective runtime
producer. A body in runtime PQS fallback is treated as non-Exact for coastline generation
and therefore uses the already-accepted bounded PQS/block-pipeline path
(`path=LEGACY_BOUNDED`) instead of pausing.

Candidate runtime acceptance additionally requires zero Kerbin
`EXACT_CPU_NOT_SELECTED` coastline failures and at least one Kerbin legacy-bounded
high-density coastline queue event.


## Acceptance

Accepted runtime evidence:

- Candidate audit: `AERIS54_PRODUCER_COHERENCE_VERDICT=PASS_CANDIDATE`.
- Kerbin runtime fallback observed exactly once.
- Kerbin observed with `producer_policy=PQS_RUNTIME_FALLBACK_V1`.
- New Kerbin `ENV4_DB_WRITE_SUPPRESSED` events: 0.
- Kerbin coastline `EXACT_CPU_NOT_SELECTED` failures: 0.
- Kerbin coastline legacy bounded queues: 1635.
- Suspected AERIS54 exceptions: 0.
- Final Kerbin coastline completion:
  `event=COMPLETE; processed=6728; total=6728; elapsed_s=421.497`.
- Final runtime DLL SHA256:
  `8e888eeb29861b387ebd141bd00b86106c0d90b1d35a24828f7dcf7defa90a52`.

Acceptance conclusion:

```
producer-coherence hotfix = ACCEPTED
persistent HF2 live-topology exclusion = PRESERVED
runtime fallback identity = PQS_RUNTIME_FALLBACK_V1
old Exact CPU environment DB = PRESERVED
stale producer-environment writes = FORBIDDEN
Kerbin high-density coastline = COMPLETE 6728/6728
LAND control authority = NONE / PILOT
LAND-R2 = UNBLOCKED
```
