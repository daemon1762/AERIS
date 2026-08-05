using System;
using System.Collections.Generic;
using System.Diagnostics;
using AERISFlightControl.Performance;

namespace AERISFlightControl.Terrain
{
    internal sealed class AERISTerrainGpuTileRasterRequest
    {
        internal int Generation;
        internal AERISTerrainHeightTile Tile;
        internal bool ContoursEnabled;
        internal bool ShadingEnabled;
        internal float ContourIntervalMeters;
        internal string StyleKey;
        internal AERISTerrainVirtualDetailProfile VirtualDetailProfile;
    }

    internal class AERISTerrainRenderReadyHeightField
    {
        internal int Generation;
        internal AERISTerrainTileKey Key;
        internal long TileCreatedUtcTicks;
        internal string StyleKey;
        internal int Resolution;
        internal double SouthLatitudeDeg;
        internal double NorthLatitudeDeg;
        internal double WestLongitudeDeg;
        internal double EastLongitudeDeg;
        internal float[] VertexX;
        internal float[] VertexY;
        internal float[] ElevationMeters;
        internal byte[] Water;
        internal byte[] Valid;
        internal byte[] Shade;
        internal int[] Triangles;
        internal float[] ContourSegments;
        internal float[] CoastlineSegments;
        internal int CoastlineResolution;
        internal float[] CoastalLandCorrectionVertices;
        internal float[] CoastalLandCorrectionElevationMeters;
        internal byte[] CoastalLandCorrectionShade;
        internal float[] CoastalWaterCorrectionVertices;
        internal int CoastalCorrectionParentCells;
        internal float MeshMilliseconds;
        internal float ContourMilliseconds;
        internal float WorkerMilliseconds;
        internal AERISTerrainVirtualDetailLevel VirtualDetailLevel;
        internal long LastUseSequence;
        internal bool ResidentTokenValid;
        internal AERISResidentCommitToken ResidentToken;

        internal long EstimatedBytes
        {
            get
            {
                long bytes = 384L;
                bytes += VertexX == null ? 0L : VertexX.LongLength * sizeof(float);
                bytes += VertexY == null ? 0L : VertexY.LongLength * sizeof(float);
                bytes += ElevationMeters == null ? 0L : ElevationMeters.LongLength * sizeof(float);
                bytes += Water == null ? 0L : Water.LongLength;
                bytes += Valid == null ? 0L : Valid.LongLength;
                bytes += Shade == null ? 0L : Shade.LongLength;
                bytes += Triangles == null ? 0L : Triangles.LongLength * sizeof(int);
                bytes += ContourSegments == null ? 0L : ContourSegments.LongLength * sizeof(float);
                bytes += CoastlineSegments == null ? 0L : CoastlineSegments.LongLength * sizeof(float);
                bytes += CoastalLandCorrectionVertices == null ? 0L : CoastalLandCorrectionVertices.LongLength * sizeof(float);
                bytes += CoastalLandCorrectionElevationMeters == null ? 0L : CoastalLandCorrectionElevationMeters.LongLength * sizeof(float);
                bytes += CoastalLandCorrectionShade == null ? 0L : CoastalLandCorrectionShade.LongLength;
                bytes += CoastalWaterCorrectionVertices == null ? 0L : CoastalWaterCorrectionVertices.LongLength * sizeof(float);
                return Math.Max(0L, bytes);
            }
        }
    }

    internal sealed class AERISTerrainGpuTileRasterResult : AERISTerrainRenderReadyHeightField { }

    internal sealed class AERISTerrainGpuTileRasterizer : IDisposable
    {
        // Candidate9 restores the documented Candidate8 safety contract. A 33x33 FAR
        // tile has 1024 coarse parent cells; 256 therefore caps sparse refinement at
        // one quarter of a tile and still prevents Candidate7-style whole-tile 129 fill.
        const int MaximumSparseCorrectionParentCells = 256;

        sealed class PendingState
        {
            internal int Generation;
            internal long CreatedUtcTicks;
            internal string StyleKey;
            internal string SchedulerKey;
            internal long EnqueuedTicks;
        }

        readonly object gate = new object();
        readonly Dictionary<string, PendingState> pending = new Dictionary<string, PendingState>(StringComparer.Ordinal);
        readonly Queue<AERISTerrainGpuTileRasterResult> completed = new Queue<AERISTerrainGpuTileRasterResult>();
        bool disposed;
        int dropped;
        int failures;

        internal int PendingCount { get { lock (gate) return pending.Count; } }
        internal int DroppedCount { get { lock (gate) return dropped; } }
        internal int FailureCount { get { lock (gate) return failures; } }

        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)
        {
            if (disposed || request == null || request.Tile == null || request.Tile.Elevation == null || request.Tile.Flags == null || string.IsNullOrEmpty(request.StyleKey)) return false;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return false;
            string tileId = request.Tile.Key.StableId;
            long createdUtcTicks = request.Tile.CreatedUtcTicks;
            lock (gate)
            {
                PendingState existing;
                if (pending.TryGetValue(tileId, out existing) && existing != null && existing.CreatedUtcTicks == createdUtcTicks && string.Equals(existing.StyleKey, request.StyleKey, StringComparison.Ordinal) && ElapsedSeconds(existing.EnqueuedTicks) < 10.0) return true;
                string pendingSchedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
                pending[tileId] = new PendingState { Generation = request.Generation, CreatedUtcTicks = createdUtcTicks, StyleKey = request.StyleKey, SchedulerKey = pendingSchedulerKey, EnqueuedTicks = Stopwatch.GetTimestamp() };
            }
            request.Tile = request.Tile.CloneImmutable();
            string schedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
            bool accepted = runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute, schedulerKey, runtime.CaptureStamp(), context =>
            {
                context.ThrowIfStale();
                AERISTerrainGpuTileRasterResult result = BuildMesh(request);
                context.ThrowIfStale();
                return result;
            }, value =>
            {
                AERISTerrainGpuTileRasterResult result = value as AERISTerrainGpuTileRasterResult;
                lock (gate)
                {
                    if (disposed) return;
                    PendingState newest;
                    if (!pending.TryGetValue(tileId, out newest) || newest == null || newest.Generation != request.Generation) { dropped++; return; }
                    pending.Remove(tileId);
                    if (result == null) { failures++; return; }
                    while (completed.Count >= 64) { completed.Dequeue(); dropped++; }
                    completed.Enqueue(result);
                }
            });
            if (!accepted)
            {
                lock (gate)
                {
                    PendingState newest;
                    if (pending.TryGetValue(tileId, out newest) && newest != null && newest.Generation == request.Generation) pending.Remove(tileId);
                    dropped++;
                }
            }
            return accepted;
        }

        internal void CancelAll()
        {
            var schedulerKeys = new List<string>();
            lock (gate)
            {
                foreach (PendingState state in pending.Values) if (state != null && !string.IsNullOrEmpty(state.SchedulerKey)) schedulerKeys.Add(state.SchedulerKey);
                pending.Clear(); completed.Clear();
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            for (int i = 0; i < schedulerKeys.Count; i++) runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute, schedulerKeys[i]);
        }

        internal int Drain(List<AERISTerrainGpuTileRasterResult> destination, int maximum)
        {
            if (destination == null || maximum <= 0) return 0;
            int count = 0;
            lock (gate) while (count < maximum && completed.Count > 0) { destination.Add(completed.Dequeue()); count++; }
            return count;
        }

        static AERISTerrainGpuTileRasterResult BuildMesh(AERISTerrainGpuTileRasterRequest request)
        {
            AERISTerrainHeightTile sourceTile = request.Tile;
            AERISTerrainHeightTile tile = AERISTerrainVirtualDetailPolicy.ReconstructFar(sourceTile, request.VirtualDetailProfile);
            int resolution = tile == null ? 0 : tile.Resolution;
            if (resolution < 2 || resolution > 257) return null;
            int count = resolution * resolution;
            if (tile.Elevation.Length < count || tile.Flags.Length < count) return null;
            bool highDensityBoundary = AERISTerrainCoastlineExtractor.HasCurrentHighDensityPayload(tile);
            Stopwatch watch = Stopwatch.StartNew();
            var x = new float[count]; var y = new float[count]; var elevationMeters = new float[count]; var water = new byte[count]; var valid = new byte[count]; var shade = new byte[count];
            double finalIntervals = Math.Max(1, AERISTerrainTileFormat.Resolution(tile.Key.Lod) - 1);
            double actualIntervals = Math.Max(1, resolution - 1);
            float cellMeters = (float)Math.Max(1.0, AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod) * finalIntervals / actualIntervals);
            for (int row = 0; row < resolution; row++) for (int column = 0; column < resolution; column++)
            {
                int index = row * resolution + column;
                x[index] = column / (float)(resolution - 1); y[index] = row / (float)(resolution - 1);
                float value = tile.Elevation[index]; bool isValid = tile.Flags[index] != 0 && Finite(value);
                valid[index] = isValid ? (byte)255 : (byte)0; water[index] = tile.Flags[index] == 2 ? (byte)1 : (byte)0; elevationMeters[index] = isValid ? value : 0f;
                shade[index] = isValid && request.ShadingEnabled && water[index] == 0 ? ResolveShade(tile, row, column, value, cellMeters) : (byte)255;
            }
            var triangles = new List<int>((resolution - 1) * (resolution - 1) * 6);
            for (int row = 0; row < resolution - 1; row++) for (int column = 0; column < resolution - 1; column++)
            {
                int a = row * resolution + column, b = a + 1, c = a + resolution, d = c + 1;
                if (valid[a] != 0 && valid[b] != 0 && valid[c] != 0) { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
                if (valid[b] != 0 && valid[c] != 0 && valid[d] != 0) { triangles.Add(b); triangles.Add(c); triangles.Add(d); }
            }
            float[] correctionLandXY = new float[0], correctionLandElevation = new float[0], correctionWaterXY = new float[0]; byte[] correctionLandShade = new byte[0]; int correctionParents = 0;
            if (highDensityBoundary) BuildSparseCoastalCorrections(tile, request.ShadingEnabled, out correctionLandXY, out correctionLandElevation, out correctionLandShade, out correctionWaterXY, out correctionParents);
            float meshMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            Stopwatch contourWatch = Stopwatch.StartNew();
            float[] contours = request.ContoursEnabled ? BuildContours(tile, Math.Max(25f, request.ContourIntervalMeters)) : new float[0];
            float[] coastlines = highDensityBoundary ? (float[])tile.HighDensityCoastlineSegments.Clone() : AERISTerrainCoastlineExtractor.Build(tile);
            contourWatch.Stop(); watch.Stop();
            return new AERISTerrainGpuTileRasterResult
            {
                Generation = request.Generation, Key = tile.Key, TileCreatedUtcTicks = tile.CreatedUtcTicks, StyleKey = request.StyleKey, Resolution = resolution,
                SouthLatitudeDeg = tile.SouthLatitudeDeg, NorthLatitudeDeg = tile.NorthLatitudeDeg, WestLongitudeDeg = tile.WestLongitudeDeg, EastLongitudeDeg = tile.EastLongitudeDeg,
                VertexX = x, VertexY = y, ElevationMeters = elevationMeters, Water = water, Valid = valid, Shade = shade, Triangles = triangles.ToArray(), ContourSegments = contours,
                CoastlineSegments = coastlines, CoastlineResolution = highDensityBoundary ? tile.HighDensityCoastlineResolution : tile.Resolution,
                CoastalLandCorrectionVertices = correctionLandXY, CoastalLandCorrectionElevationMeters = correctionLandElevation, CoastalLandCorrectionShade = correctionLandShade,
                CoastalWaterCorrectionVertices = correctionWaterXY, CoastalCorrectionParentCells = correctionParents, MeshMilliseconds = meshMilliseconds,
                ContourMilliseconds = (float)contourWatch.Elapsed.TotalMilliseconds, WorkerMilliseconds = (float)watch.Elapsed.TotalMilliseconds,
                VirtualDetailLevel = request.VirtualDetailProfile == null ? AERISTerrainVirtualDetailLevel.FarDirect : request.VirtualDetailProfile.Level
            };
        }

        struct CorrectionPoint { internal float X; internal float Y; internal byte ClassFlag; internal float Elevation; }

        static void BuildSparseCoastalCorrections(AERISTerrainHeightTile tile, bool shadingEnabled, out float[] landXY, out float[] landElevation, out byte[] landShade, out float[] waterXY, out int parentCellCount)
        {
            landXY = new float[0]; landElevation = new float[0]; landShade = new byte[0]; waterXY = new float[0]; parentCellCount = 0;
            if (tile == null || tile.HighDensityCoastalFlags == null || tile.HighDensityCoastlineResolution != AERISTerrainCoastlineExtractor.HighDensityResolution) return;
            int hd = tile.HighDensityCoastlineResolution, baseResolution = tile.Resolution;
            if (baseResolution < 2 || hd < 2 || (hd - 1) % (baseResolution - 1) != 0) return;
            int factor = (hd - 1) / (baseResolution - 1); if (factor <= 0) return;
            byte[] flags = tile.HighDensityCoastalFlags; if (flags.Length != hd * hd) return;
            int parentWidth = baseResolution - 1; var parents = new bool[parentWidth * parentWidth];
            for (int row = 0; row < hd - 1; row++) for (int column = 0; column < hd - 1; column++)
            {
                int a = row * hd + column, b = a + 1, c = a + hd, d = c + 1;
                byte fa = flags[a], fb = flags[b], fc = flags[c], fd = flags[d];
                if (fa == 0 || fb == 0 || fc == 0 || fd == 0) continue;
                bool wa = fa == 2, wb = fb == 2, wc = fc == 2, wd = fd == 2;
                if (wa == wb && wa == wc && wa == wd) continue;
                int pr = Math.Min(parentWidth - 1, row / factor), pc = Math.Min(parentWidth - 1, column / factor); parents[pr * parentWidth + pc] = true;
            }
            int detectedParents = 0; for (int i = 0; i < parents.Length; i++) if (parents[i]) detectedParents++;
            if (detectedParents <= 0 || detectedParents > MaximumSparseCorrectionParentCells) return;
            var land = new List<float>(4096); var water = new List<float>(4096); var landHeights = new List<float>(2048); var landShades = new List<byte>(2048);
            float baseCellMeters = (float)Math.Max(1.0, AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod));
            for (int pr = 0; pr < parentWidth; pr++) for (int pc = 0; pc < parentWidth; pc++)
            {
                if (!parents[pr * parentWidth + pc]) continue; parentCellCount++;
                int rowStart = pr * factor, columnStart = pc * factor;
                for (int sr = 0; sr < factor; sr++) for (int sc = 0; sc < factor; sc++)
                {
                    int row = rowStart + sr, column = columnStart + sc;
                    CorrectionPoint a = CorrectionSample(tile, flags, hd, row, column), b = CorrectionSample(tile, flags, hd, row, column + 1), c = CorrectionSample(tile, flags, hd, row + 1, column), d = CorrectionSample(tile, flags, hd, row + 1, column + 1);
                    if (a.ClassFlag == 0 || b.ClassFlag == 0 || c.ClassFlag == 0 || d.ClassFlag == 0) continue;
                    AppendCorrectionTriangle(tile, land, landHeights, landShades, water, a, c, b, shadingEnabled, baseCellMeters);
                    AppendCorrectionTriangle(tile, land, landHeights, landShades, water, b, c, d, shadingEnabled, baseCellMeters);
                }
            }
            landXY = land.ToArray(); landElevation = landHeights.ToArray(); landShade = landShades.ToArray(); waterXY = water.ToArray();
        }

        static CorrectionPoint CorrectionSample(AERISTerrainHeightTile tile, byte[] flags, int resolution, int row, int column)
        {
            int index = row * resolution + column; byte classFlag = flags[index]; float x = column / (float)(resolution - 1), y = row / (float)(resolution - 1);
            return new CorrectionPoint { X = x, Y = y, ClassFlag = classFlag, Elevation = classFlag == 0 ? 0f : SampleClassPreservingHeight(tile, x, y, classFlag) };
        }

        static void AppendCorrectionTriangle(AERISTerrainHeightTile tile, List<float> land, List<float> landHeights, List<byte> landShades, List<float> water, CorrectionPoint a, CorrectionPoint b, CorrectionPoint c, bool shadingEnabled, float baseCellMeters)
        {
            var input = new CorrectionPoint[] { a, b, c };
            AppendCorrectionPolygon(tile, land, landHeights, landShades, input, false, shadingEnabled, baseCellMeters);
            AppendCorrectionPolygon(tile, water, null, null, input, true, false, baseCellMeters);
        }

        static void AppendCorrectionPolygon(AERISTerrainHeightTile tile, List<float> output, List<float> elevations, List<byte> shades, CorrectionPoint[] input, bool targetWater, bool shadingEnabled, float baseCellMeters)
        {
            var clipped = new CorrectionPoint[6]; int count = 0;
            for (int i = 0; i < 3; i++)
            {
                CorrectionPoint current = input[i], next = input[(i + 1) % 3];
                bool currentInside = (current.ClassFlag == 2) == targetWater, nextInside = (next.ClassFlag == 2) == targetWater;
                if (currentInside) clipped[count++] = current;
                if (currentInside != nextInside) clipped[count++] = CorrectionCrossing(current, next, targetWater ? (byte)2 : (byte)1);
            }
            if (count < 3) return;
            for (int i = 1; i < count - 1; i++)
            {
                AppendCorrectionVertex(tile, output, elevations, shades, clipped[0], targetWater, shadingEnabled, baseCellMeters);
                AppendCorrectionVertex(tile, output, elevations, shades, clipped[i], targetWater, shadingEnabled, baseCellMeters);
                AppendCorrectionVertex(tile, output, elevations, shades, clipped[i + 1], targetWater, shadingEnabled, baseCellMeters);
            }
        }

        static CorrectionPoint CorrectionCrossing(CorrectionPoint a, CorrectionPoint b, byte targetClass)
        {
            float t = AERISTerrainCoastlinePolicy.CrossingFraction(a.ClassFlag == 2, b.ClassFlag == 2, a.Elevation, b.Elevation);
            return new CorrectionPoint { X = a.X + (b.X - a.X) * t, Y = a.Y + (b.Y - a.Y) * t, ClassFlag = targetClass, Elevation = a.Elevation + (b.Elevation - a.Elevation) * t };
        }

        static void AppendCorrectionVertex(AERISTerrainHeightTile tile, List<float> output, List<float> elevations, List<byte> shades, CorrectionPoint point, bool targetWater, bool shadingEnabled, float baseCellMeters)
        {
            output.Add(point.X); output.Add(point.Y); if (targetWater || elevations == null || shades == null) return;
            float elevation = SampleClassPreservingHeight(tile, point.X, point.Y, 1); elevations.Add(elevation);
            shades.Add(shadingEnabled ? ResolveShadeAtNormalized(tile, point.X, point.Y, elevation, baseCellMeters) : (byte)255);
        }

        static byte ResolveShadeAtNormalized(AERISTerrainHeightTile tile, float normalizedX, float normalizedY, float fallback, float cellMeters)
        {
            if (tile == null || tile.Resolution < 2) return 255; float step = 1f / Math.Max(1, tile.Resolution - 1);
            float west = SampleClassPreservingHeight(tile, Math.Max(0f, normalizedX - step), normalizedY, 1), east = SampleClassPreservingHeight(tile, Math.Min(1f, normalizedX + step), normalizedY, 1), south = SampleClassPreservingHeight(tile, normalizedX, Math.Max(0f, normalizedY - step), 1), north = SampleClassPreservingHeight(tile, normalizedX, Math.Min(1f, normalizedY + step), 1);
            if (!Finite(west)) west = fallback; if (!Finite(east)) east = fallback; if (!Finite(south)) south = fallback; if (!Finite(north)) north = fallback;
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f), ny = -(north - south) / Math.Max(2f, cellMeters * 2f), nz = 1f, inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); nx *= inverse; ny *= inverse; nz *= inverse;
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f), factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f); return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        static float SampleClassPreservingHeight(AERISTerrainHeightTile tile, float normalizedX, float normalizedY, byte classFlag)
        {
            if (tile == null || tile.Resolution < 2 || tile.Elevation == null || tile.Flags == null || classFlag == 0) return 0f;
            double sx = Math.Max(0.0, Math.Min(tile.Resolution - 1.0, normalizedX * (tile.Resolution - 1.0))), sy = Math.Max(0.0, Math.Min(tile.Resolution - 1.0, normalizedY * (tile.Resolution - 1.0)));
            int x0 = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Floor(sx))), y0 = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Floor(sy))), x1 = Math.Min(tile.Resolution - 1, x0 + 1), y1 = Math.Min(tile.Resolution - 1, y0 + 1);
            double fx = sx - x0, fy = sy - y0; bool targetWater = classFlag == 2, found = false; double sum = 0.0, weight = 0.0;
            AccumulateClassHeight(tile, y0 * tile.Resolution + x0, (1.0 - fx) * (1.0 - fy), targetWater, ref sum, ref weight); AccumulateClassHeight(tile, y0 * tile.Resolution + x1, fx * (1.0 - fy), targetWater, ref sum, ref weight); AccumulateClassHeight(tile, y1 * tile.Resolution + x0, (1.0 - fx) * fy, targetWater, ref sum, ref weight); AccumulateClassHeight(tile, y1 * tile.Resolution + x1, fx * fy, targetWater, ref sum, ref weight);
            if (weight > 0.000001) return (float)(sum / weight);
            int cx = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Round(sx))), cy = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Round(sy))); double bestDistance = double.MaxValue; float best = 0f;
            for (int radius = 0; radius <= 2; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = cx + dx, py = cy + dy; if (px < 0 || py < 0 || px >= tile.Resolution || py >= tile.Resolution) continue; int index = py * tile.Resolution + px;
                    if (tile.Flags[index] == 0 || (tile.Flags[index] == 2) != targetWater || !Finite(tile.Elevation[index])) continue; double distance = dx * dx + dy * dy; if (distance >= bestDistance) continue; bestDistance = distance; best = tile.Elevation[index]; found = true;
                }
                if (found) return best;
            }
            int fallback = cy * tile.Resolution + cx; return fallback >= 0 && fallback < tile.Elevation.Length && Finite(tile.Elevation[fallback]) ? tile.Elevation[fallback] : 0f;
        }

        static void AccumulateClassHeight(AERISTerrainHeightTile tile, int index, double sampleWeight, bool targetWater, ref double sum, ref double weight)
        {
            if (sampleWeight <= 0.0 || index < 0 || index >= tile.Elevation.Length || index >= tile.Flags.Length || tile.Flags[index] == 0 || (tile.Flags[index] == 2) != targetWater || !Finite(tile.Elevation[index])) return;
            sum += tile.Elevation[index] * sampleWeight; weight += sampleWeight;
        }

        static byte ResolveShade(AERISTerrainHeightTile tile, int row, int column, float fallback, float cellMeters)
        {
            float west = Sample(tile, row, Math.Max(0, column - 1), fallback), east = Sample(tile, row, Math.Min(tile.Resolution - 1, column + 1), fallback), south = Sample(tile, Math.Max(0, row - 1), column, fallback), north = Sample(tile, Math.Min(tile.Resolution - 1, row + 1), column, fallback);
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f), ny = -(north - south) / Math.Max(2f, cellMeters * 2f), nz = 1f, inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz); nx *= inverse; ny *= inverse; nz *= inverse;
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f), factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f); return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        const int MaximumContourLevelsPerTile = 96;

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

        static bool HighDensityBoundaryCrossesParentCell(
            AERISTerrainHeightTile tile, int parentRow, int parentColumn)
        {
            if (tile == null || tile.HighDensityCoastalFlags == null ||
                tile.HighDensityCoastlineResolution !=
                    AERISTerrainCoastlineExtractor.HighDensityResolution ||
                tile.Resolution < 2) return false;
            int hd = tile.HighDensityCoastlineResolution;
            if ((hd - 1) % (tile.Resolution - 1) != 0) return false;
            int factor = (hd - 1) / (tile.Resolution - 1);
            if (factor <= 0 || tile.HighDensityCoastalFlags.Length != hd * hd)
                return false;
            int rowStart = parentRow * factor;
            int columnStart = parentColumn * factor;
            byte[] flags = tile.HighDensityCoastalFlags;
            for (int sr = 0; sr < factor; sr++)
            {
                int row = rowStart + sr;
                for (int sc = 0; sc < factor; sc++)
                {
                    int column = columnStart + sc;
                    int a = row * hd + column;
                    int b = a + 1;
                    int c = a + hd;
                    int d = c + 1;
                    byte fa = flags[a], fb = flags[b], fc = flags[c], fd = flags[d];
                    if (fa == 0 || fb == 0 || fc == 0 || fd == 0) continue;
                    bool wa = fa == 2, wb = fb == 2, wc = fc == 2, wd = fd == 2;
                    if (!(wa == wb && wa == wc && wa == wd)) return true;
                }
            }
            return false;
        }


        static void AddCrossing(float[] points, ref int pointCount, int x0, int y0, int x1, int y1, float v0, float v1, float level, int resolution)
        {
            if (pointCount >= 4 || v0 == v1 || !((v0 <= level && v1 > level) || (v1 <= level && v0 > level))) return; float t = (level - v0) / (v1 - v0); points[pointCount * 2] = (x0 + (x1 - x0) * t) / (resolution - 1f); points[pointCount * 2 + 1] = (y0 + (y1 - y0) * t) / (resolution - 1f); pointCount++;
        }

        static float Sample(AERISTerrainHeightTile tile, int row, int column, float fallback) { int index = row * tile.Resolution + column; return index >= 0 && index < tile.Elevation.Length && tile.Flags[index] != 0 && Finite(tile.Elevation[index]) ? tile.Elevation[index] : fallback; }
        static double ElapsedSeconds(long startTicks) { if (startTicks <= 0L) return double.PositiveInfinity; return (Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency; }
        static float Clamp(float value, float minimum, float maximum) { return value < minimum ? minimum : value > maximum ? maximum : value; }
        static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

        public void Dispose() { if (disposed) return; disposed = true; lock (gate) { pending.Clear(); completed.Clear(); } }
    }
}
