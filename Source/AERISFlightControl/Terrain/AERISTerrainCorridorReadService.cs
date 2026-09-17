using System;
using System.Collections.Generic;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    internal sealed class AERISTerrainCorridorQueryPoint
    {
        internal int Index;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double AlongTrackMeters;
        internal double CrossTrackMeters;
        internal bool MissedApproach;
    }

    internal sealed class AERISTerrainCorridorReadPlan
    {
        internal string DirectionStableId = string.Empty;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal string EnvironmentSignature = string.Empty;
        internal string GameDataHash = string.Empty;
        internal long AirfieldDatabaseRevision;
        internal long RunwayGeometryRevision;
        internal long TerrainRequestGeneration;
        internal long TerrainDatabaseGeneration;
        internal long ProducerGeneration;
        internal AERISTerrainCorridorQueryPoint[] QueryPoints =
            new AERISTerrainCorridorQueryPoint[0];
        internal AERISTerrainTileKey[] TileKeys = new AERISTerrainTileKey[0];
        internal long[] TileGenerationUtcTicks = new long[0];
        internal string[] PointTileStableIds = new string[0];
        internal int[] PointSourceLods = new int[0];
        internal bool TerrainCoverageComplete;
    }

    internal sealed class AERISTerrainCorridorReadResult
    {
        internal AERISTerrainCorridorReadPlan Plan;
        internal Dictionary<string, AERISTerrainHeightTile> Tiles =
            new Dictionary<string, AERISTerrainHeightTile>(StringComparer.Ordinal);
        internal bool TerrainCoverageComplete;
        internal string FailureReason = string.Empty;
    }

    // Read-only LAND-R2 bridge over the accepted preload DB and warm cache. Plan capture
    // performs metadata lookups only; payload I/O/decode stays on the shared scheduler.
    internal sealed class AERISTerrainCorridorReadService
    {
        static readonly AERISTerrainTileLod[] FidelityOrder =
        {
            AERISTerrainTileLod.Land,
            AERISTerrainTileLod.Local,
            AERISTerrainTileLod.Route,
            AERISTerrainTileLod.Far
        };

        readonly AERISTerrainPreloadDatabase database;
        readonly AERISTerrainWarmTileCache warm;
        readonly AERISPerformanceRuntime performance;

        internal AERISTerrainCorridorReadService(
            AERISTerrainPreloadDatabase database,
            AERISTerrainWarmTileCache warm,
            AERISPerformanceRuntime performance)
        {
            this.database = database;
            this.warm = warm;
            this.performance = performance;
        }

        internal bool TryCapturePlan(
            string directionStableId,
            string bodyName,
            double bodyRadiusMeters,
            string environmentSignature,
            string gameDataHash,
            long airfieldDatabaseRevision,
            long runwayGeometryRevision,
            long producerGeneration,
            IList<AERISTerrainCorridorQueryPoint> queryPoints,
            out AERISTerrainCorridorReadPlan plan,
            out string failureReason)
        {
            plan = null;
            failureReason = string.Empty;
            if (database == null)
            {
                failureReason = "TERRAIN_DATABASE_UNAVAILABLE";
                return false;
            }
            if (queryPoints == null || queryPoints.Count == 0)
            {
                failureReason = "QUERY_POINTS_EMPTY";
                return false;
            }
            if (!IsFinite(bodyRadiusMeters) || bodyRadiusMeters <= 0.0)
            {
                failureReason = "BODY_RADIUS_INVALID";
                return false;
            }

            var copiedPoints = new AERISTerrainCorridorQueryPoint[queryPoints.Count];
            var pointTileStableIds = new string[queryPoints.Count];
            var pointSourceLods = new int[queryPoints.Count];
            var tileKeys = new List<AERISTerrainTileKey>();
            var tileGenerationUtcTicks = new List<long>();
            var generationByTileId = new Dictionary<string, long>(
                StringComparer.Ordinal);
            var seenTileIds = new HashSet<string>(StringComparer.Ordinal);
            bool coverageComplete = true;
            long requestGeneration = database.RequestGeneration;

            for (int i = 0; i < queryPoints.Count; i++)
            {
                AERISTerrainCorridorQueryPoint source = queryPoints[i];
                if (source == null || !IsFinite(source.LatitudeDeg) ||
                    !IsFinite(source.LongitudeDeg) ||
                    !IsFinite(source.AlongTrackMeters) ||
                    !IsFinite(source.CrossTrackMeters))
                {
                    failureReason = "QUERY_POINT_INVALID";
                    return false;
                }

                copiedPoints[i] = ClonePoint(source);
                pointTileStableIds[i] = string.Empty;
                pointSourceLods[i] = -1;
                for (int lodIndex = 0; lodIndex < FidelityOrder.Length; lodIndex++)
                {
                    AERISTerrainTileLod lod = FidelityOrder[lodIndex];
                    AERISTerrainTileKey key = KeyForPoint(bodyName, bodyRadiusMeters,
                        environmentSignature, lod, source.LatitudeDeg,
                        source.LongitudeDeg);
                    if (!database.Contains(key)) continue;
                    long generationUtcTicks;
                    if (!database.TryGetGenerationUtcTicks(key,
                        out generationUtcTicks)) continue;
                    pointTileStableIds[i] = key.StableId;
                    pointSourceLods[i] = (int)lod;
                    long previousGeneration;
                    if (generationByTileId.TryGetValue(key.StableId,
                        out previousGeneration) &&
                        previousGeneration != generationUtcTicks)
                    {
                        failureReason = "TERRAIN_METADATA_CHANGED";
                        return false;
                    }
                    generationByTileId[key.StableId] = generationUtcTicks;
                    if (seenTileIds.Add(key.StableId))
                    {
                        tileKeys.Add(key);
                        tileGenerationUtcTicks.Add(generationUtcTicks);
                    }
                    break;
                }
                if (string.IsNullOrEmpty(pointTileStableIds[i]))
                    coverageComplete = false;
            }

            var captured = new AERISTerrainCorridorReadPlan
            {
                DirectionStableId = directionStableId ?? string.Empty,
                BodyName = bodyName ?? string.Empty,
                BodyRadiusMeters = bodyRadiusMeters,
                EnvironmentSignature = environmentSignature ?? string.Empty,
                GameDataHash = gameDataHash ?? string.Empty,
                AirfieldDatabaseRevision = airfieldDatabaseRevision,
                RunwayGeometryRevision = runwayGeometryRevision,
                TerrainRequestGeneration = requestGeneration,
                TerrainDatabaseGeneration = database.DatabaseGeneration,
                ProducerGeneration = producerGeneration,
                QueryPoints = copiedPoints,
                TileKeys = tileKeys.ToArray(),
                TileGenerationUtcTicks = tileGenerationUtcTicks.ToArray(),
                PointTileStableIds = pointTileStableIds,
                PointSourceLods = pointSourceLods,
                TerrainCoverageComplete = coverageComplete
            };
            if (!IsPlanCurrent(captured))
            {
                failureReason = "TERRAIN_METADATA_CHANGED";
                return false;
            }
            plan = captured;
            return true;
        }

        internal bool IsPlanCurrent(AERISTerrainCorridorReadPlan plan)
        {
            if (database == null || plan == null || plan.TileKeys == null ||
                plan.TileGenerationUtcTicks == null ||
                plan.TileKeys.Length != plan.TileGenerationUtcTicks.Length ||
                database.RequestGeneration != plan.TerrainRequestGeneration)
                return false;
            for (int i = 0; i < plan.TileKeys.Length; i++)
            {
                long currentGeneration;
                if (!database.TryGetGenerationUtcTicks(plan.TileKeys[i],
                    out currentGeneration) ||
                    currentGeneration != plan.TileGenerationUtcTicks[i])
                    return false;
            }
            return true;
        }

        internal bool SubmitRead(
            AERISTerrainCorridorReadPlan plan,
            AERISRuntimeGenerationStamp stamp,
            Action<AERISTerrainCorridorReadResult> commit)
        {
            if (plan == null || commit == null || plan.TileKeys == null ||
                plan.TileKeys.Length == 0) return false;
            AERISPerformanceRuntime runtime = performance ??
                AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return false;

            AERISTerrainCorridorReadPlan submittedPlan = ClonePlan(plan);
            string schedulerKey = "land-r2-terrain-read|" +
                submittedPlan.DirectionStableId + "|" +
                submittedPlan.ProducerGeneration;
            return runtime.Scheduler.SubmitRequired(AERISRuntimeLane.SafetyLand,
                schedulerKey, stamp,
                delegate(AERISRuntimeJobContext context)
                {
                    if (!IsPlanCurrent(submittedPlan))
                        return FailedResult(submittedPlan,
                            "TERRAIN_REQUIRED_TILE_CHANGED");
                    var loaded = new Dictionary<string, AERISTerrainHeightTile>(
                        StringComparer.Ordinal);
                    bool loadedAny = database.TryLoadBatch(submittedPlan.TileKeys,
                        warm, loaded, new AERISTerrainPreloadTelemetry(),
                        submittedPlan.GameDataHash);
                    AERISTerrainCorridorReadResult result = BuildReadResult(
                        submittedPlan, loaded, loadedAny);
                    if (!IsPlanCurrent(submittedPlan))
                    {
                        result.Tiles.Clear();
                        result.TerrainCoverageComplete = false;
                        result.FailureReason = "TERRAIN_REQUIRED_TILE_CHANGED";
                    }
                    return result;
                },
                delegate(object value)
                {
                    // Null is the scheduler's terminal signal for stale, cancelled or
                    // failed required work. Preserve it so the producer retires credit.
                    commit(value as AERISTerrainCorridorReadResult);
                }, false);
        }

        static AERISTerrainCorridorReadResult BuildReadResult(
            AERISTerrainCorridorReadPlan plan,
            IDictionary<string, AERISTerrainHeightTile> loaded,
            bool loadedAny)
        {
            var result = new AERISTerrainCorridorReadResult
            {
                Plan = plan,
                TerrainCoverageComplete = plan != null &&
                    plan.TerrainCoverageComplete,
                FailureReason = string.Empty
            };
            if (plan == null || plan.TileKeys == null ||
                plan.TileGenerationUtcTicks == null ||
                plan.TileKeys.Length != plan.TileGenerationUtcTicks.Length)
            {
                result.TerrainCoverageComplete = false;
                result.FailureReason = "TERRAIN_READ_PLAN_INVALID";
                return result;
            }

            bool generationMismatch = false;
            for (int i = 0; i < plan.TileKeys.Length; i++)
            {
                AERISTerrainTileKey key = plan.TileKeys[i];
                AERISTerrainHeightTile tile;
                if (loaded == null || !loaded.TryGetValue(key.StableId, out tile) ||
                    tile == null) continue;
                if (tile.CreatedUtcTicks != plan.TileGenerationUtcTicks[i])
                {
                    generationMismatch = true;
                    break;
                }
                result.Tiles[key.StableId] = tile.CloneImmutable();
            }
            if (generationMismatch)
            {
                result.Tiles.Clear();
                result.TerrainCoverageComplete = false;
                result.FailureReason = "TERRAIN_TILE_GENERATION_MISMATCH";
            }
            else if (!loadedAny || result.Tiles.Count != plan.TileKeys.Length)
            {
                result.TerrainCoverageComplete = false;
                result.FailureReason = "TERRAIN_READ_INCOMPLETE";
            }
            return result;
        }

        static AERISTerrainCorridorReadResult FailedResult(
            AERISTerrainCorridorReadPlan plan, string failureReason)
        {
            return new AERISTerrainCorridorReadResult
            {
                Plan = plan,
                TerrainCoverageComplete = false,
                FailureReason = failureReason ?? string.Empty
            };
        }

        static AERISTerrainCorridorQueryPoint ClonePoint(
            AERISTerrainCorridorQueryPoint source)
        {
            return new AERISTerrainCorridorQueryPoint
            {
                Index = source.Index,
                LatitudeDeg = source.LatitudeDeg,
                LongitudeDeg = source.LongitudeDeg,
                AlongTrackMeters = source.AlongTrackMeters,
                CrossTrackMeters = source.CrossTrackMeters,
                MissedApproach = source.MissedApproach
            };
        }

        static AERISTerrainCorridorReadPlan ClonePlan(
            AERISTerrainCorridorReadPlan source)
        {
            var points = new AERISTerrainCorridorQueryPoint[
                source.QueryPoints == null ? 0 : source.QueryPoints.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = source.QueryPoints[i] == null ? null :
                    ClonePoint(source.QueryPoints[i]);
            return new AERISTerrainCorridorReadPlan
            {
                DirectionStableId = source.DirectionStableId ?? string.Empty,
                BodyName = source.BodyName ?? string.Empty,
                BodyRadiusMeters = source.BodyRadiusMeters,
                EnvironmentSignature = source.EnvironmentSignature ?? string.Empty,
                GameDataHash = source.GameDataHash ?? string.Empty,
                AirfieldDatabaseRevision = source.AirfieldDatabaseRevision,
                RunwayGeometryRevision = source.RunwayGeometryRevision,
                TerrainRequestGeneration = source.TerrainRequestGeneration,
                TerrainDatabaseGeneration = source.TerrainDatabaseGeneration,
                ProducerGeneration = source.ProducerGeneration,
                QueryPoints = points,
                TileKeys = source.TileKeys == null ? new AERISTerrainTileKey[0] :
                    (AERISTerrainTileKey[])source.TileKeys.Clone(),
                TileGenerationUtcTicks = source.TileGenerationUtcTicks == null ?
                    new long[0] : (long[])source.TileGenerationUtcTicks.Clone(),
                PointTileStableIds = source.PointTileStableIds == null ? new string[0] :
                    (string[])source.PointTileStableIds.Clone(),
                PointSourceLods = source.PointSourceLods == null ? new int[0] :
                    (int[])source.PointSourceLods.Clone(),
                TerrainCoverageComplete = source.TerrainCoverageComplete
            };
        }

        static AERISTerrainTileKey KeyForPoint(string bodyName, double radius,
            string environment, AERISTerrainTileLod lod, double latitude,
            double longitude)
        {
            double span = AERISTerrainTileFormat.AngularSpanDegrees(lod, radius);
            int latitudeCount = Math.Max(1, (int)Math.Ceiling(180.0 / span));
            int longitudeCount = Math.Max(1, (int)Math.Ceiling(360.0 / span));
            double clampedLat = Math.Max(-90.0, Math.Min(89.999999, latitude));
            double normalizedLon = longitude % 360.0;
            if (normalizedLon > 180.0) normalizedLon -= 360.0;
            if (normalizedLon < -180.0) normalizedLon += 360.0;
            int latIndex = Math.Max(0, Math.Min(latitudeCount - 1,
                (int)Math.Floor((clampedLat + 90.0) / span)));
            int lonIndex = (int)Math.Floor((normalizedLon + 180.0) / span);
            lonIndex %= longitudeCount;
            if (lonIndex < 0) lonIndex += longitudeCount;
            return new AERISTerrainTileKey(bodyName, radius, environment,
                lod, latIndex, lonIndex);
        }

        static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
