using System;

namespace AERISFlightControl.Terrain
{
    // R041 all-body height-modifier chain pure CLR evaluator.
    //
    // HARD RULE: no Unity/KSP/runtime-object types in this file.
    // Runtime configuration is copied on the main thread into immutable
    // primitive/pure snapshots before worker evaluation.
    internal static class AERIS39AllBodyHeightModifierChainPureCpuExact
    {
        internal sealed class CertifiedHeightMapOpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly AERIS39HeightMapPureCpuExact.Snapshot HeightMap;

            internal CertifiedHeightMapOpSnapshot(
                AERIS39HeightMapPureCpuExact.Snapshot heightMap)
            {
                HeightMap = heightMap ?? throw new ArgumentNullException("heightMap");
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double u,
                double v,
                double height)
            {
                return AERIS39HeightMapPureCpuExact.Evaluate(
                    HeightMap, u, v, height);
            }
        }

        internal sealed class ChainSnapshot
        {
            internal readonly AERISR041MohoDresPureCpuExact.HeightOpSnapshot[] Ops;

            internal ChainSnapshot(
                AERISR041MohoDresPureCpuExact.HeightOpSnapshot[] ops)
            {
                if (ops == null || ops.Length == 0)
                    throw new ArgumentException("height modifier chain requires ops", "ops");

                Ops = new AERISR041MohoDresPureCpuExact.HeightOpSnapshot[ops.Length];
                for (int i = 0; i < ops.Length; i++)
                {
                    if (ops[i] == null)
                        throw new ArgumentException("height modifier op is null", "ops");
                    Ops[i] = ops[i];
                }
            }
        }

        internal static double Evaluate(
            ChainSnapshot snapshot,
            double x,
            double y,
            double z,
            double u,
            double v,
            double inputHeight)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            double height = inputHeight;
            for (int i = 0; i < snapshot.Ops.Length; i++)
                height = snapshot.Ops[i].Evaluate(x, y, z, u, v, height);
            return height;
        }

        internal static long DoubleBits(double value)
        {
            return BitConverter.DoubleToInt64Bits(value);
        }
    }
}
