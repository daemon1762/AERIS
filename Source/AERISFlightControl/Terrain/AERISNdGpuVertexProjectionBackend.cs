using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // AERIS24 GPU Vertex Projection PoC.
    //
    // The shader receives immutable unit-sphere geographic XYZ through TEXCOORD1.
    // Every authoritative 10 Hz BACK render updates only the map projection uniforms;
    // the GPU then executes the same spherical projection law as AERISNdMapProjection.
    // This backend owns presentation only. It has no flight-control, runway-certification,
    // LAND, safety, or terrain-content authority. Failure is fail-closed to the existing
    // CPU exact projection/upload path.
    internal sealed class AERISNdGpuVertexProjectionBackend : IDisposable
    {
        const string ShaderName = "AERIS/ND/ExactVertexProjection";
        const string BundleWindows = "aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle";
        const string BundleLinux = "aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle";
        const string ProbeWindows = "aeris25_gpu_dynamic_colour_probe_windows.bundle";
        const string ProbeLinux = "aeris25_gpu_dynamic_colour_probe_linux.bundle";
        const string ProbeMarker = "AERIS24_GPU_BUNDLE_PROBE_V1";
        // Managed-memory recovery is a one-time compatibility path for Proton/Wine cases
        // where System.IO can read the package but Unity native LoadFromFile rejects the
        // translated path. Bound the allocation so a corrupted/replaced package cannot
        // create an unbounded main-thread allocation.
        const long MaximumManagedBundleBytes = 8L * 1024L * 1024L;

        static readonly int CenterId = Shader.PropertyToID("_AerisCenter");
        static readonly int EastId = Shader.PropertyToID("_AerisEast");
        static readonly int NorthId = Shader.PropertyToID("_AerisNorth");
        static readonly int RadiusId = Shader.PropertyToID("_AerisRadiusMeters");
        static readonly int HorizontalId = Shader.PropertyToID("_AerisHorizontalMeters");
        static readonly int VerticalId = Shader.PropertyToID("_AerisVerticalMeters");
        static readonly int AnchorId = Shader.PropertyToID("_AerisAnchorRenderV");
        static readonly int OrientationSignId = Shader.PropertyToID("_AerisOrientationSign");
        static readonly int ColourId = Shader.PropertyToID("_Color");
        static readonly int DynamicTerrainSemanticModeId = Shader.PropertyToID("_AerisTerrainSemanticMode");
        static readonly int DynamicTerrainDisplayModeId = Shader.PropertyToID("_AerisTerrainDisplayMode");
        static readonly int DynamicTerrainPresetId = Shader.PropertyToID("_AerisTerrainPreset");
        static readonly int DynamicTerrainAircraftAltitudeId = Shader.PropertyToID("_AerisAircraftAltitudeMeters");

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
        string bundleLoadMode = "NONE";
        AERISNdProjectionBackendMode requestedMode =
            AERISNdProjectionBackendMode.Automatic;
        bool viewportSuspendedResident;
        int activationCount;
        int residentSuspensionCount;

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
        internal bool DynamicTerrainColourActive { get { return Active; } }
        internal string BundlePath { get { return bundlePath; } }
        internal int ActivationCount { get { return activationCount; } }
        internal int ResidentSuspensionCount { get { return residentSuspensionCount; } }
        internal string RequestedModeName
        {
            get
            {
                switch (requestedMode)
                {
                    case AERISNdProjectionBackendMode.Cpu: return "CPU";
                    case AERISNdProjectionBackendMode.Gpu: return "GPU";
                    default: return "AUTO";
                }
            }
        }
        internal string EffectiveModeName
        {
            get
            {
                if (requestedMode == AERISNdProjectionBackendMode.Cpu)
                    return "CPU_EXACT";
                if (Active) return "GPU_ACTIVE";
                return attempted ? "CPU_FALLBACK" : "GPU_PENDING";
            }
        }
        internal Material TerrainMaterial { get { return terrainMaterial; } }
        internal Material ContourMaterial { get { return contourMaterial; } }
        internal Material CoastlineMaterial { get { return coastlineMaterial; } }

        internal void SetRequestedMode(AERISNdProjectionBackendMode mode)
        {
            if (mode == requestedMode) return;
            ReleaseForSuspension();
            requestedMode = mode;
            AERISLogger.Info("[AERIS24_ND_PROJECTION_BACKEND] requested=" +
                RequestedModeName + "; effective=" + EffectiveModeName + ".");
        }

        internal bool TryEnsureLoaded()
        {
            // Resume is intentionally allocation-free when visibility alone suspended ND.
            viewportSuspendedResident = false;
            // Explicit CPU is a hard no-touch rail: do not probe, read or invoke
            // AssetBundle APIs at all. AUTO/GPU retain the same fail-closed GPU attempt.
            if (requestedMode == AERISNdProjectionBackendMode.Cpu)
            {
                if (!attempted)
                {
                    attempted = true;
                    disabled = true;
                    failure = "CPU_EXACT_REQUESTED";
                    AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] SKIPPED; " +
                        "requested=CPU; effective=CPU_EXACT; AssetBundleInit=0.");
                }
                return false;
            }
            if (attempted) return Active;
            attempted = true;
            try
            {
                if (SystemInfo.graphicsShaderLevel < 30)
                    return Fail("graphicsShaderLevel<30");

                string fileName;
                string probeFileName;
                if (Application.platform == RuntimePlatform.WindowsPlayer)
                {
                    fileName = BundleWindows;
                    probeFileName = ProbeWindows;
                }
                else if (Application.platform == RuntimePlatform.LinuxPlayer)
                {
                    fileName = BundleLinux;
                    probeFileName = ProbeLinux;
                }
                else
                    return Fail("unsupported runtime platform=" + Application.platform);

                string root = KSPUtil.ApplicationRootPath;
                if (string.IsNullOrEmpty(root))
                    return Fail("KSP application root unavailable");
                string shaderDirectory = Path.Combine(root, "GameData",
                    "AERISFlightControl", "Shaders");
                RunContainerProbe(Path.Combine(shaderDirectory, probeFileName),
                    probeFileName);

                bundlePath = Path.Combine(shaderDirectory, fileName);
                string loadFailure;
                if (!TryLoadBundle(bundlePath, out bundle, out bundleLoadMode,
                        out loadFailure))
                    return Fail(loadFailure);

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
                activationCount++;
                AERISLogger.Info("[AERIS25_GPU_DYNAMIC_COLOUR] ACTIVE; requested=" +
                    RequestedModeName + "; effective=" + EffectiveModeName + "; shader=" +
                    ShaderName + "; bundle=" + fileName + "; load=" + bundleLoadMode +
                    "; unity=" + Application.unityVersion + "; platform=" +
                    Application.platform + "; graphics=" + SystemInfo.graphicsDeviceType +
                    "/" + SystemInfo.graphicsDeviceName + ".");
                return true;
            }
            catch (Exception ex)
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        void RunContainerProbe(string path, string fileName)
        {
            AssetBundle probe = null;
            string mode;
            string reason;
            try
            {
                if (!TryLoadBundle(path, out probe, out mode, out reason))
                {
                    AERISLogger.Warn("[AERIS24_GPU_BUNDLE_PROBE] FAIL; bundle=" +
                        fileName + "; reason=" + reason + "; unity=" +
                        Application.unityVersion + "; platform=" + Application.platform +
                        "; graphics=" + SystemInfo.graphicsDeviceType + ".");
                    return;
                }

                bool markerFound = false;
                TextAsset[] assets = probe.LoadAllAssets<TextAsset>();
                if (assets != null)
                    for (int i = 0; i < assets.Length; i++)
                        if (assets[i] != null && assets[i].text != null &&
                            assets[i].text.IndexOf(ProbeMarker,
                                StringComparison.Ordinal) >= 0)
                        {
                            markerFound = true;
                            break;
                        }
                if (!markerFound)
                {
                    AERISLogger.Warn("[AERIS24_GPU_BUNDLE_PROBE] FAIL; bundle=" +
                        fileName + "; reason=TextAsset marker missing; load=" + mode +
                        "; unity=" + Application.unityVersion + ".");
                    return;
                }

                long bytes = new FileInfo(path).Length;
                AERISLogger.Info("[AERIS24_GPU_BUNDLE_PROBE] PASS; bundle=" + fileName +
                    "; load=" + mode + "; bytes=" + bytes + "; unity=" +
                    Application.unityVersion + "; platform=" + Application.platform +
                    "; graphics=" + SystemInfo.graphicsDeviceType + ".");
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[AERIS24_GPU_BUNDLE_PROBE] FAIL; bundle=" + fileName +
                    "; reason=" + ex.GetType().Name + ": " + ex.Message +
                    "; unity=" + Application.unityVersion + ".");
            }
            finally
            {
                if (probe != null)
                {
                    try { probe.Unload(false); } catch { }
                }
            }
        }

        bool TryLoadBundle(string path, out AssetBundle loaded, out string mode,
            out string reason)
        {
            loaded = null;
            mode = "NONE";
            reason = string.Empty;
            if (!File.Exists(path))
            {
                reason = "bundle missing: " + path;
                return false;
            }

            var nativeLog = new NativeUnityLogCapture();
            try
            {
                loaded = AssetBundle.LoadFromFile(path);
                if (loaded != null)
                {
                    mode = "FILE";
                    return true;
                }

                var info = new FileInfo(path);
                long length = info.Exists ? info.Length : -1L;
                if (length <= 0L || length > MaximumManagedBundleBytes)
                {
                    reason = "AssetBundle.LoadFromFile returned null; managed recovery size rejected=" +
                        length + NativeDiagnosticSuffix(nativeLog);
                    return false;
                }
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes == null || bytes.LongLength != length)
                {
                    reason = "AssetBundle.LoadFromFile returned null; managed recovery read mismatch" +
                        NativeDiagnosticSuffix(nativeLog);
                    return false;
                }
                loaded = AssetBundle.LoadFromMemory(bytes);
                if (loaded == null)
                {
                    reason = "AssetBundle.LoadFromFile returned null; AssetBundle.LoadFromMemory returned null; bytes=" +
                        length + NativeDiagnosticSuffix(nativeLog);
                    return false;
                }
                mode = "MEMORY";
                return true;
            }
            finally
            {
                nativeLog.Dispose();
            }
        }

        static string NativeDiagnosticSuffix(NativeUnityLogCapture capture)
        {
            if (capture == null) return "; unityNative=NONE";
            string summary = capture.Summary;
            return "; unityNative=" + (string.IsNullOrEmpty(summary) ? "NONE" : summary);
        }

        sealed class NativeUnityLogCapture : IDisposable
        {
            const int MaximumMessages = 12;
            readonly List<string> messages = new List<string>(MaximumMessages);
            bool disposed;

            internal NativeUnityLogCapture()
            {
                Application.logMessageReceived += OnLog;
            }

            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (disposed || messages.Count >= MaximumMessages) return;
                if (type != LogType.Error && type != LogType.Exception &&
                    type != LogType.Assert && type != LogType.Warning) return;
                string text = condition ?? string.Empty;
                text = text.Replace('\r', ' ').Replace('\n', ' ').Replace(';', ',');
                if (text.Length > 400) text = text.Substring(0, 400);
                messages.Add(type + ":" + text);
            }

            internal string Summary
            {
                get
                {
                    return messages.Count == 0 ? string.Empty :
                        string.Join(" | ", messages.ToArray());
                }
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                Application.logMessageReceived -= OnLog;
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

        // Validate all three passes before any Entry writes into the BACK target. If a
        // driver/backend rejects the shader, this frame falls back to CPU exact before a
        // partially drawn BACK can become eligible for FRONT swap.
        internal bool ValidatePassesOrFallback()
        {
            if (!Active) return false;
            try
            {
                bool ok = terrainMaterial.SetPass(0) && contourMaterial.SetPass(0) &&
                    coastlineMaterial.SetPass(0);
                if (ok) return true;
                DisableAndFallback("custom vertex shader SetPass rejected");
                return false;
            }
            catch (Exception ex)
            {
                DisableAndFallback("SetPass " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        internal void ConfigureProjection(AERISNdMapProjection projection,
            Color contourColour, AERISTerrainDisplayMode mode,
            AERISTerrainColourPreset preset, float aircraftAltitudeAslMeters)
        {
            if (!Active) return;
            Vector4 center = new Vector4((float)projection.CenterX,
                (float)projection.CenterY, (float)projection.CenterZ, 0f);
            Vector4 east = new Vector4((float)projection.EastX,
                (float)projection.EastY, 0f, 0f);
            Vector4 north = new Vector4((float)projection.NorthX,
                (float)projection.NorthY, (float)projection.NorthZ, 0f);
            // Render-target orientation is resolved by the canonical ND projection.
            // The GPU backend consumes only the resolved sign and does not duplicate
            // AERISTerrainRenderTargetOrientation knowledge.
            float orientationSign = projection.RenderNorthSign;

            ConfigureMaterial(terrainMaterial, projection, center, east, north,
                orientationSign, Color.white);
            ConfigureMaterial(contourMaterial, projection, center, east, north,
                orientationSign, contourColour);
            ConfigureMaterial(coastlineMaterial, projection, center, east, north,
                orientationSign, Color.white);
            ConfigureDynamicTerrainColour(terrainMaterial, true, mode, preset, aircraftAltitudeAslMeters);
            ConfigureDynamicTerrainColour(contourMaterial, false, mode, preset, aircraftAltitudeAslMeters);
            ConfigureDynamicTerrainColour(coastlineMaterial, false, mode, preset, aircraftAltitudeAslMeters);
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

        internal void RetainForViewportSuspension()
        {
            if (viewportSuspendedResident) return;
            viewportSuspendedResident = true;
            if (!Active) return;
            residentSuspensionCount++;
            AERISLogger.Info("[AERIS24_GPU_VERTEX_PROJECTION] RESIDENT SUSPEND; requested=" +
                RequestedModeName + "; effective=" + EffectiveModeName +
                "; activation=" + activationCount + "; retained=" +
                residentSuspensionCount + ".");
        }

        static void ConfigureDynamicTerrainColour(Material material,
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
            AERISLogger.Warn("[AERIS25_GPU_DYNAMIC_COLOUR] CPU EXACT FALLBACK; requested=" +
                RequestedModeName + "; effective=" + EffectiveModeName + "; reason=" +
                failure + "; unity=" + Application.unityVersion + "; platform=" +
                Application.platform + "; graphics=" + SystemInfo.graphicsDeviceType + ".");
        }

        // Terrain OFF/suspension follows the renderer's existing release contract. A later
        // Terrain ON may load the bundle again; a transient presentation lifecycle event is
        // not a permanent GPU capability failure.
        internal void ReleaseForSuspension()
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
            attempted = false;
            disabled = false;
            failureLogged = false;
            failure = string.Empty;
            bundlePath = string.Empty;
            bundleLoadMode = "NONE";
            viewportSuspendedResident = false;
        }

        public void Dispose()
        {
            ReleaseForSuspension();
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
