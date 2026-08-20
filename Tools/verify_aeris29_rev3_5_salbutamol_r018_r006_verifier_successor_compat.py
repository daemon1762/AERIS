#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
V = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r018_r006_verifier_successor_compat.py'
PREFIX = '[AERIS29 REV3.5 R018 R006 VERIFIER SUCCESSOR COMPAT VERIFY]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'

for path in (V, A):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

text = V.read_text()
applicator = A.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check('legacy_foundation_gate = (' in text,
      'R006 legacy gate remains explicitly represented')
check('r018_foundation_gate_successor = (' in text,
      'exact R018 successor gate exists')
check(R018 in text, 'R018 identity required by successor')
check('foundationComplete = rendered && r018VisibleGpuComplete;' in text,
      'R018 visible FRONT gate required')
check('bool r018OverscanGpuComplete = visible.FoundationComplete &&' in text,
      'old overscan truth remains witness-only successor requirement')
for token in (
    'operationHealthRev35R018VisiblePlanValid',
    'operationHealthRev35R018VisibleRequiredFar',
    'operationHealthRev35R018VisibleReadyFar',
    'operationHealthRev35R018OverscanHolAvoided',
    'oh_rev35_r018_visible_required_far=',
    'oh_rev35_r018_overscan_hol_avoided=',
):
    check(token in text, 'R018 successor witness ' + token)
check('check(legacy_foundation_gate or r018_foundation_gate_successor,' in text,
      'R006 verifier admits only legacy or exact R018 successor')
check(text.count('r018_foundation_gate_successor = (') == 1,
      'R018 successor clause is singular')
check("V = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py'" in applicator,
      'applicator targets R006 verifier only')
for forbidden in (
    'AERISTerrainGpuTileRenderer.cs',
    'AERISWorkerScheduler.cs',
    'AERISTerrainGpuTileRasterizer.cs',
    'Source/AERISFlightControl/Autopilot',
    'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Protect',
):
    check(forbidden not in applicator, 'compat applicator does not target ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
print('contract=test-only R006 legacy/exact-R018 successor compatibility; runtime authority unchanged')
