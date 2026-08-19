#!/usr/bin/env python3
from pathlib import Path
import subprocess, sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'
PREFIX = '[AERIS28 REV3.5 R011 VERIFY]'

renderer = R.read_text(); observer = O.read_text(); project = P.read_text()
build = B.read_text(); prebuild = PRE.read_text()
checks = [
    (R010 in renderer, 'R010 parent marker'),
    ('ndReloadGeneration++;' in renderer and 'if (Reloading) return false;' in renderer,
     'black-reload successor preserved'),
    (R011 not in renderer, 'renderer contains no R011 behavior marker'),
    ('AERISR011TurningViewChurnObserver.cs' in project, 'observer compiled'),
    ('REV3_5_R011_VARIANT="' + R011 + '"' in build, 'R011 build identity variable'),
    ('rev3_5_r011_variant=%s' in build, 'R011 candidate identity append'),
    ('verify_aeris28_rev3_5_salbutamol_r011_turning_view_churn_observer.py' in build,
     'R011 verifier wired into build'),
    ('selftest_v01800_oh_rev35_r011_turning_view_churn_observer.py' in prebuild,
     'R011 selftest wired into prebuild'),
    ('const float SampleIntervalSeconds = 0.10f;' in observer, '10 Hz observer cadence'),
    ('const float LogIntervalSeconds = 5.0f;' in observer, '5 s observer log cadence'),
    ('[OH_REV3_5_R011_TURN_CHURN]' in observer, 'observer telemetry marker'),
    ('OPERATION HEALTH' in build, 'Operation Health lineage display retained'),
    ('FixedNavigationDisplayUpdateHz = 10f' in (ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text(),
     'fixed 10 Hz authority retained'),
    ('160000f' in (ROOT/'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text(),
     '160 km range authority retained'),
]
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', '.SetValue(', '.Invoke(',
                  'FlightCtrlState', 'OnAutopilotUpdate', 'FlightInputHandler'):
    checks.append((forbidden not in observer, 'observer excludes ' + forbidden))
failed=[]
for ok,label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
subprocess.run([sys.executable, str(ROOT/'Tools/selftest_v01800_oh_rev35_r011_turning_view_churn_observer.py')], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
