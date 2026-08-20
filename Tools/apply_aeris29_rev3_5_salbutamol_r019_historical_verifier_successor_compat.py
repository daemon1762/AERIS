#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PREFIX = '[AERIS29 REV3.5 R019 HISTORICAL VERIFIER SUCCESSOR COMPAT]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'


def fail(message):
    raise SystemExit(PREFIX + ' FAIL ' + message)


# Reuse the committed R006 exact-R018 successor patch first.
r006 = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r018_r006_verifier_successor_compat.py'
if not r006.is_file():
    fail('missing R006 compatibility applicator')
subprocess.run([sys.executable, str(r006)], cwd=str(ROOT), check=True)

items = [
    (
        ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py',
        """ck('foundationComplete = rendered && visible.FoundationComplete &&' in r and
   'lastBackFoundationCoverage >= 0.999f' in r and
   'readyFar >= visible.FarFoundationCount' in r,
   'strict FoundationComplete gate retained')
""",
        """# R018/R019 successor compatibility: preserve the historical R007
# publication-completeness contract, but admit the exact R018 visible-foundation
# split inherited unchanged by R019. Hidden overscan truth remains witness-only.
legacy_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount' in r)
r018_foundation_gate_successor = (
    'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT' in r and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in r and
    'bool r018OverscanGpuComplete = visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount;' in r and
    'operationHealthRev35R018VisiblePlanValid' in r and
    'operationHealthRev35R018VisibleRequiredFar' in r and
    'operationHealthRev35R018VisibleReadyFar' in r and
    'operationHealthRev35R018OverscanHolAvoided' in r and
    'oh_rev35_r018_visible_required_far=' in r and
    'oh_rev35_r018_overscan_hol_avoided=' in r)
ck(legacy_foundation_gate or r018_foundation_gate_successor,
   'strict FoundationComplete gate is R007 legacy or exact R018/R019 successor')
"""
    ),
    (
        ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py',
        """check('foundationComplete = rendered && visible.FoundationComplete &&' in r and
      'lastBackFoundationCoverage >= 0.999f' in r and
      'readyFar >= visible.FarFoundationCount' in r,
      'strict FoundationComplete swap gate retained')
""",
        """# R018/R019 successor compatibility for the historical R008 visual gate.
legacy_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount' in r)
r018_foundation_gate_successor = (
    'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT' in r and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in r and
    'bool r018OverscanGpuComplete = visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount;' in r and
    'operationHealthRev35R018VisiblePlanValid' in r and
    'operationHealthRev35R018VisibleRequiredFar' in r and
    'operationHealthRev35R018VisibleReadyFar' in r and
    'operationHealthRev35R018OverscanHolAvoided' in r and
    'oh_rev35_r018_visible_required_far=' in r and
    'oh_rev35_r018_overscan_hol_avoided=' in r)
check(legacy_foundation_gate or r018_foundation_gate_successor,
      'strict FoundationComplete swap gate is R008 legacy or exact R018/R019 successor')
"""
    ),
    (
        ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py',
        """check('foundationComplete = rendered && visible.FoundationComplete &&' in r and
      'lastBackFoundationCoverage >= 0.999f' in r and
      'readyFar >= visible.FarFoundationCount' in r,
      'strict Foundation publication gate retained')
""",
        """# R018/R019 successor compatibility for the historical R009 visual gate.
legacy_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount' in r)
r018_foundation_gate_successor = (
    'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT' in r and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in r and
    'bool r018OverscanGpuComplete = visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount;' in r and
    'operationHealthRev35R018VisiblePlanValid' in r and
    'operationHealthRev35R018VisibleRequiredFar' in r and
    'operationHealthRev35R018VisibleReadyFar' in r and
    'operationHealthRev35R018OverscanHolAvoided' in r and
    'oh_rev35_r018_visible_required_far=' in r and
    'oh_rev35_r018_overscan_hol_avoided=' in r)
check(legacy_foundation_gate or r018_foundation_gate_successor,
      'strict Foundation publication gate is R009 legacy or exact R018/R019 successor')
"""
    ),
    (
        ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py',
        """ck('foundationComplete = rendered && visible.FoundationComplete &&' in r and
   'lastBackFoundationCoverage >= 0.999f' in r,
   'strict complete-foundation swap gate retained')
""",
        """# R018/R019 successor compatibility for the historical R010 visual gate.
legacy_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r)
r018_foundation_gate_successor = (
    'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT' in r and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in r and
    'bool r018OverscanGpuComplete = visible.FoundationComplete &&' in r and
    'lastBackFoundationCoverage >= 0.999f' in r and
    'readyFar >= visible.FarFoundationCount;' in r and
    'operationHealthRev35R018VisiblePlanValid' in r and
    'operationHealthRev35R018VisibleRequiredFar' in r and
    'operationHealthRev35R018VisibleReadyFar' in r and
    'operationHealthRev35R018OverscanHolAvoided' in r and
    'oh_rev35_r018_visible_required_far=' in r and
    'oh_rev35_r018_overscan_hol_avoided=' in r)
ck(legacy_foundation_gate or r018_foundation_gate_successor,
   'strict complete-foundation swap gate is R010 legacy or exact R018/R019 successor')
"""
    ),
]

for path, legacy, successor in items:
    if not path.is_file():
        fail('missing ' + str(path.relative_to(ROOT)))
    text = path.read_text()
    if successor in text:
        print(PREFIX + ' already compatible: ' + path.name)
        continue
    # Accept earlier manual R018 successor forms as already compatible.
    if 'r018_foundation_gate_successor = (' in text and R018 in text:
        print(PREFIX + ' existing exact R018 successor retained: ' + path.name)
        continue
    count = text.count(legacy)
    if count != 1:
        fail('%s legacy gate anchor count=%d' % (path.name, count))
    path.write_text(text.replace(legacy, successor, 1))
    print(PREFIX + ' patched: ' + path.name)

print(PREFIX + ' APPLY PASS')
print('scope=historical verifier compatibility only; runtime source unchanged')
