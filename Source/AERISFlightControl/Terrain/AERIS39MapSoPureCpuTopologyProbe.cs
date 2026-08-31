using System;
using System.Runtime.CompilerServices;

namespace AERISFlightControl.Terrain
{
    // AERIS39 MAPSO-3 diagnostic only.
    // Primitive-only instance topology shaped after stock MapSO so the embedded
    // KSP Mono JIT sees direct instance fields + virtual/callvirt boundaries.
    // No Unity/KSP/runtime object is stored or accessed here.
    internal sealed class AERIS39MapSoPureCpuTopologyProbe
    {
        const int Byte2FloatBits = unchecked((int)0x3B808081);
        static readonly float Byte2Float = FloatFromBits(Byte2FloatBits);

        readonly byte[] _data;
        readonly int _width;
        readonly int _height;
        readonly int _bpp;
        readonly int _rowWidth;

        struct Coords
        {
            internal int MinX;
            internal int MaxX;
            internal int MinY;
            internal int MaxY;
            internal float MidX;
            internal float MidY;
        }

        internal AERIS39MapSoPureCpuTopologyProbe(
            byte[] data,
            int width,
            int height,
            int bpp,
            int rowWidth)
        {
            if (data == null) throw new ArgumentNullException("data");
            _data = (byte[])data.Clone();
            _width = width;
            _height = height;
            _bpp = bpp;
            _rowWidth = rowWidth;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal virtual float GetPixelFloat(double x, double y)
        {
            Coords c = ConstructBilinearCoords(x, y);

            if (_bpp == 1)
            {
                float low = Lerp(
                    GreyFloat(c.MinX, c.MinY),
                    GreyFloat(c.MaxX, c.MinY),
                    c.MidX);
                float high = Lerp(
                    GreyFloat(c.MinX, c.MaxY),
                    GreyFloat(c.MaxX, c.MaxY),
                    c.MidX);
                return Lerp(low, high, c.MidY);
            }

            float lowMulti = Lerp(
                GetPixelFloat(c.MinX, c.MinY),
                GetPixelFloat(c.MaxX, c.MinY),
                c.MidX);
            float highMulti = Lerp(
                GetPixelFloat(c.MinX, c.MaxY),
                GetPixelFloat(c.MaxX, c.MaxY),
                c.MidX);
            return Lerp(lowMulti, highMulti, c.MidY);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal virtual float GetPixelFloat(int x, int y)
        {
            int index = PixelIndex(x, y);
            float retVal = 0f;
            int itr = 0;
            while (itr < _bpp)
            {
                retVal = retVal + (float)_data[index + itr];
                itr = itr + 1;
            }
            retVal = retVal / (float)_bpp;
            retVal = retVal * Byte2Float;
            return retVal;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        virtual Coords ConstructBilinearCoords(double x, double y)
        {
            x = Math.Abs(x - Math.Floor(x));
            y = Math.Abs(y - Math.Floor(y));

            double centerX = x * (double)_width;
            int minX = (int)Math.Floor(centerX);
            int maxX = (int)Math.Ceiling(centerX);
            float midX = (float)(centerX - (double)minX);
            if (maxX == _width) maxX = 0;

            double centerY = y * (double)_height;
            int minY = (int)Math.Floor(centerY);
            int maxY = (int)Math.Ceiling(centerY);
            float midY = (float)(centerY - (double)minY);
            if (maxY == _height) maxY = 0;

            Coords c = new Coords();
            c.MinX = minX;
            c.MaxX = maxX;
            c.MinY = minY;
            c.MaxY = maxY;
            c.MidX = midX;
            c.MidY = midY;
            return c;
        }

        virtual int PixelIndex(int x, int y)
        {
            return unchecked(x * _bpp + y * _rowWidth);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        virtual float GreyFloat(int x, int y)
        {
            return Byte2Float * (float)_data[PixelIndex(x, y)];
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

        static float FloatFromBits(int bits)
        {
            byte[] bytes = BitConverter.GetBytes(bits);
            return BitConverter.ToSingle(bytes, 0);
        }
    }
}
