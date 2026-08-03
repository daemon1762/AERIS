using System;

namespace AERISFlightControl.Terrain
{
    // CP3.5 Gate 4 Candidate 2 — CP3 Golden Cartographic Quality.
    //
    // Late CP3 Gate 4C is the visual-quality floor, not an optional HIGH-only effect.
    // The persistent terrain authority remains FAR REAL 33x33, but the worker may
    // reconstruct the *presentation* geometry exactly as CP3 Gate 4C did:
    //   FAR 33 -> VIRTUAL ROUTE 65
    //   FAR 33 -> VIRTUAL LOCAL 97
    // without additional PQS sampling.  This recovers the dense contour/coastline
    // cartographic appearance that Candidate 1 lost by leaving MIDDLE at native 33.
    //
    // HIGH adds bounded REAL 65 PQS refinement.  When a complete REAL65 tile exists,
    // the same pure-data worker path may reconstruct it to VIRTUAL129.  If REAL65 is
    // unavailable or safety-throttled, HIGH falls back to the CP3 Golden virtual path
    // instead of collapsing to blocky REAL33.
    internal enum AERISTerrainVirtualDetailLevel
    {
        FarDirect = 0,
        VirtualRoute = 1,
        VirtualLocal = 2
    }

    internal sealed class AERISTerrainVirtualDetailProfile
    {
        internal readonly AERISTerrainVirtualDetailLevel Level;
        internal readonly string Name;
        internal readonly int FallbackVirtualResolution;
        internal readonly int RefinedSourceResolution;
        internal readonly int RefinedVirtualResolution;
        internal readonly bool ReconstructVirtualGeometry;
        internal readonly float RenderTargetScale;

        internal AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel level,
            string name, int fallbackVirtualResolution, int refinedSourceResolution,
            int refinedVirtualResolution, bool reconstructVirtualGeometry,
            float renderTargetScale)
        {
            Level = level;
            Name = name ?? "CP3 GOLDEN FAR";
            FallbackVirtualResolution = Math.Max(AERISTerrainTileFormat.DefaultResolution,
                fallbackVirtualResolution);
            RefinedSourceResolution = Math.Max(AERISTerrainTileFormat.DefaultResolution,
                refinedSourceResolution);
            RefinedVirtualResolution = Math.Max(FallbackVirtualResolution,
                refinedVirtualResolution);
            ReconstructVirtualGeometry = reconstructVirtualGeometry;
            RenderTargetScale = Math.Max(1f, renderTargetScale);
        }
    }

    internal static class AERISTerrainVirtualDetailPolicy
    {
        internal const int LowRealResolution = 33;
        internal const int MiddleRealResolution = 33;
        internal const int MiddleVirtualResolution = 65;
        internal const int HighRealResolution = 65;
        internal const int Cp3GoldenRouteResolution = 65;
        internal const int Cp3GoldenLocalResolution = 97;
        internal const int HighVirtualResolution = 129;

        // LOW is deliberately not allowed to regress to Candidate-1's giant 33x33
        // polygon blocks at useful map ranges. At <=20 km it reuses the proven CP3
        // VIRTUAL LOCAL 97 reconstruction; at 20..80 km it uses VIRTUAL ROUTE 65.
        // Neither path performs extra PQS. 160 km keeps the late-CP3 FAR/Hi-DPI
        // presentation that is the accepted long-range visual reference.
        static readonly AERISTerrainVirtualDetailProfile LowGoldenLocal =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualLocal,
                "LOW CP3 GOLDEN VIRTUAL LOCAL 97", Cp3GoldenLocalResolution,
                HighRealResolution, Cp3GoldenLocalResolution, true, 1.00f);
        static readonly AERISTerrainVirtualDetailProfile LowGoldenRoute =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualRoute,
                "LOW CP3 GOLDEN VIRTUAL ROUTE 65", Cp3GoldenRouteResolution,
                HighRealResolution, Cp3GoldenRouteResolution, true, 1.00f);
        static readonly AERISTerrainVirtualDetailProfile LowGoldenFarHiDpi =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.FarDirect,
                "LOW CP3 GOLDEN FAR HI-DPI", LowRealResolution,
                HighRealResolution, HighRealResolution, true, 1.25f);

        // MIDDLE corresponds to the late-CP3 high-quality visual path: LOCAL-class
        // reconstruction close in, ROUTE-class farther out, with no quality-time PQS.
        static readonly AERISTerrainVirtualDetailProfile MiddleGoldenLocal =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualLocal,
                "MIDDLE CP3 GOLDEN VIRTUAL LOCAL 97", Cp3GoldenLocalResolution,
                HighRealResolution, Cp3GoldenLocalResolution, true, 1.25f);
        static readonly AERISTerrainVirtualDetailProfile MiddleGoldenRoute =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualRoute,
                "MIDDLE CP3 GOLDEN VIRTUAL ROUTE 65", MiddleVirtualResolution,
                HighRealResolution, MiddleVirtualResolution, true, 1.30f);
        static readonly AERISTerrainVirtualDetailProfile MiddleGoldenFar =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualRoute,
                "MIDDLE CP3 GOLDEN LONG RANGE 65", MiddleVirtualResolution,
                HighRealResolution, MiddleVirtualResolution, true, 1.30f);

        // HIGH always retains a CP3-quality full-map fallback.  A complete REAL65 tile
        // upgrades only that bounded tile to VIRTUAL129; the rest of the map never falls
        // back to blocky 33 merely because refinement is still building or throttled.
        static readonly AERISTerrainVirtualDetailProfile HighGoldenLocal =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualLocal,
                "HIGH CP3 GOLDEN 97 / REAL65 -> VIRTUAL129", Cp3GoldenLocalResolution,
                HighRealResolution, HighVirtualResolution, true, 1.50f);
        static readonly AERISTerrainVirtualDetailProfile HighGoldenRoute =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualLocal,
                "HIGH CP3 GOLDEN 65 / REAL65 -> VIRTUAL129", Cp3GoldenRouteResolution,
                HighRealResolution, HighVirtualResolution, true, 1.50f);

        internal static AERISTerrainVirtualDetailProfile Resolve(string qualityName,
            float rangeMeters)
        {
            float range = Math.Max(1000f, rangeMeters);
            bool high = string.Equals(qualityName, "HIGH",
                StringComparison.OrdinalIgnoreCase);
            bool middle = string.Equals(qualityName, "MIDDLE",
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(qualityName, "MEDIUM", StringComparison.OrdinalIgnoreCase);

            if (high)
                return range <= 20000f ? HighGoldenLocal : HighGoldenRoute;
            if (middle)
            {
                if (range <= 20000f) return MiddleGoldenLocal;
                if (range <= 80000f) return MiddleGoldenRoute;
                return MiddleGoldenFar;
            }
            if (range <= 20000f) return LowGoldenLocal;
            if (range <= 80000f) return LowGoldenRoute;
            return LowGoldenFarHiDpi;
        }

        internal static AERISTerrainHeightTile ReconstructFar(
            AERISTerrainHeightTile source, AERISTerrainVirtualDetailProfile profile)
        {
            if (source == null || profile == null ||
                source.Key.Lod != AERISTerrainTileLod.Far || source.Resolution < 2 ||
                source.Elevation == null || source.Flags == null ||
                !profile.ReconstructVirtualGeometry)
                return source;

            int targetResolution = profile.FallbackVirtualResolution;
            if (source.Resolution >= profile.RefinedSourceResolution &&
                profile.RefinedVirtualResolution > targetResolution)
                targetResolution = profile.RefinedVirtualResolution;

            // A higher real tile may remain after the pilot changes quality/range.
            // Downsample only when the selected presentation explicitly asks for less;
            // otherwise reconstruct to the CP3/129 target on the worker.
            if (targetResolution == source.Resolution) return source;
            return Resample(source, targetResolution);
        }

        static AERISTerrainHeightTile Resample(AERISTerrainHeightTile source,
            int targetResolution)
        {
            targetResolution = Math.Max(3, targetResolution);
            if (source == null || source.Resolution == targetResolution) return source;

            int count = targetResolution * targetResolution;
            var elevation = new float[count];
            var flags = new byte[count];
            float minimum = float.MaxValue;
            float maximum = float.MinValue;

            for (int row = 0; row < targetResolution; row++)
            {
                double v = row / (double)(targetResolution - 1);
                double sourceY = v * (source.Resolution - 1);
                int y0 = Math.Max(0, Math.Min(source.Resolution - 1,
                    (int)Math.Floor(sourceY)));
                int y1 = Math.Min(source.Resolution - 1, y0 + 1);
                float ty = (float)(sourceY - y0);
                for (int column = 0; column < targetResolution; column++)
                {
                    double u = column / (double)(targetResolution - 1);
                    double sourceX = u * (source.Resolution - 1);
                    int x0 = Math.Max(0, Math.Min(source.Resolution - 1,
                        (int)Math.Floor(sourceX)));
                    int x1 = Math.Min(source.Resolution - 1, x0 + 1);
                    float tx = (float)(sourceX - x0);
                    int targetIndex = row * targetResolution + column;

                    int i00 = y0 * source.Resolution + x0;
                    int i10 = y0 * source.Resolution + x1;
                    int i01 = y1 * source.Resolution + x0;
                    int i11 = y1 * source.Resolution + x1;
                    byte f00 = Flag(source, i00), f10 = Flag(source, i10);
                    byte f01 = Flag(source, i01), f11 = Flag(source, i11);

                    // Land/sea is categorical. Never average the class across a coast.
                    int nearestX = tx < 0.5f ? x0 : x1;
                    int nearestY = ty < 0.5f ? y0 : y1;
                    int nearestIndex = nearestY * source.Resolution + nearestX;
                    byte nearestFlag = Flag(source, nearestIndex);
                    if (nearestFlag == 0)
                    {
                        nearestFlag = FirstValidFlag(f00, f10, f01, f11);
                        if (nearestFlag == 0) continue;
                    }
                    flags[targetIndex] = nearestFlag;

                    bool sameClass = f00 == nearestFlag && f10 == nearestFlag &&
                        f01 == nearestFlag && f11 == nearestFlag;
                    float value;
                    if (sameClass && ValidSample(source, i00) &&
                        ValidSample(source, i10) && ValidSample(source, i01) &&
                        ValidSample(source, i11))
                    {
                        float a = Lerp(source.Elevation[i00], source.Elevation[i10], tx);
                        float b = Lerp(source.Elevation[i01], source.Elevation[i11], tx);
                        value = Lerp(a, b, ty);
                    }
                    else
                    {
                        value = NearestClassHeight(source, sourceX, sourceY,
                            nearestFlag, nearestIndex);
                    }
                    if (!Finite(value))
                    {
                        flags[targetIndex] = 0;
                        elevation[targetIndex] = 0f;
                        continue;
                    }
                    elevation[targetIndex] = value;
                    minimum = Math.Min(minimum, value);
                    maximum = Math.Max(maximum, value);
                }
            }

            return new AERISTerrainHeightTile
            {
                Key = source.Key,
                Resolution = targetResolution,
                SouthLatitudeDeg = source.SouthLatitudeDeg,
                NorthLatitudeDeg = source.NorthLatitudeDeg,
                WestLongitudeDeg = source.WestLongitudeDeg,
                EastLongitudeDeg = source.EastLongitudeDeg,
                MinimumElevationMeters = minimum == float.MaxValue ?
                    source.MinimumElevationMeters : minimum,
                MaximumElevationMeters = maximum == float.MinValue ?
                    source.MaximumElevationMeters : maximum,
                Elevation = elevation,
                Flags = flags,
                CreatedUtcTicks = source.CreatedUtcTicks,
                LastAccessSequence = source.LastAccessSequence,
                Quality = source.Quality,
                IsPreview = source.IsPreview,
                SamplingComplete = source.SamplingComplete,
                Source = source.Source,
                PqsConfigurationHash = source.PqsConfigurationHash,
                GameDataHash = source.GameDataHash,
                TerrainGenerationId = source.TerrainGenerationId
            };
        }

        static byte Flag(AERISTerrainHeightTile tile, int index)
        {
            return tile == null || tile.Flags == null || index < 0 ||
                index >= tile.Flags.Length ? (byte)0 : tile.Flags[index];
        }

        static byte FirstValidFlag(byte a, byte b, byte c, byte d)
        {
            if (a != 0) return a;
            if (b != 0) return b;
            if (c != 0) return c;
            return d;
        }

        static bool ValidSample(AERISTerrainHeightTile tile, int index)
        {
            return tile != null && tile.Elevation != null && tile.Flags != null &&
                index >= 0 && index < tile.Elevation.Length && index < tile.Flags.Length &&
                tile.Flags[index] != 0 && Finite(tile.Elevation[index]);
        }

        static float NearestClassHeight(AERISTerrainHeightTile tile, double sourceX,
            double sourceY, byte targetFlag, int fallbackIndex)
        {
            int cx = Math.Max(0, Math.Min(tile.Resolution - 1,
                (int)Math.Round(sourceX)));
            int cy = Math.Max(0, Math.Min(tile.Resolution - 1,
                (int)Math.Round(sourceY)));
            double bestDistance = double.MaxValue;
            float best = ValidSample(tile, fallbackIndex) ?
                tile.Elevation[fallbackIndex] : 0f;
            bool found = ValidSample(tile, fallbackIndex) &&
                tile.Flags[fallbackIndex] == targetFlag;
            for (int dy = -1; dy <= 1; dy++)
            {
                int y = cy + dy;
                if (y < 0 || y >= tile.Resolution) continue;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = cx + dx;
                    if (x < 0 || x >= tile.Resolution) continue;
                    int index = y * tile.Resolution + x;
                    if (!ValidSample(tile, index) || tile.Flags[index] != targetFlag)
                        continue;
                    double ddx = x - sourceX, ddy = y - sourceY;
                    double distance = ddx * ddx + ddy * ddy;
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    best = tile.Elevation[index];
                    found = true;
                }
            }
            return found ? best : float.NaN;
        }

        static float Lerp(float a, float b, float t)
        {
            return a + (b - a) * t;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
