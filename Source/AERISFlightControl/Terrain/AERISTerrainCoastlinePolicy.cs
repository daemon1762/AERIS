namespace AERISFlightControl.Terrain
{
    // Shared land/water boundary policy.  Terrain flags are sampled at grid vertices,
    // so the exact shoreline lies somewhere between a land and water sample.  Bias the
    // boundary slightly toward the land sample: this deliberately sacrifices a narrow
    // strip of uncertain coast instead of painting warning/topographic land colours over
    // confirmed water.  The worker coastline and the Unity surface clipper use this same
    // interpolation value, keeping the visible coast band exactly on the fill boundary.
    internal static class AERISTerrainCoastlinePolicy
    {
        internal const float LandInsetFraction = 0.38f;

        internal static float CrossingFraction(bool water0, bool water1)
        {
            if (water0 == water1) return 0.5f;
            return water0 ? 1f - LandInsetFraction : LandInsetFraction;
        }

        internal static float Interpolate(float value0, float value1,
            bool water0, bool water1)
        {
            float t = CrossingFraction(water0, water1);
            return value0 + (value1 - value0) * t;
        }
    }
}
