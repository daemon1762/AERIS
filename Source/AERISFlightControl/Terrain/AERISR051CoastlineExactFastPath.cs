using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace AERISFlightControl.Terrain
{
    // AERIS51 C2: Exact-CPU high-density coastline fast path.
    //
    // Main thread:
    // - certifies the already-accepted R047 Exact CPU producer;
    // - captures three body-relative basis vectors plus one reconstruction witness;
    // - publishes only immutable primitive data to the worker.
    //
    // GeneralCompute worker:
    // - reconstructs every 129x129 body-relative direction with pure math;
    // - evaluates the accepted Exact CPU terrain snapshot directly;
    // - emits the land/water mask and coastline vector in one batch.
    //
    // No CelestialBody/PQS/PQSMod/Unity runtime object is retained by the worker payload.
    internal sealed partial class AERISTerrainBlockPipeline
    {
        internal sealed class R051CoastlineExactSnapshot
        {
            internal readonly AERISR042ExactCpuShadowRuntimeSnapshot Terrain;
            internal readonly string BodyName;
            internal readonly string EnvironmentHash;
            internal readonly bool HasOcean;
            internal readonly int MainThreadId;
            internal readonly double E0X, E0Y, E0Z;
            internal readonly double E90X, E90Y, E90Z;
            internal readonly double NorthX, NorthY, NorthZ;

            internal R051CoastlineExactSnapshot(
                AERISR042ExactCpuShadowRuntimeSnapshot terrain,
                string bodyName,
                string environmentHash,
                bool hasOcean,
                int mainThreadId,
                double e0x, double e0y, double e0z,
                double e90x, double e90y, double e90z,
                double northX, double northY, double northZ)
            {
                Terrain = terrain;
                BodyName = bodyName ?? string.Empty;
                EnvironmentHash = environmentHash ?? string.Empty;
                HasOcean = hasOcean;
                MainThreadId = mainThreadId;
                E0X = e0x; E0Y = e0y; E0Z = e0z;
                E90X = e90x; E90Y = e90y; E90Z = e90z;
                NorthX = northX; NorthY = northY; NorthZ = northZ;
            }

            internal bool IsValid
            {
                get
                {
                    return Terrain != null && Terrain.IsStructurallyValid &&
                        MainThreadId > 0 &&
                        R051Finite(E0X) && R051Finite(E0Y) && R051Finite(E0Z) &&
                        R051Finite(E90X) && R051Finite(E90Y) && R051Finite(E90Z) &&
                        R051Finite(NorthX) && R051Finite(NorthY) && R051Finite(NorthZ);
                }
            }
        }

        internal sealed class R051CoastlineExactResult
        {
            internal string Error = string.Empty;
            internal int WorkerThreadId;
            internal int Samples;
            internal double WorkerMilliseconds;
            internal byte[] Flags = new byte[0];
            internal float[] Segments = new float[0];
        }

        readonly Dictionary<string, R051CoastlineExactSnapshot>
            r051CoastlineExactSnapshotCache =
                new Dictionary<string, R051CoastlineExactSnapshot>(
                    StringComparer.Ordinal);

        internal bool R051TryCaptureCoastlineExactSnapshot(
            CelestialBody body,
            string environmentHash,
            out R051CoastlineExactSnapshot snapshot,
            out string failure)
        {
            snapshot = null;
            failure = string.Empty;
            if (body == null)
            {
                failure = "BODY_NULL";
                return false;
            }
            if (Thread.CurrentThread.ManagedThreadId != r040bMainThreadId)
            {
                failure = "CAPTURE_NOT_MAIN_THREAD";
                return false;
            }

            string bodyName = body.name ?? string.Empty;
            string cacheKey = bodyName + "|" + (environmentHash ?? string.Empty);
            if (r051CoastlineExactSnapshotCache.TryGetValue(cacheKey, out snapshot) &&
                snapshot != null && snapshot.IsValid)
                return true;

            var request = new AERISTerrainTileRequest
            {
                WorkOwner = AERISTerrainWorkOwner.PreloadBuilder,
                Resolution = 2,
                FinalResolution = 2
            };
            R043PtcTileState ptc =
                TryCreateR043PtcState(body, request, environmentHash);
            if (ptc == null || ptc.Snapshot == null ||
                !ptc.Snapshot.IsStructurallyValid || !ptc.ProductionEnabled)
            {
                failure = "EXACT_CPU_NOT_SELECTED";
                snapshot = null;
                return false;
            }

            try
            {
                Vector3d e0 = body.GetRelSurfaceNVector(0.0, 0.0).normalized;
                Vector3d e90 = body.GetRelSurfaceNVector(0.0, 90.0).normalized;
                Vector3d north = body.GetRelSurfaceNVector(90.0, 0.0).normalized;
                if (!R051FiniteVector(e0) || !R051FiniteVector(e90) ||
                    !R051FiniteVector(north))
                {
                    failure = "BASIS_NONFINITE";
                    return false;
                }

                // Runtime proof that the three captured basis vectors reproduce KSP's
                // current body-relative latitude/longitude convention. This prevents a
                // guessed coordinate convention from ever becoming terrain authority.
                const double witnessLat = 23.456789;
                const double witnessLon = -67.891234;
                Vector3d actual =
                    body.GetRelSurfaceNVector(witnessLat, witnessLon).normalized;
                double wx, wy, wz;
                R051DirectionFromBasis(
                    e0.x, e0.y, e0.z,
                    e90.x, e90.y, e90.z,
                    north.x, north.y, north.z,
                    witnessLat, witnessLon,
                    out wx, out wy, out wz);
                double mismatch = Math.Max(
                    Math.Abs(actual.x - wx),
                    Math.Max(Math.Abs(actual.y - wy), Math.Abs(actual.z - wz)));
                if (!R051Finite(mismatch) || mismatch > 1e-10)
                {
                    failure = "BASIS_RECONSTRUCTION_MISMATCH:" +
                        mismatch.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                    return false;
                }

                snapshot = new R051CoastlineExactSnapshot(
                    ptc.Snapshot,
                    bodyName,
                    environmentHash,
                    body.ocean,
                    r040bMainThreadId,
                    e0.x, e0.y, e0.z,
                    e90.x, e90.y, e90.z,
                    north.x, north.y, north.z);
                if (!snapshot.IsValid)
                {
                    failure = "SNAPSHOT_INVALID";
                    snapshot = null;
                    return false;
                }

                r051CoastlineExactSnapshotCache[cacheKey] = snapshot;
                return true;
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
                snapshot = null;
                return false;
            }
        }

        internal static R051CoastlineExactResult R051BuildExactCoastline(
            R051CoastlineExactSnapshot snapshot,
            double southLatitudeDeg,
            double northLatitudeDeg,
            double westLongitudeDeg,
            double eastLongitudeDeg,
            int resolution)
        {
            var result = new R051CoastlineExactResult();
            result.WorkerThreadId = Thread.CurrentThread.ManagedThreadId;
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                if (snapshot == null || !snapshot.IsValid)
                    throw new InvalidOperationException("SNAPSHOT_INVALID");
                if (result.WorkerThreadId == snapshot.MainThreadId)
                    throw new InvalidOperationException("WORKER_IS_MAIN");
                if (resolution < 2)
                    throw new InvalidOperationException("RESOLUTION_INVALID");

                int count = checked(resolution * resolution);
                var elevation = new float[count];
                var flags = new byte[count];

                for (int row = 0; row < resolution; row++)
                {
                    double v = row / (double)(resolution - 1);
                    double latitude = southLatitudeDeg +
                        (northLatitudeDeg - southLatitudeDeg) * v;
                    for (int column = 0; column < resolution; column++)
                    {
                        double u01 = column / (double)(resolution - 1);
                        double longitude =
                            InterpolateLongitude(westLongitudeDeg, eastLongitudeDeg, u01);
                        double x, y, z;
                        R051DirectionFromBasis(
                            snapshot.E0X, snapshot.E0Y, snapshot.E0Z,
                            snapshot.E90X, snapshot.E90Y, snapshot.E90Z,
                            snapshot.NorthX, snapshot.NorthY, snapshot.NorthZ,
                            latitude, longitude,
                            out x, out y, out z);

                        double mapU, mapV;
                        R051ComputeStockMapCoords(x, y, z, out mapU, out mapV);

                        double absolute;
                        if (snapshot.Terrain.Candidate ==
                            AERISR042ExactCpuShadowSourceResolver.CandidateKind.
                                R039MinmusVertexPlanet)
                        {
                            absolute =
                                AERISR039MinmusPureCpuExact.EvaluateVertexPlanet(
                                    snapshot.Terrain.MinmusSnapshot,
                                    x, y, z,
                                    snapshot.Terrain.PqsRadius);
                        }
                        else if (snapshot.Terrain.Candidate ==
                            AERISR042ExactCpuShadowSourceResolver.CandidateKind.
                                R041HeightModifierChain)
                        {
                            absolute =
                                AERIS39AllBodyHeightModifierChainPureCpuExact.Evaluate(
                                    snapshot.Terrain.HeightChainSnapshot,
                                    x, y, z, mapU, mapV,
                                    snapshot.Terrain.PqsRadius);
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                "UNSUPPORTED_CANDIDATE:" + snapshot.Terrain.Candidate);
                        }

                        double asl = absolute - snapshot.Terrain.PqsRadius;
                        if (!R051Finite(asl))
                            throw new InvalidOperationException("EXACT_CPU_NONFINITE");
                        if (asl < 0.0) asl = 0.0;

                        int index = row * resolution + column;
                        elevation[index] = (float)asl;
                        flags[index] =
                            snapshot.HasOcean && asl <= 1.0 ? (byte)2 : (byte)1;
                        result.Samples++;
                    }
                }

                var temporary = new AERISTerrainHeightTile
                {
                    Resolution = resolution,
                    Elevation = elevation,
                    Flags = flags
                };
                result.Flags = flags;
                result.Segments =
                    AERISTerrainCoastlineExtractor.Build(temporary) ??
                    new float[0];
            }
            catch (Exception ex)
            {
                result.Error =
                    ex.GetType().Name + ":" + (ex.Message ?? string.Empty);
                result.Flags = new byte[0];
                result.Segments = new float[0];
            }
            finally
            {
                watch.Stop();
                result.WorkerMilliseconds = watch.Elapsed.TotalMilliseconds;
            }
            return result;
        }

        static void R051DirectionFromBasis(
            double e0x, double e0y, double e0z,
            double e90x, double e90y, double e90z,
            double nx, double ny, double nz,
            double latitudeDeg, double longitudeDeg,
            out double x, out double y, out double z)
        {
            double lat = latitudeDeg * Math.PI / 180.0;
            double lon = longitudeDeg * Math.PI / 180.0;
            double cosLat = Math.Cos(lat);
            double a = cosLat * Math.Cos(lon);
            double b = cosLat * Math.Sin(lon);
            double c = Math.Sin(lat);
            x = a * e0x + b * e90x + c * nx;
            y = a * e0y + b * e90y + c * ny;
            z = a * e0z + b * e90z + c * nz;
            double magnitude = Math.Sqrt(x * x + y * y + z * z);
            if (!R051Finite(magnitude) || magnitude <= 0.0)
                throw new InvalidOperationException("DIRECTION_INVALID");
            x /= magnitude;
            y /= magnitude;
            z /= magnitude;
        }

        static void R051ComputeStockMapCoords(
            double x, double y, double z,
            out double u, out double v)
        {
            double clampedY = Math.Max(-1.0, Math.Min(1.0, y));
            double latitudeRad = Math.Asin(clampedY);
            double xz = Math.Sqrt(x * x + z * z);
            double longitudeRad;
            if (xz > 0.0)
            {
                double longitudeBasis =
                    Math.Max(-1.0, Math.Min(1.0, x / xz));
                longitudeRad = z < 0.0
                    ? Math.PI - Math.Asin(longitudeBasis)
                    : Math.Asin(longitudeBasis);
            }
            else longitudeRad = 0.0;

            u = longitudeRad / Math.PI * 0.5;
            v = latitudeRad / Math.PI + 0.5;
        }

        static bool R051FiniteVector(Vector3d value)
        {
            return R051Finite(value.x) &&
                R051Finite(value.y) &&
                R051Finite(value.z);
        }

        static bool R051Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
