#!/usr/bin/env python3
"""Regression model for startup provider stability, cache root compatibility and archive drain."""
from __future__ import annotations

import sys
sys.dont_write_bytecode = True

from v01700_testlib import SOURCE, CheckSuite, read


def can_begin_refresh(provider_runtime_ready: bool) -> bool:
    return provider_runtime_ready




def provider_gate_step(state: tuple[str | None, float], in_flight: bool,
                       vessel: str | None, packed: bool, now: float) -> tuple[tuple[str | None, float], bool]:
    tracked, since = state
    ready_sample = in_flight and vessel is not None and not packed
    if not ready_sample:
        return (None, -1.0), False
    if tracked != vessel:
        return (vessel, now), False
    if since < 0.0:
        return (vessel, now), False
    return (vessel, since), now - since >= 1.5

def resolve_cache_root(loaded: dict | None) -> dict | None:
    if loaded is None:
        return None
    if loaded.get("name", "").lower() == "aerisairfieldcertificationcache":
        return loaded
    named = loaded.get("AERISAirfieldCertificationCache")
    if isinstance(named, dict):
        return named
    return loaded if loaded.get("schemaVersion") not in (None, "") else None


def archive_paused(overloaded: bool, land_active: bool,
                   archive_drain_preferred: bool) -> bool:
    paused = overloaded or land_active
    if archive_drain_preferred and not land_active:
        paused = False
    return paused


suite = CheckSuite("v0.17.0.3 startup/cache/archive field-hotfix regression")

suite.check(not can_begin_refresh(False),
            "startup/manual refresh waits while flight providers are not stable")
suite.check(can_begin_refresh(True),
            "refresh begins once the unpacked flight provider runtime is stable")

state, ready = provider_gate_step((None, -1.0), True, "A", False, 10.0)
suite.check(not ready and state == ("A", 10.0),
            "provider gate starts a timer for the current unpacked vessel")
state, ready = provider_gate_step(state, True, "A", False, 11.6)
suite.check(ready, "provider gate opens after the same vessel is stable for 1.5 seconds")
state, ready = provider_gate_step(state, True, "B", False, 11.7)
suite.check(not ready and state == ("B", 11.7),
            "active-vessel replacement resets provider stability")

named = {"name": "AERISAirfieldCertificationCache", "schemaVersion": "3"}
wrapped = {"name": "ROOT", "AERISAirfieldCertificationCache": named}
direct = {"name": "ROOT", "schemaVersion": "3", "Record": []}
missing = {"name": "ROOT"}
suite.equal(resolve_cache_root(named), named, "named cache root is accepted")
suite.equal(resolve_cache_root(wrapped), named, "wrapped named cache root is accepted")
suite.equal(resolve_cache_root(direct), direct,
            "legacy ConfigNode.Save direct-root cache is accepted")
suite.equal(resolve_cache_root(missing), None, "unrelated/malformed cache remains rejected")

suite.check(not archive_paused(True, False, True),
            "scene-idle archive drain overrides stale scene-transition backoff")
suite.check(archive_paused(True, True, True),
            "LAND activity always keeps archive compression paused")
suite.check(archive_paused(True, False, False),
            "in-flight severe backoff still pauses archive compression")

cache = read(SOURCE / "Landing" / "AERISRunwayCertificationCache.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")
runtime = read(SOURCE / "Performance" / "AERISPerformanceRuntime.cs")
scheduler = read(SOURCE / "Performance" / "AERISWorkerScheduler.cs")
archive = read(SOURCE / "Recording" / "AERISFlightDataArchive.cs")

for token in (
    'const string CacheNodeName = "AERISAirfieldCertificationCache"',
    "ResolveCacheRoot(loaded)",
    "ResolveCacheRoot(verify)",
    'loaded.GetValue("schemaVersion")',
    'GetNodes("Record").Length != entries.Count',
    'GetNodes("FailureRecord").Length != failures.Count',
    "TryLoadFile(temporary, roundTripEntries, roundTripFailures",
    "roundTripEntries.Count != entries.Count",
    "roundTripFailures.Count != failures.Count",
    "[AIRFIELD_CACHE] load accepted",
    "[AIRFIELD_CACHE] save verified",
):
    suite.check(token in cache, "cache compatibility/integrity contract: " + token)

for token in (
    "Tick(bool freezeApproachGeometry, bool providerRuntimeReady)",
    "CanBeginRefresh(providerRuntimeReady)",
    'WAITING FOR STABLE FLIGHT PROVIDERS',
    'AIRFIELD_PROVIDER_SNAPSHOT',
    'ProviderSnapshotSignature(stagedRecords',
    "if (refreshRequested || IsReloadActive())",
    "if (!IsReloadActive() && !refreshRequested && pendingRefresh)",
):
    suite.check(token in registry, "provider-stable reload contract: " + token)

for token in (
    "airfieldProviderReadySince=-1f",
    "airfieldProviderReadyVessel",
    "inFlight&&vessel!=null&&!vessel.packed",
    "airfieldProviderReadyVessel!=vessel",
    "now-airfieldProviderReadySince>=1.5f",
    "Airfields.Tick(Landing!=null&&Landing.Armed,airfieldProvidersReady)",
    "Performance.Tick(now,Mathf.Max(0f,Time.unscaledDeltaTime)*1000.0,snapshotCost,Landing!=null&&Landing.Armed,inFlight)",
):
    suite.check(token in bootstrap, "bootstrap readiness/scene contract: " + token)

for token in (
    "bool landActive, bool inFlight",
    "!inFlight &&",
    "AERISFlightDataArchive.PendingCount > 0",
    "archiveDrainPreferred",
):
    suite.check(token in runtime, "runtime archive-drain contract: " + token)

for token in (
    "bool landActive, bool archiveDrainPreferred",
    "SCENE-IDLE ARCHIVE DRAIN",
    "if (archiveDrainPreferred && !landActive)",
    "archivePaused = false",
):
    suite.check(token in scheduler, "permit-controller archive contract: " + token)

for token in (
    "[FDR][ARCHIVE] queued",
    "[FDR][ARCHIVE] scheduler accepted",
    "ZIP verified",
):
    suite.check(token in archive, "archive lifecycle evidence: " + token)

suite.finish()
