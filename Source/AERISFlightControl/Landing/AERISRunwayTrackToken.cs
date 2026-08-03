using System;
using AERISFlightControl.Landing;

namespace AERISFlightControl.API
{
    // Public, immutable and deliberately control-free handoff contract for the future
    // Touchdown -> Ground Assist phase.  It contains only certified geometry values.
    public struct AERISRunwayTrackPoint
    {
        public double LatitudeDeg { get; private set; }
        public double LongitudeDeg { get; private set; }
        public double ElevationMeters { get; private set; }

        internal AERISRunwayTrackPoint(AERISGeoPoint point)
        {
            LatitudeDeg = point == null ? 0.0 : point.LatitudeDeg;
            LongitudeDeg = point == null ? 0.0 : point.LongitudeDeg;
            ElevationMeters = point == null ? 0.0 : point.ElevationMeters;
        }
    }

    public sealed class AERISRunwayTrackToken
    {
        readonly AERISRunwayTrackPoint[] usablePolygon;
        readonly double[] widthProfileMeters;

        public string AirfieldStableId { get; private set; }
        public string PhysicalRunwayStableId { get; private set; }
        public string ApproachDirectionStableId { get; private set; }
        public AERISRunwayTrackPoint CenterlineStart { get; private set; }
        public AERISRunwayTrackPoint CenterlineEnd { get; private set; }
        public AERISRunwayTrackPoint OperationalThreshold { get; private set; }
        public AERISRunwayTrackPoint RolloutEnd { get; private set; }
        public double HeadingDeg { get; private set; }
        public long DatabaseGeneration { get; private set; }
        public long GeometryGeneration { get; private set; }
        public string GeometryFingerprint { get; private set; }

        public AERISRunwayTrackPoint[] UsablePolygon
        {
            get { return (AERISRunwayTrackPoint[])usablePolygon.Clone(); }
        }

        public double[] WidthProfileMeters
        {
            get { return (double[])widthProfileMeters.Clone(); }
        }

        AERISRunwayTrackToken(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, AERISRunwayDirectionDefinition direction,
            long databaseGeneration)
        {
            AirfieldStableId = airfield.StableId;
            PhysicalRunwayStableId = runway.StableId;
            ApproachDirectionStableId = direction.StableId;
            CenterlineStart = new AERISRunwayTrackPoint(direction.UsableStart ??
                direction.Threshold);
            CenterlineEnd = new AERISRunwayTrackPoint(direction.UsableEnd ??
                direction.OppositeThreshold);
            OperationalThreshold = new AERISRunwayTrackPoint(direction.Threshold);
            RolloutEnd = new AERISRunwayTrackPoint(direction.RolloutEnd ??
                direction.OppositeThreshold);
            HeadingDeg = direction.HeadingDeg;
            DatabaseGeneration = databaseGeneration;
            GeometryGeneration = direction.GeometryRevision;
            GeometryFingerprint = direction.GeometryFingerprint ?? string.Empty;
            usablePolygon = new AERISRunwayTrackPoint[runway.UsablePolygon.Count];
            for (int i = 0; i < usablePolygon.Length; i++)
                usablePolygon[i] = new AERISRunwayTrackPoint(runway.UsablePolygon[i]);
            widthProfileMeters = runway.WidthProfileMeters.ToArray();
        }

        internal static AERISRunwayTrackToken Create(AERISAirfieldDefinition airfield,
            AERISRunwayDefinition runway, AERISRunwayDirectionDefinition direction,
            long databaseGeneration)
        {
            if (airfield == null || runway == null || direction == null ||
                !direction.HasCertifiedGeometry || !direction.HeadingMatchesGeometry)
                return null;
            return new AERISRunwayTrackToken(airfield, runway, direction,
                databaseGeneration);
        }
    }

    public static class AERISRunwayTrackTokenApi
    {
        static readonly object Sync = new object();
        static Func<AERISRunwayTrackToken> provider;

        internal static void Bind(Func<AERISRunwayTrackToken> value)
        {
            lock (Sync) provider = value;
        }

        internal static void Unbind()
        {
            lock (Sync) provider = null;
        }

        public static bool TryGet(out AERISRunwayTrackToken token)
        {
            token = null;
            Func<AERISRunwayTrackToken> current;
            lock (Sync) current = provider;
            if (current == null) return false;
            try
            {
                token = current();
                return token != null;
            }
            catch
            {
                token = null;
                return false;
            }
        }
    }
}
