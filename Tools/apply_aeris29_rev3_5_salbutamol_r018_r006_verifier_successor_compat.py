#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
V = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py'
PREFIX = '[AERIS29 REV3.5 R018 R006 VERIFIER SUCCESSOR COMPAT]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'

if not V.is_file():
    raise SystemExit(PREFIX + ' FAIL missing ' + str(V.relative_to(ROOT)))

text = V.read_text()
legacy = """check('foundationComplete = rendered && visible.FoundationComplete &&' in renderer and
      'lastBackFoundationCoverage >= 0.999f' in renderer and
      'readyFar >= visible.FarFoundationCount' in renderer,
      'foundation-complete swap gate is byte-for-byte semantically retained')
"""
successor = """# R018 successor compatibility: R006 originally observed and froze the then-current
# complete-foundation gate. Preserve that legacy contract, but admit only the exact R018
# visible-foundation split when its full identity/readiness/witness telemetry is present.
legacy_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in renderer and
    'lastBackFoundationCoverage >= 0.999f' in renderer and
    'readyFar >= visible.FarFoundationCount' in renderer)
r018_foundation_gate_successor = (
    'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT' in renderer and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in renderer and
    'bool r018OverscanGpuComplete = visible.FoundationComplete &&' in renderer and
    'lastBackFoundationCoverage >= 0.999f' in renderer and
    'readyFar >= visible.FarFoundationCount;' in renderer and
    'operationHealthRev35R018VisiblePlanValid' in renderer and
    'operationHealthRev35R018VisibleRequiredFar' in renderer and
    'operationHealthRev35R018VisibleReadyFar' in renderer and
    'operationHealthRev35R018OverscanHolAvoided' in renderer and
    'oh_rev35_r018_visible_required_far=' in renderer and
    'oh_rev35_r018_overscan_hol_avoided=' in renderer)
check(legacy_foundation_gate or r018_foundation_gate_successor,
      'foundation-complete swap gate is R006 legacy or exact R018 visible successor')
"""

if successor in text:
    print(PREFIX + ' already applied')
elif legacy in text:
    if text.count(legacy) != 1:
        raise SystemExit(PREFIX + ' FAIL legacy assertion ambiguous=%d' % text.count(legacy))
    text = text.replace(legacy, successor, 1)
    V.write_text(text)
    print(PREFIX + ' APPLY PASS')
else:
    raise SystemExit(PREFIX + ' FAIL legacy assertion anchor missing and exact successor absent')

final = V.read_text()
required = (
    'legacy_foundation_gate = (',
    'r018_foundation_gate_successor = (',
    R018,
    'foundationComplete = rendered && r018VisibleGpuComplete;',
    'bool r018OverscanGpuComplete = visible.FoundationComplete &&',
    'operationHealthRev35R018VisiblePlanValid',
    'operationHealthRev35R018VisibleRequiredFar',
    'operationHealthRev35R018VisibleReadyFar',
    'operationHealthRev35R018OverscanHolAvoided',
    'oh_rev35_r018_visible_required_far=',
    'oh_rev35_r018_overscan_hol_avoided=',
    'foundation-complete swap gate is R006 legacy or exact R018 visible successor',
)
missing = [token for token in required if token not in final]
if missing:
    raise SystemExit(PREFIX + ' FAIL successor contract incomplete: ' + ', '.join(missing))
if final.count('r018_foundation_gate_successor = (') != 1:
    raise SystemExit(PREFIX + ' FAIL successor gate count=%d' %
                     final.count('r018_foundation_gate_successor = ('))
print('target=R006 verifier only; runtime renderer/source authority unchanged')
print('legacy_gate=retained')
print('successor=exact R018 marker + visible gate + overscan witness + R018 telemetry required')
