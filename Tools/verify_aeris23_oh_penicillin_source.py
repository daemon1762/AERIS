#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

def require(condition, message):
    if not condition:
        raise SystemExit("[PENICILLIN VERIFY] FAIL: " + message)
    print("[PASS] " + message)

def active_count(text, line):
    target = line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip() == target)

affine = ROOT / "Tools/verify_aeris23_witness_affine_source.py"
stagger_test = ROOT / "Tools/selftest_v01800_operation_health_staggered_exact_refresh.py"
for path in (affine, stagger_test):
    require(path.is_file(), "required inherited verifier exists: " + path.name)

subprocess.run([sys.executable, str(affine)], cwd=str(ROOT), check=True)
subprocess.run([sys.executable, str(stagger_test)], cwd=str(ROOT), check=True)

renderer = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
runtime = (ROOT / "Source/AERISFlightControl/Performance/AERISPerformanceRuntime.cs").read_text()
monitor_path = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
monitor = monitor_path.read_text()
csproj = (ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj").read_text()
build = (ROOT / "build_ubuntu.sh").read_text()
config = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()

require("StaggeredExactRefreshSlotCount = 12" in renderer,
        "twelve-slot Stagger exact distribution retained")
require("StaggeredExactRefreshMinimumSeconds = 2.80f" in renderer and
        "StaggeredExactRefreshSlotSeconds = 0.10f" in renderer,
        "2.80-3.90 second Stagger window retained")
require("AffineWitnessMaximumAgeSeconds = 4.00f" in renderer,
        "4.00 second hard freshness rail retained")
require("AffineWitnessAcceptancePixels = 0.08f" in renderer,
        "0.08 px affine witness rail retained")
require("nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f" in renderer,
        "fixed 10 Hz presentation authority retained")
require("RenderTextureFormat.ARGB32" in renderer and "FilterMode.Bilinear" in renderer,
        "ARGB32/Bilinear visual authority retained")
require("oh_stagger_back_peak=" in renderer and
        "oh_stagger_back_samples=" in renderer and
        "oh_stagger_back_gt8=" in renderer,
        "Stagger burst telemetry retained")

require('internal const string Codename = "PENICILLIN";' in monitor,
        "PENICILLIN codename embedded in runtime monitor")
require('internal const string Revision = "OH_PHASE2_001";' in monitor,
        "Operation Health revision embedded")
require('internal const string Candidate = "AERIS23_OH_PENICILLIN";' in monitor,
        "PENICILLIN candidate identity embedded")
require("[KSPAddon(KSPAddon.Startup.Flight, false)]" in monitor,
        "monitor is Flight-scene scoped")
require("AERISOperationHealthLogLevel" in monitor and
        "Off = 0" in monitor and "Normal = 1" in monitor and
        "Diagnostic = 2" in monitor and "Trace = 3" in monitor,
        "OFF/NORMAL/DIAGNOSTIC/TRACE logging levels exist")
require("if (logLevel == AERISOperationHealthLogLevel.Off || destroyed)" in monitor,
        "OFF mode has an early zero-work callback path")
require("GC.CollectionCount(0)" in monitor and "GC.CollectionCount(1)" in monitor and
        "GC.CollectionCount(2)" in monitor, "generation GC events are observed")
require("Time.fixedDeltaTime" in monitor and "fixedSimSecondsWindow" in monitor and
        "physics_ratio_1s=" in monitor and "physics_ratio_5s=" in monitor,
        "physics realtime integrity is measured at 1s and 5s")
require("fixed_catchup_frames=" in monitor and "fixed_catchup_peak=" in monitor and
        "fixed_gap_max_ms=" in monitor,
        "FixedUpdate catch-up and wall-gap metrics are published")
require("frame_p95_ms=" in monitor and "frame_max_ms=" in monitor and
        "frame_hitch=" in monitor, "frame health p95/max/hitch metrics are published")
require("collision_nd_frame=" in monitor and "collision_nd_physics=" in monitor and
        "collision_nd_gc=" in monitor, "heavy-work collision telemetry is published")
require("aeris_known_main_max_ms=" in monitor and "attribution=PARTIAL_PASSIVE" in monitor,
        "AERIS main attribution is explicitly partial/passive")
require("oh_self_max_ms=" in monitor and "oh_self_ema_ms=" in monitor,
        "monitor measures its own overhead")
require("lastFrameGapMs >= frameHitchThresholdMs" in monitor and
        "deferredSummaryCount++" in monitor,
        "diagnostic summary emission defers on hitch frames")
require("AERISLogger.Info(\"[OH] codename=\" + Codename" in monitor,
        "Operation Health aggregate logging uses the existing async logger")

require("AERISOperationHealthPenicillin.RecordRuntimeFrame(" in runtime,
        "Performance runtime exports existing timing values to PENICILLIN")
require(renderer.count("AERISOperationHealthPenicillin.RecordNavigationDisplayBack(") == 1,
        "ND BACK exports exactly one passive timing notification")
require("penicillinBackMilliseconds" in renderer and "penicillinExactThisBack" in renderer,
        "ND timing and exact burst counts are correlated")
require('Performance\\AERISOperationHealthPenicillin.cs' in csproj,
        "PENICILLIN monitor is compiled")

require("codename = PENICILLIN" in config and "logLevel = DIAGNOSTIC" in config and
        "summaryIntervalSeconds = 1.0" in config,
        "default diagnostic config is packaged")
require(build.count("AERISOperationHealth.cfg") >= 2,
        "user Operation Health config is preserved across source installs")

pen='CANDIDATE_NAME="AERIS23_OH_PENICILLIN"'
stagger='CANDIDATE_NAME="AERIS23_AFFINE_STAGGERED_EXACT_REFRESH"'
pen_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_oh_penicillin_candidate.py"'
stagger_verify='PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_staggered_exact_refresh_source.py"'
require(active_count(build,pen)==1,
        "build executable candidate identity is exactly AERIS23_OH_PENICILLIN")
require(active_count(build,stagger)==0,
        "no executable stale Stagger candidate identity remains")
require(active_count(build,pen_verify)==1,
        "build preflight requires final calibrated PENICILLIN verifier")
require(active_count(build,stagger_verify)==0,
        "no executable stale Stagger verifier remains")

for forbidden_token in (
    "AERISFlightControl.Autopilot",
    "AtmosphereAutopilot",
    "FlightCtrlState",
    "OnFlyByWire",
    "ctrlState.",
    ".pitch =",
    ".roll =",
    ".yaw =",
):
    require(forbidden_token not in monitor,
            "PENICILLIN monitor does not reference control token: " + forbidden_token)

diff = subprocess.run(
    ["git", "diff", "--name-only"],
    cwd=str(ROOT), check=True, text=True, stdout=subprocess.PIPE
).stdout.splitlines()
forbidden_prefixes = (
    "Source/AERISFlightControl/AA/",
    "Source/AERISFlightControl/Autopilot/",
    "Source/AERISFlightControl/Protect/",
    "Source/AERISFlightControl/Landing/",
)
violations = [name for name in diff if any(name.startswith(prefix) for prefix in forbidden_prefixes)]
require(not violations,
        "AA/AP/PROTECT/LAND source has no PENICILLIN working-tree edits" +
        ("" if not violations else ": " + ", ".join(violations)))

print("[AERIS23 CANDIDATE VERIFY] OH_PENICILLIN SOURCE+SAFETY PASS")
