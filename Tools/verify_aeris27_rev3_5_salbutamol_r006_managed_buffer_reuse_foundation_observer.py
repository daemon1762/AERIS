#!/usr/bin/env python3
from pathlib import Path
import re
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 OH REV3.5 SALBUTAMOL SULFATE R006 VERIFY]'
MARKERS = (
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R001',
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R002_PACKED_ALLOCATION_SPLIT',
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R003_REQUESTED_VIEW_ADMISSION',
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R004_ADAPTIVE_HIGH_FLOW_COMMIT',
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R005_SPLIT_WEIGHT_FLOW_LANES',
    'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_MANAGED_BUFFER_REUSE_FOUNDATION_OBSERVER',
)
R006 = MARKERS[-1]
checks = []


def check(value, label):
    ok = bool(value)
    checks.append((ok, label))
    print(('[PASS] ' if ok else '[FAIL] ') + label)


def method_body(text, signature):
    start = text.find(signature)
    if start < 0:
        return ''
    op = text.find('{', start)
    if op < 0:
        return ''
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
check(B.is_file(), 'build exists')
if not R.is_file() or not B.is_file():
    raise SystemExit(1)
renderer = R.read_text()
build = B.read_text()
renderer_flat = ' '.join(renderer.split())
for marker in MARKERS:
    check(marker in renderer, 'lineage marker retained: ' + marker)
check('AERIS25_PHASE6_003_AUTHORITATIVE_PUBLICATION' in renderer and
      'AERIS26_REV003_OBSERVER_M1' not in renderer,
      'Phase6_003 renderer authority retained; Observer identity remains out of renderer')

check('Rev35R006GeographicPoolMaximumBytes = 8L * 1024L * 1024L' in renderer,
      'geographic reuse pool hard byte cap is exactly 8 MiB')
legacy_r006_pool_cap = 'Rev35R006GeographicPoolMaximumArrays = 16' in renderer
accepted_r029_pool_cap = (
    'Rev35R006GeographicPoolMaximumArrays = 128' in renderer and
    'Rev35R029GeographicPoolEvictionMaximumPerRecycle = 4' in renderer
)
check(legacy_r006_pool_cap or accepted_r029_pool_cap,
      'geographic reuse pool cap is legacy R006 or exact accepted R029 descendant')
check('Dictionary<int, Stack<GeographicUnitPoint[]>> rev35R006GeographicPool' in renderer,
      'pool stores only exact-length bare GeographicUnitPoint arrays')
acquire = method_body(renderer,
    '        GeographicUnitPoint[] AcquireRev35R006GeographicBuffer(int length)')
recycle = method_body(renderer,
    '        void RecycleRev35R006GeographicBuffer(ref GeographicUnitPoint[] buffer)')
rebalance = method_body(renderer,
    '        bool TryMakeRoomRev35R029GeographicPool(int incomingLength, long incomingBytes)')
check(acquire and 'new GeographicUnitPoint[length]' in acquire and
      'rev35R006GeographicPool.TryGetValue(length' in acquire and
      'operationHealthRev35R006GeoPoolHit++' in acquire and
      'operationHealthRev35R006GeoPoolMiss++' in acquire,
      'pool miss allocates exact length and pool hit reuses exact length')
legacy_r006_recycle = (
    recycle and
    'Rev35R006GeographicPoolMaximumArrays' in recycle and
    'Rev35R006GeographicPoolMaximumBytes' in recycle and
    'stack.Push(buffer)' in recycle and
    'operationHealthRev35R006GeoPoolReject++' in recycle
)
accepted_r029_recycle = (
    recycle and rebalance and accepted_r029_pool_cap and
    'TryMakeRoomRev35R029GeographicPool(buffer.Length, bytes)' in recycle and
    'stack.Push(buffer)' in recycle and
    'operationHealthRev35R006GeoPoolReject++' in recycle and
    'operationHealthRev35R006GeoPoolRecycle++' in recycle and
    'Rev35R006GeographicPoolMaximumArrays' in rebalance and
    'Rev35R006GeographicPoolMaximumBytes' in rebalance and
    'Rev35R029GeographicPoolEvictionMaximumPerRecycle' in rebalance and
    'selectedStack.Pop()' in rebalance and
    'rev35R006GeographicPoolBytes = Math.Max(0L,' in rebalance and
    'rev35R006GeographicPoolBytes - selectedBytes' in rebalance and
    'rev35R006GeographicPoolArrays = Math.Max(0,' in rebalance and
    'rev35R006GeographicPoolArrays - 1' in rebalance and
    'operationHealthRev35R029GeoPoolEvicted++' in rebalance and
    'operationHealthRev35R029GeoPoolEvictedBytes += selectedBytes' in rebalance and
    'evictions++' in rebalance and
    'return fits;' in rebalance
)
check(legacy_r006_recycle or accepted_r029_recycle,
      'pool recycle path is legacy R006 bounded path or exact accepted R029 rebalance descendant')
check('Entry' not in acquire and 'Mesh' not in acquire and
      'Entry' not in recycle and 'Mesh' not in recycle,
      'pool is not a completed Entry or Unity Mesh presentation cache')

geo = method_body(renderer,
    '        bool AdvancePendingGeographic(Vector3[] source,')
check(geo, 'AdvancePendingGeographic resolved')
check('AcquireRev35R006GeographicBuffer(source.Length)' in geo and
      'output = new GeographicUnitPoint[source.Length]' not in geo,
      'Geographic stage removes direct recurring exact-output array allocation')
acquire_pos = geo.find('AcquireRev35R006GeographicBuffer(source.Length)')
budget_pos = geo.find('mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=', acquire_pos)
loop_pos = geo.find('while (cursor < source.Length)', acquire_pos)
check(0 <= acquire_pos < budget_pos < loop_pos,
      'Geographic allocation/reuse is followed by budget checkpoint before trig loop')
check('(iterations & 31) == 0' in geo and
      'mainThreadCommitStopwatch.Elapsed.TotalMilliseconds >=' in geo,
      'Geographic trig conversion remains resumable every 32 items')

finalize = method_body(renderer,
    '        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
check(finalize, 'FinalizePendingEntryCommit resolved')
for source_name, entry_name in (
    ('pending.PackedSource', 'PackedTerrainProjectedVertices'),
    ('pending.ContourSource', 'ContourProjectedVertices'),
    ('pending.CoastlineSource', 'CoastlineProjectedVertices'),
):
    check((entry_name + ' = ' + source_name + ',') in finalize,
          entry_name + ' takes ownership of completed source buffer')
    check(('AllocateProjectedVertices(' + source_name + ')') not in finalize,
          entry_name + ' no longer creates a duplicate same-length Vector3 array')
check('operationHealthRev35R006ProjectedOwnershipTransfers += 3;' in finalize,
      'projected ownership transfers are observable')
check('PackedTerrainGeographicPoints = pending.PackedGeographic' in finalize and
      'ContourGeographicPoints = pending.ContourGeographic' in finalize and
      'CoastlineGeographicPoints = pending.CoastlineGeographic' in finalize,
      'Entry geographic authority remains exact and unchanged')

cancel = method_body(renderer, '        void CancelPendingEntryCommit()')
check(cancel and cancel.count('RecycleRev35R006GeographicBuffer(') == 3 and
      'RecycleMesh(ref pending.PackedMesh);' in cancel,
      'cancelled partial commit returns all three geographic arrays and native Meshes')
release = method_body(renderer,
    '        void ReleaseDeferredEntryRetirements(bool force)')
check(release and 'presentationEntryPins.Contains(entry)' in release and
      'RecycleRev35R006EntryGeographic(entry);' in release and
      'RecycleMesh(ref entry.PackedTerrainMesh);' in release,
      'retired geographic arrays recycle only at accepted snapshot-safe Mesh retirement')
pin_pos = release.find('presentationEntryPins.Contains(entry)')
geo_release_pos = release.find('RecycleRev35R006EntryGeographic(entry)')
mesh_release_pos = release.find('RecycleMesh(ref entry.PackedTerrainMesh)')
check(0 <= pin_pos < geo_release_pos < mesh_release_pos,
      'snapshot pin guard precedes geographic and Mesh recycling')
remove = method_body(renderer, '        void Remove(Entry entry)')
if 'RecycleMesh(ref entry.PackedTerrainMesh);' in remove:
    check('RecycleRev35R006EntryGeographic(entry);' in remove and
          remove.find('RecycleRev35R006EntryGeographic(entry)') <
          remove.find('RecycleMesh(ref entry.PackedTerrainMesh)'),
          'direct accepted Remove mirrors existing Mesh lifetime for geographic arrays')
else:
    check(True, 'direct Remove has no native Mesh recycle path to mirror')

advance = method_body(renderer,
    '        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,')
pump = method_body(renderer,
    '        void PumpStagedCompletedCommit(AERISTerrainTileSystem system,')
check(advance and 'bool allowPublication' in advance and
      'case PendingEntryCommitStage.Finalize:' in advance and
      'if (!allowPublication)' in advance and
      'FinalizePendingEntryCommit(pending, system)' in advance,
      'Phase6_003 publication authority gate is unchanged')
check(pump and 'bool allowPublication' in pump and
      'operationHealthMainCommitPublicationDeferrals++' in pump,
      'publication deferral contract remains observable')
non_tick_start = renderer.find('            if (!authoritativeTickDue)')
non_tick_end = renderer.find('            operationHealthAuthoritativeTicks++;', non_tick_start)
non_tick = renderer[non_tick_start:non_tick_end] if non_tick_start >= 0 and non_tick_end > non_tick_start else ''
check('PumpStagedCompletedCommit(system, false);' in non_tick and
      'PumpStagedCompletedCommit(system, true);' not in non_tick,
      'hidden Repaint still cannot publish')
check('presentationNow + 0.10f' in renderer,
      'fixed visible 10 Hz authority retained')

check('pending.Rev35R006FinalizeReadyTicks = Stopwatch.GetTimestamp();' in advance,
      'entry records when all geographic work reaches Finalize')
check('CurrentRev35R006FinalizeWaitMilliseconds()' in renderer and
      'operationHealthRev35R006FinalizeWaitMaxMs' in finalize,
      'actual Finalize publication wait is measured separately from call-count deferrals')

observer = method_body(renderer,
    '        void ObserveRev35R006FoundationCriticalPath(')
check(observer, 'foundation critical-path observer resolved')
for token in ('missing++', 'partial++', 'pending++', 'renderReady++', 'upstream++',
              'contourOnlyFallback++', 'visible.FoundationComplete'):
    check(token in observer, 'foundation observer contains ' + token)
for forbidden in ('SwapFrontAndBack(', 'requestedViewReady =',
                  'contentFoundationCoverage =', 'foundationComplete ='):
    check(forbidden not in observer,
          'foundation observer cannot change presentation authority: ' + forbidden)
measure_anchor = 'contentFoundationCoverage = MeasureFoundationGpuReadiness(visible,'
observe_anchor = 'ObserveRev35R006FoundationCriticalPath(visible, tiles,'
check(measure_anchor in renderer and observe_anchor in renderer and
      renderer.find(measure_anchor) < renderer.find(observe_anchor),
      'foundation observer runs after accepted readiness measurement')
legacy_r006_foundation_gate = (
    'foundationComplete = rendered && visible.FoundationComplete &&' in renderer and
    'lastBackFoundationCoverage >= 0.999f' in renderer and
    'readyFar >= visible.FarFoundationCount' in renderer
)
accepted_r018_foundation_gate = (
    'bool r018VisibleGpuComplete = operationHealthRev35R018VisiblePlanValid && operationHealthRev35R018VisibleRequiredFar > 0 && operationHealthRev35R018VisibleReadyFar >= operationHealthRev35R018VisibleRequiredFar;' in renderer_flat and
    'bool r018OverscanGpuComplete = visible.FoundationComplete && lastBackFoundationCoverage >= 0.999f && readyFar >= visible.FarFoundationCount;' in renderer_flat and
    'foundationComplete = rendered && r018VisibleGpuComplete;' in renderer_flat and
    'if (!r018OverscanGpuComplete) operationHealthRev35R018OverscanHolAvoided++;' in renderer_flat and
    'foundationComplete = rendered && r018VisibleGpuComplete && r018OverscanGpuComplete' not in renderer_flat
)
check(legacy_r006_foundation_gate or accepted_r018_foundation_gate,
      'foundation swap gate is legacy R006 or exact accepted R018 visible-GPU descendant')
check(renderer.count('Rev35R006ContourOnlyStyleDifference(') == 2,
      'contour-only fallback logic is observer-only helper plus one observer call')

check('gpuVertexGeographicScratch.Capacity = points.Length;' in renderer and
      'operationHealthRev35R006GpuAttrGrow++' in renderer and
      'operationHealthRev35R006GpuAttrGrowMaxMs' in renderer,
      'GPU geographic attribute scratch growth is measured without changing capacity policy')

for token in (
    'oh_rev35_r006_variant=', 'oh_rev35_r006_geo_pool_hit=',
    'oh_rev35_r006_geo_pool_miss=', 'oh_rev35_r006_geo_pool_reject=',
    'oh_rev35_r006_geo_pool_recycle=', 'oh_rev35_r006_geo_pool_arrays=',
    'oh_rev35_r006_geo_pool_bytes=', 'oh_rev35_r006_geo_alloc_max_ms=',
    'oh_rev35_r006_geo_max_items=', 'oh_rev35_r006_projected_transfer=',
    'oh_rev35_r006_finalize_wait_current_ms=', 'oh_rev35_r006_finalize_wait_max_ms=',
    'oh_rev35_r006_finalize_wait_samples=', 'oh_rev35_r006_missing_far=',
    'oh_rev35_r006_missing_partial=', 'oh_rev35_r006_missing_pending=',
    'oh_rev35_r006_missing_render_ready=', 'oh_rev35_r006_missing_upstream=',
    'oh_rev35_r006_contour_only_fallback=', 'oh_rev35_r006_source_incomplete=',
    'oh_rev35_r006_foundation_wait_ms=', 'oh_rev35_r006_foundation_wait_max_ms=',
    'oh_rev35_r006_wait_500=', 'oh_rev35_r006_wait_1000=',
    'oh_rev35_r006_wait_2000=', 'oh_rev35_r006_wait_3000=',
    'oh_rev35_r006_wait_5000=', 'oh_rev35_r006_gpu_attr_grow=',
    'oh_rev35_r006_gpu_attr_grow_max_ms=', 'oh_rev35_r006_gpu_attr_capacity_max=',
):
    check(token in renderer, 'runtime telemetry publishes ' + token)

check('REV3_5_R006_VARIANT="' + R006 + '"' in build,
      'build records R006 identity')
check('verify_aeris27_rev3_5_salbutamol_r006_managed_buffer_reuse_foundation_observer.py' in build,
      'build invokes final R006 verifier')
check('rev3_5_r006_variant=%s' in build,
      'candidate build identity records R006')

for forbidden in (
    'Task.Run(', 'new Thread(', 'ThreadPool.', 'WaitManagedPreparation',
    'ResidentPreparedPresentation',
    'AERIS25_PHASE6_004_MANAGED_PREPARATION_PIPELINE',
    'AERIS25_PHASE6_005_NONBLOCKING_SPECULATIVE_PREPARATION',
    'AERIS25_PHASE7_001_DIAZEPAM_RESIDENT_RAM_REUSE',
):
    check(forbidden not in renderer, 'renderer excludes rejected mechanism: ' + forbidden)
check('RenderTextureFormat.ARGB32' in renderer and 'FilterMode.Bilinear' in renderer,
      'Golden ARGB32/Bilinear render target retained')
draw = method_body(renderer,
    '        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
terrain = draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)')
contour = draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)')
coast = draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)')
check(0 <= terrain < contour < coast,
      'painter order remains terrain -> contour -> coastline')
check('runwayMapLockErrorPx=' in renderer and 'visualCoverage=' in renderer,
      'Runway Map Lock and Golden visual coverage telemetry retained')

failed = [label for ok, label in checks if not ok]
print('\n' + PREFIX + ' %d/%d PASS' % (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print(PREFIX + ' STATIC PASS')
