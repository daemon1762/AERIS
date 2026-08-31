using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Security.Cryptography;

internal static class AERIS39MapSo2BNativePureCpuWitness
{
    const string ExpectedAssemblySha = "d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4";
    const string ExpectedMapSoMvid = "4b449f28-41f8-4227-adfa-ad3149c8fdba";
    const string ExpectedCoreSha = "36d48c2068f85117781e380375d027ef0942f3b0c98654282603649106d76a72";
    const string ExpectedCoreMvid = "12e76cd5-0cc6-4cf1-9e75-9e981cb725af";
    const int ExpectedByte2FloatBits = unchecked((int)0x3B808081);
    const int ExpectedFloat2ByteBits = unchecked((int)0x437F0000);

    static Type mapSoType;
    static Type colorType;
    static Type color32Type;

    static FieldInfo colorR;
    static FieldInfo colorG;
    static FieldInfo colorB;
    static FieldInfo colorA;
    static FieldInfo color32R;
    static FieldInfo color32G;
    static FieldInfo color32B;
    static FieldInfo color32A;

    static MethodInfo getPixelFloatInt;
    static MethodInfo getPixelFloatSingle;
    static MethodInfo getPixelFloatDouble;
    static MethodInfo getPixelColorInt;
    static MethodInfo getPixelColorSingle;
    static MethodInfo getPixelColorDouble;
    static MethodInfo getPixelColor32Int;
    static MethodInfo getPixelColor32Single;
    static MethodInfo getPixelColor32Double;

    static long floatChecks;
    static long colorChecks;
    static long color32Checks;
    static long mismatches;
    static int printedMismatches;
    static double maxAbsError;

    struct PColor
    {
        public float R;
        public float G;
        public float B;
        public float A;

        public PColor(float r, float g, float b, float a)
        {
            R = r; G = g; B = b; A = a;
        }
    }

    struct PColor32
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public PColor32(byte r, byte g, byte b, byte a)
        {
            R = r; G = g; B = b; A = a;
        }
    }

    struct Coords
    {
        public int MinX;
        public int MaxX;
        public int MinY;
        public int MaxY;
        public float MidX;
        public float MidY;
    }

    struct PointD
    {
        public double X;
        public double Y;
        public PointD(double x, double y) { X = x; Y = y; }
    }

    sealed class Snapshot
    {
        public int Width;
        public int Height;
        public int Bpp;
        public int RowWidth;
        public byte[] Data;
        public byte Val;
    }

    static int Main(string[] args)
    {
        if (args == null || args.Length != 1)
        {
            Console.Error.WriteLine("usage: AERIS39_MAPSO2B_native_purecpu_witness.exe <KSP Managed directory>");
            return 2;
        }

        string managed = Path.GetFullPath(args[0]);
        string assemblyPath = Path.Combine(managed, "Assembly-CSharp.dll");
        string corePath = Path.Combine(managed, "UnityEngine.CoreModule.dll");

        if (!File.Exists(assemblyPath) || !File.Exists(corePath))
        {
            Console.Error.WriteLine("FAIL: required managed assemblies missing");
            return 3;
        }

        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs eventArgs)
        {
            try
            {
                string name = new AssemblyName(eventArgs.Name).Name + ".dll";
                string candidate = Path.Combine(managed, name);
                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
            }
            catch
            {
                return null;
            }
        };

        try
        {
            string assemblySha = Sha256File(assemblyPath);
            string coreSha = Sha256File(corePath);
            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            Assembly core = Assembly.LoadFrom(corePath);
            mapSoType = assembly.GetType("MapSO", true, false);
            colorType = core.GetType("UnityEngine.Color", true, false);
            color32Type = core.GetType("UnityEngine.Color32", true, false);

            string mapMvid = mapSoType.Module.ModuleVersionId.ToString("D");
            string coreMvid = colorType.Module.ModuleVersionId.ToString("D");

            Console.WriteLine("=== AERIS39 MAPSO-2B NATIVE VS PURE CPU WITNESS ===");
            Console.WriteLine("assembly=" + assemblyPath);
            Console.WriteLine("assembly_sha256=" + assemblySha);
            Console.WriteLine("mapso_mvid=" + mapMvid);
            Console.WriteLine("unity_core=" + corePath);
            Console.WriteLine("unity_core_sha256=" + coreSha);
            Console.WriteLine("unity_core_mvid=" + coreMvid);

            bool identityPass =
                string.Equals(assemblySha, ExpectedAssemblySha, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(mapMvid, ExpectedMapSoMvid, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(coreSha, ExpectedCoreSha, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(coreMvid, ExpectedCoreMvid, StringComparison.OrdinalIgnoreCase);

            Console.WriteLine("managed_identity_exact=" + Bool(identityPass));
            if (!identityPass)
            {
                Console.WriteLine("AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS=FAIL");
                return 4;
            }

            BindReflection();
            RuntimeHelpers.RunClassConstructor(mapSoType.TypeHandle);

            float nativeByte2Float = (float)GetField(mapSoType, "Byte2Float", true).GetValue(null);
            float nativeFloat2Byte = (float)GetField(mapSoType, "Float2Byte", true).GetValue(null);
            int byteBits = FloatBits(nativeByte2Float);
            int floatBits = FloatBits(nativeFloat2Byte);

            Console.WriteLine("native_Byte2Float=" + nativeByte2Float.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("native_Byte2Float_bits=0x" + unchecked((uint)byteBits).ToString("X8", CultureInfo.InvariantCulture));
            Console.WriteLine("native_Float2Byte=" + nativeFloat2Byte.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("native_Float2Byte_bits=0x" + unchecked((uint)floatBits).ToString("X8", CultureInfo.InvariantCulture));
            Console.WriteLine("constant_bits_exact=" + Bool(byteBits == ExpectedByte2FloatBits && floatBits == ExpectedFloat2ByteBits));

            if (byteBits != ExpectedByte2FloatBits || floatBits != ExpectedFloat2ByteBits)
            {
                Console.WriteLine("AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS=FAIL");
                return 5;
            }

            int[,] dimensions = new int[,]
            {
                { 1, 1 },
                { 2, 3 },
                { 7, 5 },
                { 13, 11 }
            };

            int snapshots = 0;
            long coordinatePairs = 0;

            for (int d = 0; d < dimensions.GetLength(0); d++)
            {
                int width = dimensions[d, 0];
                int height = dimensions[d, 1];

                for (int bpp = 1; bpp <= 4; bpp++)
                {
                    Snapshot snapshot = MakeSnapshot(width, height, bpp);
                    object native = MakeNative(snapshot);
                    snapshots++;

                    TestIntegerSurface(native, snapshot);

                    List<PointD> points = BuildPoints(width, height, 0x9E3779B9u ^ (uint)(width * 1009 + height * 313 + bpp * 7919));
                    coordinatePairs += points.Count;
                    TestCoordinateSurface(native, snapshot, points);
                }
            }

            Console.WriteLine();
            Console.WriteLine("snapshots=" + snapshots.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("coordinate_pairs=" + coordinatePairs.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("float_checks=" + floatChecks.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("color_checks=" + colorChecks.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("color32_checks=" + color32Checks.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("total_checks=" + (floatChecks + colorChecks + color32Checks).ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("mismatch_count=" + mismatches.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine("max_abs_error=" + maxAbsError.ToString("R", CultureInfo.InvariantCulture));
            Console.WriteLine("bit_exact=" + Bool(mismatches == 0));
            Console.WriteLine("production_authority=PQS");
            Console.WriteLine("db_authority=PQS");
            Console.WriteLine("producer_switch=false");
            Console.WriteLine("db_write=false");
            Console.WriteLine("preload_mutation=false");
            Console.WriteLine("diagnostic_runtime_object_invocation=true");
            Console.WriteLine("production_worker_runtime_object_access=false");

            bool pass = mismatches == 0;
            Console.WriteLine("AERIS39_MAPSO2B_NATIVE_PURECPU_WITNESS=" + (pass ? "PASS" : "FAIL"));
            return pass ? 0 : 6;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL: " + ex.GetType().FullName + ": " + ex.Message);
            Console.Error.WriteLine(ex.StackTrace ?? string.Empty);
            return 7;
        }
    }

    static void BindReflection()
    {
        const BindingFlags allInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        getPixelFloatInt = RequireMethod(mapSoType, "GetPixelFloat", allInstance, new Type[] { typeof(int), typeof(int) });
        getPixelFloatSingle = RequireMethod(mapSoType, "GetPixelFloat", allInstance, new Type[] { typeof(float), typeof(float) });
        getPixelFloatDouble = RequireMethod(mapSoType, "GetPixelFloat", allInstance, new Type[] { typeof(double), typeof(double) });

        getPixelColorInt = RequireMethod(mapSoType, "GetPixelColor", allInstance, new Type[] { typeof(int), typeof(int) });
        getPixelColorSingle = RequireMethod(mapSoType, "GetPixelColor", allInstance, new Type[] { typeof(float), typeof(float) });
        getPixelColorDouble = RequireMethod(mapSoType, "GetPixelColor", allInstance, new Type[] { typeof(double), typeof(double) });

        getPixelColor32Int = RequireMethod(mapSoType, "GetPixelColor32", allInstance, new Type[] { typeof(int), typeof(int) });
        getPixelColor32Single = RequireMethod(mapSoType, "GetPixelColor32", allInstance, new Type[] { typeof(float), typeof(float) });
        getPixelColor32Double = RequireMethod(mapSoType, "GetPixelColor32", allInstance, new Type[] { typeof(double), typeof(double) });

        colorR = RequireField(colorType, "r", false);
        colorG = RequireField(colorType, "g", false);
        colorB = RequireField(colorType, "b", false);
        colorA = RequireField(colorType, "a", false);

        color32R = RequireField(color32Type, "r", false);
        color32G = RequireField(color32Type, "g", false);
        color32B = RequireField(color32Type, "b", false);
        color32A = RequireField(color32Type, "a", false);
    }

    static MethodInfo RequireMethod(Type type, string name, BindingFlags flags, Type[] parameters)
    {
        MethodInfo method = type.GetMethod(name, flags, null, parameters, null);
        if (method == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }

    static FieldInfo RequireField(Type type, string name, bool isStatic)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        FieldInfo field = type.GetField(name, flags);
        if (field == null)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    static FieldInfo GetField(Type type, string name, bool isStatic)
    {
        return RequireField(type, name, isStatic);
    }

    static Snapshot MakeSnapshot(int width, int height, int bpp)
    {
        Snapshot s = new Snapshot();
        s.Width = width;
        s.Height = height;
        s.Bpp = bpp;
        s.RowWidth = width * bpp;
        s.Data = new byte[width * height * bpp];
        s.Val = (byte)((width * 17 + height * 29 + bpp * 43) & 0xFF);

        for (int i = 0; i < s.Data.Length; i++)
            s.Data[i] = (byte)((i * 73 + width * 19 + height * 31 + bpp * 47 + (i >> 1) * 11) & 0xFF);

        byte[] anchors = new byte[] { 0, 1, 2, 63, 127, 128, 129, 191, 253, 254, 255 };
        for (int i = 0; i < anchors.Length && i < s.Data.Length; i++)
            s.Data[i] = anchors[i];

        return s;
    }

    static object MakeNative(Snapshot s)
    {
        object instance = FormatterServices.GetUninitializedObject(mapSoType);
        SetInstanceField(instance, "_width", s.Width);
        SetInstanceField(instance, "_height", s.Height);
        SetInstanceField(instance, "_bpp", s.Bpp);
        SetInstanceField(instance, "_rowWidth", s.RowWidth);
        SetInstanceField(instance, "_data", s.Data);
        SetInstanceField(instance, "val", s.Val);
        return instance;
    }

    static void SetInstanceField(object instance, string name, object value)
    {
        FieldInfo field = mapSoType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            throw new MissingFieldException(mapSoType.FullName, name);
        field.SetValue(instance, value);
    }

    static void TestIntegerSurface(object native, Snapshot s)
    {
        for (int y = 0; y < s.Height; y++)
        {
            for (int x = 0; x < s.Width; x++)
            {
                float nf = (float)getPixelFloatInt.Invoke(native, new object[] { x, y });
                float pf = PixelFloatInt(s, x, y);
                CheckFloat("GetPixelFloat(int,int) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp + " x=" + x + " y=" + y, nf, pf);

                object ncObj = getPixelColorInt.Invoke(native, new object[] { x, y });
                PColor nc = ReadColor(ncObj);
                PColor pc = PixelColorInt(s, x, y);
                CheckColor("GetPixelColor(int,int) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp + " x=" + x + " y=" + y, nc, pc);

                object n32Obj = getPixelColor32Int.Invoke(native, new object[] { x, y });
                PColor32 n32 = ReadColor32(n32Obj);
                PColor32 p32 = PixelColor32Int(s, x, y);
                CheckColor32("GetPixelColor32(int,int) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp + " x=" + x + " y=" + y, n32, p32);
            }
        }
    }

    static void TestCoordinateSurface(object native, Snapshot s, List<PointD> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            PointD p = points[i];
            float xf = (float)p.X;
            float yf = (float)p.Y;

            float nff = (float)getPixelFloatSingle.Invoke(native, new object[] { xf, yf });
            float pff = SampleFloatSingle(s, xf, yf);
            CheckFloat("GetPixelFloat(float,float) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp + " x=" + xf.ToString("R", CultureInfo.InvariantCulture) + " y=" + yf.ToString("R", CultureInfo.InvariantCulture), nff, pff);

            float nfd = (float)getPixelFloatDouble.Invoke(native, new object[] { p.X, p.Y });
            float pfd = SampleFloatDouble(s, p.X, p.Y);
            CheckFloat("GetPixelFloat(double,double) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp + " x=" + p.X.ToString("R", CultureInfo.InvariantCulture) + " y=" + p.Y.ToString("R", CultureInfo.InvariantCulture), nfd, pfd);

            PColor ncf = ReadColor(getPixelColorSingle.Invoke(native, new object[] { xf, yf }));
            PColor pcf = SampleColorSingle(s, xf, yf);
            CheckColor("GetPixelColor(float,float) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp, ncf, pcf);

            PColor ncd = ReadColor(getPixelColorDouble.Invoke(native, new object[] { p.X, p.Y }));
            PColor pcd = SampleColorDouble(s, p.X, p.Y);
            CheckColor("GetPixelColor(double,double) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp, ncd, pcd);

            PColor n32f = ReadColor(getPixelColor32Single.Invoke(native, new object[] { xf, yf }));
            PColor p32f = SampleColor32Single(s, xf, yf);
            CheckColor("GetPixelColor32(float,float) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp, n32f, p32f);

            PColor n32d = ReadColor(getPixelColor32Double.Invoke(native, new object[] { p.X, p.Y }));
            PColor p32d = SampleColor32Double(s, p.X, p.Y);
            CheckColor("GetPixelColor32(double,double) w=" + s.Width + " h=" + s.Height + " bpp=" + s.Bpp, n32d, p32d);
        }
    }

    static List<PointD> BuildPoints(int width, int height, uint seed)
    {
        List<PointD> points = new List<PointD>();
        double[] baseValues = new double[]
        {
            -3.75, -2.0, -1.25, -1.0, -0.9999999403953552, -0.5, -0.0000001,
            0.0, 0.0000001, 0.125, 0.4999999701976776, 0.5, 0.5000000596046448,
            0.875, 0.9999999403953552, 1.0, 1.0000001192092896, 1.25, 2.0, 3.75
        };

        for (int i = 0; i < baseValues.Length; i++)
            for (int j = 0; j < baseValues.Length; j++)
                points.Add(new PointD(baseValues[i], baseValues[j]));

        AddDimensionBoundaries(points, width, true);
        AddDimensionBoundaries(points, height, false);

        uint state = seed == 0 ? 1u : seed;
        for (int i = 0; i < 256; i++)
        {
            state = NextState(state);
            double x = ((double)state / 4294967296.0) * 8.0 - 4.0;
            state = NextState(state);
            double y = ((double)state / 4294967296.0) * 8.0 - 4.0;
            points.Add(new PointD(x, y));
        }

        return points;
    }

    static void AddDimensionBoundaries(List<PointD> points, int dimension, bool xAxis)
    {
        if (dimension <= 0) return;
        for (int i = 0; i <= dimension; i++)
        {
            double v = (double)i / (double)dimension;
            double vd = NextDoubleDown(v);
            double vu = NextDoubleUp(v);
            float vf = (float)v;
            double vfd = (double)NextFloatDown(vf);
            double vfu = (double)NextFloatUp(vf);

            if (xAxis)
            {
                points.Add(new PointD(v, 0.37109375));
                points.Add(new PointD(vd, 0.62890625));
                points.Add(new PointD(vu, -0.37109375));
                points.Add(new PointD(vfd, 1.37109375));
                points.Add(new PointD(vfu, -1.62890625));
                if (i < dimension)
                    points.Add(new PointD(((double)i + 0.5) / dimension, 0.5));
            }
            else
            {
                points.Add(new PointD(0.37109375, v));
                points.Add(new PointD(0.62890625, vd));
                points.Add(new PointD(-0.37109375, vu));
                points.Add(new PointD(1.37109375, vfd));
                points.Add(new PointD(-1.62890625, vfu));
                if (i < dimension)
                    points.Add(new PointD(0.5, ((double)i + 0.5) / dimension));
            }
        }
    }

    static uint NextState(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    static double NextDoubleUp(double value)
    {
        if (double.IsNaN(value) || value == double.PositiveInfinity) return value;
        if (value == 0.0) return BitConverter.Int64BitsToDouble(1L);
        long bits = BitConverter.DoubleToInt64Bits(value);
        bits += value > 0.0 ? 1L : -1L;
        return BitConverter.Int64BitsToDouble(bits);
    }

    static double NextDoubleDown(double value)
    {
        if (double.IsNaN(value) || value == double.NegativeInfinity) return value;
        if (value == 0.0) return BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000001UL));
        long bits = BitConverter.DoubleToInt64Bits(value);
        bits += value > 0.0 ? -1L : 1L;
        return BitConverter.Int64BitsToDouble(bits);
    }

    static float NextFloatUp(float value)
    {
        if (float.IsNaN(value) || value == float.PositiveInfinity) return value;
        if (value == 0f) return FloatFromBits(1);
        int bits = FloatBits(value);
        bits += value > 0f ? 1 : -1;
        return FloatFromBits(bits);
    }

    static float NextFloatDown(float value)
    {
        if (float.IsNaN(value) || value == float.NegativeInfinity) return value;
        if (value == 0f) return FloatFromBits(unchecked((int)0x80000001));
        int bits = FloatBits(value);
        bits += value > 0f ? -1 : 1;
        return FloatFromBits(bits);
    }

    static Coords ConstructSingle(float x, float y, int width, int height)
    {
        float floorX = (float)Math.Floor((double)x);
        float normalizedX = x - floorX;
        normalizedX = Math.Abs(normalizedX);

        float floorY = (float)Math.Floor((double)y);
        float normalizedY = y - floorY;
        normalizedY = Math.Abs(normalizedY);

        float centerX = normalizedX * (float)width;
        int minX = (int)Math.Floor((double)centerX);
        int maxX = (int)Math.Ceiling((double)centerX);
        float midX = centerX - (float)minX;
        if (maxX == width) maxX = 0;

        float centerY = normalizedY * (float)height;
        int minY = (int)Math.Floor((double)centerY);
        int maxY = (int)Math.Ceiling((double)centerY);
        float midY = centerY - (float)minY;
        if (maxY == height) maxY = 0;

        Coords c = new Coords();
        c.MinX = minX; c.MaxX = maxX; c.MinY = minY; c.MaxY = maxY; c.MidX = midX; c.MidY = midY;
        return c;
    }

    static Coords ConstructDouble(double x, double y, int width, int height)
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
        c.MinX = minX; c.MaxX = maxX; c.MinY = minY; c.MaxY = maxY; c.MidX = midX; c.MidY = midY;
        return c;
    }

    static int PixelIndex(Snapshot s, int x, int y)
    {
        return x * s.Bpp + y * s.RowWidth;
    }

    static float GreyFloat(Snapshot s, int x, int y)
    {
        float b = (float)s.Data[PixelIndex(s, x, y)];
        float result = Byte2Float() * b;
        return result;
    }

    static float PixelFloatInt(Snapshot s, int x, int y)
    {
        int index = PixelIndex(s, x, y);
        float retVal = 0f;
        for (int i = 0; i < s.Bpp; i++)
        {
            float add = (float)s.Data[index + i];
            retVal = retVal + add;
        }
        retVal = retVal / (float)s.Bpp;
        retVal = retVal * Byte2Float();
        return retVal;
    }

    static PColor PixelColorInt(Snapshot s, int x, int y)
    {
        int index = PixelIndex(s, x, y);
        float k = Byte2Float();

        if (s.Bpp == 3)
            return new PColor(k * (float)s.Data[index], k * (float)s.Data[index + 1], k * (float)s.Data[index + 2], 1f);

        if (s.Bpp == 4)
            return new PColor(k * (float)s.Data[index], k * (float)s.Data[index + 1], k * (float)s.Data[index + 2], k * (float)s.Data[index + 3]);

        if (s.Bpp == 2)
        {
            float retVal = k * (float)s.Data[index];
            return new PColor(retVal, retVal, retVal, k * (float)s.Data[index + 1]);
        }

        float gray = k * (float)s.Data[index];
        return new PColor(gray, gray, gray, 1f);
    }

    static PColor32 PixelColor32Int(Snapshot s, int x, int y)
    {
        int index = PixelIndex(s, x, y);

        if (s.Bpp == 3)
            return new PColor32(s.Data[index], s.Data[index + 1], s.Data[index + 2], 255);

        if (s.Bpp == 4)
            return new PColor32(s.Data[index], s.Data[index + 1], s.Data[index + 2], s.Data[index + 3]);

        if (s.Bpp == 2)
        {
            float ignoredRetVal = (float)s.Data[index];
            if (ignoredRetVal < -1f) throw new InvalidOperationException("unreachable");
            PColor c = new PColor((float)s.Val, (float)s.Val, (float)s.Val, (float)s.Data[index + 1]);
            return ColorToColor32(c);
        }

        byte gray = s.Data[index];
        return new PColor32(gray, gray, gray, gray);
    }

    static float SampleFloatSingle(Snapshot s, float x, float y)
    {
        Coords c = ConstructSingle(x, y, s.Width, s.Height);
        float a;
        float b;
        float d;
        float e;

        if (s.Bpp == 1)
        {
            a = GreyFloat(s, c.MinX, c.MinY);
            b = GreyFloat(s, c.MaxX, c.MinY);
            d = GreyFloat(s, c.MinX, c.MaxY);
            e = GreyFloat(s, c.MaxX, c.MaxY);
        }
        else
        {
            a = PixelFloatInt(s, c.MinX, c.MinY);
            b = PixelFloatInt(s, c.MaxX, c.MinY);
            d = PixelFloatInt(s, c.MinX, c.MaxY);
            e = PixelFloatInt(s, c.MaxX, c.MaxY);
        }

        float low = LerpFloat(a, b, c.MidX);
        float high = LerpFloat(d, e, c.MidX);
        return LerpFloat(low, high, c.MidY);
    }

    static float SampleFloatDouble(Snapshot s, double x, double y)
    {
        Coords c = ConstructDouble(x, y, s.Width, s.Height);
        float a;
        float b;
        float d;
        float e;

        if (s.Bpp == 1)
        {
            a = GreyFloat(s, c.MinX, c.MinY);
            b = GreyFloat(s, c.MaxX, c.MinY);
            d = GreyFloat(s, c.MinX, c.MaxY);
            e = GreyFloat(s, c.MaxX, c.MaxY);
        }
        else
        {
            a = PixelFloatInt(s, c.MinX, c.MinY);
            b = PixelFloatInt(s, c.MaxX, c.MinY);
            d = PixelFloatInt(s, c.MinX, c.MaxY);
            e = PixelFloatInt(s, c.MaxX, c.MaxY);
        }

        float low = LerpFloat(a, b, c.MidX);
        float high = LerpFloat(d, e, c.MidX);
        return LerpFloat(low, high, c.MidY);
    }

    static PColor SampleColorSingle(Snapshot s, float x, float y)
    {
        Coords c = ConstructSingle(x, y, s.Width, s.Height);
        PColor low = LerpColor(PixelColorInt(s, c.MinX, c.MinY), PixelColorInt(s, c.MaxX, c.MinY), c.MidX);
        PColor high = LerpColor(PixelColorInt(s, c.MinX, c.MaxY), PixelColorInt(s, c.MaxX, c.MaxY), c.MidX);
        return LerpColor(low, high, c.MidY);
    }

    static PColor SampleColorDouble(Snapshot s, double x, double y)
    {
        Coords c = ConstructDouble(x, y, s.Width, s.Height);
        PColor low = LerpColor(PixelColorInt(s, c.MinX, c.MinY), PixelColorInt(s, c.MaxX, c.MinY), c.MidX);
        PColor high = LerpColor(PixelColorInt(s, c.MinX, c.MaxY), PixelColorInt(s, c.MaxX, c.MaxY), c.MidX);
        return LerpColor(low, high, c.MidY);
    }

    static PColor SampleColor32Single(Snapshot s, float x, float y)
    {
        Coords c = ConstructSingle(x, y, s.Width, s.Height);
        PColor32 low = LerpColor32(PixelColor32Int(s, c.MinX, c.MinY), PixelColor32Int(s, c.MaxX, c.MinY), c.MidX);
        PColor32 high = LerpColor32(PixelColor32Int(s, c.MinX, c.MaxY), PixelColor32Int(s, c.MaxX, c.MaxY), c.MidX);
        PColor32 final = LerpColor32(low, high, c.MidY);
        return Color32ToColor(final);
    }

    static PColor SampleColor32Double(Snapshot s, double x, double y)
    {
        Coords c = ConstructDouble(x, y, s.Width, s.Height);
        PColor32 low = LerpColor32(PixelColor32Int(s, c.MinX, c.MinY), PixelColor32Int(s, c.MaxX, c.MinY), c.MidX);
        PColor32 high = LerpColor32(PixelColor32Int(s, c.MinX, c.MaxY), PixelColor32Int(s, c.MaxX, c.MaxY), c.MidX);
        PColor32 final = LerpColor32(low, high, c.MidY);
        return Color32ToColor(final);
    }

    static float Clamp01(float value)
    {
        if (value < 0f) return 0f;
        if (value > 1f) return 1f;
        return value;
    }

    static float LerpFloat(float a, float b, float t)
    {
        float delta = b - a;
        float ct = Clamp01(t);
        float scaled = delta * ct;
        float result = a + scaled;
        return result;
    }

    static PColor LerpColor(PColor a, PColor b, float t)
    {
        t = Clamp01(t);

        float dr = b.R - a.R;
        float rr = a.R + dr * t;
        float dg = b.G - a.G;
        float rg = a.G + dg * t;
        float db = b.B - a.B;
        float rb = a.B + db * t;
        float da = b.A - a.A;
        float ra = a.A + da * t;

        return new PColor(rr, rg, rb, ra);
    }

    static PColor32 LerpColor32(PColor32 a, PColor32 b, float t)
    {
        t = Clamp01(t);

        int dri = (int)b.R - (int)a.R;
        float dr = (float)dri;
        float rr = (float)a.R + dr * t;

        int dgi = (int)b.G - (int)a.G;
        float dg = (float)dgi;
        float rg = (float)a.G + dg * t;

        int dbi = (int)b.B - (int)a.B;
        float db = (float)dbi;
        float rb = (float)a.B + db * t;

        int dai = (int)b.A - (int)a.A;
        float da = (float)dai;
        float ra = (float)a.A + da * t;

        return new PColor32(unchecked((byte)rr), unchecked((byte)rg), unchecked((byte)rb), unchecked((byte)ra));
    }

    static PColor32 ColorToColor32(PColor c)
    {
        return new PColor32(
            FloatChannelToByte(c.R),
            FloatChannelToByte(c.G),
            FloatChannelToByte(c.B),
            FloatChannelToByte(c.A));
    }

    static byte FloatChannelToByte(float value)
    {
        float clamped = Clamp01(value);
        float scaled = clamped * 255f;
        double widened = (double)scaled;
        double roundedD = Math.Round(widened);
        float rounded = (float)roundedD;
        return unchecked((byte)rounded);
    }

    static PColor Color32ToColor(PColor32 c)
    {
        float r = (float)c.R;
        float g = (float)c.G;
        float b = (float)c.B;
        float a = (float)c.A;
        r = r / 255f;
        g = g / 255f;
        b = b / 255f;
        a = a / 255f;
        return new PColor(r, g, b, a);
    }

    static float Byte2Float()
    {
        return FloatFromBits(ExpectedByte2FloatBits);
    }

    static PColor ReadColor(object boxed)
    {
        return new PColor(
            (float)colorR.GetValue(boxed),
            (float)colorG.GetValue(boxed),
            (float)colorB.GetValue(boxed),
            (float)colorA.GetValue(boxed));
    }

    static PColor32 ReadColor32(object boxed)
    {
        return new PColor32(
            (byte)color32R.GetValue(boxed),
            (byte)color32G.GetValue(boxed),
            (byte)color32B.GetValue(boxed),
            (byte)color32A.GetValue(boxed));
    }

    static void CheckFloat(string label, float native, float pure)
    {
        floatChecks++;
        if (FloatBits(native) == FloatBits(pure)) return;
        RegisterFloatMismatch(label, "value", native, pure);
    }

    static void CheckColor(string label, PColor native, PColor pure)
    {
        colorChecks++;
        bool ok = true;
        ok &= FloatBits(native.R) == FloatBits(pure.R);
        ok &= FloatBits(native.G) == FloatBits(pure.G);
        ok &= FloatBits(native.B) == FloatBits(pure.B);
        ok &= FloatBits(native.A) == FloatBits(pure.A);
        if (ok) return;

        mismatches++;
        UpdateError(native.R, pure.R);
        UpdateError(native.G, pure.G);
        UpdateError(native.B, pure.B);
        UpdateError(native.A, pure.A);

        if (printedMismatches < 20)
        {
            printedMismatches++;
            Console.WriteLine("MISMATCH " + label +
                " native=" + ColorString(native) +
                " pure=" + ColorString(pure));
        }
    }

    static void CheckColor32(string label, PColor32 native, PColor32 pure)
    {
        color32Checks++;
        if (native.R == pure.R && native.G == pure.G && native.B == pure.B && native.A == pure.A) return;

        mismatches++;
        if (printedMismatches < 20)
        {
            printedMismatches++;
            Console.WriteLine("MISMATCH " + label +
                " native=" + Color32String(native) +
                " pure=" + Color32String(pure));
        }
    }

    static void RegisterFloatMismatch(string label, string channel, float native, float pure)
    {
        mismatches++;
        UpdateError(native, pure);
        if (printedMismatches < 20)
        {
            printedMismatches++;
            Console.WriteLine("MISMATCH " + label + " channel=" + channel +
                " native=" + native.ToString("R", CultureInfo.InvariantCulture) +
                " native_bits=0x" + unchecked((uint)FloatBits(native)).ToString("X8", CultureInfo.InvariantCulture) +
                " pure=" + pure.ToString("R", CultureInfo.InvariantCulture) +
                " pure_bits=0x" + unchecked((uint)FloatBits(pure)).ToString("X8", CultureInfo.InvariantCulture));
        }
    }

    static void UpdateError(float a, float b)
    {
        double error = Math.Abs((double)a - (double)b);
        if (error > maxAbsError) maxAbsError = error;
    }

    static string ColorString(PColor c)
    {
        return "(" +
            FloatWithBits(c.R) + "," +
            FloatWithBits(c.G) + "," +
            FloatWithBits(c.B) + "," +
            FloatWithBits(c.A) + ")";
    }

    static string Color32String(PColor32 c)
    {
        return "(" + c.R.ToString(CultureInfo.InvariantCulture) + "," +
            c.G.ToString(CultureInfo.InvariantCulture) + "," +
            c.B.ToString(CultureInfo.InvariantCulture) + "," +
            c.A.ToString(CultureInfo.InvariantCulture) + ")";
    }

    static string FloatWithBits(float value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture) + "/0x" +
            unchecked((uint)FloatBits(value)).ToString("X8", CultureInfo.InvariantCulture);
    }

    static int FloatBits(float value)
    {
        return BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
    }

    static float FloatFromBits(int bits)
    {
        return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
    }

    static string Bool(bool value)
    {
        return value ? "true" : "false";
    }

    static string Sha256File(string path)
    {
        using (FileStream stream = File.OpenRead(path))
        using (SHA256 sha = SHA256.Create())
        {
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
