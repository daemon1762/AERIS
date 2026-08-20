#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PRE = ROOT / 'Tools/run_v01800_operation_health_pass3_prebuild.py'
PREFIX = '[AERIS29 REV3.5 R019 VISIBLE FAR COMMIT PRIORITY VERIFY]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method_body(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
    depth = 0; state = 'code'; i = op
    while i < len(text):
        c = text[i]; n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return text[start:i + 1]
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
    return ''


for path in (R, B, PRE):
    check(path.is_file(), 'file exists: ' + str(path.relative_to(ROOT)))
if not all(path.is_file() for path in (R, B, PRE)):
    raise SystemExit(1)
renderer = R.read_text(); build = B.read_text(); prebuild = PRE.read_text()

check(R018 in renderer, 'R018 presentation-gate parent retained')
check(R019 in renderer, 'R019 renderer identity present')
check('foundationComplete = rendered && r018VisibleGpuComplete;' in renderer,
      'R018 FRONT presentation gate remains authoritative')
check('bool r018OverscanGpuComplete = visible.FoundationComplete &&' in renderer,
      'hidden overscan remains witness/preparation truth')

refresh = method_body(renderer, '        void RefreshRev35R019VisibleFarKeys(')
check(bool(refresh), 'R019 exact visible-key planner helper resolved')
check('AERISTerrainViewportFoundationPlanner.Build(body, environmentHash,' in refresh,
      'R019 reuses canonical Gate3.1 viewport planner')
check('visibleRangeMeters' in refresh and 'plan.FarKeys' in refresh,
      'R019 priority keys come from exact visible FAR plan')
check('rev35R019VisibleFarKeys.Add(plan.FarKeys[i]);' in refresh,
      'R019 stores exact visible FAR TileKeys only')

queue = method_body(renderer,
    '        void QueueRev35R007FoundationField(AERISTerrainHeightTile tile,')
check(bool(queue), 'R007 handoff queue resolved')
check('rev35R019VisibleFarKeys.Contains(tile.Key)' in queue,
      'exact visible FAR is detected before handoff enqueue')
check('rev35R019VisibleFoundationQueue.Enqueue(cacheKey);' in queue,
      'visible RenderReady FAR enters dedicated priority queue')
check('rev35R007FoundationQueue.Enqueue(cacheKey);' in queue,
      'hidden/non-visible requested FAR remains on inherited R007 queue')
check('combinedQueueCount >= Rev35R007FoundationQueueMaximum' in queue,
      'combined priority+legacy queue retains R007 hard bound')
check('rev35R007FoundationQueued.Add(cacheKey);' in queue,
      'shared R007 duplicate authority retained')

priority = method_body(renderer,
    '        bool TryBeginRev35R019VisibleFoundationCommit()')
check(bool(priority), 'R019 visible priority commit helper resolved')
check('rev35R019VisibleFoundationQueue.Dequeue()' in priority,
      'visible queue is consumed explicitly')
check('!contentSnapshotValid || !requested.Contains(cacheKey)' in priority,
      'R003/R007 current-view stale gate retained')
check('entries.ContainsKey(cacheKey)' in priority,
      'already-committed suppression retained')
check('renderReadyFields.TryGetValue(cacheKey, out field)' in priority,
      'priority lane admits only already RenderReady fields')
check('TryBeginPendingEntryCommit(field)' in priority,
      'priority lane enters inherited single pending commit lane')
check('operationHealthRev35R019HiddenQueueBypassed++' in priority,
      'hidden queue bypass is observable')

pump = method_body(renderer,
    '        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
check(bool(pump), 'staged main-thread commit pump resolved')
check('TryBeginRev35R019VisibleFoundationCommit()' in pump and
      'TryBeginRev35R007QueuedFoundationCommit()' in pump and
      pump.find('TryBeginRev35R019VisibleFoundationCommit()') <
      pump.find('TryBeginRev35R007QueuedFoundationCommit()'),
      'visible RenderReady queue is admitted before hidden R007 queue')
check('PendingEntryCommit pendingEntryCommit;' in renderer and
      'List<PendingEntryCommit>' not in renderer and
      'Queue<PendingEntryCommit>' not in renderer,
      'single serial PendingEntryCommit authority remains unchanged')

budget = method_body(renderer,
    '        double ResolveRev35R004CommitBudget(bool steadyCommitProfile)')
check(bool(budget), 'R004 adaptive budget helper resolved')
check('operationHealthRev35R018VisibleRequiredFar -' in budget and
      'operationHealthRev35R018VisibleReadyFar' in budget,
      'R019 budget urgency is driven only by exact-visible deficit')
check('r019VisibleDeficit >= 2' in budget and
      'Rev35R004BudgetOneHalfMilliseconds' in budget,
      'visible deficit >=2 requests existing 1.50 ms tier')
check('r019VisibleDeficit == 1' in budget and
      'Rev35R004BudgetOneMilliseconds' in budget,
      'single visible deficit requests existing 1.00 ms tier')
frame_guard = budget.find('// Real unscaled Unity frame time is only a protective ceiling.')
r019_budget = budget.find('int r019VisibleDeficit')
check(0 <= r019_budget < frame_guard,
      'existing R004 frame guard remains downstream and authoritative')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in renderer,
      'R004 2.00 ms hard ceiling retained')
check('Rev35R004FrameGuardMediumMilliseconds = 15.0' in renderer and
      'Rev35R004FrameGuardSoftMilliseconds = 20.0' in renderer and
      'Rev35R004FrameGuardHardMilliseconds = 25.0' in renderer,
      'R004 15/20/25 ms frame guards retained')

for token in (
    'oh_rev35_r019_variant=',
    'oh_rev35_r019_visible_keys=',
    'oh_rev35_r019_visible_priority_queue=',
    'oh_rev35_r019_visible_priority_peak=',
    'oh_rev35_r019_visible_priority_queued=',
    'oh_rev35_r019_visible_priority_begin=',
    'oh_rev35_r019_hidden_queue_bypass=',
    'oh_rev35_r019_visible_deficit=',
    'oh_rev35_r019_budget_100=',
    'oh_rev35_r019_budget_150=',
):
    check(token in renderer, 'runtime telemetry ' + token)

check('REV3_5_R019_VARIANT="' + R019 + '"' in build,
      'build records R019 identity')
check('verify_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py' in build,
      'build invokes R019 verifier')
check('rev3_5_r019_variant=%s' in build,
      'candidate identity records R019')
check('selftest_v01800_oh_rev35_r019_visible_far_commit_priority.py' in prebuild,
      'R019 selftest wired into prebuild')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
    'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    check(forbidden not in refresh and forbidden not in priority,
          'R019 helpers exclude rejected mechanism: ' + forbidden)

applicator = (ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_visible_far_commit_priority.py').read_text()
for forbidden_path in (
    'AERISWorkerScheduler.cs', 'AERISTerrainGpuTileRasterizer.cs',
    'Source/AERISFlightControl/Autopilot', 'Source/AERISFlightControl/AA',
    'Source/AERISFlightControl/Protect', 'Source/AERISFlightControl/Landing'):
    check(forbidden_path not in applicator,
          'R019 applicator does not target ' + forbidden_path)

check('internal const float FixedNavigationDisplayUpdateHz = 10f' in
      (ROOT / 'Source/AERISFlightControl/Settings/AERISSettings.cs').read_text(),
      'fixed visible 10 Hz authority retained')
check('RenderTextureFormat.ARGB32' in renderer and 'FilterMode.Bilinear' in renderer,
      'Golden ARGB32/Bilinear retained')
check('HistoryOverscanScale = 1.35f' in renderer and
      'MaximumHistorySurfaceRangeMeters = 250000f' in renderer,
      'hidden 1.35x/250km overscan authority unchanged')

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
print('contract=exact-visible RenderReady FAR first; adaptive budget request only within existing R004 rails')
print('presentation=R018 unchanged; single commit lane retained; no worker/rasterizer/AP/quality/range changes')
