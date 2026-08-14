using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace AERIS.Editor
{
    public static class BuildAERISGpuAssets
    {
        const string ShaderAssetPath = "Assets/AERISNdExactVertexProjection.shader";
        const string ProbeAssetPath = "Assets/AERISBundleProbe.txt";
        const string ShaderBundleName = "aeris_nd_gpu_vertex_projection";
        const string ProbeBundleName = "aeris_gpu_bundle_probe";

        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64,
                "aeris_nd_gpu_vertex_projection_windows.bundle",
                "aeris_gpu_bundle_probe_windows.bundle");
        }

        public static void BuildLinux()
        {
            Build(BuildTarget.StandaloneLinux64,
                "aeris_nd_gpu_vertex_projection_linux.bundle",
                "aeris_gpu_bundle_probe_linux.bundle");
        }

        static void Build(BuildTarget target, string installedShaderName,
            string installedProbeName)
        {
            // The batch launcher must open this project under the same active target as
            // the bundle target (-buildTarget Win64/Linux64). Fail instead of silently
            // emitting a host-target-imported bundle if that invariant is broken.
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Active build target mismatch: active=" +
                    EditorUserBuildSettings.activeBuildTarget + "; requested=" + target);
            if (!File.Exists(Path.Combine(Application.dataPath,
                    "AERISNdExactVertexProjection.shader")))
                throw new FileNotFoundException("Shader asset not found", ShaderAssetPath);
            if (!File.Exists(Path.Combine(Application.dataPath, "AERISBundleProbe.txt")))
                throw new FileNotFoundException("Probe asset not found", ProbeAssetPath);

            // Mirror the KSPBuildTools Windows graphics-API environment.  KSPBuildTools
            // deliberately disables Unity's default Windows graphics API list and exports
            // with OpenGLCore + Direct3D11.  Keep every other diagnostic variable unchanged
            // for this hotfix so the next probe isolates this one compatibility condition.
            if (target == BuildTarget.StandaloneWindows64)
            {
                PlayerSettings.SetUseDefaultGraphicsAPIs(
                    BuildTarget.StandaloneWindows64, false);
                PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
                    new[] { GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Direct3D11 });
                Debug.Log("[AERIS24 GPU VERTEX] Windows graphics APIs=OpenGLCore,Direct3D11 (KSPBuildTools parity)");
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Directory.GetParent(projectRoot).FullName;
            string tempOutput = Path.Combine(projectRoot, "Temp",
                "AERIS_GPU_BUNDLE_" + target);
            if (Directory.Exists(tempOutput)) Directory.Delete(tempOutput, true);
            Directory.CreateDirectory(tempOutput);

            // Keep the exact previous diagnostic container format: tiny, uncompressed
            // UnityFS bundles built under the same target/options.  The only intended
            // experimental change in this revision is the Windows graphics API list above.
            BuildAssetBundleOptions options =
                BuildAssetBundleOptions.UncompressedAssetBundle |
                BuildAssetBundleOptions.DeterministicAssetBundle |
                BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.StrictMode;
            AssetBundleBuild[] builds = new AssetBundleBuild[2];
            builds[0] = new AssetBundleBuild
            {
                assetBundleName = ShaderBundleName,
                assetNames = new[] { ShaderAssetPath }
            };
            builds[1] = new AssetBundleBuild
            {
                assetBundleName = ProbeBundleName,
                assetNames = new[] { ProbeAssetPath }
            };

            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(tempOutput,
                builds, options, target);
            if (manifest == null)
                throw new InvalidOperationException("BuildAssetBundles returned null for " +
                    target);

            string shaderSource = Path.Combine(tempOutput, ShaderBundleName);
            string probeSource = Path.Combine(tempOutput, ProbeBundleName);
            if (!File.Exists(shaderSource))
                throw new FileNotFoundException("Expected shader AssetBundle was not emitted",
                    shaderSource);
            if (!File.Exists(probeSource))
                throw new FileNotFoundException("Expected probe AssetBundle was not emitted",
                    probeSource);

            string destinationDirectory = Path.Combine(repositoryRoot, "GameData",
                "AERISFlightControl", "Shaders");
            Directory.CreateDirectory(destinationDirectory);
            string shaderDestination = Path.Combine(destinationDirectory,
                installedShaderName);
            string probeDestination = Path.Combine(destinationDirectory,
                installedProbeName);
            File.Copy(shaderSource, shaderDestination, true);
            File.Copy(probeSource, probeDestination, true);

            Debug.Log("[AERIS24 GPU VERTEX] built target-matched uncompressed " + target +
                " shader bundle: " + shaderDestination);
            Debug.Log("[AERIS24 GPU VERTEX] built target-matched uncompressed " + target +
                " probe bundle: " + probeDestination);
        }
    }
}
