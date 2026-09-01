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
            UnityNativeCacheFloat = 5,
            UnityNativeCacheStrictBinary32 = 6,
            NormalizedPolynomialStrictBinary32 = 7,
            HermiteStrictBinary32 = 8,
            NormalizedPolynomialStrictBinary32Reciprocal = 9,
            UnityNativeCacheStrictBinary32Reciprocal = 10,
            BezierDefaultWeightStrictBinary32 = 11,
            BezierDefaultWeightDeCasteljauStrictBinary32 = 12
        }

        internal const int RidgedCurveEvaluationModeCount = 13;

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
            if (t < keys[0].Time) return keys[0].Value;
            if (t >= keys[keys.Length - 1].Time)
                return keys[keys.Length - 1].Value;

            // Unity FindIndexForSampling uses upper-bound semantics: when curveT is
            // exactly an internal key time, that key is the lhs of the next segment.
            int right = 1;
            while (right < keys.Length - 1 && t >= keys[right].Time)
                right++;

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

                case RidgedCurveEvaluationMode.UnityNativeCacheFloat:
                {
                    // Historical Unity Runtime/Math/AnimationCurve.cpp cache path,
                    // expressed directly in managed float arithmetic.
                    float dx = k1.Time - k0.Time;
                    dx = Math.Max(dx, 0.0001f);
                    float dy = k1.Value - k0.Value;
                    float length = 1.0f / (dx * dx);
                    float d1 = k0.OutTangent * dx;
                    float d2 = k1.InTangent * dx;
                    float c0 = (d1 + d2 - dy - dy) * length / dx;
                    float c1 = (dy + dy + dy - d1 - d1 - d2) * length;
                    float c2 = k0.OutTangent;
                    float c3 = k0.Value;
                    float localT = t - k0.Time;
                    return (localT * (localT * (localT * c0 + c1) + c2)) + c3;
                }

                case RidgedCurveEvaluationMode.UnityNativeCacheStrictBinary32:
                    return EvaluateUnityNativeCacheStrict(k0, k1, t, false);

                case RidgedCurveEvaluationMode.UnityNativeCacheStrictBinary32Reciprocal:
                    return EvaluateUnityNativeCacheStrict(k0, k1, t, true);

                case RidgedCurveEvaluationMode.NormalizedPolynomialStrictBinary32:
                    return EvaluateNormalizedPolynomialStrict(k0, k1, t, false);

                case RidgedCurveEvaluationMode.NormalizedPolynomialStrictBinary32Reciprocal:
                    return EvaluateNormalizedPolynomialStrict(k0, k1, t, true);

                case RidgedCurveEvaluationMode.HermiteStrictBinary32:
                    return EvaluateHermiteStrict(k0, k1, t);

                case RidgedCurveEvaluationMode.BezierDefaultWeightStrictBinary32:
                    return EvaluateBezierDefaultWeightStrict(k0, k1, t, false);

                case RidgedCurveEvaluationMode.BezierDefaultWeightDeCasteljauStrictBinary32:
                    return EvaluateBezierDefaultWeightStrict(k0, k1, t, true);

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        static float EvaluateUnityNativeCacheStrict(
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k0,
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k1,
            float t,
            bool reciprocalLocal)
        {
            // Reproduce the C++ source one binary32 operation at a time. The B32*
            // helpers prevent the CLR/JIT from retaining extra precision across a
            // source-level operation boundary.
            float dx = B32Sub(k1.Time, k0.Time);
            if (dx < 0.0001f) dx = 0.0001f;
            float dy = B32Sub(k1.Value, k0.Value);
            float dx2 = B32Mul(dx, dx);
            float length = B32Div(1.0f, dx2);
            float d1 = B32Mul(k0.OutTangent, dx);
            float d2 = B32Mul(k1.InTangent, dx);

            float n0 = B32Add(d1, d2);
            n0 = B32Sub(n0, dy);
            n0 = B32Sub(n0, dy);
            float c0 = B32Mul(n0, length);
            c0 = B32Div(c0, dx);

            float n1 = B32Add(dy, dy);
            n1 = B32Add(n1, dy);
            n1 = B32Sub(n1, d1);
            n1 = B32Sub(n1, d1);
            n1 = B32Sub(n1, d2);
            float c1 = B32Mul(n1, length);
            float c2 = k0.OutTangent;
            float c3 = k0.Value;

            float localT = B32Sub(t, k0.Time);
            if (reciprocalLocal)
            {
                // Diagnostic alternate parameterization: preserve the same cubic but
                // reconstruct local time through normalized u and dx using strict ops.
                float invDx = B32Div(1.0f, dx);
                float u = B32Mul(localT, invDx);
                localT = B32Mul(u, dx);
            }

            float s1 = B32Add(B32Mul(localT, c0), c1);
            float s2 = B32Add(B32Mul(localT, s1), c2);
            return B32Add(B32Mul(localT, s2), c3);
        }

        static float EvaluateNormalizedPolynomialStrict(
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k0,
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k1,
            float t,
            bool reciprocal)
        {
            float dt = B32Sub(k1.Time, k0.Time);
            float local = B32Sub(t, k0.Time);
            float u = reciprocal
                ? B32Mul(local, B32Div(1.0f, dt))
                : B32Div(local, dt);
            float m0 = B32Mul(k0.OutTangent, dt);
            float m1 = B32Mul(k1.InTangent, dt);

            float a = B32Mul(2.0f, k0.Value);
            a = B32Sub(a, B32Mul(2.0f, k1.Value));
            a = B32Add(a, m0);
            a = B32Add(a, m1);

            float b = B32Mul(-3.0f, k0.Value);
            b = B32Add(b, B32Mul(3.0f, k1.Value));
            b = B32Sub(b, B32Mul(2.0f, m0));
            b = B32Sub(b, m1);

            float result = B32Add(B32Mul(a, u), b);
            result = B32Add(B32Mul(result, u), m0);
            return B32Add(B32Mul(result, u), k0.Value);
        }

        static float EvaluateHermiteStrict(
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k0,
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k1,
            float t)
        {
            float dt = B32Sub(k1.Time, k0.Time);
            float u = B32Div(B32Sub(t, k0.Time), dt);
            float m0 = B32Mul(k0.OutTangent, dt);
            float m1 = B32Mul(k1.InTangent, dt);
            float u2 = B32Mul(u, u);
            float u3 = B32Mul(u2, u);

            float h00 = B32Sub(B32Mul(2.0f, u3), B32Mul(3.0f, u2));
            h00 = B32Add(h00, 1.0f);
            float h10 = B32Sub(u3, B32Mul(2.0f, u2));
            h10 = B32Add(h10, u);
            float h11 = B32Sub(u3, u2);
            float h01 = B32Sub(B32Mul(-2.0f, u3), B32Mul(-3.0f, u2));

            float result = B32Mul(h00, k0.Value);
            result = B32Add(result, B32Mul(h10, m0));
            result = B32Add(result, B32Mul(h11, m1));
            return B32Add(result, B32Mul(h01, k1.Value));
        }

        static float EvaluateBezierDefaultWeightStrict(
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k0,
            AERISR041MohoDresPureCpuExact.CurveKeySnapshot k1,
            float t,
            bool deCasteljau)
        {
            float dt = B32Sub(k1.Time, k0.Time);
            float u = B32Div(B32Sub(t, k0.Time), dt);
            float m0 = B32Mul(k0.OutTangent, dt);
            float m1 = B32Mul(k1.InTangent, dt);
            float w = B32Div(1.0f, 3.0f);
            float p0 = k0.Value;
            float p1 = B32Add(p0, B32Mul(w, m0));
            float p3 = k1.Value;
            float p2 = B32Sub(p3, B32Mul(w, m1));

            if (deCasteljau)
            {
                float a = B32Add(p0, B32Mul(B32Sub(p1, p0), u));
                float b = B32Add(p1, B32Mul(B32Sub(p2, p1), u));
                float c = B32Add(p2, B32Mul(B32Sub(p3, p2), u));
                float d = B32Add(a, B32Mul(B32Sub(b, a), u));
                float e = B32Add(b, B32Mul(B32Sub(c, b), u));
                return B32Add(d, B32Mul(B32Sub(e, d), u));
            }

            float u2 = B32Mul(u, u);
            float u3 = B32Mul(u2, u);
            float omt = B32Sub(1.0f, u);
            float omt2 = B32Mul(omt, omt);
            float omt3 = B32Mul(omt2, omt);
            float result = B32Mul(omt3, p0);
            result = B32Add(result,
                B32Mul(B32Mul(B32Mul(3.0f, u), omt2), p1));
            result = B32Add(result,
                B32Mul(B32Mul(B32Mul(3.0f, u2), omt), p2));
            return B32Add(result, B32Mul(u3, p3));
        }

        // These helpers model one IEEE-754 binary32 arithmetic operation followed by
        // immediate round-to-nearest-even. Products and sums of binary32 operands are
        // exactly representable in binary64; division is computed at binary64 precision
        // before the explicit binary32 rounding, which is sufficient for this witness
        // unless the live bit comparison proves otherwise.
        static float B32Add(float a, float b)
        {
            return (float)((double)a + (double)b);
        }

        static float B32Sub(float a, float b)
        {
            return (float)((double)a - (double)b);
        }

        static float B32Mul(float a, float b)
        {
            return (float)((double)a * (double)b);
        }

        static float B32Div(float a, float b)
        {
            return (float)((double)a / (double)b);
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
