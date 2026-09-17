using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AERISFlightControl.Terrain;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISTerrainCorridorIdentity
    {
        internal string DirectionStableId = string.Empty;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal string EnvironmentSignature = string.Empty;
        internal long AirfieldDatabaseRevision;
        internal long RunwayGeometryRevision;
        internal long TerrainRequestGeneration;
        internal long TerrainDatabaseGeneration;
        internal long ProducerGeneration;
    }

    internal sealed class AERISTerrainCorridorComputeInput
    {
        internal AERISTerrainCorridorIdentity Identity;
        internal double ThresholdLatitudeDeg;
        internal double ThresholdLongitudeDeg;
        internal double ThresholdElevationMeters;
        internal double InboundHeadingDeg;
        internal double MissedApproachHeadingDeg;
        internal double RequiredMissedApproachAltitudeMeters;
        internal AERISApproachPlanningLimits Limits;
        internal AERISTerrainCorridorReadResult ReadResult;
    }

    // Pure LAND-R2 analysis over copied read results. Scheduling and runtime capture
    // stay outside this type so worker execution cannot reach Unity or KSP objects.
    internal sealed class AERISApproachTerrainCorridorProducer
    {
        const string TerrainOnlyObstacleSignature =
            "R2_TERRAIN_ONLY_OBSTACLES_INCOMPLETE";

        sealed class OrderedQuery
        {
            internal int Ordinal;
            internal AERISTerrainCorridorQueryPoint Point;
            internal string TileStableId;
            internal int SourceLod;
        }

        internal static int PriorityRank(bool armed, bool selected,
            bool activeBody)
        {
            if (armed) return 0;
            if (selected) return 1;
            return activeBody ? 2 : 3;
        }

        internal static bool IdentityMatches(
            AERISTerrainCorridorIdentity expected,
            AERISTerrainCorridorIdentity current)
        {
            if (expected == null || current == null) return false;
            return string.Equals(expected.DirectionStableId,
                       current.DirectionStableId, StringComparison.Ordinal) &&
                   string.Equals(expected.BodyName, current.BodyName,
                       StringComparison.Ordinal) &&
                   expected.BodyRadiusMeters == current.BodyRadiusMeters &&
                   string.Equals(expected.EnvironmentSignature,
                       current.EnvironmentSignature, StringComparison.Ordinal) &&
                   expected.AirfieldDatabaseRevision ==
                       current.AirfieldDatabaseRevision &&
                   expected.RunwayGeometryRevision ==
                       current.RunwayGeometryRevision &&
                   expected.TerrainRequestGeneration ==
                       current.TerrainRequestGeneration &&
                   expected.TerrainDatabaseGeneration ==
                       current.TerrainDatabaseGeneration &&
                   expected.ProducerGeneration == current.ProducerGeneration;
        }

        internal static AERISApproachObstacleSnapshot AnalyzePure(
            AERISTerrainCorridorComputeInput input)
        {
            AERISApproachObstacleSnapshot snapshot = CreateFailClosedSnapshot(input);
            AERISTerrainCorridorIdentity identity = input == null ? null :
                input.Identity;
            AERISTerrainCorridorReadResult result = input == null ? null :
                input.ReadResult;
            AERISTerrainCorridorReadPlan plan = result == null ? null : result.Plan;

            StringBuilder signature = new StringBuilder();
            signature.Append("R2|");
            signature.Append(identity == null ? string.Empty :
                identity.DirectionStableId ?? string.Empty);
            signature.Append('|');
            signature.Append(identity == null ? string.Empty :
                identity.BodyName ?? string.Empty);
            signature.Append('|');
            signature.Append(identity == null ? string.Empty :
                identity.EnvironmentSignature ?? string.Empty);
            signature.Append('|');
            signature.Append((identity == null ? 0L :
                identity.TerrainDatabaseGeneration).ToString(
                    CultureInfo.InvariantCulture));

            AERISTerrainCorridorQueryPoint[] points = plan == null ? null :
                plan.QueryPoints;
            string[] mappings = plan == null ? null : plan.PointTileStableIds;
            int[] sourceLods = plan == null ? null : plan.PointSourceLods;
            bool structureValid = result != null && plan != null &&
                result.Tiles != null && points != null && points.Length > 0 &&
                mappings != null && mappings.Length == points.Length &&
                sourceLods != null && sourceLods.Length == points.Length;
            bool terrainComplete = structureValid && result.TerrainCoverageComplete &&
                plan.TerrainCoverageComplete;
            bool haveMissedPoint = false;
            bool missedPointsClear = true;
            int bestObservedLod = -1;

            if (structureValid)
            {
                List<OrderedQuery> ordered = new List<OrderedQuery>(points.Length);
                for (int i = 0; i < points.Length; i++)
                {
                    ordered.Add(new OrderedQuery
                    {
                        Ordinal = i,
                        Point = points[i],
                        TileStableId = mappings[i] ?? string.Empty,
                        SourceLod = sourceLods[i]
                    });
                }
                ordered.Sort(CompareQueries);

                for (int i = 0; i < ordered.Count; i++)
                {
                    OrderedQuery query = ordered[i];
                    bool missed = query.Point != null &&
                        query.Point.MissedApproach;
                    if (missed) haveMissedPoint = true;

                    AERISTerrainHeightTile tile;
                    double elevation;
                    if (!TrySampleQuery(result.Tiles, query, out tile,
                        out elevation))
                    {
                        terrainComplete = false;
                        if (missed) missedPointsClear = false;
                        continue;
                    }

                    long elevationMillimetres;
                    if (!TryElevationMillimetres(elevation,
                        out elevationMillimetres))
                    {
                        terrainComplete = false;
                        if (missed) missedPointsClear = false;
                        continue;
                    }

                    snapshot.Samples.Add(new AERISApproachObstacleSample
                    {
                        AlongTrackMeters = query.Point.AlongTrackMeters,
                        CrossTrackMeters = query.Point.CrossTrackMeters,
                        TopElevationMeters = elevation,
                        HorizontalRadiusMeters = 0.0,
                        IsTerrain = true,
                        SourceId = tile.Key.StableId
                    });

                    // AERISTerrainTileLod numeric values increase with fidelity.
                    // Despite the legacy Minimum* field name, retain the maximum
                    // numeric LOD used: the best observed fidelity in this snapshot.
                    if (query.SourceLod > bestObservedLod)
                        bestObservedLod = query.SourceLod;
                    signature.Append('|');
                    signature.Append(query.Point.Index.ToString(
                        CultureInfo.InvariantCulture));
                    signature.Append('|');
                    signature.Append(tile.Key.StableId);
                    signature.Append('|');
                    signature.Append(query.SourceLod.ToString(
                        CultureInfo.InvariantCulture));
                    signature.Append('|');
                    signature.Append(elevationMillimetres.ToString(
                        CultureInfo.InvariantCulture));

                    if (missed && !(elevation <
                        input.RequiredMissedApproachAltitudeMeters))
                        missedPointsClear = false;
                }
            }

            snapshot.MinimumTerrainSourceLod = bestObservedLod;
            snapshot.TerrainCoverageComplete = terrainComplete;
            snapshot.TerrainMissedApproachClear = haveMissedPoint &&
                missedPointsClear;
            snapshot.TerrainSignature = AERISTerrainHash.Fnv1A64Hex(
                signature.ToString());
            return snapshot;
        }

        static AERISApproachObstacleSnapshot CreateFailClosedSnapshot(
            AERISTerrainCorridorComputeInput input)
        {
            AERISTerrainCorridorIdentity identity = input == null ? null :
                input.Identity;
            return new AERISApproachObstacleSnapshot
            {
                Generation = identity == null ? 0L : identity.ProducerGeneration,
                DirectionStableId = identity == null ? string.Empty :
                    identity.DirectionStableId ?? string.Empty,
                BodyName = identity == null ? string.Empty :
                    identity.BodyName ?? string.Empty,
                EnvironmentSignature = identity == null ? string.Empty :
                    identity.EnvironmentSignature ?? string.Empty,
                AirfieldDatabaseRevision = identity == null ? 0L :
                    identity.AirfieldDatabaseRevision,
                RunwayGeometryRevision = identity == null ? 0L :
                    identity.RunwayGeometryRevision,
                TerrainRequestGeneration = identity == null ? 0L :
                    identity.TerrainRequestGeneration,
                TerrainDatabaseGeneration = identity == null ? 0L :
                    identity.TerrainDatabaseGeneration,
                BodyRadiusMeters = identity == null ? 0.0 :
                    identity.BodyRadiusMeters,
                MissedApproachMinimumAltitudeMeters = input == null ? 0.0 :
                    input.RequiredMissedApproachAltitudeMeters,
                TerrainCoverageComplete = false,
                ObstacleCoverageComplete = false,
                MinimumTerrainSourceLod = -1,
                TerrainMissedApproachClear = false,
                ObstacleSignature = TerrainOnlyObstacleSignature,
                CorridorComplete = false,
                MissedApproachClear = false
            };
        }

        static int CompareQueries(OrderedQuery left, OrderedQuery right)
        {
            int leftIndex = left.Point == null ? int.MaxValue : left.Point.Index;
            int rightIndex = right.Point == null ? int.MaxValue : right.Point.Index;
            int byIndex = leftIndex.CompareTo(rightIndex);
            return byIndex != 0 ? byIndex : left.Ordinal.CompareTo(right.Ordinal);
        }

        static bool TrySampleQuery(
            Dictionary<string, AERISTerrainHeightTile> tiles,
            OrderedQuery query,
            out AERISTerrainHeightTile tile,
            out double elevation)
        {
            tile = null;
            elevation = 0.0;
            if (query == null || query.Point == null ||
                string.IsNullOrEmpty(query.TileStableId) ||
                query.SourceLod < (int)AERISTerrainTileLod.Global ||
                query.SourceLod > (int)AERISTerrainTileLod.Land ||
                !IsFinite(query.Point.LatitudeDeg) ||
                !IsFinite(query.Point.LongitudeDeg) ||
                !IsFinite(query.Point.AlongTrackMeters) ||
                !IsFinite(query.Point.CrossTrackMeters) ||
                !tiles.TryGetValue(query.TileStableId, out tile) || tile == null ||
                !string.Equals(tile.Key.StableId, query.TileStableId,
                    StringComparison.Ordinal) ||
                !tile.SamplingComplete || tile.Quality < 100)
                return false;

            int resolution = tile.Resolution;
            if (resolution < 2 || resolution > 46340 ||
                tile.Elevation == null ||
                tile.Elevation.Length != resolution * resolution ||
                !IsFinite(tile.SouthLatitudeDeg) ||
                !IsFinite(tile.NorthLatitudeDeg) ||
                !IsFinite(tile.WestLongitudeDeg) ||
                !IsFinite(tile.EastLongitudeDeg) ||
                !(tile.NorthLatitudeDeg > tile.SouthLatitudeDeg) ||
                !(tile.EastLongitudeDeg > tile.WestLongitudeDeg) ||
                query.Point.LatitudeDeg < tile.SouthLatitudeDeg ||
                query.Point.LatitudeDeg > tile.NorthLatitudeDeg ||
                query.Point.LongitudeDeg < tile.WestLongitudeDeg ||
                query.Point.LongitudeDeg > tile.EastLongitudeDeg)
                return false;

            double x = (query.Point.LongitudeDeg - tile.WestLongitudeDeg) /
                (tile.EastLongitudeDeg - tile.WestLongitudeDeg) *
                (resolution - 1);
            double y = (query.Point.LatitudeDeg - tile.SouthLatitudeDeg) /
                (tile.NorthLatitudeDeg - tile.SouthLatitudeDeg) *
                (resolution - 1);
            x = Math.Max(0.0, Math.Min(resolution - 1, x));
            y = Math.Max(0.0, Math.Min(resolution - 1, y));
            int x0 = (int)Math.Floor(x);
            int y0 = (int)Math.Floor(y);
            int x1 = Math.Min(resolution - 1, x0 + 1);
            int y1 = Math.Min(resolution - 1, y0 + 1);
            double z00 = tile.Elevation[y0 * resolution + x0];
            double z10 = tile.Elevation[y0 * resolution + x1];
            double z01 = tile.Elevation[y1 * resolution + x0];
            double z11 = tile.Elevation[y1 * resolution + x1];
            if (!IsFinite(z00) || !IsFinite(z10) || !IsFinite(z01) ||
                !IsFinite(z11)) return false;

            double tx = x - x0;
            double ty = y - y0;
            double south = z00 + (z10 - z00) * tx;
            double north = z01 + (z11 - z01) * tx;
            elevation = south + (north - south) * ty;
            return IsFinite(elevation);
        }

        static bool TryElevationMillimetres(double elevation,
            out long elevationMillimetres)
        {
            elevationMillimetres = 0L;
            double scaled = elevation * 1000.0;
            if (!IsFinite(scaled) || scaled < long.MinValue ||
                scaled > long.MaxValue) return false;
            elevationMillimetres = (long)Math.Round(scaled,
                MidpointRounding.AwayFromZero);
            return true;
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
