using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Landing
{
    // Main-thread finalizer.  Worker candidates are converted to body coordinates and
    // each approach direction receives an independent terrain/corridor decision.
    internal static class AERISOperationalRunwayResolver
    {
        delegate double TerrainAltitude2(CelestialBody body, double latitude, double longitude);
        delegate double TerrainAltitude3(CelestialBody body, double latitude, double longitude,
            bool allowNegative);

        static bool terrainResolved;
        static MethodInfo terrainMethod;
        static TerrainAltitude2 terrain2;
        static TerrainAltitude3 terrain3;
        static readonly object[] terrainArgs2 = new object[2];
        static readonly object[] terrainArgs3 = new object[3];

        internal static bool TryResolve(AERISProviderFacilityRecord record,
            AERISRunwaySurveySnapshot snapshot, AERISRunwaySurveyResult result,
            long geometryRevision, out List<AERISRunwayDefinition> runways,
            out string detail)
        {
            runways = new List<AERISRunwayDefinition>();
            detail = string.Empty;
            bool provisionalResult = result != null &&
                result.State == AERISRunwayCertificationState.Provisional;
            if (record == null || snapshot == null || result == null ||
                (result.State != AERISRunwayCertificationState.Certified &&
                 result.State != AERISRunwayCertificationState.Provisional) ||
                result.Runways == null || result.Runways.Length == 0)
            {
                detail = "NO CERTIFIED/PROVISIONAL WORKER CANDIDATE";
                return false;
            }
            CelestialBody body = record.RuntimeBody;
            if (body == null || body.Radius <= 0.0)
            {
                detail = "CELESTIAL BODY UNAVAILABLE FOR FINAL GEOMETRY";
                return false;
            }

            int certifiedDirections = 0;
            for (int i = 0; i < result.Runways.Length; i++)
            {
                AERISRunwayAxisCandidate axis = result.Runways[i];
                if (axis == null) continue;
                if (snapshot.AbsolutePlacementRequired &&
                    (!axis.AxisRegistrationValid || !axis.AbsolutePlacementValid))
                {
                    AERISLogger.Error("[RUNWAY_AXIS] CERTIFICATION REJECTED; site=" +
                        snapshot.ProviderSiteId + "; runwayIndex=" + i +
                        "; reason=" + (!axis.AxisRegistrationValid
                            ? "RUNWAY_AXIS_INVALID" : "ABSOLUTE_PLACEMENT_INVALID") +
                        "; axisDetail=" + axis.AxisRegistrationDetail +
                        "; placementDetail=" + axis.AbsolutePlacementDetail + ".");
                    continue;
                }
                AERISRunwayDefinition runway = BuildRunway(record, snapshot, axis,
                    i, geometryRevision, "CERT_", true);
                if (runway == null) continue;
                LogAbsolutePlacement(snapshot, axis, i, runway);
                LogRunwayBasis(snapshot, axis, i, runway);
                if (provisionalResult || axis.CertificationBasis ==
                    AERISRunwayCertificationBasis.ProvisionalGeometry)
                {
                    MarkProvisional(runway.Directions[0], result.FailureCode,
                        result.Detail, axis);
                    MarkProvisional(runway.Directions[1], result.FailureCode,
                        result.Detail, axis);
                }
                else
                {
                    ValidateApproach(body, runway.Directions[0], true,
                        axis.ApproachAAvailable);
                    ValidateApproach(body, runway.Directions[1], false,
                        axis.ApproachBAvailable);
                }
                certifiedDirections += runway.CertifiedDirectionCount;
                runways.Add(runway);
            }
            if (runways.Count == 0)
            {
                detail = "NO FINITE OPERATIONAL RUNWAY GEOMETRY";
                return false;
            }
            int provisionalDirections = 0;
            for (int i = 0; i < runways.Count; i++)
                for (int j = 0; j < runways[i].Directions.Count; j++)
                    if (runways[i].Directions[j].CertificationState ==
                        AERISRunwayCertificationState.Provisional)
                        provisionalDirections++;
            detail = runways.Count + " PHYSICAL RUNWAY(S), " + certifiedDirections +
                " CERTIFIED APPROACH DIRECTION(S), " + provisionalDirections +
                " PROVISIONAL NON-SELECTABLE DIRECTION(S)";
            return certifiedDirections > 0;
        }

        static AERISRunwayDefinition BuildRunway(AERISProviderFacilityRecord record,
            AERISRunwaySurveySnapshot snapshot, AERISRunwayAxisCandidate axis,
            int index, long geometryRevision, string idPrefix,
            bool enforceAbsolutePlacement)
        {
            if (enforceAbsolutePlacement && snapshot.AbsolutePlacementRequired &&
                (!axis.AxisRegistrationValid || !axis.AbsolutePlacementValid))
                return null;
            double midpoint = (axis.PhysicalStartMeters + axis.PhysicalEndMeters) * 0.5;
            double normalEast = -axis.AxisNorth;
            double normalNorth = axis.AxisEast;
            double centerAcross = axis.CenterEast * normalEast + axis.CenterNorth * normalNorth;
            AERISGeoPoint physicalA = Offset(snapshot, axis.AxisEast * axis.PhysicalStartMeters +
                normalEast * centerAcross, axis.AxisNorth * axis.PhysicalStartMeters +
                normalNorth * centerAcross, axis.CenterUp);
            AERISGeoPoint physicalB = Offset(snapshot, axis.AxisEast * axis.PhysicalEndMeters +
                normalEast * centerAcross, axis.AxisNorth * axis.PhysicalEndMeters +
                normalNorth * centerAcross, axis.CenterUp);
            AERISGeoPoint usableA = Offset(snapshot, axis.AxisEast * axis.UsableStartMeters +
                normalEast * centerAcross, axis.AxisNorth * axis.UsableStartMeters +
                normalNorth * centerAcross, axis.CenterUp);
            AERISGeoPoint usableB = Offset(snapshot, axis.AxisEast * axis.UsableEndMeters +
                normalEast * centerAcross, axis.AxisNorth * axis.UsableEndMeters +
                normalNorth * centerAcross, axis.CenterUp);
            AERISGeoPoint thresholdA = Offset(snapshot,
                axis.AxisEast * axis.OperationalThresholdA + normalEast * centerAcross,
                axis.AxisNorth * axis.OperationalThresholdA + normalNorth * centerAcross,
                axis.CenterUp);
            AERISGeoPoint thresholdB = Offset(snapshot,
                axis.AxisEast * axis.OperationalThresholdB + normalEast * centerAcross,
                axis.AxisNorth * axis.OperationalThresholdB + normalNorth * centerAcross,
                axis.CenterUp);
            if (!physicalA.IsFinite || !physicalB.IsFinite || !usableA.IsFinite ||
                !usableB.IsFinite || !thresholdA.IsFinite || !thresholdB.IsFinite) return null;

            string runwayId = (string.IsNullOrEmpty(idPrefix) ? "RWY_" : idPrefix) +
                Sanitize(record.ProviderSiteId) + "_" + index;
            var runway = new AERISRunwayDefinition
            {
                Id = runwayId,
                DisplayName = "RWY " + RunwayNumber(axis.HeadingDeg) + "/" +
                    RunwayNumber(axis.HeadingDeg + 180.0),
                ProviderSiteId = record.ProviderSiteId,
                ProviderUuid = record.ProviderUuid,
                StableId = snapshot.StableRecordId + "\n" + runwayId,
                LengthMeters = axis.LengthMeters,
                WidthMeters = axis.WidthMeters,
                Surface = string.IsNullOrEmpty(snapshot.Surface) ? "UNKNOWN" : snapshot.Surface,
                GeometryFingerprint = snapshot.InputFingerprint,
                GeometryRevision = geometryRevision
            };
            double halfWidth = Math.Max(1.0, axis.WidthMeters * 0.5);
            runway.UsablePolygon.Add(Offset(snapshot,
                axis.AxisEast * axis.UsableStartMeters + normalEast * (centerAcross - halfWidth),
                axis.AxisNorth * axis.UsableStartMeters + normalNorth * (centerAcross - halfWidth),
                axis.CenterUp));
            runway.UsablePolygon.Add(Offset(snapshot,
                axis.AxisEast * axis.UsableStartMeters + normalEast * (centerAcross + halfWidth),
                axis.AxisNorth * axis.UsableStartMeters + normalNorth * (centerAcross + halfWidth),
                axis.CenterUp));
            runway.UsablePolygon.Add(Offset(snapshot,
                axis.AxisEast * axis.UsableEndMeters + normalEast * (centerAcross + halfWidth),
                axis.AxisNorth * axis.UsableEndMeters + normalNorth * (centerAcross + halfWidth),
                axis.CenterUp));
            runway.UsablePolygon.Add(Offset(snapshot,
                axis.AxisEast * axis.UsableEndMeters + normalEast * (centerAcross - halfWidth),
                axis.AxisNorth * axis.UsableEndMeters + normalNorth * (centerAcross - halfWidth),
                axis.CenterUp));
            runway.WidthProfileMeters.Add(axis.WidthMeters);
            runway.WidthProfileMeters.Add(axis.WidthMeters);
            runway.WidthProfileMeters.Add(axis.WidthMeters);

            AERISRunwayDirectionDefinition a = BuildDirection(runway, snapshot,
                axis, "A", thresholdA, thresholdB, physicalA, physicalB, usableA,
                usableB, axis.HeadingDeg, geometryRevision);
            AERISRunwayDirectionDefinition b = BuildDirection(runway, snapshot,
                axis, "B", thresholdB, thresholdA, physicalB, physicalA, usableB,
                usableA, axis.HeadingDeg + 180.0, geometryRevision);
            runway.Directions.Add(a);
            runway.Directions.Add(b);
            if (axis.CertificationBasis == AERISRunwayCertificationBasis.UserCalibrated)
            {
                string reciprocalDetail;
                if (!ValidateReciprocalDirectionPair(a, b, out reciprocalDetail))
                {
                    AERISLogger.Error("[RUNWAY_CALIBRATION] RECIPROCAL PAIR REJECTED; site=" +
                        snapshot.ProviderSiteId + "; runway=" + runway.DisplayName +
                        "; detail=" + reciprocalDetail + ".");
                    return null;
                }
                AERISLogger.Info("[RUNWAY_CALIBRATION] RECIPROCAL PAIR GENERATED; site=" +
                    snapshot.ProviderSiteId + "; physicalRunway=" + runway.DisplayName +
                    "; directionA=" + a.DisplayName + "; directionB=" + b.DisplayName +
                    "; localizerPair=True; approachValidation=INDEPENDENT; detail=" +
                    reciprocalDetail + ".");
            }
            return runway;
        }

        static bool ValidateReciprocalDirectionPair(
            AERISRunwayDirectionDefinition a, AERISRunwayDirectionDefinition b,
            out string detail)
        {
            detail = string.Empty;
            if (a == null || b == null || !a.HasFiniteGeometry || !b.HasFiniteGeometry)
            {
                detail = "DIRECTION GEOMETRY MISSING/NON-FINITE";
                return false;
            }
            double headingError = HeadingDifference(a.HeadingDeg,
                AERISAirfieldConfigParser.NormalizeHeading(b.HeadingDeg + 180.0));
            if (headingError > 0.5)
            {
                detail = "RECIPROCAL HEADING ERROR " +
                    headingError.ToString("0.000", CultureInfo.InvariantCulture) + " DEG";
                return false;
            }
            if (!SamePoint(a.Threshold, b.OppositeThreshold) ||
                !SamePoint(b.Threshold, a.OppositeThreshold))
            {
                detail = "RECIPROCAL THRESHOLD/OPPOSITE-END SWAP MISMATCH";
                return false;
            }
            if (string.Equals(a.StableId, b.StableId,
                StringComparison.OrdinalIgnoreCase))
            {
                detail = "RECIPROCAL DIRECTIONS SHARE A STABLE ID";
                return false;
            }
            detail = "HEADINGS RECIPROCAL; THRESHOLDS SWAPPED; STABLE IDS DISTINCT";
            return true;
        }

        static bool SamePoint(AERISGeoPoint a, AERISGeoPoint b)
        {
            return a != null && b != null && a.IsFinite && b.IsFinite &&
                Math.Abs(a.LatitudeDeg - b.LatitudeDeg) <= 0.0000001 &&
                Math.Abs(NormalizeSignedLongitude(a.LongitudeDeg - b.LongitudeDeg)) <=
                    0.0000001 &&
                Math.Abs(a.ElevationMeters - b.ElevationMeters) <= 0.5;
        }

        static double NormalizeSignedLongitude(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        static AERISRunwayDirectionDefinition BuildDirection(AERISRunwayDefinition runway,
            AERISRunwaySurveySnapshot snapshot, AERISRunwayAxisCandidate axis,
            string suffix, AERISGeoPoint threshold, AERISGeoPoint oppositeThreshold,
            AERISGeoPoint physicalStart, AERISGeoPoint physicalEnd,
            AERISGeoPoint usableStart, AERISGeoPoint usableEnd, double heading,
            long geometryRevision)
        {
            heading = AERISAirfieldConfigParser.NormalizeHeading(heading);
            var direction = new AERISRunwayDirectionDefinition
            {
                Id = "RWY_" + RunwayNumber(heading) + "_" + suffix,
                DisplayName = "RWY " + RunwayNumber(heading),
                Threshold = threshold.Clone(),
                OppositeThreshold = oppositeThreshold.Clone(),
                PhysicalStart = physicalStart.Clone(),
                PhysicalEnd = physicalEnd.Clone(),
                UsableStart = usableStart.Clone(),
                UsableEnd = usableEnd.Clone(),
                RolloutEnd = usableEnd.Clone(),
                HeadingDeg = heading,
                GlidePathAngleDeg = 3.0,
                ThresholdCrossingHeightMeters = 15.0,
                LocalizerCaptureAngleDeg = 25.0,
                LocalizerCaptureDistanceMeters = 30000.0,
                GlidePathCaptureDistanceMeters = 20000.0,
                MissedApproachHeadingDeg = heading,
                MissedApproachSafeAltitudeMeters = 1000.0,
                StableId = runway.StableId + "\n" + suffix,
                CertificationState = AERISRunwayCertificationState.Pending,
                FailureCode = AERISRunwayFailureCode.None,
                PendingDetail = "APPROACH VALIDATION",
                ClassificationConfidence = axis.ClassificationConfidence,
                GeometryConfidence = axis.GeometryConfidence,
                CenterlineUncertaintyMeters = axis.CenterlineUncertaintyMeters,
                HeadingUncertaintyDeg = axis.HeadingUncertaintyDeg,
                PhysicalEndUncertaintyMeters = axis.PhysicalEndUncertaintyMeters,
                UsableEndUncertaintyMeters = axis.UsableEndUncertaintyMeters,
                ThresholdUncertaintyMeters = axis.ThresholdUncertaintyMeters,
                LengthUncertaintyMeters = axis.LengthUncertaintyMeters,
                WidthUncertaintyMeters = axis.WidthUncertaintyMeters,
                ElevationUncertaintyMeters = axis.ElevationUncertaintyMeters,
                DisplacedThresholdConfidence = axis.DisplacedThresholdConfidence,
                ApproachCorridorConfidence = axis.ApproachCorridorConfidence,
                EvidenceFamilies = axis.EvidenceFamilies,
                MeasurementMethods = axis.Methods,
                CertificationBasis = axis.CertificationBasis,
                CertificationBasisDetail = axis.CertificationBasisDetail,
                GeometryFingerprint = snapshot.InputFingerprint,
                GeometryRevision = geometryRevision
            };
            NormalizeDirectionGeometry(direction);
            direction.PopulateOperationalReferences(Math.Min(300.0,
                Math.Max(60.0, runway.LengthMeters * 0.12)));
            direction.ParameterEstimates.Add(Estimate("CENTERLINE", 0.0,
                axis.CenterlineUncertaintyMeters, "m", axis));
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "RUNWAY_AXIS_REGISTRATION",
                Value = axis.AxisRegistrationValid ? 1.0 : 0.0,
                Uncertainty = Math.Abs(axis.RegisteredHeadingAfterDeg -
                    axis.MeshSurfaceHeadingDeg),
                Units = "state",
                Confidence = axis.AxisRegistrationValid
                    ? axis.GeometryConfidence : 0.0,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.AxisRegistrationDetail
            });
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "ABSOLUTE_PLACEMENT",
                Value = axis.AbsolutePlacementValid ? 1.0 : 0.0,
                Uncertainty = Math.Abs(axis.LaunchCrossTrackAfterMeters),
                Units = "state",
                Confidence = axis.AbsolutePlacementValid
                    ? axis.GeometryConfidence : 0.0,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.AbsolutePlacementDetail
            });
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "LAUNCH_CROSS_TRACK",
                Value = axis.LaunchCrossTrackBeforeMeters,
                Uncertainty = axis.CenterlineUncertaintyMeters,
                Units = "m",
                Confidence = axis.AbsolutePlacementValid
                    ? axis.GeometryConfidence : 0.0,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.AbsolutePlacementDetail
            });
            direction.ParameterEstimates.Add(Estimate("HEADING", heading,
                axis.HeadingUncertaintyDeg, "deg", axis));
            direction.ParameterEstimates.Add(Estimate("OPERATIONAL_THRESHOLD", 0.0,
                axis.ThresholdUncertaintyMeters, "m", axis));
            direction.ParameterEstimates.Add(Estimate("PHYSICAL_END", runway.LengthMeters,
                axis.PhysicalEndUncertaintyMeters, "m", axis));
            direction.ParameterEstimates.Add(Estimate("USABLE_END", runway.LengthMeters,
                axis.UsableEndUncertaintyMeters, "m", axis));
            direction.ParameterEstimates.Add(Estimate("LENGTH", runway.LengthMeters,
                axis.LengthUncertaintyMeters, "m", axis));
            direction.ParameterEstimates.Add(Estimate("WIDTH", runway.WidthMeters,
                axis.WidthUncertaintyMeters, "m", axis));
            direction.ParameterEstimates.Add(Estimate("ELEVATION", threshold.ElevationMeters,
                axis.ElevationUncertaintyMeters, "m ASL", axis));
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "DISPLACED_THRESHOLD",
                Value = Math.Abs(AERISAirfieldConfigParser.GreatCircleDistanceMeters(
                    physicalStart, threshold, Math.Max(1.0, snapshot.BodyRadiusMeters))),
                Uncertainty = axis.ThresholdUncertaintyMeters,
                Units = "m",
                Confidence = axis.DisplacedThresholdConfidence,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.Detail
            });
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "RUNWAY_CERTIFICATION_BASIS",
                Value = (double)axis.CertificationBasis,
                Uncertainty = 0.0,
                Units = "enum",
                Confidence = axis.GeometryConfidence,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.CertificationBasisDetail
            });
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "ANCHOR_SURFACE_SCAN",
                Value = axis.AnchorScanValid ? 1.0 : 0.0,
                Uncertainty = axis.AnchorWidthSpreadMeters,
                Units = "state",
                Confidence = axis.AnchorScanValid ? axis.GeometryConfidence : 0.0,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.AnchorScanDetail
            });
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "PLAN_WITNESS_MATCH",
                Value = axis.PlanWitnessMatched ? 1.0 : 0.0,
                Uncertainty = axis.PlanWitnessCenterErrorMeters,
                Units = "state",
                Confidence = axis.PlanWitnessMatched ? axis.GeometryConfidence : 0.0,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.PlanWitnessDetail
            });
            direction.ParameterEstimates.Add(new AERISRunwayParameterEstimate
            {
                Name = "APPROACH_CORRIDOR",
                Value = 0.0,
                Uncertainty = 0.0,
                Units = "state",
                Confidence = 0.0,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = "PENDING DIRECTION-SPECIFIC TERRAIN/OBSTACLE VALIDATION"
            });
            return direction;
        }


        static void MarkProvisional(AERISRunwayDirectionDefinition direction,
            AERISRunwayFailureCode failureCode, string resultDetail,
            AERISRunwayAxisCandidate axis)
        {
            if (direction == null) return;
            direction.CertificationState = AERISRunwayCertificationState.Provisional;
            direction.FailureCode = failureCode == AERISRunwayFailureCode.None
                ? AERISRunwayFailureCode.AnchorSurfaceUnresolved : failureCode;
            direction.FailureDetail = string.Empty;
            direction.PendingDetail = "PROVISIONAL — NEVER LAND/ARM ELIGIBLE; " +
                (resultDetail ?? string.Empty);
            direction.ApproachCorridorConfidence = 0.0;
            SetApproachEstimate(direction, 0.0, direction.PendingDetail);
            direction.CertificationBasis = axis == null
                ? AERISRunwayCertificationBasis.ProvisionalGeometry
                : axis.CertificationBasis;
            direction.CertificationBasisDetail = axis == null
                ? "PROVISIONAL GEOMETRY"
                : axis.CertificationBasisDetail;
        }

        static void LogRunwayBasis(AERISRunwaySurveySnapshot snapshot,
            AERISRunwayAxisCandidate axis, int index, AERISRunwayDefinition runway)
        {
            if (snapshot == null || axis == null || runway == null) return;
            AERISLogger.Info("[RUNWAY_CERT_BASIS] site=" + snapshot.ProviderSiteId +
                "; runwayIndex=" + index + "; runway=" + runway.DisplayName +
                "; basis=" + axis.CertificationBasis +
                "; basisDetail=" + axis.CertificationBasisDetail +
                "; classificationConfidence=" + axis.ClassificationConfidence.ToString("0.000",
                    CultureInfo.InvariantCulture) +
                "; geometryConfidence=" + axis.GeometryConfidence.ToString("0.000",
                    CultureInfo.InvariantCulture) + ".");
            AERISLogger.Info("[RUNWAY_ANCHOR_SCAN] site=" + snapshot.ProviderSiteId +
                "; runwayIndex=" + index + "; valid=" + axis.AnchorScanValid +
                "; connectedPrimitives=" + axis.AnchorConnectedPrimitiveCount +
                "; crossSections=" + axis.AnchorCrossSectionCount +
                "; stableRatio=" + axis.AnchorStableCrossSectionRatio.ToString("0.000",
                    CultureInfo.InvariantCulture) +
                "; widthMedianM=" + axis.AnchorWidthMedianMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; widthSpreadM=" + axis.AnchorWidthSpreadMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; detail=" + axis.AnchorScanDetail + ".");
            if (snapshot.RunwayWitnessAvailable || axis.PlanWitnessCompared)
                AERISLogger.Info("[RUNWAY_WITNESS] site=" + snapshot.ProviderSiteId +
                    "; runwayIndex=" + index +
                    "; source=" + snapshot.RunwayWitnessSource +
                    "; name=" + snapshot.RunwayWitnessName +
                    "; userCalibrated=" + snapshot.RunwayWitnessUserCalibrated +
                    "; compared=" + axis.PlanWitnessCompared +
                    "; matched=" + axis.PlanWitnessMatched +
                    "; centerErrorM=" + axis.PlanWitnessCenterErrorMeters.ToString("0.00",
                        CultureInfo.InvariantCulture) +
                    "; headingErrorDeg=" + axis.PlanWitnessHeadingErrorDeg.ToString("0.00",
                        CultureInfo.InvariantCulture) +
                    "; lengthRatio=" + axis.PlanWitnessLengthRatio.ToString("0.000",
                        CultureInfo.InvariantCulture) +
                    "; detail=" + axis.PlanWitnessDetail + ".");
        }

        static void LogAbsolutePlacement(AERISRunwaySurveySnapshot snapshot,
            AERISRunwayAxisCandidate axis, int index, AERISRunwayDefinition runway)
        {
            if (snapshot == null || axis == null || runway == null ||
                !snapshot.AbsolutePlacementRequired) return;
            AERISLogger.Info("[RUNWAY_AXIS] site=" + snapshot.ProviderSiteId +
                "; runwayIndex=" + index +
                "; runway=" + runway.DisplayName +
                "; meshRunwayHeadingDeg=" + axis.MeshSurfaceHeadingDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchTransformHeadingDeg=" + snapshot.LaunchAnchorHeadingDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; registeredHeadingBeforeDeg=" + axis.RegisteredHeadingBeforeDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; registeredHeadingAfterDeg=" + axis.RegisteredHeadingAfterDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; headingCorrectionDeg=" + axis.HeadingCorrectionDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; axisReference=LAUNCH_ANCHOR" +
                "; axisReferenceErrorDeg=" + axis.AxisReferenceErrorDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; surfaceAspect=" + axis.AxisSurfaceAspectRatio.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; surfacePoints=" + axis.AxisSurfacePointCount +
                "; axisRegistrationValid=" + axis.AxisRegistrationValid +
                "; detail=" + axis.AxisRegistrationDetail + ".");
            AERISLogger.Info("[RUNWAY_PLACEMENT] site=" + snapshot.ProviderSiteId +
                "; runwayIndex=" + index +
                "; runway=" + runway.DisplayName +
                "; providerOriginLat=" + snapshot.ReferenceLatitudeDeg.ToString("0.00000000",
                    CultureInfo.InvariantCulture) +
                "; providerOriginLon=" + snapshot.ReferenceLongitudeDeg.ToString("0.00000000",
                    CultureInfo.InvariantCulture) +
                "; providerOriginUsed=" + snapshot.ProviderReferenceOriginUsed +
                "; providerToLaunchM=" + snapshot.ProviderReferenceToLaunchMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchEastM=" + snapshot.LaunchAnchorEastMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchNorthM=" + snapshot.LaunchAnchorNorthMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchCrossBeforeM=" + axis.LaunchCrossTrackBeforeMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchCrossAfterM=" + axis.LaunchCrossTrackAfterMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchAlongM=" + axis.LaunchAlongTrackMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; launchHeadingTelemetryErrorDeg=" + axis.LaunchHeadingErrorDeg.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; axisRegistrationValid=" + axis.AxisRegistrationValid +
                "; correctionM=" + axis.AbsoluteTranslationMeters.ToString("0.00",
                    CultureInfo.InvariantCulture) +
                "; absolutePlacementValid=" + axis.AbsolutePlacementValid +
                "; detail=" + axis.AbsolutePlacementDetail + ".");
        }

        static void NormalizeDirectionGeometry(
            AERISRunwayDirectionDefinition direction)
        {
            if (direction == null || !direction.HasFiniteGeometry) return;
            double beforeBearing = direction.ThresholdBearingDeg;
            double beforeError = HeadingDifference(direction.HeadingDeg, beforeBearing);
            if (beforeError <= 10.0)
            {
                direction.GeometryDirectionDetail = "HEADING/THRESHOLD BEARING ALIGNED " +
                    beforeError.ToString("0.00", CultureInfo.InvariantCulture) + " deg";
                return;
            }
            double reciprocalError = HeadingDifference(direction.HeadingDeg,
                AERISAirfieldConfigParser.NormalizeHeading(beforeBearing + 180.0));
            if (reciprocalError <= 10.0)
            {
                Swap(ref direction.Threshold, ref direction.OppositeThreshold);
                Swap(ref direction.PhysicalStart, ref direction.PhysicalEnd);
                Swap(ref direction.UsableStart, ref direction.UsableEnd);
                direction.RolloutEnd = direction.UsableEnd == null ? null :
                    direction.UsableEnd.Clone();
                direction.TouchdownAim = null;
                direction.GlideSlopeAnchor = null;
                direction.GeometryDirectionAutoCorrected = true;
                direction.GeometryDirectionDetail =
                    "AUTO-CORRECTED RECIPROCAL ENDPOINT ORDER; priorBearing=" +
                    beforeBearing.ToString("0.00", CultureInfo.InvariantCulture) +
                    "; heading=" + direction.HeadingDeg.ToString("0.00",
                        CultureInfo.InvariantCulture);
                AERISLogger.Warn("[RUNWAY_GEOMETRY] " + direction.DisplayName +
                    " reciprocal endpoint order corrected; heading=" +
                    direction.HeadingDeg.ToString("0.00", CultureInfo.InvariantCulture) +
                    "; previousBearing=" + beforeBearing.ToString("0.00",
                        CultureInfo.InvariantCulture) + ".");
                return;
            }
            direction.GeometryDirectionDetail =
                "INVALID HEADING/THRESHOLD BEARING; heading=" +
                direction.HeadingDeg.ToString("0.00", CultureInfo.InvariantCulture) +
                "; bearing=" + beforeBearing.ToString("0.00",
                    CultureInfo.InvariantCulture) + "; error=" +
                beforeError.ToString("0.00", CultureInfo.InvariantCulture);
        }

        static void Swap(ref AERISGeoPoint a, ref AERISGeoPoint b)
        {
            AERISGeoPoint temporary = a;
            a = b;
            b = temporary;
        }

        static double HeadingDifference(double a, double b)
        {
            double delta = AERISAirfieldConfigParser.NormalizeHeading(a) -
                AERISAirfieldConfigParser.NormalizeHeading(b);
            delta %= 360.0;
            if (delta > 180.0) delta -= 360.0;
            if (delta < -180.0) delta += 360.0;
            return Math.Abs(delta);
        }

        static AERISRunwayParameterEstimate Estimate(string name, double value,
            double uncertainty, string units, AERISRunwayAxisCandidate axis)
        {
            return new AERISRunwayParameterEstimate
            {
                Name = name,
                Value = value,
                Uncertainty = uncertainty,
                Units = units,
                Confidence = axis.GeometryConfidence,
                EvidenceFamilies = axis.EvidenceFamilies,
                Methods = axis.Methods,
                Detail = axis.Detail
            };
        }

        static void ValidateApproach(CelestialBody body,
            AERISRunwayDirectionDefinition direction, bool firstDirection,
            bool coarseAvailable)
        {
            if (direction == null || !direction.HasFiniteGeometry)
                return;
            if (!direction.HeadingMatchesGeometry)
            {
                direction.CertificationState = AERISRunwayCertificationState.Failed;
                direction.FailureCode = AERISRunwayFailureCode.ReciprocalMismatch;
                direction.FailureDetail = "RUNWAY GEOMETRY HEADING MISMATCH — " +
                    direction.GeometryDirectionDetail;
                direction.PendingDetail = string.Empty;
                SetApproachEstimate(direction, 0.0, direction.FailureDetail);
                AERISLogger.Error("[RUNWAY_GEOMETRY] CERTIFICATION REJECTED: " +
                    direction.DisplayName + "; " + direction.FailureDetail + ".");
                return;
            }
            if (!coarseAvailable)
            {
                direction.CertificationState = AERISRunwayCertificationState.Failed;
                direction.FailureCode = AERISRunwayFailureCode.ApproachObstacleBlocked;
                direction.FailureDetail = "COARSE APPROACH CORRIDOR REJECTED";
                direction.PendingDetail = string.Empty;
                SetApproachEstimate(direction, 0.0, "COARSE APPROACH CORRIDOR REJECTED");
                return;
            }
            if (!ResolveTerrainMethod())
            {
                direction.CertificationState = AERISRunwayCertificationState.Pending;
                direction.FailureCode = AERISRunwayFailureCode.None;
                direction.PendingDetail = "APPROACH TERRAIN DATA UNAVAILABLE";
                SetApproachEstimate(direction, 0.0, direction.PendingDetail);
                return;
            }

            double headingOut = AERISAirfieldConfigParser.NormalizeHeading(
                direction.HeadingDeg + 180.0);
            double minimumClearance = double.PositiveInfinity;
            const int samples = 16;
            for (int i = 0; i < samples; i++)
            {
                double distance = 250.0 + i * (7750.0 / (samples - 1.0));
                AERISGeoPoint sample = Destination(direction.Threshold, headingOut,
                    distance, body.Radius);
                double terrain;
                if (!TryTerrainSample(body, sample.LatitudeDeg, sample.LongitudeDeg,
                    out terrain))
                {
                    direction.CertificationState = AERISRunwayCertificationState.Pending;
                    direction.PendingDetail = "APPROACH TERRAIN SAMPLE UNAVAILABLE";
                    SetApproachEstimate(direction, 0.0, direction.PendingDetail);
                    return;
                }
                double glideAltitude = direction.Threshold.ElevationMeters +
                    direction.ThresholdCrossingHeightMeters +
                    Math.Tan(direction.GlidePathAngleDeg * Math.PI / 180.0) * distance;
                minimumClearance = Math.Min(minimumClearance, glideAltitude - terrain);
            }
            if (minimumClearance < 10.0)
            {
                direction.CertificationState = AERISRunwayCertificationState.Failed;
                direction.FailureCode = AERISRunwayFailureCode.ApproachTerrainBlocked;
                direction.FailureDetail = "MINIMUM 8 KM GLIDE CORRIDOR CLEARANCE " +
                    minimumClearance.ToString("0.0", CultureInfo.InvariantCulture) + " m";
                direction.PendingDetail = string.Empty;
                SetApproachEstimate(direction, 0.0, direction.FailureDetail);
                return;
            }
            direction.CertificationState = AERISRunwayCertificationState.Certified;
            direction.FailureCode = AERISRunwayFailureCode.None;
            direction.FailureDetail = string.Empty;
            direction.PendingDetail = string.Empty;
            direction.ApproachCorridorConfidence = 0.95;
            SetApproachEstimate(direction, direction.ApproachCorridorConfidence,
                "8 KM TERRAIN CORRIDOR CLEAR; MINIMUM CLEARANCE " +
                minimumClearance.ToString("0.0", CultureInfo.InvariantCulture) + " m");
            direction.CertifiedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        static void SetApproachEstimate(AERISRunwayDirectionDefinition direction,
            double confidence, string detail)
        {
            if (direction == null) return;
            direction.ApproachCorridorConfidence = confidence;
            for (int i = 0; i < direction.ParameterEstimates.Count; i++)
            {
                AERISRunwayParameterEstimate value = direction.ParameterEstimates[i];
                if (!string.Equals(value.Name, "APPROACH_CORRIDOR",
                    StringComparison.OrdinalIgnoreCase)) continue;
                value.Value = confidence > 0.0 ? 1.0 : 0.0;
                value.Confidence = confidence;
                value.Detail = detail ?? string.Empty;
                return;
            }
        }

        static bool ResolveTerrainMethod()
        {
            if (terrainResolved) return terrainMethod != null;
            terrainResolved = true;
            try
            {
                MethodInfo[] methods = typeof(CelestialBody).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    if (!string.Equals(methods[i].Name, "TerrainAltitude",
                        StringComparison.Ordinal)) continue;
                    ParameterInfo[] parameters = methods[i].GetParameters();
                    bool two = parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(double) &&
                        parameters[1].ParameterType == typeof(double);
                    bool three = parameters.Length == 3 &&
                        parameters[0].ParameterType == typeof(double) &&
                        parameters[1].ParameterType == typeof(double) &&
                        parameters[2].ParameterType == typeof(bool);
                    if (!two && !three) continue;
                    terrainMethod = methods[i];
                    try
                    {
                        if (two) terrain2 = (TerrainAltitude2)Delegate.CreateDelegate(
                            typeof(TerrainAltitude2), methods[i]);
                        else terrain3 = (TerrainAltitude3)Delegate.CreateDelegate(
                            typeof(TerrainAltitude3), methods[i]);
                    }
                    catch { terrain2 = null; terrain3 = null; }
                    break;
                }
            }
            catch { terrainMethod = null; }
            return terrainMethod != null;
        }

        internal static bool TryTerrainSample(CelestialBody body, double latitude,
            double longitude,
            out double altitude)
        {
            altitude = 0.0;
            if (body == null || !Finite(latitude) || !Finite(longitude) ||
                !ResolveTerrainMethod()) return false;
            try
            {
                if (terrain2 != null) altitude = terrain2(body, latitude, longitude);
                else if (terrain3 != null) altitude = terrain3(body, latitude, longitude, false);
                else
                {
                    object raw;
                    if (terrainMethod.GetParameters().Length == 2)
                    {
                        terrainArgs2[0] = latitude;
                        terrainArgs2[1] = longitude;
                        raw = terrainMethod.Invoke(body, terrainArgs2);
                    }
                    else
                    {
                        terrainArgs3[0] = latitude;
                        terrainArgs3[1] = longitude;
                        terrainArgs3[2] = false;
                        raw = terrainMethod.Invoke(body, terrainArgs3);
                    }
                    altitude = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                }
                return Finite(altitude);
            }
            catch { return false; }
        }

        static AERISGeoPoint Offset(AERISRunwaySurveySnapshot snapshot,
            double eastMeters, double northMeters, double upMeters)
        {
            double radius = Math.Max(1.0, snapshot.BodyRadiusMeters);
            double distance = Math.Sqrt(eastMeters * eastMeters + northMeters * northMeters);
            if (distance < 0.001)
                return new AERISGeoPoint
                {
                    LatitudeDeg = snapshot.ReferenceLatitudeDeg,
                    LongitudeDeg = snapshot.ReferenceLongitudeDeg,
                    ElevationMeters = snapshot.ReferenceElevationMeters + upMeters
                };
            double bearing = Math.Atan2(eastMeters, northMeters);
            double angular = distance / radius;
            double lat1 = snapshot.ReferenceLatitudeDeg * Math.PI / 180.0;
            double lon1 = snapshot.ReferenceLongitudeDeg * Math.PI / 180.0;
            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angular) +
                Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing));
            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) *
                Math.Cos(lat1), Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
            return new AERISGeoPoint
            {
                LatitudeDeg = lat2 * 180.0 / Math.PI,
                LongitudeDeg = AERISAirfieldConfigParser.NormalizeLongitude(
                    lon2 * 180.0 / Math.PI),
                ElevationMeters = snapshot.ReferenceElevationMeters + upMeters
            };
        }

        static AERISGeoPoint Destination(AERISGeoPoint origin, double headingDeg,
            double distanceMeters, double radius)
        {
            double bearing = headingDeg * Math.PI / 180.0;
            double angular = distanceMeters / Math.Max(1.0, radius);
            double lat1 = origin.LatitudeDeg * Math.PI / 180.0;
            double lon1 = origin.LongitudeDeg * Math.PI / 180.0;
            double lat2 = Math.Asin(Math.Sin(lat1) * Math.Cos(angular) +
                Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing));
            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) *
                Math.Cos(lat1), Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
            return new AERISGeoPoint
            {
                LatitudeDeg = lat2 * 180.0 / Math.PI,
                LongitudeDeg = AERISAirfieldConfigParser.NormalizeLongitude(
                    lon2 * 180.0 / Math.PI),
                ElevationMeters = origin.ElevationMeters
            };
        }

        static string RunwayNumber(double heading)
        {
            int number = (int)Math.Floor((AERISAirfieldConfigParser.NormalizeHeading(heading) + 5.0) /
                10.0) % 36;
            if (number <= 0) number = 36;
            return number.ToString("00", CultureInfo.InvariantCulture);
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value)) return "UNNAMED";
            char[] chars = value.ToUpperInvariant().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                if (!char.IsLetterOrDigit(chars[i])) chars[i] = '_';
            return new string(chars).Trim('_');
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
