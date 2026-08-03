#!/usr/bin/env python3
from pathlib import Path
import hashlib,re,sys
sys.dont_write_bytecode=True
from v01700_testlib import ROOT,SOURCE,CheckSuite,read,strip_csharp_comments_and_literals

suite=CheckSuite('v0.18.0.0 CP3.5 Gate 1 Back Render Profiler / Symmetric Top UI Candidate 2')
renderer=read(SOURCE/'Terrain/AERISTerrainGpuTileRenderer.cs')
window=read(SOURCE/'UI/AERISWindow.cs')
nd=read(SOURCE/'UI/AERISNavigationDisplay.cs')
version=read(SOURCE/'Properties/AERISBuildVersion.generated.cs')
build=read(ROOT/'build_ubuntu.sh')
avc=read(ROOT/'GameData/AERISFlightControl/AERISFlightControl.version')
readme=read(ROOT/'README.md')
gate2='internal const string UiCheckpoint = "DEV CP3.5 GATE 2 —' in version

for name,text in (('renderer',renderer),('window',window),('nd',nd)):
    clean=strip_csharp_comments_and_literals(text)
    suite.check(clean.count('{')==clean.count('}'),name+' braces balanced')
    suite.check(clean.count('(')==clean.count(')'),name+' parens balanced')

# Gate 1 forced-recovery safety remains; Candidate 1's low-FPS 160 km workaround is removed.
suite.check('forcedRecoveryBackRenders++' not in renderer,
            'forced recovery full-render path remains removed')
suite.check('forced_recovery_suppressed=' in renderer and
            'suppressedForcedRecoveryFrames++' in renderer,
            'suppressed forced-recovery demand remains observable')
suite.check((('const float BackRefreshAsyncTargetSeconds = 0.05f;' in renderer) or
             ('const float KeyFrameMinimumIntervalSeconds = 0.35f;' in renderer)) if gate2 else
            ('const float BackRefreshDiagnosticSeconds = 0.20f;' in renderer),
            'successor uses its explicit bounded BACK/key-frame cadence')
for token in ('BackRefresh40KmSeconds','BackRefresh80KmSeconds','BackRefresh160KmSeconds'):
    suite.check(token not in renderer,'Candidate 1 range slowdown removed: '+token)
resolve=renderer[renderer.find('static float ResolveBackRefreshCadenceSeconds'):renderer.find('static float ResolveHistorySurfaceRange')]
suite.check((('return BackRefreshAsyncTargetSeconds;' in resolve) or
             ('return KeyFrameMinimumIntervalSeconds;' in resolve)) if gate2 else
            ('return BackRefreshDiagnosticSeconds;' in resolve),
            'range-independent cadence resolver returns the current explicit schedule')
suite.check('lastBackRefreshCadenceSeconds = ResolveBackRefreshCadenceSeconds(rangeMeters)' in renderer and
            'nextBackRefreshRealtime = Time.realtimeSinceStartup +' in renderer,
            'scheduled BACK deadline remains explicit')
should=renderer[renderer.find('bool ShouldRefreshBackBuffer'):renderer.find('static float ResolveBackRefreshCadenceSeconds')]
suite.check('!frontBufferValid && lastBackAttemptViewGeneration < 0L' in should and
            'Time.realtimeSinceStartup >= nextBackRefreshRealtime' in should,
            'only initial FRONT attempt bypasses cadence')
suite.check('GUI.matrix' not in strip_csharp_comments_and_literals(renderer),
            'no executable GUI.matrix terrain warp')
suite.check('cpuTerrainDrawCount++' not in renderer and 'cpu_terrain_draw=0' in renderer,
            'CPU terrain presentation remains prohibited')

# Low-overhead BACK decomposition profiler.
suite.check('const int BackRenderDetailedProfileStride = 4;' in renderer,
            'detailed profiler samples one in four BACK renders')
suite.check('bool detailedProfile = (backProfileSequence++ % BackRenderDetailedProfileStride) == 0L;' in renderer,
            'sampling stride is enforced at BACK entry')
suite.check('[CP3.5_GATE1_BACK_PROFILE]' in renderer,
            'aggregated BACK profile telemetry marker exists')
for token in ('setup_clear_avg_ms=','projection_cpu_avg_ms=','mesh_vertex_upload_avg_ms=',
              'bounds_avg_ms=','colour_cpu_avg_ms=','colour_upload_avg_ms=',
              'draw_submit_avg_ms=','finalize_avg_ms=','other_avg_ms=',
              'projected_vertices_avg=','draw_calls_avg=','cadence_s='):
    suite.check(token in renderer,'profile telemetry includes '+token)
suite.check(renderer.count('[CP3.5_GATE1_BACK_PROFILE]')==1,
            'BACK profiler uses one aggregated log site, not per-tile logging')
suite.check(('TryStartProjectionBatch(' in renderer and
             'SetProjectionSafeBounds(mesh);' in renderer and
             ('RenderBackBuffer(tiles, projection' in renderer or
              'Never fall\n                // back to the former ~28 ms main-thread full-vertex projection path' in renderer)) if gate2 else
            ('if (!detailedProfile)' in renderer and 'mesh.vertices = projectedVertices;' in renderer and
             'mesh.RecalculateBounds();' in renderer),
            'successor preserves a bounded presentation path while Gate 2 removes normal main-thread projection')
suite.check('profile.ProjectionCpuMs +=' in renderer and
            'profile.MeshVertexUploadMs +=' in renderer and
            'profile.BoundsMs +=' in renderer,
            'projection CPU, vertex upload and bounds costs are separately timed')
suite.check('profile.ColourCpuMs +=' in renderer and
            'profile.ColourUploadMs +=' in renderer,
            'colour generation and mesh colour upload are separately timed')
suite.check('profile.DrawSubmitMs +=' in renderer and 'profile.DrawCalls++' in renderer,
            'material/DrawMeshNow submission has separate timing and draw count')
suite.check('runtime.Gpu.RecordFrameCost(totalMs)' in renderer,
            'existing GPU presentation total-cost telemetry remains fed')

# Screenshot-reference top UI with symmetric allocation and no content-driven wrapping.
suite.check('string[] labels={"FLIGHT CONTROL","PROTECT","AUTOPILOT","SYSTEM","EXTEND ADDONS"};' in window,
            'main top labels match screenshot-reference row set')
suite.check('DrawSymmetricTabRow(ref next,labels,0,3);' in window and
            'DrawSymmetricTabRow(ref next,labels,3,2);' in window,
            'main top UI is fixed 3 + 2 row topology')
suite.check('float totalGap=TopTabGap*Mathf.Max(0,actual-1);' in window and
            'float width=Mathf.Max(1f,(row.width-totalGap)/actual);' in window,
            'button widths are equal subdivisions of the full available row')
suite.check('Rect buttonRect=new Rect(row.x+i*(width+TopTabGap),row.y,width,row.height);' in window,
            'top buttons advance by equal width and equal gap from the same left edge')
suite.check('GUILayoutUtility.GetRect(1f,height,GUILayout.ExpandWidth(true),GUILayout.Height(height))' in window,
            'top rows occupy the available window width with symmetric outer margins')
suite.check('GUILayoutUtility.GetRect(1f,masterHeight,GUILayout.ExpandWidth(true),GUILayout.Height(masterHeight))' in window,
            'MASTER uses the full available symmetric row width')
suite.check('string[] labels={"STATUS","OPTIONS","AIRFIELDS","PRELOAD MAPS"};' in window and
            'DrawSymmetricTabRow(ref next,labels,3,1);' in window,
            'SYSTEM row preserves Candidate 13 removal and fills PRELOAD row symmetrically')
suite.check('DIAGNOSTICS' not in strip_csharp_comments_and_literals(window),
            'removed SYSTEM DIAGNOSTICS functionality is not reintroduced')
suite.check(re.search(r'wordWrap\s*=\s*true',window) is None and
            re.search(r'wordWrap\s*=\s*true',nd) is None,
            'automatic text wrapping remains prohibited')
suite.check('skinLabel.wordWrap=false' in window and 'skinButton.wordWrap=false' in window and
            'skinToggle.wordWrap=false' in window and 'skinBox.wordWrap=false' in window,
            'raw AERIS window skin text is forced no-wrap while drawing')
suite.check('rect.width<540f' not in window and 'rect.width<720f' not in window and
            'rect.width<560f' not in window and 'rect.width<760f' not in window,
            'topology cannot jump at historical width thresholds')
suite.check('return Mathf.CeilToInt(5f/BaseTabColumns);' in window,
            'main tab row count is invariant and text-independent')

# Candidate 8/9 performance throttles remain except the superseded top geometry details.
tiles=read(SOURCE/'Terrain/AERISTerrainTileSystem.cs')
suite.check('const float PreloadStatusUiRefreshSeconds = 0.25f' in tiles and
            'cachedPreloadStatus' in tiles,
            'preload UI snapshot caching remains at 4 Hz')
suite.check('nextTerrainTelemetrySampleRealtime = telemetryNow + 0.5f' in nd and
            'nextNdGcSampleRealtime = now + 1f' in nd,
            'ND telemetry/GC throttles remain inherited')

# Supply/authority boundaries stay frozen during presentation diagnosis.
frozen={
 'Terrain/AERISTerrainTileContracts.cs':'7790977cd845c58767a70f193db3efbfc573812706466b477846b06447440f86',
 'Terrain/AERISTerrainGpuTileRasterizer.cs':'f931ec7b381ebdf6323ae711c31d063256a961fa574995a650507c11b10cd032',
 'Landing/AERISAirfieldRegistry.cs':'c1e70635741b779f585d0dd3d7a486e0c5761588f14cee41a710ba4f69cf800e',
 'Autopilot/AERISBankDirector.cs':'bc65d86ef3c1263ae850f0b6b1426dc7d7080cb16fe1d7316ac02d6cb8a5d7d7',
}
for rel,expected in frozen.items():
    suite.check(hashlib.sha256((SOURCE/rel).read_bytes()).hexdigest()==expected,
                'frozen supply/authority boundary byte-identical: '+rel)
cal=ROOT/'GameData/AERISFlightControl/Airfields/Defaults/03_Field_Verified_Runway_Calibrations.cfg'
suite.check(cal.read_text(errors='replace').count('Calibration\n{')>=41,
            '41 physical runway field-verified baseline remains present')

identity='DEV CP3.5 GATE 1 — BACK RENDER PROFILER / SYMMETRIC TOP UI CANDIDATE 2'
suite.check(('internal const string UiCheckpoint = "DEV CP3.5 GATE 2 —' in version) if gate2 else
            ('internal const string UiCheckpoint = "'+identity+'"' in version),
            'source current UiCheckpoint is Candidate 2 or its Gate 2 successor')
suite.check('DEV CP3.5 GATE 1 BACK RENDER PROFILER SYMMETRIC TOP UI CANDIDATE 2' in build,
            'build identity is Candidate 2')
suite.check('CP3.5 Gate 1 Back Render Profiler / Symmetric Top UI Candidate 2' in avc,
            'AVC identity is Candidate 2')
suite.check('diagnostic bridge' in readme.lower() and '[CP3.5_GATE1_BACK_PROFILE]' in readme and
            'equal left/right outer margins' in readme,
            'README documents profiling bridge and symmetric top UI')
suite.check((ROOT/'ACCEPTANCE_v0.18.0.0_CP3.5_GATE1_BACK_RENDER_PROFILER_SYMMETRIC_TOP_UI_CANDIDATE2.txt').is_file(),
            'Candidate 2 acceptance document present')
suite.check((ROOT/'Docs/ND_CP3.5_GATE1_BACK_RENDER_PROFILER_SYMMETRIC_TOP_UI_CANDIDATE2_TEST_CARD_v0.18.0.0_ja.md').is_file(),
            'Candidate 2 runtime test card present')
suite.check((ROOT/'Evidence/SOURCE_DIFF_AUDIT_v0.18.0.0_CP3.5_GATE1_BACK_RENDER_PROFILER_SYMMETRIC_TOP_UI_CANDIDATE2.txt').is_file(),
            'Candidate 2 source diff audit present')
suite.check((ROOT/'Evidence/RUNTIME_DIAGNOSIS_AERIS19_GATE1_CANDIDATE1_ND_LOWFPS_2026-08-02.txt').is_file(),
            'Candidate 1 runtime diagnosis evidence preserved')
suite.check((ROOT/'build_ubuntu.sh').stat().st_mode & 0o111 != 0,
            'build_ubuntu.sh executable permission retained')
suite.finish()
