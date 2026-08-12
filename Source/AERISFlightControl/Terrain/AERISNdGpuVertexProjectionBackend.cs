using System;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS24 GPU Vertex Projection PoC.
    //
    // The shader receives immutable unit-sphere geographic XYZ through TEXCOORD1.
    // Every authoritative 10 Hz BACK render updates only the map projection uniforms;
    // the GPU then executes the same spherical projection law as AERISNdMapProjection.
    // This backend owns presentation only.  It has no flight-control, runway-certification,
    // LAND, safety, or terrain-content authority.  Failure is fail-closed to the existing
    // CPU exact projection/upload path.
    internal sealed class AERISNdGpuVertexProjectionBackend : IDisposable
    {
        const string ShaderName = "AERIS/ND/ExactVertexProjection";
        const string BundleWindows = "aeris_nd_gpu_vertex_projection_windows.bundle";
        const string BundleLinux = "aeris_nd_gpu_vertex_projection_linux.bundle";

        static readonly int CenterId = Shader.PropertyToID("_AerisCenter");
        static readonly int EastId = Shader.PropertyToID("_AerisEast");
        static readonly int NorthId = Shader.PropertyToID("_AerisNorth");
        static readonly int RadiusId = Shader.PropertyToID("_AerisRadiusMeters");
        static readonly int HorizontalId = Shader.PropertyToID("_AerisHorizontalMeters");
        static readonly int VerticalId = Shader.PropertyToID("_AerisVerticalMeters");
        static readonly int AnchorId = Shader.PropertyToID("_AerisAnchorRenderV");
        static readonly int OrientationSignId = Shader.PropertyToID("_AerisOrientationSign");
        static readonly int ColourId = Shader.PropertyToID("_Color");

        AssetBundle bundle;
        Shader shader;
        Material terrainMaterial;
        Material contourMaterial;
        Material coastlineMaterial;
        bool attempted;
        bool disabled;
        bool failureLogged;
        string failure = string.Empty;
        string bundlePath = string.Empty;

        internal bool Active
        {
            get
            {
                return attempted && !disabled && shader != null && shader.isSupported &&
                    terrainMaterial != null && contourMaterial != null &&
                    coastlineMaterial != null;
            }
        }

        internal string Failure { get { return failure; } }
        internal string BundlePath { get { return bundlePath; } }
        internal Material TerrainMaterial { get { return terrainMaterial; } }
        internal Material ContourMaterial { get { return contourMaterial; } }
        internal Material CoastlineMaterial { get { return coastlineMaterial; } }

        internal bool TryEnsureLoaded()
        {
            if (attempted) return Active;
            attempted = true;
            try
            {
                if (SystemInfo.graphicsShaderLevel < 30)
                    return Fail("graphicsShaderLevel<30");

                string fileName;
                if (Application.platform == RuntimePlatform.WindowsPlayer)
                    fileName = BundleWindows;
                else if (Application.platform == RuntimePlatform.LinuxPlayer)
                    fileName = BundleLinux;
                else
                    return Fail("unsupported runtime platform=" + Application.platform);

                string root = KSPUtil.ApplicationRootPath;
                if (string.IsNullOrEmpty(root))
                    return Fail("KSP application root unavailable");
                bundlePath = Path.Combine(root, "GameData", "AERISFlightControl",
                    "Shaders", fileName);
                if (!File.Exists(bundlePath))
                    return Fail("shader bundle missing: " + bundlePath);

                bundle = AssetBundle.LoadFromFile(bundlePath);
                if (bundle == null)
                    return Fail("AssetBundle.LoadFromFile returned null");

                Shader[] shaders = bundle.LoadAllAssets<Shader>();
                if (shaders != null)
                    for (int i = 0; i < shaders.Length; i++)
                        if (shaders[i] != null &&
                            string.Equals(shaders[i].name, ShaderName,
                                StringComparison.Ordinal))
                        {
                            shader = shaders[i];
                            break;
                        }
                if (shader == null)
                    return Fail("shader asset not found: " + ShaderName);
                if (!shader.isSupported)
                    return Fail("shader unsupported by active graphics backend");

                terrainMaterial = CreateMaterial("AERIS_ND_GPU_EXACT_TERRAIN");
                contourMaterial = CreateMaterial("AERIS_ND_GPU_EXACT_CONTOUR");
                coastlineMaterial = CreateMaterial("AERIS_ND_GPU_EXACT_COASTLINE");
                if (terrainMaterial == null || contourMaterial == null ||
                    coastlineMaterial == null)
                    return Fail("GPU exact projection material creation failed");

                failure = string.Empty;
                AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] ACTIVE; shader=" +
                    ShaderName + "; bundle=" + fileName + "; graphics=" +
                    SystemInfo.graphicsDeviceType + "/" +
                    SystemInfo.graphicsDeviceName + ".");
                return true;
            }
            catch (Exception ex)
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        Material CreateMaterial(string name)
        {
            if (shader == null) return null;
            var material = new Material(shader);
            material.name = name;
            material.hideFlags = HideFlags.HideAndDontSave;
            material.color = Color.white;
            return material;
        }

        internal void ConfigureProjection(AERISNdMapProjection projection,
            Color contourColour)
        {
            if (!Active) return;
            Vector4 center = new Vector4((float)projection.CenterX,
                (float)projection.CenterY, (float)projection.CenterZ, 0f);
            Vector4 east = new Vector4((float)projection.EastX,
                (float)projection.EastY, 0f, 0f);
            Vector4 north = new Vector4((float)projection.NorthX,
                (float)projection.NorthY, (float)projection.NorthZ, 0f);
            float orientationSign = projection.Orientation ==
                AERISTerrainRenderTargetOrientation.Flipped ? -1f : 1f;

            ConfigureMaterial(terrainMaterial, projection, center, east, north,
                orientationSign, Color.white);
            ConfigureMaterial(contourMaterial, projection, center, east, north,
                orientationSign, contourColour);
            ConfigureMaterial(coastlineMaterial, projection, center, east, north,
                orientationSign, Color.white);
        }

        static void ConfigureMaterial(Material material,
            AERISNdMapProjection projection, Vector4 center, Vector4 east,
            Vector4 north, float orientationSign, Color colour)
        {
            if (material == null) return;
            material.SetVector(CenterId, center);
            material.SetVector(EastId, east);
            material.SetVector(NorthId, north);
            material.SetFloat(RadiusId, (float)projection.RadiusMeters);
            material.SetFloat(HorizontalId,
                (float)Math.Max(1.0, projection.HorizontalMeters));
            material.SetFloat(VerticalId,
                (float)Math.Max(1.0, projection.VerticalMeters));
            material.SetFloat(AnchorId, projection.AnchorRenderV);
            material.SetFloat(OrientationSignId, orientationSign);
            material.SetColor(ColourId, colour);
        }

        internal void DisableAndFallback(string reason)
        {
            if (disabled) return;
            disabled = true;
            failure = string.IsNullOrEmpty(reason) ?
                "GPU vertex projection disabled" : reason;
            LogFailureOnce();
        }

        bool Fail(string reason)
        {
            disabled = true;
            failure = reason ?? "GPU vertex projection unavailable";
            LogFailureOnce();
            return false;
        }

        void LogFailureOnce()
        {
            if (failureLogged) return;
            failureLogged = true;
            AERISLogger.Warn("[AERIS24_GPU_VERTEX_PROJECTION] CPU EXACT FALLBACK; reason=" +
                failure + ".");
        }

        public void Dispose()
        {
            DestroyMaterial(ref terrainMaterial);
            DestroyMaterial(ref contourMaterial);
            DestroyMaterial(ref coastlineMaterial);
            if (bundle != null)
            {
                try { bundle.Unload(false); } catch { }
                bundle = null;
            }
            shader = null;
            disabled = true;
        }

        static void DestroyMaterial(ref Material material)
        {
            if (material == null) return;
            Material value = material;
            material = null;
            try
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(value);
                else UnityEngine.Object.DestroyImmediate(value);
            }
            catch { }
        }
    }
}
