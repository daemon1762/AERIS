using System.Globalization;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Core
{
    // Proof-candidate identity witness. Included only by the temporary R043 proof project.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR043RuntimeBuildIdentityObserver : MonoBehaviour
    {
        void Start()
        {
            AERISLogger.Info(
                "[AERIS43][R043_BUILD_IDENTITY]" +
                "; semantic=" + Safe(AERISBuildVersion.Semantic) +
                "; display=" + Safe(AERISBuildVersion.Display) +
                "; checkpoint=" + Safe(AERISBuildVersion.UiCheckpoint) +
                "; candidate=" + Safe(AERISBuildVersion.CandidateName) +
                "; source_git_sha=" + Safe(AERISBuildVersion.SourceGitSha) +
                "; source_tree_sha256=" + Safe(AERISBuildVersion.SourceTreeSha256) +
                "; realtime=" + Time.realtimeSinceStartup.ToString("R", CultureInfo.InvariantCulture));
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
