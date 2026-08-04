using System;

namespace AERISFlightControl.Terrain
{
    // CP3 Gate 4C: Route/Local are presentation qualities reconstructed from the
    // authoritative FAR height field. They are not persistent terrain payload LODs.
    // Exact Route/Local/LAND tiles, when already available or LAND-demanded, remain
    // authoritative overlays and are never synthesized by this class.
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
        internal readonly int ReconstructionScale;
        internal readonly int MaximumResolution;
        internal readonly float RenderTargetScale;

        internal AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel level,
            string name, int reconstructionScale, int maximumResolution,
            float renderTargetScale)
        {
            Level = level;
            Name = name ?? "FAR DIRECT";
            ReconstructionScale = Math.Max(1, reconstructionScale);
            MaximumResolution = Math.Max(AERISTerrainTileFormat.DefaultResolution,
                maximumResolution);
            RenderTargetScale = Math.Max(1f, renderTargetScale);
        }
    }

    internal static class AERISTerrainVirtualDetailPolicy
    {
        static readonly AERISTerrainVirtualDetailProfile FarDirect =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.FarDirect,
                "FAR DIRECT", 1, 33, 1.0f);
        static readonly AERISTerrainVirtualDetailProfile VirtualRoute =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualRoute,
                "VIRTUAL ROUTE", 2, 65, 1.25f);
        static readonly AERISTerrainVirtualDetailProfile VirtualLocal =
            new AERISTerrainVirtualDetailProfile(AERISTerrainVirtualDetailLevel.VirtualLocal,
                "VIRTUAL LOCAL", 3, 97, 1.50f);

        internal static AERISTerrainVirtualDetailProfile Resolve(string qualityName,
            float rangeMeters)
        {
            float range = Math.Max(1000f, rangeMeters);
            bool land = string.Equals(qualityName, "LAND",
                StringComparison.OrdinalIgnoreCase);
            bool high = string.Equals(qualityName, "HIGH",
                StringComparison.OrdinalIgnoreCase);
            bool medium = string.Equals(qualityName, "MEDIUM",
                StringComparison.OrdinalIgnoreCase);
            // Do not spend local-class reconstruction where one FAR cell projects to
            // only a few screen pixels. Detail rises as the pilot zooms in.
            if ((land && range <= 40000f) || (high && range <= 20000f))
                return VirtualLocal;
            if ((land || high || medium) && range <= 80000f)
                return VirtualRoute;
            return FarDirect;
        }

        internal static AERISTerrainHeightTile ReconstructFar(
            AERISTerrainHeightTile source, AERISTerrainVirtualDetailProfile profile)
        {
            if (source == null || profile == null ||
                source.Key.Lod != AERISTerrainTileLod.Far ||
                profile.Level == AERISTerrainVirtualDetailLevel.FarDirect ||
                source.Resolution < 2 || source.Elevation == null || source.Flags == null)
                return source;

            int targetResolution = Math.Min(profile.MaximumResolution,
                (source.Resolution - 1) * profile.ReconstructionScale + 1);
            if (targetResolution <= source.Resolution) return source;

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

                    // Land/sea is categorical. Never average the class across a coast,
                    // because that reproduces the old land-colour bleed into water.
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
                        // At coastline/invalid boundaries use the nearest same-class
                        // authoritative sample. This is deliberately conservative: Gate 4C
                        // may smooth known FAR data but must not invent a coast or mountain.
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
                TerrainGenerationId = source.TerrainGenerationId,
                HighDensityCoastlineResolution =
                    source.HighDensityCoastlineResolution,
                HighDensityCoastlineSegments =
                    source.HighDensityCoastlineSegments == null ? null :
                    (float[])source.HighDensityCoastlineSegments.Clone()
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
