using System;
using System.Collections.Generic;

namespace AERISFlightControl.Landing
{
    internal enum AERISAirfieldSource
    {
        Unknown = 0,
        Stock = 1,
        Dlc = 2,
        KerbalKonstructs = 3,
        StockLaunchsitesExpansion = 4,
        UserCfg = 5
    }

    internal enum AERISFacilityKind
    {
        Unknown = 0,
        Runway = 1,
        LaunchPad = 2,
        Helipad = 3,
        Harbour = 4
    }

    internal enum AERISAirfieldValidation
    {
        DiscoveryOnly = 0,
        FoundationValidated = 1,
        RuntimeSurveyValidated = 2,
        PrecisionValidated = 3,
        Rejected = 4
    }

    internal enum AERISRunwayCertificationState
    {
        Pending = 0,
        Certified = 1,
        Failed = 2,
        Revalidation = 3,
        // Geometry may be retained as non-selectable evidence but is never ARM eligible.
        Provisional = 4
    }

    internal enum AERISRunwayCertificationBasis
    {
        Unknown = 0,
        PlanWitness = 1,
        AnchorSurfaceScan = 2,
        ProvisionalGeometry = 3,
        UserCalibrated = 4,
        WitnessConflict = 5
    }

    internal enum AERISRunwayFailureCode
    {
        None = 0,
        NotFixedWingRunway,
        FacilityCategoryConflict,
        ModelUnavailable,
        MeshUnreadable,
        ColliderUnavailable,
        NoGeometryEvidence,
        InsufficientEvidence,
        MultipleGeometrySolutions,
        WholeSiteBoundsOnly,
        CenterlineConflict,
        ThresholdUnresolved,
        DisplacedThresholdUnresolved,
        RunwayWidthUnresolved,
        SurfaceDiscontinuity,
        SurfaceSlopeExceeded,
        RunwayTooShort,
        RunwayTooNarrow,
        ApproachTerrainBlocked,
        ApproachObstacleBlocked,
        ReciprocalMismatch,
        MeasurementDisagreement,
        ProviderDataError,
        AbsolutePlacementInvalid,
        ModelChanged,
        PositionChanged,
        RotationOrScaleChanged,
        MeshFingerprintChanged,
        ProviderVersionChanged,
        SurveyTimeout,
        UnsupportedLayout,
        PlanWitnessConflict,
        AnchorSurfaceUnresolved,
        UserCalibrationInvalid,
        WorkerFailure,
        UserCalibrationRequired,
        ObservedPlacementMismatch
    }

    [Flags]
    internal enum AERISRunwayEvidenceFamily
    {
        None = 0,
        MetadataSemantic = 1 << 0,
        GeometryTopology = 1 << 1,
        AviationMarkingLighting = 1 << 2,
        OperationalLayout = 1 << 3,
        SurfaceElevationTerrain = 1 << 4,
        ExternalRunwayWitness = 1 << 5,
        UserCalibration = 1 << 6
    }

    [Flags]
    internal enum AERISRunwayMeasurementMethod : long
    {
        None = 0,
        M01Metadata = 1L << 0,
        M02RendererBounds = 1L << 1,
        M03MeshPca = 1L << 2,
        M04Collider = 1L << 3,
        M05SubMeshMaterial = 1L << 4,
        M06ParallelEdges = 1L << 5,
        M07LongSurfaceStrip = 1L << 6,
        M08SurfaceFlatness = 1L << 7,
        M09LongitudinalProfile = 1L << 8,
        M10CenterlineGeometry = 1L << 9,
        M11ThresholdMarking = 1L << 10,
        M12RunwayNumber = 1L << 11,
        M13RunwayLights = 1L << 12,
        M14RepeatedPavement = 1L << 13,
        M15SpawnHeading = 1L << 14,
        M16TaxiwayApronTopology = 1L << 15,
        M17PlatformExclusion = 1L << 16,
        M18PqsArtificialSurface = 1L << 17,
        M19BilateralSymmetry = 1L << 18,
        M20ReciprocalConsistency = 1L << 19,
        M21NameModelPrior = 1L << 20,
        M22LodConsistency = 1L << 21,
        M23RobustLineFit = 1L << 22,
        M24CrossSectionVoting = 1L << 23,
        M25MultiScale = 1L << 24,
        M26TemplateFit = 1L << 25,
        M27PlanWitness = 1L << 26,
        M28AnchorSurfaceScan = 1L << 27,
        M29UserCalibration = 1L << 28
    }

    internal enum AERISRunwaySurveyMethod
    {
        ManualRequired = 0,
        StaticBounds = 1,
        PairedThresholds = 2,
        ConsensusAutomatic = 3
    }

    internal enum AERISAirfieldReloadState
    {
        Idle = 0,
        LoadingCache,
        Discovering,
        Surveying,
        Validating,
        Staged,
        Complete,
        Failed
    }

    internal sealed class AERISRunwaySurveyDefinition
    {
        internal string Id = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderSiteId = string.Empty;
        internal string ProviderGroup = string.Empty;
        internal string SourcePathContains = string.Empty;
        internal string ModelName = string.Empty;
        internal AERISRunwaySurveyMethod Method = AERISRunwaySurveyMethod.ManualRequired;
        internal string PairKey = string.Empty;
        internal double MinimumLengthMeters = 250.0;
        internal double MaximumLengthMeters = 10000.0;
        internal double MinimumWidthMeters = 8.0;
        internal double MaximumWidthMeters = 500.0;
        internal double MinimumAspectRatio = 4.0;
        internal double DefaultWidthMeters = 45.0;
        internal string Surface = "PAVED";
        internal string SourceMod = string.Empty;
        internal string ProviderVersion = string.Empty;
        internal string Notes = string.Empty;

        internal bool Matches(string providerUuid, string providerSiteId, string providerGroup,
            string sourcePath, string modelName)
        {
            if (!string.IsNullOrEmpty(ProviderUuid) &&
                !string.Equals(ProviderUuid, providerUuid, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(ProviderSiteId) &&
                !string.Equals(ProviderSiteId, providerSiteId, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(ProviderGroup) &&
                !string.Equals(ProviderGroup, providerGroup, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.IsNullOrEmpty(SourcePathContains) &&
                (sourcePath ?? string.Empty).IndexOf(SourcePathContains,
                    StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (!string.IsNullOrEmpty(ModelName) &&
                !string.Equals(ModelName, modelName, StringComparison.OrdinalIgnoreCase)) return false;
            return !string.IsNullOrEmpty(ProviderUuid) || !string.IsNullOrEmpty(ProviderSiteId);
        }
    }

    internal sealed class AERISGeoPoint
    {
        internal double LatitudeDeg;
        internal double LongitudeDeg;
        internal double ElevationMeters;

        internal bool IsFinite
        {
            get
            {
                return Finite(LatitudeDeg) && Finite(LongitudeDeg) && Finite(ElevationMeters) &&
                    LatitudeDeg >= -90.0 && LatitudeDeg <= 90.0 &&
                    LongitudeDeg >= -180.0 && LongitudeDeg <= 180.0;
            }
        }

        internal AERISGeoPoint Clone()
        {
            return new AERISGeoPoint
            {
                LatitudeDeg = LatitudeDeg,
                LongitudeDeg = LongitudeDeg,
                ElevationMeters = ElevationMeters
            };
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class AERISRunwayParameterEstimate
    {
        internal string Name = string.Empty;
        internal string Units = string.Empty;
        internal double Value;
        internal double Uncertainty;
        internal double Confidence;
        internal AERISRunwayEvidenceFamily EvidenceFamilies;
        internal AERISRunwayMeasurementMethod Methods;
        internal string Detail = string.Empty;

        internal AERISRunwayParameterEstimate Clone()
        {
            return (AERISRunwayParameterEstimate)MemberwiseClone();
        }
    }

    internal sealed class AERISRunwayDirectionDefinition
    {
        internal string Id = string.Empty;
        internal string DisplayName = string.Empty;

        // Existing consumers use Threshold/OppositeThreshold.  In v0.16.4 these are
        // explicitly the direction-specific operational threshold and rollout-side
        // usable end; physical and usable pavement ends are retained separately.
        internal AERISGeoPoint Threshold = new AERISGeoPoint();
        internal AERISGeoPoint OppositeThreshold = new AERISGeoPoint();
        internal AERISGeoPoint PhysicalStart;
        internal AERISGeoPoint PhysicalEnd;
        internal AERISGeoPoint UsableStart;
        internal AERISGeoPoint UsableEnd;
        internal AERISGeoPoint GlideSlopeAnchor;
        internal AERISGeoPoint TouchdownAim;
        internal AERISGeoPoint RolloutEnd;
        internal double HeadingDeg;
        internal double GlidePathAngleDeg = 3.0;
        internal double ThresholdCrossingHeightMeters = 15.0;
        internal double LocalizerCaptureAngleDeg = 25.0;
        internal double LocalizerCaptureDistanceMeters = 30000.0;
        internal double GlidePathCaptureDistanceMeters = 20000.0;
        internal double MissedApproachHeadingDeg;
        internal double MissedApproachSafeAltitudeMeters = 1000.0;
        internal string StableId = string.Empty;

        internal AERISRunwayCertificationState CertificationState =
            AERISRunwayCertificationState.Pending;
        internal AERISRunwayFailureCode FailureCode = AERISRunwayFailureCode.None;
        internal string FailureDetail = string.Empty;
        internal string PendingDetail = "DISCOVERED";
        internal double ClassificationConfidence;
        internal double GeometryConfidence;
        internal double CenterlineUncertaintyMeters;
        internal double HeadingUncertaintyDeg;
        internal double PhysicalEndUncertaintyMeters;
        internal double UsableEndUncertaintyMeters;
        internal double ThresholdUncertaintyMeters;
        internal double LengthUncertaintyMeters;
        internal double WidthUncertaintyMeters;
        internal double ElevationUncertaintyMeters;
        internal double DisplacedThresholdConfidence;
        internal double ApproachCorridorConfidence;
        internal AERISRunwayEvidenceFamily EvidenceFamilies;
        internal AERISRunwayMeasurementMethod MeasurementMethods;
        internal string GeometryFingerprint = string.Empty;
        internal long GeometryRevision;
        internal string CertifiedUtc = string.Empty;
        internal bool GeometryDirectionAutoCorrected;
        internal string GeometryDirectionDetail = string.Empty;
        internal AERISRunwayCertificationBasis CertificationBasis =
            AERISRunwayCertificationBasis.Unknown;
        internal string CertificationBasisDetail = string.Empty;
        internal List<AERISRunwayParameterEstimate> ParameterEstimates =
            new List<AERISRunwayParameterEstimate>();

        internal bool IsCertified
        {
            get { return CertificationState == AERISRunwayCertificationState.Certified; }
        }

        internal bool HasFiniteGeometry
        {
            get
            {
                return Threshold != null && OppositeThreshold != null &&
                    Threshold.IsFinite && OppositeThreshold.IsFinite &&
                    !double.IsNaN(HeadingDeg) && !double.IsInfinity(HeadingDeg);
            }
        }

        internal double ThresholdBearingDeg
        {
            get
            {
                return HasFiniteGeometry ?
                    AERISAirfieldConfigParser.InitialBearingDeg(Threshold, OppositeThreshold) :
                    double.NaN;
            }
        }

        internal double HeadingGeometryErrorDeg
        {
            get
            {
                double bearing = ThresholdBearingDeg;
                if (double.IsNaN(bearing) || double.IsInfinity(bearing))
                    return double.PositiveInfinity;
                double delta = AERISAirfieldConfigParser.NormalizeHeading(HeadingDeg) - bearing;
                delta %= 360.0;
                if (delta > 180.0) delta -= 360.0;
                if (delta < -180.0) delta += 360.0;
                return Math.Abs(delta);
            }
        }

        internal bool HeadingMatchesGeometry
        {
            get { return HasFiniteGeometry && HeadingGeometryErrorDeg <= 10.0; }
        }

        internal bool HasCertifiedGeometry
        {
            get
            {
                return IsCertified && HasFiniteGeometry && HeadingMatchesGeometry &&
                    GeometryConfidence >= 0.85 && ClassificationConfidence >= 0.90 &&
                    PhysicalStart != null && PhysicalStart.IsFinite &&
                    PhysicalEnd != null && PhysicalEnd.IsFinite &&
                    UsableStart != null && UsableStart.IsFinite &&
                    UsableEnd != null && UsableEnd.IsFinite &&
                    GlideSlopeAnchor != null && GlideSlopeAnchor.IsFinite &&
                    TouchdownAim != null && TouchdownAim.IsFinite &&
                    RolloutEnd != null && RolloutEnd.IsFinite;
            }
        }

        internal void PopulateOperationalReferences(double touchdownOffsetMeters)
        {
            if (!HasFiniteGeometry) return;
            if (PhysicalStart == null || !PhysicalStart.IsFinite) PhysicalStart = Threshold.Clone();
            if (PhysicalEnd == null || !PhysicalEnd.IsFinite) PhysicalEnd = OppositeThreshold.Clone();
            if (UsableStart == null || !UsableStart.IsFinite) UsableStart = Threshold.Clone();
            if (UsableEnd == null || !UsableEnd.IsFinite) UsableEnd = OppositeThreshold.Clone();
            if (RolloutEnd == null || !RolloutEnd.IsFinite) RolloutEnd = UsableEnd.Clone();
            GlideSlopeAnchor = Threshold.Clone();
            GlideSlopeAnchor.ElevationMeters += Math.Max(0.0, ThresholdCrossingHeightMeters);
            if (TouchdownAim == null || !TouchdownAim.IsFinite)
                TouchdownAim = AERISAirfieldConfigParser.InterpolateGeo(Threshold,
                    OppositeThreshold, Math.Max(0.0, touchdownOffsetMeters));
        }

        internal AERISRunwayDirectionDefinition Clone()
        {
            var value = (AERISRunwayDirectionDefinition)MemberwiseClone();
            value.Threshold = ClonePoint(Threshold);
            value.OppositeThreshold = ClonePoint(OppositeThreshold);
            value.PhysicalStart = ClonePoint(PhysicalStart);
            value.PhysicalEnd = ClonePoint(PhysicalEnd);
            value.UsableStart = ClonePoint(UsableStart);
            value.UsableEnd = ClonePoint(UsableEnd);
            value.GlideSlopeAnchor = ClonePoint(GlideSlopeAnchor);
            value.TouchdownAim = ClonePoint(TouchdownAim);
            value.RolloutEnd = ClonePoint(RolloutEnd);
            value.ParameterEstimates = new List<AERISRunwayParameterEstimate>();
            for (int i = 0; i < ParameterEstimates.Count; i++)
                value.ParameterEstimates.Add(ParameterEstimates[i].Clone());
            return value;
        }

        static AERISGeoPoint ClonePoint(AERISGeoPoint point)
        {
            return point == null ? null : point.Clone();
        }
    }

    internal sealed class AERISRunwayDefinition
    {
        internal string Id = string.Empty;
        internal string DisplayName = string.Empty;
        internal string ProviderSiteId = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string StableId = string.Empty;
        internal double LengthMeters;
        internal double WidthMeters;
        internal string Surface = "UNKNOWN";
        internal string GeometryFingerprint = string.Empty;
        internal long GeometryRevision;
        internal List<AERISGeoPoint> UsablePolygon = new List<AERISGeoPoint>();
        internal List<double> WidthProfileMeters = new List<double>();
        internal List<AERISRunwayDirectionDefinition> Directions =
            new List<AERISRunwayDirectionDefinition>();

        internal bool HasGeometry { get { return Directions.Count > 0; } }
        internal int CertifiedDirectionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Directions.Count; i++) if (Directions[i].HasCertifiedGeometry) count++;
                return count;
            }
        }

        internal AERISRunwayDefinition Clone()
        {
            var value = (AERISRunwayDefinition)MemberwiseClone();
            value.UsablePolygon = new List<AERISGeoPoint>();
            value.WidthProfileMeters = new List<double>();
            value.Directions = new List<AERISRunwayDirectionDefinition>();
            for (int i = 0; i < UsablePolygon.Count; i++) value.UsablePolygon.Add(UsablePolygon[i].Clone());
            for (int i = 0; i < WidthProfileMeters.Count; i++) value.WidthProfileMeters.Add(WidthProfileMeters[i]);
            for (int i = 0; i < Directions.Count; i++) value.Directions.Add(Directions[i].Clone());
            return value;
        }
    }

    internal sealed class AERISAirfieldDefinition
    {
        internal string Id = string.Empty;
        internal string Body = "Kerbin";
        internal string DisplayName = "UNNAMED";
        internal string Description = string.Empty;
        internal AERISAirfieldSource Source = AERISAirfieldSource.Unknown;
        internal AERISFacilityKind FacilityKind = AERISFacilityKind.Unknown;
        internal AERISAirfieldValidation Validation = AERISAirfieldValidation.DiscoveryOnly;
        internal string ProviderSiteId = string.Empty;
        internal string ProviderGroup = string.Empty;
        internal string ProviderUuid = string.Empty;
        internal string ProviderStableRecordId = string.Empty;
        internal string SourceMod = string.Empty;
        internal string ProviderVersion = string.Empty;
        internal string DefinitionVersion = "1";
        internal string SourcePath = string.Empty;
        internal bool ProviderDetected;
        internal string ProviderRuntimeStatus = "CONFIG ONLY";
        internal string ProviderRevision = string.Empty;
        internal double ReferenceLatitudeDeg;
        internal double ReferenceLongitudeDeg;
        internal double ReferenceElevationMeters;
        internal List<AERISRunwayDefinition> Runways = new List<AERISRunwayDefinition>();

        internal string StableId
        {
            get { return (Body ?? string.Empty) + "\n" + (Id ?? string.Empty); }
        }

        internal int DirectionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Runways.Count; i++) count += Runways[i].Directions.Count;
                return count;
            }
        }

        internal int CertifiedDirectionCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Runways.Count; i++) count += Runways[i].CertifiedDirectionCount;
                return count;
            }
        }

        internal bool CanArmFoundation
        {
            get
            {
                return FacilityKind == AERISFacilityKind.Runway &&
                    Validation != AERISAirfieldValidation.Rejected && CertifiedDirectionCount > 0;
            }
        }

        internal AERISRunwayDirectionDefinition DirectionAt(int index)
        {
            if (index < 0) return null;
            int cursor = 0;
            for (int i = 0; i < Runways.Count; i++)
            {
                for (int j = 0; j < Runways[i].Directions.Count; j++)
                {
                    if (cursor == index) return Runways[i].Directions[j];
                    cursor++;
                }
            }
            return null;
        }

        internal AERISRunwayDirectionDefinition CertifiedDirectionAt(int index)
        {
            if (index < 0) return null;
            int cursor = 0;
            for (int i = 0; i < Runways.Count; i++)
            {
                for (int j = 0; j < Runways[i].Directions.Count; j++)
                {
                    AERISRunwayDirectionDefinition direction = Runways[i].Directions[j];
                    if (!direction.HasCertifiedGeometry) continue;
                    if (cursor == index) return direction;
                    cursor++;
                }
            }
            return null;
        }

        internal AERISRunwayDefinition RunwayForDirection(AERISRunwayDirectionDefinition direction)
        {
            if (direction == null) return null;
            for (int i = 0; i < Runways.Count; i++)
                if (Runways[i].Directions.Contains(direction)) return Runways[i];
            return null;
        }

        internal AERISAirfieldDefinition Clone()
        {
            var value = (AERISAirfieldDefinition)MemberwiseClone();
            value.Runways = new List<AERISRunwayDefinition>();
            for (int i = 0; i < Runways.Count; i++) value.Runways.Add(Runways[i].Clone());
            return value;
        }
    }

    internal sealed class AERISRunwayObservation
    {
        internal bool Valid;
        internal string Status = "NO OBSERVATION";
        internal double DistanceToThresholdMeters;
        internal double BearingToThresholdDeg;
        internal double ApproachDistanceMeters;
        internal double AlongRunwayMeters;
        internal double CrossTrackMeters;
        internal double InterceptAngleDeg;
        internal double GlidePathTargetAltitudeMeters;
        internal double GlidePathErrorMeters;
        internal double ThresholdEastMeters;
        internal double ThresholdNorthMeters;
        internal double OppositeEastMeters;
        internal double OppositeNorthMeters;
        internal double VesselAltitudeAslMeters;
        internal double VesselHeadingDeg;
        internal bool OnApproachSide;
        internal bool RunwayGeometryDirectionValid;
        internal bool LocalizerGeometryEligible;
        internal bool GlidePathGeometryEligible;
        internal string InhibitReason = "LAND ARM REQUIRED";
    }
}
