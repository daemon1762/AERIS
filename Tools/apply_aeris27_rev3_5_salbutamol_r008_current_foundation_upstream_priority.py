#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
Z = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 SALBUTAMOL SULFATE R008 CURRENT FOUNDATION UPSTREAM PRIORITY]'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_FOUNDATION_CHAINED_ADMISSION'
R008 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R008_CURRENT_FOUNDATION_UPSTREAM_PRIORITY'


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


if not all(p.is_file() for p in (R, Z, T, B)):
    fail('required generated files missing')
renderer = R.read_text(); raster = Z.read_text(); tile = T.read_text(); build = B.read_text()
if R007 not in renderer or HF4 not in renderer or HF3 not in tile:
    fail('R007 + HF4 + HF3 generated parent required')
if R008 in renderer or R008 in raster:
    print(PREFIX + ' already present')
    raise SystemExit(0)

# ---- Rasterizer request identity + upstream reconciliation -----------------
raster, _ = replace_once(
    raster,
    '        internal AERISTerrainVirtualDetailProfile VirtualDetailProfile;\n',
    '        internal AERISTerrainVirtualDetailProfile VirtualDetailProfile;\n'
    '        // ' + R008 + ': exact renderer cache-key identity for current-view\n'
    '        // reconciliation. This does not alter tile data or worker ownership.\n'
    '        internal string RequestIdentity;\n',
    'R008 request identity')

raster, _ = replace_once(
    raster,
    '        internal AERISResidentCommitToken ResidentToken;\n',
    '        internal AERISResidentCommitToken ResidentToken;\n'
    '        internal string RequestIdentity;\n',
    'R008 result identity')

raster, _ = replace_once(
    raster,
    '            internal long EnqueuedTicks;\n',
    '            internal long EnqueuedTicks;\n'
    '            internal string RequestIdentity;\n',
    'R008 pending identity')

raster, _ = replace_once(
    raster,
    '        readonly List<string> cancelSchedulerKeysScratch = new List<string>(32);\n',
    '        readonly List<string> cancelSchedulerKeysScratch = new List<string>(32);\n'
    '        // ' + R008 + ': bounded reconciliation scratch. completed is already hard\n'
    '        // capped at 64, and this queue never survives beyond one reconcile call.\n'
    '        readonly List<string> rev35R008CancelTileIdsScratch = new List<string>(128);\n'
    '        readonly List<string> rev35R008CancelSchedulerKeysScratch = new List<string>(128);\n'
    '        readonly Queue<AERISTerrainGpuTileRasterResult> rev35R008CompletedScratch =\n'
    '            new Queue<AERISTerrainGpuTileRasterResult>(64);\n'
    '        long rev35R008Reconciliations;\n'
    '        long rev35R008PendingCancelled;\n'
    '        long rev35R008CompletedDropped;\n'
    '        long rev35R008SchedulerCancels;\n',
    'R008 reconcile scratch/counters')

raster, _ = replace_once(
    raster,
    '        internal int FailureCount { get { lock (gate) return failures; } }\n',
    '        internal int FailureCount { get { lock (gate) return failures; } }\n'
    '        internal long Rev35R008Reconciliations { get { lock (gate) return rev35R008Reconciliations; } }\n'
    '        internal long Rev35R008PendingCancelled { get { lock (gate) return rev35R008PendingCancelled; } }\n'
    '        internal long Rev35R008CompletedDropped { get { lock (gate) return rev35R008CompletedDropped; } }\n'
    '        internal long Rev35R008SchedulerCancels { get { lock (gate) return rev35R008SchedulerCancels; } }\n',
    'R008 counter properties')

# Enqueue must always carry the exact current renderer identity.
e0, e1 = method_bounds(raster, '        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)')
enqueue = raster[e0:e1]
enqueue, _ = replace_once(
    enqueue,
    '            if (disposed || request == null || request.Tile == null || request.Tile.Elevation == null || request.Tile.Flags == null || string.IsNullOrEmpty(request.StyleKey)) return false;\n',
    '            if (disposed || request == null || request.Tile == null || request.Tile.Elevation == null || request.Tile.Flags == null || string.IsNullOrEmpty(request.StyleKey) || string.IsNullOrEmpty(request.RequestIdentity)) return false;\n',
    'R008 require request identity')
enqueue, _ = replace_once(
    enqueue,
    '                pending[tileId] = new PendingState { Generation = request.Generation, CreatedUtcTicks = createdUtcTicks, StyleKey = request.StyleKey, SchedulerKey = pendingSchedulerKey, EnqueuedTicks = Stopwatch.GetTimestamp() };\n',
    '                pending[tileId] = new PendingState { Generation = request.Generation, CreatedUtcTicks = createdUtcTicks, StyleKey = request.StyleKey, SchedulerKey = pendingSchedulerKey, EnqueuedTicks = Stopwatch.GetTimestamp(), RequestIdentity = request.RequestIdentity };\n',
    'R008 pending identity capture')
raster = raster[:e0] + enqueue + raster[e1:]

# Result carries immutable request identity back to the renderer-side completed queue.
raster, _ = replace_once(
    raster,
    '                Generation = request.Generation, Key = tile.Key, TileCreatedUtcTicks = tile.CreatedUtcTicks, StyleKey = request.StyleKey, Resolution = resolution,\n',
    '                Generation = request.Generation, Key = tile.Key, TileCreatedUtcTicks = tile.CreatedUtcTicks, StyleKey = request.StyleKey, RequestIdentity = request.RequestIdentity, Resolution = resolution,\n',
    'R008 result identity carry')

reconcile = r'''        internal void ReconcileCurrentRequests(HashSet<string> currentRequestIdentities)
        {
            if (disposed || currentRequestIdentities == null) return;
            rev35R008CancelTileIdsScratch.Clear();
            rev35R008CancelSchedulerKeysScratch.Clear();
            rev35R008CompletedScratch.Clear();
            lock (gate)
            {
                rev35R008Reconciliations++;
                foreach (KeyValuePair<string, PendingState> pair in pending)
                {
                    PendingState state = pair.Value;
                    if (state == null || string.IsNullOrEmpty(state.RequestIdentity) ||
                        !currentRequestIdentities.Contains(state.RequestIdentity))
                    {
                        rev35R008CancelTileIdsScratch.Add(pair.Key);
                        if (state != null && !string.IsNullOrEmpty(state.SchedulerKey))
                            rev35R008CancelSchedulerKeysScratch.Add(state.SchedulerKey);
                    }
                }
                for (int i = 0; i < rev35R008CancelTileIdsScratch.Count; i++)
                {
                    if (pending.Remove(rev35R008CancelTileIdsScratch[i]))
                        rev35R008PendingCancelled++;
                }

                while (completed.Count > 0)
                {
                    AERISTerrainGpuTileRasterResult result = completed.Dequeue();
                    if (result != null && !string.IsNullOrEmpty(result.RequestIdentity) &&
                        currentRequestIdentities.Contains(result.RequestIdentity))
                        rev35R008CompletedScratch.Enqueue(result);
                    else
                    {
                        dropped++;
                        rev35R008CompletedDropped++;
                    }
                }
                while (rev35R008CompletedScratch.Count > 0)
                    completed.Enqueue(rev35R008CompletedScratch.Dequeue());
            }

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime != null)
            {
                for (int i = 0; i < rev35R008CancelSchedulerKeysScratch.Count; i++)
                {
                    runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,
                        rev35R008CancelSchedulerKeysScratch[i]);
                    lock (gate) rev35R008SchedulerCancels++;
                }
            }
            rev35R008CancelTileIdsScratch.Clear();
            rev35R008CancelSchedulerKeysScratch.Clear();
            rev35R008CompletedScratch.Clear();
        }

'''
anchor = '        internal void CancelAll()\n'
raster, _ = replace_once(raster, anchor, reconcile + anchor,
                           'R008 reconcile method insertion')

# ---- Renderer: requested-first -> reconcile -> FAR-first scheduling --------
renderer, _ = replace_once(
    renderer,
    '        const int Rev35R007FoundationQueueMaximum = 128;\n',
    '        const int Rev35R007FoundationQueueMaximum = 128;\n'
    '        // ' + R008 + ': current requested FAR receives upstream admission before\n'
    '        // obsolete-view raster work. No worker count or commit budget is changed.\n'
    '        const string Rev35R008Variant = "' + R008 + '";\n',
    'R008 renderer identity')

renderer, _ = replace_once(
    renderer,
    '        int operationHealthRev35R007QueuePeak;\n',
    '        int operationHealthRev35R007QueuePeak;\n'
    '        long operationHealthRev35R008GeometryPumpSuppress;\n'
    '        long operationHealthRev35R008PendingCommitCancelled;\n'
    '        long operationHealthRev35R008FoundationScheduleFirst;\n'
    '        bool rev35R008GeometryReconcilePending;\n',
    'R008 renderer telemetry fields')

# Suppress one old-view pump while a new exact request set is not yet known.
p0, p1 = method_bounds(renderer, '        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
pump = renderer[p0:p1]
open_brace = pump.find('{')
if open_brace < 0: fail('R008 pump body missing')
insert = '''\n            if (rev35R008GeometryReconcilePending)\n            {\n                operationHealthRev35R008GeometryPumpSuppress++;\n                return;\n            }'''
if 'operationHealthRev35R008GeometryPumpSuppress++' not in pump:
    pump = pump[:open_brace + 1] + insert + pump[open_brace + 1:]
renderer = renderer[:p0] + pump + renderer[p1:]

# Mark geometry invalidation before the existing R007 pump call.
renderer, _ = replace_once(
    renderer,
    '                if (contentGeometryChanged)\n'
    '                    ResetRev35R007FoundationQueue();\n'
    '                PumpStagedCompletedCommit(system,',
    '                if (contentGeometryChanged)\n'
    '                {\n'
    '                    ResetRev35R007FoundationQueue();\n'
    '                    rev35R008GeometryReconcilePending = true;\n'
    '                }\n'
    '                PumpStagedCompletedCommit(system,',
    'R008 pre-capture stale pump suppression')

# The old single loop builds requested and schedules simultaneously. Replace only the
# beginning/loop shell so exact requested identities exist before any new job is admitted,
# then run FAR first and all other LODs second. Body remains otherwise identical.
old_loop_head = '''                requested.Clear();
                scheduledThisFrame.Clear();
                ResetRev35R007FoundationQueue();
                tiles = PrepareSortedTileScratch(visible.Tiles);
                EnsureEntryScratch(tiles == null ? 0 : tiles.Length);
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null)
                    {
                        fallbackEntriesScratch[i] = null;
                        currentEntriesScratch[i] = null;
                        drawEntriesScratch[i] = null;
                        continue;
                    }
                    string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                    requested.Add(cacheKey);'''
new_loop_head = '''                requested.Clear();
                scheduledThisFrame.Clear();
                ResetRev35R007FoundationQueue();
                tiles = PrepareSortedTileScratch(visible.Tiles);
                EnsureEntryScratch(tiles == null ? 0 : tiles.Length);

                // R008 phase 1: establish the complete current exact request set first.
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile requestedTile = tiles[i];
                    if (requestedTile == null) continue;
                    requested.Add(CacheKey(requestedTile.Key,
                        requestedTile.CreatedUtcTicks, styleKey));
                }
                rasterizer.ReconcileCurrentRequests(requested);
                if (pendingEntryCommit != null &&
                    !requested.Contains(pendingEntryCommit.CacheKey))
                {
                    CancelPendingEntryCommit();
                    operationHealthRev35R008PendingCommitCancelled++;
                }
                rev35R008GeometryReconcilePending = false;

                // R008 phase 2: current FAR foundation enters GeneralCompute before
                // Route/Local/exact work. Same worker pool and FIFO semantics remain.
                for (int admissionPass = 0; admissionPass < 2; admissionPass++)
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null)
                    {
                        if (admissionPass == 0)
                        {
                            fallbackEntriesScratch[i] = null;
                            currentEntriesScratch[i] = null;
                            drawEntriesScratch[i] = null;
                        }
                        continue;
                    }
                    bool r008Foundation = tile.Key.Lod == AERISTerrainTileLod.Far;
                    if ((admissionPass == 0) != r008Foundation) continue;
                    string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);'''
renderer, _ = replace_once(renderer, old_loop_head, new_loop_head,
                           'R008 requested-first / FAR-first loop')

# Schedule carries exact identity and observes FAR-first admissions.
s0, s1 = method_bounds(renderer,
    '        void Schedule(AERISTerrainHeightTile tile, string cacheKey, string styleKey,')
schedule = renderer[s0:s1]
schedule, _ = replace_once(
    schedule,
    '                StyleKey = styleKey,\n'
    '                VirtualDetailProfile = virtualDetail\n',
    '                StyleKey = styleKey,\n'
    '                VirtualDetailProfile = virtualDetail,\n'
    '                RequestIdentity = cacheKey\n',
    'R008 schedule exact identity')
schedule, _ = replace_once(
    schedule,
    '            rasterizer.Enqueue(new AERISTerrainGpuTileRasterRequest\n',
    '            if (tile.Key.Lod == AERISTerrainTileLod.Far)\n'
    '                operationHealthRev35R008FoundationScheduleFirst++;\n'
    '            rasterizer.Enqueue(new AERISTerrainGpuTileRasterRequest\n',
    'R008 FAR schedule telemetry')
renderer = renderer[:s0] + schedule + renderer[s1:]

# Teardown cannot leave the pump suppressed into a later viewport lifecycle.
renderer = renderer.replace(
    'ResetRev35R007FoundationQueue();',
    'ResetRev35R007FoundationQueue();\n            rev35R008GeometryReconcilePending = false;')
# The replacement above also touches Draw with indentation that remains valid C#; eliminate
# duplicate reset assignment if an exact block received it more than once consecutively.
renderer = renderer.replace(
    'rev35R008GeometryReconcilePending = false;\n            rev35R008GeometryReconcilePending = false;',
    'rev35R008GeometryReconcilePending = false;')

# Telemetry: keep renderer counters and rasterizer upstream-reconciliation counters together.
telemetry_anchor = (
    '                "; oh_rev35_r007_reset=" + operationHealthRev35R007QueueResets +\n')
telemetry_new = telemetry_anchor + (
    '                "; oh_rev35_r008_variant=" + Rev35R008Variant +\n'
    '                "; oh_rev35_r008_pump_suppress=" + operationHealthRev35R008GeometryPumpSuppress +\n'
    '                "; oh_rev35_r008_pending_cancel=" + operationHealthRev35R008PendingCommitCancelled +\n'
    '                "; oh_rev35_r008_far_schedule=" + operationHealthRev35R008FoundationScheduleFirst +\n'
    '                "; oh_rev35_r008_reconcile=" + rasterizer.Rev35R008Reconciliations +\n'
    '                "; oh_rev35_r008_raster_pending_cancel=" + rasterizer.Rev35R008PendingCancelled +\n'
    '                "; oh_rev35_r008_raster_completed_drop=" + rasterizer.Rev35R008CompletedDropped +\n'
    '                "; oh_rev35_r008_scheduler_cancel=" + rasterizer.Rev35R008SchedulerCancels +\n')
renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                           'R008 telemetry append')

# ---- Build identity ---------------------------------------------------------
if 'REV3_5_R008_VARIANT="' + R008 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R007_VARIANT="' + R007 + '"\n',
        'REV3_5_R007_VARIANT="' + R007 + '"\n'
        'REV3_5_R008_VARIANT="' + R008 + '"\n',
        'build R008 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r007_foundation_chained_admission.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r008_current_foundation_upstream_priority.py"\n',
        'build R008 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r007_variant=%s\\n\' "$REV3_5_R007_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r007_variant=%s\\n\' "$REV3_5_R007_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r008_variant=%s\\n\' "$REV3_5_R008_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R008 identity')

# No rejected worker migration / unbounded concurrency may re-enter.
for forbidden in (
    'Task.Run(', 'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    if forbidden in renderer or forbidden in raster:
        fail('rejected mechanism present after R008: ' + forbidden)

R.write_text(renderer); Z.write_text(raster); B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R007)
print('r008=' + R008)
print('upstream=current-request reconcile + stale scheduler cancellation + stale completed drop')
print('admission=FAR first; Route/Local/exact second; same GeneralCompute workers')
print('worker_change=0 scheduler_fairness_change=0 commit_budget_change=0 10Hz_change=0 160km_change=0')
print('R007/HF4/HF3/R003 authority retained')
