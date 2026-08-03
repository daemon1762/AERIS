#!/usr/bin/env python3
from pathlib import Path
import re,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3.5 Gate 2 Parallel Projection / Compact Autopilot UI Candidate 1')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
scheduler=read(SOURCE/'Performance/AERISWorkerScheduler.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
readme=read(ROOT/'README.md')

for name,text in (('renderer',renderer),('scheduler',scheduler),('window',window)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

# Gate 2 presentation architecture.
suite.check('const float BackRefreshAsyncTargetSeconds = 0.05f;' in renderer,
            'Gate 2 publishes 50 ms target cadence')
suite.check('pendingProjectionBatch != null' in renderer and
            'if (refreshAllowed && pendingProjectionBatch == null)' in renderer,
            'only one projection batch may be outstanding')
suite.check('TryRenderReadyProjectionBatch(visible, effectiveMode, currentPreset' in renderer and
            'TryStartProjectionBatch(visible, tiles, projection' in renderer,
            'Draw consumes completed batch then schedules successor')
suite.check('if (!asynchronous)' in renderer and
            renderer.count('RenderBackBuffer(tiles, projection')==1,
            'synchronous full projection is emergency fallback only')
suite.check('mesh.RecalculateBounds()' not in renderer and
            'SetProjectionSafeBounds(mesh);' in renderer,
            'per-refresh RecalculateBounds removed in favour of safe fixed bounds')
suite.check('[CP3.5_GATE2_PARALLEL]' in renderer,
            'Gate 2 worker telemetry marker exists')
for token in ('workers_last=','worker_cpu_ms_per_completed=','worker_wall_ms_per_completed=',
              'projected_vertices=','colour_vertices=','target_cadence_s='):
    suite.check(token in renderer,'Gate 2 telemetry includes '+token)

# Multi-core shared scheduler contract.
suite.check('runtime.Scheduler.PermitController.ActivePermits' in renderer and
            'runtime.Scheduler.WorkerCount' in renderer,
            'worker fan-out follows scheduler pool and active permit limits')
suite.check('int workerCount = Math.Max(1, Math.Min(drawEntries.Count' in renderer and
            'chunkWeights[j] < chunkWeights[lightest]' in renderer,
            'entries are vertex-weight balanced across all currently permitted workers')
suite.check('AERISRuntimeLane.GeneralCompute' in renderer and
            'runtime.Scheduler.SubmitRequired(' in renderer,
            'projection chunks use shared GeneralCompute scheduler with required commits')
suite.check('Math.Max(2, LogicalProcessors - ReservedProcessors)' in scheduler and
            'workerTotal = configuredWorkers > 0' in scheduler,
            'shared scheduler scales worker pool from logical CPU count with reserved cores')
suite.check('job.Key.StartsWith("terrain-projection-"' in scheduler,
            'terrain worker telemetry includes projection jobs')

worker_start=renderer.find('static ProjectionChunkResult ProjectProjectionChunk')
worker_end=renderer.find('void CommitProjectionChunk',worker_start)
worker=renderer[worker_start:worker_end]
for forbidden in ('Graphics.','RenderTexture','Material.','GL.','Mesh ','mesh.',
                  'FlightGlobals','Vessel ','UnityEngine.Object','GameObject','Transform'):
    suite.check(forbidden not in worker,'worker pure-data boundary excludes '+forbidden)
suite.check('ProjectPointsWorker(' in worker and 'entry.LandColours' in worker,
            'worker owns geographic projection and colour preparation')
suite.check('UploadPreparedProjection(' in renderer and 'mesh.vertices = vertices;' in renderer and
            'Graphics.DrawMeshNow' in renderer,
            'Unity Mesh upload and draw remain on prepared main-thread commit path')
suite.check('batch.SubmissionFailed = true;' in renderer[renderer.find('void CancelProjectionBatch'):renderer.find('bool RenderBackBuffer',renderer.find('void CancelProjectionBatch'))] and
            'CancelKey(' not in renderer[renderer.find('void CancelProjectionBatch'):renderer.find('bool RenderBackBuffer',renderer.find('void CancelProjectionBatch'))],
            'reset lets one bounded worker batch retire instead of racing cancelled scratch writers')

# UI contract accepted during Gate 2.
suite.check('const float MinWindowWidth=480f;' in window and
            'const float CompactButtonHeight=20f;' in window and
            'const float MasterButtonHeight=40f;' in window,
            'compact readable window/button baseline published')
suite.check('float CompactControlHeight()' in window and
            'GUI.skin.button.CalcSize(new GUIContent("Ag"))' in window and
            'float ReadableButtonWidth(string label,float baseline)' in window,
            'button minimum geometry is text-readable rather than blindly compressed')
suite.check(re.search(r'wordWrap\s*=\s*true',window) is None and
            'skinButton.wordWrap=false' in window and 'skinLabel.wordWrap=false' in window,
            'automatic text wrapping remains prohibited')
suite.check('GUILayout.ExpandWidth(true)' in window and
            'CompactFullRowButton(' in window and 'CompactFullRowToggle(' in window,
            'long one-line controls use full-row small-MASTER layout')
suite.check('float rowHeight=ResponsiveHeight(38f);' in window and '\\n"+designation' in window,
            'explicit two-line runway rows retain their taller layout')
suite.check('string[] labels={"FLIGHT CONTROL","PROTECT","AUTOPILOT","SYSTEM","EXTEND ADDONS"};' in window and
            'DrawSymmetricTabRow(ref next,labels,0,3);' in window and
            'DrawSymmetricTabRow(ref next,labels,3,2);' in window,
            'screenshot-reference top layout remains 3+2 with symmetric rows')
suite.check('string[] labels={"TAKEOFF","FLIGHT","NAV","LAND"};' in window,
            'AUTOPILOT child categories are TAKEOFF / FLIGHT / NAV / LAND')
suite.check('core.AnyNormalApArmed' in window and
            'core.AutoTakeoff!=null&&(core.AutoTakeoff.Armed||core.AutoTakeoff.Executing)' in window and
            'core.Landing!=null&&core.Landing.Armed' in window,
            'category colours derive from real controller state')
suite.check('GUI.backgroundColor=active[i]?' in window and
            'new Color(0.12f,0.75f,0.16f,1f)' in window and
            'new Color(0.78f,0.12f,0.12f,1f)' in window,
            'category buttons use green-any-active / red-none-active status colours')
suite.check('if(autopilotPage==0){DrawAutoTakeoffPage' in window and
            'if(autopilotPage==2){DrawFlightPlanLibrary' in window and
            'if(autopilotPage==3){DrawLandingFoundation' in window and
            'LATERAL — FLIGHT' in window and 'VERTICAL — FLIGHT' in window and 'SPEED — FLIGHT' in window,
            'AUTOPILOT pages are functionally separated and FLIGHT owns normal AP controls')

identity='DEV CP3.5 GATE 2 — PARALLEL PROJECTION / COMPACT AUTOPILOT UI CANDIDATE 1'
suite.check('internal const string UiCheckpoint = "'+identity+'"' in version,
            'source identity is Gate 2 Candidate 1')
suite.check('DEV CP3.5 GATE 2 PARALLEL PROJECTION COMPACT AUTOPILOT UI CANDIDATE 1' in build,
            'Ubuntu build generator publishes Gate 2 Candidate 1')
suite.check('Gate 2 Parallel Projection / Compact Autopilot UI Candidate 1' in avc,
            'AVC metadata publishes Gate 2 Candidate 1')
suite.check('[CP3.5_GATE2_PARALLEL]' in readme and 'TAKEOFF | FLIGHT | NAV | LAND' in readme,
            'README documents Gate 2 performance and UI contracts')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh executable permission retained')
suite.finish()
