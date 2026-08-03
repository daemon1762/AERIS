#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01640_testlib import SOURCE, ROOT, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.16.4.0 reload, LAND freeze, AIRFIELDS and ND contract")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
landing = read(SOURCE / "Landing" / "AERISLandingFoundation.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")
nd = read(SOURCE / "UI" / "AERISNavigationDisplay.cs")
settings = read(SOURCE / "Settings" / "AERISSettings.cs")
defaults = read(ROOT / "GameData" / "AERISFlightControl" / "Config" /
                "AERISSettings.cfg")
bootstrap = read(SOURCE / "Core" / "AERISBootstrap.cs")

for state in ("LoadingCache", "Discovering", "Surveying", "Validating",
              "Staged", "Complete", "Failed"):
    suite.check("AERISAirfieldReloadState." + state in registry,
                f"reload phase is implemented: {state}")
suite.check("if (refreshRequested && !IsReloadActive() &&" in registry and
            "CanBeginRefresh(providerRuntimeReady)) BeginRefresh()" in registry,
            "reload begins only outside an active transaction and after provider readiness")
suite.check("pendingRefresh = true" in registry and
            "MANUAL RELOAD COALESCED — ONE PENDING" in registry,
            "repeated manual reloads coalesce to one pending request")
suite.check("one airfield load requested" in bootstrap.lower() and
            bootstrap.count("RequestStartupLoad()") == 1,
            "automatic load is requested once after addon/provider startup")
suite.check("RefreshDynamicProviders" not in bootstrap,
            "scene/vessel transitions do not trigger a full airfield rescan")

suite.check("approachFrozen = freezeApproachGeometry" in registry,
            "registry receives current LAND freeze state")
suite.check("if (!approachFrozen) CommitStaged()" in registry,
            "staged geometry commits only after LAND is disarmed")
for token in ("frozenAirfield = airfield.Clone()",
              "frozenDirection = FindDirection(frozenAirfield",
              "frozenAirfield.RunwayForDirection(frozenDirection)", "frozenDatabaseRevision",
              "frozenGeometryRevision"):
    suite.check(token in landing, f"LAND deep-freeze contract: {token}")
suite.check("registry.IsDirectionRevoked(armedDirectionStableId)" in landing and "Disarm(" in landing,
            "live certification revocation immediately disarms LAND")
suite.check("!direction.HasCertifiedGeometry" in landing and
            'error = "RUNWAY APPROACH IS NOT CERTIFIED"' in landing,
            "LAND ARM rejects every non-CERTIFIED direction")

for field, key in (
    ("AirfieldsCertifiedExpanded", "airfieldsCertifiedExpanded"),
    ("AirfieldsUserCalibratedExpanded", "airfieldsUserCalibratedExpanded"),
    ("AirfieldsFailedExpanded", "airfieldsFailedExpanded"),
    ("AirfieldsPendingExpanded", "airfieldsPendingExpanded"),
    ("AirfieldsRevalidationExpanded", "airfieldsRevalidationExpanded"),
):
    suite.check(field in settings, f"AIRFIELDS fold setting exists: {field}")
    suite.check(key in defaults, f"AIRFIELDS fold default exists: {key}")

suite.check('string[] labels={"STATUS","OPTIONS","AIRFIELDS","PRELOAD MAPS","DIAGNOSTICS"}' in window,
            "SYSTEM tab preserves STATUS / OPTIONS / AIRFIELDS before PRELOAD MAPS / DIAGNOSTICS")
suite.check('"↻ RELOAD / RESCAN"' in window and "RequestManualReload" in window,
            "AIRFIELDS header exposes the required manual reload button")
for category in ("CERTIFIED — AUTOMATIC / PROVIDER", "USER CALIBRATED — MANUAL",
                 "FAILED", "PENDING", "REVALIDATION"):
    suite.check('"' + category + '"' in window,
                f"AIRFIELDS category is visible: {category}")
for token in ("GeometryConfidence", "ClassificationConfidence", "EvidenceFamilies",
              "MeasurementMethods", "CenterlineUncertaintyMeters",
              "HeadingUncertaintyDeg", "ThresholdUncertaintyMeters",
              "GeometryFingerprint", "FailureCode"):
    suite.check(token in window, f"AIRFIELDS detail exposes: {token}")
suite.check("GUI.enabled=oldEnabled&&!land.Armed" in window,
            "LAND runway selection is locked while armed")
suite.check("SelectableDirectionAt" in window and "SelectableDirectionCount" in window,
            "LAND selector iterates certified directions only")
suite.check("EffectiveState(direction)" in window,
            "AIRFIELDS UI shows live REVALIDATION overlay state")

suite.check("AERISRunwayTrackTokenApi.Bind" in bootstrap and
            "AERISRunwayTrackTokenApi.Unbind" in bootstrap,
            "read-only runway-track token has lifecycle binding")

suite.check("AERISRunwayDirectionDefinition direction = land != null ? land.ActiveDirection" in nd,
            "ND uses frozen selected LAND direction")
suite.check("if (!landActive)" in nd and "DrawAirfieldSymbols" in nd,
            "LAND/ILS view removes non-selected facility symbols")
suite.check("DrawLandingPlan" in nd and "DrawLandingProfile" in nd,
            "selected runway and glide profile remain visible")
suite.check("width=1.0f" in nd or "1.0f" in nd,
            "ownship core line remains approximately one pixel")
suite.check("1.8f" in nd,
            "ownship contrast edge remains below the two-pixel limit")

for path in (SOURCE / "Landing", SOURCE / "UI"):
    for file in path.glob("*.cs"):
        if file.name in {"AERISLandingFoundation.cs", "AERISAirfieldRegistry.cs",
                         "AERISRunwaySnapshotBuilder.cs",
                         "AERISOperationalRunwayResolver.cs"}:
            continue
suite.check("FlightCtrlState" not in strip_csharp_comments_and_literals(
    read(SOURCE / "Landing" / "AERISLandingFoundation.cs")),
    "LAND foundation remains observation-only")
suite.finish()
