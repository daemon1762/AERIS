using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using UnityEngine;
using AERISFlightControl.Logging;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    // R043 PRELOAD_PTC Phase 1 proof-only witness.
    //
    // This observer deliberately does NOT modify the live preload producer, database,
    // tile cache, or authority. It reproduces the exact PreloadBuilder tile-grid
    // coordinate contract on the KSP main thread, captures public PQS TerrainAltitude
    // witnesses plus the stock body-relative direction/map coordinates, copies only
    // primitive scalars + already-certified immutable R042 snapshots to GeneralCompute,
    // and proves bit-exact parity there.
    //
    // Main Thread = body discovery / R042 snapshot / preload-grid coordinates / PQS witness
    // Worker      = accepted pure-CPU exact evaluator / bit comparison
    // Authority   = PQS throughout this phase
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERISR043PreloadPtcShadowTileGridObserver : MonoBehaviour
    {
        const int SamplesPerTileAxis = 7;
        const int SamplesPerTile = SamplesPerTileAxis * SamplesPerTileAxis;
        const int TilesPerBody = 5;
        const int ChecksPerBody = SamplesPerTile * TilesPerBody;
        const int ExpectedBodies = 7;
        const int ExpectedChecks = ChecksPerBody * ExpectedBodies;
        const int CaptureBatchLimit = 24;
        const double CaptureBudgetMilliseconds = 2.0;

        static readonly string[] TargetBodies =
        {
            "Minmus", "Kerbin", "Eve", "Duna", "Dres", "Moho", "Eeloo"
        };

        static readonly AERISTerrainTileLod[] ProbeLods =
        {
            AERISTerrainTileLod.Global,
            AERISTerrainTileLod.Far,
            AERISTerrainTileLod.Route,
            AERISTerrainTileLod.Local,
            AERISTerrainTileLod.Land
        };

        sealed class ProbePoint
        {
            internal string Label;
            internal AERISTerrainTileLod Lod;
            internal int TileLatitudeIndex;
            internal int TileLongitudeIndex;
            internal int Resolution;
            internal int XIndex;
            internal int YIndex;
            internal double Latitude;
            internal double Longitude;
        }

        sealed class Check
        {
            internal string Label;
            internal int Lod;
            internal double Latitude;
            internal double Longitude;
            internal double X;
            internal double Y;
            internal double Z;
            internal double U;
            internal double V;
            internal double ExpectedAsl;
            internal long ExpectedBits;
        }

        sealed class BodyCapture
        {
            internal string BodyName;
            internal CelestialBody Body;
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal ProbePoint[] Points;
            internal readonly List<Check> Checks = new List<Check>(ChecksPerBody);
            internal int NextPoint;
        }

        sealed class BodyPayload
        {
            internal string BodyName;
            internal AERISR042ExactCpuShadowRuntimeSnapshot Snapshot;
            internal Check[] Checks;
        }

        sealed class Payload
        {
            internal int MainThreadId;
            internal BodyPayload[] Bodies;
        }

        sealed class BodyResult
        {
            internal string BodyName;
            internal int Checks;
            internal int Mismatches;
            internal int NonFinite;
            internal double MaxAbsError;
            internal string FirstMismatchLabel;
            internal long FirstExpectedBits;
            internal long FirstActualBits;
        }

        sealed class Result
        {
            internal int MainThreadId;
            internal int WorkerThreadId;
            internal BodyResult[] Bodies;
            internal int TotalChecks;
            internal int TotalMismatches;
            internal int TotalNonFinite;
            internal double MaxAbsError;
            internal string Failure;
        }

        int mainThreadId;
        bool initialized;
        bool submitted;
        bool reported;
        int activeBodyIndex;
        BodyCapture[] captures;
        float nextAttempt;

        void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (reported) return;
            if (Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 0.02f;

            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null || runtime.Scheduler == null) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;

            if (!initialized)
            {
                if (!InitializeCaptures()) return;
                initialized = true;
                AERISLogger.Info(
                    "[AERIS43][R043_PRELOAD_PTC_SHADOW_BEGIN]" +
                    "; bodies=7" +
                    "; lods=GLOBAL,FAR,ROUTE,LOCAL,LAND" +
                    "; tiles_per_body=5" +
                    "; samples_per_tile=49" +
                    "; checks_per_body=245" +
                    "; expected_total_checks=1715" +
                    "; grid_contract=PRELOAD_BUILDER_TILE_BOUNDS_PLUS_BLOCKPIPELINE_LONGITUDE" +
                    "; reference=AERIS_TERRAINAWARENESS_TRYSAMPLETERRAINASLSHARED" +
                    "; coordinate_source=STOCK_GETRELSURFACENVECTOR_PLUS_PQS_MAP_COORDS" +
                    "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                    "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                    Invariants());
            }

            if (!CaptureBatch()) return;
            if (submitted) return;

            Payload payload = BuildPayload();
            if (payload == null)
            {
                Fail("PAYLOAD_BUILD_FAILED");
                return;
            }

            int expectedMainThread = mainThreadId;
            bool accepted = runtime.Scheduler.SubmitRequired(
                AERISRuntimeLane.GeneralCompute,
                "r043-preload-ptc-shadow-tilegrid",
                runtime.CaptureStamp(),
                context => EvaluatePayload(payload, expectedMainThread),
                value => CommitResult(value as Result),
                false);
            if (!accepted) return;

            submitted = true;
            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_SHADOW_SUBMITTED]" +
                "; bodies=7" +
                "; total_checks=1715" +
                "; lane=GeneralCompute" +
                "; commit_required=true" +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        bool InitializeCaptures()
        {
            captures = new BodyCapture[TargetBodies.Length];
            for (int i = 0; i < TargetBodies.Length; i++)
            {
                string bodyName = TargetBodies[i];
                CelestialBody body = FindBody(bodyName);
                if (body == null)
                {
                    Fail("BODY_NOT_FOUND:" + bodyName);
                    return false;
                }

                AERISR042ExactCpuShadowRuntimeSnapshot snapshot;
                string failure;
                if (!AERISR042ExactCpuShadowRuntimeSnapshotBuilder.TryCapture(
                    body, mainThreadId, out snapshot, out failure))
                {
                    Fail("SNAPSHOT:" + bodyName + ":" + failure);
                    return false;
                }

                ProbePoint[] points = BuildProbePoints(body);
                if (points == null || points.Length != ChecksPerBody)
                {
                    Fail("PROBE_COUNT:" + bodyName + ":" +
                        (points == null ? "0" : points.Length.ToString(CultureInfo.InvariantCulture)));
                    return false;
                }

                captures[i] = new BodyCapture
                {
                    BodyName = bodyName,
                    Body = body,
                    Snapshot = snapshot,
                    Points = points
                };
            }
            return true;
        }

        bool CaptureBatch()
        {
            if (captures == null || captures.Length != ExpectedBodies) return false;
            int captured = 0;
            long start = Stopwatch.GetTimestamp();
            long budgetTicks = Math.Max(1L, (long)Math.Ceiling(
                CaptureBudgetMilliseconds * Stopwatch.Frequency / 1000.0));

            while (activeBodyIndex < captures.Length && captured < CaptureBatchLimit &&
                Stopwatch.GetTimestamp() - start < budgetTicks)
            {
                BodyCapture body = captures[activeBodyIndex];
                if (body == null)
                {
                    Fail("NULL_BODY_CAPTURE:" + activeBodyIndex.ToString(CultureInfo.InvariantCulture));
                    return false;
                }

                if (body.NextPoint >= body.Points.Length)
                {
                    if (body.Checks.Count != ChecksPerBody)
                    {
                        Fail("BODY_CAPTURE_COUNT:" + body.BodyName + ":" +
                            body.Checks.Count.ToString(CultureInfo.InvariantCulture));
                        return false;
                    }
                    AERISLogger.Info(
                        "[AERIS43][R043_PRELOAD_PTC_SHADOW_CAPTURE_BODY]" +
                        "; body=" + Safe(body.BodyName) +
                        "; pass=true" +
                        "; checks=245" +
                        "; lods=5" +
                        "; tiles=5" +
                        "; snapshot_candidate=" + body.Snapshot.Candidate +
                        "; capture_thread_is_main=" + Bool(
                            body.Snapshot.CaptureThreadId == mainThreadId) +
                        "; runtime_object_retained=false" +
                        Invariants());
                    activeBodyIndex++;
                    continue;
                }

                ProbePoint point = body.Points[body.NextPoint++];
                double expectedAsl;
                if (!AERISTerrainAwareness.TrySampleTerrainAslShared(
                    body.Body, point.Latitude, point.Longitude, out expectedAsl))
                {
                    Fail("PUBLIC_PQS_REFERENCE_FAILED:" + body.BodyName + ":" + point.Label);
                    return false;
                }

                Vector3d inputDirection = body.Body.GetRelSurfaceNVector(
                    point.Latitude, point.Longitude);
                Vector3d direction = inputDirection.normalized;
                double u;
                double v;
                ComputeStockMapCoords(direction, out u, out v);

                body.Checks.Add(new Check
                {
                    Label = point.Label,
                    Lod = (int)point.Lod,
                    Latitude = point.Latitude,
                    Longitude = point.Longitude,
                    X = direction.x,
                    Y = direction.y,
                    Z = direction.z,
                    U = u,
                    V = v,
                    ExpectedAsl = expectedAsl,
                    ExpectedBits = BitConverter.DoubleToInt64Bits(expectedAsl)
                });
                captured++;
            }

            return activeBodyIndex >= captures.Length;
        }

        Payload BuildPayload()
        {
            if (captures == null || captures.Length != ExpectedBodies) return null;
            var bodies = new BodyPayload[captures.Length];
            int total = 0;
            for (int i = 0; i < captures.Length; i++)
            {
                BodyCapture capture = captures[i];
                if (capture == null || capture.Snapshot == null ||
                    capture.Checks.Count != ChecksPerBody) return null;
                bodies[i] = new BodyPayload
                {
                    BodyName = capture.BodyName,
                    Snapshot = capture.Snapshot,
                    Checks = capture.Checks.ToArray()
                };
                total += bodies[i].Checks.Length;
            }
            if (total != ExpectedChecks) return null;
            return new Payload { MainThreadId = mainThreadId, Bodies = bodies };
        }

        static Result EvaluatePayload(Payload payload, int expectedMainThread)
        {
            var result = new Result
            {
                MainThreadId = expectedMainThread,
                WorkerThreadId = Thread.CurrentThread.ManagedThreadId,
                Bodies = payload == null || payload.Bodies == null ?
                    new BodyResult[0] : new BodyResult[payload.Bodies.Length]
            };

            try
            {
                if (payload == null || payload.Bodies == null)
                    throw new InvalidOperationException("PAYLOAD_NULL");

                for (int i = 0; i < payload.Bodies.Length; i++)
                {
                    BodyPayload body = payload.Bodies[i];
                    if (body == null || body.Snapshot == null || body.Checks == null)
                        throw new InvalidOperationException("BODY_PAYLOAD_NULL_" + i);

                    var bodyResult = new BodyResult
                    {
                        BodyName = body.BodyName,
                        Checks = body.Checks.Length
                    };

                    for (int c = 0; c < body.Checks.Length; c++)
                    {
                        Check check = body.Checks[c];
                        double absolute;
                        if (body.Snapshot.Candidate ==
                            AERISR042ExactCpuShadowSourceResolver.CandidateKind.R039MinmusVertexPlanet)
                        {
                            absolute = AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                                body.Snapshot.MinmusSnapshot,
                                check.X, check.Y, check.Z,
                                body.Snapshot.PqsRadius);
                        }
                        else if (body.Snapshot.Candidate ==
                            AERISR042ExactCpuShadowSourceResolver.CandidateKind.R041HeightModifierChain)
                        {
                            absolute = AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                                body.Snapshot.HeightChainSnapshot,
                                check.X, check.Y, check.Z,
                                check.U, check.V,
                                body.Snapshot.PqsRadius);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                body.BodyName + "_UNSUPPORTED_CANDIDATE");
                        }

                        double actualAsl = absolute - body.Snapshot.PqsRadius;
                        if (actualAsl < 0.0) actualAsl = 0.0;
                        long actualBits = BitConverter.DoubleToInt64Bits(actualAsl);

                        bool finite =
                            !double.IsNaN(actualAsl) && !double.IsInfinity(actualAsl) &&
                            !double.IsNaN(check.ExpectedAsl) && !double.IsInfinity(check.ExpectedAsl);
                        double error = finite ? Math.Abs(actualAsl - check.ExpectedAsl) :
                            double.PositiveInfinity;
                        if (error > bodyResult.MaxAbsError) bodyResult.MaxAbsError = error;
                        if (error > result.MaxAbsError) result.MaxAbsError = error;
                        if (!finite)
                        {
                            bodyResult.NonFinite++;
                            result.TotalNonFinite++;
                        }

                        if (actualBits != check.ExpectedBits || !finite)
                        {
                            bodyResult.Mismatches++;
                            result.TotalMismatches++;
                            if (string.IsNullOrEmpty(bodyResult.FirstMismatchLabel))
                            {
                                bodyResult.FirstMismatchLabel = check.Label;
                                bodyResult.FirstExpectedBits = check.ExpectedBits;
                                bodyResult.FirstActualBits = actualBits;
                            }
                        }
                        result.TotalChecks++;
                    }
                    result.Bodies[i] = bodyResult;
                }
            }
            catch (Exception ex)
            {
                result.Failure = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
            }
            return result;
        }

        void CommitResult(Result result)
        {
            if (reported) return;
            reported = true;

            if (result == null)
            {
                AERISLogger.Info(
                    "[AERIS43][R043_PRELOAD_PTC_SHADOW_COMPLETE]" +
                    "; pass=false; failure=NULL_WORKER_RESULT" + Invariants());
                return;
            }

            bool workerNotMain = result.WorkerThreadId > 0 &&
                result.WorkerThreadId != mainThreadId;
            bool pass = string.IsNullOrEmpty(result.Failure) &&
                result.Bodies != null && result.Bodies.Length == ExpectedBodies &&
                result.TotalChecks == ExpectedChecks &&
                result.TotalMismatches == 0 && result.TotalNonFinite == 0 &&
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
                    bool bodyPass = body.Checks == ChecksPerBody &&
                        body.Mismatches == 0 && body.NonFinite == 0;
                    pass &= bodyPass;
                    AERISLogger.Info(
                        "[AERIS43][R043_PRELOAD_PTC_SHADOW_BODY]" +
                        "; body=" + Safe(body.BodyName) +
                        "; pass=" + Bool(bodyPass) +
                        "; checks=" + body.Checks.ToString(CultureInfo.InvariantCulture) +
                        "; mismatch_count=" + body.Mismatches.ToString(CultureInfo.InvariantCulture) +
                        "; nonfinite_count=" + body.NonFinite.ToString(CultureInfo.InvariantCulture) +
                        "; bit_exact=" + Bool(body.Mismatches == 0 && body.NonFinite == 0) +
                        "; max_abs_error=" + R(body.MaxAbsError) +
                        "; first_mismatch=" + Safe(body.FirstMismatchLabel) +
                        "; first_expected_bits=" +
                            unchecked((ulong)body.FirstExpectedBits).ToString("x16", CultureInfo.InvariantCulture) +
                        "; first_actual_bits=" +
                            unchecked((ulong)body.FirstActualBits).ToString("x16", CultureInfo.InvariantCulture) +
                        "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                        "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                        "; worker_not_main=" + Bool(workerNotMain) +
                        Invariants());
                }
            }

            AERISLogger.Info(
                "[AERIS43][R043_PRELOAD_PTC_SHADOW_COMPLETE]" +
                "; pass=" + Bool(pass) +
                "; bodies=" + (result.Bodies == null ? "0" :
                    result.Bodies.Length.ToString(CultureInfo.InvariantCulture)) +
                "; total_checks=" + result.TotalChecks.ToString(CultureInfo.InvariantCulture) +
                "; mismatch_count=" + result.TotalMismatches.ToString(CultureInfo.InvariantCulture) +
                "; nonfinite_count=" + result.TotalNonFinite.ToString(CultureInfo.InvariantCulture) +
                "; bit_exact=" + Bool(result.TotalMismatches == 0 && result.TotalNonFinite == 0) +
                "; max_abs_error=" + R(result.MaxAbsError) +
                "; worker_thread_id=" + result.WorkerThreadId.ToString(CultureInfo.InvariantCulture) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                "; worker_not_main=" + Bool(workerNotMain) +
                "; worker_scheduler=AERIS_SHARED_GENERAL_COMPUTE" +
                "; worker_runtime_object_access=false" +
                "; failure=" + Safe(result.Failure) +
                Invariants());
        }

        ProbePoint[] BuildProbePoints(CelestialBody body)
        {
            var points = new List<ProbePoint>(ChecksPerBody);
            for (int l = 0; l < ProbeLods.Length; l++)
            {
                AERISTerrainTileLod lod = ProbeLods[l];
                int latCount = AERISTerrainTileSystem.LatitudeTileCountFor(body, lod);
                int lonCount = AERISTerrainTileSystem.LongitudeTileCountFor(body, lod);
                if (latCount <= 0 || lonCount <= 0)
                    throw new InvalidOperationException(body.name + "_INVALID_TILE_GRID_" + lod);

                int tileLat;
                int tileLon;
                SelectTile(lod, latCount, lonCount, out tileLat, out tileLon);

                double span = AERISTerrainTileFormat.AngularSpanDegrees(lod, body.Radius);
                double south = Math.Max(-90.0, -90.0 + tileLat * span);
                double north = Math.Min(90.0, south + span);
                double west = NormalizeLongitude(-180.0 + tileLon * span);
                double east = NormalizeLongitude(west + span);
                int resolution = AERISTerrainTileFormat.Resolution(lod);
                int[] indices = ProbeIndices(resolution);
                if (indices.Length != SamplesPerTileAxis)
                    throw new InvalidOperationException("PROBE_AXIS_COUNT_" + resolution);

                for (int yi = 0; yi < indices.Length; yi++)
                {
                    for (int xi = 0; xi < indices.Length; xi++)
                    {
                        int x = indices[xi];
                        int y = indices[yi];
                        double u = x / (double)(resolution - 1);
                        double v = y / (double)(resolution - 1);
                        double latitude = south + (north - south) * v;
                        double longitude = InterpolateLongitude(west, east, u);
                        points.Add(new ProbePoint
                        {
                            Label = lod.ToString().ToUpperInvariant() + "_T" +
                                tileLat.ToString(CultureInfo.InvariantCulture) + "_" +
                                tileLon.ToString(CultureInfo.InvariantCulture) + "_XY" +
                                x.ToString(CultureInfo.InvariantCulture) + "_" +
                                y.ToString(CultureInfo.InvariantCulture),
                            Lod = lod,
                            TileLatitudeIndex = tileLat,
                            TileLongitudeIndex = tileLon,
                            Resolution = resolution,
                            XIndex = x,
                            YIndex = y,
                            Latitude = latitude,
                            Longitude = longitude
                        });
                    }
                }
            }
            return points.ToArray();
        }

        static void SelectTile(AERISTerrainTileLod lod, int latCount, int lonCount,
            out int latitudeIndex, out int longitudeIndex)
        {
            switch (lod)
            {
                case AERISTerrainTileLod.Global:
                    latitudeIndex = 0;
                    longitudeIndex = 0;
                    break;
                case AERISTerrainTileLod.Far:
                    latitudeIndex = Math.Max(0, latCount - 1);
                    longitudeIndex = Math.Max(0, lonCount - 1);
                    break;
                case AERISTerrainTileLod.Route:
                    latitudeIndex = latCount / 2;
                    longitudeIndex = Math.Max(0, lonCount - 1);
                    break;
                case AERISTerrainTileLod.Local:
                    latitudeIndex = latCount / 2;
                    longitudeIndex = lonCount / 2;
                    break;
                default:
                    latitudeIndex = Math.Min(Math.Max(0, latCount - 1), latCount * 2 / 3);
                    longitudeIndex = Math.Min(Math.Max(0, lonCount - 1), lonCount / 3);
                    break;
            }
        }

        static int[] ProbeIndices(int resolution)
        {
            int last = Math.Max(0, resolution - 1);
            int[] candidates =
            {
                0,
                Math.Min(last, 1),
                last / 4,
                last / 2,
                last * 3 / 4,
                Math.Max(0, last - 1),
                last
            };
            var result = new List<int>(SamplesPerTileAxis);
            for (int i = 0; i < candidates.Length; i++)
                if (!result.Contains(candidates[i])) result.Add(candidates[i]);
            return result.ToArray();
        }

        static void ComputeStockMapCoords(Vector3d direction, out double u, out double v)
        {
            double latitudeRad = Math.Asin(direction.y);
            if (double.IsNaN(latitudeRad)) latitudeRad = Math.PI * 0.5;

            Vector3d directionXZ = new Vector3d(direction.x, 0.0, direction.z).normalized;
            double directionXZMagnitude = directionXZ.magnitude;
            double longitudeRad;
            if (directionXZMagnitude > 0.0)
            {
                double longitudeBasis = directionXZ.x / directionXZMagnitude;
                longitudeRad = directionXZ.z < 0.0
                    ? Math.PI - Math.Asin(longitudeBasis)
                    : Math.Asin(longitudeBasis);
            }
            else
            {
                longitudeRad = 0.0;
            }

            u = longitudeRad / Math.PI * 0.5;
            v = latitudeRad / Math.PI + 0.5;
        }

        static double InterpolateLongitude(double west, double east, double t)
        {
            double delta = NormalizeLongitude(east - west);
            if (delta <= 0.0) delta += 360.0;
            return NormalizeLongitude(west + delta * t);
        }

        static double NormalizeLongitude(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        CelestialBody FindBody(string name)
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
                "[AERIS43][R043_PRELOAD_PTC_SHADOW_COMPLETE]" +
                "; pass=false" +
                "; failure=" + Safe(failure) +
                "; main_thread_id=" + mainThreadId.ToString(CultureInfo.InvariantCulture) +
                Invariants());
        }

        static string Invariants()
        {
            return
                "; stage=PRELOAD_PTC_SHADOW_TILEGRID" +
                "; preload_probe=DETERMINISTIC_TILE_GRID_SHADOW_ONLY" +
                "; snapshot_payload=PRIMITIVES_PLUS_ACCEPTED_PURE_SNAPSHOTS_ONLY" +
                "; production_authority=PQS" +
                "; producer_switch=false" +
                "; db_authority=PQS" +
                "; exact_cpu_db_write=false" +
                "; production_preload_path_mutated=false";
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

        static string R(double value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
