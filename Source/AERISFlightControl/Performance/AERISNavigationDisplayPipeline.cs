using System;
using System.Threading;

namespace AERISFlightControl.Performance
{
    // Main-thread capture contract for the ND navigation layer. Only immutable primitive
    // values and strings cross the worker boundary; no Unity/KSP object is retained.
    internal sealed class AERISNavigationRunwaySource
    {
        internal int AirfieldIndex;
        internal string AirfieldStableId = string.Empty;
        internal string AirfieldName = string.Empty;
        internal string RunwayStableId = string.Empty;
        internal string RunwayName = string.Empty;
        internal string DirectionAName = string.Empty;
        internal string DirectionBName = string.Empty;
        internal int DirectionASelectableIndex = -1;
        internal int DirectionBSelectableIndex = -1;
        internal bool SelectedAirfield;
        internal bool SelectedRunway;
        internal bool Certified;
        internal bool Provisional;
        internal string CertificationBasis = string.Empty;
        internal double LatitudeADeg;
        internal double LongitudeADeg;
        internal double ElevationAMeters;
        internal double LatitudeBDeg;
        internal double LongitudeBDeg;
        internal double ElevationBMeters;
        internal double LengthMeters;
        internal double WidthMeters;
    }

    internal sealed class AERISNavigationFacilitySource
    {
        internal int AirfieldIndex;
        internal string StableId = string.Empty;
        internal string Name = string.Empty;
        internal int FacilityKind;
        internal bool Selected;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double ElevationMeters;
    }

    internal sealed class AERISNavigationDisplaySnapshot
    {
        internal AERISRuntimeGenerationStamp Generation;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal double OriginLatitudeDeg;
        internal double OriginLongitudeDeg;
        internal long DatabaseRevision;
        internal long SelectionRevision;
        internal AERISNavigationRunwaySource[] Runways = new AERISNavigationRunwaySource[0];
        internal AERISNavigationFacilitySource[] Facilities = new AERISNavigationFacilitySource[0];
    }

    internal sealed class AERISPreparedRunwaySymbol
    {
        internal int AirfieldIndex;
        internal string AirfieldStableId = string.Empty;
        internal string AirfieldName = string.Empty;
        internal string RunwayStableId = string.Empty;
        internal string RunwayName = string.Empty;
        internal string DirectionAName = string.Empty;
        internal string DirectionBName = string.Empty;
        internal int DirectionASelectableIndex = -1;
        internal int DirectionBSelectableIndex = -1;
        internal bool SelectedAirfield;
        internal bool SelectedRunway;
        internal bool Certified;
        internal bool Provisional;
        internal string CertificationBasis = string.Empty;
        internal double LatitudeADeg;
        internal double LongitudeADeg;
        internal double LatitudeBDeg;
        internal double LongitudeBDeg;
        internal double EastAMeters;
        internal double NorthAMeters;
        internal double EastBMeters;
        internal double NorthBMeters;
        internal double CenterEastMeters;
        internal double CenterNorthMeters;
        internal double CenterLatitudeDeg;
        internal double CenterLongitudeDeg;
        internal double DistanceFromOriginMeters;
        internal double BearingFromOriginDeg;
        internal double LengthMeters;
        internal double WidthMeters;
        internal double ElevationMeters;
    }

    internal sealed class AERISPreparedFacilitySymbol
    {
        internal int AirfieldIndex;
        internal string StableId = string.Empty;
        internal string Name = string.Empty;
        internal int FacilityKind;
        internal bool Selected;
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal bool HasGeographicPosition;
        internal double EastMeters;
        internal double NorthMeters;
        internal double DistanceFromOriginMeters;
    }

    internal sealed class AERISPreparedNavigationFrame
    {
        internal AERISRuntimeGenerationStamp Generation;
        internal string BodyName = string.Empty;
        internal double BodyRadiusMeters;
        internal double OriginLatitudeDeg;
        internal double OriginLongitudeDeg;
        internal long DatabaseRevision;
        internal long SelectionRevision;
        internal AERISPreparedRunwaySymbol[] Runways = new AERISPreparedRunwaySymbol[0];
        internal AERISPreparedFacilitySymbol[] Facilities = new AERISPreparedFacilitySymbol[0];
    }

    internal static class AERISPreparedNavigationFrameApi
    {
        static AERISPreparedNavigationFrame latest;

        internal static void Publish(AERISPreparedNavigationFrame value)
        {
            Volatile.Write(ref latest, value);
        }

        internal static void Clear()
        {
            Volatile.Write(ref latest, null);
        }

        internal static bool TryGetLatest(out AERISPreparedNavigationFrame value)
        {
            value = Volatile.Read(ref latest);
            return value != null;
        }
    }

    // Pure CPU preprocessing on the shared Performance Runtime scheduler. This is not an
    // ND-owned ThreadPool and it has no flight-control or Unity API access.
    internal sealed class AERISNavigationDisplayPipeline : IDisposable
    {
        internal const string JobKey = "navigation-display-preprocess";
        readonly AERISWorkerScheduler scheduler;
        bool disposed;

        internal AERISNavigationDisplayPipeline(AERISWorkerScheduler value)
        {
            scheduler = value;
        }

        internal bool Submit(AERISNavigationDisplaySnapshot snapshot)
        {
            if (disposed || scheduler == null || snapshot == null) return false;
            AERISNavigationDisplaySnapshot immutable = snapshot;
            return scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute, JobKey,
                snapshot.Generation, context => Prepare(immutable, context), value =>
                {
                    AERISPreparedNavigationFrame frame = value as AERISPreparedNavigationFrame;
                    if (frame != null) AERISPreparedNavigationFrameApi.Publish(frame);
                });
        }

        static object Prepare(AERISNavigationDisplaySnapshot source,
            AERISRuntimeJobContext context)
        {
            context.ThrowIfStale();
            AERISNavigationRunwaySource[] sourceRunways = source.Runways ??
                new AERISNavigationRunwaySource[0];
            var runways = new AERISPreparedRunwaySymbol[sourceRunways.Length];
            for (int i = 0; i < sourceRunways.Length; i++)
            {
                if ((i & 7) == 0) context.ThrowIfStale();
                AERISNavigationRunwaySource item = sourceRunways[i];
                if (item == null)
                {
                    runways[i] = new AERISPreparedRunwaySymbol();
                    continue;
                }
                double eastA, northA, eastB, northB;
                ToLocalMeters(source.BodyRadiusMeters, source.OriginLatitudeDeg,
                    source.OriginLongitudeDeg, item.LatitudeADeg, item.LongitudeADeg,
                    out eastA, out northA);
                ToLocalMeters(source.BodyRadiusMeters, source.OriginLatitudeDeg,
                    source.OriginLongitudeDeg, item.LatitudeBDeg, item.LongitudeBDeg,
                    out eastB, out northB);
                double centerEast = (eastA + eastB) * 0.5;
                double centerNorth = (northA + northB) * 0.5;
                double centerLat, centerLon;
                MidpointOnSphere(item.LatitudeADeg, item.LongitudeADeg,
                    item.LatitudeBDeg, item.LongitudeBDeg, out centerLat, out centerLon);
                double distance = Math.Sqrt(centerEast * centerEast + centerNorth * centerNorth);
                double bearing = NormalizeHeading(Math.Atan2(centerEast, centerNorth) * 180.0 / Math.PI);
                runways[i] = new AERISPreparedRunwaySymbol
                {
                    AirfieldIndex = item.AirfieldIndex,
                    AirfieldStableId = item.AirfieldStableId ?? string.Empty,
                    AirfieldName = item.AirfieldName ?? string.Empty,
                    RunwayStableId = item.RunwayStableId ?? string.Empty,
                    RunwayName = item.RunwayName ?? string.Empty,
                    DirectionAName = item.DirectionAName ?? string.Empty,
                    DirectionBName = item.DirectionBName ?? string.Empty,
                    DirectionASelectableIndex = item.DirectionASelectableIndex,
                    DirectionBSelectableIndex = item.DirectionBSelectableIndex,
                    SelectedAirfield = item.SelectedAirfield,
                    SelectedRunway = item.SelectedRunway,
                    Certified = item.Certified,
                    Provisional = item.Provisional,
                    CertificationBasis = item.CertificationBasis ?? string.Empty,
                    LatitudeADeg = item.LatitudeADeg,
                    LongitudeADeg = item.LongitudeADeg,
                    LatitudeBDeg = item.LatitudeBDeg,
                    LongitudeBDeg = item.LongitudeBDeg,
                    EastAMeters = eastA,
                    NorthAMeters = northA,
                    EastBMeters = eastB,
                    NorthBMeters = northB,
                    CenterEastMeters = centerEast,
                    CenterNorthMeters = centerNorth,
                    CenterLatitudeDeg = centerLat,
                    CenterLongitudeDeg = centerLon,
                    DistanceFromOriginMeters = distance,
                    BearingFromOriginDeg = bearing,
                    LengthMeters = item.LengthMeters,
                    WidthMeters = item.WidthMeters,
                    ElevationMeters = (item.ElevationAMeters + item.ElevationBMeters) * 0.5
                };
            }

            AERISNavigationFacilitySource[] sourceFacilities = source.Facilities ??
                new AERISNavigationFacilitySource[0];
            var facilities = new AERISPreparedFacilitySymbol[sourceFacilities.Length];
            for (int i = 0; i < sourceFacilities.Length; i++)
            {
                if ((i & 15) == 0) context.ThrowIfStale();
                AERISNavigationFacilitySource item = sourceFacilities[i];
                if (item == null)
                {
                    facilities[i] = new AERISPreparedFacilitySymbol();
                    continue;
                }
                double east, north;
                ToLocalMeters(source.BodyRadiusMeters, source.OriginLatitudeDeg,
                    source.OriginLongitudeDeg, item.LatitudeDeg, item.LongitudeDeg,
                    out east, out north);
                facilities[i] = new AERISPreparedFacilitySymbol
                {
                    AirfieldIndex = item.AirfieldIndex,
                    StableId = item.StableId ?? string.Empty,
                    Name = item.Name ?? string.Empty,
                    FacilityKind = item.FacilityKind,
                    Selected = item.Selected,
                    LatitudeDeg = item.LatitudeDeg,
                    LongitudeDeg = item.LongitudeDeg,
                    HasGeographicPosition = !double.IsNaN(item.LatitudeDeg) &&
                        !double.IsInfinity(item.LatitudeDeg) &&
                        !double.IsNaN(item.LongitudeDeg) &&
                        !double.IsInfinity(item.LongitudeDeg),
                    EastMeters = east,
                    NorthMeters = north,
                    DistanceFromOriginMeters = Math.Sqrt(east * east + north * north)
                };
            }

            Array.Sort(runways, CompareRunways);
            Array.Sort(facilities, CompareFacilities);
            context.ThrowIfStale();
            return new AERISPreparedNavigationFrame
            {
                Generation = source.Generation,
                BodyName = source.BodyName ?? string.Empty,
                BodyRadiusMeters = source.BodyRadiusMeters,
                OriginLatitudeDeg = source.OriginLatitudeDeg,
                OriginLongitudeDeg = source.OriginLongitudeDeg,
                DatabaseRevision = source.DatabaseRevision,
                SelectionRevision = source.SelectionRevision,
                Runways = runways,
                Facilities = facilities
            };
        }

        static int CompareRunways(AERISPreparedRunwaySymbol a,
            AERISPreparedRunwaySymbol b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int selected = b.SelectedRunway.CompareTo(a.SelectedRunway);
            if (selected != 0) return selected;
            selected = b.SelectedAirfield.CompareTo(a.SelectedAirfield);
            if (selected != 0) return selected;
            int certified = b.Certified.CompareTo(a.Certified);
            if (certified != 0) return certified;
            int distance = a.DistanceFromOriginMeters.CompareTo(b.DistanceFromOriginMeters);
            if (distance != 0) return distance;
            return string.Compare(a.RunwayStableId, b.RunwayStableId,
                StringComparison.Ordinal);
        }

        static int CompareFacilities(AERISPreparedFacilitySymbol a,
            AERISPreparedFacilitySymbol b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int selected = b.Selected.CompareTo(a.Selected);
            if (selected != 0) return selected;
            int distance = a.DistanceFromOriginMeters.CompareTo(b.DistanceFromOriginMeters);
            if (distance != 0) return distance;
            return string.Compare(a.StableId, b.StableId, StringComparison.Ordinal);
        }

        static void ToLocalMeters(double radiusMeters, double originLatDeg,
            double originLonDeg, double targetLatDeg, double targetLonDeg,
            out double eastMeters, out double northMeters)
        {
            eastMeters = northMeters = 0.0;
            double radius = Math.Max(1.0, radiusMeters);
            if (!Finite(originLatDeg) || !Finite(originLonDeg) ||
                !Finite(targetLatDeg) || !Finite(targetLonDeg)) return;
            double lat1 = originLatDeg * Math.PI / 180.0;
            double lat2 = targetLatDeg * Math.PI / 180.0;
            double dLon = NormalizeLongitude(targetLonDeg - originLonDeg) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            double bearing = Math.Atan2(y, x);
            double dLat = lat2 - lat1;
            double sinLat = Math.Sin(dLat * 0.5);
            double sinLon = Math.Sin(dLon * 0.5);
            double haversine = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) *
                sinLon * sinLon;
            haversine = Math.Max(0.0, Math.Min(1.0, haversine));
            double angle = 2.0 * Math.Atan2(Math.Sqrt(haversine),
                Math.Sqrt(Math.Max(0.0, 1.0 - haversine)));
            double distance = radius * angle;
            eastMeters = Math.Sin(bearing) * distance;
            northMeters = Math.Cos(bearing) * distance;
        }

        static void MidpointOnSphere(double latADeg, double lonADeg,
            double latBDeg, double lonBDeg, out double latitudeDeg,
            out double longitudeDeg)
        {
            double latA = latADeg * Math.PI / 180.0;
            double lonA = lonADeg * Math.PI / 180.0;
            double latB = latBDeg * Math.PI / 180.0;
            double lonB = lonBDeg * Math.PI / 180.0;
            double ax = Math.Cos(latA) * Math.Cos(lonA);
            double ay = Math.Cos(latA) * Math.Sin(lonA);
            double az = Math.Sin(latA);
            double bx = Math.Cos(latB) * Math.Cos(lonB);
            double by = Math.Cos(latB) * Math.Sin(lonB);
            double bz = Math.Sin(latB);
            double x = ax + bx;
            double y = ay + by;
            double z = az + bz;
            double horizontal = Math.Sqrt(x * x + y * y);
            if (horizontal < 1e-12 && Math.Abs(z) < 1e-12)
            {
                latitudeDeg = (latADeg + latBDeg) * 0.5;
                longitudeDeg = NormalizeLongitude((lonADeg + lonBDeg) * 0.5);
                return;
            }
            latitudeDeg = Math.Atan2(z, horizontal) * 180.0 / Math.PI;
            longitudeDeg = NormalizeLongitude(Math.Atan2(y, x) * 180.0 / Math.PI);
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

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (scheduler != null)
                scheduler.CancelKey(AERISRuntimeLane.GeneralCompute, JobKey);
            AERISPreparedNavigationFrameApi.Clear();
        }
    }
}
