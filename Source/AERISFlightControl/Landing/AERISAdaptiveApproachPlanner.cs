using System;
using System.Collections.Generic;
using System.Globalization;

namespace AERISFlightControl.Landing
{
    // Pure CPU planning foundation.  It produces display/diagnostic procedures only;
    // it never owns FlightCtrlState, AP directors or LAND control authority.
    internal static class AERISAdaptiveApproachPlanner
    {
        const double KerbinFallbackRadiusMeters = 600000.0;

        internal static IList<AERISApproachProcedure> BuildCandidates(
            string physicalRunwayId, AERISRunwayDefinition runway,
            AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles,
            AERISApproachPlanningLimits limits, long registryGeneration)
        {
            var result = new List<AERISApproachProcedure>();
            if (limits == null) limits = new AERISApproachPlanningLimits();
            if (direction == null || runway == null || !direction.HasCertifiedGeometry)
            {
                result.Add(BuildRejected(physicalRunwayId, direction,
                    registryGeneration, "RUNWAY_NOT_CERTIFIED",
                    "Certified runway-direction geometry is required."));
                return result.AsReadOnly();
            }
            if (obstacles == null || !obstacles.CorridorComplete)
            {
                result.Add(BuildPending(physicalRunwayId, direction,
                    registryGeneration, "Terrain/obstacle corridor snapshot is pending."));
                return result.AsReadOnly();
            }
            string validation;
            if (!ValidateInputs(runway, direction, obstacles, out validation))
            {
                result.Add(BuildRejected(physicalRunwayId, direction,
                    registryGeneration, "INPUT_MISMATCH", validation));
                return result.AsReadOnly();
            }
            if (!obstacles.MissedApproachClear)
            {
                result.Add(BuildRejected(physicalRunwayId, direction,
                    registryGeneration, "MISSED_APPROACH_BLOCKED",
                    "No obstacle-cleared missed-approach corridor is available."));
                return result.AsReadOnly();
            }

            List<double> angles = OrderedAngles(limits);
            AERISApproachProcedure direct = null;
            for (int i = 0; i < angles.Count; i++)
            {
                double angle = angles[i];
                if (!AnglePermitted(angle, limits)) continue;
                string reason;
                if (!FinalCorridorClear(direction, obstacles, limits, angle,
                    limits.MaximumCaptureDistanceMeters, out reason)) continue;
                direct = BuildDirect(physicalRunwayId, runway, direction,
                    obstacles, limits, angle, registryGeneration);
                result.Add(direct);
                break;
            }

            // A lateral modification may only bypass obstacles outside the mandatory
            // straight final.  The localizer itself always remains the runway centerline.
            AERISApproachProcedure left = BuildDoglegIfUseful(physicalRunwayId,
                runway, direction, obstacles, limits, -1.0, registryGeneration);
            AERISApproachProcedure right = BuildDoglegIfUseful(physicalRunwayId,
                runway, direction, obstacles, limits, 1.0, registryGeneration);
            if (left != null) result.Add(left);
            if (right != null) result.Add(right);

            if (result.Count == 0)
                result.Add(BuildRejected(physicalRunwayId, direction,
                    registryGeneration, "NO_SAFE_PROFILE",
                    "All restrained glide profiles and obstacle-aware outer legs were rejected."));
            result.Sort(CompareProcedures);
            return result.AsReadOnly();
        }

        static bool ValidateInputs(AERISRunwayDefinition runway,
            AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, out string reason)
        {
            reason = string.Empty;
            if (!string.IsNullOrEmpty(obstacles.DirectionStableId) &&
                !string.Equals(obstacles.DirectionStableId, direction.StableId,
                    StringComparison.OrdinalIgnoreCase))
            {
                reason = "Obstacle snapshot direction generation mismatch.";
                return false;
            }
            return true;
        }

        static List<double> OrderedAngles(AERISApproachPlanningLimits limits)
        {
            var values = new List<double>();
            double step = Math.Max(0.05, limits.GlideAngleStepDeg);
            double min = Math.Max(0.5, limits.MinimumGlideAngleDeg);
            double max = Math.Max(min, limits.ConditionalMaximumGlideAngleDeg);
            double preferred = Clamp(limits.PreferredGlideAngleDeg, min, max);
            values.Add(Quantize(preferred, step));
            for (double delta = step; delta <= max - min + step * 0.5; delta += step)
            {
                double high = Quantize(preferred + delta, step);
                double low = Quantize(preferred - delta, step);
                if (high <= max + 1e-9 && !ContainsAngle(values, high)) values.Add(high);
                if (low >= min - 1e-9 && !ContainsAngle(values, low)) values.Add(low);
            }
            return values;
        }

        static bool AnglePermitted(double angle, AERISApproachPlanningLimits limits)
        {
            if (angle < limits.MinimumGlideAngleDeg - 1e-9 ||
                angle > limits.ConditionalMaximumGlideAngleDeg + 1e-9) return false;
            if (angle > limits.ObstacleMaximumGlideAngleDeg + 1e-9 &&
                !limits.AircraftSupportsSteepApproach) return false;
            return true;
        }

        static bool FinalCorridorClear(AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, AERISApproachPlanningLimits limits,
            double angleDeg, double maximumDistance, out string reason)
        {
            reason = string.Empty;
            double thresholdElevation = direction.Threshold.ElevationMeters;
            double anchorHeight = Math.Max(0.0, direction.ThresholdCrossingHeightMeters);
            double tangent = Math.Tan(angleDeg * Math.PI / 180.0);
            for (int i = 0; i < obstacles.Samples.Count; i++)
            {
                AERISApproachObstacleSample sample = obstacles.Samples[i];
                if (sample == null || !Finite(sample.AlongTrackMeters) ||
                    sample.AlongTrackMeters < 0.0 ||
                    sample.AlongTrackMeters > maximumDistance) continue;
                double lateralLimit = limits.CorridorHalfWidthMeters +
                    Math.Max(0.0, sample.HorizontalRadiusMeters);
                if (Math.Abs(sample.CrossTrackMeters) > lateralLimit) continue;
                double pathAltitude = thresholdElevation + anchorHeight +
                    sample.AlongTrackMeters * tangent;
                double clearance = sample.IsTerrain
                    ? limits.MinimumTerrainClearanceMeters
                    : limits.MinimumObstacleClearanceMeters;
                if (pathAltitude + 1e-6 < sample.TopElevationMeters + clearance)
                {
                    reason = "Blocked by " + (sample.IsTerrain ? "terrain" : "obstacle") +
                        " " + (sample.SourceId ?? string.Empty) + " at " +
                        sample.AlongTrackMeters.ToString("0", CultureInfo.InvariantCulture) +
                        " m.";
                    return false;
                }
            }
            return true;
        }

        static AERISApproachProcedure BuildDirect(string physicalRunwayId,
            AERISRunwayDefinition runway, AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, AERISApproachPlanningLimits limits,
            double angle, long generation)
        {
            AERISApproachProcedureType type = angle > limits.NormalMaximumGlideAngleDeg
                ? AERISApproachProcedureType.SteepDirect
                : AERISApproachProcedureType.Direct;
            var procedure = CreateBase(physicalRunwayId, direction, obstacles,
                limits, angle, type, generation);
            AERISGeoPoint capture = Offset(direction.Threshold,
                NormalizeHeading(direction.HeadingDeg + 180.0),
                limits.MaximumCaptureDistanceMeters, obstacles.BodyRadiusMeters);
            procedure.Legs.Add(new AERISApproachLeg
            {
                Id = procedure.StableId + "/FINAL",
                Type = AERISApproachLegType.FinalLocalizer,
                Start = capture,
                End = direction.Threshold.Clone(),
                InboundCourseDeg = direction.HeadingDeg,
                MinimumAltitudeMeters = PathAltitude(direction, angle,
                    limits.MaximumCaptureDistanceMeters),
                CorridorHalfWidthMeters = limits.CorridorHalfWidthMeters,
                ConstraintText = "FINAL LOCALIZER MUST COINCIDE WITH RUNWAY CENTERLINE"
            });
            BuildGlideProfile(procedure, direction, limits, angle,
                limits.MaximumCaptureDistanceMeters);
            AddMissedApproach(procedure, direction, obstacles, limits);
            procedure.Detail = type == AERISApproachProcedureType.SteepDirect
                ? "Conditional steep direct approach; aircraft capability required."
                : "Direct centerline approach with restrained adaptive glide path.";
            return procedure;
        }

        static AERISApproachProcedure BuildDoglegIfUseful(string physicalRunwayId,
            AERISRunwayDefinition runway, AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, AERISApproachPlanningLimits limits,
            double side, long generation)
        {
            bool blockedOutsideFinal = false;
            double obstacleOffset = 0.0;
            for (int i = 0; i < obstacles.Samples.Count; i++)
            {
                AERISApproachObstacleSample sample = obstacles.Samples[i];
                if (sample == null || sample.AlongTrackMeters <=
                    limits.MinimumFinalStraightMeters ||
                    sample.AlongTrackMeters > limits.MaximumCaptureDistanceMeters) continue;
                if (Math.Abs(sample.CrossTrackMeters) <= limits.CorridorHalfWidthMeters +
                    Math.Max(0.0, sample.HorizontalRadiusMeters))
                {
                    blockedOutsideFinal = true;
                    obstacleOffset = Math.Max(obstacleOffset,
                        Math.Abs(sample.CrossTrackMeters) + sample.HorizontalRadiusMeters);
                }
            }
            if (!blockedOutsideFinal) return null;

            double angle = SelectDoglegGlideAngle(direction, obstacles, limits);
            if (!Finite(angle)) return null;
            string finalReason;
            if (!FinalCorridorClear(direction, obstacles, limits, angle,
                limits.MinimumFinalStraightMeters, out finalReason)) return null;

            double joinDistance = Math.Max(limits.MinimumFinalStraightMeters,
                limits.TransitionLengthMeters * 2.0);
            double captureDistance = Math.Max(joinDistance + 3000.0,
                Math.Min(limits.MaximumCaptureDistanceMeters,
                    joinDistance + 10000.0));
            double lateralOffset = Math.Max(limits.CorridorHalfWidthMeters * 2.5,
                obstacleOffset + limits.CorridorHalfWidthMeters);
            double turnAngle = Math.Atan2(lateralOffset,
                captureDistance - joinDistance) * 180.0 / Math.PI;
            if (turnAngle > limits.MaximumDoglegTurnDeg + 1e-9) return null;

            AERISApproachProcedureType type = side < 0.0
                ? AERISApproachProcedureType.DoglegLeft
                : AERISApproachProcedureType.DoglegRight;
            AERISGeoPoint join = Offset(direction.Threshold,
                NormalizeHeading(direction.HeadingDeg + 180.0), joinDistance,
                obstacles.BodyRadiusMeters);
            AERISGeoPoint outerCenter = Offset(direction.Threshold,
                NormalizeHeading(direction.HeadingDeg + 180.0), captureDistance,
                obstacles.BodyRadiusMeters);
            AERISGeoPoint outer = Offset(outerCenter,
                NormalizeHeading(direction.HeadingDeg + side * 90.0), lateralOffset,
                obstacles.BodyRadiusMeters);
            string doglegReason;
            if (!DoglegCorridorClear(direction, obstacles, limits, angle,
                joinDistance, captureDistance, side * lateralOffset,
                out doglegReason)) return null;
            var procedure = CreateBase(physicalRunwayId, direction, obstacles,
                limits, angle, type, generation);
            double doglegCourse = InitialBearing(outer, join);
            procedure.Legs.Add(new AERISApproachLeg
            {
                Id = procedure.StableId + "/DOGLEG",
                Type = AERISApproachLegType.Dogleg,
                Start = outer,
                End = join,
                InboundCourseDeg = doglegCourse,
                MinimumAltitudeMeters = PathAltitude(direction, angle, captureDistance),
                CorridorHalfWidthMeters = limits.CorridorHalfWidthMeters,
                ConstraintText = "OUTER OBSTACLE BYPASS; MAX TURN " +
                    limits.MaximumDoglegTurnDeg.ToString("0.0",
                        CultureInfo.InvariantCulture) + " DEG"
            });
            procedure.Legs.Add(new AERISApproachLeg
            {
                Id = procedure.StableId + "/FINAL",
                Type = AERISApproachLegType.FinalLocalizer,
                Start = join,
                End = direction.Threshold.Clone(),
                InboundCourseDeg = direction.HeadingDeg,
                MinimumAltitudeMeters = PathAltitude(direction, angle, joinDistance),
                CorridorHalfWidthMeters = limits.CorridorHalfWidthMeters,
                ConstraintText = "FINAL LOCALIZER MUST COINCIDE WITH RUNWAY CENTERLINE"
            });
            BuildGlideProfile(procedure, direction, limits, angle, captureDistance);
            AddMissedApproach(procedure, direction, obstacles, limits);
            procedure.Detail = "Obstacle-aware outer dogleg; stabilized final remains centerline.";
            return procedure;
        }

        static bool DoglegCorridorClear(
            AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles,
            AERISApproachPlanningLimits limits, double angleDeg,
            double joinDistance, double captureDistance, double outerCrossTrack,
            out string reason)
        {
            reason = string.Empty;
            double dx = captureDistance - joinDistance;
            double dy = outerCrossTrack;
            double denominator = dx * dx + dy * dy;
            if (denominator < 1.0)
            {
                reason = "DOGLEG SEGMENT DEGENERATE";
                return false;
            }
            double tangent = Math.Tan(angleDeg * Math.PI / 180.0);
            for (int i = 0; i < obstacles.Samples.Count; i++)
            {
                AERISApproachObstacleSample sample = obstacles.Samples[i];
                if (sample == null || sample.AlongTrackMeters < joinDistance ||
                    sample.AlongTrackMeters > captureDistance) continue;
                // Segment from join (joinDistance,0) to outer
                // (captureDistance,outerCrossTrack) in runway-relative coordinates.
                double px = sample.AlongTrackMeters - joinDistance;
                double py = sample.CrossTrackMeters;
                double t = Clamp((px * dx + py * dy) / denominator, 0.0, 1.0);
                double closestX = t * dx;
                double closestY = t * dy;
                double lateralDistance = Math.Sqrt(
                    (px - closestX) * (px - closestX) +
                    (py - closestY) * (py - closestY));
                double protectedRadius = limits.CorridorHalfWidthMeters +
                    Math.Max(0.0, sample.HorizontalRadiusMeters);
                if (lateralDistance > protectedRadius) continue;
                double pathAlong = joinDistance + closestX;
                double pathAltitude = direction.Threshold.ElevationMeters +
                    Math.Max(0.0, direction.ThresholdCrossingHeightMeters) +
                    pathAlong * tangent;
                double clearance = sample.IsTerrain
                    ? limits.MinimumTerrainClearanceMeters
                    : limits.MinimumObstacleClearanceMeters;
                if (pathAltitude + 1e-6 < sample.TopElevationMeters + clearance)
                {
                    reason = "DOGLEG BLOCKED BY " +
                        (sample.IsTerrain ? "TERRAIN " : "OBSTACLE ") +
                        (sample.SourceId ?? string.Empty);
                    return false;
                }
            }
            return true;
        }

        static double SelectDoglegGlideAngle(AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, AERISApproachPlanningLimits limits)
        {
            List<double> angles = OrderedAngles(limits);
            for (int i = 0; i < angles.Count; i++)
            {
                double angle = angles[i];
                if (!AnglePermitted(angle, limits)) continue;
                string reason;
                if (FinalCorridorClear(direction, obstacles, limits, angle,
                    limits.MinimumFinalStraightMeters, out reason)) return angle;
            }
            return double.NaN;
        }

        static AERISApproachProcedure CreateBase(string physicalRunwayId,
            AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, AERISApproachPlanningLimits limits,
            double angle, AERISApproachProcedureType type, long generation)
        {
            string directionId = direction.StableId ?? string.Empty;
            string stable = directionId + "\nPROC\n" + type.ToString().ToUpperInvariant() +
                "\n" + angle.ToString("0.00", CultureInfo.InvariantCulture);
            bool conditional = angle > limits.NormalMaximumGlideAngleDeg + 1e-9;
            return new AERISApproachProcedure
            {
                StableId = stable,
                PhysicalRunwayId = physicalRunwayId ?? string.Empty,
                DirectionStableId = directionId,
                DisplayName = type.ToString().ToUpperInvariant() + " " +
                    angle.ToString("0.00", CultureInfo.InvariantCulture) + " DEG",
                Type = type,
                State = conditional ? AERISApproachProcedureState.Conditional :
                    AERISApproachProcedureState.Available,
                FinalCourseDeg = direction.HeadingDeg,
                GlideAngleDeg = angle,
                ThresholdCrossingHeightMeters = direction.ThresholdCrossingHeightMeters,
                RequiredMissedApproachAltitudeMeters = Math.Max(
                    limits.MinimumMissedApproachAltitudeMeters,
                    obstacles.MissedApproachMinimumAltitudeMeters),
                TerrainSignature = obstacles.TerrainSignature ?? string.Empty,
                ObstacleSignature = obstacles.ObstacleSignature ?? string.Empty,
                RegistryGeneration = generation
            };
        }

        static AERISApproachProcedure BuildPending(string physicalRunwayId,
            AERISRunwayDirectionDefinition direction, long generation, string detail)
        {
            return new AERISApproachProcedure
            {
                StableId = (direction == null ? string.Empty : direction.StableId) +
                    "\nPROC\nPENDING",
                PhysicalRunwayId = physicalRunwayId ?? string.Empty,
                DirectionStableId = direction == null ? string.Empty :
                    direction.StableId ?? string.Empty,
                DisplayName = "APPROACH SURVEY PENDING",
                State = AERISApproachProcedureState.Pending,
                FinalCourseDeg = direction == null ? 0.0 : direction.HeadingDeg,
                FailureCode = "CORRIDOR_PENDING",
                Detail = detail ?? string.Empty,
                RegistryGeneration = generation
            };
        }

        static AERISApproachProcedure BuildRejected(string physicalRunwayId,
            AERISRunwayDirectionDefinition direction, long generation,
            string code, string detail)
        {
            return new AERISApproachProcedure
            {
                StableId = (direction == null ? string.Empty : direction.StableId) +
                    "\nPROC\nREJECTED",
                PhysicalRunwayId = physicalRunwayId ?? string.Empty,
                DirectionStableId = direction == null ? string.Empty :
                    direction.StableId ?? string.Empty,
                DisplayName = "NO CERTIFIED APPROACH",
                State = AERISApproachProcedureState.Rejected,
                FinalCourseDeg = direction == null ? 0.0 : direction.HeadingDeg,
                FailureCode = code ?? string.Empty,
                Detail = detail ?? string.Empty,
                RegistryGeneration = generation
            };
        }

        static void BuildGlideProfile(AERISApproachProcedure procedure,
            AERISRunwayDirectionDefinition direction, AERISApproachPlanningLimits limits,
            double angle, double captureDistance)
        {
            double finalStart = Math.Max(limits.MinimumFinalStraightMeters,
                limits.TransitionLengthMeters + limits.FlareGateDistanceMeters);
            double transitionStart = Math.Min(captureDistance,
                finalStart + limits.TransitionLengthMeters);
            procedure.GlideProfile.Add(new AERISGlideProfileSegment
            {
                Type = AERISGlideSegmentType.OuterDescent,
                StartDistanceFromThresholdMeters = captureDistance,
                EndDistanceFromThresholdMeters = transitionStart,
                StartPathAngleDeg = angle,
                EndPathAngleDeg = angle,
                MinimumAltitudeMeters = PathAltitude(direction, angle, transitionStart),
                ConstraintText = "NO ABRUPT LOW-ALTITUDE PATH CHANGE"
            });
            procedure.GlideProfile.Add(new AERISGlideProfileSegment
            {
                Type = AERISGlideSegmentType.Transition,
                StartDistanceFromThresholdMeters = transitionStart,
                EndDistanceFromThresholdMeters = finalStart,
                StartPathAngleDeg = angle,
                EndPathAngleDeg = angle,
                MinimumAltitudeMeters = PathAltitude(direction, angle, finalStart),
                ConstraintText = "CONTINUOUS CURVATURE TRANSITION"
            });
            procedure.GlideProfile.Add(new AERISGlideProfileSegment
            {
                Type = AERISGlideSegmentType.StabilizedFinal,
                StartDistanceFromThresholdMeters = finalStart,
                EndDistanceFromThresholdMeters = limits.FlareGateDistanceMeters,
                StartPathAngleDeg = angle,
                EndPathAngleDeg = angle,
                MinimumAltitudeMeters = PathAltitude(direction, angle,
                    limits.FlareGateDistanceMeters),
                ConstraintText = "CONSTANT-ANGLE STABILIZED FINAL"
            });
            procedure.GlideProfile.Add(new AERISGlideProfileSegment
            {
                Type = AERISGlideSegmentType.FlareGate,
                StartDistanceFromThresholdMeters = limits.FlareGateDistanceMeters,
                EndDistanceFromThresholdMeters = 0.0,
                StartPathAngleDeg = angle,
                EndPathAngleDeg = 0.0,
                MinimumAltitudeMeters = direction.Threshold.ElevationMeters +
                    Math.Max(0.0, direction.ThresholdCrossingHeightMeters),
                ConstraintText = "DISPLAY/PLANNING GATE ONLY; NO FLIGHT CONTROL AUTHORITY"
            });
        }

        static void AddMissedApproach(AERISApproachProcedure procedure,
            AERISRunwayDirectionDefinition direction,
            AERISApproachObstacleSnapshot obstacles, AERISApproachPlanningLimits limits)
        {
            double safeAltitude = Math.Max(limits.MinimumMissedApproachAltitudeMeters,
                obstacles.MissedApproachMinimumAltitudeMeters);
            AERISGeoPoint end = Offset(direction.Threshold,
                NormalizeHeading(direction.MissedApproachHeadingDeg), 5000.0,
                obstacles.BodyRadiusMeters);
            end.ElevationMeters = Math.Max(end.ElevationMeters, safeAltitude);
            procedure.Legs.Add(new AERISApproachLeg
            {
                Id = procedure.StableId + "/MISSED",
                Type = AERISApproachLegType.MissedApproach,
                Start = direction.Threshold.Clone(),
                End = end,
                InboundCourseDeg = NormalizeHeading(direction.MissedApproachHeadingDeg),
                MinimumAltitudeMeters = safeAltitude,
                CorridorHalfWidthMeters = limits.CorridorHalfWidthMeters,
                ConstraintText = "PROCEDURE REJECTED IF MISSED-APPROACH CORRIDOR IS NOT CLEAR"
            });
        }

        static double PathAltitude(AERISRunwayDirectionDefinition direction,
            double angleDeg, double distanceMeters)
        {
            return direction.Threshold.ElevationMeters +
                Math.Max(0.0, direction.ThresholdCrossingHeightMeters) +
                Math.Max(0.0, distanceMeters) *
                Math.Tan(angleDeg * Math.PI / 180.0);
        }

        static AERISGeoPoint Offset(AERISGeoPoint origin, double bearingDeg,
            double distanceMeters, double bodyRadiusMeters)
        {
            if (origin == null) return null;
            double radius = Finite(bodyRadiusMeters) && bodyRadiusMeters >= 1000.0
                ? bodyRadiusMeters : KerbinFallbackRadiusMeters;
            radius += origin.ElevationMeters;
            if (radius < 1000.0) radius = KerbinFallbackRadiusMeters;
            double angular = distanceMeters / radius;
            double bearing = bearingDeg * Math.PI / 180.0;
            double lat1 = origin.LatitudeDeg * Math.PI / 180.0;
            double lon1 = origin.LongitudeDeg * Math.PI / 180.0;
            double sinLat2 = Math.Sin(lat1) * Math.Cos(angular) +
                Math.Cos(lat1) * Math.Sin(angular) * Math.Cos(bearing);
            double lat2 = Math.Asin(Clamp(sinLat2, -1.0, 1.0));
            double lon2 = lon1 + Math.Atan2(Math.Sin(bearing) * Math.Sin(angular) *
                Math.Cos(lat1), Math.Cos(angular) - Math.Sin(lat1) * Math.Sin(lat2));
            return new AERISGeoPoint
            {
                LatitudeDeg = lat2 * 180.0 / Math.PI,
                LongitudeDeg = NormalizeLongitude(lon2 * 180.0 / Math.PI),
                ElevationMeters = origin.ElevationMeters
            };
        }

        static double InitialBearing(AERISGeoPoint from, AERISGeoPoint to)
        {
            if (from == null || to == null) return 0.0;
            double lat1 = from.LatitudeDeg * Math.PI / 180.0;
            double lat2 = to.LatitudeDeg * Math.PI / 180.0;
            double dLon = (to.LongitudeDeg - from.LongitudeDeg) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2);
            double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) *
                Math.Cos(lat2) * Math.Cos(dLon);
            return NormalizeHeading(Math.Atan2(y, x) * 180.0 / Math.PI);
        }

        static int CompareProcedures(AERISApproachProcedure a,
            AERISApproachProcedure b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int state = a.State.CompareTo(b.State);
            if (state != 0) return state;
            double aPenalty = Math.Abs(a.GlideAngleDeg - 3.0) + TypePenalty(a.Type);
            double bPenalty = Math.Abs(b.GlideAngleDeg - 3.0) + TypePenalty(b.Type);
            int value = aPenalty.CompareTo(bPenalty);
            if (value != 0) return value;
            return string.Compare(a.StableId, b.StableId,
                StringComparison.OrdinalIgnoreCase);
        }

        static double TypePenalty(AERISApproachProcedureType type)
        {
            switch (type)
            {
                case AERISApproachProcedureType.Direct: return 0.0;
                case AERISApproachProcedureType.SteepDirect: return 0.5;
                case AERISApproachProcedureType.OffsetLeft:
                case AERISApproachProcedureType.OffsetRight: return 1.0;
                default: return 1.5;
            }
        }

        static bool ContainsAngle(IList<double> values, double value)
        {
            for (int i = 0; i < values.Count; i++)
                if (Math.Abs(values[i] - value) < 1e-9) return true;
            return false;
        }

        static double Quantize(double value, double step)
        {
            return Math.Round(value / step, MidpointRounding.AwayFromZero) * step;
        }

        static double NormalizeHeading(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        static double NormalizeLongitude(double value)
        {
            while (value > 180.0) value -= 360.0;
            while (value < -180.0) value += 360.0;
            return value;
        }

        static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
