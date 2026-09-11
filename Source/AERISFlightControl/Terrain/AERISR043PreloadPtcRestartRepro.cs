using System;
using System.Collections.Generic;

namespace AERISFlightControl.Terrain
{
    // R043 PRELOAD_PTC restart-repro proof seam.
    // Dormant in normal operation. It exposes the production DB read path without
    // adding any independent file I/O or codec implementation.
    internal sealed partial class AERISTerrainTileSystem
    {
        internal bool R043TryPrepareRestartProof(
            string bodyName,
            out CelestialBody body,
            out AERISTerrainTileKey key,
            out AERISTerrainTileRequest request,
            out string failure)
        {
            body = null;
            key = default(AERISTerrainTileKey);
            request = null;
            failure = string.Empty;

            if (!GameDataHashReady)
            {
                failure = "GAMEDATA_HASH_NOT_READY";
                return false;
            }
            if (preloadDatabase == null || !preloadDatabase.IndexLoaded)
            {
                failure = "DATABASE_INDEX_NOT_READY";
                return false;
            }

            if (FlightGlobals.Bodies == null)
            {
                failure = "BODIES_UNAVAILABLE";
                return false;
            }

            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody candidate = FlightGlobals.Bodies[i];
                if (candidate == null) continue;
                if (string.Equals(candidate.name, bodyName,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(candidate.bodyName, bodyName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    body = candidate;
                    break;
                }
            }

            if (body == null || !BodyHasSolidSurface(body))
            {
                failure = "BODY_UNAVAILABLE";
                return false;
            }

            string environment = EnvironmentHashForBody(body);
            if (string.IsNullOrEmpty(environment))
            {
                failure = "ENVIRONMENT_HASH_EMPTY";
                return false;
            }

            const AERISTerrainTileLod lod = AERISTerrainTileLod.Global;
            int latCount = LatitudeTileCountFor(body, lod);
            int lonCount = LongitudeTileCountFor(body, lod);
            if (latCount <= 0 || lonCount <= 0)
            {
                failure = "TILE_GRID_INVALID";
                return false;
            }

            int latIndex = Math.Max(0, Math.Min(latCount - 1, latCount / 2));
            int lonIndex = WrapTileIndex(Math.Max(0, lonCount / 3), lonCount);
            key = new AERISTerrainTileKey(
                body.name,
                body.Radius,
                environment,
                lod,
                latIndex,
                lonIndex);

            if (!preloadDatabase.Contains(key))
            {
                failure = "DATABASE_KEY_MISSING";
                return false;
            }

            double span = AERISTerrainTileFormat.AngularSpanDegrees(lod, body.Radius);
            double south = Math.Max(-90.0, -90.0 + latIndex * span);
            double north = Math.Min(90.0, south + span);
            double west = NormalizeLongitude(-180.0 + lonIndex * span);
            double east = NormalizeLongitude(west + span);
            int resolution = AERISTerrainTileFormat.Resolution(lod);

            request = new AERISTerrainTileRequest
            {
                Key = key,
                Priority = AERISTerrainTilePriority.Critical,
                CenterLatitudeDeg = (south + north) * 0.5,
                CenterLongitudeDeg = NormalizeLongitude((west + east) * 0.5),
                SouthLatitudeDeg = south,
                NorthLatitudeDeg = north,
                WestLongitudeDeg = west,
                EastLongitudeDeg = east,
                Resolution = resolution,
                FinalResolution = resolution,
                Stage = AERISTerrainSamplingStage.Final,
                Lane = AERISTerrainRequestLane.Background,
                ReadLane = AERISTerrainReadLane.Background,
                ViewDistanceMeters = double.MaxValue,
                RequestSequence = 1L,
                BodyGeneration = 1L,
                VesselGeneration = 0L,
                TerrainGeneration = 1L,
                ViewGeneration = 0L,
                RangeGeneration = 0L,
                PlanGeneration = 1L,
                DatabaseGeneration = preloadDatabase.RequestGeneration,
                WorkOwner = AERISTerrainWorkOwner.PreloadBuilder,
                Visible = false
            };
            return true;
        }

        internal Dictionary<string, AERISTerrainHeightTile>
            R043LoadRestartProofBatch(IList<AERISTerrainTileKey> keys)
        {
            var output = new Dictionary<string, AERISTerrainHeightTile>(
                StringComparer.Ordinal);
            if (preloadDatabase == null || keys == null || keys.Count == 0)
                return output;

            // Null warm cache deliberately forces the persistent DB/chunk path.
            preloadDatabase.TryLoadBatch(
                keys,
                null,
                output,
                null,
                GameDataHash);
            return output;
        }
    }
}
