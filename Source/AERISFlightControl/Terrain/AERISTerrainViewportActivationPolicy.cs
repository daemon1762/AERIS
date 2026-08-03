using System;

namespace AERISFlightControl.Terrain
{
    // CP2.5 central flight-viewport activation authority. This policy gates only the
    // aviation Navigation Display terrain viewport. It never gates Preload Builder,
    // FDI, FlightState, AP, AA or PROTECT processing.
    internal sealed class AERISTerrainViewportActivationPolicy
    {
        internal const double ReactivateBelowAltitudeAslMeters = 39500.0;
        internal const double DeactivateAtOrAboveAltitudeAslMeters = 40500.0;

        bool initialized;
        bool active;
        int revision;
        double altitudeAslMeters;
        string bodyName = string.Empty;
        string status = "TERRAIN VIEWPORT INACTIVE — NOT EVALUATED";

        internal bool Active { get { return initialized && active; } }
        internal bool Initialized { get { return initialized; } }
        internal int Revision { get { return revision; } }
        internal double AltitudeAslMeters { get { return altitudeAslMeters; } }
        internal string BodyName { get { return bodyName; } }
        internal string StatusText { get { return status; } }

        internal bool Evaluate(bool flightViewportEligible, double currentAltitudeAslMeters,
            string currentBodyName)
        {
            bodyName = currentBodyName ?? string.Empty;
            altitudeAslMeters = Finite(currentAltitudeAslMeters) ?
                currentAltitudeAslMeters : double.NaN;

            if (!flightViewportEligible || !Finite(currentAltitudeAslMeters))
                return Publish(false, "TERRAIN VIEWPORT OFF — NO ACTIVE FLIGHT ASL");

            if (!initialized)
            {
                bool initialActive = currentAltitudeAslMeters <
                    DeactivateAtOrAboveAltitudeAslMeters;
                return Publish(initialActive, initialActive ?
                    "TERRAIN VIEWPORT ON — INITIAL BELOW 40.5 KM ASL" :
                    "TERRAIN VIEWPORT OFF — AT/ABOVE 40.5 KM ASL");
            }

            if (currentAltitudeAslMeters >= DeactivateAtOrAboveAltitudeAslMeters)
                return Publish(false, "TERRAIN VIEWPORT OFF — AT/ABOVE 40.5 KM ASL");
            if (currentAltitudeAslMeters < ReactivateBelowAltitudeAslMeters)
                return Publish(true, "TERRAIN VIEWPORT ON — BELOW 39.5 KM ASL");

            status = active ?
                "TERRAIN VIEWPORT ON — 39.5–40.5 KM HYSTERESIS HOLD" :
                "TERRAIN VIEWPORT OFF — 39.5–40.5 KM HYSTERESIS HOLD";
            return false;
        }

        internal void Reset(string reason)
        {
            initialized = false;
            active = false;
            altitudeAslMeters = double.NaN;
            bodyName = string.Empty;
            revision++;
            status = string.IsNullOrEmpty(reason) ?
                "TERRAIN VIEWPORT INACTIVE — RESET" :
                "TERRAIN VIEWPORT INACTIVE — RESET: " + reason.ToUpperInvariant();
        }

        bool Publish(bool nextActive, string nextStatus)
        {
            bool changed = !initialized || active != nextActive;
            initialized = true;
            active = nextActive;
            status = nextStatus ?? string.Empty;
            if (changed) revision++;
            return changed;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
