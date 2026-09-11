using System;
using System.Collections.Generic;
using System.Globalization;

namespace AERISFlightControl.Terrain
{
    // R043 PRELOAD_PTC live-path proof seam.
    //
    // Dormant in normal operation. A temporary MainMenu observer may request one
    // deterministic GLOBAL tile for a certified body. The request uses the production
    // BodyPlan/environment hash, real BlockPipeline, real encode queue and real
    // AERISTerrainPreloadDatabase. Existing DB content is not deleted; SaveEncodedBatch
    // atomically rewrites the same PQS-derived tile if it already exists.
    internal sealed partial class AERISTerrainPreloadBuilder
    {
        readonly Dictionary<string, string> r043LiveProofBodyByStableId =
            new Dictionary<string, string>(StringComparer.Ordinal);
        readonly HashSet<string> r043LiveProofDurableBodies =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal bool R043RequestLivePreloadProof(
            string bodyName,
            out string stableId,
            out string failure)
        {
            stableId = string.Empty;
            failure = string.Empty;

            if (disposed)
            {
                failure = "BUILDER_DISPOSED";
                return false;
            }
            if (HighLogic.LoadedSceneIsFlight)
            {
                failure = "FLIGHT_SCENE";
                return false;
            }
            if (database == null || !database.IndexLoaded)
            {
                failure = "DATABASE_INDEX_NOT_READY";
                return false;
            }
            if (blockPipeline == null)
            {
                failure = "BLOCK_PIPELINE_NULL";
                return false;
            }

            CelestialBody body = FindBody(bodyName);
            if (body == null || !AERISTerrainTileSystem.BodyHasSolidSurface(body))
            {
                failure = "BODY_UNAVAILABLE";
                return false;
            }

            BodyPlan plan = GetSupportedBodyPlan(bodyName);
            if (plan == null)
            {
                failure = "BODY_PLAN_UNAVAILABLE";
                return false;
            }

            EnsureEnvironment(plan, body);
            if (string.IsNullOrEmpty(plan.EnvironmentHash))
            {
                failure = "ENVIRONMENT_HASH_EMPTY";
                return false;
            }

            const AERISTerrainTileLod lod = AERISTerrainTileLod.Global;
            int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
            int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
            if (latCount <= 0 || lonCount <= 0)
            {
                failure = "TILE_GRID_INVALID";
                return false;
            }

            // A deterministic non-polar tile. The DB key is a normal production key.
            int latIndex = Math.Max(0, Math.Min(latCount - 1, latCount / 2));
            int lonIndex = AERISTerrainTileSystem.WrapTileIndex(
                Math.Max(0, lonCount / 3), lonCount);
            var key = new AERISTerrainTileKey(
                body.name,
                body.Radius,
                plan.EnvironmentHash,
                lod,
                latIndex,
                lonIndex);
            stableId = key.StableId;

            lock (sync)
            {
                string existingBody;
                if (r043LiveProofBodyByStableId.TryGetValue(stableId, out existingBody))
                    return true;
                if (pendingWrites.Contains(stableId))
                {
                    failure = "TILE_ALREADY_PENDING";
                    return false;
                }
                r043LiveProofBodyByStableId[stableId] = body.name ?? bodyName;
            }

            AERISTerrainBlockPipeline.R043RegisterLivePreloadProofStableId(stableId);

            AERISTerrainTileRequest request = CreateBuilderRequest(body, key, plan);
            request.WorkOwner = AERISTerrainWorkOwner.PreloadBuilder;
            request.ReadLane = AERISTerrainReadLane.Background;
            request.Lane = AERISTerrainRequestLane.Background;
            request.Priority = AERISTerrainTilePriority.Critical;
            request.Visible = false;
            request.ViewDistanceMeters = double.MaxValue;

            bool accepted = blockPipeline.Enqueue(
                body,
                request,
                AERISTerrainTileSource.PreloadBuilderGenerated,
                plan.EnvironmentHash,
                CurrentGameDataHash(),
                database.DatabaseGeneration,
                candidate => IsBuilderRequestCurrent(plan, candidate),
                (committedRequest, tile, final) =>
                    CommitGeneratedTile(plan, tile, final));

            if (!accepted)
            {
                lock (sync)
                    r043LiveProofBodyByStableId.Remove(stableId);
                failure = "BLOCK_PIPELINE_ENQUEUE_REJECTED";
                return false;
            }

            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_LIVE_REQUEST]" +
                "; pass=true" +
                "; body=" + SafeR043Live(body.name) +
                "; stable_id=" + SafeR043Live(stableId) +
                "; lod=Global" +
                "; resolution=" +
                    request.Resolution.ToString(CultureInfo.InvariantCulture) +
                "; environment=" + SafeR043Live(plan.EnvironmentHash) +
                "; existing_db_entry=" +
                    (database.Contains(key) ? "true" : "false") +
                "; work_owner=PreloadBuilder" +
                "; source=PreloadBuilderGenerated" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; exact_cpu_db_write=false");

            return true;
        }

        internal bool R043LivePreloadProofDurable(string bodyName)
        {
            lock (sync)
                return r043LiveProofDurableBodies.Contains(bodyName ?? string.Empty);
        }

        void R043NoteDurableBatch(ChunkWritePayload payload)
        {
            if (payload == null || payload.StableIds == null ||
                payload.Tiles == null) return;

            int count = Math.Min(payload.StableIds.Length, payload.Tiles.Length);
            for (int i = 0; i < count; i++)
            {
                string stableId = payload.StableIds[i] ?? string.Empty;
                if (string.IsNullOrEmpty(stableId)) continue;

                string bodyName;
                lock (sync)
                {
                    if (!r043LiveProofBodyByStableId.TryGetValue(
                            stableId, out bodyName))
                        continue;
                }

                AERISTerrainPreloadEncodedTile encoded = payload.Tiles[i];
                bool idMatches = encoded != null &&
                    string.Equals(encoded.Key.StableId, stableId,
                        StringComparison.Ordinal);
                bool durable = idMatches && database.Contains(encoded.Key);

                AERISLogger.Info(
                    "[AERIS43][R043_PRELOAD_PTC_LIVE_DB_DURABLE]" +
                    "; pass=" + (durable ? "true" : "false") +
                    "; body=" + SafeR043Live(bodyName) +
                    "; stable_id=" + SafeR043Live(stableId) +
                    "; key_match=" + (idMatches ? "true" : "false") +
                    "; database_contains=" + (durable ? "true" : "false") +
                    "; save_encoded_batch=true" +
                    "; db_authority=PQS" +
                    "; production_authority=PQS" +
                    "; producer_switch=false" +
                    "; exact_cpu_db_write=false");

                if (durable)
                {
                    lock (sync)
                        r043LiveProofDurableBodies.Add(bodyName ?? string.Empty);
                }
            }
        }

        static string SafeR043Live(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
