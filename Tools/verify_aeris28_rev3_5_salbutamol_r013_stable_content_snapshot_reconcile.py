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
A = ROOT / 'Tools/apply_aeris28_rev3_5_salbutamol_r013_stable_content_snapshot_reconcile.py'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS28 REV3.5 R013 VERIFY]'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'

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
check(('const string Rev35R013Variant = "' + R013 + '";') in renderer,
      'R013 renderer marker present')
check('bool rev35R013ReuseStableSnapshot = !contentGeometryChanged &&' in renderer,
      'stable snapshot reuse gate present')
check('visible = system.CaptureVisible(' in renderer,
      'full geographic capture path retained')
check('if (!rev35R013ReuseStableSnapshot)' in renderer and
      'tiles = PrepareSortedTileScratch(visible.Tiles);' in renderer,
      'sorted tile recapture is conditional')
check('contentFoundationCoverage = MeasureFoundationGpuReadiness(' in renderer,
      'foundation readiness remains authoritative')
check('ResolveRenderableEntries(' in renderer and 'Schedule(' in renderer,
      'existing resolve/schedule path retained')
check('const float ContentPlanningHeadingStepDeg = 6f;' in renderer,
      'REV009 6 degree planning authority retained')
check('const float ContentMaintenanceRetrySeconds = 0.20f;' in renderer,
      '5 Hz retry authority retained')
check('rev35R007FoundationQueue.Count > 0' in renderer,
      'R010 continuous commit wake retained')
for token in ('oh_rev35_r013_variant=', 'oh_rev35_r013_snapshot_reuse=',
              'oh_rev35_r013_full_capture=', 'oh_rev35_r013_completion_reconcile=',
              'oh_rev35_r013_retry_reconcile='):
    check(token in renderer, 'telemetry ' + token)
check('REV3_5_R013_VARIANT="' + R013 + '"' in build,
      'R013 build identity present')
check('rev3_5_r013_variant=%s' in build,
      'R013 candidate identity emission present')

# The R013 overlay is deliberately renderer-only plus build/test wiring. It must not open
# another worker/scheduler/rasterizer authority or touch R012 preload/ND logic.
check("T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'" not in applicator,
      'R013 applicator does not patch TileSystem')
check('AERISTerrainGpuTileRasterizer.cs' not in applicator,
      'R013 applicator does not patch Rasterizer')
check('AERISWorkerScheduler.cs' not in applicator,
      'R013 applicator does not patch WorkerScheduler')
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
    str(ROOT / 'Tools/selftest_v01800_oh_rev35_r013_stable_content_snapshot_reconcile.py')
], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
print('contract=stable content material snapshot reuse for completion/retry reconcile; true geometry refresh still CaptureVisible')
print('authority=R010 commit lane + REV009 6deg planner + fixed 10Hz/160km/complete-coverage unchanged')
