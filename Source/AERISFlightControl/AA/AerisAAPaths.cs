using System;
using System.IO;

namespace AtmosphereAutopilot
{
    /// <summary>
    /// Storage paths for the AA code embedded in AERIS.
    /// Keeps embedded-AA state isolated from a separately installed AtmosphereAutopilot.
    /// </summary>
    internal static class AerisAAPaths
    {
        internal static string Root { get { return KSPUtil.ApplicationRootPath + "GameData/AERISFlightControl/AA"; } }
        internal static string GlobalSettings { get { return Root + "/Global_settings.txt"; } }
        internal static string DesignsDirectory { get { return Root + "/designs"; } }
        internal static string VesselDesign(string vesselName)
        {
            return DesignsDirectory + "/" + KSPUtil.SanitizeFilename(vesselName) + ".txt";
        }
        internal static string LegacyGlobalSettings { get { return KSPUtil.ApplicationRootPath + "GameData/AtmosphereAutopilot/Global_settings.txt"; } }
        internal static string LegacyVesselDesign(string vesselName)
        {
            return KSPUtil.ApplicationRootPath + "GameData/AtmosphereAutopilot/designs/" + KSPUtil.SanitizeFilename(vesselName) + ".txt";
        }
        internal static void EnsureDirectories()
        {
            try { Directory.CreateDirectory(Root); Directory.CreateDirectory(DesignsDirectory); }
            catch { }
        }
        // One-way compatibility import: legacy files are read only when the AERIS copy does not exist.
        // All subsequent serialization writes only inside GameData/AERISFlightControl/AA.
        internal static string ResolveGlobalForRead()
        {
            if (File.Exists(GlobalSettings)) return GlobalSettings;
            return File.Exists(LegacyGlobalSettings) ? LegacyGlobalSettings : GlobalSettings;
        }
        internal static string ResolveVesselForRead(string vesselName)
        {
            string current = VesselDesign(vesselName);
            if (File.Exists(current)) return current;
            string legacy = LegacyVesselDesign(vesselName);
            return File.Exists(legacy) ? legacy : current;
        }
    }
}
