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

    // Gate 4A CPU-authoritative render-ready height field. This immutable payload is
    // produced on the bounded GeneralCompute lane and may remain resident after every
    // Unity Mesh/RenderTexture has been released. It contains no UnityEngine.Object.
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
                return Math.Max(0L, bytes);
            }
        }
    }

    // Compatibility result name retained for the established bounded worker queue.
    internal sealed class AERISTerrainGpuTileRasterResult :
        AERISTerrainRenderReadyHeightField
    {
    }

    // Pure-data GPU mesh preparation. Height values remain explicit metres across the
    // worker boundary. The Unity main thread converts them to final TOPO/REL vertex
    // colours without relying on built-in-shader texture scale/offset behaviour. The
    // worker computes immutable topology, fixed-NW slope factors, land-only contours
    // and a dedicated coastline polyline on the bounded GeneralCompute lane.
    internal sealed class AERISTerrainGpuTileRasterizer : IDisposable
    {
        sealed class PendingState
        {
            internal int Generation;
            internal long CreatedUtcTicks;
            internal string StyleKey;
            internal string SchedulerKey;
            internal long EnqueuedTicks;
        }

        readonly object gate = new object();
        readonly Dictionary<string, PendingState> pending =
            new Dictionary<string, PendingState>(StringComparer.Ordinal);
        readonly Queue<AERISTerrainGpuTileRasterResult> completed =
            new Queue<AERISTerrainGpuTileRasterResult>();
        bool disposed;
        int dropped;
        int failures;

        internal int PendingCount { get { lock (gate) return pending.Count; } }
        internal int DroppedCount { get { lock (gate) return dropped; } }
        internal int FailureCount { get { lock (gate) return failures; } }

        internal bool Enqueue(AERISTerrainGpuTileRasterRequest request)
        {
            if (disposed || request == null || request.Tile == null ||
                request.Tile.Elevation == null || request.Tile.Flags == null ||
                string.IsNullOrEmpty(request.StyleKey)) return false;
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return false;
            string tileId = request.Tile.Key.StableId;
            long createdUtcTicks = request.Tile.CreatedUtcTicks;
            lock (gate)
            {
                PendingState existing;
                if (pending.TryGetValue(tileId, out existing) && existing != null &&
                    existing.CreatedUtcTicks == createdUtcTicks &&
                    string.Equals(existing.StyleKey, request.StyleKey, StringComparison.Ordinal) &&
                    ElapsedSeconds(existing.EnqueuedTicks) < 10.0) return true;
                string pendingSchedulerKey = "terrain-gpu-mesh:" +
                    request.Tile.Key.FileStem;
                pending[tileId] = new PendingState
                {
                    Generation = request.Generation,
                    CreatedUtcTicks = createdUtcTicks,
                    StyleKey = request.StyleKey,
                    SchedulerKey = pendingSchedulerKey,
                    EnqueuedTicks = Stopwatch.GetTimestamp()
                };
            }
            request.Tile = request.Tile.CloneImmutable();
            string schedulerKey = "terrain-gpu-mesh:" + request.Tile.Key.FileStem;
            bool accepted = runtime.Scheduler.SubmitLatest(AERISRuntimeLane.GeneralCompute,
                schedulerKey, runtime.CaptureStamp(), context =>
                {
                    context.ThrowIfStale();
                    AERISTerrainGpuTileRasterResult result = BuildMesh(request);
                    context.ThrowIfStale();
                    return result;
                }, value =>
                {
                    AERISTerrainGpuTileRasterResult result =
                        value as AERISTerrainGpuTileRasterResult;
                    lock (gate)
                    {
                        if (disposed) return;
                        PendingState newest;
                        if (!pending.TryGetValue(tileId, out newest) || newest == null ||
                            newest.Generation != request.Generation)
                        {
                            dropped++;
                            return;
                        }
                        pending.Remove(tileId);
                        if (result == null)
                        {
                            failures++;
                            return;
                        }
                        while (completed.Count >= 64)
                        {
                            completed.Dequeue();
                            dropped++;
                        }
                        completed.Enqueue(result);
                    }
                });
            if (!accepted)
            {
                lock (gate)
                {
                    PendingState newest;
                    if (pending.TryGetValue(tileId, out newest) && newest != null &&
                        newest.Generation == request.Generation) pending.Remove(tileId);
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
                foreach (PendingState state in pending.Values)
                    if (state != null && !string.IsNullOrEmpty(state.SchedulerKey))
                        schedulerKeys.Add(state.SchedulerKey);
                pending.Clear();
                completed.Clear();
            }
            AERISPerformanceRuntime runtime = AERISPerformanceRuntime.Current;
            if (runtime == null) return;
            for (int i = 0; i < schedulerKeys.Count; i++)
                runtime.Scheduler.CancelKey(AERISRuntimeLane.GeneralCompute,
                    schedulerKeys[i]);
        }

        internal int Drain(List<AERISTerrainGpuTileRasterResult> destination, int maximum)
        {
            if (destination == null || maximum <= 0) return 0;
            int count = 0;
            lock (gate)
            {
                while (count < maximum && completed.Count > 0)
                {
                    destination.Add(completed.Dequeue());
                    count++;
                }
            }
            return count;
        }

        static AERISTerrainGpuTileRasterResult BuildMesh(
            AERISTerrainGpuTileRasterRequest request)
        {
            AERISTerrainHeightTile sourceTile = request.Tile;
            AERISTerrainHeightTile tile = AERISTerrainVirtualDetailPolicy.ReconstructFar(
                sourceTile, request.VirtualDetailProfile);
            int baseResolution = tile == null ? 0 : tile.Resolution;
            if (baseResolution < 2 || baseResolution > 257) return null;
            int baseCount = baseResolution * baseResolution;
            if (tile.Elevation.Length < baseCount || tile.Flags.Length < baseCount) return null;

            bool highDensityBoundary =
                AERISTerrainCoastlineExtractor.HasCurrentHighDensityPayload(tile);
            int resolution = highDensityBoundary ?
                tile.HighDensityCoastlineResolution : baseResolution;
            int count = checked(resolution * resolution);

            Stopwatch watch = Stopwatch.StartNew();
            var x = new float[count];
            var y = new float[count];
            var elevationMeters = new float[count];
            var water = new byte[count];
            var valid = new byte[count];
            var shade = new byte[count];
            // Candidate7 keeps the authoritative elevation field at 33x33/17x17. On a
            // coastal tile only the land/water class grid is 129x129; display heights
            // are class-preserving interpolation of the low-resolution height field.
            // This gives the fill and coastline the same boundary density without
            // silently promoting terrain relief/contours to a new sampling authority.
            for (int row = 0; row < resolution; row++)
            {
                for (int column = 0; column < resolution; column++)
                {
                    int index = row * resolution + column;
                    float nx = column / (float)(resolution - 1);
                    float ny = row / (float)(resolution - 1);
                    x[index] = nx;
                    y[index] = ny;
                    byte classFlag;
                    float value;
                    if (highDensityBoundary)
                    {
                        classFlag = tile.HighDensityCoastalFlags[index];
                        value = SampleClassPreservingHeight(tile, nx, ny, classFlag);
                    }
                    else
                    {
                        int sourceIndex = row * baseResolution + column;
                        classFlag = tile.Flags[sourceIndex];
                        value = tile.Elevation[sourceIndex];
                    }
                    bool isValid = classFlag != 0 && Finite(value);
                    valid[index] = isValid ? (byte)255 : (byte)0;
                    water[index] = classFlag == 2 ? (byte)1 : (byte)0;
                    elevationMeters[index] = isValid ? value : 0f;
                }
            }

            // Preview/high-density boundary meshes span the same physical tile as the
            // base FAR tile. Scale cell metres by the actual display-grid intervals so
            // the fixed-NW relief shading remains spatially stable.
            double finalIntervals = Math.Max(1,
                AERISTerrainTileFormat.Resolution(tile.Key.Lod) - 1);
            double actualIntervals = Math.Max(1, resolution - 1);
            float cellMeters = (float)Math.Max(1.0,
                AERISTerrainTileFormat.NominalCellMeters(tile.Key.Lod) *
                finalIntervals / actualIntervals);
            for (int row = 0; row < resolution; row++)
                for (int column = 0; column < resolution; column++)
                {
                    int index = row * resolution + column;
                    if (valid[index] == 0 || water[index] != 0 || !request.ShadingEnabled)
                        shade[index] = 255;
                    else if (highDensityBoundary)
                        shade[index] = ResolveShadeGrid(elevationMeters, valid, resolution,
                            row, column, elevationMeters[index], cellMeters);
                    else
                        shade[index] = ResolveShade(tile, row, column,
                            elevationMeters[index], cellMeters);
                }

            var triangles = new List<int>((resolution - 1) * (resolution - 1) * 6);
            for (int row = 0; row < resolution - 1; row++)
            {
                for (int column = 0; column < resolution - 1; column++)
                {
                    int a = row * resolution + column;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    if (valid[a] != 0 && valid[b] != 0 && valid[c] != 0)
                    { triangles.Add(a); triangles.Add(c); triangles.Add(b); }
                    if (valid[b] != 0 && valid[c] != 0 && valid[d] != 0)
                    { triangles.Add(b); triangles.Add(c); triangles.Add(d); }
                }
            }

            float meshMilliseconds = (float)watch.Elapsed.TotalMilliseconds;
            Stopwatch contourWatch = Stopwatch.StartNew();
            float[] contours = request.ContoursEnabled ?
                BuildContours(tile, Math.Max(25f, request.ContourIntervalMeters)) :
                new float[0];
            float[] coastlines = highDensityBoundary ?
                (float[])tile.HighDensityCoastlineSegments.Clone() :
                AERISTerrainCoastlineExtractor.Build(tile);
            contourWatch.Stop();
            watch.Stop();
            return new AERISTerrainGpuTileRasterResult
            {
                Generation = request.Generation,
                Key = tile.Key,
                TileCreatedUtcTicks = tile.CreatedUtcTicks,
                StyleKey = request.StyleKey,
                Resolution = resolution,
                SouthLatitudeDeg = tile.SouthLatitudeDeg,
                NorthLatitudeDeg = tile.NorthLatitudeDeg,
                WestLongitudeDeg = tile.WestLongitudeDeg,
                EastLongitudeDeg = tile.EastLongitudeDeg,
                VertexX = x,
                VertexY = y,
                ElevationMeters = elevationMeters,
                Water = water,
                Valid = valid,
                Shade = shade,
                Triangles = triangles.ToArray(),
                ContourSegments = contours,
                CoastlineSegments = coastlines,
                CoastlineResolution = highDensityBoundary ?
                    tile.HighDensityCoastlineResolution : tile.Resolution,
                MeshMilliseconds = meshMilliseconds,
                ContourMilliseconds = (float)contourWatch.Elapsed.TotalMilliseconds,
                WorkerMilliseconds = (float)watch.Elapsed.TotalMilliseconds,
                VirtualDetailLevel = request.VirtualDetailProfile == null ?
                    AERISTerrainVirtualDetailLevel.FarDirect :
                    request.VirtualDetailProfile.Level
            };
        }

        static float SampleClassPreservingHeight(AERISTerrainHeightTile tile,
            float normalizedX, float normalizedY, byte classFlag)
        {
            if (tile == null || tile.Resolution < 2 || tile.Elevation == null ||
                tile.Flags == null || classFlag == 0) return 0f;
            double sx = Math.Max(0.0, Math.Min(tile.Resolution - 1.0,
                normalizedX * (tile.Resolution - 1.0)));
            double sy = Math.Max(0.0, Math.Min(tile.Resolution - 1.0,
                normalizedY * (tile.Resolution - 1.0)));
            int x0 = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Floor(sx)));
            int y0 = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Floor(sy)));
            int x1 = Math.Min(tile.Resolution - 1, x0 + 1);
            int y1 = Math.Min(tile.Resolution - 1, y0 + 1);
            double fx = sx - x0;
            double fy = sy - y0;
            bool targetWater = classFlag == 2;
            double sum = 0.0, weight = 0.0;
            AccumulateClassHeight(tile, y0 * tile.Resolution + x0,
                (1.0 - fx) * (1.0 - fy), targetWater, ref sum, ref weight);
            AccumulateClassHeight(tile, y0 * tile.Resolution + x1,
                fx * (1.0 - fy), targetWater, ref sum, ref weight);
            AccumulateClassHeight(tile, y1 * tile.Resolution + x0,
                (1.0 - fx) * fy, targetWater, ref sum, ref weight);
            AccumulateClassHeight(tile, y1 * tile.Resolution + x1,
                fx * fy, targetWater, ref sum, ref weight);
            if (weight > 0.000001) return (float)(sum / weight);

            int cx = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Round(sx)));
            int cy = Math.Max(0, Math.Min(tile.Resolution - 1, (int)Math.Round(sy)));
            double bestDistance = double.MaxValue;
            float best = 0f;
            bool found = false;
            for (int radius = 0; radius <= 2; radius++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        int px = cx + dx, py = cy + dy;
                        if (px < 0 || py < 0 || px >= tile.Resolution ||
                            py >= tile.Resolution) continue;
                        int index = py * tile.Resolution + px;
                        if (tile.Flags[index] == 0 ||
                            (tile.Flags[index] == 2) != targetWater ||
                            !Finite(tile.Elevation[index])) continue;
                        double distance = dx * dx + dy * dy;
                        if (distance >= bestDistance) continue;
                        bestDistance = distance;
                        best = tile.Elevation[index];
                        found = true;
                    }
                if (found) return best;
            }

            int fallback = cy * tile.Resolution + cx;
            return fallback >= 0 && fallback < tile.Elevation.Length &&
                Finite(tile.Elevation[fallback]) ? tile.Elevation[fallback] : 0f;
        }

        static void AccumulateClassHeight(AERISTerrainHeightTile tile, int index,
            double sampleWeight, bool targetWater, ref double sum, ref double weight)
        {
            if (sampleWeight <= 0.0 || index < 0 || index >= tile.Elevation.Length ||
                index >= tile.Flags.Length || tile.Flags[index] == 0 ||
                (tile.Flags[index] == 2) != targetWater ||
                !Finite(tile.Elevation[index])) return;
            sum += tile.Elevation[index] * sampleWeight;
            weight += sampleWeight;
        }

        static byte ResolveShadeGrid(float[] elevation, byte[] valid, int resolution,
            int row, int column, float fallback, float cellMeters)
        {
            int westIndex = row * resolution + Math.Max(0, column - 1);
            int eastIndex = row * resolution + Math.Min(resolution - 1, column + 1);
            int southIndex = Math.Max(0, row - 1) * resolution + column;
            int northIndex = Math.Min(resolution - 1, row + 1) * resolution + column;
            float west = valid[westIndex] != 0 ? elevation[westIndex] : fallback;
            float east = valid[eastIndex] != 0 ? elevation[eastIndex] : fallback;
            float south = valid[southIndex] != 0 ? elevation[southIndex] : fallback;
            float north = valid[northIndex] != 0 ? elevation[northIndex] : fallback;
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f);
            float ny = -(north - south) / Math.Max(2f, cellMeters * 2f);
            float nz = 1f;
            float inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inverse; ny *= inverse; nz *= inverse;
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f);
            float factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f);
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        static byte ResolveShade(AERISTerrainHeightTile tile, int row, int column,
            float fallback, float cellMeters)
        {
            float west = Sample(tile, row, Math.Max(0, column - 1), fallback);
            float east = Sample(tile, row, Math.Min(tile.Resolution - 1, column + 1), fallback);
            float south = Sample(tile, Math.Max(0, row - 1), column, fallback);
            float north = Sample(tile, Math.Min(tile.Resolution - 1, row + 1), column, fallback);
            float nx = -(east - west) / Math.Max(2f, cellMeters * 2f);
            float ny = -(north - south) / Math.Max(2f, cellMeters * 2f);
            float nz = 1f;
            float inverse = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx *= inverse; ny *= inverse; nz *= inverse;
            // Fixed north-west light. It is independent of sun, time of day and aircraft.
            float diffuse = Math.Max(0f, nx * -0.55f + ny * 0.55f + nz * 0.63f);
            float factor = Clamp(0.82f + diffuse * 0.20f, 0.82f, 1.04f);
            return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(factor * 227f)));
        }

        static float[] BuildContours(AERISTerrainHeightTile tile, float interval)
        {
            var output = new List<float>(tile.Resolution * tile.Resolution);
            var points = new float[8];
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
                        tile.Flags[c] == 0 || tile.Flags[d] == 0 ||
                        tile.Flags[a] == 2 || tile.Flags[b] == 2 ||
                        tile.Flags[c] == 2 || tile.Flags[d] == 2) continue;
                    float va = tile.Elevation[a], vb = tile.Elevation[b];
                    float vc = tile.Elevation[c], vd = tile.Elevation[d];
                    if (!Finite(va) || !Finite(vb) || !Finite(vc) || !Finite(vd)) continue;
                    float minimum = Math.Min(Math.Min(va, vb), Math.Min(vc, vd));
                    float maximum = Math.Max(Math.Max(va, vb), Math.Max(vc, vd));
                    int first = (int)Math.Floor(minimum / interval) + 1;
                    int last = (int)Math.Floor(maximum / interval);
                    int levels = Math.Min(16, Math.Max(0, last - first + 1));
                    for (int levelIndex = 0; levelIndex < levels; levelIndex++)
                    {
                        float level = (first + levelIndex) * interval;
                        int pointCount = 0;
                        AddCrossing(points, ref pointCount, column, row, column + 1, row,
                            va, vb, level, resolution);
                        AddCrossing(points, ref pointCount, column + 1, row, column + 1, row + 1,
                            vb, vd, level, resolution);
                        AddCrossing(points, ref pointCount, column + 1, row + 1, column, row + 1,
                            vd, vc, level, resolution);
                        AddCrossing(points, ref pointCount, column, row + 1, column, row,
                            vc, va, level, resolution);
                        if (pointCount >= 2)
                        {
                            output.Add(points[0]); output.Add(points[1]);
                            output.Add(points[2]); output.Add(points[3]);
                        }
                        if (pointCount >= 4)
                        {
                            output.Add(points[4]); output.Add(points[5]);
                            output.Add(points[6]); output.Add(points[7]);
                        }
                    }
                }
            }
            return output.ToArray();
        }


        static void AddCrossing(float[] points, ref int pointCount,
            int x0, int y0, int x1, int y1, float v0, float v1, float level,
            int resolution)
        {
            if (pointCount >= 4 || v0 == v1 ||
                !((v0 <= level && v1 > level) || (v1 <= level && v0 > level))) return;
            float t = (level - v0) / (v1 - v0);
            points[pointCount * 2] = (x0 + (x1 - x0) * t) / (resolution - 1f);
            points[pointCount * 2 + 1] = (y0 + (y1 - y0) * t) / (resolution - 1f);
            pointCount++;
        }

        static float Sample(AERISTerrainHeightTile tile, int row, int column,
            float fallback)
        {
            int index = row * tile.Resolution + column;
            return index >= 0 && index < tile.Elevation.Length && tile.Flags[index] != 0 &&
                Finite(tile.Elevation[index]) ? tile.Elevation[index] : fallback;
        }

        static double ElapsedSeconds(long startTicks)
        {
            if (startTicks <= 0L) return double.PositiveInfinity;
            return (Stopwatch.GetTimestamp() - startTicks) / (double)Stopwatch.Frequency;
        }

        static float Clamp(float value, float minimum, float maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        static bool Finite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lock (gate)
            {
                pending.Clear();
                completed.Clear();
            }
        }
    }
}
