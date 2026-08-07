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
        // Operation Health Step 2: smooth only the sub-cell crossing location. The
        // source 129x129 land/water class sign never changes, so islands and coastline
        // connectivity remain exactly under the persisted Candidate11 authority.
        internal const float PresentationSmoothingBlend = 0.65f;
        internal const float PresentationMinimumBoundaryMagnitude = 0.20f;

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

        internal static float[] BuildPresentationBoundaryField(byte[] flags,
            int resolution)
        {
            if (flags == null || resolution < 2 ||
                flags.Length != resolution * resolution) return new float[0];
            var field = new float[flags.Length];
            for (int row = 0; row < resolution; row++)
            {
                for (int column = 0; column < resolution; column++)
                {
                    int index = row * resolution + column;
                    byte own = flags[index];
                    if (own == 0)
                    {
                        field[index] = 0f;
                        continue;
                    }
                    float rawSign = own == 2 ? -1f : 1f;
                    float weighted = 0f;
                    float weight = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int py = row + dy;
                        if (py < 0 || py >= resolution) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = column + dx;
                            if (px < 0 || px >= resolution) continue;
                            byte neighbour = flags[py * resolution + px];
                            if (neighbour == 0) continue;
                            float sign = neighbour == 2 ? -1f : 1f;
                            int kernel = dx == 0 && dy == 0 ? 4 :
                                (dx == 0 || dy == 0 ? 2 : 1);
                            weighted += sign * kernel;
                            weight += kernel;
                        }
                    }
                    float filtered = weight <= 0f ? rawSign : weighted / weight;
                    // Sign preservation is a hard topology rule. Even when the local
                    // majority is the opposite class, only confidence magnitude changes.
                    float aligned = rawSign * filtered;
                    float magnitude = (float)System.Math.Max(
                        PresentationMinimumBoundaryMagnitude,
                        System.Math.Min(1.0, aligned));
                    field[index] = rawSign * magnitude;
                }
            }
            return field;
        }

        internal static float PresentationCrossingFraction(bool water0,
            bool water1, float scalar0, float scalar1)
        {
            float golden = CrossingFraction(water0, water1);
            if (water0 == water1 || float.IsNaN(scalar0) ||
                float.IsNaN(scalar1) || float.IsInfinity(scalar0) ||
                float.IsInfinity(scalar1) || scalar0 * scalar1 >= 0f)
                return golden;
            float denominator = scalar0 - scalar1;
            if (System.Math.Abs(denominator) <= 0.000001f) return golden;
            float zero = scalar0 / denominator;
            if (float.IsNaN(zero) || float.IsInfinity(zero)) return golden;
            zero = (float)System.Math.Max(0.18, System.Math.Min(0.82, zero));
            float blended = golden + (zero - golden) *
                PresentationSmoothingBlend;
            return (float)System.Math.Max(0.24, System.Math.Min(0.76, blended));
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
