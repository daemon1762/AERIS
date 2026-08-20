#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
PREFIX = '[OH REV3.5 R019 VISIBLE FAR COMMIT PRIORITY]'
R018 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R018_VISIBLE_FOUNDATION_PRESENTATION_GATE_SPLIT'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


check(R.is_file(), 'renderer exists')
if not R.is_file():
    raise SystemExit(1)
r = R.read_text()
check(R018 in r, 'R018 parent present')
check(R019 in r, 'R019 identity present')
check('foundationComplete = rendered && r018VisibleGpuComplete;' in r,
      'R019 does not replace R018 presentation gate')
check('RefreshRev35R019VisibleFarKeys(vessel.mainBody, tiles,' in r,
      'visible priority plan refreshed before content admission')
check('rev35R019VisibleFarKeys.Contains(tile.Key)' in r,
      'exact-visible FAR classification exists')
check('rev35R019VisibleFoundationQueue.Enqueue(cacheKey);' in r,
      'visible RenderReady queue exists')
check('if (!TryBeginRev35R019VisibleFoundationCommit())' in r and
      'TryBeginRev35R007QueuedFoundationCommit();' in r,
      'priority falls back to inherited hidden queue')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in r,
      '2.00 ms hard ceiling retained')
check('r019VisibleDeficit >= 2' in r and
      'Rev35R004BudgetOneHalfMilliseconds' in r,
      'multi-tile visible deficit selects at most inherited 1.50 request tier before guard')
check('r019VisibleDeficit == 1' in r and
      'Rev35R004BudgetOneMilliseconds' in r,
      'single visible deficit selects inherited 1.00 request tier before guard')

# Pure truth table for the intended queue/budget policy. Runtime code remains C#;
# this test fixes the policy without requiring Unity/KSP.
def choose_queue(visible_count, hidden_count):
    if visible_count > 0:
        return 'VISIBLE'
    if hidden_count > 0:
        return 'HIDDEN'
    return 'NONE'


def requested_budget(base, deficit, commit_work):
    value = base
    if commit_work and deficit >= 2:
        value = max(value, 1.50)
    elif commit_work and deficit == 1:
        value = max(value, 1.00)
    return min(value, 2.00)

check(choose_queue(5, 8) == 'VISIBLE',
      'truth table: visible RenderReady bypasses hidden overscan queue')
check(choose_queue(0, 8) == 'HIDDEN',
      'truth table: hidden queue proceeds when visible queue empty')
check(choose_queue(0, 0) == 'NONE',
      'truth table: no synthetic commit work is created')
check(requested_budget(0.50, 0, True) == 0.50,
      'truth table: no visible deficit means no budget boost')
check(requested_budget(0.50, 1, True) == 1.00,
      'truth table: one visible deficit requests 1.00 ms')
check(requested_budget(0.50, 2, True) == 1.50,
      'truth table: two visible deficits request 1.50 ms')
check(requested_budget(1.00, 6, True) == 1.50,
      'truth table: large visible deficit raises 1.00 to 1.50 ms')
check(requested_budget(2.00, 6, True) == 2.00,
      'truth table: existing 2.00 ms hard ceiling is never exceeded')
check(requested_budget(0.50, 6, False) == 0.50,
      'truth table: upstream-only deficit without commit work does not boost')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
    'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    # Check the R019 implementation neighbourhood rather than the whole inherited file.
    start = r.find('const string Rev35R019Variant')
    end = r.find('void MeasureVisibleFoundationGpuReadiness', start)
    neighbourhood = r[start:end] if start >= 0 and end > start else ''
    check(forbidden not in neighbourhood,
          'R019 implementation excludes ' + forbidden)

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' PASS %d/%d' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
print('policy=visible RenderReady first; 1.00/1.50ms urgency request; R004 guard/2.00ms cap remains authority')
