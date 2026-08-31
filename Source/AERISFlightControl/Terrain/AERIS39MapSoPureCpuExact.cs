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
    // stock MapSO as closely as possible.  Stock GreyFloat,
    // GetPixelFloat(int,int), ConstructBilinearCoords(double,double), and
    // GetPixelFloat(double,double) are virtual/callvirt boundaries.  The pure
    // implementation therefore prevents the worker JIT from collapsing those
    // boundaries.  Lerp and the bilinear call order mirror the captured stock
    // IL instead of introducing algebraically equivalent temporary locals.
    //
    // HARD RULE: do not algebraically simplify or reorder this code.
    internal static class AERIS39MapSoPureCpuExact
    {
        const int Byte2FloatBits = unchecked((int)0x3B808081);
        static readonly float Byte2Float = FloatFromBits(Byte2FloatBits);

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

        struct Coords
        {
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

            Coords c = ConstructBilinearCoords(
                x, y, map.Width, map.Height);

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
        static Coords ConstructBilinearCoords(
            double x,
            double y,
            int width,
            int height)
        {
            x = Math.Abs(x - Math.Floor(x));
            y = Math.Abs(y - Math.Floor(y));

            double centerX = x * (double)width;
            int minX = (int)Math.Floor(centerX);
            int maxX = (int)Math.Ceiling(centerX);
            float midX = (float)(centerX - (double)minX);
            if (maxX == width) maxX = 0;

            double centerY = y * (double)height;
            int minY = (int)Math.Floor(centerY);
            int maxY = (int)Math.Ceiling(centerY);
            float midY = (float)(centerY - (double)minY);
            if (maxY == height) maxY = 0;

            Coords c = new Coords();
            c.MinX = minX;
            c.MaxX = maxX;
            c.MinY = minY;
            c.MaxY = maxY;
            c.MidX = midX;
            c.MidY = midY;
            return c;
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
