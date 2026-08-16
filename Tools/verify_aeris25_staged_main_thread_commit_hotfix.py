#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
R = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
M = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
C = (ROOT / 'GameData/AERISFlightControl/Config/AERISOperationHealth.cfg').read_text()
U = (ROOT / 'build_ubuntu.sh').read_text()
P5V = (ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py').read_text()
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


def method_body(signature):
    start = R.find(signature)
    if start < 0: return ''
    open_index = R.find('{', start)
    if open_index < 0: return ''
    depth = 0
    state = 'code'
    i = open_index
    while i < len(R):
        c = R[i]
        n = R[i + 1] if i + 1 < len(R) else ''
        if state == 'code':
            if c == '/' and n == '/': state = 'line'; i += 2; continue
            if c == '/' and n == '*': state = 'block'; i += 2; continue
            if c == '"': state = 'string'; i += 1; continue
            if c == "'": state = 'char'; i += 1; continue
            if c == '{': depth += 1
            elif c == '}':
                depth -= 1
                if depth == 0: return R[start:i + 1]
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


ck('internal const string Codename = "NOREPINEPHRINE";' in M and
   'internal const string Revision = "OH_PHASE6_002";' in M and
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
   'codename = NOREPINEPHRINE' in C,
   'NOREPINEPHRINE OH_PHASE6_002 identity is authoritative')
ck('OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV002 STAGED COMMIT' in U and
   'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV002 STAGED COMMIT' in U,
   'build/in-game identity is NOREPINEPHRINE rev002 staged commit')
ck('AERIS25_STAGED_MAIN_THREAD_COMMIT' in R and
   'enum PendingEntryCommitStage' in R and 'sealed class PendingEntryCommit' in R,
   'resumable staged Entry commit state exists')
ck(all(stage in R for stage in (
   'ClipTriangles', 'PrepareSources', 'PreparePackedTerrain', 'UploadPackedTerrain',
   'UploadContour', 'UploadCoastline', 'GeographicPacked', 'GeographicContour',
   'GeographicCoastline', 'Finalize')),
   'staged commit covers CPU preparation, three uploads, geography and final publish')
ck('MainThreadCommitSteadyBudgetMilliseconds = 0.50' in R and
   'MainThreadCommitBootstrapBudgetMilliseconds = 1.25' in R,
   'Phase6 measured budgets remain 0.50 ms steady / 1.25 ms bootstrap')
ck('internal SurfaceBuilder Land;' in R and 'internal SurfaceBuilder Water;' in R and
   'internal SurfacePoint[] ClipScratch;' in R and
   'Land = landSurfaceScratch' in R and 'Water = waterSurfaceScratch' in R and
   'ClipScratch = surfaceClipScratch' in R and
   'internal readonly SurfaceBuilder Land = new SurfaceBuilder();' not in R,
   'staged commit reuses renderer-resident SurfaceBuilder/clip scratch instead of per-result builders')

pump = method_body('        void PumpStagedCompletedCommit(AERISTerrainTileSystem system)')
advance = method_body('        bool AdvancePendingEntryCommit(AERISTerrainTileSystem system,')
finalize = method_body('        bool FinalizePendingEntryCommit(PendingEntryCommit pending,')
try_upload = method_body('        bool TryUploadRenderReadyField(AERISTerrainHeightTile tile, string cacheKey,')
cancel = method_body('        void CancelPendingEntryCommit()')

ck(pump and 'rasterizer.Drain(completed, 1)' in pump and
   'BuildEntry(' not in pump and 'pendingEntryCommit' in pump,
   'completed results enter staged state instead of whole-result BuildEntry')
ck('AdvancePendingEntryCommit(system, budgetMilliseconds' in pump and
   'publishedThisWindow < hardMaximum' in pump,
   'staged pump remains subordinate to inherited hard result ceilings')
ck('mainThreadCommitStopwatch.Elapsed.TotalMilliseconds' in pump and
   'operationHealthMainCommitBudgetHits++' in pump,
   'staged pump is still measured by wall-clock budget')
ck('ElapsedMilliseconds(mainThreadCommitStopwatch)' not in R,
   'undefined Phase6_001 elapsed helper cannot regress')

non_tick_start = R.find('            if (!authoritativeTickDue)')
non_tick_end = R.find('            operationHealthAuthoritativeTicks++;', non_tick_start)
non_tick = R[non_tick_start:non_tick_end] if non_tick_start >= 0 and non_tick_end > non_tick_start else ''
ck('PumpStagedCompletedCommit(system);' in non_tick and
   'CaptureVisible(' not in non_tick and 'RenderBackBuffer(' not in non_tick,
   'non-authoritative Repaint advances only staged commit work, not visible presentation')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'visible ND presentation authority remains fixed 10 Hz')

ck('AdvancePendingClip' in advance and 'AdvancePendingGeographic' in advance and
   'YieldPendingEntryCommit' in advance,
   'large clipping and geographic loops are resumable budget checkpoints')
ck('UploadPreparedPackedTerrainMesh' in advance and 'BuildLineMesh(' in advance,
   'terrain/contour/coast Unity Mesh mutations are separate stages')
ck('pendingEntryCommit = null;' in advance and 'FinalizePendingEntryCommit' in advance,
   'pending authority clears only through final stage completion')
ck(finalize and 'AddEntry(entry);' in finalize and
   'CaptureAndMarkRenderReady(result, system);' in finalize and
   'MarkGpuReady(result);' in finalize,
   'cache/presentation/GPU authority is published together only at Finalize')
staged_region_start = R.find('        void PumpStagedCompletedCommit(')
staged_region_end = R.find('        void CaptureAndMarkRenderReady(', staged_region_start)
staged_region = R[staged_region_start:staged_region_end] if staged_region_start >= 0 and staged_region_end > staged_region_start else ''
ck(staged_region.count('AddEntry(entry);') == 1 and
   staged_region.find('AddEntry(entry);') > staged_region.find('FinalizePendingEntryCommit'),
   'no partial Entry reaches presentation authority before Finalize')
ck(try_upload and 'BuildEntry(' not in try_upload and
   'TryBeginPendingEntryCommit(field);' in try_upload and 'return true;' in try_upload,
   'render-ready RAM reuse also enters staged commit and suppresses duplicate worker work')
ck(cancel and 'RecycleMesh(ref pending.PackedMesh);' in cancel and
   'RecycleMesh(ref pending.ContourMesh);' in cancel and
   'RecycleMesh(ref pending.CoastlineMesh);' in cancel,
   'cancel path safely recycles all partially created Unity Mesh objects')
ck('CancelPendingEntryCommit();' in method_body('        void ResetContentSnapshot()') and
   'CancelPendingEntryCommit();' in method_body('        public void Dispose()'),
   'snapshot reset and renderer disposal cancel partial commit state')
ck('EnsureGpuDynamicTerrainColourAttributes(Entry entry)' in R and
   'entry.PackedTerrainMesh.SetUVs(2, gpuDynamicTerrainSemanticScratch);' in R and
   'LandElevationMeters = pending.LandElevation' in finalize and
   'LandShade = pending.LandShade' in finalize and
   'CoastalLandCorrectionElevationMeters =' in finalize,
   'finalized staged Entry retains source arrays required by accepted GPU dynamic semantic upload')

for field in (
    'oh_main_commit_pending=', 'oh_main_commit_pending_stage=',
    'oh_main_commit_stage_yield=', 'oh_main_commit_stage_max_ms=',
    'oh_main_commit_publish=', 'oh_main_commit_backlog=',
    'oh_main_commit_backlog_peak=', 'oh_main_commit_window_max_ms=',
    'oh_main_commit_overbudget=', 'oh_main_commit_processed='):
    ck(field in R, 'runtime telemetry publishes ' + field[:-1])

ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'presentationEntryPins.Contains(entry)' in R and 'oh_presentation_packet_reuse=' in R,
   'accepted ADENOSINE persistent presentation path remains intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R and
   'oh_snapshot_stale_mesh=' in R and 'oh_gpu_vertex_reject_semantic_mesh_null=' in R,
   'rev008 lifetime guard and rev007 root-cause witness remain intact')
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and
   'oh_prune_budget_hit=' in R and 'oh_heading_plan_coalesced=' in R,
   'accepted ATROPINE rev009 prune/heading governor remains intact')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_STAGED_MAIN_THREAD_COMMIT' not in SH,
   'rev002 changes no shader equations or shader bytes')

draw = method_body('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
terrain = draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)')
contour = draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)')
coast = draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)')
ck(0 <= terrain < contour < coast,
   'hard painter order remains terrain -> contour -> coastline inside every Entry')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck('phase6_identity' in P5V and 'OH_PHASE6_002' in P5V and
   'verify_aeris25_staged_main_thread_commit_hotfix.py' in P5V,
   'ADENOSINE inherited verifier explicitly admits exact rev002 descendant')
active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDWRITEBYTECODE=1 python3'))
ck('verify_aeris25_staged_main_thread_commit_hotfix.py' in active and
   'verify_aeris25_main_thread_commit_governor.py' not in active,
   'rev002 build uses one final-tree staged-commit verifier')

frozen = ['Source/AERISFlightControl/AA', 'Source/AERISFlightControl/Autopilot',
          'Source/AERISFlightControl/Protect', 'Source/AERISFlightControl/Landing']
try:
    changed = subprocess.check_output(
        ['git', '-C', str(ROOT), 'diff', '--name-only', 'HEAD', '--'] + frozen,
        text=True).strip().splitlines()
except Exception:
    changed = ['GIT_DIFF_UNAVAILABLE']
ck(changed == [], 'AA/AP/PROTECT/LAND working-tree edits remain NONE')

failed = [name for ok, name in checks if not ok]
print('\n[AERIS25 NOREPINEPHRINE PHASE6_002 STAGED MAIN THREAD COMMIT] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    message = '; '.join(failed)
    print('FAILED: ' + message)
    print('::error title=NOREPINEPHRINE Phase6_002 verifier::' + message)
    raise SystemExit(1)
print('[AERIS25 NOREPINEPHRINE PHASE6_002 STAGED MAIN THREAD COMMIT] STATIC PASS')
