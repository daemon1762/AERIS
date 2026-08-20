#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
O17 = ROOT / 'Source/AERISFlightControl/Terrain/AERISR017NdPresentationStallObserver.cs'
PLANNER = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainViewportFoundationPlanner.cs'
S = ROOT / 'Source/AERISFlightControl/Settings/AERISSettings.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
A = ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py'
PREFIX = '[OH REV3.5 R018 VISIBLE FOUNDATION PRESENTATION GATE SPLIT]'
R013 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R013_STABLE_CONTENT_SNAPSHOT_RECONCILE'
R014 = 'AERIS28_REV3_5_SALBUTAMOL_SULFATE_R014_PUBLICATION_GATED_CONTENT_RECONCILE'
R017 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R017_ND_PRESENTATION_STALL_OBSERVER'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'

for path in (R, O17, PLANNER, S, B, PRE, A):
    if not path.is_file():
        raise SystemExit(PREFIX + ' FAIL missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
observer = O17.read_text()
planner = PLANNER.read_text()
settings = S.read_text()
build = B.read_text()
prebuild = PRE.read_text()
applicator = A.read_text()
checks = []


def check(value, label):
    checks.append((bool(value), label))


def statement(text, token):
    start = text.find(token)
    if start < 0:
        return ''
    end = text.find(';', start)
    if end < 0:
        return ''
    return text[start:end + 1]


def block_bounds(text, token):
    start = text.find(token)
    if start < 0:
        return ''
    brace = text.find('{', start)
    if brace < 0:
        return ''
    depth = 0
    state = 'code'
    i = brace
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{':
                depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return text[start:i + 1]
            i += 1
            continue
        if state == 'line':
            if c == '\n':
                state = 'code'
            i += 1
            continue
        if state == 'block':
            if c == '*' and n == '/':
                state = 'code'; i += 2; continue
            i += 1
            continue
        if state == 'string':
            if c == '\\':
                i += 2; continue
            if c == '"':
                state = 'code'
            i += 1
            continue
        if state == 'char':
            if c == '\\':
                i += 2; continue
            if c == "'":
                state = 'code'
            i += 1
            continue
    return ''


check(R014 in renderer, 'R014 publication-gated parent retained')
check(R017 in observer and '[OH_REV3_5_R017_ND_PRESENT_STALL]' in observer,
      'R017 stall observer retained')
check(R013 not in renderer and 'REV3_5_R013_VARIANT=' not in build,
      'rejected R013 remains absent')
check(('const string Rev35R018Variant = "' + R018 + '";') in renderer,
      'R018 renderer identity present')

check('internal const int GuardRingTiles = 1;' in planner,
      'canonical Gate3.1 one-tile guard ring retained')
check('AERISNdMapProjection.Create(body,' in planner,
      'canonical planner uses exact ND projection')
check('projection.UnprojectGuiToLatitudeLongitude(u, v,' in planner,
      'canonical planner samples actual viewport')
check('AERISTerrainTileLod.Far' in planner,
      'canonical planner owns FAR visible foundation keys')

visible_helper = block_bounds(renderer, 'void MeasureVisibleFoundationGpuReadiness(')
check(bool(visible_helper), 'visible FAR readiness helper resolved')
check('AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,' in visible_helper,
      'R018 reuses canonical viewport-foundation planner')
check('visibleRangeMeters' in visible_helper,
      'visible planner receives exact user-visible range')
check('plan.FarKeys.Length' in visible_helper,
      'visible gate requires full canonical FAR key set')
check('tile.Key.Equals(requiredKey)' in visible_helper,
      'visible required keys match exact captured tile keys')
check('current.CoverageFraction >= 0.999f' in visible_helper,
      'visible required key needs exact-current complete GPU Entry')
check('fallbackEntry' not in visible_helper and 'fallbackEntriesScratch' not in visible_helper,
      'visible gate never promotes fallback Entry to complete readiness')

measure_call = renderer.find('MeasureVisibleFoundationGpuReadiness(vessel.mainBody, tiles,')
full_measure = renderer.find('contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,')
content_visible = renderer.find('contentVisible = visible;', full_measure)
check(full_measure >= 0 and measure_call > full_measure and measure_call < content_visible,
      'visible planner runs only in inherited full content reconcile')
check(renderer.count('MeasureVisibleFoundationGpuReadiness(vessel.mainBody, tiles,') == 1,
      'visible planner has one reconcile-time call site only')

front_gate = statement(renderer, 'foundationComplete = rendered')
check(bool(front_gate), 'FRONT gate statement resolved')
check('rendered && r018VisibleGpuComplete' in front_gate,
      'FRONT gate is rendered BACK + visible plan completeness')
check('visible.FoundationComplete' not in front_gate and
      'lastBackFoundationCoverage' not in front_gate and
      'visible.FarFoundationCount' not in front_gate,
      'hidden overscan CPU/GPU readiness removed from FRONT admission')

recovery_gate = statement(renderer, 'bool readyFoundationNow =')
check('r018VisibleGpuComplete' in recovery_gate,
      'presentation recovery uses cached visible plan completeness')
check('visible.FoundationComplete' not in recovery_gate and
      'lastBackFoundationCoverage' not in recovery_gate and
      'visible.FarFoundationCount' not in recovery_gate,
      'recovery no longer waits on hidden overscan readiness')

check('bool r018OverscanGpuComplete = visible.FoundationComplete &&' in renderer and
      'lastBackFoundationCoverage >= 0.999f' in renderer and
      'readyFar >= visible.FarFoundationCount' in renderer,
      'old overscan truth retained as witness telemetry only')
check('if (!r018OverscanGpuComplete)' in renderer and
      'operationHealthRev35R018OverscanHolAvoided++;' in renderer,
      'successful visible swap records avoided overscan HOL')
check('operationHealthRev35R018VisiblePlanValid = false;' in renderer and
      'operationHealthRev35R018VisibleRequiredFar = 0;' in renderer,
      'content reset invalidates cached visible readiness')

for token in (
    'oh_rev35_r018_variant=',
    'oh_rev35_r018_visible_plan_valid=',
    'oh_rev35_r018_visible_required_far=',
    'oh_rev35_r018_visible_ready_far=',
    'oh_rev35_r018_visible_coverage=',
    'oh_rev35_r018_overscan_required_far=',
    'oh_rev35_r018_overscan_ready_far=',
    'oh_rev35_r018_overscan_hol_avoided='):
    check(token in renderer, 'runtime telemetry ' + token)

check('const float HistoryOverscanScale = 1.35f;' in renderer,
      'inherited 1.35x temporal overscan unchanged')
check('const float MaximumHistorySurfaceRangeMeters = 250000f;' in renderer,
      'inherited 250km hidden overscan cap unchanged')
check('ResolveHistorySurfaceRange(rangeMeters)' in renderer,
      'hidden temporal overscan remains active')
check('FixedNavigationDisplayUpdateHz = 10f' in settings,
      'fixed visible 10Hz authority retained')
check('160000f' in settings, 'exact 160km authority retained')
check('contentRetryDue || rev35R014PublicationPendingBeforeTick;' in renderer,
      'R014 publication wake authority retained')
check('if (rev35R014ReconcileRan)' in renderer,
      'R014 full-reconcile prune gate retained')
check('operationHealthRev35R017BlockedCoverage' in renderer and
      'operationHealthRev35R017BlockedReadyFar' in renderer,
      'R017 blocker counters retained for A/B evidence')

check(('REV3_5_R018_VARIANT="' + R018 + '"') in build,
      'R018 build identity variable')
check('rev3_5_r018_variant=%s' in build,
      'R018 candidate identity append')
check('verify_aeris29_rev3_5_salbutamol_r018_visible_foundation_presentation_gate_split.py'
      in build, 'R018 verifier wired into build')
check('selftest_v01800_oh_rev35_r018_visible_foundation_presentation_gate_split.py'
      in prebuild, 'R018 selftest wired into prebuild')

for forbidden_path in (
    'Autopilot/',
    'FlightCtrlState',
    'OnFlyByWire',
    'AERISWorkerScheduler.cs',
    'AERISTerrainGpuTileRasterizer.cs'):
    check(forbidden_path not in applicator,
          'applicator does not touch ' + forbidden_path)

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation',
                  'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in renderer, 'rejected mechanism absent: ' + forbidden)


def front_admitted(rendered, visible_plan_valid, visible_ready, visible_required,
                   overscan_cpu_complete, overscan_ready, overscan_required):
    del overscan_cpu_complete, overscan_ready, overscan_required
    return (rendered and visible_plan_valid and visible_required > 0 and
            visible_ready >= visible_required)


check(front_admitted(True, True, 87, 87, True, 115, 115),
      'truth table: visible-complete FRONT admitted')
check(front_admitted(True, True, 87, 87, False, 114, 115),
      'truth table: hidden overscan CPU/GPU miss no longer blocks')
check(not front_admitted(True, True, 86, 87, True, 115, 115),
      'truth table: one visible FAR miss still blocks')
check(not front_admitted(True, False, 87, 87, True, 115, 115),
      'truth table: invalid visible plan fails closed')
check(not front_admitted(False, True, 87, 87, True, 115, 115),
      'truth table: failed BACK render still blocks')

failed = []
for ok, label in checks:
    print(('[PASS] ' if ok else '[FAIL] ') + label)
    if not ok:
        failed.append(label)
if failed:
    raise SystemExit(PREFIX + ' FAIL: ' + ', '.join(failed))
print(PREFIX + ' PASS %d/%d' % (len(checks), len(checks)))
