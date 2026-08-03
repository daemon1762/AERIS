using System;
using System.Collections.Generic;

namespace AERISFlightControl.Landing
{
    internal enum AERISApproachProcedureType
    {
        Direct = 0,
        OffsetLeft = 1,
        OffsetRight = 2,
        DoglegLeft = 3,
        DoglegRight = 4,
        SteepDirect = 5
    }

    internal enum AERISApproachProcedureState
    {
        Pending = 0,
        Available = 1,
        Conditional = 2,
        Rejected = 3
    }

    internal enum AERISApproachLegType
    {
        OuterCapture = 0,
        Offset = 1,
        Dogleg = 2,
        FinalLocalizer = 3,
        MissedApproach = 4
    }

    internal enum AERISGlideSegmentType
    {
        OuterDescent = 0,
        Transition = 1,
        StabilizedFinal = 2,
        FlareGate = 3
    }

    // Immutable-by-convention CPU snapshot.  Positive AlongTrackMeters is outward
    // from the landing threshold along the reciprocal final course.  CrossTrackMeters
    // is positive right of the inbound localizer.  It contains no Unity objects.
    internal sealed class AERISApproachObstacleSample
    {
        internal double AlongTrackMeters;
        internal double CrossTrackMeters;
        internal double TopElevationMeters;
        internal double HorizontalRadiusMeters;
        internal bool IsTerrain;
        internal string SourceId = string.Empty;

        internal AERISApproachObstacleSample Clone()
        {
            return (AERISApproachObstacleSample)MemberwiseClone();
        }
    }

    internal sealed class AERISApproachObstacleSnapshot
    {
        internal long Generation;
        internal string DirectionStableId = string.Empty;
        internal string TerrainSignature = string.Empty;
        internal string ObstacleSignature = string.Empty;
        internal bool CorridorComplete;
        internal double BodyRadiusMeters = 600000.0;
        internal bool MissedApproachClear;
        internal double MissedApproachMinimumAltitudeMeters;
        internal List<AERISApproachObstacleSample> Samples =
            new List<AERISApproachObstacleSample>();

        internal AERISApproachObstacleSnapshot Clone()
        {
            var value = (AERISApproachObstacleSnapshot)MemberwiseClone();
            value.Samples = new List<AERISApproachObstacleSample>(Samples.Count);
            for (int i = 0; i < Samples.Count; i++)
                if (Samples[i] != null) value.Samples.Add(Samples[i].Clone());
            return value;
        }
    }

    internal sealed class AERISApproachPlanningLimits
    {
        internal double MinimumGlideAngleDeg = 2.5;
        internal double PreferredGlideAngleDeg = 3.0;
        internal double NormalMaximumGlideAngleDeg = 4.0;
        internal double ObstacleMaximumGlideAngleDeg = 5.0;
        internal double ConditionalMaximumGlideAngleDeg = 6.0;
        internal double GlideAngleStepDeg = 0.25;
        internal double MinimumFinalStraightMeters = 4000.0;
        internal double MaximumCaptureDistanceMeters = 30000.0;
        internal double MinimumTerrainClearanceMeters = 90.0;
        internal double MinimumObstacleClearanceMeters = 60.0;
        internal double CorridorHalfWidthMeters = 180.0;
        internal double TransitionLengthMeters = 1000.0;
        internal double FlareGateDistanceMeters = 350.0;
        internal double MaximumDoglegTurnDeg = 35.0;
        internal double MinimumMissedApproachAltitudeMeters = 1000.0;
        internal bool AircraftSupportsSteepApproach;

        internal AERISApproachPlanningLimits Clone()
        {
            return (AERISApproachPlanningLimits)MemberwiseClone();
        }
    }

    internal sealed class AERISApproachLeg
    {
        internal AERISApproachLegType Type;
        internal string Id = string.Empty;
        internal AERISGeoPoint Start;
        internal AERISGeoPoint End;
        internal double InboundCourseDeg;
        internal double MinimumAltitudeMeters;
        internal double MaximumAltitudeMeters;
        internal double CorridorHalfWidthMeters;
        internal string ConstraintText = string.Empty;

        internal AERISApproachLeg Clone()
        {
            var value = (AERISApproachLeg)MemberwiseClone();
            value.Start = Start == null ? null : Start.Clone();
            value.End = End == null ? null : End.Clone();
            return value;
        }
    }

    internal sealed class AERISGlideProfileSegment
    {
        internal AERISGlideSegmentType Type;
        internal double StartDistanceFromThresholdMeters;
        internal double EndDistanceFromThresholdMeters;
        internal double StartPathAngleDeg;
        internal double EndPathAngleDeg;
        internal double MinimumAltitudeMeters;
        internal string ConstraintText = string.Empty;

        internal AERISGlideProfileSegment Clone()
        {
            return (AERISGlideProfileSegment)MemberwiseClone();
        }
    }

    internal sealed class AERISApproachProcedure
    {
        internal string StableId = string.Empty;
        internal string PhysicalRunwayId = string.Empty;
        internal string DirectionStableId = string.Empty;
        internal string DisplayName = string.Empty;
        internal AERISApproachProcedureType Type;
        internal AERISApproachProcedureState State;
        internal double FinalCourseDeg;
        internal double GlideAngleDeg;
        internal double ThresholdCrossingHeightMeters;
        internal double RequiredMissedApproachAltitudeMeters;
        internal string TerrainSignature = string.Empty;
        internal string ObstacleSignature = string.Empty;
        internal string FailureCode = string.Empty;
        internal string Detail = string.Empty;
        internal long RegistryGeneration;
        internal List<AERISApproachLeg> Legs = new List<AERISApproachLeg>();
        internal List<AERISGlideProfileSegment> GlideProfile =
            new List<AERISGlideProfileSegment>();

        internal bool CanDisplay
        {
            get { return State != AERISApproachProcedureState.Rejected; }
        }

        internal bool CanUseForShadowGuidance
        {
            get
            {
                return State == AERISApproachProcedureState.Available ||
                    State == AERISApproachProcedureState.Conditional;
            }
        }

        internal AERISApproachProcedure Clone()
        {
            var value = (AERISApproachProcedure)MemberwiseClone();
            value.Legs = new List<AERISApproachLeg>(Legs.Count);
            for (int i = 0; i < Legs.Count; i++)
                if (Legs[i] != null) value.Legs.Add(Legs[i].Clone());
            value.GlideProfile = new List<AERISGlideProfileSegment>(GlideProfile.Count);
            for (int i = 0; i < GlideProfile.Count; i++)
                if (GlideProfile[i] != null) value.GlideProfile.Add(GlideProfile[i].Clone());
            return value;
        }
    }
}
