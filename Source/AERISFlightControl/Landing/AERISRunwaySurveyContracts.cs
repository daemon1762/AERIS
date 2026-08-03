using System;

namespace AERISFlightControl.Landing
{
    [Flags]
    internal enum AERISSurveySemantic
    {
        None = 0,
        Runway = 1 << 0,
        Centerline = 1 << 1,
        Threshold = 1 << 2,
        RunwayNumber = 1 << 3,
        EdgeLight = 1 << 4,
        ApproachLight = 1 << 5,
        Taxiway = 1 << 6,
        Apron = 1 << 7,
        Platform = 1 << 8,
        Spawn = 1 << 9,
        Pavement = 1 << 10,
        NaturalSurface = 1 << 11,
        BlastPad = 1 << 12,
        Stopway = 1 << 13,
        Obstacle = 1 << 14,
        Lod = 1 << 15
    }

    internal struct AERISSurveyPoint
    {
        internal readonly double East;
        internal readonly double North;
        internal readonly double Up;
        internal readonly double Weight;
        internal readonly AERISSurveySemantic Semantic;
        internal readonly AERISRunwayMeasurementMethod Method;

        internal AERISSurveyPoint(double east, double north, double up, double weight,
            AERISSurveySemantic semantic, AERISRunwayMeasurementMethod method)
        {
            East = east;
            North = north;
            Up = up;
            Weight = weight;
            Semantic = semantic;
            Method = method;
        }
    }

    internal struct AERISSurveyPrimitive
    {
        internal readonly double CenterEast;
        internal readonly double CenterNorth;
        internal readonly double CenterUp;
        internal readonly double AxisEast;
        internal readonly double AxisNorth;
        internal readonly double LengthMeters;
        internal readonly double WidthMeters;
        internal readonly double HeightMeters;
        internal readonly double FlatnessDeg;
        internal readonly AERISSurveySemantic Semantic;
        internal readonly AERISRunwayEvidenceFamily EvidenceFamily;
        internal readonly AERISRunwayMeasurementMethod Method;
        internal readonly int SourceGroup;

        internal AERISSurveyPrimitive(double centerEast, double centerNorth, double centerUp,
            double axisEast, double axisNorth, double lengthMeters, double widthMeters,
            double heightMeters, double flatnessDeg, AERISSurveySemantic semantic,
            AERISRunwayEvidenceFamily evidenceFamily, AERISRunwayMeasurementMethod method,
            int sourceGroup)
        {
            CenterEast = centerEast;
            CenterNorth = centerNorth;
            CenterUp = centerUp;
            double magnitude = Math.Sqrt(axisEast * axisEast + axisNorth * axisNorth);
            AxisEast = magnitude > 1e-9 ? axisEast / magnitude : 0.0;
            AxisNorth = magnitude > 1e-9 ? axisNorth / magnitude : 1.0;
            LengthMeters = lengthMeters;
            WidthMeters = widthMeters;
            HeightMeters = heightMeters;
            FlatnessDeg = flatnessDeg;
            Semantic = semantic;
            EvidenceFamily = evidenceFamily;
            Method = method;
            SourceGroup = sourceGroup;
        }
    }

    internal sealed class AERISRunwaySurveySnapshot
    {
        internal const int CurrentAlgorithmVersion = 1710;
        internal const int CurrentRunwayDetectorRevision = 5;
        internal const int CurrentAbsolutePlacementRevision = 2;
        internal const int CurrentAxisRegistrationRevision = 2;
        internal const int CurrentModAirfieldRecoveryRevision = 1;
        internal readonly long Generation;
        internal readonly long Sequence;
        internal readonly string StableRecordId;
        internal readonly string ProviderUuid;
        internal readonly string ProviderSiteId;
        internal readonly string ProviderGroup;
        internal readonly string ProviderCategory;
        internal readonly string ProviderVersion;
        internal readonly string SourcePath;
        internal readonly string ModelName;
        internal readonly string Body;
        internal readonly double BodyRadiusMeters;
        internal readonly double ReferenceLatitudeDeg;
        internal readonly double ReferenceLongitudeDeg;
        internal readonly double ReferenceElevationMeters;
        internal readonly double DeclaredLengthMeters;
        internal readonly double DeclaredWidthMeters;
        internal readonly double DeclaredHeadingDeg;
        internal readonly bool AbsolutePlacementRequired;
        internal readonly bool AbsolutePlacementConstraintAvailable;
        internal readonly double LaunchAnchorEastMeters;
        internal readonly double LaunchAnchorNorthMeters;
        internal readonly double LaunchAnchorUpMeters;
        internal readonly double LaunchAnchorHeadingDeg;
        internal readonly bool ProviderReferenceOriginUsed;
        internal readonly double ProviderReferenceToLaunchMeters;
        internal readonly double MinimumLengthMeters;
        internal readonly double MaximumLengthMeters;
        internal readonly double MinimumWidthMeters;
        internal readonly double MaximumWidthMeters;
        internal readonly double MinimumAspectRatio;
        internal readonly string Surface;
        internal readonly string SourceMod;
        internal readonly AERISRunwaySurveyMethod SurveyMethod;
        internal readonly bool ProviderExplicitRunway;
        internal readonly bool GeometryReadable;
        internal readonly bool ColliderReadable;
        internal readonly bool PqsSampled;
        internal readonly double PqsReferenceElevationMeters;
        internal readonly bool ApproachAAvailable;
        internal readonly bool ApproachBAvailable;
        internal readonly AERISSurveyPoint[] Points;
        internal readonly AERISSurveyPrimitive[] Primitives;
        internal readonly bool RunwayWitnessAvailable;
        internal readonly bool RunwayWitnessUserCalibrated;
        internal readonly bool RunwayUserCalibrationPresent;
        internal readonly bool RunwayUserCalibrationPending;
        internal readonly bool RunwayPlacementMismatchObserved;
        internal readonly string RunwayPlacementObservationDetail;
        internal readonly string RunwayWitnessSource;
        internal readonly string RunwayWitnessName;
        internal readonly string RunwayWitnessSourcePath;
        internal readonly double RunwayWitnessStartEastMeters;
        internal readonly double RunwayWitnessStartNorthMeters;
        internal readonly double RunwayWitnessStartUpMeters;
        internal readonly double RunwayWitnessEndEastMeters;
        internal readonly double RunwayWitnessEndNorthMeters;
        internal readonly double RunwayWitnessEndUpMeters;
        internal readonly double RunwayWitnessHeadingDeg;
        internal readonly double RunwayWitnessLengthMeters;
        internal readonly double RunwayWitnessMatchDistanceMeters;
        internal readonly double RunwayWitnessConfidence;
        internal readonly string RunwayWitnessFingerprint;
        internal readonly string SourceFingerprint;
        internal readonly string InputFingerprint;

        internal AERISRunwaySurveySnapshot(long generation, long sequence,
            string stableRecordId, string providerUuid, string providerSiteId,
            string providerGroup, string providerCategory, string providerVersion,
            string sourcePath, string modelName, string body, double bodyRadiusMeters,
            double referenceLatitudeDeg, double referenceLongitudeDeg,
            double referenceElevationMeters, double declaredLengthMeters,
            double declaredWidthMeters, double declaredHeadingDeg,
            bool absolutePlacementRequired, bool absolutePlacementConstraintAvailable,
            double launchAnchorEastMeters,
            double launchAnchorNorthMeters, double launchAnchorUpMeters,
            double launchAnchorHeadingDeg, bool providerReferenceOriginUsed,
            double providerReferenceToLaunchMeters,
            double minimumLengthMeters, double maximumLengthMeters,
            double minimumWidthMeters, double maximumWidthMeters,
            double minimumAspectRatio, string surface, string sourceMod,
            AERISRunwaySurveyMethod surveyMethod, bool providerExplicitRunway,
            bool geometryReadable, bool colliderReadable,
            bool pqsSampled, double pqsReferenceElevationMeters,
            bool approachAAvailable, bool approachBAvailable,
            AERISSurveyPoint[] points, AERISSurveyPrimitive[] primitives,
            bool runwayWitnessAvailable, bool runwayWitnessUserCalibrated,
            bool runwayUserCalibrationPresent, bool runwayUserCalibrationPending,
            bool runwayPlacementMismatchObserved,
            string runwayPlacementObservationDetail,
            string runwayWitnessSource, string runwayWitnessName,
            string runwayWitnessSourcePath,
            double runwayWitnessStartEastMeters, double runwayWitnessStartNorthMeters,
            double runwayWitnessStartUpMeters, double runwayWitnessEndEastMeters,
            double runwayWitnessEndNorthMeters, double runwayWitnessEndUpMeters,
            double runwayWitnessHeadingDeg, double runwayWitnessLengthMeters,
            double runwayWitnessMatchDistanceMeters, double runwayWitnessConfidence,
            string runwayWitnessFingerprint,
            string sourceFingerprint, string inputFingerprint)
        {
            Generation = generation;
            Sequence = sequence;
            StableRecordId = stableRecordId ?? string.Empty;
            ProviderUuid = providerUuid ?? string.Empty;
            ProviderSiteId = providerSiteId ?? string.Empty;
            ProviderGroup = providerGroup ?? string.Empty;
            ProviderCategory = providerCategory ?? string.Empty;
            ProviderVersion = providerVersion ?? string.Empty;
            SourcePath = sourcePath ?? string.Empty;
            ModelName = modelName ?? string.Empty;
            Body = body ?? string.Empty;
            BodyRadiusMeters = bodyRadiusMeters;
            ReferenceLatitudeDeg = referenceLatitudeDeg;
            ReferenceLongitudeDeg = referenceLongitudeDeg;
            ReferenceElevationMeters = referenceElevationMeters;
            DeclaredLengthMeters = declaredLengthMeters;
            DeclaredWidthMeters = declaredWidthMeters;
            DeclaredHeadingDeg = declaredHeadingDeg;
            AbsolutePlacementRequired = absolutePlacementRequired;
            AbsolutePlacementConstraintAvailable = absolutePlacementConstraintAvailable;
            LaunchAnchorEastMeters = launchAnchorEastMeters;
            LaunchAnchorNorthMeters = launchAnchorNorthMeters;
            LaunchAnchorUpMeters = launchAnchorUpMeters;
            LaunchAnchorHeadingDeg = launchAnchorHeadingDeg;
            ProviderReferenceOriginUsed = providerReferenceOriginUsed;
            ProviderReferenceToLaunchMeters = providerReferenceToLaunchMeters;
            MinimumLengthMeters = minimumLengthMeters;
            MaximumLengthMeters = maximumLengthMeters;
            MinimumWidthMeters = minimumWidthMeters;
            MaximumWidthMeters = maximumWidthMeters;
            MinimumAspectRatio = minimumAspectRatio;
            Surface = surface ?? string.Empty;
            SourceMod = sourceMod ?? string.Empty;
            SurveyMethod = surveyMethod;
            ProviderExplicitRunway = providerExplicitRunway;
            GeometryReadable = geometryReadable;
            ColliderReadable = colliderReadable;
            PqsSampled = pqsSampled;
            PqsReferenceElevationMeters = pqsReferenceElevationMeters;
            ApproachAAvailable = approachAAvailable;
            ApproachBAvailable = approachBAvailable;
            Points = points == null ? new AERISSurveyPoint[0] :
                (AERISSurveyPoint[])points.Clone();
            Primitives = primitives == null ? new AERISSurveyPrimitive[0] :
                (AERISSurveyPrimitive[])primitives.Clone();
            RunwayWitnessAvailable = runwayWitnessAvailable;
            RunwayWitnessUserCalibrated = runwayWitnessUserCalibrated;
            RunwayUserCalibrationPresent = runwayUserCalibrationPresent;
            RunwayUserCalibrationPending = runwayUserCalibrationPending;
            RunwayPlacementMismatchObserved = runwayPlacementMismatchObserved;
            RunwayPlacementObservationDetail = runwayPlacementObservationDetail ??
                string.Empty;
            RunwayWitnessSource = runwayWitnessSource ?? string.Empty;
            RunwayWitnessName = runwayWitnessName ?? string.Empty;
            RunwayWitnessSourcePath = runwayWitnessSourcePath ?? string.Empty;
            RunwayWitnessStartEastMeters = runwayWitnessStartEastMeters;
            RunwayWitnessStartNorthMeters = runwayWitnessStartNorthMeters;
            RunwayWitnessStartUpMeters = runwayWitnessStartUpMeters;
            RunwayWitnessEndEastMeters = runwayWitnessEndEastMeters;
            RunwayWitnessEndNorthMeters = runwayWitnessEndNorthMeters;
            RunwayWitnessEndUpMeters = runwayWitnessEndUpMeters;
            RunwayWitnessHeadingDeg = runwayWitnessHeadingDeg;
            RunwayWitnessLengthMeters = runwayWitnessLengthMeters;
            RunwayWitnessMatchDistanceMeters = runwayWitnessMatchDistanceMeters;
            RunwayWitnessConfidence = runwayWitnessConfidence;
            RunwayWitnessFingerprint = runwayWitnessFingerprint ?? string.Empty;
            SourceFingerprint = sourceFingerprint ?? string.Empty;
            InputFingerprint = inputFingerprint ?? string.Empty;
        }
    }

    internal sealed class AERISRunwayAxisCandidate
    {
        internal double CenterEast;
        internal double CenterNorth;
        internal double CenterUp;
        internal double AxisEast;
        internal double AxisNorth;
        internal double PhysicalStartMeters;
        internal double PhysicalEndMeters;
        internal double UsableStartMeters;
        internal double UsableEndMeters;
        internal double OperationalThresholdA;
        internal double OperationalThresholdB;
        internal double WidthMeters;
        internal double LengthMeters;
        internal double HeadingDeg;
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
        internal bool AbsolutePlacementValid;
        internal bool LaunchConstraintApplied;
        internal double LaunchCrossTrackBeforeMeters;
        internal double LaunchCrossTrackAfterMeters;
        internal double LaunchAlongTrackMeters;
        internal double LaunchHeadingErrorDeg;
        internal double AbsoluteTranslationMeters;
        internal bool AxisRegistrationValid;
        internal double MeshSurfaceHeadingDeg;
        internal double RegisteredHeadingBeforeDeg;
        internal double RegisteredHeadingAfterDeg;
        internal double HeadingCorrectionDeg;
        internal double AxisReferenceErrorDeg;
        internal double AxisSurfaceAspectRatio;
        internal int AxisSurfacePointCount;
        internal string AxisRegistrationDetail = string.Empty;
        internal string AbsolutePlacementDetail = string.Empty;
        internal AERISRunwayCertificationBasis CertificationBasis =
            AERISRunwayCertificationBasis.Unknown;
        internal string CertificationBasisDetail = string.Empty;
        internal bool AnchorScanValid;
        internal int AnchorConnectedPrimitiveCount;
        internal int AnchorCrossSectionCount;
        internal double AnchorStableCrossSectionRatio;
        internal double AnchorWidthMedianMeters;
        internal double AnchorWidthSpreadMeters;
        internal string AnchorScanDetail = string.Empty;
        internal bool PlanWitnessCompared;
        internal bool PlanWitnessMatched;
        internal double PlanWitnessCenterErrorMeters;
        internal double PlanWitnessHeadingErrorDeg;
        internal double PlanWitnessLengthRatio;
        internal string PlanWitnessDetail = string.Empty;
        internal AERISRunwayEvidenceFamily EvidenceFamilies;
        internal AERISRunwayMeasurementMethod Methods;
        internal bool ApproachAAvailable;
        internal bool ApproachBAvailable;
        internal string Detail = string.Empty;
    }

    internal sealed class AERISRunwaySurveyResult
    {
        internal long Generation;
        internal long Sequence;
        internal string StableRecordId = string.Empty;
        internal string InputFingerprint = string.Empty;
        internal int AlgorithmVersion = AERISRunwaySurveySnapshot.CurrentAlgorithmVersion;
        internal AERISRunwayCertificationState State = AERISRunwayCertificationState.Pending;
        internal AERISRunwayFailureCode FailureCode = AERISRunwayFailureCode.None;
        internal string Detail = string.Empty;
        internal AERISRunwayAxisCandidate[] Runways = new AERISRunwayAxisCandidate[0];
        internal AERISRunwayMeasurementMethod PlannedMethods;
        internal AERISRunwayMeasurementMethod ExecutedMethods;
        internal long ElapsedTicks;
        internal bool WorkerException;
    }

    internal sealed class AERISRunwaySurveyJob
    {
        internal readonly long Generation;
        internal readonly long Sequence;
        internal readonly AERISRunwaySurveySnapshot Snapshot;

        internal AERISRunwaySurveyJob(long generation, long sequence,
            AERISRunwaySurveySnapshot snapshot)
        {
            Generation = generation;
            Sequence = sequence;
            Snapshot = snapshot;
        }
    }
}
