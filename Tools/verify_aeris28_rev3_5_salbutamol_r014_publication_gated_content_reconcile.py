#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR011TurningViewChurnObserver.cs'
P = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainPreloadBuilder.cs'
N = ROOT / 'Source/AERISFlightControl/UI/AERISNavigationDisplay.cs'
A = ROOT / 'Tools/apply_aeris28_rev3_5_salbutamol_r014_publication_gated_content_reconcile.py'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS28 REV3.5 R014 VERIFY]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'

for path in (R, O, P, N, A, B):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text(); observer = O.read_text(); preload = P.read_text()
nav = N.read_text(); applicator = A.read_text(); build = B.read_text()
checks = []

def check(value, label):
    checks.append((bool(value), label))

check(R010 in renderer, 'R010 formal rendering parent retained')
check('[OH_REV3_5_R011_TURN_CHURN]' in observer, 'R011 observer retained')
check('appliedPointSetSignature' in preload and 'deferredPointSetInvalidation' in preload,
      'R012 preload fix retained')
check('RELOADING ND\\nTERRAIN INIT' in nav, 'R012 cold-start fix retained')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 experiment not inherited')
check(('const string Rev35R014Variant = "' + R014 + '";') in renderer,
      'R014 renderer marker present')
check('rev35R014PublicationSerial++;' in renderer,
      'R014 successful-publication serial present')
check('rev35R014PublicationSerial != rev35R014ReconciledPublicationSerial' in renderer,
      'R014 deferred-publication state present')
check('contentRetryDue || rev35R014PublicationPendingBeforeTick;' in renderer,
      'deferred publication keeps content path awake')
check('bool rev35R014ContentCadenceDue =' in renderer and
      'presentationNow >= nextContentMaintenanceRealtime;' in renderer,
      'R014 full reconcile uses inherited content deadline')
check('const float ContentMaintenanceRetrySeconds = 0.20f;' in renderer,
      'R014 preserves inherited 0.20 second / 5 Hz cadence')
check('if (rev35R014ReconcileRan)' in renderer,
      'R014 prune/full-reconcile witness present')
check('rasterizer.ReconcileCurrentRequests(requested);' in renderer,
      'R008 current-request reconcile retained')
check('for (int admissionPass = 0; admissionPass < 2; admissionPass++)' in renderer,
      'R008 FAR-first admission retained')
check('const float ContentPlanningHeadingStepDeg = 6f;' in renderer,
      'REV009 6 degree planning authority retained')
check('rev35R007FoundationQueue.Count > 0' in renderer,
      'R010 continuous commit wake retained')
check('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in renderer,
      'Phase6_003 authoritative publication retained')
check(renderer.count('PendingEntryCommit pendingEntryCommit;') == 1,
      'single pending commit lane retained')
for token in ('oh_rev35_r014_variant=', 'oh_rev35_r014_publications=',
              'oh_rev35_r014_full_reconcile=', 'oh_rev35_r014_worker_only_skip=',
              'oh_rev35_r014_publication_defer=', 'oh_rev35_r014_publication_reconcile=',
              'oh_rev35_r014_retry_reconcile='):
    check(token in renderer, 'telemetry ' + token)
check('REV3_5_R014_VARIANT="' + R014 + '"' in build,
      'R014 build identity present')
check('rev3_5_r014_variant=%s' in build,
      'R014 candidate identity emission present')

check('AERISTerrainGpuTileRasterizer.cs' not in applicator,
      'R014 applicator does not patch Rasterizer source file')
check('AERISWorkerScheduler.cs' not in applicator,
      'R014 applicator does not patch WorkerScheduler source file')
check("P.write_text" not in applicator and "N.write_text" not in applicator,
      'R014 applicator does not write R012 preload/ND sources')
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in renderer, 'rejected mechanism absent: ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

subprocess.run([
    sys.executable,
    str(ROOT / 'Tools/selftest_v01800_oh_rev35_r014_publication_gated_content_reconcile.py')
], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
print('contract=R010 staged progress remains immediate; actual publications are batched into inherited 0.20s/5Hz full Capture/requested/R008 FAR-first resolve/foundation/prune; geometry stays immediate')
print('authority=R012 safe baseline; rejected R013 absent; REV009 6deg + R010 single lane + fixed 10Hz/160km retained')
