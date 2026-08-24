using System;

namespace AERISFlightControl.Terrain
{
    // AERIS34 R039 Minmus pure CPU exact reconstruction.
    //
    // IMPORTANT:
    // - This file has no Unity/KSP/runtime-object dependency.
    // - All arrays and scalar state arrive through immutable copied snapshots.
    // - Arithmetic order follows the R038/R039 captured CLR IL.
    // - Runtime objects must never be passed into these evaluators.
    internal static class AERISR039MinmusPureCpuExact
    {
        internal sealed class SimplexSnapshot
        {
            internal readonly int[] Perm;
            internal readonly int[][] Grad3;
            internal readonly double Frequency;
            internal readonly double Octaves;
            internal readonly double Persistence;

            internal SimplexSnapshot(
                int[] perm,
                int[][] grad3,
                double frequency,
                double octaves,
                double persistence)
            {
                if (perm == null || perm.Length != 512)
                    throw new ArgumentException("Simplex perm must contain 512 integers.", "perm");
                if (grad3 == null || grad3.Length != 12)
                    throw new ArgumentException("Simplex grad3 must contain 12 rows.", "grad3");

                Perm = (int[])perm.Clone();
                Grad3 = new int[grad3.Length][];
                for (int i = 0; i < grad3.Length; i++)
                {
                    if (grad3[i] == null || grad3[i].Length < 3)
                        throw new ArgumentException("Simplex grad3 row must contain at least 3 integers.", "grad3");
                    Grad3[i] = (int[])grad3[i].Clone();
                }

                Frequency = frequency;
                Octaves = octaves;
                Persistence = persistence;
            }
        }

        internal sealed class RidgedSnapshot
        {
            internal readonly double Frequency;
            internal readonly int Seed;
            internal readonly int NoiseQuality;
            internal readonly double Lacunarity;
            internal readonly int OctaveCount;
            internal readonly double[] SpectralWeights;
            internal readonly double[] RandomVectors;

            internal RidgedSnapshot(
                double frequency,
                int seed,
                int noiseQuality,
                double lacunarity,
                int octaveCount,
                double[] spectralWeights,
                double[] randomVectors)
            {
                if (spectralWeights == null || spectralWeights.Length < octaveCount)
                    throw new ArgumentException("Ridged spectral-weight table is incomplete.", "spectralWeights");
                if (randomVectors == null || randomVectors.Length != 1024)
                    throw new ArgumentException("LibNoise RandomVectors must contain 1024 doubles.", "randomVectors");

                Frequency = frequency;
                Seed = seed;
                NoiseQuality = noiseQuality;
                Lacunarity = lacunarity;
                OctaveCount = octaveCount;
                SpectralWeights = (double[])spectralWeights.Clone();
                RandomVectors = (double[])randomVectors.Clone();
            }
        }

        internal sealed class VertexPlanetSnapshot
        {
            internal readonly double Deformity;
            internal readonly double OceanLevel;
            internal readonly bool OceanSnap;
            internal readonly double OceanDepth;
            internal readonly double OceanStep;
            internal readonly double TerrainRidgeBalance;
            internal readonly double TerrainRidgesMin;
            internal readonly double TerrainRidgesMax;
            internal readonly double TerrainShapeStart;
            internal readonly double TerrainShapeEnd;

            internal readonly SimplexSnapshot Continental;
            internal readonly SimplexSnapshot ContinentalSmoothing;
            internal readonly SimplexSnapshot ContinentalSharpnessMap;
            internal readonly SimplexSnapshot ContinentalRuggedness;
            internal readonly RidgedSnapshot ContinentalSharpness;

            internal readonly double ContinentalDeformity;
            internal readonly double ContinentalPersistence;
            internal readonly double ContinentalSmoothingPersistence;
            internal readonly double ContinentalSharpnessMapDeformity;
            internal readonly double ContinentalRuggednessDeformity;
            internal readonly double ContinentalRuggednessPersistence;
            internal readonly double ContinentalSharpnessDeformity;

            internal VertexPlanetSnapshot(
                double deformity,
                double oceanLevel,
                bool oceanSnap,
                double oceanDepth,
                double oceanStep,
                double terrainRidgeBalance,
                double terrainRidgesMin,
                double terrainRidgesMax,
                double terrainShapeStart,
                double terrainShapeEnd,
                SimplexSnapshot continental,
                SimplexSnapshot continentalSmoothing,
                SimplexSnapshot continentalSharpnessMap,
                SimplexSnapshot continentalRuggedness,
                RidgedSnapshot continentalSharpness,
                double continentalDeformity,
                double continentalPersistence,
                double continentalSmoothingPersistence,
                double continentalSharpnessMapDeformity,
                double continentalRuggednessDeformity,
                double continentalRuggednessPersistence,
                double continentalSharpnessDeformity)
            {
                if (continental == null || continentalSmoothing == null ||
                    continentalSharpnessMap == null || continentalRuggedness == null ||
                    continentalSharpness == null)
                    throw new ArgumentNullException("VertexPlanet primitive snapshot is incomplete.");

                Deformity = deformity;
                OceanLevel = oceanLevel;
                OceanSnap = oceanSnap;
                OceanDepth = oceanDepth;
                OceanStep = oceanStep;
                TerrainRidgeBalance = terrainRidgeBalance;
                TerrainRidgesMin = terrainRidgesMin;
                TerrainRidgesMax = terrainRidgesMax;
                TerrainShapeStart = terrainShapeStart;
                TerrainShapeEnd = terrainShapeEnd;

                Continental = continental;
                ContinentalSmoothing = continentalSmoothing;
                ContinentalSharpnessMap = continentalSharpnessMap;
                ContinentalRuggedness = continentalRuggedness;
                ContinentalSharpness = continentalSharpness;

                ContinentalDeformity = continentalDeformity;
                ContinentalPersistence = continentalPersistence;
                ContinentalSmoothingPersistence = continentalSmoothingPersistence;
                ContinentalSharpnessMapDeformity = continentalSharpnessMapDeformity;
                ContinentalRuggednessDeformity = continentalRuggednessDeformity;
                ContinentalRuggednessPersistence = continentalRuggednessPersistence;
                ContinentalSharpnessDeformity = continentalSharpnessDeformity;
            }
        }

        internal static int FastFloor(double value)
        {
            // Captured IL: x > 0 ? (int)x : (int)x - 1.
            // Deliberately NOT Math.Floor; zero and exact negative integers differ.
            if (value > 0.0)
                return (int)value;
            return (int)value - 1;
        }

        static bool BltUn(double a, double b)
        {
            return double.IsNaN(a) || double.IsNaN(b) || a < b;
        }

        static bool BgeUn(double a, double b)
        {
            return double.IsNaN(a) || double.IsNaN(b) || a >= b;
        }

        static double Dot3(int[] gradient, double x, double y, double z)
        {
            double value = (double)gradient[0] * x;
            value = value + (double)gradient[1] * y;
            value = value + (double)gradient[2] * z;
            return value;
        }

        internal static double SimplexValue(
            SimplexSnapshot snapshot,
            double x,
            double y,
            double z)
        {
            const double F3 = 0.33333333333333331;
            const double G3 = 0.16666666666666666;

            double s = (x + y);
            s = s + z;
            s = s * F3;

            int i = FastFloor(x + s);
            int j = FastFloor(y + s);
            int k = FastFloor(z + s);

            int ijk = unchecked(i + j);
            ijk = unchecked(ijk + k);
            double t = (double)ijk * G3;

            double X0 = (double)i - t;
            double Y0 = (double)j - t;
            double Z0 = (double)k - t;

            double x0 = x - X0;
            double y0 = y - Y0;
            double z0 = z - Z0;

            int i1;
            int j1;
            int k1;
            int i2;
            int j2;
            int k2;

            // Branch shape follows captured blt.un / bge.un opcodes.
            if (!BltUn(x0, y0))
            {
                if (!BltUn(y0, z0))
                {
                    i1 = 1; j1 = 0; k1 = 0;
                    i2 = 1; j2 = 1; k2 = 0;
                }
                else if (!BltUn(x0, z0))
                {
                    i1 = 1; j1 = 0; k1 = 0;
                    i2 = 1; j2 = 0; k2 = 1;
                }
                else
                {
                    i1 = 0; j1 = 0; k1 = 1;
                    i2 = 1; j2 = 0; k2 = 1;
                }
            }
            else
            {
                if (!BgeUn(y0, z0))
                {
                    i1 = 0; j1 = 0; k1 = 1;
                    i2 = 0; j2 = 1; k2 = 1;
                }
                else if (!BgeUn(x0, z0))
                {
                    i1 = 0; j1 = 1; k1 = 0;
                    i2 = 0; j2 = 1; k2 = 1;
                }
                else
                {
                    i1 = 0; j1 = 1; k1 = 0;
                    i2 = 1; j2 = 1; k2 = 0;
                }
            }

            double x1 = x0 - (double)i1;
            x1 = x1 + G3;
            double y1 = y0 - (double)j1;
            y1 = y1 + G3;
            double z1 = z0 - (double)k1;
            z1 = z1 + G3;

            double twoG3 = 2.0 * G3;
            double x2 = x0 - (double)i2;
            x2 = x2 + twoG3;
            double y2 = y0 - (double)j2;
            y2 = y2 + twoG3;
            double z2 = z0 - (double)k2;
            z2 = z2 + twoG3;

            double threeG3 = 3.0 * G3;
            double x3 = x0 - 1.0;
            x3 = x3 + threeG3;
            double y3 = y0 - 1.0;
            y3 = y3 + threeG3;
            double z3 = z0 - 1.0;
            z3 = z3 + threeG3;

            int ii = i & 255;
            int jj = j & 255;
            int kk = k & 255;

            int gi0 = snapshot.Perm[
                ii + snapshot.Perm[
                    jj + snapshot.Perm[kk]]] % 12;

            int gi1 = snapshot.Perm[
                ii + i1 + snapshot.Perm[
                    jj + j1 + snapshot.Perm[
                        kk + k1]]] % 12;

            int gi2 = snapshot.Perm[
                ii + i2 + snapshot.Perm[
                    jj + j2 + snapshot.Perm[
                        kk + k2]]] % 12;

            int gi3 = snapshot.Perm[
                ii + 1 + snapshot.Perm[
                    jj + 1 + snapshot.Perm[
                        kk + 1]]] % 12;

            double t0 = 0.6 - x0 * x0;
            t0 = t0 - y0 * y0;
            t0 = t0 - z0 * z0;
            double n0;
            if (BgeUn(t0, 0.0))
            {
                t0 = t0 * t0;
                double d0 = Dot3(snapshot.Grad3[gi0], x0, y0, z0);
                n0 = (t0 * t0) * d0;
            }
            else
            {
                n0 = 0.0;
            }

            double t1 = 0.6 - x1 * x1;
            t1 = t1 - y1 * y1;
            t1 = t1 - z1 * z1;
            double n1;
            if (BgeUn(t1, 0.0))
            {
                t1 = t1 * t1;
                double d1 = Dot3(snapshot.Grad3[gi1], x1, y1, z1);
                n1 = (t1 * t1) * d1;
            }
            else
            {
                n1 = 0.0;
            }

            double t2 = 0.6 - x2 * x2;
            t2 = t2 - y2 * y2;
            t2 = t2 - z2 * z2;
            double n2;
            if (BgeUn(t2, 0.0))
            {
                t2 = t2 * t2;
                double d2 = Dot3(snapshot.Grad3[gi2], x2, y2, z2);
                n2 = (t2 * t2) * d2;
            }
            else
            {
                n2 = 0.0;
            }

            double t3 = 0.6 - x3 * x3;
            t3 = t3 - y3 * y3;
            t3 = t3 - z3 * z3;
            double n3;
            if (BgeUn(t3, 0.0))
            {
                t3 = t3 * t3;
                double d3 = Dot3(snapshot.Grad3[gi3], x3, y3, z3);
                n3 = (t3 * t3) * d3;
            }
            else
            {
                n3 = 0.0;
            }

            double sum = n0 + n1;
            sum = sum + n2;
            sum = sum + n3;
            return 32.0 * sum;
        }

        internal static double SimplexNoise(
            SimplexSnapshot snapshot,
            double x,
            double y,
            double z,
            double persistence)
        {
            double total = 0.0;
            double amplitude = 1.0;
            double f = snapshot.Frequency;
            double maxAmplitude = 0.0;
            double itr = 0.0;

            while (itr < snapshot.Octaves)
            {
                double nx = x * f;
                double ny = y * f;
                double nz = z * f;
                double value = SimplexValue(snapshot, nx, ny, nz);
                double weighted = value * amplitude;
                total = total + weighted;

                f = f * 2.0;
                maxAmplitude = maxAmplitude + amplitude;
                amplitude = amplitude * persistence;
                itr = itr + 1.0;
            }

            return total / maxAmplitude;
        }

        internal static double SimplexNoiseNormalized(
            SimplexSnapshot snapshot,
            double x,
            double y,
            double z,
            double persistence)
        {
            double value = SimplexNoise(snapshot, x, y, z, persistence);
            value = value + 1.0;
            value = value * 0.5;
            return value;
        }

        internal static double SCurve3(double a)
        {
            double a2 = a * a;
            double inner = 2.0 * a;
            inner = 3.0 - inner;
            return a2 * inner;
        }

        internal static double SCurve5(double a)
        {
            double a3 = a * a;
            a3 = a3 * a;
            double a4 = a3 * a;
            double a5 = a4 * a;

            double result = 6.0 * a5;
            result = result - 15.0 * a4;
            result = result + 10.0 * a3;
            return result;
        }

        internal static double LinearInterpolate(double a, double b, double t)
        {
            double left = 1.0 - t;
            left = left * a;
            double right = t * b;
            return left + right;
        }

        internal static double GradientNoise(
            RidgedSnapshot snapshot,
            double fx,
            double fy,
            double fz,
            int ix,
            int iy,
            int iz,
            long seed)
        {
            int hash = unchecked(1619 * ix);
            hash = unchecked(hash + unchecked(31337 * iy));
            hash = unchecked(hash + unchecked(6971 * iz));

            long vectorIndex = (long)hash;
            vectorIndex = unchecked(vectorIndex + 1013L * seed);
            vectorIndex = vectorIndex & -1L;
            vectorIndex = vectorIndex ^ (vectorIndex >> 8);
            vectorIndex = vectorIndex & 255L;

            int baseIndex = checked((int)(vectorIndex << 2));

            double xGradient = snapshot.RandomVectors[baseIndex];
            double yGradient = snapshot.RandomVectors[baseIndex + 1];
            double zGradient = snapshot.RandomVectors[baseIndex + 2];

            double xPoint = fx - (double)ix;
            double yPoint = fy - (double)iy;
            double zPoint = fz - (double)iz;

            double value = xGradient * xPoint;
            value = value + yGradient * yPoint;
            value = value + zGradient * zPoint;
            value = value * 2.12;
            return value;
        }

        internal static double GradientCoherentNoise(
            RidgedSnapshot snapshot,
            double x,
            double y,
            double z,
            int seed,
            int quality)
        {
            int x0 = FastFloor(x);
            int x1 = x0 + 1;
            int y0 = FastFloor(y);
            int y1 = y0 + 1;
            int z0 = FastFloor(z);
            int z1 = z0 + 1;

            double xs = 0.0;
            double ys = 0.0;
            double zs = 0.0;

            switch (quality)
            {
                case 0:
                    xs = x - (double)x0;
                    ys = y - (double)y0;
                    zs = z - (double)z0;
                    break;

                case 1:
                    xs = SCurve3(x - (double)x0);
                    ys = SCurve3(y - (double)y0);
                    zs = SCurve3(z - (double)z0);
                    break;

                case 2:
                    xs = SCurve5(x - (double)x0);
                    ys = SCurve5(y - (double)y0);
                    zs = SCurve5(z - (double)z0);
                    break;
            }

            double n0 = GradientNoise(snapshot, x, y, z, x0, y0, z0, seed);
            double n1 = GradientNoise(snapshot, x, y, z, x1, y0, z0, seed);
            double ix0 = LinearInterpolate(n0, n1, xs);

            n0 = GradientNoise(snapshot, x, y, z, x0, y1, z0, seed);
            n1 = GradientNoise(snapshot, x, y, z, x1, y1, z0, seed);
            double ix1 = LinearInterpolate(n0, n1, xs);
            double iy0 = LinearInterpolate(ix0, ix1, ys);

            n0 = GradientNoise(snapshot, x, y, z, x0, y0, z1, seed);
            n1 = GradientNoise(snapshot, x, y, z, x1, y0, z1, seed);
            ix0 = LinearInterpolate(n0, n1, xs);

            n0 = GradientNoise(snapshot, x, y, z, x0, y1, z1, seed);
            n1 = GradientNoise(snapshot, x, y, z, x1, y1, z1, seed);
            ix1 = LinearInterpolate(n0, n1, xs);
            double iy1 = LinearInterpolate(ix0, ix1, ys);

            return LinearInterpolate(iy0, iy1, zs);
        }

        internal static double RidgedGetValue(
            RidgedSnapshot snapshot,
            double x,
            double y,
            double z)
        {
            x = x * snapshot.Frequency;
            y = y * snapshot.Frequency;
            z = z * snapshot.Frequency;

            double signal = 0.0;
            double value = 0.0;
            double weight = 1.0;
            double offset = 1.0;
            double gain = 2.0;

            int i = 0;
            while (i < snapshot.OctaveCount)
            {
                int seed = unchecked(snapshot.Seed + i);
                seed = seed & int.MaxValue;

                signal = GradientCoherentNoise(
                    snapshot,
                    x,
                    y,
                    z,
                    seed,
                    snapshot.NoiseQuality);

                signal = Math.Abs(signal);
                signal = offset - signal;
                signal = signal * signal;
                signal = signal * weight;

                weight = signal * gain;
                if (weight > 1.0)
                    weight = 1.0;
                if (weight < 0.0)
                    weight = 0.0;

                double weighted = signal * snapshot.SpectralWeights[i];
                value = value + weighted;

                x = x * snapshot.Lacunarity;
                y = y * snapshot.Lacunarity;
                z = z * snapshot.Lacunarity;
                i = i + 1;
            }

            value = value * 1.25;
            value = value - 1.0;
            return value;
        }

        internal static double Lerp(double a, double b, double t)
        {
            // Captured IL: b*t + a*(1-t), in this exact order.
            double result = b * t;
            double oneMinus = 1.0 - t;
            double left = a * oneMinus;
            return result + left;
        }

        internal static double Clamp(double value, double minimum, double maximum)
        {
            if (value < minimum)
                return minimum;
            if (value > maximum)
                return maximum;
            return value;
        }

        internal static double CubicHermite(
            double p0,
            double p1,
            double m0,
            double m1,
            double t)
        {
            double t2 = t * t;
            double t3 = t2 * t;

            double h00 = 2.0 * t3;
            h00 = h00 - 3.0 * t2;
            h00 = h00 + 1.0;
            double result = p0 * h00;

            double h10 = t3 - 2.0 * t2;
            h10 = h10 + t;
            result = result + m0 * h10;

            double h01 = -2.0 * t3;
            h01 = h01 + 3.0 * t2;
            result = result + p1 * h01;

            double h11 = t3 - t2;
            result = result + m1 * h11;

            return result;
        }

        internal static double EvaluateVertexPlanet(
            VertexPlanetSnapshot snapshot,
            double x,
            double y,
            double z,
            double initialVertHeight)
        {
            double continentalDeformity = 1.0;

            double continental2Height = SimplexNoiseNormalized(
                snapshot.ContinentalSmoothing,
                x,
                y,
                z,
                snapshot.ContinentalSmoothing.Persistence);

            double continentalPersistence =
                snapshot.ContinentalSmoothingPersistence * continental2Height;
            continentalPersistence =
                snapshot.ContinentalPersistence - continentalPersistence;

            double continentialHeight = SimplexNoiseNormalized(
                snapshot.Continental,
                x,
                y,
                z,
                continentalPersistence);

            double continentialSharpnessValue =
                RidgedGetValue(snapshot.ContinentalSharpness, x, y, z);
            continentialSharpnessValue = continentialSharpnessValue + 1.0;
            continentialSharpnessValue = continentialSharpnessValue * 0.5;

            double sharpnessLerpB =
                snapshot.ContinentalSharpnessDeformity *
                snapshot.TerrainRidgeBalance;
            double sharpnessLerpT =
                continental2Height + continentialSharpnessValue;
            sharpnessLerpT = sharpnessLerpT * 0.5;
            double sharpnessLerp = Lerp(
                snapshot.ContinentalSharpnessDeformity,
                sharpnessLerpB,
                sharpnessLerpT);
            continentialSharpnessValue =
                continentialSharpnessValue * sharpnessLerp;

            double continentialSharpnessMapValue = SimplexNoise(
                snapshot.ContinentalSharpnessMap,
                x,
                y,
                z,
                snapshot.ContinentalSharpnessMap.Persistence);
            continentialSharpnessMapValue =
                continentialSharpnessMapValue + 1.0;
            continentialSharpnessMapValue =
                continentialSharpnessMapValue * 0.5;
            continentialSharpnessMapValue = Clamp(
                continentialSharpnessMapValue,
                snapshot.TerrainRidgesMin,
                snapshot.TerrainRidgesMax);

            continentialSharpnessMapValue =
                continentialSharpnessMapValue - snapshot.TerrainRidgesMin;
            double ridgeRange =
                snapshot.TerrainRidgesMax - snapshot.TerrainRidgesMin;
            continentialSharpnessMapValue =
                continentialSharpnessMapValue / ridgeRange;
            continentialSharpnessMapValue =
                continentialSharpnessMapValue *
                snapshot.ContinentalSharpnessMapDeformity;

            double addedSharpness = Lerp(
                0.0,
                continentialSharpnessValue,
                continentialSharpnessMapValue);
            continentialSharpnessValue =
                continentialSharpnessValue + addedSharpness;

            continentialHeight =
                continentialHeight + continentialSharpnessValue;

            double deformityProduct =
                snapshot.ContinentalSharpnessDeformity *
                snapshot.ContinentalSharpnessMapDeformity;
            continentalDeformity =
                continentalDeformity + deformityProduct;

            continentialHeight =
                continentialHeight / continentalDeformity;

            double continentalDelta =
                continentialHeight - snapshot.OceanLevel;
            double oceanDenominator =
                1.0 - snapshot.OceanLevel;
            continentalDelta =
                continentalDelta / oceanDenominator;

            double vHeight;
            if (BgeUn(continentialHeight, snapshot.OceanLevel))
            {
                double ruggedPersistence =
                    snapshot.ContinentalRuggednessPersistence *
                    continentalDelta;

                double continentalRHeight = SimplexNoiseNormalized(
                    snapshot.ContinentalRuggedness,
                    x,
                    y,
                    z,
                    ruggedPersistence);
                continentalRHeight =
                    continentalRHeight * continentalDelta;
                continentalRHeight =
                    continentalRHeight * continentalDelta;

                continentialHeight =
                    continentalDelta * snapshot.ContinentalDeformity;
                double ruggedTerm =
                    continentalRHeight *
                    snapshot.ContinentalRuggednessDeformity;
                continentialHeight =
                    continentialHeight + ruggedTerm;

                double totalDeformity =
                    snapshot.ContinentalDeformity +
                    snapshot.ContinentalRuggednessDeformity;
                continentialHeight =
                    continentialHeight / totalDeformity;

                // The runtime stores a pre-smooth field here. It has no
                // downstream effect on the pure return value.
                double continentialHeightPreSmooth = continentialHeight;
                if (double.IsNaN(continentialHeightPreSmooth))
                {
                    // Keep the local live so the assignment cannot be
                    // algebraically elided into surrounding expressions.
                }

                continentialHeight = CubicHermite(
                    0.0,
                    1.0,
                    snapshot.TerrainShapeStart,
                    snapshot.TerrainShapeEnd,
                    continentialHeight);

                vHeight = continentialHeight;
            }
            else
            {
                if (snapshot.OceanSnap)
                {
                    vHeight = -snapshot.OceanStep;
                }
                else
                {
                    vHeight =
                        continentalDelta * snapshot.OceanDepth;
                    vHeight =
                        vHeight - snapshot.OceanStep;
                }

                double continentialHeightPreSmooth = vHeight;
                if (double.IsNaN(continentialHeightPreSmooth))
                {
                    // Same captured side-state assignment as runtime.
                }
            }

            double rounded = Math.Round(vHeight, 5);
            double deformation = rounded * snapshot.Deformity;
            return initialVertHeight + deformation;
        }
    }
}
