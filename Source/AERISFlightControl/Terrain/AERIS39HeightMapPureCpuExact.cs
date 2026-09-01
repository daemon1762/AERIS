using System;

namespace AERISFlightControl.Terrain
{
    // R041 HEIGHTMAP FULL FORMULA SHADOW.
    // Pure CLR only: no Unity/KSP/runtime-object types.
    //
    // Formula authority: R041C PQSMod_VertexHeightMap.OnVertexBuildHeight IL.
    // Do not algebraically simplify or reorder this path.
    internal static class AERIS39HeightMapPureCpuExact
    {
        internal sealed class Snapshot
        {
            internal readonly double Offset;
            internal readonly double Deformity;
            internal readonly AERIS39MapSoPureCpuExact.MapSnapshot Map;

            internal Snapshot(
                double offset,
                double deformity,
                AERIS39MapSoPureCpuExact.MapSnapshot map)
            {
                Offset = offset;
                Deformity = deformity;
                Map = map ?? throw new ArgumentNullException("map");
            }
        }

        internal static double Evaluate(
            Snapshot snapshot,
            double u,
            double v,
            double inputHeight)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            // Exact R041C/stock evaluation order:
            // pixel(float) -> deformity(double) * pixel(double)
            // -> inputHeight + offset -> + product.
            float pixel = AERIS39MapSoPureCpuExact.GetPixelFloat(snapshot.Map, u, v);
            double product = snapshot.Deformity * (double)pixel;
            double value = inputHeight + snapshot.Offset;
            value = value + product;
            return value;
        }

        internal static long DoubleBits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value);
        }
    }
}
