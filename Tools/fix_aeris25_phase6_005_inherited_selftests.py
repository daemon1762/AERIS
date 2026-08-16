#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
P5 = ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py'
STEP2 = ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'
RETAINED = ROOT / 'Tools/selftest_v01800_operation_health_retained_surface.py'
PREFIX = '[AERIS25 NOREPINEPHRINE PHASE6_005 SELFTEST]'


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit('%s %s anchor mismatch old=%d' % (PREFIX, label, count))
    return text.replace(old, new, 1), True


# ADENOSINE final-tree verifier: admit only the exact Phase6_005 identity/build/verifier.
text = P5.read_text()
changes = False
pairs = [
    (
'''     ('internal const string Revision = "OH_PHASE6_003";' in M) or
     ('internal const string Revision = "OH_PHASE6_004";' in M)) and
''',
'''     ('internal const string Revision = "OH_PHASE6_003";' in M) or
     ('internal const string Revision = "OH_PHASE6_004";' in M) or
     ('internal const string Revision = "OH_PHASE6_005";' in M)) and
''', 'Phase6 identity successor'),
    (
'''     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE' in U)))
''',
'''     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV004 MANAGED PREPARATION PIPELINE' in U) or
     ('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV005 NON-BLOCKING SPECULATIVE PREPARATION' in U and
      'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV005 NON-BLOCKING SPECULATIVE PREPARATION' in U)))
''', 'Phase6 build successor'),
    (
'''    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active) or
    ('verify_aeris25_managed_preparation_pipeline_hotfix.py' in active)) and
''',
'''    ('verify_aeris25_authoritative_publication_lifetime_hotfix.py' in active) or
    ('verify_aeris25_managed_preparation_pipeline_hotfix.py' in active) or
    ('verify_aeris25_nonblocking_speculative_preparation_hotfix.py' in active)) and
''', 'Phase6 final verifier successor'),
]
for old, new, label in pairs:
    text, changed = replace_once(text, old, new, label)
    changes = changes or changed
if changes:
    P5.write_text(text)
    print(PREFIX + ' inherited ADENOSINE verifier accepts exact rev005 successor')
else:
    print(PREFIX + ' inherited ADENOSINE verifier already accepts exact rev005 successor')

# Phase6_002/003/004 upgraded Step2 and Retained FRONT tests use phase6_staged as an
# exact successor identity gate. Extend that gate to REV005, without broadening it to
# arbitrary future revisions.
old_identity = """(('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B) or
    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in B) or
    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in B))"""
new_identity = """(('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in B) or
    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV003 AUTHORITATIVE PUBLICATION' in B) or
    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV004 MANAGED PREPARATION PIPELINE' in B) or
    ('NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV005 NON-BLOCKING SPECULATIVE PREPARATION' in B))"""
for path in (STEP2, RETAINED):
    text = path.read_text()
    text, changed = replace_once(text, old_identity, new_identity,
                                 path.name + ' exact rev005 identity')
    if changed:
        path.write_text(text)
        print(PREFIX + ' exact rev005 staged identity admitted: ' + path.name)
    else:
        print(PREFIX + ' exact rev005 staged identity already present: ' + path.name)

# REV005 deliberately changes the worker-ready expression. The old exact REV004 form
# remains accepted, but REV005 must additionally require the detached-ready witness; a
# generic pending/raster-only expression is not enough for this revision.
text = STEP2.read_text()
old_ready = """ck(('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw) or
   (phase6_staged and
    'bool workerResultReady = pendingEntryCommit != null || rasterizer.CompletedCount > 0;' in draw),
   'worker completion or exact Phase6_002 pending commit wakes content maintenance')"""
new_ready = """ck(('bool workerResultReady = rasterizer.CompletedCount > 0;' in draw) or
   (phase6_staged and
    (('bool workerResultReady = pendingEntryCommit != null || rasterizer.CompletedCount > 0;' in draw) or
     ('bool workerResultReady = pendingEntryCommit != null ||\\n                rasterizer.CompletedCount > 0 || HasReadyManagedPreparationWaiter();' in draw))),
   'worker completion or exact Phase6_002/005 pending-ready state wakes content maintenance')"""
text, changed = replace_once(text, old_ready, new_ready, 'Step2 rev005 worker-ready successor')
if changed:
    STEP2.write_text(text)
    print(PREFIX + ' Step2 exact rev005 detached-ready successor applied')
else:
    print(PREFIX + ' Step2 exact rev005 detached-ready successor already present')

print('Invariant: REV005 may detach worker preparation from the single pending head, but hidden frames still perform no visible capture/render publication and content capture remains authoritative-tick work.')
