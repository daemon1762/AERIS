# AERIS52 — PRELOAD COASTLINE C3 DYNAMIC ADMISSION

Base: AERIS51 C1+C2 accepted  
Base SHA: `f899e90a6de5b1f8c2b0a0733057b9c7dd666978`

## Purpose

C1 removed the second all-FAR discovery pass on uninterrupted cold rebuilds.
C2 moved certified 129x129 coastline generation to immutable Exact CPU workers.
C3 makes Exact coastline concurrency load-aware instead of using only a fixed worker-count cap.

## Authority

C3 changes admission count only.

It does not change:

- 129x129 coastline resolution
- Exact CPU producer authority
- terrain payload format
- C1 FAR classification
- C2 worker mathematics
- persistent commit semantics
- LAND/AP/AA/PROTECT/Ground Assist/ND control authority

## Admission range

`2 .. min(8, scheduler worker count)`

Startup begins at up to 4 jobs.

Downshift is immediate.
Recovery is one lane at a time with a 1.5 second hysteresis.

## Inputs

C3 uses existing measured runtime telemetry only:

- frame-time EMA
- ND main-thread EMA
- worker backlog flag
- GeneralCompute queue depth
- required-result completion depth
- scheduler queue-delay P95
- worker-time P95
- encode backlog
- SSD write backlog / active write count / observed write MiB/s

No CPU model-name tuning is used.

## Policy bands

- SEVERE_LOAD -> 2
- HIGH_LOAD -> up to 3
- MODERATE_LOAD -> up to 4
- MILD_LOAD -> up to 6
- COMFORTABLE -> up to min(8, worker count)

A deterministic policy self-test validates the 2/3/4/6/8 mapping before runtime evidence is accepted.

## Fallback coverage

C3 wraps both:

- C1 transient-index fast path
- accepted durable disk-scan fallback

Non-Exact bodies still retain the historical legacy two-tile high-density limit.

## Evidence

Markers:

```
[AERIS52][PRELOAD_COAST_C3]; event=POLICY_SELFTEST; pass=true
[AERIS52][PRELOAD_COAST_C3]; event=ADMISSION_CHANGE; ...
[AERIS52][PRELOAD_COAST_C3]; event=ADMISSION_STATE; ...
[AERIS52][PRELOAD_COAST_C3]; event=SUMMARY; ...
```

The AERIS51 C1/C2 runtime gates remain mandatory:
- transient index / disk-rescan bypass on cold rebuild
- Exact worker queue == commit
- zero snapshot failure
- zero worker failure
- zero ENV4 DB-write suppression

After C3 acceptance, resume LAND-R2.
