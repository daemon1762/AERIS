using System;

namespace AERISFlightControl.Terrain
{
    // R041 pure CLR height-only evaluator for stock PQSLandControl.OnVertexBuildHeight.
    // HARD RULE: no Unity/KSP/runtime-object types in this file.
    internal static class AERIS39LandControlPureCpuExact
    {
        internal sealed class LerpRangeSnapshot
        {
            internal readonly double StartStart;
            internal readonly double StartEnd;
            internal readonly double EndStart;
            internal readonly double EndEnd;
            internal readonly double StartDelta;
            internal readonly double EndDelta;

            internal LerpRangeSnapshot(
                double startStart,
                double startEnd,
                double endStart,
                double endEnd,
                double startDelta,
                double endDelta)
            {
                StartStart = startStart;
                StartEnd = startEnd;
                EndStart = endStart;
                EndEnd = endEnd;
                StartDelta = startDelta;
                EndDelta = endDelta;
            }

            internal double Evaluate(double point)
            {
                if (point <= StartStart || point >= EndEnd)
                    return 0d;
                if (point < StartEnd)
                    return (point - StartStart) * StartDelta;
                if (point <= EndStart)
                    return 1d;
                if (point < EndEnd)
                    return 1d - ((point - EndStart) * EndDelta);
                return 0d;
            }
        }

        internal sealed class LandClassSnapshot
        {
            internal readonly LerpRangeSnapshot AltitudeRange;
            internal readonly LerpRangeSnapshot LatitudeRange;
            internal readonly bool LatitudeDouble;
            internal readonly LerpRangeSnapshot LatitudeDoubleRange;
            internal readonly LerpRangeSnapshot LongitudeRange;
            internal readonly float CoverageBlend;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot CoverageSimplex;
            internal readonly double MinimumRealHeight;
            internal readonly double AlterRealHeight;

            internal LandClassSnapshot(
                LerpRangeSnapshot altitudeRange,
                LerpRangeSnapshot latitudeRange,
                bool latitudeDouble,
                LerpRangeSnapshot latitudeDoubleRange,
                LerpRangeSnapshot longitudeRange,
                float coverageBlend,
                AERISR039MinmusPureCpuExact.SimplexSnapshot coverageSimplex,
                double minimumRealHeight,
                double alterRealHeight)
            {
                AltitudeRange = altitudeRange ?? throw new ArgumentNullException("altitudeRange");
                LatitudeRange = latitudeRange ?? throw new ArgumentNullException("latitudeRange");
                LatitudeDouble = latitudeDouble;
                LatitudeDoubleRange = latitudeDoubleRange ?? throw new ArgumentNullException("latitudeDoubleRange");
                LongitudeRange = longitudeRange ?? throw new ArgumentNullException("longitudeRange");
                CoverageBlend = coverageBlend;
                CoverageSimplex = coverageSimplex ?? throw new ArgumentNullException("coverageSimplex");
                MinimumRealHeight = minimumRealHeight;
                AlterRealHeight = alterRealHeight;
            }
        }

        internal sealed class OpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly bool UseHeightMap;
            internal readonly AERIS39MapSoPureCpuExact.MapSnapshot HeightMap;
            internal readonly float VHeightMax;
            internal readonly double SphereRadius;
            internal readonly double SphereSx;
            internal readonly double SphereSy;
            internal readonly float AltitudeBlend;
            internal readonly float LatitudeBlend;
            internal readonly float LongitudeBlend;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot AltitudeSimplex;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot LatitudeSimplex;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot LongitudeSimplex;
            internal readonly LandClassSnapshot[] LandClasses;

            internal OpSnapshot(
                bool useHeightMap,
                AERIS39MapSoPureCpuExact.MapSnapshot heightMap,
                float vHeightMax,
                double sphereRadius,
                double sphereSx,
                double sphereSy,
                float altitudeBlend,
                float latitudeBlend,
                float longitudeBlend,
                AERISR039MinmusPureCpuExact.SimplexSnapshot altitudeSimplex,
                AERISR039MinmusPureCpuExact.SimplexSnapshot latitudeSimplex,
                AERISR039MinmusPureCpuExact.SimplexSnapshot longitudeSimplex,
                LandClassSnapshot[] landClasses)
            {
                if (useHeightMap && heightMap == null)
                    throw new ArgumentNullException("heightMap");
                if (altitudeSimplex == null) throw new ArgumentNullException("altitudeSimplex");
                if (latitudeSimplex == null) throw new ArgumentNullException("latitudeSimplex");
                if (longitudeSimplex == null) throw new ArgumentNullException("longitudeSimplex");
                if (landClasses == null) throw new ArgumentNullException("landClasses");

                UseHeightMap = useHeightMap;
                HeightMap = heightMap;
                VHeightMax = vHeightMax;
                SphereRadius = sphereRadius;
                SphereSx = sphereSx;
                SphereSy = sphereSy;
                AltitudeBlend = altitudeBlend;
                LatitudeBlend = latitudeBlend;
                LongitudeBlend = longitudeBlend;
                AltitudeSimplex = altitudeSimplex;
                LatitudeSimplex = latitudeSimplex;
                LongitudeSimplex = longitudeSimplex;
                LandClasses = (LandClassSnapshot[])landClasses.Clone();
                for (int i = 0; i < LandClasses.Length; i++)
                    if (LandClasses[i] == null)
                        throw new ArgumentException("land class snapshot is null", "landClasses");
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height)
            {
                double vHeight;
                if (UseHeightMap)
                {
                    vHeight = (double)AERIS39MapSoPureCpuExact.GetPixelFloat(HeightMap, u, v);
                }
                else
                {
                    vHeight = (height - SphereRadius) / (double)VHeightMax;
                }

                double altitudeNoise = AERISR039MinmusPureCpuExact.SimplexNoise(
                    AltitudeSimplex, x, y, z, AltitudeSimplex.Persistence);
                vHeight = vHeight + ((double)AltitudeBlend * altitudeNoise);
                if (vHeight > 1d)
                    vHeight = 1d;

                double latitudeNoise = AERISR039MinmusPureCpuExact.SimplexNoise(
                    LatitudeSimplex, x, y, z, LatitudeSimplex.Persistence);
                double vLat = SphereSy + ((double)LatitudeBlend * latitudeNoise);
                if (vLat > 1d)
                    vLat = 1d;
                else if (vLat < 0d)
                    vLat = 0d;

                double longitudeNoise = AERISR039MinmusPureCpuExact.SimplexNoise(
                    LongitudeSimplex, x, y, z, LongitudeSimplex.Persistence);
                double vLon = SphereSx + ((double)LongitudeBlend * longitudeNoise);
                if (vLon > 1d)
                    vLon = 1d;
                else if (vLon < 0d)
                    vLon = 0d;

                int count = LandClasses.Length;
                double[] deltas = new double[count];
                bool[] active = new bool[count];
                double totalDelta = 0d;

                for (int i = 0; i < count; i++)
                {
                    LandClassSnapshot lc = LandClasses[i];
                    double altDelta = lc.AltitudeRange.Evaluate(vHeight);
                    double latDelta = lc.LatitudeRange.Evaluate(vLat);
                    if (lc.LatitudeDouble)
                    {
                        double doubleLatDelta = lc.LatitudeDoubleRange.Evaluate(vLat);
                        latDelta = Math.Max(doubleLatDelta, latDelta);
                    }
                    double lonDelta = lc.LongitudeRange.Evaluate(vLon);

                    double delta = altDelta * latDelta;
                    delta = delta * lonDelta;
                    double coverage = AERISR039MinmusPureCpuExact.SimplexNoiseNormalized(
                        lc.CoverageSimplex,
                        x,
                        y,
                        z,
                        lc.CoverageSimplex.Persistence);
                    double noisyDelta = delta * coverage;
                    delta = Lerp(delta, noisyDelta, (double)lc.CoverageBlend);

                    if (delta == 0d)
                        continue;

                    deltas[i] = delta;
                    active[i] = true;
                    totalDelta = totalDelta + delta;
                }

                for (int i = 0; i < count; i++)
                {
                    if (!active[i])
                        continue;

                    LandClassSnapshot lc = LandClasses[i];
                    double delta = deltas[i] / totalDelta;
                    if (delta > 0d)
                    {
                        if (lc.MinimumRealHeight != 0d &&
                            (height - SphereRadius < lc.MinimumRealHeight))
                        {
                            height = SphereRadius + (delta * lc.MinimumRealHeight);
                        }

                        height = height + (delta * lc.AlterRealHeight);
                    }
                }

                return height;
            }
        }

        internal static double Lerp(double v1, double v2, double dt)
        {
            // Stock PQSLandControl source order.
            return (v2 * dt) + (v1 * (1d - dt));
        }
    }
}
