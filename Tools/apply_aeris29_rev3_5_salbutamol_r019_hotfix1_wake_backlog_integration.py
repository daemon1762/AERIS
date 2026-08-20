#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
V7 = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py'
V10 = ROOT / 'Tools/verify_aeris27_rev3_5_salbutamol_r010_continuous_commit_stream.py'
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


def insert_after_unique_line(text, marker, insertion, already_marker, label):
    """Insert after one generated C# source line without depending on Python escape spelling."""
    if already_marker in text:
        return text, False
    count = text.count(marker)
    if count != 1:
        fail('%s anchor count=%d' % (label, count))
    start = text.find(marker)
    end = text.find('\n', start)
    if end < 0:
        fail('%s line terminator missing' % label)
    end += 1
    return text[:end] + insertion + text[end:], True


for path in (R, V7, V10):
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

# Publish only an identity witness. Do not anchor on the Python applicator's escaped "\\n"
# spelling: inspect the already-generated C# source line and insert after that physical line.
telemetry_marker = '                "; oh_rev35_r019_variant=" + Rev35R019Variant +'
telemetry_insert = (
    '                "; oh_rev35_r019_hf1_variant=" + Rev35R019Hotfix1Variant +\n')
renderer, _ = insert_after_unique_line(
    renderer,
    telemetry_marker,
    telemetry_insert,
    'oh_rev35_r019_hf1_variant=',
    'Hotfix1 telemetry identity')

# Fail closed before writing any runtime source if the complete Hotfix1 shape is not present.
required_runtime = (
    HF1,
    'Rev35R019Hotfix1Variant',
    'oh_rev35_r019_hf1_variant=',
    'rev35R019VisibleFoundationQueue.Count > 0 ||\n                    rev35R007FoundationQueue.Count > 0',
    'int r010QueueBacklog =\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\n                Math.Max(0, rev35R007FoundationQueue.Count);',
    'Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\n                Math.Max(0, rev35R007FoundationQueue.Count);',
)
missing_runtime = [token for token in required_runtime if token not in renderer]
if missing_runtime:
    fail('runtime contract incomplete: ' + ', '.join(missing_runtime))
R.write_text(renderer)

# Historical R007 verifier froze the pre-split implementation shape (one queue Count >= 128).
# R019 retains the SAME total hard cap 128 across visible+hidden queues. Admit only that exact
# successor form; do not weaken the overflow requirement.
v7 = V7.read_text()
legacy7 = '''ck('rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum' in queue and
   'operationHealthRev35R007Overflow++' in queue,
   'queue is bounded and overflow observable')
'''
successor7 = '''legacy_queue_bound = (
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
if successor7 not in v7:
    count = v7.count(legacy7)
    if count != 1:
        fail('R007 queue-bound verifier legacy anchor count=%d' % count)
    v7 = v7.replace(legacy7, successor7, 1)
    V7.write_text(v7)
    print(PREFIX + ' patched historical R007 queue-bound assertion')
else:
    print(PREFIX + ' historical R007 queue-bound successor already present')

# Historical R010 verifier froze the inherited one-queue wake/backlog implementation.
# R019 Hotfix1 retains the same continuous-pump contract but the bounded handoff is now
# visible-priority + hidden. Admit only legacy or the exact two-queue successor shape.
v10 = V10.read_text()
legacy10_wake = '''wake = re.search(
    r'if \\(pendingEntryCommit != null \\|\\| rasterizer\\.CompletedCount > 0 \\|\\|\\s*'
    r'rev35R007FoundationQueue\\.Count > 0\\)', r, re.S)
ck(wake is not None, 'R007 FIFO wakes non-authoritative staged pump')
'''
successor10_wake = '''legacy_wake = re.search(
    r'if \\(pendingEntryCommit != null \\|\\| rasterizer\\.CompletedCount > 0 \\|\\|\\s*'
    r'rev35R007FoundationQueue\\.Count > 0\\)', r, re.S)
r019_wake = re.search(
    r'if \\(pendingEntryCommit != null \\|\\| rasterizer\\.CompletedCount > 0 \\|\\|\\s*'
    r'rev35R019VisibleFoundationQueue\\.Count > 0 \\|\\|\\s*'
    r'rev35R007FoundationQueue\\.Count > 0\\)', r, re.S)
ck(legacy_wake is not None or r019_wake is not None,
   'R007 legacy FIFO or exact R019 visible+hidden queues wake staged pump')
'''
if successor10_wake not in v10:
    if v10.count(legacy10_wake) != 1:
        fail('R010 wake verifier legacy anchor count=%d' % v10.count(legacy10_wake))
    v10 = v10.replace(legacy10_wake, successor10_wake, 1)

legacy10_budget = '''ck('int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);' in r,
   'R007 FIFO included in adaptive backlog')
'''
successor10_budget = '''legacy_r010_backlog = (
    'int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);' in r)
r019_r010_backlog = (
    'int r010QueueBacklog =\\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r)
ck(legacy_r010_backlog or r019_r010_backlog,
   'R007 legacy FIFO or exact R019 visible+hidden queues included in adaptive backlog')
'''
if successor10_budget not in v10:
    if v10.count(legacy10_budget) != 1:
        fail('R010 adaptive backlog verifier legacy anchor count=%d' %
             v10.count(legacy10_budget))
    v10 = v10.replace(legacy10_budget, successor10_budget, 1)

legacy10_final = '''ck('(pendingEntryCommit == null ? 0 : 1) +\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r,
   'main commit final backlog includes R007 FIFO')
'''
successor10_final = '''legacy_final_backlog = (
    '(pendingEntryCommit == null ? 0 : 1) +\\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r)
r019_final_backlog = (
    '(pendingEntryCommit == null ? 0 : 1) +\\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r)
ck(legacy_final_backlog or r019_final_backlog,
   'main commit final backlog includes R007 legacy or exact R019 visible+hidden queues')
'''
if successor10_final not in v10:
    if v10.count(legacy10_final) != 1:
        fail('R010 final backlog verifier legacy anchor count=%d' % v10.count(legacy10_final))
    v10 = v10.replace(legacy10_final, successor10_final, 1)

# Validate successor clauses before writing the historical verifier.
for token in (
    'legacy_wake = re.search(', 'r019_wake = re.search(',
    'legacy_r010_backlog = (', 'r019_r010_backlog = (',
    'legacy_final_backlog = (', 'r019_final_backlog = (',
    'rev35R019VisibleFoundationQueue'):
    if token not in v10:
        fail('R010 successor verifier incomplete: ' + token)
compile(v10, str(V10), 'exec')
V10.write_text(v10)
print(PREFIX + ' historical R010 wake/backlog successor verified')

print(PREFIX + ' APPLY PASS')
print('wake=pending OR raster completed OR visible priority queue OR hidden R007 queue')
print('backlog=visible priority + hidden R007; total queue hard cap remains 128')
print('historical=R007 bound + R010 wake/backlog admit legacy or exact R019 successor only')
print('commit_lane=1 unchanged; R004 max=2.00ms unchanged; worker/rasterizer/presentation authority unchanged')
