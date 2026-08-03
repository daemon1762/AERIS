#!/usr/bin/env python3
from pathlib import Path
import re,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3.5 Gate 2 Exact Keyframe / Overscan Temporal Reprojection / Multicore GPU Candidate 2')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
scheduler=read(SOURCE/'Performance/AERISWorkerScheduler.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
readme=read(ROOT/'README.md')
roadmap_path=ROOT/'Docs/ROADMAP_CP3.5_ND_PRESENTATION_PERFORMANCE_v0.18.0.0_ja.md'
roadmap=read(roadmap_path) if roadmap_path.exists() else ''

for name,text in (('renderer',renderer),('scheduler',scheduler),('window',window)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')
    suite.check(clean.count('[')==clean.count(']'),name+' brackets balanced')

# Exact key-frame + overscan architecture.
suite.check('const float HistoryOverscanScale = 1.25f;' in renderer,
            'authoritative key frame uses bounded 1.25x overscan surface')
suite.check('const float MaximumHistorySurfaceRangeMeters = 250000f;' in renderer,
            'overscan surface has an explicit upper range bound')
suite.check('ResolveHistorySurfaceRange(rangeMeters)' in renderer and
            'CaptureVisible(centerLatitudeDeg,\n                centerLongitudeDeg, historySurfaceRangeMeters' in renderer,
            'tile capture covers the same bounded overscan surface that is rendered')
suite.check('AERISNdMapProjection historySurfaceProjection = AERISNdMapProjection.Create(' in renderer and
            'historySurfaceRangeMeters, mapHeadingDeg, trackUp' in renderer,
            'authoritative key-frame projection is created at overscan range')
suite.check('AERISNdMapProjection projection = AERISNdMapProjection.Create(' in renderer and
            'centerLongitudeDeg, rangeMeters,' in renderer,
            'visible CURRENT projection remains the requested ND range')
suite.check('pendingProjectionBatch != null' in renderer and
            'if (refreshAllowed && pendingProjectionBatch == null)' in renderer,
            'only one exact key-frame worker batch may be outstanding')
suite.check('TryRenderReadyProjectionBatch(visible, effectiveMode, currentPreset' in renderer and
            'TryStartProjectionBatch(visible, tiles,\n                    historySurfaceProjection' in renderer,
            'Draw consumes completed exact key frame then schedules an overscan successor')

draw_start=renderer.find('internal AERISTerrainGpuDrawState Draw(')
draw_end=renderer.find('bool TryStartProjectionBatch(',draw_start)
draw=renderer[draw_start:draw_end]
suite.check('RenderBackBuffer(' not in draw,
            'Draw never falls back to main-thread full-vertex projection')
suite.check('if (!asynchronous)' in draw and 'projectionBatchesSubmissionFailed++;' in draw and
            'skippedBackRenderFrames++;' in draw,
            'scheduler admission failure is fail-closed and bounded')

# Multicore pure-data worker contract.
suite.check('runtime.Scheduler.PermitController.ActivePermits' in renderer and
            'runtime.Scheduler.WorkerCount' in renderer,
            'key-frame worker fan-out follows shared scheduler and active permit limits')
suite.check('chunkWeights[j] < chunkWeights[lightest]' in renderer and
            'EntryProjectionVertexCount(entry)' in renderer,
            'projection work is balanced by actual vertex weight')
suite.check('AERISRuntimeLane.GeneralCompute' in renderer and
            'runtime.Scheduler.SubmitRequired(' in renderer,
            'exact projection chunks use shared GeneralCompute workers')
suite.check('Math.Max(2, LogicalProcessors - ReservedProcessors)' in scheduler and
            'workerTotal = configuredWorkers > 0' in scheduler,
            'shared scheduler scales from logical CPU count while reserving cores')
suite.check('job.Key.StartsWith("terrain-projection-"' in scheduler,
            'scheduler telemetry recognizes terrain projection worker jobs')
worker_start=renderer.find('static ProjectionChunkResult ProjectProjectionChunk')
worker_end=renderer.find('void CommitProjectionChunk',worker_start)
worker=renderer[worker_start:worker_end]
for forbidden in ('Graphics.','RenderTexture','Material.','GL.','Mesh ','mesh.',
                  'FlightGlobals','Vessel ','UnityEngine.Object','GameObject','Transform'):
    suite.check(forbidden not in worker,'worker pure-data boundary excludes '+forbidden)
suite.check('ProjectPointsWorker(' in worker and 'entry.LandColours' in worker,
            'workers perform exact geographic projection and colour preparation')
suite.check('mesh.vertices = vertices;' in renderer and 'SetProjectionSafeBounds(mesh);' in renderer,
            'Unity Mesh mutation remains in main-thread commit with fixed safe bounds')
suite.check('mesh.RecalculateBounds()' not in renderer,
            'per-keyframe RecalculateBounds remains eliminated')

# Triple GPU surfaces + temporal reprojection.
for token in ('RenderTexture backTarget','RenderTexture frontTarget',
              'RenderTexture presentationTarget','Material reprojectionMaterial'):
    suite.check(token in renderer,'GPU presentation resource exists: '+token)
suite.check('internal long UsedBytes' in renderer and
            'Math.Max(0L, presentationTargetBytes)' in renderer,
            'third presentation surface participates in VRAM accounting')
suite.check('AERIS_ND_TERRAIN_BACK' in renderer and 'AERIS_ND_TERRAIN_FRONT' in renderer and
            'AERIS_ND_TERRAIN_PRESENTATION' in renderer,
            'BACK / FRONT / PRESENTATION RenderTextures are explicitly named')
suite.check('FilterMode.Bilinear' in renderer and 'TextureWrapMode.Clamp' in renderer,
            'temporal source and destination surfaces use bilinear clamped sampling')
suite.check('Shader.Find("Unlit/Texture")' in renderer and
            'AERIS_ND_TEMPORAL_REPROJECTION_MATERIAL' in renderer,
            'reprojection uses a dedicated built-in texture material')
suite.check('DestroyRenderTargets();' in renderer and
            'DestroyUnityObject(reprojectionMaterial);' in renderer,
            'temporal GPU resources participate in explicit teardown')

# Geographic reprojection is exact at grid points, then GPU interpolated.
suite.check('const int TemporalGridCells = 8;' in renderer and
            'const float TemporalMaximumErrorPixels = 0.75f;' in renderer,
            'temporal grid and strict sub-pixel acceptance limit are explicit')
suite.check('const float TemporalMinimumUvMargin = 0.0025f;' in renderer,
            'temporal sampling has an explicit source-surface edge margin')
suite.check('currentProjection.UnprojectGuiToLatitudeLongitude(guiU, guiV' in renderer and
            'sourceProjection.ProjectLatitudeLongitudeToGui(latitudeDeg' in renderer,
            'reprojection maps CURRENT pixels through geographic coordinates into exact key frame')
suite.check('Vector2 interpolated = (temporalSourceUv[i00] +' in renderer and
            'double error = Math.Sqrt(dx * dx + dy * dy);' in renderer,
            'cell midpoint interpolation error is measured in actual render-target pixels')
suite.check('maxErrorPixels <= TemporalMaximumErrorPixels' in renderer and
            'minimumUvMargin >= TemporalMinimumUvMargin' in renderer,
            'temporal presentation is accepted only inside error and overscan bounds')
suite.check('frontTrackUp == currentProjection.TrackUp ?' in renderer and ': 180f;' in renderer,
            'TRACK-UP topology change forces a new key frame instead of hiding mode mismatch')
suite.check('reprojectionMaterial.mainTexture = frontTarget;' in renderer and
            'RenderTexture.active = presentationTarget;' in renderer and
            'GL.Begin(GL.QUADS);' in renderer,
            'GPU resamples the exact FRONT into the PRESENTATION surface')
suite.check('PresentTextureDirect(plot, presentationTarget, currentProjection.Orientation);' in renderer,
            'current temporal presentation is emitted from the third GPU surface')
suite.check('GUI.matrix' not in strip_csharp_comments_and_literals(renderer),
            'forbidden GUI.matrix temporal terrain warp is absent')
suite.check('GUI.DrawTextureWithTexCoords(plot, frontTarget, uv, true);' in renderer,
            'conservative exact FRONT latch contract is retained')

# Adaptive key-frame refresh policy.
for token in ('KeyFrameMinimumIntervalSeconds = 0.35f','KeyFrameMaximumAgeSeconds = 1.25f',
              'KeyFrameRefreshHeadingDeg = 3.0f','KeyFrameRefreshDriftPixels = 36f',
              'KeyFrameRefreshErrorPixels = 0.30f'):
    suite.check(token in renderer,'adaptive key-frame policy includes '+token)
suite.check('if (age >= KeyFrameMaximumAgeSeconds) return true;' in renderer and
            'if (!temporalAvailable) return true;' in renderer and
            'if (temporalErrorPixels >= KeyFrameRefreshErrorPixels) return true;' in renderer and
            'if (temporalDriftPixels >= KeyFrameRefreshDriftPixels) return true;' in renderer and
            'if (temporalHeadingDeltaDeg >= KeyFrameRefreshHeadingDeg) return true;' in renderer,
            'age/error/drift/heading all trigger authoritative key-frame refresh')
suite.check('return KeyFrameMinimumIntervalSeconds;' in renderer,
            'key-frame generation has a bounded minimum interval while display remains per-Repaint')
suite.check('lastBackRefreshCadenceSeconds = ResolveBackRefreshCadenceSeconds(rangeMeters);' in renderer,
            'worker admission cadence is recorded explicitly')

# Telemetry must separate grid CPU work from GPU submission timing.
suite.check('[CP3.5_GATE2_PARALLEL]' in renderer and '[CP3.5_GATE2_TEMPORAL]' in renderer,
            'parallel key-frame and temporal reprojection telemetry markers both exist')
for token in ('workers_last=','worker_cpu_ms_per_completed=','worker_wall_ms_per_completed=',
              'projected_vertices=','colour_vertices=','keyframe_min_interval_s='):
    suite.check(token in renderer,'parallel telemetry includes '+token)
for token in ('overscan_scale=','grid=','max_error_px=','min_uv_margin=','drift_px=',
              'heading_delta_deg=','grid_cpu_ms_per_frame=','submit_ms_per_frame=','confidence='):
    suite.check(token in renderer,'temporal telemetry includes '+token)
suite.check('temporalGridMilliseconds += ElapsedMilliseconds(startTicks);' in renderer and
            'temporalSubmitMilliseconds += ElapsedMilliseconds(submitTicks);' in renderer,
            'temporal CPU grid timing and GPU submission timing are measured independently')
suite.check('temporalCpuMilliseconds' not in renderer,
            'old double-counted temporal CPU metric is removed')

# Gate 4A safety / authority boundaries.
suite.check('cpu_terrain_draw=0.' in renderer,
            'GPU-only terrain presentation telemetry remains explicit')
suite.check('forcedRecoveryBackRenders' in renderer and 'suppressedForcedRecoveryFrames' in renderer,
            'forced-recovery lineage telemetry remains available for regression diagnosis')
suite.check('FlightCtrlState' not in renderer and 'MainThrottle' not in renderer and
            'OnFlyByWire' not in renderer,
            'ND presentation renderer owns no aircraft control authority')

# UI contract carried forward exactly as requested by the user.
suite.check('const float MinWindowWidth=480f;' in window and
            'const float CompactButtonHeight=20f;' in window and
            'const float MasterButtonHeight=40f;' in window,
            'window has readable minimum width and compact button geometry')
suite.check('GUI.skin.button.CalcSize(new GUIContent("Ag"))' in window and
            'ReadableButtonWidth(string label,float baseline)' in window,
            'minimum control geometry derives from readable text metrics')
suite.check(re.search(r'wordWrap\s*=\s*true',window) is None and
            'skinButton.wordWrap=false' in window and 'skinLabel.wordWrap=false' in window,
            'automatic text wrapping remains prohibited')
suite.check('string[] labels={"FLIGHT CONTROL","PROTECT","AUTOPILOT","SYSTEM","EXTEND ADDONS"};' in window and
            'DrawSymmetricTabRow(ref next,labels,0,3);' in window and
            'DrawSymmetricTabRow(ref next,labels,3,2);' in window,
            'top menu remains screenshot-reference 3+2 with symmetric margins')
suite.check('CompactFullRowButton(' in window and 'CompactFullRowToggle(' in window,
            'long one-line controls use compact mini-MASTER rows')
suite.check('float rowHeight=ResponsiveHeight(38f);' in window and '\\n"+designation' in window,
            'intentional two-line runway rows retain their taller explicit layout')
suite.check('string[] labels={"TAKEOFF","FLIGHT","NAV","LAND"};' in window,
            'AUTOPILOT child categories remain TAKEOFF / FLIGHT / NAV / LAND')
suite.check('core.AnyNormalApArmed' in window and
            'core.AutoTakeoff!=null&&(core.AutoTakeoff.Armed||core.AutoTakeoff.Executing)' in window and
            'core.Landing!=null&&core.Landing.Armed' in window,
            'category status colours derive from actual controller state')
suite.check('GUI.backgroundColor=active[i]?' in window and
            'new Color(0.12f,0.75f,0.16f,1f)' in window and
            'new Color(0.78f,0.12f,0.12f,1f)' in window,
            'AUTOPILOT child categories use green-any-active / red-none-active colours')

# Identity, docs and roadmap.
identity='DEV CP3.5 GATE 2 — EXACT KEYFRAME / OVERSCAN TEMPORAL REPROJECTION / MULTICORE GPU CANDIDATE 2'
hotfix_identity=identity+' — COMPILE HOTFIX 1'
suite.check(('internal const string UiCheckpoint = "'+identity+'"' in version) or
            ('internal const string UiCheckpoint = "'+hotfix_identity+'"' in version),
            'source identity is Gate 2 Candidate 2 or its Compile Hotfix 1 successor')
suite.check('DEV CP3.5 GATE 2 EXACT KEYFRAME OVERSCAN TEMPORAL REPROJECTION MULTICORE GPU CANDIDATE 2' in build,
            'Ubuntu build generator preserves Gate 2 Candidate 2 lineage')
suite.check('Gate 2 Exact Keyframe / Overscan Temporal Reprojection / Multicore GPU Candidate 2' in avc,
            'AVC metadata preserves Gate 2 Candidate 2 lineage')
suite.check('Exact Key Frame' in readme and 'Overscan' in readme and
            'TAKEOFF | FLIGHT | NAV | LAND' in readme,
            'README documents performance architecture and accepted UI contract')
suite.check(roadmap_path.exists(),'revised CP3.5 performance roadmap is included')
for token in ('Gate 0','Gate 1','Gate 2','Gate 3','Gate 4','Gate 5','Gate 6',
              'Exact Key Frame','Overscan','Temporal Reprojection','Unified ND World Surface'):
    suite.check(token in roadmap,'CP3.5 roadmap includes '+token)
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh executable permission retained')

suite.finish()
