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
        const string KspBtShaderBundleName = "aeris_nd_gpu_vertex_projection_kspbt";
        const string KspBtProbeBundleName = "aeris_gpu_bundle_probe_kspbt";

        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64,
                "aeris_nd_gpu_vertex_projection_windows.bundle",
                "aeris_gpu_bundle_probe_windows.bundle",
                "aeris_nd_gpu_vertex_projection_diagnostic_windows.bundle",
                "aeris_gpu_bundle_probe_diagnostic_windows.bundle");
        }

        public static void BuildLinux()
        {
            Build(BuildTarget.StandaloneLinux64,
                "aeris_nd_gpu_vertex_projection_linux.bundle",
                "aeris_gpu_bundle_probe_linux.bundle",
                null, null);
        }

        static void Build(BuildTarget target, string installedShaderName,
            string installedProbeName, string diagnosticShaderName,
            string diagnosticProbeName)
        {
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Active build target mismatch: active=" +
                    EditorUserBuildSettings.activeBuildTarget + "; requested=" + target);
            if (!File.Exists(Path.Combine(Application.dataPath,
                    "AERISNdExactVertexProjection.shader")))
                throw new FileNotFoundException("Shader asset not found", ShaderAssetPath);
            if (!File.Exists(Path.Combine(Application.dataPath, "AERISBundleProbe.txt")))
                throw new FileNotFoundException("Probe asset not found", ProbeAssetPath);

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
            string destinationDirectory = Path.Combine(repositoryRoot, "GameData",
                "AERISFlightControl", "Shaders");
            Directory.CreateDirectory(destinationDirectory);

            // Control arm retained under diagnostic-only names. This is the exact
            // uncompressed/force-rebuilt format already proven to receive Unity's
            // "not compatible with this newer version" rejection in KSP.
            string diagnosticOutput = Path.Combine(projectRoot, "Temp",
                "AERIS_GPU_BUNDLE_DIAGNOSTIC_" + target);
            RecreateDirectory(diagnosticOutput);
            AssetBundleBuild[] diagnosticBuilds = new AssetBundleBuild[2];
            diagnosticBuilds[0] = new AssetBundleBuild
            {
                assetBundleName = ShaderBundleName,
                assetNames = new[] { ShaderAssetPath }
            };
            diagnosticBuilds[1] = new AssetBundleBuild
            {
                assetBundleName = ProbeBundleName,
                assetNames = new[] { ProbeAssetPath }
            };
            BuildAssetBundleOptions diagnosticOptions =
                BuildAssetBundleOptions.UncompressedAssetBundle |
                BuildAssetBundleOptions.DeterministicAssetBundle |
                BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.StrictMode;
            RequireManifest(BuildPipeline.BuildAssetBundles(diagnosticOutput,
                diagnosticBuilds, diagnosticOptions, target), "AERIS diagnostic", target);

            if (target != BuildTarget.StandaloneWindows64)
            {
                // Native Linux remains on the existing diagnostic path; KSPBuildTools'
                // published builtin builder is Windows-target specific.
                CopyRequired(Path.Combine(diagnosticOutput, ShaderBundleName),
                    Path.Combine(destinationDirectory, installedShaderName), "Linux shader");
                CopyRequired(Path.Combine(diagnosticOutput, ProbeBundleName),
                    Path.Combine(destinationDirectory, installedProbeName), "Linux probe");
                return;
            }

            CopyRequired(Path.Combine(diagnosticOutput, ShaderBundleName),
                Path.Combine(destinationDirectory, diagnosticShaderName),
                "AERIS diagnostic control shader");
            CopyRequired(Path.Combine(diagnosticOutput, ProbeBundleName),
                Path.Combine(destinationDirectory, diagnosticProbeName),
                "AERIS diagnostic control probe");

            // Experimental arm: mirror KSPBuildTools' builtin AssetBundleBuilder.
            // Explicit one-bundle AssetBundleBuild arrays and ONLY ChunkBasedCompression.
            // The runtime-visible primary filenames are populated from this arm so no
            // loader change can contaminate the A/B result.
            string kspBtShaderOutput = Path.Combine(projectRoot, "Temp",
                "AERIS_GPU_KSPBT_SHADER");
            string kspBtProbeOutput = Path.Combine(projectRoot, "Temp",
                "AERIS_GPU_KSPBT_PROBE");
            RecreateDirectory(kspBtShaderOutput);
            RecreateDirectory(kspBtProbeOutput);

            AssetBundleBuild[] shaderBuild = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = KspBtShaderBundleName,
                    assetNames = new[] { ShaderAssetPath }
                }
            };
            AssetBundleBuild[] probeBuild = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = KspBtProbeBundleName,
                    assetNames = new[] { ProbeAssetPath }
                }
            };
            BuildAssetBundleOptions kspBtOptions =
                BuildAssetBundleOptions.ChunkBasedCompression;
            RequireManifest(BuildPipeline.BuildAssetBundles(kspBtShaderOutput,
                shaderBuild, kspBtOptions, target), "KSPBuildTools parity shader", target);
            RequireManifest(BuildPipeline.BuildAssetBundles(kspBtProbeOutput,
                probeBuild, kspBtOptions, target), "KSPBuildTools parity probe", target);

            CopyRequired(Path.Combine(kspBtShaderOutput, KspBtShaderBundleName),
                Path.Combine(destinationDirectory, installedShaderName),
                "KSPBuildTools parity PRIMARY shader");
            CopyRequired(Path.Combine(kspBtProbeOutput, KspBtProbeBundleName),
                Path.Combine(destinationDirectory, installedProbeName),
                "KSPBuildTools parity PRIMARY probe");
            Debug.Log("[AERIS24 GPU VERTEX] A/B arm=KSPBuildTools; options=ChunkBasedCompression only; runtime primary replaced");
        }

        static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            Directory.CreateDirectory(path);
        }

        static void RequireManifest(AssetBundleManifest manifest, string label,
            BuildTarget target)
        {
            if (manifest == null)
                throw new InvalidOperationException(label +
                    " BuildAssetBundles returned null for " + target);
        }

        static void CopyRequired(string source, string destination, string label)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException("Expected " + label +
                    " AssetBundle was not emitted", source);
            File.Copy(source, destination, true);
            Debug.Log("[AERIS24 GPU VERTEX] built " + label + ": " + destination);
        }
    }
}
