using System;

namespace AERISFlightControl.Terrain
{
    // R041 pure CLR height evaluator for stock PQSMod_FlattenArea.
    // HARD RULE: no Unity/KSP/runtime-object types in this file.
    internal static class AERIS39FlattenAreaPureCpuExact
    {
        internal sealed class OpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly bool OverrideQuadBuildCheck;
            internal readonly bool QuadActive;
            internal readonly double PosNormX;
            internal readonly double PosNormY;
            internal readonly double PosNormZ;
            internal readonly double AngleInner;
            internal readonly double AngleOuter;
            internal readonly double AngleQuadInclusion;
            internal readonly double AngleDelta;
            internal readonly double FlattenToRadius;
            internal readonly double SmoothStart;
            internal readonly double SmoothEnd;

            internal OpSnapshot(
                bool overrideQuadBuildCheck,
                bool quadActive,
                double posNormX,
                double posNormY,
                double posNormZ,
                double angleInner,
                double angleOuter,
                double angleQuadInclusion,
                double angleDelta,
                double flattenToRadius,
                double smoothStart,
                double smoothEnd)
            {
                OverrideQuadBuildCheck = overrideQuadBuildCheck;
                QuadActive = quadActive;
                PosNormX = posNormX;
                PosNormY = posNormY;
                PosNormZ = posNormZ;
                AngleInner = angleInner;
                AngleOuter = angleOuter;
                AngleQuadInclusion = angleQuadInclusion;
                AngleDelta = angleDelta;
                FlattenToRadius = flattenToRadius;
                SmoothStart = smoothStart;
                SmoothEnd = smoothEnd;
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double uIgnored,
                double vIgnored,
                double height)
            {
                // Stock fast-path gate. Runtime quad/setup state is captured on the
                // main thread rather than reconstructed on the worker.
                if (!OverrideQuadBuildCheck && !QuadActive)
                    return height;

                double testAngle = Math.Acos(
                    (x * PosNormX) + (y * PosNormY) + (z * PosNormZ));

                if (testAngle < AngleQuadInclusion)
                {
                    // removeScatter is intentionally reference-only for this height
                    // witness; it does not alter vertHeight directly.
                    if (testAngle < AngleOuter)
                    {
                        if (testAngle < AngleInner)
                            return FlattenToRadius;

                        double aDelta = (testAngle - AngleInner) / AngleDelta;
                        return CubicHermite(
                            FlattenToRadius,
                            height,
                            SmoothStart,
                            SmoothEnd,
                            aDelta);
                    }
                }

                return height;
            }
        }

        internal static double CubicHermite(
            double start,
            double end,
            double startTangent,
            double endTangent,
            double t)
        {
            // Preserve stock operation order exactly.
            double ct2 = t * t;
            double ct3 = ct2 * t;

            return (start * ((2 * ct3) - (3 * ct2) + 1d)) +
                (startTangent * (ct3 - (2 * ct2) + t)) +
                (end * ((-2 * ct3) + (3 * ct2))) +
                (endTangent * (ct3 - ct2));
        }
    }
}
