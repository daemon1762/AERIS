#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
Z = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
S = ROOT / 'Source/AERISFlightControl/Performance/AERISWorkerScheduler.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 SALBUTAMOL SULFATE R009 GHOST PENDING BACKPRESSURE]'
R008 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'
R009 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R009_GHOST_PENDING_BACKPRESSURE'


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
    if start < 0: fail('method missing: ' + signature)
    op = text.find('{', start)
    if op < 0: fail('method open missing: ' + signature)
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


if not all(p.is_file() for p in (R, Z, S, B)):
    fail('required generated files missing')
renderer = R.read_text(); raster = Z.read_text(); scheduler = S.read_text(); build = B.read_text()
if R008 not in renderer or R008 not in raster:
    fail('R008 generated parent required')
if R009 in renderer or R009 in raster or R009 in scheduler:
    print(PREFIX + ' already present')
    raise SystemExit(0)

# ---- Scheduler: commit-required queue entries are never replaced/evicted ----
scheduler, _ = replace_once(
    scheduler,
    '        const int ResultCapacity = 256;\n',
    '        const int ResultCapacity = 256;\n'
    '        // ' + R009 + ': commit-required queue entries are protected from\n'
    '        // best-effort replacement/eviction. No lane capacity or worker count changes.\n'
    '        const string Rev35R009QueueProtection = "' + R009 + '";\n',
    'R009 scheduler identity')

old_nonrequired = '''                else
                {
                    latestJobIds[job.IdentityKey] = job.Id;
                    if (queue.ByKey.TryGetValue(key, out previous))
                    {
                        previous.Value.Cancelled = true;
                        Interlocked.Increment(ref cancelled);
                        previous.Value = job;
                        Interlocked.Increment(ref replaced);
                    }
                    else
                    {
                        if (queue.Jobs.Count >= queue.Capacity)
                        {
                            LinkedListNode<Job> oldest = queue.Jobs.First;
                            if (oldest != null)
                            {
                                oldest.Value.Cancelled = true;
                                Interlocked.Increment(ref cancelled);
                                RemoveLatestIfSameLocked(oldest.Value);
                                queue.ByKey.Remove(oldest.Value.Key);
                                queue.Jobs.RemoveFirst();
                                Interlocked.Increment(ref dropped);
                            }
                        }
                        LinkedListNode<Job> node = queue.Jobs.AddLast(job);
                        queue.ByKey[key] = node;
                    }
                }'''
new_nonrequired = '''                else
                {
                    if (queue.ByKey.TryGetValue(key, out previous))
                    {
                        // Best-effort work may never replace an admitted required job.
                        if (previous.Value != null && previous.Value.CommitRequired)
                        {
                            Interlocked.Increment(ref dropped);
                            return false;
                        }
                        latestJobIds[job.IdentityKey] = job.Id;
                        previous.Value.Cancelled = true;
                        Interlocked.Increment(ref cancelled);
                        previous.Value = job;
                        Interlocked.Increment(ref replaced);
                    }
                    else
                    {
                        if (queue.Jobs.Count >= queue.Capacity)
                        {
                            // Skip commit-required jobs while searching for an evictable
                            // best-effort entry. If none exists, reject the incoming
                            // best-effort job instead of corrupting required ownership.
                            LinkedListNode<Job> evictable = queue.Jobs.First;
                            while (evictable != null && evictable.Value != null &&
                                evictable.Value.CommitRequired)
                                evictable = evictable.Next;
                            if (evictable == null)
                            {
                                Interlocked.Increment(ref dropped);
                                return false;
                            }
                            evictable.Value.Cancelled = true;
                            Interlocked.Increment(ref cancelled);
                            RemoveLatestIfSameLocked(evictable.Value);
                            queue.ByKey.Remove(evictable.Value.Key);
                            queue.Jobs.Remove(evictable);
                            Interlocked.Increment(ref dropped);
                        }
                        latestJobIds[job.IdentityKey] = job.Id;
                        LinkedListNode<Job> node = queue.Jobs.AddLast(job);
                        queue.ByKey[key] = node;
                    }
                }'''
scheduler, _ = replace_once(scheduler, old_nonrequired, new_nonrequired,
                            'R009 required queue protection')

# ---- Rasterizer: required backpressure + accepted-only pending registration --
raster, _ = replace_once(
    raster,
    '        const int MaximumSparseCorrectionParentCells = 256;\n',
    '        const int MaximumSparseCorrectionParentCells = 256;\n'
    '        const string Rev35R009Variant = "' + R009 + '";\n',
    'R009 raster identity')

# R008 owns these counters/properties; append R009 observability beside them.
raster, _ = replace_once(
    raster,
    '        long rev35R008SchedulerCancels;\n',
    '        long rev35R008SchedulerCancels;\n'
    '        long rev35R009AdmissionAccepted;\n'
    '        long rev35R009AdmissionRejected;\n'
    '        long rev35R009PendingRegistered;\n'
    '        long rev35R009DuplicatePending;\n'
    '        long rev35R009TerminalNull;\n',
    'R009 raster counters')
raster, _ = replace_once(
    raster,
    '        internal long Rev35R008SchedulerCancels { get { lock (gate) return rev35R008SchedulerCancels; } }\n',
    '        internal long Rev35R008SchedulerCancels { get { lock (gate) return rev35R008SchedulerCancels; } }\n'
    '        internal long Rev35R009AdmissionAccepted { get { lock (gate) return rev35R009AdmissionAccepted; } }\n'
    '        internal long Rev35R009AdmissionRejected { get { lock (gate) return rev35R009AdmissionRejected; } }\n'
    '        internal long Rev35R009PendingRegistered { get { lock (gate) return rev35R009PendingRegistered; } }\n'
    '        internal long Rev35R009DuplicatePending { get { lock (gate) return rev35R009DuplicatePending; } }\n'
    '        internal long Rev35R009TerminalNull { get { lock (gate) return rev35R009TerminalNull; } }\n',
    'R009 raster counter properties')

e0, e1 = method_bounds(raster, '        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)')
enqueue = raster[e0:e1]
old_pre = '''            lock (gate)
            {
                PendingState existing;
                if (pending.TryGetValue(tileId, out existing) && existing != null && existing.CreatedUtcTicks == createdUtcTicks && string.Equals(existing.StyleKey, request.StyleKey, StringComparison.Ordinal) && ElapsedSeconds(existing.EnqueuedTicks) < 10.0) return true;
                string pendingSchedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
                pending[tileId] = new PendingState { Generation = request.Generation, CreatedUtcTicks = createdUtcTicks, StyleKey = request.StyleKey, SchedulerKey = pendingSchedulerKey, EnqueuedTicks = Stopwatch.GetTimestamp(), RequestIdentity = request.RequestIdentity };
            }
            request.Tile = request.Tile.CloneImmutable();
            string schedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
            bool accepted = runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute, schedulerKey, runtime.CaptureStamp(), context =>'''
new_pre = '''            lock (gate)
            {
                PendingState existing;
                if (pending.TryGetValue(tileId, out existing) && existing != null && existing.CreatedUtcTicks == createdUtcTicks && string.Equals(existing.StyleKey, request.StyleKey, StringComparison.Ordinal) && ElapsedSeconds(existing.EnqueuedTicks) < 10.0)
                {
                    rev35R009DuplicatePending++;
                    return true;
                }
            }
            request.Tile = request.Tile.CloneImmutable();
            string schedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
            bool accepted = runtime.Scheduler.SubmitRequired(AERISRuntimeLane.GeneralCompute, schedulerKey, runtime.CaptureStamp(), context =>'''
enqueue, _ = replace_once(enqueue, old_pre, new_pre,
                          'R009 SubmitRequired / deferred pending registration')

enqueue, _ = replace_once(
    enqueue,
    '                    if (result == null) { failures++; return; }\n',
    '                    if (result == null) { failures++; rev35R009TerminalNull++; return; }\n',
    'R009 terminal null telemetry')

old_post = '''            if (!accepted)
            {
                lock (gate)
                {
                    PendingState newest;
                    if (pending.TryGetValue(tileId, out newest) && newest != null && newest.Generation == request.Generation) pending.Remove(tileId);
                    dropped++;
                }
            }
            return accepted;'''
new_post = '''            if (!accepted)
            {
                // Required admission rejected by bounded queue/backpressure. No pending
                // ownership exists yet, so the caller can retry on the next content tick.
                lock (gate) rev35R009AdmissionRejected++;
                return false;
            }
            lock (gate)
            {
                // Scheduler accepted the required job. Only now publish Rasterizer pending
                // ownership; DrainCommits executes on the main thread after Enqueue returns.
                pending[tileId] = new PendingState
                {
                    Generation = request.Generation,
                    CreatedUtcTicks = createdUtcTicks,
                    StyleKey = request.StyleKey,
                    SchedulerKey = schedulerKey,
                    EnqueuedTicks = Stopwatch.GetTimestamp(),
                    RequestIdentity = request.RequestIdentity
                };
                rev35R009AdmissionAccepted++;
                rev35R009PendingRegistered++;
            }
            return true;'''
enqueue, _ = replace_once(enqueue, old_post, new_post,
                          'R009 accepted-only pending registration')
raster = raster[:e0] + enqueue + raster[e1:]

# ---- Renderer telemetry / identity only. Commit/quality semantics untouched --
renderer, _ = replace_once(
    renderer,
    '        const string Rev35R008Variant = "' + R008 + '";\n',
    '        const string Rev35R008Variant = "' + R008 + '";\n'
    '        const string Rev35R009Variant = "' + R009 + '";\n',
    'R009 renderer identity')
telemetry_anchor = (
    '                "; oh_rev35_r008_scheduler_cancel=" + rasterizer.Rev35R008SchedulerCancels +\n')
telemetry_new = telemetry_anchor + (
    '                "; oh_rev35_r009_variant=" + Rev35R009Variant +\n'
    '                "; oh_rev35_r009_admit_accept=" + rasterizer.Rev35R009AdmissionAccepted +\n'
    '                "; oh_rev35_r009_admit_reject=" + rasterizer.Rev35R009AdmissionRejected +\n'
    '                "; oh_rev35_r009_pending_registered=" + rasterizer.Rev35R009PendingRegistered +\n'
    '                "; oh_rev35_r009_duplicate_pending=" + rasterizer.Rev35R009DuplicatePending +\n'
    '                "; oh_rev35_r009_terminal_null=" + rasterizer.Rev35R009TerminalNull +\n')
renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                           'R009 telemetry append')

if 'REV3_5_R009_VARIANT="' + R009 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R008_VARIANT="' + R008 + '"\n',
        'REV3_5_R008_VARIANT="' + R008 + '"\n'
        'REV3_5_R009_VARIANT="' + R009 + '"\n',
        'build R009 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r009_ghost_pending_backpressure.py"\n',
        'build R009 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r008_variant=%s\\n\' "$REV3_5_R008_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r008_variant=%s\\n\' "$REV3_5_R008_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r009_variant=%s\\n\' "$REV3_5_R009_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R009 identity')

for forbidden in (
    'Task.Run(', 'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE'):
    if forbidden in renderer or forbidden in raster:
        fail('rejected mechanism present after R009: ' + forbidden)

R.write_text(renderer); Z.write_text(raster); S.write_text(scheduler); B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R008)
print('r009=' + R009)
print('terrain_scheduler=SubmitRequired')
print('pending_registration=AFTER_ACCEPT')
print('best_effort_required_eviction=FORBIDDEN')
print('worker_count_change=0 lane_capacity_change=0 commit_budget_change=0')
print('quality_change=0 10Hz_change=0 160km_change=0')
