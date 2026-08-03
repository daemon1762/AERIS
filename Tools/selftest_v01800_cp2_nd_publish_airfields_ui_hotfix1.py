#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import ROOT, SOURCE, CheckSuite, read, strip_csharp_comments_and_literals

suite = CheckSuite("v0.18.0.0 CP2 ND publish and airfields UI collapse hotfix 1")
nd = read(SOURCE / "UI/AERISNavigationDisplay.cs")
window = read(SOURCE / "UI/AERISWindow.cs")
settings = read(SOURCE / "Settings/AERISSettings.cs")
performance = read(SOURCE / "Performance/AERISPerformanceRuntime.cs")
build = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
runner = read(ROOT / "Tools/run_v01800_cp2_acceptance.py")
default_cfg = read(ROOT / "GameData/AERISFlightControl/Config/AERISSettings.cfg")

# ND regression: the main-thread capture must actually enter the shared worker pipeline.
suite.check("new AERISNavigationDisplaySnapshot" in nd,
            "ND capture creates an immutable navigation snapshot")
suite.check("Generation = core.Performance.CaptureStamp()" in nd,
            "ND snapshot carries the current runtime generation")
suite.check("DatabaseRevision = registry.DatabaseRevision" in nd and
            "SelectionRevision = registry.SelectionRevision" in nd,
            "ND snapshot carries database and selection revisions")
suite.check("Runways = runwaySources.ToArray()" in nd and
            "Facilities = facilitySources.ToArray()" in nd,
            "captured runways and facilities are included in the snapshot")
suite.check("core.Performance.SubmitNavigationDisplay(snapshot)" in nd,
            "ND snapshot is submitted to the shared preprocessing pipeline")
suite.check("internal bool SubmitNavigationDisplay" in performance and
            "navigationDisplay.Submit(snapshot)" in performance,
            "performance runtime exposes the expected worker submission path")
submit = nd.index("bool submitted = core.Performance.SubmitNavigationDisplay(snapshot);")
commit = nd.index("capturedDatabaseRevision = registry.DatabaseRevision;", submit)
retry = nd.index("nextNavigationCaptureRealtime = now + 0.5f;", submit)
suite.check(submit < commit and submit < retry,
            "capture revision is committed only after the submit attempt")
suite.check("if (submitted)" in nd[submit:commit + 200],
            "successful submission gates capture revision commit")
suite.check("Do not mark an unpublished database revision as captured" in nd,
            "source documents the stale-frame disappearance failure boundary")
suite.check("nextNavigationCaptureRealtime = now + 10f;" in nd and
            "nextNavigationCaptureRealtime = now + 0.5f;" in nd,
            "accepted captures use normal cadence while rejected captures retry quickly")

# Airfield list presentation: closed by default, migrated once, and wrapped.
suite.check("CurrentAirfieldsUiLayoutRevision = 2" in settings,
            "airfields UI has an explicit one-time layout migration revision")
for field in ("AirfieldsCertifiedExpanded", "AirfieldsUserCalibratedExpanded", "AirfieldsFailedExpanded",
              "AirfieldsPendingExpanded", "AirfieldsRevalidationExpanded",
              "AirfieldsProvisionalExpanded"):
    declaration = "internal bool " + field + ";"
    suite.check(declaration in settings,
                field + " has a closed default declaration")
suite.check('"airfieldsUiLayoutRevision", 0' in settings and
            "airfieldsUiLayoutRevision < CurrentAirfieldsUiLayoutRevision" in settings,
            "old settings files enter the one-time collapse migration")
for token in ("settings.AirfieldsCertifiedExpanded = false;",
              "settings.AirfieldsUserCalibratedExpanded = false;",
              "settings.AirfieldsFailedExpanded = false;",
              "settings.AirfieldsPendingExpanded = false;",
              "settings.AirfieldsRevalidationExpanded = false;",
              "settings.AirfieldsProvisionalExpanded = false;"):
    suite.check(token in settings, "migration/reset closes " + token.split('.')[1].split()[0])
suite.check('node.AddValue("airfieldsUiLayoutRevision", AirfieldsUiLayoutRevision)' in settings,
            "airfields UI migration revision is persisted")
suite.check("if (saveSettingsMigration) settings.Save();" in settings,
            "existing installs persist the collapsed migration immediately")
suite.check("airfieldsUiLayoutRevision = 2" in default_cfg,
            "shipped default config identifies the collapsed airfields layout")
for key in ("airfieldsCertifiedExpanded", "airfieldsUserCalibratedExpanded", "airfieldsFailedExpanded",
            "airfieldsPendingExpanded", "airfieldsRevalidationExpanded",
            "airfieldsProvisionalExpanded"):
    suite.check((key + " = False") in default_cfg,
                "shipped default config closes " + key)

suite.check('UiBuildTitle("FLIGHT CONTROL")' in window and
            'UiBuildTitle("PRELOAD TERRAIN CONTROL")' in window,
            "compact UI headings replace the unbounded full build identity")
suite.check("AERISBuildVersion.Display+\" — Flight Control\"" not in window and
            "AERISBuildVersion.Display+\" — PRELOAD TERRAIN STATUS\"" not in window,
            "full hotfix history is no longer rendered into the compact window")
suite.check('return "AERIS v"+AERISBuildVersion.Semantic+" "+AERISBuildVersion.UiCheckpoint+" — "+suffix;' in window and
            'DEV CP2 — "+suffix' not in window,
            "compact heading retains semantic version and current checkpoint identity")
suite.check("wrappedLabelStyle.wordWrap=true" in window and
            "airfieldRowButtonStyle.wordWrap=true" in window and
            "airfieldActionButtonStyle.wordWrap=false" in window,
            "AIRFIELD text labels/selection rows can wrap while action buttons are fixed")
suite.check('string row=airfield.DisplayName+"\\n"+designation' in window,
            "airfield row separates identity from grouped runway geometry/status on two lines")
suite.check("WrappedControlHeight(airfieldRowButtonStyle" in window and
            "WrappedControlHeight(airfieldActionButtonStyle" not in window,
            "only AIRFIELD selection rows retain variable wrapped height")
suite.check("WrappedAirfieldLabel(registry.UserRunwayCalibrationSummary(airfield))" in window and
            "WrappedAirfieldLabel(\"CALIBRATION ACTION: \"+runwayCalibrationMessage)" in window,
            "remaining AIRFIELD calibration messages use wrapped layout")
suite.check("BeginScrollView(airfieldsScroll,false,true" in window,
            "airfield list explicitly disables horizontal scrolling")

suite.check("Kola" not in strip_csharp_comments_and_literals(window) and
            "Kola" not in strip_csharp_comments_and_literals(nd),
            "ND and UI fixes remain airport-agnostic")
suite.check("ND NAVIGATION SNAPSHOT PUBLISH AIRFIELDS UI COLLAPSE HOTFIX 1" in build,
            "build identity names the ND publish and airfields UI hotfix")
suite.check("selftest_v01800_cp2_nd_publish_airfields_ui_hotfix1.py" in runner,
            "full CP2 acceptance includes this regression test")
for rel in (
        "Docs/CP2_ND_NAVIGATION_SNAPSHOT_PUBLISH_AIRFIELDS_UI_COLLAPSE_HOTFIX_1_v0.18.0.0_ja.md",
        "Docs/ND_CP2_ND_NAVIGATION_SNAPSHOT_PUBLISH_AIRFIELDS_UI_COLLAPSE_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
        "Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl20_ND_DISAPPEAR_AIRFIELDS_UI.txt",
        "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_ND_PUBLISH_AIRFIELDS_UI_COLLAPSE_HOTFIX1.txt"):
    suite.check((ROOT / rel).is_file(), "current hotfix evidence exists: " + rel)
suite.finish()
