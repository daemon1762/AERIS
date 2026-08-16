#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
FILES=[ROOT/'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py',
       ROOT/'Tools/selftest_v01800_operation_health_retained_surface.py']
PREFIX='[AERIS25 NOREPINEPHRINE PHASE6_004 SELFTEST]'
OLD="(('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B) or\n    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in B))"
NEW="(('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B) or\n    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in B) or\n    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in B))"
for path in FILES:
    text=path.read_text()
    if NEW in text:
        print(PREFIX+' already present: '+path.name); continue
    count=text.count(OLD)
    if count != 1:
        raise SystemExit('%s %s rev003 marker count=%d' % (PREFIX,path.name,count))
    path.write_text(text.replace(OLD,NEW,1))
    print(PREFIX+' exact rev004 successor admitted: '+path.name)
print('Invariant: visible authority stays 10 Hz; rev004 relocates managed preparation only; rev003 publication/lifetime remains authoritative.')
