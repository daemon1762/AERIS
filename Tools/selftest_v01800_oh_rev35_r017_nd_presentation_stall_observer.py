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
PREFIX = '[OH REV3.5 R017 ND PRESENTATION STALL OBSERVER]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R016 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R016_FDR_HIGH_RATE_DIAGNOSTICS_ISOLATION'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'

for path in (R, O, REC, P, B, PRE):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O.read_text()
recorder = REC.read_text()
project = P.read_text()
build = B.read_text()
prebuild = PRE.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


check(R014 in renderer, 'R014 publication-gated renderer parent retained')
check(R016 in recorder, 'R016 recorder isolation parent retained')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check(R017 in observer and '[OH_REV3_5_R017_ND_PRESENT_STALL]' in observer,
      'R017 observer identity and telemetry prefix present')
check('const float SampleIntervalSeconds = 0.10f;' in observer,
      'observer samples at nominal 10 Hz')
check('const float StallThresholdSeconds = 0.25f;' in observer,
      'stall threshold is 0.25 seconds')
check('frontAge >= StallThresholdSeconds' in observer,
      'old committed FRONT age participates in stall predicate')
check('frontValid && frontPresented && frontLatched && demandPending' in observer,
      'stall requires an actually presented retained FRONT plus pending demand')
for token in ('blockedSinceSwap > 0', 'skippedSinceSwap > 0', 'motionSinceSwap > 0',
              'revisionMismatch', 'publicationPending', '!requestedReady'):
    check(token in observer, 'pending-demand predicate includes ' + token)

for field in ('operationHealthRev35R017BlockedRenderedFalse',
              'operationHealthRev35R017BlockedFoundationFlag',
              'operationHealthRev35R017BlockedCoverage',
              'operationHealthRev35R017BlockedReadyFar',
              'operationHealthRev35R017CadenceSkips'):
    check(('long ' + field + ';') in renderer,
          'renderer owns observation counter ' + field)
    check(('RendererField("' + field + '")') in observer,
          'observer reads ' + field)

check('if (!rendered) operationHealthRev35R017BlockedRenderedFalse++;' in renderer,
      'blocked reason mirrors existing rendered predicate')
check('if (!visible.FoundationComplete)' in renderer and
      'operationHealthRev35R017BlockedFoundationFlag++;' in renderer,
      'blocked reason mirrors existing FoundationComplete predicate')
check('if (lastBackFoundationCoverage < 0.999f)' in renderer and
      'operationHealthRev35R017BlockedCoverage++;' in renderer,
      'blocked reason mirrors existing coverage predicate')
check('if (readyFar < visible.FarFoundationCount)' in renderer and
      'operationHealthRev35R017BlockedReadyFar++;' in renderer,
      'blocked reason mirrors existing readyFar predicate')
check('else if (refreshRequired)' in renderer and
      'skippedBackRenderFrames++;\n                operationHealthRev35R017CadenceSkips++;' in renderer,
      'cadence observation stays inside existing refreshRequired skip branch')

check('AERISLogger.Info(LogPrefix + " START' in observer,
      'observer logs one stall start record')
check('AERISLogger.Info(LogPrefix + " END' in observer,
      'observer logs stall recovery/end record')
for token in ('blocked_rendered_false=', 'blocked_foundation_flag=', 'blocked_coverage=',
              'blocked_ready_far=', 'cadence_skip=', 'content_tick_since_swap=',
              'content_capture_since_swap=', 'resolve_since_swap=',
              'r014_full_reconcile_since_swap=', 'r014_publications_since_swap=',
              'publication_pending='):
    check(token in observer, 'stall record exports ' + token)

check('Terrain\\AERISR017NdPresentationStallObserver.cs' in project,
      'R017 observer is compiled')
check('REV3_5_R017_VARIANT="' + R017 + '"' in build,
      'R017 build identity variable present')
check('rev3_5_r017_variant=%s' in build,
      'R017 candidate identity emission present')
check('verify_aeris29_rev3_5_salbutamol_r017_nd_presentation_stall_observer.py' in build,
      'R017 verifier wired into build')
check('selftest_v01800_oh_rev35_r017_nd_presentation_stall_observer.py' in prebuild,
      'R017 selftest wired into prebuild')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  '.SetValue(', '.Invoke(', 'FlightCtrlState', 'OnFlyByWire',
                  'OnAutopilotUpdate', 'Graphics.Blit(', 'RenderBackBuffer(',
                  'SwapFrontAndBack(', 'PresentFrontDirect('):
    check(forbidden not in observer, 'observer owns no authority: excludes ' + forbidden)

# Instrumentation may count branch outcomes only; it must not alter the formal gates.
check('foundationComplete = rendered && visible.FoundationComplete &&\n                    lastBackFoundationCoverage >= 0.999f &&\n                    readyFar >= visible.FarFoundationCount;' in renderer,
      'formal complete-foundation gate remains byte-exact')
check('bool refreshAllowed = ShouldRefreshBackBuffer(visible, refreshRequired);' in renderer,
      'formal refresh admission authority remains unchanged')
check('if (foundationComplete)\n                {\n                    SwapFrontAndBack(' in renderer,
      'successful swap authority remains foundationComplete only')

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)

if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
