using System;
using System.Runtime.CompilerServices;

namespace AERISFlightControl.Terrain
{
    // AERIS39 MAPSO-3 pure CLR MapSO sampling primitive.
    //
    // This class is intentionally free of Unity/KSP/runtime-object types.
    // It reproduces the KSP 1.12.5 MapSO.GetPixelFloat(double,double)
    // dependency closure certified by MAPSO-1 / MAPSO-2A / MAPSO-2B.
    //
    // MAPSO-3 Fix2: preserve the observable runtime evaluation boundaries of
    // stock MapSO as closely as possible. Stock GreyFloat,
    // GetPixelFloat(int,int), ConstructBilinearCoords(double,double), and
    // GetPixelFloat(double,double) are virtual/callvirt boundaries. The pure
    // implementation therefore prevents the worker JIT from collapsing those
    // boundaries. Lerp and the bilinear call order mirror the captured stock
    // IL instead of introducing algebraically equivalent temporary locals.
    //
    // MAPSO-3G: the real KSP runtime witness isolated the remaining mismatch to
    // ConstructBilinearCoords. The captured stock double overload materializes
    // centerXD/centerYD, min/max, and mid values through instance fields. A
    // local-only reconstruction is mathematically equivalent but can expose a
    // different Mono JIT intermediate-precision boundary. Preserve the stock
    // field store/reload shape with one CLR-only scratch object per worker
    // thread. No Unity/KSP/runtime object is stored here and no per-sample
    // allocation occurs after the thread's first sample.
    //
    // HARD RULE: do not algebraically simplify, reorder, or localize the
    // bilinear scratch field accesses without a new exact-bit witness.
    internal static class AERIS39MapSoPureCpuExact
    {
        const int Byte2FloatBits = unchecked((int)0x3B808081);
        static readonly float Byte2Float = FloatFromBits(Byte2FloatBits);

        [ThreadStatic]
        static BilinearScratch threadBilinearScratch;

        internal sealed class MapSnapshot
        {
            internal readonly byte[] Data;
            internal readonly int Width;
            internal readonly int Height;
            internal readonly int BytesPerPixel;
            internal readonly int RowWidth;

            internal MapSnapshot(
                byte[] data,
                int width,
                int height,
                int bytesPerPixel,
                int rowWidth)
            {
                if (data == null) throw new ArgumentNullException("data");
                if (width <= 0) throw new ArgumentOutOfRangeException("width");
                if (height <= 0) throw new ArgumentOutOfRangeException("height");
                if (bytesPerPixel <= 0) throw new ArgumentOutOfRangeException("bytesPerPixel");
                if (rowWidth <= 0) throw new ArgumentOutOfRangeException("rowWidth");
                if (rowWidth < width * bytesPerPixel)
                    throw new ArgumentException("rowWidth is smaller than packed row", "rowWidth");
                if (data.Length < rowWidth * height)
                    throw new ArgumentException("map data is incomplete", "data");

                Data = (byte[])data.Clone();
                Width = width;
                Height = height;
                BytesPerPixel = bytesPerPixel;
                RowWidth = rowWidth;
            }
        }

        sealed class BilinearScratch
        {
            internal int Width;
            internal int Height;
            internal double CenterXD;
            internal double CenterYD;
            internal int MinX;
            internal int MaxX;
            internal int MinY;
            internal int MaxY;
            internal float MidX;
            internal float MidY;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float GetPixelFloat(
            MapSnapshot map,
            double x,
            double y)
        {
            if (map == null) throw new ArgumentNullException("map");

            BilinearScratch c = AcquireBilinearScratch(map.Width, map.Height);
            ConstructBilinearCoords(c, x, y);

            if (map.BytesPerPixel == 1)
            {
                float low = Lerp(
                    GreyFloat(map, c.MinX, c.MinY),
                    GreyFloat(map, c.MaxX, c.MinY),
                    c.MidX);
                float high = Lerp(
                    GreyFloat(map, c.MinX, c.MaxY),
                    GreyFloat(map, c.MaxX, c.MaxY),
                    c.MidX);
                return Lerp(low, high, c.MidY);
            }

            float lowMulti = Lerp(
                GetPixelFloat(map, c.MinX, c.MinY),
                GetPixelFloat(map, c.MaxX, c.MinY),
                c.MidX);
            float highMulti = Lerp(
                GetPixelFloat(map, c.MinX, c.MaxY),
                GetPixelFloat(map, c.MaxX, c.MaxY),
                c.MidX);
            return Lerp(lowMulti, highMulti, c.MidY);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static float GetPixelFloat(
            MapSnapshot map,
            int x,
            int y)
        {
            if (map == null) throw new ArgumentNullException("map");

            int index = PixelIndex(map, x, y);
            float retVal = 0f;
            int itr = 0;

            while (itr < map.BytesPerPixel)
            {
                retVal = retVal + (float)map.Data[index + itr];
                itr = itr + 1;
            }

            retVal = retVal / (float)map.BytesPerPixel;
            retVal = retVal * Byte2Float;
            return retVal;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static BilinearScratch AcquireBilinearScratch(int width, int height)
        {
            BilinearScratch c = threadBilinearScratch;
            if (c == null)
            {
                c = new BilinearScratch();
                threadBilinearScratch = c;
            }

            c.Width = width;
            c.Height = height;
            return c;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ConstructBilinearCoords(
            BilinearScratch c,
            double x,
            double y)
        {
            // Stock MapSO.ConstructBilinearCoords(double,double), captured IL:
            // - normalize by writing x/y back to their arguments;
            // - materialize centerXD/centerYD through fields;
            // - reload those fields for Floor/Ceiling and mid subtraction;
            // - convert only the final mid result to float;
            // - wrap max index only after mid is materialized.
            x = Math.Abs(x - Math.Floor(x));
            y = Math.Abs(y - Math.Floor(y));

            c.CenterXD = x * (double)c.Width;
            c.MinX = (int)Math.Floor(c.CenterXD);
            c.MaxX = (int)Math.Ceiling(c.CenterXD);
            c.MidX = (float)(c.CenterXD - (double)c.MinX);
            if (c.MaxX == c.Width) c.MaxX = 0;

            c.CenterYD = y * (double)c.Height;
            c.MinY = (int)Math.Floor(c.CenterYD);
            c.MaxY = (int)Math.Ceiling(c.CenterYD);
            c.MidY = (float)(c.CenterYD - (double)c.MinY);
            if (c.MaxY == c.Height) c.MaxY = 0;
        }

        static int PixelIndex(MapSnapshot map, int x, int y)
        {
            return unchecked(x * map.BytesPerPixel + y * map.RowWidth);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static float GreyFloat(MapSnapshot map, int x, int y)
        {
            return Byte2Float * (float)map.Data[PixelIndex(map, x, y)];
        }

        static float Lerp(float a, float b, float t)
        {
            float result = a + (b - a) * Clamp01(t);
            return result;
        }

        static float Clamp01(float value)
        {
            float result;
            if (value < 0f)
                result = 0f;
            else if (value > 1f)
                result = 1f;
            else
                result = value;
            return result;
        }

        internal static int FloatBits(float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            return BitConverter.ToInt32(bytes, 0);
        }

        static float FloatFromBits(int bits)
        {
            byte[] bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }
    }
}
