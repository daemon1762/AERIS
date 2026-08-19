#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
REC = ROOT / 'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'
O15 = ROOT / 'Source/AERISFlightControl/Performance/AERISR015PeriodicGcAttributionObserver.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R016 FDR HIGH RATE DIAGNOSTICS ISOLATION]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
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


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def method_bounds(text, method_name):
    token = 'internal void ' + method_name + '('
    start = text.find(token)
    if start < 0 or text.find(token, start + 1) >= 0:
        fail('method anchor mismatch for ' + method_name)
    brace = text.find('{', start)
    if brace < 0:
        fail('method opening brace missing for ' + method_name)
    depth = 0
    i = brace
    while i < len(text):
        c = text[i]
        if c == '{':
            depth += 1
        elif c == '}':
            depth -= 1
            if depth == 0:
                return start, brace, i + 1
        i += 1
    fail('method closing brace missing for ' + method_name)


def inject_guard(text, method_name):
    start, brace, end = method_bounds(text, method_name)
    body = text[brace:end]
    guard = '            if (!R016HighRateDiagnosticsEnabled) return;\n'
    if guard in body:
        return text, False
    insert = brace + 2 if text[brace:brace + 2] == '{\n' else brace + 1
    return text[:insert] + guard + text[insert:], True


for path in (REC, O15, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

rec = REC.read_text()
observer = O15.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R015 not in observer or '[OH_REV3_5_R015_GC_ATTR]' not in observer:
    fail('R015 GC attribution observer parent required before R016 overlay')
if ('REV3_5_R015_VARIANT="' + R015 + '"') not in build:
    fail('R015 build identity parent required before R016 overlay')
if R013 in rec or 'REV3_5_R013_VARIANT=' in build or 'rev3_5_r013_variant=' in build:
    fail('rejected R013 experiment must remain absent')

field_anchor = '        const int MaxExtensionTelemetryChannels = 256;\n'
field_block = (
    field_anchor +
    '        // R016 isolation test: suppress only built-in control-cadence diagnostic CSV producers.\n'
    '        // Core 10 Hz FDR, CVR, extension telemetry and AA comparison remain untouched.\n'
    '        static readonly bool R016HighRateDiagnosticsEnabled = false;\n'
    '        const string R016IsolationVariant = "' + R016 + '";\n'
    '        internal static string R016IsolationMarker { get { return R016IsolationVariant; } }\n')
rec, _ = replace_once(rec, field_anchor, field_block, 'R016 isolation fields')

for method in HIGH_RATE_METHODS:
    rec, _ = inject_guard(rec, method)

# Core streams are intentionally outside the isolation gate.
for method in ('Sample', 'RecordCvr', 'RecordExtensionTelemetry'):
    start, brace, end = method_bounds(rec, method)
    if 'R016HighRateDiagnosticsEnabled' in rec[brace:end]:
        fail('core recorder path was gated by R016: ' + method)

# V/S cruise guide is a child of the guarded V/S diagnostic method and must not be
# independently rewritten; preserving it proves the experiment only changes admission.
start, brace, end = method_bounds(rec, 'WriteVsCruiseAccelerationGuideDiagnostics')
if 'R016HighRateDiagnosticsEnabled' in rec[brace:end]:
    fail('V/S cruise guide must remain structurally unchanged behind parent guard')

r015_var = 'REV3_5_R015_VARIANT="' + R015 + '"\n'
r016_var = r015_var + 'REV3_5_R016_VARIANT="' + R016 + '"\n'
build, _ = replace_once(build, r015_var, r016_var, 'R016 build identity variable')

r015_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r015_periodic_gc_attribution_observer.py"\n')
r016_verify = r015_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r016_fdr_high_rate_diagnostics_isolation.py"\n')
build, _ = replace_once(build, r015_verify, r016_verify, 'R016 build verifier')

r015_identity = (
    'printf \'rev3_5_r015_variant=%s\\n\' "$REV3_5_R015_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r016_identity = r015_identity + (
    'printf \'rev3_5_r016_variant=%s\\n\' "$REV3_5_R016_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r015_identity, r016_identity, 'R016 candidate identity')

r015_suite = (
    " ('OH REV3.5 R015 Periodic GC Attribution Observer',"
    "'selftest_v01800_oh_rev35_r015_periodic_gc_attribution_observer.py'),\n")
r016_suite = r015_suite + (
    " ('OH REV3.5 R016 FDR High Rate Diagnostics Isolation',"
    "'selftest_v01800_oh_rev35_r016_fdr_high_rate_diagnostics_isolation.py'),\n")
prebuild, _ = replace_once(prebuild, r015_suite, r016_suite, 'R016 prebuild suite')

# This revision is an isolation experiment, not a recorder redesign.
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    if forbidden in rec:
        fail('R016 recorder source contains forbidden mechanism: ' + forbidden)

REC.write_text(rec)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent_r014=' + R014)
print('parent_r015=' + R015)
print('r016=' + R016)
print('disabled_built_in_high_rate_methods=' + str(len(HIGH_RATE_METHODS)))
print('core_fdr_10hz=UNCHANGED cvr=UNCHANGED extension_telemetry=UNCHANGED aa_comparison=UNCHANGED')
print('diagnostic_writer_open_and_headers=UNCHANGED; continuous diagnostic row allocation/enqueue=DISABLED')
print('nd_change=0 ap_change=0 fbw_change=0 protect_change=0 control_tuning_change=0')
print('r014_publication_reconcile_change=0 r015_gc_observer_change=0')
