using System;
using System.Collections.Generic;
using System.Globalization;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    internal static class AERISAirfieldConfigParser
    {
        internal static void ParseFile(string path, IList<AERISAirfieldDefinition> output)
        {
            if (string.IsNullOrEmpty(path) || output == null) return;
            try
            {
                ConfigNode loaded = ConfigNode.Load(path);
                if (loaded == null) return;
                if (string.Equals(loaded.name, "AERISAirfields", StringComparison.OrdinalIgnoreCase))
                    ParseRoot(loaded, path, output);
                ConfigNode[] roots = loaded.GetNodes("AERISAirfields");
                if (roots != null)
                    for (int i = 0; i < roots.Length; i++) ParseRoot(roots[i], path, output);
            }
            catch (Exception ex)
            {
                AERISLogger.Warn("[AIRFIELD_REGISTRY] parse failed: " + path + "; " + ex.Message);
            }
        }

        static void ParseRoot(ConfigNode root, string path, IList<AERISAirfieldDefinition> output)
        {
            ConfigNode[] nodes = root.GetNodes("Airfield");
            if (nodes == null) return;
            for (int i = 0; i < nodes.Length; i++)
            {
                AERISAirfieldDefinition airfield = ParseAirfield(nodes[i], path);
                if (airfield != null) output.Add(airfield);
            }
        }

        static AERISAirfieldDefinition ParseAirfield(ConfigNode node, string path)
        {
            if (node == null) return null;
            var value = new AERISAirfieldDefinition();
            value.Id = ReadString(node, "id", string.Empty).Trim();
            value.Body = ReadString(node, "body", "Kerbin").Trim();
            value.DisplayName = ReadString(node, "displayName", value.Id).Trim();
            value.Description = ReadString(node, "description", string.Empty).Trim();
            value.Source = ReadEnum(node, "source", AERISAirfieldSource.UserCfg);
            value.FacilityKind = ReadEnum(node, "facilityType", AERISFacilityKind.Unknown);
            value.Validation = ReadEnum(node, "validation", AERISAirfieldValidation.DiscoveryOnly);
            value.ProviderSiteId = ReadString(node, "providerSiteId", string.Empty).Trim();
            value.ProviderGroup = ReadString(node, "providerGroup", string.Empty).Trim();
            value.ProviderUuid = ReadString(node, "providerUUID", string.Empty).Trim();
            value.SourceMod = ReadString(node, "sourceMod", string.Empty).Trim();
            value.DefinitionVersion = ReadString(node, "definitionVersion", "1").Trim();
            value.SourcePath = path ?? string.Empty;
            ReadDouble(node, "referenceLatitude", out value.ReferenceLatitudeDeg);
            ReadDouble(node, "referenceLongitude", out value.ReferenceLongitudeDeg);
            ReadDouble(node, "referenceElevation", out value.ReferenceElevationMeters);

            if (string.IsNullOrEmpty(value.Id) || string.IsNullOrEmpty(value.Body) ||
                string.IsNullOrEmpty(value.DisplayName))
            {
                AERISLogger.Warn("[AIRFIELD_REGISTRY] invalid Airfield node in " + path);
                return null;
            }

            ConfigNode[] runways = node.GetNodes("Runway");
            if (runways != null)
                for (int i = 0; i < runways.Length; i++)
                {
                    AERISRunwayDefinition runway = ParseRunway(runways[i], value);
                    if (runway != null) value.Runways.Add(runway);
                }
            return value;
        }

        static AERISRunwayDefinition ParseRunway(ConfigNode node, AERISAirfieldDefinition airfield)
        {
            if (node == null) return null;
            var runway = new AERISRunwayDefinition();
            runway.Id = ReadString(node, "id", string.Empty).Trim();
            runway.DisplayName = ReadString(node, "displayName", runway.Id).Trim();
            runway.ProviderSiteId = ReadString(node, "providerSiteId", airfield.ProviderSiteId).Trim();
            runway.ProviderUuid = ReadString(node, "providerUUID", airfield.ProviderUuid).Trim();
            runway.StableId = airfield.StableId + "\n" + runway.Id;
            ReadDouble(node, "length", out runway.LengthMeters);
            ReadDouble(node, "width", out runway.WidthMeters);
            runway.Surface = ReadString(node, "surface", "UNKNOWN").Trim();
            if (string.IsNullOrEmpty(runway.Id)) return null;

            ConfigNode[] directions = node.GetNodes("Direction");
            if (directions != null)
                for (int i = 0; i < directions.Length; i++)
                {
                    AERISRunwayDirectionDefinition direction = ParseDirection(directions[i], airfield, runway);
                    if (direction != null) runway.Directions.Add(direction);
                }
            if (runway.LengthMeters <= 0.0 && runway.Directions.Count > 0)
                runway.LengthMeters = GreatCircleDistanceMeters(runway.Directions[0].Threshold,
                    runway.Directions[0].OppositeThreshold, BodyRadius(airfield.Body));
            return runway;
        }

        static AERISRunwayDirectionDefinition ParseDirection(ConfigNode node,
            AERISAirfieldDefinition airfield, AERISRunwayDefinition runway)
        {
            if (node == null) return null;
            var direction = new AERISRunwayDirectionDefinition();
            direction.Id = ReadString(node, "id", string.Empty).Trim();
            direction.DisplayName = ReadString(node, "displayName", direction.Id).Trim();
            bool geometry = ReadDouble(node, "thresholdLat", out direction.Threshold.LatitudeDeg) &&
                ReadDouble(node, "thresholdLon", out direction.Threshold.LongitudeDeg) &&
                ReadDouble(node, "oppositeThresholdLat", out direction.OppositeThreshold.LatitudeDeg) &&
                ReadDouble(node, "oppositeThresholdLon", out direction.OppositeThreshold.LongitudeDeg);
            ReadDouble(node, "thresholdElevation", out direction.Threshold.ElevationMeters);
            ReadDouble(node, "oppositeThresholdElevation", out direction.OppositeThreshold.ElevationMeters);
            if (!ReadDouble(node, "heading", out direction.HeadingDeg) && geometry)
                direction.HeadingDeg = InitialBearingDeg(direction.Threshold, direction.OppositeThreshold);
            direction.HeadingDeg = NormalizeHeading(direction.HeadingDeg);
            if (!ReadDouble(node, "glidePathAngle", out direction.GlidePathAngleDeg)) direction.GlidePathAngleDeg = 3.0;
            if (!ReadDouble(node, "thresholdCrossingHeight", out direction.ThresholdCrossingHeightMeters)) direction.ThresholdCrossingHeightMeters = 15.0;
            if (!ReadDouble(node, "localizerCaptureAngle", out direction.LocalizerCaptureAngleDeg)) direction.LocalizerCaptureAngleDeg = 25.0;
            if (!ReadDouble(node, "localizerCaptureDistance", out direction.LocalizerCaptureDistanceMeters)) direction.LocalizerCaptureDistanceMeters = 30000.0;
            if (!ReadDouble(node, "glidePathCaptureDistance", out direction.GlidePathCaptureDistanceMeters)) direction.GlidePathCaptureDistanceMeters = 20000.0;
            if (!ReadDouble(node, "missedApproachHeading", out direction.MissedApproachHeadingDeg)) direction.MissedApproachHeadingDeg = direction.HeadingDeg;
            if (!ReadDouble(node, "missedApproachSafeAltitude", out direction.MissedApproachSafeAltitudeMeters)) direction.MissedApproachSafeAltitudeMeters = 1000.0;
            direction.StableId = airfield.StableId + "\n" + runway.Id + "\n" + direction.Id;

            if (string.IsNullOrEmpty(direction.Id) || !geometry || !direction.HasFiniteGeometry)
            {
                AERISLogger.Warn("[AIRFIELD_REGISTRY] invalid runway direction " +
                    airfield.Id + "/" + runway.Id + "/" + direction.Id);
                return null;
            }
            ReadOptionalPoint(node, "physicalStart", ref direction.PhysicalStart);
            ReadOptionalPoint(node, "physicalEnd", ref direction.PhysicalEnd);
            ReadOptionalPoint(node, "usableStart", ref direction.UsableStart);
            ReadOptionalPoint(node, "usableEnd", ref direction.UsableEnd);
            ReadOptionalPoint(node, "touchdownAim", ref direction.TouchdownAim);
            ReadOptionalPoint(node, "rolloutEnd", ref direction.RolloutEnd);

            AERISRunwayCertificationBasis configuredBasis = ReadEnum(node,
                "certificationBasis", AERISRunwayCertificationBasis.Unknown);
            string configuredBasisDetail = ReadString(node, "certificationBasisDetail",
                string.Empty).Trim();
            bool trustedConfiguredGeometry =
                airfield.Validation == AERISAirfieldValidation.FoundationValidated ||
                airfield.Validation == AERISAirfieldValidation.PrecisionValidated;
            if (trustedConfiguredGeometry)
            {
                direction.CertificationState = AERISRunwayCertificationState.Certified;
                direction.CertificationBasis = configuredBasis;
                direction.CertificationBasisDetail = configuredBasisDetail;
                direction.ClassificationConfidence = 1.0;
                direction.GeometryConfidence = 1.0;
                direction.EvidenceFamilies = AERISRunwayEvidenceFamily.MetadataSemantic |
                    AERISRunwayEvidenceFamily.GeometryTopology |
                    AERISRunwayEvidenceFamily.OperationalLayout;
                direction.MeasurementMethods = AERISRunwayMeasurementMethod.M01Metadata |
                    AERISRunwayMeasurementMethod.M15SpawnHeading |
                    AERISRunwayMeasurementMethod.M20ReciprocalConsistency;
                if (configuredBasis == AERISRunwayCertificationBasis.UserCalibrated)
                {
                    direction.EvidenceFamilies |= AERISRunwayEvidenceFamily.UserCalibration |
                        AERISRunwayEvidenceFamily.ExternalRunwayWitness;
                    direction.MeasurementMethods |=
                        AERISRunwayMeasurementMethod.M29UserCalibration;
                }
                direction.PendingDetail = string.Empty;
                direction.FailureCode = AERISRunwayFailureCode.None;
                direction.GeometryFingerprint = "CFG:" + airfield.Id + ":" + runway.Id + ":" + direction.Id +
                    ":" + airfield.DefinitionVersion;
                direction.CertifiedUtc = "CONFIGURED_BASELINE";
            }
            direction.PopulateOperationalReferences(Math.Min(300.0,
                Math.Max(60.0, runway.LengthMeters > 0.0 ? runway.LengthMeters * 0.12 : 150.0)));
            return direction;
        }

        static void ReadOptionalPoint(ConfigNode node, string prefix, ref AERISGeoPoint point)
        {
            double latitude;
            double longitude;
            if (!ReadDouble(node, prefix + "Lat", out latitude) ||
                !ReadDouble(node, prefix + "Lon", out longitude)) return;
            double elevation;
            if (!ReadDouble(node, prefix + "Elevation", out elevation)) elevation = 0.0;
            var candidate = new AERISGeoPoint
            {
                LatitudeDeg = latitude,
                LongitudeDeg = NormalizeLongitude(longitude),
                ElevationMeters = elevation
            };
            if (candidate.IsFinite) point = candidate;
        }

        static T ReadEnum<T>(ConfigNode node, string key, T fallback) where T : struct
        {
            if (node == null || !node.HasValue(key)) return fallback;
            T value;
            return Enum.TryParse<T>(node.GetValue(key), true, out value) ? value : fallback;
        }

        static string ReadString(ConfigNode node, string key, string fallback)
        {
            return node != null && node.HasValue(key) ? node.GetValue(key) ?? fallback : fallback;
        }

        static bool ReadDouble(ConfigNode node, string key, out double value)
        {
            value = 0.0;
            if (node == null || !node.HasValue(key)) return false;
            return double.TryParse(node.GetValue(key), NumberStyles.Float, CultureInfo.InvariantCulture,
                out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        internal static double NormalizeHeading(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        internal static double NormalizeLongitude(double value)
        {
            value %= 360.0;
            if (value > 180.0) value -= 360.0;
            if (value < -180.0) value += 360.0;
            return value;
        }

        internal static double GreatCircleDistanceMeters(AERISGeoPoint a, AERISGeoPoint b,
            double radius)
        {
            if (a == null || b == null || radius <= 0.0) return 0.0;
            double lat1 = a.LatitudeDeg * Math.PI / 180.0;
            double lat2 = b.LatitudeDeg * Math.PI / 180.0;
            double dLat = lat2 - lat1;
            double dLon = NormalizeLongitude(b.LongitudeDeg - a.LongitudeDeg) * Math.PI / 180.0;
            double h = Math.Sin(dLat * 0.5) * Math.Sin(dLat * 0.5) +
                Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(dLon * 0.5) * Math.Sin(dLon * 0.5);
            h = Math.Max(0.0, Math.Min(1.0, h));
            return radius * 2.0 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1.0 - h));
        }

        internal static double InitialBearingDeg(AERISGeoPoint a, AERISGeoPoint b)
        {
            double lat1 = a.LatitudeDeg * Math.PI / 180.0;
            double lat2 = b.LatitudeDeg * Math.PI / 180.0;
            double dLon = NormalizeLongitude(b.LongitudeDeg - a.LongitudeDeg) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) -
                Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
            return NormalizeHeading(Math.Atan2(y, x) * 180.0 / Math.PI);
        }

        internal static AERISGeoPoint InterpolateGeo(AERISGeoPoint start,
            AERISGeoPoint end, double distanceFromStartMeters)
        {
            if (start == null || end == null || !start.IsFinite || !end.IsFinite)
                return start == null ? new AERISGeoPoint() : start.Clone();
            const double radius = 600000.0;
            double length = GreatCircleDistanceMeters(start, end, radius);
            if (length <= 0.001) return start.Clone();
            double fraction = Math.Max(0.0, Math.Min(1.0, distanceFromStartMeters / length));
            double lonDelta = NormalizeLongitude(end.LongitudeDeg - start.LongitudeDeg);
            return new AERISGeoPoint
            {
                LatitudeDeg = start.LatitudeDeg + (end.LatitudeDeg - start.LatitudeDeg) * fraction,
                LongitudeDeg = NormalizeLongitude(start.LongitudeDeg + lonDelta * fraction),
                ElevationMeters = start.ElevationMeters +
                    (end.ElevationMeters - start.ElevationMeters) * fraction
            };
        }

        static double BodyRadius(string body)
        {
            if (string.Equals(body, "Kerbin", StringComparison.OrdinalIgnoreCase)) return 600000.0;
            return 600000.0;
        }
    }
}
