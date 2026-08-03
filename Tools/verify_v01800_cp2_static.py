#!/usr/bin/env python3
import json, re, sys
sys.dont_write_bytecode = True
from pathlib import Path
from v01700_testlib import ROOT, CheckSuite, read, sha256, strip_csharp_comments_and_literals
suite=CheckSuite("v0.18.0.0 CP2 static package verification")
data=json.loads((ROOT/"GameData/AERISFlightControl/AERISFlightControl.version").read_text())
v=data["VERSION"]
semver="%d.%d.%d.%d"%(v["MAJOR"],v["MINOR"],v["PATCH"],v.get("BUILD",0))
suite.equal(semver,"0.18.0.0","VERSION is v0.18.0.0")
generated=read(ROOT/"Source/AERISFlightControl/Properties/AERISBuildVersion.generated.cs")
suite.check('Cp2FrozenBaselineDisplay = \"AERIS Flight Control v0.18.0.0 DEV CP2 KK RUNWAY ABSOLUTE REGISTRATION HOTFIX 1 PRELOAD FAST PATH 1 AXIS REGISTRATION HOTFIX 1 COMPILE HOTFIX 1 MOD AIRFIELD RECOVERY HOTFIX 1 AUTO PRELOAD PROGRESSION 1 COMPILE HOTFIX 1 AXIS REFERENCE HOTFIX 2 RUNWAY WITNESS ANCHOR SCAN CALIBRATION HOTFIX 3 GENERIC RUNWAY PLACEMENT VERIFICATION MANUAL CALIBRATION FINAL CANDIDATE 3 COMPILE HOTFIX 1 CALIBRATION ROUND-TRIP HOTFIX 1 BIDIRECTIONAL RUNWAY PAIR HOTFIX 1 ND NAVIGATION SNAPSHOT PUBLISH AIRFIELDS UI COLLAPSE HOTFIX 1 RESPONSIVE AIRFIELDS UI LAYOUT RESIZE HOTFIX 1 MANUAL CALIBRATED RUNWAY SEPARATION PRESERVATION HOTFIX 1 MANUAL CALIBRATION REFLECTION HOTFIX 1 MANUAL RUNWAY DESIGNATION GROUPING HOTFIX 1 MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1\"' in generated,
            "dedicated frozen CP2 display identifies absolute geodetic endpoint build entrypoint hotfix 1")
csproj=read(ROOT/"Source/AERISFlightControl/AERISFlightControl.csproj")
items=re.findall(r'<Compile Include="([^"]+)"',csproj)
missing=[x for x in items if not (ROOT/"Source/AERISFlightControl"/x.replace('\\','/')).is_file()]
suite.check(not missing,"every csproj Compile item exists",", ".join(missing))
for rel in (
 "Source/AERISFlightControl/Terrain/AERISTerrainTileContracts.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainTileCache.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainCoastlinePolicy.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainPerformance.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainAwareness.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainPreloadContracts.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainPreloadCodec.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainPreloadDatabase.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs",
 "Source/AERISFlightControl/Terrain/AERISTerrainBlockPipeline.cs",
 "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs",
 "Source/AERISFlightControl/UI/ToolbarBridge.cs",
 "Source/AERISFlightControl/UI/AERISWindow.cs",
 "Source/AERISFlightControl/UI/AERISFlightInstrument.cs",
 "Source/AERISFlightControl/Settings/AERISSettings.cs",
 "Source/AERISFlightControl/Settings/AERISNavigationDisplayProfileStore.cs",
 "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs",
 "Source/AERISFlightControl/Performance/AERISNavigationTrafficPipeline.cs",
 "Source/AERISFlightControl/Integrations/AERISWindProviderApi.cs"):
 clean=strip_csharp_comments_and_literals(read(ROOT/rel))
 suite.equal(clean.count('{'),clean.count('}'),rel+" brace balance")
 suite.equal(clean.count('('),clean.count(')'),rel+" parenthesis balance")
 suite.equal(clean.count('['),clean.count(']'),rel+" bracket balance")
bank=ROOT/"Source/AERISFlightControl/Autopilot/AERISBankDirector.cs"
suite.equal(sha256(bank),"bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7",
            "BANK source remains byte-identical")
all_source="\n".join(read(p) for p in (ROOT/"Source/AERISFlightControl").rglob("*.cs"))
for legacy in ("class AERISNavDirector","class AERISRouteSpeedPlanner",
               "class AERISTrajectoryPrimitives","TryStartNavLanding"):
 suite.check(legacy not in all_source,"legacy NAV remains absent: "+legacy)
landing=strip_csharp_comments_and_literals(read(ROOT/"Source/AERISFlightControl/Landing/AERISLandingFoundation.cs"))
for forbidden in ("FlightCtrlState","MainThrottle","wheelThrottle","wheelSteer",
                  "ApplyAaNative",".SetArmed("):
 suite.check(forbidden not in landing,"LAND foundation remains control-free: "+forbidden)
banned=[]
for p in ROOT.rglob('*'):
 if not p.is_file(): continue
 low=p.name.lower()
 if low.endswith(('.dll','.exe','.pdb','.pyc','.pyo')) or '__pycache__' in p.parts:
  banned.append(str(p.relative_to(ROOT)))
suite.check(not banned,"source package contains no binary/debug/python cache",", ".join(banned[:10]))
# No runtime cache or build output is allowed in the source checkpoint.
for rel in ("GameData/AERISFlightControl/PluginData/TerrainCache",
            "GameData/AERISFlightControl/PluginData/TerrainPreloadDatabase",
            "Source/AERISFlightControl/bin", "Source/AERISFlightControl/obj"):
 suite.check(not (ROOT/rel).exists(),"source package excludes runtime/build tree: "+rel)

for rel in (
 "README.md",
 "Docs/CP2_PRELOAD_TERRAIN_INTEGRATION_v0.18.0.0_ja.md",
 "Docs/CP2_PRELOAD_STATUS_TOOLBAR_v0.18.0.0_ja.md",
 "Docs/CP2_PRELOAD_STATUS_TOOLBAR_COMPILE_HOTFIX_1_v0.18.0.0_ja.md",
 "Docs/CP2_FIELD_RENDER_CONSISTENCY_HOTFIX_1_v0.18.0.0_ja.md",
 "Docs/RUNTIME_EVIDENCE_ANALYSIS_2026-07-24_ja.md",
 "Docs/CODE_AUDIT_v0.18.0.0_CP2_FIELD_RENDER_HOTFIX1_ja.md",
 "Docs/ND_CP2_PRELOAD_TERRAIN_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/DEVELOPMENT_CHECKPOINT_v0.18.0.0_CP2_ja.md",
 "Docs/HANDOVER_AERIS13_v0.18.0.0_CP2_FIELD_RENDER_HOTFIX1_ja.md",
 "Docs/ROADMAP_CURRENT_AERIS13_v0.18.0.0_ja.md",
 "Docs/NEXT_CHAT_START_HERE_ja.txt",
 "Evidence/AERIS13_INPUT_EVIDENCE_INDEX_2026-07-25.txt",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_FIELD_RENDER_HOTFIX1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_FIELD_RENDER_CONSISTENCY_HOTFIX1.txt",
 "Docs/CP2_ALIGNMENT_DIAGNOSTIC_HOTFIX_1_v0.18.0.0_ja.md",
 "Docs/ND_CP2_ALIGNMENT_DIAGNOSTIC_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_ALIGNMENT_DIAGNOSTIC_HOTFIX1_ja.md",
 "Evidence/AERIS14_VIDEO_LOG_REVIEW_2026-07-25.txt",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_ALIGNMENT_DIAGNOSTIC_HOTFIX1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_ALIGNMENT_DIAGNOSTIC_HOTFIX1.txt",
 "Docs/CP2_RUNWAY_TERRAIN_SAFETY_HOTFIX_1_v0.18.0.0_ja.md",
 "Docs/ND_CP2_RUNWAY_TERRAIN_SAFETY_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_RUNWAY_TERRAIN_SAFETY_HOTFIX1_ja.md",
 "Evidence/AERIS14_RUNWAY_TERRAIN_SAFETY_FINDINGS_2026-07-25.txt",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_RUNWAY_TERRAIN_SAFETY_HOTFIX1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_RUNWAY_TERRAIN_SAFETY_HOTFIX1.txt",
 "Docs/CP2_CLOSURE_CANDIDATE_1_v0.18.0.0_ja.md",
 "Docs/ND_CP2_RUNWAY_MAP_LOCK_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_CLOSURE_CANDIDATE1_ja.md",
 "Evidence/AERIS14_CP2_CLOSURE_FINDINGS_2026-07-25.txt",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_CLOSURE_CANDIDATE1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_CLOSURE_CANDIDATE1.txt",
 "Docs/CP2_RUNWAY_MAP_LOCK_HOTFIX_2_PRELOAD_FAST_PATH_1_v0.18.0.0_ja.md",
 "Docs/ND_CP2_RUNWAY_MAP_LOCK_HOTFIX_2_PRELOAD_FAST_PATH_1_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_RUNWAY_MAP_LOCK_HOTFIX2_PRELOAD_FASTPATH1_ja.md",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_RUNWAY_MAP_LOCK_HOTFIX2_PRELOAD_FASTPATH1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_RUNWAY_MAP_LOCK_HOTFIX2_PRELOAD_FASTPATH1.txt",
 "Docs/ND_CP2_RUNWAY_PRESENTATION_UI_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_RUNWAY_PRESENTATION_UI_HOTFIX1_ja.md",
 "Evidence/AERIS14_RUNWAY_PRESENTATION_UI_FINDINGS_2026-07-25.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_RUNWAY_PRESENTATION_UI_HOTFIX1.txt",
 "Docs/ND_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX1_ja.md",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_KK_RUNWAY_ABSOLUTE_REGISTRATION_HOTFIX1.txt",
 "Docs/CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX_1_v0.18.0.0_ja.md",
 "Docs/ND_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX1_ja.md",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_HOTFIX1.txt",
 "Docs/CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX_1_v0.18.0.0_ja.md",
 "Docs/ND_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX_1_TEST_CARD_v0.18.0.0_ja.md",
 "Docs/HANDOVER_AERIS14_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX1_ja.md",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX1.txt",
 "ACCEPTANCE_v0.18.0.0_CP2_KK_RUNWAY_AXIS_REGISTRATION_COMPILE_HOTFIX1.txt"):

 suite.check((ROOT/rel).is_file(), "current CP2 Preload document exists: "+rel)
for rel in (
 "00_README_ja.md",
 "01_FINAL_HANDOVER_ja.md",
 "02_RUNWAY_BY_RUNWAY_RESPONSE_ja.md",
 "03_ADAPTIVE_GLIDE_PATH_POLICY_ja.md",
 "04_FAILURE_CODE_RESPONSE_MAP_ja.md",
 "05_EVIDENCE_INTEGRITY_REPORT_ja.md",
 "06_POST_HOTFIX_FIELD_TEST_CARD_ja.md",
 "07_NEXT_CHAT_START_PROMPT_ja.txt",
 "08_SOURCE_AUDIT_LOCATOR_ja.md"):
 suite.check((ROOT/"Docs/AERIS13_Baseline"/rel).is_file(),
             "AERIS13 baseline document retained: "+rel)
for rel in (
 "ACCEPTANCE_v0.18.0.0_CP2_GENERIC_RUNWAY_PLACEMENT_VERIFICATION_FINAL_CANDIDATE3.txt",
 "Docs/CP2_GENERIC_RUNWAY_PLACEMENT_VERIFICATION_MANUAL_CALIBRATION_FINAL_CANDIDATE_3_v0.18.0.0_ja.md",
 "Docs/ND_CP2_GENERIC_RUNWAY_PLACEMENT_VERIFICATION_FINAL_TEST_CARD_v0.18.0.0_ja.md",
 "Evidence/RUNTIME_DIAGNOSIS_AERISFlightControl18_GENERIC_PLACEMENT_VERIFICATION.txt",
 "Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP2_GENERIC_RUNWAY_PLACEMENT_VERIFICATION_FINAL_CANDIDATE3.txt"):
 suite.check((ROOT/rel).is_file(), "current final-candidate record retained: "+rel)
readme=read(ROOT/"README.md")
test_card=read(ROOT/"Docs/ND_CP2_GENERIC_RUNWAY_PLACEMENT_VERIFICATION_FINAL_TEST_CARD_v0.18.0.0_ja.md")
suite.check("DEV CP2 — Absolute Geodetic Endpoint Authority Build Entrypoint Hotfix 1" in readme,
            "README identifies the current combined checkpoint")
suite.check("AERISFlightControl-v0.18.0.0_DEV_CP2_GenericRunwayPlacementVerification_FinalCandidate3_CompileHotfix1_Source.zip" in test_card,
            "test card uses the current package name")
suite.check("MANUAL RUNWAY ABSOLUTE GEODETIC ENDPOINT AUTHORITY HOTFIX 1 BUILD ENTRYPOINT HOTFIX 1" in read(ROOT/"build_ubuntu.sh"),
            "build entrypoint regenerates the current display string")
version_text=read(ROOT/"GameData/AERISFlightControl/AERISFlightControl.version")
suite.check("Manual Runway Absolute Geodetic Endpoint Authority Hotfix 1 / Build Entrypoint Hotfix 1" in version_text,
            "AVC metadata identifies the current checkpoint")
toolbar_doc=read(ROOT/"Docs/CP2_PRELOAD_STATUS_TOOLBAR_v0.18.0.0_ja.md")
for token in ("単一", "Main Menu", "Space Center", "VAB", "SPH", "Tracking Station",
              "読み取り専用", "onGUIApplicationLauncherReady", "onGUIApplicationLauncherDestroyed"):
 suite.check(token in toolbar_doc, "toolbar design documents scene/lifecycle contract: "+token)
suite.finish()
