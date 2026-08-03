using System;

namespace AERISFlightControl.Terrain
{
    // Shared land/water boundary policy. Candidate 3 replaces the old fixed 0.38
    // crossing with a sea-level interpolation whenever the authoritative endpoint
    // elevations bracket the ocean surface. This increases shoreline precision
    // without increasing FAR mesh resolution or allocating a second Hi-Res mesh.
    internal static class AERISTerrainCoastlinePolicy
    {
        internal const float LandInsetFraction = 0.38f;
        internal const float OceanSurfaceMeters = 1.0f;
        const float LandSafetyBiasFraction = 0.02f;

        internal static float CrossingFraction(bool water0, bool water1)
        {
            if (water0 == water1) return 0.5f;
            return water0 ? 1f - LandInsetFraction : LandInsetFraction;
        }

        internal static float CrossingFraction(bool water0, bool water1,
            float elevation0Meters, float elevation1Meters)
        {
            if (water0 == water1) return 0.5f;
            if (float.IsNaN(elevation0Meters) || float.IsInfinity(elevation0Meters) ||
                float.IsNaN(elevation1Meters) || float.IsInfinity(elevation1Meters) ||
                Math.Abs(elevation1Meters - elevation0Meters) <= 0.0001f)
                return CrossingFraction(water0, water1);

            float t = (OceanSurfaceMeters - elevation0Meters) /
                (elevation1Meters - elevation0Meters);
            if (float.IsNaN(t) || float.IsInfinity(t) || t <= 0f || t >= 1f)
                return CrossingFraction(water0, water1);

            // Preserve the historical safety rule: uncertain pixels belong to water,
            // so the visible land edge is moved a tiny amount toward the land sample.
            if (water0) t += (1f - t) * LandSafetyBiasFraction;
            else t -= t * LandSafetyBiasFraction;
            return Math.Max(0.05f, Math.Min(0.95f, t));
        }

        internal static float Interpolate(float value0, float value1,
            bool water0, bool water1)
        {
            float t = CrossingFraction(water0, water1);
            return value0 + (value1 - value0) * t;
        }

        internal static float Interpolate(float value0, float value1,
            bool water0, bool water1, float elevation0Meters,
            float elevation1Meters)
        {
            float t = CrossingFraction(water0, water1, elevation0Meters,
                elevation1Meters);
            return value0 + (value1 - value0) * t;
        }
    }
}
