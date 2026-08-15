#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

# Final build executes these inherited gates before rev009. Re-run them on the
# final generated tree so successor allowlist drift cannot hide until install.
for name in [
    'verify_aeris25_gpu_dynamic_terrain_colour_ready.py',
    'verify_aeris25_chunk_cull_guard_hotfix.py',
    'verify_aeris25_temporal_foundation_overscan_hotfix.py',
]:
    script = ROOT / 'Tools' / name
    if not script.is_file():
        raise SystemExit('[AERIS25 ATROPINE REV009] inherited final-tree verifier missing: ' + name)
    print('[AERIS25 ATROPINE REV009 INHERITED] $ ' + name)
    subprocess.run([sys.executable, str(script)], cwd=str(ROOT), check=True)
print('[AERIS25 ATROPINE REV009 INHERITED] FINAL-TREE VERIFIER MATRIX PASS')

R = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
T = (ROOT / 'Source/AERISFlightControl/Terrain/AERISTerrainTileSystem.cs').read_text()
M = (ROOT / 'Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs').read_text()
U = (ROOT / 'build_ubuntu.sh').read_text()
SH = (ROOT / 'GpuAssets/Assets/AERISNdExactVertexProjection.shader').read_text()
checks = []


def ck(value, name):
    ok = bool(value)
    checks.append((ok, name))
    print(('[PASS] ' if ok else '[FAIL] ') + name)


ck('internal const string Revision = "OH_PHASE4_009";' in M,
   'ATROPINE revision is OH_PHASE4_009')
ck('AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in R and
   'AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' in T,
   'renderer and TileSystem carry burst-governor markers')
ck('const int SteadyContentCommitMaximumResults = 2;' in R and
   'const int BootstrapContentCommitMaximumResults = 4;' in R and
   'const int NormalPruneMaximumRemovals = 4;' in R and
   'const float ContentPlanningHeadingStepDeg = 6f;' in R,
   'bounded commit/prune/heading constants are exact')
ck('int burstMaximum = frontBufferValid && requestedViewReady ?' in R and
   'SteadyContentCommitMaximumResults : BootstrapContentCommitMaximumResults;' in R and
   'Math.Min(profileMaximum, burstMaximum)' in R,
   'completed raster -> Mesh commits are capped at 2 steady / 4 bootstrap')
ck('operationHealthContentCommitBudgetHits++' in R and
   'operationHealthContentCommitBacklogPeak' in R and
   'oh_content_commit_budget_hit=' in R and
   'oh_content_commit_backlog_peak=' in R,
   'commit-governor pressure/backlog telemetry is published')
ck('removed < NormalPruneMaximumRemovals' in R and
   'removed++;' in R and
   'operationHealthPruneBudgetHits++' in R and
   'operationHealthPruneDebtPeakBytes' in R,
   'normal Entry retirement is capped to four per content tick with debt telemetry')
ck('oh_prune_budget_hit=' in R and 'oh_prune_debt_peak_bytes=' in R,
   'prune-governor telemetry is published')

needs_start = R.find('        bool NeedsContentRefresh(')
needs_end = R.find('        void ResetContentSnapshot()', needs_start)
needs = R[needs_start:needs_end] if needs_start >= 0 and needs_end > needs_start else ''
ck('ContentPlanningHeadingStepDeg' in needs and
   'headingDelta >= 3f' in needs and
   'operationHealthContentHeadingCoalesced++' in needs,
   'renderer coalesces only hidden heading content refresh from 3 to cumulative 6 degrees')
ck('bool adoptContentPlanningHeading =' in R and
   'if (adoptContentPlanningHeading) contentHeadingDeg = mapHeadingDeg;' in R,
   'terrain-generation content captures do not reset cumulative planning heading')
ck('oh_heading_plan_coalesced=' in R,
   'renderer publishes hidden heading coalescing telemetry')

ck('double planningHeadingDelta =' in T and
   'planningHeadingDelta >= 6.0' in T and
   'bool acceptPlanningHeading =' in T and
   'if (acceptPlanningHeading) displayViewHeadingDeg = normalizedHeading;' in T,
   'TileSystem uses cumulative 6-degree latest-wins hidden planning heading')
ck('bool structuralViewChanged = rangeChanged || centerChanged ||' in T and
   'bool materiallyChanged = structuralViewChanged || headingChanged;' in T,
   'range/center/orientation remain immediate hidden planning changes')

ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,
   'fixed 10 Hz ND presentation authority remains unchanged')
ck('AERISNdMapProjection.Create(' in R and
   'mapHeadingDeg, trackUp, anchorV, orientation);' in R,
   'visible map projection still receives current heading directly')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,
   'Golden ARGB32/Bilinear render target remains unchanged')
ck('runwayMapLockErrorPx=' in R and 'visualCoverage=' in R,
   'Runway Map Lock and Golden coverage telemetry remain present')

ck('AERIS25_SNAPSHOT_MESH_LIFETIME_GUARD' in R and
   'bool IsEntryProtectedByContentSnapshot(Entry entry)' in R and
   'oh_snapshot_stale_mesh=' in R,
   'rev008 snapshot Mesh lifetime guard remains intact')
ck('AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS' in R and
   'oh_gpu_vertex_reject_semantic_mesh_null=' in R,
   'rev007 semantic-mesh-null attribution remains available')
ck('operationHealthFoundationCullBypass++' not in R and
   'AERIS25_CHUNK_CULL_GUARD' in R and
   'AERIS25_TEMPORAL_FOUNDATION_OVERSCAN' in R,
   'rev005 bypass remains rolled back; rev003/rev004 presentation path remains')
ck('AERIS25_DYNAMIC_COLOUR_MODE_SPLIT' in SH and
   'AERIS25_CONTENT_GENERATION_BURST_GOVERNOR' not in SH,
   'rev009 changes no shader equations or shader bytes')

draw_start = R.find('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
draw_end = R.find('        static void EnsurePackedTerrainColours(', draw_start)
draw = R[draw_start:draw_end] if draw_start >= 0 and draw_end > draw_start else ''
terrain = draw.find('Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix);')
contour = draw.find('Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);')
coast = draw.find('Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);')
ck(0 <= terrain < contour < coast,
   'per-Entry packed terrain -> contour -> coastline painter order remains intact')

active = '\n'.join(line for line in U.splitlines()
                   if line.strip().startswith('PYTHONDONTWRITEBYTECODE=1 python3'))
ck('REV009 CONTENT GENERATION BURST GOVERNOR' in U and
   'verify_aeris25_content_generation_burst_governor_hotfix.py' in active and
   'verify_aeris25_snapshot_mesh_lifetime_guard_hotfix.py' not in active,
   'build identity and active successor verifier are rev009-specific')

core = (ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour.py').read_text()
ready = (ROOT / 'Tools/verify_aeris25_gpu_dynamic_terrain_colour_ready.py').read_text()
cull = (ROOT / 'Tools/verify_aeris25_chunk_cull_guard_hotfix.py').read_text()
over = (ROOT / 'Tools/verify_aeris25_temporal_foundation_overscan_hotfix.py').read_text()
ck('OH_PHASE4_009' in core and 'OH_PHASE4_009' in ready and
   'OH_PHASE4_009' in cull and 'OH_PHASE4_009' in over,
   'all inherited final build verifiers explicitly admit rev009')
ck('REV009 CONTENT GENERATION BURST GOVERNOR' in cull and
   'REV009 CONTENT GENERATION BURST GOVERNOR' in over,
   'inherited presentation verifiers admit rev009 build identity')

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
print('\n[AERIS25 ATROPINE REV009 CONTENT GENERATION BURST GOVERNOR] %d/%d PASS' %
      (len(checks) - len(failed), len(checks)))
if failed:
    print('FAILED: ' + '; '.join(failed))
    raise SystemExit(1)
print('[AERIS25 ATROPINE REV009 CONTENT GENERATION BURST GOVERNOR] STATIC PASS')
