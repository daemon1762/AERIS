#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
T = ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs'
O = ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs'
B = ROOT / 'build_ubuntu.sh'
PREFIX = '[AERIS27 REV3.5 R007 MANAGED HEAP ATTRIBUTION]'
HF3 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R006_COMPLETE_COVERAGE_CONTRACT_HOTFIX3'
R007 = 'AERIS27_REV3_5_SALBUTAMOL_SULFATE_R007_MANAGED_HEAP_ATTRIBUTION'


def fail(message):
    raise SystemExit(PREFIX + ' ' + message)


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        fail('%s anchor mismatch old=%d' % (label, count))
    return text.replace(old, new, 1), True


for path in (R, T, O, B):
    if not path.is_file():
        fail('required generated file missing: ' + str(path))
renderer = R.read_text()
tile = T.read_text()
oh = O.read_text()
build = B.read_text()
if HF3 not in tile or 'REV3_5_R006_HOTFIX3="' + HF3 + '"' not in build:
    fail('R006 HF3 generated parent required')
if R007 in renderer and R007 in tile and R007 in oh:
    print(PREFIX + ' already present')
    raise SystemExit(0)

# ---------------------------------------------------------------------------
# Renderer: low-rate heap-positive attribution across the 5 Hz content path.
# GC.GetTotalMemory(false) is a global heap observation, not exact allocation accounting;
# name every metric "heap positive window" and quarantine samples crossed by Gen2 GC.
# ---------------------------------------------------------------------------
renderer, _ = replace_once(
    renderer,
    '        int operationHealthRev35R006GpuAttrCapacityMax;\n',
    '        int operationHealthRev35R006GpuAttrCapacityMax;\n'
    '        const string Rev35R007Variant = "' + R007 + '";\n'
    '        long operationHealthRev35R007HeapCheckpoint;\n'
    '        int operationHealthRev35R007Gen2Checkpoint;\n'
    '        long operationHealthRev35R007HeapGcCollision;\n'
    '        long operationHealthRev35R007DrainSamples;\n'
    '        long operationHealthRev35R007DrainPositiveBytes;\n'
    '        long operationHealthRev35R007DrainPositiveMaxBytes;\n'
    '        long operationHealthRev35R007CaptureSamples;\n'
    '        long operationHealthRev35R007CapturePositiveBytes;\n'
    '        long operationHealthRev35R007CapturePositiveMaxBytes;\n'
    '        long operationHealthRev35R007ResolveSamples;\n'
    '        long operationHealthRev35R007ResolvePositiveBytes;\n'
    '        long operationHealthRev35R007ResolvePositiveMaxBytes;\n'
    '        long operationHealthRev35R007PruneSamples;\n'
    '        long operationHealthRev35R007PrunePositiveBytes;\n'
    '        long operationHealthRev35R007PrunePositiveMaxBytes;\n',
    'R007 renderer fields')

renderer_helper = r'''        void BeginRev35R007HeapWindow()
        {
            operationHealthRev35R007HeapCheckpoint = GC.GetTotalMemory(false);
            operationHealthRev35R007Gen2Checkpoint = GC.CollectionCount(2);
        }

        void ObserveRev35R007HeapWindow(ref long samples, ref long positiveBytes,
            ref long positiveMaxBytes)
        {
            long current = GC.GetTotalMemory(false);
            int gen2 = GC.CollectionCount(2);
            samples++;
            if (gen2 != operationHealthRev35R007Gen2Checkpoint)
                operationHealthRev35R007HeapGcCollision++;
            else
            {
                long delta = current - operationHealthRev35R007HeapCheckpoint;
                if (delta > 0L)
                {
                    positiveBytes += delta;
                    positiveMaxBytes = Math.Max(positiveMaxBytes, delta);
                }
            }
            operationHealthRev35R007HeapCheckpoint = current;
            operationHealthRev35R007Gen2Checkpoint = gen2;
        }

'''
anchor = '        GeographicUnitPoint[] AcquireRev35R006GeographicBuffer(int length)\n'
if renderer_helper.strip() not in renderer:
    if renderer.count(anchor) != 1:
        fail('R007 renderer helper anchor mismatch')
    renderer = renderer.replace(anchor, renderer_helper + anchor, 1)

renderer, _ = replace_once(
    renderer,
    '                operationHealthContentTicks++;\n'
    '                if (workerResultReady) operationHealthContentWorkerDrains++;\n'
    '                DrainCompleted(system);\n',
    '                operationHealthContentTicks++;\n'
    '                if (workerResultReady) operationHealthContentWorkerDrains++;\n'
    '                BeginRev35R007HeapWindow();\n'
    '                DrainCompleted(system);\n'
    '                ObserveRev35R007HeapWindow(\n'
    '                    ref operationHealthRev35R007DrainSamples,\n'
    '                    ref operationHealthRev35R007DrainPositiveBytes,\n'
    '                    ref operationHealthRev35R007DrainPositiveMaxBytes);\n',
    'R007 drain attribution')

renderer, _ = replace_once(
    renderer,
    '                operationHealthContentCaptures++;\n'
    '                if (visible == null || visible.Tiles == null ||\n',
    '                operationHealthContentCaptures++;\n'
    '                ObserveRev35R007HeapWindow(\n'
    '                    ref operationHealthRev35R007CaptureSamples,\n'
    '                    ref operationHealthRev35R007CapturePositiveBytes,\n'
    '                    ref operationHealthRev35R007CapturePositiveMaxBytes);\n'
    '                if (visible == null || visible.Tiles == null ||\n',
    'R007 capture attribution')

foundation_observer = '''                ObserveRev35R006FoundationCriticalPath(visible, tiles,
                    currentEntriesScratch, fallbackEntriesScratch, styleKey,
                    readyGlobal, readyFar);
'''
renderer, _ = replace_once(
    renderer,
    foundation_observer,
    foundation_observer +
    '''                ObserveRev35R007HeapWindow(
                    ref operationHealthRev35R007ResolveSamples,
                    ref operationHealthRev35R007ResolvePositiveBytes,
                    ref operationHealthRev35R007ResolvePositiveMaxBytes);
''',
    'R007 resolve attribution')

renderer, _ = replace_once(
    renderer,
    '            if (contentTickRequired)\n'
    '            {\n'
    '                Prune(ResolveVramLimitBytes());\n'
    '                PruneRenderReady(ResolveRenderReadyLimitBytes());\n'
    '            }\n',
    '            if (contentTickRequired)\n'
    '            {\n'
    '                BeginRev35R007HeapWindow();\n'
    '                Prune(ResolveVramLimitBytes());\n'
    '                PruneRenderReady(ResolveRenderReadyLimitBytes());\n'
    '                ObserveRev35R007HeapWindow(\n'
    '                    ref operationHealthRev35R007PruneSamples,\n'
    '                    ref operationHealthRev35R007PrunePositiveBytes,\n'
    '                    ref operationHealthRev35R007PrunePositiveMaxBytes);\n'
    '            }\n',
    'R007 prune attribution')

renderer_telemetry_anchor = (
    '                "; oh_rev35_r006_gpu_attr_capacity_max=" + '
    'operationHealthRev35R006GpuAttrCapacityMax +\n')
renderer_telemetry_new = renderer_telemetry_anchor + (
    '                "; oh_rev35_r007_variant=" + Rev35R007Variant +\n'
    '                "; oh_rev35_r007_heap_gc_collision=" + operationHealthRev35R007HeapGcCollision +\n'
    '                "; oh_rev35_r007_drain_samples=" + operationHealthRev35R007DrainSamples +\n'
    '                "; oh_rev35_r007_drain_pos_bytes=" + operationHealthRev35R007DrainPositiveBytes +\n'
    '                "; oh_rev35_r007_drain_pos_max_bytes=" + operationHealthRev35R007DrainPositiveMaxBytes +\n'
    '                "; oh_rev35_r007_capture_samples=" + operationHealthRev35R007CaptureSamples +\n'
    '                "; oh_rev35_r007_capture_pos_bytes=" + operationHealthRev35R007CapturePositiveBytes +\n'
    '                "; oh_rev35_r007_capture_pos_max_bytes=" + operationHealthRev35R007CapturePositiveMaxBytes +\n'
    '                "; oh_rev35_r007_resolve_samples=" + operationHealthRev35R007ResolveSamples +\n'
    '                "; oh_rev35_r007_resolve_pos_bytes=" + operationHealthRev35R007ResolvePositiveBytes +\n'
    '                "; oh_rev35_r007_resolve_pos_max_bytes=" + operationHealthRev35R007ResolvePositiveMaxBytes +\n'
    '                "; oh_rev35_r007_prune_samples=" + operationHealthRev35R007PruneSamples +\n'
    '                "; oh_rev35_r007_prune_pos_bytes=" + operationHealthRev35R007PrunePositiveBytes +\n'
    '                "; oh_rev35_r007_prune_pos_max_bytes=" + operationHealthRev35R007PrunePositiveMaxBytes +\n')
renderer, _ = replace_once(renderer, renderer_telemetry_anchor,
                           renderer_telemetry_new, 'R007 renderer telemetry')

# ---------------------------------------------------------------------------
# TileSystem: identify allocation outside renderer Draw: 2 s preload-point refresh,
# 5 Hz request planning, and a 2 Hz sampled I/O/scheduler maintenance window.
# ---------------------------------------------------------------------------
tile, _ = replace_once(
    tile,
    '        readonly AERISTerrainTileCacheTelemetry telemetry = new AERISTerrainTileCacheTelemetry();\n',
    '        readonly AERISTerrainTileCacheTelemetry telemetry = new AERISTerrainTileCacheTelemetry();\n'
    '        const string Rev35R007ManagedHeapAttribution = "' + R007 + '";\n'
    '        float rev35R007NextMaintenanceHeapSampleRealtime;\n'
    '        long rev35R007HeapGcCollision;\n'
    '        long rev35R007PreloadPointSamples;\n'
    '        long rev35R007PreloadPointPositiveBytes;\n'
    '        long rev35R007PreloadPointPositiveMaxBytes;\n'
    '        long rev35R007PlanSamples;\n'
    '        long rev35R007PlanPositiveBytes;\n'
    '        long rev35R007PlanPositiveMaxBytes;\n'
    '        long rev35R007MaintenanceSamples;\n'
    '        long rev35R007MaintenancePositiveBytes;\n'
    '        long rev35R007MaintenancePositiveMaxBytes;\n'
    '        long rev35R007ScheduleSamples;\n'
    '        long rev35R007SchedulePositiveBytes;\n'
    '        long rev35R007SchedulePositiveMaxBytes;\n',
    'R007 tile-system fields')

tile_helper = r'''        void ObserveRev35R007PositiveHeap(long beforeBytes, int beforeGen2,
            ref long samples, ref long positiveBytes, ref long positiveMaxBytes)
        {
            long current = GC.GetTotalMemory(false);
            int gen2 = GC.CollectionCount(2);
            samples++;
            if (gen2 != beforeGen2)
            {
                rev35R007HeapGcCollision++;
                return;
            }
            long delta = current - beforeBytes;
            if (delta <= 0L) return;
            positiveBytes += delta;
            positiveMaxBytes = Math.Max(positiveMaxBytes, delta);
        }

'''
tile_anchor = '        void InvalidatePreloadStatusUiSnapshot()\n'
if tile_helper.strip() not in tile:
    if tile.count(tile_anchor) != 1:
        fail('R007 tile helper anchor mismatch')
    tile = tile.replace(tile_anchor, tile_helper + tile_anchor, 1)

# 2-second point refresh: observe only when the refresh actually executes.
tile, _ = replace_once(
    tile,
    '            if (now < nextPreloadPointRefreshRealtime) return;\n'
    '            nextPreloadPointRefreshRealtime = now + 2f;\n'
    '            var values = new List<AERISTerrainPreloadPoint>(256);\n',
    '            if (now < nextPreloadPointRefreshRealtime) return;\n'
    '            nextPreloadPointRefreshRealtime = now + 2f;\n'
    '            long r007HeapBefore = GC.GetTotalMemory(false);\n'
    '            int r007Gen2Before = GC.CollectionCount(2);\n'
    '            var values = new List<AERISTerrainPreloadPoint>(256);\n',
    'R007 preload-point begin')
tile, _ = replace_once(
    tile,
    '            if (preloadBuilder != null) preloadBuilder.UpdatePoints(values);\n'
    '        }\n\n'
    '        void PlanRequests(Vessel vessel, AERISLandingFoundation landing)\n',
    '            if (preloadBuilder != null) preloadBuilder.UpdatePoints(values);\n'
    '            ObserveRev35R007PositiveHeap(r007HeapBefore, r007Gen2Before,\n'
    '                ref rev35R007PreloadPointSamples,\n'
    '                ref rev35R007PreloadPointPositiveBytes,\n'
    '                ref rev35R007PreloadPointPositiveMaxBytes);\n'
    '        }\n\n'
    '        void PlanRequests(Vessel vessel, AERISLandingFoundation landing)\n',
    'R007 preload-point end')

# Planning executes at the existing bounded planning cadence.
tile, _ = replace_once(
    tile,
    '            if (now >= nextPlanRealtime)\n'
    '            {\n'
    '                nextPlanRealtime = now + ResolvePlanningIntervalSeconds(performance);\n'
    '                PlanRequests(vessel, landing);\n'
    '            }\n'
    '            SchedulePreloadReads();\n'
    '            ScheduleResidentPopulationRead();\n'
    '            StartNextRequestIfNeeded();\n',
    '            if (now >= nextPlanRealtime)\n'
    '            {\n'
    '                nextPlanRealtime = now + ResolvePlanningIntervalSeconds(performance);\n'
    '                long r007PlanBefore = GC.GetTotalMemory(false);\n'
    '                int r007PlanGen2 = GC.CollectionCount(2);\n'
    '                PlanRequests(vessel, landing);\n'
    '                ObserveRev35R007PositiveHeap(r007PlanBefore, r007PlanGen2,\n'
    '                    ref rev35R007PlanSamples, ref rev35R007PlanPositiveBytes,\n'
    '                    ref rev35R007PlanPositiveMaxBytes);\n'
    '            }\n'
    '            bool r007MaintenanceSample = now >= rev35R007NextMaintenanceHeapSampleRealtime;\n'
    '            long r007MaintenanceBefore = 0L;\n'
    '            int r007MaintenanceGen2 = 0;\n'
    '            if (r007MaintenanceSample)\n'
    '            {\n'
    '                rev35R007NextMaintenanceHeapSampleRealtime = now + 0.50f;\n'
    '                r007MaintenanceBefore = GC.GetTotalMemory(false);\n'
    '                r007MaintenanceGen2 = GC.CollectionCount(2);\n'
    '            }\n'
    '            SchedulePreloadReads();\n'
    '            ScheduleResidentPopulationRead();\n'
    '            StartNextRequestIfNeeded();\n'
    '            if (r007MaintenanceSample)\n'
    '                ObserveRev35R007PositiveHeap(r007MaintenanceBefore,\n'
    '                    r007MaintenanceGen2, ref rev35R007ScheduleSamples,\n'
    '                    ref rev35R007SchedulePositiveBytes,\n'
    '                    ref rev35R007SchedulePositiveMaxBytes);\n',
    'R007 plan/schedule attribution')

# I/O recovery/retry is an always-on hot path. Sample it at 2 Hz only.
tile, _ = replace_once(
    tile,
    '            RecoverAbandonedIo(now);\n'
    '            RetryPendingDiskWrites(now);\n'
    '            if (now >= nextPlanRealtime)\n',
    '            bool r007IoSample = now >= rev35R007NextMaintenanceHeapSampleRealtime;\n'
    '            long r007IoBefore = 0L;\n'
    '            int r007IoGen2 = 0;\n'
    '            if (r007IoSample)\n'
    '            {\n'
    '                r007IoBefore = GC.GetTotalMemory(false);\n'
    '                r007IoGen2 = GC.CollectionCount(2);\n'
    '            }\n'
    '            RecoverAbandonedIo(now);\n'
    '            RetryPendingDiskWrites(now);\n'
    '            if (r007IoSample)\n'
    '                ObserveRev35R007PositiveHeap(r007IoBefore, r007IoGen2,\n'
    '                    ref rev35R007MaintenanceSamples,\n'
    '                    ref rev35R007MaintenancePositiveBytes,\n'
    '                    ref rev35R007MaintenancePositiveMaxBytes);\n'
    '            if (now >= nextPlanRealtime)\n',
    'R007 IO attribution')

# Extend the existing HF3 CP3 telemetry line with TileSystem heap-positive witnesses.
tile, _ = replace_once(
    tile,
    '                        "; hf3_worst_q=" +\n'
    '                        operationHealthRev35R006Hf3WorstIncompleteQuality + ".");\n',
    '                        "; hf3_worst_q=" +\n'
    '                        operationHealthRev35R006Hf3WorstIncompleteQuality +\n'
    '                        "; r007_variant=" + Rev35R007ManagedHeapAttribution +\n'
    '                        "; r007_heap_gc_collision=" + rev35R007HeapGcCollision +\n'
    '                        "; r007_preload_point_samples=" + rev35R007PreloadPointSamples +\n'
    '                        "; r007_preload_point_pos_bytes=" + rev35R007PreloadPointPositiveBytes +\n'
    '                        "; r007_preload_point_pos_max_bytes=" + rev35R007PreloadPointPositiveMaxBytes +\n'
    '                        "; r007_plan_samples=" + rev35R007PlanSamples +\n'
    '                        "; r007_plan_pos_bytes=" + rev35R007PlanPositiveBytes +\n'
    '                        "; r007_plan_pos_max_bytes=" + rev35R007PlanPositiveMaxBytes +\n'
    '                        "; r007_io_samples=" + rev35R007MaintenanceSamples +\n'
    '                        "; r007_io_pos_bytes=" + rev35R007MaintenancePositiveBytes +\n'
    '                        "; r007_io_pos_max_bytes=" + rev35R007MaintenancePositiveMaxBytes +\n'
    '                        "; r007_schedule_samples=" + rev35R007ScheduleSamples +\n'
    '                        "; r007_schedule_pos_bytes=" + rev35R007SchedulePositiveBytes +\n'
    '                        "; r007_schedule_pos_max_bytes=" + rev35R007SchedulePositiveMaxBytes + ".");\n',
    'R007 TileSystem telemetry')

# ---------------------------------------------------------------------------
# Operation Health: passive Gen2 interval witness. Never force a collection.
# ---------------------------------------------------------------------------
oh, _ = replace_once(
    oh,
    '        internal const string Candidate = "AERIS23_OH_PENICILLIN";\n',
    '        internal const string Candidate = "AERIS23_OH_PENICILLIN";\n'
    '        internal const string Rev35R007ManagedHeapAttribution = "' + R007 + '";\n',
    'R007 OH identity')
oh, _ = replace_once(
    oh,
    '        int lastGcFrame = -1000000;\n',
    '        int lastGcFrame = -1000000;\n'
    '        long rev35R007LastGen2Ticks;\n'
    '        long rev35R007Gen2Events;\n'
    '        double rev35R007Gen2IntervalMinMs = double.MaxValue;\n'
    '        double rev35R007Gen2IntervalMaxMs;\n'
    '        double rev35R007Gen2IntervalSumMs;\n',
    'R007 OH fields')
oh, _ = replace_once(
    oh,
    '            lastGc2 = GC.CollectionCount(2);\n\n'
    '            if (logLevel != AERISOperationHealthLogLevel.Off)\n',
    '            lastGc2 = GC.CollectionCount(2);\n'
    '            rev35R007LastGen2Ticks = now;\n\n'
    '            if (logLevel != AERISOperationHealthLogLevel.Off)\n',
    'R007 OH initialize Gen2 clock')
oh, _ = replace_once(
    oh,
    '            if (d0 + d1 + d2 > 0)\n'
    '                lastGcFrame = Time.frameCount;\n\n'
    '            gc0Window += d0;\n',
    '            if (d0 + d1 + d2 > 0)\n'
    '                lastGcFrame = Time.frameCount;\n'
    '            if (d2 > 0)\n'
    '            {\n'
    '                double intervalMs = rev35R007LastGen2Ticks <= 0L ? 0.0 :\n'
    '                    Math.Max(0.0, (now - rev35R007LastGen2Ticks) * 1000.0 /\n'
    '                        Stopwatch.Frequency);\n'
    '                rev35R007LastGen2Ticks = now;\n'
    '                rev35R007Gen2Events += d2;\n'
    '                if (intervalMs > 0.0)\n'
    '                {\n'
    '                    rev35R007Gen2IntervalMinMs = Math.Min(\n'
    '                        rev35R007Gen2IntervalMinMs, intervalMs);\n'
    '                    rev35R007Gen2IntervalMaxMs = Math.Max(\n'
    '                        rev35R007Gen2IntervalMaxMs, intervalMs);\n'
    '                    rev35R007Gen2IntervalSumMs += intervalMs;\n'
    '                }\n'
    '                long heapBytes = GC.GetTotalMemory(false);\n'
    '                AERISLogger.Warn("[R007_GC] variant=" +\n'
    '                    Rev35R007ManagedHeapAttribution + "; gen2_delta=" + d2 +\n'
    '                    "; interval_ms=" + intervalMs.ToString("F3",\n'
    '                        CultureInfo.InvariantCulture) + "; event_count=" +\n'
    '                    rev35R007Gen2Events + "; interval_min_ms=" +\n'
    '                    (rev35R007Gen2IntervalMinMs == double.MaxValue ? 0.0 :\n'
    '                        rev35R007Gen2IntervalMinMs).ToString("F3",\n'
    '                            CultureInfo.InvariantCulture) +\n'
    '                    "; interval_max_ms=" +\n'
    '                    rev35R007Gen2IntervalMaxMs.ToString("F3",\n'
    '                        CultureInfo.InvariantCulture) +\n'
    '                    "; heap_after_bytes=" + heapBytes + "; forced_gc=0");\n'
    '            }\n\n'
    '            gc0Window += d0;\n',
    'R007 Gen2 observer')

# ---------------------------------------------------------------------------
# Build identity + verifier chain.
# ---------------------------------------------------------------------------
if 'REV3_5_R007_VARIANT="' + R007 + '"' not in build:
    build, _ = replace_once(
        build,
        'REV3_5_R006_HOTFIX3="' + HF3 + '"\n',
        'REV3_5_R006_HOTFIX3="' + HF3 + '"\n'
        'REV3_5_R007_VARIANT="' + R007 + '"\n',
        'R007 build identity')
    build, _ = replace_once(
        build,
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py"\n',
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r006_complete_coverage_contract_hotfix3.py"\n'
        'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris27_rev3_5_salbutamol_r007_managed_heap_attribution.py"\n',
        'R007 build verifier')
    build, _ = replace_once(
        build,
        'printf \'rev3_5_r006_hotfix3=%s\\n\' "$REV3_5_R006_HOTFIX3" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'printf \'rev3_5_r006_hotfix3=%s\\n\' "$REV3_5_R006_HOTFIX3" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n'
        'printf \'rev3_5_r007_variant=%s\\n\' "$REV3_5_R007_VARIANT" >> "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"\n',
        'R007 candidate identity')

R.write_text(renderer)
T.write_text(tile)
O.write_text(oh)
B.write_text(build)
print(PREFIX + ' APPLY PASS')
print('variant=' + R007)
print('mode=MEASUREMENT_ONLY_HEAP_POSITIVE_WINDOWS')
print('renderer_windows=DRAIN/CAPTURE/RESOLVE/PRUNE')
print('tile_windows=PRELOAD_POINTS/PLAN/IO/SCHEDULE')
print('gen2_interval_observer=PASSIVE; forced_gc=0')
print('quality_change=0 authority_change=0 worker_change=0 10Hz_change=0 160km_change=0')
