using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // Deterministic proof harness for R043 PRELOAD_PTC real BlockPipeline integration.
    //
    // It does not use the terrain DB and does not switch authority. Seven synthetic
    // PreloadBuilder-owned GLOBAL requests are sent through the production
    // AERISTerrainBlockPipeline Enqueue/SampleOne/GeneralCompute/CommitBlock path.
    // The commit callback is RAM-only; the pipeline itself logs exact-CPU shadow parity.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR043PreloadPtcIntegratedPipelineObserver : MonoBehaviour
    {
        static readonly string[] TargetBodies =
        {
            "Minmus", "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        const int ExpectedBodies = 7;
        const int Resolution = 17;
        const int ExpectedChecksPerBody = Resolution * Resolution;
        const int ExpectedChecks = ExpectedBodies * ExpectedChecksPerBody;
        const float TimeoutSeconds = 180f;

        readonly HashSet<string> committed =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int mainThreadId;
        bool started;
        bool reported;
        float startedRealtime;
        AERISTerrainBlockPipeline pipeline;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (reported) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            if (!started)
            {
                if (!StartHarness()) return;
            }

            if (pipeline == null)
            {
                Fail("PIPELINE_NULL");
                return;
            }

            pipeline.Tick(
                50000f,
                512,
                4.0f,
                Mathf.Max(0.001f, Time.unscaledDeltaTime));

            if (committed.Count == ExpectedBodies)
            {
                reported = true;
                AERISLogger.Info(
                    "[AERIS43][R043_PRELOAD_PTC_INTEGRATION_HARNESS_COMPLETE]" +
                    "; pass=true" +
                    "; bodies=7" +
                    "; tiles=7" +
                    "; lod=Global" +
                    "; resolution=17" +
                    "; expected_checks_per_body=289" +
                    "; expected_total_checks=2023" +
                    "; commit_target=RAM_CALLBACK_ONLY" +
                    "; exact_cpu_db_write=false" +
                    "; production_authority=PQS" +
                    "; producer_switch=false" +
                    "; db_authority=PQS" +
                    "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                    "; main_thread_id=" +
                        mainThreadId.ToString(CultureInfo.InvariantCulture));
                pipeline.Dispose();
                pipeline = null;
                return;
            }

            if (Time.realtimeSinceStartup - startedRealtime > TimeoutSeconds)
                Fail("TIMEOUT:committed=" +
                    committed.Count.ToString(CultureInfo.InvariantCulture));
        }

        bool StartHarness()
        {
            pipeline = new AERISTerrainBlockPipeline(null);
            startedRealtime = Time.realtimeSinceStartup;

            for (int i = 0; i < TargetBodies.Length; i++)
            {
                string bodyName = TargetBodies[i];
                CelestialBody body = FindBody(bodyName);
                if (body == null || !AERISTerrainTileSystem.BodyHasSolidSurface(body))
                {
                    Fail("BODY_UNAVAILABLE:" + bodyName);
                    return false;
                }

                AERISTerrainTileRequest request = BuildRequest(body);
                if (request == null)
                {
                    Fail("REQUEST_BUILD_FAILED:" + bodyName);
                    return false;
                }

                string capturedBody = bodyName;
                bool accepted = pipeline.Enqueue(
                    body,
                    request,
                    AERISTerrainTileSource.PreloadBuilderGenerated,
                    request.Key.EnvironmentHash,
                    "R043_INTEGRATION_PROOF",
                    1L,
                    candidate => candidate != null,
                    (committedRequest, tile, final) =>
                    {
                        if (!final) return;
                        bool tilePass =
                            committedRequest != null &&
                            tile != null &&
                            tile.SamplingComplete &&
                            tile.Resolution == Resolution &&
                            tile.Elevation != null &&
                            tile.Elevation.Length == ExpectedChecksPerBody &&
                            tile.Flags != null &&
                            tile.Flags.Length == ExpectedChecksPerBody;

                        AERISLogger.Info(
                            "[AERIS43][R043_PRELOAD_PTC_INTEGRATION_HARNESS_COMMIT]" +
                            "; pass=" + (tilePass ? "true" : "false") +
                            "; body=" + Safe(capturedBody) +
                            "; resolution=" +
                                (tile == null ? "0" :
                                    tile.Resolution.ToString(CultureInfo.InvariantCulture)) +
                            "; sampling_complete=" +
                                (tile != null && tile.SamplingComplete ?
                                    "true" : "false") +
                            "; commit_target=RAM_CALLBACK_ONLY" +
                            "; exact_cpu_db_write=false" +
                            "; production_authority=PQS" +
                            "; producer_switch=false" +
                            "; db_authority=PQS");

                        if (!tilePass)
                        {
                            Fail("COMMIT_CONTRACT:" + capturedBody);
                            return;
                        }

                        committed.Add(capturedBody);
                    });

                if (!accepted)
                {
                    Fail("ENQUEUE_REJECTED:" + bodyName);
                    return false;
                }
            }

            started = true;
            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_INTEGRATION_HARNESS_BEGIN]" +
                "; bodies=7" +
                "; tiles=7" +
                "; lod=Global" +
                "; resolution=17" +
                "; expected_checks_per_body=289" +
                "; expected_total_checks=2023" +
                "; work_owner=PreloadBuilder" +
                "; source=PreloadBuilderGenerated" +
                "; commit_target=RAM_CALLBACK_ONLY" +
                "; exact_cpu_db_write=false" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture));
            return true;
        }

        static AERISTerrainTileRequest BuildRequest(CelestialBody body)
        {
            const AERISTerrainTileLod lod = AERISTerrainTileLod.Global;
            int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
            int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
            if (latCount <= 0 || lonCount <= 0) return null;

            int latIndex = Math.Max(0, Math.Min(latCount - 1, latCount / 2));
            int lonIndex = AERISTerrainTileSystem.WrapTileIndex(lonCount / 2, lonCount);
            string environment = "R043_INTEGRATION_PROOF:" + (body.name ?? string.Empty);
            var key = new AERISTerrainTileKey(
                body.name,
                body.Radius,
                environment,
                lod,
                latIndex,
                lonIndex);

            double span = AERISTerrainTileFormat.AngularSpanDegrees(lod, body.Radius);
            double south = Math.Max(-90.0, -90.0 + latIndex * span);
            double north = Math.Min(90.0, south + span);
            double west = NormalizeLongitude(-180.0 + lonIndex * span);
            double east = NormalizeLongitude(west + span);

            return new AERISTerrainTileRequest
            {
                Key = key,
                Priority = AERISTerrainTilePriority.Low,
                CenterLatitudeDeg = (south + north) * 0.5,
                CenterLongitudeDeg = NormalizeLongitude((west + east) * 0.5),
                SouthLatitudeDeg = south,
                NorthLatitudeDeg = north,
                WestLongitudeDeg = west,
                EastLongitudeDeg = east,
                Resolution = Resolution,
                FinalResolution = Resolution,
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
                DatabaseGeneration = 1L,
                WorkOwner = AERISTerrainWorkOwner.PreloadBuilder,
                Visible = false
            };
        }

        static double NormalizeLongitude(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        static CelestialBody FindBody(string name)
        {
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body == null) continue;
                if (string.Equals(body.name, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(body.bodyName, name, StringComparison.OrdinalIgnoreCase))
                    return body;
            }
            return null;
        }

        void Fail(string failure)
        {
            if (reported) return;
            reported = true;
            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_INTEGRATION_HARNESS_COMPLETE]" +
                "; pass=false" +
                "; failure=" + Safe(failure) +
                "; committed=" +
                    committed.Count.ToString(CultureInfo.InvariantCulture) +
                "; expected_total_checks=2023" +
                "; commit_target=RAM_CALLBACK_ONLY" +
                "; exact_cpu_db_write=false" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture));
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

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
