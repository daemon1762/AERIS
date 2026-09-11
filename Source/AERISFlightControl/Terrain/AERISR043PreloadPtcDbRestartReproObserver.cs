using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // Temporary restart-repro controller.
    //
    // Phase A (GeneralCompute): load the two previously committed GLOBAL tiles from
    // the restarted Preload DB through the production TryLoadBatch/decode path.
    // Phase B (Main Thread + real BlockPipeline): regenerate the same two PQS tiles
    // into RAM only.
    // Phase C (GeneralCompute): encode/decode the fresh PQS tiles with the official
    // preload codec and compare the resulting base payload bit-for-bit with the
    // restarted DB decode.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR043PreloadPtcDbRestartReproObserver : MonoBehaviour
    {
        sealed class Proof
        {
            internal string BodyName;
            internal CelestialBody Body;
            internal AERISTerrainTileKey Key;
            internal AERISTerrainTileRequest Request;
            internal AERISTerrainHeightTile DbTile;
            internal AERISTerrainHeightTile FreshPqsTile;
        }

        sealed class BodyResult
        {
            internal string BodyName;
            internal string StableId;
            internal int Samples;
            internal int ElevationBitMismatches;
            internal int FlagMismatches;
            internal bool GeometryExact;
            internal bool MetadataValid;
            internal string Failure = string.Empty;
        }

        sealed class Result
        {
            internal BodyResult[] Bodies;
            internal int TotalSamples;
            internal int ElevationBitMismatches;
            internal int FlagMismatches;
            internal int WorkerThreadId;
            internal int MainThreadId;
            internal string Failure = string.Empty;
        }

        readonly Proof[] proofs =
        {
            new Proof { BodyName = "Minmus" },
            new Proof { BodyName = "Kerbin" }
        };

        const int ExpectedBodies = 2;
        const int ExpectedSamplesPerBody = 289;
        const int ExpectedSamples = 578;
        const float TimeoutSeconds = 240f;

        int mainThreadId;
        float startedRealtime;
        bool prepared;
        bool dbReadSubmitted;
        bool dbReadComplete;
        bool referenceQueued;
        bool comparisonSubmitted;
        bool reported;
        AERISTerrainBlockPipeline pipeline;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            startedRealtime = Time.realtimeSinceStartup;
        }

        void Update()
        {
            if (reported) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;

            if (Time.realtimeSinceStartup - startedRealtime > TimeoutSeconds)
            {
                Fail("TIMEOUT");
                return;
            }

            AERISBootstrap bootstrap =
                UnityEngine.Object.FindObjectOfType<AERISBootstrap>();
            if (bootstrap == null || bootstrap.Terrain == null ||
                bootstrap.Terrain.DisplayTiles == null)
                return;

            AERISTerrainTileSystem tiles = bootstrap.Terrain.DisplayTiles;
            if (!tiles.IndexLoaded || !AERISTerrainTileSystem.GameDataHashReady)
                return;

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null)
                return;

            if (!prepared)
            {
                for (int i = 0; i < proofs.Length; i++)
                {
                    CelestialBody body;
                    AERISTerrainTileKey key;
                    AERISTerrainTileRequest request;
                    string failure;
                    if (!tiles.R043TryPrepareRestartProof(
                        proofs[i].BodyName,
                        out body,
                        out key,
                        out request,
                        out failure))
                    {
                        Fail("PREPARE_" + proofs[i].BodyName + ":" + failure);
                        return;
                    }

                    proofs[i].Body = body;
                    proofs[i].Key = key;
                    proofs[i].Request = request;

                    AERISLogger.Info(
                        "[AERIS43][R043_PRELOAD_PTC_RESTART_PREPARE]" +
                        "; pass=true" +
                        "; body=" + Safe(proofs[i].BodyName) +
                        "; stable_id=" + Safe(key.StableId) +
                        "; environment=" + Safe(key.EnvironmentHash) +
                        "; lod=" + key.Lod +
                        "; resolution=" +
                            request.Resolution.ToString(CultureInfo.InvariantCulture) +
                        "; database_contains=true" +
                        "; restart_process=true" +
                        "; db_write=false" +
                        "; production_authority=PQS" +
                        "; producer_switch=false" +
                        "; db_authority=PQS");
                }
                prepared = true;
            }

            if (!dbReadSubmitted)
            {
                dbReadSubmitted = true;
                var keys = new AERISTerrainTileKey[proofs.Length];
                for (int i = 0; i < proofs.Length; i++)
                    keys[i] = proofs[i].Key;

                bool accepted = runtime.Scheduler.SubmitRequired(
                    AERISRuntimeLane.GeneralCompute,
                    "r043-preload-ptc-restart-db-read",
                    runtime.CaptureStamp(),
                    context => tiles.R043LoadRestartProofBatch(keys),
                    value =>
                    {
                        var loaded = value as Dictionary<string, AERISTerrainHeightTile>;
                        if (loaded == null)
                        {
                            Fail("DB_READ_RESULT_NULL");
                            return;
                        }

                        for (int i = 0; i < proofs.Length; i++)
                        {
                            AERISTerrainHeightTile tile;
                            if (!loaded.TryGetValue(
                                    proofs[i].Key.StableId, out tile) ||
                                tile == null)
                            {
                                Fail("DB_READ_MISSING:" + proofs[i].BodyName);
                                return;
                            }
                            proofs[i].DbTile = tile;
                            AERISLogger.Info(
                                "[AERIS43][R043_PRELOAD_PTC_RESTART_DB_READ]" +
                                "; pass=true" +
                                "; body=" + Safe(proofs[i].BodyName) +
                                "; stable_id=" + Safe(proofs[i].Key.StableId) +
                                "; source=" + tile.Source +
                                "; resolution=" +
                                    tile.Resolution.ToString(
                                        CultureInfo.InvariantCulture) +
                                "; sampling_complete=" +
                                    Bool(tile.SamplingComplete) +
                                "; restart_process=true" +
                                "; persistent_db_read=true" +
                                "; db_write=false" +
                                "; production_authority=PQS" +
                                "; producer_switch=false" +
                                "; db_authority=PQS");
                        }

                        dbReadComplete = true;
                    },
                    false);

                if (!accepted)
                {
                    Fail("DB_READ_SCHEDULER_REJECTED");
                    return;
                }
            }

            if (!dbReadComplete) return;

            if (!referenceQueued)
            {
                referenceQueued = true;
                pipeline = new AERISTerrainBlockPipeline(null);

                for (int i = 0; i < proofs.Length; i++)
                {
                    int index = i;
                    Proof proof = proofs[index];
                    bool accepted = pipeline.Enqueue(
                        proof.Body,
                        proof.Request,
                        AERISTerrainTileSource.PreloadBuilderGenerated,
                        proof.Key.EnvironmentHash,
                        AERISTerrainTileSystem.GameDataHash,
                        1L,
                        candidate => candidate != null,
                        (request, tile, final) =>
                        {
                            if (!final) return;
                            proofs[index].FreshPqsTile =
                                tile == null ? null : tile.CloneImmutable();
                        });

                    if (!accepted)
                    {
                        Fail("REFERENCE_ENQUEUE_REJECTED:" + proof.BodyName);
                        return;
                    }
                }
            }

            if (pipeline != null)
                pipeline.Tick(
                    50000f,
                    512,
                    4.0f,
                    Mathf.Max(0.001f, Time.unscaledDeltaTime));

            if (comparisonSubmitted) return;

            for (int i = 0; i < proofs.Length; i++)
                if (proofs[i].FreshPqsTile == null) return;

            comparisonSubmitted = true;
            Proof[] payload = new Proof[proofs.Length];
            for (int i = 0; i < proofs.Length; i++)
            {
                payload[i] = new Proof
                {
                    BodyName = proofs[i].BodyName,
                    Key = proofs[i].Key,
                    DbTile = proofs[i].DbTile.CloneImmutable(),
                    FreshPqsTile = proofs[i].FreshPqsTile.CloneImmutable()
                };
            }

            bool compareAccepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute,
                "r043-preload-ptc-restart-codec-compare",
                runtime.CaptureStamp(),
                context => Evaluate(payload, mainThreadId),
                value => CommitResult(value as Result),
                false);

            if (!compareAccepted)
                Fail("COMPARE_SCHEDULER_REJECTED");
        }

        static Result Evaluate(Proof[] payload, int mainThread)
        {
            var result = new Result
            {
                MainThreadId = mainThread,
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId,
                Bodies = new BodyResult[payload == null ? 0 : payload.Length]
            };

            try
            {
                if (payload == null || payload.Length != ExpectedBodies)
                    throw new InvalidOperationException("PAYLOAD_COUNT");

                for (int i = 0; i < payload.Length; i++)
                {
                    Proof proof = payload[i];
                    if (proof == null || proof.DbTile == null ||
                        proof.FreshPqsTile == null)
                        throw new InvalidOperationException(
                            "BODY_PAYLOAD_NULL:" + i);

                    AERISTerrainPreloadEncodedTile encoded =
                        AERISTerrainPreloadCodec.Encode(
                            proof.FreshPqsTile,
                            proof.Key.EnvironmentHash,
                            AERISTerrainTileSystem.GameDataHash,
                            1L,
                            AERISTerrainCodecId.Deflate);
                    AERISTerrainHeightTile reference =
                        AERISTerrainPreloadCodec.Decode(encoded);

                    BodyResult body = Compare(
                        proof.BodyName,
                        proof.Key,
                        proof.DbTile,
                        reference);
                    result.Bodies[i] = body;
                    result.TotalSamples += body.Samples;
                    result.ElevationBitMismatches +=
                        body.ElevationBitMismatches;
                    result.FlagMismatches += body.FlagMismatches;
                }
            }
            catch (Exception ex)
            {
                result.Failure =
                    ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }

            return result;
        }

        static BodyResult Compare(
            string bodyName,
            AERISTerrainTileKey key,
            AERISTerrainHeightTile db,
            AERISTerrainHeightTile reference)
        {
            var result = new BodyResult
            {
                BodyName = bodyName ?? string.Empty,
                StableId = key.StableId
            };

            if (db == null || reference == null)
            {
                result.Failure = "TILE_NULL";
                return result;
            }

            result.GeometryExact =
                db.Key.Equals(reference.Key) &&
                db.Resolution == reference.Resolution &&
                DoubleBits(db.SouthLatitudeDeg) ==
                    DoubleBits(reference.SouthLatitudeDeg) &&
                DoubleBits(db.NorthLatitudeDeg) ==
                    DoubleBits(reference.NorthLatitudeDeg) &&
                DoubleBits(db.WestLongitudeDeg) ==
                    DoubleBits(reference.WestLongitudeDeg) &&
                DoubleBits(db.EastLongitudeDeg) ==
                    DoubleBits(reference.EastLongitudeDeg);

            result.MetadataValid =
                db.Source == AERISTerrainTileSource.PreloadDatabase &&
                db.SamplingComplete &&
                !db.IsPreview &&
                string.Equals(
                    db.PqsConfigurationHash,
                    key.EnvironmentHash,
                    StringComparison.Ordinal) &&
                string.Equals(
                    db.GameDataHash,
                    AERISTerrainTileSystem.GameDataHash,
                    StringComparison.Ordinal);

            if (db.Resolution != 17 || reference.Resolution != 17 ||
                db.Elevation == null || reference.Elevation == null ||
                db.Flags == null || reference.Flags == null ||
                db.Elevation.Length != ExpectedSamplesPerBody ||
                reference.Elevation.Length != ExpectedSamplesPerBody ||
                db.Flags.Length != ExpectedSamplesPerBody ||
                reference.Flags.Length != ExpectedSamplesPerBody)
            {
                result.Failure = "BASE_PAYLOAD_SHAPE";
                return result;
            }

            for (int i = 0; i < ExpectedSamplesPerBody; i++)
            {
                if (SingleBits(db.Elevation[i]) !=
                    SingleBits(reference.Elevation[i]))
                    result.ElevationBitMismatches++;
                if (db.Flags[i] != reference.Flags[i])
                    result.FlagMismatches++;
                result.Samples++;
            }

            if (!result.GeometryExact)
                result.Failure = "GEOMETRY_MISMATCH";
            else if (!result.MetadataValid)
                result.Failure = "METADATA_MISMATCH";

            return result;
        }

        void CommitResult(Result result)
        {
            if (reported) return;

            if (result == null)
            {
                Fail("RESULT_NULL");
                return;
            }

            bool workerNotMain =
                result.WorkerThreadId > 0 &&
                result.WorkerThreadId != mainThreadId;
            bool pass =
                string.IsNullOrEmpty(result.Failure) &&
                result.Bodies != null &&
                result.Bodies.Length == ExpectedBodies &&
                result.TotalSamples == ExpectedSamples &&
                result.ElevationBitMismatches == 0 &&
                result.FlagMismatches == 0 &&
                workerNotMain;

            if (result.Bodies != null)
            {
                for (int i = 0; i < result.Bodies.Length; i++)
                {
                    BodyResult body = result.Bodies[i];
                    if (body == null)
                    {
                        pass = false;
                        continue;
                    }

                    bool bodyPass =
                        string.IsNullOrEmpty(body.Failure) &&
                        body.Samples == ExpectedSamplesPerBody &&
                        body.ElevationBitMismatches == 0 &&
                        body.FlagMismatches == 0 &&
                        body.GeometryExact &&
                        body.MetadataValid;

                    if (!bodyPass) pass = false;

                    AERISLogger.Info(
                        "[AERIS43][R043_PRELOAD_PTC_RESTART_BODY]" +
                        "; pass=" + Bool(bodyPass) +
                        "; body=" + Safe(body.BodyName) +
                        "; stable_id=" + Safe(body.StableId) +
                        "; checks=" +
                            body.Samples.ToString(CultureInfo.InvariantCulture) +
                        "; elevation_bit_mismatch=" +
                            body.ElevationBitMismatches.ToString(
                                CultureInfo.InvariantCulture) +
                        "; flag_mismatch=" +
                            body.FlagMismatches.ToString(
                                CultureInfo.InvariantCulture) +
                        "; geometry_exact=" + Bool(body.GeometryExact) +
                        "; metadata_valid=" + Bool(body.MetadataValid) +
                        "; failure=" + Safe(body.Failure) +
                        "; db_source=PreloadDatabase" +
                        "; reference=PQS_THEN_OFFICIAL_CODEC" +
                        "; codec_version=" +
                            AERISTerrainPreloadFormat.CodecVersion.ToString(
                                CultureInfo.InvariantCulture) +
                        "; restart_process=true" +
                        "; db_write=false" +
                        "; production_authority=PQS" +
                        "; producer_switch=false" +
                        "; db_authority=PQS");
                }
            }

            reported = true;
            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_DB_RESTART_REPRO_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; bodies=2" +
                "; total_checks=" +
                    result.TotalSamples.ToString(CultureInfo.InvariantCulture) +
                "; elevation_bit_mismatch=" +
                    result.ElevationBitMismatches.ToString(
                        CultureInfo.InvariantCulture) +
                "; flag_mismatch=" +
                    result.FlagMismatches.ToString(
                        CultureInfo.InvariantCulture) +
                "; worker_thread_id=" +
                    result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; failure=" + Safe(result.Failure) +
                "; db_read=TryLoadBatch" +
                "; reference=PQS_THEN_OFFICIAL_CODEC" +
                "; restart_process=true" +
                "; persistent_db_read=true" +
                "; db_write=false" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS");

            if (pipeline != null)
            {
                pipeline.Dispose();
                pipeline = null;
            }
        }

        void Fail(string failure)
        {
            if (reported) return;
            reported = true;
            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_DB_RESTART_REPRO_COMPLETE]" +
                "; pass=false" +
                "; failure=" + Safe(failure) +
                "; restart_process=true" +
                "; persistent_db_read=false" +
                "; db_write=false" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS");
            if (pipeline != null)
            {
                pipeline.Dispose();
                pipeline = null;
            }
        }

        void OnDestroy()
        {
            if (pipeline != null)
            {
                pipeline.Dispose();
                pipeline = null;
            }
        }

        static long DoubleBits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value);
        }

        static int SingleBits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }
    }
}
