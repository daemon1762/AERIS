#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
V7 = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py'
PREFIX = '[AERIS29 REV3.5 R019 HOTFIX1 WAKE/BACKLOG INTEGRATION]'
R019 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY'
HF1 = 'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_HOTFIX1_VISIBLE_QUEUE_WAKE_BACKLOG_INTEGRATION'


def fail(message):
    raise SystemExit(PREFIX + ' FAIL ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor count=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, V7):
    if not path.is_file():
        fail('missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
if R019 not in renderer:
    fail('R019 generated overlay required before Hotfix1')

# Hotfix identity is source-visible so the incremental DLL can be verified directly.
if HF1 not in renderer:
    old = '        const string Rev35R019Variant = "' + R019 + '";\n'
    new = old + (
        '        // ' + HF1 + ': include the exact-visible priority queue in the inherited\n'
        '        // R010 wake/backlog accounting without changing the single commit lane.\n'
        '        const string Rev35R019Hotfix1Variant = "' + HF1 + '";\n')
    renderer, _ = replace_once(renderer, old, new, 'Hotfix1 identity')

# R010 originally wakes the non-authoritative commit pump for pending/raster/R007 queue.
# R019 split exact-visible FAR into a second queue, so that queue must participate in the
# same wake condition or a visible-only backlog can sleep until an unrelated content tick.
wake_old = '''                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||
                    rev35R007FoundationQueue.Count > 0)
'''
wake_new = '''                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||
                    rev35R019VisibleFoundationQueue.Count > 0 ||
                    rev35R007FoundationQueue.Count > 0)
'''
renderer, _ = replace_once(renderer, wake_old, wake_new,
                           'Hotfix1 R010 non-authoritative wake')

# R010's adaptive-budget backlog must count both halves of the still-single bounded handoff
# queue. This changes no budget rail: R004 still owns 0.50/1.00/1.50/2.00 ms and frame guard.
budget_old = '''            int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);
'''
budget_new = '''            int r010QueueBacklog =
                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +
                Math.Max(0, rev35R007FoundationQueue.Count);
'''
renderer, _ = replace_once(renderer, budget_old, budget_new,
                           'Hotfix1 R010 adaptive backlog')

final_old = '''            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1) +
                Math.Max(0, rev35R007FoundationQueue.Count);
'''
final_new = '''            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1) +
                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +
                Math.Max(0, rev35R007FoundationQueue.Count);
'''
renderer, _ = replace_once(renderer, final_old, final_new,
                           'Hotfix1 final backlog')

# Publish only an identity witness; existing R019 counters already expose queue/budget effect.
telemetry_old = (
    '                "; oh_rev35_r019_variant=" + Rev35R019Variant +\\n')
telemetry_new = telemetry_old + (
    '                "; oh_rev35_r019_hf1_variant=" + Rev35R019Hotfix1Variant +\\n')
renderer, _ = replace_once(renderer, telemetry_old, telemetry_new,
                           'Hotfix1 telemetry identity')
R.write_text(renderer)

# Historical R007 verifier froze the pre-split implementation shape (one queue Count >= 128).
# R019 retains the SAME total hard cap 128 across visible+hidden queues. Admit only that exact
# successor form; do not weaken the overflow requirement.
v = V7.read_text()
legacy = '''ck('rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum' in queue and
   'operationHealthRev35R007Overflow++' in queue,
   'queue is bounded and overflow observable')
'''
successor = '''legacy_queue_bound = (
    'rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum' in queue and
    'operationHealthRev35R007Overflow++' in queue)
r019_combined_queue_bound = (
    'AERIS29_REV3_5_SALBUTAMOL_SULFATE_R019_VISIBLE_FAR_COMMIT_PRIORITY' in r and
    'int combinedQueueCount = rev35R007FoundationQueue.Count +' in queue and
    'rev35R019VisibleFoundationQueue.Count;' in queue and
    'if (combinedQueueCount >= Rev35R007FoundationQueueMaximum)' in queue and
    'operationHealthRev35R007Overflow++' in queue)
ck(legacy_queue_bound or r019_combined_queue_bound,
   'queue is R007 legacy bounded or exact R019 combined-visible/hidden bounded')
'''
if successor not in v:
    count = v.count(legacy)
    if count != 1:
        fail('R007 queue-bound verifier legacy anchor count=%d' % count)
    v = v.replace(legacy, successor, 1)
    V7.write_text(v)
    print(PREFIX + ' patched historical R007 queue-bound assertion')
else:
    print(PREFIX + ' historical R007 queue-bound successor already present')

print(PREFIX + ' APPLY PASS')
print('wake=pending OR raster completed OR visible priority queue OR hidden R007 queue')
print('backlog=visible priority + hidden R007; total queue hard cap remains 128')
print('commit_lane=1 unchanged; R004 max=2.00ms unchanged; worker/rasterizer/presentation authority unchanged')
