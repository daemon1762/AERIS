using System;

namespace AERISFlightControl.API
{
    // Certified runway geometry is exposed separately through
    // AERISRunwayTrackTokenApi.  That token is immutable/read-only; obtaining it does
    // not register a provider and cannot invoke this control-capable bridge.
    // Optional extension contract for aircraft-specific ground deceleration devices.
    // AERIS owns the trajectory and safety allowance; providers own only their hardware.
    public struct AERISGroundAssistRequest
    {
        public bool TouchdownSessionActive;
        public bool StableGroundContact;
        public float SurfaceSpeedMps;
        public float TargetDecelerationMps2;
        public float MeasuredDecelerationMps2;
        public float StabilityAllowance01;
        public bool ReverseThrustRequested;
        public float ReverseThrustDemand01;
    }

    public struct AERISGroundAssistStatus
    {
        public bool ReverseThrustAvailable;
        public bool ReverseThrustActive;
        public bool AsymmetricReverseThrust;
        public string Detail;
    }

    public interface IAERISGroundAssistProvider
    {
        string ProviderId { get; }
        bool TryApplyGroundAssist(Vessel vessel, AERISGroundAssistRequest request,
            out AERISGroundAssistStatus status);
    }

    public static class AERISGroundAssistBridge
    {
        static IAERISGroundAssistProvider provider;
        public static IAERISGroundAssistProvider Provider { get { return provider; } }
        public static string ProviderId
        {
            get { return provider == null ? "None" : (provider.ProviderId ?? "UnnamedProvider"); }
        }

        public static void Register(IAERISGroundAssistProvider value) { provider = value; }
        public static void Unregister(IAERISGroundAssistProvider value)
        {
            if (ReferenceEquals(provider, value)) provider = null;
        }

        public static bool TryApply(Vessel vessel, AERISGroundAssistRequest request,
            out AERISGroundAssistStatus status)
        {
            status = new AERISGroundAssistStatus();
            IAERISGroundAssistProvider active = provider;
            if (active == null || vessel == null) return false;
            try { return active.TryApplyGroundAssist(vessel, request, out status); }
            catch { return false; }
        }
    }
}
