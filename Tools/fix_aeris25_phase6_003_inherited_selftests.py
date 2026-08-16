#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
STEP2 = ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'
RETAINED = ROOT / 'Tools/selftest_v01800_operation_health_retained_surface.py'
FILES = [STEP2, RETAINED]
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_003 SELFTEST]'
OLD = "'NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B"
NEW = "(('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B) or\n    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in B))"

for path in FILES:
    text = path.read_text()
    if NEW not in text:
        count = text.count(OLD)
        if count != 1:
            raise SystemExit('%s %s exact rev002 marker count=%d' % (PREFIX, path.name, count))
        text = text.replace(OLD, NEW, 1)
        path.write_text(text)
        print(PREFIX + ' exact rev003 successor admitted: ' + path.name)
    else:
        print(PREFIX + ' identity successor already present: ' + path.name)

# Phase6_003 makes publication authority explicit in the method signature. The inherited
# Step2 semantic is unchanged: visible CaptureVisible work remains content-tick-only, but
# the content tick now calls the staged pump with allowPublication=true. Hidden frames use
# the matching false call and are checked separately by the rev003 verifier.
text = STEP2.read_text()
old_call = "(phase6_staged and 'PumpStagedCompletedCommit(system);' in content)"
new_call = "(phase6_staged and (('PumpStagedCompletedCommit(system);' in content) or\n    ('PumpStagedCompletedCommit(system, true);' in content)))"
if new_call not in text:
    count = text.count(old_call)
    if count != 1:
        raise SystemExit('%s Step2 publication-call anchor count=%d' % (PREFIX, count))
    STEP2.write_text(text.replace(old_call, new_call, 1))
    print(PREFIX + ' Step2 exact authoritative publication call admitted')
else:
    print(PREFIX + ' Step2 authoritative publication successor already present')

print('Invariant: hidden staged preparation may advance between visible ticks; Finalize/publication remains authoritative-only under rev003')
