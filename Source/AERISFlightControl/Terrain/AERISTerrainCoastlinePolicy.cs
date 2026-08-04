namespace AERISFlightControl.Terrain
{
    // Shared land/water boundary policy. Terrain flags are sampled at grid vertices.
    // Candidate 4 refines the crossing within the cell using the same 1 m ASL
    // threshold used by terrain sampling. If reconstructed/legacy data does not
    // bracket that threshold, retain the proven Golden fallback instead of inventing
    // a shoreline outside the classified land/water edge.
    internal static class AERISTerrainCoastlinePolicy
    {
        internal const float WaterElevationThresholdMeters = 1.0f;
        internal const float LandInsetFraction = 0.38f;

        internal static float CrossingFraction(bool water0, bool water1)
        {
            if (water0 == water1) return 0.5f;
            return water0 ? 1f - LandInsetFraction : LandInsetFraction;
        }

        internal static float CrossingFraction(bool water0, bool water1,
            float elevation0Meters, float elevation1Meters)
        {
            if (water0 == water1) return 0.5f;
            if (Finite(elevation0Meters) && Finite(elevation1Meters))
            {
                float delta = elevation1Meters - elevation0Meters;
                if (System.Math.Abs(delta) > 0.0001f)
                {
                    float t = (WaterElevationThresholdMeters - elevation0Meters) / delta;
                    // Only accept interpolation that lies on this classified edge.
                    // Inconsistent virtual/legacy samples fall back to Golden policy.
                    if (t >= 0f && t <= 1f) return t;
                }
            }
            return CrossingFraction(water0, water1);
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

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
