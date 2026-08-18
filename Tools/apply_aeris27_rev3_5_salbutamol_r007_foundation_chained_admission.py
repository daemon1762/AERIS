#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 SALBUTAMOL SULFATE R007 FOUNDATION CHAINED ADMISSION]'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


def method_bounds(text, signature):
    start = text.find(signature)
    if start < 0:
        fail('method missing: ' + signature)
    op = text.find('{', start)
    if op < 0:
        fail('method open missing: ' + signature)
    depth = 0
    state = 'code'
    i = op
    while i < len(text):
        c = text[i]
        n = text[i + 1] if i + 1 < len(text) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return start, i + 1
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
    fail('method close missing: ' + signature)


if not R.is_file() or not T.is_file() or not B.is_file():
    fail('required generated files missing')
renderer = R.read_text()
tile = T.read_text()
build = B.read_text()
if HF4 not in renderer or HF3 not in tile:
    fail('R006 HF4 + HF3 generated parent required')
if R007 in renderer:
    print(PREFIX + ' already present')
    raise SystemExit(0)

# Identity + strict queue bound. This is not a second commit lane and does not retain
# completed Entries/Meshes; it holds only cache-key references to already RenderReady FAR
# fields that are required by the current viewport.
renderer, _ = replace_once(
    renderer,
    '        const int Rev35R006Hf4IndexPoolMaximumArrays = 128;\n',
    '        const int Rev35R006Hf4IndexPoolMaximumArrays = 128;\n'
    '        // ' + R007 + ': remove the 5 Hz re-admission gap between already-\n'
    '        // RenderReady FAR foundation fields without increasing commit budget or lanes.\n'
    '        const string Rev35R007Variant = "' + R007 + '";\n'
    '        const int Rev35R007FoundationQueueMaximum = 128;\n',
    'R007 identity / queue bound')

# Queue references only. The HashSet prevents duplicate cache-key admission while a content
# snapshot is being rebuilt. No result array or Unity object is copied into this queue.
renderer, _ = replace_once(
    renderer,
    '        readonly HashSet<string> scheduledThisFrame =\n'
    '            new HashSet<string>(StringComparer.Ordinal);\n',
    '        readonly HashSet<string> scheduledThisFrame =\n'
    '            new HashSet<string>(StringComparer.Ordinal);\n'
    '        readonly Queue<string> rev35R007FoundationQueue =\n'
    '            new Queue<string>(Rev35R007FoundationQueueMaximum);\n'
    '        readonly HashSet<string> rev35R007FoundationQueued =\n'
    '            new HashSet<string>(StringComparer.Ordinal);\n',
    'R007 bounded queue state')

renderer, _ = replace_once(
    renderer,
    '        int operationHealthRev35R006Hf4IndexMaxItems;\n',
    '        int operationHealthRev35R006Hf4IndexMaxItems;\n'
    '        long operationHealthRev35R007Queued;\n'
    '        long operationHealthRev35R007ChainedBegins;\n'
    '        long operationHealthRev35R007ImmediateBegins;\n'
    '        long operationHealthRev35R007DuplicateSkips;\n'
    '        long operationHealthRev35R007StaleSkips;\n'
    '        long operationHealthRev35R007AlreadyCommittedSkips;\n'
    '        long operationHealthRev35R007MissingFieldSkips;\n'
    '        long operationHealthRev35R007Overflow;\n'
    '        long operationHealthRev35R007QueueResets;\n'
    '        int operationHealthRev35R007QueuePeak;\n',
    'R007 telemetry fields')

helpers = r'''        void ResetRev35R007FoundationQueue()
        {
            if (rev35R007FoundationQueue.Count > 0 ||
                rev35R007FoundationQueued.Count > 0)
                operationHealthRev35R007QueueResets++;
            rev35R007FoundationQueue.Clear();
            rev35R007FoundationQueued.Clear();
        }

        void QueueRev35R007FoundationField(AERISTerrainHeightTile tile,
            string cacheKey)
        {
            if (tile == null || tile.Key.Lod != AERISTerrainTileLod.Far ||
                string.IsNullOrEmpty(cacheKey)) return;
            // Only the latest exact requested viewport may enter this handoff queue.
            if (!requested.Contains(cacheKey) || entries.ContainsKey(cacheKey)) return;
            if (rev35R007FoundationQueued.Contains(cacheKey))
            {
                operationHealthRev35R007DuplicateSkips++;
                return;
            }
            if (rev35R007FoundationQueue.Count >= Rev35R007FoundationQueueMaximum)
            {
                operationHealthRev35R007Overflow++;
                return;
            }
            rev35R007FoundationQueued.Add(cacheKey);
            rev35R007FoundationQueue.Enqueue(cacheKey);
            operationHealthRev35R007Queued++;
            operationHealthRev35R007QueuePeak = Math.Max(
                operationHealthRev35R007QueuePeak,
                rev35R007FoundationQueue.Count);
        }

        bool TryBeginRev35R007QueuedFoundationCommit()
        {
            while (rev35R007FoundationQueue.Count > 0)
            {
                string cacheKey = rev35R007FoundationQueue.Dequeue();
                rev35R007FoundationQueued.Remove(cacheKey);
                // R003 remains authoritative: a rotated/translated viewport can make a
                // queued cache key obsolete before it reaches the single commit lane.
                if (!contentSnapshotValid || !requested.Contains(cacheKey))
                {
                    operationHealthRev35R007StaleSkips++;
                    continue;
                }
                if (entries.ContainsKey(cacheKey))
                {
                    operationHealthRev35R007AlreadyCommittedSkips++;
                    continue;
                }
                AERISTerrainRenderReadyHeightField field;
                if (!renderReadyFields.TryGetValue(cacheKey, out field) || field == null)
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                if (!TryBeginPendingEntryCommit(field))
                {
                    operationHealthRev35R007MissingFieldSkips++;
                    continue;
                }
                operationHealthRev35R007ChainedBegins++;
                return true;
            }
            return false;
        }

'''
anchor = '        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string cacheKey,\n'
renderer, _ = replace_once(renderer, anchor, helpers + anchor,
                           'R007 helper insertion')

# Phase6_002 currently suppresses duplicate raster work when RenderReady exists, but if the
# single pending slot is occupied it leaves the next FAR field waiting for the next 5 Hz
# content capture. Queue that exact current FAR key instead. Immediate admission remains
# unchanged when the lane is free.
u0, u1 = method_bounds(renderer,
    '        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string cacheKey,')
upload = renderer[u0:u1]
old_upload = '''            if (pendingEntryCommit == null)
                TryBeginPendingEntryCommit(field);
            return true;'''
new_upload = '''            if (pendingEntryCommit == null)
            {
                if (TryBeginPendingEntryCommit(field))
                    operationHealthRev35R007ImmediateBegins++;
            }
            else
            {
                QueueRev35R007FoundationField(tile, cacheKey);
            }
            return true;'''
upload, _ = replace_once(upload, old_upload, new_upload,
                         'R007 RenderReady handoff queue')
renderer = renderer[:u0] + upload + renderer[u1:]

# Chain a queued current FAR foundation field before touching rasterizer FIFO. The existing
# R004 budget, hardMaximum, single pending slot, R003 stale gate and Phase6_003 publication
# authority remain the only execution rails.
p0, p1 = method_bounds(renderer,
    '        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
pump = renderer[p0:p1]
old_pump = '''                if (pendingEntryCommit == null)
                {
                    completed.Clear();'''
new_pump = '''                if (pendingEntryCommit == null)
                    TryBeginRev35R007QueuedFoundationCommit();
                if (pendingEntryCommit == null)
                {
                    completed.Clear();'''
pump, _ = replace_once(pump, old_pump, new_pump,
                       'R007 chained admission before raster FIFO')
renderer = renderer[:p0] + pump + renderer[p1:]

# A heading/centre/range content invalidation must not chain keys from the previous exact
# viewport before CaptureVisible rebuilds requested[]. Same-view worker/retry ticks may still
# consume the current queue before recapture.
content_old = '''            if (contentTickRequired)
            {
                operationHealthContentTicks++;
                if (workerResultReady) operationHealthContentWorkerDrains++;
                PumpStagedCompletedCommit(system,'''
if content_old not in renderer:
    # Older call formatting may use DrainCompleted before the staged patch name is visible
    # in this textual neighbourhood; fail closed rather than guessing.
    fail('R007 content pump anchor missing')
content_new = '''            if (contentTickRequired)
            {
                operationHealthContentTicks++;
                if (workerResultReady) operationHealthContentWorkerDrains++;
                if (contentGeometryChanged)
                    ResetRev35R007FoundationQueue();
                PumpStagedCompletedCommit(system,'''
renderer = renderer.replace(content_old, content_new, 1)

# Every fresh CaptureVisible/requested rebuild owns a fresh exact queue. Fields encountered
# later in the same tile loop are re-enqueued deterministically.
renderer, _ = replace_once(
    renderer,
    '                requested.Clear();\n'
    '                scheduledThisFrame.Clear();\n',
    '                requested.Clear();\n'
    '                scheduledThisFrame.Clear();\n'
    '                ResetRev35R007FoundationQueue();\n',
    'R007 fresh requested-view queue reset')

# Full GPU/viewport teardown drops cache-key references alongside the already accepted HF4
# managed pools. This is resource hygiene only; ordinary Entry retirement does not touch it.
if 'ClearRev35R006Hf4PackedPools();' not in renderer:
    fail('R007 HF4 full-release anchor missing')
renderer = renderer.replace(
    'ClearRev35R006Hf4PackedPools();',
    'ClearRev35R006Hf4PackedPools();\n            ResetRev35R007FoundationQueue();')

telemetry_anchor = (
    '                "; oh_rev35_r006_hf4_index_max_items=" + '
    'operationHealthRev35R006Hf4IndexMaxItems +\n')
telemetry_new = telemetry_anchor + (
    '                "; oh_rev35_r007_variant=" + Rev35R007Variant +\n'
    '                "; oh_rev35_r007_queue=" + rev35R007FoundationQueue.Count +\n'
    '                "; oh_rev35_r007_queue_peak=" + operationHealthRev35R007QueuePeak +\n'
    '                "; oh_rev35_r007_queued=" + operationHealthRev35R007Queued +\n'
    '                "; oh_rev35_r007_chain=" + operationHealthRev35R007ChainedBegins +\n'
    '                "; oh_rev35_r007_immediate=" + operationHealthRev35R007ImmediateBegins +\n'
    '                "; oh_rev35_r007_duplicate=" + operationHealthRev35R007DuplicateSkips +\n'
    '                "; oh_rev35_r007_stale=" + operationHealthRev35R007StaleSkips +\n'
    '                "; oh_rev35_r007_already=" + operationHealthRev35R007AlreadyCommittedSkips +\n'
    '                "; oh_rev35_r007_missing=" + operationHealthRev35R007MissingFieldSkips +\n'
    '                "; oh_rev35_r007_overflow=" + operationHealthRev35R007Overflow +\n'
    '                "; oh_rev35_r007_reset=" + operationHealthRev35R007QueueResets +\n')
renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                           'R007 telemetry append')

if 'REV3_5_R007_VARIANT="' + R007 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R006_HOTFIX4="' + HF4 + '"\n',
        'REV3_5_R006_HOTFIX4="' + HF4 + '"\n'
        'REV3_5_R007_VARIANT="' + R007 + '"\n',
        'build R007 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py"\n',
        'build R007 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r006_hotfix4=%s\\n\' "$REV3_5_R006_HOTFIX4" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r006_hotfix4=%s\\n\' "$REV3_5_R006_HOTFIX4" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r007_variant=%s\\n\' "$REV3_5_R007_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R007 identity')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
    'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    if forbidden in renderer:
        fail('rejected mechanism present after R007: ' + forbidden)

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + HF4)
print('r007=' + R007)
print('queue=FAR RenderReady cache-key references only; hard cap=128')
print('commit_lane=1 budget_change=0 hardMaximum_change=0 worker_change=0')
print('publication=Phase6_003 retained; stale authority=R003 retained')
print('quality_change=0 10Hz_change=0 160km_change=0')
