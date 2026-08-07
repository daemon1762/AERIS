using System;
using System.Collections.Generic;

namespace AERISFlightControl.Terrain
{
    // Pure-data coastline extraction shared by the normal render-ready worker and the
    // Candidate6 high-density preload refinement pass. Output uses normalized tile-local
    // segment pairs: x0,y0,x1,y1... and therefore remains independent of Unity objects.
    internal static class AERISTerrainCoastlineExtractor
    {
        internal const int HighDensityResolution = 129;
        internal const int HighDensityFormatVersion = 2;

        internal static bool ContainsLandWaterBoundary(AERISTerrainHeightTile tile)
        {
            if (tile == null || tile.Flags == null) return false;
            bool land = false;
            bool water = false;
            int count = Math.Min(tile.Flags.Length,
                Math.Max(0, tile.Resolution * tile.Resolution));
            for (int i = 0; i < count; i++)
            {
                if (tile.Flags[i] == 2) water = true;
                else if (tile.Flags[i] != 0) land = true;
                if (land && water) return true;
            }
            return false;
        }

        internal static bool HasCurrentHighDensityPayload(AERISTerrainHeightTile tile)
        {
            if (tile == null || tile.HighDensityCoastlineResolution != HighDensityResolution ||
                tile.HighDensityCoastlineSegments == null ||
                tile.HighDensityCoastalFlags == null) return false;
            int required = HighDensityResolution * HighDensityResolution;
            return tile.HighDensityCoastalFlags.Length == required;
        }

        internal static float[] Build(AERISTerrainHeightTile tile)
        {
            if (tile == null || tile.Resolution < 2 || tile.Elevation == null ||
                tile.Flags == null) return new float[0];
            int required = tile.Resolution * tile.Resolution;
            if (tile.Elevation.Length < required || tile.Flags.Length < required)
                return new float[0];

            var output = new List<float>(tile.Resolution * tile.Resolution * 2);
            int resolution = tile.Resolution;
            for (int row = 0; row < resolution - 1; row++)
            {
                for (int column = 0; column < resolution - 1; column++)
                {
                    int a = row * resolution + column;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    if (tile.Flags[a] == 0 || tile.Flags[b] == 0 ||
                        tile.Flags[c] == 0 || tile.Flags[d] == 0) continue;
                    AddTriangle(output,
                        column, row, tile.Flags[a] == 2, tile.Elevation[a],
                        column, row + 1, tile.Flags[c] == 2, tile.Elevation[c],
                        column + 1, row, tile.Flags[b] == 2, tile.Elevation[b],
                        resolution);
                    AddTriangle(output,
                        column + 1, row, tile.Flags[b] == 2, tile.Elevation[b],
                        column, row + 1, tile.Flags[c] == 2, tile.Elevation[c],
                        column + 1, row + 1, tile.Flags[d] == 2, tile.Elevation[d],
                        resolution);
                }
            }
            return output.ToArray();
        }

        // Operation Health Step 2: rebuild only the presentation line from the existing
        // persisted 129x129 class mask. The class topology and payload format are untouched;
        // this field merely moves crossings within already-crossing edges.
        internal static float[] BuildFromClassMask(byte[] flags, int resolution,
            float[] boundaryField)
        {
            if (flags == null || boundaryField == null || resolution < 2 ||
                flags.Length != resolution * resolution ||
                boundaryField.Length != flags.Length) return new float[0];
            var output = new List<float>(resolution * resolution * 2);
            for (int row = 0; row < resolution - 1; row++)
            {
                for (int column = 0; column < resolution - 1; column++)
                {
                    int a = row * resolution + column;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    if (flags[a] == 0 || flags[b] == 0 || flags[c] == 0 ||
                        flags[d] == 0) continue;
                    AddClassTriangle(output, column, row, flags[a] == 2,
                        boundaryField[a], column, row + 1, flags[c] == 2,
                        boundaryField[c], column + 1, row, flags[b] == 2,
                        boundaryField[b], resolution);
                    AddClassTriangle(output, column + 1, row, flags[b] == 2,
                        boundaryField[b], column, row + 1, flags[c] == 2,
                        boundaryField[c], column + 1, row + 1, flags[d] == 2,
                        boundaryField[d], resolution);
                }
            }
            return output.ToArray();
        }

        static void AddClassTriangle(List<float> output,
            int x0, int y0, bool water0, float scalar0,
            int x1, int y1, bool water1, float scalar1,
            int x2, int y2, bool water2, float scalar2, int resolution)
        {
            var points = new float[6];
            int pointCount = 0;
            AddClassCrossing(points, ref pointCount, x0, y0, x1, y1,
                water0, water1, scalar0, scalar1, resolution);
            AddClassCrossing(points, ref pointCount, x1, y1, x2, y2,
                water1, water2, scalar1, scalar2, resolution);
            AddClassCrossing(points, ref pointCount, x2, y2, x0, y0,
                water2, water0, scalar2, scalar0, resolution);
            if (pointCount != 2) return;
            output.Add(points[0]); output.Add(points[1]);
            output.Add(points[2]); output.Add(points[3]);
        }

        static void AddClassCrossing(float[] points, ref int pointCount,
            int x0, int y0, int x1, int y1, bool water0, bool water1,
            float scalar0, float scalar1, int resolution)
        {
            if (pointCount >= 3 || water0 == water1) return;
            float t = AERISTerrainCoastlinePolicy.PresentationCrossingFraction(
                water0, water1, scalar0, scalar1);
            points[pointCount * 2] =
                (x0 + (x1 - x0) * t) / (resolution - 1f);
            points[pointCount * 2 + 1] =
                (y0 + (y1 - y0) * t) / (resolution - 1f);
            pointCount++;
        }

        static void AddTriangle(List<float> output,
            int x0, int y0, bool water0, float elevation0,
            int x1, int y1, bool water1, float elevation1,
            int x2, int y2, bool water2, float elevation2, int resolution)
        {
            var points = new float[6];
            int pointCount = 0;
            AddCrossing(points, ref pointCount, x0, y0, x1, y1,
                water0, water1, elevation0, elevation1, resolution);
            AddCrossing(points, ref pointCount, x1, y1, x2, y2,
                water1, water2, elevation1, elevation2, resolution);
            AddCrossing(points, ref pointCount, x2, y2, x0, y0,
                water2, water0, elevation2, elevation0, resolution);
            if (pointCount != 2) return;
            output.Add(points[0]); output.Add(points[1]);
            output.Add(points[2]); output.Add(points[3]);
        }

        static void AddCrossing(float[] points, ref int pointCount,
            int x0, int y0, int x1, int y1, bool water0, bool water1,
            float elevation0, float elevation1, int resolution)
        {
            if (pointCount >= 3 || water0 == water1) return;
            float t = AERISTerrainCoastlinePolicy.CrossingFraction(water0, water1,
                elevation0, elevation1);
            points[pointCount * 2] = (x0 + (x1 - x0) * t) / (resolution - 1f);
            points[pointCount * 2 + 1] = (y0 + (y1 - y0) * t) / (resolution - 1f);
            pointCount++;
        }
    }
}
