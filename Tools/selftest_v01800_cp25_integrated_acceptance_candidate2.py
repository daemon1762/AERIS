#!/usr/bin/env python3
import sys
sys.dont_write_bytecode = True
from v01700_testlib import (ROOT, SOURCE, CheckSuite, read, extract_method,
                            strip_csharp_comments_and_literals, text_sha256)

suite = CheckSuite("v0.18.0.0 CP2.5 Integrated Acceptance Candidate 2")
sync = read(SOURCE / "AA/SyncModuleControlSurface.cs")
build_version = read(SOURCE / "Properties/AERISBuildVersion.generated.cs")
build = read(ROOT / "build_ubuntu.sh")
version = read(ROOT / "GameData/AERISFlightControl/AERISFlightControl.version")
runner = read(ROOT / "Tools/run_v01800_cp25_acceptance.py")
contract = read(ROOT / "ACCEPTANCE_v0.18.0.0_CP2.5_INTEGRATED_ACCEPTANCE_CANDIDATE2.txt")
spec = read(ROOT / "Docs/CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_2_v0.18.0.0_ja.md")
test_card = read(ROOT / "Docs/ND_CP25_INTEGRATED_ACCEPTANCE_CANDIDATE_2_TEST_CARD_v0.18.0.0_ja.md")
evidence = read(ROOT / "Evidence/RUNTIME_DISCOVERY_AA_CONTROL_SURFACE_EVENT_LIFECYCLE_AERIS32_2026-07-29.txt")

suite.check("using System.Reflection;" in sync,
            "control-surface lifecycle fix has reflection support")
suite.check("using AERISFlightControl.Logging;" in sync,
            "lifecycle result is observable through the AERIS logger")
suite.check("public new void OnDestroy()" in sync,
            "non-virtual stock OnDestroy is explicitly redeclared as a Unity message")
ondestroy = extract_method(sync, "OnDestroy")
suite.check("CleanupStockControlSurfaceCallbacks(\"pre-destroy\")" in ondestroy,
            "callbacks are removed before stock destruction")
suite.check("base.OnDestroy();" in ondestroy,
            "stock ModuleControlSurface.OnDestroy is invoked")
suite.check("finally" in ondestroy and
            "CleanupStockControlSurfaceCallbacks(\"post-destroy\")" in ondestroy,
            "callbacks are removed again after stock destruction even on failure")
suite.check(ondestroy.count("base.OnDestroy();") == 1,
            "stock OnDestroy is called exactly once")

onstart = extract_method(sync, "OnStart")
suite.check("CleanupStockControlSurfaceCallbacks(\"pre-start\")" in onstart,
            "restart-safe cleanup precedes stock registration")
suite.check("base.OnStart(state);" in onstart,
            "stock OnStart remains active")
suite.check(onstart.index("CleanupStockControlSurfaceCallbacks") < onstart.index("base.OnStart(state)"),
            "pre-start cleanup occurs before stock callback registration")
suite.check("if (HighLogic.LoadedSceneIsFlight)" in onstart and "usesMirrorDeploy" in onstart,
            "frozen mirror-deploy initialization remains after stock start")

for event_name, handler_name in (
    ("GameEvents.onEditorPartEvent", "OnEditorPartEvent"),
    ("GameEvents.onPartActionUIShown", "OnPartActionUIShown"),
    ("GameEvents.onPartActionUIDismiss", "OnPartActionUIDismiss"),
    ("GameEvents.onVariantApplied", "onVariantApplied"),
    ("GameEvents.onVesselReferenceTransformSwitch", "onVesselReferenceTransformSwitch"),
):
    suite.check(event_name in sync and ('"' + handler_name + '"') in sync,
                "stock callback cleanup covers " + handler_name)

suite.check("FindInstanceMethod(typeof(ModuleControlSurface), handlerName)" in sync,
            "private handlers are resolved from the stock declaring type")
suite.check("FindSingleDelegateParameterMethod(eventSource.GetType(), \"Remove\")" in sync,
            "native GameEvents Remove(delegate) API is discovered")
suite.check("Delegate.CreateDelegate(delegateType, this, handler, false)" in sync,
            "stock private handler is rebound to the live subclass instance")
suite.check("remove.Invoke(eventSource, new object[] { callback })" in sync,
            "cleanup uses the event source's own removal API")
suite.check("lifecycleCleanupWarningLogged" in sync and
            "[AA/CONTROL_SURFACE_LIFECYCLE] cleanup incomplete" in sync,
            "reflection failures are contained and reported once")
suite.check("lifecycleCleanupActivationLogged" in sync and
            "[AA/CONTROL_SURFACE_LIFECYCLE] explicit stock callback cleanup active." in sync,
            "successful explicit cleanup is positively observable once")
suite.check("Harmony" not in strip_csharp_comments_and_literals(sync),
            "lifecycle fix introduces no Harmony dependency")

ctrl = extract_method(sync, "CtrlSurfaceUpdate")
suite.check(text_sha256(ctrl) == "a8fe50749807dec9eadf51f605e5c0e87f14112cb60ace4c9cb4b89487a7484c",
            "CtrlSurfaceUpdate body is byte-identical to Candidate 1")
# The helper extracts braces only; keep a second independently recorded signature of
# the declaration-plus-body region to catch changes around the override declaration.
ctrl_decl_start = sync.index("protected override void CtrlSurfaceUpdate(Vector3 vel)")
ctrl_decl = sync[ctrl_decl_start:ctrl_decl_start + len("protected override void CtrlSurfaceUpdate(Vector3 vel)")]
suite.check(text_sha256(ctrl_decl) == "eedb3c9cc4da53ce5ce7a8aa4aec5322402c97e180735e5bed8ed771bb3d9b0c",
            "control-surface override declaration remains frozen")

suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 2" in build_version and
            "AA CONTROL SURFACE EVENT LIFECYCLE HOTFIX 1" in build_version,
            "generated identity names Candidate 2 and lifecycle fix")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 1" in build_version and
            "EMPTY MANUAL CATEGORY UI HOTFIX 1" in build_version,
            "Candidate 1 identity remains in the compatibility chain")
suite.check("CP2.5 INTEGRATED ACCEPTANCE CANDIDATE 2" in build and
            "AA CONTROL SURFACE EVENT LIFECYCLE HOTFIX 1" in build,
            "Ubuntu build entrypoint generates Candidate 2 identity")
suite.check("Integrated Acceptance Candidate 2" in version and
            "AA Control Surface Event Lifecycle Hotfix 1" in version,
            "KSP metadata identifies Candidate 2")
suite.check("selftest_v01800_cp25_integrated_acceptance_candidate2.py" in runner,
            "full CP2.5 runner executes Candidate 2 regression")
suite.check("CANDIDATE 1" in contract and "Gate 1-4" in contract and
            "CONTROL-LAW FREEZE" in contract,
            "Candidate 2 contract preserves the integrated baseline and control-law freeze")
suite.check("23件" in spec and "onVesselReferenceTransformSwitch" in spec and
            "実機合格条件" in spec,
            "Japanese specification records the discovery and runtime closure target")
suite.check("最低3回" in test_card and "KSPCF:MemoryLeaks" in test_card and
            "操縦則非退行" in test_card,
            "runtime card covers repeated transitions, leak evidence and flight quality")
suite.check("total: 23" in evidence and
            "onVesselReferenceTransformSwitch: 20" in evidence and
            "onEditorPartEvent: 3" in evidence,
            "Candidate 1 discovery evidence is bundled")

code = strip_csharp_comments_and_literals(sync)
for forbidden in ("FlightInputHandler.state", "MainThrottle =", "RunwayMasterCorrection",
                  "CURATED_RUNWAY_GEODETIC_DEFAULTS", "CurrentBodyResidentCache",
                  "authorityLimiter =", "ctrlSurfaceRange =", "actuatorSpeed ="):
    suite.check(forbidden not in code,
                "lifecycle fix adds no " + forbidden + " authority/content")

suite.finish()
