#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
V7 = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py'
PREFIX = '[AERIS29 REV3.5 R019 HOTFIX1 VERIFY]'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
HF1 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


check(R.is_file(), 'renderer exists')
check(V7.is_file(), 'R007 verifier exists')
if not R.is_file() or not V7.is_file():
    raise SystemExit(1)

r = R.read_text()
v = V7.read_text()
check(R019 in r, 'R019 parent marker retained')
check(HF1 in r, 'Hotfix1 marker present')
check('oh_rev35_r019_hf1_variant=' in r, 'Hotfix1 runtime identity telemetry present')
check('rev35R019VisibleFoundationQueue.Count > 0 ||\n                    rev35R007FoundationQueue.Count > 0' in r,
      'R010 non-authoritative wake includes visible then hidden queue')
check('int r010QueueBacklog =\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r,
      'R010 adaptive backlog counts both queue halves')
check('Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r,
      'final backlog includes both queue halves')
check('Rev35R007FoundationQueueMaximum = 128' in r,
      'combined handoff hard cap remains exactly 128')
check('int combinedQueueCount = rev35R007FoundationQueue.Count +' in r and
      'rev35R019VisibleFoundationQueue.Count;' in r and
      'if (combinedQueueCount >= Rev35R007FoundationQueueMaximum)' in r,
      'runtime enforces total visible+hidden queue cap')
check('PendingEntryCommit pendingEntryCommit;' in r and
      'Queue<PendingEntryCommit>' not in r and 'List<PendingEntryCommit>' not in r,
      'single pending commit lane retained')
check('Rev35R004BudgetMaximumMilliseconds = 2.00' in r,
      'R004 2.00 ms maximum retained')
check('Rev35R004FrameGuardMediumMilliseconds = 15.0' in r and
      'Rev35R004FrameGuardSoftMilliseconds = 20.0' in r and
      'Rev35R004FrameGuardHardMilliseconds = 25.0' in r,
      'R004 frame guards retained')
check('legacy_queue_bound = (' in v and 'r019_combined_queue_bound = (' in v,
      'R007 verifier has legacy/exact-R019 queue-bound successor')
check("ck(legacy_queue_bound or r019_combined_queue_bound," in v,
      'R007 verifier admits only legacy or exact R019 combined bound')
check('operationHealthRev35R007Overflow++' in v,
      'R007 overflow observability remains required')
check('foundationComplete = rendered && r018VisibleGpuComplete;' in r,
      'R018 visible FRONT gate remains authoritative')

for forbidden in ('Task.Run(', 'new Thread(', 'ThreadPool.', 'GC.Collect(',
                  'WaitManagedPreparation', 'ResidentPreparedPresentation'):
    # Hotfix itself is queue/wake accounting only.
    app = (ROOT / 'Tools/apply_aeris29_rev3_5_salbutamol_r019_hotfix1_wake_backlog_integration.py').read_text()
    check(forbidden not in app, 'Hotfix applicator excludes ' + forbidden)

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
print('contract=visible priority queue participates in inherited R010 wake/backlog; total cap 128 and single commit lane retained')
