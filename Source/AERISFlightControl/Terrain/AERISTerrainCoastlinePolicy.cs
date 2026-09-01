using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using AERISFlightControl.Logging;

namespace AERISFlightControl.Terrain
{
    // Shared land/water boundary policy. Terrain flags are sampled at grid vertices.
    // CP3.75 Candidate 9 establishes one boundary authority for both the coastline line
    // and sparse land/water fill: the classified edge plus the proven Golden land inset.
    // Elevation remains terrain/shading data, but must not independently move the
    // coastline crossing because the persisted HD payload contains the 129x129 class mask
    // while sparse fill heights are reconstructed from the low-resolution base tile.
    internal static class AERISTerrainCoastlinePolicy
    {
        // Retained as the terrain sampling classification threshold for compatibility.
        // Candidate 9 no longer uses it to derive a second presentation crossing.
        internal const float WaterElevationThresholdMeters = 1.0f;
        internal const float LandInsetFraction = 0.38f;
        // Operation Health Step 2: smooth only the sub-cell crossing location. The
        // source 129x129 land/water class sign never changes, so islands and coastline
        // connectivity remain exactly under the persisted Candidate11 authority.
        internal const float PresentationSmoothingBlend = 0.65f;
        internal const float PresentationMinimumBoundaryMagnitude = 0.20f;

        internal static float CrossingFraction(bool water0, bool water1)
        {
            if (water0 == water1) return 0.5f;
            return water0 ? 1f - LandInsetFraction : LandInsetFraction;
        }

        internal static float CrossingFraction(bool water0, bool water1,
            float elevation0Meters, float elevation1Meters)
        {
            // Candidate 9 unified coastal-boundary authority. The HD coastline extractor
            // and sparse correction clipper must return bit-for-bit identical crossing
            // coordinates for the same classified edge. Do not let independently sourced
            // elevation fields shift the painter boundary away from the coastline vector.
            return CrossingFraction(water0, water1);
        }

        internal static float[] BuildPresentationBoundaryField(byte[] flags,
            int resolution)
        {
            if (flags == null || resolution < 2 ||
                flags.Length != resolution * resolution) return new float[0];
            var field = new float[flags.Length];
            for (int row = 0; row < resolution; row++)
            {
                for (int column = 0; column < resolution; column++)
                {
                    int index = row * resolution + column;
                    byte own = flags[index];
                    if (own == 0)
                    {
                        field[index] = 0f;
                        continue;
                    }
                    float rawSign = own == 2 ? -1f : 1f;
                    float weighted = 0f;
                    float weight = 0f;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int py = row + dy;
                        if (py < 0 || py >= resolution) continue;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int px = column + dx;
                            if (px < 0 || px >= resolution) continue;
                            byte neighbour = flags[py * resolution + px];
                            if (neighbour == 0) continue;
                            float sign = neighbour == 2 ? -1f : 1f;
                            int kernel = dx == 0 && dy == 0 ? 4 :
                                (dx == 0 || dy == 0 ? 2 : 1);
                            weighted += sign * kernel;
                            weight += kernel;
                        }
                    }
                    float filtered = weight <= 0f ? rawSign : weighted / weight;
                    // Sign preservation is a hard topology rule. Even when the local
                    // majority is the opposite class, only confidence magnitude changes.
                    float aligned = rawSign * filtered;
                    float magnitude = (float)System.Math.Max(
                        PresentationMinimumBoundaryMagnitude,
                        System.Math.Min(1.0, aligned));
                    field[index] = rawSign * magnitude;
                }
            }
            return field;
        }

        internal static float PresentationCrossingFraction(bool water0,
            bool water1, float scalar0, float scalar1)
        {
            float golden = CrossingFraction(water0, water1);
            if (water0 == water1 || float.IsNaN(scalar0) ||
                float.IsNaN(scalar1) || float.IsInfinity(scalar0) ||
                float.IsInfinity(scalar1) || scalar0 * scalar1 >= 0f)
                return golden;
            float denominator = scalar0 - scalar1;
            if (System.Math.Abs(denominator) <= 0.000001f) return golden;
            float zero = scalar0 / denominator;
            if (float.IsNaN(zero) || float.IsInfinity(zero)) return golden;
            zero = (float)System.Math.Max(0.18, System.Math.Min(0.82, zero));
            float blended = golden + (zero - golden) *
                PresentationSmoothingBlend;
            return (float)System.Math.Max(0.24, System.Math.Min(0.76, blended));
        }

        internal static float Interpolate(float value0, float value1,
            bool water0, bool water1)
        {
            float t = CrossingFraction(water0, water1);
            return value0 + (value1 - value0) * t;
        }

        internal static float Interpolate(float value0, float value1,
            bool water0, bool water1, float elevation0Meters,
            float elevation1Meters)
        {
            float t = CrossingFraction(water0, water1, elevation0Meters,
                elevation1Meters);
            return value0 + (value1 - value0) * t;
        }
    }

    // AERIS40 temporary R041 diagnostic. This is deliberately isolated from the
    // production terrain pipeline and from the accepted shadow observer. It calls the
    // live Unity AnimationCurve only on the main thread, then tries to recover the
    // binary32 cubic-cache coefficients that reproduce those live results bit-for-bit.
    // No recovered state is used by production in this revision.
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    internal sealed class AERIS40RidgedCurveCoefficientRecoveryObserver : MonoBehaviour
    {
        const string Prefix = "[AERIS39][HEIGHT_CHAIN_CURVE_RECOVERY";
        bool emitted;
        float nextAttempt;
        int mainThreadId;

        struct Candidate
        {
            internal float C0;
            internal float C1;
            internal int Matches;
            internal double MaxError;
        }

        void Awake()
        {
            mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
        }

        void Update()
        {
            if (emitted) return;
            if (Time.realtimeSinceStartup < nextAttempt) return;
            nextAttempt = Time.realtimeSinceStartup + 1f;
            if (System.Threading.Thread.CurrentThread.ManagedThreadId != mainThreadId) return;
            if (FlightGlobals.Bodies == null || FlightGlobals.Bodies.Count == 0) return;
            emitted = true;

            try
            {
                AnimationCurve curve = FindKerbinRidgedCurve();
                Recover(curve);
            }
            catch (Exception ex)
            {
                AERISLogger.Error(
                    Prefix + "_FAIL]" +
                    "; error=" + Safe(ex.GetType().FullName + ":" + ex.Message) +
                    Invariants());
            }
        }

        static AnimationCurve FindKerbinRidgedCurve()
        {
            CelestialBody kerbin = null;
            for (int i = 0; i < FlightGlobals.Bodies.Count; i++)
            {
                CelestialBody body = FlightGlobals.Bodies[i];
                if (body != null && string.Equals(body.name, "Kerbin", StringComparison.Ordinal))
                {
                    kerbin = body;
                    break;
                }
            }
            if (kerbin == null || kerbin.pqsController == null)
                throw new InvalidOperationException("KERBIN_PQS_MISSING");

            IList mods = GetModifierList(kerbin.pqsController);
            if (mods == null) throw new InvalidOperationException("KERBIN_PQS_MODIFIER_LIST_MISSING");

            for (int i = 0; i < mods.Count; i++)
            {
                object mod = mods[i];
                if (mod == null) continue;
                if (!string.Equals(mod.GetType().Name, "PQSMod_VertexRidgedAltitudeCurve",
                    StringComparison.Ordinal)) continue;

                object raw;
                if (!TryReadMember(mod, "simplexCurve", out raw))
                    throw new MissingMemberException(mod.GetType().FullName, "simplexCurve");
                AnimationCurve curve = raw as AnimationCurve;
                if (curve == null) throw new InvalidOperationException("KERBIN_RIDGED_CURVE_MISSING");
                return curve;
            }

            throw new InvalidOperationException("KERBIN_RIDGED_MODIFIER_MISSING");
        }

        static void Recover(AnimationCurve curve)
        {
            if (curve == null) throw new ArgumentNullException("curve");
            Keyframe[] keys = curve.keys;
            if (keys == null || keys.Length < 2)
                throw new InvalidOperationException("RIDGED_CURVE_KEYS_MISSING");

            int segments = keys.Length - 1;
            var coeff = new float[segments * 4];
            bool segmentsExact = true;

            AERISLogger.Info(
                Prefix + "_BEGIN]" +
                "; body=Kerbin" +
                "; segments=" + segments +
                "; strategy=LIVE_EVALUATE_ULP_COEFFICIENT_RECOVERY" +
                "; live_calls_thread=MAIN_THREAD_ONLY" + Invariants());

            for (int segment = 0; segment < segments; segment++)
            {
                Keyframe k0 = keys[segment];
                Keyframe k1 = keys[segment + 1];
                float originalDx = k1.time - k0.time;
                float dx = Math.Max(originalDx, 0.0001f);
                float dy = k1.value - k0.value;
                float length = 1.0f / (dx * dx);
                float d1 = k0.outTangent * dx;
                float d2 = k1.inTangent * dx;
                float theoryC0 = (d1 + d2 - dy - dy) * length / dx;
                float theoryC1 = (dy + dy + dy - d1 - d1 - d2) * length;
                float c2 = k0.outTangent;
                float c3 = k0.value;

                float[] probeT;
                float[] probeLive;
                BuildSegmentProbes(curve, k0.time, k1.time, out probeT, out probeLive);

                float estimateC0;
                float estimateC1;
                EstimateCoefficients(k0.time, originalDx, c2, c3,
                    probeT, probeLive, theoryC0, theoryC1,
                    out estimateC0, out estimateC1);

                Candidate best = Score(k0.time, theoryC0, theoryC1, c2, c3,
                    probeT, probeLive);
                SearchSquare(k0.time, estimateC0, estimateC1, 16, c2, c3,
                    probeT, probeLive, ref best);
                SearchSquare(k0.time, theoryC0, theoryC1, 16, c2, c3,
                    probeT, probeLive, ref best);

                if (best.Matches != probeT.Length)
                {
                    SearchAxis(k0.time, best.C0, best.C1, true, 4096, c2, c3,
                        probeT, probeLive, ref best);
                    SearchAxis(k0.time, best.C0, best.C1, false, 4096, c2, c3,
                        probeT, probeLive, ref best);
                    SearchSquare(k0.time, best.C0, best.C1, 128, c2, c3,
                        probeT, probeLive, ref best);
                }

                bool exact = best.Matches == probeT.Length;
                segmentsExact &= exact;
                int o = segment * 4;
                coeff[o] = best.C0;
                coeff[o + 1] = best.C1;
                coeff[o + 2] = c2;
                coeff[o + 3] = c3;

                AERISLogger.Info(
                    Prefix + "_SEGMENT]" +
                    "; segment=" + segment +
                    "; t0_bits=" + Hex(FloatBits(k0.time)) +
                    "; t1_bits=" + Hex(FloatBits(k1.time)) +
                    "; probes=" + probeT.Length +
                    "; matches=" + best.Matches +
                    "; exact=" + Bool(exact) +
                    "; max_abs_error=" + R(best.MaxError) +
                    "; theory_c0_bits=" + Hex(FloatBits(theoryC0)) +
                    "; estimate_c0_bits=" + Hex(FloatBits(estimateC0)) +
                    "; recovered_c0_bits=" + Hex(FloatBits(best.C0)) +
                    "; recovered_c0_ulp_from_theory=" + UlpDelta(best.C0, theoryC0) +
                    "; theory_c1_bits=" + Hex(FloatBits(theoryC1)) +
                    "; estimate_c1_bits=" + Hex(FloatBits(estimateC1)) +
                    "; recovered_c1_bits=" + Hex(FloatBits(best.C1)) +
                    "; recovered_c1_ulp_from_theory=" + UlpDelta(best.C1, theoryC1) +
                    Invariants());
            }

            int uniformMatches = 0;
            double uniformMaxError = 0.0;
            const int uniformTests = 129;
            for (int i = 0; i < uniformTests; i++)
            {
                float t = i / (float)(uniformTests - 1);
                Compare(curve, keys, coeff, t, ref uniformMatches, ref uniformMaxError);
            }

            int boundaryMatches = 0;
            int boundaryTests = 0;
            double boundaryMaxError = 0.0;
            for (int i = 1; i < keys.Length - 1; i++)
            {
                float center = keys[i].time;
                Compare(curve, keys, coeff, OffsetUlps(center, -1),
                    ref boundaryMatches, ref boundaryMaxError); boundaryTests++;
                Compare(curve, keys, coeff, center,
                    ref boundaryMatches, ref boundaryMaxError); boundaryTests++;
                Compare(curve, keys, coeff, OffsetUlps(center, 1),
                    ref boundaryMatches, ref boundaryMaxError); boundaryTests++;
            }

            int randomMatches = 0;
            int randomTests = 128;
            double randomMaxError = 0.0;
            uint state = 0xA39C041Du;
            for (int i = 0; i < randomTests; i++)
            {
                state = state * 1664525u + 1013904223u;
                float t = (state & 0x00FFFFFFu) / 16777216f;
                Compare(curve, keys, coeff, t, ref randomMatches, ref randomMaxError);
            }

            bool exactAll = segmentsExact &&
                uniformMatches == uniformTests &&
                boundaryMatches == boundaryTests &&
                randomMatches == randomTests;

            AERISLogger.Info(
                Prefix + "_COMPLETE]" +
                "; body=Kerbin" +
                "; segment_probe_exact=" + Bool(segmentsExact) +
                "; uniform_matches=" + uniformMatches +
                "; uniform_tests=" + uniformTests +
                "; uniform_max_abs_error=" + R(uniformMaxError) +
                "; boundary_matches=" + boundaryMatches +
                "; boundary_tests=" + boundaryTests +
                "; boundary_max_abs_error=" + R(boundaryMaxError) +
                "; random_matches=" + randomMatches +
                "; random_tests=" + randomTests +
                "; random_max_abs_error=" + R(randomMaxError) +
                "; bit_exact=" + Bool(exactAll) +
                "; recovered_payload=PRIMITIVE_FLOAT_COEFFICIENTS_ONLY" +
                Invariants());
        }

        static void BuildSegmentProbes(AnimationCurve curve, float t0, float t1,
            out float[] times, out float[] live)
        {
            const int count = 15;
            times = new float[count];
            live = new float[count];
            float dt = t1 - t0;
            for (int i = 0; i < count; i++)
            {
                float fraction = (i + 1) / 16f;
                float t = t0 + dt * fraction;
                if (t <= t0) t = OffsetUlps(t0, 1);
                if (t >= t1) t = OffsetUlps(t1, -1);
                times[i] = t;
                live[i] = curve.Evaluate(t);
            }
        }

        static void EstimateCoefficients(float t0, float dt, float c2, float c3,
            float[] times, float[] live, float fallbackC0, float fallbackC1,
            out float c0, out float c1)
        {
            double aa = 0.0, ab = 0.0, bb = 0.0, ar = 0.0, br = 0.0;
            double dtd = dt;
            if (dtd == 0.0)
            {
                c0 = fallbackC0;
                c1 = fallbackC1;
                return;
            }

            for (int i = 0; i < times.Length; i++)
            {
                double x = (double)(times[i] - t0);
                double q = x / dtd;
                double a = q * q * q;
                double b = q * q;
                double residual = (double)live[i] -
                    ((double)c2 * x + (double)c3);
                aa += a * a;
                ab += a * b;
                bb += b * b;
                ar += a * residual;
                br += b * residual;
            }

            double determinant = aa * bb - ab * ab;
            if (Math.Abs(determinant) < 1E-30)
            {
                c0 = fallbackC0;
                c1 = fallbackC1;
                return;
            }

            double normalizedCubic = (ar * bb - br * ab) / determinant;
            double normalizedQuadratic = (br * aa - ar * ab) / determinant;
            double dt2 = dtd * dtd;
            double dt3 = dt2 * dtd;
            c0 = (float)(normalizedCubic / dt3);
            c1 = (float)(normalizedQuadratic / dt2);
            if (float.IsNaN(c0) || float.IsInfinity(c0)) c0 = fallbackC0;
            if (float.IsNaN(c1) || float.IsInfinity(c1)) c1 = fallbackC1;
        }

        static Candidate Score(float t0, float c0, float c1, float c2, float c3,
            float[] times, float[] live)
        {
            var candidate = new Candidate
            {
                C0 = c0,
                C1 = c1,
                Matches = 0,
                MaxError = 0.0
            };
            for (int i = 0; i < times.Length; i++)
            {
                float pure = EvaluateSegment(t0, c0, c1, c2, c3, times[i]);
                if (FloatBits(pure) == FloatBits(live[i])) candidate.Matches++;
                candidate.MaxError = Math.Max(candidate.MaxError,
                    Math.Abs((double)pure - (double)live[i]));
            }
            return candidate;
        }

        static void SearchSquare(float t0, float center0, float center1, int radius,
            float c2, float c3, float[] times, float[] live, ref Candidate best)
        {
            for (int d0 = -radius; d0 <= radius; d0++)
            {
                float c0 = OffsetUlps(center0, d0);
                for (int d1 = -radius; d1 <= radius; d1++)
                {
                    Candidate candidate = Score(t0, c0, OffsetUlps(center1, d1),
                        c2, c3, times, live);
                    Accept(candidate, ref best);
                    if (best.Matches == times.Length && best.MaxError == 0.0) return;
                }
            }
        }

        static void SearchAxis(float t0, float center0, float center1, bool c0Axis,
            int radius, float c2, float c3, float[] times, float[] live,
            ref Candidate best)
        {
            for (int d = -radius; d <= radius; d++)
            {
                float c0 = c0Axis ? OffsetUlps(center0, d) : center0;
                float c1 = c0Axis ? center1 : OffsetUlps(center1, d);
                Candidate candidate = Score(t0, c0, c1, c2, c3, times, live);
                Accept(candidate, ref best);
                if (best.Matches == times.Length && best.MaxError == 0.0) return;
            }
        }

        static void Accept(Candidate candidate, ref Candidate best)
        {
            if (candidate.Matches > best.Matches ||
                (candidate.Matches == best.Matches && candidate.MaxError < best.MaxError))
                best = candidate;
        }

        static float EvaluateSegment(float t0, float c0, float c1, float c2,
            float c3, float t)
        {
            float local = t - t0;
            return (local * (local * (local * c0 + c1) + c2)) + c3;
        }

        static float EvaluateRecovered(Keyframe[] keys, float[] coeff, float t)
        {
            if (t < keys[0].time) return keys[0].value;
            if (t >= keys[keys.Length - 1].time) return keys[keys.Length - 1].value;

            int right = 1;
            while (right < keys.Length - 1 && t >= keys[right].time) right++;
            int segment = right - 1;
            int o = segment * 4;
            return EvaluateSegment(keys[segment].time,
                coeff[o], coeff[o + 1], coeff[o + 2], coeff[o + 3], t);
        }

        static void Compare(AnimationCurve curve, Keyframe[] keys, float[] coeff,
            float t, ref int matches, ref double maxError)
        {
            float live = curve.Evaluate(t);
            float pure = EvaluateRecovered(keys, coeff, t);
            if (FloatBits(live) == FloatBits(pure)) matches++;
            maxError = Math.Max(maxError, Math.Abs((double)live - (double)pure));
        }

        static IList GetModifierList(object pqs)
        {
            if (pqs == null) return null;
            Type type = pqs.GetType();
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            string[] names = { "mods", "modifiers", "pqsMods" };
            for (int i = 0; i < names.Length; i++)
            {
                FieldInfo field = type.GetField(names[i], flags);
                if (field != null)
                {
                    try
                    {
                        IList list = field.GetValue(pqs) as IList;
                        if (list != null) return list;
                    }
                    catch { }
                }
                PropertyInfo property = type.GetProperty(names[i], flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                {
                    try
                    {
                        IList list = property.GetValue(pqs, null) as IList;
                        if (list != null) return list;
                    }
                    catch { }
                }
            }

            FieldInfo[] fields = type.GetFields(flags);
            for (int i = 0; i < fields.Length; i++)
            {
                if (!typeof(IList).IsAssignableFrom(fields[i].FieldType)) continue;
                try
                {
                    IList list = fields[i].GetValue(pqs) as IList;
                    if (list == null) continue;
                    for (int j = 0; j < Math.Min(list.Count, 8); j++)
                    {
                        object item = list[j];
                        if (item != null && item.GetType().Name.IndexOf("PQSMod",
                            StringComparison.OrdinalIgnoreCase) >= 0) return list;
                    }
                }
                catch { }
            }
            return null;
        }

        static bool TryReadMember(object target, string name, out object value)
        {
            value = null;
            if (target == null) return false;
            Type type = target.GetType();
            const BindingFlags flags = BindingFlags.Instance |
                BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo field = type.GetField(name, flags);
            if (field != null)
            {
                try { value = field.GetValue(target); return true; }
                catch { }
            }
            PropertyInfo property = type.GetProperty(name, flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                try { value = property.GetValue(target, null); return true; }
                catch { }
            }
            return false;
        }

        static unsafe int FloatBits(float value)
        {
            return *(int*)&value;
        }

        static unsafe float FloatFromBits(int bits)
        {
            return *(float*)&bits;
        }

        static int ToOrdered(float value)
        {
            int bits = FloatBits(value);
            return bits < 0 ? int.MinValue - bits : bits;
        }

        static float FromOrdered(int ordered)
        {
            int bits = ordered < 0 ? int.MinValue - ordered : ordered;
            return FloatFromBits(bits);
        }

        static float OffsetUlps(float value, int offset)
        {
            long ordered = ToOrdered(value);
            long target = ordered + offset;
            if (target < int.MinValue) target = int.MinValue;
            if (target > int.MaxValue) target = int.MaxValue;
            return FromOrdered((int)target);
        }

        static long UlpDelta(float value, float baseline)
        {
            return (long)ToOrdered(value) - (long)ToOrdered(baseline);
        }

        static string Hex(int bits)
        {
            return "0x" + unchecked((uint)bits).ToString("X8",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        static string R(double value)
        {
            return value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value)) return "-";
            return value.Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
        }

        static string Invariants()
        {
            return "; production_authority=PQS" +
                "; db_authority=PQS" +
                "; producer_switch=false" +
                "; db_write=false" +
                "; preload_mutation=false" +
                "; production_worker_runtime_object_access=false";
        }
    }
}
