#!/usr/bin/env python3
from pathlib import Path

root = Path(__file__).resolve().parents[1]
renderer = root/'Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs'
text = renderer.read_text()

def replace_once(source, old, new, label):
    count = source.count(old)
    if count != 1:
        raise SystemExit(f'{label}: expected exactly one match, found {count}')
    return source.replace(old, new, 1)

text = replace_once(
    text,
    '        long operationHealthPreparedEntryUses;\n',
    '''        long operationHealthPreparedEntryUses;\n        // AERIS23 Operation Health Phase A: low-overhead measurement only. These\n        // counters do not alter presentation authority, geometry, painter order or cadence.\n        double phaseAMeasureProjectionCpuMs;\n        double phaseAMeasureMeshUploadMs;\n        double phaseAMeasureBackRenderMs;\n        long phaseAMeasureBackRenderSamples;\n        long phaseAMeasureProjectedVertices;\n        long phaseAMeasureUploadedVertices;\n        long phaseAMeasureDrawnVertices;\n        long phaseAMeasureVisibleEntries;\n''',
    'Phase A fields')

text = replace_once(
    text,
    '''                    if (drawEntry == null) continue;\n                    operationHealthPreparedEntryUses++;\n''',
    '''                    if (drawEntry == null) continue;\n                    phaseAMeasureVisibleEntries++;\n                    operationHealthPreparedEntryUses++;\n''',
    'visible entry counter')

text = replace_once(
    text,
    '''            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)\n                runtime.Gpu.RecordFrameCost((Stopwatch.GetTimestamp() - frameStartTicks) *\n                    1000.0 / Stopwatch.Frequency);\n            return rendered;\n''',
    '''            double backRenderMilliseconds = (Stopwatch.GetTimestamp() -\n                frameStartTicks) * 1000.0 / Stopwatch.Frequency;\n            phaseAMeasureBackRenderMs += backRenderMilliseconds;\n            phaseAMeasureBackRenderSamples++;\n            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;\n            if (runtime != null)\n                runtime.Gpu.RecordFrameCost(backRenderMilliseconds);\n            return rendered;\n''',
    'back render timing')

old_project = '''        void ProjectMesh(Mesh mesh, GeographicUnitPoint[] points,\n            Vector3[] projectedVertices, AERISNdMapProjection context)\n        {\n            if (mesh == null || points == null || projectedVertices == null ||\n                points.Length != projectedVertices.Length) return;\n            for (int i = 0; i < points.Length; i++)\n            {\n                GeographicUnitPoint point = points[i];\n                float u, v;\n                context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,\n                    out u, out v);\n                projectedVertices[i] = new Vector3(u, v, 0f);\n            }\n            mesh.vertices = projectedVertices;\n            operationHealthBoundsSkips++;\n        }\n'''
new_project = '''        void ProjectMesh(Mesh mesh, GeographicUnitPoint[] points,\n            Vector3[] projectedVertices, AERISNdMapProjection context)\n        {\n            if (mesh == null || points == null || projectedVertices == null ||\n                points.Length != projectedVertices.Length) return;\n            // Phase A deliberately times at mesh granularity rather than per vertex.\n            // Vector3 packing remains inline with exact projection to avoid adding a\n            // measurement-only second pass over every vertex.\n            long projectionStartTicks = Stopwatch.GetTimestamp();\n            for (int i = 0; i < points.Length; i++)\n            {\n                GeographicUnitPoint point = points[i];\n                float u, v;\n                context.ProjectUnitToRenderNUp(point.X, point.Y, point.Z,\n                    out u, out v);\n                projectedVertices[i] = new Vector3(u, v, 0f);\n            }\n            long uploadStartTicks = Stopwatch.GetTimestamp();\n            mesh.vertices = projectedVertices;\n            long uploadEndTicks = Stopwatch.GetTimestamp();\n            phaseAMeasureProjectionCpuMs += (uploadStartTicks - projectionStartTicks) *\n                1000.0 / Stopwatch.Frequency;\n            phaseAMeasureMeshUploadMs += (uploadEndTicks - uploadStartTicks) *\n                1000.0 / Stopwatch.Frequency;\n            phaseAMeasureProjectedVertices += points.LongLength;\n            phaseAMeasureUploadedVertices += projectedVertices.LongLength;\n            operationHealthBoundsSkips++;\n        }\n'''
text = replace_once(text, old_project, new_project, 'ProjectMesh measurement')

text = replace_once(
    text,
    '''                if (entry.WaterMesh != null) Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);\n                if (entry.LandMesh != null) Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);\n                if (entry.CoastalWaterCorrectionMesh != null)\n                    Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);\n                if (entry.CoastalLandCorrectionMesh != null)\n                    Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);\n                rendered = true;\n''',
    '''                if (entry.WaterMesh != null)\n                {\n                    phaseAMeasureDrawnVertices += entry.WaterMesh.vertexCount;\n                    Graphics.DrawMeshNow(entry.WaterMesh, mapMatrix);\n                }\n                if (entry.LandMesh != null)\n                {\n                    phaseAMeasureDrawnVertices += entry.LandMesh.vertexCount;\n                    Graphics.DrawMeshNow(entry.LandMesh, mapMatrix);\n                }\n                if (entry.CoastalWaterCorrectionMesh != null)\n                {\n                    phaseAMeasureDrawnVertices +=\n                        entry.CoastalWaterCorrectionMesh.vertexCount;\n                    Graphics.DrawMeshNow(entry.CoastalWaterCorrectionMesh, mapMatrix);\n                }\n                if (entry.CoastalLandCorrectionMesh != null)\n                {\n                    phaseAMeasureDrawnVertices +=\n                        entry.CoastalLandCorrectionMesh.vertexCount;\n                    Graphics.DrawMeshNow(entry.CoastalLandCorrectionMesh, mapMatrix);\n                }\n                rendered = true;\n''',
    'terrain drawn vertices')

text = replace_once(
    text,
    '''            if (drawContours && entry.ContourMesh != null &&\n                contourMaterial.SetPass(0))\n                Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);\n            if (entry.CoastlineMesh != null && coastlineMaterial.SetPass(0))\n                Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);\n''',
    '''            if (drawContours && entry.ContourMesh != null &&\n                contourMaterial.SetPass(0))\n            {\n                phaseAMeasureDrawnVertices += entry.ContourMesh.vertexCount;\n                Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);\n            }\n            if (entry.CoastlineMesh != null && coastlineMaterial.SetPass(0))\n            {\n                phaseAMeasureDrawnVertices += entry.CoastlineMesh.vertexCount;\n                Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);\n            }\n''',
    'line drawn vertices')

text = replace_once(
    text,
    '''                "; oh_view_invalidate=" + operationHealthViewInvalidations +\n                "; cpu_terrain_draw=0.");\n        }\n\n        void ResetFrontBufferState(bool preserveCadenceAndContent = false)\n''',
    '''                "; oh_view_invalidate=" + operationHealthViewInvalidations +\n                "; phase_a_measure=1" +\n                "; measure_samples=" + phaseAMeasureBackRenderSamples +\n                "; projection_cpu_ms=" + PhaseAAverageMilliseconds(\n                    phaseAMeasureProjectionCpuMs).ToString("F3", CultureInfo.InvariantCulture) +\n                "; mesh_pack_ms=0.000; mesh_pack_inline=1" +\n                "; mesh_upload_ms=" + PhaseAAverageMilliseconds(\n                    phaseAMeasureMeshUploadMs).ToString("F3", CultureInfo.InvariantCulture) +\n                "; back_render_ms=" + PhaseAAverageMilliseconds(\n                    phaseAMeasureBackRenderMs).ToString("F3", CultureInfo.InvariantCulture) +\n                "; projected_vertices=" + PhaseAAverageCount(phaseAMeasureProjectedVertices) +\n                "; uploaded_vertices=" + PhaseAAverageCount(phaseAMeasureUploadedVertices) +\n                "; drawn_vertices=" + PhaseAAverageCount(phaseAMeasureDrawnVertices) +\n                "; visible_entries=" + PhaseAAverageCount(phaseAMeasureVisibleEntries) +\n                "; culled_entries=0" +\n                "; cpu_terrain_draw=0.");\n            ResetPhaseAMeasurementWindow();\n        }\n\n        double PhaseAAverageMilliseconds(double totalMilliseconds)\n        {\n            return phaseAMeasureBackRenderSamples <= 0 ? 0.0 :\n                totalMilliseconds / phaseAMeasureBackRenderSamples;\n        }\n\n        long PhaseAAverageCount(long totalCount)\n        {\n            if (phaseAMeasureBackRenderSamples <= 0) return 0L;\n            return (long)Math.Round(totalCount /\n                (double)phaseAMeasureBackRenderSamples);\n        }\n\n        void ResetPhaseAMeasurementWindow()\n        {\n            phaseAMeasureProjectionCpuMs = 0.0;\n            phaseAMeasureMeshUploadMs = 0.0;\n            phaseAMeasureBackRenderMs = 0.0;\n            phaseAMeasureBackRenderSamples = 0L;\n            phaseAMeasureProjectedVertices = 0L;\n            phaseAMeasureUploadedVertices = 0L;\n            phaseAMeasureDrawnVertices = 0L;\n            phaseAMeasureVisibleEntries = 0L;\n        }\n\n        void ResetFrontBufferState(bool preserveCadenceAndContent = false)\n''',
    'Phase A telemetry publication')

renderer.write_text(text)

selftest = root/'Tools/selftest_v01800_operation_health_phase_a_measurement.py'
selftest.write_text(r'''#!/usr/bin/env python3
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
''')

runner = root/'Tools/run_v01800_operation_health_pass3_prebuild.py'
rt = runner.read_text()
marker = " ('Operation Health Step 2 Motion Content Split + Coastal Edge Refinement','selftest_v01800_operation_health_step2_motion_content_coastal_refinement.py'),\n"
addition = " ('Operation Health Phase A Measurement','selftest_v01800_operation_health_phase_a_measurement.py'),\n"
if addition not in rt:
    if marker not in rt:
        raise SystemExit('prebuild insertion marker not found')
    rt = rt.replace(marker, marker + addition, 1)
runner.write_text(rt)
