using System;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // Temporary R043 live-preload proof controller.
    // Uses the persistent AERISBootstrap's real TerrainTileSystem/PreloadBuilder/DB.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR043PreloadPtcLivePreloadObserver : MonoBehaviour
    {
        const float TimeoutSeconds = 240f;
        const float RetrySeconds = 0.5f;

        readonly string[] bodies = { "Minmus", "Kerbin" };
        readonly string[] stableIds = { string.Empty, string.Empty };
        readonly bool[] requested = { false, false };

        int mainThreadId;
        float startRealtime;
        float nextRetryRealtime;
        bool reported;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            startRealtime = Time.realtimeSinceStartup;
        }

        void Update()
        {
            if (reported) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;

            AERISBootstrap bootstrap =
                UnityEngine.Object.FindObjectOfType<AERISBootstrap>();
            if (bootstrap == null || bootstrap.Terrain == null ||
                bootstrap.Terrain.DisplayTiles == null)
            {
                if (Time.realtimeSinceStartup - startRealtime > TimeoutSeconds)
                    Fail("BOOTSTRAP_OR_TERRAIN_UNAVAILABLE");
                return;
            }

            AERISTerrainTileSystem tiles = bootstrap.Terrain.DisplayTiles;
            if (!tiles.IndexLoaded)
            {
                if (Time.realtimeSinceStartup - startRealtime > TimeoutSeconds)
                    Fail("PRELOAD_INDEX_TIMEOUT");
                return;
            }

            if (bootstrap.Settings == null ||
                !bootstrap.Settings.TerrainPreloadEnabled)
            {
                Fail("TERRAIN_PRELOAD_DISABLED");
                return;
            }

            if (Time.realtimeSinceStartup >= nextRetryRealtime)
            {
                nextRetryRealtime = Time.realtimeSinceStartup + RetrySeconds;
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (requested[i]) continue;

                    string stableId;
                    string failure;
                    bool accepted = tiles.R043RequestLivePreloadProof(
                        bodies[i], out stableId, out failure);
                    if (accepted)
                    {
                        requested[i] = true;
                        stableIds[i] = stableId ?? string.Empty;
                        AERISLogger.Info(
                            "[AERIS43][R043_PRELOAD_PTC_LIVE_OBSERVER_REQUEST]" +
                            "; pass=true" +
                            "; body=" + bodies[i] +
                            "; stable_id=" + Safe(stableIds[i]) +
                            "; main_thread_id=" +
                                mainThreadId.ToString(CultureInfo.InvariantCulture) +
                            "; production_authority=PQS" +
                            "; producer_switch=false" +
                            "; db_authority=PQS" +
                            "; exact_cpu_db_write=false");
                    }
                    else if (!string.Equals(
                        failure, "TILE_ALREADY_PENDING",
                        StringComparison.Ordinal))
                    {
                        Fail("REQUEST_" + bodies[i] + ":" + failure);
                        return;
                    }
                }
            }

            bool allRequested = requested[0] && requested[1];
            bool minmusDurable = allRequested &&
                tiles.R043LivePreloadProofDurable("Minmus");
            bool kerbinDurable = allRequested &&
                tiles.R043LivePreloadProofDurable("Kerbin");

            if (minmusDurable && kerbinDurable)
            {
                reported = true;
                AERISLogger.Info(
                    "[AERIS43][R043_PRELOAD_PTC_LIVE_COMPLETE]" +
                    "; pass=true" +
                    "; bodies=2" +
                    "; evaluator_families=2" +
                    "; minmus_stable_id=" + Safe(stableIds[0]) +
                    "; kerbin_stable_id=" + Safe(stableIds[1]) +
                    "; expected_checks_per_body=289" +
                    "; expected_total_checks=578" +
                    "; path=REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB" +
                    "; db_commit=SaveEncodedBatch" +
                    "; production_authority=PQS" +
                    "; producer_switch=false" +
                    "; db_authority=PQS" +
                    "; exact_cpu_db_write=false" +
                    "; main_thread_id=" +
                        mainThreadId.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (Time.realtimeSinceStartup - startRealtime > TimeoutSeconds)
            {
                Fail(
                    "TIMEOUT:minmus_requested=" + requested[0] +
                    ",kerbin_requested=" + requested[1] +
                    ",minmus_durable=" + minmusDurable +
                    ",kerbin_durable=" + kerbinDurable);
            }
        }

        void Fail(string failure)
        {
            if (reported) return;
            reported = true;
            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_LIVE_COMPLETE]" +
                "; pass=false" +
                "; failure=" + Safe(failure) +
                "; expected_total_checks=578" +
                "; path=REAL_PRELOADBUILDER_TO_BLOCKPIPELINE_TO_ENCODE_TO_DB" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; exact_cpu_db_write=false" +
                "; main_thread_id=" +
                    mainThreadId.ToString(CultureInfo.InvariantCulture));
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
