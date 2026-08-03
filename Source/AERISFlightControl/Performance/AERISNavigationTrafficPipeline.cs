using System;
using System.Threading;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Performance
{
    // Main-thread capture contract for nearby airborne vessels. Only primitive values and
    // strings cross the worker boundary; no Vessel, CelestialBody or Unity object is retained.
    internal sealed class AERISNavigationTrafficSource
    {
        internal string StableId = string.Empty;
        internal string Name = string.Empty;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double AltitudeAslMeters;
        internal double GroundTrackDeg;
        internal double GroundSpeedMps;
    }

    internal sealed class AERISNavigationTrafficSnapshot
    {
        internal AERISRuntimeGenerationStamp Generation;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal double OriginLatitudeDeg;
        internal double OriginLongitudeDeg;
        internal double OwnAltitudeAslMeters;
        internal double OwnGroundTrackDeg;
        internal double OwnGroundSpeedMps;
        internal AERISNavigationTrafficSource[] Traffic =
            new AERISNavigationTrafficSource[0];
    }

    internal sealed class AERISPreparedTrafficSymbol
    {
        internal string StableId = string.Empty;
        internal string Name = string.Empty;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double EastMeters;
        internal double NorthMeters;
        internal double RelativeAltitudeMeters;
        internal double GroundTrackDeg;
        internal double GroundSpeedMps;
        internal double RelativeSpeedMps;
        internal double ClosestApproachSeconds;
        internal double ClosestApproachMeters;
        internal int ThreatLevel;
    }

    internal sealed class AERISPreparedTrafficFrame
    {
        internal AERISRuntimeGenerationStamp Generation;
        internal string BodyName = string.Empty;
        internal double OriginLatitudeDeg;
        internal double OriginLongitudeDeg;
        internal AERISPreparedTrafficSymbol[] Traffic =
            new AERISPreparedTrafficSymbol[0];
    }

    internal static class AERISPreparedTrafficFrameApi
    {
        static AERISPreparedTrafficFrame latest;

        internal static void Publish(AERISPreparedTrafficFrame value)
        {
            Volatile.Write(ref latest, value);
        }

        internal static void Clear()
        {
            Volatile.Write(ref latest, null);
        }

        internal static bool TryGetLatest(out AERISPreparedTrafficFrame value)
        {
            value = Volatile.Read(ref latest);
            return value != null;
        }
    }

    internal sealed class AERISTrafficAlertState
    {
        internal AERISRuntimeGenerationStamp Generation;
        internal int ThreatLevel;
        internal string StableId = string.Empty;
        internal string Name = string.Empty;
        internal double RelativeAltitudeMeters;
        internal double RelativeSpeedMps;
        internal double ClosestApproachSeconds;
        internal double ClosestApproachMeters;
    }

    internal static class AERISTrafficAlertApi
    {
        static readonly object Sync = new object();
        static AERISTrafficAlertState latest;
        static int lastLoggedThreatLevel;
        static string lastLoggedStableId = string.Empty;

        internal static void Publish(AERISPreparedTrafficFrame frame)
        {
            AERISTrafficAlertState state = Resolve(frame);
            Volatile.Write(ref latest, state);
            lock (Sync)
            {
                string stableId = state == null ? string.Empty : state.StableId;
                int level = state == null ? 0 : state.ThreatLevel;
                if (level == lastLoggedThreatLevel && string.Equals(stableId,
                    lastLoggedStableId, StringComparison.Ordinal)) return;
                if (level > 0 && state != null)
                {
                    string severity = level >= 2 ? "CRITICAL" : "CAUTION";
                    string message = "[TRAFFIC_ALERT] severity=" + severity +
                        "; target=" + Safe(state.Name) + "; stableId=" + Safe(state.StableId) +
                        "; cpa_m=" + state.ClosestApproachMeters.ToString("0") +
                        "; cpa_s=" + state.ClosestApproachSeconds.ToString("0") +
                        "; relative_alt_m=" + state.RelativeAltitudeMeters.ToString("+0;-0;0") +
                        "; relative_speed_mps=" + state.RelativeSpeedMps.ToString("0.0") +
                        "; intervention=NONE";
                    if (level >= 2) AERISLogger.Error(message);
                    else AERISLogger.Warn(message);
                }
                else if (lastLoggedThreatLevel > 0)
                    AERISLogger.Info("[TRAFFIC_ALERT] severity=CLEAR; intervention=NONE");
                lastLoggedThreatLevel = level;
                lastLoggedStableId = stableId;
            }
        }

        internal static bool TryGetLatest(out AERISTrafficAlertState state)
        {
            state = Volatile.Read(ref latest);
            return state != null && state.ThreatLevel > 0;
        }

        internal static void Clear()
        {
            Volatile.Write(ref latest, null);
            lock (Sync)
            {
                lastLoggedThreatLevel = 0;
                lastLoggedStableId = string.Empty;
            }
        }

        static AERISTrafficAlertState Resolve(AERISPreparedTrafficFrame frame)
        {
            if (frame == null || frame.Traffic == null || frame.Traffic.Length == 0)
                return null;
            AERISPreparedTrafficSymbol item = frame.Traffic[0];
            if (item == null || item.ThreatLevel <= 0) return null;
            return new AERISTrafficAlertState
            {
                Generation = frame.Generation,
                ThreatLevel = item.ThreatLevel,
                StableId = item.StableId ?? string.Empty,
                Name = item.Name ?? string.Empty,
                RelativeAltitudeMeters = item.RelativeAltitudeMeters,
                RelativeSpeedMps = item.RelativeSpeedMps,
                ClosestApproachSeconds = item.ClosestApproachSeconds,
                ClosestApproachMeters = item.ClosestApproachMeters
            };
        }

        static string Safe(string value)
        {
            return (value ?? string.Empty).Replace(";", "_").Replace("\r", " ").Replace("\n", " ");
        }
    }

    // Lightweight straight-line closest-approach preprocessing on the shared bounded
    // GeneralCompute lane. It is display-only and has no AP, LAND or NAV control authority.
    internal sealed class AERISNavigationTrafficPipeline : IDisposable
    {
        internal const string JobKey = "navigation-traffic-preprocess";
        readonly AERISWorkerScheduler scheduler;
        bool disposed;

        internal AERISNavigationTrafficPipeline(AERISWorkerScheduler value)
        {
            scheduler = value;
        }

        internal bool Submit(AERISNavigationTrafficSnapshot snapshot)
        {
            if (disposed || scheduler == null || snapshot == null) return false;
            AERISNavigationTrafficSnapshot immutable = snapshot;
            return scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute, JobKey,
                snapshot.Generation, context => Prepare(immutable, context), value =>
                {
                    AERISPreparedTrafficFrame frame = value as AERISPreparedTrafficFrame;
                    if (frame != null)
                    {
                        AERISPreparedTrafficFrameApi.Publish(frame);
                        AERISTrafficAlertApi.Publish(frame);
                    }
                });
        }

        static object Prepare(AERISNavigationTrafficSnapshot source,
            AERISRuntimeJobContext context)
        {
            context.ThrowIfStale();
            AERISNavigationTrafficSource[] input = source.Traffic ??
                new AERISNavigationTrafficSource[0];
            var output = new AERISPreparedTrafficSymbol[input.Length];
            double ownTrackRad = NormalizeHeading(source.OwnGroundTrackDeg) *
                Math.PI / 180.0;
            double ownEastVelocity = Math.Sin(ownTrackRad) *
                Math.Max(0.0, source.OwnGroundSpeedMps);
            double ownNorthVelocity = Math.Cos(ownTrackRad) *
                Math.Max(0.0, source.OwnGroundSpeedMps);
            for (int i = 0; i < input.Length; i++)
            {
                if ((i & 7) == 0) context.ThrowIfStale();
                AERISNavigationTrafficSource item = input[i];
                if (item == null)
                {
                    output[i] = new AERISPreparedTrafficSymbol();
                    continue;
                }
                double east, north;
                ToLocalMeters(source.BodyRadiusMeters, source.OriginLatitudeDeg,
                    source.OriginLongitudeDeg, item.LatitudeDeg, item.LongitudeDeg,
                    out east, out north);
                double trackRad = NormalizeHeading(item.GroundTrackDeg) *
                    Math.PI / 180.0;
                double targetEastVelocity = Math.Sin(trackRad) *
                    Math.Max(0.0, item.GroundSpeedMps);
                double targetNorthVelocity = Math.Cos(trackRad) *
                    Math.Max(0.0, item.GroundSpeedMps);
                double relativeEastVelocity = targetEastVelocity - ownEastVelocity;
                double relativeNorthVelocity = targetNorthVelocity - ownNorthVelocity;
                double relativeSpeed = Math.Sqrt(relativeEastVelocity * relativeEastVelocity +
                    relativeNorthVelocity * relativeNorthVelocity);
                double velocitySquared = relativeEastVelocity * relativeEastVelocity +
                    relativeNorthVelocity * relativeNorthVelocity;
                double closestSeconds = velocitySquared <= 0.01 ? 0.0 :
                    Clamp(-(east * relativeEastVelocity + north * relativeNorthVelocity) /
                    velocitySquared, 0.0, 120.0);
                double closestEast = east + relativeEastVelocity * closestSeconds;
                double closestNorth = north + relativeNorthVelocity * closestSeconds;
                double closestMeters = Math.Sqrt(closestEast * closestEast +
                    closestNorth * closestNorth);
                double relativeAltitude = item.AltitudeAslMeters -
                    source.OwnAltitudeAslMeters;
                int threat = ResolveThreatLevel(closestMeters, Math.Abs(relativeAltitude),
                    closestSeconds);
                output[i] = new AERISPreparedTrafficSymbol
                {
                    StableId = item.StableId ?? string.Empty,
                    Name = item.Name ?? string.Empty,
                    LatitudeDeg = item.LatitudeDeg,
                    LongitudeDeg = item.LongitudeDeg,
                    EastMeters = east,
                    NorthMeters = north,
                    RelativeAltitudeMeters = relativeAltitude,
                    GroundTrackDeg = NormalizeHeading(item.GroundTrackDeg),
                    GroundSpeedMps = Math.Max(0.0, item.GroundSpeedMps),
                    RelativeSpeedMps = relativeSpeed,
                    ClosestApproachSeconds = closestSeconds,
                    ClosestApproachMeters = closestMeters,
                    ThreatLevel = threat
                };
            }
            Array.Sort(output, CompareTraffic);
            context.ThrowIfStale();
            return new AERISPreparedTrafficFrame
            {
                Generation = source.Generation,
                BodyName = source.BodyName ?? string.Empty,
                OriginLatitudeDeg = source.OriginLatitudeDeg,
                OriginLongitudeDeg = source.OriginLongitudeDeg,
                Traffic = output
            };
        }

        static int ResolveThreatLevel(double closestMeters,
            double absoluteRelativeAltitudeMeters, double closestSeconds)
        {
            if (closestSeconds <= 60.0 &&
                closestMeters <= 500.0 && absoluteRelativeAltitudeMeters <= 300.0)
                return 2;
            if (closestSeconds <= 120.0 &&
                closestMeters <= 1500.0 && absoluteRelativeAltitudeMeters <= 750.0)
                return 1;
            return 0;
        }

        static int CompareTraffic(AERISPreparedTrafficSymbol left,
            AERISPreparedTrafficSymbol right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;
            int threat = right.ThreatLevel.CompareTo(left.ThreatLevel);
            if (threat != 0) return threat;
            double leftDistance = left.EastMeters * left.EastMeters +
                left.NorthMeters * left.NorthMeters;
            double rightDistance = right.EastMeters * right.EastMeters +
                right.NorthMeters * right.NorthMeters;
            int distance = leftDistance.CompareTo(rightDistance);
            return distance != 0 ? distance : string.CompareOrdinal(left.StableId,
                right.StableId);
        }

        static void ToLocalMeters(double bodyRadiusMeters, double originLatitudeDeg,
            double originLongitudeDeg, double latitudeDeg, double longitudeDeg,
            out double eastMeters, out double northMeters)
        {
            double radius = Math.Max(1.0, bodyRadiusMeters);
            double lat1 = originLatitudeDeg * Math.PI / 180.0;
            double lat2 = latitudeDeg * Math.PI / 180.0;
            double deltaLongitude = NormalizeLongitude(longitudeDeg -
                originLongitudeDeg) * Math.PI / 180.0;
            double y = Math.Sin(deltaLongitude) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) *
                Math.Cos(lat2) * Math.Cos(deltaLongitude);
            double bearing = Math.Atan2(y, x);
            double deltaLatitude = lat2 - lat1;
            double haversine = Math.Sin(deltaLatitude * 0.5) *
                Math.Sin(deltaLatitude * 0.5) + Math.Cos(lat1) * Math.Cos(lat2) *
                Math.Sin(deltaLongitude * 0.5) * Math.Sin(deltaLongitude * 0.5);
            haversine = Clamp(haversine, 0.0, 1.0);
            double angle = 2.0 * Math.Atan2(Math.Sqrt(haversine),
                Math.Sqrt(Math.Max(0.0, 1.0 - haversine)));
            double distance = radius * angle;
            eastMeters = Math.Sin(bearing) * distance;
            northMeters = Math.Cos(bearing) * distance;
        }

        static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        static double NormalizeHeading(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        public void Dispose()
        {
            disposed = true;
            AERISPreparedTrafficFrameApi.Clear();
            AERISTrafficAlertApi.Clear();
        }
    }
}
