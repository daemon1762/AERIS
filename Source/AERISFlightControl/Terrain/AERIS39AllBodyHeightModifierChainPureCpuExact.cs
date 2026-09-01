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
        internal enum RidgedCurveEvaluationMode
        {
            LegacyHermiteBasisFloat = 0,
            LegacyPolynomialFloat = 1,
            LegacyHermiteBasisDouble = 2,
            LegacyPolynomialDouble = 3,
            HermiteReciprocalFloat = 4,
            HermiteGroupedFloat = 5,
            PolynomialGroupedFloat = 6,
            LocalPolynomialFloat = 7,
            LocalPolynomialReciprocalFloat = 8,
            LocalPolynomialDouble = 9,
            AbsolutePolynomialDouble = 10,
            AbsolutePolynomialFloat = 11
        }

        internal const int RidgedCurveEvaluationModeCount = 12;

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
            internal readonly RidgedCurveEvaluationMode CurveMode;

            internal RidgedAltitudeCurveOpSnapshot(
                float deformity,
                double radiusMin,
                float ridgedMinimum,
                double simplexHeightStart,
                double simplexHeightEnd,
                double hDeltaR,
                AERISR039MinmusPureCpuExact.SimplexSnapshot simplex,
                AERISR039MinmusPureCpuExact.RidgedSnapshot ridgedAdd,
                AERISR041MohoDresPureCpuExact.CurveSnapshot curve,
                RidgedCurveEvaluationMode curveMode)
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
                CurveMode = curveMode;
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
                delta = delta * (double)EvaluateRidgedCurve(Curve, CurveMode, t);
                return height + delta;
            }
        }

        internal static float EvaluateRidgedCurve(
            AERISR041MohoDresPureCpuExact.CurveSnapshot curve,
            RidgedCurveEvaluationMode mode,
            float t)
        {
            if (curve == null) throw new ArgumentNullException("curve");

            if ((int)mode >= 0 && (int)mode <= 3)
            {
                var legacy = new AERISR041MohoDresPureCpuExact.CurveSnapshot(
                    curve.Keys,
                    (AERISR041MohoDresPureCpuExact.CurveEvaluationMode)(int)mode,
                    curve.PreWrapMode,
                    curve.PostWrapMode);
                return AERISR041MohoDresPureCpuExact.EvaluateCurve(legacy, t);
            }

            AERISR041MohoDresPureCpuExact.CurveKeySnapshot[] keys = curve.Keys;
            if (keys == null || keys.Length == 0) return 0f;
            if (keys.Length == 1) return keys[0].Value;
            if (t <= keys[0].Time) return keys[0].Value;
            if (t >= keys[keys.Length - 1].Time)
                return keys[keys.Length - 1].Value;

            int right = 1;
            while (right < keys.Length && t > keys[right].Time)
                right++;
            if (right >= keys.Length)
                return keys[keys.Length - 1].Value;

            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k0 = keys[right - 1];
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k1 = keys[right];
            float dt = k1.Time - k0.Time;
            if (dt == 0f) return k1.Value;
            if (float.IsInfinity(k0.OutTangent) || float.IsInfinity(k1.InTangent))
                return k0.Value;

            switch (mode)
            {
                case RidgedCurveEvaluationMode.HermiteReciprocalFloat:
                {
                    float invDt = 1f / dt;
                    float uf = (t - k0.Time) * invDt;
                    float m0 = k0.OutTangent * dt;
                    float m1 = k1.InTangent * dt;
                    float u2 = uf * uf;
                    float u3 = u2 * uf;
                    float h00 = 2f * u3 - 3f * u2 + 1f;
                    float h10 = u3 - 2f * u2 + uf;
                    float h01 = -2f * u3 + 3f * u2;
                    float h11 = u3 - u2;
                    float result = k0.Value * h00;
                    result = result + m0 * h10;
                    result = result + k1.Value * h01;
                    result = result + m1 * h11;
                    return result;
                }

                case RidgedCurveEvaluationMode.HermiteGroupedFloat:
                {
                    float uf = (t - k0.Time) / dt;
                    float m0 = k0.OutTangent * dt;
                    float m1 = k1.InTangent * dt;
                    float u2 = uf * uf;
                    float h00 = ((2f * uf - 3f) * u2) + 1f;
                    float h10 = ((uf - 2f) * uf + 1f) * uf;
                    float h01 = (-2f * uf + 3f) * u2;
                    float h11 = (uf - 1f) * u2;
                    float left = k0.Value * h00 + m0 * h10;
                    float rightValue = k1.Value * h01 + m1 * h11;
                    return left + rightValue;
                }

                case RidgedCurveEvaluationMode.PolynomialGroupedFloat:
                {
                    float uf = (t - k0.Time) / dt;
                    float m0 = k0.OutTangent * dt;
                    float m1 = k1.InTangent * dt;
                    float a = 2f * (k0.Value - k1.Value) + m0 + m1;
                    float b = 3f * (k1.Value - k0.Value) - 2f * m0 - m1;
                    return ((a * uf + b) * uf + m0) * uf + k0.Value;
                }

                case RidgedCurveEvaluationMode.LocalPolynomialFloat:
                {
                    float x = t - k0.Time;
                    float dy = k1.Value - k0.Value;
                    float slope = dy / dt;
                    float c3 = (k0.OutTangent + k1.InTangent - 2f * slope) / (dt * dt);
                    float c2 = (3f * slope - 2f * k0.OutTangent - k1.InTangent) / dt;
                    return ((c3 * x + c2) * x + k0.OutTangent) * x + k0.Value;
                }

                case RidgedCurveEvaluationMode.LocalPolynomialReciprocalFloat:
                {
                    float x = t - k0.Time;
                    float invDt = 1f / dt;
                    float slope = (k1.Value - k0.Value) * invDt;
                    float c3 = (k0.OutTangent + k1.InTangent - 2f * slope) * invDt * invDt;
                    float c2 = (3f * slope - 2f * k0.OutTangent - k1.InTangent) * invDt;
                    return ((c3 * x + c2) * x + k0.OutTangent) * x + k0.Value;
                }

                case RidgedCurveEvaluationMode.LocalPolynomialDouble:
                {
                    double x = (double)t - (double)k0.Time;
                    double dtd = (double)k1.Time - (double)k0.Time;
                    double slope = ((double)k1.Value - (double)k0.Value) / dtd;
                    double c3 = ((double)k0.OutTangent + (double)k1.InTangent - 2.0 * slope) /
                        (dtd * dtd);
                    double c2 = (3.0 * slope - 2.0 * (double)k0.OutTangent -
                        (double)k1.InTangent) / dtd;
                    double result = ((c3 * x + c2) * x + (double)k0.OutTangent) * x +
                        (double)k0.Value;
                    return (float)result;
                }

                case RidgedCurveEvaluationMode.AbsolutePolynomialDouble:
                {
                    double t0 = (double)k0.Time;
                    double p0 = (double)k0.Value;
                    double m0 = (double)k0.OutTangent;
                    double t1 = (double)k1.Time;
                    double p1 = (double)k1.Value;
                    double m1 = (double)k1.InTangent;
                    double t0Sq = t0 * t0;
                    double t0Cu = t0Sq * t0;
                    double t1Sq = t1 * t1;
                    double t1Cu = t1Sq * t1;
                    double divisor = t0Cu - t1Cu + 3.0 * t0 * t1 * (t1 - t0);
                    if (divisor == 0.0) return k1.Value;
                    double a = ((m0 + m1) * (t0 - t1) + (p1 - p0) * 2.0) / divisor;
                    double b = (2.0 * (t1Sq * m0 - t0Sq * m1) - t0Sq * m0 +
                        t1Sq * m1 + t0 * t1 * (m1 - m0) +
                        3.0 * (t0 + t1) * (p0 - p1)) / divisor;
                    double c = (t0Cu * m1 - t1Cu * m0 +
                        t0 * t1 * (t0 * (2.0 * m0 + m1) - t1 * (m0 + 2.0 * m1)) +
                        6.0 * t0 * t1 * (p1 - p0)) / divisor;
                    double d = ((t0 * t1Sq - t0Sq * t1) * (t1 * m0 + t0 * m1) -
                        p0 * t1Cu + t0Cu * p1 +
                        3.0 * t0 * t1 * (t1 * p0 - t0 * p1)) / divisor;
                    double td = (double)t;
                    return (float)(d + td * (c + td * (b + td * a)));
                }

                case RidgedCurveEvaluationMode.AbsolutePolynomialFloat:
                {
                    float localC = k0.OutTangent;
                    float slope = (k1.Value - k0.Value) / dt;
                    float localA = (k0.OutTangent + k1.InTangent - 2f * slope) / (dt * dt);
                    float localB = (3f * slope - 2f * k0.OutTangent - k1.InTangent) / dt;
                    float t0 = k0.Time;
                    float t0Sq = t0 * t0;
                    float absoluteA = localA;
                    float absoluteB = localB - 3f * localA * t0;
                    float absoluteC = localC - 2f * localB * t0 + 3f * localA * t0Sq;
                    float absoluteD = k0.Value - localC * t0 + localB * t0Sq -
                        localA * t0Sq * t0;
                    return absoluteD + t * (absoluteC + t * (absoluteB + t * absoluteA));
                }

                default:
                    throw new ArgumentOutOfRangeException("mode");
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
