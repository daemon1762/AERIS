using System;
using System.Collections.Generic;
using UnityEngine;

namespace AERISFlightControl.Terrain
{
    // Immutable planning point emitted by the Gate 3 constant-turn-rate predictor.
    // It contains no payload and performs no disk I/O.
    internal sealed class AERISPredictiveCorridorPoint
    {
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal AERISTerrainTileLod Lod;
        internal AERISTerrainTilePriority Priority;
        internal double HorizonSeconds;
        internal double CorridorHalfWidthMeters;
        internal bool Centerline;
    }

    internal sealed class AERISPredictiveForwardCorridorSnapshot
    {
        internal bool Active;
        internal double GroundSpeedMetersPerSecond;
        internal double GroundTrackDeg;
        internal double TurnRateDegPerSecond;
        internal double LookAheadSeconds;
        internal double LookAheadDistanceMeters;
        internal double MaximumHalfWidthMeters;
        internal int CenterlinePoints;
        internal int TotalPoints;
        internal int RequestedTiles;
        internal int PinnedTiles;
        internal bool LandDemandActive;
        internal string Status = "INACTIVE";
    }

    // Predicts a bounded forward corridor from current ground track, horizontal
    // surface speed and yaw rate. The model is deliberately control-free: it only
    // describes likely future map positions and never writes to vessel controls.
    internal sealed class AERISPredictiveForwardCorridor
    {
        const double MinimumGroundSpeed = 5.0;
        const double MaximumTurnRateDegPerSecond = 12.0;
        const int MaximumPointCount = 18;
        readonly List<AERISPredictiveCorridorPoint> points =
            new List<AERISPredictiveCorridorPoint>(MaximumPointCount);
        AERISPredictiveForwardCorridorSnapshot snapshot =
            new AERISPredictiveForwardCorridorSnapshot();

        internal IList<AERISPredictiveCorridorPoint> Build(CelestialBody body,
            Vessel vessel, double mapRangeMeters, AERISTerrainTileLod nearLod,
            bool landDemandActive)
        {
            points.Clear();
            snapshot = new AERISPredictiveForwardCorridorSnapshot
            {
                LandDemandActive = landDemandActive
            };
            if (body == null || vessel == null ||
                !Finite(vessel.latitude) || !Finite(vessel.longitude))
            {
                snapshot.Status = "NO ACTIVE VESSEL";
                return points;
            }

            double speed = ResolveHorizontalGroundSpeed(vessel, body);
            double heading = AERISTerrainAwareness.ResolveMapHeading(vessel);
            double turnRate = ResolveTurnRate(vessel, body);
            snapshot.GroundSpeedMetersPerSecond = speed;
            snapshot.GroundTrackDeg = heading;
            snapshot.TurnRateDegPerSecond = turnRate;
            if (speed < MinimumGroundSpeed)
            {
                snapshot.Status = "GROUND SPEED BELOW CORRIDOR THRESHOLD";
                return points;
            }

            double range = Math.Max(5000.0, Math.Min(250000.0,
                Finite(mapRangeMeters) ? mapRangeMeters : 20000.0));
            double targetDistance = Math.Max(range * 1.5, speed * 90.0);
            targetDistance = Math.Max(range * 0.75,
                Math.Min(Math.Min(250000.0, range * 4.0), targetDistance));
            double horizon = Math.Max(30.0, Math.Min(420.0,
                targetDistance / Math.Max(MinimumGroundSpeed, speed)));
            targetDistance = speed * horizon;
            snapshot.LookAheadSeconds = horizon;
            snapshot.LookAheadDistanceMeters = targetDistance;

            double predictionTurnRate = Math.Max(-135.0 / horizon,
                Math.Min(135.0 / horizon, turnRate));
            double[] fractions = { 0.12, 0.25, 0.45, 0.70, 1.00 };
            for (int i = 0; i < fractions.Length && points.Count < MaximumPointCount; i++)
            {
                double t = Math.Max(1.0, horizon * fractions[i]);
                double east, north, course;
                PredictLocalOffset(speed, heading, predictionTurnRate, t,
                    out east, out north, out course);
                double centerLat, centerLon;
                OffsetLatLon(body, vessel.latitude, vessel.longitude,
                    east, north, out centerLat, out centerLon);
                if (!Finite(centerLat) || !Finite(centerLon)) continue;

                AERISTerrainTileLod lod = ResolveLod(i, nearLod);
                AERISTerrainTilePriority priority = i <= 1 ?
                    AERISTerrainTilePriority.High :
                    (i <= 3 ? AERISTerrainTilePriority.Normal :
                        AERISTerrainTilePriority.Low);
                double distance = Math.Sqrt(east * east + north * north);
                double halfWidth = ResolveHalfWidth(speed, turnRate, t,
                    distance, lod);
                snapshot.MaximumHalfWidthMeters = Math.Max(
                    snapshot.MaximumHalfWidthMeters, halfWidth);
                Add(centerLat, centerLon, lod, priority, t, halfWidth, true);

                // The lateral edges make the request robust against turn-rate change,
                // wind drift and short pilot inputs without exploding into a disc.
                if (i < 4 && points.Count + 2 <= MaximumPointCount)
                {
                    double leftLat, leftLon, rightLat, rightLon;
                    OffsetLatLon(body, centerLat, centerLon,
                        Math.Sin((course - 90.0) * Math.PI / 180.0) * halfWidth,
                        Math.Cos((course - 90.0) * Math.PI / 180.0) * halfWidth,
                        out leftLat, out leftLon);
                    OffsetLatLon(body, centerLat, centerLon,
                        Math.Sin((course + 90.0) * Math.PI / 180.0) * halfWidth,
                        Math.Cos((course + 90.0) * Math.PI / 180.0) * halfWidth,
                        out rightLat, out rightLon);
                    Add(leftLat, leftLon, lod, priority, t, halfWidth, false);
                    Add(rightLat, rightLon, lod, priority, t, halfWidth, false);
                }
            }

            snapshot.Active = points.Count > 0;
            snapshot.TotalPoints = points.Count;
            snapshot.Status = snapshot.Active ?
                (Math.Abs(turnRate) >= 0.15 ? "CURVED FORWARD CORRIDOR" :
                    "STRAIGHT FORWARD CORRIDOR") : "NO VALID CORRIDOR POINTS";
            return points;
        }

        internal void SetRuntimeCounts(int requestedTiles, int pinnedTiles)
        {
            snapshot.RequestedTiles = Math.Max(0, requestedTiles);
            snapshot.PinnedTiles = Math.Max(0, pinnedTiles);
        }

        internal AERISPredictiveForwardCorridorSnapshot Snapshot()
        {
            return new AERISPredictiveForwardCorridorSnapshot
            {
                Active = snapshot.Active,
                GroundSpeedMetersPerSecond = snapshot.GroundSpeedMetersPerSecond,
                GroundTrackDeg = snapshot.GroundTrackDeg,
                TurnRateDegPerSecond = snapshot.TurnRateDegPerSecond,
                LookAheadSeconds = snapshot.LookAheadSeconds,
                LookAheadDistanceMeters = snapshot.LookAheadDistanceMeters,
                MaximumHalfWidthMeters = snapshot.MaximumHalfWidthMeters,
                CenterlinePoints = snapshot.CenterlinePoints,
                TotalPoints = snapshot.TotalPoints,
                RequestedTiles = snapshot.RequestedTiles,
                PinnedTiles = snapshot.PinnedTiles,
                LandDemandActive = snapshot.LandDemandActive,
                Status = snapshot.Status
            };
        }

        internal void Reset(string reason)
        {
            points.Clear();
            snapshot = new AERISPredictiveForwardCorridorSnapshot
            {
                Status = string.IsNullOrEmpty(reason) ? "INACTIVE" :
                    "INACTIVE — " + reason.ToUpperInvariant()
            };
        }

        void Add(double latitude, double longitude, AERISTerrainTileLod lod,
            AERISTerrainTilePriority priority, double horizonSeconds,
            double halfWidthMeters, bool centerline)
        {
            if (!Finite(latitude) || !Finite(longitude) ||
                points.Count >= MaximumPointCount) return;
            points.Add(new AERISPredictiveCorridorPoint
            {
                LatitudeDeg = Math.Max(-90.0, Math.Min(90.0, latitude)),
                LongitudeDeg = NormalizeLongitude(longitude),
                Lod = lod,
                Priority = priority,
                HorizonSeconds = Math.Max(0.0, horizonSeconds),
                CorridorHalfWidthMeters = Math.Max(0.0, halfWidthMeters),
                Centerline = centerline
            });
            if (centerline) snapshot.CenterlinePoints++;
        }

        static AERISTerrainTileLod ResolveLod(int index,
            AERISTerrainTileLod nearLod)
        {
            AERISTerrainTileLod normalizedNear = nearLod > AERISTerrainTileLod.Local ?
                AERISTerrainTileLod.Local : nearLod;
            if (index <= 1) return normalizedNear;
            if (index <= 3) return normalizedNear > AERISTerrainTileLod.Route ?
                AERISTerrainTileLod.Route : normalizedNear;
            return AERISTerrainTileLod.Far;
        }

        static double ResolveHorizontalGroundSpeed(Vessel vessel,
            CelestialBody body)
        {
            try
            {
                Vector3d up = (vessel.CoM - body.position).normalized;
                Vector3d horizontal = Vector3d.Exclude(up, vessel.srf_velocity);
                double value = horizontal.magnitude;
                if (Finite(value)) return Math.Max(0.0, value);
            }
            catch { }
            return Finite(vessel.srfSpeed) ? Math.Max(0.0, vessel.srfSpeed) : 0.0;
        }

        static double ResolveTurnRate(Vessel vessel, CelestialBody body)
        {
            try
            {
                Vector3d up = (vessel.CoM - body.position).normalized;
                double value = Vector3d.Dot(vessel.angularVelocity, up) *
                    180.0 / Math.PI;
                if (!Finite(value)) return 0.0;
                return Math.Max(-MaximumTurnRateDegPerSecond,
                    Math.Min(MaximumTurnRateDegPerSecond, value));
            }
            catch { return 0.0; }
        }

        static void PredictLocalOffset(double speed, double headingDeg,
            double turnRateDegPerSecond, double seconds, out double east,
            out double north, out double courseDeg)
        {
            double heading = headingDeg * Math.PI / 180.0;
            double omega = turnRateDegPerSecond * Math.PI / 180.0;
            courseDeg = NormalizeHeading(headingDeg + turnRateDegPerSecond * seconds);
            if (Math.Abs(omega) < 0.0005)
            {
                east = speed * seconds * Math.Sin(heading);
                north = speed * seconds * Math.Cos(heading);
                return;
            }
            double end = heading + omega * seconds;
            east = speed / omega * (Math.Cos(heading) - Math.Cos(end));
            north = speed / omega * (Math.Sin(end) - Math.Sin(heading));
        }

        static double ResolveHalfWidth(double speed, double turnRate,
            double seconds, double distance, AERISTerrainTileLod lod)
        {
            double tileWidth = AERISTerrainTileFormat.NominalCellMeters(lod) *
                Math.Max(1, AERISTerrainTileFormat.Resolution(lod) - 1);
            double uncertainty = speed * Math.Max(4.0, seconds * 0.08) +
                distance * Math.Min(0.18, Math.Abs(turnRate) * 0.008);
            return Math.Max(tileWidth * 0.55,
                Math.Min(25000.0, Math.Max(1000.0, uncertainty)));
        }

        static void OffsetLatLon(CelestialBody body, double latitude,
            double longitude, double eastMeters, double northMeters,
            out double resultLatitude, out double resultLongitude)
        {
            double radius = Math.Max(1000.0, body == null ? 600000.0 : body.Radius);
            double lat = latitude * Math.PI / 180.0;
            double angular = Math.Sqrt(eastMeters * eastMeters +
                northMeters * northMeters) / radius;
            if (angular <= 1e-12)
            {
                resultLatitude = latitude;
                resultLongitude = NormalizeLongitude(longitude);
                return;
            }
            double bearing = Math.Atan2(eastMeters, northMeters);
            double sinLat = Math.Sin(lat);
            double cosLat = Math.Cos(lat);
            double sinAngular = Math.Sin(angular);
            double cosAngular = Math.Cos(angular);
            double lat2 = Math.Asin(Math.Max(-1.0, Math.Min(1.0,
                sinLat * cosAngular + cosLat * sinAngular * Math.Cos(bearing))));
            double lon2 = longitude * Math.PI / 180.0 + Math.Atan2(
                Math.Sin(bearing) * sinAngular * cosLat,
                cosAngular - sinLat * Math.Sin(lat2));
            resultLatitude = lat2 * 180.0 / Math.PI;
            resultLongitude = NormalizeLongitude(lon2 * 180.0 / Math.PI);
        }

        static double NormalizeHeading(double value)
        {
            double wrapped = value % 360.0;
            return wrapped < 0.0 ? wrapped + 360.0 : wrapped;
        }

        static double NormalizeLongitude(double value)
        {
            double wrapped = (value + 180.0) % 360.0;
            if (wrapped < 0.0) wrapped += 360.0;
            return wrapped - 180.0;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
