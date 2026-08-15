#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]

# Always reconstruct the exact runtime-tested PENICILLIN lineage first.  GPU Vertex
# Projection is a presentation successor, not a reimplementation of Single-Authority,
# Witness Affine, Stagger, or Operation Health.
penicillin = ROOT / "Tools/apply_aeris23_oh_penicillin_candidate.py"
if not penicillin.is_file():
    raise SystemExit("[AERIS24 GPU VERTEX] PENICILLIN applicator missing")
subprocess.run([sys.executable, str(penicillin)], cwd=str(ROOT), check=True)

renderer = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
project = ROOT / "Source/AERISFlightControl/AERISFlightControl.csproj"
build = ROOT / "build_ubuntu.sh"
backend = ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"
shader = ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader"
builder = ROOT / "GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs"
for path in (renderer, project, build, backend, shader, builder):
    if not path.is_file():
        raise SystemExit("[AERIS24 GPU VERTEX] required file missing: " + str(path))


def replace_once(src, old, new, label):
    count = src.count(old)
    if count != 1:
        raise SystemExit("[AERIS24 GPU VERTEX] %s: expected 1 anchor, found %d" %
                         (label, count))
    return src.replace(old, new, 1)


def active_count(text, line):
    target = line.strip()
    return sum(1 for raw in text.splitlines() if raw.strip() == target)


def replace_active_line(text, old, new, label):
    old_count = active_count(text, old)
    new_count = active_count(text, new)
    if old_count == 1 and new_count == 0:
        lines = text.splitlines(True)
        for i, raw in enumerate(lines):
            if raw.strip() == old.strip():
                ending = "\n" if raw.endswith("\n") else ""
                indent = raw[:len(raw) - len(raw.lstrip())]
                lines[i] = indent + new.strip() + ending
                return "".join(lines)
    if old_count == 0 and new_count == 1:
        return text
    raise SystemExit("[AERIS24 GPU VERTEX] %s: old=%d new=%d" %
                     (label, old_count, new_count))

text = renderer.read_text()
if "oh_gpu_vertex_projection=" not in text:
    if "PackedTerrainMesh" not in text or "TryResolveWitnessAffineBridge(" not in text or \
       "ResolveStaggeredExactRefreshDeadlineSeconds" not in text:
        raise SystemExit("[AERIS24 GPU VERTEX] PENICILLIN/Stagger generated renderer prerequisite absent")

    # Per-Entry state records only whether the immutable geographic attribute has been
    # uploaded.  CPU projected arrays and exact fallback state remain intact.
    text = replace_once(text,
'''            internal int ExactRefreshStaggerSlot = -1;
            internal float[] LandElevationMeters;''',
'''            internal int ExactRefreshStaggerSlot = -1;
            // AERIS24 GPU Vertex Projection. Geographic XYZ is uploaded once into
            // TEXCOORD1. The original CPU projected arrays remain the complete fallback.
            internal bool GpuVertexProjectionAttributesReady;
            internal bool GpuVertexProjectionRejected;
            internal float[] LandElevationMeters;''',
'Entry GPU projection state')

    text = replace_once(text,
'''        readonly SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();
        readonly SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();
        readonly SurfacePoint[] surfaceClipScratch = new SurfacePoint[6];''',
'''        readonly SurfaceBuilder landSurfaceScratch = new SurfaceBuilder();
        readonly SurfaceBuilder waterSurfaceScratch = new SurfaceBuilder();
        readonly SurfacePoint[] surfaceClipScratch = new SurfacePoint[6];
        readonly AERISNdGpuVertexProjectionBackend gpuVertexProjection =
            new AERISNdGpuVertexProjectionBackend();
        readonly List<Vector3> gpuVertexGeographicScratch = new List<Vector3>(4096);
        bool gpuVertexProjectionBackFailure;''',
'GPU projection backend fields')

    text = replace_once(text,
'''        long operationHealthStaggeredExactDue;
        long operationHealthStaggeredExactDeferrals;
        long operationHealthLoadingBackdropFrames;''',
'''        long operationHealthStaggeredExactDue;
        long operationHealthStaggeredExactDeferrals;
        long operationHealthGpuVertexAttributeUploads;
        long operationHealthGpuVertexAttributeFailures;
        long operationHealthGpuVertexExactBypasses;
        long operationHealthGpuVertexBackFrames;
        long operationHealthGpuVertexDraws;
        long operationHealthLoadingBackdropFrames;''',
'GPU projection telemetry fields')

    helper_anchor = '''        Matrix4x4 EnsureProjectedGeometry(Entry entry,
            AERISNdMapProjection context, float movementThresholdMeters,'''
    if text.count(helper_anchor) != 1:
        raise SystemExit("[AERIS24 GPU VERTEX] EnsureProjectedGeometry helper anchor mismatch")
    helper = r'''        bool EnsureGpuVertexProjectionAttributes(Entry entry)
        {
            if (entry == null || !gpuVertexProjection.Active ||
                entry.GpuVertexProjectionRejected) return false;
            if (entry.GpuVertexProjectionAttributesReady) return true;
            try
            {
                if (!UploadGpuGeographicAttribute(entry.PackedTerrainMesh,
                        entry.PackedTerrainGeographicPoints) ||
                    !UploadGpuGeographicAttribute(entry.ContourMesh,
                        entry.ContourGeographicPoints) ||
                    !UploadGpuGeographicAttribute(entry.CoastlineMesh,
                        entry.CoastlineGeographicPoints))
                {
                    entry.GpuVertexProjectionRejected = true;
                    operationHealthGpuVertexAttributeFailures++;
                    return false;
                }
                entry.GpuVertexProjectionAttributesReady = true;
                return true;
            }
            catch (Exception ex)
            {
                entry.GpuVertexProjectionRejected = true;
                operationHealthGpuVertexAttributeFailures++;
                AERISLogger.Warn("[AERIS24_GPU_VERTEX_PROJECTION] Entry CPU fallback; key=" +
                    (entry.CacheKey ?? "NONE") + "; reason=" + ex.GetType().Name +
                    ": " + ex.Message + ".");
                return false;
            }
        }

        bool UploadGpuGeographicAttribute(Mesh mesh, GeographicUnitPoint[] points)
        {
            if (mesh == null) return true;
            if (points == null || points.Length != mesh.vertexCount) return false;
            gpuVertexGeographicScratch.Clear();
            if (gpuVertexGeographicScratch.Capacity < points.Length)
                gpuVertexGeographicScratch.Capacity = points.Length;
            for (int i = 0; i < points.Length; i++)
            {
                GeographicUnitPoint point = points[i];
                gpuVertexGeographicScratch.Add(new Vector3((float)point.X,
                    (float)point.Y, (float)point.Z));
            }
            // UV channel 1 maps to TEXCOORD1 and is immutable after this one-time upload.
            mesh.SetUVs(1, gpuVertexGeographicScratch);
            operationHealthGpuVertexAttributeUploads++;
            return true;
        }

'''
    text = text.replace(helper_anchor, helper + helper_anchor, 1)

    # GPU active = exact shader projection on every authoritative BACK.  The old
    # affine/stagger/ProjectMesh state machine is preserved byte-for-path as fallback.
    text = replace_once(text,
'''        {
            if (entry == null) return Matrix4x4.identity;
            bool structuralProjectionChange =''',
'''        {
            if (entry == null) return Matrix4x4.identity;
            if (gpuVertexProjection.Active && EnsureGpuVertexProjectionAttributes(entry))
            {
                operationHealthGpuVertexExactBypasses++;
                return Matrix4x4.identity;
            }
            bool structuralProjectionChange =''',
'GPU exact bypass before CPU affine/exact state machine')

    # Configure and preflight the custom pass before any Entry writes BACK.  On a pass
    # rejection the backend disables itself and this same frame follows CPU exact.
    text = replace_once(text,
'''                GL.Clear(true, true, Color.clear);
                float projectionThresholdMeters = Math.Max(0.25f,''',
'''                GL.Clear(true, true, Color.clear);
                gpuVertexProjectionBackFailure = false;
                bool gpuVertexFrameActive = gpuVertexProjection.TryEnsureLoaded();
                if (gpuVertexFrameActive)
                {
                    gpuVertexProjection.ConfigureProjection(projection,
                        ResolveContourColour(settings == null ?
                            AERISTerrainColourPreset.Standard :
                            settings.TerrainColourPreset));
                    gpuVertexFrameActive = gpuVertexProjection.ValidatePassesOrFallback();
                    if (gpuVertexFrameActive) operationHealthGpuVertexBackFrames++;
                }
                float projectionThresholdMeters = Math.Max(0.25f,''',
'GPU projection BACK preflight')

    # Strictly preserve Entry painter order: packed terrain -> contour -> coastline.
    draw_start = text.index('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
    draw_end = text.index('        static void EnsurePackedTerrainColours(Entry entry,', draw_start)
    gpu_draw = r'''        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || entry.PackedTerrainMesh == null) return false;
            EnsurePackedTerrainColours(entry, mode, preset, aircraftAltitudeAslMeters);
            bool gpuEntry = gpuVertexProjection.Active &&
                entry.GpuVertexProjectionAttributesReady &&
                !entry.GpuVertexProjectionRejected;
            Material terrainDrawMaterial = gpuEntry ?
                gpuVertexProjection.TerrainMaterial : terrainMaterial;
            Material contourDrawMaterial = gpuEntry ?
                gpuVertexProjection.ContourMaterial : contourMaterial;
            Material coastlineDrawMaterial = gpuEntry ?
                gpuVertexProjection.CoastlineMaterial : coastlineMaterial;
            bool rendered = false;
            if (terrainDrawMaterial != null && terrainDrawMaterial.SetPass(0))
            {
                Graphics.DrawMeshNow(entry.PackedTerrainMesh, mapMatrix);
                operationHealthDrawMeshSubmissions++;
                operationHealthPackedTerrainDraws++;
                if (gpuEntry) operationHealthGpuVertexDraws++;
                int saved = Math.Max(0, entry.PackedTerrainSourceMeshCount - 1);
                operationHealthPackedTerrainDrawSubmissionsSaved += saved;
                operationHealthTerrainSetPassSaved += saved;
                rendered = true;
            }
            else if (gpuEntry)
            {
                gpuVertexProjectionBackFailure = true;
                gpuVertexProjection.DisableAndFallback("terrain SetPass failed after preflight");
                return false;
            }
            if (drawContours && entry.ContourMesh != null)
            {
                if (contourDrawMaterial != null && contourDrawMaterial.SetPass(0))
                {
                    Graphics.DrawMeshNow(entry.ContourMesh, mapMatrix);
                    operationHealthDrawMeshSubmissions++;
                }
                else if (gpuEntry)
                {
                    gpuVertexProjectionBackFailure = true;
                    gpuVertexProjection.DisableAndFallback("contour SetPass failed after preflight");
                    return false;
                }
            }
            if (entry.CoastlineMesh != null)
            {
                if (coastlineDrawMaterial != null && coastlineDrawMaterial.SetPass(0))
                {
                    Graphics.DrawMeshNow(entry.CoastlineMesh, mapMatrix);
                    operationHealthDrawMeshSubmissions++;
                }
                else if (gpuEntry)
                {
                    gpuVertexProjectionBackFailure = true;
                    gpuVertexProjection.DisableAndFallback("coastline SetPass failed after preflight");
                    return false;
                }
            }
            return rendered;
        }

'''
    text = text[:draw_start] + gpu_draw + text[draw_end:]

    text = replace_once(text,
'''            return rendered;
        }

        float MeasureFoundationGpuReadiness''',
'''            return rendered && !gpuVertexProjectionBackFailure;
        }

        float MeasureFoundationGpuReadiness''',
'prevent partial GPU BACK swap on runtime shader failure')

    text = replace_once(text,
'''                "; oh_stagger_due=" + operationHealthStaggeredExactDue +
                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_stagger_due=" + operationHealthStaggeredExactDue +
                "; oh_stagger_defer=" + operationHealthStaggeredExactDeferrals +
                "; oh_gpu_vertex_projection=" +
                    (gpuVertexProjection.Active ? "ACTIVE" : "CPU_FALLBACK") +
                "; oh_gpu_vertex_attr_upload=" + operationHealthGpuVertexAttributeUploads +
                "; oh_gpu_vertex_attr_fail=" + operationHealthGpuVertexAttributeFailures +
                "; oh_gpu_vertex_exact_bypass=" + operationHealthGpuVertexExactBypasses +
                "; oh_gpu_vertex_back_frames=" + operationHealthGpuVertexBackFrames +
                "; oh_gpu_vertex_draws=" + operationHealthGpuVertexDraws +
                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'GPU projection telemetry publication')

    text = replace_once(text,
'''            uniformColourScratch.Clear();
            completed.Clear();''',
'''            uniformColourScratch.Clear();
            gpuVertexGeographicScratch.Clear();
            gpuVertexProjection.ReleaseForSuspension();
            completed.Clear();''',
'GPU projection suspension release')

    text = replace_once(text,
'''            disposed = true;
            rasterizer.Dispose();''',
'''            disposed = true;
            gpuVertexProjection.Dispose();
            rasterizer.Dispose();''',
'GPU projection final dispose')

    renderer.write_text(text)
else:
    print("[AERIS24 GPU VERTEX] renderer patch already applied")

# Compile the dedicated runtime backend without changing any AA/AP/PROTECT/LAND source.
proj = project.read_text()
compile_line = '    <Compile Include="Terrain\\AERISNdGpuVertexProjectionBackend.cs" />\n'
anchor = '    <Compile Include="Terrain\\AERISTerrainGpuTileRenderer.cs" />\n'
if compile_line not in proj:
    if proj.count(anchor) != 1:
        raise SystemExit("[AERIS24 GPU VERTEX] csproj renderer anchor mismatch")
    proj = proj.replace(anchor, compile_line + anchor, 1)
project.write_text(proj)

# Promote only executable identity lines. Historical identity text remains traceable.
b = build.read_text()
b = replace_active_line(b,
    'CANDIDATE_NAME="AERIS23_OH_PENICILLIN"',
    'CANDIDATE_NAME="AERIS24_GPU_VERTEX_PROJECTION_POC"',
    'candidate-name promotion')
b = replace_active_line(b,
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris23_oh_penicillin_candidate.py"',
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris24_gpu_vertex_projection_poc.py"',
    'build verifier promotion')

# Include the generated csproj change in source-tree identity and require a platform bundle
# before compiling a candidate that is meant to exercise the GPU path.
source_hash_old = '''      Source/AERISFlightControl/Core/AERISBootstrap.cs \\
      build_ubuntu.sh
  } | sha256sum | awk '{print $1}'
)'''
source_hash_new = '''      Source/AERISFlightControl/Core/AERISBootstrap.cs \\
      Source/AERISFlightControl/AERISFlightControl.csproj \\
      build_ubuntu.sh
  } | sha256sum | awk '{print $1}'
)'''
if source_hash_old in b:
    b = b.replace(source_hash_old, source_hash_new, 1)
elif source_hash_new not in b:
    raise SystemExit("[AERIS24 GPU VERTEX] source-tree hash anchor mismatch")

identity_echo = '''echo "[AERIS23_CANDIDATE_SOURCE] candidate=$CANDIDATE_NAME; git=$SOURCE_GIT_SHA; source_tree_sha256=$SOURCE_TREE_SHA256"
'''
bundle_guard = identity_echo + r'''GPU_SHADER_DIR="$ROOT/GameData/AERISFlightControl/Shaders"
if test -f "$KSP/KSP_x64.exe"; then
  GPU_SHADER_BUNDLE_NAME="aeris_nd_gpu_vertex_projection_windows.bundle"
elif test -f "$KSP/KSP.x86_64"; then
  GPU_SHADER_BUNDLE_NAME="aeris_nd_gpu_vertex_projection_linux.bundle"
elif test -f "$GPU_SHADER_DIR/aeris_nd_gpu_vertex_projection_windows.bundle"; then
  GPU_SHADER_BUNDLE_NAME="aeris_nd_gpu_vertex_projection_windows.bundle"
else
  GPU_SHADER_BUNDLE_NAME="aeris_nd_gpu_vertex_projection_linux.bundle"
fi
GPU_SHADER_BUNDLE="$GPU_SHADER_DIR/$GPU_SHADER_BUNDLE_NAME"
test -f "$GPU_SHADER_BUNDLE" || {
  echo "[AERIS24 GPU VERTEX] ERROR: required shader bundle missing: $GPU_SHADER_BUNDLE" >&2
  echo "Run Tools/build_aeris24_gpu_shader_bundle.sh before the normal AERIS build." >&2
  exit 1
}
GPU_SHADER_BUNDLE_SHA256="$(sha256sum "$GPU_SHADER_BUNDLE" | awk '{print $1}')"
echo "[AERIS24_GPU_VERTEX_BUNDLE] name=$GPU_SHADER_BUNDLE_NAME; sha256=$GPU_SHADER_BUNDLE_SHA256"
'''
if "GPU_SHADER_BUNDLE_SHA256=" not in b:
    if b.count(identity_echo) != 1:
        raise SystemExit("[AERIS24 GPU VERTEX] build bundle guard anchor mismatch")
    b = b.replace(identity_echo, bundle_guard, 1)

old_identity = '''printf 'candidate=%s\\ngit=%s\\nsource_tree_sha256=%s\\nbuilt_dll_sha256=%s\\n' \\
  "$CANDIDATE_NAME" "$SOURCE_GIT_SHA" "$SOURCE_TREE_SHA256" "$BUILT_DLL_SHA256" \\
  > "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"'''
new_identity = '''printf 'candidate=%s\\ngit=%s\\nsource_tree_sha256=%s\\nbuilt_dll_sha256=%s\\ngpu_shader_bundle=%s\\ngpu_shader_bundle_sha256=%s\\n' \\
  "$CANDIDATE_NAME" "$SOURCE_GIT_SHA" "$SOURCE_TREE_SHA256" "$BUILT_DLL_SHA256" \\
  "$GPU_SHADER_BUNDLE_NAME" "$GPU_SHADER_BUNDLE_SHA256" \\
  > "$ROOT/GameData/AERISFlightControl/AERISCandidateBuildIdentity.txt"'''
if old_identity in b:
    b = b.replace(old_identity, new_identity, 1)
elif new_identity not in b:
    raise SystemExit("[AERIS24 GPU VERTEX] build identity bundle anchor mismatch")

build.write_text(b)

verify = ROOT / "Tools/verify_aeris24_gpu_vertex_projection_poc.py"
if not verify.is_file():
    raise SystemExit("[AERIS24 GPU VERTEX] verifier missing: " + str(verify))
subprocess.run([sys.executable, str(verify)], cwd=str(ROOT), check=True)

print("[AERIS24 GPU VERTEX] candidate=AERIS24_GPU_VERTEX_PROJECTION_POC")
print("[AERIS24 GPU VERTEX] GPU exact projection patch applied over runtime-tested PENICILLIN lineage")
print("[AERIS24 GPU VERTEX] CPU ProjectMesh/Affine/Stagger path retained as fail-closed fallback")
print("Next: Tools/build_aeris24_gpu_shader_bundle.sh windows  # current Proton/WindowsPlayer KSP")
print("Then: PYTHONDONTWRITEBYTECODE=1 python3 Tools/run_v01800_operation_health_pass3_prebuild.py")
print("Then: ./build_ubuntu.sh <KSP_PATH>")
