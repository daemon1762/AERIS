#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 R006 PACKED MANAGED BUFFER REUSE HOTFIX4 VERIFY]'
R006 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
HF4 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_PACKED_MANAGED_BUFFER_REUSE_HOTFIX4'
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method_body(text, signature):
    start = text.find(signature)
    if start < 0: return ''
    op = text.find('{', start)
    if op < 0: return ''
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
                if depth == 0: return text[start:i + 1]
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
    return ''


check(R.is_file(), 'renderer exists')
check(T.is_file(), 'tile system exists')
check(B.is_file(), 'build exists')
if not R.is_file() or not T.is_file() or not B.is_file():
    raise SystemExit(1)
renderer = R.read_text()
tile = T.read_text()
build = B.read_text()
check(R006 in renderer, 'R006 parent marker retained')
check(HF3 in tile, 'HF3 complete-coverage parent retained')
check(HF4 in renderer, 'HF4 marker present')

check('Rev35R006Hf4ColourPoolMaximumBytes = 16L * 1024L * 1024L' in renderer and
      'Rev35R006Hf4ColourPoolMaximumArrays = 128' in renderer,
      'Color32 exact-length pool is hard bounded at 16 MiB / 128 arrays')
check('Rev35R006Hf4IndexPoolMaximumBytes = 8L * 1024L * 1024L' in renderer and
      'Rev35R006Hf4IndexPoolMaximumArrays = 128' in renderer,
      'index exact-length pool is hard bounded at 8 MiB / 128 arrays')
check('Dictionary<int, Stack<Color32[]>> rev35R006Hf4ColourPool' in renderer,
      'Color32 pool is exact-length keyed')
check('Dictionary<int, Stack<int[]>> rev35R006Hf4IndexPool' in renderer,
      'index pool is exact-length keyed')

colour_acquire = method_body(renderer,
    '        Color32[] AcquireRev35R006Hf4ColourBuffer(int length)')
colour_recycle = method_body(renderer,
    '        void RecycleRev35R006Hf4ColourBuffer(ref Color32[] buffer)')
index_acquire = method_body(renderer,
    '        int[] AcquireRev35R006Hf4IndexBuffer(int length)')
index_recycle = method_body(renderer,
    '        void RecycleRev35R006Hf4IndexBuffer(ref int[] buffer)')
check(colour_acquire and 'new Color32[length]' in colour_acquire and
      'rev35R006Hf4ColourPool.TryGetValue(length' in colour_acquire and
      'operationHealthRev35R006Hf4ColourPoolHit++' in colour_acquire and
      'operationHealthRev35R006Hf4ColourPoolMiss++' in colour_acquire,
      'Color32 pool miss allocates exact length; hit reuses exact length')
check(colour_recycle and 'Rev35R006Hf4ColourPoolMaximumArrays' in colour_recycle and
      'Rev35R006Hf4ColourPoolMaximumBytes' in colour_recycle and
      'stack.Push(buffer)' in colour_recycle and
      'operationHealthRev35R006Hf4ColourPoolReject++' in colour_recycle,
      'Color32 recycle is hard bounded and observable')
check(index_acquire and 'new int[length]' in index_acquire and
      'rev35R006Hf4IndexPool.TryGetValue(length' in index_acquire and
      'operationHealthRev35R006Hf4IndexPoolHit++' in index_acquire and
      'operationHealthRev35R006Hf4IndexPoolMiss++' in index_acquire,
      'index pool miss allocates exact length; hit reuses exact length')
check(index_recycle and 'Rev35R006Hf4IndexPoolMaximumArrays' in index_recycle and
      'Rev35R006Hf4IndexPoolMaximumBytes' in index_recycle and
      'stack.Push(buffer)' in index_recycle and
      'operationHealthRev35R006Hf4IndexPoolReject++' in index_recycle,
      'index recycle is hard bounded and observable')
for body, label in ((colour_acquire, 'Color32 acquire'),
                    (colour_recycle, 'Color32 recycle'),
                    (index_acquire, 'index acquire'),
                    (index_recycle, 'index recycle')):
    check('Entry' not in body and 'Mesh' not in body,
          label + ' does not cache completed Entry or Unity Mesh')

packed = method_body(renderer,
    '        bool AdvancePendingPackedTerrain(PendingEntryCommit pending,')
check(packed, 'packed prepare method resolved')
check('pending.PackedSource = new Vector3[count];' in packed,
      'PackedSource remains non-pooled Entry projected-geometry authority')
check('AcquireRev35R006Hf4ColourBuffer(count)' in packed and
      'pending.PackedColours = new Color32[count];' not in packed,
      'recurring packed Color32 direct allocation removed')
check('AcquireRev35R006Hf4IndexBuffer(count)' in packed and
      'pending.PackedIndices = new int[count];' not in packed,
      'recurring packed index direct allocation removed')
check(packed.count('operationHealthRev35R004AllocationContinues++;') == 3,
      'R004 budget-aware split-allocation cadence remains intact')

upload = method_body(renderer,
    '        Mesh UploadPreparedPackedTerrainMesh(string name, PendingEntryCommit pending)')
check(upload and 'mesh.vertices = pending.PackedSource;' in upload and
      'mesh.colors32 = pending.PackedColours;' in upload and
      'mesh.triangles = pending.PackedIndices;' in upload and
      'mesh.UploadMeshData(false);' in upload,
      'Unity Mesh upload consumes all three packed managed buffers before retirement')

finalize = method_body(renderer,
    '        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
check(finalize, 'FinalizePendingEntryCommit resolved')
colour_owner = finalize.find('PackedTerrainColours = pending.PackedColours,')
colour_transfer = finalize.find('operationHealthRev35R006Hf4ColourOwnershipTransfer++')
colour_null = finalize.find('pending.PackedColours = null;', colour_transfer)
index_recycle_pos = finalize.find(
    'RecycleRev35R006Hf4IndexBuffer(ref pending.PackedIndices);')
check(0 <= colour_owner < colour_transfer < colour_null,
      'Entry takes Color32 ownership before pending reference is cleared')
check(index_recycle_pos > colour_owner,
      'index buffer recycles only after Entry accounting/publication construction')
check('RecycleRev35R006Hf4ColourBuffer(ref pending.PackedColours)' not in finalize,
      'successful Finalize does not prematurely recycle Entry-owned Color32 buffer')
check('RecycleRev35R006Hf4IndexBuffer(ref pending.PackedSource)' not in finalize and
      'RecycleRev35R006Hf4ColourBuffer(ref pending.PackedSource)' not in finalize,
      'PackedSource is never returned to HF4 pools')
check('PackedTerrainProjectedVertices = pending.PackedSource,' in finalize,
      'R006 projected ownership transfer remains authoritative')

cancel = method_body(renderer, '        void CancelPendingEntryCommit()')
check(cancel and
      'RecycleRev35R006Hf4ColourBuffer(ref pending.PackedColours);' in cancel and
      'RecycleRev35R006Hf4IndexBuffer(ref pending.PackedIndices);' in cancel,
      'cancel path returns unpublished Color32 and index buffers')
check('RecycleRev35R006Hf4ColourBuffer(ref pending.PackedSource)' not in cancel and
      'RecycleRev35R006Hf4IndexBuffer(ref pending.PackedSource)' not in cancel,
      'cancel path does not treat PackedSource as HF4 scratch')

release = method_body(renderer,
    '        void ReleaseDeferredEntryRetirements(bool force)')
check(release and 'presentationEntryPins.Contains(entry)' in release and
      'RecycleRev35R006Hf4EntryPackedBuffers(entry);' in release and
      'RecycleRev35R006EntryGeographic(entry);' in release and
      'RecycleMesh(ref entry.PackedTerrainMesh);' in release,
      'snapshot-safe deferred retirement returns Color32 beside R006 geographic buffers')
pin = release.find('presentationEntryPins.Contains(entry)')
colour_retire = release.find('RecycleRev35R006Hf4EntryPackedBuffers(entry)')
geo_retire = release.find('RecycleRev35R006EntryGeographic(entry)')
mesh_retire = release.find('RecycleMesh(ref entry.PackedTerrainMesh)')
check(0 <= pin < colour_retire < geo_retire < mesh_retire,
      'presentation pin guard precedes all HF4/R006/native recycling')

remove = method_body(renderer, '        void Remove(Entry entry)')
if 'RecycleMesh(ref entry.PackedTerrainMesh);' in remove:
    check('RecycleRev35R006Hf4EntryPackedBuffers(entry);' in remove and
          remove.find('RecycleRev35R006Hf4EntryPackedBuffers(entry)') <
          remove.find('RecycleMesh(ref entry.PackedTerrainMesh)'),
          'direct accepted Remove mirrors existing native lifetime for Color32')
else:
    check(True, 'direct Remove has no native PackedTerrainMesh retirement path')

release_gpu = method_body(renderer, '        void ReleaseGpuResources()')
reset = release_gpu.find('ResetContentSnapshot();')
post_clear = release_gpu.find('ClearRev35R006Hf4PackedPools();', reset)
destroy = release_gpu.find('DestroyRenderTargets();', reset)
check(0 <= reset < post_clear < destroy,
      'full teardown performs final HF4 pool drain after ResetContentSnapshot')
check(release_gpu.count('ClearRev35R006Hf4PackedPools();') >= 2,
      'HF1/HF2 pre/post-reset full-release drains both include HF4 pools')

for token in (
    'oh_rev35_r006_hf4_variant=',
    'oh_rev35_r006_hf4_colour_pool_hit=',
    'oh_rev35_r006_hf4_colour_pool_miss=',
    'oh_rev35_r006_hf4_colour_pool_recycle=',
    'oh_rev35_r006_hf4_colour_pool_reject=',
    'oh_rev35_r006_hf4_colour_pool_arrays=',
    'oh_rev35_r006_hf4_colour_pool_bytes=',
    'oh_rev35_r006_hf4_colour_new_alloc_max_ms=',
    'oh_rev35_r006_hf4_colour_max_items=',
    'oh_rev35_r006_hf4_colour_transfer=',
    'oh_rev35_r006_hf4_index_pool_hit=',
    'oh_rev35_r006_hf4_index_pool_miss=',
    'oh_rev35_r006_hf4_index_pool_recycle=',
    'oh_rev35_r006_hf4_index_pool_reject=',
    'oh_rev35_r006_hf4_index_pool_arrays=',
    'oh_rev35_r006_hf4_index_pool_bytes=',
    'oh_rev35_r006_hf4_index_new_alloc_max_ms=',
    'oh_rev35_r006_hf4_index_max_items=',
):
    check(token in renderer, 'runtime telemetry publishes ' + token)

check('REV3_5_R006_HOTFIX4="' + HF4 + '"' in build,
      'build records HF4 identity')
check('verify_aeris27_rev3_5_salbutamol_r006_packed_managed_buffer_reuse_hotfix4.py' in build,
      'build invokes HF4 verifier')
check('rev3_5_r006_hotfix4=%s' in build,
      'candidate identity records HF4')

check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz authority retained')
check('RenderTextureFormat.ARGB32' in renderer and 'FilterMode.Bilinear' in renderer,
      'Golden ARGB32/Bilinear render target retained')
check('FinalizePendingEntryCommit(pending, system)' in renderer,
      'Finalize-only publication retained')
for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
    'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    check(forbidden not in renderer, 'renderer excludes rejected mechanism: ' + forbidden)

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
