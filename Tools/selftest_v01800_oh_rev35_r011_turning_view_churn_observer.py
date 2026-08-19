#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SRC = ROOT / 'Source' / 'AERISFlightControl'
O = SRC / 'Terrain' / 'AERISR011TurningViewChurnObserver.cs'
R = SRC / 'Terrain' / 'AERISTerrainGpuTileRenderer.cs'
P = SRC / 'AERISFlightControl.csproj'
B = ROOT / 'build_ubuntu.sh'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'

checks = []
def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)

ck(O.is_file(), 'R011 observer source exists')
observer = O.read_text() if O.is_file() else ''
renderer = R.read_text()
project = P.read_text()
build = B.read_text()

ck(R010 in renderer, 'generated R010 renderer parent is present')
ck('ndReloadGeneration++;' in renderer and
   'frontReloadGeneration = ndReloadGeneration;' in renderer and
   'if (Reloading) return false;' in renderer,
   'AERIS24 black-reload successor is preserved')
ck('AERISR011TurningViewChurnObserver.cs' in project,
   'R011 observer is included in xbuild project')
ck(('REV3_5_R011_VARIANT="' + R011 + '"') in build and
   'rev3_5_r011_variant=%s' in build,
   'R011 candidate identity is appended by reconstruction overlay')
ck('OPERATION HEALTH' in build,
   'existing Operation Health lineage display remains intact')
ck('const float SampleIntervalSeconds = 0.10f;' in observer,
   'observer sample cadence is 10 Hz')
ck('const float LogIntervalSeconds = 5.0f;' in observer,
   'observer log cadence is five seconds')
ck('[OH_REV3_5_R011_TURN_CHURN]' in observer,
   'observer has dedicated telemetry prefix')

for token in (
    'reason_snapshot=', 'reason_visible=', 'reason_terrain_gen=',
    'reason_heading3=', 'reason_disp2pct=', 'front_terrain_gen=',
    'front_view_gen=', 'front_content_rev=', 'requested_clear_est=',
    'resolve_calls=', 'auth_heading005=', 'auth_move=', 'front_swap='):
    ck(token in observer, 'observer telemetry token ' + token)

ck('>= 3f' in observer and
   'Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02)' in observer,
   'observer mirrors R010 heading/movement refresh thresholds')
ck('>= 3f' in renderer and
   'Math.Max(100.0, Math.Max(1f, rangeMeters) * 0.02)' in renderer,
   'R010 renderer keeps authoritative heading/movement thresholds')

for forbidden in (
    '.SetValue(', '.Invoke(', 'new Thread', 'Task.Run', 'ThreadPool.',
    'FlightCtrlState', 'OnAutopilotUpdate', 'FlightInputHandler'):
    ck(forbidden not in observer, 'observer excludes ' + forbidden)

ck('new AERISTerrainGpuTileRenderer' not in observer,
   'observer does not create a second renderer')
ck('new AERISTerrainTileSystem' not in observer,
   'observer does not create a second tile system')
ck(R011 not in renderer,
   'R011 identity does not alter R010 renderer implementation')

failed = [name for ok, name in checks if not ok]
print('\n[OH REV3.5 R011 TURNING VIEW CHURN OBSERVER] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + ', '.join(failed))
    raise SystemExit(1)
