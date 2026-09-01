using System;

namespace AERISFlightControl.Terrain
{
    // AERIS38 R041D: pure CLR evaluator primitives for the first stock-body
    // expansion candidates (Moho + Dres).
    //
    // HARD RULE: this file must remain free of Unity/KSP/runtime-object types.
    // All state arrives as immutable copied scalars/arrays from the main thread.
    internal static class AERISR041MohoDresPureCpuExact
    {
        internal enum MapInterpolationMode
        {
            WidthMinusOneClampFloat = 0,
            WidthMinusOneClampDouble = 1,
            WidthWrapFloat = 2,
            WidthWrapDouble = 3,
            WidthHalfTexelWrapFloat = 4,
            WidthHalfTexelWrapDouble = 5,
            WidthMinusOneWrapFloat = 6,
            WidthMinusOneWrapDouble = 7,
            WidthClampFloat = 8,
            WidthClampDouble = 9,
            WidthHalfTexelClampFloat = 10,
            WidthHalfTexelClampDouble = 11
        }

        internal enum CurveEvaluationMode
        {
            HermiteBasisFloat = 0,
            PolynomialFloat = 1,
            HermiteBasisDouble = 2,
            PolynomialDouble = 3
        }

        internal enum CoordMode
        {
            EastNorth = 0,
            EastSouth = 1,
            WestNorth = 2,
            WestSouth = 3
        }

        internal sealed class MapSnapshot
        {
            internal readonly byte[] Data;
            internal readonly int Width;
            internal readonly int Height;
            internal readonly int BytesPerPixel;
            internal readonly int RowWidth;
            internal readonly int Channel;
            internal readonly MapInterpolationMode Mode;

            internal MapSnapshot(
                byte[] data,
                int width,
                int height,
                int bytesPerPixel,
                int rowWidth,
                int channel,
                MapInterpolationMode mode)
            {
                if (data == null) throw new ArgumentNullException("data");
                if (width <= 0 || height <= 0)
                    throw new ArgumentOutOfRangeException("map dimensions");
                if (bytesPerPixel <= 0)
                    throw new ArgumentOutOfRangeException("bytesPerPixel");
                if (rowWidth < width * bytesPerPixel)
                    throw new ArgumentOutOfRangeException("rowWidth");
                if (channel < 0 || channel >= bytesPerPixel)
                    throw new ArgumentOutOfRangeException("channel");
                if (data.Length < rowWidth * height)
                    throw new ArgumentException("map data is incomplete", "data");

                Data = (byte[])data.Clone();
                Width = width;
                Height = height;
                BytesPerPixel = bytesPerPixel;
                RowWidth = rowWidth;
                Channel = channel;
                Mode = mode;
            }
        }

        internal sealed class CurveKeySnapshot
        {
            internal readonly float Time;
            internal readonly float Value;
            internal readonly float InTangent;
            internal readonly float OutTangent;
            internal readonly int WeightedMode;

            internal CurveKeySnapshot(
                float time,
                float value,
                float inTangent,
                float outTangent,
                int weightedMode)
            {
                Time = time;
                Value = value;
                InTangent = inTangent;
                OutTangent = outTangent;
                WeightedMode = weightedMode;
            }
        }

        internal sealed class CurveSnapshot
        {
            internal readonly CurveKeySnapshot[] Keys;
            internal readonly CurveEvaluationMode Mode;
            internal readonly int PreWrapMode;
            internal readonly int PostWrapMode;
            internal readonly bool HasWeightedKeys;

            internal CurveSnapshot(
                CurveKeySnapshot[] keys,
                CurveEvaluationMode mode,
                int preWrapMode,
                int postWrapMode)
            {
                if (keys == null || keys.Length == 0)
                    throw new ArgumentException("curve requires keys", "keys");

                Keys = new CurveKeySnapshot[keys.Length];
                bool weighted = false;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (keys[i] == null)
                        throw new ArgumentException("curve key is null", "keys");
                    Keys[i] = new CurveKeySnapshot(
                        keys[i].Time,
                        keys[i].Value,
                        keys[i].InTangent,
                        keys[i].OutTangent,
                        keys[i].WeightedMode);
                    weighted |= keys[i].WeightedMode != 0;
                }

                Mode = mode;
                PreWrapMode = preWrapMode;
                PostWrapMode = postWrapMode;
                HasWeightedKeys = weighted;
            }
        }

        internal abstract class HeightOpSnapshot
        {
            internal abstract double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height);
        }

        internal sealed class HeightMapOpSnapshot : HeightOpSnapshot
        {
            internal readonly double Offset;
            internal readonly double Deformity;
            internal readonly MapSnapshot Map;

            internal HeightMapOpSnapshot(
                double offset,
                double deformity,
                MapSnapshot map)
            {
                Offset = offset;
                Deformity = deformity;
                Map = map ?? throw new ArgumentNullException("map");
            }

            internal override double Evaluate(
                double x, double y, double z, double u, double v, double height)
            {
                float pixel = EvaluateMap(Map, u, v);
                double product = Deformity * (double)pixel;
                double value = height + Offset;
                value = value + product;
                return value;
            }
        }

        internal sealed class SimplexHeightOpSnapshot : HeightOpSnapshot
        {
            internal readonly double Deformity;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot Simplex;

            internal SimplexHeightOpSnapshot(
                double deformity,
                AERISR039MinmusPureCpuExact.SimplexSnapshot simplex)
            {
                Deformity = deformity;
                Simplex = simplex ?? throw new ArgumentNullException("simplex");
            }

            internal override double Evaluate(
                double x, double y, double z, double u, double v, double height)
            {
                double noise = AERISR039MinmusPureCpuExact.SimplexNoise(
                    Simplex, x, y, z, Simplex.Persistence);
                double delta = noise * Deformity;
                return height + delta;
            }
        }

        internal sealed class SimplexAbsoluteOpSnapshot : HeightOpSnapshot
        {
            internal readonly double Deformity;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot Simplex;

            internal SimplexAbsoluteOpSnapshot(
                double deformity,
                AERISR039MinmusPureCpuExact.SimplexSnapshot simplex)
            {
                Deformity = deformity;
                Simplex = simplex ?? throw new ArgumentNullException("simplex");
            }

            internal override double Evaluate(
                double x, double y, double z, double u, double v, double height)
            {
                double noise = AERISR039MinmusPureCpuExact.SimplexNoise(
                    Simplex, x, y, z, Simplex.Persistence);
                noise = noise + 1.0;
                noise = noise * 0.5;
                noise = noise * Deformity;
                return height + noise;
            }
        }

        internal sealed class FlattenOceanOpSnapshot : HeightOpSnapshot
        {
            internal readonly double OceanRad;

            internal FlattenOceanOpSnapshot(double oceanRad)
            {
                OceanRad = oceanRad;
            }

            internal override double Evaluate(
                double x, double y, double z, double u, double v, double height)
            {
                if (height < OceanRad)
                    return OceanRad;
                return height;
            }
        }

        internal sealed class HeightNoiseRidgedOpSnapshot : HeightOpSnapshot
        {
            internal readonly float Deformity;
            internal readonly AERISR039MinmusPureCpuExact.RidgedSnapshot Noise;

            internal HeightNoiseRidgedOpSnapshot(
                float deformity,
                AERISR039MinmusPureCpuExact.RidgedSnapshot noise)
            {
                Deformity = deformity;
                Noise = noise ?? throw new ArgumentNullException("noise");
            }

            internal override double Evaluate(
                double x, double y, double z, double u, double v, double height)
            {
                double noise = AERISR039MinmusPureCpuExact.RidgedGetValue(
                    Noise, x, y, z);
                double delta = noise * (double)Deformity;
                return height + delta;
            }
        }

        internal sealed class Curve2OpSnapshot : HeightOpSnapshot
        {
            internal readonly float Deformity;
            internal readonly double RadiusMin;
            internal readonly double SimplexHeightStart;
            internal readonly double SimplexHeightEnd;
            internal readonly double HDeltaR;
            internal readonly AERISR039MinmusPureCpuExact.SimplexSnapshot Simplex;
            internal readonly AERISR039MinmusPureCpuExact.RidgedSnapshot RidgedAdd;
            internal readonly AERISR039MinmusPureCpuExact.RidgedSnapshot RidgedSub;
            internal readonly CurveSnapshot Curve;

            internal Curve2OpSnapshot(
                float deformity,
                double radiusMin,
                double simplexHeightStart,
                double simplexHeightEnd,
                double hDeltaR,
                AERISR039MinmusPureCpuExact.SimplexSnapshot simplex,
                AERISR039MinmusPureCpuExact.RidgedSnapshot ridgedAdd,
                AERISR039MinmusPureCpuExact.RidgedSnapshot ridgedSub,
                CurveSnapshot curve)
            {
                Deformity = deformity;
                RadiusMin = radiusMin;
                SimplexHeightStart = simplexHeightStart;
                SimplexHeightEnd = simplexHeightEnd;
                HDeltaR = hDeltaR;
                Simplex = simplex ?? throw new ArgumentNullException("simplex");
                RidgedAdd = ridgedAdd ?? throw new ArgumentNullException("ridgedAdd");
                RidgedSub = ridgedSub ?? throw new ArgumentNullException("ridgedSub");
                Curve = curve ?? throw new ArgumentNullException("curve");
            }

            internal override double Evaluate(
                double x, double y, double z, double u, double v, double height)
            {
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
                    double scaled = (h - SimplexHeightStart) * HDeltaR;
                    t = (float)scaled;
                }

                double s = AERISR039MinmusPureCpuExact.SimplexNoiseNormalized(
                    Simplex, x, y, z, Simplex.Persistence);
                float curve = EvaluateCurve(Curve, t);
                s = s * (double)curve;
                if (s == 0.0)
                    return height;

                double r = AERISR039MinmusPureCpuExact.RidgedGetValue(
                    RidgedAdd, x, y, z);
                double sub = AERISR039MinmusPureCpuExact.RidgedGetValue(
                    RidgedSub, x, y, z);
                r = r - sub;

                if (r < -1.0) r = -1.0;
                if (r > 1.0) r = 1.0;

                double delta = r + 1.0;
                delta = delta * 0.5;
                delta = delta * (double)Deformity;
                delta = delta * s;
                return height + delta;
            }
        }

        internal sealed class BodySnapshot
        {
            internal readonly string Name;
            internal readonly double Radius;
            internal readonly HeightOpSnapshot[] Ops;

            internal BodySnapshot(
                string name,
                double radius,
                HeightOpSnapshot[] ops)
            {
                Name = name ?? string.Empty;
                Radius = radius;
                if (ops == null) throw new ArgumentNullException("ops");
                Ops = (HeightOpSnapshot[])ops.Clone();
            }
        }

        internal static double EvaluateBody(
            BodySnapshot body,
            CoordMode coordMode,
            double latitudeDeg,
            double longitudeDeg,
            double x,
            double y,
            double z,
            bool absoluteInitialHeight)
        {
            if (body == null) throw new ArgumentNullException("body");

            double u;
            double v;
            Coordinates(coordMode, latitudeDeg, longitudeDeg, out u, out v);

            double height = absoluteInitialHeight ? body.Radius : 0.0;
            for (int i = 0; i < body.Ops.Length; i++)
            {
                HeightOpSnapshot op = body.Ops[i];
                if (op == null) continue;
                height = op.Evaluate(x, y, z, u, v, height);
            }

            return absoluteInitialHeight ? height - body.Radius : height;
        }

        internal static void Coordinates(
            CoordMode mode,
            double latitudeDeg,
            double longitudeDeg,
            out double u,
            out double v)
        {
            bool west = mode == CoordMode.WestNorth || mode == CoordMode.WestSouth;
            bool south = mode == CoordMode.EastSouth || mode == CoordMode.WestSouth;

            double lon = west ? -longitudeDeg : longitudeDeg;
            u = lon / 360.0 + 0.5;
            v = south ? (0.5 - latitudeDeg / 180.0) :
                (latitudeDeg / 180.0 + 0.5);
        }

        internal static float EvaluateMap(
            MapSnapshot map,
            double u,
            double v)
        {
            if (map == null) throw new ArgumentNullException("map");

            int mode = (int)map.Mode;
            bool useDouble = (mode & 1) != 0;
            int family = mode >> 1;

            double fx;
            double fy;
            bool wrapX;

            switch (family)
            {
                case 0: // width-1, clamp
                    fx = u * (double)Math.Max(0, map.Width - 1);
                    fy = v * (double)Math.Max(0, map.Height - 1);
                    wrapX = false;
                    break;
                case 1: // width, repeat X
                    fx = u * (double)map.Width;
                    fy = v * (double)map.Height;
                    wrapX = true;
                    break;
                case 2: // width with half-texel shift, repeat X
                    fx = u * (double)map.Width - 0.5;
                    fy = v * (double)map.Height - 0.5;
                    wrapX = true;
                    break;
                case 3: // width-1, repeat X
                    fx = u * (double)Math.Max(0, map.Width - 1);
                    fy = v * (double)Math.Max(0, map.Height - 1);
                    wrapX = true;
                    break;
                case 4: // width, clamp
                    fx = u * (double)map.Width;
                    fy = v * (double)map.Height;
                    wrapX = false;
                    break;
                case 5: // width half-texel, clamp
                    fx = u * (double)map.Width - 0.5;
                    fy = v * (double)map.Height - 0.5;
                    wrapX = false;
                    break;
                default:
                    throw new ArgumentOutOfRangeException("map.Mode");
            }

            int x0 = FloorToInt(fx);
            int y0 = FloorToInt(fy);
            double txd = fx - (double)x0;
            double tyd = fy - (double)y0;

            int x1 = x0 + 1;
            int y1 = y0 + 1;

            if (wrapX)
            {
                x0 = Wrap(x0, map.Width);
                x1 = Wrap(x1, map.Width);
            }
            else
            {
                x0 = ClampIndex(x0, map.Width);
                x1 = ClampIndex(x1, map.Width);
            }

            y0 = ClampIndex(y0, map.Height);
            y1 = ClampIndex(y1, map.Height);

            float p00 = PixelFloat(map, x0, y0);
            float p10 = PixelFloat(map, x1, y0);
            float p01 = PixelFloat(map, x0, y1);
            float p11 = PixelFloat(map, x1, y1);

            if (useDouble)
            {
                double a = (double)p00 + ((double)p10 - (double)p00) * txd;
                double b = (double)p01 + ((double)p11 - (double)p01) * txd;
                double value = a + (b - a) * tyd;
                return (float)value;
            }

            float tx = (float)txd;
            float ty = (float)tyd;
            float af = p00 + (p10 - p00) * tx;
            float bf = p01 + (p11 - p01) * tx;
            return af + (bf - af) * ty;
        }

        internal static float PixelFloat(MapSnapshot map, int x, int y)
        {
            x = ClampIndex(x, map.Width);
            y = ClampIndex(y, map.Height);
            int index = checked(y * map.RowWidth + x * map.BytesPerPixel + map.Channel);
            byte value = map.Data[index];
            return value * (1f / 255f);
        }

        internal static float EvaluateCurve(CurveSnapshot curve, float t)
        {
            if (curve == null) throw new ArgumentNullException("curve");
            CurveKeySnapshot[] keys = curve.Keys;
            if (keys.Length == 1) return keys[0].Value;
            if (t < keys[0].Time) return keys[0].Value;
            if (t >= keys[keys.Length - 1].Time)
                return keys[keys.Length - 1].Value;

            // Unity FindIndexForSampling uses upper-bound semantics: an exact internal
            // key time belongs to the following segment.
            int right = 1;
            while (right < keys.Length - 1 && t >= keys[right].Time)
                right++;

            CurveKeySnapshot k0 = keys[right - 1];
            CurveKeySnapshot k1 = keys[right];
            float dt = k1.Time - k0.Time;
            if (dt == 0f) return k1.Value;

            if (float.IsInfinity(k0.OutTangent) ||
                float.IsInfinity(k1.InTangent))
                return k0.Value;

            if (curve.Mode == CurveEvaluationMode.PolynomialFloat)
                return EvaluateUnityNativeCacheStrict(k0, k1, t);

            if (curve.Mode == CurveEvaluationMode.HermiteBasisDouble ||
                curve.Mode == CurveEvaluationMode.PolynomialDouble)
            {
                double u = ((double)t - (double)k0.Time) / (double)dt;
                double m0 = (double)k0.OutTangent * (double)dt;
                double m1 = (double)k1.InTangent * (double)dt;

                if (curve.Mode == CurveEvaluationMode.PolynomialDouble)
                {
                    double a = 2.0 * (double)k0.Value;
                    a = a - 2.0 * (double)k1.Value;
                    a = a + m0;
                    a = a + m1;
                    double b = -3.0 * (double)k0.Value;
                    b = b + 3.0 * (double)k1.Value;
                    b = b - 2.0 * m0;
                    b = b - m1;
                    double result = a * u + b;
                    result = result * u + m0;
                    result = result * u + (double)k0.Value;
                    return (float)result;
                }

                double u2 = u * u;
                double u3 = u2 * u;
                double h00 = 2.0 * u3 - 3.0 * u2 + 1.0;
                double h10 = u3 - 2.0 * u2 + u;
                double h01 = -2.0 * u3 + 3.0 * u2;
                double h11 = u3 - u2;
                double resultH = (double)k0.Value * h00;
                resultH = resultH + m0 * h10;
                resultH = resultH + (double)k1.Value * h01;
                resultH = resultH + m1 * h11;
                return (float)resultH;
            }

            float uf = (t - k0.Time) / dt;
            float fm0 = k0.OutTangent * dt;
            float fm1 = k1.InTangent * dt;
            float u2f = uf * uf;
            float u3f = u2f * uf;
            float h00f = 2f * u3f - 3f * u2f + 1f;
            float h10f = u3f - 2f * u2f + uf;
            float h01f = -2f * u3f + 3f * u2f;
            float h11f = u3f - u2f;
            float resultF = k0.Value * h00f;
            resultF = resultF + fm0 * h10f;
            resultF = resultF + k1.Value * h01f;
            resultF = resultF + fm1 * h11f;
            return resultF;
        }

        static float EvaluateUnityNativeCacheStrict(
            CurveKeySnapshot k0,
            CurveKeySnapshot k1,
            float t)
        {
            float dx = B32Sub(k1.Time, k0.Time);
            if (dx < 0.0001f) dx = 0.0001f;
            float dy = B32Sub(k1.Value, k0.Value);
            float length = B32Div(1.0f, B32Mul(dx, dx));
            float d1 = B32Mul(k0.OutTangent, dx);
            float d2 = B32Mul(k1.InTangent, dx);

            float n0 = B32Add(d1, d2);
            n0 = B32Sub(n0, dy);
            n0 = B32Sub(n0, dy);
            float c0 = B32Div(B32Mul(n0, length), dx);

            float n1 = B32Add(dy, dy);
            n1 = B32Add(n1, dy);
            n1 = B32Sub(n1, d1);
            n1 = B32Sub(n1, d1);
            n1 = B32Sub(n1, d2);
            float c1 = B32Mul(n1, length);
            float c2 = k0.OutTangent;
            float c3 = k0.Value;
            float local = B32Sub(t, k0.Time);

            float s1 = B32Add(B32Mul(local, c0), c1);
            float s2 = B32Add(B32Mul(local, s1), c2);
            return B32Add(B32Mul(local, s2), c3);
        }

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

        static int FloorToInt(double value)
        {
            int i = (int)value;
            if (value < 0.0 && value != (double)i) i--;
            return i;
        }

        static int ClampIndex(int value, int count)
        {
            if (count <= 1) return 0;
            if (value < 0) return 0;
            if (value >= count) return count - 1;
            return value;
        }

        static int Wrap(int value, int count)
        {
            if (count <= 1) return 0;
            int r = value % count;
            if (r < 0) r += count;
            return r;
        }
    }
}
