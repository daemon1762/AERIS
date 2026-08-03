using System;
using UnityEngine;
using AERISFlightControl.FlightState;
using AERISFlightControl.Logging;
using AERISFlightControl.API;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISLandingFoundation
    {
        readonly AERISAirfieldRegistry registry;
        string armedDirectionStableId = string.Empty;
        Vessel armedVessel;
        float nextObservationRealtime;
        AERISAirfieldDefinition frozenAirfield;
        AERISRunwayDefinition frozenRunway;
        AERISRunwayDirectionDefinition frozenDirection;
        long frozenDatabaseRevision;
        long frozenGeometryRevision;

        internal AERISLandingFoundation(AERISAirfieldRegistry registry)
        {
            this.registry = registry;
            Observation = new AERISRunwayObservation();
        }

        internal bool Armed { get; private set; }
        internal string StateText { get; private set; } = "OFF";
        internal string ControlText { get { return "PILOT"; } }
        internal string LocalizerText
        {
            get
            {
                AERISRunwayDirectionDefinition direction = ActiveDirection;
                if (direction == null || !direction.HeadingMatchesGeometry) return "N/A";
                return Observation != null && Observation.LocalizerGeometryEligible ?
                    "ELIGIBLE" : "WAIT";
            }
        }
        internal string GlidePathText
        {
            get
            {
                AERISRunwayDirectionDefinition direction = ActiveDirection;
                if (direction == null || !direction.HeadingMatchesGeometry ||
                    Observation == null || !Observation.OnApproachSide) return "N/A";
                return Observation.GlidePathGeometryEligible ? "ELIGIBLE" : "WAIT";
            }
        }
        internal string Status { get; private set; } = "SELECT A RUNWAY";
        internal AERISRunwayObservation Observation { get; private set; }
        internal bool AutoDisplayDemand { get { return Armed; } }
        internal long FrozenDatabaseRevision { get { return frozenDatabaseRevision; } }
        internal long FrozenGeometryRevision { get { return frozenGeometryRevision; } }
        internal AERISRunwayDirectionDefinition ActiveDirection
        {
            get { return Armed ? frozenDirection : (registry == null ? null : registry.SelectedDirection); }
        }

        internal bool TryArm(Vessel vessel, out string error)
        {
            error = "LAND ARM unavailable";
            AERISAirfieldDefinition airfield = registry == null ? null : registry.SelectedAirfield;
            AERISRunwayDirectionDefinition direction = registry == null ? null : registry.SelectedDirection;
            if (!HighLogic.LoadedSceneIsFlight || vessel == null)
            {
                error = "ACTIVE FLIGHT VESSEL REQUIRED";
                return false;
            }
            if (vessel.packed)
            {
                error = "ACTIVE VESSEL IS ON RAILS";
                return false;
            }
            if (vessel.LandedOrSplashed || vessel.situation == Vessel.Situations.PRELAUNCH)
            {
                error = "AIRBORNE FIXED-WING FLIGHT REQUIRED";
                return false;
            }
            if (airfield == null)
            {
                error = "SELECT AN AIRFIELD";
                return false;
            }
            if (airfield.FacilityKind != AERISFacilityKind.Runway)
            {
                error = "SELECTED FACILITY IS NOT A RUNWAY";
                return false;
            }
            if (direction != null && !direction.HeadingMatchesGeometry)
            {
                error = "RUNWAY GEOMETRY HEADING MISMATCH — LAND ARM INHIBITED";
                return false;
            }
            if (!airfield.CanArmFoundation || direction == null ||
                !direction.HasCertifiedGeometry)
            {
                error = "RUNWAY APPROACH IS NOT CERTIFIED";
                return false;
            }
            if (vessel.mainBody == null || !string.Equals(vessel.mainBody.name, airfield.Body,
                StringComparison.OrdinalIgnoreCase))
            {
                error = "ACTIVE VESSEL IS NOT ON " + airfield.Body.ToUpperInvariant();
                return false;
            }
            string vesselType = vessel.vesselType.ToString().ToUpperInvariant();
            // First-gate LAND is intentionally fixed-wing only. Requiring the KSP Plane
            // vessel type prevents probes, rovers, ships and airborne debris from arming
            // an approach merely because they happen to be off the ground.
            if (vesselType != "PLANE")
            {
                error = "FIXED-WING PLANE VESSEL TYPE REQUIRED";
                return false;
            }

            Armed = true;
            armedVessel = vessel;
            armedDirectionStableId = direction.StableId;
            frozenAirfield = airfield.Clone();
            frozenDirection = FindDirection(frozenAirfield, direction.StableId);
            frozenRunway = frozenAirfield == null || frozenDirection == null ? null :
                frozenAirfield.RunwayForDirection(frozenDirection);
            if (frozenAirfield == null || frozenRunway == null || frozenDirection == null ||
                !frozenDirection.HasCertifiedGeometry)
            {
                Armed = false;
                armedVessel = null;
                armedDirectionStableId = string.Empty;
                frozenAirfield = null;
                frozenRunway = null;
                frozenDirection = null;
                error = "CERTIFIED RUNWAY SNAPSHOT COULD NOT BE FROZEN";
                return false;
            }
            frozenDatabaseRevision = registry == null ? 0L : registry.DatabaseRevision;
            frozenGeometryRevision = frozenDirection.GeometryRevision;
            StateText = "LAND_ARMED";
            Status = "ARMED / OBSERVATION ONLY / CONTROL REMAINS PILOT";
            nextObservationRealtime = 0f;
            AERISLogger.Info("[LAND_FOUNDATION] ARM accepted: " + airfield.DisplayName + " / " +
                direction.DisplayName + "; databaseRevision=" + frozenDatabaseRevision +
                "; geometryRevision=" + frozenGeometryRevision +
                "; no AP or FlightCtrlState authority granted.");
            return true;
        }

        internal void Disarm(string reason)
        {
            bool wasArmed = Armed;
            Armed = false;
            armedVessel = null;
            armedDirectionStableId = string.Empty;
            frozenAirfield = null;
            frozenRunway = null;
            frozenDirection = null;
            frozenDatabaseRevision = 0L;
            frozenGeometryRevision = 0L;
            StateText = registry != null && registry.SelectedDirection != null ? "RUNWAY_SELECTED" : "OFF";
            Status = string.IsNullOrEmpty(reason) ? "DISARMED" : reason.ToUpperInvariant();
            if (wasArmed) AERISLogger.Info("[LAND_FOUNDATION] DISARM: " + reason);
        }

        internal void ResetForSceneTransition(string reason)
        {
            Disarm(reason);
            Observation = new AERISRunwayObservation { Status = "RESET: " + (reason ?? string.Empty) };
        }

        internal void Tick(Vessel vessel, VirtualAttitudeInstrument attitude)
        {
            AERISRunwayDirectionDefinition direction = ActiveDirection;
            AERISAirfieldDefinition airfield = Armed ? frozenAirfield :
                (registry == null ? null : registry.SelectedAirfield);
            if (Armed)
            {
                if (vessel == null || vessel != armedVessel)
                {
                    Disarm("active vessel changed");
                    return;
                }
                if (direction == null || !direction.HasCertifiedGeometry ||
                    !direction.HeadingMatchesGeometry ||
                    (registry != null && registry.IsDirectionRevoked(armedDirectionStableId)) ||
                    !string.Equals(direction.StableId, armedDirectionStableId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    Disarm("frozen runway certification invalid");
                    return;
                }
            }
            if (Time.realtimeSinceStartup < nextObservationRealtime) return;
            nextObservationRealtime = Time.realtimeSinceStartup + (Armed ? 0.10f : 0.25f);
            Observation = BuildObservation(vessel, attitude, airfield, direction, Armed);
            if (!Armed)
            {
                StateText = direction == null ? "OFF" : "RUNWAY_SELECTED";
                Status = direction == null ? "SELECT A RUNWAY" : "READY TO ARM / CONTROL PILOT";
            }
        }

        internal bool TryCreateTrackToken(out AERISRunwayTrackToken token)
        {
            token = null;
            AERISAirfieldDefinition airfield = Armed ? frozenAirfield :
                (registry == null ? null : registry.SelectedAirfield);
            AERISRunwayDirectionDefinition direction = ActiveDirection;
            AERISRunwayDefinition runway = Armed ? frozenRunway :
                (registry == null ? null : registry.SelectedRunway);
            if (airfield == null || runway == null || direction == null ||
                !direction.HasCertifiedGeometry || !direction.HeadingMatchesGeometry)
                return false;
            token = AERISRunwayTrackToken.Create(airfield, runway, direction,
                Armed ? frozenDatabaseRevision : registry.DatabaseRevision);
            return token != null;
        }

        static AERISRunwayDirectionDefinition FindDirection(
            AERISAirfieldDefinition airfield, string stableId)
        {
            if (airfield == null || string.IsNullOrEmpty(stableId)) return null;
            for (int i = 0; i < airfield.Runways.Count; i++)
                for (int j = 0; j < airfield.Runways[i].Directions.Count; j++)
                    if (string.Equals(airfield.Runways[i].Directions[j].StableId, stableId,
                        StringComparison.OrdinalIgnoreCase))
                        return airfield.Runways[i].Directions[j];
            return null;
        }

        static AERISRunwayObservation BuildObservation(Vessel vessel,
            VirtualAttitudeInstrument attitude, AERISAirfieldDefinition airfield,
            AERISRunwayDirectionDefinition direction, bool armed)
        {
            var result = new AERISRunwayObservation();
            if (vessel == null || vessel.mainBody == null)
            {
                result.Status = "ACTIVE VESSEL REQUIRED";
                result.InhibitReason = result.Status;
                return result;
            }
            if (airfield == null || direction == null || !direction.HasFiniteGeometry)
            {
                result.Status = "RUNWAY GEOMETRY UNAVAILABLE";
                result.InhibitReason = result.Status;
                return result;
            }
            result.RunwayGeometryDirectionValid = direction.HeadingMatchesGeometry;
            if (!result.RunwayGeometryDirectionValid)
            {
                result.Status = "RUNWAY GEOMETRY HEADING MISMATCH";
                result.InhibitReason = result.Status;
                return result;
            }
            if (!string.Equals(vessel.mainBody.name, airfield.Body, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "SELECTED RUNWAY IS ON " + airfield.Body.ToUpperInvariant();
                result.InhibitReason = result.Status;
                return result;
            }

            double radius = Math.Max(1.0, vessel.mainBody.Radius);
            double vesselLat = vessel.latitude;
            double vesselLon = vessel.longitude;
            ToLocalMeters(vesselLat, vesselLon, direction.Threshold.LatitudeDeg,
                direction.Threshold.LongitudeDeg, radius,
                out result.ThresholdEastMeters, out result.ThresholdNorthMeters);
            ToLocalMeters(vesselLat, vesselLon, direction.OppositeThreshold.LatitudeDeg,
                direction.OppositeThreshold.LongitudeDeg, radius,
                out result.OppositeEastMeters, out result.OppositeNorthMeters);

            double runwayEast = result.OppositeEastMeters - result.ThresholdEastMeters;
            double runwayNorth = result.OppositeNorthMeters - result.ThresholdNorthMeters;
            double runwayLength = Math.Sqrt(runwayEast * runwayEast + runwayNorth * runwayNorth);
            if (runwayLength < 1.0)
            {
                result.Status = "RUNWAY GEOMETRY DEGENERATE";
                result.InhibitReason = result.Status;
                return result;
            }
            double unitEast = runwayEast / runwayLength;
            double unitNorth = runwayNorth / runwayLength;
            double vesselFromThresholdEast = -result.ThresholdEastMeters;
            double vesselFromThresholdNorth = -result.ThresholdNorthMeters;
            result.AlongRunwayMeters = vesselFromThresholdEast * unitEast +
                vesselFromThresholdNorth * unitNorth;
            result.CrossTrackMeters = vesselFromThresholdEast * unitNorth -
                vesselFromThresholdNorth * unitEast;
            result.OnApproachSide = result.AlongRunwayMeters <= 50.0;
            result.ApproachDistanceMeters = result.OnApproachSide ?
                Math.Max(0.0, -result.AlongRunwayMeters) : 0.0;
            result.DistanceToThresholdMeters = Math.Sqrt(
                result.ThresholdEastMeters * result.ThresholdEastMeters +
                result.ThresholdNorthMeters * result.ThresholdNorthMeters);
            result.BearingToThresholdDeg = BearingDeg(vesselLat, vesselLon,
                direction.Threshold.LatitudeDeg, direction.Threshold.LongitudeDeg);
            result.VesselHeadingDeg = attitude != null && attitude.InstrumentHeadingValid
                ? attitude.InstrumentHeadingDeg : result.BearingToThresholdDeg;
            result.InterceptAngleDeg = Math.Abs(DeltaAngle(result.VesselHeadingDeg,
                direction.HeadingDeg));
            result.VesselAltitudeAslMeters = attitude != null && attitude.AltitudeAslValid
                ? attitude.AltitudeAslM : Math.Max(0.0, vessel.altitude);
            if (result.OnApproachSide)
            {
                result.GlidePathTargetAltitudeMeters = direction.Threshold.ElevationMeters +
                    direction.ThresholdCrossingHeightMeters +
                    Math.Tan(direction.GlidePathAngleDeg * Math.PI / 180.0) *
                    result.ApproachDistanceMeters;
                result.GlidePathErrorMeters = result.VesselAltitudeAslMeters -
                    result.GlidePathTargetAltitudeMeters;
            }
            else
            {
                result.GlidePathTargetAltitudeMeters = double.NaN;
                result.GlidePathErrorMeters = double.NaN;
            }

            double funnelHalfWidth = Math.Max(120.0,
                result.ApproachDistanceMeters * Math.Tan(direction.LocalizerCaptureAngleDeg *
                Math.PI / 180.0));
            result.LocalizerGeometryEligible = result.OnApproachSide &&
                result.ApproachDistanceMeters >= 250.0 &&
                result.ApproachDistanceMeters <= direction.LocalizerCaptureDistanceMeters &&
                Math.Abs(result.CrossTrackMeters) <= funnelHalfWidth &&
                result.InterceptAngleDeg <= direction.LocalizerCaptureAngleDeg;
            result.GlidePathGeometryEligible = result.LocalizerGeometryEligible &&
                result.ApproachDistanceMeters <= direction.GlidePathCaptureDistanceMeters &&
                result.GlidePathErrorMeters >= -250.0 && result.GlidePathErrorMeters <= 120.0;

            if (!result.OnApproachSide) result.InhibitReason = "NOT ON APPROACH SIDE";
            else if (!armed) result.InhibitReason = "LAND ARM REQUIRED";
            else if (result.ApproachDistanceMeters > direction.LocalizerCaptureDistanceMeters)
                result.InhibitReason = "OUTSIDE LOC CAPTURE DISTANCE";
            else if (Math.Abs(result.CrossTrackMeters) > funnelHalfWidth)
                result.InhibitReason = "OUTSIDE LOC FUNNEL";
            else if (result.InterceptAngleDeg > direction.LocalizerCaptureAngleDeg)
                result.InhibitReason = "LOC INTERCEPT ANGLE HIGH";
            else if (result.GlidePathErrorMeters > 120.0)
                result.InhibitReason = "ABOVE GLIDE PATH";
            else if (result.GlidePathErrorMeters < -250.0)
                result.InhibitReason = "BELOW GLIDE PATH — MAINTAIN ALTITUDE";
            else result.InhibitReason = "CAPTURE LOGIC NOT ENABLED IN FIRST GATE";

            result.Valid = true;
            result.Status = result.OnApproachSide ?
                "OBSERVATION VALID / CONTROL PILOT / LOC WAIT / GS WAIT" :
                "OBSERVATION VALID / NOT ON APPROACH SIDE / GS N/A";
            return result;
        }

        static void ToLocalMeters(double originLatDeg, double originLonDeg,
            double targetLatDeg, double targetLonDeg, double radius,
            out double eastMeters, out double northMeters)
        {
            double originLat = originLatDeg * Math.PI / 180.0;
            double targetLat = targetLatDeg * Math.PI / 180.0;
            double dLat = targetLat - originLat;
            double dLon = AERISAirfieldConfigParser.NormalizeLongitude(targetLonDeg - originLonDeg) *
                Math.PI / 180.0;
            double meanLat = (originLat + targetLat) * 0.5;
            eastMeters = dLon * Math.Cos(meanLat) * radius;
            northMeters = dLat * radius;
        }

        static double BearingDeg(double lat1Deg, double lon1Deg, double lat2Deg, double lon2Deg)
        {
            var a = new AERISGeoPoint { LatitudeDeg = lat1Deg, LongitudeDeg = lon1Deg };
            var b = new AERISGeoPoint { LatitudeDeg = lat2Deg, LongitudeDeg = lon2Deg };
            return AERISAirfieldConfigParser.InitialBearingDeg(a, b);
        }

        static double DeltaAngle(double current, double target)
        {
            double value = (target - current) % 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }
    }
}
