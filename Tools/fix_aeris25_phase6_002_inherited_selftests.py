#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
P = ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_002 SELFTEST]'


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch old=%d' % (PREFIX, label, count))
    return text.replace(old, new, 1), True


text = P.read_text()
old1 = "ck('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw,'worker completion wakes content maintenance')"
new1 = """phase6_staged = ('AERIS25_STAGED_MAIN_THREAD_COMMIT' in R and
    'NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B)
ck(('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw) or
   (phase6_staged and
    'bool workerResultReady = pendingEntryCommit != null || rasterizer.CompletedCount > 0;' in draw),
   'worker completion or exact Phase6_002 pending commit wakes content maintenance')"""
text, c1 = replace_once(text, old1, new1, 'worker-ready successor')

old2 = "ck('DrainCompleted(system);' in content and 'system.CaptureVisible(' in content,'worker drain and visible capture are content-only work')"
new2 = """ck((('DrainCompleted(system);' in content) or
    (phase6_staged and 'PumpStagedCompletedCommit(system);' in content)) and
   'system.CaptureVisible(' in content,
   'content tick owns visible capture and legacy drain or exact Phase6_002 staged pump')"""
text, c2 = replace_once(text, old2, new2, 'content-pump successor')

if c1 or c2:
    P.write_text(text)
    print(PREFIX + ' exact Phase6_002 Step2 successor contract applied')
else:
    print(PREFIX + ' exact Phase6_002 Step2 successor contract already present')
print('Invariant: visible CaptureVisible/projection/BACK presentation remain authoritative-tick work; only staged commit may advance between ticks')
