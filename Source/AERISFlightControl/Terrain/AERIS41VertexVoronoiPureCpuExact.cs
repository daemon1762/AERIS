using System;

namespace AERISFlightControl.Terrain
{
    // AERIS41 R041 Eeloo PQSMod_VertexVoronoi pure CPU exact evaluator.
    //
    // Source authority:
    // - KSP 1.12.5 Assembly-CSharp.dll SHA256
    //   d9e42483f25ee80a9c11d6c1c0a0d29b4ec78c1e08d76c971b71580c9cce51e4
    // - PQSMod_VertexVoronoi.OnSetup IL SHA256
    //   3001bcb789bb44ad2ad6f8e71427072cba1273d4aee60ceda9429703da20a056
    // - PQSMod_VertexVoronoi.OnVertexBuildHeight IL SHA256
    //   6d29f26da507292670e5ed64d813cc3192035260be62df4f484f853bc43c69f3
    // - LibNoise.Voronoi.GetValue(double,double,double) IL SHA256
    //   2c7c9267e110e5a18b00a9e96ef6892479ef9ff69b686b480dda100d313975a3
    // - LibNoise.ValueNoiseBasis.IntValueNoise IL SHA256
    //   7733bb62195c7d449d8b94acb5dc5563abd96f4b1979f29b0fe4fe1f2b997d4b
    //
    // HARD RULE: no Unity/KSP/runtime-object type is referenced here.
    internal static class AERIS41VertexVoronoiPureCpuExact
    {
        // Captured LibNoise.Math.Sqrt3 static value, including exact binary64 bits
        // 0x3FFBB67AE8584CAA.
        const double Sqrt3 = 1.7320508075688772;

        internal sealed class OpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly double Frequency;
            internal readonly double Displacement;
            internal readonly int Seed;
            internal readonly bool DistanceEnabled;
            internal readonly double Deformation;

            internal OpSnapshot(
                double frequency,
                double displacement,
                int seed,
                bool distanceEnabled,
                double deformation)
            {
                Frequency = frequency;
                Displacement = displacement;
                Seed = seed;
                DistanceEnabled = distanceEnabled;
                Deformation = deformation;
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height)
            {
                double value = VoronoiGetValue(
                    x, y, z,
                    Frequency,
                    Displacement,
                    Seed,
                    DistanceEnabled);

                // Captured PQSMod_VertexVoronoi.OnVertexBuildHeight order:
                // oldHeight + (voronoi.GetValue(direction.x,y,z) * deformation).
                double delta = value * Deformation;
                return height + delta;
            }
        }

        // Captured branch/conv.i4 shape used by LibNoise.Voronoi, deliberately
        // not Math.Floor. Zero and exact negative integers therefore map one cell
        // lower, matching the runtime IL.
        static int VoronoiCell(double value)
        {
            if (value > 0.0)
                return (int)value;
            return unchecked((int)value - 1);
        }

        internal static int IntValueNoise(int x, int y, int z, int seed)
        {
            // Exact captured unchecked int32 operation order.
            int n = unchecked(1619 * x);
            n = unchecked(n + unchecked(31337 * y));
            n = unchecked(n + unchecked(6971 * z));
            n = unchecked(n + unchecked(1013 * seed));
            n = n & 2147483647;

            n = unchecked((n >> 13) ^ n);

            int square = unchecked(n * n);
            int inner = unchecked(square * 60493);
            inner = unchecked(inner + 19990303);
            int result = unchecked(n * inner);
            result = unchecked(result + 1376312589);
            return result & 2147483647;
        }

        internal static double ValueNoise(int x, int y, int z, int seed)
        {
            // Captured IL: 1.0 - ((double)IntValueNoise / 1073741824.0).
            double value = (double)IntValueNoise(x, y, z, seed);
            value = value / 1073741824.0;
            return 1.0 - value;
        }

        internal static double ValueNoise(int x, int y, int z)
        {
            return ValueNoise(x, y, z, 0);
        }

        internal static double VoronoiGetValue(
            double x,
            double y,
            double z,
            double frequency,
            double displacement,
            int seed,
            bool distanceEnabled)
        {
            // Captured GetValue begins by scaling each argument independently.
            x = x * frequency;
            y = y * frequency;
            z = z * frequency;

            int xInt = VoronoiCell(x);
            int yInt = VoronoiCell(y);
            int zInt = VoronoiCell(z);

            double minDistance = 2147483647.0;
            double xCandidate = 0.0;
            double yCandidate = 0.0;
            double zCandidate = 0.0;

            // Captured nesting is z outer, y middle, x inner, inclusive [-2,+2].
            int zCur = unchecked(zInt - 2);
            while (zCur <= unchecked(zInt + 2))
            {
                int yCur = unchecked(yInt - 2);
                while (yCur <= unchecked(yInt + 2))
                {
                    int xCur = unchecked(xInt - 2);
                    while (xCur <= unchecked(xInt + 2))
                    {
                        double xPos = (double)xCur;
                        xPos = xPos + ValueNoise(xCur, yCur, zCur, seed);

                        double yPos = (double)yCur;
                        yPos = yPos + ValueNoise(
                            xCur, yCur, zCur, unchecked(seed + 1));

                        double zPos = (double)zCur;
                        zPos = zPos + ValueNoise(
                            xCur, yCur, zCur, unchecked(seed + 2));

                        double xDist = xPos - x;
                        double yDist = yPos - y;
                        double zDist = zPos - z;

                        double distance = xDist * xDist;
                        distance = distance + (yDist * yDist);
                        distance = distance + (zDist * zDist);

                        // Captured bge.un skips the update for >= and NaN.
                        // C# '<' is likewise false for NaN.
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            xCandidate = xPos;
                            yCandidate = yPos;
                            zCandidate = zPos;
                        }

                        xCur = unchecked(xCur + 1);
                    }
                    yCur = unchecked(yCur + 1);
                }
                zCur = unchecked(zCur + 1);
            }

            double value;
            if (distanceEnabled)
            {
                double dx = xCandidate - x;
                double dy = yCandidate - y;
                double dz = zCandidate - z;

                double distance = dx * dx;
                distance = distance + (dy * dy);
                distance = distance + (dz * dz);
                value = Math.Sqrt(distance);
                value = value * Sqrt3;
                value = value - 1.0;
            }
            else
            {
                value = 0.0;
            }

            int xCell = VoronoiCell(xCandidate);
            int yCell = VoronoiCell(yCandidate);
            int zCell = VoronoiCell(zCandidate);

            double displacementNoise = ValueNoise(xCell, yCell, zCell);
            displacementNoise = displacement * displacementNoise;
            return value + displacementNoise;
        }
    }
}
