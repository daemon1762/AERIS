#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py'
REC = ROOT / 'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS29 REV3.5 R017 VERIFY]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R016 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'

for path in (R, O, A, REC, B):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
applicator = A.read_text()
recorder = REC.read_text()
build = B.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R014 in renderer, 'R014 renderer parent retained')
check(R016 in recorder, 'R016 isolation parent retained')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check(R017 in observer and '[OH_REV3_5_R017_ND_PRESENT_STALL]' in observer,
      'R017 observer marker present')
check('operationHealthRev35R017BlockedRenderedFalse' in renderer and
      'operationHealthRev35R017BlockedFoundationFlag' in renderer and
      'operationHealthRev35R017BlockedCoverage' in renderer and
      'operationHealthRev35R017BlockedReadyFar' in renderer and
      'operationHealthRev35R017CadenceSkips' in renderer,
      'R017 exact blocker counters present')
check('frontAge >= StallThresholdSeconds' in observer and 'demandPending' in observer,
      'observer detects old-front stall only with real pending demand')
check('SwapFrontAndBack(' not in observer and 'RenderBackBuffer(' not in observer and
      'PresentFrontDirect(' not in observer,
      'observer has no renderer authority')
check('R.write_text(renderer)' in applicator and 'REC.write_text' not in applicator,
      'R017 applicator instruments renderer only; R016 recorder behavior is not rewritten')
check('REV3_5_R017_VARIANT="' + R017 + '"' in build,
      'R017 build identity present')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  '.SetValue(', '.Invoke(', 'System.Diagnostics.StackTrace'):
    check(forbidden not in observer, 'observer excludes ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

subprocess.run([
    sys.executable,
    str(ROOT / 'Tools/selftest_v01800_oh_rev35_r017_nd_presentation_stall_observer.py')
], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
print('contract=measurement-only ND local front-presentation stall attribution; no presentation/control authority')
