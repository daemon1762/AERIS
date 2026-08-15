#!/usr/bin/env python3
from pathlib import Path
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
RENDERER = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
MONITOR = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
BUILD = ROOT / "build_ubuntu.sh"


def replace_once(text, old, new, label):
    if new in text:
        return text, False
    count = text.count(old)
    if count != 1:
        raise SystemExit("[AERIS25 ATROPINE REV007] %s anchor mismatch old=%d" %
                         (label, count))
    return text.replace(old, new, 1), True


renderer = RENDERER.read_text()

field_old = '''        long operationHealthGpuVertexPackedMismatch;
        long operationHealthGpuVertexContourMismatch;
        long operationHealthGpuVertexCoastlineMismatch;
'''
field_new = '''        long operationHealthGpuVertexPackedMismatch;
        long operationHealthGpuVertexContourMismatch;
        long operationHealthGpuVertexCoastlineMismatch;
        // AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS: diagnostic-only attribution.
        // These counters never alter render/fallback authority. Initial rejection
        // accounting must reconcile exactly with oh_gpu_vertex_attr_fail.
        const int GpuVertexRejectDiagnosticSampleLimit = 64;
        long operationHealthGpuVertexRejectInitial;
        long operationHealthGpuVertexRejectRevisits;
        long operationHealthGpuVertexRejectPackedNull;
        long operationHealthGpuVertexRejectPackedLength;
        long operationHealthGpuVertexRejectContourNull;
        long operationHealthGpuVertexRejectContourLength;
        long operationHealthGpuVertexRejectCoastNull;
        long operationHealthGpuVertexRejectCoastLength;
        long operationHealthGpuVertexRejectSemanticPackedMeshNull;
        long operationHealthGpuVertexRejectSemanticRejected;
        long operationHealthGpuVertexRejectSemanticException;
        long operationHealthGpuVertexRejectSemanticOther;
        long operationHealthGpuVertexRejectException;
        long operationHealthGpuVertexRejectOther;
        int operationHealthGpuVertexRejectDiagnosticSamples;
'''
renderer, fields_changed = replace_once(
    renderer, field_old, field_new, 'GPU reject diagnostic telemetry fields')

marker = 'AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS'
if marker not in renderer:
    raise SystemExit('[AERIS25 ATROPINE REV007] diagnostic field marker missing after insertion')

function_start = renderer.find('        bool EnsureGpuVertexProjectionAttributes(Entry entry)\n')
function_end = renderer.find('        bool UploadGpuGeographicAttribute(', function_start)
if function_start < 0 or function_end < 0 or function_end <= function_start:
    raise SystemExit('[AERIS25 ATROPINE REV007] GPU attribute helper boundary not found')

old_function = renderer[function_start:function_end]
if 'RecordGpuVertexProjectionReject' not in old_function:
    new_function = r'''        void RecordGpuVertexProjectionReject(Entry entry, string reason)
        {
            // AERIS25_GPU_VERTEX_REJECT_DIAGNOSTICS is observation-only. Keep the
            // generic failure counter authoritative and classify only the same initial
            // false->true reject transitions that already caused CPU exact fallback.
            operationHealthGpuVertexRejectInitial++;
            if (reason == "PACKED_GEO_NULL")
                operationHealthGpuVertexRejectPackedNull++;
            else if (reason == "PACKED_GEO_LENGTH")
                operationHealthGpuVertexRejectPackedLength++;
            else if (reason == "CONTOUR_GEO_NULL")
                operationHealthGpuVertexRejectContourNull++;
            else if (reason == "CONTOUR_GEO_LENGTH")
                operationHealthGpuVertexRejectContourLength++;
            else if (reason == "COAST_GEO_NULL")
                operationHealthGpuVertexRejectCoastNull++;
            else if (reason == "COAST_GEO_LENGTH")
                operationHealthGpuVertexRejectCoastLength++;
            else if (reason == "SEMANTIC_PACKED_MESH_NULL")
                operationHealthGpuVertexRejectSemanticPackedMeshNull++;
            else if (reason == "SEMANTIC_REJECTED")
                operationHealthGpuVertexRejectSemanticRejected++;
            else if (reason == "SEMANTIC_EXCEPTION")
                operationHealthGpuVertexRejectSemanticException++;
            else if (reason == "SEMANTIC_OTHER")
                operationHealthGpuVertexRejectSemanticOther++;
            else if (reason == "EXCEPTION")
                operationHealthGpuVertexRejectException++;
            else
                operationHealthGpuVertexRejectOther++;

            if (operationHealthGpuVertexRejectDiagnosticSamples >=
                GpuVertexRejectDiagnosticSampleLimit)
                return;

            operationHealthGpuVertexRejectDiagnosticSamples++;
            try
            {
                int packedVertices = entry == null || entry.PackedTerrainMesh == null ?
                    -1 : entry.PackedTerrainMesh.vertexCount;
                int contourVertices = entry == null || entry.ContourMesh == null ?
                    -1 : entry.ContourMesh.vertexCount;
                int coastVertices = entry == null || entry.CoastlineMesh == null ?
                    -1 : entry.CoastlineMesh.vertexCount;
                int packedGeo = entry == null || entry.PackedTerrainGeographicPoints == null ?
                    -1 : entry.PackedTerrainGeographicPoints.Length;
                int contourGeo = entry == null || entry.ContourGeographicPoints == null ?
                    -1 : entry.ContourGeographicPoints.Length;
                int coastGeo = entry == null || entry.CoastlineGeographicPoints == null ?
                    -1 : entry.CoastlineGeographicPoints.Length;
                AERISLogger.Warn("[AERIS25_GPU_VERTEX_REJECT_DIAG] sample=" +
                    operationHealthGpuVertexRejectDiagnosticSamples + "/" +
                    GpuVertexRejectDiagnosticSampleLimit + "; reason=" + reason +
                    "; key=" + (entry == null || entry.CacheKey == null ? "NONE" : entry.CacheKey) +
                    "; lod=" + (entry == null ? "NONE" : entry.TileKey.Lod.ToString()) +
                    "; packedV=" + packedVertices + "; packedGeo=" + packedGeo +
                    "; contourV=" + contourVertices + "; contourGeo=" + contourGeo +
                    "; coastV=" + coastVertices + "; coastGeo=" + coastGeo +
                    "; gpuReady=" + (entry != null && entry.GpuVertexProjectionAttributesReady) +
                    "; semanticReady=" + (entry != null && entry.GpuDynamicColourAttributesReady) +
                    "; semanticRejected=" + (entry != null && entry.GpuDynamicColourRejected) +
                    "; coverage=" + (entry == null ? "NONE" :
                        entry.CoverageFraction.ToString("F3", CultureInfo.InvariantCulture)) + ".");
            }
            catch
            {
                // Diagnostics must never change renderer/fallback behaviour.
            }
        }

        bool EnsureGpuVertexProjectionAttributes(Entry entry)
        {
            if (entry == null || !gpuVertexProjection.Active) return false;
            if (entry.GpuVertexProjectionRejected)
            {
                operationHealthGpuVertexRejectRevisits++;
                return false;
            }
            if (entry.GpuVertexProjectionAttributesReady) return true;
            try
            {
                if (!UploadGpuGeographicAttribute(entry.PackedTerrainMesh,
                        entry.PackedTerrainGeographicPoints,
                        ref operationHealthGpuVertexPackedMismatch))
                {
                    RecordGpuVertexProjectionReject(entry,
                        entry.PackedTerrainGeographicPoints == null ?
                            "PACKED_GEO_NULL" : "PACKED_GEO_LENGTH");
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                if (!UploadGpuGeographicAttribute(entry.ContourMesh,
                        entry.ContourGeographicPoints,
                        ref operationHealthGpuVertexContourMismatch))
                {
                    RecordGpuVertexProjectionReject(entry,
                        entry.ContourGeographicPoints == null ?
                            "CONTOUR_GEO_NULL" : "CONTOUR_GEO_LENGTH");
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                if (!UploadGpuGeographicAttribute(entry.CoastlineMesh,
                        entry.CoastlineGeographicPoints,
                        ref operationHealthGpuVertexCoastlineMismatch))
                {
                    RecordGpuVertexProjectionReject(entry,
                        entry.CoastlineGeographicPoints == null ?
                            "COAST_GEO_NULL" : "COAST_GEO_LENGTH");
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }

                long semanticFailuresBefore = operationHealthGpuDynamicSemanticFailures;
                if (!EnsureGpuDynamicTerrainColourAttributes(entry))
                {
                    string semanticReason;
                    if (entry.PackedTerrainMesh == null)
                        semanticReason = "SEMANTIC_PACKED_MESH_NULL";
                    else if (operationHealthGpuDynamicSemanticFailures >
                        semanticFailuresBefore)
                        semanticReason = "SEMANTIC_EXCEPTION";
                    else if (entry.GpuDynamicColourRejected)
                        semanticReason = "SEMANTIC_REJECTED";
                    else
                        semanticReason = "SEMANTIC_OTHER";
                    RecordGpuVertexProjectionReject(entry, semanticReason);
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                entry.GpuVertexProjectionAttributesReady = true;
                return true;
            }
            catch (Exception ex)
            {
                RecordGpuVertexProjectionReject(entry, "EXCEPTION");
                entry.GpuVertexProjectionRejected = true;
                operationHealthGpuVertexAttributeFailures++;
                AERISLogger.Warn("[AERIS24_GPU_VERTEX_PROJECTION] Entry CPU fallback; key=" +
                    (entry.CacheKey ?? "NONE") + "; reason=" + ex.GetType().Name +
                    ": " + ex.Message + ".");
                return false;
            }
        }

'''
    renderer = renderer[:function_start] + new_function + renderer[function_end:]
    function_changed = True
else:
    function_changed = False

telemetry_old = '''                "; oh_gpu_vertex_coast_mismatch=" + operationHealthGpuVertexCoastlineMismatch +
                "; oh_gpu_vertex_exact_bypass=" + operationHealthGpuVertexExactBypasses +
'''
telemetry_new = '''                "; oh_gpu_vertex_coast_mismatch=" + operationHealthGpuVertexCoastlineMismatch +
                "; oh_gpu_vertex_reject_initial=" + operationHealthGpuVertexRejectInitial +
                "; oh_gpu_vertex_reject_revisit=" + operationHealthGpuVertexRejectRevisits +
                "; oh_gpu_vertex_reject_packed_null=" + operationHealthGpuVertexRejectPackedNull +
                "; oh_gpu_vertex_reject_packed_length=" + operationHealthGpuVertexRejectPackedLength +
                "; oh_gpu_vertex_reject_contour_null=" + operationHealthGpuVertexRejectContourNull +
                "; oh_gpu_vertex_reject_contour_length=" + operationHealthGpuVertexRejectContourLength +
                "; oh_gpu_vertex_reject_coast_null=" + operationHealthGpuVertexRejectCoastNull +
                "; oh_gpu_vertex_reject_coast_length=" + operationHealthGpuVertexRejectCoastLength +
                "; oh_gpu_vertex_reject_semantic_mesh_null=" + operationHealthGpuVertexRejectSemanticPackedMeshNull +
                "; oh_gpu_vertex_reject_semantic_rejected=" + operationHealthGpuVertexRejectSemanticRejected +
                "; oh_gpu_vertex_reject_semantic_exception=" + operationHealthGpuVertexRejectSemanticException +
                "; oh_gpu_vertex_reject_semantic_other=" + operationHealthGpuVertexRejectSemanticOther +
                "; oh_gpu_vertex_reject_exception=" + operationHealthGpuVertexRejectException +
                "; oh_gpu_vertex_reject_other=" + operationHealthGpuVertexRejectOther +
                "; oh_gpu_vertex_reject_samples=" + operationHealthGpuVertexRejectDiagnosticSamples +
                "; oh_gpu_vertex_exact_bypass=" + operationHealthGpuVertexExactBypasses +
'''
renderer, telemetry_changed = replace_once(
    renderer, telemetry_old, telemetry_new, 'GPU reject diagnostic publication')

if fields_changed or function_changed or telemetry_changed:
    RENDERER.write_text(renderer)
    print('[AERIS25 ATROPINE REV007] GPU vertex reject diagnostics applied')
else:
    print('[AERIS25 ATROPINE REV007] GPU vertex reject diagnostics already present')

monitor = MONITOR.read_text()
if 'internal const string Revision = "OH_PHASE4_007";' not in monitor:
    if monitor.count('internal const string Revision = "OH_PHASE4_006";') != 1:
        raise SystemExit('[AERIS25 ATROPINE REV007] Operation Health revision anchor mismatch')
    monitor = monitor.replace('internal const string Revision = "OH_PHASE4_006";',
                              'internal const string Revision = "OH_PHASE4_007";', 1)
    MONITOR.write_text(monitor)
    print('[AERIS25 ATROPINE REV007] revision=OH_PHASE4_007')
else:
    print('[AERIS25 ATROPINE REV007] revision already OH_PHASE4_007')

build = BUILD.read_text()
old_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV006 RENDERABLE ENTRY GATE"'
new_display = 'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ATROPINE GPU DYNAMIC TERRAIN COLOUR REV007 GPU VERTEX REJECT DIAGNOSTICS"'
build, display_changed = replace_once(build, old_display, new_display,
                                      'in-game display revision')
old_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV006 RENDERABLE ENTRY GATE";'
new_checkpoint = 'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ATROPINE — GPU DYNAMIC TERRAIN COLOUR — REV007 GPU VERTEX REJECT DIAGNOSTICS";'
build, checkpoint_changed = replace_once(build, old_checkpoint, new_checkpoint,
                                         'in-game checkpoint revision')

renderable_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_renderable_entry_gate_hotfix.py"'
diagnostic_verify = 'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_vertex_reject_diagnostics_hotfix.py"'
active_renderable = sum(1 for line in build.splitlines()
                        if line.strip() == renderable_verify)
active_diagnostic = sum(1 for line in build.splitlines()
                        if line.strip() == diagnostic_verify)
verify_changed = False
if active_renderable == 1 and active_diagnostic == 0:
    build = build.replace(renderable_verify, diagnostic_verify, 1)
    verify_changed = True
elif active_renderable == 0 and active_diagnostic == 1:
    pass
else:
    raise SystemExit('[AERIS25 ATROPINE REV007] build verifier gate mismatch renderable=%d diagnostic=%d' %
                     (active_renderable, active_diagnostic))

if display_changed or checkpoint_changed or verify_changed:
    BUILD.write_text(build)
    print('[AERIS25 ATROPINE REV007] build/in-game identity and verifier gate promoted')
else:
    print('[AERIS25 ATROPINE REV007] build/in-game identity already promoted')

print('[AERIS25 ATROPINE REV007] GPU VERTEX REJECT DIAGNOSTICS HOTFIX APPLIED')
print('Diagnostic contract: no shader/render/culling/fallback authority change')
print('Runtime invariant: oh_gpu_vertex_attr_fail == oh_gpu_vertex_reject_initial')
print('Revisits are isolated in oh_gpu_vertex_reject_revisit; first 64 initial rejects are sampled')
