#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
REC = ROOT / 'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'
O15 = ROOT / 'Source/AERISFlightControl/Performance/AERISR015PeriodicGcAttributionObserver.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r016_fdr_high_rate_diagnostics_isolation.py'
PREFIX = '[OH REV3.5 R016 FDR HIGH RATE DIAGNOSTICS ISOLATION]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R015 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER'
R016 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION'

HIGH_RATE_METHODS = (
    'SampleBankDiagnostics',
    'SampleHeadingDiagnostics',
    'SampleApSmoothness',
    'SamplePitchDiagnostics',
    'SampleVerticalSpeedDiagnostics',
    'SampleAccelerationDiagnostics',
    'SampleVelocityDiagnostics',
    'SampleAltitudeDiagnostics',
    'SampleGroundTakeoffDiagnostics',
)


def block_bounds(text, op):
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return op, i + 1
            i += 1; continue
        if state == 'line':
            if c == '\n': state = 'code'
            i += 1; continue
        if state == 'block':
            if c == '*' and n == '/': state = 'code'; i += 2; continue
            i += 1; continue
        if state == 'string':
            if c == '\\': i += 2; continue
            if c == '"': state = 'code'
            i += 1; continue
        if state == 'char':
            if c == '\\': i += 2; continue
            if c == "'": state = 'code'
            i += 1; continue
    return -1, -1


def method_body(text, name):
    token = 'internal void ' + name + '('
    start = text.find(token)
    if start < 0 or text.find(token, start + 1) >= 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
    _, end = block_bounds(text, op)
    return text[start:end] if end > op else ''


for path in (REC, O15, B, PRE, A):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

rec = REC.read_text()
observer = O15.read_text()
build = B.read_text()
prebuild = PRE.read_text()
applicator = A.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R015 in observer and '[OH_REV3_5_R015_GC_ATTR]' in observer,
      'R015 GC observer retained for A/B attribution')
check(R013 not in rec and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check('static readonly bool R016HighRateDiagnosticsEnabled = false;' in rec,
      'R016 high-rate diagnostic gate is disabled')
check(('const string R016IsolationVariant = "' + R016 + '";') in rec,
      'R016 compiled isolation marker present')
check('R016IsolationMarker' in rec, 'R016 marker has compiled accessor')
check(rec.count('if (!R016HighRateDiagnosticsEnabled) return;') == len(HIGH_RATE_METHODS),
      'exactly nine built-in high-rate methods are gated')

guard = 'if (!R016HighRateDiagnosticsEnabled) return;'
for method in HIGH_RATE_METHODS:
    body = method_body(rec, method)
    check(bool(body), method + ' boundary resolves')
    check(body.count(guard) == 1, method + ' has exactly one R016 gate')
    if body:
        gi = body.find(guard)
        begin = body.find('BeginFlight(')
        capture = body.find('CaptureCsv(')
        check(gi >= 0 and (begin < 0 or gi < begin), method + ' gate precedes BeginFlight')
        check(gi >= 0 and (capture < 0 or gi < capture), method + ' gate precedes CSV allocation')

core_sample = method_body(rec, 'Sample')
cvr = method_body(rec, 'RecordCvr')
extension = method_body(rec, 'RecordExtensionTelemetry')
vs_guide = method_body(rec, 'WriteVsCruiseAccelerationGuideDiagnostics')
check(bool(core_sample) and bool(cvr) and bool(extension) and bool(vs_guide),
      'core recorder method boundaries resolve')
check(guard not in core_sample, '10 Hz core FDR is not isolated')
check('SampleIntervalSeconds = 0.10f' in rec and 'nextSample = Time.realtimeSinceStartup + SampleIntervalSeconds;' in core_sample,
      '10 Hz core FDR cadence remains present')
check('fdrWriter.WriteCsv(line);' in core_sample,
      '10 Hz core FDR still writes rows')
check(guard not in cvr and 'cvrWriter.WriteCsv(' in cvr,
      'CVR path remains enabled')
check(guard not in extension and 'writer.WriteCsv(values);' in extension,
      'extension telemetry remains enabled')
check(guard not in vs_guide and 'vsCruiseAccelerationGuideWriter.WriteCsv(line);' in vs_guide,
      'V/S cruise child source remains unchanged behind guarded parent')
check('WriteVsCruiseAccelerationGuideDiagnostics(vs, now);' in method_body(rec, 'SampleVerticalSpeedDiagnostics'),
      'V/S child call remains structurally owned by guarded V/S diagnostic')

for writer in ('bankDiagnosticsWriter', 'hdgDiagnosticsWriter', 'apSmoothnessWriter',
               'pitchDiagnosticsWriter', 'vsDiagnosticsWriter',
               'vsCruiseAccelerationGuideWriter', 'accelerationDiagnosticsWriter',
               'velocityDiagnosticsWriter', 'altDiagnosticsWriter',
               'groundTakeoffDiagnosticsWriter'):
    check(('OpenDiagnostic(' in rec and writer in rec),
          writer + ' remains structurally present for isolation-only experiment')

check('REV3_5_R016_VARIANT="' + R016 + '"' in build,
      'R016 build identity variable present')
check('rev3_5_r016_variant=%s' in build,
      'R016 candidate identity emission present')
check('verify_aeris29_rev3_5_salbutamol_r016_fdr_high_rate_diagnostics_isolation.py' in build,
      'R016 verifier wired into build')
check('selftest_v01800_oh_rev35_r016_fdr_high_rate_diagnostics_isolation.py' in prebuild,
      'R016 selftest wired into prebuild')

for forbidden_target in ('AERISTerrainGpuTileRenderer.cs', 'AERISWorkerScheduler.cs',
                         'AERISTerrainGpuTileRasterizer.cs', 'Source/AERISFlightControl/AA',
                         'Source/AERISFlightControl/Autopilot', 'Source/AERISFlightControl/Protect'):
    check(forbidden_target not in applicator,
          'R016 applicator does not target ' + forbidden_target)

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    check(forbidden not in rec, 'recorder excludes ' + forbidden)

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok: failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
print('contract=disable nine built-in control-cadence diagnostic row producers before BeginFlight/CSV allocation; preserve core 10Hz FDR/CVR/extensions/AA comparison')
