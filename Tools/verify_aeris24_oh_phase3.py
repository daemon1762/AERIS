#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
OLD = "PENI" + "CILLIN"
NEW = "EPI" + "NEPHRINE"
CANDIDATE = "AERIS24_GPU_VERTEX_PROJECTION_POC"
M = (ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs").read_text()
C = (ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg").read_text()
B = (ROOT / "build_ubuntu.sh").read_text()
R = (ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs").read_text()
W = (ROOT / "Source/AERISFlightControl/UI/AERISWindow.cs").read_text()
U = (ROOT / "Source/AERISFlightControl/UI/AERISNavigationDisplay.cs").read_text()
checks = [
    (('internal const string Codename = "' + NEW + '";') in M, "Phase 3 codename"),
    ('internal const string Revision = "OH_PHASE3_007";' in M, "Phase 3 warm visibility suspend revision"),
    (('internal const string Candidate = "' + CANDIDATE + '";') in M, "stable technical candidate"),
    (("    codename = " + NEW) in C and ("    codename = " + OLD) not in C, "packaged config codename"),
    (('CANDIDATE_NAME="' + CANDIDATE + '"') in B, "build candidate"),
    (("OPERATION HEALTH PHASE 3 " + NEW + " GPU VERTEX PROJECTION") in B, "build display"),
    ('oh_gpu_vertex_requested=' in R and 'oh_gpu_vertex_projection=' in R and
     'oh_gpu_vertex_exact_bypass=' in R and 'oh_nd_reload=' in R and
     'oh_nd_reload_snapshot=' in R and 'oh_gpu_vertex_resident_suspend=' in R and
     'oh_nd_warm_suspend_count=' in R and 'oh_nd_warm_prune_removed=' in R,
     "GPU + black reload + warm visibility telemetry"),
    ('SuspendVisibilityWarm' in R and 'ResumeVisibilityWarm' in R and
     'PruneWarmResume' in R and 'SuspendVisibilityWarm();' in U,
     "warm visibility successor is generated"),
    ('DrawProjectionBackendSelector' in W and 'GUILayout.HorizontalSlider' in W and
     'DrawTerrainGpuSelector' not in W,
     "SYSTEM options UI successor is generated"),
    ('AERISSettings.cfg NavigationDisplayProfiles.cfg AERISOperationHealth.cfg' in B and
     'OH_CONFIG="$TARGET/Config/AERISOperationHealth.cfg"' in B and
     ("r'\\1" + NEW + "'") in B,
     "preserved Operation Health policy promotes package-owned codename"),
]
failed = []
for ok, label in checks:
    print(("[PASS] " if ok else "[FAIL] ") + label)
    if not ok: failed.append(label)
print("[AERIS24 OH PHASE3] %d/%d PASS" % (len(checks)-len(failed), len(checks)))
if failed: raise SystemExit("; ".join(failed))
