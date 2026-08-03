using System;
using System.Collections.Generic;

namespace AERISFlightControl.Landing
{
    internal sealed class AERISRunwayMethodPlan
    {
        internal AERISRunwayMeasurementMethod ApplicableMethods;
        internal string Detail = string.Empty;
    }

    // Pure applicability planner.  It avoids pretending that all 26 methods are
    // independent votes and avoids scheduling methods whose required data is absent.
    internal static class AERISRunwaySurveyPlanner
    {
        internal static AERISRunwayMethodPlan Create(AERISRunwaySurveySnapshot snapshot)
        {
            var plan = new AERISRunwayMethodPlan();
            if (snapshot == null)
            {
                plan.Detail = "NO SNAPSHOT";
                return plan;
            }
            AERISRunwayMeasurementMethod methods = AERISRunwayMeasurementMethod.None;
            if (snapshot.ProviderExplicitRunway)
                methods |= AERISRunwayMeasurementMethod.M01Metadata |
                    AERISRunwayMeasurementMethod.M21NameModelPrior;
            if (Finite(snapshot.DeclaredHeadingDeg))
                methods |= AERISRunwayMeasurementMethod.M15SpawnHeading |
                    AERISRunwayMeasurementMethod.M20ReciprocalConsistency;
            if (snapshot.Primitives.Length > 0)
                methods |= AERISRunwayMeasurementMethod.M02RendererBounds |
                    AERISRunwayMeasurementMethod.M06ParallelEdges |
                    AERISRunwayMeasurementMethod.M07LongSurfaceStrip |
                    AERISRunwayMeasurementMethod.M08SurfaceFlatness |
                    AERISRunwayMeasurementMethod.M09LongitudinalProfile |
                    AERISRunwayMeasurementMethod.M17PlatformExclusion |
                    AERISRunwayMeasurementMethod.M19BilateralSymmetry |
                    AERISRunwayMeasurementMethod.M23RobustLineFit |
                    AERISRunwayMeasurementMethod.M24CrossSectionVoting |
                    AERISRunwayMeasurementMethod.M25MultiScale |
                    AERISRunwayMeasurementMethod.M26TemplateFit;
            if (snapshot.Points.Length >= 8)
                methods |= AERISRunwayMeasurementMethod.M03MeshPca |
                    AERISRunwayMeasurementMethod.M05SubMeshMaterial;
            if (snapshot.ColliderReadable)
                methods |= AERISRunwayMeasurementMethod.M04Collider;
            if (snapshot.PqsSampled)
                methods |= AERISRunwayMeasurementMethod.M18PqsArtificialSurface;

            var sourceGroupIds = new HashSet<int>();
            AERISSurveySemantic semantics = AERISSurveySemantic.None;
            for (int i = 0; i < snapshot.Primitives.Length; i++)
            {
                AERISSurveyPrimitive primitive = snapshot.Primitives[i];
                methods |= primitive.Method;
                semantics |= primitive.Semantic;
                sourceGroupIds.Add(primitive.SourceGroup);
            }
            int sourceGroups = sourceGroupIds.Count;
            if ((semantics & AERISSurveySemantic.Centerline) != 0)
                methods |= AERISRunwayMeasurementMethod.M10CenterlineGeometry;
            if ((semantics & AERISSurveySemantic.Threshold) != 0)
                methods |= AERISRunwayMeasurementMethod.M11ThresholdMarking;
            if ((semantics & AERISSurveySemantic.RunwayNumber) != 0)
                methods |= AERISRunwayMeasurementMethod.M12RunwayNumber;
            if ((semantics & (AERISSurveySemantic.EdgeLight |
                AERISSurveySemantic.ApproachLight)) != 0)
                methods |= AERISRunwayMeasurementMethod.M13RunwayLights;
            if (sourceGroups >= 2)
                methods |= AERISRunwayMeasurementMethod.M14RepeatedPavement |
                    AERISRunwayMeasurementMethod.M16TaxiwayApronTopology;
            plan.ApplicableMethods = methods;
            plan.Detail = "applicable=0x" + ((long)methods).ToString("X") +
                "; primitives=" + snapshot.Primitives.Length + "; points=" +
                snapshot.Points.Length + "; sourceGroups=" + sourceGroups;
            return plan;
        }

        static bool Finite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
