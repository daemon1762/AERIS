#!/usr/bin/env python3
from pathlib import Path
p=Path('Source/AERISFlightControl/Terrain/AERISTerrainGpuTileRasterizer.cs')
s=p.read_text(encoding='utf-8')
start=s.index('        static float[] BuildContours(AERISTerrainHeightTile tile, float interval)')
end=s.index('        static bool HighDensityBoundaryCrossesParentCell(', start)
new='''        const int MaximumContourLevelsPerTile = 96;

        static float[] BuildContours(AERISTerrainHeightTile tile, float interval)
        {
            var output = new List<float>(tile.Resolution * tile.Resolution * 2);
            int resolution = tile.Resolution;
            int levelStride = ResolveContourLevelStride(tile, interval);
            var points = new float[6];
            for (int row = 0; row < resolution - 1; row++)
            {
                for (int column = 0; column < resolution - 1; column++)
                {
                    // Candidate11: keep contours in coastal parent cells. The line segment
                    // itself is clipped against the persisted 129x129 land/water mask.
                    bool coastalParent = HighDensityBoundaryCrossesParentCell(
                        tile, row, column);
                    int a = row * resolution + column;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    AppendTriangleContours(output, points, tile, interval,
                        levelStride, coastalParent,
                        column, row, a, column, row + 1, c,
                        column + 1, row, b);
                    AppendTriangleContours(output, points, tile, interval,
                        levelStride, coastalParent,
                        column + 1, row, b, column, row + 1, c,
                        column + 1, row + 1, d);
                }
            }
            return output.ToArray();
        }

        static int ResolveContourLevelStride(AERISTerrainHeightTile tile,
            float interval)
        {
            interval = Math.Max(1f, interval);
            float minimum = float.PositiveInfinity;
            float maximum = float.NegativeInfinity;
            if (tile == null || tile.Elevation == null || tile.Flags == null)
                return 1;
            int count = Math.Min(tile.Elevation.Length, tile.Flags.Length);
            for (int i = 0; i < count; i++)
            {
                if (tile.Flags[i] == 0 || tile.Flags[i] == 2 ||
                    !Finite(tile.Elevation[i])) continue;
                minimum = Math.Min(minimum, tile.Elevation[i]);
                maximum = Math.Max(maximum, tile.Elevation[i]);
            }
            if (!Finite(minimum) || !Finite(maximum) || maximum <= minimum)
                return 1;
            int first = (int)Math.Floor(minimum / interval) + 1;
            int last = (int)Math.Floor(maximum / interval);
            int levels = Math.Max(0, last - first + 1);
            return Math.Max(1, (int)Math.Ceiling(levels /
                (double)MaximumContourLevelsPerTile));
        }

        static int AlignContourLevel(int levelIndex, int stride)
        {
            if (stride <= 1) return levelIndex;
            int remainder = levelIndex % stride;
            if (remainder == 0) return levelIndex;
            if (remainder < 0) remainder += stride;
            return levelIndex + (stride - remainder);
        }

        static void AppendTriangleContours(List<float> output, float[] points,
            AERISTerrainHeightTile tile, float interval, int levelStride,
            bool coastalParent,
            int x0, int y0, int i0, int x1, int y1, int i1,
            int x2, int y2, int i2)
        {
            if (tile == null || output == null || points == null ||
                i0 < 0 || i1 < 0 || i2 < 0 ||
                i0 >= tile.Flags.Length || i1 >= tile.Flags.Length ||
                i2 >= tile.Flags.Length ||
                i0 >= tile.Elevation.Length || i1 >= tile.Elevation.Length ||
                i2 >= tile.Elevation.Length) return;
            if (tile.Flags[i0] == 0 || tile.Flags[i1] == 0 || tile.Flags[i2] == 0 ||
                tile.Flags[i0] == 2 || tile.Flags[i1] == 2 || tile.Flags[i2] == 2)
                return;
            float v0 = tile.Elevation[i0];
            float v1 = tile.Elevation[i1];
            float v2 = tile.Elevation[i2];
            if (!Finite(v0) || !Finite(v1) || !Finite(v2)) return;

            interval = Math.Max(1f, interval);
            float minimum = Math.Min(v0, Math.Min(v1, v2));
            float maximum = Math.Max(v0, Math.Max(v1, v2));
            int first = (int)Math.Floor(minimum / interval) + 1;
            int last = (int)Math.Floor(maximum / interval);
            int levelIndex = AlignContourLevel(first, levelStride);
            for (; levelIndex <= last; levelIndex += Math.Max(1, levelStride))
            {
                float level = levelIndex * interval;
                int pointCount = 0;
                AddCrossing(points, ref pointCount, x0, y0, x1, y1,
                    v0, v1, level, tile.Resolution);
                AddCrossing(points, ref pointCount, x1, y1, x2, y2,
                    v1, v2, level, tile.Resolution);
                AddCrossing(points, ref pointCount, x2, y2, x0, y0,
                    v2, v0, level, tile.Resolution);
                if (pointCount < 2) continue;
                AppendContourSegment(output, tile, coastalParent,
                    points[0], points[1], points[2], points[3]);
            }
        }

        static void AppendContourSegment(List<float> output,
            AERISTerrainHeightTile tile, bool coastalParent,
            float x0, float y0, float x1, float y1)
        {
            if (!coastalParent || tile == null ||
                tile.HighDensityCoastalFlags == null)
            {
                output.Add(x0); output.Add(y0); output.Add(x1); output.Add(y1);
                return;
            }
            int hd = tile.HighDensityCoastlineResolution;
            if (hd < 2 || tile.HighDensityCoastalFlags.Length != hd * hd)
                return;
            float dx = x1 - x0;
            float dy = y1 - y0;
            float hdSpan = Math.Max(Math.Abs(dx), Math.Abs(dy)) * (hd - 1);
            int pieces = Math.Max(2, Math.Min(16,
                (int)Math.Ceiling(hdSpan * 2f)));
            for (int piece = 0; piece < pieces; piece++)
            {
                float t0 = piece / (float)pieces;
                float t1 = (piece + 1) / (float)pieces;
                float tm = (t0 + t1) * 0.5f;
                float mx = x0 + dx * tm;
                float my = y0 + dy * tm;
                if (!HighDensityPointIsLand(tile, mx, my)) continue;
                output.Add(x0 + dx * t0); output.Add(y0 + dy * t0);
                output.Add(x0 + dx * t1); output.Add(y0 + dy * t1);
            }
        }

        static bool HighDensityPointIsLand(AERISTerrainHeightTile tile,
            float normalizedX, float normalizedY)
        {
            if (tile == null || tile.HighDensityCoastalFlags == null) return false;
            int hd = tile.HighDensityCoastlineResolution;
            if (hd < 2 || tile.HighDensityCoastalFlags.Length != hd * hd)
                return false;
            int column = Math.Max(0, Math.Min(hd - 1,
                (int)Math.Round(Clamp(normalizedX, 0f, 1f) * (hd - 1))));
            int row = Math.Max(0, Math.Min(hd - 1,
                (int)Math.Round(Clamp(normalizedY, 0f, 1f) * (hd - 1))));
            byte flag = tile.HighDensityCoastalFlags[row * hd + column];
            return flag != 0 && flag != 2;
        }

'''
s=s[:start]+new+s[end:]
p.write_text(s,encoding='utf-8')
print('Candidate11 contour patch applied')
