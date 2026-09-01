using System;
using System.Runtime.CompilerServices;

namespace AERISFlightControl.Terrain
{
    // AERIS39 MAPSO-3 pure CLR MapSO sampling primitive.
    //
    // This class is intentionally free of Unity/KSP/runtime-object types.
    // Runtime effective coordinate semantics are captured on the main thread
    // into the immutable snapshot as a primitive enum. Workers never inspect
    // Harmony/KSPCF/runtime objects.
    //
    // HARD RULE: do not algebraically simplify or reorder the certified paths.
    internal static class AERIS39MapSoPureCpuExact
    {
        const int Byte2FloatBits = unchecked((int)0x3B808081);
        static readonly float Byte2Float = FloatFromBits(Byte2FloatBits);

        [ThreadStatic]
        static BilinearScratch threadBilinearScratch;

        internal enum CoordinateSemantics
        {
            StockWrapXY = 0,
            KspCommunityFixesWrapXClampY = 1
        }

        internal sealed class MapSnapshot
        {
            internal readonly byte[] Data;
            internal readonly int Width;
            internal readonly int Height;
            internal readonly int BytesPerPixel;
            internal readonly int RowWidth;
            internal readonly CoordinateSemantics Semantics;

            internal MapSnapshot(
                byte[] data,
                int width,
                int height,
                int bytesPerPixel,
                int rowWidth)
                : this(data, width, height, bytesPerPixel, rowWidth, CoordinateSemantics.StockWrapXY)
            {
            }

            internal MapSnapshot(
                byte[] data,
                int width,
                int height,
                int bytesPerPixel,
                int rowWidth,
                CoordinateSemantics semantics)
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
                if (semantics != CoordinateSemantics.StockWrapXY &&
                    semantics != CoordinateSemantics.KspCommunityFixesWrapXClampY)
                    throw new ArgumentOutOfRangeException("semantics");

                Data = (byte[])data.Clone();
                Width = width;
                Height = height;
                BytesPerPixel = bytesPerPixel;
                RowWidth = rowWidth;
                Semantics = semantics;
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
            ConstructBilinearCoords(c, map.Semantics, x, y);

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
            CoordinateSemantics semantics,
            double x,
            double y)
        {
            if (semantics == CoordinateSemantics.KspCommunityFixesWrapXClampY)
            {
                // KSPCommunityFixes MapSOCorrectWrapping double-prefix exact source order.
                // X wraps as longitude.
                x = Math.Abs(x - Math.Floor(x));
                c.CenterXD = x * (double)c.Width;
                c.MinX = (int)Math.Floor(c.CenterXD);
                c.MaxX = (int)Math.Ceiling(c.CenterXD);
                c.MidX = (float)c.CenterXD - c.MinX;
                if (c.MaxX == c.Width) c.MaxX = 0;

                // Y clamps as latitude; poles do not wrap.
                y = Math.Min(Math.Max(y, 0.0), 0.99999);
                c.CenterYD = y * (double)c.Height;
                c.MinY = (int)Math.Floor(c.CenterYD);
                c.MaxY = (int)Math.Ceiling(c.CenterYD);
                c.MidY = (float)c.CenterYD - c.MinY;
                if (c.MaxY >= c.Height) c.MaxY = c.Height - 1;
                return;
            }

            // Stock KSP 1.12.5 MapSO.ConstructBilinearCoords(double,double).
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
