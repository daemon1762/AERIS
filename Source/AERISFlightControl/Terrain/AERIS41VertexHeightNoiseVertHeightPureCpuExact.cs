using System;

namespace AERISFlightControl.Terrain
{
    // AERIS41 R041 Eeloo PQSMod_VertexHeightNoiseVertHeight pure CPU exact path.
    //
    // HARD RULE:
    // - No Unity/KSP/runtime-object types.
    // - Runtime state is copied on the main thread into primitive snapshots.
    // - RidgedMultifractal/GradientNoise arithmetic is reused from the already
    //   accepted R039 exact LibNoise reconstruction.
    internal static class AERIS41VertexHeightNoiseVertHeightPureCpuExact
    {
        internal sealed class OpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly float Deformity;
            internal readonly double RadiusMin;
            internal readonly double RadiusDelta;
            internal readonly float HeightStart;
            internal readonly float HeightEnd;
            internal readonly double HDeltaR;
            internal readonly AERISR039MinmusPureCpuExact.RidgedSnapshot Ridged;

            internal OpSnapshot(
                float deformity,
                double radiusMin,
                double radiusDelta,
                float heightStart,
                float heightEnd,
                double hDeltaR,
                AERISR039MinmusPureCpuExact.RidgedSnapshot ridged)
            {
                Deformity = deformity;
                RadiusMin = radiusMin;
                RadiusDelta = radiusDelta;
                HeightStart = heightStart;
                HeightEnd = heightEnd;
                HDeltaR = hDeltaR;
                Ridged = ridged ?? throw new ArgumentNullException("ridged");
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height)
            {
                // Captured PQSMod_VertexHeightNoiseVertHeight.OnVertexBuildHeight
                // IL SHA256:
                // 0c6ef5f07a24e18ecb86404c162a79872d583da0cfa46e16c8c31cbfa92ad7fc
                double h = height - RadiusMin;
                h = h / RadiusDelta;

                double heightStart = (double)HeightStart;
                if (double.IsNaN(h) || h < heightStart)
                    return height;

                double heightEnd = (double)HeightEnd;
                if (double.IsNaN(h) || h > heightEnd)
                    return height;

                h = h - heightStart;
                h = h * HDeltaR;

                double n = AERISR039MinmusPureCpuExact.RidgedGetValue(
                    Ridged, x, y, z);

                // Captured bge.un / ble.un branch behavior leaves NaN untouched.
                if (!double.IsNaN(n) && n < -1.0)
                    n = -1.0;
                if (!double.IsNaN(n) && n > 1.0)
                    n = 1.0;

                double delta = n + 1.0;
                delta = delta * 0.5;
                delta = delta * (double)Deformity;
                delta = delta * h;
                return height + delta;
            }
        }
    }
}
