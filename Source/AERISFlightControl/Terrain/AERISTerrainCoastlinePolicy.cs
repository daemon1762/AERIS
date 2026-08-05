namespace AERISFlightControl.Terrain
{
    // Shared land/water boundary policy. Terrain flags are sampled at grid vertices.
    // CP3.75 Candidate 9 establishes one boundary authority for both the coastline line
    // and sparse land/water fill: the classified edge plus the proven Golden land inset.
    // Elevation remains terrain/shading data, but must not independently move the
    // coastline crossing because the persisted HD payload contains the 129x129 class mask
    // while sparse fill heights are reconstructed from the low-resolution base tile.
    internal static class AERISTerrainCoastlinePolicy
    {
        // Retained as the terrain sampling classification threshold for compatibility.
        // Candidate 9 no longer uses it to derive a second presentation crossing.
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
            // Candidate 9 unified coastal-boundary authority. The HD coastline extractor
            // and sparse correction clipper must return bit-for-bit identical crossing
            // coordinates for the same classified edge. Do not let independently sourced
            // elevation fields shift the painter boundary away from the coastline vector.
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
    }
}
