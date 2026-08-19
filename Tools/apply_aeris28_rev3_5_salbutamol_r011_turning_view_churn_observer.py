#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS28 REV3.5 SALBUTAMOL SULFATE R011 TURNING VIEW CHURN OBSERVER]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R011 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R011_TURNING_VIEW_CHURN_OBSERVER'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True

for path in (R, O, P, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
project = P.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R010 not in renderer:
    fail('R010 generated parent required before R011 overlay')
for token in ('ndReloadGeneration++;', 'frontReloadGeneration = ndReloadGeneration;',
              'if (Reloading) return false;', 'oh_nd_reload='):
    if token not in renderer:
        fail('AERIS24 black-reload successor missing before R011 overlay: ' + token)
if '[OH_REV3_5_R011_TURN_CHURN]' not in observer:
    fail('R011 observer source marker missing')

compile_line = '    <Compile Include="Terrain\\AERISR011TurningViewChurnObserver.cs" />\n'
if compile_line not in project:
    anchor = '    <Compile Include="Terrain\\AERISTerrainGpuTileRenderer.cs" />\n'
    project, _ = replace_once(project, anchor, anchor + compile_line,
                              'R011 xbuild compile include')

r010_var = 'REV3_5_R010_VARIANT="' + R010 + '"\n'
r011_var = r010_var + 'REV3_5_R011_VARIANT="' + R011 + '"\n'
build, _ = replace_once(build, r010_var, r011_var,
                          'R011 build identity variable')

r010_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py"\n'
r011_verify = r010_verify + 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r011_turning_view_churn_observer.py"\n'
build, _ = replace_once(build, r010_verify, r011_verify,
                          'R011 build verifier')

r010_identity = 'printf \'rev3_5_r010_variant=%s\\n\' "$REV3_5_R010_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
r011_identity = r010_identity + 'printf \'rev3_5_r011_variant=%s\\n\' "$REV3_5_R011_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
build, _ = replace_once(build, r010_identity, r011_identity,
                          'R011 candidate identity')

suite = " ('OH REV3.5 R011 Turning View Churn Observer','selftest_v01800_oh_rev35_r011_turning_view_churn_observer.py'),\n"
if suite not in prebuild:
    anchor = 'suites=[\n'
    prebuild, _ = replace_once(prebuild, anchor, anchor + suite,
                               'R011 prebuild suite')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', '.SetValue(', '.Invoke(',
                  'FlightCtrlState', 'OnAutopilotUpdate', 'FlightInputHandler'):
    if forbidden in observer:
        fail('observer-only contract violated: ' + forbidden)

P.write_text(project)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent=' + R010)
print('r011=' + R011)
print('renderer_behavior_change=0')
print('worker_change=0 scheduler_change=0 rasterizer_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 publication_authority_change=0')
print('observer=read-only 10Hz sample / 5s summary')
