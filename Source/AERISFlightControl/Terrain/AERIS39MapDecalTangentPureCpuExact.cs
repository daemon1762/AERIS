using System;

namespace AERISFlightControl.Terrain
{
    // R041 pure CLR height evaluator for stock PQSMod_MapDecalTangent.
    //
    // HARD RULE: no Unity/KSP/runtime-object types in this file. Runtime state is
    // snapshotted on the main thread as primitive values plus an immutable MapSO copy.
    internal static class AERIS39MapDecalTangentPureCpuExact
    {
        internal struct HeightAlphaSample
        {
            internal readonly float Height;
            internal readonly float Alpha;

            internal HeightAlphaSample(float height, float alpha)
            {
                Height = height;
                Alpha = alpha;
            }
        }

        internal sealed class OpSnapshot :
            AERISR041MohoDresPureCpuExact.HeightOpSnapshot
        {
            internal readonly double DecalRadius;
            internal readonly double SphereRadius;
            internal readonly double HeightMapDeformity;
            internal readonly bool CullBlack;
            internal readonly bool UseAlphaHeightSmoothing;
            internal readonly bool Absolute;
            internal readonly double AbsoluteOffset;
            internal readonly float SmoothHeight;
            internal readonly float SmoothHR;
            internal readonly float SmoothH1M;
            internal readonly bool QuadActive;
            internal readonly bool BuildHeight;
            internal readonly bool SphereIsBuildingMaps;
            internal readonly double InclusionAngle;
            internal readonly double PosNormX;
            internal readonly double PosNormY;
            internal readonly double PosNormZ;
            internal readonly float RotX;
            internal readonly float RotY;
            internal readonly float RotZ;
            internal readonly float RotW;
            internal readonly AERIS39MapSoPureCpuExact.MapSnapshot HeightMap;

            internal OpSnapshot(
                double decalRadius,
                double sphereRadius,
                double heightMapDeformity,
                bool cullBlack,
                bool useAlphaHeightSmoothing,
                bool absolute,
                double absoluteOffset,
                float smoothHeight,
                float smoothHR,
                float smoothH1M,
                bool quadActive,
                bool buildHeight,
                bool sphereIsBuildingMaps,
                double inclusionAngle,
                double posNormX,
                double posNormY,
                double posNormZ,
                float rotX,
                float rotY,
                float rotZ,
                float rotW,
                AERIS39MapSoPureCpuExact.MapSnapshot heightMap)
            {
                DecalRadius = decalRadius;
                SphereRadius = sphereRadius;
                HeightMapDeformity = heightMapDeformity;
                CullBlack = cullBlack;
                UseAlphaHeightSmoothing = useAlphaHeightSmoothing;
                Absolute = absolute;
                AbsoluteOffset = absoluteOffset;
                SmoothHeight = smoothHeight;
                SmoothHR = smoothHR;
                SmoothH1M = smoothH1M;
                QuadActive = quadActive;
                BuildHeight = buildHeight;
                SphereIsBuildingMaps = sphereIsBuildingMaps;
                InclusionAngle = inclusionAngle;
                PosNormX = posNormX;
                PosNormY = posNormY;
                PosNormZ = posNormZ;
                RotX = rotX;
                RotY = rotY;
                RotZ = rotZ;
                RotW = rotW;
                HeightMap = heightMap;
            }

            internal override double Evaluate(
                double x,
                double y,
                double z,
                double uIgnored,
                double vIgnored,
                double height)
            {
                // Exact stock OnVertexBuildHeight control flow. quadActive/buildHeight,
                // rot, posNorm and smoothing coefficients are captured from the live
                // modifier after setup rather than reconstructed on the worker.
                if (!QuadActive && BuildHeight)
                    return height;

                if (SphereIsBuildingMaps)
                {
                    double quadDot = Dot(x, y, z, PosNormX, PosNormY, PosNormZ);
                    double quadAngle = Math.Acos(quadDot);
                    if (quadAngle > InclusionAngle)
                        return height;
                }

                // Stock source performs Quaternion * Vector3 after the Vector3d input is
                // converted to UnityEngine.Vector3 (binary32), then casts the result back
                // to Vector3d. Keep that exact precision boundary.
                float px = (float)x;
                float py = (float)y;
                float pz = (float)z;
                float rx;
                float ry;
                float rz;
                RotateQuaternionVector(
                    RotX, RotY, RotZ, RotW,
                    px, py, pz,
                    out rx, out ry, out rz);

                float mapU = (float)(((((double)rx * SphereRadius) / DecalRadius) + 1d) * 0.5d);
                float mapV = (float)(((((double)rz * SphereRadius) / DecalRadius) + 1d) * 0.5d);

                if (mapU < 0f || mapU > 1f || mapV < 0f || mapV > 1f)
                    return height;

                if (HeightMap == null)
                    return height;

                HeightAlphaSample ha = GetPixelHeightAlpha(HeightMap, mapU, mapV);
                float smoothFactor = GetHeightSmoothing(mapU, mapV, SmoothHeight, SmoothHR, SmoothH1M);
                if (UseAlphaHeightSmoothing)
                    smoothFactor *= ha.Alpha;

                double heightDot = Dot(x, y, z, PosNormX, PosNormY, PosNormZ);
                double vHeight = (HeightMapDeformity * (double)ha.Height) / heightDot;
                if (Absolute)
                    vHeight += SphereRadius + AbsoluteOffset;

                if (smoothFactor <= 0f)
                    return height;
                if (CullBlack && !(ha.Height > 0f))
                    return height;

                // Stock PQSMod_MapDecalTangent.Lerp(v2, v1, dt):
                // (v1 * dt) + (v2 * (1d - dt)). Do not replace with a+(b-a)t.
                double dt = (double)smoothFactor;
                return (vHeight * dt) + (height * (1d - dt));
            }
        }

        internal static HeightAlphaSample GetPixelHeightAlpha(
            AERIS39MapSoPureCpuExact.MapSnapshot map,
            float x,
            float y)
        {
            if (map == null) throw new ArgumentNullException("map");

            int minX;
            int maxX;
            int minY;
            int maxY;
            float midX;
            float midY;
            ConstructBilinearCoordsFloat(
                map,
                x,
                y,
                out minX,
                out maxX,
                out minY,
                out maxY,
                out midX,
                out midY);

            HeightAlphaSample lower = Lerp(
                GetPixelHeightAlpha(map, minX, minY),
                GetPixelHeightAlpha(map, maxX, minY),
                midX);
            HeightAlphaSample upper = Lerp(
                GetPixelHeightAlpha(map, minX, maxY),
                GetPixelHeightAlpha(map, maxX, maxY),
                midX);
            return Lerp(lower, upper, midY);
        }

        static void ConstructBilinearCoordsFloat(
            AERIS39MapSoPureCpuExact.MapSnapshot map,
            float x,
            float y,
            out int minX,
            out int maxX,
            out int minY,
            out int maxY,
            out float midX,
            out float midY)
        {
            // MapSO float overload. KSPCommunityFixes keeps longitude wrapping but
            // changes latitude from stock wrapping to [0, 0.99999] clamping.
            x = Math.Abs(x - (float)Math.Floor((double)x));
            if (map.Semantics ==
                AERIS39MapSoPureCpuExact.CoordinateSemantics.KspCommunityFixesWrapXClampY)
            {
                if (y < 0f) y = 0f;
                if (y > 0.99999f) y = 0.99999f;
            }
            else
            {
                y = Math.Abs(y - (float)Math.Floor((double)y));
            }

            float centerX = x * map.Width;
            minX = (int)Math.Floor((double)centerX);
            maxX = (int)Math.Ceiling((double)centerX);
            midX = centerX - minX;
            if (maxX == map.Width)
                maxX = 0;

            float centerY = y * map.Height;
            minY = (int)Math.Floor((double)centerY);
            maxY = (int)Math.Ceiling((double)centerY);
            midY = centerY - minY;

            if (map.Semantics ==
                AERIS39MapSoPureCpuExact.CoordinateSemantics.KspCommunityFixesWrapXClampY)
            {
                if (maxY >= map.Height)
                    maxY = map.Height - 1;
            }
            else if (maxY == map.Height)
            {
                maxY = 0;
            }
        }

        static HeightAlphaSample GetPixelHeightAlpha(
            AERIS39MapSoPureCpuExact.MapSnapshot map,
            int x,
            int y)
        {
            int index = checked((x * map.BytesPerPixel) + (y * map.RowWidth));
            const float byte2Float = 1f / 255f;

            float height = byte2Float * map.Data[index];
            float alpha;
            if (map.BytesPerPixel == 2)
                alpha = byte2Float * map.Data[index + 1];
            else if (map.BytesPerPixel == 4)
                alpha = byte2Float * map.Data[index + 3];
            else
                alpha = 1f;

            return new HeightAlphaSample(height, alpha);
        }

        static HeightAlphaSample Lerp(
            HeightAlphaSample a,
            HeightAlphaSample b,
            float dt)
        {
            return new HeightAlphaSample(
                a.Height + (b.Height - a.Height) * dt,
                a.Alpha + (b.Alpha - a.Alpha) * dt);
        }

        static float GetHeightSmoothing(
            float u,
            float v,
            float smoothHeight,
            float smoothHR,
            float smoothH1M)
        {
            float smoothU;
            if (u < smoothHeight)
                smoothU = u * smoothHR;
            else if (u > smoothH1M)
                smoothU = (1f - u) * smoothHR;
            else
                smoothU = 1f;

            float smoothV;
            if (v < smoothHeight)
                smoothV = v * smoothHR;
            else if (v > smoothH1M)
                smoothV = (1f - v) * smoothHR;
            else
                smoothV = 1f;

            return Math.Min(smoothU, smoothV);
        }

        static double Dot(
            double ax,
            double ay,
            double az,
            double bx,
            double by,
            double bz)
        {
            return ax * bx + ay * by + az * bz;
        }

        static void RotateQuaternionVector(
            float qx,
            float qy,
            float qz,
            float qw,
            float px,
            float py,
            float pz,
            out float rx,
            out float ry,
            out float rz)
        {
            // UnityEngine.Quaternion.operator *(Quaternion, Vector3), preserving its
            // managed binary32 source operation order.
            float num = qx * 2f;
            float num2 = qy * 2f;
            float num3 = qz * 2f;
            float num4 = qx * num;
            float num5 = qy * num2;
            float num6 = qz * num3;
            float num7 = qx * num2;
            float num8 = qx * num3;
            float num9 = qy * num3;
            float num10 = qw * num;
            float num11 = qw * num2;
            float num12 = qw * num3;

            rx = (1f - (num5 + num6)) * px +
                (num7 - num12) * py +
                (num8 + num11) * pz;
            ry = (num7 + num12) * px +
                (1f - (num4 + num6)) * py +
                (num9 - num10) * pz;
            rz = (num8 - num11) * px +
                (num9 + num10) * py +
                (1f - (num4 + num5)) * pz;
        }
    }
}
