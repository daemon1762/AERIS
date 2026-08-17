#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R006 MANAGED BUFFER REUSE + FOUNDATION OBSERVER]'
R005 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'

def fail(message):
    raise SystemExit(PREFIX + ' ' + message)

def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True

def method_body(text, signature):
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
            if c == '/' and n == '/':
                state = 'line'; i += 2; continue
            if c == '/' and n == '*':
                state = 'block'; i += 2; continue
            if c == '"':
                state = 'string'; i += 1; continue
            if c == "'":
                state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0:
                    return start, i + 1, text[start:i + 1]
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

if not R.is_file() or not B.is_file():
    fail('generated renderer/build missing')
renderer = R.read_text()
build = B.read_text()
if R005 not in renderer:
    fail('R005 generated parent required')
if R006 in renderer:
    print(PREFIX + ' patch already present')
    sys.exit(0)

renderer, _ = replace_once(
    renderer,
    '        const int Rev35R005SourceChunkHardCap = 64;\n',
    '        const int Rev35R005SourceChunkHardCap = 64;\n'
    '        // R006 attacks recurring managed/LOH churn without caching completed Entries.\n'
    '        // Only retired exact-length geographic arrays are retained, with a hard cap.\n'
    '        const string Rev35R006Variant = "' + R006 + '";\n'
    '        const long Rev35R006GeographicPoolMaximumBytes = 8L * 1024L * 1024L;\n'
    '        const int Rev35R006GeographicPoolMaximumArrays = 16;\n',
    'R006 identity/pool bounds')

renderer, _ = replace_once(
    renderer,
    '            internal long StartedTicks;\n',
    '            internal long StartedTicks;\n'
    '            internal long Rev35R006FinalizeReadyTicks;\n',
    'R006 finalize-ready timestamp')

renderer, _ = replace_once(
    renderer,
    '        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);\n',
    '        readonly Queue<Mesh> meshPool = new Queue<Mesh>(MaximumPooledMeshes);\n'
    '        readonly Dictionary<int, Stack<GeographicUnitPoint[]>> rev35R006GeographicPool =\n'
    '            new Dictionary<int, Stack<GeographicUnitPoint[]>>();\n'
    '        long rev35R006GeographicPoolBytes;\n'
    '        int rev35R006GeographicPoolArrays;\n',
    'R006 geographic pool state')

renderer, _ = replace_once(
    renderer,
    '        int operationHealthRev35R005PackedChunkMaxItems;\n',
    '        int operationHealthRev35R005PackedChunkMaxItems;\n'
    '        long operationHealthRev35R006GeoPoolHit;\n'
    '        long operationHealthRev35R006GeoPoolMiss;\n'
    '        long operationHealthRev35R006GeoPoolReject;\n'
    '        long operationHealthRev35R006GeoPoolRecycle;\n'
    '        long operationHealthRev35R006ProjectedOwnershipTransfers;\n'
    '        double operationHealthRev35R006GeoAllocationMaxMs;\n'
    '        int operationHealthRev35R006GeoMaxItems;\n'
    '        long operationHealthRev35R006FinalizeWaitSamples;\n'
    '        double operationHealthRev35R006FinalizeWaitMaxMs;\n'
    '        int operationHealthRev35R006FoundationMissingFar;\n'
    '        int operationHealthRev35R006FoundationMissingPartial;\n'
    '        int operationHealthRev35R006FoundationMissingPending;\n'
    '        int operationHealthRev35R006FoundationMissingRenderReady;\n'
    '        int operationHealthRev35R006FoundationMissingUpstream;\n'
    '        int operationHealthRev35R006ContourOnlyFallback;\n'
    '        int operationHealthRev35R006FoundationSourceIncomplete;\n'
    '        float operationHealthRev35R006FoundationWaitSince = -1f;\n'
    '        int operationHealthRev35R006FoundationWaitThresholdMask;\n'
    '        double operationHealthRev35R006FoundationWaitCurrentMs;\n'
    '        double operationHealthRev35R006FoundationWaitMaxMs;\n'
    '        long operationHealthRev35R006FoundationWait500;\n'
    '        long operationHealthRev35R006FoundationWait1000;\n'
    '        long operationHealthRev35R006FoundationWait2000;\n'
    '        long operationHealthRev35R006FoundationWait3000;\n'
    '        long operationHealthRev35R006FoundationWait5000;\n'
    '        long operationHealthRev35R006GpuAttrGrow;\n'
    '        double operationHealthRev35R006GpuAttrGrowMaxMs;\n'
    '        int operationHealthRev35R006GpuAttrCapacityMax;\n',
    'R006 telemetry fields')

helpers = r'''        GeographicUnitPoint[] AcquireRev35R006GeographicBuffer(int length)
        {
            if (length <= 0) return null;
            operationHealthRev35R006GeoMaxItems = Math.Max(
                operationHealthRev35R006GeoMaxItems, length);
            Stack<GeographicUnitPoint[]> stack;
            if (rev35R006GeographicPool.TryGetValue(length, out stack) &&
                stack != null && stack.Count > 0)
            {
                GeographicUnitPoint[] buffer = stack.Pop();
                long bytes = Math.Max(0L, (long)length * 24L);
                rev35R006GeographicPoolBytes = Math.Max(0L,
                    rev35R006GeographicPoolBytes - bytes);
                rev35R006GeographicPoolArrays = Math.Max(0,
                    rev35R006GeographicPoolArrays - 1);
                if (stack.Count == 0) rev35R006GeographicPool.Remove(length);
                operationHealthRev35R006GeoPoolHit++;
                return buffer;
            }
            operationHealthRev35R006GeoPoolMiss++;
            long startTicks = Stopwatch.GetTimestamp();
            GeographicUnitPoint[] created = new GeographicUnitPoint[length];
            double elapsed = (Stopwatch.GetTimestamp() - startTicks) * 1000.0 /
                Stopwatch.Frequency;
            operationHealthRev35R006GeoAllocationMaxMs = Math.Max(
                operationHealthRev35R006GeoAllocationMaxMs, elapsed);
            return created;
        }

        void RecycleRev35R006GeographicBuffer(ref GeographicUnitPoint[] buffer)
        {
            if (buffer == null || buffer.Length <= 0)
            {
                buffer = null;
                return;
            }
            long bytes = Math.Max(0L, (long)buffer.Length * 24L);
            if (rev35R006GeographicPoolArrays >= Rev35R006GeographicPoolMaximumArrays ||
                bytes > Rev35R006GeographicPoolMaximumBytes ||
                rev35R006GeographicPoolBytes + bytes >
                    Rev35R006GeographicPoolMaximumBytes)
            {
                operationHealthRev35R006GeoPoolReject++;
                buffer = null;
                return;
            }
            Stack<GeographicUnitPoint[]> stack;
            if (!rev35R006GeographicPool.TryGetValue(buffer.Length, out stack) ||
                stack == null)
            {
                stack = new Stack<GeographicUnitPoint[]>();
                rev35R006GeographicPool[buffer.Length] = stack;
            }
            stack.Push(buffer);
            rev35R006GeographicPoolBytes += bytes;
            rev35R006GeographicPoolArrays++;
            operationHealthRev35R006GeoPoolRecycle++;
            buffer = null;
        }

        void RecycleRev35R006EntryGeographic(Entry entry)
        {
            if (entry == null) return;
            RecycleRev35R006GeographicBuffer(
                ref entry.PackedTerrainGeographicPoints);
            RecycleRev35R006GeographicBuffer(ref entry.ContourGeographicPoints);
            RecycleRev35R006GeographicBuffer(ref entry.CoastlineGeographicPoints);
        }

        void ClearRev35R006GeographicPool()
        {
            rev35R006GeographicPool.Clear();
            rev35R006GeographicPoolBytes = 0L;
            rev35R006GeographicPoolArrays = 0;
        }

        double CurrentRev35R006FinalizeWaitMilliseconds()
        {
            if (pendingEntryCommit == null ||
                pendingEntryCommit.Stage != PendingEntryCommitStage.Finalize ||
                pendingEntryCommit.Rev35R006FinalizeReadyTicks <= 0L) return 0.0;
            return Math.Max(0.0,
                (Stopwatch.GetTimestamp() -
                    pendingEntryCommit.Rev35R006FinalizeReadyTicks) * 1000.0 /
                    Stopwatch.Frequency);
        }

        static bool Rev35R006ContourOnlyStyleDifference(string left, string right)
        {
            if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) ||
                string.Equals(left, right, StringComparison.Ordinal)) return false;
            int l1 = left.IndexOf('|'), r1 = right.IndexOf('|');
            int l2 = left.LastIndexOf('|'), r2 = right.LastIndexOf('|');
            if (l1 <= 0 || r1 <= 0 || l2 <= l1 || r2 <= r1) return false;
            if (l1 != r1 || left.Length - l2 != right.Length - r2) return false;
            if (string.CompareOrdinal(left, 0, right, 0, l1) != 0) return false;
            int suffixLength = left.Length - l2 - 1;
            return string.CompareOrdinal(left, l2 + 1, right, r2 + 1,
                suffixLength) == 0;
        }

        void ObserveRev35R006FoundationCriticalPath(
            AERISTerrainVisibleTileSet visible, AERISTerrainHeightTile[] tiles,
            Entry[] currentEntries, Entry[] fallbackEntries, string styleKey,
            int readyGlobal, int readyFar)
        {
            int missing = 0, partial = 0, pending = 0, renderReady = 0, upstream = 0;
            int contourOnlyFallback = 0;
            if (visible != null && tiles != null)
            {
                for (int i = 0; i < tiles.Length; i++)
                {
                    AERISTerrainHeightTile tile = tiles[i];
                    if (tile == null || tile.Key.Lod != AERISTerrainTileLod.Far)
                        continue;
                    Entry current = currentEntries != null && i < currentEntries.Length ?
                        currentEntries[i] : null;
                    if (current != null && current.CoverageFraction >= 0.999f)
                        continue;
                    missing++;
                    if (current != null)
                    {
                        partial++;
                        continue;
                    }
                    string cacheKey = CacheKey(tile.Key, tile.CreatedUtcTicks, styleKey);
                    if (pendingEntryCommit != null &&
                        string.Equals(pendingEntryCommit.CacheKey, cacheKey,
                            StringComparison.Ordinal))
                        pending++;
                    else if (renderReadyFields.ContainsKey(cacheKey))
                        renderReady++;
                    else
                        upstream++;
                    Entry fallback = fallbackEntries != null &&
                        i < fallbackEntries.Length ? fallbackEntries[i] : null;
                    if (fallback != null &&
                        Rev35R006ContourOnlyStyleDifference(fallback.StyleKey, styleKey))
                        contourOnlyFallback++;
                }
            }
            operationHealthRev35R006FoundationMissingFar = missing;
            operationHealthRev35R006FoundationMissingPartial = partial;
            operationHealthRev35R006FoundationMissingPending = pending;
            operationHealthRev35R006FoundationMissingRenderReady = renderReady;
            operationHealthRev35R006FoundationMissingUpstream = upstream;
            operationHealthRev35R006ContourOnlyFallback = contourOnlyFallback;
            bool sourceIncomplete = visible != null && !visible.FoundationComplete;
            operationHealthRev35R006FoundationSourceIncomplete =
                sourceIncomplete ? 1 : 0;

            bool waiting = visible != null &&
                (sourceIncomplete || readyFar < visible.FarFoundationCount || missing > 0);
            float now = Time.realtimeSinceStartup;
            if (!waiting)
            {
                operationHealthRev35R006FoundationWaitSince = -1f;
                operationHealthRev35R006FoundationWaitThresholdMask = 0;
                operationHealthRev35R006FoundationWaitCurrentMs = 0.0;
                return;
            }
            if (operationHealthRev35R006FoundationWaitSince < 0f)
                operationHealthRev35R006FoundationWaitSince = now;
            double elapsed = Math.Max(0.0,
                (now - operationHealthRev35R006FoundationWaitSince) * 1000.0);
            operationHealthRev35R006FoundationWaitCurrentMs = elapsed;
            operationHealthRev35R006FoundationWaitMaxMs = Math.Max(
                operationHealthRev35R006FoundationWaitMaxMs, elapsed);
            int mask = operationHealthRev35R006FoundationWaitThresholdMask;
            if (elapsed >= 500.0 && (mask & 1) == 0)
            {
                operationHealthRev35R006FoundationWait500++; mask |= 1;
            }
            if (elapsed >= 1000.0 && (mask & 2) == 0)
            {
                operationHealthRev35R006FoundationWait1000++; mask |= 2;
            }
            if (elapsed >= 2000.0 && (mask & 4) == 0)
            {
                operationHealthRev35R006FoundationWait2000++; mask |= 4;
            }
            if (elapsed >= 3000.0 && (mask & 8) == 0)
            {
                operationHealthRev35R006FoundationWait3000++; mask |= 8;
            }
            if (elapsed >= 5000.0 && (mask & 16) == 0)
            {
                operationHealthRev35R006FoundationWait5000++; mask |= 16;
            }
            operationHealthRev35R006FoundationWaitThresholdMask = mask;
        }

'''
geo_sig = '        bool AdvancePendingGeographic(Vector3[] source,\n'
renderer, _ = replace_once(renderer, geo_sig, helpers + geo_sig,
                           'R006 helper insertion')

old_geo_alloc = '''            if (output == null || output.Length != source.Length)
                output = new GeographicUnitPoint[source.Length];
'''
new_geo_alloc = '''            if (output == null || output.Length != source.Length)
            {
                if (output != null)
                    RecycleRev35R006GeographicBuffer(ref output);
                output = AcquireRev35R006GeographicBuffer(source.Length);
                if (mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=
                    budgetMilliseconds)
                    return false;
            }
'''
renderer, _ = replace_once(renderer, old_geo_alloc, new_geo_alloc,
                           'R006 geographic allocation reuse')

renderer, _ = replace_once(
    renderer,
    '                        pending.Stage = PendingEntryCommitStage.Finalize;\n',
    '                        pending.Rev35R006FinalizeReadyTicks = Stopwatch.GetTimestamp();\n'
    '                        pending.Stage = PendingEntryCommitStage.Finalize;\n',
    'R006 finalize-ready timestamp set')

f0, f1, finalize = method_body(
    renderer, '        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
finalize, _ = replace_once(
    finalize,
    '            AERISTerrainRenderReadyHeightField result = pending.Result;\n',
    '            AERISTerrainRenderReadyHeightField result = pending.Result;\n'
    '            if (pending.Rev35R006FinalizeReadyTicks > 0L)\n'
    '            {\n'
    '                double finalizeWait = Math.Max(0.0,\n'
    '                    (Stopwatch.GetTimestamp() - pending.Rev35R006FinalizeReadyTicks) *\n'
    '                    1000.0 / Stopwatch.Frequency);\n'
    '                operationHealthRev35R006FinalizeWaitSamples++;\n'
    '                operationHealthRev35R006FinalizeWaitMaxMs = Math.Max(\n'
    '                    operationHealthRev35R006FinalizeWaitMaxMs, finalizeWait);\n'
    '            }\n',
    'R006 finalize wait measurement')
for old, new, label in (
    ('PackedTerrainProjectedVertices = AllocateProjectedVertices(pending.PackedSource),',
     'PackedTerrainProjectedVertices = pending.PackedSource,',
     'R006 packed projected ownership transfer'),
    ('ContourProjectedVertices = AllocateProjectedVertices(pending.ContourSource),',
     'ContourProjectedVertices = pending.ContourSource,',
     'R006 contour projected ownership transfer'),
    ('CoastlineProjectedVertices = AllocateProjectedVertices(pending.CoastlineSource),',
     'CoastlineProjectedVertices = pending.CoastlineSource,',
     'R006 coastline projected ownership transfer'),
):
    finalize, _ = replace_once(finalize, old, new, label)
finalize, _ = replace_once(
    finalize,
    '            pending.PackedMesh = null;\n',
    '            operationHealthRev35R006ProjectedOwnershipTransfers += 3;\n'
    '            pending.PackedMesh = null;\n',
    'R006 projected transfer telemetry')
renderer = renderer[:f0] + finalize + renderer[f1:]

c0, c1, cancel = method_body(renderer, '        void CancelPendingEntryCommit()')
cancel, _ = replace_once(
    cancel,
    '            pendingEntryCommit = null;\n',
    '            RecycleRev35R006GeographicBuffer(ref pending.PackedGeographic);\n'
    '            RecycleRev35R006GeographicBuffer(ref pending.ContourGeographic);\n'
    '            RecycleRev35R006GeographicBuffer(ref pending.CoastlineGeographic);\n'
    '            pendingEntryCommit = null;\n',
    'R006 cancel geographic recycle')
renderer = renderer[:c0] + cancel + renderer[c1:]

r0, r1, release = method_body(
    renderer, '        void ReleaseDeferredEntryRetirements(bool force)')
if 'RecycleMesh(ref entry.PackedTerrainMesh);' not in release:
    fail('R006 deferred release Mesh authority missing')
release, _ = replace_once(
    release,
    '                RecycleMesh(ref entry.PackedTerrainMesh);\n',
    '                RecycleRev35R006EntryGeographic(entry);\n'
    '                RecycleMesh(ref entry.PackedTerrainMesh);\n',
    'R006 deferred geographic recycle')
renderer = renderer[:r0] + release + renderer[r1:]

rm0, rm1, remove = method_body(renderer, '        void Remove(Entry entry)')
if 'RecycleMesh(ref entry.PackedTerrainMesh);' in remove:
    remove, _ = replace_once(
        remove,
        '            RecycleMesh(ref entry.PackedTerrainMesh);\n',
        '            RecycleRev35R006EntryGeographic(entry);\n'
        '            RecycleMesh(ref entry.PackedTerrainMesh);\n',
        'R006 direct remove geographic recycle')
    renderer = renderer[:rm0] + remove + renderer[rm1:]

foundation_anchor = '''                contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,
                    tiles, currentEntriesScratch, out readyGlobal, out readyFar);
'''
renderer, _ = replace_once(
    renderer, foundation_anchor,
    foundation_anchor +
    '''                ObserveRev35R006FoundationCriticalPath(visible, tiles,
                    currentEntriesScratch, fallbackEntriesScratch, styleKey,
                    readyGlobal, readyFar);
''',
    'R006 foundation observer')

gpu_old = '''            if (gpuVertexGeographicScratch.Capacity < points.Length)
                gpuVertexGeographicScratch.Capacity = points.Length;
'''
gpu_new = '''            if (gpuVertexGeographicScratch.Capacity < points.Length)
            {
                long rev35R006GrowStart = Stopwatch.GetTimestamp();
                gpuVertexGeographicScratch.Capacity = points.Length;
                double rev35R006GrowMs = (Stopwatch.GetTimestamp() -
                    rev35R006GrowStart) * 1000.0 / Stopwatch.Frequency;
                operationHealthRev35R006GpuAttrGrow++;
                operationHealthRev35R006GpuAttrGrowMaxMs = Math.Max(
                    operationHealthRev35R006GpuAttrGrowMaxMs, rev35R006GrowMs);
                operationHealthRev35R006GpuAttrCapacityMax = Math.Max(
                    operationHealthRev35R006GpuAttrCapacityMax,
                    gpuVertexGeographicScratch.Capacity);
            }
'''
renderer, _ = replace_once(renderer, gpu_old, gpu_new,
                           'R006 GPU attribute growth observer')

telemetry_anchor = (
    '                "; oh_rev35_r005_packed_chunk_max_items=" + '
    'operationHealthRev35R005PackedChunkMaxItems +\n')
telemetry_new = telemetry_anchor + (
    '                "; oh_rev35_r006_variant=" + Rev35R006Variant +\n'
    '                "; oh_rev35_r006_geo_pool_hit=" + operationHealthRev35R006GeoPoolHit +\n'
    '                "; oh_rev35_r006_geo_pool_miss=" + operationHealthRev35R006GeoPoolMiss +\n'
    '                "; oh_rev35_r006_geo_pool_reject=" + operationHealthRev35R006GeoPoolReject +\n'
    '                "; oh_rev35_r006_geo_pool_recycle=" + operationHealthRev35R006GeoPoolRecycle +\n'
    '                "; oh_rev35_r006_geo_pool_arrays=" + rev35R006GeographicPoolArrays +\n'
    '                "; oh_rev35_r006_geo_pool_bytes=" + rev35R006GeographicPoolBytes +\n'
    '                "; oh_rev35_r006_geo_alloc_max_ms=" + operationHealthRev35R006GeoAllocationMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_geo_max_items=" + operationHealthRev35R006GeoMaxItems +\n'
    '                "; oh_rev35_r006_projected_transfer=" + operationHealthRev35R006ProjectedOwnershipTransfers +\n'
    '                "; oh_rev35_r006_finalize_wait_current_ms=" + CurrentRev35R006FinalizeWaitMilliseconds().ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_finalize_wait_max_ms=" + operationHealthRev35R006FinalizeWaitMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_finalize_wait_samples=" + operationHealthRev35R006FinalizeWaitSamples +\n'
    '                "; oh_rev35_r006_missing_far=" + operationHealthRev35R006FoundationMissingFar +\n'
    '                "; oh_rev35_r006_missing_partial=" + operationHealthRev35R006FoundationMissingPartial +\n'
    '                "; oh_rev35_r006_missing_pending=" + operationHealthRev35R006FoundationMissingPending +\n'
    '                "; oh_rev35_r006_missing_render_ready=" + operationHealthRev35R006FoundationMissingRenderReady +\n'
    '                "; oh_rev35_r006_missing_upstream=" + operationHealthRev35R006FoundationMissingUpstream +\n'
    '                "; oh_rev35_r006_contour_only_fallback=" + operationHealthRev35R006ContourOnlyFallback +\n'
    '                "; oh_rev35_r006_source_incomplete=" + operationHealthRev35R006FoundationSourceIncomplete +\n'
    '                "; oh_rev35_r006_foundation_wait_ms=" + operationHealthRev35R006FoundationWaitCurrentMs.ToString("F1", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_foundation_wait_max_ms=" + operationHealthRev35R006FoundationWaitMaxMs.ToString("F1", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_wait_500=" + operationHealthRev35R006FoundationWait500 +\n'
    '                "; oh_rev35_r006_wait_1000=" + operationHealthRev35R006FoundationWait1000 +\n'
    '                "; oh_rev35_r006_wait_2000=" + operationHealthRev35R006FoundationWait2000 +\n'
    '                "; oh_rev35_r006_wait_3000=" + operationHealthRev35R006FoundationWait3000 +\n'
    '                "; oh_rev35_r006_wait_5000=" + operationHealthRev35R006FoundationWait5000 +\n'
    '                "; oh_rev35_r006_gpu_attr_grow=" + operationHealthRev35R006GpuAttrGrow +\n'
    '                "; oh_rev35_r006_gpu_attr_grow_max_ms=" + operationHealthRev35R006GpuAttrGrowMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_gpu_attr_capacity_max=" + operationHealthRev35R006GpuAttrCapacityMax +\n')
renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                           'R006 telemetry append')

if 'REV3_5_R006_VARIANT="' + R006 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R005_VARIANT="' + R005 + '"\n',
        'REV3_5_R005_VARIANT="' + R005 + '"\n'
        'REV3_5_R006_VARIANT="' + R006 + '"\n',
        'build R006 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r005_split_weight_flow_lanes.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py"\n',
        'build R006 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r005_variant=%s\\n\' "$REV3_5_R005_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r005_variant=%s\\n\' "$REV3_5_R005_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r006_variant=%s\\n\' "$REV3_5_R006_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate R006 identity')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.',
    'WaitManagedPreparation', 'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    if forbidden in renderer:
        fail('rejected mechanism present after R006: ' + forbidden)

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('parent=' + R005)
print('r006=' + R006)
print('geo_pool=exact-length retired managed arrays only; 8MiB/16 arrays HARD CAP')
print('projected_fallback=ownership transfer from already-complete source arrays')
print('foundation=observer only; readiness/publication semantics unchanged')
print('worker_count_change=0 task_run=0 speculative=0 presentation_cache=0 quality_change=0 10Hz_change=0 160km_change=0')
