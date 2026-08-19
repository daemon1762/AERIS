#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Performance/AERISR015PeriodicGcAttributionObserver.cs'
P = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R015 PERIODIC GC ATTRIBUTION OBSERVER]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R015 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, O, P, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
project = P.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R014 not in renderer:
    fail('formal R014 generated parent required before R015 overlay')
if R013 in renderer or 'REV3_5_R013_VARIANT=' in build or 'rev3_5_r013_variant=' in build:
    fail('rejected R013 experiment must remain absent')
if R015 not in observer or '[OH_REV3_5_R015_GC_ATTR]' not in observer:
    fail('R015 observer source marker missing')

compile_line = '    <Compile Include="Performance\\AERISR015PeriodicGcAttributionObserver.cs" />\n'
if compile_line not in project:
    anchor = '    <Compile Include="Performance\\AERISOperationHealthPenicillin.cs" />\n'
    project, _ = replace_once(project, anchor, anchor + compile_line,
                              'R015 xbuild compile include')

r014_var = 'REV3_5_R014_VARIANT="' + R014 + '"\n'
r015_var = r014_var + 'REV3_5_R015_VARIANT="' + R015 + '"\n'
build, _ = replace_once(build, r014_var, r015_var,
                        'R015 build identity variable')

r014_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris28_rev3_5_salbutamol_r014_publication_gated_content_reconcile.py"\n')
r015_verify = r014_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r015_periodic_gc_attribution_observer.py"\n')
build, _ = replace_once(build, r014_verify, r015_verify,
                        'R015 build verifier')

r014_identity = (
    'printf \'rev3_5_r014_variant=%s\\n\' "$REV3_5_R014_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r015_identity = r014_identity + (
    'printf \'rev3_5_r015_variant=%s\\n\' "$REV3_5_R015_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r014_identity, r015_identity,
                        'R015 candidate identity')

r014_suite = (
    " ('OH REV3.5 R014 Publication Gated Content Reconcile',"
    "'selftest_v01800_oh_rev35_r014_publication_gated_content_reconcile.py'),\n")
r015_suite = r014_suite + (
    " ('OH REV3.5 R015 Periodic GC Attribution Observer',"
    "'selftest_v01800_oh_rev35_r015_periodic_gc_attribution_observer.py'),\n")
prebuild, _ = replace_once(prebuild, r014_suite, r015_suite,
                           'R015 prebuild suite')

# R015 is observation-only. It may wire one source file into xbuild and identity/selftests,
# but it must not rewrite the generated renderer, scheduler, rasterizer or control sources.
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  '.SetValue(', '.Invoke(', 'System.Diagnostics.StackTrace',
                  'UnityEngine.Profiling.Profiler', 'FlightCtrlState', 'OnFlyByWire',
                  'OnAutopilotUpdate'):
    if forbidden in observer:
        fail('observer-only contract violated: ' + forbidden)

P.write_text(project)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent_r014=' + R014)
print('r015=' + R015)
print('mode=MEASUREMENT_ONLY')
print('sample=10Hz GC.CollectionCount + GC.GetTotalMemory(false) + fixed 64-value ring')
print('renderer_counter_reflection=Gen2 event only; target binding max 1Hz')
print('full_gc_log=one line per observed Gen2 collection')
print('negative_attribution=terrain_heavy_idle when content/capture/resolve/publication/full-reconcile deltas are all zero')
print('renderer_change=0 scheduler_change=0 rasterizer_change=0 worker_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 publication_authority_change=0')
