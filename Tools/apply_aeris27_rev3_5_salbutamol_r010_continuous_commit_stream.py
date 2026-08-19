#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 SALBUTAMOL SULFATE R010 CONTINUOUS COMMIT STREAM]'
R009 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE'
R010 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R010_CONTINUOUS_COMMIT_STREAM'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


if not R.is_file() or not B.is_file():
    fail('required generated files missing')
renderer = R.read_text()
build = B.read_text()
if R009 not in renderer:
    fail('R009 generated parent required')
if R010 in renderer:
    print(PREFIX + ' already present')
    raise SystemExit(0)

renderer, _ = replace_once(
    renderer,
    '        const string Rev35R009Variant = "' + R009 + '";\n',
    '        const string Rev35R009Variant = "' + R009 + '";\n'
    '        // ' + R010 + ': keep the single staged commit lane alive while the\n'
    '        // existing R007 current-FAR handoff FIFO still has work.\n'
    '        const string Rev35R010Variant = "' + R010 + '";\n',
    'R010 renderer identity')

renderer, _ = replace_once(
    renderer,
    '        long operationHealthRev35R008FoundationScheduleFirst;\n',
    '        long operationHealthRev35R008FoundationScheduleFirst;\n'
    '        long operationHealthRev35R010QueueBacklogBudgetSamples;\n'
    '        int operationHealthRev35R010QueueBacklogPeak;\n',
    'R010 telemetry fields')

# Keep all inherited non-authoritative block internals untouched. Only widen the existing
# wake predicate so an already-RenderReady R007 FAR FIFO cannot sleep until the next 10 Hz
# authoritative tick after pendingEntryCommit becomes null at the frame budget boundary.
old_wake_condition = (
    '                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0)\n')
new_wake_condition = (
    '                if (pendingEntryCommit != null || rasterizer.CompletedCount > 0 ||\n'
    '                    rev35R007FoundationQueue.Count > 0)\n')
renderer, _ = replace_once(renderer, old_wake_condition, new_wake_condition,
                           'R010 non-authoritative queue wake condition')

# R004 adaptive budgeting predates R007 and did not count the current-FAR handoff FIFO.
old_budget = '''            int backlog = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1);'''
new_budget = '''            int r010QueueBacklog = Math.Max(0, rev35R007FoundationQueue.Count);
            int backlog = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1) + r010QueueBacklog;
            if (r010QueueBacklog > 0)
            {
                operationHealthRev35R010QueueBacklogBudgetSamples++;
                operationHealthRev35R010QueueBacklogPeak = Math.Max(
                    operationHealthRev35R010QueueBacklogPeak, r010QueueBacklog);
            }'''
renderer, _ = replace_once(renderer, old_budget, new_budget,
                           'R010 R004 real backlog accounting')

# Main-commit backlog telemetry/hard-rail witness must report the same real queue depth.
old_final = '''            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1);'''
new_final = '''            int finalRemainingCompleted = Math.Max(0, rasterizer.CompletedCount) +
                (pendingEntryCommit == null ? 0 : 1) +
                Math.Max(0, rev35R007FoundationQueue.Count);'''
renderer, _ = replace_once(renderer, old_final, new_final,
                           'R010 final backlog accounting')

telemetry_anchor = (
    '                "; oh_rev35_r009_terminal_null=" + rasterizer.Rev35R009TerminalNull +\n')
telemetry_new = telemetry_anchor + (
    '                "; oh_rev35_r010_variant=" + Rev35R010Variant +\n'
    '                "; oh_rev35_r010_queue_budget_samples=" + operationHealthRev35R010QueueBacklogBudgetSamples +\n'
    '                "; oh_rev35_r010_queue_backlog_peak=" + operationHealthRev35R010QueueBacklogPeak +\n')
renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                           'R010 telemetry append')

if 'REV3_5_R010_VARIANT="' + R010 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R009_VARIANT="' + R009 + '"\n',
        'REV3_5_R009_VARIANT="' + R009 + '"\n'
        'REV3_5_R010_VARIANT="' + R010 + '"\n',
        'build R010 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutam_r009_ghost_pending_backpressure.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutam_r009_ghost_pending_backpressure.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutam_r010_continuous_commit_stream.py"\n',
        'build R010 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r009_variant=%s\\n\' "$REV3_5_R009_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r009_variant=%s\\n\' "$REV3_5_R009_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r010_variant=%s\\n\' "$REV3_5_R010_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R010 identity')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
    'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    if forbidden in renderer:
        fail('rejected mechanism present after R010: ' + forbidden)

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R009)
print('r010=' + R010)
print('commit_lane=1 unchanged')
print('wake=current pending OR raster completed OR R007 current-FAR queue')
print('adaptive_budget=existing R004 0.50/1.00/1.50/2.00ms; R007 queue now counted')
print('hard_maximum_change=0 worker_change=0 scheduler_change=0 rasterizer_change=0')
print('quality_change=0 10Hz_change=0 exact_range_change=0 publication_authority_change=0')
