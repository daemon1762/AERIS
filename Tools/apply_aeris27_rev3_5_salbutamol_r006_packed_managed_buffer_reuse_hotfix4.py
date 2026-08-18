#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 PACKED MANAGED BUFFER REUSE HOTFIX4]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF2 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_RESOURCE_RELEASE_ORDER_HOTFIX2'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'


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
if R006 not in renderer or HF2 not in renderer:
    fail('R006 + HF2 generated renderer parent required')
if HF3 not in tile or 'REV3_5_R006_HOTFIX3="' + HF3 + '"' not in build:
    fail('HF3 complete-coverage generated parent required')
if HF4 in renderer:
    print(PREFIX + ' already present')
    raise SystemExit(0)

renderer, _ = replace_once(
    renderer,
    '        const int Rev35R006GeographicPoolMaximumArrays = 16;\n',
    '        const int Rev35R006GeographicPoolMaximumArrays = 16;\n'
    '        // ' + HF4 + ': reuse only managed buffers whose ownership has ended.\n'
    '        // PackedSource remains Entry projected-geometry authority and is never pooled.\n'
    '        const string Rev35R006PackedManagedBufferHotfix4 = "' + HF4 + '";\n'
    '        const long Rev35R006Hf4ColourPoolMaximumBytes = 16L * 1024L * 1024L;\n'
    '        const int Rev35R006Hf4ColourPoolMaximumArrays = 128;\n'
    '        const long Rev35R006Hf4IndexPoolMaximumBytes = 8L * 1024L * 1024L;\n'
    '        const int Rev35R006Hf4IndexPoolMaximumArrays = 128;\n',
    'HF4 identity and hard bounds')

renderer, _ = replace_once(
    renderer,
    '        int rev35R006GeographicPoolArrays;\n',
    '        int rev35R006GeographicPoolArrays;\n'
    '        readonly Dictionary<int, Stack<Color32[]>> rev35R006Hf4ColourPool =\n'
    '            new Dictionary<int, Stack<Color32[]>>();\n'
    '        readonly Dictionary<int, Stack<int[]>> rev35R006Hf4IndexPool =\n'
    '            new Dictionary<int, Stack<int[]>>();\n'
    '        long rev35R006Hf4ColourPoolBytes;\n'
    '        int rev35R006Hf4ColourPoolArrays;\n'
    '        long rev35R006Hf4IndexPoolBytes;\n'
    '        int rev35R006Hf4IndexPoolArrays;\n',
    'HF4 pool state')

renderer, _ = replace_once(
    renderer,
    '        int operationHealthRev35R006GpuAttrCapacityMax;\n',
    '        int operationHealthRev35R006GpuAttrCapacityMax;\n'
    '        long operationHealthRev35R006Hf4ColourPoolHit;\n'
    '        long operationHealthRev35R006Hf4ColourPoolMiss;\n'
    '        long operationHealthRev35R006Hf4ColourPoolRecycle;\n'
    '        long operationHealthRev35R006Hf4ColourPoolReject;\n'
    '        long operationHealthRev35R006Hf4ColourOwnershipTransfer;\n'
    '        double operationHealthRev35R006Hf4ColourNewAllocMaxMs;\n'
    '        int operationHealthRev35R006Hf4ColourMaxItems;\n'
    '        long operationHealthRev35R006Hf4IndexPoolHit;\n'
    '        long operationHealthRev35R006Hf4IndexPoolMiss;\n'
    '        long operationHealthRev35R006Hf4IndexPoolRecycle;\n'
    '        long operationHealthRev35R006Hf4IndexPoolReject;\n'
    '        double operationHealthRev35R006Hf4IndexNewAllocMaxMs;\n'
    '        int operationHealthRev35R006Hf4IndexMaxItems;\n',
    'HF4 telemetry fields')

helpers = r'''        Color32[] AcquireRev35R006Hf4ColourBuffer(int length)
        {
            if (length <= 0) return null;
            operationHealthRev35R006Hf4ColourMaxItems = Math.Max(
                operationHealthRev35R006Hf4ColourMaxItems, length);
            Stack<Color32[]> stack;
            if (rev35R006Hf4ColourPool.TryGetValue(length, out stack) &&
                stack != null && stack.Count > 0)
            {
                Color32[] buffer = stack.Pop();
                long bytes = Math.Max(0L, (long)length * 4L);
                rev35R006Hf4ColourPoolBytes = Math.Max(0L,
                    rev35R006Hf4ColourPoolBytes - bytes);
                rev35R006Hf4ColourPoolArrays = Math.Max(0,
                    rev35R006Hf4ColourPoolArrays - 1);
                if (stack.Count == 0) rev35R006Hf4ColourPool.Remove(length);
                operationHealthRev35R006Hf4ColourPoolHit++;
                return buffer;
            }
            operationHealthRev35R006Hf4ColourPoolMiss++;
            long started = Stopwatch.GetTimestamp();
            Color32[] created = new Color32[length];
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency;
            operationHealthRev35R006Hf4ColourNewAllocMaxMs = Math.Max(
                operationHealthRev35R006Hf4ColourNewAllocMaxMs, elapsed);
            return created;
        }

        void RecycleRev35R006Hf4ColourBuffer(ref Color32[] buffer)
        {
            if (buffer == null || buffer.Length <= 0)
            {
                buffer = null;
                return;
            }
            long bytes = Math.Max(0L, (long)buffer.Length * 4L);
            if (rev35R006Hf4ColourPoolArrays >=
                    Rev35R006Hf4ColourPoolMaximumArrays ||
                bytes > Rev35R006Hf4ColourPoolMaximumBytes ||
                rev35R006Hf4ColourPoolBytes + bytes >
                    Rev35R006Hf4ColourPoolMaximumBytes)
            {
                operationHealthRev35R006Hf4ColourPoolReject++;
                buffer = null;
                return;
            }
            Stack<Color32[]> stack;
            if (!rev35R006Hf4ColourPool.TryGetValue(buffer.Length, out stack) ||
                stack == null)
            {
                stack = new Stack<Color32[]>();
                rev35R006Hf4ColourPool[buffer.Length] = stack;
            }
            stack.Push(buffer);
            rev35R006Hf4ColourPoolBytes += bytes;
            rev35R006Hf4ColourPoolArrays++;
            operationHealthRev35R006Hf4ColourPoolRecycle++;
            buffer = null;
        }

        int[] AcquireRev35R006Hf4IndexBuffer(int length)
        {
            if (length <= 0) return null;
            operationHealthRev35R006Hf4IndexMaxItems = Math.Max(
                operationHealthRev35R006Hf4IndexMaxItems, length);
            Stack<int[]> stack;
            if (rev35R006Hf4IndexPool.TryGetValue(length, out stack) &&
                stack != null && stack.Count > 0)
            {
                int[] buffer = stack.Pop();
                long bytes = Math.Max(0L, (long)length * 4L);
                rev35R006Hf4IndexPoolBytes = Math.Max(0L,
                    rev35R006Hf4IndexPoolBytes - bytes);
                rev35R006Hf4IndexPoolArrays = Math.Max(0,
                    rev35R006Hf4IndexPoolArrays - 1);
                if (stack.Count == 0) rev35R006Hf4IndexPool.Remove(length);
                operationHealthRev35R006Hf4IndexPoolHit++;
                return buffer;
            }
            operationHealthRev35R006Hf4IndexPoolMiss++;
            long started = Stopwatch.GetTimestamp();
            int[] created = new int[length];
            double elapsed = (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency;
            operationHealthRev35R006Hf4IndexNewAllocMaxMs = Math.Max(
                operationHealthRev35R006Hf4IndexNewAllocMaxMs, elapsed);
            return created;
        }

        void RecycleRev35R006Hf4IndexBuffer(ref int[] buffer)
        {
            if (buffer == null || buffer.Length <= 0)
            {
                buffer = null;
                return;
            }
            long bytes = Math.Max(0L, (long)buffer.Length * 4L);
            if (rev35R006Hf4IndexPoolArrays >=
                    Rev35R006Hf4IndexPoolMaximumArrays ||
                bytes > Rev35R006Hf4IndexPoolMaximumBytes ||
                rev35R006Hf4IndexPoolBytes + bytes >
                    Rev35R006Hf4IndexPoolMaximumBytes)
            {
                operationHealthRev35R006Hf4IndexPoolReject++;
                buffer = null;
                return;
            }
            Stack<int[]> stack;
            if (!rev35R006Hf4IndexPool.TryGetValue(buffer.Length, out stack) ||
                stack == null)
            {
                stack = new Stack<int[]>();
                rev35R006Hf4IndexPool[buffer.Length] = stack;
            }
            stack.Push(buffer);
            rev35R006Hf4IndexPoolBytes += bytes;
            rev35R006Hf4IndexPoolArrays++;
            operationHealthRev35R006Hf4IndexPoolRecycle++;
            buffer = null;
        }

        void RecycleRev35R006Hf4EntryPackedBuffers(Entry entry)
        {
            if (entry == null) return;
            RecycleRev35R006Hf4ColourBuffer(ref entry.PackedTerrainColours);
        }

        void ClearRev35R006Hf4PackedPools()
        {
            rev35R006Hf4ColourPool.Clear();
            rev35R006Hf4IndexPool.Clear();
            rev35R006Hf4ColourPoolBytes = 0L;
            rev35R006Hf4ColourPoolArrays = 0;
            rev35R006Hf4IndexPoolBytes = 0L;
            rev35R006Hf4IndexPoolArrays = 0;
        }

'''
anchor = '        GeographicUnitPoint[] AcquireRev35R006GeographicBuffer(int length)\n'
renderer, _ = replace_once(renderer, anchor, helpers + anchor,
                           'HF4 helper insertion before R006 geographic pool')

p0, p1 = method_bounds(renderer,
    '        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,')
packed = renderer[p0:p1]
packed, _ = replace_once(
    packed,
    '                            pending.PackedColours = new Color32[count];\n',
    '                            pending.PackedColours =\n'
    '                                AcquireRev35R006Hf4ColourBuffer(count);\n',
    'HF4 packed colour acquisition')
packed, _ = replace_once(
    packed,
    '                            pending.PackedIndices = new int[count];\n',
    '                            pending.PackedIndices =\n'
    '                                AcquireRev35R006Hf4IndexBuffer(count);\n',
    'HF4 packed index acquisition')
if 'pending.PackedSource = new Vector3[count];' not in packed:
    fail('HF4 refuses to pool PackedSource ownership authority')
renderer = renderer[:p0] + packed + renderer[p1:]

f0, f1 = method_bounds(renderer,
    '        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
finalize = renderer[f0:f1]
if 'PackedTerrainColours = pending.PackedColours,' not in finalize:
    fail('HF4 Entry colour ownership anchor missing')
finalize, _ = replace_once(
    finalize,
    '            operationHealthRev35R006ProjectedOwnershipTransfers += 3;\n'
    '            pending.PackedMesh = null;\n',
    '            operationHealthRev35R006ProjectedOwnershipTransfers += 3;\n'
    '            // Entry now owns PackedColours for dynamic REL/TOPO recolouring.\n'
    '            operationHealthRev35R006Hf4ColourOwnershipTransfer++;\n'
    '            pending.PackedColours = null;\n'
    '            // Unity has copied triangle indices and Finalize has finished accounting.\n'
    '            RecycleRev35R006Hf4IndexBuffer(ref pending.PackedIndices);\n'
    '            pending.PackedMesh = null;\n',
    'HF4 successful ownership transfer / index recycle')
if 'RecycleRev35R006Hf4ColourBuffer(ref pending.PackedColours)' in finalize or \
   'RecycleRev35R006Hf4IndexBuffer(ref pending.PackedSource)' in finalize:
    fail('HF4 unsafe Finalize recycle detected')
renderer = renderer[:f0] + finalize + renderer[f1:]

c0, c1 = method_bounds(renderer, '        void CancelPendingEntryCommit()')
cancel = renderer[c0:c1]
cancel, _ = replace_once(
    cancel,
    '            RecycleRev35R006GeographicBuffer(ref pending.PackedGeographic);\n',
    '            RecycleRev35R006Hf4ColourBuffer(ref pending.PackedColours);\n'
    '            RecycleRev35R006Hf4IndexBuffer(ref pending.PackedIndices);\n'
    '            RecycleRev35R006GeographicBuffer(ref pending.PackedGeographic);\n',
    'HF4 cancel packed recycle')
renderer = renderer[:c0] + cancel + renderer[c1:]

# R006 already identified every snapshot-safe Entry retirement point. Mirror those exact
# sites so an Entry-held Color32 buffer cannot be reused while a presentation packet pins it.
retire_anchor = 'RecycleRev35R006EntryGeographic(entry);'
retire_count = renderer.count(retire_anchor)
if retire_count <= 0:
    fail('HF4 R006 safe retirement anchors missing')
renderer = renderer.replace(
    retire_anchor,
    'RecycleRev35R006Hf4EntryPackedBuffers(entry);\n'
    '                ' + retire_anchor)

# HF1/HF2 already clear the geographic pool on full teardown, including the final post-reset
# drain. Clear HF4 pools at the same full-release sites; ordinary eviction continues to reuse.
clear_anchor = 'ClearRev35R006GeographicPool();'
clear_count = renderer.count(clear_anchor)
if clear_count <= 0:
    fail('HF4 full-teardown geographic clear anchor missing')
renderer = renderer.replace(
    clear_anchor,
    clear_anchor + '\n            ClearRev35R006Hf4PackedPools();')

telemetry_anchor = (
    '                "; oh_rev35_r006_gpu_attr_capacity_max=" + '
    'operationHealthRev35R006GpuAttrCapacityMax +\n')
telemetry_new = telemetry_anchor + (
    '                "; oh_rev35_r006_hf4_variant=" + Rev35R006PackedManagedBufferHotfix4 +\n'
    '                "; oh_rev35_r006_hf4_colour_pool_hit=" + operationHealthRev35R006Hf4ColourPoolHit +\n'
    '                "; oh_rev35_r006_hf4_colour_pool_miss=" + operationHealthRev35R006Hf4ColourPoolMiss +\n'
    '                "; oh_rev35_r006_hf4_colour_pool_recycle=" + operationHealthRev35R006Hf4ColourPoolRecycle +\n'
    '                "; oh_rev35_r006_hf4_colour_pool_reject=" + operationHealthRev35R006Hf4ColourPoolReject +\n'
    '                "; oh_rev35_r006_hf4_colour_pool_arrays=" + rev35R006Hf4ColourPoolArrays +\n'
    '                "; oh_rev35_r006_hf4_colour_pool_bytes=" + rev35R006Hf4ColourPoolBytes +\n'
    '                "; oh_rev35_r006_hf4_colour_new_alloc_max_ms=" + operationHealthRev35R006Hf4ColourNewAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_hf4_colour_max_items=" + operationHealthRev35R006Hf4ColourMaxItems +\n'
    '                "; oh_rev35_r006_hf4_colour_transfer=" + operationHealthRev35R006Hf4ColourOwnershipTransfer +\n'
    '                "; oh_rev35_r006_hf4_index_pool_hit=" + operationHealthRev35R006Hf4IndexPoolHit +\n'
    '                "; oh_rev35_r006_hf4_index_pool_miss=" + operationHealthRev35R006Hf4IndexPoolMiss +\n'
    '                "; oh_rev35_r006_hf4_index_pool_recycle=" + operationHealthRev35R006Hf4IndexPoolRecycle +\n'
    '                "; oh_rev35_r006_hf4_index_pool_reject=" + operationHealthRev35R006Hf4IndexPoolReject +\n'
    '                "; oh_rev35_r006_hf4_index_pool_arrays=" + rev35R006Hf4IndexPoolArrays +\n'
    '                "; oh_rev35_r006_hf4_index_pool_bytes=" + rev35R006Hf4IndexPoolBytes +\n'
    '                "; oh_rev35_r006_hf4_index_new_alloc_max_ms=" + operationHealthRev35R006Hf4IndexNewAllocMaxMs.ToString("F3", CultureInfo.InvariantCulture) +\n'
    '                "; oh_rev35_r006_hf4_index_max_items=" + operationHealthRev35R006Hf4IndexMaxItems +\n')
renderer, _ = replace_once(renderer, telemetry_anchor, telemetry_new,
                           'HF4 telemetry append')

if 'REV3_5_R006_HOTFIX4="' + HF4 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R006_HOTFIX3="' + HF3 + '"\n',
        'REV3_5_R006_HOTFIX3="' + HF3 + '"\n'
        'REV3_5_R006_HOTFIX4="' + HF4 + '"\n',
        'build HF4 identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py"\n',
        'build HF4 verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r006_hotfix3=%s\\n\' "$REV3_5_R006_HOTFIX3" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r006_hotfix3=%s\\n\' "$REV3_5_R006_HOTFIX3" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r006_hotfix4=%s\\n\' "$REV3_5_R006_HOTFIX4" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'candidate HF4 identity')

R.write_text(renderer)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('hf4=' + HF4)
print('colour_pool=EXACT_LENGTH 16MiB/128; lifetime=ENTRY_RETIREMENT')
print('index_pool=EXACT_LENGTH 8MiB/128; lifetime=POST_UPLOAD_FINALIZE')
print('packed_source_pool=0; projected Entry ownership retained')
print('quality_change=0 authority_change=0 worker_count_change=0 10Hz_change=0 160km_change=0')
