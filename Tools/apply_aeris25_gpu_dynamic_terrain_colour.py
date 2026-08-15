#!/usr/bin/env python3
from pathlib import Path
import subprocess
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[1]
CANDIDATE = "AERIS25_GPU_DYNAMIC_TERRAIN_COLOUR"
OLD_OH = "EPI" + "NEPHRINE"
NEW_OH = "ATRO" + "PINE"
OLD_REVISION = "OH_PHASE3_007"
REVISION = "OH_PHASE4_001"
OLD_BUNDLE_WINDOWS = "aeris_nd_gpu_vertex_projection_windows.bundle"
OLD_BUNDLE_LINUX = "aeris_nd_gpu_vertex_projection_linux.bundle"
BUNDLE_WINDOWS = "aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle"
BUNDLE_LINUX = "aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle"


def run(label, path):
    if not path.is_file():
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR] missing step: %s (%s)" % (label, path))
    print("\n[AERIS25 GPU DYNAMIC COLOUR] " + label)
    subprocess.run([sys.executable, str(path)], cwd=str(ROOT), check=True)


def replace_once(text, old, new, label):
    count = text.count(old)
    if count == 1:
        return text.replace(old, new, 1)
    if count == 0 and new in text:
        return text
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR] %s anchor mismatch old=%d" % (label, count))


def replace_active_line(text, old, new, label):
    old_target = old.strip()
    new_target = new.strip()
    lines = text.splitlines(True)
    old_hits = [i for i, line in enumerate(lines) if line.strip() == old_target]
    new_hits = [i for i, line in enumerate(lines) if line.strip() == new_target]
    if len(old_hits) == 1 and len(new_hits) == 0:
        i = old_hits[0]
        ending = "\n" if lines[i].endswith("\n") else ""
        indent = lines[i][:len(lines[i]) - len(lines[i].lstrip())]
        lines[i] = indent + new_target + ending
        return "".join(lines)
    if len(old_hits) == 0 and len(new_hits) == 1:
        return text
    raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR] %s active-line mismatch old=%d new=%d" %
                     (label, len(old_hits), len(new_hits)))


parent = ROOT / "Tools/apply_aeris24_gpu_vertex_projection_ready.py"
run("reconstruct/revalidate accepted AERIS24 rev007 parent", parent)

renderer = ROOT / "Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRenderer.cs"
backend = ROOT / "Source/AERISFlightControl/Terrain/AERISNdGpuVertexProjectionBackend.cs"
shader = ROOT / "GpuAssets/Assets/AERISNdExactVertexProjection.shader"
builder = ROOT / "GpuAssets/Assets/Editor/BuildAERISGpuAssets.cs"
monitor = ROOT / "Source/AERISFlightControl/Performance/AERISOperationHealthPenicillin.cs"
config = ROOT / "GameData/AERISFlightControl/Config/AERISOperationHealth.cfg"
build = ROOT / "build_ubuntu.sh"
for path in (renderer, backend, shader, builder, monitor, config, build):
    if not path.is_file():
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR] required file missing: " + str(path))

s = shader.read_text()
if "_AerisTerrainSemanticMode" not in s:
    s = replace_once(s,
'''        _Color ("Tint", Color) = (1,1,1,1)\n''',
'''        _Color ("Tint", Color) = (1,1,1,1)\n        [HideInInspector] _AerisTerrainSemanticMode ("AERIS terrain semantic mode", Float) = 0\n        [HideInInspector] _AerisTerrainDisplayMode ("AERIS terrain display mode", Float) = 0\n        [HideInInspector] _AerisTerrainPreset ("AERIS terrain colour preset", Float) = 0\n        [HideInInspector] _AerisAircraftAltitudeMeters ("AERIS aircraft altitude", Float) = 0\n''', 'shader hidden dynamic properties')
    s = replace_once(s,
'''                fixed4 color : COLOR;\n                float3 geographicUnit : TEXCOORD1;\n''',
'''                fixed4 color : COLOR;\n                float3 geographicUnit : TEXCOORD1;\n                // x=elevation metres, y=raw shade byte 0..255, z=1 land / 0 water.\n                float3 terrainSemantic : TEXCOORD2;\n''', 'shader semantic input')
    s = replace_once(s,
'''            float _AerisOrientationSign;\n\n            float AerisAngularScale''',
'''            float _AerisOrientationSign;\n            float _AerisTerrainSemanticMode;\n            float _AerisTerrainDisplayMode;\n            float _AerisTerrainPreset;\n            float _AerisAircraftAltitudeMeters;\n\n            float4 AerisByteColour(float r, float g, float b)\n            {\n                return float4(r, g, b, 255.0) / 255.0;\n            }\n\n            float4 AerisLerpByte(float4 a, float4 b, float t)\n            {\n                float4 raw = lerp(a, b, saturate(t));\n                return floor(raw * 255.0 + 0.5) / 255.0;\n            }\n\n            float4 AerisGradient(float t, float4 a, float4 b, float4 c,\n                float4 d, float4 e)\n            {\n                if (t <= 0.25) return AerisLerpByte(a, b, t * 4.0);\n                if (t <= 0.50) return AerisLerpByte(b, c, (t - 0.25) * 4.0);\n                if (t <= 0.75) return AerisLerpByte(c, d, (t - 0.50) * 4.0);\n                return AerisLerpByte(d, e, (t - 0.75) * 4.0);\n            }\n\n            float4 AerisRelativeColour(float clearance, int preset)\n            {\n                if (clearance <= 30.0)\n                {\n                    if (preset == 1) return AerisByteColour(190, 45, 210);\n                    return AerisByteColour(224, 31, 20);\n                }\n                if (clearance <= 300.0)\n                {\n                    if (preset == 2) return AerisByteColour(242, 235, 225);\n                    return AerisByteColour(235, 184, 20);\n                }\n                if (clearance <= 600.0)\n                {\n                    if (preset == 1) return AerisByteColour(35, 105, 210);\n                    if (preset == 3) return AerisByteColour(70, 235, 70);\n                    return AerisByteColour(51, 122, 41);\n                }\n                if (preset == 1) return AerisByteColour(15, 35, 75);\n                if (preset == 3) return AerisByteColour(12, 72, 24);\n                return AerisByteColour(26, 61, 31);\n            }\n\n            float4 AerisTopographicColour(float elevation, int preset)\n            {\n                float t = saturate((elevation + 500.0) / 12500.0);\n                if (preset == 1)\n                    return AerisGradient(t, AerisByteColour(25,55,105),\n                        AerisByteColour(45,110,175), AerisByteColour(225,175,70),\n                        AerisByteColour(150,105,85), AerisByteColour(245,245,245));\n                if (preset == 2)\n                    return AerisGradient(t, AerisByteColour(25,70,48),\n                        AerisByteColour(70,135,75), AerisByteColour(160,150,80),\n                        AerisByteColour(125,90,75), AerisByteColour(245,245,245));\n                if (preset == 3)\n                    return AerisGradient(t, AerisByteColour(5,35,15),\n                        AerisByteColour(40,150,40), AerisByteColour(255,220,40),\n                        AerisByteColour(160,70,30), AerisByteColour(255,255,255));\n                return AerisGradient(t, AerisByteColour(18,65,35),\n                    AerisByteColour(55,125,55), AerisByteColour(150,145,70),\n                    AerisByteColour(120,85,65), AerisByteColour(235,235,235));\n            }\n\n            float4 AerisApplyShade(float4 colour, float shadeByte, bool relativeMode)\n            {\n                float raw = clamp(shadeByte / 227.0, 0.82, 1.04);\n                float blend = relativeMode ? 0.30 : 0.55;\n                float factor = lerp(1.0, raw, blend);\n                factor = relativeMode ? clamp(factor, 0.94, 1.02) :\n                    clamp(factor, 0.88, 1.035);\n                float3 bytes = floor(colour.rgb * 255.0 * factor + 0.5);\n                return float4(clamp(bytes, 0.0, 255.0) / 255.0, colour.a);\n            }\n\n            float4 AerisTerrainColour(float3 semantic)\n            {\n                int preset = (int)floor(_AerisTerrainPreset + 0.5);\n                if (semantic.z < 0.5)\n                {\n                    if (preset == 1) return AerisByteColour(0, 20, 70);\n                    return AerisByteColour(8, 52, 118);\n                }\n                bool relativeMode = _AerisTerrainDisplayMode > 0.5;\n                float4 baseColour = relativeMode ?\n                    AerisRelativeColour(_AerisAircraftAltitudeMeters - semantic.x, preset) :\n                    AerisTopographicColour(semantic.x, preset);\n                return AerisApplyShade(baseColour, semantic.y, relativeMode);\n            }\n\n            float AerisAngularScale''', 'shader dynamic colour helpers')
    s = replace_once(s,
'''                output.color = input.color * _Color;\n''',
'''                output.color = _AerisTerrainSemanticMode > 0.5 ?\n                    AerisTerrainColour(input.terrainSemantic) : input.color * _Color;\n''', 'shader dynamic output')
    shader.write_text(s)
else:
    print("[AERIS25 GPU DYNAMIC COLOUR] shader patch already applied")

b = backend.read_text()
if "DynamicTerrainSemanticModeId" not in b:
    b = replace_once(b, 'const string BundleWindows = "' + OLD_BUNDLE_WINDOWS + '";',
                     'const string BundleWindows = "' + BUNDLE_WINDOWS + '";',
                     'backend Windows bundle identity')
    b = replace_once(b, 'const string BundleLinux = "' + OLD_BUNDLE_LINUX + '";',
                     'const string BundleLinux = "' + BUNDLE_LINUX + '";',
                     'backend Linux bundle identity')
    b = replace_once(b,
'''        static readonly int ColourId = Shader.PropertyToID("_Color");\n''',
'''        static readonly int ColourId = Shader.PropertyToID("_Color");\n        static readonly int DynamicTerrainSemanticModeId = Shader.PropertyToID("_AerisTerrainSemanticMode");\n        static readonly int DynamicTerrainDisplayModeId = Shader.PropertyToID("_AerisTerrainDisplayMode");\n        static readonly int DynamicTerrainPresetId = Shader.PropertyToID("_AerisTerrainPreset");\n        static readonly int DynamicTerrainAircraftAltitudeId = Shader.PropertyToID("_AerisAircraftAltitudeMeters");\n''', 'backend dynamic property IDs')
    b = replace_once(b,
'''        internal string Failure { get { return failure; } }\n''',
'''        internal string Failure { get { return failure; } }\n        internal bool DynamicTerrainColourActive { get { return Active; } }\n''', 'backend dynamic active property')
    b = replace_once(b,
'''                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; shader=" +\n''',
'''                AERISLogger.Info("[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; shader=" +\n''', 'backend active log identity')
    b = replace_once(b,
'''        internal void ConfigureProjection(AERISNdMapProjection projection,\n            Color contourColour)\n''',
'''        internal void ConfigureProjection(AERISNdMapProjection projection,\n            Color contourColour, AERISTerrainDisplayMode mode,\n            AERISTerrainColourPreset preset, float aircraftAltitudeAslMeters)\n''', 'backend ConfigureProjection signature')
    b = replace_once(b,
'''            ConfigureMaterial(terrainMaterial, projection, center, east, north,\n                orientationSign, Color.white);\n            ConfigureMaterial(contourMaterial, projection, center, east, north,\n                orientationSign, contourColour);\n            ConfigureMaterial(coastlineMaterial, projection, center, east, north,\n                orientationSign, Color.white);\n''',
'''            ConfigureMaterial(terrainMaterial, projection, center, east, north,\n                orientationSign, Color.white);\n            ConfigureMaterial(contourMaterial, projection, center, east, north,\n                orientationSign, contourColour);\n            ConfigureMaterial(coastlineMaterial, projection, center, east, north,\n                orientationSign, Color.white);\n            ConfigureDynamicTerrainColour(terrainMaterial, true, mode, preset, aircraftAltitudeAslMeters);\n            ConfigureDynamicTerrainColour(contourMaterial, false, mode, preset, aircraftAltitudeAslMeters);\n            ConfigureDynamicTerrainColour(coastlineMaterial, false, mode, preset, aircraftAltitudeAslMeters);\n''', 'backend dynamic material configuration')
    insert = '''        internal void DisableAndFallback(string reason)\n'''
    helper = r'''        static void ConfigureDynamicTerrainColour(Material material,
            bool terrainSemanticMode, AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset, float aircraftAltitudeAslMeters)
        {
            if (material == null) return;
            int presetCode = 0;
            switch (preset)
            {
                case AERISTerrainColourPreset.RedGreenAssist: presetCode = 1; break;
                case AERISTerrainColourPreset.BlueYellowAssist: presetCode = 2; break;
                case AERISTerrainColourPreset.HighContrast: presetCode = 3; break;
            }
            bool relativeMode = mode == AERISTerrainDisplayMode.Relative;
            float quantizedAltitude = relativeMode ?
                Mathf.RoundToInt(aircraftAltitudeAslMeters / 5f) * 5f : aircraftAltitudeAslMeters;
            material.SetFloat(DynamicTerrainSemanticModeId, terrainSemanticMode ? 1f : 0f);
            material.SetFloat(DynamicTerrainDisplayModeId, relativeMode ? 1f : 0f);
            material.SetFloat(DynamicTerrainPresetId, presetCode);
            material.SetFloat(DynamicTerrainAircraftAltitudeId, quantizedAltitude);
        }

'''
    if b.count(insert) != 1:
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR] backend DisableAndFallback anchor mismatch")
    b = b.replace(insert, helper + insert, 1)
    b = b.replace('[AERIS24_GPU_VERTEX_PROJECTION] CPU EXACT FALLBACK',
                  '[AERIS25_GPU_DYNAMIC_COLOUR] CPU EXACT FALLBACK')
    backend.write_text(b)
else:
    print("[AERIS25 GPU DYNAMIC COLOUR] backend patch already applied")

r = renderer.read_text()
if "oh_gpu_dynamic_colour=" not in r:
    r = replace_once(r,
'''            internal bool GpuVertexProjectionAttributesReady;\n            internal bool GpuVertexProjectionRejected;\n            internal float[] LandElevationMeters;''',
'''            internal bool GpuVertexProjectionAttributesReady;\n            internal bool GpuVertexProjectionRejected;\n            internal bool GpuDynamicColourAttributesReady;\n            internal bool GpuDynamicColourRejected;\n            internal float[] LandElevationMeters;''', 'renderer Entry dynamic state')
    r = replace_once(r,
'''        readonly List<Vector3> gpuVertexGeographicScratch = new List<Vector3>(4096);\n        bool gpuVertexProjectionBackFailure;''',
'''        readonly List<Vector3> gpuVertexGeographicScratch = new List<Vector3>(4096);\n        readonly List<Vector3> gpuDynamicTerrainSemanticScratch = new List<Vector3>(4096);\n        bool gpuVertexProjectionBackFailure;''', 'renderer dynamic semantic scratch')
    r = replace_once(r,
'''        long operationHealthGpuVertexBackFrames;\n        long operationHealthGpuVertexDraws;\n        long operationHealthLoadingBackdropFrames;''',
'''        long operationHealthGpuVertexBackFrames;\n        long operationHealthGpuVertexDraws;\n        long operationHealthGpuDynamicSemanticUploads;\n        long operationHealthGpuDynamicSemanticFailures;\n        long operationHealthGpuDynamicCpuColourBypasses;\n        long operationHealthLoadingBackdropFrames;''', 'renderer dynamic telemetry fields')
    helper_anchor = '''        bool EnsureGpuVertexProjectionAttributes(Entry entry)\n'''
    if r.count(helper_anchor) != 1:
        raise SystemExit("[AERIS25 GPU DYNAMIC COLOUR] GPU attribute helper anchor mismatch")
    helper = r'''        bool EnsureGpuDynamicTerrainColourAttributes(Entry entry)
        {
            if (entry == null || entry.PackedTerrainMesh == null || entry.GpuDynamicColourRejected) return false;
            if (entry.GpuDynamicColourAttributesReady) return true;
            try
            {
                int vertexCount = entry.PackedTerrainMesh.vertexCount;
                if (vertexCount <= 0 || entry.PackedTerrainColours == null ||
                    entry.PackedTerrainColours.Length != vertexCount)
                    throw new InvalidOperationException("packed terrain semantic vertex mismatch");
                gpuDynamicTerrainSemanticScratch.Clear();
                if (gpuDynamicTerrainSemanticScratch.Capacity < vertexCount)
                    gpuDynamicTerrainSemanticScratch.Capacity = vertexCount;
                for (int i = 0; i < vertexCount; i++)
                    gpuDynamicTerrainSemanticScratch.Add(new Vector3(0f, 255f, 0f));
                int landCount = Math.Min(entry.PackedLandCount,
                    entry.LandElevationMeters == null ? 0 : entry.LandElevationMeters.Length);
                landCount = Math.Min(landCount, entry.LandShade == null ? 0 : entry.LandShade.Length);
                for (int i = 0; i < landCount; i++)
                {
                    int target = entry.PackedLandOffset + i;
                    if (target < 0 || target >= vertexCount) continue;
                    gpuDynamicTerrainSemanticScratch[target] = new Vector3(
                        entry.LandElevationMeters[i], entry.LandShade[i], 1f);
                }
                int coastalLandCount = Math.Min(entry.PackedCoastalLandCount,
                    entry.CoastalLandCorrectionElevationMeters == null ? 0 :
                    entry.CoastalLandCorrectionElevationMeters.Length);
                for (int i = 0; i < coastalLandCount; i++)
                {
                    int target = entry.PackedCoastalLandOffset + i;
                    if (target < 0 || target >= vertexCount) continue;
                    byte shade = entry.CoastalLandCorrectionShade != null &&
                        i < entry.CoastalLandCorrectionShade.Length ?
                        entry.CoastalLandCorrectionShade[i] : (byte)255;
                    gpuDynamicTerrainSemanticScratch[target] = new Vector3(
                        entry.CoastalLandCorrectionElevationMeters[i], shade, 1f);
                }
                entry.PackedTerrainMesh.SetUVs(2, gpuDynamicTerrainSemanticScratch);
                entry.GpuDynamicColourAttributesReady = true;
                operationHealthGpuDynamicSemanticUploads++;
                return true;
            }
            catch (Exception ex)
            {
                entry.GpuDynamicColourRejected = true;
                operationHealthGpuDynamicSemanticFailures++;
                AERISLogger.Warn("[AERIS25_GPU_DYNAMIC_COLOUR] Entry CPU fallback; key=" +
                    (entry.CacheKey ?? "NONE") + "; reason=" + ex.GetType().Name + ": " + ex.Message + ".");
                return false;
            }
        }

'''
    r = r.replace(helper_anchor, helper + helper_anchor, 1)
    r = replace_once(r,
'''                entry.GpuVertexProjectionAttributesReady = true;\n                return true;\n''',
'''                if (!EnsureGpuDynamicTerrainColourAttributes(entry))\n                {\n                    entry.GpuVertexProjectionRejected = true;\n                    operationHealthGpuVertexAttributeFailures++;\n                    return false;\n                }\n                entry.GpuVertexProjectionAttributesReady = true;\n                return true;\n''', 'renderer semantic gate inside GPU projection readiness')
    r = replace_once(r,
'''                    gpuVertexProjection.ConfigureProjection(projection,\n                        ResolveContourColour(settings == null ?\n                            AERISTerrainColourPreset.Standard :\n                            settings.TerrainColourPreset));\n''',
'''                    AERISTerrainColourPreset gpuPreset = settings == null ?\n                        AERISTerrainColourPreset.Standard : settings.TerrainColourPreset;\n                    gpuVertexProjection.ConfigureProjection(projection,\n                        ResolveContourColour(gpuPreset), effectiveMode, gpuPreset,\n                        (float)vessel.altitude);\n''', 'renderer dynamic uniforms on BACK preflight')
    draw_start = r.index('        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,')
    draw_end = r.index('        static void EnsurePackedTerrainColours(Entry entry,', draw_start)
    dynamic_draw = r'''        bool DrawEntry(Entry entry, Matrix4x4 mapMatrix, bool drawContours,
            AERISTerrainDisplayMode mode, AERISTerrainColourPreset preset,
            float aircraftAltitudeAslMeters)
        {
            if (entry == null || entry.PackedTerrainMesh == null) return false;
            bool gpuEntry = gpuVertexProjection.Active &&
                entry.GpuVertexProjectionAttributesReady && entry.GpuDynamicColourAttributesReady &&
                !entry.GpuVertexProjectionRejected && !entry.GpuDynamicColourRejected;
            if (!gpuEntry)
                EnsurePackedTerrainColours(entry, mode, preset, aircraftAltitudeAslMeters);
            else
                operationHealthGpuDynamicCpuColourBypasses++;
            Material terrainDrawMaterial = gpuEntry ? gpuVertexProjection.TerrainMaterial : terrainMaterial;
            Material contourDrawMaterial = gpuEntry ? gpuVertexProjection.ContourMaterial : contourMaterial;
            Material coastlineDrawMaterial = gpuEntry ? gpuVertexProjection.CoastlineMaterial : coastlineMaterial;
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
    r = r[:draw_start] + dynamic_draw + r[draw_end:]
    r = replace_once(r,
'''                "; oh_gpu_vertex_draws=" + operationHealthGpuVertexDraws +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
'''                "; oh_gpu_vertex_draws=" + operationHealthGpuVertexDraws +\n                "; oh_gpu_dynamic_colour=" +\n                    (gpuVertexProjection.DynamicTerrainColourActive ? "ACTIVE" : "CPU_FALLBACK") +\n                "; oh_gpu_dynamic_semantic_upload=" + operationHealthGpuDynamicSemanticUploads +\n                "; oh_gpu_dynamic_semantic_fail=" + operationHealthGpuDynamicSemanticFailures +\n                "; oh_gpu_dynamic_cpu_colour_bypass=" + operationHealthGpuDynamicCpuColourBypasses +\n                "; oh_loading_backdrop=" + operationHealthLoadingBackdropFrames +''',
        'renderer dynamic telemetry publication')
    r = replace_once(r,
'''            gpuVertexGeographicScratch.Clear();\n            gpuVertexProjection.ReleaseForSuspension();\n''',
'''            gpuVertexGeographicScratch.Clear();\n            gpuDynamicTerrainSemanticScratch.Clear();\n            gpuVertexProjection.ReleaseForSuspension();\n''', 'renderer semantic scratch suspension release')
    r = replace_once(r,
'''                "; colourSource=EXPLICIT_VERTEX" +\n''',
'''                "; colourSource=" + (gpuVertexProjection.DynamicTerrainColourActive ?\n                    "GPU_DYNAMIC_SEMANTIC" : "EXPLICIT_VERTEX") +\n''', 'renderer alignment colour-source telemetry')
    renderer.write_text(r)
else:
    print("[AERIS25 GPU DYNAMIC COLOUR] renderer patch already applied")

e = builder.read_text()
if BUNDLE_WINDOWS not in e:
    e = replace_once(e, '"' + OLD_BUNDLE_WINDOWS + '",', '"' + BUNDLE_WINDOWS + '",',
                     'builder Windows installed bundle')
    e = replace_once(e, '"' + OLD_BUNDLE_LINUX + '",', '"' + BUNDLE_LINUX + '",',
                     'builder Linux installed bundle')
    e = e.replace('[AERIS24 GPU VERTEX]', '[AERIS25 GPU DYNAMIC COLOUR]')
    builder.write_text(e)

m = monitor.read_text()
m = replace_once(m, 'internal const string Codename = "' + OLD_OH + '";',
                 'internal const string Codename = "' + NEW_OH + '";', 'OH codename')
m = replace_once(m, 'internal const string Revision = "' + OLD_REVISION + '";',
                 'internal const string Revision = "' + REVISION + '";', 'OH revision')
m = replace_once(m, 'internal const string Candidate = "AERIS24_GPU_VERTEX_PROJECTION_POC";',
                 'internal const string Candidate = "' + CANDIDATE + '";', 'OH candidate')
monitor.write_text(m)

c = config.read_text()
c = replace_once(c, '    codename = ' + OLD_OH, '    codename = ' + NEW_OH, 'config codename')
config.write_text(c)

bu = build.read_text()
bu = replace_active_line(bu,
    'CANDIDATE_NAME="AERIS24_GPU_VERTEX_PROJECTION_POC"',
    'CANDIDATE_NAME="' + CANDIDATE + '"', 'build candidate')
bu = replace_active_line(bu,
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris24_gpu_vertex_projection_poc.py"',
    'PYTHONDONTWRITEBYTECODE=1 python3 "$ROOT/Tools/verify_aeris25_gpu_dynamic_terrain_colour.py"',
    'build verifier')
bu = replace_active_line(bu,
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 OPERATION HEALTH PHASE 3 ' + OLD_OH + ' GPU VERTEX PROJECTION"',
    'DISPLAY="AERIS Flight Control v$SEMVER DEV CP3.75 AERIS25 OPERATION HEALTH PHASE 4 ' + NEW_OH + ' GPU DYNAMIC TERRAIN COLOUR"',
    'build display identity')
bu = replace_active_line(bu,
    'internal const string UiCheckpoint = "DEV CP3.75 — OPERATION HEALTH PHASE 3 ' + OLD_OH + ' — GPU VERTEX PROJECTION";',
    'internal const string UiCheckpoint = "DEV CP3.75 — AERIS25 — OPERATION HEALTH PHASE 4 ' + NEW_OH + ' — GPU DYNAMIC TERRAIN COLOUR";',
    'in-game checkpoint identity')
bu = bu.replace(OLD_BUNDLE_WINDOWS, BUNDLE_WINDOWS)
bu = bu.replace(OLD_BUNDLE_LINUX, BUNDLE_LINUX)
bu = replace_once(bu, r"r'\1" + OLD_OH + "'", r"r'\1" + NEW_OH + "'",
                  'installed config codename promotion')
bu = bu.replace('Operation Health codename key missing during Phase 3 install promotion',
                'Operation Health codename key missing during Phase 4 install promotion')
bu = bu.replace('[AERIS24 GPU VERTEX]', '[AERIS25 GPU DYNAMIC COLOUR]')
build.write_text(bu)

verifier = ROOT / "Tools/verify_aeris25_gpu_dynamic_terrain_colour.py"
if verifier.is_file():
    run("verify AERIS25 GPU Dynamic Terrain Colour", verifier)

print("\n[AERIS25 GPU DYNAMIC COLOUR] candidate=" + CANDIDATE)
print("[AERIS25 GPU DYNAMIC COLOUR] codename=" + NEW_OH)
print("[AERIS25 GPU DYNAMIC COLOUR] revision=" + REVISION)
print("[AERIS25 GPU DYNAMIC COLOUR] CPU exact palette/shade path retained fail-closed")
print("[AERIS25 GPU DYNAMIC COLOUR] packed terrain semantics upload once; dynamic palette/REL altitude via uniforms")
print("Next: Tools/build_aeris25_gpu_shader_bundle.sh windows")
print("Then: ./build_ubuntu.sh <KSP_PATH>")
