#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r018_complete_foundation_deferred_adoption.py'
S = ROOT / 'Tools/selftest_v01800_oh_rev35_r018_complete_foundation_deferred_adoption.py'
B = ROOT / 'build_ubuntu.sh'

R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_COMPLETE_FOUNDATION_DEFERRED_ADOPTION'

def fail(msg):
    raise SystemExit('[AERIS29 R018 VERIFY] ' + msg)

for p in (R, A, S, B):
    if not p.is_file():
        fail('missing ' + str(p.relative_to(ROOT)))

subprocess.run([sys.executable, str(S)], cwd=str(ROOT), check=True)

r = R.read_text()
a = A.read_text()
b = B.read_text()

checks = (
    (R018 in r, 'renderer identity'),
    ('rev35R018DeferredAdoptionPending' in r, 'pending handover state'),
    ('rev35R018CandidateCoverage >= 0.999f' in r, 'candidate coverage gate'),
    ('readyFar >= visible.FarFoundationCount' in r, 'candidate/FRONT FAR gate'),
    ('Rev35R018RestoreActivePresentationScratch' in r, 'ACTIVE scratch restore'),
    ('rev35R018ProtectedActiveKeys' in r, 'ACTIVE key lifetime protection'),
    ('oh_rev35_r018_handover_adopted=' in r, 'R018 telemetry'),
    ('ContentPlanningHeadingStepDeg = 6f' in r, '6 degree planning threshold'),
    ('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in r,
     '10Hz authority'),
    (('REV3_5_R018_VARIANT="' + R018 + '"') in b, 'build identity'),
    ('rev3_5_r018_variant=' in b, 'candidate identity'),
    (R013 not in r and 'REV3_5_R013_VARIANT=' not in b, 'R013 absent'),
)
failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    fail(', '.join(failed))

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    if forbidden in a:
        fail('applicator contains forbidden mechanism: ' + forbidden)

print('[AERIS29 R018 VERIFY] PASS')
