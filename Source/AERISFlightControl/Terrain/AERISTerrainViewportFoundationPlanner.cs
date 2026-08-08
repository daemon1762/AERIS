using System;
using System.Collections.Generic;
using UnityEngine;
using AERISFlightControl.Settings;

namespace AERISFlightControl.Terrain
{
    // CP3 Gate 3.1: derives the coarse terrain foundation from the actual ND projection
    // instead of a centre-radius square. The returned set is deliberately conservative:
    // every sampled viewport tile receives a one-tile guard ring so TRACK UP rotation,
    // aspect-correct window coverage and the lower aircraft anchor cannot expose an
    // unplanned wedge between replans.
    internal sealed class AERISTerrainViewportFoundationPlan
    {
        internal AERISTerrainTileKey[] GlobalKeys = new AERISTerrainTileKey[0];
        internal AERISTerrainTileKey[] FarKeys = new AERISTerrainTileKey[0];
        internal int SampleColumns;
        internal int SampleRows;
        internal int GuardRingTiles;
        internal bool TrackUp;
        internal double HeadingDeg;
        internal double RangeMeters;

        internal int TotalKeys
        {
            get { return GlobalKeys.Length + FarKeys.Length; }
        }
    }

    internal static class AERISTerrainViewportFoundationPlanner
    {
        internal const int GuardRingTiles = 1;
        // Enlarged windows now expose genuinely more geography. These bounds remain
        // finite, but are high enough for the maximum AERIS window at the 160 km scale.
        internal const int MaximumGlobalKeys = 128;
        internal const int MaximumFarKeys = 1024;
        const double SampleSpacingTileFraction = 0.42;

        internal static AERISTerrainViewportFoundationPlan Build(CelestialBody body,
            string environmentHash, double centerLatitudeDeg,
            double centerLongitudeDeg, double rangeMeters, double horizontalMeters,
            double verticalMeters, double headingDeg, bool trackUp, float anchorGuiV,
            AERISTerrainRenderTargetOrientation orientation)
        {
            var output = new AERISTerrainViewportFoundationPlan
            {
                TrackUp = trackUp,
                HeadingDeg = NormalizeHeading(headingDeg),
                RangeMeters = Math.Max(1.0, rangeMeters),
                GuardRingTiles = GuardRingTiles
            };
            if (body == null || body.Radius <= 0.0 ||
                string.IsNullOrEmpty(environmentHash)) return output;

            AERISNdMapProjection projection = AERISNdMapProjection.CreateWithExtents(body,
                centerLatitudeDeg, centerLongitudeDeg, horizontalMeters, verticalMeters,
                (float)output.HeadingDeg, trackUp, anchorGuiV, orientation);

            int farColumns, farRows;
            output.FarKeys = Collect(body, environmentHash, projection,
                AERISTerrainTileLod.Far, MaximumFarKeys,
                out farColumns, out farRows);
            int globalColumns, globalRows;
            output.GlobalKeys = Collect(body, environmentHash, projection,
                AERISTerrainTileLod.Global, MaximumGlobalKeys,
                out globalColumns, out globalRows);
            output.SampleColumns = Math.Max(farColumns, globalColumns);
            output.SampleRows = Math.Max(farRows, globalRows);
            return output;
        }

        static AERISTerrainTileKey[] Collect(CelestialBody body,
            string environmentHash, AERISNdMapProjection projection,
            AERISTerrainTileLod lod, int maximumKeys,
            out int sampleColumns, out int sampleRows)
        {
            double tileMeters = AERISTerrainTileFormat.NominalCellMeters(lod) *
                Math.Max(1, AERISTerrainTileFormat.Resolution(lod) - 1);
            double spacingMeters = Math.Max(250.0,
                tileMeters * SampleSpacingTileFraction);
            sampleColumns = Clamp((int)Math.Ceiling(
                projection.HorizontalMeters / spacingMeters), 4, 96);
            sampleRows = Clamp((int)Math.Ceiling(
                projection.VerticalMeters / spacingMeters), 4, 96);

            var sampled = new Dictionary<string, AERISTerrainTileKey>(
                StringComparer.Ordinal);
            for (int row = 0; row <= sampleRows; row++)
            {
                float v = row / (float)Math.Max(1, sampleRows);
                for (int column = 0; column <= sampleColumns; column++)
                {
                    float u = column / (float)Math.Max(1, sampleColumns);
                    projection.UnprojectGuiToLatitudeLongitude(u, v,
                        out double latitudeDeg, out double longitudeDeg);
                    AERISTerrainTileKey key = AERISTerrainTileSystem.KeyForPoint(
                        body, environmentHash, lod, latitudeDeg, longitudeDeg);
                    sampled[key.StableId] = key;
                }
            }

            var guarded = new Dictionary<string, AERISTerrainTileKey>(
                StringComparer.Ordinal);
            int latitudeCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
            int longitudeCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
            foreach (AERISTerrainTileKey key in sampled.Values)
            {
                for (int dy = -GuardRingTiles; dy <= GuardRingTiles; dy++)
                {
                    int latitudeIndex = key.LatitudeIndex + dy;
                    if (latitudeIndex < 0 || latitudeIndex >= latitudeCount) continue;
                    for (int dx = -GuardRingTiles; dx <= GuardRingTiles; dx++)
                    {
                        int longitudeIndex = WrapIndex(key.LongitudeIndex + dx,
                            longitudeCount);
                        var guardedKey = new AERISTerrainTileKey(key.BodyName,
                            key.BodyRadiusMillimetres / 1000.0, key.EnvironmentHash,
                            key.Lod, latitudeIndex, longitudeIndex);
                        guarded[guardedKey.StableId] = guardedKey;
                    }
                }
            }

            var values = new List<AERISTerrainTileKey>(guarded.Values);
            values.Sort((a, b) => string.CompareOrdinal(a.StableId, b.StableId));
            if (values.Count > maximumKeys)
            {
                // The limits are intentionally above the maximum expected 160 km Kerbin
                // viewport. Fail closed to a deterministic prefix only for pathological
                // bodies/ranges; telemetry and static acceptance make this visible.
                values.RemoveRange(maximumKeys, values.Count - maximumKeys);
            }
            return values.ToArray();
        }

        static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        static int WrapIndex(int value, int count)
        {
            if (count <= 0) return 0;
            int wrapped = value % count;
            return wrapped < 0 ? wrapped + count : wrapped;
        }

        static double NormalizeHeading(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0.0;
            value %= 360.0;
            return value < 0.0 ? value + 360.0 : value;
        }
    }
}
