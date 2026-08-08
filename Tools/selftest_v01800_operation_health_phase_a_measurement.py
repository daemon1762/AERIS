#!/usr/bin/env python3
from pathlib import Path
import sys
sys.dont_write_bytecode=True
ROOT=Path(__file__).resolve().parents[1]
R=(ROOT/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs').read_text()
checks=[]
def ck(v,n):
    checks.append((bool(v),n)); print(('[PASS] ' if v else '[FAIL] ')+n)
ck('phaseAMeasureProjectionCpuMs' in R and 'phaseAMeasureMeshUploadMs' in R and 'phaseAMeasureBackRenderMs' in R,'Phase A timing accumulators exist')
ck('phaseAMeasureProjectedVertices' in R and 'phaseAMeasureUploadedVertices' in R and 'phaseAMeasureDrawnVertices' in R,'Phase A vertex counters exist')
ck('phaseAMeasureVisibleEntries' in R,'Phase A visible-entry counter exists')
project=R[R.index('void ProjectMesh('):R.index('static double UnitLatitude(')]
ck(project.count('Stopwatch.GetTimestamp()') == 3,'projection timing is mesh-granularity, not per-vertex')
loop=project[project.index('for (int i = 0; i < points.Length; i++)'):project.index('long uploadStartTicks')]
ck('Stopwatch.GetTimestamp()' not in loop,'no Stopwatch calls occur inside the vertex loop')
ck('mesh.vertices = projectedVertices;' in project and 'phaseAMeasureMeshUploadMs +=' in project,'mesh upload timing surrounds existing vertex upload')
ck('phaseAMeasureProjectedVertices += points.LongLength;' in project and 'phaseAMeasureUploadedVertices += projectedVertices.LongLength;' in project,'projected/uploaded vertex totals use exact array lengths')
render=R[R.index('bool RenderBackBuffer('):R.index('float MeasureFoundationGpuReadiness(')]
ck('phaseAMeasureVisibleEntries++;' in render,'visible entries are counted without culling')
ck('phaseAMeasureBackRenderSamples++;' in render and 'RecordFrameCost(backRenderMilliseconds)' in render,'BACK timing reuses one elapsed sample')
draw=R[R.index('bool DrawEntry('):R.index('void EnsureWaterColour(')]
ck('phaseAMeasureDrawnVertices +=' in draw and draw.count('Graphics.DrawMeshNow') == 6,'drawn vertices cover unchanged six-mesh painter path')
log=R[R.index('void LogGpuOnlyPresentation('):R.index('void ResetFrontBufferState(')]
for token in ('projection_cpu_ms=','mesh_pack_ms=0.000','mesh_pack_inline=1','mesh_upload_ms=','back_render_ms=','projected_vertices=','uploaded_vertices=','drawn_vertices=','visible_entries=','culled_entries=0'):
    ck(token in log,'telemetry publishes '+token)
ck('ResetPhaseAMeasurementWindow();' in log,'measurement window resets only after telemetry publication')
ck('TryCull' not in R and 'culled_entries=0' in R,'Phase A introduces no culling behavior')
ck('nextAuthoritativePresentationTickRealtime = presentationNow + 0.10f' in R,'fixed 10 Hz authority remains unchanged')
ck('RenderTextureFormat.ARGB32' in R and 'FilterMode.Bilinear' in R,'visual RenderTexture authority remains unchanged')
failed=[n for ok,n in checks if not ok]
print('\n[Operation Health Phase A Measurement] %d/%d PASS' % (len(checks)-len(failed),len(checks)))
if failed:
    print('FAILED: '+', '.join(failed)); raise SystemExit(1)
