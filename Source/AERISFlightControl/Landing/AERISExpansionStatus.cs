using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace AERISFlightControl.Landing
{
    // CP3 Gate 5 Candidate 7: expansion installation state is intentionally
    // independent from runtime runway exposure. Disk presence is checked on a
    // ThreadPool worker so AIRFIELDS/ND never introduce a synchronous SSD read.
    internal static class AERISExpansionStatus
    {
        static readonly object Sync = new object();
        static volatile int scanState; // 0=not requested, 1=scanning, 2=complete
        static int scanGeneration;
        static volatile bool makingHistoryInstalled;
        static volatile bool breakingGroundInstalled;
        static volatile bool makingHistoryLoaded;
        static volatile bool breakingGroundLoaded;

        internal static bool MakingHistoryInstalled { get { return makingHistoryInstalled; } }
        internal static bool BreakingGroundInstalled { get { return breakingGroundInstalled; } }
        internal static bool MakingHistoryLoaded { get { return makingHistoryLoaded; } }
        internal static bool BreakingGroundLoaded { get { return breakingGroundLoaded; } }
        internal static bool ScanComplete { get { return scanState == 2; } }

        internal static void RequestRefresh()
        {
            RefreshLoadedAssemblies();
            string root;
            try { root = KSPUtil.ApplicationRootPath; }
            catch { root = string.Empty; }
            int generation;
            lock (Sync)
            {
                if (scanState == 1) return;
                scanState = 1;
                generation = ++scanGeneration;
            }
            ThreadPool.QueueUserWorkItem(delegate(object ignored)
            {
                bool mh = false;
                bool bg = false;
                try
                {
                    string expansionRoot = Path.Combine(root ?? string.Empty,
                        "GameData", "SquadExpansion");
                    mh = Directory.Exists(Path.Combine(expansionRoot, "MakingHistory"));
                    bg = Directory.Exists(Path.Combine(expansionRoot, "Serenity"));
                }
                catch { }
                lock (Sync)
                {
                    if (generation != scanGeneration) return;
                    makingHistoryInstalled = mh;
                    breakingGroundInstalled = bg;
                    scanState = 2;
                }
            });
        }

        internal static void RefreshLoadedAssemblies()
        {
            bool mh = false;
            bool bg = false;
            try
            {
                foreach (object loaded in AssemblyLoader.loadedAssemblies)
                {
                    if (loaded == null) continue;
                    string assemblyName = string.Empty;
                    string path = string.Empty;
                    try
                    {
                        object assemblyObject = AERISKspFacilityProvider.GetMemberValue(
                            loaded, "assembly", "Assembly");
                        Assembly assembly = assemblyObject as Assembly;
                        if (assembly != null) assemblyName = assembly.GetName().Name ?? string.Empty;
                    }
                    catch { }
                    try
                    {
                        object pathObject = AERISKspFacilityProvider.GetMemberValue(
                            loaded, "path", "Path", "url", "Url");
                        if (pathObject != null) path = pathObject.ToString();
                    }
                    catch { }
                    string marker = (assemblyName + " " + path).Replace('\\', '/').ToLowerInvariant();
                    if (marker.Contains("squadexpansion/makinghistory") ||
                        marker.Contains("makinghistory")) mh = true;
                    if (marker.Contains("squadexpansion/serenity") ||
                        marker.Contains("breakingground") || marker.Contains("serenity")) bg = true;
                }
            }
            catch { }
            makingHistoryLoaded = mh;
            breakingGroundLoaded = bg;
        }

        internal static string ExpansionSummary
        {
            get
            {
                return "EXPANSIONS: MH " + Describe(makingHistoryInstalled,
                    makingHistoryLoaded) + " | BG " + Describe(breakingGroundInstalled,
                    breakingGroundLoaded);
            }
        }

        internal static string DlcRunwaySummary(int detected, int defined)
        {
            string runtime = "RUNTIME " + detected + "/" + defined;
            if (makingHistoryLoaded)
                return detected > 0
                    ? "DLC RUNWAY: DESSERT AIRFIELD AVAILABLE | " + runtime
                    : "DLC RUNWAY: DESSERT AIRFIELD SAVE-LOCKED / NOT EXPOSED | " + runtime;
            if (makingHistoryInstalled)
                return "DLC RUNWAY: DESSERT AIRFIELD INSTALLED / SESSION RESTART REQUIRED | " + runtime;
            if (scanState == 1 || scanState == 0)
                return "DLC RUNWAY: DESSERT AIRFIELD INSTALL CHECK PENDING | " + runtime;
            return "DLC RUNWAY: DESSERT AIRFIELD — MAKING HISTORY NOT INSTALLED | " + runtime;
        }

        static string Describe(bool installed, bool loaded)
        {
            if (loaded) return installed || scanState != 2 ? "LOADED" : "LOADED (DISK CHECK STALE)";
            if (installed) return "INSTALLED / RESTART REQUIRED";
            if (scanState == 1 || scanState == 0) return "CHECKING";
            return "NOT INSTALLED";
        }
    }
}
