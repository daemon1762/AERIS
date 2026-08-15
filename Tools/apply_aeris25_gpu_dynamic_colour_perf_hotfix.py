#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
SHADER = ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader"
RENDERER = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
MONITOR = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS25 ATROPINE REV002] %s anchor mismatch old=%d" % (label, count))
    return text.replace(old, new, 1), True


# Runtime evidence from OH_PHASE4_001: CPU BACK time collapsed, but whole-frame p95
# regressed while GPU_DYNAMIC_SEMANTIC was active.  Keep the exact same colour laws,
# thresholds and quantization, but make REL and TOPO shader execution explicitly
# mutually exclusive.  The prior ternary function selection could be flattened by the
# shader compiler into evaluation of both colour functions per terrain vertex.
shader = SHADER.read_text()
old_colour = '''                bool relativeMode = _AerisTerrainDisplayMode > 0.5;
                float4 baseColour = relativeMode ?
                    AerisRelativeColour(_AerisAircraftAltitudeMeters - semantic.x, preset) :
                    AerisTopographicColour(semantic.x, preset);
                return AerisApplyShade(baseColour, semantic.y, relativeMode);
'''
new_colour = '''                // AERIS25_DYNAMIC_COLOUR_MODE_SPLIT: REL and TOPO are uniform-mode
                // exclusive.  Preserve the exact existing equations while preventing
                // the unused colour path from becoming per-vertex work.
                if (_AerisTerrainDisplayMode > 0.5)
                    return AerisApplyShade(
                        AerisRelativeColour(_AerisAircraftAltitudeMeters - semantic.x, preset),
                        semantic.y, true);
                return AerisApplyShade(AerisTopographicColour(semantic.x, preset),
                    semantic.y, false);
'''
shader, changed = replace_once(shader, old_colour, new_colour,
                               'shader REL/TOPO mode split')
if changed:
    SHADER.write_text(shader)
    print('[AERIS25 ATROPINE REV002] shader REL/TOPO execution split applied')
else:
    print('[AERIS25 ATROPINE REV002] shader mode split already present')

renderer = RENDERER.read_text()

# Attribute-failure cause telemetry.  These counters do not change fallback authority;
# they only identify which immutable geographic stream failed the existing length gate.
field_old = '''        long operationHealthGpuDynamicSemanticUploads;
        long operationHealthGpuDynamicSemanticFailures;
        long operationHealthGpuDynamicCpuColourBypasses;
'''
field_new = '''        long operationHealthGpuDynamicSemanticUploads;
        long operationHealthGpuDynamicSemanticFailures;
        long operationHealthGpuDynamicCpuColourBypasses;
        long operationHealthGpuDynamicVerticesSubmitted;
        long operationHealthGpuVertexPackedMismatch;
        long operationHealthGpuVertexContourMismatch;
        long operationHealthGpuVertexCoastlineMismatch;
'''
renderer, hit = replace_once(renderer, field_old, field_new,
                             'runtime performance telemetry fields')

calls_old = '''                if (!UploadGpuGeographicAttribute(entry.PackedTerrainMesh,
                        entry.PackedTerrainGeographicPoints) ||
                    !UploadGpuGeographicAttribute(entry.ContourMesh,
                        entry.ContourGeographicPoints) ||
                    !UploadGpuGeographicAttribute(entry.CoastlineMesh,
                        entry.CoastlineGeographicPoints))
'''
calls_new = '''                if (!UploadGpuGeographicAttribute(entry.PackedTerrainMesh,
                        entry.PackedTerrainGeographicPoints,
                        ref operationHealthGpuVertexPackedMismatch) ||
                    !UploadGpuGeographicAttribute(entry.ContourMesh,
                        entry.ContourGeographicPoints,
                        ref operationHealthGpuVertexContourMismatch) ||
                    !UploadGpuGeographicAttribute(entry.CoastlineMesh,
                        entry.CoastlineGeographicPoints,
                        ref operationHealthGpuVertexCoastlineMismatch))
'''
renderer, hit2 = replace_once(renderer, calls_old, calls_new,
                              'GPU geographic attribute cause counters')

helper_old = '''        bool UploadGpuGeographicAttribute(Mesh mesh, GeographicUnitPoint[] points)
        {
            if (mesh == null) return true;
            if (points == null || points.Length != mesh.vertexCount) return false;
'''
helper_new = '''        bool UploadGpuGeographicAttribute(Mesh mesh, GeographicUnitPoint[] points,
            ref long mismatchCounter)
        {
            if (mesh == null) return true;
            if (points == null || points.Length != mesh.vertexCount)
            {
                mismatchCounter++;
                return false;
            }
'''
renderer, hit3 = replace_once(renderer, helper_old, helper_new,
                              'GPU geographic attribute mismatch helper')

draw_old = '''                if (gpuEntry) operationHealthGpuVertexDraws++;
                int saved = Math.Max(0, entry.PackedTerrainSourceMeshCount - 1);
'''
draw_new = '''                if (gpuEntry)
                {
                    operationHealthGpuVertexDraws++;
                    operationHealthGpuDynamicVerticesSubmitted +=
                        entry.PackedTerrainMesh.vertexCount;
                }
                int saved = Math.Max(0, entry.PackedTerrainSourceMeshCount - 1);
'''
renderer, hit4 = replace_once(renderer, draw_old, draw_new,
                              'GPU submitted vertex telemetry')

telemetry_old = '''                "; oh_gpu_vertex_attr_fail=" + operationHealthGpuVertexAttributeFailures +
                "; oh_gpu_vertex_exact_bypass=" + operationHealthGpuVertexExactBypasses +
'''
telemetry_new = '''                "; oh_gpu_vertex_attr_fail=" + operationHealthGpuVertexAttributeFailures +
                "; oh_gpu_vertex_packed_mismatch=" + operationHealthGpuVertexPackedMismatch +
                "; oh_gpu_vertex_contour_mismatch=" + operationHealthGpuVertexContourMismatch +
                "; oh_gpu_vertex_coast_mismatch=" + operationHealthGpuVertexCoastlineMismatch +
                "; oh_gpu_vertex_exact_bypass=" + operationHealthGpuVertexExactBypasses +
'''
renderer, hit5 = replace_once(renderer, telemetry_old, telemetry_new,
                              'GPU attr-cause telemetry publication')

telemetry2_old = '''                "; oh_gpu_dynamic_cpu_colour_bypass=" + operationHealthGpuDynamicCpuColourBypasses +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +'''
telemetry2_new = '''                "; oh_gpu_dynamic_cpu_colour_bypass=" + operationHealthGpuDynamicCpuColourBypasses +
                "; oh_gpu_dynamic_vertex_submit=" + operationHealthGpuDynamicVerticesSubmitted +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +'''
renderer, hit6 = replace_once(renderer, telemetry2_old, telemetry2_new,
                              'GPU vertex pressure telemetry publication')

if any((hit, hit2, hit3, hit4, hit5, hit6)):
    RENDERER.write_text(renderer)
    print('[AERIS25 ATROPINE REV002] GPU fallback-cause and vertex-pressure telemetry applied')
else:
    print('[AERIS25 ATROPINE REV002] runtime telemetry hotfix already present')

monitor = MONITOR.read_text()
monitor, rev_changed = replace_once(
    monitor,
    'internal const string Revision = "OH_PHASE4_001";',
    'internal const string Revision = "OH_PHASE4_002";',
    'Operation Health revision promotion')
if rev_changed:
    MONITOR.write_text(monitor)
    print('[AERIS25 ATROPINE REV002] revision=OH_PHASE4_002')
else:
    print('[AERIS25 ATROPINE REV002] revision already OH_PHASE4_002')

print('[AERIS25 ATROPINE REV002] performance hotfix applied')
print('Contract: same Golden colour equations; REL/TOPO shader work is mode-exclusive')
print('Telemetry: packed/contour/coastline attr mismatches + submitted dynamic-colour vertices')
