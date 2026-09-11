using System;
using System.Collections.Generic;
using System.Globalization;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // AERIS46 / R043 natural-parity diagnosis seam.
    //
    // Compiled only by the temporary AERIS46 diagnostic project. It requests two
    // adjacent production-shaped FAR tiles on one body so the shared boundary cache
    // must reuse the common edge. The commit callback is observation-only: no tile is
    // written to the preload DB and no production authority is changed.
    internal sealed partial class AERISTerrainPreloadBuilder
    {
        readonly HashSet<string> r046NaturalDiagnosticRequested =
            new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> r046NaturalDiagnosticCompleted =
            new HashSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, string[]> r046NaturalDiagnosticBodyStableIds =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        internal bool R046RequestNaturalParityPair(
            string bodyName,
            out string pairId,
            out string failure)
        {
            pairId = string.Empty;
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

            const AERISTerrainTileLod lod = AERISTerrainTileLod.Far;
            int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
            int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
            if (latCount <= 0 || lonCount < 2)
            {
                failure = "TILE_GRID_INVALID";
                return false;
            }

            int latIndex = Math.Max(0, Math.Min(latCount - 1, latCount / 2));
            int lon0 = AERISTerrainTileSystem.WrapTileIndex(
                Math.Max(0, lonCount / 3), lonCount);
            int lon1 = AERISTerrainTileSystem.WrapTileIndex(lon0 + 1, lonCount);

            var keys = new[]
            {
                new AERISTerrainTileKey(
                    body.name, body.Radius, plan.EnvironmentHash,
                    lod, latIndex, lon0),
                new AERISTerrainTileKey(
                    body.name, body.Radius, plan.EnvironmentHash,
                    lod, latIndex, lon1)
            };

            string[] ids = { keys[0].StableId, keys[1].StableId };
            pairId = ids[0] + "|" + ids[1];

            lock (sync)
                r046NaturalDiagnosticBodyStableIds[body.name ?? bodyName] = ids;

            for (int i = 0; i < keys.Length; i++)
            {
                string stableId = ids[i];
                lock (sync)
                {
                    if (r046NaturalDiagnosticRequested.Contains(stableId))
                        continue;
                }

                AERISTerrainBlockPipeline.R043RegisterLivePreloadProofStableId(
                    stableId);

                AERISTerrainTileRequest request =
                    CreateBuilderRequest(body, keys[i], plan);
                request.WorkOwner = AERISTerrainWorkOwner.PreloadBuilder;
                request.ReadLane = AERISTerrainReadLane.Background;
                request.Lane = AERISTerrainRequestLane.Background;
                request.Priority = AERISTerrainTilePriority.Critical;
                request.Visible = false;
                request.ViewDistanceMeters = double.MaxValue;

                string capturedStableId = stableId;
                bool accepted = blockPipeline.Enqueue(
                    body,
                    request,
                    AERISTerrainTileSource.PreloadBuilderGenerated,
                    plan.EnvironmentHash,
                    CurrentGameDataHash(),
                    database.DatabaseGeneration,
                    candidate => IsBuilderRequestCurrent(plan, candidate),
                    (committedRequest, tile, final) =>
                    {
                        if (!final) return;
                        lock (sync)
                            r046NaturalDiagnosticCompleted.Add(capturedStableId);
                        AERISLogger.Info(
                            "[AERIS46][R043_NATURAL_PAIR_COMMIT]" +
                            "; body=" + SafeR046Natural(body.name) +
                            "; stable_id=" + SafeR046Natural(capturedStableId) +
                            "; final=true" +
                            "; db_write=false" +
                            "; production_authority=PQS" +
                            "; producer_switch=false" +
                            "; exact_cpu_db_write=false");
                    });

                if (!accepted)
                {
                    failure = "BLOCK_PIPELINE_ENQUEUE_REJECTED:" + i.ToString(
                        CultureInfo.InvariantCulture);
                    return false;
                }

                lock (sync)
                    r046NaturalDiagnosticRequested.Add(stableId);
            }

            AERISLogger.Info(
                "[AERIS46][R043_NATURAL_PAIR_REQUEST]" +
                "; pass=true" +
                "; body=" + SafeR046Natural(body.name) +
                "; lod=Far" +
                "; resolution=" +
                    AERISTerrainTileFormat.Resolution(lod).ToString(
                        CultureInfo.InvariantCulture) +
                "; lat_index=" + latIndex.ToString(CultureInfo.InvariantCulture) +
                "; lon0=" + lon0.ToString(CultureInfo.InvariantCulture) +
                "; lon1=" + lon1.ToString(CultureInfo.InvariantCulture) +
                "; stable_id_0=" + SafeR046Natural(ids[0]) +
                "; stable_id_1=" + SafeR046Natural(ids[1]) +
                "; environment=" + SafeR046Natural(plan.EnvironmentHash) +
                "; adjacent_shared_boundary=true" +
                "; db_write=false" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; exact_cpu_db_write=false");

            return true;
        }

        internal bool R046NaturalParityPairComplete(string bodyName)
        {
            string[] ids;
            lock (sync)
            {
                if (!r046NaturalDiagnosticBodyStableIds.TryGetValue(
                        bodyName ?? string.Empty, out ids) ||
                    ids == null || ids.Length != 2)
                    return false;
                return r046NaturalDiagnosticCompleted.Contains(ids[0]) &&
                    r046NaturalDiagnosticCompleted.Contains(ids[1]);
            }
        }

        static string SafeR046Natural(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
