#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
PATH = ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_005 SELFTEST]'
text = PATH.read_text()
if 'OH_PHASE6_005' in text and 'REV005 NON-BLOCKING SPECULATIVE PREPARATION' in text and \
        'verify_aeris25_nonblocking_speculative_preparation_hotfix.py' in text:
    print(PREFIX + ' inherited selftests already accept rev005')
    raise SystemExit(0)

def replace_once(old, new, label):
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch old=%d' % (PREFIX, label, count))
    text = text.replace(old, new, 1)

replace_once(
'''     ('internal const string Revision = "OH_PHASE6_003";' in M) or\n     ('internal const string Revision = "OH_PHASE6_004";' in M)) and\n''',
'''     ('internal const string Revision = "OH_PHASE6_003";' in M) or\n     ('internal const string Revision = "OH_PHASE6_004";' in M) or\n     ('internal const string Revision = "OH_PHASE6_005";' in M)) and\n''', 'Phase6 identity successor')
replace_once(
'''     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in U and\n      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE' in U)))\n''',
'''     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in U and\n      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE' in U) or\n     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV005 NON-BLOCKING SPECULATIVE PREPARATION' in U and\n      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV005 NON-BLOCKING SPECULATIVE PREPARATION' in U)))\n''', 'Phase6 build successor')
replace_once(
'''    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active) or\n    ('verify_aeris25_managed_preparation_pipeline_hotfix.py' in active)) and\n''',
'''    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active) or\n    ('verify_aeris25_managed_preparation_pipeline_hotfix.py' in active) or\n    ('verify_aeris25_nonblocking_speculative_preparation_hotfix.py' in active)) and\n''', 'Phase6 final verifier successor')
PATH.write_text(text)
print(PREFIX + ' inherited ADENOSINE verifier accepts exact rev005 successor')
