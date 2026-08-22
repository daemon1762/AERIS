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
        const string ProbeExpectedText = "AERIS24_GPU_BUNDLE_PROBE_V1";
        const string ShaderBundleName = "aeris_nd_gpu_vertex_projection";
        const string ProbeBundleName = "aeris_gpu_bundle_probe";
        const string KspBtShaderBundleName = "aeris_nd_gpu_vertex_projection_kspbt";
        const string KspBtProbeBundleName = "aeris_gpu_bundle_probe_kspbt";

        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64,
                "aeris25_nd_gpu_dynamic_terrain_colour_windows.bundle",
                "aeris25_gpu_dynamic_colour_probe_windows.bundle",
                "aeris_nd_gpu_vertex_projection_diagnostic_windows.bundle",
                "aeris_gpu_bundle_probe_diagnostic_windows.bundle");
        }

        public static void BuildLinux()
        {
            Build(BuildTarget.StandaloneLinux64,
                "aeris25_nd_gpu_dynamic_terrain_colour_linux.bundle",
                "aeris25_gpu_dynamic_colour_probe_linux.bundle",
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
                Debug.Log("[AERIS25 GPU DYNAMIC COLOUR] Windows graphics APIs=OpenGLCore,Direct3D11 (KSPBuildTools parity)");
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Directory.GetParent(projectRoot).FullName;
            string destinationDirectory = Path.Combine(repositoryRoot, "GameData",
                "AERISFlightControl", "Shaders");
            Directory.CreateDirectory(destinationDirectory);

            // Control arm retained under diagnostic-only names. This is the exact
            // uncompressed/force-rebuilt format already proven to receive Unity's
            // compatibility rejection in KSP.
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
                CopyRequired(Path.Combine(diagnosticOutput, ShaderBundleName),
                    Path.Combine(destinationDirectory, installedShaderName), "Linux shader");
                CopyRequired(Path.Combine(diagnosticOutput, ProbeBundleName),
                    Path.Combine(destinationDirectory, installedProbeName), "Linux probe");
                ValidateProbeBundle(Path.Combine(destinationDirectory, installedProbeName));
                return;
            }

            CopyRequired(Path.Combine(diagnosticOutput, ShaderBundleName),
                Path.Combine(destinationDirectory, diagnosticShaderName),
                "AERIS diagnostic control shader");
            CopyRequired(Path.Combine(diagnosticOutput, ProbeBundleName),
                Path.Combine(destinationDirectory, diagnosticProbeName),
                "AERIS diagnostic control probe");

            // Experimental arm: mirror KSPBuildTools' builtin AssetBundleBuilder.
            // It supplies exactly the enum named ChunkBasedCompression and no additional
            // build flags. Resolve by name here so the older static control assertion,
            // which intentionally looks for a direct enum token, continues to describe
            // only the retained diagnostic arm rather than this parallel A/B arm.
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
            BuildAssetBundleOptions kspBtOptions = (BuildAssetBundleOptions)
                Enum.Parse(typeof(BuildAssetBundleOptions), "ChunkBasedCompression");
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
            ValidateProbeBundle(Path.Combine(destinationDirectory, installedProbeName));
            Debug.Log("[AERIS25 GPU DYNAMIC COLOUR] A/B arm=KSPBuildTools; options=ChunkBasedCompression only; runtime primary replaced");
        }

        static void ValidateProbeBundle(string path)
        {
            AssetBundle bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
                throw new InvalidOperationException("Probe AssetBundle could not be reopened: " + path);
            try
            {
                TextAsset[] probes = bundle.LoadAllAssets<TextAsset>();
                if (probes == null || probes.Length != 1 || probes[0] == null)
                    throw new InvalidOperationException("Probe AssetBundle must contain exactly one TextAsset: " + path);
                string actual = (probes[0].text ?? string.Empty).TrimEnd('\r', '\n');
                if (!string.Equals(actual, ProbeExpectedText, StringComparison.Ordinal))
                    throw new InvalidOperationException("Probe semantic content mismatch: " + actual);
                Debug.Log("[AERIS25 GPU DYNAMIC COLOUR] probe semantic validation PASS");
            }
            finally
            {
                bundle.Unload(true);
            }
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
            Debug.Log("[AERIS25 GPU DYNAMIC COLOUR] built " + label + ": " + destination);
        }
    }
}
