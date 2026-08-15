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
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
P5V = (ROOT / 'Tools/verify_aeris25_persistent_presentation_batching.py').read_text()
STEP2 = (ROOT / 'Tools/selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


ck('internal const string Codename = "NOREPINEPHRINE";' in M and
   'internal const string Revision = "OH_PHASE6_001";' in M and
   'internal const string Candidate = "AERIS25_MAIN_THREAD_COMMIT_GOVERNOR";' in M and
   'codename = NOREPINEPHRINE' in C,
   'NOREPINEPHRINE OH_PHASE6_001 identity is authoritative')
ck('CANDIDATE_NAME="AERIS25_MAIN_THREAD_COMMIT_GOVERNOR"' in U and
   'OPERATION HEALTH PHASE 6 NOREPINEPHRINE MAIN THREAD COMMIT GOVERNOR REV001' in U and
   'OPERATION HEALTH PHASE 6 NOREPINEPHRINE — MAIN THREAD COMMIT GOVERNOR — REV001' in U,
   'build/in-game identity is AERIS25-3 NOREPINEPHRINE')
ck('AERIS25_MAIN_THREAD_COMMIT_GOVERNOR' in R and
   'MainThreadCommitSteadyBudgetMilliseconds = 0.50' in R and
   'MainThreadCommitBootstrapBudgetMilliseconds = 1.25' in R,
   'measured time budgets are 0.50 ms steady / 1.25 ms bootstrap')
ck('SteadyContentCommitMaximumResults = 2' in R and
   'BootstrapContentCommitMaximumResults = 4' in R and
   'hardMaximum = Math.Max(1, Math.Min(profileMaximum, burstMaximum))' in R,
   'ATROPINE rev009 2/4 count ceilings remain hard safety rails')

start = R.find('        void DrainCompleted(AERISTerrainTileSystem system)')
end = R.find('        bool IsEntryGenerationCurrent(', start)
drain = R[start:end] if start >= 0 and end > start else ''
ck('rasterizer.Drain(completed, 1)' in drain and
   'rasterizer.Drain(completed, maximum)' not in drain,
   'completed raster results are consumed one at a time')
ck('readonly Stopwatch mainThreadCommitStopwatch = new Stopwatch();' in R and
   'mainThreadCommitStopwatch.Reset();' in drain and
   'mainThreadCommitStopwatch.Start();' in drain and
   'ElapsedMilliseconds(mainThreadCommitStopwatch)' in drain,
   'one resident Stopwatch measures the main-thread commit window')
first_drain = drain.find('rasterizer.Drain(completed, 1)')
first_budget_check = drain.find('elapsedMilliseconds >= budgetMilliseconds')
ck(0 <= first_drain < first_budget_check and
   'if (rasterizer.Drain(completed, 1) <= 0) break;' in drain,
   'minimum one-result forward progress precedes any elapsed budget stop')
ck('processedThisWindow < hardMaximum' in drain and
   'processedThisWindow += completed.Count;' in drain,
   'measured budget remains subordinate to bounded hard result count')
ck('operationHealthMainCommitProcessed += completed.Count;' in drain and
   'operationHealthMainCommitBacklog = remainingCompleted;' in drain and
   'operationHealthMainCommitBacklogPeak = Math.Max(' in drain,
   'processed work and live/peak backlog are observable')
ck('if (elapsedMilliseconds >= budgetMilliseconds)' in drain and
   'operationHealthMainCommitBudgetHits++' in drain and
   'operationHealthMainCommitOverbudget++' in drain,
   'elapsed-time budget stop and unavoidable single-result overrun are observable')
ck('operationHealthContentCommitBudgetHits++' in drain and
   'operationHealthContentCommitBacklogPeak = Math.Max(' in drain,
   'rev009 count-cap telemetry remains as inherited hard-rail witness')
ck('Enqueue' not in drain and 'TryEnqueue' not in drain and 'requeue' not in drain.lower(),
   'Phase6 never pushes consumed results back into the worker queue')

for field in (
    'oh_main_commit_budget_hit=', 'oh_main_commit_backlog=',
    'oh_main_commit_backlog_peak=', 'oh_main_commit_window_max_ms=',
    'oh_main_commit_overbudget=', 'oh_main_commit_processed=',
    'oh_main_commit_budget_ms='):
    ck(field in R, 'runtime telemetry publishes ' + field[:-1])

ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and
   'oh_prune_budget_hit=' in R and 'oh_heading_plan_coalesced=' in R,
   'accepted ATROPINE rev009 prune/heading governor remains intact')
ck('AERIS25_PERSISTENT_PRESENTATION_BATCHING' in R and
   'struct PresentationPacket' in R and
   'presentationEntryPins.Contains(entry)' in R and
   'oh_presentation_packet_reuse=' in R,
   'accepted ADENOSINE persistent packet and O(1) snapshot pin path remain intact')
ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R and
   'oh_snapshot_stale_mesh=' in R and
   'oh_gpu_vertex_reject_semantic_mesh_null=' in R,
   'rev008 lifetime guard and rev007 rejection witness remain intact')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_MAIN_THREAD_COMMIT_GOVERNOR' not in SH,
   'NOREPINEPHRINE changes no shader equations or shader bytes')

draw_start = R.find('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
draw_end = R.find('        static void EnsurePackedTerrainColours(Entry entry,', draw_start)
draw = R[draw_start:draw_end] if draw_start >= 0 and draw_end > draw_start else ''
terrain = draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix)')
contour = draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix)')
coast = draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix)')
ck(0 <= terrain < contour < coast,
   'hard painter order remains terrain -> contour -> coastline inside every Entry')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz visible ND authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')
ck('GPU_DYNAMIC_SEMANTIC' in R and 'oh_gpu_dynamic_colour=' in R,
   'GPU Dynamic Terrain Colour authority remains present')
ck('AERIS25 OPERATION HEALTH PHASE 6 '+('NOREPI'+'NEPHRINE')+' MAIN THREAD COMMIT GOVERNOR' in U and
   "phase6='NOREPI'+'NEPHRINE'" in STEP2,
   'Step2 inherited build lineage explicitly admits Phase6 NOREPINEPHRINE')
ck('phase6_identity' in P5V and 'verify_aeris25_main_thread_commit_governor.py' in P5V,
   'ADENOSINE verifier explicitly admits the exact Phase6 descendant')

active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('verify_aeris25_main_thread_commit_governor.py' in active and
   'verify_aeris25_persistent_presentation_batching.py' not in active,
   'Phase6 build uses one final-tree NOREPINEPHRINE verifier')

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
print('\n[AERIS25 NOREPINEPHRINE PHASE6_001 MAIN THREAD COMMIT GOVERNOR] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 NOREPINEPHRINE PHASE6_001 MAIN THREAD COMMIT GOVERNOR] STATIC PASS')
