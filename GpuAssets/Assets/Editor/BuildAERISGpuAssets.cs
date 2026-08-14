using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace AERIS.Editor
{
    public static class BuildAERISGpuAssets
    {
        const string ShaderAssetPath = "Assets/AERISNdExactVertexProjection.shader";
        const string BundleName = "aeris_nd_gpu_vertex_projection";

        public static void BuildWindows()
        {
            Build(BuildTarget.StandaloneWindows64,
                "aeris_nd_gpu_vertex_projection_windows.bundle");
        }

        public static void BuildLinux()
        {
            Build(BuildTarget.StandaloneLinux64,
                "aeris_nd_gpu_vertex_projection_linux.bundle");
        }

        static void Build(BuildTarget target, string installedName)
        {
            // The batch launcher must open this project under the same active target as
            // the bundle target (-buildTarget Win64/Linux64). Fail instead of silently
            // emitting a host-target-imported bundle if that invariant is broken.
            if (EditorUserBuildSettings.activeBuildTarget != target)
                throw new InvalidOperationException("Active build target mismatch: active=" +
                    EditorUserBuildSettings.activeBuildTarget + "; requested=" + target);

            AssetImporter importer = AssetImporter.GetAtPath(ShaderAssetPath);
            if (importer == null)
                throw new InvalidOperationException("Shader asset not found: " +
                    ShaderAssetPath);
            importer.assetBundleName = BundleName;
            importer.assetBundleVariant = string.Empty;
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string repositoryRoot = Directory.GetParent(projectRoot).FullName;
            string tempOutput = Path.Combine(projectRoot, "Temp",
                "AERIS_GPU_BUNDLE_" + target);
            Directory.CreateDirectory(tempOutput);

            // ForceRebuild prevents an earlier host-target/incremental artifact from being
            // reused after the active-target correction. StrictMode promotes any shader
            // compilation warning/error that would make the runtime bundle unusable into
            // a build failure instead of allowing a bad bundle to escape the asset gate.
            BuildAssetBundleOptions options =
                BuildAssetBundleOptions.ChunkBasedCompression |
                BuildAssetBundleOptions.DeterministicAssetBundle |
                BuildAssetBundleOptions.ForceRebuildAssetBundle |
                BuildAssetBundleOptions.StrictMode;
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(tempOutput,
                options, target);
            if (manifest == null)
                throw new InvalidOperationException("BuildAssetBundles returned null for " +
                    target);

            string source = Path.Combine(tempOutput, BundleName);
            if (!File.Exists(source))
                throw new FileNotFoundException("Expected AssetBundle was not emitted", source);

            string destinationDirectory = Path.Combine(repositoryRoot, "GameData",
                "AERISFlightControl", "Shaders");
            Directory.CreateDirectory(destinationDirectory);
            string destination = Path.Combine(destinationDirectory, installedName);
            File.Copy(source, destination, true);

            Debug.Log("[AERIS24 GPU VERTEX] built target-matched " + target +
                " bundle: " + destination);
        }
    }
}
