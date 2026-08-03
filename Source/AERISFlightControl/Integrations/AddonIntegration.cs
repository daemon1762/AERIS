using System;
using System.Collections.Generic;
using AERISFlightControl.API;
using UnityEngine;

namespace AERISFlightControl.Integrations
{
    // Add-on discovery and vessel compatibility are intentionally event-driven.
    // UI code reads cached state only; it never scans vessel parts or probes APIs.
    internal interface IAERISAddonIntegration
    {
        string Id { get; }
        string DisplayName { get; }
        bool IsInstalled { get; }
        bool IsApiReady { get; }
        bool CurrentVesselCompatible { get; }
        string StatusText { get; }
        void DetectAtStartup();
        void RefreshForVessel(Vessel vessel);
    }

    internal sealed class AutoPropPitchIntegration : IAERISAddonIntegration
    {
        public string Id { get { return "AutoPropPitch"; } }
        public string DisplayName { get { return "AutoPropPitch"; } }
        public bool IsInstalled { get; private set; }
        public bool IsApiReady { get; private set; }
        public bool CurrentVesselCompatible { get; private set; }
        public string StatusText { get; private set; }
        public string ProviderId { get; private set; }
        public AERISPropulsionStatus LastCompatibilityStatus { get; private set; }

        public void DetectAtStartup()
        {
            var ids = AERISPropulsionBridge.GetRegisteredProviderIds();
            bool assemblyLoaded = IsAutoPropPitchAssemblyLoaded();
            IsInstalled = ids.Length > 0 || assemblyLoaded;
            IsApiReady = ids.Length > 0;
            ProviderId = ids.Length == 0 ? (assemblyLoaded ? "API not registered" : "Not installed") :
                string.Join(", ", ids);
            StatusText = ids.Length > 0 ? (ids.Length + " propulsion provider(s) registered") :
                (assemblyLoaded ? "Installed / propulsion provider API not registered" : "Not installed");
        }

        static bool IsAutoPropPitchAssemblyLoaded()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    string name = assemblies[i] == null ? string.Empty :
                        assemblies[i].GetName().Name;
                    if (string.Equals(name, "AutoPropPitch", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("AutoPropPitch.", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        public void RefreshForVessel(Vessel vessel)
        {
            CurrentVesselCompatible = false;
            LastCompatibilityStatus = new AERISPropulsionStatus();
            if (!IsApiReady || vessel == null)
            {
                StatusText = !IsInstalled ? "Not installed" :
                    (IsApiReady ? "Awaiting compatible vessel" :
                    "Installed / propulsion provider API not registered");
                return;
            }
            AERISPropulsionStatus status;
            try
            {
                string selectedProviderId;
                if (AERISPropulsionBridge.TrySelectProvider(vessel, new AERISPropulsionRequest(), out selectedProviderId, out status))
                {
                    LastCompatibilityStatus = status;
                    CurrentVesselCompatible = status.HasControllablePropulsion;
                    if (CurrentVesselCompatible) ProviderId = selectedProviderId;
                    StatusText = CurrentVesselCompatible ? "Compatible external propulsion: " + selectedProviderId : "No compatible propulsion on current vessel";
                }
                else StatusText = "Provider did not return vessel status";
            }
            catch (Exception ex)
            {
                StatusText = "Compatibility probe failed: " + ex.GetType().Name;
            }
        }
    }

    internal static class AddonIntegration
    {
        static readonly List<IAERISAddonIntegration> all = new List<IAERISAddonIntegration>();
        static readonly AutoPropPitchIntegration app = new AutoPropPitchIntegration();
        static bool startupDetected;
        internal static IList<IAERISAddonIntegration> All { get { return all; } }
        internal static bool StartupDetected { get { return startupDetected; } }
        internal static bool AppInstalled { get { return app.IsInstalled; } }
        internal static bool AppApiReady { get { return app.IsApiReady; } }
        internal static string AppProviderId { get { return app.ProviderId ?? "Not installed"; } }
        internal static string AppStatusText { get { return app.StatusText ?? "Not checked"; } }
        internal static bool CurrentVesselSupportsApp { get { return app.CurrentVesselCompatible; } }

        static AddonIntegration()
        {
            all.Add(app);
        }

        // Startup registration order is not guaranteed across companion mods.
        // We detect once at boot, then allow an event-triggered retry only while a provider is absent.
        internal static bool DetectAtStartup(bool allowLateRetry = false)
        {
            if (startupDetected && (!allowLateRetry || app.IsApiReady)) return app.IsApiReady;
            foreach (var integration in all) integration.DetectAtStartup();
            startupDetected = true;
            return app.IsApiReady;
        }

        internal static void RefreshForVessel(Vessel vessel)
        {
            // No polling: a retry happens only on the existing vessel/scene events.
            if (!startupDetected || !app.IsApiReady) DetectAtStartup(true);
            foreach (var integration in all) integration.RefreshForVessel(vessel);
        }
    }
}
