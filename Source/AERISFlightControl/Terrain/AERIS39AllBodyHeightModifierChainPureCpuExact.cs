using System;

namespace AERISFlightControl.Terrain
{
    // R041 all-body height-modifier chain pure CLR evaluator.
    //
    // HARD RULE: no Unity/KSP/runtime-object types in this file.
    // Runtime configuration is copied on the main thread into immutable
    // primitive/pure snapshots before worker evaluation.
    internal static class AERIS39AllBodyHeightModifierChainPureCpuExact
    {
        internal sealed class CertifiedHeightMapOpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly AERIS39HeightMapPureCpuExact.Snapshot HeightMap;

            internal CertifiedHeightMapOpSnapshot(
                AERIS39HeightMapPureCpuExact.Snapshot heightMap)
            {
                HeightMap = heightMap ?? throw new ArgumentNullException("heightMap");
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height)
            {
                return AERIS39HeightMapPureCpuExact.Evaluate(
                    HeightMap, u, v, height);
            }
        }

        internal sealed class RidgedAltitudeCurveOpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly float Deformity;
            internal readonly double RadiusMin;
            internal readonly float RidgedMinimum;
            internal readonly double SimplexHeightStart;
            internal readonly double SimplexHeightEnd;
            internal readonly double HDeltaR;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot Simplex;
            internal readonly AERISR039MinmusPureCpuExact.RidgedSnapshot RidgedAdd;
            internal readonly AERISR041MohoDresPureCpuExact.CurveSnapshot Curve;

            internal RidgedAltitudeCurveOpSnapshot(
                float deformity,
                double radiusMin,
                float ridgedMinimum,
                double simplexHeightStart,
                double simplexHeightEnd,
                double hDeltaR,
                AERISR039MinmusPureCpuExact.SimplexSnapshot simplex,
                AERISR039MinmusPureCpuExact.RidgedSnapshot ridgedAdd,
                AERISR041MohoDresPureCpuExact.CurveSnapshot curve)
            {
                Deformity = deformity;
                RadiusMin = radiusMin;
                RidgedMinimum = ridgedMinimum;
                SimplexHeightStart = simplexHeightStart;
                SimplexHeightEnd = simplexHeightEnd;
                HDeltaR = hDeltaR;
                Simplex = simplex ?? throw new ArgumentNullException("simplex");
                RidgedAdd = ridgedAdd ?? throw new ArgumentNullException("ridgedAdd");
                Curve = curve ?? throw new ArgumentNullException("curve");
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height)
            {
                // Exact stock PQSMod_VertexRidgedAltitudeCurve operation order.
                double h = height - RadiusMin;
                float t;
                if (h <= SimplexHeightStart)
                {
                    t = 0f;
                }
                else if (h >= SimplexHeightEnd)
                {
                    t = 1f;
                }
                else
                {
                    t = (float)((h - SimplexHeightStart) * HDeltaR);
                }

                double s = AERISR039MinmusPureCpuExact.SimplexNoiseNormalized(
                    Simplex, x, y, z, Simplex.Persistence);
                if (s == 0.0)
                    return height;

                double r = Math.Max(
                    (double)RidgedMinimum,
                    AERISR039MinmusPureCpuExact.RidgedGetValue(RidgedAdd, x, y, z));
                r = r * Math.Max(s, 0.0);

                if (r < -1.0) r = -1.0;
                if (r > 1.0) r = 1.0;

                double delta = r * (double)Deformity;
                delta = delta * (double)AERISR041MohoDresPureCpuExact.EvaluateCurve(Curve, t);
                return height + delta;
            }
        }

        internal sealed class ChainSnapshot
        {
            internal readonly AERISR041MohoDresPureCpuExact.HeightOpSnapshot[] Ops;

            internal ChainSnapshot(
                AERISR041MohoDresPureCpuExact.HeightOpSnapshot[] ops)
            {
                if (ops == null || ops.Length == 0)
                    throw new ArgumentException("height modifier chain requires ops", "ops");

                Ops = new AERISR041MohoDresPureCpuExact.HeightOpSnapshot[ops.Length];
                for (int i = 0; i < ops.Length; i++)
                {
                    if (ops[i] == null)
                        throw new ArgumentException("height modifier op is null", "ops");
                    Ops[i] = ops[i];
                }
            }
        }

        internal static double Evaluate(
            ChainSnapshot snapshot,
            double x,
            double y,
            double z,
            double u,
            double v,
            double inputHeight)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            double height = inputHeight;
            for (int i = 0; i < snapshot.Ops.Length; i++)
                height = snapshot.Ops[i].Evaluate(x, y, z, u, v, height);
            return height;
        }

        internal static long DoubleBits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value);
        }
    }
}
