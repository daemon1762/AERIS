using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Core;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // Temporary AERIS46 controller for the adjacent-FAR natural parity diagnosis.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR046NaturalParityDiagnosticObserver : MonoBehaviour
    {
        const float TimeoutSeconds = 300f;
        const float RetrySeconds = 0.5f;

        readonly string[] bodies = { "Kerbin", "Eve" };
        readonly bool[] requested = { false, false };
        readonly string[] pairIds = { string.Empty, string.Empty };

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
                TimeoutOrReturn("BOOTSTRAP_OR_TERRAIN_UNAVAILABLE");
                return;
            }

            AERISTerrainTileSystem tiles = bootstrap.Terrain.DisplayTiles;
            if (!tiles.IndexLoaded)
            {
                TimeoutOrReturn("PRELOAD_INDEX_TIMEOUT");
                return;
            }

            if (bootstrap.Settings == null ||
                !bootstrap.Settings.TerrainPreloadEnabled)
            {
                Fail("TERRAIN_PRELOAD_DISABLED");
                return;
            }

            if (!AERISTerrainTileSystem.GameDataHashReady ||
                string.IsNullOrEmpty(AERISTerrainTileSystem.GameDataHash))
            {
                TimeoutOrReturn("ENVIRONMENT_HASH_TIMEOUT");
                return;
            }

            AERISTerrainPreloadBuilder builder = ResolveBuilder(tiles);
            if (builder == null)
            {
                TimeoutOrReturn("PRELOAD_BUILDER_UNAVAILABLE");
                return;
            }

            if (Time.realtimeSinceStartup >= nextRetryRealtime)
            {
                nextRetryRealtime = Time.realtimeSinceStartup + RetrySeconds;
                for (int i = 0; i < bodies.Length; i++)
                {
                    if (requested[i]) continue;
                    string pairId;
                    string failure;
                    bool accepted = builder.R046RequestNaturalParityPair(
                        bodies[i], out pairId, out failure);
                    if (!accepted)
                    {
                        Fail("REQUEST_" + bodies[i] + ":" + failure);
                        return;
                    }
                    requested[i] = true;
                    pairIds[i] = pairId ?? string.Empty;
                }
            }

            bool allComplete = true;
            for (int i = 0; i < bodies.Length; i++)
            {
                if (!requested[i] ||
                    !builder.R046NaturalParityPairComplete(bodies[i]))
                {
                    allComplete = false;
                    break;
                }
            }

            if (allComplete)
            {
                reported = true;
                AERISLogger.Info(
                    "[AERIS46][R043_NATURAL_PAIR_COMPLETE]" +
                    "; pass=true" +
                    "; bodies=2" +
                    "; tiles=4" +
                    "; lod=Far" +
                    "; resolution=33" +
                    "; kerbin_pair=" + Safe(pairIds[0]) +
                    "; eve_pair=" + Safe(pairIds[1]) +
                    "; adjacent_shared_boundary=true" +
                    "; db_write=false" +
                    "; production_authority=PQS" +
                    "; producer_switch=false" +
                    "; exact_cpu_db_write=false" +
                    "; main_thread_id=" +
                        mainThreadId.ToString(CultureInfo.InvariantCulture));
                return;
            }

            TimeoutOrReturn("PAIR_TIMEOUT");
        }

        AERISTerrainPreloadBuilder ResolveBuilder(AERISTerrainTileSystem tiles)
        {
            try
            {
                FieldInfo field = typeof(AERISTerrainTileSystem).GetField(
                    "preloadBuilder",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return field == null ? null :
                    field.GetValue(tiles) as AERISTerrainPreloadBuilder;
            }
            catch
            {
                return null;
            }
        }

        void TimeoutOrReturn(string failure)
        {
            if (Time.realtimeSinceStartup - startRealtime > TimeoutSeconds)
                Fail(failure);
        }

        void Fail(string failure)
        {
            if (reported) return;
            reported = true;
            AERISLogger.Info(
                "[AERIS46][R043_NATURAL_PAIR_COMPLETE]" +
                "; pass=false" +
                "; failure=" + Safe(failure) +
                "; db_write=false" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
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
