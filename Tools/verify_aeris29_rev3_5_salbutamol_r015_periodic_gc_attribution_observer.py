#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
O = ROOT / 'Source/AERISFlightControl/Performance/AERISR015PeriodicGcAttributionObserver.cs'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r015_periodic_gc_attribution_observer.py'
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS29 REV3.5 R015 VERIFY]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R015 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER'

for path in (O, A, R, B):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

observer = O.read_text()
applicator = A.read_text()
renderer = R.read_text()
build = B.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R014 in renderer, 'R014 formal runtime parent retained')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check(R015 in observer and '[OH_REV3_5_R015_GC_ATTR]' in observer,
      'R015 observer identity and telemetry marker present')
check('R.write_text' not in applicator and 'renderer.write_text' not in applicator,
      'R015 applicator never writes renderer source')
check('AERISWorkerScheduler.cs' not in applicator,
      'R015 applicator does not target WorkerScheduler')
check('AERISTerrainGpuTileRasterizer.cs' not in applicator,
      'R015 applicator does not target Rasterizer')
check('AERISTerrainTileSystem.cs' not in applicator,
      'R015 applicator does not target TileSystem')
check('Source/AERISFlightControl/AA' not in applicator and
      'Source/AERISFlightControl/Autopilot' not in applicator and
      'Source/AERISFlightControl/Protect' not in applicator and
      'Source/AERISFlightControl/Landing' not in applicator,
      'R015 applicator does not target control sources')
check('GC.CollectionCount(2)' in observer and 'GC.GetTotalMemory(false)' in observer,
      'observer measures Full GC and managed heap without forcing GC')
check('terrain_heavy_idle=' in observer,
      'observer exports negative-attribution witness')
check('REV3_5_R015_VARIANT="' + R015 + '"' in build,
      'R015 build identity present')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  '.SetValue(', '.Invoke(', 'System.Diagnostics.StackTrace',
                  'UnityEngine.Profiling.Profiler'):
    check(forbidden not in observer, 'observer excludes ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

subprocess.run([
    sys.executable,
    str(ROOT / 'Tools/selftest_v01800_oh_rev35_r015_periodic_gc_attribution_observer.py')
], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
print('contract=measurement-only 10Hz GC/heap fixed-ring observer; renderer counter reflection only at Full-GC events/baseline; one event log per Gen2 collection')
print('attribution=correlation + AERIS terrain-heavy-idle negative witness only; no unsupported external-cause claim')
