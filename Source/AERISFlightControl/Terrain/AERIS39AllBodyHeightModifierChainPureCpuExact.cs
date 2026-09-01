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
            UnityNativeCacheFmaInner = 6,
            UnityNativeCacheFmaMiddle = 7,
            UnityNativeCacheFmaOuter = 8,
            UnityNativeCacheFmaInnerMiddle = 9,
            UnityNativeCacheFmaInnerOuter = 10,
            UnityNativeCacheFmaMiddleOuter = 11,
            UnityNativeCacheFmaAll = 12
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
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaInner:
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaMiddle:
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaOuter:
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaInnerMiddle:
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaInnerOuter:
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaMiddleOuter:
                case RidgedCurveEvaluationMode.UnityNativeCacheFmaAll:
                {
                    // Unity native AnimationCurve cache path:
                    // Runtime/Math/AnimationCurve.cpp CalculateCacheData + EvaluateCache.
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

                    bool fuseInner =
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaInner ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaInnerMiddle ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaInnerOuter ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaAll;
                    bool fuseMiddle =
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaMiddle ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaInnerMiddle ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaMiddleOuter ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaAll;
                    bool fuseOuter =
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaOuter ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaInnerOuter ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaMiddleOuter ||
                        mode == RidgedCurveEvaluationMode.UnityNativeCacheFmaAll;

                    float s1 = fuseInner
                        ? FmaFloatViaDouble(localT, c0, c1)
                        : localT * c0 + c1;
                    float s2 = fuseMiddle
                        ? FmaFloatViaDouble(localT, s1, c2)
                        : localT * s1 + c2;
                    return fuseOuter
                        ? FmaFloatViaDouble(localT, s2, c3)
                        : localT * s2 + c3;
                }

                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
        }

        // Diagnostic software binary32 fused multiply-add candidate. The product of
        // two binary32 values is exactly representable in binary64; the final cast
        // applies one binary32 rounding after the multiply/add pair. Acceptance is
        // still determined only by the live Unity bit witness, never by assumption.
        private static float FmaFloatViaDouble(float a, float b, float c)
        {
            return (float)(((double)a * (double)b) + (double)c);
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
                for (int i = 0; i < snapshot.Ops.Length; i++)
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
