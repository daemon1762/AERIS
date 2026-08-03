using System;

namespace AERISFlightControl.Terrain
{
    [Flags]
    internal enum AERISTerrainLandDetailDemand
    {
        None = 0,
        LandArm = 1,
        Approach = 2,
        AutoLanding = 4
    }

    // CP2.5 Gate 3 central authority for LAND-resolution terrain work. This policy is
    // display/data only: it never writes FlightCtrlState, AP targets, vessel controls or
    // KSP graphics settings. A developer capability toggle merely permits LAND detail;
    // an operational landing demand must also exist before the LAND profile or requests
    // are activated.
    internal sealed class AERISTerrainLandDetailActivationPolicy
    {
        internal bool Active { get; private set; }
        internal bool CapabilityEnabled { get; private set; }
        internal AERISTerrainLandDetailDemand Demand { get; private set; }
        internal string BodyName { get; private set; } = string.Empty;
        internal string StatusText { get; private set; } =
            "LAND DETAIL OFF — NOT EVALUATED";
        internal int Revision { get; private set; }

        internal bool Evaluate(bool flightEligible, bool flightViewportActive,
            bool capabilityEnabled, bool landArmDemand, bool approachDemand,
            bool autoLandingDemand, string bodyName)
        {
            AERISTerrainLandDetailDemand demand = AERISTerrainLandDetailDemand.None;
            if (landArmDemand) demand |= AERISTerrainLandDetailDemand.LandArm;
            if (approachDemand) demand |= AERISTerrainLandDetailDemand.Approach;
            if (autoLandingDemand) demand |= AERISTerrainLandDetailDemand.AutoLanding;

            bool nextActive = flightEligible && flightViewportActive &&
                capabilityEnabled && demand != AERISTerrainLandDetailDemand.None;
            string nextStatus = !flightEligible ?
                "LAND DETAIL OFF — NO ACTIVE FLIGHT" :
                !flightViewportActive ?
                    "LAND DETAIL OFF — TERRAIN VIEWPORT OFF" :
                !capabilityEnabled ?
                    "LAND DETAIL OFF — DEVELOPER CAPABILITY DISABLED" :
                demand == AERISTerrainLandDetailDemand.None ?
                    "LAND DETAIL STANDBY — WAITING FOR LAND ARM / APPROACH" :
                    "LAND DETAIL ACTIVE — " + FormatDemand(demand);

            bool changed = nextActive != Active || demand != Demand ||
                capabilityEnabled != CapabilityEnabled ||
                !string.Equals(BodyName, bodyName ?? string.Empty,
                    StringComparison.Ordinal);
            Active = nextActive;
            CapabilityEnabled = capabilityEnabled;
            Demand = demand;
            BodyName = bodyName ?? string.Empty;
            StatusText = nextStatus;
            if (changed) Revision++;
            return changed;
        }

        internal void Reset(string reason)
        {
            bool changed = Active || CapabilityEnabled ||
                Demand != AERISTerrainLandDetailDemand.None ||
                !string.IsNullOrEmpty(BodyName);
            Active = false;
            CapabilityEnabled = false;
            Demand = AERISTerrainLandDetailDemand.None;
            BodyName = string.Empty;
            StatusText = string.IsNullOrEmpty(reason) ?
                "LAND DETAIL RESET" :
                "LAND DETAIL RESET — " + reason.ToUpperInvariant();
            if (changed) Revision++;
        }

        internal static string FormatDemand(AERISTerrainLandDetailDemand demand)
        {
            if ((demand & AERISTerrainLandDetailDemand.AutoLanding) != 0)
                return "AUTO_LANDING";
            if ((demand & AERISTerrainLandDetailDemand.Approach) != 0)
                return "APPROACH";
            if ((demand & AERISTerrainLandDetailDemand.LandArm) != 0)
                return "LAND_ARM";
            return "NONE";
        }
    }
}
