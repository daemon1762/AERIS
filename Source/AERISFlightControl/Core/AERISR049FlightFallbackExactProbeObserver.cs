using System;
using System.Globalization;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;
using AERISFlightControl.Terrain;

namespace AERISFlightControl.Core
{
    // Temporary AERIS49 runtime proof observer.
    // Compiled only by the proof project. It never writes the terrain database.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    internal sealed class AERISR049FlightFallbackExactProbeObserver : MonoBehaviour
    {
        AERISTerrainBlockPipeline pipeline;
        bool started;
        bool finished;
        float startedAt;

        void Update()
        {
            if (finished) return;
            if (!started)
            {
                if (!HighLogic.LoadedSceneIsFlight ||
                    FlightGlobals.ActiveVessel == null ||
                    FlightGlobals.ActiveVessel.mainBody == null ||
                    AERISPerformanceRuntime.Current == null ||
                    !AERISTerrainTileSystem.GameDataHashReady)
                    return;

                CelestialBody body = FlightGlobals.ActiveVessel.mainBody;
                if (!AERISTerrainTileSystem.BodyHasSolidSurface(body)) return;

                string env = AERISTerrainTileSystem.EnvironmentHashForBody(body);
                if (string.IsNullOrEmpty(env)) return;

                double lat = FlightGlobals.ActiveVessel.latitude;
                double lon = FlightGlobals.ActiveVessel.longitude;
                double span = Math.Max(0.01,
                    AERISTerrainTileFormat.AngularSpanDegrees(
                        AERISTerrainTileLod.Far, body.Radius));
                double south = Math.Max(-89.0, lat - span * 0.5);
                double north = Math.Min(89.0, lat + span * 0.5);
                double west = NormalizeLongitude(lon - span * 0.5);
                double east = NormalizeLongitude(lon + span * 0.5);

                var request = new AERISTerrainTileRequest
                {
                    Key = new AERISTerrainTileKey(
                        body.name, body.Radius, env,
                        AERISTerrainTileLod.Far, 987654321, 123456789),
                    Priority = AERISTerrainTilePriority.Critical,
                    CenterLatitudeDeg = lat,
                    CenterLongitudeDeg = lon,
                    SouthLatitudeDeg = south,
                    NorthLatitudeDeg = north,
                    WestLongitudeDeg = west,
                    EastLongitudeDeg = east,
                    Resolution = AERISTerrainTileFormat.Resolution(
                        AERISTerrainTileLod.Far),
                    FinalResolution = AERISTerrainTileFormat.Resolution(
                        AERISTerrainTileLod.Far),
                    Stage = AERISTerrainSamplingStage.Final,
                    Lane = AERISTerrainRequestLane.Viewport,
                    ViewDistanceMeters = 100000.0,
                    RequestSequence = DateTime.UtcNow.Ticks,
                    BodyGeneration = 1,
                    VesselGeneration = 1,
                    TerrainGeneration = 1,
                    ViewGeneration = 1,
                    RangeGeneration = 1,
                    PlanGeneration = 1,
                    DatabaseGeneration = 1,
                    ReadLane = AERISTerrainReadLane.Critical,
                    WorkOwner = AERISTerrainWorkOwner.FlightFallback,
                    Visible = true
                };

                pipeline = new AERISTerrainBlockPipeline(null);
                bool accepted = pipeline.Enqueue(
                    body,
                    request,
                    AERISTerrainTileSource.RealtimeGenerated,
                    env,
                    AERISTerrainTileSystem.GameDataHash,
                    1,
                    current => true,
                    (committedRequest, tile, complete) =>
                    {
                        if (!complete || finished || tile == null) return;
                        finished = true;
                        AERISLogger.Info(
                            "[AERIS49][FLIGHTFALLBACK_EXACT_PROBE]" +
                            "; pass=" + Bool(
                                tile.RuntimeExactCpuPolicyExpected &&
                                tile.RuntimeExactCpuProduced) +
                            "; body=" + Safe(tile.Key.BodyName) +
                            "; work_owner=FlightFallback" +
                            "; runtime_exact_policy_expected=" +
                                Bool(tile.RuntimeExactCpuPolicyExpected) +
                            "; runtime_exact_cpu_produced=" +
                                Bool(tile.RuntimeExactCpuProduced) +
                            "; environment_hash=" + Safe(tile.Key.EnvironmentHash) +
                            "; resolution=" +
                                tile.Resolution.ToString(CultureInfo.InvariantCulture) +
                            "; quality=" +
                                tile.Quality.ToString(CultureInfo.InvariantCulture) +
                            "; sampling_complete=" + Bool(tile.SamplingComplete) +
                            "; persisted=false" +
                            "; probe_only=true");
                        DisposePipeline();
                    });

                if (!accepted)
                {
                    finished = true;
                    AERISLogger.Warn(
                        "[AERIS49][FLIGHTFALLBACK_EXACT_PROBE]" +
                        "; pass=false; failure=ENQUEUE_REJECTED" +
                        "; persisted=false; probe_only=true");
                    DisposePipeline();
                    return;
                }

                started = true;
                startedAt = Time.realtimeSinceStartup;
                AERISLogger.Info(
                    "[AERIS49][FLIGHTFALLBACK_EXACT_PROBE_ARMED]" +
                    "; body=" + Safe(body.name) +
                    "; work_owner=FlightFallback" +
                    "; environment_hash=" + Safe(env) +
                    "; persisted=false; probe_only=true");
            }

            if (pipeline == null || finished) return;

            pipeline.Tick(100000f, 4096, 20f,
                Mathf.Max(0.001f, Time.unscaledDeltaTime));

            if (Time.realtimeSinceStartup - startedAt > 30f)
            {
                finished = true;
                AERISLogger.Warn(
                    "[AERIS49][FLIGHTFALLBACK_EXACT_PROBE]" +
                    "; pass=false; failure=TIMEOUT" +
                    "; persisted=false; probe_only=true");
                DisposePipeline();
            }
        }

        void OnDestroy()
        {
            DisposePipeline();
        }

        void DisposePipeline()
        {
            if (pipeline == null) return;
            try { pipeline.Dispose(); } catch { }
            pipeline = null;
        }

        static double NormalizeLongitude(double longitude)
        {
            while (longitude > 180.0) longitude -= 360.0;
            while (longitude < -180.0) longitude += 360.0;
            return longitude;
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace(';', ',').Replace('|', '/')
                .Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
