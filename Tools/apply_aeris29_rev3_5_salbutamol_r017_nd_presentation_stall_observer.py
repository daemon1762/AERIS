#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
REC = ROOT / 'Source/AERISFlightControl/Recording/AERISFlightDataRecorder.cs'
P = ROOT / 'Source/AERISFlightControl/AERISFlightControl.csproj'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 SALBUTAMOL SULFATE R017 ND PRESENTATION STALL OBSERVER]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R015 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R015_PERIODIC_GC_ATTRIBUTION_OBSERVER'
R016 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, O, REC, P, B, PRE):
    if not path.is_file():
        fail('required file missing: ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
recorder = REC.read_text()
project = P.read_text()
build = B.read_text()
prebuild = PRE.read_text()

if R014 not in renderer:
    fail('formal R014 generated renderer parent required before R017 overlay')
if R015 not in observer and R015 not in build:
    fail('R015 lineage identity missing')
if R016 not in recorder or ('REV3_5_R016_VARIANT="' + R016 + '"') not in build:
    fail('R016 isolation parent required before R017 overlay')
if R013 in renderer or 'REV3_5_R013_VARIANT=' in build or 'rev3_5_r013_variant=' in build:
    fail('rejected R013 experiment must remain absent')
if R017 not in observer or '[OH_REV3_5_R017_ND_PRESENT_STALL]' not in observer:
    fail('committed R017 observer source marker missing')

field_old = '''        long frontBufferSwaps;
        long blockedIncompleteSwaps;
'''
field_new = field_old + '''        // R017 observation-only exact blocker predicates. These counters mirror the
        // existing foundationComplete / cadence branches and never alter their result.
        long operationHealthRev35R017BlockedRenderedFalse;
        long operationHealthRev35R017BlockedFoundationFlag;
        long operationHealthRev35R017BlockedCoverage;
        long operationHealthRev35R017BlockedReadyFar;
        long operationHealthRev35R017CadenceSkips;
'''
renderer, _ = replace_once(renderer, field_old, field_new,
                           'R017 exact blocker counters')

branch_old = '''                else
                {
                    blockedIncompleteSwaps++;
                }
            }
            else if (refreshRequired)
            {
                skippedBackRenderFrames++;
            }
'''
branch_new = '''                else
                {
                    blockedIncompleteSwaps++;
                    // R017 mirrors the exact existing foundationComplete predicates.
                    // Multiple counters may advance for one blocked attempt by design.
                    if (!rendered) operationHealthRev35R017BlockedRenderedFalse++;
                    if (!visible.FoundationComplete)
                        operationHealthRev35R017BlockedFoundationFlag++;
                    if (lastBackFoundationCoverage < 0.999f)
                        operationHealthRev35R017BlockedCoverage++;
                    if (readyFar < visible.FarFoundationCount)
                        operationHealthRev35R017BlockedReadyFar++;
                }
            }
            else if (refreshRequired)
            {
                skippedBackRenderFrames++;
                operationHealthRev35R017CadenceSkips++;
            }
'''
renderer, _ = replace_once(renderer, branch_old, branch_new,
                           'R017 blocked/cadence branch instrumentation')

compile_line = '    <Compile Include="Terrain\\AERISR017NdPresentationStallObserver.cs" />\n'
if compile_line not in project:
    anchor = '    <Compile Include="Performance\\AERISR015PeriodicGcAttributionObserver.cs" />\n'
    project, _ = replace_once(project, anchor, anchor + compile_line,
                              'R017 xbuild compile include')

r016_var = 'REV3_5_R016_VARIANT="' + R016 + '"\n'
r017_var = r016_var + 'REV3_5_R017_VARIANT="' + R017 + '"\n'
build, _ = replace_once(build, r016_var, r017_var,
                        'R017 build identity variable')

r016_verify = (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r016_fdr_high_rate_diagnostics_isolation.py"\n')
r017_verify = r016_verify + (
    'PYTHONDONTWRITEBYTECODE=1 python3 '
    '"$ROOT/Tools/verify_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py"\n')
build, _ = replace_once(build, r016_verify, r017_verify,
                        'R017 build verifier')

r016_identity = (
    'printf \'rev3_5_r016_variant=%s\\n\' "$REV3_5_R016_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
r017_identity = r016_identity + (
    'printf \'rev3_5_r017_variant=%s\\n\' "$REV3_5_R017_VARIANT" >> '
    '"$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n')
build, _ = replace_once(build, r016_identity, r017_identity,
                        'R017 candidate identity')

r016_suite = (
    " ('OH REV3.5 R016 FDR High Rate Diagnostics Isolation',"
    "'selftest_v01800_oh_rev35_r016_fdr_high_rate_diagnostics_isolation.py'),\n")
r017_suite = r016_suite + (
    " ('OH REV3.5 R017 ND Presentation Stall Observer',"
    "'selftest_v01800_oh_rev35_r017_nd_presentation_stall_observer.py'),\n")
prebuild, _ = replace_once(prebuild, r016_suite, r017_suite,
                           'R017 prebuild suite')

# R017 may only count existing branch outcomes and observe them. No presentation,
# geometry, worker, controller, lifecycle, quality, range or cadence authority is added.
for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  '.SetValue(', '.Invoke(', 'WaitManagedPreparation',
                  'ResidentPreparedPresentation'):
    if forbidden in observer:
        fail('observer-only contract violated: ' + forbidden)

R.write_text(renderer)
P.write_text(project)
B.write_text(build)
PRE.write_text(prebuild)
print(PREFIX + ' APPLY PASS')
print('parent_r014=' + R014)
print('parent_r015=' + R015)
print('parent_r016=' + R016)
print('r017=' + R017)
print('mode=MEASUREMENT_ONLY_ND_PRESENTATION')
print('stall_threshold_s=0.25 with real pending presentation demand required')
print('blocked_predicates=!rendered,!visible.FoundationComplete,coverage<0.999,readyFar<requiredFar')
print('cadence_predicate=existing refreshRequired && !refreshAllowed branch')
print('nd_behavior_change=0 quality_change=0 10Hz_change=0 exact_range_change=0')
print('ap_change=0 fbw_change=0 protect_change=0 worker_change=0 publication_authority_change=0')
