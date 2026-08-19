#!/usr/bin/env python3
from pathlib import Path
import subprocess, sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
N = ROOT / 'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'
R012 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R012_COLD_START_PRELOAD_READY_RECOVERY'
PREFIX = '[AERIS28 REV3.5 R012 VERIFY]'

for path in (R, O, P, N, B, PRE):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text(); observer = O.read_text(); preload = P.read_text()
nav = N.read_text(); build = B.read_text(); prebuild = PRE.read_text()
checks = [
    (R010 in renderer, 'R010 formal rendering parent retained'),
    ('[OH_REV3_5_R011_TURN_CHURN]' in observer, 'R011 observer retained'),
    (R012 not in renderer, 'R012 renderer hot path unchanged'),
    ('bool deferredPointSetInvalidation;' in preload,
     'Flight point-set completion invalidation deferral present'),
    ('if (flightSuspended || HighLogic.LoadedSceneIsFlight)' in preload,
     'Flight and first-frame scene guard present'),
    ('ApplyDeferredPointSetInvalidation();' in preload,
     'non-Flight deferred invalidation drain present'),
    ('"RELOADING ND " + percent + "%"' in nav,
     'explicit cold-start reload label present'),
    ('new Color(0.015f, 0.025f, 0.035f, 1f)' in nav,
     'near-black standby backdrop present'),
    ('REV3_5_R012_VARIANT="' + R012 + '"' in build,
     'R012 build identity variable'),
    ('rev3_5_r012_variant=%s' in build,
     'R012 candidate identity append'),
    ('verify_aeris28_rev3_5_salbutamol_r012_cold_start_preload_ready_recovery.py' in build,
     'R012 verifier wired into build'),
    ('selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py' in prebuild,
     'R012 selftest wired into prebuild'),
]
for token in ('ndReloadGeneration++;', 'frontReloadGeneration = ndReloadGeneration;',
              'if (Reloading) return false;', 'oh_nd_reload='):
    checks.append((token in renderer, 'black-reload successor preserved: ' + token))
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    checks.append((forbidden not in preload and forbidden not in nav,
                   'R012 source excludes ' + forbidden))

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

subprocess.run([
    sys.executable,
    str(ROOT / 'Tools/selftest_v01800_oh_rev35_r012_cold_start_preload_ready_recovery.py')
], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
