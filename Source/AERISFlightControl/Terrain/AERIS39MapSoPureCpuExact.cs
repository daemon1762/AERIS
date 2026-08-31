using System;

namespace AERISFlightControl.Terrain
{
    // AERIS39 MAPSO-3 pure CLR MapSO sampling primitive.
    //
    // This class is intentionally free of Unity/KSP/runtime-object types.
    // It reproduces the KSP 1.12.5 MapSO.GetPixelFloat(double,double)
    // dependency closure certified by MAPSO-1 / MAPSO-2A / MAPSO-2B.
    // Do not algebraically simplify the operation order below.
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

        internal static float GetPixelFloat(
            MapSnapshot map,
            double x,
            double y)
        {
            if (map == null) throw new ArgumentNullException("map");

            Coords c = ConstructBilinearCoords(
                x, y, map.Width, map.Height);

            float a;
            float b;
            float d;
            float e;

            if (map.BytesPerPixel == 1)
            {
                a = GreyFloat(map, c.MinX, c.MinY);
                b = GreyFloat(map, c.MaxX, c.MinY);
                d = GreyFloat(map, c.MinX, c.MaxY);
                e = GreyFloat(map, c.MaxX, c.MaxY);
            }
            else
            {
                a = GetPixelFloat(map, c.MinX, c.MinY);
                b = GetPixelFloat(map, c.MaxX, c.MinY);
                d = GetPixelFloat(map, c.MinX, c.MaxY);
                e = GetPixelFloat(map, c.MaxX, c.MaxY);
            }

            float low = Lerp(a, b, c.MidX);
            float high = Lerp(d, e, c.MidX);
            return Lerp(low, high, c.MidY);
        }

        internal static float GetPixelFloat(
            MapSnapshot map,
            int x,
            int y)
        {
            if (map == null) throw new ArgumentNullException("map");

            int index = PixelIndex(map, x, y);
            float retVal = 0f;

            for (int i = 0; i < map.BytesPerPixel; i++)
            {
                float add = (float)map.Data[index + i];
                retVal = retVal + add;
            }

            retVal = retVal / (float)map.BytesPerPixel;
            retVal = retVal * Byte2Float;
            return retVal;
        }

        static Coords ConstructBilinearCoords(
            double x,
            double y,
            int width,
            int height)
        {
            double normalizedX = Math.Abs(x - Math.Floor(x));
            double normalizedY = Math.Abs(y - Math.Floor(y));

            double centerX = normalizedX * (double)width;
            int minX = (int)Math.Floor(centerX);
            int maxX = (int)Math.Ceiling(centerX);
            float midX = (float)(centerX - (double)minX);
            if (maxX == width) maxX = 0;

            double centerY = normalizedY * (double)height;
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

        static float GreyFloat(MapSnapshot map, int x, int y)
        {
            float b = (float)map.Data[PixelIndex(map, x, y)];
            float result = Byte2Float * b;
            return result;
        }

        static float Lerp(float a, float b, float t)
        {
            float delta = b - a;
            float ct = Clamp01(t);
            float scaled = delta * ct;
            float result = a + scaled;
            return result;
        }

        static float Clamp01(float value)
        {
            if (value < 0f) return 0f;
            if (value > 1f) return 1f;
            return value;
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
