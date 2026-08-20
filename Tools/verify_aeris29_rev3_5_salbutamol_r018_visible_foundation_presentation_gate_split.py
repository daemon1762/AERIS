#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O17 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py'
B = ROOT / 'build_ubuntu.sh'
S = ROOT / 'Tools/selftest_v01800_oh_rev35_r018_visible_foundation_presentation_gate_split.py'
PREFIX = '[AERIS29 REV3.5 R018 VERIFY]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'

for path in (R, O17, A, B, S):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O17.read_text()
applicator = A.read_text()
build = B.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R014 in renderer, 'R014 formal renderer parent retained')
check(R017 in observer and '[OH_REV3_5_R017_ND_PRESENT_STALL]' in observer,
      'R017 diagnostic parent retained')
check(('const string Rev35R018Variant = "' + R018 + '";') in renderer,
      'R018 renderer identity present')
check('void MeasureVisibleFoundationGpuReadiness(' in renderer,
      'R018 visible FAR readiness helper present')
check('AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,' in renderer,
      'R018 uses canonical viewport planner')
check('operationHealthRev35R018VisibleReadyFar >=' in renderer and
      'operationHealthRev35R018VisibleRequiredFar;' in renderer,
      'R018 exact-current visible completeness predicate present')
check('foundationComplete = rendered && r018VisibleGpuComplete;' in renderer,
      'FRONT swap gate split present')
check('bool readyFoundationNow = r018VisibleGpuComplete;' in renderer,
      'recovery gate split present')
check('operationHealthRev35R018OverscanHolAvoided++;' in renderer,
      'overscan HOL avoidance witness present')
check('oh_rev35_r018_visible_required_far=' in renderer and
      'oh_rev35_r018_overscan_required_far=' in renderer and
      'oh_rev35_r018_overscan_hol_avoided=' in renderer,
      'R018 runtime telemetry present')
check(('REV3_5_R018_VARIANT="' + R018 + '"') in build,
      'R018 build identity present')
check('rev3_5_r018_variant=%s' in build,
      'R018 candidate identity emission present')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check('AERISWorkerScheduler.cs' not in applicator and
      'AERISTerrainGpuTileRasterizer.cs' not in applicator,
      'R018 does not patch scheduler/rasterizer')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    check(forbidden not in renderer, 'forbidden mechanism absent: ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

subprocess.run([sys.executable, str(S)], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
print('contract=canonical exact-range viewport FAR plan gates FRONT/recovery; hidden overscan remains preparation-only')
print('authority=R014 publication batching + R017 diagnostics retained; fixed 10Hz/exact 160km/complete visible coverage unchanged')
