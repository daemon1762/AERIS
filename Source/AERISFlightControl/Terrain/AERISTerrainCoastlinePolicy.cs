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

        // AERIS23 fallback for a visible FAR coastal tile whose persisted Candidate11
        // 129x129 classification payload is not resident. This does NOT create a denser
        // terrain surface and is never persisted. It only subdivides coarse parent cells
        // that already contain a proven land/water transition. Uniform parent cells remain
        // bit-for-bit uniform and every original 33x33 grid vertex keeps its exact class.
        // The subdivision follows the same two-triangle topology as the FAR terrain mesh,
        // so it cannot introduce a boundary across a parent cell that had none.
        internal static byte[] BuildTopologyPreservingCoastalPresentationMask(
            AERISTerrainHeightTile tile, int targetResolution)
        {
            if (tile == null || tile.Resolution < 2 || tile.Flags == null ||
                tile.Flags.Length < tile.Resolution * tile.Resolution ||
                targetResolution < tile.Resolution ||
                (targetResolution - 1) % (tile.Resolution - 1) != 0 ||
                !AERISTerrainCoastlineExtractor.ContainsLandWaterBoundary(tile))
                return new byte[0];

            int sourceResolution = tile.Resolution;
            int factor = (targetResolution - 1) / (sourceResolution - 1);
            if (factor <= 1) return new byte[0];
            var output = new byte[targetResolution * targetResolution];

            for (int row = 0; row < targetResolution; row++)
            {
                int parentRow = System.Math.Min(sourceResolution - 2, row / factor);
                float fy = (row - parentRow * factor) / (float)factor;
                for (int column = 0; column < targetResolution; column++)
                {
                    int targetIndex = row * targetResolution + column;
                    // Original source vertices are immutable topology anchors.
                    if (row % factor == 0 && column % factor == 0)
                    {
                        int sy = System.Math.Min(sourceResolution - 1, row / factor);
                        int sx = System.Math.Min(sourceResolution - 1, column / factor);
                        output[targetIndex] = tile.Flags[sy * sourceResolution + sx];
                        continue;
                    }

                    int parentColumn = System.Math.Min(sourceResolution - 2,
                        column / factor);
                    float fx = (column - parentColumn * factor) / (float)factor;
                    int a = parentRow * sourceResolution + parentColumn;
                    int b = a + 1;
                    int c = a + sourceResolution;
                    int d = c + 1;
                    byte fa = tile.Flags[a], fb = tile.Flags[b];
                    byte fc = tile.Flags[c], fd = tile.Flags[d];

                    // Unknown source topology is never synthesized. Leave these subpoints
                    // invalid so the existing coarse/base presentation remains authority.
                    if (fa == 0 || fb == 0 || fc == 0 || fd == 0)
                    {
                        output[targetIndex] = 0;
                        continue;
                    }

                    // The overwhelmingly common all-land/all-water parent takes the
                    // constant fast path and therefore incurs no synthetic boundary.
                    if (fa == fb && fa == fc && fa == fd)
                    {
                        output[targetIndex] = fa;
                        continue;
                    }

                    float sa = ClassSign(fa), sb = ClassSign(fb);
                    float sc = ClassSign(fc), sd = ClassSign(fd);
                    float scalar;
                    // Match BuildTriangleIndices: first triangle a,c,b; second b,c,d.
                    if (fx + fy <= 1f)
                        scalar = sa * (1f - fx - fy) + sb * fx + sc * fy;
                    else
                        scalar = sb * (1f - fy) + sc * (1f - fx) +
                            sd * (fx + fy - 1f);
                    output[targetIndex] = System.Math.Abs(scalar) <= 0.00001f ?
                        NearestPresentationFlag(fa, fb, fc, fd, fx, fy) :
                        (scalar < 0f ? (byte)2 : (byte)1);
                }
            }
            return output;
        }

        static float ClassSign(byte flag)
        {
            return flag == 2 ? -1f : 1f;
        }

        static byte NearestPresentationFlag(byte a, byte b, byte c, byte d,
            float fx, float fy)
        {
            byte result = fx < 0.5f ? (fy < 0.5f ? a : c) :
                (fy < 0.5f ? b : d);
            if (result != 0) return result;
            if (a != 0) return a;
            if (b != 0) return b;
            if (c != 0) return c;
            return d;
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
