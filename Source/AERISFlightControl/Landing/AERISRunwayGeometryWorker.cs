using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace AERISFlightControl.Landing
{
    // Pure numeric runway estimator.  No Unity/KSP type is referenced in this file.
    // Correlated measurements are fused as parameters, while certification counts
    // independent evidence families rather than raw method votes.
    internal static class AERISRunwayGeometryWorker
    {
        const double DegToRad = Math.PI / 180.0;
        const double RadToDeg = 180.0 / Math.PI;

        sealed class Orientation
        {
            internal double East;
            internal double North;
            internal double Heading;
            internal double Prior;
            internal bool IndependentSurfaceAxis;
            internal double SurfaceAspectRatio;
            internal int SurfacePointCount;
            internal AERISRunwayMeasurementMethod Methods;
        }

        sealed class PrimitiveProjection
        {
            internal AERISSurveyPrimitive Primitive;
            internal double Along;
            internal double Across;
            internal double AlongHalf;
            internal double AcrossHalf;
            internal double Alignment;
            internal double Weight;
            internal bool MarkerOnly;
        }

        sealed class AxisProjection
        {
            internal AERISSurveyPoint Point;
            internal double Along;
            internal double Across;
        }

        sealed class Band
        {
            internal readonly List<PrimitiveProjection> Members =
                new List<PrimitiveProjection>();
            internal double AcrossCenter;
            internal double AcrossWeight;
        }

        internal static AERISRunwaySurveyResult Execute(AERISRunwaySurveyJob job)
        {
            var watch = Stopwatch.StartNew();
            var result = new AERISRunwaySurveyResult();
            if (job == null || job.Snapshot == null)
            {
                result.State = AERISRunwayCertificationState.Failed;
                result.FailureCode = AERISRunwayFailureCode.ProviderDataError;
                result.Detail = "SURVEY JOB/SNAPSHOT MISSING";
                return result;
            }

            AERISRunwaySurveySnapshot snapshot = job.Snapshot;
            AERISRunwayMethodPlan methodPlan = AERISRunwaySurveyPlanner.Create(snapshot);
            result.Generation = job.Generation;
            result.Sequence = job.Sequence;
            result.StableRecordId = snapshot.StableRecordId;
            result.InputFingerprint = snapshot.InputFingerprint;
            result.PlannedMethods = methodPlan.ApplicableMethods;
            try
            {
                if (!snapshot.ProviderExplicitRunway)
                    return Fail(result, AERISRunwayFailureCode.NotFixedWingRunway,
                        "PROVIDER DOES NOT CLASSIFY THE FACILITY AS A FIXED-WING RUNWAY", watch);
                if (snapshot.RunwayUserCalibrationPending)
                    return Fail(result, AERISRunwayFailureCode.UserCalibrationRequired,
                        (snapshot.RunwayPlacementMismatchObserved
                            ? "OBSERVED RUNWAY PLACEMENT MISMATCH — USER TWO-POINT CALIBRATION REQUIRED"
                            : "USER TWO-POINT RUNWAY CALIBRATION IS INCOMPLETE") +
                        (string.IsNullOrEmpty(snapshot.RunwayPlacementObservationDetail)
                            ? string.Empty : "; " +
                                snapshot.RunwayPlacementObservationDetail), watch);
                if (snapshot.SurveyMethod == AERISRunwaySurveyMethod.ManualRequired &&
                    !snapshot.RunwayWitnessUserCalibrated)
                    return Fail(result, AERISRunwayFailureCode.UserCalibrationRequired,
                        "CATALOG REQUIRES USER TWO-POINT RUNWAY CALIBRATION BEFORE CERTIFICATION",
                        watch);
                if (snapshot.AbsolutePlacementRequired &&
                    !snapshot.AbsolutePlacementConstraintAvailable &&
                    !snapshot.RunwayWitnessAvailable)
                    return Fail(result, AERISRunwayFailureCode.AbsolutePlacementInvalid,
                        "KK/SLE ABSOLUTE PLACEMENT REQUIRES A FINITE LAUNCH ANCHOR OR RUNWAY WITNESS",
                        watch);
                if (!snapshot.GeometryReadable ||
                    (snapshot.Primitives.Length == 0 && snapshot.Points.Length < 8))
                    return Fail(result, AERISRunwayFailureCode.NoGeometryEvidence,
                        "NO COPIED NUMERIC GEOMETRY WAS AVAILABLE", watch);

                var protectedCandidates = new List<AERISRunwayAxisCandidate>();
                AERISRunwayFailureCode protectionFailure;
                string protectionDetail;
                bool witnessConflict;
                BuildProtectedCandidates(snapshot, protectedCandidates,
                    out protectionFailure, out protectionDetail, out witnessConflict);
                if (witnessConflict)
                {
                    return Fail(result, AERISRunwayFailureCode.PlanWitnessConflict,
                        protectionDetail, watch);
                }

                List<Orientation> orientations = BuildOrientations(snapshot);
                var geometryCandidates = new List<AERISRunwayAxisCandidate>();
                bool wholeSiteOnly = false;
                AERISRunwayFailureCode strongestRejection = protectionFailure;
                for (int i = 0; i < orientations.Count; i++)
                {
                    List<AERISRunwayAxisCandidate> local;
                    bool rejectedPlatform;
                    AERISRunwayFailureCode rejection;
                    EvaluateOrientation(snapshot, orientations[i], out local,
                        out rejectedPlatform, out rejection);
                    if (rejectedPlatform) wholeSiteOnly = true;
                    strongestRejection = PreferFailure(strongestRejection, rejection);
                    for (int j = 0; j < local.Count; j++)
                    {
                        if (local[j].CertificationBasis ==
                            AERISRunwayCertificationBasis.Unknown)
                        {
                            local[j].CertificationBasis =
                                AERISRunwayCertificationBasis.ProvisionalGeometry;
                            local[j].CertificationBasisDetail =
                                "UNANCHORED GEOMETRY CANDIDATE";
                        }
                        AddOrReplace(geometryCandidates, local[j]);
                    }
                }

                var candidates = new List<AERISRunwayAxisCandidate>();
                for (int i = 0; i < protectedCandidates.Count; i++)
                    AddOrReplace(candidates, protectedCandidates[i]);
                if (!snapshot.AbsolutePlacementRequired)
                    for (int i = 0; i < geometryCandidates.Count; i++)
                        AddOrReplace(candidates, geometryCandidates[i]);


                if (snapshot.AbsolutePlacementRequired && candidates.Count == 0 &&
                    geometryCandidates.Count > 0)
                {
                    geometryCandidates.Sort(CompareCandidates);
                    RemoveWeakDuplicates(geometryCandidates);
                    if (geometryCandidates.Count > 4)
                        geometryCandidates.RemoveRange(4, geometryCandidates.Count - 4);
                    for (int i = 0; i < geometryCandidates.Count; i++)
                    {
                        geometryCandidates[i].CertificationBasis =
                            AERISRunwayCertificationBasis.ProvisionalGeometry;
                        geometryCandidates[i].CertificationBasisDetail =
                            "NON-SELECTABLE GEOMETRY — NO PLAN/ANCHOR-CONNECTED AUTHORITY";
                    }
                    result.Runways = geometryCandidates.ToArray();
                    result.State = AERISRunwayCertificationState.Provisional;
                    result.FailureCode = protectionFailure != AERISRunwayFailureCode.None
                        ? protectionFailure : AERISRunwayFailureCode.AnchorSurfaceUnresolved;
                    result.Detail = "PROVISIONAL_GEOMETRY — " +
                        (string.IsNullOrEmpty(protectionDetail)
                            ? "ANCHOR-CONNECTED SURFACE NOT RESOLVED" : protectionDetail);
                    watch.Stop();
                    result.ElapsedTicks = watch.ElapsedTicks;
                    return result;
                }
                candidates.Sort(CompareCandidates);
                RemoveWeakDuplicates(candidates);
                if (candidates.Count == 0)
                    return Fail(result, strongestRejection != AERISRunwayFailureCode.None
                            ? strongestRejection : (wholeSiteOnly
                                ? AERISRunwayFailureCode.WholeSiteBoundsOnly
                                : AERISRunwayFailureCode.InsufficientEvidence),
                        string.IsNullOrEmpty(protectionDetail)
                            ? RejectionDetail(strongestRejection, wholeSiteOnly)
                            : protectionDetail + "; " +
                                RejectionDetail(strongestRejection, wholeSiteOnly),
                        watch);

                // More than four unresolved physical axes is deliberately ambiguous.
                // X and parallel layouts remain representable as two or more axes.
                if (candidates.Count > 4)
                    return Fail(result, AERISRunwayFailureCode.MultipleGeometrySolutions,
                        "MORE THAN FOUR COMPARABLY PLAUSIBLE RUNWAY AXES REMAIN", watch);

                var accepted = new List<AERISRunwayAxisCandidate>();
                for (int i = 0; i < candidates.Count; i++)
                {
                    AERISRunwayAxisCandidate candidate = candidates[i];
                    int families = CountFamilies(candidate.EvidenceFamilies);
                    bool protectedBasis = candidate.CertificationBasis ==
                        AERISRunwayCertificationBasis.PlanWitness ||
                        candidate.CertificationBasis ==
                            AERISRunwayCertificationBasis.AnchorSurfaceScan ||
                        candidate.CertificationBasis ==
                            AERISRunwayCertificationBasis.UserCalibrated;
                    bool certifiable = candidate.ClassificationConfidence >= 0.90 &&
                        candidate.GeometryConfidence >= 0.85 && families >= 3 &&
                        (candidate.EvidenceFamilies & AERISRunwayEvidenceFamily.GeometryTopology) != 0 &&
                        (!snapshot.AbsolutePlacementRequired || protectedBasis);
                    if (certifiable) accepted.Add(candidate);
                }
                if (accepted.Count == 0)
                    return Fail(result, AERISRunwayFailureCode.InsufficientEvidence,
                        "GEOMETRY EXISTS BUT INDEPENDENT EVIDENCE/UNCERTAINTY GATE DID NOT PASS",
                        watch);

                result.Runways = accepted.ToArray();
                for (int i = 0; i < accepted.Count; i++)
                    result.ExecutedMethods |= accepted[i].Methods;
                result.State = AERISRunwayCertificationState.Certified;
                result.FailureCode = AERISRunwayFailureCode.None;
                result.Detail = "CONSENSUS CERTIFIED " + accepted.Count +
                    " PHYSICAL RUNWAY AXIS/AXES; basis=" +
                    accepted[0].CertificationBasis.ToString().ToUpperInvariant() +
                    "; " + methodPlan.Detail;
                watch.Stop();
                result.ElapsedTicks = watch.ElapsedTicks;
                return result;
            }
            catch (Exception ex)
            {
                result.State = AERISRunwayCertificationState.Failed;
                result.FailureCode = AERISRunwayFailureCode.WorkerFailure;
                result.Detail = ex.GetType().Name + ": " + ex.Message;
                result.WorkerException = true;
                watch.Stop();
                result.ElapsedTicks = watch.ElapsedTicks;
                return result;
            }
        }

        static AERISRunwaySurveyResult Fail(AERISRunwaySurveyResult result,
            AERISRunwayFailureCode code, string detail, Stopwatch watch)
        {
            result.State = AERISRunwayCertificationState.Failed;
            result.FailureCode = code;
            result.Detail = detail;
            watch.Stop();
            result.ElapsedTicks = watch.ElapsedTicks;
            return result;
        }

        static List<Orientation> BuildOrientations(AERISRunwaySurveySnapshot snapshot)
        {
            var values = new List<Orientation>();
            bool absoluteAxisRequired = snapshot != null && snapshot.AbsolutePlacementRequired;

            Orientation surfaceAxis;
            bool hasIndependentSurfaceAxis = TryRunwaySurfacePca(snapshot, out surfaceAxis);
            if (hasIndependentSurfaceAxis)
            {
                AddOrientation(values, surfaceAxis.East, surfaceAxis.North,
                    surfaceAxis.Prior, surfaceAxis.Methods, true,
                    surfaceAxis.SurfaceAspectRatio, surfaceAxis.SurfacePointCount);
            }

            // Provider/launch headings are not independent for KK/SLE.  They are often
            // derived from the same static transform and can agree perfectly while the
            // runway mesh is rotated inside the model.  Keep metadata as a candidate only
            // for stock/non-absolute sources, or as a fail-closed last resort that will not
            // pass the independent surface-axis gate.
            if (!absoluteAxisRequired && Finite(snapshot.DeclaredHeadingDeg))
            {
                double heading = NormalizeHeading180(snapshot.DeclaredHeadingDeg);
                AddOrientation(values, Math.Sin(heading * DegToRad),
                    Math.Cos(heading * DegToRad), 1.0,
                    AERISRunwayMeasurementMethod.M01Metadata |
                    AERISRunwayMeasurementMethod.M15SpawnHeading,
                    false, 0.0, 0);
            }

            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if (!FinitePrimitive(primitive) || primitive.LengthMeters < 15.0) continue;
                double aspect = primitive.WidthMeters > 0.1
                    ? primitive.LengthMeters / primitive.WidthMeters : primitive.LengthMeters;
                bool semantic = (primitive.Semantic &
                    (AERISSurveySemantic.Runway | AERISSurveySemantic.Centerline |
                     AERISSurveySemantic.Threshold | AERISSurveySemantic.EdgeLight)) != 0;
                if (absoluteAxisRequired && !TrustedRunwayAxisPrimitive(snapshot, primitive, aspect))
                    continue;
                if (aspect < 2.0 && !semantic) continue;
                AddOrientation(values, primitive.AxisEast, primitive.AxisNorth,
                    absoluteAxisRequired
                        ? Math.Min(1.05, semantic ? 1.00 : 0.82)
                        : (semantic ? 0.95 : Math.Min(0.90, 0.55 + aspect * 0.035)),
                    primitive.Method, absoluteAxisRequired,
                    aspect, 0);
            }

            if (!absoluteAxisRequired)
            {
                Orientation pca;
                if (TryPointPca(snapshot.Points, out pca)) AddOrientation(values,
                    pca.East, pca.North, pca.Prior, pca.Methods,
                    false, pca.SurfaceAspectRatio, pca.SurfacePointCount);
            }
            else if (!hasIndependentSurfaceAxis && Finite(snapshot.DeclaredHeadingDeg))
            {
                // Diagnostic-only fallback.  ApplyAxisRegistrationConstraint rejects it
                // because no independent physical runway surface axis was measured.
                double heading = NormalizeHeading180(snapshot.DeclaredHeadingDeg);
                AddOrientation(values, Math.Sin(heading * DegToRad),
                    Math.Cos(heading * DegToRad), 0.25,
                    AERISRunwayMeasurementMethod.M01Metadata |
                    AERISRunwayMeasurementMethod.M15SpawnHeading,
                    false, 0.0, 0);
            }

            values.Sort((a, b) => b.Prior.CompareTo(a.Prior));
            return values;
        }

        static bool TrustedRunwayAxisPrimitive(AERISRunwaySurveySnapshot snapshot,
            AERISSurveyPrimitive primitive, double aspect)
        {
            AERISSurveySemantic semantic = primitive.Semantic;
            bool runwaySemantic = (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline | AERISSurveySemantic.EdgeLight |
                AERISSurveySemantic.Pavement)) != 0 ||
                (snapshot != null && snapshot.ProviderExplicitRunway &&
                    semantic == AERISSurveySemantic.None && aspect >= 8.0);
            bool excluded = (semantic & (AERISSurveySemantic.Taxiway |
                AERISSurveySemantic.Apron | AERISSurveySemantic.Platform |
                AERISSurveySemantic.Obstacle | AERISSurveySemantic.NaturalSurface)) != 0 &&
                (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline)) == 0;
            double minimumLength = Math.Max(80.0,
                snapshot == null ? 80.0 : snapshot.MinimumLengthMeters * 0.40);
            return !excluded && runwaySemantic && primitive.FlatnessDeg <= 6.0 &&
                primitive.LengthMeters >= minimumLength && aspect >= 4.0;
        }

        static void AddOrientation(List<Orientation> values, double east,
            double north, double prior, AERISRunwayMeasurementMethod methods,
            bool independentSurfaceAxis, double surfaceAspectRatio,
            int surfacePointCount)
        {
            double magnitude = Math.Sqrt(east * east + north * north);
            if (!Finite(magnitude) || magnitude < 1e-8) return;
            east /= magnitude;
            north /= magnitude;
            double heading = NormalizeHeading180(Math.Atan2(east, north) * RadToDeg);
            for (int i = 0; i < values.Count; i++)
            {
                if (AngleDifference180(values[i].Heading, heading) <= 2.0)
                {
                    // A physical runway-surface axis is independent evidence.  A later
                    // provider/primitive orientation may add methods, but must never
                    // overwrite that axis merely because its metadata prior is larger.
                    bool replaceAxis = independentSurfaceAxis &&
                        !values[i].IndependentSurfaceAxis;
                    if (independentSurfaceAxis == values[i].IndependentSurfaceAxis &&
                        prior > values[i].Prior) replaceAxis = true;
                    if (!values[i].IndependentSurfaceAxis &&
                        !independentSurfaceAxis && prior > values[i].Prior)
                        replaceAxis = true;
                    if (replaceAxis)
                    {
                        values[i].East = east;
                        values[i].North = north;
                        values[i].Heading = heading;
                        values[i].Prior = prior;
                    }
                    values[i].Methods |= methods;
                    values[i].IndependentSurfaceAxis |= independentSurfaceAxis;
                    values[i].SurfaceAspectRatio = Math.Max(values[i].SurfaceAspectRatio,
                        surfaceAspectRatio);
                    values[i].SurfacePointCount = Math.Max(values[i].SurfacePointCount,
                        surfacePointCount);
                    return;
                }
            }
            values.Add(new Orientation
            {
                East = east,
                North = north,
                Heading = heading,
                Prior = prior,
                IndependentSurfaceAxis = independentSurfaceAxis,
                SurfaceAspectRatio = surfaceAspectRatio,
                SurfacePointCount = surfacePointCount,
                Methods = methods
            });
        }

        static bool TryRunwaySurfacePca(AERISRunwaySurveySnapshot snapshot,
            out Orientation value)
        {
            value = null;
            if (snapshot == null || snapshot.Points == null ||
                snapshot.Points.Length < 16) return false;
            var eligible = new List<AERISSurveyPoint>();
            for (int i = 0; i < snapshot.Points.Length; i++)
            {
                AERISSurveyPoint point = snapshot.Points[i];
                if (!Finite(point.East) || !Finite(point.North) || !Finite(point.Up))
                    continue;
                AERISSurveySemantic semantic = point.Semantic;
                bool included = (semantic & (AERISSurveySemantic.Runway |
                    AERISSurveySemantic.Centerline | AERISSurveySemantic.Pavement)) != 0 ||
                    (snapshot.ProviderExplicitRunway && semantic == AERISSurveySemantic.None);
                bool excluded = (semantic & (AERISSurveySemantic.Taxiway |
                    AERISSurveySemantic.Apron | AERISSurveySemantic.Platform |
                    AERISSurveySemantic.Obstacle | AERISSurveySemantic.NaturalSurface |
                    AERISSurveySemantic.ApproachLight)) != 0 &&
                    (semantic & (AERISSurveySemantic.Runway |
                    AERISSurveySemantic.Centerline)) == 0;
                if (included && !excluded) eligible.Add(point);
            }
            if (eligible.Count < 16) return false;

            // Keep the worker bounded even for very detailed KK statics.  The source
            // ordering is deterministic, so the same asset always yields the same sample.
            if (eligible.Count > 4096)
            {
                int step = Math.Max(1, eligible.Count / 4096);
                var bounded = new List<AERISSurveyPoint>(4096);
                for (int i = 0; i < eligible.Count && bounded.Count < 4096; i += step)
                    bounded.Add(eligible[i]);
                eligible = bounded;
            }

            double stripeWidth = snapshot.DeclaredWidthMeters > 1.0
                ? Math.Max(30.0, Math.Min(180.0, snapshot.DeclaredWidthMeters * 1.75))
                : 90.0;
            var headings = new List<double>();
            if (Finite(snapshot.DeclaredHeadingDeg))
            {
                double declared = NormalizeHeading180(snapshot.DeclaredHeadingDeg);
                for (int i = -40; i <= 40; i++)
                    AddHeadingCandidate(headings, declared + i * 0.5);
            }
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if (!FinitePrimitive(primitive)) continue;
                double primitiveAspect = primitive.WidthMeters > 0.1
                    ? primitive.LengthMeters / primitive.WidthMeters : primitive.LengthMeters;
                if (!TrustedRunwayAxisPrimitive(snapshot, primitive, primitiveAspect)) continue;
                AddHeadingCandidate(headings,
                    Math.Atan2(primitive.AxisEast, primitive.AxisNorth) * RadToDeg);
            }
            double initialEast, initialNorth, initialAspect;
            if (TryWeightedPca(eligible, out initialEast, out initialNorth,
                out initialAspect))
                AddHeadingCandidate(headings,
                    Math.Atan2(initialEast, initialNorth) * RadToDeg);
            if (headings.Count == 0)
                for (int i = 0; i < 90; i++) AddHeadingCandidate(headings, i * 2.0);

            double bestScore = double.NegativeInfinity;
            List<AERISSurveyPoint> bestPoints = null;
            double bestHeading = 0.0;
            for (int i = 0; i < headings.Count; i++)
            {
                double score;
                List<AERISSurveyPoint> selected;
                if (!TryBestRunwayStripe(eligible, headings[i], stripeWidth,
                    out score, out selected)) continue;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestPoints = selected;
                    bestHeading = headings[i];
                }
            }
            if (bestPoints == null || bestPoints.Count < 16) return false;

            double east, north, surfaceAspect;
            if (!TryWeightedPca(bestPoints, out east, out north, out surfaceAspect)) return false;
            if (AngleDifference180(Math.Atan2(east, north) * RadToDeg,
                bestHeading) > 12.0) return false;
            double minAlong = double.PositiveInfinity;
            double maxAlong = double.NegativeInfinity;
            double minAcross = double.PositiveInfinity;
            double maxAcross = double.NegativeInfinity;
            double refinedNormalEast = -north;
            double refinedNormalNorth = east;
            for (int i = 0; i < bestPoints.Count; i++)
            {
                double along = bestPoints[i].East * east + bestPoints[i].North * north;
                double cross = bestPoints[i].East * refinedNormalEast +
                    bestPoints[i].North * refinedNormalNorth;
                minAlong = Math.Min(minAlong, along);
                maxAlong = Math.Max(maxAlong, along);
                minAcross = Math.Min(minAcross, cross);
                maxAcross = Math.Max(maxAcross, cross);
            }
            double span = maxAlong - minAlong;
            double width = maxAcross - minAcross;
            double minimumSpan = Math.Max(180.0,
                Math.Max(snapshot.MinimumLengthMeters * 0.65,
                    snapshot.DeclaredLengthMeters > 1.0
                        ? snapshot.DeclaredLengthMeters * 0.55 : 0.0));
            double geometricAspect = width > 1.0 ? span / width : span;
            if (!Finite(span) || !Finite(geometricAspect) || span < minimumSpan ||
                geometricAspect < 4.0 || surfaceAspect < 4.0) return false;

            value = new Orientation
            {
                East = east,
                North = north,
                Heading = NormalizeHeading180(Math.Atan2(east, north) * RadToDeg),
                Prior = Math.Min(1.25, 1.05 + Math.Log10(Math.Max(4.0,
                    geometricAspect)) * 0.10),
                IndependentSurfaceAxis = true,
                SurfaceAspectRatio = geometricAspect,
                SurfacePointCount = bestPoints.Count,
                Methods = AERISRunwayMeasurementMethod.M03MeshPca |
                    AERISRunwayMeasurementMethod.M05SubMeshMaterial |
                    AERISRunwayMeasurementMethod.M07LongSurfaceStrip |
                    AERISRunwayMeasurementMethod.M23RobustLineFit |
                    AERISRunwayMeasurementMethod.M24CrossSectionVoting |
                    AERISRunwayMeasurementMethod.M25MultiScale
            };
            return true;
        }

        static void AddHeadingCandidate(List<double> values, double heading)
        {
            heading = NormalizeHeading180(heading);
            for (int i = 0; i < values.Count; i++)
                if (AngleDifference180(values[i], heading) <= 0.20) return;
            values.Add(heading);
        }

        static bool TryBestRunwayStripe(IList<AERISSurveyPoint> points,
            double headingDeg, double stripeWidth, out double score,
            out List<AERISSurveyPoint> selected)
        {
            score = double.NegativeInfinity;
            selected = null;
            if (points == null || points.Count < 16) return false;
            double radians = NormalizeHeading180(headingDeg) * DegToRad;
            double east = Math.Sin(radians);
            double north = Math.Cos(radians);
            double normalEast = -north;
            double normalNorth = east;
            var projected = new List<AxisProjection>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                AERISSurveyPoint point = points[i];
                projected.Add(new AxisProjection
                {
                    Point = point,
                    Along = point.East * east + point.North * north,
                    Across = point.East * normalEast + point.North * normalNorth
                });
            }
            projected.Sort((a, b) => a.Across.CompareTo(b.Across));
            int count = projected.Count;
            int[] minQueue = new int[count];
            int[] maxQueue = new int[count];
            int minHead = 0, minTail = 0, maxHead = 0, maxTail = 0;
            int left = 0;
            int bestLeft = -1, bestRight = -1;
            for (int right = 0; right < count; right++)
            {
                while (minTail > minHead &&
                    projected[minQueue[minTail - 1]].Along >= projected[right].Along)
                    minTail--;
                minQueue[minTail++] = right;
                while (maxTail > maxHead &&
                    projected[maxQueue[maxTail - 1]].Along <= projected[right].Along)
                    maxTail--;
                maxQueue[maxTail++] = right;
                while (left < right &&
                    projected[right].Across - projected[left].Across > stripeWidth)
                {
                    left++;
                    while (minHead < minTail && minQueue[minHead] < left) minHead++;
                    while (maxHead < maxTail && maxQueue[maxHead] < left) maxHead++;
                }
                int support = right - left + 1;
                if (support < 16 || minHead >= minTail || maxHead >= maxTail) continue;
                double span = projected[maxQueue[maxHead]].Along -
                    projected[minQueue[minHead]].Along;
                double width = Math.Max(1.0,
                    projected[right].Across - projected[left].Across);
                double aspect = span / width;
                if (span < 100.0 || aspect < 2.5) continue;
                double localScore = support * span * Math.Min(8.0, aspect);
                if (localScore > score)
                {
                    score = localScore;
                    bestLeft = left;
                    bestRight = right;
                }
            }
            if (bestLeft < 0 || bestRight < bestLeft) return false;
            selected = new List<AERISSurveyPoint>(bestRight - bestLeft + 1);
            for (int i = bestLeft; i <= bestRight; i++)
                selected.Add(projected[i].Point);
            if (selected.Count < 16) return false;
            score = ScoreRunwayStripe(selected, headingDeg, stripeWidth);
            return Finite(score) && score > 0.0;
        }

        static double ScoreRunwayStripe(IList<AERISSurveyPoint> points,
            double headingDeg, double stripeWidth)
        {
            if (points == null || points.Count < 16) return double.NegativeInfinity;
            double radians = NormalizeHeading180(headingDeg) * DegToRad;
            double east = Math.Sin(radians);
            double north = Math.Cos(radians);
            double normalEast = -north;
            double normalNorth = east;
            double minAlong = double.PositiveInfinity;
            double maxAlong = double.NegativeInfinity;
            double minAcross = double.PositiveInfinity;
            double maxAcross = double.NegativeInfinity;
            for (int i = 0; i < points.Count; i++)
            {
                double along = points[i].East * east + points[i].North * north;
                double across = points[i].East * normalEast +
                    points[i].North * normalNorth;
                minAlong = Math.Min(minAlong, along);
                maxAlong = Math.Max(maxAlong, along);
                minAcross = Math.Min(minAcross, across);
                maxAcross = Math.Max(maxAcross, across);
            }
            double span = maxAlong - minAlong;
            double width = Math.Max(1.0, maxAcross - minAcross);
            if (!Finite(span) || span < 100.0) return double.NegativeInfinity;
            double aspect = span / width;
            if (!Finite(aspect) || aspect < 2.5) return double.NegativeInfinity;
            int bins = (int)Math.Floor(span / Math.Max(40.0, stripeWidth));
            bins = Math.Max(16, Math.Min(48, bins));
            var counts = new int[bins];
            double safeSpan = Math.Max(1e-6, span);
            for (int i = 0; i < points.Count; i++)
            {
                double along = points[i].East * east + points[i].North * north;
                int bin = (int)Math.Floor(((along - minAlong) / safeSpan) * bins);
                if (bin < 0) bin = 0;
                if (bin >= bins) bin = bins - 1;
                counts[bin]++;
            }
            int occupied = 0;
            double mean = 0.0;
            for (int i = 0; i < bins; i++)
            {
                if (counts[i] >= 2) occupied++;
                mean += counts[i];
            }
            mean /= bins;
            double coverage = occupied / (double)bins;
            if (coverage < 0.70 || mean <= 0.0) return double.NegativeInfinity;
            double variance = 0.0;
            for (int i = 0; i < bins; i++)
            {
                double difference = counts[i] - mean;
                variance += difference * difference;
            }
            variance /= bins;
            double coefficientOfVariation = Math.Sqrt(Math.Max(0.0, variance)) / mean;
            var sortedCounts = (int[])counts.Clone();
            Array.Sort(sortedCounts);
            double medianSupport = bins % 2 == 0
                ? (sortedCounts[bins / 2 - 1] + sortedCounts[bins / 2]) * 0.5
                : sortedCounts[bins / 2];
            double uniformity = 1.0 / (1.0 + 4.0 * coefficientOfVariation *
                coefficientOfVariation);
            return span * Math.Min(30.0, aspect) * coverage * uniformity *
                (1.0 + Math.Log(1.0 + medianSupport));
        }

        static bool TryWeightedPca(IList<AERISSurveyPoint> points,
            out double east, out double north, out double aspect)
        {
            east = 0.0;
            north = 1.0;
            aspect = 0.0;
            if (points == null || points.Count < 8) return false;
            double meanE = 0.0;
            double meanN = 0.0;
            double total = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                AERISSurveyPoint point = points[i];
                double weight = Math.Max(0.05, point.Weight);
                if ((point.Semantic & AERISSurveySemantic.Centerline) != 0) weight *= 2.5;
                else if ((point.Semantic & AERISSurveySemantic.Runway) != 0) weight *= 1.5;
                else if (point.Semantic == AERISSurveySemantic.None) weight *= 0.35;
                meanE += point.East * weight;
                meanN += point.North * weight;
                total += weight;
            }
            if (total <= 0.0) return false;
            meanE /= total;
            meanN /= total;
            double ee = 0.0;
            double nn = 0.0;
            double en = 0.0;
            for (int i = 0; i < points.Count; i++)
            {
                AERISSurveyPoint point = points[i];
                double weight = Math.Max(0.05, point.Weight);
                if ((point.Semantic & AERISSurveySemantic.Centerline) != 0) weight *= 2.5;
                else if ((point.Semantic & AERISSurveySemantic.Runway) != 0) weight *= 1.5;
                else if (point.Semantic == AERISSurveySemantic.None) weight *= 0.35;
                double de = point.East - meanE;
                double dn = point.North - meanN;
                ee += de * de * weight;
                nn += dn * dn * weight;
                en += de * dn * weight;
            }
            double angle = 0.5 * Math.Atan2(2.0 * en, nn - ee);
            east = Math.Sin(angle);
            north = Math.Cos(angle);
            double trace = ee + nn;
            double discriminant = Math.Sqrt(Math.Max(0.0,
                (ee - nn) * (ee - nn) + 4.0 * en * en));
            double major = (trace + discriminant) * 0.5;
            double minor = (trace - discriminant) * 0.5;
            aspect = minor > 1e-6 ? major / minor : major;
            return Finite(aspect) && aspect >= 3.0;
        }

        static bool TryPointPca(AERISSurveyPoint[] points, out Orientation value)
        {
            value = null;
            if (points == null || points.Length < 8) return false;
            double meanE = 0.0;
            double meanN = 0.0;
            double weight = 0.0;
            for (int i = 0; i < points.Length; i++)
            {
                AERISSurveyPoint point = points[i];
                if (!Finite(point.East) || !Finite(point.North)) continue;
                double w = Math.Max(0.05, point.Weight);
                if ((point.Semantic & (AERISSurveySemantic.Apron |
                    AERISSurveySemantic.Platform)) != 0) w *= 0.10;
                meanE += point.East * w;
                meanN += point.North * w;
                weight += w;
            }
            if (weight <= 0.0) return false;
            meanE /= weight;
            meanN /= weight;
            double ee = 0.0;
            double nn = 0.0;
            double en = 0.0;
            for (int i = 0; i < points.Length; i++)
            {
                AERISSurveyPoint point = points[i];
                if (!Finite(point.East) || !Finite(point.North)) continue;
                double w = Math.Max(0.05, point.Weight);
                if ((point.Semantic & (AERISSurveySemantic.Apron |
                    AERISSurveySemantic.Platform)) != 0) w *= 0.10;
                double e = point.East - meanE;
                double n = point.North - meanN;
                ee += e * e * w;
                nn += n * n * w;
                en += e * n * w;
            }
            double angle = 0.5 * Math.Atan2(2.0 * en, nn - ee);
            double east = Math.Sin(angle);
            double north = Math.Cos(angle);
            double trace = ee + nn;
            double discriminant = Math.Sqrt(Math.Max(0.0,
                (ee - nn) * (ee - nn) + 4.0 * en * en));
            double major = (trace + discriminant) * 0.5;
            double minor = (trace - discriminant) * 0.5;
            double ratio = minor > 1e-6 ? major / minor : major;
            if (!Finite(ratio) || ratio < 3.0) return false;
            value = new Orientation
            {
                East = east,
                North = north,
                Heading = NormalizeHeading180(Math.Atan2(east, north) * RadToDeg),
                Prior = Math.Min(0.88, 0.55 + Math.Log10(Math.Max(1.0, ratio)) * 0.18),
                IndependentSurfaceAxis = false,
                SurfaceAspectRatio = ratio,
                SurfacePointCount = points.Length,
                Methods = AERISRunwayMeasurementMethod.M03MeshPca |
                    AERISRunwayMeasurementMethod.M23RobustLineFit |
                    AERISRunwayMeasurementMethod.M25MultiScale
            };
            return true;
        }


        sealed class SurfaceSection
        {
            internal double Along;
            internal double AcrossCenter;
            internal double Width;
            internal double Up;
            internal int PrimitiveCount;
            internal AERISRunwayEvidenceFamily Evidence;
            internal AERISRunwayMeasurementMethod Methods;
        }

        static void BuildProtectedCandidates(AERISRunwaySurveySnapshot snapshot,
            List<AERISRunwayAxisCandidate> output,
            out AERISRunwayFailureCode failure, out string detail,
            out bool witnessConflict)
        {
            failure = AERISRunwayFailureCode.None;
            detail = string.Empty;
            witnessConflict = false;
            if (snapshot == null || output == null)
            {
                failure = AERISRunwayFailureCode.AnchorSurfaceUnresolved;
                detail = "RUNWAY SNAPSHOT/OUTPUT MISSING";
                return;
            }

            AERISRunwayAxisCandidate anchor;
            string anchorDetail;
            bool anchorValid = TryBuildAnchorSurfaceCandidate(snapshot, out anchor,
                out anchorDetail);

            if (!snapshot.RunwayWitnessAvailable)
            {
                if (anchorValid)
                {
                    AddOrReplace(output, anchor);
                    detail = anchorDetail;
                    return;
                }
                failure = AERISRunwayFailureCode.AnchorSurfaceUnresolved;
                detail = string.IsNullOrEmpty(anchorDetail)
                    ? "NO PLAN WITNESS AND NO LAUNCH-ANCHOR-CONNECTED SURFACE"
                    : anchorDetail;
                return;
            }

            AERISRunwayAxisCandidate witness;
            string witnessDetail;
            bool witnessValid = TryBuildWitnessCandidate(snapshot, anchorValid ? anchor : null,
                out witness, out witnessDetail);
            if (!witnessValid)
            {
                if (anchorValid && !snapshot.RunwayWitnessUserCalibrated)
                {
                    // A malformed external plan is not allowed to suppress a physically
                    // anchor-connected runway.  It remains diagnostic evidence only.
                    AddOrReplace(output, anchor);
                    failure = AERISRunwayFailureCode.None;
                    detail = "EXTERNAL PLAN WITNESS UNUSABLE; ANCHOR SCAN RETAINED; " +
                        witnessDetail;
                    return;
                }
                failure = snapshot.RunwayWitnessUserCalibrated
                    ? AERISRunwayFailureCode.UserCalibrationInvalid
                    : AERISRunwayFailureCode.AnchorSurfaceUnresolved;
                detail = witnessDetail;
                return;
            }

            if (snapshot.RunwayWitnessUserCalibrated)
            {
                // User calibration is an explicit two-point rescue.  Physical scan data
                // refines width/elevation when available, but never moves the marked axis.
                witness.CertificationBasis = AERISRunwayCertificationBasis.UserCalibrated;
                witness.CertificationBasisDetail = "USER TWO-POINT THRESHOLD CALIBRATION";
                witness.Methods |= AERISRunwayMeasurementMethod.M29UserCalibration;
                witness.EvidenceFamilies |= AERISRunwayEvidenceFamily.UserCalibration |
                    AERISRunwayEvidenceFamily.ExternalRunwayWitness;
                if (anchorValid) CompareWitnessAndAnchor(witness, anchor,
                    "USER CALIBRATION PHYSICAL CROSS-CHECK");
                AddOrReplace(output, witness);
                detail = witnessDetail;
                return;
            }

            if (!anchorValid)
            {
                // A Kramax plan is independent positional evidence, but bare coordinates
                // cannot certify unless copied physical geometry covers
                // most of the plan corridor.
                if (witness.AnchorScanValid && witness.AnchorStableCrossSectionRatio >= 0.55)
                {
                    witness.CertificationBasis = AERISRunwayCertificationBasis.PlanWitness;
                    witness.CertificationBasisDetail =
                        "KRAMAX RW/STOP CORRIDOR WITH PHYSICAL SURFACE COVERAGE";
                    AddOrReplace(output, witness);
                    detail = witnessDetail;
                    return;
                }
                failure = AERISRunwayFailureCode.AnchorSurfaceUnresolved;
                detail = "PLAN WITNESS FOUND BUT PHYSICAL RUNWAY CORRIDOR WAS NOT RESOLVED; " +
                    witnessDetail + "; " + anchorDetail;
                return;
            }

            CompareWitnessAndAnchor(witness, anchor, "KRAMAX PLAN CROSS-CHECK");
            double centerGate = Math.Max(350.0,
                Math.Min(1200.0, snapshot.RunwayWitnessLengthMeters * 0.35));
            double conflictCenterGate = Math.Max(900.0,
                Math.Min(2500.0, snapshot.RunwayWitnessLengthMeters * 0.70));
            double ratio = witness.PlanWitnessLengthRatio;
            bool ratioMatch = ratio >= 0.45 && ratio <= 2.20;
            bool match = witness.PlanWitnessHeadingErrorDeg <= 12.0 &&
                witness.PlanWitnessCenterErrorMeters <= centerGate && ratioMatch;
            bool conflict = witness.PlanWitnessHeadingErrorDeg > 22.0 ||
                witness.PlanWitnessCenterErrorMeters > conflictCenterGate ||
                ratio < 0.30 || ratio > 3.25;

            if (conflict)
            {
                witnessConflict = true;
                failure = AERISRunwayFailureCode.PlanWitnessConflict;
                anchor.CertificationBasis = AERISRunwayCertificationBasis.WitnessConflict;
                witness.CertificationBasis = AERISRunwayCertificationBasis.WitnessConflict;
                anchor.CertificationBasisDetail = "ANCHOR SURFACE CONFLICTS WITH KRAMAX PLAN";
                witness.CertificationBasisDetail = "KRAMAX PLAN CONFLICTS WITH ANCHOR SURFACE";
                AddOrReplace(output, anchor);
                AddOrReplace(output, witness);
                detail = "PLAN_WITNESS_CONFLICT source=" + snapshot.RunwayWitnessName +
                    "; centerError=" + Format(witness.PlanWitnessCenterErrorMeters) +
                    "m; headingError=" + Format(witness.PlanWitnessHeadingErrorDeg) +
                    "deg; lengthRatio=" + Format(ratio) + "; anchor=" + anchorDetail +
                    "; witness=" + witnessDetail;
                return;
            }

            if (match)
            {
                witness.CertificationBasis = AERISRunwayCertificationBasis.PlanWitness;
                witness.CertificationBasisDetail =
                    "KRAMAX RW/STOP MATCHED LAUNCH-ANCHOR-CONNECTED SURFACE";
                witness.ClassificationConfidence = Math.Max(0.96,
                    witness.ClassificationConfidence);
                witness.GeometryConfidence = Math.Max(0.90,
                    Math.Min(0.98, (witness.GeometryConfidence +
                        anchor.GeometryConfidence) * 0.5 + 0.04));
                witness.EvidenceFamilies |= anchor.EvidenceFamilies |
                    AERISRunwayEvidenceFamily.ExternalRunwayWitness;
                witness.Methods |= anchor.Methods |
                    AERISRunwayMeasurementMethod.M27PlanWitness;
                witness.AnchorScanValid = true;
                witness.AnchorConnectedPrimitiveCount = anchor.AnchorConnectedPrimitiveCount;
                witness.AnchorCrossSectionCount = anchor.AnchorCrossSectionCount;
                witness.AnchorStableCrossSectionRatio =
                    anchor.AnchorStableCrossSectionRatio;
                witness.AnchorWidthMedianMeters = anchor.AnchorWidthMedianMeters;
                witness.AnchorWidthSpreadMeters = anchor.AnchorWidthSpreadMeters;
                witness.AnchorScanDetail = anchor.AnchorScanDetail;
                AddOrReplace(output, witness);
                detail = "PLAN_WITNESS_MATCH; " + witness.PlanWitnessDetail;
                return;
            }

            // A weak, non-conflicting plan match cannot promote the plan coordinates.
            // Keep the physically connected runway and record the witness as advisory.
            anchor.PlanWitnessCompared = true;
            anchor.PlanWitnessMatched = false;
            anchor.PlanWitnessCenterErrorMeters = witness.PlanWitnessCenterErrorMeters;
            anchor.PlanWitnessHeadingErrorDeg = witness.PlanWitnessHeadingErrorDeg;
            anchor.PlanWitnessLengthRatio = witness.PlanWitnessLengthRatio;
            anchor.PlanWitnessDetail = "PLAN WITNESS ADVISORY ONLY; " +
                witness.PlanWitnessDetail;
            anchor.CertificationBasisDetail += "; KRAMAX WITNESS NOT STRONG ENOUGH TO MOVE AXIS";
            AddOrReplace(output, anchor);
            detail = "ANCHOR_SCAN CERTIFIED; PLAN WITNESS ADVISORY; " +
                witness.PlanWitnessDetail;
        }

        static bool TryBuildAnchorSurfaceCandidate(AERISRunwaySurveySnapshot snapshot,
            out AERISRunwayAxisCandidate candidate, out string detail)
        {
            candidate = null;
            detail = string.Empty;
            if (snapshot == null || !snapshot.AbsolutePlacementConstraintAvailable ||
                !Finite(snapshot.LaunchAnchorEastMeters) ||
                !Finite(snapshot.LaunchAnchorNorthMeters) ||
                !Finite(snapshot.LaunchAnchorHeadingDeg))
            {
                detail = "ANCHOR SURFACE SCAN UNAVAILABLE — FINITE LAUNCH ANCHOR REQUIRED";
                return false;
            }

            var headings = new List<double>();
            AddHeading(headings, snapshot.LaunchAnchorHeadingDeg);
            // Runway mesh children may be rotated relative to the launch transform.
            // Include only primitive axes whose rectangle is actually close to the anchor.
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if (!EligibleSurfacePrimitive(primitive)) continue;
                double distance = DistanceToPrimitiveRectangle(primitive,
                    snapshot.LaunchAnchorEastMeters, snapshot.LaunchAnchorNorthMeters);
                double gate = Math.Max(20.0,
                    Math.Min(120.0, primitive.WidthMeters * 0.75 + 15.0));
                if (distance > gate) continue;
                AddHeading(headings, Math.Atan2(primitive.AxisEast,
                    primitive.AxisNorth) * RadToDeg);
            }

            double bestScore = double.NegativeInfinity;
            string bestReject = string.Empty;
            for (int i = 0; i < headings.Count; i++)
            {
                double radians = headings[i] * DegToRad;
                double axisEast = Math.Sin(radians);
                double axisNorth = Math.Cos(radians);
                AERISRunwayAxisCandidate local;
                string reject;
                if (!TryBuildAnchorCandidateForAxis(snapshot, axisEast, axisNorth,
                    out local, out reject))
                {
                    if (!string.IsNullOrEmpty(reject)) bestReject = reject;
                    continue;
                }
                double score = CandidateScore(local) +
                    local.AnchorStableCrossSectionRatio * 0.20 -
                    Math.Min(0.20, Math.Abs(local.LaunchCrossTrackBeforeMeters) /
                        Math.Max(10.0, local.WidthMeters) * 0.10);
                if (score <= bestScore) continue;
                bestScore = score;
                candidate = local;
            }
            if (candidate == null)
            {
                detail = string.IsNullOrEmpty(bestReject)
                    ? "ANCHOR SURFACE SCAN FOUND NO CONNECTED STRAIGHT CONSTANT-WIDTH CORRIDOR"
                    : bestReject;
                return false;
            }
            detail = candidate.AnchorScanDetail;
            return true;
        }

        static bool TryBuildAnchorCandidateForAxis(AERISRunwaySurveySnapshot snapshot,
            double axisEast, double axisNorth, out AERISRunwayAxisCandidate candidate,
            out string detail)
        {
            candidate = null;
            detail = string.Empty;
            double magnitude = Math.Sqrt(axisEast * axisEast + axisNorth * axisNorth);
            if (magnitude < 1e-9) return false;
            axisEast /= magnitude;
            axisNorth /= magnitude;
            double normalEast = -axisNorth;
            double normalNorth = axisEast;
            double anchorAlong = snapshot.LaunchAnchorEastMeters * axisEast +
                snapshot.LaunchAnchorNorthMeters * axisNorth;
            double anchorAcross = snapshot.LaunchAnchorEastMeters * normalEast +
                snapshot.LaunchAnchorNorthMeters * normalNorth;

            var projections = new List<PrimitiveProjection>();
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if (!EligibleSurfacePrimitive(primitive)) continue;
                double dot = Math.Abs(primitive.AxisEast * axisEast +
                    primitive.AxisNorth * axisNorth);
                if (dot < Math.Cos(18.0 * DegToRad)) continue;
                double side = Math.Sqrt(Math.Max(0.0, 1.0 - dot * dot));
                double alongLength = primitive.LengthMeters * dot +
                    primitive.WidthMeters * side;
                double acrossWidth = primitive.WidthMeters * dot +
                    primitive.LengthMeters * side;
                double along = primitive.CenterEast * axisEast +
                    primitive.CenterNorth * axisNorth;
                double across = primitive.CenterEast * normalEast +
                    primitive.CenterNorth * normalNorth;
                double lateralDistance = Math.Abs(across - anchorAcross) -
                    acrossWidth * 0.5;
                double longitudinalDistance = Math.Abs(along - anchorAlong) -
                    alongLength * 0.5;
                double lateralGate = Math.Max(18.0,
                    Math.Min(120.0, acrossWidth * 0.80 + 12.0));
                // Fragments may be connected longitudinally, but a nearby parallel
                // runway/taxiway must not enter the anchor component.
                if (lateralDistance > lateralGate) continue;
                if (longitudinalDistance > Math.Max(180.0,
                    snapshot.MinimumLengthMeters * 0.75)) continue;
                projections.Add(new PrimitiveProjection
                {
                    Primitive = primitive,
                    Along = along,
                    Across = across,
                    AlongHalf = Math.Max(0.5, alongLength * 0.5),
                    AcrossHalf = Math.Max(0.5, acrossWidth * 0.5),
                    Alignment = dot,
                    Weight = Math.Max(1.0, alongLength),
                    MarkerOnly = false
                });
            }
            if (projections.Count == 0)
            {
                detail = "ANCHOR SURFACE SCAN: NO ELIGIBLE PRIMITIVE NEAR LAUNCH ANCHOR";
                return false;
            }

            // Start only from a physical/semantic surface that actually touches the
            // launch anchor, then flood through two-dimensionally adjacent primitives.
            // A longitudinal-only merge can accidentally join a remote apron, road or
            // parallel strip that happens to overlap the same along-track interval.
            bool anchorColliderContact = HasAnchorColliderContact(snapshot);
            var seeds = new List<int>();
            for (int i = 0; i < projections.Count; i++)
            {
                if (IsAnchorSeedProjection(projections[i], snapshot,
                    anchorColliderContact)) seeds.Add(i);
            }
            if (seeds.Count == 0)
            {
                detail = "ANCHOR SURFACE SCAN: NO COLLIDER-BACKED OR EXPLICIT RUNWAY SURFACE TOUCHES LAUNCH ANCHOR";
                return false;
            }

            var visited = new bool[projections.Count];
            var queue = new Queue<int>();
            for (int i = 0; i < seeds.Count; i++)
            {
                int index = seeds[i];
                if (visited[index]) continue;
                visited[index] = true;
                queue.Enqueue(index);
            }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                for (int i = 0; i < projections.Count; i++)
                {
                    if (visited[i] || !AnchorSurfaceProjectionsConnect(
                        projections[current], projections[i])) continue;
                    visited[i] = true;
                    queue.Enqueue(i);
                }
            }
            var connected = new List<PrimitiveProjection>();
            for (int i = 0; i < projections.Count; i++)
                if (visited[i]) connected.Add(projections[i]);
            if (connected.Count == 0)
            {
                detail = "ANCHOR SURFACE SCAN: ANCHOR-CONNECTED COMPONENT IS EMPTY";
                return false;
            }

            double componentStart = double.PositiveInfinity;
            double componentEnd = double.NegativeInfinity;
            for (int i = 0; i < connected.Count; i++)
            {
                componentStart = Math.Min(componentStart,
                    connected[i].Along - connected[i].AlongHalf);
                componentEnd = Math.Max(componentEnd,
                    connected[i].Along + connected[i].AlongHalf);
            }
            if (!Finite(componentStart) || !Finite(componentEnd) ||
                componentEnd - componentStart < snapshot.MinimumLengthMeters)
            {
                detail = "ANCHOR SURFACE SCAN: CONNECTED COMPONENT TOO SHORT";
                return false;
            }

            double spacing = 25.0;
            int sampleCount = Math.Max(3, (int)Math.Ceiling(
                (componentEnd - componentStart) / spacing) + 1);
            var sections = new List<SurfaceSection>();
            for (int i = 0; i < sampleCount; i++)
            {
                double along = Math.Min(componentEnd, componentStart + i * spacing);
                SurfaceSection section;
                if (TryMeasureSection(connected, along, anchorAcross,
                    snapshot.MinimumWidthMeters, snapshot.MaximumWidthMeters,
                    out section)) sections.Add(section);
            }
            if (sections.Count < 3)
            {
                detail = "ANCHOR SURFACE SCAN: FEWER THAN THREE VALID CROSS-SECTIONS";
                return false;
            }

            var widths = new List<double>();
            for (int i = 0; i < sections.Count; i++) widths.Add(sections[i].Width);
            double medianWidth = Median(widths);
            var deviations = new List<double>();
            for (int i = 0; i < widths.Count; i++)
                deviations.Add(Math.Abs(widths[i] - medianWidth));
            double spread = Median(deviations);
            double lowWidth = Math.Max(snapshot.MinimumWidthMeters,
                medianWidth * 0.62);
            double highWidth = Math.Min(snapshot.MaximumWidthMeters,
                Math.Max(medianWidth * 1.45, medianWidth + 12.0));

            int bestStart = -1;
            int bestEnd = -1;
            int runStart = -1;
            for (int i = 0; i <= sections.Count; i++)
            {
                bool stable = i < sections.Count &&
                    sections[i].Width >= lowWidth && sections[i].Width <= highWidth &&
                    Math.Abs(sections[i].AcrossCenter - anchorAcross) <=
                        Math.Max(30.0, sections[i].Width * 0.60);
                if (stable && runStart < 0) runStart = i;
                if ((!stable || i == sections.Count) && runStart >= 0)
                {
                    int runEnd = i - 1;
                    double startAlong = sections[runStart].Along;
                    double endAlong = sections[runEnd].Along;
                    double distance = anchorAlong < startAlong ? startAlong - anchorAlong :
                        (anchorAlong > endAlong ? anchorAlong - endAlong : 0.0);
                    bool reachesAnchor = distance <= Math.Max(75.0, medianWidth * 1.5);
                    int length = runEnd - runStart;
                    int bestLength = bestEnd - bestStart;
                    if (reachesAnchor && length > bestLength)
                    {
                        bestStart = runStart;
                        bestEnd = runEnd;
                    }
                    runStart = -1;
                }
            }
            if (bestStart < 0 || bestEnd <= bestStart)
            {
                detail = "ANCHOR SURFACE SCAN: NO STABLE-WIDTH STRAIGHT SECTION REACHES LAUNCH ANCHOR";
                return false;
            }

            double physicalStart = Math.Max(componentStart,
                sections[bestStart].Along - spacing * 0.5);
            double physicalEnd = Math.Min(componentEnd,
                sections[bestEnd].Along + spacing * 0.5);
            double physicalLength = physicalEnd - physicalStart;
            if (physicalLength < snapshot.MinimumLengthMeters ||
                physicalLength > snapshot.MaximumLengthMeters)
            {
                detail = "ANCHOR SURFACE SCAN: STABLE SECTION LENGTH OUTSIDE LIMITS " +
                    Format(physicalLength) + "M";
                return false;
            }

            var centerValues = new List<double>();
            var upValues = new List<double>();
            AERISRunwayEvidenceFamily evidence =
                AERISRunwayEvidenceFamily.GeometryTopology |
                AERISRunwayEvidenceFamily.MetadataSemantic |
                AERISRunwayEvidenceFamily.OperationalLayout;
            AERISRunwayMeasurementMethod methods =
                AERISRunwayMeasurementMethod.M04Collider |
                AERISRunwayMeasurementMethod.M07LongSurfaceStrip |
                AERISRunwayMeasurementMethod.M08SurfaceFlatness |
                AERISRunwayMeasurementMethod.M09LongitudinalProfile |
                AERISRunwayMeasurementMethod.M24CrossSectionVoting |
                AERISRunwayMeasurementMethod.M28AnchorSurfaceScan;
            int sectionPrimitives = 0;
            for (int i = bestStart; i <= bestEnd; i++)
            {
                centerValues.Add(sections[i].AcrossCenter);
                upValues.Add(sections[i].Up);
                sectionPrimitives += sections[i].PrimitiveCount;
                evidence |= sections[i].Evidence;
                methods |= sections[i].Methods;
            }
            double acrossCenter = Median(centerValues);
            double centerUp = Median(upValues);
            double anchorCross = anchorAcross - acrossCenter;
            if (Math.Abs(anchorCross) > Math.Max(25.0, medianWidth * 0.65))
            {
                detail = "ANCHOR SURFACE SCAN: LAUNCH ANCHOR IS OUTSIDE STABLE RUNWAY WIDTH; cross=" +
                    Format(anchorCross) + "M width=" + Format(medianWidth) + "M";
                return false;
            }
            if (snapshot.PqsSampled)
            {
                evidence |= AERISRunwayEvidenceFamily.SurfaceElevationTerrain;
                methods |= AERISRunwayMeasurementMethod.M18PqsArtificialSurface;
            }
            double stableRatio = (bestEnd - bestStart + 1) /
                (double)Math.Max(1, sampleCount);
            double coverage = Math.Min(1.0, sections.Count /
                (double)Math.Max(1, sampleCount));
            double widthStability = Clamp01(1.0 - spread /
                Math.Max(3.0, medianWidth * 0.30));
            double geometryConfidence = Clamp01(0.84 +
                Math.Min(0.08, coverage * 0.08) +
                Math.Min(0.05, stableRatio * 0.05) +
                widthStability * 0.03);
            double classificationConfidence = Clamp01(0.92 +
                stableRatio * 0.04 + widthStability * 0.03);
            double safetyMargin = Math.Max(2.0,
                Math.Min(18.0, physicalLength * 0.01 + spread));
            double usableStart = physicalStart + safetyMargin;
            double usableEnd = physicalEnd - safetyMargin;
            if (usableEnd - usableStart < snapshot.MinimumLengthMeters)
            {
                detail = "ANCHOR SURFACE SCAN: SAFETY MARGINS LEAVE INSUFFICIENT LENGTH";
                return false;
            }

            double centerAlong = (physicalStart + physicalEnd) * 0.5;
            double heading = NormalizeHeading360(Math.Atan2(axisEast, axisNorth) * RadToDeg);
            double headingError = AngleDifference180(heading,
                snapshot.LaunchAnchorHeadingDeg);
            candidate = new AERISRunwayAxisCandidate
            {
                CenterEast = axisEast * centerAlong + normalEast * acrossCenter,
                CenterNorth = axisNorth * centerAlong + normalNorth * acrossCenter,
                CenterUp = centerUp,
                AxisEast = axisEast,
                AxisNorth = axisNorth,
                PhysicalStartMeters = physicalStart,
                PhysicalEndMeters = physicalEnd,
                UsableStartMeters = usableStart,
                UsableEndMeters = usableEnd,
                OperationalThresholdA = usableStart,
                OperationalThresholdB = usableEnd,
                WidthMeters = medianWidth,
                LengthMeters = usableEnd - usableStart,
                HeadingDeg = heading,
                ClassificationConfidence = classificationConfidence,
                GeometryConfidence = geometryConfidence,
                CenterlineUncertaintyMeters = Math.Max(0.35,
                    Math.Min(medianWidth * 0.18, spread + 0.75)),
                HeadingUncertaintyDeg = Math.Max(0.10,
                    Math.Min(2.0, 1.2 - widthStability * 0.8)),
                PhysicalEndUncertaintyMeters = Math.Max(1.0, spacing * 0.5),
                UsableEndUncertaintyMeters = Math.Max(1.0, safetyMargin * 0.6),
                ThresholdUncertaintyMeters = Math.Max(1.0, safetyMargin * 0.7),
                LengthUncertaintyMeters = Math.Max(2.0, spacing),
                WidthUncertaintyMeters = Math.Max(0.75, spread + 0.5),
                ElevationUncertaintyMeters = snapshot.PqsSampled ? 0.75 : 2.0,
                DisplacedThresholdConfidence = 0.72,
                ApproachCorridorConfidence = 0.0,
                AbsolutePlacementValid = true,
                LaunchConstraintApplied = false,
                LaunchCrossTrackBeforeMeters = anchorCross,
                LaunchCrossTrackAfterMeters = anchorCross,
                LaunchAlongTrackMeters = anchorAlong,
                LaunchHeadingErrorDeg = headingError,
                AbsoluteTranslationMeters = 0.0,
                AxisRegistrationValid = true,
                MeshSurfaceHeadingDeg = heading,
                RegisteredHeadingBeforeDeg = snapshot.LaunchAnchorHeadingDeg,
                RegisteredHeadingAfterDeg = heading,
                HeadingCorrectionDeg = headingError,
                AxisReferenceErrorDeg = headingError,
                AxisSurfaceAspectRatio = physicalLength / Math.Max(1.0, medianWidth),
                AxisSurfacePointCount = sections.Count,
                AxisRegistrationDetail = "ANCHOR-CONNECTED CROSS-SECTION AXIS",
                AbsolutePlacementDetail = "LAUNCH ANCHOR LIES ON CONNECTED STABLE-WIDTH SURFACE",
                CertificationBasis = AERISRunwayCertificationBasis.AnchorSurfaceScan,
                CertificationBasisDetail = "LAUNCH-ANCHOR-CONNECTED PHYSICAL SURFACE",
                AnchorScanValid = true,
                AnchorConnectedPrimitiveCount = connected.Count,
                AnchorCrossSectionCount = bestEnd - bestStart + 1,
                AnchorStableCrossSectionRatio = stableRatio,
                AnchorWidthMedianMeters = medianWidth,
                AnchorWidthSpreadMeters = spread,
                EvidenceFamilies = evidence,
                Methods = methods,
                ApproachAAvailable = snapshot.ApproachAAvailable,
                ApproachBAvailable = snapshot.ApproachBAvailable
            };
            candidate.AnchorScanDetail = "ANCHOR_SCAN valid=True; primitives=" +
                connected.Count + "; sectionPrimitiveVotes=" + sectionPrimitives +
                "; sections=" + candidate.AnchorCrossSectionCount + "/" + sampleCount +
                "; stableRatio=" + Format(stableRatio) + "; widthMedian=" +
                Format(medianWidth) + "m; widthSpread=" + Format(spread) +
                "m; anchorCross=" + Format(anchorCross) + "m; length=" +
                Format(candidate.LengthMeters) + "m; heading=" + Format(heading) + "deg";
            candidate.Detail = candidate.AnchorScanDetail;
            return true;
        }

        static bool TryBuildWitnessCandidate(AERISRunwaySurveySnapshot snapshot,
            AERISRunwayAxisCandidate anchor, out AERISRunwayAxisCandidate candidate,
            out string detail)
        {
            candidate = null;
            detail = string.Empty;
            if (snapshot == null || !snapshot.RunwayWitnessAvailable ||
                !Finite(snapshot.RunwayWitnessStartEastMeters) ||
                !Finite(snapshot.RunwayWitnessStartNorthMeters) ||
                !Finite(snapshot.RunwayWitnessEndEastMeters) ||
                !Finite(snapshot.RunwayWitnessEndNorthMeters))
            {
                detail = "RUNWAY WITNESS COORDINATES MISSING/NON-FINITE";
                return false;
            }
            double deltaEast = snapshot.RunwayWitnessEndEastMeters -
                snapshot.RunwayWitnessStartEastMeters;
            double deltaNorth = snapshot.RunwayWitnessEndNorthMeters -
                snapshot.RunwayWitnessStartNorthMeters;
            double length = Math.Sqrt(deltaEast * deltaEast + deltaNorth * deltaNorth);
            if (!Finite(length) || length < Math.Max(80.0,
                snapshot.MinimumLengthMeters * 0.40))
            {
                detail = "RUNWAY WITNESS ENDPOINTS TOO CLOSE";
                return false;
            }
            double axisEast = deltaEast / length;
            double axisNorth = deltaNorth / length;
            double normalEast = -axisNorth;
            double normalNorth = axisEast;
            double startAlong = snapshot.RunwayWitnessStartEastMeters * axisEast +
                snapshot.RunwayWitnessStartNorthMeters * axisNorth;
            double endAlong = snapshot.RunwayWitnessEndEastMeters * axisEast +
                snapshot.RunwayWitnessEndNorthMeters * axisNorth;
            if (endAlong < startAlong)
            {
                double swap = startAlong;
                startAlong = endAlong;
                endAlong = swap;
                axisEast = -axisEast;
                axisNorth = -axisNorth;
                normalEast = -axisNorth;
                normalNorth = axisEast;
            }
            double startAcross = snapshot.RunwayWitnessStartEastMeters * normalEast +
                snapshot.RunwayWitnessStartNorthMeters * normalNorth;
            double endAcross = snapshot.RunwayWitnessEndEastMeters * normalEast +
                snapshot.RunwayWitnessEndNorthMeters * normalNorth;
            double acrossCenter = (startAcross + endAcross) * 0.5;
            double centerUp = (snapshot.RunwayWitnessStartUpMeters +
                snapshot.RunwayWitnessEndUpMeters) * 0.5;

            double width;
            double spread;
            int sections;
            int primitiveVotes;
            double corridorCoverage;
            AERISRunwayEvidenceFamily physicalEvidence;
            AERISRunwayMeasurementMethod physicalMethods;
            string corridorDetail;
            MeasureWitnessCorridor(snapshot, axisEast, axisNorth, startAlong, endAlong,
                acrossCenter, out width, out spread, out sections, out primitiveVotes,
                out corridorCoverage, out physicalEvidence, out physicalMethods,
                out corridorDetail);
            if ((!Finite(width) || width < snapshot.MinimumWidthMeters) && anchor != null)
            {
                width = anchor.WidthMeters;
                spread = anchor.WidthUncertaintyMeters;
                sections = anchor.AnchorCrossSectionCount;
                primitiveVotes = anchor.AnchorConnectedPrimitiveCount;
                corridorCoverage = Math.Max(corridorCoverage,
                    anchor.AnchorStableCrossSectionRatio);
                physicalEvidence |= anchor.EvidenceFamilies;
                physicalMethods |= anchor.Methods;
            }
            if (!Finite(width) || width < snapshot.MinimumWidthMeters)
            {
                if (!snapshot.RunwayWitnessUserCalibrated)
                {
                    detail = "KRAMAX WITNESS HAS NO PHYSICAL CORRIDOR COVERAGE; " +
                        corridorDetail;
                    return false;
                }
                width = snapshot.DeclaredWidthMeters >= snapshot.MinimumWidthMeters &&
                    snapshot.DeclaredWidthMeters <= snapshot.MaximumWidthMeters
                    ? snapshot.DeclaredWidthMeters : Math.Max(snapshot.MinimumWidthMeters, 45.0);
                spread = Math.Max(2.0, width * 0.20);
            }
            width = Math.Max(snapshot.MinimumWidthMeters,
                Math.Min(snapshot.MaximumWidthMeters, width));

            double geometryConfidence = snapshot.RunwayWitnessUserCalibrated
                ? 0.96 : Clamp01(0.82 + corridorCoverage * 0.14);
            double classificationConfidence = snapshot.RunwayWitnessUserCalibrated
                ? 0.99 : Clamp01(0.91 + corridorCoverage * 0.07);
            AERISRunwayEvidenceFamily evidence =
                AERISRunwayEvidenceFamily.MetadataSemantic |
                AERISRunwayEvidenceFamily.GeometryTopology |
                AERISRunwayEvidenceFamily.OperationalLayout |
                AERISRunwayEvidenceFamily.ExternalRunwayWitness | physicalEvidence;
            AERISRunwayMeasurementMethod methods =
                AERISRunwayMeasurementMethod.M01Metadata |
                AERISRunwayMeasurementMethod.M20ReciprocalConsistency |
                AERISRunwayMeasurementMethod.M27PlanWitness | physicalMethods;
            if (snapshot.RunwayWitnessUserCalibrated)
            {
                evidence |= AERISRunwayEvidenceFamily.UserCalibration;
                methods |= AERISRunwayMeasurementMethod.M29UserCalibration;
            }
            if (snapshot.PqsSampled)
            {
                evidence |= AERISRunwayEvidenceFamily.SurfaceElevationTerrain;
                methods |= AERISRunwayMeasurementMethod.M18PqsArtificialSurface;
            }
            double margin = snapshot.RunwayWitnessUserCalibrated ? 0.0 :
                Math.Max(2.0, Math.Min(15.0, length * 0.01));
            double usableStart = startAlong + margin;
            double usableEnd = endAlong - margin;
            if (usableEnd - usableStart < Math.Max(80.0,
                snapshot.MinimumLengthMeters * 0.40))
            {
                detail = "RUNWAY WITNESS USABLE SEGMENT TOO SHORT AFTER SAFETY MARGIN";
                return false;
            }
            double centerAlong = (startAlong + endAlong) * 0.5;
            double heading = NormalizeHeading360(Math.Atan2(axisEast, axisNorth) * RadToDeg);
            candidate = new AERISRunwayAxisCandidate
            {
                CenterEast = axisEast * centerAlong + normalEast * acrossCenter,
                CenterNorth = axisNorth * centerAlong + normalNorth * acrossCenter,
                CenterUp = centerUp,
                AxisEast = axisEast,
                AxisNorth = axisNorth,
                PhysicalStartMeters = startAlong,
                PhysicalEndMeters = endAlong,
                UsableStartMeters = usableStart,
                UsableEndMeters = usableEnd,
                OperationalThresholdA = usableStart,
                OperationalThresholdB = usableEnd,
                WidthMeters = width,
                LengthMeters = usableEnd - usableStart,
                HeadingDeg = heading,
                ClassificationConfidence = classificationConfidence,
                GeometryConfidence = geometryConfidence,
                CenterlineUncertaintyMeters = snapshot.RunwayWitnessUserCalibrated
                    ? 1.5 : Math.Max(3.0, Math.Min(35.0,
                        snapshot.RunwayWitnessMatchDistanceMeters * 0.02 + spread)),
                HeadingUncertaintyDeg = snapshot.RunwayWitnessUserCalibrated ? 0.20 : 0.75,
                PhysicalEndUncertaintyMeters = snapshot.RunwayWitnessUserCalibrated ? 2.0 : 12.0,
                UsableEndUncertaintyMeters = snapshot.RunwayWitnessUserCalibrated ? 2.0 : 15.0,
                ThresholdUncertaintyMeters = snapshot.RunwayWitnessUserCalibrated ? 2.0 : 15.0,
                LengthUncertaintyMeters = snapshot.RunwayWitnessUserCalibrated ? 3.0 : 25.0,
                WidthUncertaintyMeters = Math.Max(1.0, spread),
                ElevationUncertaintyMeters = snapshot.RunwayWitnessUserCalibrated ? 1.0 : 3.0,
                DisplacedThresholdConfidence = snapshot.RunwayWitnessUserCalibrated ? 0.98 : 0.85,
                ApproachCorridorConfidence = 0.0,
                AbsolutePlacementValid = true,
                AxisRegistrationValid = true,
                MeshSurfaceHeadingDeg = anchor == null ? double.NaN : anchor.HeadingDeg,
                RegisteredHeadingBeforeDeg = snapshot.RunwayWitnessHeadingDeg,
                RegisteredHeadingAfterDeg = heading,
                HeadingCorrectionDeg = 0.0,
                AxisReferenceErrorDeg = 0.0,
                AxisRegistrationDetail = snapshot.RunwayWitnessUserCalibrated
                    ? "USER BODY-FIXED GEODETIC A/B AXIS — NO MESH/ANCHOR REALIGNMENT"
                    : "RW/STOP TWO-POINT WITNESS AXIS",
                AbsolutePlacementDetail = snapshot.RunwayWitnessUserCalibrated
                    ? "BODY-FIXED ABSOLUTE LAT/LON/ALT ENDPOINT AUTHORITY"
                    : "ABSOLUTE GEO ENDPOINTS FROM " + snapshot.RunwayWitnessSource,
                CertificationBasis = snapshot.RunwayWitnessUserCalibrated
                    ? AERISRunwayCertificationBasis.UserCalibrated
                    : AERISRunwayCertificationBasis.PlanWitness,
                CertificationBasisDetail = snapshot.RunwayWitnessUserCalibrated
                    ? "USER TWO-ENDPOINT PHYSICAL RUNWAY; RECIPROCAL DIRECTIONS GENERATED"
                    : "KRAMAX RW/STOP PLAN WITNESS",
                AnchorScanValid = corridorCoverage > 0.0,
                AnchorConnectedPrimitiveCount = primitiveVotes,
                AnchorCrossSectionCount = sections,
                AnchorStableCrossSectionRatio = corridorCoverage,
                AnchorWidthMedianMeters = width,
                AnchorWidthSpreadMeters = spread,
                EvidenceFamilies = evidence,
                Methods = methods,
                ApproachAAvailable = snapshot.RunwayWitnessUserCalibrated ||
                    snapshot.ApproachAAvailable,
                ApproachBAvailable = snapshot.RunwayWitnessUserCalibrated ||
                    snapshot.ApproachBAvailable
            };
            candidate.AnchorScanDetail = corridorDetail;
            candidate.Detail = "WITNESS source=" + snapshot.RunwayWitnessSource +
                "; name=" + snapshot.RunwayWitnessName + "; length=" +
                Format(length) + "m; heading=" + Format(heading) + "deg; coverage=" +
                Format(corridorCoverage) + "; width=" + Format(width) + "m" +
                (snapshot.RunwayWitnessUserCalibrated
                    ? "; coordinateAuthority=BODY_FIXED_GEODETIC_ABSOLUTE" : string.Empty);
            detail = candidate.Detail + "; " + corridorDetail;
            return true;
        }

        static void MeasureWitnessCorridor(AERISRunwaySurveySnapshot snapshot,
            double axisEast, double axisNorth, double startAlong, double endAlong,
            double acrossCenter, out double width, out double spread, out int sections,
            out int primitiveVotes, out double coverage,
            out AERISRunwayEvidenceFamily evidence,
            out AERISRunwayMeasurementMethod methods, out string detail)
        {
            width = double.NaN;
            spread = double.NaN;
            sections = 0;
            primitiveVotes = 0;
            coverage = 0.0;
            evidence = AERISRunwayEvidenceFamily.None;
            methods = AERISRunwayMeasurementMethod.None;
            detail = "WITNESS CORRIDOR NOT MEASURED";
            double normalEast = -axisNorth;
            double normalNorth = axisEast;
            var projections = new List<PrimitiveProjection>();
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if (!EligibleSurfacePrimitive(primitive)) continue;
                double dot = Math.Abs(primitive.AxisEast * axisEast +
                    primitive.AxisNorth * axisNorth);
                if (dot < Math.Cos(20.0 * DegToRad)) continue;
                double side = Math.Sqrt(Math.Max(0.0, 1.0 - dot * dot));
                double alongLength = primitive.LengthMeters * dot +
                    primitive.WidthMeters * side;
                double acrossWidth = primitive.WidthMeters * dot +
                    primitive.LengthMeters * side;
                double along = primitive.CenterEast * axisEast +
                    primitive.CenterNorth * axisNorth;
                double across = primitive.CenterEast * normalEast +
                    primitive.CenterNorth * normalNorth;
                if (along + alongLength * 0.5 < startAlong - 50.0 ||
                    along - alongLength * 0.5 > endAlong + 50.0) continue;
                if (Math.Abs(across - acrossCenter) - acrossWidth * 0.5 >
                    Math.Max(30.0, acrossWidth * 0.75)) continue;
                projections.Add(new PrimitiveProjection
                {
                    Primitive = primitive,
                    Along = along,
                    Across = across,
                    AlongHalf = Math.Max(0.5, alongLength * 0.5),
                    AcrossHalf = Math.Max(0.5, acrossWidth * 0.5),
                    Alignment = dot,
                    Weight = Math.Max(1.0, alongLength)
                });
            }
            if (projections.Count == 0) return;
            double spacing = 25.0;
            int total = Math.Max(2, (int)Math.Ceiling((endAlong - startAlong) / spacing) + 1);
            var widths = new List<double>();
            for (int i = 0; i < total; i++)
            {
                double along = Math.Min(endAlong, startAlong + i * spacing);
                SurfaceSection section;
                if (!TryMeasureSection(projections, along, acrossCenter,
                    snapshot.MinimumWidthMeters, snapshot.MaximumWidthMeters,
                    out section)) continue;
                widths.Add(section.Width);
                primitiveVotes += section.PrimitiveCount;
                evidence |= section.Evidence;
                methods |= section.Methods;
            }
            sections = widths.Count;
            coverage = widths.Count / (double)Math.Max(1, total);
            if (widths.Count == 0) return;
            width = Median(widths);
            var deviations = new List<double>();
            for (int i = 0; i < widths.Count; i++)
                deviations.Add(Math.Abs(widths[i] - width));
            spread = Median(deviations);
            methods |= AERISRunwayMeasurementMethod.M24CrossSectionVoting |
                AERISRunwayMeasurementMethod.M28AnchorSurfaceScan;
            evidence |= AERISRunwayEvidenceFamily.GeometryTopology;
            detail = "WITNESS_CORRIDOR sections=" + sections + "/" + total +
                "; coverage=" + Format(coverage) + "; width=" + Format(width) +
                "m; spread=" + Format(spread) + "m; primitiveVotes=" + primitiveVotes;
        }

        static bool TryMeasureSection(List<PrimitiveProjection> projections,
            double along, double referenceAcross, double minimumWidth,
            double maximumWidth, out SurfaceSection section)
        {
            section = null;
            var intervals = new List<Interval>();
            var contributing = new List<PrimitiveProjection>();
            for (int i = 0; i < projections.Count; i++)
            {
                PrimitiveProjection value = projections[i];
                if (value == null || value.MarkerOnly ||
                    Math.Abs(along - value.Along) > value.AlongHalf + 1.0) continue;
                intervals.Add(new Interval
                {
                    Start = value.Across - value.AcrossHalf,
                    End = value.Across + value.AcrossHalf
                });
                contributing.Add(value);
            }
            if (intervals.Count == 0) return false;
            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            var merged = new List<Interval>();
            for (int i = 0; i < intervals.Count; i++)
            {
                if (merged.Count == 0 || intervals[i].Start >
                    merged[merged.Count - 1].End + 3.0)
                {
                    merged.Add(new Interval { Start = intervals[i].Start,
                        End = intervals[i].End });
                }
                else
                {
                    merged[merged.Count - 1].End = Math.Max(
                        merged[merged.Count - 1].End, intervals[i].End);
                }
            }
            Interval best = null;
            double bestDistance = double.PositiveInfinity;
            for (int i = 0; i < merged.Count; i++)
            {
                double distance = referenceAcross < merged[i].Start
                    ? merged[i].Start - referenceAcross
                    : (referenceAcross > merged[i].End
                        ? referenceAcross - merged[i].End : 0.0);
                double intervalWidth = merged[i].End - merged[i].Start;
                if (intervalWidth < Math.Max(2.0, minimumWidth * 0.50) ||
                    intervalWidth > Math.Max(maximumWidth, minimumWidth) * 1.35) continue;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = merged[i];
                }
            }
            if (best == null || bestDistance > Math.Max(25.0,
                (best.End - best.Start) * 0.65)) return false;
            double weightedUp = 0.0;
            double totalWeight = 0.0;
            AERISRunwayEvidenceFamily evidence = AERISRunwayEvidenceFamily.None;
            AERISRunwayMeasurementMethod methods = AERISRunwayMeasurementMethod.None;
            int count = 0;
            for (int i = 0; i < contributing.Count; i++)
            {
                PrimitiveProjection value = contributing[i];
                double primitiveStart = value.Across - value.AcrossHalf;
                double primitiveEnd = value.Across + value.AcrossHalf;
                if (primitiveEnd < best.Start || primitiveStart > best.End) continue;
                double weight = Math.Max(1.0, value.Weight);
                weightedUp += value.Primitive.CenterUp * weight;
                totalWeight += weight;
                evidence |= value.Primitive.EvidenceFamily;
                methods |= value.Primitive.Method;
                count++;
            }
            section = new SurfaceSection
            {
                Along = along,
                AcrossCenter = (best.Start + best.End) * 0.5,
                Width = best.End - best.Start,
                Up = totalWeight > 0.0 ? weightedUp / totalWeight : 0.0,
                PrimitiveCount = count,
                Evidence = evidence,
                Methods = methods
            };
            return Finite(section.Width) && section.Width > 0.0;
        }


        static bool HasAnchorColliderContact(AERISRunwaySurveySnapshot snapshot)
        {
            if (snapshot == null || !snapshot.ColliderReadable) return false;
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if ((primitive.Method & AERISRunwayMeasurementMethod.M04Collider) == 0 ||
                    !FinitePrimitive(primitive)) continue;
                double distance = DistanceToPrimitiveRectangle(primitive,
                    snapshot.LaunchAnchorEastMeters, snapshot.LaunchAnchorNorthMeters);
                double vertical = Math.Max(0.0, Math.Abs(snapshot.LaunchAnchorUpMeters -
                    primitive.CenterUp) - primitive.HeightMeters * 0.5);
                double horizontalGate = Math.Max(5.0, Math.Min(25.0,
                    Math.Min(primitive.LengthMeters, primitive.WidthMeters) * 0.35 + 3.0));
                double verticalGate = Math.Max(3.0,
                    Math.Min(10.0, primitive.HeightMeters * 0.5 + 3.0));
                if (distance <= horizontalGate && vertical <= verticalGate) return true;
            }
            return false;
        }

        static bool IsAnchorSeedProjection(PrimitiveProjection projection,
            AERISRunwaySurveySnapshot snapshot, bool anchorColliderContact)
        {
            if (projection == null || snapshot == null) return false;
            AERISSurveyPrimitive primitive = projection.Primitive;
            AERISSurveySemantic explicitSurface = AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold |
                AERISSurveySemantic.Spawn;
            bool explicitRunway = (primitive.Semantic & explicitSurface) != 0;
            bool collider = (primitive.Method &
                AERISRunwayMeasurementMethod.M04Collider) != 0;
            if (!explicitRunway && !collider && !anchorColliderContact) return false;
            double distance = DistanceToPrimitiveRectangle(primitive,
                snapshot.LaunchAnchorEastMeters, snapshot.LaunchAnchorNorthMeters);
            double shortSide = Math.Min(primitive.LengthMeters, primitive.WidthMeters);
            double horizontalGate = Math.Max(5.0, Math.Min(25.0,
                shortSide * 0.35 + 3.0));
            double vertical = Math.Max(0.0, Math.Abs(snapshot.LaunchAnchorUpMeters -
                primitive.CenterUp) - primitive.HeightMeters * 0.5);
            double verticalGate = Math.Max(3.0,
                Math.Min(10.0, primitive.HeightMeters * 0.5 + 3.0));
            return distance <= horizontalGate && vertical <= verticalGate;
        }

        static bool AnchorSurfaceProjectionsConnect(PrimitiveProjection a,
            PrimitiveProjection b)
        {
            if (a == null || b == null) return false;
            double alongGap = IntervalGap(a.Along - a.AlongHalf,
                a.Along + a.AlongHalf, b.Along - b.AlongHalf, b.Along + b.AlongHalf);
            double acrossGap = IntervalGap(a.Across - a.AcrossHalf,
                a.Across + a.AcrossHalf, b.Across - b.AcrossHalf, b.Across + b.AcrossHalf);
            double shortHalf = Math.Min(a.AcrossHalf, b.AcrossHalf);
            double alongGate = Math.Max(12.0, Math.Min(60.0, shortHalf * 1.5 + 10.0));
            double acrossGate = Math.Max(4.0, Math.Min(18.0, shortHalf * 0.35 + 4.0));
            double verticalGap = Math.Max(0.0,
                Math.Abs(a.Primitive.CenterUp - b.Primitive.CenterUp) -
                (a.Primitive.HeightMeters + b.Primitive.HeightMeters) * 0.5);
            return alongGap <= alongGate && acrossGap <= acrossGate &&
                verticalGap <= 4.0;
        }

        static double IntervalGap(double aStart, double aEnd,
            double bStart, double bEnd)
        {
            if (aEnd < bStart) return bStart - aEnd;
            if (bEnd < aStart) return aStart - bEnd;
            return 0.0;
        }

        static bool EligibleSurfacePrimitive(AERISSurveyPrimitive primitive)
        {
            if (!FinitePrimitive(primitive) || primitive.FlatnessDeg > 8.0) return false;
            AERISSurveySemantic semantic = primitive.Semantic;
            bool explicitRunway = (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold)) != 0;
            bool excluded = (semantic & (AERISSurveySemantic.Apron |
                AERISSurveySemantic.Platform | AERISSurveySemantic.Obstacle |
                AERISSurveySemantic.NaturalSurface | AERISSurveySemantic.ApproachLight)) != 0 &&
                !explicitRunway;
            bool taxiOnly = (semantic & AERISSurveySemantic.Taxiway) != 0 &&
                !explicitRunway;
            if (excluded || taxiOnly) return false;
            double longSide = Math.Max(primitive.LengthMeters, primitive.WidthMeters);
            double shortSide = Math.Min(primitive.LengthMeters, primitive.WidthMeters);
            bool surfaceSemantic = (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Pavement | AERISSurveySemantic.Centerline |
                AERISSurveySemantic.Threshold | AERISSurveySemantic.Spawn)) != 0;
            return surfaceSemantic || (longSide >= 80.0 && shortSide >= 4.0 &&
                longSide / Math.Max(1.0, shortSide) >= 2.0);
        }

        static double DistanceToPrimitiveRectangle(AERISSurveyPrimitive primitive,
            double east, double north)
        {
            double normalEast = -primitive.AxisNorth;
            double normalNorth = primitive.AxisEast;
            double deltaEast = east - primitive.CenterEast;
            double deltaNorth = north - primitive.CenterNorth;
            double along = Math.Abs(deltaEast * primitive.AxisEast +
                deltaNorth * primitive.AxisNorth) - primitive.LengthMeters * 0.5;
            double across = Math.Abs(deltaEast * normalEast +
                deltaNorth * normalNorth) - primitive.WidthMeters * 0.5;
            along = Math.Max(0.0, along);
            across = Math.Max(0.0, across);
            return Math.Sqrt(along * along + across * across);
        }

        static void CompareWitnessAndAnchor(AERISRunwayAxisCandidate witness,
            AERISRunwayAxisCandidate anchor, string label)
        {
            if (witness == null || anchor == null) return;
            witness.PlanWitnessCompared = true;
            witness.PlanWitnessCenterErrorMeters = Distance(witness.CenterEast,
                witness.CenterNorth, anchor.CenterEast, anchor.CenterNorth);
            witness.PlanWitnessHeadingErrorDeg = AngleDifference180(witness.HeadingDeg,
                anchor.HeadingDeg);
            witness.PlanWitnessLengthRatio = anchor.LengthMeters /
                Math.Max(1.0, witness.LengthMeters);
            witness.PlanWitnessMatched = witness.PlanWitnessHeadingErrorDeg <= 12.0 &&
                witness.PlanWitnessCenterErrorMeters <= Math.Max(350.0,
                    witness.LengthMeters * 0.35) &&
                witness.PlanWitnessLengthRatio >= 0.45 &&
                witness.PlanWitnessLengthRatio <= 2.20;
            witness.PlanWitnessDetail = label + "; matched=" +
                witness.PlanWitnessMatched + "; centerError=" +
                Format(witness.PlanWitnessCenterErrorMeters) + "m; headingError=" +
                Format(witness.PlanWitnessHeadingErrorDeg) + "deg; lengthRatio=" +
                Format(witness.PlanWitnessLengthRatio);
        }

        static void AddHeading(List<double> headings, double heading)
        {
            if (headings == null || !Finite(heading)) return;
            heading = NormalizeHeading180(heading);
            for (int i = 0; i < headings.Count; i++)
                if (AngleDifference180(headings[i], heading) <= 2.0) return;
            headings.Add(heading);
        }

        static double Median(List<double> values)
        {
            if (values == null || values.Count == 0) return double.NaN;
            values.Sort();
            int middle = values.Count / 2;
            return (values.Count & 1) == 1 ? values[middle] :
                (values[middle - 1] + values[middle]) * 0.5;
        }

        static string Format(double value)
        {
            return Finite(value) ? value.ToString("0.00",
                System.Globalization.CultureInfo.InvariantCulture) : "NaN";
        }

        static void EvaluateOrientation(AERISRunwaySurveySnapshot snapshot,
            Orientation orientation, out List<AERISRunwayAxisCandidate> output,
            out bool rejectedPlatform, out AERISRunwayFailureCode rejection)
        {
            output = new List<AERISRunwayAxisCandidate>();
            rejectedPlatform = false;
            rejection = AERISRunwayFailureCode.None;
            var projections = new List<PrimitiveProjection>();
            double normalE = -orientation.North;
            double normalN = orientation.East;
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                if (!FinitePrimitive(primitive)) continue;
                double alignment = Math.Abs(primitive.AxisEast * orientation.East +
                    primitive.AxisNorth * orientation.North);
                AERISSurveySemantic semantic = primitive.Semantic;
                bool excluded = (semantic & (AERISSurveySemantic.Apron |
                    AERISSurveySemantic.Platform | AERISSurveySemantic.Obstacle)) != 0 &&
                    (semantic & (AERISSurveySemantic.Runway |
                    AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold)) == 0;
                if (excluded)
                {
                    rejectedPlatform = true;
                    continue;
                }
                if ((semantic & AERISSurveySemantic.ApproachLight) != 0 &&
                    (semantic & AERISSurveySemantic.Runway) == 0) continue;
                if ((semantic & AERISSurveySemantic.Taxiway) != 0 &&
                    (semantic & (AERISSurveySemantic.Runway |
                    AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold)) == 0)
                    continue;
                bool explicitFeature = (semantic & (AERISSurveySemantic.Runway |
                    AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold |
                    AERISSurveySemantic.EdgeLight | AERISSurveySemantic.ApproachLight)) != 0;
                bool transverseThreshold = alignment < Math.Cos(15.0 * DegToRad) &&
                    (semantic & AERISSurveySemantic.Threshold) != 0;
                // Perpendicular members of an X/cross layout must never expand the
                // current candidate's width or endpoints merely because both are named
                // "runway".  A transverse threshold marking is retained as a marker-only
                // observation; it cannot contribute surface extent or width.
                if (alignment < Math.Cos(15.0 * DegToRad) && !transverseThreshold) continue;
                double along = primitive.CenterEast * orientation.East +
                    primitive.CenterNorth * orientation.North;
                double across = primitive.CenterEast * normalE +
                    primitive.CenterNorth * normalN;
                double alignedLength = primitive.LengthMeters * alignment +
                    primitive.WidthMeters * Math.Sqrt(Math.Max(0.0, 1.0 - alignment * alignment));
                double alignedWidth = primitive.WidthMeters * alignment +
                    primitive.LengthMeters * Math.Sqrt(Math.Max(0.0, 1.0 - alignment * alignment));
                double aspect = alignedWidth > 0.1 ? alignedLength / alignedWidth : alignedLength;
                if (aspect < 1.7 && !explicitFeature) continue;
                double weight = Math.Max(0.10, primitive.LengthMeters) *
                    (0.25 + 0.75 * alignment);
                if (explicitFeature) weight *= 1.65;
                projections.Add(new PrimitiveProjection
                {
                    Primitive = primitive,
                    Along = along,
                    Across = across,
                    AlongHalf = Math.Max(0.5, alignedLength * 0.5),
                    AcrossHalf = Math.Max(0.5, alignedWidth * 0.5),
                    Alignment = alignment,
                    Weight = transverseThreshold ? Math.Min(weight, 5.0) : weight,
                    MarkerOnly = transverseThreshold
                });
            }
            if (projections.Count == 0) return;
            projections.Sort((a, b) => a.Across.CompareTo(b.Across));
            List<Band> bands = BuildBands(projections, snapshot);
            for (int i = 0; i < bands.Count; i++)
            {
                AERISRunwayAxisCandidate candidate;
                bool platform;
                AERISRunwayFailureCode localRejection;
                if (TryBuildCandidate(snapshot, orientation, bands[i], out candidate,
                    out platform, out localRejection)) output.Add(candidate);
                if (platform) rejectedPlatform = true;
                rejection = PreferFailure(rejection, localRejection);
            }
        }

        static List<Band> BuildBands(List<PrimitiveProjection> projections,
            AERISRunwaySurveySnapshot snapshot)
        {
            var bands = new List<Band>();
            double declaredWidth = snapshot.DeclaredWidthMeters > 1.0
                ? snapshot.DeclaredWidthMeters : 45.0;
            // Cluster fragments of one strip without swallowing a nearby parallel
            // runway.  The previous 1.75-width gate could merge 45 m runways whose
            // centerlines were roughly 100 m apart.  Member half-width is already
            // included below, so only a modest seam allowance belongs here.
            double gap = Math.Max(8.0, Math.Min(60.0, declaredWidth * 0.55));
            for (int i = 0; i < projections.Count; i++)
            {
                PrimitiveProjection projection = projections[i];
                Band best = null;
                double bestDistance = double.PositiveInfinity;
                for (int j = 0; j < bands.Count; j++)
                {
                    double distance = Math.Abs(projection.Across - bands[j].AcrossCenter);
                    double memberHalf = projection.MarkerOnly ? 0.0 : projection.AcrossHalf;
                    if (distance <= gap + memberHalf && distance < bestDistance)
                    {
                        best = bands[j];
                        bestDistance = distance;
                    }
                }
                if (best == null)
                {
                    best = new Band();
                    bands.Add(best);
                }
                best.Members.Add(projection);
                best.AcrossCenter = (best.AcrossCenter * best.AcrossWeight +
                    projection.Across * projection.Weight) /
                    Math.Max(1e-9, best.AcrossWeight + projection.Weight);
                best.AcrossWeight += projection.Weight;
            }
            return bands;
        }

        static bool TryBuildCandidate(AERISRunwaySurveySnapshot snapshot,
            Orientation orientation, Band band, out AERISRunwayAxisCandidate candidate,
            out bool rejectedPlatform, out AERISRunwayFailureCode rejection)
        {
            candidate = null;
            rejectedPlatform = false;
            rejection = AERISRunwayFailureCode.None;
            if (band == null || band.Members.Count == 0) return false;
            double minAlong = double.PositiveInfinity;
            double maxAlong = double.NegativeInfinity;
            double usableRawMin = double.PositiveInfinity;
            double usableRawMax = double.NegativeInfinity;
            double weightedCenterUp = 0.0;
            double totalWeight = 0.0;
            double widthWeighted = 0.0;
            double usableWidthWeighted = 0.0;
            double usableWeight = 0.0;
            double flatnessWeighted = 0.0;
            double flatnessWeight = 0.0;
            double alignmentWeighted = 0.0;
            int sourceMaskCount = 0;
            var sourceGroups = new HashSet<int>();
            AERISSurveySemantic semantics = AERISSurveySemantic.None;
            AERISRunwayEvidenceFamily evidence = AERISRunwayEvidenceFamily.None;
            AERISRunwayMeasurementMethod methods = orientation.Methods;
            bool hasMeasuredGeometry = false;
            double thresholdLow = double.PositiveInfinity;
            double thresholdHigh = double.NegativeInfinity;
            for (int i = 0; i < band.Members.Count; i++)
            {
                PrimitiveProjection value = band.Members[i];
                if (value.MarkerOnly)
                {
                    semantics |= value.Primitive.Semantic;
                    evidence |= value.Primitive.EvidenceFamily |
                        AERISRunwayEvidenceFamily.AviationMarkingLighting;
                    methods |= value.Primitive.Method |
                        AERISRunwayMeasurementMethod.M11ThresholdMarking;
                    thresholdLow = Math.Min(thresholdLow, value.Along);
                    thresholdHigh = Math.Max(thresholdHigh, value.Along);
                    continue;
                }
                minAlong = Math.Min(minAlong, value.Along - value.AlongHalf);
                maxAlong = Math.Max(maxAlong, value.Along + value.AlongHalf);
                widthWeighted += value.AcrossHalf * 2.0 * value.Weight;
                bool nonLandingSurface = (value.Primitive.Semantic &
                    (AERISSurveySemantic.BlastPad | AERISSurveySemantic.Stopway |
                     AERISSurveySemantic.ApproachLight | AERISSurveySemantic.Obstacle)) != 0;
                if (!nonLandingSurface)
                {
                    usableRawMin = Math.Min(usableRawMin,
                        value.Along - value.AlongHalf);
                    usableRawMax = Math.Max(usableRawMax,
                        value.Along + value.AlongHalf);
                    usableWidthWeighted += value.AcrossHalf * 2.0 * value.Weight;
                    usableWeight += value.Weight;
                }
                if ((value.Primitive.Semantic & AERISSurveySemantic.Threshold) != 0)
                {
                    thresholdLow = Math.Min(thresholdLow, value.Along);
                    thresholdHigh = Math.Max(thresholdHigh, value.Along);
                }
                weightedCenterUp += value.Primitive.CenterUp * value.Weight;
                // Slope certification must be based on landing-surface geometry only.
                // KK models commonly bundle edge lights, foundations, buildings or
                // approach structures whose local bounds are steep even when the runway
                // pavement is flat.  Those objects remain useful topology evidence, but
                // cannot veto an otherwise valid runway with SurfaceSlopeExceeded.
                if (RunwaySurfaceFlatnessEvidence(value.Primitive))
                {
                    flatnessWeighted += Math.Abs(value.Primitive.FlatnessDeg) * value.Weight;
                    flatnessWeight += value.Weight;
                }
                alignmentWeighted += value.Alignment * value.Weight;
                totalWeight += value.Weight;
                semantics |= value.Primitive.Semantic;
                evidence |= value.Primitive.EvidenceFamily;
                methods |= value.Primitive.Method;
                if ((value.Primitive.EvidenceFamily &
                    AERISRunwayEvidenceFamily.GeometryTopology) != 0)
                    hasMeasuredGeometry = true;
                if (sourceGroups.Add(value.Primitive.SourceGroup)) sourceMaskCount++;
            }
            // A provider/spawn prior may select between measured candidates, but can
            // never create a candidate or satisfy the mandatory geometry family.
            if (!hasMeasuredGeometry)
            {
                rejection = AERISRunwayFailureCode.NoGeometryEvidence;
                return false;
            }
            if (!Finite(minAlong) || !Finite(maxAlong) || totalWeight <= 0.0)
            {
                rejection = AERISRunwayFailureCode.MeasurementDisagreement;
                return false;
            }
            double length = maxAlong - minAlong;
            if (!Finite(usableRawMin) || !Finite(usableRawMax) ||
                usableRawMax <= usableRawMin)
            {
                usableRawMin = minAlong;
                usableRawMax = maxAlong;
            }
            double width = usableWeight > 0.0
                ? usableWidthWeighted / usableWeight : widthWeighted / totalWeight;
            double flatness = flatnessWeight > 1e-9
                ? flatnessWeighted / flatnessWeight : 0.0;
            double alignmentMean = alignmentWeighted / totalWeight;
            if (width <= 0.1 || length <= 0.1)
            {
                rejection = AERISRunwayFailureCode.MeasurementDisagreement;
                return false;
            }
            double aspect = length / width;
            bool semanticRunway = (semantics & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline | AERISSurveySemantic.Threshold |
                AERISSurveySemantic.RunwayNumber | AERISSurveySemantic.EdgeLight)) != 0;
            if (width > snapshot.MaximumWidthMeters || aspect < snapshot.MinimumAspectRatio)
            {
                if (!semanticRunway) rejectedPlatform = true;
                rejection = !semanticRunway
                    ? AERISRunwayFailureCode.WholeSiteBoundsOnly
                    : AERISRunwayFailureCode.RunwayWidthUnresolved;
                return false;
            }
            if (length < snapshot.MinimumLengthMeters)
            {
                rejection = AERISRunwayFailureCode.RunwayTooShort;
                return false;
            }
            if (length > snapshot.MaximumLengthMeters)
            {
                rejection = AERISRunwayFailureCode.UnsupportedLayout;
                return false;
            }
            if (width < snapshot.MinimumWidthMeters)
            {
                rejection = AERISRunwayFailureCode.RunwayTooNarrow;
                return false;
            }

            double continuity = Math.Min(1.0,
                IntervalCoverage(band.Members) / Math.Max(1.0, length));
            if (continuity < 0.65)
            {
                rejection = AERISRunwayFailureCode.SurfaceDiscontinuity;
                return false;
            }
            if (flatness > 8.0)
            {
                rejection = AERISRunwayFailureCode.SurfaceSlopeExceeded;
                return false;
            }
            double declaredAgreement = 1.0;
            if (snapshot.DeclaredLengthMeters > 1.0)
                declaredAgreement *= Agreement(length, snapshot.DeclaredLengthMeters, 0.10);
            if (snapshot.DeclaredWidthMeters > 1.0)
                declaredAgreement *= Agreement(width, snapshot.DeclaredWidthMeters, 0.30);
            double headingAgreementTolerance = snapshot.AbsolutePlacementRequired ? 15.0 : 2.0;
            double headingAgreement = Finite(snapshot.DeclaredHeadingDeg)
                ? AgreementAngle(orientation.Heading, snapshot.DeclaredHeadingDeg,
                    headingAgreementTolerance) : 0.75;
            double geometryConfidence = Clamp01(0.52 + 0.18 * alignmentMean +
                0.12 * continuity + 0.10 * declaredAgreement +
                0.08 * Math.Min(1.0, sourceMaskCount / 3.0));
            double classificationConfidence = Clamp01(0.78 +
                (snapshot.ProviderExplicitRunway ? 0.10 : 0.0) +
                (semanticRunway ? 0.08 : 0.0) + 0.04 * headingAgreement +
                0.025 * Math.Min(1.0, sourceMaskCount / 2.0) +
                (aspect >= 8.0 ? 0.015 : 0.0));

            if (snapshot.ProviderExplicitRunway)
            {
                evidence |= AERISRunwayEvidenceFamily.MetadataSemantic;
                methods |= AERISRunwayMeasurementMethod.M01Metadata |
                    AERISRunwayMeasurementMethod.M21NameModelPrior;
            }
            methods |= AERISRunwayMeasurementMethod.M23RobustLineFit |
                AERISRunwayMeasurementMethod.M24CrossSectionVoting |
                AERISRunwayMeasurementMethod.M26TemplateFit;
            if (flatness <= 3.0)
            {
                methods |= AERISRunwayMeasurementMethod.M08SurfaceFlatness |
                    AERISRunwayMeasurementMethod.M09LongitudinalProfile;
                geometryConfidence = Clamp01(geometryConfidence + 0.020);
            }
            // Surface/terrain is an independent evidence family only after an actual
            // PQS sample.  Flatness calculated from the same mesh remains a correlated
            // geometry parameter and must not create an extra certification vote.
            if (snapshot.PqsSampled)
            {
                evidence |= AERISRunwayEvidenceFamily.SurfaceElevationTerrain;
                methods |= AERISRunwayMeasurementMethod.M18PqsArtificialSurface;
                geometryConfidence = Clamp01(geometryConfidence + 0.015);
            }
            if (Finite(snapshot.DeclaredHeadingDeg) && headingAgreement >= 0.75)
            {
                evidence |= AERISRunwayEvidenceFamily.OperationalLayout;
                methods |= AERISRunwayMeasurementMethod.M15SpawnHeading |
                    AERISRunwayMeasurementMethod.M20ReciprocalConsistency;
                geometryConfidence = Clamp01(geometryConfidence + 0.03);
            }
            if ((semantics & (AERISSurveySemantic.Centerline |
                AERISSurveySemantic.Threshold | AERISSurveySemantic.RunwayNumber |
                AERISSurveySemantic.EdgeLight | AERISSurveySemantic.ApproachLight)) != 0)
            {
                evidence |= AERISRunwayEvidenceFamily.AviationMarkingLighting;
                methods |= AERISRunwayMeasurementMethod.M10CenterlineGeometry |
                    AERISRunwayMeasurementMethod.M11ThresholdMarking |
                    AERISRunwayMeasurementMethod.M12RunwayNumber |
                    AERISRunwayMeasurementMethod.M13RunwayLights;
                geometryConfidence = Clamp01(geometryConfidence + 0.04);
            }

            // Unmarked strips may legitimately lack markings.  A stable repeated/component
            // layout is an independent operational family when at least two source groups
            // agree on the same axis.
            if (sourceMaskCount >= 2 && continuity >= 0.70)
            {
                evidence |= AERISRunwayEvidenceFamily.OperationalLayout;
                methods |= AERISRunwayMeasurementMethod.M14RepeatedPavement |
                    AERISRunwayMeasurementMethod.M16TaxiwayApronTopology |
                    AERISRunwayMeasurementMethod.M06ParallelEdges |
                    AERISRunwayMeasurementMethod.M19BilateralSymmetry;
            }

            double safetyMargin = Math.Max(2.0,
                Math.Min(length * 0.08, 2.0 + (1.0 - geometryConfidence) * 20.0 +
                    Math.Max(0.0, flatness - 1.0) * 1.5));
            double usableStart = usableRawMin + safetyMargin;
            double usableEnd = usableRawMax - safetyMargin;
            if (usableEnd - usableStart < snapshot.MinimumLengthMeters)
            {
                rejection = AERISRunwayFailureCode.RunwayTooShort;
                return false;
            }
            double midpoint = (minAlong + maxAlong) * 0.5;
            double operationalA = usableStart;
            double operationalB = usableEnd;
            bool hasLowThreshold = Finite(thresholdLow) && thresholdLow < midpoint;
            bool hasHighThreshold = Finite(thresholdHigh) && thresholdHigh > midpoint;
            if (hasLowThreshold)
                operationalA = Math.Max(usableStart,
                    Math.Min(usableEnd, thresholdLow));
            if (hasHighThreshold)
                operationalB = Math.Min(usableEnd,
                    Math.Max(usableStart, thresholdHigh));
            if (operationalB - operationalA < snapshot.MinimumLengthMeters)
            {
                rejection = AERISRunwayFailureCode.DisplacedThresholdUnresolved;
                return false;
            }
            candidate = new AERISRunwayAxisCandidate
            {
                CenterEast = orientation.East * ((minAlong + maxAlong) * 0.5) +
                    (-orientation.North) * band.AcrossCenter,
                CenterNorth = orientation.North * ((minAlong + maxAlong) * 0.5) +
                    orientation.East * band.AcrossCenter,
                CenterUp = weightedCenterUp / totalWeight,
                AxisEast = orientation.East,
                AxisNorth = orientation.North,
                PhysicalStartMeters = minAlong,
                PhysicalEndMeters = maxAlong,
                UsableStartMeters = usableStart,
                UsableEndMeters = usableEnd,
                OperationalThresholdA = operationalA,
                OperationalThresholdB = operationalB,
                WidthMeters = width,
                LengthMeters = usableEnd - usableStart,
                HeadingDeg = NormalizeHeading360(orientation.Heading),
                AxisRegistrationValid = !snapshot.AbsolutePlacementRequired,
                MeshSurfaceHeadingDeg = orientation.IndependentSurfaceAxis
                    ? NormalizeHeading360(orientation.Heading) : double.NaN,
                RegisteredHeadingBeforeDeg = NormalizeHeading360(orientation.Heading),
                RegisteredHeadingAfterDeg = NormalizeHeading360(orientation.Heading),
                AxisSurfaceAspectRatio = orientation.SurfaceAspectRatio,
                AxisSurfacePointCount = orientation.SurfacePointCount,
                ClassificationConfidence = classificationConfidence,
                GeometryConfidence = geometryConfidence,
                CenterlineUncertaintyMeters = Math.Max(0.25,
                    Math.Min(width * 0.25, width * (1.0 - geometryConfidence) * 0.65)),
                HeadingUncertaintyDeg = Math.Max(0.05,
                    Math.Min(2.0, (1.0 - geometryConfidence) * 4.0)),
                PhysicalEndUncertaintyMeters = Math.Max(0.75,
                    Math.Min(25.0, safetyMargin * (1.0 - geometryConfidence + 0.30))),
                UsableEndUncertaintyMeters = Math.Max(0.75,
                    Math.Min(20.0, safetyMargin * (1.0 - geometryConfidence + 0.22))),
                ThresholdUncertaintyMeters = Math.Max(0.5,
                    Math.Min(25.0, safetyMargin * (1.0 - geometryConfidence +
                        ((hasLowThreshold || hasHighThreshold) ? 0.08 : 0.15)))),
                LengthUncertaintyMeters = Math.Max(1.0,
                    Math.Min(length * 0.05, safetyMargin * 2.0)),
                WidthUncertaintyMeters = Math.Max(0.5,
                    Math.Min(width * 0.20, width * (1.0 - geometryConfidence))),
                ElevationUncertaintyMeters = Math.Max(0.25,
                    Math.Min(5.0, Math.Abs(flatness) * 0.35 + (1.0 - geometryConfidence) * 2.0)),
                DisplacedThresholdConfidence = hasLowThreshold || hasHighThreshold
                    ? Math.Min(0.99, 0.75 + geometryConfidence * 0.22)
                    : Math.Min(0.90, 0.55 + geometryConfidence * 0.30),
                ApproachCorridorConfidence = 0.0,
                EvidenceFamilies = evidence,
                Methods = methods,
                ApproachAAvailable = snapshot.ApproachAAvailable,
                ApproachBAvailable = snapshot.ApproachBAvailable,
                Detail = "members=" + band.Members.Count + "; groups=" + sourceMaskCount +
                    "; continuity=" + continuity.ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "; flatness=" + flatness.ToString("0.000",
                        System.Globalization.CultureInfo.InvariantCulture)
            };
            if (!ApplyAbsolutePlacementConstraint(snapshot, candidate, out rejection))
            {
                candidate = null;
                return false;
            }
            return true;
        }

        static bool RunwaySurfaceFlatnessEvidence(AERISSurveyPrimitive primitive)
        {
            AERISSurveySemantic semantic = primitive.Semantic;
            bool explicitSurface = (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Pavement | AERISSurveySemantic.Centerline)) != 0;
            bool excluded = (semantic & (AERISSurveySemantic.Taxiway |
                AERISSurveySemantic.Apron | AERISSurveySemantic.Platform |
                AERISSurveySemantic.Obstacle | AERISSurveySemantic.NaturalSurface |
                AERISSurveySemantic.ApproachLight | AERISSurveySemantic.EdgeLight)) != 0 &&
                (semantic & (AERISSurveySemantic.Runway |
                AERISSurveySemantic.Centerline)) == 0;
            return explicitSurface && !excluded && Finite(primitive.FlatnessDeg);
        }

        static bool ApplyAbsolutePlacementConstraint(
            AERISRunwaySurveySnapshot snapshot, AERISRunwayAxisCandidate candidate,
            out AERISRunwayFailureCode rejection)
        {
            rejection = AERISRunwayFailureCode.None;
            if (snapshot == null || candidate == null)
            {
                rejection = AERISRunwayFailureCode.AbsolutePlacementInvalid;
                return false;
            }
            if (!snapshot.AbsolutePlacementRequired)
            {
                candidate.AxisRegistrationValid = true;
                candidate.AxisRegistrationDetail = "NOT REQUIRED";
                candidate.AbsolutePlacementValid = true;
                candidate.AbsolutePlacementDetail = "NOT REQUIRED";
                return true;
            }
            if (!ApplyAxisRegistrationConstraint(snapshot, candidate, out rejection))
                return false;
            if (!snapshot.AbsolutePlacementConstraintAvailable ||
                !Finite(snapshot.LaunchAnchorEastMeters) ||
                !Finite(snapshot.LaunchAnchorNorthMeters) ||
                !Finite(snapshot.LaunchAnchorHeadingDeg))
            {
                candidate.AbsolutePlacementDetail =
                    "REQUIRED LAUNCH ANCHOR IS MISSING OR NON-FINITE";
                rejection = AERISRunwayFailureCode.AbsolutePlacementInvalid;
                return false;
            }

            double normalEast = -candidate.AxisNorth;
            double normalNorth = candidate.AxisEast;
            double centerAcross = candidate.CenterEast * normalEast +
                candidate.CenterNorth * normalNorth;
            double launchAcross = snapshot.LaunchAnchorEastMeters * normalEast +
                snapshot.LaunchAnchorNorthMeters * normalNorth;
            double launchAlong = snapshot.LaunchAnchorEastMeters * candidate.AxisEast +
                snapshot.LaunchAnchorNorthMeters * candidate.AxisNorth;
            double crossTrack = launchAcross - centerAcross;
            double headingError = AngleDifference180(candidate.HeadingDeg,
                snapshot.LaunchAnchorHeadingDeg);
            double alongMargin = Math.Max(75.0, candidate.WidthMeters * 1.00);
            double maximumCorrection = Math.Max(75.0, candidate.WidthMeters * 1.25);
            bool alongValid = Finite(launchAlong) &&
                launchAlong >= candidate.PhysicalStartMeters - alongMargin &&
                launchAlong <= candidate.PhysicalEndMeters + alongMargin;
            bool correctionValid = Finite(crossTrack) &&
                Math.Abs(crossTrack) <= maximumCorrection;

            candidate.LaunchCrossTrackBeforeMeters = crossTrack;
            candidate.LaunchAlongTrackMeters = launchAlong;
            candidate.LaunchHeadingErrorDeg = headingError;
            candidate.AbsoluteTranslationMeters = Math.Abs(crossTrack);
            if (!alongValid || !correctionValid)
            {
                candidate.AbsolutePlacementValid = false;
                candidate.LaunchCrossTrackAfterMeters = crossTrack;
                candidate.AbsolutePlacementDetail =
                    "INVALID launchCross=" + crossTrack.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "m; maxCorrection=" + maximumCorrection.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "m; launchAlong=" + launchAlong.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "m; physical=[" + candidate.PhysicalStartMeters.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) + "," +
                    candidate.PhysicalEndMeters.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "]; launchHeadingTelemetryError=" + headingError.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) + "deg; axis=" +
                    candidate.AxisRegistrationDetail;
                candidate.Detail += "; absolutePlacement=" +
                    candidate.AbsolutePlacementDetail;
                rejection = AERISRunwayFailureCode.AbsolutePlacementInvalid;
                return false;
            }

            candidate.CenterEast += normalEast * crossTrack;
            candidate.CenterNorth += normalNorth * crossTrack;
            candidate.LaunchConstraintApplied = Math.Abs(crossTrack) > 0.25;
            candidate.LaunchCrossTrackAfterMeters = 0.0;
            candidate.AbsolutePlacementValid = true;
            double correctionRatio = maximumCorrection > 1e-6
                ? Math.Abs(crossTrack) / maximumCorrection : 0.0;
            candidate.GeometryConfidence = Clamp01(candidate.GeometryConfidence -
                Math.Min(0.08, correctionRatio * 0.08));
            candidate.CenterlineUncertaintyMeters = Math.Max(
                candidate.CenterlineUncertaintyMeters,
                Math.Min(maximumCorrection, Math.Max(0.50, Math.Abs(crossTrack) * 0.10)));
            candidate.AbsolutePlacementDetail =
                "VALID launchCrossBefore=" + crossTrack.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "m; launchCrossAfter=0.00m; launchAlong=" +
                launchAlong.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "m; launchHeadingTelemetryError=" + headingError.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; translated=" + Math.Abs(crossTrack).ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) + "m; axis=" +
                candidate.AxisRegistrationDetail;
            candidate.Detail += "; absolutePlacement=" +
                candidate.AbsolutePlacementDetail;
            return true;
        }

        static bool ApplyAxisRegistrationConstraint(
            AERISRunwaySurveySnapshot snapshot, AERISRunwayAxisCandidate candidate,
            out AERISRunwayFailureCode rejection)
        {
            rejection = AERISRunwayFailureCode.None;
            Orientation physicalAxis;
            if (!TryRunwaySurfacePca(snapshot, out physicalAxis) || physicalAxis == null ||
                !physicalAxis.IndependentSurfaceAxis)
            {
                candidate.AxisRegistrationValid = false;
                candidate.AxisRegistrationDetail =
                    "INVALID NO INDEPENDENT RUNWAY-SURFACE AXIS";
                candidate.Detail += "; axisRegistration=" +
                    candidate.AxisRegistrationDetail;
                rejection = AERISRunwayFailureCode.AbsolutePlacementInvalid;
                return false;
            }

            double measuredHeading = NormalizeHeading360(physicalAxis.Heading);
            double candidateHeading = NormalizeHeading360(candidate.HeadingDeg);
            // For KK/SLE the provider static orientation is the model-instance rotation,
            // not the runway designator.  Many valid runways use an internally rotated
            // mesh while the static orientation remains 0 degrees.  The launch/spawn
            // transform is the independent world-space runway reference already used by
            // absolute placement, so use it only as a broad axis sanity gate.  The
            // measured pavement stripe remains authoritative and may be reciprocal.
            double registeredReferenceHeading = Finite(snapshot.LaunchAnchorHeadingDeg)
                ? NormalizeHeading360(snapshot.LaunchAnchorHeadingDeg) : candidateHeading;
            double legacyRegisteredHeading = registeredReferenceHeading;
            double surfaceError = AngleDifference180(candidateHeading, measuredHeading);
            double axisReferenceError = AngleDifference180(measuredHeading,
                registeredReferenceHeading);
            bool surfaceAgreement = Finite(surfaceError) && surfaceError <= 1.0;
            bool boundedSurfaceCorrection = Finite(surfaceError) && surfaceError <= 12.0;
            bool axisReferenceAgreement = Finite(snapshot.LaunchAnchorHeadingDeg) &&
                Finite(axisReferenceError) && axisReferenceError <= 15.0;
            bool supportValid = physicalAxis.SurfacePointCount >= 16 &&
                physicalAxis.SurfaceAspectRatio >= 4.0;

            candidate.MeshSurfaceHeadingDeg = measuredHeading;
            candidate.RegisteredHeadingBeforeDeg = legacyRegisteredHeading;
            candidate.AxisReferenceErrorDeg = axisReferenceError;
            candidate.AxisSurfaceAspectRatio = physicalAxis.SurfaceAspectRatio;
            candidate.AxisSurfacePointCount = physicalAxis.SurfacePointCount;
            if (!boundedSurfaceCorrection || !axisReferenceAgreement || !supportValid)
            {
                candidate.AxisRegistrationValid = false;
                candidate.RegisteredHeadingAfterDeg = candidateHeading;
                candidate.HeadingCorrectionDeg =
                    SignedAxisDifference(legacyRegisteredHeading, candidateHeading);
                candidate.AxisRegistrationDetail =
                    "INVALID meshHeading=" + measuredHeading.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "deg; candidateHeading=" + candidateHeading.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "deg; surfaceError=" + surfaceError.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "deg; correctionLimit=12.00deg; axisReference=LAUNCH_ANCHOR; axisReferenceError=" +
                    axisReferenceError.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "deg; aspect=" + physicalAxis.SurfaceAspectRatio.ToString("0.00",
                        System.Globalization.CultureInfo.InvariantCulture) +
                    "; points=" + physicalAxis.SurfacePointCount;
                candidate.Detail += "; axisRegistration=" +
                    candidate.AxisRegistrationDetail;
                rejection = AERISRunwayFailureCode.AbsolutePlacementInvalid;
                return false;
            }

            // The measured pavement stripe is authoritative.  A candidate created by a
            // trusted primitive may be within the old 2-degree merge window yet still be
            // visibly wrong over a long runway.  Re-register the complete scalar geometry
            // onto the physical axis instead of rejecting almost every KK site.
            if (!surfaceAgreement)
            {
                ReRegisterCandidateToPhysicalAxis(candidate, physicalAxis);
                candidateHeading = NormalizeHeading360(candidate.HeadingDeg);
            }
            candidate.AxisRegistrationValid = true;
            candidate.RegisteredHeadingAfterDeg = candidateHeading;
            candidate.HeadingCorrectionDeg =
                SignedAxisDifference(legacyRegisteredHeading, candidateHeading);
            candidate.HeadingUncertaintyDeg = Math.Max(candidate.HeadingUncertaintyDeg,
                Math.Min(2.5, 0.10 + surfaceError * 0.10 + axisReferenceError * 0.03));
            if (!surfaceAgreement)
                candidate.GeometryConfidence = Clamp01(candidate.GeometryConfidence -
                    Math.Min(0.06, surfaceError * 0.004));
            candidate.AxisRegistrationDetail =
                "VALID meshHeading=" + measuredHeading.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; registeredBefore=" + legacyRegisteredHeading.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; registeredAfter=" + candidate.RegisteredHeadingAfterDeg.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; headingCorrection=" + candidate.HeadingCorrectionDeg.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; candidateSurfaceErrorBefore=" + surfaceError.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; axisRealigned=" + (!surfaceAgreement ? "True" : "False") +
                "; axisReference=LAUNCH_ANCHOR; axisReferenceError=" + axisReferenceError.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "deg; aspect=" + physicalAxis.SurfaceAspectRatio.ToString("0.00",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "; points=" + physicalAxis.SurfacePointCount;
            candidate.Detail += "; axisRegistration=" +
                candidate.AxisRegistrationDetail;
            return true;
        }

        static void ReRegisterCandidateToPhysicalAxis(
            AERISRunwayAxisCandidate candidate, Orientation physicalAxis)
        {
            double axisEast = physicalAxis.East;
            double axisNorth = physicalAxis.North;
            double dot = axisEast * candidate.AxisEast +
                axisNorth * candidate.AxisNorth;
            if (dot < 0.0)
            {
                axisEast = -axisEast;
                axisNorth = -axisNorth;
            }
            double oldMidpoint = (candidate.PhysicalStartMeters +
                candidate.PhysicalEndMeters) * 0.5;
            double newMidpoint = candidate.CenterEast * axisEast +
                candidate.CenterNorth * axisNorth;
            candidate.PhysicalStartMeters = newMidpoint +
                (candidate.PhysicalStartMeters - oldMidpoint);
            candidate.PhysicalEndMeters = newMidpoint +
                (candidate.PhysicalEndMeters - oldMidpoint);
            candidate.UsableStartMeters = newMidpoint +
                (candidate.UsableStartMeters - oldMidpoint);
            candidate.UsableEndMeters = newMidpoint +
                (candidate.UsableEndMeters - oldMidpoint);
            candidate.OperationalThresholdA = newMidpoint +
                (candidate.OperationalThresholdA - oldMidpoint);
            candidate.OperationalThresholdB = newMidpoint +
                (candidate.OperationalThresholdB - oldMidpoint);
            candidate.AxisEast = axisEast;
            candidate.AxisNorth = axisNorth;
            candidate.HeadingDeg = NormalizeHeading360(
                Math.Atan2(axisEast, axisNorth) * RadToDeg);
            candidate.MeshSurfaceHeadingDeg = candidate.HeadingDeg;
        }

        static double SignedAxisDifference(double fromHeading, double toHeading)
        {
            double from = NormalizeHeading180(fromHeading);
            double to = NormalizeHeading180(toHeading);
            double difference = to - from;
            while (difference > 90.0) difference -= 180.0;
            while (difference < -90.0) difference += 180.0;
            return difference;
        }

        static AERISRunwayFailureCode PreferFailure(AERISRunwayFailureCode current,
            AERISRunwayFailureCode candidate)
        {
            return FailurePriority(candidate) > FailurePriority(current) ? candidate : current;
        }

        static int FailurePriority(AERISRunwayFailureCode value)
        {
            switch (value)
            {
                case AERISRunwayFailureCode.PlanWitnessConflict: return 120;
                case AERISRunwayFailureCode.ObservedPlacementMismatch: return 119;
                case AERISRunwayFailureCode.UserCalibrationRequired: return 118;
                case AERISRunwayFailureCode.UserCalibrationInvalid: return 115;
                case AERISRunwayFailureCode.AnchorSurfaceUnresolved: return 105;
                case AERISRunwayFailureCode.AbsolutePlacementInvalid: return 100;
                case AERISRunwayFailureCode.SurfaceSlopeExceeded: return 90;
                case AERISRunwayFailureCode.SurfaceDiscontinuity: return 85;
                case AERISRunwayFailureCode.DisplacedThresholdUnresolved: return 80;
                case AERISRunwayFailureCode.RunwayTooShort: return 75;
                case AERISRunwayFailureCode.RunwayTooNarrow: return 74;
                case AERISRunwayFailureCode.MeasurementDisagreement: return 70;
                case AERISRunwayFailureCode.UnsupportedLayout: return 65;
                case AERISRunwayFailureCode.WholeSiteBoundsOnly: return 50;
                case AERISRunwayFailureCode.RunwayWidthUnresolved: return 45;
                case AERISRunwayFailureCode.NoGeometryEvidence: return 40;
                default: return 0;
            }
        }

        static string RejectionDetail(AERISRunwayFailureCode rejection,
            bool wholeSiteOnly)
        {
            switch (rejection)
            {
                case AERISRunwayFailureCode.PlanWitnessConflict:
                    return "KRAMAX/USER RUNWAY WITNESS CONFLICTS WITH THE LAUNCH-ANCHOR-CONNECTED PHYSICAL SURFACE";
                case AERISRunwayFailureCode.ObservedPlacementMismatch:
                    return "OBSERVED VESSEL POSITION DOES NOT LIE INSIDE THE CERTIFIED RUNWAY CORRIDOR";
                case AERISRunwayFailureCode.UserCalibrationRequired:
                    return "USER TWO-POINT RUNWAY CALIBRATION IS REQUIRED BEFORE CERTIFICATION";
                case AERISRunwayFailureCode.UserCalibrationInvalid:
                    return "USER TWO-POINT RUNWAY CALIBRATION IS INCOMPLETE OR INVALID";
                case AERISRunwayFailureCode.AnchorSurfaceUnresolved:
                    return "NO STRAIGHT CONSTANT-WIDTH SURFACE CORRIDOR CONNECTED TO THE LAUNCH ANCHOR WAS RESOLVED";
                case AERISRunwayFailureCode.AbsolutePlacementInvalid:
                    return "KK/SLE RUNWAY SURFACE AXIS OR LAUNCH-ANCHOR ABSOLUTE PLACEMENT DID NOT PASS FAIL-CLOSED VALIDATION";
                case AERISRunwayFailureCode.SurfaceSlopeExceeded:
                    return "CANDIDATE SURFACE SLOPE/RELIEF EXCEEDED THE 8 DEGREE SAFETY LIMIT";
                case AERISRunwayFailureCode.SurfaceDiscontinuity:
                    return "NO CANDIDATE PROVIDED AT LEAST 65 PERCENT CONTINUOUS SURFACE COVERAGE";
                case AERISRunwayFailureCode.DisplacedThresholdUnresolved:
                    return "MARKING/USABLE-END FUSION LEFT INSUFFICIENT OPERATIONAL LENGTH";
                case AERISRunwayFailureCode.RunwayTooShort:
                    return "USABLE RUNWAY LENGTH IS BELOW THE CONFIGURED MINIMUM";
                case AERISRunwayFailureCode.RunwayTooNarrow:
                    return "USABLE RUNWAY WIDTH IS BELOW THE CONFIGURED MINIMUM";
                case AERISRunwayFailureCode.MeasurementDisagreement:
                    return "MEASURED GEOMETRY IS NON-FINITE OR DEGENERATE";
                case AERISRunwayFailureCode.UnsupportedLayout:
                    return "MEASURED STRIP EXCEEDS SUPPORTED STRAIGHT-RUNWAY LIMITS";
            }
            return wholeSiteOnly
                ? "ONLY WHOLE-SITE/PLATFORM BOUNDS SURVIVED; NO RUNWAY BAND CERTIFIED"
                : "NO RUNWAY AXIS MET GEOMETRY, CONTINUITY AND EVIDENCE LIMITS";
        }

        static void AddOrReplace(List<AERISRunwayAxisCandidate> values,
            AERISRunwayAxisCandidate candidate)
        {
            for (int i = 0; i < values.Count; i++)
            {
                AERISRunwayAxisCandidate existing = values[i];
                double distance = Distance(existing.CenterEast, existing.CenterNorth,
                    candidate.CenterEast, candidate.CenterNorth);
                double heading = AngleDifference180(existing.HeadingDeg, candidate.HeadingDeg);
                double lateralGate = Math.Max(12.0,
                    Math.Max(existing.WidthMeters, candidate.WidthMeters) * 0.8);
                if (distance <= lateralGate && heading <= 3.0)
                {
                    if (CandidateScore(candidate) > CandidateScore(existing)) values[i] = candidate;
                    return;
                }
            }
            values.Add(candidate);
        }

        static void RemoveWeakDuplicates(List<AERISRunwayAxisCandidate> values)
        {
            for (int i = values.Count - 1; i >= 0; i--)
            {
                AERISRunwayAxisCandidate a = values[i];
                for (int j = 0; j < i; j++)
                {
                    AERISRunwayAxisCandidate b = values[j];
                    double distance = Distance(a.CenterEast, a.CenterNorth,
                        b.CenterEast, b.CenterNorth);
                    double heading = AngleDifference180(a.HeadingDeg, b.HeadingDeg);
                    if (distance < Math.Max(a.WidthMeters, b.WidthMeters) && heading < 4.0)
                    {
                        values.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        static int CompareCandidates(AERISRunwayAxisCandidate a,
            AERISRunwayAxisCandidate b)
        {
            int score = CandidateScore(b).CompareTo(CandidateScore(a));
            if (score != 0) return score;
            int heading = a.HeadingDeg.CompareTo(b.HeadingDeg);
            if (heading != 0) return heading;
            return a.CenterEast.CompareTo(b.CenterEast);
        }

        static double CandidateScore(AERISRunwayAxisCandidate value)
        {
            if (value == null) return 0.0;
            return value.GeometryConfidence * 0.55 +
                value.ClassificationConfidence * 0.30 +
                Math.Min(5, CountFamilies(value.EvidenceFamilies)) * 0.03;
        }

        static int CountFamilies(AERISRunwayEvidenceFamily value)
        {
            int bits = (int)value;
            int count = 0;
            while (bits != 0)
            {
                count += bits & 1;
                bits >>= 1;
            }
            return count;
        }

        sealed class Interval
        {
            internal double Start;
            internal double End;
        }

        static double IntervalCoverage(IList<PrimitiveProjection> values)
        {
            var intervals = new List<Interval>();
            if (values != null)
                for (int i = 0; i < values.Count; i++)
                {
                    PrimitiveProjection value = values[i];
                    if (value == null || value.MarkerOnly || !Finite(value.Along) ||
                        !Finite(value.AlongHalf) || value.AlongHalf <= 0.0) continue;
                    intervals.Add(new Interval
                    {
                        Start = value.Along - value.AlongHalf,
                        End = value.Along + value.AlongHalf
                    });
                }
            if (intervals.Count == 0) return 0.0;
            intervals.Sort((a, b) => a.Start.CompareTo(b.Start));
            double start = intervals[0].Start;
            double end = intervals[0].End;
            double total = 0.0;
            for (int i = 1; i < intervals.Count; i++)
            {
                if (intervals[i].Start <= end)
                {
                    end = Math.Max(end, intervals[i].End);
                    continue;
                }
                total += Math.Max(0.0, end - start);
                start = intervals[i].Start;
                end = intervals[i].End;
            }
            return total + Math.Max(0.0, end - start);
        }

        static bool FinitePrimitive(AERISSurveyPrimitive value)
        {
            return Finite(value.CenterEast) && Finite(value.CenterNorth) &&
                Finite(value.CenterUp) && Finite(value.AxisEast) &&
                Finite(value.AxisNorth) && Finite(value.LengthMeters) &&
                Finite(value.WidthMeters) && value.LengthMeters > 0.0 &&
                value.WidthMeters > 0.0;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static double Clamp01(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }

        static double Agreement(double value, double reference, double toleranceFraction)
        {
            if (reference <= 1e-6) return 0.5;
            double error = Math.Abs(value - reference) / reference;
            return Clamp01(1.0 - error / Math.Max(0.01, toleranceFraction));
        }

        static double AgreementAngle(double a, double b, double toleranceDeg)
        {
            return Clamp01(1.0 - AngleDifference180(a, b) /
                Math.Max(0.1, toleranceDeg));
        }

        static double AngleDifference180(double a, double b)
        {
            double difference = Math.Abs(NormalizeHeading180(a) - NormalizeHeading180(b));
            return Math.Min(difference, 180.0 - difference);
        }

        static double NormalizeHeading180(double value)
        {
            value %= 180.0;
            if (value < 0.0) value += 180.0;
            return value;
        }

        static double NormalizeHeading360(double value)
        {
            value %= 360.0;
            if (value < 0.0) value += 360.0;
            return value;
        }

        static double Distance(double ae, double an, double be, double bn)
        {
            double de = ae - be;
            double dn = an - bn;
            return Math.Sqrt(de * de + dn * dn);
        }
    }
}
