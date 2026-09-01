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
            AbsolutePolynomialDouble = 4,
            CompiledAbsoluteFloatHorner = 5,
            CompiledAbsoluteDoubleToFloatHorner = 6,
            CompiledAbsoluteDoubleToFloatDoubleHorner = 7,
            CompiledLocalDoubleToFloatHorner = 8,
            CompiledNormalizedDoubleToFloatHorner = 9,
            CompiledNormalizedFloatDoubleHorner = 10,
            CompiledAbsoluteFloatExpanded = 11
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

                case RidgedCurveEvaluationMode.CompiledAbsoluteFloatHorner:
                case RidgedCurveEvaluationMode.CompiledAbsoluteFloatExpanded:
                {
                    float t0 = k0.Time;
                    float p0 = k0.Value;
                    float m0 = k0.OutTangent;
                    float t1 = k1.Time;
                    float p1 = k1.Value;
                    float m1 = k1.InTangent;
                    float t0Sq = t0 * t0;
                    float t0Cu = t0Sq * t0;
                    float t1Sq = t1 * t1;
                    float t1Cu = t1Sq * t1;
                    float divisor = t0Cu - t1Cu + 3f * t0 * t1 * (t1 - t0);
                    if (divisor == 0f) return k1.Value;
                    float a = ((m0 + m1) * (t0 - t1) + (p1 - p0) * 2f) / divisor;
                    float b = (2f * (t1Sq * m0 - t0Sq * m1) - t0Sq * m0 +
                        t1Sq * m1 + t0 * t1 * (m1 - m0) +
                        3f * (t0 + t1) * (p0 - p1)) / divisor;
                    float c = (t0Cu * m1 - t1Cu * m0 +
                        t0 * t1 * (t0 * (2f * m0 + m1) - t1 * (m0 + 2f * m1)) +
                        6f * t0 * t1 * (p1 - p0)) / divisor;
                    float d = ((t0 * t1Sq - t0Sq * t1) * (t1 * m0 + t0 * m1) -
                        p0 * t1Cu + t0Cu * p1 +
                        3f * t0 * t1 * (t1 * p0 - t0 * p1)) / divisor;

                    if (mode == RidgedCurveEvaluationMode.CompiledAbsoluteFloatExpanded)
                    {
                        float cubic = ((a * t) * t) * t;
                        float quadratic = (b * t) * t;
                        float result = cubic + quadratic;
                        result = result + c * t;
                        result = result + d;
                        return result;
                    }

                    return d + t * (c + t * (b + t * a));
                }

                case RidgedCurveEvaluationMode.CompiledAbsoluteDoubleToFloatHorner:
                case RidgedCurveEvaluationMode.CompiledAbsoluteDoubleToFloatDoubleHorner:
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
                    float a = (float)(((m0 + m1) * (t0 - t1) + (p1 - p0) * 2.0) / divisor);
                    float b = (float)((2.0 * (t1Sq * m0 - t0Sq * m1) - t0Sq * m0 +
                        t1Sq * m1 + t0 * t1 * (m1 - m0) +
                        3.0 * (t0 + t1) * (p0 - p1)) / divisor);
                    float c = (float)((t0Cu * m1 - t1Cu * m0 +
                        t0 * t1 * (t0 * (2.0 * m0 + m1) - t1 * (m0 + 2.0 * m1)) +
                        6.0 * t0 * t1 * (p1 - p0)) / divisor);
                    float d = (float)(((t0 * t1Sq - t0Sq * t1) * (t1 * m0 + t0 * m1) -
                        p0 * t1Cu + t0Cu * p1 +
                        3.0 * t0 * t1 * (t1 * p0 - t0 * p1)) / divisor);

                    if (mode == RidgedCurveEvaluationMode.CompiledAbsoluteDoubleToFloatDoubleHorner)
                    {
                        double td = (double)t;
                        double result = (double)d + td * ((double)c + td * ((double)b + td * (double)a));
                        return (float)result;
                    }

                    return d + t * (c + t * (b + t * a));
                }

                case RidgedCurveEvaluationMode.CompiledLocalDoubleToFloatHorner:
                {
                    double t0 = (double)k0.Time;
                    double t1 = (double)k1.Time;
                    double dtd = t1 - t0;
                    double p0 = (double)k0.Value;
                    double p1 = (double)k1.Value;
                    double m0 = (double)k0.OutTangent;
                    double m1 = (double)k1.InTangent;
                    double slope = (p1 - p0) / dtd;
                    float a = (float)((m0 + m1 - 2.0 * slope) / (dtd * dtd));
                    float b = (float)((3.0 * slope - 2.0 * m0 - m1) / dtd);
                    float c = (float)m0;
                    float d = (float)p0;
                    float x = t - k0.Time;
                    return d + x * (c + x * (b + x * a));
                }

                case RidgedCurveEvaluationMode.CompiledNormalizedDoubleToFloatHorner:
                {
                    double dtd = (double)k1.Time - (double)k0.Time;
                    double p0 = (double)k0.Value;
                    double p1 = (double)k1.Value;
                    double m0 = (double)k0.OutTangent * dtd;
                    double m1 = (double)k1.InTangent * dtd;
                    float a = (float)(2.0 * (p0 - p1) + m0 + m1);
                    float b = (float)(3.0 * (p1 - p0) - 2.0 * m0 - m1);
                    float c = (float)m0;
                    float d = (float)p0;
                    float u = (t - k0.Time) / dt;
                    return d + u * (c + u * (b + u * a));
                }

                case RidgedCurveEvaluationMode.CompiledNormalizedFloatDoubleHorner:
                {
                    float m0 = k0.OutTangent * dt;
                    float m1 = k1.InTangent * dt;
                    float a = 2f * (k0.Value - k1.Value) + m0 + m1;
                    float b = 3f * (k1.Value - k0.Value) - 2f * m0 - m1;
                    float c = m0;
                    float d = k0.Value;
                    float u = (t - k0.Time) / dt;
                    double ud = (double)u;
                    double result = (double)d + ud * ((double)c + ud * ((double)b + ud * (double)a));
                    return (float)result;
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
