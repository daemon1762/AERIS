#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
REC = ROOT / 'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'
O15 = ROOT / 'Source/AERISFlightControl/Performance/AERISR015PeriodicGcAttributionObserver.cs'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r016_fdr_high_rate_diagnostics_isolation.py'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS29 REV3.5 R016 VERIFY]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R015 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER'
R016 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION'

for path in (REC, O15, A, B):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

rec = REC.read_text()
observer = O15.read_text()
applicator = A.read_text()
build = B.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R015 in observer and '[OH_REV3_5_R015_GC_ATTR]' in observer,
      'R015 attribution observer retained')
check(R013 not in rec and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check(R016 in rec, 'R016 compiled recorder identity present')
check('static readonly bool R016HighRateDiagnosticsEnabled = false;' in rec,
      'R016 isolation gate is disabled')
check(rec.count('if (!R016HighRateDiagnosticsEnabled) return;') == 9,
      'exactly nine high-rate built-in diagnostic entry gates present')
check('const float SampleIntervalSeconds = 0.10f;' in rec,
      'core FDR 10Hz constant retained')
check('fdrWriter.WriteCsv(line);' in rec and 'cvrWriter.WriteCsv(' in rec,
      'core FDR/CVR writer paths retained')
check('REV3_5_R016_VARIANT="' + R016 + '"' in build,
      'R016 build identity present')
check('rev3_5_r016_variant=%s' in build,
      'R016 candidate identity append present')

for forbidden_target in ('AERISTerrainGpuTileRenderer.cs', 'AERISWorkerScheduler.cs',
                         'AERISTerrainGpuTileRasterizer.cs', 'Source/AERISFlightControl/AA',
                         'Source/AERISFlightControl/Autopilot', 'Source/AERISFlightControl/Protect'):
    check(forbidden_target not in applicator,
          'R016 applicator does not target ' + forbidden_target)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))

subprocess.run([
    sys.executable,
    str(ROOT / 'Tools/selftest_v01800_oh_rev35_r016_fdr_high_rate_diagnostics_isolation.py')
], cwd=str(ROOT), check=True)
print(PREFIX + ' PASS')
print('contract=A/B isolation only: nine built-in control-cadence diagnostic producers return before BeginFlight/CSV capture; 10Hz core FDR, CVR, extension telemetry, R014 and R015 remain active')
