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


def replace_python_assertion_block(text, label, new_block, successor_marker,
                                   include_assignment=None):
    """Replace one historical verifier assertion by its human-readable label.

    This intentionally ignores whitespace/escape spelling so it works on both the
    pristine historical verifier and the already-materialized R018 local verifier.
    """
    if successor_marker in text:
        return text, False
    needle = "'" + label + "'"
    if text.count(needle) != 1:
        fail('historical verifier label count=%d label=%s' %
             (text.count(needle), label))
    label_pos = text.find(needle)
    if include_assignment:
        start = text.rfind('\n' + include_assignment, 0, label_pos)
        if start < 0:
            fail('historical verifier assignment start missing: ' + include_assignment)
        start += 1
    else:
        start = text.rfind('\nck(', 0, label_pos)
        if start < 0:
            fail('historical verifier ck start missing: ' + label)
        start += 1
    line_end = text.find('\n', label_pos)
    if line_end < 0:
        line_end = len(text)
    else:
        line_end += 1
    return text[:start] + new_block + text[line_end:], True


for path in (R, V7, V10):
    if not path.is_file():
        fail('missing ' + str(path.relative_to(ROOT)))

renderer = R.read_text()
if R019 not in renderer:
    fail('R019 generated overlay required before Hotfix1')

if HF1 not in renderer:
    old = '        const string Rev35R019Variant = "' + R019 + '";\n'
    new = old + (
        '        // ' + HF1 + ': include the exact-visible priority queue in the inherited\n'
        '        // R010 wake/backlog accounting without changing the single commit lane.\n'
        '        const string Rev35R019Hotfix1Variant = "' + HF1 + '";\n')
    renderer, _ = replace_once(renderer, old, new, 'Hotfix1 identity')

wake_old = '''                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||
                    rev35R007FoundationQueue.Count > 0)
'''
wake_new = '''                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||
                    rev35R019VisibleFoundationQueue.Count > 0 ||
                    rev35R007FoundationQueue.Count > 0)
'''
renderer, _ = replace_once(renderer, wake_old, wake_new,
                           'Hotfix1 R010 non-authoritative wake')

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

telemetry_marker = '                "; oh_rev35_r019_variant=" + Rev35R019Variant +'
telemetry_insert = (
    '                "; oh_rev35_r019_hf1_variant=" + Rev35R019Hotfix1Variant +\n')
renderer, _ = insert_after_unique_line(
    renderer, telemetry_marker, telemetry_insert,
    'oh_rev35_r019_hf1_variant=', 'Hotfix1 telemetry identity')

required_runtime = (
    HF1,
    'Rev35R019Hotfix1Variant',
    'oh_rev35_r019_hf1_variant=',
    'rev35R019VisibleFoundationQueue.Count > 0 ||\n                    rev35R007FoundationQueue.Count > 0',
    'int r010QueueBacklog =\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\n                Math.Max(0, rev35R007FoundationQueue.Count);',
    '(pendingEntryCommit == null ? 0 : 1) +\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\n                Math.Max(0, rev35R007FoundationQueue.Count);',
)
missing_runtime = [token for token in required_runtime if token not in renderer]
if missing_runtime:
    fail('runtime contract incomplete: ' + ', '.join(missing_runtime))
R.write_text(renderer)

# R007 historical queue-bound compatibility.
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

# R010 historical wake/backlog compatibility. Use assertion labels rather than
# byte-exact source because the local R018 materialization may already have successor
# edits elsewhere in this verifier.
v10 = V10.read_text()

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
v10, changed = replace_python_assertion_block(
    v10,
    'R007 FIFO wakes non-authoritative staged pump',
    successor10_wake,
    'r019_wake = re.search(',
    include_assignment='wake = re.search(')
if changed:
    print(PREFIX + ' patched historical R010 wake assertion')

successor10_budget = '''legacy_r010_backlog = (
    'int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);' in r)
r019_r010_backlog = (
    'int r010QueueBacklog =\\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r)
ck(legacy_r010_backlog or r019_r010_backlog,
   'R007 legacy FIFO or exact R019 visible+hidden queues included in adaptive backlog')
'''
v10, changed = replace_python_assertion_block(
    v10,
    'R007 FIFO included in adaptive backlog',
    successor10_budget,
    'r019_r010_backlog = (')
if changed:
    print(PREFIX + ' patched historical R010 adaptive-backlog assertion')

successor10_final = '''legacy_final_backlog = (
    '(pendingEntryCommit == null ? 0 : 1) +\\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r)
r019_final_backlog = (
    '(pendingEntryCommit == null ? 0 : 1) +\\n                Math.Max(0, rev35R019VisibleFoundationQueue.Count) +\\n                Math.Max(0, rev35R007FoundationQueue.Count);' in r)
ck(legacy_final_backlog or r019_final_backlog,
   'main commit final backlog includes R007 legacy or exact R019 visible+hidden queues')
'''
v10, changed = replace_python_assertion_block(
    v10,
    'main commit final backlog includes R007 FIFO',
    successor10_final,
    'r019_final_backlog = (')
if changed:
    print(PREFIX + ' patched historical R010 final-backlog assertion')

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
